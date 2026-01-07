using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
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
        private Text fpsText;

        public List<Gallery.Category> categories = new List<Gallery.Category>();
        // private List<FileEntry> currentFiles = new List<FileEntry>(); // Unused

        private List<GameObject> activeButtons = new List<GameObject>();
        private Stack<GameObject> fileButtonPool = new Stack<GameObject>();
        private Stack<GameObject> navButtonPool = new Stack<GameObject>(); // NEW: Separate pool for Nav buttons
        private Dictionary<string, Image> fileButtonImages = new Dictionary<string, Image>();
        private string selectedPath = null;
        private List<GameObject> leftActiveTabButtons = new List<GameObject>();
        private List<GameObject> leftSubActiveTabButtons = new List<GameObject>(); // NEW
        private List<GameObject> rightActiveTabButtons = new List<GameObject>();
        private List<GameObject> rightSubActiveTabButtons = new List<GameObject>(); // NEW

        private string currentPath = "";
        private string currentExtension = "json";
        
        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;
        
        public enum TabSide { Hidden, Left, Right }
        public enum ContentType { Category, Creator, Status, License, Tags }

        // Configuration
        // public bool IsUndocked = false; // Removed
        public bool DragDropReplaceMode
        {
            get { return Settings.Instance != null && Settings.Instance.DragDropReplaceMode != null ? Settings.Instance.DragDropReplaceMode.Value : false; }
            set { if (Settings.Instance != null && Settings.Instance.DragDropReplaceMode != null) Settings.Instance.DragDropReplaceMode.Value = value; }
        }
        // private Toggle addToggle;
        // private Toggle replaceToggle;
        public Gallery.Category? UndockedCategory; // Removed
        public string UndockedCreator; // Removed
        public bool hasBeenPositioned = false;
        // private TabSide currentTabSide = TabSide.Right; // Unused
        private ContentType activeContentType = ContentType.Category; // Deprecated
        
        private ContentType? leftActiveContent = null;
        private ContentType? rightActiveContent = ContentType.Category;
        
        private GameObject leftTabScrollGO;
        private GameObject leftSubTabScrollGO; // NEW: For split view
        private GameObject rightTabScrollGO;
        private GameObject rightSubTabScrollGO; // NEW: For split view
        private GameObject leftTabContainerGO;
        private GameObject leftSubTabContainerGO; // NEW: For split view
        private GameObject rightTabContainerGO;
        private GameObject rightSubTabContainerGO; // NEW: For split view
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

        private Text rightReplaceBtnText;
        private Image rightReplaceBtnImage;
        private Text leftReplaceBtnText;
        private Image leftReplaceBtnImage;
        
        private Text leftSortBtnText;
        private Text rightSortBtnText;
        private GameObject leftSortBtn;
        private GameObject rightSortBtn;

        // Sub Sort/Search
        private GameObject leftSubSortBtn;
        private Text leftSubSortBtnText;
        private InputField leftSubSearchInput;
        
        private GameObject rightSubSortBtn;
        private Text rightSubSortBtnText;
        private InputField rightSubSearchInput;
        private GameObject rightSubClearBtn; // NEW
        private Text rightSubClearBtnText; // NEW
        
        private GameObject leftSubClearBtn; // NEW
        private Text leftSubClearBtnText; // NEW
        private string currentCreator = "";
        private string categoryFilter = "";
        private string creatorFilter = "";
        private string tagFilter = ""; // NEW
        private string currentLoadingGroupId = "";
        private Coroutine refreshCoroutine;
        
        private bool filterFavorite = false;
        private string nameFilter = "";
        private string nameFilterLower = "";

        // Tagging
        private List<string> currentPaths = new List<string>();
        private HashSet<string> activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void UpdateSideButtonsVisibility()
        {
            // Removed Tag Button Logic
        }

        private InputField leftSearchInput;
        private InputField rightSearchInput;
        private InputField titleSearchInput;
        private Stack<GameObject> tabButtonPool = new Stack<GameObject>();

        private List<CanvasGroup> sideButtonGroups = new List<CanvasGroup>();
        private float sideButtonsAlpha = 1f;
        private bool isResizing = false;
        private int hoverCount = 0;
        private GameObject pointerDotGO;
        private PointerEventData currentPointerData;
        private List<RectTransform> cancelDropZoneRTs = new List<RectTransform>();
        private List<Image> cancelDropZoneImages = new List<Image>();
        private List<Text> cancelDropZoneTexts = new List<Text>();
        private List<GameObject> cancelDropGroups = new List<GameObject>();
        private Color cancelZoneNormalColor = new Color(0.25f, 0.05f, 0.05f, 0.8f);
        private Color cancelZoneHoverColor = new Color(0.6f, 0.1f, 0.1f, 0.9f);

        private float fpsTimer = 0f;
        private int fpsFrames = 0;
        private const float FpsInterval = 0.5f;

        private void AddHoverDelegate(GameObject go)
        {
            var del = go.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) => {
                if (enter) hoverCount++;
                else hoverCount--;
            };
            del.OnPointerEnterEvent += (d) => {
                currentPointerData = d;
            };
        }

        private void AddRightClickDelegate(GameObject go, Action action)
        {
            var del = go.AddComponent<UIRightClickDelegate>();
            del.OnRightClick = action;
        }

        // Follow Mode Fields
        private bool followUser = true;
        private float lastFollowUpdateTime = 0f;
        private const float FollowUpdateInterval = 0.5f;
        private const float FollowRotateStepDegrees = 120f;
        private Quaternion targetFollowRotation;
        private bool isReorienting = false;
        private const float ReorientStartAngle = 20f;
        private const float ReorientStopAngle = 0.5f;
        
        private Text rightFollowBtnText;
        private Image rightFollowBtnImage;
        private Text leftFollowBtnText;
        private Image leftFollowBtnImage;
        
        public struct CreatorCacheEntry { public string Name; public int Count; }
        private List<CreatorCacheEntry> cachedCreators = new List<CreatorCacheEntry>();
        private bool creatorsCached = false;
        
        private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
        private bool categoriesCached = false;
        
        private Dictionary<string, int> tagCounts = new Dictionary<string, int>();
        private bool tagsCached = false;

        // Pagination
        private int currentPage = 0;
        private int itemsPerPage = 200;
        private Text paginationText;
        private GameObject paginationPrevBtn;
        private GameObject paginationNextBtn;

        // Define colors for different content types
        public static readonly Color ColorCategory = new Color(0.7f, 0.2f, 0.2f, 1f); // Desaturated Red
        public static readonly Color ColorCreator = new Color(0.3f, 0.6f, 0.3f, 1f); // Desaturated Green
        public static readonly Color ColorLicense = new Color(1f, 0f, 1f, 1f); // Magenta

        public string GetCurrentPath() => currentPath;
        public string GetCurrentExtension() => currentExtension;
        public string GetCurrentCreator() => currentCreator;
        public string GetTitle() => titleText != null ? titleText.text : "";
        public RectTransform GetBackgroundRT() => backgroundBoxGO != null ? backgroundBoxGO.GetComponent<RectTransform>() : null;

        public ContentType? GetLeftActiveContent() => leftActiveContent;
        public ContentType? GetRightActiveContent() => rightActiveContent;

        public void SetLeftActiveContent(ContentType? type)
        {
            leftActiveContent = type;
            UpdateLayout();
            UpdateTabs();
        }

        public void SetRightActiveContent(ContentType? type)
        {
            rightActiveContent = type;
            UpdateLayout();
            UpdateTabs();
        }

        public void SetFilters(string path, string extension, string creator)
        {
            currentPath = path;
            currentExtension = extension;
            currentCreator = creator;
            creatorsCached = false;
            tagsCached = false;
            categoriesCached = false;
        }

        void OnDestroy()
        {
            if (canvas != null)
            {
                if (SuperController.singleton != null)
                {
                    SuperController.singleton.RemoveCanvas(canvas);
                }
                Destroy(canvas.gameObject);
            }
            // Remove from manager if needed
            if (Gallery.singleton != null)
            {
                Gallery.singleton.RemovePanel(this);
            }
        }

        public void Init()
        {
            if (canvas != null) return;

            // IsUndocked = isUndocked; // Removed
            // string nameSuffix = isUndocked ? "_Undocked" : "";
            GameObject canvasGO = new GameObject("VPB_GalleryCanvas");
            canvasGO.layer = 5; // UI layer
            canvas = canvasGO.AddComponent<Canvas>();
            RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(1200, 800);
            
            // Note: In VaM VR, standard GraphicRaycaster often conflicts or is ignored.
            // We rely on BoxCollider for the main panel background hit detection.
            // Adding GraphicRaycaster but disabling it by default unless needed for non-VR mouse interaction?
            // Actually, let's keep it simple: relying on BoxCollider with offset seems to be the intended path for VaM UI panels.
            // But user said offset didn't work. The key might be that the collider MUST be there for the laser to "stop"
            // but the "dimming" means it thinks it's penetrating.
            // The resize handles work because they have small colliders or none?
            // Resize handles in this code do NOT have colliders added explicitly! They just use Image + UIHoverBorder/UIHoverColor.
            // So if resize handles work WITHOUT collider, we should aim for that.
            
            // Standard GraphicRaycaster is needed for UI elements without colliders.
            GraphicRaycaster gr = canvasGO.AddComponent<GraphicRaycaster>();
            gr.ignoreReversedGraphics = true;
            
            if (SuperController.singleton != null)
                SuperController.singleton.AddCanvas(canvas);

            if (Application.isPlaying)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Camera.main;
                canvas.sortingOrder = -10000;
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
            
            // Add UIHoverColor (This handles hover/drag color changes AND sets raycast target properly)
            UIHoverColor bgHover = backgroundBoxGO.AddComponent<UIHoverColor>();
            bgHover.targetImage = backgroundBoxGO.GetComponent<Image>();
            bgHover.normalColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            bgHover.hoverColor = new Color(0.1f, 0.1f, 0.1f, 0.9f); // Same color for now, but ensures interaction
            
            // AddHoverDelegate
            AddHoverDelegate(backgroundBoxGO);
            
            UIDraggable dragger = backgroundBoxGO.AddComponent<UIDraggable>();
            dragger.target = canvasGO.transform;

            // Title Bar
            GameObject titleBarGO = new GameObject("TitleBar");
            titleBarGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform titleBarRT = titleBarGO.AddComponent<RectTransform>();
            titleBarRT.anchorMin = new Vector2(0, 1);
            titleBarRT.anchorMax = new Vector2(1, 1);
            titleBarRT.pivot = new Vector2(0.5f, 1);
            titleBarRT.anchoredPosition = new Vector2(0, 0);
            titleBarRT.sizeDelta = new Vector2(0, 70);

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(titleBarGO.transform, false);
            titleText = titleGO.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 28;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleLeft;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 0.5f);
            titleRT.anchorMax = new Vector2(0, 0.5f);
            titleRT.pivot = new Vector2(0, 0.5f);
            titleRT.anchoredPosition = new Vector2(15, 0);
            titleRT.sizeDelta = new Vector2(300, 40);

            GameObject fpsGO = new GameObject("FPS");
            fpsGO.transform.SetParent(titleBarGO.transform, false);
            fpsText = fpsGO.AddComponent<Text>();
            fpsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            fpsText.fontSize = 20;
            fpsText.color = Color.white;
            fpsText.alignment = TextAnchor.MiddleRight;
            RectTransform fpsRT = fpsGO.GetComponent<RectTransform>();
            fpsRT.anchorMin = new Vector2(1, 0.5f);
            fpsRT.anchorMax = new Vector2(1, 0.5f);
            fpsRT.pivot = new Vector2(1, 0.5f);
            fpsRT.anchoredPosition = new Vector2(-70, 0);
            fpsRT.sizeDelta = new Vector2(100, 40);

            titleSearchInput = CreateSearchInput(titleBarGO, 300f, (val) => {
                SetNameFilter(val);
            });
            RectTransform titleSearchRT = titleSearchInput.GetComponent<RectTransform>();
            titleSearchRT.anchorMin = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchorMax = new Vector2(0.5f, 0.5f);
            titleSearchRT.pivot = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchoredPosition = new Vector2(0, 0);
            titleSearchRT.sizeDelta = new Vector2(300, 40);

            // File Sort Button
            GameObject fileSortBtn = UI.CreateUIButton(titleBarGO, 40, 40, "Az↑", 16, 0, 0, AnchorPresets.middleCenter, null);
            fileSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            fileSortBtn.GetComponentInChildren<Text>().color = Color.white;
            RectTransform fileSortRT = fileSortBtn.GetComponent<RectTransform>();
            fileSortRT.anchorMin = new Vector2(0.5f, 0.5f);
            fileSortRT.anchorMax = new Vector2(0.5f, 0.5f);
            fileSortRT.pivot = new Vector2(0.5f, 0.5f);
            fileSortRT.anchoredPosition = new Vector2(175, 0); // To the right of search
            
            Text fileSortText = fileSortBtn.GetComponentInChildren<Text>();
            
            Button fileSortButton = fileSortBtn.GetComponent<Button>();
            fileSortButton.onClick.RemoveAllListeners();
            fileSortButton.onClick.AddListener(() => CycleSort("Files", fileSortText));
            
            AddRightClickDelegate(fileSortBtn, () => ToggleSortDirection("Files", fileSortText));
            
            // Init File Sort State
            UpdateSortButtonText(fileSortText, GetSortState("Files"));

            // Tab Area - Create for all panels so undocked can clone/filter
            if (true)
            {
                float tabAreaWidth = 220f;
                
                // 1. Right Tab Area
                rightTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchRight, tabAreaWidth, 0, Vector2.zero);
                RectTransform rightTabRT = rightTabScrollGO.GetComponent<RectTransform>();
                rightTabRT.anchorMin = new Vector2(1, 0);
                rightTabRT.anchorMax = new Vector2(1, 1);
                rightTabRT.offsetMin = new Vector2(-tabAreaWidth - 10, 20); 
                rightTabRT.offsetMax = new Vector2(-10, -90); 
                
                rightTabContainerGO = rightTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                VerticalLayoutGroup rightVlg = rightTabContainerGO.GetComponent<VerticalLayoutGroup>();
                rightVlg.spacing = 2;
                rightTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);

                // 1b. Right Sub Tab Area (For Tags split view)
                rightSubTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchRight, tabAreaWidth, 0, Vector2.zero);
                RectTransform rightSubTabRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                rightSubTabRT.anchorMin = new Vector2(1, 0);
                rightSubTabRT.anchorMax = new Vector2(1, 0.5f); // Bottom half default
                rightSubTabRT.offsetMin = new Vector2(-tabAreaWidth - 10, 20);
                rightSubTabRT.offsetMax = new Vector2(-10, -45);
                
                rightSubTabContainerGO = rightSubTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                VerticalLayoutGroup rightSubVlg = rightSubTabContainerGO.GetComponent<VerticalLayoutGroup>();
                rightSubVlg.spacing = 2;
                rightSubTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);
                rightSubTabScrollGO.SetActive(false); // Hidden by default

                // Right Sub Sort Button
                rightSubSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topRight, null);
                rightSubSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                rightSubSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform rsSubRT = rightSubSortBtn.GetComponent<RectTransform>();
                rsSubRT.anchorMin = new Vector2(1, 0.5f);
                rsSubRT.anchorMax = new Vector2(1, 0.5f);
                rsSubRT.pivot = new Vector2(1, 1);
                rsSubRT.anchoredPosition = new Vector2(-190, -10); // Below split line
                
                rightSubSortBtnText = rightSubSortBtn.GetComponentInChildren<Text>();
                Button rightSubSortButton = rightSubSortBtn.GetComponent<Button>();
                rightSubSortButton.onClick.RemoveAllListeners();
                rightSubSortButton.onClick.AddListener(() => {
                    CycleSort("Tags", rightSubSortBtnText);
                });
                AddRightClickDelegate(rightSubSortBtn, () => {
                    ToggleSortDirection("Tags", rightSubSortBtnText);
                });

                // Right Sub Search
                rightSubSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    tagFilter = val;
                    UpdateTabs();
                });
                RectTransform rSubSearchRT = rightSubSearchInput.GetComponent<RectTransform>();
                rSubSearchRT.anchorMin = new Vector2(1, 0.5f);
                rSubSearchRT.anchorMax = new Vector2(1, 0.5f);
                rSubSearchRT.pivot = new Vector2(1, 1);
                rSubSearchRT.anchoredPosition = new Vector2(-10, -10);
                
                // Right Sub Clear Button
                rightSubClearBtn = UI.CreateUIButton(backgroundBoxGO, tabAreaWidth, 35, "Clear Selected", 14, 0, 0, AnchorPresets.bottomRight, () => {
                    activeTags.Clear();
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                });
                rightSubClearBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // Dark Red
                rightSubClearBtnText = rightSubClearBtn.GetComponentInChildren<Text>();
                rightSubClearBtnText.color = Color.white;
                
                RectTransform rSubClearRT = rightSubClearBtn.GetComponent<RectTransform>();
                rSubClearRT.anchorMin = new Vector2(1, 0);
                rSubClearRT.anchorMax = new Vector2(1, 0);
                rSubClearRT.pivot = new Vector2(1, 0);
                rSubClearRT.anchoredPosition = new Vector2(-10, 10);
                rightSubClearBtn.SetActive(false);

                // Init Sub Sort Text
                UpdateSortButtonText(rightSubSortBtnText, GetSortState("Tags"));
                
                rightSubSortBtn.SetActive(false);
                rightSubSearchInput.gameObject.SetActive(false);

                // Right Sort Button
                rightSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topRight, null);
                rightSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                rightSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform rsRT = rightSortBtn.GetComponent<RectTransform>();
                rsRT.anchorMin = new Vector2(1, 1);
                rsRT.anchorMax = new Vector2(1, 1);
                rsRT.pivot = new Vector2(1, 1);
                rsRT.anchoredPosition = new Vector2(-190, -55); // Left of Search
                
                rightSortBtnText = rightSortBtn.GetComponentInChildren<Text>();
                Button rightSortButton = rightSortBtn.GetComponent<Button>();
                rightSortButton.onClick.RemoveAllListeners();
                rightSortButton.onClick.AddListener(() => {
                    if (rightActiveContent.HasValue) CycleSort(rightActiveContent.Value.ToString(), rightSortBtnText);
                });
                AddRightClickDelegate(rightSortBtn, () => {
                    if (rightActiveContent.HasValue) ToggleSortDirection(rightActiveContent.Value.ToString(), rightSortBtnText);
                });

                rightSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    if (rightActiveContent == ContentType.Category) categoryFilter = val;
                    else if (rightActiveContent == ContentType.Creator) creatorFilter = val;
                    UpdateTabs();
                });
                RectTransform rSearchRT = rightSearchInput.GetComponent<RectTransform>();
                rSearchRT.anchorMin = new Vector2(1, 1);
                rSearchRT.anchorMax = new Vector2(1, 1);
                rSearchRT.pivot = new Vector2(1, 1);
                rSearchRT.anchoredPosition = new Vector2(-10, -55);

                // 2. Left Tab Area
                leftTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchLeft, tabAreaWidth, 0, Vector2.zero);
                RectTransform leftTabRT = leftTabScrollGO.GetComponent<RectTransform>();
                leftTabRT.anchorMin = new Vector2(0, 0);
                leftTabRT.anchorMax = new Vector2(0, 1);
                leftTabRT.offsetMin = new Vector2(10, 20);
                leftTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -90);
                
                leftTabContainerGO = leftTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                VerticalLayoutGroup leftVlg = leftTabContainerGO.GetComponent<VerticalLayoutGroup>();
                leftVlg.spacing = 2;
                leftTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);

                // 2b. Left Sub Tab Area (For Tags split view)
                leftSubTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchLeft, tabAreaWidth, 0, Vector2.zero);
                RectTransform leftSubTabRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                leftSubTabRT.anchorMin = new Vector2(0, 0);
                leftSubTabRT.anchorMax = new Vector2(0, 0.5f); // Bottom half default
                leftSubTabRT.offsetMin = new Vector2(10, 20);
                leftSubTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -45);
                
                leftSubTabContainerGO = leftSubTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                VerticalLayoutGroup leftSubVlg = leftSubTabContainerGO.GetComponent<VerticalLayoutGroup>();
                leftSubVlg.spacing = 2;
                leftSubTabContainerGO.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 0);
                leftSubTabScrollGO.SetActive(false); // Hidden by default

                // Left Sub Sort Button
                leftSubSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topLeft, null);
                leftSubSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                leftSubSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform lsSubRT = leftSubSortBtn.GetComponent<RectTransform>();
                lsSubRT.anchorMin = new Vector2(0, 0.5f);
                lsSubRT.anchorMax = new Vector2(0, 0.5f);
                lsSubRT.pivot = new Vector2(0, 1);
                lsSubRT.anchoredPosition = new Vector2(10, -10); // Below split line
                
                leftSubSortBtnText = leftSubSortBtn.GetComponentInChildren<Text>();
                Button leftSubSortButton = leftSubSortBtn.GetComponent<Button>();
                leftSubSortButton.onClick.RemoveAllListeners();
                leftSubSortButton.onClick.AddListener(() => {
                    CycleSort("Tags", leftSubSortBtnText);
                });
                AddRightClickDelegate(leftSubSortBtn, () => {
                    ToggleSortDirection("Tags", leftSubSortBtnText);
                });

                // Left Sub Search
                leftSubSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    tagFilter = val;
                    UpdateTabs();
                });
                RectTransform lSubSearchRT = leftSubSearchInput.GetComponent<RectTransform>();
                lSubSearchRT.anchorMin = new Vector2(0, 0.5f);
                lSubSearchRT.anchorMax = new Vector2(0, 0.5f);
                lSubSearchRT.pivot = new Vector2(0, 1);
                lSubSearchRT.anchoredPosition = new Vector2(55, -10);

                // Left Sub Clear Button
                leftSubClearBtn = UI.CreateUIButton(backgroundBoxGO, tabAreaWidth, 35, "Clear Selected", 14, 0, 0, AnchorPresets.bottomLeft, () => {
                    activeTags.Clear();
                    currentPage = 0;
                    RefreshFiles();
                    UpdateTabs();
                });
                leftSubClearBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // Dark Red
                leftSubClearBtnText = leftSubClearBtn.GetComponentInChildren<Text>();
                leftSubClearBtnText.color = Color.white;
                
                RectTransform lSubClearRT = leftSubClearBtn.GetComponent<RectTransform>();
                lSubClearRT.anchorMin = new Vector2(0, 0);
                lSubClearRT.anchorMax = new Vector2(0, 0);
                lSubClearRT.pivot = new Vector2(0, 0);
                lSubClearRT.anchoredPosition = new Vector2(10, 10);
                leftSubClearBtn.SetActive(false);

                // Init Sub Sort Text
                UpdateSortButtonText(leftSubSortBtnText, GetSortState("Tags"));

                leftSubSortBtn.SetActive(false);
                leftSubSearchInput.gameObject.SetActive(false);

                // Left Sort Button
                leftSortBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, "Az↑", 14, 0, 0, AnchorPresets.topLeft, null);
                leftSortBtn.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                leftSortBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform lsRT = leftSortBtn.GetComponent<RectTransform>();
                lsRT.anchorMin = new Vector2(0, 1);
                lsRT.anchorMax = new Vector2(0, 1);
                lsRT.pivot = new Vector2(0, 1);
                lsRT.anchoredPosition = new Vector2(10, -55);
                
                leftSortBtnText = leftSortBtn.GetComponentInChildren<Text>();
                Button leftSortButton = leftSortBtn.GetComponent<Button>();
                leftSortButton.onClick.RemoveAllListeners();
                leftSortButton.onClick.AddListener(() => {
                    if (leftActiveContent.HasValue) CycleSort(leftActiveContent.Value.ToString(), leftSortBtnText);
                });
                AddRightClickDelegate(leftSortBtn, () => {
                    if (leftActiveContent.HasValue) ToggleSortDirection(leftActiveContent.Value.ToString(), leftSortBtnText);
                });

                leftSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, (val) => {
                    if (leftActiveContent == ContentType.Category) categoryFilter = val;
                    else if (leftActiveContent == ContentType.Creator) creatorFilter = val;
                    UpdateTabs();
                });
                RectTransform lSearchRT = leftSearchInput.GetComponent<RectTransform>();
                lSearchRT.anchorMin = new Vector2(0, 1);
                lSearchRT.anchorMax = new Vector2(0, 1);
                lSearchRT.pivot = new Vector2(0, 1);
                lSearchRT.anchoredPosition = new Vector2(55, -55);

                // Right Button Container
                GameObject rightButtonsContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleRight, 130, 430, new Vector2(120, 0));
                sideButtonGroups.Add(rightButtonsContainer.AddComponent<CanvasGroup>());

                // Right Toggle Buttons
                int btnFontSize = 20;
                float btnWidth = 120;
                float btnHeight = 50;
                float spacing = 60f;
                float startY = 180f;

                // Follow (Top)
                GameObject rightFollowBtn = UI.CreateUIButton(rightButtonsContainer, btnWidth, btnHeight, "Static", btnFontSize, 0, startY, AnchorPresets.centre, ToggleFollowMode);
                rightFollowBtnImage = rightFollowBtn.GetComponent<Image>();
                rightFollowBtnText = rightFollowBtn.GetComponentInChildren<Text>();
                rightFollowBtnImage.color = Color.gray;

                // Clone (Gray)
                GameObject rightCloneBtn = UI.CreateUIButton(rightButtonsContainer, btnWidth, btnHeight, "Clone", btnFontSize, 0, startY - spacing, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, true);
                });
                rightCloneBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 1f);

                // Category (Red)
                GameObject rightCatBtn = UI.CreateUIButton(rightButtonsContainer, btnWidth, btnHeight, "Category", btnFontSize, 0, startY - spacing * 2, AnchorPresets.centre, () => ToggleRight(ContentType.Category));
                rightCategoryBtnImage = rightCatBtn.GetComponent<Image>();
                rightCategoryBtnImage.color = ColorCategory;
                rightCategoryBtnText = rightCatBtn.GetComponentInChildren<Text>();
                // rightCategoryBtnText.text = "<"; // Set by create
                
                // Creator (Green)
                GameObject rightCreatorBtn = UI.CreateUIButton(rightButtonsContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 3, AnchorPresets.centre, () => ToggleRight(ContentType.Creator));
                rightCreatorBtnImage = rightCreatorBtn.GetComponent<Image>();
                rightCreatorBtnImage.color = ColorCreator;
                rightCreatorBtnText = rightCreatorBtn.GetComponentInChildren<Text>();
                // rightCreatorBtnText.text = ">"; // Set by create

                // Status (Blue) - NEW
                GameObject rightStatusBtn = UI.CreateUIButton(rightButtonsContainer, btnWidth, btnHeight, "Status", btnFontSize, 0, startY - spacing * 4, AnchorPresets.centre, () => ToggleRight(ContentType.Status));
                rightStatusBtn.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.7f, 1f);

                // Replace Toggle (Right)
                GameObject rightReplaceBtn = UI.CreateUIButton(rightButtonsContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 5, AnchorPresets.centre, ToggleReplaceMode);
                rightReplaceBtnImage = rightReplaceBtn.GetComponent<Image>();
                rightReplaceBtnText = rightReplaceBtn.GetComponentInChildren<Text>();

                // Undo (Right)
                GameObject rightUndoBtn = UI.CreateUIButton(rightButtonsContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 6, AnchorPresets.centre, Undo);
                rightUndoBtn.GetComponent<Image>().color = new Color(0.6f, 0.4f, 0.2f, 1f); // Brown/Orange
                rightUndoBtn.GetComponentInChildren<Text>().color = Color.white;

                // Left Button Container
                GameObject leftButtonsContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.01f), AnchorPresets.middleLeft, 130, 430, new Vector2(-120, 0));
                sideButtonGroups.Add(leftButtonsContainer.AddComponent<CanvasGroup>());

                // Left Toggle Buttons
                // Follow (Top)
                GameObject leftFollowBtn = UI.CreateUIButton(leftButtonsContainer, btnWidth, btnHeight, "Static", btnFontSize, 0, startY, AnchorPresets.centre, ToggleFollowMode);
                leftFollowBtnImage = leftFollowBtn.GetComponent<Image>();
                leftFollowBtnText = leftFollowBtn.GetComponentInChildren<Text>();
                leftFollowBtnImage.color = Color.gray;

                // Clone (Gray)
                GameObject leftCloneBtn = UI.CreateUIButton(leftButtonsContainer, btnWidth, btnHeight, "Clone", btnFontSize, 0, startY - spacing, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, false);
                });
                leftCloneBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 1f);

                // Category (Red)
                GameObject leftCatBtn = UI.CreateUIButton(leftButtonsContainer, btnWidth, btnHeight, "Category", btnFontSize, 0, startY - spacing * 2, AnchorPresets.centre, () => ToggleLeft(ContentType.Category));
                leftCategoryBtnImage = leftCatBtn.GetComponent<Image>();
                leftCategoryBtnImage.color = ColorCategory;
                leftCategoryBtnText = leftCatBtn.GetComponentInChildren<Text>();
                // leftCategoryBtnText.text = "<"; // Set by create
                
                // Creator (Green)
                GameObject leftCreatorBtn = UI.CreateUIButton(leftButtonsContainer, btnWidth, btnHeight, "Creator", btnFontSize, 0, startY - spacing * 3, AnchorPresets.centre, () => ToggleLeft(ContentType.Creator));
                leftCreatorBtnImage = leftCreatorBtn.GetComponent<Image>();
                leftCreatorBtnImage.color = ColorCreator;
                leftCreatorBtnText = leftCreatorBtn.GetComponentInChildren<Text>();
                // leftCreatorBtnText.text = "<"; // Set by create

                // Status (Blue) - NEW
                GameObject leftStatusBtn = UI.CreateUIButton(leftButtonsContainer, btnWidth, btnHeight, "Status", btnFontSize, 0, startY - spacing * 4, AnchorPresets.centre, () => ToggleLeft(ContentType.Status));
                leftStatusBtn.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.7f, 1f);

                // Replace Toggle (Left)
                GameObject leftReplaceBtn = UI.CreateUIButton(leftButtonsContainer, btnWidth, btnHeight, "Add", btnFontSize, 0, startY - spacing * 5, AnchorPresets.centre, ToggleReplaceMode);
                leftReplaceBtnImage = leftReplaceBtn.GetComponent<Image>();
                leftReplaceBtnText = leftReplaceBtn.GetComponentInChildren<Text>();

                // Undo (Left)
                GameObject leftUndoBtn = UI.CreateUIButton(leftButtonsContainer, btnWidth, btnHeight, "Undo", btnFontSize, 0, startY - spacing * 6, AnchorPresets.centre, Undo);
                leftUndoBtn.GetComponent<Image>().color = new Color(0.6f, 0.4f, 0.2f, 1f); // Brown/Orange
                leftUndoBtn.GetComponentInChildren<Text>().color = Color.white;

                CreateCancelButtonsGroup(rightButtonsContainer, btnWidth, btnHeight, startY - spacing * 8 - 80f);
                CreateCancelButtonsGroup(leftButtonsContainer, btnWidth, btnHeight, startY - spacing * 8 - 80f);
            }

            // Add Hover Delegates to all side buttons and containers
            foreach (var cg in sideButtonGroups)
            {
                if (cg != null)
                {
                    AddHoverDelegate(cg.gameObject);
                    foreach (Transform t in cg.transform)
                    {
                        AddHoverDelegate(t.gameObject);
                    }
                }
            }

            UpdateReplaceButtonState();
            UpdateFollowButtonState();

            // Content Area
            float rightPadding = -20; // Default padding, updated by UpdateLayout
            
            GameObject scrollableGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0.2f, 0.2f, 0.2f, 0.5f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            AddHoverDelegate(scrollableGO); // Add this
            contentScrollRT = scrollableGO.GetComponent<RectTransform>();
            contentScrollRT.offsetMin = new Vector2(20, 20);
            contentScrollRT.offsetMax = new Vector2(rightPadding, -55);

            scrollRect = scrollableGO.GetComponent<ScrollRect>();
            contentGO = scrollRect.content.gameObject;

            // Status Bar Removed as per request

            // Set initial state - Default to collapsed sidebars unless set by caller
            {
                // Default new panes to Category on right (Main Panel behavior)
                // The user asked for "single type of pane".
                // If we want them all to behave like the main panel, they should start with Category open?
                // Or maybe started closed?
                // The main panel started with rightActiveContent = ContentType.Category.
                // Let's stick to that for consistency.
                rightActiveContent = ContentType.Category;
                leftActiveContent = null;
            }
            
            UpdateLayout();

            // Grid Layout
            VerticalLayoutGroup vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) DestroyImmediate(vlg);

            GridLayoutGroup grid = contentGO.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(10, 10);
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.childAlignment = TextAnchor.UpperLeft;

            UIGridAdaptive adaptive = contentGO.AddComponent<UIGridAdaptive>();
            adaptive.grid = grid;
            adaptive.minSize = 180f;
            adaptive.maxSize = 250f;
            adaptive.spacing = 10f;

            // Close button - Rendered last to be on top
            GameObject closeBtn = UI.CreateUIButton(backgroundBoxGO, 50, 50, "X", 30, 0, 0, AnchorPresets.topRight, () => {
                if (Gallery.singleton != null) Gallery.singleton.RemovePanel(this);
                
                // Destroy canvas explicitly
                if (canvas != null)
                {
                    if (SuperController.singleton != null) SuperController.singleton.RemoveCanvas(canvas);
                    Destroy(canvas.gameObject);
                }
                
                Destroy(this.gameObject);
            });
            closeBtn.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.7f, 1f);
            AddHoverDelegate(closeBtn);

            SetLayerRecursive(canvasGO, 5); // UI layer for children
            
            // Register with manager
            if (Gallery.singleton != null)
            {
                Gallery.singleton.AddPanel(this);
            }

            CreateResizeHandles();

            // Pointer Dot
            pointerDotGO = new GameObject("PointerDot");
            pointerDotGO.transform.SetParent(backgroundBoxGO.transform, false);
            Image dotImg = pointerDotGO.AddComponent<Image>();
            dotImg.color = Color.magenta;
            dotImg.raycastTarget = false;
            RectTransform dotRT = pointerDotGO.GetComponent<RectTransform>();
            dotRT.sizeDelta = new Vector2(10, 10);
            pointerDotGO.SetActive(false);

            CreatePaginationControls();

            Hide();
        }

        private void CreatePaginationControls()
        {
            // Pagination Container (Bottom Center)
            GameObject pageContainer = new GameObject("PaginationContainer");
            pageContainer.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform pageRT = pageContainer.AddComponent<RectTransform>();
            pageRT.anchorMin = new Vector2(0.5f, 0);
            pageRT.anchorMax = new Vector2(0.5f, 0);
            pageRT.pivot = new Vector2(0.5f, 0);
            pageRT.anchoredPosition = new Vector2(0, 10);
            pageRT.sizeDelta = new Vector2(400, 40);

            // Prev Button
            paginationPrevBtn = UI.CreateUIButton(pageContainer, 40, 40, "<", 20, -80, 0, AnchorPresets.middleCenter, PrevPage);
            
            // Text
            GameObject textGO = new GameObject("PageText");
            textGO.transform.SetParent(pageContainer.transform, false);
            paginationText = textGO.AddComponent<Text>();
            paginationText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            paginationText.fontSize = 18;
            paginationText.color = Color.white;
            paginationText.alignment = TextAnchor.MiddleCenter;
            paginationText.text = "1 / 1";
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0.5f, 0.5f);
            textRT.anchorMax = new Vector2(0.5f, 0.5f);
            textRT.pivot = new Vector2(0.5f, 0.5f);
            textRT.anchoredPosition = new Vector2(0, 0);
            textRT.sizeDelta = new Vector2(100, 40);

            // Next Button
            paginationNextBtn = UI.CreateUIButton(pageContainer, 40, 40, ">", 20, 80, 0, AnchorPresets.middleCenter, NextPage);

            // Hover support
            AddHoverDelegate(paginationPrevBtn);
            AddHoverDelegate(paginationNextBtn);
        }

        private void NextPage()
        {
            currentPage++;
            RefreshFiles();
        }

        private void PrevPage()
        {
            if (currentPage > 0)
            {
                currentPage--;
                RefreshFiles(false, true);
            }
        }

        private void CreateCancelButtonsGroup(GameObject parent, float btnWidth, float btnHeight, float yOffset)
        {
            float tallHeight = btnHeight * 3f + 8f;
            GameObject container = UI.AddChildGOImage(parent, new Color(0, 0, 0, 0.02f), AnchorPresets.centre, btnWidth, tallHeight, new Vector2(0, yOffset));
            container.name = "CancelButtons";
            VerticalLayoutGroup vlg = container.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 0f;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            GameObject btn = UI.CreateUIButton(container, btnWidth, tallHeight, "Release\nHere To\nCancel", 18, 0, 0, AnchorPresets.centre, null);
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(btnWidth, tallHeight);
            LayoutElement le = btn.AddComponent<LayoutElement>();
            le.preferredHeight = tallHeight;
            le.preferredWidth = btnWidth;

            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = cancelZoneNormalColor;
            Text t = btn.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = new Color(1f, 1f, 1f, 0.9f);
                t.alignment = TextAnchor.MiddleCenter;
                t.supportRichText = false;
            }

            cancelDropZoneRTs.Add(rt);
            cancelDropZoneImages.Add(img);
            cancelDropZoneTexts.Add(t);
            AddHoverDelegate(btn);

            AddHoverDelegate(container);
            container.SetActive(false);
            cancelDropGroups.Add(container);
        }

        private string dragStatusMsg = null;

        public void SetStatus(string msg)
        {
            if (string.IsNullOrEmpty(msg)) dragStatusMsg = null;
            else dragStatusMsg = msg;
        }

        public void ShowCancelDropZone(bool show)
        {
            foreach (var g in cancelDropGroups)
            {
                if (g != null) g.SetActive(show);
            }
            if (show) UpdateCancelDropZoneVisual(-1);
        }

        public bool IsPointerOverCancelDropZone(PointerEventData eventData)
        {
            if (eventData == null || cancelDropZoneRTs.Count == 0) return false;
            Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : Camera.main;
            int hoveredIndex = -1;
            for (int i = 0; i < cancelDropZoneRTs.Count; i++)
            {
                RectTransform rt = cancelDropZoneRTs[i];
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, eventData.position, cam))
                {
                    hoveredIndex = i;
                    break;
                }
            }
            UpdateCancelDropZoneVisual(hoveredIndex);
            return hoveredIndex >= 0;
        }

        private void UpdateCancelDropZoneVisual(int hoveredIndex)
        {
            for (int i = 0; i < cancelDropZoneImages.Count; i++)
            {
                Image img = cancelDropZoneImages[i];
                if (img != null) img.color = (i == hoveredIndex) ? cancelZoneHoverColor : cancelZoneNormalColor;
            }
            for (int i = 0; i < cancelDropZoneTexts.Count; i++)
            {
                Text t = cancelDropZoneTexts[i];
                if (t != null) t.color = (i == hoveredIndex) ? Color.white : new Color(1f, 1f, 1f, 0.9f);
            }
        }

        private Camera _cachedCamera;
        void Update()
        {
            // Sorting Logic - DISABLED to prevent FPS drops
            /*
            if (canvas != null && canvas.gameObject.activeSelf)
            {
                if (_cachedCamera == null) _cachedCamera = canvas.worldCamera;
                if (_cachedCamera == null) _cachedCamera = Camera.main;

                // Throttled and reduced sensitivity to prevent constant canvas rebuilds
                if (_cachedCamera != null && Time.time - lastSortUpdate > SortUpdateInterval)
                {
                    lastSortUpdate = Time.time;
                    float dist = Vector3.Distance(_cachedCamera.transform.position, canvas.transform.position);
                    int order = -10000 - (int)(dist * 10); // 10cm steps
                    if (canvas.sortingOrder != order)
                    {
                        canvas.sortingOrder = order;
                    }
                }
            }
            */

            // Status Bar Logic
            if (dragStatusMsg != null)
            {
                if (statusBarText != null) statusBarText.text = dragStatusMsg;
            }
            else
            {
                if (IsVisible && statusBarText != null && !UnityEngine.XR.XRSettings.enabled)
                {
                     string msg;
                     Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : Camera.main;
                     if (cam == null) return;
                     
                     SceneUtils.DetectAtom(Input.mousePosition, cam, out msg);
                     statusBarText.text = msg;
                }
            }

            // FPS (lightweight, ~2Hz)
            if (fpsText != null)
            {
                fpsTimer += Time.unscaledDeltaTime;
                fpsFrames++;
                if (fpsTimer >= FpsInterval)
                {
                    float fps = fpsFrames / fpsTimer;
                    fpsText.text = string.Format("{0:0} FPS", fps);
                    fpsTimer = 0f;
                    fpsFrames = 0;
                }
            }

            // Side Buttons Auto-Hide Logic
            bool showSideButtons = hoverCount > 0;
            
            bool enableFade = (Settings.Instance != null && Settings.Instance.EnableGalleryFade != null) ? Settings.Instance.EnableGalleryFade.Value : true;
            float targetAlpha = (showSideButtons || isResizing || !enableFade) ? 1.0f : 0.0f;
            if (Mathf.Abs(sideButtonsAlpha - targetAlpha) > 0.01f)
            {
                sideButtonsAlpha = Mathf.Lerp(sideButtonsAlpha, targetAlpha, Time.deltaTime * 15.0f);
                foreach (var cg in sideButtonGroups)
                {
                    if (cg != null) cg.alpha = sideButtonsAlpha;
                }
            }

            if (followUser && canvas != null)
            {
                if (_cachedCamera == null || !_cachedCamera.isActiveAndEnabled)
                    _cachedCamera = Camera.main;

                if (_cachedCamera != null)
                {
                    float now = Time.unscaledTime;
                    if (lastFollowUpdateTime <= 0f || now - lastFollowUpdateTime >= FollowUpdateInterval)
                    {
                        lastFollowUpdateTime = now;
                        Vector3 lookDir = canvas.transform.position - _cachedCamera.transform.position;
                        
                        if (lookDir.sqrMagnitude > 0.001f)
                        {
                            targetFollowRotation = Quaternion.LookRotation(lookDir, Vector3.up);

                            float angleDiff = Quaternion.Angle(canvas.transform.rotation, targetFollowRotation);
                            if (!isReorienting && angleDiff > ReorientStartAngle) isReorienting = true;

                            if (isReorienting)
                            {
                                canvas.transform.rotation = Quaternion.RotateTowards(canvas.transform.rotation, targetFollowRotation, FollowRotateStepDegrees);
                                if (Quaternion.Angle(canvas.transform.rotation, targetFollowRotation) < ReorientStopAngle) isReorienting = false;
                            }
                        }
                    }
                }
            }

            // Pointer Dot Logic
            if (pointerDotGO != null)
            {
                if (hoverCount > 0 && currentPointerData != null && currentPointerData.pointerCurrentRaycast.isValid)
                {
                    if (!pointerDotGO.activeSelf) pointerDotGO.SetActive(true);
                    // Use standard 5mm offset to prevent z-fighting
                    pointerDotGO.transform.position = currentPointerData.pointerCurrentRaycast.worldPosition - canvas.transform.forward * 0.005f;
                    pointerDotGO.transform.SetAsLastSibling(); 
                }
                else
                {
                    if (pointerDotGO.activeSelf) pointerDotGO.SetActive(false);
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

        private void UpdateReplaceButtonState()
        {
            string text = DragDropReplaceMode ? "Replace" : "Add";
            Color color = DragDropReplaceMode ? new Color(0.8f, 0.2f, 0.2f, 1f) : new Color(0.2f, 0.6f, 0.2f, 1f);

            if (rightReplaceBtnText != null) rightReplaceBtnText.text = text;
            if (rightReplaceBtnImage != null) rightReplaceBtnImage.color = color;
            
            if (leftReplaceBtnText != null) leftReplaceBtnText.text = text;
            if (leftReplaceBtnImage != null) leftReplaceBtnImage.color = color;
        }

        private void ToggleReplaceMode()
        {
            DragDropReplaceMode = !DragDropReplaceMode;
            UpdateReplaceButtonState();
        }

        private void UpdateLayout()
        {
            if (!creatorsCached) CacheCreators();
            if (!categoriesCached) CacheCategoryCounts();

            if (contentScrollRT == null) return;
            
            float leftOffset = 20;
            float rightOffset = -20;
            
            // Left Side
            if (leftActiveContent.HasValue && leftTabScrollGO != null)
            {
                leftTabScrollGO.SetActive(true);
                leftOffset = 230; 
                
                if (leftSortBtn != null) leftSortBtn.SetActive(true);

                if (leftSearchInput != null) 
                {
                    leftSearchInput.gameObject.SetActive(true);
                    string target = "";
                    if (leftActiveContent.Value == ContentType.Category) target = categoryFilter;
                    else if (leftActiveContent.Value == ContentType.Creator) target = creatorFilter;
                    else target = ""; // Status filter?

                    if (leftSearchInput.text != target) leftSearchInput.text = target;
                    
                    if (leftSearchInput.placeholder is Text ph)
                    {
                        ph.text = leftActiveContent.Value.ToString() + "...";
                    }
                    
                    // Hide search input for Status for now
                    if (leftActiveContent.Value == ContentType.Status) leftSearchInput.gameObject.SetActive(false);
                }

                // Sub Controls logic moved to UpdateTabs to ensure correct state synchronization
                /*
                if (leftSubTabScrollGO.activeSelf)
                {
                    if (leftSubSortBtn != null) 
                    {
                        leftSubSortBtn.SetActive(true);
                        UpdateSortButtonText(leftSubSortBtnText, GetSortState("Tags"));
                    }
                    if (leftSubSearchInput != null) 
                    {
                        leftSubSearchInput.gameObject.SetActive(true);
                        if (leftSubSearchInput.text != tagFilter) leftSubSearchInput.text = tagFilter;
                    }
                }
                else
                {
                    if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                    if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
                }
                */
            }
            else if (leftTabScrollGO != null)
            {
                leftTabScrollGO.SetActive(false);
                if (leftSubTabScrollGO != null) leftSubTabScrollGO.SetActive(false);
                if (leftSearchInput != null) leftSearchInput.gameObject.SetActive(false);
                if (leftSortBtn != null) leftSortBtn.SetActive(false);
                
                // Ensure sub controls are hidden if main panel is closed
                if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
            }
            
            // Right Side
            if (rightActiveContent.HasValue && rightTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(true);
                rightOffset = -230;

                if (rightSortBtn != null) rightSortBtn.SetActive(true);

                if (rightSearchInput != null) 
                {
                    rightSearchInput.gameObject.SetActive(true);
                    string target = "";
                    if (rightActiveContent.Value == ContentType.Category) target = categoryFilter;
                    else if (rightActiveContent.Value == ContentType.Creator) target = creatorFilter;
                    else target = "";

                    if (rightSearchInput.text != target) rightSearchInput.text = target;

                    if (rightSearchInput.placeholder is Text ph)
                    {
                        ph.text = rightActiveContent.Value.ToString() + "...";
                    }

                    // Hide search input for Status for now
                    if (rightActiveContent.Value == ContentType.Status) rightSearchInput.gameObject.SetActive(false);
                }

                // Sub Controls logic moved to UpdateTabs
                /*
                if (rightSubTabScrollGO.activeSelf)
                {
                    if (rightSubSortBtn != null) 
                    {
                        rightSubSortBtn.SetActive(true);
                        UpdateSortButtonText(rightSubSortBtnText, GetSortState("Tags"));
                    }
                    if (rightSubSearchInput != null) 
                    {
                        rightSubSearchInput.gameObject.SetActive(true);
                        if (rightSubSearchInput.text != tagFilter) rightSubSearchInput.text = tagFilter;
                    }
                }
                else
                {
                    if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                    if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
                }
                */
            }
            else if (rightTabScrollGO != null)
            {
                rightTabScrollGO.SetActive(false);
                if (rightSubTabScrollGO != null) rightSubTabScrollGO.SetActive(false);
                if (rightSearchInput != null) rightSearchInput.gameObject.SetActive(false);
                if (rightSortBtn != null) rightSortBtn.SetActive(false);
                
                // Ensure sub controls are hidden if main panel is closed
                if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
            }
            
            contentScrollRT.offsetMin = new Vector2(leftOffset, 60);
            contentScrollRT.offsetMax = new Vector2(rightOffset, -55);
            
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
             // Text updates disabled as per request to keep static labels
             /*
             UpdateButtonState(rightCategoryBtnText, true, ContentType.Category);
             UpdateButtonState(rightCreatorBtnText, true, ContentType.Creator);
             UpdateButtonState(leftCategoryBtnText, false, ContentType.Category);
             UpdateButtonState(leftCreatorBtnText, false, ContentType.Creator);
             */
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

            var sortedCategories = categories.OrderByDescending(c => c.path.Length).ToList();

            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    // Filter by creator if set
                    if (!string.IsNullOrEmpty(currentCreator))
                    {
                        if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                    }

                    if (pkg.FileEntries == null) continue;
                    foreach (var entry in pkg.FileEntries)
                    {
                        foreach (var cat in sortedCategories)
                        {
                            if (IsMatch(entry, cat.paths, cat.path, catExtensions[cat.name]))
                            {
                                categoryCounts[cat.name]++;
                                break;
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
                     if (IsMatch(entry, currentPaths, currentPath, extensions))
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

        private void CacheTagCounts()
        {
            tagCounts.Clear();
            if (FileManager.PackagesByUid == null) return;

            string[] extensions = currentExtension.Split('|');
            
            // Collect all relevant tags to count
            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string title = titleText != null ? titleText.text : "";
            if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagsToCount.UnionWith(TagFilter.AllClothingTags);
                tagsToCount.UnionWith(TagFilter.ClothingUnknownTags);
            }
            else if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagsToCount.UnionWith(TagFilter.AllHairTags);
                tagsToCount.UnionWith(TagFilter.HairUnknownTags);
            }
            
            if (tagsToCount.Count == 0) return;

            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (pkg.FileEntries == null) continue;
                
                // If filtering by creator, respect it
                if (!string.IsNullOrEmpty(currentCreator))
                {
                    if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                }

                foreach (var entry in pkg.FileEntries)
                {
                    if (IsMatch(entry, currentPaths, currentPath, extensions))
                    {
                        string pathLower = entry.Path.ToLowerInvariant();
                        foreach(var tag in tagsToCount)
                        {
                            if (pathLower.Contains(tag)) // tagsToCount are lowercase
                            {
                                if (!tagCounts.ContainsKey(tag)) tagCounts[tag] = 0;
                                tagCounts[tag]++;
                            }
                        }
                    }
                }
            }
            tagsCached = true;
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

            // Add Hover Border
            handleGO.AddComponent<UIHoverBorder>();

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
            t.raycastTarget = false;
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
            resizer.onResizeStatusChange = (isResizing) => {
                 this.isResizing = isResizing;
            };

            // Hover Effect
            UIHoverColor hover = handleGO.AddComponent<UIHoverColor>();
            hover.targetText = t;
            hover.normalColor = t.color;
            hover.hoverColor = Color.green;
            
            AddHoverDelegate(handleGO); // Ensure tracking works here too
        }

        public void SetCategories(List<Gallery.Category> cats)
        {
            categories = cats;
            categoriesCached = false;

            // Try to restore last tab if not specified
            if (string.IsNullOrEmpty(currentPath) && Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
            {
                string lastPageName = Settings.Instance.LastGalleryPage.Value;
                var cat = categories.FirstOrDefault(c => c.name == lastPageName);
                if (!string.IsNullOrEmpty(cat.name))
                {
                    currentPath = cat.path;
                    currentPaths = cat.paths;
                    currentExtension = cat.extension;
                    titleText.text = cat.name;
                    activeTags.Clear();
                }
            }

            if (string.IsNullOrEmpty(currentPath) && categories.Count > 0)
            {
                // Fallback to first category
                currentPath = categories[0].path;
                currentPaths = categories[0].paths;
                currentExtension = categories[0].extension;
                titleText.text = categories[0].name;
                activeTags.Clear();
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
            
            UpdateTabs();
            // If we have categories but no path, set title to first category
            if (categories.Count > 0 && string.IsNullOrEmpty(currentPath))
            {
                 titleText.text = categories[0].name;
            }
        }

        
        // Undo Stack
        private Stack<Action> undoStack = new Stack<Action>();
        
        public void PushUndo(Action action)
        {
            if (action == null) return;
            undoStack.Push(action);
            if (undoStack.Count > 20) // Limit stack size
            {
                // Stack doesn't have RemoveFromBottom, but 20 is small enough.
                // Or we can just let it grow a bit. 20 is safe.
            }
        }
        
        private void Undo()
        {
            if (undoStack.Count > 0)
            {
                Action action = undoStack.Pop();
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Undo: " + ex.Message);
                }
            }
        }
        
        private void UpdateTabs()
        {
            if (leftActiveContent.HasValue) 
            {
                UpdateSortButtonText(leftSortBtnText, GetSortState(leftActiveContent.Value.ToString()));
                
                // Split View Logic
                bool splitView = false;
                if (leftActiveContent == ContentType.Category)
                {
                    string title = titleText != null ? titleText.text : "";
                    if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        splitView = true;
                    }
                }

                if (splitView && leftActiveContent == ContentType.Category && leftSubTabScrollGO != null)
                {
                    // Split Layout
                    leftSubTabScrollGO.SetActive(true);
                    
                    if (leftSubSortBtn != null) 
                    {
                        leftSubSortBtn.SetActive(true);
                        UpdateSortButtonText(leftSubSortBtnText, GetSortState("Tags"));
                    }
                    if (leftSubSearchInput != null) 
                    {
                        leftSubSearchInput.gameObject.SetActive(true);
                        if (leftSubSearchInput.text != tagFilter) leftSubSearchInput.text = tagFilter;
                    }
                    
                    RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                    leftRT.anchorMin = new Vector2(0, 0.5f);
                    leftRT.anchorMax = new Vector2(0, 1);
                    leftRT.offsetMin = new Vector2(10, 5); // Add gap at bottom
                    
                    RectTransform subRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(0, 0);
                    subRT.anchorMax = new Vector2(0, 0.5f);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); // Add gap at top for controls
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, 60); // Gap for clear button

                    // Populate Top (Category)
                    UpdateTabs(ContentType.Category, leftTabContainerGO, leftActiveTabButtons, true);
                    
                    // Populate Bottom (Tags)
                    UpdateTabs(ContentType.Tags, leftSubTabContainerGO, leftSubActiveTabButtons, true);
                }
                else
                {
                    // Full Layout
                    if (leftSubTabScrollGO != null) leftSubTabScrollGO.SetActive(false);
                    if (leftSubSortBtn != null) leftSubSortBtn.SetActive(false);
                    if (leftSubSearchInput != null) leftSubSearchInput.gameObject.SetActive(false);
                    if (leftSubClearBtn != null) leftSubClearBtn.SetActive(false);

                    RectTransform leftRT = leftTabScrollGO.GetComponent<RectTransform>();
                    leftRT.anchorMin = new Vector2(0, 0);
                    leftRT.anchorMax = new Vector2(0, 1);
                    leftRT.offsetMin = new Vector2(10, 20); // Restore default

                    UpdateTabs(leftActiveContent.Value, leftTabContainerGO, leftActiveTabButtons, true);
                }
            }
            if (rightActiveContent.HasValue) 
            {
                UpdateSortButtonText(rightSortBtnText, GetSortState(rightActiveContent.Value.ToString()));
                
                // Right Split View Logic
                bool splitView = false;
                if (rightActiveContent == ContentType.Category)
                {
                    string title = titleText != null ? titleText.text : "";
                    if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        splitView = true;
                    }
                }

                if (splitView && rightActiveContent == ContentType.Category && rightSubTabScrollGO != null)
                {
                    // Split Layout
                    rightSubTabScrollGO.SetActive(true);
                    
                    if (rightSubSortBtn != null) 
                    {
                        rightSubSortBtn.SetActive(true);
                        UpdateSortButtonText(rightSubSortBtnText, GetSortState("Tags"));
                    }
                    if (rightSubSearchInput != null) 
                    {
                        rightSubSearchInput.gameObject.SetActive(true);
                        if (rightSubSearchInput.text != tagFilter) rightSubSearchInput.text = tagFilter;
                    }
                    
                    RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                    rightRT.anchorMin = new Vector2(1, 0.5f);
                    rightRT.anchorMax = new Vector2(1, 1);
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, 5); // Add gap at bottom
                    
                    RectTransform subRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                    subRT.anchorMin = new Vector2(1, 0);
                    subRT.anchorMax = new Vector2(1, 0.5f);
                    subRT.offsetMax = new Vector2(subRT.offsetMax.x, -55); // Add gap at top for controls
                    subRT.offsetMin = new Vector2(subRT.offsetMin.x, 60); // Gap for clear button

                    // Populate Top (Category)
                    UpdateTabs(ContentType.Category, rightTabContainerGO, rightActiveTabButtons, false);
                    
                    // Populate Bottom (Tags)
                    UpdateTabs(ContentType.Tags, rightSubTabContainerGO, rightSubActiveTabButtons, false);
                }
                else
                {
                    // Full Layout
                    if (rightSubTabScrollGO != null) rightSubTabScrollGO.SetActive(false);
                    if (rightSubSortBtn != null) rightSubSortBtn.SetActive(false);
                    if (rightSubSearchInput != null) rightSubSearchInput.gameObject.SetActive(false);
                    if (rightSubClearBtn != null) rightSubClearBtn.SetActive(false);

                    RectTransform rightRT = rightTabScrollGO.GetComponent<RectTransform>();
                    rightRT.anchorMin = new Vector2(1, 0);
                    rightRT.anchorMax = new Vector2(1, 1);
                    rightRT.offsetMin = new Vector2(rightRT.offsetMin.x, 20); // Restore default

                    UpdateTabs(rightActiveContent.Value, rightTabContainerGO, rightActiveTabButtons, false);
                }
            }
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

                // Sort
                var displayCategories = new List<Gallery.Category>(categories);
                var sortState = GetSortState("Category");
                GallerySortManager.Instance.SortCategories(displayCategories, sortState, categoryCounts);

                foreach (var cat in displayCategories)
                {
                    if (!string.IsNullOrEmpty(categoryFilter) && cat.name.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var c = cat;
                    bool isActive = (c.path == currentPath && c.extension == currentExtension);
                    Color btnColor = isActive ? ColorCategory : new Color(0.7f, 0.7f, 0.7f, 1f);

                    int count = 0;
                    if (categoryCounts.ContainsKey(c.name)) count = categoryCounts[c.name];
                    
                    if (count == 0 && !isActive) continue;

                    string label = c.name + " (" + count + ")";

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        Show(c.name, c.extension, c.path);
                        if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                        {
                            Settings.Instance.LastGalleryPage.Value = c.name;
                        }
                        UpdateTabs();
                    }, trackedButtons);
                }
            }
            else if (contentType == ContentType.Creator)
            {
                if (!creatorsCached) CacheCreators();
                if (cachedCreators == null) return;
                
                // Sort
                var sortState = GetSortState("Creator");
                GallerySortManager.Instance.SortCreators(cachedCreators, sortState);

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
                        categoriesCached = false;
                        tagsCached = false;
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs(); 
                    }, trackedButtons);
                }
            }
            else if (contentType == ContentType.Status)
            {
                var statusList = new List<string> { "Favorite", "Hidden", "Loaded", "Unloaded", "Autoinstall" };
                
                var sortState = GetSortState("Status");
                if (sortState.Type == SortType.Name)
                {
                    if (sortState.Direction == SortDirection.Ascending) statusList.Sort();
                    else statusList.Sort((a, b) => b.CompareTo(a));
                }
                
                Color statusColor = new Color(0.3f, 0.5f, 0.7f, 1f); // Blue-ish

                foreach (var status in statusList)
                {
                    bool isActive = false;
                    if (status == "Favorite") isActive = filterFavorite;
                    
                    Color btnColor = isActive ? statusColor : new Color(0.7f, 0.7f, 0.7f, 1f);

                    CreateTabButton(container.transform, status, btnColor, isActive, () => {
                        if (status == "Favorite")
                        {
                            filterFavorite = !filterFavorite;
                            UpdateTabs();
                            currentPage = 0;
                            RefreshFiles();
                        }
                    }, trackedButtons);
                }
            }
            
            else if (contentType == ContentType.Tags)
            {
                if (!tagsCached) CacheTagCounts();

                // Determine which tags to show
                List<string> tagsToShow = new List<string>();
                string title = titleText != null ? titleText.text : "";
                
                if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tagsToShow.AddRange(TagFilter.ClothingTypeTags);
                    tagsToShow.AddRange(TagFilter.ClothingRegionTags);
                    tagsToShow.AddRange(TagFilter.ClothingOtherTags);
                }
                else if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tagsToShow.AddRange(TagFilter.HairTypeTags);
                    tagsToShow.AddRange(TagFilter.HairRegionTags);
                    tagsToShow.AddRange(TagFilter.HairOtherTags);
                }
                
                // Filter
                if (!string.IsNullOrEmpty(tagFilter))
                {
                    tagsToShow.RemoveAll(t => t.IndexOf(tagFilter, StringComparison.OrdinalIgnoreCase) < 0);
                }

                // Sort
                var sortState = GetSortState("Tags");
                if (sortState.Type == SortType.Name)
                {
                    if (sortState.Direction == SortDirection.Ascending) tagsToShow.Sort();
                    else tagsToShow.Sort((a, b) => b.CompareTo(a));
                }
                else if (sortState.Type == SortType.Count)
                {
                    tagsToShow.Sort((a, b) => {
                        int cA = tagCounts.ContainsKey(a) ? tagCounts[a] : 0;
                        int cB = tagCounts.ContainsKey(b) ? tagCounts[b] : 0;
                        int cmp = cA.CompareTo(cB);
                        if (cmp == 0) return a.CompareTo(b);
                        return sortState.Direction == SortDirection.Ascending ? cmp : -cmp;
                    });
                }
                
                foreach (var tag in tagsToShow)
                {
                    int count = 0;
                    if (tagCounts.ContainsKey(tag)) count = tagCounts[tag];
                    
                    bool isActive = activeTags.Contains(tag);
                    
                    // Optional: hide tags with 0 count unless active?
                    // User asked for counter, not hiding. But typical gallery behavior is to hide empty filters.
                    // Let's hide them if count is 0 and not active.
                    if (count == 0 && !isActive) continue;

                    string label = tag + " (" + count + ")";
                    Color btnColor = isActive ? new Color(0.5f, 0.2f, 0.5f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f);

                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        if (activeTags.Contains(tag)) activeTags.Remove(tag);
                        else activeTags.Add(tag);
                        
                        currentPage = 0;
                        RefreshFiles();
                        UpdateTabs();
                    }, trackedButtons);
                }

                // Update Clear Button
                GameObject clearBtn = isLeft ? leftSubClearBtn : rightSubClearBtn;
                Text clearBtnText = isLeft ? leftSubClearBtnText : rightSubClearBtnText;
                
                if (clearBtn != null)
                {
                    if (activeTags.Count > 0)
                    {
                        clearBtn.SetActive(true);
                        if (clearBtnText != null) clearBtnText.text = "Clear Selected (" + activeTags.Count + ")";
                    }
                    else
                    {
                        clearBtn.SetActive(false);
                    }
                }
            }
            
            SetLayerRecursive(container, 5);
        }

        private void CreateTabButton(Transform parent, string label, Color color, bool isActive, UnityAction onClick, List<GameObject> targetList)
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
                groupHLG.childControlWidth = true; // Changed to true since we only have one button now
                groupHLG.childControlHeight = true;
                groupHLG.childForceExpandWidth = true; // Changed to true
                groupHLG.spacing = 0;

                // Tab Button
                GameObject btnGO = UI.CreateUIButton(groupGO, 170, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                btnGO.name = "Button";
                AddHoverDelegate(btnGO);
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
            txt.fontSize = 18;
            txt.color = isActive ? Color.white : Color.black;
            
            if (targetList != null) targetList.Add(groupGO);
        }

        private InputField CreateSearchInput(GameObject parent, float width, UnityAction<string> onValueChanged)
        {
            GameObject inputGO = new GameObject("SearchInput");
            inputGO.transform.SetParent(parent.transform, false);
            
            Image bg = inputGO.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            
            // Add Hover Border
            inputGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(inputGO);

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
                input.ActivateInputField();
                input.MoveTextEnd(false);
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

            // ESC key handling to clear and refocus
            Button clearBtnComponent = clearBtn.GetComponent<Button>();
            inputGO.AddComponent<SearchInputESCHandler>().Initialize(input, clearBtnComponent);

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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (canvas == null) Init();

            titleText.text = title;
            bool paramsChanged = (currentExtension != extension || currentPath != path);
            if (paramsChanged)
            {
                creatorsCached = false;
                tagsCached = false;
                categoriesCached = false;
                currentCreator = "";
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

            canvas.gameObject.SetActive(true);
            LogUtil.Log("GalleryPanel Show setup took: " + sw.ElapsedMilliseconds + "ms");
            
            // Only refresh if params changed OR if we are empty (first run) OR explicit refresh needed
            // Also prevent restart if already running for same params (implied by paramsChanged check)
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
                    // Place 2.0m in front of camera (increased from 1.0m to avoid clipping/too close)
                    // Ensure we don't spawn inside objects if possible, but for UI just floating in front is standard.
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

        public void RefreshFiles(bool keepScroll = false, bool scrollToBottom = false)
        {
            if (refreshCoroutine != null) StopCoroutine(refreshCoroutine);
            refreshCoroutine = StartCoroutine(RefreshFilesRoutine(keepScroll, scrollToBottom));
        }

        private IEnumerator RefreshFilesRoutine(bool keepScroll, bool scrollToBottom)
        {
            yield return null; // Allow UI to render first
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            
            if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
            {
                CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
            }
            currentLoadingGroupId = Guid.NewGuid().ToString();

            // DELAY clearing buttons until we have the new list ready, to avoid flickering/blank screen.
            // But we must capture the state of activeButtons before we do anything that might change them?
            // actually activeButtons is only touched here.

            List<FileEntry> files = new List<FileEntry>();
            string[] extensions = currentExtension.Split('|');
            bool hasNameFilter = !string.IsNullOrEmpty(nameFilterLower);
            
            // Time-based yielding configuration
            var yieldWatch = new System.Diagnostics.Stopwatch();
            long maxMsPerFrame = 10; // Allow 10ms of work per frame (target 60fps is 16ms, but we have overhead)
            
            yieldWatch.Start();

            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    string filterCreator = currentCreator;
                    if (!string.IsNullOrEmpty(filterCreator))
                    {
                        if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != filterCreator) continue;
                    }

                    if (pkg.FileEntries != null)
                    {
                        foreach (var entry in pkg.FileEntries)
                        {
                            // Time-based yield
                            if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                            {
                                yield return null;
                                yieldWatch.Reset();
                                yieldWatch.Start();
                            }

                            bool match = IsMatch(entry, currentPaths, currentPath, extensions);
                            if (match && hasNameFilter)
                            {
                                if (entry.Path.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0)
                                    match = false;
                            }
                            if (match && filterFavorite)
                            {
                                try { if (!entry.IsFavorite()) match = false; }
                                catch { }
                            }
                            if (match && activeTags.Count > 0)
                            {
                                bool tagMatch = false;
                                string pathLower = entry.Path.ToLowerInvariant();
                                foreach(var tag in activeTags)
                                {
                                    if (pathLower.Contains(tag.ToLowerInvariant())) 
                                    {
                                        tagMatch = true; 
                                        break; 
                                    }
                                }
                                if (!tagMatch) match = false;
                            }

                            if (match)
                            {
                                files.Add(entry);
                            }
                        }
                    }
                }
            }

            List<string> pathsToSearch = new List<string>();
            if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
            else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

            if (activeContentType == ContentType.Category)
            {
                foreach (var searchPath in pathsToSearch)
                {
                    if (!Directory.Exists(searchPath)) continue;

                    foreach (var ext in extensions)
                    {
                        // Directory.GetFiles is blocking, unfortunately. 
                        // If this is slow, we can't yield inside it. 
                        // But we can yield during the processing of results.
                        string[] sysFiles = new string[0];
                        try 
                        {
                            sysFiles = Directory.GetFiles(searchPath, "*." + ext, SearchOption.AllDirectories);
                        }
                        catch { }

                        foreach (var sysPath in sysFiles)
                        {
                            if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                            {
                                yield return null;
                                yieldWatch.Reset();
                                yieldWatch.Start();
                            }

                            if (activeTags.Count > 0)
                            {
                                bool tagMatch = false;
                                string pathLower = sysPath.ToLowerInvariant();
                                foreach(var tag in activeTags)
                                {
                                    if (pathLower.Contains(tag.ToLowerInvariant())) 
                                    {
                                        tagMatch = true; 
                                        break; 
                                    }
                                }
                                if (!tagMatch) continue;
                            }

                            var sysEntry = new SystemFileEntry(sysPath);
                            bool include = true;
                            if (hasNameFilter)
                            {
                                if (sysEntry.Path.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0) include = false;
                            }
                            if (include && filterFavorite)
                            {
                                try { if (!sysEntry.IsFavorite()) include = false; }
                                catch { }
                            }
                            
                            if (include)
                            {
                                files.Add(sysEntry);
                            }
                        }
                    }
                }
            }
            
            yield return null; // Yield before sorting
            var sortState = GetSortState("Files");
            GallerySortManager.Instance.SortFiles(files, sortState);

            // NOW clear the old buttons, just before we are ready to show new ones.
            foreach (var btn in activeButtons)
            {
                btn.SetActive(false);
                if (btn.name.StartsWith("NavButton_"))
                {
                    navButtonPool.Push(btn);
                }
                else
                {
                    fileButtonPool.Push(btn);
                }
            }
            activeButtons.Clear();
            fileButtonImages.Clear();

            int totalFiles = files.Count;
            int totalPages = Mathf.CeilToInt((float)totalFiles / itemsPerPage);
            if (totalPages == 0) totalPages = 1;
            
            if (currentPage >= totalPages) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;
            
            if (paginationText != null) 
                paginationText.text = (currentPage + 1) + " / " + totalPages + " (" + totalFiles + ")";

            if (paginationPrevBtn != null) 
                paginationPrevBtn.GetComponent<Button>().interactable = (currentPage > 0);
            
            if (paginationNextBtn != null) 
                paginationNextBtn.GetComponent<Button>().interactable = (currentPage < totalPages - 1);

            int startIndex = currentPage * itemsPerPage;
            int endIndex = Mathf.Min(startIndex + itemsPerPage, totalFiles);

            if (currentPage > 0)
            {
                GameObject prevBtn = InjectButton("Previous\nPage", PrevPage);
                prevBtn.transform.SetAsFirstSibling();
            }

            // Create buttons. 
            // Strategy: Create the first batch (e.g. 32) immediately to fill the view.
            // Then yield more frequently.
            
            int createdCount = 0;
            int firstBatchSize = 32;
            yieldWatch.Reset();
            yieldWatch.Start();

            for (int i = startIndex; i < endIndex; i++)
            {
                try
                {
                    CreateFileButton(files[i]);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VPB] Error creating button for " + files[i].Name + ": " + ex.ToString());
                }

                createdCount++;

                // If we are past the first batch, use time budgeting
                if (createdCount > firstBatchSize)
                {
                     if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                     {
                         yield return null;
                         yieldWatch.Reset();
                         yieldWatch.Start();
                     }
                }
                // Else: we are in the first batch, just keep going until 32 are done (unless it takes insane time? nah 32 is fast)
            }

            if (currentPage < totalPages - 1)
            {
                GameObject nextBtn = InjectButton("Next\nPage", NextPage);
                nextBtn.transform.SetAsLastSibling();
            }
            
            if (scrollRect != null && !keepScroll)
            {
                scrollRect.verticalNormalizedPosition = scrollToBottom ? 0f : 1f;
            }

            refreshCoroutine = null;
            LogUtil.Log("RefreshFilesRoutine took: " + swTotal.ElapsedMilliseconds + "ms");
        }

        public GameObject InjectButton(string label, UnityAction action)
        {
            GameObject btnGO;
            if (navButtonPool.Count > 0)
            {
                btnGO = navButtonPool.Pop();
                btnGO.SetActive(true);
            }
            else
            {
                btnGO = CreateNewNavButtonGO();
            }

            // Reset/Configure for Navigation
            BindNavigationButton(btnGO, label, action);
            activeButtons.Add(btnGO);
            return btnGO;
        }

        private GameObject CreateNewNavButtonGO()
        {
            GameObject btnGO = new GameObject("NavButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.6f, 1f);

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();

            GameObject navTextGO = new GameObject("NavText");
            navTextGO.transform.SetParent(btnGO.transform, false);
            Text t = navTextGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 24;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            
            RectTransform rt = navTextGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            return btnGO;
        }

        private void BindNavigationButton(GameObject btnGO, string label, UnityAction action)
        {
            btnGO.name = "NavButton_" + label.Replace("\n", ""); // Identification for Pool

            // Reset common elements
            Button btn = btnGO.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            if (action != null) btn.onClick.AddListener(action);

            // Set Text
            Transform navTextT = btnGO.transform.Find("NavText");
            if (navTextT != null)
            {
                Text t = navTextT.GetComponent<Text>();
                if (t != null) t.text = label;
            }

            // Set BG Color (Optional reset if changed elsewhere)
            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = new Color(0.2f, 0.4f, 0.6f, 1f); 
        }

        private bool IsMatch(FileEntry entry, List<string> paths, string singlePath, string[] extensions)
        {
            string entryPath = entry.Path;
            int startIdx = 0;
            // Handle package paths which look like "uid:/path"
            int colonIdx = entryPath.IndexOf(":/");
            if (colonIdx >= 0)
            {
                startIdx = colonIdx + 2;
            }

            bool pathMatch = false;
            if (paths != null && paths.Count > 0)
            {
                foreach(var p in paths) {
                    if (string.Compare(entryPath, startIdx, p, 0, p.Length, StringComparison.OrdinalIgnoreCase) == 0) {
                        pathMatch = true;
                        break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(singlePath))
            {
                if (string.Compare(entryPath, startIdx, singlePath, 0, singlePath.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    pathMatch = true;
            }

            if (!pathMatch) return false;

            foreach (var ext in extensions)
            {
                if (entryPath.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void CreateFileButton(FileEntry file)
        {
            GameObject btnGO;
            if (fileButtonPool.Count > 0)
            {
                btnGO = fileButtonPool.Pop();
                btnGO.SetActive(true);
            }
            else
            {
                btnGO = CreateNewFileButtonGO();
            }
            
            BindFileButton(btnGO, file);
            btnGO.transform.SetAsLastSibling();
            activeButtons.Add(btnGO);
        }

        private GameObject CreateNewFileButtonGO()
        {
            GameObject btnGO = new GameObject("FileButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = Color.gray;

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();

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
            draggable.ThumbnailImage = thumbImg;
            draggable.Panel = this;

            // Favorite Button
            try
            {
                GameObject favBtnGO = UI.CreateUIButton(btnGO, 40, 40, "★", 24, 0, 0, AnchorPresets.topRight, null);
                favBtnGO.name = "Button_Fav"; // Name it to find it later
                favBtnGO.SetActive(false); 
                RectTransform favRT = favBtnGO.GetComponent<RectTransform>();
                favRT.anchoredPosition = new Vector2(-5, -5);
                
                Image favBg = favBtnGO.GetComponent<Image>();
                favBg.color = new Color(0, 0, 0, 0.4f);

                favBtnGO.AddComponent<FavoriteHandler>();
                
                // Hover Logic
                var hoverDelegate = btnGO.AddComponent<UIHoverDelegate>();
                hoverDelegate.OnHoverChange = (isHovering) => {
                     var handler = favBtnGO.GetComponent<FavoriteHandler>();
                     if (handler != null) handler.SetHover(isHovering);
                };
                hoverDelegate.OnHoverChange += (enter) => {
                    if (enter) hoverCount++;
                    else hoverCount--;
                };

                if (favBtnGO != null) AddHoverDelegate(favBtnGO);
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Error creating favorite icon template: " + ex.Message);
            }
            
            SetLayerRecursive(btnGO, 5);
            return btnGO;
        }

        private void BindFileButton(GameObject btnGO, FileEntry file)
        {
            btnGO.name = "FileButton_" + file.Name;
            
            // Image
            Image img = btnGO.GetComponent<Image>();
            if (file.Path == selectedPath) img.color = Color.yellow;
            else img.color = Color.gray;
            if (!fileButtonImages.ContainsKey(file.Path)) fileButtonImages.Add(file.Path, img);

            // Button
            Button btn = btnGO.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnFileClick(file));

            // Thumbnail
            Transform thumbTr = btnGO.transform.Find("Thumbnail");
            if (thumbTr != null) thumbTr.gameObject.SetActive(true);
            RawImage thumbImg = thumbTr.GetComponent<RawImage>();
            thumbImg.texture = null; // Clear prev
            thumbImg.color = new Color(0, 0, 0, 0.5f);
            LoadThumbnail(file, thumbImg);

            // Hide NavText
            Transform navTextTr = btnGO.transform.Find("NavText");
            if (navTextTr != null) navTextTr.gameObject.SetActive(false);
            
            // Label
            Transform labelTr = btnGO.transform.Find("Card/Label");
            Text labelText = labelTr.GetComponent<Text>();
            labelText.text = file.Name;
            
            // Draggable
            UIDraggableItem draggable = btnGO.GetComponent<UIDraggableItem>();
            draggable.FileEntry = file;
            
            // Favorite
            Transform favTr = btnGO.transform.Find("Button_Fav");
            if (favTr != null)
            {
                GameObject favBtnGO = favTr.gameObject;
                FavoriteHandler favHandler = favBtnGO.GetComponent<FavoriteHandler>();
                Text favText = favBtnGO.GetComponentInChildren<Text>();
                
                favHandler.Init(file, favText);
                
                Button favBtn = favBtnGO.GetComponent<Button>();
                favBtn.onClick.RemoveAllListeners();
                favBtn.onClick.AddListener(() => {
                    try { file.SetFavorite(!file.IsFavorite()); }
                    catch (Exception ex) { Debug.LogError("[VPB] Error toggling favorite: " + ex.Message); }
                });
            }
        }

        private void LoadThumbnail(FileEntry file, RawImage target)
        {
            string imgPath = "";
            if (file.Path.EndsWith(".json") || file.Path.EndsWith(".vap") || file.Path.EndsWith(".vam") || file.Path.EndsWith(".assetbundle") || file.Path.EndsWith(".unity3d"))
                imgPath = Regex.Replace(file.Path, "\\.(json|vac|vap|vam|scene|assetbundle|unity3d)$", ".jpg");
            else if (file.Path.EndsWith(".jpg") || file.Path.EndsWith(".png"))
                imgPath = file.Path;

            if (string.IsNullOrEmpty(imgPath)) return;

            if (CustomImageLoaderThreaded.singleton == null) return;

            // 1. Memory Cache
            Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath);
            if (tex != null)
            {
                target.texture = tex;
                target.color = Color.white;
                return;
            }

            // 2. Disk Cache - Removed to prevent blocking main thread
            // Now handled asynchronously by CustomImageLoaderThreaded
            // if (GalleryThumbnailCache.Instance.TryGetThumbnail(...)) { ... }

            // 3. Request Load
            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.QIPool.Get();
            qi.imgPath = imgPath;
            qi.isThumbnail = true;
            qi.priority = 10; 
            qi.groupId = currentLoadingGroupId;
            qi.callback = (res) => {
                if (res != null && res.tex != null) {
                    if (target != null) {
                        target.texture = res.tex;
                        target.color = Color.white;
                    }
                    StartCoroutine(GenerateAndCacheThumbnail(imgPath, res.tex, file.LastWriteTime.ToFileTime()));
                }
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
        }

        private IEnumerator GenerateAndCacheThumbnail(string path, Texture2D sourceTex, long lastWriteTime)
        {
            yield return null;

            if (sourceTex == null) yield break;

            int maxDim = 256;
            byte[] bytes = null;
            int w = sourceTex.width;
            int h = sourceTex.height;

            if (w <= maxDim && h <= maxDim)
            {
                bytes = sourceTex.GetRawTextureData();
            }
            else
            {
                float aspect = (float)w / h;
                if (w > h) { w = maxDim; h = Mathf.RoundToInt(maxDim / aspect); }
                else { h = maxDim; w = Mathf.RoundToInt(maxDim * aspect); }

                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
                Graphics.Blit(sourceTex, rt);
                
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                
                Texture2D newTex = new Texture2D(w, h, TextureFormat.RGB24, false);
                newTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                newTex.Apply();
                
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                
                bytes = newTex.GetRawTextureData();
                Destroy(newTex);
            }

            if (bytes != null)
            {
                GalleryThumbnailCache.Instance.SaveThumbnail(path, bytes, bytes.Length, w, h, TextureFormat.RGB24, lastWriteTime);
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

        public void SetFollowMode(bool enabled)
        {
            if (followUser != enabled)
            {
                ToggleFollowMode();
            }
        }

        // Sorting
        private Dictionary<string, SortState> contentSortStates = new Dictionary<string, SortState>();

        private SortState GetSortState(string context)
        {
            if (!contentSortStates.ContainsKey(context))
            {
                contentSortStates[context] = GallerySortManager.Instance.GetDefaultSortState(context);
            }
            return contentSortStates[context];
        }

        private void CycleSort(string context, Text buttonText)
        {
            var state = GetSortState(context);
            // Cycle Type
            int currentType = (int)state.Type;
            int maxType = Enum.GetNames(typeof(SortType)).Length;
            
            // Basic cycle: Name -> Date -> Size -> Count -> Name
            // But Count is only for Category/Creator. Size/Date only for Files.
            // We need context-aware cycling.
            
            SortType nextType = state.Type;
            do {
                currentType = (currentType + 1) % maxType;
                nextType = (SortType)currentType;
            } while (!IsSortTypeValid(context, nextType));
            
            state.Type = nextType;
            
            // Default directions
            if (state.Type == SortType.Name) state.Direction = SortDirection.Ascending;
            else state.Direction = SortDirection.Descending; // Date, Count, Size usually Descending first
            
            SaveSortState(context, state);
            UpdateSortButtonText(buttonText, state);
            
            if (context == "Files") 
            {
                currentPage = 0;
                RefreshFiles();
            }
            else UpdateTabs();
        }
        
        private void ToggleSortDirection(string context, Text buttonText)
        {
            var state = GetSortState(context);
            state.Direction = (state.Direction == SortDirection.Ascending) ? SortDirection.Descending : SortDirection.Ascending;
            SaveSortState(context, state);
            UpdateSortButtonText(buttonText, state);
            
            if (context == "Files") 
            {
                currentPage = 0;
                RefreshFiles();
            }
            else UpdateTabs();
        }

        private bool IsSortTypeValid(string context, SortType type)
        {
            if (context == "Files")
            {
                return type == SortType.Name || type == SortType.Date || type == SortType.Size;
            }
            else if (context == "Category" || context == "Creator" || context == "Status" || context == "Tags")
            {
                return type == SortType.Name || type == SortType.Count;
            }
            return false;
        }

        private void UpdateSortButtonText(Text t, SortState state)
        {
            if (t == null) return;
            string symbol = "";
            switch(state.Type)
            {
                case SortType.Name: symbol = "Az"; break;
                case SortType.Date: symbol = "Dt"; break;
                case SortType.Size: symbol = "Sz"; break;
                case SortType.Count: symbol = "#"; break;
                case SortType.Score: symbol = "Sc"; break;
            }
            string arrow = state.Direction == SortDirection.Ascending ? "↑" : "↓";
            t.text = symbol + arrow;
        }

        private void SaveSortState(string context, SortState state)
        {
            contentSortStates[context] = state;
            GallerySortManager.Instance.SaveSortState(context, state);
        }
        
        public bool GetFollowMode()
        {
            return followUser;
        }

        public void ToggleFollowMode()
        {
            followUser = !followUser;
            if (followUser)
            {
                lastFollowUpdateTime = 0f; // Force immediate update
                if (canvas != null) targetFollowRotation = canvas.transform.rotation;
            }
            UpdateFollowButtonState();
        }

        private void UpdateFollowButtonState()
        {
            string text = followUser ? "Follow" : "Static";
            Color color = followUser ? new Color(0.2f, 0.6f, 0.8f, 1f) : Color.gray;
            
            if (rightFollowBtnText != null) rightFollowBtnText.text = text;
            if (rightFollowBtnImage != null) rightFollowBtnImage.color = color;
            
            if (leftFollowBtnText != null) leftFollowBtnText.text = text;
            if (leftFollowBtnImage != null) leftFollowBtnImage.color = color;
        }

    }

    public class UIHoverDelegate : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> OnHoverChange;
        public Action<PointerEventData> OnPointerEnterEvent;
        public void OnPointerEnter(PointerEventData d) 
        {
            OnHoverChange?.Invoke(true);
            OnPointerEnterEvent?.Invoke(d);
        }
        public void OnPointerExit(PointerEventData d) => OnHoverChange?.Invoke(false);
    }

    public class UIRightClickDelegate : MonoBehaviour, IPointerClickHandler
    {
        public Action OnRightClick;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                OnRightClick?.Invoke();
        }
    }

    public class FavoriteHandler : MonoBehaviour
    {
        private FileEntry entry;
        private Text iconText;
        private bool isHovering = false;
        private bool isFavorite = false;

        public void Init(FileEntry e, Text t)
        {
            entry = e;
            iconText = t;
            try
            {
                if (FavoritesManager.Instance != null)
                {
                    isFavorite = FavoritesManager.Instance.IsFavorite(entry);
                    FavoritesManager.Instance.OnFavoriteChanged += OnFavChanged;
                }
            }
            catch (Exception) { }
            UpdateState();
        }

        public void SetHover(bool hover)
        {
            isHovering = hover;
            UpdateState();
        }

        private void OnFavChanged(string uid, bool fav)
        {
            if (entry != null && entry.Uid == uid)
            {
                isFavorite = fav;
                UpdateState();
            }
        }

        private void UpdateState()
        {
            // Logic: Visible if Favorite OR Hovering
            bool shouldShow = isFavorite || isHovering;
            
            gameObject.SetActive(shouldShow);

            if (iconText != null)
            {
                // Color: Yellow if Favorite, otherwise White with alpha (Ghost)
                iconText.color = isFavorite ? Color.yellow : new Color(1f, 1f, 1f, 0.5f);
            }
        }

        void OnDestroy()
        {
            try
            {
                // Only unsubscribe if we successfully subscribed (which means Instance worked)
                // But safer to just try/catch
                FavoritesManager.Instance.OnFavoriteChanged -= OnFavChanged;
            }
            catch { }
        }
    }

    public class SearchInputESCHandler : MonoBehaviour
    {
        private InputField inputField;
        private Button clearButton;
        private bool refocusQueued;

        public void Initialize(InputField input, Button clearBtn = null)
        {
            inputField = input;
            clearButton = clearBtn;
        }

        private void OnGUI()
        {
            if (inputField == null || !inputField.isFocused) return;
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                if (clearButton != null) clearButton.onClick?.Invoke();
                else
                {
                    inputField.text = "";
                    inputField.ActivateInputField();
                    inputField.MoveTextEnd(false);
                }
                if (!refocusQueued && inputField != null)
                {
                    refocusQueued = true;
                    StartCoroutine(Refocus());
                }
            }
        }

        private IEnumerator Refocus()
        {
            yield return null;
            refocusQueued = false;
            if (inputField != null)
            {
                inputField.ActivateInputField();
                inputField.MoveTextEnd(false);
            }
        }
    }
}
