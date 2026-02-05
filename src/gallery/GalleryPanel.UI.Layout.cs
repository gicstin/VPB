using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
{        public void UpdateLayout()
        {
            if (!creatorsCached) CacheCreators();
            if (!categoriesCached) CacheCategoryCounts();

            if (contentScrollRT == null) return;

            try
            {
                if (titleSearchInput != null)
                {
                    RectTransform bgRT = backgroundBoxGO != null ? backgroundBoxGO.GetComponent<RectTransform>() : null;
                    RectTransform searchRT = titleSearchInput.GetComponent<RectTransform>();
                    if (bgRT != null && searchRT != null)
                    {
                        float w = bgRT.rect.width;

                        // Keep some safety space for the left title area and the right-side FPS display,
                        // plus buttons to the right of search.
                        float reservedLeft = 320f;
                        float reservedRight = 240f;
                        float reservedButtonsRightOfSearch = 190f;

                        float available = w - reservedLeft - reservedRight - reservedButtonsRightOfSearch;
                        float target = Mathf.Clamp(available, 100f, 240f);

                        if (Mathf.Abs(searchRT.sizeDelta.x - target) > 0.5f)
                        {
                            searchRT.sizeDelta = new Vector2(target, searchRT.sizeDelta.y);
                        }
                    }
                }
            }
            catch { }

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
                if (rightRefreshBtn != null) rightRefreshBtn.SetActive(true);

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
                }
            }
            else if (rightTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(false);
                if (rightSubTabScrollGO != null) rightSubTabScrollGO.SetActive(false);
                if (rightSearchInput != null) rightSearchInput.gameObject.SetActive(false);
                if (rightSortBtn != null) rightSortBtn.SetActive(false);
                if (rightRefreshBtn != null) rightRefreshBtn.SetActive(false);
                
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

            if (packageManagerContainer != null)
            {
                RectTransform rt = packageManagerContainer.GetComponent<RectTransform>();
                // Match horizontal offsets with contentScrollRT (dynamic side panels)
                // Use bottomOffset (60) to align with grid view, clearing the footer
                rt.offsetMin = new Vector2(leftOffset, bottomOffset);
                rt.offsetMax = new Vector2(rightOffset, topOffset);
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

        public void UpdateSideButtonPositions()
        {
            if (backgroundBoxGO == null) return;
            float spacing = 60f;
            float groupGap = VPBConfig.Instance.EnableButtonGaps ? 10f : 0f;
            float stackHeight = GetSideButtonsStackHeight(spacing, groupGap);
            float topY = stackHeight * 0.5f;

            // Settings
            UpdateListPositions(rightSideButtons, topY, spacing, groupGap);
            UpdateListPositions(leftSideButtons, topY, spacing, groupGap);

            try
            {
                string title = currentCategoryTitle ?? "";
                bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSubScene = title.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isScene = !isSubScene && title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isHair && hairSubmenuOpen)
                {
                    float xPad = 80f;

                    RectTransform leftBaseRT = leftRemoveAllHairBtn != null ? leftRemoveAllHairBtn.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightRemoveAllHairBtn != null ? rightRemoveAllHairBtn.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftRemoveHairSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftRemoveHairSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightRemoveHairSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveHairSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }
                    float yStart = -(visibleCount - 1) * 0.5f * spacing;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        for (int i = 0; i < leftRemoveHairSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftRemoveHairSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2(-(w * 0.5f) - xPad, baseY + yStart + spacing * i);
                        }

                        try
                        {
                            if (leftRemoveHairSubmenuGapPanelGO != null)
                            {
                                RectTransform prt = leftRemoveHairSubmenuGapPanelGO.GetComponent<RectTransform>();
                                if (prt != null)
                                {
                                    float panelW = 0f;
                                    float panelH = 0f;
                                    try
                                    {
                                        GameObject sample = leftRemoveHairSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (sample == null) sample = rightRemoveHairSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform srt = sample != null ? sample.GetComponent<RectTransform>() : null;
                                        panelW = srt != null ? srt.sizeDelta.x : 200f;
                                        panelH = srt != null ? srt.sizeDelta.y : spacing;
                                    }
                                    catch { panelW = 200f; panelH = spacing; }

                                    panelH = Mathf.Max(panelH, visibleCount * spacing);
                                    prt.sizeDelta = new Vector2(panelW, panelH);
                                    prt.anchoredPosition = new Vector2(-(panelW * 0.5f) - xPad, baseY);
                                    leftRemoveHairSubmenuGapPanelGO.transform.SetAsFirstSibling();
                                }
                            }
                        }
                        catch { }
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        for (int i = 0; i < rightRemoveHairSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveHairSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2((w * 0.5f) + xPad, baseY + yStart + spacing * i);
                        }

                        try
                        {
                            if (rightRemoveHairSubmenuGapPanelGO != null)
                            {
                                RectTransform prt = rightRemoveHairSubmenuGapPanelGO.GetComponent<RectTransform>();
                                if (prt != null)
                                {
                                    float panelW = 0f;
                                    float panelH = 0f;
                                    try
                                    {
                                        GameObject sample = rightRemoveHairSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (sample == null) sample = leftRemoveHairSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform srt = sample != null ? sample.GetComponent<RectTransform>() : null;
                                        panelW = srt != null ? srt.sizeDelta.x : 200f;
                                        panelH = srt != null ? srt.sizeDelta.y : spacing;
                                    }
                                    catch { panelW = 200f; panelH = spacing; }

                                    panelH = Mathf.Max(panelH, visibleCount * spacing);
                                    prt.sizeDelta = new Vector2(panelW, panelH);
                                    prt.anchoredPosition = new Vector2((panelW * 0.5f) + xPad, baseY);
                                    rightRemoveHairSubmenuGapPanelGO.transform.SetAsFirstSibling();
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (isClothing && clothingSubmenuOpen)
                {
                    float xPad = 80f;
                    float colGap = 10f;

                    RectTransform leftBaseRT = leftRemoveAllClothingBtn != null ? leftRemoveAllClothingBtn.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightRemoveAllClothingBtn != null ? rightRemoveAllClothingBtn.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftRemoveClothingSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftRemoveClothingSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightRemoveClothingSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveClothingSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }
                    float yStart = -(visibleCount - 1) * 0.5f * spacing;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        for (int i = 0; i < leftRemoveClothingSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftRemoveClothingSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2(-(w * 0.5f) - xPad, baseY + yStart + spacing * i);
                        }

                        for (int i = 0; i < leftRemoveClothingVisibilityToggleButtons.Count; i++)
                        {
                            GameObject go = leftRemoveClothingVisibilityToggleButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;

                            float itemW = 0f;
                            try
                            {
                                if (i < leftRemoveClothingSubmenuButtons.Count)
                                {
                                    RectTransform irt = leftRemoveClothingSubmenuButtons[i] != null ? leftRemoveClothingSubmenuButtons[i].GetComponent<RectTransform>() : null;
                                    if (irt != null) itemW = irt.sizeDelta.x;
                                }
                            }
                            catch { }
                            if (itemW <= 0f) itemW = 200f;

                            float w = rt.sizeDelta.x;
                            float itemCenterX = -(itemW * 0.5f) - xPad;
                            rt.anchoredPosition = new Vector2(itemCenterX - (itemW * 0.5f) - (w * 0.5f) - colGap, baseY + yStart + spacing * i);
                        }

                        try
                        {
                            if (leftRemoveClothingSubmenuPanelGO != null)
                            {
                                RectTransform prt = leftRemoveClothingSubmenuPanelGO.GetComponent<RectTransform>();
                                if (prt != null)
                                {
                                    float panelW = 0f;
                                    float panelH = 0f;
                                    float toggleW = 0f;
                                    try
                                    {
                                        GameObject tsample = leftRemoveClothingVisibilityToggleButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (tsample == null) tsample = rightRemoveClothingVisibilityToggleButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform trt = tsample != null ? tsample.GetComponent<RectTransform>() : null;
                                        toggleW = trt != null ? trt.sizeDelta.x : 80f;
                                    }
                                    catch { toggleW = 80f; }
                                    try
                                    {
                                        GameObject sample = leftRemoveClothingSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (sample == null) sample = rightRemoveClothingSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform srt = sample != null ? sample.GetComponent<RectTransform>() : null;
                                        float itemW = srt != null ? srt.sizeDelta.x : 200f;
                                        panelW = itemW + toggleW + colGap;
                                        panelH = srt != null ? srt.sizeDelta.y : spacing;
                                    }
                                    catch { panelW = 200f; panelH = spacing; }

                                    panelH = Mathf.Max(panelH, visibleCount * spacing);
                                    prt.sizeDelta = new Vector2(panelW, panelH);
                                    prt.anchoredPosition = new Vector2(-(panelW * 0.5f) - xPad, baseY);
                                    leftRemoveClothingSubmenuPanelGO.transform.SetAsFirstSibling();
                                }
                            }
                        }
                        catch { }
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        for (int i = 0; i < rightRemoveClothingSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveClothingSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2((w * 0.5f) + xPad, baseY + yStart + spacing * i);
                        }

                        for (int i = 0; i < rightRemoveClothingVisibilityToggleButtons.Count; i++)
                        {
                            GameObject go = rightRemoveClothingVisibilityToggleButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;

                            float itemW = 0f;
                            try
                            {
                                if (i < rightRemoveClothingSubmenuButtons.Count)
                                {
                                    RectTransform irt = rightRemoveClothingSubmenuButtons[i] != null ? rightRemoveClothingSubmenuButtons[i].GetComponent<RectTransform>() : null;
                                    if (irt != null) itemW = irt.sizeDelta.x;
                                }
                            }
                            catch { }
                            if (itemW <= 0f) itemW = 200f;

                            float w = rt.sizeDelta.x;
                            float itemCenterX = (itemW * 0.5f) + xPad;
                            rt.anchoredPosition = new Vector2(itemCenterX + (itemW * 0.5f) + (w * 0.5f) + colGap, baseY + yStart + spacing * i);
                        }

                        try
                        {
                            if (rightRemoveClothingSubmenuPanelGO != null)
                            {
                                RectTransform prt = rightRemoveClothingSubmenuPanelGO.GetComponent<RectTransform>();
                                if (prt != null)
                                {
                                    float panelW = 0f;
                                    float panelH = 0f;
                                    float toggleW = 0f;
                                    try
                                    {
                                        GameObject tsample = rightRemoveClothingVisibilityToggleButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (tsample == null) tsample = leftRemoveClothingVisibilityToggleButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform trt = tsample != null ? tsample.GetComponent<RectTransform>() : null;
                                        toggleW = trt != null ? trt.sizeDelta.x : 80f;
                                    }
                                    catch { toggleW = 80f; }
                                    try
                                    {
                                        GameObject sample = rightRemoveClothingSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (sample == null) sample = leftRemoveClothingSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform srt = sample != null ? sample.GetComponent<RectTransform>() : null;
                                        float itemW = srt != null ? srt.sizeDelta.x : 200f;
                                        panelW = itemW + toggleW + colGap;
                                        panelH = srt != null ? srt.sizeDelta.y : spacing;
                                    }
                                    catch { panelW = 200f; panelH = spacing; }

                                    panelH = Mathf.Max(panelH, visibleCount * spacing);
                                    prt.sizeDelta = new Vector2(panelW, panelH);
                                    prt.anchoredPosition = new Vector2((panelW * 0.5f) + xPad, baseY);
                                    rightRemoveClothingSubmenuPanelGO.transform.SetAsFirstSibling();
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (isScene && atomSubmenuOpen)
                {
                    float xPad = 80f;

                    RectTransform leftBaseRT = leftRemoveAtomBtn != null ? leftRemoveAtomBtn.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightRemoveAtomBtn != null ? rightRemoveAtomBtn.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftRemoveAtomSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftRemoveAtomSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightRemoveAtomSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveAtomSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }
                    float yStart = -(visibleCount - 1) * 0.5f * spacing;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        for (int i = 0; i < leftRemoveAtomSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftRemoveAtomSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2(-(w * 0.5f) - xPad, baseY + yStart + spacing * i);
                        }
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        for (int i = 0; i < rightRemoveAtomSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightRemoveAtomSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2((w * 0.5f) + xPad, baseY + yStart + spacing * i);
                        }
                    }
                }

                if (saveSubmenuOpen)
                {
                    float xPad = 80f;

                    RectTransform leftBaseRT = leftSaveBtnGO != null ? leftSaveBtnGO.GetComponent<RectTransform>() : null;
                    RectTransform rightBaseRT = rightSaveBtnGO != null ? rightSaveBtnGO.GetComponent<RectTransform>() : null;

                    int visibleCount = 0;
                    for (int i = 0; i < leftSaveSubmenuButtons.Count; i++)
                    {
                        GameObject go = leftSaveSubmenuButtons[i];
                        if (go != null && go.activeSelf) visibleCount++;
                    }
                    if (visibleCount == 0)
                    {
                        for (int i = 0; i < rightSaveSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightSaveSubmenuButtons[i];
                            if (go != null && go.activeSelf) visibleCount++;
                        }
                    }
                    float yStart = -(visibleCount - 1) * 0.5f * spacing;

                    if (leftBaseRT != null)
                    {
                        float baseY = leftBaseRT.anchoredPosition.y;
                        for (int i = 0; i < leftSaveSubmenuButtons.Count; i++)
                        {
                            GameObject go = leftSaveSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2(-(w * 0.5f) - xPad, baseY + yStart + spacing * i);
                        }

                        try
                        {
                            if (leftSaveSubmenuPanelGO != null)
                            {
                                RectTransform prt = leftSaveSubmenuPanelGO.GetComponent<RectTransform>();
                                if (prt != null)
                                {
                                    float panelW = 0f;
                                    float panelH = 0f;
                                    try
                                    {
                                        GameObject sample = leftSaveSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (sample == null) sample = rightSaveSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform srt = sample != null ? sample.GetComponent<RectTransform>() : null;
                                        panelW = srt != null ? srt.sizeDelta.x : 200f;
                                        panelH = srt != null ? srt.sizeDelta.y : spacing;
                                    }
                                    catch { panelW = 200f; panelH = spacing; }

                                    panelH = Mathf.Max(panelH, visibleCount * spacing);
                                    prt.sizeDelta = new Vector2(panelW, panelH);
                                    prt.anchoredPosition = new Vector2(-(panelW * 0.5f) - xPad, baseY);
                                    leftSaveSubmenuPanelGO.transform.SetAsFirstSibling();
                                }
                            }
                        }
                        catch { }
                    }

                    if (rightBaseRT != null)
                    {
                        float baseY = rightBaseRT.anchoredPosition.y;
                        for (int i = 0; i < rightSaveSubmenuButtons.Count; i++)
                        {
                            GameObject go = rightSaveSubmenuButtons[i];
                            if (go == null || !go.activeSelf) continue;
                            RectTransform rt = go.GetComponent<RectTransform>();
                            if (rt == null) continue;
                            float w = rt.sizeDelta.x;
                            rt.anchoredPosition = new Vector2((w * 0.5f) + xPad, baseY + yStart + spacing * i);
                        }

                        try
                        {
                            if (rightSaveSubmenuPanelGO != null)
                            {
                                RectTransform prt = rightSaveSubmenuPanelGO.GetComponent<RectTransform>();
                                if (prt != null)
                                {
                                    float panelW = 0f;
                                    float panelH = 0f;
                                    try
                                    {
                                        GameObject sample = rightSaveSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        if (sample == null) sample = leftSaveSubmenuButtons.FirstOrDefault(g => g != null && g.activeSelf);
                                        RectTransform srt = sample != null ? sample.GetComponent<RectTransform>() : null;
                                        panelW = srt != null ? srt.sizeDelta.x : 200f;
                                        panelH = srt != null ? srt.sizeDelta.y : spacing;
                                    }
                                    catch { panelW = 200f; panelH = spacing; }

                                    panelH = Mathf.Max(panelH, visibleCount * spacing);
                                    prt.sizeDelta = new Vector2(panelW, panelH);
                                    prt.anchoredPosition = new Vector2((panelW * 0.5f) + xPad, baseY);
                                    rightSaveSubmenuPanelGO.transform.SetAsFirstSibling();
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private SideButtonLayoutEntry[] GetSideButtonsLayout()
        {
            string title = currentCategoryTitle ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSubScene = title.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isScene = !isSubScene && title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;

            int idxSettings = -1;
            int idxFloating = -1;
            int idxClone = -1;
            int idxFollow = -1;
            int idxCategory = -1;
            int idxCreator = -1;
            int idxTarget = -1;
            int idxApplyMode = -1;
            int idxReplace = -1;
            int idxRandom = 4;
            int idxRemoveHair = 15;
            int idxRemoveClothing = 14;
            int idxRemoveAtom = -1;
            int idxHub = 12;
            int idxUndo = 13;
            int idxRedo = 16;
            int idxSave = -1;
            try
            {
                List<RectTransform> refList = rightSideButtons;
                if (refList == null || refList.Count == 0) refList = leftSideButtons;
                if (refList != null)
                {
                    int FindIndexByTextRef(Text t)
                    {
                        if (t == null) return -1;
                        return refList.FindIndex(rt => rt != null && rt.GetComponentInChildren<Text>() == t);
                    }

                    int FindIndexByExactLabel(string label)
                    {
                        if (string.IsNullOrEmpty(label)) return -1;
                        return refList.FindIndex(rt => {
                            if (rt == null) return false;
                            Text tx = rt.GetComponentInChildren<Text>();
                            if (tx == null) return false;
                            return string.Equals(tx.text, label, StringComparison.Ordinal);
                        });
                    }

                    idxCategory = FindIndexByTextRef(rightCategoryBtnText != null ? rightCategoryBtnText : leftCategoryBtnText);
                    idxCreator = FindIndexByTextRef(rightCreatorBtnText != null ? rightCreatorBtnText : leftCreatorBtnText);
                    idxTarget = FindIndexByTextRef(rightTargetBtnText != null ? rightTargetBtnText : leftTargetBtnText);
                    idxApplyMode = FindIndexByTextRef(rightApplyModeBtnText != null ? rightApplyModeBtnText : leftApplyModeBtnText);
                    idxReplace = FindIndexByTextRef(rightReplaceBtnText != null ? rightReplaceBtnText : leftReplaceBtnText);
                    idxFloating = FindIndexByTextRef(rightDesktopModeBtnText != null ? rightDesktopModeBtnText : leftDesktopModeBtnText);
                    idxFollow = FindIndexByTextRef(rightFollowBtnText != null ? rightFollowBtnText : leftFollowBtnText);

                    // Settings and Clone don't currently have stored Text refs.
                    idxSettings = FindIndexByExactLabel("Settings");
                    idxClone = FindIndexByExactLabel("Clone");

                    if (rightLoadRandomBtn != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == rightLoadRandomBtn);
                        if (i >= 0) idxRandom = i;
                    }

                    if (rightRemoveAllHairBtn != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == rightRemoveAllHairBtn);
                        if (i >= 0) idxRemoveHair = i;
                    }

                    if (rightRemoveAllClothingBtn != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == rightRemoveAllClothingBtn);
                        if (i >= 0) idxRemoveClothing = i;
                    }

                    GameObject removeAtomGo = rightRemoveAtomBtn != null ? rightRemoveAtomBtn : leftRemoveAtomBtn;
                    if (removeAtomGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == removeAtomGo);
                        if (i >= 0) idxRemoveAtom = i;
                    }

                    GameObject hubGo = rightHubBtnGO != null ? rightHubBtnGO : leftHubBtnGO;
                    if (hubGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == hubGo);
                        if (i >= 0) idxHub = i;
                    }

                    GameObject undoGo = rightUndoBtnGO != null ? rightUndoBtnGO : leftUndoBtnGO;
                    if (undoGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == undoGo);
                        if (i >= 0) idxUndo = i;
                    }

                    GameObject redoGo = rightRedoBtnGO != null ? rightRedoBtnGO : leftRedoBtnGO;
                    if (redoGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == redoGo);
                        if (i >= 0) idxRedo = i;
                    }

                    GameObject saveGo = rightSaveBtnGO != null ? rightSaveBtnGO : leftSaveBtnGO;
                    if (saveGo != null)
                    {
                        int i = refList.FindIndex(rt => rt != null && rt.gameObject == saveGo);
                        if (i >= 0) idxSave = i;
                    }
                }
            }
            catch { }

            var layout = new List<SideButtonLayoutEntry>()
            {
                new SideButtonLayoutEntry(idxSettings, 0, 0), // Settings

                new SideButtonLayoutEntry(idxFloating, 0, 2), // Floating
                new SideButtonLayoutEntry(idxClone, 0, 0), // Clone
                new SideButtonLayoutEntry(idxFollow, 0, 0), // Follow

                new SideButtonLayoutEntry(idxCategory, 0, 2), // Category
                new SideButtonLayoutEntry(idxCreator, 0, 0), // Creator
                new SideButtonLayoutEntry(idxHub, 0, 0), // Hub

                new SideButtonLayoutEntry(idxSave, 0, 2), // Save

                new SideButtonLayoutEntry(idxTarget, 0, 0), // Target
                new SideButtonLayoutEntry(idxApplyMode, 0, 0), // Apply Mode
                new SideButtonLayoutEntry(idxReplace, 0, 0), // Replace

                new SideButtonLayoutEntry(idxRemoveClothing, 0, 0), // Remove Clothing (context)
                new SideButtonLayoutEntry(idxRemoveAtom, 0, 0), // Remove Atom (scene)
                new SideButtonLayoutEntry(idxRemoveHair, 0, 0), // Remove Hair (context)
            };

            layout.Add(new SideButtonLayoutEntry(idxRandom, 0, 2)); // Random
            layout.Add(new SideButtonLayoutEntry(idxUndo, 0, 0)); // Undo
            layout.Add(new SideButtonLayoutEntry(idxRedo, 0, 0)); // Redo
            return layout.ToArray();
        }

        private void SetAtomSubmenuButtonsVisible(bool visible)
        {
            try
            {
                for (int i = 0; i < rightRemoveAtomSubmenuButtons.Count; i++)
                {
                    if (rightRemoveAtomSubmenuButtons[i] != null) rightRemoveAtomSubmenuButtons[i].SetActive(visible);
                }
                for (int i = 0; i < leftRemoveAtomSubmenuButtons.Count; i++)
                {
                    if (leftRemoveAtomSubmenuButtons[i] != null) leftRemoveAtomSubmenuButtons[i].SetActive(visible);
                }
            }
            catch { }
        }

        private void PopulateAtomSubmenuButtons()
        {
            SetAtomSubmenuButtonsVisible(false);

            if (SuperController.singleton == null) return;

            bool IsEssentialAtom(Atom a)
            {
                if (a == null) return true;
                string uid = null;
                string type = null;
                try { uid = a.uid; } catch { }
                try { type = a.type; } catch { }

                if (!string.IsNullOrEmpty(type) && type.Equals("CoreControl", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(uid) && uid.Equals("CoreControl", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(uid) && uid.StartsWith("CoreControl", StringComparison.OrdinalIgnoreCase)) return true;

                if (!string.IsNullOrEmpty(type) && type.Equals("VRController", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(uid) && uid.Equals("CameraRig", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(uid) && uid.IndexOf("[CameraRig]", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }

            List<Atom> atoms = null;
            try { atoms = SuperController.singleton.GetAtoms(); }
            catch { }
            if (atoms == null) atoms = new List<Atom>();

            var options = new List<KeyValuePair<string, string>>();
            try
            {
                for (int i = 0; i < atoms.Count; i++)
                {
                    Atom a = atoms[i];
                    if (a == null) continue;
                    if (string.IsNullOrEmpty(a.uid)) continue;

                    if (IsEssentialAtom(a)) continue;

                    string label = a.uid;
                    try
                    {
                        if (!string.IsNullOrEmpty(a.type)) label = a.type + ": " + a.uid;
                    }
                    catch { }

                    options.Add(new KeyValuePair<string, string>(a.uid, label));
                }

                options = options
                    .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { }

            int count = Mathf.Min(options.Count, AtomSubmenuMaxButtons);

            for (int i = 0; i < AtomSubmenuMaxButtons; i++)
            {
                string uid = i < count ? options[i].Key : null;
                string label = i < count ? options[i].Value : null;

                void Configure(GameObject btnGO)
                {
                    if (btnGO == null) return;
                    Button btn = btnGO.GetComponent<Button>();
                    Text t = btnGO.GetComponentInChildren<Text>();
                    if (t != null) t.text = label ?? "";
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.interactable = !string.IsNullOrEmpty(uid);
                        if (!string.IsNullOrEmpty(uid))
                        {
                            btn.onClick.AddListener(() => {
                                try
                                {
                                    if (SuperController.singleton == null) return;
                                    Atom a = null;
                                    try { a = SuperController.singleton.GetAtomByUid(uid); }
                                    catch { }
                                    if (a == null) return;
                                    if (IsEssentialAtom(a)) return;
                                    PushUndoSnapshotForAtomRemoval(a);
                                    SuperController.singleton.RemoveAtom(a);
                                }
                                finally
                                {
                                    atomSubmenuOpen = false;
                                    SetAtomSubmenuButtonsVisible(false);
                                    UpdateSideButtonPositions();
                                }
                            });
                        }
                    }
                    btnGO.SetActive(i < count);
                }

                if (i < rightRemoveAtomSubmenuButtons.Count) Configure(rightRemoveAtomSubmenuButtons[i]);
                if (i < leftRemoveAtomSubmenuButtons.Count) Configure(leftRemoveAtomSubmenuButtons[i]);
            }
        }

        private void ToggleAtomSubmenuFromSideButtons()
        {
            atomSubmenuOpen = !atomSubmenuOpen;
            if (atomSubmenuOpen)
            {
                CloseOtherSubmenus("Atom");
                PopulateAtomSubmenuButtons();
            }
            else
            {
                SetAtomSubmenuButtonsVisible(false);
            }

            UpdateSideButtonPositions();
        }

        private void PushUndoSnapshotForAtomRemoval(Atom atom)
        {
            if (atom == null) return;
            if (SuperController.singleton == null) return;

            try
            {
                string atomUid = null;
                try { atomUid = atom.uid; } catch { }
                if (string.IsNullOrEmpty(atomUid)) return;

                JSONNode atomNode = null;
                try
                {
                    // Try common serialization methods (VaM versions differ)
                    string[] candidates = new[] { "GetSaveJSON", "GetJSON", "GetAtomJSON", "GetSceneJSON" };
                    for (int i = 0; i < candidates.Length && atomNode == null; i++)
                    {
                        MethodInfo mi = null;
                        try { mi = atom.GetType().GetMethod(candidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch { }
                        if (mi == null) continue;
                        var ps = mi.GetParameters();
                        if (ps != null && ps.Length != 0) continue;
                        object result = null;
                        try { result = mi.Invoke(atom, null); }
                        catch { }
                        if (result == null) continue;
                        if (result is JSONNode node)
                        {
                            atomNode = node;
                        }
                        else
                        {
                            try
                            {
                                string s = result.ToString();
                                if (!string.IsNullOrEmpty(s)) atomNode = JSON.Parse(s);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                if (atomNode == null) return;

                // Ensure id exists for merge-load
                try
                {
                    if (atomNode["id"] == null || string.IsNullOrEmpty(atomNode["id"].Value)) atomNode["id"] = atomUid;
                }
                catch { }

                // Freeze to a string now (atom may be removed after this)
                string atomJson = null;
                try { atomJson = atomNode.ToString(); }
                catch { }
                if (string.IsNullOrEmpty(atomJson)) return;

                JSONClass mini = new JSONClass();
                JSONArray one = new JSONArray();
                try { one.Add(JSON.Parse(atomJson)); }
                catch { one.Add(atomNode); }
                mini["atoms"] = one;

                string undoTempPath = Path.Combine(SuperController.singleton.savesDir, "vpb_temp_undo_atom_" + Guid.NewGuid().ToString() + ".json");
                File.WriteAllText(undoTempPath, mini.ToString());

                PushUndo(() => {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        if (!File.Exists(undoTempPath)) return;

                        SceneLoadingUtils.LoadScene(undoTempPath, true);
                    }
                    catch { }
                    finally
                    {
                        try { if (File.Exists(undoTempPath)) File.Delete(undoTempPath); } catch { }
                    }
                });
            }
            catch { }
        }

        private void SetHairSubmenuButtonsVisible(bool visible)
        {
            try
            {
                if (rightRemoveHairSubmenuGapPanelGO != null) rightRemoveHairSubmenuGapPanelGO.SetActive(visible);
                if (leftRemoveHairSubmenuGapPanelGO != null) leftRemoveHairSubmenuGapPanelGO.SetActive(visible);

                for (int i = 0; i < rightRemoveHairSubmenuButtons.Count; i++)
                {
                    if (rightRemoveHairSubmenuButtons[i] != null) rightRemoveHairSubmenuButtons[i].SetActive(visible);
                }
                for (int i = 0; i < leftRemoveHairSubmenuButtons.Count; i++)
                {
                    if (leftRemoveHairSubmenuButtons[i] != null) leftRemoveHairSubmenuButtons[i].SetActive(visible);
                }
            }
            catch { }
        }

        private void SetClothingSubmenuButtonsVisible(bool visible)
        {
            try
            {
                if (rightRemoveClothingSubmenuPanelGO != null) rightRemoveClothingSubmenuPanelGO.SetActive(visible);
                if (leftRemoveClothingSubmenuPanelGO != null) leftRemoveClothingSubmenuPanelGO.SetActive(visible);

                for (int i = 0; i < rightRemoveClothingVisibilityToggleButtons.Count; i++)
                {
                    if (rightRemoveClothingVisibilityToggleButtons[i] != null) rightRemoveClothingVisibilityToggleButtons[i].SetActive(false);
                }
                for (int i = 0; i < leftRemoveClothingVisibilityToggleButtons.Count; i++)
                {
                    if (leftRemoveClothingVisibilityToggleButtons[i] != null) leftRemoveClothingVisibilityToggleButtons[i].SetActive(false);
                }

                if (!visible)
                {
                    for (int i = 0; i < rightRemoveClothingSubmenuButtons.Count; i++)
                    {
                        if (rightRemoveClothingSubmenuButtons[i] != null) rightRemoveClothingSubmenuButtons[i].SetActive(false);
                    }
                    for (int i = 0; i < leftRemoveClothingSubmenuButtons.Count; i++)
                    {
                        if (leftRemoveClothingSubmenuButtons[i] != null) leftRemoveClothingSubmenuButtons[i].SetActive(false);
                    }
                }
            }
            catch { }
        }

        private void PopulateHairSubmenuButtons(Atom target)
        {
            // Avoid briefly hiding buttons during periodic resync while the pointer is over the submenu.
            if (!hairSubmenuOpen) SetHairSubmenuButtonsVisible(false);

            if (target == null) return;

            try
            {
                hairSubmenuTargetAtomUid = target.uid;
            }
            catch { }

            List<KeyValuePair<string, string>> options = null;
            try
            {
                var items = new List<KeyValuePair<string, string>>();
                DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                if (dcs != null && dcs.hairItems != null)
                {
                    foreach (var item in dcs.hairItems)
                    {
                        if (item == null || !item.active) continue;

                        string path = null;
                        try { path = item.uid; } catch { }
                        if (string.IsNullOrEmpty(path) || (!path.Contains(":/") && !path.Contains(":\\")))
                        {
                            try
                            {
                                string internalId = null;
                                string containingVAMDir = null;
                                Type it = item.GetType();

                                FieldInfo fInternalId = it.GetField("internalId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (fInternalId != null) internalId = fInternalId.GetValue(item) as string;

                                FieldInfo fVamDir = it.GetField("containingVAMDir", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (fVamDir != null) containingVAMDir = fVamDir.GetValue(item) as string;

                                if (string.IsNullOrEmpty(internalId))
                                {
                                    FieldInfo fItemPath = it.GetField("itemPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fItemPath != null) internalId = fItemPath.GetValue(item) as string;
                                }

                                if (!string.IsNullOrEmpty(containingVAMDir) && !string.IsNullOrEmpty(internalId))
                                {
                                    path = containingVAMDir.Replace("\\", "/").TrimEnd('/') + "/" + internalId.Replace("\\", "/").TrimStart('/');
                                }
                            }
                            catch { }
                        }

                        if (string.IsNullOrEmpty(path)) continue;
                        string p = path.Replace("\\", "/");
                        string pl = p.ToLowerInvariant();
                        int idx = pl.IndexOf("/custom/hair/");
                        if (idx < 0) idx = pl.IndexOf("/hair/");
                        if (idx >= 0)
                        {
                            string sub = p.Substring(idx);
                            string[] parts = sub.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int pi = 0; pi < parts.Length; pi++) parts[pi] = parts[pi].Trim();

                            string typeFolder = (parts.Length >= 4) ? parts[3] : null;
                            string fileName = null;
                            try
                            {
                                string last = parts.Length > 0 ? parts[parts.Length - 1] : null;
                                if (!string.IsNullOrEmpty(last))
                                {
                                    int dot = last.LastIndexOf('.');
                                    fileName = dot > 0 ? last.Substring(0, dot) : last;
                                }
                            }
                            catch { }

                            if (string.IsNullOrEmpty(fileName))
                            {
                                try { fileName = item.name; }
                                catch { }
                            }

                            string label = !string.IsNullOrEmpty(typeFolder)
                                ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + (fileName ?? ""))
                                : (fileName ?? "");

                            if (!string.IsNullOrEmpty(label))
                            {
                                items.Add(new KeyValuePair<string, string>(item.uid, label));
                            }
                        }
                    }
                }

                options = items
                    .Where(kvp => !string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                    .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { }

            if (options == null) options = new List<KeyValuePair<string, string>>();
            int optionTotal = options.Count;
            int count = Mathf.Min(optionTotal, HairSubmenuMaxButtons);

            try { hairSubmenuLastOptionCount = optionTotal; } catch { }
            UpdateRemoveHairButtonLabels(optionTotal);

            // Populate buttons on both sides (they share the same label/callback).
            for (int i = 0; i < HairSubmenuMaxButtons; i++)
            {
                string uid = i < count ? options[i].Key : null;
                string label = i < count ? options[i].Value : null;

                void Configure(GameObject btnGO)
                {
                    if (btnGO == null) return;
                    Button btn = btnGO.GetComponent<Button>();
                    Text t = btnGO.GetComponentInChildren<Text>();
                    if (t != null) t.text = label ?? "";

                    if (btn != null) btn.transition = Selectable.Transition.None;

                    try
                    {
                        var et = btnGO.GetComponent<EventTrigger>();
                        if (et == null) et = btnGO.AddComponent<EventTrigger>();

                        if (et.triggers == null) et.triggers = new List<EventTrigger.Entry>();
                        et.triggers.RemoveAll(e => e != null && (e.eventID == EventTriggerType.PointerEnter || e.eventID == EventTriggerType.PointerExit));

                        var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        enterEntry.callback.AddListener((data) => {
                            try
                            {
                                hairSubmenuOptionsHoverCount++;
                                hairSubmenuOptionsHovered = true;
                                hairSubmenuLastHoverTime = Time.unscaledTime;

                                Atom tgt = null;
                                try
                                {
                                    if (!string.IsNullOrEmpty(hairSubmenuTargetAtomUid)) tgt = SuperController.singleton.GetAtomByUid(hairSubmenuTargetAtomUid);
                                }
                                catch { }
                                if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;

                                if (tgt != null && !string.IsNullOrEmpty(uid))
                                {
                                    ApplyHairPreview(tgt, uid);
                                }
                            }
                            catch { }
                        });
                        et.triggers.Add(enterEntry);

                        var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        exitEntry.callback.AddListener((data) => {
                            try
                            {
                                hairSubmenuOptionsHoverCount--;
                                if (hairSubmenuOptionsHoverCount < 0) hairSubmenuOptionsHoverCount = 0;
                                hairSubmenuOptionsHovered = hairSubmenuOptionsHoverCount > 0;
                                hairSubmenuLastHoverTime = Time.unscaledTime;

                                Atom tgt = null;
                                try
                                {
                                    if (!string.IsNullOrEmpty(hairSubmenuTargetAtomUid)) tgt = SuperController.singleton.GetAtomByUid(hairSubmenuTargetAtomUid);
                                }
                                catch { }
                                if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;

                                if (tgt != null && !string.IsNullOrEmpty(uid))
                                {
                                    ClearHairPreview(tgt, uid);
                                }
                            }
                            catch { }
                        });
                        et.triggers.Add(exitEntry);
                    }
                    catch { }
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        if (!string.IsNullOrEmpty(uid))
                        {
                            btn.onClick.AddListener(() => {
                                Atom tgt = null;
                                try
                                {
                                    try
                                    {
                                        if (!string.IsNullOrEmpty(hairSubmenuTargetAtomUid)) tgt = SuperController.singleton.GetAtomByUid(hairSubmenuTargetAtomUid);
                                    }
                                    catch { }
                                    if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                                    if (tgt == null) return;

                                    ClearHairPreview();

                                    UIDraggableItem dragger = rightRemoveAllHairBtn != null ? rightRemoveAllHairBtn.GetComponent<UIDraggableItem>() : null;
                                    if (dragger == null && rightRemoveAllHairBtn != null) dragger = rightRemoveAllHairBtn.AddComponent<UIDraggableItem>();
                                    if (dragger != null)
                                    {
                                        dragger.Panel = this;
                                        dragger.RemoveHairItemByUid(tgt, uid);
                                    }
                                }
                                catch { }
                                finally
                                {
                                    // Keep submenu open; SyncHairSubmenu will close only if no options remain.
                                    if (tgt != null)
                                    {
                                        SyncHairSubmenu(tgt, true);
                                        hairSubmenuLastHoverTime = Time.unscaledTime;
                                        hairSubmenuParentHovered = true;
                                        hairSubmenuOptionsHovered = true;
                                        hairSubmenuParentHoverCount = Mathf.Max(1, hairSubmenuParentHoverCount);
                                        hairSubmenuOptionsHoverCount = Mathf.Max(1, hairSubmenuOptionsHoverCount);
                                    }
                                }
                            });
                        }
                    }
                    btnGO.SetActive(i < count);
                }

                if (i < rightRemoveHairSubmenuButtons.Count) Configure(rightRemoveHairSubmenuButtons[i]);
                if (i < leftRemoveHairSubmenuButtons.Count) Configure(leftRemoveHairSubmenuButtons[i]);
            }
        }

        private void ToggleHairSubmenuFromSideButtons(Atom target)
        {
            hairSubmenuOpen = !hairSubmenuOpen;
            if (hairSubmenuOpen)
            {
                CloseOtherSubmenus("Hair");
                ClearHairPreview();
                PopulateHairSubmenuButtons(target);
            }
            else
            {
                CloseHairSubmenuUI();
            }

            UpdateSideButtonPositions();
        }

        private void CloseHairSubmenuUI()
        {
            try
            {
                ClearHairPreview();
                hairSubmenuOpen = false;
                hairSubmenuParentHovered = false;
                hairSubmenuOptionsHovered = false;
                hairSubmenuParentHoverCount = 0;
                hairSubmenuOptionsHoverCount = 0;
                hairSubmenuLastOptionCount = 0;
                SetHairSubmenuButtonsVisible(false);
            }
            catch { }
        }

        private void SyncHairSubmenu(Atom target, bool keepOpenIfHasOptions)
        {
            if (target == null) { CloseHairSubmenuUI(); return; }
            PopulateHairSubmenuButtons(target);
            int options = 0;
            try { options = hairSubmenuLastOptionCount; }
            catch { options = 0; }

            if (options <= 0)
            {
                CloseHairSubmenuUI();
            }
            else
            {
                hairSubmenuOpen = keepOpenIfHasOptions;
                UpdateRemoveHairButtonLabels(options);
            }
            UpdateSideButtonPositions();
        }

        private void UpdateRemoveHairButtonLabels(int optionCount)
        {
            UpdateRemoveButtonLabels(leftRemoveAllHairBtn, rightRemoveAllHairBtn, "Remove\nHair", optionCount);
        }

        private void ApplyHairPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;

                if (!string.IsNullOrEmpty(previewRemoveHairAtomUid) && !string.IsNullOrEmpty(previewRemoveHairItemUid))
                {
                    if (!string.Equals(previewRemoveHairAtomUid, target.uid, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previewRemoveHairItemUid, itemUid, StringComparison.OrdinalIgnoreCase))
                    {
                        ClearHairPreview();
                    }
                }

                if (!string.IsNullOrEmpty(previewRemoveHairAtomUid) && !string.IsNullOrEmpty(previewRemoveHairItemUid))
                {
                    return;
                }

                JSONStorable geometry = null;
                try { geometry = target.GetStorableByID("geometry"); } catch { }
                if (geometry == null) return;

                JSONStorableBool active = null;
                try { active = geometry.GetBoolJSONParam("hair:" + itemUid); } catch { }
                if (active == null) return;

                previewRemoveHairAtomUid = target.uid;
                previewRemoveHairItemUid = itemUid;
                previewRemoveHairPrevGeometryVal = active.val;

                if (active.val) active.val = false;
            }
            catch { }
        }

        private void ClearHairPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;
                if (string.IsNullOrEmpty(previewRemoveHairAtomUid) || string.IsNullOrEmpty(previewRemoveHairItemUid)) return;
                if (!string.Equals(previewRemoveHairAtomUid, target.uid, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.Equals(previewRemoveHairItemUid, itemUid, StringComparison.OrdinalIgnoreCase)) return;
                RestoreHairPreview();
            }
            catch { }
        }

        private void ClearHairPreview()
        {
            try { RestoreHairPreview(); }
            catch { }
        }

        private void RestoreHairPreview()
        {
            try
            {
                if (string.IsNullOrEmpty(previewRemoveHairAtomUid) || string.IsNullOrEmpty(previewRemoveHairItemUid))
                {
                    previewRemoveHairAtomUid = null;
                    previewRemoveHairItemUid = null;
                    previewRemoveHairPrevGeometryVal = null;
                    return;
                }

                Atom atom = null;
                try { atom = SuperController.singleton.GetAtomByUid(previewRemoveHairAtomUid); } catch { }
                if (atom == null)
                {
                    previewRemoveHairAtomUid = null;
                    previewRemoveHairItemUid = null;
                    previewRemoveHairPrevGeometryVal = null;
                    return;
                }

                JSONStorable geometry = null;
                try { geometry = atom.GetStorableByID("geometry"); } catch { }
                if (geometry != null)
                {
                    JSONStorableBool active = null;
                    try { active = geometry.GetBoolJSONParam("hair:" + previewRemoveHairItemUid); } catch { }
                    if (active != null && previewRemoveHairPrevGeometryVal.HasValue)
                    {
                        active.val = previewRemoveHairPrevGeometryVal.Value;
                    }
                }

                previewRemoveHairAtomUid = null;
                previewRemoveHairItemUid = null;
                previewRemoveHairPrevGeometryVal = null;
            }
            catch
            {
                previewRemoveHairAtomUid = null;
                previewRemoveHairItemUid = null;
                previewRemoveHairPrevGeometryVal = null;
            }
        }

        private void UpdateSideContextActions()
        {
            string title = currentCategoryTitle ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSubScene = title.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isScene = !isSubScene && title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
            bool showSave = !IsHubMode;

            if (rightRemoveAllClothingBtn != null) rightRemoveAllClothingBtn.SetActive(isClothing);
            if (leftRemoveAllClothingBtn != null) leftRemoveAllClothingBtn.SetActive(isClothing);
            if (rightRemoveAllHairBtn != null) rightRemoveAllHairBtn.SetActive(isHair);
            if (leftRemoveAllHairBtn != null) leftRemoveAllHairBtn.SetActive(isHair);
            if (rightRemoveAtomBtn != null) rightRemoveAtomBtn.SetActive(isScene);
            if (leftRemoveAtomBtn != null) leftRemoveAtomBtn.SetActive(isScene);
            if (rightSaveBtnGO != null) rightSaveBtnGO.SetActive(showSave);
            if (leftSaveBtnGO != null) leftSaveBtnGO.SetActive(showSave);

            // Update arrow indicators immediately (not only after submenu hover).
            if (isClothing)
            {
                Atom tgt = null;
                try { tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom; } catch { }

                int count = 0;
                try
                {
                    if (tgt != null)
                    {
                        // When submenu is open, keep label count stable by using the cached submenu option count.
                        if (clothingSubmenuOpen)
                        {
                            count = clothingSubmenuLastOptionCount;
                        }
                        else
                        {
                        bool shouldCheck = true;
                        try
                        {
                            if (!string.IsNullOrEmpty(clothingLabelLastAtomUid) && string.Equals(clothingLabelLastAtomUid, tgt.uid, StringComparison.OrdinalIgnoreCase))
                            {
                                if (Time.unscaledTime - clothingLabelLastCheckTime < 0.25f) shouldCheck = false;
                            }
                        }
                        catch { }

                        if (!shouldCheck)
                        {
                            count = clothingLabelLastCount;
                        }
                        else
                        {
                            try
                            {
                                JSONStorable geometry = null;
                                try { geometry = tgt.GetStorableByID("geometry"); } catch { }
                                if (geometry != null)
                                {
                                    foreach (var name in geometry.GetBoolParamNames())
                                    {
                                        if (string.IsNullOrEmpty(name)) continue;
                                        if (!name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;

                                        string clothingUid = null;
                                        try { clothingUid = name.Substring(9); } catch { }
                                        if (string.IsNullOrEmpty(clothingUid)) continue;

                                        // Skip built-in clothing (ref impl does this to avoid issues)
                                        if (!clothingUid.Contains("/")) continue;

                                        bool isPreviewItem = (!string.IsNullOrEmpty(previewRemoveClothingAtomUid)
                                            && !string.IsNullOrEmpty(previewRemoveClothingItemUid)
                                            && string.Equals(previewRemoveClothingAtomUid, tgt.uid, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(previewRemoveClothingItemUid, clothingUid, StringComparison.OrdinalIgnoreCase)
                                            && previewRemoveClothingPrevGeometryVal.HasValue
                                            && previewRemoveClothingPrevGeometryVal.Value);

                                        JSONStorableBool jsb = null;
                                        try { jsb = geometry.GetBoolJSONParam(name); } catch { }
                                        if (jsb == null) continue;

                                        // Count submenu options, not current active bools.
                                        if (jsb.val || isPreviewItem) count++;
                                    }
                                }
                            }
                            catch { }

                            try
                            {
                                clothingLabelLastCheckTime = Time.unscaledTime;
                                clothingLabelLastAtomUid = tgt.uid;
                                clothingLabelLastHasOptions = count > 0;
                                clothingLabelLastCount = count;
                            }
                            catch { }
                        }
                        }
                    }
                }
                catch { }

                UpdateRemoveClothingButtonLabels(count);
            }
            else
            {
                UpdateRemoveClothingButtonLabels(0);
            }

            if (isHair)
            {
                int count = 0;
                try
                {
                    Atom tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                    if (tgt != null)
                    {
                        // When submenu is open, keep label count stable by using the cached submenu option count.
                        if (hairSubmenuOpen)
                        {
                            count = hairSubmenuLastOptionCount;
                        }
                        else
                        {
                            DAZCharacterSelector dcs = null;
                            try { dcs = tgt.GetComponentInChildren<DAZCharacterSelector>(); } catch { }
                            if (dcs != null && dcs.hairItems != null)
                            {
                                foreach (var it in dcs.hairItems)
                                {
                                    if (it == null) continue;
                                    bool active = false;
                                    try { active = it.active; } catch { active = false; }
                                    if (active) count++;
                                }
                            }
                        }
                    }
                }
                catch { }
                UpdateRemoveHairButtonLabels(count);
            }
            else
            {
                UpdateRemoveHairButtonLabels(0);
            }

            if (!isHair && hairSubmenuOpen)
            {
                hairSubmenuOpen = false;
                SetHairSubmenuButtonsVisible(false);
            }

            if (!isClothing && clothingSubmenuOpen)
            {
                clothingSubmenuOpen = false;
                SetClothingSubmenuButtonsVisible(false);
            }

            if (!isScene && atomSubmenuOpen)
            {
                atomSubmenuOpen = false;
                SetAtomSubmenuButtonsVisible(false);
            }

            if (!showSave && saveSubmenuOpen)
            {
                saveSubmenuOpen = false;
                CloseSaveSubmenuUI();
            }

            UpdateSideButtonPositions();
        }

        private float GetSideButtonsStackHeight(float spacing, float gap)
        {
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            if (layout == null || layout.Length <= 1) return 0f;

            int visibleCount = 0;
            int gapUnits = 0;
            bool firstVisible = true;
            for (int i = 0; i < layout.Length; i++)
            {
                RectTransform rt = (layout[i].buttonIndex >= 0 && layout[i].buttonIndex < rightSideButtons.Count) ? rightSideButtons[layout[i].buttonIndex] : null;
                if (rt == null && leftSideButtons != null)
                {
                    rt = (layout[i].buttonIndex >= 0 && layout[i].buttonIndex < leftSideButtons.Count) ? leftSideButtons[layout[i].buttonIndex] : null;
                }
                if (rt == null || !rt.gameObject.activeSelf) continue;

                visibleCount++;
                if (!firstVisible) gapUnits += layout[i].gapTier;
                firstVisible = false;
            }

            if (visibleCount <= 1) return 0f;
            return (visibleCount - 1) * spacing + gapUnits * gap;
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


    }

}
