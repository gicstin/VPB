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
        private void UpdateFooterPaddingForFloatingResizeHandles(bool isFloating)
        {
            if (footerHLG == null) return;
            if (footerHLG.padding == null) footerHLG.padding = new RectOffset(0, 0, 0, 0);

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            int defaultRight = Mathf.RoundToInt(10f * s);
            // Floating mode has bottom resize handles on both sides; mirror the existing left reservation
            // so the bottom-right handle does not sit over the right-side footer buttons.
            int desiredRight = isFloating ? footerHLG.padding.left : defaultRight;

            if (_footerHLGLastRightPadding == desiredRight && footerHLG.padding.right == desiredRight) return;

            footerHLG.padding = new RectOffset(footerHLG.padding.left, desiredRight, footerHLG.padding.top, footerHLG.padding.bottom);
            _footerHLGLastRightPadding = desiredRight;
        }

        private void Update()
        {
            if (canvas != null && VPBConfig.Instance != null)
            {
                HandleKeyboardInput();

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

                try { UpdateSelectionContextMenu(); } catch { }

                try { ApplyVamMenuGateVisibility(); } catch { }
                try { ApplyVamMenuAnchoring(); } catch { }

                // Determine whether the gallery is "active" (scrolling or thumbnails still loading).
                // While active we pause all disk saves — background threads must not contend on
                // the cache write-lock while the user is interacting; the cost is we defer
                // persistence, but current-session display is unaffected (images stay in memory).
                bool isScrollingRecently = (Time.unscaledTime - lastScrollTime) < 1.0f;
                bool isThumbnailLoading  = CustomImageLoaderThreaded.singleton != null &&
                                           CustomImageLoaderThreaded.singleton.PendingThumbnailCount > 0;
                bool savingActive = !isScrollingRecently && !isThumbnailLoading;
                if (GalleryThumbnailCache.Instance != null)
                    GalleryThumbnailCache.Instance.SavingPaused = !savingActive;

                // Only start the disk-save coroutine when saving is safe to resume.
                if (savingActive &&
                    thumbnailCacheCoroutine == null &&
                    pendingThumbnailCacheJobs != null &&
                    pendingThumbnailCacheJobs.Count > 0)
                {
                    thumbnailCacheCoroutine = StartCoroutine(ProcessThumbnailCacheQueue());
                }

                // Update thumbnail cache progress panel
                if (_thumbCacheProgressGO != null && _thumbCacheProgressGO.activeSelf)
                {
                    UpdateThumbnailCacheProgressDisplay();
                    bool queueEmpty = pendingThumbnailCacheJobs == null || pendingThumbnailCacheJobs.Count == 0;
                    if (queueEmpty && _thumbCacheSaved > 0 && thumbnailCacheCoroutine == null)
                    {
                        if (_thumbCacheFinishTime < 0f) _thumbCacheFinishTime = Time.unscaledTime;
                        else if (Time.unscaledTime - _thumbCacheFinishTime > 3f) HideThumbnailCacheProgress();
                    }
                }

                if (isFixedLocally)
                {
                    UpdateFooterPaddingForFloatingResizeHandles(false);
                    bool autoCollapse = VPBConfig.Instance.DesktopFixedAutoCollapse;
                    // Show trigger whenever collapsed (to allow expanding), or when in AH mode expanded (for hover detection)
                    if (collapseTriggerGO != null) collapseTriggerGO.SetActive(isCollapsed || autoCollapse);

                    if (isCollapsed)
                    {
                        // Both AO and AH: expand on hover over trigger
                        if (isHoveringTrigger)
                        {
                            SetCollapsed(false);
                        }
                    }
                    else if (autoCollapse)
                    {
                        // AH mode: auto-collapse after user stops hovering
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
                    // AO mode when expanded: stay expanded, no action needed

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

                    // Show/Hide generic resize handles based on mode
                    Transform handleBL = backgroundBoxGO.transform.Find("ResizeHandle_" + AnchorPresets.bottomLeft);
                    if (handleBL != null)
                    {
                        bool shouldShow = false;
                        if (handleBL.gameObject.activeSelf != shouldShow) handleBL.gameObject.SetActive(shouldShow);
                    }
                    Transform handleBR = backgroundBoxGO.transform.Find("ResizeHandle_" + AnchorPresets.bottomRight);
                    if (handleBR != null)
                    {
                        bool shouldShow = !isFixedLocally;
                        if (handleBR.gameObject.activeSelf != shouldShow) handleBR.gameObject.SetActive(shouldShow);
                    }
                    Transform handleTL = backgroundBoxGO.transform.Find("ResizeHandle_" + AnchorPresets.topLeft);
                    if (handleTL != null)
                    {
                        bool shouldShow = !isFixedLocally;
                        if (handleTL.gameObject.activeSelf != shouldShow) handleTL.gameObject.SetActive(shouldShow);
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
                    UpdateFooterPaddingForFloatingResizeHandles(true);
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
                            // Floating mode uses corner resize handles; do not enable the fixed-mode handle.
                            if (child.name.StartsWith("ResizeHandle_") && child.name != "ResizeHandle_FixedBottom")
                                child.gameObject.SetActive(true);
                        }
                        Transform fixedHandle = backgroundBoxGO.transform.Find("ResizeHandle_FixedBottom");
                        if (fixedHandle != null && fixedHandle.gameObject.activeSelf) fixedHandle.gameObject.SetActive(false);

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

            // When a status message is showing, interrupt any in-progress path fade
            if (!string.IsNullOrEmpty(finalStatus) && hoverPathCanvasGroup != null)
            {
                if (hoverFadeCoroutine != null)
                {
                    StopCoroutine(hoverFadeCoroutine);
                    hoverFadeCoroutine = null;
                }
                // Only hide the path text, not the entire container (toolbox reuses hoverPathRT)
                if (hoverPathText != null)
                    hoverPathText.gameObject.SetActive(false);
            }

            if (statusBarText != null)
            {
                statusBarText.text = finalStatus ?? "";
                statusBarText.gameObject.SetActive(!string.IsNullOrEmpty(finalStatus));
            }

            if (hoverPathText != null)
            {
                // Path text and status text are never shown together; status has priority.
                bool showPath = string.IsNullOrEmpty(finalStatus) && !string.IsNullOrEmpty(hoverPathText.text);
                hoverPathText.gameObject.SetActive(showPath);
            }

            // Info bar (hoverPathRT) is always active — no show/hide needed

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
                        
                        bool anchoringActive = (GetAnchoredInstance() == this && VPBConfig.Instance != null && VPBConfig.Instance.GalleryAnchorToVamMenu && IsVamMenuVisible());

                        if (followUser && !anchoringActive)
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

        private void HandleKeyboardInput()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                if (EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null) return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool a = Input.GetKeyDown(KeyCode.A);
            bool del = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);

            if (ctrl && a)
            {
                if (IsHubMode) return;
                TrySelectAllCurrentGalleryView("ctrl+a");
                return;
            }

            if (del)
            {
                if (selectedFiles != null && selectedFiles.Count > 0)
                {
                    try { TboxDeleteSelectedPackages(); } catch { }
                }
                return;
            }

            int move = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow)) move = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow)) move = 1;

            int moveH = 0;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) moveH = -1;
            else if (Input.GetKeyDown(KeyCode.RightArrow)) moveH = 1;

            if (move == 0 && moveH == 0) return;
            
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;

            // Find current index in currentFilteredFiles (visible page)
            int currentIndex = -1;
            
            // Prefer anchor path if available for navigation continuity
            string navPath = !string.IsNullOrEmpty(selectionAnchorPath) ? selectionAnchorPath : selectedPath;
            
            if (!string.IsNullOrEmpty(navPath))
            {
                currentIndex = currentFilteredFiles.FindIndex(f => f.Path == navPath);
            }

            if (currentIndex < 0) 
            {
                currentIndex = 0;
            }

            int newIndex = currentIndex;

            if (layoutMode == GalleryLayoutMode.List)
            {
                newIndex += move; 
                // Ignore horizontal in list mode for now
            }
            else // Grid
            {
                 int cols = gridColumnCount;
                 if (cols < 1) cols = 4; // Fallback

                 if (move != 0) newIndex += move * cols;
                 if (moveH != 0) newIndex += moveH;
            }

            // Clamp
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= currentFilteredFiles.Count) newIndex = currentFilteredFiles.Count - 1;

            if (newIndex != currentIndex || (selectedFiles.Count == 0))
            {
                FileEntry newFile = currentFilteredFiles[newIndex];
                
                if (shift)
                {
                    // Range Select
                    string anchor = selectionAnchorPath;
                    int anchorIndex = -1;
                    if (!string.IsNullOrEmpty(anchor)) anchorIndex = currentFilteredFiles.FindIndex(f => f.Path == anchor);
                    
                    if (anchorIndex < 0) anchorIndex = currentIndex; 

                    int lo = Mathf.Min(anchorIndex, newIndex);
                    int hi = Mathf.Max(anchorIndex, newIndex);

                    if (!ctrl)
                    {
                        selectedFiles.Clear();
                        selectedFilePaths.Clear();
                    }

                    for (int i = lo; i <= hi; i++)
                    {
                        var f = currentFilteredFiles[i];
                        if (selectedFilePaths.Add(f.Path))
                        {
                            selectedFiles.Add(f);
                        }
                    }
                }
                else
                {
                    // Single Select (or Toggle with Ctrl)
                    if (!ctrl)
                    {
                        selectedFiles.Clear();
                        selectedFilePaths.Clear();
                    }
                    
                    if (selectedFilePaths.Add(newFile.Path))
                    {
                        selectedFiles.Add(newFile);
                    }
                    selectionAnchorPath = newFile.Path; // Move anchor
                }

                selectedPath = newFile.Path;
                selectedHubItem = null;
                SetHoverPath(newFile);
                RefreshSelectionVisuals();
                UpdatePaginationText();
            }
        }
    }

}
