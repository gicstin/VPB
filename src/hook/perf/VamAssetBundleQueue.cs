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
        internal static long Recovered;

        sealed class Delivery
        {
            public AssetLoader.AssetBundleFromFileRequest Request;
            public AssetBundle Bundle;
            public bool Ready;
        }

        sealed class Entry
        {
            public string Path;
            public List<Delivery> Waiters;
        }

        sealed class LoadResult
        {
            public AssetBundle Bundle;
        }

        static readonly Dictionary<string, Entry> s_InFlight = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly List<string> s_Pending = new List<string>();
        static readonly Dictionary<string, int> s_PackageBusy = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly List<Delivery> s_Delivery = new List<Delivery>();
        static int s_DeliveryHead;
        static bool s_Draining;
        static int s_Running;
        static bool s_DriverRunning;
        static float s_LastProgressRealtime;

        const float StallSeconds = 60f;
        const int DeliveryCompactThreshold = 64;

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

        static void ReleaseRef(AssetLoader loader, string path)
        {
            try
            {
                var counts = RefCounts(loader);
                int n;
                if (!counts.TryGetValue(path, out n)) return;
                if (n <= 1) counts.Remove(path);
                else counts[path] = n - 1;
            }
            catch { }
        }

        public static bool PreQueueLoadAssetBundleFromFile(AssetLoader.AssetBundleFromFileRequest abffr)
        {
            if (!Enabled) return true;
            if (abffr == null || string.IsNullOrEmpty(abffr.path)) return true;

            AssetLoader loader = Singleton();
            if (loader == null) return true;

            string path = abffr.path;
            bool refAdded = false;
            Delivery created = null;

            try
            {
                AddRef(loader, path);
                refAdded = true;
                Served++;

                created = new Delivery { Request = abffr };
                s_Delivery.Add(created);
                EnsureDriver(loader);

                AssetBundle existing;
                if (PathToBundle(loader).TryGetValue(path, out existing))
                {
                    AlreadyLoaded++;
                    created.Bundle = existing;
                    created.Ready = true;
                    return false;
                }

                Entry e;
                if (s_InFlight.TryGetValue(path, out e))
                {
                    Deduped++;
                    e.Waiters.Add(created);
                    return false;
                }

                e = new Entry { Path = path, Waiters = new List<Delivery>(2) { created } };
                s_InFlight[path] = e;
                s_Pending.Add(path);
                MarkProgress();
                Pump(loader);
                return false;
            }
            catch (Exception ex)
            {
                Failures++;
                if (refAdded) ReleaseRef(loader, path);
                if (created != null)
                {
                    created.Request = null;
                    created.Ready = true;
                }
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
                MarkProgress();
                loader.StartCoroutine(RunGuarded(loader, next));
            }
        }

        static string PackageKeyOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int i = path.IndexOf(":/", StringComparison.Ordinal);
            if (i <= 0) i = path.IndexOf(":\\", StringComparison.Ordinal);
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

        static void MarkProgress()
        {
            s_LastProgressRealtime = Time.realtimeSinceStartup;
        }

        static IEnumerator RunGuarded(AssetLoader loader, string path)
        {
            LoadResult result = new LoadResult();
            List<IEnumerator> stack = new List<IEnumerator>(2);
            stack.Add(LoadOne(path, result));

            while (stack.Count > 0)
            {
                IEnumerator top = stack[stack.Count - 1];
                object current = null;
                bool moved = false;
                bool failed = false;
                try
                {
                    moved = top.MoveNext();
                    if (moved) current = top.Current;
                }
                catch (Exception ex)
                {
                    failed = true;
                    LogUtil.LogWarning("[VPB.Perf] bundle load threw for " + path + ": " + ex.Message);
                }

                if (failed)
                {
                    result.Bundle = null;
                    break;
                }
                if (!moved)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                IEnumerator nested = current as IEnumerator;
                if (nested != null)
                {
                    stack.Add(nested);
                    continue;
                }
                yield return current;
            }

            Finish(loader, path, result.Bundle);
        }

        static IEnumerator LoadOne(string path, LoadResult result)
        {
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
                try { result.Bundle = abcr.assetBundle; }
                catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] assetBundle fetch failed for " + path + ": " + ex.Message); }
            }
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
                    AssetBundle published;
                    if (!map.TryGetValue(path, out published))
                    {
                        map.Add(path, bundle);
                    }
                    else if (!ReferenceEquals(published, bundle))
                    {
                        try { bundle.Unload(false); } catch { }
                        bundle = published;
                    }
                    Completed++;
                }

                if (e != null)
                {
                    for (int i = 0; i < e.Waiters.Count; i++)
                    {
                        Delivery d = e.Waiters[i];
                        if (d == null) continue;
                        d.Bundle = bundle;
                        d.Ready = true;
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
                MarkProgress();
                try { Pump(loader); }
                catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] bundle pump failed: " + ex.Message); }
                try { Drain(); }
                catch (Exception ex) { LogUtil.LogWarning("[VPB.Perf] bundle drain failed: " + ex.Message); }
            }
        }

        static void Drain()
        {
            if (s_Draining) return;
            s_Draining = true;
            try
            {
                while (s_DeliveryHead < s_Delivery.Count)
                {
                    Delivery d = s_Delivery[s_DeliveryHead];
                    if (d == null) { s_DeliveryHead++; continue; }
                    if (!d.Ready) break;

                    s_DeliveryHead++;
                    var req = d.Request;
                    d.Request = null;
                    if (req == null) continue;

                    req.assetBundle = d.Bundle;
                    try { if (req.callback != null) req.callback(req); }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning("[VPB.Perf] bundle callback threw for "
                            + (req.path ?? "?") + ": " + ex.Message);
                    }
                }

                if (s_DeliveryHead >= s_Delivery.Count)
                {
                    s_Delivery.Clear();
                    s_DeliveryHead = 0;
                }
                else if (s_DeliveryHead >= DeliveryCompactThreshold)
                {
                    s_Delivery.RemoveRange(0, s_DeliveryHead);
                    s_DeliveryHead = 0;
                }
            }
            finally
            {
                s_Draining = false;
            }
        }

        static void EnsureDriver(AssetLoader loader)
        {
            if (s_DriverRunning || loader == null) return;
            s_DriverRunning = true;
            MarkProgress();
            loader.StartCoroutine(DriverCo(loader));
        }

        static IEnumerator DriverCo(AssetLoader loader)
        {
            while (true)
            {
                yield return null;

                bool idle = false;
                try
                {
                    Drain();
                    Pump(loader);
                    Watchdog(loader);
                    idle = s_DeliveryHead >= s_Delivery.Count
                        && s_InFlight.Count == 0
                        && s_Pending.Count == 0
                        && s_Running == 0;
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB.Perf] bundle driver tick failed: " + ex.Message);
                }

                if (idle) break;
            }
            s_DriverRunning = false;
        }

        static void Watchdog(AssetLoader loader)
        {
            if (s_Running == 0 && s_Pending.Count == 0 && s_InFlight.Count == 0
                && s_DeliveryHead < s_Delivery.Count)
            {
                Delivery head = s_Delivery[s_DeliveryHead];
                if (head != null && !head.Ready)
                {
                    Recovered++;
                    head.Ready = true;
                    LogUtil.LogWarning("[VPB.Perf] bundle delivery orphaned for "
                        + (head.Request != null ? head.Request.path : "?") + "; releasing queue");
                    Drain();
                }
                return;
            }

            if (s_Pending.Count == 0 || s_Running < MaxConcurrent) return;
            if (Time.realtimeSinceStartup - s_LastProgressRealtime < StallSeconds) return;

            Recovered++;
            LogUtil.LogWarning("[VPB.Perf] bundle queue stalled " + StallSeconds + "s (running=" + s_Running
                + " pending=" + s_Pending.Count + " inflight=" + s_InFlight.Count + "); reclaiming worker slots");
            s_Running = 0;
            s_PackageBusy.Clear();
            MarkProgress();
            Pump(loader);
        }

        static long s_WindowServed;
        static long s_WindowDeduped;
        static long s_WindowAlreadyLoaded;
        static long s_WindowStarted;
        static long s_WindowCompleted;
        static long s_WindowFailures;
        static long s_WindowRecovered;

        public static void BeginLoadWindow()
        {
            s_WindowServed = Served;
            s_WindowDeduped = Deduped;
            s_WindowAlreadyLoaded = AlreadyLoaded;
            s_WindowStarted = Started;
            s_WindowCompleted = Completed;
            s_WindowFailures = Failures;
            s_WindowRecovered = Recovered;
        }

        public static string Status()
        {
            return "[VPB.Perf] bundleQueue load: served=" + (Served - s_WindowServed)
                + " started=" + (Started - s_WindowStarted)
                + " dedup=" + (Deduped - s_WindowDeduped)
                + " cached=" + (AlreadyLoaded - s_WindowAlreadyLoaded)
                + " done=" + (Completed - s_WindowCompleted)
                + " fail=" + (Failures - s_WindowFailures)
                + " recovered=" + (Recovered - s_WindowRecovered)
                + " | session: served=" + Served
                + " started=" + Started
                + " dedup=" + Deduped
                + " cached=" + AlreadyLoaded
                + " done=" + Completed
                + " fail=" + Failures
                + " recovered=" + Recovered
                + " | inflight=" + (s_Delivery.Count - s_DeliveryHead)
                + " maxConcurrent=" + MaxConcurrent;
        }
    }
}
