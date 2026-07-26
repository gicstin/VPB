using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using SimpleJSON;

namespace VPB
{
    /// <summary>
    /// Cold-start timing for VaM native paths and VPB package refresh. Enable via
    /// BepInEx Logging.LogStartupTiming (or LogStartupDetails).
    /// </summary>
    internal static class VamStartupProfiler
    {
        public const string LogTag = "[VPB.Startup.Timing]";

        const int TopSlowCount = 10;
        const double TopSlowMinMs = 10.0;

        static bool s_CacheValid;
        static bool s_CachedEnabled;
        static bool s_SummaryFlushed;

        static readonly object s_Lock = new object();
        static readonly Dictionary<string, double> s_PhasesMs = new Dictionary<string, double>(StringComparer.Ordinal);
        static readonly List<KeyValuePair<string, double>> s_TopNativeScans = new List<KeyValuePair<string, double>>(TopSlowCount);

        static Stopwatch s_NativeRefreshSw;
        static int s_NativeRefreshDepth;
        static int s_NativeRegisterCount;
        static long s_NativeScanCount;
        static double s_NativeScanTotalMs;

        static Stopwatch s_VpbRefreshSw;
        static string s_VpbRefreshReason = "";

        static Stopwatch s_SyncVamXSw;
        static long s_SyncVamXSkippedCount;
        static Stopwatch s_BootstrapSw;
        static string s_BootstrapUrl = "";
        static int s_VpbRefreshDepth;

        static double s_LastMilestoneProcessSec = -1.0;
        static double s_VpbPackageScanCompleteSec = -1.0;
        static readonly List<string> s_Timeline = new List<string>(64);
        static readonly Dictionary<string, int> s_ScopeDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<string, Stopwatch> s_ScopeSw = new Dictionary<string, Stopwatch>(StringComparer.Ordinal);

        const double GapWarnSecDefault = 2.0;

        public static bool CachedEnabled
        {
            get
            {
                if (!s_CacheValid) RefreshCache();
                return s_CachedEnabled;
            }
        }

        public static void RefreshCache()
        {
            s_CacheValid = true;
            try
            {
                var inst = Settings.Instance;
                bool timing = inst != null && inst.LogStartupTiming != null && inst.LogStartupTiming.Value;
                bool details = inst != null && inst.LogStartupDetails != null && inst.LogStartupDetails.Value;
                s_CachedEnabled = timing || details;
            }
            catch
            {
                s_CachedEnabled = false;
            }
        }

        /// <summary>VPB package scan finished and MessageKit posted — start of pre-SuperController.Awake wait.</summary>
        public static void MarkVpbPackageScanComplete()
        {
            try { s_VpbPackageScanCompleteSec = LogUtil.GetSecondsSinceProcessStart(); } catch { }
            Milestone("vpb_package_scan_complete");
        }

