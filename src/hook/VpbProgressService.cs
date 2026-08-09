using System;
using UnityEngine;

namespace VPB
{
        /// <summary>Unified long-task progress for external <see cref="VpbBusyChrome"/>.</summary>
    internal static class VpbProgressService
    {
        internal enum GalleryPhase
        {
            Pending,
            Packages,
            Indexes,
            Incremental
        }

        internal struct DisplaySnapshot
        {
            public bool Visible;
            public float Progress01;
            public string Title;
            public string Subtitle;
            public bool ShowMovingStrip;
            public bool Cancellable;
            /// <summary>True when Unity main thread may stall — strip off; OS heartbeat on.</summary>
            public bool Blocking;
        }

        private static volatile bool s_DeepScanActive;
        private static volatile int s_DeepScanDone;
        private static volatile int s_DeepScanTotal;

        private static volatile bool s_GalleryActive;
        private static volatile bool s_GalleryPending;
        private static volatile GalleryPhase s_GalleryPhase;
        private static volatile int s_GalleryDone;
        private static volatile int s_GalleryTotal;

        private static volatile bool s_ManifestActive;
        private static volatile bool s_GalleryUiActive;
        private static volatile bool s_AwaitingGalleryUi;
        private static volatile bool s_GalleryUiReady;
        /// <summary>True after any gallery panel finished its first grid load this VaM session (survives panel close/recreate).</summary>
        private static volatile bool s_GalleryUiEverReady;

        private static readonly object s_StartupStateSync = new object();
        private static bool s_StartupCycleInProgress;
        private static bool s_StartupCycleTouched;
        private static DateTime s_ReadyUntilUtc = DateTime.MinValue;
        private static double s_LastMeasuredStartupSeconds;
        private static double s_LastScanAndIndexSeconds;
        private static DateTime s_StartupCycleStartedUtc = DateTime.MinValue;

        private static volatile bool s_BulkZstdActive;
        private static volatile bool s_BulkZstdDecompress;
        private static volatile int s_BulkZstdDone;
        private static volatile int s_BulkZstdTotal;
        private static volatile string s_BulkZstdCurrentFile;

        private static volatile bool s_SceneLoadActive;
        private static volatile bool s_SceneLoadNativePhase;
        private static volatile string s_SceneLoadTitle;
        private static volatile string s_SceneLoadSubtitle;
        private static volatile int s_SceneLoadDepDone;
        private static volatile int s_SceneLoadDepTotal;

        private static volatile bool s_BrowseRefreshActive;
        private static volatile int s_BrowseRefreshDone;
        private static volatile int s_BrowseRefreshTotal;
        private static volatile string s_BrowseRefreshPhase;

        private static readonly object s_BlockingSync = new object();
        private static int s_BlockingDepth;
        private static string s_BlockingTitle;
        private static string s_BlockingSubtitle;

        internal const string DefaultBlockingSubtitle = "VaM may freeze — working…";

        internal static bool IsDeepScanActive => s_DeepScanActive;

        internal static bool IsGalleryProgressActive =>
            s_GalleryActive || s_GalleryPending;

        internal static bool IsBrowseRefreshActive => s_BrowseRefreshActive;

        internal static bool IsBlocking
        {
            get { lock (s_BlockingSync) { return s_BlockingDepth > 0; } }
        }

        internal static bool IsStartupProgressVisible
        {
            get
            {
                if (!IsStartupOverlayEnabled()) return false;
                if (s_ManifestActive) return true;
                if (s_DeepScanActive) return true;
                if (s_GalleryActive || s_GalleryPending) return true;
                if (!s_GalleryUiEverReady
                    && (s_GalleryUiActive || (s_AwaitingGalleryUi && !s_GalleryUiReady))) return true;
                if (DateTime.UtcNow < s_ReadyUntilUtc) return true;
                return false;
            }
        }

        internal static bool SuppressHookHeaderProgress => IsStartupProgressVisible;

        internal static void EnsureOverlay()
        {
            try { VpbBusyChrome.EnsureCreated(); } catch { }
        }

