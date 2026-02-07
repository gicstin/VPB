using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel
    {

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

        private static int GetDepsCountForList(FileEntry file)
        {
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null && vfe.Package.RecursivePackageDependencies != null)
                {
                    return vfe.Package.RecursivePackageDependencies.Count;
                }
            }
            catch { }
            return -1;
        }
        private float CurrentBottomOffset
        {
            get
            {
                float bottom = 60;
                if (isFixedLocally && actionsPanel != null && actionsPanel.actionsPaneGO != null && actionsPanel.actionsPaneGO.activeSelf)
                {
                    bottom = 410;
                }
                return bottom;
            }
        }

        private float SideTabBottomMargin => isFixedLocally ? CurrentBottomOffset + 8f : 5f;
        private float SideTabDefaultBottomOffset => isFixedLocally ? CurrentBottomOffset + 8f : 68f;

        private void UpdateTabs()
        {
            if (titleText != null)
            {
                if (IsHubMode) titleText.text = "HUB: " + currentHubCategory;
                else titleText.text = currentCategoryTitle;
            }

            UpdateFooterContextActions();
            UpdateSideContextActions();

            if (IsHubMode)
            {
                UpdateHubLayout();
                UpdateSideButtonsVisibility();
                return;
            }

            if (leftActiveContent.HasValue) 
            {
                UpdateSortButtonText(leftSortBtnText, GetSortState(leftActiveContent.Value.ToString()));
                
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
                    
                    if (leftSubSortBtn != null) 
                    {
                        leftSubSortBtn.SetActive(true);
                        UpdateSortButtonText(leftSubSortBtnText, GetSortState("Tags"));
                    }
                    if (leftSubSearchInput != null) 
                    {
                        leftSubSearchInput.gameObject.SetActive(true);
                        if (leftSubSearchInput.text != tagFilter) leftSubSearchInput.text = tagFilter;
                    }
                    
                    RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                    leftRT.anchorMin = new Vector2(0, 0.5f);
                    leftRT.anchorMax = new Vector2(0, 1);
                    leftRT.offsetMin = new Vector2(10, SideTabBottomMargin); // Add gap at bottom
                    
                    RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(0, 0);
                    subRT.anchorMax = new Vector2(0, 0.5f);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); // Add gap at top for controls
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin + 105); // Gap for clear button (moved up)

                    // Populate Top (Category / Hub Category / Status)
                    UpdateTabs(leftActiveContent.Value, leftTabContainerGO, leftActiveTabButtons, true);
                    
                    // Populate Bottom (Tags / Hub Tags / Ratings / Size / SceneSource)
                    ContentType subType = ContentType.Tags;
                    if (leftActiveContent == ContentType.Hub) subType = ContentType.HubTags;
                    else if (leftActiveContent == ContentType.Category)
                    {
                        string title = titleText != null ? titleText.text : "";
                        if (title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            subType = ContentType.SceneSource;
                        }
                        else if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            subType = ContentType.AppearanceSource;
                        }
                    }
                    
                    UpdateTabs(subType, leftSubTabContainerGO, leftSubActiveTabButtons, true);
                }
                else
                {
                    // Full Layout
                    if (leftSubTabScrollGO != null) leftSubTabScrollGO.SetActive(false);
                    if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                    if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
                    if (leftSubClearBtn != null) leftSubClearBtn.SetActive(false);

                    RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                    leftRT.anchorMin = new Vector2(0, 0);
                    leftRT.anchorMax = new Vector2(0, 1);
                    leftRT.offsetMin = new Vector2(10, SideTabDefaultBottomOffset); // Restore default

                    UpdateTabs(leftActiveContent.Value, leftTabContainerGO, leftActiveTabButtons, true);
                }
            }
            if (rightActiveContent.HasValue) 
            {
                UpdateSortButtonText(rightSortBtnText, GetSortState(rightActiveContent.Value.ToString()));
                
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
                    
                    if (rightSubSortBtn != null) 
                    {
                        rightSubSortBtn.SetActive(true);
                        UpdateSortButtonText(rightSubSortBtnText, GetSortState("Tags"));
                    }
                    if (rightSubSearchInput != null) 
                    {
                        rightSubSearchInput.gameObject.SetActive(true);
                        if (rightSubSearchInput.text != tagFilter) rightSubSearchInput.text = tagFilter;
                        
                        // Reset anchor to 0.5f for non-Hub split
                        RectTransform rt = rightSubSearchInput.GetComponent<RectTransform>();
                        rt.anchorMin = new Vector2(1, 0.5f);
                        rt.anchorMax = new Vector2(1, 0.5f);
                    }

                    if (rightSubSortBtn != null)
                    {
                        RectTransform rt = rightSubSortBtn.GetComponent<RectTransform>();
                        rt.anchorMin = new Vector2(1, 0.5f);
                        rt.anchorMax = new Vector2(1, 0.5f);
                    }
                    
                    RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                    rightRT.anchorMin = new Vector2(1, 0.5f);
                    rightRT.anchorMax = new Vector2(1, 1);
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, SideTabBottomMargin); // Add gap at bottom
                    
                    RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(1, 0);
                    subRT.anchorMax = new Vector2(1, 0.5f);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); // Add gap at top for controls
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin + 105); // Gap for clear button (moved up)

                    // Populate Top (Category / Hub Category / Status)
                    UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
                    
                    // Populate Bottom (Tags / Hub Tags / Ratings / Size / SceneSource)
                    ContentType subType = ContentType.Tags;
                    if (rightActiveContent == ContentType.Hub) subType = ContentType.HubTags;
                    else if (rightActiveContent == ContentType.Category)
                    {
                        string title = titleText != null ? titleText.text : "";
                        if (title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            subType = ContentType.SceneSource;
                        }
                        else if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            subType = ContentType.AppearanceSource;
                        }
                    }

                    UpdateTabs(subType, rightSubTabContainerGO, rightSubActiveTabButtons, false);
                }
                else
                {
                    // Full Layout
                    if (rightSubTabScrollGO != null) rightSubTabScrollGO.SetActive(false);
                    if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                    if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
                    if (rightSubClearBtn != null) rightSubClearBtn.SetActive(false);

                    RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                    rightRT.anchorMin = new Vector2(1, 0);
                    rightRT.anchorMax = new Vector2(1, 1);
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, SideTabDefaultBottomOffset); // Restore default

                    UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
                }
            }

            UpdateSideButtonsVisibility();
        }

        private void UpdateHubLayout()
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
                    if (leftSearchInput.placeholder is Text ph) ph.text = "Categories...";
                }

                // Left Search Bottom (Tags)
                if (leftSubSearchInput != null)
                {
                    leftSubSearchInput.gameObject.SetActive(true);
                    if (leftSubSearchInput.text != tagFilter) leftSubSearchInput.text = tagFilter;
                    if (leftSubSearchInput.placeholder is Text ph) ph.text = "Search Tags...";
                }

                RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                leftRT.anchorMin = new Vector2(0, 0.5f);
                leftRT.anchorMax = new Vector2(0, 1);
                leftRT.offsetMin = new Vector2(10, SideTabBottomMargin); 
                
                RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                subRT.anchorMin = new Vector2(0, 0);
                subRT.anchorMax = new Vector2(0, 0.5f);
                subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); 
                subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin + 105); 

                UpdateTabs(ContentType.Hub, leftTabContainerGO, leftActiveTabButtons, true);
                UpdateTabs(ContentType.HubTags, leftSubTabContainerGO, leftSubActiveTabButtons, true);
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
                    if (rightSubSearchInput.placeholder is Text ph) ph.text = "Search Creators...";
                    
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
                rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, SideTabBottomMargin); 

                RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                subRT.anchorMin = new Vector2(1, 0);
                subRT.anchorMax = new Vector2(1, 0.7f);
                subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55);
                subRT.offsetMin = new Vector2(subRT.offsetMin.x, SideTabBottomMargin + 105); 

                UpdateTabs(ContentType.HubPayTypes, rightTabContainerGO, rightActiveTabButtons, false);
                UpdateTabs(ContentType.HubCreators, rightSubTabContainerGO, rightSubActiveTabButtons, false);
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
                    
                    if (count == 0 && !isActive) continue;

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
                        if (titleText != null) titleText.text = "All Categories";
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
                var sceneFilters = new List<string> { "All Scenes", "Addon Scenes", "Custom Scenes" };
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

                            CreateTabButton(container.transform, "Single (" + posePeopleFacetCountSingle + ")", isSingleActive ? active : inactive, isSingleActive, () => {
                                posePeopleFilter = (posePeopleFilter == PosePeopleFilter.Single) ? PosePeopleFilter.All : PosePeopleFilter.Single;
                                        RefreshFiles();
                                UpdateTabs();
                            }, trackedButtons);

                            CreateTabButton(container.transform, "Dual (" + posePeopleFacetCountDual + ")", isDualActive ? active : inactive, isDualActive, () => {
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
                        if (clearBtnText != null) clearBtnText.text = "Clear Selected (" + activeTags.Count + ")";
                    }
                    else
                    {
                        clearBtn.SetActive(false);
                    }
                }
            }
            
            SetLayerRecursive(container, 5);
        }

        private void CreateTabButton(Transform parent, string label, Color color, bool isActive, UnityAction onClick, List<GameObject> targetList, UnityAction onRightClick = null)
        {
            GameObject btnGO = GetTabButton(parent);
            if (btnGO == null)
            {
                btnGO = UI.CreateUIButton(parent.gameObject, 170, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                AddHoverDelegate(btnGO);
            }
            
            // Check if it was a group previously (cleanup from previous implementation if pooling mixed types)
            if (btnGO.name.StartsWith("TabGroup"))
            {
                // This shouldn't happen if we clean up properly, but for robustness:
                // Destroy(btnGO);
                // btnGO = UI.CreateUIButton(parent, 170, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                // AddHoverDelegate(btnGO);
                
                // Better: Reuse the Button inside the group if possible, or just re-create.
                // Since we are reverting, we assume simple button structure.
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
            
            Text txt = btnGO.GetComponentInChildren<Text>();
            txt.text = label;
            txt.fontSize = 18;
            txt.color = Color.white;
            
            // Ensure LayoutElement
            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le == null) le = btnGO.AddComponent<LayoutElement>();
            le.minWidth = 140;
            le.preferredWidth = 170;
            le.minHeight = 35;
            le.preferredHeight = 35;
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
            textAreaRT.offsetMin = new Vector2(10, 0);
            textAreaRT.offsetMax = new Vector2(-45, 0); // Room for X button
            
            // Placeholder
            GameObject placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);
            Text placeholderText = placeholder.AddComponent<Text>();
            placeholderText.text = "Search...";
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
            listRowRT.offsetMax = new Vector2(-260, 0);

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
            listNameText.fontSize = 24;
            listNameText.fontStyle = FontStyle.Bold;
            listNameText.color = Color.white;
            listNameText.alignment = TextAnchor.LowerLeft;
            listNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            listNameText.verticalOverflow = VerticalWrapMode.Truncate;
            listNameText.raycastTarget = false;
            LayoutElement listNameLE = listNameGO.AddComponent<LayoutElement>();
            listNameLE.flexibleWidth = 1;
            listNameLE.minHeight = 32;

            // Details Row
            GameObject detailsRowGO = new GameObject("Details");
            detailsRowGO.transform.SetParent(listRowGO.transform, false);
            HorizontalLayoutGroup detailsHLG = detailsRowGO.AddComponent<HorizontalLayoutGroup>();
            detailsHLG.childAlignment = TextAnchor.MiddleLeft;
            detailsHLG.childControlHeight = true;
            detailsHLG.childControlWidth = true;
            detailsHLG.childForceExpandHeight = false;
            detailsHLG.childForceExpandWidth = false;
            detailsHLG.spacing = 20f;
            detailsHLG.padding = new RectOffset(0, 0, 0, 0);
            LayoutElement detailsLE = detailsRowGO.AddComponent<LayoutElement>();
            detailsLE.flexibleWidth = 1;
            detailsLE.minHeight = 24;

            // Helper to create detail text
            GameObject CreateDetailText(string name, string placeholder, float width)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(detailsRowGO.transform, false);
                Text t = go.AddComponent<Text>();
                t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                t.fontSize = 18;
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

            CreateDetailText("Size", "Size", 100);
            CreateDetailText("Date", "Date", 120);
            CreateDetailText("Deps", "Deps", 100);

            // Rating (Positioned at bottom right, independent of Details row)
            GameObject ratingGO = new GameObject("Rating");
            ratingGO.transform.SetParent(btnGO.transform, false);
            RectTransform ratingRT = ratingGO.AddComponent<RectTransform>();
            ratingRT.anchorMin = new Vector2(1, 0); // Bottom Right
            ratingRT.anchorMax = new Vector2(1, 0);
            ratingRT.pivot = new Vector2(1, 0);
            ratingRT.sizeDelta = new Vector2(40, 40);
            ratingRT.anchoredPosition = new Vector2(-2, 2); // Moved to corner

            GameObject starBtnGO = UI.CreateUIButton(ratingGO, 32, 32, "★", 20, 0, 0, AnchorPresets.middleCenter, null);
            starBtnGO.name = "Star";
            starBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
            Text starIconText = starBtnGO.GetComponentInChildren<Text>();

            GameObject selectorGO = new GameObject("RatingSelector");
            selectorGO.transform.SetParent(btnGO.transform, false);
            RectTransform selectorRT = selectorGO.AddComponent<RectTransform>();
            // Left of Star (Star is 40px wide at -2, so -42 left edge. Gap 5px -> -47)
            selectorRT.anchorMin = new Vector2(1, 0);
            selectorRT.anchorMax = new Vector2(1, 0);
            selectorRT.pivot = new Vector2(1, 0);
            selectorRT.sizeDelta = new Vector2(250, 40);
            selectorRT.anchoredPosition = new Vector2(-47, 2);

            CanvasGroup selectorCG = selectorGO.AddComponent<CanvasGroup>();
            selectorCG.alpha = 0f;
            selectorCG.interactable = false;
            selectorCG.blocksRaycasts = false;

            Image selectorBg = selectorGO.AddComponent<Image>();
            selectorBg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);

            HorizontalLayoutGroup selectorHLG = selectorGO.AddComponent<HorizontalLayoutGroup>();
            selectorHLG.childAlignment = TextAnchor.MiddleLeft;
            selectorHLG.childControlHeight = true;
            selectorHLG.childControlWidth = true;
            selectorHLG.childForceExpandWidth = true;
            selectorHLG.childForceExpandHeight = false;
            selectorHLG.spacing = 1;

            RatingHandler ratingHandler = btnGO.AddComponent<RatingHandler>();
            for (int i = 0; i <= 5; i++)
            {
                int ratingValue = i;
                string label = i == 0 ? "X" : i.ToString();
                GameObject optBtnGO = UI.CreateUIButton(selectorGO, 40, 40, label, 16, 0, 0, AnchorPresets.middleCenter, () => ratingHandler.SetRating(ratingValue));
                optBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
                optBtnGO.GetComponent<Image>().color = RatingHandler.RatingColors[i];
                if (i == 0) optBtnGO.GetComponentInChildren<Text>().color = Color.red;
                else optBtnGO.GetComponentInChildren<Text>().color = Color.black;

                LayoutElement optLE = optBtnGO.AddComponent<LayoutElement>();
                optLE.minWidth = 0;
                optLE.flexibleWidth = 1;
                optLE.preferredHeight = 40;
                optLE.minHeight = 40;
                optLE.flexibleHeight = 0;
            }

            Button starBtn = starBtnGO.GetComponent<Button>();
            starBtn.onClick.AddListener(() => ratingHandler.ToggleSelector());
            
            // Drag Logic
            UIDraggableItem draggable = btnGO.AddComponent<UIDraggableItem>();
            draggable.ThumbnailImage = thumbImg;
            draggable.Panel = this;

            SetLayerRecursive(btnGO, 5);
            return btnGO;
        }

        public void UpdateFileButtonVisuals(GameObject btnGO, FileEntry file)
        {
            if (btnGO == null || file == null) return;
            
            // Image
            Image img = btnGO.GetComponent<Image>();
            bool isSelected = (!string.IsNullOrEmpty(file.Path) && selectedFilePaths.Contains(file.Path));
            
            // Semi-translucent for List mode to match gallery style
            if (layoutMode == GalleryLayoutMode.List)
            {
                if (isSelected) img.color = new Color(0.6f, 0.5f, 0.0f, 0.7f);
                else img.color = new Color(0f, 0f, 0f, 0.4f);
            }
            else
            {
                if (isSelected) img.color = new Color(0.7f, 0.7f, 0.2f, 1f);
                else img.color = Color.gray;
            }

            // Handle Selection Outline
            Outline outline = btnGO.GetComponent<Outline>();
            UIHoverBorder hoverBorder = btnGO.GetComponent<UIHoverBorder>();
            
            if (outline != null)
            {
                if (outline.enabled != isSelected) outline.enabled = isSelected;
                if (isSelected)
                {
                    outline.effectColor = Color.yellow;
                    outline.effectDistance = new Vector2(4f, -4f);
                }
                else
                {
                    outline.effectDistance = new Vector2(2f, -2f);
                }

                if (hoverBorder != null) 
                {
                    hoverBorder.isSelected = isSelected;
                    hoverBorder.borderSize = isSelected ? 4f : 2f;
                }
            }
        }

        public void BindFileButton(GameObject btnGO, FileEntry file)
        {
            btnGO.name = "FileButton_" + file.Name;
            
            // Update mapping
            Image img = btnGO.GetComponent<Image>();
            fileButtonImages[file.Path] = img;

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
                btn.onClick.AddListener(() => OnFileClick(file));
            }

            // Right Click
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
                        // Use full width
                        listRowRT.offsetMax = new Vector2(0, 0);
                    }
                }
            }

            Transform selectorTr = btnGO.transform.Find("RatingSelector");
            if (selectorTr != null)
            {
                selectorTr.gameObject.SetActive(isListMode);
                if (!isListMode)
                {
                    RatingHandler rh = btnGO.GetComponent<RatingHandler>();
                    if (rh != null) rh.CloseSelector();
                }
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
                thumbImg.texture = null; // Clear prev
                thumbImg.color = new Color(0, 0, 0, 0); // Transparent until loaded
                LoadThumbnail(file, thumbImg);
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
                    string nameStr = file.Name;
                    if (file is VarFileEntry vfe && vfe.Package != null)
                    {
                        // Optimization: Use cached UID/Extension if available or fast path
                        string ext = System.IO.Path.GetExtension(nameStr);
                        labelText.text = $"{vfe.Package.Uid}.var ({ext})";
                    }
                    else
                    {
                        labelText.text = nameStr;
                    }
                }
            }

            // Rating Visibility (Only for List Mode)
            Transform ratingTr = btnGO.transform.Find("Rating");
            if (ratingTr != null)
            {
                ratingTr.gameObject.SetActive(isListMode);
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
                        if (file is VarFileEntry vfe && vfe.Package != null)
                        {
                            string ext = System.IO.Path.GetExtension(file.Name);
                            t.text = $"{vfe.Package.Uid}.var ({ext})";
                        }
                        else
                        {
                            t.text = file.Name;
                        }
                    }
                }

                Transform depsTr = btnGO.transform.Find("ListRow/Details/Deps");
                if (depsTr != null)
                {
                    Text t = depsTr.GetComponent<Text>();
                    if (t != null)
                    {
                        int deps = GetDepsCountForList(file);
                        t.text = deps >= 0 ? ("Deps: " + deps.ToString()) : "";
                    }
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

                Text starText = null;
                Transform starBtnTr = btnGO.transform.Find("Rating/Star");
                if (starBtnTr != null) starText = starBtnTr.GetComponentInChildren<Text>();
                
                // Fallback for old layout if needed (though we rebuild buttons)
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

    }
}
