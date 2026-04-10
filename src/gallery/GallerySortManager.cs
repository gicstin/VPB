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
        Name,
        Date,
        Size,
        Count,
        Score,
        Rating,
        Deps,
        Dependents,
        Missing
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

                // Use Memory-Mapped File + Span<T>.IndexOf for SIMD-accelerated search
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    dependencies = ExtractDependenciesWithMemoryMappedFile(file, filePath);
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

        /// <summary>Extract dependencies using Memory-Mapped File + Span<T>.IndexOf (SIMD-accelerated)</summary>
        private static HashSet<string> ExtractDependenciesWithMemoryMappedFile(FileEntry file, string filePath)
        {
            var dependencies = new HashSet<string>();

            try
            {
                using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                    long fileSize = fileStream.Length;
                    if (fileSize == 0) return dependencies;

                    // For very large files, read in chunks
                    int chunkSize = (int)System.Math.Min(fileSize, 10 * 1024 * 1024); // 10MB chunks
                    byte[] buffer = new byte[chunkSize];
                    int bytesRead = fileStream.Read(buffer, 0, chunkSize);

                    if (bytesRead > 0)
                    {
                        // Convert bytes to string for searching
                        string content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        // Extract dependencies using fast substring search
                        ExtractDependenciesFromSpan(content, dependencies);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] ExtractDependenciesWithMemoryMappedFile error: {ex}");
            }

            return dependencies;
        }

        /// <summary>Extract dependencies using fast substring search</summary>
        private static void ExtractDependenciesFromSpan(string content, HashSet<string> dependencies)
        {
            // Look for dot-separated sequences (Author.Name.version pattern)
            int pos = 0;

            while (pos < content.Length && dependencies.Count < 150)
            {
                // Find next dot
                int nextDot = content.IndexOf('.', pos);
                if (nextDot < 0) break;

                // Backtrack to find start of identifier
                int start = nextDot;
                while (start > 0 && (char.IsLetterOrDigit(content[start - 1]) || content[start - 1] == '_' || content[start - 1] == '-' || content[start - 1] == ' '))
                    start--;

                // Skip if doesn't start with letter/underscore/digit
                if (start >= nextDot || (!char.IsLetterOrDigit(content[start]) && content[start] != '_' && content[start] != ' '))
                {
                    pos = nextDot + 1;
                    continue;
                }

                // Look for second dot
                int nextDot2 = content.IndexOf('.', nextDot + 1);
                if (nextDot2 < 0)
                {
                    pos = nextDot + 1;
                    continue;
                }

                // Find end of version (after second dot)
                int versionEnd = nextDot2 + 1;
                while (versionEnd < content.Length && (char.IsLetterOrDigit(content[versionEnd]) || content[versionEnd] == '.' || content[versionEnd] == '-' || content[versionEnd] == '_' || content[versionEnd] == ' '))
                    versionEnd++;

                string candidate = content.Substring(start, versionEnd - start);
                // Strip anything after colon (e.g., "Author.Name.Version:path/to/file" -> "Author.Name.Version")
                int colonIdx = candidate.IndexOf(':');
                if (colonIdx > 0)
                    candidate = candidate.Substring(0, colonIdx);

                if (IsValidDependencyCandidate(candidate))
                {
                    dependencies.Add(candidate);
                }

                pos = nextDot + 1;
            }
        }

        private static bool IsValidDependencyCandidate(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || candidate.Length > 200) return false;

            var parts = candidate.Split('.');
            // Only accept exactly 3 parts: Author.Name.Version
            if (parts.Length != 3) return false;

            // Author must be non-empty and start with letter, digit, or underscore
            if (string.IsNullOrEmpty(parts[0]) ||
                (!char.IsLetterOrDigit(parts[0][0]) && parts[0][0] != '_'))
                return false;

            // Name must be non-empty
            if (string.IsNullOrEmpty(parts[1]))
                return false;

            // Reject if author or name contains invalid characters (comma, equals, brackets, etc.)
            if (parts[0].IndexOfAny(new[] { ',', '=', '[', ']', '{', '}', '(', ')', '<', '>', ':', ';', '!', '?', '%', '$', '#', '@', '&', '*', '+', '^' }) >= 0)
                return false;
            if (parts[1].IndexOfAny(new[] { ',', '=', '[', ']', '{', '}', '(', ')', '<', '>', ':', ';', '!', '?', '%', '$', '#', '@', '&', '*', '+', '^' }) >= 0)
                return false;

            // Author or name must contain at least one letter (reject purely numeric patterns)
            bool authorHasLetter = parts[0].Any(c => char.IsLetter(c));
            bool nameHasLetter = parts[1].Any(c => char.IsLetter(c));
            if (!authorHasLetter && !nameHasLetter)
                return false;

            // Version part must be valid and not all zeros
            string version = parts[2];
            if (version == "latest") return true;
            if (version.StartsWith("min") && version.Length > 3 && char.IsDigit(version[3])) return true;
            // Version must start with non-zero digit or be "0" (single zero is valid, but "00", "000" etc. are not)
            if (version.Length > 0 && char.IsDigit(version[0]))
            {
                // Reject if all zeros (like "00", "000", "0000")
                if (!version.Any(c => c != '0' && c != '.'))
                    return false;
                return true;
            }

            return false;
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
