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
                for (int i = 0; i < pkgs.Count; i++)
                {
                    VarPackage p = pkgs[i];
                    if (p == null) continue;
                    try
                    {
                        if (p.InstallSelf()) moved++;
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxLoadSelectedPackages " + p.Uid + ": " + ex.Message);
                    }
                }

                if (moved > 0)
                    RefreshAfterTboxPackageFileMoves();
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
                for (int i = 0; i < pkgs.Count; i++)
                {
                    VarPackage p = pkgs[i];
                    if (p == null) continue;
                    try
                    {
                        if (p.UninstallSelf()) moved++;
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxUnloadSelectedPackages " + p.Uid + ": " + ex.Message);
                    }
                }

                if (moved > 0)
                    RefreshAfterTboxPackageFileMoves();
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
                for (int i = 0; i < pkgs.Count; i++)
                {
                    VarPackage p = pkgs[i];
                    if (p == null) continue;
                    try
                    {
                        if (p.InstallRecursive()) moved++;
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxLoadDepsSelectedPackages " + p.Uid + ": " + ex.Message);
                    }
                }

                if (moved > 0)
                    RefreshAfterTboxPackageFileMoves();
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

                var paths = new List<string>(8);
                TryCollectUniqueVarPackagePathsFromSelection(paths);
                if (paths.Count == 0)
                {
                    ShowTemporaryStatus("No .var paths in selection.", 2f);
                    return;
                }

                try { NativeTextureCacheBuildOverlay.EnsureCreated(); } catch { }
                try { NativeTextureOnDemandCache.DismissSummary(); } catch { }

                if (NativeTextureOnDemandCache.IsOnDemandBusy)
                {
                    ShowTemporaryStatus("Texture caching already running.", 2f);
                    return;
                }

                NativeTextureOnDemandCache.SetNextJobWriteModeOverride(NativeTextureOnDemandCache.CacheWriteMode.ZstdOnly);

                if (paths.Count == 1)
                {
                    NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(this, paths[0]);
                    return;
                }

                StartCoroutine(TboxCacheTexturesBatchCoroutine(paths));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxCacheTexturesSelected error: " + ex);
                ShowTemporaryStatus("Cache textures failed. See log.", 2f);
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

        private void RefreshAfterTboxPackageFileMoves()
        {
            try { MVR.FileManagement.FileManager.Refresh(); } catch { }
            try { FileManager.NotifyInstalled(); } catch { }
            try { FileManager.Refresh(true, false, false); } catch { }
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
                        vfe.RefreshDisplayPathsFromPackage();
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
