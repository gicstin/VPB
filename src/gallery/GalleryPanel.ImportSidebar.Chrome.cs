using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Match Settings float shell. Role-split accents — not one green on every control.
        private static readonly Color ImportSidebarHeaderBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color ImportSidebarStepHeaderBg = GalleryUiColorTokens.SurfaceDarker;
        /// <summary>Selected type / atom / checklist — quiet ActiveSelected (not Apply green).</summary>
        internal static readonly Color ImportSidebarSelectedAccent = GalleryUiColorTokens.ActiveSelected;
        // Bulk commands: secondary mid vs destroy.
        private static readonly Color ImportSidebarSelectAllBg = GalleryUiColorTokens.ActiveSecondary;
        private static readonly Color ImportSidebarClearAllBg = GalleryUiColorTokens.AccentDanger;
        // Accumulate toggle: ON = ActiveOn; OFF = mid chrome.
        private static readonly Color ImportSidebarMultiToggleBg = GalleryUiColorTokens.ActiveOn;
        private static readonly Color ImportSidebarMultiToggleOffBg = GalleryUiColorTokens.SurfaceMid;
        // Option group caption — quiet chrome.
        private static readonly Color ImportSidebarGroupHeaderBg = GalleryUiColorTokens.SurfacePanel;
        private static readonly Color ImportSidebarUnavailableRow = GalleryUiColorTokens.RowZero;
        private static readonly Color ImportSidebarUnavailableText = GalleryUiColorTokens.TextDim;
        // Selected but empty on source — keep intent without shouting.
        private static readonly Color ImportSidebarPausedSelectedRow = GalleryUiColorTokens.SurfaceMid;
        private static readonly Color ImportSidebarApplyReasonText = GalleryUiColorTokens.ActiveWarnText;
        private static readonly Color ImportSidebarApplyReasonBg = GalleryUiColorTokens.ActiveWarnSurface;
        private static readonly Color ImportSidebarScenesLockedBanner = GalleryUiColorTokens.ModeApplyStripe;
        private static readonly Color ImportSidebarScenesLockedHeaderBg = GalleryUiColorTokens.ActiveWarnHeader;
        // Match hint: raised idle row.
        private static readonly Color ImportSidebarMatchHintColor = GalleryUiColorTokens.PopupRowActive;
        // Primary Apply CTA only — AccentConfirm reserved here.
        private static readonly Color ImportSidebarApplyBg = GalleryUiColorTokens.AccentConfirm;
        // Float shell mirrors SettingsFloat*.
        private static readonly Color ImportSidebarFloatTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color ImportSidebarFloatFooterBarBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color ImportSidebarFloatPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color ImportSidebarFloatChromeIconBg = GalleryUiColorTokens.ChromeIconWell;
        private static readonly Color ImportSidebarSecondaryActionBg = GalleryUiColorTokens.SurfaceMid;

        /// <summary>Hide side-column filter/sort chrome on the edge replaced by the import sidebar.</summary>
        private void SuppressImportOccupiedSideColumnChrome()
        {
            if (!ImportSidebarOccupiesSideColumn) return;
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
                importSidebarHeaderLabel.color = GalleryUiColorTokens.TextPrimary;
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
