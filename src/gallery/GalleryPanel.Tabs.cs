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
                        title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        splitView = true;
                    }
                }
                else if (leftActiveContent == ContentType.Hub)
                {
                    splitView = true;
                }
                else if (leftActiveContent == ContentType.Status)
                {
                    if (currentStatus == "Favorites" || currentStatus == "Size")
                    {
                        splitView = true;
                    }
                }

                if (splitView && (leftActiveContent == ContentType.Category || leftActiveContent == ContentType.Hub || leftActiveContent == ContentType.Status) && leftSubTabScrollGO != null)
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
                    else if (leftActiveContent == ContentType.Status)
                    {
                         if (currentStatus == "Size") subType = ContentType.Size;
                         else subType = ContentType.Ratings;
                    }
                    else if (leftActiveContent == ContentType.Category)
                    {
                        string title = titleText != null ? titleText.text : "";
                        if (title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            subType = ContentType.SceneSource;
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
                        title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        splitView = true;
                    }
                }
                else if (rightActiveContent == ContentType.Hub)
                {
                    splitView = true;
                }
                else if (rightActiveContent == ContentType.Status)
                {
                    if (currentStatus == "Favorites" || currentStatus == "Size")
                    {
                        splitView = true;
                    }
                }

                if (splitView && (rightActiveContent == ContentType.Category || rightActiveContent == ContentType.Hub || rightActiveContent == ContentType.Status) && rightSubTabScrollGO != null)
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
                    else if (rightActiveContent == ContentType.Status)
                    {
                         if (currentStatus == "Size") subType = ContentType.Size;
                         else subType = ContentType.Ratings;
                    }
                    else if (rightActiveContent == ContentType.Category)
                    {
                        string title = titleText != null ? titleText.text : "";
                        if (title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            subType = ContentType.SceneSource;
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
                        currentPage = 0;
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
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs(); 
                    }, trackedButtons, () => {
                        currentCreator = "";
                        categoriesCached = false;
                        tagsCached = false;
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs();
                    });
                }
            }
            else if (contentType == ContentType.Status)
            {
                var statusList = new List<string> { "Hidden", "Loaded", "Unloaded", "Autoinstall", "Favorites", "Size" };
                
                var sortState = GetSortState("Status");
                if (sortState.Type == SortType.Name)
                {
                    if (sortState.Direction == SortDirection.Ascending) statusList.Sort();
                    else statusList.Sort((a, b) => b.CompareTo(a));
                }
                
                Color statusColor = new Color(0.3f, 0.5f, 0.7f, 1f); // Blue-ish

                foreach (var status in statusList)
                {
                    bool isActive = (currentStatus == status);
                    
                    Color btnColor = isActive ? statusColor : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, status, btnColor, isActive, () => {
                        if (currentStatus == status) currentStatus = "";
                        else currentStatus = status;
                        
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons, () => {
                        currentStatus = "";
                        currentPage = 0;
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
                        
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
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
                        
                        currentPage = 0;
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
                        
                        currentPage = 0;
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
            else if (contentType == ContentType.ActiveItems)
            {
                var categories = new List<string> { "All", "Atoms", "Clothing", "Hair", "Plugins", "Pose", "Appearance", "Audio" };
                
                foreach (var cat in categories)
                {
                    bool isActive = (currentActiveItemCategory == cat) || (string.IsNullOrEmpty(currentActiveItemCategory) && cat == "All");
                    Color btnColor = isActive ? ColorActiveItems : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, cat, btnColor, isActive, () => {
                        if (cat == "All") currentActiveItemCategory = "";
                        else currentActiveItemCategory = cat;
                        
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
                }
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

                        string[] options = new string[] { "Real Clothing", "Presets", "Items", "Male", "Female", "Decals" };
                        for (int gi = 0; gi < options.Length; gi++)
                        {
                            string opt = options[gi];
                            ClothingSubfilter flag = 0;
                            if (opt == "Real Clothing") flag = ClothingSubfilter.RealClothing;
                            else if (opt == "Presets") flag = ClothingSubfilter.Presets;
                            else if (opt == "Items") flag = ClothingSubfilter.Items;
                            else if (opt == "Male") flag = ClothingSubfilter.Male;
                            else if (opt == "Female") flag = ClothingSubfilter.Female;
                            else if (opt == "Decals") flag = ClothingSubfilter.Decals;

                            bool isActive = (flag != 0) && ((clothingSubfilter & flag) != 0);
                            Color btnColor = isActive ? active : inactive;

                            int cnt = 0;
                            if (opt == "Real Clothing") cnt = clothingSubfilterFacetCountReal;
                            else if (opt == "Presets") cnt = clothingSubfilterFacetCountPresets;
                            else if (opt == "Items") cnt = clothingSubfilterFacetCountItems;
                            else if (opt == "Male") cnt = clothingSubfilterFacetCountMale;
                            else if (opt == "Female") cnt = clothingSubfilterFacetCountFemale;
                            else if (opt == "Decals") cnt = clothingSubfilterFacetCountDecals;

                            string label = opt + " (" + cnt + ")";

                            CreateTabButton(container.transform, label, btnColor, isActive, () => {
                                if (flag != 0)
                                {
                                    if ((clothingSubfilter & flag) != 0) clothingSubfilter &= ~flag;
                                    else clothingSubfilter |= flag;
                                }
                                tagsCached = false;
                                currentPage = 0;
                                RefreshFiles();
                                UpdateTabs();
                            }, trackedButtons);
                        }
                    }

                    tagsToShow.AddRange(TagFilter.ClothingTypeTags);
                    tagsToShow.AddRange(TagFilter.ClothingRegionTags);
                    tagsToShow.AddRange(TagFilter.ClothingOtherTags);
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
                        
                        currentPage = 0;
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

        private GameObject CreateNewFileButtonGO()
        {
            if (layoutMode == GalleryLayoutMode.VerticalCard)
                return CreateNewVerticalCardGO();

            GameObject btnGO = new GameObject("FileButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = Color.gray;

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
            
            // Drag Logic
            UIDraggableItem draggable = btnGO.AddComponent<UIDraggableItem>();
            draggable.ThumbnailImage = thumbImg;
            draggable.Panel = this;

            SetLayerRecursive(btnGO, 5);
            return btnGO;
        }

        private void BindFileButton(GameObject btnGO, FileEntry file)
        {
            btnGO.name = "FileButton_" + file.Name;
            
            // Image - Cached lookup or fast fetch
            Image img = btnGO.GetComponent<Image>();
            bool isSelected = (!string.IsNullOrEmpty(file.Path) && selectedFilePaths.Contains(file.Path));
            
            if (layoutMode == GalleryLayoutMode.VerticalCard)
                img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            else
                img.color = Color.gray;

            // Handle Selection Outline
            // Optimize: Get components only once if possible, but pooling makes this tricky without a custom component wrapper
            // We could add a "FileButtonView" component to cache these references on creation
            
            UIHoverBorder hoverBorder = btnGO.GetComponent<UIHoverBorder>();
            Outline outline = btnGO.GetComponent<Outline>();
            
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
            
            // Update mapping
            fileButtonImages[file.Path] = img;

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

            // Thumbnail
            Transform thumbTr = btnGO.transform.Find("Thumbnail");
            if (thumbTr == null) thumbTr = btnGO.transform.Find("ThumbContainer/Thumbnail");

            if (thumbTr != null)
            {
                if (!thumbTr.gameObject.activeSelf) thumbTr.gameObject.SetActive(true);
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
            
            // Draggable
            UIDraggableItem draggable = btnGO.GetComponent<UIDraggableItem>();
            if (draggable != null) draggable.FileEntry = file;
            
            if (layoutMode == GalleryLayoutMode.VerticalCard)
            {
                try {
                    BindVerticalCard(btnGO, file);
                } catch (Exception ex) {
                    Debug.LogError($"[VPB] Error binding vertical card for {file.Name}: {ex}");
                }
            }
        }

        private void BindVerticalCard(GameObject btnGO, FileEntry file)
        {
            // Name
            Transform nameTr = btnGO.transform.Find("Info/Name");
            if (nameTr != null)
            {
                Text t = nameTr.GetComponent<Text>();
                string name = file.Name;
                
                // For .var files, include a hint of the package if it's a content file
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    // If the name is generic (like default.json), use the package UID
                    string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
                    string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(name);
                    
                    if (nameNoExt.Equals("default", StringComparison.OrdinalIgnoreCase) || 
                        nameNoExt.Equals("preset", StringComparison.OrdinalIgnoreCase))
                    {
                        name = vfe.Package.Uid + " (" + name + ")";
                    }
                    else
                    {
                        // Use filename but ensure it's indicative
                        name = nameNoExt;
                    }
                }
                
                // Rough estimate for 2 rows (around 55-60 chars for 20pt bold)
                if (name.Length > 60)
                {
                    name = name.Substring(0, 57) + "...";
                }
                t.text = name;
            }

            // Date & Size
            Transform dateSizeTr = btnGO.transform.Find("Info/DateSize");
            if (dateSizeTr != null)
            {
                Text t = dateSizeTr.GetComponent<Text>();
                string ageStr = GetAgeString(file.LastWriteTime);
                
                long size = file.Size;
                if (file is VarFileEntry vfe && vfe.Package != null)
                    size = vfe.Package.Size;

                string sizeStr = FormatBytes(size);
                t.text = $"{ageStr} old  |  {sizeStr}";
            }

            // Stars
            RatingHandler ratingHandler = btnGO.GetComponent<RatingHandler>();
            if (ratingHandler != null)
            {
                Transform ratingTr = btnGO.transform.Find("Rating");
                Transform starBtnTr = ratingTr?.Find("Star");
                Transform selectorTr = ratingTr?.Find("RatingSelector");
                if (starBtnTr != null && selectorTr != null)
                {
                    Text s = starBtnTr.GetComponentInChildren<Text>(); // The "★" text
                    ratingHandler.Init(file, s, selectorTr.gameObject);
                }
            }
        }

        private string GetAgeString(DateTime lastWriteTime)
        {
            TimeSpan age = DateTime.Now - lastWriteTime;
            if (age.TotalDays < 1) return "Today";
            if (age.TotalDays < 7) return $"{(int)age.TotalDays}d";
            if (age.TotalDays < 30) return $"{(int)(age.TotalDays / 7)}w";
            if (age.TotalDays < 365) return $"{(int)(age.TotalDays / 30)}m";
            return $"{(int)(age.TotalDays / 365)}y";
        }

        private string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        private string GetTagsString(FileEntry file)
        {
            List<string> tags = new List<string>();
            string pathLower = file.Path.ToLowerInvariant();
            
            // Check common tags based on path
            foreach (var tag in TagFilter.AllClothingTags)
                if (pathLower.Contains(tag)) tags.Add(tag);
            foreach (var tag in TagFilter.AllHairTags)
                if (pathLower.Contains(tag)) tags.Add(tag);

            if (tags.Count == 0) return "No tags";
            return string.Join(", ", tags.Take(3).ToArray()) + (tags.Count > 3 ? "..." : "");
        }

        private GameObject CreateNewVerticalCardGO()
        {
            GameObject btnGO = new GameObject("FileButton_Card_Template");
            btnGO.transform.SetParent(contentGO.transform, false);

            Image img = btnGO.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f); // Translucent backdrop matching pane

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };

            // Vertical Layout for Card Content
            VerticalLayoutGroup mainVLG = btnGO.AddComponent<VerticalLayoutGroup>();
            mainVLG.padding = new RectOffset(5, 5, 5, 0); // No bottom padding to tighten
            mainVLG.spacing = 2; // Much tighter spacing
            mainVLG.childControlHeight = true;
            mainVLG.childControlWidth = true;
            mainVLG.childForceExpandHeight = false;
            mainVLG.childForceExpandWidth = true;
            mainVLG.childAlignment = TextAnchor.UpperCenter;

            // Thumbnail (1x1 Aspect Ratio)
            GameObject thumbContainer = new GameObject("ThumbContainer");
            thumbContainer.transform.SetParent(btnGO.transform, false);
            RectTransform thumbContRT = thumbContainer.AddComponent<RectTransform>();
            thumbContRT.pivot = new Vector2(0.5f, 1f); // Pivot at Top
            
            // Forces the LayoutGroup to respect a 1:1 ratio based on the dynamic width
            AspectRatioLayoutElement thumbALE = thumbContainer.AddComponent<AspectRatioLayoutElement>();
            thumbALE.aspectRatio = 1f;

            // Masking
            Image maskImg = thumbContainer.AddComponent<Image>();
            maskImg.color = new Color(0.15f, 0.15f, 0.15f, 1f); // Match backdrop
            maskImg.raycastTarget = false;
            thumbContainer.AddComponent<Mask>().showMaskGraphic = true;

            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(thumbContainer.transform, false);
            RawImage thumbImg = thumbGO.AddComponent<RawImage>();
            thumbImg.color = new Color(0, 0, 0, 0); // Transparent until loaded
            
            // Envelope parent so it fills the 1:1 square completely
            AspectRatioFitter thumbARF = thumbGO.AddComponent<AspectRatioFitter>();
            thumbARF.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            thumbARF.aspectRatio = 1f; // Updated when texture loads

            RectTransform thumbRT = thumbGO.GetComponent<RectTransform>();
            thumbRT.anchorMin = Vector2.zero;
            thumbRT.anchorMax = Vector2.one;
            thumbRT.sizeDelta = Vector2.zero;
            thumbRT.pivot = new Vector2(0.5f, 0.5f);

            // Info Container
            GameObject infoGO = new GameObject("Info");
            infoGO.transform.SetParent(btnGO.transform, false);
            RectTransform infoRT = infoGO.AddComponent<RectTransform>();
            infoRT.pivot = new Vector2(0.5f, 1f); // Pivot at Top

            VerticalLayoutGroup infoVLG = infoGO.AddComponent<VerticalLayoutGroup>();
            infoVLG.spacing = 0; // Tightest spacing
            infoVLG.childControlHeight = true;
            infoVLG.childControlWidth = true;
            infoVLG.childForceExpandHeight = false;
            infoVLG.childForceExpandWidth = true;
            infoVLG.padding = new RectOffset(5, 5, 0, 0);

            LayoutElement infoLE = infoGO.AddComponent<LayoutElement>();
            infoLE.minHeight = 85; 
            infoLE.preferredHeight = 85;
            infoLE.flexibleHeight = 0;

            // Name
            Text nameText = CreateCardText(infoGO, "Name", 20, FontStyle.Bold);
            nameText.alignment = TextAnchor.UpperLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.verticalOverflow = VerticalWrapMode.Truncate;
            nameText.lineSpacing = 0.9f;
            nameText.GetComponent<LayoutElement>().preferredHeight = 42; // Fixed height for exactly 2 rows
            nameText.GetComponent<LayoutElement>().minHeight = 42;

            // Date & Size
            Text dateSizeText = CreateCardText(infoGO, "DateSize", 16, FontStyle.Normal);
            dateSizeText.alignment = TextAnchor.UpperLeft;
            dateSizeText.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            LayoutElement dateLE = dateSizeText.GetComponent<LayoutElement>();
            if (dateLE != null)
            {
                dateLE.preferredHeight = 22;
                dateLE.minHeight = 22;
                dateLE.flexibleHeight = 0;
            }

            VerticalCardInfoSizer infoSizer = btnGO.AddComponent<VerticalCardInfoSizer>();
            infoSizer.infoLE = infoLE;
            infoSizer.nameLE = nameText.GetComponent<LayoutElement>();
            infoSizer.nameText = nameText;
            infoSizer.dateLE = dateLE;
            infoSizer.dateGO = dateSizeText.gameObject;
            infoSizer.ratingHeight = 45f;
            infoSizer.maxInfoHeight = 85f;

            // Spacer (push rating to the bottom of the fixed-height card)
            GameObject spacerGO = new GameObject("Spacer");
            spacerGO.transform.SetParent(btnGO.transform, false);
            LayoutElement spacerLE = spacerGO.AddComponent<LayoutElement>();
            spacerLE.flexibleHeight = 1;
            spacerLE.flexibleWidth = 1;

            // Rating System (Bottom)
            GameObject ratingGO = new GameObject("Rating");
            ratingGO.transform.SetParent(btnGO.transform, false);
            LayoutElement ratingLE = ratingGO.AddComponent<LayoutElement>();
            ratingLE.minHeight = 45;
            ratingLE.preferredHeight = 45;
            ratingLE.flexibleHeight = 0;

            HorizontalLayoutGroup ratingHLG = ratingGO.AddComponent<HorizontalLayoutGroup>();
            ratingHLG.childAlignment = TextAnchor.MiddleLeft;
            ratingHLG.childControlWidth = true;
            ratingHLG.childControlHeight = true;
            ratingHLG.childForceExpandWidth = true;
            ratingHLG.childForceExpandHeight = false;
            ratingHLG.padding = new RectOffset(5, 5, 0, 0);
            ratingHLG.spacing = 0;

            GameObject starBtnGO = UI.CreateUIButton(ratingGO, 32, 45, "★", 24, 0, 0, AnchorPresets.centre, null);
            starBtnGO.name = "Star";
            starBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
            LayoutElement starLE = starBtnGO.AddComponent<LayoutElement>();
            starLE.minWidth = 0;
            starLE.flexibleWidth = 1;
            starLE.preferredHeight = 45;
            starLE.minHeight = 45;
            starLE.flexibleHeight = 0;
            Text starIconText = starBtnGO.GetComponentInChildren<Text>();

            GameObject selectorGO = new GameObject("RatingSelector");
            selectorGO.transform.SetParent(ratingGO.transform, false);
            LayoutElement selectorLE = selectorGO.AddComponent<LayoutElement>();
            selectorLE.minWidth = 0;
            selectorLE.flexibleWidth = 6;
            selectorLE.flexibleHeight = 0;
            CanvasGroup selectorCG = selectorGO.AddComponent<CanvasGroup>();
            selectorCG.alpha = 0f;
            selectorCG.interactable = false;
            selectorCG.blocksRaycasts = false;

            Image selectorBg = selectorGO.AddComponent<Image>();
            selectorBg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
            selectorBg.raycastTarget = false;

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
                GameObject optBtnGO = UI.CreateUIButton(selectorGO, 45, 45, label, 16, 0, 0, AnchorPresets.centre, () => ratingHandler.SetRating(ratingValue));
                optBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
                optBtnGO.GetComponent<Image>().color = RatingHandler.RatingColors[i];
                if (i == 0) optBtnGO.GetComponentInChildren<Text>().color = Color.red;
                else optBtnGO.GetComponentInChildren<Text>().color = Color.black;
                AddHoverDelegate(optBtnGO);

                LayoutElement optLE = optBtnGO.AddComponent<LayoutElement>();
                optLE.minWidth = 0;
                optLE.flexibleWidth = 1;
                optLE.preferredHeight = 45;
                optLE.minHeight = 45;
                optLE.flexibleHeight = 0;
            }

            Button starBtn = starBtnGO.GetComponent<Button>();
            starBtn.onClick.AddListener(() => ratingHandler.ToggleSelector());
            AddHoverDelegate(starBtnGO);

            // Drag Logic
            UIDraggableItem draggable = btnGO.AddComponent<UIDraggableItem>();
            draggable.ThumbnailImage = thumbImg;
            draggable.Panel = this;

            // We still want the Hover Reveal card for compatibility with BindFileButton
            GameObject cardGO = new GameObject("Card");
            cardGO.transform.SetParent(btnGO.transform, false);
            cardGO.SetActive(false);
            
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(cardGO.transform, false);
            labelGO.AddComponent<Text>(); // Binding expects this

            UIHoverReveal hover = btnGO.AddComponent<UIHoverReveal>();
            hover.card = cardGO;
            hover.panel = this;

            SetLayerRecursive(btnGO, 5);
            return btnGO;
        }

        private Text CreateCardText(GameObject parent, string name, int fontSize, FontStyle style)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 1f); // Pivot at Top for correct stacking

            Text t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 4;
            return t;
        }
    }
}
