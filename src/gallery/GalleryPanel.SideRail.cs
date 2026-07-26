using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel
    {
        private struct SideRailChrome
        {
            public bool IsLeft;
            public ContentType? ActiveContent;
            public GameObject TabScrollGO;
            public GameObject SubTabScrollGO;
            public InputField SearchInput;
            public GameObject SortBtn;
            public GameObject RefreshBtn;
            public GameObject SubSortBtn;
            public GameObject SubSceneSortBtn;
            public InputField SubSearchInput;
            public UnityAction<string> SearchOnValueChanged;
        }

        private SideRailChrome BuildLeftSideRailChrome()
        {
            return new SideRailChrome
            {
                IsLeft = true,
                ActiveContent = leftActiveContent,
                TabScrollGO = leftTabScrollGO,
                SubTabScrollGO = leftSubTabScrollGO,
                SearchInput = leftSearchInput,
                SortBtn = leftSortBtn,
                RefreshBtn = null,
                SubSortBtn = leftSubSortBtn,
                SubSceneSortBtn = leftSubSceneSortBtn,
                SubSearchInput = leftSubSearchInput,
                SearchOnValueChanged = _leftMainSideSearchOnValueChanged,
            };
        }

        private SideRailChrome BuildRightSideRailChrome()
        {
            return new SideRailChrome
            {
                IsLeft = false,
                ActiveContent = rightActiveContent,
                TabScrollGO = rightTabScrollGO,
                SubTabScrollGO = rightSubTabScrollGO,
                SearchInput = rightSearchInput,
                SortBtn = rightSortBtn,
                RefreshBtn = rightRefreshBtn,
                SubSortBtn = rightSubSortBtn,
                SubSceneSortBtn = rightSubSceneSortBtn,
                SubSearchInput = rightSubSearchInput,
                SearchOnValueChanged = _rightMainSideSearchOnValueChanged,
            };
        }

        /// <summary>Sync side rail search/sort chrome. Returns signed grid inset magnitude (positive left, negative right).</summary>
        private float SyncSideRailChrome(SideRailChrome rail, float closedOffset)
        {
            float openMag = UiMetrics.Px(GalleryUiDesignTokens.SideTabOpenGridInsetRef);
            float openOffset = rail.IsLeft ? openMag : -openMag;

            if (rail.ActiveContent.HasValue && rail.TabScrollGO != null)
            {
                rail.TabScrollGO.SetActive(true);
                ContentType type = rail.ActiveContent.Value;
                bool cleanupSide = ContentTypeSuppressesSideSearch(type);
                bool hideSort = cleanupSide || ContentTypeSuppressesSideSort(type);

                if (rail.SortBtn != null) rail.SortBtn.SetActive(!hideSort);
                if (rail.RefreshBtn != null) rail.RefreshBtn.SetActive(!cleanupSide);

                if (rail.SearchInput != null)
                {
                    rail.SearchInput.gameObject.SetActive(!cleanupSide);
                    if (!cleanupSide)
                    {
                        string target = GetSideFilterTextForContentType(type);
                        SetSideSearchInputTextWithoutNotify(rail.SearchInput, target, rail.SearchOnValueChanged);
                        if (rail.SearchInput.placeholder is Text ph)
                            ph.text = GetContentTypePlaceholder(type);
                    }
                }

                return openOffset;
            }

            if (rail.TabScrollGO != null)
            {
                rail.TabScrollGO.SetActive(false);
                if (rail.SubTabScrollGO != null) rail.SubTabScrollGO.SetActive(false);
                if (rail.SearchInput != null) rail.SearchInput.gameObject.SetActive(false);
                if (rail.SortBtn != null) rail.SortBtn.SetActive(false);
                if (rail.RefreshBtn != null) rail.RefreshBtn.SetActive(false);

                if (rail.SubSortBtn != null) rail.SubSortBtn.SetActive(false);
                if (rail.IsLeft) leftSubSceneSortBarActive = false;
                else rightSubSceneSortBarActive = false;
                if (rail.SubSceneSortBtn != null) rail.SubSceneSortBtn.SetActive(false);
                if (rail.SubSearchInput != null) rail.SubSearchInput.gameObject.SetActive(false);
            }

            return closedOffset;
        }
    }
}
