using System;
using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private static VPBLogSource logger = VPBLogger.Gallery;
        public Canvas canvas;
        public Text statusBarText;
        private bool _registeredWithSuperController;
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

        /// <summary>
        /// Extra, high-detail refresh timing logs around <see cref="GalleryPanel.RefreshFilesRoutine"/>.
        /// Intended for diagnosing stalls that do not show up in catNav timing (e.g. startup, auto-refresh).
        /// </summary>
#if DEBUG
        public static bool LogGalleryRefreshDeepTiming = true;
#else
        public static bool LogGalleryRefreshDeepTiming = false;
#endif

        /// <summary>Optional label from last <see cref="GalleryPanel.RefreshFiles"/>; copied into <c>[VPB.Gallery.DeepTiming]</c> lines.</summary>
        private string _refreshFilesDebugSource;

        /// <summary>Deferred phase1/phase2 side-tab work after the grid is shown; stopped when a new <see cref="GalleryPanel.RefreshFiles"/> supersedes it.</summary>
        private Coroutine _deferredGallerySideTabsCoroutine;
        /// <summary><see cref="GalleryPanel.IO.RefreshFilesRoutine"/> spawn; parent refresh stop does not cancel nested IEnumerator — stop explicitly on supersede.</summary>
        private Coroutine _earlyMetaApplyCoroutine;
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

        private GameObject _categoryQuickChromeRootGO;
        private RectTransform _categoryQuickChromeRootRT;
        private RectTransform _categoryQuickMenuOuterRT;
        private GameObject _categoryQuickBlockerGO;
        private GameObject _categoryQuickMenuOuterGO;
        private GameObject _categoryQuickMenuScrollGO;
        private GameObject _categoryQuickMenuContentGO;
        private bool _categoryQuickMenuOpen;
        private Coroutine _categoryQuickApplyCoroutine;

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
        // When gallery is unhidden via menu gate (no Show() call), refresh raycaster once.
        private bool _queuedRaycastRefreshOnVisible;
        // VR cold boot: avoid enabling canvas/raycaster before World UI ready.
        private Coroutine _deferredSetVisibleCoroutine;
        private bool _pendingVisibleAfterStartupReady;
        private Dictionary<string, float> categoryScrollPositions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        // Tracks category keys that were written during this app session (not just loaded from disk cache).
        private HashSet<string> sessionCategoryScrollKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        private ContentType? leftPrevActiveContent = null;
        private ContentType? rightPrevActiveContent = null;
        
        private GameObject leftTabScrollGO;
        private GameObject leftSubTabScrollGO; // NEW: For split view
        private GameObject rightTabScrollGO;
        private GameObject rightSubTabScrollGO; // NEW: For split view
        private GameObject leftTabContainerGO;
        private GameObject leftSubTabContainerGO; // NEW: For split view
        private GameObject rightTabContainerGO;
        private GameObject rightSubTabContainerGO; // NEW: For split view
        private RectTransform _leftTabViewportRT;
        private RectTransform _rightTabViewportRT;
        private RectTransform _leftSubTabViewportRT;
        private RectTransform _rightSubTabViewportRT;
        private Vector2 _leftTabViewportDefOffsetMin;
        private Vector2 _leftTabViewportDefOffsetMax;
        private Vector2 _rightTabViewportDefOffsetMin;
        private Vector2 _rightTabViewportDefOffsetMax;
        private Vector2 _leftSubTabViewportDefOffsetMin;
        private Vector2 _leftSubTabViewportDefOffsetMax;
        private Vector2 _rightSubTabViewportDefOffsetMin;
        private Vector2 _rightSubTabViewportDefOffsetMax;
        private GameObject leftUserTagsAvailStickyGO;
        private GameObject rightUserTagsAvailStickyGO;
        private GameObject leftUserTagsAvailFooterGO;
        private GameObject rightUserTagsAvailFooterGO;
        private GameObject leftUserTagsAppliedStickyGO;
        private GameObject rightUserTagsAppliedStickyGO;
        private Text leftUserTagAvailTitleText;
        private Text rightUserTagAvailTitleText;
        private Text leftUserTagApplyBtnText;
        private Text rightUserTagApplyBtnText;
        private Text leftUserTagAppliedTitleText;
        private Text rightUserTagAppliedTitleText;
        private RectTransform contentScrollRT;
        
        // Buttons
        private Text rightCategoryBtnText;
        private Image rightCategoryBtnImage;
        private Image rightCategoryBtnIconImage;
        private Text rightCreatorBtnText;
        private Image rightCreatorBtnImage;
        private Image rightCreatorBtnIconImage;
        private Text rightPathBtnText;
        private Image rightPathBtnImage;
        private Image rightPathBtnIconImage;
        
        private Text footerHubBtnText;
        private Image footerHubBtnImage;
        private GameObject footerHubBtnGO;

        private Text leftCategoryBtnText;
        private Image leftCategoryBtnImage;
        private Image leftCategoryBtnIconImage;
        private Text leftCreatorBtnText;
        private Image leftCreatorBtnImage;
        private Image leftCreatorBtnIconImage;
        private Text leftPathBtnText;
        private Image leftPathBtnImage;
        private Image leftPathBtnIconImage;
        private Image leftHistoryBtnImage;
        private Image leftHistoryBtnIconImage;
        private Image rightHistoryBtnImage;
        private Image rightHistoryBtnIconImage;

        private GalleryHistoryFilterMode galleryHistoryFilterMode = GalleryHistoryFilterMode.Recent;
        private string historyTabFilter = "";

        private string settingsFilter = "";
        /// <summary>While true, main side-rail search <see cref="InputField.onValueChanged"/> handlers ignore events (programmatic text assignment / layout).</summary>
        private bool _suppressMainSideSearchValueChanged;
        private string currentSettingsGroup = "all";
        private bool settingsListViewActive = false;
        private bool internalSettingsSessionActive = false;
        private InternalSettingsSnapshot internalSettingsBackup;
        private bool internalSettingsHadPreSessionViewState = false;
        private GalleryLayoutMode internalSettingsPreSessionLayoutMode = GalleryLayoutMode.Grid;
        private float internalSettingsPreSessionScrollNormalized = 1f;
        /// <summary>Isolated list zoom while internal Settings panel open; does not persist to <see cref="ListRowHeight"/>.</summary>
        private float internalSettingsListRowHeightSession = 100f;

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

        private GameObject clothingSlotPickerOverlayGO;
        private GameObject clothingSlotPickerPanelGO;

        private GameObject hairSlotPickerOverlayGO;
        private GameObject hairSlotPickerPanelGO;

        private GameObject leftRemoveHairSubmenuPanelGO;
        private GameObject rightRemoveHairSubmenuPanelGO;

        private GameObject rightSaveBtnGO;
        private GameObject leftSaveBtnGO;

        private bool saveSubmenuOpen = false;
        private List<GameObject> rightSaveSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftSaveSubmenuButtons = new List<GameObject>();

        private const float SaveSubmenuAutoHideDelay = 1.5f;

        private bool hairSubmenuOpen = false;
        private List<GameObject> rightRemoveHairSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftRemoveHairSubmenuButtons = new List<GameObject>();

        private const float HairSubmenuSyncInterval = 0.5f;

        private string previewRemoveHairAtomUid = null;
        private string previewRemoveHairItemUid = null;
        private bool? previewRemoveHairPrevGeometryVal = null;

        private bool clothingSubmenuOpen = false;
        private List<GameObject> rightRemoveClothingSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftRemoveClothingSubmenuButtons = new List<GameObject>();

        private List<GameObject> rightRemoveClothingVisibilityToggleButtons = new List<GameObject>();
        private List<GameObject> leftRemoveClothingVisibilityToggleButtons = new List<GameObject>();

        private const float ClothingSubmenuSyncInterval = 0.5f;

        // 5a — anchor yStart captured on first submenu open; reused on removal resyncs to prevent button jump
        private float _clothingSubmenuAnchorYStart = float.NaN;
        private float _hairSubmenuAnchorYStart = float.NaN;

        private float sideContextLastUpdateTime = 0f;
        private const float SideContextUpdateInterval = 0.25f;

        // Selection toolbox/context menu updates can be expensive (package lookups, layout refresh).
        // Throttle them to avoid per-frame work when selection is active.
        private float selectionContextLastUpdateTime = 0f;
        private const float SelectionContextUpdateInterval = 0.25f;
        private int _selectionContextLastSelCount = -1;
        private int _selectionContextLastTotalCount = -1;

        private string previewRemoveClothingAtomUid = null;
        private string previewRemoveClothingItemUid = null;
        private bool? previewRemoveClothingPrevGeometryVal = null;
        private bool isPreviewRemoveClothingAll = false;
        private List<string> previewRemoveClothingAllItemUids = new List<string>();
        private List<bool> previewRemoveClothingAllPrevVals = new List<bool>();

        // Tracks active clothing UIDs per atom to detect changes for auto-refresh.
        private Dictionary<string, HashSet<string>> _lastActiveClothingUids = new Dictionary<string, HashSet<string>>();
        // Tracks clothing UIDs that were present when the atom was first encountered in this session.
        private Dictionary<string, HashSet<string>> _sessionInitialClothingUids = new Dictionary<string, HashSet<string>>();

        private bool isPreviewRemoveHairAll = false;
        private List<string> previewRemoveHairAllItemUids = new List<string>();
        private List<bool> previewRemoveHairAllPrevVals = new List<bool>();

        private bool atomSubmenuOpen = false;
        private List<GameObject> rightRemoveAtomSubmenuButtons = new List<GameObject>();
        private List<GameObject> leftRemoveAtomSubmenuButtons = new List<GameObject>();

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
        // Creator filter: pipe-separated canonical list (supports multi-select).
        // Example: "foo|bar". Empty = no filter.
        private string currentCreator = "";
        private string _currentCreatorSetSrc = null;
        private readonly HashSet<string> _currentCreatorSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Title bar creator dropdown
        private GameObject titleCreatorBtn;
        private Image titleCreatorBtnBackdrop;
        private Text titleCreatorBtnText;
        private Image titleCreatorBtnIconImage;
        private GameObject titleCreatorDropdown;
        private GameObject titleCreatorDropdownBlocker;
        private InputField titleCreatorDropdownSearchInput;
        private string titleCreatorDropdownFilter = "";
        private ScrollRect _titleCreatorVirtScroll;
        private RectTransform _titleCreatorVirtContentRT;
        private GameObject titleCreatorDropdownHolder;
        private readonly List<CreatorCacheEntry> _titleCreatorVirtView = new List<CreatorCacheEntry>(512);
        private readonly List<GameObject> _titleCreatorVirtButtons = new List<GameObject>(32);
        private int _titleCreatorVirtLastFirstIdx = -1;
        private string _titleCreatorVirtViewSig = null;
        private string currentRatingFilter = "";
        private string currentSizeFilter = "";
        private string categoryFilter = "";
        private string creatorFilter = "";
        private string pathFilter = "";
        private string removeClothingFilter = "";
        private string removeHairFilter = "";
        private string removeAtomFilter = "";
        private string targetFilter = "";
        
        // Creator side-tab virtualization (fast: pool + rebind visible rows on scroll)
        private readonly List<CreatorCacheEntry> _creatorVirtView = new List<CreatorCacheEntry>(512);
        private string _creatorVirtViewSig = null;

        private readonly List<GameObject> _leftCreatorVirtButtons = new List<GameObject>(64);
        private readonly List<GameObject> _rightCreatorVirtButtons = new List<GameObject>(64);
        private ScrollRect _leftCreatorVirtScroll;
        private ScrollRect _rightCreatorVirtScroll;
        private bool _leftCreatorVirtHooked;
        private bool _rightCreatorVirtHooked;
        private int _leftCreatorVirtLastFirstIdx = -1;
        private int _rightCreatorVirtLastFirstIdx = -1;
        private string tagFilter = ""; // NEW
        private string userTagFilter = "";
        /// <summary>Lower split pane when <see cref="ContentType.UserTags"/> — filters tags applied to current selection.</summary>
        private string userTagAppliedFilter = "";
        /// <summary>Selected applied-tag rows for remove / multi-select (CTRL toggle, SHIFT range); see <see cref="GalleryPanel.RemoveFocusedAppliedUserTagFromSelection"/>.</summary>
        private readonly HashSet<string> userTagAppliedRemoveSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Range anchor for SHIFT+click in applied-tags list (visible order).</summary>
        private string userTagAppliedRemoveAnchor = null;
        private readonly List<UserTagSideTabEntry> cachedAppliedUserTagsSelection = new List<UserTagSideTabEntry>(32);
        private string currentSceneSourceFilter = ""; // NEW
        private string currentAppearanceSourceFilter = "";
        private string currentPackagePathFilter = "";
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
        private Coroutine _refreshHistoryLightCo;
        // Hub CDN thumbnail URLs for missing dep packages (dep uid → thumbnail URL), populated async after missing-deps filter is applied.
        private readonly Dictionary<string, string> _hubThumbnailUrlCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        /// <summary>Checked tags in Available pick list. When <see cref="_userTagAvailFilterMode"/> is off: staging for Apply. When on: SQL/exists grid filter for category browse.</summary>
        private readonly HashSet<string> activeUserTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>When true, selected user tags narrow main grid/list (SQLite EXISTS); when false, picks only affect Apply.</summary>
        private bool _userTagAvailFilterMode;
        /// <summary>ALL VAR only: when true, applying/removing user tags on package row also touches all indexed child items in that VAR.</summary>
        private bool _userTagInheritVarToChildren;
        /// <summary>Set during refresh drain when SQLite category query already constrained rows by gallery user tags.</summary>
        private bool _refreshSqliteBulkIncludedUserTagGridFilter;
        /// <summary>
        /// Package-scan fallback pre-filtered VAR rows using <see cref="VpbLocalDatabase.TryBuildCatMemRowKeysMatchingAllUserTags"/> on worker
        /// (SQLite category query unavailable). Stored as int for .NET 3.5 (no System.Threading.Volatile).
        /// </summary>
        private int _refreshWorkerFallbackUserTagPrefilterFlag;
        private bool userTagsCached = false;
        /// <summary>Bumped when <see cref="GalleryPanel.CacheUserTagsSideTab"/> finishes rebuilding SQLite-backed rows.</summary>
        private int userTagSideTabDataRevision = 0;
        private List<UserTagSideTabEntry> cachedUserTagSideTab = new List<UserTagSideTabEntry>(64);

        /// <summary>Filtered/sorted view for virtualized User Tags pick list (same role as <see cref="_creatorVirtView"/>).</summary>
        private readonly List<UserTagSideTabEntry> _userTagVirtView = new List<UserTagSideTabEntry>(256);
        private string _userTagVirtViewSig = null;
        private readonly List<GameObject> _leftUserTagVirtButtons = new List<GameObject>(32);
        private readonly List<GameObject> _rightUserTagVirtButtons = new List<GameObject>(32);
        private ScrollRect _leftUserTagVirtScroll;
        private ScrollRect _rightUserTagVirtScroll;
        private bool _leftUserTagVirtHooked;
        private bool _rightUserTagVirtHooked;

        private enum UserTagSelectionState { Off, On, Mixed }
        private readonly Dictionary<string, UserTagSelectionState> _userTagSelectionStates = new Dictionary<string, UserTagSelectionState>(StringComparer.OrdinalIgnoreCase);
        private int _userTagSelectionRowCount = 0;
        private string _userTagPulseTag = null;
        private float _userTagPulseUntil = 0f;
        private FileEntry _userTagDragHoverFile = null;
        private string _userTagDropPulseKey = null;
        private float _userTagDropPulseUntil = 0f;
        private Coroutine _userTagVisualPulseCoroutine;

        private static readonly Color UserTagStateOnColor = new Color(0.20f, 0.50f, 0.25f, 1f);
        private static readonly Color UserTagStateMixedColor = new Color(0.36f, 0.38f, 0.22f, 1f);
        private static readonly Color UserTagStatePulseColor = new Color(0.72f, 0.62f, 0.25f, 1f);
        private static readonly Color UserTagFilterActiveColor = new Color(0.18f, 0.38f, 0.62f, 1f);
        private static readonly Color UserTagDropGlowColor = new Color(0.35f, 0.95f, 0.55f, 1f);
        private const float UserTagVisualPulseSeconds = 0.45f;

        /// <summary>Dim overlay on gallery pane + centered panel for <see cref="GalleryPanel.ShowUserTagListEditor"/>.</summary>
        private GameObject _userTagEditorRoot;
        private Transform _userTagEditorRowsParent;
        private InputField _userTagEditorFilterInput;
        private InputField _userTagEditorNewTagInput;
        private Image _userTagEditorSortIconImage;
        private Text _userTagEditorTitleText;
        private GameObject _userTagEditorMergeModalGo;
        private Text _userTagEditorMergeModalTitleText;
        private InputField _userTagEditorMergeModalInput;
        private GameObject _userTagEditorRenameModalGo;
        private Text _userTagEditorRenameModalTitleText;
        private InputField _userTagEditorRenameModalInput;
        /// <summary>Display name of row used as rename prefix (also updates tags starting with prefix + space).</summary>
        private string _userTagEditorRenameSourcePrefix;
        /// <summary>0=name asc, 1=name desc, 2=count desc, 3=count asc.</summary>
        private int _userTagEditorSortMode;
        private readonly HashSet<string> _userTagEditorRowSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Last row clicked without Shift; anchor for Shift+click range in tag editor list.</summary>
        private string _userTagEditorAnchorTag;

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
        internal enum HairSubfilter
        {
            Presets = 1 << 0,
            Items = 1 << 1,
            Male = 1 << 2,
            Female = 1 << 3,
            Custom = 1 << 4,
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
        private HairSubfilter hairSubfilter = 0;

        private bool _clothingGenderUserOverride;
        private bool _hairGenderUserOverride;

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

        private int hairSubfilterCountAll = 0;
        private int hairSubfilterCountPresets = 0;
        private int hairSubfilterCountCustom = 0;
        private int hairSubfilterCountItems = 0;
        private int hairSubfilterCountMale = 0;
        private int hairSubfilterCountFemale = 0;

        private int hairSubfilterFacetCountPresets = 0;
        private int hairSubfilterFacetCountCustom = 0;
        private int hairSubfilterFacetCountItems = 0;
        private int hairSubfilterFacetCountMale = 0;
        private int hairSubfilterFacetCountFemale = 0;

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
        /// <summary>Delegate instance registered on side search <see cref="InputField.onValueChanged"/>; <c>UpdateLayout</c> removes it when assigning text so listeners do not run (stops settings filter clearing on refresh).</summary>
        private UnityAction<string> _leftMainSideSearchOnValueChanged;
        private UnityAction<string> _rightMainSideSearchOnValueChanged;
        private InputField titleSearchInput;
        private UnityAction<string> _titleBarSearchOnValueChanged;
        private bool _suppressTitleBarSearchValueChanged;
        private GameObject _titleSearchCompactGO;
        private RectTransform _titleSearchCompactRT;
        private GameObject _titleSearchPopupRootGO;
        private RectTransform _titleSearchPopupPanelRT;
        private InputField _titleSearchPopupField;
        private bool _titleSearchPopupOpen;
        private float _titleSearchPopupProximityAwayTimer;
        private int targetDropdownValue = 0;
        private List<string> targetDropdownOptions = new List<string>();
        private List<GameObject> tboxPersonAtomBtns = new List<GameObject>();
        private GameObject tboxTargetMenuRootGO;
        private GameObject tboxTargetMenuPanelGO;
        private RectTransform tboxTargetMenuPanelRT;
        private GameObject tboxTargetDropdownRowGO;
        private Text tboxTargetDropdownBtnText;
        private bool tboxTargetMenuOpen;

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
        private string _fpsLastAppliedText = null;

        // Follow Mode Fields
        public static GalleryPanel GetAnchoredInstance()
        {
            if (Gallery.singleton == null) return null;
            var pl = Gallery.singleton.Panels;
            if (pl == null) return null;
            for (int i = 0; i < pl.Count; i++)
            {
                var p = pl[i];
                if (p != null && p.canvas != null && !p._userHidden)
                {
                    return p;
                }
            }
            return null;
        }
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
        private RectTransform _titleBarSettingsBtnRT;
        private RectTransform _titleBarQfToggleBtnRT;
        private RectTransform _titleBarFileSortTypeBtnRT;
        private RectTransform _titleBarRatingSortToggleBtnRT;
        private RectTransform _titleBarRefreshBtnRT;
        private RectTransform _titleBarFpsRT;
        private RectTransform _titleBarMinimizeBtnRT;
        private RectTransform _titleBarCloseBtnRT;

        // Fixed desktop dock "Top": side rail buttons live on footer bar (pagination).
        private GameObject _footerSideButtonsGroupGO;
        private RectTransform _footerSideButtonsGroupRT;
        private RectTransform _footerLeftSectionRT;
        private RectTransform _footerRightSectionRT;
        private bool _titleBarSideButtonsReparented;
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
        private Sprite footerAutoHideLeftOffSprite;
        private Sprite footerAutoHideLeftOnSprite;
        private Sprite footerAutoHideRightOffSprite;
        private Sprite footerAutoHideRightOnSprite;
        private Sprite footerAutoHideTopOffSprite;
        private Sprite footerAutoHideTopOnSprite;
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
        /// <summary>Side-rail Tags (UserTags) buttons — for layout / edge-align.</summary>
        private GameObject leftUserTagsSideBtn;
        private GameObject rightUserTagsSideBtn;

        private Sprite galleryCreatorOffSprite;

        private GameObject footerLoadRandomBtn;

        private QuickFiltersUI quickFiltersUI; // NEW
        
        private List<CreatorCacheEntry> cachedCreators = new List<CreatorCacheEntry>();
        private bool creatorsCached = false;
        private List<PathCacheEntry> cachedPaths = new List<PathCacheEntry>();
        private bool pathsCached = false;
        
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
        private HorizontalLayoutGroup footerHLG;
        private int _footerHLGLastRightPadding = -1;
        private GameObject footerBackBtn;
        private GameObject footerClearFilterBtn;
        private Text footerFilterModeText;
        private GameObject footerFilterModeSpacerGO;
        private Text hoverPathText;
        // True when hoverPathText is showing the filtered item count fallback (not an item path).
        private bool hoverPathIsCountMode = false;
        private RectTransform hoverPathRT;
        private CanvasGroup hoverPathCanvasGroup;
        private Coroutine hoverFadeCoroutine;
        // Hover preview overlay (bottom-left above tbox)
        private GameObject hoverPreviewGO;
        private RectTransform hoverPreviewRT;
        private RawImage hoverPreviewImage;
        private FileEntry hoverPreviewFile;
        private bool hoverPreviewDummyActive;
        private GameObject paginationPrevBtn;
        private GameObject paginationPrev10Btn;
        private GameObject paginationNextBtn;
        private GameObject paginationNext10Btn;
        private GameObject paginationFirstBtn;
        private GameObject paginationLastBtn;
        private GameObject gridSizeMinusBtn;
        private GameObject gridSizePlusBtn;
        private GameObject footerScrollTopBtn;
        private GameObject footerScrollBottomBtn;
        private GameObject footerScrollStepUpBtn;
        private GameObject footerScrollStepDownBtn;
        private GameObject leftUserTagScrollStepUpBtn;
        private GameObject leftUserTagScrollStepDownBtn;
        private GameObject rightUserTagScrollStepUpBtn;
        private GameObject rightUserTagScrollStepDownBtn;
        private GameObject footerSpringScrollToggleBtn;
        private Image footerSpringScrollToggleBtnImage;
        private Image footerSpringScrollToggleIconImage;

        // Floating-pane helper (VR): big spring scroll button next to scrollbar (toggleable, default ON).
        private bool springScrollButtonEnabled = true;
        private GameObject springScrollButtonGO;

        // Hold-to-launch/apply (VR): hover-hold over an item to apply after a delay.
        private bool holdToLaunchEnabled = false;
        internal bool HoldToLaunchEnabled { get { return holdToLaunchEnabled; } }
        private bool holdToLaunchPrevEnableDragDrop = false;
        private GameObject footerHoldToLaunchToggleBtn;
        private Image footerHoldToLaunchToggleBtnImage;
        private Image footerHoldToLaunchToggleIconImage;
        private Sprite footerHoldToLaunchOnSprite;
        private Sprite footerHoldToLaunchOffSprite;
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

        /// <summary>Row height for RecyclingGridView / list rows; settings UI uses session zoom only.</summary>
        private float EffectiveListRowHeightForGallery()
        {
            if (IsSettingsPanelOpen() || settingsListViewActive)
                return internalSettingsListRowHeightSession;
            return ListRowHeight;
        }

        /// <summary>Thumb decode density: settings list acts as single-column (widest) regardless of saved grid columns.</summary>
        private int EffectiveGridColumnsForThumbDecode()
        {
            if (IsSettingsPanelOpen() || settingsListViewActive)
                return 1;
            return GridColumnCount;
        }

        private float listThumbSize => EffectiveListRowHeightForGallery();
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
        private Sprite galleryPathSprite;
        private Sprite galleryHistorySprite;
        private Sprite targetOnSprite;
        private Sprite targetOffSprite;
        private Sprite galleryApplySprite;
        private Sprite galleryApplyOneClickSprite;
        private Sprite galleryApplyTwoClickSprite;
        private Sprite galleryAddSprite;
        private Sprite galleryReplaceSprite;
        private Sprite galleryRemoveSprite;

        private Image rightFollowBtnIconImage;
        private Image leftFollowBtnIconImage;
        private Image rightCloneBtnIconImage;
        private Image leftCloneBtnIconImage;
        private Image rightSaveBtnIconImage;
        private Image leftSaveBtnIconImage;

        public List<FileEntry> selectedFiles = new List<FileEntry>();
        private HashSet<string> selectedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string selectionAnchorPath = null;
        private string selectionAnchorIdentityKey = null;
        private float keyboardNavigationNextRepeatRealtime = 0f;
        private const float KeyboardNavigationInitialRepeatDelay = 0.32f;
        private const float KeyboardNavigationRepeatInterval = 0.07f;
        private bool lastHistoryQueryFailed = false;
        private string lastHistoryQueryRejectReason = null;
        private bool lastHistoryQueryHadNameFilter = false;
        private bool pendingHistoryRemoveConfirm = false;
        private int pendingHistoryRemoveConfirmCount = 0;
        private float pendingHistoryRemoveConfirmUntilRealtime = 0f;
        private List<VpbLocalDatabase.ItemUsageSnapshot> pendingHistoryUndoSnapshots = null;
        private float pendingHistoryUndoUntilRealtime = 0f;
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

        #pragma warning disable CS0414
        private Hub.GalleryHubItem selectedHubItem;
        #pragma warning restore CS0414
        
        // Define colors for different content types
        public static readonly Color ColorCategory = new Color(0.5f, 0.15f, 0.15f, 1f); // Darker Red
        public static readonly Color ColorCreator = new Color(0.15f, 0.45f, 0.15f, 1f); // Darker Green
        /// <summary>Title bar search field backdrop when empty (grey chip).</summary>
        public static readonly Color ColorTitleSearchBackdropIdle = new Color(0.15f, 0.15f, 0.15f, 1f);
        /// <summary>Title bar search when query non-empty; blue accent paired with <see cref="ColorCreator"/> tone.</summary>
        public static readonly Color ColorTitleSearchFilterActive = new Color(0.15f, 0.30f, 0.52f, 1f);
        public static readonly Color ColorPath = new Color(0.15f, 0.35f, 0.6f, 1f); // Darker Blue
        public static readonly Color ColorHistory = new Color(0.38f, 0.28f, 0.52f, 1f);
        /// <summary>Side-tab / chrome accent for History (slightly brighter than <see cref="ColorHistory"/>).</summary>
        public static readonly Color ColorHistoryAccent = new Color(0.48f, 0.34f, 0.65f, 1f);
        public static readonly Color ColorHub = new Color(0.8f, 0.4f, 0f, 1f); // Darker Orange
        public static readonly Color ColorLicense = new Color(0.6f, 0f, 0.6f, 1f); // Darker Magenta

        private string dragStatusMsg = null;
        private string temporaryStatusMsg = null;
        private Coroutine temporaryStatusCoroutine = null;
        private GameObject temporaryStatusOwner = null;

        public bool isFixedLocally = false;
        private bool isCollapsed = false;
        private GameObject collapseTriggerGO; // Right dock
        private Text collapseHandleText;
        private GameObject collapseTriggerLeftGO;
        private Text collapseHandleLeftText;
        private GameObject collapseTriggerTopGO;
        private Text collapseHandleTopText;
        private float collapseTimer = 0f;
        private bool isHoveringTrigger = false;
        private Camera _cachedCamera;

        // Per-category filter state memory (BA-style: each category remembers its own filters)
        private readonly Dictionary<string, CategoryFilterState> _categoryFilterStates = new Dictionary<string, CategoryFilterState>(StringComparer.OrdinalIgnoreCase);
        private string _panelId = null;

        private string PanelId
        {
            get
            {
                if (_panelId == null)
                    _panelId = "panel_" + GetHashCode().ToString("x8");
                return _panelId;
            }
        }

        // Sorting
        private Dictionary<string, SortState> contentSortStates = new Dictionary<string, SortState>();

        public GalleryLayoutMode layoutMode = GalleryLayoutMode.Grid;
        private GameObject footerLayoutBtn;
        private Text footerLayoutBtnText;
        private Image footerLayoutBtnImage;

        private GameObject footerHeightBtn;
        private Text footerHeightBtnText;
        private Image footerHeightBtnImage;

        private GameObject footerDockBtn;
        private Image footerDockBtnImage;
        private Image footerDockIconImage;
        private Sprite footerDockRightSprite;
        private Sprite footerDockLeftSprite;
        private Sprite footerDockTopSprite;

        private GameObject footerAutoHideBtn;
        private Text footerAutoHideBtnText;
        private Image footerAutoHideBtnImage;
        private GameObject footerMenuGateBtn;
        private Image footerMenuGateBtnImage;
        private Image footerMenuGateIconImage;
        private Sprite footerMenuGateOffSprite;
        private Sprite footerMenuGateOnSprite;

        private GameObject footerShowHiddenPackagesBtn;
        private Text footerShowHiddenPackagesBtnText;
        private Image footerShowHiddenPackagesBtnImage;

        private bool _userHidden = false;
        private bool _hiddenByMenuGate = false;
        
        private Text fileSortBtnText; // NEW
        private Text fileSortTypeText; // Sort Type button text (Az/Dt/Sz/Rt)
        private Text fileSortDirText; // Unused (combined sort button); kept for CommitSortTypeChange / UpdateSortButtonText signature
        private Image fileSortDirIconImage; // Asc/desc glyph on combined file-sort button
        private Sprite fileSortDirAscSprite;
        private Sprite fileSortDirDescSprite;
        private Image ratingSortIconImage; // Icon image on the star toggle (swapped on/off)
        private Sprite ratingStarNormalSprite; // star.png — shown when filter is OFF
        private Sprite ratingStarOffSprite;    // star_off.png — shown when filter is ON
        private GameObject fileSortTypeMenuRoot;
        private GameObject fileSortTypeMenuPanelGO;
        
        // Side-pane sort dropdown (Category/Creator/Tags/etc)
        private GameObject sidePaneSortMenuRoot;
        private GameObject sidePaneSortMenuPanelGO;
        private RectTransform sidePaneSortMenuPanelRT;
        private string sidePaneSortMenuContext;
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
        private List<GalleryPanel> _panelsHiddenForSave;
        // Scene save can hand off to VaM screenshot capture asynchronously; keep save mode
        // active until that flow ends so overwrite/screenshot UI is not interrupted.
        private Coroutine _sceneSaveFinalizeCoroutine;
        private bool _sceneSaveSawScreenshotCamera;
        private bool _sceneSaveRehideApplied;
    }
}
