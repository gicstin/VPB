using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Coroutine _layoutPresetSaveCo;
        private static readonly HashSet<string> s_layoutNameScratch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<GalleryLayoutPreset> s_layoutListScratch = new List<GalleryLayoutPreset>(16);

        /// <summary>Coalesced SQLite write — reorder drags and rapid edits must not hit disk per tick.</summary>
        internal void ScheduleLayoutPresetsSave()
        {
            InvalidateLayoutPresetCommandCatalogs();
            if (_layoutPresetSaveCo != null) return;
            if (!isActiveAndEnabled)
            {
                try { GalleryLayoutPresetStore.Save(); } catch { }
                return;
            }
            _layoutPresetSaveCo = StartCoroutine(LayoutPresetsSaveCo());
        }

        /// <summary>Preset rows are cached per pane; a change in one pane must not leave stale names in another.</summary>
        private static void InvalidateLayoutPresetCommandCatalogs()
        {
            try
            {
                if (Gallery.singleton == null || Gallery.singleton.Panels == null) return;
                List<GalleryPanel> panels = Gallery.singleton.Panels;
                for (int i = 0; i < panels.Count; i++)
                {
                    if (panels[i] != null) panels[i].InvalidateCommandPaletteCatalog();
                }
            }
            catch { }
        }

        private IEnumerator LayoutPresetsSaveCo()
        {
            yield return null;
            yield return null;
            try { GalleryLayoutPresetStore.Save(); }
            catch (Exception ex) { LogUtil.LogError("[VPB][Layout] preset save: " + ex.Message); }
            _layoutPresetSaveCo = null;
        }

        /// <summary>Captures the live arrangement as a new named preset for the running mode.</summary>
        internal GalleryLayoutPreset SaveCurrentLayoutAsPreset(string preferredName)
        {
            GalleryLayoutPreset preset;
            try { preset = CaptureCurrentLayout(""); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB][Layout] capture: " + ex.Message);
                return null;
            }

            string name = preferredName;
            if (string.IsNullOrEmpty(name))
                name = BuildSuggestedLayoutName(preset);

            GalleryLayoutPresetStore.CollectNames(s_layoutNameScratch);
            preset.Name = EnsureUniqueLayoutName(name, s_layoutNameScratch);

            GalleryLayoutPresetStore.Add(preset);
            GalleryLayoutPresetStore.MarkActive(preset);
            ScheduleLayoutPresetsSave();

            if (!GalleryLayoutPresetStore.SqlAvailable)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.status.layout_no_sql",
                    "Layout kept for this session only — preset storage is unavailable."), 3f);
            }
            else
            {
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.status.layout_preset_saved", "Saved layout: {0}"), preset.Name), 2f);
            }
            return preset;
        }

        /// <summary>Re-captures the live arrangement into an existing preset, keeping its identity.</summary>
        /// <summary>Shipped baselines are read-only; the manager offers Duplicate instead.</summary>
        private bool RejectBuiltInLayoutEdit(GalleryLayoutPreset preset)
        {
            if (preset == null || !preset.IsBuiltIn) return false;
            ShowTemporaryStatus(VPBTranslation.T(
                "gallery.status.layout_builtin_readonly",
                "Built-in layouts cannot be changed — duplicate it first."), 2.5f);
            return true;
        }

        internal bool UpdateLayoutPresetFromLive(GalleryLayoutPreset existing)
        {
            if (existing == null) return false;
            if (RejectBuiltInLayoutEdit(existing)) return false;
            if (existing.Mode != CurrentLayoutPresetMode())
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.status.layout_update_mode",
                    "That layout belongs to the other mode — cannot update it from here."), 2.5f);
                return false;
            }

            GalleryLayoutPreset fresh;
            try { fresh = CaptureCurrentLayout(existing.Name); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB][Layout] update capture: " + ex.Message);
                return false;
            }

            GalleryLayoutPresetStore.Replace(existing, fresh);
            ScheduleLayoutPresetsSave();
            ShowTemporaryStatus(string.Format(
                VPBTranslation.T("gallery.status.layout_preset_updated", "Updated layout: {0}"), fresh.Name), 2f);
            return true;
        }

        internal bool ApplyLayoutPresetById(int id)
        {
            GalleryLayoutPreset e = GalleryLayoutPresetStore.FindById(id);
            return ApplyNamedLayoutPreset(e);
        }

        internal bool ApplyNamedLayoutPreset(GalleryLayoutPreset preset)
        {
            if (preset == null) return false;

            GalleryLayoutPreset full = GalleryLayoutPresetStore.ResolvePayload(preset);
            if (full == null)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.status.layout_unreadable", "That layout could not be read."), 2.5f);
                return false;
            }

            if (!ApplyLayoutPreset(full, true)) return false;
            GalleryLayoutPresetStore.MarkActive(full);
            return true;
        }

        internal bool RenameLayoutPreset(GalleryLayoutPreset preset, string newName)
        {
            if (preset == null || string.IsNullOrEmpty(newName)) return false;
            if (RejectBuiltInLayoutEdit(preset)) return false;
            GalleryLayoutPresetStore.CollectNames(s_layoutNameScratch);
            s_layoutNameScratch.Remove(preset.Name ?? "");
            preset.Name = EnsureUniqueLayoutName(newName, s_layoutNameScratch);
            ScheduleLayoutPresetsSave();
            return true;
        }

        internal bool DuplicateLayoutPreset(GalleryLayoutPreset preset)
        {
            if (preset == null) return false;
            GalleryLayoutPreset full = GalleryLayoutPresetStore.ResolvePayload(preset);
            if (full == null) return false;

            GalleryLayoutPreset copy = GalleryLayoutPreset.FromJsonString(full.ToJsonString());
            if (copy == null) return false;

            copy.Id = 0;
            copy.Pinned = false;
            copy.PayloadLoaded = true;
            copy.IsBuiltIn = false;

            GalleryLayoutPresetStore.CollectNames(s_layoutNameScratch);
            copy.Name = EnsureUniqueLayoutName(full.Name ?? "Layout", s_layoutNameScratch);

            GalleryLayoutPresetStore.Add(copy);
            ScheduleLayoutPresetsSave();
            return true;
        }

        internal bool DeleteLayoutPreset(GalleryLayoutPreset preset)
        {
            if (preset == null) return false;
            if (RejectBuiltInLayoutEdit(preset)) return false;
            GalleryLayoutPresetStore.Remove(preset);
            ScheduleLayoutPresetsSave();
            ShowTemporaryStatus(string.Format(
                VPBTranslation.T("gallery.status.layout_preset_deleted", "Deleted layout: {0}"), preset.Name ?? ""), 2f);
            return true;
        }

        internal bool ToggleLayoutPresetPinned(GalleryLayoutPreset preset)
        {
            if (preset == null || preset.IsBuiltIn) return false;
            preset.Pinned = !preset.Pinned;
            ScheduleLayoutPresetsSave();
            return preset.Pinned;
        }

        /// <summary>True when the live arrangement drifted from the preset that was last applied.</summary>
        internal bool IsActiveLayoutPresetDirty()
        {
            if (GalleryLayoutPresetStore.ActiveId == 0) return false;
            try { return GalleryLayoutPresetStore.IsActiveDirty(CaptureCurrentLayout("")); }
            catch { return false; }
        }

        /// <summary>Applies the Nth preset of the running mode (hotkey slots).</summary>
        internal bool ApplyLayoutPresetBySlot(int slotIndex)
        {
            if (slotIndex < 0) return false;
            GalleryLayoutPresetStore.CollectForMode(CurrentLayoutPresetMode(), s_layoutListScratch);
            if (slotIndex >= s_layoutListScratch.Count) return false;
            return ApplyNamedLayoutPreset(s_layoutListScratch[slotIndex]);
        }

        internal void CollectLayoutPresetsForCurrentMode(List<GalleryLayoutPreset> into)
        {
            GalleryLayoutPresetStore.CollectForMode(CurrentLayoutPresetMode(), into);
        }

        internal void CollectRecentLayoutPresets(List<GalleryLayoutPreset> into)
        {
            GalleryLayoutPresetStore.CollectRecent(CurrentLayoutPresetMode(), into);
        }
    }
}
