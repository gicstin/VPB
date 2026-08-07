using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
    /// <summary>
    /// Shared rules for on-disk <c>Saves/scene/*.json</c> rows (not <see cref="VarFileEntry"/>).
    /// Used by delete, hide sidecars, and toolbox copy.
    /// </summary>
    public static class LocalSceneGallerySupport
    {
        /// <summary>Prefix for keys in <see cref="FileEntry.AutoInstallLookup"/> / AutoInstall.txt so local scenes never collide with package UIDs.</summary>
        public const string AutoInstallLookupKeyPrefix = "VPB_LS:";
        private const string SceneImportCachePrefix = "Saves/scene/VPB/";
        private const string TempScenesPrefix = "Saves/scene/VPB_TempScenes/";

        private static string s_savesSceneDirFullPath;
        private static string s_savesSceneDirFullPathNormalized;
        /// <summary>Gallery path → AutoInstall lookup key (empty string = known non-scene / miss).</summary>
        private static Dictionary<string, string> s_autoInstallKeyByPath;

        /// <summary>
        /// True for Windows rooted drive paths (<c>C:/...</c>). These contain <c>:/</c> and must not be
        /// treated as VaM <c>pkg:/internal</c> VFS paths on scroll/thumb hot paths.
        /// </summary>
        public static bool IsWindowsDriveAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 3) return false;
            return char.IsLetter(path[0]) && path[1] == ':'
                && (path[2] == '/' || path[2] == '\\');
        }

        public static string GetSavesSceneDirectoryFullPath()
        {
            if (!string.IsNullOrEmpty(s_savesSceneDirFullPath))
                return s_savesSceneDirFullPath;
            try
            {
                s_savesSceneDirFullPath = FileManager.GetFullPath(
                    Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Saves"), "scene"));
                if (!string.IsNullOrEmpty(s_savesSceneDirFullPath))
                {
                    s_savesSceneDirFullPathNormalized = Path.GetFullPath(s_savesSceneDirFullPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                return s_savesSceneDirFullPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Clear path caches (rare; e.g. after VaM cwd / install-root change).</summary>
        public static void InvalidatePathCaches()
        {
            s_savesSceneDirFullPath = null;
            s_savesSceneDirFullPathNormalized = null;
            s_autoInstallKeyByPath = null;
        }

        /// <summary>
        /// True if <paramref name="fileFullPath"/> is a file path inside <paramref name="directoryFullPath"/> (resolved).
        /// </summary>
        public static bool IsStrictFilePathInsideDirectory(string fileFullPath, string directoryFullPath)
        {
            if (string.IsNullOrEmpty(fileFullPath) || string.IsNullOrEmpty(directoryFullPath)) return false;
            try
            {
                string f = Path.GetFullPath(fileFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string d = Path.GetFullPath(directoryFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (f.Length <= d.Length) return false;
                if (!f.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return false;
                char boundary = f[d.Length];
                return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeLocalUserScenePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');
            if (!p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            string lower = p.ToLowerInvariant();
            if (lower.Contains("/subscene/") || lower.Contains("/subscenedata/")) return false;
            if (IsVpbGeneratedLocalScenePath(p)) return false;
            if (lower.Contains("/deletedscenes/")) return false;

            if (lower.IndexOf("/saves/scene/", StringComparison.Ordinal) >= 0) return true;
            if (lower.StartsWith("saves/scene/", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Recursive listing rule for on-disk <c>Saves/scene</c> scenes: any real <c>.json</c> under
        /// <c>Saves/scene</c> (incl. nested subfolders), excluding subscenes and VPB-generated scenes.
        /// Unlike VaM's native browser, a sibling <c>.jpg</c> preview is NOT required \u2014 preview-less
        /// scenes are listed and render with the gallery's thumbnail placeholder.
        /// </summary>
        public static bool IsVaMLocalSceneListingCandidate(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath)) return false;
            if (!jsonPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
            if (IsVpbGeneratedLocalScenePath(jsonPath)) return false;

            string full;
            try
            {
                if (Path.IsPathRooted(jsonPath))
                    full = Path.GetFullPath(jsonPath.Replace('/', Path.DirectorySeparatorChar));
                else
                    full = FileManager.GetFullPath(jsonPath.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(full) || !File.Exists(full)) return false;

            string sceneRoot = GetSavesSceneDirectoryFullPath();
            if (string.IsNullOrEmpty(sceneRoot) || !IsStrictFilePathInsideDirectory(full, sceneRoot))
                return false;

            string norm = full.Replace('\\', '/');
            string lower = norm.ToLowerInvariant();
            if (lower.Contains("/subscene/") || lower.Contains("/subscenedata/")) return false;

            // A sibling .jpg is intentionally NOT required: preview-less scenes (and scenes in subfolders
            // that lack a preview) still list and fall back to the gallery thumbnail placeholder.
            return true;
        }

        public static bool IsVpbGeneratedLocalScenePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('\\', '/').TrimStart('/');
            return p.StartsWith(SceneImportCachePrefix, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(TempScenesPrefix, StringComparison.OrdinalIgnoreCase)
                || p.IndexOf("/" + SceneImportCachePrefix, StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("/" + TempScenesPrefix, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryEnsureVpbGeneratedSceneHideMarker(string jsonPath)
        {
            try
            {
                if (string.IsNullOrEmpty(jsonPath)) return false;
                if (!jsonPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
                if (!IsVpbGeneratedLocalScenePath(jsonPath)) return false;

                string full;
                try
                {
                    full = Path.IsPathRooted(jsonPath)
                        ? Path.GetFullPath(jsonPath)
                        : FileManager.GetFullPath(jsonPath.Replace('/', Path.DirectorySeparatorChar));
                }
                catch
                {
                    full = jsonPath;
                }

                string hidePath = full + ".hide";
                if (File.Exists(hidePath)) return true;

                string dir = Path.GetDirectoryName(hidePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(hidePath, string.Empty);
                return File.Exists(hidePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves a gallery <see cref="FileEntry"/> to a real <c>Saves/scene</c> JSON file on disk.
        /// </summary>
        /// <param name="logTraversalWarning">If true, logs when the path resolves outside <c>Saves/scene</c>.</param>
        public static bool TryResolveSavesSceneJson(FileEntry f, out string absoluteJsonPath, out string galleryRelativePath, bool logTraversalWarning)
        {
            absoluteJsonPath = null;
            galleryRelativePath = null;
            if (f == null) return false;
            if (f is VarFileEntry) return false;

            string p = f.Path;
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');

            // IMPORTANT: On Windows, absolute disk paths like "C:/.../Saves/scene/foo.json" contain ":/" and can be
            // misclassified as a package path by VaM helpers that treat any ":/" as "pkg:/internalPath".
            // Treat rooted drive-letter paths as local disk paths, not package refs.
            if (!IsWindowsDriveAbsolutePath(p))
            {
                try
                {
                    if (FileManager.IsPackagePath(p)) return false;
                }
                catch { }
            }

            if (!LooksLikeLocalUserScenePath(p)) return false;

            string sceneRoot = GetSavesSceneDirectoryFullPath();
            if (string.IsNullOrEmpty(sceneRoot)) return false;

            string full;
            try
            {
                if (Path.IsPathRooted(p))
                {
                    full = Path.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar));
                }
                else
                {
                    full = FileManager.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar));
                }
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(full) || !File.Exists(full)) return false;

            try
            {
                FileAttributes fa = File.GetAttributes(full);
                if ((fa & FileAttributes.Directory) != 0) return false;
            }
            catch { }

            if (!IsStrictFilePathInsideDirectory(full, sceneRoot))
            {
                if (logTraversalWarning)
                    LogUtil.LogWarning("[VPB] Local scene: rejected path outside Saves/scene (possible traversal or symlink escape): " + full);
                return false;
            }

            absoluteJsonPath = full;
            // Normalize to a VaM-relative path ("Saves/scene/...") so FileManager.ReadAllText can open it.
            try
            {
                string rootFull = Path.GetFullPath(sceneRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fileFull = Path.GetFullPath(full);
                if (fileFull.Length > rootFull.Length + 1 &&
                    fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    string relPart = fileFull.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    relPart = relPart.Replace('\\', '/');
                    galleryRelativePath = "Saves/scene/" + relPart;
                }
                else
                {
                    // Fallback: keep the original path as provided by the gallery.
                    galleryRelativePath = p.TrimStart('/');
                }
            }
            catch
            {
                galleryRelativePath = p.TrimStart('/');
            }
            return true;
        }

        /// <summary>
        /// Gallery-relative <c>Saves/scene/...</c> from a listed row path — path math only (no <c>File.Exists</c>).
        /// Scroll/badge hot path: gallery already listed the JSON.
        /// </summary>
        public static bool TryBuildGalleryRelativeScenePathNoDisk(string rawPath, out string galleryRelativePath)
        {
            galleryRelativePath = null;
            if (string.IsNullOrEmpty(rawPath)) return false;
            string p = rawPath.Replace('\\', '/');
            if (!LooksLikeLocalUserScenePath(p)) return false;

            if (p.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase))
            {
                galleryRelativePath = "Saves/scene/" + p.Substring("Saves/scene/".Length).TrimStart('/');
                return true;
            }

            int idx = p.IndexOf("/Saves/scene/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                galleryRelativePath = "Saves/scene/" + p.Substring(idx + "/Saves/scene/".Length).TrimStart('/');
                return true;
            }

            // Absolute under cached Saves/scene root → strip prefix.
            try
            {
                string sceneRoot = GetSavesSceneDirectoryFullPath();
                if (string.IsNullOrEmpty(sceneRoot)) return false;
                string rootNorm = s_savesSceneDirFullPathNormalized;
                if (string.IsNullOrEmpty(rootNorm))
                {
                    rootNorm = Path.GetFullPath(sceneRoot)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    s_savesSceneDirFullPathNormalized = rootNorm;
                }

                string full = Path.IsPathRooted(p)
                    ? Path.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar))
                    : FileManager.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(full)) return false;
                string fileFull = Path.GetFullPath(full);
                if (fileFull.Length <= rootNorm.Length + 1) return false;
                if (!fileFull.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase)) return false;
                char boundary = fileFull[rootNorm.Length];
                if (boundary != Path.DirectorySeparatorChar && boundary != Path.AltDirectorySeparatorChar)
                    return false;
                string relPart = fileFull.Substring(rootNorm.Length + 1).Replace('\\', '/');
                if (string.IsNullOrEmpty(relPart)) return false;
                galleryRelativePath = "Saves/scene/" + relPart;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds the AutoInstall.txt key for this local scene row.
        /// Uses path-only resolve + cache — no per-bind <c>File.Exists</c> (scroll-hot badge path).
        /// </summary>
        public static bool TryGetLocalSceneAutoInstallLookupKey(FileEntry f, out string key)
        {
            key = null;
            if (f == null || f is VarFileEntry) return false;
            string p = f.Path;
            if (string.IsNullOrEmpty(p)) return false;

            if (s_autoInstallKeyByPath != null && s_autoInstallKeyByPath.TryGetValue(p, out string cached))
            {
                if (string.IsNullOrEmpty(cached)) return false;
                key = cached;
                return true;
            }

            string rel;
            if (!TryBuildGalleryRelativeScenePathNoDisk(p, out rel) || string.IsNullOrEmpty(rel))
            {
                // Fall back to full resolve once (security / odd paths), then cache.
                if (!TryResolveSavesSceneJson(f, out _, out rel, false) || string.IsNullOrEmpty(rel))
                {
                    CacheAutoInstallKey(p, string.Empty);
                    return false;
                }
            }

            key = AutoInstallLookupKeyPrefix + rel.Replace('\\', '/');
            CacheAutoInstallKey(p, key);
            return true;
        }

        private static void CacheAutoInstallKey(string path, string keyOrEmpty)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (s_autoInstallKeyByPath == null)
                s_autoInstallKeyByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (s_autoInstallKeyByPath.Count > 8000)
                s_autoInstallKeyByPath.Clear();
            s_autoInstallKeyByPath[path] = keyOrEmpty ?? string.Empty;
        }

        public static bool IsLocalSceneAutoInstallMarked(FileEntry f)
        {
            if (!TryGetLocalSceneAutoInstallLookupKey(f, out string key)) return false;
            try { return FileEntry.AutoInstallLookup != null && FileEntry.AutoInstallLookup.Contains(key); }
            catch { return false; }
        }

        /// <summary>
        /// For a disk scene JSON, runs <see cref="VarPackage.InstallSelf"/> on each extracted package UID (scene file is not moved).
        /// </summary>
        public static bool InstallDependenciesForSceneJsonFile(string absoluteJsonPath)
        {
            bool dirty = false;
            if (string.IsNullOrEmpty(absoluteJsonPath) || !File.Exists(absoluteJsonPath)) return false;

            try
            {
                var deps = DependencyExtractor.ExtractDependenciesFromFile(absoluteJsonPath, maxDependencies: 250, maxMilliseconds: 2500);
                if (deps == null || deps.Count == 0) return false;

                foreach (string uid in deps)
                {
                    if (string.IsNullOrEmpty(uid)) continue;
                    try
                    {
                        VarPackage pkg = FileManager.GetPackage(uid);
                        if (pkg != null && pkg.InstallSelf())
                            dirty = true;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] InstallDependenciesForSceneJsonFile: " + ex.Message);
            }

            return dirty;
        }

        /// <summary>
        /// Startup pass: every <see cref="AutoInstallLookupKeyPrefix"/> entry in AutoInstall.txt gets dependency packages installed from AllPackages.
        /// </summary>
        public static bool InstallDependenciesForAllAutoMarkedLocalScenes()
        {
            bool anyDirty = false;
            try
            {
                var lookup = FileEntry.AutoInstallLookup;
                if (lookup == null) return false;

                foreach (string item in lookup)
                {
                    if (string.IsNullOrEmpty(item) || item.Length <= AutoInstallLookupKeyPrefix.Length) continue;
                    if (!item.StartsWith(AutoInstallLookupKeyPrefix, StringComparison.Ordinal)) continue;

                    string rel = item.Substring(AutoInstallLookupKeyPrefix.Length);
                    if (string.IsNullOrEmpty(rel)) continue;

                    string full;
                    try
                    {
                        full = FileManager.GetFullPath(rel.Replace('/', Path.DirectorySeparatorChar));
                    }
                    catch { continue; }

                    if (!File.Exists(full)) continue;

                    if (InstallDependenciesForSceneJsonFile(full))
                        anyDirty = true;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] InstallDependenciesForAllAutoMarkedLocalScenes: " + ex.Message);
            }

            return anyDirty;
        }
    }
}
