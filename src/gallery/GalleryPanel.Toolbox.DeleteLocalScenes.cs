using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private const string DeletedLocalScenesFolderName = "DeletedScenes";

        private struct LocalSceneDeleteItem
        {
            public string AbsoluteJson;
            public string GalleryRelativePath;
        }

        private static string EnsureDirectoryPrefixForStartsWith(string directoryFullPathTrimmed)
        {
            if (string.IsNullOrEmpty(directoryFullPathTrimmed)) return directoryFullPathTrimmed;
            int n = directoryFullPathTrimmed.Length;
            char last = directoryFullPathTrimmed[n - 1];
            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                return directoryFullPathTrimmed;
            return directoryFullPathTrimmed + Path.DirectorySeparatorChar;
        }

        /// <summary>Whether <paramref name="pathFull"/> is inside or equal to <paramref name="ancestorFull"/> (directory tree). Safe for drive root and symlink-resolved paths.</summary>
        private static bool IsPathInsideOrEqualDirectory(string pathFull, string ancestorFull)
        {
            if (string.IsNullOrEmpty(pathFull) || string.IsNullOrEmpty(ancestorFull)) return false;
            try
            {
                string p = Path.GetFullPath(pathFull).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string a = Path.GetFullPath(ancestorFull).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(p, a, StringComparison.OrdinalIgnoreCase)) return true;
                string prefix = EnsureDirectoryPrefixForStartsWith(a);
                return p.Length > a.Length && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetGameRootFullPath()
        {
            try
            {
                return FileManager.GetFullPath(Directory.GetCurrentDirectory());
            }
            catch
            {
                return null;
            }
        }

        private static string GetExpectedDeletedScenesDirectoryFullPath()
        {
            try
            {
                return FileManager.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), DeletedLocalScenesFolderName));
            }
            catch
            {
                return null;
            }
        }

        private static List<LocalSceneDeleteItem> CollectLocalSceneDeleteItemsFromSelection(IList<FileEntry> files, bool logTraversalWarning)
        {
            var list = new List<LocalSceneDeleteItem>();
            if (files == null || files.Count == 0) return list;

            var seenAbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out string abs, out string rel, logTraversalWarning)) continue;
                if (!seenAbs.Add(abs)) continue;
                list.Add(new LocalSceneDeleteItem { AbsoluteJson = abs, GalleryRelativePath = rel });
            }
            return list;
        }

        private int GetTboxDeleteEligibleLocalSceneCount()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return 0;
            return CollectLocalSceneDeleteItemsFromSelection(selectedFiles, false).Count;
        }

        private static bool EnsureDeletedLocalScenesDirectory(string deletedDir)
        {
            try
            {
                if (Directory.Exists(deletedDir)) return true;
                Directory.CreateDirectory(deletedDir);
                return Directory.Exists(deletedDir);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] DeletedScenes: could not create directory '" + deletedDir + "': " + ex.Message);
                return false;
            }
        }

        private static string PickUniqueDestPath(string destDir, string fileName)
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

        /// <summary>Move or copy+delete (cross-volume / reparse-point safe). Rolls back copy if source delete fails.</summary>
        private static bool TryMoveFileRobust(string src, string dst, string logContext)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) return false;
            try
            {
                if (!File.Exists(src)) return false;
            }
            catch
            {
                return false;
            }

            try
            {
                string dd = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dd) && !Directory.Exists(dd))
                    Directory.CreateDirectory(dd);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] " + logContext + " mkdir failed: " + ex.Message);
                return false;
            }

            try
            {
                File.Move(src, dst);
                return true;
            }
            catch (IOException ex)
            {
                return TryMoveFileByCopyDelete(src, dst, logContext, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return TryMoveFileByCopyDelete(src, dst, logContext, ex);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] " + logContext + " move failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryMoveFileByCopyDelete(string src, string dst, string logContext, Exception triggerEx)
        {
            try
            {
                File.Copy(src, dst, false);
                try
                {
                    File.Delete(src);
                    return true;
                }
                catch (Exception delEx)
                {
                    LogUtil.LogError("[VPB] " + logContext + " copied to dest but could not remove source; rolling back dest. Source delete: " + delEx.Message);
                    try
                    {
                        if (File.Exists(dst)) File.Delete(dst);
                    }
                    catch (Exception rbEx)
                    {
                        LogUtil.LogError("[VPB] " + logContext + " rollback delete failed (duplicate may exist): " + rbEx.Message);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] " + logContext + " copy fallback failed" + (triggerEx != null ? " (after: " + triggerEx.Message + ")" : "") + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Moves scene JSON plus same-base .jpg/.png and optional .json.fav / .json.hide into <paramref name="deletedDir"/>.</summary>
        private void PerformLocalScenesDeleteMove(List<LocalSceneDeleteItem> items, string deletedDir, out int moved, out int failed, List<FileMoveUndoPair> undoOut = null)
        {
            moved = 0;
            failed = 0;
            if (items == null || items.Count == 0) return;

            string sceneRoot = LocalSceneGallerySupport.GetSavesSceneDirectoryFullPath();
            string gameRoot = GetGameRootFullPath();
            string expectedDeleted = GetExpectedDeletedScenesDirectoryFullPath();
            if (string.IsNullOrEmpty(sceneRoot) || string.IsNullOrEmpty(gameRoot) || string.IsNullOrEmpty(expectedDeleted))
            {
                LogUtil.LogError("[VPB] Local scene delete: could not resolve game paths.");
                failed = items.Count;
                return;
            }

            string deletedNorm;
            try
            {
                deletedNorm = FileManager.GetFullPath(deletedDir);
            }
            catch
            {
                failed = items.Count;
                return;
            }

            if (!string.Equals(deletedNorm, expectedDeleted, StringComparison.OrdinalIgnoreCase))
            {
                LogUtil.LogError("[VPB] Local scene delete: destination folder mismatch (expected '" + expectedDeleted + "', got '" + deletedNorm + "'). Aborting.");
                failed = items.Count;
                return;
            }

            if (!IsPathInsideOrEqualDirectory(deletedNorm, gameRoot))
            {
                LogUtil.LogError("[VPB] Local scene delete: DeletedScenes path is not under game root. Aborting.");
                failed = items.Count;
                return;
            }

            try
            {
                if (File.Exists(deletedNorm) && !Directory.Exists(deletedNorm))
                {
                    LogUtil.LogError("[VPB] Local scene delete: '" + deletedNorm + "' exists but is not a directory. Aborting.");
                    failed = items.Count;
                    return;
                }
            }
            catch { }

            if (!EnsureDeletedLocalScenesDirectory(deletedNorm))
            {
                failed = items.Count;
                return;
            }

            var movedRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < items.Count; i++)
            {
                string srcJson = items[i].AbsoluteJson;
                string rel = items[i].GalleryRelativePath;

                if (string.IsNullOrEmpty(srcJson))
                {
                    failed++;
                    continue;
                }

                try
                {
                    if (!File.Exists(srcJson))
                    {
                        failed++;
                        continue;
                    }
                }
                catch
                {
                    failed++;
                    continue;
                }

                if (!LocalSceneGallerySupport.IsStrictFilePathInsideDirectory(srcJson, sceneRoot))
                {
                    LogUtil.LogWarning("[VPB] Local scene delete: skipped (no longer under Saves/scene): " + srcJson);
                    failed++;
                    continue;
                }

                if (IsPathInsideOrEqualDirectory(srcJson, deletedNorm))
                {
                    LogUtil.LogWarning("[VPB] Local scene delete: skipped (source inside DeletedScenes): " + srcJson);
                    failed++;
                    continue;
                }

                try
                {
                    string jsonName = Path.GetFileName(srcJson);
                    if (string.IsNullOrEmpty(jsonName))
                    {
                        failed++;
                        continue;
                    }

                    string dstJson = PickUniqueDestPath(deletedNorm, jsonName);
                    if (!TryMoveFileRobust(srcJson, dstJson, "Local scene JSON"))
                    {
                        failed++;
                        continue;
                    }

                    moved++;
                    if (!string.IsNullOrEmpty(rel)) movedRelPaths.Add(rel);
                    if (undoOut != null)
                    {
                        FileMoveUndoPair pair;
                        pair.FromDeletedPath = dstJson;
                        pair.ToOriginalPath = srcJson;
                        undoOut.Add(pair);
                    }

                    string srcDir = Path.GetDirectoryName(srcJson);
                    string dstDir = Path.GetDirectoryName(dstJson);
                    if (string.IsNullOrEmpty(srcDir) || string.IsNullOrEmpty(dstDir))
                        continue;

                    string srcBaseFile = Path.GetFileNameWithoutExtension(srcJson);
                    string stemDst = Path.Combine(dstDir, Path.GetFileNameWithoutExtension(dstJson));

                    TryMoveSidecarFull(
                        sceneRoot,
                        SafeCombinePaths(srcDir, srcBaseFile + ".jpg"),
                        stemDst + ".jpg",
                        deletedNorm,
                        "Local scene sidecar jpg");
                    TryMoveSidecarFull(
                        sceneRoot,
                        SafeCombinePaths(srcDir, srcBaseFile + ".png"),
                        stemDst + ".png",
                        deletedNorm,
                        "Local scene sidecar png");
                    TryMoveSidecarFull(
                        sceneRoot,
                        SafeCombinePaths(srcDir, jsonName + ".fav"),
                        dstJson + ".fav",
                        deletedNorm,
                        "Local scene sidecar fav");
                    TryMoveSidecarFull(
                        sceneRoot,
                        SafeCombinePaths(srcDir, jsonName + ".hide"),
                        dstJson + ".hide",
                        deletedNorm,
                        "Local scene sidecar hide");
                }
                catch (Exception ex)
                {
                    failed++;
                    LogUtil.LogError("[VPB] Local scene delete(move) failed for " + srcJson + ": " + ex.Message);
                }
            }

            try { PurgeGalleryEntriesForMovedLocalScenes(movedRelPaths); } catch { }

            try
            {
                try { FileManagerBridge.Refresh("tbox_delete_local_scenes", RefreshScope.Both); } catch { }
            }
            catch { }
        }

        private static string SafeCombinePaths(string dir, string file)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return null;
            try
            {
                return Path.Combine(dir, file);
            }
            catch
            {
                return null;
            }
        }

        private static void TryMoveSidecarFull(string sceneRootFull, string srcPath, string preferredDstPath, string deletedDirFull, string logContext)
        {
            if (string.IsNullOrEmpty(srcPath) || string.IsNullOrEmpty(preferredDstPath)) return;

            string srcFull;
            try
            {
                srcFull = FileManager.GetFullPath(srcPath);
            }
            catch
            {
                return;
            }

            try
            {
                if (!File.Exists(srcFull)) return;
                FileAttributes fa = File.GetAttributes(srcFull);
                if ((fa & FileAttributes.Directory) != 0) return;
            }
            catch
            {
                return;
            }

            if (!string.IsNullOrEmpty(sceneRootFull) && !LocalSceneGallerySupport.IsStrictFilePathInsideDirectory(srcFull, sceneRootFull))
            {
                LogUtil.LogWarning("[VPB] " + logContext + " skipped (not under Saves/scene): " + srcFull);
                return;
            }

            if (!string.IsNullOrEmpty(deletedDirFull) && IsPathInsideOrEqualDirectory(srcFull, deletedDirFull))
                return;

            try
            {
                string dir = Path.GetDirectoryName(preferredDstPath);
                string name = Path.GetFileName(preferredDstPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;
                string dst = PickUniqueDestPath(dir, name);
                TryMoveFileRobust(srcFull, dst, logContext);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] " + logContext + " failed: " + srcFull + " -> " + ex.Message);
            }
        }

        private void PurgeGalleryEntriesForMovedLocalScenes(HashSet<string> movedRelativePaths)
        {
            if (movedRelativePaths == null || movedRelativePaths.Count == 0) return;

            try
            {
                if (selectedFiles != null)
                {
                    selectedFiles.RemoveAll(f =>
                    {
                        if (f == null || string.IsNullOrEmpty(f.Path)) return false;
                        string rp = f.Path.Replace('\\', '/');
                        return movedRelativePaths.Contains(rp);
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
                        if (f == null || string.IsNullOrEmpty(f.Path)) return false;
                        string rp = f.Path.Replace('\\', '/');
                        return movedRelativePaths.Contains(rp);
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
        }
    }
}
