using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>
        /// Produces a NEW preset in the other mode; the source is never touched. The translation is
        /// lossy by nature — a screen-anchor ratio and a metre pose describe different things — so it
        /// is explicit and named rather than something apply does silently.
        /// </summary>
        internal GalleryLayoutPreset ConvertLayoutPresetToOtherMode(GalleryLayoutPreset source)
        {
            if (source == null) return null;

            GalleryLayoutPreset full = GalleryLayoutPresetStore.ResolvePayload(source);
            if (full == null)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.status.layout_unreadable", "That layout could not be read."), 2.5f);
                return null;
            }

            GalleryLayoutPreset copy = GalleryLayoutPreset.FromJsonString(full.ToJsonString());
            if (copy == null) return null;

            bool toVr = !full.IsVrPreset;
            copy.Id = 0;
            copy.Pinned = false;
            copy.PayloadLoaded = true;
            copy.IsBuiltIn = false;
            copy.Mode = toVr ? (int)LayoutPresetMode.VR : (int)LayoutPresetMode.Desktop;

            if (toVr) ConvertPanesToVr(copy);
            else ConvertPanesToDesktop(copy);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GalleryLayoutPresetStore.CollectNames(names);
            string suffix = toVr ? " (VR)" : " (Desktop)";
            copy.Name = EnsureUniqueLayoutName((full.Name ?? "Layout") + suffix, names);

            GalleryLayoutPresetStore.Add(copy);
            ScheduleLayoutPresetsSave();

            ShowTemporaryStatus(string.Format(
                VPBTranslation.T("gallery.status.layout_converted", "Created {0}"), copy.Name), 2.5f);
            return copy;
        }

        /// <summary>VR has no docks: every pane floats, fanned out from the default viewing pose.</summary>
        private static void ConvertPanesToVr(GalleryLayoutPreset preset)
        {
            if (preset.Panes == null) return;

            const float dist = 1.1f;
            const float stepDeg = 34f;

            for (int i = 0; i < preset.Panes.Count; i++)
            {
                LayoutPaneState p = preset.Panes[i];
                if (p == null) continue;

                p.DockSlot = (int)GalleryDockSide.None;
                p.Collapsed = false;

                // Fan alternately right/left of centre so pane 0 stays straight ahead.
                int rank = (i + 1) / 2;
                float sign = (i % 2 == 0) ? 1f : -1f;
                float yawDeg = rank * stepDeg * sign;
                float yaw = yawDeg * Mathf.Deg2Rad;

                p.LocalPos = new Vector3(Mathf.Sin(yaw) * dist, 0f, Mathf.Cos(yaw) * dist);
                p.LocalRot = Quaternion.Euler(0f, yawDeg, 0f);
                if (p.SizeRef.x < 100f || p.SizeRef.y < 100f)
                    p.SizeRef = new Vector2(1200f, 800f);
            }

            if (preset.Global != null)
            {
                preset.Global.DesktopFixedMode = false;
                preset.Global.DockLeft = new LayoutDockSlotState();
                preset.Global.DockTop = new LayoutDockSlotState();
                preset.Global.DockRight = new LayoutDockSlotState();
            }
        }

        /// <summary>Desktop: fill the three edges in default order, then leave the rest floating.</summary>
        private static void ConvertPanesToDesktop(GalleryLayoutPreset preset)
        {
            if (preset.Panes == null) return;

            GalleryDockSide preferred = GalleryDockSide.Right;
            if (preset.Global != null)
                preferred = GalleryDockLayout.Parse(preset.Global.DefaultDockSide);

            var order = new List<GalleryDockSide>(3);
            order.Add(preferred);
            if (preferred != GalleryDockSide.Right) order.Add(GalleryDockSide.Right);
            if (preferred != GalleryDockSide.Left) order.Add(GalleryDockSide.Left);
            if (preferred != GalleryDockSide.Top) order.Add(GalleryDockSide.Top);

            int next = 0;
            for (int i = 0; i < preset.Panes.Count; i++)
            {
                LayoutPaneState p = preset.Panes[i];
                if (p == null) continue;

                if (next < order.Count)
                {
                    p.DockSlot = (int)order[next];
                    next++;
                }
                else
                {
                    p.DockSlot = (int)GalleryDockSide.None;
                    if (p.SizeRef.x < 100f || p.SizeRef.y < 100f)
                        p.SizeRef = new Vector2(1200f, 800f);
                }
                p.Collapsed = false;
            }

            if (preset.Global == null) return;
            preset.Global.DesktopFixedMode = next > 0;
            for (int i = 0; i < order.Count && i < next; i++)
            {
                LayoutDockSlotState slot = LayoutDockSlotFor(preset.Global, order[i]);
                if (slot != null) slot.Occupied = true;
            }
        }

        private static LayoutDockSlotState LayoutDockSlotFor(LayoutGlobalState g, GalleryDockSide side)
        {
            if (g == null) return null;
            if (side == GalleryDockSide.Left) return g.DockLeft;
            if (side == GalleryDockSide.Top) return g.DockTop;
            if (side == GalleryDockSide.Right) return g.DockRight;
            return null;
        }

        private static string LayoutPresetExportDir
        {
            get
            {
                string baseDir = Directory.GetCurrentDirectory();
                return Path.Combine(Path.Combine(Path.Combine(baseDir, "Saves"), "PluginData"), "VPB");
            }
        }

        /// <summary>Writes one preset as shareable JSON next to VPB.cfg.</summary>
        internal bool ExportLayoutPreset(GalleryLayoutPreset preset)
        {
            if (preset == null) return false;

            GalleryLayoutPreset full = GalleryLayoutPresetStore.ResolvePayload(preset);
            if (full == null) return false;

            try
            {
                string dir = Path.Combine(LayoutPresetExportDir, "Layouts");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string file = Path.Combine(dir, SanitizeLayoutFileName(full.Name) + ".vpblayout.json");
                File.WriteAllText(file, full.ToJsonString());

                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.status.layout_exported", "Exported to {0}"), file), 4f);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB][Layout] export: " + ex.Message);
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.status.layout_export_failed", "Could not write the layout file."), 3f);
                return false;
            }
        }

        /// <summary>Imports every *.vpblayout.json found in the export folder. Names are de-duplicated.</summary>
        internal int ImportLayoutPresets()
        {
            int added = 0;
            try
            {
                string dir = Path.Combine(LayoutPresetExportDir, "Layouts");
                if (!Directory.Exists(dir))
                {
                    ShowTemporaryStatus(string.Format(
                        VPBTranslation.T("gallery.status.layout_import_none", "No layouts found in {0}"), dir), 4f);
                    return 0;
                }

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                GalleryLayoutPresetStore.CollectNames(names);

                string[] files = Directory.GetFiles(dir, "*.vpblayout.json");
                for (int i = 0; i < files.Length; i++)
                {
                    GalleryLayoutPreset e = null;
                    try { e = GalleryLayoutPreset.FromJsonString(File.ReadAllText(files[i])); }
                    catch (Exception ex) { LogUtil.LogWarning("[VPB][Layout] import " + files[i] + ": " + ex.Message); }
                    if (e == null) continue;

                    e.Id = 0;
                    e.Pinned = false;
                    e.PayloadLoaded = true;
                    if (string.IsNullOrEmpty(e.Name))
                        e.Name = Path.GetFileNameWithoutExtension(files[i]);
                    e.Name = EnsureUniqueLayoutName(e.Name, names);
                    names.Add(e.Name);

                    GalleryLayoutPresetStore.Add(e);
                    added++;
                }

                if (added > 0) ScheduleLayoutPresetsSave();

                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.status.layout_imported", "Imported {0} layout(s)"), added), 3f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB][Layout] import: " + ex.Message);
            }
            return added;
        }

        private static string SanitizeLayoutFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "layout";
            char[] bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = true;
                for (int b = 0; b < bad.Length; b++)
                {
                    if (c == bad[b]) { ok = false; break; }
                }
                sb.Append(ok ? c : '_');
            }
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "layout" : result;
        }
    }
}
