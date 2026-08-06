using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Empty grid state when filters/search hide all rows.

        private GameObject _emptyGridStateGO;
        private Text _emptyGridStateMessage;
        private GameObject _emptyGridStateActionBtn;
        private Text _emptyGridStateActionText;

        private void CreateEmptyGridStateOverlay(GameObject viewportGO)
        {
            if (viewportGO == null || _emptyGridStateGO != null) return;

            _emptyGridStateGO = UI.CreateChildRT(viewportGO, "EmptyGridState", AnchorPresets.stretchAll);

            Image blocker = UI.AddImage(_emptyGridStateGO, new Color(0f, 0f, 0f, 0.01f), false);

            var colGO = UI.CreateChildRT(_emptyGridStateGO, "Column", AnchorPresets.middleCenter, new Vector2(460f, 140f));

            var vlg = UI.AddVLG(colGO, spacing: 12f, childAlignment: TextAnchor.MiddleCenter);

            _emptyGridStateMessage = UI.CreateLabel(colGO, "", GalleryUiDesignTokens.FontBodyRef, new Color(0.75f, 0.75f, 0.78f, 1f), TextAnchor.MiddleCenter, verticalWrap: VerticalWrapMode.Overflow, raycastTarget: false, name: "Message");
            var msgLE = UI.AddLE(_emptyGridStateMessage.gameObject, preferredHeight: 52f);

            _emptyGridStateActionBtn = UI.CreateUIButton(
                colGO, 200, 36,
                VPBTranslation.T("gallery.empty.clear_search", "Clear search"),
                16, 0, 0, AnchorPresets.middleCenter,
                OnEmptyGridStateActionClicked);
            _emptyGridStateActionBtn.name = "EmptyGridAction";
            _emptyGridStateActionText = _emptyGridStateActionBtn.GetComponentInChildren<Text>(true);
            var btnLE = _emptyGridStateActionBtn.GetComponent<LayoutElement>();
            if (btnLE == null) btnLE = _emptyGridStateActionBtn.AddComponent<LayoutElement>();
            btnLE.preferredWidth = 220f;
            btnLE.preferredHeight = 36f;

            _emptyGridStateGO.SetActive(false);
        }

        private void OnEmptyGridStateActionClicked()
        {
            try
            {
                if (!string.IsNullOrEmpty(nameFilter) && nameFilter.Trim().Length > 0)
                {
                    ClearTitleBarSearch();
                    return;
                }
                if (HasActiveBrowseFiltersExcludingTitleSearch())
                {
                    try { ClearAllBrowseFiltersKeepCategory(); } catch { RefreshFiles(true); }
                    return;
                }
                RefreshFiles(true);
            }
            catch { RefreshFiles(true); }
        }

        private void ClearSubPaneAndExtraBrowseFilters()
        {
            clothingSubfilter = 0;
            hairSubfilter = 0;
            appearanceSubfilter = 0;
            posePeopleFilter = PosePeopleFilter.All;
            _clothingGenderUserOverride = false;
            _hairGenderUserOverride = false;
            // Include/exclude filter sets always clear — armed independent of F/T work mode.
            try { activeUserTags?.Clear(); } catch { }
            try { excludedUserTags?.Clear(); } catch { }
            _userTagShowUnusedBucket = false;
            // Not tagged owned by title-bar Filter (ClearTitleBarBrowseFilters).
            try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
        }

        public void UpdateEmptyGridState()
        {
            if (_emptyGridStateGO == null) return;

            bool show = hasLoadedContent
                && !IsSettingsPanelOpen()
                && !settingsListViewActive
                && (currentFilteredFiles == null || currentFilteredFiles.Count == 0)
                && (loadingOverlayGO == null || !loadingOverlayGO.activeSelf);

            _emptyGridStateGO.SetActive(show);
            if (!show) return;

            bool hasSearch = !string.IsNullOrEmpty(nameFilter) && nameFilter.Trim().Length > 0;
            bool hasOtherFilters = HasActiveBrowseFiltersExcludingTitleSearch();

            if (hasSearch)
            {
                _emptyGridStateMessage.text = VPBTranslation.T("gallery.empty.no_match_search", "No items match your search.");
                if (_emptyGridStateActionText != null)
                    _emptyGridStateActionText.text = VPBTranslation.T("gallery.empty.clear_search", "Clear search");
            }
            else if (hasOtherFilters)
            {
                _emptyGridStateMessage.text = hasSearch
                    ? VPBTranslation.T("gallery.empty.no_match_search_and_filters",
                        "No items match. Title bar search filters the grid; side panel search filters that list.")
                    : VPBTranslation.T("gallery.empty.no_match_filters", "No items match the current filters.");
                if (_emptyGridStateActionText != null)
                    _emptyGridStateActionText.text = VPBTranslation.T("gallery.empty.clear_filters", "Clear filters");
            }
            else
            {
                _emptyGridStateMessage.text = VPBTranslation.T("gallery.empty.no_items", "No items in this category.");
                if (_emptyGridStateActionText != null)
                    _emptyGridStateActionText.text = VPBTranslation.T("gallery.empty.refresh", "Refresh");
            }
        }

        // Title search field styling when Settings side panel is open.

        public static readonly Color ColorTitleSearchSettingsMode = new Color(0.14f, 0.28f, 0.42f, 1f);

        public void SyncTitleSearchChromeForActiveMode()
        {
            if (titleSearchInput == null) return;

            bool settingsMode = IsSettingsPanelOpen() || settingsListViewActive;
            if (titleSearchInput.placeholder is Text ph)
            {
                ph.text = settingsMode
                    ? VPBTranslation.T("gallery.search.settings", "Filter settings...")
                    : VPBTranslation.T("gallery.search.main", "Search name, #tag, OR, badge…");
            }

            string tSearch = titleSearchInput.text ?? "";
            bool hasTerm = tSearch.Trim().Length > 0;
            Color c;
            if (settingsMode)
                c = hasTerm ? ColorTitleSearchFilterActive : ColorTitleSearchSettingsMode;
            else
                c = hasTerm ? ColorTitleSearchFilterActive : ColorTitleSearchBackdropIdle;

            Image fieldBg = titleSearchInput.GetComponent<Image>();
            if (fieldBg != null) fieldBg.color = c;
            if (_titleSearchCompactGO != null)
            {
                Image cmpBg = _titleSearchCompactGO.GetComponent<Image>();
                if (cmpBg != null) cmpBg.color = c;
            }
        }
    }

}
