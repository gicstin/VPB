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
            public Worker Worker;
        }

        sealed class Worker
        {
            public string Path;
            public Entry Owner;
            public int LastDriverTick;
            public float LastTickRealtime;
            public bool Disowned;
        }

        sealed class LoadResult
        {
            public AssetBundle Bundle;
        }

        static readonly Dictionary<string, Entry> s_InFlight = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly List<string> s_Pending = new List<string>();
        static readonly Dictionary<string, int> s_PackageBusy = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly List<Delivery> s_Delivery = new List<Delivery>();
        static readonly List<Worker> s_Active = new List<Worker>();
        static readonly List<string> s_OrphanScratch = new List<string>();
        static int s_DeliveryHead;
        static bool s_Draining;
        static bool s_Pumping;
        static bool s_PumpAgain;
        static bool s_DriverRunning;
        static int s_DriverTick;
        static int s_DriverEpoch;
        static float s_DriverTickRealtime;
        static AssetLoader s_DriverLoader;
        static float s_LastProgressRealtime;

        const float StallSeconds = 60f;
        const float DriverStallSeconds = 10f;
        const int WorkerStallTicks = 240;
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
            if (loader == null) return;
            if (s_Pumping)
            {
                s_PumpAgain = true;
                return;
            }
            s_Pumping = true;
            try
            {
                do
                {
                    s_PumpAgain = false;
                    PumpOnce(loader);
                }
                while (s_PumpAgain);
            }
            finally
            {
                s_Pumping = false;
            }
        }

        static void PumpOnce(AssetLoader loader)
        {
            while (s_Active.Count < MaxConcurrent && s_Pending.Count > 0)
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

                Entry owner;
                if (!s_InFlight.TryGetValue(next, out owner) || owner == null) continue;

                Worker worker = new Worker
                {
                    Path = next,
                    Owner = owner,
                    LastDriverTick = s_DriverTick,
                    LastTickRealtime = Time.realtimeSinceStartup
                };
                owner.Worker = worker;
                s_Active.Add(worker);
                MarkPackageBusy(next, 1);
                Started++;
                MarkProgress();
                loader.StartCoroutine(RunGuarded(loader, worker));
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

        static void Heartbeat(Worker worker)
        {
            worker.LastDriverTick = s_DriverTick;
            worker.LastTickRealtime = Time.realtimeSinceStartup;
        }

        static IEnumerator RunGuarded(AssetLoader loader, Worker worker)
        {
            LoadResult result = new LoadResult();
            List<IEnumerator> stack = new List<IEnumerator>(2);
            stack.Add(LoadOne(worker.Path, result));

            while (stack.Count > 0)
            {
                Heartbeat(worker);

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
                    LogUtil.LogWarning("[VPB.Perf] bundle load threw for " + worker.Path + ": " + ex.Message);
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

                AsyncOperation op = current as AsyncOperation;
                if (op != null)
                {
                    while (!op.isDone)
                    {
                        Heartbeat(worker);
                        yield return null;
                    }
                    continue;
                }

                yield return current;
            }

            Finish(loader, worker, result.Bundle);
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
                    bytes = null;
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

        static AssetBundle PublishOrUnload(AssetLoader loader, string path, AssetBundle bundle)
        {
            if (bundle == null) return null;

            if (loader == null || !ReferenceEquals(Singleton(), loader))
            {
                try { bundle.Unload(false); } catch { }
                return null;
            }

            var map = PathToBundle(loader);
            AssetBundle published;
            if (!map.TryGetValue(path, out published))
            {
                map.Add(path, bundle);
                return bundle;
            }
            if (!ReferenceEquals(published, bundle))
            {
                try { bundle.Unload(false); } catch { }
            }
            return published;
        }

        static void ResolveWaiters(Entry e, AssetBundle bundle)
        {
            if (e == null || e.Waiters == null) return;
            for (int i = 0; i < e.Waiters.Count; i++)
            {
                Delivery d = e.Waiters[i];
                if (d == null || d.Ready) continue;
                d.Bundle = bundle;
                d.Ready = true;
            }
            e.Waiters.Clear();
        }

        static void Finish(AssetLoader loader, Worker worker, AssetBundle bundle)
        {
            bool disowned = worker.Disowned;
            try
            {
                if (disowned)
                {
                    PublishOrUnload(loader, worker.Path, bundle);
                }
                else
                {
                    Entry cur;
                    if (s_InFlight.TryGetValue(worker.Path, out cur) && ReferenceEquals(cur, worker.Owner))
                        s_InFlight.Remove(worker.Path);

                    bundle = PublishOrUnload(loader, worker.Path, bundle);

                    if (bundle == null)
                    {
                        Failures++;
                        SuperController.LogError("Error during attempt to load assetbundle " + worker.Path + ". Not valid");
                    }
                    else
                    {
                        Completed++;
                    }

                    ResolveWaiters(worker.Owner, bundle);
                }
            }
            catch (Exception ex)
            {
                Failures++;
                LogUtil.LogWarning("[VPB.Perf] bundle finish failed for " + worker.Path + ": " + ex.Message);
            }
            finally
            {
                if (!disowned)
                {
                    s_Active.Remove(worker);
                    MarkPackageBusy(worker.Path, -1);
                    MarkProgress();
                }
                worker.Owner = null;
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
                    AssetBundle ready = d.Bundle;
                    d.Request = null;
                    d.Bundle = null;
                    if (req == null) continue;

                    req.assetBundle = ready;
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
            if (loader == null) return;

            if (!ReferenceEquals(s_DriverLoader, loader))
            {
                AdoptLoader(loader);
            }
            else if (s_DriverRunning)
            {
                if (Time.realtimeSinceStartup - s_DriverTickRealtime < DriverStallSeconds) return;
                Recovered++;
                LogUtil.LogWarning("[VPB.Perf] bundle driver unresponsive; restarting");
            }

            StartDriver(loader);
        }

        static void AdoptLoader(AssetLoader loader)
        {
            if ((object)s_DriverLoader != null)
            {
                for (int i = s_Active.Count - 1; i >= 0; i--)
                    DisownWorker(i, "AssetLoader replaced");
                s_PackageBusy.Clear();
            }
            s_DriverLoader = loader;
        }

        static void StartDriver(AssetLoader loader)
        {
            s_DriverLoader = loader;
            s_DriverRunning = true;
            s_DriverEpoch++;
            s_DriverTickRealtime = Time.realtimeSinceStartup;
            MarkProgress();
            loader.StartCoroutine(DriverCo(loader, s_DriverEpoch));
        }

        static IEnumerator DriverCo(AssetLoader loader, int epoch)
        {
            while (true)
            {
                yield return null;
                if (epoch != s_DriverEpoch) yield break;

                bool idle = false;
                try
                {
                    s_DriverTick++;
                    s_DriverTickRealtime = Time.realtimeSinceStartup;
                    Drain();
                    Pump(loader);
                    Watchdog(loader);
                    idle = s_DeliveryHead >= s_Delivery.Count
                        && s_InFlight.Count == 0
                        && s_Pending.Count == 0
                        && s_Active.Count == 0;
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB.Perf] bundle driver tick failed: " + ex.Message);
                }

                if (idle) break;
            }
            if (epoch == s_DriverEpoch) s_DriverRunning = false;
        }

        static void DisownWorker(int index, string reason)
        {
            Worker w = s_Active[index];
            s_Active.RemoveAt(index);
            if (w == null) return;

            w.Disowned = true;
            MarkPackageBusy(w.Path, -1);
            Recovered++;
            Failures++;
            LogUtil.LogWarning("[VPB.Perf] bundle worker abandoned for " + w.Path + " (" + reason + ")");

            Entry e = w.Owner;
            Entry cur;
            if (e != null && s_InFlight.TryGetValue(w.Path, out cur) && ReferenceEquals(cur, e))
                s_InFlight.Remove(w.Path);
            ResolveWaiters(e, null);
            MarkProgress();
        }

        static void ReclaimDeadWorkers()
        {
            if (s_Active.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = s_Active.Count - 1; i >= 0; i--)
            {
                Worker w = s_Active[i];
                if (w == null) { s_Active.RemoveAt(i); continue; }
                if (s_DriverTick - w.LastDriverTick < WorkerStallTicks) continue;
                if (now - w.LastTickRealtime < StallSeconds) continue;
                DisownWorker(i, "no coroutine tick for " + StallSeconds + "s");
            }
        }

        static void ReleaseOrphanEntries()
        {
            s_OrphanScratch.Clear();
            foreach (var kv in s_InFlight) s_OrphanScratch.Add(kv.Key);

            for (int i = 0; i < s_OrphanScratch.Count; i++)
            {
                Entry e;
                if (!s_InFlight.TryGetValue(s_OrphanScratch[i], out e)) continue;
                s_InFlight.Remove(s_OrphanScratch[i]);
                Recovered++;
                Failures++;
                LogUtil.LogWarning("[VPB.Perf] bundle entry orphaned for " + s_OrphanScratch[i] + "; releasing waiters");
                ResolveWaiters(e, null);
            }
            s_OrphanScratch.Clear();
            MarkProgress();
        }

        static void Watchdog(AssetLoader loader)
        {
            ReclaimDeadWorkers();

            if (s_Active.Count > 0) return;

            if (s_PackageBusy.Count > 0) s_PackageBusy.Clear();

            if (s_Pending.Count > 0)
            {
                Pump(loader);
                return;
            }

            if (s_InFlight.Count > 0)
            {
                ReleaseOrphanEntries();
                Drain();
                return;
            }

            if (s_DeliveryHead < s_Delivery.Count)
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
            }
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
                + " active=" + s_Active.Count
                + " pending=" + s_Pending.Count
                + " maxConcurrent=" + MaxConcurrent;
        }
    }
}
