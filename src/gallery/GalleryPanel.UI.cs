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
            // Pagination Container (Bottom Left)
            GameObject pageContainer = new GameObject("PaginationContainer");
            pageContainer.transform.SetParent(backgroundBoxGO.transform, false);
            paginationRT = pageContainer.AddComponent<RectTransform>();
            paginationRT.anchorMin = new Vector2(0, 0);
            paginationRT.anchorMax = new Vector2(0, 0);
            paginationRT.pivot = new Vector2(0, 0.5f);
            paginationRT.anchoredPosition = new Vector2(50, 30); // Centered in 60px footer area, moved right from corner
            paginationRT.sizeDelta = new Vector2(300, 40);

            // Prev Button
            paginationPrevBtn = UI.CreateUIButton(pageContainer, 40, 40, "<", 20, 0, 0, AnchorPresets.middleLeft, PrevPage);
            
            // Text
            GameObject textGO = new GameObject("PageText");
            textGO.transform.SetParent(pageContainer.transform, false);
            paginationText = textGO.AddComponent<Text>();
            paginationText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            paginationText.fontSize = 18;
            paginationText.color = Color.white;
            paginationText.alignment = TextAnchor.MiddleCenter;
            paginationText.text = "1 / 1";
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0, 0.5f);
            textRT.anchorMax = new Vector2(0, 0.5f);
            textRT.pivot = new Vector2(0, 0.5f);
            textRT.anchoredPosition = new Vector2(50, 0);
            textRT.sizeDelta = new Vector2(100, 40);

            // Next Button
            paginationNextBtn = UI.CreateUIButton(pageContainer, 40, 40, ">", 20, 160, 0, AnchorPresets.middleLeft, NextPage);
            AddTooltip(paginationNextBtn, "Next Page");

            // Follow Quick Toggles
            footerFollowAngleBtn = UI.CreateUIButton(pageContainer, 40, 40, "∡", 20, 260, 0, AnchorPresets.middleLeft, () => ToggleFollowQuick("Angle"));
            footerFollowAngleImage = footerFollowAngleBtn.GetComponent<Image>();
            AddTooltip(footerFollowAngleBtn, "Follow Angle");
            
            footerFollowDistanceBtn = UI.CreateUIButton(pageContainer, 40, 40, "↕", 20, 310, 0, AnchorPresets.middleLeft, () => ToggleFollowQuick("Distance"));
            footerFollowDistanceImage = footerFollowDistanceBtn.GetComponent<Image>();
            AddTooltip(footerFollowDistanceBtn, "Follow Distance");
            
            footerFollowHeightBtn = UI.CreateUIButton(pageContainer, 40, 40, "⊙", 20, 360, 0, AnchorPresets.middleLeft, () => ToggleFollowQuick("Height"));
            footerFollowHeightImage = footerFollowHeightBtn.GetComponent<Image>();
            AddTooltip(footerFollowHeightBtn, "Follow Eye Height");

            // Layout Mode Toggle
            footerLayoutBtn = UI.CreateUIButton(pageContainer, 40, 40, "▤", 20, 410, 0, AnchorPresets.middleLeft, ToggleLayoutMode);
            footerLayoutBtnImage = footerLayoutBtn.GetComponent<Image>();
            footerLayoutBtnText = footerLayoutBtn.GetComponentInChildren<Text>();
            AddTooltip(footerLayoutBtn, "Toggle Layout Mode (Grid/Card)");

            // Hover support
            AddHoverDelegate(paginationPrevBtn);
            AddTooltip(paginationPrevBtn, "Previous Page");
            AddHoverDelegate(paginationNextBtn);
            AddHoverDelegate(footerFollowAngleBtn);
            AddHoverDelegate(footerFollowDistanceBtn);
            AddHoverDelegate(footerFollowHeightBtn);
            AddHoverDelegate(footerLayoutBtn);

            // Hover Path Text (Bottom Right - now much wider)
            GameObject pathGO = new GameObject("HoverPathText");
            pathGO.transform.SetParent(backgroundBoxGO.transform, false);
            hoverPathText = pathGO.AddComponent<Text>();
            hoverPathText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hoverPathText.fontSize = 14;
            hoverPathText.color = new Color(1f, 1f, 1f, 0.7f);
            hoverPathText.alignment = TextAnchor.LowerRight;
            hoverPathText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hoverPathText.verticalOverflow = VerticalWrapMode.Truncate;
            hoverPathText.text = "";
            hoverPathText.raycastTarget = false;
            hoverPathRT = pathGO.GetComponent<RectTransform>();
            hoverPathRT.anchorMin = new Vector2(0, 0); // Stretch from left
            hoverPathRT.anchorMax = new Vector2(1, 0); // To right
            hoverPathRT.pivot = new Vector2(1, 0);
            hoverPathRT.anchoredPosition = new Vector2(-60, 10); // Offset from right scrollbar
            hoverPathRT.offsetMin = new Vector2(510, 10); // Start after pagination + new buttons
            hoverPathRT.offsetMax = new Vector2(-60, 75); // End before scrollbar, taller to show full file name

            UpdateSideButtonsVisibility();
            UpdateFooterFollowStates();
            UpdateFooterLayoutState();
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
                footerLayoutBtnText.text = (layoutMode == GalleryLayoutMode.VerticalCard) ? "≣" : "▤";
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

        private void PrevPage()
        {
            if (currentPage > 0)
            {
                currentPage--;
                RefreshFiles(false, true);
            }
        }

        private GameObject CreateCancelButtonsGroup(GameObject parent, float btnWidth, float btnHeight, float yOffset)
        {
            float tallHeight = btnHeight * 3f + 8f;
            GameObject container = UI.AddChildGOImage(parent, new Color(0, 0, 0, 0.02f), AnchorPresets.centre, btnWidth, tallHeight, new Vector2(0, yOffset));
            container.name = "CancelButtons";
            VerticalLayoutGroup vlg = container.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 0f;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            GameObject btn = UI.CreateUIButton(container, btnWidth, tallHeight, "Release\nHere To\nCancel", 18, 0, 0, AnchorPresets.centre, null);
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(btnWidth, tallHeight);
            LayoutElement le = btn.AddComponent<LayoutElement>();
            le.preferredHeight = tallHeight;
            le.preferredWidth = btnWidth;

            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = cancelZoneNormalColor;
            Text t = btn.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = new Color(1f, 1f, 1f, 0.9f);
                t.alignment = TextAnchor.MiddleCenter;
                t.supportRichText = false;
            }

            cancelDropZoneRTs.Add(rt);
            cancelDropZoneImages.Add(img);
            cancelDropZoneTexts.Add(t);
            AddHoverDelegate(btn);

            AddHoverDelegate(container);
            container.SetActive(false);
            cancelDropGroups.Add(container);
            return container;
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
            
            float bottomOffset = 60;

            contentScrollRT.offsetMin = new Vector2(leftOffset, bottomOffset);
            contentScrollRT.offsetMax = new Vector2(rightOffset, -55);

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
                float footerY = 0;
                paginationRT.anchoredPosition = new Vector2(50, footerY + 30);
                
                if (hoverPathRT != null)
                {
                    hoverPathRT.offsetMin = new Vector2(510, footerY + 10);
                    hoverPathRT.offsetMax = new Vector2(-60, footerY + 55);
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
        }

        private void UpdateButtonStates()
        {
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

            // Cancel zones
            float cancelY = -topY - 80f;
            foreach (var go in rightCancelGroups) if (go != null) go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, cancelY);
            foreach (var go in leftCancelGroups) if (go != null) go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, cancelY);
        }

        private SideButtonLayoutEntry[] GetSideButtonsLayout()
        {
            // gapBeforeUnits is how many "groupGap" units to insert before this row (in addition to normal spacing)
            return new SideButtonLayoutEntry[]
            {
                new SideButtonLayoutEntry(0, 0, 0),
                new SideButtonLayoutEntry(1, 0, 0),
                new SideButtonLayoutEntry(2, 0, 1),
                new SideButtonLayoutEntry(3, 0, 0),
                new SideButtonLayoutEntry(4, 0, 1),
                new SideButtonLayoutEntry(5, 0, 0),
                new SideButtonLayoutEntry(6, 0, 0),
                new SideButtonLayoutEntry(10, 0, 0),
                new SideButtonLayoutEntry(7, 0, 1),
                new SideButtonLayoutEntry(8, 0, 0),
                new SideButtonLayoutEntry(9, 0, 0),
                new SideButtonLayoutEntry(11, 0, 1),
            };
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
    }
}
