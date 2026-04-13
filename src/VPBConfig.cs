using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;

namespace VPB
{
    public class VPBConfig
    {
        private static VPBConfig _instance;
        private static string s_LastLoggedSavedGalleryCategory;
        private static string s_LastLoggedLoadedGalleryCategory;

        /// <summary>
        /// When &gt; 0, the next <see cref="GalleryPanel.UpdateTabs"/> on each gallery pane may skip rebuilding category/creator/tag side-tab buttons.
        /// Reset by <see cref="Save(bool,bool)"/> / <see cref="TriggerChange"/> so stale values cannot leak across failed saves or mis-ordered calls.
        /// </summary>
        private int _lightweightGalleryTabRefreshSlotsRemaining;

        public static bool IsLogConfigPerfEnabled()
        {
            try
            {
                return Settings.Instance != null && Settings.Instance.LogConfigPerf != null && Settings.Instance.LogConfigPerf.Value;
            }
            catch
            {
                return false;
            }
        }

        private static bool LogConfigPerfVerbose()
        {
            return IsLogConfigPerfEnabled();
        }

        private static void LogPerfSave(string pathForLog, bool notifyListeners, bool lightTabsHint, long buildMs, long toStringMs, long diskMs, long notifyMs, long totalMs)
        {
            bool verbose = LogConfigPerfVerbose();
            bool slow = totalMs >= 75 || notifyMs >= 50 || diskMs >= 25;
            if (!verbose && !slow)
                return;
            string msg = "[VPBConfig.Perf] Save total=" + totalMs + "ms build=" + buildMs + "ms toString=" + toStringMs + "ms disk=" + diskMs + "ms ConfigChanged=" + notifyMs + "ms notifyListeners=" + notifyListeners + " lightTabsHint=" + lightTabsHint + " path=" + pathForLog;
            if (slow && !verbose)
                LogUtil.LogWarning(msg);
            else
                LogUtil.Log(msg);
        }

        private static void LogPerfTriggerChange(long notifyMs)
        {
            bool verbose = LogConfigPerfVerbose();
            if (!verbose && notifyMs < 50)
                return;
            string msg = "[VPBConfig.Perf] TriggerChange ConfigChanged=" + notifyMs + "ms (no disk write)";
            if (!verbose && notifyMs >= 50)
                LogUtil.LogWarning(msg);
            else
                LogUtil.Log(msg);
        }

        private static void LogPerfLoad(string pathForLog, long totalMs, bool fileExisted)
        {
            if (!LogConfigPerfVerbose() || !fileExisted)
                return;
            LogUtil.Log("[VPBConfig.Perf] Load total=" + totalMs + "ms path=" + pathForLog);
        }

        private static string DescribeConfigChangedHandler(Delegate d)
        {
            if (d == null)
                return "?";
            var m = d.Method;
            string typeName = m.DeclaringType != null ? m.DeclaringType.Name : "?";
            string s = typeName + "." + m.Name;
            UnityEngine.Object uo = d.Target as UnityEngine.Object;
            if (uo != null)
                s += " (inst=" + uo.GetInstanceID() + ")";
            else if (d.Target != null)
                s += " (tgt=" + d.Target.GetHashCode() + ")";
            return s;
        }

        /// <summary>Runs <see cref="ConfigChanged"/> subscribers one-by-one with per-handler timing (same order as +=).</summary>
        /// <returns>Wall time for all handlers (ms).</returns>
        private long InvokeConfigChangedWithPerfLogging(string context)
        {
            Delegate[] list = ConfigChanged != null ? ConfigChanged.GetInvocationList() : null;
            if (list == null || list.Length == 0)
                return 0;

            ConfigChangedInvocationDepth++;
            try
            {
                bool verbose = LogConfigPerfVerbose();
                Stopwatch swTotal = Stopwatch.StartNew();
                int n = list.Length;
                long[] msEach = new long[n];
                string[] names = new string[n];

                for (int i = 0; i < n; i++)
                {
                    names[i] = DescribeConfigChangedHandler(list[i]);
                    Stopwatch sw = Stopwatch.StartNew();
                    try
                    {
                        ((OnConfigChanged)list[i]).Invoke();
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError("[VPB] ConfigChanged handler threw | " + names[i] + " | " + ex.Message);
                    }
                    msEach[i] = sw.ElapsedMilliseconds;
                }

                long totalMs = swTotal.ElapsedMilliseconds;
                bool logAll = verbose || totalMs >= 50;

                try
                {
                    int detailLines = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (logAll || msEach[i] >= 25)
                        {
                            LogUtil.Log("[VPBConfig.Perf] ConfigChanged+" + context + " [" + (i + 1) + "/" + n + "] " + names[i] + " " + msEach[i] + "ms");
                            detailLines++;
                        }
                    }
                    if (detailLines > 0 && n > 1)
                        LogUtil.Log("[VPBConfig.Perf] ConfigChanged+" + context + " wall=" + totalMs + "ms for " + n + " handlers");
                }
                catch { }

                return totalMs;
            }
            finally
            {
                ConfigChangedInvocationDepth--;
            }
        }

