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
{        private void Update()
        {
            if (canvas != null && VPBConfig.Instance != null)
            {
                UpdateTargetMarker();

                try
                {
                    if (Time.unscaledTime - sideContextLastUpdateTime >= SideContextUpdateInterval)
                    {
                        sideContextLastUpdateTime = Time.unscaledTime;
                        UpdateSideContextActions();
                    }
                }
                catch { }

                try
                {
                    if (hairSubmenuOpen)
                    {
                        // Periodic resync to keep submenu reflecting live hair state (e.g. after Undo)
                        try
                        {
                            bool allowResync = !(hairSubmenuParentHovered || hairSubmenuOptionsHovered);
                            if (allowResync && Time.unscaledTime - hairSubmenuLastSyncTime >= HairSubmenuSyncInterval)
                            {
                                hairSubmenuLastSyncTime = Time.unscaledTime;
                                Atom tgt = null;
                                try
                                {
                                    if (!string.IsNullOrEmpty(hairSubmenuTargetAtomUid))
                                        tgt = SuperController.singleton.GetAtomByUid(hairSubmenuTargetAtomUid);
                                }
                                catch { }
                                if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                                // Keep open if there are still options; auto-closes inside if empty.
                                SyncHairSubmenu(tgt, true);
                            }
                        }
                        catch { }

                        bool hoveredManual = false;
                        try
                        {
                            Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : null;

                            RectTransform lrt = leftRemoveAllHairBtn != null ? leftRemoveAllHairBtn.GetComponent<RectTransform>() : null;
                            RectTransform rrt = rightRemoveAllHairBtn != null ? rightRemoveAllHairBtn.GetComponent<RectTransform>() : null;

                            RectTransform lprt = leftRemoveHairSubmenuGapPanelGO != null ? leftRemoveHairSubmenuGapPanelGO.GetComponent<RectTransform>() : null;
                            RectTransform rprt = rightRemoveHairSubmenuGapPanelGO != null ? rightRemoveHairSubmenuGapPanelGO.GetComponent<RectTransform>() : null;

                            if (lrt != null && leftRemoveAllHairBtn.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(lrt, Input.mousePosition, cam)) hoveredManual = true;
                            else if (rrt != null && rightRemoveAllHairBtn.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rrt, Input.mousePosition, cam)) hoveredManual = true;
                            else if (lprt != null && leftRemoveHairSubmenuGapPanelGO.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(lprt, Input.mousePosition, cam)) hoveredManual = true;
                            else if (rprt != null && rightRemoveHairSubmenuGapPanelGO.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rprt, Input.mousePosition, cam)) hoveredManual = true;

                            if (!hoveredManual)
                            {
                                for (int i = 0; i < leftRemoveHairSubmenuButtons.Count; i++)
                                {
                                    GameObject go = leftRemoveHairSubmenuButtons[i];
                                    if (go == null || !go.activeInHierarchy) continue;
                                    RectTransform rt = go.GetComponent<RectTransform>();
                                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam)) { hoveredManual = true; break; }
                                }
                            }
                            if (!hoveredManual)
                            {
                                for (int i = 0; i < rightRemoveHairSubmenuButtons.Count; i++)
                                {
                                    GameObject go = rightRemoveHairSubmenuButtons[i];
                                    if (go == null || !go.activeInHierarchy) continue;
                                    RectTransform rt = go.GetComponent<RectTransform>();
                                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam)) { hoveredManual = true; break; }
                                }
                            }
                        }
                        catch { }

                        bool hovered = hoveredManual || hairSubmenuParentHovered || hairSubmenuOptionsHovered;
                        if (!hovered)
                        {
                            if (Time.unscaledTime - hairSubmenuLastHoverTime >= HairSubmenuAutoHideDelay)
                            {
                                hairSubmenuOpen = false;
                                SetHairSubmenuButtonsVisible(false);
                                UpdateSideButtonPositions();
                            }
                        }
                        else
                        {
                            hairSubmenuLastHoverTime = Time.unscaledTime;
                        }
                    }
                }
                catch { }

                try
                {
                    if (clothingSubmenuOpen)
                    {
                        // Periodic resync to keep submenu reflecting live clothing state
                        try
                        {
                            bool allowResync = !(clothingSubmenuParentHovered || clothingSubmenuOptionsHovered);
                            if (allowResync && Time.unscaledTime - clothingSubmenuLastSyncTime >= ClothingSubmenuSyncInterval)
                            {
                                clothingSubmenuLastSyncTime = Time.unscaledTime;
                                Atom tgt = null;
                                try
                                {
                                    if (!string.IsNullOrEmpty(clothingSubmenuTargetAtomUid))
                                        tgt = SuperController.singleton.GetAtomByUid(clothingSubmenuTargetAtomUid);
                                }
                                catch { }
                                if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                                // Keep open if there are still options; auto-closes inside if empty.
                                SyncClothingSubmenu(tgt, true);
                            }
                        }
                        catch { }

                        bool hoveredManual = false;
                        try
                        {
                            Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : null;

                            RectTransform lrt = leftRemoveAllClothingBtn != null ? leftRemoveAllClothingBtn.GetComponent<RectTransform>() : null;
                            RectTransform rrt = rightRemoveAllClothingBtn != null ? rightRemoveAllClothingBtn.GetComponent<RectTransform>() : null;

                            RectTransform lprt = leftRemoveClothingSubmenuPanelGO != null ? leftRemoveClothingSubmenuPanelGO.GetComponent<RectTransform>() : null;
                            RectTransform rprt = rightRemoveClothingSubmenuPanelGO != null ? rightRemoveClothingSubmenuPanelGO.GetComponent<RectTransform>() : null;

                            if (lrt != null && leftRemoveAllClothingBtn.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(lrt, Input.mousePosition, cam)) hoveredManual = true;
                            else if (rrt != null && rightRemoveAllClothingBtn.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rrt, Input.mousePosition, cam)) hoveredManual = true;
                            else if (lprt != null && leftRemoveClothingSubmenuPanelGO.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(lprt, Input.mousePosition, cam)) hoveredManual = true;
                            else if (rprt != null && rightRemoveClothingSubmenuPanelGO.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rprt, Input.mousePosition, cam)) hoveredManual = true;

                            if (!hoveredManual)
                            {
                                for (int i = 0; i < leftRemoveClothingSubmenuButtons.Count; i++)
                                {
                                    GameObject go = leftRemoveClothingSubmenuButtons[i];
                                    if (go == null || !go.activeInHierarchy) continue;
                                    RectTransform rt = go.GetComponent<RectTransform>();
                                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam)) { hoveredManual = true; break; }
                                }
                            }
                            if (!hoveredManual)
                            {
                                for (int i = 0; i < rightRemoveClothingSubmenuButtons.Count; i++)
                                {
                                    GameObject go = rightRemoveClothingSubmenuButtons[i];
                                    if (go == null || !go.activeInHierarchy) continue;
                                    RectTransform rt = go.GetComponent<RectTransform>();
                                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam)) { hoveredManual = true; break; }
                                }
                            }
                        }
                        catch { }

                        bool hovered = hoveredManual || clothingSubmenuParentHovered || clothingSubmenuOptionsHovered;
                        if (!hovered)
                        {
                            if (Time.unscaledTime - clothingSubmenuLastHoverTime >= ClothingSubmenuAutoHideDelay)
                            {
                                clothingSubmenuOpen = false;
                                SetClothingSubmenuButtonsVisible(false);
                                UpdateSideButtonPositions();
                            }
                        }
                        else
                        {
                            clothingSubmenuLastHoverTime = Time.unscaledTime;
                        }
                    }
                }
                catch { }

                try
                {
                    if (atomSubmenuOpen)
                    {
                        bool hovered = atomSubmenuParentHovered || atomSubmenuOptionsHovered;
                        if (!hovered)
                        {
                            if (Time.unscaledTime - atomSubmenuLastHoverTime >= AtomSubmenuAutoHideDelay)
                            {
                                atomSubmenuOpen = false;
                                SetAtomSubmenuButtonsVisible(false);
                                UpdateSideButtonPositions();
                            }
                        }
                        else
                        {
                            atomSubmenuLastHoverTime = Time.unscaledTime;
                        }
                    }
                }
                catch { }

                if (isLoadingOverlayVisible && loadingBarFillRT != null)
                {
                    loadingBarAnimT += Time.deltaTime;
                    float barWidth = (loadingBarContainerRT != null ? loadingBarContainerRT.rect.width : 420f);
                    float fillWidth = loadingBarFillRT.sizeDelta.x;
                    float travel = Mathf.Max(0f, (barWidth - fillWidth) * 0.5f);
                    float t = Mathf.PingPong(loadingBarAnimT * 1.2f, 1f);
                    float x = Mathf.Lerp(-travel, travel, t);
                    loadingBarFillRT.anchoredPosition = new Vector2(x, 0f);
                }

                if (thumbnailCacheCoroutine == null && pendingThumbnailCacheJobs != null && pendingThumbnailCacheJobs.Count > 0)
                {
                    if (Time.unscaledTime - lastScrollTime > 0.25f)
                    {
                        thumbnailCacheCoroutine = StartCoroutine(ProcessThumbnailCacheQueue());
                    }
                }

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
                    float leftRatio = VPBConfig.Instance.DesktopCustomWidth;
                    
                    float bottomAnchor = 0f;
                    if (VPBConfig.Instance.DesktopFixedHeightMode == 1) bottomAnchor = VPBConfig.Instance.DesktopCustomHeight;

                    // Show/Hide bottom resize handle based on mode
                    Transform customHandle = backgroundBoxGO.transform.Find("ResizeHandle_FixedBottom");
                    if (customHandle != null)
                    {
                        bool shouldShow = isFixedLocally;
                        if (customHandle.gameObject.activeSelf != shouldShow)
                            customHandle.gameObject.SetActive(shouldShow);
                    }

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

            if (!string.IsNullOrEmpty(finalStatus) && hoverPathCanvasGroup != null)
            {
                if (hoverFadeCoroutine != null)
                {
                    StopCoroutine(hoverFadeCoroutine);
                    hoverFadeCoroutine = null;
                }
                hoverPathCanvasGroup.alpha = 1f;
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

            if (hoverPathRT != null)
            {
                bool anyVisible = (statusBarText != null && statusBarText.gameObject.activeSelf) || (hoverPathText != null && hoverPathText.gameObject.activeSelf);
                hoverPathRT.gameObject.SetActive(anyVisible);
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
            if (showSideButtons)
            {
                sideButtonsFadeDelayTimer = 0f;
            }
            else
            {
                sideButtonsFadeDelayTimer += Time.deltaTime;
                if (sideButtonsFadeDelayTimer < SideButtonsFadeDelay)
                {
                    showSideButtons = true;
                }
            }
            
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

        public void ResetFollowOffsets()
        {
            offsetsInitialized = false;
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
