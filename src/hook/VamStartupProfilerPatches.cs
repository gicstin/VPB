using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using AssetBundles;
using HarmonyLib;
using MeshVR;

namespace VPB
{
    /// <summary>Harmony timing hooks for VaM cold-start hotspots (see VamStartupProfiler).</summary>
    internal static class VamStartupProfilerPatches
    {
        // Must use MVR.FileManagement.* — namespace VPB also defines FileManager/VarPackage with different Refresh overloads.

        static int s_LateOptionalPatchesAttempted;
        static int s_PerformancePatchProfilerPatched;
        static readonly HashSet<string> s_PatchedOptionalMethods = new HashSet<string>(StringComparer.Ordinal);

        public static void TryApplyLateOptionalPatches(Harmony harmony)
        {
            TryApplyLateOptionalPatches(harmony, false);
        }

        public static void TryApplyLateOptionalPatchesRetry(Harmony harmony)
        {
            TryApplyLateOptionalPatches(harmony, true);
        }

        static void TryApplyLateOptionalPatches(Harmony harmony, bool isRetry)
        {
            if (harmony == null) return;
            if (!isRetry)
            {
                if (System.Threading.Interlocked.CompareExchange(ref s_LateOptionalPatchesAttempted, 1, 0) != 0)
                    return;
            }
            else if (s_PerformancePatchProfilerPatched != 0)
            {
                return;
            }

            try
            {
                LogUtil.Log("[VPB.Startup.Timing] applying late optional VaM profiler patches"
                    + (isRetry ? " (PerformancePatch retry)" : ""));
            }
            catch { }
            if (isRetry)
                ApplyPerformancePatchOptional(harmony);
            else
                ApplyOptionalPatches(harmony);
            if (!isRetry)
                TryPatchSceneLoad(harmony);
        }

        static void TryPatchSceneLoad(Harmony harmony)
        {
            try
            {
                var load = AccessTools.Method(typeof(UnityEngine.SceneManagement.SceneManager), "LoadScene",
                    new[] { typeof(string), typeof(UnityEngine.SceneManagement.LoadSceneMode) });
                if (load != null)
                {
                    var pre = typeof(VamStartupProfilerPatches).GetMethod(nameof(PreSceneLoad), BindingFlags.Static | BindingFlags.NonPublic);
                    var post = typeof(VamStartupProfilerPatches).GetMethod(nameof(PostSceneLoad), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(load, prefix: pre != null ? new HarmonyMethod(pre) : null, postfix: post != null ? new HarmonyMethod(post) : null);
                }
            }
            catch { }
        }

        static void PreSceneLoad(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            VamStartupProfiler.Milestone("Unity.LoadScene_begin name=" + (sceneName ?? "") + " mode=" + mode);
        }

        static void PostSceneLoad(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            VamStartupProfiler.Milestone("Unity.LoadScene_end name=" + (sceneName ?? "") + " mode=" + mode);
        }

        public static void ApplyOptionalPatches(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                PatchTypeMethod(harmony, typeof(AssetBundleManager), "Initialize", null,
                    nameof(PreAssetBundleManagerInitialize), nameof(PostAssetBundleManagerInitialize));
                PatchTypeMethod(harmony, typeof(AssetBundleManager), "Initialize",
                    new[] { typeof(string) },
                    nameof(PreAssetBundleManagerInitialize), nameof(PostAssetBundleManagerInitialize));
                PatchTypeMethod(harmony, typeof(AssetLoader), "Awake", null,
                    nameof(PreMeshVRAssetLoaderAwake), nameof(PostMeshVRAssetLoaderAwake));
                PatchTypeMethod(harmony, typeof(AssetLoader), "Start", null,
                    nameof(PreMeshVRAssetLoaderStart), nameof(PostMeshVRAssetLoaderStart));
                PatchOptionalRuntimeType(harmony, "PerformancePatch", "Awake",
                    nameof(PrePerformancePatchAwake), nameof(PostPerformancePatchAwake));
                PatchOptionalRuntimeType(harmony, "PerformancePatch", "Start",
                    nameof(PrePerformancePatchStart), nameof(PostPerformancePatchStart));
            }
            catch { }
        }

