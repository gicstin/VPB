using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private const float OnDemandLogThrottleSeconds = 0.5f;

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
        private static string s_CurrentPackage;

        private static int s_TexturesPlanned;
        private static int s_TexturesProcessed;
        private static int s_CacheWrites;
        private static int s_CacheSkips;
        private static int s_CacheFails;

        private static long s_NativeCacheBytes;
        private static long s_NativeCacheBytesWritten;
        private static long s_NativeCacheBytesExisting;

        private static int s_ZstdWrites;
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

        private static bool s_ZstdInitialized;

        private static CacheWriteMode? s_NextJobWriteModeOverride;
        private static CacheWriteMode s_JobWriteMode;

        private static StringBuilder s_DebugTrace;

        private static bool s_CompletionSoundPlayed;
        private static float s_LastCompletionSoundTime;
        private static GameObject s_CompletionAudioGO;
        private static AudioSource s_CompletionAudioSource;
        private static AudioClip s_CompletionClip;

        private static bool s_BatchMode;

        public static bool IsOnDemandBusy => s_OnDemandBusy;

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
            s_CurrentPackage = null;
            s_TexturesPlanned = 0;
            s_TexturesProcessed = 0;
            s_CacheWrites = 0;
            s_CacheSkips = 0;
            s_CacheFails = 0;
            s_NativeCacheBytes = 0;
            s_NativeCacheBytesWritten = 0;
            s_NativeCacheBytesExisting = 0;
            s_ZstdWrites = 0;
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

            s_CompletionSoundPlayed = false;
            s_UiSummary = null;
            s_UiShowSummary = false;
            s_UiVisible = true;
            s_UiTitle = title;
            s_UiSubtitle = string.Empty;

            try
            {
                if (s_DebugTrace == null) s_DebugTrace = new StringBuilder(64 * 1024);
                s_DebugTrace.Length = 0;
                s_DebugTrace.AppendLine("VPB OnDemand Cache Trace");
                s_DebugTrace.AppendLine("StartedUtc: " + DateTime.UtcNow.ToString("o"));
                s_DebugTrace.AppendLine("Title: " + (title ?? string.Empty));
                s_DebugTrace.AppendLine("WriteMode: " + s_JobWriteMode);
                s_DebugTrace.AppendLine();
            }
            catch { }
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

            if (s_ZstdDownscaleWrites > 0)
            {
                s_UiSummary += "\nResize 8k→4k: " + s_ZstdDownscaleWrites + "    Saved: " + resizeSaved + " (" + resizePct + "%)";
            }
            s_OnDemandBusy = false;
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

        private static void EnsureZstdInitialized()
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
                yield return null;

                s_TexturesProcessed++;
                if (!s_BatchMode)
                {
                    s_ProcessedWork++;
                }
                UpdateUiStatus();
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

            int maxJsonDepth = 2;
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
                if (depth > maxJsonDepth) continue;
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
                    UpdateUiStatus();
                }
            }

            if (localOrig != null && localOrig.Count > 0)
            {
                yield return WorkerBuildSelectiveLocalCoroutine(localFlags, localOrig);
            }
        }

        private struct TextureFlags
        {
            public bool compress;
            public bool linear;
            public bool isNormalMap;
            public bool createAlphaFromGrayscale;
            public bool createNormalFromBump;
            public bool invert;
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
            return v.EndsWith(".png") || v.EndsWith(".jpg") || v.EndsWith(".jpeg");
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
            if (string.IsNullOrEmpty(selfPackageUid)) return false;

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

        private static void ApplyPathHeuristics(string internalPath, ref TextureFlags flags)
        {
            if (string.IsNullOrEmpty(internalPath)) return;
            string file = Path.GetFileName(internalPath) ?? internalPath;
            string l = file.ToLowerInvariant();

            bool isLut = l.Contains("lut");
            bool isLensDirt = l.Contains("lensdirt") || (internalPath.IndexOf("lens dirt", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!flags.isNormalMap)
            {
                if (l.Contains("normal") || l.Contains("norm") || l.Contains("normalmap") || l.Contains("nrm") || l.Contains("_nm") || l.Contains("-nm") || l.Contains(" nm")
                    || l.EndsWith("_n.png") || l.EndsWith("_n.jpg") || l.EndsWith("_n.jpeg")
                    || l.EndsWith("-n.png") || l.EndsWith("-n.jpg") || l.EndsWith("-n.jpeg")
                    || l.EndsWith(" n.png") || l.EndsWith(" n.jpg") || l.EndsWith(" n.jpeg"))
                {
                    flags.isNormalMap = true;
                    flags.linear = true;
                    flags.compress = false;
                }
            }

            if (isLut)
            {
                flags.linear = true;
                flags.compress = false;
            }

            if (isLensDirt)
            {
                flags.linear = true;
                flags.compress = false;
            }

            if (!flags.linear)
            {
                bool looksLikeSpecSuffix = l.EndsWith("_s.jpg") || l.EndsWith("-s.jpg") || l.EndsWith(" s.jpg")
                    || l.EndsWith("_s.jpeg") || l.EndsWith("-s.jpeg") || l.EndsWith(" s.jpeg");
                bool looksLikeGlossSuffix = l.EndsWith("_g.jpg") || l.EndsWith("-g.jpg") || l.EndsWith(" g.jpg")
                    || l.EndsWith("_g.jpeg") || l.EndsWith("-g.jpeg") || l.EndsWith(" g.jpeg");

                if (flags.isNormalMap || l.Contains("spec") || l.Contains("gloss") || l.Contains("rough") || l.Contains("metal") || isLut || looksLikeSpecSuffix || looksLikeGlossSuffix)
                {
                    flags.linear = true;
                }
            }
        }

        private static bool TryGetFlagsFromVapKey(string key, out TextureFlags flags)
        {
            flags = new TextureFlags
            {
                compress = true,
                linear = false,
                isNormalMap = false,
                createAlphaFromGrayscale = false,
                createNormalFromBump = false,
                invert = false,
                bumpStrength = 1f
            };

            if (string.IsNullOrEmpty(key)) return false;

            string k = key;

            // Common VaM "textures" atom keys are consistent enough to map directly.
            // This helps avoid filename-based false positives (e.g. *gen*.jpg).
            if (k.EndsWith("DiffuseUrl", StringComparison.OrdinalIgnoreCase))
            {
                flags.compress = true;
                flags.linear = false;
                return true;
            }
            if (k.EndsWith("SpecularUrl", StringComparison.OrdinalIgnoreCase))
            {
                flags.compress = true;
                flags.linear = true;
                return true;
            }
            if (k.EndsWith("GlossUrl", StringComparison.OrdinalIgnoreCase))
            {
                flags.compress = true;
                flags.linear = true;
                return true;
            }
            if (k.EndsWith("NormalUrl", StringComparison.OrdinalIgnoreCase))
            {
                flags.compress = false;
                flags.linear = true;
                flags.isNormalMap = true;
                return true;
            }

            if (k.IndexOf("customTexture_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool isMain = k.IndexOf("MainTex", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSpec = k.IndexOf("SpecTex", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isVajGloss = k.IndexOf("GlossTex", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isBump = k.IndexOf("BumpMap", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isAlpha = k.IndexOf("AlphaTex", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isBump)
                {
                    flags.isNormalMap = true;
                    flags.linear = true;
                    flags.compress = false;
                    return true;
                }

                if (isSpec || isVajGloss)
                {
                    flags.linear = true;
                    return true;
                }

                if (isAlpha)
                {
                    flags.createAlphaFromGrayscale = true;
                    return true;
                }

                if (isMain) return true;
            }

            bool isNormal = k.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSpecular = k.IndexOf("Specular", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isGloss = k.IndexOf("Gloss", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBumpUrl = k.IndexOf("BumpUrl", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBumpLike = isBumpUrl || k.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isDetailMap = k.IndexOf("Detail Map", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("DetailMap", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isRough = k.IndexOf("Rough", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isMetal = k.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isAO = k.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0 || k.IndexOf("AoUrl", StringComparison.OrdinalIgnoreCase) >= 0;

            // VaM frequently uses BumpUrl for what ultimately behaves like a normal map input.
            // For cache parity with VaM, treat BumpUrl as normal-map-like (linear, not compressed).
            bool treatAsNormal = isNormal || isBumpLike || isDetailMap;
            flags.isNormalMap = treatAsNormal;

            if (flags.isNormalMap)
            {
                flags.compress = false;
            }

            // If we're treating it as a normal-map-like input, we don't want the bump->normal transform variant.
            if (isBumpUrl && !treatAsNormal)
            {
                flags.createNormalFromBump = true;
            }

            if (treatAsNormal || isSpecular || isGloss || isBumpUrl || isRough || isMetal || isAO)
            {
                flags.linear = true;
            }

            if (k.IndexOf("DiffuseUrl", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("DecalUrl", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("DetailUrl", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (isNormal || isSpecular || isGloss || isBumpUrl || isDetailMap || isRough || isMetal || isAO) return true;

            return false;
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
                foreach (KeyValuePair<string, JSONNode> kv in obj)
                {
                    string key = kv.Key;
                    JSONNode valueNode = kv.Value;

                    bool isStringLike = valueNode != null && valueNode.AsObject == null && valueNode.AsArray == null && !string.IsNullOrEmpty(valueNode.Value);
                    if (isStringLike && (!string.IsNullOrEmpty(key) && key.IndexOf("Url", StringComparison.OrdinalIgnoreCase) >= 0 || LooksLikeVamFileRef(valueNode.Value) || LooksLikeImagePath(valueNode.Value)))
                    {
                        string rawUrl = valueNode.Value;
                        string pkgId;
                        string internalPath;
                        if (TryResolveTextureRef(rawUrl, selfPackageUid, referencingInternalPath, out pkgId, out internalPath))
                        {
                            string il = (internalPath ?? string.Empty).ToLowerInvariant();
                            if ((il.EndsWith(".png") || il.EndsWith(".jpg") || il.EndsWith(".jpeg")) && outTextures != null)
                            {
                                TextureFlags flags;
                                if (!TryGetFlagsFromVapKey(key, out flags))
                                {
                                    flags = new TextureFlags { compress = true, linear = false, isNormalMap = false, createAlphaFromGrayscale = false, createNormalFromBump = false, invert = false, bumpStrength = 1f };
                                }
                                ApplyPathHeuristics(internalPath, ref flags);

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

            string sig = string.Empty;
            if (targetWidth > 0 && targetHeight > 0) sig += targetWidth + "_" + targetHeight;
            if (flags.compress) sig += "_C";
            if (flags.linear) sig += "_L";
            if (flags.isNormalMap) sig += "_N";
            if (flags.createAlphaFromGrayscale) sig += "_A";
            if (flags.createNormalFromBump) sig = sig + "_BN" + flags.bumpStrength;
            if (flags.invert) sig += "_I";

            return textureCacheDir + "/" + fileName + "_" + sizeStr + "_" + timeStr + "_" + sig + ".vamcache";
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

            Trace("CacheImageStart: uidPath='" + imgUidPath + "' internal='" + internalPath + "' variants=" + variants.Count);

            float frameStart = Time.realtimeSinceStartup;

            for (int v = 0; v < variants.Count; v++)
            {
                if (s_CancelRequested) yield break;
                TextureFlags flags = variants[v];
                ApplyPathHeuristics(internalPath, ref flags);
                variants[v] = flags;

                bool buildSized = false;
                int[] sizedWidths = buildSized ? new[] { 0, DefaultSizedCacheWidth } : new[] { 0 };
                int[] sizedHeights = buildSized ? new[] { 0, DefaultSizedCacheHeight } : new[] { 0 };

                for (int si = 0; si < sizedWidths.Length; si++)
                {
                    if (s_CancelRequested) yield break;
                    int targetWidth = sizedWidths[si];
                    int targetHeight = sizedHeights[si];

                    string cachePath = GetNativeCachePathDynamic(imgUidPath, flags, entrySize, entryTime, targetWidth, targetHeight);
                    if (string.IsNullOrEmpty(cachePath)) continue;

                    bool wantNative = (s_JobWriteMode == CacheWriteMode.NativeOnly || s_JobWriteMode == CacheWriteMode.NativeAndZstd);
                    bool wantZstd = (s_JobWriteMode == CacheWriteMode.ZstdOnly || s_JobWriteMode == CacheWriteMode.NativeAndZstd);
                    string zstdPath = null;
                    if (wantZstd)
                    {
                        try
                        {
                            zstdPath = TextureUtil.GetZstdCachePath(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength);
                        }
                        catch { zstdPath = null; }
                    }

                    if (wantZstd && string.IsNullOrEmpty(zstdPath))
                    {
                        for (int ztry = 0; ztry < 3 && string.IsNullOrEmpty(zstdPath); ztry++)
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
                                zstdPath = TextureUtil.GetZstdCachePath(imgUidPath, flags.compress, flags.linear, flags.isNormalMap, flags.createAlphaFromGrayscale, flags.createNormalFromBump, flags.invert, targetWidth, targetHeight, flags.bumpStrength);
                            }
                            catch { zstdPath = null; }
                        }

                        if (string.IsNullOrEmpty(zstdPath))
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

                    bool zstdExists = (!string.IsNullOrEmpty(zstdPath) && File.Exists(zstdPath));
                    bool needNative = wantNative && !nativeExists;
                    bool needZstd = wantZstd && !zstdExists;

                    if (!needNative && !needZstd)
                    {
                        if (wantNative) s_CacheSkips++;
                        if (wantZstd) s_ZstdSkips++;
                        Trace("CacheSkip: internal='" + internalPath + "' target=" + targetWidth + "x" + targetHeight + " sig=" + GetFlagsSignature(flags));

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
                            try { s_ZstdCompressedBytes += new FileInfo(zstdPath).Length; } catch { }
                            try
                            {
                                long zb = new FileInfo(zstdPath).Length;
                                s_ZstdDiskBytes += zb;
                                s_ZstdDiskBytesExisting += zb;
                            }
                            catch { }
                            try
                            {
                                string zmetaDisk = zstdPath + "meta";
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
                                string zmetaPath = zstdPath + "meta";
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

                    byte[] payload;
                    int w;
                    int h;
                    TextureFormat tf;
                    bool hasAlpha;
                    string err;
                    byte[] payloadZstd = null;
                    int wz = 0;
                    int hz = 0;
                    TextureFormat tfz = TextureFormat.RGBA32;
                    bool hasAlphaz = false;
                    string errz = null;
                    bool didZstdDownscale = false;
                    int zstdSourceW = 0;
                    int zstdSourceH = 0;

                    bool allowZstdDownscale = false;
                    try
                    {
                        allowZstdDownscale = (Settings.Instance != null
                            && Settings.Instance.Downscale8kTo4kBeforeZstdCache.Value);
                    }
                    catch { allowZstdDownscale = false; }

                    if (needNative && needZstd && allowZstdDownscale)
                    {
                        bool okNative = TryBuildCachePayloadUnity(imgUidPath, internalPath, flags, targetWidth, targetHeight, false, out payload, out w, out h, out tf, out hasAlpha, out err, out _, out _, out _);
                        if (!okNative || payload == null)
                        {
                            s_CacheFails++;
                            Trace("CacheFail: internal='" + internalPath + "' err='" + (err ?? string.Empty) + "'");
                            continue;
                        }

                        bool okZ = TryBuildCachePayloadUnity(imgUidPath, internalPath, flags, targetWidth, targetHeight, true, out payloadZstd, out wz, out hz, out tfz, out hasAlphaz, out errz, out didZstdDownscale, out zstdSourceW, out zstdSourceH);
                        if (!okZ || payloadZstd == null)
                        {
                            payloadZstd = null;
                        }
                    }
                    else
                    {
                        bool ok = TryBuildCachePayloadUnity(
                            imgUidPath,
                            internalPath,
                            flags,
                            targetWidth,
                            targetHeight,
                            (needZstd && !needNative && allowZstdDownscale),
                            out payload,
                            out w,
                            out h,
                            out tf,
                            out hasAlpha,
                            out err,
                            out didZstdDownscale,
                            out zstdSourceW,
                            out zstdSourceH);

                        if (!ok || payload == null)
                        {
                            s_CacheFails++;
                            Trace("CacheFail: internal='" + internalPath + "' err='" + (err ?? string.Empty) + "'");
                            continue;
                        }

                        payloadZstd = payload;
                        wz = w;
                        hz = h;
                        tfz = tf;
                        hasAlphaz = hasAlpha;
                        errz = err;
                    }

                    if (needNative)
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(cachePath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        }
                        catch { }

                        try
                        {
                            JSONClass meta = new JSONClass();
                            meta["type"] = "image";
                            meta["width"] = w.ToString();
                            meta["height"] = h.ToString();
                            meta["format"] = tf.ToString();

                            AtomicWriteAllText(cachePath + "meta", meta.ToString(string.Empty));
                            AtomicWriteAllBytes(cachePath, payload);
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
                            Trace("CacheWrite: path='" + cachePath + "' fmt=" + tf + " bytes=" + (payload != null ? payload.Length : 0));
                        }
                        catch (Exception ex)
                        {
                            s_CacheFails++;
                            Trace("CacheWriteFail: path='" + cachePath + "' err='" + ex.Message + "'");
                        }
                    }
                    else if (wantNative)
                    {
                        s_CacheSkips++;
                    }

                    if (needZstd)
                    {
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
                                string zdir = Path.GetDirectoryName(zstdPath);
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

                            AtomicWriteAllBytes(zstdPath, compressed);

                            JSONClass zmeta = new JSONClass();
                            zmeta["type"] = "compressed";
                            zmeta["width"] = wz.ToString();
                            zmeta["height"] = hz.ToString();
                            zmeta["format"] = tfz.ToString();
                            if (didZstdDownscale)
                            {
                                zmeta["downscaled"].AsBool = true;
                                zmeta["sourceWidth"] = zstdSourceW.ToString();
                                zmeta["sourceHeight"] = zstdSourceH.ToString();
                            }
                            zmeta["zstdLevel"].AsInt = level;
                            AtomicWriteAllText(zstdPath + "meta", zmeta.ToString(string.Empty));

                            s_ZstdWrites++;
                            s_ZstdOriginalBytes += payloadZstd.Length;
                            if (compressed != null) s_ZstdCompressedBytes += compressed.Length;
                            try
                            {
                                long zb = new FileInfo(zstdPath).Length;
                                s_ZstdDiskBytes += zb;
                                s_ZstdDiskBytesWritten += zb;
                            }
                            catch { }
                            try
                            {
                                string zmetaDisk = zstdPath + "meta";
                                if (File.Exists(zmetaDisk))
                                {
                                    long mb = new FileInfo(zmetaDisk).Length;
                                    s_ZstdDiskBytes += mb;
                                    s_ZstdDiskBytesWritten += mb;
                                }
                            }
                            catch { }
                            Trace("ZstdWrite: path='" + zstdPath + "' orig=" + payloadZstd.Length + " comp=" + (compressed != null ? compressed.Length : 0));

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
                            Trace("ZstdFail: path='" + (zstdPath ?? string.Empty) + "'");
                        }
                    }
                    else if (wantZstd)
                    {
                        s_ZstdSkips++;
                    }

                AfterZstd:

                    if (Time.realtimeSinceStartup - frameStart >= 0.008f)
                    {
                        frameStart = Time.realtimeSinceStartup;
                        yield return null;
                    }
                }

                if (Time.realtimeSinceStartup - frameStart >= 0.008f)
                {
                    frameStart = Time.realtimeSinceStartup;
                    yield return null;
                }
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

                    bool isPreset = fe.InternalPath.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vaj", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase);
                    if (!isPreset) continue;

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

                int maxJsonDepth = 2;
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
                    if (depth > maxJsonDepth) continue;
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

                    bool isPreset = fe.InternalPath.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vaj", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase);
                    if (!isPreset) continue;

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

                int maxJsonDepth = 2;
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
                    if (depth > maxJsonDepth) continue;
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

                for (int i = 0; i < seedTex.Count; i++)
                {
                    var rt = seedTex[i];
                    if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;
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

                yield return WorkerBuildSelectiveUnityCoroutine(tp, flagsMap, origMap);

                if (!s_BatchMode)
                {
                    s_PackagesProcessed = pkgIndex;
                    UpdateUiStatus();
                }
            }
        }

        private static bool TryBuildCachePayloadUnity(string imgUidPath, string internalPath, TextureFlags flags, int targetWidth, int targetHeight, bool allowZstdDownscale, out byte[] payload, out int width, out int height, out TextureFormat format, out bool hasAlpha, out string error, out bool didDownscale, out int sourceWidth, out int sourceHeight)
        {
            payload = null;
            width = 0;
            height = 0;
            format = TextureFormat.RGBA32;
            hasAlpha = false;
            error = null;
            didDownscale = false;
            sourceWidth = 0;
            sourceHeight = 0;

            byte[] src = null;
            try { src = FileManager.ReadAllBytes(imgUidPath); }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (src == null || src.Length == 0) return false;

            Texture2D tex = null;
            Texture2D working = null;
            RenderTexture rt = null;

            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, flags.linear);
                if (!tex.LoadImage(src))
                {
                    error = "LoadImage failed";
                    return false;
                }

                sourceWidth = tex.width;
                sourceHeight = tex.height;

                int effTargetW = targetWidth;
                int effTargetH = targetHeight;
                if (allowZstdDownscale && effTargetW <= 0 && effTargetH <= 0)
                {
                    int sw = tex.width;
                    int sh = tex.height;
                    if (Mathf.IsPowerOfTwo(sw) && Mathf.IsPowerOfTwo(sh) && (sw > 4096 || sh > 4096))
                    {
                        int div = 1;
                        while ((sw / div) > 4096 || (sh / div) > 4096)
                        {
                            div *= 2;
                            if (div <= 0) break;
                        }

                        if (div > 1)
                        {
                            effTargetW = sw / div;
                            effTargetH = sh / div;
                        }
                    }
                }

                if (effTargetW > 0 && effTargetH > 0 && (tex.width != effTargetW || tex.height != effTargetH))
                {
                    didDownscale = true;
                }

                if (effTargetW > 0 && effTargetH > 0 && (tex.width != effTargetW || tex.height != effTargetH))
                {
                    rt = RenderTexture.GetTemporary(effTargetW, effTargetH, 0, RenderTextureFormat.ARGB32, flags.linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
                    Graphics.Blit(tex, rt);
                    RenderTexture prev = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = rt;
                        working = new Texture2D(effTargetW, effTargetH, TextureFormat.RGBA32, false, flags.linear);
                        working.ReadPixels(new Rect(0, 0, effTargetW, effTargetH), 0, 0, false);
                        working.Apply(false, false);
                    }
                    finally
                    {
                        RenderTexture.active = prev;
                    }
                }
                else
                {
                    working = tex;
                }

                if (flags.isNormalMap || flags.invert || flags.createAlphaFromGrayscale || flags.createNormalFromBump)
                {
                    Color32[] colors = working.GetPixels32();
                    if (colors != null && colors.Length > 0)
                    {
                        if (flags.createAlphaFromGrayscale)
                        {
                            // LoadImage can yield RGB24 textures for PNGs without alpha.
                            // If we intend to write alpha, we must convert to an alpha-capable format first,
                            // otherwise SetPixels32/Apply will drop alpha and Compress() will choose DXT1.
                            if (working.format == TextureFormat.RGB24 || working.format == TextureFormat.DXT1)
                            {
                                Texture2D conv = new Texture2D(working.width, working.height, TextureFormat.RGBA32, false, flags.linear);
                                try
                                {
                                    conv.SetPixels32(colors);
                                    conv.Apply(false, false);
                                    if (working != null && working != tex) UnityEngine.Object.Destroy(working);
                                    working = conv;
                                    colors = working.GetPixels32();
                                }
                                catch
                                {
                                    try { UnityEngine.Object.Destroy(conv); } catch { }
                                }
                            }
                        }

                        if (flags.isNormalMap)
                        {
                            for (int i = 0; i < colors.Length; i++)
                            {
                                var c = colors[i];
                                c.a = 255;
                                colors[i] = c;
                            }
                        }

                        if (flags.invert)
                        {
                            for (int i = 0; i < colors.Length; i++)
                            {
                                var c = colors[i];
                                c.r = (byte)(255 - c.r);
                                c.g = (byte)(255 - c.g);
                                c.b = (byte)(255 - c.b);
                                c.a = (byte)(255 - c.a);
                                colors[i] = c;
                            }
                        }

                        if (flags.createAlphaFromGrayscale)
                        {
                            bool hasExistingAlpha = false;
                            for (int i = 0; i < colors.Length; i++)
                            {
                                if (colors[i].a != 255)
                                {
                                    hasExistingAlpha = true;
                                    break;
                                }
                            }

                            if (!hasExistingAlpha)
                            {
                                for (int i = 0; i < colors.Length; i++)
                                {
                                    var c = colors[i];
                                    int avg = (c.r + c.g + c.b) / 3;
                                    c.a = (byte)avg;
                                    colors[i] = c;
                                }
                            }

                            bool enforceDxt5 = flags.compress && !string.IsNullOrEmpty(internalPath) && internalPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                            if (enforceDxt5)
                            {
                                if (colors != null && colors.Length > 0)
                                {
                                    var c0 = colors[0];
                                    c0.a = 128;
                                    colors[0] = c0;
                                }
                            }
                        }

                        if (flags.createNormalFromBump)
                        {
                            int w = working.width;
                            int h = working.height;
                            float[][] hMap = new float[h][];
                            for (int yy = 0; yy < h; yy++)
                            {
                                hMap[yy] = new float[w];
                                for (int xx = 0; xx < w; xx++)
                                {
                                    int idx = yy * w + xx;
                                    var c = colors[idx];
                                    hMap[yy][xx] = (c.r + c.g + c.b) / 768f;
                                }
                            }

                            Vector3 v = default(Vector3);
                            for (int yy = 0; yy < h; yy++)
                            {
                                for (int xx = 0; xx < w; xx++)
                                {
                                    float h21 = 0.5f, h22 = 0.5f, h23 = 0.5f, h24 = 0.5f, h25 = 0.5f, h26 = 0.5f, h27 = 0.5f, h28 = 0.5f;
                                    int xm1 = xx - 1, xp1 = xx + 1, yp1 = yy + 1, ym1 = yy - 1;

                                    if (yp1 < h && xm1 >= 0) h21 = hMap[yp1][xm1];
                                    if (xm1 >= 0) h22 = hMap[yy][xm1];
                                    if (ym1 >= 0 && xm1 >= 0) h23 = hMap[ym1][xm1];
                                    if (yp1 < h) h24 = hMap[yp1][xx];
                                    if (ym1 >= 0) h25 = hMap[ym1][xx];
                                    if (yp1 < h && xp1 < w) h26 = hMap[yp1][xp1];
                                    if (xp1 < w) h27 = hMap[yy][xp1];
                                    if (ym1 >= 0 && xp1 < w) h28 = hMap[ym1][xp1];

                                    float nx = h26 + 2f * h27 + h28 - h21 - 2f * h22 - h23;
                                    float ny = h23 + 2f * h25 + h28 - h21 - 2f * h24 - h26;
                                    v.x = nx * flags.bumpStrength;
                                    v.y = ny * flags.bumpStrength;
                                    v.z = 1f;
                                    v.Normalize();

                                    int idx = yy * w + xx;
                                    colors[idx] = new Color32(
                                        (byte)((v.x * 0.5f + 0.5f) * 255f),
                                        (byte)((v.y * 0.5f + 0.5f) * 255f),
                                        (byte)((v.z * 0.5f + 0.5f) * 255f),
                                        255);
                                }
                            }
                        }

                        working.SetPixels32(colors);
                        working.Apply(false, false);
                    }
                }

                if (flags.compress)
                {
                    working.Compress(true);
                    working.Apply(false, false);
                }
                else
                {
                    string nameLower = null;
                    try { nameLower = (internalPath ?? string.Empty).ToLowerInvariant(); } catch { nameLower = string.Empty; }
                    bool forceRGBA = flags.isNormalMap;
                    bool forceRGB = flags.linear && !string.IsNullOrEmpty(nameLower) && nameLower.Contains("lut");

                    if (forceRGBA && working.format != TextureFormat.RGBA32)
                    {
                        Texture2D conv = new Texture2D(working.width, working.height, TextureFormat.RGBA32, false, flags.linear);
                        try
                        {
                            Color32[] cols = working.GetPixels32();
                            conv.SetPixels32(cols);
                            conv.Apply(false, false);
                            if (working != null && working != tex) UnityEngine.Object.Destroy(working);
                            working = conv;
                        }
                        catch
                        {
                            try { UnityEngine.Object.Destroy(conv); } catch { }
                        }
                    }
                    else if (forceRGB && working.format != TextureFormat.RGB24)
                    {
                        Texture2D conv = new Texture2D(working.width, working.height, TextureFormat.RGB24, false, flags.linear);
                        try
                        {
                            Color32[] cols = working.GetPixels32();
                            conv.SetPixels32(cols);
                            conv.Apply(false, false);
                            if (working != null && working != tex) UnityEngine.Object.Destroy(working);
                            working = conv;
                        }
                        catch
                        {
                            try { UnityEngine.Object.Destroy(conv); } catch { }
                        }
                    }
                }

                width = working.width;
                height = working.height;
                format = working.format;

                payload = working.GetRawTextureData();
                if (payload == null || payload.Length == 0)
                {
                    error = "GetRawTextureData failed";
                    return false;
                }

                if (format == TextureFormat.DXT5 || format == TextureFormat.RGBA32)
                {
                    hasAlpha = true;
                }
                else if (format == TextureFormat.DXT1 || format == TextureFormat.RGB24)
                {
                    hasAlpha = false;
                }
                else
                {
                    hasAlpha = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (working != null && working != tex) UnityEngine.Object.Destroy(working);
                if (tex != null) UnityEngine.Object.Destroy(tex);
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
        private static bool s_HotkeyDown;
        private static bool s_HotkeyNativeOnlyDown;
        private static bool s_MultiRunning;

        public static void Update()
        {
            try { NativeTextureCacheBuildOverlay.EnsureCreated(); } catch { }

            if (Input.GetKeyDown(KeyCode.F3)) s_HotkeyDown = true;
            if (Input.GetKeyDown(KeyCode.F7)) s_HotkeyNativeOnlyDown = true;

            if (!s_HotkeyDown && !s_HotkeyNativeOnlyDown) return;

            bool requestNativeOnly = s_HotkeyNativeOnlyDown;
            bool requestNativeAndZstd = s_HotkeyDown;
            s_HotkeyDown = false;
            s_HotkeyNativeOnlyDown = false;

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
            else if (requestNativeAndZstd)
            {
                // F3 is the default launcher: build VPB Zstd cache (optionally with 8k->4k downscale).
                NativeTextureOnDemandCache.SetNextJobWriteModeOverride(NativeTextureOnDemandCache.CacheWriteMode.ZstdOnly);
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
