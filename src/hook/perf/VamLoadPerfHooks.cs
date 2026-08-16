using System;
using HarmonyLib;

namespace VPB
{
    internal static class VamLoadPerfHooks
    {
        static bool s_Applied;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || s_Applied) return;
            s_Applied = true;

            ApplySettings();
            VamAssetBundleQueue.PatchAll(harmony);
            VamSceneLoadPhaseProfiler.PatchAll(harmony);
            VamFrameAttributionProfiler.Apply(harmony);
        }

        public static void ApplySettings()
        {
            var s = Settings.Instance;
            if (s == null) return;
            try
            {
                if (s.LoadParallelAssetBundles != null)
                    VamAssetBundleQueue.Enabled = s.LoadParallelAssetBundles.Value;
                if (s.LoadParallelAssetBundleWorkers != null)
                    VamAssetBundleQueue.MaxConcurrent = Math.Max(1, Math.Min(16, s.LoadParallelAssetBundleWorkers.Value));
                if (s.LoadAttributeSlowFrames != null)
                    VamFrameAttributionProfiler.Enabled = s.LoadAttributeSlowFrames.Value;
                if (s.LoadProfileScenePhases != null)
                    VamSceneLoadPhaseProfiler.Enabled = s.LoadProfileScenePhases.Value;
                if (s.LoadProfileSlowFrameMs != null)
                    VamSceneLoadPhaseProfiler.SlowFrameMs = Math.Max(50, s.LoadProfileSlowFrameMs.Value);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] load perf settings apply failed: " + ex.Message);
            }
        }

        public static void Tick()
        {
            VamSceneLoadPhaseProfiler.Tick();
        }

    }
}
