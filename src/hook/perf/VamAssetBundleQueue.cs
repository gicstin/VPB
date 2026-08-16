using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MeshVR;
using UnityEngine;

namespace VPB
{
    internal static class VamAssetBundleQueue
    {
        internal static bool Enabled = true;
        internal static int MaxConcurrent = 3;

        internal static long Served;
        internal static long Deduped;
        internal static long AlreadyLoaded;
        internal static long Started;
        internal static long Completed;
        internal static long Failures;

        sealed class Entry
        {
            public string Path;
            public List<AssetLoader.AssetBundleFromFileRequest> Waiters;
        }

        static readonly Dictionary<string, Entry> s_InFlight = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly List<string> s_Pending = new List<string>();
        static readonly Dictionary<string, int> s_PackageBusy = new Dictionary<string, int>(StringComparer.Ordinal);
        static int s_Running;

        static FieldInfo s_SingletonField;
        static FieldInfo s_RefCountsField;
        static FieldInfo s_PathToBundleField;
        static bool s_ReflectionReady;

        public static void PatchAll(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                if (!EnsureReflection())
                {
                    LogUtil.LogWarning("[VPB.Perf] AssetLoader internals not found; parallel bundle queue disabled");
                    Enabled = false;
                    return;
                }
                var m = AccessTools.Method(typeof(AssetLoader), "QueueLoadAssetBundleFromFile",
                    new Type[] { typeof(AssetLoader.AssetBundleFromFileRequest) });
                if (m == null)
                {
                    LogUtil.LogWarning("[VPB.Perf] AssetLoader.QueueLoadAssetBundleFromFile not found; parallel bundle queue disabled");
                    Enabled = false;
                    return;
                }
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(VamAssetBundleQueue), nameof(PreQueueLoadAssetBundleFromFile)));
            }
            catch (Exception ex)
            {
                Enabled = false;
                LogUtil.LogWarning("[VPB.Perf] parallel bundle queue patch failed: " + ex.Message);
            }
        }

        static bool EnsureReflection()
        {
            if (s_ReflectionReady) return true;
            s_SingletonField = AccessTools.Field(typeof(AssetLoader), "singleton");
            s_RefCountsField = AccessTools.Field(typeof(AssetLoader), "assetBundleReferenceCounts");
            s_PathToBundleField = AccessTools.Field(typeof(AssetLoader), "pathToAssetBundle");
            s_ReflectionReady = s_SingletonField != null && s_RefCountsField != null && s_PathToBundleField != null;
            return s_ReflectionReady;
        }

        static AssetLoader Singleton()
        {
            return s_SingletonField != null ? s_SingletonField.GetValue(null) as AssetLoader : null;
        }

        static Dictionary<string, int> RefCounts(AssetLoader loader)
        {
            var d = s_RefCountsField.GetValue(loader) as Dictionary<string, int>;
            if (d == null)
            {
                d = new Dictionary<string, int>();
                s_RefCountsField.SetValue(loader, d);
            }
            return d;
        }

        static Dictionary<string, AssetBundle> PathToBundle(AssetLoader loader)
        {
            var d = s_PathToBundleField.GetValue(loader) as Dictionary<string, AssetBundle>;
            if (d == null)
            {
                d = new Dictionary<string, AssetBundle>();
                s_PathToBundleField.SetValue(loader, d);
            }
            return d;
        }

        static void AddRef(AssetLoader loader, string path)
        {
            var counts = RefCounts(loader);
            int n;
            counts[path] = counts.TryGetValue(path, out n) ? n + 1 : 1;
        }

        public static bool PreQueueLoadAssetBundleFromFile(AssetLoader.AssetBundleFromFileRequest abffr)
        {
            if (!Enabled) return true;
            if (abffr == null || string.IsNullOrEmpty(abffr.path)) return true;

            AssetLoader loader = Singleton();
            if (loader == null) return true;

            try
            {
                string path = abffr.path;
                AddRef(loader, path);
                Served++;

                AssetBundle existing;
                if (PathToBundle(loader).TryGetValue(path, out existing))
                {
                    AlreadyLoaded++;
                    abffr.assetBundle = existing;
                    if (abffr.callback != null) abffr.callback(abffr);
                    return false;
                }

                Entry e;
                if (s_InFlight.TryGetValue(path, out e))
                {
                    Deduped++;
                    e.Waiters.Add(abffr);
                    return false;
                }

                e = new Entry
                {
                    Path = path,
                    Waiters = new List<AssetLoader.AssetBundleFromFileRequest>(2) { abffr }
                };
                s_InFlight[path] = e;
                s_Pending.Add(path);
                Pump(loader);
                return false;
            }
            catch (Exception ex)
            {
                Failures++;
                LogUtil.LogWarning("[VPB.Perf] bundle queue fell back to VaM loader: " + ex.Message);
                return true;
            }
        }

        static void Pump(AssetLoader loader)
        {
            while (s_Running < MaxConcurrent && s_Pending.Count > 0)
            {
                string next = null;
                int nextIndex = -1;
                for (int i = 0; i < s_Pending.Count; i++)
                {
                    string candidate = s_Pending[i];
                    if (IsPackageBusy(candidate)) continue;
                    next = candidate;
                    nextIndex = i;
                    break;
                }
                if (next == null) return;

                s_Pending.RemoveAt(nextIndex);
                s_Running++;
                MarkPackageBusy(next, 1);
                Started++;
                loader.StartCoroutine(LoadOne(loader, next));
            }
        }

        static string PackageKeyOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int i = path.IndexOf(":/", StringComparison.Ordinal);
            return i > 0 ? path.Substring(0, i) : string.Empty;
        }

        static bool IsPackageBusy(string path)
        {
            string key = PackageKeyOf(path);
            if (key.Length == 0) return false;
            int n;
            return s_PackageBusy.TryGetValue(key, out n) && n > 0;
        }

        static void MarkPackageBusy(string path, int delta)
        {
            string key = PackageKeyOf(path);
            if (key.Length == 0) return;
            int n;
            s_PackageBusy.TryGetValue(key, out n);
            n += delta;
            if (n <= 0) s_PackageBusy.Remove(key);
            else s_PackageBusy[key] = n;
        }

        static IEnumerator LoadOne(AssetLoader loader, string path)
        {
            AssetBundle bundle = null;
            AssetBundleCreateRequest abcr = null;
            byte[] bytes = null;

            bool inPackage = false;
            try { inPackage = MVR.FileManagement.FileManager.IsFileInPackage(path); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] IsFileInPackage failed for " + path + ": " + ex.Message); }

            if (inPackage)
            {
                MVR.FileManagement.VarFileEntry vfe = null;
                try { vfe = MVR.FileManagement.FileManager.GetVarFileEntry(path); }
                catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] GetVarFileEntry failed for " + path + ": " + ex.Message); }

                if (vfe != null && vfe.Simulated)
                {
                    string onDisk = vfe.Package.Path + "\\" + vfe.InternalPath;
                    try { abcr = AssetBundle.LoadFromFileAsync(onDisk); }
                    catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] LoadFromFileAsync failed for " + onDisk + ": " + ex.Message); }
                }
                else if (vfe != null)
                {
                    bytes = new byte[vfe.Size];
                    yield return MVR.FileManagement.FileManager.ReadAllBytesCoroutine(vfe, bytes);
                    try { abcr = AssetBundle.LoadFromMemoryAsync(bytes); }
                    catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] LoadFromMemoryAsync failed for " + path + ": " + ex.Message); }
                }
            }
            else
            {
                try { abcr = AssetBundle.LoadFromFileAsync(path); }
                catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] LoadFromFileAsync failed for " + path + ": " + ex.Message); }
            }

            if (abcr != null)
            {
                yield return abcr;
                try { bundle = abcr.assetBundle; }
                catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] assetBundle fetch failed for " + path + ": " + ex.Message); }
            }

            Finish(loader, path, bundle);
        }

        static void Finish(AssetLoader loader, string path, AssetBundle bundle)
        {
            Entry e = null;
            try
            {
                s_InFlight.TryGetValue(path, out e);
                s_InFlight.Remove(path);

                if (bundle == null)
                {
                    Failures++;
                    SuperController.LogError("Error during attempt to load assetbundle " + path + ". Not valid");
                }
                else
                {
                    var map = PathToBundle(loader);
                    if (!map.ContainsKey(path)) map.Add(path, bundle);
                    else bundle = map[path];
                    Completed++;
                }

                if (e != null)
                {
                    for (int i = 0; i < e.Waiters.Count; i++)
                    {
                        var w = e.Waiters[i];
                        if (w == null) continue;
                        w.assetBundle = bundle;
                        try { if (w.callback != null) w.callback(w); }
                        catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] bundle callback threw for " + path + ": " + ex.Message); }
                    }
                }
            }
            catch (Exception ex)
            {
                Failures++;
                LogUtil.LogWarning("[VPB.Perf] bundle finish failed for " + path + ": " + ex.Message);
            }
            finally
            {
                s_Running--;
                if (s_Running < 0) s_Running = 0;
                MarkPackageBusy(path, -1);
                try { Pump(loader); }
                catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] bundle pump failed: " + ex.Message); }
            }
        }

        public static string Status()
        {
            return "[VPB.Perf] bundleQueue served=" + Served
                + " started=" + Started
                + " dedup=" + Deduped
                + " cached=" + AlreadyLoaded
                + " done=" + Completed
                + " fail=" + Failures
                + " maxConcurrent=" + MaxConcurrent;
        }
    }
}
