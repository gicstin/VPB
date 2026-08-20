using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        /// <summary>
        /// UI pointer screen position for hit tests. Prefers EventSystem laser/mouse sample
        /// (<see cref="currentPointerData"/>). Desktop may fall back to <see cref="Input.mousePosition"/>;
        /// VR without a sample returns false — mouse is not the laser.
        /// </summary>
        internal bool TryGetUiPointerScreenPosition(out Vector2 screenPos)
        {
            try
            {
                if (currentPointerData != null)
                {
                    screenPos = currentPointerData.position;
                    return true;
                }
            }
            catch { }

            screenPos = Input.mousePosition;
            return !XrUtils.IsVrActive();
        }

        private bool IsPointerInsideGalleryWindowRect()
        {
            if (backgroundBoxGO == null) return false;
            RectTransform rt = _backgroundBoxRT;
            if (rt == null)
            {
                rt = backgroundBoxGO.GetComponent<RectTransform>();
                _backgroundBoxRT = rt;
            }
            if (rt == null) return false;

            Vector2 screenPos;
            if (!TryGetUiPointerScreenPosition(out screenPos))
                return false;

            Camera cam = null;
            try
            {
                // For ScreenSpaceOverlay, camera MUST be null for RectTransformUtility.
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = (canvas.worldCamera != null) ? canvas.worldCamera : Camera.main;
            }
            catch { cam = null; }

            bool inside;
            try { inside = RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam); }
            catch { return false; }
            if (!inside) return false;

            // Modeless floats live on canvas (siblings of backgroundBoxGO). Their panels often
            // sit over the pane screen-rect — raw contains would pin AH/opacity forever.
            // If the live raycast hit is outside the pane subtree, pointer is on float chrome.
            GameObject hitGo;
            if (TryGetCurrentPointerRaycastGameObject(out hitGo) && hitGo != null
                && !IsTransformUnderGalleryPaneSubtree(hitGo.transform))
                return false;

            return true;
        }

        /// <summary>EventSystem raycast under pointer when sample is valid. Warm path — no alloc.</summary>
        private bool TryGetCurrentPointerRaycastGameObject(out GameObject hitGo)
        {
            hitGo = null;
            try
            {
                if (currentPointerData == null) return false;
                if (!currentPointerData.pointerCurrentRaycast.isValid) return false;
                hitGo = currentPointerData.pointerCurrentRaycast.gameObject;
                return hitGo != null;
            }
            catch
            {
                hitGo = null;
                return false;
            }
        }

        /// <summary>True when transform is backgroundBoxGO or a descendant (not canvas-sibling floats).</summary>
        private bool IsTransformUnderGalleryPaneSubtree(Transform t)
        {
            if (t == null || backgroundBoxGO == null) return false;
            try
            {
                Transform pane = backgroundBoxGO.transform;
                return t == pane || t.IsChildOf(pane);
            }
            catch { return false; }
        }

        private RectTransform GetCachedCollapseTriggerRT(GameObject triggerGO, ref RectTransform cache)
        {
            if (triggerGO == null) { cache = null; return null; }
            if (cache == null || cache.gameObject != triggerGO)
                cache = triggerGO.GetComponent<RectTransform>();
            return cache;
        }

        private UIAnchorResizer GetCachedUIAnchorResizer(GameObject go, ref UIAnchorResizer cache)
        {
            if (go == null) { cache = null; return null; }
            if (cache == null || cache.gameObject != go)
                cache = go.GetComponent<UIAnchorResizer>();
            return cache;
        }

        private void MarkGalleryPaneChromeDirty()
        {
            try { GalleryPaneChromeEnforcer.MarkDirtyOn(backgroundBoxGO); } catch { }
        }

        private void Update()
        {
            if (canvas != null && !canvas.enabled)
            {
                try { RemoveModeHidePopup(); RemoveModeClearHelp(); } catch { }
                try { ApplyVamMenuGateVisibility(); } catch { }
                return;
            }

            if (_floatsOnly)
            {
                FloatsOnlyUpdateTick();
                return;
            }

            try { GalleryVrThumbstickScroll.TickOncePerFrame(); } catch { }
            try { DetailStripScrubTick(); } catch { }

            try { PluginSettingsHotkeyCaptureUpdate(); } catch { }
            try { FooterCompressCacheHoverTick(); FooterCompressCachePollHoverTooltip(); } catch { }
            try { FooterPluginInfoPollHoverTooltip(); } catch { }
            try { BenchModalRuntimeTick(); } catch { }
            try { RemoveModeUpdate(); } catch { }

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
                try { CustomImageLoaderThreaded.NotifyGalleryScrollUnscaledTime(lastScrollTime); } catch { }
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

                if (isFixedLocally && backgroundBoxGO != null)
                {
                    // Self-correct the content subtree (e.g. first load completing while collapsed),
                    // so a collapsed-and-loaded pane stops rendering/raycasting off-screen content.
                    bool wantSubtree = ShouldContentSubtreeBeActive();
                    if (backgroundBoxGO.activeSelf != wantSubtree)
                        backgroundBoxGO.SetActive(wantSubtree);

                    bool autoCollapse = DockAutoHide;
                    GalleryDockSide dockSide = EffectiveDockSide;
                    string dock = GalleryDockLayout.ToConfigString(dockSide);

                    // Show trigger whenever collapsed (to allow expanding), or when in AH mode expanded (for hover detection)
                    bool showTrigger = isCollapsed || autoCollapse;
                    if (collapseTriggerGO != null) collapseTriggerGO.SetActive(showTrigger && dockSide == GalleryDockSide.Right);
                    if (collapseTriggerLeftGO != null) collapseTriggerLeftGO.SetActive(showTrigger && dockSide == GalleryDockSide.Left);
                    if (collapseTriggerTopGO != null) collapseTriggerTopGO.SetActive(showTrigger && dockSide == GalleryDockSide.Top);
                    SyncCollapseTriggerBands();
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
                            RectTransform ctRT = null;
                            if (activeTrigger == collapseTriggerLeftGO) ctRT = GetCachedCollapseTriggerRT(activeTrigger, ref _collapseTriggerLeftRT);
                            else if (activeTrigger == collapseTriggerTopGO) ctRT = GetCachedCollapseTriggerRT(activeTrigger, ref _collapseTriggerTopRT);
                            else ctRT = GetCachedCollapseTriggerRT(activeTrigger, ref _collapseTriggerRT);
                            // Overlay MUST pass null — leftover worldCamera triggers ScreenPointToRay spam.
                            Camera cam = null;
                            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                                cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
                            if (ctRT != null)
                                isHoveringTriggerManual = RectTransformUtility.RectangleContainsScreenPoint(ctRT, Input.mousePosition, cam);
                        }
                        if (isHoveringTrigger || isHoveringTriggerManual) SetCollapsed(false);
                    }
                    else if (autoCollapse)
                    {
                        // AH mode: collapse after idle (no pointer engagement AND no text/chrome engagement)
                        // Manual hover check for trigger area when it is NOT a raycast target (to avoid blocking scrollbar)
                        bool isHoveringTriggerManual = false;
                        GameObject activeTrigger = null;
                        if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) activeTrigger = collapseTriggerLeftGO;
                        else if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase)) activeTrigger = collapseTriggerTopGO;
                        else activeTrigger = collapseTriggerGO;
                        if (activeTrigger != null)
                        {
                            RectTransform ctRT = null;
                            if (activeTrigger == collapseTriggerLeftGO) ctRT = GetCachedCollapseTriggerRT(activeTrigger, ref _collapseTriggerLeftRT);
                            else if (activeTrigger == collapseTriggerTopGO) ctRT = GetCachedCollapseTriggerRT(activeTrigger, ref _collapseTriggerTopRT);
                            else ctRT = GetCachedCollapseTriggerRT(activeTrigger, ref _collapseTriggerRT);
                            Camera cam = null;
                            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                                cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
                            if (ctRT != null)
                                isHoveringTriggerManual = RectTransformUtility.RectangleContainsScreenPoint(ctRT, Input.mousePosition, cam);
                        }

                        // Engagement = pointer on pane OR text focus / modal chrome (Ctrl+F, fields…).
                        // Modeless floats do not pin expanded. Hover-only gate collapsed mid-typing.
                        bool isEngaged = IsGalleryInteractionEngaged() || isHoveringTrigger || isHoveringTriggerManual;
                        if (!isEngaged && !GalleryDockLayout.InExpandGrace())
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
                        DetachWorldSpaceCanvasFromPlayerUi();
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                        canvas.worldCamera = null;
                        canvas.transform.localScale = Vector3.one;
                        
                        if (dragger != null) dragger.enabled = false;
                        if (_resizeHandleBottomLeftGO != null) _resizeHandleBottomLeftGO.SetActive(false);
                        if (_resizeHandleBottomRightGO != null) _resizeHandleBottomRightGO.SetActive(false);
                        if (_resizeHandleTopLeftGO != null) _resizeHandleTopLeftGO.SetActive(false);
                        if (_resizeHandleFixedBottomGO != null) _resizeHandleFixedBottomGO.SetActive(false);
                        if (_resizeHandleFixedBottomRightGO != null) _resizeHandleFixedBottomRightGO.SetActive(false);
                    }

                    // Always update anchors in Fixed mode to support height toggles and screen resizing
                    RectTransform bgRT = _backgroundBoxRT;
                    if (bgRT == null)
                    {
                        bgRT = backgroundBoxGO.GetComponent<RectTransform>();
                        _backgroundBoxRT = bgRT;
                    }
                    dockSide = EffectiveDockSide;
                    dock = GalleryDockLayout.ToConfigString(dockSide);
                    ApplyDockSortingOrder();

                    if (DockHeightMode == 1)
                    {
                        float raw = DockCustomHeight;
                        float clamped = Mathf.Clamp(raw, GalleryDockLayout.MinCrossAnchor, GalleryDockLayout.MaxCrossAnchor);
                        if (Mathf.Abs(raw - clamped) > 0.0001f)
                        {
                            DockCustomHeight = clamped;
                            try { VPBConfig.Instance.Save(false, true); } catch { }
                        }
                    }

                    // Show/Hide bottom resize handles based on dock mode (handles seated in footer slots).
                    // Right dock → bottom-left grip; Left dock → bottom-right grip; Top dock → both grips (height only),
                    // both using the straight-down chevron.
                    bool isTopDock = string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase);
                    bool showFixedBottomLeft = isFixedLocally && (string.Equals(dock, "Right", StringComparison.OrdinalIgnoreCase) || isTopDock);
                    bool showFixedBottomRight = isFixedLocally && (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase) || isTopDock);

                    // Cross-slot limits belong on the resizer, not just on the resolved rect: otherwise
                    // the drag preview runs past a bound the layout then silently snaps back from.
                    float dragMinY = isTopDock
                        ? GalleryDockLayout.TopBottomAnchorFloor()
                        : GalleryDockLayout.MinCrossAnchor;
                    float dragMaxY = isTopDock
                        ? GalleryDockLayout.MaxCrossAnchor
                        : GalleryDockLayout.SideBottomAnchorCeiling();

                    if (_resizeHandleFixedBottomGO != null)
                    {
                        if (_resizeHandleFixedBottomGO.activeSelf != showFixedBottomLeft)
                            _resizeHandleFixedBottomGO.SetActive(showFixedBottomLeft);

                        UIAnchorResizer rz = GetCachedUIAnchorResizer(_resizeHandleFixedBottomGO, ref _fixedBottomResizer);
                        if (rz != null)
                        {
                            rz.resizeX = !isTopDock;
                            rz.resizeY = true;
                            rz.minAnchorY = dragMinY;
                            rz.maxAnchorY = dragMaxY;
                        }
                    }
                    if (_resizeHandleFixedBottomRightGO != null)
                    {
                        if (_resizeHandleFixedBottomRightGO.activeSelf != showFixedBottomRight)
                            _resizeHandleFixedBottomRightGO.SetActive(showFixedBottomRight);

                        UIAnchorResizer rz = GetCachedUIAnchorResizer(_resizeHandleFixedBottomRightGO, ref _fixedBottomRightResizer);
                        if (rz != null)
                        {
                            rz.resizeX = !isTopDock;
                            rz.resizeY = true;
                            rz.minAnchorY = dragMinY;
                            rz.maxAnchorY = dragMaxY;
                        }
                    }

                    // Icons only when dock / visibility changes — ApplyBarIconFromPath every frame was hot-path waste.
                    int iconKey = ((int)dockSide << 3)
                        | ((showFixedBottomLeft ? 1 : 0) << 2)
                        | ((showFixedBottomRight ? 1 : 0) << 1)
                        | (isTopDock ? 1 : 0);
                    if (iconKey != _fixedDockHandleIconKey)
                    {
                        _fixedDockHandleIconKey = iconKey;
                        if (showFixedBottomLeft && _resizeHandleFixedBottomGO != null)
                            try { UI.ApplyBarIconFromPath(_resizeHandleFixedBottomGO, isTopDock ? "chevrons-down" : "chevrons-down-left"); } catch { }
                        if (showFixedBottomRight && _resizeHandleFixedBottomRightGO != null)
                            try { UI.ApplyBarIconFromPath(_resizeHandleFixedBottomRightGO, isTopDock ? "chevrons-down" : "chevrons-down-right"); } catch { }
                    }

                    // Floating corner handles are hidden in fixed mode.
                    if (_resizeHandleBottomLeftGO != null && _resizeHandleBottomLeftGO.activeSelf) _resizeHandleBottomLeftGO.SetActive(false);
                    if (_resizeHandleBottomRightGO != null && _resizeHandleBottomRightGO.activeSelf) _resizeHandleBottomRightGO.SetActive(false);
                    if (_resizeHandleTopLeftGO != null && _resizeHandleTopLeftGO.activeSelf) _resizeHandleTopLeftGO.SetActive(false);

                    Vector2 desiredMin, desiredMax;
                    if (GalleryDockLayout.TryGetRect(dockSide, out desiredMin, out desiredMax)
                        && (bgRT.anchorMin != desiredMin || bgRT.anchorMax != desiredMax))
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
                        if (dockSide == GalleryDockSide.Left)
                            off = new Vector2(-bgRT.rect.width, 0f);
                        else if (dockSide == GalleryDockSide.Top)
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
                    if (collapseTriggerGO != null) collapseTriggerGO.SetActive(false);
                    if (collapseTriggerLeftGO != null) collapseTriggerLeftGO.SetActive(false);
                    if (collapseTriggerTopGO != null) collapseTriggerTopGO.SetActive(false);
                    if (isCollapsed) SetCollapsed(false);

                    if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        canvas.renderMode = RenderMode.WorldSpace;
                        canvas.worldCamera = Camera.main;
                        ResetWorldSpaceCanvasScaleSync();
                        ApplyWorldSpaceCanvasScale();
                        
                        if (backgroundBoxGO == null) return;
                        RectTransform bgRT = _backgroundBoxRT;
                        if (bgRT == null)
                        {
                            bgRT = backgroundBoxGO.GetComponent<RectTransform>();
                            _backgroundBoxRT = bgRT;
                        }
                        bgRT.anchorMin = new Vector2(0.5f, 0.5f);
                        bgRT.anchorMax = new Vector2(0.5f, 0.5f);
                        bgRT.sizeDelta = new Vector2(1200, 800);
                        bgRT.anchoredPosition = Vector2.zero;
                        
                        UpdateSideButtonsVisibility();

                        if (dragger != null) dragger.enabled = true;
                        // Floating mode uses the corner handles; the fixed-dock handles stay hidden.
                        if (_resizeHandleBottomLeftGO != null) _resizeHandleBottomLeftGO.SetActive(true);
                        if (_resizeHandleBottomRightGO != null) _resizeHandleBottomRightGO.SetActive(true);
                        if (_resizeHandleTopLeftGO != null) _resizeHandleTopLeftGO.SetActive(true);
                        if (_resizeHandleFixedBottomGO != null && _resizeHandleFixedBottomGO.activeSelf) _resizeHandleFixedBottomGO.SetActive(false);
                        if (_resizeHandleFixedBottomRightGO != null && _resizeHandleFixedBottomRightGO.activeSelf) _resizeHandleFixedBottomRightGO.SetActive(false);
                        _fixedDockHandleIconKey = int.MinValue;

                        RepositionInFront();
                    }
                }
            }

            // WorldSpace: keep pane size independent of VaM worldScale (native mainHUD behavior).
            if (!isFixedLocally)
                SyncWorldSpaceCanvasScaleIfWorldScaleChanged();

            // Desktop: re-chrome when VaM Monitor UI Scale changes (HostScale).
            SyncHostUiScaleIfChanged();

            // Coalesced VPB.cfg write after Ctrl+Alt+/- scale nudges (idle = one bool).
            try { GalleryUiScaleHotkey.TickDeferredSave(); } catch { }

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
                CancelStickyHoverTooltip();
                temporaryStatusOwner = null;
                temporaryStatusMsg = null;
            }

            AdvanceStickyHoverTooltip();

            // Mode sticky: toast never blanks ambient modes (concat when both). Drag still wins.
            string finalStatus = ResolveStatusBarText(dragStatusMsg, temporaryStatusMsg, ModeAmbientMsg);

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
                // Stronger mode cue: tint when sticky modes present (not toast-only).
                ApplyStatusBarModeTint(ModeAmbientMsg != null);
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
                    ApplyCategoryQuickChromeLayout(ChromeScale);
                }
                catch { }
            }

            if (IsVisible && titleSearchInput != null)
            {
                float iscale = ChromeScale;
                try { ApplyTitleBarResponsiveLayout(iscale); } catch { }
                try { ApplyFooterOverflowLayout(iscale); } catch { }
                try { TickTitleSearchPopupOutsideClickDismiss(); } catch { }
                try { TickTitleSearchPopupOpenCue(); } catch { }
            }
            else if (IsVisible && paginationRT != null)
            {
                try { ApplyFooterOverflowLayout(ChromeScale); } catch { }
            }

            try { TickLayoutRevertBar(); } catch { }
            try { TickLayoutModeWatch(); } catch { }

            try { ValidateHoverPreviewActive(); } catch { }
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

        /// <summary>
        /// Ctrl+Alt+= / Ctrl+Alt+KeypadPlus → scale up; Ctrl+Alt+- / Ctrl+Alt+KeypadMinus → scale down.
        /// Avoids Ctrl+/- / Ctrl+scroll (grid column / list thumb zoom). Step 0.1; persists desktop/VR value.
        /// </summary>
        private bool TryHandleGalleryUiScaleHotkey()
        {
            int dir = 0;
            if (VpbShortcutMap.Down(VpbShortcut.UiScaleUp) || VpbShortcutMap.Down(VpbShortcut.UiScaleUpAlt)) dir = 1;
            else if (VpbShortcutMap.Down(VpbShortcut.UiScaleDown) || VpbShortcutMap.Down(VpbShortcut.UiScaleDownAlt)) dir = -1;
            if (dir == 0) return false;

            if (!GalleryUiScaleHotkey.TryNudge(dir))
                return false;

            try
            {
                float scale = VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f;
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.status.ui_scale", "UI scale: {0:0.0}"),
                    scale), 1.25f);
            }
            catch { }

            return true;
        }

        private void HandleKeyboardInput()
        {
            if (IsPluginHotkeyCaptureActive())
                return;

            // Focus title search (expanded Settings float → settings filter). Works with another InputField selected.
            if (VpbShortcutMap.Down(VpbShortcut.FocusSearch))
            {
                try { FocusTitleSearchFromHotkey(); } catch { }
                return;
            }

            // Command palette (works even with search focused).
            if (VpbShortcutMap.Down(VpbShortcut.CommandPalette))
            {
                try { ToggleCommandPalette(); } catch { }
                return;
            }
            // Floating filter presets. Before InputField gate.
            if (VpbShortcutMap.Down(VpbShortcut.FilterPresets))
            {
                try { ToggleFloatingQuickFilters(); } catch { }
                return;
            }
            // Floating Scene Import (detach if needed; hide keeps float).
            if (VpbShortcutMap.Down(VpbShortcut.ImportSidebar))
            {
                try { ToggleFloatingImportSidebar(); } catch { }
                return;
            }
            // Layout presets manager.
            if (VpbShortcutMap.Down(VpbShortcut.LayoutPresets))
            {
                try { ToggleLayoutPresetsFloat(); } catch { }
                return;
            }
            if (VpbShortcutMap.Down(VpbShortcut.Undo))
            {
                // Search clear lives on main Undo stack — allow Undo while title-search field focused.
                try
                {
                    if (IsSearchClearUndoTop())
                    {
                        Undo();
                        return;
                    }
                }
                catch { }
            }

            if (DetailStripIsTagMenuOpen()
                && (Input.GetKeyDown(KeyCode.UpArrow)
                    || Input.GetKeyDown(KeyCode.DownArrow)
                    || Input.GetKeyDown(KeyCode.Space)))
            {
                KeyCode k = Input.GetKeyDown(KeyCode.UpArrow) ? KeyCode.UpArrow
                    : (Input.GetKeyDown(KeyCode.DownArrow) ? KeyCode.DownArrow : KeyCode.Space);
                if (DetailStripTagMenuHandleListKey(k))
                    return;
            }

            // Confirm modal: Esc=Cancel (or dismiss-only for Try-On sticky gate), Enter=Confirm.
            if (TryHandleConfirmOverlayKeys())
                return;

            if (TryHandleGridContextMenuKeys())
                return;

            if (TryHandleFilterChipOverflowEsc())
                return;

            // Strip keep: Esc ladder + / focus before InputField gate.
            if (StripKeepHandleSubScenePickKeys())
                return;
            if (Input.GetKeyDown(KeyCode.Escape) && _stripKeepShortcutHelpVisible)
            {
                HideStripKeepShortcutHelp();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) && _stripKeepAwaitingSoftConfirm)
            {
                _stripKeepAwaitingSoftConfirm = false;
                try { RefreshStripKeepSummaryAndConfirm(); } catch { }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) && IsStripKeepRecipeSaveInlineOpen())
            {
                HideStripKeepRecipeSaveInline();
                return;
            }
            if (IsStripKeepRecipeSaveInlineOpen()
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                CommitStripKeepRecipeSaveInline();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) && _stripKeepRenameOverlayRoot != null)
            {
                HideStripKeepRenameOverlay();
                return;
            }
            if (IsStripKeepSelectorOpen() && StripKeepHandleKeyboard())
                return;
            // Dismiss strip float + Scene Tools (Cancel/X same). Soft-confirm/overlays handled above.
            if (Input.GetKeyDown(KeyCode.Escape) && IsStripKeepSelectorOpen())
            {
                ExitCreatorMode();
                return;
            }

            // Command palette: Esc / arrows / Enter (search field must not trap Esc forever).
            if (TryHandleCommandPaletteKeyboard())
                return;

            // In-app help: Esc / F1 before InputField gate (search field must not swallow Esc).
            if (TryHandleInAppHelpKeyboard(allowQuestionKey: false))
                return;

            // Remap Atom UIDs float: Esc ladder (filter → collapse → cancel) before InputField gate.
            if (TryHandleRemapAtomUidsEsc())
                return;

            // Settings float: Esc → Cancel+close before InputField gate.
            if (TryHandleSettingsFloatEsc())
                return;

            // Plugins float: apply orphan .cs after async cslist-ref warm (never block open).
            try { TickPluginsFloatRefsRefresh(); } catch { }

            // Plugins float: Esc clear filter → close.
            if (TryHandlePluginsFloatEsc())
                return;

            // Layout presets float: Esc menu → rename/delete → close (before InputField gate).
            if (TryHandleLayoutPresetsFloatKeyboard())
                return;

            // Scene Import float: Esc expand / hide-keep-detach.
            if (TryHandleImportSidebarFloatEsc())
                return;

            // Filter presets: Esc modes/hide, arrows/Enter/D/Ctrl+S/U (before InputField gate so rename Esc works).
            if (quickFiltersUI != null && quickFiltersUI.IsVisible && quickFiltersUI.TryHandleKeyboard())
                return;

            // Sticky tools + apply/hold Esc BEFORE InputField gate.
            // Banner advertises Esc → mode; search focus must not trap exit (Norman false signifier).
            if (Input.GetKeyDown(KeyCode.Escape)
                && (ModeAmbientEscExitsAny() || ApplySemanticsEscExitsAny()))
            {
                try
                {
                    if (EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(null);
                }
                catch { }
            }

            if (Input.GetKeyDown(KeyCode.Escape) && creatorModeActive && !creatorModeStripBusy)
            {
                ExitCreatorMode();
                return;
            }

            if (TryHandleRemoveModeEsc())
                return;

            if (TryHandleTryOnEsc())
                return;

            if (TryHandleCleanupModeEsc())
                return;

            // Docked Import Esc (float handled earlier via TryHandleImportSidebarFloatEsc).
            if (TryHandleImportSidebarDockedEsc())
                return;

            if (TryHandleBenchPickModeEsc())
                return;

            if (TryHandleApplySemanticsEsc())
                return;

            // Search ↔ grid focus transfer before InputField gate (no Unity Tab cycle — Mode.None).
            if (TryHandleKeyboardFocusTransfer())
                return;

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                var sel = EventSystem.current.currentSelectedGameObject;
                if (sel.GetComponent<InputField>() != null) return;
            }

            // Remap Atom UIDs: Enter after InputField gate (collapse picker / Import).
            if (TryHandleRemapAtomUidsEnter())
                return;

            // ? / Shift+/ — Hotkeys sheet (recognition). After InputField gate.
            if (TryHandleInAppHelpKeyboard(allowQuestionKey: true))
                return;

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

            if (Input.GetKeyDown(KeyCode.Escape)
                && _detailStripTagMenuRoot != null
                && _detailStripTagMenuRoot.activeSelf)
            {
                // Nested modal → clear filter → close (same ladder as search Esc).
                DetailStripTagMenuOnSearchEscape();
                return;
            }

            // Bare Esc — clear selection when no menu/mode claimed it.
            if (TryHandleClearSelectionEsc())
                return;

            if (IsVisible && VPBConfig.Instance.CategoryNumberKeysEnabled && TryConsumeCategoryQuickNumberKey())
                return;

            // UI chrome scale (not Ctrl+scroll grid zoom).
            if (TryHandleGalleryUiScaleHotkey())
                return;

            if (VpbShortcutMap.Down(VpbShortcut.HistoryRefresh))
            {
                if (activeContentType == ContentType.History)
                {
                    try { RefreshHistoryBrowsePreferLight(true); } catch { }
                    return;
                }
            }
            if (VpbShortcutMap.Down(VpbShortcut.Redo) || VpbShortcutMap.Down(VpbShortcut.RedoAlt))
            {
                try { Redo(); } catch { }
                return;
            }
            if (VpbShortcutMap.Down(VpbShortcut.Undo))
            {
                try { Undo(); } catch { }
                return;
            }

            // Toggle Scene Tools (side-rail parity).
            if (VpbShortcutMap.Down(VpbShortcut.SceneTools))
            {
                try { ToggleCreatorMode(); } catch { }
                return;
            }

            // Toggle Scene Eraser.
            if (VpbShortcutMap.Down(VpbShortcut.SceneEraser))
            {
                try { ToggleRemoveMode(false, false); } catch { }
                return;
            }

            // Direct open/close Strip Scene window.
            if (VpbShortcutMap.Down(VpbShortcut.StripScene))
            {
                try { HotkeyOpenStripSceneDirect(); } catch { }
                return;
            }

            if (VpbShortcutMap.Down(VpbShortcut.SelectAll))
            {
                TrySelectAllCurrentGalleryView("select-all");
                return;
            }

            if (VpbShortcutMap.Down(VpbShortcut.DeleteSelection)
                || VpbShortcutMap.Down(VpbShortcut.DeleteSelectionAlt))
            {
                if (selectedFiles != null && selectedFiles.Count > 0)
                {
                    if (activeContentType == ContentType.History)
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

            // Apply selection (keyboard expert path). Before arrow early-out.
            if (VpbShortcutMap.Down(VpbShortcut.Apply) || VpbShortcutMap.Down(VpbShortcut.ApplyAlt))
            {
                try { TryKeyboardApplySelection(); } catch { }
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

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

            // Click/keyboard selection may resize detail strip again after thumb-scrub lock.
            try { DetailStripUnlockAfterExternalSelectionChange(); } catch { }

            // Find current index in currentFilteredFiles (visible page)
            int currentIndex = -1;
            
            // Prefer anchor path if available for navigation continuity
            bool historyBrowseForNav = activeContentType == ContentType.History;
            string navPath = GetCurrentSelectionAnchorIdentityKey(historyBrowseForNav);
            
            if (!string.IsNullOrEmpty(navPath))
            {
                currentIndex = FindIndexBySelectionIdentity(currentFilteredFiles, navPath, historyBrowseForNav);
            }

            if (currentIndex < 0) 
            {
                currentIndex = 0;
            }

            // Up from first item → title search (keyboard loop; skip when range/add modifiers).
            if (TryKeyboardUpToTitleSearch(currentIndex, move, moveH, shift, ctrl))
                return;

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
                    bool historyBrowse = activeContentType == ContentType.History;
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
                    bool historyBrowse = activeContentType == ContentType.History;
                    if (!ctrl)
                    {
                        selectedFiles.Clear();
                        selectedFilePaths.Clear();
                    }
                    
                    AddFileToSelection(newFile, historyBrowse);
                    SetSelectionAnchor(newFile, historyBrowse); // Move anchor
                }

                selectedPath = historyBrowseForNav ? GetSelectionIdentityKey(newFile, true) : newFile.Path;
                SetHoverPath(newFile);
                if (settingsListViewActive)
                {
                    try { DetailStripHide(); } catch { }
                    RefreshSelectionVisualsCore(runHeavySideEffects: false);
                }
                else
                    RefreshSelectionVisuals();
                // After detail/tbox resize shrinks viewport — ensure full cell (thumb+label) visible.
                try { EnsureGridSelectionFullyVisible(newIndex); } catch { }
                UpdatePaginationText();
            }
        }

        /// <summary>
        /// Keyboard/selection scroll after chrome layout. Must run after detail strip/tbox
        /// change bottom inset — otherwise caption under square thumb stays clipped.
        /// </summary>
        private void EnsureGridSelectionFullyVisible(int index)
        {
            if (index < 0) return;
            if (layoutMode != GalleryLayoutMode.Grid || settingsListViewActive) return;
            if (recyclingGrid == null) return;
            try { SyncGalleryMainAreaBottomEdgeFromCurrentLayout(); } catch { }
            recyclingGrid.EnsureItemVisible(index);
        }

        private void SyncGalleryMainAreaBottomEdgeFromCurrentLayout()
        {
            if (contentScrollRT == null) return;
            float paneScale = ChromeScale;
            if (paneScale <= 0f) paneScale = 1f;
            float leftOffset = contentScrollRT.offsetMin.x;
            float rightOffset = contentScrollRT.offsetMax.x;
            float topOffset = contentScrollRT.offsetMax.y;
            float tabTop = TabScrollTopOffset();
            SyncGalleryMainAreaBottomEdge(leftOffset, rightOffset, topOffset, tabTop);
        }
    }

}
