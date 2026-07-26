using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly Color ImportSidebarHeaderBg = new Color(0.10f, 0.26f, 0.44f, 1f);
        private static readonly Color ImportSidebarStepHeaderBg = new Color(0.18f, 0.24f, 0.32f, 0.98f);
        internal static readonly Color ImportSidebarSelectedAccent = new Color(0.14f, 0.40f, 0.62f, 1f);
        // Bulk-select action buttons: deliberately off the blue/gray type-chip palette so they read as
        // commands, not selectable chips. Faint green = add, faint red = clear.
        private static readonly Color ImportSidebarSelectAllBg = new Color(0.20f, 0.34f, 0.26f, 1f);
        private static readonly Color ImportSidebarClearAllBg = new Color(0.36f, 0.24f, 0.24f, 1f);
        // Multi-select toggle: amber when on (chips accumulate), muted when off (single-select).
        private static readonly Color ImportSidebarMultiToggleBg = new Color(0.44f, 0.30f, 0.12f, 1f);
        private static readonly Color ImportSidebarMultiToggleOffBg = new Color(0.26f, 0.24f, 0.22f, 1f);
        // Per-type option group caption: blue-tinted to tie it to the matching selected (blue) type chip.
        private static readonly Color ImportSidebarGroupHeaderBg = new Color(0.16f, 0.30f, 0.46f, 1f);
        // Type chip with nothing to import from the source: visibly recessed + greyed, non-interactable.
        private static readonly Color ImportSidebarUnavailableRow = new Color(0.16f, 0.16f, 0.17f, 1f);
        private static readonly Color ImportSidebarUnavailableText = new Color(0.45f, 0.46f, 0.48f, 1f);
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

        /// <summary>Flush viewport left; reserve scrollbar + inner pad on the right only (no center-shift).</summary>
        private void AlignImportSidebarScrollViewport(float s)
        {
            if (importSidebarBodyScrollRT == null) return;
            Transform vp = importSidebarBodyScrollRT.Find("Viewport");
            if (vp == null) return;
            RectTransform vprt = vp as RectTransform;
            if (vprt == null) return;
            float gutter = ImportSidebarScrollBarWidthPx(s) + ImportSidebarInnerPadHRef * s;
            vprt.sizeDelta = Vector2.zero;
            vprt.anchoredPosition = Vector2.zero;
            float minY = vprt.offsetMin.y;
            float maxY = vprt.offsetMax.y;
            vprt.offsetMin = new Vector2(0f, minY);
            vprt.offsetMax = new Vector2(-gutter, maxY);
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
                importSidebarHeaderLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                importSidebarHeaderLabel.verticalOverflow = VerticalWrapMode.Truncate;
                RectTransform rt = importSidebarHeaderLabel.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float padInner = ImportSidebarInnerPadHRef * s;
                    float padV = 2f * s;
                    rt.offsetMin = new Vector2(0f, padV);
                    rt.offsetMax = new Vector2(-padInner, -padV);
                }
            }
        }
    }
}
