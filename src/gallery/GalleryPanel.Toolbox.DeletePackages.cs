using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using SimpleJSON;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private const string DeletedPackagesFolderName = "DeletedPackages";

        private static void LogSuppressed(string context, Exception ex, bool verbose = false)
        {
            if (string.IsNullOrEmpty(context)) context = "unknown";
            string msg = "[VPB] Suppressed exception (" + context + "): " + (ex != null ? ex.Message : "null");
            if (verbose) LogUtil.LogVerboseUi(msg);
            else LogUtil.LogWarning(msg);
        }

        /// <summary>Unique package UIDs referenced by the current selection (same basis as copy / delete).</summary>
        private static HashSet<string> CollectUniquePackageUidsFromSelection(IList<FileEntry> files)
        {
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (files == null) return uids;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null) continue;
                string uid = TryGetPackageUidForEntry(f);
                if (!string.IsNullOrEmpty(uid)) uids.Add(uid);
            }
            return uids;
        }

        /// <summary>Classify selected UIDs for toolbox delete; <paramref name="toDelete"/> matches what the confirm dialog will move.</summary>
        private static void ClassifyUidsForTboxDelete(
            HashSet<string> uids,
            string currentScenePkg,
            HashSet<string> runningSceneDeps,
            List<string> blocked,
            List<string> warned,
            List<string> toDelete,
            bool warnDependents = true)
        {
            if (uids == null || uids.Count == 0) return;
            try { DependencyGraph.EnsureForUids(uids); } catch { }

            foreach (var uid in uids)
            {
                if (string.IsNullOrEmpty(uid)) continue;

                // Critical: locked packages should never be moved
                try
                {
                    if (LockedPackagesManager.Instance != null && LockedPackagesManager.Instance.IsLocked(uid))
                    {
                        blocked.Add($"{uid}.var (locked)");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // Conservative: if lock state can't be checked, treat as blocked.
                    blocked.Add($"{uid}.var (lock check failed)");
                    LogSuppressed("DeletePackages.LockedPackagesManager.IsLocked", ex);
                    continue;
                }

                // Currently loaded scene package: allow delete (file move + undo) but warn.
                // Blocking it hid Delete for the open scene; confirm dialog is the friction (High risk).
                bool isLoadedScenePkg = !string.IsNullOrEmpty(currentScenePkg)
                    && string.Equals(uid, currentScenePkg, StringComparison.OrdinalIgnoreCase);
                if (isLoadedScenePkg)
                    warned.Add(uid + ".var (currently loaded scene)");

                // Running scene dependency (skip if already called out as the loaded scene package).
                if (!isLoadedScenePkg && runningSceneDeps != null && runningSceneDeps.Contains(uid))
                {
                    warned.Add($"{uid}.var (referenced by running scene)");
                }

                // Dependents warning (skip for exclusive-dep pass — those dependents are the packages we are already deleting).
                if (warnDependents)
                {
                    int depCount = 0;
                    try { depCount = GetDependentCount(uid); } catch { depCount = 0; }
                    if (depCount > 0)
                    {
                        warned.Add($"{uid}.var ({depCount} dependents)");
                    }
                }

                // Critical: must be resolvable to a file on disk
                string srcPath = ResolveVarPathForUid(uid);
                if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
                {
                    blocked.Add($"{uid}.var (file not found)");
                    continue;
                }

                // Critical: already deleted
                try
                {
                    string norm = srcPath.Replace('\\', '/');
                    if (norm.IndexOf("/" + DeletedPackagesFolderName + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        blocked.Add($"{uid}.var (already in {DeletedPackagesFolderName})");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    LogSuppressed("DeletePackages.PathAlreadyDeletedCheck", ex);
                }

                // Critical: avoid moving enabled auto-install packages (too easy to break user prefs)
                try
                {
                    // Only warn; user may still proceed
                    if (FileEntry.AutoInstallLookup != null && FileEntry.AutoInstallLookup.Contains(uid))
                        warned.Add($"{uid}.var (auto-install enabled)");
                }
                catch (Exception ex)
                {
                    LogSuppressed("DeletePackages.AutoInstallLookup", ex);
                }

                toDelete.Add(uid);
            }
        }

        private static string NormalizeScenePathForCompare(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace('\\', '/').Trim();
        }

        private static bool IsCurrentlyLoadedLocalScene(string absJson, string galleryRel)
        {
            string loaded = null;
            try { loaded = VamHookPlugin.CurrentSceneSaveName; }
            catch { loaded = null; }
            if (string.IsNullOrEmpty(loaded)) return false;
            if (loaded.IndexOf(":/", StringComparison.Ordinal) >= 0) return false;

            string loadedNorm = NormalizeScenePathForCompare(loaded);
            if (loadedNorm.Length == 0) return false;

            if (!string.IsNullOrEmpty(galleryRel)
                && string.Equals(NormalizeScenePathForCompare(galleryRel), loadedNorm, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrEmpty(absJson)) return false;
            string absNorm = NormalizeScenePathForCompare(absJson);
            if (string.Equals(absNorm, loadedNorm, StringComparison.OrdinalIgnoreCase)) return true;
            if (absNorm.Length > loadedNorm.Length
                && absNorm.EndsWith("/" + loadedNorm, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                string loadedFull = FileManager.GetFullPath(loaded);
                if (!string.IsNullOrEmpty(loadedFull)
                    && string.Equals(
                        NormalizeScenePathForCompare(loadedFull),
                        absNorm,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool SelectionDeletesCurrentlyLoadedScene(
            List<string> packageUidsToDelete,
            string currentScenePkg,
            List<LocalSceneDeleteItem> localScenes)
        {
            if (!string.IsNullOrEmpty(currentScenePkg) && packageUidsToDelete != null)
            {
                for (int i = 0; i < packageUidsToDelete.Count; i++)
                {
                    if (string.Equals(packageUidsToDelete[i], currentScenePkg, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            if (localScenes == null) return false;
            for (int i = 0; i < localScenes.Count; i++)
            {
                if (IsCurrentlyLoadedLocalScene(localScenes[i].AbsoluteJson, localScenes[i].GalleryRelativePath))
                    return true;
            }
            return false;
        }

        private static void AppendLoadedLocalSceneWarnings(List<LocalSceneDeleteItem> localScenes, List<string> warned)
        {
            if (localScenes == null || warned == null) return;
            for (int i = 0; i < localScenes.Count; i++)
            {
                if (!IsCurrentlyLoadedLocalScene(localScenes[i].AbsoluteJson, localScenes[i].GalleryRelativePath))
                    continue;
                string label = localScenes[i].GalleryRelativePath;
                if (string.IsNullOrEmpty(label)) label = localScenes[i].AbsoluteJson;
                warned.Add(label + " (currently loaded scene)");
            }
        }

        /// <summary>Package count for toolbox Delete (local scenes counted separately via <see cref="GetTboxDeleteEligibleLocalSceneCount"/>).</summary>
        private int GetTboxDeleteEligiblePackageCount()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return 0;
            var uids = CollectUniquePackageUidsFromSelection(selectedFiles);
            if (uids.Count == 0) return 0;

            string currentScenePkg = null;
            try { currentScenePkg = VamHookPlugin.CurrentScenePackageUid; }
            catch (Exception ex) { LogSuppressed("DeletePackages.CurrentScenePackageUid", ex); }
            HashSet<string> runningSceneDeps = TryGetRunningSceneDependenciesFast();

            var blocked = new List<string>();
            var warned = new List<string>();
            var toDelete = new List<string>();
            ClassifyUidsForTboxDelete(uids, currentScenePkg, runningSceneDeps, blocked, warned, toDelete);
            return toDelete.Count;
        }

        // Called by the toolbox button created in GalleryPanel.SelectionContextMenu.cs
        private void TboxDeleteSelectedPackages()
        {
            try
            {
                if (cleanupModeActive)
                {
                    TboxApplyCleanupSelected();
                    return;
                }

                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var localScenes = CollectLocalSceneDeleteItemsFromSelection(selectedFiles, true);
                var localPresets = CollectLocalPresetDeleteItemsFromSelection(selectedFiles);

                var uids = CollectUniquePackageUidsFromSelection(selectedFiles);

                string baseDir = Directory.GetCurrentDirectory();
                string deletedPkgDir = Path.Combine(baseDir, DeletedPackagesFolderName);
                string deletedSceneDir = Path.Combine(baseDir, DeletedLocalScenesFolderName);
                string deletedPresetDir = Path.Combine(baseDir, LocalPresetDeleteSupport.DeletedLocalPresetsFolderName);
                EnsureDeletedPackagesDirectory(deletedPkgDir);
                EnsureDeletedLocalScenesDirectory(deletedSceneDir);
                LocalPresetDeleteSupport.EnsureDeletedLocalPresetsDirectory(deletedPresetDir);

                string currentScenePkg = null;
                try { currentScenePkg = VamHookPlugin.CurrentScenePackageUid; }
                catch (Exception ex) { LogSuppressed("DeletePackages.CurrentScenePackageUid", ex); }

                HashSet<string> runningSceneDeps = TryGetRunningSceneDependenciesFast();

                var blocked = new List<string>();
                var warned = new List<string>();
                var toDelete = new List<string>();
                if (uids.Count > 0)
                    ClassifyUidsForTboxDelete(uids, currentScenePkg, runningSceneDeps, blocked, warned, toDelete);

                var relatedEntries = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < toDelete.Count; i++)
                {
                    string uid = toDelete[i];
                    if (string.IsNullOrEmpty(uid)) continue;
                    try
                    {
                        var rel = GetRelatedGalleryEntryNamesForPackage(uid, maxNames: 16);
                        if (rel != null && rel.Count > 0) relatedEntries[uid] = rel;
                    }
                    catch (Exception ex)
                    {
                        LogSuppressed("DeletePackages.GetRelatedGalleryEntryNamesForPackage", ex);
                    }
                }

                if (toDelete.Count == 0 && localScenes.Count == 0 && localPresets.Count == 0)
                {
                    if (uids.Count == 0)
                        ShowTemporaryStatus("Nothing to delete (no packages, local scenes, or local presets in selection).");
                    else
                        ShowTemporaryStatus(blocked.Count > 0 ? "Nothing to delete (blocked)." : "Nothing to delete.");
                    return;
                }

                bool deletingLoadedScene = SelectionDeletesCurrentlyLoadedScene(toDelete, currentScenePkg, localScenes);
                if (deletingLoadedScene)
                    AppendLoadedLocalSceneWarnings(localScenes, warned);

                // Related-entry dump: useful when a package hides collateral gallery items.
                // Multi-select with one listing per package = selection echo — skip (Hick / memory load).
                string relatedBlock = BuildRelatedEntriesBlock(relatedEntries, toDelete.Count);

                var summaryLines = new List<string>();
                if (deletingLoadedScene)
                {
                    summaryLines.Add(VPBTranslation.T(
                        "gallery.delete.warn_current_scene",
                        "Includes the currently loaded scene. Files move to DeletedPackages/DeletedScenes; scene stays in memory until you load another. Ctrl+Z undoes the move."));
                    summaryLines.Add("");
                }
                if (toDelete.Count > 0)
                {
                    AppendSectionTitle(summaryLines, string.Format(
                        VPBTranslation.T("gallery.delete.section_packages", "Move {0} package(s) to DeletedPackages"),
                        toDelete.Count));
                    AppendUidVarLines(summaryLines, toDelete);
                }
                if (localScenes.Count > 0)
                    summaryLines.Add(string.Format(
                        VPBTranslation.T("gallery.delete.section_local_scenes", "Move {0} local scene(s) (JSON and preview if present) to DeletedScenes."),
                        localScenes.Count));
                if (localPresets.Count > 0)
                    summaryLines.Add(string.Format(
                        VPBTranslation.T("gallery.delete.section_local_presets", "Move {0} local preset(s) to DeletedPresets."),
                        localPresets.Count));

                if (toDelete.Count > 0)
                {
                    summaryLines.Add("");
                    summaryLines.Add(VPBTranslation.T(
                        "gallery.delete.unused_deps_hint",
                        "Unused deps… scans for dependencies no other package uses, then asks again."));
                }

                string msg =
                    string.Join("\n", summaryLines.ToArray()) + "\n\n" +
                    (string.IsNullOrEmpty(relatedBlock) ? "" : (relatedBlock + "\n\n")) +
                    FormatDeleteWarnBlockedBlocks(warned, blocked);

                UnityAction onDelete = () =>
                    ExecuteTboxDeleteMoves(toDelete, localScenes, localPresets, deletedPkgDir, deletedSceneDir, deletedPresetDir);

                if (toDelete.Count > 0)
                {
                    DisplayConfirm(
                        VPBTranslation.T("gallery.delete.title", "Delete"),
                        msg,
                        onDelete,
                        null,
                        VPBTranslation.T("gallery.delete.btn_delete", "Delete"),
                        VPBTranslation.T("hook.cancel", "Cancel"),
                        () => TboxBeginExclusiveDepScan(
                            toDelete, localScenes, localPresets, blocked, warned, relatedBlock,
                            deletingLoadedScene, currentScenePkg, runningSceneDeps,
                            deletedPkgDir, deletedSceneDir, deletedPresetDir),
                        VPBTranslation.T("gallery.delete.btn_unused_deps", "Unused deps…"));
                }
                else
                {
                    DisplayConfirm(VPBTranslation.T("gallery.delete.title", "Delete"), msg, onDelete);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxDeleteSelectedPackages error: " + ex);
                ShowTemporaryStatus("Delete failed. See log.", 2f);
            }
        }

        private static string FormatDeleteWarnBlockedBlocks(List<string> warned, List<string> blocked)
        {
            var lines = new List<string>();
            AppendDistinctNamedSection(lines, warned, "gallery.delete.section_warnings", "Warnings ({0})");
            AppendDistinctNamedSection(lines, blocked, "gallery.delete.section_blocked", "Blocked — will not be deleted ({0})");
            if (lines.Count == 0) return "";
            return string.Join("\n", lines.ToArray()) + "\n\n";
        }

        private void ExecuteTboxDeleteMoves(
            List<string> packageUids,
            List<LocalSceneDeleteItem> localScenes,
            List<LocalPresetDeleteItem> localPresets,
            string deletedPkgDir,
            string deletedSceneDir,
            string deletedPresetDir)
        {
            try { SelectNextBeforeTboxDelete(packageUids, localScenes, localPresets); }
            catch (Exception ex) { LogSuppressed("DeletePackages.SelectNextBeforeTboxDelete", ex); }

            int pm = 0, pf = 0, sm = 0, sf = 0, prm = 0, prf = 0;
            var undoPairs = new List<FileMoveUndoPair>();
            if (packageUids != null && packageUids.Count > 0)
                PerformDeleteMove(packageUids, deletedPkgDir, out pm, out pf, undoPairs);
            if (localScenes != null && localScenes.Count > 0)
                PerformLocalScenesDeleteMove(localScenes, deletedSceneDir, out sm, out sf, undoPairs);
            if (localPresets != null && localPresets.Count > 0)
                PerformLocalPresetsDeleteMove(localPresets, deletedPresetDir, out prm, out prf, undoPairs);
            try
            {
                PushFileMoveUndo(
                    undoPairs,
                    VPBTranslation.T("gallery.undo.delete", "Delete"),
                    "tbox_undo_delete");
            }
            catch { }
            ShowCombinedDeleteStatus(pm, pf, sm, sf, prm, prf);
        }

        private void TboxBeginExclusiveDepScan(
            List<string> toDelete,
            List<LocalSceneDeleteItem> localScenes,
            List<LocalPresetDeleteItem> localPresets,
            List<string> blocked,
            List<string> warned,
            string relatedBlock,
            bool deletingLoadedScene,
            string currentScenePkg,
            HashSet<string> runningSceneDeps,
            string deletedPkgDir,
            string deletedSceneDir,
            string deletedPresetDir)
        {
            if (_tboxExclusiveDepScanCo != null)
            {
                try { StopCoroutine(_tboxExclusiveDepScanCo); } catch { }
                _tboxExclusiveDepScanCo = null;
            }
            _tboxExclusiveDepScanSerial++;
            _tboxExclusiveDepScanCo = StartCoroutine(TboxScanExclusiveDepsThenConfirm(
                toDelete, localScenes, localPresets, blocked, warned, relatedBlock,
                deletingLoadedScene, currentScenePkg, runningSceneDeps,
                deletedPkgDir, deletedSceneDir, deletedPresetDir,
                _tboxExclusiveDepScanSerial));
        }

        private IEnumerator TboxScanExclusiveDepsThenConfirm(
            List<string> toDelete,
            List<LocalSceneDeleteItem> localScenes,
            List<LocalPresetDeleteItem> localPresets,
            List<string> blocked,
            List<string> warned,
            string relatedBlock,
            bool deletingLoadedScene,
            string currentScenePkg,
            HashSet<string> runningSceneDeps,
            string deletedPkgDir,
            string deletedSceneDir,
            string deletedPresetDir,
            int serial)
        {
            var abort = new int[1];
            DisplayConfirm(
                VPBTranslation.T("gallery.delete.scan_unused_title", "Unused dependencies"),
                VPBTranslation.T("gallery.delete.scan_unused_msg", "Scanning for dependencies no other package uses…"),
                null,
                () => { abort[0] = 1; },
                null,
                VPBTranslation.T("hook.cancel", "Cancel"),
                hideConfirm: true);

            ExclusiveDependencyFinder.ScanInput snap = null;
            try { snap = ExclusiveDependencyFinder.CaptureInstalledSnapshot(toDelete); }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.CaptureInstalledSnapshot", ex);
            }

            var done = new int[1];
            ExclusiveDependencyFinder.ScanResult scanResult = null;
            Exception workerEx = null;
            ExclusiveDependencyFinder.ScanInput snapCapture = snap;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    scanResult = ExclusiveDependencyFinder.Find(snapCapture, () => abort[0] != 0);
                }
                catch (Exception ex)
                {
                    workerEx = ex;
                    scanResult = new ExclusiveDependencyFinder.ScanResult();
                }
                finally
                {
                    done[0] = 1;
                }
            });

            while (done[0] == 0)
                yield return null;

            _tboxExclusiveDepScanCo = null;
            if (serial != _tboxExclusiveDepScanSerial || abort[0] != 0)
                yield break;

            try { CloseConfirmOverlay(invokeCancel: false); } catch { }

            if (workerEx != null)
            {
                LogUtil.LogWarning("[VPB] Exclusive dep scan failed: " + workerEx.Message);
                ShowTemporaryStatus("Unused-dep scan failed. See log.", 2.5f);
                yield break;
            }

            if (scanResult == null) scanResult = new ExclusiveDependencyFinder.ScanResult();
            try
            {
                LogUtil.Log("[VPB] Exclusive deps scan selected=" + toDelete.Count
                    + " seedUids=" + scanResult.SeedCount
                    + " forwardSrc=" + scanResult.ForwardSources
                    + " seedTokens=" + scanResult.SeedTokens
                    + " candidates=" + scanResult.Candidates
                    + " exclusive=" + (scanResult.ExclusiveUids != null ? scanResult.ExclusiveUids.Count : 0)
                    + " varsScanned=" + scanResult.VarsScanned
                    + " resolveMiss=" + scanResult.ResolveMisses);
            }
            catch { }

            TboxShowDeleteWithExclusiveDepsConfirm(
                toDelete,
                scanResult,
                localScenes,
                localPresets,
                blocked,
                warned,
                relatedBlock,
                deletingLoadedScene,
                currentScenePkg,
                runningSceneDeps,
                deletedPkgDir,
                deletedSceneDir,
                deletedPresetDir);
        }

        private void TboxShowDeleteWithExclusiveDepsConfirm(
            List<string> toDelete,
            ExclusiveDependencyFinder.ScanResult scanResult,
            List<LocalSceneDeleteItem> localScenes,
            List<LocalPresetDeleteItem> localPresets,
            List<string> blocked,
            List<string> warned,
            string relatedBlock,
            bool deletingLoadedScene,
            string currentScenePkg,
            HashSet<string> runningSceneDeps,
            string deletedPkgDir,
            string deletedSceneDir,
            string deletedPresetDir)
        {
            var seedSet = new HashSet<string>(toDelete, StringComparer.OrdinalIgnoreCase);
            var exclusiveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> exclusiveRaw = scanResult != null ? scanResult.ExclusiveUids : null;
            if (exclusiveRaw != null)
            {
                for (int i = 0; i < exclusiveRaw.Count; i++)
                {
                    string u = exclusiveRaw[i];
                    if (!string.IsNullOrEmpty(u) && !seedSet.Contains(u))
                        exclusiveSet.Add(u);
                }
            }

            var exclusiveBlocked = new List<string>();
            var exclusiveWarned = new List<string>();
            var exclusiveOk = new List<string>();
            if (exclusiveSet.Count > 0)
                ClassifyUidsForTboxDelete(exclusiveSet, currentScenePkg, runningSceneDeps, exclusiveBlocked, exclusiveWarned, exclusiveOk, warnDependents: false);

            var combined = new List<string>(toDelete.Count + exclusiveOk.Count);
            for (int i = 0; i < toDelete.Count; i++)
                combined.Add(toDelete[i]);
            for (int i = 0; i < exclusiveOk.Count; i++)
            {
                if (!seedSet.Contains(exclusiveOk[i]))
                    combined.Add(exclusiveOk[i]);
            }

            var warnAll = new List<string>();
            if (warned != null) warnAll.AddRange(warned);
            warnAll.AddRange(exclusiveWarned);
            var blockAll = new List<string>();
            if (blocked != null) blockAll.AddRange(blocked);
            blockAll.AddRange(exclusiveBlocked);

            bool loaded = deletingLoadedScene
                || SelectionDeletesCurrentlyLoadedScene(combined, currentScenePkg, localScenes);

            var summaryLines = new List<string>();
            if (loaded)
            {
                summaryLines.Add(VPBTranslation.T(
                    "gallery.delete.warn_current_scene",
                    "Includes the currently loaded scene. Files move to DeletedPackages/DeletedScenes; scene stays in memory until you load another. Ctrl+Z undoes the move."));
                summaryLines.Add("");
            }

            if (toDelete.Count > 0)
            {
                AppendSectionTitle(summaryLines, string.Format(
                    VPBTranslation.T("gallery.delete.section_packages", "Move {0} package(s) to DeletedPackages"),
                    toDelete.Count));
                AppendUidVarLines(summaryLines, toDelete);
            }
            if (exclusiveOk.Count > 0)
            {
                AppendSectionTitle(summaryLines, string.Format(
                    VPBTranslation.T("gallery.delete.unused_deps_found", "Unused dependencies ({0}, no other package references these):"),
                    exclusiveOk.Count));
                AppendUidVarLines(summaryLines, exclusiveOk);
            }
            else
            {
                AppendSectionTitle(summaryLines, VPBTranslation.T(
                    "gallery.delete.unused_deps_none",
                    "No unused dependencies. Delete selected packages only?"));
            }
            if (exclusiveBlocked.Count > 0)
            {
                AppendSectionTitle(summaryLines, VPBTranslation.T("gallery.delete.unused_deps_skipped", "Skipped unused deps:"));
                for (int i = 0; i < exclusiveBlocked.Count; i++)
                    summaryLines.Add(exclusiveBlocked[i]);
            }
            if (localScenes != null && localScenes.Count > 0)
                summaryLines.Add(string.Format(
                    VPBTranslation.T("gallery.delete.section_local_scenes", "Move {0} local scene(s) (JSON and preview if present) to DeletedScenes."),
                    localScenes.Count));
            if (localPresets != null && localPresets.Count > 0)
                summaryLines.Add(string.Format(
                    VPBTranslation.T("gallery.delete.section_local_presets", "Move {0} local preset(s) to DeletedPresets."),
                    localPresets.Count));

            string msg =
                string.Join("\n", summaryLines.ToArray()) + "\n\n" +
                (string.IsNullOrEmpty(relatedBlock) ? "" : (relatedBlock + "\n\n")) +
                FormatDeleteWarnBlockedBlocks(warnAll, blockAll);

            DisplayConfirm(
                VPBTranslation.T("gallery.delete.title", "Delete"),
                msg,
                () => ExecuteTboxDeleteMoves(combined, localScenes, localPresets, deletedPkgDir, deletedSceneDir, deletedPresetDir),
                null,
                exclusiveOk.Count > 0
                    ? VPBTranslation.T("gallery.delete.confirm_with_deps", "Delete all")
                    : VPBTranslation.T("gallery.delete.btn_delete", "Delete"),
                VPBTranslation.T("hook.cancel", "Cancel"));
        }

        private void ShowCombinedDeleteStatus(int pkgMoved, int pkgFailed, int sceneMoved, int sceneFailed, int presetMoved, int presetFailed)
        {
            int ok = pkgMoved + sceneMoved + presetMoved;
            int fail = pkgFailed + sceneFailed + presetFailed;
            if (ok == 0 && fail == 0) return;

            var parts = new List<string>();
            if (pkgMoved > 0) parts.Add(pkgMoved + " package(s)");
            if (sceneMoved > 0) parts.Add(sceneMoved + " local scene(s)");
            if (presetMoved > 0) parts.Add(presetMoved + " local preset(s)");

            if (fail == 0)
            {
                ShowTemporaryStatus("Deleted " + string.Join(" and ", parts.ToArray()) + ". Ctrl+Z undoes.", 2.5f);
                return;
            }

            if (ok > 0)
                ShowTemporaryStatus("Deleted " + string.Join(", ", parts.ToArray()) + "; " + fail + " failed. See log.", 3f);
            else
                ShowTemporaryStatus("Delete failed (" + fail + "). See log.", 3f);
        }

        /// <summary>
        /// If entire selection will leave the grid after delete, select nearest survivor first
        /// so detail strip stays open (same nearest-next policy as rating prune).
        /// </summary>
        private void SelectNextBeforeTboxDelete(
            List<string> packageUidsToDelete,
            List<LocalSceneDeleteItem> localScenes,
            List<LocalPresetDeleteItem> localPresets)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;

            var pkgUids = packageUidsToDelete != null && packageUidsToDelete.Count > 0
                ? new HashSet<string>(packageUidsToDelete, StringComparer.OrdinalIgnoreCase)
                : null;
            HashSet<string> scenePaths = null;
            if (localScenes != null && localScenes.Count > 0)
            {
                scenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < localScenes.Count; i++)
                {
                    string rp = localScenes[i].GalleryRelativePath;
                    if (!string.IsNullOrEmpty(rp))
                        scenePaths.Add(rp.Replace('\\', '/'));
                }
            }
            HashSet<string> presetPaths = null;
            if (localPresets != null && localPresets.Count > 0)
            {
                presetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < localPresets.Count; i++)
                {
                    string rp = localPresets[i].RelativePath;
                    if (!string.IsNullOrEmpty(rp))
                        presetPaths.Add(rp.Replace('\\', '/'));
                }
            }

            if ((pkgUids == null || pkgUids.Count == 0)
                && (scenePaths == null || scenePaths.Count == 0)
                && (presetPaths == null || presetPaths.Count == 0))
                return;

            bool historyBrowse = activeContentType == ContentType.History;
            bool anySurvivor = false;
            int firstRemovedIdx = -1;
            int anchorRemovedIdx = -1;
            string anchorKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse);

            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry sel = selectedFiles[i];
                if (sel == null) continue;
                if (!WillGalleryEntryBeRemovedByTboxDelete(sel, pkgUids, scenePaths, presetPaths))
                {
                    anySurvivor = true;
                    continue;
                }

                int idx = FindIndexBySelectionIdentity(
                    currentFilteredFiles,
                    GetSelectionIdentityKey(sel, historyBrowse),
                    historyBrowse);
                if (idx >= 0 && (firstRemovedIdx < 0 || idx < firstRemovedIdx))
                    firstRemovedIdx = idx;

                if (!string.IsNullOrEmpty(anchorKey)
                    && string.Equals(
                        GetSelectionIdentityKey(sel, historyBrowse),
                        anchorKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    anchorRemovedIdx = idx;
                }
            }

            // Partial multi-select: purge keeps survivors — no reselect needed.
            if (anySurvivor) return;

            int pivot = anchorRemovedIdx >= 0 ? anchorRemovedIdx : firstRemovedIdx;
            FileEntry next = FindNearestSurvivorBeforeTboxDelete(
                currentFilteredFiles, pivot, pkgUids, scenePaths, presetPaths);
            if (next == null) return;

            try { DetailStripUnlockAfterExternalSelectionChange(); } catch { }

            selectedFiles.Clear();
            selectedFilePaths.Clear();
            AddFileToSelection(next, historyBrowse);
            SetSelectionAnchor(next, historyBrowse);
            selectedPath = historyBrowse ? GetSelectionIdentityKey(next, true) : next.Path;

            try { RefreshSelectionVisuals(); } catch { }
        }

        private static bool WillGalleryEntryBeRemovedByTboxDelete(
            FileEntry f,
            HashSet<string> packageUidsToDelete,
            HashSet<string> localSceneRelPaths,
            HashSet<string> localPresetRelPaths)
        {
            if (f == null) return false;

            if (packageUidsToDelete != null && packageUidsToDelete.Count > 0)
            {
                try
                {
                    if (f is VarFileEntry vfe && vfe.Package != null
                        && packageUidsToDelete.Contains(vfe.Package.Uid))
                        return true;
                }
                catch { }

                string uid = TryGetPackageUidForEntry(f);
                if (!string.IsNullOrEmpty(uid) && packageUidsToDelete.Contains(uid))
                    return true;
            }

            string path = null;
            try { path = f.Path ?? f.Uid; } catch { path = null; }
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');

            if (localSceneRelPaths != null && localSceneRelPaths.Contains(path))
                return true;
            if (localPresetRelPaths != null && localPresetRelPaths.Contains(path))
                return true;
            return false;
        }

        private static FileEntry FindNearestSurvivorBeforeTboxDelete(
            List<FileEntry> files,
            int pivotIndex,
            HashSet<string> packageUidsToDelete,
            HashSet<string> localSceneRelPaths,
            HashSet<string> localPresetRelPaths)
        {
            if (files == null || files.Count == 0) return null;
            int n = files.Count;
            if (pivotIndex < 0) pivotIndex = 0;
            if (pivotIndex >= n) pivotIndex = n - 1;

            for (int i = pivotIndex + 1; i < n; i++)
            {
                FileEntry fe = files[i];
                if (fe != null
                    && !WillGalleryEntryBeRemovedByTboxDelete(fe, packageUidsToDelete, localSceneRelPaths, localPresetRelPaths))
                    return fe;
            }
            for (int i = pivotIndex - 1; i >= 0; i--)
            {
                FileEntry fe = files[i];
                if (fe != null
                    && !WillGalleryEntryBeRemovedByTboxDelete(fe, packageUidsToDelete, localSceneRelPaths, localPresetRelPaths))
                    return fe;
            }
            return null;
        }

        private static void EnsureDeletedPackagesDirectory(string deletedDir)
        {
            try
            {
                if (!Directory.Exists(deletedDir)) Directory.CreateDirectory(deletedDir);
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.EnsureDeletedPackagesDirectory", ex);
            }
        }

        private void PerformDeleteMove(List<string> uids, string deletedDir, out int moved, out int failed, List<FileMoveUndoPair> undoOut = null)
        {
            moved = 0;
            failed = 0;
            var movedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < uids.Count; i++)
            {
                string uid = uids[i];
                if (string.IsNullOrEmpty(uid)) continue;

                try
                {
                    string srcPath = ResolveVarPathForUid(uid);
                    if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) { failed++; continue; }

                    string fileName = Path.GetFileName(srcPath);
                    if (string.IsNullOrEmpty(fileName)) fileName = uid + ".var";

                    string dstPath = Path.Combine(deletedDir, fileName);
                    if (File.Exists(dstPath))
                    {
                        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string baseName = Path.GetFileNameWithoutExtension(fileName);
                        dstPath = Path.Combine(deletedDir, baseName + "__" + stamp + ".var");
                    }

                    File.Move(srcPath, dstPath);
                    moved++;
                    movedUids.Add(uid);
                    if (undoOut != null)
                    {
                        FileMoveUndoPair pair;
                        pair.FromDeletedPath = dstPath;
                        pair.ToOriginalPath = srcPath;
                        undoOut.Add(pair);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    LogUtil.LogError("[VPB] Delete(move) failed for " + uid + ": " + ex.Message);
                }
            }

            try { PurgeGalleryEntriesForMovedPackages(movedUids); }
            catch (Exception ex) { LogSuppressed("DeletePackages.PurgeGalleryEntriesForMovedPackages", ex); }

            try
            {
                try { FileManagerBridge.Refresh("tbox_delete_packages", RefreshScope.Both); }
                catch (Exception ex) { LogSuppressed("DeletePackages.FileManagerBridge.Refresh", ex); }
            }
            catch (Exception ex) { LogSuppressed("DeletePackages.RefreshWrapper", ex); }
        }

        private static int GetDependentCount(string uid)
        {
            try
            {
                var pkg = FileManager.GetPackageForDependency(uid, false);
                if (pkg != null)
                    return FileManager.ResolveDependentCount(pkg);
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.GetDependentCount(FileManager)", ex);
            }

            try
            {
                if (DependencyGraph.TryGetTransitiveDependents(uid, out HashSet<string> deps) && deps != null)
                    return deps.Count;
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.GetDependentCount(DependencyGraph)", ex);
            }

            return 0;
        }

        private static string ResolveVarPathForUid(string uid)
        {
            try
            {
                var pkg = FileManager.GetPackageForDependency(uid, false);
                if (pkg != null && !string.IsNullOrEmpty(pkg.Path))
                {
                    string p = pkg.Path.Replace('\\', '/');
                    if (File.Exists(p)) return p;

                    // Handle AllPackages -> AddonPackages mapping (common "installed" location)
                    if (p.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
                    {
                        string mapped = "AddonPackages/" + p.Substring("AllPackages/".Length);
                        if (File.Exists(mapped)) return mapped;
                    }
                }
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.ResolveVarPathForUid(FileManager)", ex);
            }

            // Fallback: try common folder by filename
            try
            {
                string candidate = "AddonPackages/" + uid + ".var";
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.ResolveVarPathForUid(FallbackExists)", ex);
            }

            return null;
        }

        private static HashSet<string> TryGetRunningSceneDependenciesFast()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc == null) return null;

                string json = TryGetSceneJsonString(sc);
                if (string.IsNullOrEmpty(json)) return null;

                // This is fast and does not require full JSON walking
                return DependencyExtractor.ExtractDependenciesFromRawText(json);
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.TryGetRunningSceneDependenciesFast", ex);
            }
            return null;
        }

        private static string TryGetSceneJsonString(SuperController sc)
        {
            try
            {
                string[] sceneCandidates = new[]
                {
                    "GetSaveJSON",
                    "GetSaveSceneJSON",
                    "GetSceneJSON",
                    "GetJSON",
                    "GetSaveJson",
                    "GetSceneJson",
                };

                var t = sc.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                for (int i = 0; i < sceneCandidates.Length; i++)
                {
                    MethodInfo mi = null;
                    try { mi = t.GetMethod(sceneCandidates[i], flags); }
                    catch (Exception ex) { LogSuppressed("DeletePackages.TryGetSceneJsonString.GetMethod(" + sceneCandidates[i] + ")", ex, verbose: true); }
                    if (mi == null) continue;
                    var ps = mi.GetParameters();
                    if (ps != null && ps.Length != 0) continue;

                    object result = null;
                    try { result = mi.Invoke(sc, null); }
                    catch (Exception ex) { LogSuppressed("DeletePackages.TryGetSceneJsonString.Invoke(" + sceneCandidates[i] + ")", ex, verbose: true); }
                    if (result == null) continue;

                    if (result is JSONNode node)
                        return node.ToString();

                    string s = null;
                    try { s = result.ToString(); }
                    catch (Exception ex) { LogSuppressed("DeletePackages.TryGetSceneJsonString.ToString(" + sceneCandidates[i] + ")", ex, verbose: true); }
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.TryGetSceneJsonString", ex);
            }
            return null;
        }

        private List<string> GetRelatedGalleryEntryNamesForPackage(string packageUid, int maxNames = 16)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(packageUid)) return names;

            // Prefer current filtered list (what the user is actually looking at)
            try
            {
                if (currentFilteredFiles != null)
                {
                    for (int i = 0; i < currentFilteredFiles.Count; i++)
                    {
                        var f = currentFilteredFiles[i];
                        if (f == null) continue;
                        if (!(f is VarFileEntry vfe) || vfe.Package == null) continue;
                        if (!string.Equals(vfe.Package.Uid, packageUid, StringComparison.OrdinalIgnoreCase)) continue;

                        string label = null;
                        try { label = !string.IsNullOrEmpty(vfe.InternalPath) ? vfe.InternalPath : vfe.Name; } catch { label = vfe.Name; }
                        if (string.IsNullOrEmpty(label)) label = vfe.Name;
                        names.Add(label);
                        if (names.Count >= maxNames) break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.GetRelatedGalleryEntryNamesForPackage(CurrentFilteredFiles)", ex);
            }

            if (names.Count > 0) return names;

            // Fallback to package cache (shows "other scenes" even if not visible in current list)
            try
            {
                var pkg = FileManager.GetPackageForDependency(packageUid, false);
                if (pkg != null && pkg.TryGetCachedFileEntryData(out List<string> entryNames, out _, out _))
                {
                    for (int i = 0; i < entryNames.Count && names.Count < maxNames; i++)
                    {
                        var p = entryNames[i];
                        if (string.IsNullOrEmpty(p)) continue;
                        if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            names.Add(p);
                    }
                }
            }
            catch (Exception ex)
            {
                LogSuppressed("DeletePackages.GetRelatedGalleryEntryNamesForPackage(PackageCache)", ex);
            }

            return names;
        }

        /// <summary>Section header with blank line before (Gestalt chunking).</summary>
        private static void AppendSectionTitle(List<string> lines, string title)
        {
            if (lines == null || string.IsNullOrEmpty(title)) return;
            if (lines.Count > 0)
                lines.Add("");
            lines.Add(title);
        }

        private static void AppendUidVarLines(List<string> lines, List<string> uids)
        {
            if (lines == null || uids == null) return;
            for (int i = 0; i < uids.Count; i++)
            {
                string uid = uids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                lines.Add(uid + ".var");
            }
        }

        private static void AppendDistinctNamedSection(List<string> lines, List<string> items, string key, string english)
        {
            if (lines == null || items == null || items.Count == 0) return;
            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                string s = items[i];
                if (string.IsNullOrEmpty(s) || !seen.Add(s)) continue;
                unique.Add(s);
            }
            if (unique.Count == 0) return;
            AppendSectionTitle(lines, string.Format(VPBTranslation.T(key, english), unique.Count));
            for (int i = 0; i < unique.Count; i++)
                lines.Add(unique[i]);
        }

        /// <summary>
        /// Related gallery paths inside packages being moved.
        /// Single package: list paths (collateral awareness).
        /// Multi: only when some package exposes &gt;1 listing; otherwise selection already named the tiles.
        /// </summary>
        private static string BuildRelatedEntriesBlock(Dictionary<string, List<string>> relatedEntries, int packageDeleteCount)
        {
            if (relatedEntries == null || relatedEntries.Count == 0) return "";

            int maxPerPkg = 0;
            int totalEntries = 0;
            foreach (var kvp in relatedEntries)
            {
                int n = kvp.Value != null ? kvp.Value.Count : 0;
                totalEntries += n;
                if (n > maxPerPkg) maxPerPkg = n;
            }
            if (totalEntries == 0) return "";

            bool multi = packageDeleteCount > 1 || relatedEntries.Count > 1;
            // Multi + one listing per package = echo of selection / package list. Skip.
            if (multi && maxPerPkg <= 1) return "";

            var lines = new List<string>();

            if (!multi)
            {
                // One package: keep scannable path list under a single header.
                lines.Add("Also removing related gallery entries:");
                foreach (var kvp in relatedEntries)
                {
                    var list = kvp.Value;
                    if (list == null) continue;
                    int shown = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (string.IsNullOrEmpty(list[i])) continue;
                        if (shown >= 10) { lines.Add("- ..."); break; }
                        lines.Add("- " + list[i]);
                        shown++;
                    }
                }
                return string.Join("\n", lines.ToArray());
            }

            // Multi with collateral (unselected scenes/presets inside package).
            lines.Add("Also removing other gallery entries inside these packages:");
            int pkgShown = 0;
            foreach (var kvp in relatedEntries)
            {
                var list = kvp.Value;
                // Only packages with more than one listing are collateral risk.
                if (list == null || list.Count <= 1) continue;
                if (pkgShown >= 6) { lines.Add("- ..."); break; }
                string uid = kvp.Key;

                if (list.Count <= 3)
                {
                    lines.Add("- " + uid + ".var — " + string.Join("; ", list.ToArray()));
                }
                else
                {
                    lines.Add("- " + uid + ".var — " + list.Count + " entries (" + list[0] + "; …)");
                }
                pkgShown++;
            }
            if (pkgShown == 0) return "";
            return string.Join("\n", lines.ToArray());
        }

        private void PurgeGalleryEntriesForMovedPackages(HashSet<string> movedUids)
        {
            if (movedUids == null || movedUids.Count == 0) return;

            try
            {
                // Remove from selection
                if (selectedFiles != null)
                {
                    selectedFiles.RemoveAll(f =>
                    {
                        try
                        {
                            if (f is VarFileEntry vfe && vfe.Package != null)
                                return movedUids.Contains(vfe.Package.Uid);
                        }
                        catch (Exception ex) { LogSuppressed("DeletePackages.PurgeGalleryEntries.SelectionPredicate", ex, verbose: true); }
                        return false;
                    });
                }
                try { RefreshSelectionVisuals(); }
                catch (Exception ex) { LogSuppressed("DeletePackages.RefreshSelectionVisuals", ex); }
            }
            catch (Exception ex) { LogSuppressed("DeletePackages.PurgeGalleryEntries.Selection", ex); }

            try
            {
                // Remove from current list and refresh UI
                if (currentFilteredFiles != null)
                {
                    int before = currentFilteredFiles.Count;
                    currentFilteredFiles.RemoveAll(f =>
                    {
                        try
                        {
                            if (f is VarFileEntry vfe && vfe.Package != null)
                                return movedUids.Contains(vfe.Package.Uid);
                        }
                        catch (Exception ex) { LogSuppressed("DeletePackages.PurgeGalleryEntries.ListPredicate", ex, verbose: true); }
                        return false;
                    });

                    if (recyclingGrid != null && currentFilteredFiles.Count != before)
                    {
                        recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                        recyclingGrid.Refresh();
                    }
                    try { UpdatePaginationText(); }
                    catch (Exception ex) { LogSuppressed("DeletePackages.UpdatePaginationText", ex); }
                }
            }
            catch (Exception ex) { LogSuppressed("DeletePackages.PurgeGalleryEntries.CurrentList", ex); }
        }
    }
}

