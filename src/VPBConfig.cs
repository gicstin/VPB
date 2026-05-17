using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using VPB.src.util;

namespace VPB
{
    public class VPBConfig
    {
        public enum GlobalSourceFilterValue
        {
            All,
            Local,
            Var
        }

        public const float MinUiScale = 0.72f;
        public const float MaxUiScale = 1.5f;

        private static float ClampUiScale(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) return 1f;
            if (v < MinUiScale) return MinUiScale;
            if (v > MaxUiScale) return MaxUiScale;
            return v;
        }
        private static VPBConfig _instance;
        private static string s_LastLoggedSavedGalleryCategory;
        private static string s_LastLoggedLoadedGalleryCategory;

        /// <summary>
        /// When &gt; 0, the next <see cref="GalleryPanel.UpdateTabs"/> on each gallery pane may skip rebuilding category/creator/tag side-tab buttons.
        /// Reset by <see cref="Save(bool,bool)"/> / <see cref="TriggerChange"/> so stale values cannot leak across failed saves or mis-ordered calls.
        /// </summary>
        private int _lightweightGalleryTabRefreshSlotsRemaining;


        /// <summary>Runs <see cref="ConfigChanged"/> subscribers one-by-one (same order as +=).</summary>
        private void InvokeConfigChanged()
        {
            if (ConfigChanged != null)
                ConfigChanged();
        }

        private static void LogPerfTriggerChange(long notifyMs)
        {
            if (notifyMs < 50)
                return;
            string msg = "[VPBConfig.Perf] TriggerChange ConfigChanged=" + notifyMs + "ms (no disk write)";
            LogUtil.LogWarning(msg);
        }

