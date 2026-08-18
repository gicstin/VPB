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

            var vlg = UI.AddVLG(colGO, spacing: UI.GapGroup(), childAlignment: TextAnchor.MiddleCenter);

            _emptyGridStateMessage = UI.CreateLabel(colGO, "", GalleryUiDesignTokens.FontBodyRef, new Color(0.75f, 0.75f, 0.78f, 1f), TextAnchor.MiddleCenter, verticalWrap: VerticalWrapMode.Overflow, raycastTarget: false, name: "Message");
            var msgLE = UI.AddLE(_emptyGridStateMessage.gameObject, preferredHeight: 52f);

            _emptyGridStateActionBtn = UI.CreateUIButton(
                colGO, 200, 36,
                VPBTranslation.T("gallery.empty.clear_search", "Clear search"),
                GalleryUiDesignTokens.FontBodyRef, 0, 0, AnchorPresets.middleCenter,
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

            bool emptyFiles = currentFilteredFiles == null || currentFilteredFiles.Count == 0;
            bool refreshing = VpbProgressService.IsBrowseRefreshActive || _quietGalleryRefresh;
            bool baseOk = hasLoadedContent
                && !settingsListViewActive;

            // BusyChrome owns refresh feedback — hide empty chrome while refreshing (no duplicate "Updating…").
            bool show = baseOk && emptyFiles && !refreshing;
            _emptyGridStateGO.SetActive(show);
            if (!show) return;

            if (_emptyGridStateActionBtn != null)
                _emptyGridStateActionBtn.SetActive(true);

            bool hasSearch = !string.IsNullOrEmpty(nameFilter) && nameFilter.Trim().Length > 0;
            bool hasOtherFilters = HasActiveBrowseFiltersExcludingTitleSearch();

            if (hasSearch)
            {
                _emptyGridStateMessage.text = VPBTranslation.T(
                    "gallery.empty.no_match_search",
                    "No items match the title search.");
                if (_emptyGridStateActionText != null)
                    _emptyGridStateActionText.text = VPBTranslation.T("gallery.empty.clear_search", "Clear search");
            }
            else if (hasOtherFilters)
            {
                _emptyGridStateMessage.text = VPBTranslation.T(
                    "gallery.empty.no_match_filters",
                    "No items match the current filters.");
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

        /// <summary>
        /// Title search is always the gallery grid find.
        /// Settings / side lists use the side-rail filter field — never hijack this chrome.
        /// </summary>
        public void SyncTitleSearchChromeForActiveMode()
        {
            if (titleSearchInput == null) return;

            if (titleSearchInput.placeholder is Text ph)
            {
                ph.text = HasTitleSearchChips()
                    ? VPBTranslation.T(
                        "gallery.search.main_chips",
                        "Type + Enter chip · Tab/↓ grid · Shift+Enter exclude")
                    : VPBTranslation.T(
                        "gallery.search.main",
                        "Search grid: name, #tag, OR, badge…");
            }

            string tSearch = titleSearchInput.text ?? "";
            bool hasTerm = tSearch.Trim().Length > 0;
            Color c = hasTerm ? ColorTitleSearchFilterActive : ColorTitleSearchBackdropIdle;

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
