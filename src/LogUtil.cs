using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;

namespace var_browser
{
    class LogUtil
    {
        static ManualLogSource logSource;
        static StreamWriter cleanLogWriter;
        static string cleanLogLastPath;
        static readonly DateTime processStartTime;
        static readonly Stopwatch sincePluginAwake = new Stopwatch();
        static readonly Stopwatch sceneClickStopwatch = new Stopwatch();
        static bool sceneClickActive;
        static double? sceneClickLastSeconds;
        static string sceneClickName;
        static bool sceneClickSawImageWork;
        static float sceneClickLastActivityRealtime;
        static bool sceneClickSceneLoadTotalEnded;
        static float sceneClickEndArmRealtime;
        static bool sceneClickEndArmed;
        static readonly Stopwatch sceneLoadStopwatch = new Stopwatch();
        static readonly Stopwatch sceneLoadInternalStopwatch = new Stopwatch();
        static string sceneLoadName;
        static bool sceneLoadActive;
        static bool sceneLoadInternalActive;
        static double? sceneLoadLastSeconds;

        static int sceneLoadStartFrame;
        static int sceneLoadEndFrame;
        static readonly List<float> sceneLoadFrameMs = new List<float>(4096);
        static float sceneLoadFrameMsSum;
        static float sceneLoadFrameMsMax;
        static int sceneLoadSt33;
        static int sceneLoadSt50;
        static int sceneLoadSt100;

        static float sceneLoadBeginRealtime;
        static int sceneLoadNotLoadingStableFrames;
        static int sceneLoadNotBusyStableFrames;
        static bool sceneLoadEndArmed;
        static float sceneLoadEndArmRealtime;
        static bool sceneLoadAutoEndFailedLogged;
        static bool sceneLoadSawImageWork;

        static long memAllocStart;
        static long memAllocEnd;
        static long memReservedStart;
        static long memReservedEnd;
        static long memMonoStart;
        static long memMonoEnd;
        static long memManagedStart;
        static long memManagedEnd;

        static int imageWorkInFlight;
        static float imageLastActivityRealtime;

        struct PerfMetric
        {
            public double totalMs;
            public long totalBytes;
            public int count;
        }

        struct SlowConvertSample
        {
            public string path;
            public double ms;
            public int srcW;
            public int srcH;
            public TextureFormat srcFmt;
            public int dstW;
            public int dstH;
            public TextureFormat dstFmt;
        }

        struct SlowDiskSample
        {
            public string op;
            public string path;
            public double ms;
            public long bytes;
        }

        static readonly Dictionary<string, PerfMetric> perf = new Dictionary<string, PerfMetric>(StringComparer.Ordinal);
        static readonly Dictionary<string, float> recentLogRealtime = new Dictionary<string, float>(StringComparer.Ordinal);
        static readonly List<SlowConvertSample> slowConverts = new List<SlowConvertSample>(128);
        static readonly List<SlowDiskSample> slowDisk = new List<SlowDiskSample>(128);
        static bool pluginAwakeMarked;
        static bool readyLogged;
        static double? readyProcessSeconds;
        static bool startupReadyLogged;

        static LogUtil()
        {
            try
            {
                processStartTime = Process.GetCurrentProcess().StartTime;
            }
            catch
            {
                processStartTime = DateTime.Now;
            }
        }

        public static void SetLogSource(ManualLogSource source)
        {
            logSource = source;
        }

