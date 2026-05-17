using BepInEx.Configuration;
using System;
using UnityEngine;
namespace VPB
{
    class Settings
    {
        private static Settings instance;
        private static ConfigFile configFile;
        public static Settings Instance
        {
            get
            {
                if (instance == null) instance = new Settings();
                return instance;
            }
        }

        public static void SaveConfig()
        {
            if (configFile != null)
            {
                try
                {
                    configFile.Save();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Failed to save config: " + ex.Message);
                }
            }
        }

        public ConfigEntry<string> UIKey;
        public ConfigEntry<string> GalleryKey;
        public ConfigEntry<string> CreateGalleryKey;
        public ConfigEntry<string> HubKey;
        public ConfigEntry<string> ClearConsoleKey;
        public ConfigEntry<string> BoneViewKey;
        public ConfigEntry<string> ToggleFixedGalleryKey;
        public ConfigEntry<float> UIScale;
        public ConfigEntry<Vector2> UIPosition;
        public ConfigEntry<bool> MiniMode;
        public ConfigEntry<Vector2> QuickMenuCreateGalleryPosDesktop;
        public ConfigEntry<Vector2> QuickMenuCreateGalleryPosVR;
        public ConfigEntry<Vector2> QuickMenuShowHidePosDesktop;
        public ConfigEntry<Vector2> QuickMenuShowHidePosVR;
        public ConfigEntry<bool> QuickMenuCreateGalleryUseSameInVR;
        public ConfigEntry<bool> QuickMenuShowHideUseSameInVR;
        public ConfigEntry<bool> QuickMenuCreateGalleryEnabled;
        public ConfigEntry<bool> QuickMenuShowHideEnabled;
        public ConfigEntry<bool> QuickMenuCreateGalleryAnchorBaselineMigrated;
        public ConfigEntry<bool> EnableZstdCompression;
        public ConfigEntry<int> ZstdCompressionLevel;
        public ConfigEntry<bool> DeleteOriginalCacheAfterCompression;
        public ConfigEntry<bool> Downscale8kTo4kBeforeZstdCache;
        public ConfigEntry<int> ThumbnailThreshold;
        /// <summary>0 = auto from CPU count and TurboJPEG mode; else clamped 1–64.</summary>
        public ConfigEntry<int> MaxLoaderThreads;
        /// <summary>0 = auto from CPU count and TurboJPEG mode; else clamped 1–64.</summary>
        public ConfigEntry<int> MaxThumbnailThreads;

        public ConfigEntry<bool> AutoOptimizeCache;
        public ConfigEntry<string> LastGalleryPage;
        public ConfigEntry<int> TextureLogLevel;

        public ConfigEntry<bool> LogStartupDetails;
        public ConfigEntry<bool> LogHubRequests;
        public ConfigEntry<bool> LogPerfTelemetry;
        public ConfigEntry<bool> LogPerfDiagnostics;
        public ConfigEntry<bool> LogSavePerf;

        public ConfigEntry<string> HubHostedOption;
        public ConfigEntry<string> HubPayTypeFilter;
        public ConfigEntry<string> HubCategoryFilter;
        public ConfigEntry<string> HubCreatorFilter;
        public ConfigEntry<string> HubTagsFilter;
        public ConfigEntry<string> HubSearchText;
        public ConfigEntry<string> HubSortPrimary;
        public ConfigEntry<string> HubSortSecondary;
        public ConfigEntry<int> HubItemsPerPage;
        public ConfigEntry<int> HubCurrentPage;
        public ConfigEntry<bool> HubOnlyDownloadable;
        public ConfigEntry<bool> HubHideDownloaded;

        public ConfigEntry<bool> LogImageQueueEvents;
        public ConfigEntry<bool> LogVerboseUi;
        /// <summary>When true, logs every VPB.cfg Save/Load/TriggerChange with millisecond timings. When false, only logs if a step is unusually slow.</summary>
        public ConfigEntry<bool> LogConfigPerf;
        public ConfigEntry<bool> EnableUiTransparency;
        public ConfigEntry<float> UiTransparencyValue;
        public ConfigEntry<bool> ShowSceneLoadingOverlay;
        public ConfigEntry<bool> AutoPageEnabled;
        public ConfigEntry<bool> HideOldVersions;
        public ConfigEntry<bool> LoadDependenciesWithPackage;
        public ConfigEntry<bool> ForceLatestDependencies;
        public ConfigEntry<string> ForceLatestDependencyPackageGroups;
        public ConfigEntry<string> ForceLatestDependencyIgnorePackageGroups;

