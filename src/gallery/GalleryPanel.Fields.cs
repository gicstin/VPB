using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        private GameObject loadingOverlayGO;
        private RectTransform loadingBarContainerRT;
        private RectTransform loadingBarFillRT;
        private bool isLoadingOverlayVisible;
        private float loadingBarAnimT;
        private float lastScrollTime;
        private Queue<ThumbnailCacheJob> pendingThumbnailCacheJobs = new Queue<ThumbnailCacheJob>();
        private Coroutine thumbnailCacheCoroutine;
        private Text titleText;
        private Text fpsText;
        
        // Reflection Cache
        private static readonly Dictionary<Type, FieldInfo> _pathToScriptFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, PropertyInfo> _pathToScriptPropCache = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, FieldInfo> _internalIdFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _vamDirFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _itemPathFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo[]> _storableFieldsCache = new Dictionary<Type, FieldInfo[]>();

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
                    try { VPBConfig.Instance.Save(); } catch { }
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
        private Text rightActiveItemsBtnText;
        private Image rightActiveItemsBtnImage;
        private Text rightCreatorBtnText;
        private Image rightCreatorBtnImage;
        
        private Text rightHubBtnText; // NEW
        private Image rightHubBtnImage; // NEW
        private GameObject rightHubBtnGO;

        private Text leftCategoryBtnText;
        private Image leftCategoryBtnImage;
        private Text leftActiveItemsBtnText;
        private Image leftActiveItemsBtnImage;
        private Text leftCreatorBtnText;
        private Image leftCreatorBtnImage;

        private Text leftHubBtnText; // NEW
        private Image leftHubBtnImage; // NEW
        private GameObject leftHubBtnGO;

        private Text rightReplaceBtnText;
        private Image rightReplaceBtnImage;
        private Text leftReplaceBtnText;
        private Image leftReplaceBtnImage;

        private GameObject rightUndoBtnGO;
        private GameObject leftUndoBtnGO;

        private GameObject rightRemoveAllClothingBtn;
        private GameObject rightRemoveAllHairBtn;
        private GameObject rightRemoveAtomBtn;
        private GameObject leftRemoveAllClothingBtn;
        private GameObject leftRemoveAllHairBtn;
        private GameObject leftRemoveAtomBtn;

        private GameObject rightRemoveClothingExpandBtn;
        private GameObject leftRemoveClothingExpandBtn;

        private GameObject rightRemoveClothingSubmenuPanelGO;
        private GameObject leftRemoveClothingSubmenuPanelGO;

        private GameObject rightRemoveHairExpandBtn;
        private GameObject leftRemoveHairExpandBtn;

        private GameObject clothingSlotPickerOverlayGO;
        private GameObject clothingSlotPickerPanelGO;

        private GameObject hairSlotPickerOverlayGO;
        private GameObject hairSlotPickerPanelGO;

        private GameObject rightRemoveHairSubmenuPanelGO;
        private GameObject leftRemoveHairSubmenuPanelGO;

        private GameObject rightRemoveHairSubmenuGapPanelGO;
        private GameObject leftRemoveHairSubmenuGapPanelGO;

        private bool hairSubmenuOpen = false;
        private List<GameObject> rightRemoveHairSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftRemoveHairSubmenuButtons = new List<GameObject>();

        private bool hairSubmenuParentHovered = false;
        private bool hairSubmenuOptionsHovered = false;
        private int hairSubmenuParentHoverCount = 0;
        private int hairSubmenuOptionsHoverCount = 0;
        private float hairSubmenuLastHoverTime = 0f;
        private const float HairSubmenuAutoHideDelay = 1.5f;

        private string hairSubmenuTargetAtomUid = null;

        private int hairSubmenuLastOptionCount = 0;

        private float hairSubmenuLastSyncTime = 0f;
        private const float HairSubmenuSyncInterval = 0.5f;

        private string previewRemoveHairAtomUid = null;
        private string previewRemoveHairItemUid = null;
        private bool? previewRemoveHairPrevGeometryVal = null;

        private bool clothingSubmenuOpen = false;
        private List<GameObject> rightRemoveClothingSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftRemoveClothingSubmenuButtons = new List<GameObject>();

        private List<GameObject> rightRemoveClothingVisibilityToggleButtons = new List<GameObject>();
        private List<GameObject> leftRemoveClothingVisibilityToggleButtons = new List<GameObject>();

        private bool clothingSubmenuParentHovered = false;
        private bool clothingSubmenuOptionsHovered = false;
        private int clothingSubmenuParentHoverCount = 0;
        private int clothingSubmenuOptionsHoverCount = 0;
        private float clothingSubmenuLastHoverTime = 0f;
        private const float ClothingSubmenuAutoHideDelay = 1.5f;

        private string clothingSubmenuTargetAtomUid = null;

        private int clothingSubmenuLastOptionCount = 0;
        private float clothingSubmenuLastSyncTime = 0f;
        private const float ClothingSubmenuSyncInterval = 0.5f;

        private float clothingLabelLastCheckTime = 0f;
        private string clothingLabelLastAtomUid = null;
        private bool clothingLabelLastHasOptions = false;
        private int clothingLabelLastCount = 0;

        private float sideContextLastUpdateTime = 0f;
        private const float SideContextUpdateInterval = 0.25f;

        private string previewRemoveClothingAtomUid = null;
        private string previewRemoveClothingItemUid = null;
        private bool? previewRemoveClothingPrevGeometryVal = null;

        private bool atomSubmenuOpen = false;
        private List<GameObject> rightRemoveAtomSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftRemoveAtomSubmenuButtons = new List<GameObject>();

        private bool atomSubmenuParentHovered = false;
        private bool atomSubmenuOptionsHovered = false;
        private int atomSubmenuParentHoverCount = 0;
        private int atomSubmenuOptionsHoverCount = 0;
        private float atomSubmenuLastHoverTime = 0f;
        private const float AtomSubmenuAutoHideDelay = 0.75f;

        private int rightRemoveHairSubmenuStartIndex = -1;
        private int leftRemoveHairSubmenuStartIndex = -1;
        private const int HairSubmenuMaxButtons = 10;
        private const int AtomSubmenuMaxButtons = 20;
        
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
        private string currentActiveItemCategory = "";
        private string currentRatingFilter = "";
        private string currentSizeFilter = "";
        private string categoryFilter = "";
        private string creatorFilter = "";
        private string tagFilter = ""; // NEW
        private string currentSceneSourceFilter = ""; // NEW
        private string currentAppearanceSourceFilter = "";
        private string currentLoadingGroupId = "";
        private Coroutine refreshCoroutine;
        
        private string nameFilter = "";
        private string nameFilterLower = "";

        // Tagging
        private List<string> currentPaths = new List<string>();
        private HashSet<string> activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [Flags]
        private enum ClothingSubfilter
        {
            RealClothing = 1 << 0,
            Presets = 1 << 1,
            Items = 1 << 2,
            Male = 1 << 3,
            Female = 1 << 4,
            Decals = 1 << 5,
        }

        [Flags]
        private enum AppearanceSubfilter
        {
            Presets = 1 << 0,
            Custom = 1 << 1,
        }

        private ClothingSubfilter clothingSubfilter = 0;

        private AppearanceSubfilter appearanceSubfilter = 0;

        private int clothingSubfilterCountAll = 0;
        private int clothingSubfilterCountReal = 0;
        private int clothingSubfilterCountPresets = 0;
        private int clothingSubfilterCountItems = 0;
        private int clothingSubfilterCountMale = 0;
        private int clothingSubfilterCountFemale = 0;
        private int clothingSubfilterCountDecals = 0;

        private int clothingSubfilterFacetCountReal = 0;
        private int clothingSubfilterFacetCountPresets = 0;
        private int clothingSubfilterFacetCountItems = 0;
        private int clothingSubfilterFacetCountMale = 0;
        private int clothingSubfilterFacetCountFemale = 0;
        private int clothingSubfilterFacetCountDecals = 0;

        private int appearanceSubfilterCountAll = 0;
        private int appearanceSubfilterCountPresets = 0;
        private int appearanceSubfilterCountCustom = 0;

        private int appearanceSubfilterFacetCountPresets = 0;
        private int appearanceSubfilterFacetCountCustom = 0;

        private int appearanceSourceCountAll = 0;
        private int appearanceSourceCountPresets = 0;
        private int appearanceSourceCountCustom = 0;

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
        private GameObject leftSideHoverStrip;
        private GameObject rightSideHoverStrip;
        private Stack<GameObject> tabButtonPool = new Stack<GameObject>();

        private List<CanvasGroup> sideButtonGroups = new List<CanvasGroup>();
        private float sideButtonsAlpha = 1f;
        private float sideButtonsFadeDelayTimer = 0f;
        private const float SideButtonsFadeDelay = 0.6f;
        private bool isResizing = false;
        private int hoverCount = 0;
        private UIDraggable dragger;
        private GameObject pointerDotGO;
        private PointerEventData currentPointerData;
        private GameObject targetMarkerGO;
        private string targetMarkerAtomUid;

        private RectTransform previewBorderRT;
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
        private GameObject footerRemoveAllHairBtn;
        private Image footerRemoveAllHairBtnImage;
        private Text footerRemoveAllHairBtnText;

        // Side buttons for dynamic positioning
        private List<RectTransform> rightSideButtons = new List<RectTransform>();
        private List<RectTransform> leftSideButtons = new List<RectTransform>();
        private GameObject leftClearCreatorBtn;
        private GameObject leftClearStatusBtn;
        private GameObject rightClearCreatorBtn;
        private GameObject rightClearStatusBtn;

        private GameObject leftLoadRandomBtn;
        private GameObject rightLoadRandomBtn;

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
        private CanvasGroup hoverPathCanvasGroup;
        private Coroutine hoverFadeCoroutine;
        private GameObject paginationPrevBtn;
        private GameObject paginationPrev10Btn;
        private GameObject paginationNextBtn;
        private GameObject paginationNext10Btn;
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
        public static readonly Color ColorActiveItems = new Color(0.4f, 0.2f, 0.6f, 1f); // Purple
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

        private GameObject footerAutoHideBtn;
        private Text footerAutoHideBtnText;
        private Image footerAutoHideBtnImage;
        
        private Text fileSortBtnText; // NEW
        private Text quickFiltersToggleBtnText; // NEW
    }
}
