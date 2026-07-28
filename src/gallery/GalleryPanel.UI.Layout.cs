using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
{
        private void SetTitleSearchInputTextWithoutNotify(InputField input, string text, UnityAction<string> handler)
        {
            if (input == null) return;
            string t = text ?? "";
            if (input.text == t) return;
            _suppressTitleBarSearchValueChanged = true;
            try
            {
                if (handler != null)
                {
                    input.onValueChanged.RemoveListener(handler);
                    input.text = t;
                    input.onValueChanged.AddListener(handler);
                }
                else input.text = t;
            }
            finally
            {
                _suppressTitleBarSearchValueChanged = false;
            }
            if (input == titleSearchInput)
            {
                try { SyncTitleBarSearchBackdrop(); } catch { }
            }
        }

        /// <summary>Keeps title bar search aligned: package name filter in browse mode, settings row filter while settings side is open.</summary>
        private void SyncTitleSearchInputWithActiveMode()
        {
            if (titleSearchInput == null || _titleBarSearchOnValueChanged == null) return;
            if (IsSettingsPanelOpen())
                SetTitleSearchInputTextWithoutNotify(titleSearchInput, CanonicalSettingsSideSearchText(), _titleBarSearchOnValueChanged);
            else
                SetTitleSearchInputTextWithoutNotify(titleSearchInput, GetTitleSearchBrowseFieldText(), _titleBarSearchOnValueChanged);
        }

        private void SetSideSearchInputTextWithoutNotify(InputField input, string text, UnityAction<string> handler)
        {
            if (input == null) return;
            string t = text ?? "";
            if (input.text == t) return;
            _suppressMainSideSearchValueChanged = true;
            try
            {
                if (handler != null)
                {
                    input.onValueChanged.RemoveListener(handler);
                    input.text = t;
                    input.onValueChanged.AddListener(handler);
                }
                else input.text = t;
            }
            finally
            {
                _suppressMainSideSearchValueChanged = false;
            }
        }

        public void UpdateLayout()
        {
            UpdateLayout(true, true);
        }

        /// <summary>When false, skips all synchronous side-tab cache fills (legacy single flag).</summary>
        public void UpdateLayout(bool allowAllSynchronousCaches)
        {
            UpdateLayout(allowAllSynchronousCaches, allowAllSynchronousCaches);
        }

        /// <param name="allowSynchronousCreatorCategoryCaches">When false, skips main-thread creator/category scans (worker or <see cref="GalleryPanel.RefreshFilesRoutine"/> overlap).</param>
        /// <param name="allowSynchronousUserTagsSideTabCache">When false, skips synchronous user-tag side tab scan.</param>
        public void UpdateLayout(bool allowSynchronousCreatorCategoryCaches, bool allowSynchronousUserTagsSideTabCache)
        {
            EnsureUserTagAvailScrollTrackingHooks();
            SnapshotUserTagAvailScrollForPreserveBoth();

            GalleryUiMetrics uiMetrics = UiMetrics;
            float paneScale = uiMetrics.ChromeScale;
            if (backgroundBoxGO != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(backgroundBoxGO.GetComponent<RectTransform>());
            }
            Canvas.ForceUpdateCanvases();
            bool skipBrowseSideCaches = IsSettingsPanelOpen() || settingsListViewActive;
            if (allowSynchronousCreatorCategoryCaches && !skipBrowseSideCaches)
            {
                // Prefer facet-specific caching from UpdateTabs builders when opening side panes
                // (ToggleSide passes false here). Full sync only for cold layout / refresh paths.
                if (!creatorsCached) CacheCreators();
                if (!categoriesCached) CacheCategoryCounts();
            }
            if (allowSynchronousUserTagsSideTabCache && !userTagsCached)
                CacheUserTagsSideTab();

            if (contentScrollRT == null) return;

            try
            {
                try { SyncTitleSearchInputWithActiveMode(); } catch { }
                try { ApplyTitleBarResponsiveLayout(paneScale); } catch { }
                try { ApplyFooterOverflowLayout(paneScale); } catch { }
                try { ApplyTopDockSideButtonsLayout(paneScale); } catch { }
                MarkGalleryPaneChromeDirty();
            }
            catch { }

            // Ensure UI reflects persisted replace / appearance outfit mode even if the panel was recreated/re-shown.
            UpdateReplaceButtonState();
            UpdateKeepClothingButtonState();
            
            float closedInset = GalleryUiDesignTokens.SideTabClosedGridInsetRef * paneScale;
            float leftOffset = SyncSideRailChrome(BuildLeftSideRailChrome(), closedInset);
            float rightOffset = SyncSideRailChrome(BuildRightSideRailChrome(), -closedInset);
            
            // Import sidebar hides its own side's tab column and pushes the grid edge in by 230,
            // so the sidebar replaces (not overlaps) the Category / Creator slot it occupies.
            float importInset = GalleryUiDesignTokens.SideTabOpenGridInsetRef * paneScale;
            if (importSidebarActive)
            {
                if (importSidebarOnLeft)
                {
                    if (leftTabScrollGO != null) leftTabScrollGO.SetActive(false);
                    if (leftSubTabScrollGO != null) leftSubTabScrollGO.SetActive(false);
                    if (leftSearchInput != null) leftSearchInput.gameObject.SetActive(false);
                    if (leftSortBtn != null) leftSortBtn.SetActive(false);
                    if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                    if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
                    if (leftOffset < importInset) leftOffset = importInset;
                }
                else
                {
                    if (rightTabScrollGO != null) rightTabScrollGO.SetActive(false);
                    if (rightSubTabScrollGO != null) rightSubTabScrollGO.SetActive(false);
                    if (rightSearchInput != null) rightSearchInput.gameObject.SetActive(false);
                    if (rightSortBtn != null) rightSortBtn.SetActive(false);
                    if (rightRefreshBtn != null) rightRefreshBtn.SetActive(false);
                    if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                    if (rightSubSceneSortBtn != null) rightSubSceneSortBtn.SetActive(false);
                    if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
                    if (rightOffset > -importInset) rightOffset = -importInset;
                }
                if (importSidebarRoot != null) importSidebarRoot.transform.SetAsLastSibling();
            }

            try { SyncSidePanelHeaderChrome(paneScale); } catch { }
            try { SuppressImportOccupiedSideColumnChrome(); } catch { }

            try
            {
                float chipPad = FilterChipHorizontalPaddingRef * paneScale;
                float panelW = backgroundBoxGO != null
                    ? backgroundBoxGO.GetComponent<RectTransform>().rect.width
                    : 0f;
                // Bar spans [leftOffset+pad, panelW+rightOffset-pad] (rightOffset is negative).
                float chipAvailW = panelW > 1f ? panelW + rightOffset - leftOffset - 2f * chipPad : -1f;
                RefreshActiveFilterChips(chipAvailW);
            }
            catch { }

            float filterTopInset = 0f;
            try { filterTopInset = ActiveFilterChromeTopInsetPx(paneScale); } catch { }
            float topOffset = -GalleryUiDesignTokens.SideTabTopOffsetRef * paneScale - filterTopInset;
            float tabTopOffset = TabScrollTopOffset(); // clears sort/search row aligned with grid top
            ApplySideTabFilterRowVerticalLayout(paneScale);

            // Footer first: main grid/tab insets use the top of this stack (grows when tbox expands).
            if (paginationRT != null)
            {
                // Footer bar: ALWAYS stretch to full width of backgroundBoxGO
                paginationRT.offsetMin = new Vector2(0, 0);
                paginationRT.offsetMax = new Vector2(0, GalleryUiDesignTokens.FooterBarHeightRef * paneScale);

                if (hoverPathRT != null)
                {
                    // Info bar: stretch to full width, sits above the buttons bar
                    hoverPathRT.offsetMin = new Vector2(0, GalleryUiDesignTokens.FooterBarHeightRef * paneScale);
                    // Update tbox expansion references so animation uses the correct scale
                    tboxTopOffsetBase  = GalleryUiDesignTokens.FooterToolboxTopRef * paneScale;
                    tboxInfoRowHeight  = GalleryUiDesignTokens.FooterInfoRowHeightRef * paneScale;
                    if (tbox == null)
                        hoverPathRT.offsetMax = new Vector2(0, tboxTopOffsetBase);
                    else
                    {
                        // Height-only refresh — full UpdateSelectionContextMenu here flashes on category switch.
                        try { SyncTboxFooterRowChrome(paneScale); } catch { }
                        try { ApplyTboxBarHeightNow(); } catch { }
                    }
                }
            }

            SyncGalleryMainAreaBottomEdge(leftOffset, rightOffset, topOffset, tabTopOffset);
            _lastBrowseGridLeftInset = leftOffset;
            _lastBrowseGridRightInset = rightOffset;

            try { ApplyFirstRunHintStripLayout(leftOffset, rightOffset, paneScale); } catch { }
            try { ApplyActiveFilterChipBarLayout(leftOffset, rightOffset, paneScale); } catch { }

            // Side button stacks stay vertically fixed (do not ride the footer inset).
            if (leftSideContainer != null)
            {
                RectTransform rt = leftSideContainer.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
            }
            if (rightSideContainer != null)
            {
                RectTransform rt = rightSideContainer.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
            }
            if (leftSideHoverStrip != null)
            {
                RectTransform rt = leftSideHoverStrip.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, 0f);
            }
            if (rightSideHoverStrip != null)
            {
                RectTransform rt = rightSideHoverStrip.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, 0f);
            }

            UpdateFooterLayoutState();

            try { RefreshHoverPreviewLayoutImmediate(); } catch { }

            // Layout rebuild / hover refresh can reset ScrollRect viewports after SyncGalleryMainAreaBottomEdge;
            // sticky chrome must win last so Available / Applied toolbars stay visible in Tags mode.
            try
            {
                if (leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags)
                {
                    ApplyUserTagsStickyScrollChrome(tabTopOffset);
                    // Sticky over-inset / inactive subtree → Mask height≈0, Tags(N) with empty rows (#74).
                    bool needVirtRecover =
                        (IsUserTagsSideTabOpen(true) && IsUserTagAvailViewportCollapsed(true))
                        || (IsUserTagsSideTabOpen(false) && IsUserTagAvailViewportCollapsed(false));
                    if (needVirtRecover)
                        RequestUserTagAvailVirtRecoverAfterLayout();
                }
            }
            catch { }

            RestorePreservedUserTagAvailScroll();
            try { EnforceCreatorSideRailButtonVisibilityFromConfig(); } catch { }
        }

        /// <summary>Places side-pane sort/refresh/search row below optional collapse header strip.</summary>
        private void ApplySideTabFilterRowVerticalLayout(float paneScale)
        {
            ApplyMainSideSearchRowLayout(true, paneScale);
            ApplyMainSideSearchRowLayout(false, paneScale);
            SyncSideTabSubFilterRowChrome(paneScale);
        }

        /// <summary>Full horizontal + vertical layout for upper side-pane search row (sort / refresh / search).</summary>
        private void ApplyMainSideSearchRowLayout(bool isLeft, float s)
        {
            if (s <= 0f) s = 1f;
            float margin = GalleryUiDesignTokens.SideTabSideMarginRef * s;
            float sortSz = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            float gap = GalleryUiDesignTokens.SideTabControlGapRef * s;
            float refreshW = GalleryUiDesignTokens.SideTabRefreshBtnWidthRef * s;
            float rowH = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            ContentType? active = isLeft ? leftActiveContent : rightActiveContent;
            bool showSort = active.HasValue
                && !ContentTypeSuppressesSideSearch(active.Value)
                && !ContentTypeSuppressesSideSort(active.Value);
            float searchW = (GalleryUiDesignTokens.SideTabColumnWidthRef - GalleryUiDesignTokens.SideTabMainSearchSortReserveRef) * s;
            if (!showSort) searchW += GalleryUiDesignTokens.SideTabMainSearchSortReserveRef * s;
            float y = SidePanelFilterRowYForSide(isLeft, s);

            if (isLeft)
            {
                if (leftSortBtn != null)
                {
                    RectTransform sortRt = leftSortBtn.GetComponent<RectTransform>();
                    if (sortRt != null)
                    {
                        sortRt.anchoredPosition = new Vector2(margin, y);
                        sortRt.sizeDelta = new Vector2(sortSz, sortSz);
                    }
                }
                if (leftSearchInput != null)
                {
                    RectTransform searchRt = leftSearchInput.GetComponent<RectTransform>();
                    if (searchRt != null)
                    {
                        float searchX = margin + (showSort ? sortSz + gap : 0f);
                        searchRt.anchoredPosition = new Vector2(searchX, y);
                        searchRt.sizeDelta = new Vector2(searchW, rowH);
                    }
                    RescaleSearchInput(leftSearchInput, s, GalleryUiDesignTokens.SideTabRowHeightRef);
                }
            }
            else
            {
                if (rightSearchInput != null)
                {
                    RectTransform searchRt = rightSearchInput.GetComponent<RectTransform>();
                    if (searchRt != null)
                    {
                        searchRt.anchoredPosition = new Vector2(-margin, y);
                        searchRt.sizeDelta = new Vector2(searchW, rowH);
                    }
                    RescaleSearchInput(rightSearchInput, s, GalleryUiDesignTokens.SideTabRowHeightRef);
                }
                if (rightRefreshBtn != null)
                {
                    RectTransform refreshRt = rightRefreshBtn.GetComponent<RectTransform>();
                    if (refreshRt != null)
                    {
                        refreshRt.anchoredPosition = new Vector2(-(margin + searchW) + refreshW, y);
                        refreshRt.sizeDelta = new Vector2(refreshW, rowH);
                    }
                    if (rightRefreshBtnText != null)
                        GalleryUiMetrics.ApplyFont(rightRefreshBtnText, 18, s, 10);
                }
                if (rightSortBtn != null)
                {
                    RectTransform sortRt = rightSortBtn.GetComponent<RectTransform>();
                    if (sortRt != null)
                    {
                        sortRt.anchoredPosition = new Vector2(-(margin + searchW + gap), y);
                        sortRt.sizeDelta = new Vector2(sortSz, sortSz);
                    }
                }
            }
        }

        /// <summary>Distance from panel bottom to the top edge of the bottom chrome (footer + info/tbox bar).</summary>
        private float GalleryMainAreaBottomInset()
        {
            float s = ChromeScale;
            if (hoverPathRT != null) return hoverPathRT.offsetMax.y;
            return GalleryUiDesignTokens.GalleryMainBottomFallbackRef * s;
        }

        /// <summary>Bottom inset for category/creator/tag tab scroll rects — keeps lists above footer + info/tbox bar.</summary>
        private float SideTabScrollBottomInsetY()
        {
            return GalleryMainAreaBottomInset() + GalleryUiDesignTokens.SideTabScrollBottomPadRef;
        }

        /// <summary>Small gap at the horizontal split only (upper pane — not footer clearance).</summary>
        private float SideTabSplitSeamInset()
        {
            float s = ChromeScale;
            return GalleryUiDesignTokens.SideTabSplitSeamRef * s;
        }

        /// <summary>True for the upper stack in a split tab column (category / hub pay-type / etc.).</summary>
        private static bool IsUpperStackedSideTabPane(RectTransform rt)
        {
            if (rt == null) return false;
            // Top stack in split column: category/tags (φ major top), hub right (0.7/1), cleanup stale (0.5/1), etc.
            return rt.anchorMax.y >= 0.85f && rt.anchorMin.y >= 0.25f;
        }

        private static float SideTabSubFilterRowAnchorY(ContentType activeContent)
        {
            return activeContent == ContentType.Category
                ? GalleryUiDesignTokens.CategorySideSubPaneHeightFraction
                : 0.5f;
        }

        private float ResolveSideTabSubFilterRowAnchorY(bool isLeft)
        {
            // Prefer live split from sub-pane (may rise when InfoBar is tall).
            GameObject sub = isLeft ? leftSubTabScrollGO : rightSubTabScrollGO;
            if (sub != null && sub.activeSelf)
            {
                RectTransform subRT = sub.GetComponent<RectTransform>();
                if (subRT != null && subRT.anchorMax.y > 0.05f && subRT.anchorMax.y < 0.95f)
                    return subRT.anchorMax.y;
            }
            ContentType? ac = isLeft ? leftActiveContent : rightActiveContent;
            return ac.HasValue ? SideTabSubFilterRowAnchorY(ac.Value) : 0.5f;
        }

        private void ApplySideTabSubFilterRowAnchor(RectTransform rt, bool isLeft)
        {
            if (rt == null) return;
            float y = ResolveSideTabSubFilterRowAnchorY(isLeft);
            float xEdge = isLeft ? 0f : 1f;
            rt.anchorMin = new Vector2(xEdge, y);
            rt.anchorMax = new Vector2(xEdge, y);
            rt.pivot = new Vector2(xEdge, 1f);
        }

        private void ApplySideTabSubSortButtonLayout(GameObject btn, bool isLeft, float s)
        {
            if (btn == null || !btn.activeSelf) return;
            RectTransform rt = btn.GetComponent<RectTransform>();
            if (rt == null) return;
            ApplySideTabSubFilterRowAnchor(rt, isLeft);
            float sz = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            float margin = GalleryUiDesignTokens.SideTabSideMarginRef * s;
            float subRowY = -GalleryUiDesignTokens.SideTabSubFilterRowTopGapRef * s;
            rt.sizeDelta = new Vector2(sz, sz);
            rt.anchoredPosition = new Vector2(isLeft ? margin : -margin, subRowY);
        }

        /// <summary>Re-anchor lower split filter row (sort + search) to match sub-pane top (φ minor for category).</summary>
        private void SyncSideTabSubFilterRowChrome(float s)
        {
            if (s <= 0f) s = 1f;
            if (leftSubTabScrollGO != null && leftSubTabScrollGO.activeSelf)
            {
                ApplySideTabSubSortButtonLayout(leftSubSortBtn, true, s);
                ApplySideTabSubSortButtonLayout(leftSubSceneSortBtn, true, s);
                ApplyLeftSubSearchLayoutScaled(s);
            }
            if (rightSubTabScrollGO != null && rightSubTabScrollGO.activeSelf)
            {
                ApplySideTabSubSortButtonLayout(rightSubSortBtn, false, s);
                ApplySideTabSubSortButtonLayout(rightSubSceneSortBtn, false, s);
                ApplyRightSubSearchLayoutScaled(s);
            }
        }

        private static float SceneSourceSortBarBreadth(float s) => GalleryUiDesignTokens.SideTabRowHeightRef * s;

        private void ApplyLeftSubSearchLayoutScaled(float s)
        {
            if (leftSubSearchInput == null) return;
            RectTransform rt = leftSubSearchInput.GetComponent<RectTransform>();
            ApplySideTabSubFilterRowAnchor(rt, true);
            float tabAreaW = GalleryUiDesignTokens.SideTabColumnWidthRef * s;
            float margin = GalleryUiDesignTokens.SideTabSideMarginRef * s;
            float gap = GalleryUiDesignTokens.SideTabControlGapRef * s;
            float rowH = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            float bar = SceneSourceSortBarBreadth(s);
            bool reserveSortBar = leftSubSceneSortBarActive
                || (leftSubSortBtn != null && leftSubSortBtn.activeSelf);
            float subRowY = -GalleryUiDesignTokens.SideTabSubFilterRowTopGapRef * s;
            if (reserveSortBar)
            {
                rt.anchoredPosition = new Vector2(margin + bar + gap, subRowY);
                rt.sizeDelta = new Vector2(tabAreaW - margin - bar - gap - margin, rowH);
            }
            else
            {
                rt.anchoredPosition = new Vector2(margin, subRowY);
                rt.sizeDelta = new Vector2(tabAreaW - 2f * margin, rowH);
            }
            RescaleSearchInput(leftSubSearchInput, s, GalleryUiDesignTokens.SideTabRowHeightRef);
        }

        private void ApplyRightSubSearchLayoutScaled(float s)
        {
            if (rightSubSearchInput == null) return;
            RectTransform rt = rightSubSearchInput.GetComponent<RectTransform>();
            ApplySideTabSubFilterRowAnchor(rt, false);
            float tabAreaW = GalleryUiDesignTokens.SideTabColumnWidthRef * s;
            float margin = GalleryUiDesignTokens.SideTabSideMarginRef * s;
            float gap = GalleryUiDesignTokens.SideTabControlGapRef * s;
            float rowH = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            float bar = SceneSourceSortBarBreadth(s);
            bool reserveSortBar = rightSubSceneSortBarActive
                || (rightSubSortBtn != null && rightSubSortBtn.activeSelf);
            float subRowY = -GalleryUiDesignTokens.SideTabSubFilterRowTopGapRef * s;
            if (reserveSortBar)
            {
                rt.anchoredPosition = new Vector2(-margin - bar - gap, subRowY);
                rt.sizeDelta = new Vector2(tabAreaW - margin - bar - gap - margin, rowH);
            }
            else
            {
                rt.anchoredPosition = new Vector2(-margin, subRowY);
                rt.sizeDelta = new Vector2(tabAreaW - 2f * margin, rowH);
            }
            RescaleSearchInput(rightSubSearchInput, s, GalleryUiDesignTokens.SideTabRowHeightRef);
        }
        private float SubTabScrollPaneTopOffset()
        {
            float s = ChromeScale;
            float topGap = GalleryUiDesignTokens.SideTabSubFilterRowTopGapRef * s;
            float rowH = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            return -(topGap + rowH + topGap);
        }

        /// <summary>Keeps grid, side tab scrollers, and sub clear buttons above the footer; call after tbox height animates.</summary>
        private void SyncGalleryMainAreaBottomEdge(float leftOffset, float rightOffset, float topOffset, float tabTopOffset)
        {
            if (contentScrollRT == null) return;

            float gridBottomInset = GalleryMainAreaBottomInset();
            float tabBottomInset = SideTabScrollBottomInsetY();

            contentScrollRT.offsetMin = new Vector2(leftOffset, gridBottomInset);
            contentScrollRT.offsetMax = new Vector2(rightOffset, topOffset);

            // Category/subcategory split: raise split line when tall InfoBar would crush sub-pane.
            try { SyncSideTabSplitAgainstBottomChrome(tabBottomInset, tabTopOffset); } catch { }

            if (leftTabScrollGO != null && leftTabScrollGO.activeSelf)
            {
                RectTransform rt = leftTabScrollGO.GetComponent<RectTransform>();
                float tabBot = IsUpperStackedSideTabPane(rt) ? SideTabSplitSeamInset() : tabBottomInset;
                rt.offsetMin = new Vector2(rt.offsetMin.x, tabBot);
                rt.offsetMax = new Vector2(rt.offsetMax.x, tabTopOffset);
            }
            if (rightTabScrollGO != null && rightTabScrollGO.activeSelf)
            {
                RectTransform rt = rightTabScrollGO.GetComponent<RectTransform>();
                float tabBot = IsUpperStackedSideTabPane(rt) ? SideTabSplitSeamInset() : tabBottomInset;
                rt.offsetMin = new Vector2(rt.offsetMin.x, tabBot);
                rt.offsetMax = new Vector2(rt.offsetMax.x, tabTopOffset);
            }
            float subTabTop = SubTabScrollPaneTopOffset();
            if (leftSubTabScrollGO != null && leftSubTabScrollGO.activeSelf)
            {
                RectTransform rt = leftSubTabScrollGO.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, tabBottomInset);
                rt.offsetMax = new Vector2(rt.offsetMax.x, subTabTop);
            }
            if (rightSubTabScrollGO != null && rightSubTabScrollGO.activeSelf)
            {
                RectTransform rt = rightSubTabScrollGO.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, tabBottomInset);
                rt.offsetMax = new Vector2(rt.offsetMax.x, subTabTop);
            }
            if (leftSubClearBtn != null && leftSubClearBtn.activeSelf)
            {
                RectTransform rt = leftSubClearBtn.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, tabBottomInset);
            }
            if (rightSubClearBtn != null && rightSubClearBtn.activeSelf)
            {
                RectTransform rt = rightSubClearBtn.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, tabBottomInset);
            }

            try { ApplyUserTagsStickyScrollChrome(tabTopOffset); } catch { }
        }

        /// <summary>
        /// When InfoBar/detail-strip grows, fixed φ-split subcategory pane can shrink below usable height.
        /// Raise split (grow sub-pane) so category tags/subcategory lists stay scrollable above footer.
        /// </summary>
        private void SyncSideTabSplitAgainstBottomChrome(float tabBottomInset, float tabTopOffset)
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            if (backgroundBoxGO == null) return;
            RectTransform bg = backgroundBoxGO.GetComponent<RectTransform>();
            if (bg == null) return;
            float parentH = bg.rect.height;
            if (parentH < 120f) return;

            float minSubH = GalleryUiDesignTokens.SideTabSubPaneMinHeightRef * s;
            float minMainH = GalleryUiDesignTokens.SideTabMainPaneMinHeightRef * s;
            float subTopReserve = -SubTabScrollPaneTopOffset();
            if (subTopReserve < 0f) subTopReserve = 0f;
            float mainTopReserve = -tabTopOffset;
            if (mainTopReserve < 0f) mainTopReserve = 0f;
            float seam = SideTabSplitSeamInset();

            AdjustOneSideTabSplit(leftTabScrollGO, leftSubTabScrollGO, leftActiveContent,
                tabBottomInset, tabTopOffset, parentH, minSubH, minMainH, subTopReserve, mainTopReserve, seam);
            AdjustOneSideTabSplit(rightTabScrollGO, rightSubTabScrollGO, rightActiveContent,
                tabBottomInset, tabTopOffset, parentH, minSubH, minMainH, subTopReserve, mainTopReserve, seam);

            try { SyncSideTabSubFilterRowChrome(s); } catch { }
        }

        private static void AdjustOneSideTabSplit(
            GameObject mainScroll, GameObject subScroll, ContentType? activeContent,
            float tabBottomInset, float tabTopOffset, float parentH,
            float minSubH, float minMainH, float subTopReserve, float mainTopReserve, float seam)
        {
            if (mainScroll == null || subScroll == null || !subScroll.activeSelf) return;
            RectTransform mainRT = mainScroll.GetComponent<RectTransform>();
            RectTransform subRT = subScroll.GetComponent<RectTransform>();
            if (mainRT == null || subRT == null) return;
            // Only adjust true split stacks (sub pane not full-height).
            if (subRT.anchorMax.y >= 0.95f) return;

            float ideal = activeContent.HasValue
                ? SideTabSplitSubPaneTopAnchorY(activeContent.Value)
                : subRT.anchorMax.y;
            float minSplit = (tabBottomInset + subTopReserve + minSubH) / parentH;
            float maxSplit = 1f - (mainTopReserve + seam + minMainH) / parentH;
            float splitY = ideal;
            if (splitY < minSplit) splitY = minSplit;
            if (maxSplit > 0.2f && splitY > maxSplit) splitY = maxSplit;
            if (minSplit > maxSplit)
                splitY = Mathf.Clamp(minSplit, 0.22f, 0.72f);
            else
                splitY = Mathf.Clamp(splitY, 0.22f, 0.72f);

            float x = mainRT.anchorMin.x;
            mainRT.anchorMin = new Vector2(x, splitY);
            mainRT.anchorMax = new Vector2(x, 1f);
            subRT.anchorMin = new Vector2(x, 0f);
            subRT.anchorMax = new Vector2(x, splitY);
        }

        private void UpdateButtonState(Text btnText, bool isRight, ContentType type)
        {
             if (btnText == null) return;
             
             bool isOpen = isRight ? (rightActiveContent == type) : (leftActiveContent == type);
             
             if (isRight)
                 btnText.text = isOpen ? ">" : "<";
             else
                 btnText.text = isOpen ? "<" : ">";
        }

        public void SetFollowMode(bool enabled)
        {
            if (followUser != enabled)
            {
                ToggleFollowMode();
            }
        }

        public bool GetFollowMode()
        {
            return followUser;
        }

        public void ToggleFollowMode()
        {
            followUser = !followUser;
            if (followUser)
            {
                lastFollowUpdateTime = 0f; // Force immediate update
                if (canvas != null)
                {
                    targetFollowRotation = canvas.transform.rotation;
                    
                    if (_cachedCamera == null) _cachedCamera = Camera.main;
                    if (_cachedCamera != null)
                    {
                        Vector3 offset = canvas.transform.position - _cachedCamera.transform.position;
                        followYOffset = offset.y;
                        followXZOffset = new Vector2(offset.x, offset.z);
                        offsetsInitialized = true;
                    }
                }
            }
            UpdateFollowButtonState();
        }

        private void UpdateFollowButtonState()
        {
            string text = followUser
                ? VPBTranslation.T("gallery.follow.follow", "Follow")
                : VPBTranslation.T("gallery.follow.static", "Static");
            Color color = followUser ? UI.AccentBlue : UI.ChromeMid;
            Sprite spr = followUser ? galleryFollowOnSprite : galleryFollowOffSprite;

            if (rightFollowBtnIconImage != null && spr != null)
            {
                rightFollowBtnIconImage.sprite = spr;
                rightFollowBtnIconImage.enabled = true;
                if (rightFollowBtnText != null) rightFollowBtnText.gameObject.SetActive(false);
            }
            else if (rightFollowBtnText != null)
            {
                rightFollowBtnText.gameObject.SetActive(true);
                rightFollowBtnText.text = text;
            }

            if (rightFollowBtnImage != null) rightFollowBtnImage.color = color;

            if (leftFollowBtnIconImage != null && spr != null)
            {
                leftFollowBtnIconImage.sprite = spr;
                leftFollowBtnIconImage.enabled = true;
                if (leftFollowBtnText != null) leftFollowBtnText.gameObject.SetActive(false);
            }
            else if (leftFollowBtnText != null)
            {
                leftFollowBtnText.gameObject.SetActive(true);
                leftFollowBtnText.text = text;
            }

            if (leftFollowBtnImage != null) leftFollowBtnImage.color = color;
        }

        public void UpdateSideButtonPositions()
        {
            if (backgroundBoxGO == null) return;
            try { EnforceCreatorSideRailButtonVisibilityFromConfig(); } catch { }
            if (IsFixedTopDockMode())
            {
                ApplyTopDockSideButtonsLayout(ChromeScale);
                return;
            }
            float scale = ChromeScale;
            float spacing;
            float groupGap;
            try { ApplySideRailHeightFit(scale, out spacing, out groupGap); }
            catch
            {
                spacing = GalleryUiDesignTokens.SideButtonSpacingRef * scale;
                groupGap = VPBConfig.Instance != null && VPBConfig.Instance.EnableButtonGaps
                    ? GalleryUiDesignTokens.SideButtonGroupGapRef * scale
                    : 0f;
            }
            float stackHeight = GetSideButtonsStackHeight(spacing, groupGap);
            if (_sideRailOverflowCollapsedIdx.Count > 0)
                stackHeight += spacing;
            float topY = stackHeight * 0.5f;

            // Settings
            UpdateListPositions(rightSideButtons, topY, spacing, groupGap, isLeftRail: false);
            UpdateListPositions(leftSideButtons, topY, spacing, groupGap, isLeftRail: true);
            try { PlaceSideRailOverflowButtons(topY, spacing, groupGap, scale); } catch { }
            try { SyncSideRailOpenFacetChrome(); } catch { }

            try
            {
                string title = currentCategoryTitle ?? "";
                bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSubScene = title.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isScene = !isSubScene && title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isHair && hairSubmenuOpen)
                {
                    float removeHorizGap = 3f * scale;

                    RectTransform leftBaseRT = leftRemoveAllHairBtn != null ? leftRemoveAllHairBtn.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightRemoveAllHairBtn != null ? rightRemoveAllHairBtn.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftRemoveHairSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftRemoveHairSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightRemoveHairSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveHairSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }
                    // 5a — anchor on first layout; reuse on removal resyncs to prevent jump
                    if (float.IsNaN(_hairSubmenuAnchorYStart))
                        _hairSubmenuAnchorYStart = -(visibleCount - 1) * 0.5f * spacing;
                    float yStart = _hairSubmenuAnchorYStart;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        RectTransform pr = leftBaseRT.parent as RectTransform;
                        for (int i = 0; i < leftRemoveHairSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftRemoveHairSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(leftBaseRT, pr, anchorOnRightSidePanel: false, removeHorizGap, w * 0.5f, out cx))
                                cx = -(w * 0.5f) - 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + spacing * i);
                        }

                        // Removed - submenus are now handled by side tabs
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        RectTransform pr = rightBaseRT.parent as RectTransform;
                        for (int i = 0; i < rightRemoveHairSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveHairSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(rightBaseRT, pr, anchorOnRightSidePanel: true, removeHorizGap, w * 0.5f, out cx))
                                cx = (w * 0.5f) + 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + spacing * i);
                        }

                        // Removed - submenus are now handled by side tabs
                    }
                }

                if (isClothing && clothingSubmenuOpen)
                {
                    float removeHorizGap = 3f * scale;
                    float colGap = 10f;

                    RectTransform leftBaseRT = leftRemoveAllClothingBtn != null ? leftRemoveAllClothingBtn.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightRemoveAllClothingBtn != null ? rightRemoveAllClothingBtn.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftRemoveClothingSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftRemoveClothingSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightRemoveClothingSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveClothingSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }
                    // 5a — anchor on first layout; reuse on removal resyncs to prevent jump
                    if (float.IsNaN(_clothingSubmenuAnchorYStart))
                        _clothingSubmenuAnchorYStart = -(visibleCount - 1) * 0.5f * spacing;
                    float yStart = _clothingSubmenuAnchorYStart;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        RectTransform pr = leftBaseRT.parent as RectTransform;
                        for (int i = 0; i < leftRemoveClothingSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftRemoveClothingSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(leftBaseRT, pr, anchorOnRightSidePanel: false, removeHorizGap, w * 0.5f, out cx))
                                cx = -(w * 0.5f) - 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + spacing * i);
                        }

                        for (int i = 0; i < leftRemoveClothingVisibilityToggleButtons.Count; i++)
                        {
                            GameObject go = leftRemoveClothingVisibilityToggleButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;

                            float itemW = 0f;
                            try
                            {
                                if (i < leftRemoveClothingSubmenuButtons.Count)
                                {
                                    RectTransform irt = leftRemoveClothingSubmenuButtons[i] != null ? leftRemoveClothingSubmenuButtons[i].GetComponent<RectTransform>() : null;
                                    if (irt != null) itemW = irt.sizeDelta.x;
                                }
                            }
                            catch { }
                            if (itemW <= 0f) itemW = 200f;

                            float w = rt.sizeDelta.x;
                            float itemCenterX;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(leftBaseRT, pr, anchorOnRightSidePanel: false, removeHorizGap, itemW * 0.5f, out itemCenterX))
                                itemCenterX = -(itemW * 0.5f) - 80f * scale;
                            rt.anchoredPosition = new Vector2(itemCenterX - (itemW * 0.5f) - (w * 0.5f) - colGap, baseY + yStart + spacing * i);
                        }
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        RectTransform pr = rightBaseRT.parent as RectTransform;
                        for (int i = 0; i < rightRemoveClothingSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveClothingSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(rightBaseRT, pr, anchorOnRightSidePanel: true, removeHorizGap, w * 0.5f, out cx))
                                cx = (w * 0.5f) + 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + spacing * i);
                        }

                        for (int i = 0; i < rightRemoveClothingVisibilityToggleButtons.Count; i++)
                        {
                            GameObject go = rightRemoveClothingVisibilityToggleButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;

                            float itemW = 0f;
                            try
                            {
                                if (i < rightRemoveClothingSubmenuButtons.Count)
                                {
                                    RectTransform irt = rightRemoveClothingSubmenuButtons[i] != null ? rightRemoveClothingSubmenuButtons[i].GetComponent<RectTransform>() : null;
                                    if (irt != null) itemW = irt.sizeDelta.x;
                                }
                            }
                            catch { }
                            if (itemW <= 0f) itemW = 200f;

                            float w = rt.sizeDelta.x;
                            float itemCenterX;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(rightBaseRT, pr, anchorOnRightSidePanel: true, removeHorizGap, itemW * 0.5f, out itemCenterX))
                                itemCenterX = (itemW * 0.5f) + 80f * scale;
                            rt.anchoredPosition = new Vector2(itemCenterX + (itemW * 0.5f) + (w * 0.5f) + colGap, baseY + yStart + spacing * i);
                        }

                        // Removed - submenus are now handled by side tabs
                    }
                }

                if (isScene && atomSubmenuOpen)
                {
                    float removeHorizGap = 3f * scale;

                    RectTransform leftBaseRT = leftRemoveAtomBtn != null ? leftRemoveAtomBtn.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightRemoveAtomBtn != null ? rightRemoveAtomBtn.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftRemoveAtomSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftRemoveAtomSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightRemoveAtomSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveAtomSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }
                    float yStart = -(visibleCount - 1) * 0.5f * spacing;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        RectTransform pr = leftBaseRT.parent as RectTransform;
                        for (int i = 0; i < leftRemoveAtomSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftRemoveAtomSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(leftBaseRT, pr, anchorOnRightSidePanel: false, removeHorizGap, w * 0.5f, out cx))
                                cx = -(w * 0.5f) - 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + spacing * i);
                        }
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        RectTransform pr = rightBaseRT.parent as RectTransform;
                        for (int i = 0; i < rightRemoveAtomSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveAtomSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(rightBaseRT, pr, anchorOnRightSidePanel: true, removeHorizGap, w * 0.5f, out cx))
                                cx = (w * 0.5f) + 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + spacing * i);
                        }
                    }
                }

                if (saveSubmenuOpen)
                {
                    // Tighter than main-rail spacing; row height follows actual submenu <see cref="RectTransform.sizeDelta"/>.y.
                    const float saveSubGapY = 2f;
                    float horizGap = 3f * scale;

                    RectTransform leftBaseRT = leftSaveBtnGO != null ? leftSaveBtnGO.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightSaveBtnGO != null ? rightSaveBtnGO.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftSaveSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftSaveSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightSaveSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightSaveSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }

                    GameObject sampleGo = leftSaveSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf)
                        ?? rightSaveSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                    RectTransform sampleRt = sampleGo != null ? sampleGo.GetComponent<RectTransform>() : null;
                    float rowH = sampleRt != null && sampleRt.sizeDelta.y > 2f
                        ? sampleRt.sizeDelta.y
                        : (50f * scale);
                    float saveSubRowStep = rowH + (saveSubGapY * scale);
                    float yStart = -(visibleCount - 1) * 0.5f * saveSubRowStep;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        RectTransform pr = leftBaseRT.parent as RectTransform;

                        for (int i = 0; i < leftSaveSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftSaveSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(leftBaseRT, pr, anchorOnRightSidePanel: false, horizGap, w * 0.5f, out cx))
                                cx = -(w * 0.5f) - 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + saveSubRowStep * i);
                        }

                        // Removed - submenus are now handled by side tabs
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        RectTransform pr = rightBaseRT.parent as RectTransform;

                        for (int i = 0; i < rightSaveSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightSaveSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            float cx;
                            if (!TryComputeOuterSubmenuAnchoredCenterX(rightBaseRT, pr, anchorOnRightSidePanel: true, horizGap, w * 0.5f, out cx))
                                cx = (w * 0.5f) + 80f * scale;
                            rt.anchoredPosition = new Vector2(cx, baseY + yStart + saveSubRowStep * i);
                        }

                        // Removed - submenus are now handled by side tabs
                    }
                }
            }
            catch { }
        }

        private bool IsFixedTopDockMode()
        {
            if (!isFixedLocally) return false;
            if (VPBConfig.Instance == null) return false;
            string dock = "Right";
            try { dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide); } catch { dock = "Right"; }
            return string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyTopDockSideButtonsLayout(float paneScale)
        {
            if (_footerSideButtonsGroupRT == null || _footerSideButtonsGroupGO == null) return;
            if (_footerLeftSectionRT == null || _footerRightSectionRT == null) return;
            if (paginationRT == null) return;
            if (leftSideButtons == null || leftSideButtons.Count == 0) return;

            bool active = IsFixedTopDockMode() && !isCollapsed;

            if (!active)
            {
                if (_footerSideButtonsGroupGO.activeSelf) _footerSideButtonsGroupGO.SetActive(false);
                if (_titleBarSideButtonsReparented)
                {
                    _titleBarSideButtonsReparented = false;
                    if (leftSideContainer != null)
                    {
                        for (int i = 0; i < leftSideButtons.Count; i++)
                        {
                            RectTransform rt = leftSideButtons[i];
                            if (rt == null) continue;
                            rt.SetParent(leftSideContainer.transform, worldPositionStays: false);
                        }
                    }
                }
                if (_leftSideRailOverflowBtnRT != null && leftSideContainer != null
                    && _leftSideRailOverflowBtnRT.parent != leftSideContainer.transform)
                    _leftSideRailOverflowBtnRT.SetParent(leftSideContainer.transform, worldPositionStays: false);
                if (_leftSideRailOverflowBtnGO != null) _leftSideRailOverflowBtnGO.SetActive(false);
                if (_rightSideRailOverflowBtnGO != null) _rightSideRailOverflowBtnGO.SetActive(false);
                return;
            }

            // Hide side rails in Top dock; buttons move to title bar.
            if (leftSideContainer != null && leftSideContainer.activeSelf) leftSideContainer.SetActive(false);
            if (rightSideContainer != null && rightSideContainer.activeSelf) rightSideContainer.SetActive(false);

            if (!_footerSideButtonsGroupGO.activeSelf) _footerSideButtonsGroupGO.SetActive(true);

            if (!_titleBarSideButtonsReparented)
            {
                _titleBarSideButtonsReparented = true;
                for (int i = 0; i < leftSideButtons.Count; i++)
                {
                    RectTransform rt = leftSideButtons[i];
                    if (rt == null) continue;
                    rt.SetParent(_footerSideButtonsGroupRT, worldPositionStays: false);
                }
            }

            float s = paneScale <= 0f ? 1f : paneScale;
            RectTransform footerRT = paginationRT;

            if (footerRT != null && footerRT.gameObject.activeInHierarchy)
            {
                try { LayoutRebuilder.ForceRebuildLayoutImmediate(footerRT); } catch { }
            }
            try { Canvas.ForceUpdateCanvases(); } catch { }

            // Compute free space between left/right footer sections.
            Bounds bLeft = RectTransformUtility.CalculateRelativeRectTransformBounds(footerRT, _footerLeftSectionRT);
            Bounds bRight = RectTransformUtility.CalculateRelativeRectTransformBounds(footerRT, _footerRightSectionRT);
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            List<RectTransform> buttonList = leftSideButtons;
            bool showSceneImport = ImportSidebarCategoryAllowed() && !cleanupModeActive && !IsSettingsPanelOpen();
            float gap = 10f * s;
            float btnSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject sceneImportGo = leftSceneImportSideBtn != null ? leftSceneImportSideBtn : rightSceneImportSideBtn;

            float availW = Mathf.Max(btnSz, bRight.min.x - bLeft.max.x - 8f * s);
            try { ApplyTopDockSideButtonsOverflowFit(s, buttonList, layout, availW, btnSz, ref gap); } catch { }

            _footerSideButtonsGroupRT.localScale = Vector3.one;

            float x0 = 0f;
            bool firstInRow = true;
            for (int i = 0; i < layout.Length; i++)
            {
                int idx = layout[i].buttonIndex;
                if (idx < 0 || idx >= buttonList.Count) continue;
                if (_sideRailOverflowCollapsedIdx.Contains(idx)) continue;

                RectTransform rt = buttonList[idx];
                bool isSceneImport = sceneImportGo != null && rt != null && rt.gameObject == sceneImportGo;

                if (rt == null || !rt.gameObject.activeSelf)
                {
                    if (isSceneImport && !showSceneImport)
                    {
                        if (!firstInRow) x0 += gap;
                        x0 += btnSz;
                        firstInRow = false;
                    }
                    continue;
                }

                if (!firstInRow) x0 += gap;

                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnSz, btnSz);
                rt.anchoredPosition = new Vector2(x0, 0f);
                ScaleButtonIconPadding(rt, s);
                x0 += btnSz;
                firstInRow = false;
            }

            if (_sideRailOverflowCollapsedIdx.Count > 0 && _leftSideRailOverflowBtnRT != null && _leftSideRailOverflowBtnGO != null
                && _leftSideRailOverflowBtnGO.activeSelf)
            {
                if (!firstInRow) x0 += gap;
                RectTransform ov = _leftSideRailOverflowBtnRT;
                ov.anchorMin = ov.anchorMax = new Vector2(0f, 0.5f);
                ov.pivot = new Vector2(0f, 0.5f);
                ov.sizeDelta = new Vector2(btnSz, btnSz);
                ov.anchoredPosition = new Vector2(x0, 0f);
                x0 += btnSz;
            }

            float groupW = x0;
            float groupH = btnSz;
            _footerSideButtonsGroupRT.sizeDelta = new Vector2(groupW, groupH);

            // Center in free gap between left/right packs (not panel mid / equal thirds).
            float cx = (bLeft.max.x + bRight.min.x) * 0.5f;
            _footerSideButtonsGroupRT.anchoredPosition = new Vector2(cx, 0f);
        }

        /// <summary>
        /// Edge-anchored side button vs centre-anchored submenu rows: submenu opens on the outer side of the rail
        /// (left rail: left of anchor; right rail: right of anchor), away from the gallery. Used for Save and Remove submenus.
        /// </summary>
        private static bool TryComputeOuterSubmenuAnchoredCenterX(RectTransform anchorBtnRT, RectTransform parentRT, bool anchorOnRightSidePanel, float horizGap, float subHalfW, out float anchoredCenterX)
        {
            anchoredCenterX = 0f;
            if (anchorBtnRT == null || parentRT == null) return false;
            try
            {
                Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(parentRT, anchorBtnRT);
                // Outer edge away from gallery: left rail uses anchor's left (min.x); right rail uses anchor's right (max.x).
                float outerEdgeX = anchorOnRightSidePanel ? b.max.x : b.min.x;
                float pivotLocalX = anchorOnRightSidePanel
                    ? (outerEdgeX + horizGap + subHalfW)
                    : (outerEdgeX - horizGap - subHalfW);
                anchoredCenterX = pivotLocalX - parentRT.rect.center.x;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void CollectLayoutOrderedVisibleSideButtons(List<RectTransform> dest, List<RectTransform> buttonList)
        {
            if (dest == null || buttonList == null) return;
            dest.Clear();
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            for (int i = 0; i < layout.Length; i++)
            {
                int idx = layout[i].buttonIndex;
                if (idx < 0 || idx >= buttonList.Count) continue;
                RectTransform rt = buttonList[idx];
                if (rt == null || !rt.gameObject.activeSelf) continue;
                dest.Add(rt);
            }
        }

        private SideButtonLayoutEntry[] GetSideButtonsLayout()
        {
            string title = currentCategoryTitle ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSubScene = title.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isScene = !isSubScene && title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;

            int idxFloating = -1;
            int idxClone = -1;
            int idxFollow = -1;
            int idxCategory = -1;
            int idxCreator = -1;
            int idxPath = -1;
            int idxHistory = -1;
            int idxUserTags = -1;
            int idxSceneImport = -1;
            int idxTarget = -1;
            int idxApplyMode = -1;
            int idxRemoveMode = -1;
            int idxRemoveHair = 15;
            int idxRemoveClothing = 14;
            int idxRemoveAtom = -1;
            int idxSave = -1;
            try
            {
                List<RectTransform> refList = rightSideButtons;
                if (refList == null || refList.Count == 0) refList = leftSideButtons;
                if (refList != null)
                {
                    int FindIndexByTextRef(Text t)
                    {
                        if (t == null) return -1;
                        return refList.FindIndex(rt => rt != null && rt.GetComponentInChildren<Text>(true) == t);
                    }


                    idxCategory = FindIndexByTextRef(rightCategoryBtnText != null ? rightCategoryBtnText : leftCategoryBtnText);
                    // Hide-creator setting: omit from layout so button never takes a rail/footer slot.
                    if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryHideCreatorSideButtons)
                        idxCreator = FindIndexByTextRef(rightCreatorBtnText != null ? rightCreatorBtnText : leftCreatorBtnText);
                    else
                        idxCreator = -1;
                    idxPath = FindIndexByTextRef(rightPathBtnText != null ? rightPathBtnText : leftPathBtnText);
                    // idxTarget: target button moved to toolbox, no longer a side button
                    idxApplyMode = FindIndexByTextRef(rightApplyModeBtnText != null ? rightApplyModeBtnText : leftApplyModeBtnText);
                    idxFloating = FindIndexByTextRef(rightDesktopModeBtnText != null ? rightDesktopModeBtnText : leftDesktopModeBtnText);
                    idxFollow = FindIndexByTextRef(rightFollowBtnText != null ? rightFollowBtnText : leftFollowBtnText);

                    idxClone    = FindIndexByTextRef(rightCloneBtnText    != null ? rightCloneBtnText    : leftCloneBtnText);

                    GameObject utGo = rightUserTagsSideBtn != null ? rightUserTagsSideBtn : leftUserTagsSideBtn;
                    if (utGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == utGo);
                        if (i >= 0) idxUserTags = i;
                    }

                    GameObject impGo = rightSceneImportSideBtn != null ? rightSceneImportSideBtn : leftSceneImportSideBtn;
                    if (impGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == impGo);
                        if (i >= 0) idxSceneImport = i;
                    }

                    if (rightRemoveAllHairBtn != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == rightRemoveAllHairBtn);
                        if (i >= 0) idxRemoveHair = i;
                    }

                    if (rightRemoveAllClothingBtn != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == rightRemoveAllClothingBtn);
                        if (i >= 0) idxRemoveClothing = i;
                    }

                    GameObject removeAtomGo = rightRemoveAtomBtn != null ? rightRemoveAtomBtn : leftRemoveAtomBtn;
                    if (removeAtomGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == removeAtomGo);
                        if (i >= 0) idxRemoveAtom = i;
                    }

                    GameObject removeModeGo = rightRemoveModeSideBtn != null ? rightRemoveModeSideBtn : leftRemoveModeSideBtn;
                    if (removeModeGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == removeModeGo);
                        if (i >= 0) idxRemoveMode = i;
                    }

                    GameObject saveGo = rightSaveBtnGO != null ? rightSaveBtnGO : leftSaveBtnGO;
                    if (saveGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == saveGo);
                        if (i >= 0) idxSave = i;
                    }

                    GameObject histGo = rightHistoryBtnImage != null ? rightHistoryBtnImage.gameObject : null;
                    if (histGo == null && leftHistoryBtnImage != null) histGo = leftHistoryBtnImage.gameObject;
                    if (histGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == histGo);
                        if (i >= 0) idxHistory = i;
                    }
                }
            }
            catch { }

            bool showSceneImport = ImportSidebarCategoryAllowed() && !cleanupModeActive && !IsSettingsPanelOpen();
            int zone = GalleryUiDesignTokens.SideButtonZoneGapTier;

            var layout = new List<SideButtonLayoutEntry>()
            {
                // ── Layout zone ──────────────────────────────────────────────
                new SideButtonLayoutEntry(idxFloating, 0, zone), // Floating / Fixed
                new SideButtonLayoutEntry(idxClone, 0, 0),
                new SideButtonLayoutEntry(idxFollow, 0, 0),

                // ── Browse facets ────────────────────────────────────────────
                new SideButtonLayoutEntry(idxSceneImport, 0, zone), // Import (Scenes only)
                new SideButtonLayoutEntry(idxUserTags, 0, showSceneImport ? 0 : zone), // Tags — keep zone gap when Import hidden
                new SideButtonLayoutEntry(idxCategory, 0, 0),
                new SideButtonLayoutEntry(idxCreator, 0, 0),
                new SideButtonLayoutEntry(idxPath, 0, 0),
                new SideButtonLayoutEntry(idxHistory, 0, 0),

                // ── Tools ────────────────────────────────────────────────────
                new SideButtonLayoutEntry(idxRemoveMode, 0, zone), // Remove Item Mode
                new SideButtonLayoutEntry(idxApplyMode, 0, 0),
                new SideButtonLayoutEntry(idxSave, 0, 0),
                new SideButtonLayoutEntry(idxTarget, 0, 0), // legacy index (usually -1)

                // Category-context remove lists (same tools family, slight sub-gap)
                new SideButtonLayoutEntry(idxRemoveClothing, 0, 2),
                new SideButtonLayoutEntry(idxRemoveAtom, 0, 0),
                new SideButtonLayoutEntry(idxRemoveHair, 0, 0),
            };

            return layout.ToArray();
        }

        private void SetAtomSubmenuButtonsVisible(bool visible)
        {
            // Removed - submenus are now handled by side tabs
        }

        private void PopulateAtomSubmenuButtons()
        {
            // Removed - submenus are now handled by side tabs
        }

        private void ToggleAtomSubmenuFromSideButtons(bool? forceLeftSide = null)
        {
            bool useLeftSide = forceLeftSide ?? isFixedLocally;
            CloseOtherSideIfSubmenu(useLeftSide);
            if (useLeftSide)
            {
                if (leftActiveContent == ContentType.RemoveAtom)
                {
                    leftActiveContent = leftPrevActiveContent;
                }
                else
                {
                    leftPrevActiveContent = leftActiveContent;
                    leftActiveContent = ContentType.RemoveAtom;
                }
            }
            else
            {
                if (rightActiveContent == ContentType.RemoveAtom)
                {
                    rightActiveContent = rightPrevActiveContent;
                }
                else
                {
                    rightPrevActiveContent = rightActiveContent;
                    rightActiveContent = ContentType.RemoveAtom;
                }
            }
            UpdateLayout();
            UpdateTabs();
        }

        private void ToggleTargetSubmenuFromSideButtons(bool? forceLeftSide = null)
        {
            // Removed: target selection is now in the toolbox.
        }

        private void PushUndoSnapshotForAtomRemoval(Atom atom)
        {
            if (atom == null) return;
            if (SuperController.singleton == null) return;

            try
            {
                string atomUid = null;
                try { atomUid = atom.uid; } catch { }
                if (string.IsNullOrEmpty(atomUid)) return;

                JSONNode atomNode = null;

                // Primary: Atom.Store serializes the atom into a JSONArray (same call Try-On uses).
                // This is the reliable path; the reflection probes below are a version fallback.
                try
                {
                    JSONArray storeArr = new JSONArray();
                    atom.Store(storeArr, true, true);
                    if (storeArr.Count > 0) atomNode = storeArr[0];
                }
                catch { }

                try
                {
                    // Reflection fallback only if Store did not capture (loop is gated on atomNode == null).
                    string[] candidates = new[] { "GetSaveJSON", "GetJSON", "GetAtomJSON", "GetSceneJSON" };
                    for (int i = 0; i < candidates.Length && atomNode == null; i++)
                    {
                        MethodInfo mi = null;
                        try { mi = atom.GetType().GetMethod(candidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch { }
                        if (mi == null) continue;
                        var ps = mi.GetParameters();
                        if (ps != null && ps.Length != 0) continue;
                        object result = null;
                        try { result = mi.Invoke(atom, null); }
                        catch { }
                        if (result == null) continue;
                        if (result is JSONNode node)
                        {
                            atomNode = node;
                        }
                        else
                        {
                            try
                            {
                                string s = result.ToString();
                                if (!string.IsNullOrEmpty(s)) atomNode = JSON.Parse(s);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                if (atomNode == null) return;

                // Ensure id exists for merge-load
                try
                {
                    if (atomNode["id"] == null || string.IsNullOrEmpty(atomNode["id"].Value)) atomNode["id"] = atomUid;
                }
                catch { }

                // Freeze to a string now (atom may be removed after this)
                string atomJson = null;
                try { atomJson = atomNode.ToString(); }
                catch { }
                if (string.IsNullOrEmpty(atomJson)) return;

                JSONClass mini = new JSONClass();
                JSONArray one = new JSONArray();
                try { one.Add(JSON.Parse(atomJson)); }
                catch { one.Add(atomNode); }
                mini["atoms"] = one;

                string undoTempPath = Path.Combine(SuperController.singleton.savesDir, "vpb_temp_undo_atom_" + Guid.NewGuid().ToString() + ".json");
                File.WriteAllText(undoTempPath, VPB.src.util.JsonSerializationUtil.Serialize(mini, 100_000));

                PushUndo(() => {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        if (!File.Exists(undoTempPath)) return;

                        SceneLoadingUtils.LoadScene(undoTempPath, true);
                    }
                    catch { }
                    finally
                    {
                        try { if (File.Exists(undoTempPath)) File.Delete(undoTempPath); } catch { }
                    }
                });
            }
            catch { }
        }

        private void SetHairSubmenuButtonsVisible(bool visible)
        {
            // Removed - submenus are now handled by side tabs
        }

        private void SetClothingSubmenuButtonsVisible(bool visible)
        {
            // Removed - submenus are now handled by side tabs
        }

        private void PopulateHairSubmenuButtons(Atom target)
        {
            // Removed - submenus are now handled by side tabs
        }

        private void ToggleHairSubmenuFromSideButtons(Atom target, bool? forceLeftSide = null)
        {
            bool useLeftSide = forceLeftSide ?? isFixedLocally;
            CloseOtherSideIfSubmenu(useLeftSide);
            if (useLeftSide)
            {
                if (leftActiveContent == ContentType.RemoveHair)
                {
                    leftActiveContent = leftPrevActiveContent;
                }
                else
                {
                    leftPrevActiveContent = leftActiveContent;
                    leftActiveContent = ContentType.RemoveHair;
                }
            }
            else
            {
                if (rightActiveContent == ContentType.RemoveHair)
                {
                    rightActiveContent = rightPrevActiveContent;
                }
                else
                {
                    rightPrevActiveContent = rightActiveContent;
                    rightActiveContent = ContentType.RemoveHair;
                }
            }
            UpdateLayout();
            UpdateTabs();
        }

        private void CloseHairSubmenuUI()
        {
            try
            {
                ClearHairPreview();
                if (leftActiveContent == ContentType.RemoveHair) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.RemoveHair) rightActiveContent = rightPrevActiveContent;
                UpdateTabs();
            }
            catch { }
        }

        private void SyncHairSubmenu(Atom target, bool keepOpenIfHasOptions)
        {
            if (target == null) { CloseHairSubmenuUI(); return; }
            UpdateTabs();
        }

        private void UpdateRemoveHairButtonLabels(int optionCount)
        {
            UpdateRemoveButtonLabels(leftRemoveAllHairBtn, rightRemoveAllHairBtn, "Remove\nHair", optionCount);
        }

        private void ApplyHairPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;

                if (!string.IsNullOrEmpty(previewRemoveHairAtomUid))
                {
                    if (!string.Equals(previewRemoveHairAtomUid, target.uid, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previewRemoveHairItemUid, itemUid, StringComparison.OrdinalIgnoreCase) ||
                        isPreviewRemoveHairAll)
                    {
                        ClearHairPreview();
                    }
                }

                if (!string.IsNullOrEmpty(previewRemoveHairAtomUid))
                {
                    return;
                }

                JSONStorable geometry = null;
                try { geometry = target.GetStorableByID("geometry"); } catch { }
                if (geometry == null) return;

                JSONStorableBool active = null;
                try { active = geometry.GetBoolJSONParam("hair:" + itemUid); } catch { }
                if (active == null) return;

                previewRemoveHairAtomUid = target.uid;
                previewRemoveHairItemUid = itemUid;
                previewRemoveHairPrevGeometryVal = active.val;
                isPreviewRemoveHairAll = false;

                if (active.val) active.val = false;
            }
            catch { }
        }

        private void ApplyHairAllPreview(Atom target)
        {
            try
            {
                if (target == null) return;

                if (!string.IsNullOrEmpty(previewRemoveHairAtomUid))
                {
                    if (!string.Equals(previewRemoveHairAtomUid, target.uid, StringComparison.OrdinalIgnoreCase) || !isPreviewRemoveHairAll)
                    {
                        ClearHairPreview();
                    }
                }

                if (!string.IsNullOrEmpty(previewRemoveHairAtomUid)) return;

                DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                if (dcs == null || dcs.hairItems == null) return;

                previewRemoveHairAtomUid = target.uid;
                previewRemoveHairItemUid = "ALL";
                isPreviewRemoveHairAll = true;
                previewRemoveHairAllItemUids.Clear();
                previewRemoveHairAllPrevVals.Clear();

                foreach (var item in dcs.hairItems)
                {
                    if (item == null || !item.active) continue;
                    previewRemoveHairAllItemUids.Add(item.uid);
                    previewRemoveHairAllPrevVals.Add(item.active);
                    item.active = false;
                }
            }
            catch { }
        }

        private void ClearHairPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;
                if (string.IsNullOrEmpty(previewRemoveHairAtomUid) || string.IsNullOrEmpty(previewRemoveHairItemUid)) return;
                if (!string.Equals(previewRemoveHairAtomUid, target.uid, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.Equals(previewRemoveHairItemUid, itemUid, StringComparison.OrdinalIgnoreCase)) return;
                RestoreHairPreview();
            }
            catch { }
        }

        private void ClearHairAllPreview(Atom target)
        {
            try
            {
                if (target == null) return;
                if (string.IsNullOrEmpty(previewRemoveHairAtomUid) || !isPreviewRemoveHairAll) return;
                if (!string.Equals(previewRemoveHairAtomUid, target.uid, StringComparison.OrdinalIgnoreCase)) return;
                RestoreHairPreview();
            }
            catch { }
        }

        private void ClearHairPreview()
        {
            try { RestoreHairPreview(); }
            catch { }
        }

        private void RestoreHairPreview()
        {
            try
            {
                if (string.IsNullOrEmpty(previewRemoveHairAtomUid))
                {
                    ResetHairPreviewFields();
                    return;
                }

                Atom atom = null;
                try { atom = SuperController.singleton.GetAtomByUid(previewRemoveHairAtomUid); } catch { }
                if (atom == null)
                {
                    ResetHairPreviewFields();
                    return;
                }

                if (isPreviewRemoveHairAll)
                {
                    DAZCharacterSelector dcs = atom.GetComponentInChildren<DAZCharacterSelector>();
                    if (dcs != null && dcs.hairItems != null)
                    {
                        for (int i = 0; i < previewRemoveHairAllItemUids.Count; i++)
                        {
                            try
                            {
                                string uid = previewRemoveHairAllItemUids[i];
                                foreach (var item in dcs.hairItems)
                                {
                                    if (item != null && string.Equals(item.uid, uid, StringComparison.OrdinalIgnoreCase))
                                    {
                                        item.active = previewRemoveHairAllPrevVals[i];
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(previewRemoveHairItemUid))
                {
                    JSONStorable geometry = null;
                    try { geometry = atom.GetStorableByID("geometry"); } catch { }
                    if (geometry != null)
                    {
                        JSONStorableBool active = null;
                        try { active = geometry.GetBoolJSONParam("hair:" + previewRemoveHairItemUid); } catch { }
                        if (active != null && previewRemoveHairPrevGeometryVal.HasValue)
                        {
                            active.val = previewRemoveHairPrevGeometryVal.Value;
                        }
                    }
                }

                ResetHairPreviewFields();
            }
            catch
            {
                ResetHairPreviewFields();
            }
        }

        private void ResetHairPreviewFields()
        {
            previewRemoveHairAtomUid = null;
            previewRemoveHairItemUid = null;
            previewRemoveHairPrevGeometryVal = null;
            isPreviewRemoveHairAll = false;
            previewRemoveHairAllItemUids.Clear();
            previewRemoveHairAllPrevVals.Clear();
        }

        private void RefreshSceneImportSideButtonVisibility()
        {
            bool show = ImportSidebarCategoryAllowed() && !cleanupModeActive && !IsSettingsPanelOpen();
            if (rightSceneImportSideBtn != null && rightSceneImportSideBtn.activeSelf != show)
                rightSceneImportSideBtn.SetActive(show);
            if (leftSceneImportSideBtn != null && leftSceneImportSideBtn.activeSelf != show)
                leftSceneImportSideBtn.SetActive(show);
        }

        // True when the current selection contains an appearance preset. Used to reveal the
        // appearance clothing-mode row in mixed views (History) only when it's relevant.
        private bool SelectionContainsAppearance()
        {
            if (selectedFiles == null) return false;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                if (TryOnClassify(f) == TryOnKind.Appearance) return true;
            }
            return false;
        }

        private void UpdateSideContextActions()
        {
            RefreshSceneImportSideButtonVisibility();

            string title = currentCategoryTitle ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSubScene = title.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isScene = !isSubScene && title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isAppearance = title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0;
            bool showSave = true;

            // Appearance clothing-apply-mode segmented row (Full Look / Keep My Outfit / Outfit Only / Merge Outfit)
            // lives in the toolbox. Shown while browsing the Appearance category, and in History mode
            // (mixed items) only when the selected item is actually an appearance preset — so it isn't
            // always on when History contains scenes, clothing, etc.
            bool isHistoryBrowse = activeContentType == ContentType.History
                || leftActiveContent == ContentType.History
                || rightActiveContent == ContentType.History;
            bool showClothingMode = isAppearance || (isHistoryBrowse && SelectionContainsAppearance());
            if (tboxClothingModeRowGO != null && tboxClothingModeRowGO.activeSelf != showClothingMode)
            {
                tboxClothingModeRowGO.SetActive(showClothingMode);
                if (showClothingMode) UpdateKeepClothingButtonState();
                try { RefreshTboxFlexButtonLayout(); } catch { }
            }
            if (rightRemoveAllClothingBtn != null) rightRemoveAllClothingBtn.SetActive(false);
            if (leftRemoveAllClothingBtn != null) leftRemoveAllClothingBtn.SetActive(false);
            if (rightRemoveAllHairBtn != null) rightRemoveAllHairBtn.SetActive(false);
            if (leftRemoveAllHairBtn != null) leftRemoveAllHairBtn.SetActive(false);
            if (rightRemoveAtomBtn != null) rightRemoveAtomBtn.SetActive(false);
            if (leftRemoveAtomBtn != null) leftRemoveAtomBtn.SetActive(false);
            if (rightSaveBtnGO != null) rightSaveBtnGO.SetActive(showSave);
            if (leftSaveBtnGO != null) leftSaveBtnGO.SetActive(showSave);

            // Keep remove-list siderail in sync while Remove Mode (bin) is on.
            if (_removeModeActive)
            {
                try { EnsureRemoveSiderailOpenForCurrentCategory(); } catch { }
            }

            // Update arrow indicators immediately (not only after submenu hover).
            bool anyClothingChanged = false;
            bool isRemoveClothingOpen = (leftActiveContent == ContentType.RemoveClothing || rightActiveContent == ContentType.RemoveClothing);

            if (isClothing || isRemoveClothingOpen)
            {
                int count = 0;
                try
                {
                    var atoms = SuperController.singleton != null ? SuperController.singleton.GetAtoms() : null;
                    if (atoms != null) foreach (Atom tgt in atoms)
                    {
                        if (tgt == null || !SceneUtils.IsPersonLikeAtom(tgt)) continue;
                        HashSet<string> currentUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        JSONStorable geometry = tgt.GetStorableByID("geometry");
                        if (geometry != null)
                        {
                            foreach (var name in geometry.GetBoolParamNames())
                            {
                                if (string.IsNullOrEmpty(name) || !name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                                string clothingUid = name.Substring(9);
                                if (!clothingUid.Contains("/")) continue;

                                bool isPreviewItem = (!string.IsNullOrEmpty(previewRemoveClothingAtomUid)
                                    && !string.IsNullOrEmpty(previewRemoveClothingItemUid)
                                    && string.Equals(previewRemoveClothingAtomUid, tgt.uid, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(previewRemoveClothingItemUid, clothingUid, StringComparison.OrdinalIgnoreCase)
                                    && previewRemoveClothingPrevGeometryVal.HasValue
                                    && previewRemoveClothingPrevGeometryVal.Value);

                                JSONStorableBool jsb = geometry.GetBoolJSONParam(name);
                                bool isActive = jsb != null && (jsb.val || isPreviewItem);
                                if (isActive)
                                {
                                    count++;
                                    currentUids.Add(clothingUid);
                                }
                            }
                        }

                        // Initialize session-initial UIDs for this atom if not already tracked.
                        if (!_sessionInitialClothingUids.ContainsKey(tgt.uid))
                        {
                            _sessionInitialClothingUids[tgt.uid] = new HashSet<string>(currentUids, StringComparer.OrdinalIgnoreCase);
                        }

                        // Detect changes for auto-refresh.
                        if (!_lastActiveClothingUids.TryGetValue(tgt.uid, out var lastUids))
                        {
                            lastUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }

                        if (!currentUids.SetEquals(lastUids))
                        {
                            anyClothingChanged = true;
                            _lastActiveClothingUids[tgt.uid] = currentUids;
                        }
                    }
                }
                catch { }

                if (isClothing) UpdateRemoveClothingButtonLabels(count);

                // Auto-refresh the side tab if the list changed and the tab is open.
                if (anyClothingChanged && isRemoveClothingOpen)
                {
                    UpdateTabs();
                }
            }
            else
            {
                UpdateRemoveClothingButtonLabels(0);
            }

            if (isHair)
            {
                int count = 0;
                try
                {
                    var atoms = SuperController.singleton != null ? SuperController.singleton.GetAtoms() : null;
                    if (atoms != null) foreach (Atom tgt in atoms)
                    {
                        if (tgt == null || !SceneUtils.IsPersonLikeAtom(tgt)) continue;
                        DAZCharacterSelector dcs = tgt.GetComponentInChildren<DAZCharacterSelector>();
                        if (dcs != null && dcs.hairItems != null)
                        {
                            foreach (var it in dcs.hairItems)
                            {
                                if (it != null && it.active) count++;
                            }
                        }
                    }
                }
                catch { }
                UpdateRemoveHairButtonLabels(count);
            }
            else
            {
                UpdateRemoveHairButtonLabels(0);
            }

            if (!isHair && (leftActiveContent == ContentType.RemoveHair || rightActiveContent == ContentType.RemoveHair))
            {
                if (leftActiveContent == ContentType.RemoveHair) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.RemoveHair) rightActiveContent = rightPrevActiveContent;
                UpdateTabs();
            }

            if (!isClothing && (leftActiveContent == ContentType.RemoveClothing || rightActiveContent == ContentType.RemoveClothing))
            {
                if (leftActiveContent == ContentType.RemoveClothing) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.RemoveClothing) rightActiveContent = rightPrevActiveContent;
                UpdateTabs();
            }

            if (!isScene && (leftActiveContent == ContentType.RemoveAtom || rightActiveContent == ContentType.RemoveAtom))
            {
                if (leftActiveContent == ContentType.RemoveAtom) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.RemoveAtom) rightActiveContent = rightPrevActiveContent;
                UpdateTabs();
            }

            if (!showSave && (leftActiveContent == ContentType.SavePresets || rightActiveContent == ContentType.SavePresets))
            {
                if (leftActiveContent == ContentType.SavePresets) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.SavePresets) rightActiveContent = rightPrevActiveContent;
                UpdateTabs();
            }

            UpdateSideButtonPositions();
        }

        private float GetSideButtonsStackHeight(float spacing, float gap)
        {
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            if (layout == null || layout.Length <= 1) return 0f;

            int visibleCount = 0;
            int gapUnits = 0;
            bool firstVisible = true;
            for (int i = 0; i < layout.Length; i++)
            {
                RectTransform rt = (layout[i].buttonIndex >= 0 && layout[i].buttonIndex < rightSideButtons.Count) ? rightSideButtons[layout[i].buttonIndex] : null;
                if (rt == null && leftSideButtons != null)
                {
                    rt = (layout[i].buttonIndex >= 0 && layout[i].buttonIndex < leftSideButtons.Count) ? leftSideButtons[layout[i].buttonIndex] : null;
                }
                if (rt == null || !rt.gameObject.activeSelf) continue;

                visibleCount++;
                if (!firstVisible) gapUnits += layout[i].gapTier;
                firstVisible = false;
            }

            if (visibleCount <= 1) return 0f;
            return (visibleCount - 1) * spacing + gapUnits * gap;
        }

        /// <summary>Square icon chrome on side rails: SideButtonSquareRef when loaded sprites exist.</summary>
        private bool UsesSquareChromeSideButton(RectTransform rt, List<RectTransform> list)
        {
            if (rt == null || list == null) return false;
            int idx = list.IndexOf(rt);
            if (idx < 0) return false;
            bool isRight = ReferenceEquals(list, rightSideButtons);
            if (idx < GalleryLeadingIconButtonCount)
            {
                if (isRight)
                {
                    return (idx == 0 && rightDesktopModeBtnIconImage != null)
                        || (idx == 1 && rightFollowBtnIconImage != null)
                        || (idx == 2 && rightCloneBtnIconImage != null);
                }
                return (idx == 0 && leftDesktopModeBtnIconImage != null)
                    || (idx == 1 && leftFollowBtnIconImage != null)
                    || (idx == 2 && leftCloneBtnIconImage != null);
            }
            GameObject go = rt.gameObject;
            if (rightSaveBtnGO != null && go == rightSaveBtnGO) return rightSaveBtnIconImage != null;
            if (leftSaveBtnGO != null && go == leftSaveBtnGO) return leftSaveBtnIconImage != null;
            if (isRight)
            {
                if (rightSceneImportSideBtn != null && go == rightSceneImportSideBtn) return true;
                if (rightRemoveModeSideBtn != null && go == rightRemoveModeSideBtn) return true;
                if (rightUserTagsSideBtn != null && go == rightUserTagsSideBtn) return true;
                if (galleryCategorySprite != null && rightCategoryBtnIconImage != null && rightCategoryBtnImage != null && go == rightCategoryBtnImage.gameObject)
                    return true;
                if (galleryCreatorSprite != null && rightCreatorBtnIconImage != null && rightCreatorBtnImage != null && go == rightCreatorBtnImage.gameObject)
                    return true;
                if (galleryPathSprite != null && rightPathBtnIconImage != null && rightPathBtnImage != null && go == rightPathBtnImage.gameObject)
                    return true;
                if (rightHistoryBtnImage != null && go == rightHistoryBtnImage.gameObject)
                    return true;
                if ((galleryApplyOneClickSprite != null || galleryApplyTwoClickSprite != null) && rightApplyModeBtnIconImage != null && rightApplyModeBtnImage != null && go == rightApplyModeBtnImage.gameObject)
                    return true;
                if (galleryRemoveSprite != null && rightRemoveAtomBtnIconImage != null && rightRemoveAtomBtn != null && go == rightRemoveAtomBtn)
                    return true;
                if (galleryRemoveSprite != null && rightRemoveAllClothingBtnIconImage != null && rightRemoveAllClothingBtn != null && go == rightRemoveAllClothingBtn)
                    return true;
                if (galleryRemoveSprite != null && rightRemoveAllHairBtnIconImage != null && rightRemoveAllHairBtn != null && go == rightRemoveAllHairBtn)
                    return true;
            }
            else
            {
                if (leftSceneImportSideBtn != null && go == leftSceneImportSideBtn) return true;
                if (leftRemoveModeSideBtn != null && go == leftRemoveModeSideBtn) return true;
                if (leftUserTagsSideBtn != null && go == leftUserTagsSideBtn) return true;
                if (galleryCategorySprite != null && leftCategoryBtnIconImage != null && leftCategoryBtnImage != null && go == leftCategoryBtnImage.gameObject)
                    return true;
                if (galleryCreatorSprite != null && leftCreatorBtnIconImage != null && leftCreatorBtnImage != null && go == leftCreatorBtnImage.gameObject)
                    return true;
                if (galleryPathSprite != null && leftPathBtnIconImage != null && leftPathBtnImage != null && go == leftPathBtnImage.gameObject)
                    return true;
                if (leftHistoryBtnImage != null && go == leftHistoryBtnImage.gameObject)
                    return true;
                if ((galleryApplyOneClickSprite != null || galleryApplyTwoClickSprite != null) && leftApplyModeBtnIconImage != null && leftApplyModeBtnImage != null && go == leftApplyModeBtnImage.gameObject)
                    return true;
                if (galleryRemoveSprite != null && leftRemoveAtomBtnIconImage != null && leftRemoveAtomBtn != null && go == leftRemoveAtomBtn)
                    return true;
                if (galleryRemoveSprite != null && leftRemoveAllClothingBtnIconImage != null && leftRemoveAllClothingBtn != null && go == leftRemoveAllClothingBtn)
                    return true;
                if (galleryRemoveSprite != null && leftRemoveAllHairBtnIconImage != null && leftRemoveAllHairBtn != null && go == leftRemoveAllHairBtn)
                    return true;
            }
            return false;
        }

        /// <summary>Right rail: hug inner edge toward gallery. Left rail: hug inner edge toward gallery.</summary>
        private void ApplySquareSideButtonEdgeAlignment(RectTransform rt, List<RectTransform> list, float y)
        {
            if (rt == null || list == null) return;
            float inset = GalleryUiDesignTokens.SideButtonEdgeInsetRef * ChromeScale;
            bool isRightRail = ReferenceEquals(list, rightSideButtons);
            if (UsesSquareChromeSideButton(rt, list))
            {
                rt.anchorMin = rt.anchorMax = isRightRail ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
                rt.pivot = isRightRail ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(isRightRail ? inset : -inset, y);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, y);
            }
        }

        private void UpdateListPositions(List<RectTransform> buttons, float startY, float spacing, float gap, bool isLeftRail)
        {
            if (buttons == null) return;

            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            float y = startY;
            bool firstVisible = true;
            int sepSlot = 0;
            GameObject[] seps = EnsureSideRailZoneSeparators(isLeftRail);
            bool showSeps = gap > 0.01f && seps != null;

            for (int s = 0; s < SideRailZoneSepSlots; s++)
            {
                if (seps != null && seps[s] != null)
                    seps[s].SetActive(false);
            }

            for (int i = 0; i < layout.Length; i++)
            {
                RectTransform rt = (layout[i].buttonIndex >= 0 && layout[i].buttonIndex < buttons.Count) ? buttons[layout[i].buttonIndex] : null;
                if (rt == null || !rt.gameObject.activeSelf) continue;

                if (!firstVisible)
                {
                    float step = spacing + gap * layout[i].gapTier;
                    if (showSeps && layout[i].gapTier > 0 && sepSlot < SideRailZoneSepSlots && seps[sepSlot] != null)
                    {
                        float sepY = y - step * 0.5f;
                        PlaceSideRailZoneSep(seps[sepSlot], buttons, sepY, isLeftRail);
                        seps[sepSlot].SetActive(true);
                        sepSlot++;
                    }
                    y -= step;
                }
                ApplySquareSideButtonEdgeAlignment(rt, buttons, y);
                firstVisible = false;
            }
        }

        private GameObject[] EnsureSideRailZoneSeparators(bool isLeftRail)
        {
            GameObject container = isLeftRail ? leftSideContainer : rightSideContainer;
            if (container == null) return null;
            GameObject[] seps = isLeftRail ? _leftSideZoneSeps : _rightSideZoneSeps;
            if (seps != null && seps.Length == SideRailZoneSepSlots && seps[0] != null)
                return seps;

            seps = new GameObject[SideRailZoneSepSlots];
            for (int i = 0; i < SideRailZoneSepSlots; i++)
            {
                GameObject go = UI.AddChildGOImage(
                    container,
                    new Color(1f, 1f, 1f, 0.14f),
                    AnchorPresets.middleCenter,
                    GalleryUiDesignTokens.SideButtonZoneSepWidthRef,
                    GalleryUiDesignTokens.SideButtonZoneSepHeightRef,
                    Vector2.zero);
                go.name = isLeftRail ? ("LeftSideZoneSep" + i) : ("RightSideZoneSep" + i);
                go.SetActive(false);
                try
                {
                    var img = go.GetComponent<Image>();
                    if (img != null) img.raycastTarget = false;
                }
                catch { }
                seps[i] = go;
            }
            if (isLeftRail) _leftSideZoneSeps = seps;
            else _rightSideZoneSeps = seps;
            return seps;
        }

        private void PlaceSideRailZoneSep(GameObject sep, List<RectTransform> list, float y, bool isLeftRail)
        {
            if (sep == null || list == null) return;
            RectTransform rt = sep.GetComponent<RectTransform>();
            if (rt == null) return;
            float scale = ChromeScale;
            float w = GalleryUiDesignTokens.SideButtonZoneSepWidthRef * scale;
            float h = Mathf.Max(1f, GalleryUiDesignTokens.SideButtonZoneSepHeightRef * scale);
            rt.sizeDelta = new Vector2(w, h);
            float inset = GalleryUiDesignTokens.SideButtonEdgeInsetRef * scale;
            if (isLeftRail)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-inset - (GalleryUiDesignTokens.SideButtonSquareRef * scale - w) * 0.5f, y);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(inset + (GalleryUiDesignTokens.SideButtonSquareRef * scale - w) * 0.5f, y);
            }
        }

        /// <summary>Rim open browse-facet buttons so active pane is not color-only (SidePanelHeader still names it).</summary>
        private void SyncSideRailOpenFacetChrome()
        {
            SyncSideRailOpenFacetChromeForSide(isLeft: true);
            SyncSideRailOpenFacetChromeForSide(isLeft: false);
        }

        private void SyncSideRailOpenFacetChromeForSide(bool isLeft)
        {
            ContentType? active = isLeft ? leftActiveContent : rightActiveContent;
            bool importOpen = importSidebarActive && importSidebarOnLeft == isLeft;

            GameObject cat = isLeft
                ? (leftCategoryBtnImage != null ? leftCategoryBtnImage.gameObject : null)
                : (rightCategoryBtnImage != null ? rightCategoryBtnImage.gameObject : null);
            GameObject cre = isLeft
                ? (leftCreatorBtnImage != null ? leftCreatorBtnImage.gameObject : null)
                : (rightCreatorBtnImage != null ? rightCreatorBtnImage.gameObject : null);
            GameObject path = isLeft
                ? (leftPathBtnImage != null ? leftPathBtnImage.gameObject : null)
                : (rightPathBtnImage != null ? rightPathBtnImage.gameObject : null);
            GameObject hist = isLeft
                ? (leftHistoryBtnImage != null ? leftHistoryBtnImage.gameObject : null)
                : (rightHistoryBtnImage != null ? rightHistoryBtnImage.gameObject : null);
            GameObject tags = isLeft ? leftUserTagsSideBtn : rightUserTagsSideBtn;
            GameObject imp = isLeft ? leftSceneImportSideBtn : rightSceneImportSideBtn;

            SetSideRailFacetSelected(cat, active == ContentType.Category, ColorCategory);
            SetSideRailFacetSelected(cre, active == ContentType.Creator, ColorCreator);
            SetSideRailFacetSelected(path, active == ContentType.Path, ColorPath);
            SetSideRailFacetSelected(hist, active == ContentType.History, ColorHistoryAccent);
            SetSideRailFacetSelected(tags,
                active == ContentType.UserTags || active == ContentType.UserTagsApplied,
                new Color(0.14f, 0.42f, 0.48f, 1f));
            SetSideRailFacetSelected(imp, importOpen, ColorSceneImport);
        }

        private static void SetSideRailFacetSelected(GameObject btn, bool selected, Color facetColor)
        {
            if (btn == null) return;
            UIHoverBorder hb = btn.GetComponent<UIHoverBorder>();
            if (hb == null) return;

            // If a prior build installed an outside hover-dot, drop it so full rim highlight works again.
            bool clearedDot = false;
            if (hb.hoverBorderGO != null)
            {
                try { UnityEngine.Object.Destroy(hb.hoverBorderGO); } catch { }
                hb.hoverBorderGO = null;
                clearedDot = true;
            }

            // Selected: bright facet rim. Idle: default yellow hover (do not leave selected tint).
            Color accent = selected
                ? Color.Lerp(facetColor, Color.white, 0.55f)
                : new Color(1f, 1f, 0f, 1f);

            bool selChanged = hb.isSelected != selected;
            bool colorChanged = hb.hoverColor != accent;
            if (!selChanged && !colorChanged && !clearedDot) return;

            hb.isSelected = selected;
            hb.hoverColor = accent;
            // Full ApplyBorderSettings only when tint/layout inputs change; selection-only = show/hide rim.
            if (colorChanged || clearedDot)
            {
                try { hb.ApplyBorderSettings(); } catch { }
            }
            else
            {
                hb.SyncIndicatorVisibility();
            }
        }


    }

}
