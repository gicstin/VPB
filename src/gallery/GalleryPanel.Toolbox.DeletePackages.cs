using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using SimpleJSON;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private const string DeletedPackagesFolderName = "DeletedPackages";

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
            List<string> toDelete)
        {
            if (uids == null || uids.Count == 0) return;

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
                catch { }

                // Critical: do not delete the currently loaded scene's package
                if (!string.IsNullOrEmpty(currentScenePkg) && string.Equals(uid, currentScenePkg, StringComparison.OrdinalIgnoreCase))
                {
                    blocked.Add($"{uid}.var (current scene package)");
                    continue;
                }

                // Critical: if the running scene references this package, require confirm (or block if you prefer)
                if (runningSceneDeps != null && runningSceneDeps.Contains(uid))
                {
                    warned.Add($"{uid}.var (referenced by running scene)");
                }

                // Dependents warning
                int depCount = 0;
                try { depCount = GetDependentCount(uid); } catch { depCount = 0; }
                if (depCount > 0)
                {
                    warned.Add($"{uid}.var ({depCount} dependents)");
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
                catch { }

                // Critical: avoid moving enabled auto-install packages (too easy to break user prefs)
                try
                {
                    // Only warn; user may still proceed
                    if (FileEntry.AutoInstallLookup != null && FileEntry.AutoInstallLookup.Contains(uid))
                        warned.Add($"{uid}.var (auto-install enabled)");
                }
                catch { }

                toDelete.Add(uid);
            }
        }

        /// <summary>Package count for toolbox Delete (local scenes counted separately via <see cref="GetTboxDeleteEligibleLocalSceneCount"/>).</summary>
        private int GetTboxDeleteEligiblePackageCount()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return 0;
            var uids = CollectUniquePackageUidsFromSelection(selectedFiles);
            if (uids.Count == 0) return 0;

            string currentScenePkg = null;
            try { currentScenePkg = VamHookPlugin.CurrentScenePackageUid; } catch { }
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

                var uids = CollectUniquePackageUidsFromSelection(selectedFiles);

                string baseDir = Directory.GetCurrentDirectory();
                string deletedPkgDir = Path.Combine(baseDir, DeletedPackagesFolderName);
                string deletedSceneDir = Path.Combine(baseDir, DeletedLocalScenesFolderName);
                EnsureDeletedPackagesDirectory(deletedPkgDir);
                EnsureDeletedLocalScenesDirectory(deletedSceneDir);

                string currentScenePkg = null;
                try { currentScenePkg = VamHookPlugin.CurrentScenePackageUid; } catch { }

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
                    catch { }
                }

                if (toDelete.Count == 0 && localScenes.Count == 0)
                {
                    if (uids.Count == 0)
                        ShowTemporaryStatus("Nothing to delete (no packages or local scenes in selection).");
                    else
                        ShowTemporaryStatus(blocked.Count > 0 ? "Nothing to delete (blocked)." : "Nothing to delete.");
                    return;
                }

                string relatedBlock = BuildRelatedEntriesBlock(relatedEntries);

                var summaryLines = new List<string>();
                if (toDelete.Count > 0)
                    summaryLines.Add($"Move {toDelete.Count} package(s) into '{DeletedPackagesFolderName}'.");
                if (localScenes.Count > 0)
                    summaryLines.Add($"Move {localScenes.Count} local scene(s) (JSON and preview image if present) into '{DeletedLocalScenesFolderName}'.");

                string msg =
                    string.Join("\n", summaryLines.ToArray()) + "\n\n" +
                    (string.IsNullOrEmpty(relatedBlock) ? "" : (relatedBlock + "\n\n")) +
                    (warned.Count > 0 ? ("Warnings:\n- " + string.Join("\n- ", warned.Distinct().Take(12).ToArray()) + (warned.Count > 12 ? "\n- ..." : "") + "\n\n") : "") +
                    (blocked.Count > 0 ? ("Blocked packages (will NOT be deleted):\n- " + string.Join("\n- ", blocked.Distinct().Take(12).ToArray()) + (blocked.Count > 12 ? "\n- ..." : "") + "\n\n") : "") +
                    "Proceed?";

                DisplayConfirm("Delete", msg, () =>
                {
                    int pm = 0, pf = 0, sm = 0, sf = 0;
                    if (toDelete.Count > 0)
                        PerformDeleteMove(toDelete, deletedPkgDir, out pm, out pf);
                    if (localScenes.Count > 0)
                        PerformLocalScenesDeleteMove(localScenes, deletedSceneDir, out sm, out sf);
                    ShowCombinedDeleteStatus(pm, pf, sm, sf);
                });
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxDeleteSelectedPackages error: " + ex);
                ShowTemporaryStatus("Delete failed. See log.", 2f);
            }
        }

        private void ShowCombinedDeleteStatus(int pkgMoved, int pkgFailed, int sceneMoved, int sceneFailed)
        {
            int ok = pkgMoved + sceneMoved;
            int fail = pkgFailed + sceneFailed;
            if (ok == 0 && fail == 0) return;

            var parts = new List<string>();
            if (pkgMoved > 0) parts.Add(pkgMoved + " package(s)");
            if (sceneMoved > 0) parts.Add(sceneMoved + " local scene(s)");

            if (fail == 0)
            {
                ShowTemporaryStatus("Deleted " + string.Join(" and ", parts.ToArray()) + ".", 2f);
                return;
            }

            if (ok > 0)
                ShowTemporaryStatus("Deleted " + string.Join(", ", parts.ToArray()) + "; " + fail + " failed. See log.", 3f);
            else
                ShowTemporaryStatus("Delete failed (" + fail + "). See log.", 3f);
        }

        private static void EnsureDeletedPackagesDirectory(string deletedDir)
        {
            try
            {
                if (!Directory.Exists(deletedDir)) Directory.CreateDirectory(deletedDir);
            }
            catch { }
        }

        private void PerformDeleteMove(List<string> uids, string deletedDir, out int moved, out int failed)
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
                }
                catch (Exception ex)
                {
                    failed++;
                    LogUtil.LogError("[VPB] Delete(move) failed for " + uid + ": " + ex.Message);
                }
            }

            try { PurgeGalleryEntriesForMovedPackages(movedUids); } catch { }

            try
            {
                // Best-effort refresh to reflect moved packages
                try { FileManager.Refresh(); } catch { }
                try { if (MVR.FileManagement.FileManager.singleton != null) MVR.FileManagement.FileManager.Refresh(); } catch { }
            }
            catch { }
        }

        private static int GetDependentCount(string uid)
        {
            // Prefer explicit dependent count (rebuilt by scans)
            try
            {
                var pkg = FileManager.GetPackageForDependency(uid, false);
                if (pkg != null) return Math.Max(0, pkg.DependentCount);
            }
            catch { }

            // Fallback to dependency graph query
            try
            {
                if (DependencyGraph.TryGetTransitiveDependents(uid, out HashSet<string> deps) && deps != null)
                    return deps.Count;
            }
            catch { }

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
            catch { }

            // Fallback: try common folder by filename
            try
            {
                string candidate = "AddonPackages/" + uid + ".var";
                if (File.Exists(candidate)) return candidate;
            }
            catch { }

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
            catch { }
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
                    try { mi = t.GetMethod(sceneCandidates[i], flags); } catch { }
                    if (mi == null) continue;
                    var ps = mi.GetParameters();
                    if (ps != null && ps.Length != 0) continue;

                    object result = null;
                    try { result = mi.Invoke(sc, null); } catch { }
                    if (result == null) continue;

                    if (result is JSONNode node)
                        return node.ToString();

                    string s = null;
                    try { s = result.ToString(); } catch { }
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
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
            catch { }

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
            catch { }

            return names;
        }

        private static string BuildRelatedEntriesBlock(Dictionary<string, List<string>> relatedEntries)
        {
            if (relatedEntries == null || relatedEntries.Count == 0) return "";

            var lines = new List<string>();
            int pkgShown = 0;
            foreach (var kvp in relatedEntries)
            {
                if (pkgShown >= 6) { lines.Add("..."); break; }
                string uid = kvp.Key;
                var list = kvp.Value ?? new List<string>();

                lines.Add($"Also removing related entries from {uid}.var:");
                int shown = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (shown >= 10) { lines.Add("- ..."); break; }
                    lines.Add("- " + list[i]);
                    shown++;
                }
                pkgShown++;
            }
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
                        catch { }
                        return false;
                    });
                }
                try { RefreshSelectionVisuals(); } catch { }
            }
            catch { }

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
                        catch { }
                        return false;
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

