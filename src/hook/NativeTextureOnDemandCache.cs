using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Zip;
using SimpleJSON;
using ZstdNet;
using UnityEngine;

namespace VPB
{
    public static class NativeTextureOnDemandCache
    {
        internal enum CacheWriteMode
        {
            NativeOnly,
            ZstdOnly,
            NativeAndZstd
        }

        private const int DefaultSizedCacheWidth = 512;
        private const int DefaultSizedCacheHeight = 512;
        private const int JsonWalkMaxDepth = 6;
        private const float OnDemandLogThrottleSeconds = 0.5f;
        /// <summary>Max main-thread work per on-demand frame slice before yielding (higher = faster warm, slightly longer frames).</summary>
        private const float OnDemandFrameBudgetSec = 0.020f;
        private const int DownscaleDimensionProbeMaxBytes = 512 * 1024;
        /// <summary>Skip 8k downscale probe when VAR entry smaller than this (unlikely 8k source).</summary>
        private const long DownscaleProbeMinEntryBytes = 3L * 1024L * 1024L;
        private const int UiStatusUpdateEveryNTex = 8;
        private const float UiStatusUpdateMinIntervalSec = 0.25f;

        internal struct UiSnapshot
        {
            public bool Visible;
            public bool ShowSummary;
            public float Progress01;
            public string Title;
            public string Subtitle;
            public string SummaryText;
        }

        private static bool s_OnDemandBusy;
        private static float s_OnDemandLastLogTime;

        private static bool s_UiVisible;
        private static bool s_UiShowSummary;
        private static string s_UiTitle;
        private static string s_UiSubtitle;
        private static string s_UiSummary;

        private static float s_JobStartUnscaledTime;
        private static float s_LastElapsedSeconds;
        private static int s_TotalWork;
        private static int s_ProcessedWork;

        private static int s_PackagesPlanned;
        private static int s_PackagesProcessed;
        private static int s_PackagesResolved;
        private static string s_CurrentPackage;

        private static int s_TexturesPlanned;
        private static int s_TexturesProcessed;
        private static int s_CacheWrites;
        private static int s_CacheSkips;
        private static int s_CacheFails;

        private static int s_NativeDeletes;
        private static int s_ZstdDeletes;
        private static int s_PurgeTexturesDeleted;
        private static int s_PurgePackagesDeleted;
        private static bool s_PurgeWorkerAnyDeletes;

        private static long s_NativeCacheBytes;
        private static long s_NativeCacheBytesWritten;
        private static long s_NativeCacheBytesExisting;

        private static int s_ZstdWrites;
        private static int s_ZstdRewrites;
        private static int s_ZstdSkips;
        private static int s_ZstdFails;
        private static long s_ZstdOriginalBytes;
        private static long s_ZstdCompressedBytes;
        private static long s_ZstdDiskBytes;
        private static long s_ZstdDiskBytesWritten;
        private static long s_ZstdDiskBytesExisting;
        private static int s_ZstdDownscaleWrites;
        private static long s_ZstdDownscaleSavedBytes;
        private static long s_ZstdDownscaleOriginalBytes;
        private static long s_ZstdDownscaleFinalBytes;

        private static bool s_CancelRequested;

        private static bool s_IsPurgeJob;

        private static bool s_ZstdInitialized;

        private static CacheWriteMode? s_NextJobWriteModeOverride;
        private static CacheWriteMode s_JobWriteMode;
        private static bool s_NextJobRewriteExistingZstd;
        private static bool s_JobRewriteExistingZstd;

        private static StringBuilder s_DebugTrace;

        private static readonly List<string> s_OnDemandIssueLog = new List<string>();
        private const int OnDemandIssueLogMax = 80;

        private static bool s_CompletionSoundPlayed;
        private static float s_LastCompletionSoundTime;
        private static GameObject s_CompletionAudioGO;
        private static AudioSource s_CompletionAudioSource;
        private static AudioClip s_CompletionClip;

        private static bool s_BatchMode;

        private static int s_UiStatusTexturesSinceUpdate;
        private static float s_LastUiStatusUpdateTime;

        public static bool IsOnDemandBusy => s_OnDemandBusy;

        /// <summary>While true, GetZstdCachePath must not log per-texture MISS lines during on-demand builds.</summary>
        internal static bool SuppressZstdMissLookupLog { get; private set; }

        internal static UiSnapshot GetUiSnapshot()
        {
            float progress = 0f;
            // In bulk mode, progress is per-item (6/8), not per-texture.
            // In single mode, prefer texture progress whenever possible.
            int total = s_TotalWork;
            int done = s_ProcessedWork;
            if (!s_BatchMode && s_TexturesPlanned > 0)
            {
                total = s_TexturesPlanned;
                done = s_TexturesProcessed;
            }
            if (total > 0)
            {
                progress = Mathf.Clamp01((float)done / total);
            }

            return new UiSnapshot
            {
                Visible = s_UiVisible,
                ShowSummary = s_UiShowSummary,
                Progress01 = progress,
                Title = s_UiTitle,
                Subtitle = s_UiSubtitle,
                SummaryText = s_UiSummary
            };
        }

        internal static void DismissSummary()
        {
            s_UiShowSummary = false;
            s_UiVisible = false;
        }

        internal static void RequestCancel()
        {
            s_CancelRequested = true;
            try
            {
                if (s_UiVisible && !s_UiShowSummary)
                {
                    s_UiSubtitle = (s_UiSubtitle ?? string.Empty) + " | Cancel requested";
                }
            }
            catch { }
        }

        internal static bool CancelRequested => s_CancelRequested;

        internal static void SetNextJobWriteModeOverride(CacheWriteMode mode)
        {
            s_NextJobWriteModeOverride = mode;
        }

        internal static void SetNextJobRewriteExistingZstd(bool rewriteExistingZstd)
        {
            s_NextJobRewriteExistingZstd = rewriteExistingZstd;
        }

        internal static void BeginBatchJob(string title, int totalItems)
        {
            BeginUiJob(title);
            s_BatchMode = true;
            s_CancelRequested = false;
            s_TotalWork = Math.Max(1, totalItems);
            s_ProcessedWork = 0;
            s_PackagesPlanned = totalItems;
            s_PackagesProcessed = 0;
            UpdateUiStatus();
        }

        internal static void EndBatchJob(string resultTitle)
        {
            s_PackagesProcessed = s_PackagesPlanned;
            s_ProcessedWork = s_TotalWork;
            s_BatchMode = false;
            EndUiJob(resultTitle);
        }

        internal static void BatchItemStart(string label)
        {
            s_CurrentPackage = label;
            UpdateUiStatus();
        }

        internal static void BatchItemDone()
        {
            s_PackagesProcessed++;
            s_ProcessedWork++;
            UpdateUiStatus();
        }

        private static void BeginUiJob(string title)
        {
            s_JobStartUnscaledTime = Time.unscaledTime;
            s_LastElapsedSeconds = 0f;
            s_TotalWork = 0;
            s_ProcessedWork = 0;
            s_PackagesPlanned = 0;
            s_PackagesProcessed = 0;
            s_PackagesResolved = 0;
            s_CurrentPackage = null;
            s_TexturesPlanned = 0;
            s_TexturesProcessed = 0;
            s_CacheWrites = 0;
            s_CacheSkips = 0;
            s_CacheFails = 0;
            s_NativeDeletes = 0;
            s_ZstdDeletes = 0;
            s_PurgeTexturesDeleted = 0;
            s_PurgePackagesDeleted = 0;
            s_PurgeWorkerAnyDeletes = false;
            s_NativeCacheBytes = 0;
            s_NativeCacheBytesWritten = 0;
            s_NativeCacheBytesExisting = 0;
            s_ZstdWrites = 0;
            s_ZstdRewrites = 0;
            s_ZstdSkips = 0;
            s_ZstdFails = 0;
            s_ZstdOriginalBytes = 0;
            s_ZstdCompressedBytes = 0;
            s_ZstdDiskBytes = 0;
            s_ZstdDiskBytesWritten = 0;
            s_ZstdDiskBytesExisting = 0;
            s_ZstdDownscaleWrites = 0;
            s_ZstdDownscaleSavedBytes = 0;
            s_ZstdDownscaleOriginalBytes = 0;
            s_ZstdDownscaleFinalBytes = 0;
            s_CancelRequested = false;
            s_IsPurgeJob = !string.IsNullOrEmpty(title) && title.IndexOf("Purg", StringComparison.OrdinalIgnoreCase) >= 0;

            CacheWriteMode mode = CacheWriteMode.NativeOnly;
            try
            {
                if (s_NextJobWriteModeOverride.HasValue)
                {
                    mode = s_NextJobWriteModeOverride.Value;
                }
                else if (Settings.Instance != null && Settings.Instance.EnableZstdCompression.Value)
                {
                    mode = CacheWriteMode.ZstdOnly;
                }
            }
            catch { }
            s_NextJobWriteModeOverride = null;
            s_JobWriteMode = mode;
            s_JobRewriteExistingZstd = s_NextJobRewriteExistingZstd;
            s_NextJobRewriteExistingZstd = false;

            s_CompletionSoundPlayed = false;
            s_UiSummary = null;
            s_UiShowSummary = false;
            s_UiVisible = true;
            s_UiTitle = title;
            s_UiSubtitle = string.Empty;
            s_UiStatusTexturesSinceUpdate = 0;
            s_LastUiStatusUpdateTime = Time.unscaledTime;

            try
            {
                if (s_DebugTrace == null) s_DebugTrace = new StringBuilder(64 * 1024);
                s_DebugTrace.Length = 0;
                s_DebugTrace.AppendLine("VPB OnDemand Cache Trace");
                s_DebugTrace.AppendLine("StartedUtc: " + DateTime.UtcNow.ToString("o"));
                s_DebugTrace.AppendLine("Title: " + (title ?? string.Empty));
                s_DebugTrace.AppendLine("WriteMode: " + s_JobWriteMode);
                s_DebugTrace.AppendLine("RewriteExistingZstd: " + s_JobRewriteExistingZstd);
                s_DebugTrace.AppendLine();
            }
            catch { }
            try { s_OnDemandIssueLog.Clear(); } catch { }
        }

        private static void Trace(string msg)
        {
            try
            {
                if (s_DebugTrace == null) return;
                s_DebugTrace.AppendLine(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " | " + (msg ?? string.Empty));
            }
            catch { }
        }

        private static int OnDemandLogLevel()
        {
            try { return Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0; }
            catch { return 0; }
        }

        private static void OnDemandLog(string msg, bool always = false)
        {
            Trace(msg);
            if (!always && OnDemandLogLevel() < 1) return;
            try { LogUtil.Log("[VPB OnDemand] " + msg); } catch { }
        }

        private static void RecordOnDemandIssue(string category, string imgUidPath, string internalPath, TextureFlags flags, string detail)
        {
            string flagsSig = GetFlagsSignature(flags);
            string line = category + " | " + (internalPath ?? imgUidPath ?? string.Empty)
                + " | uid=" + (imgUidPath ?? string.Empty)
                + " | sig=" + flagsSig
                + (string.IsNullOrEmpty(detail) ? string.Empty : " | " + detail);
            try
            {
                if (s_OnDemandIssueLog.Count < OnDemandIssueLogMax)
                {
                    s_OnDemandIssueLog.Add(line);
                }
            }
            catch { }
            Trace("Issue: " + line);
        }

        private static string FormatOnDemandSkipDetail(bool nativeExists, bool zstdExists, bool needNative, bool needZstd, TextureFlags flags)
        {
            if (nativeExists || zstdExists)
            {
                return "cache hit native=" + nativeExists + " zstd=" + zstdExists;
            }

            if (!needNative && !needZstd)
            {
                return "no work for mode=" + s_JobWriteMode + " sig=" + GetFlagsSignature(flags);
            }

            return "cache hit native=" + nativeExists + " zstd=" + zstdExists;
        }

        /// <summary>True only for assets VaM reads as native .vamcache outside VPB zstd serve path (e.g. Spectral LUT).</summary>
        private static bool NeedsNativeCacheAtRuntime(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return false;
            return internalPath.IndexOf("spectrallut", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>On-demand prewarm: ZstdOnly writes .zvamcache only; native is for NativeOnly/NativeAndZstd or runtime-only LUT tables.</summary>
        private static void ResolveOnDemandWriteTargets(string internalPath, TextureFlags flags, out bool wantNative, out bool wantZstd)
        {
            wantNative = s_JobWriteMode == CacheWriteMode.NativeOnly
                || s_JobWriteMode == CacheWriteMode.NativeAndZstd
                || (s_JobWriteMode != CacheWriteMode.ZstdOnly && NeedsNativeCacheAtRuntime(internalPath));

            wantZstd = s_JobWriteMode == CacheWriteMode.ZstdOnly || s_JobWriteMode == CacheWriteMode.NativeAndZstd;
        }

        private static void EndUiJob(string resultTitle)
        {
            float elapsed = Time.unscaledTime - s_JobStartUnscaledTime;
            if (elapsed < 0.5f && s_LastElapsedSeconds > elapsed)
            {
                elapsed = s_LastElapsedSeconds;
            }
            s_UiTitle = resultTitle;
            s_UiVisible = true;
            s_UiShowSummary = true;
            s_UiSubtitle = string.Empty;

            TryPlayCompletionSound();

            string elapsedStr = FormatDuration(elapsed);

            if (s_IsPurgeJob)
            {
                s_UiSummary = "<b>Elapsed:</b> " + elapsedStr + "\n"
                    + "<b>Packages:</b> " + s_PackagesProcessed + "/" + s_PackagesPlanned
                    + "    <b>Resolved:</b> " + s_PackagesResolved + "\n"
                    + "<b>Textures:</b> " + s_TexturesProcessed + "/" + s_TexturesPlanned + "\n\n"
                    + "<b>Purged</b>\n"
                    + "Packages: " + s_PurgePackagesDeleted + "    Textures: " + s_PurgeTexturesDeleted + "\n"
                    + "<b>Deleted</b>\n"
                    + "Native: " + s_NativeDeletes + "    Zstd: " + s_ZstdDeletes;

                s_OnDemandBusy = false;
                return;
            }

            long zstdSavedBytes = 0;
            string zstdSaved = "0B";
            string zstdPct = "0";
            string zstdInput = FormatBytes(s_ZstdOriginalBytes);
            string zstdOutput = FormatBytes(s_ZstdCompressedBytes);
            string zstdDisk = FormatBytes(s_ZstdDiskBytes);
            string zstdDiskWritten = FormatBytes(s_ZstdDiskBytesWritten);
            string zstdDiskExisting = FormatBytes(s_ZstdDiskBytesExisting);
            if (s_ZstdOriginalBytes > 0 && s_ZstdCompressedBytes >= 0)
            {
                zstdSavedBytes = s_ZstdOriginalBytes - s_ZstdCompressedBytes;
                if (zstdSavedBytes < 0) zstdSavedBytes = 0;
                zstdSaved = FormatBytes(zstdSavedBytes);
                float pct = (float)s_ZstdCompressedBytes / Mathf.Max(1f, (float)s_ZstdOriginalBytes);
                float savedPct = 100f * (1f - Mathf.Clamp01(pct));
                zstdPct = savedPct.ToString("0");
            }

            long resizeBaselineBytes = s_ZstdDownscaleOriginalBytes;
            long resizeFinalBytes = s_ZstdDownscaleFinalBytes;
            long resizeSavedBytes = s_ZstdDownscaleSavedBytes;
            if (resizeSavedBytes < 0) resizeSavedBytes = 0;
            string resizeBase = FormatBytes(resizeBaselineBytes);
            string resizeFinal = FormatBytes(resizeFinalBytes);
            string resizeSaved = FormatBytes(resizeSavedBytes);
            string resizePct = "0";
            if (resizeBaselineBytes > 0)
            {
                float pct = (float)resizeFinalBytes / Mathf.Max(1f, (float)resizeBaselineBytes);
                float savedPct = 100f * (1f - Mathf.Clamp01(pct));
                resizePct = savedPct.ToString("0");
            }

            long totalBaselineBytes = s_ZstdOriginalBytes + s_ZstdDownscaleSavedBytes;
            if (totalBaselineBytes < 0) totalBaselineBytes = 0;
            long totalSavedBytes = totalBaselineBytes - s_ZstdDiskBytes;
            if (totalSavedBytes < 0) totalSavedBytes = 0;
            string totalBase = FormatBytes(totalBaselineBytes);
            string totalFinal = FormatBytes(s_ZstdDiskBytes);
            string totalSaved = FormatBytes(totalSavedBytes);
            string totalPct = "0";
            if (totalBaselineBytes > 0)
            {
                float pct = (float)s_ZstdDiskBytes / Mathf.Max(1f, (float)totalBaselineBytes);
                float savedPct = 100f * (1f - Mathf.Clamp01(pct));
                totalPct = savedPct.ToString("0");
            }

            s_UiSummary = "<b>Elapsed:</b> " + elapsedStr + "\n"
                + "<b>Packages:</b> " + s_PackagesProcessed + "/" + s_PackagesPlanned + "    <b>Textures:</b> " + s_TexturesProcessed + "/" + s_TexturesPlanned + "\n\n"
                + "<b>Size</b>\n"
                + "Native Cache Size: " + totalBase + "\n"
                + "Zstd Compressed Size: " + totalFinal + "\n"
                + "Saved Space: " + totalSaved + " (" + totalPct + "%)\n\n"
                + "<b>Zstd Cache</b>\n"
                + "Wrote: " + s_ZstdWrites + "    Skipped: " + s_ZstdSkips + "    Failed: " + s_ZstdFails;

            if (s_ZstdRewrites > 0)
            {
                s_UiSummary += "\nRewrote existing: " + s_ZstdRewrites;
            }

            if (s_ZstdDownscaleWrites > 0)
            {
                s_UiSummary += "\nResize 8k→4k: " + s_ZstdDownscaleWrites + "    Saved: " + resizeSaved + " (" + resizePct + "%)";
            }

            s_UiSummary += "\n\n<b>Native</b> Wrote: " + s_CacheWrites + "    Skipped: " + s_CacheSkips + "    Failed: " + s_CacheFails;

            FlushOnDemandIssueLogToDebug();

            OnDemandLog(
                "Job done packages=" + s_PackagesProcessed + "/" + s_PackagesPlanned
                + " textures=" + s_TexturesProcessed + "/" + s_TexturesPlanned
                + " nativeW=" + s_CacheWrites + " nativeSkip=" + s_CacheSkips + " nativeFail=" + s_CacheFails
                + " zstdW=" + s_ZstdWrites + " zstdSkip=" + s_ZstdSkips + " zstdFail=" + s_ZstdFails,
                always: s_CacheFails > 0 || s_ZstdFails > 0);

            if (OnDemandLogLevel() >= 2 && s_DebugTrace != null && s_DebugTrace.Length > 0)
            {
                try { LogUtil.Log("[VPB OnDemand] Trace dump:\n" + s_DebugTrace.ToString()); } catch { }
            }

            s_OnDemandBusy = false;
        }

        private static void FlushOnDemandIssueLogToDebug()
        {
            if (s_OnDemandIssueLog.Count == 0) return;

            try
            {
                if (s_DebugTrace != null)
                {
                    s_DebugTrace.AppendLine();
                    s_DebugTrace.AppendLine("Issues (" + s_OnDemandIssueLog.Count + (s_OnDemandIssueLog.Count >= OnDemandIssueLogMax ? "+" : "") + "):");
                    for (int i = 0; i < s_OnDemandIssueLog.Count; i++)
                    {
                        s_DebugTrace.AppendLine(s_OnDemandIssueLog[i]);
                    }
                }
            }
            catch { }

            if (OnDemandLogLevel() < 1) return;

            try
            {
                var sb = new StringBuilder(4096);
                sb.Append("[VPB OnDemand] Issues (").Append(s_OnDemandIssueLog.Count).Append("):\n");
                for (int i = 0; i < s_OnDemandIssueLog.Count; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(s_OnDemandIssueLog[i]);
                }
                LogUtil.Log(sb.ToString());
            }
            catch { }
        }

        private static void TryDeleteFileAndMeta(string path, ref int deletedMainCount)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deletedMainCount++;
                }
            }
            catch { }
            try { if (File.Exists(path + "meta")) File.Delete(path + "meta"); } catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            const double kb = 1024.0;
            const double mb = kb * 1024.0;
            const double gb = mb * 1024.0;
            if (bytes >= gb) return (bytes / gb).ToString("0.00") + "GB";
            if (bytes >= mb) return (bytes / mb).ToString("0.00") + "MB";
            if (bytes >= kb) return (bytes / kb).ToString("0.00") + "KB";
            return bytes + "B";
        }