        static void ApplyPerformancePatchOptional(Harmony harmony)
        {
            if (harmony == null || s_PerformancePatchProfilerPatched != 0) return;
            try
            {
                PatchOptionalRuntimeType(harmony, "PerformancePatch", "Awake",
                    nameof(PrePerformancePatchAwake), nameof(PostPerformancePatchAwake));
                PatchOptionalRuntimeType(harmony, "PerformancePatch", "Start",
                    nameof(PrePerformancePatchStart), nameof(PostPerformancePatchStart));
            }
            catch { }
        }

        static void PatchOptionalRuntimeType(Harmony harmony, string typeName, string methodName,
            string prefixName, string postfixName)
        {
            Type t = ResolveStartupType(typeName, silent: true);
            if (t == null) return;
            PatchTypeMethod(harmony, t, methodName, null, prefixName, postfixName);
        }

        static Type ResolveStartupType(string typeName, bool silent)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            string simple = typeName;
            int dot = typeName.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < typeName.Length)
                simple = typeName.Substring(dot + 1);

            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < asms.Length; a++)
                {
                    Assembly asm = asms[a];
                    if (asm == null) continue;
                    try
                    {
                        Type[] types = asm.GetTypes();
                        for (int i = 0; i < types.Length; i++)
                        {
                            Type cand = types[i];
                            if (cand == null) continue;
                            if (string.Equals(cand.Name, simple, StringComparison.Ordinal)
                                || string.Equals(cand.FullName, typeName, StringComparison.Ordinal))
                            {
                                return cand;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (silent) return null;
            try { return AccessTools.TypeByName(typeName); }
            catch { return null; }
        }

        static void PatchTypeMethod(Harmony harmony, string typeName, string methodName, string prefixName, string postfixName)
        {
            Type t = ResolveStartupType(typeName, silent: false);
            if (t == null) return;
            PatchTypeMethod(harmony, t, methodName, null, prefixName, postfixName);
        }

        static void PatchTypeMethod(Harmony harmony, Type t, string methodName, Type[] argTypes,
            string prefixName, string postfixName)
        {
            if (t == null || harmony == null) return;
            try
            {
                MethodInfo target = argTypes == null
                    ? AccessTools.Method(t, methodName)
                    : AccessTools.Method(t, methodName, argTypes);
                if (target == null) return;
                string patchKey = t.FullName + "." + target.Name
                    + "(" + (argTypes == null ? 0 : argTypes.Length) + ")";
                lock (s_PatchedOptionalMethods)
                {
                    if (s_PatchedOptionalMethods.Contains(patchKey))
                        return;
                }
                MethodInfo pre = typeof(VamStartupProfilerPatches).GetMethod(prefixName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                MethodInfo post = typeof(VamStartupProfilerPatches).GetMethod(postfixName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (pre == null && post == null) return;
                harmony.Patch(target,
                    prefix: pre != null ? new HarmonyMethod(pre) : null,
                    postfix: post != null ? new HarmonyMethod(post) : null);
                lock (s_PatchedOptionalMethods)
                {
                    s_PatchedOptionalMethods.Add(patchKey);
                }
                if (string.Equals(t.Name, "PerformancePatch", StringComparison.Ordinal))
                    System.Threading.Interlocked.Exchange(ref s_PerformancePatchProfilerPatched, 1);
                try
                {
                    LogUtil.Log("[VPB.Startup.Timing] patched " + t.FullName + "." + methodName
                        + " (" + asmName(t) + ")");
                }
                catch { }
            }
            catch (Exception ex)
            {
                try
                {
                    LogUtil.LogWarning("[VPB] Startup profiler optional patch skipped "
                        + (t != null ? t.FullName : "?") + "." + methodName + ": " + ex.Message);
                }
                catch { }
            }
        }

        static string asmName(Type t)
        {
            try { return t != null && t.Assembly != null ? t.Assembly.GetName().Name : ""; }
            catch { return ""; }
        }

        /// <summary>Apply profiler patches without aborting VPB Awake if one patch fails.</summary>
        public static void ApplySafe(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                harmony.PatchAll(typeof(VamStartupProfilerPatches));
            }
            catch (Exception ex)
            {
                try
                {
                    LogUtil.LogError("[VPB] Startup profiler PatchAll failed (trying per-patch): " + ex.Message);
                }
                catch { }
                ApplyCorePatchesManually(harmony);
            }
            ApplyOptionalPatches(harmony);
        }

        static void ApplyCorePatchesManually(Harmony harmony)
        {
            PatchMethod(harmony, AccessTools.Method(typeof(MVR.FileManagement.FileManager), "Refresh"),
                nameof(PreNativeRefresh), null, nameof(FinalizeNativeRefresh));
            PatchMethod(harmony, AccessTools.Method(typeof(SuperController), "SyncToKeyFile", new[] { typeof(bool) }),
                nameof(PreSyncToKeyFile), nameof(PostSyncToKeyFile), null);
            PatchMethod(harmony, AccessTools.Method(typeof(SuperController), "SyncVamX"),
                nameof(PreSyncVamX), nameof(PostSyncVamX), nameof(FinalizeSyncVamX));
        }

        static void PatchMethod(Harmony harmony, MethodInfo target, string prefix, string postfix, string finalizer)
        {
            if (target == null || harmony == null) return;
            try
            {
                HarmonyMethod pre = string.IsNullOrEmpty(prefix) ? null
                    : new HarmonyMethod(typeof(VamStartupProfilerPatches).GetMethod(prefix, BindingFlags.Static | BindingFlags.Public));
                HarmonyMethod post = string.IsNullOrEmpty(postfix) ? null
                    : new HarmonyMethod(typeof(VamStartupProfilerPatches).GetMethod(postfix, BindingFlags.Static | BindingFlags.Public));
                HarmonyMethod fin = string.IsNullOrEmpty(finalizer) ? null
                    : new HarmonyMethod(typeof(VamStartupProfilerPatches).GetMethod(finalizer, BindingFlags.Static | BindingFlags.Public));
                harmony.Patch(target, prefix: pre, postfix: post, finalizer: fin);
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] Startup profiler patch skipped " + target.Name + ": " + ex.Message); }
                catch { }
            }
        }

        public static void PrePerformancePatchAwake() { VamStartupProfiler.Milestone("PerformancePatch.Awake_begin"); }
        public static void PostPerformancePatchAwake() { VamStartupProfiler.Milestone("PerformancePatch.Awake_end"); }
        public static void PrePerformancePatchStart() { VamStartupProfiler.Milestone("PerformancePatch.Start_begin"); }
        public static void PostPerformancePatchStart() { VamStartupProfiler.Milestone("PerformancePatch.Start_end"); }
        public static void PreAssetBundleManagerInitialize() { VamStartupProfiler.BeginScope("AssetBundleManager.Initialize"); }
        public static void PostAssetBundleManagerInitialize() { VamStartupProfiler.EndScope("AssetBundleManager.Initialize"); }
        public static void PreMeshVRAssetLoaderAwake() { VamStartupProfiler.Milestone("MeshVR.AssetLoader.Awake_begin"); }
        public static void PostMeshVRAssetLoaderAwake() { VamStartupProfiler.Milestone("MeshVR.AssetLoader.Awake_end"); }
        public static void PreMeshVRAssetLoaderStart() { VamStartupProfiler.Milestone("MeshVR.AssetLoader.Start_begin"); }
        public static void PostMeshVRAssetLoaderStart() { VamStartupProfiler.Milestone("MeshVR.AssetLoader.Start_end"); }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "Awake")]
        public static void PreSuperControllerAwake()
        {
            VamStartupProfiler.LogPreSuperControllerAwaitIfAny();
            VamStartupProfiler.Milestone("SuperController.Awake_begin");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "Awake")]
        public static void PostSuperControllerAwake()
        {
            VamStartupProfiler.Milestone("SuperController.Awake_end");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "SyncToKeyFile", new Type[] { typeof(bool) })]
        public static void PreSyncToKeyFile(bool userInvoked)
        {
            VamStartupProfiler.Milestone("SyncToKeyFile_begin userInvoked=" + (userInvoked ? "1" : "0"));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PreNativeRefresh()
        {
            VamStartupProfiler.Milestone("native_FileManager.Refresh_begin");
            VamStartupProfiler.BeginNativeRefresh();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static Exception FinalizeNativeRefresh(Exception __exception)
        {
            VamStartupProfiler.EndNativeRefresh(
                VamScanFilter.ScanAllowedCount,
                VamScanFilter.ScanBlockedCount,
                __exception);
            return __exception;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "RegisterPackage", new Type[] { typeof(string) })]
        public static void PostNativeRegisterPackage(string __0, MVR.FileManagement.VarPackage __result)
        {
            if (__result != null)
                VamStartupProfiler.RecordNativeRegister();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "Scan")]
        public static void PreVarPackageScan(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "Scan")]
        public static void PostVarPackageScan(MVR.FileManagement.VarPackage __instance, long __state)
        {
            try
            {
                long elapsed = Stopwatch.GetTimestamp() - __state;
                double ms = elapsed * 1000.0 / Stopwatch.Frequency;
                string uid = null;
                try { uid = __instance.Uid; } catch { }
                if (string.IsNullOrEmpty(uid))
                {
                    try { uid = __instance.Path; } catch { }
                }
                VamStartupProfiler.RecordNativePackageScan(uid, ms);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "SyncToKeyFile", new Type[] { typeof(bool) })]
        public static void PostSyncToKeyFile()
        {
            VamStartupProfiler.Milestone("SyncToKeyFile_done");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "SyncVamX")]
        public static bool PreSyncVamX(SuperController __instance)
        {
            // Always run VaM SyncVamX for main-menu vamX/Create tiles. Skipping it (fast-absent / redundant-skip)
            // left duplicate "Create" entries: ApplyVamXUiAbsent enabled every vamXDisabled* array at once.
            VamStartupProfiler.BeginSyncVamX();
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "SyncVamX")]
        public static void PostSyncVamX(SuperController __instance)
        {
            VamStartupOptimizations.NoteSyncVamXCompleted(VamStartupOptimizations.GetVamXInstalledForProfiler(__instance));
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(SuperController), "SyncVamX")]
        public static Exception FinalizeSyncVamX(Exception __exception)
        {
            VamStartupProfiler.EndSyncVamX();
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "LoadBootstrapPlugin", new Type[] { typeof(string) })]
        public static void PreLoadBootstrapPlugin(string url)
        {
            VamStartupProfiler.BeginBootstrapPlugin(url);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(SuperController), "LoadBootstrapPlugin", new Type[] { typeof(string) })]
        public static Exception FinalizeLoadBootstrapPlugin(Exception __exception)
        {
            VamStartupProfiler.EndBootstrapPlugin(__exception);
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "Start")]
        public static void PreSuperControllerStart()
        {
            VamStartupProfiler.Milestone("SuperController.Start_begin");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "Start")]
        public static void PostSuperControllerStart()
        {
            VamStartupProfiler.Milestone("SuperController.Start_end");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "ActivateWorldUI")]
        public static void PostActivateWorldUIProfiler()
        {
            VamStartupProfiler.Milestone("SuperController.ActivateWorldUI_done");
            VamStartupProfiler.TryFlushSummary("WorldUI.Activate");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "DeactivateWorldUI")]
        public static void PreDeactivateWorldUI()
        {
            VamStartupProfiler.Milestone("SuperController.DeactivateWorldUI_begin");
        }
    }
}
