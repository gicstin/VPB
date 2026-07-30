using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void EnsureCanvasRegisteredWithSuperController()
        {
            if (_registeredWithSuperController) return;
            if (canvas == null) return;
            if (SuperController.singleton == null) return;

            try
            {
                SuperController.singleton.AddCanvas(canvas);
                _registeredWithSuperController = true;
            }
            catch { }
        }

        private IEnumerator RefreshRaycasterNextFrame()
        {
            yield return null;
            if (canvas == null) yield break;
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null) yield break;
            // Toggle to force Unity/VaM to rebuild internal raycast state.
            raycaster.enabled = false;
            raycaster.enabled = true;
        }

        private IEnumerator RefreshRaycasterAfterDelay(float delaySecs)
        {
            yield return new WaitForSecondsRealtime(delaySecs);
            yield return StartCoroutine(RefreshRaycasterNextFrame());
        }

        private Atom GetBestTargetAtom()
        {
            if (SuperController.singleton == null) return null;

            // 0. Prefer the target selected in the GalleryPanel dropdown
            try
            {
                Atom selectedInDropdown = SelectedTargetAtom;
                if (selectedInDropdown != null) return selectedInDropdown;
            }
            catch { }

            // 1. Prefer selected atom if it's a Person
            try
            {
                Atom selected = SuperController.singleton.GetSelectedAtom();
                if (selected != null && SceneUtils.IsPersonLikeAtom(selected)) return selected;
            }
            catch { }

            // 2. Fallback: Find any Person atom in the scene
            try
            {
                List<Atom> allAtoms = SuperController.singleton.GetAtoms();
                if (allAtoms != null)
                {
                    foreach (Atom a in allAtoms)
                    {
                        if (a == null) continue;
                        try { if (SceneUtils.IsPersonLikeAtom(a)) return a; } catch { }
                    }
                }
            }
            catch { }

            return null;
        }

        private bool ExecuteAutoActionForFile(FileEntry file)
        {
            if (file == null) return false;

            try
            {
                // Create a lightweight action runner without showing any UI.
                var go = new GameObject("VPB_AutoActionRunner");
                go.hideFlags = HideFlags.HideAndDontSave;

                try
                {
                    var dragger = go.AddComponent<UIDraggableItem>();
                    dragger.FileEntry = file;
                    dragger.Panel = this;

                    string pathLower = (file.Path ?? "").ToLowerInvariant();
                    string category = CurrentCategoryTitle ?? "";
                    string categoryLower = category.ToLowerInvariant();

                    // Match the primary tab's first action behavior (auto action = first button).
                    if (pathLower.EndsWith(".var"))
                    {
                        try
                        {
                            // Not a user "open" — cache warm only; do not write History / item_usage.
                            NativeTextureOnDemandCache.SetNextJobWriteModeOverride(NativeTextureOnDemandCache.CacheWriteMode.ZstdOnly);
                            NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(this, file.Path);
                            return true;
                        }
                        catch { return false; }
                    }

                    if (pathLower.Contains("/clothing/") || pathLower.Contains("\\clothing\\") || category.Contains("Clothing"))
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                        dragger.LoadClothing(target);
                        return true;
                    }

                    if (pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || category.Contains("SubScene"))
                    {
                        dragger.LoadSubScene(file.Uid);
                        return true;
                    }

                    bool isScene = pathLower.EndsWith(".json") && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene") || category.Contains("Scene"));
                    if (isScene)
                    {
                        dragger.LoadSceneFile(file.Uid);
                        return true;
                    }

                    if (pathLower.Contains("/hair/") || pathLower.Contains("\\hair\\") || category.Contains("Hair"))
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                        dragger.LoadHair(target);
                        return true;
                    }

                    if (pathLower.Contains("/skin/") || pathLower.Contains("\\skin\\") || category.Contains("Skin"))
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                        dragger.LoadSkin(target);
                        return true;
                    }

                    if (pathLower.Contains("/morphs/") || pathLower.Contains("\\morphs\\") || category.Contains("Morphs"))
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                        dragger.LoadMorphs(target);
                        return true;
                    }

                    if (pathLower.Contains("/appearance/") || pathLower.Contains("\\appearance\\") || category.Contains("Appearance"))
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                        dragger.LoadAppearance(target);
                        return true;
                    }

                    bool isPluginScript =
                        (pathLower.Contains("/custom/scripts/") || pathLower.Contains("\\custom\\scripts\\"))
                        && (pathLower.EndsWith(".cs") || pathLower.EndsWith(".cslist") || pathLower.EndsWith(".dll"));
                    bool isPluginPreset =
                        pathLower.Contains("/custom/atom/person/plugins/") ||
                        pathLower.Contains("\\custom\\atom\\person\\plugins\\") ||
                        pathLower.Contains("/custom/pluginpresets/") ||
                        pathLower.Contains("\\custom\\pluginpresets\\") ||
                        (pathLower.EndsWith(".vap") && (categoryLower.Contains("person plugins") || categoryLower.Contains("plugin preset") || categoryLower.Contains("plugins")));
                    if (isPluginScript || isPluginPreset || categoryLower.Contains("plugins"))
                    {
                        // Category "Plugins" also covers script rows; avoid false-positives on non-script/non-vap.
                        if (isPluginScript || isPluginPreset
                            || pathLower.EndsWith(".cs") || pathLower.EndsWith(".cslist") || pathLower.EndsWith(".dll")
                            || pathLower.EndsWith(".vap"))
                        {
                            Atom target = GetBestTargetAtom();
                            if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                            dragger.LoadPlugins(target);
                            return true;
                        }
                    }

                    if (pathLower.Contains("/pose/") || pathLower.Contains("\\pose\\") || pathLower.Contains("/person/") || pathLower.Contains("\\person\\") || category.Contains("Pose"))
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                        dragger.LoadPose(target);
                        return true;
                    }

                    if (pathLower.Contains("/assets/") || pathLower.Contains("\\assets\\") || pathLower.EndsWith(".assetbundle") || pathLower.EndsWith(".unity3d"))
                    {
                        Atom selected = null;
                        try { selected = SuperController.singleton != null ? SuperController.singleton.GetSelectedAtom() : null; } catch { selected = null; }
                        if (selected != null && selected.type == "CustomUnityAsset") dragger.LoadCUAIntoAtom(selected, file.Uid);
                        else dragger.LoadCUA(file.Uid);
                        return true;
                    }

                    return false;
                }
                finally
                {
                    try { UnityEngine.Object.Destroy(go); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] ExecuteAutoActionForFile error: " + ex);
                return false;
            }
        }

        private void LoadRandom()
        {
            try
            {
                // Prefer the currently visible list (includes top search + filter-mode search).
                // lastFilteredFiles is a post-refresh snapshot and does not change when the user searches.
                var pool = (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                    ? currentFilteredFiles
                    : lastFilteredFiles;

                if (pool == null || pool.Count == 0)
                {
                    LogUtil.LogWarning("[VPB] Load Random: no items available.");
                    return;
                }

                int idx = UnityEngine.Random.Range(0, pool.Count);
                FileEntry file = pool[idx];
                if (file == null)
                {
                    LogUtil.LogWarning("[VPB] Load Random: selected file was null.");
                    return;
                }

                // Select it
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;

                selectedFiles.Add(file);
                if (!string.IsNullOrEmpty(file.Path)) selectedFilePaths.Add(file.Path);
                selectedPath = file.Path;
                // Selection should not "stick" the hover path. Hover-only content comes from pointer enter.
                SetHoverPath("");
                RefreshSelectionVisuals();
                UpdatePaginationText();

                // Apply (same logic as click)
                string pathLower = (file.Path ?? "").ToLowerInvariant();
                bool isSubScene = pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || (currentCategoryTitle != null && currentCategoryTitle.Contains("SubScene"));
                bool isScene = !isSubScene && pathLower.EndsWith(".json") && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene") || (currentCategoryTitle != null && currentCategoryTitle.Contains("Scene")));

                if (isScene)
                {
                    UI.LoadSceneFile(file, this);
                    return;
                }

                if (!ExecuteAutoActionForFile(file))
                {
                    LogUtil.LogWarning("[VPB] Load Random: no auto action available for this item.");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Load Random exception: " + ex);
            }
        }

        /// <summary>
        /// Title-bar / side Refresh: rescan packages and reload the grid while preserving scroll when possible.
        /// Needed when <see cref="VPBConfig.GalleryManualRefreshOnly"/> blocks automatic file-manager updates.
        /// Waits for the async package scan so Path listings / SQL <c>var_path</c> match disk after Explorer moves.
        /// </summary>
        public void UserRequestedPackageRefresh()
        {
            try
            {
                if (cleanupModeActive)
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.status.refreshing_packages", "Refreshing packages..."), 1.5f);
                    RebuildCleanupCandidates(true, true);
                    return;
                }

                if (_userPackageRefreshCo != null)
                {
                    try { StopCoroutine(_userPackageRefreshCo); } catch { }
                    _userPackageRefreshCo = null;
                }
                _userPackageRefreshCo = StartCoroutine(UserRequestedPackageRefreshCo());
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Refresh packages failed: " + ex);
                ShowTemporaryStatus(VPBTranslation.T("gallery.status.refresh_failed", "Refresh failed. See log."), 2f);
            }
        }

        private Coroutine _userPackageRefreshCo;

        private IEnumerator UserRequestedPackageRefreshCo()
        {
            ShowTemporaryStatus(VPBTranslation.T("gallery.status.refreshing_packages", "Refreshing packages..."), 2.5f);

            DateTime scanBefore = DateTime.MinValue;
            try { scanBefore = FileManager.lastPackageRefreshTime; } catch { }

            try { FileManagerBridge.Refresh("gallery_manual", RefreshScope.Both, init: true); } catch { }

            // Let RefreshCo start (or attach to an in-flight coalesced scan).
            yield return null;

            float waited = 0f;
            const float maxWaitSec = 180f;
            while (waited < maxWaitSec)
            {
                bool scanning = false;
                try { scanning = FileManager.IsScanning; } catch { }
                DateTime scanNow = DateTime.MinValue;
                try { scanNow = FileManager.lastPackageRefreshTime; } catch { }

                if (!scanning && scanNow > scanBefore)
                    break;
                if (!scanning && waited > 0.5f)
                    break;

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            // One frame for onRefreshHandlers / MessageKit after scan clock stamp.
            yield return null;

            try
            {
                VpbLocalDatabase.TrySyncAllPkgPathsFromLivePackages(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            catch { }

            // Deleted/moved Path folder while still selected → clear so RefreshFiles is not empty-filtered.
            try { TryClearStalePackagePathFilter(); } catch { }

            GalleryFileListSnapshotCache.InvalidateAll();
            creatorsCached = false;
            categoriesCached = false;
            tagsCached = false;
            pathsCached = false;
            refreshOnNextShow = true;
            // RefreshFiles is async: Path tabs need Custom/Saves from the finished grid.
            // Do not UpdateTabs here — CachePaths would mark pathsCached with SQL AddonPackages only.
            RefreshFiles(true);
            refreshOnNextShow = false;
            try { lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime; } catch { }
            try { GallerySortManager.StartBackgroundWarmLooseDepsCache(); } catch { }

            _userPackageRefreshCo = null;
        }

        /// <summary>
        /// Right-click Refresh: native VaM FileManager.Refresh only (catalog / package handlers).
        /// Does not rescan the VPB package index or reload the gallery grid.
        /// </summary>
        public void UserRequestedNativeFileManagerRefresh()
        {
            try
            {
                LogUtil.Log("[VPB] Gallery refresh right-click: native VaM FileManager.Refresh");
                ShowTemporaryStatus(VPBTranslation.T("gallery.status.refreshing_vam_files", "Refreshing VaM file list..."), 1.5f);

                FileManagerBridge.Refresh("gallery_native", RefreshScope.NativeOnly, flushNativeImmediately: true);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Native file manager refresh failed: " + ex);
                ShowTemporaryStatus(VPBTranslation.T("gallery.status.refresh_failed", "Refresh failed. See log."), 2f);
            }
        }

        public void Show(string title, string extension, string path)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool needsInit = canvas == null;
            LogUtil.Log("[Gallery] GalleryPanel.Show entry: title='" + title + "' path='" + path + "' needsInit=" + needsInit + " currentPath='" + currentPath + "' hasLoadedContent=" + hasLoadedContent);
            _userHidden = false;

            if (_benchPickModeActive && !BenchPickModeAllowsShowRequest(title))
            {
                ShowTemporaryStatus(VPBTranslation.T("bench.pick.block_nav",
                    "End Scene Load Test selection first (Done or Cancel)."), 2.5f);
                return;
            }

            // Otherwise the next-frame yield path immediately hides us again.
            if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryAnchorToVamMenu
                  && VPBConfig.Instance.AnchorYieldsToVamPanels && XrUtils.IsVrActive())
            {
                try
                {
                    var sc = SuperController.singleton;
                    if (sc != null)
                    {
                        if (sc.activeUI != SuperController.ActiveUI.None)
                            sc.activeUI = SuperController.ActiveUI.None;
                        if (sc.fileBrowserUI != null && sc.fileBrowserUI.window != null && sc.fileBrowserUI.window.activeSelf)
                            sc.fileBrowserUI.Hide();
                        if (sc.mediaFileBrowserUI != null && sc.mediaFileBrowserUI.window != null && sc.mediaFileBrowserUI.window.activeSelf)
                            sc.mediaFileBrowserUI.Hide();
                        if (sc.GetSelectedController() != null) sc.ClearSelection();
                    }
                }
                catch (Exception ex) { LogUtil.LogError("Show priority-takeover failed: " + ex.Message); }
            }

            if (needsInit) Init();
            LogUtil.Log("[Gallery] GalleryPanel.Show post-init: " + sw.ElapsedMilliseconds + "ms");

            // Switching middle content (category/page) must leave internal settings mode.
            // Default behavior: auto-save on exit; only explicit Discard uses cancel path.
            if (IsSettingsPanelOpen() || settingsListViewActive)
                ExitInternalSettingsMode(true);

            bool registeredBefore = _registeredWithSuperController;
            EnsureCanvasRegisteredWithSuperController();

            // Lazy-load per-category scroll cache; capture key for the category we may be leaving.
            if (!_scrollCacheLoaded) LoadCategoryScrollCache();
            string _prevCategoryKey = MakeCategoryScrollKey(currentCategoryTitle, currentPath);

            DateTime pkgRefreshTime = DateTime.MinValue;
            try { pkgRefreshTime = FileManager.lastPackageRefreshTime; } catch { }
            // Init() often runs before FileManager stamps lastPackageRefreshTime; lastApplied stayed MinValue
            // and the first Show then treated every open as "packages changed". Adopt the current clock once.
            if (lastAppliedPackageRefreshTime == DateTime.MinValue && pkgRefreshTime > DateTime.MinValue)
                lastAppliedPackageRefreshTime = pkgRefreshTime;

            bool packageTimestampAdvanced = false;
            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryManualRefreshOnly)
                packageTimestampAdvanced = (pkgRefreshTime > lastAppliedPackageRefreshTime);

            // After the panel has loaded once, package updates should flow through
            // Gallery.NotifyPackagesChanged -> ApplyPackageDelta instead of forcing
            // a full RefreshFiles() during Show(). This avoids hide/open race stalls.
            bool packagesChanged = refreshOnNextShow || (!hasLoadedContent && packageTimestampAdvanced);

            titleText.text = title;
            bool paramsChanged = (currentExtension != extension || currentPath != path);
            bool categoryTitleChanged = !string.Equals(title, currentCategoryTitle, StringComparison.Ordinal);

            // Navigating to a different category while a Try-On preview is still pending should not
            // silently discard it. Auto-commit (implicit Keep) so e.g. previewing clothing then moving
            // to the Appearance category keeps that clothing instead of reverting when the next preset
            // loads. Only fires on an actual category change, so flipping through looks in the same
            // category still unstacks normally.
            if (categoryTitleChanged && hasLoadedContent && _tryOnActive)
            {
                try { TryOnKeep(); } catch { }
            }

            if (cleanupModeActive && (paramsChanged || categoryTitleChanged))
            {
                try { ExitCleanupModeForSidePanelNavigation(restoreGalleryCategory: false, refreshGalleryFiles: false); } catch { }
            }

            if (paramsChanged)
            {
                // Save current category's filters before switching away
                if (hasLoadedContent)
                    SaveCurrentCategoryFilterState(currentCategoryTitle, currentPath);

                if (leftActiveContent == ContentType.History) leftActiveContent = null;
                if (rightActiveContent == ContentType.History) rightActiveContent = null;
                SyncActiveContentTypeFromSidePanels();

                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
                pathsCached = false;
                userTagsCached = false;
            }
            else if (packagesChanged)
            {
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
                pathsCached = false;
                userTagsCached = false;
            }

            currentCategoryTitle = title;

            bool sameViewReopen = hasLoadedContent && !paramsChanged;

            // Save scroll for the category we're leaving; prime the restore target for the new one.
            if (paramsChanged && hasLoadedContent && scrollRect != null)
            {
                categoryScrollPositions[_prevCategoryKey] = Mathf.Clamp01(scrollRect.verticalNormalizedPosition);
                sessionCategoryScrollKeys.Add(_prevCategoryKey);
                SaveCategoryScrollCache();
            }
            string nextCategoryKey = MakeCategoryScrollKey(title, path);
            if (!hasLoadedContent)
            {
                // Cold launch should always start at top.
                _pendingScrollRestore = 1f;
            }
            else if (paramsChanged)
            {
                // Category switch: only restore positions that were captured in this runtime session.
                // Persisted cache values from previous runs are intentionally ignored here to prevent stale/random starts.
                if (sessionCategoryScrollKeys.Contains(nextCategoryKey) &&
                    categoryScrollPositions.TryGetValue(nextCategoryKey, out float _sp))
                    _pendingScrollRestore = Mathf.Clamp01(_sp);
                else
                    _pendingScrollRestore = 1f;
            }
            else
            {
                // Same-view reopen/refresh can restore remembered position.
                _pendingScrollRestore = categoryScrollPositions.TryGetValue(nextCategoryKey, out float _sp)
                    ? Mathf.Clamp01(_sp)
                    : 1f;
            }

            currentExtension = extension;
            currentPath = path;
            
            // Set currentPaths
            currentPaths = null;
            if (categories != null) {
                var cat = categories.FirstOrDefault(c => c.path == path && c.name == title);
                if (!string.IsNullOrEmpty(cat.name)) currentPaths = cat.paths;
            }
            if (currentPaths == null) currentPaths = new List<string> { path };

            // Restore per-category filters (or clear to defaults for first visit)
            if (paramsChanged)
                RestoreCategoryFilterState(title, path);

            // Auto gender subfilter must apply on category change too (before RefreshFiles builds grid).
            ReconcileAutoGenderForCurrentTarget();

            if (Application.isPlaying && canvas.renderMode == RenderMode.WorldSpace)
            {
                // In VR on startup, Camera.main can be null briefly. Rebind whenever it becomes available.
                if (Camera.main != null)
                    canvas.worldCamera = Camera.main;
            }

            // Decide refresh before UpdateLayout so we can avoid synchronous full-library cache scans
            // (CacheCreators / CacheCategoryCounts) when RefreshFilesRoutine will rebuild them on a worker thread.
            bool shouldRefresh = paramsChanged || !hasLoadedContent || packagesChanged;
            bool startupDeferredInitialRefresh = false;
            if (shouldRefresh && !hasLoadedContent && !LogUtil.IsStartupReadyLogged())
            {
                startupDeferredInitialRefresh = true;
                shouldRefresh = false;
                ScheduleInitialRefreshAfterStartupReady();
            }

            try
            {
                if (shouldRefresh && Gallery.IsSuppressed())
                {
                    LogUtil.Log("[VPB] GalleryPanel.Show: Skipping RefreshFiles (suppressed)");
                    lastAppliedPackageRefreshTime = pkgRefreshTime;
                    shouldRefresh = false;
                }
            }
            catch (Exception suppressEx)
            {
                LogUtil.LogError($"[VPB] Error checking suppress state: {suppressEx.Message}");
            }

            // Fast reopen path: same already-loaded view should just become visible again.
            // Do not run layout/tabs/refresh logic here; it causes the redraw/flicker you reported.
            if (sameViewReopen && hasLoadedContent && !shouldRefresh)
            {
                SetCanvasVisible(true);
                if (refreshOnNextShow)
                {
                    refreshOnNextShow = false;
                    lastAppliedPackageRefreshTime = pkgRefreshTime;
                    try { RefreshVisibleGridVisualsOnly(); } catch { }
                }
                // Restore split sub-pane chrome (tags / scene source) after hide — no list rebuild.
                try { UpdateTabsImpl(rebuildSideTabLists: false); } catch { }
                try { EnsureSideTabsFreshForPackageScan(); } catch { }
                try { TryApplyPendingPackageDeltaOnShow(); } catch { }
                CancelGalleryCategoryTypeNavigationTiming("same_view_reopen");
                LogUtil.Log("[Gallery] GalleryPanel.Show done: " + sw.ElapsedMilliseconds + "ms title='" + currentCategoryTitle + "' path='" + currentPath + "'");
                return;
            }

            LogGalleryCategoryTypeNavPhase("Show_before_UpdateLayout_1");
            UpdateSideButtonsVisibility();
            UpdateLayout(!shouldRefresh && !sameViewReopen);
            LogGalleryCategoryTypeNavPhase("Show_after_UpdateLayout_1");
            RefreshTargetDropdown();

            SetCanvasVisible(true);

            // Refresh raycast on first show (cold-launch VR fix) and on late registration.
            // On cold launch, VaM's VR pointer system may not have connected to the canvas yet
            // even when registration succeeded in Init().
            bool isFirstShow = !hasLoadedContent;
            if (isFirstShow || (!registeredBefore && _registeredWithSuperController))
            {
                try { StartCoroutine(RefreshRaycasterNextFrame()); } catch { }
            }
            // Second delayed refresh: VaM's VR pointer system may take ~1 second to fully connect.
            if (isFirstShow)
            {
                try { StartCoroutine(RefreshRaycasterAfterDelay(1f)); } catch { }
            }

            if (shouldRefresh)
            {
                RefreshFiles(hasLoadedContent && !paramsChanged);
                refreshOnNextShow = false;
                lastAppliedPackageRefreshTime = pkgRefreshTime;
                LogGalleryCategoryTypeNavPhase("Show_after_RefreshFiles_invoke");
            }
            else
            {
                if (startupDeferredInitialRefresh)
                {
                    _sideTabsNeedFullRebuildAfterFirstRefresh = true;
                    LogUtil.Log("[VPB] GalleryPanel.Show: deferred initial RefreshFiles until startup ready");
                }
                LogGalleryCategoryTypeNavPhase("Show_skip_RefreshFiles");
                try { TryApplyPendingPackageDeltaOnShow(); } catch { }
            }

            // Same-view reopen / first load before grid refresh: avoid synchronous category count scans on stale inventory.
            if (sameViewReopen || refreshCoroutine != null || startupDeferredInitialRefresh || !hasLoadedContent || _sideTabsNeedFullRebuildAfterFirstRefresh)
                UpdateTabsImpl(rebuildSideTabLists: false);
            else
                UpdateTabs();
            LogGalleryCategoryTypeNavPhase("Show_after_UpdateTabs");
            UpdateLayout(!sameViewReopen && refreshCoroutine == null);
            LogGalleryCategoryTypeNavPhase("Show_after_UpdateLayout_2");
            RefreshImportSidebarCategoryGate();

            // Position it in front of the user if in VR, ONLY ONCE
            if (!hasBeenPositioned)
            {
                Transform targetTransform = null;
                if (Camera.main != null) targetTransform = Camera.main.transform;
                else if (SuperController.singleton != null) targetTransform = SuperController.singleton.centerCameraTarget.transform;

                if (targetTransform != null)
                {
                    // Place 2.0m in front of camera
                    canvas.transform.position = targetTransform.position + targetTransform.forward * 2.0f;
                    
                    // Face the user
                    Vector3 lookDir = canvas.transform.position - targetTransform.position;
                    
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        canvas.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                    }
                    
                    hasBeenPositioned = true;
                }
            }
            if (_paneLoadTimingStopwatch != null && refreshCoroutine == null)
                CompletePaneLoadTimingIfPending("(Show finished without async refresh)");
            if (refreshCoroutine == null)
                FinalizeGalleryCategoryTypeNavigationSync("(Show end, no async refresh)");
            LogUtil.Log("[Gallery] GalleryPanel.Show done: " + sw.ElapsedMilliseconds + "ms title='" + currentCategoryTitle + "' path='" + currentPath + "'");
        }

        private Coroutine deferredStartupRefreshCoroutine;
        public bool HasDeferredStartupRefreshPending => deferredStartupRefreshCoroutine != null;

        private void ScheduleInitialRefreshAfterStartupReady()
        {
            if (deferredStartupRefreshCoroutine != null) return;
            deferredStartupRefreshCoroutine = StartCoroutine(DeferredInitialRefreshAfterStartupReady());
        }

        private IEnumerator DeferredInitialRefreshAfterStartupReady()
        {
            while (!LogUtil.IsStartupReadyLogged())
                yield return null;

            if (hasLoadedContent)
            {
                deferredStartupRefreshCoroutine = null;
                yield break;
            }
            if (canvas == null)
            {
                deferredStartupRefreshCoroutine = null;
                yield break;
            }

            try
            {
                RefreshFiles(false);
            }
            catch { }
            finally
            {
                deferredStartupRefreshCoroutine = null;
            }
        }

        public void Hide()
        {
            _userHidden = true;
            _hiddenByMenuGate = false;
            VpbPerfDiag.LogTransition("GalleryPanel.Hide", "userHidden=true");
            SetCanvasVisible(false);

            hoverCount = 0;
            try { HideHoverPreview(null); } catch { }
        }

        private void SetCanvasVisible(bool visible)
        {
            if (canvas == null) return;

            bool isVR = XrUtils.IsVrActive();

            bool wasEnabled = canvas.enabled;
            if (VpbPerfDiag.CachedEnabled && wasEnabled != visible)
            {
                if (visible) VpbPerfDiag.SetCanvasVisibleOn++;
                else VpbPerfDiag.SetCanvasVisibleOff++;
                VpbPerfDiag.LogTransition("SetCanvasVisible",
                    "from=" + (wasEnabled ? "on" : "off") + " to=" + (visible ? "on" : "off")
                    + " userHidden=" + _userHidden + " menuGate=" + _hiddenByMenuGate + " isVR=" + isVR);
            }

            if (!visible)
            {
                _pendingVisibleAfterStartupReady = false;
                StopCo(ref _deferredSetVisibleCoroutine);
                ApplyImmediateVisibility(false);
                _queuedRaycastRefreshOnVisible = false;
                return;
            }

            // VR cold boot: enabling world-space canvas too early can produce “visible but dead” pointer state.
            // Defer actual enable until World UI ready and menu visible; then do full refresh + raycaster rebuild.
            if (isVR && Application.isPlaying && !LogUtil.IsStartupReadyLogged())
            {
                _pendingVisibleAfterStartupReady = true;
                if (_deferredSetVisibleCoroutine == null)
                    _deferredSetVisibleCoroutine = StartCoroutine(DeferredSetVisibleAfterStartupReady());
                // Keep disabled until ready to avoid stuck non-interactible canvas.
                ApplyImmediateVisibility(false);
                return;
            }

            ApplyImmediateVisibility(true);

            // Robust cold-boot fix: if first refresh got deferred while menu-gated hidden,
            // ensure we run (or schedule) initial refresh on any transition to visible.
            if (visible && Application.isPlaying && !hasLoadedContent && refreshCoroutine == null)
            {
                // If no category ever selected (should not happen, but can if created without Show()),
                // force a first Show() using configured initial category.
                if (string.IsNullOrEmpty(currentPath) && categories != null && categories.Count > 0)
                {
                    try
                    {
                        var initial = categories[0];
                        string categoryToOpen = null;
                        if (VPBConfig.Instance != null) categoryToOpen = VPBConfig.Instance.ResolveInitialGalleryCategoryName();
                        if (!string.IsNullOrEmpty(categoryToOpen))
                        {
                            for (int i = 0; i < categories.Count; i++)
                            {
                                if (string.Equals(categories[i].name, categoryToOpen, StringComparison.OrdinalIgnoreCase))
                                {
                                    initial = categories[i];
                                    break;
                                }
                            }
                        }
                        Show(initial.name, initial.extension, initial.path);
                        return;
                    }
                    catch { }
                }

                // If startup not ready yet, schedule deferred refresh (idempotent).
                if (!LogUtil.IsStartupReadyLogged())
                {
                    try { ScheduleInitialRefreshAfterStartupReady(); } catch { }
                }
                else
                {
                    try { RefreshFiles(false); } catch { }
                }
            }

            // Cold-boot VR fix when gallery is shown via menu gate (no Show() call).
            // VaM VR pointer wiring can lag behind canvas enable; force rebuild next frame + after short delay.
            if (visible)
            {
                if (isVR && Application.isPlaying && !_queuedRaycastRefreshOnVisible)
                {
                    _queuedRaycastRefreshOnVisible = true;
                    try { StartCoroutine(RefreshRaycasterNextFrame()); } catch { }
                    try { StartCoroutine(RefreshRaycasterAfterDelay(1f)); } catch { }
                }
            }
            else
            {
                _queuedRaycastRefreshOnVisible = false;
            }
        }

        private void ApplyImmediateVisibility(bool v)
        {
            if (canvas == null) return;
            canvas.enabled = v;
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = v;
            // canvas.enabled=false halts rendering but every child MonoBehaviour keeps ticking; deactivate the subtree too.
            bool wantSubtree = ShouldContentSubtreeBeActive();
            if (backgroundBoxGO != null && backgroundBoxGO.activeSelf != wantSubtree)
                backgroundBoxGO.SetActive(wantSubtree);
        }

        // Desired active state for the gallery content subtree (backgroundBoxGO).
        // A collapsed fixed pane parks its content off-screen, but off-screen UI is still fully
        // drawn and raycast-walked by the canvas every frame, so a loaded-but-collapsed pane keeps
        // halving FPS. Deactivate the subtree in that state. Keep it active until the first content
        // build finishes though: an inactive parent can leave the recycling grid with a zero viewport.
        private bool ShouldContentSubtreeBeActive()
        {
            if (canvas == null || !canvas.enabled) return false;
            if (isCollapsed && hasLoadedContent) return false;
            return true;
        }

        private IEnumerator DeferredSetVisibleAfterStartupReady()
        {
            while (!LogUtil.IsStartupReadyLogged())
                yield return null;

            _deferredSetVisibleCoroutine = null;
            if (!_pendingVisibleAfterStartupReady) yield break;
            _pendingVisibleAfterStartupReady = false;
            if (canvas == null) yield break;

            // Wait until menu visible too (anchor gate path).
            while (!IsVamMenuVisible())
                yield return null;

            ApplyImmediateVisibility(true);

            try { EnsureCanvasRegisteredWithSuperController(); } catch { }

            // Force VaM/Unity to rebuild pointer interaction now that UI is ready.
            try { StartCoroutine(RefreshRaycasterNextFrame()); } catch { }
            try { StartCoroutine(RefreshRaycasterAfterDelay(1f)); } catch { }

            // Ensure initial content refresh runs once we become visible.
            if (!hasLoadedContent && refreshCoroutine == null)
            {
                if (!LogUtil.IsStartupReadyLogged())
                {
                    try { ScheduleInitialRefreshAfterStartupReady(); } catch { }
                }
                else
                {
                    try { RefreshFiles(false); } catch { }
                }
            }

        }

        private static bool IsVamMenuVisible()
        {
            try
            {
                return SuperController.singleton != null &&
                       SuperController.singleton.mainHUD != null &&
                       SuperController.singleton.mainHUD.gameObject != null &&
                       SuperController.singleton.mainHUD.gameObject.activeInHierarchy;
            }
            catch { return true; }
        }

        private void ApplyVamMenuGateVisibility()
        {
            if (VPBConfig.Instance == null || canvas == null) return;
            bool isVR = XrUtils.IsVrActive();

            // The anchor-based gate only applies to the specific panel that is anchored.
            bool isAnchoredInstance = (GetAnchoredInstance() == this);
            bool isAnchored = isVR && VPBConfig.Instance.GalleryAnchorToVamMenu && isAnchoredInstance;

            bool gate = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible || isAnchored;
            bool menuVisible = IsVamMenuVisible();

            // SelectedOptions is VaM's idle default state; only treat as yield when a controller is actually selected.
            bool yieldTrigger = false;
            if (isAnchored && VPBConfig.Instance.AnchorYieldsToVamPanels)
            {
                var sc = SuperController.singleton;
                if (sc != null)
                {
                    var aui = sc.activeUI;
                    yieldTrigger = aui == SuperController.ActiveUI.MainMenu
                                || aui == SuperController.ActiveUI.MainMenuOnly
                                || aui == SuperController.ActiveUI.OnlineBrowser
                                || aui == SuperController.ActiveUI.PackageBuilder
                                || aui == SuperController.ActiveUI.PackageManager
                                || aui == SuperController.ActiveUI.PackageDownloader
                                || aui == SuperController.ActiveUI.MultiButtonPanel
                                || aui == SuperController.ActiveUI.EmbeddedScenePanel
                                || aui == SuperController.ActiveUI.Custom;

                    if (!yieldTrigger && aui == SuperController.ActiveUI.SelectedOptions)
                    {
                        // Edit to Play does not clear selectedController; without gameMode guard VPB stays hidden in Play.
                        try
                        {
                            var ctrl = sc.GetSelectedController();
                            yieldTrigger = ctrl != null && !ctrl.guihidden && sc.gameMode == SuperController.GameMode.Edit;
                        }
                        catch { }
                    }

                    if (!yieldTrigger)
                    {
                        try
                        {
                            if (sc.fileBrowserUI != null && sc.fileBrowserUI.window != null && sc.fileBrowserUI.window.activeSelf)
                                yieldTrigger = true;
                            else if (sc.mediaFileBrowserUI != null && sc.mediaFileBrowserUI.window != null && sc.mediaFileBrowserUI.window.activeSelf)
                                yieldTrigger = true;
                        }
                        catch { }
                    }
                }
            }

            if (!gate && !yieldTrigger)
            {
                if (_hiddenByMenuGate && !_userHidden)
                {
                    if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.MenuGateFlip++;
                    SetCanvasVisible(true);
                    _hiddenByMenuGate = false;
                }
                return;
            }

            bool shouldHide = yieldTrigger || (gate && !menuVisible);

            if (shouldHide)
            {
                if (canvas.enabled)
                {
                    if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.MenuGateFlip++;
                    SetCanvasVisible(false);
                    _hiddenByMenuGate = true;
                }
            }
            else
            {
                if (_hiddenByMenuGate && !_userHidden)
                {
                    if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.MenuGateFlip++;
                    SetCanvasVisible(true);
                    _hiddenByMenuGate = false;
                }
            }
        }

        private void ApplyVamMenuAnchoring()
        {
            if (VPBConfig.Instance == null || canvas == null) return;
            if (!XrUtils.IsVrActive()) return;
            if (!VPBConfig.Instance.GalleryAnchorToVamMenu) return;

            // Priority check: only the first visible panel gets anchored.
            if (GetAnchoredInstance() != this) return;

            // If we are the priority panel, check if menu is visible for snapping.
            if (!IsVamMenuVisible()) return;

            var sc = SuperController.singleton;
            Transform vamMenuTrans = sc.mainHUD.transform;
            if (vamMenuTrans == null) return;

            // Land VPB's bottom at the dock's top using mainHUD's own RectTransform; lossyScale captures any HUD or world-scale.
            RectTransform canvasRT = canvas.GetComponent<RectTransform>();
            float galleryHalfHeight = (canvasRT.rect.height * 0.5f) * canvasRT.lossyScale.y;
            RectTransform hudRT = vamMenuTrans.GetComponent<RectTransform>();
            float hudHalfHeight = (hudRT != null) ? (hudRT.rect.height * 0.5f) * hudRT.lossyScale.y : 0.1f;
            float gap = 0.01f;
            Vector3 targetPos = vamMenuTrans.position + (vamMenuTrans.up * (hudHalfHeight + gap + galleryHalfHeight));

            // WorldSpace canvas transform writes force a full canvas rebuild; skip when nothing moved.
            if (canvas.transform.position != targetPos)
                canvas.transform.position = targetPos;

            // mainHUD's forward faces away from user; rotate 180 on local Y so the canvas faces the user.
            Quaternion targetRot = vamMenuTrans.rotation * Quaternion.Euler(0, 180, 0);
            if (canvas.transform.rotation != targetRot)
                canvas.transform.rotation = targetRot;

            // Keep offsets reset so follow mode captures the anchored position when anchoring ends.
            offsetsInitialized = false;
        }


        private static string MakeCategoryScrollKey(string title, string path)
            => (title ?? "") + "|" + (path ?? "");

        private string ScrollCachePath
        {
            get
            {
                string baseDir = Directory.GetCurrentDirectory();
                return Path.Combine(Path.Combine(Path.Combine(Path.Combine(baseDir, "Saves"), "PluginData"), "VPB"), "gallery_scroll.json");
            }
        }

        private void LoadCategoryScrollCache()
        {
            _scrollCacheLoaded = true;
            try
            {
                string p = ScrollCachePath;
                if (!File.Exists(p)) return;
                JSONNode root = JSON.Parse(File.ReadAllText(p));
                if (root == null) return;
                categoryScrollPositions.Clear();
                foreach (KeyValuePair<string, JSONNode> kvp in root.AsObject)
                    categoryScrollPositions[kvp.Key] = kvp.Value.AsFloat;
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] ScrollCache load: " + ex.Message); }
        }

        private void SaveCategoryScrollCache()
        {
            try
            {
                string p = ScrollCachePath;
                string dir = Path.GetDirectoryName(p);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                JSONClass root = new JSONClass();
                foreach (var kvp in categoryScrollPositions)
                    root[kvp.Key].AsFloat = kvp.Value;
                File.WriteAllText(p, JsonSerializationUtil.Serialize(root, 4096));
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] ScrollCache save: " + ex.Message); }
        }

        public void SetHoverPath(FileEntry file)
        {
            if (file == null)
            {
                SetHoverPath("");
                return;
            }

            if (cleanupModeActive && file is CleanupFileEntry cfe && cfe.Candidate != null)
            {
                string details = BuildCleanupHoverDetails(cfe.Candidate);
                if (!string.IsNullOrEmpty(details))
                {
                    SetHoverPath((file.Path ?? "") + "\n" + details);
                    return;
                }
            }
            SetHoverPath(file.Path);
        }

        /// <summary>
        /// Pointer entered a gallery item — claim info-bar path ownership and show full path.
        /// Sibling cell exit (deferred) must not wipe this after claim.
        /// </summary>
        internal void ClaimHoverPath(UIHoverReveal owner, FileEntry file)
        {
            if (owner == null || file == null)
            {
                SetHoverPath(file);
                return;
            }
            // Set path first (empty path clears ownership), then claim so deferred sibling exit cannot wipe.
            SetHoverPath(file);
            _hoverPathRevealOwner = owner;
        }

        /// <summary>
        /// Pointer left a gallery item — restore count fallback only if this reveal still owns the path.
        /// </summary>
        internal void ReleaseHoverPath(UIHoverReveal owner)
        {
            if (owner == null || _hoverPathRevealOwner != owner) return;
            _hoverPathRevealOwner = null;
            RestoreSelectedHoverPath();
        }

        private string GetFilteredVisibleItemsCountText()
        {
            int total = (currentFilteredFiles != null) ? currentFilteredFiles.Count : 0;
            int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
            if (sel > 0)
            {
                // Prefer the selection phrasing used by the tbox label for consistency.
                string selStr = sel == 1
                    ? VPBTranslation.T("gallery.tbox.selected_one", "1 Selected")
                    : string.Format(VPBTranslation.T("gallery.tbox.selected_many", "{0} Selected"), sel);
                string countStr = string.Format(VPBTranslation.T("gallery.items.count", "{0} Items"), total);
                return string.Format("{0}  ·  {1}", selStr, countStr);
            }
            return string.Format(VPBTranslation.T("gallery.items.count", "{0} Items"), total);
        }

        private void RefreshHoverPathCountTextIfNeeded()
        {
            if (!hoverPathIsCountMode) return;
            if (hoverPathText == null) return;
            hoverPathText.text = GetFilteredVisibleItemsCountText();
        }

        public void SetHoverPath(string path)
        {
            // Direct callers drop reveal ownership; ClaimHoverPath re-assigns after SetHoverPath(file).
            _hoverPathRevealOwner = null;
            bool hasPath = !string.IsNullOrEmpty(path);
            hoverPathIsCountMode = !hasPath;
            float targetAlpha = 1f; // pure on/off: always visible (path or count fallback)

            // No fade/transition: snap alpha immediately.
            if (hoverFadeCoroutine != null)
            {
                StopCoroutine(hoverFadeCoroutine);
                hoverFadeCoroutine = null;
            }
            if (hoverPathCanvasGroup != null) hoverPathCanvasGroup.alpha = targetAlpha;

            if (hoverPathText != null)
            {
                if (hasPath)
                {
                    string displayPath = path;
                    // Ensure we show full internal paths for .var files without manual line breaks.
                    // Text wrapping is handled by the UI Text component.
                    hoverPathText.text = displayPath.Replace("/", "/\u200B").Replace(":", ":\u200B");
                }
                else
                {
                    // Hover-out fallback: show current filtered visible count.
                    RefreshHoverPathCountTextIfNeeded();
                }
            }
        }

        private IEnumerator FadeHoverPath(float targetAlpha)
        {
            if (hoverPathCanvasGroup != null) hoverPathCanvasGroup.alpha = targetAlpha;
            hoverFadeCoroutine = null;
            yield break;
        }

        public void RestoreSelectedHoverPath()
        {
            // When not hovering an item, always show filtered totals (+ selected count).
            SetHoverPath("");
        }

        private void SetNameFilter(string val)
        {
            string f = val ?? "";
            if (f == nameFilter) return;
            AssignNameFilterState(f);

            try
            {
                CancelTitleSearchSqlDebounce();
                CancelTitleSearchInMemoryDebounce();

                // In package filter mode, keep search scoped to the current filtered list
                // (do not refresh the whole gallery, which would clear filter mode).
                if (IsFilterActive)
                {
                    ApplySearchWithinFilter(f);
                    SyncBrowseFilterChipChrome();
                    return;
                }

                bool active = HasActiveNameFilter();

                // Outside filter mode: in-memory when base list known; SQL (debounced) for time
                // windows or when base is dirty. Bare terms OR into user tags via one key lookup.
                if (topSearchBaseFiles == null)
                {
                    if (!_topSearchBaseIsClean)
                    {
                        if (!active)
                        {
                            RefreshFiles();
                            return;
                        }
                        // Narrowing — SQL applies full search AST (name/tag/time).
                        ScheduleTitleSearchSqlRefresh();
                        return;
                    }
                    topSearchBaseFiles = new List<FileEntry>(currentFilteredFiles);
                }

                if (!active)
                {
                    currentFilteredFiles.Clear();
                    currentFilteredFiles.AddRange(topSearchBaseFiles);
                    topSearchBaseFiles = null;
                    _topSearchBaseIsClean = true;
                    FinishTitleSearchUiRefresh();
                }
                else if (nameFilterQuery.RequiresSqlRefresh)
                {
                    // Time / loaded / tagged windows need SQL; keep snapshot for instant clear.
                    ScheduleTitleSearchSqlRefresh();
                    return;
                }
                else
                {
                    // Debounce name/tag in-memory filter — per-keystroke full-list scans stall VaM.
                    ScheduleTitleSearchInMemoryApply();
                    return;
                }
            }
            finally
            {
                // Avoid chip rebuild + UpdateLayout on every keystroke; debounce with search apply.
                if (!HasActiveNameFilter() || nameFilterQuery.RequiresSqlRefresh)
                    SyncBrowseFilterChipChrome();
            }
        }

        private void CancelTitleSearchSqlDebounce()
        {
            if (_titleSearchSqlDebounceCo == null) return;
            try { StopCoroutine(_titleSearchSqlDebounceCo); } catch { }
            _titleSearchSqlDebounceCo = null;
        }

        private void CancelTitleSearchInMemoryDebounce()
        {
            if (_titleSearchInMemoryDebounceCo == null) return;
            try { StopCoroutine(_titleSearchInMemoryDebounceCo); } catch { }
            _titleSearchInMemoryDebounceCo = null;
        }

        private void ScheduleTitleSearchInMemoryApply()
        {
            CancelTitleSearchInMemoryDebounce();
            CancelTitleSearchSqlDebounce();
            if (!isActiveAndEnabled)
            {
                ApplyTitleSearchToBaseListInMemory();
                FinishTitleSearchUiRefresh();
                SyncBrowseFilterChipChrome();
                return;
            }
            _titleSearchInMemoryDebounceCo = StartCoroutine(TitleSearchInMemoryDebounceRoutine());
        }

        private IEnumerator TitleSearchInMemoryDebounceRoutine()
        {
            yield return new WaitForSecondsRealtime(0.12f);
            _titleSearchInMemoryDebounceCo = null;
            // Query may have changed again; apply current AST.
            if (topSearchBaseFiles == null) yield break;
            if (nameFilterQuery != null && nameFilterQuery.RequiresSqlRefresh)
            {
                ScheduleTitleSearchSqlRefresh();
                yield break;
            }
            ApplyTitleSearchToBaseListInMemory();
            FinishTitleSearchUiRefresh();
            SyncBrowseFilterChipChrome();
        }

        private void ScheduleTitleSearchSqlRefresh()
        {
            CancelTitleSearchSqlDebounce();
            CancelTitleSearchInMemoryDebounce();
            if (!isActiveAndEnabled)
            {
                RunTitleSearchSqlRefreshNow();
                return;
            }
            _titleSearchSqlDebounceCo = StartCoroutine(TitleSearchSqlDebounceRoutine());
        }

        private IEnumerator TitleSearchSqlDebounceRoutine()
        {
            yield return new WaitForSecondsRealtime(0.15f);
            _titleSearchSqlDebounceCo = null;
            RunTitleSearchSqlRefreshNow();
        }

        private void RunTitleSearchSqlRefreshNow()
        {
            // Preserve in-memory base so clearing search can restore without rebuild.
            if (topSearchBaseFiles != null)
                _keepTopSearchBaseAcrossRefresh = true;
            else if (_topSearchBaseIsClean && currentFilteredFiles != null)
            {
                topSearchBaseFiles = new List<FileEntry>(currentFilteredFiles);
                _keepTopSearchBaseAcrossRefresh = true;
            }
            try { RefreshFiles(); }
            catch { }
            finally { SyncBrowseFilterChipChrome(); }
        }

        private void ApplyTitleSearchToBaseListInMemory()
        {
            if (topSearchBaseFiles == null) return;
            var query = nameFilterQuery ?? GallerySearchQuery.Empty;

            bool isPackageList = false;
            try
            {
                if (topSearchBaseFiles.Count > 0)
                {
                    var head = topSearchBaseFiles[0];
                    isPackageList = head is PackageListEntry || head is MissingPackageListEntry;
                }
            }
            catch { isPackageList = false; }

            if (isPackageList && query.TagInclude.Count == 0 && query.TagExclude.Count == 0
                && query.CreatorTerms.Count == 0 && query.BroadTerms.Count > 0
                && (query.BroadExclude == null || query.BroadExclude.Count == 0))
            {
                // Package UID SQL fast path (name terms only).
                var allowedUids = new List<string>(topSearchBaseFiles.Count);
                for (int i = 0; i < topSearchBaseFiles.Count; i++)
                {
                    var e = topSearchBaseFiles[i];
                    if (e == null) continue;
                    string n = null;
                    try { n = e.Name; } catch { n = null; }
                    if (string.IsNullOrEmpty(n)) continue;
                    if (n.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                        n = n.Substring(0, n.Length - 4);
                    if (!string.IsNullOrEmpty(n))
                        allowedUids.Add(n);
                }

                var pkgRows = new List<VpbLocalDatabase.PackageRow>();
                bool gotSql = false;
                try
                {
                    gotSql = VpbLocalDatabase.TryQueryPackageRowsForUidsWithAllTerms(allowedUids, query.BroadTermsArray(), pkgRows);
                }
                catch { gotSql = false; }

                if (gotSql)
                {
                    var byUid = new Dictionary<string, VpbLocalDatabase.PackageRow>(pkgRows.Count, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < pkgRows.Count; i++)
                    {
                        var r = pkgRows[i];
                        if (!string.IsNullOrEmpty(r.PackageUid))
                            byUid[r.PackageUid] = r;
                    }

                    currentFilteredFiles.Clear();
                    for (int i = 0; i < allowedUids.Count; i++)
                    {
                        var uid = allowedUids[i];
                        if (string.IsNullOrEmpty(uid)) continue;
                        VpbLocalDatabase.PackageRow r;
                        if (!byUid.TryGetValue(uid, out r)) continue;
                        DateTime wt = DateTime.MinValue;
                        if (r.LastWriteTicksOrInvalid != long.MinValue)
                        {
                            try { wt = DateTime.FromBinary(r.LastWriteTicksOrInvalid); } catch { wt = DateTime.MinValue; }
                        }
                        currentFilteredFiles.Add(new PackageListEntry(r.PackageUid, r.VarPath, wt, r.PackageSizeOrInvalid, r.PackageCreationTicksOrInvalid, r.FirstScannedTicksOrInvalid));
                    }
                    return;
                }
            }

            // Name/path first; tag-key SQL only when terms warrant it (cached).
            var tagKeys = GetSearchTagKeysCached();
            var filtered = new List<FileEntry>(Math.Min(topSearchBaseFiles.Count, 256));
            for (int i = 0; i < topSearchBaseFiles.Count; i++)
            {
                var e = topSearchBaseFiles[i];
                if (e == null) continue;
                if (MatchesFileEntryBySearchQuery(e, query, tagKeys))
                    filtered.Add(e);
            }
            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(filtered);
        }

        private void FinishTitleSearchUiRefresh()
        {
            if (recyclingGrid != null)
            {
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                ScrollGalleryToTop();
                recyclingGrid.Refresh();
            }
            try { UpdatePaginationText(); } catch { }

            bool creatorTabOpen = (leftActiveContent.HasValue && leftActiveContent.Value == ContentType.Creator)
                               || (rightActiveContent.HasValue && rightActiveContent.Value == ContentType.Creator);
            if (creatorTabOpen)
                try { UpdateTabsImpl(rebuildSideTabLists: false); } catch { }
        }

        private bool PrepareFileEntryGestureSelection(FileEntry file)
        {
            bool historyBrowse = activeContentType == ContentType.History;
            string idKey = GetSelectionIdentityKey(file, historyBrowse);
            bool applyToSelection = selectedFiles != null && selectedFiles.Count > 0
                && !string.IsNullOrEmpty(idKey)
                && selectedFilePaths != null && selectedFilePaths.Contains(idKey);

            if (!applyToSelection)
            {
                try { DetailStripUnlockAfterExternalSelectionChange(); } catch { }
                HashSet<string> untaggedSelBefore = _userTagAvailMode == UserTagAvailMode.FilterUntagged
                    ? SnapshotSelectionIdentityKeys(this)
                    : null;
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                AddFileToSelection(file, historyBrowse);
                selectedPath = !string.IsNullOrEmpty(file.Path) ? file.Path : idKey;
                SetSelectionAnchor(file, historyBrowse);

                if (untaggedSelBefore != null)
                {
                    try
                    {
                        HashSet<string> deselected = BuildDeselectedSelectionKeys(untaggedSelBefore, SnapshotSelectionIdentityKeys(this));
                        if (deselected != null)
                            PruneUntaggedGridAfterSelectionChange(deselected);
                    }
                    catch { }
                }

                SetHoverPath("");
                RefreshSelectionVisuals();
                UpdatePaginationText();
            }

            return applyToSelection;
        }

        internal void OnFileRightClick(FileEntry file)
        {
            if (file == null || file is InternalSettingRowEntry) return;

            // Right click selects if not selected.
            // Note: We intentionally do NOT open the actions panel here; right-click should not
            // force any bottom UI to appear (a separate context menu implementation will handle actions).
            bool applyWhitelistToSelection = PrepareFileEntryGestureSelection(file);

            try
            {
                bool temporary = IsCtrlHeld();
                HandleDesktopScanWhitelistClickGesture(file, applyWhitelistToSelection, temporary);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] OnFileRightClick scan whitelist: " + ex); }

            if (isFixedLocally && VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedHeightMode == 0)
            {
                VPBConfig.Instance.DesktopFixedHeightMode = 1; // Custom height
                UpdateFooterHeightState();
                UpdateLayout();
            }
        }

        internal void OnFileMiddleClick(FileEntry file)
        {
            if (file == null || file is InternalSettingRowEntry) return;

            bool applyWhitelistToSelection = PrepareFileEntryGestureSelection(file);

            try { HandleDesktopScanWhitelistClickGesture(file, applyWhitelistToSelection, temporary: true); }
            catch (Exception ex) { LogUtil.LogError("[VPB] OnFileMiddleClick scan whitelist: " + ex); }
        }

        internal void OnFileClick(FileEntry file)
        {
            if (file == null) return;
            if (file is InternalSettingRowEntry)
            {
                HandleInternalSettingsRowClick(file, secondary: false);
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            
            if (ctrl && alt)
            {
                string copyName = file.Name;
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    copyName = vfe.Package.Uid + ".var";
                }
                
                LogUtil.Log("[VPB] Copying to clipboard: " + copyName);
                GUIUtility.systemCopyBuffer = copyName;
                ShowTemporaryStatus("Copied to clipboard: " + copyName, 2f);
                return;
            }

            // Import sidebar active: a single click sets the import source (instead of launching the scene),
            // but a double click still opens/launches the scene (falls through to the normal handling below).
            if (importSidebarActive)
            {
                float importClickTime = Time.realtimeSinceStartup;
                string importFileKey = !string.IsNullOrEmpty(file.Path) ? file.Path : file.Uid;
                bool importDoubleClick = (importClickTime - lastClickTime < 0.3f)
                    && string.Equals(selectedPath, importFileKey, StringComparison.OrdinalIgnoreCase);
                lastClickTime = importClickTime;
                if (!importDoubleClick)
                {
                    selectedFiles.Clear();
                    selectedFilePaths.Clear();
                    selectedFiles.Add(file);
                    if (!string.IsNullOrEmpty(file.Path)) selectedFilePaths.Add(file.Path);
                    selectionAnchorPath = file.Path;
                    selectedPath = importFileKey;
                    SetHoverPath("");
                    RefreshSelectionVisuals();
                    OpenImportSidebarWith(file, importSidebarTargetAtom);
                    return;
                }
                // double click: continue to the normal launch path below.
            }

            float time = Time.realtimeSinceStartup;
            string fileKey = !string.IsNullOrEmpty(file.Path) ? file.Path : file.Uid;
            bool isDoubleClick = (time - lastClickTime < 0.3f && string.Equals(selectedPath, fileKey, StringComparison.OrdinalIgnoreCase));
            lastClickTime = time;

            bool selectionChanged = false;
            HashSet<string> untaggedSelBefore = _userTagAvailMode == UserTagAvailMode.FilterUntagged
                ? SnapshotSelectionIdentityKeys(this)
                : null;

            // Update selection set (Ctrl toggle / Shift range / single)
            if (shift && currentFilteredFiles != null && currentFilteredFiles.Count > 0)
            {
                string anchorPath = selectionAnchorPath;
                if (string.IsNullOrEmpty(anchorPath)) anchorPath = selectedPath;
                if (string.IsNullOrEmpty(anchorPath)) anchorPath = file.Path;

                int anchorIndex = -1;
                int clickIndex = -1;
                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    var f = currentFilteredFiles[i];
                    if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                    if (anchorIndex < 0 && string.Equals(f.Path, anchorPath, StringComparison.OrdinalIgnoreCase)) anchorIndex = i;
                    if (clickIndex < 0 && string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase)) clickIndex = i;
                    if (anchorIndex >= 0 && clickIndex >= 0) break;
                }

                if (anchorIndex < 0) anchorIndex = clickIndex;
                if (clickIndex < 0) clickIndex = anchorIndex;

                if (anchorIndex >= 0 && clickIndex >= 0)
                {
                    int lo = Mathf.Min(anchorIndex, clickIndex);
                    int hi = Mathf.Max(anchorIndex, clickIndex);

                    if (!ctrl)
                    {
                        selectedFiles.Clear();
                        selectedFilePaths.Clear();
                        selectionChanged = true;
                    }

                    for (int i = lo; i <= hi; i++)
                    {
                        var f = currentFilteredFiles[i];
                        if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                        if (selectedFilePaths.Add(f.Path))
                        {
                            selectedFiles.Add(f);
                            selectionChanged = true;
                        }
                    }
                }
            }
            else if (ctrl)
            {
                if (selectedFilePaths.Contains(file.Path))
                {
                    selectedFilePaths.Remove(file.Path);
                    selectedFiles.RemoveAll(f => f != null && string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase));
                    selectionChanged = true;
                }
                else
                {
                    selectedFilePaths.Add(file.Path);
                    selectedFiles.Add(file);
                    selectionChanged = true;
                }
                selectionAnchorPath = file.Path;
            }
            else
            {
                if (!(selectedFiles.Count == 1 && selectedFilePaths.Contains(file.Path)))
                {
                    selectedFiles.Clear();
                    selectedFilePaths.Clear();
                    selectedFiles.Add(file);
                    selectedFilePaths.Add(file.Path);
                    selectionChanged = true;
                }
                selectionAnchorPath = file.Path;
            }

            // Keep primary selection path for double-click detection / hover path
            if (selectionChanged || !string.Equals(selectedPath, fileKey, StringComparison.OrdinalIgnoreCase))
            {
                if (selectionChanged && untaggedSelBefore != null)
                {
                    try
                    {
                        HashSet<string> deselected = BuildDeselectedSelectionKeys(untaggedSelBefore, SnapshotSelectionIdentityKeys(this));
                        if (deselected != null)
                            PruneUntaggedGridAfterSelectionChange(deselected);
                    }
                    catch { }
                }
                selectedPath = fileKey;
                // Selection should not "stick" the hover path.
                SetHoverPath("");
                RefreshSelectionVisuals();
                UpdatePaginationText();
            }
            else if (ItemApplyMode == ApplyMode.DoubleClick && !isDoubleClick)
            {
                return;
            }

            if (_benchPickModeActive)
            {
                BenchOnGallerySelectionChangedInPickMode();
                return;
            }

            // Apply Logic
            // Hold-to-launch overrides 1-click apply: clicks should still select, but only 2-click applies while hold mode is on.
            bool shouldApply = holdToLaunchEnabled
                ? (ItemApplyMode == ApplyMode.DoubleClick && isDoubleClick)
                : ((ItemApplyMode == ApplyMode.SingleClick) || (ItemApplyMode == ApplyMode.DoubleClick && isDoubleClick));
            
            if (shouldApply)
            {
                ApplyFileEntryNow(file);
            }
        }

        internal void ApplyFileFromHold(FileEntry file)
        {
            if (file == null) return;
            if (_benchPickModeActive) return;
            ApplyFileEntryNow(file);
        }

        private void ApplyFileEntryNow(FileEntry file)
        {
            if (file == null) return;
            if (_benchPickModeActive) return;

            FileEntry applyFile = file;
            FileEntry resolvedScene = TryResolveSceneCategoryPackageRowToSceneJson(file);
            if (resolvedScene != null)
                applyFile = resolvedScene;

            string pathLower = (applyFile.Path ?? "").ToLowerInvariant();
            // Exclude Scenes from auto-apply, but allow SubScenes
            bool isSubScene = pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\")
                || (!string.IsNullOrEmpty(currentCategoryTitle) && currentCategoryTitle.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0);
            bool isScene = !isSubScene && pathLower.EndsWith(".json")
                && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene")
                    || (!string.IsNullOrEmpty(currentCategoryTitle) && currentCategoryTitle.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0));

            if (!isScene)
            {
                if (TryOnInterceptApply(applyFile)) return;
                ExecuteAutoActionForFile(applyFile);
            }
            else
            {
                TryOnAbandonForSceneLoad();
                UI.LoadSceneFile(applyFile, this);
            }
        }

        /// <summary>
        /// In Scene categories, package-level rows use <see cref="VarFileEntry"/> with <c>meta.json</c> (Path = .var), so click apply
        /// must target a real scene JSON inside the zip — otherwise <see cref="ExecuteAutoActionForFile"/> treats the row as a bare .var and runs texture caching.
        /// </summary>
        private FileEntry TryResolveSceneCategoryPackageRowToSceneJson(FileEntry file)
        {
            if (file == null) return null;
            if (string.IsNullOrEmpty(currentCategoryTitle)) return null;
            if (currentCategoryTitle.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (currentCategoryTitle.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) < 0) return null;

            string pathNorm = (file.Path ?? "").Replace('\\', '/');
            string pathLower = pathNorm.ToLowerInvariant();
            if (pathLower.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;

            VarPackage pkg = null;
            if (file is VarFileEntry vfe && vfe.Package != null)
            {
                string ip = (vfe.InternalPath ?? "").Replace('\\', '/');
                if (string.Equals(ip, "meta.json", StringComparison.OrdinalIgnoreCase))
                    pkg = vfe.Package;
                else if (pathLower.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    pkg = vfe.Package;
            }
            else if (file is PackageListEntry ple && ple.Package != null && pathLower.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                pkg = ple.Package;

            if (pkg == null) return null;

            List<VarFileEntry> entries = pkg.FileEntries;
            if (entries == null || entries.Count == 0) return null;

            VarFileEntry best = null;
            for (int i = 0; i < entries.Count; i++)
            {
                VarFileEntry cand = entries[i];
                if (cand == null) continue;
                string ip = (cand.InternalPath ?? "").Replace('\\', '/');
                if (!ip.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                string ipLower = ip.ToLowerInvariant();
                if (ipLower.IndexOf("saves/scene", StringComparison.OrdinalIgnoreCase) < 0
                    && ipLower.IndexOf("/scene/", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (best == null || cand.LastWriteTime > best.LastWriteTime)
                    best = cand;
            }

            return best;
        }

        private string GetSelectionIdentityKey(FileEntry file, bool historyBrowse)
        {
            if (file == null) return "";
            if (historyBrowse)
            {
                if (!string.IsNullOrEmpty(file.Path)) return file.Path;
                return file.Uid ?? "";
            }
            return !string.IsNullOrEmpty(file.Path) ? file.Path : (file.Uid ?? "");
        }

        private string GetCurrentSelectionAnchorIdentityKey(bool historyBrowse)
        {
            if (!string.IsNullOrEmpty(selectionAnchorIdentityKey)) return selectionAnchorIdentityKey;
            if (!string.IsNullOrEmpty(selectionAnchorPath)) return selectionAnchorPath;
            if (selectedFiles != null && selectedFiles.Count > 0)
                return GetSelectionIdentityKey(selectedFiles[0], historyBrowse);
            if (!string.IsNullOrEmpty(selectedPath)) return selectedPath;
            return "";
        }

        private int FindIndexBySelectionIdentity(List<FileEntry> files, string key, bool historyBrowse)
        {
            if (files == null || string.IsNullOrEmpty(key)) return -1;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null) continue;
                string k = GetSelectionIdentityKey(f, historyBrowse);
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private void AddFileToSelection(FileEntry file, bool historyBrowse, HashSet<string> historySelectionKeys = null)
        {
            if (file == null) return;
            if (!historyBrowse)
            {
                string p = file.Path;
                if (string.IsNullOrEmpty(p)) p = file.Uid;
                if (string.IsNullOrEmpty(p)) return;
                if (selectedFilePaths.Add(p)) selectedFiles.Add(file);
                return;
            }
            string idKey = GetSelectionIdentityKey(file, true);
            if (historySelectionKeys != null)
            {
                if (string.IsNullOrEmpty(idKey) || historySelectionKeys.Contains(idKey)) return;
                historySelectionKeys.Add(idKey);
            }
            string addKey = !string.IsNullOrEmpty(file.Path) ? file.Path : (file.Uid ?? "");
            if (string.IsNullOrEmpty(addKey)) return;
            if (selectedFilePaths.Add(addKey)) selectedFiles.Add(file);
        }

        private void SetSelectionAnchor(FileEntry file, bool historyBrowse)
        {
            if (file == null) return;
            selectionAnchorPath = file.Path;
            selectionAnchorIdentityKey = GetSelectionIdentityKey(file, historyBrowse);
        }

        private void RefreshSelectionVisuals()
        {
            RefreshSelectionVisualsCore(runHeavySideEffects: true);
        }

        /// <summary>
        /// Grid selection chrome only. Heavy side effects (user-tags pane + toolbox/context menu)
        /// are optional — skip during detail-strip thumb scrub for scroll performance.
        /// </summary>
        private void RefreshSelectionVisualsCore(bool runHeavySideEffects)
        {
            // Walk recycled activeItems only — no Transform foreach / GetComponent storm.
            if (recyclingGrid != null)
            {
                int n = recyclingGrid.ActiveItemCount;
                for (int i = 0; i < n; i++)
                {
                    RecyclingGridItem rgvItem = recyclingGrid.GetActiveItemAt(i);
                    if (rgvItem == null) continue;
                    GameObject btn = rgvItem.gameObject;
                    if (btn == null || !btn.activeSelf) continue;

                    FileButtonBinder binder = rgvItem.binder;
                    if (binder == null) binder = FileButtonBinder.GetOrAdd(btn);

                    if (btn.name.StartsWith("FileButton_"))
                    {
                        UIDraggableItem diag = binder != null ? binder.draggable : null;
                        FileEntry feForVisuals = null;
                        try
                        {
                            if (settingsListViewActive && currentFilteredFiles != null
                                && rgvItem.index >= 0 && rgvItem.index < currentFilteredFiles.Count)
                                feForVisuals = currentFilteredFiles[rgvItem.index];
                            else if (diag != null) feForVisuals = diag.FileEntry;
                        }
                        catch { feForVisuals = diag != null ? diag.FileEntry : null; }
                        if (feForVisuals != null)
                            UpdateFileButtonVisuals(btn, feForVisuals);
                    }

                    RatingHandler ratingHandler = binder != null ? binder.ratingHandler : null;
                    if (ratingHandler != null) ratingHandler.CloseSelector();
                }
            }
            // Fallback for non-recycled items (if any legacy usage remains)
            else
            {
                for (int i = 0; activeButtons != null && i < activeButtons.Count; i++)
                {
                    GameObject btn = activeButtons[i];
                    if (btn == null) continue;

                    FileButtonBinder binder = FileButtonBinder.GetOrAdd(btn);
                    if (btn.name.StartsWith("FileButton_"))
                    {
                        UIDraggableItem diag = binder != null ? binder.draggable : null;
                        RecyclingGridItem rgvItem = binder != null ? binder.gridItem : null;
                        FileEntry feForVisuals = null;
                        try
                        {
                            if (settingsListViewActive && rgvItem != null && currentFilteredFiles != null
                                && rgvItem.index >= 0 && rgvItem.index < currentFilteredFiles.Count)
                                feForVisuals = currentFilteredFiles[rgvItem.index];
                            else if (diag != null) feForVisuals = diag.FileEntry;
                        }
                        catch { feForVisuals = diag != null ? diag.FileEntry : null; }
                        if (feForVisuals != null)
                            UpdateFileButtonVisuals(btn, feForVisuals);
                    }

                    RatingHandler ratingHandler = binder != null ? binder.ratingHandler : null;
                    if (ratingHandler != null) ratingHandler.CloseSelector();
                }
            }
            if (!runHeavySideEffects) return;
            // Keep toolbox grid-rate selector open during selection visual refresh.
            // Selector visibility is already managed by RefreshTboxGridRateControlState() (selection count / mode gating)
            // and by user interaction (ToggleSelector/SetRating). Auto-closing here makes it impossible to use in
            // some modes where RefreshSelectionVisuals is triggered frequently.
            try { RefreshAppliedUserTagsPaneAfterSelectionChange(); } catch { }
            // Immediate detail-strip / toolbox height sync (avoid waiting for the 250ms poll).
            try { UpdateSelectionContextMenu(); } catch { }
        }

        public bool NotifyPackagesChanged(DateTime refreshTime)
        {
            if (refreshTime <= DateTime.MinValue) refreshTime = DateTime.Now;
            if (refreshTime <= lastAppliedPackageRefreshTime) return false;

            // Folder moves keep the same UID set; Path side-panel counts must still rebuild.
            pathsCached = false;
            try { TryClearStalePackagePathFilter(); } catch { }

            // If content is already loaded, Gallery.AutoRefreshAfterPackageScan will apply
            // an incremental delta immediately. Do not arm refreshOnNextShow here, otherwise
            // a hide/open race can trigger a one-off full RefreshFiles() stall on Show().
            if (!hasLoadedContent || recyclingGrid == null || scrollRect == null)
            {
                refreshOnNextShow = true;
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
			    try { if (IsVisible) UpdateTabs(); } catch { }
            }
            return true;
        }

        /// <summary>When manual-refresh-only blocked FileManager observer, apply hub/download delta on next Show.</summary>
        private void TryApplyPendingPackageDeltaOnShow()
        {
            if (IsSettingsPanelOpen() || settingsListViewActive) return;
            if (!hasLoadedContent || recyclingGrid == null) return;
            bool hasPending = false;
            try { hasPending = FileManager.HasPendingGalleryPackageDelta(); } catch { }
            if (!hasPending) return;

            List<VarPackage> added = null;
            List<VarPackage> removed = null;
            try
            {
                added = new List<VarPackage>(FileManager.lastAddedPackages);
                removed = new List<VarPackage>(FileManager.lastRemovedPackages);
            }
            catch { return; }

            try
            {
                LogUtil.Log("[VPB.Gallery.Delta] TryApplyPendingPackageDeltaOnShow title='"
                    + (currentCategoryTitle ?? "") + "' added=" + (added != null ? added.Count : 0));
            }
            catch { }

            bool applied = false;
            try { applied = ApplyPackageDelta(added, removed); } catch { }
            if (applied)
            {
                try { FileManager.AckPackageGalleryDeltaConsumed(); } catch { }
            }
        }

        internal void OnGallerySqlIndexUpdated()
        {
            if (IsSettingsPanelOpen() || settingsListViewActive) return;
            if (!IsVisible && !hasLoadedContent) return;
            if (activeContentType != ContentType.Category && activeContentType != ContentType.History) return;

            bool scanning = false;
            try { scanning = FileManager.IsScanning; } catch { }
            if (scanning)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] OnGallerySqlIndexUpdated deferred (package scan in progress) title='"
                        + (currentCategoryTitle ?? "") + "'");
                }
                catch { }
                return;
            }

            DateTime refreshTime = DateTime.MinValue;
            try { refreshTime = FileManager.lastPackageRefreshTime; } catch { }

            if (lastPackageDeltaChangedGrid && refreshTime > DateTime.MinValue
                && refreshTime <= lastAppliedPackageRefreshTime)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] OnGallerySqlIndexUpdated SKIP (delta already applied) title='"
                        + (currentCategoryTitle ?? "") + "'");
                }
                catch { }
                return;
            }

            GalleryFileListSnapshotCache.InvalidateAll();

            List<VarPackage> added = null;
            List<VarPackage> removed = null;
            bool hasPackageDelta = false;
            try
            {
                added = new List<VarPackage>(FileManager.lastAddedPackages);
                removed = new List<VarPackage>(FileManager.lastRemovedPackages);
                hasPackageDelta = added.Count > 0 || removed.Count > 0;
            }
            catch { }

            if (hasPackageDelta)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] OnGallerySqlIndexUpdated ApplyPackageDelta title='"
                        + (currentCategoryTitle ?? "") + "' added=" + (added != null ? added.Count : 0));
                }
                catch { }
                bool applied = false;
                try { applied = ApplyPackageDelta(added, removed); } catch { }
                if (applied)
                {
                    try { FileManager.AckPackageGalleryDeltaConsumed(); } catch { }
                    return;
                }
            }

            try
            {
                LogUtil.Log("[VPB.Gallery.Delta] OnGallerySqlIndexUpdated RefreshFiles title='"
                    + (currentCategoryTitle ?? "") + "' deltaApplied=" + (lastPackageDeltaChangedGrid ? "1" : "0")
                    + " pendingAdded=" + (added != null ? added.Count : 0));
            }
            catch { }
            try { RefreshFiles(true, refreshDebugSource: "sql_index_updated"); } catch { }
        }
    }
}