        public static void ConfigureCleanLog(bool enabled, string path)
        {
            try
            {
                if (!enabled)
                {
                    if (cleanLogWriter != null)
                    {
                        try { cleanLogWriter.Flush(); } catch { }
                        try { cleanLogWriter.Dispose(); } catch { }
                    }
                    cleanLogWriter = null;
                    cleanLogLastPath = null;
                    return;
                }

                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                // Normalize slashes and allow relative paths.
                path = path.Replace('\\', '/');
                string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(Environment.CurrentDirectory, path);
                fullPath = fullPath.Replace('\\', '/');

                if (cleanLogWriter != null && string.Equals(cleanLogLastPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (cleanLogWriter != null)
                {
                    try { cleanLogWriter.Flush(); } catch { }
                    try { cleanLogWriter.Dispose(); } catch { }
                    cleanLogWriter = null;
                }

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                cleanLogWriter = new StreamWriter(new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
                cleanLogWriter.AutoFlush = true;
                cleanLogLastPath = fullPath;
            }
            catch
            {
                cleanLogWriter = null;
                cleanLogLastPath = null;
            }
        }

        static void WriteCleanLine(string msg)
        {
            try
            {
                if (cleanLogWriter == null) return;
                cleanLogWriter.WriteLine(msg);
            }
            catch
            {
                try
                {
                    if (cleanLogWriter != null)
                    {
                        try { cleanLogWriter.Flush(); } catch { }
                        try { cleanLogWriter.Dispose(); } catch { }
                    }
                }
                catch { }
                cleanLogWriter = null;
                cleanLogLastPath = null;
            }
        }

        public static void MarkPluginAwake()
        {
            if (pluginAwakeMarked)
            {
                return;
            }

            pluginAwakeMarked = true;
            sincePluginAwake.Start();
        }

        public static void Log(string log)
        {
            string msg = DateTime.Now.ToString("HH:mm:ss") + " (vb_log) " + log;
            WriteCleanLine(msg);
            if (logSource != null)
            {
                logSource.LogInfo(msg);
                return;
            }
            UnityEngine.Debug.Log(msg);
        }
        public static void LogError(string log)
        {
            string msg = DateTime.Now.ToString("HH:mm:ss") + " (vb_err) " + log;
            WriteCleanLine(msg);
            if (logSource != null)
            {
                logSource.LogError(msg);
                return;
            }
            UnityEngine.Debug.LogError(msg);
        }
        public static void LogWarning(string log)
        {
            string msg = DateTime.Now.ToString("HH:mm:ss") + " (vb_warn) " + log;
            WriteCleanLine(msg);
            if (logSource != null)
            {
                logSource.LogWarning(msg);
                return;
            }
            UnityEngine.Debug.LogWarning(msg);
        }

        static int GetTextureLogLevel()
        {
            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null)
                {
                    return Settings.Instance.TextureLogLevel.Value;
                }
            }
            catch { }

            return 1;
        }

        static int GetTextureSlowConvertMs()
        {
            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogSlowConvertMs != null)
                {
                    return Settings.Instance.TextureLogSlowConvertMs.Value;
                }
            }
            catch { }

            return 50;
        }

