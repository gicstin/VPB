using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private void TboxLoadSelectedPackages()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var pkgs = new List<VarPackage>(8);
                TryCollectUniqueVarPackagesFromSelection(pkgs);
                if (pkgs.Count == 0)
                {
                    ShowTemporaryStatus("No .var packages in selection.", 2f);
                    return;
                }

                int moved = 0;
                var movedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < pkgs.Count; i++)
                {
                    VarPackage p = pkgs[i];
                    if (p == null) continue;
                    try
                    {
                        if (p.InstallSelf())
                        {
                            moved++;
                            if (!string.IsNullOrEmpty(p.Uid)) movedUids.Add(p.Uid);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxLoadSelectedPackages " + p.Uid + ": " + ex.Message);
                    }
                }

                if (movedUids.Count > 0)
                    RefreshAfterTboxPackageFileMoves(movedUids);
                ShowTemporaryStatus(moved > 0
                    ? $"Load: moved {moved} package(s) to AddonPackages."
                    : "Load: nothing to move (already installed or blocked).", 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxLoadSelectedPackages error: " + ex);
                ShowTemporaryStatus("Load failed. See log.", 2f);
            }
        }

        private void TboxUnloadSelectedPackages()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var pkgs = new List<VarPackage>(8);
                TryCollectUniqueVarPackagesFromSelection(pkgs);
                if (pkgs.Count == 0)
                {
                    ShowTemporaryStatus("No .var packages in selection.", 2f);
                    return;
                }

                int moved = 0;
                var movedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < pkgs.Count; i++)
                {
                    VarPackage p = pkgs[i];
                    if (p == null) continue;
                    try
                    {
                        if (p.UninstallSelf())
                        {
                            moved++;
                            if (!string.IsNullOrEmpty(p.Uid)) movedUids.Add(p.Uid);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxUnloadSelectedPackages " + p.Uid + ": " + ex.Message);
                    }
                }

                if (movedUids.Count > 0)
                    RefreshAfterTboxPackageFileMoves(movedUids);
                ShowTemporaryStatus(moved > 0
                    ? $"Unload: moved {moved} package(s) to AllPackages."
                    : "Unload: nothing to move (not in AddonPackages or blocked).", 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxUnloadSelectedPackages error: " + ex);
                ShowTemporaryStatus("Unload failed. See log.", 2f);
            }
        }

        private void TboxLoadDepsSelectedPackages()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var pkgs = new List<VarPackage>(8);
                TryCollectUniqueVarPackagesFromSelection(pkgs);
                if (pkgs.Count == 0)
                {
                    ShowTemporaryStatus("No .var packages in selection.", 2f);
                    return;
                }

                int moved = 0;
                var movedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var buf = new List<string>(32);
                for (int i = 0; i < pkgs.Count; i++)
                {
                    VarPackage p = pkgs[i];
                    if (p == null) continue;
                    try
                    {
                        buf.Clear();
                        if (!p.InstallRecursive(buf)) continue;
                        moved++;
                        for (int j = 0; j < buf.Count; j++)
                        {
                            string u = buf[j];
                            if (!string.IsNullOrEmpty(u)) movedUids.Add(u);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxLoadDepsSelectedPackages " + p.Uid + ": " + ex.Message);
                    }
                }

                if (movedUids.Count > 0)
                    RefreshAfterTboxPackageFileMoves(movedUids);
                ShowTemporaryStatus(moved > 0
                    ? $"Load deps: installed {moved} tree(s) (self + dependencies per settings)."
                    : "Load deps: nothing to install.", 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxLoadDepsSelectedPackages error: " + ex);
                ShowTemporaryStatus("Load deps failed. See log.", 2f);
            }
        }

        private void TboxCacheTexturesSelected()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var packagePaths = new List<string>(8);
                var scenePaths = new List<string>(8);
                TryCollectUniqueVarPackagePathsFromSelection(packagePaths);
                TryCollectUniqueLocalSceneJsonPathsFromSelection(scenePaths);
                if (packagePaths.Count == 0 && scenePaths.Count == 0)
                {
                    ShowTemporaryStatus("No .var packages or local scene JSON in selection.", 2f);
                    return;
                }

                try { NativeTextureCacheBuildOverlay.EnsureCreated(); } catch { }
                try { NativeTextureOnDemandCache.DismissSummary(); } catch { }

                if (NativeTextureOnDemandCache.IsOnDemandBusy)
                {
                    ShowTemporaryStatus("Texture caching already running.", 2f);
                    return;
                }

                bool purgeCache = IsCtrlShiftHeldForTextureCachePurge();
                if (purgeCache)
                {
                    // Purge supports:
                    // - .var packages (purges package texture caches)
                    // - local scenes (purges only local disk texture caches referenced by the scene)
                    if (packagePaths.Count == 1 && scenePaths.Count == 0)
                    {
                        NativeTextureOnDemandCache.TryPurgePackageCacheOnDemand(this, packagePaths[0]);
                        return;
                    }
                    if (scenePaths.Count == 1 && packagePaths.Count == 0)
                    {
                        NativeTextureOnDemandCache.TryPurgeSceneCacheOnDemand(this, scenePaths[0]);
                        return;
                    }

                    StartCoroutine(TboxPurgeTexturesMultiBatchCoroutine(scenePaths, packagePaths));
                    return;
                }

                bool rewriteExistingZstd = IsCtrlHeldForTextureCacheRewrite();
                NativeTextureOnDemandCache.SetNextJobWriteModeOverride(NativeTextureOnDemandCache.CacheWriteMode.ZstdOnly);
                NativeTextureOnDemandCache.SetNextJobRewriteExistingZstd(rewriteExistingZstd);
                if (rewriteExistingZstd)
                {
                    ShowTemporaryStatus("Rewriting existing zstd texture cache...", 2f);
                }

                // Single target: keep single-mode so UI totals are correct.
                if (scenePaths.Count == 1 && packagePaths.Count == 0)
                {
                    NativeTextureOnDemandCache.TryBuildSceneCacheOnDemand(this, scenePaths[0]);
                    return;
                }
                if (packagePaths.Count == 1 && scenePaths.Count == 0)
                {
                    NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(this, packagePaths[0]);
                    return;
                }

                // Multi / mixed selection: batch mode to process each item sequentially.
                StartCoroutine(TboxCacheTexturesMultiBatchCoroutine(scenePaths, packagePaths));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxCacheTexturesSelected error: " + ex);
                ShowTemporaryStatus("Cache textures failed. See log.", 2f);
            }
        }

        private static bool IsCtrlHeldForTextureCacheRewrite()
        {
            try
            {
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCtrlShiftHeldForTextureCachePurge()
        {
            try
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                return ctrl && shift;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerator TboxCacheTexturesBatchCoroutine(List<string> paths)
        {
            NativeTextureOnDemandCache.BeginBatchJob("Caching Textures...", paths.Count);
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (NativeTextureOnDemandCache.CancelRequested) break;
                    string p = paths[i];
                    if (string.IsNullOrEmpty(p)) continue;

                    NativeTextureOnDemandCache.BatchItemStart(p);
                    NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(this, p);
                    while (NativeTextureOnDemandCache.IsOnDemandBusy)
                        yield return null;
                    NativeTextureOnDemandCache.BatchItemDone();
                    yield return null;
                }
            }
            finally
            {
                NativeTextureOnDemandCache.EndBatchJob(
                    NativeTextureOnDemandCache.CancelRequested ? "Texture caching cancelled" : "Texture caching complete");
            }
        }

        private IEnumerator TboxCacheTexturesMultiBatchCoroutine(List<string> scenePaths, List<string> packagePaths)
        {
            int total = 0;
            try { if (scenePaths != null) total += scenePaths.Count; } catch { }
            try { if (packagePaths != null) total += packagePaths.Count; } catch { }
            total = Mathf.Max(1, total);

            NativeTextureOnDemandCache.BeginBatchJob("Caching Textures...", total);
            try
            {
                // Scenes first
                if (scenePaths != null)
                {
                    for (int i = 0; i < scenePaths.Count; i++)
                    {
                        if (NativeTextureOnDemandCache.CancelRequested) break;
                        string p = scenePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.BatchItemStart(p);
                        NativeTextureOnDemandCache.TryBuildSceneCacheOnDemand(this, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy)
                            yield return null;
                        NativeTextureOnDemandCache.BatchItemDone();
                        yield return null;
                    }
                }

                // Packages second
                if (packagePaths != null)
                {
                    for (int i = 0; i < packagePaths.Count; i++)
                    {
                        if (NativeTextureOnDemandCache.CancelRequested) break;
                        string p = packagePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.BatchItemStart(p);
                        NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(this, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy)
                            yield return null;
                        NativeTextureOnDemandCache.BatchItemDone();
                        yield return null;
                    }
                }
            }
            finally
            {
                NativeTextureOnDemandCache.EndBatchJob(
                    NativeTextureOnDemandCache.CancelRequested ? "Texture caching cancelled" : "Texture caching complete");
            }
        }

        private IEnumerator TboxPurgeTexturesBatchCoroutine(List<string> paths)
        {
            NativeTextureOnDemandCache.BeginBatchJob("Purging Texture Cache...", paths.Count);
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (NativeTextureOnDemandCache.CancelRequested) break;
                    string p = paths[i];
                    if (string.IsNullOrEmpty(p)) continue;

                    NativeTextureOnDemandCache.BatchItemStart(p);
                    NativeTextureOnDemandCache.TryPurgePackageCacheOnDemand(this, p);
                    while (NativeTextureOnDemandCache.IsOnDemandBusy)
                        yield return null;
                    NativeTextureOnDemandCache.BatchItemDone();
                    yield return null;
                }
            }
            finally
            {
                NativeTextureOnDemandCache.EndBatchJob(
                    NativeTextureOnDemandCache.CancelRequested ? "Texture purge cancelled" : "Texture purge complete");
            }
        }

        private IEnumerator TboxPurgeTexturesMultiBatchCoroutine(List<string> scenePaths, List<string> packagePaths)
        {
            int total = 0;
            try { if (scenePaths != null) total += scenePaths.Count; } catch { }
            try { if (packagePaths != null) total += packagePaths.Count; } catch { }
            total = Mathf.Max(1, total);

            NativeTextureOnDemandCache.BeginBatchJob("Purging Texture Cache...", total);
            try
            {
                // Scenes first (local-only purge)
                if (scenePaths != null)
                {
                    for (int i = 0; i < scenePaths.Count; i++)
                    {
                        if (NativeTextureOnDemandCache.CancelRequested) break;
                        string p = scenePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.BatchItemStart(p);
                        NativeTextureOnDemandCache.TryPurgeSceneCacheOnDemand(this, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy)
                            yield return null;
                        NativeTextureOnDemandCache.BatchItemDone();
                        yield return null;
                    }
                }

                // Packages second
                if (packagePaths != null)
                {
                    for (int i = 0; i < packagePaths.Count; i++)
                    {
                        if (NativeTextureOnDemandCache.CancelRequested) break;
                        string p = packagePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.BatchItemStart(p);
                        NativeTextureOnDemandCache.TryPurgePackageCacheOnDemand(this, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy)
                            yield return null;
                        NativeTextureOnDemandCache.BatchItemDone();
                        yield return null;
                    }
                }
            }
            finally
            {
                NativeTextureOnDemandCache.EndBatchJob(
                    NativeTextureOnDemandCache.CancelRequested ? "Texture purge cancelled" : "Texture purge complete");
            }
        }

        private void TryCollectUniqueLocalSceneJsonPathsFromSelection(List<string> outPaths)
        {
            outPaths.Clear();
            if (selectedFiles == null || selectedFiles.Count == 0) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;

                if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string rel, false))
                    continue;
                if (string.IsNullOrEmpty(rel)) continue;

                // Use the gallery-relative path (Saves/scene/...) so FileManager.ReadAllText can open it.
                string p = rel.Replace('\\', '/');
                if (!seen.Add(p)) continue;
                outPaths.Add(p);
            }
        }

        private void RefreshAfterTboxPackageFileMoves(HashSet<string> movedPackageUids)
        {
            try { FileManagerBridge.Refresh("tbox_load_unload", RefreshScope.InstallOnly, movedPackageUids); } catch { }
            ResyncTboxSelectionPathsAfterVarMoves();
            try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
            try { RefreshSelectionVisuals(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
        }

        /// <summary>
        /// <see cref="VarPackage.InstallSelf"/> updates package disk path, but gallery <see cref="FileEntry.Path"/> was fixed at ctor.
        /// Refresh selected rows, <see cref="selectedFilePaths"/>, and hover path so UI shows AddonPackages (or AllPackages after unload).
        /// </summary>
        private void ResyncTboxSelectionPathsAfterVarMoves()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;

            var oldPaths = new List<string>(selectedFiles.Count);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                oldPaths.Add(f != null ? (f.Path ?? "") : "");
            }

            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                try
                {
                    if (f is VarFileEntry vfe)
                        vfe.TryRefreshPathsFromLivePackage();
                    else if (f is PackageListEntry ple)
                        ple.RefreshPathsFromPackage();
                    else if (f is SystemFileEntry sfe && sfe.isVar && sfe.package != null)
                        sfe.RefreshVarDisplayPathFromPackage();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] ResyncTboxSelectionPathsAfterVarMoves: " + ex.Message);
                }
            }

            try
            {
                if (!string.IsNullOrEmpty(selectionAnchorPath))
                {
                    for (int i = 0; i < selectedFiles.Count && i < oldPaths.Count; i++)
                    {
                        if (string.Equals(selectionAnchorPath, oldPaths[i], StringComparison.OrdinalIgnoreCase)
                            && selectedFiles[i] != null)
                        {
                            selectionAnchorPath = selectedFiles[i].Path;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (selectedFilePaths != null)
            {
                selectedFilePaths.Clear();
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    FileEntry f = selectedFiles[i];
                    if (f != null && !string.IsNullOrEmpty(f.Path))
                        selectedFilePaths.Add(f.Path);
                }
            }

            try
            {
                if (selectedFiles.Count > 0 && selectedFiles[0] != null)
                    selectedPath = selectedFiles[0].Path;
            }
            catch { }

            try
            {
                if (selectedFiles.Count > 0)
                    SetHoverPath(selectedFiles[0]);
            }
            catch { }
        }

        /// <summary>Unique <see cref="VarPackage"/> instances for selected rows (skips local scene JSON and missing packages).</summary>
        private void TryCollectUniqueVarPackagesFromSelection(List<VarPackage> outList)
        {
            outList.Clear();
            if (selectedFiles == null) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false))
                    continue;

                VarPackage pkg = null;
                if (f is VarFileEntry vfe && vfe.Package != null)
                    pkg = vfe.Package;
                else if (f is PackageListEntry ple && ple.Package != null)
                    pkg = ple.Package;
                else if (f is SystemFileEntry sfe && sfe.isVar && sfe.package != null)
                    pkg = sfe.package;
                else
                {
                    string uid = TryGetPackageUidForEntry(f);
                    if (string.IsNullOrEmpty(uid)) continue;
                    string path = ResolveVarPathForUid(uid);
                    if (string.IsNullOrEmpty(path)) continue;
                    try
                    {
                        var fe = FileManager.GetFileEntry(path, true);
                        if (fe is VarFileEntry vfe2 && vfe2.Package != null)
                            pkg = vfe2.Package;
                        else if (fe is SystemFileEntry sfe2 && sfe2.isVar && sfe2.package != null)
                            pkg = sfe2.package;
                    }
                    catch { }
                }

                if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) continue;
                if (!seen.Add(pkg.Uid)) continue;
                outList.Add(pkg);
            }
        }

        private void TryCollectUniqueVarPackagePathsFromSelection(List<string> outPaths)
        {
            outPaths.Clear();
            var tmp = new List<VarPackage>(8);
            TryCollectUniqueVarPackagesFromSelection(tmp);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tmp.Count; i++)
            {
                VarPackage p = tmp[i];
                if (p == null) continue;
                string path = p.Path;
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(path)) continue;
                outPaths.Add(path.Replace('\\', '/'));
            }
        }
    }
}
