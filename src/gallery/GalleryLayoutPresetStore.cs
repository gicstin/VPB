using System;
using System.Collections.Generic;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// Owns the named layout presets: lazy load, CRUD, ordering, recents and the active-preset marker.
    /// Writes are coalesced by <see cref="GalleryPanel"/> so a reorder drag never hits SQLite per tick.
    /// </summary>
    internal static class GalleryLayoutPresetStore
    {
        private static readonly List<GalleryLayoutPreset> s_presets = new List<GalleryLayoutPreset>();
        private static readonly List<int> s_recentIds = new List<int>(RecentMax);
        private static bool s_loaded;
        private static bool s_sqlAvailable = true;
        private static int s_activeId;
        private static string s_activeSignature;
        private static bool s_activeDockShapeOnly;

        internal const int RecentMax = 5;

        internal static bool SqlAvailable
        {
            get { return s_sqlAvailable; }
        }

        internal static int ActiveId
        {
            get { return s_activeId; }
        }

        internal static List<GalleryLayoutPreset> All
        {
            get { EnsureLoaded(); return s_presets; }
        }

        /// <summary>
        /// Loads with payloads: the manager draws every listed row's arrangement, so a lazy list
        /// would just turn into one SQLite round trip per visible row on first open.
        /// </summary>
        internal static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            try { s_sqlAvailable = VpbLocalDatabase.TryLoadLayoutPresets(s_presets, true); }
            catch { s_sqlAvailable = false; }
            // Appended last so a user's own presets always sort above the shipped baselines.
            try { GalleryLayoutPresetDefaults.Append(s_presets); }
            catch { }
        }

        internal static void Reload()
        {
            s_loaded = false;
            s_presets.Clear();
            EnsureLoaded();
        }

        /// <summary>Presets for one interaction mode, in sort order. Reuses <paramref name="into"/>.</summary>
        internal static void CollectForMode(int mode, List<GalleryLayoutPreset> into)
        {
            if (into == null) return;
            into.Clear();
            EnsureLoaded();
            for (int i = 0; i < s_presets.Count; i++)
            {
                GalleryLayoutPreset e = s_presets[i];
                if (e != null && e.Mode == mode) into.Add(e);
            }
        }

        /// <summary>Name / category / dock-shape match. Empty query matches everything.</summary>
        internal static bool MatchesSearch(GalleryLayoutPreset e, string query)
        {
            if (e == null) return false;
            if (string.IsNullOrEmpty(query)) return true;

            if (!string.IsNullOrEmpty(e.Name)
                && e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!e.PayloadLoaded || e.Panes == null) return false;

            for (int i = 0; i < e.Panes.Count; i++)
            {
                LayoutPaneState p = e.Panes[i];
                if (p == null) continue;
                if (!string.IsNullOrEmpty(p.CategoryTitle)
                    && p.CategoryTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                string side = (GalleryDockSide)p.DockSlot == GalleryDockSide.None
                    ? "float"
                    : GalleryDockLayout.ToConfigString((GalleryDockSide)p.DockSlot);
                if (side.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        internal static GalleryLayoutPreset FindById(int id)
        {
            if (id <= 0) return null;
            EnsureLoaded();
            for (int i = 0; i < s_presets.Count; i++)
            {
                if (s_presets[i] != null && s_presets[i].Id == id) return s_presets[i];
            }
            return null;
        }

        /// <summary>Returns the preset with its payload guaranteed present, pulling it from SQLite on demand.</summary>
        internal static GalleryLayoutPreset ResolvePayload(GalleryLayoutPreset e)
        {
            if (e == null) return null;
            if (e.PayloadLoaded) return e;
            if (e.Id <= 0) return e;

            GalleryLayoutPreset full = null;
            try { full = VpbLocalDatabase.TryLoadLayoutPresetPayload(e.Id); }
            catch { full = null; }
            if (full == null) return null;

            int idx = s_presets.IndexOf(e);
            if (idx >= 0) s_presets[idx] = full;
            return full;
        }

        internal static void CollectNames(HashSet<string> into)
        {
            if (into == null) return;
            into.Clear();
            EnsureLoaded();
            for (int i = 0; i < s_presets.Count; i++)
            {
                if (s_presets[i] != null && !string.IsNullOrEmpty(s_presets[i].Name))
                    into.Add(s_presets[i].Name);
            }
        }

        internal static GalleryLayoutPreset Add(GalleryLayoutPreset preset)
        {
            if (preset == null) return null;
            EnsureLoaded();
            preset.PayloadLoaded = true;
            preset.UpdatedUtc = DateTime.UtcNow.ToBinary();
            s_presets.Add(preset);
            return preset;
        }

        internal static void Replace(GalleryLayoutPreset existing, GalleryLayoutPreset fresh)
        {
            if (existing == null || fresh == null || existing.IsBuiltIn) return;
            EnsureLoaded();
            int idx = s_presets.IndexOf(existing);
            if (idx < 0) return;

            fresh.Id = existing.Id;
            fresh.Name = existing.Name;
            fresh.Pinned = existing.Pinned;
            fresh.SortOrder = existing.SortOrder;
            fresh.ButtonColor = existing.ButtonColor;
            fresh.PayloadLoaded = true;
            fresh.UpdatedUtc = DateTime.UtcNow.ToBinary();
            s_presets[idx] = fresh;

            if (s_activeId == fresh.Id) MarkActive(fresh);
        }

        internal static void Remove(GalleryLayoutPreset preset)
        {
            if (preset == null || preset.IsBuiltIn) return;
            EnsureLoaded();
            s_presets.Remove(preset);
            s_recentIds.Remove(preset.Id);
            if (s_activeId == preset.Id) ClearActive();
        }

        internal static void Move(GalleryLayoutPreset preset, int newIndex)
        {
            if (preset == null || preset.IsBuiltIn) return;
            EnsureLoaded();
            int idx = s_presets.IndexOf(preset);
            if (idx < 0) return;
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= s_presets.Count) newIndex = s_presets.Count - 1;
            if (newIndex == idx) return;

            s_presets.RemoveAt(idx);
            s_presets.Insert(newIndex, preset);
        }

        internal static bool Save()
        {
            EnsureLoaded();
            for (int i = 0; i < s_presets.Count; i++)
            {
                if (s_presets[i] != null) s_presets[i].SortOrder = i;
            }
            try
            {
                s_sqlAvailable = VpbLocalDatabase.TrySaveLayoutPresets(s_presets);
                return s_sqlAvailable;
            }
            catch { s_sqlAvailable = false; return false; }
        }

        internal static void MarkActive(GalleryLayoutPreset preset)
        {
            if (preset == null) { ClearActive(); return; }
            s_activeId = preset.Id;
            s_activeDockShapeOnly = preset.DockShapeOnly;
            try { s_activeSignature = GalleryLayoutPreset.BuildContentSignature(preset); }
            catch { s_activeSignature = null; }
            PushRecent(preset.Id);
        }

        internal static void ClearActive()
        {
            s_activeId = 0;
            s_activeSignature = null;
            s_activeDockShapeOnly = false;
        }

        /// <summary>True when the live arrangement has drifted from the preset that was last applied.</summary>
        internal static bool IsActiveDirty(GalleryLayoutPreset liveSnapshot)
        {
            if (s_activeId == 0 || string.IsNullOrEmpty(s_activeSignature)) return false;
            if (liveSnapshot == null) return false;

            // Compare on the same terms the active preset restores on, or a dock-shape preset would
            // read as drifted the instant any unrelated setting moved.
            bool restore = liveSnapshot.DockShapeOnly;
            liveSnapshot.DockShapeOnly = s_activeDockShapeOnly;
            string live;
            try { live = GalleryLayoutPreset.BuildContentSignature(liveSnapshot); }
            catch { return false; }
            finally { liveSnapshot.DockShapeOnly = restore; }
            return !string.Equals(live, s_activeSignature, StringComparison.Ordinal);
        }

        private static void PushRecent(int id)
        {
            if (id <= 0) return;
            s_recentIds.Remove(id);
            s_recentIds.Insert(0, id);
            while (s_recentIds.Count > RecentMax) s_recentIds.RemoveAt(s_recentIds.Count - 1);
        }

        /// <summary>Most recently applied presets for the given mode, newest first.</summary>
        internal static void CollectRecent(int mode, List<GalleryLayoutPreset> into)
        {
            if (into == null) return;
            into.Clear();
            EnsureLoaded();
            for (int i = 0; i < s_recentIds.Count; i++)
            {
                GalleryLayoutPreset e = FindById(s_recentIds[i]);
                if (e != null && e.Mode == mode) into.Add(e);
            }
        }

        /// <summary>Statics survive scene loads; clear on plugin teardown so nothing dangles.</summary>
        internal static void ResetForTeardown()
        {
            s_presets.Clear();
            s_recentIds.Clear();
            s_loaded = false;
            s_activeId = 0;
            s_activeSignature = null;
            s_activeDockShapeOnly = false;
            s_sqlAvailable = true;
        }
    }
}