        private static void AtomicWriteAllBytes(string finalPath, byte[] data)
        {
            string tmp = finalPath + ".tmp";
            try
            {
                string dir = null;
                try { dir = Path.GetDirectoryName(finalPath); } catch { dir = null; }
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }

            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { }

            try
            {
                File.WriteAllBytes(tmp, data);
                try
                {
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                }
                catch { }
                File.Move(tmp, finalPath);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        private static void AtomicWriteAllText(string finalPath, string text)
        {
            string tmp = finalPath + ".tmp";
            try
            {
                string dir = null;
                try { dir = Path.GetDirectoryName(finalPath); } catch { dir = null; }
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }

            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { }

            try
            {
                File.WriteAllText(tmp, text);
                try
                {
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                }
                catch { }
                File.Move(tmp, finalPath);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        internal static void EnsureZstdInitialized()
        {
            if (s_ZstdInitialized) return;
            try
            {
                ExternMethods.Initialize();
                s_ZstdInitialized = true;
            }
            catch { }
        }

        private static void TryPlayCompletionSound()
        {
            if (s_CompletionSoundPlayed) return;

            float now = Time.unscaledTime;
            if (now - s_LastCompletionSoundTime < 1.0f) return;
            s_LastCompletionSoundTime = now;

            try
            {
                if (s_CompletionAudioGO == null)
                {
                    s_CompletionAudioGO = new GameObject("VPB_CacheCompleteAudio");
                    UnityEngine.Object.DontDestroyOnLoad(s_CompletionAudioGO);
                    s_CompletionAudioSource = s_CompletionAudioGO.AddComponent<AudioSource>();
                    s_CompletionAudioSource.playOnAwake = false;
                    s_CompletionAudioSource.loop = false;
                    s_CompletionAudioSource.spatialBlend = 0f;
                    s_CompletionAudioSource.volume = 0.25f;
                }

                if (s_CompletionClip == null)
                {
                    s_CompletionClip = CreateCompletionClip();
                }

                if (s_CompletionAudioSource != null && s_CompletionClip != null)
                {
                    s_CompletionAudioSource.PlayOneShot(s_CompletionClip);
                    s_CompletionSoundPlayed = true;
                }
            }
            catch { }
        }

        private static AudioClip CreateCompletionClip()
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.32f;
            const int channels = 1;
            int totalSamples = Mathf.CeilToInt(sampleRate * durationSeconds);

            var data = new float[totalSamples];
            float freqA = 880f;
            float freqB = 659.25f;
            int split = Mathf.FloorToInt(totalSamples * 0.55f);
            float amp = 0.22f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float f = (i < split) ? freqA : freqB;
                float env = 1f;
                if (t < 0.02f) env = t / 0.02f;
                else if (t > durationSeconds - 0.06f) env = Mathf.Clamp01((durationSeconds - t) / 0.06f);

                data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * amp * env;
            }

            AudioClip clip = AudioClip.Create("VPB_CacheComplete", totalSamples, channels, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void UpdateUiStatus()
        {
            float elapsed = Time.unscaledTime - s_JobStartUnscaledTime;
            if (elapsed > s_LastElapsedSeconds) s_LastElapsedSeconds = elapsed;

            int done = s_ProcessedWork;
            int total = s_TotalWork;
            // In bulk mode, progress is per-item; in single mode, prefer texture progress when available.
            if (!s_BatchMode && s_TexturesPlanned > 0)
            {
                done = s_TexturesProcessed;
                total = s_TexturesPlanned;
            }

            string etaStr = "";
            if (total > 0 && done > 0)
            {
                float rate = done / Mathf.Max(0.0001f, elapsed);
                float remaining = (total - done) / Mathf.Max(0.0001f, rate);
                etaStr = " | ETA " + FormatDuration(remaining);
            }

            string pkgStr = string.IsNullOrEmpty(s_CurrentPackage) ? "" : (" | " + s_CurrentPackage);

            s_UiSubtitle = "Progress " + done + "/" + total
                + " | " + FormatDuration(elapsed)
                + etaStr
                + pkgStr;
        }

        private static void UpdateUiStatusThrottled(bool force = false)
        {
            if (!force)
            {
                s_UiStatusTexturesSinceUpdate++;
                float now = Time.unscaledTime;
                if (s_UiStatusTexturesSinceUpdate < UiStatusUpdateEveryNTex
                    && (now - s_LastUiStatusUpdateTime) < UiStatusUpdateMinIntervalSec)
                {
                    return;
                }
                s_UiStatusTexturesSinceUpdate = 0;
                s_LastUiStatusUpdateTime = now;
            }
            else
            {
                s_UiStatusTexturesSinceUpdate = 0;
            }

            UpdateUiStatus();
        }

        private static string FormatDuration(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int s = Mathf.FloorToInt(seconds);
            int h = s / 3600;
            int m = (s % 3600) / 60;
            int ss = s % 60;
            if (h > 0) return h + ":" + m.ToString("00") + ":" + ss.ToString("00");
            return m + ":" + ss.ToString("00");
        }

        public static void TryBuildSceneCacheOnDemand(MonoBehaviour host)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            string scenePath = ResolveCurrentScenePath();
            if (string.IsNullOrEmpty(scenePath))
            {
                ThrottledLog("[VPB] On-demand cache skipped: no current scene path.");
                return;
            }

            if (!s_BatchMode) BeginUiJob("Caching Textures...");
            s_OnDemandBusy = true;
            host.StartCoroutine(BuildSceneCacheCoroutine(scenePath));
        }

        public static void TryBuildSceneCacheOnDemand(MonoBehaviour host, string scenePath)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            string normalized = NormalizeScenePath(scenePath);
            if (string.IsNullOrEmpty(normalized))
            {
                ThrottledLog("[VPB] On-demand cache skipped: no scene path.");
                return;
            }

            if (!s_BatchMode) BeginUiJob("Caching Textures...");
            s_OnDemandBusy = true;
            host.StartCoroutine(BuildSceneCacheCoroutine(normalized));
        }

        public static void TryBuildPackageCacheOnDemand(MonoBehaviour host, string packagePath)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            if (string.IsNullOrEmpty(packagePath))
            {
                ThrottledLog("[VPB] On-demand cache skipped: no package path.");
                return;
            }

            if (!s_BatchMode) BeginUiJob("Caching Textures...");
            s_OnDemandBusy = true;
            host.StartCoroutine(BuildPackageCacheCoroutine(packagePath));
        }

        public static void TryPurgePackageCacheOnDemand(MonoBehaviour host, string packagePath)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            if (string.IsNullOrEmpty(packagePath))
            {
                ThrottledLog("[VPB] On-demand purge skipped: no package path.");
                return;
            }

            if (!s_BatchMode) BeginUiJob("Purging Texture Cache...");
            s_OnDemandBusy = true;
            host.StartCoroutine(PurgePackageCacheCoroutine(packagePath));
        }

        public static void TryPurgeSceneCacheOnDemand(MonoBehaviour host, string scenePath)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            string normalized = NormalizeScenePath(scenePath);
            if (string.IsNullOrEmpty(normalized))
            {
                ThrottledLog("[VPB] On-demand purge skipped: no scene path.");
                return;
            }

