using System;
using System.Collections;
using System.Diagnostics;
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
        private ScrollRect scrollRect;
        private GameObject loadingOverlayGO;
        private RectTransform loadingBarContainerRT;
        private RectTransform loadingBarFillRT;
        private float lastScrollTime;
        private Queue<ThumbnailCacheJob> pendingThumbnailCacheJobs = new Queue<ThumbnailCacheJob>();
        private Coroutine thumbnailCacheCoroutine;
        private int _nextThumbPriority = 10;

        /// <summary>
        /// BepInEx builds use the normal csproj (DEBUG/TRACE only): UNITY_EDITOR is never defined here.
        /// Default on for Debug configuration; set true at runtime to profile Release builds.
        /// </summary>
#if DEBUG
        public static bool LogCategoryCreatorSideTabSwitchTiming = true;
#else
        public static bool LogCategoryCreatorSideTabSwitchTiming = false;
#endif
        private Stopwatch _sideTabCategoryCreatorTimingSw;
        private string _sideTabCategoryCreatorTimingSide;

#if DEBUG
        public static bool LogGalleryCategoryTypeSwitchTiming = true;
#else
        public static bool LogGalleryCategoryTypeSwitchTiming = false;
#endif
        private Stopwatch _categoryTypeNavStopwatch;
        private int _categoryTypeNavTargetSession;
        private string _categoryTypeNavLabel;
        /// <summary>Set when <see cref="GalleryPanel.RefreshFiles"/> starts a routine; copied at coroutine entry for correct timing when clicks overlap.</summary>
        private int _boundCategoryNavSessionForCurrentRefresh;

        /// <summary>Incremented at each <see cref="GalleryPanel.RefreshFiles"/> so deferred sub-pane tag counting aborts when a newer refresh supersedes it.</summary>
        private int _deferredSubPaneSessionId;

        /// <summary>Deferred phase1/phase2 side-tab work after the grid is shown; stopped when a new <see cref="GalleryPanel.RefreshFiles"/> supersedes it.</summary>
        private Coroutine _deferredGallerySideTabsCoroutine;

        /// <summary>Sliced tag/facet scan started from <see cref="GalleryPanel.UpdateTabs"/> (e.g. clothing subfilter) so we never block the main thread like <c>CacheTagCounts()</c>.</summary>
        private Coroutine _sideTabsTagCountSliceCo;

        // Thumbnail cache progress UI
        private GameObject _thumbCacheProgressGO;
        private RectTransform _thumbCacheBarFillRT;

        // Thumbnail cache progress tracking
        private int _thumbCacheTotalEnqueued;
        private int _thumbCacheSaved;
        private float _thumbCacheFinishTime = -1f;
        private Text titleText;
        private Text fpsText;

        public List<Gallery.Category> categories = new List<Gallery.Category>();
        private Dictionary<string, string> packageCategoryLabelCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private List<GameObject> activeButtons = new List<GameObject>();
        private Stack<GameObject> fileButtonPool = new Stack<GameObject>();
        private Stack<GameObject> navButtonPool = new Stack<GameObject>(); // NEW: Separate pool for Nav buttons
        private Dictionary<string, Image> fileButtonImages = new Dictionary<string, Image>();
        private string selectedPath = null;
        private Stack<Action> undoStack = new Stack<Action>();
        private Stack<Action> redoStack = new Stack<Action>();
        private bool isApplyingUndoRedo = false;
        private List<GameObject> leftActiveTabButtons = new List<GameObject>();
        private List<GameObject> leftSubActiveTabButtons = new List<GameObject>(); // NEW
        private List<GameObject> rightActiveTabButtons = new List<GameObject>();
        private List<GameObject> rightSubActiveTabButtons = new List<GameObject>(); // NEW

        // Dual-buffer Category/Creator main side-tab lists (avoids destroying hundreds of buttons on each toggle).
        private GameObject leftCategoryTabHolder;
        private GameObject leftCreatorTabHolder;
        private GameObject rightCategoryTabHolder;
        private GameObject rightCreatorTabHolder;
        private List<GameObject> leftCategoryTabButtons = new List<GameObject>();
        private List<GameObject> leftCreatorTabButtons = new List<GameObject>();
        private List<GameObject> rightCategoryTabButtons = new List<GameObject>();
        private List<GameObject> rightCreatorTabButtons = new List<GameObject>();
        private string leftCategoryTabsLastSig;
        private string leftCreatorTabsLastSig;
        private string rightCategoryTabsLastSig;
        private string rightCreatorTabsLastSig;
        private int categorySideTabDataRevision;
        private int creatorSideTabDataRevision;

        private string currentPath = "";
        private string currentExtension = "json";
        private string currentCategoryTitle = "";
        
        private float lastClickTime = 0f;
        
        public bool IsVisible => canvas != null && canvas.gameObject.activeInHierarchy && canvas.enabled;
        public bool HasLoadedContent => hasLoadedContent;

        private bool refreshOnNextShow;
        private bool hasLoadedContent = false;
        private Dictionary<string, float> categoryScrollPositions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private float _pendingScrollRestore = 1f;
        private bool _scrollCacheLoaded = false;
        private DateTime lastAppliedPackageRefreshTime = DateTime.MinValue;
        
        // Configuration
        public bool DragDropReplaceMode
        {
            get { return VPBConfig.Instance != null ? VPBConfig.Instance.DragDropReplaceMode : false; }
            set { 
                if (VPBConfig.Instance != null) {
                    VPBConfig.Instance.DragDropReplaceMode = value;
                    try { VPBConfig.Instance.Save(true, true); } catch { }
                }
            }
        }

        public string AppearanceClothingApplyMode
        {
            get { return VPBConfig.Instance != null ? VPBConfig.Instance.AppearanceClothingApplyMode : "replace"; }
            set
            {
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.AppearanceClothingApplyMode = value;
                    try { VPBConfig.Instance.Save(true, true); } catch { }
                }
            }
        }

        public bool KeepClothingWhenApplyingAppearance
        {
            get { return VPBConfig.Instance != null && VPBConfig.Instance.KeepClothingWhenApplyingAppearance; }
            set
            {
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.KeepClothingWhenApplyingAppearance = value;
                    try { VPBConfig.Instance.Save(true, true); } catch { }
                }
            }
        }
        public bool hasBeenPositioned = false;
        private ContentType activeContentType = ContentType.Category;
        
        private ContentType? leftActiveContent = null;
        private ContentType? rightActiveContent = null;
        
        private GameObject leftTabScrollGO;
        private GameObject leftSubTabScrollGO; // NEW: For split view
        private GameObject rightTabScrollGO;
        private GameObject rightSubTabScrollGO; // NEW: For split view
        private GameObject leftTabContainerGO;
        private GameObject leftSubTabContainerGO; // NEW: For split view
        private GameObject rightTabContainerGO;
        private GameObject rightSubTabContainerGO; // NEW: For split view
        private RectTransform contentScrollRT;
        
        // Buttons
        private Text rightCategoryBtnText;
        private Image rightCategoryBtnImage;
        private Image rightCategoryBtnIconImage;
        private Text rightCreatorBtnText;
        private Image rightCreatorBtnImage;
        private Image rightCreatorBtnIconImage;
        
        private Text footerHubBtnText;
        private Image footerHubBtnImage;
        private GameObject footerHubBtnGO;

        private Text leftCategoryBtnText;
        private Image leftCategoryBtnImage;
        private Image leftCategoryBtnIconImage;
        private Text leftCreatorBtnText;
        private Image leftCreatorBtnImage;
        private Image leftCreatorBtnIconImage;

        private Text rightReplaceBtnText;
        private Image rightReplaceBtnImage;
        private Image rightReplaceBtnIconImage;
        private Text leftReplaceBtnText;
        private Image leftReplaceBtnImage;
        private Image leftReplaceBtnIconImage;

        private GameObject rightKeepClothingBtnGO;
        private Text rightKeepClothingBtnText;
        private Image rightKeepClothingBtnImage;
        private GameObject leftKeepClothingBtnGO;
        private Text leftKeepClothingBtnText;
        private Image leftKeepClothingBtnImage;

        private GameObject footerUndoBtnGO;
        private GameObject footerRedoBtnGO;

        private GameObject rightRemoveAllClothingBtn;
        private Image rightRemoveAllClothingBtnIconImage;
        private GameObject rightRemoveAllHairBtn;
        private Image rightRemoveAllHairBtnIconImage;
        private GameObject rightRemoveAtomBtn;
        private Image rightRemoveAtomBtnIconImage;
        private GameObject leftRemoveAllClothingBtn;
        private Image leftRemoveAllClothingBtnIconImage;
        private GameObject leftRemoveAllHairBtn;
        private Image leftRemoveAllHairBtnIconImage;
        private GameObject leftRemoveAtomBtn;
        private Image leftRemoveAtomBtnIconImage;

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

        private GameObject rightSaveBtnGO;
        private GameObject leftSaveBtnGO;

        private GameObject rightSaveSubmenuPanelGO;
        private GameObject leftSaveSubmenuPanelGO;

        private bool saveSubmenuOpen = false;
        private List<GameObject> rightSaveSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftSaveSubmenuButtons = new List<GameObject>();

        private bool saveSubmenuParentHovered = false;
        private bool saveSubmenuOptionsHovered = false;
        private int saveSubmenuParentHoverCount = 0;
        private int saveSubmenuOptionsHoverCount = 0;
        private float saveSubmenuLastHoverTime = 0f;
        private float saveSubmenuLastOptionsHoverTime = 0f;
        private const float SaveSubmenuAutoHideDelay = 1.5f;

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

        // 5a — anchor yStart captured on first submenu open; reused on removal resyncs to prevent button jump
        private float _clothingSubmenuAnchorYStart = float.NaN;
        private float _hairSubmenuAnchorYStart = float.NaN;

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
        private const int ClothingSubmenuMaxButtons = 30;
        private const int AtomSubmenuMaxButtons = 20;

        private const int SaveSubmenuMaxButtons = 14;
        
        private Text leftSortBtnText;
        private Text rightSortBtnText;
        private GameObject leftSortBtn;
        private GameObject rightSortBtn;
        private Image leftSortBtnBackdrop;
        private Image leftSortBtnIconImage;
        private Image rightSortBtnBackdrop;
        private Image rightSortBtnIconImage;

        private GameObject rightRefreshBtn;
        private Text rightRefreshBtnText;

        // Sub Sort/Search
        private GameObject leftSubSortBtn;
        private Text leftSubSortBtnText;
        private Image leftSubSortBtnBackdrop;
        private Image leftSubSortBtnIconImage;
        private InputField leftSubSearchInput;

        /// <summary>Scene split (All/Addon/Custom): one square button cycling 4 file-sort modes; replaces <see cref="leftSubSortBtn"/>.</summary>
        private GameObject leftSubSceneSortBtn;
        private Image leftSubSceneSortBtnBackdrop;
        private Image leftSubSceneSortIconImage;
        private GameObject rightSubSceneSortBtn;
        private Image rightSubSceneSortBtnBackdrop;
        private Image rightSubSceneSortIconImage;
        private Sprite[] sceneSourceSortModeSprites;
        private bool leftSubSceneSortBarActive;
        private bool rightSubSceneSortBarActive;
        
        private GameObject rightSubSortBtn;
        private Text rightSubSortBtnText;
        private Image rightSubSortBtnBackdrop;
        private Image rightSubSortBtnIconImage;
        private InputField rightSubSearchInput;
        private GameObject rightSubClearBtn; // NEW
        private Text rightSubClearBtnText; // NEW
        
        private GameObject leftSubClearBtn; // NEW
        private Text leftSubClearBtnText; // NEW
        private string currentCreator = "";
        private string currentRatingFilter = "";
        private string currentSizeFilter = "";
        private string categoryFilter = "";
        private string creatorFilter = "";
        private string tagFilter = ""; // NEW
        private string currentSceneSourceFilter = ""; // NEW
        private string currentAppearanceSourceFilter = "";
        private PosePeopleFilter posePeopleFilter = PosePeopleFilter.All;
        private readonly Dictionary<string, int> posePeopleCountCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object posePeopleCountCacheLock = new object();
        private readonly object posePeopleIndexLock = new object();
        private readonly Queue<FileEntry> posePeopleIndexQueue = new Queue<FileEntry>();
        private readonly HashSet<string> posePeopleIndexQueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Coroutine posePeopleIndexCoroutine;
        private string posePeopleIndexGroupId = "";
        private string currentLoadingGroupId = "";
        private Coroutine refreshCoroutine;
        private bool _cacheRetryPending = false;

        /// <summary>When set, logs elapsed time when the first file-list load finishes after create/clone pane.</summary>
        private System.Diagnostics.Stopwatch _paneLoadTimingStopwatch;
        private string _paneLoadTimingKind;

        /// <summary>True when SetCategories skipped a full side-tab rebuild; first RefreshFilesRoutine completes it after caches exist.</summary>
        private bool _sideTabsNeedFullRebuildAfterFirstRefresh;
        
        private string nameFilter = "";
        private string nameFilterLower = "";
        // Tokenized search terms (lowercased, whitespace-split). Enables multi-term search like "acid timeline".
        private string[] nameFilterTerms = new string[0];

        // Tagging
        private List<string> currentPaths = new List<string>();
        private HashSet<string> activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [Flags]
        internal enum ClothingSubfilter
        {
            RealClothing = 1 << 0,
            Presets = 1 << 1,
            Items = 1 << 2,
            Male = 1 << 3,
            Female = 1 << 4,
            Decals = 1 << 5,
            Custom = 1 << 6,
        }

        [Flags]
        internal enum AppearanceSubfilter
        {
            Presets = 1 << 0,
            Custom = 1 << 1,
            Male = 1 << 2,
            Female = 1 << 3,
            Futa = 1 << 4,
        }

        private enum PosePeopleFilter
        {
            All,
            Single,
            Dual,
        }

        private ClothingSubfilter clothingSubfilter = 0;

        private AppearanceSubfilter appearanceSubfilter = 0;

        private int clothingSubfilterCountAll = 0;
        private int clothingSubfilterCountReal = 0;
        private int clothingSubfilterCountPresets = 0;
        private int clothingSubfilterCountCustom = 0;
        private int clothingSubfilterCountItems = 0;
        private int clothingSubfilterCountMale = 0;
        private int clothingSubfilterCountFemale = 0;
        private int clothingSubfilterCountDecals = 0;

        private int clothingSubfilterFacetCountReal = 0;
        private int clothingSubfilterFacetCountPresets = 0;
        private int clothingSubfilterFacetCountCustom = 0;
        private int clothingSubfilterFacetCountItems = 0;
        private int clothingSubfilterFacetCountMale = 0;
        private int clothingSubfilterFacetCountFemale = 0;
        private int clothingSubfilterFacetCountDecals = 0;

        private int appearanceSubfilterCountAll = 0;
        private int appearanceSubfilterCountPresets = 0;
        private int appearanceSubfilterCountCustom = 0;
        private int appearanceSubfilterCountMale = 0;
        private int appearanceSubfilterCountFemale = 0;
        private int appearanceSubfilterCountFuta = 0;

        private int appearanceSubfilterFacetCountPresets = 0;
        private int appearanceSubfilterFacetCountCustom = 0;
        private int appearanceSubfilterFacetCountMale = 0;
        private int appearanceSubfilterFacetCountFemale = 0;
        private int appearanceSubfilterFacetCountFuta = 0;

        private int appearanceSubfilterCurrentCountAll = 0;
        private int appearanceSubfilterCurrentCountMale = 0;
        private int appearanceSubfilterCurrentCountFemale = 0;
        private int appearanceSubfilterCurrentCountFuta = 0;

        private int posePeopleFacetCountSingle = 0;
        private int posePeopleFacetCountDual = 0;

        private int appearanceSourceCountAll = 0;
        private int appearanceSourceCountPresets = 0;
        private int appearanceSourceCountCustom = 0;

        private InputField leftSearchInput;
        private InputField rightSearchInput;
        private InputField titleSearchInput;
        private Text leftTargetBtnText;
        private Image leftTargetBtnImage;
        private Image leftTargetBtnIconImage;
        private Text rightTargetBtnText;
        private Image rightTargetBtnImage;
        private Image rightTargetBtnIconImage;
        private int targetDropdownValue = 0;
        private List<string> targetDropdownOptions = new List<string>();

        private enum AppearanceGender
        {
            Unknown,
            Female,
            Male,
            Futa,
        }

        private static AppearanceGender GetAppearanceGender(FileEntry entry)
        {
            if (entry == null) return AppearanceGender.Unknown;

            string p = entry.Path ?? "";
            string ip = null;
            try
            {
                if (entry is VarFileEntry vfe) ip = vfe.InternalPath;
            }
            catch { }

            HashSet<string> userTags = null;
            try { userTags = TagsManager.Instance.GetTags(entry.Uid); } catch { userTags = null; }
            if (userTags != null && userTags.Count > 0)
            {
                foreach (var t in userTags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    string tl = t.Trim().ToLowerInvariant();
                    if (tl == "futa" || tl == "herm" || tl == "shemale" || tl == "dickgirl") return AppearanceGender.Futa;
                }
                foreach (var t in userTags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    string tl = t.Trim().ToLowerInvariant();
                    if (tl == "female" || tl == "woman" || tl == "girl") return AppearanceGender.Female;
                    if (tl == "male" || tl == "man" || tl == "boy") return AppearanceGender.Male;
                }
            }

            string name = entry.Name ?? "";
            string pkgUid = "";
            try
            {
                if (entry is VarFileEntry vfe2 && vfe2.Package != null) pkgUid = vfe2.Package.Uid ?? "";
            }
            catch { }

            string s = (string.IsNullOrEmpty(ip) ? p : ip).Replace('\\', '/');
            string combined = (s + " " + name + " " + pkgUid).ToLowerInvariant();

            char[] seps = new char[] { '/', '\\', '.', '_', '-', ' ', '(', ')', '[', ']', '{', '}', ',', ';', ':' };
            string[] tokens = combined.Split(seps, StringSplitOptions.RemoveEmptyEntries);

            bool HasToken(string tok)
            {
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (tokens[i] == tok) return true;
                }
                return false;
            }

            if (HasToken("futa") || HasToken("herm") || HasToken("shemale") || HasToken("dickgirl")) return AppearanceGender.Futa;
            if (HasToken("female") || HasToken("woman") || HasToken("girl")) return AppearanceGender.Female;
            if (HasToken("male") || HasToken("man") || HasToken("boy")) return AppearanceGender.Male;

            return AppearanceGender.Unknown;
        }
        private List<Atom> personAtoms = new List<Atom>();
        private System.Collections.Generic.List<System.Action<float>> innerPaneScaleActions = new System.Collections.Generic.List<System.Action<float>>();
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
        
        private Text titleBarSettingsBtnText;
        private Text rightCloneBtnText;
        private Text leftCloneBtnText;

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
        // Icon swap fields for multi-state footer buttons
        private Image footerLayoutIconImage;
        private Sprite footerLayoutGridSprite;
        private Sprite footerLayoutListSprite;
        private Image footerHeightIconImage;
        private Sprite footerHeightFreeSprite;
        private Sprite footerHeightFixedSprite;
        private Image footerAutoHideIconImage;
        private Sprite footerAutoHideOffSprite;
        private Sprite footerAutoHideOnSprite;
        private Image footerShowHiddenIconImage;
        private Sprite footerShowHiddenOffSprite;
        private Sprite footerShowHiddenOnSprite;
        // Tbox pin icon swap
        private Image  tboxPinIconImage;
        private Sprite tboxPinOnSprite;
        private Sprite tboxPinOffSprite;

        // Side buttons for dynamic positioning
        private List<RectTransform> rightSideButtons = new List<RectTransform>();
        private List<RectTransform> leftSideButtons = new List<RectTransform>();
        private GameObject leftClearCreatorBtn;
        private GameObject rightClearCreatorBtn;

        private GameObject footerLoadRandomBtn;

        // Settings Pane
        private SettingsPanel settingsPanel;
        private QuickFiltersUI quickFiltersUI; // NEW
        
        private List<CreatorCacheEntry> cachedCreators = new List<CreatorCacheEntry>();
        private bool creatorsCached = false;
        
        private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
        private bool categoriesCached = false;
        
        private Dictionary<string, int> tagCounts = new Dictionary<string, int>();
        private bool tagsCached = false;

        /// <summary>Incremented on each full <see cref="GalleryPanel.RefreshFiles"/> so background tag scans can abort when superseded.</summary>
        private int galleryFileRefreshSequence;
        internal int GalleryFileRefreshSequence { get { return System.Threading.Thread.VolatileRead(ref galleryFileRefreshSequence); } }
        private TagParallelWaiter tagParallelWaiter;

        // Pagination
        private int currentPage = 0;
        private Text paginationText;
        private RectTransform paginationRT;
        private GameObject footerClearFilterBtn;
        private Text footerClearFilterBtnText;
        private Text footerFilterModeText;
        private GameObject footerFilterModeSpacerGO;
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
        private GameObject footerScrollTopBtn;
        private GameObject footerScrollBottomBtn;
        // lastTotalItems removed
        private int lastTotalPages = 1;
        // lastShownCount removed
        public int GridColumnCount
        {
            get { return VPBConfig.Instance != null ? VPBConfig.Instance.GridColumnCount : 4; }
            set {
                if (VPBConfig.Instance != null) {
                    VPBConfig.Instance.GridColumnCount = value;
                    try { VPBConfig.Instance.Save(true, true); } catch { }
                }
            }
        }
        private int gridColumnCount = 4; // Legacy field for local caching during scroll if needed

        private float listThumbSize => ListRowHeight;
        public float ListRowHeight
        {
            get { return VPBConfig.Instance != null ? VPBConfig.Instance.ListRowHeight : 100f; }
            set {
                if (VPBConfig.Instance != null) {
                    VPBConfig.Instance.ListRowHeight = value;
                    try { VPBConfig.Instance.Save(true, true); } catch { }
                }
            }
        }

        // Apply Mode
        public ApplyMode ItemApplyMode
        {
            get {
                if (VPBConfig.Instance != null) {
                    try
                    {
                        return (ApplyMode)Enum.Parse(typeof(ApplyMode), VPBConfig.Instance.ApplyMode);
                    }
                    catch
                    {
                        LogUtil.LogWarning("[GalleryPanel] ItemApplyMode: unrecognized value '" + VPBConfig.Instance.ApplyMode + "', defaulting to DoubleClick");
                        return ApplyMode.DoubleClick;
                    }
                }
                return ApplyMode.DoubleClick;
            }
            set {
                if (VPBConfig.Instance != null) {
                    if (string.Equals(VPBConfig.Instance.ApplyMode, value.ToString(), System.StringComparison.Ordinal)) {
                        return;
                    }
                    VPBConfig.Instance.ApplyMode = value.ToString();
                    try {
                        VPBConfig.Instance.Save(true, true);
                    } catch (System.Exception ex) {
                        LogUtil.LogError("[GalleryPanel] ItemApplyMode save failed: " + ex.Message);
                    }
                }
            }
        }
        private Text rightApplyModeBtnText;
        private Image rightApplyModeBtnImage;
        private Image rightApplyModeBtnIconImage;
        private Text leftApplyModeBtnText;
        private Image leftApplyModeBtnImage;
        private Image leftApplyModeBtnIconImage;

        private Text rightDesktopModeBtnText;
        private Image rightDesktopModeBtnImage;
        private Image rightDesktopModeBtnIconImage;
        private Text leftDesktopModeBtnText;
        private Image leftDesktopModeBtnImage;
        private Image leftDesktopModeBtnIconImage;

        /// <summary>First N entries in <see cref="rightSideButtons"/> / <see cref="leftSideButtons"/> are 50×50 icon buttons (desktop float/fixed, follow, clone).</summary>
        internal const int GalleryLeadingIconButtonCount = 3;

        private Sprite galleryFloatSprite;
        private Sprite galleryFixedSprite;
        private Sprite galleryFollowOnSprite;
        private Sprite galleryFollowOffSprite;
        private Sprite galleryCloneSprite;
        private Sprite gallerySaveSprite;
        private Sprite galleryCategorySprite;
        private Sprite galleryCreatorSprite;
        private Sprite galleryTargetSprite;
        private Sprite galleryApplySprite;
        private Sprite galleryApplyOneClickSprite;
        private Sprite galleryApplyTwoClickSprite;
        private Sprite galleryAddSprite;
        private Sprite galleryReplaceSprite;
        private Sprite galleryRemoveSprite;

        /// <summary>Current target line for status tooltip when target side button uses an icon.</summary>
        private string sideTargetTooltipLive = "";

        private Image rightFollowBtnIconImage;
        private Image leftFollowBtnIconImage;
        private Image rightCloneBtnIconImage;
        private Image leftCloneBtnIconImage;
        private Image rightSaveBtnIconImage;
        private Image leftSaveBtnIconImage;

        public List<FileEntry> selectedFiles = new List<FileEntry>();
        private HashSet<string> selectedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string selectionAnchorPath = null;

        private List<FileEntry> lastFilteredFiles = new List<FileEntry>();

        /// <summary>Refuse select-all (toolbar and Ctrl+A) when the filtered gallery has more than this many items.</summary>
        private const int SelectAllSafetyMaxItemCount = 1000;
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

        public GalleryLayoutMode layoutMode = GalleryLayoutMode.Grid;
        private GameObject footerLayoutBtn;
        private Text footerLayoutBtnText;
        private Image footerLayoutBtnImage;

        private GameObject footerHeightBtn;
        private Text footerHeightBtnText;
        private Image footerHeightBtnImage;

        private GameObject footerAutoHideBtn;
        private Text footerAutoHideBtnText;
        private Image footerAutoHideBtnImage;

        private GameObject footerShowHiddenPackagesBtn;
        private Text footerShowHiddenPackagesBtnText;
        private Image footerShowHiddenPackagesBtnImage;
        
        private Text fileSortBtnText; // NEW
        private Text fileSortTypeText; // Sort Type button text (Az/Dt/Sz/Rt)
        private Text fileSortDirText; // Sort Direction button text (↑/↓)
        private Image fileSortDirIconImage; // Icon image on the sort-direction button (swapped asc/desc)
        private Sprite fileSortDirAscSprite;
        private Sprite fileSortDirDescSprite;
        private Image ratingSortIconImage; // Icon image on the star toggle (swapped on/off)
        private Sprite ratingStarNormalSprite; // star.png — shown when filter is OFF
        private Sprite ratingStarOffSprite;    // star-off.png — shown when filter is ON
        private GameObject fileSortTypeMenuRoot;
        private GameObject fileSortTypeMenuPanelGO;
        private Text quickFiltersToggleBtnText; // NEW
        private Image quickFiltersToggleBtnIconImage;
        private GameObject ratingSortToggleBtn;
        private Text ratingSortToggleBtnText;
        private Text titleBarRefreshBtnText;
        private GameObject languageSwitcherBtnGO;
        private Text _langBtnText;
        private Image _langBtnImage;
        private GameObject languageMenuPopupGO;
        private bool languageMenuOpen;
        private bool isRatingSortToggleEnabled;

        // Tracks panels hidden when a save flow starts, so they can be restored when it ends
        private List<Canvas> _canvasesHiddenForSave;
    }
}
