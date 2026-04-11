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

                var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    string uid = TryGetPackageUidForEntry(f);
                    if (!string.IsNullOrEmpty(uid)) uids.Add(uid);
                }

                if (uids.Count == 0)
                {
                    ShowTemporaryStatus("No packages found in selection.");
                    return;
                }

                int installOk = 0;
                int loadOk = 0;
                foreach (var uid in uids)
                {
                    if (string.IsNullOrEmpty(uid)) continue;

                    string path = ResolveVarPathForUid(uid);
                    if (string.IsNullOrEmpty(path)) continue;

                    try
                    {
                        var fe = FileManager.GetFileEntry(path, true);
                        if (fe is VarFileEntry vfe)
                        {
                            vfe.SetAutoInstall(true);
                            installOk++;
                        }
                        else if (fe is SystemFileEntry sfe && sfe.isVar)
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
                        if (AutoLoadPackagesManager.Instance != null)
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

                // SetAutoInstall no longer moves .var files here; refresh grid so AI badges match the updated lookup.
                try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }

                if (installOk == 0 && loadOk == 0)
                    ShowTemporaryStatus("Could not update packages (not found on disk?).", 2.5f);
                else
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

                var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    string uid = TryGetPackageUidForEntry(f);
                    if (!string.IsNullOrEmpty(uid)) uids.Add(uid);
                }

                if (uids.Count == 0)
                {
                    ShowTemporaryStatus("No packages found in selection.");
                    return;
                }

                int ok = 0;
                int failed = 0;

                foreach (var uid in uids)
                {
                    if (string.IsNullOrEmpty(uid)) continue;

                    string path = ResolveVarPathForUid(uid);
                    if (string.IsNullOrEmpty(path))
                    {
                        failed++;
                        continue;
                    }

                    try
                    {
                        var fe = FileManager.GetFileEntry(path, true);
                        if (fe == null)
                        {
                            failed++;
                            continue;
                        }
                        if (PackageHidePrefs.TryEnsureVpbPackageHidden(fe)) ok++;
                        else failed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogUtil.LogError("[VPB] TboxHideSelectedPackages " + uid + ": " + ex.Message);
                    }
                }

                try
                {
                    try { FileManager.Refresh(); } catch { }
                    try { if (MVR.FileManagement.FileManager.singleton != null) MVR.FileManagement.FileManager.Refresh(); } catch { }
                }
                catch { }

                try { RefreshFiles(true); } catch { }

                if (ok == 0)
                    ShowTemporaryStatus(failed > 0 ? "Hide failed (see log)." : "Nothing to hide.", 2f);
                else
                    ShowTemporaryStatus($"Hidden {ok} package(s)" + (failed > 0 ? $", {failed} skipped." : "."), 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxHideSelectedPackages error: " + ex);
                ShowTemporaryStatus("Hide failed. See log.", 2f);
            }
        }
    }
}
