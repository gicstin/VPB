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
    public class Gallery : MonoBehaviour
    {
        public static Gallery singleton;

        private Canvas mainCanvas;
        private GameObject backgroundBoxGO;
        private GameObject contentGO;
        private ScrollRect scrollRect;
        private Text titleText;

        private List<FileEntry> currentFiles = new List<FileEntry>();
        private List<GameObject> activeButtons = new List<GameObject>();

        private string currentPath = "";
        private string currentExtension = "json";

        public bool IsVisible => mainCanvas != null && mainCanvas.gameObject.activeSelf;

        void Awake()
        {
            singleton = this;
        }

        public void Init()
        {
            if (mainCanvas != null) return;

            GameObject canvasGO = new GameObject("VPB_GalleryCanvas");
            mainCanvas = canvasGO.AddComponent<Canvas>();
            canvasGO.AddComponent<GraphicRaycaster>();
            
            if (Application.isPlaying)
            {
                // In VaM, we use WorldSpace for VR support
                mainCanvas.renderMode = RenderMode.WorldSpace;
                mainCanvas.transform.position = new Vector3(0, 1.5f, 2.0f); // Default position
                mainCanvas.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                canvasGO.layer = 10; // WorldUI layer
            }
            else
            {
                mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4;

            // Background
            backgroundBoxGO = UI.AddChildGOImage(canvasGO, new Color(0.1f, 0.1f, 0.1f, 0.9f), AnchorPresets.centre, 1200, 800, Vector2.zero);

            // Title
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(backgroundBoxGO.transform, false);
            titleText = titleGO.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 40;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.UpperCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -20);
            titleRT.sizeDelta = new Vector2(0, 60);

            // Close button
            UI.CreateUIButton(backgroundBoxGO, 60, 60, "X", 30, -30, -30, AnchorPresets.topRight, () => Hide());

            // Content Area
            GameObject scrollableGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0.2f, 0.2f, 0.2f, 0.5f), AnchorPresets.stretchAll, 0, 0, new Vector2(0, -50));
            RectTransform scrollRT = scrollableGO.GetComponent<RectTransform>();
            scrollRT.offsetMin = new Vector2(20, 20);
            scrollRT.offsetMax = new Vector2(-20, -100);

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

            SetLayerRecursive(canvasGO, 10); // WorldUI layer

            Hide();
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
            if (mainCanvas == null) Init();

            titleText.text = title;
            currentExtension = extension;
            currentPath = path;

            mainCanvas.gameObject.SetActive(true);
            RefreshFiles();

            // Position it in front of the user if in VR
            if (SuperController.singleton != null)
            {
                Transform head = SuperController.singleton.centerCameraTarget.transform;
                mainCanvas.transform.position = head.position + head.forward * 1.5f;
                // Face the user
                mainCanvas.transform.rotation = head.rotation;
            }
        }

        public void Hide()
        {
            if (mainCanvas != null)
                mainCanvas.gameObject.SetActive(false);
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

            LogUtil.Log("Gallery found " + files.Count + " files for path: " + currentPath);

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
            SetLayerRecursive(btnGO, 10);
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
            // Handle file selection (e.g., load scene)
            LogUtil.Log("Selected file: " + file.Path);
            
            // Post message to load
            if (file.Path.EndsWith(".json"))
            {
                MethodInfo loadInternalMethod = typeof(SuperController).GetMethod("LoadInternal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                loadInternalMethod.Invoke(SuperController.singleton, new object[3] { file.Path, false, false });
            }
            
            Hide();
        }
    }
}
