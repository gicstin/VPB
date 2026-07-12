using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace VPB
{
    [System.Serializable]
    public class AllSerializableVarPackage
    {
        public SerializableVarPackage[] Packages;
    }

    class VarPackageMgr
    {
        public static VarPackageMgr singleton = new VarPackageMgr();

        readonly object lookupLock = new object();
        public Dictionary<string, SerializableVarPackage> lookup = new Dictionary<string, SerializableVarPackage>();

        bool dirtyExternal = false;
        bool needsBlobUpgrade = false;
        volatile int manifestLoadState; // 0=pending, 1=loading, 2=ready, -1=failed
        ManualResetEvent manifestLoadEvent;

        public bool existCache = false;
        public bool IsManifestReady => manifestLoadState == 2 && existCache;

        public SerializableVarPackage TryGetCache(string uid)
        {
            lock (lookupLock)
            {
                SerializableVarPackage cached;
                if (lookup.TryGetValue(uid, out cached))
                    return cached;
            }
            return null;
        }

        public SerializableVarPackage TryGetCacheValidated(string uid, long fileSize, long lastWriteTimeUtcTicks)
        {
            var cached = TryGetCache(uid);
            if (cached == null)
                return null;
            if (cached.VarFileSize <= 0 || cached.VarLastWriteTimeUtcTicks <= 0)
                return null;
            if (cached.VarFileSize != fileSize || cached.VarLastWriteTimeUtcTicks != lastWriteTimeUtcTicks)
                return null;
            return cached;
        }

        public void SetCache(string uid, SerializableVarPackage value)
        {
            lock (lookupLock)
            {
                lookup[uid] = value;
            }
            dirtyExternal = true;
        }

        public void Init()
        {
            existCache = false;
            manifestLoadState = 0;
            manifestLoadEvent = null;
            needsBlobUpgrade = false;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (!Directory.Exists("Cache/VPB")) Directory.CreateDirectory("Cache/VPB");
            }
            catch { }

            EnsureSqliteReadyForManifest();
            sw.Stop();
            LogUtil.Log("VarPackageMgr.Init took " + sw.ElapsedMilliseconds + "ms (manifest load deferred to refresh)");
        }

        /// <summary>Starts manifest load on a worker if not already started. Returns wait handle (may already be signaled).</summary>
        internal ManualResetEvent BeginManifestLoadIfNeeded()
        {
            if (manifestLoadState == 2 || manifestLoadState == -1)
            {
                if (manifestLoadEvent == null)
                    manifestLoadEvent = new ManualResetEvent(true);
                return manifestLoadEvent;
            }

            lock (lookupLock)
            {
                if (manifestLoadState == 1 || manifestLoadState == 2)
                    return manifestLoadEvent;
                manifestLoadState = 1;
                manifestLoadEvent = new ManualResetEvent(false);
                ManualResetEvent evt = manifestLoadEvent;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { LoadManifestFromStorage(); }
                    catch (Exception ex)
                    {
                        try { LogUtil.LogWarning("VarPackageMgr manifest load failed: " + ex.Message); } catch { }
                        manifestLoadState = -1;
                    }
                    finally
                    {
                        try { evt.Set(); } catch { }
                    }
                });
                return manifestLoadEvent;
            }
        }

        void LoadManifestFromStorage()
        {
            int loadedCount = 0;
            bool blobUpgrade = false;
            Stopwatch sw = Stopwatch.StartNew();

            lock (lookupLock)
            {
                if (VpbSqlite3.IsAvailable
                    && VpbLocalDatabase.TryLoadPackageManifestsIntoLookup(lookup, out loadedCount, out blobUpgrade)
                    && loadedCount > 0)
                {
                    existCache = true;
                    if (blobUpgrade) needsBlobUpgrade = true;
                    manifestLoadState = 2;
                    sw.Stop();
                    LogUtil.Log("VarPackageMgr manifest load DONE pkgs=" + loadedCount + " ms=" + sw.ElapsedMilliseconds
                        + (blobUpgrade ? " (blob upgrade pending)" : ""));
                    return;
                }
            }

            manifestLoadState = -1;
            sw.Stop();
            LogUtil.Log("VarPackageMgr manifest cache missing (sql=" + (VpbSqlite3.IsAvailable ? "1" : "0") + ")");
        }

        static void EnsureSqliteReadyForManifest()
        {
            try
            {
                string gameRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                if (!string.IsNullOrEmpty(gameRoot))
                    VpbSqlite3.SetGameInstallRootForNativeDll(gameRoot);
            }
            catch { }
            if (VpbSqlite3.IsAvailable) { }
        }

        public void Refresh()
        {
            if (!dirtyExternal && !needsBlobUpgrade) return;

            Dictionary<string, SerializableVarPackage> snapshot;
            lock (lookupLock)
            {
                snapshot = new Dictionary<string, SerializableVarPackage>(lookup);
            }
            if (snapshot.Count == 0) return;

            if (VpbSqlite3.IsAvailable && VpbLocalDatabase.TrySavePackageManifestSnapshot(snapshot))
            {
                dirtyExternal = false;
                needsBlobUpgrade = false;
            }
        }
    }
}
