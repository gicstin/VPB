using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prime31.MessageKit;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public class Gallery : MonoBehaviour
    {
        public static Gallery singleton;

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
            // Do not rebuild the SQLite index here: FileManager.lastPackageRefreshTime is often still
            // DateTime.MinValue, which would publish scan stamp 0 and disable TryQueryGalleryCategoryRows until
            // a full rebuild runs after a real package refresh. Rebuild is scheduled from
            // OnFileManagerRefresh / SetCategories instead.
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
            yield return JSONExtensions.LoadCharacterGenderMap();
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
                if (p == null || p.IsHubMode) continue;
                try { p.RefreshHistoryBrowseIfActive(true); } catch { }
            }
        }

        private void OnFileManagerRefresh()
        {
            if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryManualRefreshOnly)
            {
                if (_hasHadInitialRefresh)
                {
                    LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh SKIPPED (manual refresh only)");
                    try
                    {
                        if (!VpbLocalDatabase.TryRestoreReadyStateIfMetaMatchesInventory())
                            VpbLocalDatabase.ScheduleGalleryIndexRebuildAfterScan();
                    }
                    catch { try { VpbLocalDatabase.ScheduleGalleryIndexRebuildAfterScan(); } catch { } }
                    return;
                }
                LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh INITIAL (manual refresh only, first-run exemption)");
            }

            if (IsSuppressed())
            {
                LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh SKIPPED (suppressed)");
                return;
            }

            DateTime refreshTime = DateTime.MinValue;
            try { refreshTime = FileManager.lastPackageRefreshTime; } catch { }

            // Ignore broadcasts that did not advance the package scan clock (e.g. legacy global pings).
            if (lastObservedPackageRefreshTime != DateTime.MinValue &&
                refreshTime <= lastObservedPackageRefreshTime)
                return;

            LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh TRIGGERED");
            GalleryFileListSnapshotCache.Clear();
            GalleryTagCountSnapshotCache.Clear();
            try
            {
                if (!VpbLocalDatabase.TryRestoreReadyStateIfMetaMatchesInventory())
                    VpbLocalDatabase.ScheduleGalleryIndexRebuildAfterScan();
            }
            catch { try { VpbLocalDatabase.ScheduleGalleryIndexRebuildAfterScan(); } catch { } }
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

                    foreach (var p in panels)
                    {
                        if (p == null) continue;
                        if (p.IsHubMode) continue;

                        bool changed = false;
                        try { changed = p.NotifyPackagesChanged(refreshTime); } catch { changed = true; }

                        if (changed && (p.IsVisible || p.HasLoadedContent))
                        {
                            // Keep hidden panels warm too. Otherwise hidden panels only set
                            // refreshOnNextShow=true and pay a full RefreshFiles() stall on the
                            // next open (~1-2s with large libraries) instead of applying the
                            // incremental delta while the panel is out of view.
                            p.ApplyPackageDelta(added, removed);
                        }
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

        public void AddPanel(GalleryPanel p)
        {
            if (!panels.Contains(p)) panels.Add(p);
        }

        public void RemovePanel(GalleryPanel p)
        {
            if (panels.Contains(p)) panels.Remove(p);
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
                try { VpbLocalDatabase.InvalidateReadyStateOnCategoriesChanged(); } catch { }
                try { VpbLocalDatabase.ScheduleGalleryIndexRebuildAfterScan(); } catch { }
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
                if (string.IsNullOrEmpty(categoryToOpen) && VPBConfig.Instance != null)
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
                    // LastUsed: restore last-viewed category from saved state.
                    try
                    {
                        string last = VPBConfig.ReadLastGalleryCategoryFromDisk();
                        if (string.IsNullOrEmpty(last) && VPBConfig.Instance != null)
                            last = VPBConfig.Instance.LastGalleryCategory;

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
                }
            }
            else if (createTiming != null)
            {
                createTiming.Stop();
            }
        }

        public void Hide()
        {
            foreach(var p in panels)
            {
                p.Hide();
            }
        }

        public void CloseAll()
        {
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
    }
}