        internal static bool TryGetActiveDisplaySnapshot(out DisplaySnapshot snapshot)
        {
            if (TryGetSceneLoadDisplaySnapshot(out snapshot))
            {
                ApplyBlockingOverlay(ref snapshot);
                return true;
            }
            if (TryGetBlockingDisplaySnapshot(out snapshot))
                return true;
            if (TryGetStartupDisplaySnapshot(out snapshot))
            {
                ApplyBlockingOverlay(ref snapshot);
                return true;
            }
            if (TryGetBulkZstdDisplaySnapshot(out snapshot))
            {
                ApplyBlockingOverlay(ref snapshot);
                return true;
            }
            if (TryGetBrowseRefreshDisplaySnapshot(out snapshot))
            {
                ApplyBlockingOverlay(ref snapshot);
                return true;
            }
            return false;
        }

        internal static void BeginSceneLoadPrep(string displayName, string actionVerb = "Loading")
        {
            s_SceneLoadActive = true;
            s_SceneLoadNativePhase = false;
            string verb = string.IsNullOrEmpty(actionVerb) ? "Loading" : actionVerb;
            s_SceneLoadTitle = string.IsNullOrEmpty(displayName)
                ? verb + " scene"
                : (verb + " " + displayName);
            s_SceneLoadSubtitle = "Preparing — please wait";
            s_SceneLoadDepDone = 0;
            s_SceneLoadDepTotal = 0;
            EnsureOverlay();
        }

        internal static void ReportSceneLoadPrepPhase(string subtitle)
        {
            if (!s_SceneLoadActive || s_SceneLoadNativePhase) return;
            s_SceneLoadSubtitle = string.IsNullOrEmpty(subtitle) ? "Preparing — please wait" : subtitle;
        }

        internal static void ReportSceneLoadDepProgress(int done, int total)
        {
            if (!s_SceneLoadActive || s_SceneLoadNativePhase) return;
            s_SceneLoadDepDone = Math.Max(0, done);
            s_SceneLoadDepTotal = Math.Max(0, total);
            if (total > 0)
                s_SceneLoadSubtitle = "Installing packages " + done + "/" + total;
        }

        internal static void HandoffSceneLoadNative(bool merge = false)
        {
            if (!s_SceneLoadActive) return;
            s_SceneLoadNativePhase = true;
            s_SceneLoadSubtitle = merge
                ? "Merging scene — please wait"
                : "Restoring scene — please wait";
            s_SceneLoadDepDone = 0;
            s_SceneLoadDepTotal = 0;

            string nativeStatus = merge ? "Merging scene..." : "Loading scene...";
            try { SceneLoadNativeUiBridge.ShowForSceneLoad(merge, nativeStatus); } catch { }

            // Keep VPB busy chrome + moving strip through native Load — do not EndSceneLoad here.
            EnsureOverlay();
        }

        internal static void EndSceneLoad()
        {
            s_SceneLoadActive = false;
            s_SceneLoadNativePhase = false;
            s_SceneLoadTitle = null;
            s_SceneLoadSubtitle = null;
            s_SceneLoadDepDone = 0;
            s_SceneLoadDepTotal = 0;
        }

        /// <summary>
        /// Tier B: main thread may stall. Start OS heartbeat. Unity strip stays enabled when frames pump.
        /// Nestable — pair with <see cref="ExitBlocking"/>.
        /// </summary>
        internal static void EnterBlocking(string title, string subtitle = null)
        {
            bool first;
            lock (s_BlockingSync)
            {
                s_BlockingDepth++;
                first = s_BlockingDepth == 1;
                if (!string.IsNullOrEmpty(title))
                    s_BlockingTitle = title;
                else if (string.IsNullOrEmpty(s_BlockingTitle))
                    s_BlockingTitle = "Working";
                s_BlockingSubtitle = string.IsNullOrEmpty(subtitle)
                    ? DefaultBlockingSubtitle
                    : subtitle;
            }
            EnsureOverlay();
            if (first)
            {
                try { VpbOsBusyHeartbeat.Show(); } catch { }
            }
        }

        internal static void ExitBlocking()
        {
            bool last = false;
            lock (s_BlockingSync)
            {
                if (s_BlockingDepth <= 0) return;
                s_BlockingDepth--;
                last = s_BlockingDepth == 0;
                if (last)
                {
                    s_BlockingTitle = null;
                    s_BlockingSubtitle = null;
                }
            }
            if (last)
            {
                try { VpbOsBusyHeartbeat.Hide(); } catch { }
            }
        }

