using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
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

        public static float ClampUiScalePublic(float v) => ClampUiScale(v);

        public static float ClampOutlinerWidth(float v)
        {
            float min = GalleryUiDesignTokens.OutlinerRailMinWidthRef;
            float max = GalleryUiDesignTokens.OutlinerRailMaxWidthRef;
            if (float.IsNaN(v) || float.IsInfinity(v)) return GalleryUiDesignTokens.OutlinerRailWidthRef;
            return Mathf.Clamp(v, min, max);
        }

        public static float ClampOutlinerMoveStep(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) return 0.1f;
            return Mathf.Clamp(v, 0.001f, 10f);
        }

        public static float ClampOutlinerRotateStep(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) return 15f;
            return Mathf.Clamp(v, 0.1f, 180f);
        }

        public static float ClampOutlinerTargetsPulse(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return 3f;
            return Mathf.Clamp(v, 0f, 30f);
        }

        public static float ClampOutlinerFloatWidth(float v)
        {
            float min = GalleryUiDesignTokens.OutlinerFloatMinWidthRef;
            float max = GalleryUiDesignTokens.OutlinerFloatMaxWidthRef;
            if (float.IsNaN(v) || float.IsInfinity(v)) return GalleryUiDesignTokens.OutlinerFloatDefaultWidthRef;
            return Mathf.Clamp(v, min, max);
        }

        public static float ClampOutlinerFloatHeight(float v)
        {
            float min = GalleryUiDesignTokens.OutlinerFloatMinHeightRef;
            float max = GalleryUiDesignTokens.OutlinerFloatMaxHeightRef;
            if (float.IsNaN(v) || float.IsInfinity(v)) return GalleryUiDesignTokens.OutlinerFloatDefaultHeightRef;
            return Mathf.Clamp(v, min, max);
        }

        public static int ClampRatingPresenceFilterMode(int v)
        {
            if (v < 0) return 0;
            if (v > 2) return 0;
            return v;
        }

        public bool HasDefaultFilterPreset
        {
            get { return GalleryDefaultFilterPresetId > 0 || !string.IsNullOrEmpty(GalleryDefaultFilterPresetName); }
        }

        public static float ClampGalleryElementCornerRadiusFraction(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = GalleryUiDesignTokens.ButtonCornerRadiusFraction;
            return Mathf.Clamp(v, MinGalleryElementCornerRadiusFraction, MaxGalleryElementCornerRadiusFraction);
        }

        private static VPBConfig _instance;
        private static string s_LastLoggedSavedGalleryCategory;
        private static string s_LastLoggedLoadedGalleryCategory;

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
                string path = GlobalInfo.PluginFile("VPB.cfg");
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
                return GlobalInfo.PluginFile("VPB.cfg");
            }
        }

        public bool EnableButtonGaps = true;
        public bool EnableGalleryElementRounding = true;
        /// <summary>Idle + selected button rims on muted chrome. Off = fill-only (hover rim stays).</summary>
        public bool EnableGalleryButtonChromeRims = true;
        public float GalleryElementCornerRadiusFraction = GalleryUiDesignTokens.ButtonCornerRadiusFraction;
        public bool VrHoverTooltipEnabled = true;
        public string ShowSideButtons = "Auto";
        public string LastGallerySideRailEdge = "Right";
        public string _followAngle = "Both";
        public string FollowAngle
        {
            get { return _followAngle; }
            set { _followAngle = value; }
        }
        public string _followDistance = "VR";
        public string FollowDistance
        {
            get { return _followDistance; }
            set { _followDistance = value; }
        }
        public string _followEyeHeight = "VR";
        public string FollowEyeHeight
        {
            get { return _followEyeHeight; }
            set { _followEyeHeight = value; }
        }
        public float BringToFrontDistance = 1.5f;
        public float ReorientStartAngle = 20f;
        public float MovementThreshold = 0.1f;
        public bool DisableGalleryTransparency = true;
        public bool DisableGalleryPaneTransparency = false;
        public bool DisableGalleryAssignableButtonsTransparency = false;
        public bool DisableGalleryDockHoverTransparency = false;
        public bool EnableGalleryFade = true;
        public bool EnableGalleryTranslucency = false;
        /// <summary>When true, package scans do not update the gallery until the user uses Refresh.</summary>
        public bool GalleryManualRefreshOnly = true;
        public float GalleryOpacity = 1.0f;
        public bool DragDropReplaceMode = false;
        public bool ClothingReplaceUseGeometry = true;
        public int ClothingReplaceStrictness = 2;
        public bool ClothingReplaceStrictnessUpgraded = true;
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
        public bool KeepClothingWhenApplyingAppearance
        {
            get { return string.Equals(AppearanceClothingApplyMode, "keep", StringComparison.OrdinalIgnoreCase); }
            set { AppearanceClothingApplyMode = value ? "keep" : "replace"; }
        }
        public bool SuppressAppearanceScaleChange = false;
        /// <summary>Persisted import-sidebar state (open, onLeft, suppress-clothing, only-suppress-real, sub-toggles, last type). See GalleryPanel.ImportSidebar.cs Load/SaveImportSidebarPrefs.</summary>
        public JSONClass ImportSidebarPrefs = new JSONClass();
        /// <summary>When true, suppresses CheesyFX NullReferenceException spam in Unity/BepInEx logs (broken Update loops).</summary>
        public bool SuppressCheesyFxNullReferenceLogs = true;
        /// <summary>Controls when in-game VaM notification messages (errors and warnings) are suppressed. "Off" = never; "VR Only" = suppressed in VR; "Desktop Only" = suppressed on desktop; "Both" = always suppressed.</summary>
        public string BlockInGameMessages = "Off";
        /// <summary>When true, suppress VaM "Missing addon package … depends on …" spam in Unity/BepInEx and in-game error log.</summary>
        public bool HideMissingDependencyLogs = true;
        public bool ClearInGameLogsOnSceneLaunch = false;
        public bool EnableDragDrop = false;
        public bool GalleryAutoGenderFilter = true;
        public bool GalleryCollapseOnSceneLaunch = true;
        public bool GalleryRememberRatingFilter = true;
        public int GalleryLastRatingPresenceFilterMode = 0;
        public bool GalleryApplyDefaultFilterPresetOnStart = true;
        public int GalleryDefaultFilterPresetId = 0;
        public string GalleryDefaultFilterPresetName = "";
        public bool EffectiveEnableDragDrop
        {
            get { return EnableDragDrop && !HoldToLaunchEnabled; }
        }
        /// <summary>Legacy persisted flag. Desktop DnD uses movement threshold only; VR still uses <see cref="DragHoldThreshold"/>. Serialized for forward compatibility.</summary>
        public bool RequireDragHoldBeforeMove = false;
        /// <summary>VR only: minimum seconds held before gallery item drag can start. Desktop ignores this (click-drag after pixel slack).</summary>
        public const float DragHoldThresholdMin = 0.4f;
        public float DragHoldThreshold = 0.5f;

        public static float ClampDragHoldThreshold(float seconds) =>
            Mathf.Clamp(seconds, DragHoldThresholdMin, 1f);

        /// <summary>Clamp hold threshold; keep legacy RequireDragHoldBeforeMove in sync when DnD enabled.</summary>
        public void NormalizeDragDropHoldSettings()
        {
            DragHoldThreshold = ClampDragHoldThreshold(DragHoldThreshold);
            if (EnableDragDrop)
                RequireDragHoldBeforeMove = true;
        }
        public string ApplyMode = "DoubleClick";
        public string LastContextSceneAction = "";
        public string LastContextAppearanceAction = "";
        public string LastGalleryCategory = "";
        public bool PerfModeEnabled = false;
        public int PerfStepIndex = 0;
        public int PerfStepScaleVersion = VpbPerfController.PerfStepScaleVersion;
        /// <summary>Legacy 0–1 blend; used only to migrate old configs to PerfStepIndex.</summary>
        public float PerfBlend = 0f;
        public string PerfPresetMode = "None";
        /// <summary>Obsolete: perf always re-applies on scene load while On. Kept for config compat only.</summary>
        public bool PerfReapplyOnSceneLoad = false;

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

        public const int SceneImportCacheLimitMbDefault = 1024;
        public const int SceneImportCacheLimitMbMin = 256;
        public const int SceneImportCacheLimitMbMax = 8192;
        public int SceneImportCacheLimitMb = SceneImportCacheLimitMbDefault;

        public static int ClampSceneImportCacheLimitMb(int value)
        {
            return Mathf.Clamp(value, SceneImportCacheLimitMbMin, SceneImportCacheLimitMbMax);
        }

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
        /// <summary>Category on cold VaM launch only: "Scenes" (default), "Clothing", "Hair", "Pose", "Appearance", "Plugins", or "LastUsed". In-session Close/reopen uses <see cref="LastGalleryCategory"/>.</summary>
        public string InitialGalleryCategory = "Scenes";
        /// <summary>Global source filter for gallery: All (default), Local (loose files only), or Var (.var packages only). Last-used live value; per-category memory lives in CategoryFilterState when <see cref="GallerySourceFilterIndependent"/>.</summary>
        public GlobalSourceFilterValue GlobalSourceFilter = GlobalSourceFilterValue.All;
        public bool GallerySourceFilterIndependent = true;

        private static readonly string[] s_InitialGalleryCategoryCanonical = { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" };

        public bool PluginGalleryGridThumbnails = true;
        public bool PluginGalleryCategoryLabelsOnly = false;
        public bool GalleryThumbPlaceholderLabelsEnabled = true;
        public float GalleryThumbPlaceholderSizeScale = 0.7f;
        public bool GalleryListNamesLegacyFileName = false;
        public bool GalleryPrettyPresetNames = true;
        public bool GalleryRandomPrefersSimilar = false;
        public string GallerySearchScope = "PathAndName";
        public string GalleryHoverPreviewMode = "List";
        public float GalleryListHoverPreviewSize = 300f;
        public const float GalleryHoverPreviewSizeMin = 200f;
        public const float GalleryHoverPreviewSizeMax = 1200f;
        public float GalleryListHoverPreviewOffsetX = 0f;
        public float GalleryListHoverPreviewOffsetY = 0f;
        /// <summary>When true, each grid cell shows a persistent label strip below the thumbnail with Creator.Package.Version. Grid mode only.</summary>
        public bool GalleryGridLabelsEnabled = true;
        public float GalleryGridLabelFontSize = 18f;
        public bool GalleryGridLabelsAutoHideAtHighDensity = true;
        public bool GalleryGridHoverBadgesEnabled = true;
        public bool GalleryDepStatusBadgeEnabled = true;
        public float GalleryGridSpacingX = 0f;
        public float GalleryGridSpacingY = 0f;
        public float GalleryGridThumbnailPadding = 0f;
        /// <summary>Grid: hover border width (pixels). Implemented via Outline effectDistance.</summary>
        public float GalleryGridHoverBorderWidth = 1f;
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

        public bool GalleryScanWlBadgePrimaryV1 = true;

        public bool GalleryScanWlBorderEnabled = false;
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

        public bool GalleryScanWlTempBorderEnabled = false;
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

        public bool PassthroughEnabled = false;
        public float PassthroughKeyColorR = 0f;
        public float PassthroughKeyColorG = 30f / 255f;
        public float PassthroughKeyColorB = 60f / 255f;
        public bool PassthroughLightsEnabled = false;
        public bool PassthroughLightsOverrideScene = true;
        public string PassthroughLightsSpec = "";
        public int PassthroughLightCount = 1;
        public bool PassthroughLightsMoveAsGroup = false;
        public string PassthroughLightPresetsJson = "[]";
        public string PassthroughLightPresetSelected = "";
        public bool PassthroughNavGrabRepaired = false;
        public bool PassthroughKeyCustom = false;
        public bool PassthroughCleanKey = true;
        public bool PassthroughExactColor = true;
        public bool PassthroughHardEdges = true;
        public string PassthroughHideScene = VpbPassthrough.HideAllButPeople;

        public Color GetPassthroughKeyColor()
        {
            return new Color(
                Mathf.Clamp01(PassthroughKeyColorR),
                Mathf.Clamp01(PassthroughKeyColorG),
                Mathf.Clamp01(PassthroughKeyColorB),
                1f);
        }

        public void SetPassthroughKeyColor(Color c)
        {
            PassthroughKeyColorR = Mathf.Clamp01(c.r);
            PassthroughKeyColorG = Mathf.Clamp01(c.g);
            PassthroughKeyColorB = Mathf.Clamp01(c.b);
            try { VpbPassthrough.NotifySettingsChanged(); } catch { }
            try { TriggerChange(); } catch { }
        }

        public static string NormalizePassthroughHideScene(string value)
        {
            if (string.Equals(value, VpbPassthrough.HideNothing, StringComparison.OrdinalIgnoreCase)) return VpbPassthrough.HideNothing;
            if (string.Equals(value, VpbPassthrough.HideEnvironment, StringComparison.OrdinalIgnoreCase)) return VpbPassthrough.HideEnvironment;
            return VpbPassthrough.HideAllButPeople;
        }

        public static int NormalizePassthroughLightCount(int n)
        {
            if (n < 1) return 1;
            if (n > VpbPassthroughLights.MaxLights) return VpbPassthroughLights.MaxLights;
            return n;
        }

        public int CreatorStripKeepMask = (int)SceneUtils.CreatorStripKeepDefault;

        public string CreatorStripRecipesJson = "[]";

        public string CreatorStripLastRecipeJson = "";

        public readonly FloatGeometryPair CreatorStripPanelGeometry =
            new FloatGeometryPair("CreatorStripPanel", 560f, 640f);

        public bool CreatorStripRemovePossessable = true;
        public bool CreatorStripAddPossessableMale = true;
        public bool CreatorStripAddPossessableFemale = false;
        public int CreatorStripPersonRenameMode = 0;

        public string CreatorStripDefaultSubScenePath = "";

        public int CreatorStripCreateFillMode = 1;

        /// <summary>Legacy (unused): old pin-toolbox pref. Kept for VPB.cfg read/write compat only.</summary>
        public bool GalleryTboxToolbarPinned = false;
        public bool GalleryDetailStripExpanded = true;
        public bool GalleryDetailStripSideInfoEnabled = true;
        public bool GalleryDetailStripThumbOnRight = false;
        public float GalleryDetailStripHeightRef = 0f;
        public readonly FloatGeometryPair GalleryDetailStripTagMenuGeometry =
            new FloatGeometryPair("GalleryDetailStripTagMenu", 0f, 0f);
        public bool GalleryQuickFiltersDetached = false;
        public readonly FloatGeometryPair GalleryQuickFiltersGeometry =
            new FloatGeometryPair("GalleryQuickFilters", 280f, 420f);
        public bool GalleryImportSidebarDetached = false;
        public readonly FloatGeometryPair GalleryImportSidebarGeometry =
            new FloatGeometryPair("GalleryImportSidebar", 360f, 560f);
        public readonly FloatGeometryPair GalleryRemapAtomUidsGeometry =
            new FloatGeometryPair("GalleryRemapAtomUids", 680f, 460f);
        public readonly FloatGeometryPair GallerySettingsFloatGeometry =
            new FloatGeometryPair("GallerySettingsFloat", 680f, 640f);
        /// <summary>Last Settings category key (appearance, browsing, …). Never "all".</summary>
        public string GallerySettingsLastGroup = "appearance";
        public readonly FloatGeometryPair GalleryPluginsFloatGeometry =
            new FloatGeometryPair("GalleryPluginsFloat", 460f, 560f);
        public readonly FloatGeometryPair GalleryInsightsFloatGeometry =
            new FloatGeometryPair("GalleryInsightsFloat", 620f, 640f);
        public readonly FloatGeometryPair GalleryOutlinerFloatGeometry =
            new FloatGeometryPair("GalleryOutlinerFloat", 980f, 720f);
        public readonly FloatGeometryPair GalleryLayoutPresetsFloatGeometry =
            new FloatGeometryPair("GalleryLayoutPresetsFloat", 380f, 460f);

        public readonly FloatGeometryPair QuickMenuAssignFloatGeometry =
            new FloatGeometryPair("QuickMenuAssignFloat", 360f, 480f);
        /// <summary>Plugins float: show only highest integer version per Author.Name package group.</summary>
        public bool GalleryPluginsFloatLatestOnly = false;
        public bool GalleryPluginsFloatCslistOnly = false;
        /// <summary>When true, gallery pane only shows while the VaM menu (main HUD) is visible.</summary>
        public bool GalleryOnlyWhenVamMenuVisible = false;
        public bool GalleryFloatsOnlyMode = false;
        public bool GalleryAnchorToVamMenu = true;
        /// <summary>VR menu-anchor only: gallery pane pitch (deg). Pivot bottom edge; top tips toward user. 0..20.</summary>
        public float GalleryVrMenuAnchorTiltDeg = 10f;
        public const float MinGalleryVrMenuAnchorTiltDeg = 0f;
        public const float MaxGalleryVrMenuAnchorTiltDeg = 20f;
        public static float ClampGalleryVrMenuAnchorTiltDeg(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return MinGalleryVrMenuAnchorTiltDeg;
            return Mathf.Clamp(v, MinGalleryVrMenuAnchorTiltDeg, MaxGalleryVrMenuAnchorTiltDeg);
        }
        public Vector3 GalleryAnchorOffset = new Vector3(0f, 0.1f, -0.1f);
        /// <summary>When anchored to VaM menu, hide the gallery if a full-screen VaM panel becomes active (Settings, Hub, package managers) so they never overlap.</summary>
        public bool AnchorYieldsToVamPanels = true;

        public static readonly Vector3 QuickMenuVrWatchOffsetDefault = new Vector3(0.045f, -0.012f, 0.03f);
        public const float QuickMenuVrWatchTowardDefault = 0.04f;
        public const float QuickMenuVrWatchScaleMulDefault = 0.75f;
        public const float QuickMenuVrWatchScaleMulMin = 0.5f;
        public const float QuickMenuVrWatchScaleMulMax = 1.5f;
        /// <summary>Per-axis limit of the wrist-locked rotation trim, in degrees.</summary>
        public const float QuickMenuVrWatchFaceRotationMaxDeg = 180f;
        public static readonly Vector3 QuickMenuVrWatchFaceRotationDefault = new Vector3(-60f, 0f, 0f);
        public const float QuickMenuVrWatchGlanceDwellDefault = 0.18f;
        public const float QuickMenuVrWatchGlanceDwellMax = 1f;
        public const float QuickMenuVrWatchShoulderBlendDefault = 0f;
        public const int QuickMenuVrWatchExtraSlotCount = 4;
        public const int QuickMenuVrWatchAssignSlotCount = 8;
        public const int QuickMenuVrWatchPageCount = 10;

        public bool QuickMenuVrWatchVisible = true;
        /// <summary>Which hand: "Left only" / "Right only" / "Opposite to menu" / "Same hand". No Off.</summary>
        public string QuickMenuVrWatchMode = "Opposite to menu";
        public string QuickMenuVrWatchShowWhen = "Glance";
        public bool QuickMenuVrWatchOnlyWithMenu = true;
        public bool QuickMenuVrWatchRememberHand = false;
        public bool QuickMenuVrWatchFaceUser = true;
        /// <summary>Visual size multiplier. 1.0 = HUD meters-per-pixel after worldScale compensate.</summary>
        public float QuickMenuVrWatchScaleMul = QuickMenuVrWatchScaleMulDefault;
        public float QuickMenuVrWatchScale = 0.001f;
        public float QuickMenuVrWatchTowardUserDist = QuickMenuVrWatchTowardDefault;
        public Vector3 QuickMenuVrWatchOffset = QuickMenuVrWatchOffsetDefault;
        public Vector3 QuickMenuVrWatchFaceRotation = QuickMenuVrWatchFaceRotationDefault;
        /// <summary>True after the first-run wrist-watch cue has finished.</summary>
        public bool QuickMenuVrWatchOnboardingSeen = false;
        public string[] QuickMenuVrWatchExtraActions;
        public string[][] QuickMenuVrWatchButtonsPages;
        public int QuickMenuVrWatchCurrentPage;
        /// <summary>True after extras/defaults have been copied into watch pages once.</summary>
        public bool QuickMenuVrWatchButtonsMigrated;
        /// <summary>True after one-time migrate of old 1.0 default scale to <see cref="QuickMenuVrWatchScaleMulDefault"/>.</summary>
        public bool QuickMenuVrWatchScaleMulV2;
        /// <summary>True after one-time adopt of wrist-locked rest pitch (-60 X) from the old 0,0,0 default.</summary>
        public bool QuickMenuVrWatchFaceRestPitchV2;
        public bool QuickMenuVrWatchFreezeOnApproach = true;
        public bool QuickMenuVrWatchGripPin = true;
        public float QuickMenuVrWatchShoulderBlend = QuickMenuVrWatchShoulderBlendDefault;
        public bool QuickMenuVrWatchLabels = false;
        public bool QuickMenuVrWatchExpanded = false;
        public bool QuickMenuVrWatchCollapsed = false;
        public bool QuickMenuVrWatchHoldConfirm = true;
        /// <summary>Seconds the glance pose must hold before the face appears.</summary>
        public float QuickMenuVrWatchGlanceDwell = QuickMenuVrWatchGlanceDwellDefault;
        public bool QuickMenuRandomHoverPreview = true;

        public static float ClampWatchFaceRotationDeg(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Mathf.Clamp(v, -QuickMenuVrWatchFaceRotationMaxDeg, QuickMenuVrWatchFaceRotationMaxDeg);
        }

        public void ResetVrWatchPose()
        {
            QuickMenuVrWatchOffset = QuickMenuVrWatchOffsetDefault;
            QuickMenuVrWatchTowardUserDist = QuickMenuVrWatchTowardDefault;
            QuickMenuVrWatchScaleMul = QuickMenuVrWatchScaleMulDefault;
            QuickMenuVrWatchScale = VpbWorldSpaceUiScale.MetersPerUiPixel * QuickMenuVrWatchScaleMulDefault;
            QuickMenuVrWatchFaceUser = true;
            QuickMenuVrWatchFaceRotation = QuickMenuVrWatchFaceRotationDefault;
            QuickMenuVrWatchShoulderBlend = QuickMenuVrWatchShoulderBlendDefault;
            QuickMenuVrWatchGlanceDwell = QuickMenuVrWatchGlanceDwellDefault;
        }

        private void MigrateVrWatchSettings(JSONNode node)
        {
            if (node == null) return;

            if (string.Equals(QuickMenuVrWatchMode, "Off", System.StringComparison.Ordinal))
            {
                QuickMenuVrWatchVisible = false;
                QuickMenuVrWatchMode = "Opposite to menu";
            }

            if (node["QuickMenuVrWatchShowWhen"] == null)
            {
                if (node["QuickMenuVrWatchOnlyWithMenu"] != null && !QuickMenuVrWatchOnlyWithMenu)
                    QuickMenuVrWatchShowWhen = "Always";
                else
                    QuickMenuVrWatchShowWhen = "Menu";
            }

            if (node["QuickMenuVrWatchScaleMul"] == null && node["QuickMenuVrWatchScale"] != null)
            {
                float old = QuickMenuVrWatchScale;
                if (Mathf.Abs(old - 0.0005f) < 1e-6f)
                    QuickMenuVrWatchScaleMul = QuickMenuVrWatchScaleMulDefault;
                else
                    QuickMenuVrWatchScaleMul = Mathf.Clamp(old / VpbWorldSpaceUiScale.MetersPerUiPixel,
                        QuickMenuVrWatchScaleMulMin, QuickMenuVrWatchScaleMulMax);
            }

            if (node["QuickMenuVrWatchTowardUserDist"] != null &&
                Mathf.Abs(QuickMenuVrWatchTowardUserDist - 0.12f) < 1e-5f)
                QuickMenuVrWatchTowardUserDist = QuickMenuVrWatchTowardDefault;

            if (node["QuickMenuVrWatchOffset"] != null)
            {
                Vector3 o = QuickMenuVrWatchOffset;
                if (Mathf.Abs(o.x) < 1e-5f && Mathf.Abs(o.y - 0.05f) < 1e-5f && Mathf.Abs(o.z - 0.04f) < 1e-5f)
                    QuickMenuVrWatchOffset = QuickMenuVrWatchOffsetDefault;
            }

            if (!QuickMenuVrWatchScaleMulV2 && node["QuickMenuVrWatchScaleMul"] != null &&
                Mathf.Abs(QuickMenuVrWatchScaleMul - 1f) < 1e-5f)
                QuickMenuVrWatchScaleMul = QuickMenuVrWatchScaleMulDefault;
            QuickMenuVrWatchScaleMulV2 = true;
            if (!QuickMenuVrWatchFaceRestPitchV2 && QuickMenuVrWatchFaceRotation.sqrMagnitude < 1e-4f)
                QuickMenuVrWatchFaceRotation = QuickMenuVrWatchFaceRotationDefault;
            QuickMenuVrWatchFaceRestPitchV2 = true;
            EnsureWatchExtraActions();
            EnsureWatchButtonPages();
            MigrateWatchExtrasIntoPages();
        }

        public void EnsureWatchExtraActions()
        {
            string[] src = QuickMenuVrWatchExtraActions;
            if (src != null && src.Length == QuickMenuVrWatchExtraSlotCount) return;
            string[] next = new string[QuickMenuVrWatchExtraSlotCount];
            int n = src != null ? src.Length : 0;
            if (n > QuickMenuVrWatchExtraSlotCount) n = QuickMenuVrWatchExtraSlotCount;
            for (int i = 0; i < n; i++) next[i] = src[i] ?? "";
            for (int i = n; i < QuickMenuVrWatchExtraSlotCount; i++) next[i] = "";
            QuickMenuVrWatchExtraActions = next;
        }

        public string GetWatchExtraAction(int idx)
        {
            EnsureWatchExtraActions();
            if (idx < 0 || idx >= QuickMenuVrWatchExtraSlotCount) return "";
            string v = QuickMenuVrWatchExtraActions[idx];
            return v ?? "";
        }

        public void SetWatchExtraAction(int idx, string id)
        {
            EnsureWatchExtraActions();
            if (idx < 0 || idx >= QuickMenuVrWatchExtraSlotCount) return;
            QuickMenuVrWatchExtraActions[idx] = id ?? "";
        }

        public void EnsureWatchButtonPages()
        {
            int pages = QuickMenuVrWatchPageCount;
            int slots = QuickMenuVrWatchAssignSlotCount;
            string[][] src = QuickMenuVrWatchButtonsPages;
            bool ok = src != null && src.Length == pages;
            if (ok)
            {
                for (int p = 0; p < pages; p++)
                {
                    if (src[p] == null || src[p].Length != slots)
                    {
                        ok = false;
                        break;
                    }
                }
            }
            if (ok)
            {
                MigrateWatchExtrasIntoPages();
                return;
            }

            string[][] next = new string[pages][];
            for (int p = 0; p < pages; p++)
            {
                string[] row = new string[slots];
                string[] old = (src != null && p < src.Length) ? src[p] : null;
                int n = old != null ? old.Length : 0;
                if (n > slots) n = slots;
                for (int s = 0; s < n; s++) row[s] = old[s] ?? "";
                for (int s = n; s < slots; s++) row[s] = "";
                next[p] = row;
            }
            QuickMenuVrWatchButtonsPages = next;
            if (QuickMenuVrWatchCurrentPage < 0) QuickMenuVrWatchCurrentPage = 0;
            if (QuickMenuVrWatchCurrentPage >= pages) QuickMenuVrWatchCurrentPage = 0;
            MigrateWatchExtrasIntoPages();
        }

        private void MigrateWatchExtrasIntoPages()
        {
            if (QuickMenuVrWatchButtonsMigrated) return;
            if (QuickMenuVrWatchButtonsPages == null || QuickMenuVrWatchButtonsPages.Length < 1) return;
            if (QuickMenuVrWatchButtonsPages[0] == null ||
                QuickMenuVrWatchButtonsPages[0].Length != QuickMenuVrWatchAssignSlotCount)
                return;
            EnsureWatchExtraActions();
            bool page0Empty = true;
            string[] row = QuickMenuVrWatchButtonsPages[0];
            for (int s = 0; s < QuickMenuVrWatchAssignSlotCount; s++)
            {
                if (row[s] != null && row[s].Length > 0)
                {
                    page0Empty = false;
                    break;
                }
            }
            if (page0Empty)
            {
                string[] seed = { "save", "undo", "redo", "random", "hub", "history", "target_atom", "creator_mode" };
                int n = seed.Length;
                if (n > QuickMenuVrWatchAssignSlotCount) n = QuickMenuVrWatchAssignSlotCount;
                for (int s = 0; s < n; s++) row[s] = seed[s];
                for (int i = 0; i < QuickMenuVrWatchExtraSlotCount; i++)
                {
                    string id = QuickMenuVrWatchExtraActions[i];
                    if (!string.IsNullOrEmpty(id)) row[i] = id;
                }
            }
            QuickMenuVrWatchButtonsMigrated = true;
        }

        // Interaction toggles (persisted) <summary>"Off", "Desktop Only", "VR Only", "Desktop &amp; VR".
        public string SpringScrollButtonMode = "Desktop & VR";
        public bool HoldToLaunchEnabled = false;
        public bool TryOnModeEnabled = false;
        public bool InsightsAutoScan = false;
        public bool InsightsConfirmUnreviewedPlugins = false;
        public bool OutlinerOpen = false;
        public int OutlinerLayoutMode = 0;
        public int OutlinerDockSide = 0;
        public float OutlinerWidth = GalleryUiDesignTokens.OutlinerRailWidthRef;
        public float OutlinerSplit = GalleryUiDesignTokens.OutlinerSplitTreeShareRef;
        public int OutlinerPollFrames = 10;
        public string OutlinerPinsJson = "{}";
        public bool OutlinerLinkEdit = false;
        public bool OutlinerLocalSpace = false;
        public bool OutlinerZUpAxes = false;
        public float OutlinerMoveStep = 0.1f;
        public float OutlinerRotateStep = 15f;
        public bool OutlinerLookPreviews = true;
        public int OutlinerTargetsMode = 1;
        public bool OutlinerAutoTargets = true;
        public bool OutlinerTargetsRootOnly = false;
        public float OutlinerTargetsPulseSeconds = 3f;
        public bool SearchRescueEnabled = true;
        public bool VerticalMoveKeysEnabled = true;
        public bool DataPackLookapediaEnabled = true;
        public bool DataPackHubTagsEnabled = true;
        public string HubFetchMissingMode = "Ask";
        public int HubFetchMissingMaxMB = 1500;
        public JSONClass ShortcutBindings = new JSONClass();
        public bool ShortcutsRequireWindowFocus = true;
        public bool ShortcutsNeedVisiblePane = true;
        public bool CategoryNumberKeysEnabled = true;
        public bool HoldToLaunchPrevEnableDragDrop = false;
        /// <summary>Seconds pointer must stay pressed on item before hold-to-launch fires (when HoldToLaunch is on).</summary>
        public float HoldToLaunchHoldSeconds = 1f;

        public int QuickMenuButtonsVersion = 1;
        public int QuickMenuButtonsCurrentPage = 0;
        public string[][] QuickMenuButtonsPages = null;
        public int QuickMenuEditSlotIdx = 12;
        public int QuickMenuPageToggleSlotIdx = 15;

        private static string MatchCanonical(string value, string[] canonical)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string v = value.Trim();
            for (int i = 0; i < canonical.Length; i++)
            {
                if (string.Equals(v, canonical[i], StringComparison.OrdinalIgnoreCase))
                    return canonical[i];
            }
            return null;
        }

        private static readonly string[] s_HoverPreviewModeCanonical = { "Off", "List", "Grid", "Both" };
        public static string NormalizeHoverPreviewMode(string value)
        {
            return MatchCanonical(value, s_HoverPreviewModeCanonical) ?? "List";
        }

        private static readonly string[] s_GallerySearchScopeCanonical = { "PathAndName", "NameOnly", "NameStartsWith" };
        public static string NormalizeGallerySearchScope(string value)
        {
            return MatchCanonical(value, s_GallerySearchScopeCanonical) ?? "PathAndName";
        }

        public static string NormalizeInitialGalleryCategory(string value)
        {
            return MatchCanonical(value, s_InitialGalleryCategoryCanonical) ?? "Scenes";
        }

        /// <summary>Resolved tab for a new pane or the first gallery open this VaM process: a category name, or null when <see cref="InitialGalleryCategory"/> is LastUsed (restore saved tab). Reopen after Close uses LastGalleryCategory via <see cref="Gallery.SessionInitialCategoryApplied"/>.</summary>
        public string ResolveInitialGalleryCategoryName()
        {
            string n = NormalizeInitialGalleryCategory(InitialGalleryCategory);
            if (string.Equals(n, "LastUsed", StringComparison.OrdinalIgnoreCase))
                return null;
            return n;
        }

        public string GalleryDefaultLeftSidePanel = "None";
        public string GalleryDefaultRightSidePanel = "None";
        /// <summary>Last left side-rail / Import from Hide/Close (see <see cref="GallerySidePanelOptions"/>). Used after first open instead of defaults.</summary>
        public string LastGalleryLeftSidePanel = "None";
        public string LastGalleryRightSidePanel = "None";
        /// <summary>True after browse memory has written side-rail place at least once this install.</summary>
        public bool LastGallerySideRailsSaved = false;
        public string GalleryDefaultUserTagAvailMode = "FilterByTags";
        public bool GalleryHideUnusedUserTagsInFilterMode = true;
        public string GalleryUserTagFilterCombineMode = "Compound";
        public float GalleryScrollButtonStepViewportFraction = 0.65f;
        public bool GalleryScrollButtonsEnabled = true;
        /// <summary>When true, VR thumbstick forward/back scrolls the gallery while the pointer is over a pane (blocks free-move on that axis).</summary>
        public bool GalleryVrThumbstickScrollEnabled = true;
        /// <summary>When true, gallery does not create side-rail Creator buttons; creator filtering uses title-bar control only. Side creator panes stay closed.</summary>
        public bool GalleryHideCreatorSideButtons = false;
        public bool GalleryShowCategoryIcons = true;
        /// <summary>When true, creator side/title lists merge names that differ only by case; label uses the variant with the most packages and counts are summed.</summary>
        public bool GalleryConsolidateCreatorNames = true;
        public bool BaMigrationPromptDismissed = false;

        public static readonly string[] GallerySidePanelOptions = { "None", "Import", "Tags", "Category", "Creator", "Path", "History" };

        public static string NormalizeShowSideButtons(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Auto";
            string v = value.Trim();
            if (string.Equals(v, "Left", StringComparison.OrdinalIgnoreCase)) return "Left";
            if (string.Equals(v, "Right", StringComparison.OrdinalIgnoreCase)) return "Right";
            if (string.Equals(v, "Both", StringComparison.OrdinalIgnoreCase)) return "Both";
            return "Auto";
        }

        public static string NormalizeSideRailEdge(string value)
        {
            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase)) return "Left";
            return "Right";
        }

        private static readonly string[] s_GallerySidePanelCanonical = GallerySidePanelOptions;

        public static string NormalizeGallerySidePanel(string value)
        {
            return MatchCanonical(value, s_GallerySidePanelCanonical) ?? "None";
        }

        private static readonly string[] s_GalleryDefaultUserTagAvailModeCanonical = { "FilterByTags", "Tag", "FilterUntagged" };

        public static string NormalizeGalleryDefaultUserTagAvailMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "FilterByTags";
            string v = value.Trim();
            string canonical = MatchCanonical(v, s_GalleryDefaultUserTagAvailModeCanonical);
            if (canonical != null) return canonical;
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

        public static string FormatGalleryDefaultUserTagAvailModeForSettings(string value)
        {
            string n = NormalizeGalleryDefaultUserTagAvailMode(value);
            if (string.Equals(n, "Tag", StringComparison.OrdinalIgnoreCase))
                return "Apply tags";
            if (string.Equals(n, "FilterUntagged", StringComparison.OrdinalIgnoreCase))
                return "Untagged only";
            return "Filter tags";
        }

        public static bool ParseGallerySourceFilterIndependent(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            if (string.Equals(value, "Synced", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Shared", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Sync", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        public static string FormatGallerySourceFilterScopeForSettings(bool independent)
        {
            return independent ? "Independent" : "Synced";
        }

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

        public static string NormalizeGalleryUserTagFilterCombineMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Compound";
            string v = value.Trim();
            string canonical = MatchCanonical(v, s_GalleryUserTagFilterCombineModeCanonical);
            if (canonical != null) return canonical;
            if (string.Equals(v, "Any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "OR", StringComparison.OrdinalIgnoreCase))
                return "Compound";
            if (string.Equals(v, "All", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "AND", StringComparison.OrdinalIgnoreCase))
                return "Isolate";
            return "Compound";
        }

        public bool IsGalleryUserTagFilterIsolate()
        {
            return string.Equals(
                NormalizeGalleryUserTagFilterCombineMode(GalleryUserTagFilterCombineMode),
                "Isolate",
                StringComparison.OrdinalIgnoreCase);
        }
        public bool DesktopFixedMode = false;
        public bool DesktopAutoDockSeeded = true;
        /// <summary>Seconds pointer must be outside fixed pane before auto-collapse (when auto-hide is on).</summary>
        public float DesktopFixedAutoHideSeconds = 1.0f;
        public string DesktopFixedDockSide = "Right";
        public string DesktopFixedDefaultDockSide = "Right";
        public bool DesktopFixedEnforceDockSide = false;
        public string DesktopFixedEnforcedDockSide = "Right";

        public int LayoutPresetStartupIdDesktop;
        public int LayoutPresetStartupIdVR;
        /// <summary>Offer the matching layout when VR/desktop mode changes. Suggestion only — never auto-applies.</summary>
        public bool LayoutPresetSuggestOnModeSwitch = true;

        /// <summary>Seconds the layout Revert bar stays up after an apply.</summary>
        public float LayoutPresetRevertBarSeconds = 8f;

        public string LastLayoutSnapshotDesktop = "";
        public string LastLayoutSnapshotVR = "";

        public readonly GalleryDockSlot DockLeft = new GalleryDockSlot("DockLeft");
        public readonly GalleryDockSlot DockTop = new GalleryDockSlot("DockTop");
        public readonly GalleryDockSlot DockRight = new GalleryDockSlot("DockRight");

        public GalleryDockSlot DockSlotFor(GalleryDockSide side)
        {
            if (side == GalleryDockSide.Left) return DockLeft;
            if (side == GalleryDockSide.Top) return DockTop;
            if (side == GalleryDockSide.Right) return DockRight;
            return null;
        }

        public GalleryDockSlot ActiveDockSlot
        {
            get
            {
                GalleryDockSlot s = DockSlotFor(GalleryDockLayout.Parse(DesktopFixedDockSide));
                return s != null ? s : DockRight;
            }
        }

        public bool DesktopFixedAutoCollapse
        {
            get { return ActiveDockSlot.AutoHide; }
            set { ActiveDockSlot.AutoHide = value; }
        }
        public int DesktopFixedHeightMode
        {
            get { return ActiveDockSlot.HeightMode; }
            set { ActiveDockSlot.HeightMode = value; GalleryDockLayout.BumpVersion(); }
        }
        public float DesktopCustomHeight
        {
            get { return ActiveDockSlot.CustomHeight; }
            set { ActiveDockSlot.CustomHeight = value; GalleryDockLayout.BumpVersion(); }
        }
        public float DesktopCustomWidth
        {
            get { return ActiveDockSlot.WidthFree; }
            set { ActiveDockSlot.WidthFree = value; GalleryDockLayout.BumpVersion(); }
        }
        public bool EnableAutoFixedGallery = true;
        public float ListRowHeight = 100f;
        public int GridColumnCount = 4;
        public int GalleryLayoutMode = 0;
        public bool GalleryShowHiddenPackages = false;
        public float SideButtonScale = 1.0f;
        public float SideButtonScaleVR = 1.0f;
        public float SideButtonScaleDesktop = 1.0f;
        private float _innerPaneScaleVR = 1.0f;
        private float _innerPaneScaleDesktop = 1.0f;
        public bool GalleryUiScaleUnifiedMigrated = false;
        /// <summary>True after first-run gallery UI scale auto-seed finished, or after grandfathering an existing VPB.cfg.</summary>
        public bool GalleryUiScaleAutoSeeded = false;
        public int GalleryUiScaleAutoSeedRevision = 0;
        private bool _loadedFromExistingConfig;
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
        public float CurrentGalleryUiScale => CurrentInnerPaneScale;
        public float CurrentSideButtonScale => CurrentGalleryUiScale;
        public float CurrentInnerPaneScale => IsVR ? InnerPaneScaleVR : InnerPaneScaleDesktop;

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
                return GalleryUiScaleAutoDetect.ReadMonitorUiScale();
            }
        }

        public bool TryEnsureGalleryUiScaleAutoSeeded()
        {
            if (GalleryUiScaleAutoSeeded
                && GalleryUiScaleAutoSeedRevision >= GalleryUiScaleAutoDetect.SeedRevision)
                return true;

            int screenH = 0;
            try { screenH = Screen.height; } catch { screenH = 0; }

            // Brand-new install: wait until Screen.height is valid (may be 0 in early Awake).
            if (!_loadedFromExistingConfig)
            {
                if (!GalleryUiScaleAutoDetect.TryApplyRecommendedPaneScales(this))
                    return false;
                GalleryUiScaleAutoSeeded = true;
                GalleryUiScaleAutoSeedRevision = GalleryUiScaleAutoDetect.SeedRevision;
                try { Save(false, true); } catch { }
                return true;
            }

            // Existing cfg: grandfather once, or correct untouched rev-1 seeds after formula change.
            bool changed = false;
            if (!GalleryUiScaleAutoSeeded)
            {
                GalleryUiScaleAutoSeeded = true;
                changed = true;
            }

            if (GalleryUiScaleAutoSeedRevision < GalleryUiScaleAutoDetect.SeedRevision)
            {
                bool retouch = false;
                if (screenH > 0
                    && GalleryUiScaleAutoSeedRevision >= 1
                    && GalleryUiScaleAutoDetect.LooksLikeUntouchedRevision1Seed(InnerPaneScaleDesktop, screenH))
                {
                    retouch = GalleryUiScaleAutoDetect.TryApplyRecommendedPaneScales(this);
                }
                else if (GalleryUiScaleAutoSeedRevision == 0 && screenH > 0
                    && GalleryUiScaleAutoDetect.LooksLikeUntouchedRevision1Seed(InnerPaneScaleDesktop, screenH))
                {
                    // Seeded under rev1 before revision field existed.
                    retouch = GalleryUiScaleAutoDetect.TryApplyRecommendedPaneScales(this);
                }

                if (retouch) changed = true;
                GalleryUiScaleAutoSeedRevision = GalleryUiScaleAutoDetect.SeedRevision;
                changed = true;
            }

            if (changed)
            {
                try { Save(false, true); } catch { }
            }
            return true;
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

        public string UiLocale = "";
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
            return GalleryGridLabelsStripVisible(GridColumnCount);
        }

        public bool GalleryGridLabelsStripVisible(int columnCount)
        {
            if (!GalleryGridLabelsEnabled) return false;
            if (GalleryGridLabelsAutoHideAtHighDensity && columnCount >= 11) return false;
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

        /// <summary>Fired after Save(bool,bool) (with notification) and TriggerChange.</summary>
        public event OnConfigChanged ConfigChanged;

        internal static int ConfigChangedInvocationDepth { get; private set; }

        public void Load()
        {
            using (VpbNumberText.Invariant())
                LoadCore();
        }

        private static readonly HashSet<string> s_HandSerializedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(_followAngle), nameof(_followDistance), nameof(_followEyeHeight),
            nameof(GallerySourceFilterIndependent), nameof(PassthroughLightCount),
            nameof(QuickMenuButtonsVersion), nameof(QuickMenuButtonsCurrentPage),
            nameof(QuickMenuEditSlotIdx), nameof(QuickMenuPageToggleSlotIdx),
        };

        private static readonly Dictionary<string, Func<float, float>> s_FloatRules = new Dictionary<string, Func<float, float>>(StringComparer.Ordinal)
        {
            { nameof(GalleryElementCornerRadiusFraction), ClampGalleryElementCornerRadiusFraction },
            { nameof(DragHoldThreshold), ClampDragHoldThreshold },
            { nameof(PerfBlend), ClampPerfBlend },
            { nameof(GalleryScrollButtonStepViewportFraction), v => Mathf.Clamp(v, 0.10f, 2.00f) },
            { nameof(GalleryThumbPlaceholderSizeScale), ClampGalleryThumbPlaceholderSizeScale },
            { nameof(GalleryListHoverPreviewSize), v => Mathf.Clamp(v, GalleryHoverPreviewSizeMin, GalleryHoverPreviewSizeMax) },
            { nameof(GalleryListHoverPreviewOffsetX), v => Mathf.Clamp(v, -4000f, 4000f) },
            { nameof(GalleryListHoverPreviewOffsetY), v => Mathf.Clamp(v, -4000f, 4000f) },
            { nameof(GalleryGridLabelFontSize), v => Mathf.Clamp(v, 8f, 40f) },
            { nameof(GalleryGridSpacingX), v => Mathf.Clamp(v, 0f, 80f) },
            { nameof(GalleryGridSpacingY), v => Mathf.Clamp(v, 0f, 80f) },
            { nameof(GalleryGridThumbnailPadding), v => Mathf.Clamp(v, 0f, 40f) },
            { nameof(GalleryGridHoverBorderWidth), v => Mathf.Clamp(v, 0f, 20f) },
            { nameof(GalleryGridSelectedBorderWidth), v => Mathf.Clamp(v, 0f, 30f) },
            { nameof(GalleryGridBorderColorR), Mathf.Clamp01 },
            { nameof(GalleryGridBorderColorG), Mathf.Clamp01 },
            { nameof(GalleryGridBorderColorB), Mathf.Clamp01 },
            { nameof(GalleryGridBorderColorA), Mathf.Clamp01 },
            { nameof(GalleryScanWlBorderWidth), v => Mathf.Clamp(v, 0f, 20f) },
            { nameof(GalleryScanWlGridFrameInset), v => Mathf.Clamp(v, 0f, 24f) },
            { nameof(GalleryScanWlListFrameInset), v => Mathf.Clamp(v, 0f, 24f) },
            { nameof(GalleryScanWlBorderColorR), Mathf.Clamp01 },
            { nameof(GalleryScanWlBorderColorG), Mathf.Clamp01 },
            { nameof(GalleryScanWlBorderColorB), Mathf.Clamp01 },
            { nameof(GalleryScanWlBorderColorA), Mathf.Clamp01 },
            { nameof(GalleryScanWlTempBorderWidth), v => Mathf.Clamp(v, 0f, 20f) },
            { nameof(GalleryScanWlTempGridFrameInset), v => Mathf.Clamp(v, 0f, 24f) },
            { nameof(GalleryScanWlTempListFrameInset), v => Mathf.Clamp(v, 0f, 24f) },
            { nameof(GalleryScanWlTempBorderColorR), Mathf.Clamp01 },
            { nameof(GalleryScanWlTempBorderColorG), Mathf.Clamp01 },
            { nameof(GalleryScanWlTempBorderColorB), Mathf.Clamp01 },
            { nameof(GalleryScanWlTempBorderColorA), Mathf.Clamp01 },
            { nameof(PassthroughKeyColorR), Mathf.Clamp01 },
            { nameof(PassthroughKeyColorG), Mathf.Clamp01 },
            { nameof(PassthroughKeyColorB), Mathf.Clamp01 },
            { nameof(GalleryDetailStripHeightRef), v => Mathf.Max(0f, v) },
            { nameof(GalleryVrMenuAnchorTiltDeg), ClampGalleryVrMenuAnchorTiltDeg },
            { nameof(QuickMenuVrWatchScaleMul), v => Mathf.Clamp(v, QuickMenuVrWatchScaleMulMin, QuickMenuVrWatchScaleMulMax) },
            { nameof(QuickMenuVrWatchShoulderBlend), Mathf.Clamp01 },
            { nameof(QuickMenuVrWatchGlanceDwell), v => Mathf.Clamp(v, 0f, QuickMenuVrWatchGlanceDwellMax) },
            { nameof(QuickMenuVrWatchTowardUserDist), v => Mathf.Clamp(v, -0.5f, 0.5f) },
            { nameof(OutlinerWidth), ClampOutlinerWidth },
            { nameof(OutlinerSplit), Mathf.Clamp01 },
            { nameof(OutlinerMoveStep), ClampOutlinerMoveStep },
            { nameof(OutlinerRotateStep), ClampOutlinerRotateStep },
            { nameof(OutlinerTargetsPulseSeconds), ClampOutlinerTargetsPulse },
            { nameof(HoldToLaunchHoldSeconds), v => Mathf.Clamp(v, 0.2f, 1f) },
            { nameof(LayoutPresetRevertBarSeconds), v => Mathf.Clamp(v, 2f, 30f) },
        };

        private static readonly Dictionary<string, Func<int, int>> s_IntRules = new Dictionary<string, Func<int, int>>(StringComparer.Ordinal)
        {
            { nameof(GalleryLastRatingPresenceFilterMode), ClampRatingPresenceFilterMode },
            { nameof(PerfStepIndex), ClampPerfStepIndex },
            { nameof(SceneImportCacheLimitMb), ClampSceneImportCacheLimitMb },
            { nameof(CreatorStripKeepMask), v => v == 0 ? (int)SceneUtils.CreatorStripKeepDefault : (v & (int)SceneUtils.CreatorStripKeepAllUser) },
            { nameof(CreatorStripPersonRenameMode), v => Mathf.Clamp(v, 0, 3) },
            { nameof(CreatorStripCreateFillMode), v => Mathf.Clamp(v, 0, 2) },
            { nameof(OutlinerTargetsMode), v => Mathf.Clamp(v, 0, 2) },
            { nameof(OutlinerPollFrames), v => Mathf.Clamp(v, 1, 60) },
            { nameof(HubFetchMissingMaxMB), v => Mathf.Clamp(v, 0, 20000) },
        };

        private static readonly Dictionary<string, Func<string, string>> s_StringRules = new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
        {
            { nameof(ShowSideButtons), NormalizeShowSideButtons },
            { nameof(LastGallerySideRailEdge), NormalizeSideRailEdge },
            { nameof(InitialGalleryCategory), NormalizeInitialGalleryCategory },
            { nameof(GallerySearchScope), NormalizeGallerySearchScope },
            { nameof(GalleryHoverPreviewMode), NormalizeHoverPreviewMode },
            { nameof(GalleryDefaultLeftSidePanel), NormalizeGallerySidePanel },
            { nameof(GalleryDefaultRightSidePanel), NormalizeGallerySidePanel },
            { nameof(LastGalleryLeftSidePanel), NormalizeGallerySidePanel },
            { nameof(LastGalleryRightSidePanel), NormalizeGallerySidePanel },
            { nameof(GalleryDefaultUserTagAvailMode), NormalizeGalleryDefaultUserTagAvailMode },
            { nameof(GalleryUserTagFilterCombineMode), NormalizeGalleryUserTagFilterCombineMode },
            { nameof(DesktopFixedDockSide), NormalizeDesktopFixedDockSide },
            { nameof(DesktopFixedDefaultDockSide), NormalizeDesktopFixedDockSide },
            { nameof(DesktopFixedEnforcedDockSide), NormalizeDesktopFixedDockSide },
            { nameof(PassthroughHideScene), NormalizePassthroughHideScene },
            { nameof(SpringScrollButtonMode), NormalizeSpringScrollButtonMode },
            { nameof(HubFetchMissingMode), VpbHubDependencyFetcher.NormalizeMode },
            { nameof(PassthroughLightPresetsJson), v => string.IsNullOrEmpty(v) ? "[]" : v },
            { nameof(CreatorStripRecipesJson), v => string.IsNullOrEmpty(v) ? "[]" : v },
            { nameof(OutlinerPinsJson), v => string.IsNullOrEmpty(v) ? "{}" : v },
            { nameof(GallerySettingsLastGroup), v => string.IsNullOrEmpty(v) ? "appearance" : v },
            { nameof(UiLocale), v => v ?? "en" },
        };

        private static FieldInfo[] s_AutoSerializedFields;
        private static FieldInfo[] s_FloatGeometryFields;

        private static FieldInfo[] AutoSerializedFields()
        {
            if (s_AutoSerializedFields != null) return s_AutoSerializedFields;
            var fields = new List<FieldInfo>();
            var geometry = new List<FieldInfo>();
            foreach (FieldInfo f in typeof(VPBConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type t = f.FieldType;
                if (t == typeof(FloatGeometryPair)) geometry.Add(f);
                if (f.IsInitOnly || s_HandSerializedFields.Contains(f.Name)) continue;
                if (t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(string) || t == typeof(JSONClass))
                    fields.Add(f);
            }
            s_FloatGeometryFields = geometry.ToArray();
            s_AutoSerializedFields = fields.ToArray();
            return s_AutoSerializedFields;
        }

        private IEnumerable<FloatGeometryPair> FloatGeometries()
        {
            AutoSerializedFields();
            foreach (FieldInfo f in s_FloatGeometryFields)
                yield return (FloatGeometryPair)f.GetValue(this);
        }

        private static T ApplyRule<T>(Dictionary<string, Func<T, T>> rules, string name, T value)
        {
            Func<T, T> rule;
            return rules.TryGetValue(name, out rule) ? rule(value) : value;
        }

        private void LoadAutoSerializedFields(JSONNode node)
        {
            foreach (FieldInfo f in AutoSerializedFields())
            {
                JSONNode v = node[f.Name];
                if (v == null) continue;
                Type t = f.FieldType;
                if (t == typeof(bool)) f.SetValue(this, v.AsBool);
                else if (t == typeof(int)) f.SetValue(this, ApplyRule(s_IntRules, f.Name, v.AsInt));
                else if (t == typeof(float)) f.SetValue(this, ApplyRule(s_FloatRules, f.Name, v.AsFloat));
                else if (t == typeof(string)) f.SetValue(this, ApplyRule(s_StringRules, f.Name, v.Value));
                else f.SetValue(this, v.AsObject);
            }
        }

        private void SaveAutoSerializedFields(JSONClass node)
        {
            foreach (FieldInfo f in AutoSerializedFields())
            {
                object value = f.GetValue(this);
                Type t = f.FieldType;
                if (t == typeof(bool)) node[f.Name].AsBool = (bool)value;
                else if (t == typeof(int)) node[f.Name].AsInt = ApplyRule(s_IntRules, f.Name, (int)value);
                else if (t == typeof(float)) node[f.Name].AsFloat = ApplyRule(s_FloatRules, f.Name, (float)value);
                else if (t == typeof(string)) node[f.Name] = ApplyRule(s_StringRules, f.Name, (string)value) ?? "";
                else if (value != null) node[f.Name] = (JSONClass)value;
            }
        }

        private static string ReadFollowMode(JSONNode v, string current)
        {
            if (v == null) return current;
            string val = v.Value;
            if (val == "true" || val == "True") return "Both";
            if (val == "false" || val == "False") return "Off";
            return val;
        }

        private static Vector3 ReadVector3(JSONNode v, Vector3 missingYz)
        {
            return new Vector3(
                v["x"].AsFloat,
                v["y"] != null ? v["y"].AsFloat : missingYz.y,
                v["z"] != null ? v["z"].AsFloat : missingYz.z);
        }

        private static JSONClass WriteVector3(Vector3 v)
        {
            JSONClass o = new JSONClass();
            o["x"].AsFloat = v.x;
            o["y"].AsFloat = v.y;
            o["z"].AsFloat = v.z;
            return o;
        }

        private static string[][] ReadPages(JSONNode pages)
        {
            if (pages == null || pages.Count <= 0) return null;
            var result = new string[pages.Count][];
            for (int p = 0; p < result.Length; p++)
            {
                JSONNode row = pages[p];
                int slotCount = row != null ? row.Count : 0;
                result[p] = new string[slotCount];
                for (int s = 0; s < slotCount; s++)
                    result[p][s] = row[s] != null ? row[s].Value : "";
            }
            return result;
        }

        private static JSONArray WriteStrings(string[] values)
        {
            JSONArray result = new JSONArray();
            if (values != null)
                foreach (string v in values) result.Add(v ?? "");
            return result;
        }

        private static JSONArray WritePages(string[][] pages)
        {
            JSONArray result = new JSONArray();
            if (pages != null)
                foreach (string[] row in pages) result.Add(WriteStrings(row));
            return result;
        }

        private void LoadCore()
        {
            string cfgPath = ConfigPath;
            bool cfgExistedAtStart = File.Exists(cfgPath);
            _loadedFromExistingConfig = false;
            _lightweightGalleryTabRefreshSlotsRemaining = 0;
            if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true) VPBLogger.Config.LogInfo("Starting Load() from: " + cfgPath);
            GalleryDockLayout.BumpVersion();
            try { VpbPassthroughLights.InvalidateSlots(); } catch { }

            try
            {
                if (File.Exists(ConfigPath))
                {
                    _loadedFromExistingConfig = cfgExistedAtStart;
                    string prevLastGalleryCategory = s_LastLoggedLoadedGalleryCategory;
                    string json = File.ReadAllText(ConfigPath);
                    JSONNode node = JSON.Parse(json);
                    if (node != null)
                    {
                        RepairLegacyDecimalCommas(node);
                        LoadAutoSerializedFields(node);
                        _followAngle = ReadFollowMode(node["FollowAngle"], _followAngle);
                        _followDistance = ReadFollowMode(node["FollowDistance"], _followDistance);
                        _followEyeHeight = ReadFollowMode(node["FollowEyeHeight"], _followEyeHeight);
                        if (node["DisableGalleryPaneTransparency"] == null)
                            DisableGalleryPaneTransparency = !EnableGalleryTranslucency;
                        if (!ClothingReplaceStrictnessUpgraded)
                        {
                            if (ClothingReplaceStrictness == 1) ClothingReplaceStrictness = 2;
                            ClothingReplaceStrictnessUpgraded = true;
                        }
                        if (node["AppearanceClothingApplyMode"] != null)
                            AppearanceClothingApplyMode = node["AppearanceClothingApplyMode"].Value;
                        else if (node["KeepClothingWhenApplyingAppearance"] != null)
                            AppearanceClothingApplyMode = node["KeepClothingWhenApplyingAppearance"].AsBool ? "keep" : "replace";
                        bool hadPerfModeKey = node["PerfModeEnabled"] != null;
                        if (node["PerfStepIndex"] == null && (node["PerfBlend"] != null || hadPerfModeKey || !string.IsNullOrEmpty(PerfPresetMode)))
                            MigrateLegacyPerfPresetFields(this, hadPerfModeKey);
                        else
                            PerfStepIndex = ClampPerfStepIndex(PerfStepIndex);
                        RemapPerfStepIndexIfScaleVersionChanged();
                        if (node["global_source_filter"] != null)
                        {
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
                        if (node["gallery_source_filter_independent"] != null)
                            GallerySourceFilterIndependent = node["gallery_source_filter_independent"].AsBool;
                        if (node["DesktopFixedAutoCollapse"] != null) DesktopFixedAutoCollapse = node["DesktopFixedAutoCollapse"].AsBool;
                        if (node["DesktopFixedHeightMode"] != null) DesktopFixedHeightMode = node["DesktopFixedHeightMode"].AsInt;
                        if (node["DesktopCustomHeight"] != null) DesktopCustomHeight = node["DesktopCustomHeight"].AsFloat;
                        if (node["DesktopCustomWidth"] != null) DesktopCustomWidth = node["DesktopCustomWidth"].AsFloat;
                        GalleryDockLayout.LoadSlotsFromConfigNode(node, this);
                        if (node["GalleryHoverPreviewMode"] == null && node["GalleryListHoverPreviewEnabled"] != null)
                            GalleryHoverPreviewMode = node["GalleryListHoverPreviewEnabled"].AsBool ? "List" : "Off";
                        PassthroughLightCount = node["PassthroughLightCount"] != null
                            ? NormalizePassthroughLightCount(node["PassthroughLightCount"].AsInt)
                            : 0;
                        try { VpbPassthroughLights.InvalidateSlots(); } catch { }
                        if (node["GalleryScanWlBadgePrimaryV1"] == null || !node["GalleryScanWlBadgePrimaryV1"].AsBool)
                        {
                            GalleryScanWlBorderEnabled = false;
                            GalleryScanWlTempBorderEnabled = false;
                        }
                        GalleryScanWlBadgePrimaryV1 = true;
                        foreach (FloatGeometryPair geometry in FloatGeometries())
                            geometry.Load(node);
                        if (node["GalleryAnchorOffset"] != null)
                            GalleryAnchorOffset = ReadVector3(node["GalleryAnchorOffset"], new Vector3(0f, 0.1f, -0.1f));
                        if (node["QuickMenuVrWatchOffset"] != null)
                            QuickMenuVrWatchOffset = ReadVector3(node["QuickMenuVrWatchOffset"], QuickMenuVrWatchOffsetDefault);
                        if (node["QuickMenuVrWatchFaceRotation"] != null)
                        {
                            Vector3 r = ReadVector3(node["QuickMenuVrWatchFaceRotation"], Vector3.zero);
                            QuickMenuVrWatchFaceRotation = new Vector3(
                                ClampWatchFaceRotationDeg(r.x),
                                ClampWatchFaceRotationDeg(r.y),
                                ClampWatchFaceRotationDeg(r.z));
                        }
                        if (node["QuickMenuVrWatchExtraActions"] != null)
                        {
                            JSONNode ex = node["QuickMenuVrWatchExtraActions"];
                            EnsureWatchExtraActions();
                            int n = Mathf.Min(ex.Count, QuickMenuVrWatchExtraSlotCount);
                            for (int i = 0; i < n; i++)
                                QuickMenuVrWatchExtraActions[i] = ex[i] != null ? ex[i].Value : "";
                        }
                        QuickMenuVrWatchButtonsPages = ReadPages(node["QuickMenuVrWatchButtonsPages"]) ?? QuickMenuVrWatchButtonsPages;
                        MigrateVrWatchSettings(node);
                        if (node["SideButtonScaleVR"] == null) SideButtonScaleVR = SideButtonScale;
                        if (node["SideButtonScaleDesktop"] == null) SideButtonScaleDesktop = SideButtonScale;
                        if (node["InnerPaneScale"] != null) InnerPaneScale = node["InnerPaneScale"].AsFloat;
                        InnerPaneScaleVR = node["InnerPaneScaleVR"] != null ? node["InnerPaneScaleVR"].AsFloat : InnerPaneScale;
                        InnerPaneScaleDesktop = node["InnerPaneScaleDesktop"] != null ? node["InnerPaneScaleDesktop"].AsFloat : InnerPaneScale;
                        if (node["GalleryUiScaleAutoSeedRevision"] == null && GalleryUiScaleAutoSeeded)
                            GalleryUiScaleAutoSeedRevision = 1;
                        MigrateGalleryUiScaleUnified();
                        if (node["SpringScrollButtonMode"] == null && node["SpringScrollButtonEnabled"] != null)
                            SpringScrollButtonMode = node["SpringScrollButtonEnabled"].AsBool ? "Desktop & VR" : "Off";
                        try
                        {
                            JSONNode qm = node["QuickMenuButtons"];
                            if (qm != null)
                            {
                                if (qm["version"] != null) QuickMenuButtonsVersion = qm["version"].AsInt;
                                if (qm["currentPage"] != null) QuickMenuButtonsCurrentPage = qm["currentPage"].AsInt;
                                if (qm["editSlotIdx"] != null) QuickMenuEditSlotIdx = qm["editSlotIdx"].AsInt;
                                if (qm["pageToggleSlotIdx"] != null) QuickMenuPageToggleSlotIdx = qm["pageToggleSlotIdx"].AsInt;
                                QuickMenuButtonsPages = ReadPages(qm["pages"]) ?? QuickMenuButtonsPages;
                            }
                        }
                        catch { }
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
                        changed |= ClampDockSlotAnchors(DockLeft);
                        changed |= ClampDockSlotAnchors(DockTop);
                        changed |= ClampDockSlotAnchors(DockRight);
                        float ah = ClampDesktopFixedAnchor01(DesktopFixedAutoHideSeconds, 0.1f, 10f, 1.0f);
                        if (Mathf.Abs(DesktopFixedAutoHideSeconds - ah) > 0.0001f) { DesktopFixedAutoHideSeconds = ah; changed = true; }
                        if (changed)
                        {
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
                        if (!string.Equals(prevLastGalleryCategory, LastGalleryCategory, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(LastGalleryCategory))
                        {
                            s_LastLoggedLoadedGalleryCategory = LastGalleryCategory;
                            if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true) VPBLogger.Config.LogInfo("Loaded LastGalleryCategory='" + LastGalleryCategory + "' from " + ConfigPath);
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

            try { SeedDesktopAutoDockOnce(); } catch { }

            try { TryEnsureGalleryUiScaleAutoSeeded(); } catch { }
            try { VpbShortcutMap.LoadFromConfig(this); } catch { }
        }

        internal static void RepairLegacyDecimalCommas(JSONNode node)
        {
            JSONClass obj = node as JSONClass;
            if (obj == null) return;
            foreach (KeyValuePair<string, JSONNode> kvp in obj)
            {
                if (string.Equals(kvp.Key, "HiddenCategories", StringComparison.Ordinal)) continue;
                JSONData leaf = kvp.Value as JSONData;
                if (leaf == null) continue;
                string v = leaf.Value;
                if (IsDecimalCommaNumber(v)) leaf.Value = v.Replace(',', '.');
            }
        }

        private static bool IsDecimalCommaNumber(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            int comma = v.IndexOf(',');
            if (comma <= 0 || comma == v.Length - 1 || v.IndexOf(',', comma + 1) >= 0) return false;
            int start = v[0] == '-' ? 1 : 0;
            if (comma == start) return false;
            for (int i = start; i < v.Length; i++)
            {
                if (i == comma) continue;
                char c = v[i];
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private void SeedDesktopAutoDockOnce()
        {
            if (DesktopAutoDockSeeded) return;
            DesktopAutoDockSeeded = true;
            if (EnableAutoFixedGallery) DesktopFixedMode = true;
            try { Save(false, true); } catch { }
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

        public void Save(bool notifyListeners, bool preferLightGalleryTabChromeOnly)
        {
            if (!notifyListeners)
                _lightweightGalleryTabRefreshSlotsRemaining = 0;
            else if (preferLightGalleryTabChromeOnly)
                ArmLightweightGalleryTabRefreshInternal();
            else
                _lightweightGalleryTabRefreshSlotsRemaining = 0;

            bool lightTabsHint = notifyListeners && preferLightGalleryTabChromeOnly;

            var invariantNumbers = VpbNumberText.Invariant();
            try
            {
                string path = ConfigPath;
                string prevLogged = s_LastLoggedSavedGalleryCategory;
                Stopwatch sw = Stopwatch.StartNew();
                JSONClass node = new JSONClass();
                NormalizeDragDropHoldSettings();
                PerfStepIndex = ClampPerfStepIndex(PerfStepIndex);
                PerfStepScaleVersion = VpbPerfController.PerfStepScaleVersion;
                int perfMax = PerfStepMaxIndex();
                PerfBlend = perfMax > 0 ? (float)PerfStepIndex / (float)perfMax : 0f;
                SceneImportCacheLimitMb = ClampSceneImportCacheLimitMb(SceneImportCacheLimitMb);
                try { VpbShortcutMap.SaveToConfig(); } catch { }
                SaveAutoSerializedFields(node);
                node["FollowAngle"] = _followAngle;
                node["FollowDistance"] = _followDistance;
                node["FollowEyeHeight"] = _followEyeHeight;
                node["AppearanceClothingApplyMode"] = AppearanceClothingApplyMode;
                node["KeepClothingWhenApplyingAppearance"].AsBool = KeepClothingWhenApplyingAppearance;
                node["PerfPresetMode"] = PerfModeEnabled ? "On" : "None";
                node["global_source_filter"] = GlobalSourceFilter.ToString();
                node["gallery_source_filter_independent"].AsBool = GallerySourceFilterIndependent;
                node["DesktopFixedAutoCollapse"].AsBool = DesktopFixedAutoCollapse;
                node["DesktopFixedAutoHideSeconds"].AsFloat = Mathf.Clamp(DesktopFixedAutoHideSeconds, 0.1f, 10f);
                node["DesktopFixedHeightMode"].AsInt = DesktopFixedHeightMode;
                node["DesktopCustomHeight"].AsFloat = DesktopCustomHeight;
                node["DesktopCustomWidth"].AsFloat = DesktopCustomWidth;
                DockLeft.Save(node);
                DockTop.Save(node);
                DockRight.Save(node);
                foreach (FloatGeometryPair geometry in FloatGeometries())
                    geometry.Save(node);
                int lightCount = PassthroughLightCount;
                if (lightCount < 1)
                {
                    try { lightCount = VpbPassthroughLights.GetActiveCount(); }
                    catch { lightCount = 1; }
                }
                node["PassthroughLightCount"].AsInt = NormalizePassthroughLightCount(lightCount);
                node["GalleryAnchorOffset"] = WriteVector3(GalleryAnchorOffset);
                node["QuickMenuVrWatchScale"].AsFloat = VpbWorldSpaceUiScale.MetersPerUiPixel * QuickMenuVrWatchScaleMul;
                node["QuickMenuVrWatchOffset"] = WriteVector3(QuickMenuVrWatchOffset);
                node["QuickMenuVrWatchFaceRotation"] = WriteVector3(QuickMenuVrWatchFaceRotation);
                EnsureWatchExtraActions();
                node["QuickMenuVrWatchExtraActions"] = WriteStrings(QuickMenuVrWatchExtraActions);
                EnsureWatchButtonPages();
                node["QuickMenuVrWatchButtonsPages"] = WritePages(QuickMenuVrWatchButtonsPages);
                node["InnerPaneScale"].AsFloat = InnerPaneScale;
                node["InnerPaneScaleVR"].AsFloat = InnerPaneScaleVR;
                node["InnerPaneScaleDesktop"].AsFloat = InnerPaneScaleDesktop;
                node["HiddenCategories"] = string.Join(",", new List<string>(HiddenCategories ?? new HashSet<string>()).ToArray());
                try
                {
                    JSONClass qm = new JSONClass();
                    qm["version"].AsInt = QuickMenuButtonsVersion;
                    qm["currentPage"].AsInt = QuickMenuButtonsCurrentPage;
                    qm["editSlotIdx"].AsInt = QuickMenuEditSlotIdx;
                    qm["pageToggleSlotIdx"].AsInt = QuickMenuPageToggleSlotIdx;
                    qm["pages"] = WritePages(QuickMenuButtonsPages);
                    node["QuickMenuButtons"] = qm;
                }
                catch { }
                long msBuild = sw.ElapsedMilliseconds;
                string jsonOutput = JsonSerializationUtil.Serialize(node, 32_768);
                invariantNumbers.Dispose();
                long msAfterToString = sw.ElapsedMilliseconds;

                if (!VpbAtomicTextFile.TryWriteWithBackup(path, jsonOutput))
                {
                    VPBLogger.Config.LogError("[VPB] Failed to write temporary config file, aborting save.");
                    return;
                }
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
            finally
            {
                invariantNumbers.Dispose();
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
            string canonical = MatchCanonical(v, s_SpringScrollButtonModeCanonical);
            if (canonical != null) return canonical;
            if (string.Equals(v, "Both", StringComparison.OrdinalIgnoreCase)) return "Desktop & VR";
            if (string.Equals(v, "Desktop", StringComparison.OrdinalIgnoreCase)) return "Desktop Only";
            if (string.Equals(v, "VR", StringComparison.OrdinalIgnoreCase)) return "VR Only";
            return "Desktop & VR";
        }

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
            return MatchCanonical(value, s_DesktopFixedDockSideCanonical) ?? "Right";
        }

        private static float ClampDesktopFixedAnchor01(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>Keeps a dock slot's anchors usable so a docked pane can never become unreachably small.</summary>
        private static bool ClampDockSlotAnchors(GalleryDockSlot slot)
        {
            if (slot == null) return false;
            bool changed = false;
            float w = ClampDesktopFixedAnchor01(slot.WidthFree,
                GalleryDockLayout.MinCrossAnchor, GalleryDockLayout.MaxCrossAnchor, GalleryUiDesignTokens.GoldenRatioMajor);
            float h = ClampDesktopFixedAnchor01(slot.CustomHeight,
                GalleryDockLayout.MinCrossAnchor, GalleryDockLayout.MaxCrossAnchor, 0.5f);
            if (Mathf.Abs(slot.WidthFree - w) > 0.0001f) { slot.WidthFree = w; changed = true; }
            if (Mathf.Abs(slot.CustomHeight - h) > 0.0001f) { slot.CustomHeight = h; changed = true; }
            if (changed) GalleryDockLayout.BumpVersion();
            return changed;
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
