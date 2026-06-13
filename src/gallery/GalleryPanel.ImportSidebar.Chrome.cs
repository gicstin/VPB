using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly Color ImportSidebarHeaderBg = new Color(0.10f, 0.26f, 0.44f, 1f);
        private static readonly Color ImportSidebarStepHeaderBg = new Color(0.18f, 0.24f, 0.32f, 0.98f);
        internal static readonly Color ImportSidebarSelectedAccent = new Color(0.14f, 0.40f, 0.62f, 1f);
        // Mid-tone between ColorInactiveRow and ImportSidebarSelectedAccent: marks rows whose ID
        // name-matches a counterpart on the opposite list without stealing the selected-state color.
        private static readonly Color ImportSidebarMatchHintColor = new Color(0.20f, 0.30f, 0.40f, 1f);

        /// <summary>Hide side-column filter/sort chrome on the edge replaced by the import sidebar.</summary>
        private void SuppressImportOccupiedSideColumnChrome()
        {
            if (!importSidebarActive) return;
            SetSideColumnFilterChromeVisible(importSidebarOnLeft, false);
            try { SetUserTagScrollStepButtonsActive(importSidebarOnLeft, false); } catch { }
            try { SanitizeImportSidebarScrollChrome(); } catch { }
        }

        private void SetSideColumnFilterChromeVisible(bool isLeft, bool visible)
        {
            if (visible) return;
            if (isLeft)
            {
                if (leftSortBtn != null) leftSortBtn.SetActive(false);
                if (leftSearchInput != null) leftSearchInput.gameObject.SetActive(false);
                if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                if (leftSubSceneSortBtn != null) leftSubSceneSortBtn.SetActive(false);
                if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
                if (leftSubClearBtn != null) leftSubClearBtn.SetActive(false);
                if (_leftSidePanelHeaderGO != null) _leftSidePanelHeaderGO.SetActive(false);
            }
            else
            {
                if (rightSortBtn != null) rightSortBtn.SetActive(false);
                if (rightRefreshBtn != null) rightRefreshBtn.SetActive(false);
                if (rightSearchInput != null) rightSearchInput.gameObject.SetActive(false);
                if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                if (rightSubSceneSortBtn != null) rightSubSceneSortBtn.SetActive(false);
                if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
                if (rightSubClearBtn != null) rightSubClearBtn.SetActive(false);
                if (_rightSidePanelHeaderGO != null) _rightSidePanelHeaderGO.SetActive(false);
            }
        }

        /// <summary>Strip jump/step buttons if they were ever parented to this sidebar's scrollbar.</summary>
        private void SanitizeImportSidebarScrollChrome()
        {
            if (importSidebarBodyScrollRT == null) return;
            Transform sb = importSidebarBodyScrollRT.Find("Scrollbar");
            if (sb == null) return;
            for (int i = sb.childCount - 1; i >= 0; i--)
            {
                Transform ch = sb.GetChild(i);
                if (ch == null) continue;
                string n = ch.name ?? "";
                if (n.IndexOf("ScrollStep", System.StringComparison.Ordinal) >= 0
                    || n.IndexOf("ScrollbarScroll", System.StringComparison.Ordinal) >= 0)
                {
                    try { UnityEngine.Object.Destroy(ch.gameObject); } catch { }
                }
            }
        }

        /// <summary>Match CreateVScrollableContent viewport width to pinned header/Apply (no legacy -5px shift).</summary>
        private void AlignImportSidebarScrollViewport(float s)
        {
            if (importSidebarBodyScrollRT == null) return;
            Transform vp = importSidebarBodyScrollRT.Find("Viewport");
            if (vp == null) return;
            RectTransform vprt = vp as RectTransform;
            if (vprt == null) return;
            float scrollW = ImportSidebarScrollBarWidthPx(s);
            vprt.sizeDelta = new Vector2(-scrollW, 0f);
            vprt.anchoredPosition = new Vector2(-scrollW * 0.5f, 0f);
        }

        private void StyleImportSidebarHeader(float s = 1f)
        {
            if (importSidebarHeaderRoot == null) return;
            Image bg = importSidebarHeaderRoot.GetComponent<Image>();
            if (bg != null) bg.color = ImportSidebarHeaderBg;
            if (importSidebarHeaderLabel != null)
            {
                importSidebarHeaderLabel.color = Color.white;
                importSidebarHeaderLabel.alignment = TextAnchor.MiddleCenter;
                importSidebarHeaderLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
                importSidebarHeaderLabel.verticalOverflow = VerticalWrapMode.Truncate;
                RectTransform rt = importSidebarHeaderLabel.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float padH = ImportSidebarInnerPadHRef * s;
                    float padV = 2f * s;
                    rt.offsetMin = new Vector2(padH, padV);
                    rt.offsetMax = new Vector2(-padH, -padV);
                }
            }
        }
    }
}