            if (!s_BatchMode) BeginUiJob("Purging Texture Cache...");
            s_OnDemandBusy = true;
            host.StartCoroutine(PurgeSceneCacheCoroutine(normalized));
        }

        private static string ResolveCurrentScenePath()
        {
            string scenePath = LogUtil.GetSceneLoadName();
            scenePath = NormalizeScenePath(scenePath);
            if (!string.IsNullOrEmpty(scenePath)) return scenePath;

            var sc = SuperController.singleton;
            if (sc == null) return null;

            string resolved = TryGetScenePathFromSuperController(sc);
            resolved = NormalizeScenePath(resolved);
            if (!string.IsNullOrEmpty(resolved)) return resolved;

            return null;
        }

        private static string NormalizeScenePath(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return scenePath;
            string normalized = scenePath.Replace('\\', '/').Trim();

            if (normalized.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "var:/" + normalized.Substring("AllPackages/".Length);
            }

            if (normalized.StartsWith("var:/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (normalized.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("var:".Length);
                if (!string.IsNullOrEmpty(normalized) && normalized[0] != '/') normalized = "/" + normalized;
                return "var:" + normalized;
            }

            return normalized;
        }

        private static string TryGetScenePathFromSuperController(SuperController sc)
        {
            try
            {
                var t = sc.GetType();
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

                string[] candidateNames =
                {
                    "currentSaveName",
                    "currentSaveFile",
                    "currentScenePath",
                    "currentSceneSaveName",
                    "lastSaveName",
                    "lastScenePath",
                    "sceneFilePath",
                    "currentSceneFile",
                    "loadedScenePath",
                    "loadedSceneName"
                };

                for (int i = 0; i < candidateNames.Length; i++)
                {
                    string name = candidateNames[i];

                    try
                    {
                        var p = t.GetProperty(name, flags);
                        if (p != null && p.PropertyType == typeof(string))
                        {
                            string value = p.GetValue(sc, null) as string;
                            if (LooksLikeScenePath(value)) return value;
                        }
                    }
                    catch { }

                    try
                    {
                        var f = t.GetField(name, flags);
                        if (f != null && f.FieldType == typeof(string))
                        {
                            string value = f.GetValue(sc) as string;
                            if (LooksLikeScenePath(value)) return value;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static bool LooksLikeScenePath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Replace('\\', '/').ToLowerInvariant();
            if (!v.EndsWith(".json")) return false;
            return v.Contains("saves/scene") || v.Contains(":/");
        }

        private static IEnumerator BuildSceneCacheCoroutine(string scenePath)
        {
            ThrottledLog("[VPB] On-demand cache start: " + scenePath);

            try
            {
                yield return BuildCacheForSceneTexturesUnity(scenePath);
                ThrottledLog("[VPB] On-demand cache finished.");
                if (!s_BatchMode) EndUiJob(s_CancelRequested ? "Texture caching cancelled" : "Texture caching complete");
            }
            finally
            {
                s_OnDemandBusy = false;
            }
        }

        private static IEnumerator WorkerBuildSelectiveLocalCoroutine(Dictionary<string, List<TextureFlags>> internalLowerToFlags, Dictionary<string, string> internalLowerToOriginal)
        {
            if (internalLowerToOriginal == null || internalLowerToOriginal.Count == 0) yield break;

            foreach (var kv in internalLowerToOriginal)
            {
                if (s_CancelRequested) yield break;
                string internalLower = kv.Key;
                string internalPath = kv.Value;
                if (string.IsNullOrEmpty(internalPath)) continue;

                IEnumerator work = null;
                try
                {
                    List<TextureFlags> variants;
                    if (internalLowerToFlags == null || !internalLowerToFlags.TryGetValue(internalLower, out variants) || variants == null || variants.Count == 0)
                    {
                        variants = new List<TextureFlags>
                        {
                            new TextureFlags
                            {
                                compress = true,
                                linear = false,
                                isNormalMap = false,
                                createAlphaFromGrayscale = false,
                                createNormalFromBump = false,
                                invert = false,
                                isReadable = false,
                                bumpStrength = 1f
                            }
                        };
                    }

                    string localPath = "SELF:/" + (internalPath ?? string.Empty);
                    if (TryFileEntryExists(localPath))
                    {
                        work = WriteNativeCacheForImageVariantsCoroutine(localPath, internalPath, variants, 0, default(DateTime));
                    }
                }
                catch { work = null; }

                if (work != null) yield return work;

                s_TexturesProcessed++;
                if (!s_BatchMode)
                {
                    s_ProcessedWork++;
                }
                UpdateUiStatusThrottled();
            }
        }

        private static IEnumerator BuildPackageCacheCoroutine(string packagePath)
        {
            ThrottledLog("[VPB] On-demand package cache start: " + packagePath);

            try
            {
                yield return BuildCacheForSelectedPackageUnity(packagePath);
                ThrottledLog("[VPB] On-demand package cache finished.");
                if (!s_BatchMode) EndUiJob(s_CancelRequested ? "Texture caching cancelled" : "Texture caching complete");
            }
            finally
            {
                s_OnDemandBusy = false;
            }
        }

        private static IEnumerator PurgePackageCacheCoroutine(string packagePath)
        {
            ThrottledLog("[VPB] On-demand package purge start: " + packagePath);

            try
            {
                yield return PurgeCacheForSelectedPackageUnity(packagePath);
                ThrottledLog("[VPB] On-demand package purge finished.");
                if (!s_BatchMode) EndUiJob(s_CancelRequested ? "Texture purge cancelled" : "Texture purge complete");
            }
            finally
            {
                s_OnDemandBusy = false;
            }
        }

        private static IEnumerator PurgeSceneCacheCoroutine(string scenePath)
        {
            ThrottledLog("[VPB] On-demand scene purge start: " + scenePath);

            try
            {
                // Scene purge: purge cache entries for BOTH:
                // - local disk textures referenced by the scene (SELF:/Custom, SELF:/Saves)
                // - package textures referenced by the scene (dependency packages)
                yield return PurgeCacheForSceneTexturesUnity(scenePath);
                ThrottledLog("[VPB] On-demand scene purge finished.");
                if (!s_BatchMode) EndUiJob(s_CancelRequested ? "Texture purge cancelled" : "Texture purge complete");
            }
            finally
            {
                s_OnDemandBusy = false;
            }
        }

        private static IEnumerator PurgeCacheForSceneTexturesUnity(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) yield break;

            Trace("ScenePurgeStart: " + scenePath);

            string sceneText = null;
            try { sceneText = FileManager.ReadAllText(scenePath); }
            catch (Exception ex)
            {
                ThrottledLog("[VPB] On-demand purge abort: cannot read scene: " + ex.Message);
                yield break;
            }

            if (string.IsNullOrEmpty(sceneText))
            {
                ThrottledLog("[VPB] On-demand purge abort: empty scene");
                yield break;
            }

            // Determine self UID if the scene is inside a package; this affects resolution of relative refs.
            string selfUid = null;
            try
            {
                string normalized = scenePath.Replace('\\', '/');
                if (normalized.StartsWith("var:/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring("var:/".Length);
                }
                else if (normalized.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring("var:".Length);
                    if (!string.IsNullOrEmpty(normalized) && normalized[0] == '/') normalized = normalized.Substring(1);
                }
                int idx = normalized.IndexOf(":/", StringComparison.Ordinal);
                if (idx > 0)
                {
                    string pkgId = NormalizePackageId(normalized.Substring(0, idx));
                    VarPackage p = ResolvePackageWithFallback(pkgId);
                    if (p != null) selfUid = p.Uid;
                }
            }
            catch { }

            JSONNode sceneNode = null;
            try { sceneNode = JSON.Parse(sceneText); } catch { }
            if (sceneNode == null)
            {
                ThrottledLog("[VPB] On-demand purge abort: JSON parse failed");
                yield break;
            }

            var required = new List<RequiredTexture>();
            var jsonRefs = new List<RequiredJsonFile>();
            ExtractSceneUrlsRecursive(sceneNode, selfUid, required, jsonRefs);

            // Scene JSON typically contains an explicit "dependencies" object; use it so purge matches
            // the gallery's dependency count, not just textures referenced directly by the scene.
            HashSet<string> depIds = null;
            try { depIds = DependencyExtractor.ExtractDependenciesFromJson(sceneText, fastModeOnly: true); }
            catch { depIds = null; }

            if (required == null || required.Count == 0)
            {
                ThrottledLog("[VPB] On-demand purge: no texture URLs found");
                if (depIds == null || depIds.Count == 0) yield break;
            }

            var byPkgFlags = new Dictionary<string, Dictionary<string, List<TextureFlags>>>(StringComparer.OrdinalIgnoreCase);
            var byPkgOrig = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            var localFlags = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
            var localOrig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < required.Count; i++)
            {
                RequiredTexture rt = required[i];
                if (string.IsNullOrEmpty(rt.InternalPath)) continue;
                if (rt.PackageId != null && rt.PackageId.Length == 0)
                {
                    string internalLowerLocal = rt.InternalPath.ToLowerInvariant();
                    AddFlagVariant(localFlags, internalLowerLocal, rt.Flags);
                    if (!localOrig.ContainsKey(internalLowerLocal)) localOrig[internalLowerLocal] = rt.InternalPath;
                    continue;
                }

                if (string.IsNullOrEmpty(rt.PackageId)) continue;

                VarPackage pkg = ResolvePackageWithFallback(rt.PackageId);
                if (pkg == null) continue;

                string pkgUid = pkg.Uid;
                string internalLower = rt.InternalPath.ToLowerInvariant();

                Dictionary<string, List<TextureFlags>> flagsMap;
                if (!byPkgFlags.TryGetValue(pkgUid, out flagsMap))
                {
                    flagsMap = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
                    byPkgFlags[pkgUid] = flagsMap;
                }

                Dictionary<string, string> origMap;
                if (!byPkgOrig.TryGetValue(pkgUid, out origMap))
                {
                    origMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byPkgOrig[pkgUid] = origMap;
                }

                AddFlagVariant(flagsMap, internalLower, rt.Flags);
                if (!origMap.ContainsKey(internalLower)) origMap[internalLower] = rt.InternalPath;
            }

            int totalWork = 0;
            foreach (var kv in byPkgOrig)
            {
                if (kv.Value != null) totalWork += kv.Value.Count;
            }
            if (localOrig != null) totalWork += localOrig.Count;

            if (totalWork <= 0)
            {
                // Allow dependency-only purge (scene has deps but no resolvable texture URLs).
                if (depIds == null || depIds.Count == 0)
                {
                    ThrottledLog("[VPB] On-demand purge: nothing to purge (no resolvable textures).");
                    yield break;
                }
                totalWork = 0;
            }

            int plannedPackages = byPkgOrig.Count;
            if (depIds != null && depIds.Count > 0)
            {
                var plannedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in byPkgOrig.Keys) plannedUids.Add(k);
                foreach (var dep in depIds)
                {
                    if (string.IsNullOrEmpty(dep)) continue;
                    VarPackage dp = ResolvePackageWithFallback(dep);
                    if (dp == null) continue;
                    plannedUids.Add(dp.Uid);
                }
                plannedPackages = plannedUids.Count;
            }

            if (s_BatchMode)
            {
                s_TexturesPlanned += totalWork;
                UpdateUiStatus();
            }
            else
            {
                s_PackagesPlanned = plannedPackages;
                s_PackagesProcessed = 0;
                s_TexturesPlanned = totalWork;
                s_TexturesProcessed = 0;
                s_TotalWork = Math.Max(1, s_TexturesPlanned);
                s_ProcessedWork = 0;
                UpdateUiStatus();
            }

            int pkgIndex = 0;
            foreach (var kv in byPkgFlags)
            {
                string pkgUid = kv.Key;
                Dictionary<string, List<TextureFlags>> flagsMap = kv.Value;
                Dictionary<string, string> origMap;
                byPkgOrig.TryGetValue(pkgUid, out origMap);

                pkgIndex++;
                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex - 1;
                    UpdateUiStatus();
                }

                VarPackage pkg = ResolvePackageWithFallback(pkgUid);
                if (pkg == null) continue;
                s_PackagesResolved++;

                s_PurgeWorkerAnyDeletes = false;
                yield return WorkerPurgeSelectiveUnityCoroutine(pkg, flagsMap, origMap);
                if (s_PurgeWorkerAnyDeletes) s_PurgePackagesDeleted++;

                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex;
                    UpdateUiStatus();
                }
            }

            if (localOrig != null && localOrig.Count > 0)
            {
                s_PurgeWorkerAnyDeletes = false;
                yield return WorkerPurgeLocalSelectiveUnityCoroutine(localFlags, localOrig);
                if (s_PurgeWorkerAnyDeletes) s_PurgePackagesDeleted++;
            }

            // Additionally purge caches for explicit dependency packages (even if the scene doesn't reference their textures directly).
            if (depIds != null && depIds.Count > 0)
            {
                var depUidDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dep in depIds)
                {
                    if (s_CancelRequested) yield break;
                    if (string.IsNullOrEmpty(dep)) continue;
                    VarPackage dp = ResolvePackageWithFallback(dep);
                    if (dp == null) continue;
                    if (!depUidDedup.Add(dp.Uid)) continue;

                    s_PackagesResolved++;
                    s_CurrentPackage = dp.Uid;
                    if (!s_BatchMode)
                    {
                        s_PackagesProcessed = Math.Min(s_PackagesPlanned, s_PackagesProcessed + 1);
                    }
                    UpdateUiStatus();

                    s_PurgeWorkerAnyDeletes = false;
                    yield return PurgeCacheForPackagePresetsFollowDepsUnity(dp);
                    if (s_PurgeWorkerAnyDeletes) s_PurgePackagesDeleted++;
                }
            }
        }

        private static IEnumerator WorkerPurgeLocalSelectiveUnityCoroutine(Dictionary<string, List<TextureFlags>> internalLowerToFlags, Dictionary<string, string> internalLowerToOriginal)
        {
            if (internalLowerToOriginal == null || internalLowerToOriginal.Count == 0) yield break;

            foreach (var kv in internalLowerToOriginal)
            {
                if (s_CancelRequested) yield break;
                string internalLower = kv.Key;
                string internalPath = kv.Value;
                if (string.IsNullOrEmpty(internalPath)) continue;

                try
                {
                    List<TextureFlags> variants;
                    if (internalLowerToFlags == null || !internalLowerToFlags.TryGetValue(internalLower, out variants) || variants == null || variants.Count == 0)
                    {
                        variants = new List<TextureFlags>
                        {
                            new TextureFlags
                            {
                                compress = true,
                                linear = false,
                                isNormalMap = false,
                                createAlphaFromGrayscale = false,
                                createNormalFromBump = false,
                                invert = false,
                                isReadable = false,
                                bumpStrength = 1f
                            }
                        };
                    }

                    string imgUidPath = "SELF:/" + (internalPath ?? string.Empty);

                    for (int v = 0; v < variants.Count; v++)
                    {
                        TextureFlags flags = variants[v];
                        int[] sizedWidths;
                        int[] sizedHeights;
                        GetSizedCacheDimensionArrays(flags, internalPath, out sizedWidths, out sizedHeights);
                        for (int si = 0; si < sizedWidths.Length; si++)
                        {
                            int targetWidth = sizedWidths[si];
                            int targetHeight = sizedHeights[si];
                            int beforeDeletes = s_NativeDeletes + s_ZstdDeletes;

                            string nativePath = null;
                            try { nativePath = ResolveVaMNativeCachePath(imgUidPath, flags, targetWidth, targetHeight); }
                            catch { nativePath = null; }

                            if (!string.IsNullOrEmpty(nativePath))
                            {
                                TryDeleteFileAndMeta(nativePath, ref s_NativeDeletes);
                            }
                            else
                            {
                                TryDeleteNativeCacheWildcard(imgUidPath, flags, targetWidth, targetHeight);
                            }

                            string zstdPath = null;
                            try
                            {
                                zstdPath = TextureUtil.GetZstdCachePath(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength, flags.isReadable);
                            }
                            catch { zstdPath = null; }

                            if (!string.IsNullOrEmpty(zstdPath))
                            {
                                TryDeleteFileAndMeta(zstdPath, ref s_ZstdDeletes);
                            }

                            int afterDeletes = s_NativeDeletes + s_ZstdDeletes;
                            if (afterDeletes > beforeDeletes)
                            {
                                s_PurgeTexturesDeleted++;
                                s_PurgeWorkerAnyDeletes = true;
                            }
                        }
                    }
                }
                catch { }

                yield return null;

                s_TexturesProcessed++;
                if (!s_BatchMode)
                {
                    s_ProcessedWork++;
                }
                UpdateUiStatus();
            }
        }

        private static IEnumerator BuildCacheForSceneTexturesUnity(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) yield break;

            Trace("SceneStart: " + scenePath);

            string sceneText = null;
            try { sceneText = FileManager.ReadAllText(scenePath); }
            catch (Exception ex)
            {
                ThrottledLog("[VPB] On-demand cache abort: cannot read scene: " + ex.Message);
                yield break;
            }

            if (string.IsNullOrEmpty(sceneText))
            {
                ThrottledLog("[VPB] On-demand cache abort: empty scene");
                yield break;
            }

            string selfUid = null;
            try
            {
                string normalized = scenePath.Replace('\\', '/');
                if (normalized.StartsWith("var:/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring("var:/".Length);
                }
                else if (normalized.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring("var:".Length);
                    if (!string.IsNullOrEmpty(normalized) && normalized[0] == '/') normalized = normalized.Substring(1);
                }
                int idx = normalized.IndexOf(":/", StringComparison.Ordinal);
                if (idx > 0)
                {
                    string pkgId = NormalizePackageId(normalized.Substring(0, idx));
                    VarPackage p = ResolvePackageWithFallback(pkgId);
                    if (p != null) selfUid = p.Uid;
                }
            }
            catch { }

            JSONNode sceneNode = null;
            try { sceneNode = JSON.Parse(sceneText); } catch { }
            if (sceneNode == null)
            {
                ThrottledLog("[VPB] On-demand cache abort: JSON parse failed");
                yield break;
            }

            var required = new List<RequiredTexture>();
            var jsonRefs = new List<RequiredJsonFile>();
            ExtractSceneUrlsRecursive(sceneNode, selfUid, required, jsonRefs);
            try { ExtractEmbeddedVamImageRefs(sceneText, required); } catch { }
            try { MergeSceneDependencyPresetTextures(sceneText, required); } catch { }

            var visitedJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<KeyValuePair<RequiredJsonFile, int>>();
            if (jsonRefs != null)
            {
                for (int i = 0; i < jsonRefs.Count; i++)
                {
                    queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(jsonRefs[i], 1));
                }
            }

            while (queue.Count > 0)
            {
                var kv = queue.Dequeue();
                RequiredJsonFile rj = kv.Key;
                int depth = kv.Value;
                if (depth > JsonWalkMaxDepth) continue;
                if (string.IsNullOrEmpty(rj.PackageId) || string.IsNullOrEmpty(rj.InternalPath)) continue;

                string depPkgId = NormalizePackageId(rj.PackageId);

                VarPackage jp = ResolvePackageWithFallback(rj.PackageId);
                if (jp == null) continue;

                Trace("JsonDequeue: depth=" + depth + " pkgId='" + depPkgId + "' resolvedUid='" + jp.Uid + "' path='" + rj.InternalPath + "'");

                string jsonUidPath = jp.Uid + ":/" + rj.InternalPath;
                string visitKey = jsonUidPath.ToLowerInvariant();
                if (!visitedJson.Add(visitKey)) continue;

                string txt = null;
                try { txt = FileManager.ReadAllText(jsonUidPath); } catch { txt = null; }
                if (string.IsNullOrEmpty(txt)) continue;

                JSONNode n = null;
                try { n = JSON.Parse(txt); } catch { n = null; }
                if (n == null) continue;

                var nestedTex = new List<RequiredTexture>();
                var nestedJson = new List<RequiredJsonFile>();
                ExtractSceneUrlsRecursive(n, jp.Uid, rj.InternalPath, nestedTex, nestedJson);
                if (nestedTex != null && nestedTex.Count > 0) required.AddRange(nestedTex);
                if (nestedJson != null && nestedJson.Count > 0)
                {
                    for (int i = 0; i < nestedJson.Count; i++)
                    {
                        queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(nestedJson[i], depth + 1));
                    }
                }
            }

            if (required.Count == 0)
            {
                ThrottledLog("[VPB] On-demand cache: no texture URLs found");
                yield break;
            }

            var byPkgFlags = new Dictionary<string, Dictionary<string, List<TextureFlags>>>(StringComparer.OrdinalIgnoreCase);
            var byPkgOrig = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            var localFlags = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
            var localOrig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < required.Count; i++)
            {
                RequiredTexture rt = required[i];
                if (string.IsNullOrEmpty(rt.InternalPath)) continue;

                if (rt.PackageId != null && rt.PackageId.Length == 0)
                {
                    string internalLowerLocal = rt.InternalPath.ToLowerInvariant();
                    AddFlagVariant(localFlags, internalLowerLocal, rt.Flags);
                    if (!localOrig.ContainsKey(internalLowerLocal)) localOrig[internalLowerLocal] = rt.InternalPath;
                    continue;
                }

                if (string.IsNullOrEmpty(rt.PackageId)) continue;

                VarPackage pkg = ResolvePackageWithFallback(rt.PackageId);
                if (pkg == null) continue;

                string pkgUid = pkg.Uid;
                string internalLower = rt.InternalPath.ToLowerInvariant();

                Dictionary<string, List<TextureFlags>> flagsMap;
                if (!byPkgFlags.TryGetValue(pkgUid, out flagsMap))
                {
                    flagsMap = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
                    byPkgFlags[pkgUid] = flagsMap;
                }

                Dictionary<string, string> origMap;
                if (!byPkgOrig.TryGetValue(pkgUid, out origMap))
                {
                    origMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byPkgOrig[pkgUid] = origMap;
                }

                AddFlagVariant(flagsMap, internalLower, rt.Flags);
                if (!origMap.ContainsKey(internalLower))
                {
                    origMap[internalLower] = rt.InternalPath;
                }
            }

            ExpandCachedPackagesToNewestInstalled(byPkgFlags, byPkgOrig);

            int totalWork = 0;
            foreach (var kv in byPkgOrig)
            {
                if (kv.Value != null) totalWork += kv.Value.Count;
            }

            if (localOrig != null) totalWork += localOrig.Count;

            if (s_BatchMode)
            {
                s_TexturesPlanned += totalWork;
                UpdateUiStatus();
            }
            else
            {
                s_PackagesPlanned = byPkgOrig.Count;
                s_PackagesProcessed = 0;
                s_TexturesPlanned = totalWork;
                s_TexturesProcessed = 0;
                s_TotalWork = Math.Max(1, totalWork);
                s_ProcessedWork = 0;
                UpdateUiStatus();
            }

            int pkgIndex = 0;
            foreach (var kv in byPkgFlags)
            {
                string pkgUid = kv.Key;
                Dictionary<string, List<TextureFlags>> flagsMap = kv.Value;

                Dictionary<string, string> origMap;
                byPkgOrig.TryGetValue(pkgUid, out origMap);

                VarPackage pkg = ResolvePackageWithFallback(pkgUid);
                if (pkg == null)
                {
                    int missing = (origMap != null) ? origMap.Count : 0;
                    if (missing > 0)
                    {
                        if (s_BatchMode)
                        {
                            s_TexturesPlanned = Math.Max(0, s_TexturesPlanned - missing);
                        }
                        else
                        {
                            s_TexturesPlanned = Math.Max(0, s_TexturesPlanned - missing);
                            s_TotalWork = Math.Max(1, s_TexturesPlanned);
                            s_PackagesPlanned = Math.Max(0, s_PackagesPlanned - 1);
                        }
                    }
                    UpdateUiStatus();
                    continue;
                }

                pkgIndex++;

                s_CurrentPackage = pkg.Uid;
                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex - 1;
                    UpdateUiStatus();
                }

                yield return WorkerBuildSelectiveUnityCoroutine(pkg, flagsMap, origMap);

                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex;
                    UpdateUiStatusThrottled(force: true);
                }
            }

            if (localOrig != null && localOrig.Count > 0)
            {
                yield return WorkerBuildSelectiveLocalCoroutine(localFlags, localOrig);
            }
        }

        internal struct TextureFlags
        {
            public bool compress;
            public bool linear;
            public bool isNormalMap;
            public bool createAlphaFromGrayscale;
            public bool createNormalFromBump;
            public bool invert;
            public bool isReadable;
            public float bumpStrength;
        }

        private struct RequiredTexture
        {
            public string PackageId;
            public string InternalPath;
            public TextureFlags Flags;
        }

        private struct RequiredJsonFile
        {
            public string PackageId;
            public string InternalPath;
        }

        private struct ImageEntry
        {
            public string InternalPath;
            public DateTime EntryTime;
            public long EntrySize;
        }

        private static List<ImageEntry> EnumerateAllImagesInVar(VarPackage pkg)
        {
            var list = new List<ImageEntry>();
            if (pkg == null) return list;

            string varPath = pkg.Path;
            if (string.IsNullOrEmpty(varPath)) return list;
            if (!File.Exists(varPath)) return list;

            try
            {
                using (FileStream fs = File.Open(varPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete))
                using (ZipFile zf = new ZipFile(fs))
                {
                    zf.IsStreamOwner = false;
                    foreach (ZipEntry ze in zf)
                    {
                        if (ze == null || !ze.IsFile) continue;
                        string name = ze.Name;
                        if (string.IsNullOrEmpty(name)) continue;
                        string lower = name.ToLowerInvariant();
                        if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg"))
                        {
                            list.Add(new ImageEntry
                            {
                                InternalPath = name,
                                EntryTime = ze.DateTime,
                                EntrySize = ze.Size
                            });
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        private static string GetFlagsSignature(TextureFlags flags)
        {
            string sig = string.Empty;
            if (flags.compress) sig += "_C";
            if (flags.linear) sig += "_L";
            if (flags.isNormalMap) sig += "_N";
            if (flags.createAlphaFromGrayscale) sig += "_A";
            if (flags.createNormalFromBump) sig += "_BN" + flags.bumpStrength;
            if (flags.invert) sig += "_I";
            return sig;
        }

        private static void AddFlagVariant(Dictionary<string, List<TextureFlags>> map, string internalLower, TextureFlags flags)
        {
            if (map == null || string.IsNullOrEmpty(internalLower)) return;
            List<TextureFlags> list;
            if (!map.TryGetValue(internalLower, out list) || list == null)
            {
                list = new List<TextureFlags>();
                map[internalLower] = list;
            }

            string sig = GetFlagsSignature(flags);
            for (int i = 0; i < list.Count; i++)
            {
                if (GetFlagsSignature(list[i]) == sig) return;
            }

            list.Add(flags);
        }

        private static bool ShouldBuildSizedCache(TextureFlags flags, string internalPath)
        {
            if (flags.isNormalMap) return false;
            if (flags.createNormalFromBump) return false;
            if (!flags.compress) return false;
            if (!string.IsNullOrEmpty(internalPath))
            {
                string ext = Path.GetExtension(internalPath);
                if (!string.IsNullOrEmpty(ext))
                {
                    if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static void GetSizedCacheDimensionArrays(TextureFlags flags, string internalPath, out int[] sizedWidths, out int[] sizedHeights)
        {
            if (ShouldBuildSizedCache(flags, internalPath))
            {
                sizedWidths = new[] { 0, DefaultSizedCacheWidth };
                sizedHeights = new[] { 0, DefaultSizedCacheHeight };
            }
            else
            {
                sizedWidths = new[] { 0 };
                sizedHeights = new[] { 0 };
            }
        }

        private static string NormalizePackageId(string pkgId)
        {
            if (string.IsNullOrEmpty(pkgId)) return pkgId;
            string p = pkgId.Trim();
            p = p.Replace('\\', '/');
            if (p.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("AllPackages/".Length);
            }
            int slash = p.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < p.Length) p = p.Substring(slash + 1);
            if (p.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(0, p.Length - 4);
            }
            return p;
        }

        private static string ToLatestPackageId(string pkgId)
        {
            if (string.IsNullOrEmpty(pkgId)) return pkgId;

            string n = NormalizePackageId(pkgId);
            if (string.IsNullOrEmpty(n)) return n;

            // Drop a trailing .latest
            if (n.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
            {
                n = n.Substring(0, n.Length - ".latest".Length);
            }

            // If the last segment is an integer version, strip it.
            int lastDot = n.LastIndexOf('.');
            if (lastDot > 0 && lastDot + 1 < n.Length)
            {
                int v;
                if (int.TryParse(n.Substring(lastDot + 1), out v))
                {
                    n = n.Substring(0, lastDot);
                }
            }

            return n + ".latest";
        }

        private static readonly Regex s_EmbeddedVamImageRef = new Regex(
            @"([A-Za-z0-9][A-Za-z0-9_\-\.]*\.[A-Za-z0-9][A-Za-z0-9_\-\.]*\.\d+):/(Custom/[^\s""\\]+\.(?:png|jpg|jpeg|tif|tiff))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Scene saves embed exact package versions (e.g. MacGruber.PostMagic.4) outside preset JSON trees.</summary>
        private static void ExtractEmbeddedVamImageRefs(string text, List<RequiredTexture> outTextures)
        {
            if (string.IsNullOrEmpty(text) || outTextures == null) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection matches = s_EmbeddedVamImageRef.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                Match m = matches[i];
                if (!m.Success || m.Groups.Count < 3) continue;

                string pkgId = NormalizePackageId(m.Groups[1].Value);
                string internalPath = NormalizeInternalPath(m.Groups[2].Value);
                if (string.IsNullOrEmpty(pkgId) || string.IsNullOrEmpty(internalPath)) continue;

                string dedupe = pkgId + "|" + internalPath.ToLowerInvariant();
                if (!seen.Add(dedupe)) continue;

                TextureFlags flags = ResolveTextureFlags(null, null, internalPath);

                outTextures.Add(new RequiredTexture
                {
                    PackageId = pkgId,
                    InternalPath = internalPath,
                    Flags = flags
                });

                Trace("FoundTexture: key='embedded' pkg='" + pkgId + "' path='" + internalPath + "' flags=" + GetFlagsSignature(flags));
            }
        }

        /// <summary>Mirror preset-resolved packages to newest installed UID so runtime version bumps still get pre-cached.</summary>
        private static void ExpandCachedPackagesToNewestInstalled(
            Dictionary<string, Dictionary<string, List<TextureFlags>>> byPkgFlags,
            Dictionary<string, Dictionary<string, string>> byPkgOrig)
        {
            if (byPkgFlags == null || byPkgOrig == null || byPkgFlags.Count == 0) return;

            var mirrorFrom = new List<string>(byPkgFlags.Keys);
            for (int i = 0; i < mirrorFrom.Count; i++)
            {
                string sourceUid = mirrorFrom[i];
                string newestUid = VamOnDemandLoader.TryGetNewestInstalledUid(sourceUid);
                if (string.IsNullOrEmpty(newestUid)
                    || newestUid.Equals(sourceUid, StringComparison.OrdinalIgnoreCase)
                    || ResolvePackageWithFallback(newestUid) == null)
                {
                    continue;
                }

                Dictionary<string, List<TextureFlags>> srcFlags;
                Dictionary<string, string> srcOrig;
                if (!byPkgFlags.TryGetValue(sourceUid, out srcFlags) || srcFlags == null) continue;
                byPkgOrig.TryGetValue(sourceUid, out srcOrig);

                Dictionary<string, List<TextureFlags>> dstFlags;
                if (!byPkgFlags.TryGetValue(newestUid, out dstFlags))
                {
                    dstFlags = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
                    byPkgFlags[newestUid] = dstFlags;
                }

                Dictionary<string, string> dstOrig;
                if (!byPkgOrig.TryGetValue(newestUid, out dstOrig))
                {
                    dstOrig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byPkgOrig[newestUid] = dstOrig;
                }

                foreach (KeyValuePair<string, List<TextureFlags>> kv in srcFlags)
                {
                    if (kv.Value == null) continue;
                    for (int f = 0; f < kv.Value.Count; f++)
                    {
                        AddFlagVariant(dstFlags, kv.Key, kv.Value[f]);
                    }
                    string origPath;
                    if (srcOrig != null && srcOrig.TryGetValue(kv.Key, out origPath) && !dstOrig.ContainsKey(kv.Key))
                    {
                        dstOrig[kv.Key] = origPath;
                    }
                }

                Trace("ExpandPkg: source='" + sourceUid + "' newest='" + newestUid + "' textures=" + srcFlags.Count);

                // Drop stale package bucket so we only build under newest UID (avoids PostMagic.3 fail then .4 retry).
                byPkgFlags.Remove(sourceUid);
                byPkgOrig.Remove(sourceUid);
            }
        }

        private static VarPackage ResolvePackageWithFallback(string pkgId)
        {
            if (string.IsNullOrEmpty(pkgId)) return null;

            string depPkgId = NormalizePackageId(pkgId);
            VarPackage p = null;

            try { p = FileManager.GetPackageForDependency(depPkgId, true); } catch { p = null; }
            if (p != null) return p;

            // First try creator.asset.latest (or keep .latest if already).
            string latest = ToLatestPackageId(depPkgId);
            if (!string.IsNullOrEmpty(latest) && !latest.Equals(depPkgId, StringComparison.OrdinalIgnoreCase))
            {
                try { p = FileManager.GetPackageForDependency(latest, true); } catch { p = null; }
                if (p != null) return p;
            }

            // Back-compat: if caller passed a base id without version, allow base.latest.
            if (!depPkgId.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
            {
                try { p = FileManager.GetPackageForDependency(depPkgId + ".latest", true); } catch { p = null; }
                if (p != null) return p;
            }

            return null;
        }

        private static string StripSuffixAfterKnownExtension(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return internalPath;
            string p = internalPath;
            string lower = p.ToLowerInvariant();
            string[] exts = new[] { ".png", ".jpg", ".jpeg", ".vap", ".json", ".vaj", ".vam", ".vmi" };
            int bestEnd = -1;
            for (int i = 0; i < exts.Length; i++)
            {
                int idx = lower.LastIndexOf(exts[i], StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int end = idx + exts[i].Length;
                    if (end > bestEnd) bestEnd = end;
                }
            }
            if (bestEnd > 0 && bestEnd < p.Length)
            {
                return p.Substring(0, bestEnd);
            }
            return p;
        }

        private static bool LooksLikeVamFileRef(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value;
            int idx = v.IndexOf(":/", StringComparison.Ordinal);
            if (idx <= 0) return false;
            string l = v.ToLowerInvariant();
            return l.Contains(".png") || l.Contains(".jpg") || l.Contains(".jpeg")
                || l.Contains(".vap") || l.Contains(".json") || l.Contains(".vaj") || l.Contains(".vam") || l.Contains(".vmi");
        }

        private static bool LooksLikeImagePath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Trim().ToLowerInvariant();
            return v.EndsWith(".png") || v.EndsWith(".jpg") || v.EndsWith(".jpeg") || v.EndsWith(".tif") || v.EndsWith(".tiff");
        }

        private static bool IsTextureJsonKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (key.IndexOf("Url", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (key.StartsWith("customTexture_", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.StartsWith("simTexture", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsPackageSceneJsonPath(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return false;
            return internalPath.IndexOf("Saves/scene", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeInternalPath(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return internalPath;
            string p = internalPath.Replace('\\', '/');
            if (p.StartsWith("/", StringComparison.Ordinal)) p = p.Substring(1);

            // Collapse path segments like ./ and ../ anywhere in the path.
            // This matters because many .vaj files use './texture/foo.png'.
            string[] parts = p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var stack = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part) || part == ".") continue;
                if (part == "..")
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                stack.Add(part);
            }
            return string.Join("/", stack.ToArray());
        }

        private static string GetInternalDirectory(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return null;
            string p = internalPath.Replace('\\', '/');
            int slash = p.LastIndexOf('/');
            if (slash <= 0) return null;
            return p.Substring(0, slash);
        }

        private static bool TryResolveTextureRef(string rawValue, string selfPackageUid, string referencingInternalPath, out string pkgId, out string internalPath)
        {
            pkgId = null;
            internalPath = null;
            if (string.IsNullOrEmpty(rawValue)) return false;

            if (TryParseVamRef(rawValue, selfPackageUid, out pkgId, out internalPath))
            {
                Trace("ResolveRef: vamref raw='" + rawValue + "' -> pkg='" + pkgId + "' path='" + internalPath + "'");
                return true;
            }

            if (!LooksLikeImagePath(rawValue)) return false;

            // Local scenes / presets often reference images by VaM-relative disk path (e.g. "Custom/Textures/foo.png")
            // without a package prefix ("pkg:/..."). In that case, treat it like a SELF reference so the scene-cache
            // local lane (PackageId == "") can process it.
            if (string.IsNullOrEmpty(selfPackageUid))
            {
                string localRel = rawValue.Trim().Replace('\\', '/');
                if (localRel.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    localRel = localRel.Substring(7);
                else if (localRel.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    localRel = localRel.Substring(5);

                // Normalize leading slash, and strip "AllPackages/" if present.
                localRel = localRel.TrimStart('/');
                if (localRel.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
                    localRel = localRel.Substring("AllPackages/".Length);

                // Accept common VaM-local roots. (AddonPackages/AllPackages are package repos; local content is typically Custom/ or Saves/.)
                if (localRel.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                    localRel.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase))
                {
                    pkgId = string.Empty;
                    internalPath = StripSuffixAfterKnownExtension(NormalizeInternalPath(localRel));
                    Trace("ResolveRef: local raw='" + rawValue + "' -> SELF path='" + internalPath + "'");
                    return !string.IsNullOrEmpty(internalPath);
                }

                return false;
            }

            pkgId = NormalizePackageId(selfPackageUid);

            string rel = rawValue.Trim().Replace('\\', '/');
            if (rel.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                rel = rel.Substring("AllPackages/".Length);
            }

            if (rel.StartsWith("./", StringComparison.Ordinal) || rel.StartsWith("../", StringComparison.Ordinal))
            {
                string dir = GetInternalDirectory(referencingInternalPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    rel = dir + "/" + rel;
                }
            }

            if (rel.IndexOf('/') < 0)
            {
                string dir = GetInternalDirectory(referencingInternalPath);
                if (!string.IsNullOrEmpty(dir)) rel = dir + "/" + rel;
            }

            internalPath = StripSuffixAfterKnownExtension(NormalizeInternalPath(rel));
            Trace("ResolveRef: rel raw='" + rawValue + "' ref='" + (referencingInternalPath ?? string.Empty) + "' -> pkg='" + pkgId + "' path='" + internalPath + "'");
            return !string.IsNullOrEmpty(internalPath);
        }

        private static bool TryParseVamRef(string rawValue, string selfPackageUid, out string pkgId, out string internalPath)
        {
            pkgId = null;
            internalPath = null;

            if (string.IsNullOrEmpty(rawValue)) return false;

            string normalized = rawValue.Trim();
            normalized = normalized.Replace('\\', '/');

            if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(7);
            }
            else if (normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(5);
            }

            if (normalized.StartsWith("var:/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("var:/".Length);
            }
            else if (normalized.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("var:".Length);
                if (!string.IsNullOrEmpty(normalized) && normalized[0] == '/') normalized = normalized.Substring(1);
            }

            if (!string.IsNullOrEmpty(selfPackageUid) && normalized.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = selfPackageUid + ":/" + normalized.Substring("SELF:/".Length);
            }
            else if (!string.IsNullOrEmpty(selfPackageUid) && normalized.StartsWith("SELF:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = selfPackageUid + ":" + normalized.Substring("SELF:".Length);
            }

            if (normalized.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase))
            {
                pkgId = string.Empty;
                internalPath = normalized.Substring("SELF:/".Length);
                if (!string.IsNullOrEmpty(internalPath) && internalPath[0] == '/') internalPath = internalPath.Substring(1);
                internalPath = internalPath.Replace('\\', '/');
                int qSelf = internalPath.IndexOfAny(new[] { '?', '#' });
                if (qSelf >= 0) internalPath = internalPath.Substring(0, qSelf);
                try { internalPath = Uri.UnescapeDataString(internalPath); } catch { }
                internalPath = StripSuffixAfterKnownExtension(internalPath);
                return !string.IsNullOrEmpty(internalPath);
            }

            if (normalized.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("AllPackages/".Length);
            }

            int idx = normalized.IndexOf(":/", StringComparison.Ordinal);
            if (idx <= 0 || idx + 2 >= normalized.Length) return false;

            pkgId = NormalizePackageId(normalized.Substring(0, idx));
            internalPath = normalized.Substring(idx + 2);

            if (!string.IsNullOrEmpty(internalPath) && internalPath[0] == '/') internalPath = internalPath.Substring(1);
            internalPath = internalPath.Replace('\\', '/');
            int q = internalPath.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) internalPath = internalPath.Substring(0, q);
            try { internalPath = Uri.UnescapeDataString(internalPath); } catch { }
            internalPath = StripSuffixAfterKnownExtension(internalPath);

            return !string.IsNullOrEmpty(pkgId) && !string.IsNullOrEmpty(internalPath);
        }

        private static TextureFlags ResolveTextureFlags(string jsonKey, JSONClass parentObject, string internalPath = null)
        {
            TextureFlags flags;
            if (VaMTextureLoadFlags.TryResolve(jsonKey, parentObject, out flags))
                return flags;
            if (TryResolveOnDemandFlagsFallback(jsonKey, parentObject, internalPath, out flags))
                return flags;
            return VaMTextureLoadFlags.DefaultImageLoaderFlags();
        }

        /// <summary>Fallback when <see cref="VaMTextureLoadFlags.TryResolveHeuristic"/> is unavailable (older VaMTextureLoadFlags.cs).</summary>
        private static bool TryResolveOnDemandFlagsFallback(string jsonKey, JSONClass parentObject, string internalPath, out TextureFlags flags)
        {
            flags = VaMTextureLoadFlags.DefaultImageLoaderFlags();
            if (!string.IsNullOrEmpty(jsonKey))
            {
                if (TryResolveOnDemandKeyHeuristic(jsonKey, out flags))
                    return true;
                if (TryResolveOnDemandParentUrlSiblingFlags(jsonKey, parentObject, out flags))
                    return true;
            }

            if (!string.IsNullOrEmpty(internalPath))
            {
                string name;
                try { name = Path.GetFileNameWithoutExtension(internalPath); }
                catch { name = null; }
                if (!string.IsNullOrEmpty(name) && TryResolveOnDemandKeyHeuristic(name, out flags))
                    return true;
            }

            return false;
        }

        private static bool TryResolveOnDemandParentUrlSiblingFlags(string jsonKey, JSONClass parent, out TextureFlags flags)
        {
            flags = VaMTextureLoadFlags.DefaultImageLoaderFlags();
            if (parent == null || string.IsNullOrEmpty(jsonKey) || !jsonKey.EndsWith("Url", StringComparison.OrdinalIgnoreCase))
                return false;

            string stem = jsonKey.Substring(0, jsonKey.Length - 3);
            if (parent[stem + "IsLinear"] == null && parent[stem + "IsNormal"] == null && parent[stem + "IsTransparency"] == null)
                return false;

            bool isLinear = ReadOnDemandParentBool(parent, stem + "IsLinear", false);
            bool isNormal = ReadOnDemandParentBool(parent, stem + "IsNormal", false);
            bool isTransparency = ReadOnDemandParentBool(parent, stem + "IsTransparency", false);
            flags = VaMTextureLoadFlags.MaterialOptionsCustomFlags(isLinear, isNormal, isTransparency);
            return true;
        }

        private static bool TryResolveOnDemandKeyHeuristic(string jsonKey, out TextureFlags flags)
        {
            flags = VaMTextureLoadFlags.DefaultImageLoaderFlags();
            if (string.IsNullOrEmpty(jsonKey)) return false;

            string k = jsonKey.ToLowerInvariant();
            if (!k.Contains("url") && !k.Contains("texture") && !k.Contains("tex")
                && k.IndexOf("map", StringComparison.Ordinal) < 0 && !k.Contains("image"))
            {
                return false;
            }

            if (k.Contains("normal") || (k.Contains("bump") && !k.Contains("specular")))
            {
                if (k.Contains("bump") && !k.Contains("normal"))
                {
                    flags = VaMTextureLoadFlags.DefaultImageLoaderFlags();
                    flags.linear = true;
                    flags.createNormalFromBump = true;
                    flags.compress = false;
                    return true;
                }

                flags = VaMTextureLoadFlags.NormalMapFlags();
                return true;
            }

            if (k.Contains("specular") || k.Contains("gloss") || k.Contains("roughness")
                || k.Contains("metallic") || k.Contains("metalness") || k.Contains("reflection"))
            {
                flags = VaMTextureLoadFlags.SpecularOrGlossFlags();
                return true;
            }

            if (k.Contains("alpha") || k.Contains("opacity") || k.Contains("transparency") || k.Contains("mask"))
            {
                flags = VaMTextureLoadFlags.MaterialOptionsCustomFlags(isLinear: false, isNormalMap: false, isTransparency: true);
                return true;
            }

            if (k.Contains("decal") || k.Contains("diffuse") || k.Contains("albedo") || k.Contains("color"))
            {
                flags = VaMTextureLoadFlags.DiffuseFlags();
                return true;
            }

            if (k.EndsWith("url", StringComparison.OrdinalIgnoreCase))
            {
                flags = VaMTextureLoadFlags.DiffuseFlags();
                return true;
            }

            return false;
        }

        private static bool ReadOnDemandParentBool(JSONClass parent, string fieldName, bool defaultValue)
        {
            if (parent == null || string.IsNullOrEmpty(fieldName)) return defaultValue;
            JSONNode node = parent[fieldName];
            if (node == null) return defaultValue;
            try { return node.AsBool; }
            catch { return defaultValue; }
        }

        private static void ExtractSceneUrlsRecursive(JSONNode node, string selfPackageUid, List<RequiredTexture> outTextures, List<RequiredJsonFile> outJsonFiles)
        {
            ExtractSceneUrlsRecursive(node, selfPackageUid, null, outTextures, outJsonFiles);
        }

        private static void ExtractSceneUrlsRecursive(JSONNode node, string selfPackageUid, string referencingInternalPath, List<RequiredTexture> outTextures, List<RequiredJsonFile> outJsonFiles)
        {
            if (node == null) return;

            var obj = node.AsObject;
            if (obj != null)
            {
                if (outTextures != null)
                {
                    var dazEntries = new List<VaMDazImportMaterialExtractor.DazTextureEntry>();
                    if (VaMDazImportMaterialExtractor.TryCollectFromShaderMaterialNode(node, dazEntries))
                    {
                        for (int di = 0; di < dazEntries.Count; di++)
                        {
                            VaMDazImportMaterialExtractor.DazTextureEntry de = dazEntries[di];
                            if (string.IsNullOrEmpty(de.ImagePath)) continue;

                            string pkgId;
                            string internalPath;
                            if (!TryResolveTextureRef(de.ImagePath, selfPackageUid, referencingInternalPath, out pkgId, out internalPath))
                                continue;

                            string il = (internalPath ?? string.Empty).ToLowerInvariant();
                            if (!il.EndsWith(".png") && !il.EndsWith(".jpg") && !il.EndsWith(".jpeg") && !il.EndsWith(".tif") && !il.EndsWith(".tiff"))
                                continue;

                            outTextures.Add(new RequiredTexture
                            {
                                PackageId = pkgId,
                                InternalPath = internalPath,
                                Flags = de.Flags
                            });

                            Trace("FoundTexture: key='daz_import' pkg='" + (pkgId ?? string.Empty) + "' path='" + internalPath + "' flags=" + GetFlagsSignature(de.Flags));
                        }
                    }
                }

                foreach (KeyValuePair<string, JSONNode> kv in obj)
                {
                    string key = kv.Key;
                    JSONNode valueNode = kv.Value;

                    if (key.Equals("image_file", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("current_value", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractSceneUrlsRecursive(valueNode, selfPackageUid, referencingInternalPath, outTextures, outJsonFiles);
                        continue;
                    }

                    string rawUrl;
                    bool hasUrl = VaMTextureLoadFlags.TryUnwrapUrlValue(valueNode, out rawUrl);
                    bool deepSceneScan = IsPackageSceneJsonPath(referencingInternalPath);
                    bool isTextureCandidate = hasUrl && (
                        IsTextureJsonKey(key)
                        || LooksLikeVamFileRef(rawUrl)
                        || LooksLikeImagePath(rawUrl)
                        || (deepSceneScan && LooksLikeImagePath(rawUrl) && rawUrl.IndexOf(":/", StringComparison.Ordinal) > 0));
                    if (isTextureCandidate)
                    {
                        string pkgId;
                        string internalPath;
                        if (TryResolveTextureRef(rawUrl, selfPackageUid, referencingInternalPath, out pkgId, out internalPath))
                        {
                            string il = (internalPath ?? string.Empty).ToLowerInvariant();
                            if ((il.EndsWith(".png") || il.EndsWith(".jpg") || il.EndsWith(".jpeg") || il.EndsWith(".tif") || il.EndsWith(".tiff")) && outTextures != null)
                            {
                                TextureFlags flags = ResolveTextureFlags(key, obj, internalPath);
                                if (flags.isReadable && !string.IsNullOrEmpty(pkgId))
                                {
                                    try { SuperControllerHook.RegisterSimTexture(pkgId + ":/" + internalPath); } catch { }
                                }

                                outTextures.Add(new RequiredTexture
                                {
                                    PackageId = pkgId,
                                    InternalPath = internalPath,
                                    Flags = flags
                                });

                                Trace("FoundTexture: key='" + (key ?? string.Empty) + "' pkg='" + (pkgId ?? string.Empty) + "' path='" + (internalPath ?? string.Empty) + "' flags=" + GetFlagsSignature(flags));
                            }
                            else if ((il.EndsWith(".vap") || il.EndsWith(".json") || il.EndsWith(".vaj") || il.EndsWith(".vam") || il.EndsWith(".vmi")) && outJsonFiles != null)
                            {
                                outJsonFiles.Add(new RequiredJsonFile
                                {
                                    PackageId = pkgId,
                                    InternalPath = internalPath
                                });

                                Trace("FoundJsonRef: key='" + (key ?? string.Empty) + "' pkg='" + (pkgId ?? string.Empty) + "' path='" + (internalPath ?? string.Empty) + "'");

                                if (il.EndsWith(".vam") && !string.IsNullOrEmpty(internalPath))
                                {
                                    string vajPath = null;
                                    try { vajPath = internalPath.Substring(0, internalPath.Length - 4) + ".vaj"; } catch { vajPath = null; }
                                    if (!string.IsNullOrEmpty(vajPath))
                                    {
                                        outJsonFiles.Add(new RequiredJsonFile
                                        {
                                            PackageId = pkgId,
                                            InternalPath = vajPath
                                        });

                                        Trace("FoundJsonRef: derived vaj pkg='" + (pkgId ?? string.Empty) + "' path='" + vajPath + "'");
                                    }
                                }
                            }
                        }
                    }

                    ExtractSceneUrlsRecursive(valueNode, selfPackageUid, referencingInternalPath, outTextures, outJsonFiles);
                }
                return;
            }

            var arr = node.AsArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    ExtractSceneUrlsRecursive(arr[i], selfPackageUid, referencingInternalPath, outTextures, outJsonFiles);
                }
            }
        }

        private static void ConfigureQueuedImageFlags(CustomImageLoaderThreaded.QueuedImage qi, string imgUidPath, TextureFlags flags, int targetWidth, int targetHeight)
        {
            if (qi == null) return;
            qi.imgPath = imgUidPath;
            qi.compress = flags.compress;
            qi.linear = flags.linear;
            qi.isNormalMap = flags.isNormalMap;
            qi.createAlphaFromGrayscale = flags.createAlphaFromGrayscale;
            qi.createNormalFromBump = flags.createNormalFromBump;
            qi.invert = flags.invert;
            qi.bumpStrength = flags.bumpStrength;
            qi.createMipMaps = true;
            qi.isThumbnail = false;
            qi.setSize = targetWidth > 0 && targetHeight > 0;
            if (qi.setSize)
            {
                qi.width = targetWidth;
                qi.height = targetHeight;
            }
        }

        /// <summary>VaM-native cache filename (same rules as <see cref="CustomImageLoaderThreaded.QueuedImage.GetDiskCachePath"/>).</summary>
        private static string ResolveVaMNativeCachePath(string imgUidPath, TextureFlags flags, int targetWidth, int targetHeight)
        {
            CustomImageLoaderThreaded loader = CustomImageLoaderThreaded.singleton;
            if (loader == null || string.IsNullOrEmpty(imgUidPath)) return null;
            CustomImageLoaderThreaded.QueuedImage qi = loader.GetQI();
            try
            {
                ConfigureQueuedImageFlags(qi, imgUidPath, flags, targetWidth, targetHeight);
                return qi.ResolveDiskCachePath();
            }
            finally
            {
                loader.ReturnQueuedImage(qi);
            }
        }

        private static bool IsOnDemandSeedPath(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return false;
            string p = internalPath.Replace('\\', '/');
            if (p.IndexOf("__MACOSX", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (p.Equals("meta.json", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("/meta.json", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".vaj", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseImageDimensionsFromBuffer(byte[] buf, int len, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (buf == null || len < 24) return false;

            if (len >= 24 && buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47)
            {
                width = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
                height = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
                return width > 0 && height > 0;
            }

            if (len >= 4 && buf[0] == 0xFF && buf[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < len)
                {
                    if (buf[i] != 0xFF)
                    {
                        i++;
                        continue;
                    }

                    int marker = buf[i + 1];
                    if (marker == 0xD8)
                    {
                        i += 2;
                        continue;
                    }

                    if (marker == 0xD9) break;

                    int segLen = (buf[i + 2] << 8) | buf[i + 3];
                    if (segLen < 2 || i + 2 + segLen > len) break;

                    if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2 || marker == 0xC3
                        || marker == 0xC5 || marker == 0xC6 || marker == 0xC7
                        || marker == 0xC9 || marker == 0xCA || marker == 0xCB)
                    {
                        if (i + 7 < len)
                        {
                            height = (buf[i + 5] << 8) | buf[i + 6];
                            width = (buf[i + 7] << 8) | buf[i + 8];
                            return width > 0 && height > 0;
                        }
                        break;
                    }

                    i += 2 + segLen;
                }
            }

            return false;
        }

        private static bool TryReadImageDimensionsPartial(string imgUidPath, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(imgUidPath)) return false;

            try
            {
                using (FileEntryStream fes = FileManager.OpenStream(imgUidPath))
                {
                    Stream stream = fes != null ? fes.Stream : null;
                    if (stream == null) return false;

                    long length = 0;
                    try { length = stream.Length; } catch { length = 0; }
                    int toRead = DownscaleDimensionProbeMaxBytes;
                    if (length > 0 && length < toRead) toRead = (int)length;
                    if (toRead < 16) return false;

                    byte[] buf = new byte[toRead];
                    int off = 0;
                    while (off < toRead)
                    {
                        int n = stream.Read(buf, off, toRead - off);
                        if (n <= 0) break;
                        off += n;
                    }

                    if (off < 16) return false;
                    return TryParseImageDimensionsFromBuffer(buf, off, out width, out height);
                }
            }
            catch
            {
                return false;
            }
        }

        private static long TryGetOnDemandFileEntrySize(string imgUidPath)
        {
            if (string.IsNullOrEmpty(imgUidPath)) return 0;
            try
            {
                FileEntry fileEntry = FileManager.GetFileEntry(imgUidPath);
                if (fileEntry == null) return 0;
                long size = fileEntry.Size;
                try
                {
                    VarFileEntry vfe = fileEntry as VarFileEntry;
                    if (vfe != null) size = vfe.EntrySize;
                }
                catch { }
                return size > 0 ? size : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string MakeRequiredTextureDedupeKey(RequiredTexture rt)
        {
            if (rt.InternalPath == null) return string.Empty;
            return (rt.PackageId ?? string.Empty) + "|" + rt.InternalPath + "|" + GetFlagsSignature(rt.Flags);
        }

        /// <summary>Add textures referenced from dependency VAR presets (scene.dependencies), deduped against scene graph.</summary>
        private static void MergeSceneDependencyPresetTextures(string sceneText, List<RequiredTexture> required)
        {
            if (string.IsNullOrEmpty(sceneText) || required == null) return;

            HashSet<string> depIds = null;
            try { depIds = DependencyExtractor.ExtractDependenciesFromJson(sceneText, fastModeOnly: true); }
            catch { depIds = null; }
            if (depIds == null || depIds.Count == 0) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < required.Count; i++)
            {
                string key = MakeRequiredTextureDedupeKey(required[i]);
                if (!string.IsNullOrEmpty(key)) seen.Add(key);
            }

            foreach (string dep in depIds)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                VarPackage dp = ResolvePackageWithFallback(dep);
                if (dp == null) continue;

                List<RequiredTexture> depRequired = BuildRequiredTexturesFromPackagePresetsFollowDeps(dp);
                if (depRequired == null || depRequired.Count == 0) continue;

                Trace("SceneDepPresetWalk: dep='" + dp.Uid + "' textures=" + depRequired.Count);

                for (int i = 0; i < depRequired.Count; i++)
                {
                    RequiredTexture rt = depRequired[i];
                    if (string.IsNullOrEmpty(rt.InternalPath)) continue;
                    string dedupeKey = MakeRequiredTextureDedupeKey(rt);
                    if (string.IsNullOrEmpty(dedupeKey) || !seen.Add(dedupeKey)) continue;
                    required.Add(rt);
                }
            }
        }

        private static bool TryComputeZstdDownscaleDimensions(string imgUidPath, int targetWidth, int targetHeight, out int outW, out int outH, out int sourceW, out int sourceH)
        {
            outW = 0;
            outH = 0;
            sourceW = 0;
            sourceH = 0;
            if (string.IsNullOrEmpty(imgUidPath)) return false;

            long entryBytes = TryGetOnDemandFileEntrySize(imgUidPath);
            if (entryBytes > 0 && entryBytes < DownscaleProbeMinEntryBytes)
            {
                return false;
            }

            int sw = 0;
            int sh = 0;
            if (!TryReadImageDimensionsPartial(imgUidPath, out sw, out sh))
            {
                Texture2D probe = null;
                try
                {
                    byte[] src = FileManager.ReadAllBytes(imgUidPath);
                    if (src == null || src.Length == 0) return false;
                    probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!probe.LoadImage(src)) return false;
                    sw = probe.width;
                    sh = probe.height;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    if (probe != null) UnityEngine.Object.Destroy(probe);
                }
            }

            if (targetWidth > 0) sw = targetWidth;
            if (targetHeight > 0) sh = targetHeight;

            sourceW = sw;
            sourceH = sh;

            if (sw <= 0 || sh <= 0) return false;
            if (sw <= 4096 && sh <= 4096) return false;
            if (!Mathf.IsPowerOfTwo(sw) || !Mathf.IsPowerOfTwo(sh)) return false;

            int div = 1;
            while ((sw / div) > 4096 || (sh / div) > 4096)
            {
                div *= 2;
                if (div <= 0) return false;
            }

            outW = sw / div;
            outH = sh / div;
            return outW > 0 && outH > 0;
        }

        private sealed class OnDemandLoaderResultSlot
        {
            public CustomImageLoaderThreaded.OnDemandCacheBuildResult Value;
        }

        /// <summary>Decode on thread pool when possible; compress + zstd/native write on main thread.</summary>
        private static IEnumerator CoBuildViaLoader(
            string imgUidPath,
            TextureFlags flags,
            int targetWidth,
            int targetHeight,
            bool decodeFromSource,
            bool suppressNativeDiskWrite,
            OnDemandLoaderResultSlot slot)
        {
            slot.Value = default(CustomImageLoaderThreaded.OnDemandCacheBuildResult);

            if (!decodeFromSource)
            {
                slot.Value = BuildViaLoader(imgUidPath, flags, targetWidth, targetHeight, false, suppressNativeDiskWrite);
                yield break;
            }

            OnDemandBackgroundDecode.Job job = OnDemandBackgroundDecode.Start(
                imgUidPath,
                targetWidth > 0 && targetHeight > 0,
                targetWidth,
                targetHeight,
                flags.compress,
                flags.linear,
                flags.isNormalMap,
                flags.createAlphaFromGrayscale,
                flags.createNormalFromBump,
                flags.bumpStrength,
                flags.invert);

            while (job != null && !job.IsDone)
            {
                if (s_CancelRequested)
                {
                    slot.Value = default(CustomImageLoaderThreaded.OnDemandCacheBuildResult);
                    slot.Value.error = "Cancelled";
                    yield break;
                }
                yield return null;
            }

            if (job != null && job.Success)
            {
                slot.Value = CustomImageLoaderThreaded.BuildOnDemandFinishFromDecoded(
                    imgUidPath,
                    job.Raw,
                    job.RawLength,
                    job.Width,
                    job.Height,
                    job.Format,
                    flags.compress,
                    flags.linear,
                    flags.isNormalMap,
                    flags.createAlphaFromGrayscale,
                    flags.createNormalFromBump,
                    flags.invert,
                    flags.bumpStrength,
                    suppressNativeDiskWrite);
                yield break;
            }

            slot.Value = BuildViaLoader(imgUidPath, flags, targetWidth, targetHeight, true, suppressNativeDiskWrite);
        }

        private static CustomImageLoaderThreaded.OnDemandCacheBuildResult BuildViaLoader(
            string imgUidPath,
            TextureFlags flags,
            int targetWidth,
            int targetHeight,
            bool decodeFromSourceOnly,
            bool suppressNativeDiskWrite)
        {
            return CustomImageLoaderThreaded.BuildOnDemandViaLoader(
                imgUidPath,
                flags.compress,
                flags.linear,
                flags.isNormalMap,
                flags.createAlphaFromGrayscale,
                flags.createNormalFromBump,
                flags.invert,
                flags.bumpStrength,
                targetWidth,
                targetHeight,
                decodeFromSourceOnly,
                suppressNativeDiskWrite);
        }

        private static string GetNativeCachePathDynamic(string imgPath, TextureFlags flags, long explicitSize, DateTime explicitLastWriteTime, int targetWidth = 0, int targetHeight = 0)
        {
            FileEntry fileEntry = null;
            try { fileEntry = FileManager.GetFileEntry(imgPath); } catch { fileEntry = null; }
            string textureCacheDir = null;
            try { textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir(); } catch { textureCacheDir = null; }
            if (string.IsNullOrEmpty(textureCacheDir)) return null;

            long size = explicitSize;
            DateTime lastWriteTime = explicitLastWriteTime;

            if (fileEntry != null)
            {
                size = fileEntry.Size;
                try
                {
                    VarFileEntry vfe = fileEntry as VarFileEntry;
                    if (vfe != null) size = vfe.EntrySize;
                }
                catch { }

                lastWriteTime = fileEntry.LastWriteTime;
            }

            if (size <= 0) return null;
            if (lastWriteTime == default(DateTime)) return null;

            string sizeStr = size.ToString();
            string timeStr = lastWriteTime.ToFileTime().ToString();
            string fileName = Path.GetFileName(imgPath);
            try { fileName = TextureUtil.SanitizeFileName(fileName).Replace('.', '_'); }
            catch { fileName = fileName.Replace('.', '_'); }

            string sig = GetFlagsSignature(flags);
            if (targetWidth > 0 && targetHeight > 0) sig = targetWidth + "_" + targetHeight + sig;

            return textureCacheDir + "/" + fileName + "_" + sizeStr + "_" + timeStr + "_" + sig + ".vamcache";
        }

        private static void TryDeleteNativeCacheWildcard(string imgUidPath, TextureFlags flags, int targetWidth, int targetHeight)
        {
            if (string.IsNullOrEmpty(imgUidPath)) return;

            string exact = ResolveVaMNativeCachePath(imgUidPath, flags, targetWidth, targetHeight);
            if (!string.IsNullOrEmpty(exact))
            {
                TryDeleteFileAndMeta(exact, ref s_NativeDeletes);
                return;
            }

            string textureCacheDir = null;
            try { textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir(); } catch { textureCacheDir = null; }
            if (string.IsNullOrEmpty(textureCacheDir)) return;
            if (!Directory.Exists(textureCacheDir)) return;

            string fileName = Path.GetFileName(imgUidPath);
            try { fileName = TextureUtil.SanitizeFileName(fileName).Replace('.', '_'); }
            catch { fileName = (fileName ?? string.Empty).Replace('.', '_'); }

            string sig = GetFlagsSignature(flags);
            if (targetWidth > 0 && targetHeight > 0) sig = targetWidth + "_" + targetHeight + sig;

            string pattern = fileName + "_*_*_" + sig + ".vamcache";
            string[] matches = null;
            try { matches = Directory.GetFiles(textureCacheDir, pattern); }
            catch { matches = null; }
            if (matches == null || matches.Length == 0) return;

            for (int i = 0; i < matches.Length; i++)
            {
                string p = matches[i];
                if (string.IsNullOrEmpty(p)) continue;
                TryDeleteFileAndMeta(p, ref s_NativeDeletes);
            }
        }

        private static IEnumerator WorkerBuildSelectiveUnityCoroutine(VarPackage pkg, Dictionary<string, List<TextureFlags>> internalLowerToFlags, Dictionary<string, string> internalLowerToOriginal)
        {
            if (pkg == null || internalLowerToOriginal == null || internalLowerToOriginal.Count == 0) yield break;

            foreach (var kv in internalLowerToOriginal)
            {
                if (s_CancelRequested) yield break;
                string internalLower = kv.Key;
                string internalPath = kv.Value;
                if (string.IsNullOrEmpty(internalPath)) continue;

                IEnumerator work = null;
                try
                {
                    List<TextureFlags> variants;
                    if (internalLowerToFlags == null || !internalLowerToFlags.TryGetValue(internalLower, out variants) || variants == null || variants.Count == 0)
                    {
                        variants = new List<TextureFlags>
                        {
                            new TextureFlags
                            {
                                compress = true,
                                linear = false,
                                isNormalMap = false,
                                createAlphaFromGrayscale = false,
                                createNormalFromBump = false,
                                invert = false,
                                isReadable = false,
                                bumpStrength = 1f
                            }
                        };
                    }

                    string imgUidPath = pkg.Uid + ":/" + internalPath;
                    if (TryFileEntryExists(imgUidPath))
                    {
                        work = WriteNativeCacheForImageVariantsCoroutine(imgUidPath, internalPath, variants, 0, default(DateTime));
                    }
                }
                catch { work = null; }

                if (work != null) yield return work;

                s_TexturesProcessed++;
                if (!s_BatchMode)
                {
                    s_ProcessedWork++;
                }
                UpdateUiStatusThrottled();
            }
        }

        private static IEnumerator WorkerPurgeSelectiveUnityCoroutine(VarPackage pkg, Dictionary<string, List<TextureFlags>> internalLowerToFlags, Dictionary<string, string> internalLowerToOriginal)
        {
            if (pkg == null || internalLowerToOriginal == null || internalLowerToOriginal.Count == 0) yield break;

            foreach (var kv in internalLowerToOriginal)
            {
                if (s_CancelRequested) yield break;
                string internalLower = kv.Key;
                string internalPath = kv.Value;
                if (string.IsNullOrEmpty(internalPath)) continue;

                try
                {
                    List<TextureFlags> variants;
                    if (internalLowerToFlags == null || !internalLowerToFlags.TryGetValue(internalLower, out variants) || variants == null || variants.Count == 0)
                    {
                        variants = new List<TextureFlags>
                        {
                            new TextureFlags
                            {
                                compress = true,
                                linear = false,
                                isNormalMap = false,
                                createAlphaFromGrayscale = false,
                                createNormalFromBump = false,
                                invert = false,
                                isReadable = false,
                                bumpStrength = 1f
                            }
                        };
                    }

                    string imgUidPath = pkg.Uid + ":/" + internalPath;

                    for (int v = 0; v < variants.Count; v++)
                    {
                        TextureFlags flags = variants[v];
                        int[] sizedWidths;
                        int[] sizedHeights;
                        GetSizedCacheDimensionArrays(flags, internalPath, out sizedWidths, out sizedHeights);

                        for (int si = 0; si < sizedWidths.Length; si++)
                        {
                            int targetWidth = sizedWidths[si];
                            int targetHeight = sizedHeights[si];
                            int beforeDeletes = s_NativeDeletes + s_ZstdDeletes;

                            string nativePath = null;
                            try { nativePath = ResolveVaMNativeCachePath(imgUidPath, flags, targetWidth, targetHeight); }
                            catch { nativePath = null; }

                            if (!string.IsNullOrEmpty(nativePath))
                            {
                                TryDeleteFileAndMeta(nativePath, ref s_NativeDeletes);
                            }
                            else
                            {
                                TryDeleteNativeCacheWildcard(imgUidPath, flags, targetWidth, targetHeight);
                            }

                            string zstdPath = null;
                            try
                            {
                                zstdPath = TextureUtil.GetZstdCachePath(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength, flags.isReadable);
                            }
                            catch { zstdPath = null; }

                            if (!string.IsNullOrEmpty(zstdPath))
                            {
                                TryDeleteFileAndMeta(zstdPath, ref s_ZstdDeletes);
                            }

                            int afterDeletes = s_NativeDeletes + s_ZstdDeletes;
                            if (afterDeletes > beforeDeletes)
                            {
                                s_PurgeTexturesDeleted++;
                                s_PurgeWorkerAnyDeletes = true;
                            }
                        }
                    }
                }
                catch { }

                yield return null;

                s_TexturesProcessed++;
                if (!s_BatchMode)
                {
                    s_ProcessedWork++;
                }
                UpdateUiStatus();
            }
        }

        private static bool TryFileEntryExists(string uidPath)
        {
            try { return FileManager.GetFileEntry(uidPath) != null; }
            catch { return false; }
        }

        private static IEnumerator WriteNativeCacheForImageVariantsCoroutine(string imgUidPath, string internalPath, List<TextureFlags> variants, long entrySize, DateTime entryTime)
        {
            if (string.IsNullOrEmpty(imgUidPath) || string.IsNullOrEmpty(internalPath) || variants == null || variants.Count == 0) yield break;

            SuppressZstdMissLookupLog = true;
            try
            {

            Trace("CacheImageStart: uidPath='" + imgUidPath + "' internal='" + internalPath + "' variants=" + variants.Count);

            float frameStart = Time.realtimeSinceStartup;

            for (int v = 0; v < variants.Count; v++)
            {
                if (s_CancelRequested) yield break;
                TextureFlags flags = variants[v];

                int[] sizedWidths;
                int[] sizedHeights;
                GetSizedCacheDimensionArrays(flags, internalPath, out sizedWidths, out sizedHeights);

                for (int si = 0; si < sizedWidths.Length; si++)
                {
                    if (s_CancelRequested) yield break;
                    int targetWidth = sizedWidths[si];
                    int targetHeight = sizedHeights[si];

                    string cachePath = ResolveVaMNativeCachePath(imgUidPath, flags, targetWidth, targetHeight);
                    if (string.IsNullOrEmpty(cachePath))
                    {
                        s_CacheFails++;
                        RecordOnDemandIssue("FAIL", imgUidPath, internalPath, flags, "ResolveVaMNativeCachePath returned null (no FileEntry?)");
                        continue;
                    }

                    bool wantNative;
                    bool wantZstd;
                    ResolveOnDemandWriteTargets(internalPath, flags, out wantNative, out wantZstd);

                    if (!TryFileEntryExists(imgUidPath))
                    {
                        s_CacheFails++;
                        RecordOnDemandIssue("FAIL", imgUidPath, internalPath, flags, "FileEntry missing before loader build");
                        continue;
                    }

                    if (wantNative && !MVR.FileManagement.CacheManager.CachingEnabled)
                    {
                        RecordOnDemandIssue("SKIP", imgUidPath, internalPath, flags, "CacheManager.CachingEnabled=false (native only)");
                    }

                    string zstdPathOnDisk = null;
                    string zstdWritePath = null;
                    if (wantZstd)
                    {
                        try
                        {
                            zstdPathOnDisk = TextureUtil.FindZstdCacheFileOnDisk(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength, flags.isReadable);
                            zstdWritePath = !string.IsNullOrEmpty(zstdPathOnDisk)
                                ? zstdPathOnDisk
                                : TextureUtil.BuildExactZstdCachePath(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength, flags.isReadable);
                        }
                        catch
                        {
                            zstdPathOnDisk = null;
                            zstdWritePath = null;
                        }
                    }

                    if (wantZstd && string.IsNullOrEmpty(zstdWritePath))
                    {
                        for (int ztry = 0; ztry < 3 && string.IsNullOrEmpty(zstdWritePath); ztry++)
                        {
                            if (ztry == 1)
                            {
                                try { MVR.FileManagement.FileManager.Refresh(); } catch { }
                            }

                            if (ztry > 0)
                            {
                                yield return null;
                            }

                            try
                            {
                                zstdPathOnDisk = TextureUtil.FindZstdCacheFileOnDisk(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength, flags.isReadable);
                                zstdWritePath = !string.IsNullOrEmpty(zstdPathOnDisk)
                                    ? zstdPathOnDisk
                                    : TextureUtil.BuildExactZstdCachePath(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength, flags.isReadable);
                            }
                            catch
                            {
                                zstdPathOnDisk = null;
                                zstdWritePath = null;
                            }
                        }

                        if (string.IsNullOrEmpty(zstdWritePath))
                        {
                            s_ZstdSkips++;
                            Trace("ZstdSkipNoPath: uidPath='" + imgUidPath + "' internal='" + internalPath + "' target=" + targetWidth + "x" + targetHeight + " sig=" + GetFlagsSignature(flags));
                            if (!wantNative) continue;
                            wantZstd = false;
                        }
                    }

                    bool nativeExists = false;
                    if (wantNative)
                    {
                        if (File.Exists(cachePath) && File.Exists(cachePath + "meta"))
                        {
                            nativeExists = true;
                        }
                    }

                    bool zstdExists = !string.IsNullOrEmpty(zstdPathOnDisk);
                    if (zstdExists && SuperControllerHook.IsTiffTexturePath(internalPath)
                        && !TextureUtil.IsZstdCachePayloadSane(zstdPathOnDisk, 64 * 64))
                    {
                        TextureUtil.TryDeleteZstdCacheFile(zstdPathOnDisk);
                        zstdExists = false;
                        zstdPathOnDisk = null;
                        zstdWritePath = null;
                    }
                    if (zstdExists && flags.createAlphaFromGrayscale
                        && !TextureUtil.IsZstdCachePayloadSane(zstdPathOnDisk, 32 * 32))
                    {
                        TextureUtil.TryDeleteZstdCacheFile(zstdPathOnDisk);
                        zstdExists = false;
                        zstdPathOnDisk = null;
                        zstdWritePath = null;
                    }
                    bool needNative = wantNative && !nativeExists;
                    bool rewriteZstd = wantZstd && zstdExists && s_JobRewriteExistingZstd;
                    bool needZstd = wantZstd && (!zstdExists || rewriteZstd);

                    if (!needNative && !needZstd)
                    {
                        if (wantNative) s_CacheSkips++;
                        if (wantZstd) s_ZstdSkips++;
                        RecordOnDemandIssue("SKIP", imgUidPath, internalPath, flags,
                            FormatOnDemandSkipDetail(nativeExists, zstdExists, needNative, needZstd, flags));

                        if (s_JobWriteMode == CacheWriteMode.ZstdOnly && nativeExists && !NeedsNativeCacheAtRuntime(internalPath))
                        {
                            TryDeleteFileAndMeta(cachePath, ref s_NativeDeletes);
                            nativeExists = false;
                        }

                        if (nativeExists)
                        {
                            try
                            {
                                long nb = new FileInfo(cachePath).Length;
                                s_NativeCacheBytes += nb;
                                s_NativeCacheBytesExisting += nb;
                            }
                            catch { }
                            try
                            {
                                string nmeta = cachePath + "meta";
                                if (File.Exists(nmeta))
                                {
                                    long mb = new FileInfo(nmeta).Length;
                                    s_NativeCacheBytes += mb;
                                    s_NativeCacheBytesExisting += mb;
                                }
                            }
                            catch { }
                        }

                        if (zstdExists)
                        {
                            try { s_ZstdCompressedBytes += new FileInfo(zstdPathOnDisk).Length; } catch { }
                            try
                            {
                                long zb = new FileInfo(zstdPathOnDisk).Length;
                                s_ZstdDiskBytes += zb;
                                s_ZstdDiskBytesExisting += zb;
                            }
                            catch { }
                            try
                            {
                                string zmetaDisk = zstdPathOnDisk + "meta";
                                if (File.Exists(zmetaDisk))
                                {
                                    long mb = new FileInfo(zmetaDisk).Length;
                                    s_ZstdDiskBytes += mb;
                                    s_ZstdDiskBytesExisting += mb;
                                }
                            }
                            catch { }
                            try
                            {
                                string zmetaPath = zstdPathOnDisk + "meta";
                                if (File.Exists(zmetaPath))
                                {
                                    var mz = SimpleJSON.JSON.Parse(File.ReadAllText(zmetaPath));
                                    if (mz != null)
                                    {
                                        int mw = mz["width"].AsInt;
                                        int mh = mz["height"].AsInt;
                                        TextureFormat mfmt = TextureFormat.RGBA32;
                                        try
                                        {
                                            string fmt = mz["format"].Value;
                                            if (!string.IsNullOrEmpty(fmt))
                                            {
                                                try { mfmt = (TextureFormat)Enum.Parse(typeof(TextureFormat), fmt, true); } catch { }
                                            }
                                        }
                                        catch { }

                                        if (mw > 0 && mh > 0)
                                        {
                                            int expected = TextureUtil.GetExpectedRawDataSize(mw, mh, mfmt);
                                            if (expected > 0) s_ZstdOriginalBytes += expected;

                                            bool downscaled = false;
                                            try { downscaled = mz["downscaled"].AsBool; } catch { downscaled = false; }
                                            if (downscaled)
                                            {
                                                int sw = 0;
                                                int sh = 0;
                                                try { sw = mz["sourceWidth"].AsInt; } catch { sw = 0; }
                                                try { sh = mz["sourceHeight"].AsInt; } catch { sh = 0; }
                                                if (sw > 0 && sh > 0)
                                                {
                                                    int expectedSource = TextureUtil.GetExpectedRawDataSize(sw, sh, mfmt);
                                                    if (expectedSource > 0)
                                                    {
                                                        long saved = (long)expectedSource - (long)expected;
                                                        if (saved > 0)
                                                        {
                                                            s_ZstdDownscaleWrites++;
                                                            s_ZstdDownscaleSavedBytes += saved;
                                                            s_ZstdDownscaleOriginalBytes += expectedSource;
                                                            s_ZstdDownscaleFinalBytes += expected;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        continue;
                    }

                    if (s_CancelRequested) yield break;

                    byte[] payloadZstd = null;
                    int wz = 0;
                    int hz = 0;
                    TextureFormat tfz = TextureFormat.RGBA32;
                    bool didZstdDownscale = false;
                    int zstdSourceW = 0;
                    int zstdSourceH = 0;

                    bool allowZstdDownscale = false;
                    try
                    {
                        allowZstdDownscale = (Settings.Instance != null
                            && Settings.Instance.Downscale8kTo4kBeforeZstdCache.Value
                            && !flags.isReadable
                            && !SuperControllerHook.IsSimulationTexturePath(internalPath));
                    }
                    catch { allowZstdDownscale = false; }

                    int zstdTargetW = 0;
                    int zstdTargetH = 0;
                    int probeSourceW = 0;
                    int probeSourceH = 0;
                    bool wantsZstdDownscale = needZstd && allowZstdDownscale
                        && TryComputeZstdDownscaleDimensions(imgUidPath, targetWidth, targetHeight, out zstdTargetW, out zstdTargetH, out probeSourceW, out probeSourceH);

                    bool decodeFromSource = needNative || (needZstd && (rewriteZstd || !nativeExists));
                    bool suppressNativeDiskWrite = !needNative;

                    if (OnDemandLogLevel() >= 2)
                    {
                        OnDemandLog(
                            "Build internal='" + internalPath + "' native=" + needNative + " zstd=" + needZstd
                            + " decodeSrc=" + decodeFromSource + " noNativeWrite=" + suppressNativeDiskWrite
                            + " cachePath=" + System.IO.Path.GetFileName(cachePath ?? string.Empty));
                    }

                    CustomImageLoaderThreaded.OnDemandCacheBuildResult loaderNative = default(CustomImageLoaderThreaded.OnDemandCacheBuildResult);
                    CustomImageLoaderThreaded.OnDemandCacheBuildResult loaderZstd = default(CustomImageLoaderThreaded.OnDemandCacheBuildResult);
                    var loaderSlot = new OnDemandLoaderResultSlot();

                    if (needNative && needZstd && wantsZstdDownscale)
                    {
                        yield return CoBuildViaLoader(imgUidPath, flags, targetWidth, targetHeight, true, false, loaderSlot);
                        loaderNative = loaderSlot.Value;
                        if (!loaderNative.success)
                        {
                            s_CacheFails++;
                            RecordOnDemandIssue("FAIL", imgUidPath, internalPath, flags, "loader native (downscale path): " + (loaderNative.error ?? string.Empty));
                            continue;
                        }

                        if (zstdTargetW == targetWidth && zstdTargetH == targetHeight)
                        {
                            loaderZstd = loaderNative;
                        }
                        else
                        {
                            yield return CoBuildViaLoader(imgUidPath, flags, zstdTargetW, zstdTargetH, true, true, loaderSlot);
                            loaderZstd = loaderSlot.Value;
                            if (!loaderZstd.success)
                            {
                                RecordOnDemandIssue("WARN", imgUidPath, internalPath, flags, "loader zstd downscale failed, using native payload: " + (loaderZstd.error ?? string.Empty));
                                loaderZstd = loaderNative;
                            }
                        }
                    }
                    else
                    {
                        int buildW = (needZstd && wantsZstdDownscale && !needNative) ? zstdTargetW : targetWidth;
                        int buildH = (needZstd && wantsZstdDownscale && !needNative) ? zstdTargetH : targetHeight;
                        yield return CoBuildViaLoader(imgUidPath, flags, buildW, buildH, decodeFromSource, suppressNativeDiskWrite, loaderSlot);
                        var built = loaderSlot.Value;
                        if (!built.success)
                        {
                            s_CacheFails++;
                            RecordOnDemandIssue("FAIL", imgUidPath, internalPath, flags,
                                "loader: " + (built.error ?? string.Empty)
                                + " decodeSrc=" + decodeFromSource + " noNativeWrite=" + suppressNativeDiskWrite
                                + " w=" + buildW + " h=" + buildH);
                            continue;
                        }

                        if (needNative) loaderNative = built;
                        if (needZstd) loaderZstd = built;
                    }

                    if (needNative)
                    {
                        if (loaderNative.wroteNativeCache || (!nativeExists && !string.IsNullOrEmpty(cachePath) && File.Exists(cachePath) && File.Exists(cachePath + "meta")))
                        {
                            s_CacheWrites++;
                            try
                            {
                                long nb = new FileInfo(cachePath).Length;
                                s_NativeCacheBytes += nb;
                                s_NativeCacheBytesWritten += nb;
                            }
                            catch { }
                            try
                            {
                                string nmeta = cachePath + "meta";
                                if (File.Exists(nmeta))
                                {
                                    long mb = new FileInfo(nmeta).Length;
                                    s_NativeCacheBytes += mb;
                                    s_NativeCacheBytesWritten += mb;
                                }
                            }
                            catch { }
                            Trace("CacheWrite: path='" + cachePath + "' fmt=" + loaderNative.format + " bytes=" + (loaderNative.payload != null ? loaderNative.payload.Length : 0));
                        }
                        else if (!nativeExists)
                        {
                            s_CacheFails++;
                            RecordOnDemandIssue("FAIL", imgUidPath, internalPath, flags,
                                "native cache not created after loader ok path=" + System.IO.Path.GetFileName(cachePath)
                                + " loaderFmt=" + loaderNative.format + " " + loaderNative.width + "x" + loaderNative.height
                                + " cachingEnabled=" + MVR.FileManagement.CacheManager.CachingEnabled);
                        }
                    }
                    else if (wantNative)
                    {
                        s_CacheSkips++;
                    }

                    if (needZstd)
                    {
                        payloadZstd = loaderZstd.payload;
                        wz = loaderZstd.width;
                        hz = loaderZstd.height;
                        tfz = loaderZstd.format;
                        didZstdDownscale = loaderZstd.didDownscale || wantsZstdDownscale;
                        if (wantsZstdDownscale && loaderNative.success && loaderNative.width > 0)
                        {
                            zstdSourceW = loaderNative.width;
                            zstdSourceH = loaderNative.height;
                        }
                        else if (probeSourceW > 0)
                        {
                            zstdSourceW = probeSourceW;
                            zstdSourceH = probeSourceH;
                        }
                        else
                        {
                            zstdSourceW = loaderZstd.sourceWidth > 0 ? loaderZstd.sourceWidth : loaderNative.sourceWidth;
                            zstdSourceH = loaderZstd.sourceHeight > 0 ? loaderZstd.sourceHeight : loaderNative.sourceHeight;
                        }

                        try
                        {
                            if (payloadZstd == null || payloadZstd.Length == 0)
                            {
                                s_ZstdFails++;
                                goto AfterZstd;
                            }

                            int level = 3;
                            try { if (Settings.Instance != null) level = Settings.Instance.ZstdCompressionLevel.Value; } catch { }

                            try
                            {
                                string zdir = Path.GetDirectoryName(zstdWritePath);
                                if (!string.IsNullOrEmpty(zdir) && !Directory.Exists(zdir)) Directory.CreateDirectory(zdir);
                            }
                            catch { }

                            byte[] compressed;
                            try
                            {
                                EnsureZstdInitialized();
                                using (var compressor = new Compressor(new CompressionOptions(level)))
                                {
                                    compressed = compressor.Wrap(payloadZstd, 0, payloadZstd.Length);
                                }
                            }
                            catch
                            {
                                s_ZstdFails++;
                                goto AfterZstd;
                            }

                            AtomicWriteAllBytes(zstdWritePath, compressed);

                            JSONClass zmeta = new JSONClass();
                            zmeta["type"] = "compressed";
                            zmeta["width"] = wz.ToString();
                            zmeta["height"] = hz.ToString();
                            zmeta["format"] = tfz.ToString();
                            if (flags.isReadable) zmeta["isReadable"] = "true";
                            if (didZstdDownscale)
                            {
                                zmeta["downscaled"].AsBool = true;
                                zmeta["sourceWidth"] = zstdSourceW.ToString();
                                zmeta["sourceHeight"] = zstdSourceH.ToString();
                            }
                            zmeta["zstdLevel"].AsInt = level;
                            TextureUtil.WriteMipFieldsToMeta(zmeta, wz, hz, tfz, payloadZstd.Length, true);
                            AtomicWriteAllText(zstdWritePath + "meta", VPB.src.util.JsonSerializationUtil.Serialize(zmeta, 1024));

                            s_ZstdWrites++;
                            if (rewriteZstd) s_ZstdRewrites++;
                            s_ZstdOriginalBytes += payloadZstd.Length;
                            if (compressed != null) s_ZstdCompressedBytes += compressed.Length;
                            try
                            {
                                long zb = new FileInfo(zstdWritePath).Length;
                                s_ZstdDiskBytes += zb;
                                s_ZstdDiskBytesWritten += zb;
                            }
                            catch { }
                            try
                            {
                                string zmetaDisk = zstdWritePath + "meta";
                                if (File.Exists(zmetaDisk))
                                {
                                    long mb = new FileInfo(zmetaDisk).Length;
                                    s_ZstdDiskBytes += mb;
                                    s_ZstdDiskBytesWritten += mb;
                                }
                            }
                            catch { }
                            Trace((rewriteZstd ? "ZstdRewrite: path='" : "ZstdWrite: path='") + zstdWritePath + "' orig=" + payloadZstd.Length + " comp=" + (compressed != null ? compressed.Length : 0));

                            if (s_JobWriteMode == CacheWriteMode.ZstdOnly && !NeedsNativeCacheAtRuntime(internalPath))
                            {
                                bool deleteNative = false;
                                try
                                {
                                    deleteNative = Settings.Instance == null
                                        || Settings.Instance.DeleteOriginalCacheAfterCompression == null
                                        || Settings.Instance.DeleteOriginalCacheAfterCompression.Value;
                                }
                                catch { deleteNative = true; }

                                if (deleteNative && !string.IsNullOrEmpty(cachePath) && File.Exists(cachePath))
                                {
                                    TryDeleteFileAndMeta(cachePath, ref s_NativeDeletes);
                                }
                            }

                            if (didZstdDownscale) s_ZstdDownscaleWrites++;
                            if (didZstdDownscale)
                            {
                                try
                                {
                                    int expected = TextureUtil.GetExpectedRawDataSize(zstdSourceW, zstdSourceH, tfz);
                                    if (expected > 0)
                                    {
                                        long saved = (long)expected - (long)payloadZstd.Length;
                                        if (saved > 0) s_ZstdDownscaleSavedBytes += saved;
                                        s_ZstdDownscaleOriginalBytes += expected;
                                        s_ZstdDownscaleFinalBytes += payloadZstd.Length;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch
                        {
                            s_ZstdFails++;
                            Trace("ZstdFail: path='" + (zstdWritePath ?? string.Empty) + "'");
                        }
                    }
                    else if (wantZstd)
                    {
                        s_ZstdSkips++;
                    }

                AfterZstd:

                    if (Time.realtimeSinceStartup - frameStart >= OnDemandFrameBudgetSec)
                    {
                        frameStart = Time.realtimeSinceStartup;
                        yield return null;
                    }
                }

                if (Time.realtimeSinceStartup - frameStart >= OnDemandFrameBudgetSec)
                {
                    frameStart = Time.realtimeSinceStartup;
                    yield return null;
                }
            }
            }
            finally
            {
                SuppressZstdMissLookupLog = false;
            }
        }

        private static void WriteNativeCacheForImageVariants(string imgUidPath, string internalPath, List<TextureFlags> variants, long entrySize, DateTime entryTime)
        {
            var it = WriteNativeCacheForImageVariantsCoroutine(imgUidPath, internalPath, variants, entrySize, entryTime);
            while (it != null && it.MoveNext()) { }
        }

        private static Dictionary<string, List<TextureFlags>> BuildInternalPathToFlagsFromPackagePresets(VarPackage pkg)
        {
            Dictionary<string, List<TextureFlags>> internalPathToFlags = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
            if (pkg == null) return internalPathToFlags;

            try
            {
                var seedJson = new List<RequiredJsonFile>();
                var seedTex = new List<RequiredTexture>();

                foreach (var fe in pkg.FileEntries)
                {
                    if (fe == null) continue;
                    if (string.IsNullOrEmpty(fe.InternalPath)) continue;

                    if (!IsOnDemandSeedPath(fe.InternalPath)) continue;

                    try
                    {
                        string uidPath = pkg.Uid + ":/" + fe.InternalPath;
                        string text = FileManager.ReadAllText(uidPath);
                        if (string.IsNullOrEmpty(text)) continue;

                        JSONNode node = null;
                        try { node = JSON.Parse(text); } catch { node = null; }
                        if (node == null) continue;

                        ExtractSceneUrlsRecursive(node, pkg.Uid, fe.InternalPath, seedTex, seedJson);
                    }
                    catch { }
                }

                var visitedJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<KeyValuePair<RequiredJsonFile, int>>();
                for (int i = 0; i < seedJson.Count; i++)
                {
                    queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(seedJson[i], 1));
                }

                while (queue.Count > 0)
                {
                    var kv = queue.Dequeue();
                    RequiredJsonFile rj = kv.Key;
                    int depth = kv.Value;
                    if (depth > JsonWalkMaxDepth) continue;
                    if (string.IsNullOrEmpty(rj.PackageId) || string.IsNullOrEmpty(rj.InternalPath)) continue;

                    if (!NormalizePackageId(rj.PackageId).Equals(NormalizePackageId(pkg.Uid), StringComparison.OrdinalIgnoreCase)) continue;

                    string jsonUidPath = pkg.Uid + ":/" + rj.InternalPath;
                    if (!visitedJson.Add(jsonUidPath)) continue;

                    string txt = null;
                    try { txt = FileManager.ReadAllText(jsonUidPath); } catch { txt = null; }
                    if (string.IsNullOrEmpty(txt)) continue;

                    JSONNode n = null;
                    try { n = JSON.Parse(txt); } catch { n = null; }
                    if (n == null) continue;

                    var nestedTex = new List<RequiredTexture>();
                    var nestedJson = new List<RequiredJsonFile>();
                    ExtractSceneUrlsRecursive(n, pkg.Uid, rj.InternalPath, nestedTex, nestedJson);

                    if (nestedTex != null && nestedTex.Count > 0) seedTex.AddRange(nestedTex);
                    if (nestedJson != null && nestedJson.Count > 0)
                    {
                        for (int i = 0; i < nestedJson.Count; i++)
                        {
                            queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(nestedJson[i], depth + 1));
                        }
                    }
                }

                for (int i = 0; i < seedTex.Count; i++)
                {
                    var rt = seedTex[i];
                    if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;
                    if (!NormalizePackageId(rt.PackageId).Equals(NormalizePackageId(pkg.Uid), StringComparison.OrdinalIgnoreCase)) continue;

                    string k = rt.InternalPath.ToLowerInvariant();
                    if (string.IsNullOrEmpty(k)) continue;

                    AddFlagVariant(internalPathToFlags, k, rt.Flags);
                }
            }
            catch { }

            return internalPathToFlags;
        }

        private static List<RequiredTexture> BuildRequiredTexturesFromPackagePresetsFollowDeps(VarPackage pkg)
        {
            var required = new List<RequiredTexture>();
            if (pkg == null) return required;

            try
            {
                var seedJson = new List<RequiredJsonFile>();
                var seedTex = new List<RequiredTexture>();

                foreach (var fe in pkg.FileEntries)
                {
                    if (fe == null) continue;
                    if (string.IsNullOrEmpty(fe.InternalPath)) continue;

                    if (!IsOnDemandSeedPath(fe.InternalPath)) continue;

                    try
                    {
                        string uidPath = pkg.Uid + ":/" + fe.InternalPath;
                        string text = FileManager.ReadAllText(uidPath);
                        if (string.IsNullOrEmpty(text)) continue;

                        JSONNode node = null;
                        try { node = JSON.Parse(text); } catch { node = null; }
                        if (node == null) continue;

                        ExtractSceneUrlsRecursive(node, pkg.Uid, fe.InternalPath, seedTex, seedJson);
                    }
                    catch { }
                }

                var visitedJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<KeyValuePair<RequiredJsonFile, int>>();
                for (int i = 0; i < seedJson.Count; i++)
                {
                    queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(seedJson[i], 1));
                }

                while (queue.Count > 0)
                {
                    var kv = queue.Dequeue();
                    RequiredJsonFile rj = kv.Key;
                    int depth = kv.Value;
                    if (depth > JsonWalkMaxDepth) continue;
                    if (string.IsNullOrEmpty(rj.PackageId) || string.IsNullOrEmpty(rj.InternalPath)) continue;

                    VarPackage jp = ResolvePackageWithFallback(rj.PackageId);
                    if (jp == null) continue;

                    string jsonUidPath = jp.Uid + ":/" + rj.InternalPath;
                    string visitKey = jsonUidPath.ToLowerInvariant();
                    if (!visitedJson.Add(visitKey)) continue;

                    string txt = null;
                    try { txt = FileManager.ReadAllText(jsonUidPath); } catch { txt = null; }
                    if (string.IsNullOrEmpty(txt)) continue;

                    JSONNode n = null;
                    try { n = JSON.Parse(txt); } catch { n = null; }
                    if (n == null) continue;

                    var nestedTex = new List<RequiredTexture>();
                    var nestedJson = new List<RequiredJsonFile>();
                    ExtractSceneUrlsRecursive(n, jp.Uid, rj.InternalPath, nestedTex, nestedJson);

                    if (nestedTex != null && nestedTex.Count > 0) seedTex.AddRange(nestedTex);
                    if (nestedJson != null && nestedJson.Count > 0)
                    {
                        for (int i = 0; i < nestedJson.Count; i++)
                        {
                            queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(nestedJson[i], depth + 1));
                        }
                    }
                }

                var seenTex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < seedTex.Count; i++)
                {
                    var rt = seedTex[i];
                    if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;
                    string dedupeKey = rt.PackageId + "|" + rt.InternalPath + "|" + GetFlagsSignature(rt.Flags);
                    if (!seenTex.Add(dedupeKey)) continue;
                    required.Add(rt);
                }
            }
            catch { }

            return required;
        }

        private static IEnumerator BuildCacheForSelectedPackageUnity(string packagePath)
        {
            if (string.IsNullOrEmpty(packagePath)) yield break;

            VarPackage pkg = null;
            try { pkg = FileManager.GetPackage(packagePath, false); }
            catch { pkg = null; }

            if (pkg == null) yield break;

            try { pkg.Scan(); }
            catch { }

            var required = BuildRequiredTexturesFromPackagePresetsFollowDeps(pkg);
            if (required == null || required.Count == 0)
            {
                OnDemandLog("Package cache: no texture refs in presets for " + packagePath, always: true);
                yield break;
            }

            OnDemandLog("Package cache: " + required.Count + " texture ref(s) from presets for " + packagePath);

            var byPkgFlags = new Dictionary<string, Dictionary<string, List<TextureFlags>>>(StringComparer.OrdinalIgnoreCase);
            var byPkgOrig = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < required.Count; i++)
            {
                RequiredTexture rt = required[i];
                if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;

                VarPackage tp = ResolvePackageWithFallback(rt.PackageId);
                if (tp == null) continue;

                string pkgUid = tp.Uid;
                string internalLower = rt.InternalPath.ToLowerInvariant();

                Dictionary<string, List<TextureFlags>> flagsMap;
                if (!byPkgFlags.TryGetValue(pkgUid, out flagsMap))
                {
                    flagsMap = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
                    byPkgFlags[pkgUid] = flagsMap;
                }

                Dictionary<string, string> origMap;
                if (!byPkgOrig.TryGetValue(pkgUid, out origMap))
                {
                    origMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byPkgOrig[pkgUid] = origMap;
                }

                AddFlagVariant(flagsMap, internalLower, rt.Flags);
                if (!origMap.ContainsKey(internalLower))
                {
                    origMap[internalLower] = rt.InternalPath;
                }
            }

            ExpandCachedPackagesToNewestInstalled(byPkgFlags, byPkgOrig);

            int totalWork = 0;
            foreach (var kv in byPkgOrig)
            {
                if (kv.Value != null) totalWork += kv.Value.Count;
            }
            if (totalWork <= 0) yield break;

            s_CurrentPackage = pkg.Uid;
            if (s_BatchMode)
            {
                s_TexturesPlanned += totalWork;
                UpdateUiStatus();
            }
            else
            {
                s_PackagesPlanned = byPkgOrig.Count;
                s_PackagesProcessed = 0;
                s_TexturesPlanned = totalWork;
                s_TexturesProcessed = 0;
                s_TotalWork = Math.Max(1, totalWork);
                s_ProcessedWork = 0;
                UpdateUiStatus();
            }

            int pkgIndex = 0;
            foreach (var kv in byPkgFlags)
            {
                string pkgUid = kv.Key;
                Dictionary<string, List<TextureFlags>> flagsMap = kv.Value;
                Dictionary<string, string> origMap;
                byPkgOrig.TryGetValue(pkgUid, out origMap);

                VarPackage tp = ResolvePackageWithFallback(pkgUid);
                if (tp == null)
                {
                    int missing = (origMap != null) ? origMap.Count : 0;
                    if (missing > 0)
                    {
                        if (s_BatchMode)
                        {
                            s_TexturesPlanned = Math.Max(0, s_TexturesPlanned - missing);
                        }
                        else
                        {
                            s_TexturesPlanned = Math.Max(0, s_TexturesPlanned - missing);
                            s_TotalWork = Math.Max(1, s_TexturesPlanned);
                            s_PackagesPlanned = Math.Max(0, s_PackagesPlanned - 1);
                        }
                    }
                    UpdateUiStatus();
                    continue;
                }

                pkgIndex++;

                s_CurrentPackage = tp.Uid;
                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex - 1;
                    UpdateUiStatus();
                }

                yield return WorkerBuildSelectiveUnityCoroutine(tp, flagsMap, origMap);

                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex;
                    UpdateUiStatusThrottled(force: true);
                }
            }
        }

        private static IEnumerator PurgeCacheForSelectedPackageUnity(string packagePath)
        {
            if (string.IsNullOrEmpty(packagePath)) yield break;

            VarPackage pkg = null;
            try { pkg = FileManager.GetPackage(packagePath, false); }
            catch { pkg = null; }

            if (pkg == null) yield break;

            try { pkg.Scan(); }
            catch { }

            var required = BuildRequiredTexturesFromPackagePresetsFollowDeps(pkg);
            if (required == null || required.Count == 0) yield break;

            var byPkgFlags = new Dictionary<string, Dictionary<string, List<TextureFlags>>>(StringComparer.OrdinalIgnoreCase);
            var byPkgOrig = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < required.Count; i++)
            {
                RequiredTexture rt = required[i];
                if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;

                VarPackage tp = ResolvePackageWithFallback(rt.PackageId);
                if (tp == null) continue;

                string pkgUid = tp.Uid;
                string internalLower = rt.InternalPath.ToLowerInvariant();

                Dictionary<string, List<TextureFlags>> flagsMap;
                if (!byPkgFlags.TryGetValue(pkgUid, out flagsMap))
                {
                    flagsMap = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
                    byPkgFlags[pkgUid] = flagsMap;
                }

                Dictionary<string, string> origMap;
                if (!byPkgOrig.TryGetValue(pkgUid, out origMap))
                {
                    origMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byPkgOrig[pkgUid] = origMap;
                }

                AddFlagVariant(flagsMap, internalLower, rt.Flags);
                if (!origMap.ContainsKey(internalLower))
                {
                    origMap[internalLower] = rt.InternalPath;
                }
            }

            int totalWork = 0;
            foreach (var kv in byPkgOrig)
            {
                if (kv.Value != null) totalWork += kv.Value.Count;
            }
            if (totalWork <= 0) yield break;

            s_CurrentPackage = pkg.Uid;
            if (s_BatchMode)
            {
                s_TexturesPlanned += totalWork;
                UpdateUiStatus();
            }
            else
            {
                s_PackagesPlanned = byPkgOrig.Count;
                s_PackagesProcessed = 0;
                s_TexturesPlanned = totalWork;
                s_TexturesProcessed = 0;
                s_TotalWork = Math.Max(1, totalWork);
                s_ProcessedWork = 0;
                UpdateUiStatus();
            }

            int pkgIndex = 0;
            foreach (var kv in byPkgFlags)
            {
                string pkgUid = kv.Key;
                Dictionary<string, List<TextureFlags>> flagsMap = kv.Value;
                Dictionary<string, string> origMap;
                byPkgOrig.TryGetValue(pkgUid, out origMap);

                VarPackage tp = ResolvePackageWithFallback(pkgUid);
                if (tp == null)
                {
                    int missing = (origMap != null) ? origMap.Count : 0;
                    if (missing > 0)
                    {
                        if (s_BatchMode)
                        {
                            s_TexturesPlanned = Math.Max(0, s_TexturesPlanned - missing);
                        }
                        else
                        {
                            s_TexturesPlanned = Math.Max(0, s_TexturesPlanned - missing);
                            s_TotalWork = Math.Max(1, s_TexturesPlanned);
                            s_PackagesPlanned = Math.Max(0, s_PackagesPlanned - 1);
                        }
                    }
                    UpdateUiStatus();
                    continue;
                }

                pkgIndex++;

                s_CurrentPackage = tp.Uid;
                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex - 1;
                    UpdateUiStatus();
                }

                s_PackagesResolved++;
                s_PurgeWorkerAnyDeletes = false;
                yield return WorkerPurgeSelectiveUnityCoroutine(tp, flagsMap, origMap);
                if (s_PurgeWorkerAnyDeletes) s_PurgePackagesDeleted++;

                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex;
                    UpdateUiStatus();
                }
            }
        }

        private static IEnumerator PurgeCacheForPackagePresetsFollowDepsUnity(VarPackage rootPkg)
        {
            if (rootPkg == null) yield break;

            try { rootPkg.Scan(); } catch { }

            var required = BuildRequiredTexturesFromPackagePresetsFollowDeps(rootPkg);
            if (required == null || required.Count == 0) yield break;

            var byPkgFlags = new Dictionary<string, Dictionary<string, List<TextureFlags>>>(StringComparer.OrdinalIgnoreCase);
            var byPkgOrig = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < required.Count; i++)
            {
                RequiredTexture rt = required[i];
                if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;

                VarPackage tp = ResolvePackageWithFallback(rt.PackageId);
                if (tp == null) continue;

                string pkgUid = tp.Uid;
                string internalLower = rt.InternalPath.ToLowerInvariant();

                if (!byPkgFlags.TryGetValue(pkgUid, out var flagsMap))
                {
                    flagsMap = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
                    byPkgFlags[pkgUid] = flagsMap;
                }

                if (!byPkgOrig.TryGetValue(pkgUid, out var origMap))
                {
                    origMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byPkgOrig[pkgUid] = origMap;
                }

                AddFlagVariant(flagsMap, internalLower, rt.Flags);
                if (!origMap.ContainsKey(internalLower)) origMap[internalLower] = rt.InternalPath;
            }

            foreach (var kv in byPkgFlags)
            {
                if (s_CancelRequested) yield break;
                string pkgUid = kv.Key;
                if (!byPkgOrig.TryGetValue(pkgUid, out var origMap)) origMap = null;

                VarPackage tp = ResolvePackageWithFallback(pkgUid);
                if (tp == null) continue;

                s_PackagesResolved++;
                s_CurrentPackage = tp.Uid;
                UpdateUiStatus();

                yield return WorkerPurgeSelectiveUnityCoroutine(tp, kv.Value, origMap);
            }
        }

        private static void ThrottledLog(string msg)
        {
            float now = Time.unscaledTime;
            if (now - s_OnDemandLastLogTime < OnDemandLogThrottleSeconds) return;
            s_OnDemandLastLogTime = now;
            LogUtil.Log(msg);
        }
    }

    internal static class OnDemandTextureCacheHook
    {
        private static bool s_HotkeyNativeOnlyDown;
        private static bool s_HotkeyRewriteExistingZstdDown;
        private static bool s_MultiRunning;

        public static void Update()
        {
            try { NativeTextureCacheBuildOverlay.EnsureCreated(); } catch { }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                s_HotkeyRewriteExistingZstdDown = IsCtrlHeld();
                s_HotkeyNativeOnlyDown = true;
            }

            if (!s_HotkeyNativeOnlyDown) return;

            bool requestRewriteExistingZstd = s_HotkeyRewriteExistingZstdDown;
            bool requestNativeOnly = s_HotkeyNativeOnlyDown && !requestRewriteExistingZstd;
            s_HotkeyNativeOnlyDown = false;
            s_HotkeyRewriteExistingZstdDown = false;

            // If the previous job summary is still visible, close it before starting a new job.
            // Otherwise the UI can remain in summary-mode and block the new run.
            try { NativeTextureOnDemandCache.DismissSummary(); } catch { }

            var sc = SuperController.singleton;
            if (sc == null) return;

            if (s_MultiRunning)
            {
                return;
            }

            if (requestNativeOnly)
            {
                NativeTextureOnDemandCache.SetNextJobWriteModeOverride(NativeTextureOnDemandCache.CacheWriteMode.NativeOnly);
            }
            else if (requestRewriteExistingZstd)
            {
                NativeTextureOnDemandCache.SetNextJobWriteModeOverride(NativeTextureOnDemandCache.CacheWriteMode.ZstdOnly);
                NativeTextureOnDemandCache.SetNextJobRewriteExistingZstd(true);
            }

            var selectedScenePaths = new List<string>();
            var selectedPackagePaths = new List<string>();
            CollectSelectedGalleryTargets(selectedScenePaths, selectedPackagePaths);

            int selectedCount = 0;
            try { if (selectedScenePaths != null) selectedCount += selectedScenePaths.Count; } catch { }
            try { if (selectedPackagePaths != null) selectedCount += selectedPackagePaths.Count; } catch { }

            // Only enter batch mode when there are multiple targets.
            if (selectedCount > 1)
            {
                sc.StartCoroutine(RunMultiSelection(sc, selectedScenePaths, selectedPackagePaths));
                return;
            }

            // Single selection: run single-mode so the job reports correct per-item totals.
            if (selectedScenePaths != null && selectedScenePaths.Count == 1)
            {
                NativeTextureOnDemandCache.TryBuildSceneCacheOnDemand(sc, selectedScenePaths[0]);
                return;
            }
            if (selectedPackagePaths != null && selectedPackagePaths.Count == 1)
            {
                NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(sc, selectedPackagePaths[0]);
                return;
            }

            // Fallback: no selection, try current scene
            NativeTextureOnDemandCache.TryBuildSceneCacheOnDemand(sc);
        }

        private static bool IsCtrlHeld()
        {
            try
            {
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerator RunMultiSelection(MonoBehaviour host, List<string> scenePaths, List<string> packagePaths)
        {
            s_MultiRunning = true;

            int totalItems = 0;
            try { if (scenePaths != null) totalItems += scenePaths.Count; } catch { }
            try { if (packagePaths != null) totalItems += packagePaths.Count; } catch { }

            NativeTextureOnDemandCache.BeginBatchJob("Caching Textures...", totalItems);
            try
            {
                // Scenes first
                if (scenePaths != null)
                {
                    for (int i = 0; i < scenePaths.Count; i++)
                    {
                        if (NativeTextureOnDemandCache.CancelRequested) break;
                        string p = scenePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.BatchItemStart(p);
                        NativeTextureOnDemandCache.TryBuildSceneCacheOnDemand(host, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy) yield return null;
                        NativeTextureOnDemandCache.BatchItemDone();
                        yield return null;
                    }
                }

                // Packages second
                if (packagePaths != null)
                {
                    for (int i = 0; i < packagePaths.Count; i++)
                    {
                        if (NativeTextureOnDemandCache.CancelRequested) break;
                        string p = packagePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.BatchItemStart(p);
                        NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(host, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy) yield return null;
                        NativeTextureOnDemandCache.BatchItemDone();
                        yield return null;
                    }
                }
            }
            finally
            {
                NativeTextureOnDemandCache.EndBatchJob(NativeTextureOnDemandCache.CancelRequested ? "Texture caching cancelled" : "Texture caching complete");
                s_MultiRunning = false;
            }
        }

        private static void CollectSelectedGalleryTargets(List<string> scenePathsOut, List<string> packagePathsOut)
        {
            if (scenePathsOut == null || packagePathsOut == null) return;

            var sceneDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pkgDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var gallery = Gallery.singleton;
            if (gallery == null || gallery.Panels == null) return;

            for (int i = 0; i < gallery.Panels.Count; i++)
            {
                var panel = gallery.Panels[i];
                if (panel == null || panel.selectedFiles == null || panel.selectedFiles.Count == 0) continue;

                for (int j = 0; j < panel.selectedFiles.Count; j++)
                {
                    FileEntry fe = panel.selectedFiles[j];
                    if (fe == null) continue;

                    string selectedPath = fe.Path ?? string.Empty;
                    string packagePath = null;
                    bool isVarSelection = false;
                    if (fe is VarFileEntry vfe && vfe.Package != null)
                    {
                        isVarSelection = true;
                        packagePath = vfe.Package.Path;
                    }
                    else
                    {
                        string p = selectedPath;
                        int idx = !string.IsNullOrEmpty(p) ? p.IndexOf(":/", StringComparison.Ordinal) : -1;
                        packagePath = idx > 0 ? p.Substring(0, idx) : p;
                    }

                    if (!isVarSelection && !string.IsNullOrEmpty(selectedPath))
                    {
                        string lower = selectedPath.ToLowerInvariant();
                        if (lower.EndsWith(".json"))
                        {
                            if (sceneDedup.Add(selectedPath)) scenePathsOut.Add(selectedPath);
                        }
                    }

                    if (!string.IsNullOrEmpty(packagePath) && packagePath.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pkgDedup.Add(packagePath)) packagePathsOut.Add(packagePath);
                    }
                }
            }
        }
    }
}
