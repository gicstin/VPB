using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private float SideTabBottomMargin => GalleryMainAreaBottomInset() + 8f;
        private float SideTabDefaultBottomOffset => GalleryMainAreaBottomInset() + 8f;

        // Top inset for side tab scroll: clears sort + search row (SideTabTopOffsetRef*s + row + gap).
        // Filter chip bar lives in the main grid column only — do not add ActiveFilterChromeTopInsetPx here.
        private float TabScrollTopOffset()
        {
            float s = ChromeScale;
            float rowTop = GalleryUiDesignTokens.SideTabTopOffsetRef * s;
            float headerExtra = 0f;
            try { headerExtra = SidePanelHeaderExtraTopInset(); } catch { }
            float rowH = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            float gap = GalleryUiDesignTokens.SideTabFilterRowBottomGapRef * s;
            return -(rowTop + headerExtra + rowH + gap);
        }

        private static float SideTabSplitSubPaneTopAnchorY(ContentType activeContent)
        {
            return activeContent == ContentType.Category
                ? GalleryUiDesignTokens.CategorySideSubPaneHeightFraction
                : 0.5f;
        }

        private static bool CategoryNeedsSplitView(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            return title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Split sub-panes that need tag-count cache priming (excludes Scene — uses SceneSource only).</summary>
        private static bool CategoryNeedsTagCountCachePass(string title)
        {
            if (!CategoryNeedsSplitView(title)) return false;
            return title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static ContentType InferCategorySubPaneTypeFromTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return ContentType.Tags;
            if (title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0) return ContentType.SceneSource;
            if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0) return ContentType.AppearanceSource;
            return ContentType.Tags;
        }

        /// <summary>Header/footer/side chrome without recreating category/creator/tag tab buttons when <see cref="VPBConfig.Save(bool,bool)"/> used <c>preferLightGalleryTabChromeOnly: true</c>.</summary>
        private void UpdateTabsLightChromeOnlyStandardGallery()
        {
            if (titleText != null)
            {
                bool showTitle = !IsFilterActive;
                if (titleText.gameObject.activeSelf != showTitle) titleText.gameObject.SetActive(showTitle);
                if (showTitle)
                    titleText.text = currentCategoryTitle;
            }
            SyncCategoryQuickSwitchChrome();
            try { ApplyTitleBarResponsiveLayout(ChromeScale); } catch { }
            try { ApplyFooterOverflowLayout(ChromeScale); } catch { }
            try { ApplyInAppHelpPanelLayout(ChromeScale); } catch { }
            UpdateSideContextActions();

            // Lightweight refresh must still keep split sub-pane lists alive (Hair/Clothing tags, SceneSource, etc.).
            // Otherwise sub-pane can go empty if a lightweight slot is consumed during category navigation.
            try
            {
                if (leftSubTabScrollGO != null && leftSubTabScrollGO.activeSelf && leftActiveContent == ContentType.Category)
                {
                    string t = titleText != null ? (titleText.text ?? "") : "";
                    ContentType subType = InferCategorySubPaneTypeFromTitle(t);
                    UpdateTabs(subType, leftSubTabContainerGO, leftSubActiveTabButtons, true);
                }
                if (rightSubTabScrollGO != null && rightSubTabScrollGO.activeSelf && rightActiveContent == ContentType.Category)
                {
                    string t2 = titleText != null ? (titleText.text ?? "") : "";
                    ContentType subType2 = InferCategorySubPaneTypeFromTitle(t2);
                    UpdateTabs(subType2, rightSubTabContainerGO, rightSubActiveTabButtons, false);
                }
            }
            catch { }
            UpdateSideButtonsVisibility();
            float paneScale = ChromeScale;
            try { SyncSidePanelHeaderChrome(paneScale); } catch { }
            try { SuppressImportOccupiedSideColumnChrome(); } catch { }
            try { SyncImportSidebarHeaderLabel(); } catch { }
            try { ApplyUserTagsStickyScrollChrome(TabScrollTopOffset()); } catch { }
            MarkGalleryPaneChromeDirty();
        }

        /// <summary>
        /// Rebuilds every side-tab button list (categories / creators / tags / hub). Can take seconds with large libraries.
        /// </summary>
        /// <remarks>
        /// INVARIANT: Do not subscribe this method to <see cref="VPBConfig.ConfigChanged"/>. Use
        /// <see cref="RefreshSideTabAreasForConfigChange"/> for that channel. A runtime guard downgrades mistaken calls during dispatch.
        /// </remarks>
        internal void UpdateTabs()
        {
            UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: true);
        }

        /// <summary>
        /// <see cref="VPBConfig.ConfigChanged"/> handler: title/footer/side chrome, sort labels, and tab scroll rect layout
        /// without destroying and recreating hundreds of side-tab buttons (avoids multi-second stalls on resize/scale).
        /// </summary>
        private void RefreshSideTabAreasForConfigChange()
        {
            UpdateTabsImpl(rebuildSideTabLists: false, rebuildSubPaneSideTabLists: true);
        }

        /// <param name="rebuildSubPaneSideTabLists">When false, skips tag/hub-sub/scene-source side lists (split bottom). Main category/creator strips still rebuild when <paramref name="rebuildSideTabLists"/> is true.</param>
        private void UpdateTabsImpl(bool rebuildSideTabLists, bool rebuildSubPaneSideTabLists = true)
        {
            // UserTags split uses sticky viewports; lightweight path skips ApplyUserTagsStickyScrollChrome at the end
            // of this method — layout rebuilds would leave toolbars hidden until a full UpdateLayout.
            bool userTagsSideOpen = leftActiveContent == ContentType.UserTags
                || rightActiveContent == ContentType.UserTags;
            if ((leftTabContainerGO != null || rightTabContainerGO != null)
                && VPBConfig.Instance != null && VPBConfig.Instance.TryConsumeLightweightGalleryTabRefreshSlot()
                && !userTagsSideOpen)
            {
                UpdateTabsLightChromeOnlyStandardGallery();
                return;
            }

            if (rebuildSideTabLists && VPBConfig.ConfigChangedInvocationDepth > 0)
            {
                LogUtil.LogError("[VPB] GalleryPanel: full UpdateTabs (side-tab list rebuild) was invoked during ConfigChanged. Downgrading to chrome/layout only. Fix: remove UpdateTabs from ConfigChanged; keep RefreshSideTabAreasForConfigChange.");
                rebuildSideTabLists = false;
            }

            if (titleText != null)
            {
                // When filtering by deps/dependents, the active category title is not meaningful.
                // Hide it to reduce visual noise; the footer shows the filter mode instead.
                bool showTitle = !IsFilterActive;
                if (titleText.gameObject.activeSelf != showTitle) titleText.gameObject.SetActive(showTitle);

                if (showTitle)
                {
                    if (IsSettingsPanelOpen())
                        titleText.text = VPBTranslation.T("settings.title", "Settings");
                    else titleText.text = currentCategoryTitle;
                }
            }

            SyncCategoryQuickSwitchChrome();

            UpdateSideContextActions();

            if (!leftActiveContent.HasValue || leftActiveContent != ContentType.UserTags)
                PurgeUserTagsSideTabArtifactsFromMainPane(true);
            if (!rightActiveContent.HasValue || rightActiveContent != ContentType.UserTags)
                PurgeUserTagsSideTabArtifactsFromMainPane(false);

            if (leftActiveContent.HasValue)
            {
                // Split View Logic
                bool splitView = false;
                if (leftActiveContent == ContentType.Category)
                {
                    string title = titleText != null ? titleText.text : "";
                    splitView = CategoryNeedsSplitView(title);
                }
                else if (leftActiveContent == ContentType.CleanupCategories)
                {
                    // Split view when filtering stale cache: show age buckets in the sub-pane.
                    splitView = GetCleanupFilterMode() == 4;
                }

                if ((splitView && (leftActiveContent == ContentType.Category || leftActiveContent == ContentType.CleanupCategories))
                    && leftSubTabScrollGO != null)
                {
                    // Split Layout
                    leftSubTabScrollGO.SetActive(true);

                    ContentType subType = ContentType.Tags;
                    if (leftActiveContent == ContentType.CleanupCategories) subType = ContentType.CleanupStaleBuckets;
                    else if (leftActiveContent == ContentType.Category)
                        subType = InferCategorySubPaneTypeFromTitle(titleText != null ? titleText.text : "");

                    bool sceneSourceLeft = leftActiveContent == ContentType.Category && subType == ContentType.SceneSource;
                    leftSubSceneSortBarActive = sceneSourceLeft;
                    if (leftSubSortBtn != null)
                        leftSubSortBtn.SetActive(!sceneSourceLeft);
                    if (leftSubSceneSortBtn != null) leftSubSceneSortBtn.SetActive(sceneSourceLeft);
                    if (leftActiveContent == ContentType.CleanupCategories)
                    {
                        if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
                        if (leftSubClearBtn != null) leftSubClearBtn.SetActive(false);
                    }
                    else
                    {
                        if (leftSubSearchInput != null)
                        {
                            leftSubSearchInput.gameObject.SetActive(true);
                            if (leftSubSearchInput.text != tagFilter) leftSubSearchInput.text = tagFilter;
                            if (leftSubSearchInput.placeholder is Text phT)
                                phT.text = GetContentTypePlaceholder(ContentType.Tags);
                        }
                    }
                    if (sceneSourceLeft) SyncSceneSourceSortButtonHighlights();
                    SyncSideTabSubFilterRowChrome(ChromeScale);

                    float splitY = SideTabSplitSubPaneTopAnchorY(leftActiveContent.Value);
                    RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                    leftRT.anchorMin = new Vector2(0, splitY);
                    leftRT.anchorMax = new Vector2(0, 1);
                    leftRT.offsetMin = new Vector2(10, SideTabSplitSeamInset());
                    leftRT.offsetMax = new Vector2(leftRT.offsetMax.x, TabScrollTopOffset());

                    RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(0, 0);
                    subRT.anchorMax = new Vector2(0, splitY);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, SubTabScrollPaneTopOffset());
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin);

                    // Populate Top (Category / Hub Category / Status)
                    if (rebuildSideTabLists)
                    {
                        if (!TryUpdateCategoryCreatorDualBufferMainPane(leftActiveContent.Value, leftTabContainerGO, true))
                        {
                            TeardownCategoryCreatorDualBufferOneSide(true);
                            UpdateTabs(leftActiveContent.Value, leftTabContainerGO, leftActiveTabButtons, true);
                        }
                    }

                    // Populate Bottom (Tags / Hub Tags / Ratings / Size / SceneSource)
                    if (rebuildSideTabLists && !rebuildSubPaneSideTabLists)
                    {
                        foreach (var b in leftSubActiveTabButtons) ReturnTabButton(b);
                        leftSubActiveTabButtons.Clear();
                    }
                    if (rebuildSideTabLists && rebuildSubPaneSideTabLists)
                        UpdateTabs(subType, leftSubTabContainerGO, leftSubActiveTabButtons, true);
                }
                else
                {
                    // Full Layout
                    if (leftSubTabScrollGO != null) leftSubTabScrollGO.SetActive(false);
                    leftSubSceneSortBarActive = false;
                    if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                    if (leftSubSceneSortBtn != null) leftSubSceneSortBtn.SetActive(false);
                    if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
                    if (leftSubClearBtn != null) leftSubClearBtn.SetActive(false);

                    RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                    leftRT.anchorMin = new Vector2(0, 0);
                    leftRT.anchorMax = new Vector2(0, 1);
                    leftRT.offsetMin = new Vector2(10, SideTabDefaultBottomOffset); // Restore default
                    leftRT.offsetMax = new Vector2(leftRT.offsetMax.x, TabScrollTopOffset());

                    if (rebuildSideTabLists)
                    {
                        if (!TryUpdateCategoryCreatorDualBufferMainPane(leftActiveContent.Value, leftTabContainerGO, true))
                        {
                            TeardownCategoryCreatorDualBufferOneSide(true);
                            UpdateTabs(leftActiveContent.Value, leftTabContainerGO, leftActiveTabButtons, true);
                        }
                    }
                }
            }
            else
            {
                leftSubSceneSortBarActive = false;
                if (leftSubSceneSortBtn != null) leftSubSceneSortBtn.SetActive(false);
                if (leftCategoryTabHolder != null) leftCategoryTabHolder.SetActive(false);
                if (leftCreatorTabHolder != null) leftCreatorTabHolder.SetActive(false);
            }

            if (rightActiveContent.HasValue)
            {
                // Right Split View Logic
                bool splitView = false;
                if (rightActiveContent == ContentType.Category)
                {
                    string title = titleText != null ? titleText.text : "";
                    splitView = CategoryNeedsSplitView(title);
                }
                else if (rightActiveContent == ContentType.CleanupCategories)
                {
                    // Split view when filtering stale cache: show age buckets in the sub-pane.
                    splitView = GetCleanupFilterMode() == 4;
                }

                if ((splitView && (rightActiveContent == ContentType.Category || rightActiveContent == ContentType.CleanupCategories))
                    && rightSubTabScrollGO != null)
                {
                    // Split Layout
                    rightSubTabScrollGO.SetActive(true);

                    ContentType subType = ContentType.Tags;
                    if (rightActiveContent == ContentType.CleanupCategories) subType = ContentType.CleanupStaleBuckets;
                    else if (rightActiveContent == ContentType.Category)
                        subType = InferCategorySubPaneTypeFromTitle(titleText != null ? titleText.text : "");

                    bool sceneSourceRight = rightActiveContent == ContentType.Category && subType == ContentType.SceneSource;
                    rightSubSceneSortBarActive = sceneSourceRight;
                    if (rightSubSortBtn != null)
                        rightSubSortBtn.SetActive(!sceneSourceRight);
                    if (rightSubSceneSortBtn != null) rightSubSceneSortBtn.SetActive(sceneSourceRight);
                    if (rightActiveContent == ContentType.CleanupCategories)
                    {
                        if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
                        if (rightSubClearBtn != null) rightSubClearBtn.SetActive(false);
                    }
                    else
                    {
                        if (rightSubSearchInput != null)
                        {
                            rightSubSearchInput.gameObject.SetActive(true);
                            if (rightSubSearchInput.text != tagFilter) rightSubSearchInput.text = tagFilter;
                            if (rightSubSearchInput.placeholder is Text phT)
                                phT.text = GetContentTypePlaceholder(ContentType.Tags);
                        }
                    }
                    if (sceneSourceRight) SyncSceneSourceSortButtonHighlights();
                    SyncSideTabSubFilterRowChrome(ChromeScale);

                    float splitY = SideTabSplitSubPaneTopAnchorY(rightActiveContent.Value);
                    RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                    rightRT.anchorMin = new Vector2(1, splitY);
                    rightRT.anchorMax = new Vector2(1, 1);
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, SideTabSplitSeamInset());
                    rightRT.offsetMax = new Vector2(rightRT.offsetMax.x, TabScrollTopOffset());

                    RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(1, 0);
                    subRT.anchorMax = new Vector2(1, splitY);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, SubTabScrollPaneTopOffset());
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin);

                    // Populate Top (Category / Hub Category / Status)
                    if (rebuildSideTabLists)
                    {
                        if (!TryUpdateCategoryCreatorDualBufferMainPane(rightActiveContent.Value, rightTabContainerGO, false))
                        {
                            TeardownCategoryCreatorDualBufferOneSide(false);
                            UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
                        }
                    }

                    // Populate Bottom (Tags / Hub Tags / Ratings / Size / SceneSource)
                    if (rebuildSideTabLists && !rebuildSubPaneSideTabLists)
                    {
                        foreach (var b in rightSubActiveTabButtons) ReturnTabButton(b);
                        rightSubActiveTabButtons.Clear();
                    }
                    if (rebuildSideTabLists && rebuildSubPaneSideTabLists)
                        UpdateTabs(subType, rightSubTabContainerGO, rightSubActiveTabButtons, false);
                }
                else
                {
                    // Full Layout
                    if (rightSubTabScrollGO != null) rightSubTabScrollGO.SetActive(false);
                    rightSubSceneSortBarActive = false;
                    if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                    if (rightSubSceneSortBtn != null) rightSubSceneSortBtn.SetActive(false);
                    if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
                    if (rightSubClearBtn != null) rightSubClearBtn.SetActive(false);

                    RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                    rightRT.anchorMin = new Vector2(1, 0);
                    rightRT.anchorMax = new Vector2(1, 1);
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, SideTabDefaultBottomOffset); // Restore default
                    rightRT.offsetMax = new Vector2(rightRT.offsetMax.x, TabScrollTopOffset());

                    if (rebuildSideTabLists)
                    {
                        if (!TryUpdateCategoryCreatorDualBufferMainPane(rightActiveContent.Value, rightTabContainerGO, false))
                        {
                            TeardownCategoryCreatorDualBufferOneSide(false);
                            UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
                        }
                    }
                }
            }
            else
            {
                rightSubSceneSortBarActive = false;
                if (rightSubSceneSortBtn != null) rightSubSceneSortBtn.SetActive(false);
                if (rightCategoryTabHolder != null) rightCategoryTabHolder.SetActive(false);
                if (rightCreatorTabHolder != null) rightCreatorTabHolder.SetActive(false);
            }

            SyncSidePaneTopSortButtonVisuals();
            UpdateSideButtonsVisibility();

            float paneScale = ChromeScale;
            try { SyncSidePanelHeaderChrome(paneScale); } catch { }

            // UpdateLayout runs before UpdateTabs in ToggleLeft/Right; UpdateTabsImpl mutates tab ScrollRect geometry
            // after that — viewport stretch resets unless sticky chrome is reapplied here.
            try { ApplyUserTagsStickyScrollChrome(TabScrollTopOffset()); } catch { }
            MarkGalleryPaneChromeDirty();
        }

        private bool TryUpdateCategoryCreatorDualBufferMainPane(ContentType activeContent, GameObject tabContainer, bool isLeft)
        {
            if (tabContainer == null) return false;
            if (activeContent != ContentType.Category && activeContent != ContentType.Creator) return false;

            EnsureCategoryCreatorHolders(tabContainer, isLeft);

            GameObject catH = isLeft ? leftCategoryTabHolder : rightCategoryTabHolder;
            GameObject crH = isLeft ? leftCreatorTabHolder : rightCreatorTabHolder;
            List<GameObject> catList = isLeft ? leftCategoryTabButtons : rightCategoryTabButtons;
            List<GameObject> crList = isLeft ? leftCreatorTabButtons : rightCreatorTabButtons;

            string catSig = ComputeCategorySideTabSignature();
            string crSig = ComputeCreatorSideTabSignature();

            string lastCat = isLeft ? leftCategoryTabsLastSig : rightCategoryTabsLastSig;
            string lastCr = isLeft ? leftCreatorTabsLastSig : rightCreatorTabsLastSig;

            // Avoid building the hidden pane on first open; build it once the user switches or after it existed and data changed.
            bool categoryPaneEverBuilt = lastCat != null;
            if (!categoriesCached || lastCat != catSig)
            {
                if (activeContent == ContentType.Category || categoryPaneEverBuilt)
                {
                    UpdateTabs(ContentType.Category, catH, catList, isLeft);
                    lastCat = catSig;
                    if (isLeft) leftCategoryTabsLastSig = lastCat;
                    else rightCategoryTabsLastSig = lastCat;
                }
            }
            bool creatorPaneEverBuilt = lastCr != null;
            if (!creatorsCached || lastCr != crSig)
            {
                if (activeContent == ContentType.Creator || creatorPaneEverBuilt)
                {
                    UpdateTabs(ContentType.Creator, crH, crList, isLeft);
                    lastCr = crSig;
                    if (isLeft) leftCreatorTabsLastSig = lastCr;
                    else rightCreatorTabsLastSig = lastCr;
                }
            }

            if (catH != null) catH.SetActive(activeContent == ContentType.Category);
            if (crH != null) crH.SetActive(activeContent == ContentType.Creator);

            return true;
        }

    }
}

