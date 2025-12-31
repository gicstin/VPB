using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace var_browser
{
    public class GalleryPanel : MonoBehaviour
    {
        public Canvas canvas;
        private GameObject backgroundBoxGO;
        private GameObject contentGO;
        private GameObject tabContainerGO;
        private ScrollRect scrollRect;
        private Text titleText;

        public List<Gallery.Category> categories = new List<Gallery.Category>();
        // private List<FileEntry> currentFiles = new List<FileEntry>(); // Unused

        private List<GameObject> activeButtons = new List<GameObject>();
        private List<GameObject> activeTabButtons = new List<GameObject>();

        private string currentPath = "";
        private string currentExtension = "json";
        
        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;
        
        // Configuration
        public bool IsUndocked = false;
        public Gallery.Category? UndockedCategory;
        private bool hasBeenPositioned = false;

        void OnDestroy()
        {
            if (canvas != null && SuperController.singleton != null)
            {
                SuperController.singleton.RemoveCanvas(canvas);
            }
            // Remove from manager if needed
            if (Gallery.singleton != null)
            {
                Gallery.singleton.RemovePanel(this);
            }
        }

        public void Init(bool isUndocked = false)
        {
            if (canvas != null) return;

            IsUndocked = isUndocked;
            string nameSuffix = isUndocked ? "_Undocked" : "";
            GameObject canvasGO = new GameObject("VPB_GalleryCanvas" + nameSuffix);
            canvas = canvasGO.AddComponent<Canvas>();
            RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(1200, 800);
            canvasGO.AddComponent<GraphicRaycaster>();

            if (SuperController.singleton != null)
                SuperController.singleton.AddCanvas(canvas);

            if (Application.isPlaying)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Camera.main;
                canvas.sortingOrder = 0;
                // Position will be set in Show()
                canvas.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                canvasGO.layer = 5; // UI layer
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4;

            // Background
            backgroundBoxGO = UI.AddChildGOImage(canvasGO, new Color(0.1f, 0.1f, 0.1f, 0.9f), AnchorPresets.centre, 1200, 800, Vector2.zero);
            
            UIDraggable dragger = backgroundBoxGO.AddComponent<UIDraggable>();
            dragger.target = canvasGO.transform;

            // Title
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(backgroundBoxGO.transform, false);
            titleText = titleGO.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 30;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.UpperCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -10);
            titleRT.sizeDelta = new Vector2(0, 40);

            // Tab Area - Only create if not undocked
            if (!IsUndocked)
            {
                tabContainerGO = new GameObject("Tabs");
                tabContainerGO.transform.SetParent(backgroundBoxGO.transform, false);
                RectTransform tabRT = tabContainerGO.AddComponent<RectTransform>();
                tabRT.anchorMin = new Vector2(0, 1);
                tabRT.anchorMax = new Vector2(1, 1);
                tabRT.pivot = new Vector2(0.5f, 1);
                tabRT.anchoredPosition = new Vector2(0, -50);
                tabRT.sizeDelta = new Vector2(-40, 40);

                HorizontalLayoutGroup hlg = tabContainerGO.AddComponent<HorizontalLayoutGroup>();
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth = false;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.spacing = 10;
            }

            // Close button
            UI.CreateUIButton(backgroundBoxGO, 60, 60, "X", 30, -30, -30, AnchorPresets.topRight, () => Hide());

            // Content Area
            float topOffset = IsUndocked ? -50 : -100; // More space if tabs exist
            GameObject scrollableGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0.2f, 0.2f, 0.2f, 0.5f), AnchorPresets.stretchAll, 0, 0, new Vector2(0, topOffset));
            RectTransform scrollRT = scrollableGO.GetComponent<RectTransform>();
            scrollRT.offsetMin = new Vector2(20, 20);
            scrollRT.offsetMax = new Vector2(-20, IsUndocked ? -60 : -100);

            scrollRect = scrollableGO.GetComponent<ScrollRect>();
            contentGO = scrollRect.content.gameObject;

            // Grid Layout
            VerticalLayoutGroup vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) DestroyImmediate(vlg);

            GridLayoutGroup grid = contentGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(200, 250);
            grid.spacing = new Vector2(10, 10);
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.childAlignment = TextAnchor.UpperLeft;

            SetLayerRecursive(canvasGO, 5); // UI layer for children
            
            // Register with manager
            if (Gallery.singleton != null)
            {
                Gallery.singleton.AddPanel(this);
            }

            CreateResizeHandles();
            Hide();
        }

        private void CreateResizeHandles()
        {
            CreateResizeHandle(AnchorPresets.bottomRight, 0);
            CreateResizeHandle(AnchorPresets.bottomLeft, -90);
            CreateResizeHandle(AnchorPresets.topLeft, 180);
            CreateResizeHandle(AnchorPresets.topRight, 90);
        }

        private void CreateResizeHandle(int anchor, float rotationZ)
        {
            GameObject handleGO = new GameObject("ResizeHandle_" + anchor);
            handleGO.transform.SetParent(backgroundBoxGO.transform, false);

            Image img = handleGO.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.01f); // Invisible hit area

            RectTransform handleRT = handleGO.GetComponent<RectTransform>();
            handleRT.anchorMin = AnchorPresets.GetAnchorMin(anchor);
            handleRT.anchorMax = AnchorPresets.GetAnchorMax(anchor);
            handleRT.pivot = AnchorPresets.GetPivot(anchor);
            handleRT.anchoredPosition = Vector2.zero;
            handleRT.sizeDelta = new Vector2(60, 60);

            // Text
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(handleGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.text = "◢";
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 36; // Increased size
            t.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            t.alignment = TextAnchor.MiddleCenter;

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            textRT.localRotation = Quaternion.Euler(0, 0, rotationZ);

            // UIResizable
            UIResizable resizer = handleGO.AddComponent<UIResizable>();
            resizer.target = backgroundBoxGO.GetComponent<RectTransform>();
            resizer.anchor = anchor;

            // Hover Effect
            UIHoverColor hover = handleGO.AddComponent<UIHoverColor>();
            hover.targetText = t;
            hover.normalColor = t.color;
            hover.hoverColor = Color.green;
        }

        public void SetCategories(List<Gallery.Category> cats)
        {
            categories = cats;

            // Try to restore last tab if not specified
            if (!IsUndocked && string.IsNullOrEmpty(currentPath) && Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
            {
                string lastPageName = Settings.Instance.LastGalleryPage.Value;
                var cat = categories.FirstOrDefault(c => c.name == lastPageName);
                if (!string.IsNullOrEmpty(cat.name))
                {
                    currentPath = cat.path;
                    currentExtension = cat.extension;
                    titleText.text = cat.name;
                }
            }

            if (string.IsNullOrEmpty(currentPath) && categories.Count > 0)
            {
                // Fallback to first category
                currentPath = categories[0].path;
                currentExtension = categories[0].extension;
                titleText.text = categories[0].name;
            }

            // If undocked, update tabs (which might just handle layout without adding tab buttons if we want)
            // But currently UpdateTabs logic adds buttons.
            // If undocked, we have 1 category in the list (as passed by Gallery.Undock)
            // So it will create 1 tab button for that category.
            // Maybe we don't want the tab button at all if undocked? Just the title?
            // The user asked "tabs in the main gallery to be able to be undocked".
            // An independent panel usually implies just the content.
            // But navigation is nice? 
            // If I pass only 1 category, it shows 1 tab. That's redundant with the title.
            // Let's hide the tab area if IsUndocked.
            
            if (!IsUndocked) UpdateTabs();
            else 
            {
                 // If undocked, we might want to update title to reflect category if it changed (it shouldn't)
                 if (categories.Count > 0)
                 {
                     titleText.text = categories[0].name;
                 }
            }
        }

        private void UpdateTabs()
        {
            if (tabContainerGO == null) return;

            foreach (var btn in activeTabButtons)
            {
                Destroy(btn);
            }
            activeTabButtons.Clear();

            if (categories == null || categories.Count == 0) return;

            foreach (var cat in categories)
            {
                // We use a local copy for the closure
                var c = cat;
                bool isActive = (c.path == currentPath && c.extension == currentExtension);
                Color btnColor = isActive ? new Color(0.3f, 0.4f, 0.6f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f);

                // Container for Tab + Undock
                GameObject groupGO = new GameObject("TabGroup_" + c.name);
                groupGO.transform.SetParent(tabContainerGO.transform, false);
                LayoutElement groupLE = groupGO.AddComponent<LayoutElement>();
                groupLE.minWidth = 140; 
                groupLE.preferredWidth = 170; // 140 + 30
                groupLE.minHeight = 35;
                groupLE.preferredHeight = 35;
                
                HorizontalLayoutGroup groupHLG = groupGO.AddComponent<HorizontalLayoutGroup>();
                groupHLG.childControlWidth = false;
                groupHLG.childControlHeight = true;
                groupHLG.childForceExpandWidth = false;
                groupHLG.spacing = 2;

                // Tab Button
                GameObject btnGO = UI.CreateUIButton(groupGO, 130, 35, c.name, 14, 0, 0, AnchorPresets.middleLeft, () => {
                    Show(c.name, c.extension, c.path);
                    if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                    {
                        Settings.Instance.LastGalleryPage.Value = c.name;
                    }
                });
                
                Image img = btnGO.GetComponent<Image>();
                if (img != null) img.color = btnColor;

                Text txt = btnGO.GetComponentInChildren<Text>();
                if (txt != null) txt.color = isActive ? Color.white : Color.black;

                // Undock Button - Only in main panel
                if (!IsUndocked)
                {
                    GameObject undockGO = UI.CreateUIButton(groupGO, 30, 35, "↗", 14, 0, 0, AnchorPresets.middleRight, () => {
                        if (Gallery.singleton != null)
                        {
                            Gallery.singleton.Undock(c);
                        }
                    });
                    Image undockImg = undockGO.GetComponent<Image>();
                    undockImg.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                }

                activeTabButtons.Add(groupGO);
            }
            
            SetLayerRecursive(tabContainerGO, 5);
        }

        private void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        public void Show(string title, string extension, string path)
        {
            if (canvas == null) Init(IsUndocked);

            titleText.text = title;
            currentExtension = extension;
            currentPath = path;

            if (Application.isPlaying && canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = Camera.main;
            }

            canvas.gameObject.SetActive(true);
            RefreshFiles();
            if (!IsUndocked) UpdateTabs();

            // Position it in front of the user if in VR, ONLY ONCE
            if (!hasBeenPositioned && SuperController.singleton != null)
            {
                Transform head = SuperController.singleton.centerCameraTarget.transform;
                // If undocked, maybe offset slightly? No, user can move it.
                // Or maybe default position is different for undocked?
                // Let's stick to center for now.
                canvas.transform.position = head.position + head.forward * 1.5f;
                
                // Face the user
                bool lockRotation = (Settings.Instance != null && Settings.Instance.LockGalleryRotation != null && Settings.Instance.LockGalleryRotation.Value);
                if (lockRotation)
                {
                    // Enforce horizontal leveling on launch
                    Vector3 lookDir = canvas.transform.position - head.position;
                    canvas.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
                else
                {
                    canvas.transform.rotation = head.rotation;
                }
                
                hasBeenPositioned = true;
            }
        }

        public void Hide()
        {
            if (canvas != null)
                canvas.gameObject.SetActive(false);
        }

        public void RefreshFiles()
        {
            // Clear existing
            foreach (var btn in activeButtons)
            {
                Destroy(btn);
            }
            activeButtons.Clear();

            List<FileEntry> files = new List<FileEntry>();
            string[] extensions = currentExtension.Split('|');

            // 1. Search in .var packages via FileManager
            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    if (pkg.FileEntries != null)
                    {
                        foreach (var entry in pkg.FileEntries)
                        {
                            if (IsMatch(entry, currentPath, extensions))
                            {
                                files.Add(entry);
                            }
                        }
                    }
                }
            }

            // 2. Search on local disk (System files)
            if (Directory.Exists(currentPath))
            {
                foreach (var ext in extensions)
                {
                    string[] systemFiles = Directory.GetFiles(currentPath, "*." + ext, SearchOption.AllDirectories);
                    foreach (var sysPath in systemFiles)
                    {
                        files.Add(new SystemFileEntry(sysPath));
                    }
                }
            }

            // Sort by date (newest first)
            files.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

            // Limit to avoid performance issues if too many files
            int count = Mathf.Min(files.Count, 200);
            for (int i = 0; i < count; i++)
            {
                CreateFileButton(files[i]);
            }
        }

        private bool IsMatch(FileEntry entry, string path, string[] extensions)
        {
            string entryPath = entry.Path;
            // Handle package paths which look like "uid:/path"
            if (entryPath.Contains(":/"))
            {
                entryPath = entryPath.Substring(entryPath.IndexOf(":/") + 2);
            }

            if (!entryPath.StartsWith(path, StringComparison.OrdinalIgnoreCase)) return false;

            foreach (var ext in extensions)
            {
                if (entryPath.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void CreateFileButton(FileEntry file)
        {
            GameObject btnGO = new GameObject("FileButton_" + file.Name);
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = Color.gray;

            Button btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(() => OnFileClick(file));

            // Thumbnail (if any)
            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(btnGO.transform, false);
            RawImage thumbImg = thumbGO.AddComponent<RawImage>();
            thumbImg.color = new Color(0, 0, 0, 0.5f);
            RectTransform thumbRT = thumbGO.GetComponent<RectTransform>();
            thumbRT.anchorMin = new Vector2(0, 0.2f);
            thumbRT.anchorMax = new Vector2(1, 1);
            thumbRT.sizeDelta = Vector2.zero;

            // Load thumbnail
            LoadThumbnail(file, thumbImg);

            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(btnGO.transform, false);
            Text labelText = labelGO.AddComponent<Text>();
            labelText.text = file.Name;
            labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = 18;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.LowerCenter;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = new Vector2(1, 0.2f);
            labelRT.sizeDelta = Vector2.zero;

            activeButtons.Add(btnGO);
            SetLayerRecursive(btnGO, 5);
        }

        private void LoadThumbnail(FileEntry file, RawImage target)
        {
            string imgPath = "";
            if (file.Path.EndsWith(".json") || file.Path.EndsWith(".vap") || file.Path.EndsWith(".vam"))
                imgPath = Regex.Replace(file.Path, "\\.(json|vac|vap|vam|scene|assetbundle)$", ".jpg");
            else if (file.Path.EndsWith(".jpg") || file.Path.EndsWith(".png"))
                imgPath = file.Path;

            if (string.IsNullOrEmpty(imgPath)) 
            {
                return;
            }

            // Check cache
            if (CustomImageLoaderThreaded.singleton == null) return;

            Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath);
            if (tex != null)
            {
                target.texture = tex;
                target.color = Color.white;
            }
            else
            {
                // Request
                CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.QIPool.Get();
                qi.imgPath = imgPath;
                qi.isThumbnail = true;
                qi.priority = 10; // High priority for gallery thumbnails
                qi.callback = (res) => {
                    if (res != null && res.tex != null && target != null) {
                        target.texture = res.tex;
                        target.color = Color.white;
                    }
                };
                CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
            }
        }

        private void OnFileClick(FileEntry file)
        {
            LogUtil.Log("Selected file: " + file.Path);
            
            // Post message to load
            if (file.Path.EndsWith(".json"))
            {
                MethodInfo loadInternalMethod = typeof(SuperController).GetMethod("LoadInternal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                loadInternalMethod.Invoke(SuperController.singleton, new object[3] { file.Path, false, false });
            }
            
            // Just hide this panel? or all? 
            // If I select a file, I usually want to close the gallery.
            if (Gallery.singleton != null)
                Gallery.singleton.Hide();
            else
                Hide();
        }
    }
}
