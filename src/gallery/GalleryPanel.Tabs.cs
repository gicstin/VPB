using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void UpdateTabs()
        {
            if (titleText != null)
            {
                if (IsHubMode) titleText.text = "HUB: " + currentHubCategory;
                else titleText.text = currentCategoryTitle;
            }

            if (IsHubMode)
            {
                UpdateHubLayout();
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
                        title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
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
                    leftRT.offsetMin = new Vector2(10, 5); // Add gap at bottom
                    
                    RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(0, 0);
                    subRT.anchorMax = new Vector2(0, 0.5f);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); // Add gap at top for controls
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, 110); // Gap for clear button (moved up)

                    // Populate Top (Category / Hub Category)
                    UpdateTabs(leftActiveContent.Value, leftTabContainerGO, leftActiveTabButtons, true);
                    
                    // Populate Bottom (Tags / Hub Tags)
                    ContentType subType = leftActiveContent == ContentType.Hub ? ContentType.HubTags : ContentType.Tags;
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
                    leftRT.offsetMin = new Vector2(10, 68); // Restore default

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
                        title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
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
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, 5); // Add gap at bottom
                    
                    RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(1, 0);
                    subRT.anchorMax = new Vector2(1, 0.5f);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); // Add gap at top for controls
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, 110); // Gap for clear button (moved up)

                    // Populate Top (Category / Hub Category)
                    UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
                    
                    // Populate Bottom (Tags / Hub Tags)
                    ContentType subType = rightActiveContent == ContentType.Hub ? ContentType.HubTags : ContentType.Tags;
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
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, 68); // Restore default

                    UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
                }
            }
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
                leftRT.offsetMin = new Vector2(10, 5); 
                
                RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                subRT.anchorMin = new Vector2(0, 0);
                subRT.anchorMax = new Vector2(0, 0.5f);
                subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); 
                subRT.offsetMin = new Vector2(subRT.offsetMin.x, 110); 

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
                rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, 5); 

                RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                subRT.anchorMin = new Vector2(1, 0);
                subRT.anchorMax = new Vector2(1, 0.7f);
                subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55);
                subRT.offsetMin = new Vector2(subRT.offsetMin.x, 110); 

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
                        UpdateTabs();
                    }, trackedButtons);
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
                    }, trackedButtons);
                }
            }
            else if (contentType == ContentType.Status)
            {
                var statusList = new List<string> { "Favorite", "Hidden", "Loaded", "Unloaded", "Autoinstall" };
                
                var sortState = GetSortState("Status");
                if (sortState.Type == SortType.Name)
                {
                    if (sortState.Direction == SortDirection.Ascending) statusList.Sort();
                    else statusList.Sort((a, b) => b.CompareTo(a));
                }
                
                Color statusColor = new Color(0.3f, 0.5f, 0.7f, 1f); // Blue-ish

                foreach (var status in statusList)
                {
                    bool isActive = false;
                    if (status == "Favorite") isActive = filterFavorite;
                    
                    Color btnColor = isActive ? statusColor : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, status, btnColor, isActive, () => {
                        if (status == "Favorite")
                        {
                            filterFavorite = !filterFavorite;
                            UpdateTabs();
                            currentPage = 0;
                            RefreshFiles();
                        }
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

        private void CreateTabButton(Transform parent, string label, Color color, bool isActive, UnityAction onClick, List<GameObject> targetList)
        {
            GameObject groupGO = GetTabButton(parent);
            if (groupGO == null)
            {
                // Container
                groupGO = new GameObject("TabGroup");
                groupGO.transform.SetParent(parent, false);
                LayoutElement groupLE = groupGO.AddComponent<LayoutElement>();
                groupLE.minWidth = 140; 
                groupLE.preferredWidth = 170;
                groupLE.minHeight = 35;
                groupLE.preferredHeight = 35;
                
                HorizontalLayoutGroup groupHLG = groupGO.AddComponent<HorizontalLayoutGroup>();
                groupHLG.childControlWidth = true; // Changed to true since we only have one button now
                groupHLG.childControlHeight = true;
                groupHLG.childForceExpandWidth = true; // Changed to true
                groupHLG.spacing = 0;

                // Tab Button
                GameObject btnGO = UI.CreateUIButton(groupGO, 170, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                btnGO.name = "Button";
                AddHoverDelegate(btnGO);
            }
            
            groupGO.name = "TabGroup_" + label;
            
            // Reconfigure
            Transform btnTr = groupGO.transform.GetChild(0);
            GameObject btnGO_Reuse = btnTr.gameObject;
            Button btnComp = btnGO_Reuse.GetComponent<Button>();
            btnComp.onClick.RemoveAllListeners();
            if (onClick != null) btnComp.onClick.AddListener(onClick);
            
            Image img = btnGO_Reuse.GetComponent<Image>();
            img.color = color;
            
            Text txt = btnGO_Reuse.GetComponentInChildren<Text>();
            txt.text = label;
            txt.fontSize = 18;
            txt.color = Color.white;
            
            if (targetList != null) targetList.Add(groupGO);
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
            GameObject btnGO = new GameObject("FileButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = Color.gray;

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();

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

            // Favorite Button
            try
            {
                GameObject favBtnGO = UI.CreateUIButton(btnGO, 40, 40, "★", 24, 0, 0, AnchorPresets.topRight, null);
                favBtnGO.name = "Button_Fav"; // Name it to find it later
                favBtnGO.SetActive(false); 
                RectTransform favRT = favBtnGO.GetComponent<RectTransform>();
                favRT.anchoredPosition = new Vector2(-5, -5);
                
                Image favBg = favBtnGO.GetComponent<Image>();
                favBg.color = new Color(0, 0, 0, 0.4f);

                favBtnGO.AddComponent<FavoriteHandler>();
                
                // Hover Logic
                var hoverDelegate = btnGO.AddComponent<UIHoverDelegate>();
                hoverDelegate.OnHoverChange = (isHovering) => {
                     var handler = favBtnGO.GetComponent<FavoriteHandler>();
                     if (handler != null) handler.SetHover(isHovering);
                };
                hoverDelegate.OnHoverChange += (enter) => {
                    if (enter) hoverCount++;
                    else hoverCount--;
                };

                if (favBtnGO != null) AddHoverDelegate(favBtnGO);
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Error creating favorite icon template: " + ex.Message);
            }
            
            SetLayerRecursive(btnGO, 5);
            return btnGO;
        }

        private void BindFileButton(GameObject btnGO, FileEntry file)
        {
            btnGO.name = "FileButton_" + file.Name;
            
            // Image
            Image img = btnGO.GetComponent<Image>();
            if (selectedPath == file.Path) img.color = Color.yellow;
            else img.color = Color.gray;
            if (!fileButtonImages.ContainsKey(file.Path)) fileButtonImages.Add(file.Path, img);

            // Button
            Button btn = btnGO.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnFileClick(file));

            // Thumbnail
            Transform thumbTr = btnGO.transform.Find("Thumbnail");
            if (thumbTr != null) thumbTr.gameObject.SetActive(true);
            RawImage thumbImg = thumbTr.GetComponent<RawImage>();
            thumbImg.texture = null; // Clear prev
            thumbImg.color = new Color(0, 0, 0, 0.5f);
            LoadThumbnail(file, thumbImg);

            // Hide NavText
            Transform navTextTr = btnGO.transform.Find("NavText");
            if (navTextTr != null) navTextTr.gameObject.SetActive(false);
            
            // Hover Path
            UIHoverReveal hover = btnGO.GetComponent<UIHoverReveal>();
            if (hover != null) hover.file = file;

            // Label
            Transform labelTr = btnGO.transform.Find("Card/Label");
            Text labelText = labelTr.GetComponent<Text>();
            
            string nameStr = file.Name;
            if (file is VarFileEntry vfe && vfe.Package != null)
            {
                string ext = System.IO.Path.GetExtension(nameStr);
                // Creator.PackageName.Version.var (.json)
                labelText.text = $"{vfe.Package.Uid}.var ({ext})";
            }
            else
            {
                labelText.text = nameStr;
            }
            
            // Draggable
            UIDraggableItem draggable = btnGO.GetComponent<UIDraggableItem>();
            draggable.FileEntry = file;
            
            // Favorite
            Transform favTr = btnGO.transform.Find("Button_Fav");
            if (favTr != null)
            {
                GameObject favBtnGO = favTr.gameObject;
                FavoriteHandler favHandler = favBtnGO.GetComponent<FavoriteHandler>();
                Text favText = favBtnGO.GetComponentInChildren<Text>();
                
                favHandler.Init(file, favText);
                
                Button favBtn = favBtnGO.GetComponent<Button>();
                favBtn.onClick.RemoveAllListeners();
                favBtn.onClick.AddListener(() => {
                    try { file.SetFavorite(!file.IsFavorite()); }
                    catch (Exception ex) { Debug.LogError("[VPB] Error toggling favorite: " + ex.Message); }
                });
            }
        }
    }
}
