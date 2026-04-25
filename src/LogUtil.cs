using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using VPB.src.util;

namespace VPB
{
    class LogUtil
    {
        public static readonly object JsonLock = new object();
        private static VPBLogSource logSource = VPBLogger.GetInstance(VPBModule.Main);

        static readonly DateTime processStartTime;
        static DateTime pluginSessionStartTime;
        static float pluginSessionEngineStartSeconds;
        static readonly Stopwatch sincePluginAwake = new Stopwatch();
        static readonly Stopwatch sceneClickStopwatch = new Stopwatch();
        static bool sceneClickActive;
        static double? sceneClickLastSeconds;
        static string sceneClickName;
        static bool sceneClickSawImageWork;
        static float sceneClickLastActivityRealtime;
        static float sceneClickBeginRealtime;
        static bool sceneClickSceneLoadTotalEnded;
        static float sceneClickEndArmRealtime;
        static bool sceneClickEndArmed;
        static readonly Stopwatch sceneLoadStopwatch = new Stopwatch();
        static readonly Stopwatch sceneLoadInternalStopwatch = new Stopwatch();
        static string sceneLoadName;
        static string sceneLoadPackageUid;
        static bool sceneLoadActive;
        static bool sceneLoadInternalActive;
        static double? sceneLoadLastSeconds;
        static int sceneLoadTotalSerial;

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
        static float sceneLoadFirstNotLoadingRealtime;
        static float sceneLoadFirstNotBusyRealtime;
        static float sceneLoadEndCriteriaRealtime;
        static float sceneLoadPreLoadInternalRealtime;
        static float sceneLoadPostLoadInternalRealtime;
        static float sceneLoadWorldUiActivatedRealtime;
        static float sceneLoadFirstImageActivityRealtime;
        static float sceneSettleNextSampleRealtime;
        static int sceneSettleSampleCount;
        static int sceneSettleBusySampleCount;
        static int sceneSettleQueueMax;
        static int sceneSettleQueueLast;
        static int sceneSettleAtomsMin;
        static int sceneSettleAtomsMax;
        static int sceneSettleAtomsLast;
        static int sceneSettlePersonsMin;
        static int sceneSettlePersonsMax;
        static int sceneSettlePersonsLast;
        static int sceneSettlePrevAtoms;
        static int sceneSettlePrevPersons;
        static int sceneSettleStableSampleStreak;
        static float sceneSettleSoftReadyRealtime;
        static int sceneSettleFileExistsMissCount;
        static int sceneSettleOpenStreamMissCount;
        static int sceneSettleVarEntryMissCount;
        static int sceneSettleOnDemandRetryCount;
        static int sceneLoadTailUpdateCount;
        static int sceneLoadTailBusyCount;
        static int sceneLoadTailIdleWindowBlockCount;
        static int sceneLoadLoadingFlapTransitions;
        static int sceneLoadBusyFlapTransitions;
        static bool sceneLoadPrevLoadingKnown;
        static bool sceneLoadPrevLoadingValue;
        static bool sceneLoadPrevBusyKnown;
        static bool sceneLoadPrevBusyValue;

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

        struct SlowDiskSample
        {
            public string op;
            public string path;
            public double ms;
            public long bytes;
        }

        static readonly Dictionary<string, PerfMetric> perf = new Dictionary<string, PerfMetric>(StringComparer.Ordinal);
        static readonly Dictionary<string, float> recentLogRealtime = new Dictionary<string, float>(StringComparer.Ordinal);
        static readonly List<SlowDiskSample> slowDisk = new List<SlowDiskSample>(128);
        static bool pluginAwakeMarked;
        static bool readyLogged;
        static double? readyProcessSeconds;
        static bool startupReadyLogged;
        static bool startupAutoReadyLogged;

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

            ResetPluginSession();
        }

        public static void ResetPluginSession()
        {
            pluginSessionStartTime = DateTime.Now;
            pluginSessionEngineStartSeconds = Time.realtimeSinceStartup;
            readyLogged = false;
            startupReadyLogged = false;
            startupAutoReadyLogged = false;
            readyProcessSeconds = null;
            sincePluginAwake.Reset();
            pluginAwakeMarked = false;
        }

        static string lastTimeString;
        static long lastTimeTicks;