        public static void OnUnityStartupLog(string message)
        {
            if (!CachedEnabled || string.IsNullOrEmpty(message)) return;
            try
            {
                if (message.IndexOf("PerformancePatch loading", StringComparison.OrdinalIgnoreCase) >= 0)
                    Milestone("UnityLog.PerformancePatch_loading");
                if (message.IndexOf("PerformancePatch loaded", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Milestone("UnityLog.PerformancePatch_loaded");
                    var plugin = VamHookPlugin.singleton;
                    if (plugin != null)
                        VamStartupProfilerPatches.TryApplyLateOptionalPatchesRetry(plugin.StartupHarmony);
                }
            }
            catch { }
        }

        public static void LogPreSuperControllerAwaitIfAny()
        {
            if (!CachedEnabled || s_VpbPackageScanCompleteSec < 0) return;
            try
            {
                double now = LogUtil.GetSecondsSinceProcessStart();
                double wait = now - s_VpbPackageScanCompleteSec;
                if (wait >= GapWarnSecDefault)
                    Milestone("pre_supercontroller_await_sec=" + wait.ToString("0.000"));
            }
            catch { }
        }

        public static void Milestone(string name)
        {
            if (!CachedEnabled || string.IsNullOrEmpty(name)) return;
            try
            {
                double t = LogUtil.GetSecondsSinceProcessStart();
                double delta = s_LastMilestoneProcessSec >= 0 ? (t - s_LastMilestoneProcessSec) : 0;
                s_LastMilestoneProcessSec = t;

                lock (s_Lock)
                {
                    s_Timeline.Add(name + "@" + t.ToString("0.000") + "s");
                }

                string line = LogTag + " " + name + " t=" + t.ToString("0.000") + "s";
                if (delta >= GapWarnSecDefault)
                    line += " gap=+" + delta.ToString("0.000") + "s";
                LogUtil.Log(line);
            }
            catch { }
        }

        public static void BeginScope(string scope)
        {
            if (!CachedEnabled || string.IsNullOrEmpty(scope)) return;
            lock (s_Lock)
            {
                int depth;
                if (!s_ScopeDepth.TryGetValue(scope, out depth))
                    depth = 0;
                if (depth == 0)
                {
                    var sw = Stopwatch.StartNew();
                    s_ScopeSw[scope] = sw;
                    Milestone(scope + "_begin");
                }
                s_ScopeDepth[scope] = depth + 1;
            }
        }

        public static void EndScope(string scope)
        {
            if (!CachedEnabled || string.IsNullOrEmpty(scope)) return;
            lock (s_Lock)
            {
                int depth;
                if (!s_ScopeDepth.TryGetValue(scope, out depth) || depth <= 0)
                    return;
                depth--;
                s_ScopeDepth[scope] = depth;
                if (depth != 0) return;

                Stopwatch sw;
                double ms = 0;
                if (s_ScopeSw.TryGetValue(scope, out sw) && sw != null)
                {
                    sw.Stop();
                    ms = sw.Elapsed.TotalMilliseconds;
                    s_ScopeSw.Remove(scope);
                }
                AddPhaseMs(scope, ms);
                Milestone(scope + "_end ms=" + ms.ToString("0"));
            }
        }

        public static void AddPhaseMs(string name, double ms)
        {
            if (!CachedEnabled || string.IsNullOrEmpty(name) || ms < 0) return;
            lock (s_Lock)
            {
                double prev;
                if (s_PhasesMs.TryGetValue(name, out prev))
                    s_PhasesMs[name] = prev + ms;
                else
                    s_PhasesMs[name] = ms;
            }
        }

        public static void BeginNativeRefresh()
        {
            if (!CachedEnabled) return;
            lock (s_Lock)
            {
                if (s_NativeRefreshDepth == 0)
                    s_NativeRefreshSw = Stopwatch.StartNew();
                s_NativeRefreshDepth++;
            }
        }

        public static void EndNativeRefresh(int scanAllowed, int scanBlocked, Exception ex)
        {
            if (!CachedEnabled) return;
            lock (s_Lock)
            {
                if (s_NativeRefreshDepth <= 0) return;
                s_NativeRefreshDepth--;
                if (s_NativeRefreshDepth > 0) return;

                double ms = 0;
                if (s_NativeRefreshSw != null)
                {
                    s_NativeRefreshSw.Stop();
                    ms = s_NativeRefreshSw.Elapsed.TotalMilliseconds;
                    s_NativeRefreshSw = null;
                }

                AddPhaseMs("native_refresh_total", ms);
                try
                {
                    LogUtil.Log(string.Format(
                        LogTag + " native FileManager.Refresh done ms={0:0} registers={1} scan_allowed={2} scan_blocked={3} pkgs_scanned={4} scan_ms_sum={5:0} ex={6}",
                        ms, s_NativeRegisterCount, scanAllowed, scanBlocked, s_NativeScanCount, s_NativeScanTotalMs,
                        ex != null ? ex.GetType().Name : "none"));
                }
                catch { }

                s_NativeRegisterCount = 0;
                s_NativeScanCount = 0;
                s_NativeScanTotalMs = 0;
            }
        }

        public static void RecordNativeRegister()
        {
            if (!CachedEnabled) return;
            lock (s_Lock)
            {
                s_NativeRegisterCount++;
            }
        }

        public static void RecordNativePackageScan(string uid, double ms)
        {
            if (!CachedEnabled) return;
            lock (s_Lock)
            {
                s_NativeScanCount++;
                s_NativeScanTotalMs += ms;
                if (ms < TopSlowMinMs) return;

                string key = string.IsNullOrEmpty(uid) ? "(unknown)" : uid;
                int idx = -1;
                for (int i = 0; i < s_TopNativeScans.Count; i++)
                {
                    if (s_TopNativeScans[i].Key == key)
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx >= 0)
                {
                    if (ms <= s_TopNativeScans[idx].Value) return;
                    s_TopNativeScans.RemoveAt(idx);
                }

                int insertAt = 0;
                while (insertAt < s_TopNativeScans.Count && s_TopNativeScans[insertAt].Value >= ms)
                    insertAt++;
                s_TopNativeScans.Insert(insertAt, new KeyValuePair<string, double>(key, ms));
                if (s_TopNativeScans.Count > TopSlowCount)
                    s_TopNativeScans.RemoveAt(s_TopNativeScans.Count - 1);
            }
        }

        public static void BeginVpbRefresh(string reason)
        {
            if (!CachedEnabled) return;
            bool outer = false;
            string tag = string.IsNullOrEmpty(reason) ? "manual" : reason;
            lock (s_Lock)
            {
                if (s_VpbRefreshDepth == 0)
                {
                    s_VpbRefreshReason = tag;
                    s_VpbRefreshSw = Stopwatch.StartNew();
                    outer = true;
                }
                s_VpbRefreshDepth++;
            }
            if (outer)
                Milestone("vpb_refresh_begin reason=" + tag);
        }

        public static void EndVpbRefresh()
        {
            if (!CachedEnabled) return;
            double ms = 0;
            string reason = "";
            lock (s_Lock)
            {
                if (s_VpbRefreshDepth <= 0) return;
                s_VpbRefreshDepth--;
                if (s_VpbRefreshDepth > 0) return;

                if (s_VpbRefreshSw != null)
                {
                    s_VpbRefreshSw.Stop();
                    ms = s_VpbRefreshSw.Elapsed.TotalMilliseconds;
                    s_VpbRefreshSw = null;
                }
                reason = s_VpbRefreshReason ?? "";
                s_VpbRefreshReason = "";
                AddPhaseMs("vpb_refresh_" + reason, ms);
            }
            try
            {
                LogUtil.Log(LogTag + " vpb FileManager.Refresh done ms=" + ms.ToString("0") + " reason=" + reason);
            }
            catch { }
        }

        public static void RecordSqlRebuildMs(long totalMs, bool aborted)
        {
            if (!CachedEnabled) return;
            AddPhaseMs(aborted ? "sql_rebuild_abort" : "sql_rebuild", totalMs);
            try
            {
                LogUtil.Log(LogTag + " sql_rebuild recorded ms=" + totalMs + " aborted=" + (aborted ? "1" : "0"));
            }
            catch { }
        }

        public static void RecordSqlRebuildSkipped()
        {
            if (!CachedEnabled) return;
            AddPhaseMs("sql_rebuild_skipped", 0);
        }

        public static void RecordSyncVamXSkipped()
        {
            if (!CachedEnabled) return;
            System.Threading.Interlocked.Increment(ref s_SyncVamXSkippedCount);
            AddPhaseMs("sync_vamx_skipped", 0);
        }

        public static void BeginSyncVamX()
        {
            if (!CachedEnabled) return;
            lock (s_Lock)
            {
                s_SyncVamXSw = Stopwatch.StartNew();
            }
        }

        public static void EndSyncVamX()
        {
            if (!CachedEnabled) return;
            double ms = 0;
            lock (s_Lock)
            {
                if (s_SyncVamXSw != null)
                {
                    s_SyncVamXSw.Stop();
                    ms = s_SyncVamXSw.Elapsed.TotalMilliseconds;
                    s_SyncVamXSw = null;
                    AddPhaseMs("sync_vamx", ms);
                }
            }
            try
            {
                LogUtil.Log(LogTag + " SyncVamX ms=" + ms.ToString("0"));
            }
            catch { }
        }

        public static void BeginBootstrapPlugin(string url)
        {
            if (!CachedEnabled) return;
            lock (s_Lock)
            {
                s_BootstrapUrl = url ?? "";
                s_BootstrapSw = Stopwatch.StartNew();
            }
            Milestone("bootstrap_begin url=" + (url ?? ""));
        }

        public static void EndBootstrapPlugin(Exception ex)
        {
            if (!CachedEnabled) return;
            double ms = 0;
            string url = "";
            lock (s_Lock)
            {
                url = s_BootstrapUrl ?? "";
                s_BootstrapUrl = "";
                if (s_BootstrapSw != null)
                {
                    s_BootstrapSw.Stop();
                    ms = s_BootstrapSw.Elapsed.TotalMilliseconds;
                    s_BootstrapSw = null;
                    AddPhaseMs("bootstrap_plugin", ms);
                }
            }
            try
            {
                LogUtil.Log(LogTag + " LoadBootstrapPlugin ms=" + ms.ToString("0")
                    + " url=" + url
                    + " ex=" + (ex != null ? ex.GetType().Name : "none"));
            }
            catch { }
        }

        public static void TryFlushSummary(string context)
        {
            TryFlushSummary(context, false);
        }

        public static void TryFlushSummary(string context, bool force)
        {
            if (!CachedEnabled) return;
            lock (s_Lock)
            {
                if (s_SummaryFlushed && !force) return;
                if (!force) s_SummaryFlushed = true;
            }

            try
            {
                var sb = new StringBuilder(768);
                sb.Append(LogTag);
                sb.Append(" SUMMARY context=");
                sb.Append(string.IsNullOrEmpty(context) ? "unknown" : context);
                sb.Append(" t=");
                sb.Append(LogUtil.GetSecondsSinceProcessStart().ToString("0.000"));
                sb.Append("s");

                lock (s_Lock)
                {
                    if (s_PhasesMs.Count > 0)
                    {
                        sb.Append(" | phases=");
                        bool first = true;
                        foreach (var kv in s_PhasesMs)
                        {
                            if (!first) sb.Append(';');
                            first = false;
                            sb.Append(kv.Key);
                            sb.Append('=');
                            sb.Append(kv.Value.ToString("0"));
                            sb.Append("ms");
                        }
                    }

                    long vamXSkip = System.Threading.Interlocked.CompareExchange(ref s_SyncVamXSkippedCount, 0, 0);
                    if (vamXSkip > 0)
                    {
                        sb.Append(" | sync_vamx_skipped_calls=");
                        sb.Append(vamXSkip);
                    }

                    if (s_TopNativeScans.Count > 0)
                    {
                        sb.Append(" | top_native_scan=");
                        for (int i = 0; i < s_TopNativeScans.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(s_TopNativeScans[i].Key);
                            sb.Append('=');
                            sb.Append(s_TopNativeScans[i].Value.ToString("0"));
                            sb.Append("ms");
                        }
                    }

                    if (s_Timeline.Count > 0)
                    {
                        sb.Append(" | timeline=");
                        int start = Math.Max(0, s_Timeline.Count - 12);
                        for (int i = start; i < s_Timeline.Count; i++)
                        {
                            if (i > start) sb.Append(',');
                            sb.Append(s_Timeline[i]);
                        }
                    }
                }

                LogUtil.LogWarning(sb.ToString());
            }
            catch { }
        }

        internal static JSONClass ExportBenchSnapshot(string context)
        {
            var root = new JSONClass();
            root["context"] = context ?? "";
            root["processSeconds"] = LogUtil.GetSecondsSinceProcessStart().ToString("0.000");
            root["generatedAt"] = DateTime.Now.ToString("o");

            lock (s_Lock)
            {
                if (s_PhasesMs.Count > 0)
                {
                    var phases = new JSONClass();
                    foreach (var kv in s_PhasesMs)
                        phases[kv.Key] = kv.Value.ToString("0.00");
                    root["phasesMs"] = phases;
                }

                if (s_Timeline.Count > 0)
                {
                    var timeline = new JSONArray();
                    for (int i = 0; i < s_Timeline.Count; i++)
                        timeline.Add(s_Timeline[i]);
                    root["timeline"] = timeline;
                }

                root["nativeScanCount"] = s_NativeScanCount.ToString();
                root["nativeScanTotalMs"] = s_NativeScanTotalMs.ToString("0.00");
                if (s_VpbRefreshReason != null && s_VpbRefreshReason.Length > 0)
                    root["vpbRefreshReason"] = s_VpbRefreshReason;
            }

            return root;
        }
    }
}
