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
        private struct SideButtonLayoutEntry
        {
            public int buttonIndex;
            public int row;
            public int gapTier;

            public SideButtonLayoutEntry(int buttonIndex, int row, int gapTier)
            {
                this.buttonIndex = buttonIndex;
                this.row = row;
                this.gapTier = gapTier;
            }
        }
        private void CreatePaginationControls()
        {
            // Pagination Container (Footer Bar)
            GameObject pageContainer = new GameObject("PaginationContainer");
            pageContainer.transform.SetParent(backgroundBoxGO.transform, false);
            paginationRT = pageContainer.AddComponent<RectTransform>();
            paginationRT.anchorMin = new Vector2(0, 0);
            paginationRT.anchorMax = new Vector2(1, 0); // Stretch horizontally
            paginationRT.pivot = new Vector2(0.5f, 0);
            paginationRT.anchoredPosition = new Vector2(0, 0);
            paginationRT.sizeDelta = new Vector2(0, 40); // Footer bar height for buttons
            
            HorizontalLayoutGroup footerHLG = pageContainer.AddComponent<HorizontalLayoutGroup>();
            footerHLG.padding = new RectOffset(60, 10, 0, 0); // 60 padding on left for resize handle
            footerHLG.childControlWidth = true;
            footerHLG.childControlHeight = true;
            footerHLG.childForceExpandWidth = true;

            // --- Left Section (Follow Controls) ---
            GameObject leftSection = new GameObject("LeftSection");
            leftSection.transform.SetParent(pageContainer.transform, false);
            leftSection.AddComponent<RectTransform>();
            leftSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup leftHLG = leftSection.AddComponent<HorizontalLayoutGroup>();
            leftHLG.childControlWidth = false;
            leftHLG.childForceExpandWidth = false;
            leftHLG.childAlignment = TextAnchor.MiddleLeft;
            leftHLG.spacing = 10;

            // Follow Quick Toggles
            footerFollowAngleBtn = UI.CreateUIButton(leftSection, 40, 40, "∡", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Angle"));
            footerFollowAngleImage = footerFollowAngleBtn.GetComponent<Image>();
            AddTooltip(footerFollowAngleBtn, "Follow Angle");
            
            footerFollowDistanceBtn = UI.CreateUIButton(leftSection, 40, 40, "↕", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Distance"));
            footerFollowDistanceImage = footerFollowDistanceBtn.GetComponent<Image>();
            AddTooltip(footerFollowDistanceBtn, "Follow Distance");
            
            footerFollowHeightBtn = UI.CreateUIButton(leftSection, 40, 40, "⊙", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Height"));
            footerFollowHeightImage = footerFollowHeightBtn.GetComponent<Image>();
            AddTooltip(footerFollowHeightBtn, "Follow Eye Height");

            // --- Center Section (Pagination) ---
            GameObject centerSection = new GameObject("CenterSection");
            centerSection.transform.SetParent(pageContainer.transform, false);
            centerSection.AddComponent<RectTransform>();
            centerSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup centerHLG = centerSection.AddComponent<HorizontalLayoutGroup>();
            centerHLG.childControlWidth = false;
            centerHLG.childForceExpandWidth = false;
            centerHLG.childAlignment = TextAnchor.MiddleCenter;
            centerHLG.spacing = 10;

            paginationFirstBtn = UI.CreateUIButton(centerSection, 40, 40, "|<", 18, 0, 0, AnchorPresets.middleCenter, FirstPage);
            paginationPrevBtn = UI.CreateUIButton(centerSection, 40, 40, "<", 20, 0, 0, AnchorPresets.middleCenter, PrevPage);
            
            GameObject textGO = new GameObject("PageText");
            textGO.transform.SetParent(centerSection.transform, false);
            paginationText = textGO.AddComponent<Text>();
            paginationText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            paginationText.fontSize = 18;
            paginationText.color = Color.white;
            paginationText.alignment = TextAnchor.MiddleCenter;
            paginationText.text = "1 / 1";
            paginationText.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.sizeDelta = new Vector2(200, 40);

            paginationNextBtn = UI.CreateUIButton(centerSection, 40, 40, ">", 20, 0, 0, AnchorPresets.middleCenter, NextPage);
            paginationLastBtn = UI.CreateUIButton(centerSection, 40, 40, ">|", 18, 0, 0, AnchorPresets.middleCenter, LastPage);

            // --- Right Section (Utility Controls) ---
            GameObject rightSection = new GameObject("RightSection");
            rightSection.transform.SetParent(pageContainer.transform, false);
            rightSection.AddComponent<RectTransform>();
            rightSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup rightHLG = rightSection.AddComponent<HorizontalLayoutGroup>();
            rightHLG.childControlWidth = false;
            rightHLG.childForceExpandWidth = false;
            rightHLG.childAlignment = TextAnchor.MiddleRight;
            rightHLG.spacing = 10;

            selectAllBtn = UI.CreateUIButton(rightSection, 40, 40, "A", 20, 0, 0, AnchorPresets.middleCenter, SelectAll);
            clearSelectionBtn = UI.CreateUIButton(rightSection, 40, 40, "C", 20, 0, 0, AnchorPresets.middleCenter, ClearSelection);
            gridSizeMinusBtn = UI.CreateUIButton(rightSection, 40, 40, "-", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(1));
            gridSizePlusBtn = UI.CreateUIButton(rightSection, 40, 40, "+", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(-1));
            
            footerLayoutBtn = UI.CreateUIButton(rightSection, 40, 40, "≡", 20, 0, 0, AnchorPresets.middleCenter, ToggleLayoutMode);
            footerLayoutBtnImage = footerLayoutBtn.GetComponent<Image>();
            footerLayoutBtnText = footerLayoutBtn.GetComponentInChildren<Text>();

            footerHeightBtn = UI.CreateUIButton(rightSection, 40, 40, "↕", 20, 0, 0, AnchorPresets.middleCenter, ToggleFixedHeightMode);
            footerHeightBtnImage = footerHeightBtn.GetComponent<Image>();
            footerHeightBtnText = footerHeightBtn.GetComponentInChildren<Text>();

            footerAutoHideBtn = UI.CreateUIButton(rightSection, 40, 40, "A", 20, 0, 0, AnchorPresets.middleCenter, ToggleAutoHideMode);
            footerAutoHideBtnImage = footerAutoHideBtn.GetComponent<Image>();
            footerAutoHideBtnText = footerAutoHideBtn.GetComponentInChildren<Text>();

            // --- Context Actions (Category-aware) ---
            footerRemoveAllClothingBtn = UI.CreateUIButton(rightSection, 40, 40, "Cl", 16, 0, 0, AnchorPresets.middleCenter, () => {
                Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                if (target == null)
                {
                    LogUtil.LogWarning("[VPB] Please select a Person atom.");
                    return;
                }

                // Reuse existing action pipeline on UIDraggableItem
                UIDraggableItem dragger = footerRemoveAllClothingBtn.GetComponent<UIDraggableItem>();
                if (dragger == null) dragger = footerRemoveAllClothingBtn.AddComponent<UIDraggableItem>();
                dragger.Panel = this;
                dragger.RemoveAllClothing(target);
            });
            footerRemoveAllClothingBtnImage = footerRemoveAllClothingBtn.GetComponent<Image>();
            footerRemoveAllClothingBtnText = footerRemoveAllClothingBtn.GetComponentInChildren<Text>();
            AddTooltip(footerRemoveAllClothingBtn, "Remove All Clothing from Target");

            footerRemoveAllHairBtn = UI.CreateUIButton(rightSection, 40, 40, "Hr", 16, 0, 0, AnchorPresets.middleCenter, () => {
                Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                if (target == null)
                {
                    LogUtil.LogWarning("[VPB] Please select a Person atom.");
                    return;
                }

                UIDraggableItem dragger = footerRemoveAllHairBtn.GetComponent<UIDraggableItem>();
                if (dragger == null) dragger = footerRemoveAllHairBtn.AddComponent<UIDraggableItem>();
                dragger.Panel = this;
                dragger.RemoveAllHair(target);
            });
            footerRemoveAllHairBtnImage = footerRemoveAllHairBtn.GetComponent<Image>();
            footerRemoveAllHairBtnText = footerRemoveAllHairBtn.GetComponentInChildren<Text>();
            AddTooltip(footerRemoveAllHairBtn, "Remove All Hair from Target");

            // Hover support
            AddHoverDelegate(paginationFirstBtn);
            AddTooltip(paginationFirstBtn, "First Page");
            AddHoverDelegate(paginationPrevBtn);
            AddTooltip(paginationPrevBtn, "Previous Page");
            AddHoverDelegate(paginationNextBtn);
            AddTooltip(paginationNextBtn, "Next Page");
            AddHoverDelegate(paginationLastBtn);
            AddTooltip(paginationLastBtn, "Last Page");
            AddHoverDelegate(selectAllBtn);
            AddTooltip(selectAllBtn, "Select All");
            AddHoverDelegate(clearSelectionBtn);
            AddTooltip(clearSelectionBtn, "Clear Selection");
            AddHoverDelegate(gridSizeMinusBtn);
            AddTooltip(gridSizeMinusBtn, "Decrease Columns");
            AddHoverDelegate(gridSizePlusBtn);
            AddTooltip(gridSizePlusBtn, "Increase Columns");
            AddHoverDelegate(footerFollowAngleBtn);
            AddHoverDelegate(footerFollowDistanceBtn);
            AddHoverDelegate(footerFollowHeightBtn);
            AddHoverDelegate(footerLayoutBtn);
            AddTooltip(footerLayoutBtn, "Toggle Layout Mode");
            AddHoverDelegate(footerHeightBtn);
            AddTooltip(footerHeightBtn, "Toggle Fixed Height Mode");
            AddHoverDelegate(footerAutoHideBtn);
            AddTooltip(footerAutoHideBtn, "Auto-Hide (Fixed)");
            AddHoverDelegate(footerRemoveAllClothingBtn);
            AddHoverDelegate(footerRemoveAllHairBtn);

            // Hover Path Text (Now placed above the buttons with background)
            GameObject pathGO = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.85f), AnchorPresets.hStretchBottom, 0, 40, new Vector2(0, 40));
            pathGO.name = "HoverPathContainer";
            pathGO.GetComponent<Image>().raycastTarget = false;
            hoverPathRT = pathGO.GetComponent<RectTransform>();
            hoverPathCanvasGroup = pathGO.AddComponent<CanvasGroup>();
            hoverPathCanvasGroup.alpha = 0;
            hoverPathCanvasGroup.blocksRaycasts = false;
            hoverPathCanvasGroup.interactable = false;
            
            GameObject hoverPathTextGO = new GameObject("HoverPathText");
            hoverPathTextGO.transform.SetParent(pathGO.transform, false);
            hoverPathText = hoverPathTextGO.AddComponent<Text>();
            hoverPathText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hoverPathText.fontSize = 20; // Slightly smaller to ensure 2 rows fit comfortably in 60px
            hoverPathText.color = Color.white;
            var shadow = hoverPathTextGO.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(1, -1);
            hoverPathText.alignment = TextAnchor.MiddleCenter;
            hoverPathText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hoverPathText.verticalOverflow = VerticalWrapMode.Truncate;
            hoverPathText.lineSpacing = 0.9f;
            hoverPathText.text = "";
            hoverPathText.raycastTarget = false;
            
            RectTransform hoverPathTextRT = hoverPathTextGO.GetComponent<RectTransform>();
            hoverPathTextRT.anchorMin = Vector2.zero;
            hoverPathTextRT.anchorMax = Vector2.one;
            hoverPathTextRT.sizeDelta = Vector2.zero;
            hoverPathTextRT.anchoredPosition = Vector2.zero;

            UpdateSideButtonsVisibility();
            UpdateFooterFollowStates();
            UpdateFooterLayoutState();
            UpdateFooterHeightState();
            UpdateFooterAutoHideState();
            UpdateFooterContextActions();
            UpdatePaginationText();
        }

        private void UpdateFooterContextActions()
        {
            // Default to hidden
            if (footerRemoveAllClothingBtn != null) footerRemoveAllClothingBtn.SetActive(false);
            if (footerRemoveAllHairBtn != null) footerRemoveAllHairBtn.SetActive(false);

            string title = currentCategoryTitle ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;

            if (footerRemoveAllClothingBtn != null) footerRemoveAllClothingBtn.SetActive(isClothing);
            if (footerRemoveAllHairBtn != null) footerRemoveAllHairBtn.SetActive(isHair);

            // Slight visual cue (optional but consistent with other footer buttons)
            if (footerRemoveAllClothingBtnImage != null) footerRemoveAllClothingBtnImage.color = new Color(0.6f, 0.2f, 0.2f, 1f);
            if (footerRemoveAllHairBtnImage != null) footerRemoveAllHairBtnImage.color = new Color(0.6f, 0.2f, 0.2f, 1f);
            if (footerRemoveAllClothingBtnText != null) footerRemoveAllClothingBtnText.color = Color.white;
            if (footerRemoveAllHairBtnText != null) footerRemoveAllHairBtnText.color = Color.white;
        }

        private void ToggleLayoutMode()
        {
            layoutMode = (layoutMode == GalleryLayoutMode.Grid) ? GalleryLayoutMode.VerticalCard : GalleryLayoutMode.Grid;
            
            // Immediately update grid component
            if (contentGO != null)
            {
                UIGridAdaptive adaptive = contentGO.GetComponent<UIGridAdaptive>();
                if (adaptive != null)
                {
                    adaptive.isVerticalCard = (layoutMode == GalleryLayoutMode.VerticalCard);
                    adaptive.forcedColumnCount = gridColumnCount;
                    adaptive.UpdateGrid();
                }
            }

            // FULL PURGE: Clear both pooled and active buttons because templates are fundamentally different
            foreach (var go in fileButtonPool) if (go != null) Destroy(go);
            fileButtonPool.Clear();
            
            foreach (var go in activeButtons) if (go != null) Destroy(go);
            activeButtons.Clear();

            UpdateFooterLayoutState();
            RefreshFiles(true); // Force full refresh
        }

        private void UpdateFooterLayoutState()
        {
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            if (footerLayoutBtnImage != null)
                footerLayoutBtnImage.color = (layoutMode == GalleryLayoutMode.VerticalCard) ? activeColor : inactiveColor;
            
            if (footerLayoutBtnText != null)
                footerLayoutBtnText.text = (layoutMode == GalleryLayoutMode.VerticalCard) ? "≡" : "▤";
        }

        private void ToggleFixedHeightMode()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.DesktopFixedHeightMode = (VPBConfig.Instance.DesktopFixedHeightMode + 1) % 2;
            VPBConfig.Instance.Save();
            UpdateFooterHeightState();
            UpdateLayout();
        }

        private void UpdateFooterHeightState()
        {
            if (VPBConfig.Instance == null) return;
            
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            
            if (footerHeightBtnImage != null)
                footerHeightBtnImage.color = VPBConfig.Instance.DesktopFixedHeightMode > 0 ? activeColor : inactiveColor;
                
            if (footerHeightBtnText != null)
            {
                switch(VPBConfig.Instance.DesktopFixedHeightMode)
                {
                    case 0: footerHeightBtnText.text = "H1"; break;
                    case 1: footerHeightBtnText.text = "HC"; break;
                }
            }
        }

        private void ToggleAutoHideMode()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.DesktopFixedAutoCollapse = !VPBConfig.Instance.DesktopFixedAutoCollapse;
            VPBConfig.Instance.Save();
            UpdateFooterAutoHideState();
            UpdateLayout();
        }

        private void UpdateFooterAutoHideState()
        {
            if (VPBConfig.Instance == null) return;

            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            if (footerAutoHideBtnImage != null)
                footerAutoHideBtnImage.color = VPBConfig.Instance.DesktopFixedAutoCollapse ? activeColor : inactiveColor;

            if (footerAutoHideBtnText != null)
            {
                footerAutoHideBtnText.text = VPBConfig.Instance.DesktopFixedAutoCollapse ? "AH" : "AO";
            }
        }

        private void ToggleFollowQuick(string type)
        {
            if (VPBConfig.Instance == null) return;
            
            if (type == "Angle") {
                VPBConfig.Instance.FollowAngle = (VPBConfig.Instance.FollowAngle == "Off") ? "Both" : "Off";
            } else if (type == "Distance") {
                VPBConfig.Instance.FollowDistance = (VPBConfig.Instance.FollowDistance == "Off") ? "Both" : "Off";
            } else if (type == "Height") {
                VPBConfig.Instance.FollowEyeHeight = (VPBConfig.Instance.FollowEyeHeight == "Off") ? "Both" : "Off";
            }
            
            VPBConfig.Instance.TriggerChange();
            UpdateFooterFollowStates();
        }

        private void UpdateFooterFollowStates()
        {
            if (VPBConfig.Instance == null) return;
            
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            
            if (footerFollowAngleImage != null)
                footerFollowAngleImage.color = VPBConfig.Instance.FollowAngle != "Off" ? activeColor : inactiveColor;
                
            if (footerFollowDistanceImage != null)
                footerFollowDistanceImage.color = VPBConfig.Instance.FollowDistance != "Off" ? activeColor : inactiveColor;
                
            if (footerFollowHeightImage != null)
                footerFollowHeightImage.color = VPBConfig.Instance.FollowEyeHeight != "Off" ? activeColor : inactiveColor;
        }

        private void AddTooltip(GameObject go, string tooltip)
        {
            if (go == null) return;
            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            
            del.OnHoverChange += (enter) => {
                if (enter) temporaryStatusMsg = tooltip;
                else if (temporaryStatusMsg == tooltip) temporaryStatusMsg = null;
            };
        }


        private void UpdateDesktopModeButton()
        {
            if (VPBConfig.Instance == null) return;

            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }

            bool fixedMode = isFixedLocally;
            string text = fixedMode ? "Floating" : "Fixed";
            Color color = fixedMode ? new Color(0.15f, 0.45f, 0.6f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);

            if (rightDesktopModeBtnText != null) 
            {
                rightDesktopModeBtnText.text = text;
                rightDesktopModeBtnText.transform.parent.gameObject.SetActive(!isVR);
            }
            if (rightDesktopModeBtnImage != null) rightDesktopModeBtnImage.color = color;

            if (leftDesktopModeBtnText != null) 
            {
                leftDesktopModeBtnText.text = text;
                leftDesktopModeBtnText.transform.parent.gameObject.SetActive(!isVR);
            }
            if (leftDesktopModeBtnImage != null) leftDesktopModeBtnImage.color = color;

            if (footerFollowAngleBtn != null) footerFollowAngleBtn.SetActive(!fixedMode);
            if (footerFollowDistanceBtn != null) footerFollowDistanceBtn.SetActive(!fixedMode);
            if (footerFollowHeightBtn != null) footerFollowHeightBtn.SetActive(!fixedMode);
            if (footerHeightBtn != null) footerHeightBtn.SetActive(fixedMode);
            if (footerAutoHideBtn != null) footerAutoHideBtn.SetActive(fixedMode);

            UpdateSideButtonPositions();
        }

        private void ToggleDesktopMode()
        {
            if (VPBConfig.Instance == null) return;

            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }
            if (isVR)
            {
                if (isFixedLocally) SetFixedLocally(false);
                return;
            }
            
            bool targetFixed = !isFixedLocally;
            
            if (targetFixed)
            {
                // Only one can be fixed. Revert others.
                if (Gallery.singleton != null)
                {
                    foreach (var p in Gallery.singleton.Panels)
                    {
                        if (p != this) p.SetFixedLocally(false);
                    }
                }
                isFixedLocally = true;
                VPBConfig.Instance.DesktopFixedMode = true;
            }
            else
            {
                isFixedLocally = false;
                VPBConfig.Instance.DesktopFixedMode = false;
            }
            
            VPBConfig.Instance.Save();
            UpdateDesktopModeButton();
            UpdateLayout();
        }

        public void SetFixedLocally(bool fixedMode)
        {
            if (fixedMode)
            {
                bool isVR = false;
                try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }
                if (isVR) fixedMode = false;
            }

            if (isFixedLocally == fixedMode) return;
            isFixedLocally = fixedMode;
            if (!fixedMode) SetCollapsed(false);
            UpdateDesktopModeButton();
            UpdateSideButtonsVisibility();
            UpdateLayout();
        }

        public void SetCollapsed(bool collapsed)
        {
            if (isCollapsed == collapsed) return;
            isCollapsed = collapsed;
            collapseTimer = 0f;
            
            if (backgroundBoxGO != null)
            {
                RectTransform rt = backgroundBoxGO.GetComponent<RectTransform>();
                rt.anchoredPosition = collapsed ? new Vector2(rt.rect.width, 0) : Vector2.zero;
            }

            if (collapseTriggerGO != null)
            {
                Image img = collapseTriggerGO.GetComponent<Image>();
                if (img != null) 
                {
                    img.color = collapsed ? new Color(0.15f, 0.15f, 0.15f, 0.4f) : new Color(1, 1, 1, 0f);
                    img.raycastTarget = collapsed;
                }
            }
            if (collapseHandleText != null)
            {
                collapseHandleText.gameObject.SetActive(collapsed);
            }
            
            UpdateSideButtonsVisibility();
            UpdateLayout();
        }

        private void NextPage()
        {
            currentPage++;
            RefreshFiles();
        }

        private void FirstPage()
        {
            currentPage = 0;
            RefreshFiles(false, true);
        }

        private void LastPage()
        {
            currentPage = Mathf.Max(0, lastTotalPages - 1);
            RefreshFiles();
        }

        private void PrevPage()
        {
            if (currentPage > 0)
            {
                currentPage--;
                RefreshFiles(false, true);
            }
        }

        private void SelectAll()
        {
            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectionAnchorPath = null;

            for (int i = 0; i < lastFilteredFiles.Count; i++)
            {
                var f = lastFilteredFiles[i];
                if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                if (selectedFilePaths.Add(f.Path)) selectedFiles.Add(f);
            }

            if (selectedFiles.Count > 0)
            {
                selectedPath = selectedFiles[0].Path;
                selectionAnchorPath = selectedPath;
                SetHoverPath(selectedFiles[0]);
            }
            else
            {
                selectedPath = null;
                SetHoverPath("");
            }

            RefreshFiles(true);
            UpdatePaginationText();
            actionsPanel?.HandleSelectionChanged(selectedFiles, selectedHubItem);
        }

        private void ClearSelection()
        {
            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectionAnchorPath = null;
            selectedPath = null;
            selectedHubItem = null;
            SetHoverPath("");
            RefreshFiles(true);
            UpdatePaginationText();
            actionsPanel?.HandleSelectionChanged(selectedFiles, selectedHubItem);
        }

        private void AdjustGridColumns(int delta)
        {
            gridColumnCount = Mathf.Clamp(gridColumnCount + delta, 1, 12);
            if (contentGO != null)
            {
                UIGridAdaptive adaptive = contentGO.GetComponent<UIGridAdaptive>();
                if (adaptive != null)
                {
                    adaptive.forcedColumnCount = gridColumnCount;
                    adaptive.UpdateGrid();
                }
            }
            RefreshFiles(true);
        }

        private void UpdatePaginationText()
        {
            if (paginationText != null)
            {
                int page = currentPage + 1;
                int totalPages = Mathf.Max(1, lastTotalPages);
                int total = Mathf.Max(0, lastTotalItems);
                paginationText.text = $"{page} / {totalPages} ({total})";
            }
        }

        private void ToggleRight(ContentType type)
        {
            if (rightActiveContent == type) 
            {
                rightActiveContent = null;
            }
            else 
            {
                rightActiveContent = type;
                // Collapse Left IF it is the SAME type
                if (leftActiveContent == type) leftActiveContent = null;
            }
            
            UpdateLayout();
            UpdateTabs();

            if (leftActiveContent == ContentType.ActiveItems || rightActiveContent == ContentType.ActiveItems)
            {
                RefreshFiles();
            }
        }

        private void ToggleLeft(ContentType type)
        {
            if (leftActiveContent == type)
            {
                leftActiveContent = null;
            }
            else
            {
                leftActiveContent = type;
                // Collapse Right IF it is the SAME type
                if (rightActiveContent == type) rightActiveContent = null;
            }
            
            UpdateLayout();
            UpdateTabs();

            if (leftActiveContent == ContentType.ActiveItems || rightActiveContent == ContentType.ActiveItems)
            {
                RefreshFiles();
            }
        }

        private void UpdateReplaceButtonState()
        {
            string text = DragDropReplaceMode ? "Replace" : "Add";
            Color color = DragDropReplaceMode ? new Color(0.6f, 0.15f, 0.15f, 1f) : new Color(0.15f, 0.45f, 0.15f, 1f);

            if (rightReplaceBtnText != null) rightReplaceBtnText.text = text;
            if (rightReplaceBtnImage != null) rightReplaceBtnImage.color = color;
            
            if (leftReplaceBtnText != null) leftReplaceBtnText.text = text;
            if (leftReplaceBtnImage != null) leftReplaceBtnImage.color = color;
        }

        private void ToggleReplaceMode()
        {
            DragDropReplaceMode = !DragDropReplaceMode;
            UpdateReplaceButtonState();
        }

        private void UpdateApplyModeButtonState()
        {
            string text = ItemApplyMode == ApplyMode.SingleClick ? "1-Click" : "2-Click";
            Color color = ItemApplyMode == ApplyMode.SingleClick ? new Color(0.6f, 0.45f, 0.15f, 1f) : new Color(0.15f, 0.15f, 0.45f, 1f);

            if (rightApplyModeBtnText != null) rightApplyModeBtnText.text = text;
            if (rightApplyModeBtnImage != null) rightApplyModeBtnImage.color = color;
            
            if (leftApplyModeBtnText != null) leftApplyModeBtnText.text = text;
            if (leftApplyModeBtnImage != null) leftApplyModeBtnImage.color = color;
        }

        private void ToggleApplyMode()
        {
            ItemApplyMode = (ItemApplyMode == ApplyMode.SingleClick) ? ApplyMode.DoubleClick : ApplyMode.SingleClick;
            UpdateApplyModeButtonState();
        }

        public void UpdateLayout()
        {
            if (!creatorsCached) CacheCreators();
            if (!categoriesCached) CacheCategoryCounts();

            if (contentScrollRT == null) return;

            // Ensure UI reflects persisted replace mode even if the panel was recreated/re-shown.
            UpdateReplaceButtonState();
            
            float leftOffset = 20;
            float rightOffset = -20;
            
            bool forceBoth = IsHubMode;
            
            // Left Side
            if ((forceBoth || leftActiveContent.HasValue) && leftTabScrollGO != null)
            {
                leftTabScrollGO.SetActive(true);
                leftOffset = 230; 
                
                if (leftSortBtn != null) leftSortBtn.SetActive(true);

                if (leftSearchInput != null) 
                {
                    leftSearchInput.gameObject.SetActive(true);
                    string target = "";
                    ContentType type = leftActiveContent.HasValue ? leftActiveContent.Value : ContentType.Hub;

                    if (type == ContentType.Category) target = categoryFilter;
                    else if (type == ContentType.Creator) target = creatorFilter;
                    else target = ""; // Status filter?

                    if (leftSearchInput.text != target) leftSearchInput.text = target;
                    
                    if (leftSearchInput.placeholder is Text ph)
                    {
                        ph.text = type.ToString() + "...";
                    }
                    
                    // Hide search input for Status for now
                    if (type == ContentType.Status) leftSearchInput.gameObject.SetActive(false);
                }
            }
            else if (leftTabScrollGO != null)
            {
                leftTabScrollGO.SetActive(false);
                if (leftSubTabScrollGO != null) leftSubTabScrollGO.SetActive(false);
                if (leftSearchInput != null) leftSearchInput.gameObject.SetActive(false);
                if (leftSortBtn != null) leftSortBtn.SetActive(false);
                
                // Ensure sub controls are hidden if main panel is closed
                if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
            }
            
            // Right Side
            if ((forceBoth || rightActiveContent.HasValue) && rightTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(true);
                rightOffset = -230;

                if (rightSortBtn != null) rightSortBtn.SetActive(true);

                if (rightSearchInput != null) 
                {
                    rightSearchInput.gameObject.SetActive(true);
                    string target = "";
                    ContentType type = rightActiveContent.HasValue ? rightActiveContent.Value : ContentType.Hub;

                    if (type == ContentType.Category) target = categoryFilter;
                    else if (type == ContentType.Creator) target = creatorFilter;
                    else target = "";

                    if (rightSearchInput.text != target) rightSearchInput.text = target;

                    if (rightSearchInput.placeholder is Text ph)
                    {
                        ph.text = type.ToString() + "...";
                    }

                    // Hide search input for Status for now
                    if (type == ContentType.Status) rightSearchInput.gameObject.SetActive(false);
                }
            }
            else if (rightTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(false);
                if (rightSubTabScrollGO != null) rightSubTabScrollGO.SetActive(false);
                if (rightSearchInput != null) rightSearchInput.gameObject.SetActive(false);
                if (rightSortBtn != null) rightSortBtn.SetActive(false);
                
                // Ensure sub controls are hidden if main panel is closed
                if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
            }
            
            float bottomOffset = 60; // Overlay status bar on top of grid
            float topOffset = -65f;

            contentScrollRT.offsetMin = new Vector2(leftOffset, bottomOffset);
            contentScrollRT.offsetMax = new Vector2(rightOffset, topOffset);

            if (leftTabScrollGO != null)
            {
                RectTransform rt = leftTabScrollGO.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, bottomOffset + 8);
            }
            if (rightTabScrollGO != null)
            {
                RectTransform rt = rightTabScrollGO.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, bottomOffset + 8);
            }
            if (leftSubTabScrollGO != null)
            {
                RectTransform rt = leftSubTabScrollGO.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, bottomOffset + 8);
            }
            if (rightSubTabScrollGO != null)
            {
                RectTransform rt = rightSubTabScrollGO.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(rt.offsetMin.x, bottomOffset + 8);
            }
            if (leftSubClearBtn != null)
            {
                RectTransform rt = leftSubClearBtn.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, bottomOffset + 8);
            }
            if (rightSubClearBtn != null)
            {
                RectTransform rt = rightSubClearBtn.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, bottomOffset + 8);
            }

            // Move Footer (Pagination and Hover Path)
            if (paginationRT != null)
            {
                // Footer bar: ALWAYS stretch to full width of backgroundBoxGO
                paginationRT.offsetMin = new Vector2(0, 0);
                paginationRT.offsetMax = new Vector2(0, 60);
                
                if (hoverPathRT != null)
                {
                    // Hover bar: stretch to full width
                    hoverPathRT.offsetMin = new Vector2(0, 60);
                    hoverPathRT.offsetMax = new Vector2(0, 120);
                }
            }
            
            if (leftSideContainer != null)
            {
                RectTransform rt = leftSideContainer.GetComponent<RectTransform>();
                float yShift = 0;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, yShift);
            }
            if (rightSideContainer != null)
            {
                RectTransform rt = rightSideContainer.GetComponent<RectTransform>();
                float yShift = 0;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, yShift);
            }

            UpdateButtonStates();
            if (actionsPanel != null) actionsPanel.UpdateUI();
        }

        private void UpdateButtonStates()
        {
             if (isResizing) return;
             ApplyCurvatureToChildren();
             // Text updates are intentionally disabled to keep static labels.
        }

        private void ApplyCurvatureToChildren()
        {
            if (canvas == null) return;
            RectTransform canvasRT = canvas.GetComponent<RectTransform>();
            bool enabled = VPBConfig.Instance != null && VPBConfig.Instance.EnableCurvature;

            // Apply to all Graphic components in the canvas
            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
            foreach (var g in graphics)
            {
                // Skip if it's part of the settings panel - we want that to stay flat for better slider interaction
                bool isSettingsChild = settingsPanel != null && settingsPanel.settingsPaneGO != null && 
                                     g.transform.IsChildOf(settingsPanel.settingsPaneGO.transform);

                bool isActionsChild = actionsPanel != null && actionsPanel.actionsPaneGO != null &&
                                     g.transform.IsChildOf(actionsPanel.actionsPaneGO.transform);

                // Also skip side panes (tabs, search, sort) to avoid clipping artifacts on large widths
                bool isSidePaneChild = (leftTabScrollGO != null && g.transform.IsChildOf(leftTabScrollGO.transform)) ||
                                     (rightTabScrollGO != null && g.transform.IsChildOf(rightTabScrollGO.transform)) ||
                                     (leftSubTabScrollGO != null && g.transform.IsChildOf(leftSubTabScrollGO.transform)) ||
                                     (rightSubTabScrollGO != null && g.transform.IsChildOf(rightSubTabScrollGO.transform)) ||
                                     (leftSearchInput != null && g.transform.IsChildOf(leftSearchInput.transform)) ||
                                     (rightSearchInput != null && g.transform.IsChildOf(rightSearchInput.transform)) ||
                                     (leftSortBtn != null && g.transform.IsChildOf(leftSortBtn.transform)) ||
                                     (rightSortBtn != null && g.transform.IsChildOf(rightSortBtn.transform)) ||
                                     (leftSubSortBtn != null && g.transform.IsChildOf(leftSubSortBtn.transform)) ||
                                     (rightSubSortBtn != null && g.transform.IsChildOf(rightSubSortBtn.transform)) ||
                                     (leftSubSearchInput != null && g.transform.IsChildOf(leftSubSearchInput.transform)) ||
                                     (rightSubSearchInput != null && g.transform.IsChildOf(rightSubSearchInput.transform)) ||
                                     (leftSubClearBtn != null && g.transform.IsChildOf(leftSubClearBtn.transform)) ||
                                     (rightSubClearBtn != null && g.transform.IsChildOf(rightSubClearBtn.transform));
                
                var mod = g.gameObject.GetComponent<CurvedUIVertexModifier>();
                
                if (isSettingsChild || isActionsChild || isSidePaneChild)
                {
                    if (mod != null) mod.enabled = false;
                    continue;
                }

                if (mod == null && enabled)
                {
                    mod = g.gameObject.AddComponent<CurvedUIVertexModifier>();
                }
                
                if (mod != null)
                {
                    mod.canvasRT = canvasRT;
                    mod.enabled = enabled;
                    g.SetVerticesDirty(); // Force remesh
                }
            }

            // Update Background MeshCollider for accurate laser dot and physical interaction
            UpdateMeshCollider(backgroundBoxGO, canvasRT, enabled, true);

            // Also update Settings Panel if it exists - but use a FLAT collider for it
            if (settingsPanel != null)
            {
                settingsPanel.UpdateCurvatureLayout();
                UpdateMeshCollider(settingsPanel.settingsPaneGO, canvasRT, enabled, false);
            }

            // Also update Actions Panel if it exists - but use a FLAT collider for it
            if (actionsPanel != null)
            {
                UpdateMeshCollider(actionsPanel.actionsPaneGO, canvasRT, enabled, false);
            }
        }

        private void UpdateMeshCollider(GameObject go, RectTransform canvasRT, bool enabled, bool curved)
        {
            if (go == null) return;
            
            if (!curved || !enabled)
            {
                var existingMC = go.GetComponent<MeshCollider>();
                if (existingMC != null) Destroy(existingMC);
                
                var bc = go.GetComponent<BoxCollider>();
                if (enabled)
                {
                    if (bc == null) bc = go.AddComponent<BoxCollider>();
                    RectTransform rt = go.GetComponent<RectTransform>();
                    // Make collider significantly thicker (20 units) for more reliable interaction in 3D space
                    bc.size = new Vector3(rt.rect.width, rt.rect.height, 20f);
                    // Adjust center based on RectTransform pivot
                    Vector2 pivot = rt.pivot;
                    bc.center = new Vector3((0.5f - pivot.x) * rt.rect.width, (0.5f - pivot.y) * rt.rect.height, 0f);
                }
                else if (bc != null)
                {
                    Destroy(bc);
                }
                return;
            }

            var existingBC = go.GetComponent<BoxCollider>();
            if (existingBC != null) Destroy(existingBC);

            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = UI.GenerateCurvedMesh(go.GetComponent<RectTransform>(), canvasRT);
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
            string text = followUser ? "Follow" : "Static";
            Color color = followUser ? new Color(0.15f, 0.45f, 0.6f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
            
            if (rightFollowBtnText != null) rightFollowBtnText.text = text;
            if (rightFollowBtnImage != null) rightFollowBtnImage.color = color;
            
            if (leftFollowBtnText != null) leftFollowBtnText.text = text;
            if (leftFollowBtnImage != null) leftFollowBtnImage.color = color;
        }

        private void UpdateSideButtonPositions()
        {
            if (VPBConfig.Instance == null) return;
            float spacing = 60f;
            float groupGap = VPBConfig.Instance.EnableButtonGaps ? 20f : 0f;
            float stackHeight = GetSideButtonsStackHeight(spacing, groupGap);
            float topY = stackHeight * 0.5f;

            // Settings
            UpdateListPositions(rightSideButtons, topY, spacing, groupGap);
            UpdateListPositions(leftSideButtons, topY, spacing, groupGap);
        }

        private SideButtonLayoutEntry[] GetSideButtonsLayout()
        {
            string title = currentCategoryTitle ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;

            return new SideButtonLayoutEntry[]
            {
                new SideButtonLayoutEntry(0, 0, 0),
                new SideButtonLayoutEntry(1, 0, 0),
                new SideButtonLayoutEntry(2, 0, 1),
                new SideButtonLayoutEntry(3, 0, 0),
                new SideButtonLayoutEntry(4, 0, 1),
                new SideButtonLayoutEntry(5, 0, 0), // ActiveItems
                new SideButtonLayoutEntry(6, 0, 0), // Creator
                new SideButtonLayoutEntry(7, 0, 0), // Status
                new SideButtonLayoutEntry(11, 0, 0), // Hub
                new SideButtonLayoutEntry(8, 0, 1), // Target
                new SideButtonLayoutEntry(9, 0, 0), // Apply Mode
                new SideButtonLayoutEntry(10, 0, 0), // Replace
                new SideButtonLayoutEntry(13, 0, 1), // Remove Clothing (context)
                new SideButtonLayoutEntry(14, 0, isHair ? 1 : 0), // Remove Hair (context)
                new SideButtonLayoutEntry(12, 0, (isClothing || isHair) ? 1 : 0), // Undo
            };
        }

        private void UpdateSideContextActions()
        {
            string title = currentCategoryTitle ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;

            if (rightRemoveAllClothingBtn != null) rightRemoveAllClothingBtn.SetActive(isClothing);
            if (leftRemoveAllClothingBtn != null) leftRemoveAllClothingBtn.SetActive(isClothing);
            if (rightRemoveAllHairBtn != null) rightRemoveAllHairBtn.SetActive(isHair);
            if (leftRemoveAllHairBtn != null) leftRemoveAllHairBtn.SetActive(isHair);

            UpdateSideButtonPositions();
        }

        private float GetSideButtonsStackHeight(float spacing, float gap)
        {
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            if (layout == null || layout.Length <= 1) return 0f;

            int gapUnits = 0;
            for (int i = 1; i < layout.Length; i++) gapUnits += layout[i].gapTier;
            return (layout.Length - 1) * spacing + gapUnits * gap;
        }

        private void UpdateListPositions(List<RectTransform> buttons, float startY, float spacing, float gap)
        {
            if (buttons == null) return;

            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            float y = startY;
            bool firstVisible = true;
            for (int i = 0; i < layout.Length; i++)
            {
                RectTransform rt = (layout[i].buttonIndex >= 0 && layout[i].buttonIndex < buttons.Count) ? buttons[layout[i].buttonIndex] : null;
                if (rt == null || !rt.gameObject.activeSelf) continue;

                if (!firstVisible) y -= (spacing + gap * layout[i].gapTier);
                rt.anchoredPosition = new Vector2(0, y);
                firstVisible = false;
            }

#if false
            if (buttons == null || buttons.Count < 12) return;
            
            // 0: Fixed/Floating
            buttons[0].anchoredPosition = new Vector2(0, startY);
            // 1: Settings
            buttons[1].anchoredPosition = new Vector2(0, startY - spacing);
            // 2: Follow
            buttons[2].anchoredPosition = new Vector2(0, startY - spacing * 2 - gap);
            // 3: Clone
            buttons[3].anchoredPosition = new Vector2(0, startY - spacing * 3 - gap);
            // 4: Category
            buttons[4].anchoredPosition = new Vector2(0, startY - spacing * 4 - gap * 2);
            // 5: Creator
            buttons[5].anchoredPosition = new Vector2(0, startY - spacing * 5 - gap * 2);
            // 6: Status
            buttons[6].anchoredPosition = new Vector2(0, startY - spacing * 6 - gap * 2);
            // 7: Hub (under Status)
            buttons[10].anchoredPosition = new Vector2(0, startY - spacing * 7 - gap * 2);

            // 8: Target (start lower group with extra gap)
            buttons[7].anchoredPosition = new Vector2(0, startY - spacing * 8 - gap * 4);

            // 9: Apply Mode
            buttons[8].anchoredPosition = new Vector2(0, startY - spacing * 9 - gap * 4);

            // 10: Add/Replace
            buttons[9].anchoredPosition = new Vector2(0, startY - spacing * 10 - gap * 4);

            // 11: Undo
            buttons[11].anchoredPosition = new Vector2(0, startY - spacing * 12 - gap * 3);
#endif
        }

        private void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        private void CreateLoadingOverlay(GameObject parentGO)
        {
            if (parentGO == null) return;
            if (loadingOverlayGO != null) return;

            loadingOverlayGO = new GameObject("LoadingOverlay");
            loadingOverlayGO.transform.SetParent(parentGO.transform, false);
            RectTransform overlayRT = loadingOverlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;
            overlayRT.anchoredPosition = Vector2.zero;

            Image overlayImg = loadingOverlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.35f);
            overlayImg.raycastTarget = true;

            GameObject barGO = new GameObject("LoadingBar");
            barGO.transform.SetParent(loadingOverlayGO.transform, false);
            loadingBarContainerRT = barGO.AddComponent<RectTransform>();
            loadingBarContainerRT.anchorMin = new Vector2(0.5f, 0.5f);
            loadingBarContainerRT.anchorMax = new Vector2(0.5f, 0.5f);
            loadingBarContainerRT.pivot = new Vector2(0.5f, 0.5f);
            loadingBarContainerRT.anchoredPosition = Vector2.zero;
            loadingBarContainerRT.sizeDelta = new Vector2(420, 10);
            Image barBg = barGO.AddComponent<Image>();
            barBg.color = new Color(1f, 1f, 1f, 0.18f);
            barBg.raycastTarget = false;

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(barGO.transform, false);
            loadingBarFillRT = fillGO.AddComponent<RectTransform>();
            loadingBarFillRT.anchorMin = new Vector2(0.5f, 0.5f);
            loadingBarFillRT.anchorMax = new Vector2(0.5f, 0.5f);
            loadingBarFillRT.pivot = new Vector2(0.5f, 0.5f);
            loadingBarFillRT.sizeDelta = new Vector2(120, 10);
            loadingBarFillRT.anchoredPosition = Vector2.zero;
            Image fillImg = fillGO.AddComponent<Image>();
            fillImg.color = new Color(1f, 1f, 1f, 0.85f);
            fillImg.raycastTarget = false;

            SetLayerRecursive(loadingOverlayGO, parentGO.layer);
            loadingOverlayGO.SetActive(false);
            isLoadingOverlayVisible = false;
            loadingBarAnimT = 0f;
        }

        private void ShowLoadingOverlay(string message)
        {
            if (loadingOverlayGO == null) return;
            loadingBarAnimT = 0f;
            isLoadingOverlayVisible = true;
            loadingOverlayGO.SetActive(true);
        }

        private void HideLoadingOverlay()
        {
            isLoadingOverlayVisible = false;
            if (loadingOverlayGO != null) loadingOverlayGO.SetActive(false);
        }

        public void DisplayColorPicker(string title, Color initialColor, UnityAction<Color> onConfirm)
        {
             // Use the singleton
             if (UIColorPicker.Instance != null)
                UIColorPicker.Instance.Show(initialColor, (c) => onConfirm?.Invoke(c));
        }

        public void DisplayTextInput(string title, string initialValue, UnityAction<string> onConfirm)
        {
            GameObject overlayGO = new GameObject("TextInputOverlay");
            overlayGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;
            
            Image overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.5f);
            
            // Panel
            GameObject panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(overlayGO.transform, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.sizeDelta = new Vector2(400, 200);
            
            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            
            // Title
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 24;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -10);
            titleRT.sizeDelta = new Vector2(0, 40);

            // Input - Using CreateSearchInput logic from Tabs.cs but since it's private there, we re-implement or call if possible.
            // Actually, CreateSearchInput is private in GalleryPanel.Tabs.cs.
            // Let's create a simple InputField here.
            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(panelGO.transform, false);
            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.sizeDelta = new Vector2(350, 40);
            inputRT.anchoredPosition = new Vector2(0, 10);

            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.sizeDelta = new Vector2(-20, -10);

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 20;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform tRT = textGO.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.sizeDelta = Vector2.zero;

            input.textComponent = t;
            input.text = initialValue;

            // Buttons
            GameObject confirmBtn = UI.CreateUIButton(panelGO, 140, 45, "Confirm", 18, 80, -60, AnchorPresets.middleCenter, () => {
                onConfirm?.Invoke(input.text);
                Destroy(overlayGO);
            });
            
            GameObject cancelBtn = UI.CreateUIButton(panelGO, 140, 45, "Cancel", 18, -80, -60, AnchorPresets.middleCenter, () => {
                Destroy(overlayGO);
            });

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
            input.ActivateInputField();
        }

        public void DisplayConfirm(string title, string message, UnityAction onConfirm)
        {
            GameObject overlayGO = new GameObject("ConfirmOverlay");
            overlayGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;
            
            Image overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.5f);
            
            // Panel
            GameObject panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(overlayGO.transform, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.sizeDelta = new Vector2(450, 250);
            
            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            
            // Title
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 24;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -15);
            titleRT.sizeDelta = new Vector2(0, 40);

            // Message
            GameObject msgGO = new GameObject("Message");
            msgGO.transform.SetParent(panelGO.transform, false);
            Text msgText = msgGO.AddComponent<Text>();
            msgText.text = message;
            msgText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            msgText.fontSize = 18;
            msgText.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            msgText.alignment = TextAnchor.MiddleCenter;
            RectTransform msgRT = msgGO.GetComponent<RectTransform>();
            msgRT.anchorMin = Vector2.zero;
            msgRT.anchorMax = Vector2.one;
            msgRT.offsetMin = new Vector2(20, 80);
            msgRT.offsetMax = new Vector2(-20, -60);

            // Buttons
            GameObject cancelBtn = UI.CreateUIButton(panelGO, 160, 45, "Cancel", 18, -100, -80, AnchorPresets.middleCenter, () => Destroy(overlayGO));
            GameObject confirmBtn = UI.CreateUIButton(panelGO, 160, 45, "Confirm", 18, 100, -80, AnchorPresets.middleCenter, () => {
                onConfirm?.Invoke();
                Destroy(overlayGO);
            });
            confirmBtn.GetComponent<Image>().color = new Color(0.4f, 0.2f, 0.2f, 1f);

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
        }
    }
}
