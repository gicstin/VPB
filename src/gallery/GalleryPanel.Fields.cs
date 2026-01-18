using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        public Canvas canvas;
        public Text statusBarText;
        private GameObject backgroundBoxGO;
        private CanvasGroup backgroundCanvasGroup;
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
        private Stack<Action> undoStack = new Stack<Action>();
        private List<GameObject> leftActiveTabButtons = new List<GameObject>();
        private List<GameObject> leftSubActiveTabButtons = new List<GameObject>(); // NEW
        private List<GameObject> rightActiveTabButtons = new List<GameObject>();
        private List<GameObject> rightSubActiveTabButtons = new List<GameObject>(); // NEW

        private string currentPath = "";
        private string currentExtension = "json";
        private string currentCategoryTitle = "";
        
        private float lastClickTime = 0f;
        
        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;
        
        // Configuration
        // public bool IsUndocked = false; // Removed
        public bool DragDropReplaceMode
        {
            get { return VPBConfig.Instance != null ? VPBConfig.Instance.DragDropReplaceMode : false; }
            set { 
                if (VPBConfig.Instance != null) {
                    VPBConfig.Instance.DragDropReplaceMode = value;
                    VPBConfig.Instance.TriggerChange();
                }
            }
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
        
        private Text rightHubBtnText; // NEW
        private Image rightHubBtnImage; // NEW

        private Text leftCategoryBtnText;
        private Image leftCategoryBtnImage;
        private Text leftCreatorBtnText;
        private Image leftCreatorBtnImage;

        private Text leftHubBtnText; // NEW
        private Image leftHubBtnImage; // NEW

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
        private string currentStatus = "";
        private string currentRatingFilter = "";
        private string currentSizeFilter = "";
        private string categoryFilter = "";
        private string creatorFilter = "";
        private string tagFilter = ""; // NEW
        private string currentLoadingGroupId = "";
        private Coroutine refreshCoroutine;
        
        private string nameFilter = "";
        private string nameFilterLower = "";

        // Tagging
        private List<string> currentPaths = new List<string>();
        private HashSet<string> activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private InputField leftSearchInput;
        private InputField rightSearchInput;
        private InputField titleSearchInput;
        private Text leftTargetBtnText;
        private Image leftTargetBtnImage;
        private Text rightTargetBtnText;
        private Image rightTargetBtnImage;
        private int targetDropdownValue = 0;
        private List<string> targetDropdownOptions = new List<string>();
        private List<Atom> personAtoms = new List<Atom>();
        private GameObject leftSideContainer;
        private GameObject rightSideContainer;
        private Stack<GameObject> tabButtonPool = new Stack<GameObject>();

        private List<CanvasGroup> sideButtonGroups = new List<CanvasGroup>();
        private float sideButtonsAlpha = 1f;
        private bool isResizing = false;
        private int hoverCount = 0;
        private UIDraggable dragger;
        private GameObject pointerDotGO;
        private PointerEventData currentPointerData;
        private GameObject targetMarkerGO;
        private string targetMarkerAtomUid;
        private List<RectTransform> cancelDropZoneRTs = new List<RectTransform>();
        private List<Image> cancelDropZoneImages = new List<Image>();
        private List<Text> cancelDropZoneTexts = new List<Text>();
        private List<GameObject> cancelDropGroups = new List<GameObject>();
        private Color cancelZoneNormalColor = new Color(0.25f, 0.05f, 0.05f, 0.8f);
        private Color cancelZoneHoverColor = new Color(0.6f, 0.1f, 0.1f, 0.9f);

        private float fpsTimer = 0f;
        private int fpsFrames = 0;
        private const float FpsInterval = 0.5f;

        // Follow Mode Fields
        private bool followUser = true;
        private float lastFollowUpdateTime = 0f;
        private const float FollowUpdateInterval = 0.5f;
        private const float FollowRotateStepDegrees = 120f;
        private Quaternion targetFollowRotation;
        private bool isReorienting = false;
        private const float ReorientStartAngle = 20f;
        private const float ReorientStopAngle = 0.5f;
        private float followYOffset = 0f;
        private Vector2 followXZOffset = Vector2.zero;
        private float followDistanceReference = 1.5f;
        private bool offsetsInitialized = false;
        
        private Text rightFollowBtnText;
        private Image rightFollowBtnImage;
        private Text leftFollowBtnText;
        private Image leftFollowBtnImage;

        private GameObject footerFollowAngleBtn;
        private Image footerFollowAngleImage;
        private GameObject footerFollowDistanceBtn;
        private Image footerFollowDistanceImage;
        private GameObject footerFollowHeightBtn;
        private Image footerFollowHeightImage;

        // Side buttons for dynamic positioning
        private List<RectTransform> rightSideButtons = new List<RectTransform>();
        private List<RectTransform> leftSideButtons = new List<RectTransform>();
        private List<GameObject> rightCancelGroups = new List<GameObject>();
        private List<GameObject> leftCancelGroups = new List<GameObject>();
        private GameObject leftClearCreatorBtn;
        private GameObject leftClearStatusBtn;
        private GameObject rightClearCreatorBtn;
        private GameObject rightClearStatusBtn;

        // Settings Pane
        private SettingsPanel settingsPanel;
        private GalleryActionsPanel actionsPanel;
        private QuickFiltersUI quickFiltersUI; // NEW
        
        private List<CreatorCacheEntry> cachedCreators = new List<CreatorCacheEntry>();
        private bool creatorsCached = false;
        
        private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
        private bool categoriesCached = false;
        
        private Dictionary<string, int> tagCounts = new Dictionary<string, int>();
        private bool tagsCached = false;

        // Pagination
        private int currentPage = 0;
        private int itemsPerPage = 100;
        private Text paginationText;
        private RectTransform paginationRT;
        private Text hoverPathText;
        private RectTransform hoverPathRT;
        private GameObject paginationPrevBtn;
        private GameObject paginationNextBtn;
        private GameObject paginationFirstBtn;
        private GameObject paginationLastBtn;
        private GameObject selectAllBtn;
        private GameObject clearSelectionBtn;
        private GameObject gridSizeMinusBtn;
        private GameObject gridSizePlusBtn;
        private int lastTotalItems = 0;
        private int lastTotalPages = 1;
        private int lastShownCount = 0;
        private int gridColumnCount = 4;

        // Apply Mode
        public ApplyMode ItemApplyMode = ApplyMode.DoubleClick;
        private Text rightApplyModeBtnText;
        private Image rightApplyModeBtnImage;
        private Text leftApplyModeBtnText;
        private Image leftApplyModeBtnImage;

        private Text rightDesktopModeBtnText;
        private Image rightDesktopModeBtnImage;
        private Text leftDesktopModeBtnText;
        private Image leftDesktopModeBtnImage;

        // private FileEntry selectedFile; // Replaced by Multi-Selection
        public List<FileEntry> selectedFiles = new List<FileEntry>();
        private HashSet<string> selectedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string selectionAnchorPath = null;
        private List<FileEntry> lastPageFiles = new List<FileEntry>();
        private List<FileEntry> lastFilteredFiles = new List<FileEntry>();
        public FileEntry selectedFile
        {
            get { return selectedFiles.Count > 0 ? selectedFiles[0] : null; }
            set
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                if (value != null) selectedFiles.Add(value);
                if (value != null && !string.IsNullOrEmpty(value.Path)) selectedFilePaths.Add(value.Path);
            }
        }

        private Hub.GalleryHubItem selectedHubItem;
        
        // Define colors for different content types
        public static readonly Color ColorCategory = new Color(0.5f, 0.15f, 0.15f, 1f); // Darker Red
        public static readonly Color ColorCreator = new Color(0.15f, 0.45f, 0.15f, 1f); // Darker Green
        public static readonly Color ColorHub = new Color(0.8f, 0.4f, 0f, 1f); // Darker Orange
        public static readonly Color ColorLicense = new Color(0.6f, 0f, 0.6f, 1f); // Darker Magenta

        private string dragStatusMsg = null;
        private string temporaryStatusMsg = null;
        private Coroutine temporaryStatusCoroutine = null;

        public bool isFixedLocally = false;
        private bool isCollapsed = false;
        private GameObject collapseTriggerGO;
        private Text collapseHandleText;
        private float collapseTimer = 0f;
        private bool isHoveringTrigger = false;
        private Camera _cachedCamera;

        // Sorting
        private Dictionary<string, SortState> contentSortStates = new Dictionary<string, SortState>();

        private GalleryLayoutMode layoutMode = GalleryLayoutMode.VerticalCard;
        private GameObject footerLayoutBtn;
        private Text footerLayoutBtnText;
        private Image footerLayoutBtnImage;

        private GameObject footerHeightBtn;
        private Text footerHeightBtnText;
        private Image footerHeightBtnImage;
        
        private Text fileSortBtnText; // NEW
        private Text quickFiltersToggleBtnText; // NEW
    }
}