        /// <summary>Force-clear blocking depth (scene banner cleanup / recovery).</summary>
        internal static void ClearBlocking()
        {
            lock (s_BlockingSync)
            {
                s_BlockingDepth = 0;
                s_BlockingTitle = null;
                s_BlockingSubtitle = null;
            }
            try { VpbOsBusyHeartbeat.Hide(); } catch { }
        }

        internal static void BeginBrowseRefresh(string phase = null)
        {
            s_BrowseRefreshActive = true;
            s_BrowseRefreshDone = 0;
            s_BrowseRefreshTotal = 0;
            s_BrowseRefreshPhase = string.IsNullOrEmpty(phase)
                ? "Preparing items for browse"
                : phase;
            EnsureOverlay();
        }

        internal static void ReportBrowseRefreshPhase(string phase)
        {
            if (!s_BrowseRefreshActive) return;
            if (!string.IsNullOrEmpty(phase))
                s_BrowseRefreshPhase = phase;
        }

        internal static void ReportBrowseRefresh(int done, int total, string phase = null)
        {
            if (!s_BrowseRefreshActive) return;
            s_BrowseRefreshDone = Math.Max(0, done);
            s_BrowseRefreshTotal = Math.Max(0, total);
            if (!string.IsNullOrEmpty(phase))
                s_BrowseRefreshPhase = phase;
        }

        internal static void EndBrowseRefresh()
        {
            s_BrowseRefreshActive = false;
            s_BrowseRefreshDone = 0;
            s_BrowseRefreshTotal = 0;
            s_BrowseRefreshPhase = null;
        }

        private static void ApplyBlockingOverlay(ref DisplaySnapshot snapshot)
        {
            if (!IsBlocking) return;
            snapshot.Blocking = true;
            // Keep ShowMovingStrip — Unity animates when frames pump; OS heartbeat covers stalls.
            snapshot.Cancellable = false;
            string freeze = null;
            lock (s_BlockingSync) { freeze = s_BlockingSubtitle; }
            if (string.IsNullOrEmpty(freeze)) freeze = DefaultBlockingSubtitle;
            if (string.IsNullOrEmpty(snapshot.Subtitle))
                snapshot.Subtitle = freeze;
            else if (snapshot.Subtitle.IndexOf("freeze", StringComparison.OrdinalIgnoreCase) < 0)
                snapshot.Subtitle = snapshot.Subtitle + " — " + freeze;
        }

        private static bool TryGetBlockingDisplaySnapshot(out DisplaySnapshot snapshot)
        {
            snapshot = default(DisplaySnapshot);
            string title;
            string subtitle;
            lock (s_BlockingSync)
            {
                if (s_BlockingDepth <= 0) return false;
                title = s_BlockingTitle;
                subtitle = s_BlockingSubtitle;
            }
            snapshot.Visible = true;
            snapshot.Blocking = true;
            snapshot.Cancellable = false;
            // Indeterminate strip when frames still pump (e.g. between sync cliffs).
            snapshot.ShowMovingStrip = true;
            snapshot.Progress01 = -1f;
            snapshot.Title = string.IsNullOrEmpty(title) ? "Working" : title;
            snapshot.Subtitle = string.IsNullOrEmpty(subtitle) ? DefaultBlockingSubtitle : subtitle;
            return true;
        }

        private static bool TryGetBrowseRefreshDisplaySnapshot(out DisplaySnapshot snapshot)
        {
            snapshot = default(DisplaySnapshot);
            if (!s_BrowseRefreshActive) return false;

            int done = s_BrowseRefreshDone;
            int total = s_BrowseRefreshTotal;
            string phase = s_BrowseRefreshPhase;

            snapshot.Visible = true;
            snapshot.Cancellable = false;
            snapshot.Title = "Loading gallery";
            if (total > 0)
            {
                snapshot.Progress01 = Mathf.Clamp01((float)done / total);
                snapshot.ShowMovingStrip = true;
                int pct = Mathf.Clamp(Mathf.RoundToInt(100f * done / Mathf.Max(1, total)), 0, 100);
                string phaseLabel = string.IsNullOrEmpty(phase) ? "Items" : phase;
                snapshot.Subtitle = phaseLabel + " " + done + "/" + total + " (" + pct + "%)";
            }
            else
            {
                snapshot.Progress01 = -1f;
                snapshot.ShowMovingStrip = true;
                string basePhase = string.IsNullOrEmpty(phase)
                    ? "Preparing items for browse"
                    : phase;
                snapshot.Subtitle = basePhase;
            }
            return true;
        }