        static string GetTimeString()
        {
             long now = DateTime.Now.Ticks / 10000000;
             if (now != lastTimeTicks)
             {
                 lastTimeTicks = now;
                 lastTimeString = DateTime.Now.ToString("HH:mm:ss");
             }
             return lastTimeString;
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

        /// <see cref="VPBLogSource.LogInfo(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogInfo")]
        public static void Log(string log)
        {
            logSource.LogInfo(log);
        }

        /// <see cref="VPBLogSource.LogInfo(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogInfo")]
        public static void Log(string p1, string p2)
        {
            logSource.LogInfo($"{p1} {p2}");
        }

        /// <see cref="VPBLogSource.LogInfo(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogInfo")]
        public static void Log(string p1, string p2, string p3)
        {

            logSource.LogInfo($"{p1} {p2} {p3}");
        }

        /// <see cref="VPBLogSource.LogInfo(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogInfo")]
        public static void Log(string p1, int p2)
        {

            logSource.LogInfo($"{p1} {p2}");
        }

        /// <see cref="VPBLogSource.LogInfo(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogInfo")]
        public static void Log(string p1, float p2)
        {
            logSource.LogInfo($"{p1} {p2}");
        }

        /// <see cref="VPBLogSource.LogError(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogError")]
        public static void LogError(string log)
        {
            logSource.LogError(log);
        }

        /// <see cref="VPBLogSource.LogError(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogError")]
        public static void LogError(string p1, string p2)
        {
            logSource.LogError($"{p1} {p2}");
        }

        /// <see cref="VPBLogSource.LogWarning(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogWarning")]
        public static void LogWarning(string log)
        {
            logSource.LogWarning(log);
        }


        /// <see cref="VPBLogSource.LogWarning(object)"/>
        /// <see cref="VPBLogger.GetInstance(VPBModule, bool)"/>
        [Obsolete("Prefer VPBLogSource.LogWarning")]
        public static void LogWarning(string p1, string p2)
        {
            logSource.LogWarning($"{p1} {p2}");
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

        public static void LogTextureSlowDisk(string op, string path, double ms, long bytes)
        {
            if (GetTextureLogLevel() <= 0) return;
            if (ms < 20) return;
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

            var sb = StringBuilderPool.Get();
            FormatBytes(sb, bytes);
            string msg = sb.ToString();
            StringBuilderPool.Return(sb);
            LogWarning($"TEX_SLOW_DISK {op} {ms.ToString("0.00")}ms ({msg}) | {path}");
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
            LogWarning("STARTUP_MILESTONE " + context + " | since process start: " + GetSecondsSinceProcessStart().ToString("0.000") + "s");
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
            sceneClickBeginRealtime = Time.realtimeSinceStartup;
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

            // Hard safety timeout: if we never see EndSceneLoadTotal (or never reach idle), don't run forever.
            if ((Time.realtimeSinceStartup - sceneClickBeginRealtime) > 600f)
            {
                try
                {
                    sceneClickStopwatch.Stop();
                }
                catch { }
                sceneClickActive = false;
                sceneClickLastSeconds = sceneClickStopwatch.Elapsed.TotalSeconds;
                sceneClickName = null;
                LogWarning("SCENE_CLICK auto-end: timeout");
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

                float idleSecondsRequired = 0.5f;

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

        public static void StartupWatchdogUpdate(bool isFileManagerInited, bool isUiInited)
        {
            if (readyLogged) return;
            if (startupAutoReadyLogged) return;

            // Require at least some signal of progress before auto-ready.
            if (!startupReadyLogged && !isFileManagerInited) return;

            // Conservative timeout to avoid false positives on very slow machines.
            // If UI init never completes (exceptions, missing dependencies, etc), freeze timer anyway.
            float elapsed = Time.realtimeSinceStartup - pluginSessionEngineStartSeconds;
            if (elapsed < 180f) return;

            startupAutoReadyLogged = true;
            LogReadyOnce("AutoReady.Timeout");
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
            sceneLoadPackageUid = null;
            try
            {
                int idx = saveName.IndexOf(":/", StringComparison.Ordinal);
                if (idx > 0)
                {
                    sceneLoadPackageUid = saveName.Substring(0, idx);
                }
            }
            catch { }
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
            sceneLoadFirstNotLoadingRealtime = -1f;
            sceneLoadFirstNotBusyRealtime = -1f;
            sceneLoadEndCriteriaRealtime = -1f;
            sceneLoadPreLoadInternalRealtime = -1f;
            sceneLoadPostLoadInternalRealtime = -1f;
            sceneLoadWorldUiActivatedRealtime = -1f;
            sceneLoadFirstImageActivityRealtime = -1f;
            sceneSettleNextSampleRealtime = 0f;
            sceneSettleSampleCount = 0;
            sceneSettleBusySampleCount = 0;
            sceneSettleQueueMax = -1;
            sceneSettleQueueLast = -1;
            sceneSettleAtomsMin = -1;
            sceneSettleAtomsMax = -1;
            sceneSettleAtomsLast = -1;
            sceneSettlePersonsMin = -1;
            sceneSettlePersonsMax = -1;
            sceneSettlePersonsLast = -1;
            sceneSettlePrevAtoms = -1;
            sceneSettlePrevPersons = -1;
            sceneSettleStableSampleStreak = 0;
            sceneSettleSoftReadyRealtime = -1f;
            sceneSettleFileExistsMissCount = 0;
            sceneSettleOpenStreamMissCount = 0;
            sceneSettleVarEntryMissCount = 0;
            sceneSettleOnDemandRetryCount = 0;
            sceneLoadTailUpdateCount = 0;
            sceneLoadTailBusyCount = 0;
            sceneLoadTailIdleWindowBlockCount = 0;
            sceneLoadLoadingFlapTransitions = 0;
            sceneLoadBusyFlapTransitions = 0;
            sceneLoadPrevLoadingKnown = false;
            sceneLoadPrevLoadingValue = false;
            sceneLoadPrevBusyKnown = false;
            sceneLoadPrevBusyValue = false;

            imageLastActivityRealtime = Time.realtimeSinceStartup;

            CaptureMemoryStart();

            slowDisk.Clear();

            sceneLoadInternalActive = true;
            sceneLoadInternalStopwatch.Reset();
            sceneLoadInternalStopwatch.Start();

            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.StartSceneLoad();
            }
        }

        public static bool IsSceneLoadActive()
        {
            return sceneLoadActive;
        }

        public static string GetSceneLoadName()
        {
            return sceneLoadName;
        }

        public static string GetSceneLoadPackageUid()
        {
            return sceneLoadPackageUid;
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

        public static int GetSceneLoadTotalSerial()
        {
            return sceneLoadTotalSerial;
        }

        public static void MarkScenePhasePreLoadInternal()
        {
            if (!sceneLoadActive) return;
            if (sceneLoadPreLoadInternalRealtime < 0f)
                sceneLoadPreLoadInternalRealtime = Time.realtimeSinceStartup;
        }

        public static void MarkScenePhasePostLoadInternal()
        {
            if (!sceneLoadActive) return;
            if (sceneLoadPostLoadInternalRealtime < 0f)
                sceneLoadPostLoadInternalRealtime = Time.realtimeSinceStartup;
        }

        public static void MarkScenePhaseWorldUiActivated()
        {
            if (!sceneLoadActive) return;
            if (sceneLoadWorldUiActivatedRealtime < 0f)
                sceneLoadWorldUiActivatedRealtime = Time.realtimeSinceStartup;
        }

        public static void RecordFileExistsResult(bool exists)
        {
            if (!sceneLoadActive) return;
            if (exists) return;
            sceneSettleFileExistsMissCount++;
        }

        public static void RecordOpenStreamResult(bool success)
        {
            if (!sceneLoadActive) return;
            if (success) return;
            sceneSettleOpenStreamMissCount++;
        }

        public static void RecordVarEntryMiss()
        {
            if (!sceneLoadActive) return;
            sceneSettleVarEntryMissCount++;
        }

        public static void RecordOnDemandRetry()
        {
            if (!sceneLoadActive) return;
            sceneSettleOnDemandRetryCount++;
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

            SampleSceneSettleWindow();

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

            if (sceneLoadPrevLoadingKnown && !sceneLoadPrevLoadingValue && loading.Value && sceneLoadFirstNotLoadingRealtime >= 0f)
            {
                sceneLoadLoadingFlapTransitions++;
            }
            sceneLoadPrevLoadingKnown = true;
            sceneLoadPrevLoadingValue = loading.Value;

            if (loading.Value)
            {
                sceneLoadNotLoadingStableFrames = 0;
                sceneLoadNotBusyStableFrames = 0;
                sceneLoadEndArmed = false;
                sceneLoadPrevBusyKnown = false;
                return;
            }

            if (sceneLoadFirstNotLoadingRealtime < 0f)
            {
                sceneLoadFirstNotLoadingRealtime = Time.realtimeSinceStartup;
            }

            // Require not-loading for a few frames to avoid flapping.
            sceneLoadNotLoadingStableFrames++;
            sceneLoadTailUpdateCount++;


            bool busy = IsImageLoadingBusy();
            if (sceneLoadPrevBusyKnown && !sceneLoadPrevBusyValue && busy && sceneLoadFirstNotBusyRealtime >= 0f)
            {
                sceneLoadBusyFlapTransitions++;
            }
            sceneLoadPrevBusyKnown = true;
            sceneLoadPrevBusyValue = busy;

            if (busy)
            {
                sceneLoadNotBusyStableFrames = 0;
                sceneLoadEndArmed = false;
                sceneLoadTailBusyCount++;
                return;
            }

            if (sceneLoadFirstNotBusyRealtime < 0f)
            {
                sceneLoadFirstNotBusyRealtime = Time.realtimeSinceStartup;
            }

            // Require a quiet window after the last image activity.
            // Scene loads can trigger image bursts after the main load is complete.
            float idleSecondsRequired = 0.5f;
            if ((Time.realtimeSinceStartup - imageLastActivityRealtime) < idleSecondsRequired)
            {
                sceneLoadNotBusyStableFrames = 0;
                sceneLoadEndArmed = false;
                sceneLoadTailIdleWindowBlockCount++;
                return;
            }

            sceneLoadNotBusyStableFrames++;
            if (sceneLoadNotLoadingStableFrames >= 5 && sceneLoadNotBusyStableFrames >= 5)
            {
                if (!sceneLoadEndArmed)
                {
                    // Arm end and wait a moment; this avoids ending in the same frame a new burst starts.
                    if (sceneLoadEndCriteriaRealtime < 0f)
                    {
                        sceneLoadEndCriteriaRealtime = Time.realtimeSinceStartup;
                    }
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

        static void SampleSceneSettleWindow()
        {
            // Focus on the expensive settle window after LoadInternal returns and before not-loading.
            if (sceneLoadPostLoadInternalRealtime < 0f) return;
            if (sceneLoadFirstNotLoadingRealtime >= 0f) return;

            float now = Time.realtimeSinceStartup;
            if (now < sceneSettleNextSampleRealtime) return;
            sceneSettleNextSampleRealtime = now + 0.5f;

            sceneSettleSampleCount++;

            bool busy = IsImageLoadingBusy();
            if (busy) sceneSettleBusySampleCount++;

            int queueDepth = TryGetCombinedImageQueueDepth();
            sceneSettleQueueLast = queueDepth;
            if (queueDepth >= 0)
            {
                if (sceneSettleQueueMax < 0 || queueDepth > sceneSettleQueueMax)
                    sceneSettleQueueMax = queueDepth;
            }

            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            try
            {
                var atoms = sc.GetAtoms();
                int atomCount = atoms != null ? atoms.Count : 0;
                int personCount = 0;
                if (atoms != null)
                {
                    for (int i = 0; i < atoms.Count; i++)
                    {
                        Atom a = atoms[i];
                        if (a != null && SceneUtils.IsPersonLikeAtom(a)) personCount++;
                    }
                }

                sceneSettleAtomsLast = atomCount;
                if (sceneSettleAtomsMin < 0 || atomCount < sceneSettleAtomsMin) sceneSettleAtomsMin = atomCount;
                if (sceneSettleAtomsMax < 0 || atomCount > sceneSettleAtomsMax) sceneSettleAtomsMax = atomCount;

                sceneSettlePersonsLast = personCount;
                if (sceneSettlePersonsMin < 0 || personCount < sceneSettlePersonsMin) sceneSettlePersonsMin = personCount;
                if (sceneSettlePersonsMax < 0 || personCount > sceneSettlePersonsMax) sceneSettlePersonsMax = personCount;

                // "Soft-ready": settle window shows stable atom/person counts and no active image busy for >=1s.
                bool stableCounts = sceneSettlePrevAtoms == atomCount && sceneSettlePrevPersons == personCount;
                sceneSettlePrevAtoms = atomCount;
                sceneSettlePrevPersons = personCount;
                if (stableCounts && !busy && queueDepth >= 0 && queueDepth <= 2)
                {
                    sceneSettleStableSampleStreak++;
                    if (sceneSettleStableSampleStreak >= 2 && sceneSettleSoftReadyRealtime < 0f)
                    {
                        sceneSettleSoftReadyRealtime = now;
                    }
                }
                else
                {
                    sceneSettleStableSampleStreak = 0;
                }
            }
            catch { }
        }

        static int TryGetCombinedImageQueueDepth()
        {
            int total = 0;
            bool any = false;

            try
            {
                if (ImageLoaderThreaded.singleton != null)
                {
                    int c = TryGetImageQueueDepthFromLoader(Traverse.Create(ImageLoaderThreaded.singleton));
                    if (c >= 0)
                    {
                        total += c;
                        any = true;
                    }
                }
            }
            catch { }

            try
            {
                if (CustomImageLoaderThreaded.singleton != null)
                {
                    int c = TryGetImageQueueDepthFromLoader(Traverse.Create(CustomImageLoaderThreaded.singleton));
                    if (c >= 0)
                    {
                        total += c;
                        any = true;
                    }
                }
            }
            catch { }

            return any ? total : -1;
        }

        static int TryGetImageQueueDepthFromLoader(Traverse tr)
        {
            if (tr == null) return -1;
            try
            {
                object n = tr.Field("numRealQueuedImages").GetValue();
                if (n is int ni && ni >= 0) return ni;
            }
            catch { }

            try
            {
                object q = tr.Field("queuedImages").GetValue();
                if (q != null)
                {
                    var countProp = q.GetType().GetProperty("Count");
                    if (countProp != null)
                    {
                        object cObj = countProp.GetValue(q, null);
                        if (cObj is int ci && ci >= 0) return ci;
                    }
                }
            }
            catch { }

            return -1;
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
            if (sceneLoadActive && sceneLoadFirstImageActivityRealtime < 0f)
            {
                sceneLoadFirstImageActivityRealtime = imageLastActivityRealtime;
            }
            if (sceneClickActive)
            {
                sceneClickSawImageWork = true;
                sceneClickLastActivityRealtime = Time.realtimeSinceStartup;
                // If a late burst happens, cancel any pending end.
                sceneClickEndArmed = false;
            }

            // If any image activity happens during a scene load, we must wait for image-idle before ending.
            // Cancel any pending end if a new image burst starts.
            sceneLoadEndArmed = false;
            sceneLoadNotBusyStableFrames = 0;
        }

        public static bool IsSceneLoading()
        {
            if (sceneLoadActive || sceneLoadInternalActive) return true;
            var loading = TryGetSuperControllerLoading();
            if (loading.HasValue && loading.Value) return true;
            return false;
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
            sceneLoadPackageUid = null;
            sceneLoadInternalActive = false;
            unchecked { sceneLoadTotalSerial++; }

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
                LogSceneLoadLifecycleBreakdown(context, name, ms / 1000.0);
                LogSceneLoadPhaseBreakdown(context, name, ms / 1000.0);
                LogSceneLoadSettleBreakdown(context, name, ms / 1000.0);
                LogSceneLoadStats(name, ms / 1000.0);
                LogPerfSummary();
                LogTextureOffenderSummary();
            }
            catch (Exception ex)
            {
                LogError("SCENELOAD STATS exception: " + ex);
            }

            perf.Clear();
            slowDisk.Clear();
            sceneLoadAutoEndFailedLogged = false;
            sceneLoadNotBusyStableFrames = 0;

            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.EndSceneLoad();
            }

            // Scene content (including Person atoms in GetAtoms()) is reliably settled once total load completes.
            try { GalleryPanel.NotifyAllPanelsSceneTargetsChanged(); } catch { }

            if (ImageLoadingMgr.singleton != null)
            {
                ImageLoadingMgr.singleton.ProcessCandidates();
            }

            CacheCleanupManager.FlushHitsBatch();
        }

        static void LogSceneLoadLifecycleBreakdown(string context, string sceneName, double durSeconds)
        {
            float endRealtime = Time.realtimeSinceStartup;
            float preLoadInternalSec = sceneLoadPreLoadInternalRealtime >= 0f
                ? Mathf.Max(0f, sceneLoadPreLoadInternalRealtime - sceneLoadBeginRealtime)
                : -1f;
            float loadInternalWindowSec = (sceneLoadPreLoadInternalRealtime >= 0f && sceneLoadPostLoadInternalRealtime >= 0f)
                ? Mathf.Max(0f, sceneLoadPostLoadInternalRealtime - sceneLoadPreLoadInternalRealtime)
                : -1f;
            float postLoadInternalToNotLoadingSec = (sceneLoadPostLoadInternalRealtime >= 0f && sceneLoadFirstNotLoadingRealtime >= 0f)
                ? Mathf.Max(0f, sceneLoadFirstNotLoadingRealtime - sceneLoadPostLoadInternalRealtime)
                : -1f;
            float firstImageActivitySec = sceneLoadFirstImageActivityRealtime >= 0f
                ? Mathf.Max(0f, sceneLoadFirstImageActivityRealtime - sceneLoadBeginRealtime)
                : -1f;
            float worldUiSec = sceneLoadWorldUiActivatedRealtime >= 0f
                ? Mathf.Max(0f, sceneLoadWorldUiActivatedRealtime - sceneLoadBeginRealtime)
                : -1f;
            float worldUiToEndSec = (sceneLoadWorldUiActivatedRealtime >= 0f)
                ? Mathf.Max(0f, endRealtime - sceneLoadWorldUiActivatedRealtime)
                : -1f;

            var sb = StringBuilderPool.Get();
            try
            {
                sb.Append(GetTimeString());
                sb.Append(" (vb_warn) ");
                sb.Append("SCENE_LOAD_LIFECYCLE ");
                sb.Append(context);
                sb.Append(" | ");
                sb.Append(sceneName);
                sb.Append(" | dur:");
                sb.Append(durSeconds.ToString("0.00"));
                sb.Append("s preLoadInternal:");
                sb.Append(preLoadInternalSec >= 0f ? preLoadInternalSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" loadInternalWindow:");
                sb.Append(loadInternalWindowSec >= 0f ? loadInternalWindowSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" postLoadInternalToNotLoading:");
                sb.Append(postLoadInternalToNotLoadingSec >= 0f ? postLoadInternalToNotLoadingSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" firstImageActivity:");
                sb.Append(firstImageActivitySec >= 0f ? firstImageActivitySec.ToString("0.00") + "s" : "n/a");
                sb.Append(" worldUi:");
                sb.Append(worldUiSec >= 0f ? worldUiSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" worldUiToEnd:");
                sb.Append(worldUiToEndSec >= 0f ? worldUiToEndSec.ToString("0.00") + "s" : "n/a");
                logSource.LogWarning(sb.ToString());
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        static void LogSceneLoadPhaseBreakdown(string context, string sceneName, double durSeconds)
        {
            float endRealtime = Time.realtimeSinceStartup;
            float firstNotLoading = sceneLoadFirstNotLoadingRealtime;

            float preNotLoadingSec = -1f;
            float tailSec = -1f;
            if (firstNotLoading >= 0f)
            {
                preNotLoadingSec = Mathf.Max(0f, firstNotLoading - sceneLoadBeginRealtime);
                tailSec = Mathf.Max(0f, endRealtime - firstNotLoading);
            }

            float firstNotBusyDelaySec = -1f;
            if (firstNotLoading >= 0f && sceneLoadFirstNotBusyRealtime >= 0f)
            {
                firstNotBusyDelaySec = Mathf.Max(0f, sceneLoadFirstNotBusyRealtime - firstNotLoading);
            }

            float criteriaReadyDelaySec = -1f;
            if (firstNotLoading >= 0f && sceneLoadEndCriteriaRealtime >= 0f)
            {
                criteriaReadyDelaySec = Mathf.Max(0f, sceneLoadEndCriteriaRealtime - firstNotLoading);
            }

            float armedToEndSec = -1f;
            if (sceneLoadEndCriteriaRealtime >= 0f)
            {
                armedToEndSec = Mathf.Max(0f, endRealtime - sceneLoadEndCriteriaRealtime);
            }

            var sb = StringBuilderPool.Get();
            try
            {
                sb.Append(GetTimeString());
                sb.Append(" (vb_warn) ");
                sb.Append("SCENE_LOAD_PHASES ");
                sb.Append(context);
                sb.Append(" | ");
                sb.Append(sceneName);
                sb.Append(" | dur:");
                sb.Append(durSeconds.ToString("0.00"));
                sb.Append("s preNotLoading:");
                sb.Append(preNotLoadingSec >= 0f ? preNotLoadingSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" tail:");
                sb.Append(tailSec >= 0f ? tailSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" firstNotBusy:");
                sb.Append(firstNotBusyDelaySec >= 0f ? firstNotBusyDelaySec.ToString("0.00") + "s" : "n/a");
                sb.Append(" criteriaReady:");
                sb.Append(criteriaReadyDelaySec >= 0f ? criteriaReadyDelaySec.ToString("0.00") + "s" : "n/a");
                sb.Append(" armedToEnd:");
                sb.Append(armedToEndSec >= 0f ? armedToEndSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" | tailTicks:");
                sb.Append(sceneLoadTailUpdateCount);
                sb.Append(" busyTicks:");
                sb.Append(sceneLoadTailBusyCount);
                sb.Append(" idleBlocks:");
                sb.Append(sceneLoadTailIdleWindowBlockCount);
                sb.Append(" loadingFlaps:");
                sb.Append(sceneLoadLoadingFlapTransitions);
                sb.Append(" busyFlaps:");
                sb.Append(sceneLoadBusyFlapTransitions);

                logSource.LogWarning(sb.ToString());
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        static void LogSceneLoadSettleBreakdown(string context, string sceneName, double durSeconds)
        {
            float settleWindowSec = -1f;
            if (sceneLoadPostLoadInternalRealtime >= 0f && sceneLoadFirstNotLoadingRealtime >= 0f)
            {
                settleWindowSec = Mathf.Max(0f, sceneLoadFirstNotLoadingRealtime - sceneLoadPostLoadInternalRealtime);
            }

            string atomsRange = (sceneSettleAtomsMin >= 0 && sceneSettleAtomsMax >= 0)
                ? (sceneSettleAtomsMin + "->" + sceneSettleAtomsMax + " (last:" + sceneSettleAtomsLast + ")")
                : "n/a";
            string personsRange = (sceneSettlePersonsMin >= 0 && sceneSettlePersonsMax >= 0)
                ? (sceneSettlePersonsMin + "->" + sceneSettlePersonsMax + " (last:" + sceneSettlePersonsLast + ")")
                : "n/a";
            string queueStats = sceneSettleQueueMax >= 0
                ? ("max:" + sceneSettleQueueMax + " last:" + sceneSettleQueueLast)
                : "n/a";
            string busyRatio = sceneSettleSampleCount > 0
                ? ((sceneSettleBusySampleCount * 100.0f / sceneSettleSampleCount).ToString("0.0") + "%")
                : "n/a";
            float softReadyDelaySec = (sceneSettleSoftReadyRealtime >= 0f && sceneLoadPostLoadInternalRealtime >= 0f)
                ? Mathf.Max(0f, sceneSettleSoftReadyRealtime - sceneLoadPostLoadInternalRealtime)
                : -1f;

            var sb = StringBuilderPool.Get();
            try
            {
                sb.Append(GetTimeString());
                sb.Append(" (vb_warn) ");
                sb.Append("SCENE_LOAD_SETTLE ");
                sb.Append(context);
                sb.Append(" | ");
                sb.Append(sceneName);
                sb.Append(" | dur:");
                sb.Append(durSeconds.ToString("0.00"));
                sb.Append("s window:");
                sb.Append(settleWindowSec >= 0f ? settleWindowSec.ToString("0.00") + "s" : "n/a");
                sb.Append(" samples:");
                sb.Append(sceneSettleSampleCount);
                sb.Append(" busy:");
                sb.Append(sceneSettleBusySampleCount);
                sb.Append(" (");
                sb.Append(busyRatio);
                sb.Append(")");
                sb.Append(" queue:");
                sb.Append(queueStats);
                sb.Append(" softReady:");
                sb.Append(softReadyDelaySec >= 0f ? softReadyDelaySec.ToString("0.00") + "s" : "n/a");
                sb.Append(" atoms:");
                sb.Append(atomsRange);
                sb.Append(" persons:");
                sb.Append(personsRange);
                sb.Append(" misses[file/open/var]:");
                sb.Append(sceneSettleFileExistsMissCount);
                sb.Append("/");
                sb.Append(sceneSettleOpenStreamMissCount);
                sb.Append("/");
                sb.Append(sceneSettleVarEntryMissCount);
                sb.Append(" onDemandRetries:");
                sb.Append(sceneSettleOnDemandRetryCount);
                logSource.LogWarning(sb.ToString());
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        static void LogTextureOffenderSummary()
        {
            if (slowDisk.Count == 0)
            {
                return;
            }

            const int topN = 5;

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
                    FormatBytes(sb, s.bytes);
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

            var sb = StringBuilderPool.Get();
            try
            {
                sb.Append(GetTimeString());
                sb.Append(" (vb_warn) ");
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
            FormatBytes(sb, memAllocEnd);
            sb.Append("(+");
            FormatBytes(sb, memAllocEnd - memAllocStart);
            sb.Append(") reserved:");
            FormatBytes(sb, memReservedEnd);
            sb.Append("(+");
            FormatBytes(sb, memReservedEnd - memReservedStart);
            sb.Append(") managed:");
            FormatBytes(sb, memManagedEnd);
            sb.Append("(+");
            FormatBytes(sb, memManagedEnd - memManagedStart);
            sb.Append(")");

            LogWarning(sb.ToString());
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        static string FormatBytes(long bytes)
        {
            var sb = StringBuilderPool.Get();
            FormatBytes(sb, bytes);
            string s = sb.ToString();
            StringBuilderPool.Return(sb);
            return s;
        }

        static void FormatBytes(StringBuilder sb, long bytes)
        {
            if (bytes == 0)
            {
                sb.Append("0B");
                return;
            }
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

            if (neg) sb.Append("-");
            sb.Append(value.ToString("0.00"));
            sb.Append(suffix);
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
            var sb = StringBuilderPool.Get();
            try
            {
                sb.Append(GetTimeString());
                sb.Append(" (vb_warn) ");
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
                        FormatBytes(sb, m.totalBytes);
                    }
                    sb.Append(")");
                }

                LogWarning(sb.ToString());
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        public static void LogReadyOnce(string context)
        {
            if (readyLogged)
            {
                return;
            }

            readyLogged = true;

            readyProcessSeconds = (double)(Time.realtimeSinceStartup - pluginSessionEngineStartSeconds);
            var sinceProcessStart = DateTime.Now - processStartTime;
            var sincePluginSessionStart = DateTime.Now - pluginSessionStartTime;
            var sincePluginStart = sincePluginAwake.IsRunning ? sincePluginAwake.Elapsed : TimeSpan.Zero;
            LogWarning(string.Format("READY {0} | since plugin session start: {1:0.000}s | since process start: {2:0.000}s | since plugin awake: {3:0.000}s", context, sincePluginSessionStart.TotalSeconds, sinceProcessStart.TotalSeconds, sincePluginStart.TotalSeconds));
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

            return (double)(Time.realtimeSinceStartup - pluginSessionEngineStartSeconds);
        }

        static class StringBuilderPool
        {
            private static readonly Stack<StringBuilder> _pool = new Stack<StringBuilder>();
            private static readonly object _lock = new object();

            public static StringBuilder Get()
            {
                lock (_lock)
                {
                    if (_pool.Count > 0) return _pool.Pop();
                }
                return new StringBuilder(512);
            }

            public static void Return(StringBuilder sb)
            {
                sb.Length = 0;
                lock (_lock)
                {
                    if (_pool.Count < 32) _pool.Push(sb);
                }
            }
        }
    }
}
