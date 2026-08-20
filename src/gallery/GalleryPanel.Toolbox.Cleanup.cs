using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Security.Cryptography;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private enum CleanupCandidateType
        {
            Duplicate,
            OldVersion,
            Damaged,
            StaleCache
        }

        private enum CleanupCandidateSourceKind
        {
            VarPackage,
            LocalScene,
            LocalFile,
            StaleCache
        }

        private sealed class CleanupCandidate
        {
            public string SourcePath;
            public string NormalizedPath;
            public string PackageUid;
            public string GroupKey;
            public CleanupCandidateSourceKind SourceKind;
            public HashSet<CleanupCandidateType> Flags = new HashSet<CleanupCandidateType>();
            public HashSet<string> Reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int NumericVersion = -1;
            public bool IsExcluded;
            public long StaleCacheLastAccessedBinary = long.MinValue;

            public bool HasFlag(CleanupCandidateType t) => Flags.Contains(t);

            public CleanupCandidateType PrimaryType
            {
                get
                {
                    if (Flags.Contains(CleanupCandidateType.StaleCache)) return CleanupCandidateType.StaleCache;
                    if (Flags.Contains(CleanupCandidateType.Damaged)) return CleanupCandidateType.Damaged;
                    if (Flags.Contains(CleanupCandidateType.Duplicate)) return CleanupCandidateType.Duplicate;
                    return CleanupCandidateType.OldVersion;
                }
            }

            public string GetFlagsLabel()
            {
                var labels = new List<string>();
                if (Flags.Contains(CleanupCandidateType.StaleCache)) labels.Add("Stale Cache");
                if (Flags.Contains(CleanupCandidateType.Damaged)) labels.Add("Damaged");
                if (Flags.Contains(CleanupCandidateType.Duplicate)) labels.Add("Duplicate");
                if (Flags.Contains(CleanupCandidateType.OldVersion)) labels.Add("Old Version");

                string baseLabel = labels.Count > 0 ? string.Join(" | ", labels.ToArray()) : "Other";
                return IsExcluded ? (baseLabel + " | Excluded") : baseLabel;
            }
        }

        private sealed class CleanupFileEntry : FileEntry
        {
            public readonly CleanupCandidate Candidate;

            public CleanupFileEntry(CleanupCandidate candidate)
            {
                Candidate = candidate;
                string src = candidate != null ? candidate.SourcePath : "";
                string fileName = "";
                try { fileName = System.IO.Path.GetFileName(src); } catch { fileName = src; }
                if (string.IsNullOrEmpty(fileName)) fileName = src;

                Name = fileName;
                Path = src ?? "";
                Uid = Path;
                Exists = File.Exists(Path);
                LastWriteTime = Exists ? File.GetLastWriteTime(Path) : DateTime.MinValue;
                Size = Exists ? new FileInfo(Path).Length : 0;
            }

            public override FileEntryStream OpenStream()
            {
                return null;
            }

            public override FileEntryStreamReader OpenStreamReader()
            {
                return null;
            }
        }

        private bool cleanupModeActive;
        private int cleanupFilterMode;
        private int cleanupStaleBucketMode; // 0=All, 1=1w, 2=2w, 3=1m, 4=2m, 5=6m
        private bool cleanupScanInProgress;
        private bool cleanupSideHostIsLeft;
        private ContentType? cleanupPrevHostContent;
        private string cleanupPrevCategoryTitle;
        private string cleanupPrevExtension;
        private string cleanupPrevPath;
        private const string CleanupExcludeTag = "Exclude";
        private readonly HashSet<string> cleanupExcludedKeyCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CleanupCandidate> cleanupCandidatesAll = new List<CleanupCandidate>();
        private readonly List<CleanupCandidate> cleanupCandidatesCachedSnapshot = new List<CleanupCandidate>();
        private long cleanupCandidatesCachedScanClockBinary = long.MinValue;
        private readonly Dictionary<string, CleanupCandidate> cleanupCandidateByPath =
            new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CleanupHashCacheEntry> cleanupHashCacheByPath =
            new Dictionary<string, CleanupHashCacheEntry>(StringComparer.OrdinalIgnoreCase);

        // Splits the previously-aggregated "var=" scan timer into stale-cache vs var vs hash so one run
        // pinpoints the bottleneck. Stopwatch ticks accumulated here, converted to ms at report time.
        private long _clDiagStaleQueryTicks;
        private int _clDiagStaleRows;
        private long _clDiagStaleFileExistsTicks;
        private int _clDiagStaleFileExistsCalls;
        private long _clDiagStaleUsagePkgTicks;
        private int _clDiagStaleUsagePkgCalls;
        private long _clDiagStaleRatingTicks;
        private int _clDiagStaleExempt;
        private int _clDiagStaleCandidates;
        private long _clDiagStaleTotalTicks;

        private long _clDiagVarEnumTicks;
        private int _clDiagVarFilesAddon;
        private int _clDiagVarFilesAll;
        private long _clDiagVarParseTicks;
        private int _clDiagVarUidGroups;
        private int _clDiagVarUidGroupsDup;
        private long _clDiagVarDupHashGroupTicks;
        private long _clDiagVarOldVerTicks;
        private long _clDiagVarInvalidUnionTicks;
        private int _clDiagVarInvalidPkgs;
        private long _clDiagVarTotalTicks;

        private int _clDiagHashCacheHits;
        private int _clDiagHashFilesRead;
        private long _clDiagHashBytesRead;
        private long _clDiagHashComputeTicks;
        private long _clDiagExcludeRefreshTicks;
        private long _clDiagPostTicks;
        private readonly List<CleanupHashSample> _clDiagHashTopFiles = new List<CleanupHashSample>();

        private sealed class CleanupHashSample
        {
            public string Path;
            public long Bytes;
            public double Ms;
        }

        private static double SwTicksToMs(long ticks)
        {
            try { return ticks * 1000.0 / Stopwatch.Frequency; }
            catch { return 0.0; }
        }

        private void ResetCleanupScanDiag()
        {
            _clDiagStaleQueryTicks = 0; _clDiagStaleRows = 0;
            _clDiagStaleFileExistsTicks = 0; _clDiagStaleFileExistsCalls = 0;
            _clDiagStaleUsagePkgTicks = 0; _clDiagStaleUsagePkgCalls = 0;
            _clDiagStaleRatingTicks = 0; _clDiagStaleExempt = 0; _clDiagStaleCandidates = 0;
            _clDiagStaleTotalTicks = 0;
            _clDiagVarEnumTicks = 0; _clDiagVarFilesAddon = 0; _clDiagVarFilesAll = 0;
            _clDiagVarParseTicks = 0; _clDiagVarUidGroups = 0; _clDiagVarUidGroupsDup = 0;
            _clDiagVarDupHashGroupTicks = 0; _clDiagVarOldVerTicks = 0;
            _clDiagVarInvalidUnionTicks = 0; _clDiagVarInvalidPkgs = 0;
            _clDiagVarTotalTicks = 0;
            _clDiagHashCacheHits = 0; _clDiagHashFilesRead = 0; _clDiagHashBytesRead = 0;
            _clDiagHashComputeTicks = 0; _clDiagExcludeRefreshTicks = 0; _clDiagPostTicks = 0;
            _clDiagHashTopFiles.Clear();
        }

        private void RecordCleanupHashSample(string path, long bytes, double ms)
        {
            // Keep the most expensive hashed files so the report names exactly which large VARs cost.
            const int Cap = 15;
            try
            {
                if (_clDiagHashTopFiles.Count < Cap)
                {
                    _clDiagHashTopFiles.Add(new CleanupHashSample { Path = path, Bytes = bytes, Ms = ms });
                    _clDiagHashTopFiles.Sort((a, b) => b.Ms.CompareTo(a.Ms));
                    return;
                }
                var worst = _clDiagHashTopFiles[_clDiagHashTopFiles.Count - 1];
                if (worst != null && ms > worst.Ms)
                {
                    worst.Path = path; worst.Bytes = bytes; worst.Ms = ms;
                    _clDiagHashTopFiles.Sort((a, b) => b.Ms.CompareTo(a.Ms));
                }
            }
            catch { }
        }

        private void LogCleanupScanDiag()
        {
            try
            {
                LogUtil.LogWarning("[VPB] Cleanup(diag) STALE | total=" + SwTicksToMs(_clDiagStaleTotalTicks).ToString("F0")
                    + "ms | query=" + SwTicksToMs(_clDiagStaleQueryTicks).ToString("F0") + "ms rows=" + _clDiagStaleRows
                    + " | fileExists=" + SwTicksToMs(_clDiagStaleFileExistsTicks).ToString("F0") + "ms x" + _clDiagStaleFileExistsCalls
                    + " | pkgLookup=" + SwTicksToMs(_clDiagStaleUsagePkgTicks).ToString("F0") + "ms x" + _clDiagStaleUsagePkgCalls
                    + " | ratings=" + SwTicksToMs(_clDiagStaleRatingTicks).ToString("F0") + "ms"
                    + " | exempt=" + _clDiagStaleExempt + " candidates=" + _clDiagStaleCandidates);

                LogUtil.LogWarning("[VPB] Cleanup(diag) VAR | total=" + SwTicksToMs(_clDiagVarTotalTicks).ToString("F0")
                    + "ms | enum=" + SwTicksToMs(_clDiagVarEnumTicks).ToString("F0") + "ms all=" + _clDiagVarFilesAll + " addon=" + _clDiagVarFilesAddon
                    + " | parse=" + SwTicksToMs(_clDiagVarParseTicks).ToString("F0") + "ms"
                    + " | uidGroups=" + _clDiagVarUidGroups + " dupGroups=" + _clDiagVarUidGroupsDup
                    + " dupHash=" + SwTicksToMs(_clDiagVarDupHashGroupTicks).ToString("F0") + "ms"
                    + " | oldVer=" + SwTicksToMs(_clDiagVarOldVerTicks).ToString("F0") + "ms"
                    + " | invalidUnion=" + SwTicksToMs(_clDiagVarInvalidUnionTicks).ToString("F0") + "ms invalidPkgs=" + _clDiagVarInvalidPkgs);

                LogUtil.LogWarning("[VPB] Cleanup(diag) HASH | filesRead=" + _clDiagHashFilesRead
                    + " cacheHits=" + _clDiagHashCacheHits
                    + " bytesRead=" + _clDiagHashBytesRead + " (" + (_clDiagHashBytesRead / (1024.0 * 1024.0)).ToString("F1") + "MB)"
                    + " computeTime=" + SwTicksToMs(_clDiagHashComputeTicks).ToString("F0") + "ms");

                LogUtil.LogWarning("[VPB] Cleanup(diag) MISC | excludeRefresh=" + SwTicksToMs(_clDiagExcludeRefreshTicks).ToString("F0")
                    + "ms postFilterSort=" + SwTicksToMs(_clDiagPostTicks).ToString("F0") + "ms");

                for (int i = 0; i < _clDiagHashTopFiles.Count; i++)
                {
                    var s = _clDiagHashTopFiles[i];
                    if (s == null) continue;
                    LogUtil.LogWarning("[VPB] Cleanup(diag) HASH top#" + (i + 1) + " | " + s.Ms.ToString("F0") + "ms "
                        + (s.Bytes / (1024.0 * 1024.0)).ToString("F1") + "MB | " + s.Path);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Cleanup(diag) report failed: " + ex.Message);
            }
        }

        private sealed class CleanupHashCacheEntry
        {
            public long Size;
            public long LastWriteUtcTicks;
            public string Sha256Hex;
        }

        private static string NormalizePathSafe(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return p.Replace('\\', '/'); } catch { return p; }
        }

        private static string CleanupTypeFolderName(CleanupCandidateType t)
        {
            switch (t)
            {
                case CleanupCandidateType.Duplicate: return "Duplicates";
                case CleanupCandidateType.OldVersion: return "OldVersions";
                case CleanupCandidateType.StaleCache: return "StaleCache";
                default: return "Damaged";
            }
        }

        private static bool TryParsePackageUidVersionFromFileName(string pathOrName, out string uid, out string groupKey, out int version)
        {
            uid = null;
            groupKey = null;
            version = -1;
            if (string.IsNullOrEmpty(pathOrName)) return false;

            string name = pathOrName;
            try
            {
                name = Path.GetFileNameWithoutExtension(pathOrName) ?? pathOrName;
            }
            catch { }
            name = (name ?? "").Trim();
            if (name.Length == 0) return false;

            string[] parts = name.Split('.');
            if (parts.Length != 3) return false;
            if (string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1])) return false;
            if (!FileManager.TryParseVarVersionSegment(parts[2], out version)) return false;

            groupKey = parts[0] + "." + parts[1];
            uid = groupKey + "." + version;
            return true;
        }

        private static bool TryParseNumericSuffixVersion(string fileStem, out string familyBase, out int version)
        {
            familyBase = null;
            version = -1;
            if (string.IsNullOrEmpty(fileStem)) return false;

            // MyScene_v12 / MyScene-12 / MyScene.12
            Match m = Regex.Match(fileStem, @"^(.*?)(?:[_\.\-]v?([0-9]+))$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;

            string b = (m.Groups[1].Value ?? "").Trim();
            string n = (m.Groups[2].Value ?? "").Trim();
            if (b.Length == 0) return false;
            if (!int.TryParse(n, out version)) return false;
            familyBase = b;
            return true;
        }

        private static CleanupCandidateSourceKind GetLocalKindForPath(string fullPath)
        {
            string p = NormalizePathSafe(fullPath).ToLowerInvariant();
            if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                p.IndexOf("/saves/scene/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CleanupCandidateSourceKind.LocalScene;
            }
            return CleanupCandidateSourceKind.LocalFile;
        }

        private static bool IsLocalMarkerFile(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath)) return false;
            // Keep favorites/hide sidecar markers out of cleanup detection:
            // they are metadata files managed by existing systems and can be zero-byte by design.
            return normalizedPath.EndsWith(".fav", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.EndsWith(".hide", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.EndsWith(".json.fav", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.EndsWith(".json.hide", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnderDeletedFolders(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath)) return false;
            return normalizedPath.IndexOf("/DeletedPackages/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedPath.IndexOf("/DeletedScenes/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetDuplicateKeepRank(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath)) return int.MaxValue;
            // Keep AddonPackages when present; prefer deleting outside AddonPackages (e.g. AllPackages).
            if (normalizedPath.IndexOf("/AddonPackages/", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (normalizedPath.IndexOf("/AllPackages/", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
        }

        private static CleanupCandidate PickDuplicateKeepCandidate(List<CleanupCandidate> arr)
        {
            if (arr == null || arr.Count == 0) return null;
            CleanupCandidate best = arr[0];
            int bestRank = GetDuplicateKeepRank(best != null ? best.NormalizedPath : null);
            for (int i = 1; i < arr.Count; i++)
            {
                var c = arr[i];
                int rank = GetDuplicateKeepRank(c != null ? c.NormalizedPath : null);
                if (rank < bestRank)
                {
                    best = c;
                    bestRank = rank;
                }
            }
            return best;
        }

        private bool TryGetFileSha256Cached(string path, out long size, out string sha256Hex)
        {
            size = 0;
            sha256Hex = null;
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                string norm = NormalizePathSafe(path);
                if (string.IsNullOrEmpty(norm)) return false;

                var fi = new FileInfo(path);
                if (!fi.Exists) return false;
                size = fi.Length;
                long lastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks;

                if (cleanupHashCacheByPath.TryGetValue(norm, out CleanupHashCacheEntry cached))
                {
                    if (cached != null
                        && cached.Size == size
                        && cached.LastWriteUtcTicks == lastWriteUtcTicks
                        && !string.IsNullOrEmpty(cached.Sha256Hex))
                    {
                        sha256Hex = cached.Sha256Hex;
                        _clDiagHashCacheHits++;
                        return true;
                    }
                }

                long hashT0 = Stopwatch.GetTimestamp();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan))
                using (var sha = SHA256.Create())
                {
                    byte[] hashBytes = sha.ComputeHash(stream);
                    sha256Hex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
                long hashTicks = Stopwatch.GetTimestamp() - hashT0;
                _clDiagHashComputeTicks += hashTicks;
                _clDiagHashFilesRead++;
                _clDiagHashBytesRead += size;
                RecordCleanupHashSample(norm, size, SwTicksToMs(hashTicks));

                cleanupHashCacheByPath[norm] = new CleanupHashCacheEntry
                {
                    Size = size,
                    LastWriteUtcTicks = lastWriteUtcTicks,
                    Sha256Hex = sha256Hex
                };
                return !string.IsNullOrEmpty(sha256Hex);
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<string, List<CleanupCandidate>> BuildDuplicateHashGroups(List<CleanupCandidate> candidates)
        {
            var result = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);
            if (candidates == null || candidates.Count <= 1) return result;

            // Bucket by on-disk length (a stat, no read); only same-size files can be byte-identical, so
            // unique-size files never get hashed in the second pass.
            var bySize = new Dictionary<long, List<CleanupCandidate>>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c == null || string.IsNullOrEmpty(c.SourcePath)) continue;
                long size;
                try
                {
                    var fi = new FileInfo(c.SourcePath);
                    if (!fi.Exists) continue;
                    size = fi.Length;
                }
                catch { continue; }

                if (!bySize.TryGetValue(size, out var sameSize))
                {
                    sameSize = new List<CleanupCandidate>();
                    bySize[size] = sameSize;
                }
                sameSize.Add(c);
            }

            foreach (var sizeGroup in bySize.Values)
            {
                if (sizeGroup == null || sizeGroup.Count <= 1) continue;
                for (int i = 0; i < sizeGroup.Count; i++)
                {
                    var c = sizeGroup[i];
                    if (c == null || string.IsNullOrEmpty(c.SourcePath)) continue;
                    if (!TryGetFileSha256Cached(c.SourcePath, out long _, out string hash) || string.IsNullOrEmpty(hash)) continue;

                    if (!result.TryGetValue(hash, out var arr))
                    {
                        arr = new List<CleanupCandidate>();
                        result[hash] = arr;
                    }
                    arr.Add(c);
                }
            }

            return result;
        }

        private CleanupCandidate GetOrCreateCleanupCandidate(
            Dictionary<string, CleanupCandidate> byPath,
            string sourcePath,
            CleanupCandidateSourceKind kind)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            string norm = NormalizePathSafe(sourcePath);
            if (norm.Length == 0) return null;

            if (byPath.TryGetValue(norm, out CleanupCandidate found))
                return found;

            var c = new CleanupCandidate
            {
                SourcePath = sourcePath,
                NormalizedPath = norm,
                SourceKind = kind
            };
            byPath[norm] = c;
            return c;
        }

        private static CleanupCandidate CloneCleanupCandidate(CleanupCandidate src)
        {
            if (src == null) return null;
            return new CleanupCandidate
            {
                SourcePath = src.SourcePath,
                NormalizedPath = src.NormalizedPath,
                PackageUid = src.PackageUid,
                GroupKey = src.GroupKey,
                SourceKind = src.SourceKind,
                NumericVersion = src.NumericVersion,
                IsExcluded = src.IsExcluded,
                StaleCacheLastAccessedBinary = src.StaleCacheLastAccessedBinary,
                Flags = new HashSet<CleanupCandidateType>(src.Flags),
                Reasons = new HashSet<string>(src.Reasons, StringComparer.OrdinalIgnoreCase),
            };
        }

        private static void CloneCleanupCandidatesInto(List<CleanupCandidate> source, List<CleanupCandidate> destination)
        {
            if (destination == null) return;
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                var clone = CloneCleanupCandidate(source[i]);
                if (clone != null) destination.Add(clone);
            }
        }

        private static long TryGetCleanupScanClockBinary()
        {
            try { return FileManager.lastPackageRefreshTime.ToBinary(); }
            catch { return long.MinValue; }
        }

        private bool TryLoadCleanupCandidatesFromCache(bool testMode)
        {
            if (testMode) return false; // Shift+Cleanup should always force a fresh scan.
            if (cleanupCandidatesCachedSnapshot == null || cleanupCandidatesCachedSnapshot.Count == 0) return false;
            long nowClock = TryGetCleanupScanClockBinary();
            if (nowClock == long.MinValue || cleanupCandidatesCachedScanClockBinary == long.MinValue) return false;
            if (nowClock != cleanupCandidatesCachedScanClockBinary) return false;

            CloneCleanupCandidatesInto(cleanupCandidatesCachedSnapshot, cleanupCandidatesAll);
            return cleanupCandidatesAll.Count > 0;
        }

        private void SaveCleanupCandidatesToCache()
        {
            cleanupCandidatesCachedScanClockBinary = TryGetCleanupScanClockBinary();
            CloneCleanupCandidatesInto(cleanupCandidatesAll, cleanupCandidatesCachedSnapshot);
        }

        private void InvalidateCleanupCandidatesCache()
        {
            cleanupCandidatesCachedScanClockBinary = long.MinValue;
            cleanupCandidatesCachedSnapshot.Clear();
        }

        private List<CleanupCandidate> BuildCleanupCandidatesGlobal(bool testMode = false)
        {
            ResetCleanupScanDiag();
            var totalSw = Stopwatch.StartNew();
            var byPath = new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);

            long staleT0 = Stopwatch.GetTimestamp();
            BuildStaleCacheCleanupCandidates(byPath, testMode);
            _clDiagStaleTotalTicks = Stopwatch.GetTimestamp() - staleT0;

            long varT0 = Stopwatch.GetTimestamp();
            BuildVarCleanupCandidates(byPath);
            _clDiagVarTotalTicks = Stopwatch.GetTimestamp() - varT0;

            long excludeT0 = Stopwatch.GetTimestamp();
            RefreshCleanupExcludedKeyCache();
            _clDiagExcludeRefreshTicks = Stopwatch.GetTimestamp() - excludeT0;

            long postT0 = Stopwatch.GetTimestamp();
            var list = byPath.Values.ToList();
            for (int i = 0; i < list.Count; i++)
                list[i].IsExcluded = IsCleanupCandidateExcluded(list[i]);
            // Keep only actionable items (flagged or explicitly excluded) to reduce scan/list overhead.
            list = list.Where(c => c != null && (c.Flags.Count > 0 || c.IsExcluded)).ToList();
            list.Sort((a, b) =>
            {
                int t = string.Compare(a.GetFlagsLabel(), b.GetFlagsLabel(), StringComparison.OrdinalIgnoreCase);
                if (t != 0) return t;
                return string.Compare(a.NormalizedPath, b.NormalizedPath, StringComparison.OrdinalIgnoreCase);
            });
            _clDiagPostTicks = Stopwatch.GetTimestamp() - postT0;
            totalSw.Stop();
            LogUtil.LogWarning("[VPB] Cleanup(list) scan done | stale=" + SwTicksToMs(_clDiagStaleTotalTicks).ToString("F0")
                + "ms var=" + SwTicksToMs(_clDiagVarTotalTicks).ToString("F0") + "ms | local=disabled | total="
                + totalSw.ElapsedMilliseconds + " ms | candidates=" + list.Count);
            LogCleanupScanDiag();
            return list;
        }

        private void RefreshCleanupExcludedKeyCache()
        {
            try { VpbLocalDatabase.TryReadCleanupExcludedUids(cleanupExcludedKeyCache); }
            catch { cleanupExcludedKeyCache.Clear(); }
        }

        private static IEnumerable<string> EnumerateCleanupExcludeKeys(CleanupCandidate c)
        {
            if (c == null) yield break;
            if (!string.IsNullOrEmpty(c.PackageUid)) yield return c.PackageUid;
            if (!string.IsNullOrEmpty(c.SourcePath)) yield return c.SourcePath;
            if (!string.IsNullOrEmpty(c.NormalizedPath) &&
                !string.Equals(c.NormalizedPath, c.SourcePath, StringComparison.OrdinalIgnoreCase))
                yield return c.NormalizedPath;
        }

        private bool IsCleanupCandidateExcluded(CleanupCandidate c)
        {
            if (c == null) return false;
            if (c.SourceKind == CleanupCandidateSourceKind.StaleCache) return false;
            try
            {
                foreach (string key in EnumerateCleanupExcludeKeys(c))
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (cleanupExcludedKeyCache.Contains(key)) return true;
                }
            }
            catch { }
            return false;
        }

        private void BuildStaleCacheCleanupCandidates(Dictionary<string, CleanupCandidate> byPath, bool testMode = false)
        {
            try
            {
                // Logic: older than 1 week (unless testMode). Hit count is tracked but not used for staleness.
                // UI can further bucket-filter (1w/2w/1m/2m/6m) without rescanning.
                int days = testMode ? 0 : 7;

                long olderThanBinary = DateTime.UtcNow.AddDays(-days).ToBinary();
                var staleRows = new List<VpbLocalDatabase.CacheUsageRow>();
                var pkgsByPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                long staleQueryT0 = Stopwatch.GetTimestamp();
                VpbLocalDatabase.TryGetStaleCacheItemsWithPackages(olderThanBinary, staleRows, pkgsByPath);
                _clDiagStaleQueryTicks += Stopwatch.GetTimestamp() - staleQueryT0;
                _clDiagStaleRows = staleRows.Count;

                foreach (var row in staleRows)
                {
                    if (string.IsNullOrEmpty(row.CachePath)) continue;
                    long feT0 = Stopwatch.GetTimestamp();
                    bool exists = File.Exists(row.CachePath);
                    _clDiagStaleFileExistsTicks += Stopwatch.GetTimestamp() - feT0;
                    _clDiagStaleFileExistsCalls++;
                    if (!exists) continue;

                    bool exempt = false;
                    {
                        long upT0 = Stopwatch.GetTimestamp();
                        List<string> cacheUids;
                        pkgsByPath.TryGetValue(row.CachePath, out cacheUids);
                        _clDiagStaleUsagePkgTicks += Stopwatch.GetTimestamp() - upT0;
                        _clDiagStaleUsagePkgCalls++;
                        if (cacheUids != null && cacheUids.Count > 0)
                        {
                            for (int i = 0; i < cacheUids.Count; i++)
                            {
                                string uid = cacheUids[i];
                                if (string.IsNullOrEmpty(uid)) continue;

                                // Exempt if package is autoinstall
                                if (FileEntry.AutoInstallLookup != null && FileEntry.AutoInstallLookup.Contains(uid))
                                {
                                    exempt = true;
                                    break;
                                }

                                // Exempt if package has star rating > 3
                                int r = 0;
                                long rtT0 = Stopwatch.GetTimestamp();
                                try { r = RatingsManager.Instance != null ? RatingsManager.Instance.GetRating(uid) : 0; } catch { r = 0; }
                                _clDiagStaleRatingTicks += Stopwatch.GetTimestamp() - rtT0;
                                if (r > 3)
                                {
                                    exempt = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (exempt) { _clDiagStaleExempt++; continue; }

                    var c = GetOrCreateCleanupCandidate(byPath, row.CachePath, CleanupCandidateSourceKind.StaleCache);
                    if (c == null) continue;

                    c.Flags.Add(CleanupCandidateType.StaleCache);
                    c.StaleCacheLastAccessedBinary = row.LastAccessedBinary;
                    DateTime lastAccess = DateTime.FromBinary(row.LastAccessedBinary);
                    c.Reasons.Add($"Stale Texture Cache (Last: {lastAccess:yyyy-MM-dd})");
                    _clDiagStaleCandidates++;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Failed to build stale cache cleanup candidates: " + ex.Message);
            }
        }

        private void BuildVarCleanupCandidates(Dictionary<string, CleanupCandidate> byPath)
        {
            var allVarPaths = new List<string>();
            try
            {
                long enumT0 = Stopwatch.GetTimestamp();
                if (Directory.Exists("AllPackages")) FileManager.SafeGetFiles("AllPackages", "*.var", allVarPaths);
                _clDiagVarFilesAll = allVarPaths.Count;
                if (Directory.Exists("AddonPackages")) FileManager.SafeGetFiles("AddonPackages", "*.var", allVarPaths);
                _clDiagVarFilesAddon = allVarPaths.Count - _clDiagVarFilesAll;
                _clDiagVarEnumTicks += Stopwatch.GetTimestamp() - enumT0;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Cleanup: var scan failed: " + ex.Message);
            }

            var byUidVersion = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);
            var byGroup = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);

            long parseT0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < allVarPaths.Count; i++)
            {
                string raw = allVarPaths[i];
                if (string.IsNullOrEmpty(raw)) continue;
                string norm = NormalizePathSafe(raw);
                if (IsUnderDeletedFolders(norm)) continue;

                var c = GetOrCreateCleanupCandidate(byPath, norm, CleanupCandidateSourceKind.VarPackage);
                if (c == null) continue;

                if (TryParsePackageUidVersionFromFileName(norm, out string uid, out string group, out int version))
                {
                    c.PackageUid = uid;
                    c.GroupKey = group;
                    c.NumericVersion = version;

                    if (!byUidVersion.TryGetValue(uid, out var dupList))
                    {
                        dupList = new List<CleanupCandidate>();
                        byUidVersion[uid] = dupList;
                    }
                    dupList.Add(c);

                    if (!byGroup.TryGetValue(group, out var grpList))
                    {
                        grpList = new List<CleanupCandidate>();
                        byGroup[group] = grpList;
                    }
                    grpList.Add(c);
                }
                else
                {
                    bool isVarByExt = false;
                    try { isVarByExt = string.Equals(Path.GetExtension(norm), ".var", StringComparison.OrdinalIgnoreCase); } catch { isVarByExt = false; }
                    if (isVarByExt)
                    {
                        c.Flags.Add(CleanupCandidateType.Damaged);
                        c.Reasons.Add("Invalid VAR filename format");
                    }
                }
            }

            _clDiagVarParseTicks += Stopwatch.GetTimestamp() - parseT0;

            _clDiagVarUidGroups = byUidVersion.Count;
            long dupT0 = Stopwatch.GetTimestamp();
            foreach (var kvp in byUidVersion)
            {
                var arr = kvp.Value;
                if (arr == null || arr.Count <= 1) continue;
                _clDiagVarUidGroupsDup++;
                var byHash = BuildDuplicateHashGroups(arr);
                var hashMatched = new HashSet<CleanupCandidate>();
                foreach (var hashKvp in byHash)
                {
                    var sameHash = hashKvp.Value;
                    if (sameHash == null || sameHash.Count <= 1) continue;
                    var keep = PickDuplicateKeepCandidate(sameHash);
                    for (int i = 0; i < sameHash.Count; i++)
                    {
                        var c = sameHash[i];
                        if (c == null) continue;
                        hashMatched.Add(c);
                        if (ReferenceEquals(c, keep)) continue;
                        c.Flags.Add(CleanupCandidateType.Duplicate);
                        c.Reasons.Add("Duplicate UID+version (hash match): " + kvp.Key);
                    }
                }

                if (arr.Count > 1 && hashMatched.Count < arr.Count)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var c = arr[i];
                        if (c == null || hashMatched.Contains(c)) continue;
                        c.Flags.Add(CleanupCandidateType.Damaged);
                        c.Reasons.Add("UID+version collision with different file hash: " + kvp.Key);
                    }
                }
            }

            _clDiagVarDupHashGroupTicks += Stopwatch.GetTimestamp() - dupT0;

            long oldVerT0 = Stopwatch.GetTimestamp();
            foreach (var kvp in byGroup)
            {
                var arr = kvp.Value;
                if (arr == null || arr.Count <= 1) continue;
                int maxV = int.MinValue;
                for (int i = 0; i < arr.Count; i++)
                    maxV = Math.Max(maxV, arr[i].NumericVersion);
                if (maxV == int.MinValue) continue;

                for (int i = 0; i < arr.Count; i++)
                {
                    var c = arr[i];
                    if (c.NumericVersion < maxV)
                    {
                        c.Flags.Add(CleanupCandidateType.OldVersion);
                        c.Reasons.Add("Old version (latest is ." + maxV + ")");
                    }
                }
            }
            _clDiagVarOldVerTicks += Stopwatch.GetTimestamp() - oldVerT0;

            // DB + scan invalid union (conservative to avoid false positives)
            long invalidT0 = Stopwatch.GetTimestamp();
            try
            {
                var invalidByPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pkgs = FileManager.PackagesByUid;
                if (pkgs != null)
                {
                    foreach (var pkg in pkgs.Values)
                    {
                        if (pkg == null || !pkg.invalid || string.IsNullOrEmpty(pkg.Path)) continue;
                        invalidByPath.Add(NormalizePathSafe(pkg.Path));
                    }
                }

                foreach (string p in invalidByPath)
                {
                    bool isVarByExt = false;
                    try { isVarByExt = string.Equals(Path.GetExtension(p), ".var", StringComparison.OrdinalIgnoreCase); } catch { isVarByExt = false; }
                    if (!isVarByExt) continue;
                    var c = GetOrCreateCleanupCandidate(byPath, p, CleanupCandidateSourceKind.VarPackage);
                    if (c == null) continue;
                    c.Flags.Add(CleanupCandidateType.Damaged);
                    c.Reasons.Add("Marked invalid by scan/DB");
                    _clDiagVarInvalidPkgs++;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Cleanup: invalid package union failed: " + ex.Message);
            }
            _clDiagVarInvalidUnionTicks += Stopwatch.GetTimestamp() - invalidT0;
        }

        private void BuildLocalCleanupCandidates(Dictionary<string, CleanupCandidate> byPath)
        {
            string savesRoot = "";
            try { savesRoot = Path.GetFullPath("Saves"); } catch { savesRoot = "Saves"; }
            if (!Directory.Exists(savesRoot)) return;

            var byFileName = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);
            var byFamily = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);

            foreach (string p in EnumerateFilesCompat(savesRoot))
            {
                string norm = NormalizePathSafe(p);
                if (IsUnderDeletedFolders(norm)) continue;
                if (norm.IndexOf("/DeletedScenes/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (IsLocalMarkerFile(norm)) continue;

                CleanupCandidateSourceKind kind = GetLocalKindForPath(norm);
                var c = GetOrCreateCleanupCandidate(byPath, norm, kind);
                if (c == null) continue;

                string ext = "";
                string fn = "";
                try { fn = Path.GetFileName(norm); } catch { fn = norm; }
                if (string.IsNullOrEmpty(fn)) fn = norm;
                try { ext = (Path.GetExtension(norm) ?? "").ToLowerInvariant(); } catch { ext = ""; }

                if (!byFileName.TryGetValue(fn, out var d))
                {
                    d = new List<CleanupCandidate>();
                    byFileName[fn] = d;
                }
                d.Add(c);

                try
                {
                    string stem = Path.GetFileNameWithoutExtension(norm) ?? "";
                    if (TryParseNumericSuffixVersion(stem, out string famBase, out int ver))
                    {
                        c.NumericVersion = ver;
                        c.GroupKey = (famBase + "|" + ext).ToLowerInvariant();
                        if (!byFamily.TryGetValue(c.GroupKey, out var fam))
                        {
                            fam = new List<CleanupCandidate>();
                            byFamily[c.GroupKey] = fam;
                        }
                        fam.Add(c);
                    }
                }
                catch { }

                try
                {
                    var fi = new FileInfo(norm);
                    if (!fi.Exists)
                    {
                        c.Flags.Add(CleanupCandidateType.Damaged);
                        c.Reasons.Add("Missing local file");
                    }
                    else if (fi.Length <= 0)
                    {
                        c.Flags.Add(CleanupCandidateType.Damaged);
                        c.Reasons.Add("Zero-byte local file");
                    }
                }
                catch
                {
                    c.Flags.Add(CleanupCandidateType.Damaged);
                    c.Reasons.Add("Unreadable local file");
                }

                bool isSceneJson = kind == CleanupCandidateSourceKind.LocalScene;
                if (isSceneJson)
                {
                    try
                    {
                        long len = 0;
                        try { len = new FileInfo(norm).Length; } catch { len = 0; }
                        // Guard large files to keep global cleanup responsive.
                        if (len > 0 && len <= 4L * 1024L * 1024L)
                        {
                            string json = File.ReadAllText(norm);
                            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
                            {
                                c.Flags.Add(CleanupCandidateType.Damaged);
                                c.Reasons.Add("Empty scene JSON");
                            }
                            else
                            {
                                SimpleJSON.JSON.Parse(json);
                            }
                        }
                        else
                        {
                            c.Flags.Add(CleanupCandidateType.Damaged);
                            c.Reasons.Add("Unreadable or oversized scene JSON");
                        }
                    }
                    catch
                    {
                        c.Flags.Add(CleanupCandidateType.Damaged);
                        c.Reasons.Add("Scene JSON parse failed");
                    }
                }

            }

            foreach (var kvp in byFileName)
            {
                var arr = kvp.Value;
                if (arr == null || arr.Count <= 1) continue;

                var byHash = BuildDuplicateHashGroups(arr);
                foreach (var hashKvp in byHash)
                {
                    var sameHash = hashKvp.Value;
                    if (sameHash == null || sameHash.Count <= 1) continue;
                    for (int i = 0; i < sameHash.Count; i++)
                    {
                        sameHash[i].Flags.Add(CleanupCandidateType.Duplicate);
                        sameHash[i].Reasons.Add("Duplicate local filename (hash match): " + kvp.Key);
                    }
                }
            }

            foreach (var kvp in byFamily)
            {
                var arr = kvp.Value;
                if (arr == null || arr.Count <= 1) continue;
                int maxV = int.MinValue;
                for (int i = 0; i < arr.Count; i++) maxV = Math.Max(maxV, arr[i].NumericVersion);
                if (maxV == int.MinValue) continue;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i].NumericVersion < maxV)
                    {
                        arr[i].Flags.Add(CleanupCandidateType.OldVersion);
                        arr[i].Reasons.Add("Older numeric-suffix version (latest is " + maxV + ")");
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateFilesCompat(string root)
        {
            if (string.IsNullOrEmpty(root)) yield break;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                string[] files = null;
                string[] subdirs = null;
                try { files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); } catch { files = null; }
                try { subdirs = Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly); } catch { subdirs = null; }

                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        string p = files[i];
                        if (!string.IsNullOrEmpty(p)) yield return p;
                    }
                }

                if (subdirs != null)
                {
                    for (int i = 0; i < subdirs.Length; i++)
                    {
                        string d = subdirs[i];
                        if (!string.IsNullOrEmpty(d)) pending.Push(d);
                    }
                }
            }
        }

        private bool CleanupCandidatePassesTypeFilter(CleanupCandidate c)
        {
            if (c == null) return false;
            switch (cleanupFilterMode)
            {
                case 1: return c.HasFlag(CleanupCandidateType.Duplicate);
                case 2: return c.HasFlag(CleanupCandidateType.OldVersion);
                case 3: return c.HasFlag(CleanupCandidateType.Damaged);
                case 4:
                    if (!c.HasFlag(CleanupCandidateType.StaleCache)) return false;
                    if (cleanupStaleBucketMode <= 0) return true;
                    try
                    {
                        if (c.StaleCacheLastAccessedBinary == long.MinValue) return true;
                        DateTime last = DateTime.FromBinary(c.StaleCacheLastAccessedBinary);
                        int ageDays = (int)Math.Floor((DateTime.UtcNow - last.ToUniversalTime()).TotalDays);
                        if (ageDays < 0) ageDays = 0;
                        switch (cleanupStaleBucketMode)
                        {
                            case 1: return ageDays >= 7 && ageDays < 14;
                            case 2: return ageDays >= 14 && ageDays < 30;
                            case 3: return ageDays >= 30 && ageDays < 60;
                            case 4: return ageDays >= 60 && ageDays < 182;
                            case 5: return ageDays >= 182;
                            default: return true;
                        }
                    }
                    catch { return true; }
                case 5: return c.IsExcluded;
                default:
                    // "All" excludes stale cache; stale cache has its own tab + bucket subfilters.
                    return c.HasFlag(CleanupCandidateType.Duplicate)
                        || c.HasFlag(CleanupCandidateType.OldVersion)
                        || c.HasFlag(CleanupCandidateType.Damaged)
                        || c.IsExcluded;
            }
        }

        private static bool CleanupReasonMatchesType(string reason, CleanupCandidateType type)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            switch (type)
            {
                case CleanupCandidateType.Duplicate:
                    return reason.StartsWith("Duplicate UID+version", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Duplicate local filename", StringComparison.OrdinalIgnoreCase);
                case CleanupCandidateType.OldVersion:
                    return reason.StartsWith("Old version", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Older numeric-suffix version", StringComparison.OrdinalIgnoreCase);
                case CleanupCandidateType.StaleCache:
                    return reason.StartsWith("Stale Texture Cache", StringComparison.OrdinalIgnoreCase);
                default:
                    return reason.StartsWith("Invalid VAR filename format", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("UID+version collision with different file hash", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Marked invalid by scan/DB", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Missing local file", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Zero-byte local file", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Unreadable local file", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Empty scene JSON", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Unreadable or oversized scene JSON", StringComparison.OrdinalIgnoreCase)
                        || reason.StartsWith("Scene JSON parse failed", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string BuildCleanupReasonLine(string label, HashSet<string> allReasons, CleanupCandidateType type)
        {
            if (allReasons == null || allReasons.Count == 0) return "";
            var matched = new List<string>();
            foreach (string reason in allReasons)
            {
                if (!CleanupReasonMatchesType(reason, type)) continue;
                matched.Add(reason);
            }
            if (matched.Count == 0) return "";
            return label + ": " + string.Join("; ", matched.ToArray());
        }

        private string BuildCleanupHoverDetails(CleanupCandidate candidate)
        {
            if (candidate == null) return "";
            var lines = new List<string>();
            if (candidate.HasFlag(CleanupCandidateType.Damaged))
            {
                string line = BuildCleanupReasonLine("Damaged", candidate.Reasons, CleanupCandidateType.Damaged);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            if (candidate.HasFlag(CleanupCandidateType.Duplicate))
            {
                string line = BuildCleanupReasonLine("Duplicate", candidate.Reasons, CleanupCandidateType.Duplicate);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            if (candidate.HasFlag(CleanupCandidateType.OldVersion))
            {
                string line = BuildCleanupReasonLine("Old", candidate.Reasons, CleanupCandidateType.OldVersion);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            if (candidate.HasFlag(CleanupCandidateType.StaleCache))
            {
                string line = BuildCleanupReasonLine("Stale Cache", candidate.Reasons, CleanupCandidateType.StaleCache);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            if (candidate.IsExcluded) lines.Add("Excluded: manually excluded from cleanup");
            return string.Join("\n", lines.ToArray());
        }

        private int GetCleanupFilterMode()
        {
            return cleanupFilterMode;
        }

        private int GetCleanupTabCount(int mode)
        {
            if (cleanupCandidatesAll == null || cleanupCandidatesAll.Count == 0) return 0;
            switch (mode)
            {
                case 1: return cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.Duplicate));
                case 2: return cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.OldVersion));
                case 3: return cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.Damaged));
                case 4: return cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.StaleCache));
                case 5: return cleanupCandidatesAll.Count(c => c.IsExcluded);
                default:
                    return cleanupCandidatesAll.Count(c =>
                        c.HasFlag(CleanupCandidateType.Duplicate) ||
                        c.HasFlag(CleanupCandidateType.OldVersion) ||
                        c.HasFlag(CleanupCandidateType.Damaged) ||
                        c.IsExcluded);
            }
        }

        private int GetCleanupStaleBucketCount(int bucketMode)
        {
            if (cleanupCandidatesAll == null || cleanupCandidatesAll.Count == 0) return 0;
            int cnt = 0;
            for (int i = 0; i < cleanupCandidatesAll.Count; i++)
            {
                var c = cleanupCandidatesAll[i];
                if (c == null || !c.HasFlag(CleanupCandidateType.StaleCache)) continue;
                if (bucketMode <= 0)
                {
                    cnt++;
                    continue;
                }
                try
                {
                    if (c.StaleCacheLastAccessedBinary == long.MinValue)
                    {
                        cnt++;
                        continue;
                    }
                    DateTime last = DateTime.FromBinary(c.StaleCacheLastAccessedBinary);
                    int ageDays = (int)Math.Floor((DateTime.UtcNow - last.ToUniversalTime()).TotalDays);
                    if (ageDays < 0) ageDays = 0;
                    bool ok = false;
                    switch (bucketMode)
                    {
                        case 1: ok = ageDays >= 7 && ageDays < 14; break;
                        case 2: ok = ageDays >= 14 && ageDays < 30; break;
                        case 3: ok = ageDays >= 30 && ageDays < 60; break;
                        case 4: ok = ageDays >= 60 && ageDays < 182; break;
                        case 5: ok = ageDays >= 182; break;
                        default: ok = true; break;
                    }
                    if (ok) cnt++;
                }
                catch { cnt++; }
            }
            return cnt;
        }

        private void SetCleanupFilterMode(int mode, bool clearSelection = true)
        {
            cleanupFilterMode = (mode >= 0 && mode <= 5) ? mode : 0;
            if (cleanupFilterMode != 4) cleanupStaleBucketMode = 0;
            ApplyCleanupFilterToList(clearSelection);
        }

        private int GetCleanupStaleBucketMode()
        {
            return cleanupStaleBucketMode;
        }

        private void SetCleanupStaleBucketMode(int mode, bool clearSelection = true)
        {
            cleanupStaleBucketMode = (mode >= 0 && mode <= 5) ? mode : 0;
            if (cleanupFilterMode != 4) cleanupStaleBucketMode = 0;
            ApplyCleanupFilterToList(clearSelection);
        }

        private void CloseImportSidebarForCleanup()
        {
            if (!importSidebarOpenIntent && !importSidebarActive) return;
            importSidebarOpenIntent = false;
            importSidebarOpenIntentLoaded = true;
            try { PersistImportSidebarOpenIntent(); } catch { }
            try { RefreshImportSidebarCategoryGate(); } catch { }
        }

        private void ActivateCleanupSideTabs()
        {
            CloseImportSidebarForCleanup();
            cleanupSideHostIsLeft = isFixedLocally;
            if (cleanupSideHostIsLeft)
            {
                cleanupPrevHostContent = leftActiveContent;
                leftActiveContent = ContentType.CleanupCategories;
                if (rightActiveContent == ContentType.CleanupCategories || rightActiveContent == ContentType.CleanupStaleBuckets)
                    rightActiveContent = null;
            }
            else
            {
                cleanupPrevHostContent = rightActiveContent;
                rightActiveContent = ContentType.CleanupCategories;
                if (leftActiveContent == ContentType.CleanupCategories || leftActiveContent == ContentType.CleanupStaleBuckets)
                    leftActiveContent = null;
            }
            SyncActiveContentTypeFromSidePanels();
            try { UpdateLayout(); } catch { }
            try { UpdateTabs(); } catch { }
        }

        private void RebuildCleanupCandidates(bool clearSelection, bool showSummaryStatus, bool testMode = false)
        {
            if (!cleanupModeActive) return;
            var sw = Stopwatch.StartNew();

            bool loadedFromCache = TryLoadCleanupCandidatesFromCache(testMode);
            if (!loadedFromCache)
            {
                cleanupCandidatesAll.Clear();
                cleanupCandidatesAll.AddRange(BuildCleanupCandidatesGlobal(testMode));
                SaveCleanupCandidatesToCache();
            }

            // Exclude status may change independently of package refresh.
            RefreshCleanupExcludedKeyCache();
            for (int i = 0; i < cleanupCandidatesAll.Count; i++)
            {
                var c = cleanupCandidatesAll[i];
                if (c == null) continue;
                c.IsExcluded = IsCleanupCandidateExcluded(c);
            }

            ApplyCleanupFilterToList(clearSelection);
            if (showSummaryStatus)
                ShowTemporaryStatus(BuildCleanupSummaryText(), 3f);
            sw.Stop();
            LogUtil.LogWarning("[VPB] Cleanup(list) rebuild done in " + sw.ElapsedMilliseconds + " ms. source=" + (loadedFromCache ? "cache" : "scan"));
        }

        private void ApplyCleanupFilterToList(bool clearSelection)
        {
            if (!cleanupModeActive) return;

            cleanupCandidateByPath.Clear();
            var filteredEntries = new List<FileEntry>();
            for (int i = 0; i < cleanupCandidatesAll.Count; i++)
            {
                var c = cleanupCandidatesAll[i];
                if (!CleanupCandidatePassesTypeFilter(c)) continue;
                cleanupCandidateByPath[c.NormalizedPath] = c;
                filteredEntries.Add(new CleanupFileEntry(c));
            }

            if (clearSelection)
            {
                try
                {
                    if (selectedFiles != null) selectedFiles.Clear();
                    if (selectedFilePaths != null) selectedFilePaths.Clear();
                    selectionAnchorPath = null;
                    selectionAnchorIdentityKey = null;
                }
                catch { }
            }

            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(filteredEntries);

            if (recyclingGrid != null)
            {
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                recyclingGrid.Refresh();
            }

            try { RefreshSelectionVisuals(); } catch { }
            try { UpdatePaginationText(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
            try { UpdateTabs(); } catch { }
        }

        private string BuildCleanupSummaryText()
        {
            int total = cleanupCandidatesAll.Count;
            int dup = cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.Duplicate));
            int old = cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.OldVersion));
            int dmg = cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.Damaged));
            int stale = cleanupCandidatesAll.Count(c => c.HasFlag(CleanupCandidateType.StaleCache));
            return string.Format(
                VPBTranslation.T("gallery.cleanup.summary", "Cleanup candidates: {0} total ({1} duplicates, {2} old, {3} damaged, {4} stale cache)"),
                total, dup, old, dmg, stale);
        }

        // Cleanup view is a temporary task-oriented list. Keep the user's original layout
        // and restore it once cleanup mode exits.
        private bool cleanupTemporaryLayoutSessionActive;
        private GalleryLayoutMode cleanupTemporaryPrevLayoutMode;

        private void EnsureTemporaryTaskListLayoutSession()
        {
            if (!cleanupTemporaryLayoutSessionActive)
            {
                cleanupTemporaryPrevLayoutMode = layoutMode;
                cleanupTemporaryLayoutSessionActive = true;
            }

            if (layoutMode != GalleryLayoutMode.List)
            {
                // Must use SetLayoutMode so RecyclingGridView config switches to list row mode.
                try { SetLayoutMode(GalleryLayoutMode.List, persistConfig: false, keepInternalSettingsMode: true); } catch { }
            }
        }

        private void TryRestoreLayoutAfterTemporaryTaskExit()
        {
            if (!cleanupTemporaryLayoutSessionActive) return;

            cleanupTemporaryLayoutSessionActive = false;
            if (layoutMode != cleanupTemporaryPrevLayoutMode)
            {
                try { SetLayoutMode(cleanupTemporaryPrevLayoutMode, persistConfig: false, keepInternalSettingsMode: true); } catch { }
            }
        }

        private void TboxOpenCleanupView()
        {
            if (cleanupScanInProgress) return;
            if (!GateStickyEnterWhileTryOn(StickyToolMode.Cleanup)) return;
            cleanupScanInProgress = true;
            var openSw = Stopwatch.StartNew();
            try
            {
                if (settingsListViewActive)
                    ExitInternalSettingsMode(true);

                try { ExitOtherStickyToolModes(StickyToolMode.Cleanup); } catch { }

                cleanupModeActive = true;
                cleanupFilterMode = 0;
                try { RefreshModeAmbientChrome(); } catch { }
                LogUtil.LogWarning("[VPB] Cleanup(list) open: starting scan...");

                try { EnsureTemporaryTaskListLayoutSession(); } catch { }

                cleanupPrevCategoryTitle = currentCategoryTitle;
                cleanupPrevExtension = currentExtension;
                cleanupPrevPath = currentPath;
                currentCategoryTitle = VPBTranslation.T("gallery.tbox.cleanup", "Cleanup");
                if (titleText != null) titleText.text = currentCategoryTitle;
                ActivateCleanupSideTabs();
                
                bool testMode = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (testMode) LogUtil.LogWarning("[VPB] Cleanup: Test Mode Enabled (0-day threshold)");

                RebuildCleanupCandidates(true, true, testMode);
                openSw.Stop();
                LogUtil.LogWarning("[VPB] Cleanup(list) open: scan ready in " + openSw.ElapsedMilliseconds + " ms.");
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Cleanup view open failed: " + ex);
                ShowTemporaryStatus(VPBTranslation.T("gallery.cleanup.scan_failed", "Cleanup scan failed. See log."), 2f);
                try { ExitCleanupModeForSidePanelNavigation(); } catch { }
            }
            finally
            {
                cleanupScanInProgress = false;
            }
        }

        private void ExitCleanupModeForSidePanelNavigation(bool restoreGalleryCategory = true, bool refreshGalleryFiles = true)
        {
            if (!cleanupModeActive) return;

            cleanupModeActive = false;
            cleanupScanInProgress = false;
            cleanupFilterMode = 0;
            cleanupStaleBucketMode = 0;
            cleanupCandidatesAll.Clear();
            cleanupCandidateByPath.Clear();
            try { RefreshModeAmbientChrome(); } catch { }

            if (cleanupSideHostIsLeft)
            {
                if (leftActiveContent.HasValue && leftActiveContent.Value == ContentType.CleanupCategories)
                    leftActiveContent = cleanupPrevHostContent;
                else if (leftActiveContent == ContentType.CleanupStaleBuckets)
                    leftActiveContent = cleanupPrevHostContent;
            }
            else
            {
                if (rightActiveContent.HasValue && rightActiveContent.Value == ContentType.CleanupCategories)
                    rightActiveContent = cleanupPrevHostContent;
                else if (rightActiveContent == ContentType.CleanupStaleBuckets)
                    rightActiveContent = cleanupPrevHostContent;
            }
            if (leftActiveContent == ContentType.CleanupCategories || leftActiveContent == ContentType.CleanupStaleBuckets)
                leftActiveContent = null;
            if (rightActiveContent == ContentType.CleanupCategories || rightActiveContent == ContentType.CleanupStaleBuckets)
                rightActiveContent = null;

            cleanupPrevHostContent = null;
            SyncActiveContentTypeFromSidePanels();

            if (restoreGalleryCategory && !string.IsNullOrEmpty(cleanupPrevCategoryTitle))
            {
                currentCategoryTitle = cleanupPrevCategoryTitle;
                if (!string.IsNullOrEmpty(cleanupPrevExtension)) currentExtension = cleanupPrevExtension;
                if (!string.IsNullOrEmpty(cleanupPrevPath)) currentPath = cleanupPrevPath;
                if (titleText != null) titleText.text = currentCategoryTitle;
            }
            cleanupPrevCategoryTitle = null;
            cleanupPrevExtension = null;
            cleanupPrevPath = null;

            TryRestoreLayoutAfterTemporaryTaskExit();

            try { ResetArmedApplySemanticsIfIdle(toast: true); } catch { }

            if (refreshGalleryFiles)
                try { RefreshFiles(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
            try { UpdateLayout(); } catch { }
            try { UpdateTabs(); } catch { }
        }

        private void TboxSelectAllVisibleCleanup()
        {
            if (!cleanupModeActive) return;
            try
            {
                if (selectedFiles == null) selectedFiles = new List<FileEntry>();
                if (selectedFilePaths == null) selectedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    var f = currentFilteredFiles[i];
                    if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                    selectedFiles.Add(f);
                    selectedFilePaths.Add(f.Path);
                }
                try { RefreshSelectionVisuals(); } catch { }
                ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.cleanup.selected_visible", "Selected {0} visible cleanup items."), selectedFiles.Count), 1.25f);
            }
            catch { }
        }

        private void TboxSelectCleanupByType(CleanupCandidateType t)
        {
            if (!cleanupModeActive) return;
            try
            {
                if (selectedFiles == null) selectedFiles = new List<FileEntry>();
                if (selectedFilePaths == null) selectedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                selectedFiles.Clear();
                selectedFilePaths.Clear();

                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    var f = currentFilteredFiles[i];
                    if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                    string norm = NormalizePathSafe(f.Path);
                    if (!cleanupCandidateByPath.TryGetValue(norm, out var c)) continue;
                    if (!c.HasFlag(t)) continue;
                    selectedFiles.Add(f);
                    selectedFilePaths.Add(f.Path);
                }

                try { RefreshSelectionVisuals(); } catch { }
                ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.cleanup.selected_type", "Selected {0} cleanup items."), selectedFiles.Count), 1.25f);
            }
            catch { }
        }

        private void TboxClearCleanupSelection()
        {
            if (!cleanupModeActive) return;
            try
            {
                if (selectedFiles != null) selectedFiles.Clear();
                if (selectedFilePaths != null) selectedFilePaths.Clear();
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                try { RefreshSelectionVisuals(); } catch { }
            }
            catch { }
        }

        private void TboxToggleCleanupFilterAll()
        {
            if (!cleanupModeActive) return;
            SetCleanupFilterMode(0, true);
        }

        private void TboxToggleCleanupFilterDuplicate()
        {
            if (!cleanupModeActive) return;
            SetCleanupFilterMode(1, true);
        }

        private void TboxToggleCleanupFilterOld()
        {
            if (!cleanupModeActive) return;
            SetCleanupFilterMode(2, true);
        }

        private void TboxToggleCleanupFilterDamaged()
        {
            if (!cleanupModeActive) return;
            SetCleanupFilterMode(3, true);
        }

        private void TboxToggleCleanupFilterExcluded()
        {
            if (!cleanupModeActive) return;
            SetCleanupFilterMode(4, true);
        }

        private bool TryMoveCleanupCandidateFile(CleanupCandidate c, out bool moved, out string failReason, out string dstPath)
        {
            moved = false;
            failReason = null;
            dstPath = null;
            if (c == null || string.IsNullOrEmpty(c.SourcePath))
            {
                failReason = "missing source";
                return false;
            }

            if (c.SourceKind == CleanupCandidateSourceKind.StaleCache)
            {
                try
                {
                    if (File.Exists(c.SourcePath))
                    {
                        File.Delete(c.SourcePath);
                        string meta = c.SourcePath + "meta";
                        if (File.Exists(meta)) File.Delete(meta);
                        VpbLocalDatabase.TryDeleteCacheUsage(c.SourcePath);
                        moved = true;
                        return true;
                    }
                    else
                    {
                        VpbLocalDatabase.TryDeleteCacheUsage(c.SourcePath);
                        moved = true; // Already gone
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    failReason = "delete failed: " + ex.Message;
                    return false;
                }
            }

            if (!File.Exists(c.SourcePath))
            {
                failReason = "missing source file";
                return false;
            }

            string rootFolder = c.SourceKind == CleanupCandidateSourceKind.VarPackage ? "DeletedPackages" : "DeletedScenes";
            string typeFolder = CleanupTypeFolderName(c.PrimaryType);
            string targetDir = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), rootFolder), typeFolder);
            try
            {
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex)
            {
                failReason = "mkdir failed: " + ex.Message;
                return false;
            }

            string fileName = Path.GetFileName(c.SourcePath);
            if (string.IsNullOrEmpty(fileName))
            {
                failReason = "bad file name";
                return false;
            }

            string dst = PickUniqueDestPath(targetDir, fileName);
            bool ok = TryMoveFileRobust(c.SourcePath, dst, "Cleanup move");
            if (!ok)
            {
                failReason = "move failed";
                return false;
            }
            moved = true;
            dstPath = dst;

            // Local scene sidecars are moved together when possible.
            if (c.SourceKind == CleanupCandidateSourceKind.LocalScene)
            {
                try
                {
                    string srcBase = Path.Combine(Path.GetDirectoryName(c.SourcePath) ?? "", Path.GetFileNameWithoutExtension(c.SourcePath) ?? "");
                    string dstBase = Path.Combine(Path.GetDirectoryName(dst) ?? "", Path.GetFileNameWithoutExtension(dst) ?? "");
                    TryMoveFileRobust(srcBase + ".jpg", dstBase + ".jpg", "Cleanup sidecar");
                    TryMoveFileRobust(srcBase + ".png", dstBase + ".png", "Cleanup sidecar");
                    TryMoveFileRobust(c.SourcePath + ".fav", dst + ".fav", "Cleanup sidecar");
                    TryMoveFileRobust(c.SourcePath + ".hide", dst + ".hide", "Cleanup sidecar");
                }
                catch { }
            }

            return true;
        }

        private List<CleanupCandidate> GetSelectedCleanupCandidates()
        {
            var list = new List<CleanupCandidate>();
            if (selectedFiles == null || selectedFiles.Count == 0) return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                var f = selectedFiles[i];
                if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                string norm = NormalizePathSafe(f.Path);
                if (!seen.Add(norm)) continue;
                if (cleanupCandidateByPath.TryGetValue(norm, out var c))
                    list.Add(c);
            }
            return list;
        }

        private void TboxApplyCleanupSelected()
        {
            if (!cleanupModeActive) return;

            var selected = GetSelectedCleanupCandidates();
            if (selected.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.cleanup.no_selection", "No cleanup items selected."), 1.5f);
                return;
            }

            int dup = selected.Count(c => c.HasFlag(CleanupCandidateType.Duplicate));
            int old = selected.Count(c => c.HasFlag(CleanupCandidateType.OldVersion));
            int dmg = selected.Count(c => c.HasFlag(CleanupCandidateType.Damaged));
            int stale = selected.Count(c => c.HasFlag(CleanupCandidateType.StaleCache));

            string msg = string.Format(
                VPBTranslation.T("gallery.cleanup.confirm_v3", "Perform cleanup for {0} selected items?\n\nDuplicates: {1}\nOld Versions: {2}\nDamaged: {3}\nStale Cache: {4}\n\n- .var packages: Move to 'DeletedPackages'\n- Local scenes/files: Move to 'DeletedScenes'\n- Stale texture cache: Permanent Delete"),
                selected.Count, dup, old, dmg, stale);

            DisplayConfirm(VPBTranslation.T("gallery.cleanup.confirm_title", "Cleanup"), msg, () =>
            {
                int moved = 0;
                int failed = 0;
                var movedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var undoPairs = new List<FileMoveUndoPair>();
                for (int i = 0; i < selected.Count; i++)
                {
                    string dstPath;
                    if (TryMoveCleanupCandidateFile(selected[i], out bool ok, out string failReason, out dstPath) && ok)
                    {
                        moved++;
                        try
                        {
                            string p = NormalizePathSafe(selected[i].SourcePath);
                            if (!string.IsNullOrEmpty(p)) movedPaths.Add(p);
                        }
                        catch { }
                        if (!string.IsNullOrEmpty(dstPath) && !string.IsNullOrEmpty(selected[i].SourcePath)
                            && selected[i].SourceKind != CleanupCandidateSourceKind.StaleCache)
                        {
                            FileMoveUndoPair pair;
                            pair.FromDeletedPath = dstPath;
                            pair.ToOriginalPath = selected[i].SourcePath;
                            undoPairs.Add(pair);
                        }
                    }
                    else
                    {
                        failed++;
                        if (!string.IsNullOrEmpty(failReason))
                            LogUtil.LogWarning("[VPB] Cleanup move failed for " + selected[i].SourcePath + ": " + failReason);
                    }
                }

                if (movedPaths.Count > 0)
                {
                    InvalidateCleanupCandidatesCache();
                    cleanupCandidatesAll.RemoveAll(c => c != null && movedPaths.Contains(c.NormalizedPath));
                }
                ApplyCleanupFilterToList(true);

                try
                {
                    PushFileMoveUndo(
                        undoPairs,
                        VPBTranslation.T("gallery.undo.cleanup", "Cleanup"),
                        "tbox_undo_cleanup");
                }
                catch { }

                if (failed == 0)
                    ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.cleanup.done_ok", "Cleanup moved {0} item(s). Undo to restore{hint:undo}."), moved), 2f);
                else
                    ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.cleanup.done_partial", "Cleanup moved {0} item(s), {1} failed. See log."), moved, failed), 3f);
            });
        }

        private void TboxCleanupSetSelectedExcludeTag(bool add)
        {
            if (!cleanupModeActive) return;
            var totalSw = Stopwatch.StartNew();

            var selected = GetSelectedCleanupCandidates();
            if (selected.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.cleanup.no_selection", "No cleanup items selected."), 1.5f);
                return;
            }

            int changed = 0;
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selected.Count; i++)
            {
                var c = selected[i];
                if (c == null) continue;
                foreach (string key in EnumerateCleanupExcludeKeys(c))
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    keys.Add(key);
                }
            }

            var writeSw = Stopwatch.StartNew();
            var keyList = keys.ToList();
            if (add) VpbLocalDatabase.TryAddCleanupExcludes(keyList);
            else VpbLocalDatabase.TryRemoveCleanupExcludes(keyList);
            writeSw.Stop();

            RefreshCleanupExcludedKeyCache();
            for (int i = 0; i < cleanupCandidatesAll.Count; i++)
            {
                var c = cleanupCandidatesAll[i];
                if (c == null) continue;
                bool before = c.IsExcluded;
                bool after = IsCleanupCandidateExcluded(c);
                c.IsExcluded = after;
                if (before != after) changed++;
            }
            ApplyCleanupFilterToList(false);
            totalSw.Stop();
            LogUtil.LogWarning("[VPB] Cleanup(list) exclude " + (add ? "add" : "remove") + " | keys=" + keyList.Count + " | changed=" + changed + " | db=" + writeSw.ElapsedMilliseconds + " ms | total=" + totalSw.ElapsedMilliseconds + " ms");

            if (add)
                ShowTemporaryStatus(changed > 0
                    ? string.Format(VPBTranslation.T("gallery.cleanup.exclude_added", "Added {0} selected item(s) to Exclude."), changed)
                    : VPBTranslation.T("gallery.cleanup.exclude_added_none", "Selected items are already in Exclude."),
                    2f);
            else
                ShowTemporaryStatus(changed > 0
                    ? string.Format(VPBTranslation.T("gallery.cleanup.exclude_removed", "Removed {0} selected item(s) from Exclude."), changed)
                    : VPBTranslation.T("gallery.cleanup.exclude_removed_none", "Selected items were not in Exclude."),
                    2f);
        }

        private bool CleanupSelectionHasExcludedEntries()
        {
            var selected = GetSelectedCleanupCandidates();
            for (int i = 0; i < selected.Count; i++)
                if (selected[i] != null && selected[i].IsExcluded) return true;
            return false;
        }

        private bool CleanupSelectionHasNonExcludedEntries()
        {
            var selected = GetSelectedCleanupCandidates();
            for (int i = 0; i < selected.Count; i++)
                if (selected[i] != null && !selected[i].IsExcluded) return true;
            return false;
        }

        private void TboxCleanupAddSelectedToExclude()
        {
            TboxCleanupSetSelectedExcludeTag(true);
        }

        private void TboxCleanupRemoveSelectedFromExclude()
        {
            TboxCleanupSetSelectedExcludeTag(false);
        }
    }
}
