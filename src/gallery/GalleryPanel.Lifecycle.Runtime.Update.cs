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
        private bool IsPointerInsideGalleryWindowRect()
        {
            if (backgroundBoxGO == null) return false;
            RectTransform rt = backgroundBoxGO.GetComponent<RectTransform>();
            if (rt == null) return false;

            Camera cam = null;
            try
            {
                // For ScreenSpaceOverlay, camera MUST be null for RectTransformUtility.
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = (canvas.worldCamera != null) ? canvas.worldCamera : Camera.main;
            }
            catch { cam = null; }

            try { return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam); }
            catch { return false; }
        }

        private void UpdateFooterPaddingForFloatingResizeHandles(bool isFloating)
        {
            if (footerHLG == null) return;
            if (footerHLG.padding == null) footerHLG.padding = new RectOffset(0, 0, 0, 0);

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            int defaultRight = Mathf.RoundToInt(10f * s);
            // Floating mode has bottom resize handles on both sides; mirror the existing left reservation
            // so the bottom-right handle does not sit over the right-side footer buttons.
            int desiredRight = defaultRight;
            if (isFloating) desiredRight = footerHLG.padding.left;
            else
            {
                // Fixed + Left dock uses a bottom-right resize handle; reserve footer right side like floating.
                try
                {
                    if (VPBConfig.Instance != null)
                    {
                        string dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);
                        if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                            desiredRight = footerHLG.padding.left;
                    }
                }
                catch { }
            }

            if (_footerHLGLastRightPadding == desiredRight && footerHLG.padding.right == desiredRight) return;

            footerHLG.padding = new RectOffset(footerHLG.padding.left, desiredRight, footerHLG.padding.top, footerHLG.padding.bottom);
            _footerHLGLastRightPadding = desiredRight;
        }

        private void Update()
        {
            if (canvas != null && !canvas.enabled)
            {
                try { ApplyVamMenuGateVisibility(); } catch { }
                return;
            }

            try { GalleryVrThumbstickScroll.TickOncePerFrame(); } catch { }

            try { PluginSettingsHotkeyCaptureUpdate(); } catch { }
            try { FooterCompressCacheHoverTick(); FooterCompressCachePollHoverTooltip(); } catch { }
            try { FooterPluginInfoPollHoverTooltip(); } catch { }

            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.GalUpdateFull++;

            if (canvas != null && VPBConfig.Instance != null)
            {
                HandleKeyboardInput();

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
                    int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
                    int total = (currentFilteredFiles != null) ? currentFilteredFiles.Count : 0;
                    bool countsChanged = sel != _selectionContextLastSelCount || total != _selectionContextLastTotalCount;
                    if (countsChanged || (Time.unscaledTime - selectionContextLastUpdateTime) >= SelectionContextUpdateInterval)
                    {
                        selectionContextLastUpdateTime = Time.unscaledTime;
                        _selectionContextLastSelCount = sel;
                        _selectionContextLastTotalCount = total;
                        UpdateSelectionContextMenu();
                    }
                }
                catch { }

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

                if (isFixedLocally && backgroundBoxGO != null)
                {
                    UpdateFooterPaddingForFloatingResizeHandles(false);
                    bool autoCollapse = VPBConfig.Instance.DesktopFixedAutoCollapse;
                    string dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);

                    // Show trigger whenever collapsed (to allow expanding), or when in AH mode expanded (for hover detection)
                    bool showTrigger = isCollapsed || autoCollapse;
                    if (collapseTriggerGO != null) collapseTriggerGO.SetActive(showTrigger && string.Equals(dock, "Right", StringComparison.OrdinalIgnoreCase));
                    if (collapseTriggerLeftGO != null) collapseTriggerLeftGO.SetActive(showTrigger && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase));
                    if (collapseTriggerTopGO != null) collapseTriggerTopGO.SetActive(showTrigger && string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase));
                    ApplyFixedCollapseTriggerVisuals();

                    if (isCollapsed)
                    {
                        // Both AO and AH: expand on hover over trigger.
                        // Pointer can already be inside trigger area when it becomes active (dock switch/collapse);
                        // use manual rect hit as fallback so Top dock behaves same as Left/Right.
                        bool isHoveringTriggerManual = false;
                        GameObject activeTrigger = null;
                        if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) activeTrigger = collapseTriggerLeftGO;
                        else if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase)) activeTrigger = collapseTriggerTopGO;
                        else activeTrigger = collapseTriggerGO;
                        if (activeTrigger != null)
                        {
                            RectTransform ctRT = activeTrigger.GetComponent<RectTransform>();
                            Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : null; // Overlay mode uses null cam
                            isHoveringTriggerManual = RectTransformUtility.RectangleContainsScreenPoint(ctRT, Input.mousePosition, cam);
                        }
                        if (isHoveringTrigger || isHoveringTriggerManual) SetCollapsed(false);
                    }
                    else if (autoCollapse)
                    {
                        // AH mode: auto-collapse after user stops hovering
                        // Manual hover check for trigger area when it is NOT a raycast target (to avoid blocking scrollbar)
                        bool isHoveringTriggerManual = false;
                        GameObject activeTrigger = null;
                        if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) activeTrigger = collapseTriggerLeftGO;
                        else if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase)) activeTrigger = collapseTriggerTopGO;
                        else activeTrigger = collapseTriggerGO;
                        if (activeTrigger != null)
                        {
                            RectTransform ctRT = activeTrigger.GetComponent<RectTransform>();
                            Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : null; // Overlay mode uses null cam
                            isHoveringTriggerManual = RectTransformUtility.RectangleContainsScreenPoint(ctRT, Input.mousePosition, cam);
                        }

                        bool isPointerInsideGalleryWindow = IsPointerInsideGalleryWindowRect();

                        // If NOT hovering gallery and NOT hovering side buttons and NOT hovering trigger, collapse after delay
                        bool isHoveringAny = hoverCount > 0 || isPointerInsideGalleryWindow || isHoveringTrigger || isHoveringTriggerManual || IsSettingsPanelOpen();
                        if (!isHoveringAny)
                        {
                            collapseTimer += Time.deltaTime;
                            float delay = 1.0f;
                            try
                            {
                                if (VPBConfig.Instance != null)
                                    delay = Mathf.Clamp(VPBConfig.Instance.DesktopFixedAutoHideSeconds, 0.1f, 10f);
                            }
                            catch { delay = 1.0f; }
                            if (collapseTimer >= delay)
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
                    dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);
                    
                    float bottomAnchor = 0f;
                    if (VPBConfig.Instance.DesktopFixedHeightMode == 1)
                    {
                        float raw = VPBConfig.Instance.DesktopCustomHeight;
                        float clamped = Mathf.Clamp(raw, 0.05f, 0.85f);
                        bottomAnchor = clamped;
                        if (Mathf.Abs(raw - clamped) > 0.0001f)
                        {
                            VPBConfig.Instance.DesktopCustomHeight = clamped;
                            try { VPBConfig.Instance.Save(false, true); } catch { }
                        }
                    }

                    // Show/Hide bottom resize handle based on mode
                    Transform customHandle = backgroundBoxGO.transform.Find("ResizeHandle_FixedBottom");
                    if (customHandle != null)
                    {
                        // Fixed-bottom-left handle: only for Right dock (avoid collisions in Left/Top)
                        bool shouldShow = isFixedLocally && (string.Equals(dock, "Right", StringComparison.OrdinalIgnoreCase) || string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase));
                        if (customHandle.gameObject.activeSelf != shouldShow)
                            customHandle.gameObject.SetActive(shouldShow);

                        // Top dock uses this handle for height only (no width change).
                        try
                        {
                            UIAnchorResizer rz = customHandle.GetComponent<UIAnchorResizer>();
                            if (rz != null)
                            {
                                bool top = string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase);
                                rz.resizeX = !top;
                                rz.resizeY = true;
                            }
                        }
                        catch { }
                    }
                    Transform customHandleRight = backgroundBoxGO.transform.Find("ResizeHandle_FixedBottomRight");
                    if (customHandleRight != null)
                    {
                        // Fixed-bottom-right handle: only for Left dock
                        bool shouldShow = isFixedLocally && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase);
                        if (customHandleRight.gameObject.activeSelf != shouldShow)
                            customHandleRight.gameObject.SetActive(shouldShow);
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

                    Vector2 desiredMin, desiredMax;
                    if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                    {
                        desiredMin = new Vector2(0f, bottomAnchor);
                        desiredMax = new Vector2(1f - leftRatio, 1f);
                    }
                    else if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase))
                    {
                        desiredMin = new Vector2(0f, bottomAnchor);
                        desiredMax = new Vector2(1f, 1f);
                    }
                    else
                    {
                        desiredMin = new Vector2(leftRatio, bottomAnchor);
                        desiredMax = new Vector2(1f, 1f);
                    }

                    if (bgRT.anchorMin != desiredMin || bgRT.anchorMax != desiredMax)
                    {
                        bgRT.anchorMin = desiredMin;
                        bgRT.anchorMax = desiredMax;
                        bgRT.offsetMin = Vector2.zero;
                        bgRT.offsetMax = Vector2.zero;

                        UpdateSideButtonsVisibility();
                    }

                    // Ensure collapsed offset matches current dock side (dock can change without anchor changes).
                    if (isCollapsed)
                    {
                        Vector2 off;
                        if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                            off = new Vector2(-bgRT.rect.width, 0f);
                        else if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase))
                            off = new Vector2(0f, bgRT.rect.height);
                        else
                            off = new Vector2(bgRT.rect.width, 0f);
                        if (bgRT.anchoredPosition != off) bgRT.anchoredPosition = off;
                    }
                    else
                    {
                        if (bgRT.anchoredPosition != Vector2.zero) bgRT.anchoredPosition = Vector2.zero;
                    }

                    // Separate triggers handle chamfer direction; nothing to mirror here.
                }
                else if (backgroundBoxGO != null)
                {
                    UpdateFooterPaddingForFloatingResizeHandles(true);
                    if (collapseTriggerGO != null) collapseTriggerGO.SetActive(false);
                    if (collapseTriggerLeftGO != null) collapseTriggerLeftGO.SetActive(false);
                    if (collapseTriggerTopGO != null) collapseTriggerTopGO.SetActive(false);
                    if (isCollapsed) SetCollapsed(false);

                    if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        canvas.renderMode = RenderMode.WorldSpace;
                        canvas.worldCamera = Camera.main;
                        canvas.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                        
                        if (backgroundBoxGO == null) return;
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

            try
            {
                AdvanceSideButtonsFadeDelayTimer();
                ApplyGalleryTransparencyVisuals();
            }
            catch { }

            // Status Bar Logic
            if (temporaryStatusOwner != null && !temporaryStatusOwner.activeInHierarchy)
            {
                // Hover owner went away without delivering an exit event; drop stale tooltip.
                temporaryStatusOwner = null;
                temporaryStatusMsg = null;
            }

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
                string newStatus = finalStatus ?? "";
                if (statusBarText.text != newStatus)
                    statusBarText.text = newStatus;
                bool showStatus = !string.IsNullOrEmpty(finalStatus);
                if (statusBarText.gameObject.activeSelf != showStatus)
                    statusBarText.gameObject.SetActive(showStatus);
            }

            if (hoverPathText != null)
            {
                // Path text and status text are never shown together; status has priority.
                bool showPath = string.IsNullOrEmpty(finalStatus) && !string.IsNullOrEmpty(hoverPathText.text);
                hoverPathText.gameObject.SetActive(showPath);
            }

            // Info bar (hoverPathRT) is always active — no show/hide needed

            // FPS readout, ~2Hz. Value comes from VpbFrameRate; the timer only throttles the text write.
            if (fpsText != null)
            {
                fpsTimer += Time.unscaledDeltaTime;
                if (fpsTimer >= FpsInterval)
                {
                    string txt = string.Format("{0:0} FPS", VpbFrameRate.Current);
                    if (_fpsLastAppliedText != txt)
                    {
                        _fpsLastAppliedText = txt;
                        fpsText.text = txt;
                    }
                    fpsTimer = 0f;
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

                        if (followUser && !anchoringActive && VPBConfig.Instance != null)
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
                                            // No transition: snap rotation immediately.
                                            canvas.transform.rotation = targetFollowRotation;
                                            if (Quaternion.Angle(canvas.transform.rotation, targetFollowRotation) < ReorientStopAngle) isReorienting = false;
                                        }
                                    }
                                }
                            }
                        }

                    }
                }
            }

            if (_categoryQuickChromeRootGO != null && _categoryQuickChromeRootGO.activeSelf)
            {
                try
                {
                    ApplyCategoryQuickChromeLayout(VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f);
                }
                catch { }
            }

            if (IsVisible && titleSearchInput != null)
            {
                float iscale = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
                try { ApplyTitleBarResponsiveLayout(iscale); } catch { }
                try { TickTitleSearchPopupProximityDismiss(iscale); } catch { }
            }

            try { ValidateHoverPreviewActive(); } catch { }

            // Pointer Dot Logic
            if (pointerDotGO != null && canvas != null)
            {
                if (hoverCount > 0 && currentPointerData != null && currentPointerData.pointerCurrentRaycast.isValid)
                {
                    if (!pointerDotGO.activeSelf) pointerDotGO.SetActive(true);
                    // Use standard 5mm offset to prevent z-fighting
                    pointerDotGO.transform.position = currentPointerData.pointerCurrentRaycast.worldPosition - canvas.transform.forward * 0.005f;
                    Transform pdParent = pointerDotGO.transform.parent;
                    if (pdParent != null && pointerDotGO.transform.GetSiblingIndex() != pdParent.childCount - 1)
                    {
                        if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.PointerSib++;
                        pointerDotGO.transform.SetAsLastSibling();
                    }
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
            if (IsPluginHotkeyCaptureActive())
                return;

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                var sel = EventSystem.current.currentSelectedGameObject;
                if (sel.GetComponent<InputField>() != null) return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) && _titleSearchPopupOpen)
            {
                CloseTitleSearchPopup();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) && _categoryQuickMenuOpen)
            {
                SetCategoryQuickMenuVisible(false);
                return;
            }

            if (IsVisible && TryConsumeCategoryQuickNumberKey())
                return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && Input.GetKeyDown(KeyCode.R))
            {
                if (!IsHubMode && activeContentType == ContentType.History)
                {
                    try { RefreshHistoryBrowsePreferLight(true); } catch { }
                    return;
                }
            }
            if (ctrl && Input.GetKeyDown(KeyCode.Z))
            {
                if (!IsHubMode && activeContentType == ContentType.History)
                {
                    if (TryUndoRecentHistoryRemoval())
                        return;
                }
            }
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
                    if (!IsHubMode && activeContentType == ContentType.History)
                    {
                        try { TboxRemoveSelectedFromHistory(); } catch { }
                    }
                    else
                    {
                        try { TboxDeleteSelectedPackages(); } catch { }
                    }
                }
                return;
            }

            bool arrowHeld =
                Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
            bool arrowPressed =
                Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
                Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow);

            if (!arrowHeld)
            {
                keyboardNavigationNextRepeatRealtime = 0f;
                return;
            }

            if (arrowPressed)
            {
                keyboardNavigationNextRepeatRealtime = Time.unscaledTime + KeyboardNavigationInitialRepeatDelay;
            }
            else if (Time.unscaledTime >= keyboardNavigationNextRepeatRealtime)
            {
                keyboardNavigationNextRepeatRealtime = Time.unscaledTime + KeyboardNavigationRepeatInterval;
            }
            else
            {
                return;
            }

            int move = 0;
            if (Input.GetKey(KeyCode.UpArrow) && !Input.GetKey(KeyCode.DownArrow)) move = -1;
            else if (Input.GetKey(KeyCode.DownArrow) && !Input.GetKey(KeyCode.UpArrow)) move = 1;

            int moveH = 0;
            if (Input.GetKey(KeyCode.LeftArrow) && !Input.GetKey(KeyCode.RightArrow)) moveH = -1;
            else if (Input.GetKey(KeyCode.RightArrow) && !Input.GetKey(KeyCode.LeftArrow)) moveH = 1;

            if (move == 0 && moveH == 0) return;
            
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;

            // Find current index in currentFilteredFiles (visible page)
            int currentIndex = -1;
            
            // Prefer anchor path if available for navigation continuity
            bool historyBrowseForNav = !IsHubMode && activeContentType == ContentType.History;
            string navPath = GetCurrentSelectionAnchorIdentityKey(historyBrowseForNav);
            
            if (!string.IsNullOrEmpty(navPath))
            {
                currentIndex = FindIndexBySelectionIdentity(currentFilteredFiles, navPath, historyBrowseForNav);
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
                 int cols = GridColumnCount;
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
                    bool historyBrowse = !IsHubMode && activeContentType == ContentType.History;
                    // Range Select
                    string anchor = GetCurrentSelectionAnchorIdentityKey(historyBrowse);
                    int anchorIndex = -1;
                    if (!string.IsNullOrEmpty(anchor)) anchorIndex = FindIndexBySelectionIdentity(currentFilteredFiles, anchor, historyBrowse);
                    
                    if (anchorIndex < 0) anchorIndex = currentIndex; 

                    int lo = Mathf.Min(anchorIndex, newIndex);
                    int hi = Mathf.Max(anchorIndex, newIndex);

                    if (!ctrl)
                    {
                        selectedFiles.Clear();
                        selectedFilePaths.Clear();
                    }
                    var historySelectionKeys = historyBrowse ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
                    if (historyBrowse && ctrl)
                    {
                        for (int i = 0; i < selectedFiles.Count; i++)
                        {
                            string existingKey = GetSelectionIdentityKey(selectedFiles[i], true);
                            if (!string.IsNullOrEmpty(existingKey)) historySelectionKeys.Add(existingKey);
                        }
                    }

                    for (int i = lo; i <= hi; i++)
                    {
                        var f = currentFilteredFiles[i];
                        AddFileToSelection(f, historyBrowse, historySelectionKeys);
                    }
                }
                else
                {
                    // Single Select (or Toggle with Ctrl)
                    bool historyBrowse = !IsHubMode && activeContentType == ContentType.History;
                    if (!ctrl)
                    {
                        selectedFiles.Clear();
                        selectedFilePaths.Clear();
                    }
                    
                    AddFileToSelection(newFile, historyBrowse);
                    SetSelectionAnchor(newFile, historyBrowse); // Move anchor
                }

                selectedPath = historyBrowseForNav ? GetSelectionIdentityKey(newFile, true) : newFile.Path;
                selectedHubItem = null;
                SetHoverPath(newFile);
                if (recyclingGrid != null) recyclingGrid.EnsureItemVisible(newIndex);
                RefreshSelectionVisuals();
                UpdatePaginationText();
            }
        }
    }

}
