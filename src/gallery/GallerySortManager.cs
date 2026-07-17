using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using SimpleJSON;

namespace VPB
{
    public enum SortType
    {
        // IMPORTANT: explicit integer values are persisted in cache files and are also used in
        // snapshot-cache keys. Keep existing values stable and append new values at the end.
        Name = 0,
        Date = 1,
        Size = 2,
        Count = 3,
        Score = 4,
        Rating = 5,
        Deps = 6,
        Dependents = 7,
        Missing = 8,
        Hidden = 9,
        HiddenOnly = 10,
        AutoInstall = 11,
        AutoInstallOnly = 12,
        /// <summary>File / package creation time (not last modified).</summary>
        DateCreated = 13,
        /// <summary>Show only loaded packages (AddonPackages/ + Custom/ + Saves/); fast-path uses SQLite <c>pkg.loaded</c>.</summary>
        LoadedOnly = 14,
        /// <summary>Show only unloaded packages (e.g. AllPackages/); fast-path uses SQLite <c>pkg.loaded</c>.</summary>
        UnloadedOnly = 15,
        /// <summary>Local "used" counter (scene launches + clothing applies).</summary>
        UsageCount = 16,
        /// <summary>Show only items with zero local usage.</summary>
        UnusedOnly = 17,
        /// <summary>Family first added: MIN(first_scanned) across all .var versions sharing creator.packageName.</summary>
        DateAdded = 18,
        /// <summary>Family last updated: first_scanned of the highest-N .var version in creator.packageName.</summary>
        DateUpdated = 19,
        /// <summary>Random order (Fisher–Yates shuffle each time sort is applied).</summary>
        Random = 20
    }

    public enum SortDirection
    {
        Ascending,
        Descending
    }

    [Serializable]
    public class SortState
    {
        public SortType Type = SortType.Name;
        public SortDirection Direction = SortDirection.Ascending;

        public SortState() { }
        public SortState(SortType type, SortDirection direction)
        {
            Type = type;
            Direction = direction;
        }

        public SortState Clone()
        {
            return new SortState(Type, Direction);
        }
    }

    public class GallerySortManager
    {
        private static GallerySortManager _instance;
        public static GallerySortManager Instance
        {
            get
            {
                if (_instance == null) _instance = new GallerySortManager();
                return _instance;
            }
        }

        private GallerySortCache cache;

        // Cache for scene dependencies to avoid re-parsing on every access
        private static Dictionary<string, HashSet<string>> _sceneDependencyCache = new Dictionary<string, HashSet<string>>();

        // Background warmer: at-most-one running task that pre-populates the SQLite loose-deps cache.
        // Writes DB only — never touches _sceneDependencyCache — so it cannot race with main-thread binds.
        private static int _looseDepsWarmRunning;
        private static readonly string[] LooseDepsWarmRoots = { "Saves", "Custom" };

        /// <summary>
        /// Kick off (or skip-if-running) a background pass that fills <c>loose_deps</c> for every loose <c>.json</c>
        /// under <c>Saves/</c> and <c>Custom/</c>. Bind reads stay O(1) after this finishes, even for Timeline-heavy
        /// scene files. Safe to call from any thread; no-op when an extraction pass is already in flight.
        /// </summary>
        public static void StartBackgroundWarmLooseDepsCache()
        {
            if (Interlocked.CompareExchange(ref _looseDepsWarmRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { WarmLooseDepsCacheCore(); }
                catch (Exception ex) { LogUtil.LogError("[VPB] Loose-deps warm failed: " + ex); }
                finally { Interlocked.Exchange(ref _looseDepsWarmRunning, 0); }
            });
        }

        private static void WarmLooseDepsCacheCore()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int seen = 0, hit = 0, written = 0;
            var probe = new HashSet<string>();
            for (int r = 0; r < LooseDepsWarmRoots.Length; r++)
            {
                string root = LooseDepsWarmRoots[r];
                if (!Directory.Exists(root)) continue;
                var files = new List<string>();
                try { FileManager.SafeGetFiles(root, "*.json", files); } catch { continue; }
                for (int i = 0; i < files.Count; i++)
                {
                    string path = files[i];
                    if (string.IsNullOrEmpty(path)) continue;
                    seen++;
                    try
                    {
                        var fi = new FileInfo(path);
                        if (!fi.Exists) continue;
                        long wt = fi.LastWriteTimeUtc.ToBinary();
                        long sz = fi.Length;

                        probe.Clear();
                        if (VpbLocalDatabase.TryReadLooseSceneDeps(path, wt, sz, probe))
                        {
                            hit++;
                            continue;
                        }

                        var deps = DependencyExtractor.ExtractDependenciesFromFile(path, 150, 1500);
                        VpbLocalDatabase.WriteLooseSceneDeps(path, wt, sz, deps ?? new HashSet<string>());
                        written++;

                        // Throttle so a Timeline-laden library doesn't burn a CPU core for minutes straight.
                        if ((written & 7) == 0) Thread.Sleep(10);
                    }
                    catch { }
                }
            }
            LogUtil.Log("[VPB] Loose-deps warm DONE | seen=" + seen + " | hit=" + hit + " | written=" + written + " | ms=" + sw.ElapsedMilliseconds);
        }

