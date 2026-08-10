using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Prime31.MessageKit;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public class Gallery : MonoBehaviour
    {
        public static Gallery singleton;

        private static int _pendingGallerySqlIndexUpdate;

        private DateTime lastObservedPackageRefreshTime = DateTime.MinValue;
        private bool _hasHadInitialRefresh = false;
        
        // Suppress auto-refresh when gallery is loading content (to preserve scroll position and state)
        private static bool suppressAutoRefresh = false;
        private static readonly object suppressLock = new object();
        public static void SuppressAutoRefresh(bool suppress) 
        { 
            lock (suppressLock) 
            { 
                suppressAutoRefresh = suppress; 
                if (suppress)
                {
                    LogUtil.Log("[VPB] Gallery auto-refresh SUPPRESSED");
                }
                else
                {
                    LogUtil.Log("[VPB] Gallery auto-refresh ENABLED");
                }
            } 
        }
        
        public static bool IsSuppressed()
        {
            lock (suppressLock)
            {
                return suppressAutoRefresh;
            }
        }

        public struct Category
        {
            public string name;
            public string extension;
            public string path;
            public List<string> paths;
        }

        /// <summary>Gallery category that lists every indexed VAR internal path (see <c>cat_mem</c> EVERYTHING rows) plus loose-disk roots.</summary>
        public const string EverythingCategoryName = "EVERYTHING";
        /// <summary>Non-file extension token; matches all extensions in refresh / index logic.</summary>
        public const string EverythingExtensionToken = "vpbeverything";

        /// <summary>Extensions used when enumerating loose files on disk for <see cref="EverythingCategoryName"/> (VAR internals already cover package files).</summary>
        public static readonly string[] EverythingLooseDiskExtensions = new[]
        {
            "json", "vam", "vap", "var",
            "cs", "cslist", "dll",
            "assetbundle", "unity3d",
        };

        public static bool IsEverythingCategoryName(string name)
        {
            return string.Equals(name, EverythingCategoryName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsEverythingCategoryExtension(string extensionPipeSeparated)
        {
            if (string.IsNullOrEmpty(extensionPipeSeparated)) return false;
            string[] parts = extensionPipeSeparated.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i] != null ? parts[i].Trim() : "";
                if (string.Equals(p, EverythingExtensionToken, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Non-file extension tokens used as category markers (not real internal-path suffixes).
        /// Must not be applied as <c>LIKE %.<paramref name="extensionToken"/></c> filters.
        /// </summary>
        public static bool IsGalleryPseudoExtensionToken(string extensionToken)
        {
            if (string.IsNullOrEmpty(extensionToken)) return false;
            string e = extensionToken.Trim();
            if (e.Length == 0) return false;
            if (string.Equals(e, EverythingExtensionToken, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(e, "varpkg", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Preview textures inside VARs; omitted from EVERYTHING grid/index.</summary>
        public static bool IsEverythingExcludedPreviewExtension(string extensionNoDot)
        {
            if (string.IsNullOrEmpty(extensionNoDot)) return false;
            return string.Equals(extensionNoDot, "jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extensionNoDot, "jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extensionNoDot, "png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Returns <paramref name="splitExtensions"/> or, for EVERYTHING mode, <see cref="EverythingLooseDiskExtensions"/> for SafeGetFiles loops.</summary>
        public static string[] DiskScanExtensionsOrEverything(string currentExtensionPipe, string[] splitExtensions)
        {
            if (IsEverythingCategoryExtension(currentExtensionPipe) && splitExtensions != null && splitExtensions.Length == 1
                && string.Equals(splitExtensions[0]?.Trim(), EverythingExtensionToken, StringComparison.OrdinalIgnoreCase))
                return EverythingLooseDiskExtensions;
            return splitExtensions;
        }

        /// <summary>
        /// Resolves VaM-relative category roots (e.g. <c>Saves/scene</c>) to on-disk paths for
        /// <see cref="FileManager.SafeGetFiles"/> / <see cref="Directory.Exists"/> (matches VaM cwd semantics).
        /// </summary>
        public static void CollectLooseDiskSearchRoots(List<string> dest, IList<string> categoryPaths, string categoryPath)
        {
            if (dest == null) return;
            dest.Clear();
            if (categoryPaths != null && categoryPaths.Count > 0)
            {
                for (int i = 0; i < categoryPaths.Count; i++)
                    TryAddLooseDiskSearchRoot(dest, categoryPaths[i]);
            }
            else
                TryAddLooseDiskSearchRoot(dest, categoryPath);
        }

        private static void TryAddLooseDiskSearchRoot(List<string> dest, string path)
        {
            if (dest == null || string.IsNullOrEmpty(path)) return;
            string full = null;
            try
            {
                string norm = path.Replace('\\', '/').TrimEnd('/');
                if (Path.IsPathRooted(norm))
                    full = Path.GetFullPath(norm.Replace('/', Path.DirectorySeparatorChar));
                else
                    full = FileManager.GetFullPath(norm.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(full) || !Directory.Exists(full)) return;

            for (int i = 0; i < dest.Count; i++)
            {
                if (string.Equals(dest[i], full, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            dest.Add(full);
        }

        private List<Category> categories = new List<Category>();
        
        // Panels management
        private List<GalleryPanel> panels = new List<GalleryPanel>();

        // IsVisible property checks if ANY panel is visible
        public bool IsVisible 
        {
            get 
            {
                return panels.Any(p => p.IsVisible);
            }
        }

        public int PanelCount => panels.Count;
        public List<GalleryPanel> Panels => panels;
        public bool AnyPanelHasLoadedContent => panels.Any(p => p != null && p.HasLoadedContent);

        /// <summary>
        /// True after the first gallery open this VaM process.
        /// Cold start (process launch): use <see cref="VPBConfig.InitialGalleryCategory"/> + default side rails.
        /// In-session Close/reopen: use Last* browse memory (category, rails, scroll, filters).
        /// </summary>
        public static bool SessionInitialCategoryApplied { get; private set; }

        /// <summary>Alias — in-session browse memory is active (not a fresh VaM process).</summary>
        public static bool SessionBrowseMemoryActive => SessionInitialCategoryApplied;

        public static void MarkSessionInitialCategoryApplied()
        {
            SessionInitialCategoryApplied = true;
        }

        /// <summary>
        /// Unhide existing panes and keep browse place. True when at least one pane had loaded content.
        /// </summary>
        public bool TryRestoreExistingPanelsKeepingState()
        {
            if (panels == null || panels.Count == 0) return false;
            bool any = false;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null) continue;
                if (!p.HasLoadedContent || string.IsNullOrEmpty(p.GetCurrentPath()))
                    continue;
                try
                {
                    p.Show(p.GetTitle(), p.GetCurrentExtension(), p.GetCurrentPath());
                    any = true;
                }
                catch { }
            }
            if (any)
                MarkSessionInitialCategoryApplied();
            return any;
        }

        private Coroutine autoRefreshCoroutine;
        private bool autoRefreshPending;
        private Coroutine startupDeferredAutoRefreshCoroutine;
        private bool startupDeferredAutoRefreshPending;
        private Coroutine genderMapInitCoroutine;

        void Awake()
        {
            singleton = this;
            try
            {
                string gameRoot = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(gameRoot))
                    VpbSqlite3.SetGameInstallRootForNativeDll(gameRoot);
            }
            catch { }
        }

        void Start()
        {
            // Apply saved cap after SQLite path setup; waiting for a new import leaves oversized upgrades untouched.
            VpbLocalDatabase.RequestSceneAtomCacheTrim();
            // Do not rebuild the SQLite index here: FileManager.lastPackageRefreshTime is often still
            // DateTime.MinValue, which would publish scan stamp 0 and disable TryQueryGalleryCategoryRows until
            // a full rebuild runs after a real package refresh. Rebuild is scheduled from
            // OnFileManagerRefresh / SetCategories instead.
        }

        void Update()
        {
            DrainPendingSqlIndexUpdate();
        }

        void OnEnable()
        {
            MessageKit.addObserver(MessageDef.FileManagerRefresh, OnFileManagerRefresh);
            MessageKit.addObserver(MessageDef.GalleryItemUsageRecorded, OnGalleryItemUsageRecorded);
            if (genderMapInitCoroutine == null)
                genderMapInitCoroutine = StartCoroutine(InitCharacterGenderMapEarly());
        }

        void OnDisable()
        {
            MessageKit.removeObserver(MessageDef.FileManagerRefresh, OnFileManagerRefresh);
            MessageKit.removeObserver(MessageDef.GalleryItemUsageRecorded, OnGalleryItemUsageRecorded);
            if (genderMapInitCoroutine != null)
            {
                StopCoroutine(genderMapInitCoroutine);
                genderMapInitCoroutine = null;
            }
        }

        private IEnumerator InitCharacterGenderMapEarly()
        {
            // Start immediately so this heavy task can overlap with startup work.
            // READY still waits on completion via StartupSettleUpdate pending checks.
            VamStartupProfiler.BeginScope("gender_map_load");
            yield return JSONExtensions.LoadCharacterGenderMap();
            VamStartupProfiler.EndScope("gender_map_load");
            genderMapInitCoroutine = null;
        }

        private void OnGalleryItemUsageRecorded()
        {
            if (IsSuppressed()) return;
            var ps = panels;
            if (ps == null) return;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                if (p == null) continue;
                try { p.RefreshHistoryBrowseIfActive(true); } catch { }
            }
        }

        private void OnFileManagerRefresh()
        {
            VamStartupProfiler.Milestone("Gallery.OnFileManagerRefresh_enter");
            bool pendingPackageDelta = false;
            try { pendingPackageDelta = FileManager.HasPendingGalleryPackageDelta(); } catch { }

            if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryManualRefreshOnly)
            {
                if (_hasHadInitialRefresh)
                {
                    if (!pendingPackageDelta)
                    {
                        LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh SKIPPED (manual refresh only)");
                        return;
                    }
                    try
                    {
                        LogUtil.Log("[VPB.Gallery.Delta] OnFileManagerRefresh manualRefreshOnly -> pending delta apply");
                    }
                    catch { }
                }
                else
                {
                    LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh INITIAL (manual refresh only, first-run exemption)");
                }
            }

            if (IsSuppressed())
            {
                LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh SKIPPED (suppressed)");
                return;
            }

            DateTime refreshTime = DateTime.MinValue;
            try { refreshTime = FileManager.lastPackageRefreshTime; } catch { }

            // Ignore broadcasts that did not advance the package scan clock (e.g. legacy global pings).
            // Still run when a pending add/remove delta exists (hub download under manual-refresh-only).
            if (lastObservedPackageRefreshTime != DateTime.MinValue &&
                refreshTime <= lastObservedPackageRefreshTime &&
                !pendingPackageDelta)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] OnFileManagerRefresh SKIPPED stale clock scanTime="
                        + refreshTime.ToString("o") + " lastObserved=" + lastObservedPackageRefreshTime.ToString("o"));
                }
                catch { }
                VamStartupProfiler.Milestone("Gallery.OnFileManagerRefresh_skipped_stale_clock");
                return;
            }

            LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh TRIGGERED");
            if (pendingPackageDelta)
                GalleryFileListSnapshotCache.Clear();
            else
                GalleryFileListSnapshotCache.InvalidateAll();

            // Process-lifetime static L1 caches: drop on package library change (stability / bound memory).
            try { GallerySortManager.ClearSceneDependencyCache(); } catch { }
            try { PackageHidePrefs.InvalidateSceneJsonCountCache(); } catch { }
            try { UIDraggableItem.ClearGlobalRegionCache(); } catch { }
            try { LooseVapGenderProbe.InvalidateMemoryCache(); } catch { }
            try { VpbLocalDatabase.ClearDeepDirMtimeCache(); } catch { }

            // VAR scan rewrote per-uid cslist-referenced rows; drop the in-memory set so the
            // next read sees the fresh SQLite state.
            try
            {
                if (panels != null)
                {
                    for (int i = 0; i < panels.Count; i++)
                    {
                        var p = panels[i];
                        if (p != null)
                        {
                            try { p.InvalidateCslistReferencedCache(); } catch { }
                        }
                    }
                }
            }
            catch { }

            lastObservedPackageRefreshTime = refreshTime;

            _hasHadInitialRefresh = true;
            try { TryQueueBaMigrationPrompt(); } catch { }

            if (!LogUtil.IsStartupReadyLogged())
            {
                startupDeferredAutoRefreshPending = true;
                if (startupDeferredAutoRefreshCoroutine == null)
                    startupDeferredAutoRefreshCoroutine = StartCoroutine(RunDeferredAutoRefreshAfterStartupReady());
                return;
            }

            if (autoRefreshCoroutine != null)
            {
                autoRefreshPending = true;
                return;
            }
            autoRefreshCoroutine = StartCoroutine(AutoRefreshAfterPackageScan());
        }

        private IEnumerator RunDeferredAutoRefreshAfterStartupReady()
        {
            while (!LogUtil.IsStartupReadyLogged())
                yield return null;

            startupDeferredAutoRefreshCoroutine = null;
            if (!startupDeferredAutoRefreshPending) yield break;
            startupDeferredAutoRefreshPending = false;

            if (autoRefreshCoroutine != null)
            {
                autoRefreshPending = true;
                yield break;
            }
            autoRefreshCoroutine = StartCoroutine(AutoRefreshAfterPackageScan());
        }

        public static bool HasStartupDeferredWork()
        {
            var g = singleton;
            if (g == null) return false;
            if (g.genderMapInitCoroutine != null) return true;
            if (g.startupDeferredAutoRefreshCoroutine != null) return true;
            if (g.autoRefreshCoroutine != null) return true;
            if (g.panels != null)
            {
                for (int i = 0; i < g.panels.Count; i++)
                {
                    var p = g.panels[i];
                    if (p != null && p.HasDeferredStartupRefreshPending)
                        return true;
                }
            }
            return false;
        }

        private IEnumerator AutoRefreshAfterPackageScan()
        {
            yield return null;
            try
            {
                while (true)
                {
                    autoRefreshPending = false;

                    DateTime refreshTime = DateTime.MinValue;
                    try { refreshTime = FileManager.lastPackageRefreshTime; } catch { }

                    // Snapshot the delta lists so all panels see the same set of changes.
                    List<VarPackage> added = null;
                    List<VarPackage> removed = null;
                    try
                    {
                        added  = new List<VarPackage>(FileManager.lastAddedPackages);
                        removed = new List<VarPackage>(FileManager.lastRemovedPackages);
                    }
                    catch { }

                    bool hasPackageDelta = (added != null && added.Count > 0) || (removed != null && removed.Count > 0);
                    try
                    {
                        LogUtil.Log("[VPB.Gallery.Delta] AutoRefresh scanTime=" + refreshTime.ToString("o")
                            + " added=" + (added != null ? added.Count : 0)
                            + " removed=" + (removed != null ? removed.Count : 0)
                            + " hasDelta=" + (hasPackageDelta ? "1" : "0")
                            + " pending=" + (autoRefreshPending ? "1" : "0"));
                    }
                    catch { }

                    bool ackDelta = false;
                    foreach (var p in panels)
                    {
                        if (p == null) continue;

                        if (!hasPackageDelta)
                        {
                            bool changed = false;
                            try { changed = p.NotifyPackagesChanged(refreshTime); } catch { changed = true; }
                            if (!changed) continue;
                            if (!p.HasLoadedContent && (p.HasDeferredStartupRefreshPending || p.IsStartupInitialRefreshInProgress))
                                continue;
                            if (changed && (p.IsVisible || p.HasLoadedContent))
                            {
                                try
                                {
                                    if (p.ApplyPackageDelta(added, removed))
                                        ackDelta = true;
                                }
                                catch { }
                            }
                            continue;
                        }

                        if (hasPackageDelta)
                        {
                            try
                            {
                                if (p.ApplyPackageDelta(added, removed))
                                    ackDelta = true;
                            }
                            catch (Exception ex)
                            {
                                try { LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta error: " + ex.Message); } catch { }
                            }
                        }
                    }

                    if (hasPackageDelta && ackDelta)
                    {
                        try { FileManager.AckPackageGalleryDeltaConsumed(); } catch { }
                    }
                    else if (hasPackageDelta && !ackDelta)
                    {
                        try { LogUtil.Log("[VPB.Gallery.Delta] AutoRefresh kept pending delta (no panel applied changes)"); } catch { }
                    }

                    if (!autoRefreshPending) break;
                    yield return null;
                }
            }
            finally
            {
                autoRefreshCoroutine = null;
                autoRefreshPending = false;
            }
        }

        void OnDestroy()
        {
            // Panels clean themselves up usually, but we can ensure destruction
            foreach (var p in panels.ToList())
            {
                if (p != null && p.gameObject != null) Destroy(p.gameObject);
            }
            panels.Clear();
        }

        /// <summary>Next slot for extra panes after <see cref="GalleryPanel.PrimaryPanelId"/>. First pane always gets primary so SQL filter memory survives Close.</summary>
        private int _nextExtraPanelSlot = 1;

        public void AddPanel(GalleryPanel p)
        {
            if (p == null || panels.Contains(p)) return;
            // Empty list → primary slot (durable). Extra panes get monotonic ids (not recycled) so concurrent panes keep distinct filter rows.
            string id = panels.Count == 0
                ? GalleryPanel.PrimaryPanelId
                : ("panel_" + (_nextExtraPanelSlot++));
            try { p.AssignStablePanelId(id); } catch { }
            panels.Add(p);
        }

        public void RemovePanel(GalleryPanel p)
        {
            if (panels.Contains(p)) panels.Remove(p);
            // All panes gone → next create is primary again (browse memory restores).
            if (panels.Count == 0)
                _nextExtraPanelSlot = 1;
        }

        public void Init()
        {
            // VamHookPlugin calls this on hotkey if panels are hidden or empty.
            // We no longer automatically create a pane here to avoid ghosts.
        }

        public void SetCategories(List<Category> cats)
        {
            categories = cats;
            bool hydrated = false;
            try { hydrated = VpbLocalDatabase.TryRestoreReadyStateIfMetaMatchesInventory(); } catch { }
            if (!hydrated)
            {
                // Cold start: package inventory is still registering when categories first bind.
                // Forcing rebuild here bypasses sqlRestore and blocks startup for ~15s. Scan completion
                // calls ScheduleGalleryIndexUpdateAfterScan() once registry is complete.
                bool startupReady = false;
                try { startupReady = LogUtil.IsStartupReadyLogged() || LogUtil.IsReadyLogged(); } catch { }
                if (startupReady)
                {
                    try { VpbLocalDatabase.InvalidateReadyStateOnCategoriesChanged(); } catch { }
                    try { VpbLocalDatabase.ScheduleGalleryIndexUpdateAfterScan(forceFullRebuild: true); } catch { }
                }
            }
            foreach (var p in panels)
            {
                p.SetCategories(categories);
            }
        }

        internal List<Category> CloneCategoriesForIndex()
        {
            int n = categories != null ? categories.Count : 0;
            var r = new List<Category>(n);
            if (categories == null) return r;
            for (int i = 0; i < categories.Count; i++)
            {
                Category c = categories[i];
                Category copy = new Category();
                copy.name = c.name;
                copy.extension = c.extension;
                copy.path = c.path;
                copy.paths = c.paths != null ? new List<string>(c.paths) : null;
                r.Add(copy);
            }
            return r;
        }

        internal Category FindCategoryByName(string title)
        {
            if (string.IsNullOrEmpty(title) || categories == null) return new Category();
            for (int i = 0; i < categories.Count; i++)
            {
                if (string.Equals(categories[i].name, title, StringComparison.OrdinalIgnoreCase))
                    return categories[i];
            }
            return new Category();
        }

        public const int MaxPanels = 20;

        private static System.Diagnostics.Stopwatch _pendingCreatePaneStopwatch;

        /// <summary>Call when the user invokes Create Gallery Pane (hotkey / UI) so timing includes category init until the new pane's grid is ready.</summary>
        public static void MarkCreateGalleryPaneRequested()
        {
            _pendingCreatePaneStopwatch = System.Diagnostics.Stopwatch.StartNew();
        }

        internal static System.Diagnostics.Stopwatch TakePendingCreatePaneStopwatch()
        {
            var s = _pendingCreatePaneStopwatch;
            _pendingCreatePaneStopwatch = null;
            return s;
        }

        public void ClonePanel(GalleryPanel original, bool toRight)
        {
            if (panels.Count >= MaxPanels)
            {
                // Optionally warn user?
                return;
            }

            var cloneTiming = System.Diagnostics.Stopwatch.StartNew();

            GameObject go = new GameObject("GalleryPanel_Clone");
            GalleryPanel p = go.AddComponent<GalleryPanel>();
            p.importSidebarInitAsClone = true;

            p.Init();
            // Force floating mode for clones
            p.SetFixedLocally(false);
            
            p.SetCategories(original.categories);
            
            // Sync state (clone duplicates gallery browse context; do not copy Settings side-tab — original uses internal session/list state clone never synced)
            p.SetFilters(original.GetCurrentPath(), original.GetCurrentExtension(), original.GetCurrentCreator());
            ContentType? cloneLeft = original.GetLeftActiveContent();
            ContentType? cloneRight = original.GetRightActiveContent();
            if (cloneLeft == ContentType.Settings) cloneLeft = null;
            if (cloneRight == ContentType.Settings) cloneRight = null;
            p.SetLeftActiveContent(cloneLeft);
            p.SetRightActiveContent(cloneRight);
            p.CopyImportSidebarStateFrom(original);
            p.SetFollowMode(original.GetFollowMode());
            
            // Sync size
            RectTransform originalRT = original.GetBackgroundRT();
            RectTransform pRT = p.GetBackgroundRT();
            if (originalRT != null && pRT != null)
            {
                // If original is fixed, it has no sizeDelta (it's stretched in ScreenSpaceOverlay).
                // Clones are always floating, so use the default 1200x800 size for fixed-to-floating clones.
                if (original.isFixedLocally)
                    pRT.sizeDelta = new Vector2(1200, 800);
                else
                    pRT.sizeDelta = originalRT.sizeDelta;
            }

            // Sync position and rotation
            Camera cam = Camera.main;
            Transform camTrans = cam != null ? cam.transform : null;
            if (camTrans == null && SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                camTrans = SuperController.singleton.centerCameraTarget.transform;

            if (camTrans != null)
            {
                Vector3 camPos = camTrans.position;
                Vector3 toOriginal;
                
                if (original.isFixedLocally)
                {
                    // Fixed panels are in ScreenSpaceOverlay. Place the floating clone directly 1.5m in front of the user.
                    // We don't use the "cloning principle" (offset) here because the source is screen-pinned, not world-placed.
                    toOriginal = camTrans.forward * 1.5f;
                    p.canvas.transform.position = camPos + toOriginal;
                    p.canvas.transform.rotation = Quaternion.LookRotation(toOriginal, Vector3.up);
                }
                else
                {
                    // For floating panels, use the standard cloning principle (place it to the side)
                    toOriginal = original.canvas.transform.position - camPos;
                    float radius = toOriginal.magnitude;
                    if (radius < 0.1f) radius = 0.1f;

                    float width = originalRT != null ? originalRT.sizeDelta.x * 0.001f : 1.2f;
                    float padding = 0.05f;
                    float angle = ((width + padding) / radius) * Mathf.Rad2Deg;
                    if (!toRight) angle = -angle;

                    Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up);
                    Vector3 toNew = rot * toOriginal;

                    p.canvas.transform.position = camPos + toNew;
                    p.canvas.transform.rotation = Quaternion.LookRotation(toNew, Vector3.up);
                }
            }
            else
            {
                if (original.isFixedLocally)
                {
                    p.canvas.transform.rotation = original.canvas.transform.rotation;
                    p.canvas.transform.position = original.canvas.transform.position + new Vector3(-1.25f, 0, 0);
                }
                else
                {
                    p.canvas.transform.rotation = original.canvas.transform.rotation;
                    float width = originalRT != null ? originalRT.sizeDelta.x * 0.001f : 1.2f;
                    float padding = 0.05f;
                    Vector3 offset = original.canvas.transform.right * (width + padding);
                    if (!toRight) offset = -offset;
                    p.canvas.transform.position = original.canvas.transform.position + offset;
                }
            }
            
            p.hasBeenPositioned = true;
            p.BeginPaneLoadTiming(cloneTiming, "clone");
            p.Show(original.GetTitle(), original.GetCurrentExtension(), original.GetCurrentPath());
        }

        public void Show(string title, string extension, string path)
        {
            LogUtil.Log("[Gallery] Gallery.Show: title='" + title + "' path='" + path + "' panelCount=" + panels.Count + " anyLoaded=" + AnyPanelHasLoadedContent);
            VpbPerfDiag.LogTransition("Gallery.Show", "title=" + title + " panels=" + panels.Count);
            if (panels.Count == 0)
            {
                // Create the panel without its internal Show() so we can call Show() exactly
                // once below with the caller's own title/extension/path.  This avoids the old
                // double-Show pattern (CreatePane→p.Show + Show again) that caused two content
                // loads, duplicate thumbnail coroutines and a scroll-position reset on startup.
                CreatePane(showAfterCreate: false);
                if (panels.Count > 0)
                    panels[0].Show(title, extension, path);
            }
            else
            {
                // Show ALL panes: if visible, update with caller's category; if hidden, restore session
                foreach(var p in panels)
                {
                    if (p.IsVisible)
                    {
                        // Panel is already visible: update it with the caller's category
                        p.Show(title, extension, path);
                    }
                    else
                    {
                        // Panel is hidden: restore previous state unless it has never loaded content
                        if (!p.HasLoadedContent || string.IsNullOrEmpty(p.GetCurrentPath()))
                        {
                             p.Show(title, extension, path);
                        }
                        else
                        {
                             p.Show(p.GetTitle(), p.GetCurrentExtension(), p.GetCurrentPath());
                        }
                    }
                }
            }
            // Any intentional Show (toggle, hotkey, CreatePane) consumes Initial for this process.
            MarkSessionInitialCategoryApplied();
        }

        public void CreatePane(string forcedInitialCategory = null, bool showAfterCreate = true)
        {
            if (panels.Count >= MaxPanels)
            {
                return;
            }

            System.Diagnostics.Stopwatch createTiming = TakePendingCreatePaneStopwatch();

            GameObject go = new GameObject("GalleryPanel_New");
            GalleryPanel p = go.AddComponent<GalleryPanel>();
            p.Init(); // Undocked
            
            p.SetCategories(categories);

            // Position relative to viewer
            if (SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
            {
                Transform cameraTransform = SuperController.singleton.centerCameraTarget.transform;
                p.canvas.transform.position = cameraTransform.position + cameraTransform.forward * 0.8f;
                p.canvas.transform.rotation = cameraTransform.rotation;
            }
            
            // Show initial category
            if (categories.Count > 0)
            {
                Gallery.Category initial = categories[0];

                string categoryToOpen = forcedInitialCategory;
                // Cold start: InitialGalleryCategory (Scenes / …). In-session recreate: leave null → Last* below.
                if (string.IsNullOrEmpty(categoryToOpen) && VPBConfig.Instance != null && !SessionBrowseMemoryActive)
                    categoryToOpen = VPBConfig.Instance.ResolveInitialGalleryCategoryName();

                if (!string.IsNullOrEmpty(categoryToOpen))
                {
                    for (int i = 0; i < categories.Count; i++)
                    {
                        if (string.Equals(categories[i].name, categoryToOpen, StringComparison.OrdinalIgnoreCase))
                        {
                            initial = categories[i];
                            break;
                        }
                    }
                }
                else
                {
                    // LastUsed setting on cold start, or any in-session CreatePane after Close.
                    try
                    {
                        string last = null;
                        if (VPBConfig.Instance != null && !string.IsNullOrEmpty(VPBConfig.Instance.LastGalleryCategory))
                            last = VPBConfig.Instance.LastGalleryCategory;
                        if (string.IsNullOrEmpty(last))
                            last = VPBConfig.ReadLastGalleryCategoryFromDisk();

                        if (!string.IsNullOrEmpty(last))
                        {
                            last = last.Trim();
                            if (last.StartsWith("Category ", StringComparison.OrdinalIgnoreCase))
                                last = last.Substring("Category ".Length);
                            else if (last.StartsWith("Category", StringComparison.OrdinalIgnoreCase) && last.Length > "Category".Length)
                                last = last.Substring("Category".Length);

                            if (last.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                                last = last.Substring("Preset ".Length);
                            else if (last.StartsWith("Preset", StringComparison.OrdinalIgnoreCase) && last.Length > "Preset".Length)
                                last = last.Substring("Preset".Length);

                            last = last.Trim();

                            if (string.Equals(last, "Scene", StringComparison.OrdinalIgnoreCase))
                                last = "Scenes";

                            for (int i = 0; i < categories.Count; i++)
                            {
                                if (string.Equals(categories[i].name, last, StringComparison.OrdinalIgnoreCase))
                                {
                                    initial = categories[i];
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (showAfterCreate)
                {
                    if (createTiming != null)
                        p.BeginPaneLoadTiming(createTiming, "create");
                    p.Show(initial.name, initial.extension, initial.path);
                    // Startup auto-pane / Create Pane consumed Initial for this process.
                    MarkSessionInitialCategoryApplied();
                }
            }
            else if (createTiming != null)
            {
                createTiming.Stop();
            }
        }

        /// <summary>When <see cref="VPBConfig.GalleryCollapseOnSceneLaunch"/> is on: fixed panes slide to dock edge; floating panes hide.</summary>
        public static void CollapsePanelsOnSceneLaunch()
        {
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryCollapseOnSceneLaunch) return;
                if (singleton == null || singleton.panels == null) return;
                foreach (var p in singleton.panels)
                {
                    if (p == null) continue;
                    bool visible = false;
                    try { visible = p.IsVisible; } catch { }
                    if (!visible) continue;
                    if (p.isFixedLocally)
                    {
                        try { p.SetCollapsed(true); } catch { }
                    }
                    else
                    {
                        try { p.Hide(); } catch { }
                    }
                }
            }
            catch { }
        }

        public void Hide()
        {
            VpbPerfDiag.LogTransition("Gallery.Hide", "panels=" + panels.Count);
            foreach(var p in panels)
            {
                p.Hide();
            }
        }

        public void CloseAll()
        {
            VpbPerfDiag.LogTransition("Gallery.CloseAll", "panels=" + panels.Count);
            foreach (var p in panels.ToList())
            {
                if (p == null) continue;
                p.Close();
            }
        }

        public void BringAllToFront()
        {
            Transform camTrans = Camera.main != null ? Camera.main.transform : null;
            if (camTrans == null && SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                camTrans = SuperController.singleton.centerCameraTarget.transform;

            if (camTrans == null) return;

            float dist = 2.0f;
            if (VPBConfig.Instance != null) dist = VPBConfig.Instance.BringToFrontDistance;

            Vector3 basePos = camTrans.position + camTrans.forward * dist;
            Vector3 right = camTrans.right;

            int count = panels.Count;
            float spacing = 0.35f;
            float start = -(count - 1) * 0.5f * spacing;

            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (p == null || p.canvas == null) continue;
                if (p.isFixedLocally) continue;

                Vector3 pos = basePos + right * (start + i * spacing);
                p.canvas.transform.position = pos;
                p.canvas.transform.rotation = Quaternion.LookRotation(pos - camTrans.position, Vector3.up);

                try { p.ResetFollowOffsets(); } catch { }
            }

            // Bring Context Menu to front if it is currently open
            try
            {
                var ctxMenu = ContextMenuPanel.ExistingInstance;
                if (ctxMenu != null && ctxMenu.gameObject.activeSelf)
                {
                    Transform ctxCanvas = ctxMenu.transform.Find("Canvas");
                    if (ctxCanvas != null && ctxCanvas.gameObject.activeSelf)
                    {
                        Vector3 contextPos = basePos + right * (start + panels.Count * spacing);
                        ctxMenu.transform.position = contextPos;
                        ctxMenu.transform.rotation = Quaternion.LookRotation(contextPos - camTrans.position, Vector3.up);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// After <see cref="FileManager.NotifyInstalled"/> resyncs path dictionaries, refresh cached
        /// <see cref="FileEntry.Path"/> only for rows whose package UID is in <paramref name="packageUids"/>.
        /// </summary>
        public static void NotifyDisplayedPathsAfterPackagePathChanges(ICollection<string> packageUids)
        {
            if (packageUids == null || packageUids.Count == 0) return;
            if (singleton == null) return;
            List<GalleryPanel> pl = singleton.Panels;
            if (pl == null) return;
            var set = new HashSet<string>(packageUids, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pl.Count; i++)
            {
                GalleryPanel p = pl[i];
                if (p == null) continue;
                try { p.RefreshDisplayedVarPathsAfterPackageMoves(set); } catch { }
            }
        }

        /// <summary>
        /// After SQLite <c>pkg.var_path</c> was corrected for Explorer moves (index reuse, no rebuild).
        /// Clears Path side-panel folder counts and refreshes displayed VAR paths for changed UIDs.
        /// </summary>
        public static void NotifyAfterPkgVarPathsSynced(ICollection<string> packageUids)
        {
            if (singleton == null) return;
            List<GalleryPanel> pl = singleton.Panels;
            if (pl != null)
            {
                for (int i = 0; i < pl.Count; i++)
                {
                    GalleryPanel p = pl[i];
                    if (p == null) continue;
                    try { p.InvalidateCachedPathTabs(); } catch { }
                }
            }
            if (packageUids != null && packageUids.Count > 0)
                NotifyDisplayedPathsAfterPackagePathChanges(packageUids);
        }

        /// <summary>
        /// Refreshes row bindings for visible non-hub panels only (no full list rebuild).
        /// Useful for badge-only state changes while auto-refresh is suppressed.
        /// </summary>
        public static void RefreshVisiblePanelRowVisuals()
        {
            if (singleton == null) return;
            List<GalleryPanel> pl = singleton.Panels;
            if (pl == null) return;
            for (int i = 0; i < pl.Count; i++)
            {
                GalleryPanel p = pl[i];
                if (p == null) continue;
                try { p.RefreshVisibleGridVisualsOnly(); } catch { }
            }
        }

        private bool _baMigrationPromptPending;

        private void TryQueueBaMigrationPrompt()
        {
            if (VPBConfig.Instance == null || VPBConfig.Instance.BaMigrationPromptDismissed)
            {
                LogUtil.Log("[VPB BA] TryQueueBaMigrationPrompt: skipped (dismissed=" + (VPBConfig.Instance?.BaMigrationPromptDismissed) + ")");
                return;
            }
            if (!BaImporter.TryDetectBaDataDir(out _))
            {
                LogUtil.Log("[VPB BA] TryQueueBaMigrationPrompt: BA data dir not found — prompt suppressed");
                return;
            }
            _baMigrationPromptPending = true;
            LogUtil.Log("[VPB BA] TryQueueBaMigrationPrompt: prompt pending — will fire next time gallery panel opens");
        }

        // Called from GalleryPanel.Show() when a panel becomes visible
        internal static bool TryConsumeBaMigrationPromptPending()
        {
            if (singleton == null || !singleton._baMigrationPromptPending) return false;
            singleton._baMigrationPromptPending = false;
            LogUtil.Log("[VPB BA] TryConsumeBaMigrationPromptPending: consuming pending prompt");
            return true;
        }

        internal IEnumerator DeferredGalleryIndexRebuildCoroutine(float delaySec)
        {
            try { VpbLocalDatabase.SetGalleryIndexBuildIndicatorPending(true); } catch { }
            if (delaySec > 0f)
            {
                try { VamStartupProfiler.Milestone("sql_rebuild_delay sec=" + delaySec.ToString("0.##")); } catch { }
                yield return new WaitForSeconds(delaySec);
            }
            while (FileManager.IsBulkDeepScanActive)
                yield return null;
            if (VpbLocalDatabase.TrySkipGalleryIndexRebuild())
            {
                try { VpbLocalDatabase.SetGalleryIndexBuildIndicatorPending(false); } catch { }
                yield break;
            }
            try
            {
                LogUtil.Log(VamStartupOptimizations.LogTag + " running deferred gallery SQLite index update");
                VamStartupProfiler.Milestone("sql_rebuild_deferred_run_begin");
            }
            catch { }
            // Deferred coroutine runs post-startup-ready; force routes to full-rebuild instead of incremental.
            try { VpbLocalDatabase.ScheduleGalleryIndexUpdateAfterScan(forceFullRebuild: true); } catch { }
        }

        /// <summary>After SQLite index patch completes, refresh visible gallery grids that use SQL.</summary>
        internal static void NotifyGalleryIndexUpdateCompleted()
        {
            Interlocked.Exchange(ref _pendingGallerySqlIndexUpdate, 1);
        }

        /// <summary>Drain pending SQL-index gallery refresh (main thread only).</summary>
        internal static void DrainPendingSqlIndexUpdate()
        {
            if (Interlocked.CompareExchange(ref _pendingGallerySqlIndexUpdate, 0, 1) != 1) return;
            bool scanning = false;
            try { scanning = FileManager.IsScanning; } catch { }
            if (scanning)
            {
                Interlocked.Exchange(ref _pendingGallerySqlIndexUpdate, 1);
                return;
            }
            var ps = singleton != null ? singleton.panels : null;
            if (ps == null) return;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                if (p == null) continue;
                try { p.OnGallerySqlIndexUpdated(); } catch { }
            }
        }
    }
}
