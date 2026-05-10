using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
    public partial class GalleryPanel
    {
        private struct LocalPresetDeleteItem
        {
            public string AbsolutePath;
            public string RelativePath;
        }

        internal static class LocalPresetDeleteSupport
        {
            internal const string DeletedLocalPresetsFolderName = "DeletedPresets";

            // Scope: "LOCAL CUSTOM PRESETS" categories (Appearance/Clothing/Hair/Pose/Skin/Morphs/General/Plugins/Subscenes).
            // Safety: only allow deletes for known VaM local preset roots (not arbitrary disk files).
            private static readonly string[] AllowedRoots = new string[]
            {
                // Broad "Custom/" covers VaM local preset roots (and any other user-created assets under Custom).
                // Still excludes .var internal paths (":/") and non-existent files at delete-time.
                "Custom/",
                "Saves/SubsceneData/",
            };

            internal static bool IsAllowedLocalPresetRelativePath(string rel)
            {
                if (string.IsNullOrEmpty(rel)) return false;
                rel = rel.Replace('\\', '/');
                if (rel.IndexOf(":/", StringComparison.Ordinal) >= 0) return false; // inside .var
                for (int i = 0; i < AllowedRoots.Length; i++)
                {
                    if (rel.StartsWith(AllowedRoots[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            internal static void EnsureDeletedLocalPresetsDirectory(string deletedDir)
            {
                try
                {
                    if (!Directory.Exists(deletedDir)) Directory.CreateDirectory(deletedDir);
                }
                catch { }
            }

            internal static string PickUniqueDestPath(string destDir, string fileName)
            {
                string dstPath = Path.Combine(destDir, fileName);
                if (!File.Exists(dstPath)) return dstPath;

                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

                for (int attempt = 0; attempt < 512; attempt++)
                {
                    string suffix = attempt == 0 ? stamp : (stamp + "_" + attempt);
                    dstPath = Path.Combine(destDir, baseName + "__" + suffix + ext);
                    if (!File.Exists(dstPath)) return dstPath;
                }

                return Path.Combine(destDir, baseName + "__" + Guid.NewGuid().ToString("N") + ext);
            }
        }

        private static List<LocalPresetDeleteItem> CollectLocalPresetDeleteItemsFromSelection(IList<FileEntry> files)
        {
            var list = new List<LocalPresetDeleteItem>();
            if (files == null || files.Count == 0) return list;

            var seenAbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null) continue;
                string rel = null;
                try { rel = f.Path ?? f.Uid; } catch { rel = null; }
                if (string.IsNullOrEmpty(rel)) continue;
                rel = rel.Replace('\\', '/');
                if (!LocalPresetDeleteSupport.IsAllowedLocalPresetRelativePath(rel)) continue;

                string abs;
                try { abs = FileManager.GetFullPath(rel); }
                catch { continue; }

                try
                {
                    if (!File.Exists(abs)) continue;
                }
                catch { continue; }

                if (!seenAbs.Add(abs)) continue;
                list.Add(new LocalPresetDeleteItem { AbsolutePath = abs, RelativePath = rel });
            }

            return list;
        }

        private int GetTboxDeleteEligibleLocalPresetCount()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return 0;
            return CollectLocalPresetDeleteItemsFromSelection(selectedFiles).Count;
        }

        private static void TryMovePresetSidecar(string srcFull, string dstFullBase, string extWithDot, string logContext)
        {
            if (string.IsNullOrEmpty(srcFull) || string.IsNullOrEmpty(dstFullBase) || string.IsNullOrEmpty(extWithDot)) return;
            string src = srcFull + extWithDot;
            try
            {
                if (!File.Exists(src)) return;
            }
            catch { return; }

            try
            {
                string dst = dstFullBase + extWithDot;
                if (!TryMoveFileRobust(src, dst, logContext)) { }
            }
            catch { }
        }

        private void PerformLocalPresetsDeleteMove(List<LocalPresetDeleteItem> items, string deletedDir, out int moved, out int failed)
        {
            moved = 0;
            failed = 0;
            if (items == null || items.Count == 0) return;

            string baseDir = Directory.GetCurrentDirectory();
            string expectedDeleted;
            try { expectedDeleted = FileManager.GetFullPath(Path.Combine(baseDir, LocalPresetDeleteSupport.DeletedLocalPresetsFolderName)); }
            catch { expectedDeleted = null; }

            string deletedNorm;
            try { deletedNorm = FileManager.GetFullPath(deletedDir); }
            catch { failed = items.Count; return; }

            if (!string.IsNullOrEmpty(expectedDeleted) && !string.Equals(deletedNorm, expectedDeleted, StringComparison.OrdinalIgnoreCase))
            {
                LogUtil.LogError("[VPB] Local preset delete: destination folder mismatch. Aborting.");
                failed = items.Count;
                return;
            }

            LocalPresetDeleteSupport.EnsureDeletedLocalPresetsDirectory(deletedNorm);

            var movedRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < items.Count; i++)
            {
                string src = items[i].AbsolutePath;
                if (string.IsNullOrEmpty(src)) { failed++; continue; }
                try
                {
                    if (!File.Exists(src)) { failed++; continue; }
                }
                catch { failed++; continue; }

                try
                {
                    string name = Path.GetFileName(src);
                    if (string.IsNullOrEmpty(name)) { failed++; continue; }
                    string dst = LocalPresetDeleteSupport.PickUniqueDestPath(deletedNorm, name);
                    if (!TryMoveFileRobust(src, dst, "Local preset"))
                    {
                        failed++;
                        continue;
                    }

                    moved++;
                    if (!string.IsNullOrEmpty(items[i].RelativePath))
                        movedRelPaths.Add(items[i].RelativePath.Replace('\\', '/'));

                    // Keep VaM markers with file when moving.
                    TryMovePresetSidecar(src, dst, ".fav", "Local preset sidecar fav");
                    TryMovePresetSidecar(src, dst, ".hide", "Local preset sidecar hide");
                }
                catch (Exception ex)
                {
                    failed++;
                    LogUtil.LogError("[VPB] Local preset delete(move) failed for " + src + ": " + ex.Message);
                }
            }

            try { PurgeGalleryEntriesForMovedLocalPresets(movedRelPaths); } catch { }

            // Do NOT trigger FileManager.Refresh here: it kicks off a full package scan (~seconds).
            // Preset delete already updates current grid by purging moved rows; next manual refresh or navigation can rescan disk.
        }

        private void PurgeGalleryEntriesForMovedLocalPresets(HashSet<string> movedRelativePaths)
        {
            if (movedRelativePaths == null || movedRelativePaths.Count == 0) return;

            try
            {
                if (selectedFiles != null)
                {
                    selectedFiles.RemoveAll(f =>
                    {
                        if (f == null) return false;
                        string p = null;
                        try { p = f.Path ?? f.Uid; } catch { p = null; }
                        if (string.IsNullOrEmpty(p)) return false;
                        p = p.Replace('\\', '/');
                        return movedRelativePaths.Contains(p);
                    });
                }
                try { RefreshSelectionVisuals(); } catch { }
            }
            catch { }

            try
            {
                if (currentFilteredFiles != null)
                {
                    int before = currentFilteredFiles.Count;
                    currentFilteredFiles.RemoveAll(f =>
                    {
                        if (f == null) return false;
                        string p = null;
                        try { p = f.Path ?? f.Uid; } catch { p = null; }
                        if (string.IsNullOrEmpty(p)) return false;
                        p = p.Replace('\\', '/');
                        return movedRelativePaths.Contains(p);
                    });

                    if (recyclingGrid != null && currentFilteredFiles.Count != before)
                    {
                        recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                        recyclingGrid.Refresh();
                    }
                    try { UpdatePaginationText(); } catch { }
                }
            }
            catch { }

            try
            {
                if (lastFilteredFiles != null)
                {
                    lastFilteredFiles.RemoveAll(f =>
                    {
                        if (f == null) return false;
                        string p = null;
                        try { p = f.Path ?? f.Uid; } catch { p = null; }
                        if (string.IsNullOrEmpty(p)) return false;
                        p = p.Replace('\\', '/');
                        return movedRelativePaths.Contains(p);
                    });
                }
            }
            catch { }

            try
            {
                if (selectedFilePaths != null)
                {
                    selectedFilePaths.RemoveWhere(p =>
                        !string.IsNullOrEmpty(p) && movedRelativePaths.Contains(p.Replace('\\', '/')));
                }
            }
            catch { }
        }
    }
}

