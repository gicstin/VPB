using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        public void Show(string title, string extension, string path)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (canvas == null) Init();

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
                currentPage = 0;
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
            LogUtil.Log("GalleryPanel Show setup took: " + sw.ElapsedMilliseconds + "ms");
            
            // Only refresh if params changed OR if we are empty (first run) OR explicit refresh needed
            if (paramsChanged || activeButtons.Count == 0)
            {
                RefreshFiles();
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
            actionsPanel?.Hide();
        }

        public void SetHoverPath(string path)
        {
            if (hoverPathText != null)
            {
                // Intelligent wrapping for paths: add zero-width space after separators
                if (string.IsNullOrEmpty(path))
                {
                    hoverPathText.text = "";
                }
                else
                {
                    string displayPath = path;
                    // Always split when entering inside a .var package
                    if (displayPath.Contains(".var:/"))
                    {
                        displayPath = displayPath.Replace(".var:/", ".var\n\\");
                    }
                    else if (displayPath.Contains(".var:"))
                    {
                        displayPath = displayPath.Replace(".var:", ".var\n\\");
                    }
                    hoverPathText.text = displayPath.Replace("/", "/\u200B").Replace(":", ":\u200B");
                }
            }
        }

        public void RestoreSelectedHoverPath()
        {
            if (selectedFile != null) SetHoverPath(selectedFile.Path);
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

        private void OnFileClick(FileEntry file)
        {
            if (file == null) return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
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
            bool isDoubleClick = (time - lastClickTime < 0.3f && selectedPath == file.Path);
            lastClickTime = time;

            if (selectedPath != file.Path)
            {
                // Deselect old
                foreach (var btn in activeButtons)
                {
                    if (btn == null || !btn.name.StartsWith("FileButton_")) continue;
                    Image img = btn.GetComponent<Image>();
                    if (img != null) img.color = Color.gray;
                }
                
                selectedPath = file.Path;
                selectedFile = file;
                selectedHubItem = null;

                SetHoverPath(selectedFile.Path);
                
                // Select new
                if (fileButtonImages.ContainsKey(selectedPath))
                {
                    if (fileButtonImages[selectedPath] != null)
                        fileButtonImages[selectedPath].color = Color.yellow;
                }

                actionsPanel?.HandleSelectionChanged(selectedFile, selectedHubItem);
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
                // Exclude Scenes from auto-apply
                bool isScene = pathLower.EndsWith(".json") && (pathLower.Contains("/scenes/") || pathLower.Contains("\\scenes\\"));
                
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
                    // actionsPanel?.Open();
                }
            }
        }

        public void ToggleSettings(bool onRight)
        {
            if (settingsPanel != null) settingsPanel.Toggle(onRight);
        }
    }
}