        static int GetTextureSlowDiskMs()
        {
            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogSlowDiskMs != null)
                {
                    return Settings.Instance.TextureLogSlowDiskMs.Value;
                }
            }
            catch { }

            return 20;
        }

        static bool ShouldLogKey(string key, float intervalSeconds)
        {
            if (string.IsNullOrEmpty(key)) return true;

            float now = Time.realtimeSinceStartup;
            float last;
            if (recentLogRealtime.TryGetValue(key, out last))
            {
                if ((now - last) < intervalSeconds)
                {
                    return false;
                }
            }

            recentLogRealtime[key] = now;

            if (recentLogRealtime.Count > 4096)
            {
                recentLogRealtime.Clear();
            }

            return true;
        }

        public static void LogTextureTrace(string key, string message)
        {
            if (GetTextureLogLevel() < 2) return;
            if (!ShouldLogKey(key, 1.0f)) return;
            Log(message);
        }

        public static void LogTextureSlowConvert(string imgPath, double ms, int srcW, int srcH, TextureFormat srcFmt, int dstW, int dstH, TextureFormat dstFmt)
        {
            if (GetTextureLogLevel() <= 0) return;
            if (ms < GetTextureSlowConvertMs()) return;
            if (slowConverts.Count < 2048)
            {
                slowConverts.Add(new SlowConvertSample
                {
                    path = imgPath,
                    ms = ms,
                    srcW = srcW,
                    srcH = srcH,
                    srcFmt = srcFmt,
                    dstW = dstW,
                    dstH = dstH,
                    dstFmt = dstFmt,
                });
            }
            LogWarning("TEX_SLOW_CONVERT " + ms.ToString("0.00") + "ms | " + srcFmt + "(" + srcW + "," + srcH + ") -> " + dstFmt + "(" + dstW + "," + dstH + ") | " + imgPath);
        }

        public static void LogTextureSlowDisk(string op, string path, double ms, long bytes)
        {
            if (GetTextureLogLevel() <= 0) return;
            if (ms < GetTextureSlowDiskMs()) return;
            if (slowDisk.Count < 2048)
            {
                slowDisk.Add(new SlowDiskSample
                {
                    op = op,
                    path = path,
                    ms = ms,
                    bytes = bytes,
                });
            }
            LogWarning("TEX_SLOW_DISK " + op + " " + ms.ToString("0.00") + "ms (" + FormatBytes(bytes) + ") | " + path);
        }

        public static void LogVerboseUi(string message)
        {
            try
            {
                if (Settings.Instance != null && Settings.Instance.LogVerboseUi != null && Settings.Instance.LogVerboseUi.Value)
                {
                    Log(message);
                }
            }
            catch { }
        }

        public static void LogStartupReadyOnce(string context)
        {
            if (startupReadyLogged)
            {
                return;
            }

            startupReadyLogged = true;
            LogWarning("STARTUP_READY " + context + " | since process start: " + GetSecondsSinceProcessStart().ToString("0.000") + "s");
        }

        public static void BeginSceneClick(string saveName)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                return;
            }

            sceneClickName = saveName;
            sceneClickLastSeconds = null;
            sceneClickActive = true;
            sceneClickSawImageWork = false;
            sceneClickLastActivityRealtime = Time.realtimeSinceStartup;
            sceneClickSceneLoadTotalEnded = false;
            sceneClickEndArmRealtime = 0f;
            sceneClickEndArmed = false;
            sceneClickStopwatch.Reset();
            sceneClickStopwatch.Start();
        }

        public static bool IsSceneClickActive()
        {
            return sceneClickActive;
        }

        public static void SceneClickUpdate()
        {
            if (!sceneClickActive)
            {
                return;
            }

            // If we haven't even reached the normal scene-load completion point yet, don't end.
            if (!sceneClickSceneLoadTotalEnded)
            {
                return;
            }

            // If we saw image work, wait until image loading is idle for a quiet window.
            if (sceneClickSawImageWork)
            {
                if (IsImageLoadingBusy())
                {
                    sceneClickEndArmed = false;
                    return;
                }

                float idleSecondsRequired = 5.0f;
                try
                {
                    if (Settings.Instance != null && Settings.Instance.SceneLoadImagesIdleSeconds != null)
                    {
                        idleSecondsRequired = Mathf.Clamp(Settings.Instance.SceneLoadImagesIdleSeconds.Value, 0f, 60f);
                    }
                }
                catch { }
                if (idleSecondsRequired <= 0f) idleSecondsRequired = 0.5f;

                if ((Time.realtimeSinceStartup - sceneClickLastActivityRealtime) < idleSecondsRequired)
                {
                    sceneClickEndArmed = false;
                    return;
                }
            }
            else
            {
                // No image work observed; fall back to SuperController not-loading.
                bool? loading = TryGetSuperControllerLoading();
                if (loading.HasValue && loading.Value)
                {
                    sceneClickEndArmed = false;
                    return;
                }
            }

            // Arm end and wait a moment; avoids ending on the exact frame state flips.
            if (!sceneClickEndArmed)
            {
                sceneClickEndArmed = true;
                sceneClickEndArmRealtime = Time.realtimeSinceStartup;
                return;
            }

            if ((Time.realtimeSinceStartup - sceneClickEndArmRealtime) < 0.5f)
            {
                return;
            }

            sceneClickStopwatch.Stop();
            sceneClickActive = false;
            sceneClickLastSeconds = sceneClickStopwatch.Elapsed.TotalSeconds;
            sceneClickName = null;
        }

        public static double? GetSceneClickSecondsForDisplay()
        {
            if (sceneClickActive)
            {
                return sceneClickStopwatch.Elapsed.TotalSeconds;
            }

            return sceneClickLastSeconds;
        }

        public static void BeginSceneLoad(string saveName)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                return;
            }

            sceneLoadName = saveName;
            sceneLoadActive = true;
            sceneLoadStopwatch.Reset();
            sceneLoadStopwatch.Start();

            sceneLoadStartFrame = Time.frameCount;
            sceneLoadEndFrame = sceneLoadStartFrame;
            sceneLoadFrameMs.Clear();
            sceneLoadFrameMsSum = 0f;
            sceneLoadFrameMsMax = 0f;
            sceneLoadSt33 = 0;
            sceneLoadSt50 = 0;
            sceneLoadSt100 = 0;

            sceneLoadBeginRealtime = Time.realtimeSinceStartup;
            sceneLoadNotLoadingStableFrames = 0;
            sceneLoadNotBusyStableFrames = 0;
            sceneLoadEndArmed = false;
            sceneLoadEndArmRealtime = 0f;
            sceneLoadSawImageWork = false;

            imageLastActivityRealtime = Time.realtimeSinceStartup;

            CaptureMemoryStart();

            slowConverts.Clear();
            slowDisk.Clear();

            sceneLoadInternalActive = true;
            sceneLoadInternalStopwatch.Reset();
            sceneLoadInternalStopwatch.Start();
        }

        public static bool IsSceneLoadActive()
        {
            return sceneLoadActive;
        }

        public static string GetSceneLoadName()
        {
            return sceneLoadName;
        }

        public static double? GetSceneLoadSecondsForDisplay()
        {
            if (sceneLoadActive)
            {
                return sceneLoadStopwatch.Elapsed.TotalSeconds;
            }

            return sceneLoadLastSeconds;
        }

        public static bool IsSceneLoadInternalActive()
        {
            return sceneLoadInternalActive;
        }

        public static void SceneLoadFrameTick(float unscaledDeltaTime)
        {
            if (!sceneLoadActive)
            {
                return;
            }

            if (unscaledDeltaTime <= 0f)
            {
                return;
            }

            float ms = unscaledDeltaTime * 1000f;
            sceneLoadFrameMs.Add(ms);
            sceneLoadFrameMsSum += ms;
            if (ms > sceneLoadFrameMsMax) sceneLoadFrameMsMax = ms;
            if (ms > 33f) sceneLoadSt33++;
            if (ms > 50f) sceneLoadSt50++;
            if (ms > 100f) sceneLoadSt100++;
        }

        public static void SceneLoadUpdate()
        {
            if (!sceneLoadActive)
            {
                return;
            }

            // Hard safety timeout so we never get stuck.
            if ((Time.realtimeSinceStartup - sceneLoadBeginRealtime) > 600f)
            {
                EndSceneLoadTotal("AutoEnd.Timeout");
                return;
            }

            bool? loading = TryGetSuperControllerLoading();
            if (!loading.HasValue)
            {
                // If we can't read loading state, fall back to ending when the scene has been "stable" long enough.
                // We keep this conservative to avoid cutting off long async loads.
                if (!sceneLoadAutoEndFailedLogged)
                {
                    sceneLoadAutoEndFailedLogged = true;
                    LogWarning("SCENE_LOAD_TOTAL auto-end: could not read SuperController loading state, using timeout fallback");
                }
                return;
            }

            if (loading.Value)
            {
                sceneLoadNotLoadingStableFrames = 0;
                sceneLoadNotBusyStableFrames = 0;
                sceneLoadEndArmed = false;
                return;
            }

            // Require not-loading for a few frames to avoid flapping.
            sceneLoadNotLoadingStableFrames++;

            bool waitForImagesIdle = false;
            try
            {
                waitForImagesIdle = Settings.Instance != null && Settings.Instance.SceneLoadWaitForImagesIdle != null && Settings.Instance.SceneLoadWaitForImagesIdle.Value;
            }
            catch { }

            // If we observed any image work during this scene load, we must not end on "not loading" alone.
            // Late image requests can arrive after the main load is finished.
            if (sceneLoadSawImageWork)
            {
                waitForImagesIdle = true;
            }

            if (!waitForImagesIdle)
            {
                if (sceneLoadNotLoadingStableFrames >= 5)
                {
                    if (!sceneLoadEndArmed)
                    {
                        sceneLoadEndArmed = true;
                        sceneLoadEndArmRealtime = Time.realtimeSinceStartup;
                        return;
                    }

                    if ((Time.realtimeSinceStartup - sceneLoadEndArmRealtime) >= 0.5f)
                    {
                        EndSceneLoadTotal("AutoEnd.NotLoading");
                    }
                }
                else
                {
                    sceneLoadEndArmed = false;
                }
                return;
            }

            bool busy = IsImageLoadingBusy();
            if (busy)
            {
                sceneLoadNotBusyStableFrames = 0;
                sceneLoadEndArmed = false;
                return;
            }

            // Require a quiet window after the last image activity.
            // Scene loads can trigger image bursts after the main load is complete.
            float idleSecondsRequired = 5.0f;
            try
            {
                if (Settings.Instance != null && Settings.Instance.SceneLoadImagesIdleSeconds != null)
                {
                    idleSecondsRequired = Mathf.Clamp(Settings.Instance.SceneLoadImagesIdleSeconds.Value, 0f, 60f);
                }
            }
            catch { }
            // Prevent flapping/early end when late image bursts happen.
            if (idleSecondsRequired <= 0f) idleSecondsRequired = 0.5f;
            if ((Time.realtimeSinceStartup - imageLastActivityRealtime) < idleSecondsRequired)
            {
                sceneLoadNotBusyStableFrames = 0;
                sceneLoadEndArmed = false;
                return;
            }

            sceneLoadNotBusyStableFrames++;
            if (sceneLoadNotLoadingStableFrames >= 5 && sceneLoadNotBusyStableFrames >= 5)
            {
                if (!sceneLoadEndArmed)
                {
                    // Arm end and wait a moment; this avoids ending in the same frame a new burst starts.
                    sceneLoadEndArmed = true;
                    sceneLoadEndArmRealtime = Time.realtimeSinceStartup;
                    return;
                }

                if ((Time.realtimeSinceStartup - sceneLoadEndArmRealtime) >= 0.5f)
                {
                    EndSceneLoadTotal("AutoEnd.NotLoading+ImagesIdleWindow");
                }
            }
            else
            {
                sceneLoadEndArmed = false;
            }
        }

        static bool IsImageLoadingBusy()
        {
            try
            {
                if (Interlocked.CompareExchange(ref imageWorkInFlight, 0, 0) > 0)
                {
                    return true;
                }

                // Vanilla loader (ImageLoaderThreaded) can still be active even when VPB's custom pipeline is not.
                // If any images are queued, treat this as busy for scene-load timing.
                try
                {
                    if (ImageLoaderThreaded.singleton != null)
                    {
                        var trV = Traverse.Create(ImageLoaderThreaded.singleton);

                        try
                        {
                            var n = trV.Field("numRealQueuedImages").GetValue();
                            if (n is int ni && ni > 0) return true;
                        }
                        catch { }

                        try
                        {
                            var q = trV.Field("queuedImages").GetValue();
                            if (q != null)
                            {
                                var countProp = q.GetType().GetProperty("Count");
                                if (countProp != null)
                                {
                                    var cObj = countProp.GetValue(q, null);
                                    if (cObj is int ci && ci > 0) return true;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                if (CustomImageLoaderThreaded.singleton == null)
                {
                    return false;
                }

                var tr = Traverse.Create(CustomImageLoaderThreaded.singleton);

                // Primary signal used by the loader itself.
                try
                {
                    var n = tr.Field("numRealQueuedImages").GetValue();
                    if (n is int ni && ni > 0) return true;
                }
                catch { }

                // Fallback: check the internal linked list queue length.
                try
                {
                    var q = tr.Field("queuedImages").GetValue();
                    if (q != null)
                    {
                        var countProp = q.GetType().GetProperty("Count");
                        if (countProp != null)
                        {
                            var cObj = countProp.GetValue(q, null);
                            if (cObj is int ci && ci > 0) return true;
                        }
                    }
                }
                catch { }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static void BeginImageWork()
        {
            MarkImageActivity();
            sceneLoadSawImageWork = true;
            Interlocked.Increment(ref imageWorkInFlight);
        }

        public static void EndImageWork()
        {
            var v = Interlocked.Decrement(ref imageWorkInFlight);
            if (v < 0)
            {
                Interlocked.Exchange(ref imageWorkInFlight, 0);
            }
        }

        public static void MarkImageActivity()
        {
            // realtimeSinceStartup is fine here; we only compare deltas.
            imageLastActivityRealtime = Time.realtimeSinceStartup;
            if (sceneClickActive)
            {
                sceneClickSawImageWork = true;
                sceneClickLastActivityRealtime = Time.realtimeSinceStartup;
                // If a late burst happens, cancel any pending end.
                sceneClickEndArmed = false;
            }

            // If any image activity happens during a scene load, we must wait for image-idle before ending.
            if (sceneLoadActive)
            {
                sceneLoadSawImageWork = true;
            }

            // Cancel any pending end if a new image burst starts.
            sceneLoadEndArmed = false;
            sceneLoadNotBusyStableFrames = 0;
        }

        static bool? TryGetSuperControllerLoading()
        {
            try
            {
                if (SuperController.singleton == null)
                {
                    return null;
                }

                var tr = Traverse.Create(SuperController.singleton);

                // Try common field/property names.
                foreach (var name in new[] { "isLoading", "loading", "_isLoading", "_loading", "loadingUIActive", "isLoadingScene" })
                {
                    try
                    {
                        var v = tr.Property(name);
                        if (v != null)
                        {
                            var obj = v.GetValue();
                            if (obj is bool b1) return b1;
                        }
                    }
                    catch { }

                    try
                    {
                        var v = tr.Field(name);
                        if (v != null)
                        {
                            var obj = v.GetValue();
                            if (obj is bool b2) return b2;
                        }
                    }
                    catch { }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static void EndSceneLoadInternal(string context)
        {
            if (!sceneLoadInternalActive)
            {
                return;
            }

            sceneLoadInternalActive = false;
            sceneLoadInternalStopwatch.Stop();
            var ms = sceneLoadInternalStopwatch.Elapsed.TotalMilliseconds;
            LogWarning("SCENE_LOAD_INTERNAL " + context + " | " + sceneLoadName + " | " + ms.ToString("0.00") + "ms");
        }

        public static void EndSceneLoadTotal(string context)
        {
            if (!sceneLoadActive)
            {
                return;
            }

            sceneLoadActive = false;
            sceneLoadStopwatch.Stop();
            var ms = sceneLoadStopwatch.Elapsed.TotalMilliseconds;
            sceneLoadLastSeconds = ms / 1000.0;
            var name = sceneLoadName;
            sceneLoadName = null;
            sceneLoadInternalActive = false;

            sceneLoadEndFrame = Time.frameCount;
            CaptureMemoryEnd();

            LogWarning("SCENE_LOAD_TOTAL " + context + " | " + name + " | " + ms.ToString("0.00") + "ms");

            if (sceneClickActive)
            {
                if (string.IsNullOrEmpty(sceneClickName) || string.Equals(sceneClickName, name, StringComparison.OrdinalIgnoreCase))
                {
                    sceneClickSceneLoadTotalEnded = true;
                }
            }

            try
            {
                LogSceneLoadStats(name, ms / 1000.0);
                LogPerfSummary();
                LogTextureOffenderSummary();
            }
            catch (Exception ex)
            {
                LogError("SCENELOAD STATS exception: " + ex);
            }

            perf.Clear();
            slowConverts.Clear();
            slowDisk.Clear();
            sceneLoadAutoEndFailedLogged = false;
            sceneLoadNotBusyStableFrames = 0;
        }

        static void LogTextureOffenderSummary()
        {
            if (slowConverts.Count == 0 && slowDisk.Count == 0)
            {
                return;
            }

            const int topN = 5;

            if (slowConverts.Count > 0)
            {
                var top = slowConverts.OrderByDescending(s => s.ms).Take(topN).ToArray();
                var sb = new StringBuilder(512);
                sb.Append("[VB] TEX_TOP_CONVERT ");
                for (int i = 0; i < top.Length; i++)
                {
                    if (i > 0) sb.Append(" | ");
                    var s = top[i];
                    sb.Append(s.ms.ToString("0.00"));
                    sb.Append("ms ");
                    sb.Append(s.srcFmt);
                    sb.Append("(");
                    sb.Append(s.srcW);
                    sb.Append("x");
                    sb.Append(s.srcH);
                    sb.Append(") -> ");
                    sb.Append(s.dstFmt);
                    sb.Append("(");
                    sb.Append(s.dstW);
                    sb.Append("x");
                    sb.Append(s.dstH);
                    sb.Append(") ");
                    sb.Append(s.path);
                }
                LogWarning(sb.ToString());
            }

            if (slowDisk.Count > 0)
            {
                var top = slowDisk.OrderByDescending(s => s.ms).Take(topN).ToArray();
                var sb = new StringBuilder(512);
                sb.Append("[VB] TEX_TOP_DISK ");
                for (int i = 0; i < top.Length; i++)
                {
                    if (i > 0) sb.Append(" | ");
                    var s = top[i];
                    sb.Append(s.op);
                    sb.Append(" ");
                    sb.Append(s.ms.ToString("0.00"));
                    sb.Append("ms (");
                    sb.Append(FormatBytes(s.bytes));
                    sb.Append(") ");
                    sb.Append(s.path);
                }
                LogWarning(sb.ToString());
            }
        }

        static void CaptureMemoryStart()
        {
            memAllocStart = SafeGet(() => Profiler.GetTotalAllocatedMemoryLong());
            memReservedStart = SafeGet(() => Profiler.GetTotalReservedMemoryLong());
            memMonoStart = SafeGet(() => Profiler.GetMonoUsedSizeLong());
            memManagedStart = SafeGet(() => GC.GetTotalMemory(false));
        }

        static void CaptureMemoryEnd()
        {
            memAllocEnd = SafeGet(() => Profiler.GetTotalAllocatedMemoryLong());
            memReservedEnd = SafeGet(() => Profiler.GetTotalReservedMemoryLong());
            memMonoEnd = SafeGet(() => Profiler.GetMonoUsedSizeLong());
            memManagedEnd = SafeGet(() => GC.GetTotalMemory(false));
        }

        static T SafeGet<T>(Func<T> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return default(T);
            }
        }

        static void LogSceneLoadStats(string sceneName, double durSeconds)
        {
            var samples = sceneLoadFrameMs.Count;
            double fpsAvg = (durSeconds > 0.00001) ? (samples / durSeconds) : 0.0;

            float avgMs = samples > 0 ? (sceneLoadFrameMsSum / samples) : 0f;
            float p95 = 0f;
            if (samples > 0)
            {
                var arr = sceneLoadFrameMs.ToArray();
                Array.Sort(arr);
                int idx = Mathf.Clamp(Mathf.CeilToInt(arr.Length * 0.95f) - 1, 0, arr.Length - 1);
                if (idx >= 0 && idx < arr.Length)
                {
                    p95 = arr[idx];
                }
            }

            var sb = new StringBuilder(512);
            sb.Append("[VB] SCENELOAD STATS ");
            sb.Append(sceneName);
            sb.Append(" | dur:");
            sb.Append(durSeconds.ToString("0.00"));
            sb.Append("s frames:");
            sb.Append(samples);
            sb.Append(" | fpsavg:");
            sb.Append(fpsAvg.ToString("0.0"));
            sb.Append(" | framems avg:");
            sb.Append(avgMs.ToString("0.0"));
            sb.Append(" p95:");
            sb.Append(p95.ToString("0.0"));
            sb.Append(" max:");
            sb.Append(sceneLoadFrameMsMax.ToString("0.0"));
            sb.Append(" | st33:");
            sb.Append(sceneLoadSt33);
            sb.Append(" st50:");
            sb.Append(sceneLoadSt50);
            sb.Append(" st100:");
            sb.Append(sceneLoadSt100);

            sb.Append(" | mem alloc:");
            sb.Append(FormatBytes(memAllocEnd));
            sb.Append("(+");
            sb.Append(FormatBytes(memAllocEnd - memAllocStart));
            sb.Append(") reserved:");
            sb.Append(FormatBytes(memReservedEnd));
            sb.Append("(+");
            sb.Append(FormatBytes(memReservedEnd - memReservedStart));
            sb.Append(") managed:");
            sb.Append(FormatBytes(memManagedEnd));
            sb.Append("(+");
            sb.Append(FormatBytes(memManagedEnd - memManagedStart));
            sb.Append(")");

            LogWarning(sb.ToString());
        }

        static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0B";
            bool neg = bytes < 0;
            double b = Math.Abs((double)bytes);
            string suffix;
            double value;
            if (b >= 1024d * 1024d * 1024d)
            {
                value = b / (1024d * 1024d * 1024d);
                suffix = "GB";
            }
            else if (b >= 1024d * 1024d)
            {
                value = b / (1024d * 1024d);
                suffix = "MB";
            }
            else if (b >= 1024d)
            {
                value = b / 1024d;
                suffix = "KB";
            }
            else
            {
                value = b;
                suffix = "B";
            }

            return (neg ? "-" : "") + value.ToString("0.00") + suffix;
        }

        public static void PerfAdd(string key, double ms, long bytes)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            PerfMetric m;
            if (!perf.TryGetValue(key, out m))
            {
                m = new PerfMetric();
            }

            m.totalMs += ms;
            m.totalBytes += bytes;
            m.count += 1;
            perf[key] = m;
        }

        static void LogPerfSummary()
        {
            if (perf.Count == 0)
            {
                return;
            }

            var keys = perf.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var sb = new StringBuilder(512);
            sb.Append("[VB] PERF ");
            bool first = true;
            foreach (var k in keys)
            {
                var m = perf[k];
                if (!first) sb.Append(" | ");
                first = false;
                sb.Append(k);
                sb.Append("=");
                sb.Append(m.totalMs.ToString("0.00"));
                sb.Append("ms (");
                sb.Append(m.count);
                if (m.totalBytes != 0)
                {
                    sb.Append(", ");
                    sb.Append(FormatBytes(m.totalBytes));
                }
                sb.Append(")");
            }

            LogWarning(sb.ToString());
        }

        public static void LogReadyOnce(string context)
        {
            if (readyLogged)
            {
                return;
            }

            readyLogged = true;

            var sinceProcessStart = DateTime.Now - processStartTime;
            readyProcessSeconds = sinceProcessStart.TotalSeconds;
            var sincePluginStart = sincePluginAwake.IsRunning ? sincePluginAwake.Elapsed : TimeSpan.Zero;
            LogWarning(string.Format("READY {0} | since process start: {1:0.000}s | since plugin awake: {2:0.000}s", context, sinceProcessStart.TotalSeconds, sincePluginStart.TotalSeconds));
        }

        public static double GetSecondsSinceProcessStart()
        {
            return (DateTime.Now - processStartTime).TotalSeconds;
        }

        public static double GetStartupSecondsForDisplay()
        {
            if (readyProcessSeconds.HasValue)
            {
                return readyProcessSeconds.Value;
            }

            return GetSecondsSinceProcessStart();
        }
    }
}
