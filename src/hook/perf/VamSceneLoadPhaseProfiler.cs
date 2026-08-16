using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace VPB
{
    internal static class VamSceneLoadPhaseProfiler
    {
        internal static bool Enabled = true;
        internal static int SlowFrameMs = 250;

        const int MaxSlowFrames = 12;
        const int MaxPhases = 32;

        internal enum HookBucket
        {
            FileExists = 0,
            OpenStream = 1,
            GetFileEntry = 2,
            UnloadUnusedAssets = 3,
            BundleCreate = 4,
            BundleFetch = 5,
            BundleUnload = 6,
            UnitySceneAsync = 7,
            BundleNames = 8,
            BundleLoadAsset = 9,
            Count = 10
        }

        sealed class Phase
        {
            public string Name;
            public double Ms;
            public int Enters;
            public double MaxMs;
        }

        sealed class SlowFrame
        {
            public string Phase;
            public double Ms;
        }

        static readonly Stopwatch s_Clock = new Stopwatch();
        static readonly List<Phase> s_Order = new List<Phase>(MaxPhases);
        static readonly Dictionary<string, Phase> s_Phases = new Dictionary<string, Phase>(StringComparer.Ordinal);
        static readonly List<SlowFrame> s_SlowFrames = new List<SlowFrame>(MaxSlowFrames);
        static readonly long[] s_HookTicks = new long[(int)HookBucket.Count];
        static readonly long[] s_HookCalls = new long[(int)HookBucket.Count];

        static bool s_Active;
        static bool s_WasLoading;
        static string s_CurrentPhase;
        static double s_PhaseStartMs;
        static double s_LoadStartMs;
        static double s_LastTickMs;

        public static void PatchAll(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                var m = AccessTools.Method(typeof(SuperController), "UpdateLoadingStatus", new Type[] { typeof(string) });
                if (m == null)
                {
                    LogUtil.LogWarning("[VPB.Perf] SuperController.UpdateLoadingStatus not found; phase profiling disabled");
                    return;
                }
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(VamSceneLoadPhaseProfiler), nameof(PreUpdateLoadingStatus)));
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] phase profiler patch failed: " + ex.Message);
            }

            Time(harmony, typeof(Resources), "UnloadUnusedAssets", Type.EmptyTypes, nameof(PostUnloadUnusedAssets));
            Time(harmony, typeof(AssetBundle), "LoadFromMemoryAsync", new Type[] { typeof(byte[]), typeof(uint) }, nameof(PostBundleCreate));
            Time(harmony, typeof(AssetBundle), "LoadFromFileAsync", new Type[] { typeof(string), typeof(uint), typeof(ulong) }, nameof(PostBundleCreate));
            Time(harmony, typeof(AssetBundle), "Unload", new Type[] { typeof(bool) }, nameof(PostBundleUnload));
            Time(harmony, typeof(UnityEngine.SceneManagement.SceneManager), "LoadSceneAsync",
                new Type[] { typeof(string), typeof(UnityEngine.SceneManagement.LoadSceneMode) }, nameof(PostUnitySceneAsync));
            TimeGetter(harmony, typeof(AssetBundleCreateRequest), "assetBundle", nameof(PostBundleFetch));
            Time(harmony, typeof(AssetBundle), "LoadAsset", new Type[] { typeof(string), typeof(Type) }, nameof(PostBundleLoadAsset));
        }

        static void Time(Harmony harmony, Type owner, string name, Type[] args, string postfixName)
        {
            try
            {
                var m = AccessTools.Method(owner, name, args);
                if (m == null) return;
                harmony.Patch(m,
                    prefix: new HarmonyMethod(typeof(VamSceneLoadPhaseProfiler), nameof(PreTimed)),
                    postfix: new HarmonyMethod(typeof(VamSceneLoadPhaseProfiler), postfixName));
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] timing patch " + owner.Name + "." + name + " failed: " + ex.Message);
            }
        }

        static void TimeGetter(Harmony harmony, Type owner, string property, string postfixName)
        {
            try
            {
                var p = AccessTools.Property(owner, property);
                var m = p != null ? p.GetGetMethod(true) : null;
                if (m == null) return;
                harmony.Patch(m,
                    prefix: new HarmonyMethod(typeof(VamSceneLoadPhaseProfiler), nameof(PreTimed)),
                    postfix: new HarmonyMethod(typeof(VamSceneLoadPhaseProfiler), postfixName));
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] timing patch " + owner.Name + "." + property + " failed: " + ex.Message);
            }
        }

        public static void PreTimed(out long __state)
        {
            __state = s_Active ? Stopwatch.GetTimestamp() : 0L;
        }

        static void StopTimer(long state, HookBucket bucket)
        {
            if (state != 0L) AddHookCost(bucket, Stopwatch.GetTimestamp() - state);
        }

        public static void PostUnloadUnusedAssets(long __state) { StopTimer(__state, HookBucket.UnloadUnusedAssets); }
        public static void PostBundleCreate(long __state) { StopTimer(__state, HookBucket.BundleCreate); }
        public static void PostBundleFetch(long __state) { StopTimer(__state, HookBucket.BundleFetch); }
        public static void PostBundleUnload(long __state) { StopTimer(__state, HookBucket.BundleUnload); }
        public static void PostUnitySceneAsync(long __state) { StopTimer(__state, HookBucket.UnitySceneAsync); }
        public static void PostBundleNames(long __state) { StopTimer(__state, HookBucket.BundleNames); }
        public static void PostBundleLoadAsset(long __state) { StopTimer(__state, HookBucket.BundleLoadAsset); }

        public static void AddHookCost(HookBucket bucket, long elapsedTicks)
        {
            if (!s_Active) return;
            int i = (int)bucket;
            s_HookTicks[i] += elapsedTicks;
            s_HookCalls[i]++;
        }

        public static bool Active { get { return s_Active; } }

        public static void PreUpdateLoadingStatus(string txt)
        {
            if (!Enabled) return;
            try { BeginPhase(Normalize(txt)); }
            catch { }
        }

        static string Normalize(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return "(none)";
            if (txt.StartsWith("Loading Atom ", StringComparison.Ordinal)) return "Loading Atoms";
            if (txt.StartsWith("Restoring atom ", StringComparison.Ordinal)) return "Restoring Atoms";
            if (txt.StartsWith("Restoring atom contents", StringComparison.Ordinal)) return "Restore Prep";
            if (txt.StartsWith("Waiting for async load", StringComparison.Ordinal)) return "Waiting Async Load";
            return txt.Length > 48 ? txt.Substring(0, 48) : txt;
        }

        static void Start()
        {
            s_Clock.Reset();
            s_Clock.Start();
            s_Order.Clear();
            s_Phases.Clear();
            s_SlowFrames.Clear();
            for (int i = 0; i < s_HookTicks.Length; i++) { s_HookTicks[i] = 0; s_HookCalls[i] = 0; }
            s_LoadStartMs = 0.0;
            s_PhaseStartMs = 0.0;
            s_LastTickMs = 0.0;
            s_CurrentPhase = "Clear + Unload";
            s_Active = true;
            VamFrameAttributionProfiler.BeginLoad();
        }

        static void BeginPhase(string name)
        {
            if (!s_Active) return;
            double now = s_Clock.Elapsed.TotalMilliseconds;
            Close(now);
            s_CurrentPhase = name;
            s_PhaseStartMs = now;
        }

        static void Close(double now)
        {
            if (s_CurrentPhase == null) return;
            double ms = now - s_PhaseStartMs;
            Phase p;
            if (!s_Phases.TryGetValue(s_CurrentPhase, out p))
            {
                if (s_Order.Count >= MaxPhases) return;
                p = new Phase { Name = s_CurrentPhase };
                s_Phases[s_CurrentPhase] = p;
                s_Order.Add(p);
            }
            p.Ms += ms;
            p.Enters++;
            if (ms > p.MaxMs) p.MaxMs = ms;
        }

        public static void Tick()
        {
            if (!Enabled) return;
            var sc = SuperController.singleton;
            bool loading = sc != null && sc.isLoading;

            if (loading && !s_WasLoading) Start();

            if (s_Active) VamFrameAttributionProfiler.Tick();

            if (s_Active)
            {
                double now = s_Clock.Elapsed.TotalMilliseconds;
                double frameMs = now - s_LastTickMs;
                s_LastTickMs = now;
                if (frameMs >= SlowFrameMs && s_SlowFrames.Count < MaxSlowFrames)
                    s_SlowFrames.Add(new SlowFrame { Phase = s_CurrentPhase ?? "(none)", Ms = frameMs });
            }

            if (!loading && s_WasLoading) Emit();
            s_WasLoading = loading;
        }

        static void Emit()
        {
            if (!s_Active) return;
            try
            {
                double now = s_Clock.Elapsed.TotalMilliseconds;
                Close(now);
                s_Active = false;
                s_Clock.Stop();

                var sb = new StringBuilder(512);
                sb.Append("[VPB.Perf] SCENE_LOAD_PROFILE total=");
                sb.Append((now - s_LoadStartMs).ToString("0"));
                sb.Append("ms | phases=");
                for (int i = 0; i < s_Order.Count; i++)
                {
                    Phase p = s_Order[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(p.Name);
                    sb.Append('=');
                    sb.Append(p.Ms.ToString("0"));
                    sb.Append("ms");
                    if (p.Enters > 1)
                    {
                        sb.Append("/x");
                        sb.Append(p.Enters);
                    }
                }

                sb.Append(" | vpbHooks=");
                AppendHook(sb, "fileExists", HookBucket.FileExists);
                sb.Append(' ');
                AppendHook(sb, "openStream", HookBucket.OpenStream);
                sb.Append(' ');
                AppendHook(sb, "getFileEntry", HookBucket.GetFileEntry);
                sb.Append(" | unity=");
                AppendHook(sb, "unloadUnused", HookBucket.UnloadUnusedAssets);
                sb.Append(' ');
                AppendHook(sb, "bundleCreate", HookBucket.BundleCreate);
                sb.Append(' ');
                AppendHook(sb, "bundleFetch", HookBucket.BundleFetch);
                sb.Append(' ');
                AppendHook(sb, "bundleUnload", HookBucket.BundleUnload);
                sb.Append(' ');
                AppendHook(sb, "sceneAsync", HookBucket.UnitySceneAsync);
                sb.Append(' ');
                AppendHook(sb, "bundleNames", HookBucket.BundleNames);
                sb.Append(' ');
                AppendHook(sb, "bundleLoadAsset", HookBucket.BundleLoadAsset);

                if (s_SlowFrames.Count > 0)
                {
                    sb.Append(" | slowFrames=");
                    for (int i = 0; i < s_SlowFrames.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(s_SlowFrames[i].Ms.ToString("0"));
                        sb.Append("ms@");
                        sb.Append(s_SlowFrames[i].Phase);
                    }
                }

                LogUtil.LogWarning(sb.ToString());
                LogUtil.LogWarning(VamAssetBundleQueue.Status());
                VamFrameAttributionProfiler.EndLoad();
            }
            catch (Exception ex)
            {
                s_Active = false;
                LogUtil.LogWarning("[VPB.Perf] phase profile emit failed: " + ex.Message);
            }
        }

        static void AppendHook(StringBuilder sb, string label, HookBucket bucket)
        {
            int i = (int)bucket;
            double ms = (double)s_HookTicks[i] * 1000.0 / Stopwatch.Frequency;
            sb.Append(label);
            sb.Append('=');
            sb.Append(ms.ToString("0"));
            sb.Append("ms/");
            sb.Append(s_HookCalls[i]);
        }
    }
}
