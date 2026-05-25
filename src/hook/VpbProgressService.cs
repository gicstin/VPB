using System;
using UnityEngine;

namespace VPB
{
    /// <summary>Unified startup / long-task progress for top banner overlay.</summary>
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

        internal static bool IsDeepScanActive => s_DeepScanActive;

        internal static bool IsGalleryProgressActive =>
            s_GalleryActive || s_GalleryPending;

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
            try { NativeTextureCacheBuildOverlay.EnsureCreated(); } catch { }
        }

        internal static bool TryGetActiveDisplaySnapshot(out DisplaySnapshot snapshot)
        {
            if (TryGetStartupDisplaySnapshot(out snapshot))
                return true;
            return TryGetBulkZstdDisplaySnapshot(out snapshot);
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
                snapshot.Subtitle = "Progress " + done + "/" + total;
                if (!string.IsNullOrEmpty(current) && current != "Completed" && current != "Cancelled" && current != "Scanning...")
                    snapshot.Subtitle += " | " + current;
            }
            else if (current == "Scanning...")
            {
                snapshot.Progress01 = -1f;
                snapshot.ShowMovingStrip = true;
                snapshot.Subtitle = "Scanning cache...";
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