        private static void LogPerfLoad(string pathForLog, long totalMs, bool fileExisted)
        {
            if (!fileExisted)
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
                foreach (Delegate d in list)
                {
                    try
                    {
                        ((OnConfigChanged)d).Invoke();
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError("[VPB] ConfigChanged handler threw | " + ex.Message);
                    }
                }
                return 0;
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
        /// <summary>When true, all transparency sub-options are overridden (assignable slots, dock strips, gallery pane).</summary>
        public bool DisableGalleryTransparency = true;
        /// <summary>When true (or <see cref="DisableGalleryTransparency"/>), gallery pane idle translucency is off (fully opaque).</summary>
        public bool DisableGalleryPaneTransparency = true;
        /// <summary>When true (or <see cref="DisableGalleryTransparency"/>), quick-menu assignable slot backdrops are fully opaque.</summary>
        public bool DisableGalleryAssignableButtonsTransparency = true;
        /// <summary>When true (or <see cref="DisableGalleryTransparency"/>), dock collapse strips are fully opaque when collapsed.</summary>
        public bool DisableGalleryDockHoverTransparency = true;
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
        /// <summary>True keeps target atom's current scale when an Appearance preset is applied (both toolbox and drag-drop). Default false.</summary>
        public bool SuppressAppearanceScaleChange { get; set; } = false;
        /// <summary>When true, suppresses CheesyFX NullReferenceException spam in Unity/BepInEx logs (broken Update loops).</summary>
        public bool SuppressCheesyFxNullReferenceLogs = true;
        /// <summary>Gallery item drag-and-drop to atoms/scene. Off by default (VR jitter / accidental drags); enable in Settings → Interaction.</summary>
        public bool EnableDragDrop = false;
        /// <summary>When true (default), Clothing/Hair categories auto-apply Male/Female subfilter based on selected target atom gender.</summary>
        public bool GalleryAutoGenderFilter = true;
        /// <summary>When true (default), visible gallery panes collapse (fixed dock) or hide (floating) when a scene is launched.</summary>
        public bool GalleryCollapseOnSceneLaunch = true;
        /// <summary>
        /// Effective drag-and-drop state at runtime.
        /// Hold-to-launch uses same pointer-down gesture as drag start, so it suppresses drag even if user preference is enabled.
        /// </summary>
        public bool EffectiveEnableDragDrop
        {
            get { return EnableDragDrop && !HoldToLaunchEnabled; }
        }
        /// <summary>Legacy persisted flag; ignored for behavior when <see cref="EnableDragDrop"/> is on — hold is always required then. Serialized for forward compatibility.</summary>
        public bool RequireDragHoldBeforeMove = false;
        /// <summary>Minimum seconds before gallery item drag can start when drag-and-drop is on; loaded/saved values are clamped to this floor.</summary>
        public const float DragHoldThresholdMin = 0.4f;
        public float DragHoldThreshold = 0.5f;

        /// <summary>Clamps persisted UI value for drag hold duration to <see cref="DragHoldThresholdMin"/> … 1.</summary>
        public static float ClampDragHoldThreshold(float seconds) =>
            Mathf.Clamp(seconds, DragHoldThresholdMin, 1f);

        /// <summary>Ensures hold-before-drag when drag-and-drop is enabled and threshold meets minimum.</summary>
        public void NormalizeDragDropHoldSettings()
        {
            DragHoldThreshold = ClampDragHoldThreshold(DragHoldThreshold);
            if (EnableDragDrop)
                RequireDragHoldBeforeMove = true;
        }
        public string ApplyMode = "DoubleClick";
        public string LastGalleryCategory = "";
        /// <summary>Category when opening a new gallery pane or at session first open: "Scenes" (default), "Clothing", "Hair", "Pose", "Appearance", "Plugins", or "LastUsed".</summary>
        public string InitialGalleryCategory = "Scenes";
        /// <summary>Global source filter for gallery: All (default), Local (loose files only), or Var (.var packages only).</summary>
        public GlobalSourceFilterValue GlobalSourceFilter = GlobalSourceFilterValue.All;

        private static readonly string[] s_InitialGalleryCategoryCanonical = { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" };

        /// <summary>When false, plugin rows (.cs/.cslist/.dll under Custom/Scripts) show no thumbnail in the grid/list; selection info box can still show a sister .jpg/.png preview.</summary>
        public bool PluginGalleryGridThumbnails = true;
        /// <summary>When true, gallery list layout uses each item's file name (legacy). When false (default), .var rows show Creator.Package.Version (package uid, no .var suffix).</summary>
        public bool GalleryListNamesLegacyFileName = false;
        /// <summary>When true (default), gallery labels strip "Preset_"/"Plugins_" prefixes and the file extension so presets appear by their human name; the original path moves into the hover tooltip. Mirrors BA's resourceDisplayName behavior.</summary>
        public bool GalleryPrettyPresetNames = true;
        /// <summary>What the gallery search box matches against. See <see cref="NormalizeGallerySearchScope"/> for canonical values; default "PathAndName" preserves prior behavior.</summary>
        public string GallerySearchScope = "PathAndName";
        /// <summary>Which layout(s) show the hover preview. Off, List, Grid, or Both. Default: List.</summary>
        public string GalleryHoverPreviewMode = "List";
        /// <summary>Square preview size (pixels) for List layout hover preview.</summary>
        public float GalleryListHoverPreviewSize = 300f;
        /// <summary>X offset (pixels) from the default bottom-left dock point for the List layout hover preview.</summary>
        public float GalleryListHoverPreviewOffsetX = 0f;
        /// <summary>Y offset (pixels) from the default bottom-left dock point for the List layout hover preview.</summary>
        public float GalleryListHoverPreviewOffsetY = 0f;
        /// <summary>When true, each grid cell shows a persistent label strip below the thumbnail with Creator.Package.Version. Grid mode only.</summary>
        public bool GalleryGridLabelsEnabled = true;
        /// <summary>Font size (pixels) for the always-on grid label strip.</summary>
        public float GalleryGridLabelFontSize = 18f;
        /// <summary>When true with always-on labels, hide label strip at 11–12 columns (highest grid density).</summary>
        public bool GalleryGridLabelsAutoHideAtHighDensity = false;
        /// <summary>Grid: horizontal spacing between thumbnail cells (pixels).</summary>
        public float GalleryGridSpacingX = 0f;
        /// <summary>Grid: vertical spacing between thumbnail cells (pixels).</summary>
        public float GalleryGridSpacingY = 0f;
        /// <summary>Grid: padding between cell background and thumbnail (pixels). 0 = thumbnail flush to edge.</summary>
        public float GalleryGridThumbnailPadding = 0f;
        /// <summary>Grid: hover border width (pixels). Implemented via Outline effectDistance.</summary>
        public float GalleryGridHoverBorderWidth = 1f;
        /// <summary>Grid: selected border width (pixels).</summary>
        public float GalleryGridSelectedBorderWidth = 2f;
        /// <summary>When true and <see cref="GalleryGridThumbnailPadding"/> is 0, render hover/selection border inward.</summary>
        public bool GalleryGridBorderInwardWhenSquare = true;
        /// <summary>RGBA 0–1 for grid/list hover and selection border tint (default yellow).</summary>
        public float GalleryGridBorderColorR = 1f;
        public float GalleryGridBorderColorG = 1f;
        public float GalleryGridBorderColorB = 0f;
        public float GalleryGridBorderColorA = 1f;

        public Color GetGalleryGridBorderColor()
        {
            return new Color(
                Mathf.Clamp01(GalleryGridBorderColorR),
                Mathf.Clamp01(GalleryGridBorderColorG),
                Mathf.Clamp01(GalleryGridBorderColorB),
                Mathf.Clamp01(GalleryGridBorderColorA));
        }

        public void SetGalleryGridBorderColor(Color c)
        {
            GalleryGridBorderColorR = c.r;
            GalleryGridBorderColorG = c.g;
            GalleryGridBorderColorB = c.b;
            GalleryGridBorderColorA = c.a;
            try { TriggerChange(); } catch { }
        }
        /// <summary>When true, the gallery selection toolbar (tbox) pin stays on across sessions until turned off manually.</summary>
        public bool GalleryTboxToolbarPinned = false;
        /// <summary>When true, gallery pane only shows while the VaM menu (main HUD) is visible.</summary>
        public bool GalleryOnlyWhenVamMenuVisible = false;
        /// <summary>When true, gallery pane is anchored to the VAM menu system in VR mode.</summary>
        public bool GalleryAnchorToVamMenu = true;
        /// <summary>Offset for anchoring gallery pane relative to the VAM menu system.</summary>
        public Vector3 GalleryAnchorOffset = new Vector3(0f, 0.1f, -0.1f);

        // Interaction toggles (persisted)
        public bool SpringScrollButtonEnabled = true;
        public bool HoldToLaunchEnabled = false;
        /// <summary>When HoldToLaunch is enabled, drag&drop is forced off; this stores the prior setting for restore.</summary>
        public bool HoldToLaunchPrevEnableDragDrop = false;
        /// <summary>Seconds pointer must stay pressed on item before hold-to-launch fires (when HoldToLaunch is on).</summary>
        public float HoldToLaunchHoldSeconds = 1f;

        // Quick Menu assignable buttons (persistent, forward-compatible via string IDs)
        public int QuickMenuButtonsVersion = 1;
        public int QuickMenuButtonsCurrentPage = 0; // 0-based
        public string[][] QuickMenuButtonsPages = null; // [page][slot] => actionId (""/null = none)
        public int QuickMenuEditSlotIdx = 12; // settings/edit toggle slot (0-based)
        public int QuickMenuPageToggleSlotIdx = 15; // page toggle slot (0-based)

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

        private static readonly string[] s_GallerySearchScopeCanonical = { "PathAndName", "NameOnly", "NameStartsWith" };
        /// <summary>
        /// Canonical: "PathAndName" (default; multi-term AND against either e.Path or pretty name),
        /// "NameOnly" (terms must all appear in pretty name), "NameStartsWith" (each term must be a prefix of the pretty name).
        /// Pretty name = <see cref="GalleryPanel.GetPrettyEntryDisplayName"/>; couples display and search so users can type what they see.
        /// </summary>
        public static string NormalizeGallerySearchScope(string value)
        {
            if (string.IsNullOrEmpty(value)) return "PathAndName";
            string v = value.Trim();
            for (int i = 0; i < s_GallerySearchScopeCanonical.Length; i++)
            {
                if (string.Equals(v, s_GallerySearchScopeCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_GallerySearchScopeCanonical[i];
            }
            return "PathAndName";
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

        /// <summary>Which list opens on the left when a gallery pane is created: None, Category, Creator, or Tags.</summary>
        public string GalleryDefaultLeftSidePanel = "None";
        /// <summary>Which list opens on the right when a gallery pane is created: None, Category, Creator, or Tags.</summary>
        public string GalleryDefaultRightSidePanel = "None";
        /// <summary>Big scroll button step in viewport heights.</summary>
        public float GalleryScrollButtonStepViewportFraction = 0.65f;
        /// <summary>When true, show big VR up/down scroll buttons on gallery and tag lists.</summary>
        public bool GalleryScrollButtonsEnabled = true;
        /// <summary>When true, VR thumbstick forward/back scrolls the gallery while the pointer is over a pane (blocks free-move on that axis).</summary>
        public bool GalleryVrThumbstickScrollEnabled = true;
        /// <summary>When true, gallery hides side-rail Creator buttons; creator filtering uses title-bar control only. Side creator panes stay closed.</summary>
        public bool GalleryHideCreatorSideButtons = false;
        /// <summary>When true, BA migration prompt has been dismissed and will not appear again.</summary>
        public bool BaMigrationPromptDismissed = false;

        private static readonly string[] s_GallerySidePanelCanonical = { "None", "Category", "Creator", "Tags" };

        /// <summary>Maps user/config values to None, Category, Creator, or Tags.</summary>
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
        /// <summary>Seconds pointer must be outside fixed pane before auto-collapse (when DesktopFixedAutoCollapse is on).</summary>
        public float DesktopFixedAutoHideSeconds = 1.0f;
        /// <summary>Desktop fixed gallery dock edge: Right (default), Left, or Top.</summary>
        public string DesktopFixedDockSide = "Right";
        /// <summary>Default dock side when entering fixed mode.</summary>
        public string DesktopFixedDefaultDockSide = "Right";
        /// <summary>When true, fixed-mode docking always uses <see cref="DesktopFixedEnforcedDockSide"/> regardless of which dock button was clicked.</summary>
        public bool DesktopFixedEnforceDockSide = false;
        /// <summary>Dock side used when <see cref="DesktopFixedEnforceDockSide"/> is true.</summary>
        public string DesktopFixedEnforcedDockSide = "Right";
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
        public float SideButtonScaleVR = 1.0f;
        public float SideButtonScaleDesktop = 0.8f;
        private float _innerPaneScaleVR = 1.0f;
        private float _innerPaneScaleDesktop = 0.8f;
        public float InnerPaneScaleVR
        {
            get { return ClampUiScale(_innerPaneScaleVR); }
            set { _innerPaneScaleVR = ClampUiScale(value); }
        }
        public float InnerPaneScaleDesktop
        {
            get { return ClampUiScale(_innerPaneScaleDesktop); }
            set { _innerPaneScaleDesktop = ClampUiScale(value); }
        }
        public float CurrentSideButtonScale => ClampUiScale(IsVR ? SideButtonScaleVR : SideButtonScaleDesktop);
        public float CurrentInnerPaneScale => IsVR ? InnerPaneScaleVR : InnerPaneScaleDesktop;
        public float InnerPaneScale
        {
            get => CurrentInnerPaneScale;
            set
            {
                if (IsVR)
                    InnerPaneScaleVR = ClampUiScale(value);
                else
                    InnerPaneScaleDesktop = ClampUiScale(value);
            }
        }

        public bool IsVR
        {
            get
            {
                return XrUtils.IsVrActive();
            }
        }

        public float UiScale
        {
            get
            {
                try
                {
                    if (Settings.Instance != null && Settings.Instance.UIScale != null)
                        return Settings.Instance.UIScale.Value;
                }
                catch { }
                return 1f;
            }
        }

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

        /// <summary>Comma, semicolon, or newline-separated category names: order for quick header menu and number keys 1–9, 0. Empty = built-in default (ALL VAR, Scenes, Appearance, …).</summary>
        public string GalleryCategoryQuickOrder = "";

        /// <summary>Comma-separated category names excluded from quick header menu and number keys only (side category list unchanged).</summary>
        public string GalleryCategoryQuickSwitchHidden = "";

        /// <summary>Record separator–delimited gallery user tag names pinned to top of User Tags side lists (order preserved).</summary>
        public string GalleryUserTagPinnedOrder = "";

        /// <summary>True when always-on grid label strip should render (respects auto-hide at highest two column counts).</summary>
        public bool GalleryGridLabelsStripVisible()
        {
            if (!GalleryGridLabelsEnabled) return false;
            if (GalleryGridLabelsAutoHideAtHighDensity && GridColumnCount >= 11) return false;
            return true;
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
            VPBLogger.Config.LogInfo("Starting Load() from: " + cfgPath);
            // Reset to defaults before loading
            EnableButtonGaps = true;
            ShowSideButtons = "Both";
            _followAngle = "Both";
            _followDistance = "VR";
            _followEyeHeight = "VR";
            BringToFrontDistance = 1.5f;
            ReorientStartAngle = 20f;
            MovementThreshold = 0.1f;
            DisableGalleryTransparency = true;
            DisableGalleryPaneTransparency = true;
            DisableGalleryAssignableButtonsTransparency = true;
            DisableGalleryDockHoverTransparency = true;
            EnableGalleryFade = true;
            EnableGalleryTranslucency = false;
            GalleryManualRefreshOnly = true;
            GalleryOpacity = 1.0f;
            DragDropReplaceMode = false;
            AppearanceClothingApplyMode = "replace";
            SuppressAppearanceScaleChange = false;
            SuppressCheesyFxNullReferenceLogs = true;
            EnableDragDrop = false;
            GalleryAutoGenderFilter = true;
            GalleryCollapseOnSceneLaunch = true;
            RequireDragHoldBeforeMove = false;
            DragHoldThreshold = 0.5f;
            ApplyMode = "DoubleClick";
            LastGalleryCategory = "";
            InitialGalleryCategory = "Scenes";
            DesktopFixedMode = false;
            DesktopFixedAutoCollapse = true;
            DesktopFixedAutoHideSeconds = 1.0f;
            DesktopFixedDockSide = "Right";
            DesktopFixedDefaultDockSide = "Right";
            DesktopFixedEnforceDockSide = false;
            DesktopFixedEnforcedDockSide = "Right";
            DesktopFixedHeightMode = 0;
            DesktopCustomHeight = 0.5f;
            DesktopCustomWidth = 1.618f / 2.618f;
            EnableAutoFixedGallery = true;
            ListRowHeight = 100f;
            GridColumnCount = 4;
            GalleryLayoutMode = 0;
            GalleryShowHiddenPackages = false;
            GalleryListNamesLegacyFileName = false;
            GalleryPrettyPresetNames = true;
            GallerySearchScope = "PathAndName";
            GalleryDefaultLeftSidePanel = "None";
            GalleryDefaultRightSidePanel = "None";
            GalleryHideCreatorSideButtons = false;
            GalleryGridLabelsEnabled = true;
            GalleryGridLabelFontSize = 18f;
            GalleryGridLabelsAutoHideAtHighDensity = false;
            GalleryGridSpacingX = 0f;
            GalleryGridSpacingY = 0f;
            GalleryGridThumbnailPadding = 0f;
            GalleryGridHoverBorderWidth = 1f;
            GalleryGridSelectedBorderWidth = 2f;
            GalleryGridBorderInwardWhenSquare = true;
            GalleryGridBorderColorR = 1f;
            GalleryGridBorderColorG = 1f;
            GalleryGridBorderColorB = 0f;
            GalleryGridBorderColorA = 1f;
            GalleryTboxToolbarPinned = false;
            UiLocale = "";
            SpringScrollButtonEnabled = true;
            HoldToLaunchEnabled = false;
            HoldToLaunchPrevEnableDragDrop = false;
            HoldToLaunchHoldSeconds = 1f;
            QuickMenuButtonsVersion = 1;
            QuickMenuButtonsCurrentPage = 0;
            QuickMenuButtonsPages = null;
            QuickMenuEditSlotIdx = 12;
            QuickMenuPageToggleSlotIdx = 15;
            GalleryCategoryQuickOrder = "";
            GalleryCategoryQuickSwitchHidden = "";
            GalleryUserTagPinnedOrder = "";
            BaMigrationPromptDismissed = false;
            GalleryScrollButtonStepViewportFraction = 0.65f;
            GalleryScrollButtonsEnabled = true;
            GalleryVrThumbstickScrollEnabled = true;
            GlobalSourceFilter = GlobalSourceFilterValue.All;

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
                        if (node["DisableGalleryTransparency"] != null) DisableGalleryTransparency = node["DisableGalleryTransparency"].AsBool;
                        if (node["DisableGalleryAssignableButtonsTransparency"] != null) DisableGalleryAssignableButtonsTransparency = node["DisableGalleryAssignableButtonsTransparency"].AsBool;
                        if (node["DisableGalleryDockHoverTransparency"] != null) DisableGalleryDockHoverTransparency = node["DisableGalleryDockHoverTransparency"].AsBool;
                        if (node["EnableGalleryFade"] != null) EnableGalleryFade = node["EnableGalleryFade"].AsBool;
                        if (node["EnableGalleryTranslucency"] != null) EnableGalleryTranslucency = node["EnableGalleryTranslucency"].AsBool;
                        if (node["DisableGalleryPaneTransparency"] != null)
                            DisableGalleryPaneTransparency = node["DisableGalleryPaneTransparency"].AsBool;
                        else
                            DisableGalleryPaneTransparency = !EnableGalleryTranslucency;
                        if (node["GalleryManualRefreshOnly"] != null) GalleryManualRefreshOnly = node["GalleryManualRefreshOnly"].AsBool;
                        if (node["GalleryOpacity"] != null) GalleryOpacity = node["GalleryOpacity"].AsFloat;
                        if (node["DragDropReplaceMode"] != null) DragDropReplaceMode = node["DragDropReplaceMode"].AsBool;
                        if (node["AppearanceClothingApplyMode"] != null)
                            AppearanceClothingApplyMode = node["AppearanceClothingApplyMode"].Value;
                        else if (node["KeepClothingWhenApplyingAppearance"] != null)
                            AppearanceClothingApplyMode = node["KeepClothingWhenApplyingAppearance"].AsBool ? "keep" : "replace";
                        if (node["SuppressAppearanceScaleChange"] != null) SuppressAppearanceScaleChange = node["SuppressAppearanceScaleChange"].AsBool;
                        if (node["SuppressCheesyFxNullReferenceLogs"] != null) SuppressCheesyFxNullReferenceLogs = node["SuppressCheesyFxNullReferenceLogs"].AsBool;
                        if (node["EnableDragDrop"] != null) EnableDragDrop = node["EnableDragDrop"].AsBool;
                        if (node["GalleryAutoGenderFilter"] != null) GalleryAutoGenderFilter = node["GalleryAutoGenderFilter"].AsBool;
                        if (node["GalleryCollapseOnSceneLaunch"] != null) GalleryCollapseOnSceneLaunch = node["GalleryCollapseOnSceneLaunch"].AsBool;
                        if (node["DragHoldThreshold"] != null)
                            DragHoldThreshold = ClampDragHoldThreshold(node["DragHoldThreshold"].AsFloat);
                        if (node["RequireDragHoldBeforeMove"] != null)
                            RequireDragHoldBeforeMove = node["RequireDragHoldBeforeMove"].AsBool;
                        if (node["ApplyMode"] != null) ApplyMode = node["ApplyMode"].Value;
                        if (node["LastGalleryCategory"] != null) LastGalleryCategory = node["LastGalleryCategory"].Value;
                        if (node["InitialGalleryCategory"] != null)
                            InitialGalleryCategory = NormalizeInitialGalleryCategory(node["InitialGalleryCategory"].Value);
                        if (node["global_source_filter"] != null)
                        {
                            // .NET Framework 3.5 has no generic Enum.TryParse, so Parse with ignoreCase + try/catch and
                            // bound-check via Enum.IsDefined. Treat any unknown/legacy value as All so users do not get
                            // stranded on a filter we no longer recognize.
                            string gsfRaw = node["global_source_filter"].Value;
                            GlobalSourceFilterValue parsed = GlobalSourceFilterValue.All;
                            if (!string.IsNullOrEmpty(gsfRaw))
                            {
                                try
                                {
                                    object boxed = Enum.Parse(typeof(GlobalSourceFilterValue), gsfRaw, true);
                                    if (boxed is GlobalSourceFilterValue && Enum.IsDefined(typeof(GlobalSourceFilterValue), boxed))
                                        parsed = (GlobalSourceFilterValue)boxed;
                                }
                                catch { }
                            }
                            GlobalSourceFilter = parsed;
                        }
                        if (node["GalleryDefaultLeftSidePanel"] != null)
                            GalleryDefaultLeftSidePanel = NormalizeGallerySidePanel(node["GalleryDefaultLeftSidePanel"].Value);
                        if (node["GalleryDefaultRightSidePanel"] != null)
                            GalleryDefaultRightSidePanel = NormalizeGallerySidePanel(node["GalleryDefaultRightSidePanel"].Value);
                        if (node["GalleryScrollButtonStepViewportFraction"] != null)
                            GalleryScrollButtonStepViewportFraction = Mathf.Clamp(node["GalleryScrollButtonStepViewportFraction"].AsFloat, 0.10f, 2.00f);
                        if (node["GalleryScrollButtonsEnabled"] != null)
                            GalleryScrollButtonsEnabled = node["GalleryScrollButtonsEnabled"].AsBool;
                        if (node["GalleryVrThumbstickScrollEnabled"] != null)
                            GalleryVrThumbstickScrollEnabled = node["GalleryVrThumbstickScrollEnabled"].AsBool;
                        if (node["GalleryHideCreatorSideButtons"] != null)
                            GalleryHideCreatorSideButtons = node["GalleryHideCreatorSideButtons"].AsBool;
                        if (node["DesktopFixedMode"] != null) DesktopFixedMode = node["DesktopFixedMode"].AsBool;
                        if (node["DesktopFixedAutoCollapse"] != null) DesktopFixedAutoCollapse = node["DesktopFixedAutoCollapse"].AsBool;
                        if (node["DesktopFixedAutoHideSeconds"] != null) DesktopFixedAutoHideSeconds = node["DesktopFixedAutoHideSeconds"].AsFloat;
                        if (node["DesktopFixedDockSide"] != null) DesktopFixedDockSide = NormalizeDesktopFixedDockSide(node["DesktopFixedDockSide"].Value);
                        if (node["DesktopFixedDefaultDockSide"] != null) DesktopFixedDefaultDockSide = NormalizeDesktopFixedDockSide(node["DesktopFixedDefaultDockSide"].Value);
                        if (node["DesktopFixedEnforceDockSide"] != null) DesktopFixedEnforceDockSide = node["DesktopFixedEnforceDockSide"].AsBool;
                        if (node["DesktopFixedEnforcedDockSide"] != null) DesktopFixedEnforcedDockSide = NormalizeDesktopFixedDockSide(node["DesktopFixedEnforcedDockSide"].Value);
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
                        if (node["GalleryPrettyPresetNames"] != null) GalleryPrettyPresetNames = node["GalleryPrettyPresetNames"].AsBool;
                        if (node["GallerySearchScope"] != null) GallerySearchScope = NormalizeGallerySearchScope(node["GallerySearchScope"].Value);
                        if (node["GalleryHoverPreviewMode"] != null)
                            GalleryHoverPreviewMode = NormalizeHoverPreviewMode(node["GalleryHoverPreviewMode"].Value);
                        else if (node["GalleryListHoverPreviewEnabled"] != null)
                            GalleryHoverPreviewMode = node["GalleryListHoverPreviewEnabled"].AsBool ? "List" : "Off";
                        if (node["GalleryListHoverPreviewSize"] != null) GalleryListHoverPreviewSize = Mathf.Clamp(node["GalleryListHoverPreviewSize"].AsFloat, 200f, 600f);
                        if (node["GalleryListHoverPreviewOffsetX"] != null) GalleryListHoverPreviewOffsetX = Mathf.Clamp(node["GalleryListHoverPreviewOffsetX"].AsFloat, -2000f, 2000f);
                        if (node["GalleryListHoverPreviewOffsetY"] != null) GalleryListHoverPreviewOffsetY = Mathf.Clamp(node["GalleryListHoverPreviewOffsetY"].AsFloat, -2000f, 2000f);
                        if (node["GalleryGridLabelsEnabled"] != null) GalleryGridLabelsEnabled = node["GalleryGridLabelsEnabled"].AsBool;
                        if (node["GalleryGridLabelFontSize"] != null) GalleryGridLabelFontSize = Mathf.Clamp(node["GalleryGridLabelFontSize"].AsFloat, 8f, 40f);
                        if (node["GalleryGridLabelsAutoHideAtHighDensity"] != null) GalleryGridLabelsAutoHideAtHighDensity = node["GalleryGridLabelsAutoHideAtHighDensity"].AsBool;
                        if (node["GalleryGridSpacingX"] != null) GalleryGridSpacingX = Mathf.Clamp(node["GalleryGridSpacingX"].AsFloat, 0f, 80f);
                        if (node["GalleryGridSpacingY"] != null) GalleryGridSpacingY = Mathf.Clamp(node["GalleryGridSpacingY"].AsFloat, 0f, 80f);
                        if (node["GalleryGridThumbnailPadding"] != null) GalleryGridThumbnailPadding = Mathf.Clamp(node["GalleryGridThumbnailPadding"].AsFloat, 0f, 40f);
                        if (node["GalleryGridHoverBorderWidth"] != null) GalleryGridHoverBorderWidth = Mathf.Clamp(node["GalleryGridHoverBorderWidth"].AsFloat, 0f, 20f);
                        if (node["GalleryGridSelectedBorderWidth"] != null) GalleryGridSelectedBorderWidth = Mathf.Clamp(node["GalleryGridSelectedBorderWidth"].AsFloat, 0f, 30f);
                        if (node["GalleryGridBorderInwardWhenSquare"] != null) GalleryGridBorderInwardWhenSquare = node["GalleryGridBorderInwardWhenSquare"].AsBool;
                        if (node["GalleryGridBorderColorR"] != null) GalleryGridBorderColorR = Mathf.Clamp01(node["GalleryGridBorderColorR"].AsFloat);
                        if (node["GalleryGridBorderColorG"] != null) GalleryGridBorderColorG = Mathf.Clamp01(node["GalleryGridBorderColorG"].AsFloat);
                        if (node["GalleryGridBorderColorB"] != null) GalleryGridBorderColorB = Mathf.Clamp01(node["GalleryGridBorderColorB"].AsFloat);
                        if (node["GalleryGridBorderColorA"] != null) GalleryGridBorderColorA = Mathf.Clamp01(node["GalleryGridBorderColorA"].AsFloat);
                        if (node["GalleryTboxToolbarPinned"] != null) GalleryTboxToolbarPinned = node["GalleryTboxToolbarPinned"].AsBool;
                        if (node["GalleryOnlyWhenVamMenuVisible"] != null) GalleryOnlyWhenVamMenuVisible = node["GalleryOnlyWhenVamMenuVisible"].AsBool;
                        if (node["GalleryAnchorToVamMenu"] != null) GalleryAnchorToVamMenu = node["GalleryAnchorToVamMenu"].AsBool;
                        if (node["GalleryAnchorOffset"] != null)
                        {
                            var o = node["GalleryAnchorOffset"];
                            GalleryAnchorToVamMenu = true;
                            GalleryAnchorOffset = new Vector3(o["x"].AsFloat, 0.1f, -0.1f);
                        }
                        if (node["SideButtonScale"] != null) SideButtonScale = node["SideButtonScale"].AsFloat;
                        if (node["SideButtonScaleVR"] != null) SideButtonScaleVR = node["SideButtonScaleVR"].AsFloat;
                        else SideButtonScaleVR = SideButtonScale;
                        if (node["SideButtonScaleDesktop"] != null) SideButtonScaleDesktop = node["SideButtonScaleDesktop"].AsFloat;
                        else SideButtonScaleDesktop = SideButtonScale;

                        if (node["InnerPaneScale"] != null) InnerPaneScale = node["InnerPaneScale"].AsFloat;
                        if (node["InnerPaneScaleVR"] != null) InnerPaneScaleVR = node["InnerPaneScaleVR"].AsFloat;
                        else InnerPaneScaleVR = InnerPaneScale;
                        if (node["InnerPaneScaleDesktop"] != null) InnerPaneScaleDesktop = node["InnerPaneScaleDesktop"].AsFloat;
                        else InnerPaneScaleDesktop = InnerPaneScale;
                        if (node["SpringScrollButtonEnabled"] != null) SpringScrollButtonEnabled = node["SpringScrollButtonEnabled"].AsBool;
                        if (node["HoldToLaunchEnabled"] != null) HoldToLaunchEnabled = node["HoldToLaunchEnabled"].AsBool;
                        if (node["HoldToLaunchPrevEnableDragDrop"] != null) HoldToLaunchPrevEnableDragDrop = node["HoldToLaunchPrevEnableDragDrop"].AsBool;
                        if (node["HoldToLaunchHoldSeconds"] != null)
                            HoldToLaunchHoldSeconds = Mathf.Clamp(node["HoldToLaunchHoldSeconds"].AsFloat, 0.2f, 1f);
                        if (node["BaMigrationPromptDismissed"] != null) BaMigrationPromptDismissed = node["BaMigrationPromptDismissed"].AsBool;
                        // Quick Menu buttons (pages)
                        try
                        {
                            JSONNode qm = node["QuickMenuButtons"];
                            if (qm != null)
                            {
                                if (qm["version"] != null) QuickMenuButtonsVersion = qm["version"].AsInt;
                                if (qm["currentPage"] != null) QuickMenuButtonsCurrentPage = qm["currentPage"].AsInt;
                                if (qm["editSlotIdx"] != null) QuickMenuEditSlotIdx = qm["editSlotIdx"].AsInt;
                                if (qm["pageToggleSlotIdx"] != null) QuickMenuPageToggleSlotIdx = qm["pageToggleSlotIdx"].AsInt;

                                JSONNode pages = qm["pages"];
                                // SimpleJSON variant in VaM does not expose IsArray; treat nodes with children as arrays.
                                if (pages != null && pages.Count > 0)
                                {
                                    int pageCount = pages.Count;
                                    QuickMenuButtonsPages = new string[pageCount][];
                                    for (int p = 0; p < pageCount; p++)
                                    {
                                        JSONNode pa = pages[p];
                                        if (pa != null && pa.Count > 0)
                                        {
                                            int slotCount = pa.Count;
                                            var slots = new string[slotCount];
                                            for (int s = 0; s < slotCount; s++)
                                                slots[s] = pa[s] != null ? pa[s].Value : "";
                                            QuickMenuButtonsPages[p] = slots;
                                        }
                                        else
                                        {
                                            QuickMenuButtonsPages[p] = new string[0];
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
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
                        if (node["GalleryCategoryQuickOrder"] != null)
                            GalleryCategoryQuickOrder = node["GalleryCategoryQuickOrder"].Value ?? "";
                        if (node["GalleryCategoryQuickSwitchHidden"] != null)
                            GalleryCategoryQuickSwitchHidden = node["GalleryCategoryQuickSwitchHidden"].Value ?? "";
                        if (node["GalleryUserTagPinnedOrder"] != null)
                            GalleryUserTagPinnedOrder = node["GalleryUserTagPinnedOrder"].Value ?? "";
                    }

                    // Migration: older builds forced EnableDragDrop off when HoldToLaunchEnabled was on, and persisted that forced-off value.
                    // Restore user intent (EnableDragDrop) from HoldToLaunchPrevEnableDragDrop while keeping effective suppression via EffectiveEnableDragDrop.
                    try
                    {
                        if (HoldToLaunchEnabled && !EnableDragDrop && HoldToLaunchPrevEnableDragDrop)
                            EnableDragDrop = true;
                    }
                    catch { }

                    // Migration: drag hold duration floor + hold-before-drag always on when drag-and-drop enabled.
                    try
                    {
                        float prevThr = DragHoldThreshold;
                        bool prevReq = RequireDragHoldBeforeMove;
                        NormalizeDragDropHoldSettings();
                        if (Mathf.Abs(prevThr - DragHoldThreshold) > 0.0001f || prevReq != RequireDragHoldBeforeMove)
                            Save(false, true);
                    }
                    catch { }

                    // Migration/validation: clamp saved UI scales into supported range.
                    // Older builds allowed smaller/larger values; keep configs stable by rewriting once.
                    try
                    {
                        bool changed = false;
                        float sbs = ClampUiScale(SideButtonScale);
                        float sbsVr = ClampUiScale(SideButtonScaleVR);
                        float sbsDesk = ClampUiScale(SideButtonScaleDesktop);
                        float ipsVr = ClampUiScale(InnerPaneScaleVR);
                        float ipsDesk = ClampUiScale(InnerPaneScaleDesktop);
                        if (Mathf.Abs(SideButtonScale - sbs) > 0.0001f) { SideButtonScale = sbs; changed = true; }
                        if (Mathf.Abs(SideButtonScaleVR - sbsVr) > 0.0001f) { SideButtonScaleVR = sbsVr; changed = true; }
                        if (Mathf.Abs(SideButtonScaleDesktop - sbsDesk) > 0.0001f) { SideButtonScaleDesktop = sbsDesk; changed = true; }
                        if (Mathf.Abs(InnerPaneScaleVR - ipsVr) > 0.0001f) { InnerPaneScaleVR = ipsVr; changed = true; }
                        if (Mathf.Abs(InnerPaneScaleDesktop - ipsDesk) > 0.0001f) { InnerPaneScaleDesktop = ipsDesk; changed = true; }

                        // Migration/validation: clamp fixed-mode anchors so panel never becomes unusably tiny.
                        // Prevents "stuck" desktop UI when DesktopCustomHeight/Width are out of range.
                        float w = ClampDesktopFixedAnchor01(DesktopCustomWidth, 0.05f, 0.85f, 1.618f / 2.618f);
                        float h = ClampDesktopFixedAnchor01(DesktopCustomHeight, 0.05f, 0.85f, 0.5f);
                        if (Mathf.Abs(DesktopCustomWidth - w) > 0.0001f) { DesktopCustomWidth = w; changed = true; }
                        if (Mathf.Abs(DesktopCustomHeight - h) > 0.0001f) { DesktopCustomHeight = h; changed = true; }
                        float ah = ClampDesktopFixedAnchor01(DesktopFixedAutoHideSeconds, 0.1f, 10f, 1.0f);
                        if (Mathf.Abs(DesktopFixedAutoHideSeconds - ah) > 0.0001f) { DesktopFixedAutoHideSeconds = ah; changed = true; }
                        if (changed)
                        {
                            // Persist without notifying listeners; load-time UI will read clamped values.
                            Save(false, true);
                        }
                    }
                    catch { }

                    try
                    {
                        if (Settings.Instance != null && Settings.Instance.LogVerboseUi != null && Settings.Instance.LogVerboseUi.Value)
                            VPBLogger.Config.LogInfo("cfg path=" + ConfigPath + " | LastGalleryCategory=" + LastGalleryCategory + " | DragDropReplaceMode=" + DragDropReplaceMode + " | AppearanceClothing=" + AppearanceClothingApplyMode + " | ApplyMode=" + ApplyMode);
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
                            VPBLogger.Config.LogInfo("Loaded LastGalleryCategory='" + LastGalleryCategory + "' from " + ConfigPath);
                        }
                    }
                    catch { }
                }
                else
                {
                    VPBLogger.Config.LogWarning("Error loading config: File DOES NOT EXIST at: " + ConfigPath);
                }
            }
            catch (Exception ex)
            {
                VPBLogger.Config.LogError("Error loading config: " + ex.Message);
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
                node["DisableGalleryTransparency"].AsBool = DisableGalleryTransparency;
                node["DisableGalleryPaneTransparency"].AsBool = DisableGalleryPaneTransparency;
                node["DisableGalleryAssignableButtonsTransparency"].AsBool = DisableGalleryAssignableButtonsTransparency;
                node["DisableGalleryDockHoverTransparency"].AsBool = DisableGalleryDockHoverTransparency;
                node["EnableGalleryFade"].AsBool = EnableGalleryFade;
                node["EnableGalleryTranslucency"].AsBool = EnableGalleryTranslucency;
                node["GalleryManualRefreshOnly"].AsBool = GalleryManualRefreshOnly;
                node["GalleryOpacity"].AsFloat = GalleryOpacity;
                node["DragDropReplaceMode"].AsBool = DragDropReplaceMode;
                node["AppearanceClothingApplyMode"] = AppearanceClothingApplyMode;
                node["SuppressAppearanceScaleChange"].AsBool = SuppressAppearanceScaleChange;
                node["SuppressCheesyFxNullReferenceLogs"].AsBool = SuppressCheesyFxNullReferenceLogs;
                node["KeepClothingWhenApplyingAppearance"].AsBool = KeepClothingWhenApplyingAppearance;
                node["EnableDragDrop"].AsBool = EnableDragDrop;
                node["GalleryAutoGenderFilter"].AsBool = GalleryAutoGenderFilter;
                node["GalleryCollapseOnSceneLaunch"].AsBool = GalleryCollapseOnSceneLaunch;
                NormalizeDragDropHoldSettings();
                node["RequireDragHoldBeforeMove"].AsBool = RequireDragHoldBeforeMove;
                node["DragHoldThreshold"].AsFloat = DragHoldThreshold;
                node["ApplyMode"] = ApplyMode;
                node["LastGalleryCategory"] = LastGalleryCategory;
                node["InitialGalleryCategory"] = InitialGalleryCategory;
                node["global_source_filter"] = GlobalSourceFilter.ToString();
                node["GalleryDefaultLeftSidePanel"] = GalleryDefaultLeftSidePanel;
                node["GalleryDefaultRightSidePanel"] = GalleryDefaultRightSidePanel;
                node["GalleryScrollButtonStepViewportFraction"].AsFloat = Mathf.Clamp(GalleryScrollButtonStepViewportFraction, 0.10f, 2.00f);
                node["GalleryScrollButtonsEnabled"].AsBool = GalleryScrollButtonsEnabled;
                node["GalleryVrThumbstickScrollEnabled"].AsBool = GalleryVrThumbstickScrollEnabled;
                node["GalleryHideCreatorSideButtons"].AsBool = GalleryHideCreatorSideButtons;
                node["DesktopFixedMode"].AsBool = DesktopFixedMode;
                node["DesktopFixedAutoCollapse"].AsBool = DesktopFixedAutoCollapse;
                node["DesktopFixedAutoHideSeconds"].AsFloat = Mathf.Clamp(DesktopFixedAutoHideSeconds, 0.1f, 10f);
                node["DesktopFixedDockSide"] = NormalizeDesktopFixedDockSide(DesktopFixedDockSide);
                node["DesktopFixedDefaultDockSide"] = NormalizeDesktopFixedDockSide(DesktopFixedDefaultDockSide);
                node["DesktopFixedEnforceDockSide"].AsBool = DesktopFixedEnforceDockSide;
                node["DesktopFixedEnforcedDockSide"] = NormalizeDesktopFixedDockSide(DesktopFixedEnforcedDockSide);
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
                node["GalleryPrettyPresetNames"].AsBool = GalleryPrettyPresetNames;
                node["GallerySearchScope"] = NormalizeGallerySearchScope(GallerySearchScope);
                node["GalleryHoverPreviewMode"] = NormalizeHoverPreviewMode(GalleryHoverPreviewMode);
                node["GalleryListHoverPreviewSize"].AsFloat = GalleryListHoverPreviewSize;
                node["GalleryListHoverPreviewOffsetX"].AsFloat = GalleryListHoverPreviewOffsetX;
                node["GalleryListHoverPreviewOffsetY"].AsFloat = GalleryListHoverPreviewOffsetY;
                node["GalleryGridLabelsEnabled"].AsBool = GalleryGridLabelsEnabled;
                node["GalleryGridLabelFontSize"].AsFloat = GalleryGridLabelFontSize;
                node["GalleryGridLabelsAutoHideAtHighDensity"].AsBool = GalleryGridLabelsAutoHideAtHighDensity;
                node["GalleryGridSpacingX"].AsFloat = Mathf.Clamp(GalleryGridSpacingX, 0f, 80f);
                node["GalleryGridSpacingY"].AsFloat = Mathf.Clamp(GalleryGridSpacingY, 0f, 80f);
                node["GalleryGridThumbnailPadding"].AsFloat = Mathf.Clamp(GalleryGridThumbnailPadding, 0f, 40f);
                node["GalleryGridHoverBorderWidth"].AsFloat = Mathf.Clamp(GalleryGridHoverBorderWidth, 0f, 20f);
                node["GalleryGridSelectedBorderWidth"].AsFloat = Mathf.Clamp(GalleryGridSelectedBorderWidth, 0f, 30f);
                node["GalleryGridBorderInwardWhenSquare"].AsBool = GalleryGridBorderInwardWhenSquare;
                node["GalleryGridBorderColorR"].AsFloat = Mathf.Clamp01(GalleryGridBorderColorR);
                node["GalleryGridBorderColorG"].AsFloat = Mathf.Clamp01(GalleryGridBorderColorG);
                node["GalleryGridBorderColorB"].AsFloat = Mathf.Clamp01(GalleryGridBorderColorB);
                node["GalleryGridBorderColorA"].AsFloat = Mathf.Clamp01(GalleryGridBorderColorA);
                node["GalleryTboxToolbarPinned"].AsBool = GalleryTboxToolbarPinned;
                node["GalleryOnlyWhenVamMenuVisible"].AsBool = GalleryOnlyWhenVamMenuVisible;
                node["GalleryAnchorToVamMenu"].AsBool = GalleryAnchorToVamMenu;
                JSONClass o = new JSONClass();
                o["x"].AsFloat = GalleryAnchorOffset.x;
                o["y"].AsFloat = GalleryAnchorOffset.y;
                o["z"].AsFloat = GalleryAnchorOffset.z;
                node["GalleryAnchorOffset"] = o;
                node["SideButtonScale"].AsFloat = SideButtonScale;
                node["SideButtonScaleVR"].AsFloat = SideButtonScaleVR;
                node["SideButtonScaleDesktop"].AsFloat = SideButtonScaleDesktop;
                node["InnerPaneScale"].AsFloat = InnerPaneScale;
                node["InnerPaneScaleVR"].AsFloat = InnerPaneScaleVR;
                node["InnerPaneScaleDesktop"].AsFloat = InnerPaneScaleDesktop;
                node["SpringScrollButtonEnabled"].AsBool = SpringScrollButtonEnabled;
                node["HoldToLaunchEnabled"].AsBool = HoldToLaunchEnabled;
                node["HoldToLaunchPrevEnableDragDrop"].AsBool = HoldToLaunchPrevEnableDragDrop;
                node["HoldToLaunchHoldSeconds"].AsFloat = Mathf.Clamp(HoldToLaunchHoldSeconds, 0.2f, 1f);
                node["BaMigrationPromptDismissed"].AsBool = BaMigrationPromptDismissed;
                node["UiLocale"] = UiLocale ?? "en";
                node["HiddenCategories"] = string.Join(",", new List<string>(HiddenCategories ?? new HashSet<string>()).ToArray());
                node["GalleryCategoryQuickOrder"] = GalleryCategoryQuickOrder ?? "";
                node["GalleryCategoryQuickSwitchHidden"] = GalleryCategoryQuickSwitchHidden ?? "";
                node["GalleryUserTagPinnedOrder"] = GalleryUserTagPinnedOrder ?? "";
                // Quick Menu buttons (pages)
                try
                {
                    JSONClass qm = new JSONClass();
                    qm["version"].AsInt = QuickMenuButtonsVersion;
                    qm["currentPage"].AsInt = QuickMenuButtonsCurrentPage;
                    qm["editSlotIdx"].AsInt = QuickMenuEditSlotIdx;
                    qm["pageToggleSlotIdx"].AsInt = QuickMenuPageToggleSlotIdx;
                    JSONArray pages = new JSONArray();
                    if (QuickMenuButtonsPages != null)
                    {
                        for (int p = 0; p < QuickMenuButtonsPages.Length; p++)
                        {
                            JSONArray slots = new JSONArray();
                            var arr = QuickMenuButtonsPages[p] ?? new string[0];
                            for (int s = 0; s < arr.Length; s++)
                                slots.Add(arr[s] ?? "");
                            pages.Add(slots);
                        }
                    }
                    qm["pages"] = pages;
                    node["QuickMenuButtons"] = qm;
                }
                catch { }
                long msBuild = sw.ElapsedMilliseconds;
                string jsonOutput = JsonSerializationUtil.Serialize(node, 32_768);
                long msAfterToString = sw.ElapsedMilliseconds;
                File.WriteAllText(path, jsonOutput);
                long msAfterDisk = sw.ElapsedMilliseconds;
                if (notifyListeners)
                {
                    try
                    {
                        InvokeConfigChanged();
                    }
                    finally
                    {
                        _lightweightGalleryTabRefreshSlotsRemaining = 0;
                    }
                }

                try
                {
                    if (Settings.Instance != null && Settings.Instance.LogVerboseUi != null && Settings.Instance.LogVerboseUi.Value)
                        VPBLogger.Config.LogInfo("Saved cfg path=" + path + " | LastGalleryCategory=" + LastGalleryCategory + " | DragDropReplaceMode=" + DragDropReplaceMode + " | AppearanceClothing=" + AppearanceClothingApplyMode + " | ApplyMode=" + ApplyMode);
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
                VPBLogger.Config.LogError("[VPB] Error saving config: " + ex.Message);
            }
        }

        public void TriggerChange()
        {
            _lightweightGalleryTabRefreshSlotsRemaining = 0;
            try
            {
                InvokeConfigChanged();
            }
            finally
            {
                _lightweightGalleryTabRefreshSlotsRemaining = 0;
            }
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

            bool isVR = IsVR;

            if (setting == "VR") return isVR;
            if (setting == "Desktop") return !isVR;

            return false;
        }

        private static readonly string[] s_DesktopFixedDockSideCanonical = { "Right", "Left", "Top" };

        public static string NormalizeDesktopFixedDockSide(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Right";
            string v = value.Trim();
            for (int i = 0; i < s_DesktopFixedDockSideCanonical.Length; i++)
            {
                if (string.Equals(v, s_DesktopFixedDockSideCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_DesktopFixedDockSideCanonical[i];
            }
            return "Right";
        }

        private static float ClampDesktopFixedAnchor01(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public bool ShouldDisableGalleryPaneTransparency()
        {
            return DisableGalleryTransparency || DisableGalleryPaneTransparency;
        }

        public bool ShouldDisableGalleryAssignableButtonsTransparency()
        {
            return DisableGalleryTransparency || DisableGalleryAssignableButtonsTransparency;
        }

        public bool ShouldDisableGalleryDockHoverTransparency()
        {
            return DisableGalleryTransparency || DisableGalleryDockHoverTransparency;
        }
    }
}
