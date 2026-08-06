using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
using Prime31.MessageKit;

namespace VPB
{
    public class FileManager : MonoBehaviour
    {
        public static bool IsScanning
        {
            get
            {
                return singleton != null
                    && (singleton.m_Co != null
                        || singleton.m_RefreshCo != null
                        || singleton.m_StartScanCo != null);
            }
        }

        public delegate void OnRefresh();

        public static bool debug;

        public static FileManager singleton;

        Coroutine m_Co = null;
        Coroutine m_RefreshCo = null;
        Coroutine m_StartScanCo = null;
        Coroutine m_MvrRefreshCo = null;
        bool m_MvrRefreshPending = false;
        bool m_RefreshPending = false;
        bool m_RefreshPendingInit = false;
        bool m_RefreshPendingClean = false;
        bool m_RefreshPendingRemoveOldVersion = false;

        // Reason tracking for the current and pending refresh passes.
        // Used to identify which startup actor triggered each scan (init/autoload/autoinstall/manual)
        // so coalesced passes can be diagnosed without a stack trace.
        readonly HashSet<string> m_CurrentRefreshReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> m_PendingRefreshReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly object m_RefreshReasonsLock = new object();

        internal static readonly object packagesLock = new object();
        protected static Dictionary<string, VarPackage> packagesByUid;
        public static Dictionary<string, VarPackage> PackagesByUid
        {
            get
            {
                return packagesByUid;

            }
        }

        public static void UpdatePackagePath(string uid, string oldPath, string newPath)
        {
            lock (packagesLock)
            {
                if (packagesByUid != null && packagesByUid.TryGetValue(uid, out var pkg))
                {
                    pkg.Path = newPath;
                }

                if (packagesByPath != null)
                {
                    if (packagesByPath.TryGetValue(oldPath, out var pkg2))
                    {
                        packagesByPath.Remove(oldPath);
                        packagesByPath[newPath] = pkg2;
                    }
                }
            }
        }

        protected static Dictionary<string, VarPackage> packagesByPath;

        protected static Dictionary<string, VarPackageGroup> packageGroups;
        protected static HashSet<VarFileEntry> allVarFileEntries;
        protected static Dictionary<string, VarFileEntry> uidToVarFileEntry;
        protected static Dictionary<string, VarFileEntry> pathToVarFileEntry;
        protected static OnRefresh onRefreshHandlers;
        protected static OnRefresh onRegistryReadyHandlers;
        static int s_RegistryReadyNotified;
        static int s_BulkDeepScanActive;

        internal static bool IsBulkDeepScanActive =>
            System.Threading.Interlocked.CompareExchange(ref s_BulkDeepScanActive, 0, 0) != 0;
        static int s_DeferredDeepScanScheduled;
        static bool s_DeferredDeepScanInit;
        static bool s_DeferredDeepScanFlag;
        static bool s_DeferredDeepScanClean;
        protected static HashSet<string> restrictedReadPaths;

        protected static Dictionary<string, string> internalPathToUidPath;
        protected static readonly object internalPathToUidPathLock = new object();

        protected static HashSet<string> secureReadPaths;

        protected static HashSet<string> secureInternalWritePaths;

        protected static HashSet<string> securePluginWritePaths;

        protected static HashSet<string> pluginWritePathsThatDoNotNeedConfirm;

        public Transform userConfirmContainer;

        public Transform userConfirmPrefab;

        public Transform userConfirmPluginActionPrefab;

        protected static Dictionary<string, string> pluginHashToPluginPath;

        protected static LinkedList<string> loadDirStack;

        public static int s_InstalledCount = 0;
        public static DateTime lastPackageRefreshTime
        {
            get;
            protected set;
        }

        // Cache for missing dependency package-IDs (strings that look like package uids but are not present locally).
        // Computing this requires scanning every package's RecursivePackageDependencies, which is expensive on large libraries.
        // We cache it per package refresh timestamp to keep UI actions (Hub missing packages panel) fast.
        private static readonly object s_MissingDependenciesCacheLock = new object();
        private static DateTime s_MissingDependenciesCacheForRefreshTime;
        private static List<string> s_MissingDependenciesCache;

        // Packages added/removed in the most recent scan — consumed by the gallery for incremental updates.
        // Written on the main thread inside RefreshCo; read on the main thread inside AutoRefreshAfterPackageScan.
        public static readonly List<VarPackage> lastAddedPackages = new List<VarPackage>();
        public static readonly List<VarPackage> lastRemovedPackages = new List<VarPackage>();

