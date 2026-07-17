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

        public const float MinUiScale = 0.5f;
        public const float MaxUiScale = 2.0f;
        public const float MinGalleryElementCornerRadiusFraction = 0.05f;
        public const float MaxGalleryElementCornerRadiusFraction = 0.5f;

        private static float ClampUiScale(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) return 1f;
            if (v < MinUiScale) return MinUiScale;
            if (v > MaxUiScale) return MaxUiScale;
            return v;
        }

        public static float ClampGalleryElementCornerRadiusFraction(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = GalleryUiDesignTokens.ButtonCornerRadiusFraction;
            return Mathf.Clamp(v, MinGalleryElementCornerRadiusFraction, MaxGalleryElementCornerRadiusFraction);
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
        /// <summary>When false, gallery buttons and other rounded elements render with square corners.</summary>
        public bool EnableGalleryElementRounding = true;
        /// <summary>Corner radius as a fraction (0.05..0.5) of each element's shorter side. Used when <see cref="EnableGalleryElementRounding"/> is true.</summary>
        public float GalleryElementCornerRadiusFraction = GalleryUiDesignTokens.ButtonCornerRadiusFraction;
        /// <summary>When true, first-run hint strip under title bar is hidden permanently.</summary>
        public bool FirstRunHintsDismissed = false;
        /// <summary>When true (default), VR hover dwell shows a local tooltip label on controls.</summary>
        public bool VrHoverTooltipEnabled = true;
        /// <summary>Mode setup wizard completed for desktop fixed dock.</summary>
        public bool ModeSetupWizardDoneDesktopFixed = false;
        /// <summary>Mode setup wizard completed for desktop floating gallery.</summary>
        public bool ModeSetupWizardDoneDesktopFloating = false;
        /// <summary>Mode setup wizard completed for VR gallery sessions.</summary>
        public bool ModeSetupWizardDoneVR = false;
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
        /// <summary>How gallery applies an appearance .vap: replace (full), keep (keep body garments), clothingOnly (garment outfit from preset only), mergeoutfit (keep body; pick clothing items to merge on top).</summary>
        private string _appearanceClothingApplyMode = "replace";
        public string AppearanceClothingApplyMode
        {
            get { return string.IsNullOrEmpty(_appearanceClothingApplyMode) ? "replace" : _appearanceClothingApplyMode; }
            set
            {
                if (string.IsNullOrEmpty(value)) { _appearanceClothingApplyMode = "replace"; return; }
                string v = value.Trim().ToLowerInvariant();
                if (v == "keep" || v == "replace" || v == "clothingonly" || v == "mergeoutfit")
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
        /// <summary>Persisted import-sidebar toggle state (suppress-clothing, only-suppress-real, sub-toggles, last type). See GalleryPanel.ImportSidebar.cs Load/SaveImportSidebarPrefs.</summary>
        public JSONClass ImportSidebarPrefs = new JSONClass();
        /// <summary>When true, suppresses CheesyFX NullReferenceException spam in Unity/BepInEx logs (broken Update loops).</summary>
        public bool SuppressCheesyFxNullReferenceLogs = true;
        /// <summary>Controls when in-game VaM notification messages (errors and warnings) are suppressed. "Off" = never; "VR Only" = suppressed in VR; "Desktop Only" = suppressed on desktop; "Both" = always suppressed.</summary>
        public string BlockInGameMessages = "Off";
        /// <summary>When true, suppress VaM "Missing addon package … depends on …" spam in Unity/BepInEx and in-game error log.</summary>
        public bool HideMissingDependencyLogs = true;
        /// <summary>When true, clear VaM in-game error and message logs at the start of each full scene load (not merge).</summary>
        public bool ClearInGameLogsOnSceneLaunch = false;
        /// <summary>Gallery item drag-and-drop to atoms/scene. Off by default (VR jitter / accidental drags); enable in Settings → Interaction.</summary>
        public bool EnableDragDrop = false;
        /// <summary>When true (default), Clothing/Hair categories auto-apply Male/Female subfilter based on selected target atom gender.</summary>
        public bool GalleryAutoGenderFilter = true;
        /// <summary>When true (default), visible gallery panes collapse (fixed dock) or hide (floating) when a scene is launched.</summary>
        public bool GalleryCollapseOnSceneLaunch = true;
        /// <summary>Effective drag-and-drop state at runtime; off while <see cref="HoldToLaunchEnabled"/> (same pointer hold as drag start).</summary>
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
        /// <summary>Gallery footer performance tuning (hair + mirrors) enabled.</summary>
        public bool PerfModeEnabled = false;
        /// <summary>Performance level 0–9 (10 steps). Persisted across sessions.</summary>
        public int PerfStepIndex = 0;
        /// <summary>Bump when step table changes; triggers one-time index remap on load.</summary>
        public int PerfStepScaleVersion = 0;
        /// <summary>Legacy 0–1 blend; used only to migrate old configs to PerfStepIndex.</summary>
        public float PerfBlend = 0f;
        /// <summary>Legacy preset id; migrated once if new keys absent.</summary>
        public string PerfPresetMode = "None";
        /// <summary>Obsolete: perf always re-applies on scene load while On. Kept for config compat only.</summary>
        public bool PerfReapplyOnSceneLoad = true;

        /// <summary>What session perf applies while footer perf is On (see Settings → Performance).</summary>
        public bool PerfApplyHair = true;
        public bool PerfApplyMirrors = true;
        public bool PerfApplyRenderScale = false;
        public bool PerfApplyMsaa = false;
        public bool PerfApplyPixelLightCount = false;
        public bool PerfApplySmoothPasses = false;
        public bool PerfApplyMirrorReflections = false;
        public bool PerfApplyRealtimeReflectionProbes = false;
        public bool PerfApplySoftPhysics = false;
        public bool PerfApplyGlowEffects = false;

        public static int PerfStepMaxIndex()
        {
            int n = VpbPerfController.StepCount;
            return n > 0 ? n - 1 : 0;
        }

        public static int ClampPerfStepIndex(int index)
        {
            return Mathf.Clamp(index, 0, PerfStepMaxIndex());
        }

        public static float ClampPerfBlend(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Mathf.Clamp01(v);
        }

        public static int BlendToPerfStepIndex(float blend01)
        {
            int max = PerfStepMaxIndex();
            if (max <= 0) return 0;
            return ClampPerfStepIndex(Mathf.RoundToInt(ClampPerfBlend(blend01) * max));
        }

        public void RemapPerfStepIndexIfScaleVersionChanged()
        {
            if (PerfStepScaleVersion >= VpbPerfController.PerfStepScaleVersion)
                return;
            int prev = PerfStepIndex;
            if (PerfStepScaleVersion < 2)
            {
                if (prev <= 1)
                    PerfStepIndex = 0;
                else
                    PerfStepIndex = ClampPerfStepIndex(prev - 2);
            }
            PerfStepIndex = ClampPerfStepIndex(PerfStepIndex);
            PerfStepScaleVersion = VpbPerfController.PerfStepScaleVersion;
        }

        public static void MigrateLegacyPerfPresetFields(VPBConfig cfg, bool hadExplicitPerfModeKey)
        {
            if (cfg == null) return;
            string mode = cfg.PerfPresetMode ?? "";
            if (string.IsNullOrEmpty(mode) || string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(mode, "P2", StringComparison.OrdinalIgnoreCase)) cfg.PerfStepIndex = 0;
            else if (string.Equals(mode, "P1", StringComparison.OrdinalIgnoreCase)) cfg.PerfStepIndex = 2;
            else if (string.Equals(mode, "Q1", StringComparison.OrdinalIgnoreCase)) cfg.PerfStepIndex = 6;
            else if (string.Equals(mode, "Q2", StringComparison.OrdinalIgnoreCase)) cfg.PerfStepIndex = PerfStepMaxIndex();
            else cfg.PerfStepIndex = BlendToPerfStepIndex(cfg.PerfBlend);
            cfg.PerfStepIndex = ClampPerfStepIndex(cfg.PerfStepIndex);

            // Old saves without PerfModeEnabled: infer On from legacy preset name only.
            if (!hadExplicitPerfModeKey && !cfg.PerfModeEnabled && LegacyPerfPresetImpliesEnabled(mode))
                cfg.PerfModeEnabled = true;
        }

        static bool LegacyPerfPresetImpliesEnabled(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return false;
            if (string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        /// <summary>Category when opening a new gallery pane or at session first open: "Scenes" (default), "Clothing", "Hair", "Pose", "Appearance", "Plugins", or "LastUsed".</summary>
        public string InitialGalleryCategory = "Scenes";
        /// <summary>Global source filter for gallery: All (default), Local (loose files only), or Var (.var packages only).</summary>
        public GlobalSourceFilterValue GlobalSourceFilter = GlobalSourceFilterValue.All;

        private static readonly string[] s_InitialGalleryCategoryCanonical = { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" };

        /// <summary>When false, plugin rows (.cs/.cslist/.dll under Custom/Scripts) show no thumbnail in the grid/list; selection info box can still show a sister .jpg/.png preview.</summary>
        public bool PluginGalleryGridThumbnails = true;
        /// <summary>When true, Plugins gallery category always shows in-preview labels and hides thumbnails, including rows that have sister images.</summary>
        public bool PluginGalleryCategoryLabelsOnly = false;
        /// <summary>When true (default), missing/black thumbnails show creator / package / item text inside the preview area.</summary>
        public bool GalleryThumbPlaceholderLabelsEnabled = true;
        /// <summary>Multiplier for in-preview placeholder font size (0.25–2). Scales with grid cell side length.</summary>
        public float GalleryThumbPlaceholderSizeScale = 0.7f;
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
        /// <summary>When true, grid cells show rating/status/deps badges only while hovered (not during scroll).</summary>
        public bool GalleryGridHoverBadgesEnabled = true;
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

        /// <summary>Gallery: draw inward border on packages included in VaM scan whitelist.</summary>
        public bool GalleryScanWlBorderEnabled = true;
        /// <summary>Gallery grid view: show scan-whitelist border on included packages.</summary>
        public bool GalleryScanWlBorderShowInGrid = true;
        /// <summary>Gallery list view: show scan-whitelist border on included packages.</summary>
        public bool GalleryScanWlBorderShowInList = true;
        /// <summary>Gallery: scan-whitelist border strip thickness (pixels).</summary>
        public float GalleryScanWlBorderWidth = 4f;
        /// <summary>Grid: inward frame inset for scan-whitelist border (pixels).</summary>
        public float GalleryScanWlGridFrameInset = 0f;
        /// <summary>List: inward frame inset for scan-whitelist border (pixels).</summary>
        public float GalleryScanWlListFrameInset = 2f;
        /// <summary>Grid: when true, border hugs thumbnail rect; when false, full cell.</summary>
        public bool GalleryScanWlBorderOnThumbnail = true;
        public float GalleryScanWlBorderColorR = 0.2f;
        public float GalleryScanWlBorderColorG = 0.95f;
        public float GalleryScanWlBorderColorB = 1f;
        public float GalleryScanWlBorderColorA = 1f;

        public Color GetGalleryScanWlBorderColor()
        {
            return new Color(
                Mathf.Clamp01(GalleryScanWlBorderColorR),
                Mathf.Clamp01(GalleryScanWlBorderColorG),
                Mathf.Clamp01(GalleryScanWlBorderColorB),
                Mathf.Clamp01(GalleryScanWlBorderColorA));
        }

        public void SetGalleryScanWlBorderColor(Color c)
        {
            GalleryScanWlBorderColorR = c.r;
            GalleryScanWlBorderColorG = c.g;
            GalleryScanWlBorderColorB = c.b;
            GalleryScanWlBorderColorA = c.a;
            try { TriggerChange(); } catch { }
        }

        /// <summary>Gallery: inward border on session-only temporary scan-whitelist UID overrides.</summary>
        public bool GalleryScanWlTempBorderEnabled = true;
        public bool GalleryScanWlTempBorderShowInGrid = true;
        public bool GalleryScanWlTempBorderShowInList = true;
        public float GalleryScanWlTempBorderWidth = 4f;
        public float GalleryScanWlTempGridFrameInset = 0f;
        public float GalleryScanWlTempListFrameInset = 2f;
        public bool GalleryScanWlTempBorderOnThumbnail = true;
        public float GalleryScanWlTempBorderColorR = 1f;
        public float GalleryScanWlTempBorderColorG = 0.15f;
        public float GalleryScanWlTempBorderColorB = 1f;
        public float GalleryScanWlTempBorderColorA = 1f;

        public Color GetGalleryScanWlTempBorderColor()
        {
            return new Color(
                Mathf.Clamp01(GalleryScanWlTempBorderColorR),
                Mathf.Clamp01(GalleryScanWlTempBorderColorG),
                Mathf.Clamp01(GalleryScanWlTempBorderColorB),
                Mathf.Clamp01(GalleryScanWlTempBorderColorA));
        }

        public void SetGalleryScanWlTempBorderColor(Color c)
        {
            GalleryScanWlTempBorderColorR = c.r;
            GalleryScanWlTempBorderColorG = c.g;
            GalleryScanWlTempBorderColorB = c.b;
            GalleryScanWlTempBorderColorA = c.a;
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
        /// <summary>When anchored to VaM menu, hide the gallery if a full-screen VaM panel becomes active (Settings, Hub, package managers) so they never overlap.</summary>
        public bool AnchorYieldsToVamPanels = true;

        // VR quick-menu wrist watch (the assignable-button grid moved onto a controller).
        /// <summary>Master show/hide for the VR wrist watch (toggled from the footer button).</summary>
        public bool QuickMenuVrWatchVisible = true;
        /// <summary>Which hand the watch rides on: "Off" / "Left only" / "Right only" / "Opposite to menu".</summary>
        public string QuickMenuVrWatchMode = "Opposite to menu";
        /// <summary>When true the watch only appears while the VaM menu is open; otherwise it shows at all times in VR.</summary>
        public bool QuickMenuVrWatchOnlyWithMenu = true;
        /// <summary>When true the watch face billboards to point at the player's eye; otherwise uses fixed local euler.</summary>
        public bool QuickMenuVrWatchFaceUser = true;
        /// <summary>World scale of the watch canvas.</summary>
        public float QuickMenuVrWatchScale = 0.0005f;
        /// <summary>Distance the panel is pulled from the controller toward the eye (off the controller's pointer ray).</summary>
        public float QuickMenuVrWatchTowardUserDist = 0.12f;
        /// <summary>Local position offset of the watch canvas on the controller.</summary>
        public Vector3 QuickMenuVrWatchOffset = new Vector3(0f, 0.05f, 0.04f);

        // Interaction toggles (persisted)
        /// <summary>"Off", "Desktop Only", "VR Only", "Desktop &amp; VR". Default Desktop &amp; VR.</summary>
        public string SpringScrollButtonMode = "Desktop & VR";
        public bool HoldToLaunchEnabled = false;
        /// <summary>Try-On Mode: apply presets non-destructively with a Keep/Compare/Revert bar.</summary>
        public bool TryOnModeEnabled = true;
        /// <summary>When ON, the E/C keys move the navigation rig up/down in world (complements WASD). On by default.</summary>
        public bool VerticalMoveKeysEnabled = true;
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

        /// <summary>Which list opens on the left when a gallery pane is created (see <see cref="GallerySidePanelOptions"/>).</summary>
        public string GalleryDefaultLeftSidePanel = "None";
        /// <summary>Which list opens on the right when a gallery pane is created (see <see cref="GallerySidePanelOptions"/>).</summary>
        public string GalleryDefaultRightSidePanel = "None";
        /// <summary>Default User Tags side panel mode when opening tags: FilterByTags (default), Tag, or FilterUntagged.</summary>
        public string GalleryDefaultUserTagAvailMode = "FilterByTags";
        /// <summary>When true (default), User Tags available list in filter-by-tags mode hides tags with zero items in the current category view.</summary>
        public bool GalleryHideUnusedUserTagsInFilterMode = true;
        /// <summary>Multi-tag grid filter: Compound (any selected tag, default) or Isolate (all selected tags).</summary>
        public string GalleryUserTagFilterCombineMode = "Compound";
        /// <summary>Big scroll button step in viewport heights.</summary>
        public float GalleryScrollButtonStepViewportFraction = 0.65f;
        /// <summary>When true, show big VR up/down scroll buttons on gallery and tag lists.</summary>
        public bool GalleryScrollButtonsEnabled = true;
        /// <summary>When true, VR thumbstick forward/back scrolls the gallery while the pointer is over a pane (blocks free-move on that axis).</summary>
        public bool GalleryVrThumbstickScrollEnabled = true;
        /// <summary>When true, gallery hides side-rail Creator buttons; creator filtering uses title-bar control only. Side creator panes stay closed.</summary>
        public bool GalleryHideCreatorSideButtons = false;
        /// <summary>When true (default), side-rail Category mode shows per-category left icons (c_*.png).</summary>
        public bool GalleryShowCategoryIcons = true;
        /// <summary>When true, creator side/title lists merge names that differ only by case; label uses the variant with the most packages and counts are summed.</summary>
        public bool GalleryConsolidateCreatorNames = true;
        /// <summary>When true, BA migration prompt has been dismissed and will not appear again.</summary>
        public bool BaMigrationPromptDismissed = false;

        /// <summary>Settings cycle options for <see cref="GalleryDefaultLeftSidePanel"/> / <see cref="GalleryDefaultRightSidePanel"/>.</summary>
        public static readonly string[] GallerySidePanelOptions = { "None", "Import", "Tags", "Category", "Creator", "Path", "History" };

        private static readonly string[] s_GallerySidePanelCanonical = GallerySidePanelOptions;

        /// <summary>Maps user/config values to a canonical side-panel default (see <see cref="GallerySidePanelOptions"/>).</summary>
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

        private static readonly string[] s_GalleryDefaultUserTagAvailModeCanonical = { "FilterByTags", "Tag", "FilterUntagged" };

        /// <summary>Maps user/config values to FilterByTags, Tag, or FilterUntagged.</summary>
        public static string NormalizeGalleryDefaultUserTagAvailMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "FilterByTags";
            string v = value.Trim();
            for (int i = 0; i < s_GalleryDefaultUserTagAvailModeCanonical.Length; i++)
            {
                if (string.Equals(v, s_GalleryDefaultUserTagAvailModeCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_GalleryDefaultUserTagAvailModeCanonical[i];
            }
            if (string.Equals(v, "Filter tags", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Filter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Filter Mode", StringComparison.OrdinalIgnoreCase))
                return "FilterByTags";
            if (string.Equals(v, "Apply tags", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Apply", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Tag Mode", StringComparison.OrdinalIgnoreCase))
                return "Tag";
            if (string.Equals(v, "Untagged only", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Untagged", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Not Tagged", StringComparison.OrdinalIgnoreCase))
                return "FilterUntagged";
            return "FilterByTags";
        }

        /// <summary>Settings cycle label for <see cref="GalleryDefaultUserTagAvailMode"/>.</summary>
        public static string FormatGalleryDefaultUserTagAvailModeForSettings(string value)
        {
            string n = NormalizeGalleryDefaultUserTagAvailMode(value);
            if (string.Equals(n, "Tag", StringComparison.OrdinalIgnoreCase))
                return "Apply tags";
            if (string.Equals(n, "FilterUntagged", StringComparison.OrdinalIgnoreCase))
                return "Untagged only";
            return "Filter tags";
        }

        /// <summary>Resolved default when opening User Tags side panel or clearing category tag filters.</summary>
        public UserTagAvailMode ResolveDefaultUserTagAvailMode()
        {
            string n = NormalizeGalleryDefaultUserTagAvailMode(GalleryDefaultUserTagAvailMode);
            if (string.Equals(n, "Tag", StringComparison.OrdinalIgnoreCase))
                return UserTagAvailMode.Tag;
            if (string.Equals(n, "FilterUntagged", StringComparison.OrdinalIgnoreCase))
                return UserTagAvailMode.FilterUntagged;
            return UserTagAvailMode.FilterByTags;
        }

        private static readonly string[] s_GalleryUserTagFilterCombineModeCanonical = { "Compound", "Isolate" };

        /// <summary>Maps user/config values to Compound or Isolate.</summary>
        public static string NormalizeGalleryUserTagFilterCombineMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Compound";
            string v = value.Trim();
            for (int i = 0; i < s_GalleryUserTagFilterCombineModeCanonical.Length; i++)
            {
                if (string.Equals(v, s_GalleryUserTagFilterCombineModeCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_GalleryUserTagFilterCombineModeCanonical[i];
            }
            if (string.Equals(v, "Any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "OR", StringComparison.OrdinalIgnoreCase))
                return "Compound";
            if (string.Equals(v, "All", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "AND", StringComparison.OrdinalIgnoreCase))
                return "Isolate";
            return "Compound";
        }

        /// <summary>True when multi-tag filter requires every selected tag (Isolate); false for Compound (any tag).</summary>
        public bool IsGalleryUserTagFilterIsolate()
        {
            return string.Equals(
                NormalizeGalleryUserTagFilterCombineMode(GalleryUserTagFilterCombineMode),
                "Isolate",
                StringComparison.OrdinalIgnoreCase);
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
        /// <summary>One-time migration: merged separate inner/side scale sliders into unified gallery UI scale.</summary>
        public bool GalleryUiScaleUnifiedMigrated = false;
        public float InnerPaneScaleVR
        {
            get { return ClampUiScale(_innerPaneScaleVR); }
            set
            {
                _innerPaneScaleVR = ClampUiScale(value);
                SideButtonScaleVR = InnerPaneScaleVR;
            }
        }
        public float InnerPaneScaleDesktop
        {
            get { return ClampUiScale(_innerPaneScaleDesktop); }
            set
            {
                _innerPaneScaleDesktop = ClampUiScale(value);
                SideButtonScaleDesktop = InnerPaneScaleDesktop;
            }
        }
        /// <summary>Unified gallery UI scale (inner chrome + side buttons). Side scale fields mirror this value.</summary>
        public float CurrentGalleryUiScale => CurrentInnerPaneScale;
        public float CurrentSideButtonScale => CurrentGalleryUiScale;
        public float CurrentInnerPaneScale => IsVR ? InnerPaneScaleVR : InnerPaneScaleDesktop;

        /// <summary>Effective rounded-corner fraction for gallery UI elements (0 when rounding is disabled).</summary>
        public float EffectiveGalleryElementCornerRadiusFraction()
        {
            if (!EnableGalleryElementRounding) return 0f;
            return ClampGalleryElementCornerRadiusFraction(GalleryElementCornerRadiusFraction);
        }

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
                return GalleryUiDesignTokens.VamUiScaleDesignBaseline;
            }
        }

        private void MigrateGalleryUiScaleUnified()
        {
            if (GalleryUiScaleUnifiedMigrated) return;
            try
            {
                float vr = ClampUiScale(Mathf.Sqrt(InnerPaneScaleVR * SideButtonScaleVR));
                float desk = ClampUiScale(Mathf.Sqrt(InnerPaneScaleDesktop * SideButtonScaleDesktop));
                _innerPaneScaleVR = vr;
                _innerPaneScaleDesktop = desk;
                SideButtonScaleVR = vr;
                SideButtonScaleDesktop = desk;
                SideButtonScale = IsVR ? vr : desk;
                GalleryUiScaleUnifiedMigrated = true;
            }
            catch { GalleryUiScaleUnifiedMigrated = true; }
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
        public static float ClampGalleryThumbPlaceholderSizeScale(float scale)
        {
            return Mathf.Clamp(scale, 0.25f, 2f);
        }

        public float GetGalleryThumbPlaceholderSizeScale()
        {
            return ClampGalleryThumbPlaceholderSizeScale(GalleryThumbPlaceholderSizeScale);
        }

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
            EnableGalleryElementRounding = true;
            GalleryElementCornerRadiusFraction = GalleryUiDesignTokens.ButtonCornerRadiusFraction;
            FirstRunHintsDismissed = false;
            VrHoverTooltipEnabled = true;
            ModeSetupWizardDoneDesktopFixed = false;
            ModeSetupWizardDoneDesktopFloating = false;
            ModeSetupWizardDoneVR = false;
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
            ImportSidebarPrefs = new JSONClass();
            SuppressCheesyFxNullReferenceLogs = true;
            BlockInGameMessages = "Off";
            HideMissingDependencyLogs = true;
            ClearInGameLogsOnSceneLaunch = false;
            EnableDragDrop = false;
            GalleryAutoGenderFilter = true;
            GalleryCollapseOnSceneLaunch = true;
            RequireDragHoldBeforeMove = false;
            DragHoldThreshold = 0.5f;
            ApplyMode = "DoubleClick";
            LastGalleryCategory = "";
            PerfModeEnabled = false;
            PerfStepIndex = 0;
            PerfStepScaleVersion = VpbPerfController.PerfStepScaleVersion;
            PerfBlend = 0f;
            PerfPresetMode = "None";
            PerfReapplyOnSceneLoad = false;
            PerfApplyHair = true;
            PerfApplyMirrors = true;
            PerfApplyRenderScale = false;
            PerfApplyMsaa = false;
            PerfApplyPixelLightCount = false;
            PerfApplySmoothPasses = false;
            PerfApplyMirrorReflections = false;
            PerfApplyRealtimeReflectionProbes = false;
            PerfApplySoftPhysics = false;
            PerfApplyGlowEffects = false;
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
            GalleryDefaultUserTagAvailMode = "FilterByTags";
            GalleryHideUnusedUserTagsInFilterMode = true;
            GalleryUserTagFilterCombineMode = "Compound";
            GalleryHideCreatorSideButtons = false;
            GalleryShowCategoryIcons = true;
            GalleryConsolidateCreatorNames = true;
            GalleryGridLabelsEnabled = true;
            GalleryGridLabelFontSize = 18f;
            GalleryGridLabelsAutoHideAtHighDensity = false;
            GalleryGridHoverBadgesEnabled = true;
            GalleryThumbPlaceholderLabelsEnabled = true;
            GalleryThumbPlaceholderSizeScale = 0.7f;
            PluginGalleryCategoryLabelsOnly = false;
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
            GalleryScanWlBorderEnabled = true;
            GalleryScanWlBorderShowInGrid = true;
            GalleryScanWlBorderShowInList = true;
            GalleryScanWlBorderWidth = 4f;
            GalleryScanWlGridFrameInset = 0f;
            GalleryScanWlListFrameInset = 2f;
            GalleryScanWlBorderOnThumbnail = true;
            GalleryScanWlBorderColorR = 0.2f;
            GalleryScanWlBorderColorG = 0.95f;
            GalleryScanWlBorderColorB = 1f;
            GalleryScanWlBorderColorA = 1f;
            GalleryScanWlTempBorderEnabled = true;
            GalleryScanWlTempBorderShowInGrid = true;
            GalleryScanWlTempBorderShowInList = true;
            GalleryScanWlTempBorderWidth = 4f;
            GalleryScanWlTempGridFrameInset = 0f;
            GalleryScanWlTempListFrameInset = 2f;
            GalleryScanWlTempBorderOnThumbnail = true;
            GalleryScanWlTempBorderColorR = 1f;
            GalleryScanWlTempBorderColorG = 0.15f;
            GalleryScanWlTempBorderColorB = 1f;
            GalleryScanWlTempBorderColorA = 1f;
            GalleryTboxToolbarPinned = false;
            UiLocale = "";
            SpringScrollButtonMode = "Desktop & VR";
            HoldToLaunchEnabled = false;
            TryOnModeEnabled = true;
            VerticalMoveKeysEnabled = true;
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
                        if (node["EnableGalleryElementRounding"] != null) EnableGalleryElementRounding = node["EnableGalleryElementRounding"].AsBool;
                        if (node["GalleryElementCornerRadiusFraction"] != null)
                            GalleryElementCornerRadiusFraction = ClampGalleryElementCornerRadiusFraction(node["GalleryElementCornerRadiusFraction"].AsFloat);
                        if (node["FirstRunHintsDismissed"] != null) FirstRunHintsDismissed = node["FirstRunHintsDismissed"].AsBool;
                        if (node["VrHoverTooltipEnabled"] != null) VrHoverTooltipEnabled = node["VrHoverTooltipEnabled"].AsBool;
                        if (node["ModeSetupWizardDoneDesktopFixed"] != null) ModeSetupWizardDoneDesktopFixed = node["ModeSetupWizardDoneDesktopFixed"].AsBool;
                        if (node["ModeSetupWizardDoneDesktopFloating"] != null) ModeSetupWizardDoneDesktopFloating = node["ModeSetupWizardDoneDesktopFloating"].AsBool;
                        if (node["ModeSetupWizardDoneVR"] != null) ModeSetupWizardDoneVR = node["ModeSetupWizardDoneVR"].AsBool;
                        if (cfgExistedAtStart
                            && node["ModeSetupWizardDoneDesktopFixed"] == null
                            && node["ModeSetupWizardDoneDesktopFloating"] == null
                            && node["ModeSetupWizardDoneVR"] == null)
                        {
                            ModeSetupWizardDoneDesktopFixed = true;
                            ModeSetupWizardDoneDesktopFloating = true;
                            ModeSetupWizardDoneVR = true;
                        }
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
                        if (node["ImportSidebarPrefs"] != null) ImportSidebarPrefs = node["ImportSidebarPrefs"].AsObject;
                        if (node["SuppressCheesyFxNullReferenceLogs"] != null) SuppressCheesyFxNullReferenceLogs = node["SuppressCheesyFxNullReferenceLogs"].AsBool;
                        if (node["BlockInGameMessages"] != null) BlockInGameMessages = node["BlockInGameMessages"].Value;
                        if (node["HideMissingDependencyLogs"] != null) HideMissingDependencyLogs = node["HideMissingDependencyLogs"].AsBool;
                        if (node["ClearInGameLogsOnSceneLaunch"] != null) ClearInGameLogsOnSceneLaunch = node["ClearInGameLogsOnSceneLaunch"].AsBool;
                        if (node["EnableDragDrop"] != null) EnableDragDrop = node["EnableDragDrop"].AsBool;
                        if (node["GalleryAutoGenderFilter"] != null) GalleryAutoGenderFilter = node["GalleryAutoGenderFilter"].AsBool;
                        if (node["GalleryCollapseOnSceneLaunch"] != null) GalleryCollapseOnSceneLaunch = node["GalleryCollapseOnSceneLaunch"].AsBool;
                        if (node["DragHoldThreshold"] != null)
                            DragHoldThreshold = ClampDragHoldThreshold(node["DragHoldThreshold"].AsFloat);
                        if (node["RequireDragHoldBeforeMove"] != null)
                            RequireDragHoldBeforeMove = node["RequireDragHoldBeforeMove"].AsBool;
                        if (node["ApplyMode"] != null) ApplyMode = node["ApplyMode"].Value;
                        if (node["LastGalleryCategory"] != null) LastGalleryCategory = node["LastGalleryCategory"].Value;
                        bool hadPerfModeKey = node["PerfModeEnabled"] != null;
                        bool hadPerfStepKey = node["PerfStepIndex"] != null;
                        bool hadPerfBlendKey = node["PerfBlend"] != null;
                        if (hadPerfModeKey) PerfModeEnabled = node["PerfModeEnabled"].AsBool;
                        if (hadPerfStepKey) PerfStepIndex = ClampPerfStepIndex(node["PerfStepIndex"].AsInt);
                        if (hadPerfBlendKey) PerfBlend = ClampPerfBlend(node["PerfBlend"].AsFloat);
                        if (node["PerfStepScaleVersion"] != null)
                            PerfStepScaleVersion = node["PerfStepScaleVersion"].AsInt;
                        if (node["PerfPresetMode"] != null) PerfPresetMode = node["PerfPresetMode"].Value;
                        if (node["PerfReapplyOnSceneLoad"] != null) PerfReapplyOnSceneLoad = node["PerfReapplyOnSceneLoad"].AsBool;
                        if (node["PerfApplyHair"] != null) PerfApplyHair = node["PerfApplyHair"].AsBool;
                        if (node["PerfApplyMirrors"] != null) PerfApplyMirrors = node["PerfApplyMirrors"].AsBool;
                        if (node["PerfApplyRenderScale"] != null) PerfApplyRenderScale = node["PerfApplyRenderScale"].AsBool;
                        if (node["PerfApplyMsaa"] != null) PerfApplyMsaa = node["PerfApplyMsaa"].AsBool;
                        if (node["PerfApplyPixelLightCount"] != null) PerfApplyPixelLightCount = node["PerfApplyPixelLightCount"].AsBool;
                        if (node["PerfApplySmoothPasses"] != null) PerfApplySmoothPasses = node["PerfApplySmoothPasses"].AsBool;
                        if (node["PerfApplyMirrorReflections"] != null) PerfApplyMirrorReflections = node["PerfApplyMirrorReflections"].AsBool;
                        if (node["PerfApplyRealtimeReflectionProbes"] != null) PerfApplyRealtimeReflectionProbes = node["PerfApplyRealtimeReflectionProbes"].AsBool;
                        if (node["PerfApplySoftPhysics"] != null) PerfApplySoftPhysics = node["PerfApplySoftPhysics"].AsBool;
                        if (node["PerfApplyGlowEffects"] != null) PerfApplyGlowEffects = node["PerfApplyGlowEffects"].AsBool;
                        if (!hadPerfStepKey && (hadPerfBlendKey || hadPerfModeKey || !string.IsNullOrEmpty(PerfPresetMode)))
                            MigrateLegacyPerfPresetFields(this, hadPerfModeKey);
                        else
                            PerfStepIndex = ClampPerfStepIndex(PerfStepIndex);
                        RemapPerfStepIndexIfScaleVersionChanged();
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
                        if (node["GalleryDefaultUserTagAvailMode"] != null)
                            GalleryDefaultUserTagAvailMode = NormalizeGalleryDefaultUserTagAvailMode(node["GalleryDefaultUserTagAvailMode"].Value);
                        if (node["GalleryHideUnusedUserTagsInFilterMode"] != null)
                            GalleryHideUnusedUserTagsInFilterMode = node["GalleryHideUnusedUserTagsInFilterMode"].AsBool;
                        if (node["GalleryUserTagFilterCombineMode"] != null)
                            GalleryUserTagFilterCombineMode = NormalizeGalleryUserTagFilterCombineMode(node["GalleryUserTagFilterCombineMode"].Value);
                        if (node["GalleryScrollButtonStepViewportFraction"] != null)
                            GalleryScrollButtonStepViewportFraction = Mathf.Clamp(node["GalleryScrollButtonStepViewportFraction"].AsFloat, 0.10f, 2.00f);
                        if (node["GalleryScrollButtonsEnabled"] != null)
                            GalleryScrollButtonsEnabled = node["GalleryScrollButtonsEnabled"].AsBool;
                        if (node["GalleryVrThumbstickScrollEnabled"] != null)
                            GalleryVrThumbstickScrollEnabled = node["GalleryVrThumbstickScrollEnabled"].AsBool;
                        if (node["GalleryHideCreatorSideButtons"] != null)
                            GalleryHideCreatorSideButtons = node["GalleryHideCreatorSideButtons"].AsBool;
                        if (node["GalleryShowCategoryIcons"] != null)
                            GalleryShowCategoryIcons = node["GalleryShowCategoryIcons"].AsBool;
                        if (node["GalleryConsolidateCreatorNames"] != null)
                            GalleryConsolidateCreatorNames = node["GalleryConsolidateCreatorNames"].AsBool;
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
                        if (node["PluginGalleryCategoryLabelsOnly"] != null) PluginGalleryCategoryLabelsOnly = node["PluginGalleryCategoryLabelsOnly"].AsBool;
                        if (node["GalleryThumbPlaceholderLabelsEnabled"] != null) GalleryThumbPlaceholderLabelsEnabled = node["GalleryThumbPlaceholderLabelsEnabled"].AsBool;
                        if (node["GalleryThumbPlaceholderSizeScale"] != null) GalleryThumbPlaceholderSizeScale = ClampGalleryThumbPlaceholderSizeScale(node["GalleryThumbPlaceholderSizeScale"].AsFloat);
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
                        if (node["GalleryGridHoverBadgesEnabled"] != null) GalleryGridHoverBadgesEnabled = node["GalleryGridHoverBadgesEnabled"].AsBool;
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
                        if (node["GalleryScanWlBorderEnabled"] != null) GalleryScanWlBorderEnabled = node["GalleryScanWlBorderEnabled"].AsBool;
                        if (node["GalleryScanWlBorderShowInGrid"] != null) GalleryScanWlBorderShowInGrid = node["GalleryScanWlBorderShowInGrid"].AsBool;
                        if (node["GalleryScanWlBorderShowInList"] != null) GalleryScanWlBorderShowInList = node["GalleryScanWlBorderShowInList"].AsBool;
                        if (node["GalleryScanWlBorderWidth"] != null) GalleryScanWlBorderWidth = Mathf.Clamp(node["GalleryScanWlBorderWidth"].AsFloat, 0f, 20f);
                        if (node["GalleryScanWlGridFrameInset"] != null) GalleryScanWlGridFrameInset = Mathf.Clamp(node["GalleryScanWlGridFrameInset"].AsFloat, 0f, 24f);
                        if (node["GalleryScanWlListFrameInset"] != null) GalleryScanWlListFrameInset = Mathf.Clamp(node["GalleryScanWlListFrameInset"].AsFloat, 0f, 24f);
                        if (node["GalleryScanWlBorderOnThumbnail"] != null) GalleryScanWlBorderOnThumbnail = node["GalleryScanWlBorderOnThumbnail"].AsBool;
                        if (node["GalleryScanWlBorderColorR"] != null) GalleryScanWlBorderColorR = Mathf.Clamp01(node["GalleryScanWlBorderColorR"].AsFloat);
                        if (node["GalleryScanWlBorderColorG"] != null) GalleryScanWlBorderColorG = Mathf.Clamp01(node["GalleryScanWlBorderColorG"].AsFloat);
                        if (node["GalleryScanWlBorderColorB"] != null) GalleryScanWlBorderColorB = Mathf.Clamp01(node["GalleryScanWlBorderColorB"].AsFloat);
                        if (node["GalleryScanWlBorderColorA"] != null) GalleryScanWlBorderColorA = Mathf.Clamp01(node["GalleryScanWlBorderColorA"].AsFloat);
                        if (node["GalleryScanWlTempBorderEnabled"] != null) GalleryScanWlTempBorderEnabled = node["GalleryScanWlTempBorderEnabled"].AsBool;
                        if (node["GalleryScanWlTempBorderShowInGrid"] != null) GalleryScanWlTempBorderShowInGrid = node["GalleryScanWlTempBorderShowInGrid"].AsBool;
                        if (node["GalleryScanWlTempBorderShowInList"] != null) GalleryScanWlTempBorderShowInList = node["GalleryScanWlTempBorderShowInList"].AsBool;
                        if (node["GalleryScanWlTempBorderWidth"] != null) GalleryScanWlTempBorderWidth = Mathf.Clamp(node["GalleryScanWlTempBorderWidth"].AsFloat, 0f, 20f);
                        if (node["GalleryScanWlTempGridFrameInset"] != null) GalleryScanWlTempGridFrameInset = Mathf.Clamp(node["GalleryScanWlTempGridFrameInset"].AsFloat, 0f, 24f);
                        if (node["GalleryScanWlTempListFrameInset"] != null) GalleryScanWlTempListFrameInset = Mathf.Clamp(node["GalleryScanWlTempListFrameInset"].AsFloat, 0f, 24f);
                        if (node["GalleryScanWlTempBorderOnThumbnail"] != null) GalleryScanWlTempBorderOnThumbnail = node["GalleryScanWlTempBorderOnThumbnail"].AsBool;
                        if (node["GalleryScanWlTempBorderColorR"] != null) GalleryScanWlTempBorderColorR = Mathf.Clamp01(node["GalleryScanWlTempBorderColorR"].AsFloat);
                        if (node["GalleryScanWlTempBorderColorG"] != null) GalleryScanWlTempBorderColorG = Mathf.Clamp01(node["GalleryScanWlTempBorderColorG"].AsFloat);
                        if (node["GalleryScanWlTempBorderColorB"] != null) GalleryScanWlTempBorderColorB = Mathf.Clamp01(node["GalleryScanWlTempBorderColorB"].AsFloat);
                        if (node["GalleryScanWlTempBorderColorA"] != null) GalleryScanWlTempBorderColorA = Mathf.Clamp01(node["GalleryScanWlTempBorderColorA"].AsFloat);
                        if (node["GalleryTboxToolbarPinned"] != null) GalleryTboxToolbarPinned = node["GalleryTboxToolbarPinned"].AsBool;
                        if (node["GalleryOnlyWhenVamMenuVisible"] != null) GalleryOnlyWhenVamMenuVisible = node["GalleryOnlyWhenVamMenuVisible"].AsBool;
                        if (node["GalleryAnchorToVamMenu"] != null) GalleryAnchorToVamMenu = node["GalleryAnchorToVamMenu"].AsBool;
                        if (node["GalleryAnchorOffset"] != null)
                        {
                            var o = node["GalleryAnchorOffset"];
                            GalleryAnchorOffset = new Vector3(
                                o["x"].AsFloat,
                                o["y"] != null ? o["y"].AsFloat : 0.1f,
                                o["z"] != null ? o["z"].AsFloat : -0.1f);
                        }
                        if (node["AnchorYieldsToVamPanels"] != null) AnchorYieldsToVamPanels = node["AnchorYieldsToVamPanels"].AsBool;
                        if (node["QuickMenuVrWatchVisible"] != null) QuickMenuVrWatchVisible = node["QuickMenuVrWatchVisible"].AsBool;
                        if (node["QuickMenuVrWatchMode"] != null) QuickMenuVrWatchMode = node["QuickMenuVrWatchMode"].Value;
                        if (node["QuickMenuVrWatchOnlyWithMenu"] != null) QuickMenuVrWatchOnlyWithMenu = node["QuickMenuVrWatchOnlyWithMenu"].AsBool;
                        if (node["QuickMenuVrWatchFaceUser"] != null) QuickMenuVrWatchFaceUser = node["QuickMenuVrWatchFaceUser"].AsBool;
                        if (node["QuickMenuVrWatchScale"] != null) QuickMenuVrWatchScale = Mathf.Clamp(node["QuickMenuVrWatchScale"].AsFloat, 0.0002f, 0.0015f);
                        if (node["QuickMenuVrWatchTowardUserDist"] != null) QuickMenuVrWatchTowardUserDist = Mathf.Clamp(node["QuickMenuVrWatchTowardUserDist"].AsFloat, -0.5f, 0.5f);
                        if (node["QuickMenuVrWatchOffset"] != null)
                        {
                            var w = node["QuickMenuVrWatchOffset"];
                            QuickMenuVrWatchOffset = new Vector3(
                                w["x"].AsFloat,
                                w["y"] != null ? w["y"].AsFloat : 0.05f,
                                w["z"] != null ? w["z"].AsFloat : 0.04f);
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
                        if (node["GalleryUiScaleUnifiedMigrated"] != null) GalleryUiScaleUnifiedMigrated = node["GalleryUiScaleUnifiedMigrated"].AsBool;
                        MigrateGalleryUiScaleUnified();
                        if (node["SpringScrollButtonMode"] != null)
                            SpringScrollButtonMode = NormalizeSpringScrollButtonMode(node["SpringScrollButtonMode"].Value);
                        else if (node["SpringScrollButtonEnabled"] != null)
                            SpringScrollButtonMode = node["SpringScrollButtonEnabled"].AsBool ? "Desktop & VR" : "Off";
                        if (node["HoldToLaunchEnabled"] != null) HoldToLaunchEnabled = node["HoldToLaunchEnabled"].AsBool;
                        if (node["TryOnModeEnabled"] != null) TryOnModeEnabled = node["TryOnModeEnabled"].AsBool;
                        if (node["VerticalMoveKeysEnabled"] != null) VerticalMoveKeysEnabled = node["VerticalMoveKeysEnabled"].AsBool;
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
                    // Restore user intent (EnableDragDrop) from HoldToLaunchPrevEnableDragDrop.
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
                node["EnableGalleryElementRounding"].AsBool = EnableGalleryElementRounding;
                node["GalleryElementCornerRadiusFraction"].AsFloat = ClampGalleryElementCornerRadiusFraction(GalleryElementCornerRadiusFraction);
                node["FirstRunHintsDismissed"].AsBool = FirstRunHintsDismissed;
                node["VrHoverTooltipEnabled"].AsBool = VrHoverTooltipEnabled;
                node["ModeSetupWizardDoneDesktopFixed"].AsBool = ModeSetupWizardDoneDesktopFixed;
                node["ModeSetupWizardDoneDesktopFloating"].AsBool = ModeSetupWizardDoneDesktopFloating;
                node["ModeSetupWizardDoneVR"].AsBool = ModeSetupWizardDoneVR;
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
                if (ImportSidebarPrefs != null) node["ImportSidebarPrefs"] = ImportSidebarPrefs;
                node["SuppressCheesyFxNullReferenceLogs"].AsBool = SuppressCheesyFxNullReferenceLogs;
                node["BlockInGameMessages"] = BlockInGameMessages;
                node["HideMissingDependencyLogs"].AsBool = HideMissingDependencyLogs;
                node["ClearInGameLogsOnSceneLaunch"].AsBool = ClearInGameLogsOnSceneLaunch;
                node["KeepClothingWhenApplyingAppearance"].AsBool = KeepClothingWhenApplyingAppearance;
                node["EnableDragDrop"].AsBool = EnableDragDrop;
                node["GalleryAutoGenderFilter"].AsBool = GalleryAutoGenderFilter;
                node["GalleryCollapseOnSceneLaunch"].AsBool = GalleryCollapseOnSceneLaunch;
                NormalizeDragDropHoldSettings();
                node["RequireDragHoldBeforeMove"].AsBool = RequireDragHoldBeforeMove;
                node["DragHoldThreshold"].AsFloat = DragHoldThreshold;
                node["ApplyMode"] = ApplyMode;
                node["LastGalleryCategory"] = LastGalleryCategory;
                PerfStepIndex = ClampPerfStepIndex(PerfStepIndex);
                PerfStepScaleVersion = VpbPerfController.PerfStepScaleVersion;
                int perfMax = PerfStepMaxIndex();
                PerfBlend = perfMax > 0 ? (float)PerfStepIndex / (float)perfMax : 0f;
                node["PerfModeEnabled"].AsBool = PerfModeEnabled;
                node["PerfStepIndex"].AsInt = PerfStepIndex;
                node["PerfStepScaleVersion"].AsInt = PerfStepScaleVersion;
                node["PerfBlend"].AsFloat = PerfBlend;
                node["PerfPresetMode"] = PerfModeEnabled ? "On" : "None";
                node["PerfReapplyOnSceneLoad"].AsBool = PerfReapplyOnSceneLoad;
                node["PerfApplyHair"].AsBool = PerfApplyHair;
                node["PerfApplyMirrors"].AsBool = PerfApplyMirrors;
                node["PerfApplyRenderScale"].AsBool = PerfApplyRenderScale;
                node["PerfApplyMsaa"].AsBool = PerfApplyMsaa;
                node["PerfApplyPixelLightCount"].AsBool = PerfApplyPixelLightCount;
                node["PerfApplySmoothPasses"].AsBool = PerfApplySmoothPasses;
                node["PerfApplyMirrorReflections"].AsBool = PerfApplyMirrorReflections;
                node["PerfApplyRealtimeReflectionProbes"].AsBool = PerfApplyRealtimeReflectionProbes;
                node["PerfApplySoftPhysics"].AsBool = PerfApplySoftPhysics;
                node["PerfApplyGlowEffects"].AsBool = PerfApplyGlowEffects;
                node["InitialGalleryCategory"] = InitialGalleryCategory;
                node["global_source_filter"] = GlobalSourceFilter.ToString();
                node["GalleryDefaultLeftSidePanel"] = GalleryDefaultLeftSidePanel;
                node["GalleryDefaultRightSidePanel"] = GalleryDefaultRightSidePanel;
                node["GalleryDefaultUserTagAvailMode"] = NormalizeGalleryDefaultUserTagAvailMode(GalleryDefaultUserTagAvailMode);
                node["GalleryHideUnusedUserTagsInFilterMode"].AsBool = GalleryHideUnusedUserTagsInFilterMode;
                node["GalleryUserTagFilterCombineMode"] = NormalizeGalleryUserTagFilterCombineMode(GalleryUserTagFilterCombineMode);
                node["GalleryScrollButtonStepViewportFraction"].AsFloat = Mathf.Clamp(GalleryScrollButtonStepViewportFraction, 0.10f, 2.00f);
                node["GalleryScrollButtonsEnabled"].AsBool = GalleryScrollButtonsEnabled;
                node["GalleryVrThumbstickScrollEnabled"].AsBool = GalleryVrThumbstickScrollEnabled;
                node["GalleryHideCreatorSideButtons"].AsBool = GalleryHideCreatorSideButtons;
                node["GalleryShowCategoryIcons"].AsBool = GalleryShowCategoryIcons;
                node["GalleryConsolidateCreatorNames"].AsBool = GalleryConsolidateCreatorNames;
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
                node["PluginGalleryCategoryLabelsOnly"].AsBool = PluginGalleryCategoryLabelsOnly;
                node["GalleryThumbPlaceholderLabelsEnabled"].AsBool = GalleryThumbPlaceholderLabelsEnabled;
                node["GalleryThumbPlaceholderSizeScale"].AsFloat = GetGalleryThumbPlaceholderSizeScale();
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
                node["GalleryGridHoverBadgesEnabled"].AsBool = GalleryGridHoverBadgesEnabled;
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
                node["GalleryScanWlBorderEnabled"].AsBool = GalleryScanWlBorderEnabled;
                node["GalleryScanWlBorderShowInGrid"].AsBool = GalleryScanWlBorderShowInGrid;
                node["GalleryScanWlBorderShowInList"].AsBool = GalleryScanWlBorderShowInList;
                node["GalleryScanWlBorderWidth"].AsFloat = Mathf.Clamp(GalleryScanWlBorderWidth, 0f, 20f);
                node["GalleryScanWlGridFrameInset"].AsFloat = Mathf.Clamp(GalleryScanWlGridFrameInset, 0f, 24f);
                node["GalleryScanWlListFrameInset"].AsFloat = Mathf.Clamp(GalleryScanWlListFrameInset, 0f, 24f);
                node["GalleryScanWlBorderOnThumbnail"].AsBool = GalleryScanWlBorderOnThumbnail;
                node["GalleryScanWlBorderColorR"].AsFloat = Mathf.Clamp01(GalleryScanWlBorderColorR);
                node["GalleryScanWlBorderColorG"].AsFloat = Mathf.Clamp01(GalleryScanWlBorderColorG);
                node["GalleryScanWlBorderColorB"].AsFloat = Mathf.Clamp01(GalleryScanWlBorderColorB);
                node["GalleryScanWlBorderColorA"].AsFloat = Mathf.Clamp01(GalleryScanWlBorderColorA);
                node["GalleryScanWlTempBorderEnabled"].AsBool = GalleryScanWlTempBorderEnabled;
                node["GalleryScanWlTempBorderShowInGrid"].AsBool = GalleryScanWlTempBorderShowInGrid;
                node["GalleryScanWlTempBorderShowInList"].AsBool = GalleryScanWlTempBorderShowInList;
                node["GalleryScanWlTempBorderWidth"].AsFloat = Mathf.Clamp(GalleryScanWlTempBorderWidth, 0f, 20f);
                node["GalleryScanWlTempGridFrameInset"].AsFloat = Mathf.Clamp(GalleryScanWlTempGridFrameInset, 0f, 24f);
                node["GalleryScanWlTempListFrameInset"].AsFloat = Mathf.Clamp(GalleryScanWlTempListFrameInset, 0f, 24f);
                node["GalleryScanWlTempBorderOnThumbnail"].AsBool = GalleryScanWlTempBorderOnThumbnail;
                node["GalleryScanWlTempBorderColorR"].AsFloat = Mathf.Clamp01(GalleryScanWlTempBorderColorR);
                node["GalleryScanWlTempBorderColorG"].AsFloat = Mathf.Clamp01(GalleryScanWlTempBorderColorG);
                node["GalleryScanWlTempBorderColorB"].AsFloat = Mathf.Clamp01(GalleryScanWlTempBorderColorB);
                node["GalleryScanWlTempBorderColorA"].AsFloat = Mathf.Clamp01(GalleryScanWlTempBorderColorA);
                node["GalleryTboxToolbarPinned"].AsBool = GalleryTboxToolbarPinned;
                node["GalleryOnlyWhenVamMenuVisible"].AsBool = GalleryOnlyWhenVamMenuVisible;
                node["GalleryAnchorToVamMenu"].AsBool = GalleryAnchorToVamMenu;
                JSONClass o = new JSONClass();
                o["x"].AsFloat = GalleryAnchorOffset.x;
                o["y"].AsFloat = GalleryAnchorOffset.y;
                o["z"].AsFloat = GalleryAnchorOffset.z;
                node["GalleryAnchorOffset"] = o;
                node["AnchorYieldsToVamPanels"].AsBool = AnchorYieldsToVamPanels;
                node["QuickMenuVrWatchVisible"].AsBool = QuickMenuVrWatchVisible;
                node["QuickMenuVrWatchMode"] = QuickMenuVrWatchMode;
                node["QuickMenuVrWatchOnlyWithMenu"].AsBool = QuickMenuVrWatchOnlyWithMenu;
                node["QuickMenuVrWatchFaceUser"].AsBool = QuickMenuVrWatchFaceUser;
                node["QuickMenuVrWatchScale"].AsFloat = QuickMenuVrWatchScale;
                node["QuickMenuVrWatchTowardUserDist"].AsFloat = QuickMenuVrWatchTowardUserDist;
                JSONClass w = new JSONClass();
                w["x"].AsFloat = QuickMenuVrWatchOffset.x;
                w["y"].AsFloat = QuickMenuVrWatchOffset.y;
                w["z"].AsFloat = QuickMenuVrWatchOffset.z;
                node["QuickMenuVrWatchOffset"] = w;
                node["SideButtonScale"].AsFloat = SideButtonScale;
                node["SideButtonScaleVR"].AsFloat = SideButtonScaleVR;
                node["SideButtonScaleDesktop"].AsFloat = SideButtonScaleDesktop;
                node["InnerPaneScale"].AsFloat = InnerPaneScale;
                node["InnerPaneScaleVR"].AsFloat = InnerPaneScaleVR;
                node["InnerPaneScaleDesktop"].AsFloat = InnerPaneScaleDesktop;
                node["GalleryUiScaleUnifiedMigrated"].AsBool = GalleryUiScaleUnifiedMigrated;
                node["SpringScrollButtonMode"] = NormalizeSpringScrollButtonMode(SpringScrollButtonMode);
                node["HoldToLaunchEnabled"].AsBool = HoldToLaunchEnabled;
                node["TryOnModeEnabled"].AsBool = TryOnModeEnabled;
                node["VerticalMoveKeysEnabled"].AsBool = VerticalMoveKeysEnabled;
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

                // tmp + verify + backup-rotate + move so a crash mid-write can't truncate the live config.
                string tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, jsonOutput);

                if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length < 2)
                {
                    VPBLogger.Config.LogError("[VPB] Failed to write temporary config file, aborting save.");
                    return;
                }

                string backupPath = path + ".bak";
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 2)
                    {
                        try
                        {
                            if (File.Exists(backupPath)) File.Delete(backupPath);
                            File.Move(path, backupPath);
                        }
                        catch (Exception bakEx)
                        {
                            VPBLogger.Config.LogWarning("[VPB] Failed to rotate config backup: " + bakEx.Message);
                            if (File.Exists(path)) File.Delete(path);
                        }
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                File.Move(tmpPath, path);
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

        private static readonly string[] s_SpringScrollButtonModeCanonical =
            { "Off", "Desktop Only", "VR Only", "Desktop & VR" };

        public static string NormalizeSpringScrollButtonMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Desktop & VR";
            string v = value.Trim();
            for (int i = 0; i < s_SpringScrollButtonModeCanonical.Length; i++)
            {
                if (string.Equals(v, s_SpringScrollButtonModeCanonical[i], StringComparison.OrdinalIgnoreCase))
                    return s_SpringScrollButtonModeCanonical[i];
            }
            // Legacy aliases
            if (string.Equals(v, "Both", StringComparison.OrdinalIgnoreCase)) return "Desktop & VR";
            if (string.Equals(v, "Desktop", StringComparison.OrdinalIgnoreCase)) return "Desktop Only";
            if (string.Equals(v, "VR", StringComparison.OrdinalIgnoreCase)) return "VR Only";
            return "Desktop & VR";
        }

        /// <summary>True when spring-scroll drag button should show for current desktop/VR context.</summary>
        public bool IsSpringScrollButtonEnabled()
        {
            string mode = NormalizeSpringScrollButtonMode(SpringScrollButtonMode);
            if (mode == "Off") return false;
            if (mode == "Desktop & VR") return true;
            bool isVR = IsVR;
            if (mode == "VR Only") return isVR;
            if (mode == "Desktop Only") return !isVR;
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
