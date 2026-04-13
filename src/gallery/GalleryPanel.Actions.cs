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
                if (selected != null && selected.type == "Person") return selected;
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
                        try { if (a.type == "Person") return a; } catch { }
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

                    // Match the primary tab's first action behavior (auto action = first button).
                    if (pathLower.EndsWith(".var"))
                    {
                        try
                        {
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
                selectedHubItem = null;

                SetHoverPath(file);
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
                try { MVR.FileManagement.FileManager.Refresh(); } catch { }
                FileManager.Refresh(true, false, false);
                creatorsCached = false;
                categoriesCached = false;
                tagsCached = false;
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
            currentCategoryTitle = title;
            bool paramsChanged = (currentExtension != extension || currentPath != path);
            if (paramsChanged)
            {
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
                // currentCreator = ""; // Keep creator filter across categories
                activeTags.Clear();
                currentSceneSourceFilter = "";
                currentAppearanceSourceFilter = "";
            }
            else if (packagesChanged)
            {
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
            }

            bool sameViewReopen = hasLoadedContent && !paramsChanged;

            // Save scroll for the category we're leaving; prime the restore target for the new one.
            if (paramsChanged && hasLoadedContent && scrollRect != null)
            {
                categoryScrollPositions[_prevCategoryKey] = scrollRect.verticalNormalizedPosition;
                SaveCategoryScrollCache();
            }
            _pendingScrollRestore = categoryScrollPositions.TryGetValue(MakeCategoryScrollKey(title, path), out float _sp) ? _sp : 1f;

            currentExtension = extension;
            currentPath = path;
            
            // Set currentPaths
            currentPaths = null;
            if (categories != null) {
                var cat = categories.FirstOrDefault(c => c.path == path && c.name == title);
                if (!string.IsNullOrEmpty(cat.name)) currentPaths = cat.paths;
            }
            if (currentPaths == null) currentPaths = new List<string> { path };

            if (titleSearchInput != null) titleSearchInput.text = nameFilter;

            if (Application.isPlaying && canvas.renderMode == RenderMode.WorldSpace)
            {
                // In VR on startup, Camera.main can be null briefly. Rebind whenever it becomes available.
                if (Camera.main != null)
                    canvas.worldCamera = Camera.main;
            }

            // Decide refresh before UpdateLayout so we can avoid synchronous full-library cache scans
            // (CacheCreators / CacheCategoryCounts) when RefreshFilesRoutine will rebuild them on a worker thread.
            bool shouldRefresh = paramsChanged || !hasLoadedContent || packagesChanged;

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
            if (sameViewReopen && hasLoadedContent)
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

            // If we had to register late (startup ordering), refresh raycast state deterministically.
            if (!registeredBefore && _registeredWithSuperController)
            {
                try { StartCoroutine(RefreshRaycasterNextFrame()); } catch { }
            }

            if (shouldRefresh)
            {
                RefreshFiles(!paramsChanged);
                refreshOnNextShow = false;
                lastAppliedPackageRefreshTime = pkgRefreshTime;
                LogGalleryCategoryTypeNavPhase("Show_after_RefreshFiles_invoke");
            }
            else
                LogGalleryCategoryTypeNavPhase("Show_skip_RefreshFiles");

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
            bool gate = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible;
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
            SetHoverPath(file.Path);
        }

        public void SetHoverPath(string path)
        {
            bool hasPath = !string.IsNullOrEmpty(path);
            float targetAlpha = hasPath ? 1f : 0f;

            if (hoverFadeCoroutine != null) StopCoroutine(hoverFadeCoroutine);
            hoverFadeCoroutine = StartCoroutine(FadeHoverPath(targetAlpha));

            if (hoverPathText != null && hasPath)
            {
                string displayPath = path;

                // Ensure we show full internal paths for .var files without manual line breaks
                // Text wrapping is now handled by the UI Text component
                
                hoverPathText.text = displayPath.Replace("/", "/\u200B").Replace(":", ":\u200B");
            }
        }

        private IEnumerator FadeHoverPath(float targetAlpha)
        {
            if (hoverPathCanvasGroup == null) yield break;
            
            float duration = 0.15f; // Fast but smooth
            float startAlpha = hoverPathCanvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                hoverPathCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
                yield return null;
            }

            hoverPathCanvasGroup.alpha = targetAlpha;
            hoverFadeCoroutine = null;
        }

        public void RestoreSelectedHoverPath()
        {
            if (selectedFile != null) SetHoverPath(selectedFile);
            else SetHoverPath("");
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
                topSearchBaseFiles = new List<FileEntry>(currentFilteredFiles);

            if (nameFilterTerms == null || nameFilterTerms.Length == 0)
            {
                currentFilteredFiles.Clear();
                currentFilteredFiles.AddRange(topSearchBaseFiles);
                topSearchBaseFiles = null;
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
        }

        private void OnFileRightClick(FileEntry file)
        {
            if (file == null) return;

            // Right click selects if not selected.
            // Note: We intentionally do NOT open the actions panel here; right-click should not
            // force any bottom UI to appear (a separate context menu implementation will handle actions).
            if (!selectedFilePaths.Contains(file.Path))
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectedFiles.Add(file);
                selectedFilePaths.Add(file.Path);
                selectedPath = file.Path;
                selectedHubItem = null;
                selectionAnchorPath = file.Path;
                
                SetHoverPath(file);
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
            string fileKey = !string.IsNullOrEmpty(file.Path) ? file.Path : file.Uid;
            bool isDoubleClick = (time - lastClickTime < 0.3f && string.Equals(selectedPath, fileKey, StringComparison.OrdinalIgnoreCase));
            lastClickTime = time;

            bool selectionChanged = false;

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
                selectedPath = fileKey;
                selectedHubItem = null;
                SetHoverPath(file);
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
			    try { if (IsVisible) UpdateTabs(); } catch { }
            }
            return true;
        }
    }
}