        /// <summary>Gallery consumed the pending add/remove delta for the current scan.</summary>
        public static void AckPackageGalleryDeltaConsumed()
        {
            int a = lastAddedPackages.Count;
            int r = lastRemovedPackages.Count;
            lastAddedPackages.Clear();
            lastRemovedPackages.Clear();
            if (a > 0 || r > 0)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] AckPackageGalleryDeltaConsumed cleared added=" + a + " removed=" + r);
                }
                catch { }
            }
        }

        /// <summary>True when the latest package scan produced adds/removes not yet consumed by the gallery.</summary>
        public static bool HasPendingGalleryPackageDelta()
        {
            try { return lastAddedPackages.Count > 0 || lastRemovedPackages.Count > 0; }
            catch { return false; }
        }

        static void MergePackageRefreshDelta(HashSet<VarPackage> removeSet, HashSet<string> addSet)
        {
            int pendingBefore = lastAddedPackages.Count;
            lastRemovedPackages.Clear();
            if (removeSet != null && removeSet.Count > 0)
                lastRemovedPackages.AddRange(removeSet);

            if (removeSet != null && removeSet.Count > 0 && lastAddedPackages.Count > 0)
            {
                var removedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rp in removeSet)
                {
                    if (rp != null && !string.IsNullOrEmpty(rp.Uid))
                        removedUids.Add(rp.Uid);
                }
                if (removedUids.Count > 0)
                {
                    for (int i = lastAddedPackages.Count - 1; i >= 0; i--)
                    {
                        VarPackage pending = lastAddedPackages[i];
                        if (pending != null && removedUids.Contains(pending.Uid))
                            lastAddedPackages.RemoveAt(i);
                    }
                }
            }

            int addSetCount = addSet != null ? addSet.Count : 0;
            if (addSet == null || addSet.Count == 0)
            {
                if (addSetCount == 0 && (pendingBefore > 0 || lastRemovedPackages.Count > 0))
                {
                    try
                    {
                        LogUtil.Log("[VPB.Gallery.Delta] MergePackageRefreshDelta preserve pending added="
                            + lastAddedPackages.Count + " (addSet empty pendingBefore=" + pendingBefore + ")");
                    }
                    catch { }
                }
                return;
            }

            int merged = 0;
            HashSet<string> pendingUids = null;
            if (lastAddedPackages.Count > 0)
            {
                pendingUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < lastAddedPackages.Count; i++)
                {
                    VarPackage pending = lastAddedPackages[i];
                    if (pending != null && !string.IsNullOrEmpty(pending.Uid))
                        pendingUids.Add(pending.Uid);
                }
            }

            if (addSetCount >= 512 && (pendingUids == null || pendingUids.Count == 0))
            {
                var bulk = new List<VarPackage>(addSetCount);
                lock (packagesLock)
                {
                    foreach (string addedPath in addSet)
                    {
                        VarPackage pkg;
                        if (packagesByPath.TryGetValue(addedPath, out pkg) && pkg != null)
                            bulk.Add(pkg);
                    }
                }
                lastAddedPackages.AddRange(bulk);
                merged = bulk.Count;
            }
            else
            {
                lock (packagesLock)
                {
                    if (pendingUids == null)
                        pendingUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string addedPath in addSet)
                    {
                        VarPackage pkg;
                        if (!packagesByPath.TryGetValue(addedPath, out pkg) || pkg == null)
                            continue;
                        if (string.IsNullOrEmpty(pkg.Uid))
                            continue;
                        if (pendingUids.Add(pkg.Uid))
                        {
                            lastAddedPackages.Add(pkg);
                            merged++;
                        }
                    }
                }
            }
            try
            {
                LogUtil.Log("[VPB.Gallery.Delta] MergePackageRefreshDelta addSet=" + addSetCount
                    + " merged=" + merged + " pendingAdded=" + lastAddedPackages.Count
                    + " pendingRemoved=" + lastRemovedPackages.Count);
            }
            catch { }
        }

        sealed class PackageRefreshDiff
        {
            internal readonly HashSet<string> PathHashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> AddSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<VarPackage> RemoveSet = new HashSet<VarPackage>();
            internal readonly HashSet<string> OldVersionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, List<string>> DuplicateUidPaths =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        static HashSet<string> SnapshotExistingPackageUids()
        {
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (packagesLock)
            {
                if (packagesByUid == null) return uids;
                foreach (var kv in packagesByUid)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                        uids.Add(kv.Key);
                }
            }
            return uids;
        }

        static void EnsurePackageDictionaryCapacity(int expectedAddCount)
        {
            if (expectedAddCount < 4096) return;
            lock (packagesLock)
            {
                if (packagesByUid != null && packagesByUid.Count == 0)
                {
                    packagesByUid = new Dictionary<string, VarPackage>(expectedAddCount, StringComparer.OrdinalIgnoreCase);
                    packagesByPath = new Dictionary<string, VarPackage>(expectedAddCount, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        static PackageRefreshDiff BuildPackageRefreshDiff(
            IList<string> rawPaths,
            bool skipFileIdDedup,
            HashSet<string> existingUids)
        {
            var diff = new PackageRefreshDiff();
            if (rawPaths == null || rawPaths.Count == 0) return diff;

            var addByUid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // UID already registered but path may have moved on disk — resolve after PathHashSet is complete.
            var uidPathConflicts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenFileIds = skipFileIdDedup ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> existingPaths = null;
            lock (packagesLock)
            {
                if (packagesByPath != null && packagesByPath.Count > 0)
                {
                    existingPaths = new HashSet<string>(packagesByPath.Keys, StringComparer.OrdinalIgnoreCase);
                }
            }

            for (int pi = 0; pi < rawPaths.Count; pi++)
            {
                string rawPath = rawPaths[pi];
                if (string.IsNullOrEmpty(rawPath)) continue;

                if (!skipFileIdDedup)
                {
                    string fileId;
                    if (TryGetWindowsFileId(rawPath, out fileId))
                    {
                        if (!seenFileIds.Add(fileId))
                            continue;
                    }
                }

                string varPath = CleanFilePath(rawPath);
                diff.PathHashSet.Add(varPath);

                string candidateUid = packagePathToUid(varPath).Trim();
                if (VpbPackageIndexDiagnostics.ShouldTrace(candidateUid))
                {
                    bool dupUid = existingUids != null && existingUids.Contains(candidateUid);
                    bool dupPath = existingPaths != null && existingPaths.Contains(varPath);
                    VpbPackageIndexDiagnostics.Log(candidateUid, "diffScan",
                        "raw='" + rawPath + "' clean='" + varPath + "' dupUid=" + (dupUid ? "1" : "0")
                        + " dupPath=" + (dupPath ? "1" : "0") + " skipFileIdDedup=" + (skipFileIdDedup ? "1" : "0"));
                }

                if (existingPaths != null && existingPaths.Contains(varPath))
                    continue;

                if (!string.IsNullOrEmpty(candidateUid) && existingUids != null && existingUids.Contains(candidateUid))
                {
                    string priorConflict;
                    if (uidPathConflicts.TryGetValue(candidateUid, out priorConflict))
                    {
                        string canonical = ChooseCanonicalDuplicatePath(priorConflict, varPath);
                        string loser = string.Equals(canonical, priorConflict, StringComparison.OrdinalIgnoreCase)
                            ? varPath
                            : priorConflict;
                        uidPathConflicts[candidateUid] = canonical;

                        List<string> dups;
                        if (!diff.DuplicateUidPaths.TryGetValue(candidateUid, out dups))
                        {
                            dups = new List<string>();
                            diff.DuplicateUidPaths.Add(candidateUid, dups);
                        }
                        dups.Add(loser);
                    }
                    else
                    {
                        uidPathConflicts[candidateUid] = varPath;
                    }
                    VpbPackageIndexDiagnostics.Log(candidateUid, "diffUidPathConflict", "path='" + varPath + "'");
                    continue;
                }

                if (!string.IsNullOrEmpty(candidateUid) && addByUid.TryGetValue(candidateUid, out string existingNewPath))
                {
                    string canonical = ChooseCanonicalDuplicatePath(existingNewPath, varPath);
                    string loser = string.Equals(canonical, existingNewPath, StringComparison.OrdinalIgnoreCase) ? varPath : existingNewPath;
                    addByUid[candidateUid] = canonical;
                    diff.AddSet.Remove(loser);
                    diff.AddSet.Add(canonical);

                    List<string> dups;
                    if (!diff.DuplicateUidPaths.TryGetValue(candidateUid, out dups))
                    {
                        dups = new List<string>();
                        diff.DuplicateUidPaths.Add(candidateUid, dups);
                    }
                    dups.Add(loser);
                    continue;
                }

                diff.AddSet.Add(varPath);
                if (!string.IsNullOrEmpty(candidateUid))
                    addByUid[candidateUid] = varPath;
            }

            // Resolve UID conflicts: relocate (old path gone) vs true duplicate (old path still on disk).
            if (uidPathConflicts.Count > 0)
            {
                foreach (KeyValuePair<string, string> kv in uidPathConflicts)
                {
                    string uid = kv.Key;
                    string newPath = kv.Value;
                    VarPackage existing = null;
                    lock (packagesLock)
                    {
                        if (packagesByUid != null)
                            packagesByUid.TryGetValue(uid, out existing);
                    }

                    bool existingStillPresent = false;
                    if (existing != null && !string.IsNullOrEmpty(existing.Path))
                    {
                        if (diff.PathHashSet.Contains(existing.Path))
                            existingStillPresent = true;
                        else
                        {
                            // Symlink/junction subfolders may be omitted from enum while path still resolves.
                            try { existingStillPresent = File.Exists(existing.Path); } catch { existingStillPresent = false; }
                        }
                    }

                    if (existingStillPresent)
                    {
                        List<string> dups;
                        if (!diff.DuplicateUidPaths.TryGetValue(uid, out dups))
                        {
                            dups = new List<string>();
                            diff.DuplicateUidPaths.Add(uid, dups);
                        }
                        dups.Add(newPath);
                        VpbPackageIndexDiagnostics.Log(uid, "diffSkipDupUid", "path='" + newPath + "'");
                        continue;
                    }

                    // File moved: RemoveSet drops old path; AddSet registers at new path.
                    diff.AddSet.Add(newPath);
                    VpbPackageIndexDiagnostics.Log(uid, "diffRelocate", "path='" + newPath + "'");
                }
            }

            VarPackage[] packagesSnapshot;
            lock (packagesLock)
            {
                packagesSnapshot = packagesByUid != null ? packagesByUid.Values.ToArray() : new VarPackage[0];
            }
            for (int i = 0; i < packagesSnapshot.Length; i++)
            {
                VarPackage pkg = packagesSnapshot[i];
                if (pkg == null || string.IsNullOrEmpty(pkg.Path)) continue;
                if (diff.PathHashSet.Contains(pkg.Path)) continue;
                // Keep packages whose path still resolves (junction/symlink enum gaps).
                try
                {
                    if (File.Exists(pkg.Path)) continue;
                }
                catch { }
                diff.RemoveSet.Add(pkg);
            }

            return diff;
        }

        static void AppendOldVersionRemovals(PackageRefreshDiff diff, bool removeOldVersion)
        {
            if (!removeOldVersion || diff == null) return;
            HashSet<string> referenced = GetReferencedPackage();
            lock (packagesLock)
            {
                if (packageGroups == null) return;
                foreach (var item in packageGroups)
                {
                    var group = item.Value;
                    if (group == null || group.Packages == null) continue;
                    foreach (var pkg in group.Packages)
                    {
                        if (pkg == null || pkg.Version == group.NewestVersion) continue;
                        if (referenced.Contains(pkg.Uid)) continue;
                        diff.RemoveSet.Add(pkg);
                        if (!string.IsNullOrEmpty(pkg.Path))
                            diff.OldVersionPaths.Add(pkg.Path);
                    }
                }
            }
        }

        static void LogDuplicateUidSummary(Dictionary<string, List<string>> duplicateUidPaths)
        {
            if (duplicateUidPaths == null || duplicateUidPaths.Count == 0) return;
            int dupCount = 0;
            foreach (var kv in duplicateUidPaths) dupCount += kv.Value.Count;
            StringBuilder sb = new StringBuilder(256);
            sb.Append("Duplicate package uids skipped: count=");
            sb.Append(duplicateUidPaths.Count);
            sb.Append(" extraPaths=");
            sb.Append(dupCount);
            sb.Append(" sample=");
            int shown = 0;
            foreach (var kv in duplicateUidPaths)
            {
                if (shown >= 3) break;
                if (shown > 0) sb.Append(';');
                sb.Append(kv.Key);
                sb.Append("(+");
                sb.Append(kv.Value.Count);
                sb.Append(")");
                shown++;
            }
            LogUtil.LogWarning(sb.ToString());
        }

        sealed class PackageRefreshApplyState
        {
            internal bool Flag;
        }

        static IEnumerator ApplyPackageRefreshDiffCo(
            List<string> allVarFiles,
            bool usedPathCache,
            bool clean,
            bool removeOldVersion,
            bool allowRegistryWarmRestore,
            PackageRefreshApplyState state)
        {
            HashSet<string> existingUidsSnap = SnapshotExistingPackageUids();
            PackageRefreshDiff diff = null;
            List<string> warmRegisterPaths = null;
            string warmMatchedSignature = null;
            ManualResetEvent diffReady = new ManualResetEvent(false);
            bool skipFileIdDedup = usedPathCache;
            bool registryWarmRestore = false;
            List<string> pathsForDiff = allVarFiles;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (allowRegistryWarmRestore
                        && usedPathCache
                        && VamStartupOptimizations.UseCachedVarPathInventory
                        && pathsForDiff != null
                        && pathsForDiff.Count > 0
                        && existingUidsSnap.Count == 0
                        && TryPrepareRegistryWarmRestore(pathsForDiff, existingUidsSnap, out warmRegisterPaths, out warmMatchedSignature))
                    {
                        registryWarmRestore = true;
                    }
                    else
                    {
                        diff = BuildPackageRefreshDiff(pathsForDiff, skipFileIdDedup, existingUidsSnap);
                    }
                }
                catch (Exception ex)
                {
                    try { LogUtil.LogError("BuildPackageRefreshDiff failed: " + ex.Message); } catch { }
                    diff = new PackageRefreshDiff();
                }
                finally
                {
                    try { diffReady.Set(); } catch { }
                }
            });

            while (!diffReady.WaitOne(0))
                yield return null;
            diffReady.Close();

            HashSet<string> addSet;
            HashSet<VarPackage> removeSet;
            Dictionary<string, List<string>> duplicateUidPaths;
            HashSet<string> oldVersionPaths;

            if (registryWarmRestore && warmRegisterPaths != null)
            {
                addSet = new HashSet<string>(warmRegisterPaths, StringComparer.OrdinalIgnoreCase);
                removeSet = new HashSet<VarPackage>();
                duplicateUidPaths = null;
                oldVersionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    LogUtil.Log(VamStartupOptimizations.LogTag + " registry warm restore paths="
                        + warmRegisterPaths.Count + " sig=" + (warmMatchedSignature ?? ""));
                }
                catch { }
            }
            else
            {
                AppendOldVersionRemovals(diff, removeOldVersion);
                addSet = diff.AddSet;
                removeSet = diff.RemoveSet;
                duplicateUidPaths = diff.DuplicateUidPaths;
                oldVersionPaths = diff.OldVersionPaths;
            }

            Stopwatch regSw = Stopwatch.StartNew();
            EnsurePackageDictionaryCapacity(addSet.Count);

            foreach (VarPackage item2 in removeSet)
            {
                UnregisterPackage(item2);
                state.Flag = true;
            }

            int regCount = 0;
            foreach (string item3 in addSet)
            {
                RegisterPackage(item3, clean);
                state.Flag = true;
                regCount++;
                if (!registryWarmRestore && (regCount & 511) == 0)
                    yield return null;
            }

            if (removeOldVersion)
            {
                foreach (string oldPath in oldVersionPaths)
                    RemoveToInvalid(oldPath, "OldVersion");
            }

            regSw.Stop();
            if (regCount >= 1000)
            {
                try
                {
                    LogUtil.Log(VamStartupOptimizations.LogTag + " RegisterPackage bulk count=" + regCount
                        + " ms=" + regSw.ElapsedMilliseconds
                        + " skipFileIdDedup=" + (skipFileIdDedup ? "1" : "0")
                        + " warmRestore=" + (registryWarmRestore ? "1" : "0"));
                }
                catch { }
            }
            try
            {
                VamStartupOptimizations.StartupDebug("registry phase done registered=" + regCount
                    + " ms=" + regSw.ElapsedMilliseconds
                    + " manifestReady=" + (VarPackageMgr.singleton != null && VarPackageMgr.singleton.IsManifestReady ? "1" : "0")
                    + " warmRestore=" + (registryWarmRestore ? "1" : "0"));
            }
            catch { }

            // Registry is usable for gallery/init as soon as bulk RegisterPackage finishes — do not wait on delta merge.
            TryNotifyRegistryReadyForInit();

            MergePackageRefreshDelta(removeSet, addSet);
            if (!registryWarmRestore)
                LogDuplicateUidSummary(duplicateUidPaths);
            try
            {
                int addN = addSet != null ? addSet.Count : 0;
                int remN = removeSet != null ? removeSet.Count : 0;
                if (addN > 0 || remN > 0)
                    VpbLocalDatabase.NotifyPackageInventoryChangedFromRefresh(addN, remN);
            }
            catch { }
            if (regCount > 0 && (registryWarmRestore || usedPathCache))
                TrySaveRegistryWarmSignatureFromLiveRegistry();
            try { VpbPackageIndexDiagnostics.AuditTracedPackages("ApplyPackageRefreshDiffCo"); } catch { }
        }

        public static string CurrentLoadDir
        {
            get
            {
                if (loadDirStack != null && loadDirStack.Count > 0)
                {
                    return loadDirStack.Last.Value;
                }
                return null;
            }
        }

        public static string CurrentPackageUid
        {
            get
            {
                string currentLoadDir = CurrentLoadDir;
                if (currentLoadDir != null)
                {
                    VarDirectoryEntry varDirectoryEntry = GetVarDirectoryEntry(currentLoadDir);
                    if (varDirectoryEntry != null)
                    {
                        return varDirectoryEntry.Package.Uid;
                    }
                }
                return null;
            }
        }

        public static string TopLoadDir
        {
            get
            {
                if (loadDirStack != null && loadDirStack.Count > 0)
                {
                    return loadDirStack.First.Value;
                }
                return null;
            }
        }

        public static string TopPackageUid
        {
            get
            {
                string topLoadDir = TopLoadDir;
                if (topLoadDir != null)
                {
                    VarDirectoryEntry varDirectoryEntry = GetVarDirectoryEntry(topLoadDir);
                    if (varDirectoryEntry != null)
                    {
                        return varDirectoryEntry.Package.Uid;
                    }
                }
                return null;
            }
        }

        public static string CurrentSaveDir
        {
            get;
            protected set;
        }

        protected static string packagePathToUid(string vpath)
        {
            string input = vpath.Replace('\\', '/');
            input = Regex.Replace(input, "\\.(var|zip)$", string.Empty);
            return Regex.Replace(input, ".*/", string.Empty);
        }

        /// <summary>
        /// Parses the final <c>&lt;version&gt;</c> segment: digits only, or digits plus Windows copy suffix
        /// <c>(n)</c> / <c> (n)</c> (e.g. <c>2(1)</c> → version <c>2</c>). Rejects digit-stripping junk like <c>1_1</c>.
        /// </summary>
        internal static bool TryParseVarVersionSegment(string segment, out int version)
        {
            version = 0;
            if (string.IsNullOrEmpty(segment)) return false;

            if (TryParseDigitsOnlyVarVersionSegment(segment, out version))
                return true;

            int paren = segment.IndexOf('(');
            if (paren <= 0) return false;

            string basePart = segment.Substring(0, paren).TrimEnd();
            if (!TryParseDigitsOnlyVarVersionSegment(basePart, out version))
                return false;

            string suffix = segment.Substring(paren);
            return Regex.IsMatch(suffix, @"^\(\d+\)$");
        }

        static bool TryParseDigitsOnlyVarVersionSegment(string segment, out int version)
        {
            version = 0;
            if (string.IsNullOrEmpty(segment)) return false;
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (c < '0' || c > '9') return false;
            }
            return int.TryParse(segment, out version);
        }

        internal static bool TryGetCanonicalUidFromVarPath(string vpath, out string canonicalUid)
        {
            canonicalUid = null;
            if (string.IsNullOrEmpty(vpath)) return false;
            string cleanPath = CleanFilePath(vpath);
            string text = packagePathToUid(cleanPath).Trim();
            string[] array = text.Split('.');
            if (array.Length != 3) return false;
            string creator = array[0];
            string name = array[1];
            if (string.IsNullOrEmpty(creator) || string.IsNullOrEmpty(name)) return false;
            int version;
            if (!TryParseVarVersionSegment(array[2], out version)) return false;
            canonicalUid = creator + "." + name + "." + version;
            return true;
        }

        static bool TryPrepareRegistryWarmRestore(
            IList<string> rawPaths,
            HashSet<string> existingUids,
            out List<string> canonicalPaths,
            out string matchedSignature)
        {
            canonicalPaths = null;
            matchedSignature = null;
            string storedSig;
            if (!VpbLocalDatabase.TryLoadPackageInventorySignatureForWarmRestore(out storedSig)) return false;
            if (rawPaths == null || rawPaths.Count == 0) return false;

            var addByUid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int pi = 0; pi < rawPaths.Count; pi++)
            {
                string rawPath = rawPaths[pi];
                if (string.IsNullOrEmpty(rawPath)) continue;
                string varPath = CleanFilePath(rawPath);

                string candidateUid;
                if (!TryGetCanonicalUidFromVarPath(varPath, out candidateUid)) continue;
                if (existingUids != null && existingUids.Contains(candidateUid)) continue;

                if (addByUid.TryGetValue(candidateUid, out string existingNewPath))
                {
                    string canonical = ChooseCanonicalDuplicatePath(existingNewPath, varPath);
                    addByUid[candidateUid] = canonical;
                }
                else
                {
                    addByUid[candidateUid] = varPath;
                }
            }

            if (addByUid.Count == 0) return false;

            var uidList = new List<string>(addByUid.Count);
            foreach (KeyValuePair<string, string> kv in addByUid)
                uidList.Add(kv.Key);
            string liveSig = VpbLocalDatabase.ComputePackageInventorySignatureFromUids(uidList);
            if (!string.Equals(liveSig, storedSig, StringComparison.Ordinal)) return false;

            canonicalPaths = new List<string>(addByUid.Count);
            foreach (KeyValuePair<string, string> kv in addByUid)
                canonicalPaths.Add(kv.Value);
            matchedSignature = liveSig;
            return true;
        }

        static void TrySaveRegistryWarmSignatureFromLiveRegistry()
        {
            try
            {
                Dictionary<string, VarPackage> snap;
                lock (packagesLock)
                {
                    snap = packagesByUid;
                }
                if (snap == null || snap.Count == 0) return;
                string sig = VpbLocalDatabase.ComputePackageInventorySignature(snap);
                VpbLocalDatabase.TrySaveRegistryWarmInventorySignature(sig);
            }
            catch { }
        }

        static bool RefreshReasonNeedsFreshVarDiskEnum(string refreshReason)
        {
            if (string.IsNullOrEmpty(refreshReason)) return false;
            return refreshReason.IndexOf("hub_download", StringComparison.OrdinalIgnoreCase) >= 0
                || refreshReason.IndexOf("hub_delete", StringComparison.OrdinalIgnoreCase) >= 0
                || refreshReason.IndexOf("hub_deferred", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool RefreshReasonNeedsHubPackageScan(string refreshReason)
        {
            return RefreshReasonNeedsFreshVarDiskEnum(refreshReason);
        }

        /// <summary>
        /// Register a hub direct-download .var immediately so hub UI Refresh() does not clear alreadyHave
        /// before the coalesced FileManager refresh finishes. Also appends path inventory for later cache hits.
        /// </summary>
        /// <param name="notifyInventoryChange">
        /// When false (batch Hub downloads), skip per-file gallery SQL gate invalidate — caller notifies once after queue drains.
        /// </param>
        public static VarPackage RegisterHubDownloadedPackage(string varPath, bool notifyInventoryChange = true)
        {
            if (string.IsNullOrEmpty(varPath)) return null;
            string cleanPath = CleanFilePath(varPath);
            if (string.IsNullOrEmpty(cleanPath) || !File.Exists(cleanPath)) return null;

            VarPackage existing = null;
            lock (packagesLock)
            {
                if (packagesByPath != null)
                    packagesByPath.TryGetValue(cleanPath, out existing);
            }
            if (existing != null)
            {
                try { VpbLocalDatabase.TryAppendVarPathInventory(cleanPath); } catch { }
                return existing;
            }

            VarPackage registered = RegisterPackage(cleanPath);
            try { VpbLocalDatabase.TryAppendVarPathInventory(cleanPath); } catch { }
            if (registered != null && notifyInventoryChange)
            {
                try { VpbLocalDatabase.NotifyPackageInventoryChangedFromRefresh(1, 0); } catch { }
            }
            try
            {
                LogUtil.Log("[VPB.HubDownload] RegisterHubDownloadedPackage path='" + cleanPath
                    + "' uid='" + (registered != null ? registered.Uid : "")
                    + "' notifyInv=" + (notifyInventoryChange ? "1" : "0"));
            }
            catch { }
            return registered;
        }

        /// <summary>Clear lazy MissingDepsCount caches so gallery badges recount after Hub installs.</summary>
        public static void InvalidateAllMissingDepsCounts()
        {
            lock (packagesLock)
            {
                if (packagesByUid == null) return;
                foreach (var kv in packagesByUid)
                {
                    VarPackage pkg = kv.Value;
                    if (pkg != null) pkg.MissingDepsCount = -1;
                }
            }
        }

        protected static VarPackage RegisterPackage(string vpath, bool clean = false)
        {
            if (debug)
            {
                LogUtil.Log("RegisterPackage " + vpath);
            }
            string cleanPath = CleanFilePath(vpath);
            string text = packagePathToUid(cleanPath).Trim();
            string[] array = text.Split('.');

            bool isDuplicated = false;
            if (array.Length == 3)
            {
                string text2 = array[0];
                string text3 = array[1];
                string shortName = text2 + "." + text3;
                string s = array[2];
                try
                {
                    if (string.IsNullOrEmpty(text2) || string.IsNullOrEmpty(text3))
                        throw new FormatException("Empty creator or package name segment");

                    int version;
                    if (!TryParseVarVersionSegment(s, out version))
                        throw new FormatException($"Version segment must be digits only, got '{s}'");

                    string canonicalUid = shortName + "." + version;
                    if (!string.Equals(s, version.ToString(), StringComparison.Ordinal))
                    {
                        LogUtil.LogWarning("[VPB] VAR version segment '" + s + "' normalized to " + version
                            + " for package " + canonicalUid + " (path '" + cleanPath + "')");
                    }

                    if (!packagesByUid.ContainsKey(canonicalUid))
                    {
                        VarPackageGroup value;
                        lock (packagesLock)
                        {
                            if (!packageGroups.TryGetValue(shortName, out value))
                            {
                                value = new VarPackageGroup(shortName);
                                packageGroups.Add(shortName, value);
                            }
                        }
                        VarPackage varPackage = new VarPackage(canonicalUid, cleanPath, value, text2, text3, version);
                        lock (packagesLock)
                        {
                            packagesByUid.Add(canonicalUid, varPackage);
                            packagesByPath.Add(varPackage.Path, varPackage);
                        }
                        value.AddPackage(varPackage);

                        VpbPackageIndexDiagnostics.Log(canonicalUid, "registerOk", "path='" + cleanPath + "'");

                        // Disabling a var package means creating a "disable" file in the same path
                        if (varPackage.Enabled)
                        {
                            if (varPackage.FileEntries != null)
                            {
                                lock (packagesLock)
                                {
                                    foreach (VarFileEntry fileEntry in varPackage.FileEntries)
                                    {
                                        allVarFileEntries.Add(fileEntry);
                                        uidToVarFileEntry.Add(fileEntry.Uid, fileEntry);
                                        pathToVarFileEntry.Add(fileEntry.Path, fileEntry);
                                    }
                                }
                            }
                        }
                        return varPackage;
                    }
                    isDuplicated = true;
                    VarPackage existing;
                    if (packagesByUid.TryGetValue(canonicalUid, out existing))
                    {
                        string existingPath = CleanFilePath(existing.Path);
                        if (string.Equals(existingPath, cleanPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!packagesByPath.ContainsKey(cleanPath))
                            {
                                lock (packagesLock)
                                {
                                    packagesByPath.Add(cleanPath, existing);
                                }
                            }
                            return existing;
                        }

                        string existingFileId;
                        string newFileId;
                        if (TryGetWindowsFileId(existingPath, out existingFileId)
                            && TryGetWindowsFileId(cleanPath, out newFileId)
                            && existingFileId == newFileId)
                        {
                            LogUtil.LogWarning("Duplicate package uid " + canonicalUid + " points to same file via different path. Existing: " + existing.Path + " New: " + cleanPath + ". Skipping duplicate registration");
                            if (!packagesByPath.ContainsKey(cleanPath))
                            {
                                lock (packagesLock)
                                {
                                    packagesByPath.Add(cleanPath, existing);
                                }
                            }
                            return existing;
                        }

                        LogUtil.LogError("Duplicate package uid " + canonicalUid + ". Existing: " + existing.Path + " New: " + cleanPath + ". Cannot register");
                    }
                    else
                    {
                        LogUtil.LogError("Duplicate package uid " + canonicalUid + ". Cannot register");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("VAR file " + vpath + " does not use integer version field in name <creator>.<name>.<version>");
                    VpbPackageIndexDiagnostics.LogPathEvent(vpath, "registerReject", "reason=bad_version ex=" + ex.Message);
                }
            }
            else
            {
                LogUtil.LogError("VAR file " + vpath + " is not named with convention <creator>.<name>.<version>");
                VpbPackageIndexDiagnostics.LogPathEvent(vpath, "registerReject", "reason=bad_name_segment_count segments=" + array.Length);
            }

            // Reaching here means it is invalid
            if (clean)
            {
                if (isDuplicated)
                {
                    RemoveToInvalid(vpath, "Duplicated");
                }
                else
                    RemoveToInvalid(vpath, "InvalidName");
            }
            return null;
        }

        public static void RemoveToInvalid(string vpath, string subPath = null)
        {
            if (string.IsNullOrEmpty(vpath))
            {
                LogUtil.LogError("[VPB] RemoveToInvalid: called with null/empty path. Skipping.");
                return;
            }

            if (!Directory.Exists("InvalidPackages"))
                Directory.CreateDirectory("InvalidPackages");

            if (!string.IsNullOrEmpty(subPath))
            {
                if (!Directory.Exists("InvalidPackages/" + subPath))
                    Directory.CreateDirectory("InvalidPackages/" + subPath);
            }

            string moveToPath = null;
            if (vpath.StartsWith("AllPackages"))
            {
                moveToPath = "InvalidPackages" + vpath.Substring("AllPackages".Length);
                if (!string.IsNullOrEmpty(subPath))
                {
                    moveToPath = "InvalidPackages/" + subPath + "/" + vpath.Substring("AllPackages".Length);
                }
            }
            else if (vpath.StartsWith("AddonPackages"))
            {
                moveToPath = "InvalidPackages" + vpath.Substring("AddonPackages".Length);
                if (!string.IsNullOrEmpty(subPath))
                {
                    moveToPath = "InvalidPackages/" + subPath + "/" + vpath.Substring("AddonPackages".Length);
                }
            }

            if (string.IsNullOrEmpty(moveToPath))
            {
                LogUtil.LogError("[VPB] RemoveToInvalid: cannot determine destination for path '" + vpath + "' (expected AllPackages or AddonPackages prefix). Skipping.");
                return;
            }

            string dir = Path.GetDirectoryName(moveToPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            while (File.Exists(moveToPath))
            {
                moveToPath += "(clone)";
            }
            File.Move(vpath, moveToPath);
        }

        public static void UnregisterPackage(VarPackage vp)
        {
            LogUtil.Log("UnregisterPackage " + vp.Path);
            if (vp != null)
            {
                if (vp.Group != null)
                {
                    vp.Group.RemovePackage(vp);
                }
                lock (packagesLock)
                {
                    packagesByUid.Remove(vp.Uid);
                    packagesByPath.Remove(vp.Path);
                    if (vp.FileEntries != null)
                    {
                        foreach (VarFileEntry fileEntry in vp.FileEntries)
                        {
                            allVarFileEntries.Remove(fileEntry);
                            uidToVarFileEntry.Remove(fileEntry.Uid);
                            pathToVarFileEntry.Remove(fileEntry.Path);
                        }
                    }
                }
                vp.Dispose();
            }
        }

        public static void RegisterRefreshHandler(OnRefresh refreshHandler)
        {
            onRefreshHandlers = (OnRefresh)Delegate.Combine(onRefreshHandlers, refreshHandler);
        }

        /// <summary>Fires once package paths are registered in memory (before per-package zip deep scan).</summary>
        public static void RegisterRegistryReadyHandler(OnRefresh readyHandler)
        {
            onRegistryReadyHandlers = (OnRefresh)Delegate.Combine(onRegistryReadyHandlers, readyHandler);
        }

        public static void UnregisterRegistryReadyHandler(OnRefresh readyHandler)
        {
            onRegistryReadyHandlers = (OnRefresh)Delegate.Remove(onRegistryReadyHandlers, readyHandler);
        }

        /// <summary>Re-arm the one-shot registry-ready notifier so it fires again after a scene reload (Hard Reset) re-runs the init refresh.</summary>
        public static void ResetRegistryReadyNotifiedForSceneReload()
        {
            System.Threading.Interlocked.Exchange(ref s_RegistryReadyNotified, 0);
        }

        public static void UnregisterRefreshHandler(OnRefresh refreshHandler)
        {
            onRefreshHandlers = (OnRefresh)Delegate.Remove(onRefreshHandlers, refreshHandler);
        }

        protected static void ClearAll()
        {
            lock (packagesLock)
            {
                if (packagesByUid != null)
                {
                    foreach (VarPackage value in packagesByUid.Values)
                    {
                        value.Dispose();
                    }
                    packagesByUid.Clear();
                }
                if (packagesByPath != null)
                {
                    packagesByPath.Clear();
                }
                if (packageGroups != null)
                {
                    packageGroups.Clear();
                }
                if (allVarFileEntries != null)
                {
                    allVarFileEntries.Clear();
                }
                if (uidToVarFileEntry != null)
                {
                    uidToVarFileEntry.Clear();
                }
                if (pathToVarFileEntry != null)
                {
                    pathToVarFileEntry.Clear();
                }

                if (internalPathToUidPath != null)
                {
                    internalPathToUidPath.Clear();
                }
            }
        }

        private static void EnsureInternalPathIndex()
        {
            if (internalPathToUidPath == null)
            {
                internalPathToUidPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            if (internalPathToUidPath.Count > 0) return;

            VarPackage[] snapshot = GetPackagesSnapshotForBrowse();
            if (snapshot == null) return;

            foreach (VarPackage pkg in snapshot)
            {
                if (pkg == null || !pkg.Enabled || pkg.invalid) continue;

                List<string> names;
                List<long> ticks;
                List<long> sizes;
                if (pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) && names != null)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        string internalPath = names[i];
                        if (string.IsNullOrEmpty(internalPath)) continue;
                        if (internalPathToUidPath.ContainsKey(internalPath)) continue;
                        internalPathToUidPath.Add(internalPath, pkg.Uid + ":/" + internalPath);
                    }
                    continue;
                }

                List<VarFileEntry> entries = null;
                try { entries = pkg.FileEntries; } catch { }
                if (entries == null) continue;
                foreach (VarFileEntry fe in entries)
                {
                    if (fe == null) continue;
                    if (string.IsNullOrEmpty(fe.InternalPath)) continue;
                    if (string.IsNullOrEmpty(fe.Uid)) continue;
                    if (!internalPathToUidPath.ContainsKey(fe.InternalPath))
                    {
                        internalPathToUidPath.Add(fe.InternalPath, fe.Uid);
                    }
                }
            }
        }

        static void InvalidateInternalPathIndex()
        {
            lock (internalPathToUidPathLock)
            {
                if (internalPathToUidPath != null)
                    internalPathToUidPath.Clear();
            }
        }

        static VarPackage[] GetPackagesSnapshotForBrowse()
        {
            lock (packagesLock)
            {
                if (packagesByUid == null || packagesByUid.Count == 0)
                    return null;
                return packagesByUid.Values.ToArray();
            }
        }

        static bool InternalPathMatchesBrowseDir(string internalPath, string dirPrefix)
        {
            if (string.IsNullOrEmpty(dirPrefix)) return true;
            if (string.IsNullOrEmpty(internalPath)) return false;
            string p = internalPath.Replace('\\', '/');
            return p.Equals(dirPrefix, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(dirPrefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        static void TryRegisterVarFileEntryLookup(VarFileEntry value)
        {
            if (value == null) return;
            try
            {
                lock (packagesLock)
                {
                    if (uidToVarFileEntry != null && !uidToVarFileEntry.ContainsKey(value.Uid))
                        uidToVarFileEntry.Add(value.Uid, value);
                    if (pathToVarFileEntry != null && !pathToVarFileEntry.ContainsKey(value.Path))
                        pathToVarFileEntry.Add(value.Path, value);
                }
            }
            catch { }
        }

        static void AppendVarFilesForPackage(VarPackage pkg, string dirPrefix, Regex regex, List<FileEntry> foundFiles)
        {
            List<string> names;
            List<long> ticks;
            List<long> sizes;
            if (pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) && names != null)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    string internalPath = names[i];
                    if (string.IsNullOrEmpty(internalPath)) continue;
                    if (!InternalPathMatchesBrowseDir(internalPath, dirPrefix)) continue;

                    string name = Path.GetFileName(internalPath.Replace('\\', '/'));
                    if (string.IsNullOrEmpty(name) || !regex.IsMatch(name)) continue;

                    VarFileEntry entry;
                    if (!pkg.TryCreateVarFileEntryFromCache(internalPath, out entry) || entry == null)
                        continue;
                    TryRegisterVarFileEntryLookup(entry);
                    foundFiles.Add(entry);
                }
                return;
            }

            List<VarFileEntry> entries = null;
            try { entries = pkg.FileEntries; } catch { }
            if (entries == null) return;
            foreach (VarFileEntry entry in entries)
            {
                if (entry == null) continue;
                if (!InternalPathMatchesBrowseDir(entry.InternalPath, dirPrefix)) continue;
                if (string.IsNullOrEmpty(entry.Name) || !regex.IsMatch(entry.Name)) continue;
                TryRegisterVarFileEntryLookup(entry);
                foundFiles.Add(entry);
            }
        }

        static string NormalizeBrowseInternalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('\\', '/').Trim('/');
        }

        static bool TryGetBrowseRelativeSegment(string internalPath, string dirPrefix, out string relativeSegment, out bool isFile)
        {
            relativeSegment = null;
            isFile = false;
            string normalizedPath = NormalizeBrowseInternalPath(internalPath);
            string normalizedDir = NormalizeBrowseInternalPath(dirPrefix);
            if (string.IsNullOrEmpty(normalizedPath)) return false;

            string relative;
            if (string.IsNullOrEmpty(normalizedDir))
            {
                relative = normalizedPath;
            }
            else if (normalizedPath.Equals(normalizedDir, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            else if (normalizedPath.StartsWith(normalizedDir + "/", StringComparison.OrdinalIgnoreCase))
            {
                relative = normalizedPath.Substring(normalizedDir.Length + 1);
            }
            else
            {
                return false;
            }

            if (string.IsNullOrEmpty(relative)) return false;

            int slashIdx = relative.IndexOf('/');
            if (slashIdx < 0)
            {
                relativeSegment = relative;
                isFile = true;
                return true;
            }

            relativeSegment = relative.Substring(0, slashIdx);
            isFile = false;
            return !string.IsNullOrEmpty(relativeSegment);
        }

        static void AppendVarBrowseImmediateChildForPackage(VarPackage pkg, string dirPrefix, Regex regex, List<FileEntry> foundFiles, HashSet<string> foundSubdirs)
        {
            List<string> names;
            List<long> ticks;
            List<long> sizes;
            if (pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) && names != null)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    string internalPath = names[i];
                    if (string.IsNullOrEmpty(internalPath)) continue;
                    if (!InternalPathMatchesBrowseDir(internalPath, dirPrefix)) continue;

                    string segment;
                    bool isFile;
                    if (!TryGetBrowseRelativeSegment(internalPath, dirPrefix, out segment, out isFile)) continue;

                    if (isFile)
                    {
                        if (string.IsNullOrEmpty(segment) || !regex.IsMatch(segment)) continue;
                        VarFileEntry entry;
                        if (!pkg.TryCreateVarFileEntryFromCache(internalPath, out entry) || entry == null)
                            continue;
                        TryRegisterVarFileEntryLookup(entry);
                        foundFiles.Add(entry);
                    }
                    else if (foundSubdirs != null)
                    {
                        string normalizedDir = NormalizeBrowseInternalPath(dirPrefix);
                        string subPath = string.IsNullOrEmpty(normalizedDir) ? segment : normalizedDir + "/" + segment;
                        foundSubdirs.Add(subPath);
                    }
                }
                return;
            }

            List<VarFileEntry> entries = null;
            try { entries = pkg.FileEntries; } catch { }
            if (entries == null) return;
            foreach (VarFileEntry entry in entries)
            {
                if (entry == null) continue;
                if (!InternalPathMatchesBrowseDir(entry.InternalPath, dirPrefix)) continue;

                string segment;
                bool isFile;
                if (!TryGetBrowseRelativeSegment(entry.InternalPath, dirPrefix, out segment, out isFile)) continue;

                if (isFile)
                {
                    if (string.IsNullOrEmpty(segment) || !regex.IsMatch(segment)) continue;
                    TryRegisterVarFileEntryLookup(entry);
                    foundFiles.Add(entry);
                }
                else if (foundSubdirs != null)
                {
                    string normalizedDir = NormalizeBrowseInternalPath(dirPrefix);
                    string subPath = string.IsNullOrEmpty(normalizedDir) ? segment : normalizedDir + "/" + segment;
                    foundSubdirs.Add(subPath);
                }
            }
        }

        /// <summary>Immediate child files and virtual subdirectories under a VAR internal browse path.</summary>
        public static void FindVarBrowseImmediateChildren(string dir, string pattern, List<FileEntry> foundFiles, List<string> foundSubdirs)
        {
            if (foundFiles == null || string.IsNullOrEmpty(pattern)) return;
            dir = CleanDirectoryPath(dir) ?? string.Empty;
            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            Regex rx;
            try
            {
                rx = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                return;
            }

            HashSet<string> subdirSet = foundSubdirs != null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
            VarPackage[] snapshot = GetPackagesSnapshotForBrowse();
            if (snapshot == null) return;

            for (int i = 0; i < snapshot.Length; i++)
            {
                VarPackage pkg = snapshot[i];
                if (pkg == null || !pkg.Enabled || pkg.invalid) continue;
                AppendVarBrowseImmediateChildForPackage(pkg, dir, rx, foundFiles, subdirSet);
            }

            if (foundSubdirs != null && subdirSet != null)
            {
                foundSubdirs.AddRange(subdirSet);
                foundSubdirs.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void SafeGetImmediateFilesInDirectory(string path, string pattern, List<string> results)
        {
            if (results == null || string.IsNullOrEmpty(path)) return;
            try
            {
                string[] files = Directory.GetFiles(path, pattern ?? "*");
                if (files != null && files.Length > 0)
                    results.AddRange(files);
            }
            catch { }
        }

        public static void SafeGetImmediateSubdirectories(string path, List<string> results)
        {
            if (results == null || string.IsNullOrEmpty(path)) return;
            try
            {
                string[] dirs = Directory.GetDirectories(path);
                if (dirs == null) return;
                for (int i = 0; i < dirs.Length; i++)
                {
                    string dir = dirs[i];
                    if (Path.GetFileName(dir).Equals("InvalidPackages", StringComparison.OrdinalIgnoreCase))
                        continue;
                    results.Add(dir);
                }
            }
            catch { }
        }

        /// <summary>Path-scoped .var listing for filesystem browse (never scans whole library when <paramref name="dirPath"/> is set).</summary>
        public static void CollectFilesystemVarEntries(string dirPath, bool recursive, List<FileEntry> foundFiles, List<string> foundSubdirs)
        {
            if (foundFiles == null) return;
            dirPath = CleanDirectoryPath(dirPath) ?? string.Empty;
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
                return;

            if (recursive)
            {
                List<string> paths = new List<string>();
                SafeGetFiles(dirPath, "*.var", paths);
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    if (!string.IsNullOrEmpty(path))
                        foundFiles.Add(new SystemFileEntry(path));
                }
                return;
            }

            List<string> immediateFiles = new List<string>();
            SafeGetImmediateFilesInDirectory(dirPath, "*.var", immediateFiles);
            for (int i = 0; i < immediateFiles.Count; i++)
            {
                string path = immediateFiles[i];
                if (!string.IsNullOrEmpty(path))
                    foundFiles.Add(new SystemFileEntry(path));
            }

            if (foundSubdirs != null)
            {
                List<string> subdirs = new List<string>();
                SafeGetImmediateSubdirectories(dirPath, subdirs);
                for (int i = 0; i < subdirs.Count; i++)
                {
                    string subdir = subdirs[i];
                    if (!string.IsNullOrEmpty(subdir))
                        foundSubdirs.Add(CleanDirectoryPath(subdir) ?? subdir);
                }
                foundSubdirs.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static bool TryResolveCustomInternalPathToUidPath(string customPath, out string uidPath)
        {
            uidPath = null;
            if (string.IsNullOrEmpty(customPath)) return false;

            string p = customPath.Replace('\\', '/');
            if (p.StartsWith("/")) p = p.Substring(1);
            if (!p.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase)) return false;

            lock (internalPathToUidPathLock)
            {
                EnsureInternalPathIndex();
                if (internalPathToUidPath == null) return false;
                return internalPathToUidPath.TryGetValue(p, out uidPath);
            }
        }

        /// <summary>
        /// Lightweight post-install/uninstall sync. Skips the full filesystem scan and does NOT
        /// signal observers. Safe to call after InstallSelf/UninstallSelf because those methods
        /// already update VarPackage.Path in-place, so PackagesByUid is already accurate.
        /// Resync packagesByPath and recount s_InstalledCount (read via OnGUI — no event needed).
        /// lastPackageRefreshTime is intentionally NOT updated so the gallery does not see a
        /// spurious change and trigger a RefreshFiles: the file list is identical before and after
        /// a path-only move (AllPackages ↔ AddonPackages).
        /// </summary>
        /// <param name="galleryPathRefreshForPackageUids">
        /// When non-null and non-empty, refresh cached <see cref="FileEntry.Path"/> only for gallery rows for these package UIDs.
        /// </param>
        public static void NotifyInstalled(ICollection<string> galleryPathRefreshForPackageUids)
        {
            lock (packagesLock)
            {
                if (packagesByUid != null && packagesByPath != null)
                {
                    packagesByPath.Clear();
                    foreach (var pkg in packagesByUid.Values)
                    {
                        if (pkg != null && !string.IsNullOrEmpty(pkg.Path))
                            packagesByPath[pkg.Path] = pkg;
                    }
                }

                s_InstalledCount = 0;
                if (packagesByUid != null)
                {
                    foreach (var pkg in packagesByUid.Values)
                    {
                        if (pkg != null && pkg.IsInstalled())
                            s_InstalledCount++;
                    }
                }
            }
            // No lastPackageRefreshTime update, no MessageKit.post — gallery content is unchanged.
            if (galleryPathRefreshForPackageUids != null && galleryPathRefreshForPackageUids.Count > 0)
            {
                try { Gallery.NotifyDisplayedPathsAfterPackagePathChanges(galleryPathRefreshForPackageUids); } catch { }
                try { VpbLocalDatabase.TryUpdatePkgPathAndLoadedForUids(galleryPathRefreshForPackageUids); } catch { }
            }
        }

        public static void Refresh(bool init = false, bool clean = false, bool removeOldVersion = false)
        {
            Refresh(null, init, clean, removeOldVersion);
        }

        /// <summary>
        /// Refresh with an explicit reason tag (e.g. "init", "autoload", "autoinstall", "manual").
        /// Reasons are accumulated across coalesced calls and emitted in the per-pass scan stats log.
        /// </summary>
        public static void Refresh(string reason, bool init = false, bool clean = false, bool removeOldVersion = false)
        {
            if (singleton == null) return;

            string normalizedReason = string.IsNullOrEmpty(reason) ? "manual" : reason;

            // Coalesce refresh requests.
            // Refresh triggers a full var enumeration which is expensive on large libraries.
            // Some UI actions can call Refresh multiple times in short succession; stopping/restarting
            // the coroutine causes repeated enumerations.
            if (singleton.m_RefreshCo != null)
            {
                singleton.m_RefreshPending = true;
                singleton.m_RefreshPendingInit |= init;
                singleton.m_RefreshPendingClean |= clean;
                singleton.m_RefreshPendingRemoveOldVersion |= removeOldVersion;
                lock (singleton.m_RefreshReasonsLock)
                {
                    singleton.m_PendingRefreshReasons.Add(normalizedReason);
                }
                return;
            }

            lock (singleton.m_RefreshReasonsLock)
            {
                singleton.m_CurrentRefreshReasons.Clear();
                singleton.m_CurrentRefreshReasons.Add(normalizedReason);
            }
            singleton.m_RefreshCo = singleton.StartCoroutine(singleton.RefreshCo(init, clean, removeOldVersion));
        }

        string GetCurrentReasonsForLog()
        {
            lock (m_RefreshReasonsLock)
            {
                if (m_CurrentRefreshReasons.Count == 0) return "manual";
                var arr = m_CurrentRefreshReasons.ToArray();
                Array.Sort(arr, StringComparer.Ordinal);
                return string.Join(",", arr);
            }
        }

        private IEnumerator RefreshCo(bool init, bool clean, bool removeOldVersion)
        {
            string refreshReason = GetCurrentReasonsForLog();
            VamStartupProfiler.BeginVpbRefresh(refreshReason);
            try
            {
#if DEBUG
            string stackTrace = new System.Diagnostics.StackTrace().ToString();
            if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
                LogUtil.LogWarning("Refresh " + stackTrace);
#endif
            if (packagesByUid == null)
            {
                packagesByUid = new Dictionary<string, VarPackage>(StringComparer.OrdinalIgnoreCase);
            }
            if (packagesByPath == null)
            {
                packagesByPath = new Dictionary<string, VarPackage>(StringComparer.OrdinalIgnoreCase);
            }
            if (packageGroups == null)
            {
                packageGroups = new Dictionary<string, VarPackageGroup>(StringComparer.OrdinalIgnoreCase);
            }
            if (allVarFileEntries == null)
            {
                allVarFileEntries = new HashSet<VarFileEntry>();
            }
            if (uidToVarFileEntry == null)
            {
                uidToVarFileEntry = new Dictionary<string, VarFileEntry>(StringComparer.OrdinalIgnoreCase);
            }
            if (pathToVarFileEntry == null)
            {
                pathToVarFileEntry = new Dictionary<string, VarFileEntry>(StringComparer.OrdinalIgnoreCase);
            }

            bool flag = false;
            try
            {
                if (!Directory.Exists("AddonPackages"))
                {
                    CreateDirectory("AddonPackages");
                }
                if (!Directory.Exists("AllPackages"))
                {
                    CreateDirectory("AllPackages");
                }
            }
            catch (Exception arg)
            {
                LogUtil.LogError("Exception during package refresh initialization " + arg);
            }

            if (Directory.Exists("AllPackages"))
            {
                List<string> allVarFiles = new List<string>();
                string[] scanRoots = new string[] { "AddonPackages", "AllPackages" };

                try { VpbProgressService.BeginManifestLoad(); } catch { }
                ManualResetEvent manifestDone = VarPackageMgr.singleton.BeginManifestLoadIfNeeded();

                VamStartupProfiler.BeginScope("vpb_RefreshCo_var_disk_enum");
                ManualResetEvent inventoryDone = new ManualResetEvent(false);
                bool usedPathCache = false;
                bool skipPathInventoryCache = RefreshReasonNeedsFreshVarDiskEnum(refreshReason);
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        List<string> cachedPaths;
                        if (!skipPathInventoryCache
                            && VamStartupOptimizations.UseCachedVarPathInventory
                            && VpbLocalDatabase.TryRestoreVarPathInventory(out cachedPaths)
                            && cachedPaths != null && cachedPaths.Count > 0)
                        {
                            lock (allVarFiles)
                            {
                                allVarFiles.AddRange(cachedPaths);
                            }
                            usedPathCache = true;
                            return;
                        }

                        foreach (string root in scanRoots)
                        {
                            if (Directory.Exists(root))
                                SafeGetFiles(root, "*.var", allVarFiles);
                        }

                        if (VamStartupOptimizations.UseCachedVarPathInventory && allVarFiles.Count > 0)
                            VpbLocalDatabase.TrySaveVarPathInventory(allVarFiles);
                    }
                    finally
                    {
                        inventoryDone.Set();
                    }
                });

                while (!inventoryDone.WaitOne(0) || !manifestDone.WaitOne(0))
                {
                    yield return null;
                }
                try { VpbProgressService.EndManifestLoad(); } catch { }
                inventoryDone.Close();
                VamStartupProfiler.EndScope("vpb_RefreshCo_var_disk_enum");
                if (usedPathCache)
                {
                    try { LogUtil.Log(VamStartupOptimizations.LogTag + " var disk enum skipped (path inventory cache)"); } catch { }
                }

                // VPB package indexing intentionally bypasses scan-whitelist filtering.
                // The whitelist applies only to VaM's native startup registration path.
                if (ScanWhitelistManager.Instance.IsEnabled)
                {
                    LogUtil.Log(string.Format(
                        "[VPB ScanWhitelist] VPB index bypass active: indexing all local .var files ({0}) while VaM scan remains whitelist-restricted",
                        allVarFiles.Count));
                }

                var applyState = new PackageRefreshApplyState { Flag = flag };
                bool allowRegistryWarmRestore = init && usedPathCache && !removeOldVersion;
                IEnumerator applyCo = ApplyPackageRefreshDiffCo(allVarFiles, usedPathCache, clean, removeOldVersion, allowRegistryWarmRestore, applyState);
                while (true)
                {
                    bool more;
                    try
                    {
                        more = applyCo.MoveNext();
                    }
                    catch (Exception arg)
                    {
                        LogUtil.LogError("Exception during package refresh processing " + arg);
                        break;
                    }
                    if (!more) break;
                    yield return applyCo.Current;
                }
                flag = applyState.Flag;
            }

            if (init)
                TryNotifyRegistryReadyForInit();
            
            try
            {
                bool forceHubScan = RefreshReasonNeedsHubPackageScan(refreshReason);
                if (init || flag || clean || removeOldVersion || forceHubScan)
                {
                    bool scanFlag = flag || forceHubScan;
                    if (ShouldDeferDeepPackageScanOnInit(init))
                    {
                        VamStartupProfiler.Milestone("vpb_RefreshCo_StartScan_deferred_until_ready init=1 pkgs="
                            + (packagesByUid != null ? packagesByUid.Count : 0));
                        try
                        {
                            LogUtil.Log(VamStartupOptimizations.LogTag
                                + " deferring deep package scan until startup ready"
                                + " | manifestReady=" + (VarPackageMgr.singleton != null && VarPackageMgr.singleton.IsManifestReady ? "1" : "0")
                                + " | registered=" + (packagesByUid != null ? packagesByUid.Count : 0));
                        }
                        catch { }
                        ScheduleDeferredDeepScan(init, scanFlag, clean);
                    }
                    else
                    {
                        VamStartupProfiler.Milestone("vpb_RefreshCo_StartScan_scheduled init=" + (init ? "1" : "0")
                            + " hub=" + (forceHubScan ? "1" : "0"));
                        try
                        {
                            VamStartupOptimizations.StartupDebug("StartScan immediate init=" + (init ? "1" : "0")
                                + " pkgs=" + (packagesByUid != null ? packagesByUid.Count : 0)
                                + " manifestReady=" + (VarPackageMgr.singleton != null && VarPackageMgr.singleton.IsManifestReady ? "1" : "0"));
                        }
                        catch { }
                        StartScan(init, scanFlag, clean, true);
                    }
                }

                s_InstalledCount = 0;
                VarPackage[] installedSnapshot;
                lock (packagesLock)
                {
                    installedSnapshot = packagesByUid.Values.ToArray();
                }
                foreach (var pkg in installedSnapshot)
                {
                    if (pkg.IsInstalled())
                    {
                        s_InstalledCount++;
                    }
                }
                VamOnDemandLoader.ClearCache();
                m_RefreshCo = null;
                if (m_RefreshPending)
                {
                    bool nextInit = m_RefreshPendingInit;
                    bool nextClean = m_RefreshPendingClean;
                    bool nextRemoveOld = m_RefreshPendingRemoveOldVersion;
                    m_RefreshPending = false;
                    m_RefreshPendingInit = false;
                    m_RefreshPendingClean = false;
                    m_RefreshPendingRemoveOldVersion = false;
                    lock (m_RefreshReasonsLock)
                    {
                        m_CurrentRefreshReasons.Clear();
                        foreach (var r in m_PendingRefreshReasons) m_CurrentRefreshReasons.Add(r);
                        m_PendingRefreshReasons.Clear();
                    }
                    m_RefreshCo = StartCoroutine(RefreshCo(nextInit, nextClean, nextRemoveOld));
                }
            }
            catch (Exception arg)
            {
                LogUtil.LogError("Exception during package refresh finalization " + arg);
            }
            }
            finally
            {
                VamStartupProfiler.EndVpbRefresh();
            }
        }

        public void StartScan(bool init, bool flag, bool clean, bool runCo)
		{
			if (m_StartScanCo != null)
			{
				StopCoroutine(m_StartScanCo);
				m_StartScanCo = null;
			}
			m_StartScanCo = StartCoroutine(StartScanCo(init, flag, clean, runCo));
		}

        static bool ShouldDeferDeepPackageScanOnInit(bool init)
        {
            if (!init) return false;
            if (!VamStartupOptimizations.DeferPackageDeepScanUntilReady) return false;
            try
            {
                if (LogUtil.IsStartupReadyLogged() || LogUtil.IsReadyLogged()) return false;
            }
            catch { }
            try
            {
                if (VarPackageMgr.singleton != null && VarPackageMgr.singleton.IsManifestReady)
                    return false;
            }
            catch { }
            return true;
        }

        static void TryNotifyRegistryReadyForInit()
        {
            if (System.Threading.Interlocked.CompareExchange(ref s_RegistryReadyNotified, 1, 0) != 0)
                return;
            int pkgCount = 0;
            try
            {
                lock (packagesLock)
                    pkgCount = packagesByUid != null ? packagesByUid.Count : 0;
            }
            catch { }
            try
            {
                LogUtil.Log(VamStartupOptimizations.LogTag + " package registry ready"
                    + " | pkgs=" + pkgCount
                    + " | manifestReady=" + (VarPackageMgr.singleton != null && VarPackageMgr.singleton.IsManifestReady ? "1" : "0")
                    + " | deepScanDeferred=" + (ShouldDeferDeepPackageScanOnInit(true) ? "1" : "0"));
            }
            catch { }
            try
            {
                if (lastPackageRefreshTime == DateTime.MinValue)
                    lastPackageRefreshTime = DateTime.UtcNow;
            }
            catch { }
            try { VpbLocalDatabase.NotifyPackageRegistryReady(); } catch { }
            try
            {
                if (onRegistryReadyHandlers != null)
                    onRegistryReadyHandlers();
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Startup] registry ready handler failed: " + ex.Message); } catch { }
            }
        }

        static void ScheduleDeferredDeepScan(bool init, bool flag, bool clean)
        {
            if (System.Threading.Interlocked.CompareExchange(ref s_DeferredDeepScanScheduled, 1, 0) != 0)
                return;
            s_DeferredDeepScanInit = init;
            s_DeferredDeepScanFlag = flag;
            s_DeferredDeepScanClean = clean;
            LogUtil.RegisterPostReadyOnce(RunDeferredDeepScanAfterStartupReady);
        }

        static void RunDeferredDeepScanAfterStartupReady()
        {
            if (singleton == null) return;
            try
            {
                LogUtil.Log(VamStartupOptimizations.LogTag + " starting deferred deep package scan post-READY"
                    + " | pkgs=" + (packagesByUid != null ? packagesByUid.Count : 0));
            }
            catch { }
            try { singleton.StartScan(s_DeferredDeepScanInit, s_DeferredDeepScanFlag, s_DeferredDeepScanClean, true); }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Startup] deferred deep scan failed to start: " + ex.Message); } catch { }
            }
        }

		static int s_BulkDepPrewarmScheduled;
		static int s_BulkDepPrewarmDone;
		static int s_FirstGallerySqlRefreshNotified;

		/// <summary>First gallery SQLite category query finished — safe to prewarm pkg_dep bulk cache off the hot path.</summary>
		public static void NotifyFirstGallerySqlRefreshComplete()
		{
			if (System.Threading.Interlocked.CompareExchange(ref s_FirstGallerySqlRefreshNotified, 1, 0) != 0)
				return;
			try { VamStartupProfiler.Milestone("vpb_pkg_dep_prewarm_scheduled_after_gallery_sql"); } catch { }
			ScheduleBulkDependencyCachePrewarm();
		}

		static void ScheduleBulkDependencyCachePrewarm()
		{
			if (System.Threading.Interlocked.CompareExchange(ref s_BulkDepPrewarmScheduled, 1, 0) != 0)
				return;
			System.Threading.ThreadPool.QueueUserWorkItem(_ =>
			{
				try
				{
					var sw = System.Diagnostics.Stopwatch.StartNew();
					if (VpbLocalDatabase.TryEnsurePackageDependencyBulkCache())
					{
						sw.Stop();
						System.Threading.Interlocked.Exchange(ref s_BulkDepPrewarmDone, 1);
						try
						{
							VamStartupProfiler.Milestone("vpb_pkg_dep_bulk_prewarm_done ms=" + sw.ElapsedMilliseconds);
						}
						catch { }
					}
				}
				catch { }
				finally
				{
					if (System.Threading.Interlocked.CompareExchange(ref s_BulkDepPrewarmDone, 0, 0) == 0)
						System.Threading.Interlocked.Exchange(ref s_BulkDepPrewarmScheduled, 0);
				}
			});
		}

		static void RunDeferredStaggeredBulkDepPrewarmFallback()
		{
			System.Threading.ThreadPool.QueueUserWorkItem(_ =>
			{
				try
				{
					// Let first gallery SQL drain finish before loading full pkg_dep into memory.
					System.Threading.Thread.Sleep(12000);
					if (System.Threading.Interlocked.CompareExchange(ref s_BulkDepPrewarmDone, 0, 0) != 0)
						return;
					try
					{
						LogUtil.Log(VamStartupOptimizations.LogTag + " pkg_dep bulk prewarm fallback (no gallery SQL yet)");
					}
					catch { }
					ScheduleBulkDependencyCachePrewarm();
				}
				catch { }
			});
		}

		static void ResetDependentCountSchedulingFlagsForNewInventory()
		{
			System.Threading.Interlocked.Exchange(ref s_BulkDepPrewarmScheduled, 0);
			System.Threading.Interlocked.Exchange(ref s_BulkDepPrewarmDone, 0);
			System.Threading.Interlocked.Exchange(ref s_FirstGallerySqlRefreshNotified, 0);
		}

		public static void InvalidateDependentCounts()
		{
			try
			{
				VarPackage[] snapshot;
				lock (packagesLock)
				{
					if (packagesByUid == null) return;
					snapshot = packagesByUid.Values.ToArray();
				}
				for (int i = 0; i < snapshot.Length; i++)
				{
					if (snapshot[i] != null)
						snapshot[i].DependentCount = -1;
				}
			}
			catch { }
		}

		public static int ResolveDependentCount(VarPackage pkg)
		{
			if (pkg == null) return 0;
			if (pkg.DependentCount >= 0)
				return pkg.DependentCount;

			string uid = pkg.Uid;
			if (string.IsNullOrEmpty(uid))
			{
				pkg.DependentCount = 0;
				return 0;
			}

			string targetShort = GetPackageGroupShortUid(uid);
			int count = 0;
			try
			{
				if (VpbLocalDatabase.TryCountDependentUids(uid, targetShort, out count))
				{
					pkg.DependentCount = count;
					return count;
				}
			}
			catch { }

			try
			{
				HashSet<string> deps;
				if (DependencyGraph.TryGetTransitiveDependents(uid, out deps) && deps != null)
					count = deps.Count;
			}
			catch { }

			pkg.DependentCount = count;
			return count;
		}

		static string GetPackageGroupShortUid(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return null;
			try
			{
				int firstDot = uid.IndexOf('.');
				if (firstDot < 0) return null;
				int secondDot = uid.IndexOf('.', firstDot + 1);
				if (secondDot < 0) return null;
				return uid.Substring(0, secondDot);
			}
			catch { return null; }
		}

		IEnumerator StartScanCo(bool init, bool flag, bool clean, bool runCo)
		{
			List<VarPackage> invalid = new List<VarPackage>();
			int allCount = 0;
			try
			{
				lock (packagesLock)
					allCount = packagesByUid != null ? packagesByUid.Count : 0;
			}
			catch { }
			try
			{
				VamStartupOptimizations.StartupDebug("StartScanCo begin init=" + (init ? "1" : "0")
                    + " pkgs=" + allCount + " runCo=" + (runCo ? "1" : "0"));
			}
			catch { }
			VamStartupProfiler.BeginScope("vpb_StartScanCo_scan");
			if (runCo)
			{
				if (m_Co != null)
				{
					StopCoroutine(m_Co);
					m_Co = null;
				}
				m_Co = StartCoroutine(ScanVarPackagesCo(clean, invalid));
				yield return m_Co;
			}
			else
			{
				if (packagesByUid != null)
				{
					VarPackage[] scanSnapshot;
					lock (packagesLock)
					{
						scanSnapshot = packagesByUid.Values.ToArray();
					}
					foreach (var pkg in scanSnapshot)
					{
						if (pkg == null) continue;
						pkg.Scan();
						if (pkg.invalid)
						{
							invalid.Add(pkg);
						}
					}
				}
			}

			if (clean && invalid.Count > 0)
			{
				foreach (var pkg in invalid)
				{
					string path = pkg.Path;
					UnregisterPackage(pkg);
					RemoveToInvalid(path, "InvalidZip");
				}
			}

			VamStartupProfiler.EndScope("vpb_StartScanCo_scan");

			InvalidateInternalPathIndex();

			// Update time BEFORE calling handlers so handlers (and any UI code they trigger) see the latest time
			lastPackageRefreshTime = DateTime.Now;
			int addedCount = 0;
			int removedCount = 0;
			try { addedCount = lastAddedPackages.Count; removedCount = lastRemovedPackages.Count; } catch { }
			try
			{
				s_DeepScanLastManifestFlushScanned = 0;
				TryPeriodicManifestFlushDuringDeepScan(allCount, allCount);
			}
			catch { }
			try { VpbLocalDatabase.NotifyPackageScanCompleted(addedCount, removedCount); } catch { }

			if (init)
			{
				if (onRefreshHandlers != null) onRefreshHandlers();
			}
			else
			{
				if (flag && onRefreshHandlers != null) onRefreshHandlers();
			}

			ResetDependentCountSchedulingFlagsForNewInventory();
			try { DependencyGraph.OnPackageInventoryRefreshed(); } catch { }
			InvalidateDependentCounts();

			if (init)
			{
				try
				{
					VamStartupProfiler.Milestone("vpb_dependent_counts_lazy");
					LogUtil.Log(VamStartupOptimizations.LogTag + " dependent counts lazy (on demand; pkg_dep prewarm after gallery SQL)");
				}
				catch { }
				LogUtil.RegisterPostReadyOnce(RunDeferredStaggeredBulkDepPrewarmFallback);
			}

			VamStartupProfiler.MarkVpbPackageScanComplete();
			MessageKit.post(MessageDef.FileManagerRefresh);
			m_StartScanCo = null;
		}

		static int s_DeepScanLastManifestFlushScanned;

		static void TryPeriodicManifestFlushDuringDeepScan(int scanned, int total)
		{
			if (scanned <= 0) return;
			int step = Math.Max(2000, Math.Min(10000, total / 10));
			if (scanned != total && scanned - s_DeepScanLastManifestFlushScanned < step) return;
			s_DeepScanLastManifestFlushScanned = scanned;
			ThreadPool.QueueUserWorkItem(_ =>
			{
				try { VarPackageMgr.singleton?.Refresh(); } catch { }
			});
		}

		IEnumerator ScanVarPackagesCo(bool clean, List<VarPackage> invalid)
		{
			if (packagesByUid == null) yield break;
			System.Threading.Interlocked.Exchange(ref s_BulkDeepScanActive, 1);
			try
			{
			// Reset per-pass scan counters so logged stats reflect THIS pass only,
			// not the cumulative totals across all coalesced/follow-up passes.
			VarPackage.ResetScanCounters();
			s_DeepScanLastManifestFlushScanned = 0;
			Stopwatch indexAllSw = Stopwatch.StartNew();
			VarPackage[] packages = packagesByUid.Values.ToArray();
			int idx = 0;
			int allCount = packages.Length;
			try { VpbProgressService.BeginDeepScan(allCount); } catch { }
			int uiUpdateStep = (VarPackageMgr.singleton != null && VarPackageMgr.singleton.existCache) ? 100 : 200;
			int progressLogStep = Math.Max(uiUpdateStep, 2000);
			string reasonsTag = GetCurrentReasonsForLog();
			{
				int maxWorkers = Math.Min(8, Math.Max(1, System.Environment.ProcessorCount));
				int nextIndex = -1;
				int doneWorkers = 0;
				int scannedCount = 0;
				object invalidLock = new object();
				ManualResetEvent doneEvent = new ManualResetEvent(false);
				for (int w = 0; w < maxWorkers; w++)
				{
					ThreadPool.QueueUserWorkItem((state) =>
					{
						try
						{
							while (true)
							{
								int i = Interlocked.Increment(ref nextIndex);
								if (i >= packages.Length) break;
								VarPackage pkg = packages[i];
								if (pkg != null)
								{
									pkg.Scan();
									if (pkg.invalid)
									{
										lock (invalidLock)
										{
											invalid.Add(pkg);
										}
									}
								}
								Interlocked.Increment(ref scannedCount);
							}
						}
						finally
						{
							if (Interlocked.Increment(ref doneWorkers) == maxWorkers)
							{
								doneEvent.Set();
							}
						}
					});
				}

				while (!doneEvent.WaitOne(0))
				{
					int current = Interlocked.CompareExchange(ref scannedCount, 0, 0);
					if (current != idx)
					{
						idx = current;
						if ((idx % uiUpdateStep) == 0 || idx == allCount)
						{
							try { VpbProgressService.ReportDeepScan(idx, allCount); } catch { }
						}
						if (VamStartupOptimizations.ShouldLogStartupDebug
                            && ((idx % progressLogStep) == 0 || idx == allCount))
						{
							try
							{
								LogUtil.Log("[VPB.Startup] deep scan progress " + idx + "/" + allCount
                                    + " elapsed_ms=" + indexAllSw.ElapsedMilliseconds);
							}
							catch { }
						}
						if ((idx % progressLogStep) == 0 || idx == allCount)
						{
							try { VpbLocalDatabase.NotifyDeepScanProgress(idx, allCount); } catch { }
						}
					}
					yield return null;
				}
				doneEvent.Close();
				idx = allCount;
				try { VpbProgressService.ReportDeepScan(idx, allCount); } catch { }
			}
			indexAllSw.Stop();
			double indexSeconds = indexAllSw.Elapsed.TotalSeconds;
			VamStartupProfiler.AddPhaseMs("vpb_scan_index", indexAllSw.Elapsed.TotalMilliseconds);
			try
			{
				LogUtil.Log(VamStartupOptimizations.LogTag + " deep scan complete"
                    + " pkgs=" + allCount
                    + " sec=" + indexSeconds.ToString("0.00")
                    + " ms=" + indexAllSw.ElapsedMilliseconds
                    + " reason=" + reasonsTag);
			}
			catch { }
			LogUtil.Log("VarPackageMgr index all packages " + allCount + " in " + indexSeconds.ToString("0.00") + "s (" + indexAllSw.ElapsedMilliseconds + "ms) reason=" + reasonsTag);
			long total;
			long cacheValidatedHit;
			long cacheHit;
			long zipScan;
			VarPackage.GetScanCounters(out total, out cacheValidatedHit, out cacheHit, out zipScan);
			LogUtil.Log("VarPackageMgr scan stats total=" + total + " cacheValidatedHit=" + cacheValidatedHit + " cacheHit=" + cacheHit + " zipScan=" + zipScan + " reason=" + reasonsTag);
			}
			finally
			{
				try { VpbProgressService.EndDeepScan(); } catch { }
				System.Threading.Interlocked.Exchange(ref s_BulkDeepScanActive, 0);
				// RebuildCore may have coalesced while bulk scan held caches incomplete.
				try { VpbLocalDatabase.FlushPendingGalleryIndexAfterDeepScan(); } catch { }
			}
		}

		public List<string> GetAllVars()
		{
			List<string> ret = new List<string>();
			if (packagesByUid != null)
			{
				VarPackage[] snapshot;
				lock (packagesLock)
				{
					snapshot = packagesByUid.Values.ToArray();
				}
				foreach (VarPackage value4 in snapshot)
				{
					if (value4 != null) ret.Add(value4.Path);
				}
			}
			return ret;
		}

		public static HashSet<string> GetReferencedPackage()
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (packagesByUid != null)
			{
				KeyValuePair<string, VarPackage>[] snapshot;
				lock (packagesLock)
				{
					snapshot = packagesByUid.ToArray();
				}
				foreach (var item in snapshot)
				{
					VarPackage vp = item.Value;
					if (vp != null && vp.RecursivePackageDependencies != null)
					{
						foreach (var key in vp.RecursivePackageDependencies)
						{
							if (key != null && !key.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
							{
								hashSet.Add(key);
							}
						}
					}
				}
			}
			return hashSet;
		}

		private static readonly HashSet<string> s_EmptyPackageGroupSet =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private static string s_CachedForceLatestRaw;
		private static string s_CachedForceLatestIgnoreRaw;
		private static HashSet<string> s_CachedForceLatestSet = s_EmptyPackageGroupSet;
		private static HashSet<string> s_CachedForceLatestIgnoreSet = s_EmptyPackageGroupSet;

		private static HashSet<string> ParsePackageGroupList(string raw)
		{
			if (string.IsNullOrEmpty(raw)) return s_EmptyPackageGroupSet;
			HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] parts = Regex.Split(raw, "[\\s,;]+", RegexOptions.CultureInvariant);
			for (int i = 0; i < parts.Length; i++)
			{
				string p = parts[i];
				if (string.IsNullOrEmpty(p)) continue;
				p = p.Trim();
				if (p.Length == 0) continue;
				set.Add(p);
			}
			return set.Count == 0 ? s_EmptyPackageGroupSet : set;
		}

		/// <summary>
		/// Cached force/ignore sets — NormalizeLoadPath hits this often; avoid Regex.Split + HashSet per call.
		/// </summary>
		private static void EnsureForceLatestGroupCache()
		{
			string forceRaw = null;
			string ignoreRaw = null;
			try
			{
				if (Settings.Instance != null)
				{
					if (Settings.Instance.ForceLatestDependencyPackageGroups != null)
						forceRaw = Settings.Instance.ForceLatestDependencyPackageGroups.Value;
					if (Settings.Instance.ForceLatestDependencyIgnorePackageGroups != null)
						ignoreRaw = Settings.Instance.ForceLatestDependencyIgnorePackageGroups.Value;
				}
			}
			catch { }

			if (forceRaw == null) forceRaw = "";
			if (ignoreRaw == null) ignoreRaw = "";

			if (s_CachedForceLatestSet != null
				&& string.Equals(forceRaw, s_CachedForceLatestRaw, StringComparison.Ordinal)
				&& string.Equals(ignoreRaw, s_CachedForceLatestIgnoreRaw, StringComparison.Ordinal))
				return;

			s_CachedForceLatestRaw = forceRaw;
			s_CachedForceLatestIgnoreRaw = ignoreRaw;
			s_CachedForceLatestSet = ParsePackageGroupList(forceRaw);
			s_CachedForceLatestIgnoreSet = ParsePackageGroupList(ignoreRaw);
		}

		private static bool TryGetPackageGroupFromDependencyId(string depId, out string packageGroup)
		{
			packageGroup = null;
			if (string.IsNullOrEmpty(depId)) return false;
			Match match;
			if ((match = Regex.Match(depId, "^([^\\.]+\\.[^\\.]+)\\.[0-9]+$", RegexOptions.CultureInvariant)).Success)
			{
				packageGroup = match.Groups[1].Value;
				return true;
			}
			if ((match = Regex.Match(depId, "^([^\\.]+\\.[^\\.]+)\\.min[0-9]+$", RegexOptions.CultureInvariant)).Success)
			{
				packageGroup = match.Groups[1].Value;
				return true;
			}
			if ((match = Regex.Match(depId, "^([^\\.]+\\.[^\\.]+)\\.latest$", RegexOptions.CultureInvariant)).Success)
			{
				packageGroup = match.Groups[1].Value;
				return true;
			}
			return false;
		}

		private static readonly HashSet<string> s_ForceLatestDepLogOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static bool s_ForceLatestSummaryLogged = false;
		private const int MaxForceLatestSkipDetailLogs = 10;
		private static int s_ForceLatestSkipDetailLogged;
		private static bool s_ForceLatestSkipCapNoteLogged;

		private static bool ShouldLogForceLatestDependencies()
		{
			try
			{
				if (VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode) return true;
				if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value) return true;
				return false;
			}
			catch
			{
				return false;
			}
		}

		private static void LogForceLatestDecisionOnce(string key, string message)
		{
			if (!ShouldLogForceLatestDependencies()) return;
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message)) return;
			try
			{
				if (!s_ForceLatestDepLogOnce.Add(key))
					return;

				// SKIP (not in force / whitelisted) is high-volume; show first N then silence.
				// APPLY / KEEP stay uncapped — those are actionable.
				bool isSkipNoise = key.Length >= 3
					&& (key.StartsWith("nf:", StringComparison.OrdinalIgnoreCase)
						|| key.StartsWith("wl:", StringComparison.OrdinalIgnoreCase));
				if (isSkipNoise)
				{
					if (s_ForceLatestSkipDetailLogged >= MaxForceLatestSkipDetailLogs)
					{
						if (!s_ForceLatestSkipCapNoteLogged)
						{
							s_ForceLatestSkipCapNoteLogged = true;
							LogUtil.Log("[VPB] ForceLatestDeps: further SKIP details silenced (cap=" + MaxForceLatestSkipDetailLogs + ")");
						}
						return;
					}
					s_ForceLatestSkipDetailLogged++;
				}

				LogUtil.Log(message);
			}
			catch { }
		}

		/// <summary>
		/// True when ForceLatestDependencies is on and <paramref name="packageGroup"/> is in the force list
		/// (and not ignored). Used by on-demand path rewrite.
		/// </summary>
		public static bool ShouldForceLatestForPackageGroup(string packageGroup)
		{
			try
			{
				if (string.IsNullOrEmpty(packageGroup)) return false;
				if (Settings.Instance == null || Settings.Instance.ForceLatestDependencies == null || !Settings.Instance.ForceLatestDependencies.Value)
					return false;

				EnsureForceLatestGroupCache();
				if (s_CachedForceLatestIgnoreSet != null && s_CachedForceLatestIgnoreSet.Contains(packageGroup))
					return false;
				return s_CachedForceLatestSet != null && s_CachedForceLatestSet.Contains(packageGroup);
			}
			catch
			{
				return false;
			}
		}

		private static string MaybeForceLatestDependency(string dependencyId)
		{
			try
			{
				if (Settings.Instance == null || Settings.Instance.ForceLatestDependencies == null || !Settings.Instance.ForceLatestDependencies.Value)
					return dependencyId;

				// One-time dev-mode summary so you can confirm settings and list sizes.
				if (!s_ForceLatestSummaryLogged && ShouldLogForceLatestDependencies())
				{
					s_ForceLatestSummaryLogged = true;
					try
					{
						EnsureForceLatestGroupCache();
						int ignoreCount0 = s_CachedForceLatestIgnoreSet != null ? s_CachedForceLatestIgnoreSet.Count : 0;
						int forceCount0 = s_CachedForceLatestSet != null ? s_CachedForceLatestSet.Count : 0;
						LogUtil.Log("[VPB] ForceLatestDeps: ENABLED | forceCount=" + forceCount0 + " | whitelistCount=" + ignoreCount0);
					}
					catch { }
				}

				if (!TryGetPackageGroupFromDependencyId(dependencyId, out string packageGroup) || string.IsNullOrEmpty(packageGroup))
					return dependencyId;

				EnsureForceLatestGroupCache();
				if (s_CachedForceLatestIgnoreSet != null && s_CachedForceLatestIgnoreSet.Contains(packageGroup))
				{
					LogForceLatestDecisionOnce("wl:" + packageGroup, "[VPB] ForceLatestDeps: SKIP (whitelisted) dep='" + dependencyId + "'");
					return dependencyId;
				}

				if (s_CachedForceLatestSet == null || !s_CachedForceLatestSet.Contains(packageGroup))
				{
					LogForceLatestDecisionOnce("nf:" + packageGroup, "[VPB] ForceLatestDeps: SKIP (not in force list) dep='" + dependencyId + "'");
					return dependencyId;
				}

				if (dependencyId.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
				{
					LogForceLatestDecisionOnce("al:" + dependencyId, "[VPB] ForceLatestDeps: KEEP (already .latest) dep='" + dependencyId + "'");
					return dependencyId;
				}

				string forced = packageGroup + ".latest";
				LogForceLatestDecisionOnce("ap:" + dependencyId + "=>" + forced, "[VPB] ForceLatestDeps: APPLY dep='" + dependencyId + "' -> '" + forced + "'");
				return forced;
			}
			catch
			{
				return dependencyId;
			}
		}

		public static VarPackage GetPackageForDependency(string dependencyId, bool ensureInstalled = true)
		{
			string resolvedId = MaybeForceLatestDependency(dependencyId);
			VarPackage pkg = GetPackage(resolvedId, ensureInstalled);
			if (pkg != null) return pkg;
			return ResolveVersionedDependencyFallback(resolvedId, ensureInstalled);
		}

		/// <summary>
		/// When an exact / min pin is missing, resolve per ReferenceVersionOption (referrer context or Latest).
		/// </summary>
		static VarPackage ResolveVersionedDependencyFallback(string dependencyId, bool ensureInstalled)
		{
			if (string.IsNullOrEmpty(dependencyId)) return null;
			try
			{
				if (dependencyId.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
					return null;

				Match min = Regex.Match(dependencyId, "^([^\\.]+\\.[^\\.]+)\\.min([0-9]+)$", RegexOptions.IgnoreCase);
				if (min.Success)
				{
					string groupId = min.Groups[1].Value;
					int reqMin;
					if (!int.TryParse(min.Groups[2].Value, out reqMin)) return null;
					VarPackageGroup g = GetPackageGroup(groupId);
					if (g == null) return null;
					VarPackage closest = g.GetClosestMatchingPackageVersion(reqMin, false, false);
					if (closest != null && ensureInstalled) EnsurePackageInstalled(closest);
					return closest;
				}

				string group = PackageIDToPackageGroupID(dependencyId);
				string verStr = PackageIDToPackageVersion(dependencyId);
				int ver;
				if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(verStr) || !int.TryParse(verStr, out ver))
					return null;

				VarPackage.ReferenceVersionOption option =
					PackageReferenceVersionResolver.GetEffectiveOption(null);
				string altUid = PackageReferenceVersionResolver.ResolveMissingVersionUid(group, ver, option);
				if (string.IsNullOrEmpty(altUid)) return null;
				return GetPackage(altUid, ensureInstalled);
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// True when a local package satisfies <paramref name="dependencyId"/> for gallery missing-deps /
		/// Hub download. Exact meta pins follow <see cref="VarPackage.ReferenceVersionOption"/> when
		/// RespectPackageReferenceVersionOption is on; otherwise newer installed versions still satisfy.
		/// </summary>
		public static bool IsDependencySatisfiedByInstalled(string dependencyId)
		{
			return IsDependencySatisfiedByInstalled(dependencyId, PackageReferenceVersionResolver.GetEffectiveOption(null));
		}

		public static bool IsDependencySatisfiedByInstalled(string dependencyId, VarPackage.ReferenceVersionOption option)
		{
			if (string.IsNullOrEmpty(dependencyId)) return false;
			try
			{
				if (GetPackage(MaybeForceLatestDependency(dependencyId), ensureInstalled: false) != null)
					return true;

				string group = PackageIDToPackageGroupID(dependencyId);
				if (string.IsNullOrEmpty(group)) return false;

				VarPackage newest = GetPackage(group + ".latest", ensureInstalled: false);
				if (newest == null) return false;

				if (dependencyId.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
					return true;

				Match min = Regex.Match(dependencyId, "\\.min([0-9]+)$", RegexOptions.IgnoreCase);
				if (min.Success)
				{
					int reqMin;
					if (int.TryParse(min.Groups[1].Value, out reqMin))
						return newest.Version >= reqMin;
					return true;
				}

				string ver = PackageIDToPackageVersion(dependencyId);
				int exact;
				if (!string.IsNullOrEmpty(ver) && int.TryParse(ver, out exact))
				{
					if (PackageReferenceVersionResolver.ForceExactGlobally()
						|| option == VarPackage.ReferenceVersionOption.Exact)
					{
						return GetPackage(dependencyId, ensureInstalled: false) != null;
					}
					return newest.Version >= exact;
				}

				return true;
			}
			catch
			{
				return false;
			}
		}

		public static bool IsDependencySatisfiedByInstalled(string dependencyId, VarPackage consumer)
		{
			return IsDependencySatisfiedByInstalled(
				dependencyId,
				PackageReferenceVersionResolver.GetSatisfactionOption(consumer));
		}

		static bool TryRebuildDependentCountsFromBulkEdges(VarPackage[] snapshot)
		{
			try
			{
				if (snapshot == null || snapshot.Length == 0) return false;
				if (!VpbLocalDatabase.TryEnsurePackageDependencyBulkCache())
					return false;

				var uidToPkg = new Dictionary<string, VarPackage>(snapshot.Length, StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < snapshot.Length; i++)
				{
					VarPackage p = snapshot[i];
					if (p == null || string.IsNullOrEmpty(p.Uid)) continue;
					uidToPkg[p.Uid] = p;
				}

				bool ok = VpbLocalDatabase.TryIterateBulkPackageDependencies((srcUid, depUid) =>
				{
					if (string.IsNullOrEmpty(depUid)) return;
					VarPackage dep;
					if (!uidToPkg.TryGetValue(depUid, out dep) || dep == null)
						dep = GetPackageForDependency(depUid, false);
					if (dep != null)
						dep.DependentCount++;
				});
				if (ok)
				{
					try { VamStartupProfiler.Milestone("vpb_RebuildDependentCounts_bulk_edges"); } catch { }
				}
				return ok;
			}
			catch
			{
				return false;
			}
		}

		// Builds a reverse-dependency index: for every installed package D, counts how many
		// other packages list D as a (transitive) dependency and stores it in D.DependentCount.
		// Called once at the end of each scan, before the FileManagerRefresh message is posted.
		public static void RebuildDependentCounts()
		{
			try
			{
				VarPackage[] snapshot;
				lock (packagesLock)
				{
					if (packagesByUid == null) return;
					snapshot = packagesByUid.Values.ToArray();
				}

				// Reset
				for (int i = 0; i < snapshot.Length; i++)
					snapshot[i].DependentCount = 0;

				// Fast path: one pkg_dep table scan + invert edges (O(edges), not O(pkgs * query)).
				if (TryRebuildDependentCountsFromBulkEdges(snapshot))
					return;

				// Prefer the persisted SQLite dependency edges when available to avoid forcing
				// package meta parsing/scans just to compute dependent counts.
				try
				{
					var depsFromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					bool usedSql = false;
					for (int i = 0; i < snapshot.Length; i++)
					{
						var pkg = snapshot[i];
						if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) continue;
						depsFromSql.Clear();
						if (!VpbLocalDatabase.TryReadRecursiveDependencyUids(pkg.Uid, depsFromSql))
						{
							usedSql = false;
							break;
						}
						usedSql = true;
						foreach (var depId in depsFromSql)
						{
							if (string.IsNullOrEmpty(depId)) continue;
							VarPackage dep = GetPackageForDependency(depId, false);
							if (dep != null) dep.DependentCount++;
						}
					}
					if (usedSql) return;
				}
				catch { }

				// Fallback: Each package's RecursivePackageDependencies is already a de-duped set,
				// so we can increment directly without per-package seen-tracking.
				for (int i = 0; i < snapshot.Length; i++)
				{
					var deps = snapshot[i].RecursivePackageDependencies;
					if (deps == null) continue;
					for (int j = 0; j < deps.Count; j++)
					{
						VarPackage dep = GetPackageForDependency(deps[j], false);
						if (dep != null)
							dep.DependentCount++;
					}
				}
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB] RebuildDependentCounts failed: " + ex.Message);
			}
		}

		public List<string> GetMissingDependenciesNames()
		{
            // Fast path: return cached value if nothing has refreshed since it was computed.
            // lastPackageRefreshTime is updated at the end of RefreshCo.
            lock (s_MissingDependenciesCacheLock)
            {
                if (s_MissingDependenciesCache != null && s_MissingDependenciesCacheForRefreshTime == lastPackageRefreshTime)
                {
                    return new List<string>(s_MissingDependenciesCache);
                }
            }

            // Normalize missing dependency ids for hub queries:
            // - Treat any versioned dependency "Author.Name.3" as "Author.Name.latest"
            //   so we only search/download the newest hub version (user preference).
            // - Keep ".latest" as-is.
            // - Keep ".minN" as ".latest" as well, because the hub can satisfy it with the latest.
            string NormalizeForHub(string depId)
            {
                if (string.IsNullOrEmpty(depId)) return depId;
                Match m;
                if ((m = Regex.Match(depId, "^([^\\.]+\\.[^\\.]+)\\.(?:[0-9]+|min[0-9]+|latest)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)).Success)
                {
                    return m.Groups[1].Value + ".latest";
                }
                return depId;
            }

            HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (packagesByUid != null)
            {
                foreach (var item in packagesByUid)
                {
                    VarPackage vp = item.Value;
                    if (vp != null && vp.RecursivePackageDependencies != null)
                    {
                        foreach (var key in vp.RecursivePackageDependencies)
                        {
                            string normalized = NormalizeForHub(key);

                            // Important: IsPackage only checks exact UID/path; it does not treat ".latest" as an alias.
                            // Use GetPackage so ".latest"/".minN" resolve to an installed package when present.
                            if (string.IsNullOrEmpty(normalized)) continue;
                            VarPackage resolved = null;
                            try { resolved = GetPackage(normalized, ensureInstalled: false); } catch { }
                            if (resolved == null)
                            {
                                hashSet.Add(normalized);
                            }
                        }
                    }
                }
            }

            var list = hashSet.ToList();
            lock (s_MissingDependenciesCacheLock)
            {
                s_MissingDependenciesCacheForRefreshTime = lastPackageRefreshTime;
                s_MissingDependenciesCache = list;
            }
            if (debug)
            {
                LogUtil.Log("GetMissingDependenciesNames " + list.Count);
            }
            return new List<string>(list);
		}

		public static bool IsSecureReadPath(string path)
		{
			return true;
		}

		public static bool IsSecureWritePath(string path)
		{
			return true;
		}

		public static HashSet<string> GetDependenciesDeep(string uid, int maxDepth = 2)
		{
			HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(uid) || maxDepth <= 0) return result;
			VarPackage root = GetPackage(uid, false);
			if (root == null) return result;

			// If SQLite has a ready dependency index, use it to avoid scanning the package.
			if (maxDepth >= 2)
			{
				try
				{
					var depsFromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					if (VpbLocalDatabase.TryReadRecursiveDependencyUids(uid, depsFromSql))
					{
						foreach (var dep in depsFromSql)
							if (!string.IsNullOrEmpty(dep)) result.Add(dep);
						return result;
					}
				}
				catch { }
			}

			try { if (!root.Scaned) root.Scan(); } catch { }

			if (maxDepth >= 2 && root.RecursivePackageDependencies != null)
			{
				foreach (var dep in root.RecursivePackageDependencies)
				{
					if (!string.IsNullOrEmpty(dep)) result.Add(dep);
				}
				return result;
			}

			if (root.PackageDependencies != null)
			{
				foreach (var dep in root.PackageDependencies)
				{
					if (!string.IsNullOrEmpty(dep)) result.Add(dep);
				}
			}
			return result;
		}

        public static void RegisterPluginHashToPluginPath(string hash, string path)
        {
            if (pluginHashToPluginPath == null)
            {
                pluginHashToPluginPath = new Dictionary<string, string>();
            }
            pluginHashToPluginPath.Remove(hash);
            pluginHashToPluginPath.Add(hash, path);
        }

        protected static string GetPluginHash()
        {
            StackTrace stackTrace = new StackTrace();
            string result = null;
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                StackFrame frame = stackTrace.GetFrame(i);
                MethodBase method = frame.GetMethod();
                AssemblyName name = method.DeclaringType.Assembly.GetName();
                string name2 = name.Name;
                if (name2.StartsWith("MVRPlugin_"))
                {
                    result = Regex.Replace(name2, "_[0-9]+$", string.Empty);
                    break;
                }
            }
            return result;
        }

        public static void AssertNotCalledFromPlugin()
        {
            string pluginHash = GetPluginHash();
            if (pluginHash != null)
            {
                throw new Exception("Plugin with signature " + pluginHash + " tried to execute forbidden operation");
            }
        }

        public static string GetFullPath(string path)
        {
            string path2 = Regex.Replace(path, "^file:///", string.Empty);
            return Path.GetFullPath(path2);
        }

        public static bool IsPackagePath(string path)
        {
            string input = path.Replace('\\', '/');
            string packageUidOrPath = Regex.Replace(input, ":/.*", string.Empty);
            // IMPORTANT: do not use GetPackage default (ensureInstalled: true) here.
            // IsPackagePath is called from UI / thumbnail / filtering codepaths and must not trigger installs.
            VarPackage package = GetPackage(packageUidOrPath, ensureInstalled: false);
            return package != null;
        }

        public static bool IsSimulatedPackagePath(string path)
        {
            string input = path.Replace('\\', '/');
            string packageUidOrPath = Regex.Replace(input, ":/.*", string.Empty);
            return GetPackage(packageUidOrPath, ensureInstalled: false)?.IsSimulated ?? false;
        }

        public static string ConvertSimulatedPackagePathToNormalPath(string path)
        {
            string text = path.Replace('\\', '/');
            if (text.Contains(":/"))
            {
                string packageUidOrPath = Regex.Replace(text, ":/.*", string.Empty);
                // Avoid unintended installs when normalizing UI paths.
                VarPackage package = GetPackage(packageUidOrPath, ensureInstalled: false);
                if (package != null && package.IsSimulated)
                {
                    string str = Regex.Replace(text, ".*:/", string.Empty);
                    path = package.Path + "/" + str;
                }
            }
            return path;
        }

        public static string RemovePackageFromPath(string path)
        {
            string input = Regex.Replace(path, ".*:/", string.Empty);
            return Regex.Replace(input, ".*:\\\\", string.Empty);
        }

        public static string NormalizePath(string path)
        {
            string text = path;
            VarFileEntry varFileEntry = GetVarFileEntry(path);
            if (varFileEntry == null)
            {
                string fullPath = GetFullPath(path);
                string oldValue = Path.GetFullPath(".") + "\\";
                string text2 = fullPath.Replace(oldValue, string.Empty);
                if (text2 != fullPath)
                {
                    text = text2;
                }
                return text.Replace('\\', '/');
            }
            return varFileEntry.Uid;
        }

        public static string GetDirectoryName(string path, bool returnSlashPath = false)
        {
            VarFileEntry value;
            string path2 = (uidToVarFileEntry != null && uidToVarFileEntry.TryGetValue(path, out value)) ?
                //((!returnSlashPath) ? value.Path : value.SlashPath) : 
                //((!returnSlashPath) ? path.Replace('/', '\\') : path.Replace('\\', '/'));
                value.Path : path.Replace('\\', '/');
            return Path.GetDirectoryName(path2);
        }

        public static string GetSuggestedBrowserDirectoryFromDirectoryPath(string suggestedDir, string currentDir, bool allowPackagePath = true)
        {
            if (currentDir == null || currentDir == string.Empty)
            {
                return suggestedDir;
            }
            string input = suggestedDir.Replace('\\', '/');
            input = Regex.Replace(input, "/$", string.Empty);
            string text = currentDir.Replace('\\', '/');
            VarDirectoryEntry varDirectoryEntry = GetVarDirectoryEntry(text);
            if (varDirectoryEntry != null)
            {
                if (!allowPackagePath)
                {
                    return null;
                }
                string text2 = varDirectoryEntry.InternalPath.Replace(input, string.Empty);
                if (varDirectoryEntry.InternalPath != text2)
                {
                    return varDirectoryEntry.Package.Path + ":/" + input + text2;
                }
            }
            else
            {
                string text3 = text.Replace(input, string.Empty);
                if (text != text3)
                {
                    return suggestedDir + text3;
                }
            }
            return null;
        }

        public static VarPackage ResolveDependency(string uid)
        {
            uid = MaybeForceLatestDependency(uid);

            // Try exact match
            if (packagesByUid.ContainsKey(uid)) return packagesByUid[uid];

            // Try to resolve group
            string groupId = PackageIDToPackageGroupID(uid);
            if (packageGroups.ContainsKey(groupId))
            {
                VarPackageGroup group = packageGroups[groupId];
                if (uid.EndsWith(".latest")) return group.NewestPackage;

                string verStr = PackageIDToPackageVersion(uid);
                if (verStr != null && int.TryParse(verStr, out int ver))
                {
                    VarPackage.ReferenceVersionOption option =
                        PackageReferenceVersionResolver.GetEffectiveOption(null);
                    string altUid = PackageReferenceVersionResolver.ResolveMissingVersionUid(groupId, ver, option);
                    if (string.IsNullOrEmpty(altUid)) return null;
                    VarPackage alt;
                    if (packagesByUid.TryGetValue(altUid, out alt)) return alt;
                    return group.GetClosestMatchingPackageVersion(
                        ver,
                        false,
                        option == VarPackage.ReferenceVersionOption.Latest);
                }

                // Fallback to newest if we can't parse version but found group
                return group.NewestPackage;
            }
            return null;
        }

        public static void SetLoadDir(string dir, bool restrictPath = false)
        {
            if (loadDirStack != null)
            {
                loadDirStack.Clear();
            }
            PushLoadDir(dir, restrictPath);
        }

        public static void PushLoadDir(string dir, bool restrictPath = false)
        {
            string text = dir.Replace('\\', '/');
            if (text != "/")
            {
                text = Regex.Replace(text, "/$", string.Empty);
            }
            if (restrictPath && !IsSecureReadPath(text))
            {
                throw new Exception("Attempted to push load dir for non-secure dir " + text);
            }
            if (loadDirStack == null)
            {
                loadDirStack = new LinkedList<string>();
            }
            loadDirStack.AddLast(text);
        }

		public static string PopLoadDir()
		{
			string result = null;
			if (loadDirStack != null && loadDirStack.Count > 0)
			{
				result = loadDirStack.Last.Value;
				loadDirStack.RemoveLast();
			}
			return result;
		}

		public static void SetLoadDirFromFilePath(string path, bool restrictPath = false)
		{
			if (loadDirStack != null)
			{
				loadDirStack.Clear();
			}
			PushLoadDirFromFilePath(path, restrictPath);
		}

		public static void PushLoadDirFromFilePath(string path, bool restrictPath = false)
		{
			if (restrictPath && !IsSecureReadPath(path))
			{
				throw new Exception("Attempted to set load dir from non-secure path " + path);
			}
			FileEntry fileEntry = GetFileEntry(path);
			string dir;
			if (fileEntry != null)
			{
				if (fileEntry is VarFileEntry)
				{
					dir = Path.GetDirectoryName(fileEntry.Uid);
				}
				else
				{
					dir = Path.GetDirectoryName(fileEntry.Path);
					string oldValue = Path.GetFullPath(".") + "\\";
					dir = dir.Replace(oldValue, string.Empty);
				}
			}
			else
			{
				dir = Path.GetDirectoryName(GetFullPath(path));
				string oldValue2 = Path.GetFullPath(".") + "\\";
				dir = dir.Replace(oldValue2, string.Empty);
			}
			PushLoadDir(dir, restrictPath);
		}

		public static string PackageIDToPackageGroupID(string packageId)
		{
			string input = Regex.Replace(packageId, "\\.[0-9]+$", string.Empty);
			input = Regex.Replace(input, "\\.latest$", string.Empty);
			return Regex.Replace(input, "\\.min[0-9]+$", string.Empty);
		}

		public static string PackageIDToPackageVersion(string packageId)
		{
			Match match = Regex.Match(packageId, "[0-9]+$");
			if (match.Success)
			{
				return match.Value;
			}
			return null;
		}

		public static string NormalizeID(string id)
		{
			if (id.StartsWith("SELF:"))
			{
				string currentPackageUid = CurrentPackageUid;
				if (currentPackageUid != null)
				{
					return id.Replace("SELF:", currentPackageUid + ":");
				}
				return id.Replace("SELF:", string.Empty);
			}
			return NormalizeCommon(id);
		}

		protected static string NormalizeCommon(string path)
		{
			string text = path;
			Match match;
			if ((match = Regex.Match(text, "^(([^\\.]+\\.[^\\.]+)\\.latest):")).Success)
			{
				string value = match.Groups[1].Value;
				string value2 = match.Groups[2].Value;
				VarPackageGroup packageGroup = GetPackageGroup(value2);
				if (packageGroup != null)
				{
					VarPackage newestEnabledPackage = packageGroup.NewestEnabledPackage;
					if (newestEnabledPackage != null)
					{
						text = text.Replace(value, newestEnabledPackage.Uid);
					}
				}
			}
			else if ((match = Regex.Match(text, "^(([^\\.]+\\.[^\\.]+)\\.min([0-9]+)):")).Success)
			{
				string value3 = match.Groups[1].Value;
				string value4 = match.Groups[2].Value;
				int requestVersion = int.Parse(match.Groups[3].Value);
				VarPackageGroup packageGroup2 = GetPackageGroup(value4);
				if (packageGroup2 != null)
				{
					VarPackage closestMatchingPackageVersion = packageGroup2.GetClosestMatchingPackageVersion(requestVersion, true, false);
					if (closestMatchingPackageVersion != null)
					{
						text = text.Replace(value3, closestMatchingPackageVersion.Uid);
					}
				}
			}
			else if ((match = Regex.Match(text, "^([^\\.]+\\.[^\\.]+\\.[0-9]+):")).Success)
			{
				string value5 = match.Groups[1].Value;
				VarPackage package = GetPackage(value5);
				if (package == null || !package.Enabled)
				{
					string packageGroupUid = PackageIDToPackageGroupID(value5);
					string verStr = PackageIDToPackageVersion(value5);
					int requestVersion;
					VarPackage.ReferenceVersionOption option =
						PackageReferenceVersionResolver.GetEffectiveOption(text);
					if (!string.IsNullOrEmpty(verStr) && int.TryParse(verStr, out requestVersion))
					{
						string altUid = PackageReferenceVersionResolver.ResolveMissingVersionUid(
							packageGroupUid, requestVersion, option);
						if (!string.IsNullOrEmpty(altUid))
						{
							text = text.Replace(value5, altUid);
						}
					}
					else
					{
						VarPackageGroup packageGroup3 = GetPackageGroup(packageGroupUid);
						if (packageGroup3 != null
							&& option != VarPackage.ReferenceVersionOption.Exact)
						{
							package = packageGroup3.NewestEnabledPackage;
							if (package != null)
							{
								text = text.Replace(value5, package.Uid);
							}
						}
					}
				}
			}
			return text;
		}

		public static string NormalizeLoadPath(string path)
		{
			string result = path;
			if (path != null && path != string.Empty && path != "/" && path != "NULL")
			{
				result = path.Replace('\\', '/');
				string currentLoadDir = CurrentLoadDir;
				if (currentLoadDir != null && currentLoadDir != string.Empty)
				{
					if (!result.Contains("/"))
					{
						result = currentLoadDir + "/" + result;
					}
					else if (Regex.IsMatch(result, "^\\./"))
					{
						result = Regex.Replace(result, "^\\./", currentLoadDir + "/");
					}
				}
				if (result.StartsWith("SELF:/"))
				{
					string currentPackageUid = CurrentPackageUid;
					result = ((currentPackageUid == null) ? result.Replace("SELF:/", string.Empty) : result.Replace("SELF:/", currentPackageUid + ":/"));
				}
				else
				{
					result = NormalizeCommon(result);
				}
			}
			return result;
		}

		public static void SetSaveDir(string path, bool restrictPath = true)
		{
			if (path == null || path == string.Empty)
			{
				CurrentSaveDir = string.Empty;
				return;
			}
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsPackagePath(path))
			{
				if (restrictPath && !IsSecureWritePath(path))
				{
					throw new Exception("Attempted to set save dir from non-secure path " + path);
				}
				string fullPath = GetFullPath(path);
				string oldValue = Path.GetFullPath(".") + "\\";
				fullPath = fullPath.Replace(oldValue, string.Empty);
				CurrentSaveDir = fullPath.Replace('\\', '/');
			}
		}

		public static void SetSaveDirFromFilePath(string path, bool restrictPath = true)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsPackagePath(path))
			{
				if (restrictPath && !IsSecureWritePath(path))
				{
					throw new Exception("Attempted to set save dir from non-secure path " + path);
				}
				string directoryName = Path.GetDirectoryName(GetFullPath(path));
				string oldValue = Path.GetFullPath(".") + "\\";
				directoryName = directoryName.Replace(oldValue, string.Empty);
				CurrentSaveDir = directoryName.Replace('\\', '/');
			}
		}

		public static void SetNullSaveDir()
		{
			CurrentSaveDir = null;
		}

		public static string NormalizeSavePath(string path)
		{
			string text = path;
			if (path != null && path != string.Empty && path != "/" && path != "NULL")
			{
				string path2 = Regex.Replace(path, "^file:///", string.Empty);
				string fullPath = Path.GetFullPath(path2);
				string oldValue = Path.GetFullPath(".") + "\\";
				string text2 = fullPath.Replace(oldValue, string.Empty);
				if (text2 != fullPath)
				{
					text = text2;
				}
				text = text.Replace('\\', '/');
				string fileName = Path.GetFileName(text2);
				string text3 = Path.GetDirectoryName(text2);
				if (text3 != null)
				{
					text3 = text3.Replace('\\', '/');
				}
				if (CurrentSaveDir == text3)
				{
					text = fileName;
				}
				else if (CurrentSaveDir != null && CurrentSaveDir != string.Empty && Regex.IsMatch(text3, "^" + CurrentSaveDir + "/"))
				{
					text = text3.Replace(CurrentSaveDir, ".") + "/" + fileName;
				}
			}
			return text;
		}

		public static List<VarPackage> GetPackages()
		{
			if (packagesByUid != null)
			{
				return packagesByUid.Values.ToList();
			}
			return new List<VarPackage>();
		}

		/// <summary>Registered package count without allocating <see cref="GetPackages"/> list (UI tips / diag).</summary>
		public static int GetPackageCount()
		{
			return packagesByUid != null ? packagesByUid.Count : 0;
		}

		public static List<string> GetPackageUids()
		{
			List<string> list;
			if (packagesByUid != null)
			{
				list = packagesByUid.Keys.ToList();
				list.Sort();
			}
			else
			{
				list = new List<string>();
			}
			return list;
		}

		public static bool IsPackage(string packageUidOrPath)
		{
			if (packagesByUid != null && packagesByUid.ContainsKey(packageUidOrPath))
			{
				return true;
			}
			if (packagesByPath != null && packagesByPath.ContainsKey(packageUidOrPath))
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Canonical <c>Author.Name.version</c> from gallery index fields. Index sometimes stores full
		/// <c>AddonPackages/Author.Name.version.var</c> in <paramref name="storedPackageUid"/>; <c>packagesByUid</c> keys never use that form.
		/// Prefer filename from <paramref name="lastKnownVarPath"/> when present.
		/// </summary>
		private static string CanonicalPackageUidFromIndexedRow(string storedPackageUid, string lastKnownVarPath)
		{
			string fromPath = null;
			if (!string.IsNullOrEmpty(lastKnownVarPath))
			{
				try
				{
					string fn = Path.GetFileName(lastKnownVarPath.TrimEnd('/', '\\'));
					if (!string.IsNullOrEmpty(fn) && fn.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
						fromPath = fn.Substring(0, fn.Length - 4);
				}
				catch { }
			}
			if (!string.IsNullOrEmpty(fromPath))
				return fromPath;

			if (string.IsNullOrEmpty(storedPackageUid))
				return null;

			string s = storedPackageUid.Trim().Replace('\\', '/');
			try
			{
				if (s.IndexOf('/') >= 0)
					s = Path.GetFileName(s.TrimEnd('/', '\\'));
			}
			catch { }

			if (string.IsNullOrEmpty(s))
				return null;

			if (s.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
				s = s.Substring(0, s.Length - 4);
			else if (s.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				s = s.Substring(0, s.Length - 4);

			return string.IsNullOrEmpty(s) ? null : s;
		}

		/// <summary>
		/// Resolve a <see cref="VarPackage"/> for an indexed gallery row without assuming the on-disk path is still correct:
		/// prefer <c>packagesByUid</c>, then last-known path from the index, then <c>AddonPackages/</c> / <c>AllPackages/</c> by .var file name.
		/// </summary>
		public static bool TryResolveVarPackageForIndexedGalleryRow(string packageUid, string lastKnownVarPath, out VarPackage pkg)
		{
			pkg = null;
			string uidKey = CanonicalPackageUidFromIndexedRow(packageUid, lastKnownVarPath);
			if (string.IsNullOrEmpty(uidKey)) return false;

			lock (packagesLock)
			{
				if (packagesByUid != null && packagesByUid.TryGetValue(uidKey, out pkg) && pkg != null)
					return true;
			}

			if (!string.IsNullOrEmpty(lastKnownVarPath))
			{
				pkg = GetPackage(lastKnownVarPath, false);
				if (pkg != null && string.Equals(pkg.Uid, uidKey, StringComparison.OrdinalIgnoreCase))
					return true;
				pkg = null;
			}

			string fn = string.IsNullOrEmpty(lastKnownVarPath) ? null : Path.GetFileName(lastKnownVarPath.TrimEnd('/', '\\'));
			if (!string.IsNullOrEmpty(fn) && fn.IndexOf('.') >= 0)
			{
				pkg = GetPackage("AddonPackages/" + fn, false);
				if (pkg != null && string.Equals(pkg.Uid, uidKey, StringComparison.OrdinalIgnoreCase))
					return true;
				pkg = null;
				pkg = GetPackage("AllPackages/" + fn, false);
				if (pkg != null && string.Equals(pkg.Uid, uidKey, StringComparison.OrdinalIgnoreCase))
					return true;
				pkg = null;
			}

			// Legacy: stored UID column is a full path — try direct path resolve.
			if (!string.IsNullOrEmpty(packageUid))
			{
				string p = packageUid.Replace('\\', '/').Trim();
				if (p.IndexOf('/') >= 0 || p.EndsWith(".var", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				{
					pkg = GetPackage(p, false);
					if (pkg != null && string.Equals(pkg.Uid, uidKey, StringComparison.OrdinalIgnoreCase))
						return true;
					pkg = null;
				}
			}

			return false;
		}

		public static VarPackage GetPackage(string packageUidOrPath, bool ensureInstalled = true)
		{
			if (VamStartupOptimizations.TryShortCircuitAbsentVamXGetPackage(packageUidOrPath))
				return null;

			VarPackage TryResolve(string uidOrPath)
			{
				VarPackage resolved = null;
				Match match;
				if ((match = Regex.Match(uidOrPath, "^([^\\.]+\\.[^\\.]+)\\.latest$")).Success)
				{
					string value2 = match.Groups[1].Value;
					VarPackageGroup packageGroup = GetPackageGroup(value2);
					if (packageGroup != null)
					{
						resolved = packageGroup.NewestPackage;
					}
				}
				else if ((match = Regex.Match(uidOrPath, "^([^\\.]+\\.[^\\.]+)\\.min([0-9]+)$")).Success)
				{
					string value3 = match.Groups[1].Value;
					int requestVersion = int.Parse(match.Groups[2].Value);
					VarPackageGroup packageGroup2 = GetPackageGroup(value3);
					if (packageGroup2 != null)
					{
						resolved = packageGroup2.GetClosestMatchingPackageVersion(requestVersion, false, false);
					}
				}
				else if (packagesByUid != null && packagesByUid.ContainsKey(uidOrPath))
				{
					packagesByUid.TryGetValue(uidOrPath, out resolved);
				}
				else if (packagesByPath != null && packagesByPath.ContainsKey(uidOrPath))
				{
					packagesByPath.TryGetValue(uidOrPath, out resolved);
				}
				return resolved;
			}

			VarPackage value = TryResolve(packageUidOrPath);
			if (value == null && !string.IsNullOrEmpty(packageUidOrPath) && Regex.IsMatch(packageUidOrPath, "\\s"))
			{
				string normalized = Regex.Replace(packageUidOrPath, "\\s+", string.Empty);
				if (!string.Equals(normalized, packageUidOrPath, StringComparison.Ordinal))
				{
					value = TryResolve(normalized);
					try
					{
						if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
						{
							LogUtil.Log("GetPackage normalize whitespace | raw=" + packageUidOrPath + " | normalized=" + normalized + " | found=" + (value != null));
						}
					}
					catch { }
				}
			}

			if (value == null)
			{
				try
				{
					if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value
						&& VamStartupOptimizations.TryLogGetPackageNotFound(packageUidOrPath))
					{
						LogUtil.Log("GetPackage not found: " + packageUidOrPath);
					}
				}
				catch { }
			}

			if (value != null && ensureInstalled) EnsurePackageInstalled(value);
			return value;
		}

		private static void EnsurePackageInstalled(VarPackage package)
		{
			if (package == null) return;

			// Scan-whitelist: packages already in AddonPackages/ need no install/move work.
			// Do not promote them into VaM's FileManager from GetPackage(ensureInstalled) —
			// catalog plugins enumerate the whole library that way and would temp-whitelist
			// every package. VaM registration stays on-demand via entry-path hooks and
			// explicit preset/dependency callers.
			if (ScanWhitelistManager.Instance.IsEnabled)
			{
				string normPath = (package.Path ?? "").Replace('\\', '/');
				if (normPath.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)
					&& ScanWhitelistManager.Instance.IsPackageScanExcluded(package.Uid, normPath))
				{
					return;
				}
			}

			// Recursively install this package and its dependencies if needed.
			// InstallRecursive will return true if ANYTHING was moved (self or dependency).
			bool moved = package.InstallRecursive();
			if (moved)
			{
				LogUtil.Log($"[VPB] Dependencies installed/verified for: {package.Uid}");
				FileManagerBridge.Refresh("dependency_install", RefreshScope.Both);
			}
		}

		public static void ScheduleCoalescedNativeRefresh()
		{
			ScheduleMvrRefresh();
		}

		private static void ScheduleMvrRefresh()
		{
			if (singleton == null) return;
			if (singleton.m_MvrRefreshCo != null)
			{
				singleton.m_MvrRefreshPending = true;
				return;
			}
			singleton.m_MvrRefreshCo = singleton.StartCoroutine(singleton.MvrRefreshCo());
		}

		private IEnumerator MvrRefreshCo()
		{
			try
			{
				// Always yield once so VPB Init / Update are not blocked by native Refresh
				// (Unity 2018 main thread — sync Refresh was a multi-minute hang under scan whitelist + #12).
				yield return null;

				bool startupNotReady = !LogUtil.IsStartupReadyLogged() && !LogUtil.IsReadyLogged();
				if (startupNotReady)
					VamStartupProfiler.Milestone("mvr_native_refresh_delay_skipped (startup)");
				else
					yield return new WaitForSeconds(0.5f);
				var sw = Stopwatch.StartNew();
				VamStartupProfiler.Milestone("mvr_native_FileManager.Refresh_invoke_begin");
				VamOnDemandLoader.InvokeNativeFileManagerRefreshForDelayedMvr("mvr_delayed_native_refresh");
				sw.Stop();
				VamStartupProfiler.AddPhaseMs("mvr_delayed_native_refresh", sw.Elapsed.TotalMilliseconds);
				VamStartupProfiler.Milestone("mvr_delayed_native_refresh_done ms=" + sw.ElapsedMilliseconds);
			}
			finally
			{
				m_MvrRefreshCo = null;
				if (m_MvrRefreshPending)
				{
					m_MvrRefreshPending = false;
					m_MvrRefreshCo = StartCoroutine(MvrRefreshCo());
				}
			}
		}

		public static List<VarPackageGroup> GetPackageGroups()
		{
			if (packageGroups != null)
			{
				return packageGroups.Values.ToList();
			}
			return new List<VarPackageGroup>();
		}

		public static VarPackageGroup GetPackageGroup(string packageGroupUid)
		{
			VarPackageGroup value = null;
			if (packageGroups != null)
			{
				packageGroups.TryGetValue(packageGroupUid, out value);
			}
			return value;
		}

        public static string CleanFilePath(string path)
		{
			return path?.Replace('\\', '/');
		}

		// Pick the canonical path for a duplicate-UID pair. Preference order:
		// 1) AddonPackages over AllPackages (Addon is the live install location).
		// 2) Path under a junction/symlink folder (e.g. AddonPackages/NEW) over a flatter
		//    sibling path to the same files — otherwise Path list never shows that folder.
		// 3) Shorter path (closer to the package root) over deeper normal subfolders.
		private static string ChooseCanonicalDuplicatePath(string a, string b)
		{
			if (string.IsNullOrEmpty(a)) return b;
			if (string.IsNullOrEmpty(b)) return a;
			bool aAddon = a.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase);
			bool bAddon = b.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase);
			if (aAddon != bAddon) return aAddon ? a : b;

			bool aReparse = VarPathHasReparsePointDirectory(a);
			bool bReparse = VarPathHasReparsePointDirectory(b);
			if (aReparse != bReparse) return aReparse ? a : b;

			if (a.Length != b.Length) return a.Length < b.Length ? a : b;
			return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? a : b;
		}

		/// <summary>
		/// True when any directory segment of the .var path (below AddonPackages/AllPackages) is a
		/// junction/symlink. Used so duplicate resolution keeps those folder names in the Path list.
		/// </summary>
		private static bool VarPathHasReparsePointDirectory(string varPath)
		{
			try
			{
				string p = CleanFilePath(varPath);
				if (string.IsNullOrEmpty(p)) return false;
				int slash = p.LastIndexOf('/');
				if (slash <= 0) return false;
				string parent = p.Substring(0, slash);
				while (!string.IsNullOrEmpty(parent))
				{
					if (string.Equals(parent, "AddonPackages", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(parent, "AllPackages", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(parent, "Custom", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(parent, "Saves", StringComparison.OrdinalIgnoreCase))
						break;
					try
					{
						FileAttributes attr = File.GetAttributes(parent);
						if ((attr & FileAttributes.ReparsePoint) != 0)
							return true;
					}
					catch { }
					int s = parent.LastIndexOf('/');
					if (s <= 0) break;
					parent = parent.Substring(0, s);
				}
			}
			catch { }
			return false;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct BY_HANDLE_FILE_INFORMATION
		{
			public uint FileAttributes;
			public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
			public uint VolumeSerialNumber;
			public uint FileSizeHigh;
			public uint FileSizeLow;
			public uint NumberOfLinks;
			public uint FileIndexHigh;
			public uint FileIndexLow;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern SafeFileHandle CreateFile(
			string lpFileName,
			uint dwDesiredAccess,
			uint dwShareMode,
			IntPtr lpSecurityAttributes,
			uint dwCreationDisposition,
			uint dwFlagsAndAttributes,
			IntPtr hTemplateFile);

		private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
		private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
		private const uint OPEN_EXISTING = 3;

		private static bool TryGetWindowsFileId(string path, out string fileId)
		{
			return TryGetWindowsFileId(path, openReparsePoint: false, out fileId);
		}

		/// <summary>
		/// File index id for cycle/dedup. When <paramref name="openReparsePoint"/> is true, identifies the
		/// junction/symlink node itself (so each AddonPackages junction is scanned under its own path).
		/// When false, follows the reparse target (same-file dedup across links).
		/// </summary>
		private static bool TryGetWindowsFileId(string path, bool openReparsePoint, out string fileId)
		{
			fileId = null;
			try
			{
				if (string.IsNullOrEmpty(path))
					return false;

				// Fast path: Only check file ID if it's a reparse point (junction/symlink)
				// or if we really need it for deduplication. 
				// For most files, we can skip the expensive CreateFile call.
				var attr = File.GetAttributes(path);
				if ((attr & FileAttributes.ReparsePoint) == 0)
				{
					return false;
				}

				uint flags = FILE_FLAG_BACKUP_SEMANTICS;
				if (openReparsePoint)
					flags |= FILE_FLAG_OPEN_REPARSE_POINT;

				using (SafeFileHandle handle = CreateFile(
					path,
					0x80, // FILE_READ_ATTRIBUTES
					(uint)(FileShare.ReadWrite | FileShare.Delete),
					IntPtr.Zero,
					OPEN_EXISTING,
					flags,
					IntPtr.Zero))
				{
					if (handle.IsInvalid)
						return false;

					BY_HANDLE_FILE_INFORMATION info;
					if (!GetFileInformationByHandle(handle.DangerousGetHandle(), out info))
						return false;

					ulong index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
					fileId = info.VolumeSerialNumber.ToString("X8") + ":" + index.ToString("X16");
					return true;
				}
			}
			catch
			{
				return false;
			}
		}

		public static void SafeGetFiles(string path, string pattern, List<string> results)
		{
			SafeGetFiles(path, pattern, results, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		private static void SafeGetFiles(string path, string pattern, List<string> results, HashSet<string> visited)
		{
			try
			{
				string dirId;
				// Use the reparse node's own id (not the target). Following target ids skipped
				// sibling junctions that point at the same library — Path list lost those subfolders.
				if (TryGetWindowsFileId(path, openReparsePoint: true, out dirId))
				{
					if (!visited.Add(dirId))
					{
						return;
					}
				}

				string[] files = Directory.GetFiles(path, pattern);
				if (files != null)
					results.AddRange(files);

				string[] dirs = Directory.GetDirectories(path);
				if (dirs != null)
				{
					foreach (string dir in dirs)
					{
						// Skip InvalidPackages to avoid re-scanning rejected files
						if (Path.GetFileName(dir).Equals("InvalidPackages", StringComparison.OrdinalIgnoreCase)) continue;

						SafeGetFiles(dir, pattern, results, visited);
					}
				}
			}
			catch (Exception)
			{
				// Ignore access denied or other errors
			}
		}

		public static void SafeGetDirectories(string path, string pattern, List<string> results)
		{
			SafeGetDirectories(path, pattern, results, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		private static void SafeGetDirectories(string path, string pattern, List<string> results, HashSet<string> visited)
		{
			try
			{
				string dirId;
				if (TryGetWindowsFileId(path, openReparsePoint: true, out dirId))
				{
					if (!visited.Add(dirId))
					{
						return;
					}
				}

				string[] dirs = Directory.GetDirectories(path, pattern);
				if (dirs != null)
				{
					foreach (string dir in dirs)
					{
						results.Add(dir);
						SafeGetDirectories(dir, pattern, results, visited);
					}
				}
			}
			catch (Exception)
			{
				// Ignore
			}
		}

		public static bool CheckIfDirectoryChanged(string dir, DateTime previousCheckTime, bool recurse = true)
		{
			return CheckIfDirectoryChanged(dir, previousCheckTime, recurse, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		private static bool CheckIfDirectoryChanged(string dir, DateTime previousCheckTime, bool recurse, HashSet<string> visited)
		{
			if (Directory.Exists(dir))
			{
				string dirId;
				if (TryGetWindowsFileId(dir, out dirId))
				{
					if (!visited.Add(dirId))
					{
						return false;
					}
				}

				DateTime lastWriteTime = Directory.GetLastWriteTime(dir);
				if (lastWriteTime > previousCheckTime)
				{
					return true;
				}
				if (recurse)
				{
					string[] directories = Directory.GetDirectories(dir);
					foreach (string dir2 in directories)
					{
						if (CheckIfDirectoryChanged(dir2, previousCheckTime, recurse, visited))
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		public static void FindVarFiles(string dir, string pattern, List<FileEntry> foundFiles)
		{
			if (foundFiles == null || string.IsNullOrEmpty(pattern)) return;
			string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
			FindVarFilesRegex(dir, regex, foundFiles);
		}

		public static void FindVarFilesRegex(string dir, string regex, List<FileEntry> foundFiles)
		{
			if (foundFiles == null || string.IsNullOrEmpty(regex)) return;

			dir = CleanDirectoryPath(dir) ?? string.Empty;
			Regex rx;
			try
			{
				rx = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			}
			catch
			{
				return;
			}

			VarPackage[] snapshot = GetPackagesSnapshotForBrowse();
			if (snapshot == null) return;

			foreach (VarPackage pkg in snapshot)
			{
				if (pkg == null || !pkg.Enabled || pkg.invalid) continue;
				AppendVarFilesForPackage(pkg, dir, rx, foundFiles);
			}
		}

		public static bool FileExists(string path, bool onlySystemFiles = false, bool restrictPath = false)
		{
			if (string.IsNullOrEmpty(path)) return false;
			if (!onlySystemFiles && GetVarFileEntry(path) != null) return true;
			if (File.Exists(path))
			{
				if (restrictPath && !IsSecureReadPath(path))
				{
					throw new Exception("Attempted to check file existence for non-secure path " + path);
				}
				return true;
			}
			return false;
		}

		public static bool IsFileInPackage(string path)
		{
			string key = CleanFilePath(path);
			if (uidToVarFileEntry != null && uidToVarFileEntry.ContainsKey(key))
			{
				return true;
			}
			if (pathToVarFileEntry != null && pathToVarFileEntry.ContainsKey(key))
			{
				return true;
			}
			return false;
		}

		//public static bool IsHidden(string path, bool restrictPath = false)
		//{
		//	FileEntry fileEntry = GetVarFileEntry(path);
		//	if (fileEntry == null)
		//	{
		//		fileEntry = GetSystemFileEntry(path, restrictPath);
		//	}
		//	return fileEntry?.IsHidden() ?? false;
		//}

		//public static void SetHidden(string path, bool hide, bool restrictPath = false)
		//{
		//	FileEntry fileEntry = GetVarFileEntry(path);
		//	if (fileEntry == null)
		//	{
		//		fileEntry = GetSystemFileEntry(path, restrictPath);
		//	}
		//	fileEntry?.SetHidden(hide);
		//}

		public static FileEntry GetFileEntry(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetVarFileEntry(path);
			if (fileEntry == null)
			{
				fileEntry = GetSystemFileEntry(path, restrictPath);
			}
			return fileEntry;
		}

		public static SystemFileEntry GetSystemFileEntry(string path, bool restrictPath = false)
		{
			SystemFileEntry result = null;
			if (File.Exists(path))
			{
				if (restrictPath && !IsSecureReadPath(path))
				{
					throw new Exception("Attempted to get file entry for non-secure path " + path);
				}
				result = new SystemFileEntry(path);
			}
			return result;
		}

		public static VarFileEntry GetVarFileEntry(string path)
		{
			VarFileEntry value = null;
			string key = CleanFilePath(path);
			if ((uidToVarFileEntry != null && uidToVarFileEntry.TryGetValue(key, out value))
				|| (pathToVarFileEntry != null && pathToVarFileEntry.TryGetValue(key, out value)))
			{
                return value;
			}

			if (key != null)
			{
				int colonIdx = key.IndexOf(":/");
				if (colonIdx > 0 && colonIdx + 2 < key.Length)
				{
					string pkgPath = key.Substring(0, colonIdx);
					string internalPath = key.Substring(colonIdx + 2);
					if (internalPath.Length > 0 && internalPath[0] == '/') internalPath = internalPath.Substring(1);

					VarPackage pkg = GetPackage(pkgPath, false);
					if (pkg == null)
					{
						string pkgName = Path.GetFileName(pkgPath);
						if (!string.IsNullOrEmpty(pkgName))
						{
							pkg = GetPackage("AddonPackages/" + pkgName, false);
							if (pkg == null) pkg = GetPackage("AllPackages/" + pkgName, false);
						}
					}

					if (pkg != null && pkg.ZipFile == null)
					{
						VarFileEntry cachedEntry;
						if (pkg.TryCreateVarFileEntryFromCache(internalPath, out cachedEntry))
						{
							value = cachedEntry;
							TryRegisterVarFileEntryLookup(value);
							return value;
						}
					}

					if (pkg != null && pkg.ZipFile != null)
					{
						try
						{
							ZipEntry ze = pkg.ZipFile.GetEntry(internalPath);
							if (ze != null)
							{
								value = new VarFileEntry(pkg, ze.Name, ze.DateTime, ze.Size);
								TryRegisterVarFileEntryLookup(value);
								return value;
							}
						}
						catch { }
					}
				}
			}

			return null;
		}

		public static void SortFileEntriesByLastWriteTime(List<FileEntry> fileEntries)
		{
			fileEntries.Sort((FileEntry e1, FileEntry e2) => e1.LastWriteTime.CompareTo(e2.LastWriteTime));
		}

		public static string CleanDirectoryPath(string path)
		{
			if (path != null)
			{
				string input = path.Replace('\\', '/');
				return Regex.Replace(input, "/$", string.Empty);
			}
			return null;
		}

	public static int FolderContentsCount(string path)
	{
		return FolderContentsCount(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
	}

	private static int FolderContentsCount(string path, HashSet<string> visited)
	{
		// Cycle guard: identify the junction/symlink node itself (not the target) so sibling
		// links to the same library are still counted under their own paths.
		try
		{
			string dirId;
			if (TryGetWindowsFileId(path, openReparsePoint: true, out dirId) && !visited.Add(dirId))
				return 0;
		}
		catch { }

		// Count files in this directory, tolerating access-denied or other errors.
		int num = 0;
		try { num = Directory.GetFiles(path).Length; } catch { }

		// Recurse into each subdirectory independently so an error in one branch
		// does not zero-out the count for sibling directories.
		string[] dirs = null;
		try { dirs = Directory.GetDirectories(path); } catch { }
		if (dirs != null)
		{
			foreach (string path2 in dirs)
			{
				num += FolderContentsCount(path2, visited);
			}
		}
		return num;
	}

	public static bool DirectoryExists(string path, bool onlySystemDirectories = false, bool restrictPath = false)
	{
		if (string.IsNullOrEmpty(path)) return false;
		return Directory.Exists(path);
	}

		public static bool IsDirectoryInPackage(string path)
		{
			//string key = CleanDirectoryPath(path);
			//if (uidToVarDirectoryEntry != null && uidToVarDirectoryEntry.ContainsKey(key))
			//{
			//	return true;
			//}
			//if (pathToVarDirectoryEntry != null && pathToVarDirectoryEntry.ContainsKey(key))
			//{
			//	return true;
			//}
			return false;
		}

		public static VarDirectoryEntry GetVarDirectoryEntry(string path)
		{
			VarDirectoryEntry value = null;
			//string key = CleanDirectoryPath(path);
			//if ((uidToVarDirectoryEntry != null && uidToVarDirectoryEntry.TryGetValue(key, out value)) 
			//	|| pathToVarDirectoryEntry == null || pathToVarDirectoryEntry.TryGetValue(key, out value))
			//{
			//}
			return value;
		}

		public static VarDirectoryEntry GetVarRootDirectoryEntryFromPath(string path)
		{
			VarDirectoryEntry value = null;
			//if (varPackagePathToRootVarDirectory != null)
			//{
			//	varPackagePathToRootVarDirectory.TryGetValue(path, out value);
			//}
			return value;
		}

		public static void CreateDirectory(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!DirectoryExists(path))
			{
				//if (!IsSecureWritePath(path))
				//{
				//	throw new Exception("Attempted to create directory at non-secure path " + path);
				//}
				Directory.CreateDirectory(path);
			}
		}
		public static void DeleteDirectory(string path, bool recursive = false)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (DirectoryExists(path))
			{
				if (!IsSecureWritePath(path))
				{
					throw new Exception("Attempted to delete file at non-secure path " + path);
				}
				Directory.Delete(path, recursive);
			}
		}
		public static void MoveDirectory(string oldPath, string newPath)
		{
			oldPath = ConvertSimulatedPackagePathToNormalPath(oldPath);
			if (!IsSecureWritePath(oldPath))
			{
				throw new Exception("Attempted to move directory from non-secure path " + oldPath);
			}
			newPath = ConvertSimulatedPackagePathToNormalPath(newPath);
			if (!IsSecureWritePath(newPath))
			{
				throw new Exception("Attempted to move directory to non-secure path " + newPath);
			}
			Directory.Move(oldPath, newPath);
		}
		public static FileEntryStream OpenStream(FileEntry fe)
		{
			if (fe == null)
			{
				throw new Exception("Null FileEntry passed to OpenStreamReader");
			}
			if (fe is VarFileEntry)
			{
				return new VarFileEntryStream(fe as VarFileEntry);
			}
			if (fe is SystemFileEntry)
			{
				return new SystemFileEntryStream(fe as SystemFileEntry);
			}
			throw new Exception("Unknown FileEntry class passed to OpenStreamReader");
		}

		public static FileEntryStream OpenStream(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return OpenStream(fileEntry);
		}

		public static FileEntryStreamReader OpenStreamReader(FileEntry fe)
		{
			if (fe == null)
			{
				throw new Exception("Null FileEntry passed to OpenStreamReader");
			}
			if (fe is VarFileEntry)
			{
				return new VarFileEntryStreamReader(fe as VarFileEntry);
			}
			if (fe is SystemFileEntry)
			{
				return new SystemFileEntryStreamReader(fe as SystemFileEntry);
			}
			throw new Exception("Unknown FileEntry class passed to OpenStreamReader");
		}

		public static FileEntryStreamReader OpenStreamReader(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return OpenStreamReader(fileEntry);
		}

		public static IEnumerator ReadAllBytesCoroutine(FileEntry fe, byte[] result)
		{
			Thread loadThread = new Thread((ThreadStart)delegate
			{
				byte[] buffer = new byte[32768];
				using (FileEntryStream fileEntryStream = OpenStream(fe))
				{
					using (MemoryStream destination = new MemoryStream(result))
					{
						StreamUtils.Copy(fileEntryStream.Stream, destination, buffer);
					}
				}
			});
			loadThread.Start();
			while (loadThread.IsAlive)
			{
				yield return null;
			}
		}

		public static byte[] ReadAllBytes(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return ReadAllBytes(fileEntry);
		}

		public static byte[] ReadAllBytes(FileEntry fe)
		{
			if (fe is VarFileEntry)
			{
				byte[] buffer = new byte[32768];
				using (FileEntryStream fileEntryStream = OpenStream(fe))
				using (MemoryStream destination = new MemoryStream())
				{
					StreamUtils.Copy(fileEntryStream.Stream, destination, buffer);
					if (destination.Length > int.MaxValue)
						throw new IOException("VAR entry too large for " + (fe.Uid ?? fe.Path ?? ""));
					return destination.ToArray();
				}
			}
			return File.ReadAllBytes(fe.Path);
		}

		public static string ReadAllText(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return ReadAllText(fileEntry);
		}

		public static string ReadAllText(FileEntry fe)
		{
			using (FileEntryStreamReader fileEntryStreamReader = OpenStreamReader(fe))
			{
				return fileEntryStreamReader.ReadToEnd();
			}
		}

		public static FileStream OpenStreamForCreate(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to open stream for create at non-secure path " + path);
			}
			return File.Open(path, FileMode.Create);
		}

		public static StreamWriter OpenStreamWriter(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to open stream writer at non-secure path " + path);
			}
			return new StreamWriter(path);
		}

		public static void WriteAllText(string path, string text)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to write all text at non-secure path " + path);
			}
			File.WriteAllText(path, text);
		}

		public static void WriteAllBytes(string path, byte[] bytes)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to write all bytes at non-secure path " + path);
			}
			File.WriteAllBytes(path, bytes);
		}

		public static void SetFileAttributes(string path, FileAttributes attrs)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to set file attributes at non-secure path " + path);
			}
			File.SetAttributes(path, attrs);
		}

		public static void DeleteFile(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (File.Exists(path))
			{
				if (!IsSecureWritePath(path))
				{
					throw new Exception("Attempted to delete file at non-secure path " + path);
				}
				File.Delete(path);
			}
		}

		protected static void DoFileCopy(string oldPath, string newPath)
		{
			FileEntry fileEntry = GetFileEntry(oldPath);
			if (fileEntry != null && fileEntry is VarFileEntry)
			{
				byte[] buffer = new byte[4096];
				using (FileEntryStream fileEntryStream = OpenStream(fileEntry))
				{
					using (FileStream destination = OpenStreamForCreate(newPath))
					{
						StreamUtils.Copy(fileEntryStream.Stream, destination, buffer);
					}
				}
			}
			else
			{
				File.Copy(oldPath, newPath);
			}
		}

		public static void CopyFile(string oldPath, string newPath, bool restrictPath = false)
		{
			oldPath = ConvertSimulatedPackagePathToNormalPath(oldPath);
			if (restrictPath && !IsSecureReadPath(oldPath))
			{
				throw new Exception("Attempted to copy file from non-secure path " + oldPath);
			}
			newPath = ConvertSimulatedPackagePathToNormalPath(newPath);
			if (!IsSecureWritePath(newPath))
			{
				throw new Exception("Attempted to copy file to non-secure path " + newPath);
			}
			DoFileCopy(oldPath, newPath);
		}


		protected static void DoFileMove(string oldPath, string newPath, bool overwrite = true)
		{
			if (File.Exists(newPath))
			{
				if (!overwrite)
				{
					throw new Exception("File " + newPath + " exists. Cannot move into");
				}
				File.Delete(newPath);
			}
			File.Move(oldPath, newPath);
		}

		public static void MoveFile(string oldPath, string newPath, bool overwrite = true)
		{
			oldPath = ConvertSimulatedPackagePathToNormalPath(oldPath);
			if (!IsSecureWritePath(oldPath))
			{
				throw new Exception("Attempted to move file from non-secure path " + oldPath);
			}
			newPath = ConvertSimulatedPackagePathToNormalPath(newPath);
			if (!IsSecureWritePath(newPath))
			{
				throw new Exception("Attempted to move file to non-secure path " + newPath);
			}
			DoFileMove(oldPath, newPath, overwrite);
		}

		private void Awake()
		{
			singleton = this;
		}

		private void OnDestroy()
		{
			ClearAll();
		}
	}

}