        internal static bool IsSceneLoadBannerActive => s_SceneLoadActive;

        internal static bool TryGetSceneLoadDisplaySnapshot(out DisplaySnapshot snapshot)
        {
            snapshot = default(DisplaySnapshot);
            if (!s_SceneLoadActive) return false;

            string title = s_SceneLoadTitle;
            string subtitle = s_SceneLoadSubtitle;
            bool native = s_SceneLoadNativePhase;
            int depDone = s_SceneLoadDepDone;
            int depTotal = s_SceneLoadDepTotal;

            snapshot.Visible = true;
            snapshot.Cancellable = false;
            snapshot.Title = string.IsNullOrEmpty(title)
                ? (native ? "Loading scene" : "Preparing scene")
                : title;
            snapshot.Subtitle = string.IsNullOrEmpty(subtitle)
                ? (native ? "Restoring scene — please wait" : "Preparing — please wait")
                : subtitle;

            if (!native && depTotal > 0)
            {
                snapshot.Progress01 = Mathf.Clamp01((float)depDone / depTotal);
                snapshot.ShowMovingStrip = true;
            }
            else
            {
                snapshot.Progress01 = -1f;
                snapshot.ShowMovingStrip = true;
            }
            return true;
        }

        internal static void RequestCancelActiveJob()
        {
            if (s_BulkZstdActive)
            {
                try { ImageLoadingMgr.singleton?.CancelBulkOperation(); } catch { }
                return;
            }
            try { NativeTextureOnDemandCache.RequestCancel(); } catch { }
        }

        internal static void BeginBulkZstd(bool decompress)
        {
            s_BulkZstdActive = true;
            s_BulkZstdDecompress = decompress;
            s_BulkZstdDone = 0;
            s_BulkZstdTotal = 0;
            s_BulkZstdCurrentFile = null;
            EnsureOverlay();
        }

        internal static void EndBulkZstd()
        {
            s_BulkZstdActive = false;
            s_BulkZstdDone = 0;
            s_BulkZstdTotal = 0;
            s_BulkZstdCurrentFile = null;
        }

        internal static void PollBulkZstdProgress()
        {
            if (!s_BulkZstdActive) return;
            try
            {
                var mgr = ImageLoadingMgr.singleton;
                if (mgr == null)
                {
                    EndBulkZstd();
                    return;
                }
                var stats = mgr.CurrentZstdStats;
                if (stats == null)
                {
                    EndBulkZstd();
                    return;
                }
                if (stats.IsRunning)
                {
                    s_BulkZstdDecompress = stats.IsDecompression;
                    s_BulkZstdDone = (int)Math.Max(0, stats.ProcessedFiles);
                    s_BulkZstdTotal = (int)Math.Max(0, stats.TotalFiles);
                    s_BulkZstdCurrentFile = stats.CurrentFile;
                    return;
                }
                EndBulkZstd();
            }
            catch { EndBulkZstd(); }
        }

