using System;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>Non-title-bar browse filters (chips, side tabs, sub-panes).</summary>
        private bool HasActiveBrowseFiltersExcludingTitleSearch()
        {
            try
            {
                if (!string.IsNullOrEmpty(currentRatingFilter)) return true;
                if (HasCreatorFilter()) return true;
                if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All) return true;
                if (activeTags != null && activeTags.Count > 0) return true;
                if (HasActiveSubPaneOrExtraBrowseFilters()) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Clear title search + browse filters; keep current category path.</summary>
        public void ClearAllBrowseFiltersKeepCategory()
        {
            currentRatingFilter = "";
            try { activeTags?.Clear(); } catch { }
            try { ClearTitleBarSearchAndSyncChrome(); } catch { }
            try { ClearCreatorFilters(); } catch { }
            ClearSubPaneAndExtraBrowseFilters();
            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
            {
                currentGlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.GlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                    try { VPBConfig.Instance.Save(); } catch { }
                }
            }
            try { UpdateTitleCreatorButtonVisual(); } catch { }
            try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
            try { UpdateTabs(); } catch { }
            RefreshFiles(true);
            SyncBrowseFilterChipChrome();
            try { UpdateEmptyGridState(); } catch { }
        }

        /// <summary>Clear title search and refresh chip bar + empty state.</summary>
        private void ClearTitleBarSearchAndSyncChrome()
        {
            ClearTitleBarSearch();
        }
    }
}
