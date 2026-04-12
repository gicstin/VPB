using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel
    {

        /// <summary>List row label: package uid (Creator.Package.Version) unless legacy file-name mode is on.</summary>
        private static string GetGalleryListRowDisplayName(FileEntry file)
        {
            if (file == null) return "[UNNAMED]";
            bool legacy = VPBConfig.Instance != null && VPBConfig.Instance.GalleryListNamesLegacyFileName;
            if (legacy)
                return string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name;
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                    return vfe.Package.Uid;
                if (file is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                    return ple.Package.Uid;
                if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                    return mple.RequestedUid;
            }
            catch { }
            return string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name;
        }

        private void SetGalleryListRowNameTooltip(GameObject nameGO, FileEntry file)
        {
            if (nameGO == null || file == null) return;
            try
            {
                bool legacy = VPBConfig.Instance != null && VPBConfig.Instance.GalleryListNamesLegacyFileName;
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    if (legacy)
                        AddTooltipPlain(nameGO, $"Package: {vfe.Package.Uid}.var");
                    else
                    {
                        string hint = string.IsNullOrEmpty(vfe.InternalPath) ? vfe.Name : vfe.InternalPath.Replace('\\', '/');
                        AddTooltipPlain(nameGO, hint);
                    }
                }
                else if (file is PackageListEntry ple && ple.Package != null)
                {
                    if (legacy)
                        AddTooltipPlain(nameGO, $"Package: {ple.Package.Uid}.var");
                    else if (!string.IsNullOrEmpty(ple.Path))
                        AddTooltipPlain(nameGO, ple.Path);
                }
            }
            catch { }
        }

        private static string FormatBytesForList(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            double d = bytes;
            int i = 0;
            while (d >= 1024.0 && i < suffix.Length - 1)
            {
                d /= 1024.0;
                i++;
            }
            if (i == 0) return bytes.ToString() + " " + suffix[i];
            return d.ToString("0.0") + " " + suffix[i];
        }

        private float SideTabBottomMargin => GalleryMainAreaBottomInset() + 8f;
        private float SideTabDefaultBottomOffset => GalleryMainAreaBottomInset() + 8f;
        // Top inset for main tab scroll: clears the sort button + search row (anchored at y=-55, height=35*scale)
        private float TabScrollTopOffset()
        {
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f;
            return -(55f + 35f * s + 5f);
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
            UpdateFooterContextActions();
            UpdateSideContextActions();
            UpdateSideButtonsVisibility();
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
            UpdateTabsImpl(rebuildSideTabLists: true);
        }

        /// <summary>
        /// <see cref="VPBConfig.ConfigChanged"/> handler: title/footer/side chrome, sort labels, and tab scroll rect layout
        /// without destroying and recreating hundreds of side-tab buttons (avoids multi-second stalls on resize/scale).
        /// </summary>
        private void RefreshSideTabAreasForConfigChange()
        {
            UpdateTabsImpl(rebuildSideTabLists: false);
        }

        private void UpdateTabsImpl(bool rebuildSideTabLists)
        {
            if (!IsHubMode && (leftTabContainerGO != null || rightTabContainerGO != null)
                && VPBConfig.Instance != null && VPBConfig.Instance.TryConsumeLightweightGalleryTabRefreshSlot())
            {
                if (VPBConfig.IsLogConfigPerfEnabled())
                {
                    try
                    {
                        LogUtil.Log("[VPBConfig.Perf] UpdateTabs: lightweight path (side tab buttons not rebuilt)");
                    }
                    catch { }
                }
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
                    if (IsHubMode) titleText.text = VPBTranslation.T("gallery.hub.title_prefix", "HUB: ") + currentHubCategory;
                    else titleText.text = currentCategoryTitle;
                }
            }

            UpdateFooterContextActions();
            UpdateSideContextActions();

            if (IsHubMode)
            {
                TeardownCategoryCreatorDualBufferForHub();
                UpdateHubLayout(rebuildSideTabLists);
                UpdateSideButtonsVisibility();
                return;
            }

            if (leftActiveContent.HasValue) 
            {
                // Split View Logic
                bool splitView = false;
                if (leftActiveContent == ContentType.Category)
                {
                    string title = titleText != null ? titleText.text : "";
                    if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        splitView = true;
                    }
                }
                else if (leftActiveContent == ContentType.Hub)
                {
                    splitView = true;
                }

                if (splitView && (leftActiveContent == ContentType.Category || leftActiveContent == ContentType.Hub) && leftSubTabScrollGO != null)
                {
                    // Split Layout
                    leftSubTabScrollGO.SetActive(true);

                    ContentType subType = ContentType.Tags;
                    if (leftActiveContent == ContentType.Hub) subType = ContentType.HubTags;
                    else if (leftActiveContent == ContentType.Category)
                    {
                        string titleSub = titleText != null ? titleText.text : "";
                        if (titleSub.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                            subType = ContentType.SceneSource;
                        else if (titleSub.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                            subType = ContentType.AppearanceSource;
                    }

                    bool sceneSourceLeft = leftActiveContent == ContentType.Category && subType == ContentType.SceneSource;
                    leftSubSceneSortBarActive = sceneSourceLeft;
                    if (leftSubSortBtn != null)
                        leftSubSortBtn.SetActive(!sceneSourceLeft);
                    if (leftSubSceneSortBtn != null) leftSubSceneSortBtn.SetActive(sceneSourceLeft);
                    if (leftSubSearchInput != null)
                    {
                        leftSubSearchInput.gameObject.SetActive(true);
                        if (leftSubSearchInput.text != tagFilter) leftSubSearchInput.text = tagFilter;
                    }
                    ApplyLeftSubSearchLayoutScaled(VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f);
                    if (sceneSourceLeft) SyncSceneSourceSortButtonHighlights();

                    RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                    leftRT.anchorMin = new Vector2(0, 0.5f);
                    leftRT.anchorMax = new Vector2(0, 1);
                    leftRT.offsetMin = new Vector2(10, SideTabSplitSeamInset());
                    leftRT.offsetMax = new Vector2(leftRT.offsetMax.x, TabScrollTopOffset());

                    RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(0, 0);
                    subRT.anchorMax = new Vector2(0, 0.5f);
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
                    if (rebuildSideTabLists)
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
            }
            if (rightActiveContent.HasValue) 
            {
                // Right Split View Logic
                bool splitView = false;
                if (rightActiveContent == ContentType.Category)
                {
                    string title = titleText != null ? titleText.text : "";
                    if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        splitView = true;
                    }
                }
                else if (rightActiveContent == ContentType.Hub)
                {
                    splitView = true;
                }

                if (splitView && (rightActiveContent == ContentType.Category || rightActiveContent == ContentType.Hub) && rightSubTabScrollGO != null)
                {
                    // Split Layout
                    rightSubTabScrollGO.SetActive(true);

                    ContentType subType = ContentType.Tags;
                    if (rightActiveContent == ContentType.Hub) subType = ContentType.HubTags;
                    else if (rightActiveContent == ContentType.Category)
                    {
                        string titleSub = titleText != null ? titleText.text : "";
                        if (titleSub.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                            subType = ContentType.SceneSource;
                        else if (titleSub.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                            subType = ContentType.AppearanceSource;
                    }

                    bool sceneSourceRight = rightActiveContent == ContentType.Category && subType == ContentType.SceneSource;
                    rightSubSceneSortBarActive = sceneSourceRight;
                    if (rightSubSortBtn != null)
                    {
                        rightSubSortBtn.SetActive(!sceneSourceRight);
                        RectTransform srt = rightSubSortBtn.GetComponent<RectTransform>();
                        srt.anchorMin = new Vector2(1, 0.5f);
                        srt.anchorMax = new Vector2(1, 0.5f);
                    }
                    if (rightSubSceneSortBtn != null) rightSubSceneSortBtn.SetActive(sceneSourceRight);
                    if (rightSubSearchInput != null)
                    {
                        rightSubSearchInput.gameObject.SetActive(true);
                        if (rightSubSearchInput.text != tagFilter) rightSubSearchInput.text = tagFilter;
                        RectTransform rt = rightSubSearchInput.GetComponent<RectTransform>();
                        rt.anchorMin = new Vector2(1, 0.5f);
                        rt.anchorMax = new Vector2(1, 0.5f);
                    }
                    ApplyRightSubSearchLayoutScaled(VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f);
                    if (sceneSourceRight) SyncSceneSourceSortButtonHighlights();

                    RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                    rightRT.anchorMin = new Vector2(1, 0.5f);
                    rightRT.anchorMax = new Vector2(1, 1);
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, SideTabSplitSeamInset());
                    rightRT.offsetMax = new Vector2(rightRT.offsetMax.x, TabScrollTopOffset());

                    RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(1, 0);
                    subRT.anchorMax = new Vector2(1, 0.5f);
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
                    if (rebuildSideTabLists)
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
            }

            SyncSidePaneTopSortButtonVisuals();
            UpdateSideButtonsVisibility();
        }

        private void TeardownCategoryCreatorDualBufferForHub()
        {
            TeardownCategoryCreatorDualBufferOneSide(true);
            TeardownCategoryCreatorDualBufferOneSide(false);
        }

        private void TeardownCategoryCreatorDualBufferOneSide(bool isLeft)
        {
            List<GameObject> catList = isLeft ? leftCategoryTabButtons : rightCategoryTabButtons;
            List<GameObject> crList = isLeft ? leftCreatorTabButtons : rightCreatorTabButtons;
            GameObject catH = isLeft ? leftCategoryTabHolder : rightCategoryTabHolder;
            GameObject crH = isLeft ? leftCreatorTabHolder : rightCreatorTabHolder;

            if (catList != null)
            {
                foreach (var b in catList) ReturnTabButton(b);
                catList.Clear();
            }
            if (crList != null)
            {
                foreach (var b in crList) ReturnTabButton(b);
                crList.Clear();
            }
            if (catH != null)
            {
                try { UnityEngine.Object.Destroy(catH); } catch { }
            }
            if (crH != null)
            {
                try { UnityEngine.Object.Destroy(crH); } catch { }
            }
            if (isLeft)
            {
                leftCategoryTabHolder = null;
                leftCreatorTabHolder = null;
                leftCategoryTabsLastSig = null;
                leftCreatorTabsLastSig = null;
            }
            else
            {
                rightCategoryTabHolder = null;
                rightCreatorTabHolder = null;
                rightCategoryTabsLastSig = null;
                rightCreatorTabsLastSig = null;
            }
        }

        private void ClearTabContainerChildrenForDualBufferInit(GameObject tabContainer)
        {
            if (tabContainer == null) return;
            Transform t = tabContainer.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject go = t.GetChild(i).gameObject;
                if (go.GetComponent<Button>() != null)
                    ReturnTabButton(go);
                else
                    UnityEngine.Object.Destroy(go);
            }
        }

        private GameObject CreateCategoryCreatorTabStackHolder(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.padding = new RectOffset(5, 5, 0, 0);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            return go;
        }

        private void EnsureCategoryCreatorHolders(GameObject tabContainer, bool isLeft)
        {
            GameObject catH = isLeft ? leftCategoryTabHolder : rightCategoryTabHolder;
            GameObject crH = isLeft ? leftCreatorTabHolder : rightCreatorTabHolder;
            if (catH != null && crH != null) return;

            List<GameObject> legacy = isLeft ? leftActiveTabButtons : rightActiveTabButtons;
            if (legacy != null)
            {
                foreach (var b in legacy) ReturnTabButton(b);
                legacy.Clear();
            }
            ClearTabContainerChildrenForDualBufferInit(tabContainer);

            catH = CreateCategoryCreatorTabStackHolder("_VPB_CategoryTabs", tabContainer.transform);
            crH = CreateCategoryCreatorTabStackHolder("_VPB_CreatorTabs", tabContainer.transform);
            if (isLeft)
            {
                leftCategoryTabHolder = catH;
                leftCreatorTabHolder = crH;
            }
            else
            {
                rightCategoryTabHolder = catH;
                rightCreatorTabHolder = crH;
            }
        }

        private string CurrentPathsSignatureFragment()
        {
            if (currentPaths == null || currentPaths.Count == 0)
                return currentPath ?? "";
            var arr = new List<string>(currentPaths);
            arr.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("\x1e", arr.ToArray());
        }

        private string ComputeCategorySideTabSignature()
        {
            SortState st = GetSortState("Category");
            float scale = VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f;
            return categorySideTabDataRevision + "|" + (categoryFilter ?? "") + "|" + (currentPath ?? "") + "|" + (currentExtension ?? "") + "|" + (currentCreator ?? "") + "|" + (int)st.Type + "|" + (int)st.Direction + "|" + scale.ToString("R") + "|" + (categories != null ? categories.Count : 0);
        }

        private string ComputeCreatorSideTabSignature()
        {
            SortState st = GetSortState("Creator");
            float scale = VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f;
            return creatorSideTabDataRevision + "|" + (creatorFilter ?? "") + "|" + CurrentPathsSignatureFragment() + "|" + (currentExtension ?? "") + "|" + (currentCreator ?? "") + "|" + (int)st.Type + "|" + (int)st.Direction + "|" + scale.ToString("R");
        }

        /// <summary>
        /// Keeps Category and Creator side-tab UIs in sibling holders and toggles visibility when switching,
        /// rebuilding only when cache/sort/filter/scale data changes.
        /// </summary>
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

        private void UpdateHubLayout(bool populateSideTabLists = true)
        {
            // Left Side: Category (Top) / Tags (Bottom)
            if (leftTabScrollGO != null && leftSubTabScrollGO != null)
            {
                leftTabScrollGO.SetActive(true);
                leftSubTabScrollGO.SetActive(true);
                
                // Left Search Top (Category)
                if (leftSearchInput != null) 
                {
                    leftSearchInput.gameObject.SetActive(true);
                    // For now, no separate search for categories on left, but let's clear it
                    if (leftSearchInput.placeholder is Text ph) ph.text = VPBTranslation.T("gallery.search.categories", "Categories...");
                }

                // Left Search Bottom (Tags)
                if (leftSubSearchInput != null)
                {
                    leftSubSearchInput.gameObject.SetActive(true);
                    if (leftSubSearchInput.text != tagFilter) leftSubSearchInput.text = tagFilter;
                    if (leftSubSearchInput.placeholder is Text ph) ph.text = VPBTranslation.T("gallery.search.tags", "Search Tags...");
                }

                RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                leftRT.anchorMin = new Vector2(0, 0.5f);
                leftRT.anchorMax = new Vector2(0, 1);
                leftRT.offsetMin = new Vector2(10, SideTabSplitSeamInset());
                leftRT.offsetMax = new Vector2(leftRT.offsetMax.x, TabScrollTopOffset());

                RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                subRT.anchorMin = new Vector2(0, 0);
                subRT.anchorMax = new Vector2(0, 0.5f);
                subRT.offsetMax = new Vector2(subRT.offsetMax.x, SubTabScrollPaneTopOffset());
                subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin);

                if (populateSideTabLists)
                {
                    UpdateTabs(ContentType.Hub, leftTabContainerGO, leftActiveTabButtons, true);
                    UpdateTabs(ContentType.HubTags, leftSubTabContainerGO, leftSubActiveTabButtons, true);
                }
            }

            // Right Side: Pay Type (Top 20%) / Creator (Bottom 80%)
            if (rightTabScrollGO != null && rightSubTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(true);
                rightSubTabScrollGO.SetActive(true);

                // Right Search Top (Pay Type) - Hide search
                if (rightSearchInput != null) rightSearchInput.gameObject.SetActive(false);

                // Right Search Bottom (Creators)
                if (rightSubSearchInput != null)
                {
                    rightSubSearchInput.gameObject.SetActive(true);
                    if (rightSubSearchInput.text != creatorFilter) rightSubSearchInput.text = creatorFilter;
                    if (rightSubSearchInput.placeholder is Text ph) ph.text = VPBTranslation.T("gallery.search.creators", "Search Creators...");
                    
                    // Adjust anchor for 70/30 split
                    RectTransform rt = rightSubSearchInput.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(1, 0.7f);
                    rt.anchorMax = new Vector2(1, 0.7f);
                }

                if (rightSubSortBtn != null)
                {
                    RectTransform rt = rightSubSortBtn.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(1, 0.7f);
                    rt.anchorMax = new Vector2(1, 0.7f);
                }

                RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                rightRT.anchorMin = new Vector2(1, 0.7f);
                rightRT.anchorMax = new Vector2(1, 1);
                rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, SideTabSplitSeamInset());
                rightRT.offsetMax = new Vector2(rightRT.offsetMax.x, TabScrollTopOffset());

                RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                subRT.anchorMin = new Vector2(1, 0);
                subRT.anchorMax = new Vector2(1, 0.7f);
                subRT.offsetMax = new Vector2(subRT.offsetMax.x, SubTabScrollPaneTopOffset());
                subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin);

                if (populateSideTabLists)
                {
                    UpdateTabs(ContentType.HubPayTypes, rightTabContainerGO, rightActiveTabButtons, false);
                    UpdateTabs(ContentType.HubCreators, rightSubTabContainerGO, rightSubActiveTabButtons, false);
                }
            }

            SyncSidePaneTopSortButtonVisuals();
        }

        /// <summary>All/Addon/Custom row order from persisted <c>SceneSource</c> sort (same 4 modes as icon cycle).</summary>
        private List<string> GetOrderedSceneSourceFilterLabels()
        {
            SortState st = GetSortState("SceneSource");
            int mode = TryGetSidePaneFourModeIndex(st);
            if (mode < 0) mode = 0;
            switch (mode)
            {
                case 1:
                    return new List<string> { "Custom Scenes", "Addon Scenes", "All Scenes" };
                case 2:
                    return new List<string> { "Addon Scenes", "All Scenes", "Custom Scenes" };
                case 3:
                    return new List<string> { "Custom Scenes", "All Scenes", "Addon Scenes" };
                default:
                    return new List<string> { "All Scenes", "Addon Scenes", "Custom Scenes" };
            }
        }

        private void UpdateTabs(ContentType contentType, GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;

            foreach (var btn in trackedButtons)
            {
                ReturnTabButton(btn);
            }
            trackedButtons.Clear();

            if (contentType == ContentType.Category)
            {
                if (categories == null || categories.Count == 0) return;
                if (!categoriesCached) CacheCategoryCounts();

                // Sort
                var displayCategories = new List<Gallery.Category>(categories);
                var sortState = GetSortState("Category");
                GallerySortManager.Instance.SortCategories(displayCategories, sortState, categoryCounts);

                foreach (var cat in displayCategories)
                {
                    if (!string.IsNullOrEmpty(categoryFilter) && cat.name.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var c = cat;
                    bool isActive = (c.path == currentPath && c.extension == currentExtension);
                    Color btnColor = isActive ? ColorCategory : new Color(0.25f, 0.25f, 0.25f, 1f);

                    int count = 0;
                    if (categoryCounts.ContainsKey(c.name)) count = categoryCounts[c.name];
                    
                    // Plugins are mostly local Custom/Scripts files; keep the tab visible even when count is 0 (fresh install).
                    if (count == 0 && !isActive && !string.Equals(c.name, "Plugins", StringComparison.OrdinalIgnoreCase)) continue;
                    if (VPBConfig.Instance != null && VPBConfig.Instance.IsHiddenCategory(c.name) && !isActive) continue;

                    string label = c.name + " (" + count + ")";

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        Show(c.name, c.extension, c.path);
                        if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                        {
                            Settings.Instance.LastGalleryPage.Value = c.name;
                        }
                        if (VPBConfig.Instance != null)
                        {
                            VPBConfig.Instance.LastGalleryCategory = c.name;
                            try { VPBConfig.Instance.Save(); } catch { }
                        }
                        UpdateTabs();
                    }, trackedButtons, () => {
                        currentPath = "";
                        currentPaths = null;
                        currentExtension = "";
                        if (titleText != null) titleText.text = VPBTranslation.T("gallery.title.all_categories", "All Categories");
                        RefreshFiles();
                        UpdateTabs();
                    });
                }
            }
            else if (contentType == ContentType.Creator)
            {
                if (!creatorsCached) CacheCreators();
                if (cachedCreators == null) return;
                
                // Sort
                var sortState = GetSortState("Creator");
                GallerySortManager.Instance.SortCreators(cachedCreators, sortState);

                foreach (var creator in cachedCreators)
                {
                    if (!string.IsNullOrEmpty(creatorFilter) && creator.Name.IndexOf(creatorFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string cName = creator.Name;
                    bool isActive = (currentCreator == cName);
                    Color btnColor = isActive ? ColorCreator : new Color(0.25f, 0.25f, 0.25f, 1f);

                    string label = cName + " (" + creator.Count + ")";

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        if (currentCreator == cName) currentCreator = "";
                        else currentCreator = cName;
                        categoriesCached = false;
                        tagsCached = false;
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons, () => {
                        currentCreator = "";
                        categoriesCached = false;
                        tagsCached = false;
                        RefreshFiles();
                        UpdateTabs();
                    });
                }
            }
            else if (contentType == ContentType.Ratings)
            {
                var ratingsList = new List<string> { "All Ratings", "5 Stars", "4 Stars", "3 Stars", "2 Stars", "1 Star", "No Ratings" };
                
                Color ratingColor = new Color(0.7f, 0.6f, 0.2f, 1f); // Gold-ish

                foreach (var rating in ratingsList)
                {
                    bool isActive = (currentRatingFilter == rating);
                    Color btnColor = isActive ? ratingColor : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, rating, btnColor, isActive, () => {
                        if (currentRatingFilter == rating) currentRatingFilter = "";
                        else currentRatingFilter = rating;
                        
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
                }
            }
            else if (contentType == ContentType.AppearanceSource)
            {
                Color appearanceColor = new Color(0.2f, 0.4f, 0.7f, 1f);

                if (!tagsCached) CacheTagCounts();

                int allCount = appearanceSourceCountAll;
                int presetsCount = appearanceSourceCountPresets;
                int customCount = appearanceSourceCountCustom;

                string[] appearanceKeys = new string[] { "", "presets", "custom" };
                string[] appearanceLabels = new string[]
                {
                    "All (" + allCount + ")",
                    "Presets (" + presetsCount + ")",
                    "Custom (" + customCount + ")",
                };

                for (int i = 0; i < appearanceKeys.Length; i++)
                {
                    string key = appearanceKeys[i];
                    string label = appearanceLabels[i];
                    bool isActive = string.Equals(currentAppearanceSourceFilter, key, StringComparison.OrdinalIgnoreCase);
                    Color btnColor = isActive ? appearanceColor : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        currentAppearanceSourceFilter = key;
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
                }

                {
                    Color inactive = new Color(0.25f, 0.25f, 0.25f, 1f);
                    Color active = new Color(0.35f, 0.35f, 0.6f, 1f);

                    string[] options = new string[] { "Female", "Male", "Futa" };
                    for (int gi = 0; gi < options.Length; gi++)
                    {
                        string opt = options[gi];
                        AppearanceSubfilter flag = 0;
                        if (opt == "Male") flag = AppearanceSubfilter.Male;
                        else if (opt == "Female") flag = AppearanceSubfilter.Female;
                        else if (opt == "Futa") flag = AppearanceSubfilter.Futa;

                        bool isGenderActive = (flag != 0) && ((appearanceSubfilter & flag) != 0);
                        Color btnColor2 = isGenderActive ? active : inactive;

                        int cnt = 0;
                        if (opt == "Male") cnt = isGenderActive ? appearanceSubfilterCurrentCountMale : appearanceSubfilterFacetCountMale;
                        else if (opt == "Female") cnt = isGenderActive ? appearanceSubfilterCurrentCountFemale : appearanceSubfilterFacetCountFemale;
                        else if (opt == "Futa") cnt = isGenderActive ? appearanceSubfilterCurrentCountFuta : appearanceSubfilterFacetCountFuta;

                        string label2 = opt + " (" + cnt + ")";

                        CreateTabButton(container.transform, label2, btnColor2, isGenderActive, () => {
                            if (flag != 0)
                            {
                                if ((appearanceSubfilter & flag) != 0) appearanceSubfilter &= ~flag;
                                else appearanceSubfilter |= flag;
                            }
                            tagsCached = false;
                                RefreshFiles();
                            UpdateTabs();
                        }, trackedButtons);
                    }
                }
            }
            else if (contentType == ContentType.Size)
            {
                var sizeFilters = new List<string> { "All Sizes", "Tiny (< 10MB)", "Small (10-100MB)", "Medium (100-500MB)", "Large (500MB-1GB)", "Very Large (> 1GB)" };
                
                Color sizeColor = new Color(0.2f, 0.7f, 0.4f, 1f); // Green-ish

                foreach (var size in sizeFilters)
                {
                    bool isActive = (currentSizeFilter == size);
                    Color btnColor = isActive ? sizeColor : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, size, btnColor, isActive, () => {
                        if (currentSizeFilter == size) currentSizeFilter = "";
                        else currentSizeFilter = size;
                        
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
                }
            }
            else if (contentType == ContentType.SceneSource)
            {
                var sceneFilters = GetOrderedSceneSourceFilterLabels();
                Color sceneColor = new Color(0.2f, 0.4f, 0.7f, 1f); // Blue-ish

                foreach (var filter in sceneFilters)
                {
                    bool isActive = (currentSceneSourceFilter == filter) || (string.IsNullOrEmpty(currentSceneSourceFilter) && filter == "All Scenes");
                    Color btnColor = isActive ? sceneColor : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, filter, btnColor, isActive, () => {
                        if (filter == "All Scenes") currentSceneSourceFilter = "";
                        else currentSceneSourceFilter = filter;
                        
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
                }
            }
            else if (contentType == ContentType.Hub)
            {
                 UpdateHubCategories(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.HubTags)
            {
                 UpdateHubTags(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.HubPayTypes)
            {
                 UpdateHubPayTypes(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.HubCreators)
            {
                 UpdateHubCreators(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.Tags)
            {
                if (!tagsCached) CacheTagCounts();

                // Determine which tags to show
                List<string> tagsToShow = new List<string>();
                string title = titleText != null ? titleText.text : "";

                if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Clothing subfilters (shown only for Clothing)
                    {
                        Color inactive = new Color(0.25f, 0.25f, 0.25f, 1f);
                        Color active = new Color(0.35f, 0.35f, 0.6f, 1f);

                        string[] options = new string[] { "Real Clothing", "Presets", "Custom", "Items", "Male", "Female", "Decals" };
                        for (int gi = 0; gi < options.Length; gi++)
                        {
                            string opt = options[gi];
                            ClothingSubfilter flag = 0;
                            if (opt == "Real Clothing") flag = ClothingSubfilter.RealClothing;
                            else if (opt == "Presets") flag = ClothingSubfilter.Presets;
                            else if (opt == "Custom") flag = ClothingSubfilter.Custom;
                            else if (opt == "Items") flag = ClothingSubfilter.Items;
                            else if (opt == "Male") flag = ClothingSubfilter.Male;
                            else if (opt == "Female") flag = ClothingSubfilter.Female;
                            else if (opt == "Decals") flag = ClothingSubfilter.Decals;

                            bool isActive = (flag != 0) && ((clothingSubfilter & flag) != 0);
                            Color btnColor = isActive ? active : inactive;

                            int cnt = 0;
                            if (opt == "Real Clothing") cnt = clothingSubfilterCountReal;
                            else if (opt == "Presets") cnt = clothingSubfilterCountPresets;
                            else if (opt == "Custom") cnt = clothingSubfilterCountCustom;
                            else if (opt == "Items") cnt = clothingSubfilterCountItems;
                            else if (opt == "Male") cnt = clothingSubfilterCountMale;
                            else if (opt == "Female") cnt = clothingSubfilterCountFemale;
                            else if (opt == "Decals") cnt = clothingSubfilterCountDecals;

                            string label = opt + " (" + cnt + ")";

                            CreateTabButton(container.transform, label, btnColor, isActive, () => {
                                if (flag != 0)
                                {
                                    if ((clothingSubfilter & flag) != 0) clothingSubfilter &= ~flag;
                                    else clothingSubfilter |= flag;
                                }
                                tagsCached = false;
                                        RefreshFiles();
                                UpdateTabs();
                            }, trackedButtons);
                        }
                    }

                    tagsToShow.AddRange(TagFilter.ClothingTypeTags);
                    tagsToShow.AddRange(TagFilter.ClothingRegionTags);
                    tagsToShow.AddRange(TagFilter.ClothingOtherTags);
                }
                else if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    {
                        Color inactive = new Color(0.25f, 0.25f, 0.25f, 1f);
                        Color active = new Color(0.35f, 0.35f, 0.6f, 1f);

                        string[] options = new string[] { "Female", "Male", "Futa" };
                        for (int gi = 0; gi < options.Length; gi++)
                        {
                            string opt = options[gi];
                            AppearanceSubfilter flag = 0;
                            if (opt == "Male") flag = AppearanceSubfilter.Male;
                            else if (opt == "Female") flag = AppearanceSubfilter.Female;
                            else if (opt == "Futa") flag = AppearanceSubfilter.Futa;

                            bool isActive = (flag != 0) && ((appearanceSubfilter & flag) != 0);
                            Color btnColor = isActive ? active : inactive;

                            int cnt = 0;
                            if (opt == "Male") cnt = isActive ? appearanceSubfilterCurrentCountMale : appearanceSubfilterFacetCountMale;
                            else if (opt == "Female") cnt = isActive ? appearanceSubfilterCurrentCountFemale : appearanceSubfilterFacetCountFemale;
                            else if (opt == "Futa") cnt = isActive ? appearanceSubfilterCurrentCountFuta : appearanceSubfilterFacetCountFuta;

                            string label = opt + " (" + cnt + ")";

                            CreateTabButton(container.transform, label, btnColor, isActive, () => {
                                if (flag != 0)
                                {
                                    if ((appearanceSubfilter & flag) != 0) appearanceSubfilter &= ~flag;
                                    else appearanceSubfilter |= flag;
                                }
                                tagsCached = false;
                                        RefreshFiles();
                                UpdateTabs();
                            }, trackedButtons);
                        }
                    }
                }
                else if (title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    {
                        Color inactive = new Color(0.25f, 0.25f, 0.25f, 1f);
                        Color active = new Color(0.35f, 0.35f, 0.6f, 1f);

                        // Pose people-count filter (Single vs Dual)
                        {
                            bool isSingleActive = (posePeopleFilter == PosePeopleFilter.Single);
                            bool isDualActive = (posePeopleFilter == PosePeopleFilter.Dual);

                            CreateTabButton(container.transform, string.Format(VPBTranslation.T("gallery.pose.single_count", "Single ({0})"), posePeopleFacetCountSingle), isSingleActive ? active : inactive, isSingleActive, () => {
                                posePeopleFilter = (posePeopleFilter == PosePeopleFilter.Single) ? PosePeopleFilter.All : PosePeopleFilter.Single;
                                        RefreshFiles();
                                UpdateTabs();
                            }, trackedButtons);

                            CreateTabButton(container.transform, string.Format(VPBTranslation.T("gallery.pose.dual_count", "Dual ({0})"), posePeopleFacetCountDual), isDualActive ? active : inactive, isDualActive, () => {
                                posePeopleFilter = (posePeopleFilter == PosePeopleFilter.Dual) ? PosePeopleFilter.All : PosePeopleFilter.Dual;
                                        RefreshFiles();
                                UpdateTabs();
                            }, trackedButtons);
                        }
                    }
                }
                else if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tagsToShow.AddRange(TagFilter.HairTypeTags);
                    tagsToShow.AddRange(TagFilter.HairRegionTags);
                    tagsToShow.AddRange(TagFilter.HairOtherTags);
                }
                
                // Filter
                if (!string.IsNullOrEmpty(tagFilter))
                {
                    tagsToShow.RemoveAll(t => t.IndexOf(tagFilter, StringComparison.OrdinalIgnoreCase) < 0);
                }

                // Sort
                var sortState = GetSortState("Tags");
                if (sortState.Type == SortType.Name)
                {
                    if (sortState.Direction == SortDirection.Ascending) tagsToShow.Sort();
                    else tagsToShow.Sort((a, b) => b.CompareTo(a));
                }
                else if (sortState.Type == SortType.Count)
                {
                    tagsToShow.Sort((a, b) => {
                        int cA = tagCounts.ContainsKey(a) ? tagCounts[a] : 0;
                        int cB = tagCounts.ContainsKey(b) ? tagCounts[b] : 0;
                        int cmp = cA.CompareTo(cB);
                        if (cmp == 0) return a.CompareTo(b);
                        return sortState.Direction == SortDirection.Ascending ? cmp : -cmp;
                    });
                }
                
                foreach (var tag in tagsToShow)
                {
                    int count = 0;
                    if (tagCounts.ContainsKey(tag)) count = tagCounts[tag];
                    
                    bool isActive = activeTags.Contains(tag);
                    
                    if (count == 0 && !isActive) continue;

                    string label = tag + " (" + count + ")";
                    Color btnColor = isActive ? new Color(0.5f, 0.2f, 0.5f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        if (activeTags.Contains(tag)) activeTags.Remove(tag);
                        else activeTags.Add(tag);
                        
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
                }

                // Update Clear Button
                GameObject clearBtn = isLeft ? leftSubClearBtn : rightSubClearBtn;
                Text clearBtnText = isLeft ? leftSubClearBtnText : rightSubClearBtnText;
                
                if (clearBtn != null)
                {
                    if (activeTags.Count > 0)
                    {
                        clearBtn.SetActive(true);
                        if (clearBtnText != null) clearBtnText.text = string.Format(VPBTranslation.T("gallery.tags.clear_selected_count", "Clear Selected ({0})"), activeTags.Count);
                    }
                    else
                    {
                        clearBtn.SetActive(false);
                    }
                }
            }
            
            SetLayerRecursive(container, 5);
        }

        private static void AddBorderEdge(GameObject parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        {
            AddBorderEdge(parent, anchorMin, anchorMax, pivot, sizeDelta, Color.white);
        }

        private static void AddBorderEdge(GameObject parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Color color)
        {
            GameObject go = new GameObject("E");
            go.transform.SetParent(parent.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            go.AddComponent<Image>().color = color;
        }

        private void CreateTabButton(Transform parent, string label, Color color, bool isActive, UnityAction onClick, List<GameObject> targetList, UnityAction onRightClick = null)
        {
            GameObject btnGO = GetTabButton(parent);
            if (btnGO == null)
            {
                btnGO = UI.CreateUIButton(parent.gameObject, 170, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                AddHoverDelegate(btnGO);
            }
            
            // Standard Button Configuration
            Button btnComp = btnGO.GetComponent<Button>();
            btnComp.onClick.RemoveAllListeners();
            if (onClick != null) btnComp.onClick.AddListener(onClick);

            UIRightClickDelegate rightClickDelegate = btnGO.GetComponent<UIRightClickDelegate>();
            if (rightClickDelegate == null) rightClickDelegate = btnGO.AddComponent<UIRightClickDelegate>();
            rightClickDelegate.OnRightClick = (onRightClick != null) ? (Action)(() => onRightClick.Invoke()) : null;
            
            Image img = btnGO.GetComponent<Image>();
            img.color = color;
            
            float s = (VPBConfig.Instance != null) ? VPBConfig.Instance.InnerPaneScale : 1f;

            Text txt = btnGO.GetComponentInChildren<Text>();
            txt.text = label;
            txt.fontSize = Mathf.RoundToInt(18 * s);
            txt.color = Color.white;

            // Ensure LayoutElement
            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le == null) le = btnGO.AddComponent<LayoutElement>();
            le.minWidth = 140f * s;
            le.preferredWidth = 170f * s;
            le.minHeight = 35f * s;
            le.preferredHeight = 35f * s;
            le.flexibleWidth = 1;

            if (targetList != null) targetList.Add(btnGO);
        }

        private InputField CreateSearchInput(GameObject parent, float width, UnityAction<string> onValueChanged, Action onClear = null)
        {
            GameObject inputGO = new GameObject("SearchInput");
            inputGO.transform.SetParent(parent.transform, false);
            
            Image bg = inputGO.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            
            // Add Hover Border
            inputGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(inputGO);

            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.sizeDelta = new Vector2(width, 35);
            
            // Text Area
            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(38, 0); // Left offset accounts for search icon
            textAreaRT.offsetMax = new Vector2(-45, 0); // Room for X button

            // Search icon (left side of input)
            {
                var s = UI.LoadIconSprite("vpb_icons/search.png", new Color(0.5f, 0.5f, 0.5f, 1f));
                if (s != null)
                {
                    GameObject iconGO = new GameObject("SearchIcon");
                    iconGO.transform.SetParent(inputGO.transform, false);
                    Image iconImg = iconGO.AddComponent<Image>();
                    iconImg.sprite = s;
                    iconImg.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                    RectTransform iconRT = iconGO.GetComponent<RectTransform>();
                    iconRT.anchorMin = new Vector2(0, 0.5f);
                    iconRT.anchorMax = new Vector2(0, 0.5f);
                    iconRT.pivot = new Vector2(0, 0.5f);
                    iconRT.anchoredPosition = new Vector2(6, 0);
                    iconRT.sizeDelta = new Vector2(24, 24);
                }
            }
            
            // Placeholder
            GameObject placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);
            Text placeholderText = placeholder.AddComponent<Text>();
            placeholderText.text = VPBTranslation.T("gallery.search.main", "Search...");
            placeholderText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            placeholderText.fontSize = 18; // Increased from 14
            placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.alignment = TextAnchor.MiddleLeft; // Vertically centered
            RectTransform placeholderRT = placeholder.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = Vector2.zero;
            
            // Text
            GameObject text = new GameObject("Text");
            text.transform.SetParent(textArea.transform, false);
            Text textComponent = text.AddComponent<Text>();
            textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            textComponent.fontSize = 18; // Increased from 14
            textComponent.color = Color.white;
            textComponent.supportRichText = false;
            textComponent.alignment = TextAnchor.MiddleLeft; // Vertically centered
            RectTransform textRT = text.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            
            input.textComponent = textComponent;
            input.placeholder = placeholderText;
            input.onValueChanged.AddListener(onValueChanged);
            
            // Clear Button
            GameObject clearBtn = UI.CreateUIButton(inputGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleRight, () => { // Increased size and font
                input.text = "";
                input.ActivateInputField();
                input.MoveTextEnd(false);
                onClear?.Invoke();
            });
            RectTransform clearRT = clearBtn.GetComponent<RectTransform>();
            clearRT.anchorMin = new Vector2(1, 0.5f);
            clearRT.anchorMax = new Vector2(1, 0.5f);
            clearRT.pivot = new Vector2(1, 0.5f);
            clearRT.anchoredPosition = new Vector2(-5, 0);
            clearBtn.GetComponent<Image>().color = new Color(0,0,0,0); // Transparent bg
            
            Text clearText = clearBtn.GetComponentInChildren<Text>();
            clearText.color = new Color(0.6f, 0.6f, 0.6f);

            UIHoverColor hover = clearBtn.AddComponent<UIHoverColor>();
            hover.targetText = clearText;
            hover.normalColor = clearText.color;
            hover.hoverColor = Color.red;

            // ESC key handling to clear and refocus
            Button clearBtnComponent = clearBtn.GetComponent<Button>();
            inputGO.AddComponent<SearchInputESCHandler>().Initialize(input, clearBtnComponent);

            return input;
        }

        private GameObject GetTabButton(Transform parent)
        {
            if (tabButtonPool.Count > 0)
            {
                GameObject btn = tabButtonPool.Pop();
                btn.transform.SetParent(parent, false);
                btn.SetActive(true);
                return btn;
            }
            return null;
        }

        private void ReturnTabButton(GameObject btn)
        {
            if (btn == null) return;
            btn.SetActive(false);
            // Keep parented to ensure cleanup on destroy
            if (backgroundBoxGO != null) btn.transform.SetParent(backgroundBoxGO.transform, false);
            tabButtonPool.Push(btn);
        }

        public GameObject InjectButton(string label, UnityAction action)
        {
            GameObject btnGO;
            if (navButtonPool.Count > 0)
            {
                btnGO = navButtonPool.Pop();
                btnGO.SetActive(true);
            }
            else
            {
                btnGO = CreateNewNavButtonGO();
            }

            // Reset/Configure for Navigation
            BindNavigationButton(btnGO, label, action);
            activeButtons.Add(btnGO);
            return btnGO;
        }

        private GameObject CreateNewNavButtonGO()
        {
            GameObject btnGO = new GameObject("NavButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.6f, 1f);

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();

            GameObject navTextGO = new GameObject("NavText");
            navTextGO.transform.SetParent(btnGO.transform, false);
            Text t = navTextGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 24;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            
            RectTransform rt = navTextGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            return btnGO;
        }

        private void BindNavigationButton(GameObject btnGO, string label, UnityAction action)
        {
            btnGO.name = "NavButton_" + label.Replace("\n", ""); // Identification for Pool

            // Reset common elements
            Button btn = btnGO.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            if (action != null) btn.onClick.AddListener(action);

            // Set Text
            Transform navTextT = btnGO.transform.Find("NavText");
            if (navTextT != null)
            {
                Text t = navTextT.GetComponent<Text>();
                if (t != null) t.text = label;
            }

            // Set BG Color (Optional reset if changed elsewhere)
            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = new Color(0.2f, 0.4f, 0.6f, 1f); 
        }


        private void CreateFileButton(FileEntry file)
        {
            GameObject btnGO;
            if (fileButtonPool.Count > 0)
            {
                btnGO = fileButtonPool.Pop();
                btnGO.SetActive(true);
            }
            else
            {
                btnGO = CreateNewFileButtonGO();
            }
            
            BindFileButton(btnGO, file);
            btnGO.transform.SetAsLastSibling();
            activeButtons.Add(btnGO);
        }

        public GameObject CreateNewFileButtonGO()
        {
            GameObject btnGO = new GameObject("FileButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };

            // Thumbnail (Fill 1x1)
            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(btnGO.transform, false);
            RawImage thumbImg = thumbGO.AddComponent<RawImage>();
            thumbImg.color = new Color(0, 0, 0, 0.5f);
            RectTransform thumbRT = thumbGO.GetComponent<RectTransform>();
            thumbRT.anchorMin = Vector2.zero;
            thumbRT.anchorMax = Vector2.one;
            thumbRT.sizeDelta = Vector2.zero;
            thumbRT.offsetMin = new Vector2(3, 3);
            thumbRT.offsetMax = new Vector2(-3, -3);

            // Card Container (Hidden by default, positions below)
            GameObject cardGO = new GameObject("Card");
            cardGO.transform.SetParent(btnGO.transform, false);
            cardGO.SetActive(false);

            RectTransform cardRT = cardGO.AddComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0, 0); // Bottom
            cardRT.anchorMax = new Vector2(1, 0); // Bottom
            cardRT.pivot = new Vector2(0.5f, 0);  // Pivot Bottom (Inside)
            cardRT.anchoredPosition = Vector2.zero;
            cardRT.sizeDelta = new Vector2(0, 0); // Width stretch

            // Dynamic height based on content
            VerticalLayoutGroup cardVLG = cardGO.AddComponent<VerticalLayoutGroup>();
            cardVLG.childControlHeight = true;
            cardVLG.childControlWidth = true;
            cardVLG.childForceExpandHeight = false;
            cardVLG.childForceExpandWidth = true;
            cardVLG.padding = new RectOffset(5, 5, 5, 5);
            
            ContentSizeFitter cardCSF = cardGO.AddComponent<ContentSizeFitter>();
            cardCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Background
            Image cardBg = cardGO.AddComponent<Image>();
            cardBg.color = new Color(0, 0, 0, 0.8f);
            cardBg.raycastTarget = false;

            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(cardGO.transform, false);
            Text labelText = labelGO.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = 18;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
            labelText.supportRichText = true;
            labelText.raycastTarget = false;
            
            // Label Layout
            LayoutElement labelLE = labelGO.AddComponent<LayoutElement>();
            labelLE.minHeight = 30;

            // Hover Logic
            UIHoverReveal hover = btnGO.AddComponent<UIHoverReveal>();
            hover.card = cardGO;
            hover.panel = this;

            // List Row (Table mode)
            GameObject listRowGO = new GameObject("ListRow");
            listRowGO.transform.SetParent(btnGO.transform, false);
            listRowGO.SetActive(false);
            RectTransform listRowRT = listRowGO.AddComponent<RectTransform>();
            listRowRT.anchorMin = new Vector2(0, 0);
            listRowRT.anchorMax = new Vector2(1, 1);
            listRowRT.pivot = new Vector2(0, 0.5f);
            listRowRT.offsetMin = new Vector2(60, 0);
            listRowRT.offsetMax = new Vector2(-50, 0);

            VerticalLayoutGroup listVLG = listRowGO.AddComponent<VerticalLayoutGroup>();
            listVLG.childAlignment = TextAnchor.MiddleLeft;
            listVLG.childControlHeight = true;
            listVLG.childControlWidth = true;
            listVLG.childForceExpandHeight = false;
            listVLG.childForceExpandWidth = true;
            listVLG.spacing = 2f;
            listVLG.padding = new RectOffset(5, 5, 5, 5);

            // Name
            GameObject listNameGO = new GameObject("Name");
            listNameGO.transform.SetParent(listRowGO.transform, false);
            Text listNameText = listNameGO.AddComponent<Text>();
            listNameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            listNameText.fontSize = 28;
            listNameText.fontStyle = FontStyle.Bold;
            listNameText.color = Color.white;
            listNameText.alignment = TextAnchor.LowerLeft;
            listNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            listNameText.verticalOverflow = VerticalWrapMode.Truncate;
            listNameText.raycastTarget = false;
            LayoutElement listNameLE = listNameGO.AddComponent<LayoutElement>();
            listNameLE.flexibleWidth = 1;
            listNameLE.minHeight = 36;

            // Details Row
            GameObject detailsRowGO = new GameObject("Details");
            detailsRowGO.transform.SetParent(listRowGO.transform, false);
            HorizontalLayoutGroup detailsHLG = detailsRowGO.AddComponent<HorizontalLayoutGroup>();
            detailsHLG.childAlignment = TextAnchor.MiddleLeft;
            detailsHLG.childControlHeight = true;
            detailsHLG.childControlWidth = true;
            detailsHLG.childForceExpandHeight = false;
            detailsHLG.childForceExpandWidth = false;
            detailsHLG.spacing = 15f;
            detailsHLG.padding = new RectOffset(0, 0, 0, 0);
            LayoutElement detailsLE = detailsRowGO.AddComponent<LayoutElement>();
            detailsLE.flexibleWidth = 1;
            detailsLE.minHeight = 28;

            // Helper to create detail text
            GameObject CreateDetailText(string name, string placeholder, float width)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(detailsRowGO.transform, false);
                Text t = go.AddComponent<Text>();
                t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                t.fontSize = 22;
                t.color = new Color(0.75f, 0.75f, 0.75f, 1f);
                t.alignment = TextAnchor.MiddleLeft;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.raycastTarget = false;
                t.text = placeholder;
                LayoutElement le = go.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.minWidth = width * 0.5f;
                return go;
            }

            CreateDetailText("Size", "Size", 110);
            CreateDetailText("Date", "Date", 130);
            CreateDetailText("Category", "Category", 160);
            CreateDetailText("Deps", "D:", 80);
            CreateDetailText("Missing", "M:", 80);
            CreateDetailText("Dependents", "Dn:", 80);

            // Rating (Top-right corner)
            GameObject ratingGO = new GameObject("Rating");
            ratingGO.transform.SetParent(btnGO.transform, false);
            RectTransform ratingRT = ratingGO.AddComponent<RectTransform>();
            ratingRT.anchorMin = new Vector2(1, 1); // Top Right
            ratingRT.anchorMax = new Vector2(1, 1);
            ratingRT.pivot = new Vector2(1, 1);
            ratingRT.sizeDelta = new Vector2(40, 40);
            ratingRT.anchoredPosition = new Vector2(-2, -2);

            GameObject starBtnGO = UI.CreateUIButton(ratingGO, 32, 32, "★", 20, 0, 0, AnchorPresets.middleCenter, null);
            starBtnGO.name = "Star";
            starBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
            Text starIconText = starBtnGO.GetComponentInChildren<Text>();

            GameObject selectorGO = new GameObject("RatingSelector");
            selectorGO.transform.SetParent(btnGO.transform, false);
            RectTransform selectorRT = selectorGO.AddComponent<RectTransform>();
            // 3-row × 2-col grid: [X][1] / [2][3] / [4][5] — drops below star icon, aligns to right edge
            selectorRT.anchorMin = new Vector2(1, 1);
            selectorRT.anchorMax = new Vector2(1, 1);
            selectorRT.pivot = new Vector2(1, 1);
            selectorRT.sizeDelta = new Vector2(80, 114);
            selectorRT.anchoredPosition = new Vector2(-2, -44);

            CanvasGroup selectorCG = selectorGO.AddComponent<CanvasGroup>();
            selectorCG.alpha = 0f;
            selectorCG.interactable = false;
            selectorCG.blocksRaycasts = false;

            Image selectorBg = selectorGO.AddComponent<Image>();
            selectorBg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);

            GridLayoutGroup selectorGrid = selectorGO.AddComponent<GridLayoutGroup>();
            selectorGrid.cellSize = new Vector2(38, 36);
            selectorGrid.spacing = new Vector2(2, 2);
            selectorGrid.padding = new RectOffset(1, 1, 1, 1);
            selectorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            selectorGrid.constraintCount = 2;
            selectorGrid.childAlignment = TextAnchor.UpperLeft;

            RatingHandler ratingHandler = btnGO.AddComponent<RatingHandler>();
            Image[] optImages = new Image[6];
            Text[] optTexts = new Text[6];
            GameObject[] optBorders = new GameObject[6];
            for (int i = 0; i <= 5; i++)
            {
                int ratingValue = i;
                string label = i == 0 ? "X" : i.ToString();
                GameObject optBtnGO = UI.CreateUIButton(selectorGO, 38, 36, label, 22, 0, 0, AnchorPresets.middleCenter, () => ratingHandler.SetRating(ratingValue));
                optBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
                optImages[i] = optBtnGO.GetComponent<Image>();
                optImages[i].color = RatingHandler.RatingColors[i];
                optTexts[i] = optBtnGO.GetComponentInChildren<Text>();
                optTexts[i].color = i == 0 ? Color.red : Color.black;

                // Selection border: 4 white edge images inside the button, rendered before the label
                GameObject borderGO = new GameObject("SelectionBorder");
                borderGO.transform.SetParent(optBtnGO.transform, false);
                borderGO.transform.SetSiblingIndex(0);
                RectTransform borderRT = borderGO.AddComponent<RectTransform>();
                borderRT.anchorMin = Vector2.zero;
                borderRT.anchorMax = Vector2.one;
                borderRT.offsetMin = Vector2.zero;
                borderRT.offsetMax = Vector2.zero;
                AddBorderEdge(borderGO, new Vector2(0,1), new Vector2(1,1), new Vector2(0.5f,1), new Vector2(0,3));
                AddBorderEdge(borderGO, new Vector2(0,0), new Vector2(1,0), new Vector2(0.5f,0), new Vector2(0,3));
                AddBorderEdge(borderGO, new Vector2(0,0), new Vector2(0,1), new Vector2(0,0.5f), new Vector2(3,0));
                AddBorderEdge(borderGO, new Vector2(1,0), new Vector2(1,1), new Vector2(1,0.5f), new Vector2(3,0));
                borderGO.SetActive(false);
                optBorders[i] = borderGO;
            }
            ratingHandler.SetOptionRefs(optImages, optTexts, optBorders);

            Button starBtn = starBtnGO.GetComponent<Button>();
            starBtn.onClick.AddListener(() => ratingHandler.ToggleSelector());
            
            // Drag Logic
            UIDraggableItem draggable = btnGO.AddComponent<UIDraggableItem>();
            draggable.ThumbnailImage = thumbImg;
            draggable.Panel = this;

            // AutoInstall Badge (Top-left corner, opposite the star rating)
            GameObject aiBadgeGO = new GameObject("AutoInstallBadge");
            aiBadgeGO.transform.SetParent(btnGO.transform, false);
            RectTransform aiBadgeRT = aiBadgeGO.AddComponent<RectTransform>();
            aiBadgeRT.anchorMin = new Vector2(0, 1); // Top Left
            aiBadgeRT.anchorMax = new Vector2(0, 1);
            aiBadgeRT.pivot = new Vector2(0, 1);
            aiBadgeRT.sizeDelta = new Vector2(32, 32);
            aiBadgeRT.anchoredPosition = new Vector2(6, -6);
            Image aiBadgeBg = aiBadgeGO.AddComponent<Image>();
            aiBadgeBg.color = new Color(0f, 0.35f, 1f, 0.85f);
            aiBadgeBg.raycastTarget = false;
            GameObject aiBadgeTextGO = new GameObject("Text");
            aiBadgeTextGO.transform.SetParent(aiBadgeGO.transform, false);
            Text aiBadgeText = aiBadgeTextGO.AddComponent<Text>();
            aiBadgeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            aiBadgeText.fontSize = 22;
            aiBadgeText.fontStyle = FontStyle.Bold;
            aiBadgeText.color = Color.white;
            aiBadgeText.alignment = TextAnchor.MiddleCenter;
            aiBadgeText.text = "A";
            aiBadgeText.raycastTarget = false;
            RectTransform aiBadgeTextRT = aiBadgeTextGO.GetComponent<RectTransform>();
            aiBadgeTextRT.anchorMin = Vector2.zero;
            aiBadgeTextRT.anchorMax = Vector2.one;
            aiBadgeTextRT.sizeDelta = Vector2.zero;
            aiBadgeTextRT.anchoredPosition = Vector2.zero;
            aiBadgeGO.SetActive(false);

            // Hidden package badge (top-left, to the right of AutoInstall "A")
            GameObject hideBadgeGO = new GameObject("HidePackageBadge");
            hideBadgeGO.transform.SetParent(btnGO.transform, false);
            RectTransform hideBadgeRT = hideBadgeGO.AddComponent<RectTransform>();
            hideBadgeRT.anchorMin = new Vector2(0, 1);
            hideBadgeRT.anchorMax = new Vector2(0, 1);
            hideBadgeRT.pivot = new Vector2(0, 1);
            hideBadgeRT.sizeDelta = new Vector2(32, 32);
            hideBadgeRT.anchoredPosition = new Vector2(42, -6);
            Image hideBadgeBg = hideBadgeGO.AddComponent<Image>();
            hideBadgeBg.color = new Color(0.35f, 0.35f, 0.4f, 0.9f);
            hideBadgeBg.raycastTarget = false;
            GameObject hideBadgeTextGO = new GameObject("Text");
            hideBadgeTextGO.transform.SetParent(hideBadgeGO.transform, false);
            Text hideBadgeText = hideBadgeTextGO.AddComponent<Text>();
            hideBadgeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hideBadgeText.fontSize = 22;
            hideBadgeText.fontStyle = FontStyle.Bold;
            hideBadgeText.color = Color.white;
            hideBadgeText.alignment = TextAnchor.MiddleCenter;
            hideBadgeText.text = "H";
            hideBadgeText.raycastTarget = false;
            RectTransform hideBadgeTextRT = hideBadgeTextGO.GetComponent<RectTransform>();
            hideBadgeTextRT.anchorMin = Vector2.zero;
            hideBadgeTextRT.anchorMax = Vector2.one;
            hideBadgeTextRT.sizeDelta = Vector2.zero;
            hideBadgeTextRT.anchoredPosition = Vector2.zero;
            hideBadgeGO.SetActive(false);

            // List-mode hover indicator: thin vertical line at left edge of thumbnail (white, semi-transparent)
            GameObject listHoverBarGO = new GameObject("ListHoverBar");
            listHoverBarGO.transform.SetParent(btnGO.transform, false);
            Image listHoverBarImg = listHoverBarGO.AddComponent<Image>();
            listHoverBarImg.color = new Color(1f, 1f, 1f, 0.45f);
            listHoverBarImg.raycastTarget = false;
            RectTransform listHoverBarRT = listHoverBarGO.GetComponent<RectTransform>();
            listHoverBarRT.anchorMin = new Vector2(0, 0);
            listHoverBarRT.anchorMax = new Vector2(0, 1);
            listHoverBarRT.pivot = new Vector2(0, 0.5f);
            listHoverBarRT.sizeDelta = new Vector2(2, 0);
            listHoverBarRT.anchoredPosition = Vector2.zero;
            listHoverBarGO.SetActive(false);

            // List-mode selection indicator: left accent bar (yellow, opaque)
            GameObject listSelBarGO = new GameObject("ListSelectionBar");
            listSelBarGO.transform.SetParent(btnGO.transform, false);
            Image listSelBarImg = listSelBarGO.AddComponent<Image>();
            listSelBarImg.color = new Color(1f, 0.85f, 0f, 1f);
            listSelBarImg.raycastTarget = false;
            RectTransform listSelBarRT = listSelBarGO.GetComponent<RectTransform>();
            listSelBarRT.anchorMin = new Vector2(0, 0);
            listSelBarRT.anchorMax = new Vector2(0, 1);
            listSelBarRT.pivot = new Vector2(0, 0.5f);
            listSelBarRT.sizeDelta = new Vector2(3, 0);
            listSelBarRT.anchoredPosition = Vector2.zero;
            listSelBarGO.SetActive(false);

            // Wire hover bar into UIHoverBorder; selection bar is managed by UpdateFileButtonVisuals
            UIHoverBorder hoverBorderComp = btnGO.GetComponent<UIHoverBorder>();
            if (hoverBorderComp != null) hoverBorderComp.hoverBorderGO = listHoverBarGO;

            SetLayerRecursive(btnGO, 5);
            return btnGO;
        }

        public void UpdateFileButtonVisuals(GameObject btnGO, FileEntry file)
        {
            if (btnGO == null)
            {
                LogUtil.LogError("[VPB] UpdateFileButtonVisuals: btnGO is null");
                return;
            }
            if (file == null)
            {
                LogUtil.LogError("[VPB] UpdateFileButtonVisuals: file is null");
                return;
            }
            
            // Image
            Image img = btnGO.GetComponent<Image>();
            bool isSelected = (!string.IsNullOrEmpty(file.Path) && selectedFilePaths.Contains(file.Path));
            
            Outline outline = btnGO.GetComponent<Outline>();
            UIHoverBorder hoverBorder = btnGO.GetComponent<UIHoverBorder>();

            if (layoutMode == GalleryLayoutMode.List)
            {
                // List mode: always dark background. Use ListSelectionBorder (4 edge Images)
                // for selection highlight — avoids Outline which fills the whole row yellow.
                bool isMaster = false;
                try { isMaster = IsFilterActive && IsFilterMasterEntry(file); } catch { isMaster = false; }
                img.color = isMaster ? new Color(0.1f, 0.25f, 0.45f, 0.55f) : new Color(0f, 0f, 0f, 0.4f);
                if (outline != null) { outline.effectColor = new Color(0f, 0f, 0f, 0f); outline.enabled = false; }
                if (hoverBorder != null) hoverBorder.isSelected = isSelected;
                // selection bar (left accent) is independent of hover bar
                Transform selBar = btnGO.transform.Find("ListSelectionBar");
                if (selBar != null) selBar.gameObject.SetActive(isSelected);
            }
            else
            {
                if (isSelected) img.color = new Color(0.7f, 0.7f, 0.2f, 1f);
                else img.color = Color.gray;

                // Hide list indicators in grid mode
                Transform selBar2 = btnGO.transform.Find("ListSelectionBar");
                if (selBar2 != null) selBar2.gameObject.SetActive(false);
                Transform hoverBar2 = btnGO.transform.Find("ListHoverBar");
                if (hoverBar2 != null) hoverBar2.gameObject.SetActive(false);

                if (outline != null)
                {
                    outline.effectColor = Color.yellow;
                    if (outline.enabled != isSelected) outline.enabled = isSelected;
                    outline.effectDistance = isSelected ? new Vector2(4f, -4f) : new Vector2(2f, -2f);

                    if (hoverBorder != null)
                    {
                        hoverBorder.isSelected = isSelected;
                        hoverBorder.borderSize = isSelected ? 4f : 2f;
                    }
                }
            }
        }

        public void BindFileButton(GameObject btnGO, FileEntry file)
        {
            // Validate inputs
            if (btnGO == null || file == null)
            {
                LogUtil.LogError("[VPB] BindFileButton: btnGO or file is null");
                return;
            }

            // Validate file properties
            if (string.IsNullOrEmpty(file.Name) || string.IsNullOrEmpty(file.Path))
            {
                LogUtil.LogError($"[VPB] BindFileButton: Invalid entry - Name={file.Name}, Path={file.Path}");
                return;
            }

            btnGO.name = "FileButton_" + file.Name;

            // Update mapping
            Image img = btnGO.GetComponent<Image>();
            if (img != null)
            {
                fileButtonImages[file.Path] = img;
            }

            // Color missing entries red
            if (file is VirtualFileEntry)
            {
                if (img != null) img.color = new Color(0.4f, 0.15f, 0.15f, 0.8f); // Red shade
            }

            // Update Visuals
            UpdateFileButtonVisuals(btnGO, file);

            // Button
            Button btn = btnGO.GetComponent<Button>();
            if (btn != null)
            {
                // Optimization: Avoid RemoveAllListeners if we can simply swap the target
                // But Unity Events are tricky. Ideally we'd have a single listener that checks a field on the button.
                // For now, keep it safe but cleaner.
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => {
                    var dragItem = btnGO.GetComponent<UIDraggableItem>();
                    if (dragItem != null && dragItem.IsLongPress) return;
                    OnFileClick(file);
                });
            }

            // Right Click
            // Kept: right click selects + opens the actions panel, but no longer shows the dependency context menu.
            var rightClick = btnGO.GetComponent<UIRightClickDelegate>();
            if (rightClick == null) rightClick = btnGO.AddComponent<UIRightClickDelegate>();
            rightClick.OnRightClick = () => OnFileRightClick(file);

            bool isListMode = (layoutMode == GalleryLayoutMode.List);

            // List Row + Rating selector visibility (List/Table mode)
            Transform listRowTr = btnGO.transform.Find("ListRow");
            if (listRowTr != null)
            {
                listRowTr.gameObject.SetActive(isListMode);
                if (isListMode)
                {
                    RectTransform listRowRT = listRowTr as RectTransform;
                    if (listRowRT != null)
                    {
                        float leftPad = listThumbSize + 15f;
                        listRowRT.offsetMin = new Vector2(leftPad, 0);
                        listRowRT.offsetMax = new Vector2(-50, 0);
                    }
                }
            }

            Transform selectorTr = btnGO.transform.Find("RatingSelector");
            if (selectorTr != null)
            {
                selectorTr.gameObject.SetActive(true);
                RatingHandler rh = btnGO.GetComponent<RatingHandler>();
                if (rh != null) rh.CloseSelector();
            }

            // Card Container (Hidden in List mode, Visible in Grid mode? No, Card is for VerticalCard mode which is removed or mapped to Grid if we had it)
            // Wait, Grid mode uses the old style overlay? Or does Grid mode use Card? 
            // In the previous code, Grid mode had "Card" active only if VerticalCard.
            // layoutMode == GalleryLayoutMode.Grid means standard grid which usually has hover reveal or overlay.
            // Let's check CreateNewFileButtonGO. CardGO is hidden by default.
            
            Transform cardTr = btnGO.transform.Find("Card");
            if (cardTr != null)
            {
                // In the new 2-mode system, Grid usually implies the simple thumbnail + optional overlay.
                // If we want "Grid" to look like cards, we set this true.
                // But typically Grid = just thumbnail with hover name.
                // VerticalCard was the one with persistent text below.
                // Since we only have Grid and List, let's assume Grid means "Thumbnail Grid".
                
                // So Card is hidden in both Grid (standard) and List.
                cardTr.gameObject.SetActive(false);
            }

            // Thumbnail
            Transform thumbTr = btnGO.transform.Find("Thumbnail");
            if (thumbTr == null) thumbTr = btnGO.transform.Find("ThumbContainer/Thumbnail");

            if (thumbTr != null)
            {
                if (!thumbTr.gameObject.activeSelf) thumbTr.gameObject.SetActive(true);
                RectTransform thumbRT = thumbTr as RectTransform;
                
                if (isListMode)
                {
                    // Full height square on left
                    thumbRT.anchorMin = new Vector2(0, 0);
                    thumbRT.anchorMax = new Vector2(0, 1);
                    thumbRT.pivot = new Vector2(0, 0.5f);
                    thumbRT.offsetMin = new Vector2(0, 0);
                    thumbRT.offsetMax = new Vector2(listThumbSize, 0);
                }
                else
                {
                    // Full thumb (Grid)
                    thumbRT.anchorMin = Vector2.zero;
                    thumbRT.anchorMax = Vector2.one;
                    thumbRT.pivot = new Vector2(0.5f, 0.5f);
                    thumbRT.anchoredPosition = Vector2.zero;
                    thumbRT.offsetMin = new Vector2(3, 3);
                    thumbRT.offsetMax = new Vector2(-3, -3);
                }

                RawImage thumbImg = thumbTr.GetComponent<RawImage>();
                if (thumbImg != null)
                {
                    // Let LoadThumbnail decide whether this is a true rebind or the same
                    // thumbnail; unconditional clearing causes a visible flash on reopen.
                    LoadThumbnail(file, thumbImg);
                }
            }

            // Hide NavText
            Transform navTextTr = btnGO.transform.Find("NavText");
            if (navTextTr != null && navTextTr.gameObject.activeSelf) navTextTr.gameObject.SetActive(false);
            
            // Hover Path
            UIHoverReveal hover = btnGO.GetComponent<UIHoverReveal>();
            if (hover != null) hover.file = file;

            // Label
            Transform labelTr = btnGO.transform.Find("Card/Label");
            if (labelTr != null)
            {
                Text labelText = labelTr.GetComponent<Text>();
                if (labelText != null)
                {
                    string displayName = string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name;
                    labelText.text = displayName;
                    // Add tooltip showing full package path if available
                    try
                    {
                        if (file is VarFileEntry vfe && vfe.Package != null)
                        {
                            AddTooltipPlain(labelTr.gameObject, $"Package: {vfe.Package.Uid}.var");
                        }
                    }
                    catch { }
                }
            }

            // Rating always visible — grid uses it as a compact favorite toggle
            Transform ratingTr = btnGO.transform.Find("Rating");
            if (ratingTr != null)
            {
                ratingTr.gameObject.SetActive(true);
            }

            // AutoInstall Badge — show blue "A" for packages flagged as AutoInstall
            Transform aiBadgeTr = btnGO.transform.Find("AutoInstallBadge");
            if (aiBadgeTr != null)
            {
                aiBadgeTr.gameObject.SetActive(file.IsAutoInstall());
            }

            Transform hideBadgeTr = btnGO.transform.Find("HidePackageBadge");
            if (hideBadgeTr != null)
            {
                hideBadgeTr.gameObject.SetActive(PackageHidePrefs.IsGalleryHideBadgeVisible(file));
            }

            // List Row Bind
            if (isListMode)
            {
                if (listRowTr != null && !listRowTr.gameObject.activeSelf) listRowTr.gameObject.SetActive(true);

                Transform nameTr = btnGO.transform.Find("ListRow/Name");
                if (nameTr != null)
                {
                    Text t = nameTr.GetComponent<Text>();
                    if (t != null)
                    {
                        string displayName = GetGalleryListRowDisplayName(file);
                        t.text = displayName;
                        SetGalleryListRowNameTooltip(nameTr.gameObject, file);
                    }
                }

                Transform depsTr = btnGO.transform.Find("ListRow/Details/Deps");
                if (depsTr != null)
                {
                    Text t = depsTr.GetComponent<Text>();
                    if (t != null)
                    {
                        int deps = GallerySortManager.GetDepsCount(file);
                        string v = deps.ToString().PadLeft(3);
                        t.text = "D: " + v + "  |  ";
                        t.raycastTarget = true;

                        // Hover-highlight only the value; Set() resets color on recycle.
                        try
                        {
                            var hv = depsTr.GetComponent<UIRichValueHover>();
                            if (hv == null) hv = depsTr.gameObject.AddComponent<UIRichValueHover>();
                            hv.target = t;
                            hv.Set("D: ", v, "  |  ");
                        }
                        catch { }
                    }
                    // Keep ScrollRect scrolling even when hovering over clickable text.
                    try
                    {
                        UIScrollPassthrough sp = depsTr.GetComponent<UIScrollPassthrough>();
                        if (sp == null) sp = depsTr.gameObject.AddComponent<UIScrollPassthrough>();
                        sp.target = scrollRect;
                    }
                    catch { }
                    // Make clickable to filter by dependencies using EventTrigger (non-invasive)
                    EventTrigger et = depsTr.GetComponent<EventTrigger>();
                    if (et == null) et = depsTr.gameObject.AddComponent<EventTrigger>();
                    var pointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                    pointerClickEntry.callback.AddListener((data) => {
                        if (GallerySortManager.GetDepsCount(file) > 0)
                            ApplyDependenciesFilter(file);
                    });
                    et.triggers.Clear();
                    et.triggers.Add(pointerClickEntry);
                    // Add tooltip
                    try { AddTooltip(depsTr.gameObject, "gallery.tooltip.dependencies", "Dependencies"); } catch { }
                }

                Transform missingTr = btnGO.transform.Find("ListRow/Details/Missing");
                if (missingTr != null)
                {
                    Text t = missingTr.GetComponent<Text>();
                    if (t != null)
                    {
                        int missing = GallerySortManager.GetMissingDepsCount(file);
                        string v = missing.ToString().PadLeft(3);
                        t.text = "M: " + v + "  |  ";
                        t.raycastTarget = true;

                        // Hover-highlight only the value; Set() resets color on recycle.
                        try
                        {
                            var hv = missingTr.GetComponent<UIRichValueHover>();
                            if (hv == null) hv = missingTr.gameObject.AddComponent<UIRichValueHover>();
                            hv.target = t;
                            hv.useConditionalColoring = true;
                            hv.zeroValueColor = Color.green;  // Green when no missing
                            hv.nonZeroValueColor = Color.red; // Red when missing deps exist
                            hv.Set("M: ", v, "  |  ");
                        }
                        catch { }
                    }
                    // Keep ScrollRect scrolling even when hovering over clickable text.
                    try
                    {
                        UIScrollPassthrough sp = missingTr.GetComponent<UIScrollPassthrough>();
                        if (sp == null) sp = missingTr.gameObject.AddComponent<UIScrollPassthrough>();
                        sp.target = scrollRect;
                    }
                    catch { }
                    // Make clickable to filter by missing dependencies using EventTrigger (non-invasive)
                    EventTrigger et = missingTr.GetComponent<EventTrigger>();
                    if (et == null) et = missingTr.gameObject.AddComponent<EventTrigger>();
                    var pointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                    pointerClickEntry.callback.AddListener((data) => {
                        try
                        {
                            int missingCount = GallerySortManager.GetMissingDepsCount(file);
                            if (missingCount > 0)
                                ApplyMissingDependenciesFilter(file);
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError($"[VPB] Missing click handler error: {ex}");
                        }
                    });
                    et.triggers.Clear();
                    et.triggers.Add(pointerClickEntry);
                    // Add tooltip
                    try { AddTooltip(missingTr.gameObject, "gallery.tooltip.missing_dependencies", "Missing Dependencies"); } catch { }
                }

                Transform catTr = btnGO.transform.Find("ListRow/Details/Category");
                if (catTr != null)
                {
                    Text t = catTr.GetComponent<Text>();
                    if (t != null)
                    {
                        string catLabel = "";
                        bool isMissing = file is VirtualFileEntry || file is MissingPackageListEntry;

                        if (isMissing)
                        {
                            catLabel = "Missing";
                            t.text = "Missing";
                            // Color missing label red
                            try { t.color = new Color(0.8f, 0.2f, 0.2f, 1f); } catch { }
                        }
                        else
                        {
                            try
                            {
                                if (IsFilterActive)
                                {
                                    if (file is PackageListEntry ple && ple.Package != null)
                                        catLabel = GetBestCategoryLabelForPackage(ple.Package);
                                    else if (file is VarFileEntry vfe3 && vfe3.Package != null)
                                        catLabel = GetBestCategoryLabelForPackage(vfe3.Package);
                                }
                            }
                            catch { catLabel = ""; }

                            t.text = string.IsNullOrEmpty(catLabel) ? "" : ("Cat: " + catLabel);
                            // Reset to default color
                            try { t.color = Color.white; } catch { }
                        }
                    }
                }

                Transform dependentsTr = btnGO.transform.Find("ListRow/Details/Dependents");
                if (dependentsTr != null)
                {
                    Text t = dependentsTr.GetComponent<Text>();
                    if (t != null)
                    {
                        int dependents = GallerySortManager.GetDependentsCount(file);
                        string v = dependents.ToString().PadLeft(3);
                        t.text = "Dn: " + v;
                        t.raycastTarget = true;

                        // Hover-highlight only the value; Set() resets color on recycle.
                        try
                        {
                            var hv = dependentsTr.GetComponent<UIRichValueHover>();
                            if (hv == null) hv = dependentsTr.gameObject.AddComponent<UIRichValueHover>();
                            hv.target = t;
                            hv.Set("Dn: ", v, "");
                        }
                        catch { }
                    }
                    // Keep ScrollRect scrolling even when hovering over clickable text.
                    try
                    {
                        UIScrollPassthrough sp = dependentsTr.GetComponent<UIScrollPassthrough>();
                        if (sp == null) sp = dependentsTr.gameObject.AddComponent<UIScrollPassthrough>();
                        sp.target = scrollRect;
                    }
                    catch { }
                    // Make clickable to filter by dependents using EventTrigger (non-invasive)
                    EventTrigger et = dependentsTr.GetComponent<EventTrigger>();
                    if (et == null) et = dependentsTr.gameObject.AddComponent<EventTrigger>();
                    var pointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                    pointerClickEntry.callback.AddListener((data) => {
                        if (GallerySortManager.GetDependentsCount(file) > 0)
                            ApplyDependentsFilter(file);
                    });
                    et.triggers.Clear();
                    et.triggers.Add(pointerClickEntry);
                    // Add tooltip
                    try { AddTooltip(dependentsTr.gameObject, "gallery.tooltip.dependents", "Dependents"); } catch { }
                }

                Transform sizeTr = btnGO.transform.Find("ListRow/Details/Size");
                if (sizeTr != null)
                {
                    Text t = sizeTr.GetComponent<Text>();
                    if (t != null) t.text = FormatBytesForList(file.Size);
                }

                Transform dateTr = btnGO.transform.Find("ListRow/Details/Date");
                if (dateTr != null)
                {
                    Text t = dateTr.GetComponent<Text>();
                    if (t != null)
                    {
                        try { t.text = file.LastWriteTime.ToString("yyyy-MM-dd"); }
                        catch { t.text = ""; }
                    }
                }

            }

            // Init RatingHandler in both list and grid mode
            {
                Text starText = null;
                Transform starBtnTr = btnGO.transform.Find("Rating/Star");
                if (starBtnTr != null) starText = starBtnTr.GetComponentInChildren<Text>();

                if (starText == null)
                {
                    Transform oldStar = btnGO.transform.Find("ListRow/Details/Rating/Star");
                    if (oldStar != null) starText = oldStar.GetComponentInChildren<Text>();
                }

                Transform selector2Tr = btnGO.transform.Find("RatingSelector");
                RatingHandler rh = btnGO.GetComponent<RatingHandler>();
                if (rh != null && selector2Tr != null && starText != null)
                {
                    rh.Init(file, starText, selector2Tr.gameObject);
                }
            }
            
            // Draggable
            UIDraggableItem draggable = btnGO.GetComponent<UIDraggableItem>();
            if (draggable != null) draggable.FileEntry = file;
        }

        private string GetBestCategoryLabelForPackage(VarPackage pkg)
        {
            if (pkg == null) return "";
            try
            {
                if (packageCategoryLabelCache != null && packageCategoryLabelCache.TryGetValue(pkg.Uid, out string cached))
                    return cached ?? "";
            }
            catch { }

            string result = "Unknown";
            try
            {
                if (categories == null || categories.Count == 0) return result;

                List<string> names; List<long> ticks; List<long> sizes;
                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) return result;

                int best = 0;
                int bestCount = 0;
                int ties = 0;

                for (int ci = 0; ci < categories.Count; ci++)
                {
                    var cat = categories[ci];
                    if (string.IsNullOrEmpty(cat.name) || string.IsNullOrEmpty(cat.extension)) continue;

                    string[] exts = cat.extension.Split('|');
                    if (exts == null || exts.Length == 0) continue;

                    int hits = 0;
                    for (int i = 0; i < names.Count; i++)
                    {
                        string ip = names[i];
                        if (string.IsNullOrEmpty(ip)) continue;

                        // ext match
                        string entryExt = System.IO.Path.GetExtension(ip);
                        if (string.IsNullOrEmpty(entryExt) || entryExt.Length < 2) continue;
                        string ext = entryExt.Substring(1);
                        bool extMatch = false;
                        for (int e = 0; e < exts.Length; e++)
                        {
                            var ce = exts[e];
                            if (!string.IsNullOrEmpty(ce) && string.Equals(ext, ce.Trim(), StringComparison.OrdinalIgnoreCase))
                            { extMatch = true; break; }
                        }
                        if (!extMatch) continue;

                        // path match
                        bool pathOk = false;
                        if (cat.paths != null && cat.paths.Count > 0)
                        {
                            for (int p = 0; p < cat.paths.Count; p++)
                            {
                                var pref = cat.paths[p];
                                if (!string.IsNullOrEmpty(pref) && GalleryInternalPathStartsWithPrefix(ip, pref))
                                { pathOk = true; break; }
                            }
                        }
                        else if (!string.IsNullOrEmpty(cat.path))
                        {
                            if (GalleryInternalPathStartsWithPrefix(ip, cat.path)) pathOk = true;
                        }
                        else
                        {
                            pathOk = true;
                        }
                        if (!pathOk) continue;

                        hits++;
                        if (hits >= 8) break; // cap work per category
                    }

                    if (hits > bestCount)
                    {
                        bestCount = hits;
                        best = ci;
                        ties = 0;
                    }
                    else if (hits > 0 && hits == bestCount)
                    {
                        ties++;
                    }
                }

                if (bestCount > 0)
                {
                    if (ties > 0) result = "Mixed";
                    else result = categories[best].name;
                }
            }
            catch { }

            try
            {
                if (packageCategoryLabelCache != null)
                {
                    if (packageCategoryLabelCache.Count > 8000) packageCategoryLabelCache.Clear();
                    packageCategoryLabelCache[pkg.Uid] = result;
                }
            }
            catch { }

            return result;
        }

        /// <summary>Update filter indicator UI when filter state changes.</summary>
        public void UpdateFilterIndicator()
        {
            // Top filter label removed; keep filter exit control in the footer only.
            HideFilterIndicator();
        }

        private GameObject GetOrCreateFilterIndicator()
        {
            if (scrollRect == null) return null;

            Transform parent = scrollRect.transform.parent;
            if (parent == null) return null;

            Transform existingIndicator = parent.Find("FilterIndicator");
            if (existingIndicator != null) return existingIndicator.gameObject;

            // Create new filter indicator
            GameObject indicatorGO = new GameObject("FilterIndicator");
            indicatorGO.transform.SetParent(parent, false);

            RectTransform rt = indicatorGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(0, 28);
            rt.anchoredPosition = new Vector2(0, -2);

            Image bgImg = indicatorGO.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.35f, 0.2f, 0.9f);

            HorizontalLayoutGroup hgroup = indicatorGO.AddComponent<HorizontalLayoutGroup>();
            hgroup.padding = new RectOffset(8, 8, 4, 4);
            hgroup.spacing = 8f;
            hgroup.childForceExpandWidth = false;
            hgroup.childForceExpandHeight = false;

            // Description text
            GameObject descGO = new GameObject("Description");
            descGO.transform.SetParent(indicatorGO.transform, false);
            Text descText = descGO.AddComponent<Text>();
            descText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            descText.fontSize = 12;
            descText.fontStyle = FontStyle.Bold;
            descText.color = Color.white;
            descText.text = "Filtered";
            descText.raycastTarget = false;
            descGO.AddComponent<LayoutElement>().preferredWidth = 200;

            // Clear button
            GameObject clearBtnGO = new GameObject("ClearButton");
            clearBtnGO.transform.SetParent(indicatorGO.transform, false);
            Image clearBtnImg = clearBtnGO.AddComponent<Image>();
            clearBtnImg.color = new Color(0.8f, 0.2f, 0.2f, 0.8f);
            Button clearBtn = clearBtnGO.AddComponent<Button>();
            clearBtn.targetGraphic = clearBtnImg;

            Text clearBtnText = new GameObject("Text").AddComponent<Text>();
            clearBtnText.transform.SetParent(clearBtnGO.transform, false);
            clearBtnText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            clearBtnText.fontSize = 12;
            clearBtnText.fontStyle = FontStyle.Bold;
            clearBtnText.color = Color.white;
            clearBtnText.text = "Clear Filter";
            clearBtnText.alignment = TextAnchor.MiddleCenter;
            clearBtnText.raycastTarget = false;

            RectTransform clearBtnRT = clearBtnGO.GetComponent<RectTransform>();
            clearBtnRT.sizeDelta = new Vector2(90, 20);

            clearBtnGO.AddComponent<LayoutElement>().preferredWidth = 90;

            return indicatorGO;
        }

        private void HideFilterIndicator()
        {
            if (scrollRect == null) return;
            Transform parent = scrollRect.transform.parent;
            if (parent == null) return;

            Transform indicator = parent.Find("FilterIndicator");
            if (indicator != null) indicator.gameObject.SetActive(false);
        }

    }
}
