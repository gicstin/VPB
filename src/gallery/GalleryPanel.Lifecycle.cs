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
            bool fixedMode = isFixedLocally;

            if (leftSideContainer != null) 
            {
                if (isCollapsed) leftSideContainer.SetActive(false);
                else leftSideContainer.SetActive(mode == "Both" || mode == "Left");
            }
            
            if (rightSideContainer != null) 
            {
                if (fixedMode || isCollapsed) rightSideContainer.SetActive(false);
                else rightSideContainer.SetActive(mode == "Both" || mode == "Right");
            }

            bool showLeftSide = !isCollapsed && (mode == "Both" || mode == "Left");
            bool showRightSide = !fixedMode && !isCollapsed && (mode == "Both" || mode == "Right");

            if (leftClearCreatorBtn != null) leftClearCreatorBtn.SetActive(showLeftSide && !string.IsNullOrEmpty(currentCreator));
            if (leftClearStatusBtn != null) leftClearStatusBtn.SetActive(showLeftSide && !string.IsNullOrEmpty(currentStatus));
            if (rightClearCreatorBtn != null) rightClearCreatorBtn.SetActive(showRightSide && !string.IsNullOrEmpty(currentCreator));
            if (rightClearStatusBtn != null) rightClearStatusBtn.SetActive(showRightSide && !string.IsNullOrEmpty(currentStatus));

            if (leftClearCreatorBtn != null && leftClearCreatorBtn.activeSelf) UpdateClearButtonPosition(false, ContentType.Creator);
            if (leftClearStatusBtn != null && leftClearStatusBtn.activeSelf) UpdateClearButtonPosition(false, ContentType.Status);
            if (rightClearCreatorBtn != null && rightClearCreatorBtn.activeSelf) UpdateClearButtonPosition(true, ContentType.Creator);
            if (rightClearStatusBtn != null && rightClearStatusBtn.activeSelf) UpdateClearButtonPosition(true, ContentType.Status);
        }

        private void UpdateClearButtonPosition(bool isRight, ContentType type)
        {
            GameObject btn = null;
            if (type == ContentType.Creator) btn = isRight ? rightClearCreatorBtn : leftClearCreatorBtn;
            else if (type == ContentType.Status) btn = isRight ? rightClearStatusBtn : leftClearStatusBtn;
            if (btn == null) return;

            // Find the button for this content type
            RectTransform targetBtnRT = null;
            
            // We need to find the specific button rect. 
            // We store them in sideButtons list but we need to know WHICH one corresponds to the type.
            // Indices based on GalleryPanel.UI.cs creation order:
            // 0: Fixed/Floating
            // 1: Settings
            // 2: Follow
            // 3: Clone
            // 4: Category
            // 5: Creator
            // 6: Status
            // 7: Target
            // 8: Apply Mode
            // 9: Replace
            // 10: Hub
            // 11: Undo

            int targetIndex = -1;
            switch(type)
            {
                case ContentType.Creator: targetIndex = 5; break;
                case ContentType.Status: targetIndex = 6; break;
            }

            if (targetIndex >= 0)
            {
                List<RectTransform> list = isRight ? rightSideButtons : leftSideButtons;
                if (targetIndex < list.Count)
                {
                    targetBtnRT = list[targetIndex];
                }
            }

            if (targetBtnRT != null && targetBtnRT.gameObject.activeInHierarchy)
            {
                RectTransform btnRT = btn.GetComponent<RectTransform>();
                // Position "outside" means further away from the center panel.
                // Left side: To the left of the button.
                // Right side: To the right of the button.
                
                float xOffset = isRight ? 65f : -65f; // Button width is 120, center to edge is 60. +5 padding
                
                // But wait, the side containers are at -120 and +120.
                // The clear button is parented to backgroundBoxGO.
                // The side button is parented to sideContainer.
                // We need to convert or just use fixed offsets relative to side container position.
                
                // Side container anchors:
                // Left: (-120, 0)
                // Right: (120, 0)
                
                // Side Button anchor is (0, Y) inside that container.
                // So world position logic or just parent relative logic.
                // The Clear Button is child of backgroundBoxGO.
                
                float btnY = targetBtnRT.anchoredPosition.y; // Y inside container
                
                // The container is centered vertically? No, container anchor is middleLeft/Right.
                // Let's check container setup in CreateUI.
                // leftSideContainer = UI.AddChildGOImage(..., AnchorPresets.middleLeft, ..., new Vector2(-120, 0));
                // So container (0,0) is at (-120, 0) from background left edge?
                // No, AnchorPresets.middleLeft means (0, 0.5) anchor.
                // AnchoredPosition (-120, 0) means shifted left by 120.
                
                // backgroundBoxGO is the parent of ClearButton.
                // ClearButton anchor is middleLeft/Right.
                
                // If ClearButton has AnchorPresets.middleLeft (Left) or middleRight (Right):
                // Left Button X: containerX + xOffset
                // Right Button X: containerX + xOffset
                
                // Wait, "Outside" relative to the gallery.
                // Left Side: Outside is further Left (Negative X).
                // Right Side: Outside is further Right (Positive X).
                
                // Left container is at -120. Button is at 0 X inside it.
                // So button center is at -120.
                // We want clear button at -120 - 60 - 25 = -205?
                // Or maybe just -80 relative to the button center.
                
                float targetX = isRight ? (140f + 65f) : (-140f - 65f);
                
                btnRT.anchoredPosition = new Vector2(targetX, btnY);
            }
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
                VPBConfig.Instance.ConfigChanged -= ApplyCurvatureToChildren;
                VPBConfig.Instance.ConfigChanged -= UpdateFooterFollowStates;
                VPBConfig.Instance.ConfigChanged -= UpdateDesktopModeButton;
                VPBConfig.Instance.ConfigChanged -= UpdateLayout;
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

            if (targetMarkerGO != null)
            {
                Destroy(targetMarkerGO);
                targetMarkerGO = null;
                targetMarkerAtomUid = null;
            }
        }

        private void EnsureTargetMarker()
        {
            if (targetMarkerGO != null) return;
            
            targetMarkerGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            targetMarkerGO.name = "VPB_TargetMarker";
            
            Collider c = targetMarkerGO.GetComponent<Collider>();
            if (c != null) Destroy(c);

            targetMarkerGO.transform.localScale = Vector3.one * 0.08f;

            Renderer r = targetMarkerGO.GetComponent<Renderer>();
            if (r != null)
            {
                Shader unlit = Shader.Find("Unlit/Color");
                if (unlit == null) unlit = Shader.Find("Transparent/Diffuse");
                
                if (unlit != null)
                {
                    Material m = new Material(unlit);
                    m.color = Color.magenta;
                    r.material = m;
                }
            }

            targetMarkerGO.SetActive(false);
        }

        private void UpdateTargetMarker()
        {
            bool shouldShow = hoverCount > 0;
            Atom target = SelectedTargetAtom;
            if (!shouldShow || target == null || target.type != "Person")
            {
                if (targetMarkerGO != null) targetMarkerGO.SetActive(false);
                return;
            }

            EnsureTargetMarker();
            if (targetMarkerGO == null) return;

            Transform desiredParent = (target.mainController != null) ? target.mainController.transform : target.transform;

            if (targetMarkerAtomUid != target.uid || targetMarkerGO.transform.parent != desiredParent)
            {
                targetMarkerAtomUid = target.uid;
                targetMarkerGO.transform.SetParent(desiredParent, false);
                targetMarkerGO.transform.localPosition = Vector3.zero;
                targetMarkerGO.transform.localRotation = Quaternion.identity;
            }

            if (!targetMarkerGO.activeSelf) targetMarkerGO.SetActive(true);
        }

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
            
            fileSortBtnText = fileSortBtn.GetComponentInChildren<Text>();
            
            Button fileSortButton = fileSortBtn.GetComponent<Button>();
            fileSortButton.onClick.RemoveAllListeners();
            fileSortButton.onClick.AddListener(() => CycleSort("Files", fileSortBtnText));
            
            AddRightClickDelegate(fileSortBtn, () => ToggleSortDirection("Files", fileSortBtnText));
            
            // Init File Sort State
            UpdateSortButtonText(fileSortBtnText, GetSortState("Files"));

            // Filter Presets Button
            GameObject qfToggleBtn = UI.CreateUIButton(titleBarGO, 160, 45, "Filter Presets", 20, 0, 0, AnchorPresets.middleCenter, ToggleQuickFilters);
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
                rightSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleRight, 130, 700, new Vector2(140, 0));
                sideButtonGroups.Add(rightSideContainer.AddComponent<CanvasGroup>());

                rightClearCreatorBtn = UI.CreateUIButton(backgroundBoxGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleRight, () => {
                    currentCreator = "";
                    categoriesCached = false;
                    tagsCached = false;
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                    UpdateSideButtonsVisibility();
                });
                rightClearCreatorBtn.GetComponent<Image>().color = ColorCreator;
                rightClearCreatorBtn.GetComponentInChildren<Text>().color = Color.white;
                sideButtonGroups.Add(rightClearCreatorBtn.AddComponent<CanvasGroup>());
                rightClearCreatorBtn.SetActive(false);

                rightClearStatusBtn = UI.CreateUIButton(backgroundBoxGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleRight, () => {
                    currentStatus = "";
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                    UpdateSideButtonsVisibility();
                });
                rightClearStatusBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f, 1f);
                rightClearStatusBtn.GetComponentInChildren<Text>().color = Color.white;
                sideButtonGroups.Add(rightClearStatusBtn.AddComponent<CanvasGroup>());
                rightClearStatusBtn.SetActive(false);

                // Right Toggle Buttons
                int btnFontSize = 20;
                float btnWidth = 120;
                float btnHeight = 50;
                float spacing = 60f;
                float groupGap = 20f;
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
                GameObject rightCreatorBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 5 - groupGap * 3, AnchorPresets.centre, () => {
                    if (isFixedLocally) ToggleLeft(ContentType.Creator); else ToggleRight(ContentType.Creator);
                });
                rightCreatorBtnImage = rightCreatorBtn.GetComponent<Image>();
                rightCreatorBtnImage.color = ColorCreator;
                rightCreatorBtnText = rightCreatorBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightCreatorBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(rightCreatorBtn, () => ToggleRight(ContentType.Creator));

                // Status (Blue)
                GameObject rightStatusBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Status", btnFontSize, 0, startY - spacing * 6 - groupGap * 3, AnchorPresets.centre, () => {
                    if (isFixedLocally) ToggleLeft(ContentType.Status); else ToggleRight(ContentType.Status);
                });
                rightStatusBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f, 1f); // Darker Blue
                rightSideButtons.Add(rightStatusBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(rightStatusBtn, () => ToggleRight(ContentType.Status));

                // Target (Dropdown-like)
                GameObject rightTargetBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Target: None", 14, 0, startY - spacing * 7 - groupGap * 4, AnchorPresets.centre, () => CycleTarget(true));
                rightTargetBtnImage = rightTargetBtn.GetComponent<Image>();
                rightTargetBtnImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                rightTargetBtnText = rightTargetBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightTargetBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(rightTargetBtn, () => CycleTarget(false));

                // Apply Mode (Right)
                GameObject rightApplyModeBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "2-Click", btnFontSize, 0, startY - spacing * 8 - groupGap * 4, AnchorPresets.centre, ToggleApplyMode);
                rightApplyModeBtnImage = rightApplyModeBtn.GetComponent<Image>();
                rightApplyModeBtnText = rightApplyModeBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightApplyModeBtn.GetComponent<RectTransform>());

                // Replace Toggle (Right)
                GameObject rightReplaceBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 9 - groupGap * 4, AnchorPresets.centre, ToggleReplaceMode);
                rightReplaceBtnImage = rightReplaceBtn.GetComponent<Image>();
                rightReplaceBtnText = rightReplaceBtn.GetComponentInChildren<Text>();
                rightSideButtons.Add(rightReplaceBtn.GetComponent<RectTransform>());

                // Hub (Orange)
                GameObject rightHubBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Hub", btnFontSize, 0, startY - spacing * 10 - groupGap * 4, AnchorPresets.centre, () => {
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
                GameObject rightUndoBtn = UI.CreateUIButton(rightSideContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 11 - groupGap * 3, AnchorPresets.centre, Undo);
                rightUndoBtn.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.15f, 1f); // Darker Brown/Orange
                rightSideButtons.Add(rightUndoBtn.GetComponent<RectTransform>());

                // Left Button Container
                leftSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleLeft, 130, 700, new Vector2(-140, 0));
                sideButtonGroups.Add(leftSideContainer.AddComponent<CanvasGroup>());

                leftClearCreatorBtn = UI.CreateUIButton(backgroundBoxGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleLeft, () => {
                    currentCreator = "";
                    categoriesCached = false;
                    tagsCached = false;
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                    UpdateSideButtonsVisibility();
                });
                leftClearCreatorBtn.GetComponent<Image>().color = ColorCreator;
                leftClearCreatorBtn.GetComponentInChildren<Text>().color = Color.white;
                sideButtonGroups.Add(leftClearCreatorBtn.AddComponent<CanvasGroup>());
                leftClearCreatorBtn.SetActive(false);

                leftClearStatusBtn = UI.CreateUIButton(backgroundBoxGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleLeft, () => {
                    currentStatus = "";
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                    UpdateSideButtonsVisibility();
                });
                leftClearStatusBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f, 1f);
                leftClearStatusBtn.GetComponentInChildren<Text>().color = Color.white;
                sideButtonGroups.Add(leftClearStatusBtn.AddComponent<CanvasGroup>());
                leftClearStatusBtn.SetActive(false);

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

                // Category (Red)
                GameObject leftCatBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Category", btnFontSize, 0, startY - spacing * 4 - groupGap * 3, AnchorPresets.centre, () => ToggleLeft(ContentType.Category));
                leftCategoryBtnImage = leftCatBtn.GetComponent<Image>();
                leftCategoryBtnImage.color = ColorCategory;
                leftCategoryBtnText = leftCatBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftCatBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftCatBtn, () => ToggleRight(ContentType.Category));
                
                // Creator (Green)
                GameObject leftCreatorBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 5 - groupGap * 3, AnchorPresets.centre, () => ToggleLeft(ContentType.Creator));
                leftCreatorBtnImage = leftCreatorBtn.GetComponent<Image>();
                leftCreatorBtnImage.color = ColorCreator;
                leftCreatorBtnText = leftCreatorBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftCreatorBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftCreatorBtn, () => ToggleRight(ContentType.Creator));

                // Status (Blue)
                GameObject leftStatusBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Status", btnFontSize, 0, startY - spacing * 6 - groupGap * 3, AnchorPresets.centre, () => ToggleLeft(ContentType.Status));
                leftStatusBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f, 1f); // Darker Blue
                leftSideButtons.Add(leftStatusBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftStatusBtn, () => ToggleRight(ContentType.Status));

                // Target (Dropdown-like)
                GameObject leftTargetBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Target: None", 14, 0, startY - spacing * 7 - groupGap * 4, AnchorPresets.centre, () => CycleTarget(true));
                leftTargetBtnImage = leftTargetBtn.GetComponent<Image>();
                leftTargetBtnImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                leftTargetBtnText = leftTargetBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftTargetBtn.GetComponent<RectTransform>());
                AddRightClickDelegate(leftTargetBtn, () => CycleTarget(false));

                // Apply Mode (Left)
                GameObject leftApplyModeBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "2-Click", btnFontSize, 0, startY - spacing * 8 - groupGap * 4, AnchorPresets.centre, ToggleApplyMode);
                leftApplyModeBtnImage = leftApplyModeBtn.GetComponent<Image>();
                leftApplyModeBtnText = leftApplyModeBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftApplyModeBtn.GetComponent<RectTransform>());

                // Replace Toggle (Left)
                GameObject leftReplaceBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 9 - groupGap * 4, AnchorPresets.centre, ToggleReplaceMode);
                leftReplaceBtnImage = leftReplaceBtn.GetComponent<Image>();
                leftReplaceBtnText = leftReplaceBtn.GetComponentInChildren<Text>();
                leftSideButtons.Add(leftReplaceBtn.GetComponent<RectTransform>());

                // Hub (Orange)
                GameObject leftHubBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Hub", btnFontSize, 0, startY - spacing * 10 - groupGap * 4, AnchorPresets.centre, () => {
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
                GameObject leftUndoBtn = UI.CreateUIButton(leftSideContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 11 - groupGap * 3, AnchorPresets.centre, Undo);
                leftUndoBtn.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.15f, 1f); // Darker Brown/Orange
                leftSideButtons.Add(leftUndoBtn.GetComponent<RectTransform>());

                UpdateDesktopModeButton();
            }

            // Main Content Area
            GameObject scrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            scrollRect = scrollGO.GetComponent<ScrollRect>();
            contentScrollRT = scrollGO.GetComponent<RectTransform>();
            contentScrollRT.offsetMin = new Vector2(20, 70);
            contentScrollRT.offsetMax = new Vector2(-230, -65); // Default top margin (Quick Filters hidden)
            
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
            adaptive.forcedColumnCount = gridColumnCount;

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
                if (Gallery.singleton != null) Gallery.singleton.RemovePanel(this);
                
                // Destroy canvas explicitly
                if (canvas != null)
                {
                    if (SuperController.singleton != null) SuperController.singleton.RemoveCanvas(canvas);
                    Destroy(canvas.gameObject);
                }
                
                Destroy(this.gameObject);
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
            if (canvas != null && VPBConfig.Instance != null)
            {
                UpdateTargetMarker();

                if (isFixedLocally)
                {
                    bool autoCollapse = VPBConfig.Instance.DesktopFixedAutoCollapse;
                    if (collapseTriggerGO != null) collapseTriggerGO.SetActive(autoCollapse);

                    if (autoCollapse)
                    {
                        if (isCollapsed)
                        {
                            if (isHoveringTrigger)
                            {
                                SetCollapsed(false);
                            }
                        }
                        else
                        {
                            // Manual hover check for trigger area when it is NOT a raycast target (to avoid blocking scrollbar)
                            bool isHoveringTriggerManual = false;
                            if (collapseTriggerGO != null)
                            {
                                RectTransform ctRT = collapseTriggerGO.GetComponent<RectTransform>();
                                Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : null; // Overlay mode uses null cam
                                isHoveringTriggerManual = RectTransformUtility.RectangleContainsScreenPoint(ctRT, Input.mousePosition, cam);
                            }

                            // If NOT hovering gallery and NOT hovering side buttons and NOT hovering trigger, collapse after delay
                            bool isHoveringAny = hoverCount > 0 || isHoveringTrigger || isHoveringTriggerManual || (settingsPanel != null && settingsPanel.settingsPaneGO != null && settingsPanel.settingsPaneGO.activeSelf);
                            if (!isHoveringAny)
                            {
                                collapseTimer += Time.deltaTime;
                                if (collapseTimer >= 1.0f) // 1 second delay
                                {
                                    SetCollapsed(true);
                                }
                            }
                            else
                            {
                                collapseTimer = 0f;
                            }
                        }
                    }
                    else if (isCollapsed)
                    {
                        SetCollapsed(false);
                    }

                    if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                        canvas.transform.localScale = Vector3.one;
                        
                        if (dragger != null) dragger.enabled = false;
                        foreach (Transform child in backgroundBoxGO.transform)
                        {
                            if (child.name.StartsWith("ResizeHandle_")) child.gameObject.SetActive(false);
                        }
                    }

                    // Always update anchors in Fixed mode to support height toggles and screen resizing
                    RectTransform bgRT = backgroundBoxGO.GetComponent<RectTransform>();
                    float leftRatio = 1.618f / 2.618f;
                    
                    float bottomAnchor = 0f;
                    if (VPBConfig.Instance.DesktopFixedHeightMode == 1) bottomAnchor = 1f / 3f;
                    else if (VPBConfig.Instance.DesktopFixedHeightMode == 2) bottomAnchor = 0.5f;

                    if (bgRT.anchorMin.y != bottomAnchor || bgRT.anchorMin.x != leftRatio)
                    {
                        bgRT.anchorMin = new Vector2(leftRatio, bottomAnchor);
                        bgRT.anchorMax = new Vector2(1, 1);
                        bgRT.offsetMin = Vector2.zero;
                        bgRT.offsetMax = Vector2.zero;
                        bgRT.anchoredPosition = isCollapsed ? new Vector2(bgRT.rect.width, 0) : Vector2.zero;
                        
                        if (collapseTriggerGO != null)
                        {
                            Image img = collapseTriggerGO.GetComponent<Image>();
                            if (img != null) 
                            {
                                img.color = isCollapsed ? new Color(0.15f, 0.15f, 0.15f, 0.4f) : new Color(1, 1, 1, 0f);
                                img.raycastTarget = isCollapsed;
                            }
                        }
                        if (collapseHandleText != null)
                        {
                            collapseHandleText.gameObject.SetActive(isCollapsed);
                        }

                        UpdateSideButtonsVisibility();
                    }
                }
                else
                {
                    if (collapseTriggerGO != null) collapseTriggerGO.SetActive(false);
                    if (isCollapsed) SetCollapsed(false);

                    if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        canvas.renderMode = RenderMode.WorldSpace;
                        canvas.worldCamera = Camera.main;
                        canvas.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                        
                        RectTransform bgRT = backgroundBoxGO.GetComponent<RectTransform>();
                        bgRT.anchorMin = new Vector2(0.5f, 0.5f);
                        bgRT.anchorMax = new Vector2(0.5f, 0.5f);
                        bgRT.sizeDelta = new Vector2(1200, 800);
                        bgRT.anchoredPosition = Vector2.zero;
                        
                        UpdateSideButtonsVisibility();

                        if (dragger != null) dragger.enabled = true;
                        foreach (Transform child in backgroundBoxGO.transform)
                        {
                            if (child.name.StartsWith("ResizeHandle_")) child.gameObject.SetActive(true);
                        }

                        RepositionInFront();
                    }
                }
            }

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
                // Hover detection for items in scene removed as per request
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

            if (canvas != null)
            {
                if (_cachedCamera == null || !_cachedCamera.isActiveAndEnabled)
                    _cachedCamera = Camera.main;

                if (_cachedCamera != null)
                {
                    float now = Time.unscaledTime;
                    bool fixedMode = isFixedLocally;

                    // Position and Rotation following throttled for VR comfort (discrete updates)
                    if (!fixedMode && (lastFollowUpdateTime <= 0f || now - lastFollowUpdateTime >= FollowUpdateInterval))
                    {
                        lastFollowUpdateTime = now;
                        
                        if (followUser)
                        {
                            if (!offsetsInitialized)
                            {
                                Vector3 offset = canvas.transform.position - _cachedCamera.transform.position;
                                followYOffset = offset.y;
                                Vector3 horizontalDiff = new Vector3(offset.x, 0, offset.z);
                                followXZOffset = new Vector2(horizontalDiff.x, horizontalDiff.z);
                                followDistanceReference = horizontalDiff.magnitude;
                                offsetsInitialized = true;
                            }
                            
                            // Handle Position Following
                            Vector3 camPos = _cachedCamera.transform.position;
                            Vector3 currentPos = canvas.transform.position;
                            Vector3 targetPos = currentPos;

                            // Capture manual movement as new reference if not following OR if being dragged
                            if (!VPBConfig.Instance.IsFollowEnabled(VPBConfig.Instance.FollowEyeHeight) || (dragger != null && dragger.isDragging))
                            {
                                followYOffset = currentPos.y - camPos.y;
                            }

                            if (!VPBConfig.Instance.IsFollowEnabled(VPBConfig.Instance.FollowDistance) || (dragger != null && dragger.isDragging))
                            {
                                Vector3 horizontalDiff = new Vector3(currentPos.x - camPos.x, 0, currentPos.z - camPos.z);
                                followXZOffset = new Vector2(horizontalDiff.x, horizontalDiff.z);
                                followDistanceReference = horizontalDiff.magnitude;
                            }

                            // Horizontal Following (Strictly respect followDistanceReference)
                            if (VPBConfig.Instance.IsFollowEnabled(VPBConfig.Instance.FollowDistance))
                            {
                                Vector3 hOffset = new Vector3(followXZOffset.x, 0, followXZOffset.y);
                                if (hOffset.sqrMagnitude < 0.0001f) hOffset = Vector3.forward;
                                Vector3 hTarget = camPos + hOffset.normalized * followDistanceReference;
                                targetPos.x = hTarget.x;
                                targetPos.z = hTarget.z;
                            }

                            // Vertical Following (Eye Height)
                            if (VPBConfig.Instance.IsFollowEnabled(VPBConfig.Instance.FollowEyeHeight))
                            {
                                targetPos.y = camPos.y + followYOffset;
                            }
                            else
                            {
                                // Stay at current Y
                                targetPos.y = currentPos.y;
                            }

                            // Only move if position changed by more than threshold AND we're not currently dragging
                            bool bypassThreshold = VPBConfig.Instance.IsLoadingScene;
                            bool isDragging = dragger != null && dragger.isDragging;

                            if (!isDragging && (bypassThreshold || Vector3.Distance(currentPos, targetPos) > VPBConfig.Instance.MovementThreshold))
                            {
                                canvas.transform.position = targetPos;
                            }

                            // Handle Rotation Following (Respect FollowAngle setting)
                            if (VPBConfig.Instance.IsFollowEnabled(VPBConfig.Instance.FollowAngle))
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

                        // Curvature logic (Independent of follow mode)
                        if (!fixedMode && VPBConfig.Instance.EnableCurvature)
                        {
                            ApplyCurvatureToChildren();
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

        public void TriggerCurvatureRefresh()
        {
            ApplyCurvatureToChildren();
        }

        public void RepositionInFront()
        {
            if (Camera.main != null)
            {
                Transform cam = Camera.main.transform;
                canvas.transform.position = cam.position + cam.forward * 1.5f;
                canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - cam.position, Vector3.up);
                offsetsInitialized = false; // Reset follow offsets
            }
        }
    }
}
