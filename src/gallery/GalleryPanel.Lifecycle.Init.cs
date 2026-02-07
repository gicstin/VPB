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
        public void Init()
        {
            if (canvas != null) return;

            // Subscribe to config changes
            if (VPBConfig.Instance != null)
            {
                bool isVR = false;
                try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }

                isFixedLocally = !isVR && VPBConfig.Instance.DesktopFixedMode && (Gallery.singleton == null || Gallery.singleton.PanelCount == 0);
                
                // Fixed panes should start with side tab lists collapsed
                if (isFixedLocally)
                {
                    leftActiveContent = null;
                    rightActiveContent = null;
                }

                VPBConfig.Instance.ConfigChanged += UpdateSideButtonPositions;
                VPBConfig.Instance.ConfigChanged += UpdateSideButtonsVisibility;
                VPBConfig.Instance.ConfigChanged += ApplyCurvatureToChildren;
                VPBConfig.Instance.ConfigChanged += UpdateFooterFollowStates;
                VPBConfig.Instance.ConfigChanged += UpdateDesktopModeButton;
                VPBConfig.Instance.ConfigChanged += UpdateLayout;
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
            // We use our custom CylindricalGraphicRaycaster to support curvature.
            CylindricalGraphicRaycaster gr = canvasGO.AddComponent<CylindricalGraphicRaycaster>();
            gr.ignoreReversedGraphics = true;
            
            if (SuperController.singleton != null)
                SuperController.singleton.AddCanvas(canvas);

            // VaM's AddCanvas often adds a BoxCollider to the canvasGO or its children.
            // We need to remove it so it doesn't interfere with our curved interaction/MeshCollider.
            foreach (var bc in canvasGO.GetComponentsInChildren<BoxCollider>(true))
            {
                Destroy(bc);
            }

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
            
            // Collapse Trigger Area (Right edge) - 60% height, centered, 60px wide, chamfered corners
            collapseTriggerGO = UI.AddChildGOChamferedImage(canvasGO, new Color(0.15f, 0.15f, 0.15f, 0.4f), AnchorPresets.vStretchRight, 60, 0, Vector2.zero, 100f);
            RectTransform ctRT = collapseTriggerGO.GetComponent<RectTransform>();
            ctRT.anchorMin = new Vector2(1, 0.2f);
            ctRT.anchorMax = new Vector2(1, 0.8f);
            collapseTriggerGO.name = "FixedModeCollapseTrigger";
            
            GameObject ctTextGO = new GameObject("Text");
            ctTextGO.transform.SetParent(collapseTriggerGO.transform, false);
            collapseHandleText = ctTextGO.AddComponent<Text>();
            collapseHandleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            collapseHandleText.text = "<";
            collapseHandleText.fontSize = 30;
            collapseHandleText.color = new Color(1, 1, 1, 0.5f);
            collapseHandleText.alignment = TextAnchor.MiddleCenter;
            RectTransform ctTextRT = ctTextGO.GetComponent<RectTransform>();
            ctTextRT.anchorMin = Vector2.zero;
            ctTextRT.anchorMax = Vector2.one;
            ctTextRT.sizeDelta = Vector2.zero;

            var ctHover = collapseTriggerGO.AddComponent<UIHoverDelegate>();
            ctHover.OnHoverChange += (enter) => {
                isHoveringTrigger = enter;
            };
            collapseTriggerGO.SetActive(false); // Hidden by default, only used in fixed mode
            
            dragger = backgroundBoxGO.AddComponent<UIDraggable>();
            dragger.target = canvasGO.transform;
            dragger.OnDragEnd = () => {
                // Toggle active state to force VaM/Unity to refresh interaction state after move
                if (canvasGO != null)
                {
                    canvasGO.SetActive(false);
                    canvasGO.SetActive(true);
                    
                    // Also ensure curvature is correctly aligned with new position
                    ApplyCurvatureToChildren();
                }
            };

            settingsPanel = new SettingsPanel(this, backgroundBoxGO);
            actionsPanel = new GalleryActionsPanel(this, canvasGO, backgroundBoxGO);
            quickFiltersUI = new QuickFiltersUI(this, backgroundBoxGO);

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
            fpsRT.anchoredPosition = new Vector2(-120, 0);
            fpsRT.sizeDelta = new Vector2(100, 40);

            titleSearchInput = CreateSearchInput(titleBarGO, 240f, (val) => {
                SetNameFilter(val);
            });
            RectTransform titleSearchRT = titleSearchInput.GetComponent<RectTransform>();
            titleSearchRT.anchorMin = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchorMax = new Vector2(0.5f, 0.5f);
            titleSearchRT.pivot = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchoredPosition = new Vector2(-40, 0);
            titleSearchRT.sizeDelta = new Vector2(240, 40);

            // File Sort Button
            GameObject fileSortBtn = UI.CreateUIButton(titleBarGO, 40, 40, "Az↑", 16, 0, 0, AnchorPresets.middleCenter, null);
            fileSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            fileSortBtn.GetComponentInChildren<Text>().color = Color.white;
            RectTransform fileSortRT = fileSortBtn.GetComponent<RectTransform>();
            fileSortRT.anchorMin = new Vector2(0.5f, 0.5f);
            fileSortRT.anchorMax = new Vector2(0.5f, 0.5f);
            fileSortRT.pivot = new Vector2(0.5f, 0.5f);
            fileSortRT.anchoredPosition = new Vector2(135, 0); // To the right of search
            
            fileSortBtnText = fileSortBtn.GetComponentInChildren<Text>();
            
            Button fileSortButton = fileSortBtn.GetComponent<Button>();
            fileSortButton.onClick.RemoveAllListeners();
            fileSortButton.onClick.AddListener(() => CycleSort("Files", fileSortBtnText));
            
            AddRightClickDelegate(fileSortBtn, () => ToggleSortDirection("Files", fileSortBtnText));
            
            // Init File Sort State
            UpdateSortButtonText(fileSortBtnText, GetSortState("Files"));

            ratingSortToggleBtn = UI.CreateUIButton(titleBarGO, 40, 40, "★", 18, 0, 0, AnchorPresets.middleCenter, null);
            ratingSortToggleBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            ratingSortToggleBtnText = ratingSortToggleBtn.GetComponentInChildren<Text>();
            ratingSortToggleBtnText.color = Color.white;
            RectTransform ratingSortToggleRT = ratingSortToggleBtn.GetComponent<RectTransform>();
            ratingSortToggleRT.anchorMin = new Vector2(0.5f, 0.5f);
            ratingSortToggleRT.anchorMax = new Vector2(0.5f, 0.5f);
            ratingSortToggleRT.pivot = new Vector2(0.5f, 0.5f);
            ratingSortToggleRT.anchoredPosition = new Vector2(180, 0);
            Button ratingSortToggleButton = ratingSortToggleBtn.GetComponent<Button>();
            ratingSortToggleButton.onClick.RemoveAllListeners();
            ratingSortToggleButton.onClick.AddListener(ToggleRatingSort);
            AddTooltip(ratingSortToggleBtn, "Show Only Rated Items");
            SyncRatingSortToggleState();

            // Refresh Button (to the right of Star)
            GameObject refreshBtn = UI.CreateUIButton(titleBarGO, 90, 40, "Refresh", 16, 0, 0, AnchorPresets.middleCenter, null);
            refreshBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            refreshBtn.GetComponentInChildren<Text>().color = Color.white;
            RectTransform refreshRT = refreshBtn.GetComponent<RectTransform>();
            refreshRT.anchorMin = new Vector2(0.5f, 0.5f);
            refreshRT.anchorMax = new Vector2(0.5f, 0.5f);
            refreshRT.pivot = new Vector2(0.5f, 0.5f);
            refreshRT.anchoredPosition = new Vector2(255, 0);

            Button refreshButton = refreshBtn.GetComponent<Button>();
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(() => {
                try
                {
                    if (!IsHubMode) ShowTemporaryStatus("Refreshing packages...", 1.5f);
                    try { MVR.FileManagement.FileManager.Refresh(); } catch { }
                    FileManager.Refresh(true, false, false);
                    creatorsCached = false;
                    categoriesCached = false;
                    tagsCached = false;
                    refreshOnNextShow = true;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Refresh packages failed: " + ex);
                    ShowTemporaryStatus("Refresh failed. See log.", 2f);
                }
            });
            AddTooltip(refreshBtn, "Refresh Packages");

            // Filter Presets Button
            GameObject qfToggleBtn = UI.CreateUIButton(titleBarGO, 130, 45, "Filter Presets", 16, 0, 0, AnchorPresets.middleCenter, ToggleQuickFilters);
            qfToggleBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            quickFiltersToggleBtnText = qfToggleBtn.GetComponentInChildren<Text>();
            quickFiltersToggleBtnText.color = Color.white;
            RectTransform qfToggleRT = qfToggleBtn.GetComponent<RectTransform>();
            qfToggleRT.anchorMin = new Vector2(0.5f, 0.5f);
            qfToggleRT.anchorMax = new Vector2(0.5f, 0.5f);
            qfToggleRT.pivot = new Vector2(0.5f, 0.5f);
            qfToggleRT.anchoredPosition = new Vector2(-240, 0); // Adjusted for wider button
            AddTooltip(qfToggleBtn, "Filter Presets");

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
                rightTabRT.offsetMax = new Vector2(-10, -95); 
                
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
                    hubTagPage = 0;
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
                    currentSceneSourceFilter = "";
                    currentAppearanceSourceFilter = "";
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

                // Right Refresh Button (to the right of Sort, still left of Search)
                rightRefreshBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "⟳", 18, 0, 0, AnchorPresets.topRight, null);
                rightRefreshBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                rightRefreshBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform rrRT = rightRefreshBtn.GetComponent<RectTransform>();
                rrRT.anchorMin = new Vector2(1, 1);
                rrRT.anchorMax = new Vector2(1, 1);
                rrRT.pivot = new Vector2(1, 1);
                rrRT.anchoredPosition = new Vector2(-145, -55); // Between Sort and Search

                rightRefreshBtnText = rightRefreshBtn.GetComponentInChildren<Text>();
                Button rightRefreshButton = rightRefreshBtn.GetComponent<Button>();
                rightRefreshButton.onClick.RemoveAllListeners();
                rightRefreshButton.onClick.AddListener(() => {
                    try
                    {
                        if (!IsHubMode) ShowTemporaryStatus("Refreshing packages...", 1.5f);
                        try { MVR.FileManagement.FileManager.Refresh(); } catch { }
                        FileManager.Refresh(true, false, false);
                        creatorsCached = false;
                        categoriesCached = false;
                        tagsCached = false;
                            refreshOnNextShow = true;
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Refresh packages failed: " + ex);
                        ShowTemporaryStatus("Refresh failed. See log.", 2f);
                    }
                });
                AddTooltip(rightRefreshBtn, "Refresh Packages");

                rightSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    if (rightActiveContent == ContentType.Category) categoryFilter = val;
                    else if (rightActiveContent == ContentType.Creator) creatorFilter = val;
                    UpdateTabs();
                }, () => {
                    if (rightActiveContent == ContentType.Creator) {
                        currentCreator = "";
                        categoriesCached = false;
                        tagsCached = false;
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
                leftTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -95);
                
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
                    hubTagPage = 0;
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
                    currentSceneSourceFilter = "";
                    currentAppearanceSourceFilter = "";
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
                rightSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleRight, 130, 700, new Vector2(140, 0));
                sideButtonGroups.Add(rightSideContainer.AddComponent<CanvasGroup>());
                AddHoverDelegate(rightSideContainer);

                // Full-height hover strip to cover top/bottom gaps outside the 700px side container
                rightSideHoverStrip = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.vStretchRight, 130, 0, new Vector2(140, 0));
                AddHoverDelegate(rightSideHoverStrip);
                try
                {
                    // Ensure it doesn't intercept clicks on actual buttons (place behind container)
                    rightSideHoverStrip.transform.SetAsFirstSibling();
                }
                catch { }

                rightClearCreatorBtn = UI.CreateUIButton(backgroundBoxGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleRight, () => {
                    currentCreator = "";
                    categoriesCached = false;
                    tagsCached = false;
                    RefreshFiles();
                    UpdateTabs();
                    UpdateSideButtonsVisibility();
                });
                rightClearCreatorBtn.GetComponent<Image>().color = ColorCreator;
                rightClearCreatorBtn.GetComponentInChildren<Text>().color = Color.white;
                sideButtonGroups.Add(rightClearCreatorBtn.AddComponent<CanvasGroup>());
                rightClearCreatorBtn.SetActive(false);

                // Right Toggle Buttons
                int btnFontSize = 20;
                float btnWidth = 120;
                float btnHeight = 50;
                float spacing = 60f;
                float groupGap = 10f;
                float startY = 320f;

                // Fixed/Floating (Topmost)
                GameObject rightDesktopBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Floating", btnFontSize, 0, startY, AnchorPresets.centre, ToggleDesktopMode);
                rightDesktopModeBtnImage = rightDesktopBtn.GetComponent<Image>();
                rightDesktopModeBtnText = rightDesktopBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightDesktopBtn.GetComponent<RectTransform>());

                // Settings
                GameObject rightSettingsBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Settings", btnFontSize, 0, startY - spacing - groupGap, AnchorPresets.centre, () => {
                    ToggleSettings(true);
                });
                rightSettingsBtn.GetComponent<Image>().color = new Color(0.15f, 0.3f, 0.45f, 1f); // Darker Blueish
                rightSideButtons.Add(rightSettingsBtn.GetComponent<RectTransform>());

                // Follow
                GameObject rightFollowBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Static", btnFontSize, 0, startY - spacing * 2 - groupGap * 2, AnchorPresets.centre, ToggleFollowMode);
                rightFollowBtnImage = rightFollowBtn.GetComponent<Image>();
                rightFollowBtnText = rightFollowBtn.GetComponentInChildren<Text>();
                rightFollowBtnImage.color = new Color(0.15f, 0.45f, 0.6f, 1f); // Darker Follow Blue
                rightSideButtons.Add(rightFollowBtn.GetComponent<RectTransform>());

                // Clone (Gray)
                GameObject rightCloneBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Clone", btnFontSize, 0, startY - spacing * 3 - groupGap * 2, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, true);
                });
                rightCloneBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f); // Darker Gray
                rightSideButtons.Add(rightCloneBtn.GetComponent<RectTransform>());

                // Load Random (always available)
                rightLoadRandomBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Random", btnFontSize, 0, 0, AnchorPresets.centre, LoadRandom);
                rightLoadRandomBtn.GetComponent<Image>().color = new Color(0.35f, 0.25f, 0.55f, 1f);
                rightSideButtons.Add(rightLoadRandomBtn.GetComponent<RectTransform>());

                // Category (Red)
                GameObject rightCatBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Category", btnFontSize, 0, startY - spacing * 4 - groupGap * 3, AnchorPresets.centre, () => {
                    if (isFixedLocally) ToggleLeft(ContentType.Category); else ToggleRight(ContentType.Category);
                });
                rightCategoryBtnImage = rightCatBtn.GetComponent<Image>();
                rightCategoryBtnImage.color = ColorCategory;
                rightCategoryBtnText = rightCatBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightCatBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(rightCatBtn, () => ToggleRight(ContentType.Category));
                
                // Creator (Green)
                GameObject rightCreatorBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 6 - groupGap * 3, AnchorPresets.centre, () => {
                    if (isFixedLocally) ToggleLeft(ContentType.Creator); else ToggleRight(ContentType.Creator);
                });
                rightCreatorBtnImage = rightCreatorBtn.GetComponent<Image>();
                rightCreatorBtnImage.color = ColorCreator;
                rightCreatorBtnText = rightCreatorBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightCreatorBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(rightCreatorBtn, () => ToggleRight(ContentType.Creator));

                // Target (Dropdown-like)
                GameObject rightTargetBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Target: None", 14, 0, startY - spacing * 8 - groupGap * 4, AnchorPresets.centre, () => CycleTarget(true));
                rightTargetBtnImage = rightTargetBtn.GetComponent<Image>();
                rightTargetBtnImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                rightTargetBtnText = rightTargetBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightTargetBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(rightTargetBtn, () => CycleTarget(false));

                // Apply Mode (Right)
                GameObject rightApplyModeBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "2-Click", btnFontSize, 0, startY - spacing * 9 - groupGap * 4, AnchorPresets.centre, ToggleApplyMode);
                rightApplyModeBtnImage = rightApplyModeBtn.GetComponent<Image>();
                rightApplyModeBtnText = rightApplyModeBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightApplyModeBtn.GetComponent<RectTransform>());

                // Replace Toggle (Right)
                GameObject rightReplaceBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 10 - groupGap * 4, AnchorPresets.centre, ToggleReplaceMode);
                rightReplaceBtnImage = rightReplaceBtn.GetComponent<Image>();
                rightReplaceBtnText = rightReplaceBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightReplaceBtn.GetComponent<RectTransform>());

                rightSaveBtnGO = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Save", btnFontSize, 0, startY - spacing * 11 - groupGap * 4, AnchorPresets.centre, () => {
                    try
                    {
                        ToggleSaveSubmenuFromSideButtons();
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Save (Right) exception: " + ex);
                    }
                });
                rightSaveBtnGO.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.2f, 1f);
                rightSaveBtnGO.GetComponentInChildren<Text>().color = Color.white;
                rightSideButtons.Add(rightSaveBtnGO.GetComponent<RectTransform>());
                rightSaveBtnGO.SetActive(false);

                try
                {
                    EventTrigger et = rightSaveBtnGO.GetComponent<EventTrigger>();
                    if (et == null) et = rightSaveBtnGO.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuParentHoverCount++;
                            saveSubmenuParentHovered = true;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                            if (!saveSubmenuOpen) ToggleSaveSubmenuFromSideButtons();
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuParentHoverCount--;
                            if (saveSubmenuParentHoverCount < 0) saveSubmenuParentHoverCount = 0;
                            saveSubmenuParentHovered = saveSubmenuParentHoverCount > 0;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                try
                {
                    rightSaveSubmenuPanelGO = new GameObject("RightSaveSubmenuPanel");
                    rightSaveSubmenuPanelGO.transform.SetParent(rightSideContainer.transform, false);
                    RectTransform prt = rightSaveSubmenuPanelGO.AddComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0.5f, 0.5f);
                    prt.anchorMax = new Vector2(0.5f, 0.5f);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(btnWidth * 1.6f, btnHeight);
                    prt.anchoredPosition = Vector2.zero;

                    AddHoverDelegate(rightSaveSubmenuPanelGO);

                    Image pimg = rightSaveSubmenuPanelGO.AddComponent<Image>();
                    pimg.color = new Color(0, 0, 0, 0.01f);
                    pimg.raycastTarget = true;

                    EventTrigger pet = rightSaveSubmenuPanelGO.AddComponent<EventTrigger>();
                    var pe = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    pe.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuOptionsHoverCount++;
                            saveSubmenuOptionsHovered = true;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(pe);

                    var px = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    px.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuOptionsHoverCount--;
                            if (saveSubmenuOptionsHoverCount < 0) saveSubmenuOptionsHoverCount = 0;
                            saveSubmenuOptionsHovered = saveSubmenuOptionsHoverCount > 0;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(px);

                    rightSaveSubmenuPanelGO.SetActive(false);
                }
                catch { }

                for (int i = 0; i < SaveSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(rightSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    rightSaveSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        int buttonIndex = i; // local copy for closure
                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                saveSubmenuOptionsHoverCount++;
                                saveSubmenuOptionsHovered = true;
                                saveSubmenuLastHoverTime = Time.unscaledTime;

                                if (buttonIndex == 4 && !saveSubmenuMoreVisible && saveSubmenuOpen)
                                {
                                    saveSubmenuMoreVisible = true;
                                    PopulateSaveSubmenuButtons();
                                    PositionSaveSubmenuButtons();
                                }
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                saveSubmenuOptionsHoverCount--;
                                if (saveSubmenuOptionsHoverCount < 0) saveSubmenuOptionsHoverCount = 0;
                                saveSubmenuOptionsHovered = saveSubmenuOptionsHoverCount > 0;
                                saveSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                // Scene Context (Right)
                rightRemoveAtomBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Remove\nAtom", 18, 0, 0, AnchorPresets.centre, () => {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        ToggleAtomSubmenuFromSideButtons();
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Atom (Right) exception: " + ex);
                    }
                });
                rightRemoveAtomBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);
                rightRemoveAtomBtn.GetComponentInChildren<Text>().color = Color.white;
                rightSideButtons.Add(rightRemoveAtomBtn.GetComponent<RectTransform>());
                rightRemoveAtomBtn.SetActive(false);

                try
                {
                    EventTrigger et = rightRemoveAtomBtn.GetComponent<EventTrigger>();
                    if (et == null) et = rightRemoveAtomBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            atomSubmenuParentHoverCount++;
                            atomSubmenuParentHovered = true;
                            atomSubmenuLastHoverTime = Time.unscaledTime;
                            if (!atomSubmenuOpen) ToggleAtomSubmenuFromSideButtons();
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            atomSubmenuParentHoverCount--;
                            if (atomSubmenuParentHoverCount < 0) atomSubmenuParentHoverCount = 0;
                            atomSubmenuParentHovered = atomSubmenuParentHoverCount > 0;
                            atomSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                // Hub (Orange)
                GameObject rightHubBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Hub", btnFontSize, 0, startY - spacing * 11 - groupGap * 4, AnchorPresets.centre, () => {
                    if (VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode)
                    {
                        if (isFixedLocally) ToggleLeft(ContentType.Hub); else ToggleRight(ContentType.Hub);
                    }
                    else
                    {
                        VamHookPlugin.singleton?.OpenHubBrowse();
                        Hide();
                    }
                });
                rightHubBtnGO = rightHubBtn;
                rightHubBtnImage = rightHubBtn.GetComponent<Image>();
                rightHubBtnImage.color = ColorHub;
                rightHubBtnText = rightHubBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightHubBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(rightHubBtn, () => {
                    if (VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode)
                    {
                        ToggleRight(ContentType.Hub);
                    }
                    else
                    {
                        VamHookPlugin.singleton?.OpenHubBrowse();
                        Hide();
                    }
                });

                // Undo (Right)
                GameObject rightUndoBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 12 - groupGap * 5, AnchorPresets.centre, Undo);
                rightUndoBtnGO = rightUndoBtn;
                rightUndoBtn.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.15f, 1f); // Darker Brown/Orange
                rightSideButtons.Add(rightUndoBtn.GetComponent<RectTransform>());

                // Redo (Right)
                GameObject rightRedoBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Redo", btnFontSize, 0, startY - spacing * 13 - groupGap * 5, AnchorPresets.centre, Redo);
                rightRedoBtnGO = rightRedoBtn;
                rightRedoBtn.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.15f, 1f); // Darker Brown/Orange
                rightSideButtons.Add(rightRedoBtn.GetComponent<RectTransform>());

                // Context Actions (Right) - inserted above Undo
                rightRemoveAllClothingBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Remove\nClothing", 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Clothing (Right)");
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        LogUtil.Log($"[VPB] Remove Clothing (Right) resolved target: {(target != null ? target.uid + " (" + target.type + ")" : "<null>")}");
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        UIDraggableItem dragger = rightRemoveAllClothingBtn.GetComponent<UIDraggableItem>();
                        if (dragger == null) dragger = rightRemoveAllClothingBtn.AddComponent<UIDraggableItem>();
                        dragger.Panel = this;
                        dragger.RemoveAllClothing(target);

                        // Robust UI sync: close submenu immediately after remove-all.
                        CloseClothingSubmenuUI();
                        UpdateSideButtonPositions();
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing (Right) exception: " + ex);
                    }
                });
                rightRemoveAllClothingBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);
                rightRemoveAllClothingBtn.GetComponentInChildren<Text>().color = Color.white;
                rightSideButtons.Add(rightRemoveAllClothingBtn.GetComponent<RectTransform>());
                rightRemoveAllClothingBtn.SetActive(false);

                try
                {
                    EventTrigger et = rightRemoveAllClothingBtn.GetComponent<EventTrigger>();
                    if (et == null) et = rightRemoveAllClothingBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            clothingSubmenuParentHoverCount++;
                            clothingSubmenuParentHovered = true;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                            if (!clothingSubmenuOpen) ToggleClothingSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            clothingSubmenuParentHoverCount--;
                            if (clothingSubmenuParentHoverCount < 0) clothingSubmenuParentHoverCount = 0;
                            clothingSubmenuParentHovered = clothingSubmenuParentHoverCount > 0;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                rightRemoveClothingExpandBtn = UI.CreateUIButton(rightRemoveAllClothingBtn, btnHeight, btnHeight, ">", 18, 104, 0, AnchorPresets.middleCenter, () => {
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        ToggleClothingSubmenuFromSideButtons(target);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing slot picker exception: " + ex);
                    }
                });
                try
                {
                    EventTrigger et = rightRemoveClothingExpandBtn.GetComponent<EventTrigger>();
                    if (et == null) et = rightRemoveClothingExpandBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            if (!clothingSubmenuOpen) ToggleClothingSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);
                }
                catch { }
                rightRemoveClothingExpandBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                rightRemoveClothingExpandBtn.GetComponentInChildren<Text>().color = Color.white;
                rightRemoveClothingExpandBtn.SetActive(false);

                // Clothing Submenu Hover Panel (Right) - catches pointer over gaps
                try
                {
                    rightRemoveClothingSubmenuPanelGO = new GameObject("RightRemoveClothingSubmenuPanel");
                    rightRemoveClothingSubmenuPanelGO.transform.SetParent(rightSideContainer.transform, false);
                    RectTransform prt = rightRemoveClothingSubmenuPanelGO.AddComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0.5f, 0.5f);
                    prt.anchorMax = new Vector2(0.5f, 0.5f);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(btnWidth * 1.6f, btnHeight);
                    prt.anchoredPosition = Vector2.zero;

                    AddHoverDelegate(rightRemoveClothingSubmenuPanelGO);

                    Image pimg = rightRemoveClothingSubmenuPanelGO.AddComponent<Image>();
                    pimg.color = new Color(0, 0, 0, 0.01f);
                    pimg.raycastTarget = true;

                    EventTrigger pet = rightRemoveClothingSubmenuPanelGO.AddComponent<EventTrigger>();
                    var pe = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    pe.callback.AddListener((data) => {
                        try
                        {
                            clothingSubmenuOptionsHoverCount++;
                            clothingSubmenuOptionsHovered = true;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(pe);

                    var px = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    px.callback.AddListener((data) => {
                        try
                        {
                            clothingSubmenuOptionsHoverCount--;
                            if (clothingSubmenuOptionsHoverCount < 0) clothingSubmenuOptionsHoverCount = 0;
                            clothingSubmenuOptionsHovered = clothingSubmenuOptionsHoverCount > 0;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(px);

                    rightRemoveClothingSubmenuPanelGO.SetActive(false);
                }
                catch { }

                // Atom Submenu Buttons (Right) - pooled
                for (int i = 0; i < AtomSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(rightSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    rightSideButtons.Add(b.GetComponent<RectTransform>());
                    rightRemoveAtomSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                atomSubmenuOptionsHoverCount++;
                                atomSubmenuOptionsHovered = true;
                                atomSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                atomSubmenuOptionsHoverCount--;
                                if (atomSubmenuOptionsHoverCount < 0) atomSubmenuOptionsHoverCount = 0;
                                atomSubmenuOptionsHovered = atomSubmenuOptionsHoverCount > 0;
                                atomSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                // Clothing Visibility Toggle Buttons (Right) - pooled, placed outside submenu items
                for (int i = 0; i < ClothingSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(rightSideContainer, 80f, btnHeight, "Hide", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                    rightSideButtons.Add(b.GetComponent<RectTransform>());
                    rightRemoveClothingVisibilityToggleButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount++;
                                clothingSubmenuOptionsHovered = true;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount--;
                                if (clothingSubmenuOptionsHoverCount < 0) clothingSubmenuOptionsHoverCount = 0;
                                clothingSubmenuOptionsHovered = clothingSubmenuOptionsHoverCount > 0;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                for (int i = 0; i < ClothingSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(rightSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    rightSideButtons.Add(b.GetComponent<RectTransform>());
                    rightRemoveClothingSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount++;
                                clothingSubmenuOptionsHovered = true;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount--;
                                if (clothingSubmenuOptionsHoverCount < 0) clothingSubmenuOptionsHoverCount = 0;
                                clothingSubmenuOptionsHovered = clothingSubmenuOptionsHoverCount > 0;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                rightRemoveAllHairBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Remove\nHair", 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Hair (Right)");
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        LogUtil.Log($"[VPB] Remove Hair (Right) resolved target: {(target != null ? target.uid + " (" + target.type + ")" : "<null>")}");
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        UIDraggableItem dragger = rightRemoveAllHairBtn.GetComponent<UIDraggableItem>();
                        if (dragger == null) dragger = rightRemoveAllHairBtn.AddComponent<UIDraggableItem>();
                        dragger.Panel = this;
                        dragger.RemoveAllHair(target);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair (Right) exception: " + ex);
                    }
                });
                rightRemoveAllHairBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);
                rightRemoveAllHairBtn.GetComponentInChildren<Text>().color = Color.white;
                rightSideButtons.Add(rightRemoveAllHairBtn.GetComponent<RectTransform>());
                rightRemoveAllHairBtn.SetActive(false);

                try
                {
                    EventTrigger et = rightRemoveAllHairBtn.GetComponent<EventTrigger>();
                    if (et == null) et = rightRemoveAllHairBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            hairSubmenuParentHoverCount++;
                            hairSubmenuParentHovered = true;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                            if (!hairSubmenuOpen) ToggleHairSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            hairSubmenuParentHoverCount--;
                            if (hairSubmenuParentHoverCount < 0) hairSubmenuParentHoverCount = 0;
                            hairSubmenuParentHovered = hairSubmenuParentHoverCount > 0;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                rightRemoveHairExpandBtn = UI.CreateUIButton(rightRemoveAllHairBtn, btnHeight, btnHeight, ">", 18, 104, 0, AnchorPresets.middleCenter, () => {
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        ToggleHairSubmenuFromSideButtons(target);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair slot picker exception: " + ex);
                    }
                });
                try
                {
                    EventTrigger et = rightRemoveHairExpandBtn.GetComponent<EventTrigger>();
                    if (et == null) et = rightRemoveHairExpandBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            if (!hairSubmenuOpen) ToggleHairSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);
                }
                catch { }
                rightRemoveHairExpandBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                rightRemoveHairExpandBtn.GetComponentInChildren<Text>().color = Color.white;
                rightRemoveHairExpandBtn.SetActive(false);

                try
                {
                    rightRemoveHairSubmenuGapPanelGO = new GameObject("RightRemoveHairSubmenuGapPanel");
                    rightRemoveHairSubmenuGapPanelGO.transform.SetParent(rightSideContainer.transform, false);
                    RectTransform prt = rightRemoveHairSubmenuGapPanelGO.AddComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0.5f, 0.5f);
                    prt.anchorMax = new Vector2(0.5f, 0.5f);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(btnWidth * 1.6f, btnHeight);
                    prt.anchoredPosition = Vector2.zero;

                    AddHoverDelegate(rightRemoveHairSubmenuGapPanelGO);

                    Image pimg = rightRemoveHairSubmenuGapPanelGO.AddComponent<Image>();
                    pimg.color = new Color(0, 0, 0, 0.01f);
                    pimg.raycastTarget = true;

                    EventTrigger pet = rightRemoveHairSubmenuGapPanelGO.AddComponent<EventTrigger>();
                    var pe = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    pe.callback.AddListener((data) => {
                        try
                        {
                            hairSubmenuOptionsHoverCount++;
                            hairSubmenuOptionsHovered = true;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(pe);

                    var px = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    px.callback.AddListener((data) => {
                        try
                        {
                            hairSubmenuOptionsHoverCount--;
                            if (hairSubmenuOptionsHoverCount < 0) hairSubmenuOptionsHoverCount = 0;
                            hairSubmenuOptionsHovered = hairSubmenuOptionsHoverCount > 0;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(px);

                    rightRemoveHairSubmenuGapPanelGO.SetActive(false);
                }
                catch { }

                // Hair Submenu Buttons (Right) - pooled, treated as real side buttons
                rightRemoveHairSubmenuStartIndex = rightSideButtons.Count;
                for (int i = 0; i < HairSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(rightSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    rightSideButtons.Add(b.GetComponent<RectTransform>());
                    rightRemoveHairSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();
                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                hairSubmenuOptionsHoverCount++;
                                hairSubmenuOptionsHovered = true;
                                hairSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                hairSubmenuOptionsHoverCount--;
                                if (hairSubmenuOptionsHoverCount < 0) hairSubmenuOptionsHoverCount = 0;
                                hairSubmenuOptionsHovered = hairSubmenuOptionsHoverCount > 0;
                                hairSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                // Left Button Container
                leftSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleLeft, 130, 700, new Vector2(-140, 0));
                sideButtonGroups.Add(leftSideContainer.AddComponent<CanvasGroup>());
                AddHoverDelegate(leftSideContainer);

                // Full-height hover strip to cover top/bottom gaps outside the 700px side container
                leftSideHoverStrip = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.vStretchLeft, 130, 0, new Vector2(-140, 0));
                AddHoverDelegate(leftSideHoverStrip);
                try
                {
                    // Ensure it doesn't intercept clicks on actual buttons (place behind container)
                    leftSideHoverStrip.transform.SetAsFirstSibling();
                }
                catch { }

                leftClearCreatorBtn = UI.CreateUIButton(backgroundBoxGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleLeft, () => {
                    currentCreator = "";
                    categoriesCached = false;
                    tagsCached = false;
                    RefreshFiles();
                    UpdateTabs();
                    UpdateSideButtonsVisibility();
                });
                leftClearCreatorBtn.GetComponent<Image>().color = ColorCreator;
                leftClearCreatorBtn.GetComponentInChildren<Text>().color = Color.white;
                sideButtonGroups.Add(leftClearCreatorBtn.AddComponent<CanvasGroup>());
                leftClearCreatorBtn.SetActive(false);

                // Left Toggle Buttons
                // Fixed/Floating (Topmost)
                GameObject leftDesktopBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Floating", btnFontSize, 0, startY, AnchorPresets.centre, ToggleDesktopMode);
                leftDesktopModeBtnImage = leftDesktopBtn.GetComponent<Image>();
                leftDesktopModeBtnText = leftDesktopBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftDesktopBtn.GetComponent<RectTransform>());

                // Settings
                GameObject leftSettingsBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Settings", btnFontSize, 0, startY - spacing - groupGap, AnchorPresets.centre, () => {
                    ToggleSettings(false);
                });
                leftSettingsBtn.GetComponent<Image>().color = new Color(0.15f, 0.3f, 0.45f, 1f); // Darker Blueish
                leftSideButtons.Add(leftSettingsBtn.GetComponent<RectTransform>());

                // Follow
                GameObject leftFollowBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Static", btnFontSize, 0, startY - spacing * 2 - groupGap * 2, AnchorPresets.centre, ToggleFollowMode);
                leftFollowBtnImage = leftFollowBtn.GetComponent<Image>();
                leftFollowBtnText = leftFollowBtn.GetComponentInChildren<Text>();
                leftFollowBtnImage.color = new Color(0.15f, 0.45f, 0.6f, 1f); // Darker Follow Blue
                leftSideButtons.Add(leftFollowBtn.GetComponent<RectTransform>());

                // Clone (Gray)
                GameObject leftCloneBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Clone", btnFontSize, 0, startY - spacing * 3 - groupGap * 2, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, false);
                });
                leftCloneBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f); // Darker Gray
                leftSideButtons.Add(leftCloneBtn.GetComponent<RectTransform>());

                // Load Random (always available)
                leftLoadRandomBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Random", btnFontSize, 0, 0, AnchorPresets.centre, LoadRandom);
                leftLoadRandomBtn.GetComponent<Image>().color = new Color(0.35f, 0.25f, 0.55f, 1f);
                leftSideButtons.Add(leftLoadRandomBtn.GetComponent<RectTransform>());

                // Category (Red)
                GameObject leftCatBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Category", btnFontSize, 0, startY - spacing * 4 - groupGap * 3, AnchorPresets.centre, () => ToggleLeft(ContentType.Category));
                leftCategoryBtnImage = leftCatBtn.GetComponent<Image>();
                leftCategoryBtnImage.color = ColorCategory;
                leftCategoryBtnText = leftCatBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftCatBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftCatBtn, () => ToggleRight(ContentType.Category));
                
                // Creator (Green)
                GameObject leftCreatorBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 6 - groupGap * 3, AnchorPresets.centre, () => ToggleLeft(ContentType.Creator));
                leftCreatorBtnImage = leftCreatorBtn.GetComponent<Image>();
                leftCreatorBtnImage.color = ColorCreator;
                leftCreatorBtnText = leftCreatorBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftCreatorBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftCreatorBtn, () => ToggleRight(ContentType.Creator));

                // Target (Dropdown-like)
                GameObject leftTargetBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Target: None", 14, 0, startY - spacing * 8 - groupGap * 4, AnchorPresets.centre, () => CycleTarget(true));
                leftTargetBtnImage = leftTargetBtn.GetComponent<Image>();
                leftTargetBtnImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                leftTargetBtnText = leftTargetBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftTargetBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftTargetBtn, () => CycleTarget(false));

                // Apply Mode (Left)
                GameObject leftApplyModeBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "2-Click", btnFontSize, 0, startY - spacing * 9 - groupGap * 4, AnchorPresets.centre, ToggleApplyMode);
                leftApplyModeBtnImage = leftApplyModeBtn.GetComponent<Image>();
                leftApplyModeBtnText = leftApplyModeBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftApplyModeBtn.GetComponent<RectTransform>());

                // Replace Toggle (Left)
                GameObject leftReplaceBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 10 - groupGap * 4, AnchorPresets.centre, ToggleReplaceMode);
                leftReplaceBtnImage = leftReplaceBtn.GetComponent<Image>();
                leftReplaceBtnText = leftReplaceBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftReplaceBtn.GetComponent<RectTransform>());

                leftSaveBtnGO = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Save", btnFontSize, 0, startY - spacing * 11 - groupGap * 4, AnchorPresets.centre, () => {
                    try
                    {
                        ToggleSaveSubmenuFromSideButtons();
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Save (Left) exception: " + ex);
                    }
                });
                leftSaveBtnGO.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.2f, 1f);
                leftSaveBtnGO.GetComponentInChildren<Text>().color = Color.white;
                leftSideButtons.Add(leftSaveBtnGO.GetComponent<RectTransform>());
                leftSaveBtnGO.SetActive(false);

                try
                {
                    EventTrigger et = leftSaveBtnGO.GetComponent<EventTrigger>();
                    if (et == null) et = leftSaveBtnGO.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuParentHoverCount++;
                            saveSubmenuParentHovered = true;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                            if (!saveSubmenuOpen) ToggleSaveSubmenuFromSideButtons();
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuParentHoverCount--;
                            if (saveSubmenuParentHoverCount < 0) saveSubmenuParentHoverCount = 0;
                            saveSubmenuParentHovered = saveSubmenuParentHoverCount > 0;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                try
                {
                    leftSaveSubmenuPanelGO = new GameObject("LeftSaveSubmenuPanel");
                    leftSaveSubmenuPanelGO.transform.SetParent(leftSideContainer.transform, false);
                    RectTransform prt = leftSaveSubmenuPanelGO.AddComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0.5f, 0.5f);
                    prt.anchorMax = new Vector2(0.5f, 0.5f);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(btnWidth * 1.6f, btnHeight);
                    prt.anchoredPosition = Vector2.zero;

                    AddHoverDelegate(leftSaveSubmenuPanelGO);

                    Image pimg = leftSaveSubmenuPanelGO.AddComponent<Image>();
                    pimg.color = new Color(0, 0, 0, 0.01f);
                    pimg.raycastTarget = true;

                    EventTrigger pet = leftSaveSubmenuPanelGO.AddComponent<EventTrigger>();
                    var pe = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    pe.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuOptionsHoverCount++;
                            saveSubmenuOptionsHovered = true;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(pe);

                    var px = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    px.callback.AddListener((data) => {
                        try
                        {
                            saveSubmenuOptionsHoverCount--;
                            if (saveSubmenuOptionsHoverCount < 0) saveSubmenuOptionsHoverCount = 0;
                            saveSubmenuOptionsHovered = saveSubmenuOptionsHoverCount > 0;
                            saveSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(px);

                    leftSaveSubmenuPanelGO.SetActive(false);
                }
                catch { }

                for (int i = 0; i < SaveSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(leftSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    leftSaveSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        int buttonIndex = i; // local copy for closure
                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                saveSubmenuOptionsHoverCount++;
                                saveSubmenuOptionsHovered = true;
                                saveSubmenuLastHoverTime = Time.unscaledTime;

                                if (buttonIndex == 4 && !saveSubmenuMoreVisible && saveSubmenuOpen)
                                {
                                    saveSubmenuMoreVisible = true;
                                    PopulateSaveSubmenuButtons();
                                    PositionSaveSubmenuButtons();
                                }
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                saveSubmenuOptionsHoverCount--;
                                if (saveSubmenuOptionsHoverCount < 0) saveSubmenuOptionsHoverCount = 0;
                                saveSubmenuOptionsHovered = saveSubmenuOptionsHoverCount > 0;
                                saveSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                // Scene Context (Left)
                leftRemoveAtomBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Remove\nAtom", 18, 0, 0, AnchorPresets.centre, () => {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        ToggleAtomSubmenuFromSideButtons();
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Atom (Left) exception: " + ex);
                    }
                });
                leftRemoveAtomBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);
                leftRemoveAtomBtn.GetComponentInChildren<Text>().color = Color.white;
                leftSideButtons.Add(leftRemoveAtomBtn.GetComponent<RectTransform>());
                leftRemoveAtomBtn.SetActive(false);

                try
                {
                    EventTrigger et = leftRemoveAtomBtn.GetComponent<EventTrigger>();
                    if (et == null) et = leftRemoveAtomBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            atomSubmenuParentHoverCount++;
                            atomSubmenuParentHovered = true;
                            atomSubmenuLastHoverTime = Time.unscaledTime;
                            if (!atomSubmenuOpen) ToggleAtomSubmenuFromSideButtons();
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            atomSubmenuParentHoverCount--;
                            if (atomSubmenuParentHoverCount < 0) atomSubmenuParentHoverCount = 0;
                            atomSubmenuParentHovered = atomSubmenuParentHoverCount > 0;
                            atomSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                // Hub (Orange)
                GameObject leftHubBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Hub", btnFontSize, 0, startY - spacing * 11 - groupGap * 4, AnchorPresets.centre, () => {
                    if (VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode)
                    {
                        ToggleLeft(ContentType.Hub);
                    }
                    else
                    {
                        VamHookPlugin.singleton?.OpenHubBrowse();
                        Hide();
                    }
                });
                leftHubBtnGO = leftHubBtn;
                leftHubBtnImage = leftHubBtn.GetComponent<Image>();
                leftHubBtnImage.color = ColorHub;
                leftHubBtnText = leftHubBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftHubBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftHubBtn, () => {
                    if (VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode)
                    {
                        ToggleRight(ContentType.Hub);
                    }
                    else
                    {
                        VamHookPlugin.singleton?.OpenHubBrowse();
                        Hide();
                    }
                });

                // Undo (Left)
                GameObject leftUndoBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 12 - groupGap * 5, AnchorPresets.centre, Undo);
                leftUndoBtnGO = leftUndoBtn;
                leftUndoBtn.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.15f, 1f); // Darker Brown/Orange
                leftSideButtons.Add(leftUndoBtn.GetComponent<RectTransform>());

                // Redo (Left)
                GameObject leftRedoBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Redo", btnFontSize, 0, startY - spacing * 13 - groupGap * 5, AnchorPresets.centre, Redo);
                leftRedoBtnGO = leftRedoBtn;
                leftRedoBtn.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.15f, 1f); // Darker Brown/Orange
                leftSideButtons.Add(leftRedoBtn.GetComponent<RectTransform>());

                // Context Actions (Left) - inserted above Undo
                leftRemoveAllClothingBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Remove\nClothing", 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Clothing (Left)");
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        LogUtil.Log($"[VPB] Remove Clothing (Left) resolved target: {(target != null ? target.uid + " (" + target.type + ")" : "<null>")}");
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        UIDraggableItem dragger = leftRemoveAllClothingBtn.GetComponent<UIDraggableItem>();
                        if (dragger == null) dragger = leftRemoveAllClothingBtn.AddComponent<UIDraggableItem>();
                        dragger.Panel = this;
                        dragger.RemoveAllClothing(target);

                        // Robust UI sync: close submenu immediately after remove-all.
                        CloseClothingSubmenuUI();
                        UpdateSideButtonPositions();
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing (Left) exception: " + ex);
                    }
                });
                leftRemoveAllClothingBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);
                leftRemoveAllClothingBtn.GetComponentInChildren<Text>().color = Color.white;
                leftSideButtons.Add(leftRemoveAllClothingBtn.GetComponent<RectTransform>());
                leftRemoveAllClothingBtn.SetActive(false);

                try { UpdateUndoRedoButtonLabels(); } catch { }

                try
                {
                    EventTrigger et = leftRemoveAllClothingBtn.GetComponent<EventTrigger>();
                    if (et == null) et = leftRemoveAllClothingBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            clothingSubmenuParentHoverCount++;
                            clothingSubmenuParentHovered = true;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                            if (!clothingSubmenuOpen) ToggleClothingSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            clothingSubmenuParentHoverCount--;
                            if (clothingSubmenuParentHoverCount < 0) clothingSubmenuParentHoverCount = 0;
                            clothingSubmenuParentHovered = clothingSubmenuParentHoverCount > 0;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                leftRemoveClothingExpandBtn = UI.CreateUIButton(leftRemoveAllClothingBtn, btnHeight, btnHeight, "<", 18, -104, 0, AnchorPresets.middleCenter, () => {
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        ToggleClothingSubmenuFromSideButtons(target);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing slot picker exception: " + ex);
                    }
                });
                try
                {
                    EventTrigger et = leftRemoveClothingExpandBtn.GetComponent<EventTrigger>();
                    if (et == null) et = leftRemoveClothingExpandBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            if (!clothingSubmenuOpen) ToggleClothingSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);
                }
                catch { }
                leftRemoveClothingExpandBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                leftRemoveClothingExpandBtn.GetComponentInChildren<Text>().color = Color.white;
                leftRemoveClothingExpandBtn.SetActive(false);

                // Clothing Submenu Hover Panel (Left) - catches pointer over gaps
                try
                {
                    leftRemoveClothingSubmenuPanelGO = new GameObject("LeftRemoveClothingSubmenuPanel");
                    leftRemoveClothingSubmenuPanelGO.transform.SetParent(leftSideContainer.transform, false);
                    RectTransform prt = leftRemoveClothingSubmenuPanelGO.AddComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0.5f, 0.5f);
                    prt.anchorMax = new Vector2(0.5f, 0.5f);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(btnWidth * 1.6f, btnHeight);
                    prt.anchoredPosition = Vector2.zero;

                    AddHoverDelegate(leftRemoveClothingSubmenuPanelGO);

                    Image pimg = leftRemoveClothingSubmenuPanelGO.AddComponent<Image>();
                    pimg.color = new Color(0, 0, 0, 0.01f);
                    pimg.raycastTarget = true;

                    EventTrigger pet = leftRemoveClothingSubmenuPanelGO.AddComponent<EventTrigger>();
                    var pe = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    pe.callback.AddListener((data) => {
                        try
                        {
                            clothingSubmenuOptionsHoverCount++;
                            clothingSubmenuOptionsHovered = true;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(pe);

                    var px = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    px.callback.AddListener((data) => {
                        try
                        {
                            clothingSubmenuOptionsHoverCount--;
                            if (clothingSubmenuOptionsHoverCount < 0) clothingSubmenuOptionsHoverCount = 0;
                            clothingSubmenuOptionsHovered = clothingSubmenuOptionsHoverCount > 0;
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(px);

                    leftRemoveClothingSubmenuPanelGO.SetActive(false);
                }
                catch { }

                // Atom Submenu Buttons (Left) - pooled
                for (int i = 0; i < AtomSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(leftSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    leftSideButtons.Add(b.GetComponent<RectTransform>());
                    leftRemoveAtomSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                atomSubmenuOptionsHoverCount++;
                                atomSubmenuOptionsHovered = true;
                                atomSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                atomSubmenuOptionsHoverCount--;
                                if (atomSubmenuOptionsHoverCount < 0) atomSubmenuOptionsHoverCount = 0;
                                atomSubmenuOptionsHovered = atomSubmenuOptionsHoverCount > 0;
                                atomSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                for (int i = 0; i < ClothingSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(leftSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    leftSideButtons.Add(b.GetComponent<RectTransform>());
                    leftRemoveClothingSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount++;
                                clothingSubmenuOptionsHovered = true;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount--;
                                if (clothingSubmenuOptionsHoverCount < 0) clothingSubmenuOptionsHoverCount = 0;
                                clothingSubmenuOptionsHovered = clothingSubmenuOptionsHoverCount > 0;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                // Clothing Visibility Toggle Buttons (Left) - pooled, placed outside submenu items
                for (int i = 0; i < ClothingSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(leftSideContainer, 80f, btnHeight, "Hide", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                    leftSideButtons.Add(b.GetComponent<RectTransform>());
                    leftRemoveClothingVisibilityToggleButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();

                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount++;
                                clothingSubmenuOptionsHovered = true;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount--;
                                if (clothingSubmenuOptionsHoverCount < 0) clothingSubmenuOptionsHoverCount = 0;
                                clothingSubmenuOptionsHovered = clothingSubmenuOptionsHoverCount > 0;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

                leftRemoveAllHairBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Remove\nHair", 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Hair (Left)");
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        LogUtil.Log($"[VPB] Remove Hair (Left) resolved target: {(target != null ? target.uid + " (" + target.type + ")" : "<null>")}");
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        UIDraggableItem dragger = leftRemoveAllHairBtn.GetComponent<UIDraggableItem>();
                        if (dragger == null) dragger = leftRemoveAllHairBtn.AddComponent<UIDraggableItem>();
                        dragger.Panel = this;
                        dragger.RemoveAllHair(target);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair (Left) exception: " + ex);
                    }
                });
                leftRemoveAllHairBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);
                leftRemoveAllHairBtn.GetComponentInChildren<Text>().color = Color.white;
                leftSideButtons.Add(leftRemoveAllHairBtn.GetComponent<RectTransform>());
                leftRemoveAllHairBtn.SetActive(false);

                try
                {
                    EventTrigger et = leftRemoveAllHairBtn.GetComponent<EventTrigger>();
                    if (et == null) et = leftRemoveAllHairBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            hairSubmenuParentHoverCount++;
                            hairSubmenuParentHovered = true;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                            if (!hairSubmenuOpen) ToggleHairSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);

                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try
                        {
                            hairSubmenuParentHoverCount--;
                            if (hairSubmenuParentHoverCount < 0) hairSubmenuParentHoverCount = 0;
                            hairSubmenuParentHovered = hairSubmenuParentHoverCount > 0;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    et.triggers.Add(exitEntry);
                }
                catch { }

                leftRemoveHairExpandBtn = UI.CreateUIButton(leftRemoveAllHairBtn, btnHeight, btnHeight, "<", 18, -104, 0, AnchorPresets.middleCenter, () => {
                    try
                    {
                        Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                        if (target == null)
                        {
                            LogUtil.LogWarning("[VPB] Please select a Person atom.");
                            return;
                        }
                        ToggleHairSubmenuFromSideButtons(target);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair slot picker exception: " + ex);
                    }
                });
                try
                {
                    EventTrigger et = leftRemoveHairExpandBtn.GetComponent<EventTrigger>();
                    if (et == null) et = leftRemoveHairExpandBtn.AddComponent<EventTrigger>();
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener((data) => {
                        try
                        {
                            Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                            if (target == null) return;
                            if (!hairSubmenuOpen) ToggleHairSubmenuFromSideButtons(target);
                        }
                        catch { }
                    });
                    et.triggers.Add(entry);
                }
                catch { }
                leftRemoveHairExpandBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                leftRemoveHairExpandBtn.GetComponentInChildren<Text>().color = Color.white;
                leftRemoveHairExpandBtn.SetActive(false);

                try
                {
                    leftRemoveHairSubmenuGapPanelGO = new GameObject("LeftRemoveHairSubmenuGapPanel");
                    leftRemoveHairSubmenuGapPanelGO.transform.SetParent(leftSideContainer.transform, false);
                    RectTransform prt = leftRemoveHairSubmenuGapPanelGO.AddComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0.5f, 0.5f);
                    prt.anchorMax = new Vector2(0.5f, 0.5f);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(btnWidth * 1.6f, btnHeight);
                    prt.anchoredPosition = Vector2.zero;

                    AddHoverDelegate(leftRemoveHairSubmenuGapPanelGO);

                    Image pimg = leftRemoveHairSubmenuGapPanelGO.AddComponent<Image>();
                    pimg.color = new Color(0, 0, 0, 0.01f);
                    pimg.raycastTarget = true;

                    EventTrigger pet = leftRemoveHairSubmenuGapPanelGO.AddComponent<EventTrigger>();
                    var pe = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    pe.callback.AddListener((data) => {
                        try
                        {
                            hairSubmenuOptionsHoverCount++;
                            hairSubmenuOptionsHovered = true;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(pe);

                    var px = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    px.callback.AddListener((data) => {
                        try
                        {
                            hairSubmenuOptionsHoverCount--;
                            if (hairSubmenuOptionsHoverCount < 0) hairSubmenuOptionsHoverCount = 0;
                            hairSubmenuOptionsHovered = hairSubmenuOptionsHoverCount > 0;
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                        }
                        catch { }
                    });
                    pet.triggers.Add(px);

                    leftRemoveHairSubmenuGapPanelGO.SetActive(false);
                }
                catch { }

                // Hair Submenu Buttons (Left) - pooled, treated as real side buttons
                leftRemoveHairSubmenuStartIndex = leftSideButtons.Count;
                for (int i = 0; i < HairSubmenuMaxButtons; i++)
                {
                    GameObject b = UI.CreateUIButton(leftSideContainer, btnWidth * 1.6f, btnHeight, "", 16, 0, 0, AnchorPresets.centre, null);
                    b.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    leftSideButtons.Add(b.GetComponent<RectTransform>());
                    leftRemoveHairSubmenuButtons.Add(b);
                    b.SetActive(false);
                    AddHoverDelegate(b);

                    try
                    {
                        EventTrigger etb = b.GetComponent<EventTrigger>();
                        if (etb == null) etb = b.AddComponent<EventTrigger>();
                        var be = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        be.callback.AddListener((data) => {
                            try
                            {
                                hairSubmenuOptionsHoverCount++;
                                hairSubmenuOptionsHovered = true;
                                hairSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(be);

                        var bx = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        bx.callback.AddListener((data) => {
                            try
                            {
                                hairSubmenuOptionsHoverCount--;
                                if (hairSubmenuOptionsHoverCount < 0) hairSubmenuOptionsHoverCount = 0;
                                hairSubmenuOptionsHovered = hairSubmenuOptionsHoverCount > 0;
                                hairSubmenuLastHoverTime = Time.unscaledTime;
                            }
                            catch { }
                        });
                        etb.triggers.Add(bx);
                    }
                    catch { }
                }

UpdateDesktopModeButton();
            }

            // Main Content Area
            GameObject scrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            scrollRect = scrollGO.GetComponent<ScrollRect>();
            contentScrollRT = scrollGO.GetComponent<RectTransform>();
            contentScrollRT.offsetMin = new Vector2(20, 110);
            contentScrollRT.offsetMax = new Vector2(-230, -65); // Default top margin (Quick Filters hidden)
            lastScrollTime = Time.unscaledTime;
            if (scrollRect != null)
            {
                scrollRect.onValueChanged.AddListener((v) => { lastScrollTime = Time.unscaledTime; });
            }
            
            contentGO = scrollRect.content.gameObject;
            CreateLoadingOverlay(scrollRect != null && scrollRect.viewport != null ? scrollRect.viewport.gameObject : scrollGO);

            // Clean up legacy layout components that interfere with virtualization
            var legacyGLG = contentGO.GetComponent<GridLayoutGroup>();
            if (legacyGLG != null) DestroyImmediate(legacyGLG);
            var legacyCSF = contentGO.GetComponent<ContentSizeFitter>();
            if (legacyCSF != null) DestroyImmediate(legacyCSF);
            var legacyVLG = contentGO.GetComponent<VerticalLayoutGroup>();
            if (legacyVLG != null) DestroyImmediate(legacyVLG);

            // Initialize RecyclingGridView immediately instead of legacy layout components
            recyclingGrid = contentGO.AddComponent<RecyclingGridView>();
            recyclingGrid.scrollRect = scrollRect;
            recyclingGrid.content = contentGO.GetComponent<RectTransform>();
            
            // Set initial adaptive config
            float minSize = 200f;
            recyclingGrid.SetGridConfig(100, 100, 10f, 10f, gridColumnCount);
            recyclingGrid.SetAdaptiveConfig(true, minSize, gridColumnCount, false);

            // Pagination Controls (Bottom Left)
            CreatePaginationControls();

            // Status Bar (Now shares the hoverPathRT container)
            GameObject statusBarGO = new GameObject("StatusBar");
            statusBarGO.transform.SetParent(hoverPathRT.transform, false);
            statusBarText = statusBarGO.AddComponent<Text>();
            statusBarText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statusBarText.fontSize = 22;
            statusBarText.color = Color.white;
            var statusShadow = statusBarGO.AddComponent<Shadow>();
            statusShadow.effectColor = new Color(0, 0, 0, 0.8f);
            statusShadow.effectDistance = new Vector2(1, -1);
            statusBarText.alignment = TextAnchor.MiddleCenter;
            statusBarText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusBarText.verticalOverflow = VerticalWrapMode.Truncate;
            statusBarText.raycastTarget = false;
            
            RectTransform statusRT = statusBarGO.GetComponent<RectTransform>();
            statusRT.anchorMin = Vector2.zero;
            statusRT.anchorMax = Vector2.one;
            statusRT.sizeDelta = Vector2.zero;
            statusRT.anchoredPosition = Vector2.zero;

            // Pointer Dot
            pointerDotGO = new GameObject("PointerDot");
            pointerDotGO.transform.SetParent(canvasGO.transform, false);
            Image dotImg = pointerDotGO.AddComponent<Image>();
            dotImg.color = new Color(1, 1, 1, 0.5f);
            // We use a dot texture if available, otherwise just a small circle/square
            pointerDotGO.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 8);
            pointerDotGO.SetActive(false);

            CreateResizeHandles();

            // Minimize button
            GameObject minimizeBtn = UI.CreateUIButton(backgroundBoxGO, 50, 50, "_", 30, 0, 0, AnchorPresets.topRight, () => {
                Hide();
            });
            RectTransform minRT = minimizeBtn.GetComponent<RectTransform>();
            minRT.anchoredPosition = new Vector2(-55, 0);
            minimizeBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
            AddHoverDelegate(minimizeBtn);

            // Close button - Rendered last to be on top
            GameObject closeBtn = UI.CreateUIButton(backgroundBoxGO, 50, 50, "X", 30, 0, 0, AnchorPresets.topRight, () => {
                Close();
            });
            closeBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
            AddHoverDelegate(closeBtn);

            UpdateSideButtonPositions();
            UpdateSideButtonsVisibility();
            UpdateDesktopModeButton();
            UpdateLayout();
            UpdateFollowButtonState();
            UpdateReplaceButtonState();
            UpdateApplyModeButtonState();
        }
    }

}
