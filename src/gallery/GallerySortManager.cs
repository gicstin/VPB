using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        UnloadedOnly = 15
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

        public GallerySortManager()
        {
            cache = new GallerySortCache();
        }

        public void SortFiles(List<FileEntry> files, SortState state)
        {
            if (files == null || state == null) return;

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
                    files.Sort((a, b) => {
                        DateTime ca = GetSortCreationTime(a);
                        DateTime cb = GetSortCreationTime(b);
                        int res = (state.Direction == SortDirection.Ascending)
                            ? ca.CompareTo(cb)
                            : cb.CompareTo(ca);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
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
                    files.Sort((a, b) => {
                        int rA = RatingsManager.Instance.GetRating(a);
                        int rB = RatingsManager.Instance.GetRating(b);
                        int res = (state.Direction == SortDirection.Ascending)
                            ? rA.CompareTo(rB)
                            : rB.CompareTo(rA);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
                case SortType.Deps:
                    files.Sort((a, b) => {
                        int dA = GetDepsCount(a);
                        int dB = GetDepsCount(b);
                        int res = (state.Direction == SortDirection.Ascending) ? dA.CompareTo(dB) : dB.CompareTo(dA);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
                case SortType.Dependents:
                    files.Sort((a, b) => {
                        int dA = GetDependentsCount(a);
                        int dB = GetDependentsCount(b);
                        int res = (state.Direction == SortDirection.Ascending) ? dA.CompareTo(dB) : dB.CompareTo(dA);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
                case SortType.Missing:
                    files.Sort((a, b) => {
                        int mA = GetMissingDepsCount(a);
                        int mB = GetMissingDepsCount(b);
                        int res = (state.Direction == SortDirection.Ascending) ? mA.CompareTo(mB) : mB.CompareTo(mA);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
                case SortType.Hidden:
                    files.Sort((a, b) => {
                        int ha = PackageHidePrefs.IsGalleryHideBadgeVisible(a) ? 1 : 0;
                        int hb = PackageHidePrefs.IsGalleryHideBadgeVisible(b) ? 1 : 0;
                        int res = (state.Direction == SortDirection.Ascending) ? ha.CompareTo(hb) : hb.CompareTo(ha);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
                case SortType.HiddenOnly:
                case SortType.AutoInstallOnly:
                case SortType.LoadedOnly:
                case SortType.UnloadedOnly:
                    if (state.Direction == SortDirection.Ascending)
                        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    else
                        files.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.AutoInstall:
                    files.Sort((a, b) => {
                        int ia = a.IsAutoInstall() ? 1 : 0;
                        int ib = b.IsAutoInstall() ? 1 : 0;
                        int res = (state.Direction == SortDirection.Ascending) ? ia.CompareTo(ib) : ib.CompareTo(ia);
                        if (res == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return res;
                    });
                    break;
            }
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
                    return vfe.Package.CreationTime;
            }
            catch { }
            return DateTime.MinValue;
        }

        /// <summary>Creation time for sorting: .var package time, on-disk file creation, or <see cref="DateTime.MinValue"/> if unknown.</summary>
        private static DateTime GetSortCreationTime(FileEntry file)
        {
            if (file == null) return DateTime.MinValue;
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null)
                    return vfe.Package.CreationTime;
                if (file is PackageListEntry ple && ple.Package != null)
                    return ple.Package.CreationTime;
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
                if (file is VarFileEntry vfe && vfe.Package != null) return vfe.Package.DependentCount;
                if (file is PackageListEntry ple && ple.Package != null) return ple.Package.DependentCount;
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
                                VarPackage pkg = FileManager.GetPackageForDependency(dep, false);
                                if (pkg == null)
                                {
                                    missingCount++;
                                }
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
                        VarPackage pkg = FileManager.GetPackageForDependency(dep, false);
                        if (pkg == null)
                        {
                            missingCount++;
                        }
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

                // Check cache first
                if (_sceneDependencyCache.TryGetValue(filePath, out var cached))
                {
                    return cached;
                }

                // Use streaming/line-by-line extraction to avoid loading entire large files into memory
                var deps = ExtractDependenciesStreaming(file);

                // Cache the result
                if (deps != null && deps.Count > 0)
                    _sceneDependencyCache[filePath] = deps;

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