        public static void ReloadFromDisk()
        {
            _instance = null;
        }

        public static string ReadLastGalleryCategoryFromDisk()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(baseDir, "Saves");
                saveDir = Path.Combine(saveDir, "PluginData");
                saveDir = Path.Combine(saveDir, "VPB");
                string path = Path.Combine(saveDir, "VPB.cfg");
                if (!File.Exists(path)) return "";

                string json = File.ReadAllText(path);
                JSONNode node = JSON.Parse(json);
                if (node == null) return "";
                if (node["LastGalleryCategory"] == null) return "";
                return node["LastGalleryCategory"].Value;
            }
            catch
            {
                return "";
            }
        }
        public static VPBConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VPBConfig();
                    _instance.Load();
                }
                return _instance;
            }
        }

        public string ConfigPathForDebug => ConfigPath;

        private string ConfigPath
        {
            get
            {
                // Use PluginData for persistence (works reliably even with hot reloads / read-only Custom folders).
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(baseDir, "Saves");
                saveDir = Path.Combine(saveDir, "PluginData");
                saveDir = Path.Combine(saveDir, "VPB");
                if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
                return Path.Combine(saveDir, "VPB.cfg");
            }
        }

        // Settings
        public bool EnableButtonGaps = true;
        public string ShowSideButtons = "Both"; // "Both", "Left", "Right"
        public string _followAngle = "Both"; // "Off", "Desktop", "VR", "Both"
        public string FollowAngle
        {
            get { return _followAngle; }
            set { _followAngle = value; }
        }
        public string _followDistance = "VR"; // "Off", "Desktop", "VR", "Both"
        public string FollowDistance
        {
            get { return _followDistance; }
            set { _followDistance = value; }
        }
        public string _followEyeHeight = "VR"; // "Off", "Desktop", "VR", "Both"
        public string FollowEyeHeight
        {
            get { return _followEyeHeight; }
            set { _followEyeHeight = value; }
        }
        public float BringToFrontDistance = 1.5f;
        public float ReorientStartAngle = 20f;
        public float MovementThreshold = 0.1f;
        public bool EnableGalleryFade = true;
        public bool EnableGalleryTranslucency = false;
        /// <summary>When true, package scans do not update the gallery until the user uses Refresh.</summary>
        public bool GalleryManualRefreshOnly = true;
        public float GalleryOpacity = 1.0f;
        public bool DragDropReplaceMode = false;
        /// <summary>How gallery applies an appearance .vap: replace (full), keep (keep body garments), clothingOnly (garment outfit from preset only).</summary>
        private string _appearanceClothingApplyMode = "replace";
        public string AppearanceClothingApplyMode
        {
            get { return string.IsNullOrEmpty(_appearanceClothingApplyMode) ? "replace" : _appearanceClothingApplyMode; }
            set
            {
                if (string.IsNullOrEmpty(value)) { _appearanceClothingApplyMode = "replace"; return; }
                string v = value.Trim().ToLowerInvariant();
                if (v == "keep" || v == "replace" || v == "clothingonly")
                    _appearanceClothingApplyMode = v;
                else
                    _appearanceClothingApplyMode = "replace";
            }
        }
        /// <summary>True when <see cref="AppearanceClothingApplyMode"/> is keep. Setting false forces replace; true forces keep.</summary>
        public bool KeepClothingWhenApplyingAppearance
        {
            get { return string.Equals(AppearanceClothingApplyMode, "keep", StringComparison.OrdinalIgnoreCase); }
            set { AppearanceClothingApplyMode = value ? "keep" : "replace"; }
        }
        /// <summary>Gallery item drag-and-drop to atoms/scene. Off by default (VR jitter / accidental drags); enable in Settings → Interaction.</summary>
        public bool EnableDragDrop = false;
        /// <summary>When false (default), drag starts immediately once drag-and-drop is on (legacy). When true, <see cref="DragHoldThreshold"/> is enforced.</summary>
        public bool RequireDragHoldBeforeMove = false;
        public float DragHoldThreshold = 0.5f;
        public string ApplyMode = "DoubleClick";
        public string LastGalleryCategory = "";
        /// <summary>Category when opening a new gallery pane or at session first open: "Scenes" (default), "Clothing", "Hair", "Pose", "Appearance", "Plugins", or "LastUsed".</summary>
        public string InitialGalleryCategory = "Scenes";

        private static readonly string[] s_InitialGalleryCategoryCanonical = { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" };

        /// <summary>When false, plugin rows (.cs/.cslist/.dll under Custom/Scripts) show no thumbnail in the grid/list; selection info box can still show a sister .jpg/.png preview.</summary>
        public bool PluginGalleryGridThumbnails = true;
        /// <summary>When true, gallery list layout uses each item's file name (legacy). When false (default), .var rows show Creator.Package.Version (package uid, no .var suffix).</summary>
        public bool GalleryListNamesLegacyFileName = false;
        /// <summary>Which layout(s) show the hover preview. Off, List, Grid, or Both. Default: List.</summary>
        public string GalleryHoverPreviewMode = "List";
        /// <summary>Square preview size (pixels) for List layout hover preview.</summary>
        public float GalleryListHoverPreviewSize = 300f;
        /// <summary>X offset (pixels) from the default bottom-left dock point for the List layout hover preview.</summary>
        public float GalleryListHoverPreviewOffsetX = 0f;
        /// <summary>Y offset (pixels) from the default bottom-left dock point for the List layout hover preview.</summary>
        public float GalleryListHoverPreviewOffsetY = 0f;
        /// <summary>When true, the gallery selection toolbar (tbox) pin stays on across sessions until turned off manually.</summary>
        public bool GalleryTboxToolbarPinned = false;
        /// <summary>When true, gallery pane only shows while the VaM menu (main HUD) is visible.</summary>
        public bool GalleryOnlyWhenVamMenuVisible = false;

        // Interaction toggles (persisted)
        public bool SpringScrollButtonEnabled = true;
        public bool HoldToLaunchEnabled = false;
        /// <summary>When HoldToLaunch is enabled, drag&drop is forced off; this stores the prior setting for restore.</summary>
        public bool HoldToLaunchPrevEnableDragDrop = false;

        private static readonly string[] s_HoverPreviewModeCanonical = { "Off", "List", "Grid", "Both" };
        public static string NormalizeHoverPreviewMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "List";
            string v = value.Trim();
            for (int i = 0; i < s_HoverPreviewModeCanonical.Length; i++)
            {
                if (string.Equals(v, s_HoverPreviewModeCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_HoverPreviewModeCanonical[i];
            }
            return "List";
        }

        /// <summary>Maps user/config values to a canonical option; unknown values become "Scenes".</summary>
        public static string NormalizeInitialGalleryCategory(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Scenes";
            string v = value.Trim();
            for (int i = 0; i < s_InitialGalleryCategoryCanonical.Length; i++)
            {
                if (string.Equals(v, s_InitialGalleryCategoryCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_InitialGalleryCategoryCanonical[i];
            }
            return "Scenes";
        }

        /// <summary>Resolved tab for a new pane or first gallery open this session: a category name, or null when <see cref="InitialGalleryCategory"/> is LastUsed (restore saved tab).</summary>
        public string ResolveInitialGalleryCategoryName()
        {
            string n = NormalizeInitialGalleryCategory(InitialGalleryCategory);
            if (string.Equals(n, "LastUsed", StringComparison.OrdinalIgnoreCase))
                return null;
            return n;
        }

        /// <summary>Which list opens on the left when a gallery pane is created: None, Category, or Creator.</summary>
        public string GalleryDefaultLeftSidePanel = "None";
        /// <summary>Which list opens on the right when a gallery pane is created: None, Category, or Creator.</summary>
        public string GalleryDefaultRightSidePanel = "None";

        private static readonly string[] s_GallerySidePanelCanonical = { "None", "Category", "Creator" };

        /// <summary>Maps user/config values to None, Category, or Creator.</summary>
        public static string NormalizeGallerySidePanel(string value)
        {
            if (string.IsNullOrEmpty(value)) return "None";
            string v = value.Trim();
            for (int i = 0; i < s_GallerySidePanelCanonical.Length; i++)
            {
                if (string.Equals(v, s_GallerySidePanelCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_GallerySidePanelCanonical[i];
            }
            return "None";
        }
        public bool DesktopFixedMode = false;
        public bool DesktopFixedAutoCollapse = true;
        public int DesktopFixedHeightMode = 0; // 0: Full, 1: Custom
        public float DesktopCustomHeight = 0.5f;
        public float DesktopCustomWidth = 1.618f / 2.618f;
        public bool EnableAutoFixedGallery = true;
        public float ListRowHeight = 100f;
        public int GridColumnCount = 4;
        /// <summary>0 = Grid, 1 = List. Matches <see cref="GalleryLayoutMode"/>.</summary>
        public int GalleryLayoutMode = 0;
        /// <summary>When true, gallery lists include packages that have an AddonPackagesFilePrefs .hide sidecar.</summary>
        public bool GalleryShowHiddenPackages = false;
        public float SideButtonScale = 1.0f;
        public float InnerPaneScale = 1.0f;
        /// <summary>UI language id: en, zh_cn, etc. Matches vpb_translations/&lt;id&gt;.json. Empty string means auto-detect on first run.</summary>
        public string UiLocale = "";
        /// <summary>Category names hidden from the Categories tab list.</summary>
        public HashSet<string> HiddenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Person", "Person BreastPhysics", "Person General",
            "Person GlutePhysics", "Person Morphs", "Person Textures"
        };

        public bool IsHiddenCategory(string name)
        {
            return HiddenCategories != null && HiddenCategories.Contains(name);
        }
        public bool IsLoadingScene { get; private set; }

        private bool? _isDevMode;
        public bool IsDevMode
        {
            get
            {
                if (!_isDevMode.HasValue)
                {
                    try
                    {
                        string assemblyLocation = typeof(VPBConfig).Assembly.Location;
                        if (!string.IsNullOrEmpty(assemblyLocation))
                        {
                            string devModeFile = Path.Combine(Path.GetDirectoryName(assemblyLocation), ".DevMode");
                            if (File.Exists(devModeFile))
                            {
                                _isDevMode = true;
                                return true;
                            }
                        }
                    }
                    catch
                    {
                    }

                    // Only .DevMode file enables dev mode
                    _isDevMode = false;
                }
                return _isDevMode.Value;
            }
            set
            {
                _isDevMode = value;
            }
        }

        public void StartSceneLoad()
        {
            IsLoadingScene = true;
            TriggerChange();
        }

        public void EndSceneLoad()
        {
            IsLoadingScene = false;
            TriggerChange();
        }

        public delegate void OnConfigChanged();

        /// <summary>
        /// Fired after <see cref="Save(bool,bool)"/> (with notification) and <see cref="TriggerChange"/>.
        /// Handlers must stay lightweight: never rebuild large UI trees here (e.g. repopulating every gallery
        /// category/creator/tag side-tab button). <see cref="GalleryPanel"/> subscribes chrome/layout handlers only, not full tab list rebuilds.
        /// </summary>
        public event OnConfigChanged ConfigChanged;

        /// <summary>Greater than zero while <see cref="ConfigChanged"/> subscribers are being invoked (nested Save/TriggerChange included).</summary>
        internal static int ConfigChangedInvocationDepth { get; private set; }

        public void Load()
        {
            string cfgPath = ConfigPath;
            bool cfgExistedAtStart = File.Exists(cfgPath);
            Stopwatch loadSw = Stopwatch.StartNew();
            _lightweightGalleryTabRefreshSlotsRemaining = 0;
            LogUtil.Log("[VPBConfig.Load] Starting Load() from: " + cfgPath);
            // Reset to defaults before loading
            EnableButtonGaps = true;
            ShowSideButtons = "Both";
            _followAngle = "Both";
            _followDistance = "VR";
            _followEyeHeight = "VR";
            BringToFrontDistance = 1.5f;
            ReorientStartAngle = 20f;
            MovementThreshold = 0.1f;
            EnableGalleryFade = true;
            EnableGalleryTranslucency = false;
            GalleryManualRefreshOnly = true;
            GalleryOpacity = 1.0f;
            DragDropReplaceMode = false;
            AppearanceClothingApplyMode = "replace";
            EnableDragDrop = false;
            RequireDragHoldBeforeMove = false;
            DragHoldThreshold = 0.5f;
            ApplyMode = "DoubleClick";
            LastGalleryCategory = "";
            InitialGalleryCategory = "Scenes";
            DesktopFixedMode = false;
            DesktopFixedAutoCollapse = true;
            DesktopFixedHeightMode = 0;
            DesktopCustomHeight = 0.5f;
            DesktopCustomWidth = 1.618f / 2.618f;
            EnableAutoFixedGallery = true;
            ListRowHeight = 100f;
            GridColumnCount = 4;
            GalleryLayoutMode = 0;
            GalleryShowHiddenPackages = false;
            GalleryListNamesLegacyFileName = false;
            GalleryDefaultLeftSidePanel = "None";
            GalleryDefaultRightSidePanel = "None";
            GalleryTboxToolbarPinned = false;
            UiLocale = "";
            SpringScrollButtonEnabled = true;
            HoldToLaunchEnabled = false;
            HoldToLaunchPrevEnableDragDrop = false;

            try
            {
                if (File.Exists(ConfigPath))
                {
                    // Capture the value from the *previous* load (before defaults were reset above)
                    // so the log below can detect when the category actually changes between loads.
                    string prevLastGalleryCategory = s_LastLoggedLoadedGalleryCategory;
                    string json = File.ReadAllText(ConfigPath);
                    JSONNode node = JSON.Parse(json);
                    if (node != null)
                    {
                        if (node["EnableButtonGaps"] != null) EnableButtonGaps = node["EnableButtonGaps"].AsBool;
                        if (node["ShowSideButtons"] != null) ShowSideButtons = node["ShowSideButtons"].Value;
                        
                        // Handle legacy bools if they exist, or just use string
                        if (node["FollowAngle"] != null) {
                            string val = node["FollowAngle"].Value;
                            if (val == "true" || val == "True") 
                                _followAngle = "Both";
                            else if (val == "false" || val == "False") 
                                _followAngle = "Off";
                            else 
                                _followAngle = val;
                        }

                        if (node["FollowDistance"] != null) {
                            string val = node["FollowDistance"].Value;
                            if (val == "true" || val == "True") 
                                _followDistance = "Both";
                            else if (val == "false" || val == "False") 
                                _followDistance = "Off";
                            else 
                                _followDistance = val;
                        }

                        if (node["FollowEyeHeight"] != null) {
                            string val = node["FollowEyeHeight"].Value;
                            if (val == "true" || val == "True") 
                                _followEyeHeight = "Both";
                            else if (val == "false" || val == "False") 
                                _followEyeHeight = "Off";
                            else 
                                _followEyeHeight = val;
                        }
                        
                        if (node["BringToFrontDistance"] != null) BringToFrontDistance = node["BringToFrontDistance"].AsFloat;
                        if (node["ReorientStartAngle"] != null) ReorientStartAngle = node["ReorientStartAngle"].AsFloat;
                        if (node["MovementThreshold"] != null) MovementThreshold = node["MovementThreshold"].AsFloat;
                        if (node["EnableGalleryFade"] != null) EnableGalleryFade = node["EnableGalleryFade"].AsBool;
                        if (node["EnableGalleryTranslucency"] != null) EnableGalleryTranslucency = node["EnableGalleryTranslucency"].AsBool;
                        if (node["GalleryManualRefreshOnly"] != null) GalleryManualRefreshOnly = node["GalleryManualRefreshOnly"].AsBool;
                        if (node["GalleryOpacity"] != null) GalleryOpacity = node["GalleryOpacity"].AsFloat;
                        if (node["DragDropReplaceMode"] != null) DragDropReplaceMode = node["DragDropReplaceMode"].AsBool;
                        if (node["AppearanceClothingApplyMode"] != null)
                            AppearanceClothingApplyMode = node["AppearanceClothingApplyMode"].Value;
                        else if (node["KeepClothingWhenApplyingAppearance"] != null)
                            AppearanceClothingApplyMode = node["KeepClothingWhenApplyingAppearance"].AsBool ? "keep" : "replace";
                        if (node["EnableDragDrop"] != null) EnableDragDrop = node["EnableDragDrop"].AsBool;
                        if (node["DragHoldThreshold"] != null)
                            DragHoldThreshold = Mathf.Clamp(node["DragHoldThreshold"].AsFloat, 0f, 3f);
                        if (node["RequireDragHoldBeforeMove"] != null)
                            RequireDragHoldBeforeMove = node["RequireDragHoldBeforeMove"].AsBool;
                        if (node["ApplyMode"] != null) ApplyMode = node["ApplyMode"].Value;
                        if (node["LastGalleryCategory"] != null) LastGalleryCategory = node["LastGalleryCategory"].Value;
                        if (node["InitialGalleryCategory"] != null)
                            InitialGalleryCategory = NormalizeInitialGalleryCategory(node["InitialGalleryCategory"].Value);
                        if (node["GalleryDefaultLeftSidePanel"] != null)
                            GalleryDefaultLeftSidePanel = NormalizeGallerySidePanel(node["GalleryDefaultLeftSidePanel"].Value);
                        if (node["GalleryDefaultRightSidePanel"] != null)
                            GalleryDefaultRightSidePanel = NormalizeGallerySidePanel(node["GalleryDefaultRightSidePanel"].Value);
                        if (node["DesktopFixedMode"] != null) DesktopFixedMode = node["DesktopFixedMode"].AsBool;
                        if (node["DesktopFixedAutoCollapse"] != null) DesktopFixedAutoCollapse = node["DesktopFixedAutoCollapse"].AsBool;
                        if (node["DesktopFixedHeightMode"] != null) DesktopFixedHeightMode = node["DesktopFixedHeightMode"].AsInt;
                        if (node["DesktopCustomHeight"] != null) DesktopCustomHeight = node["DesktopCustomHeight"].AsFloat;
                        if (node["DesktopCustomWidth"] != null) DesktopCustomWidth = node["DesktopCustomWidth"].AsFloat;
                        if (node["EnableAutoFixedGallery"] != null) EnableAutoFixedGallery = node["EnableAutoFixedGallery"].AsBool;
                        if (node["ListRowHeight"] != null) ListRowHeight = node["ListRowHeight"].AsFloat;
                        if (node["GridColumnCount"] != null) GridColumnCount = node["GridColumnCount"].AsInt;
                        if (node["GalleryLayoutMode"] != null) GalleryLayoutMode = node["GalleryLayoutMode"].AsInt;
                        if (node["GalleryShowHiddenPackages"] != null) GalleryShowHiddenPackages = node["GalleryShowHiddenPackages"].AsBool;
                        if (node["PluginGalleryGridThumbnails"] != null) PluginGalleryGridThumbnails = node["PluginGalleryGridThumbnails"].AsBool;
                        if (node["GalleryListNamesLegacyFileName"] != null) GalleryListNamesLegacyFileName = node["GalleryListNamesLegacyFileName"].AsBool;
                        if (node["GalleryHoverPreviewMode"] != null)
                            GalleryHoverPreviewMode = NormalizeHoverPreviewMode(node["GalleryHoverPreviewMode"].Value);
                        else if (node["GalleryListHoverPreviewEnabled"] != null)
                            GalleryHoverPreviewMode = node["GalleryListHoverPreviewEnabled"].AsBool ? "List" : "Off";
                        if (node["GalleryListHoverPreviewSize"] != null) GalleryListHoverPreviewSize = Mathf.Clamp(node["GalleryListHoverPreviewSize"].AsFloat, 200f, 600f);
                        if (node["GalleryListHoverPreviewOffsetX"] != null) GalleryListHoverPreviewOffsetX = Mathf.Clamp(node["GalleryListHoverPreviewOffsetX"].AsFloat, -2000f, 2000f);
                        if (node["GalleryListHoverPreviewOffsetY"] != null) GalleryListHoverPreviewOffsetY = Mathf.Clamp(node["GalleryListHoverPreviewOffsetY"].AsFloat, -2000f, 2000f);
                        if (node["GalleryTboxToolbarPinned"] != null) GalleryTboxToolbarPinned = node["GalleryTboxToolbarPinned"].AsBool;
                        if (node["GalleryOnlyWhenVamMenuVisible"] != null) GalleryOnlyWhenVamMenuVisible = node["GalleryOnlyWhenVamMenuVisible"].AsBool;
                        if (node["SideButtonScale"] != null) SideButtonScale = node["SideButtonScale"].AsFloat;
                        if (node["InnerPaneScale"] != null) InnerPaneScale = node["InnerPaneScale"].AsFloat;
                        if (node["SpringScrollButtonEnabled"] != null) SpringScrollButtonEnabled = node["SpringScrollButtonEnabled"].AsBool;
                        if (node["HoldToLaunchEnabled"] != null) HoldToLaunchEnabled = node["HoldToLaunchEnabled"].AsBool;
                        if (node["HoldToLaunchPrevEnableDragDrop"] != null) HoldToLaunchPrevEnableDragDrop = node["HoldToLaunchPrevEnableDragDrop"].AsBool;
                        if (node["UiLocale"] != null) UiLocale = node["UiLocale"].Value;
                        if (node["HiddenCategories"] != null)
                        {
                            HiddenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var part in node["HiddenCategories"].Value.Split(','))
                            {
                                string t = part.Trim();
                                if (!string.IsNullOrEmpty(t)) HiddenCategories.Add(t);
                            }
                        }
                    }

                    try
                    {
                        if (Settings.Instance != null && Settings.Instance.LogVerboseUi != null && Settings.Instance.LogVerboseUi.Value)
                            LogUtil.Log("[VPBConfig] Loaded cfg path=" + ConfigPath + " | LastGalleryCategory=" + LastGalleryCategory + " | DragDropReplaceMode=" + DragDropReplaceMode + " | AppearanceClothing=" + AppearanceClothingApplyMode + " | ApplyMode=" + ApplyMode);
                    }
                    catch { }

                    try
                    {
                        // Log only when the loaded category differs from what was logged on the previous load.
                        // prevLastGalleryCategory was captured from s_LastLoggedLoadedGalleryCategory above,
                        // so a single comparison is sufficient.
                        if (!string.Equals(prevLastGalleryCategory, LastGalleryCategory, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(LastGalleryCategory))
                        {
                            s_LastLoggedLoadedGalleryCategory = LastGalleryCategory;
                            LogUtil.Log("[VPBConfig] Loaded LastGalleryCategory='" + LastGalleryCategory + "' from " + ConfigPath);
                        }
                    }
                    catch { }
                }
                else
                {
                    LogUtil.LogWarning("[VPBConfig.Load] Config file DOES NOT EXIST at: " + ConfigPath);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VPB] Error loading config: " + ex.Message);
            }
            finally
            {
                try
                {
                    LogPerfLoad(cfgPath, loadSw.ElapsedMilliseconds, cfgExistedAtStart);
                }
                catch { }
            }
        }

        public void Save()
        {
            Save(true, false);
        }

        /// <param name="notifyListeners">When false, skips <see cref="ConfigChanged"/> (avoids full gallery layout). Use after settings UI already applied live updates.</param>
        public void Save(bool notifyListeners)
        {
            Save(notifyListeners, false);
        }

        /// <summary>
        /// Persists VPB.cfg and optionally notifies <see cref="ConfigChanged"/>.
        /// </summary>
        /// <param name="notifyListeners">When false, skips <see cref="ConfigChanged"/>.</param>
        /// <param name="preferLightGalleryTabChromeOnly">
        /// When true with <paramref name="notifyListeners"/>, gallery <see cref="GalleryPanel.UpdateTabs"/> skips rebuilding side-tab button lists
        /// (only title/footer/side chrome). Use only when the persisted change cannot alter category/creator/tag tab contents or counts.
        /// </param>
        public void Save(bool notifyListeners, bool preferLightGalleryTabChromeOnly)
        {
            if (!notifyListeners)
                _lightweightGalleryTabRefreshSlotsRemaining = 0;
            else if (preferLightGalleryTabChromeOnly)
                ArmLightweightGalleryTabRefreshInternal();
            else
                _lightweightGalleryTabRefreshSlotsRemaining = 0;

            bool lightTabsHint = notifyListeners && preferLightGalleryTabChromeOnly;

            try
            {
                string path = ConfigPath;
                string prevLogged = s_LastLoggedSavedGalleryCategory;
                Stopwatch sw = Stopwatch.StartNew();
                JSONClass node = new JSONClass();
                node["EnableButtonGaps"].AsBool = EnableButtonGaps;
                node["ShowSideButtons"] = ShowSideButtons;
                node["FollowAngle"] = _followAngle;
                node["FollowDistance"] = _followDistance;
                node["FollowEyeHeight"] = _followEyeHeight;
                node["BringToFrontDistance"].AsFloat = BringToFrontDistance;
                node["ReorientStartAngle"].AsFloat = ReorientStartAngle;
                node["MovementThreshold"].AsFloat = MovementThreshold;
                node["EnableGalleryFade"].AsBool = EnableGalleryFade;
                node["EnableGalleryTranslucency"].AsBool = EnableGalleryTranslucency;
                node["GalleryManualRefreshOnly"].AsBool = GalleryManualRefreshOnly;
                node["GalleryOpacity"].AsFloat = GalleryOpacity;
                node["DragDropReplaceMode"].AsBool = DragDropReplaceMode;
                node["AppearanceClothingApplyMode"] = AppearanceClothingApplyMode;
                node["KeepClothingWhenApplyingAppearance"].AsBool = KeepClothingWhenApplyingAppearance;
                node["EnableDragDrop"].AsBool = EnableDragDrop;
                node["RequireDragHoldBeforeMove"].AsBool = RequireDragHoldBeforeMove;
                node["DragHoldThreshold"].AsFloat = DragHoldThreshold;
                node["ApplyMode"] = ApplyMode;
                node["LastGalleryCategory"] = LastGalleryCategory;
                node["InitialGalleryCategory"] = InitialGalleryCategory;
                node["GalleryDefaultLeftSidePanel"] = GalleryDefaultLeftSidePanel;
                node["GalleryDefaultRightSidePanel"] = GalleryDefaultRightSidePanel;
                node["DesktopFixedMode"].AsBool = DesktopFixedMode;
                node["DesktopFixedAutoCollapse"].AsBool = DesktopFixedAutoCollapse;
                node["DesktopFixedHeightMode"].AsInt = DesktopFixedHeightMode;
                node["DesktopCustomHeight"].AsFloat = DesktopCustomHeight;
                node["DesktopCustomWidth"].AsFloat = DesktopCustomWidth;
                node["EnableAutoFixedGallery"].AsBool = EnableAutoFixedGallery;
                node["ListRowHeight"].AsFloat = ListRowHeight;
                node["GridColumnCount"].AsInt = GridColumnCount;
                node["GalleryLayoutMode"].AsInt = GalleryLayoutMode;
                node["GalleryShowHiddenPackages"].AsBool = GalleryShowHiddenPackages;
                node["PluginGalleryGridThumbnails"].AsBool = PluginGalleryGridThumbnails;
                node["GalleryListNamesLegacyFileName"].AsBool = GalleryListNamesLegacyFileName;
                node["GalleryHoverPreviewMode"] = NormalizeHoverPreviewMode(GalleryHoverPreviewMode);
                node["GalleryListHoverPreviewSize"].AsFloat = GalleryListHoverPreviewSize;
                node["GalleryListHoverPreviewOffsetX"].AsFloat = GalleryListHoverPreviewOffsetX;
                node["GalleryListHoverPreviewOffsetY"].AsFloat = GalleryListHoverPreviewOffsetY;
                node["GalleryTboxToolbarPinned"].AsBool = GalleryTboxToolbarPinned;
                node["GalleryOnlyWhenVamMenuVisible"].AsBool = GalleryOnlyWhenVamMenuVisible;
                node["SideButtonScale"].AsFloat = SideButtonScale;
                node["InnerPaneScale"].AsFloat = InnerPaneScale;
                node["SpringScrollButtonEnabled"].AsBool = SpringScrollButtonEnabled;
                node["HoldToLaunchEnabled"].AsBool = HoldToLaunchEnabled;
                node["HoldToLaunchPrevEnableDragDrop"].AsBool = HoldToLaunchPrevEnableDragDrop;
                node["UiLocale"] = UiLocale ?? "en";
                node["HiddenCategories"] = string.Join(",", new List<string>(HiddenCategories ?? new HashSet<string>()).ToArray());
                long msBuild = sw.ElapsedMilliseconds;
                string jsonOutput = node.ToString();
                long msAfterToString = sw.ElapsedMilliseconds;
                File.WriteAllText(path, jsonOutput);
                long msAfterDisk = sw.ElapsedMilliseconds;
                long notifyMs = 0;
                if (notifyListeners)
                {
                    try
                    {
                        notifyMs = InvokeConfigChangedWithPerfLogging("Save");
                    }
                    finally
                    {
                        _lightweightGalleryTabRefreshSlotsRemaining = 0;
                    }
                }
                long msTotal = sw.ElapsedMilliseconds;
                long toStringMs = msAfterToString - msBuild;
                long diskMs = msAfterDisk - msAfterToString;
                try
                {
                    LogPerfSave(path, notifyListeners, lightTabsHint, msBuild, toStringMs, diskMs, notifyMs, msTotal);
                }
                catch { }

                try
                {
                    if (Settings.Instance != null && Settings.Instance.LogVerboseUi != null && Settings.Instance.LogVerboseUi.Value)
                        LogUtil.Log("[VPBConfig] Saved cfg path=" + path + " | LastGalleryCategory=" + LastGalleryCategory + " | DragDropReplaceMode=" + DragDropReplaceMode + " | AppearanceClothing=" + AppearanceClothingApplyMode + " | ApplyMode=" + ApplyMode);
                }
                catch { }

                try
                {
                    if (!string.Equals(prevLogged, LastGalleryCategory, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(LastGalleryCategory))
                    {
                        s_LastLoggedSavedGalleryCategory = LastGalleryCategory;
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                _lightweightGalleryTabRefreshSlotsRemaining = 0;
                UnityEngine.Debug.LogError("[VPB] Error saving config: " + ex.Message);
            }
        }

        public void TriggerChange()
        {
            _lightweightGalleryTabRefreshSlotsRemaining = 0;
            long notifyMs = 0;
            try
            {
                notifyMs = InvokeConfigChangedWithPerfLogging("TriggerChange");
            }
            finally
            {
                _lightweightGalleryTabRefreshSlotsRemaining = 0;
            }
            try
            {
                LogPerfTriggerChange(notifyMs);
            }
            catch { }
        }

        private void ArmLightweightGalleryTabRefreshInternal()
        {
            try
            {
                int n = (Gallery.singleton != null && Gallery.singleton.PanelCount > 0)
                    ? Gallery.singleton.PanelCount
                    : 1;
                _lightweightGalleryTabRefreshSlotsRemaining = n;
            }
            catch
            {
                _lightweightGalleryTabRefreshSlotsRemaining = 1;
            }
        }

        internal bool TryConsumeLightweightGalleryTabRefreshSlot()
        {
            if (_lightweightGalleryTabRefreshSlotsRemaining <= 0)
                return false;
            _lightweightGalleryTabRefreshSlotsRemaining--;
            return true;
        }

        public bool IsFollowEnabled(string setting)
        {
            if (IsLoadingScene) return true;
            if (setting == "Off") return false;
            if (setting == "Both") return true;

            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }

            if (setting == "VR") return isVR;
            if (setting == "Desktop") return !isVR;

            return false;
        }
    }
}