        public GallerySortManager()
        {
            cache = new GallerySortCache();
        }

        public void SortFiles(List<FileEntry> files, SortState state)
        {
            if (files == null || state == null) return;

            ApplyHideOldVersionsFilter(files);

            switch (state.Type)
            {
                case SortType.Name:
                    if (state.Direction == SortDirection.Ascending)
                        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    else
                        files.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.Date:
                    files.Sort((a, b) => {
                        int res = (state.Direction == SortDirection.Ascending) 
                            ? a.LastWriteTime.CompareTo(b.LastWriteTime)
                            : b.LastWriteTime.CompareTo(a.LastWriteTime);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
                case SortType.DateCreated:
                    SortByPrecomputedDateTime(files, GetSortCreationTime, state.Direction);
                    break;
                case SortType.Size:
                    files.Sort((a, b) => {
                        int res = (state.Direction == SortDirection.Ascending)
                            ? a.Size.CompareTo(b.Size)
                            : b.Size.CompareTo(a.Size);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
                case SortType.Rating:
                    SortByPrecomputedInt(files, f => RatingsManager.Instance.GetRating(f), state.Direction);
                    break;
                case SortType.Deps:
                    // IMPORTANT: GetDepsCount for scene JSON may stream-scan the file. Never call it from the comparer.
                    SortByPrecomputedInt(files, GetDepsCount, state.Direction);
                    break;
                case SortType.Dependents:
                    SortByPrecomputedInt(files, GetDependentsCount, state.Direction);
                    break;
                case SortType.Missing:
                    // IMPORTANT: GetMissingDepsCount may scan dependencies. Never call it from the comparer.
                    SortByPrecomputedInt(files, GetMissingDepsCount, state.Direction);
                    break;
                case SortType.Hidden:
                    SortByPrecomputedInt(files, f => PackageHidePrefs.IsGalleryHideBadgeVisible(f) ? 1 : 0, state.Direction);
                    break;
                case SortType.HiddenOnly:
                case SortType.AutoInstallOnly:
                case SortType.LoadedOnly:
                case SortType.UnloadedOnly:
                case SortType.UnusedOnly:
                    if (state.Direction == SortDirection.Ascending)
                        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    else
                        files.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.AutoInstall:
                    SortByPrecomputedInt(files, f => f != null && f.IsAutoInstall() ? 1 : 0, state.Direction);
                    break;
                case SortType.UsageCount:
                    SortByUsageCount(files, state.Direction);
                    break;
                case SortType.DateAdded:
                {
                    var fam = BuildFamilyScanTimes(files);
                    LogFamilyScanTimesSummary("DateAdded", state.Direction, files, fam);
                    SortByPrecomputedDateTime(files, f => GetFamilyMinScanned(f, fam), state.Direction);
                    LogSortedHeadSample("DateAdded", state.Direction, files, f => GetFamilyMinScanned(f, fam));
                    break;
                }
                case SortType.DateUpdated:
                {
                    var fam = BuildFamilyScanTimes(files);
                    LogFamilyScanTimesSummary("DateUpdated", state.Direction, files, fam);
                    SortByPrecomputedDateTime(files, f => GetFamilyHighestVersionScanned(f, fam), state.Direction);
                    LogSortedHeadSample("DateUpdated", state.Direction, files, f => GetFamilyHighestVersionScanned(f, fam));
                    break;
                }
                case SortType.Random:
                    ShuffleFiles(files);
                    break;
            }
        }

        /// <summary>Fisher–Yates shuffle; safe on worker threads (uses <see cref="System.Random"/>).</summary>
        public static void ShuffleFiles(List<FileEntry> files)
        {
            if (files == null || files.Count < 2) return;
            var rng = new System.Random();
            for (int i = files.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                FileEntry tmp = files[i];
                files[i] = files[j];
                files[j] = tmp;
            }
        }

        /// <summary>Triple-check logging for the new family-aware sorts. Flip to false to silence.</summary>
        private static bool LogFamilySortDiagnostics = false;

        private static void LogFamilyScanTimesSummary(string label, SortDirection dir, List<FileEntry> files, Dictionary<string, FamilyScanTimes> fam)
        {
            if (!LogFamilySortDiagnostics) return;
            try
            {
                int rowCount = files != null ? files.Count : 0;
                int famCount = fam != null ? fam.Count : 0;
                LogUtil.LogWarning("[VPB] SORT_" + label + " dir=" + dir + " rows=" + rowCount + " families=" + famCount);

                if (fam == null || famCount == 0) return;
                bool descending = dir == SortDirection.Descending;
                IEnumerable<KeyValuePair<string, FamilyScanTimes>> ordered = descending
                    ? fam.OrderByDescending(kv => label == "DateAdded" ? kv.Value.MinScanned : kv.Value.HighestVersionScanned)
                    : fam.OrderBy(kv => label == "DateAdded" ? kv.Value.MinScanned : kv.Value.HighestVersionScanned);

                int shown = 0;
                foreach (var kv in ordered)
                {
                    if (shown++ >= 15) break;
                    var v = kv.Value;
                    LogUtil.LogWarning("[VPB] SORT_" + label + " family=" + kv.Key
                        + " min=" + (v.MinScanned == DateTime.MinValue ? "n/a" : v.MinScanned.ToString("yyyy-MM-dd HH:mm:ss"))
                        + " highestV=" + v.HighestVersion
                        + " highestVScanned=" + (v.HighestVersionScanned == DateTime.MinValue ? "n/a" : v.HighestVersionScanned.ToString("yyyy-MM-dd HH:mm:ss")));
                }
                if (famCount > shown) LogUtil.LogWarning("[VPB] SORT_" + label + " ... (" + (famCount - shown) + " more families)");
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] SORT_" + label + " summary log failed: " + ex.Message); }
        }

        private static void LogSortedHeadSample(string label, SortDirection dir, List<FileEntry> files, Func<FileEntry, DateTime> getKey)
        {
            if (!LogFamilySortDiagnostics) return;
            try
            {
                if (files == null || files.Count == 0) return;
                int take = Math.Min(10, files.Count);
                LogUtil.LogWarning("[VPB] SORT_" + label + " head dir=" + dir + " sample(top " + take + "):");
                for (int i = 0; i < take; i++)
                {
                    var f = files[i];
                    string uid = f is VarFileEntry vfe ? vfe.GetRowPackageUid() : (f != null ? f.Name : "(null)");
                    DateTime k = DateTime.MinValue;
                    try { k = getKey(f); } catch { }
                    LogUtil.LogWarning("[VPB] SORT_" + label + "   [" + i + "] " + uid + " key=" + (k == DateTime.MinValue ? "n/a" : k.ToString("yyyy-MM-dd HH:mm:ss")));
                }
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] SORT_" + label + " head sample log failed: " + ex.Message); }
        }

        private static void SortByUsageCount(List<FileEntry> files, SortDirection dir)
        {
            if (files == null || files.Count < 2) return;

            var keys = new List<string>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                keys.Add(VpbLocalDatabase.BuildUsageKey(files[i]));
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try { VpbLocalDatabase.TryReadItemUseCountsForKeys(keys, counts); } catch { counts.Clear(); }

            SortByPrecomputedInt(files, f =>
            {
                string k = VpbLocalDatabase.BuildUsageKey(f);
                if (string.IsNullOrEmpty(k)) return 0;
                return counts.TryGetValue(k, out int c) ? c : 0;
            }, dir);
        }

        private struct SortKeyInt
        {
            public FileEntry File;
            public int Key;
            public string Name;
        }

        private struct SortKeyDateTime
        {
            public FileEntry File;
            public DateTime Key;
            public string Name;
        }

        private static void SortByPrecomputedInt(List<FileEntry> files, Func<FileEntry, int> getKey, SortDirection dir)
        {
            if (files == null || files.Count < 2) return;
            if (getKey == null) return;

            var list = new List<SortKeyInt>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                int k = 0;
                try { k = getKey(f); } catch { k = 0; }
                list.Add(new SortKeyInt { File = f, Key = k, Name = f != null ? (f.Name ?? "") : "" });
            }

            if (dir == SortDirection.Ascending)
            {
                list.Sort((a, b) =>
                {
                    int res = a.Key.CompareTo(b.Key);
                    if (res != 0) return res;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                list.Sort((a, b) =>
                {
                    int res = b.Key.CompareTo(a.Key);
                    if (res != 0) return res;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }

            for (int i = 0; i < files.Count; i++)
                files[i] = list[i].File;
        }

        private static void SortByPrecomputedDateTime(List<FileEntry> files, Func<FileEntry, DateTime> getKey, SortDirection dir)
        {
            if (files == null || files.Count < 2) return;
            if (getKey == null) return;

            var list = new List<SortKeyDateTime>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                DateTime k = DateTime.MinValue;
                try { k = getKey(f); } catch { k = DateTime.MinValue; }
                list.Add(new SortKeyDateTime { File = f, Key = k, Name = f != null ? (f.Name ?? "") : "" });
            }

            if (dir == SortDirection.Ascending)
            {
                list.Sort((a, b) =>
                {
                    int res = a.Key.CompareTo(b.Key);
                    if (res != 0) return res;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                list.Sort((a, b) =>
                {
                    int res = b.Key.CompareTo(a.Key);
                    if (res != 0) return res;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }

            for (int i = 0; i < files.Count; i++)
                files[i] = list[i].File;
        }

        /// <summary>
        /// Sort using only fields already on <see cref="FileEntry"/> / package metadata (no Unity singletons, no disk I/O).
        /// Used from a background thread after SQLite bulk list build so the main thread can skip <see cref="SortFiles"/>.
        /// </summary>
        /// <returns><c>true</c> if the list was sorted (or trivially needs no sort); <c>false</c> if the caller must run <see cref="SortFiles"/> on the main thread.</returns>
        public static bool TrySortFilesEntryFieldsOnly(List<FileEntry> files, SortState state)
        {
            if (files == null || state == null) return false;
            if (files.Count < 2) return true;

            ApplyHideOldVersionsFilter(files);
            if (files.Count < 2) return true;

            switch (state.Type)
            {
                case SortType.Name:
                case SortType.HiddenOnly:
                case SortType.AutoInstallOnly:
                case SortType.LoadedOnly:
                case SortType.UnloadedOnly:
                    if (state.Direction == SortDirection.Ascending)
                        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    else
                        files.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    return true;
                case SortType.Date:
                    files.Sort((a, b) => {
                        int res = (state.Direction == SortDirection.Ascending)
                            ? a.LastWriteTime.CompareTo(b.LastWriteTime)
                            : b.LastWriteTime.CompareTo(a.LastWriteTime);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    return true;
                case SortType.Size:
                    files.Sort((a, b) => {
                        int res = (state.Direction == SortDirection.Ascending)
                            ? a.Size.CompareTo(b.Size)
                            : b.Size.CompareTo(a.Size);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    return true;
                case SortType.DateCreated:
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (!(files[i] is VarFileEntry)) return false;
                    }
                    files.Sort((a, b) => {
                        DateTime ca = GetSortCreationTimeVarOnly(a as VarFileEntry);
                        DateTime cb = GetSortCreationTimeVarOnly(b as VarFileEntry);
                        int res = (state.Direction == SortDirection.Ascending)
                            ? ca.CompareTo(cb)
                            : cb.CompareTo(ca);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    return true;
                case SortType.DateAdded:
                case SortType.DateUpdated:
                {
                    var fam = BuildFamilyScanTimes(files);
                    bool asc = state.Direction == SortDirection.Ascending;
                    bool useMin = state.Type == SortType.DateAdded;
                    files.Sort((a, b) =>
                    {
                        DateTime ka = useMin ? GetFamilyMinScanned(a, fam) : GetFamilyHighestVersionScanned(a, fam);
                        DateTime kb = useMin ? GetFamilyMinScanned(b, fam) : GetFamilyHighestVersionScanned(b, fam);
                        int res = asc ? ka.CompareTo(kb) : kb.CompareTo(ka);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    return true;
                }
                case SortType.Random:
                    ShuffleFiles(files);
                    return true;
                default:
                    return false;
            }
        }

        private static DateTime GetSortCreationTimeVarOnly(VarFileEntry vfe)
        {
            if (vfe == null) return DateTime.MinValue;
            DateTime fromIndex;
            if (vfe.TryGetGalleryIndexedPackageCreationTime(out fromIndex))
                return fromIndex;
            try
            {
                if (vfe.Package != null)
                {
                    try
                    {
                        long ib = vfe.Package.InternalCreationTimeBinary;
                        if (ib != long.MinValue) return DateTime.FromBinary(ib);
                    }
                    catch { }
                    return vfe.Package.CreationTime;
                }
            }
            catch { }
            return DateTime.MinValue;
        }

        private struct FamilyScanTimes
        {
            public DateTime MinScanned;
            public int HighestVersion;
            public DateTime HighestVersionScanned;
        }

        /// <summary>Family key "creator.packageName" derived from uid prefix (uid format: "Creator.Package.N").</summary>
        private static string ComputeFamilyKey(FileEntry file)
        {
            if (file == null) return null;
            string uid = null;
            if (file is VarFileEntry vfe)
            {
                uid = vfe.GetRowPackageUid();
            }
            else if (file is PackageListEntry ple)
            {
                uid = ple.GetPackageUidForGalleryUserTags();
                if (string.IsNullOrEmpty(uid) && ple.Package != null) uid = ple.Package.Uid;
            }
            else if (file is SystemFileEntry sfe)
            {
                // Loose files have no version family. Treat each path as its own singleton family so
                // DateAdded/DateUpdated key off the file's own date instead of collapsing to DateTime.MinValue.
                string p = sfe.Path ?? "";
                return p.Length == 0 ? null : "sys:" + p;
            }
            if (string.IsNullOrEmpty(uid)) return null;
            int lastDot = uid.LastIndexOf('.');
            if (lastDot <= 0) return uid;
            return uid.Substring(0, lastDot);
        }

        /// <summary>
        /// Convert to UTC kind before sort. DateTime.CompareTo compares Ticks ignoring Kind, so a
        /// Local-kind value sorts the local UTC offset ahead of any UTC-kind value at the same
        /// wall-clock minute; converting realigns Ticks so the comparison reflects the actual moment.
        /// </summary>
        private static DateTime NormalizeToUtcForCompare(DateTime dt)
        {
            if (dt == DateTime.MinValue) return dt;
            if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
            if (dt.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return dt;
        }

        /// <summary>Per-row indexed first_scanned, with VarPackage fallback. Returns DateTime.MinValue when unknown.</summary>
        private static DateTime GetIndexedFirstScannedForFile(FileEntry file)
        {
            if (file == null) return DateTime.MinValue;
            DateTime dt;
            if (file is VarFileEntry vfe)
            {
                if (vfe.TryGetGalleryIndexedFirstScanned(out dt)) return NormalizeToUtcForCompare(dt);
                try
                {
                    if (vfe.Package != null && vfe.Package.FirstScannedBinary != long.MinValue && vfe.Package.FirstScannedBinary != 0L)
                        return NormalizeToUtcForCompare(DateTime.FromBinary(vfe.Package.FirstScannedBinary));
                }
                catch { }
                return DateTime.MinValue;
            }
            if (file is PackageListEntry ple)
            {
                if (ple.TryGetGalleryIndexedFirstScanned(out dt)) return NormalizeToUtcForCompare(dt);
                try
                {
                    if (ple.Package != null && ple.Package.FirstScannedBinary != long.MinValue && ple.Package.FirstScannedBinary != 0L)
                        return NormalizeToUtcForCompare(DateTime.FromBinary(ple.Package.FirstScannedBinary));
                }
                catch { }
            }
            if (file is SystemFileEntry sfe)
            {
                // No first_scanned persistence for loose files. Mirror BA's LocalRVGE: prefer ctime
                // (when did this file land on disk), fall back to mtime. Gives loose .vap meaningful
                // per-file dates instead of all collapsing to MinValue.
                try
                {
                    DateTime ct = FileStat.GetCreationTimeOrMin(sfe.Path);
                    if (ct != DateTime.MinValue) return NormalizeToUtcForCompare(ct);
                }
                catch { }
                return NormalizeToUtcForCompare(sfe.LastWriteTime);
            }
            return DateTime.MinValue;
        }

        /// <summary>Highest VAR version (N from "Creator.Package.N") for this row's uid; 0 when unknown.</summary>
        private static int GetUidVersionNumber(FileEntry file)
        {
            if (file == null) return 0;
            string uid = null;
            if (file is VarFileEntry vfe) uid = vfe.GetRowPackageUid();
            else if (file is PackageListEntry ple)
            {
                uid = ple.GetPackageUidForGalleryUserTags();
                if (string.IsNullOrEmpty(uid) && ple.Package != null) uid = ple.Package.Uid;
            }
            if (string.IsNullOrEmpty(uid)) return 0;
            int lastDot = uid.LastIndexOf('.');
            if (lastDot < 0 || lastDot >= uid.Length - 1) return 0;
            int v;
            return int.TryParse(uid.Substring(lastDot + 1), out v) ? v : 0;
        }

        /// <summary>Build per-family (creator.package) scan-time lookup over the current file list. One pass, no I/O.</summary>
        private static Dictionary<string, FamilyScanTimes> BuildFamilyScanTimes(List<FileEntry> files)
        {
            var map = new Dictionary<string, FamilyScanTimes>(StringComparer.OrdinalIgnoreCase);
            if (files == null) return map;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string famKey = ComputeFamilyKey(f);
                if (string.IsNullOrEmpty(famKey)) continue;

                // Loose files are singleton families: ctime = DateAdded, mtime = DateUpdated.
                // Without this split, re-saved scenes/presets wouldn't float to the top under DateUpdated.
                if (f is SystemFileEntry sfe)
                {
                    DateTime added = DateTime.MinValue;
                    try { added = FileStat.GetCreationTimeOrMin(sfe.Path); } catch { }
                    DateTime updated = sfe.LastWriteTime;
                    if (added == DateTime.MinValue) added = updated;
                    // File timestamps come back as Local kind; normalize so loose-file families
                    // sort against UTC-normalized VAR families on the same axis.
                    added = NormalizeToUtcForCompare(added);
                    updated = NormalizeToUtcForCompare(updated);
                    map[famKey] = new FamilyScanTimes { MinScanned = added, HighestVersion = 0, HighestVersionScanned = updated };
                    continue;
                }

                DateTime scanned = GetIndexedFirstScannedForFile(f);
                int version = GetUidVersionNumber(f);

                FamilyScanTimes fst;
                if (!map.TryGetValue(famKey, out fst))
                {
                    fst = new FamilyScanTimes { MinScanned = scanned, HighestVersion = version, HighestVersionScanned = scanned };
                    map[famKey] = fst;
                }
                else
                {
                    if (scanned < fst.MinScanned || fst.MinScanned == DateTime.MinValue) fst.MinScanned = scanned;
                    if (version > fst.HighestVersion)
                    {
                        fst.HighestVersion = version;
                        fst.HighestVersionScanned = scanned;
                    }
                    map[famKey] = fst;
                }
            }
            return map;
        }

        private static DateTime GetFamilyMinScanned(FileEntry file, Dictionary<string, FamilyScanTimes> fam)
        {
            string k = ComputeFamilyKey(file);
            if (k == null || fam == null) return DateTime.MinValue;
            return fam.TryGetValue(k, out FamilyScanTimes fst) ? fst.MinScanned : DateTime.MinValue;
        }

        /// <summary>
        /// Date to render on the gallery row's Date subtitle. Prefers first_scanned (when VPB first indexed this uid,
        /// i.e. when the user got this version of the package) over file mtime, because .var mtimes are commonly
        /// preserved from the creator's original build date and don't reflect when the user actually obtained it.
        /// Falls back to <see cref="FileEntry.LastWriteTime"/> when first_scanned is unknown.
        /// </summary>
        public static DateTime ResolveDisplayDateForRow(FileEntry file)
        {
            if (file == null) return DateTime.MinValue;
            DateTime dt;
            if (file is VarFileEntry vfe && vfe.TryGetGalleryIndexedFirstScanned(out dt)) return dt;
            if (file is PackageListEntry ple && ple.TryGetGalleryIndexedFirstScanned(out dt)) return dt;
            // Loose files: show mtime (DateUpdated key). Reflects the user's most recent edit/resave,
            // which is the date that's actually informative for a file the user iterates on.
            return file.LastWriteTime;
        }

        /// <summary>
        /// Drop VAR rows whose uid version is less than the family's highest version.
        /// No-op when <c>Settings.HideOldVersions</c> is off. Mutates the list in place.
        /// Non-VAR entries (scenes, JSONs) are left untouched.
        /// </summary>
        public static void ApplyHideOldVersionsFilter(List<FileEntry> files)
        {
            if (files == null || files.Count < 2) return;
            bool enabled = false;
            try { enabled = Settings.Instance != null && Settings.Instance.HideOldVersions != null && Settings.Instance.HideOldVersions.Value; } catch { enabled = false; }
            if (!enabled) return;

            // First pass: per family, track the highest version number seen in this list.
            var highest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (!(f is VarFileEntry) && !(f is PackageListEntry)) continue;
                string famKey = ComputeFamilyKey(f);
                if (string.IsNullOrEmpty(famKey)) continue;
                int v = GetUidVersionNumber(f);
                int existing;
                if (!highest.TryGetValue(famKey, out existing) || v > existing) highest[famKey] = v;
            }
            if (highest.Count == 0) return;

            int removed = 0;
            int kept = 0;
            for (int i = files.Count - 1; i >= 0; i--)
            {
                var f = files[i];
                if (!(f is VarFileEntry) && !(f is PackageListEntry)) continue;
                string famKey = ComputeFamilyKey(f);
                if (string.IsNullOrEmpty(famKey)) continue;
                int v = GetUidVersionNumber(f);
                int top;
                if (!highest.TryGetValue(famKey, out top)) continue;
                if (v < top)
                {
                    files.RemoveAt(i);
                    removed++;
                }
                else kept++;
            }
            if (LogFamilySortDiagnostics)
            {
                try { LogUtil.LogWarning("[VPB] HIDE_OLD_VERSIONS removed=" + removed + " kept=" + kept + " families=" + highest.Count); } catch { }
            }
        }

        private static DateTime GetFamilyHighestVersionScanned(FileEntry file, Dictionary<string, FamilyScanTimes> fam)
        {
            string k = ComputeFamilyKey(file);
            if (k == null || fam == null) return DateTime.MinValue;
            return fam.TryGetValue(k, out FamilyScanTimes fst) ? fst.HighestVersionScanned : DateTime.MinValue;
        }

        /// <summary>Creation time for sorting: .var package time, on-disk file creation, or <see cref="DateTime.MinValue"/> if unknown.</summary>
        private static DateTime GetSortCreationTime(FileEntry file)
        {
            if (file == null) return DateTime.MinValue;
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    try
                    {
                        long ib = vfe.Package.InternalCreationTimeBinary;
                        if (ib != long.MinValue) return DateTime.FromBinary(ib);
                    }
                    catch { }
                    return vfe.Package.CreationTime;
                }
                if (file is PackageListEntry ple && ple.Package != null)
                {
                    try
                    {
                        long ib = ple.Package.InternalCreationTimeBinary;
                        if (ib != long.MinValue) return DateTime.FromBinary(ib);
                    }
                    catch { }
                    return ple.Package.CreationTime;
                }
                string p = file.Path;
                if (string.IsNullOrEmpty(p) || p.StartsWith("[MISSING]", StringComparison.OrdinalIgnoreCase))
                    return DateTime.MinValue;
                return FileStat.GetCreationTimeOrMin(p);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>Shared by list UI and Deps sort — same rules for VarFileEntry and PackageListEntry.</summary>
        public static int GetDepsCount(FileEntry file)
        {
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    var deps = vfe.Package.RecursivePackageDependencies;
                    return deps != null ? deps.Count : 0;
                }
                if (file is PackageListEntry ple && ple.Package != null)
                {
                    var deps = ple.Package.RecursivePackageDependencies;
                    return deps != null ? deps.Count : 0;
                }
                // Handle scene files and other JSON files (only from Custom and Saves folders)
                if (file != null && (file.Path?.ToLowerInvariant().EndsWith(".json") ?? false))
                {
                    string pathLower = file.Path.ToLowerInvariant();
                    if (pathLower.Contains("custom") || pathLower.Contains("saves"))
                    {
                        var deps = ExtractSceneDependencies(file);
                        if (deps != null && deps.Count > 0)
                        {
                            // Deduplicate: keep only latest version of each Author.Name
                            var deduplicated = DeduplicateDependenciesByLatestVersion(deps);
                            return deduplicated.Count;
                        }
                        return 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] GetDepsCount error: {ex}");
            }
            return 0;
        }

        public static int GetDependentsCount(FileEntry file)
        {
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null)
                    return FileManager.ResolveDependentCount(vfe.Package);
                if (file is PackageListEntry ple && ple.Package != null)
                    return FileManager.ResolveDependentCount(ple.Package);
            }
            catch { }
            return 0;
        }

        public static int GetMissingDepsCount(FileEntry file)
        {
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    // Lazy cache: calculate on first access
                    if (vfe.Package.MissingDepsCount < 0)
                    {
                        vfe.Package.MissingDepsCount = CalculateMissingDeps(vfe.Package);
                    }
                    return vfe.Package.MissingDepsCount;
                }
                if (file is PackageListEntry ple && ple.Package != null)
                {
                    // Lazy cache: calculate on first access
                    if (ple.Package.MissingDepsCount < 0)
                    {
                        ple.Package.MissingDepsCount = CalculateMissingDeps(ple.Package);
                    }
                    return ple.Package.MissingDepsCount;
                }
                // Handle scene files and other JSON files (only from Custom and Saves folders)
                if (file != null && (file.Path?.ToLowerInvariant().EndsWith(".json") ?? false))
                {
                    string pathLower = file.Path.ToLowerInvariant();
                    if (pathLower.Contains("custom") || pathLower.Contains("saves"))
                    {
                        var deps = ExtractSceneDependencies(file);
                        if (deps != null && deps.Count > 0)
                        {
                            int missingCount = 0;
                            foreach (var dep in deps)
                            {
                                if (!FileManager.IsDependencySatisfiedByInstalled(dep))
                                    missingCount++;
                            }
                            return missingCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] GetMissingDepsCount error: {ex}");
            }
            return 0;
        }

        /// <summary>Missing dependency package ids for a gallery row (same rules as <see cref="GetMissingDepsCount"/>).</summary>
        public static List<string> GetMissingDependencyIds(FileEntry file)
        {
            var missing = new List<string>();
            try
            {
                VarPackage pkg = null;
                if (file is VarFileEntry vfe) pkg = vfe.Package;
                else if (file is PackageListEntry ple) pkg = ple.Package;

                if (pkg != null)
                {
                    CollectUnsatisfiedDeps(pkg.RecursivePackageDependencies, missing);
                    return missing;
                }

                if (file != null && (file.Path?.ToLowerInvariant().EndsWith(".json") ?? false))
                {
                    string pathLower = file.Path.ToLowerInvariant();
                    if (pathLower.Contains("custom") || pathLower.Contains("saves"))
                        CollectUnsatisfiedDeps(ExtractSceneDependencies(file), missing);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] GetMissingDependencyIds error: {ex}");
            }
            return missing;
        }

        private static void CollectUnsatisfiedDeps(IEnumerable<string> deps, List<string> into)
        {
            if (deps == null || into == null) return;
            foreach (var dep in deps)
            {
                // Newer installed .var satisfies exact meta pins (Hub download-latest).
                if (!string.IsNullOrEmpty(dep) && !FileManager.IsDependencySatisfiedByInstalled(dep))
                    into.Add(dep);
            }
        }

        private static int CalculateMissingDeps(VarPackage package)
        {
            try
            {
                var deps = package.RecursivePackageDependencies;
                if (deps != null && deps.Count > 0)
                {
                    int missingCount = 0;
                    foreach (var dep in deps)
                    {
                        // Newer installed .var satisfies exact meta pins (Hub download-latest).
                        if (!FileManager.IsDependencySatisfiedByInstalled(dep))
                            missingCount++;
                    }
                    return missingCount;
                }
            }
            catch { }
            return 0;
        }

        public static HashSet<string> ExtractSceneDependencies(FileEntry file)
        {
            try
            {
                if (file == null || !file.Exists)
                {
                    return null;
                }

                string filePath = file.Path;
                if (string.IsNullOrEmpty(filePath))
                {
                    return null;
                }

                // L1: process-memory cache
                if (_sceneDependencyCache.TryGetValue(filePath, out var cached))
                {
                    return cached;
                }

                // L2: SQLite cache (survives VaM restart; keyed by mtime+size so Timeline writeback invalidates).
                long wtBin = 0, sz = 0;
                bool haveStat = false;
                try
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Exists)
                    {
                        wtBin = fi.LastWriteTimeUtc.ToBinary();
                        sz = fi.Length;
                        haveStat = true;
                    }
                }
                catch { haveStat = false; }

                if (haveStat)
                {
                    var dbDeps = new HashSet<string>();
                    if (VpbLocalDatabase.TryReadLooseSceneDeps(filePath, wtBin, sz, dbDeps))
                    {
                        _sceneDependencyCache[filePath] = dbDeps;
                        return dbDeps;
                    }
                }

                // Miss: pay the stream scan once, then persist for future binds.
                var deps = ExtractDependenciesStreaming(file);

                if (deps != null && deps.Count > 0)
                    _sceneDependencyCache[filePath] = deps;

                if (haveStat && deps != null)
                {
                    try { VpbLocalDatabase.WriteLooseSceneDeps(filePath, wtBin, sz, deps); } catch { }
                }

                return deps;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] ExtractSceneDependencies error: {ex}");
            }
            return null;
        }

        private static HashSet<string> ExtractDependenciesStreaming(FileEntry file)
        {
            HashSet<string> dependencies = new HashSet<string>();

            try
            {
                string filePath = file.Path;

                // Stream-scan the file without loading it into memory.
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    dependencies = DependencyExtractor.ExtractDependenciesFromFile(filePath, maxDependencies: 150, maxMilliseconds: 1500);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] ExtractDependenciesStreaming error: {ex}");
            }

            return dependencies;
        }

        public void SortCategories(List<Gallery.Category> categories, SortState state, Dictionary<string, int> counts = null)
        {
            if (categories == null || state == null) return;

            switch (state.Type)
            {
                case SortType.Name:
                    if (state.Direction == SortDirection.Ascending)
                        categories.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                    else
                        categories.Sort((a, b) => string.Compare(b.name, a.name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.Count:
                    if (counts != null)
                    {
                        if (state.Direction == SortDirection.Ascending)
                            categories.Sort((a, b) => GetCount(a.name, counts).CompareTo(GetCount(b.name, counts)));
                        else
                            categories.Sort((a, b) => GetCount(b.name, counts).CompareTo(GetCount(a.name, counts)));
                    }
                    break;
            }
        }

        public void SortCreators(List<CreatorCacheEntry> creators, SortState state)
        {
            if (creators == null || state == null) return;

            switch (state.Type)
            {
                case SortType.Name:
                    if (state.Direction == SortDirection.Ascending)
                        creators.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    else
                        creators.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.Count:
                    if (state.Direction == SortDirection.Ascending)
                        creators.Sort((a, b) => a.Count.CompareTo(b.Count));
                    else
                        creators.Sort((a, b) => b.Count.CompareTo(a.Count));
                    break;
            }
        }

        private int GetCount(string key, Dictionary<string, int> counts)
        {
            if (counts.TryGetValue(key, out int count)) return count;
            return 0;
        }

        public SortState GetDefaultSortState(string context)
        {
            return cache.GetSortState(context) ?? new SortState(SortType.Name, SortDirection.Ascending);
        }

        public void SaveSortState(string context, SortState state)
        {
            cache.SaveSortState(context, state);
        }

        public void SaveCache()
        {
            cache.Save();
        }

        public static HashSet<string> DeduplicateDependenciesByLatestVersion(HashSet<string> deps)
        {
            var deduplicated = new HashSet<string>();
            var byPackageName = new Dictionary<string, string>(); // key: "Author.Name", value: full "Author.Name.Version"

            foreach (var dep in deps)
            {
                var parts = dep.Split('.');
                if (parts.Length >= 3)
                {
                    // Extract Author.Name (first two parts)
                    string packageName = parts[0] + "." + parts[1];

                    if (!byPackageName.TryGetValue(packageName, out string existing))
                    {
                        byPackageName[packageName] = dep;
                    }
                    else
                    {
                        // Keep the one with higher version
                        string existingVersion = existing.Split('.')[2];
                        string newVersion = parts[2];

                        if (CompareVersions(newVersion, existingVersion) > 0)
                        {
                            byPackageName[packageName] = dep;
                        }
                    }
                }
                else
                {
                    deduplicated.Add(dep);
                }
            }

            // Add the deduplicated packages
            foreach (var kvp in byPackageName)
            {
                deduplicated.Add(kvp.Value);
            }

            return deduplicated;
        }

        private static int CompareVersions(string v1, string v2)
        {
            // "latest" is always newest
            if (v1 == "latest") return 1;
            if (v2 == "latest") return -1;

            // Try numeric comparison
            if (int.TryParse(v1, out int v1Int) && int.TryParse(v2, out int v2Int))
            {
                return v1Int.CompareTo(v2Int);
            }

            // Fallback to string comparison
            return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class GallerySortCache
    {
        private Dictionary<string, SortState> sortStates = new Dictionary<string, SortState>();
        private string cachePath;

        public GallerySortCache()
        {
            string cacheDir = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Cache"), "VPB");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            cachePath = Path.Combine(cacheDir, "gallery_sort_cache.bin");
            Load();
        }

        public SortState GetSortState(string context)
        {
            if (sortStates.TryGetValue(context, out SortState state))
                return state.Clone();
            return null;
        }

        public void SaveSortState(string context, SortState state)
        {
            sortStates[context] = state.Clone();
            Save();
        }

        private void Load()
        {
            if (!File.Exists(cachePath)) return;

            try
            {
                using (var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    int version = reader.ReadInt32();
                    if (version != 2) return;

                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        string key = reader.ReadString();
                        SortType type = (SortType)reader.ReadInt32();
                        SortDirection dir = (SortDirection)reader.ReadInt32();
                        sortStates[key] = new SortState(type, dir);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load sort cache: " + ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write(2); // Version
                    writer.Write(sortStates.Count);
                    foreach (var kvp in sortStates)
                    {
                        writer.Write(kvp.Key);
                        writer.Write((int)kvp.Value.Type);
                        writer.Write((int)kvp.Value.Direction);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to save sort cache: " + ex.Message);
            }
        }
    }
}