        internal static bool TryGetBulkZstdDisplaySnapshot(out DisplaySnapshot snapshot)
        {
            snapshot = default(DisplaySnapshot);
            if (!s_BulkZstdActive) return false;

            int done = s_BulkZstdDone;
            int total = s_BulkZstdTotal;
            string current = s_BulkZstdCurrentFile;
            bool decompress = s_BulkZstdDecompress;

            snapshot.Visible = true;
            snapshot.Cancellable = true;
            snapshot.Title = decompress ? "Decompressing texture cache" : "Compressing texture cache";
            if (total > 0)
            {
                snapshot.Progress01 = Mathf.Clamp01((float)done / total);
                snapshot.ShowMovingStrip = true;

                float elapsed = 0f;
                long origBytes = 0;
                long compBytes = 0;
                long skipped = 0;
                long failed = 0;
                try
                {
                    var stats = ImageLoadingMgr.singleton != null
                        ? ImageLoadingMgr.singleton.CurrentZstdStats
                        : null;
                    if (stats != null)
                    {
                        if (stats.StartTime != default(DateTime))
                            elapsed = (float)(DateTime.Now - stats.StartTime).TotalSeconds;
                        origBytes = stats.TotalOriginalSize;
                        compBytes = stats.TotalCompressedSize;
                        skipped = stats.SkippedCount;
                        failed = stats.FailedCount;
                    }
                }
                catch { }

                string focus = null;
                if (!string.IsNullOrEmpty(current)
                    && current != "Completed"
                    && current != "Cancelled"
                    && current != "Scanning...")
                {
                    focus = current;
                }

                snapshot.Subtitle = NativeTextureOnDemandCache.FormatLiveProgressLine(
                    done, total, elapsed, focus, includeThroughput: true);

                // Bytes + skip/fail — evaluation gulf for long compress jobs.
                if (origBytes > 0 || compBytes > 0 || skipped > 0 || failed > 0)
                {
                    var sb = new System.Text.StringBuilder(snapshot.Subtitle, snapshot.Subtitle.Length + 48);
                    if (origBytes > 0 || compBytes > 0)
                    {
                        sb.Append(" · ");
                        if (decompress)
                            sb.Append(FormatProgressBytes(compBytes)).Append("→").Append(FormatProgressBytes(origBytes));
                        else
                            sb.Append(FormatProgressBytes(origBytes)).Append("→").Append(FormatProgressBytes(compBytes));
                    }
                    if (skipped > 0) sb.Append(" · skip ").Append(skipped);
                    if (failed > 0) sb.Append(" · fail ").Append(failed);
                    snapshot.Subtitle = sb.ToString();
                }
            }
            else if (current == "Scanning...")
            {
                snapshot.Progress01 = -1f;
                snapshot.ShowMovingStrip = true;
                snapshot.Subtitle = "Scanning cache folders…";
            }
            else if (current == "Completed" || current == "Cancelled")
            {
                snapshot.Progress01 = 1f;
                snapshot.ShowMovingStrip = false;
                snapshot.Subtitle = current;
            }
            else
            {
                snapshot.Progress01 = -1f;
                snapshot.ShowMovingStrip = false;
                snapshot.Subtitle = "All caches already compressed";
            }
            return true;
        }

        private static string FormatProgressBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            const long kb = 1024L;
            const long mb = kb * 1024L;
            const long gb = mb * 1024L;
            if (bytes >= gb) return ((double)bytes / gb).ToString("0.00") + " GB";
            if (bytes >= mb) return ((double)bytes / mb).ToString("0.0") + " MB";
            if (bytes >= kb) return ((double)bytes / kb).ToString("0") + " KB";
            return bytes + " B";
        }

        internal static void PollStartupCompletion()
        {
            if (!s_AwaitingGalleryUi || s_GalleryUiReady || s_GalleryUiEverReady) return;
            try
            {
                if (Gallery.singleton != null && Gallery.singleton.AnyPanelHasLoadedContent)
                {
                    NotifyGalleryUiReady();
                    return;
                }
            }
            catch { }

            DateTime startedUtc;
            lock (s_StartupStateSync) { startedUtc = s_StartupCycleStartedUtc; }
            if (startedUtc != DateTime.MinValue && (DateTime.UtcNow - startedUtc).TotalSeconds > 120d)
            {
                if (!ExpectGalleryUiLoad())
                    NotifyGalleryUiReady();
            }
        }

        internal static void BeginManifestLoad()
        {
            s_ManifestActive = true;
            NoteStartupCycleTouch();
            RefreshGalleryUiExpectation();
            EnsureOverlay();
        }

        internal static void EndManifestLoad()
        {
            s_ManifestActive = false;
            TryEnterReadyState();
        }

        internal static void BeginGalleryUiLoad()
        {
            if (s_GalleryUiEverReady) return;
            s_GalleryUiActive = true;
            s_AwaitingGalleryUi = true;
            s_GalleryUiReady = false;
            s_ReadyUntilUtc = DateTime.MinValue;
            NoteStartupCycleTouch();
            EnsureOverlay();
        }

        internal static void EndGalleryUiLoad()
        {
            s_GalleryUiActive = false;
        }

        internal static void NotifyGalleryUiReady()
        {
            s_GalleryUiActive = false;
            s_GalleryUiReady = true;
            s_GalleryUiEverReady = true;
            s_AwaitingGalleryUi = false;
            TryEnterReadyState();
        }

        internal static void BeginDeepScan(int total)
        {
            s_DeepScanTotal = Math.Max(0, total);
            s_DeepScanDone = 0;
            s_DeepScanActive = total > 0;
            NoteStartupCycleTouch();
            EnsureOverlay();
        }

