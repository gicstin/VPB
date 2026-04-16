using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private enum CleanupCandidateType
        {
            Duplicate,
            OldVersion,
            Damaged
        }

        private enum CleanupCandidateSourceKind
        {
            VarPackage,
            LocalScene,
            LocalFile
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

            public bool HasFlag(CleanupCandidateType t) => Flags.Contains(t);

            public CleanupCandidateType PrimaryType
            {
                get
                {
                    if (Flags.Contains(CleanupCandidateType.Damaged)) return CleanupCandidateType.Damaged;
                    if (Flags.Contains(CleanupCandidateType.Duplicate)) return CleanupCandidateType.Duplicate;
                    return CleanupCandidateType.OldVersion;
                }
            }

            public string GetFlagsLabel()
            {
                bool d = Flags.Contains(CleanupCandidateType.Duplicate);
                bool o = Flags.Contains(CleanupCandidateType.OldVersion);
                bool z = Flags.Contains(CleanupCandidateType.Damaged);
                string baseLabel;
                if (d && o && z) baseLabel = "Duplicate | Old Version | Damaged";
                else if (d && z) baseLabel = "Duplicate | Damaged";
                else if (o && z) baseLabel = "Old Version | Damaged";
                else if (d && o) baseLabel = "Duplicate | Old Version";
                else if (z) baseLabel = "Damaged";
                else if (d) baseLabel = "Duplicate";
                else baseLabel = "Old Version";
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
        private bool cleanupScanInProgress;
        private bool cleanupSideHostIsLeft;
        private ContentType? cleanupPrevHostContent;
        private const string CleanupExcludeTag = "Exclude";
        private readonly HashSet<string> cleanupExcludedKeyCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CleanupCandidate> cleanupCandidatesAll = new List<CleanupCandidate>();
        private readonly Dictionary<string, CleanupCandidate> cleanupCandidateByPath =
            new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);

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
            if (!int.TryParse(parts[2], out version)) return false;

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

        private List<CleanupCandidate> BuildCleanupCandidatesGlobal()
        {
            var totalSw = Stopwatch.StartNew();
            var byPath = new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);

            var varSw = Stopwatch.StartNew();
            BuildVarCleanupCandidates(byPath);
            varSw.Stop();
            var localSw = Stopwatch.StartNew();
            BuildLocalCleanupCandidates(byPath);
            localSw.Stop();
            RefreshCleanupExcludedKeyCache();

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
            totalSw.Stop();
            LogUtil.LogWarning("[VPB] Cleanup(list) scan done | var=" + varSw.ElapsedMilliseconds + " ms | local=" + localSw.ElapsedMilliseconds + " ms | total=" + totalSw.ElapsedMilliseconds + " ms | candidates=" + list.Count);
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

        private void BuildVarCleanupCandidates(Dictionary<string, CleanupCandidate> byPath)
        {
            var allVarPaths = new List<string>();
            try
            {
                if (Directory.Exists("AllPackages")) FileManager.SafeGetFiles("AllPackages", "*.var", allVarPaths);
                if (Directory.Exists("AddonPackages")) FileManager.SafeGetFiles("AddonPackages", "*.var", allVarPaths);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Cleanup: var scan failed: " + ex.Message);
            }

            var byUidVersion = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);
            var byGroup = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);

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
                    c.Flags.Add(CleanupCandidateType.Damaged);
                    c.Reasons.Add("Invalid VAR filename format");
                }
            }

            foreach (var kvp in byUidVersion)
            {
                var arr = kvp.Value;
                if (arr == null || arr.Count <= 1) continue;
                var keep = PickDuplicateKeepCandidate(arr);
                for (int i = 0; i < arr.Count; i++)
                {
                    var c = arr[i];
                    if (c == null) continue;
                    if (ReferenceEquals(c, keep)) continue;
                    c.Flags.Add(CleanupCandidateType.Duplicate);
                    c.Reasons.Add("Duplicate UID+version: " + kvp.Key);
                }
            }

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

            // DB + scan invalid union (conservative to avoid false positives)
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

                    // Keep SQL touch lightweight for this path: it is used as accelerator metadata,
                    // but damaged classification only trusts explicit invalid markers to prevent noise.
                    try
                    {
                        var allUids = new HashSet<string>(pkgs.Keys, StringComparer.OrdinalIgnoreCase);
                        var rows = new List<VpbLocalDatabase.PackageRow>();
                        VpbLocalDatabase.TryQueryPackageRowsForUids(allUids, rows);
                    }
                    catch { }
                }

                foreach (string p in invalidByPath)
                {
                    var c = GetOrCreateCleanupCandidate(byPath, p, CleanupCandidateSourceKind.VarPackage);
                    if (c == null) continue;
                    c.Flags.Add(CleanupCandidateType.Damaged);
                    c.Reasons.Add("Marked invalid by scan/DB");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Cleanup: invalid package union failed: " + ex.Message);
            }
        }

        private void BuildLocalCleanupCandidates(Dictionary<string, CleanupCandidate> byPath)
        {
            string savesRoot = "";
            try { savesRoot = Path.GetFullPath("Saves"); } catch { savesRoot = "Saves"; }
            if (!Directory.Exists(savesRoot)) return;

            var byFileName = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);
            var byFamily = new Dictionary<string, List<CleanupCandidate>>(StringComparer.OrdinalIgnoreCase);
            var scenePreviewCandidates = new List<CleanupCandidate>();

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

                if (ext == ".jpg" || ext == ".png")
                {
                    scenePreviewCandidates.Add(c);
                }
            }

            // Missing paired-assets criterion: preview image without matching scene JSON.
            for (int i = 0; i < scenePreviewCandidates.Count; i++)
            {
                var c = scenePreviewCandidates[i];
                if (c == null || string.IsNullOrEmpty(c.NormalizedPath)) continue;
                string low = c.NormalizedPath.ToLowerInvariant();
                if (low.IndexOf("/saves/scene/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    string stem = Path.Combine(Path.GetDirectoryName(c.NormalizedPath) ?? "", Path.GetFileNameWithoutExtension(c.NormalizedPath) ?? "");
                    string sceneJson = stem + ".json";
                    if (!File.Exists(sceneJson))
                    {
                        c.Flags.Add(CleanupCandidateType.Damaged);
                        c.Reasons.Add("Preview image without matching scene JSON");
                    }
                }
                catch { }
            }

            foreach (var kvp in byFileName)
            {
                var arr = kvp.Value;
                if (arr == null || arr.Count <= 1) continue;
                for (int i = 0; i < arr.Count; i++)
                {
                    arr[i].Flags.Add(CleanupCandidateType.Duplicate);
                    arr[i].Reasons.Add("Duplicate local filename: " + kvp.Key);
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
                case 4: return c.IsExcluded;
                default:
                    return c.HasFlag(CleanupCandidateType.Duplicate)
                        || c.HasFlag(CleanupCandidateType.OldVersion)
                        || c.HasFlag(CleanupCandidateType.Damaged)
                        || c.IsExcluded;
            }
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
                case 4: return cleanupCandidatesAll.Count(c => c.IsExcluded);
                default:
                    return cleanupCandidatesAll.Count(c =>
                        c.HasFlag(CleanupCandidateType.Duplicate) ||
                        c.HasFlag(CleanupCandidateType.OldVersion) ||
                        c.HasFlag(CleanupCandidateType.Damaged) ||
                        c.IsExcluded);
            }
        }

        private void SetCleanupFilterMode(int mode, bool clearSelection = true)
        {
            cleanupFilterMode = (mode >= 0 && mode <= 4) ? mode : 0;
            ApplyCleanupFilterToList(clearSelection);
        }

        private void ActivateCleanupSideTabs()
        {
            cleanupSideHostIsLeft = isFixedLocally;
            if (cleanupSideHostIsLeft)
            {
                cleanupPrevHostContent = leftActiveContent;
                leftActiveContent = ContentType.CleanupCategories;
            }
            else
            {
                cleanupPrevHostContent = rightActiveContent;
                rightActiveContent = ContentType.CleanupCategories;
            }
            try { UpdateTabs(); } catch { }
        }

        private void RebuildCleanupCandidates(bool clearSelection, bool showSummaryStatus)
        {
            if (!cleanupModeActive) return;
            var sw = Stopwatch.StartNew();
            cleanupCandidatesAll.Clear();
            cleanupCandidatesAll.AddRange(BuildCleanupCandidatesGlobal());
            ApplyCleanupFilterToList(clearSelection);
            if (showSummaryStatus)
                ShowTemporaryStatus(BuildCleanupSummaryText(), 3f);
            sw.Stop();
            LogUtil.LogWarning("[VPB] Cleanup(list) rebuild done in " + sw.ElapsedMilliseconds + " ms.");
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
            return string.Format(
                VPBTranslation.T("gallery.cleanup.summary", "Cleanup candidates: {0} total ({1} duplicates, {2} old, {3} damaged)"),
                total, dup, old, dmg);
        }

        private void TboxOpenCleanupView()
        {
            if (cleanupScanInProgress) return;
            cleanupScanInProgress = true;
            var openSw = Stopwatch.StartNew();
            try
            {
                cleanupModeActive = true;
                cleanupFilterMode = 0;
                LogUtil.LogWarning("[VPB] Cleanup(list) open: starting scan...");

                try { SetLayoutMode(GalleryLayoutMode.List); } catch { }

                currentCategoryTitle = VPBTranslation.T("gallery.tbox.cleanup", "Cleanup");
                ActivateCleanupSideTabs();
                RebuildCleanupCandidates(true, true);
                openSw.Stop();
                LogUtil.LogWarning("[VPB] Cleanup(list) open: scan ready in " + openSw.ElapsedMilliseconds + " ms.");
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Cleanup view open failed: " + ex);
                ShowTemporaryStatus(VPBTranslation.T("gallery.cleanup.scan_failed", "Cleanup scan failed. See log."), 2f);
            }
            finally
            {
                cleanupScanInProgress = false;
            }
        }

        private void ExitCleanupModeForSidePanelNavigation()
        {
            if (!cleanupModeActive) return;

            cleanupModeActive = false;
            cleanupScanInProgress = false;
            cleanupFilterMode = 0;
            cleanupCandidatesAll.Clear();
            cleanupCandidateByPath.Clear();

            if (leftActiveContent.HasValue && leftActiveContent.Value == ContentType.CleanupCategories)
                leftActiveContent = null;
            if (rightActiveContent.HasValue && rightActiveContent.Value == ContentType.CleanupCategories)
                rightActiveContent = null;

            cleanupPrevHostContent = null;

            // Return to the normal gallery dataset after leaving cleanup mode.
            try { RefreshFiles(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
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

        private bool TryMoveCleanupCandidateFile(CleanupCandidate c, out bool moved, out string failReason)
        {
            moved = false;
            failReason = null;
            if (c == null || string.IsNullOrEmpty(c.SourcePath))
            {
                failReason = "missing source";
                return false;
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

            string msg = string.Format(
                VPBTranslation.T("gallery.cleanup.confirm", "Move {0} selected cleanup items?\n\nDuplicates: {1}\nOld: {2}\nDamaged: {3}\n\n.var -> DeletedPackages/<Type>\nLocal files -> DeletedScenes/<Type>"),
                selected.Count, dup, old, dmg);

            DisplayConfirm(VPBTranslation.T("gallery.cleanup.confirm_title", "Cleanup"), msg, () =>
            {
                int moved = 0;
                int failed = 0;
                var movedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selected.Count; i++)
                {
                    if (TryMoveCleanupCandidateFile(selected[i], out bool ok, out string failReason) && ok)
                    {
                        moved++;
                        try
                        {
                            string p = NormalizePathSafe(selected[i].SourcePath);
                            if (!string.IsNullOrEmpty(p)) movedPaths.Add(p);
                        }
                        catch { }
                    }
                    else
                    {
                        failed++;
                        if (!string.IsNullOrEmpty(failReason))
                            LogUtil.LogWarning("[VPB] Cleanup move failed for " + selected[i].SourcePath + ": " + failReason);
                    }
                }

                if (movedPaths.Count > 0)
                    cleanupCandidatesAll.RemoveAll(c => c != null && movedPaths.Contains(c.NormalizedPath));
                ApplyCleanupFilterToList(true);

                if (failed == 0)
                    ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.cleanup.done_ok", "Cleanup moved {0} item(s)."), moved), 2f);
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
