using System;
using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static bool s_defaultQuickFilterAppliedThisSession;

        internal static bool IsDefaultQuickFilter(QuickFilterEntry entry)
        {
            if (entry == null) return false;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null || !cfg.HasDefaultFilterPreset) return false;
            if (cfg.GalleryDefaultFilterPresetId > 0 && entry.Id > 0)
                return entry.Id == cfg.GalleryDefaultFilterPresetId;
            return !string.IsNullOrEmpty(entry.Name)
                && string.Equals(entry.Name, cfg.GalleryDefaultFilterPresetName, StringComparison.OrdinalIgnoreCase);
        }

        internal void ToggleDefaultQuickFilter(QuickFilterEntry entry)
        {
            if (entry == null) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;

            if (IsDefaultQuickFilter(entry))
            {
                cfg.GalleryDefaultFilterPresetId = 0;
                cfg.GalleryDefaultFilterPresetName = "";
                try { cfg.Save(false); } catch { }
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("quickfilters.default_cleared", "'{0}' no longer opens on start"),
                        entry.Name ?? ""),
                    2.5f);
                return;
            }

            if (entry.Id <= 0)
            {
                try { QuickFilterSettings.Instance.Save(); } catch { }
            }

            cfg.GalleryDefaultFilterPresetId = entry.Id > 0 ? entry.Id : 0;
            cfg.GalleryDefaultFilterPresetName = entry.Name ?? "";
            try { cfg.Save(false); } catch { }

            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("quickfilters.default_set", "'{0}' opens on VaM start"),
                    entry.Name ?? ""),
                2.5f);
        }

        internal static void ClearDefaultQuickFilterIfMatches(QuickFilterEntry entry)
        {
            if (!IsDefaultQuickFilter(entry)) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            cfg.GalleryDefaultFilterPresetId = 0;
            cfg.GalleryDefaultFilterPresetName = "";
            try { cfg.Save(false); } catch { }
        }

        internal static void SyncDefaultQuickFilterName(QuickFilterEntry entry)
        {
            if (!IsDefaultQuickFilter(entry)) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            string name = entry.Name ?? "";
            if (string.Equals(cfg.GalleryDefaultFilterPresetName, name, StringComparison.Ordinal)) return;
            cfg.GalleryDefaultFilterPresetName = name;
            try { cfg.Save(false); } catch { }
        }

        internal static QuickFilterEntry ResolveDefaultQuickFilter()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null || !cfg.HasDefaultFilterPreset) return null;
            try
            {
                List<QuickFilterEntry> filters = QuickFilterSettings.Instance != null
                    ? QuickFilterSettings.Instance.Filters
                    : null;
                if (filters == null) return null;

                if (cfg.GalleryDefaultFilterPresetId > 0)
                {
                    for (int i = 0; i < filters.Count; i++)
                    {
                        QuickFilterEntry e = filters[i];
                        if (e != null && e.Id == cfg.GalleryDefaultFilterPresetId) return e;
                    }
                }
                if (!string.IsNullOrEmpty(cfg.GalleryDefaultFilterPresetName))
                {
                    for (int i = 0; i < filters.Count; i++)
                    {
                        QuickFilterEntry e = filters[i];
                        if (e != null && string.Equals(e.Name, cfg.GalleryDefaultFilterPresetName, StringComparison.OrdinalIgnoreCase))
                            return e;
                    }
                }
            }
            catch { }
            return null;
        }

        internal static string DefaultQuickFilterDisplayName()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null || !cfg.HasDefaultFilterPreset) return "";
            QuickFilterEntry e = ResolveDefaultQuickFilter();
            if (e != null && !string.IsNullOrEmpty(e.Name)) return e.Name;
            return cfg.GalleryDefaultFilterPresetName ?? "";
        }

        internal void ApplyDefaultQuickFilterOnColdStart()
        {
            if (s_defaultQuickFilterAppliedThisSession) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null || !cfg.GalleryApplyDefaultFilterPresetOnStart || !cfg.HasDefaultFilterPreset) return;

            s_defaultQuickFilterAppliedThisSession = true;

            QuickFilterEntry entry = ResolveDefaultQuickFilter();
            if (entry == null)
            {
                LogUtil.LogWarning("[VPB] Default filter preset not found: "
                    + (cfg.GalleryDefaultFilterPresetName ?? "")
                    + " (id=" + cfg.GalleryDefaultFilterPresetId + ")");
                return;
            }

            try { ApplyQuickFilterState(entry, announce: false); }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Default filter preset apply failed: " + ex.Message);
                return;
            }

            try
            {
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("quickfilters.default_applied", "Opened default filter '{0}'"),
                        entry.Name ?? ""),
                    2.5f);
            }
            catch { }
        }
    }
}
