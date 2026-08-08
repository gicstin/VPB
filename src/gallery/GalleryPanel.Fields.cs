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
        internal GameObject backgroundBoxGO;
        private CanvasGroup backgroundCanvasGroup;
        private GameObject contentGO;
        private ScrollRect scrollRect;
        /// <summary>
        /// Filter-randomize / background refresh: rebuild lists without clearing or rebinding the visible grid.
        /// </summary>
        private bool _quietGalleryRefresh;
        private readonly List<FileEntry> _quietDisplayFiles = new List<FileEntry>();
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
        /// <summary>Time-sliced loose .vap gender facet merge for Appearance sub-pane (avoids multi-second category switch stalls).</summary>
        private Coroutine _appearanceLooseMergeCo;
        /// <summary>Background History filter-tab counts (SQLite); avoids multi-second stalls on History toggle.</summary>
        private Coroutine _historyModeCountsCo;

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
        private bool _categoryQuickMenuDirty = true;
        // Dirty-gate for ApplyCategoryQuickChromeLayout (idle Update must not walk menu rows).
        private float _categoryQuickLayoutLastScale = float.NaN;
        private bool _categoryQuickLayoutLastFlush;
        private bool _categoryQuickLayoutLastMenuOpen;
        private int _categoryQuickLayoutLastRowCount = -1;
        private string _categoryQuickMenuLastPath;
        private string _categoryQuickMenuLastExtension;
        private Coroutine _categoryQuickApplyCoroutine;
        private Image _categoryQuickArrowImage;
        private RectTransform _categoryQuickArrowIconRT;
        private LayoutElement _categoryQuickArrowLE;
        private bool _categoryQuickCompact;
        private bool _categoryQuickLayoutLastCompact;

        public List<Gallery.Category> categories = new List<Gallery.Category>();
        private Dictionary<string, string> packageCategoryLabelCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ThumbPlaceholderLabelParts> thumbPlaceholderLabelCache = new Dictionary<string, ThumbPlaceholderLabelParts>(StringComparer.OrdinalIgnoreCase);
        private int _thumbPlaceholderFontSize = GalleryUiDesignTokens.FontBodyRef;
        private int _thumbPlaceholderFontLayoutSig = int.MinValue;

        private List<GameObject> activeButtons = new List<GameObject>();
        private Stack<GameObject> fileButtonPool = new Stack<GameObject>();
        private Stack<GameObject> navButtonPool = new Stack<GameObject>(); // NEW: Separate pool for Nav buttons
        private Dictionary<string, Image> fileButtonImages = new Dictionary<string, Image>();
        private string selectedPath = null;
        private Stack<Action> undoStack = new Stack<Action>();
        private Stack<Action> redoStack = new Stack<Action>();
        private Stack<string> undoLabelStack = new Stack<string>();
        private Stack<string> redoLabelStack = new Stack<string>();
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
        public bool IsSubtreeActive => backgroundBoxGO != null && backgroundBoxGO.activeSelf;
        public bool HasLoadedContent => hasLoadedContent;
        public bool IsStartupInitialRefreshInProgress => refreshCoroutine != null && !hasLoadedContent;

        // Read-only counters for VpbPerfTelemetry. Snapshot only; safe to call any frame.
        public void GetPerfTelemetry(out int pendingThumbCacheJobs, out int thumbCacheTotalEnqueued, out int thumbCacheSaved)
        {
            pendingThumbCacheJobs = pendingThumbnailCacheJobs != null ? pendingThumbnailCacheJobs.Count : 0;
            thumbCacheTotalEnqueued = _thumbCacheTotalEnqueued;
            thumbCacheSaved = _thumbCacheSaved;
        }

        // Returns count of runtime + persistent listeners on scrollRect.onValueChanged, or -1 on failure.
        // Used to detect leaked AddListener subscriptions across panel rebuilds.
        public int GetScrollListenerCountForTelemetry()
        {
            if (scrollRect == null) return -1;
            return VpbPerfTelemetry.CountListeners(scrollRect.onValueChanged);
        }

        // Button-pool / button-list counters for VpbPerfTelemetry retention diagnosis.
        // Pools only grow (Stack pattern, no Shrink). Active lists rebuild on category switch.
        // Tab buttons sum dual-buffer side-tabs (category + creator, left + right) plus active sub-tabs.
        public void GetButtonPoolTelemetry(
            out int fileButtonPoolCount,
            out int navButtonPoolCount,
            out int fileButtonImagesCount,
            out int activeButtonsTotal,
            out int tabButtonsTotal)
        {
            fileButtonPoolCount = fileButtonPool != null ? fileButtonPool.Count : 0;
            navButtonPoolCount = navButtonPool != null ? navButtonPool.Count : 0;
            fileButtonImagesCount = fileButtonImages != null ? fileButtonImages.Count : 0;
            activeButtonsTotal =
                (activeButtons != null ? activeButtons.Count : 0) +
                (leftActiveTabButtons != null ? leftActiveTabButtons.Count : 0) +
                (leftSubActiveTabButtons != null ? leftSubActiveTabButtons.Count : 0) +
                (rightActiveTabButtons != null ? rightActiveTabButtons.Count : 0) +
                (rightSubActiveTabButtons != null ? rightSubActiveTabButtons.Count : 0);
            tabButtonsTotal =
                (leftCategoryTabButtons != null ? leftCategoryTabButtons.Count : 0) +
                (leftCreatorTabButtons != null ? leftCreatorTabButtons.Count : 0) +
                (rightCategoryTabButtons != null ? rightCategoryTabButtons.Count : 0) +
                (rightCreatorTabButtons != null ? rightCreatorTabButtons.Count : 0);
        }

        private bool refreshOnNextShow;
        private bool hasLoadedContent = false;
        // When gallery is unhidden via menu gate (no Show() call), refresh raycaster once.
        private bool _queuedRaycastRefreshOnVisible;
        // VR cold boot: avoid enabling canvas/raycaster before World UI ready.
        private Coroutine _deferredSetVisibleCoroutine;
        private bool _pendingVisibleAfterStartupReady;
        /// <summary>Show() re-entrancy guard — SetCanvasVisible must not recurse into Show.</summary>
        private int _showReentrancyDepth;
        private Dictionary<string, float> categoryScrollPositions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        // Tracks category keys that were written during this app session (not just loaded from disk cache).
        private HashSet<string> sessionCategoryScrollKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private float _pendingScrollRestore = 1f;
        private bool _scrollCacheLoaded = false;
        private DateTime lastAppliedPackageRefreshTime = DateTime.MinValue;
        /// <summary>True when <see cref="ApplyPackageDelta"/> modified the active grid this scan.</summary>
        private bool lastPackageDeltaChangedGrid;
        /// <summary>Package scan time used when category/creator side-tab counts were last built.</summary>
        private DateTime _lastCategoryCountsScanTime = DateTime.MinValue;
        
        // Configuration
        /// <summary>
        /// Transient override for filter-preset multi-random (replace once then add members).
        /// Null = use persisted config. Does not write disk.
        /// Token-scoped so StopCoroutine dispose / superseded gen cannot clear a newer owner.
        /// </summary>
        private static bool? _dragDropReplaceModeOverride;
        private static int _dragDropReplaceOverrideToken;

        /// <summary>
        /// Bumped when filter-preset randomize restarts so deferred clothing/hair toggles
        /// from a superseded LoadRandom abort instead of stacking on top of Replace.
        /// </summary>
        private static int _clothingApplySerial;

        /// <summary>
        /// Count of deferred clothing/hair apply coroutines (preset wait / legacy toggle retry).
        /// Filter-randomize waits for zero so merge steps and rapid re-dice do not race Clear/toggle.
        /// </summary>
        private static int _clothingApplyInFlight;

        public bool DragDropReplaceMode
        {
            get
            {
                if (_dragDropReplaceModeOverride.HasValue)
                    return _dragDropReplaceModeOverride.Value;
                return VPBConfig.Instance != null ? VPBConfig.Instance.DragDropReplaceMode : false;
            }
            set { 
                if (VPBConfig.Instance != null) {
                    VPBConfig.Instance.DragDropReplaceMode = value;
                    // Disk only — Save(true) rebuilds side-rail layout for no reason.
                    try { VPBConfig.Instance.Save(false); } catch { }
                }
            }
        }

        /// <summary>Effective replace/add for clothing apply (honors randomize override).</summary>
        internal static bool EffectiveDragDropReplaceMode
        {
            get
            {
                if (_dragDropReplaceModeOverride.HasValue)
                    return _dragDropReplaceModeOverride.Value;
                return VPBConfig.Instance != null && VPBConfig.Instance.DragDropReplaceMode;
            }
        }

        /// <summary>Capture serial for deferred clothing/hair work; abort if <see cref="InvalidateClothingApplySerial"/> ran.</summary>
        internal static int CaptureClothingApplySerial()
        {
            return _clothingApplySerial;
        }

        /// <summary>True when deferred apply still belongs to current randomize/load generation.</summary>
        internal static bool IsClothingApplySerialCurrent(int serial)
        {
            return serial == _clothingApplySerial;
        }

        /// <summary>Invalidate in-flight deferred clothing/hair applies (rapid re-dice).</summary>
        internal static void InvalidateClothingApplySerial()
        {
            _clothingApplySerial++;
        }

        /// <summary>Mark deferred clothing/hair apply started (pair with <see cref="EndClothingApplyWork"/>).</summary>
        internal static void BeginClothingApplyWork()
        {
            _clothingApplyInFlight++;
        }

        /// <summary>Mark deferred clothing/hair apply finished or aborted.</summary>
        internal static void EndClothingApplyWork()
        {
            if (_clothingApplyInFlight > 0)
                _clothingApplyInFlight--;
        }

        /// <summary>True while deferred clothing/hair apply coroutines still run.</summary>
        internal static bool HasPendingClothingApplyWork()
        {
            return _clothingApplyInFlight > 0;
        }

        /// <param name="token">Owner id (usually filter-randomize gen). End only clears matching token.</param>
        internal static void BeginDragDropReplaceOverride(bool replace, int token)
        {
            _dragDropReplaceModeOverride = replace;
            _dragDropReplaceOverrideToken = token;
        }

        /// <summary>Clear override only if still owned by <paramref name="token"/>.</summary>
        internal static void EndDragDropReplaceOverride(int token)
        {
            if (_dragDropReplaceOverrideToken != token) return;
            _dragDropReplaceModeOverride = null;
            _dragDropReplaceOverrideToken = 0;
        }

        /// <summary>Force-clear override (lifecycle / randomize restart). Ignores token.</summary>
        internal static void ClearDragDropReplaceOverride()
        {
            _dragDropReplaceModeOverride = null;
            _dragDropReplaceOverrideToken = 0;
        }

        public string AppearanceClothingApplyMode
        {
            get { return VPBConfig.Instance != null ? VPBConfig.Instance.AppearanceClothingApplyMode : "replace"; }
            set
            {
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.AppearanceClothingApplyMode = value;
                    try { VPBConfig.Instance.Save(false); } catch { }
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
                    try { VPBConfig.Instance.Save(false); } catch { }
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
        private GameObject leftUserTagsAvailPinnedStickyGO;
        private GameObject rightUserTagsAvailPinnedStickyGO;
        private GameObject leftUserTagsAvailFooterGO;
        private GameObject rightUserTagsAvailFooterGO;
        private GameObject leftUserTagsAppliedStickyGO;
        private GameObject rightUserTagsAppliedStickyGO;
        private GameObject leftUserTagsAppliedPinnedStickyGO;
        private GameObject rightUserTagsAppliedPinnedStickyGO;
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
        /// <summary>Root GO for left creator rail button (null when hide setting — never created).</summary>
        private GameObject leftCreatorSideBtnGO;
        /// <summary>Root GO for right creator rail button (null when hide setting — never created).</summary>
        private GameObject rightCreatorSideBtnGO;
        /// <summary>One-shot purge of creator rail orphans after create/destroy.</summary>
        private bool _creatorSideRailOrphansPurged;
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
        /// <summary>Isolated list zoom while internal Settings panel open; does not persist to <see cref="ListRowHeight"/>.</summary>
        private float internalSettingsListRowHeightSession = GalleryUiDesignTokens.SettingsFloatRowHeightRef;

        private GameObject footerUndoBtnGO;
        private GameObject footerRedoBtnGO;
        private GameObject footerCommandPaletteBtnGO;

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
        private GameObject _confirmOverlayGO;
        private UnityAction _confirmOnConfirm;
        private UnityAction _confirmOnCancel;
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
        /// <summary>Fingerprint of selection + modes that drive tbox action counts; avoids repeating package/dep lookups every tick.</summary>
        private string _tboxConditionalRefreshCacheKey = "";

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
        private readonly HashSet<string> _currentCreatorSet = new HashSet<string>(StringComparer.Ordinal);

        // Title bar creator dropdown
        private GameObject titleCreatorBtn;
        private Image titleCreatorBtnBackdrop;
        private Text titleCreatorBtnText;
        private Image titleCreatorBtnIconImage;
        private GameObject titleCreatorDropdown;
        private GameObject titleCreatorDropdownBlocker;
        private InputField titleCreatorDropdownSearchInput;
        private GameObject titleCreatorRatedOnlyBtn;
        private Image titleCreatorRatedOnlyBtnBackdrop;
        private Text titleCreatorRatedOnlyBtnText;
        private string titleCreatorDropdownFilter = "";
        private ScrollRect _titleCreatorVirtScroll;
        private RectTransform _titleCreatorVirtContentRT;
        private GameObject titleCreatorDropdownHolder;
        private readonly List<CreatorCacheEntry> _titleCreatorVirtView = new List<CreatorCacheEntry>(512);
        private readonly List<GameObject> _titleCreatorVirtButtons = new List<GameObject>(32);
        private int _titleCreatorVirtLastFirstIdx = -1;
        private string _titleCreatorVirtViewSig = null;
        private string currentRatingFilter = "";
        /// <summary>Title-bar Filter license type (VaM meta.json licenseType). Empty = off.</summary>
        private string currentLicenseFilter = "";
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

        // Mirror of VPBConfig.GlobalSourceFilter. Read in init, written by dropdown click handlers,
        // applied as the early gate in PassesFilters. Scene/Appearance side "Local only" mirrors this.
        private VPBConfig.GlobalSourceFilterValue currentGlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;

        /// <summary>Filter menu cycle: Off / Apply (prefer) / Only. Visible 3-way segment (no Shift+click).</summary>
        private enum BrowseFilterCycle : byte
        {
            Off = 0,
            Apply = 1,
            Only = 2
        }

        /// <summary>Loaded path filter: Off / LoadedOnly / UnloadedOnly. Visible 3-way segment.</summary>
        private enum BrowseLoadedMode : byte
        {
            Off = 0,
            LoadedOnly = 1,
            UnloadedOnly = 2
        }

        /// <summary>
        /// Title-bar ★ presence filter. Primary click cycles Off → RatedOnly → UnratedOnly → Off
        /// (VR laser = left only). RMB clears when armed.
        /// </summary>
        private enum RatingPresenceFilterMode : byte
        {
            Off = 0,
            RatedOnly = 1,
            UnratedOnly = 2
        }

        // Title-bar Filter cycles (per-category via CategoryFilterState; settings mirrored when applied).
        private BrowseFilterCycle _browseHiddenCycle;
        private BrowseFilterCycle _browseAlwaysLoadedCycle;
        /// <summary>Default <see cref="BrowseFilterCycle.Apply"/> = newest only (hide old).</summary>
        private BrowseFilterCycle _browseOldVersionsCycle = BrowseFilterCycle.Apply;
        /// <summary>True when current file list was narrowed by SQL <c>pkg.is_newest</c> (skip C# Old-only re-filter).</summary>
        private bool _fileListHadSqlPkgVersionFilter;
        /// <summary>True when current file list was narrowed by SQL <c>pkg.license</c> (skip per-row license PassesFilters).</summary>
        private bool _fileListHadSqlLicenseFilter;
        private BrowseFilterCycle _browseUnusedCycle;
        /// <summary>Cached armed count for Filter button chrome — skip icon reload when unchanged.</summary>
        private int _globalSourceFilterBtnArmedCount = -1;
        /// <summary>Cached label text to avoid redundant Text assigns.</summary>
        private string _globalSourceFilterBtnLabelCached;
        private BrowseLoadedMode _browseLoadedMode;
        /// <summary>Sort restored when Always-loaded Prefer/Only returns to Off.</summary>
        private SortState _browseAlwaysLoadedSavedSort;
        /// <summary>Sort restored when Unused Prefer/Only returns to Off.</summary>
        private SortState _browseUnusedSavedSort;

        private GameObject globalSourceFilterBtn;
        private Text globalSourceFilterBtnText;
        private Image globalSourceFilterBtnIcon;
        private bool _globalSourceFilterCompact;
        private GameObject globalSourceFilterMenuRoot;
        private GameObject globalSourceFilterMenuPanelGO;

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

        // Grid hover cell currently showing rating star.
        private GameObject _gridHoverBadgeBtnGO;
        /// <summary>When set, logs elapsed time when the first file-list load finishes after create/clone pane.</summary>
        private System.Diagnostics.Stopwatch _paneLoadTimingStopwatch;
        private string _paneLoadTimingKind;

        /// <summary>True when SetCategories skipped a full side-tab rebuild; first RefreshFilesRoutine completes it after caches exist.</summary>
        private bool _sideTabsNeedFullRebuildAfterFirstRefresh;
        private bool _deferSideTabCountsForceRefresh;
        private Coroutine _packageDeltaSideTabsCoroutine;
        // Handle to the active clothing/hair subfilter chip's label Text. UpdateSelectionContextMenu
        // rewrites its "(N)" from the live grid count each tick so it stays equal to the bottom "X Items".
        private UnityEngine.UI.Text _activeSubfilterChipText;
        private string _activeSubfilterChipLabelPrefix;
        
        private string nameFilter = "";
        private string nameFilterLower = "";
        // Tokenized search terms (lowercased, whitespace-split). Enables multi-term search like "acid timeline".
        private string[] nameFilterTerms = new string[0];
        /// <summary>Parsed title-bar search (bare OR tags; <c>tag:</c>/<c>creator:</c>/time).</summary>
        private GallerySearchQuery nameFilterQuery = GallerySearchQuery.Empty;
        private Coroutine _titleSearchSqlDebounceCo;
        private Coroutine _titleSearchInMemoryDebounceCo;
        private bool _keepTopSearchBaseAcrossRefresh;
        private bool _searchUserTagVocabEmptyKnown;
        private bool _searchUserTagVocabEmpty;
        private Dictionary<string, HashSet<string>> _searchTagKeysCache;
        private string _searchTagKeysCacheFor;

        // Tagging
        private List<string> currentPaths = new List<string>();
        private HashSet<string> activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// User-tag grid filter include set. Always live when non-empty (orthogonal to F/T work mode).
        /// Tag mode applies via <c>toggleTagForSelectedItems</c> immediately — does not use this set.
        /// FilterUntagged browse ignores include/exclude until dismissed.
        /// </summary>
        private readonly HashSet<string> activeUserTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>User-tag grid filter exclude (none-of). Always live when non-empty; same orthogonality as include.</summary>
        private readonly HashSet<string> excludedUserTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Available pane work mode: Tag (click applies) or FilterByTags (click arms include/exclude).
        /// Does not gate whether include/exclude filter the grid. FilterUntagged is title-bar browse filter.
        /// </summary>
        private UserTagAvailMode _userTagAvailMode = UserTagAvailMode.FilterByTags;
        /// <summary>Work mode restored when title-bar Not tagged filter turns off (Tag or FilterByTags).</summary>
        private UserTagAvailMode _userTagModeBeforeUntagged = UserTagAvailMode.FilterByTags;
        /// <summary>Filter-mode Available list: expand collapsed Unused bucket (zero-count tags).</summary>
        private bool _userTagShowUnusedBucket;
        /// <summary>Guard against recursive title↔filter user-tag chip bridging.</summary>
        private bool _bridgingUserTagFilterTitleSearch;
        /// <summary>Not Tagged filter: selection keys kept visible after tagging until deselected (avoids per-click grid SQLite scan).</summary>
        private readonly HashSet<string> _untaggedTaggedPinKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        /// <summary>True when side-tab per-category counts query succeeded (gate hide-unused; independent of <see cref="userTagsCached"/>).</summary>
        private bool _userTagSideTabCountsReady = false;
        /// <summary>Cached: any <c>gallery_item_user_tag</c> row exists. False on fresh/wiped DB (skip hide-unused).</summary>
        private bool _userTagAnyAssignmentExists = false;
        /// <summary>Bumped when <see cref="GalleryPanel.CacheUserTagsSideTab"/> finishes rebuilding SQLite-backed rows.</summary>
        private int userTagSideTabDataRevision = 0;
        private List<UserTagSideTabEntry> cachedUserTagSideTab = new List<UserTagSideTabEntry>(64);

        /// <summary>Filtered/sorted view for virtualized User Tags pick list (same role as <see cref="_creatorVirtView"/>).</summary>
        /// <summary>Create-tag row + pinned tags; fixed above scroll (not virtualized).</summary>
        private readonly List<UserTagSideTabEntry> _userTagStickyRows = new List<UserTagSideTabEntry>(24);
        private readonly List<UserTagSideTabEntry> _userTagAppliedPinnedRows = new List<UserTagSideTabEntry>(24);
        private readonly List<UserTagSideTabEntry> _userTagVirtView = new List<UserTagSideTabEntry>(256);
        private string _userTagVirtViewSig = null;
        // Visible-window cache for the scroll-jitter gate; only scroll callbacks consult it, forced repaints overwrite it.
        private int _lastUserTagVirtFirstIdxLeft = int.MinValue;
        private int _lastUserTagVirtFirstIdxRight = int.MinValue;
        private int _lastUserTagVirtVisibleLeft = -1;
        private int _lastUserTagVirtVisibleRight = -1;
        private int _lastUserTagVirtTotalLeft = -1;
        private int _lastUserTagVirtTotalRight = -1;
        private int _userTagPinRevision = 0;
        private readonly List<string> _userTagPinOrderRuntime = new List<string>(24);
        private bool _userTagPinOrderRuntimeLoaded;
        private Coroutine _userTagPinSaveCo;
        private Coroutine _userTagVirtLayoutCo;
        private Sprite _userTagPinOnSprite;
        private Sprite _userTagPinOffSprite;
        private readonly List<GameObject> _leftUserTagVirtButtons = new List<GameObject>(32);
        private readonly List<GameObject> _rightUserTagVirtButtons = new List<GameObject>(32);
        private ScrollRect _leftUserTagVirtScroll;
        private ScrollRect _rightUserTagVirtScroll;
        private bool _leftUserTagVirtHooked;
        private bool _rightUserTagVirtHooked;

        private enum UserTagSelectionState { Off, On, Mixed }
        private readonly Dictionary<string, UserTagSelectionState> _userTagSelectionStates = new Dictionary<string, UserTagSelectionState>(StringComparer.OrdinalIgnoreCase);
        private int _userTagSelectionRowCount = 0;
        private readonly Dictionary<string, float> _userTagButtonLastClickTime = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private string _userTagPulseTag = null;
        private float _userTagPulseUntil = 0f;
        private FileEntry _userTagDragHoverFile = null;
        private string _userTagDropPulseKey = null;
        private float _userTagDropPulseUntil = 0f;
        private Coroutine _userTagVisualPulseCoroutine;

        private float _scanWlBadgePulseUntil = 0f;
        private HashSet<string> _scanWlBadgePulseUids;
        private Coroutine _scanWlBadgePulseCoroutine;
        private const float ScanWlBadgePulseSeconds = 0.55f;

        private static readonly Color UserTagStateOnColor = GalleryUiColorTokens.UserTagOn;
        private static readonly Color UserTagStateMixedColor = GalleryUiColorTokens.UserTagMixed;
        private static readonly Color UserTagStatePulseColor = GalleryUiColorTokens.UserTagPulse;
        private static readonly Color UserTagFilterActiveColor = GalleryUiColorTokens.UserTagFilterOn;
        private static readonly Color UserTagFilterExcludedColor = GalleryUiColorTokens.UserTagFilterExclude;
        private static readonly Color UserTagDropGlowColor = GalleryUiColorTokens.UserTagDropGlow;
        private const float UserTagVisualPulseSeconds = 0.65f;
        private const string UserTagDropFlashName = "VPB_UserTagDropFlash";

        /// <summary>Unified tag editor root (DetailStripTagMenu). Legacy overlay name retired.</summary>
        private GameObject _userTagEditorRoot;
        private Transform _userTagEditorRowsParent;
        private InputField _userTagEditorFilterInput;
        private InputField _userTagEditorNewTagInput;
        private Image _userTagEditorNewTagInputChrome;
        private Coroutine _userTagEditorNewTagFlashCo;
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
            CustomPreset = 1 << 7,
        }

        [Flags]
        internal enum HairSubfilter
        {
            Presets = 1 << 0,
            Items = 1 << 1,
            Male = 1 << 2,
            Female = 1 << 3,
            Custom = 1 << 4,
            CustomPreset = 1 << 5,
        }

        [Flags]
        internal enum AppearanceSubfilter
        {
            Presets = 1 << 0,
            Custom = 1 << 1,
            Male = 1 << 2,
            Female = 1 << 3,
            Futa = 1 << 4,
            Unknown = 1 << 5,
        }

        private enum PosePeopleFilter
        {
            All,
            Single,
            Dual,
        }

        private HashSet<string> _cslistReferencedPaths;
        private readonly object _cslistReferencedLock = new object();

        private ClothingSubfilter clothingSubfilter = 0;
        private HairSubfilter hairSubfilter = 0;

        private bool _clothingGenderUserOverride;
        private bool _hairGenderUserOverride;

        private AppearanceSubfilter appearanceSubfilter = 0;

        private int clothingSubfilterCountAll = 0;
        private int clothingSubfilterCountReal = 0;
        private int clothingSubfilterCountPresets = 0;
        private int clothingSubfilterCountCustom = 0;
        private int clothingSubfilterCountCustomPreset = 0;
        private int clothingSubfilterCountItems = 0;
        private int clothingSubfilterCountMale = 0;
        private int clothingSubfilterCountFemale = 0;
        private int clothingSubfilterCountDecals = 0;

        private int clothingSubfilterFacetCountReal = 0;
        private int clothingSubfilterFacetCountPresets = 0;
        private int clothingSubfilterFacetCountCustom = 0;
        private int clothingSubfilterFacetCountCustomPreset = 0;
        private int clothingSubfilterFacetCountItems = 0;
        private int clothingSubfilterFacetCountMale = 0;
        private int clothingSubfilterFacetCountFemale = 0;
        private int clothingSubfilterFacetCountDecals = 0;

        private int hairSubfilterCountAll = 0;
        private int hairSubfilterCountPresets = 0;
        private int hairSubfilterCountCustom = 0;
        private int hairSubfilterCountCustomPreset = 0;
        private int hairSubfilterCountItems = 0;
        private int hairSubfilterCountMale = 0;
        private int hairSubfilterCountFemale = 0;

        private int hairSubfilterFacetCountPresets = 0;
        private int hairSubfilterFacetCountCustom = 0;
        private int hairSubfilterFacetCountCustomPreset = 0;
        private int hairSubfilterFacetCountItems = 0;
        private int hairSubfilterFacetCountMale = 0;
        private int hairSubfilterFacetCountFemale = 0;

        private int appearanceSubfilterCountAll = 0;
        private int appearanceSubfilterCountPresets = 0;
        private int appearanceSubfilterCountCustom = 0;
        private int appearanceSubfilterCountMale = 0;
        private int appearanceSubfilterCountFemale = 0;
        private int appearanceSubfilterCountFuta = 0;
        private int appearanceSubfilterCountUnknown = 0;

        private int appearanceSubfilterFacetCountPresets = 0;
        private int appearanceSubfilterFacetCountCustom = 0;
        private int appearanceSubfilterFacetCountMale = 0;
        private int appearanceSubfilterFacetCountFemale = 0;
        private int appearanceSubfilterFacetCountFuta = 0;
        private int appearanceSubfilterFacetCountUnknown = 0;

        private int appearanceSubfilterCurrentCountAll = 0;
        private int appearanceSubfilterCurrentCountMale = 0;
        private int appearanceSubfilterCurrentCountFemale = 0;
        private int appearanceSubfilterCurrentCountFuta = 0;
        private int appearanceSubfilterCurrentCountUnknown = 0;

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
        private Image _titleSearchPopupPanelImg;
        private InputField _titleSearchPopupField;
        private bool _titleSearchPopupOpen;
        /// <summary>Frame popup opened; outside-click dismiss skips this frame + next.</summary>
        private int _titleSearchPopupOpenedFrame = -1;
        /// <summary>Unscaled end time for open-cue flash; 0 = idle.</summary>
        private float _titleSearchPopupCueUntil;
        private static readonly Color TitleSearchPopupPanelIdle = GalleryUiColorTokens.TitleSearchPopupIdle;
        private static readonly Color TitleSearchPopupPanelCue = GalleryUiColorTokens.TitleSearchPopupCue;
        private const float TitleSearchPopupCueSeconds = 0.28f;

        /// <summary>Soft-undo for search clear now uses main PushUndo stack (label "Search clear").</summary>
        private int targetDropdownValue = 0;
        private List<string> targetDropdownOptions = new List<string>();
        private List<GameObject> tboxPersonAtomBtns = new List<GameObject>();
        private GameObject tboxTargetMenuRootGO;
        private GameObject tboxTargetMenuPanelGO;
        private RectTransform tboxTargetMenuPanelRT;
        private GameObject tboxTargetDropdownRowGO;
        private Text tboxTargetDropdownBtnText;
        private bool tboxTargetMenuOpen;

        private AppearanceGender GetAppearanceGender(FileEntry entry)
        {
            if (entry == null) return AppearanceGender.Unknown;
            string cat = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            cat = cat ?? "";
            EnsureAppearanceGenderRefreshCaches(cat);
            string uid = entry.Uid ?? "";
            if (!string.IsNullOrEmpty(uid) && _appearanceGenderByUid != null)
            {
                AppearanceGender cached;
                if (_appearanceGenderByUid.TryGetValue(uid, out cached)) return cached;
            }
            AppearanceGender g = AppearanceGenderClassifier.Classify(entry, cat, _appearanceUserTagsByRowKey);
            if (!string.IsNullOrEmpty(uid) && _appearanceGenderByUid != null)
                _appearanceGenderByUid[uid] = g;
            return g;
        }
        private List<Atom> personAtoms = new List<Atom>();
        private System.Collections.Generic.List<System.Action<float>> innerPaneScaleActions = new System.Collections.Generic.List<System.Action<float>>();
        private GameObject leftSideContainer;
        private GameObject rightSideContainer;
        private GameObject leftSideHoverStrip;
        private GameObject rightSideHoverStrip;
        /// <summary>Thin bars between Layout / Browse / Tools (and tools context) on each rail.</summary>
        private GameObject[] _leftSideZoneSeps;
        private GameObject[] _rightSideZoneSeps;
        private const int SideRailZoneSepSlots = 3;
        /// <summary>Full-height invisible hit targets beside side buttons (half legacy 130px width).</summary>
        private const float GallerySideHoverStripWidth = GalleryUiDesignTokens.SideHoverStripWidthRef;
        private const float GallerySideHoverStripOffset = GalleryUiDesignTokens.SideHoverStripOffsetRef;
        private Stack<GameObject> tabButtonPool = new Stack<GameObject>();

        private List<CanvasGroup> sideButtonGroups = new List<CanvasGroup>();
        private float sideButtonsAlpha = 1f;
        private float sideButtonsFadeDelayTimer = 0f;
        private const float SideButtonsFadeDelay = 0.6f;
        private bool isResizing = false;
        private int hoverCount = 0;
        private UIDraggable dragger;
        private GameObject pointerDotGO;
        internal PointerEventData currentPointerData;

        private RectTransform previewBorderRT;
        private float fpsTimer = 0f;
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
        private RectTransform _titleBarFileSortDirBtnRT;
        private RectTransform _titleBarRatingSortToggleBtnRT;
        private RectTransform _titleBarRefreshBtnRT;
        private RectTransform _titleBarFpsRT;
        private RectTransform _titleBarHelpBtnRT;
        private RectTransform _titleBarMinimizeBtnRT;
        private RectTransform _titleBarCloseBtnRT;

        // Dirty-gate for ApplyTitleBarResponsiveLayout (idle Update must not ForceUpdateCanvases).
        private float _titleBarLayoutLastScale = float.NaN;
        private float _titleBarLayoutLastW = float.NaN;
        private float _titleBarLayoutLastFpsW = float.NaN;
        private bool _titleBarLayoutLastOverflow;
        private bool _titleBarLayoutLastCatShown;
        private bool _titleBarLayoutLastFlushLeft;
        private bool _titleBarLayoutLastHasSource;

        // Fixed desktop dock "Top": side rail buttons live on footer bar.
        private GameObject _footerSideButtonsGroupGO;
        private RectTransform _footerSideButtonsGroupRT;
        private LayoutElement _footerSideButtonsGroupLE;
        private RectTransform _footerCenterSectionRT;
        private HorizontalLayoutGroup _footerCenterHLG;
        private RectTransform _footerPerfGroupRT;
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

        // Side buttons for dynamic positioning
        private List<RectTransform> rightSideButtons = new List<RectTransform>();
        private List<RectTransform> leftSideButtons = new List<RectTransform>();
        /// <summary>Side-rail Tags (UserTags) buttons — for layout / edge-align.</summary>
        private GameObject leftUserTagsSideBtn;
        private GameObject rightUserTagsSideBtn;
        /// <summary>Side-rail Scene Import buttons — above Tags; toggle Import sidebar.</summary>
        private GameObject leftSceneImportSideBtn;
        private GameObject rightSceneImportSideBtn;
        /// <summary>True during <see cref="Gallery.ClonePanel"/> init — skip global import-open restore and config import defaults.</summary>
        internal bool importSidebarInitAsClone;

        private Sprite galleryCreatorOffSprite;

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

        // Footer bar
        private RectTransform paginationRT;
        private HorizontalLayoutGroup footerHLG;

        // Resize handles (seated alongside the bar buttons; one corner slot, mode picks which is active)
        private GameObject _resizeHandleBottomLeftGO;        // floating BL (footer left slot)
        private GameObject _resizeHandleBottomRightGO;       // floating BR (footer right slot)
        private GameObject _resizeHandleTopLeftGO;           // floating TL (title bar far-left)
        private GameObject _resizeHandleFixedBottomGO;       // fixed Right/Top dock (footer left slot)
        private GameObject _resizeHandleFixedBottomRightGO;  // fixed Left dock (footer right slot)
        /// <summary>Cached comps for fixed-dock Update — avoid GetComponent every frame.</summary>
        private RectTransform _backgroundBoxRT;
        private RectTransform _collapseTriggerRT;
        private RectTransform _collapseTriggerLeftRT;
        private RectTransform _collapseTriggerTopRT;
        private UIAnchorResizer _fixedBottomResizer;
        private UIAnchorResizer _fixedBottomRightResizer;
        private string _fixedDockHandleIconKey;
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
        /// <summary>
        /// UIHoverReveal that currently owns the hover-path row. Deferred grid exit must not
        /// clear path when pointer already moved onto another cell (sibling enter claims first).
        /// </summary>
        private UIHoverReveal _hoverPathRevealOwner;
        // Hover preview overlay (canvas-local X/Y offset + size; drag placeholder in settings)
        private GameObject hoverPreviewGO;
        private RectTransform hoverPreviewRT;
        private RawImage hoverPreviewImage;
        private Image hoverPreviewBgImage;
        private Text hoverPreviewHintText;
        private FileEntry hoverPreviewFile;
        private bool hoverPreviewDummyActive;
        private UIHoverPreviewTrigger hoverPreviewSource;
        private Vector2 hoverPreviewDragGrabLocal;
        private bool hoverPreviewDragging;
        /// <summary>Swallow the click that ends a placeholder drag so it cannot step a settings row underneath.</summary>
        private bool hoverPreviewSuppressSettingsClick;
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

        // Floating-pane helper: big spring scroll button next to scrollbar (settings: Off / Desktop Only / VR Only / Desktop & VR).
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
            if (settingsListViewActive)
                return internalSettingsListRowHeightSession;
            return ListRowHeight;
        }

        /// <summary>Thumb decode density: settings list acts as single-column (widest) regardless of saved grid columns.</summary>
        private int EffectiveGridColumnsForThumbDecode()
        {
            if (settingsListViewActive)
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
        private List<FileEntry> lastFilteredFiles = new List<FileEntry>();

        /// <summary>Scratch build list for <see cref="RefreshFilesRoutine"/> — Clear+reuse; never share with snapshot cache storage.</summary>
        private readonly List<FileEntry> _refreshBuildFiles = new List<FileEntry>(4096);
        /// <summary>Scratch for loose-disk search roots during refresh.</summary>
        private readonly List<string> _refreshPathsToSearch = new List<string>(16);
        /// <summary>Scratch for SafeGetFiles during refresh loose-file scan.</summary>
        private readonly List<string> _refreshSysFilePathScratch = new List<string>(512);
        /// <summary>Scratch for sys-cache row filter during refresh.</summary>
        private readonly List<VpbLocalDatabase.SystemFileRow> _refreshSysRowsToKeepScratch = new List<VpbLocalDatabase.SystemFileRow>(256);
        /// <summary>Scratch for writing sys-file cache during refresh.</summary>
        private readonly List<VpbLocalDatabase.SystemFileRow> _refreshSysRowsForWriteScratch = new List<VpbLocalDatabase.SystemFileRow>(256);
        /// <summary>Scratch for SQLite sys-file cache read during refresh.</summary>
        private readonly List<VpbLocalDatabase.SystemFileRow> _refreshSysCachedRowsScratch = new List<VpbLocalDatabase.SystemFileRow>(256);
        /// <summary>Scratch sorted copy of search roots for sys-cache key/sig.</summary>
        private readonly List<string> _refreshPathKeySortScratch = new List<string>(16);

        /// <summary>Post-drain file list before hide-strip (same as <see cref="lastFilteredFiles"/> after full refresh). Used to toggle &quot;show hidden&quot; without re-running <see cref="GalleryPanel.RefreshFilesRoutine"/>.</summary>
        private List<FileEntry> galleryFilesPreHideSnapshot = new List<FileEntry>();

        private bool galleryPreHideSnapshotValid = false;

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
        #pragma warning restore CS0414
        
        // Content-type accent colors — <see cref="GalleryUiColorTokens"/> facets.
        public static readonly Color ColorCategory = GalleryUiColorTokens.FacetCategory;
        public static readonly Color ColorCreator = GalleryUiColorTokens.FacetCreator;
        public static readonly Color ColorTagFilter = GalleryUiColorTokens.FacetTag;
        public static readonly Color ColorRatingFilter = GalleryUiColorTokens.FacetRating;
        /// <summary>★ Not-rated filter armed (slate; distinct from rated purple accent).</summary>
        public static readonly Color ColorUnratedFilterAccent = GalleryUiColorTokens.FacetUnrated;
        /// <summary>★ Not-rated label/text tint on title-bar chrome.</summary>
        public static readonly Color ColorUnratedFilterLabel = GalleryUiColorTokens.FacetUnratedLabel;
        /// <summary>★ Not-rated icon tint when presence filter armed.</summary>
        public static readonly Color ColorUnratedFilterIcon = GalleryUiColorTokens.FacetUnratedIcon;
        public static readonly Color ColorSourceFilter = GalleryUiColorTokens.FacetSource;
        public static readonly Color ColorSubfilterFilter = GalleryUiColorTokens.FacetSubfilter;
        public static readonly Color ColorUserTagFilter = GalleryUiColorTokens.FacetUserTag;
        public static readonly Color ColorTitleSearchBackdropIdle = GalleryUiColorTokens.SurfaceDeep;
        public static readonly Color ColorTitleSearchFilterActive = GalleryUiColorTokens.FacetTitleSearchActive;
        public static readonly Color ColorPath = GalleryUiColorTokens.FacetPath;
        public static readonly Color ColorHistory = GalleryUiColorTokens.FacetHistory;
        public static readonly Color ColorHistoryAccent = GalleryUiColorTokens.FacetHistoryAccent;
        /// <summary>Scene Import side-rail button backdrop (idle + active).</summary>
        public static readonly Color ColorSceneImport = GalleryUiColorTokens.FacetSceneImport;
        public static readonly Color ColorHub = GalleryUiColorTokens.FacetHub;
        public static readonly Color ColorLicense = GalleryUiColorTokens.FacetLicense;

        private string dragStatusMsg = null;
        private string temporaryStatusMsg = null;
        private Coroutine temporaryStatusCoroutine = null;
        private GameObject temporaryStatusOwner = null;
        // Sticky hover tip: instant show; hide grace only (no show dwell / fade).
        private string _stickyTipDesired;
        private GameObject _stickyTipDesiredOwner;
        private bool _stickyTipHidePending;
        private float _stickyTipHideAt;

        public bool isFixedLocally = false;
        private bool isCollapsed = false;
        /// <summary>Fixed-mode dock collapse strip and &lt; &gt; bar (half legacy 60px).</summary>
        private const float FixedCollapseTriggerThickness = 30f;
        private const float FixedCollapseTriggerChamferSize = 50f;
        private const int FixedCollapseTriggerArrowFontSize = GalleryUiDesignTokens.FontBodyRef;
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
        /// <summary>Stable browse-memory key. Assigned by <see cref="Gallery.AddPanel"/> — never GetHashCode (that broke SQL restore after Close/restart).</summary>
        private string _panelId = null;
        internal const string PrimaryPanelId = "panel_0";
        private Coroutine _deferredCollapseLayoutCo;

        private string PanelId
        {
            get
            {
                if (string.IsNullOrEmpty(_panelId))
                    _panelId = PrimaryPanelId;
                return _panelId;
            }
        }

        /// <summary>Gallery assigns slot ids (<c>panel_0</c>, <c>panel_1</c>, …) so filter SQL survives Close + recreate.</summary>
        internal void AssignStablePanelId(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _panelId = id;
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

        private GameObject footerWatchToggleBtn;
        private Image footerWatchToggleBtnImage;
        private Image footerWatchToggleIconImage;
        private Sprite footerWatchToggleOnSprite;
        private Sprite footerWatchToggleOffSprite;

        private bool _userHidden = false;
        private bool _hiddenByMenuGate = false;
        
        private Text fileSortBtnText; // NEW
        private Text fileSortTypeText; // Sort Type button text (Az/Dt/Sz/Rt)
        private Text fileSortDirText; // Optional; null when direction shown as icon only
        private Image fileSortDirIconImage; // Asc/desc glyph on file-sort direction button
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
        private RatingPresenceFilterMode _ratingPresenceFilterMode;
        /// <summary>Scratch for rating-mutate prune (reuse; warm path, no per-click HashSet churn).</summary>
        private readonly List<FileEntry> _ratingMutatedScratch = new List<FileEntry>(8);
        private readonly List<FileEntry> _ratingPruneSurvivingSelected = new List<FileEntry>(8);
        private HashSet<FileEntry> _ratingPruneRemoveRefs;
        private HashSet<string> _ratingPruneRemoveKeys;

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
