using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace var_browser
{
    public class GalleryPanel : MonoBehaviour
    {
        public Canvas canvas;
        public Text statusBarText;
        private GameObject backgroundBoxGO;
        private GameObject contentGO;
        // private GameObject tabContainerGO; // Unused
        private ScrollRect scrollRect;
        private Text titleText;

        public List<Gallery.Category> categories = new List<Gallery.Category>();
        // private List<FileEntry> currentFiles = new List<FileEntry>(); // Unused

        private List<GameObject> activeButtons = new List<GameObject>();
        private Dictionary<string, Image> fileButtonImages = new Dictionary<string, Image>();
        private string selectedPath = null;
        private List<GameObject> leftActiveTabButtons = new List<GameObject>();
        private List<GameObject> rightActiveTabButtons = new List<GameObject>();

        private string currentPath = "";
        private string currentExtension = "json";
        
        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;
        
        public enum TabSide { Hidden, Left, Right }
        public enum ContentType { Category, Creator, License }

        // Configuration
        public bool IsUndocked = false;
        public bool DragDropReplaceMode = false;
        private Toggle addToggle;
        private Toggle replaceToggle;
        public Gallery.Category? UndockedCategory;
        public string UndockedCreator;
        private bool hasBeenPositioned = false;
        // private TabSide currentTabSide = TabSide.Right; // Unused
        private ContentType activeContentType = ContentType.Category; // Deprecated
        
        private ContentType? leftActiveContent = null;
        private ContentType? rightActiveContent = ContentType.Category;
        
        private GameObject leftTabScrollGO;
        private GameObject rightTabScrollGO;
        private GameObject leftTabContainerGO;
        private GameObject rightTabContainerGO;
        // private GameObject tabScrollGO; // Unused
        private RectTransform contentScrollRT;
        
        // Buttons
        private Text rightCategoryBtnText;
        private Image rightCategoryBtnImage;
        private Text rightCreatorBtnText;
        private Image rightCreatorBtnImage;
        
        private Text leftCategoryBtnText;
        private Image leftCategoryBtnImage;
        private Text leftCreatorBtnText;
        private Image leftCreatorBtnImage;
        
        // Data
        private string currentCreator = "";
        private string categoryFilter = "";
        private string creatorFilter = "";

        private InputField leftSearchInput;
        private InputField rightSearchInput;
        private Stack<GameObject> tabButtonPool = new Stack<GameObject>();
        
        public struct CreatorCacheEntry { public string Name; public int Count; }
        private List<CreatorCacheEntry> cachedCreators = new List<CreatorCacheEntry>();
        private bool creatorsCached = false;
        
        private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
        private bool categoriesCached = false;

        // Define colors for different content types
        public static readonly Color ColorCategory = new Color(0.7f, 0.2f, 0.2f, 1f); // Desaturated Red
        public static readonly Color ColorCreator = new Color(0.3f, 0.6f, 0.3f, 1f); // Desaturated Green
        public static readonly Color ColorLicense = new Color(1f, 0f, 1f, 1f); // Magenta

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

            // Tab Area - Create if not undocked OR if undocked for a creator
            if (!IsUndocked || !string.IsNullOrEmpty(UndockedCreator))
            {
                float tabAreaWidth = 180f;
                
                // 1. Right Tab Area
                rightTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchRight, tabAreaWidth, 0, Vector2.zero);
                RectTransform rightTabRT = rightTabScrollGO.GetComponent<RectTransform>();
                rightTabRT.anchorMin = new Vector2(1, 0);
                rightTabRT.anchorMax = new Vector2(1, 1);
                rightTabRT.offsetMin = new Vector2(-tabAreaWidth - 10, 20); 
                rightTabRT.offsetMax = new Vector2(-10, -95); 
                
                rightTabContainerGO = rightTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                VerticalLayoutGroup rightVlg = rightTabContainerGO.GetComponent<VerticalLayoutGroup>();
                rightVlg.spacing = 2;
                rightVlg.padding = new RectOffset(0, 10, 0, 0);

                rightSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth, (val) => {
                    if (rightActiveContent == ContentType.Category) categoryFilter = val;
                    else if (rightActiveContent == ContentType.Creator) creatorFilter = val;
                    UpdateTabs();
                });
                RectTransform rSearchRT = rightSearchInput.GetComponent<RectTransform>();
                rSearchRT.anchorMin = new Vector2(1, 1);
                rSearchRT.anchorMax = new Vector2(1, 1);
                rSearchRT.pivot = new Vector2(1, 1);
                rSearchRT.anchoredPosition = new Vector2(-10, -50);

                // 2. Left Tab Area
                leftTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchLeft, tabAreaWidth, 0, Vector2.zero);
                RectTransform leftTabRT = leftTabScrollGO.GetComponent<RectTransform>();
                leftTabRT.anchorMin = new Vector2(0, 0);
                leftTabRT.anchorMax = new Vector2(0, 1);
                leftTabRT.offsetMin = new Vector2(10, 20);
                leftTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -95);
                
                leftTabContainerGO = leftTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                VerticalLayoutGroup leftVlg = leftTabContainerGO.GetComponent<VerticalLayoutGroup>();
                leftVlg.spacing = 2;
                leftVlg.padding = new RectOffset(0, 10, 0, 0);

                leftSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth, (val) => {
                    if (leftActiveContent == ContentType.Category) categoryFilter = val;
                    else if (leftActiveContent == ContentType.Creator) creatorFilter = val;
                    UpdateTabs();
                });
                RectTransform lSearchRT = leftSearchInput.GetComponent<RectTransform>();
                lSearchRT.anchorMin = new Vector2(0, 1);
                lSearchRT.anchorMax = new Vector2(0, 1);
                lSearchRT.pivot = new Vector2(0, 1);
                lSearchRT.anchoredPosition = new Vector2(10, -50);

                // Right Toggle Buttons
                // Category (Red)
                GameObject rightCatBtn = UI.CreateUIButton(backgroundBoxGO, 30, 60, ">", 20, 30, 40, AnchorPresets.middleRight, () => ToggleRight(ContentType.Category));
                rightCategoryBtnImage = rightCatBtn.GetComponent<Image>();
                rightCategoryBtnImage.color = ColorCategory;
                rightCategoryBtnText = rightCatBtn.GetComponentInChildren<Text>();
                rightCategoryBtnText.text = "<";
                
                // Creator (Green)
                GameObject rightCreatorBtn = UI.CreateUIButton(backgroundBoxGO, 30, 60, ">", 20, 30, -40, AnchorPresets.middleRight, () => ToggleRight(ContentType.Creator));
                rightCreatorBtnImage = rightCreatorBtn.GetComponent<Image>();
                rightCreatorBtnImage.color = ColorCreator;
                rightCreatorBtnText = rightCreatorBtn.GetComponentInChildren<Text>();
                rightCreatorBtnText.text = ">";

                // Left Toggle Buttons
                // Category (Red)
                GameObject leftCatBtn = UI.CreateUIButton(backgroundBoxGO, 30, 60, "<", 20, -30, 40, AnchorPresets.middleLeft, () => ToggleLeft(ContentType.Category));
                leftCategoryBtnImage = leftCatBtn.GetComponent<Image>();
                leftCategoryBtnImage.color = ColorCategory;
                leftCategoryBtnText = leftCatBtn.GetComponentInChildren<Text>();
                leftCategoryBtnText.text = "<";
                
                // Creator (Green)
                GameObject leftCreatorBtn = UI.CreateUIButton(backgroundBoxGO, 30, 60, "<", 20, -30, -40, AnchorPresets.middleLeft, () => ToggleLeft(ContentType.Creator));
                leftCreatorBtnImage = leftCreatorBtn.GetComponent<Image>();
                leftCreatorBtnImage.color = ColorCreator;
                leftCreatorBtnText = leftCreatorBtn.GetComponentInChildren<Text>();
                leftCreatorBtnText.text = "<";
            }

            // Content Area
            float rightPadding = (IsUndocked && string.IsNullOrEmpty(UndockedCreator)) ? -20 : -210; // -180 tab width - 10 padding - 20 margin
            
            GameObject scrollableGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0.2f, 0.2f, 0.2f, 0.5f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            contentScrollRT = scrollableGO.GetComponent<RectTransform>();
            contentScrollRT.offsetMin = new Vector2(20, 50); // Raised for status bar (was 60, 50 is fine if bar is 40)
            contentScrollRT.offsetMax = new Vector2(rightPadding, -50);

            scrollRect = scrollableGO.GetComponent<ScrollRect>();
            contentGO = scrollRect.content.gameObject;

            // Status Bar Background
            GameObject statusBgGO = new GameObject("StatusBarBackground");
            statusBgGO.transform.SetParent(backgroundBoxGO.transform, false);
            Image statusBg = statusBgGO.AddComponent<Image>();
            statusBg.color = new Color(0f, 0f, 0f, 0.8f); // Dark black background
            statusBg.raycastTarget = false;

            RectTransform statusBgRT = statusBgGO.GetComponent<RectTransform>();
            statusBgRT.anchorMin = new Vector2(0, 0);
            statusBgRT.anchorMax = new Vector2(1, 0);
            statusBgRT.pivot = new Vector2(0.5f, 0);
            statusBgRT.anchoredPosition = Vector2.zero;
            statusBgRT.offsetMin = new Vector2(10, 10); // Left, Bottom margins
            statusBgRT.offsetMax = new Vector2(-10, 45); // Right, Top (relative to anchor min y=0) -> Height = 35

            // Status Text
            GameObject statusTextGO = new GameObject("StatusText");
            statusTextGO.transform.SetParent(statusBgGO.transform, false);
            statusBarText = statusTextGO.AddComponent<Text>();
            statusBarText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statusBarText.fontSize = 20;
            statusBarText.color = Color.yellow;
            statusBarText.alignment = TextAnchor.MiddleLeft;
            statusBarText.raycastTarget = false;
            
            RectTransform statusTextRT = statusTextGO.GetComponent<RectTransform>();
            statusTextRT.anchorMin = Vector2.zero;
            statusTextRT.anchorMax = Vector2.one;
            statusTextRT.sizeDelta = Vector2.zero; // Stretch to fill background
            statusTextRT.offsetMin = new Vector2(10, 0); // Padding left
            statusTextRT.offsetMax = new Vector2(-210, 0); // Padding right for toggles

            // Toggles
            // Add Toggle
            GameObject addToggleGO = UI.CreateToggle(statusBgGO, "Add", 80, 20, -110, 0, AnchorPresets.middleRight, (val) => {
                if (val) 
                {
                     DragDropReplaceMode = false;
                     if (replaceToggle != null && replaceToggle.isOn) replaceToggle.isOn = false;
                }
                else
                {
                     if (!DragDropReplaceMode && addToggle != null) addToggle.isOn = true;
                }
            });
            addToggle = addToggleGO.GetComponent<Toggle>();
            addToggle.isOn = !DragDropReplaceMode;

            // Replace Toggle
            GameObject replaceToggleGO = UI.CreateToggle(statusBgGO, "Replace", 100, 20, -10, 0, AnchorPresets.middleRight, (val) => {
                if (val)
                {
                    DragDropReplaceMode = true;
                    if (addToggle != null && addToggle.isOn) addToggle.isOn = false;
                }
                else
                {
                    if (DragDropReplaceMode && replaceToggle != null) replaceToggle.isOn = true;
                }
            });
            replaceToggle = replaceToggleGO.GetComponent<Toggle>();
            replaceToggle.isOn = DragDropReplaceMode;

            // Set initial state
            if (!IsUndocked) 
            {
                rightActiveContent = ContentType.Category;
                leftActiveContent = null;
                UpdateLayout();
            }
            else if (!string.IsNullOrEmpty(UndockedCreator))
            {
                currentCreator = UndockedCreator;
                rightActiveContent = ContentType.Category;
                leftActiveContent = null;
                UpdateLayout();
            }

            // Grid Layout
            VerticalLayoutGroup vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) DestroyImmediate(vlg);

            GridLayoutGroup grid = contentGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(200, 200);
            grid.spacing = new Vector2(10, 10);
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.childAlignment = TextAnchor.UpperLeft;

            // Close button - Rendered last to be on top
            GameObject closeBtn = UI.CreateUIButton(backgroundBoxGO, 50, 50, "X", 30, 0, 0, AnchorPresets.topRight, () => Hide());
            closeBtn.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.7f, 1f);

            SetLayerRecursive(canvasGO, 5); // UI layer for children
            
            // Register with manager
            if (Gallery.singleton != null)
            {
                Gallery.singleton.AddPanel(this);
            }

            CreateResizeHandles();
            Hide();
        }

        private string dragStatusMsg = null;

        public void SetStatus(string msg)
        {
            if (string.IsNullOrEmpty(msg)) dragStatusMsg = null;
            else dragStatusMsg = msg;
        }

        void Update()
        {
            if (dragStatusMsg != null)
            {
                if (statusBarText != null) statusBarText.text = dragStatusMsg;
            }
            else
            {
                if (IsVisible && statusBarText != null)
                {
                     string msg;
                     Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : Camera.main;
                     if (cam == null) return;
                     
                     SceneUtils.DetectAtom(Input.mousePosition, cam, out msg);
                     statusBarText.text = msg;
                }
            }
        }

        private void ToggleRight(ContentType type)
        {
            if (rightActiveContent == type) 
            {
                rightActiveContent = null;
            }
            else 
            {
                rightActiveContent = type;
                // Collapse Left IF it is the SAME type
                if (leftActiveContent == type) leftActiveContent = null;
            }
            
            UpdateLayout();
            UpdateTabs();
        }

        private void ToggleLeft(ContentType type)
        {
            if (leftActiveContent == type)
            {
                leftActiveContent = null;
            }
            else
            {
                leftActiveContent = type;
                // Collapse Right IF it is the SAME type
                if (rightActiveContent == type) rightActiveContent = null;
            }
            
            UpdateLayout();
            UpdateTabs();
        }

        private void UpdateLayout()
        {
            if (!creatorsCached) CacheCreators();
            if (!categoriesCached) CacheCategoryCounts();

            if (contentScrollRT == null) return;
            
            float leftOffset = 20;
            float rightOffset = (IsUndocked && string.IsNullOrEmpty(UndockedCreator)) ? -20 : -20;
            
            // Left Side
            if (leftActiveContent.HasValue && leftTabScrollGO != null)
            {
                leftTabScrollGO.SetActive(true);
                leftOffset = 210; 
                if (leftSearchInput != null) 
                {
                    leftSearchInput.gameObject.SetActive(true);
                    string target = leftActiveContent.Value == ContentType.Category ? categoryFilter : creatorFilter;
                    // Setting text triggers OnValueChanged which calls UpdateTabs, which is fine but maybe redundant.
                    // To avoid event we'd need to remove listener. 
                    // But if text is different, we probably WANT to update tabs anyway?
                    // Actually UpdateTabs is called at end of UpdateLayout anyway.
                    // But changing text triggers it immediately.
                    if (leftSearchInput.text != target) leftSearchInput.text = target;
                    
                    if (leftSearchInput.placeholder is Text ph)
                    {
                        ph.text = leftActiveContent.Value.ToString() + "...";
                    }
                }
            }
            else if (leftTabScrollGO != null)
            {
                leftTabScrollGO.SetActive(false);
                if (leftSearchInput != null) leftSearchInput.gameObject.SetActive(false);
            }
            
            // Right Side
            if (rightActiveContent.HasValue && rightTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(true);
                rightOffset = -210;
                if (rightSearchInput != null) 
                {
                    rightSearchInput.gameObject.SetActive(true);
                    string target = rightActiveContent.Value == ContentType.Category ? categoryFilter : creatorFilter;
                    if (rightSearchInput.text != target) rightSearchInput.text = target;

                    if (rightSearchInput.placeholder is Text ph)
                    {
                        ph.text = rightActiveContent.Value.ToString() + "...";
                    }
                }
            }
            else if (rightTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(false);
                if (rightSearchInput != null) rightSearchInput.gameObject.SetActive(false);
            }
            
            contentScrollRT.offsetMin = new Vector2(leftOffset, 20);
            contentScrollRT.offsetMax = new Vector2(rightOffset, -50);
            
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
             UpdateButtonState(rightCategoryBtnText, true, ContentType.Category);
             UpdateButtonState(rightCreatorBtnText, true, ContentType.Creator);
             UpdateButtonState(leftCategoryBtnText, false, ContentType.Category);
             UpdateButtonState(leftCreatorBtnText, false, ContentType.Creator);
        }

        private void UpdateButtonState(Text btnText, bool isRight, ContentType type)
        {
             if (btnText == null) return;
             
             bool isOpen = isRight ? (rightActiveContent == type) : (leftActiveContent == type);
             
             if (isRight)
                 btnText.text = isOpen ? ">" : "<";
             else
                 btnText.text = isOpen ? "<" : ">";
        }

        private void CacheCategoryCounts()
        {
            if (categories == null) return;
            categoryCounts.Clear();
            
            var catExtensions = new Dictionary<string, string[]>();
            foreach (var c in categories) 
            {
                categoryCounts[c.name] = 0;
                catExtensions[c.name] = c.extension.Split('|');
            }

            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    if (pkg.FileEntries == null) continue;
                    foreach (var entry in pkg.FileEntries)
                    {
                        foreach (var cat in categories)
                        {
                            if (IsMatch(entry, cat.path, catExtensions[cat.name]))
                            {
                                categoryCounts[cat.name]++;
                            }
                        }
                    }
                }
            }
            categoriesCached = true;
        }

        private void CacheCreators()
        {
            if (FileManager.PackagesByUid == null) return;
            
            Dictionary<string, int> counts = new Dictionary<string, int>();
            string[] extensions = currentExtension.Split('|');
            
            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (string.IsNullOrEmpty(pkg.Creator)) continue;
                if (pkg.FileEntries == null) continue;

                foreach (var entry in pkg.FileEntries)
                {
                     if (IsMatch(entry, currentPath, extensions))
                     {
                         if (!counts.ContainsKey(pkg.Creator)) counts[pkg.Creator] = 0;
                         counts[pkg.Creator]++;
                     }
                }
            }
            
            cachedCreators = counts.Select(kv => new CreatorCacheEntry { Name = kv.Key, Count = kv.Value })
                                   .OrderBy(c => c.Name).ToList();
            creatorsCached = true;
        }

        private void CreateResizeHandles()
        {
            CreateResizeHandle(AnchorPresets.bottomRight, 0);
            CreateResizeHandle(AnchorPresets.bottomLeft, -90);
            CreateResizeHandle(AnchorPresets.topLeft, 180);
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
            
            // Push handles outwards
            float offsetDist = 20f;
            Vector2 offset = Vector2.zero;
            if (anchor == AnchorPresets.bottomRight) offset = new Vector2(offsetDist, -offsetDist);
            else if (anchor == AnchorPresets.bottomLeft) offset = new Vector2(-offsetDist, -offsetDist);
            else if (anchor == AnchorPresets.topLeft) offset = new Vector2(-offsetDist, offsetDist);
            else if (anchor == AnchorPresets.topRight) offset = new Vector2(offsetDist, offsetDist);

            handleRT.anchoredPosition = offset;
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
            categoriesCached = false;

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
            if (leftActiveContent.HasValue) UpdateTabs(leftActiveContent.Value, leftTabContainerGO, leftActiveTabButtons, true);
            if (rightActiveContent.HasValue) UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
        }

        private void UpdateTabs(ContentType contentType, GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;

            foreach (var btn in trackedButtons)
            {
                ReturnTabButton(btn);
            }
            trackedButtons.Clear();

            if (contentType == ContentType.Category)
            {
                if (categories == null || categories.Count == 0) return;
                if (!categoriesCached) CacheCategoryCounts();

                foreach (var cat in categories)
                {
                    if (!string.IsNullOrEmpty(categoryFilter) && cat.name.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var c = cat;
                    bool isActive = (c.path == currentPath && c.extension == currentExtension);
                    Color btnColor = isActive ? ColorCategory : new Color(0.7f, 0.7f, 0.7f, 1f);

                    int count = 0;
                    if (categoryCounts.ContainsKey(c.name)) count = categoryCounts[c.name];
                    string label = c.name + " (" + count + ")";

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        Show(c.name, c.extension, c.path);
                        if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                        {
                            Settings.Instance.LastGalleryPage.Value = c.name;
                        }
                        UpdateTabs();
                    }, 
                    !IsUndocked ? () => {
                         if (Gallery.singleton != null) Gallery.singleton.Undock(c);
                    } : (UnityAction)null, trackedButtons);
                }
            }
            else if (contentType == ContentType.Creator)
            {
                if (!creatorsCached) CacheCreators();
                if (cachedCreators == null) return;
                
                foreach (var creator in cachedCreators)
                {
                    if (!string.IsNullOrEmpty(creatorFilter) && creator.Name.IndexOf(creatorFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string cName = creator.Name;
                    bool isActive = (currentCreator == cName);
                    Color btnColor = isActive ? ColorCreator : new Color(0.7f, 0.7f, 0.7f, 1f);

                    string label = cName + " (" + creator.Count + ")";

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        if (currentCreator == cName) currentCreator = "";
                        else currentCreator = cName;
                        RefreshFiles();
                        UpdateTabs(); 
                    }, 
                    !IsUndocked ? () => {
                         if (Gallery.singleton != null) Gallery.singleton.UndockCreator(cName);
                    } : (UnityAction)null, trackedButtons);
                }
            }
            
            SetLayerRecursive(container, 5);
        }

        private void CreateTabButton(Transform parent, string label, Color color, bool isActive, UnityAction onClick, UnityAction onUndock, List<GameObject> targetList)
        {
            GameObject groupGO = GetTabButton(parent);
            if (groupGO == null)
            {
                // Container
                groupGO = new GameObject("TabGroup");
                groupGO.transform.SetParent(parent, false);
                LayoutElement groupLE = groupGO.AddComponent<LayoutElement>();
                groupLE.minWidth = 140; 
                groupLE.preferredWidth = 170;
                groupLE.minHeight = 35;
                groupLE.preferredHeight = 35;
                
                HorizontalLayoutGroup groupHLG = groupGO.AddComponent<HorizontalLayoutGroup>();
                groupHLG.childControlWidth = false;
                groupHLG.childControlHeight = true;
                groupHLG.childForceExpandWidth = false;
                groupHLG.spacing = 2;

                // Tab Button
                GameObject btnGO = UI.CreateUIButton(groupGO, 130, 35, "", 14, 0, 0, AnchorPresets.middleLeft, null);
                btnGO.name = "Button";
                
                // Undock Button
                GameObject undockGO = UI.CreateUIButton(groupGO, 30, 35, "↗", 14, 0, 0, AnchorPresets.middleRight, null);
                undockGO.name = "UndockButton";
                undockGO.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 1f);
            }
            
            groupGO.name = "TabGroup_" + label;
            
            // Reconfigure
            Transform btnTr = groupGO.transform.GetChild(0);
            GameObject btnGO_Reuse = btnTr.gameObject;
            Button btnComp = btnGO_Reuse.GetComponent<Button>();
            btnComp.onClick.RemoveAllListeners();
            if (onClick != null) btnComp.onClick.AddListener(onClick);
            
            Image img = btnGO_Reuse.GetComponent<Image>();
            img.color = color;
            
            Text txt = btnGO_Reuse.GetComponentInChildren<Text>();
            txt.text = label;
            txt.color = isActive ? Color.white : Color.black;
            
            Transform undockTr = groupGO.transform.GetChild(1);
            GameObject undockGO_Reuse = undockTr.gameObject;
            if (onUndock != null)
            {
                undockGO_Reuse.SetActive(true);
                Button uBtn = undockGO_Reuse.GetComponent<Button>();
                uBtn.onClick.RemoveAllListeners();
                uBtn.onClick.AddListener(onUndock);
            }
            else
            {
                undockGO_Reuse.SetActive(false);
            }
            
            if (targetList != null) targetList.Add(groupGO);
        }

        private InputField CreateSearchInput(GameObject parent, float width, UnityAction<string> onValueChanged)
        {
            GameObject inputGO = new GameObject("SearchInput");
            inputGO.transform.SetParent(parent.transform, false);
            
            Image bg = inputGO.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            
            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.sizeDelta = new Vector2(width, 35);
            
            // Text Area
            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(10, 0);
            textAreaRT.offsetMax = new Vector2(-45, 0); // Room for X button
            
            // Placeholder
            GameObject placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);
            Text placeholderText = placeholder.AddComponent<Text>();
            placeholderText.text = "Search...";
            placeholderText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            placeholderText.fontSize = 18; // Increased from 14
            placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.alignment = TextAnchor.MiddleLeft; // Vertically centered
            RectTransform placeholderRT = placeholder.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = Vector2.zero;
            
            // Text
            GameObject text = new GameObject("Text");
            text.transform.SetParent(textArea.transform, false);
            Text textComponent = text.AddComponent<Text>();
            textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            textComponent.fontSize = 18; // Increased from 14
            textComponent.color = Color.white;
            textComponent.supportRichText = false;
            textComponent.alignment = TextAnchor.MiddleLeft; // Vertically centered
            RectTransform textRT = text.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            
            input.textComponent = textComponent;
            input.placeholder = placeholderText;
            input.onValueChanged.AddListener(onValueChanged);
            
            // Clear Button
            GameObject clearBtn = UI.CreateUIButton(inputGO, 40, 40, "X", 24, 0, 0, AnchorPresets.middleRight, () => { // Increased size and font
                input.text = "";
            });
            RectTransform clearRT = clearBtn.GetComponent<RectTransform>();
            clearRT.anchorMin = new Vector2(1, 0.5f);
            clearRT.anchorMax = new Vector2(1, 0.5f);
            clearRT.pivot = new Vector2(1, 0.5f);
            clearRT.anchoredPosition = new Vector2(-5, 0);
            clearBtn.GetComponent<Image>().color = new Color(0,0,0,0); // Transparent bg
            
            Text clearText = clearBtn.GetComponentInChildren<Text>();
            clearText.color = new Color(0.6f, 0.6f, 0.6f);

            UIHoverColor hover = clearBtn.AddComponent<UIHoverColor>();
            hover.targetText = clearText;
            hover.normalColor = clearText.color;
            hover.hoverColor = Color.red;

            return input;
        }

        private GameObject GetTabButton(Transform parent)
        {
            if (tabButtonPool.Count > 0)
            {
                GameObject btn = tabButtonPool.Pop();
                btn.transform.SetParent(parent, false);
                btn.SetActive(true);
                return btn;
            }
            return null;
        }

        private void ReturnTabButton(GameObject btn)
        {
            if (btn == null) return;
            btn.SetActive(false);
            // Keep parented to ensure cleanup on destroy
            if (backgroundBoxGO != null) btn.transform.SetParent(backgroundBoxGO.transform, false);
            tabButtonPool.Push(btn);
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
            if (currentExtension != extension || currentPath != path)
            {
                creatorsCached = false;
                currentCreator = "";
            }
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
            fileButtonImages.Clear();

            List<FileEntry> files = new List<FileEntry>();
            string[] extensions = currentExtension.Split('|');

            // 1. Search in .var packages via FileManager
            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    // Filter by Creator if in Creator mode or Undocked for a creator
                    string filterCreator = !string.IsNullOrEmpty(UndockedCreator) ? UndockedCreator : currentCreator;
                    
                    if (!string.IsNullOrEmpty(filterCreator))
                    {
                        if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != filterCreator) continue;
                    }

                    if (pkg.FileEntries != null)
                    {
                        foreach (var entry in pkg.FileEntries)
                        {
                            // In Creator mode, we accept ANY extension match (or still filter by currentExtension?)
                            // Usually a creator makes Scenes, Looks, etc. 
                            // If we filter by currentExtension, we might only see JSONs if that's what's selected.
                            // But when in Creator mode, we might want to see EVERYTHING or default to JSON?
                            // Let's stick to currentExtension for consistency, but maybe we need a way to reset extension?
                            // For now, assume currentExtension is valid (likely .json for scenes)
                            
                            // In Category mode, we match path (Saves/scene/...). 
                            // In Creator mode, we also respect the Category path filter if set.
                            
                            bool match = IsMatch(entry, currentPath, extensions);

                            if (match)
                            {
                                files.Add(entry);
                            }
                        }
                    }
                }
            }

            // 2. Search on local disk (System files) - Only in Category mode?
            if (activeContentType == ContentType.Category && Directory.Exists(currentPath))
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
            int startIdx = 0;
            // Handle package paths which look like "uid:/path"
            int colonIdx = entryPath.IndexOf(":/");
            if (colonIdx >= 0)
            {
                startIdx = colonIdx + 2;
            }

            // Check StartsWith using Compare to avoid substring allocation
            if (string.Compare(entryPath, startIdx, path, 0, path.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

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
            if (file.Path == selectedPath) img.color = Color.yellow;
            else img.color = Color.gray;
            if (!fileButtonImages.ContainsKey(file.Path)) fileButtonImages.Add(file.Path, img);

            Button btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(() => OnFileClick(file));

            // Thumbnail (Fill 1x1)
            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(btnGO.transform, false);
            RawImage thumbImg = thumbGO.AddComponent<RawImage>();
            thumbImg.color = new Color(0, 0, 0, 0.5f);
            RectTransform thumbRT = thumbGO.GetComponent<RectTransform>();
            thumbRT.anchorMin = Vector2.zero;
            thumbRT.anchorMax = Vector2.one;
            thumbRT.sizeDelta = Vector2.zero;
            thumbRT.offsetMin = new Vector2(3, 3);
            thumbRT.offsetMax = new Vector2(-3, -3);

            // Load thumbnail
            LoadThumbnail(file, thumbImg);

            // Card Container (Hidden by default, positions below)
            GameObject cardGO = new GameObject("Card");
            cardGO.transform.SetParent(btnGO.transform, false);
            cardGO.SetActive(false);

            RectTransform cardRT = cardGO.AddComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0, 0); // Bottom
            cardRT.anchorMax = new Vector2(1, 0); // Bottom
            cardRT.pivot = new Vector2(0.5f, 0);  // Pivot Bottom (Inside)
            cardRT.anchoredPosition = Vector2.zero;
            cardRT.sizeDelta = new Vector2(0, 0); // Width stretch

            // Dynamic height based on content
            VerticalLayoutGroup cardVLG = cardGO.AddComponent<VerticalLayoutGroup>();
            cardVLG.childControlHeight = true;
            cardVLG.childControlWidth = true;
            cardVLG.childForceExpandHeight = false;
            cardVLG.childForceExpandWidth = true;
            cardVLG.padding = new RectOffset(5, 5, 5, 5);
            
            ContentSizeFitter cardCSF = cardGO.AddComponent<ContentSizeFitter>();
            cardCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Background
            Image cardBg = cardGO.AddComponent<Image>();
            cardBg.color = new Color(0, 0, 0, 0.8f);
            cardBg.raycastTarget = false;

            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(cardGO.transform, false);
            Text labelText = labelGO.AddComponent<Text>();
            labelText.text = file.Name;
            labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = 18;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            labelText.verticalOverflow = VerticalWrapMode.Truncate;
            labelText.raycastTarget = false;
            
            // Label Layout
            LayoutElement labelLE = labelGO.AddComponent<LayoutElement>();
            labelLE.minHeight = 30;

            // Hover Logic
            UIHoverReveal hover = btnGO.AddComponent<UIHoverReveal>();
            hover.card = cardGO;
            
            // Drag Logic
            UIDraggableItem draggable = btnGO.AddComponent<UIDraggableItem>();
            draggable.FileEntry = file;
            draggable.ThumbnailImage = thumbImg;
            draggable.Panel = this;

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

            // LogUtil.Log("Loading thumbnail for " + file.Name + " from " + imgPath);

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
            if (selectedPath == file.Path) return; // Already selected
            
            // Deselect old
            if (!string.IsNullOrEmpty(selectedPath) && fileButtonImages.ContainsKey(selectedPath))
            {
                if (fileButtonImages[selectedPath] != null)
                    fileButtonImages[selectedPath].color = Color.gray;
            }
            
            selectedPath = file.Path;
            
            // Select new
            if (fileButtonImages.ContainsKey(selectedPath))
            {
                if (fileButtonImages[selectedPath] != null)
                    fileButtonImages[selectedPath].color = Color.yellow;
            }
        }
    }
}