        internal static void ReportDeepScan(int done, int total)
        {
            if (total > 0) s_DeepScanTotal = total;
            s_DeepScanDone = Math.Max(0, done);
            if (total > 0) s_DeepScanActive = true;
        }

        internal static void EndDeepScan()
        {
            s_DeepScanActive = false;
            s_DeepScanDone = 0;
            s_DeepScanTotal = 0;
            TryEnterReadyState();
        }

        internal static void SetGalleryPending(bool pending)
        {
            s_GalleryPending = pending;
            if (pending)
            {
                s_GalleryPhase = GalleryPhase.Pending;
                NoteStartupCycleTouch();
                EnsureOverlay();
            }
            else if (!s_GalleryActive)
            {
                s_GalleryPending = false;
            }
        }

        internal static void BeginGalleryRebuild(int totalPackages)
        {
            s_GalleryActive = true;
            s_GalleryPending = false;
            s_GalleryPhase = GalleryPhase.Packages;
            s_GalleryTotal = Math.Max(0, totalPackages);
            s_GalleryDone = 0;
            NoteStartupCycleTouch();
            EnsureOverlay();
        }

        internal static void ReportGalleryPackages(int done, int total)
        {
            s_GalleryActive = true;
            s_GalleryPending = false;
            s_GalleryPhase = GalleryPhase.Packages;
            if (total > 0) s_GalleryTotal = total;
            s_GalleryDone = Math.Max(0, done);
        }

        internal static void ReportGalleryCreatingIndexes()
        {
            s_GalleryActive = true;
            s_GalleryPending = false;
            s_GalleryPhase = GalleryPhase.Indexes;
            int total = s_GalleryTotal;
            if (total > 0) s_GalleryDone = total;
        }

        internal static void BeginGalleryIncremental()
        {
            s_GalleryActive = true;
            s_GalleryPending = false;
            s_GalleryPhase = GalleryPhase.Incremental;
            s_GalleryDone = 0;
            s_GalleryTotal = 0;
            NoteStartupCycleTouch();
            EnsureOverlay();
        }

        internal static void EndGalleryRebuild()
        {
            s_GalleryActive = false;
            s_GalleryPending = false;
            s_GalleryDone = 0;
            s_GalleryTotal = 0;
            s_GalleryPhase = GalleryPhase.Pending;
            TryEnterReadyState();
        }

        internal static bool TryGetStartupDisplaySnapshot(out DisplaySnapshot snapshot)
        {
            snapshot = default(DisplaySnapshot);
            if (!IsStartupOverlayEnabled()) return false;

            if (s_ManifestActive)
            {
                snapshot.Visible = true;
                snapshot.Title = "Loading package manifest";
                snapshot.Subtitle = "One-time setup — do not close VaM";
                snapshot.Progress01 = -1f;
                snapshot.ShowMovingStrip = true;
                return true;
            }

            if (s_DeepScanActive)
            {
                int done = s_DeepScanDone;
                int total = s_DeepScanTotal;
                float p = (total > 0) ? Mathf.Clamp01((float)done / total) : -1f;
                snapshot.Visible = true;
                snapshot.Progress01 = p;
                snapshot.ShowMovingStrip = true;
                snapshot.Title = "Scanning VAR packages";
                snapshot.Subtitle = (total > 0)
                    ? ("One-time setup | Progress " + done + "/" + total + " — do not close VaM")
                    : "One-time setup — do not close VaM";
                return true;
            }

            if (s_GalleryActive || s_GalleryPending)
            {
                snapshot.Visible = true;
                GalleryPhase phase = s_GalleryPhase;
                int done = s_GalleryDone;
                int total = s_GalleryTotal;

                switch (phase)
                {
                    case GalleryPhase.Indexes:
                        snapshot.Title = "Building gallery index";
                        snapshot.Subtitle = "Creating database indexes — do not close VaM";
                        snapshot.Progress01 = 1f;
                        snapshot.ShowMovingStrip = false;
                        return true;

                    case GalleryPhase.Incremental:
                        snapshot.Title = "Updating gallery index";
                        snapshot.Subtitle = "Please wait — do not close VaM";
                        snapshot.Progress01 = -1f;
                        snapshot.ShowMovingStrip = true;
                        return true;

                    case GalleryPhase.Packages:
                        if (total > 0)
                        {
                            snapshot.Title = "Building gallery index";
                            snapshot.Subtitle = "Packages " + done + "/" + total + " — do not close VaM";
                            snapshot.Progress01 = Mathf.Clamp01((float)done / total);
                            snapshot.ShowMovingStrip = true;
                        }
                        else
                        {
                            snapshot.Title = "Building gallery index";
                            snapshot.Subtitle = "Please wait — do not close VaM";
                            snapshot.Progress01 = -1f;
                            snapshot.ShowMovingStrip = true;
                        }
                        return true;

                    default:
                        snapshot.Title = "Preparing gallery index";
                        snapshot.Subtitle = "Please wait — do not close VaM";
                        snapshot.Progress01 = -1f;
                        snapshot.ShowMovingStrip = true;
                        return true;
                }
            }

            if (!s_GalleryUiEverReady
                && (s_GalleryUiActive || (s_AwaitingGalleryUi && !s_GalleryUiReady)))
            {
                snapshot.Visible = true;
                snapshot.Title = "Loading gallery";
                snapshot.Subtitle = "Preparing items for browse — do not close VaM";
                snapshot.Progress01 = -1f;
                snapshot.ShowMovingStrip = true;
                return true;
            }

            DateTime nowUtc = DateTime.UtcNow;
            DateTime readyUntilUtc = s_ReadyUntilUtc;
            if (readyUntilUtc > nowUtc)
            {
                snapshot.Visible = true;
                snapshot.Title = "READY";
                snapshot.Subtitle = "Startup " + s_LastMeasuredStartupSeconds.ToString("0.00") + "s"
                    + " | gallery ready " + s_LastScanAndIndexSeconds.ToString("0.00") + "s";
                snapshot.Progress01 = 1f;
                snapshot.ShowMovingStrip = false;
                return true;
            }

            return false;
        }

