using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private void UpdateSideButtonsVisibility()
        {
            if (VPBConfig.Instance == null) return;
            string mode = VPBConfig.Instance.ShowSideButtons;
            if (leftSideContainer != null) 
                leftSideContainer.SetActive(mode == "Both" || mode == "Left");
            if (rightSideContainer != null) 
                rightSideContainer.SetActive(mode == "Both" || mode == "Right");
        }

        private void AddHoverDelegate(GameObject go)
        {
            var del = go.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) => {
                if (enter) hoverCount++;
                else hoverCount--;
            };
            del.OnPointerEnterEvent += (d) => {
                currentPointerData = d;
            };
        }

        private void AddRightClickDelegate(GameObject go, Action action)
        {
            var del = go.AddComponent<UIRightClickDelegate>();
            del.OnRightClick = action;
        }

        void OnDestroy()
        {
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.ConfigChanged -= UpdateSideButtonPositions;
                VPBConfig.Instance.ConfigChanged -= UpdateSideButtonsVisibility;
            }

            if (canvas != null)
            {
                if (SuperController.singleton != null)
                {
                    SuperController.singleton.RemoveCanvas(canvas);
                }
                Destroy(canvas.gameObject);
            }
            // Remove from manager if needed
            if (Gallery.singleton != null)
            {
                Gallery.singleton.RemovePanel(this);
            }
        }

        public void Init()
        {
            if (canvas != null) return;

            // Subscribe to config changes
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.ConfigChanged += UpdateSideButtonPositions;
                VPBConfig.Instance.ConfigChanged += UpdateSideButtonsVisibility;
            }

            // ... standard Init code follows ...
            // string nameSuffix = isUndocked ? "_Undocked" : "";
            GameObject canvasGO = new GameObject("VPB_GalleryCanvas");
            canvasGO.layer = 5; // UI layer
            canvas = canvasGO.AddComponent<Canvas>();
            RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(1200, 800);
            
            // Note: In VaM VR, standard GraphicRaycaster often conflicts or is ignored.
            // We rely on BoxCollider for the main panel background hit detection.
            // Adding GraphicRaycaster but disabling it by default unless needed for non-VR mouse interaction?
            // Actually, let's keep it simple: relying on BoxCollider with offset seems to be the intended path for VaM UI panels.
            // But user said offset didn't work. The key might be that the collider MUST be there for the laser to "stop"
            // but the "dimming" means it thinks it's penetrating.
            // The resize handles work because they have small colliders or none?
            // Resize handles in this code do NOT have colliders added explicitly! They just use Image + UIHoverBorder/UIHoverColor.
            // So if resize handles work WITHOUT collider, we should aim for that.
            
            // Standard GraphicRaycaster is needed for UI elements without colliders.
            GraphicRaycaster gr = canvasGO.AddComponent<GraphicRaycaster>();
            gr.ignoreReversedGraphics = true;
            
            if (SuperController.singleton != null)
                SuperController.singleton.AddCanvas(canvas);

            if (Application.isPlaying)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Camera.main;
                canvas.sortingOrder = -10000;
                // Position will be set in Show()
                canvas.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                canvasGO.layer = 5; // UI layer
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4;

            // Background
            backgroundBoxGO = UI.AddChildGOImage(canvasGO, new Color(0.1f, 0.1f, 0.1f, 0.9f), AnchorPresets.centre, 1200, 800, Vector2.zero);
            backgroundCanvasGroup = backgroundBoxGO.AddComponent<CanvasGroup>();
            backgroundCanvasGroup.ignoreParentGroups = true; // Ensure we control our own opacity separately if needed
            
            // Add UIHoverColor (This handles hover/drag color changes AND sets raycast target properly)
            UIHoverColor bgHover = backgroundBoxGO.AddComponent<UIHoverColor>();
            bgHover.targetImage = backgroundBoxGO.GetComponent<Image>();
            bgHover.normalColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            bgHover.hoverColor = new Color(0.1f, 0.1f, 0.1f, 0.9f); // Same color for now, but ensures interaction
            
            // AddHoverDelegate
            AddHoverDelegate(backgroundBoxGO);
            
            UIDraggable dragger = backgroundBoxGO.AddComponent<UIDraggable>();
            dragger.target = canvasGO.transform;

            settingsPanel = new SettingsPanel(backgroundBoxGO);

            // Register Panel
            if (Gallery.singleton != null)
            {
                Gallery.singleton.AddPanel(this);
            }

            // Title Bar
            GameObject titleBarGO = new GameObject("TitleBar");
            titleBarGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform titleBarRT = titleBarGO.AddComponent<RectTransform>();
            titleBarRT.anchorMin = new Vector2(0, 1);
            titleBarRT.anchorMax = new Vector2(1, 1);
            titleBarRT.pivot = new Vector2(0.5f, 1);
            titleBarRT.anchoredPosition = new Vector2(0, 0);
            titleBarRT.sizeDelta = new Vector2(0, 70);

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(titleBarGO.transform, false);
            titleText = titleGO.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 28;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleLeft;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 0.5f);
            titleRT.anchorMax = new Vector2(0, 0.5f);
            titleRT.pivot = new Vector2(0, 0.5f);
            titleRT.anchoredPosition = new Vector2(15, 0);
            titleRT.sizeDelta = new Vector2(300, 40);

            GameObject fpsGO = new GameObject("FPS");
            fpsGO.transform.SetParent(titleBarGO.transform, false);
            fpsText = fpsGO.AddComponent<Text>();
            fpsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            fpsText.fontSize = 20;
            fpsText.color = Color.white;
            fpsText.alignment = TextAnchor.MiddleRight;
            RectTransform fpsRT = fpsGO.GetComponent<RectTransform>();
            fpsRT.anchorMin = new Vector2(1, 0.5f);
            fpsRT.anchorMax = new Vector2(1, 0.5f);
            fpsRT.pivot = new Vector2(1, 0.5f);
            fpsRT.anchoredPosition = new Vector2(-70, 0);
            fpsRT.sizeDelta = new Vector2(100, 40);

            titleSearchInput = CreateSearchInput(titleBarGO, 300f, (val) => {
                SetNameFilter(val);
            });
            RectTransform titleSearchRT = titleSearchInput.GetComponent<RectTransform>();
            titleSearchRT.anchorMin = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchorMax = new Vector2(0.5f, 0.5f);
            titleSearchRT.pivot = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchoredPosition = new Vector2(0, 0);
            titleSearchRT.sizeDelta = new Vector2(300, 40);

            // File Sort Button
            GameObject fileSortBtn = UI.CreateUIButton(titleBarGO, 40, 40, "Az↑", 16, 0, 0, AnchorPresets.middleCenter, null);
            fileSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            fileSortBtn.GetComponentInChildren<Text>().color = Color.white;
            RectTransform fileSortRT = fileSortBtn.GetComponent<RectTransform>();
            fileSortRT.anchorMin = new Vector2(0.5f, 0.5f);
            fileSortRT.anchorMax = new Vector2(0.5f, 0.5f);
            fileSortRT.pivot = new Vector2(0.5f, 0.5f);
            fileSortRT.anchoredPosition = new Vector2(175, 0); // To the right of search
            
            Text fileSortText = fileSortBtn.GetComponentInChildren<Text>();
            
            Button fileSortButton = fileSortBtn.GetComponent<Button>();
            fileSortButton.onClick.RemoveAllListeners();
            fileSortButton.onClick.AddListener(() => CycleSort("Files", fileSortText));
            
            AddRightClickDelegate(fileSortBtn, () => ToggleSortDirection("Files", fileSortText));
            
            // Init File Sort State
            UpdateSortButtonText(fileSortText, GetSortState("Files"));

            // Tab Area - Create for all panels so undocked can clone/filter
            if (true)
            {
                float tabAreaWidth = 220f;
                
                // 1. Right Tab Area
                rightTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchRight, tabAreaWidth, 0, Vector2.zero);
                RectTransform rightTabRT = rightTabScrollGO.GetComponent<RectTransform>();
                rightTabRT.anchorMin = new Vector2(1, 0);
                rightTabRT.anchorMax = new Vector2(1, 1);
                rightTabRT.offsetMin = new Vector2(-tabAreaWidth - 10, 68); 
                rightTabRT.offsetMax = new Vector2(-10, -90); 
                
                rightTabContainerGO = rightTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                rightTabContainerGO.GetComponent<VerticalLayoutGroup>().spacing = 2;
                rightTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);

                // 1b. Right Sub Tab Area (For Tags split view)
                rightSubTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchRight, tabAreaWidth, 0, Vector2.zero);
                RectTransform rightSubTabRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                rightSubTabRT.anchorMin = new Vector2(1, 0);
                rightSubTabRT.anchorMax = new Vector2(1, 0.5f); // Bottom half default
                rightSubTabRT.offsetMin = new Vector2(-tabAreaWidth - 10, 68);
                rightSubTabRT.offsetMax = new Vector2(-10, -45);
                
                rightSubTabContainerGO = rightSubTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                rightSubTabContainerGO.GetComponent<VerticalLayoutGroup>().spacing = 2;
                rightSubTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);
                rightSubTabScrollGO.SetActive(false); // Hidden by default

                // Right Sub Sort Button
                rightSubSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topRight, null);
                rightSubSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                rightSubSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform rsSubRT = rightSubSortBtn.GetComponent<RectTransform>();
                rsSubRT.anchorMin = new Vector2(1, 0.5f);
                rsSubRT.anchorMax = new Vector2(1, 0.5f);
                rsSubRT.pivot = new Vector2(1, 1);
                rsSubRT.anchoredPosition = new Vector2(-190, -10); // Below split line
                
                rightSubSortBtnText = rightSubSortBtn.GetComponentInChildren<Text>();
                Button rightSubSortButton = rightSubSortBtn.GetComponent<Button>();
                rightSubSortButton.onClick.RemoveAllListeners();
                rightSubSortButton.onClick.AddListener(() => {
                    CycleSort("Tags", rightSubSortBtnText);
                });
                AddRightClickDelegate(rightSubSortBtn, () => {
                    ToggleSortDirection("Tags", rightSubSortBtnText);
                });

                // Right Sub Search
                rightSubSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    tagFilter = val;
                    UpdateTabs();
                });
                RectTransform rSubSearchRT = rightSubSearchInput.GetComponent<RectTransform>();
                rSubSearchRT.anchorMin = new Vector2(1, 0.5f);
                rSubSearchRT.anchorMax = new Vector2(1, 0.5f);
                rSubSearchRT.pivot = new Vector2(1, 1);
                rSubSearchRT.anchoredPosition = new Vector2(-10, -10);
                
                // Right Sub Clear Button
                rightSubClearBtn = UI.CreateUIButton(backgroundBoxGO, tabAreaWidth, 35, "Clear Selected", 14, 0, 0, AnchorPresets.bottomRight, () => {
                    activeTags.Clear();
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                });
                rightSubClearBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // Dark Red
                rightSubClearBtnText = rightSubClearBtn.GetComponentInChildren<Text>();
                rightSubClearBtnText.color = Color.white;
                
                RectTransform rSubClearRT = rightSubClearBtn.GetComponent<RectTransform>();
                rSubClearRT.anchorMin = new Vector2(1, 0);
                rSubClearRT.anchorMax = new Vector2(1, 0);
                rSubClearRT.pivot = new Vector2(1, 0);
                rSubClearRT.anchoredPosition = new Vector2(-10, 68);
                rightSubClearBtn.SetActive(false);

                // Init Sub Sort Text
                UpdateSortButtonText(rightSubSortBtnText, GetSortState("Tags"));
                
                rightSubSortBtn.SetActive(false);
                rightSubSearchInput.gameObject.SetActive(false);

                // Right Sort Button
                rightSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topRight, null);
                rightSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                rightSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform rsRT = rightSortBtn.GetComponent<RectTransform>();
                rsRT.anchorMin = new Vector2(1, 1);
                rsRT.anchorMax = new Vector2(1, 1);
                rsRT.pivot = new Vector2(1, 1);
                rsRT.anchoredPosition = new Vector2(-190, -55); // Left of Search
                
                rightSortBtnText = rightSortBtn.GetComponentInChildren<Text>();
                Button rightSortButton = rightSortBtn.GetComponent<Button>();
                rightSortButton.onClick.RemoveAllListeners();
                rightSortButton.onClick.AddListener(() => {
                    if (rightActiveContent.HasValue) CycleSort(rightActiveContent.Value.ToString(), rightSortBtnText);
                });
                AddRightClickDelegate(rightSortBtn, () => {
                    if (rightActiveContent.HasValue) ToggleSortDirection(rightActiveContent.Value.ToString(), rightSortBtnText);
                });

                rightSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    if (rightActiveContent == ContentType.Category) categoryFilter = val;
                    else if (rightActiveContent == ContentType.Creator) creatorFilter = val;
                    UpdateTabs();
                }, () => {
                    if (rightActiveContent == ContentType.Creator) {
                        currentCreator = "";
                        categoriesCached = false;
                        tagsCached = false;
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs();
                    }
                });
                RectTransform rSearchRT = rightSearchInput.GetComponent<RectTransform>();
                rSearchRT.anchorMin = new Vector2(1, 1);
                rSearchRT.anchorMax = new Vector2(1, 1);
                rSearchRT.pivot = new Vector2(1, 1);
                rSearchRT.anchoredPosition = new Vector2(-10, -55);

                // 2. Left Tab Area
                leftTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchLeft, tabAreaWidth, 0, Vector2.zero);
                RectTransform leftTabRT = leftTabScrollGO.GetComponent<RectTransform>();
                leftTabRT.anchorMin = new Vector2(0, 0);
                leftTabRT.anchorMax = new Vector2(0, 1);
                leftTabRT.offsetMin = new Vector2(10, 70);
                leftTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -90);
                
                leftTabContainerGO = leftTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                leftTabContainerGO.GetComponent<VerticalLayoutGroup>().spacing = 2;
                leftTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);
                leftTabScrollGO.SetActive(false); // Hidden by default

                // 2b. Left Sub Tab Area (For Tags split view)
                leftSubTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchLeft, tabAreaWidth, 0, Vector2.zero);
                RectTransform leftSubTabRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                leftSubTabRT.anchorMin = new Vector2(0, 0);
                leftSubTabRT.anchorMax = new Vector2(0, 0.5f); // Bottom half default
                leftSubTabRT.offsetMin = new Vector2(10, 68);
                leftSubTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -45);
                
                leftSubTabContainerGO = leftSubTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                leftSubTabContainerGO.GetComponent<VerticalLayoutGroup>().spacing = 2;
                leftSubTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);
                leftSubTabScrollGO.SetActive(false); // Hidden by default

                // Left Sub Sort Button
                leftSubSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topLeft, null);
                leftSubSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                leftSubSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform lsSubRT = leftSubSortBtn.GetComponent<RectTransform>();
                lsSubRT.anchorMin = new Vector2(0, 0.5f);
                lsSubRT.anchorMax = new Vector2(0, 0.5f);
                lsSubRT.pivot = new Vector2(0, 1);
                lsSubRT.anchoredPosition = new Vector2(10, -10); // Below split line
                
                leftSubSortBtnText = leftSubSortBtn.GetComponentInChildren<Text>();
                Button leftSubSortButton = leftSubSortBtn.GetComponent<Button>();
                leftSubSortButton.onClick.RemoveAllListeners();
                leftSubSortButton.onClick.AddListener(() => {
                    CycleSort("Tags", leftSubSortBtnText);
                });
                AddRightClickDelegate(leftSubSortBtn, () => {
                    ToggleSortDirection("Tags", leftSubSortBtnText);
                });

                // Left Sub Search
                leftSubSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    tagFilter = val;
                    UpdateTabs();
                });
                RectTransform lSubSearchRT = leftSubSearchInput.GetComponent<RectTransform>();
                lSubSearchRT.anchorMin = new Vector2(0, 0.5f);
                lSubSearchRT.anchorMax = new Vector2(0, 0.5f);
                lSubSearchRT.pivot = new Vector2(0, 1);
                lSubSearchRT.anchoredPosition = new Vector2(55, -10);

                // Left Sub Clear Button
                leftSubClearBtn = UI.CreateUIButton(backgroundBoxGO, tabAreaWidth, 35, "Clear Selected", 14, 0, 0, AnchorPresets.bottomLeft, () => {
                    activeTags.Clear();
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                });
                leftSubClearBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // Dark Red
                leftSubClearBtnText = leftSubClearBtn.GetComponentInChildren<Text>();
                leftSubClearBtnText.color = Color.white;
                
                RectTransform lSubClearRT = leftSubClearBtn.GetComponent<RectTransform>();
                lSubClearRT.anchorMin = new Vector2(0, 0);
                lSubClearRT.anchorMax = new Vector2(0, 0);
                lSubClearRT.pivot = new Vector2(0, 0);
                lSubClearRT.anchoredPosition = new Vector2(10, 68);
                leftSubClearBtn.SetActive(false);

                // Init Sub Sort Text
                UpdateSortButtonText(leftSubSortBtnText, GetSortState("Tags"));

                leftSubSortBtn.SetActive(false);
                leftSubSearchInput.gameObject.SetActive(false);

                // Left Sort Button
                leftSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topLeft, null);
                leftSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                leftSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform lsRT = leftSortBtn.GetComponent<RectTransform>();
                lsRT.anchorMin = new Vector2(0, 1);
                lsRT.anchorMax = new Vector2(0, 1);
                lsRT.pivot = new Vector2(0, 1);
                lsRT.anchoredPosition = new Vector2(10, -55);
                
                leftSortBtnText = leftSortBtn.GetComponentInChildren<Text>();
                Button leftSortButton = leftSortBtn.GetComponent<Button>();
                leftSortButton.onClick.RemoveAllListeners();
                leftSortButton.onClick.AddListener(() => {
                    if (leftActiveContent.HasValue) CycleSort(leftActiveContent.Value.ToString(), leftSortBtnText);
                });
                AddRightClickDelegate(leftSortBtn, () => {
                    if (leftActiveContent.HasValue) ToggleSortDirection(leftActiveContent.Value.ToString(), leftSortBtnText);
                });

                leftSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    if (leftActiveContent == ContentType.Category) categoryFilter = val;
                    else if (leftActiveContent == ContentType.Creator) creatorFilter = val;
                    UpdateTabs();
                }, () => {
                    if (leftActiveContent == ContentType.Creator) {
                        currentCreator = "";
                        categoriesCached = false;
                        tagsCached = false;
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs();
                    }
                });
                RectTransform lSearchRT = leftSearchInput.GetComponent<RectTransform>();
                lSearchRT.anchorMin = new Vector2(0, 1);
                lSearchRT.anchorMax = new Vector2(0, 1);
                lSearchRT.pivot = new Vector2(0, 1);
                lSearchRT.anchoredPosition = new Vector2(55, -55);

                // Right Button Container
                rightSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleRight, 130, 580, new Vector2(120, 0));
                sideButtonGroups.Add(rightSideContainer.AddComponent<CanvasGroup>());

                // Right Toggle Buttons
                int btnFontSize = 20;
                float btnWidth = 120;
                float btnHeight = 50;
                float spacing = 60f;
                float groupGap = 20f;
                float startY = 260f;

                // Settings (Topmost)
                GameObject rightSettingsBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Settings", btnFontSize, 0, startY, AnchorPresets.centre, () => {
                    ToggleSettings(true);
                });
                rightSettingsBtn.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.6f, 1f); // Blueish
                rightSideButtons.Add(rightSettingsBtn.GetComponent<RectTransform>());

                // Follow (Top)
                GameObject rightFollowBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Static", btnFontSize, 0, startY - spacing - groupGap, AnchorPresets.centre, ToggleFollowMode);
                rightFollowBtnImage = rightFollowBtn.GetComponent<Image>();
                rightFollowBtnText = rightFollowBtn.GetComponentInChildren<Text>();
                rightFollowBtnImage.color = Color.gray;
                rightSideButtons.Add(rightFollowBtn.GetComponent<RectTransform>());

                // Clone (Gray)
                GameObject rightCloneBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Clone", btnFontSize, 0, startY - spacing * 2 - groupGap, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, true);
                });
                rightCloneBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 1f);
                rightSideButtons.Add(rightCloneBtn.GetComponent<RectTransform>());

                // Category (Red)
                GameObject rightCatBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Category", btnFontSize, 0, startY - spacing * 3 - groupGap * 2, AnchorPresets.centre, () => ToggleRight(ContentType.Category));
                rightCategoryBtnImage = rightCatBtn.GetComponent<Image>();
                rightCategoryBtnImage.color = ColorCategory;
                rightCategoryBtnText = rightCatBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightCatBtn.GetComponent<RectTransform>());
                
                // Creator (Green)
                GameObject rightCreatorBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 4 - groupGap * 2, AnchorPresets.centre, () => ToggleRight(ContentType.Creator));
                rightCreatorBtnImage = rightCreatorBtn.GetComponent<Image>();
                rightCreatorBtnImage.color = ColorCreator;
                rightCreatorBtnText = rightCreatorBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightCreatorBtn.GetComponent<RectTransform>());

                // Status (Blue) - NEW
                GameObject rightStatusBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Status", btnFontSize, 0, startY - spacing * 5 - groupGap * 2, AnchorPresets.centre, () => ToggleRight(ContentType.Status));
                rightStatusBtn.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.7f, 1f);
                rightSideButtons.Add(rightStatusBtn.GetComponent<RectTransform>());

                // Replace Toggle (Right)
                GameObject rightReplaceBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 6 - groupGap * 3, AnchorPresets.centre, ToggleReplaceMode);
                rightReplaceBtnImage = rightReplaceBtn.GetComponent<Image>();
                rightReplaceBtnText = rightReplaceBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightReplaceBtn.GetComponent<RectTransform>());

                // Undo (Right)
                GameObject rightUndoBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 7 - groupGap * 4, AnchorPresets.centre, Undo);
                rightUndoBtn.GetComponent<Image>().color = new Color(0.6f, 0.4f, 0.2f, 1f); // Brown/Orange
                rightUndoBtn.GetComponentInChildren<Text>().color = Color.white;
                rightSideButtons.Add(rightUndoBtn.GetComponent<RectTransform>());

                // Left Button Container
                leftSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleLeft, 130, 580, new Vector2(-120, 0));
                sideButtonGroups.Add(leftSideContainer.AddComponent<CanvasGroup>());

                // Left Toggle Buttons
                // Settings (Topmost)
                GameObject leftSettingsBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Settings", btnFontSize, 0, startY, AnchorPresets.centre, () => {
                    ToggleSettings(false);
                });
                leftSettingsBtn.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.6f, 1f); // Blueish
                leftSideButtons.Add(leftSettingsBtn.GetComponent<RectTransform>());

                // Follow (Top)
                GameObject leftFollowBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Static", btnFontSize, 0, startY - spacing - groupGap, AnchorPresets.centre, ToggleFollowMode);
                leftFollowBtnImage = leftFollowBtn.GetComponent<Image>();
                leftFollowBtnText = leftFollowBtn.GetComponentInChildren<Text>();
                leftFollowBtnImage.color = Color.gray;
                leftSideButtons.Add(leftFollowBtn.GetComponent<RectTransform>());

                // Clone (Gray)
                GameObject leftCloneBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Clone", btnFontSize, 0, startY - spacing * 2 - groupGap, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, false);
                });
                leftCloneBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 1f);
                leftSideButtons.Add(leftCloneBtn.GetComponent<RectTransform>());

                // Category (Red)
                GameObject leftCatBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Category", btnFontSize, 0, startY - spacing * 3 - groupGap * 2, AnchorPresets.centre, () => ToggleLeft(ContentType.Category));
                leftCategoryBtnImage = leftCatBtn.GetComponent<Image>();
                leftCategoryBtnImage.color = ColorCategory;
                leftCategoryBtnText = leftCatBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftCatBtn.GetComponent<RectTransform>());
                
                // Creator (Green)
                GameObject leftCreatorBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 4 - groupGap * 2, AnchorPresets.centre, () => ToggleLeft(ContentType.Creator));
                leftCreatorBtnImage = leftCreatorBtn.GetComponent<Image>();
                leftCreatorBtnImage.color = ColorCreator;
                leftCreatorBtnText = leftCreatorBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftCreatorBtn.GetComponent<RectTransform>());

                // Status (Blue) - NEW
                GameObject leftStatusBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Status", btnFontSize, 0, startY - spacing * 5 - groupGap * 2, AnchorPresets.centre, () => ToggleLeft(ContentType.Status));
                leftStatusBtn.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.7f, 1f);
                leftSideButtons.Add(leftStatusBtn.GetComponent<RectTransform>());

                // Replace Toggle (Left)
                GameObject leftReplaceBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 6 - groupGap * 3, AnchorPresets.centre, ToggleReplaceMode);
                leftReplaceBtnImage = leftReplaceBtn.GetComponent<Image>();
                leftReplaceBtnText = leftReplaceBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftReplaceBtn.GetComponent<RectTransform>());

                // Undo (Left)
                GameObject leftUndoBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 7 - groupGap * 4, AnchorPresets.centre, Undo);
                leftUndoBtn.GetComponent<Image>().color = new Color(0.6f, 0.4f, 0.2f, 1f); // Brown/Orange
                leftUndoBtn.GetComponentInChildren<Text>().color = Color.white;
                leftSideButtons.Add(leftUndoBtn.GetComponent<RectTransform>());
            }

            // Main Content Area
            GameObject scrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            scrollRect = scrollGO.GetComponent<ScrollRect>();
            contentScrollRT = scrollGO.GetComponent<RectTransform>();
            contentScrollRT.offsetMin = new Vector2(20, 70);
            contentScrollRT.offsetMax = new Vector2(-230, -90);
            
            contentGO = scrollRect.content.gameObject;
            // Remove VerticalLayoutGroup added by CreateVScrollableContent since we want GridLayoutGroup
            var oldVlg = contentGO.GetComponent<VerticalLayoutGroup>();
            if (oldVlg != null) DestroyImmediate(oldVlg);

            GridLayoutGroup glg = contentGO.AddComponent<GridLayoutGroup>();
            glg.spacing = new Vector2(10, 10);
            glg.padding = new RectOffset(10, 10, 10, 10);
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.constraint = GridLayoutGroup.Constraint.Flexible;

            UIGridAdaptive adaptive = contentGO.AddComponent<UIGridAdaptive>();
            adaptive.grid = glg;
            adaptive.minSize = 180f;
            adaptive.maxSize = 250f;
            adaptive.spacing = 10f;

            ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Status Bar (Bottom Right, similar to hoverPathText)
            GameObject statusBarGO = new GameObject("StatusBar");
            statusBarGO.transform.SetParent(backgroundBoxGO.transform, false);
            statusBarText = statusBarGO.AddComponent<Text>();
            statusBarText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statusBarText.fontSize = 16; // Match hoverPathText size
            statusBarText.color = new Color(1f, 1f, 1f, 0.7f); // Match hoverPathText color
            statusBarText.alignment = TextAnchor.LowerRight;
            statusBarText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusBarText.verticalOverflow = VerticalWrapMode.Overflow;
            statusBarText.raycastTarget = false;
            RectTransform statusRT = statusBarGO.GetComponent<RectTransform>();
            statusRT.anchorMin = new Vector2(0, 0); 
            statusRT.anchorMax = new Vector2(1, 0); 
            statusRT.pivot = new Vector2(1, 0);
            statusRT.anchoredPosition = new Vector2(-60, 10); // Match hoverPathText offset
            statusRT.offsetMin = new Vector2(360, 10); // Match hoverPathText offset
            statusRT.offsetMax = new Vector2(-60, 70); // Match hoverPathText offset

            // Pagination Controls (Bottom Left)
            CreatePaginationControls();

            // Pointer Dot
            pointerDotGO = new GameObject("PointerDot");
            pointerDotGO.transform.SetParent(canvasGO.transform, false);
            Image dotImg = pointerDotGO.AddComponent<Image>();
            dotImg.color = new Color(1, 1, 1, 0.5f);
            // We use a dot texture if available, otherwise just a small circle/square
            pointerDotGO.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 8);
            pointerDotGO.SetActive(false);

            CreateResizeHandles();

            // Close button - Rendered last to be on top
            GameObject closeBtn = UI.CreateUIButton(backgroundBoxGO, 50, 50, "X", 30, 0, 0, AnchorPresets.topRight, () => {
                if (Gallery.singleton != null) Gallery.singleton.RemovePanel(this);
                
                // Destroy canvas explicitly
                if (canvas != null)
                {
                    if (SuperController.singleton != null) SuperController.singleton.RemoveCanvas(canvas);
                    Destroy(canvas.gameObject);
                }
                
                Destroy(this.gameObject);
            });
            closeBtn.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.7f, 1f);
            AddHoverDelegate(closeBtn);

            UpdateSideButtonsVisibility();
            UpdateLayout();
            UpdateFollowButtonState();
            UpdateReplaceButtonState();
        }

        public void SetStatus(string msg)
        {
            if (string.IsNullOrEmpty(msg)) dragStatusMsg = null;
            else dragStatusMsg = msg;
        }

        public void ShowTemporaryStatus(string msg, float duration = 2.0f)
        {
            temporaryStatusMsg = msg;
            if (temporaryStatusCoroutine != null) StopCoroutine(temporaryStatusCoroutine);
            temporaryStatusCoroutine = StartCoroutine(ClearTemporaryStatus(duration));
        }

        private IEnumerator ClearTemporaryStatus(float duration)
        {
            yield return new WaitForSeconds(duration);
            temporaryStatusMsg = null;
            temporaryStatusCoroutine = null;
        }

        public void ShowCancelDropZone(bool show)
        {
            foreach (var g in cancelDropGroups)
            {
                if (g != null) g.SetActive(show);
            }
            if (show) UpdateCancelDropZoneVisual(-1);
        }

        public bool IsPointerOverCancelDropZone(PointerEventData eventData)
        {
            if (eventData == null || cancelDropZoneRTs.Count == 0) return false;
            Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : Camera.main;
            int hoveredIndex = -1;
            for (int i = 0; i < cancelDropZoneRTs.Count; i++)
            {
                RectTransform rt = cancelDropZoneRTs[i];
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, eventData.position, cam))
                {
                    hoveredIndex = i;
                    break;
                }
            }
            UpdateCancelDropZoneVisual(hoveredIndex);
            return hoveredIndex >= 0;
        }

        private void UpdateCancelDropZoneVisual(int hoveredIndex)
        {
            for (int i = 0; i < cancelDropZoneImages.Count; i++)
            {
                Image img = cancelDropZoneImages[i];
                if (img != null) img.color = (i == hoveredIndex) ? cancelZoneHoverColor : cancelZoneNormalColor;
            }
            for (int i = 0; i < cancelDropZoneTexts.Count; i++)
            {
                Text t = cancelDropZoneTexts[i];
                if (t != null) t.color = (i == hoveredIndex) ? Color.white : new Color(1f, 1f, 1f, 0.9f);
            }
        }

        void Update()
        {
            // Gallery Translucency Logic
            if (backgroundCanvasGroup != null && VPBConfig.Instance != null)
            {
                bool isHovered = hoverCount > 0 || isResizing;
                float targetGalleryAlpha = 1.0f;
                if (VPBConfig.Instance.EnableGalleryTranslucency && !isHovered)
                {
                    targetGalleryAlpha = Mathf.Max(0.1f, VPBConfig.Instance.GalleryOpacity);
                }

                if (Mathf.Abs(backgroundCanvasGroup.alpha - targetGalleryAlpha) > 0.01f)
                {
                    backgroundCanvasGroup.alpha = Mathf.Lerp(backgroundCanvasGroup.alpha, targetGalleryAlpha, Time.deltaTime * 10f);
                }
                else
                {
                    backgroundCanvasGroup.alpha = targetGalleryAlpha;
                }
            }

            // Status Bar Logic
            string finalStatus = null;
            if (dragStatusMsg != null)
            {
                finalStatus = dragStatusMsg;
            }
            else if (temporaryStatusMsg != null)
            {
                finalStatus = temporaryStatusMsg;
            }
            else
            {
                if (IsVisible && statusBarText != null && !UnityEngine.XR.XRSettings.enabled)
                {
                     string msg;
                     Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : Camera.main;
                     if (cam != null)
                     {
                         SceneUtils.DetectAtom(Input.mousePosition, cam, out msg);
                         if (!string.IsNullOrEmpty(msg)) finalStatus = msg;
                     }
                }
            }

            if (statusBarText != null)
            {
                statusBarText.text = finalStatus ?? "";
                statusBarText.gameObject.SetActive(!string.IsNullOrEmpty(finalStatus));
            }

            if (hoverPathText != null)
            {
                // Path label and status/hit label should never be on at once. Status has priority.
                bool showPath = string.IsNullOrEmpty(finalStatus) && !string.IsNullOrEmpty(hoverPathText.text);
                hoverPathText.gameObject.SetActive(showPath);
            }

            // FPS (lightweight, ~2Hz)
            if (fpsText != null)
            {
                fpsTimer += Time.unscaledDeltaTime;
                fpsFrames++;
                if (fpsTimer >= FpsInterval)
                {
                    float fps = fpsFrames / fpsTimer;
                    fpsText.text = string.Format("{0:0} FPS", fps);
                    fpsTimer = 0f;
                    fpsFrames = 0;
                }
            }

            // Side Buttons Auto-Hide Logic
            bool showSideButtons = hoverCount > 0;
            
            bool enableFade = (VPBConfig.Instance != null) ? VPBConfig.Instance.EnableGalleryFade : true;
            float targetAlpha = (showSideButtons || isResizing || !enableFade) ? 1.0f : 0.0f;
            if (Mathf.Abs(sideButtonsAlpha - targetAlpha) > 0.01f)
            {
                sideButtonsAlpha = Mathf.Lerp(sideButtonsAlpha, targetAlpha, Time.deltaTime * 15.0f);
                foreach (var cg in sideButtonGroups)
                {
                    if (cg != null) cg.alpha = sideButtonsAlpha;
                }
            }

            if (followUser && canvas != null)
            {
                if (_cachedCamera == null || !_cachedCamera.isActiveAndEnabled)
                    _cachedCamera = Camera.main;

                if (_cachedCamera != null)
                {
                    float now = Time.unscaledTime;

                    // Position and Rotation following throttled for VR comfort (discrete updates)
                    if (lastFollowUpdateTime <= 0f || now - lastFollowUpdateTime >= FollowUpdateInterval)
                    {
                        lastFollowUpdateTime = now;

                        if (!offsetsInitialized)
                        {
                            Vector3 offset = canvas.transform.position - _cachedCamera.transform.position;
                            followYOffset = offset.y;
                            followXZOffset = new Vector2(offset.x, offset.z);
                            offsetsInitialized = true;
                        }
                        
                        // Handle Position Following
                        Vector3 camPos = _cachedCamera.transform.position;
                        Vector3 currentPos = canvas.transform.position;
                        Vector3 targetPos = currentPos;

                        // Horizontal Following (Strictly respect FollowDistance)
                        if (VPBConfig.Instance.FollowDistance)
                        {
                            Vector3 hOffset = new Vector3(followXZOffset.x, 0, followXZOffset.y);
                            if (hOffset.sqrMagnitude < 0.0001f) hOffset = Vector3.forward;
                            Vector3 hTarget = camPos + hOffset.normalized * VPBConfig.Instance.FollowDistanceMeters;
                            targetPos.x = hTarget.x;
                            targetPos.z = hTarget.z;
                        }

                        // Vertical Following (Eye Height)
                        if (VPBConfig.Instance.FollowEyeHeight)
                        {
                            targetPos.y = camPos.y + followYOffset;
                        }
                        else
                        {
                            // Stay at current Y
                            targetPos.y = currentPos.y;
                        }

                        // Only move if position changed by more than threshold
                        bool bypassThreshold = VPBConfig.Instance.IsLoadingScene;
                        if (bypassThreshold || Vector3.Distance(currentPos, targetPos) > VPBConfig.Instance.MovementThreshold)
                        {
                            canvas.transform.position = targetPos;
                        }

                        // Handle Rotation Following (Respect FollowAngle setting)
                        if (VPBConfig.Instance.FollowAngle || bypassThreshold)
                        {
                            Vector3 lookDir = canvas.transform.position - _cachedCamera.transform.position;
                            if (lookDir.sqrMagnitude > 0.001f)
                            {
                                targetFollowRotation = Quaternion.LookRotation(lookDir, Vector3.up);
                                
                                if (bypassThreshold)
                                {
                                    canvas.transform.rotation = targetFollowRotation; // Immediate during load
                                }
                                else
                                {
                                    float angleDiff = Quaternion.Angle(canvas.transform.rotation, targetFollowRotation);
                                    if (!isReorienting && angleDiff > VPBConfig.Instance.ReorientStartAngle) isReorienting = true;
                                    if (isReorienting)
                                    {
                                        canvas.transform.rotation = Quaternion.RotateTowards(canvas.transform.rotation, targetFollowRotation, FollowRotateStepDegrees);
                                        if (Quaternion.Angle(canvas.transform.rotation, targetFollowRotation) < ReorientStopAngle) isReorienting = false;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Pointer Dot Logic
            if (pointerDotGO != null)
            {
                if (hoverCount > 0 && currentPointerData != null && currentPointerData.pointerCurrentRaycast.isValid)
                {
                    if (!pointerDotGO.activeSelf) pointerDotGO.SetActive(true);
                    // Use standard 5mm offset to prevent z-fighting
                    pointerDotGO.transform.position = currentPointerData.pointerCurrentRaycast.worldPosition - canvas.transform.forward * 0.005f;
                    pointerDotGO.transform.SetAsLastSibling(); 
                }
                else
                {
                    if (pointerDotGO.activeSelf) pointerDotGO.SetActive(false);
                }
            }
        }
    }
}
