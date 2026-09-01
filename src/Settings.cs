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
        /// <summary>0 = auto: min(ProcessorCount, 12); else clamped 1–32 parallel VAR deep-scan workers.</summary>
        public ConfigEntry<int> MaxDeepScanWorkers;

        public ConfigEntry<string> LastGalleryPage;
        public ConfigEntry<int> TextureLogLevel;

        public ConfigEntry<bool> LogStartupDetails;
        public ConfigEntry<string> IndexDiagUidSubstring;
        public ConfigEntry<bool> LogStartupTiming;
        public ConfigEntry<bool> StartupDeferGallerySqlRebuild;
        public ConfigEntry<float> StartupDeferGallerySqlRebuildDelaySec;
        public ConfigEntry<bool> StartupSkipRedundantSyncVamX;
        public ConfigEntry<bool> StartupSkipGallerySqlRebuildIfValid;
        public ConfigEntry<bool> StartupPreferIncrementalGallerySqlIndex;
        public ConfigEntry<int> StartupIncrementalGallerySqlMaxDelta;
        public ConfigEntry<bool> StartupDeferDependencyGraphRebuild;
        public ConfigEntry<bool> StartupDeferPackageDeepScanUntilReady;
        public ConfigEntry<bool> StartupUseCachedVarPathInventory;
        public ConfigEntry<bool> StartupSkipBootstrapNativeRefresh;
        public ConfigEntry<bool> StartupSqlBatchCatMem;
        public ConfigEntry<bool> LoadParallelAssetBundles;
        public ConfigEntry<int> LoadParallelAssetBundleWorkers;
        public ConfigEntry<bool> LoadAttributeSlowFrames;
        public ConfigEntry<bool> LoadProfileScenePhases;
        public ConfigEntry<int> LoadProfileSlowFrameMs;
        public ConfigEntry<int> StartupSqlBatchCatMemRows;
        public ConfigEntry<bool> LogHubRequests;
        public ConfigEntry<bool> LogPerfTelemetry;
        public ConfigEntry<int> LogPerfTelemetryIntervalSeconds;
        public ConfigEntry<bool> LogPerfDiagnostics;
        public ConfigEntry<bool> PerfSilenceVaMPerfMon;
        public ConfigEntry<bool> PerfDetectGiveMeFpsConflict;
        public ConfigEntry<bool> LogSavePerf;
        public ConfigEntry<bool> LogStateMachineApply;
        public ConfigEntry<bool> NetEnabled;
        public ConfigEntry<string> NetBrokerPath;
        public ConfigEntry<int> NetSamplerHz;
        public ConfigEntry<bool> NetOverlay;
        public ConfigEntry<float> NetOverlayX;
        public ConfigEntry<float> NetOverlayY;
        public ConfigEntry<bool> NetOverlayCollapsed;
        public ConfigEntry<string> NetLanAddress;
        public ConfigEntry<bool> NetLanDiscovery;
        public ConfigEntry<string> NetLanRoomCode;
        public ConfigEntry<string> NetHostRoomCode;
        public ConfigEntry<string> NetJoinRoomCode;
        public ConfigEntry<string> NetRoomBook;
        public ConfigEntry<string> NetRendezvousAddress;
        public ConfigEntry<string> NetTransport;
        public ConfigEntry<int> NetSteamAppId;
        public ConfigEntry<string> NetSteamApiPath;
        public ConfigEntry<bool> NetSteamIdentityAck;
        public ConfigEntry<bool> NetDirectIpAck;
        public ConfigEntry<bool> NetRoomCodeLocked;
        public ConfigEntry<bool> NetRecordEnabled;
        public ConfigEntry<string> NetRecordAtom;
        public ConfigEntry<int> NetRecordHz;
        public ConfigEntry<bool> NetReplayEnabled;
        public ConfigEntry<string> NetReplayAtom;
        public ConfigEntry<bool> NetReplayLoop;
        public ConfigEntry<float> NetReplaySpeed;
        public ConfigEntry<string> NetClipFile;
        public ConfigEntry<int> NetContentMaxMB;
        public ConfigEntry<bool> NetHostSession;
        public ConfigEntry<bool> NetJoinSession;
        public ConfigEntry<bool> NetReopenOnStart;
        public ConfigEntry<bool> NetSessionUi;
        public ConfigEntry<float> NetSessionUiX;
        public ConfigEntry<float> NetSessionUiY;
        public ConfigEntry<bool> NetSessionFlowSeen;
        public ConfigEntry<float> NetRulesUiX;
        public ConfigEntry<float> NetRulesUiY;
        public ConfigEntry<float> NetRulesUiW;
        public ConfigEntry<float> NetRulesUiH;
        public ConfigEntry<bool> NetRulesUiCollapsed;
        public ConfigEntry<string> NetLocalAtom;
        public ConfigEntry<bool> NetLockRoot;
        public ConfigEntry<bool> NetSyncAtoms;
        public ConfigEntry<int> NetRulesLo;
        public ConfigEntry<int> NetRulesHi;
        public ConfigEntry<bool> PoseDualAnchorAtMe;
        public ConfigEntry<bool> NetReplayLockRoot;

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
        public ConfigEntry<bool> ShowGalleryIndexBuildOverlay;
        public ConfigEntry<bool> AutoPageEnabled;
        public ConfigEntry<bool> HideOldVersions;
        public ConfigEntry<bool> LoadDependenciesWithPackage;
        public ConfigEntry<bool> SyncRefreshOnPresetLoad;
        public ConfigEntry<bool> SkipPackageMorphRefreshOnClothingHairCatalog;
        public ConfigEntry<bool> PreferLightClothingHairCatalogBeforeNativeRefresh;
        public ConfigEntry<bool> HairSwapKeepVisibleUntilLoaded;
        public ConfigEntry<bool> ReturnToSceneViewOnStartup;
        public ConfigEntry<bool> ForceLatestDependencies;
        public ConfigEntry<string> ForceLatestDependencyPackageGroups;
        public ConfigEntry<string> ForceLatestDependencyIgnorePackageGroups;
        /// <summary>When true, honor meta.json standard/script ReferenceVersionOption (Exact/Minimum/Latest).</summary>
        public ConfigEntry<bool> RespectPackageReferenceVersionOption;
        /// <summary>When true, always treat package refs as Exact (never upgrade versioned UIDs).</summary>
        public ConfigEntry<bool> ForceExactPackageVersions;
        public ConfigEntry<bool> PluginConsolidateCslist;

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
            MaxDeepScanWorkers = config.Bind<int>("Optimze", "MaxDeepScanWorkers", 0, "Parallel workers for deep VAR package scan (zip index). 0 = auto: min(ProcessorCount, 12); else clamped 1–32.");

            EnableUiTransparency = config.Bind<bool>("UI", "EnableUiTransparency", true, "Enable dynamic UI transparency (fade when idle).");
            UiTransparencyValue = config.Bind<float>("UI", "UiTransparencyValue", 0.5f, "Transparency level when idle (0.0 = Opaque, 1.0 = Invisible).");
            ShowGalleryIndexBuildOverlay = config.Bind<bool>("UI", "ShowGalleryIndexBuildOverlay", true, "Show top progress banner while VAR packages are scanned and the gallery SQLite index is built at startup. Warns not to close VaM during first-time or full rebuild indexing.");
            AutoPageEnabled = config.Bind<bool>("UI", "AutoPageEnabled", false, "Enable Auto Paging in Gallery on scroll.");
            HideOldVersions = config.Bind<bool>("UI", "HideOldVersions", true, "Legacy cfg: hide older VAR versions (newest per Creator.Package). Default on. Synced with Filters → Old versions → Newest. Prefer the Filters UI; SQL uses pkg.is_newest when the index is ready.");
            LoadDependenciesWithPackage = config.Bind<bool>("Settings", "LoadDependenciesWithPackage", true, "When loading a package, also load all its dependencies.");
            SyncRefreshOnPresetLoad = config.Bind<bool>("Settings", "SyncRefreshOnPresetLoad", true, "On interactive preset apply (appearance/clothing/hair/plugin preset), run a synchronous coalesced native VaM FileManager.Refresh before VaM binds storables. Required for first-click preset apply to find morphs/clothing/hair from packages registered on demand. When false: coalesced native refresh only (~250ms delay) — first-click preset apply for on-demand packages may miss catalog entries until refresh completes; safe to disable if sync refresh causes stalls. No full VPB rescan on preset apply.");
            SkipPackageMorphRefreshOnClothingHairCatalog = config.Bind<bool>("Settings", "SkipPackageMorphRefreshOnClothingHairCatalog", true, "During on-demand clothing/hair catalog FileManager.Refresh, skip DAZ RefreshPackageMorphs. Avoids ~18s/person morph re-ingest (e.g. Naturalis/TittyMagic) when only clothing/hair packages were registered. Morph packages still trigger full morph refresh. Disable if new morphs from a clothing .var are missing after dress.");
            PreferLightClothingHairCatalogBeforeNativeRefresh = config.Bind<bool>("Settings", "PreferLightClothingHairCatalogBeforeNativeRefresh", true, "Before forced native FileManager.Refresh on clothing/hair apply, try DAZ RefreshClothingItems/RefreshHairItems on the target Person. If the clothing/hair param already exists, cancel the pending native refresh (avoids multi-second Person refresh × all atoms).");
            HairSwapKeepVisibleUntilLoaded = config.Bind<bool>("Helpers", "HairSwapKeepVisibleUntilLoaded", true, "During hair preset replace, keep previous hair visible until new hair finishes loading. Outgoing hair collisions are disabled first; outgoing mesh is hidden only after incoming hair is ready.");
            ReturnToSceneViewOnStartup = config.Bind<bool>("Helpers", "ReturnToSceneViewOnStartup", false, "On startup, skip VaM main menu (World UI) and return to scene view (same as Return To Scene View).");
            ForceLatestDependencies = config.Bind<bool>("Settings", "ForceLatestDependencies", false, "When resolving package dependencies, force certain dependency references to use the newest locally installed version.");
            ForceLatestDependencyPackageGroups = config.Bind<string>("Settings", "ForceLatestDependencyPackageGroups", "", "Comma/space separated list of package groups (Author.Package) for which dependency version resolution should be forced to newest locally installed.");
            ForceLatestDependencyIgnorePackageGroups = config.Bind<string>("Settings", "ForceLatestDependencyIgnorePackageGroups", "", "Comma/space separated list of package groups (Author.Package) to ignore (do not force) even when ForceLatestDependencies is enabled.");
            RespectPackageReferenceVersionOption = config.Bind<bool>("Settings", "RespectPackageReferenceVersionOption", true, "When true, honor meta.json standardReferenceVersionOption / scriptReferenceVersionOption (Exact, Minimum, Latest) when a pinned package version is missing. Exact pins that exist on disk are always kept.");
            ForceExactPackageVersions = config.Bind<bool>("Settings", "ForceExactPackageVersions", false, "When true, never rewrite versioned package paths (Author.Pkg.N) to a newer version — always use the exact pin. Overrides meta Latest/Minimum for missing-version fallback.");
            PluginConsolidateCslist = config.Bind<bool>("Settings", "PluginConsolidateCslist", true, "In the Plugins gallery category, hide .cs files that are referenced by a .cslist so each multi-file plugin shows as a single row (its .cslist). Standalone .cs files (not in any .cslist) always show. Turn off to see every individual .cs file.");

            TextureLogLevel = config.Bind<int>("Logging", "TextureLogLevel", 0, "0=off, 1=summary only, 2=verbose per-texture trace.");
            LogImageQueueEvents = config.Bind<bool>("Logging", "LogImageQueueEvents", false, "Log IMGQ enqueue/dequeue events (very verbose).");
            LogVerboseUi = config.Bind<bool>("Logging", "LogVerboseUi", false, "Log verbose UI lifecycle messages (can be noisy).");
            LogConfigPerf = config.Bind<bool>("Logging", "LogConfigPerf", false, "Log VPB.cfg Save timing and each ConfigChanged subscriber. Set false after troubleshooting.");

            LogStartupDetails = config.Bind<bool>("Logging", "LogStartupDetails", false, "Log additional startup/patch/initialization details (can be noisy). Enable when troubleshooting.");
            IndexDiagUidSubstring = config.Bind<string>("Logging", "IndexDiagUidSubstring", "", "When set (e.g. RunRudolf.AlternativeFuta), emit [VPB.IndexDiag] logs for that package through disk scan, registry, SQLite index, and gallery listing. Empty = off.");
            LogStartupTiming = config.Bind<bool>("Logging", "LogStartupTiming", false, "Emit [VPB.Startup.Timing] milestones and a cold-start summary (native/VPB package refresh, SyncVamX, bootstrap). Also enabled when LogStartupDetails is true.");
            StartupDeferGallerySqlRebuild = config.Bind<bool>("Startup", "DeferGallerySqlRebuildUntilReady", true, "Defer full gallery SQLite index rebuild until World UI / startup-ready milestone. Speeds cold start; gallery SQL queries wait until rebuild runs.");
            StartupDeferGallerySqlRebuildDelaySec = config.Bind<float>("Startup", "DeferGallerySqlRebuildDelaySec", 2f, "Seconds after READY before deferred gallery SQLite rebuild starts (0 = immediate). Gives UI/gallery a short window on restored index.");
            StartupSkipRedundantSyncVamX = config.Bind<bool>("Startup", "SkipRedundantSyncVamXWhenAbsent", true, "When vamX is absent, skip expensive vamX bootstrap FileExists/on-demand work during startup. SyncVamX always runs so main-menu Create tiles stay correct.");
            StartupSkipGallerySqlRebuildIfValid = config.Bind<bool>("Startup", "SkipGallerySqlRebuildIfValid", true, "Skip full gallery SQLite rebuild when on-disk index meta matches current categories and package inventory (e.g. after sqlRestore).");
            StartupPreferIncrementalGallerySqlIndex = config.Bind<bool>("Startup", "PreferIncrementalGallerySqlIndex", true, "When package inventory changes by a small delta (Hub download, few adds/removes), patch the gallery SQLite index instead of DELETE+rebuild of all rows.");
            StartupIncrementalGallerySqlMaxDelta = config.Bind<int>("Startup", "IncrementalGallerySqlMaxDelta", 0, "Max package add+remove count for incremental SQL index (0 = auto: min(32, 20% of indexed packages)). Larger changes use full rebuild.");
            StartupDeferDependencyGraphRebuild = config.Bind<bool>("Startup", "DeferDependencyGraphRebuildUntilReady", true, "After init package scan, post FileManagerRefresh immediately and rebuild dependency graph/counts after READY (avoids ~20s blocking SQLite per-package reads on large libraries).");
            StartupDeferPackageDeepScanUntilReady = config.Bind<bool>("Startup", "DeferPackageDeepScanUntilReady", true, "When manifest/SQL cache is missing, register all .var paths immediately but defer per-package zip deep scan (DumpVarPackage) until World UI / startup-ready. Unblocks VPB UI on cold start without SQL; gallery SQL waits until scan completes.");
            StartupUseCachedVarPathInventory = config.Bind<bool>("Startup", "UseCachedVarPathInventory", true, "Skip recursive AddonPackages/AllPackages .var walks when SQLite path inventory validates (parallel stat checks). Saves ~15s on large libraries when packages unchanged.");
            StartupSkipBootstrapNativeRefresh = config.Bind<bool>("Startup", "SkipBootstrapNativeRefreshWhenUnchanged", true, "When VPB init already ran native FileManager.Refresh and .var path inventory is unchanged, skip the synchronous Refresh inside SuperController.SyncToKeyFile (Awake). Avoids redundant scan + OnPackageRefresh/SyncVamX stall.");
            StartupSqlBatchCatMem = config.Bind<bool>("Startup", "SqlBatchCatMemInserts", false, "During gallery SQLite rebuild, batch cat_mem INSERT statements instead of one row per Step(). Off by default (prepared inserts are faster on large libraries).");
            StartupSqlBatchCatMemRows = config.Bind<int>("Startup", "SqlBatchCatMemRows", 150, "Rows per batched cat_mem INSERT when SqlBatchCatMemInserts is enabled.");
            LoadParallelAssetBundles = config.Bind<bool>("SceneLoad", "ParallelAssetBundles", true, "Replace VaM's strictly-serial CUA asset-bundle queue (one bundle per frame, fully awaited) with a deduplicated parallel loader. Requests for the same path share one load instead of racing. Turn off to restore VaM's loader if custom assets misbehave.");
            LoadParallelAssetBundleWorkers = config.Bind<int>("SceneLoad", "ParallelAssetBundleWorkers", 3, "Concurrent asset-bundle loads. Loads from the same .var are still serialized (VaM shares one ZipFile handle per package).");
            LoadAttributeSlowFrames = config.Bind<bool>("SceneLoad", "AttributeSlowFrames", false, "DIAGNOSTIC. Patches Update/LateUpdate/FixedUpdate and coroutine MoveNext across VaM and every plugin assembly, then names the top time consumers for each slow frame during a scene load, plus attributed-vs-unattributed ms. Costs seconds of extra startup and per-call overhead while loading. Enable for a benchmark run, then turn back off.");
            LoadProfileScenePhases = config.Bind<bool>("SceneLoad", "ProfileScenePhases", false, "Emit a per-phase timing breakdown at the end of every scene load (Clear+Unload, Loading Atoms, Restoring Atoms, Post-Restore, Waiting Async Load), plus the slowest frames and the time spent inside VPB's own FileExists / OpenStream / GetVarFileEntry hooks. Cheap to run but writes to the log on every load; enable when investigating slow scene loads.");
            LoadProfileSlowFrameMs = config.Bind<int>("SceneLoad", "ProfileSlowFrameMs", 250, "Frames longer than this (ms) during a scene load are recorded with the phase they occurred in.");
            LogHubRequests = config.Bind<bool>("Logging", "LogHubRequests", false, "Log detailed Hub request timing and payload information (very verbose). Enable when troubleshooting Hub issues.");
            LogPerfTelemetry = config.Bind<bool>("Logging", "LogPerfTelemetry", false, "Emit a periodic VPB_PERF_TELEMETRY line with cache sizes, queue depths, panel scroll-listener counts, and heap stats. Enable when diagnosing progressive FPS degradation.");
            LogPerfTelemetryIntervalSeconds = config.Bind<int>("Logging", "LogPerfTelemetryIntervalSeconds", 30, "Seconds between VPB_PERF_TELEMETRY snapshots (clamped 1-30). Only used when LogPerfTelemetry is enabled.");
            LogPerfDiagnostics = config.Bind<bool>("Logging", "LogPerfDiagnostics", false, "Emit a 1Hz VPB.Diag line with per-frame call counters (quick-menu refresh, icon GO churn, gallery Update gating, pointer sibling reorders) plus one-shot transition logs for show/hide/menu-gate. Enable temporarily to pinpoint frame-cost hotspots; disable for normal use.");
            PerfSilenceVaMPerfMon = config.Bind<bool>("Performance", "SilenceVaMPerfMonOnPerfPreset", true, "When gallery perf preset P1/P2 is active, silence MeshVR PerfMon camera/pre update hooks (reduces overlay cost).");
            PerfDetectGiveMeFpsConflict = config.Bind<bool>("Performance", "DetectGiveMeFpsConflict", true, "Log a one-time warning if Redeyes GiveMeFPS session plugin is loaded alongside VPB perf presets.");
            LogSavePerf = config.Bind<bool>("Logging", "LogSavePerf", false, "Log scene-save timing split: bridge prep vs native SaveScene invocation. Enable when diagnosing save-time regressions vs native VaM baseline.");
            NetEnabled = config.Bind<bool>("Net", "Enabled", false, "MASTER KILL SWITCH for VPB multiplayer. While this is false the broker process (VpbNet.exe) can never be launched by anything, and multiplayer is entirely absent. Nothing is launched on plugin load even when true; the broker starts only on explicit session use.");
            NetBrokerPath = config.Bind<string>("Net", "BrokerPath", "", "Full path to VpbNet.exe. Empty uses VpbNet\\VpbNet.exe next to VPB.dll.");
            NetSamplerHz = config.Bind<int>("Net", "SamplerHz", 45, "Protocol sample rate in frames per second (1-200). 45 is the planned pose rate. Setting this above the physics rate cannot be met and will be reported as rate slips.");
            NetOverlay = config.Bind<bool>("Net", "Overlay", false, "Show the multiplayer diagnostics overlay on the HUD: session state, transport, RTT, jitter, loss, jitter-buffer delay and depth, frame age, sampler/applier microseconds, and stall/rejoin counts. Costs nothing while off, and only redraws when a value actually changes.");
            NetOverlayX = config.Bind<float>("Net", "OverlayX", 24f, "Diagnostics overlay window X position, remembered when you drag it.");
            NetOverlayY = config.Bind<float>("Net", "OverlayY", -24f, "Diagnostics overlay window Y position, remembered when you drag it.");
            NetOverlayCollapsed = config.Bind<bool>("Net", "OverlayCollapsed", false, "Diagnostics overlay collapsed to its title bar.");
            NetLanAddress = config.Bind<string>("Net", "LanAddress", "", "Joiner: the address:port the host printed, for example 192.168.1.42:47772. Host: leave empty to bind port 47772 on every interface, or set a port or address:port to override. Ignored while Net.RendezvousAddress is set, which is the more deliberate choice of the two.");
            NetLanDiscovery = config.Bind<bool>("Net", "LanDiscovery", true, "Joiner: when Net.LanAddress is empty and no invite was pasted, find the host on this subnet instead of refusing to dial - the two of you then only have to agree on the room code. The search is the ordinary connect datagram, already signed with the key that code produces, sent to the broadcast address on port 47772: a host in a different room fails the check and drops it, and only a host holding the same code ever answers. It requires a generated 12-character code, never the default one, so an unconfigured instance cannot wander into someone's session. Discovery does not cross subnets or VLANs and guest Wi-Fi with client isolation blocks it, so an invite remains the way to reach anything further away. Nothing here affects Steam or a rendezvous.");
            NetLanRoomCode = config.Bind<string>("Net", "LanRoomCode", "vpb-lan-test", "Active session code the broker reads this run. Host identity is Net.HostRoomCode - joining another room must not overwrite that. 12 characters, like K7M2-QB94-XTVR. Also accepts a pasted invite.");
            NetHostRoomCode = config.Bind<string>("Net", "HostRoomCode", "", "Selected host room. Survives joining someone else's code. Empty until you make a room.");
            NetJoinRoomCode = config.Bind<string>("Net", "JoinRoomCode", "", "Last join target (room code or invite). Prefills I have a code. Never written into the host inventory.");
            NetRoomBook = config.Bind<string>("Net", "RoomBook", "", "Host inventory and join recents. Bounded list, local only. Empty until the first generated or joined code.");
            NetRendezvousAddress = config.Bind<string>("Net", "RendezvousAddress", "", "address:port of a rendezvous, used to find a peer across the internet without either of you knowing the other's address in advance. THIS IS EMPTY BY DESIGN AND VPB WILL NEVER FILL IT IN. There is no default, no built-in list and no recommended endpoint: you use one you were given, or one you run yourself with \"VpbNet.exe --rendezvous 47773\". Its operator sees two addresses and an opaque token for a few seconds and can never read your session, but it is still someone else's machine - which is exactly why choosing it is your decision and not ours. Empty means direct or LAN only. When set, the room code must be a generated 12-character code, because the rendezvous publishes a token derived from it.");
            NetTransport = config.Bind<string>("Net", "Transport", VpbNetTransportChoice.Steam, "How a session reaches the other player. \"steam\" is the default: Steam carries the connection, neither of you learns the other's IP, nothing has to be forwarded, and the room code alone is enough to connect - but the other player can see which Steam account you are signed into. \"direct\" connects the two machines to each other: you exchange an invite, and each of you learns the other's IP address - no privacy. Both sides must choose the same one. Steam also needs the Steam client running and " + VpbNet.VpbNetSteam.NativeLibrary + " beside VpbNet.exe.");
            NetSteamAppId = config.Bind<int>("Net", "SteamAppId", (int)VpbNet.VpbNetSteam.DefaultAppId, "Steam app id the session identifies itself with. 480 is Spacewar, Valve's public sample app - any signed-in account can use it without owning anything, which is why it is the default and why VPB is not and will not be distributed on Steam. Both sides must match exactly or they cannot see each other. Change it only if you and the other player both own the same Steam game and would rather use that app id.");
            NetSteamApiPath = config.Bind<string>("Net", "SteamApiPath", "", "Full path to a 64-bit " + VpbNet.VpbNetSteam.NativeLibrary + ", or the folder holding one. Empty searches beside VpbNet.exe, then the plugins folder, then the VaM install folder. Only used when Net.Transport is \"steam\".");
            NetSteamIdentityAck = config.Bind<bool>("Net", "SteamIdentityAck", false, "Set once you have been shown, and accepted, that a Steam session lets the other player see which Steam account you are signed into. Until then the session panel shows that warning instead of the Steam connect buttons. Turning this back to false shows it again.");
            NetDirectIpAck = config.Bind<bool>("Net", "DirectIpAck", false, "Set once you have been shown, and accepted, that a Direct session exposes each player's IP address to the other. Until then the session panel shows that warning instead of the Direct host/join cards. Turning this back to false shows it again.");
            NetRoomCodeLocked = config.Bind<bool>("Net", "RoomCodeLocked", false, "While true, the selected host room cannot be replaced. New still adds another room. Protect a code once you have given it out. Shown as Lock / Unlock on the session panel.");
            NetRecordEnabled = config.Bind<bool>("Net", "RecordEnabled", false, "Diagnostic tool. Records the pose stream of one Person - the 17 network controllers, world space, lossless - to a .vpbclip file while this is true. Drive the Person however you like; recording never touches it. Turn off to close the file.");
            NetRecordAtom = config.Bind<string>("Net", "RecordAtom", "", "Atom uid to record. Empty records the first Person-like atom in the scene.");
            NetRecordHz = config.Bind<int>("Net", "RecordHz", 45, "Recording rate in frames per second (1-200). 45 matches the live pose rate.");
            NetReplayEnabled = config.Bind<bool>("Net", "ReplayEnabled", false, "Diagnostic tool. Drives one Person from a recorded .vpbclip - no second Person, no network, identical every run. Reproduces a movement without a live session. Cannot run at the same time as RecordEnabled.");
            NetReplayAtom = config.Bind<string>("Net", "ReplayAtom", "", "Atom uid to drive on replay. Empty drives the first Person-like atom in the scene.");
            NetReplayLoop = config.Bind<bool>("Net", "ReplayLoop", true, "Loop the clip. The seam is ramped over 2.5s rather than snapped, because a jump over 0.5m slings the body.");
            NetReplaySpeed = config.Bind<float>("Net", "ReplaySpeed", 1f, "Replay speed multiplier (0.05-4). Slow motion is useful for watching the applier resolve a fast movement.");
            NetClipFile = config.Bind<string>("Net", "ClipFile", "", "Clip file for record and replay. A bare name resolves inside BepInEx\\plugins\\VPB\\VpbNet\\clips; a full path is used as given. Empty means: record to a timestamped name, replay the newest clip in that folder.");
            NetContentMaxMB = config.Bind<int>("Net", "ContentMaxMB", 4096, "Ceiling on one automatic content fetch, in megabytes. The offer is shown with its total size before anything starts; past this it is refused outright rather than half-downloaded, and the panel says so. 0 switches automatic fetching off without touching your session rules.");
            NetHostSession = config.Bind<bool>("Net", "HostSession", false, "Host a LAN pose session. The joiner uses the address printed here and in the log. Requires Net.Enabled, a scene with two Person atoms (local + remote), and the same room code on both sides. Turns JoinSession off. Turn both off to leave.");
            NetJoinSession = config.Bind<bool>("Net", "JoinSession", false, "Join a LAN pose session at Net.LanAddress with Net.LanRoomCode. Requires Net.Enabled and two Person atoms. Turns HostSession off. Turn both off to leave.");
            NetReopenOnStart = config.Bind<bool>("Net", "ReopenOnStart", false, "Reopen the room by itself on the next launch if you were still hosting or joined when the game closed. Off (default) means a restart always leaves the room shut and only the saved room codes survive, so a crash mid-session never puts you back on the network without asking. Turn it on only for a standing room you want up whenever VaM is.");
            NetSessionUi = config.Bind<bool>("Net", "SessionUi", false, "Show the session panel: host a room or join one, ride avatars, leave. Permission prompts are a separate toast. Turned on when you press Host or Join yourself, but never by a session starting on its own. A live session cannot hide the panel — it collapses to a HUD; Leave ends the session. Closing it persists across restarts.");
            NetSessionUiX = config.Bind<float>("Net", "SessionUiX", 24f, "Session panel X position, remembered when you drag it.");
            NetSessionUiY = config.Bind<float>("Net", "SessionUiY", -80f, "Session panel Y position, remembered when you drag it.");
            NetSessionFlowSeen = config.Bind<bool>("Net", "SessionFlowSeen", false, "True after the first successful Open room or Join. Host/join cards then skip the numbered tutorial and show code + Copy + Open (or paste + Join).");
            NetRulesUiX = config.Bind<float>("Net", "RulesUiX", 420f, "Session rules window X position, remembered when you drag it.");
            NetRulesUiY = config.Bind<float>("Net", "RulesUiY", -140f, "Session rules window Y position, remembered when you drag it.");
            NetRulesUiW = config.Bind<float>("Net", "RulesUiW", 520f, "Session rules window width, remembered when you resize it.");
            NetRulesUiH = config.Bind<float>("Net", "RulesUiH", 400f, "Session rules window height, remembered when you resize it.");
            NetRulesUiCollapsed = config.Bind<bool>("Net", "RulesUiCollapsed", false, "Session rules window collapsed to the title bar.");
            NetLocalAtom = config.Bind<string>("Net", "LocalAtom", "", "Person to ride automatically when a session starts, if the scene has one by that name. Leave empty to start as a spectator and pick from the buttons on the session panel instead. You can change person at any time during the session.");
            NetLockRoot = config.Bind<bool>("Net", "LockRoot", false, "Live session: do not apply the peer's control (root) world position. The remote Person stays where it was placed; limb motion is relative to that slot. Off means root syncs, so grabbing or moving control on one side shows on the other.");
            NetSyncAtoms = config.Bind<bool>("Net", "SyncAtoms", false, "Live session: whether THIS machine will spawn objects the other player adds, or remove ones they delete. OFF BY DEFAULT AND READ THIS FIRST: for a subscene, this machine loads that subscene from its own library, and a subscene can carry plugins, which then run here. Only turn this on with someone you would already trust to hand you a scene file. Person and CustomUnityAsset atoms are refused outright, and every load is named in the log. Edited from Rules in Play with a friend, not here - who may try is the Objects rule.");
            NetRulesLo = config.Bind<int>("Net", "RulesLo", unchecked((int)VpbNet.VpbNetRuleTable.FromPreset(VpbNet.VpbNetRulePreset.WatchTogether).Lo), "Session rules, first half. Two packed words rather than two dozen switches, because these are edited from Rules in Play with a friend and read back there; hand-editing them here is not expected to be pleasant. Each rule is two bits: 0 blocked, 1 ask, 2 allowed. They say what the OTHER person is allowed to do to YOUR avatar and YOUR scene, and nothing about what you may do to theirs - their own copy of these decides that. Neither of you can change the other's.");
            NetRulesHi = config.Bind<int>("Net", "RulesHi", unchecked((int)VpbNet.VpbNetRuleTable.FromPreset(VpbNet.VpbNetRulePreset.WatchTogether).Hi), "Session rules, second half. See RulesLo. The default is the \"watch together\" preset: everything the other person does to their own avatar is mirrored onto the copy of them in your scene, they may move objects and fire triggers in the shared scene, and anything that would rewrite your own body asks you first or is refused outright.");
            PoseDualAnchorAtMe = config.Bind<bool>("Pose", "DualAnchorAtMe", false, "Two-person poses: place the pair where the person taking the first half already stands, instead of at the world coordinates saved in the file. Off by default, which is what the gallery has always done - the file's own coordinates put both bodies exactly where the author had them. Remembered from the last time you used the two-person pose window.");
            NetReplayLockRoot = config.Bind<bool>("Net", "ReplayLockRoot", false, "Clip replay: do not apply the clip's control (root) world position. The Person stays where it was placed; the clip plays in place.");
            LogStateMachineApply = config.Bind<bool>("Logging", "LogStateMachineApply", false, "Trace MacGruber StateMachine storable restore: Atom.Restore entry, Atom.RestoreFromLast match outcome, MVRPluginManager.CreateScriptController insideRestore field, JSONStorable.RestoreFromJSON payload size. Off by default. Enable only when investigating per-instance storable apply failures (ref. issue #52).");

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


            LastGalleryPage = config.Bind<string>("UI", "LastGalleryPage", "", "Last opened Gallery page.");
        }
    }
}