        private static void NoteStartupCycleTouch()
        {
            lock (s_StartupStateSync)
            {
                s_ReadyUntilUtc = DateTime.MinValue;
                s_StartupCycleTouched = true;
                if (!s_StartupCycleInProgress)
                {
                    s_StartupCycleInProgress = true;
                    s_StartupCycleStartedUtc = DateTime.UtcNow;
                }
            }
            RefreshGalleryUiExpectation();
        }

        private static void RefreshGalleryUiExpectation()
        {
            if (s_GalleryUiReady || s_GalleryUiEverReady) return;
            s_AwaitingGalleryUi = ExpectGalleryUiLoad();
        }

        private static bool ExpectGalleryUiLoad()
        {
            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.EnableAutoFixedGallery)
                    return true;
                var g = Gallery.singleton;
                if (g != null)
                {
                    if (g.PanelCount > 0) return true;
                    if (Gallery.HasStartupDeferredWork()) return true;
                }
            }
            catch { }
            return false;
        }

        private static void TryEnterReadyState()
        {
            if (s_ManifestActive) return;
            if (s_DeepScanActive) return;
            if (s_GalleryActive || s_GalleryPending) return;
            if (s_GalleryUiActive) return;
            if (!s_GalleryUiEverReady && s_AwaitingGalleryUi && !s_GalleryUiReady) return;

            lock (s_StartupStateSync)
            {
                if (!s_StartupCycleTouched) return;
                double startupSeconds;
                try { startupSeconds = LogUtil.GetStartupSecondsForDisplay(); }
                catch { startupSeconds = 0d; }
                s_LastMeasuredStartupSeconds = startupSeconds;

                DateTime startedUtc = s_StartupCycleStartedUtc;
                if (startedUtc != DateTime.MinValue)
                {
                    double cycleSec = (DateTime.UtcNow - startedUtc).TotalSeconds;
                    if (cycleSec < 0d) cycleSec = 0d;
                    s_LastScanAndIndexSeconds = cycleSec;
                }
                else
                {
                    s_LastScanAndIndexSeconds = 0d;
                }

                s_StartupCycleTouched = false;
                s_StartupCycleInProgress = false;
                s_StartupCycleStartedUtc = DateTime.MinValue;
                s_ReadyUntilUtc = DateTime.UtcNow.AddSeconds(5d);
            }
        }

        private static bool IsStartupOverlayEnabled()
        {
            try
            {
                if (Settings.Instance == null) return true;
                if (Settings.Instance.ShowGalleryIndexBuildOverlay == null) return true;
                return Settings.Instance.ShowGalleryIndexBuildOverlay.Value;
            }
            catch { return true; }
        }
    }
}
