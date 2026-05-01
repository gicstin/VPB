using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

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

        private bool ExecuteAutoActionForFile(FileEntry file, Hub.GalleryHubItem hubItem = null)
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
                    dragger.HubItem = hubItem;
                    dragger.Panel = this;

                    string pathLower = (file.Path ?? "").ToLowerInvariant();
                    string category = CurrentCategoryTitle ?? "";
                    string categoryLower = category.ToLowerInvariant();

                    // Match the primary tab's first action behavior (auto action = first button).
                    if (pathLower.EndsWith(".var"))
                    {
                        try
                        {
                            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(file), "package"); } catch { }
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

                    bool isPluginPreset =
                        pathLower.Contains("/custom/atom/person/plugins/") ||
                        pathLower.Contains("\\custom\\atom\\person\\plugins\\") ||
                        (pathLower.EndsWith(".vap") && (categoryLower.Contains("person plugins") || categoryLower.Contains("plugin preset")));
                    if (isPluginPreset)
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) { LogUtil.LogWarning("[VPB] Please select a Person atom."); return false; }
                        dragger.LoadPlugins(target);
                        return true;
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
                selectionAnchorIdentityKey = null;

                selectedFiles.Add(file);
                if (!string.IsNullOrEmpty(file.Path)) selectedFilePaths.Add(file.Path);
                selectedPath = file.Path;
                selectedHubItem = null;

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
        /// </summary>
        public void UserRequestedPackageRefresh()
        {
            try
            {
                if (!IsHubMode)
                    ShowTemporaryStatus(VPBTranslation.T("gallery.status.refreshing_packages", "Refreshing packages..."), 1.5f);

                if (cleanupModeActive)
                {
                    RebuildCleanupCandidates(true, true);
                    return;
                }

                try { MVR.FileManagement.FileManager.Refresh(); } catch { }
                FileManager.Refresh(true, false, false);
                GalleryFileListSnapshotCache.Clear();
                GalleryTagCountSnapshotCache.Clear();
                creatorsCached = false;
                categoriesCached = false;
                tagsCached = false;
                pathsCached = false;
                refreshOnNextShow = true;
                RefreshFiles(true);
                refreshOnNextShow = false;
                try { lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime; } catch { }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Refresh packages failed: " + ex);
                ShowTemporaryStatus(VPBTranslation.T("gallery.status.refresh_failed", "Refresh failed. See log."), 2f);
            }
        }

        public void Show(string title, string extension, string path)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool needsInit = canvas == null;
            LogUtil.Log("[Gallery] GalleryPanel.Show entry: title='" + title + "' path='" + path + "' needsInit=" + needsInit + " currentPath='" + currentPath + "' hasLoadedContent=" + hasLoadedContent);
            _userHidden = false;
            if (needsInit) Init();
            LogUtil.Log("[Gallery] GalleryPanel.Show post-init: " + sw.ElapsedMilliseconds + "ms");

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
            if (paramsChanged)
            {
                // Save current category's filters before switching away
                if (hasLoadedContent)
                    SaveCurrentCategoryFilterState(currentCategoryTitle, currentPath);

                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
                pathsCached = false;
            }
            else if (packagesChanged)
            {
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
                pathsCached = false;
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
                // Category switch: restore only categories we've visited in this runtime session.
                // First visit in this run still starts at top, avoiding stale positions from previous launches.
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
                }
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
                    LogUtil.Log("[VPB] GalleryPanel.Show: deferred initial RefreshFiles until startup ready");
                LogGalleryCategoryTypeNavPhase("Show_skip_RefreshFiles");
            }

            // Same-view reopen: keep the existing side-tab/button tree and avoid synchronous count rebuilds.
            // Full refresh path: keep UI lightweight while RefreshFilesRoutine rebuilds caches in the background.
            if (sameViewReopen || refreshCoroutine != null)
                UpdateTabsImpl(rebuildSideTabLists: false);
            else
                UpdateTabs();
            LogGalleryCategoryTypeNavPhase("Show_after_UpdateTabs");
            UpdateLayout(!sameViewReopen && refreshCoroutine == null);
            LogGalleryCategoryTypeNavPhase("Show_after_UpdateLayout_2");

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

            deferredStartupRefreshCoroutine = null;
            if (hasLoadedContent) yield break;
            if (canvas == null || !canvas.enabled) yield break;

            try
            {
                RefreshFiles(false);
            }
            catch { }
        }

        public void Hide()
        {
            _userHidden = true;
            _hiddenByMenuGate = false;
            SetCanvasVisible(false);

            hoverCount = 0;
        }

        private void SetCanvasVisible(bool visible)
        {
            if (canvas == null) return;

            canvas.enabled = visible;

            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = visible;
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
            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }
            
            // The anchor-based gate only applies to the specific panel that is anchored.
            bool isAnchoredInstance = (GetAnchoredInstance() == this);
            
            bool gate = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible || (VPBConfig.Instance.GalleryAnchorToVamMenu && isVR && isAnchoredInstance);
            bool menuVisible = IsVamMenuVisible();

            if (!gate)
            {
                if (_hiddenByMenuGate && !_userHidden)
                {
                    SetCanvasVisible(true);
                    _hiddenByMenuGate = false;
                }
                return;
            }

            if (!menuVisible)
            {
                if (canvas.enabled)
                {
                    SetCanvasVisible(false);
                    _hiddenByMenuGate = true;
                }
            }
            else
            {
                if (_hiddenByMenuGate && !_userHidden)
                {
                    SetCanvasVisible(true);
                    _hiddenByMenuGate = false;
                }
            }
        }

        private void ApplyVamMenuAnchoring()
        {
            if (VPBConfig.Instance == null || canvas == null) return;
            
            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }
            
            if (!isVR) return;
            if (!VPBConfig.Instance.GalleryAnchorToVamMenu) return;

            // Priority check: only the first visible panel gets anchored.
            if (GetAnchoredInstance() != this) return;

            // If we are the priority panel, check if menu is visible for snapping.
            if (!IsVamMenuVisible()) return;

            Transform vamMenuTrans = SuperController.singleton.mainHUD.transform;
            if (vamMenuTrans == null) return;

            Vector3 localOffset = VPBConfig.Instance.GalleryAnchorOffset;
            
            RectTransform canvasRT = canvas.GetComponent<RectTransform>();
            float galleryHalfHeight = (canvasRT.rect.height * 0.5f) * canvasRT.lossyScale.y;

            // Anchor the gallery such that its bottom matches the localOffset relative to the VAM menu top.
            // In VaM, mainHUD has its own scale and rotation.
            Vector3 targetBottomPos = vamMenuTrans.TransformPoint(localOffset);
            Vector3 targetPos = targetBottomPos + vamMenuTrans.up * galleryHalfHeight;

            canvas.transform.position = targetPos;
            
            // Follow the VaM menu rotation with a 180-degree flip to face the user
            canvas.transform.rotation = vamMenuTrans.rotation * Quaternion.Euler(0, 180, 0);

            // Keep offsets reset so follow mode captures the anchored position when anchoring ends
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
                File.WriteAllText(p, root.ToString());
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
            if (IsHubMode) return;
            hoverPathText.text = GetFilteredVisibleItemsCountText();
        }

        public void SetHoverPath(string path)
        {
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
            nameFilter = f;
            nameFilterLower = string.IsNullOrEmpty(f) ? "" : f.ToLowerInvariant();
            nameFilterTerms = SplitSearchTerms(f);

            // In package filter mode, keep search scoped to the current filtered list
            // (do not refresh the whole gallery, which would clear filter mode).
            if (IsFilterActive)
            {
                ApplySearchWithinFilter(f);
                return;
            }

            // Outside filter mode: perform top search in-memory so clearing search can instantly
            // restore the full list without a rebuild (prevents stalls).
            if (topSearchBaseFiles == null)
            {
                if (!_topSearchBaseIsClean)
                {
                    // currentFilteredFiles may already be filtered (e.g. restored from per-category
                    // memory after a SQL-filtered RefreshFiles). The unfiltered base is unknown.
                    if (nameFilterTerms == null || nameFilterTerms.Length == 0)
                    {
                        // Clearing search — rebuild from scratch to get the full unfiltered list.
                        RefreshFiles();
                        return;
                    }
                    // Narrowing search — RefreshFiles will apply nameFilterTerms via SQL.
                    RefreshFiles();
                    return;
                }
                topSearchBaseFiles = new List<FileEntry>(currentFilteredFiles);
            }

            if (nameFilterTerms == null || nameFilterTerms.Length == 0)
            {
                currentFilteredFiles.Clear();
                currentFilteredFiles.AddRange(topSearchBaseFiles);
                topSearchBaseFiles = null;
                _topSearchBaseIsClean = true;
            }
            else
            {
                // Fast path for package list rows (dependency filters): query SQLite for matching packages,
                // then rebuild results in the same order as the base list.
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

                if (isPackageList)
                {
                    var allowedUids = new List<string>(topSearchBaseFiles.Count);
                    for (int i = 0; i < topSearchBaseFiles.Count; i++)
                    {
                        var e = topSearchBaseFiles[i];
                        if (e == null) continue;
                        // PackageListEntry.Name is "<uid>.var" for both live and indexed rows.
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
                        gotSql = VpbLocalDatabase.TryQueryPackageRowsForUidsWithAllTerms(allowedUids, nameFilterTerms, pkgRows);
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
                            if (!byUid.TryGetValue(uid, out var r)) continue;
                            DateTime wt = DateTime.MinValue;
                            if (r.LastWriteTicksOrInvalid != long.MinValue)
                            {
                                try { wt = DateTime.FromBinary(r.LastWriteTicksOrInvalid); } catch { wt = DateTime.MinValue; }
                            }
                            currentFilteredFiles.Add(new PackageListEntry(r.PackageUid, r.VarPath, wt, r.PackageSizeOrInvalid, r.PackageCreationTicksOrInvalid));
                        }
                    }
                    else
                    {
                        // Fallback: tokenized in-memory filter.
                        var filtered = new List<FileEntry>();
                        for (int i = 0; i < topSearchBaseFiles.Count; i++)
                        {
                            var e = topSearchBaseFiles[i];
                            if (e == null) continue;
                            string p = null;
                            string n = null;
                            try { p = e.Path; } catch { p = null; }
                            try { n = e.Name; } catch { n = null; }
                            if (MatchesAllTermsInEither(p, n, nameFilterTerms))
                                filtered.Add(e);
                        }
                        currentFilteredFiles.Clear();
                        currentFilteredFiles.AddRange(filtered);
                    }
                }
                else
                {
                    // Default: tokenized in-memory search (AND semantics across terms).
                    var filtered = new List<FileEntry>();
                    for (int i = 0; i < topSearchBaseFiles.Count; i++)
                    {
                        var e = topSearchBaseFiles[i];
                        if (e == null) continue;
                        string p = null;
                        string n = null;
                        try { p = e.Path; } catch { p = null; }
                        try { n = e.Name; } catch { n = null; }
                        if (MatchesAllTermsInEither(p, n, nameFilterTerms))
                            filtered.Add(e);
                    }
                    currentFilteredFiles.Clear();
                    currentFilteredFiles.AddRange(filtered);
                }
            }

            if (recyclingGrid != null)
            {
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                // Search should start at the top of results; otherwise the previous scroll position
                // can clamp to the bottom when the filtered list is shorter.
                ScrollGalleryToTop();
                recyclingGrid.Refresh();
            }
            try { UpdatePaginationText(); } catch { }

            // Refresh creator side tab if open so it shows only creators applicable to search results.
            bool creatorTabOpen = (leftActiveContent.HasValue && leftActiveContent.Value == ContentType.Creator)
                               || (rightActiveContent.HasValue && rightActiveContent.Value == ContentType.Creator);
            if (creatorTabOpen)
                try { UpdateTabsImpl(rebuildSideTabLists: false); } catch { }
        }

        private void OnFileRightClick(FileEntry file)
        {
            if (file == null) return;

            // Right click selects if not selected.
            // Note: We intentionally do NOT open the actions panel here; right-click should not
            // force any bottom UI to appear (a separate context menu implementation will handle actions).
            bool historyBrowse = !IsHubMode && activeContentType == ContentType.History;
            if (!IsFileSelected(file, historyBrowse))
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                AddFileToSelection(file, historyBrowse);
                selectedPath = historyBrowse ? GetSelectionIdentityKey(file, true) : file.Path;
                selectedHubItem = null;
                SetSelectionAnchor(file, historyBrowse);
                
                // Selection should not "stick" the hover path.
                SetHoverPath("");
                RefreshSelectionVisuals();
                UpdatePaginationText();
            }

            if (isFixedLocally && VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedHeightMode == 0)
            {
                VPBConfig.Instance.DesktopFixedHeightMode = 1; // Custom height
                UpdateFooterHeightState();
                UpdateLayout();
            }
        }

        private string GetSelectionIdentityKey(FileEntry file, bool historyBrowse)
        {
            if (file == null) return null;

            if (historyBrowse)
            {
                if (file is VarFileEntry vf && !string.IsNullOrEmpty(vf.GalleryItemUsageKey))
                    return "hist:" + vf.GalleryItemUsageKey;

                try
                {
                    string usageKey = VpbLocalDatabase.BuildUsageKey(file);
                    if (!string.IsNullOrEmpty(usageKey))
                        return "hist:" + usageKey;
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(file.Path))
                return "path:" + file.Path;
            if (!string.IsNullOrEmpty(file.Uid))
                return "uid:" + file.Uid;
            return null;
        }

        private string GetCurrentSelectionAnchorIdentityKey(bool historyBrowse)
        {
            if (!historyBrowse) return selectionAnchorPath;
            if (!string.IsNullOrEmpty(selectionAnchorIdentityKey)) return selectionAnchorIdentityKey;
            if (!string.IsNullOrEmpty(selectionAnchorPath)) return selectionAnchorPath;
            if (!string.IsNullOrEmpty(selectedPath)) return selectedPath;
            return null;
        }

        private void SetSelectionAnchor(FileEntry file, bool historyBrowse)
        {
            if (file == null)
            {
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                return;
            }

            selectionAnchorPath = file.Path;
            selectionAnchorIdentityKey = historyBrowse ? GetSelectionIdentityKey(file, true) : selectionAnchorPath;
        }

        private int FindIndexBySelectionIdentity(List<FileEntry> files, string identityKey, bool historyBrowse)
        {
            if (files == null || files.Count == 0 || string.IsNullOrEmpty(identityKey)) return -1;
            for (int i = 0; i < files.Count; i++)
            {
                string key = GetSelectionIdentityKey(files[i], historyBrowse);
                if (string.Equals(key, identityKey, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private bool AddFileToSelection(FileEntry file, bool historyBrowse, HashSet<string> historySelectionKeys = null)
        {
            if (file == null) return false;

            if (!historyBrowse)
            {
                if (string.IsNullOrEmpty(file.Path)) return false;
                if (selectedFilePaths.Add(file.Path))
                {
                    selectedFiles.Add(file);
                    return true;
                }
                return false;
            }

            if (!string.IsNullOrEmpty(file.Path))
                selectedFilePaths.Add(file.Path); // Keep path-highlighting behavior for history rows.

            if (historySelectionKeys == null)
                historySelectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string key = GetSelectionIdentityKey(file, true);
            if (string.IsNullOrEmpty(key))
            {
                selectedFiles.Add(file);
                return true;
            }

            if (historySelectionKeys.Add(key))
            {
                selectedFiles.Add(file);
                return true;
            }
            return false;
        }

        private bool IsFileSelected(FileEntry file, bool historyBrowse)
        {
            if (file == null) return false;
            if (!historyBrowse)
                return !string.IsNullOrEmpty(file.Path) && selectedFilePaths.Contains(file.Path);

            string key = GetSelectionIdentityKey(file, true);
            if (string.IsNullOrEmpty(key))
                return selectedFiles.Contains(file);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                if (string.Equals(GetSelectionIdentityKey(selectedFiles[i], true), key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private bool RemoveFileFromSelection(FileEntry file, bool historyBrowse)
        {
            if (file == null) return false;

            if (!historyBrowse)
            {
                if (string.IsNullOrEmpty(file.Path) || !selectedFilePaths.Contains(file.Path))
                    return false;
                selectedFilePaths.Remove(file.Path);
                selectedFiles.RemoveAll(f => f != null && string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase));
                return true;
            }

            string key = GetSelectionIdentityKey(file, true);
            bool removed = false;
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = selectedFiles.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(GetSelectionIdentityKey(selectedFiles[i], true), key, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedFiles.RemoveAt(i);
                        removed = true;
                    }
                }
            }
            else
            {
                removed = selectedFiles.Remove(file);
            }

            if (removed && !string.IsNullOrEmpty(file.Path))
            {
                bool hasSamePath = false;
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    if (selectedFiles[i] != null && string.Equals(selectedFiles[i].Path, file.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        hasSamePath = true;
                        break;
                    }
                }
                if (!hasSamePath) selectedFilePaths.Remove(file.Path);
            }
            return removed;
        }

        private void PruneSelectionToCurrentFilteredView(bool historyBrowse)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0)
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                selectedPath = null;
                selectedHubItem = null;
                return;
            }

            var visibleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < currentFilteredFiles.Count; i++)
            {
                string key = GetSelectionIdentityKey(currentFilteredFiles[i], historyBrowse);
                if (!string.IsNullOrEmpty(key)) visibleKeys.Add(key);
            }

            for (int i = selectedFiles.Count - 1; i >= 0; i--)
            {
                string selectedKey = GetSelectionIdentityKey(selectedFiles[i], historyBrowse);
                if (string.IsNullOrEmpty(selectedKey) || !visibleKeys.Contains(selectedKey))
                    selectedFiles.RemoveAt(i);
            }

            selectedFilePaths.Clear();
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                var f = selectedFiles[i];
                if (f != null && !string.IsNullOrEmpty(f.Path))
                    selectedFilePaths.Add(f.Path);
            }

            if (selectedFiles.Count == 0)
            {
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                selectedPath = null;
                selectedHubItem = null;
            }
            else
            {
                selectedPath = historyBrowse ? GetSelectionIdentityKey(selectedFiles[0], true) : selectedFiles[0].Path;
                SetSelectionAnchor(selectedFiles[0], historyBrowse);
            }
        }

        private void OnFileClick(FileEntry file)
        {
            if (file == null) return;

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

            float time = Time.realtimeSinceStartup;
            bool historyBrowse = !IsHubMode && activeContentType == ContentType.History;
            string fileKey = historyBrowse
                ? GetSelectionIdentityKey(file, true)
                : (!string.IsNullOrEmpty(file.Path) ? file.Path : file.Uid);
            bool isDoubleClick = (time - lastClickTime < 0.3f && string.Equals(selectedPath, fileKey, StringComparison.OrdinalIgnoreCase));
            lastClickTime = time;

            bool selectionChanged = false;

            // Update selection set (Ctrl toggle / Shift range / single)
            if (shift && currentFilteredFiles != null && currentFilteredFiles.Count > 0)
            {
                string anchorKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse);
                if (string.IsNullOrEmpty(anchorKey))
                    anchorKey = GetSelectionIdentityKey(file, historyBrowse);

                int anchorIndex = -1;
                int clickIndex = -1;
                anchorIndex = FindIndexBySelectionIdentity(currentFilteredFiles, anchorKey, historyBrowse);
                clickIndex = FindIndexBySelectionIdentity(currentFilteredFiles, GetSelectionIdentityKey(file, historyBrowse), historyBrowse);

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
                        if (AddFileToSelection(f, historyBrowse, historySelectionKeys)) selectionChanged = true;
                    }
                }
            }
            else if (ctrl)
            {
                if (IsFileSelected(file, historyBrowse))
                    selectionChanged = RemoveFileFromSelection(file, historyBrowse);
                else
                    selectionChanged = AddFileToSelection(file, historyBrowse);
                SetSelectionAnchor(file, historyBrowse);
            }
            else
            {
                bool alreadySingleSelected = selectedFiles.Count == 1 && IsFileSelected(file, historyBrowse);
                if (!alreadySingleSelected)
                {
                    selectedFiles.Clear();
                    selectedFilePaths.Clear();
                    selectionChanged = AddFileToSelection(file, historyBrowse);
                }
                SetSelectionAnchor(file, historyBrowse);
            }

            // Keep primary selection path for double-click detection / hover path
            if (selectionChanged || !string.Equals(selectedPath, fileKey, StringComparison.OrdinalIgnoreCase))
            {
                selectedPath = fileKey;
                selectedHubItem = null;
                // Selection should not "stick" the hover path.
                SetHoverPath("");
                RefreshSelectionVisuals();
                UpdatePaginationText();
            }
            else if (ItemApplyMode == ApplyMode.DoubleClick && !isDoubleClick)
            {
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
            ApplyFileEntryNow(file);
        }

        private void ApplyFileEntryNow(FileEntry file)
        {
            if (file == null) return;

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
                ExecuteAutoActionForFile(applyFile);
            }
            else
            {
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

        private void RefreshSelectionVisuals()
        {
            // Iterate over active buttons in the recycling grid content
            if (recyclingGrid != null && recyclingGrid.content != null)
            {
                foreach (Transform child in recyclingGrid.content)
                {
                    if (!child.gameObject.activeSelf) continue;
                    GameObject btn = child.gameObject;
                    
                    if (btn.name.StartsWith("FileButton_"))
                    {
                        var diag = btn.GetComponent<UIDraggableItem>();
                        if (diag != null && diag.FileEntry != null)
                        {
                            UpdateFileButtonVisuals(btn, diag.FileEntry);
                        }
                    }
                    
                    var ratingHandler = btn.GetComponent<RatingHandler>();
                    if (ratingHandler != null) ratingHandler.CloseSelector();
                }
            }
            // Fallback for non-recycled items (if any legacy usage remains)
            else 
            {
                foreach (var btn in activeButtons)
                {
                    if (btn == null) continue;
                    
                    if (btn.name.StartsWith("FileButton_"))
                    {
                        var diag = btn.GetComponent<UIDraggableItem>();
                        if (diag != null && diag.FileEntry != null)
                        {
                            UpdateFileButtonVisuals(btn, diag.FileEntry);
                        }
                    }
                    
                    var ratingHandler = btn.GetComponent<RatingHandler>();
                    if (ratingHandler != null) ratingHandler.CloseSelector();
                }
            }
        }

        public void ToggleSettings(bool onRight)
        {
            if (settingsPanel != null) settingsPanel.Toggle(onRight);
        }

        public bool NotifyPackagesChanged(DateTime refreshTime)
        {
            if (refreshTime <= DateTime.MinValue) refreshTime = DateTime.Now;
            if (refreshTime <= lastAppliedPackageRefreshTime) return false;

            // If content is already loaded, Gallery.AutoRefreshAfterPackageScan will apply
            // an incremental delta immediately. Do not arm refreshOnNextShow here, otherwise
            // a hide/open race can trigger a one-off full RefreshFiles() stall on Show().
            if (!hasLoadedContent || recyclingGrid == null || scrollRect == null)
            {
                refreshOnNextShow = true;
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
                pathsCached = false;
			    try { if (IsVisible) UpdateTabs(); } catch { }
            }
            return true;
        }
    }
}
