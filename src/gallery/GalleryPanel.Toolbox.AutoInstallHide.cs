using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private void TboxAutoInstallSelectedPackages()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int resolvedUids = 0;
                int installOk = 0;
                int loadOk = 0;

                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out _, out bool fiAi, out bool uidAl))
                        continue;
                    if (!seen.Add(uid)) continue;
                    resolvedUids++;
                    if (fiAi && uidAl) continue;

                    if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out string absLocalJson, out _, false))
                    {
                        if (!fiAi)
                        {
                            try
                            {
                                f.SetAutoInstall(true);
                                installOk++;
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError("[VPB] TboxAutoInstallSelectedPackages local scene " + uid + ": " + ex.Message);
                            }
                            try
                            {
                                if (LocalSceneGallerySupport.InstallDependenciesForSceneJsonFile(absLocalJson))
                                {
                                    try { MVR.FileManagement.FileManager.Refresh(); } catch { }
                                    try { FileManager.Refresh(); } catch { }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError("[VPB] TboxAutoInstallSelectedPackages local scene deps " + uid + ": " + ex.Message);
                            }
                        }
                        continue;
                    }

                    string path = ResolveVarPathForUid(uid);
                    if (string.IsNullOrEmpty(path)) continue;

                    try
                    {
                        var fe = FileManager.GetFileEntry(path, true);
                        if (fe is VarFileEntry vfe && !fiAi)
                        {
                            vfe.SetAutoInstall(true);
                            installOk++;
                        }
                        else if (fe is SystemFileEntry sfe && sfe.isVar && !fiAi)
                        {
                            sfe.SetAutoInstall(true);
                            installOk++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxAutoInstallSelectedPackages SetAutoInstall " + uid + ": " + ex.Message);
                    }

                    try
                    {
                        if (!uidAl && AutoLoadPackagesManager.Instance != null)
                        {
                            AutoLoadPackagesManager.Instance.SetAutoLoad(uid, true);
                            loadOk++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxAutoInstallSelectedPackages SetAutoLoad " + uid + ": " + ex.Message);
                    }
                }

                if (resolvedUids == 0)
                {
                    ShowTemporaryStatus("No packages or local scenes in selection.");
                    return;
                }
                if (installOk == 0 && loadOk == 0)
                {
                    ShowTemporaryStatus("Nothing to enable for current selection.", 2f);
                    return;
                }

                // SetAutoInstall no longer moves .var files here; refresh grid so AI badges match the updated lookup.
                try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
                try { RefreshTboxConditionalActionButtons(); } catch { }
                ShowTemporaryStatus($"Autoinstall: {installOk}, auto-load: {loadOk} (install at next launch).", 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxAutoInstallSelectedPackages error: " + ex);
                ShowTemporaryStatus("Autoinstall failed. See log.", 2f);
            }
        }

        private void TboxHideSelectedPackages()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var seenUid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int resolvableUids = 0;
                int ok = 0;
                int failed = 0;

                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out FileEntry fe, out bool hidden, out _, out _))
                        continue;
                    if (!seenUid.Add(uid)) continue;
                    resolvableUids++;
                    if (hidden) continue;

                    try
                    {
                        bool hid;
                        if (LocalSceneGallerySupport.TryResolveSavesSceneJson(fe, out _, out _, false))
                            hid = PackageHidePrefs.TryEnsureLocalSceneJsonHidden(fe);
                        else
                            hid = PackageHidePrefs.TryEnsureVpbPackageHidden(fe);
                        if (hid) ok++;
                        else failed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogUtil.LogError("[VPB] TboxHideSelectedPackages " + uid + ": " + ex.Message);
                    }
                }

                if (resolvableUids == 0)
                {
                    ShowTemporaryStatus("No packages or local scenes in selection.");
                    return;
                }

                if (ok > 0 && !IsHubMode)
                {
                    try { RemoveCurrentGalleryEntriesMatchingHideFilter(); } catch { }
                    // When "show hidden" is on, rows stay in the list so Remove… skips grid refresh; rebind for H badge.
                    bool showHidden = false;
                    try { showHidden = VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; } catch { }
                    if (showHidden)
                    {
                        try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
                        try { RefreshSelectionVisuals(); } catch { }
                    }
                }
                try { RefreshTboxConditionalActionButtons(); } catch { }

                if (ok == 0)
                    ShowTemporaryStatus(failed > 0 ? "Hide failed (see log)." : "Nothing to hide.", 2f);
                else
                    ShowTemporaryStatus($"Hidden {ok}" + (failed > 0 ? $", {failed} skipped." : "."), 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxHideSelectedPackages error: " + ex);
                ShowTemporaryStatus("Hide failed. See log.", 2f);
            }
        }

        /// <summary>
        /// Removes entries excluded by the package-hide filter from the visible list and grid without rescanning.
        /// </summary>
        private void RemoveCurrentGalleryEntriesMatchingHideFilter()
        {
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;

            var removedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = currentFilteredFiles.Count - 1; i >= 0; i--)
            {
                var fe = currentFilteredFiles[i];
                if (fe == null) continue;
                if (!PackageHidePrefs.IsExcludedByGalleryHideFilter(fe)) continue;
                try
                {
                    if (!string.IsNullOrEmpty(fe.Path)) removedPaths.Add(fe.Path);
                }
                catch { }
                currentFilteredFiles.RemoveAt(i);
            }

            if (lastFilteredFiles != null && removedPaths.Count > 0)
            {
                try
                {
                    lastFilteredFiles.RemoveAll(f => f != null && !string.IsNullOrEmpty(f.Path) && removedPaths.Contains(f.Path));
                }
                catch { }
            }

            if (removedPaths.Count == 0) return;

            if (selectedFiles != null)
            {
                try
                {
                    selectedFiles.RemoveAll(f => f != null && removedPaths.Contains(f.Path));
                }
                catch { }
            }

            if (selectedFilePaths != null)
            {
                foreach (var p in removedPaths)
                    selectedFilePaths.Remove(p);
            }

            if (!string.IsNullOrEmpty(selectedPath) && removedPaths.Contains(selectedPath))
            {
                if (selectedFiles != null && selectedFiles.Count > 0)
                {
                    selectedPath = selectedFiles[0].Path;
                    try { SetHoverPath(selectedFiles[0]); } catch { }
                }
                else
                {
                    selectedPath = null;
                    try { SetHoverPath(""); } catch { }
                }
            }

            try
            {
                if (recyclingGrid != null)
                {
                    recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                    recyclingGrid.Refresh();
                }
            }
            catch { }
            try { UpdatePaginationText(); } catch { }
            try { RefreshSelectionVisuals(); } catch { }
        }

        private void TboxUnhideSelectedPackages()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var seenUid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int resolvableUids = 0;
                int ok = 0;
                int failed = 0;

                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out FileEntry fe, out bool hidden, out _, out _))
                        continue;
                    if (!seenUid.Add(uid)) continue;
                    resolvableUids++;
                    if (!hidden) continue;

                    try
                    {
                        bool unhid;
                        if (LocalSceneGallerySupport.TryResolveSavesSceneJson(fe, out _, out _, false))
                            unhid = PackageHidePrefs.TryRemoveLocalSceneJsonHide(fe);
                        else
                            unhid = PackageHidePrefs.TryRemovePackageVarHide(fe);
                        if (unhid) ok++;
                        else failed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogUtil.LogError("[VPB] TboxUnhideSelectedPackages " + uid + ": " + ex.Message);
                    }
                }

                if (resolvableUids == 0)
                {
                    ShowTemporaryStatus("No packages or local scenes in selection.");
                    return;
                }

                try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
                try { RefreshTboxConditionalActionButtons(); } catch { }

                if (ok == 0)
                    ShowTemporaryStatus(failed > 0 ? "Unhide failed (see log)." : "Nothing hidden in selection.", 2f);
                else
                    ShowTemporaryStatus($"Unhid {ok}" + (failed > 0 ? $", {failed} failed." : "."), 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxUnhideSelectedPackages error: " + ex);
                ShowTemporaryStatus("Unhide failed. See log.", 2f);
            }
        }

        private void TboxDisableAutoInstallSelectedPackages()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var seenUid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int resolvableUids = 0;
                int installOk = 0;
                int loadOk = 0;

                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out _, out bool fiAi, out bool uidAl))
                        continue;
                    if (!seenUid.Add(uid)) continue;
                    resolvableUids++;
                    if (!fiAi && !uidAl) continue;

                    if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false))
                    {
                        if (fiAi)
                        {
                            try
                            {
                                f.SetAutoInstall(false);
                                installOk++;
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError("[VPB] TboxDisableAutoInstallSelectedPackages local scene " + uid + ": " + ex.Message);
                            }
                        }
                        continue;
                    }

                    string path = ResolveVarPathForUid(uid);
                    if (string.IsNullOrEmpty(path)) continue;

                    try
                    {
                        var fe = FileManager.GetFileEntry(path, true);
                        if (fe is VarFileEntry vfe)
                        {
                            if (vfe.IsAutoInstall())
                            {
                                vfe.SetAutoInstall(false);
                                installOk++;
                            }
                        }
                        else if (fe is SystemFileEntry sfe && sfe.isVar)
                        {
                            if (sfe.IsAutoInstall())
                            {
                                sfe.SetAutoInstall(false);
                                installOk++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxDisableAutoInstallSelectedPackages SetAutoInstall " + uid + ": " + ex.Message);
                    }

                    try
                    {
                        if (AutoLoadPackagesManager.Instance != null && AutoLoadPackagesManager.Instance.IsAutoLoad(uid))
                        {
                            AutoLoadPackagesManager.Instance.SetAutoLoad(uid, false);
                            loadOk++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] TboxDisableAutoInstallSelectedPackages SetAutoLoad " + uid + ": " + ex.Message);
                    }
                }

                if (resolvableUids == 0)
                {
                    ShowTemporaryStatus("No packages or local scenes in selection.");
                    return;
                }

                try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
                try { RefreshTboxConditionalActionButtons(); } catch { }

                if (installOk == 0 && loadOk == 0)
                    ShowTemporaryStatus("No auto-install or auto-load flags to clear for selection.", 2f);
                else
                    ShowTemporaryStatus($"Cleared autoinstall: {installOk}, auto-load: {loadOk}.", 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxDisableAutoInstallSelectedPackages error: " + ex);
                ShowTemporaryStatus("Clear autoinstall failed. See log.", 2f);
            }
        }

        // ─── Scan Whitelist toolbox actions ───────────────────────────────────────

        private void TboxScanWhitelistAddFolderForSelection()
        {
            TboxScanWhitelistModifyForSelection(addFolder: true);
        }

        private void TboxScanWhitelistRemoveFolderForSelection()
        {
            TboxScanWhitelistModifyForSelection(addFolder: false);
        }

        private void TboxScanWhitelistModifyForSelection(bool addFolder)
        {
            try
            {
                if (!ScanWhitelistManager.Instance.IsEnabled)
                {
                    ShowTemporaryStatus("Scan whitelist is disabled. Enable it in VPB settings first.", 2.5f);
                    return;
                }
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.", 1.5f);
                    return;
                }

                var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int changed = 0;
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out _, out _, out _)) continue;

                    try
                    {
                        var pkg = FileManager.GetPackage(uid, ensureInstalled: false);
                        if (pkg == null) continue;
                        string normPath = (pkg.Path ?? "").Replace('\\', '/');
                        if (!normPath.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) continue;
                        string folder = ScanWhitelistManager.FolderFromVarPath(normPath);
                        if (string.IsNullOrEmpty(folder)) continue;
                        if (!seenFolders.Add(folder)) continue;

                        if (addFolder) { if (ScanWhitelistManager.Instance.AddFolder(folder)) changed++; }
                        else           { if (ScanWhitelistManager.Instance.RemoveFolder(folder)) changed++; }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning("[VPB] ScanWhitelist toolbox error for " + uid + ": " + ex.Message);
                    }
                }

                if (changed > 0)
                {
                    ScanWhitelistManager.Instance.Save();
                    try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
                    try { RefreshTboxConditionalActionButtons(); } catch { }
                    string verb = addFolder ? "Added" : "Removed";
                    ShowTemporaryStatus($"{verb} {changed} folder(s) {(addFolder ? "to" : "from")} scan whitelist.", 2.5f);
                }
                else
                {
                    ShowTemporaryStatus("No folders changed.", 1.5f);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxScanWhitelistModifyForSelection error: " + ex);
                ShowTemporaryStatus("Scan whitelist update failed. See log.", 2f);
            }
        }
    }
}
