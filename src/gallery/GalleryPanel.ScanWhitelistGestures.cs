using System;
using System.Collections.Generic;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        /// <summary>
        /// Desktop grid/list: right-click toggles persistent scan-whitelist UID override.
        /// Ctrl+right-click or middle-click toggles session-only temporary override.
        /// Applies to current selection when the clicked row was already selected, otherwise to the clicked row only.
        /// </summary>
        private void HandleDesktopScanWhitelistClickGesture(FileEntry clickedFile, bool applyToExistingSelection, bool temporary)
        {
            if (clickedFile == null) return;
            if (XrUtils.IsVrActive()) return;

            if (!ScanWhitelistManager.Instance.IsEnabled)
            {
                ShowTemporaryStatus("Scan whitelist is disabled. Enable it in VPB settings first.", 2.5f);
                return;
            }

            var targets = new List<FileEntry>();
            if (applyToExistingSelection && selectedFiles != null && selectedFiles.Count > 0)
            {
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    FileEntry f = selectedFiles[i];
                    if (f != null) targets.Add(f);
                }
            }
            else
            {
                if (selectedFiles != null && selectedFiles.Count > 0)
                    targets.AddRange(selectedFiles);
                else
                    targets.Add(clickedFile);
            }

            if (targets.Count == 0) return;

            if (temporary)
                ApplyDesktopScanWhitelistTemporaryToggle(targets);
            else
                ApplyDesktopScanWhitelistPersistentToggle(targets);
        }

        private void ApplyDesktopScanWhitelistPersistentToggle(List<FileEntry> targets)
        {
            var seenUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            int removed = 0;
            int skippedFolder = 0;
            int skippedLocalScene = 0;
            int unresolved = 0;
            bool dirty = false;

            for (int i = 0; i < targets.Count; i++)
            {
                FileEntry f = targets[i];
                if (f == null) continue;
                if (!TryGetTboxResolvablePackageState(f, out string uid, out FileEntry diskFe, out _, out _, out _, out _))
                {
                    unresolved++;
                    continue;
                }
                if (!seenUids.Add(uid)) continue;
                if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false))
                {
                    skippedLocalScene++;
                    continue;
                }

                string varPath = null;
                try { varPath = diskFe != null ? (diskFe.Path ?? diskFe.Uid) : null; } catch { varPath = null; }

                bool scanExcluded;
                try { scanExcluded = ScanWhitelistManager.Instance.IsPackageScanExcluded(uid, varPath ?? ""); }
                catch { scanExcluded = true; }

                if (scanExcluded)
                {
                    try
                    {
                        if (ScanWhitelistManager.Instance.AddUidOverride(uid))
                        {
                            added++;
                            dirty = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Desktop scan-whitelist add " + uid + ": " + ex.Message);
                    }
                    continue;
                }

                bool wasIncluded = true;
                try
                {
                    if (ScanWhitelistManager.Instance.IsUidOverridePersisted(uid))
                    {
                        if (ScanWhitelistManager.Instance.RemoveUidOverride(uid))
                            dirty = true;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Desktop scan-whitelist remove persist " + uid + ": " + ex.Message);
                }

                try { ScanWhitelistManager.Instance.RemoveTemporaryUidOverrides(new[] { uid }); }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Desktop scan-whitelist remove temp " + uid + ": " + ex.Message);
                }

                bool nowExcluded;
                try { nowExcluded = ScanWhitelistManager.Instance.IsPackageScanExcluded(uid, varPath ?? ""); }
                catch { nowExcluded = false; }

                if (wasIncluded && nowExcluded)
                    removed++;
                else if (!nowExcluded)
                    skippedFolder++;
            }

            if (dirty)
            {
                try { ScanWhitelistManager.Instance.Save(); } catch { }
            }

            if (added == 0 && removed == 0 && skippedFolder == 0 && skippedLocalScene == 0 && unresolved == 0)
            {
                ShowTemporaryStatus("Nothing to change for scan whitelist.", 2f);
                return;
            }

            try { BeginScanWlBadgePulseFromFiles(targets); } catch { }
            try { RefreshVisibleGridVisualsOnly(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
            try { DetailStripRefreshBadgesForSelection(); } catch { }

            if (added > 0 || removed > 0)
            {
                string msg = "Scan whitelist: ";
                if (added > 0) msg += added + " added";
                if (added > 0 && removed > 0) msg += ", ";
                if (removed > 0) msg += removed + " removed";
                if (skippedFolder > 0) msg += " (" + skippedFolder + " still whitelisted via folder)";
                ShowTemporaryStatus(msg + ".", 2.5f);
            }
            else if (skippedFolder > 0)
                ShowTemporaryStatus(skippedFolder + " package(s) still whitelisted via folder path.", 2.5f);
            else if (skippedLocalScene > 0)
                ShowTemporaryStatus("Local scenes are not scan-whitelisted.", 2f);
            else if (unresolved > 0)
                ShowTemporaryStatus("No packages to change in selection.", 2f);
        }

        private void ApplyDesktopScanWhitelistTemporaryToggle(List<FileEntry> targets)
        {
            var seenUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addUids = new List<string>();
            var removeUids = new List<string>();
            var selectedRootUids = new List<string>();
            int skippedLocalScene = 0;
            int skippedAlreadyIncluded = 0;
            int unresolved = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                FileEntry f = targets[i];
                if (f == null) continue;
                if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out _, out _, out _, out _))
                {
                    unresolved++;
                    continue;
                }
                if (!seenUids.Add(uid)) continue;
                if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false))
                {
                    skippedLocalScene++;
                    continue;
                }

                if (ScanWhitelistManager.Instance.IsUidTemporaryOverrideOnly(uid))
                {
                    removeUids.Add(uid);
                    continue;
                }

                if (ScanWhitelistManager.Instance.IsUidOverrideIncluded(uid))
                {
                    skippedAlreadyIncluded++;
                    continue;
                }

                selectedRootUids.Add(uid);
                addUids.Add(uid);
            }

            // Session-only deps for newly temp-whitelisted roots (matches selection toolbox).
            var depCandidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedRootUids.Count; i++)
            {
                string uid = selectedRootUids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                try
                {
                    var deps = FileManager.GetDependenciesDeep(uid, 2);
                    if (deps == null || deps.Count == 0) continue;
                    foreach (var depId in deps)
                    {
                        if (string.IsNullOrEmpty(depId)) continue;
                        depCandidateIds.Add(depId);
                    }
                }
                catch { }
            }
            foreach (var depId in depCandidateIds)
            {
                if (string.IsNullOrEmpty(depId)) continue;
                if (ScanWhitelistManager.Instance.IsUidOverrideIncluded(depId)) continue;
                if (!seenUids.Add(depId)) continue;
                addUids.Add(depId);
            }

            int removedCount = 0;
            if (removeUids.Count > 0)
            {
                try
                {
                    ScanWhitelistManager.Instance.RemoveTemporaryUidOverrides(removeUids);
                    removedCount = removeUids.Count;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Desktop temp scan-whitelist remove: " + ex.Message);
                }
            }

            int addedCount = 0;
            if (addUids.Count > 0)
            {
                try
                {
                    List<string> added = ScanWhitelistManager.Instance.AddTemporaryUidOverrides(addUids);
                    addedCount = added != null ? added.Count : 0;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Desktop temp scan-whitelist add: " + ex.Message);
                }
            }

            if (addedCount == 0 && removedCount == 0)
            {
                if (skippedAlreadyIncluded > 0)
                    ShowTemporaryStatus("Selection already whitelisted (persistent or folder).", 2f);
                else if (skippedLocalScene > 0)
                    ShowTemporaryStatus("Local scenes are not scan-whitelisted.", 2f);
                else if (unresolved > 0)
                    ShowTemporaryStatus("No packages to change in selection.", 2f);
                else
                    ShowTemporaryStatus("Nothing to change for temporary scan whitelist.", 2f);
                return;
            }

            try
            {
                var pulse = new List<string>(addUids.Count + removeUids.Count);
                pulse.AddRange(addUids);
                pulse.AddRange(removeUids);
                BeginScanWlBadgePulse(pulse);
            }
            catch { }
            try { RefreshVisibleGridVisualsOnly(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
            try { DetailStripRefreshBadgesForSelection(); } catch { }

            if (addedCount > 0 && removedCount > 0)
                ShowTemporaryStatus("Temp scan whitelist: " + addedCount + " added, " + removedCount + " removed (session).", 2.5f);
            else if (addedCount > 0)
                ShowTemporaryStatus("Temporarily whitelisted " + addedCount + " package(s) for this session.", 2.5f);
            else
                ShowTemporaryStatus("Removed " + removedCount + " package(s) from temporary scan whitelist.", 2.5f);
        }
    }
}