        internal static void Init(ConfigFile config)
        {
            Instance.Load(config);
        }
        private void Load(ConfigFile config)
        {
            configFile = config;
            UIKey = config.Bind<string>("UI", "UIKey", "Ctrl+V", "Shortcut key for Show/Hide Var Browser.");
            GalleryKey = config.Bind<string>("UI", "GalleryKey", "Ctrl+G", "Shortcut key for Show/Hide Gallery Panes.");
            CreateGalleryKey = config.Bind<string>("UI", "CreateGalleryKey", "Ctrl+N", "Shortcut key for Create Gallery Pane.");
            HubKey = config.Bind<string>("UI", "HubKey", "Ctrl+H", "Shortcut key for Open Hub Browser.");
            ClearConsoleKey = config.Bind<string>("UI", "ClearConsoleKey", "F2", "Shortcut key to clear the BepInEx console output.");
            BoneViewKey = config.Bind<string>("UI", "BoneViewKey", "F9", "Shortcut key to toggle Bone View mode (draws skeleton over characters).");
            ToggleFixedGalleryKey = config.Bind<string>("UI", "ToggleFixedGalleryKey", "B", "Shortcut key to toggle the fixed gallery menu open/closed.");
            UIScale = config.Bind<float>("UI", "Scale", 1.5f, "Set UI Scale.");
            UIPosition = config.Bind<Vector2>("UI", "Position", Vector2.zero, "Set UI Position.");
            MiniMode = config.Bind<bool>("UI", "MiniMode", false, "Set Mini Mode.");
            // Baseline anchor: treated as "0,0" in the UI position window (see QuickMenuAnchorBaseline in VamHookPlugin).
            QuickMenuCreateGalleryPosDesktop = config.Bind<Vector2>("UI", "QuickMenuCreateGalleryPosDesktop", new Vector2(-515f, -12f), "Anchored position for Quick Menu Create Gallery button in Desktop mode.");
            QuickMenuCreateGalleryPosVR = config.Bind<Vector2>("UI", "QuickMenuCreateGalleryPosVR", new Vector2(-515f, -12f), "Anchored position for Quick Menu Create Gallery button in VR mode.");
            QuickMenuShowHidePosDesktop = config.Bind<Vector2>("UI", "QuickMenuShowHidePosDesktop", new Vector2(-470f, -216f), "Anchored position for Quick Menu Show/Hide button in Desktop mode.");
            QuickMenuShowHidePosVR = config.Bind<Vector2>("UI", "QuickMenuShowHidePosVR", new Vector2(-470f, -216f), "Anchored position for Quick Menu Show/Hide button in VR mode.");
            QuickMenuCreateGalleryUseSameInVR = config.Bind<bool>("UI", "QuickMenuCreateGalleryUseSameInVR", true, "Use the same Quick Menu Create Gallery position in VR as Desktop.");
            QuickMenuShowHideUseSameInVR = config.Bind<bool>("UI", "QuickMenuShowHideUseSameInVR", true, "Use the same Quick Menu Show/Hide position in VR as Desktop.");
            QuickMenuCreateGalleryEnabled = config.Bind<bool>("UI", "QuickMenuCreateGalleryEnabled", true, "Show the Quick Menu Create Gallery button.");
            QuickMenuShowHideEnabled = config.Bind<bool>("UI", "QuickMenuShowHideEnabled", true, "Show the Quick Menu Show/Hide button.");
            // One-time migration: force everyone to the baseline anchor once, then allow custom.
            QuickMenuCreateGalleryAnchorBaselineMigrated = config.Bind<bool>("UI", "QuickMenuCreateGalleryAnchorBaselineMigrated", false, "Internal: set true after Quick Menu anchor baseline migration runs once.");
            EnableZstdCompression = config.Bind<bool>("Optimze", "EnableZstdCompression", true, "Enable Zstd compression for texture cache.");
            
            ZstdCompressionLevel = config.Bind<int>("Optimze", "ZstdCompressionLevel", 5, "Zstd compression level (1-22, higher = better compression but slower).");
            DeleteOriginalCacheAfterCompression = config.Bind<bool>("Optimze", "DeleteOriginalCacheAfterCompression", true, "Delete original .vamcache files after successful Zstd compression.");
            Downscale8kTo4kBeforeZstdCache = config.Bind<bool>("Optimze", "Downscale8kTo4kBeforeZstdCache", false, "Downscale 8K (8192x8192) textures to 4K before writing Zstd texture cache.");
            ThumbnailThreshold = config.Bind<int>("Optimze", "ThumbnailThreshold", 600, "Resolution threshold (width & height) below which a texture is considered a thumbnail and skipped by VPB optimizations.");
            MaxLoaderThreads = config.Bind<int>("Optimze", "MaxLoaderThreads", 0, "Concurrent image decode workers (full-res queue). 0 = auto: scales with ProcessorCount; lower cap when TurboJPEG path active.");
            MaxThumbnailThreads = config.Bind<int>("Optimze", "MaxThumbnailThreads", 0, "Concurrent thumbnail decode workers. 0 = auto: scales with ProcessorCount; lower cap when TurboJPEG path active.");

            EnableUiTransparency = config.Bind<bool>("UI", "EnableUiTransparency", true, "Enable dynamic UI transparency (fade when idle).");
            UiTransparencyValue = config.Bind<float>("UI", "UiTransparencyValue", 0.5f, "Transparency level when idle (0.0 = Opaque, 1.0 = Invisible).");
            ShowSceneLoadingOverlay = config.Bind<bool>("UI", "ShowSceneLoadingOverlay", false, "Show VPB full-screen loading overlay during scene loads.");
            AutoPageEnabled = config.Bind<bool>("UI", "AutoPageEnabled", false, "Enable Auto Paging in Gallery on scroll.");
            HideOldVersions = config.Bind<bool>("UI", "HideOldVersions", false, "Hide older versions of VAR packages in the gallery; show only the newest version per Creator.Package family.");
            LoadDependenciesWithPackage = config.Bind<bool>("Settings", "LoadDependenciesWithPackage", true, "When loading a package, also load all its dependencies.");
            ForceLatestDependencies = config.Bind<bool>("Settings", "ForceLatestDependencies", false, "When resolving package dependencies, force certain dependency references to use the newest locally installed version.");
            ForceLatestDependencyPackageGroups = config.Bind<string>("Settings", "ForceLatestDependencyPackageGroups", "", "Comma/space separated list of package groups (Author.Package) for which dependency version resolution should be forced to newest locally installed.");
            ForceLatestDependencyIgnorePackageGroups = config.Bind<string>("Settings", "ForceLatestDependencyIgnorePackageGroups", "", "Comma/space separated list of package groups (Author.Package) to ignore (do not force) even when ForceLatestDependencies is enabled.");

            TextureLogLevel = config.Bind<int>("Logging", "TextureLogLevel", 0, "0=off, 1=summary only, 2=verbose per-texture trace.");
            LogImageQueueEvents = config.Bind<bool>("Logging", "LogImageQueueEvents", false, "Log IMGQ enqueue/dequeue events (very verbose).");
            LogVerboseUi = config.Bind<bool>("Logging", "LogVerboseUi", false, "Log verbose UI lifecycle messages (can be noisy).");
            LogConfigPerf = config.Bind<bool>("Logging", "LogConfigPerf", false, "Log VPB.cfg Save timing and each ConfigChanged subscriber. Set false after troubleshooting.");

            LogStartupDetails = config.Bind<bool>("Logging", "LogStartupDetails", false, "Log additional startup/patch/initialization details (can be noisy). Enable when troubleshooting.");
            LogHubRequests = config.Bind<bool>("Logging", "LogHubRequests", false, "Log detailed Hub request timing and payload information (very verbose). Enable when troubleshooting Hub issues.");
            LogPerfTelemetry = config.Bind<bool>("Logging", "LogPerfTelemetry", false, "Emit a periodic VPB_PERF_TELEMETRY line (30s interval) with cache sizes, queue depths, panel scroll-listener counts, and heap stats. Enable when diagnosing progressive FPS degradation.");
            LogPerfDiagnostics = config.Bind<bool>("Logging", "LogPerfDiagnostics", false, "Emit a 1Hz VPB.Diag line with per-frame call counters (quick-menu refresh, icon GO churn, gallery Update gating, pointer sibling reorders) plus one-shot transition logs for show/hide/menu-gate. Enable temporarily to pinpoint frame-cost hotspots; disable for normal use.");
            LogSavePerf = config.Bind<bool>("Logging", "LogSavePerf", false, "Log scene-save timing split: bridge prep vs native SaveScene invocation. Enable when diagnosing save-time regressions vs native VaM baseline.");

            HubHostedOption = config.Bind<string>("HubBrowser", "HostedOption", "Hub And Dependencies", "Hub Browser: Hosted option filter.");
            HubPayTypeFilter = config.Bind<string>("HubBrowser", "PayTypeFilter", "All", "Hub Browser: Pay type filter.");
            HubCategoryFilter = config.Bind<string>("HubBrowser", "CategoryFilter", "All", "Hub Browser: Category filter.");
            HubCreatorFilter = config.Bind<string>("HubBrowser", "CreatorFilter", "All", "Hub Browser: Creator filter.");
            HubTagsFilter = config.Bind<string>("HubBrowser", "TagsFilter", "All", "Hub Browser: Tags filter.");
            HubSearchText = config.Bind<string>("HubBrowser", "SearchText", "", "Hub Browser: Search text.");
            HubSortPrimary = config.Bind<string>("HubBrowser", "SortPrimary", "Latest Update", "Hub Browser: Primary sort.");
            HubSortSecondary = config.Bind<string>("HubBrowser", "SortSecondary", "None", "Hub Browser: Secondary sort.");
            HubItemsPerPage = config.Bind<int>("HubBrowser", "ItemsPerPage", 48, "Hub Browser: Items per page.");
            HubCurrentPage = config.Bind<int>("HubBrowser", "CurrentPage", 1, "Hub Browser: Current page.");
            HubOnlyDownloadable = config.Bind<bool>("HubBrowser", "OnlyDownloadable", false, "Hub Browser: Only show downloadable resources.");
            HubHideDownloaded = config.Bind<bool>("HubBrowser", "HideDownloaded", false, "Hub Browser: Hide packages already downloaded to disk.");


            AutoOptimizeCache = config.Bind<bool>("Optimze", "AutoOptimizeCache", false, "When checked, clicking Compress Cache button will start compression without opening the confirmation window.");
            LastGalleryPage = config.Bind<string>("UI", "LastGalleryPage", "", "Last opened Gallery page.");
        }
    }
}
