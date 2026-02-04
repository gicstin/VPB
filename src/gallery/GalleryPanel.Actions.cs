using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void LoadRandom()
        {
            try
            {
                if (lastFilteredFiles == null || lastFilteredFiles.Count == 0)
                {
                    LogUtil.LogWarning("[VPB] Load Random: no items available.");
                    return;
                }

                int idx = UnityEngine.Random.Range(0, lastFilteredFiles.Count);
                FileEntry file = lastFilteredFiles[idx];
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
                actionsPanel?.HandleSelectionChanged(selectedFiles, selectedHubItem);

                // Apply (same logic as click)
                string pathLower = (file.Path ?? "").ToLowerInvariant();
                bool isSubScene = pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || (currentCategoryTitle != null && currentCategoryTitle.Contains("SubScene"));
                bool isScene = !isSubScene && pathLower.EndsWith(".json") && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene") || (currentCategoryTitle != null && currentCategoryTitle.Contains("Scene")));

                if (isScene)
                {
                    UI.LoadSceneFile(file);
                    return;
                }

                if (actionsPanel != null)
                {
                    bool success = actionsPanel.ExecuteAutoAction();
                    if (!success)
                    {
                        LogUtil.LogWarning("[VPB] Load Random: no auto action available for this item.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Load Random exception: " + ex);
            }
        }

        public void Show(string title, string extension, string path)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (canvas == null) Init();

            DateTime pkgRefreshTime = DateTime.MinValue;
            try { pkgRefreshTime = FileManager.lastPackageRefreshTime; } catch { }
            bool packagesChanged = refreshOnNextShow || (pkgRefreshTime > lastAppliedPackageRefreshTime);

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
                currentPage = 0;
            }
            else if (packagesChanged)
            {
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
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

            if (titleSearchInput != null) titleSearchInput.text = nameFilter;

            if (Application.isPlaying && canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = Camera.main;
            }
            
            UpdateSideButtonsVisibility();
            UpdateLayout();
            RefreshTargetDropdown();

            canvas.gameObject.SetActive(true);
            
            // Only refresh if params changed OR if we are empty (first run) OR explicit refresh needed
            if (paramsChanged || activeButtons.Count == 0 || packagesChanged)
            {
                RefreshFiles(!paramsChanged);
                refreshOnNextShow = false;
                lastAppliedPackageRefreshTime = pkgRefreshTime;
            }
            
            UpdateTabs();

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
        }

        public void Hide()
        {
            if (canvas != null)
                canvas.gameObject.SetActive(false);
            
            hoverCount = 0;
            actionsPanel?.Hide();
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
            currentPage = 0;
            RefreshFiles();
        }

        private void OnFileRightClick(FileEntry file)
        {
            if (file == null) return;

            // Right click selects if not selected, and opens actions panel
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
                actionsPanel?.HandleSelectionChanged(selectedFiles, selectedHubItem);
            }

            if (isFixedLocally && VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedHeightMode == 0)
            {
                VPBConfig.Instance.DesktopFixedHeightMode = 1; // Custom height
                UpdateFooterHeightState();
                UpdateLayout();
            }

            if (actionsPanel != null)
            {
                actionsPanel.Open();
                actionsPanel.Show();
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
            if (shift && lastPageFiles != null && lastPageFiles.Count > 0)
            {
                string anchorPath = selectionAnchorPath;
                if (string.IsNullOrEmpty(anchorPath)) anchorPath = selectedPath;
                if (string.IsNullOrEmpty(anchorPath)) anchorPath = file.Path;

                int anchorIndex = -1;
                int clickIndex = -1;
                for (int i = 0; i < lastPageFiles.Count; i++)
                {
                    var f = lastPageFiles[i];
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
                        var f = lastPageFiles[i];
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
                actionsPanel?.HandleSelectionChanged(selectedFiles, selectedHubItem);
            }
            else if (ItemApplyMode == ApplyMode.DoubleClick && !isDoubleClick)
            {
                return;
            }

            // Apply Logic
            bool shouldApply = (ItemApplyMode == ApplyMode.SingleClick) || (ItemApplyMode == ApplyMode.DoubleClick && isDoubleClick);
            
            if (shouldApply)
            {
                string pathLower = file.Path.ToLowerInvariant();
                // Exclude Scenes from auto-apply, but allow SubScenes
                bool isSubScene = pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || currentCategoryTitle.Contains("SubScene");
                bool isScene = !isSubScene && pathLower.EndsWith(".json") && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene") || currentCategoryTitle.Contains("Scene"));
                
                if (!isScene && actionsPanel != null)
                {
                    bool success = actionsPanel.ExecuteAutoAction();
                    if (!success)
                    {
                        // actionsPanel.Open();
                    }
                }
                else if (isScene)
                {
                    UI.LoadSceneFile(file);
                }
            }
        }

        private void RefreshSelectionVisuals()
        {
            foreach (var btn in activeButtons)
            {
                if (btn == null) continue;
                
                if (btn.name.StartsWith("FileButton_"))
                {
                    var diag = btn.GetComponent<UIDraggableItem>();
                    if (diag != null && diag.FileEntry != null)
                    {
                        BindFileButton(btn, diag.FileEntry);
                    }
                }
                
                var ratingHandler = btn.GetComponent<RatingHandler>();
                if (ratingHandler != null) ratingHandler.CloseSelector();
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

            refreshOnNextShow = true;
            creatorsCached = false;
            tagsCached = false;
            categoriesCached = false;
			try { if (IsVisible) UpdateTabs(); } catch { }
            return true;
        }
    }
}
