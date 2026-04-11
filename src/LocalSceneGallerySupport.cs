using System;
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

        public static string GetSavesSceneDirectoryFullPath()
        {
            try
            {
                return FileManager.GetFullPath(Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Saves"), "scene"));
            }
            catch
            {
                return null;
            }
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
            if (lower.Contains("/saves/scene/vpb_tmpscenes/") || lower.Contains("vpb_tmpscenes")) return false;
            if (lower.Contains("/deletedscenes/")) return false;

            if (lower.IndexOf("/saves/scene/", StringComparison.Ordinal) >= 0) return true;
            if (lower.StartsWith("saves/scene/", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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

            try
            {
                if (FileManager.IsPackagePath(p)) return false;
            }
            catch { }

            if (!LooksLikeLocalUserScenePath(p)) return false;

            string sceneRoot = GetSavesSceneDirectoryFullPath();
            if (string.IsNullOrEmpty(sceneRoot)) return false;

            string full;
            try
            {
                full = FileManager.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar));
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
            galleryRelativePath = p;
            return true;
        }

        /// <summary>Builds the AutoInstall.txt key for this local scene row, if it resolves on disk.</summary>
        public static bool TryGetLocalSceneAutoInstallLookupKey(FileEntry f, out string key)
        {
            key = null;
            if (!TryResolveSavesSceneJson(f, out _, out string rel, false)) return false;
            if (string.IsNullOrEmpty(rel)) return false;
            key = AutoInstallLookupKeyPrefix + rel.Replace('\\', '/');
            return true;
        }

        public static bool IsLocalSceneAutoInstallMarked(FileEntry f)
        {
            if (!TryGetLocalSceneAutoInstallLookupKey(f, out string key)) return false;
            try { return FileEntry.AutoInstallLookup != null && FileEntry.AutoInstallLookup.Contains(key); }
            catch { return false; }
        }
    }
}
