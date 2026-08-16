using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
{
        private static readonly List<string> ClothingSlotPickerOptions = new List<string>()
        {
            "arms",
            "feet",
            "full body",
            "hands",
            "head",
            "hip",
            "legs",
            "neck",
            "torso",
            "accessory",
            "bodysuit",
            "bottom",
            "bra",
            "dress",
            "glasses",
            "gloves",
            "hat",
            "jacket",
            "jewelry",
            "mask",
            "panties",
            "pants",
            "shirt",
            "shoes",
            "shorts",
            "skirt",
            "socks",
            "stockings",
            "sweater",
            "top",
            "trunks",
            "underwear",
            "vest",
        };

        private struct SideButtonLayoutEntry
        {
            public int buttonIndex;
            public int row;
            public int gapTier;

            public SideButtonLayoutEntry(int buttonIndex, int row, int gapTier)
            {
                this.buttonIndex = buttonIndex;
                this.row = row;
                this.gapTier = gapTier;
            }
        }

        private class SaveMenuOption
        {
            public string Label;
            public string Tooltip;
            public Action Action;
            public bool Enabled;
            public bool AutoClose = true;
        }

        private void SetSaveSubmenuButtonsVisible(bool visible)
        {
            // Removed - submenus are now handled by side tabs
        }

        private void CloseAtomSubmenuUI()
        {
            try
            {
                if (leftActiveContent == ContentType.RemoveAtom) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.RemoveAtom) rightActiveContent = rightPrevActiveContent;
                UpdateTabs();
            }
            catch { }
        }

        private void CloseOtherSubmenus(string keep)
        {
            if (!string.Equals(keep, "Save", StringComparison.OrdinalIgnoreCase) && saveSubmenuOpen)
            {
                CloseSaveSubmenuUI();
            }
            if (!string.Equals(keep, "Clothing", StringComparison.OrdinalIgnoreCase) && clothingSubmenuOpen)
            {
                CloseClothingSubmenuUI();
            }
            if (!string.Equals(keep, "Hair", StringComparison.OrdinalIgnoreCase) && hairSubmenuOpen)
            {
                CloseHairSubmenuUI();
            }
            if (!string.Equals(keep, "Atom", StringComparison.OrdinalIgnoreCase) && atomSubmenuOpen)
            {
                CloseAtomSubmenuUI();
            }
        }

        private List<SaveMenuOption> BuildSaveMenuOptions()
        {
            var options = new List<SaveMenuOption>();

            Atom target = GetBestTargetAtom();
            bool hasTarget = target != null && SceneUtils.IsPersonLikeAtom(target);
            string targetUid = target != null ? target.uid : "None";

            void AddPresetOption(string label, string storableId)
            {
                string baseName = label.Replace(" Preset...", "").Replace("...", "");
                options.Add(new SaveMenuOption
                {
                    Label = label,
                    Tooltip = hasTarget ? ("Save: " + baseName + " from " + targetUid) : ("Save: " + baseName),
                    Enabled = hasTarget,
                    Action = () => SavePresetFromStorable(target, storableId)
                });
            }

            bool canOverwriteScene = SuperController.singleton != null && TryGetSceneOverwriteSaveContext(out _);
            options.Add(new SaveMenuOption
            {
                Label = VPBTranslation.T("gallery.save.overwrite_scene", "Overwrite Save..."),
                Tooltip = VPBTranslation.T(
                    "gallery.save.overwrite_scene_tooltip",
                    "Save scene using the selected gallery scene name."),
                Enabled = canOverwriteScene,
                Action = () => OverwriteSaveSceneFromGallery()
            });

            // Scene and core presets at the bottom
            options.Add(new SaveMenuOption
            {
                Label = VPBTranslation.T("gallery.save.scene", "Scene..."),
                Tooltip = VPBTranslation.T("gallery.save.scene_tooltip", "Save current scene to file."),
                Enabled = SuperController.singleton != null,
                Action = () => SaveSceneFromGallery()
            });
            AddPresetOption(VPBTranslation.T("gallery.save.appearance",  "Appearance Preset..."),  "AppearancePresets");
            AddPresetOption(VPBTranslation.T("gallery.save.clothing",    "Clothing Preset..."),    "ClothingPresets");
            AddPresetOption(VPBTranslation.T("gallery.save.hair",        "Hair Preset..."),        "HairPresets");
            AddPresetOption(VPBTranslation.T("gallery.save.pose",        "Pose Preset..."),        "PosePresets");

            // Secondary presets above the core ones
            AddPresetOption(VPBTranslation.T("gallery.save.glute_phys",  "Glute Phys Preset..."),  "FemaleGlutePhysicsPresets");
            AddPresetOption(VPBTranslation.T("gallery.save.breast_phys", "Breast Phys Preset..."), "FemaleBreastPhysicsPresets");
            AddPresetOption(VPBTranslation.T("gallery.save.plugin",      "Plugin Preset..."),      "PluginPresets");
            AddPresetOption(VPBTranslation.T("gallery.save.animation",   "Animation Preset..."),   "AnimationPresets");
            AddPresetOption(VPBTranslation.T("gallery.save.general",     "General Preset..."),     "Preset");
            AddPresetOption(VPBTranslation.T("gallery.save.morph",       "Morph Preset..."),       "MorphPresets");
            AddPresetOption(VPBTranslation.T("gallery.save.skin",        "Skin Preset..."),        "SkinPresets");

            return options;
        }

        private void PopulateSaveSubmenuButtons()
        {
            // Removed - submenus are now handled by side tabs
        }

        private bool IsSubmenuContentType(ContentType? type)
        {
            if (!type.HasValue) return false;
            return type == ContentType.SavePresets || 
                   type == ContentType.RemoveClothing || 
                   type == ContentType.RemoveHair || 
                   type == ContentType.RemoveAtom ||
                   type == ContentType.Target;
        }

        private void CloseOtherSideIfSubmenu(bool currentlyOpeningLeft)
        {
            if (currentlyOpeningLeft)
            {
                if (IsSubmenuContentType(rightActiveContent))
                {
                    rightActiveContent = rightPrevActiveContent;
                }
            }
            else
            {
                if (IsSubmenuContentType(leftActiveContent))
                {
                    leftActiveContent = leftPrevActiveContent;
                }
            }
        }

        private void ToggleSaveSubmenuFromSideButtons(bool? forceLeftSide = null)
        {
            bool useLeftSide = forceLeftSide ?? isFixedLocally;
            // Unified save UX (issue #62): open the floating Save popup (same as the quick-menu
            // assignable Save button) instead of the SavePresets side tab.
            ToggleSaveMenuPopup(useLeftSide);
        }

        private void CloseSaveSubmenuUI()
        {
            try
            {
                if (leftActiveContent == ContentType.SavePresets) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.SavePresets) rightActiveContent = rightPrevActiveContent;
                if (leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags)
                    ForceCloseSettingsSidePanels();
                UpdateTabs();
            }
            catch { }
        }

        private void BeginSaveMode()
        {
            try { VpbSaveCacheSupport.RegisterPluginSaveWritePathsNoConfirm(); } catch { }
            if (_canvasesHiddenForSave != null) return; // already in save mode
            _canvasesHiddenForSave = new List<Canvas>();
            _panelsHiddenForSave = new List<GalleryPanel>();
            if (Gallery.singleton != null)
            {
                foreach (var p in Gallery.singleton.Panels)
                {
                    if (p != null && p.IsVisible)
                    {
                        _panelsHiddenForSave.Add(p);
                        p.Hide();
                        _canvasesHiddenForSave.Add(p.canvas);
                    }
                }
            }
        }

        private void EndSaveMode()
        {
            if (_panelsHiddenForSave != null && _panelsHiddenForSave.Count > 0)
            {
                for (int i = 0; i < _panelsHiddenForSave.Count; i++)
                {
                    var p = _panelsHiddenForSave[i];
                    if (p == null) continue;
                    try
                    {
                        string t = p.GetTitle();
                        string ext = p.GetCurrentExtension();
                        string curPath = p.GetCurrentPath();
                        if (!string.IsNullOrEmpty(t) && !string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(curPath))
                        {
                            p.Show(t, ext, curPath);
                        }
                        else if (Gallery.singleton != null)
                        {
                            // Fallback for partially initialized panels.
                            var cats = p.categories;
                            if (cats != null && cats.Count > 0)
                            {
                                var c0 = cats[0];
                                p.Show(c0.name, c0.extension, c0.path);
                            }
                        }
                    }
                    catch { }
                }
            }
            _panelsHiddenForSave = null;
            _canvasesHiddenForSave = null;
        }

        private void SaveSceneFinal(string path, bool overwriteConfirmed)
        {
            path = NormalizeSceneSavePath(path);
            try { VpbSaveCacheSupport.RegisterPluginSaveWritePathForFile(path); } catch { }
            bool saveInvoked = false;
            try
            {
                saveInvoked = TryInvokeSceneSave(path, overwriteConfirmed);
                if (saveInvoked)
                {
                    ShowTemporaryStatus("Scene saved: " + path, 2f);
                }
                else
                {
                    ShowTemporaryStatus("Save cancelled or failed.");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Save Scene failed: " + ex);
                ShowTemporaryStatus("Save failed. See log.");
            }

            if (!saveInvoked)
            {
                EndSaveMode();
                return;
            }

            if (_sceneSaveFinalizeCoroutine != null)
            {
                StopCoroutine(_sceneSaveFinalizeCoroutine);
                _sceneSaveFinalizeCoroutine = null;
            }
            _sceneSaveSawScreenshotCamera = false;
            _sceneSaveRehideApplied = false;
            _sceneSaveFinalizeCoroutine = StartCoroutine(FinalizeSceneSaveModeCoroutine(path));
        }

        private IEnumerator FinalizeSceneSaveModeCoroutine(string path)
        {
            // Capture the sidecar screenshot's on-disk timestamp before VaM writes the new one,
            // so we can detect when the fresh .jpg actually lands (see invalidation step below).
            string screenshotFull = TryGetSceneScreenshotFullPath(path);
            long screenshotBaselineMtime = GetSceneScreenshotMtimeTicks(screenshotFull);

            float waitStart = Time.unscaledTime;
            const float waitForScreenshotStartMax = 12f;
            const float waitForScreenshotFinishMax = 45f;

            // Let Save() kick off any async UI/camera flow first.
            yield return null;

            while (true)
            {
                bool screenshotActive = IsScreenshotCaptureActive();
                if (screenshotActive)
                {
                    _sceneSaveSawScreenshotCamera = true;
                    if (!_sceneSaveRehideApplied)
                    {
                        HidePanelsForSaveTracking();
                        _sceneSaveRehideApplied = true;
                    }
                }

                if (_sceneSaveSawScreenshotCamera)
                {
                    if (!screenshotActive) break; // screenshot flow completed
                    if (Time.unscaledTime - waitStart > waitForScreenshotFinishMax) break; // safety timeout
                }
                else
                {
                    // No screenshot started shortly after save: complete immediately.
                    if (Time.unscaledTime - waitStart > waitForScreenshotStartMax) break;
                }

                yield return null;
            }

            _sceneSaveFinalizeCoroutine = null;

            // VaM writes the scene screenshot (.jpg) asynchronously, often a moment after the
            // screenshot camera disables. Invalidating/reloading too early re-decodes the OLD
            // image bytes and re-caches them, leaving a stale gallery thumbnail after an overwrite
            // (issue #44). When a screenshot was captured, wait until the sidecar .jpg actually
            // changes on disk before invalidating; otherwise fall back to a brief fixed delay.
            if (_sceneSaveSawScreenshotCamera)
                yield return WaitForSceneScreenshotWrittenCoroutine(screenshotFull, screenshotBaselineMtime);
            else
                yield return new WaitForSecondsRealtime(0.2f);

            // Restore gallery visibility first — RefreshVisibleGridVisualsOnly no-ops while hidden.
            EndSaveMode();
            InvalidateSceneSaveGalleryCaches(path);

            // The displayed list still holds the pre-save FileEntry with a stale mtime, so Date
            // modified/updated sorts don't float the just-saved scene to the top (issue #45).
            // A full RefreshFiles doesn't help: the list is usually served straight from
            // GalleryFileListSnapshotCache (which also skips the re-sort), so it returns the same
            // stale order. Instead refresh the live entry's mtime from disk and re-sort in place.
            // The snapshot cache shares these FileEntry references, so it stays coherent, and the
            // SQLite loose-file cache self-heals on the next refresh (its signature keys off the
            // directory mtime, which the save just bumped).
            if (CurrentViewListsLocalScenes())
            {
                try { RefreshSavedLocalSceneEntryAndResort(path); } catch { }
            }
        }

        /// <summary>
        /// Issue #45: after an overwrite-save, update the displayed loose-scene entry's on-disk
        /// timestamp and re-sort the current view in place so Date Updated/modified sorts reflect
        /// the new save time without a full (cache-served, unsorted) rescan.
        /// </summary>
        private void RefreshSavedLocalSceneEntryAndResort(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath) || currentFilteredFiles == null) return;

            string norm = FileManager.NormalizePath(scenePath).Replace('\\', '/');
            SystemFileEntry match = null;
            for (int i = 0; i < currentFilteredFiles.Count; i++)
            {
                var sfe = currentFilteredFiles[i] as SystemFileEntry;
                if (sfe == null || string.IsNullOrEmpty(sfe.Path)) continue;
                if (string.Equals(sfe.Path, norm, StringComparison.OrdinalIgnoreCase))
                {
                    match = sfe;
                    break;
                }
            }
            if (match == null) return;

            match.RefreshLastWriteTimeFromDisk();

            try
            {
                var st = GetSortState("Files");
                if (st != null)
                {
                    ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);
                    if (activeContentType != ContentType.History)
                        GallerySortManager.Instance.SortFiles(currentFilteredFiles, st);
                }
            }
            catch { }

            RefreshRecycleGridAfterFilterChange();
        }

        private bool CurrentViewListsLocalScenes()
        {
            string title = currentCategoryTitle;
            if (string.IsNullOrEmpty(title) && titleText != null) title = titleText.text;
            if (string.IsNullOrEmpty(title)) return false;
            return title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TryGetSceneScreenshotFullPath(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return null;
            try
            {
                string jpg = Path.ChangeExtension(scenePath.Replace('\\', '/'), ".jpg");
                string full = FileManager.GetFullPath(jpg);
                return string.IsNullOrEmpty(full) ? jpg : full;
            }
            catch { return null; }
        }

        private static long GetSceneScreenshotMtimeTicks(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return 0;
            try
            {
                if (File.Exists(fullPath)) return File.GetLastWriteTimeUtc(fullPath).ToFileTimeUtc();
            }
            catch { }
            return 0;
        }

        private IEnumerator WaitForSceneScreenshotWrittenCoroutine(string jpgFullPath, long baselineMtime)
        {
            if (string.IsNullOrEmpty(jpgFullPath))
            {
                yield return new WaitForSecondsRealtime(0.2f);
                yield break;
            }

            const float maxWait = 5f;
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < maxWait)
            {
                long now = GetSceneScreenshotMtimeTicks(jpgFullPath);
                if (now > 0 && now > baselineMtime)
                {
                    // Timestamp advanced; allow a brief moment for the file body to finish flushing.
                    yield return new WaitForSecondsRealtime(0.15f);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }

        private void InvalidateSceneSaveGalleryCaches(string scenePath)
        {
            VpbSaveCacheSupport.NotifyGalleryPanelsInvalidateAfterSave(scenePath);
        }

        private void HidePanelsForSaveTracking()
        {
            if (_panelsHiddenForSave == null || _panelsHiddenForSave.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _panelsHiddenForSave.Count; i++)
            {
                var p = _panelsHiddenForSave[i];
                if (p == null) continue;
                try
                {
                    if (p.IsVisible)
                    {
                        p.Hide();
                    }
                }
                catch { }
            }
        }

        private static bool IsScreenshotCaptureActive()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            try
            {
                bool normal = sc.screenshotCamera != null && sc.screenshotCamera.enabled;
                bool hiRes = sc.hiResScreenshotCamera != null && sc.hiResScreenshotCamera.enabled;
                return normal || hiRes;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeSceneSavePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            return path.Replace('\\', '/');
        }

        private bool TryInvokeSceneSave(string path, bool overwriteConfirmed)
        {
            bool logPerf = Settings.Instance != null && Settings.Instance.LogSavePerf != null && Settings.Instance.LogSavePerf.Value;
            System.Diagnostics.Stopwatch swTotal = logPerf ? System.Diagnostics.Stopwatch.StartNew() : null;
            System.Diagnostics.Stopwatch swInner = logPerf ? new System.Diagnostics.Stopwatch() : null;
            string takenPath = null;
            bool savedOk = false;
            try
            {
                Exception signedSaveError;
                if (logPerf) { swInner.Reset(); swInner.Start(); }
                bool bridgeOk = PluginSignatureSaveBridge.TrySaveScene(path, out signedSaveError);
                if (logPerf) swInner.Stop();
                if (bridgeOk)
                {
                    takenPath = "Bridge";
                    savedOk = true;
                    return true;
                }
                if (signedSaveError != null)
                {
                    LogUtil.LogWarning("[VPB] Signed scene save bridge failed, using fallback save invocation: " + signedSaveError.Message);
                }

                object result;

                // Mirror BA behavior first: direct Save(path) tends to preserve native scene screenshot flow.
                if (TryReflectionSave("Save", new object[] { path }, logPerf, swInner, out result)) { takenPath = "Save(path)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("SaveScene", new object[] { path }, logPerf, swInner, out result)) { takenPath = "SaveScene(path)"; savedOk = InterpretSaveResult(result); return savedOk; }

                // Then try richer signatures in case this VaM build exposes them.
                if (TryReflectionSave("SaveSceneWithScreenshot", new object[] { path, overwriteConfirmed }, logPerf, swInner, out result)) { takenPath = "SaveSceneWithScreenshot(path,ow)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("SaveWithScreenshot", new object[] { path, overwriteConfirmed }, logPerf, swInner, out result)) { takenPath = "SaveWithScreenshot(path,ow)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("SaveSceneWithScreenshot", new object[] { path }, logPerf, swInner, out result)) { takenPath = "SaveSceneWithScreenshot(path)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("SaveWithScreenshot", new object[] { path }, logPerf, swInner, out result)) { takenPath = "SaveWithScreenshot(path)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("Save", new object[] { path, overwriteConfirmed, true }, logPerf, swInner, out result)) { takenPath = "Save(path,ow,true)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("SaveScene", new object[] { path, overwriteConfirmed, true }, logPerf, swInner, out result)) { takenPath = "SaveScene(path,ow,true)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("Save", new object[] { path, overwriteConfirmed }, logPerf, swInner, out result)) { takenPath = "Save(path,ow)"; savedOk = InterpretSaveResult(result); return savedOk; }
                if (TryReflectionSave("SaveScene", new object[] { path, overwriteConfirmed }, logPerf, swInner, out result)) { takenPath = "SaveScene(path,ow)"; savedOk = InterpretSaveResult(result); return savedOk; }

                takenPath = "none";
                return false;
            }
            finally
            {
                if (logPerf && swTotal != null)
                {
                    swTotal.Stop();
                    long totalMs = swTotal.ElapsedMilliseconds;
                    long innerMs = swInner != null ? swInner.ElapsedMilliseconds : -1;
                    long preambleMs = totalMs - (innerMs >= 0 ? innerMs : 0);
                    LogUtil.LogWarning("[VPB][SavePerf] path=" + (takenPath ?? "none") + " ok=" + savedOk + " totalMs=" + totalMs + " innerInvokeMs=" + innerMs + " preambleMs=" + preambleMs);
                }
            }
        }

        // Wrapper to time only the reflected native-save invocation (not the parameter-shape probing).
        private bool TryReflectionSave(string methodName, object[] args, bool logPerf, System.Diagnostics.Stopwatch swInner, out object result)
        {
            if (logPerf && swInner != null) { swInner.Reset(); swInner.Start(); }
            bool ok = TryInvokeSaveMethod(SuperController.singleton, methodName, args, out result);
            if (logPerf && swInner != null) swInner.Stop();
            return ok;
        }

        private static bool TryInvokeSaveMethod(object target, string methodName, object[] args, out object result)
        {
            result = null;
            if (target == null) return false;

            Type t = target.GetType();
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal)) continue;
                ParameterInfo[] ps = candidate.GetParameters();
                if (args.Length > ps.Length) continue;

                object[] invokeArgs = new object[ps.Length];
                bool signatureMatch = true;
                for (int p = 0; p < ps.Length; p++)
                {
                    bool hasProvidedArg = p < args.Length;
                    object arg = hasProvidedArg ? args[p] : Type.Missing;
                    Type pt = ps[p].ParameterType;

                    if (!hasProvidedArg)
                    {
                        if (!ps[p].IsOptional)
                        {
                            signatureMatch = false;
                            break;
                        }
                        invokeArgs[p] = Type.Missing;
                        continue;
                    }

                    if (arg == null)
                    {
                        if (pt.IsValueType && Nullable.GetUnderlyingType(pt) == null)
                        {
                            signatureMatch = false;
                            break;
                        }
                        invokeArgs[p] = null;
                        continue;
                    }

                    if (pt.IsInstanceOfType(arg))
                    {
                        invokeArgs[p] = arg;
                        continue;
                    }

                    try
                    {
                        Type targetType = Nullable.GetUnderlyingType(pt) ?? pt;
                        object converted = Convert.ChangeType(arg, targetType, System.Globalization.CultureInfo.InvariantCulture);
                        invokeArgs[p] = converted;
                    }
                    catch
                    {
                        signatureMatch = false;
                        break;
                    }
                }

                if (!signatureMatch) continue;
                try
                {
                    result = candidate.Invoke(target, invokeArgs);
                    return true;
                }
                catch
                {
                    // Try next overload
                }
            }
            return false;
        }

        private static bool InterpretSaveResult(object invokeResult)
        {
            if (invokeResult == null) return true; // void-returning save APIs
            if (invokeResult is bool b) return b;
            return true;
        }

        private void SavePresetFromStorable(Atom target, string storableId)
        {
            if (target == null)
            {
                ShowTemporaryStatus("Select a Person atom to save presets.");
                return;
            }
            string rootFolder;
            if (!TryGetPresetSaveRootFolder(storableId, out rootFolder))
            {
                ShowTemporaryStatus("Preset not available: " + storableId);
                return;
            }

            BeginSaveMode();

            string defaultName = GetDefaultPresetSaveName(target, storableId, rootFolder);
            if (SuperController.singleton.mainHUD != null && !SuperController.singleton.mainHUD.gameObject.activeSelf)
                SuperController.singleton.ShowMainHUDMonitor();
            SuperController.singleton.GetMediaPathDialog((selectedPath) =>
            {
                if (string.IsNullOrEmpty(selectedPath))
                {
                    EndSaveMode();
                    return;
                }
                SavePresetFileSelected(target, storableId, rootFolder, selectedPath, true);
            }, "vap", rootFolder, false, true, false, "Preset_", true);

            try
            {
                if (SuperController.singleton.mediaFileBrowserUI != null)
                {
                    SuperController.singleton.mediaFileBrowserUI.SetTextEntry(true);
                    if (SuperController.singleton.mediaFileBrowserUI.fileEntryField != null)
                    {
                        SuperController.singleton.mediaFileBrowserUI.fileEntryField.text = defaultName ?? string.Empty;
                        SuperController.singleton.mediaFileBrowserUI.ActivateFileNameField();
                    }
                }
            }
            catch { }
        }

        private bool TryGetPresetSaveRootFolder(string storableId, out string rootFolder)
        {
            rootFolder = null;
            if (string.IsNullOrEmpty(storableId)) return false;

            switch (storableId)
            {
                case "AppearancePresets":
                    rootFolder = "Custom\\Atom\\Person\\Appearance";
                    break;
                case "PosePresets":
                    rootFolder = "Custom\\Atom\\Person\\Pose";
                    break;
                case "ClothingPresets":
                    rootFolder = "Custom\\Atom\\Person\\Clothing";
                    break;
                case "HairPresets":
                    rootFolder = "Custom\\Atom\\Person\\Hair";
                    break;
                case "SkinPresets":
                    rootFolder = "Custom\\Atom\\Person\\Skin";
                    break;
                case "MorphPresets":
                    rootFolder = "Custom\\Atom\\Person\\Morphs";
                    break;
                case "Preset":
                    rootFolder = "Custom\\Atom\\Person\\General";
                    break;
                case "AnimationPresets":
                    rootFolder = "Custom\\Atom\\Person\\AnimationPresets";
                    break;
                case "PluginPresets":
                    rootFolder = "Custom\\Atom\\Person\\Plugins";
                    break;
                case "FemaleBreastPhysicsPresets":
                    rootFolder = "Custom\\Atom\\Person\\BreastPhysics";
                    break;
                case "FemaleGlutePhysicsPresets":
                    rootFolder = "Custom\\Atom\\Person\\GlutePhysics";
                    break;
            }

            return !string.IsNullOrEmpty(rootFolder);
        }

        private string GetDefaultPresetSaveName(Atom target, string storableId, string rootFolder)
        {
            string personName = TryGetPersonAtomPresetBaseName(target);
            if (string.Equals(storableId, "AppearancePresets", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(personName))
            {
                return personName;
            }

            try
            {
                JSONStorable storable = target != null ? target.GetStorableByID(storableId) : null;
                if (storable != null)
                {
                    JSONStorableString presetName = null;
                    try { presetName = storable.GetStringJSONParam("presetName"); } catch { presetName = null; }
                    if (presetName != null && !string.IsNullOrEmpty(presetName.val))
                    {
                        try
                        {
                            string currentPresetName = NormalizePresetSaveBaseName(
                                MVR.FileManagementSecure.FileManagerSecure.GetFileName(presetName.val));
                            if (!string.IsNullOrEmpty(currentPresetName))
                            {
                                return currentPresetName;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(personName))
                return personName;

            return "preset_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        private static string TryGetPersonAtomPresetBaseName(Atom target)
        {
            if (target == null) return null;
            try
            {
                if (!string.Equals(target.type, "Person", StringComparison.Ordinal))
                    return null;
            }
            catch { return null; }

            string name = null;
            try { name = target.name; } catch { name = null; }
            if (string.IsNullOrEmpty(name))
            {
                try { name = target.uid; } catch { name = null; }
            }
            return NormalizePresetSaveBaseName(name);
        }

        private static string NormalizePresetSaveBaseName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string name = raw.Trim();
            if (name.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            if (name.StartsWith("Preset_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("Preset_".Length);
            name = TextureUtil.SanitizeFileName(name);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        private void SavePresetFileSelected(Atom target, string storableId, string rootFolder, string fileNamePath, bool useScreenshot)
        {
            if (string.IsNullOrEmpty(fileNamePath)) { EndSaveMode(); return; }
            if (string.IsNullOrEmpty(rootFolder)) { EndSaveMode(); return; }

            if (!fileNamePath.StartsWith(rootFolder, StringComparison.OrdinalIgnoreCase))
            {
                ShowTemporaryStatus("Preset must be saved under: " + rootFolder, 2f);
                EndSaveMode();
                return;
            }

            string path = fileNamePath + ".vap";
            try
            {
                string dir = MVR.FileManagementSecure.FileManagerSecure.GetDirectoryName(path);
                string fileName = MVR.FileManagementSecure.FileManagerSecure.GetFileName(path);
                path = dir + "\\Preset_" + fileName;
            }
            catch { }

            if (MVR.FileManagementSecure.FileManagerSecure.FileExists(path))
            {
                VpbSaveCacheSupport.ConfirmOverwriteThenSave(
                    path,
                    "Resource " + path + " already exists. Overwrite?",
                    () => SavePresetFinal(target, storableId, path, useScreenshot),
                    () => { EndSaveMode(); });
            }
            else
            {
                SavePresetFinal(target, storableId, path, useScreenshot);
            }
        }

        private void SavePresetFinal(Atom target, string storableId, string path, bool useScreenshot)
        {
            try { VpbSaveCacheSupport.RegisterPluginSaveWritePathForFile(path); } catch { }
            if (target == null) { EndSaveMode(); return; }
            JSONStorable presetJS = null;
            try { presetJS = target.GetStorableByID(storableId); } catch { presetJS = null; }
            if (presetJS == null)
            {
                ShowTemporaryStatus("Preset not available: " + storableId, 2f);
                EndSaveMode();
                return;
            }

            JSONStorableBool loadOnSelectJSB = null;
            try { loadOnSelectJSB = presetJS.GetBoolJSONParam("loadPresetOnSelect"); } catch { loadOnSelectJSB = null; }
            bool loadOnSelectPreState = loadOnSelectJSB != null && loadOnSelectJSB.val;
            if (loadOnSelectJSB != null) loadOnSelectJSB.val = false;

            if (useScreenshot)
            {
                StartCoroutine(SavePresetWithScreenshotCoroutine(presetJS, path, loadOnSelectJSB, loadOnSelectPreState));
                return;
            }

            bool savedOk = false;
            try
            {
                JSONStorableUrl presetPathJSON = presetJS.GetUrlJSONParam("presetBrowsePath");
                if (presetPathJSON != null) presetPathJSON.val = SuperController.singleton.NormalizePath(path);
                presetJS.CallAction("StorePreset");
                ShowTemporaryStatus("Preset saved: " + path, 2f);
                savedOk = true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Save preset failed: " + ex);
                ShowTemporaryStatus("Preset save failed. See log.", 2f);
            }
            finally
            {
                if (loadOnSelectJSB != null) loadOnSelectJSB.val = loadOnSelectPreState;
                EndSaveMode();
            }

            if (savedOk)
                VpbSaveCacheSupport.NotifyGalleryPanelsInvalidateAfterSave(path);
        }

        private IEnumerator SavePresetWithScreenshotCoroutine(JSONStorable presetJS, string path, JSONStorableBool loadOnSelectJSB, bool loadOnSelectPreState)
        {
            // Panels are already hidden by BeginSaveMode(); wait one frame so the
            // hide is in effect before VAM captures the screenshot.
            yield return new WaitForEndOfFrame();

            bool saved = false;
            try
            {
                JSONStorableUrl presetPathJSON = presetJS.GetUrlJSONParam("presetBrowsePath");
                if (presetPathJSON != null) presetPathJSON.val = SuperController.singleton.NormalizePath(path);
                presetJS.CallAction("StorePresetWithScreenshot");
                saved = true;
                ShowTemporaryStatus("Preset saved: " + path, 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Save preset failed: " + ex);
                ShowTemporaryStatus("Preset save failed. See log.", 2f);
            }

            if (saved)
            {
                float waitStart = Time.unscaledTime;
                const float waitForScreenshotStartMax = 12f;
                const float waitForScreenshotFinishMax = 45f;
                bool sawScreenshot = false;
                yield return null;
                while (true)
                {
                    bool screenshotActive = IsScreenshotCaptureActive();
                    if (screenshotActive) sawScreenshot = true;

                    if (sawScreenshot)
                    {
                        if (!screenshotActive) break;
                        if (Time.unscaledTime - waitStart > waitForScreenshotFinishMax) break;
                    }
                    else if (Time.unscaledTime - waitStart > waitForScreenshotStartMax)
                    {
                        break;
                    }

                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.2f);
            }

            if (loadOnSelectJSB != null) loadOnSelectJSB.val = loadOnSelectPreState;
            EndSaveMode();

            if (saved)
                VpbSaveCacheSupport.NotifyGalleryPanelsInvalidateAfterSave(path);
        }
        private void CreatePaginationControls()
        {
            // Footer Bar
            GameObject pageContainer = UI.CreateChildRT(backgroundBoxGO, "PaginationContainer", AnchorPresets.hStretchBottom, new Vector2(0, GalleryUiDesignTokens.FooterBarHeightRef)); // Footer bar height for buttons
            paginationRT = pageContainer.GetComponent<RectTransform>();

            footerHLG = pageContainer.AddComponent<HorizontalLayoutGroup>();
            footerHLG.padding = new RectOffset(10, 10, 0, 0); // resize handles are real layout children now (no manual reservation)
            {
                var hlg = footerHLG;
                innerPaneScaleActions.Add(s => { if (hlg) { hlg.padding = new RectOffset(Mathf.RoundToInt(10 * s), Mathf.RoundToInt(10 * s), 0, 0); } });
            }
            footerHLG.childControlWidth = true;
            footerHLG.childControlHeight = true;
            // Left/right shrink-wrap; center takes remaining gap (not equal ⅓ panel thirds).
            footerHLG.childForceExpandWidth = false;
            footerHLG.childForceExpandHeight = true;
            footerHLG.childAlignment = TextAnchor.MiddleLeft;

            // Fixed dock "Top": side rail overlay strip (ignoreLayout; parked right of left-aligned quality).
            _footerSideButtonsGroupGO = UI.CreateChildRT(pageContainer, "SideButtonsGroup", AnchorPresets.middleCenter, new Vector2(0f, GalleryUiDesignTokens.ButtonSizeRef));
            _footerSideButtonsGroupRT = _footerSideButtonsGroupGO.GetComponent<RectTransform>();
            {
                _footerSideButtonsGroupLE = _footerSideButtonsGroupGO.AddComponent<LayoutElement>();
                _footerSideButtonsGroupLE.ignoreLayout = true;
            }
            _footerSideButtonsGroupGO.SetActive(false);

            // --- Left Section (Undo / Hub / Follow) ---
            GameObject leftSection = new GameObject("LeftSection");
            leftSection.transform.SetParent(pageContainer.transform, false);
            _footerLeftSectionRT = leftSection.AddComponent<RectTransform>();
            UI.AddLE(leftSection, flexibleWidth: 0f);
            
            HorizontalLayoutGroup leftHLG = UI.AddHLG(leftSection, spacing: 10, childAlignment: TextAnchor.MiddleLeft, childControlWidth: false, childControlHeight: false, childForceExpandWidth: false, childForceExpandHeight: true);
            {
                var hlg = leftHLG;
                innerPaneScaleActions.Add(s => { if (hlg) hlg.spacing = 10f * s; });
            }

            // Undo / Redo (footer left)
            footerUndoBtnGO = UI.CreateUIButton(leftSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,VPBTranslation.T("gallery.footer.undo_abbrev", "U") + " (0)", 14, 0, 0, AnchorPresets.middleCenter, Undo);
            footerUndoBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            { var s = UI.LoadIconSprite("arrow-back-up", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerUndoBtnGO, s); }
            footerRedoBtnGO = UI.CreateUIButton(leftSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,VPBTranslation.T("gallery.footer.redo_abbrev", "R") + " (0)", 14, 0, 0, AnchorPresets.middleCenter, Redo);
            footerRedoBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            { var s = UI.LoadIconSprite("arrow-forward-up", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerRedoBtnGO, s); }

            footerCommandPaletteBtnGO = UI.CreateUIButton(leftSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,
                VPBTranslation.T("gallery.footer.cmd_abbrev", "P"), 14, 0, 0, AnchorPresets.middleCenter, ToggleCommandPalette);
            footerCommandPaletteBtnGO.name = "FooterCommandPaletteBtn";
            footerCommandPaletteBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            { var s = UI.LoadIconSprite("list-search", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerCommandPaletteBtnGO, s); }

            footerHubBtnGO = UI.CreateUIButton(leftSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,VPBTranslation.T("gallery.side.hub", "Hub"), 14, 0, 0, AnchorPresets.middleCenter, () => {
                VamHookPlugin.singleton?.OpenHubBrowse();
                Hide();
            });
            footerHubBtnImage = footerHubBtnGO.GetComponent<Image>();
            footerHubBtnImage.color = GalleryUiColorTokens.ChromeIconWell;
            footerHubBtnText = footerHubBtnGO.GetComponentInChildren<Text>();
            AddRightClickDelegate(footerHubBtnGO, () => {
                VamHookPlugin.singleton?.OpenHubBrowse();
                Hide();
            });
            { var s = UI.LoadIconSprite("world-search", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerHubBtnGO, s); }

            // Follow Quick Toggles
            footerFollowAngleBtn = UI.CreateUIButton(leftSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"∡", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Angle"));
            footerFollowAngleImage = footerFollowAngleBtn.GetComponent<Image>();
            { var s = UI.LoadIconSprite("angle", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerFollowAngleBtn, s); }
            AddTooltip(footerFollowAngleBtn, "gallery.tooltip.follow_angle", "Follow Angle");
            
            footerFollowDistanceBtn = UI.CreateUIButton(leftSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"↕", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Distance"));
            footerFollowDistanceImage = footerFollowDistanceBtn.GetComponent<Image>();
            { var s = UI.LoadIconSprite("ruler-measure", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerFollowDistanceBtn, s); }
            AddTooltip(footerFollowDistanceBtn, "gallery.tooltip.follow_distance", "Follow Distance");
            
            footerFollowHeightBtn = UI.CreateUIButton(leftSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"⊙", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Height"));
            footerFollowHeightImage = footerFollowHeightBtn.GetComponent<Image>();
            { var s = UI.LoadIconSprite("ruler-measure-2", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerFollowHeightBtn, s); }
            AddTooltip(footerFollowHeightBtn, "gallery.tooltip.follow_eye_height", "Follow Eye Height");

            // --- Center Section (fills gap between left/right packs; quality ± + filter chrome) ---
            GameObject centerSection = new GameObject("CenterSection");
            centerSection.transform.SetParent(pageContainer.transform, false);
            _footerCenterSectionRT = centerSection.AddComponent<RectTransform>();
            UI.AddLE(centerSection, flexibleWidth: 1f);
            
            _footerCenterHLG = UI.AddHLG(centerSection, spacing: 10, childAlignment: TextAnchor.MiddleCenter, childControlWidth: false, childControlHeight: false, childForceExpandWidth: false, childForceExpandHeight: true);
            {
                var hlg = _footerCenterHLG;
                innerPaneScaleActions.Add(s => { if (hlg) hlg.spacing = 10f * s; });
            }

            // Quality selector + step buttons as one centered group.
            // ContentSizeFitter shrink-wraps — without it Unity's default 100×100 RT drops the pack low.
            // Top dock: pack left-aligns (see ApplyFooterCenterAlignForDock) so side-strip overlay clears it.
            {
                GameObject perfGroup = new GameObject("FooterPerfGroup");
                perfGroup.transform.SetParent(centerSection.transform, false);
                _footerPerfGroupRT = perfGroup.AddComponent<RectTransform>();
                _footerPerfGroupRT.anchorMin = _footerPerfGroupRT.anchorMax = new Vector2(0.5f, 0.5f);
                _footerPerfGroupRT.pivot = new Vector2(0.5f, 0.5f);
                HorizontalLayoutGroup perfHLG = UI.AddHLG(perfGroup, spacing: 10, childAlignment: TextAnchor.MiddleCenter, childControlWidth: false, childControlHeight: false, childForceExpandWidth: false, childForceExpandHeight: true);
                ContentSizeFitter perfFit = perfGroup.AddComponent<ContentSizeFitter>();
                perfFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                perfFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                {
                    var hlg = perfHLG;
                    innerPaneScaleActions.Add(s => { if (hlg) hlg.spacing = 10f * s; });
                }
                CreateFooterPerfControls(perfGroup);
            }

            // Filter Mode Label (shown in filter mode, left of clear button)
            {
                GameObject modeGO = new GameObject("FilterModeLabel");
                modeGO.transform.SetParent(centerSection.transform, false);
                footerFilterModeText = modeGO.AddComponent<Text>();
                footerFilterModeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                footerFilterModeText.fontSize = GalleryUiDesignTokens.FontRef;
                footerFilterModeText.fontStyle = FontStyle.Normal;
                footerFilterModeText.color = new Color(1f, 0.85f, 0f, 1f);
                footerFilterModeText.alignment = TextAnchor.MiddleRight;
                footerFilterModeText.text = "";
                footerFilterModeText.horizontalOverflow = HorizontalWrapMode.Overflow;
                footerFilterModeText.verticalOverflow = VerticalWrapMode.Truncate;
                footerFilterModeText.raycastTarget = false;
                RectTransform modeRT = modeGO.GetComponent<RectTransform>();
                modeRT.sizeDelta = new Vector2(180, GalleryUiDesignTokens.ButtonSizeRef);
                modeGO.SetActive(false);
            }

            // Small spacer between mode label and clear button (only visible in filter mode)
            {
                footerFilterModeSpacerGO = new GameObject("FilterModeSpacer");
                footerFilterModeSpacerGO.transform.SetParent(centerSection.transform, false);
                footerFilterModeSpacerGO.AddComponent<RectTransform>();
                var le = UI.AddLE(footerFilterModeSpacerGO, minWidth: 12f, preferredWidth: 12f);
                footerFilterModeSpacerGO.SetActive(false);
            }

            // Back Button — icon-only, shown whenever filter mode is active
            footerBackBtn = UI.CreateUIButton(centerSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"", 18, 0, 0, AnchorPresets.middleCenter, NavigateBack);
            footerBackBtn.name = "BackButton";
            footerBackBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.6f, 0.9f);
            { var s = UI.LoadIconSprite("arrow-left", Color.white); if (s != null) UI.AddIconToButton(footerBackBtn, s, 4f, new Color(0.2f, 0.35f, 0.6f, 0.9f)); }
            footerBackBtn.SetActive(false);

            // Clear Filter Button — icon-only, clears all filter levels
            footerClearFilterBtn = UI.CreateUIButton(centerSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"", 18, 0, 0, AnchorPresets.middleCenter, ClearPackageFilter);
            footerClearFilterBtn.name = "ClearFilterButton";
            footerClearFilterBtn.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
            { var s = UI.LoadIconSprite("filter-off", Color.white); if (s != null) UI.AddIconToButton(footerClearFilterBtn, s, 4f, new Color(0.8f, 0.2f, 0.2f, 0.9f)); }
            footerClearFilterBtn.SetActive(false);

            // --- Right Section (Utility Controls) ---
            GameObject rightSection = new GameObject("RightSection");
            rightSection.transform.SetParent(pageContainer.transform, false);
            _footerRightSectionRT = rightSection.AddComponent<RectTransform>();
            UI.AddLE(rightSection, flexibleWidth: 0f);
            
            HorizontalLayoutGroup rightHLG = UI.AddHLG(rightSection, spacing: 10, childAlignment: TextAnchor.MiddleRight, childControlWidth: false, childControlHeight: false, childForceExpandWidth: false, childForceExpandHeight: true);
            {
                var hlg = rightHLG;
                innerPaneScaleActions.Add(s => { if (hlg) hlg.spacing = 10f * s; });
            }

            // VaM Menu Gate (show gallery only when VaM menu is visible) — placed left of Select All
            footerMenuGateBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"M", 20, 0, 0, AnchorPresets.middleCenter, ToggleVamMenuGateMode);
            footerMenuGateBtnImage = footerMenuGateBtn.GetComponent<Image>();
            footerMenuGateOffSprite = UI.LoadIconSprite("layers-selected-bottom", Color.white);
            footerMenuGateOnSprite  = UI.LoadIconSprite("layers-union",      Color.white);
            { Sprite init = footerMenuGateOffSprite ?? footerMenuGateOnSprite; if (init != null) { UI.AddIconToButton(footerMenuGateBtn, init); footerMenuGateIconImage = footerMenuGateBtn.transform.Find("Icon")?.GetComponent<Image>(); } }
            AddTooltip(footerMenuGateBtn, "gallery.tooltip.vam_menu_gate", "Show only when VaM menu is visible");

            // VR wrist watch show/hide toggle (footer chrome only in VR; desktop hides via UpdateFooterVrWatchState).
            footerWatchToggleBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"W", 20, 0, 0, AnchorPresets.middleCenter, ToggleVrWatchVisible);
            footerWatchToggleBtnImage = footerWatchToggleBtn.GetComponent<Image>();
            footerWatchToggleOnSprite  = UI.LoadIconSprite("device-watch",     Color.white);
            footerWatchToggleOffSprite = UI.LoadIconSprite("device-watch-off", Color.white);
            { Sprite init = footerWatchToggleOnSprite ?? footerWatchToggleOffSprite; if (init != null) { UI.AddIconToButton(footerWatchToggleBtn, init); footerWatchToggleIconImage = footerWatchToggleBtn.transform.Find("Icon")?.GetComponent<Image>(); } }
            AddTooltip(footerWatchToggleBtn, "gallery.tooltip.vr_watch_toggle", "Show/hide VR wrist watch (look at inner wrist)");
            footerWatchToggleBtn.SetActive(false);

            // Sidebar toggle lives on the side-rail Scene Import button (above Tags); no footer button.

            gridSizeMinusBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"-", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(1));
            { var s = UI.LoadIconSprite("zoom-out", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(gridSizeMinusBtn, s); }
            gridSizePlusBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"+", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(-1));
            { var s = UI.LoadIconSprite("zoom-in", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(gridSizePlusBtn, s); }

            // Toggle hold-to-launch/apply (hold trigger/button on item; duration in Settings)
            footerHoldToLaunchToggleBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"H", 20, 0, 0, AnchorPresets.middleCenter, ToggleHoldToLaunch);
            footerHoldToLaunchToggleBtnImage = footerHoldToLaunchToggleBtn.GetComponent<Image>();
            footerHoldToLaunchOnSprite  = UI.LoadIconSprite("hand-finger",     UI.BarIconGlyphTint);
            footerHoldToLaunchOffSprite = UI.LoadIconSprite("hand-finger-off", UI.BarIconGlyphTint);
            {
                // Fallback to old icon if hold icons missing
                var fallback = UI.LoadIconSprite("player-play", UI.BarIconGlyphTint);
                var init = (holdToLaunchEnabled ? footerHoldToLaunchOnSprite : footerHoldToLaunchOffSprite) ?? footerHoldToLaunchOnSprite ?? footerHoldToLaunchOffSprite ?? fallback;
                if (init != null)
                {
                    UI.AddIconToButton(footerHoldToLaunchToggleBtn, init);
                    footerHoldToLaunchToggleIconImage = footerHoldToLaunchToggleBtn.transform.Find("Icon")?.GetComponent<Image>();
                }
            }
            AddTooltip(footerHoldToLaunchToggleBtn, "gallery.tooltip.hold_to_launch_toggle", "Hold trigger/button on item to apply/launch. While ON, drag-drop is paused (same press). See Settings for duration.");

            footerLayoutBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"▤", 20, 0, 0, AnchorPresets.middleCenter, ToggleLayoutMode);
            footerLayoutBtnImage = footerLayoutBtn.GetComponent<Image>();
            footerLayoutBtnText = footerLayoutBtn.GetComponentInChildren<Text>();
            footerLayoutGridSprite = UI.LoadIconSprite("layout-grid", UI.BarIconGlyphTint);
            footerLayoutListSprite = UI.LoadIconSprite("layout-list", UI.BarIconGlyphTint);
            { Sprite init = footerLayoutListSprite ?? footerLayoutGridSprite; if (init != null) { UI.AddIconToButton(footerLayoutBtn, init); footerLayoutIconImage = footerLayoutBtn.transform.Find("Icon")?.GetComponent<Image>(); } }

            footerDockBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"", 20, 0, 0, AnchorPresets.middleCenter, CycleDesktopFixedDockSide);
            footerDockBtnImage = footerDockBtn.GetComponent<Image>();
            footerDockRightSprite = UI.LoadIconSprite("box-align-right", UI.BarIconGlyphTint);
            footerDockLeftSprite  = UI.LoadIconSprite("box-align-left",  UI.BarIconGlyphTint);
            footerDockTopSprite   = UI.LoadIconSprite("box-align-top",   UI.BarIconGlyphTint);
            { Sprite init = footerDockRightSprite ?? footerDockLeftSprite ?? footerDockTopSprite; if (init != null) { UI.AddIconToButton(footerDockBtn, init); footerDockIconImage = footerDockBtn.transform.Find("Icon")?.GetComponent<Image>(); } }
            AddTooltipPlain(footerDockBtn, VPBTranslation.T("gallery.tooltip.dock_side", "Dock side (Left/Right/Top)"));

            footerHeightBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"↕", 20, 0, 0, AnchorPresets.middleCenter, ToggleFixedHeightMode);
            footerHeightBtnImage = footerHeightBtn.GetComponent<Image>();
            footerHeightBtnText = footerHeightBtn.GetComponentInChildren<Text>();
            footerHeightFreeSprite  = UI.LoadIconSprite("square-toggle-horizontal",  UI.BarIconGlyphTint);
            footerHeightFixedSprite = UI.LoadIconSprite("arrows-vertical", UI.BarIconGlyphTint);
            { Sprite init = footerHeightFixedSprite ?? footerHeightFreeSprite; if (init != null) { UI.AddIconToButton(footerHeightBtn, init); footerHeightIconImage = footerHeightBtn.transform.Find("Icon")?.GetComponent<Image>(); } }

            footerAutoHideBtn = UI.CreateUIButton(rightSection, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"A", 20, 0, 0, AnchorPresets.middleCenter, ToggleAutoHideMode);
            footerAutoHideBtnImage = footerAutoHideBtn.GetComponent<Image>();
            footerAutoHideBtnText = footerAutoHideBtn.GetComponentInChildren<Text>();
            footerAutoHideLeftOffSprite  = UI.LoadIconSprite("arrow-bar-to-left-dashed",  UI.BarIconGlyphTint);
            footerAutoHideLeftOnSprite   = UI.LoadIconSprite("arrow-bar-to-right",   UI.BarIconGlyphTint);
            footerAutoHideRightOffSprite = UI.LoadIconSprite("arrow-bar-to-left", UI.BarIconGlyphTint);
            footerAutoHideRightOnSprite  = UI.LoadIconSprite("arrow-bar-to-right-dashed",  UI.BarIconGlyphTint);
            footerAutoHideTopOffSprite   = UI.LoadIconSprite("arrow-bar-to-down",   UI.BarIconGlyphTint);
            footerAutoHideTopOnSprite    = UI.LoadIconSprite("arrow-bar-to-up-dashed",    UI.BarIconGlyphTint);
            footerAutoHideOffSprite = UI.LoadIconSprite("layout-sidebar-right-expand", UI.BarIconGlyphTint);
            footerAutoHideOnSprite  = UI.LoadIconSprite("layout-sidebar-right-collapse",  UI.BarIconGlyphTint);
            {
                GetFooterAutoHideSpritesForCurrentDock(out Sprite initOff, out Sprite initOn);
                Sprite init = initOff ?? initOn;
                if (init != null) { UI.AddIconToButton(footerAutoHideBtn, init); footerAutoHideIconImage = footerAutoHideBtn.transform.Find("Icon")?.GetComponent<Image>(); }
            }

            try { EnsureFooterOverflowChrome(rightSection); } catch { }

            // --- Context Actions (Category-aware) ---

            AddHoverDelegate(gridSizeMinusBtn);
            AddTooltip(gridSizeMinusBtn, "gallery.tooltip.grid_minus", "Decrease columns (Ctrl+scroll wheel over gallery)");
            AddHoverDelegate(gridSizePlusBtn);
            AddTooltip(gridSizePlusBtn, "gallery.tooltip.grid_plus", "Increase columns (Ctrl+scroll wheel over gallery)");
            AddHoverDelegate(footerMenuGateBtn);
            AddHoverDelegate(footerHoldToLaunchToggleBtn);
            AddHoverDelegate(footerBackBtn);
            AddTooltip(footerBackBtn, "gallery.tooltip.filter_back", "Go back one filter level");
            AddHoverDelegate(footerClearFilterBtn);
            AddTooltip(footerClearFilterBtn, "gallery.tooltip.clear_filter", "Clear all filters");
            AddHoverDelegate(footerUndoBtnGO);
            AddDynamicTooltip(footerUndoBtnGO, BuildUndoTooltip);
            AddHoverDelegate(footerRedoBtnGO);
            AddDynamicTooltip(footerRedoBtnGO, BuildRedoTooltip);
            AddHoverDelegate(footerCommandPaletteBtnGO);
            AddTooltip(footerCommandPaletteBtnGO, "gallery.tooltip.command_palette", "Command palette (Ctrl+Shift+P)");
            AddHoverDelegate(footerHubBtnGO);
            AddTooltip(footerHubBtnGO, "gallery.tooltip.hub", "Hub browse / dev Hub panel");
            AddHoverDelegate(footerFollowAngleBtn);
            AddHoverDelegate(footerFollowDistanceBtn);
            AddHoverDelegate(footerFollowHeightBtn);
            AddHoverDelegate(footerLayoutBtn);
            AddHoverDelegate(footerDockBtn);
            AddHoverDelegate(footerHeightBtn);
            AddHoverDelegate(footerAutoHideBtn);

            // Register inner pane button scale actions (footer)
            { var prt = paginationRT; innerPaneScaleActions.Add(s => { if (prt) prt.sizeDelta = new Vector2(0, GalleryUiDesignTokens.FooterBarHeightRef * s); }); }
            {
                var uRT = footerUndoBtnGO != null ? footerUndoBtnGO.GetComponent<RectTransform>() : null;
                var rRT = footerRedoBtnGO != null ? footerRedoBtnGO.GetComponent<RectTransform>() : null;
                var uT = footerUndoBtnGO != null ? footerUndoBtnGO.GetComponentInChildren<Text>() : null;
                var rT = footerRedoBtnGO != null ? footerRedoBtnGO.GetComponentInChildren<Text>() : null;
                var pRT = footerCommandPaletteBtnGO != null ? footerCommandPaletteBtnGO.GetComponent<RectTransform>() : null;
                var pT = footerCommandPaletteBtnGO != null ? footerCommandPaletteBtnGO.GetComponentInChildren<Text>() : null;
                var hRT = footerHubBtnGO != null ? footerHubBtnGO.GetComponent<RectTransform>() : null;
                var hT = footerHubBtnText;
                innerPaneScaleActions.Add(s => {
                    if (uRT != null) uRT.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s);
                    if (rRT != null) rRT.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s);
                    if (pRT != null) pRT.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s);
                    if (hRT != null) hRT.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s);
                    GalleryUiMetrics.ApplyFont(uT, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                    GalleryUiMetrics.ApplyFont(rT, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                    GalleryUiMetrics.ApplyFont(pT, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                    GalleryUiMetrics.ApplyFont(hT, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                });
            }
            var footerBtnGOs = new GameObject[] {
                footerFollowAngleBtn, footerFollowDistanceBtn, footerFollowHeightBtn,
                footerMenuGateBtn, footerWatchToggleBtn,
                gridSizeMinusBtn, gridSizePlusBtn,
                footerHoldToLaunchToggleBtn,
                footerLayoutBtn, footerHeightBtn, footerAutoHideBtn, footerDockBtn,
                _footerOverflowBtnGO,
            };
            for (int i = 0; i < footerBtnGOs.Length; i++)
            {
                var rt = footerBtnGOs[i] != null ? footerBtnGOs[i].GetComponent<RectTransform>() : null;
                var t = footerBtnGOs[i] != null ? footerBtnGOs[i].GetComponentInChildren<Text>() : null;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt) rt.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s);
                    if (t) GalleryUiMetrics.ApplyGlyphFont(t, GalleryUiDesignTokens.ButtonSizeRef, s, GalleryUiDesignTokens.FontMinRef);
                });
            }

            // Top-dock footer row: same scale path as footer buttons (outer chrome + group layout).
            innerPaneScaleActions.Add(s =>
            {
                try
                {
                    if (IsFixedTopDockMode() && !isCollapsed)
                        ApplyTopDockSideButtonsLayout(s);
                }
                catch { }
            });

            innerPaneScaleActions.Add(s => { try { LayoutScrollbarJumpButtons(s); } catch { } });

            UpdateSpringScrollButtonToggleUI();
            UpdateHoldToLaunchToggleUI();

            innerPaneScaleActions.Add(s => { try { ApplyFooterOverflowLayout(s); } catch { } });

            // Scale the back button
            {
                var rt = footerBackBtn != null ? footerBackBtn.GetComponent<RectTransform>() : null;
                innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s); });
            }
            // Scale the clear filter button
            {
                var rt = footerClearFilterBtn != null ? footerClearFilterBtn.GetComponent<RectTransform>() : null;
                innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s); });
            }

            // Scale the filter mode label
            {
                var t = footerFilterModeText;
                var rt = t != null ? t.GetComponent<RectTransform>() : null;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt) rt.sizeDelta = new Vector2(180f * s, GalleryUiDesignTokens.ButtonSizeRef * s);
                    if (t)
                    {
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontRef, s, GalleryUiDesignTokens.FontMinRef);
                        t.fontStyle = FontStyle.Normal;
                    }
                });
            }
            // Scale the spacer
            {
                var go = footerFilterModeSpacerGO;
                var rt = go != null ? go.GetComponent<RectTransform>() : null;
                var le = go != null ? go.GetComponent<LayoutElement>() : null;
                innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(12f*s, GalleryUiDesignTokens.ButtonSizeRef * s); if (le != null) { le.preferredWidth = 12f*s; le.minWidth = 12f*s; } });
            }

            // Unified info bar — always visible; hosts hover path, status messages, and tbox label/buttons.
            GameObject pathGO = UI.AddChildGOImage(backgroundBoxGO, UI.ChromeDark, AnchorPresets.hStretchBottom, 0, 40, new Vector2(0, 40));
            pathGO.name = "HoverPathContainer";
            pathGO.GetComponent<Image>().raycastTarget = true; // tbox hover delegate needs raycasts
            // Removed RectMask2D - it was causing visual glitches/flashing during height animation during category switches
            hoverPathRT = pathGO.GetComponent<RectTransform>();

            CreateHoverPreviewOverlay(backgroundBoxGO);

            // HoverPathText — anchored to the bottom (tooltip) row; CanvasGroup fades only the text
            // Bottom-row anchor: pinned to bottom of bar, fixed 60 px tall (updated by scale actions)
            GameObject hoverPathTextGO = UI.CreateChildRT(pathGO, "HoverPathText", AnchorPresets.hStretchBottom, new Vector2(0f, 60f));
            hoverPathCanvasGroup = hoverPathTextGO.AddComponent<CanvasGroup>();
            hoverPathCanvasGroup.alpha = 0;
            hoverPathCanvasGroup.blocksRaycasts = false;
            hoverPathCanvasGroup.interactable = false;
            var hoverPathTextRT2 = hoverPathTextGO.GetComponent<RectTransform>();

            hoverPathText = hoverPathTextGO.AddComponent<Text>();
            hoverPathText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hoverPathText.fontSize = GalleryUiDesignTokens.FontBodyRef;
            hoverPathText.color = Color.white;
            var shadow = hoverPathTextGO.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(1, -1);
            hoverPathText.alignment = TextAnchor.MiddleCenter;
            hoverPathText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hoverPathText.verticalOverflow = VerticalWrapMode.Truncate;
            hoverPathText.lineSpacing = 0.9f;
            hoverPathText.text = "";
            hoverPathText.raycastTarget = false;

            // Scale action keeps text wrapper + font in sync with ChromeScale (register after Text exists).
            {
                var hpRT = hoverPathTextRT2;
                var hpText = hoverPathText;
                innerPaneScaleActions.Add(s =>
                {
                    if (hpRT != null) hpRT.sizeDelta = new Vector2(0f, GalleryUiDesignTokens.FooterInfoRowHeightRef * s);
                    if (hpText != null)
                        GalleryUiMetrics.ApplyFont(hpText, GalleryUiDesignTokens.FooterHoverPathFontRef, s, GalleryUiDesignTokens.FontMinRef);
                });
            }

            UpdateSideButtonsVisibility();
            UpdateFooterFollowStates();
            UpdateFooterLayoutState();
            UpdateFooterHeightState();
            UpdateFooterAutoHideState();
            UpdateFooterVamMenuGateState();
            UpdateFooterVrWatchState();
            try { ApplyFooterOverflowLayout(ChromeScale); } catch { }
            try { ApplyFooterModeButtonVisibility(); } catch { }
            UpdatePaginationText();
            try { UpdateUndoRedoButtonLabels(); } catch { }
        }

        private void ApplySpringScrollButtonFromConfig()
        {
            springScrollButtonEnabled = VPBConfig.Instance != null && VPBConfig.Instance.IsSpringScrollButtonEnabled();
            if (springScrollButtonEnabled)
            {
                try { EnsureSpringScrollButtonExists(); } catch { }
            }
            if (springScrollButtonGO != null)
                springScrollButtonGO.SetActive(springScrollButtonEnabled);
            UpdateSpringScrollButtonToggleUI();
        }

        private void EnsureScrollbarJumpButtonsExist()
        {
            if (!ShouldShowGalleryScrollStepButtons())
            {
                SetGalleryScrollStepButtonsActive(false);
                return;
            }
            if (footerScrollTopBtn != null && footerScrollBottomBtn != null && footerScrollStepUpBtn != null && footerScrollStepDownBtn != null) return;
            if (scrollRect == null) return;
            Transform sb = null;
            try { sb = scrollRect.gameObject != null ? scrollRect.gameObject.transform.Find("Scrollbar") : null; }
            catch { sb = null; }
            if (sb == null) return;

            if (footerScrollTopBtn == null)
            {
                footerScrollTopBtn = UI.CreateUIButton(sb.gameObject, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"↑", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryToTop);
                footerScrollTopBtn.name = "ScrollbarScrollTop";
                { var s = UI.LoadIconSprite("arrow-bar-to-up", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerScrollTopBtn, s); }
                AddHoverDelegate(footerScrollTopBtn);
                AddTooltip(footerScrollTopBtn, "gallery.tooltip.scroll_top", "Jump to top of list");
            }
            if (footerScrollBottomBtn == null)
            {
                footerScrollBottomBtn = UI.CreateUIButton(sb.gameObject, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"↓", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryToBottom);
                footerScrollBottomBtn.name = "ScrollbarScrollBottom";
                { var s = UI.LoadIconSprite("arrow-bar-to-down", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerScrollBottomBtn, s); }
                AddHoverDelegate(footerScrollBottomBtn);
                AddTooltip(footerScrollBottomBtn, "gallery.tooltip.scroll_bottom", "Jump to bottom of list");
            }
            if (footerScrollStepUpBtn == null)
            {
                footerScrollStepUpBtn = UI.CreateUIButton(sb.gameObject, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"▲", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryStepUp);
                footerScrollStepUpBtn.name = "ScrollbarScrollStepUp";
                { var s = UI.LoadIconSprite("chevron-up", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerScrollStepUpBtn, s); }
                AddHoverDelegate(footerScrollStepUpBtn);
                AddTooltip(footerScrollStepUpBtn, "gallery.tooltip.scroll_step_up", "Scroll up");
            }
            if (footerScrollStepDownBtn == null)
            {
                footerScrollStepDownBtn = UI.CreateUIButton(sb.gameObject, GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef,"▼", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryStepDown);
                footerScrollStepDownBtn.name = "ScrollbarScrollStepDown";
                { var s = UI.LoadIconSprite("chevron-down", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerScrollStepDownBtn, s); }
                AddHoverDelegate(footerScrollStepDownBtn);
                AddTooltip(footerScrollStepDownBtn, "gallery.tooltip.scroll_step_down", "Scroll down");
            }

            HookScrollbarJumpButtonsSmartVisibility();
            UpdateScrollbarJumpButtonsVisibility();
        }

        private bool _scrollbarJumpButtonsVisibilityHooked;
        private UnityAction<Vector2> _scrollbarJumpButtonsVisibilityHandler;

        private void HookScrollbarJumpButtonsSmartVisibility()
        {
            if (_scrollbarJumpButtonsVisibilityHooked) return;
            if (scrollRect == null) return;

            if (_scrollbarJumpButtonsVisibilityHandler == null)
                _scrollbarJumpButtonsVisibilityHandler = _ => UpdateScrollbarJumpButtonsVisibility();

            try { scrollRect.onValueChanged.AddListener(_scrollbarJumpButtonsVisibilityHandler); }
            catch { }
            _scrollbarJumpButtonsVisibilityHooked = true;
        }

        private void UpdateScrollbarJumpButtonsVisibility()
        {
            if (footerScrollTopBtn == null || footerScrollBottomBtn == null) return;
            if (scrollRect == null) return;
            if (!ShouldShowGalleryScrollStepButtons())
            {
                SetGalleryScrollStepButtonsActive(false);
                return;
            }

            bool scrollable = false;
            try
            {
                if (scrollRect.content != null && scrollRect.viewport != null)
                {
                    float scrollablePx = scrollRect.content.rect.height - scrollRect.viewport.rect.height;
                    scrollable = scrollablePx > 1f;
                }
            }
            catch { scrollable = false; }

            if (!scrollable)
            {
                footerScrollTopBtn.SetActive(false);
                footerScrollBottomBtn.SetActive(false);
                if (footerScrollStepUpBtn != null) footerScrollStepUpBtn.SetActive(false);
                if (footerScrollStepDownBtn != null) footerScrollStepDownBtn.SetActive(false);
                return;
            }

            float n = 1f;
            try { n = scrollRect.verticalNormalizedPosition; } catch { n = 1f; }

            // Unity: 1 = top, 0 = bottom. Keep thresholds simple.
            bool atTop = n >= 0.999f;
            bool atBottom = n <= 0.001f;

            bool showTop = !atTop;
            bool showBottom = !atBottom;

            // If thumb overlaps button zone, keep button hidden until thumb moves away.
            try
            {
                var sbt = scrollRect.gameObject != null ? scrollRect.gameObject.transform.Find("Scrollbar") : null;
                var sb = sbt != null ? sbt.GetComponent<Scrollbar>() : null;
                var sbRT = sbt as RectTransform;
                var handleRT = sb != null ? sb.handleRect : null;
                if (sbRT != null && handleRT != null)
                {
                    var topRT = footerScrollTopBtn.GetComponent<RectTransform>();
                    var botRT = footerScrollBottomBtn.GetComponent<RectTransform>();
                    if (topRT != null && BoundsOverlapY(sbRT, handleRT, topRT)) showTop = false;
                    if (botRT != null && BoundsOverlapY(sbRT, handleRT, botRT)) showBottom = false;
                }
            }
            catch { }

            footerScrollTopBtn.SetActive(showTop);
            footerScrollBottomBtn.SetActive(showBottom);
            if (footerScrollStepUpBtn != null) footerScrollStepUpBtn.SetActive(showTop);
            if (footerScrollStepDownBtn != null) footerScrollStepDownBtn.SetActive(showBottom);
        }

        private bool ShouldShowGalleryScrollStepButtons()
        {
            return XrUtils.IsVrActive()
                && VPBConfig.Instance != null
                && VPBConfig.Instance.GalleryScrollButtonsEnabled;
        }

        private void SetGalleryScrollStepButtonsActive(bool active)
        {
            if (footerScrollTopBtn != null) footerScrollTopBtn.SetActive(active);
            if (footerScrollBottomBtn != null) footerScrollBottomBtn.SetActive(active);
            if (footerScrollStepUpBtn != null) footerScrollStepUpBtn.SetActive(active);
            if (footerScrollStepDownBtn != null) footerScrollStepDownBtn.SetActive(active);
        }

        private static bool BoundsOverlapY(RectTransform commonRoot, RectTransform a, RectTransform b)
        {
            if (commonRoot == null || a == null || b == null) return false;
            Bounds ba;
            Bounds bb;
            try { ba = RectTransformUtility.CalculateRelativeRectTransformBounds(commonRoot, a); }
            catch { return false; }
            try { bb = RectTransformUtility.CalculateRelativeRectTransformBounds(commonRoot, b); }
            catch { return false; }
            float aMin = ba.min.y;
            float aMax = ba.max.y;
            float bMin = bb.min.y;
            float bMax = bb.max.y;
            return aMax > bMin && bMax > aMin;
        }

        private static void SyncScrollbarJumpButtonCollider(GameObject go)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            var bc = go.GetComponent<BoxCollider>();
            if (bc == null) bc = go.AddComponent<BoxCollider>();
            if (rt == null) return;
            Vector2 d = rt.sizeDelta;
            bc.size = new Vector3(d.x, d.y, bc.size.z > 0.1f ? bc.size.z : 20f);
            bc.center = Vector3.zero;
            // UI collider must not participate in physics collisions with scene atoms.
            bc.isTrigger = true;
        }

        /// <summary>Place jump top / spring drag / jump bottom in a vertical stack on the scrollbar.</summary>
        private void LayoutScrollbarJumpButtons(float? innerPaneScaleOverride = null)
        {
            float paneS = innerPaneScaleOverride ?? (ChromeScale);

            // The spring scroll button toggles independently of the jump/step buttons, so resize it
            // first and never gate it behind their existence (otherwise it ignores UI-scale changes
            // whenever the jump buttons are disabled).
            ApplySpringScrollButtonScale(paneS);

            if (footerScrollTopBtn == null || footerScrollBottomBtn == null) return;

            float btnSz = Mathf.Round(GalleryUiDesignTokens.ButtonSizeRef * paneS);
            const float gap = 6f;

            var topRt = footerScrollTopBtn.GetComponent<RectTransform>();
            var botRt = footerScrollBottomBtn.GetComponent<RectTransform>();
            var upRt = footerScrollStepUpBtn != null ? footerScrollStepUpBtn.GetComponent<RectTransform>() : null;
            var downRt = footerScrollStepDownBtn != null ? footerScrollStepDownBtn.GetComponent<RectTransform>() : null;
            if (topRt == null || botRt == null) return;

            // Pin jump buttons to scrollbar top/bottom ends (not relative to spring button).
            topRt.anchorMin = topRt.anchorMax = new Vector2(0.5f, 1f);
            botRt.anchorMin = botRt.anchorMax = new Vector2(0.5f, 0f);
            topRt.pivot = new Vector2(0.5f, 1f);
            botRt.pivot = new Vector2(0.5f, 0f);
            topRt.sizeDelta = botRt.sizeDelta = new Vector2(btnSz, btnSz);
            topRt.anchoredPosition = new Vector2(0f, -gap);
            botRt.anchoredPosition = new Vector2(0f, gap);
            if (upRt != null)
            {
                upRt.anchorMin = upRt.anchorMax = new Vector2(0.5f, 1f);
                upRt.pivot = new Vector2(0.5f, 1f);
                upRt.sizeDelta = new Vector2(btnSz, btnSz);
                upRt.anchoredPosition = new Vector2(0f, -(btnSz + gap * 2f));
            }
            if (downRt != null)
            {
                downRt.anchorMin = downRt.anchorMax = new Vector2(0.5f, 0f);
                downRt.pivot = new Vector2(0.5f, 0f);
                downRt.sizeDelta = new Vector2(btnSz, btnSz);
                downRt.anchoredPosition = new Vector2(0f, btnSz + gap * 2f);
            }

            int fs = Mathf.RoundToInt(GalleryUiDesignTokens.FontBodyRef * paneS);
            foreach (var go in new[] { footerScrollTopBtn, footerScrollBottomBtn, footerScrollStepUpBtn, footerScrollStepDownBtn })
            {
                if (go == null) continue;
                var tx = go.GetComponentInChildren<Text>(true);
                if (tx != null && tx.gameObject.activeSelf) tx.fontSize = fs;
            }

            SyncScrollbarJumpButtonCollider(footerScrollTopBtn);
            SyncScrollbarJumpButtonCollider(footerScrollBottomBtn);
            SyncScrollbarJumpButtonCollider(footerScrollStepUpBtn);
            SyncScrollbarJumpButtonCollider(footerScrollStepDownBtn);

            if (springScrollButtonGO != null)
            {
                int si = springScrollButtonGO.transform.GetSiblingIndex();
                footerScrollTopBtn.transform.SetSiblingIndex(si);
                if (footerScrollStepUpBtn != null) footerScrollStepUpBtn.transform.SetSiblingIndex(si + 1);
                if (footerScrollStepDownBtn != null) footerScrollStepDownBtn.transform.SetSiblingIndex(si + 3);
                footerScrollBottomBtn.transform.SetSiblingIndex(si + 4);
            }

            UpdateScrollbarJumpButtonsVisibility();
        }

        /// <summary>Sizes the spring scroll drag button + icon for the given chrome scale. Safe when no spring button exists.</summary>
        private void ApplySpringScrollButtonScale(float paneS)
        {
            if (springScrollButtonGO == null) return;
            if (paneS <= 0f) paneS = 1f;

            float baseW = isFixedLocally
                ? GalleryUiDesignTokens.SpringScrollBtnWidthFixedRef
                : GalleryUiDesignTokens.SpringScrollBtnWidthFloatRef;
            float springW = baseW * paneS;
            float springH = springW * GalleryUiDesignTokens.SpringScrollBtnAspectRef;
            float offsetX = isFixedLocally
                ? 0f
                : GalleryUiDesignTokens.SpringScrollBtnOffsetXFloatRef * paneS;
            SpringScrollButton ssb = springScrollButtonGO.GetComponent<SpringScrollButton>();
            if (ssb != null)
                ssb.SetSize(springW, springH);
            RectTransform springRt = springScrollButtonGO.GetComponent<RectTransform>();
            if (springRt != null)
            {
                if (ssb == null)
                    springRt.sizeDelta = new Vector2(springW, springH);
                springRt.anchoredPosition = new Vector2(offsetX, 0f);
            }
            Transform iconT = springScrollButtonGO.transform.Find("Icon");
            if (iconT != null)
            {
                RectTransform irt = iconT as RectTransform;
                if (irt != null)
                {
                    float inset = GalleryUiDesignTokens.SpringScrollBtnIconInsetRef * paneS;
                    irt.sizeDelta = new Vector2(-inset, -inset);
                }
            }
            RoundedRect springRounded = springScrollButtonGO.GetComponent<RoundedRect>();
            if (springRounded != null)
                springRounded.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
        }

        private void EnsureSpringScrollButtonExists()
        {
            if (springScrollButtonGO != null) return;
            if (scrollRect == null) return;

            Transform sb = null;
            try { sb = scrollRect.gameObject != null ? scrollRect.gameObject.transform.Find("Scrollbar") : null; } catch { sb = null; }

            if (sb == null) return;

            float w = isFixedLocally
                ? GalleryUiDesignTokens.SpringScrollBtnWidthFixedRef
                : GalleryUiDesignTokens.SpringScrollBtnWidthFloatRef;
            float h = w * GalleryUiDesignTokens.SpringScrollBtnAspectRef;
            GameObject springBtn = SpringScrollButton.Create(sb.gameObject, scrollRect, w, h);
            springBtn.transform.SetAsLastSibling();

            SpringScrollButton ssb = springBtn.GetComponent<SpringScrollButton>();
            if (ssb != null)
            {
                ssb.deadzoneFraction = 0.10f;
                ssb.maxViewportHeightsPerSecond = 2.25f;
                ssb.speedSmoothing = 12f;
                ssb.responsePower = 2.0f;
            }

            try
            {
                Sprite icon = UI.LoadIconSprite("caret-up-down", UI.BarIconGlyphTint);
                if (icon != null)
                {
                    GameObject iconGO = UI.CreateChildRT(springBtn, "Icon", AnchorPresets.stretchAll, new Vector2(-24f, -24f));
                    Image img = UI.AddImage(iconGO, Color.white);
                    UI.SetIconSprite(img, icon);
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                }
            }
            catch { }

            try
            {
                AddTooltip(springBtn, "gallery.tooltip.spring_scroll_drag", "Hold and drag up/down to scroll (farther = faster). Release to stop.");
            }
            catch { }

            springScrollButtonGO = springBtn;
            ApplySpringScrollButtonScale(ChromeScale);

            try
            {
                EnsureScrollbarJumpButtonsExist();
                LayoutScrollbarJumpButtons();
            }
            catch { }
        }

        private void ToggleHoldToLaunch()
        {
            // Sticky tools own input — do not arm Hold; still allow clear if somehow armed.
            try
            {
                if (!holdToLaunchEnabled
                    && TaskChromeSuppressArmedApplyChrome(ResolveTaskChromeState()))
                    return;
            }
            catch { }

            holdToLaunchEnabled = !holdToLaunchEnabled;
            try
            {
                if (VPBConfig.Instance != null)
                {
                    if (holdToLaunchEnabled)
                    {
                        // Avoid gesture conflicts: hold-to-launch uses pointer-down hold, same as drag start.
                        // Keep user preference (EnableDragDrop) intact; runtime suppression handled by VPBConfig.EffectiveEnableDragDrop.
                        holdToLaunchPrevEnableDragDrop = VPBConfig.Instance.EnableDragDrop;
                        VPBConfig.Instance.HoldToLaunchPrevEnableDragDrop = holdToLaunchPrevEnableDragDrop;
                        VPBConfig.Instance.HoldToLaunchEnabled = true;
                    }
                    else
                    {
                        VPBConfig.Instance.HoldToLaunchEnabled = false;
                    }
                    VPBConfig.Instance.Save(false);
                }
            }
            catch { }
            UpdateHoldToLaunchToggleUI();
            try { UpdateApplyModeButtonState(); } catch { }
            try { RefreshModeAmbientChrome(); } catch { }
            try { if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true); } catch { }
            ShowTemporaryStatus(
                holdToLaunchEnabled
                    ? VPBTranslation.T(
                        "gallery.hold.enabled",
                        "Hold-launch ON — drag-drop paused. Hold thumbnail to apply.")
                    : VPBTranslation.T(
                        "gallery.hold.disabled",
                        "Hold-launch OFF — drag-drop restored if enabled in Settings."),
                2f);
        }

        private void UpdateHoldToLaunchToggleUI()
        {
            if (footerHoldToLaunchToggleBtnImage != null)
            {
                // Toggle color: green when enabled, dim when disabled
                footerHoldToLaunchToggleBtnImage.color = holdToLaunchEnabled
                    ? new Color(0.12f, 0.55f, 0.18f, 0.85f)
                    : GalleryUiColorTokens.ChromeIconWell;
            }
            if (footerHoldToLaunchToggleIconImage != null)
            {
                // Swap icons (hold / hold_off) when available
                try
                {
                    Sprite s = holdToLaunchEnabled ? footerHoldToLaunchOnSprite : footerHoldToLaunchOffSprite;
                    if (s != null) UI.SetIconSprite(footerHoldToLaunchToggleIconImage, s);
                }
                catch { }

                footerHoldToLaunchToggleIconImage.color = holdToLaunchEnabled
                    ? new Color(1f, 1f, 1f, 1f)
                    : UI.White(0.45f);
            }
        }

        private void UpdateSpringScrollButtonToggleUI()
        {
            // If toggle is ON but the GO was lost (e.g. language/UI rebuild), recreate it.
            if (springScrollButtonEnabled && springScrollButtonGO == null)
            {
                try { EnsureSpringScrollButtonExists(); } catch { }
            }

            // Resize + offset to match fixed vs floating mode
            try
            {
                if (springScrollButtonGO != null)
                    ApplySpringScrollButtonScale(ChromeScale);
                LayoutScrollbarJumpButtons();
            }
            catch { }
        }

        private void CreateHoverPreviewOverlay(GameObject parentGO)
        {
            if (hoverPreviewGO != null) return;
            // Parent to canvas so position stays put across left/right/top dock (not gallery pane rect).
            GameObject host = null;
            try { if (canvas != null) host = canvas.gameObject; } catch { host = null; }
            if (host == null) host = parentGO;
            if (host == null) return;

            hoverPreviewGO = new GameObject("HoverPreview");
            hoverPreviewGO.transform.SetParent(host.transform, false);
            hoverPreviewRT = hoverPreviewGO.AddComponent<RectTransform>();
            hoverPreviewRT.anchorMin = new Vector2(0f, 0f);
            hoverPreviewRT.anchorMax = new Vector2(0f, 0f);
            hoverPreviewRT.pivot = new Vector2(0f, 0f);

            hoverPreviewBgImage = UI.AddImage(hoverPreviewGO, new Color(0f, 0f, 0f, 0.55f), false);

            var imgGO = UI.CreateChildRT(hoverPreviewGO, "Image", AnchorPresets.stretchAll);
            var rt = imgGO.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(4f, 4f);
            rt.offsetMax = new Vector2(-4f, -4f);

            hoverPreviewImage = imgGO.AddComponent<RawImage>();
            hoverPreviewImage.color = new Color(1f, 1f, 1f, 1f);
            hoverPreviewImage.raycastTarget = false;

            Text hint = UI.CreateLabel(
                hoverPreviewGO,
                VPBTranslation.T(
                    "settings.hover_preview_drag_label",
                    "Drag to position\nScroll to change size"),
                GalleryUiDesignTokens.FontCaptionRef,
                new Color(1f, 1f, 1f, 0.95f),
                TextAnchor.MiddleCenter,
                raycastTarget: false,
                name: "DragHint");
            hoverPreviewHintText = hint;
            if (hint != null)
            {
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                hint.verticalOverflow = VerticalWrapMode.Overflow;
                hint.gameObject.SetActive(false);
            }

            var drag = hoverPreviewGO.AddComponent<HoverPreviewPlaceholderDragHandler>();
            drag.Panel = this;

            hoverPreviewGO.SetActive(false);
        }

        private bool CanShowHoverPreviewForLayout(GalleryLayoutMode mode)
        {
            if (VPBConfig.Instance == null) return false;
            string m = VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode);
            if (string.Equals(m, "Off", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(m, "Both", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(m, "List", StringComparison.OrdinalIgnoreCase)) return mode == GalleryLayoutMode.List;
            if (string.Equals(m, "Grid", StringComparison.OrdinalIgnoreCase)) return mode == GalleryLayoutMode.Grid;
            return mode == GalleryLayoutMode.List;
        }

        public void NotifyHoverPreviewTriggerEntered(UIHoverPreviewTrigger source, FileEntry file)
        {
            // Settings placeholder owns the overlay — do not let recycled thumbs steal it / clear raycasts.
            if (internalSettingsSessionActive && hoverPreviewDummyActive) return;
            if (source != null) hoverPreviewSource = source;
            ShowHoverPreview(file);
        }

        public void NotifyHoverPreviewTriggerExited(UIHoverPreviewTrigger source)
        {
            if (internalSettingsSessionActive && hoverPreviewDummyActive) return;
            if (source != null && hoverPreviewSource != null && !ReferenceEquals(source, hoverPreviewSource)) return;
            hoverPreviewSource = null;
            HideHoverPreview(null);
        }

        public void ShowHoverPreview(FileEntry file)
        {
            if (TboxRandomPreviewIsShowing()) return;
            if (internalSettingsSessionActive && hoverPreviewDummyActive) return;
            if (!CanShowHoverPreviewForLayout(layoutMode)) { HideHoverPreview(null); return; }
            if (file == null) { HideHoverPreview(null); return; }
            if (hoverPreviewGO == null || hoverPreviewRT == null || hoverPreviewImage == null) return;

            hoverPreviewDummyActive = false;
            hoverPreviewFile = file;
            SyncHoverPreviewRaycast();
            UpdateHoverPreviewLayout();
            hoverPreviewGO.SetActive(true);
            try { hoverPreviewGO.transform.SetAsLastSibling(); } catch { }

            hoverPreviewImage.color = Color.white;
            LoadThumbnail(file, hoverPreviewImage, gridThumbnailContext: false, turboJpegThumbnailDenom: 1, thumbnailUnityDecodeOnly: true);
        }

        public void HideHoverPreview(FileEntry file)
        {
            if (hoverPreviewGO == null) return;
            if (file != null && hoverPreviewFile != null && !ReferenceEquals(file, hoverPreviewFile)) return;
            hoverPreviewFile = null;
            UIHoverPreviewTrigger src = hoverPreviewSource;
            hoverPreviewSource = null;
            // Panel cleared preview while EventSystem may still be over the thumb — drop local
            // hover flag so a later enter can show again (VR false-stale recovery).
            try { if (src != null) src.SyncHoverFlagAfterPanelHide(); } catch { }
            if (!hoverPreviewDummyActive)
            {
                hoverPreviewGO.SetActive(false);
                SyncHoverPreviewRaycast();
            }
        }

        private void ValidateHoverPreviewActive()
        {
            if (hoverPreviewFile == null || hoverPreviewDummyActive) return;
            if (!CanShowHoverPreviewForLayout(layoutMode))
            {
                HideHoverPreview(null);
                return;
            }

            bool stale = isCollapsed
                || hoverPreviewSource == null
                || !hoverPreviewSource.isActiveAndEnabled
                || !hoverPreviewSource.IsHovering;

            if (!stale)
            {
                // VR: RectangleContainsScreenPoint + worldCamera disagree with VaM laser screen
                // coords — false stale killed every cell except one lucky rect (#76). Trust
                // EventSystem enter/exit; if raycast is valid, only dismiss when hit left the cell.
                bool vr = false;
                try { vr = XrUtils.IsVrActive(); } catch { }

                if (vr)
                {
                    try
                    {
                        if (currentPointerData != null && currentPointerData.pointerCurrentRaycast.isValid)
                        {
                            GameObject hit = currentPointerData.pointerCurrentRaycast.gameObject;
                            if (hit != null && !IsPointerOverHoverPreviewCell(hoverPreviewSource, hit))
                                stale = true;
                        }
                    }
                    catch { }
                }
                else
                {
                    Vector2 ptr;
                    if (TryGetUiPointerScreenPosition(out ptr))
                    {
                        if (!hoverPreviewSource.ContainsScreenPoint(ptr))
                            stale = true;
                        else
                        {
                            try { stale = !IsPointerInsideGalleryWindowRect(); } catch { }
                        }
                    }
                }
            }

            if (stale) HideHoverPreview(null);
        }

        /// <summary>
        /// True when EventSystem hit is the hover-preview thumb or another graphic on the same
        /// file cell (rating badge, root Image, etc.). Avoids screen-rect tests in VR.
        /// </summary>
        private static bool IsPointerOverHoverPreviewCell(UIHoverPreviewTrigger source, GameObject hit)
        {
            if (source == null || hit == null) return false;
            Transform src = source.transform;
            Transform ht = hit.transform;
            if (ht == src || ht.IsChildOf(src) || src.IsChildOf(ht)) return true;

            Transform cell = src.parent;
            while (cell != null)
            {
                if (cell.GetComponent<RecyclingGridItem>() != null
                    || cell.GetComponent<FileButtonBinder>() != null)
                    break;
                // List/grid file button root usually has UIHoverReveal + Button together.
                if (cell.GetComponent<UIHoverReveal>() != null && cell.GetComponent<Button>() != null)
                    break;
                cell = cell.parent;
            }
            if (cell == null) return false;
            return ht == cell || ht.IsChildOf(cell);
        }

        public void SetHoverPreviewDummyActive(bool active)
        {
            hoverPreviewDummyActive = active;
            if (!active) hoverPreviewDragging = false;
            if (!active && hoverPreviewGO != null && hoverPreviewFile == null)
                hoverPreviewGO.SetActive(false);
            SyncHoverPreviewRaycast();
            UpdateHoverPreviewLayout();
            if (active && hoverPreviewGO != null)
            {
                try { hoverPreviewGO.transform.SetAsLastSibling(); } catch { }
            }
        }

        public void RefreshHoverPreviewLayoutImmediate()
        {
            UpdateHoverPreviewLayout();
        }

        private void SyncHoverPreviewRaycast()
        {
            // Only the settings placeholder captures clicks for drag; live hover must not steal list rays.
            bool want = hoverPreviewDummyActive;
            if (hoverPreviewBgImage != null) hoverPreviewBgImage.raycastTarget = want;
            if (hoverPreviewHintText != null)
                hoverPreviewHintText.gameObject.SetActive(want);
        }

        private void UpdateHoverPreviewLayout()
        {
            if (hoverPreviewRT == null) return;
            if (hoverPreviewDragging) return;

            if (hoverPreviewGO != null)
            {
                bool layoutAllows = CanShowHoverPreviewForLayout(layoutMode);
                bool settingsDummyTune = false;
                try
                {
                    settingsDummyTune = internalSettingsSessionActive && hoverPreviewDummyActive && VPBConfig.Instance != null
                        && !string.Equals(VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode), "Off", StringComparison.OrdinalIgnoreCase);
                }
                catch { }

                bool shouldBeVisible = (layoutAllows || settingsDummyTune) && (hoverPreviewFile != null || hoverPreviewDummyActive);
                if (!shouldBeVisible)
                {
                    hoverPreviewGO.SetActive(false);
                    SyncHoverPreviewRaycast();
                    return;
                }
            }

            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            float size = 300f;
            float ox = 0f;
            float oy = 0f;
            if (VPBConfig.Instance != null)
            {
                size = Mathf.Clamp(VPBConfig.Instance.GalleryListHoverPreviewSize, VPBConfig.GalleryHoverPreviewSizeMin, VPBConfig.GalleryHoverPreviewSizeMax);
                ox = Mathf.Clamp(VPBConfig.Instance.GalleryListHoverPreviewOffsetX, -4000f, 4000f);
                oy = Mathf.Clamp(VPBConfig.Instance.GalleryListHoverPreviewOffsetY, -4000f, 4000f);
            }

            // Stationary canvas-local position. Default corner (20,12) + user offsets.
            float x = (20f + ox) * s;
            float y = (12f + oy) * s;
            hoverPreviewRT.sizeDelta = new Vector2(size, size);
            hoverPreviewRT.anchoredPosition = new Vector2(x, y);
            hoverPreviewRT.localRotation = Quaternion.identity;
            hoverPreviewRT.localScale = Vector3.one;

            if (hoverPreviewGO != null)
            {
                hoverPreviewGO.SetActive(true);
                try
                {
                    Transform hpParent = hoverPreviewGO.transform.parent;
                    if (hpParent != null
                        && hoverPreviewGO.transform.GetSiblingIndex() != hpParent.childCount - 1)
                        hoverPreviewGO.transform.SetAsLastSibling();
                }
                catch { }
            }

            SyncHoverPreviewRaycast();

            if (hoverPreviewImage != null && hoverPreviewDummyActive && hoverPreviewFile == null)
            {
                hoverPreviewImage.texture = null;
                hoverPreviewImage.color = new Color(1f, 1f, 1f, 0.18f);
            }
            else if (hoverPreviewImage != null && hoverPreviewFile != null)
            {
                if (hoverPreviewImage.color.a < 0.95f) hoverPreviewImage.color = Color.white;
            }

            if (hoverPreviewHintText != null && hoverPreviewDummyActive)
            {
                bool vrHint = false;
                try { vrHint = XrUtils.IsVrActive(); } catch { }
                hoverPreviewHintText.text = vrHint
                    ? VPBTranslation.T(
                        "settings.hover_preview_drag_label_vr",
                        "Drag to position\nThumbstick to change size")
                    : VPBTranslation.T(
                        "settings.hover_preview_drag_label",
                        "Drag to position\nScroll to change size");
                try
                {
                    GalleryUiMetrics.ApplyFont(
                        hoverPreviewHintText,
                        GalleryUiDesignTokens.FontCaptionRef,
                        s,
                        GalleryUiDesignTokens.FontMinRef);
                }
                catch { }
            }
        }

        /// <summary>
        /// VR settings placeholder: thumbstick changes preview size when laser is on the dummy.
        /// Returns true when consumed (caller should not scroll lists).
        /// </summary>
        internal bool TryApplyVrThumbstickHoverPreviewSize(float stickForward)
        {
            if (!hoverPreviewDummyActive || hoverPreviewGO == null || VPBConfig.Instance == null) return false;
            if (!hoverPreviewGO.activeInHierarchy) return false;

            if (currentPointerData == null || !currentPointerData.pointerCurrentRaycast.isValid)
                return false;
            GameObject hit = currentPointerData.pointerCurrentRaycast.gameObject;
            if (hit == null) return false;
            Transform ht = hit.transform;
            if (ht != hoverPreviewGO.transform && !ht.IsChildOf(hoverPreviewGO.transform))
                return false;

            const float deadzone = 0.12f;
            if (Mathf.Abs(stickForward) <= deadzone) return false;

            float mag = Mathf.Clamp01((Mathf.Abs(stickForward) - deadzone) / Mathf.Max(0.0001f, 1f - deadzone));
            float step = 10f * mag * (60f * Time.unscaledDeltaTime);
            if (step < 0.5f) step = 0.5f;
            float size = VPBConfig.Instance.GalleryListHoverPreviewSize;
            float next = Mathf.Clamp(size + (stickForward > 0f ? step : -step), VPBConfig.GalleryHoverPreviewSizeMin, VPBConfig.GalleryHoverPreviewSizeMax);
            if (Mathf.Abs(next - size) < 0.01f) return true;
            VPBConfig.Instance.GalleryListHoverPreviewSize = next;
            if (hoverPreviewRT != null)
                hoverPreviewRT.sizeDelta = new Vector2(next, next);
            hoverPreviewSuppressSettingsClick = true;
            return true;
        }

        private void CommitHoverPreviewPosFromAnchored(Vector2 anchored)
        {
            if (VPBConfig.Instance == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float ox = anchored.x / s - 20f;
            float oy = anchored.y / s - 12f;
            VPBConfig.Instance.GalleryListHoverPreviewOffsetX = Mathf.Clamp(ox, -4000f, 4000f);
            VPBConfig.Instance.GalleryListHoverPreviewOffsetY = Mathf.Clamp(oy, -4000f, 4000f);
        }

        internal void HoverPreviewPlaceholderBeginDrag(PointerEventData eventData)
        {
            if (!hoverPreviewDummyActive || hoverPreviewRT == null || eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            RectTransform parentRT = hoverPreviewRT.parent as RectTransform;
            if (parentRT == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRT, eventData.position, eventData.pressEventCamera, out local))
                return;
            hoverPreviewDragGrabLocal = local - hoverPreviewRT.anchoredPosition;
            hoverPreviewDragging = true;
            hoverPreviewSuppressSettingsClick = true;
        }

        internal void HoverPreviewPlaceholderDrag(PointerEventData eventData)
        {
            if (!hoverPreviewDragging || !hoverPreviewDummyActive || hoverPreviewRT == null || eventData == null) return;
            RectTransform parentRT = hoverPreviewRT.parent as RectTransform;
            if (parentRT == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRT, eventData.position, eventData.pressEventCamera, out local))
                return;
            Vector2 pos = local - hoverPreviewDragGrabLocal;
            hoverPreviewRT.anchoredPosition = pos;
            CommitHoverPreviewPosFromAnchored(pos);
            hoverPreviewSuppressSettingsClick = true;
        }

        internal void HoverPreviewPlaceholderEndDrag(PointerEventData eventData)
        {
            if (!hoverPreviewDragging) return;
            hoverPreviewDragging = false;
            hoverPreviewSuppressSettingsClick = true;
            // Do not TriggerChange / RefreshInternalSettingsListRows — that rebuilds settings rows
            // and feels like scaling/rearrange while placing the preview.
        }

        internal void HoverPreviewPlaceholderScroll(PointerEventData eventData)
        {
            if (!hoverPreviewDummyActive || eventData == null || VPBConfig.Instance == null) return;
            hoverPreviewSuppressSettingsClick = true;
            int notches = VpbScrollTuning.TakeNotches(ref hoverPreviewScrollNotchAccum, eventData.scrollDelta.y);
            if (notches == 0) return;

            const float step = 10f;
            float size = VPBConfig.Instance.GalleryListHoverPreviewSize;
            size = Mathf.Clamp(size + (notches > 0 ? step : -step), VPBConfig.GalleryHoverPreviewSizeMin, VPBConfig.GalleryHoverPreviewSizeMax);
            if (Mathf.Abs(size - VPBConfig.Instance.GalleryListHoverPreviewSize) < 0.01f) return;
            VPBConfig.Instance.GalleryListHoverPreviewSize = size;
            if (hoverPreviewRT != null)
                hoverPreviewRT.sizeDelta = new Vector2(size, size);
        }

        internal void HoverPreviewPlaceholderPointerDown(PointerEventData eventData)
        {
            if (!hoverPreviewDummyActive) return;
            hoverPreviewSuppressSettingsClick = true;
        }

        internal void HoverPreviewPlaceholderClick(PointerEventData eventData)
        {
            if (!hoverPreviewDummyActive) return;
            hoverPreviewSuppressSettingsClick = true;
        }

        private sealed class HoverPreviewPlaceholderDragHandler : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler,
            IPointerDownHandler, IPointerClickHandler, IScrollHandler
        {
            public GalleryPanel Panel;

            public void OnPointerDown(PointerEventData eventData)
            {
                try { if (Panel != null) Panel.HoverPreviewPlaceholderPointerDown(eventData); } catch { }
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                try { if (Panel != null) Panel.HoverPreviewPlaceholderClick(eventData); } catch { }
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                try { if (Panel != null) Panel.HoverPreviewPlaceholderBeginDrag(eventData); } catch { }
            }

            public void OnDrag(PointerEventData eventData)
            {
                try { if (Panel != null) Panel.HoverPreviewPlaceholderDrag(eventData); } catch { }
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                try { if (Panel != null) Panel.HoverPreviewPlaceholderEndDrag(eventData); } catch { }
            }

            public void OnScroll(PointerEventData eventData)
            {
                try { if (Panel != null) Panel.HoverPreviewPlaceholderScroll(eventData); } catch { }
            }
        }

        public void UpdatePaginationText()
        {
            {
                // Package dep/dependent filter chrome lives in ActiveFilterChipBar (not footer/toolbox).
                if (footerBackBtn != null) footerBackBtn.SetActive(false);
                if (footerClearFilterBtn != null) footerClearFilterBtn.SetActive(false);
                if (footerFilterModeText != null) footerFilterModeText.gameObject.SetActive(false);
                if (footerFilterModeSpacerGO != null) footerFilterModeSpacerGO.SetActive(false);

                // Keep the hover-path count fallback in sync with filter/search refreshes.
                try { RefreshHoverPathCountTextIfNeeded(); } catch { }

                if (tboxSelectAllBtn != null)
                {
                    Button sab = tboxSelectAllBtn.GetComponent<Button>();
                    if (sab != null)
                    {
                        int totalForSelectAll = currentFilteredFiles != null ? currentFilteredFiles.Count : 0;
                        sab.interactable = totalForSelectAll > 0 && totalForSelectAll <= SelectAllSafetyMaxItemCount;
                    }
                }
            }
        }


        private void ToggleLayoutMode()
        {
            var next = (layoutMode == GalleryLayoutMode.Grid) ? GalleryLayoutMode.List : GalleryLayoutMode.Grid;
            SetLayoutMode(next);
        }

        private void UpdateFooterLayoutState()
        {
            Color activeColor = UI.AccentBlue;
            Color inactiveColor = GalleryUiColorTokens.ChromeIconWell;

            if (footerLayoutBtnImage != null)
            {
                footerLayoutBtnImage.color = (layoutMode == GalleryLayoutMode.List) ? activeColor : inactiveColor;
            }

            if (footerLayoutBtnText != null)
            {
                footerLayoutBtnText.text = (layoutMode == GalleryLayoutMode.List) ? "≡" : "▤";
            }

            if (footerLayoutIconImage != null)
            {
                Sprite target = (layoutMode == GalleryLayoutMode.List) ? footerLayoutGridSprite : footerLayoutListSprite;
                if (target != null) UI.SetIconSprite(footerLayoutIconImage, target);
            }

            if (footerLayoutBtn != null)
            {
                var del = footerLayoutBtn.GetComponent<UIHoverDelegate>();
                if (del != null) del.OnHoverChange = null;
                bool blockGridWhileSettings = settingsListViewActive;
                var b = footerLayoutBtn.GetComponent<Button>();
                if (b != null) b.interactable = !blockGridWhileSettings;
                string modeText = blockGridWhileSettings
                    ? "Grid layout unavailable while Settings is open"
                    : ((layoutMode == GalleryLayoutMode.List) ? "Toggle Grid Layout Mode" : "Toggle List Layout Mode");
                AddTooltipPlain(footerLayoutBtn, modeText);
            }
        }

        private void ToggleFixedHeightMode()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.DesktopFixedHeightMode = (VPBConfig.Instance.DesktopFixedHeightMode + 1) % 2;
            VPBConfig.Instance.Save();
            UpdateFooterHeightState();
            InvalidateFooterOverflowLayout();
            UpdateLayout();
        }

        private void UpdateFooterHeightState()
        {
            if (VPBConfig.Instance == null) return;

            Color activeColor = UI.AccentBlue;
            Color inactiveColor = GalleryUiColorTokens.ChromeIconWell;

            if (footerHeightBtnImage != null)
                footerHeightBtnImage.color = VPBConfig.Instance.DesktopFixedHeightMode > 0 ? activeColor : inactiveColor;

            if (footerHeightBtnText != null)
            {
                switch(VPBConfig.Instance.DesktopFixedHeightMode)
                {
                    case 0: footerHeightBtnText.text = VPBTranslation.T("gallery.footer.height_h1", "H1"); break;
                    case 1: footerHeightBtnText.text = VPBTranslation.T("gallery.footer.height_hc", "HC"); break;
                }
            }

            if (footerHeightIconImage != null)
            {
                Sprite target = VPBConfig.Instance.DesktopFixedHeightMode > 0 ? footerHeightFreeSprite : footerHeightFixedSprite;
                if (target != null) UI.SetIconSprite(footerHeightIconImage, target);
            }

            if (footerHeightBtn != null)
            {
                var del = footerHeightBtn.GetComponent<UIHoverDelegate>();
                if (del != null) del.OnHoverChange = null;
                string modeText = VPBConfig.Instance.DesktopFixedHeightMode > 0 ? "Toggle Adjustable Height Mode" : "Toggle Full Height Mode";
                AddTooltipPlain(footerHeightBtn, modeText);
            }
        }

        private void ToggleGalleryShowHiddenPackages()
        {
            // Quick-menu / overflow: cycle Off → Show → Only → Off (Filter menu uses visible 3-way).
            BrowseFilterCycle next = BrowseFilterCycle.Off;
            if (_browseHiddenCycle == BrowseFilterCycle.Off)
                next = BrowseFilterCycle.Apply;
            else if (_browseHiddenCycle == BrowseFilterCycle.Apply)
                next = BrowseFilterCycle.Only;
            SetBrowseHiddenCycle(next, refresh: true);
        }

        private void ToggleAutoHideMode()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.DesktopFixedAutoCollapse = !VPBConfig.Instance.DesktopFixedAutoCollapse;
            VPBConfig.Instance.Save();
            UpdateFooterAutoHideState();
            UpdateLayout();
        }

        private void ToggleVamMenuGateMode()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible = !VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible;
            VPBConfig.Instance.Save();
            UpdateFooterVamMenuGateState();
            try { ApplyVamMenuGateVisibility(); } catch { }
        }

        private void UpdateFooterVamMenuGateState()
        {
            if (VPBConfig.Instance == null) return;
            Color activeColor = UI.AccentBlue;
            Color inactiveColor = GalleryUiColorTokens.ChromeIconWell;
            if (footerMenuGateBtnImage != null)
                footerMenuGateBtnImage.color = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible ? activeColor : inactiveColor;
            if (footerMenuGateIconImage != null)
            {
                Sprite target = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible ? footerMenuGateOnSprite : footerMenuGateOffSprite;
                if (target != null) UI.SetIconSprite(footerMenuGateIconImage, target);
            }
        }

        private void ToggleVrWatchVisible()
        {
            if (VPBConfig.Instance == null) return;
            if (!XrUtils.IsVrActive()) return;

            bool shown = false;
            try
            {
                if (VamHookPlugin.singleton != null)
                    shown = VamHookPlugin.singleton.QuickMenuIsWatchShown();
            }
            catch { }

            // Config can already be ON while Glance never showed the face (Index / OpenVR).
            // First click then must SHOW, not flip the flag off.
            bool on = !shown;
            VPBConfig.Instance.QuickMenuVrWatchVisible = on;
            VPBConfig.Instance.Save();
            UpdateFooterVrWatchState();
            try
            {
                if (VamHookPlugin.singleton != null)
                    VamHookPlugin.singleton.QuickMenuOnWatchVisibleToggled(on);
            }
            catch { }
        }

        internal void NotifyFooterVrWatchState()
        {
            UpdateFooterVrWatchState();
        }

        private void UpdateFooterVrWatchState()
        {
            bool isVR = XrUtils.IsVrActive();
            bool watchCollapsed = false;
            try { watchCollapsed = _footerOverflowCollapsed != null && _footerOverflowCollapsed.Contains(footerWatchToggleBtn); } catch { }
            bool showWatch = isVR && !watchCollapsed;
            if (footerWatchToggleBtn != null && footerWatchToggleBtn.activeSelf != showWatch)
                footerWatchToggleBtn.SetActive(showWatch);
            if (!isVR || VPBConfig.Instance == null) return;

            Color activeColor = UI.AccentBlue;
            Color inactiveColor = GalleryUiColorTokens.ChromeIconWell;
            bool on = VPBConfig.Instance.QuickMenuVrWatchVisible;
            if (footerWatchToggleBtnImage != null)
                footerWatchToggleBtnImage.color = on ? activeColor : inactiveColor;
            if (footerWatchToggleIconImage != null)
            {
                Sprite target = on ? footerWatchToggleOnSprite : footerWatchToggleOffSprite;
                if (target != null) UI.SetIconSprite(footerWatchToggleIconImage, target);
            }
        }

        private void GetFooterAutoHideSpritesForCurrentDock(out Sprite offSprite, out Sprite onSprite)
        {
            offSprite = footerAutoHideOffSprite;
            onSprite = footerAutoHideOnSprite;
            if (VPBConfig.Instance == null) return;
            string side = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);
            try
            {
                if (VPBConfig.Instance.DesktopFixedEnforceDockSide)
                    side = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedEnforcedDockSide);
            }
            catch { }
            if (string.Equals(side, "Left", StringComparison.OrdinalIgnoreCase))
            {
                offSprite = footerAutoHideLeftOffSprite ?? offSprite;
                onSprite = footerAutoHideLeftOnSprite ?? onSprite;
            }
            else if (string.Equals(side, "Top", StringComparison.OrdinalIgnoreCase))
            {
                offSprite = footerAutoHideTopOffSprite ?? offSprite;
                onSprite = footerAutoHideTopOnSprite ?? onSprite;
            }
            else
            {
                offSprite = footerAutoHideRightOffSprite ?? offSprite;
                onSprite = footerAutoHideRightOnSprite ?? onSprite;
            }
        }

        private void UpdateFooterAutoHideState()
        {
            if (VPBConfig.Instance == null) return;

            Color activeColor = UI.AccentBlue;
            Color inactiveColor = GalleryUiColorTokens.ChromeIconWell;

            if (footerAutoHideBtnImage != null)
                footerAutoHideBtnImage.color = VPBConfig.Instance.DesktopFixedAutoCollapse ? activeColor : inactiveColor;

            if (footerAutoHideBtnText != null)
            {
                footerAutoHideBtnText.text = VPBConfig.Instance.DesktopFixedAutoCollapse
                    ? VPBTranslation.T("gallery.footer.autohide_on", "AH")
                    : VPBTranslation.T("gallery.footer.autohide_off", "AO");
            }

            if (footerAutoHideIconImage != null)
            {
                GetFooterAutoHideSpritesForCurrentDock(out Sprite dockOff, out Sprite dockOn);
                Sprite target = VPBConfig.Instance.DesktopFixedAutoCollapse ? dockOn : dockOff;
                if (target != null) UI.SetIconSprite(footerAutoHideIconImage, target);
            }

            if (footerAutoHideBtn != null)
            {
                var del = footerAutoHideBtn.GetComponent<UIHoverDelegate>();
                if (del != null) del.OnHoverChange = null;
                AddTooltipPlain(
                    footerAutoHideBtn,
                    VPBConfig.Instance.DesktopFixedAutoCollapse
                        ? VPBTranslation.T(
                            "gallery.tooltip.autohide_enabled",
                            "Auto-Hide on — collapses when idle (pointer away, no text focus)")
                        : VPBTranslation.T("gallery.tooltip.autohide_disabled", "Auto-Hide off — stays expanded"));
            }
        }

        private void ToggleFollowQuick(string type)
        {
            if (VPBConfig.Instance == null) return;
            
            if (type == "Angle") {
                VPBConfig.Instance.FollowAngle = (VPBConfig.Instance.FollowAngle == "Off") ? "Both" : "Off";
            } else if (type == "Distance") {
                VPBConfig.Instance.FollowDistance = (VPBConfig.Instance.FollowDistance == "Off") ? "Both" : "Off";
            } else if (type == "Height") {
                VPBConfig.Instance.FollowEyeHeight = (VPBConfig.Instance.FollowEyeHeight == "Off") ? "Both" : "Off";
            }
            
            VPBConfig.Instance.TriggerChange();
            UpdateFooterFollowStates();
        }

        private void UpdateFooterFollowStates()
        {
            if (VPBConfig.Instance == null) return;
            
            Color activeColor = UI.AccentBlue;
            Color inactiveColor = GalleryUiColorTokens.ChromeIconWell;
            
            if (footerFollowAngleImage != null)
                footerFollowAngleImage.color = VPBConfig.Instance.FollowAngle != "Off" ? activeColor : inactiveColor;
                
            if (footerFollowDistanceImage != null)
                footerFollowDistanceImage.color = VPBConfig.Instance.FollowDistance != "Off" ? activeColor : inactiveColor;
                
            if (footerFollowHeightImage != null)
                footerFollowHeightImage.color = VPBConfig.Instance.FollowEyeHeight != "Off" ? activeColor : inactiveColor;
        }

        private void AddTooltip(GameObject go, string tooltipKey, string englishDefault)
        {
            if (go == null) return;
            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.TooltipAttach++;

            if (del.TooltipHandler != null) del.OnHoverChange -= del.TooltipHandler;
            Action<bool> handler = (enter) =>
            {
                if (enter)
                    SetHoverTooltip(VPBTranslation.T(tooltipKey, englishDefault), go);
                else
                    ClearHoverTooltip(go);
            };
            del.TooltipHandler = handler;
            del.OnHoverChange += handler;

            if (del.TooltipPointerEnterHandler != null)
                del.OnPointerEnterEvent -= del.TooltipPointerEnterHandler;
            Action<PointerEventData> pe = (d) => { currentPointerData = d; };
            del.TooltipPointerEnterHandler = pe;
            del.OnPointerEnterEvent += pe;
        }

        // Like AddTooltipPlain but the text is computed at hover time via the provider, so it can show
        // live details (version, loaded package count, memory, etc.). Snapshot is taken on hover-enter.
        // Provider must stay cheap: no sync ZIP/SQL/full package list materialization on enter.
        private void AddDynamicTooltip(GameObject go, Func<string> provider)
        {
            if (go == null || provider == null) return;
            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.TooltipAttach++;

            if (del.TooltipHandler != null) del.OnHoverChange -= del.TooltipHandler;
            Action<bool> handler = (enter) =>
            {
                if (enter)
                {
                    string msg;
                    try { msg = provider(); } catch { msg = null; }
                    if (!string.IsNullOrEmpty(msg)) SetHoverTooltip(msg, go);
                }
                else
                    ClearHoverTooltip(go);
            };
            del.TooltipHandler = handler;
            del.OnHoverChange += handler;

            if (del.TooltipPointerEnterHandler != null)
                del.OnPointerEnterEvent -= del.TooltipPointerEnterHandler;
            Action<PointerEventData> pe = (d) => { currentPointerData = d; };
            del.TooltipPointerEnterHandler = pe;
            del.OnPointerEnterEvent += pe;
        }

        private void AddTooltipPlain(GameObject go, string tooltip)
        {
            if (go == null) return;
            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.TooltipAttach++;

            if (del.TooltipHandler != null) del.OnHoverChange -= del.TooltipHandler;
            Action<bool> handler = (enter) =>
            {
                if (enter)
                    SetHoverTooltip(tooltip, go);
                else
                    ClearHoverTooltip(go);
            };
            del.TooltipHandler = handler;
            del.OnHoverChange += handler;

            if (del.TooltipPointerEnterHandler != null)
                del.OnPointerEnterEvent -= del.TooltipPointerEnterHandler;
            Action<PointerEventData> pe = (d) => { currentPointerData = d; };
            del.TooltipPointerEnterHandler = pe;
            del.OnPointerEnterEvent += pe;
        }

        private void UpdateDesktopModeButton()
        {
            if (VPBConfig.Instance == null) return;

            bool isVR = XrUtils.IsVrActive();

            bool fixedMode = isFixedLocally;
            string text = fixedMode
                ? VPBTranslation.T("gallery.desktop.floating", "Floating")
                : VPBTranslation.T("gallery.desktop.fixed", "Fixed");
            Color color = fixedMode ? UI.AccentBlue : UI.ChromeDark;
            Sprite deskSpr = fixedMode ? galleryFloatSprite : galleryFixedSprite;

            GameObject rightDeskGo = rightDesktopModeBtnImage != null ? rightDesktopModeBtnImage.gameObject : null;
            if (rightDeskGo != null) rightDeskGo.SetActive(!isVR);

            if (rightDesktopModeBtnIconImage != null && deskSpr != null)
            {
                UI.SetIconSprite(rightDesktopModeBtnIconImage, deskSpr);
                rightDesktopModeBtnIconImage.enabled = true;
                if (rightDesktopModeBtnText != null) rightDesktopModeBtnText.gameObject.SetActive(false);
            }
            else if (rightDesktopModeBtnText != null)
            {
                rightDesktopModeBtnText.gameObject.SetActive(true);
                rightDesktopModeBtnText.text = text;
            }

            if (rightDesktopModeBtnImage != null) rightDesktopModeBtnImage.color = color;

            GameObject leftDeskGo = leftDesktopModeBtnImage != null ? leftDesktopModeBtnImage.gameObject : null;
            if (leftDeskGo != null) leftDeskGo.SetActive(!isVR);

            if (leftDesktopModeBtnIconImage != null && deskSpr != null)
            {
                UI.SetIconSprite(leftDesktopModeBtnIconImage, deskSpr);
                leftDesktopModeBtnIconImage.enabled = true;
                if (leftDesktopModeBtnText != null) leftDesktopModeBtnText.gameObject.SetActive(false);
            }
            else if (leftDesktopModeBtnText != null)
            {
                leftDesktopModeBtnText.gameObject.SetActive(true);
                leftDesktopModeBtnText.text = text;
            }

            if (leftDesktopModeBtnImage != null) leftDesktopModeBtnImage.color = color;

            ApplyFooterModeButtonVisibility();

            SyncSideFollowRailButtonsVisibility();

            UpdateSideButtonPositions();
            UpdateFooterDockButtonState();
            UpdateFooterAutoHideState();
            try { UpdateFooterVrWatchState(); } catch { }
        }

        private void UpdateFooterDockButtonState()
        {
            if (footerDockIconImage == null || VPBConfig.Instance == null) return;
            string side = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);
            try
            {
                if (VPBConfig.Instance.DesktopFixedEnforceDockSide)
                    side = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedEnforcedDockSide);
            }
            catch { }
            Sprite spr = footerDockRightSprite;
            if (string.Equals(side, "Left", StringComparison.OrdinalIgnoreCase)) spr = footerDockLeftSprite;
            else if (string.Equals(side, "Top", StringComparison.OrdinalIgnoreCase)) spr = footerDockTopSprite;
            if (spr != null) UI.SetIconSprite(footerDockIconImage, spr);
        }

        /// <summary>Camera-follow is only meaningful when the panel is not fixed; hide side-rail follow controls in fixed mode.</summary>
        private void SyncSideFollowRailButtonsVisibility()
        {
            bool show = !isFixedLocally;
            if (show && _sideRailOverflowCollapsedIdx != null && _sideRailOverflowCollapsedIdx.Count > 0)
            {
                // Keep hidden if height-fit overflow already ate this slot.
                int followIdx = -1;
                try
                {
                    List<RectTransform> refList = rightSideButtons != null && rightSideButtons.Count > 0 ? rightSideButtons : leftSideButtons;
                    if (refList != null)
                    {
                        Text ft = rightFollowBtnText != null ? rightFollowBtnText : leftFollowBtnText;
                        if (ft != null)
                            followIdx = refList.FindIndex(rt => rt != null && rt.GetComponentInChildren<Text>(true) == ft);
                    }
                }
                catch { }
                if (followIdx >= 0 && _sideRailOverflowCollapsedIdx.Contains(followIdx))
                    show = false;
            }
            if (rightFollowBtnImage != null) rightFollowBtnImage.gameObject.SetActive(show);
            if (leftFollowBtnImage != null) leftFollowBtnImage.gameObject.SetActive(show);
        }

        private void PopulateClothingSubmenuButtons(Atom target)
        {
            // Removed - submenus are now handled by side tabs
        }

        private void ToggleClothingSubmenuFromSideButtons(Atom target, bool? forceLeftSide = null)
        {
            bool useLeftSide = forceLeftSide ?? isFixedLocally;
            CloseOtherSideIfSubmenu(useLeftSide);
            if (useLeftSide)
            {
                if (leftActiveContent == ContentType.RemoveClothing)
                {
                    leftActiveContent = leftPrevActiveContent;
                    if (_removeModeActive) _removeModeSiderailDismissed = true;
                }
                else
                {
                    leftPrevActiveContent = leftActiveContent;
                    leftActiveContent = ContentType.RemoveClothing;
                    if (_removeModeActive) _removeModeSiderailDismissed = false;
                }
            }
            else
            {
                if (rightActiveContent == ContentType.RemoveClothing)
                {
                    rightActiveContent = rightPrevActiveContent;
                    if (_removeModeActive) _removeModeSiderailDismissed = true;
                }
                else
                {
                    rightPrevActiveContent = rightActiveContent;
                    rightActiveContent = ContentType.RemoveClothing;
                    if (_removeModeActive) _removeModeSiderailDismissed = false;
                }
            }
            UpdateLayout();
            UpdateTabs();
        }

        private void CloseClothingSubmenuUI()
        {
            try
            {
                ClearClothingPreview();
                if (leftActiveContent == ContentType.RemoveClothing) leftActiveContent = leftPrevActiveContent;
                if (rightActiveContent == ContentType.RemoveClothing) rightActiveContent = rightPrevActiveContent;
                UpdateTabs();
            }
            catch { }
        }

        private void SyncClothingSubmenu(Atom target, bool keepOpenIfHasOptions)
        {
            if (target == null) { CloseClothingSubmenuUI(); return; }
            UpdateTabs();
        }

        private bool RemoveContextRowUsesIcon(GameObject btn)
        {
            if (btn == null) return false;
            if (btn == rightRemoveAllClothingBtn && rightRemoveAllClothingBtnIconImage != null) return true;
            if (btn == leftRemoveAllClothingBtn && leftRemoveAllClothingBtnIconImage != null) return true;
            if (btn == rightRemoveAllHairBtn && rightRemoveAllHairBtnIconImage != null) return true;
            if (btn == leftRemoveAllHairBtn && leftRemoveAllHairBtnIconImage != null) return true;
            return false;
        }

        private void UpdateRemoveButtonLabels(GameObject leftBtn, GameObject rightBtn, string baseLabel, int optionCount)
        {
            try
            {
                bool hasOptions = optionCount > 0;
                string suffix = hasOptions ? (" (" + optionCount.ToString() + ")") : "";

                if (leftBtn != null && !RemoveContextRowUsesIcon(leftBtn))
                {
                    Text t = leftBtn.GetComponentInChildren<Text>();
                    if (t != null) t.text = hasOptions ? ("< " + baseLabel + suffix) : baseLabel;
                }
                if (rightBtn != null && !RemoveContextRowUsesIcon(rightBtn))
                {
                    Text t = rightBtn.GetComponentInChildren<Text>();
                    if (t != null) t.text = hasOptions ? (baseLabel + " >" + suffix) : baseLabel;
                }
            }
            catch { }
        }

        private void UpdateRemoveClothingButtonLabels(int optionCount)
        {
            UpdateRemoveButtonLabels(leftRemoveAllClothingBtn, rightRemoveAllClothingBtn, "Unequip\nClothing", optionCount);
        }

        private void ApplyClothingPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;

                if (!string.IsNullOrEmpty(previewRemoveClothingAtomUid))
                {
                    if (!string.Equals(previewRemoveClothingAtomUid, target.uid, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previewRemoveClothingItemUid, itemUid, StringComparison.OrdinalIgnoreCase) ||
                        isPreviewRemoveClothingAll)
                    {
                        ClearClothingPreview();
                    }
                }

                if (!string.IsNullOrEmpty(previewRemoveClothingAtomUid))
                {
                    return;
                }

                JSONStorable geometry = null;
                try { geometry = target.GetStorableByID("geometry"); } catch { }
                if (geometry == null) return;

                JSONStorableBool active = null;
                try { active = geometry.GetBoolJSONParam("clothing:" + itemUid); } catch { }
                if (active == null) return;

                previewRemoveClothingAtomUid = target.uid;
                previewRemoveClothingItemUid = itemUid;
                previewRemoveClothingPrevGeometryVal = active.val;
                isPreviewRemoveClothingAll = false;

                if (active.val) active.val = false;
            }
            catch { }
        }

        private void ApplyClothingAllPreview(Atom target)
        {
            try
            {
                if (target == null) return;

                if (!string.IsNullOrEmpty(previewRemoveClothingAtomUid))
                {
                    if (!string.Equals(previewRemoveClothingAtomUid, target.uid, StringComparison.OrdinalIgnoreCase) || !isPreviewRemoveClothingAll)
                    {
                        ClearClothingPreview();
                    }
                }

                if (!string.IsNullOrEmpty(previewRemoveClothingAtomUid)) return;

                JSONStorable geometry = null;
                try { geometry = target.GetStorableByID("geometry"); } catch { }
                if (geometry == null) return;

                previewRemoveClothingAtomUid = target.uid;
                previewRemoveClothingItemUid = "ALL";
                isPreviewRemoveClothingAll = true;
                previewRemoveClothingAllItemUids.Clear();
                previewRemoveClothingAllPrevVals.Clear();

                foreach (var name in geometry.GetBoolParamNames())
                {
                    if (string.IsNullOrEmpty(name) || !name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                    JSONStorableBool jsb = geometry.GetBoolJSONParam(name);
                    if (jsb != null && jsb.val)
                    {
                        previewRemoveClothingAllItemUids.Add(name.Substring(9));
                        previewRemoveClothingAllPrevVals.Add(jsb.val);
                        jsb.val = false;
                    }
                }
            }
            catch { }
        }

        private void ClearClothingPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;
                if (string.IsNullOrEmpty(previewRemoveClothingAtomUid) || string.IsNullOrEmpty(previewRemoveClothingItemUid)) return;
                if (!string.Equals(previewRemoveClothingAtomUid, target.uid, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.Equals(previewRemoveClothingItemUid, itemUid, StringComparison.OrdinalIgnoreCase)) return;
                RestoreClothingPreview();
            }
            catch { }
        }

        private void ClearClothingAllPreview(Atom target)
        {
            try
            {
                if (target == null) return;
                if (string.IsNullOrEmpty(previewRemoveClothingAtomUid) || !isPreviewRemoveClothingAll) return;
                if (!string.Equals(previewRemoveClothingAtomUid, target.uid, StringComparison.OrdinalIgnoreCase)) return;
                RestoreClothingPreview();
            }
            catch { }
        }

        private void ClearClothingPreview()
        {
            try { RestoreClothingPreview(); }
            catch { }
        }

        private void RestoreClothingPreview()
        {
            try
            {
                if (string.IsNullOrEmpty(previewRemoveClothingAtomUid))
                {
                    ResetClothingPreviewFields();
                    return;
                }

                Atom atom = null;
                try { atom = SuperController.singleton.GetAtomByUid(previewRemoveClothingAtomUid); } catch { }
                if (atom == null)
                {
                    ResetClothingPreviewFields();
                    return;
                }

                JSONStorable geometry = null;
                try { geometry = atom.GetStorableByID("geometry"); } catch { }
                if (geometry != null)
                {
                    if (isPreviewRemoveClothingAll)
                    {
                        for (int i = 0; i < previewRemoveClothingAllItemUids.Count; i++)
                        {
                            try
                            {
                                JSONStorableBool jsb = geometry.GetBoolJSONParam("clothing:" + previewRemoveClothingAllItemUids[i]);
                                if (jsb != null) jsb.val = previewRemoveClothingAllPrevVals[i];
                            }
                            catch { }
                        }
                    }
                    else if (!string.IsNullOrEmpty(previewRemoveClothingItemUid))
                    {
                        JSONStorableBool active = null;
                        try { active = geometry.GetBoolJSONParam("clothing:" + previewRemoveClothingItemUid); } catch { }
                        if (active != null && previewRemoveClothingPrevGeometryVal.HasValue)
                        {
                            active.val = previewRemoveClothingPrevGeometryVal.Value;
                        }
                    }
                }

                ResetClothingPreviewFields();
            }
            catch
            {
                ResetClothingPreviewFields();
            }
        }

        private void ResetClothingPreviewFields()
        {
            previewRemoveClothingAtomUid = null;
            previewRemoveClothingItemUid = null;
            previewRemoveClothingPrevGeometryVal = null;
            isPreviewRemoveClothingAll = false;
            previewRemoveClothingAllItemUids.Clear();
            previewRemoveClothingAllPrevVals.Clear();
        }

        private void ToggleDesktopMode()
        {
            ToggleDesktopModeWithDockHint(null);
        }

        private void ToggleDesktopModeWithDockHint(string dockSideOrNull)
        {
            if (VPBConfig.Instance == null) return;

            bool isVR = XrUtils.IsVrActive();
            if (isVR)
            {
                if (isFixedLocally) SetFixedLocally(false);
                return;
            }

            // Fixed/Floating button must be a pure toggle.
            // Dock side switching handled by footer dock toggle + settings enforcement, not by which side rail was clicked.
            if (isFixedLocally)
            {
                isFixedLocally = false;
                VPBConfig.Instance.DesktopFixedMode = false;
                VPBConfig.Instance.Save();
                UpdateDesktopModeButton();
                try { UpdateSpringScrollButtonToggleUI(); } catch { }
                InvalidateFooterOverflowLayout();
                MarkGalleryPaneChromeDirty();
                UpdateLayout();
                try { SyncCategoryQuickSwitchChrome(); } catch { }
                return;
            }

            string hint = dockSideOrNull;
            if (!string.IsNullOrEmpty(hint))
                hint = VPBConfig.NormalizeDesktopFixedDockSide(hint);

            string desiredDock = null;
            try
            {
                if (VPBConfig.Instance.DesktopFixedEnforceDockSide)
                    desiredDock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedEnforcedDockSide);
            }
            catch { desiredDock = null; }
            if (string.IsNullOrEmpty(desiredDock))
            {
                desiredDock = !string.IsNullOrEmpty(hint)
                    ? hint
                    : VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDefaultDockSide);
            }

            // Only one can be fixed. Revert others.
            if (Gallery.singleton != null)
            {
                foreach (var p in Gallery.singleton.Panels)
                {
                    if (p != this) p.SetFixedLocally(false);
                }
            }
            isFixedLocally = true;
            VPBConfig.Instance.DesktopFixedMode = true;
            VPBConfig.Instance.DesktopFixedDockSide = desiredDock;
            
            VPBConfig.Instance.Save();
            UpdateDesktopModeButton();
            try { UpdateSpringScrollButtonToggleUI(); } catch { }
            InvalidateFooterOverflowLayout();
            MarkGalleryPaneChromeDirty();
            UpdateLayout();
            try { SyncCategoryQuickSwitchChrome(); } catch { }
        }

        private void CycleDesktopFixedDockSide()
        {
            if (VPBConfig.Instance == null) return;
            if (!isFixedLocally) return;
            bool enforce = false;
            try { enforce = VPBConfig.Instance.DesktopFixedEnforceDockSide; } catch { enforce = false; }
            string cur = VPBConfig.NormalizeDesktopFixedDockSide(enforce ? VPBConfig.Instance.DesktopFixedEnforcedDockSide : VPBConfig.Instance.DesktopFixedDockSide);
            string next = "Right";
            if (string.Equals(cur, "Right", StringComparison.OrdinalIgnoreCase)) next = "Left";
            else if (string.Equals(cur, "Left", StringComparison.OrdinalIgnoreCase)) next = "Top";
            else if (string.Equals(cur, "Top", StringComparison.OrdinalIgnoreCase)) next = "Right";
            if (enforce) VPBConfig.Instance.DesktopFixedEnforcedDockSide = next;
            VPBConfig.Instance.DesktopFixedDockSide = next;
            VPBConfig.Instance.Save(true, true);
            UpdateFooterDockButtonState();
            UpdateFooterAutoHideState();
            InvalidateFooterOverflowLayout();
            MarkGalleryPaneChromeDirty();
            UpdateLayout();
        }

        public void SetFixedLocally(bool fixedMode)
        {
            if (fixedMode)
            {
                bool isVR = XrUtils.IsVrActive();
                if (isVR) fixedMode = false;
            }

            if (isFixedLocally == fixedMode) return;
            isFixedLocally = fixedMode;
            if (!fixedMode) SetCollapsed(false);
            UpdateDesktopModeButton();
            try { UpdateSpringScrollButtonToggleUI(); } catch { }
            UpdateSideButtonsVisibility();
            InvalidateFooterOverflowLayout();
            MarkGalleryPaneChromeDirty();
            UpdateLayout();
            try { SyncCategoryQuickSwitchChrome(); } catch { }
        }

        public bool IsCollapsed => isCollapsed;

        /// <summary>Raycast + tint on dock-specific hover strip. Must run after dock changes — strips not touched by last SetCollapsed keep default raycast=true and block toolbar.</summary>
        private void ApplyFixedCollapseTriggerVisuals()
        {
            try { ApplyGalleryDockHoverVisuals(); } catch { }
        }

        public void SetCollapsed(bool collapsed)
        {
            if (isCollapsed == collapsed) return;
            // Keep grid subtree alive for the active item-drag handler (EventSystem + OnDisable cancel).
            if (collapsed)
            {
                try
                {
                    if (UIDraggableItem.IsDragging) return;
                }
                catch { }
            }
            isCollapsed = collapsed;
            collapseTimer = 0f;
            if (collapsed)
            {
                try { HideHoverPreview(null); } catch { }
                try { PersistCurrentBrowsePlace(); } catch { }
            }

            if (backgroundBoxGO != null)
            {
                RectTransform rt = _backgroundBoxRT;
                if (rt == null)
                {
                    rt = backgroundBoxGO.GetComponent<RectTransform>();
                    _backgroundBoxRT = rt;
                }
                Vector2 off = Vector2.zero;
                if (collapsed && VPBConfig.Instance != null)
                {
                    string side = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);
                    if (string.Equals(side, "Left", StringComparison.OrdinalIgnoreCase))
                        off = new Vector2(-rt.rect.width, 0f);
                    else if (string.Equals(side, "Top", StringComparison.OrdinalIgnoreCase))
                        off = new Vector2(0f, rt.rect.height);
                    else
                        off = new Vector2(rt.rect.width, 0f);
                }
                rt.anchoredPosition = collapsed ? off : Vector2.zero;

                // Stop the off-screen content from rendering/raycasting while collapsed (the FPS sink).
                bool wantSubtree = ShouldContentSubtreeBeActive();
                if (backgroundBoxGO.activeSelf != wantSubtree)
                    backgroundBoxGO.SetActive(wantSubtree);
            }

            ApplyFixedCollapseTriggerVisuals();
            
            UpdateSideButtonsVisibility();
            InvalidateFooterOverflowLayout();
            MarkGalleryPaneChromeDirty();
            // Collapse: skip UpdateLayout — ForceRebuildLayoutImmediate + Canvas.ForceUpdateCanvases on a deactivating tree was the dock minimize hitch.
            // Expand: defer one frame so SetActive(true) viewport is non-zero before layout (also avoids stacking with activate spike).
            if (collapsed)
            {
                StopCo(ref _deferredCollapseLayoutCo);
            }
            else
            {
                ScheduleDeferredExpandLayout();
            }
        }

        private void ScheduleDeferredExpandLayout()
        {
            if (!Application.isPlaying)
            {
                try { UpdateLayout(false, false); } catch { }
                try { RequestUserTagAvailVirtRecoverAfterLayout(); } catch { }
                return;
            }
            StopCo(ref _deferredCollapseLayoutCo);
            _deferredCollapseLayoutCo = StartCoroutine(DeferredExpandLayoutRoutine());
        }

        private IEnumerator DeferredExpandLayoutRoutine()
        {
            yield return null;
            _deferredCollapseLayoutCo = null;
            if (isCollapsed || canvas == null) yield break;
            // No sync CacheCreators/CacheCategoryCounts — expand is warm path.
            try { UpdateLayout(false, false); } catch { }
            try { RequestUserTagAvailVirtRecoverAfterLayout(); } catch { }
        }

        /// <summary>Select every item in <see cref="currentFilteredFiles"/> when within <see cref="SelectAllSafetyMaxItemCount"/>.</summary>
        /// <returns>True if selection was applied.</returns>
        private bool TrySelectAllCurrentGalleryView(string source)
        {
            var list = currentFilteredFiles;
            if (list == null || list.Count == 0) return false;

            if (_benchPickModeActive)
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                int max = BenchPickMaxPerSession;
                for (int i = 0; i < list.Count && selectedFiles.Count < max; i++)
                {
                    var f = list[i];
                    if (f == null) continue;
                    string k = f.Path;
                    if (string.IsNullOrEmpty(k)) k = f.Uid;
                    if (string.IsNullOrEmpty(k)) continue;
                    if (selectedFilePaths.Add(k)) selectedFiles.Add(f);
                }
                BenchOnGallerySelectionChangedInPickMode();
                ShowTemporaryStatus(VPBTranslation.T("bench.pick.select_all_capped",
                        "Selected first {0} items (bench pick limit).").Replace("{0}", max.ToString()), 3f);
                return selectedFiles.Count > 0;
            }

            if (_stripKeepSubScenePickActive)
            {
                // Single default — select first item only.
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null) continue;
                    string k = f.Path;
                    if (string.IsNullOrEmpty(k)) k = f.Uid;
                    if (string.IsNullOrEmpty(k)) continue;
                    selectedFiles.Add(f);
                    selectedFilePaths.Add(k);
                    selectionAnchorPath = k;
                    break;
                }
                StripKeepOnGallerySelectionChangedInSubScenePick();
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_pick_one",
                        "SubScene pick uses one file — first item selected."),
                    2.5f);
                return selectedFiles.Count > 0;
            }

            if (list.Count > SelectAllSafetyMaxItemCount)
            {
                string msg = string.Format(
                    VPBTranslation.T("gallery.status.select_all_too_many", "Too many items to select all ({0} shown). Maximum is {1}."),
                    list.Count,
                    SelectAllSafetyMaxItemCount);
                LogUtil.LogWarning("[VPB] Select all skipped (" + source + "): " + list.Count + " items (safety limit " + SelectAllSafetyMaxItemCount + ").");
                ShowTemporaryStatus(msg, 3.5f);
                return false;
            }

            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectionAnchorPath = null;

            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                if (selectedFilePaths.Add(f.Path)) selectedFiles.Add(f);
            }

            if (selectedFiles.Count > 0)
            {
                selectedPath = selectedFiles[0].Path;
                selectionAnchorPath = selectedPath;
                // Selection should not "stick" the hover path.
                SetHoverPath("");
            }
            else
            {
                selectedPath = null;
                SetHoverPath("");
            }

            RefreshSelectionVisuals();
            UpdatePaginationText();
            return true;
        }

        private void SelectAll()
        {
            TrySelectAllCurrentGalleryView("select_all_button");
        }

        private void ClearSelection()
        {
            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectionAnchorPath = null;
            selectedPath = null;
            SetHoverPath("");
            RefreshSelectionVisuals();
            UpdatePaginationText();
        }

        private void AdjustGridColumns(int delta)
        {
            RecyclingGridView rgvState = null;
            if (contentGO != null) rgvState = contentGO.GetComponent<RecyclingGridView>();

            if (settingsListViewActive)
            {
                if (contentGO != null)
                {
                    RecyclingGridView rgv = rgvState != null ? rgvState : contentGO.GetComponent<RecyclingGridView>();
                    if (rgv != null)
                    {
                        rgv.fixedColumns = 1;
                        rgv.fixedBottomChromePx = 0f;
                        rgv.SetGridConfig(100f, internalSettingsListRowHeightSession, 5f, 5f, 1);
                        // Settings list must adapt to viewport width (Top dock/full width resize).
                        rgv.SetAdaptiveConfig(true, 0f, 1, true);
                        rgv.Refresh();
                    }
                }
                return;
            }

            bool isListLike = (layoutMode == GalleryLayoutMode.List);
            if (!isListLike && rgvState != null)
            {
                // Defensive: if the grid is currently configured as a 1-column fixed-height list,
                // treat +/- as zoom even if layoutMode is temporarily out of sync.
                if (rgvState.useFixedHeight) isListLike = true;
            }

            if (isListLike)
            {
                try
                {
                    logger.LogInfo("List zoom: layoutMode=" + layoutMode + " fixedColumns=" + (rgvState != null ? rgvState.fixedColumns.ToString() : "null") + " fixedHeight=" + (rgvState != null ? rgvState.useFixedHeight.ToString() : "null") + " delta=" + delta);
                }
                catch { }

                // List/Table zoom: +/- changes thumbnail size + row height, NOT columns.
                // delta: +1 => "-" button (zoom out / smaller), -1 => "+" button (zoom in / larger)
                float step = 15f;
                ListRowHeight = Mathf.Clamp(ListRowHeight - (delta * step), 80f, 400f);

                if (contentGO != null)
                {
                    RecyclingGridView rgv = rgvState != null ? rgvState : contentGO.GetComponent<RecyclingGridView>();
                    if (rgv != null)
                    {
                        rgv.fixedColumns = 1;
                        rgv.fixedBottomChromePx = 0f;
                        rgv.SetGridConfig(100f, ListRowHeight, 5f, 5f, 1);
                        rgv.SetAdaptiveConfig(true, 0f, 1, true);
                        rgv.Refresh();
                    }
                }
                return;
            }

            GridColumnCount = Mathf.Clamp(GridColumnCount + delta, 1, 12);
            if (contentGO != null)
            {
                RecyclingGridView rgv = rgvState != null ? rgvState : contentGO.GetComponent<RecyclingGridView>();
                if (rgv != null)
                {
                    // Preserve the center item so RecalculateLayout restores it after the column change.
                    rgv.preserveCenterItemIndex = rgv.GetCenterItemIndex();
                    rgv.fixedColumns = GridColumnCount;
                    // No need to RefreshFiles, rgv handles column changes via its Update/RecalculateLayout
                }
            }
            RebuildGridLayout();
        }

        internal void ApplyCtrlScrollToGridColumns(int delta)
        {
            AdjustGridColumns(delta);
        }

        private void ScrollGalleryToTop()
        {
            if (recyclingGrid != null)
                recyclingGrid.ScrollToTopImmediate();
            else if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
            UpdateScrollbarJumpButtonsVisibility();
        }

        private void ScrollGalleryToBottom()
        {
            if (recyclingGrid != null)
                recyclingGrid.ScrollToBottomImmediate();
            else if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;
            UpdateScrollbarJumpButtonsVisibility();
        }

        private void ScrollGalleryStepUp()
        {
            ScrollRectByConfiguredStep(scrollRect, 1f);
            UpdateScrollbarJumpButtonsVisibility();
        }

        private void ScrollGalleryStepDown()
        {
            ScrollRectByConfiguredStep(scrollRect, -1f);
            UpdateScrollbarJumpButtonsVisibility();
        }

        private void ScrollRectByConfiguredStep(ScrollRect sr, float direction)
        {
            if (sr == null) return;
            float scrollablePx = 0f;
            float viewportH = 0f;
            try
            {
                if (sr.content != null && sr.viewport != null)
                {
                    viewportH = sr.viewport.rect.height;
                    scrollablePx = Mathf.Max(0f, sr.content.rect.height - viewportH);
                }
            }
            catch { scrollablePx = 0f; viewportH = 0f; }
            if (scrollablePx <= 0.5f || viewportH <= 0.5f) return;

            float step = VPBConfig.Instance != null ? VPBConfig.Instance.GalleryScrollButtonStepViewportFraction : 0.65f;
            step = Mathf.Clamp(step, 0.10f, 2.00f);
            float delta = (viewportH * step) / scrollablePx;
            sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition + Mathf.Sign(direction) * delta);
            try { if (recyclingGrid != null && sr == scrollRect) recyclingGrid.Refresh(); } catch { }
        }

        private void SyncActiveContentTypeFromSidePanels()
        {
            if (leftActiveContent == ContentType.History || rightActiveContent == ContentType.History)
                activeContentType = ContentType.History;
            else
                activeContentType = ContentType.Category;
        }

        private void ApplyHistoryBrowseTitle()
        {
            if (titleText == null) return;
            if (activeContentType != ContentType.History) return;
            titleText.text = VPBTranslation.T("gallery.history.title", "History");
        }

        private static bool SidePanelToggleExitsCleanupMode(ContentType type)
        {
            return type != ContentType.CleanupCategories && type != ContentType.CleanupStaleBuckets;
        }

        private void ToggleRight(ContentType type) => ToggleSide(isLeft: false, type);

        private void ToggleLeft(ContentType type) => ToggleSide(isLeft: true, type);

        /// <summary>
        /// Docked: LMB → left panel, RMB → right panel.
        /// Floating/VR: panel follows which rail button was pressed (ignore mouse button).
        /// </summary>
        private bool PreferLeftSidePanelFromRail(bool fromLeftRailButton, bool rightClick)
        {
            if (isFixedLocally) return !rightClick;
            return fromLeftRailButton;
        }

        private void ToggleSideFromRailButton(ContentType type, bool fromLeftRailButton, bool rightClick)
        {
            if (PreferLeftSidePanelFromRail(fromLeftRailButton, rightClick))
                ToggleLeft(type);
            else
                ToggleRight(type);
        }

        /// <summary>Single side-panel toggle path — left/right differ only in which rail is primary.</summary>
        private void ToggleSide(bool isLeft, ContentType type)
        {
            // Sidebar occupies one side column; opening that side's panel closes Import first. Clear intent
            // (user chose this pane over the sidebar) and reconcile via the gate, else next Show reopens it.
            if (importSidebarActive && importSidebarOnLeft == isLeft)
            {
                importSidebarOpenIntent = false;
                RefreshImportSidebarCategoryGate();
                PersistImportSidebarOpenIntent();
            }
            if (type == ContentType.Settings)
            {
                HideGlobalSourceFilterDropdownIfOpen();
                OpenSettingsSideTab();
                return;
            }
            bool hadSettingsPanel = IsSettingsPanelOpen();
            bool userTagsWasOpen = leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags;
            // Legacy middle-pane settings only — float Settings stays open with side panes (modeless).
            if (settingsListViewActive)
                ExitInternalSettingsMode(true);
            if (type == ContentType.Creator && VPBConfig.Instance != null && VPBConfig.Instance.GalleryHideCreatorSideButtons
                && leftActiveContent != ContentType.Creator && rightActiveContent != ContentType.Creator)
                return;
            bool hadHistorySide = leftActiveContent == ContentType.History || rightActiveContent == ContentType.History;
            bool wasCleanup = cleanupModeActive;
            if (wasCleanup && SidePanelToggleExitsCleanupMode(type))
                ExitCleanupModeForSidePanelNavigation();

            if (IsSubmenuContentType(type)) CloseOtherSideIfSubmenu(isLeft);
            bool timeCategoryCreatorSwitch = LogCategoryCreatorSideTabSwitchTiming
                && (type == ContentType.Category || type == ContentType.Creator || type == ContentType.UserTags || type == ContentType.Path || type == ContentType.History);
            if (timeCategoryCreatorSwitch)
                BeginSideTabCategoryCreatorTiming(isLeft ? "left" : "right");

            if (isLeft)
            {
                if (wasCleanup && SidePanelToggleExitsCleanupMode(type))
                {
                    leftActiveContent = type;
                    if (type == ContentType.Settings || rightActiveContent == type) rightActiveContent = null;
                }
                else if (leftActiveContent == type)
                    leftActiveContent = null;
                else
                {
                    leftActiveContent = type;
                    if (type == ContentType.Settings || rightActiveContent == type) rightActiveContent = null;
                }
            }
            else
            {
                if (wasCleanup && SidePanelToggleExitsCleanupMode(type))
                {
                    rightActiveContent = type;
                    if (type == ContentType.Settings || leftActiveContent == type) leftActiveContent = null;
                }
                else if (rightActiveContent == type)
                    rightActiveContent = null;
                else
                {
                    rightActiveContent = type;
                    if (type == ContentType.Settings || leftActiveContent == type) leftActiveContent = null;
                }
            }

            SyncActiveContentTypeFromSidePanels();
            bool hasHistorySide = leftActiveContent == ContentType.History || rightActiveContent == ContentType.History;
            if (!hasHistorySide && hadHistorySide && titleText != null)
                titleText.text = currentCategoryTitle;

            bool hasSettingsPanel = IsSettingsPanelOpen();
            if (!hadSettingsPanel && hasSettingsPanel)
            {
                // Keep title search on browse/grid query; settings filter lives in side rail.
                try { SyncSettingsSideSearchInputFromFilter(); } catch { }
            }
            else if (hadSettingsPanel && !hasSettingsPanel)
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, GetTitleSearchBrowseFieldText(), _titleBarSearchOnValueChanged); } catch { }

            // Ensure settings list view active before layout pass so first click opens list immediately.
            if (type == ContentType.Settings)
                SyncInternalSettingsListView();

            try { SyncTitleSearchChromeForActiveMode(); } catch { }

            // Chrome only — do not sync CacheCreators / CacheCategoryCounts / CacheUserTags here.
            // Those scans stall the main thread so rail buttons (incl. Creator) look like they spawn late.
            // UpdateTabs builders fill the cache for the open facet only.
            UpdateLayout(false, false);
            UpdateTabs();

            // Leaving Settings via re-toggling Settings button or switching side panes must restore toolbox actions.
            if (hadSettingsPanel && !IsSettingsPanelOpen())
                try { RefreshTboxConditionalActionButtons(); } catch { }

            // BA prompt: only show when user enters Settings page.
            if (!hadSettingsPanel && IsSettingsPanelOpen())
                try { TryShowBaMigrationPromptOnSettingsEnter(); } catch { }

            if (hadHistorySide != hasHistorySide || (hasHistorySide && type == ContentType.History))
            {
                if (hasHistorySide)
                    ApplyHistoryBrowseTitle();
                if (hadHistorySide && !hasHistorySide)
                    RefreshFiles(true);
                else if (hasHistorySide)
                {
                    ApplyHistorySortPresetForMode(galleryHistoryFilterMode);
                    RefreshHistoryBrowsePreferLight(true);
                }
            }

            if (timeCategoryCreatorSwitch)
                EndSideTabCategoryCreatorTiming();

            bool userTagsNowOpen = leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags;
            if (!userTagsWasOpen && userTagsNowOpen)
                try { ApplyDefaultUserTagAvailModeOnTagsPanelOpen(); } catch { }
        }

        private void UpdateReplaceButtonState()
        {
            string text = DragDropReplaceMode
                ? VPBTranslation.T("gallery.side.replace", "Replace")
                : VPBTranslation.T("gallery.side.add", "Add");
            Color color = DragDropReplaceMode ? new Color(0.6f, 0.15f, 0.15f, 1f) : new Color(0.15f, 0.45f, 0.15f, 1f);

            Sprite modeSprite = DragDropReplaceMode
                ? (galleryReplaceSprite ?? galleryAddSprite)
                : (galleryAddSprite ?? galleryReplaceSprite);

            if (tboxReplaceBtn == null) return;

            Image bg = tboxReplaceBtn.GetComponent<Image>();
            if (bg != null) bg.color = color;
            if (tboxReplaceBtnIconImage != null && modeSprite != null)
            {
                UI.SetIconSprite(tboxReplaceBtnIconImage, modeSprite);
                tboxReplaceBtnIconImage.enabled = true;
            }
            else
            {
                Text t = tboxReplaceBtn.GetComponentInChildren<Text>(true);
                if (t != null)
                {
                    t.gameObject.SetActive(true);
                    t.text = text;
                }
            }
        }

        public void RefreshAppearanceClothingSideButton()
        {
            UpdateKeepClothingButtonState();
        }

        // Reflect the persisted appearance clothing-apply-mode on the toolbox segmented row
        // (Preset / Keep / Only / Merge). Single-select: the active mode's button is highlighted, the
        // others are dimmed.
        private void UpdateKeepClothingButtonState()
        {
            string m = AppearanceClothingApplyMode ?? "replace";
            bool keep = string.Equals(m, "keep", StringComparison.OrdinalIgnoreCase);
            bool only = string.Equals(m, "clothingonly", StringComparison.OrdinalIgnoreCase);
            bool merge = string.Equals(m, "mergeoutfit", StringComparison.OrdinalIgnoreCase);
            bool preset = !keep && !only && !merge;

            StyleClothingModeButton(tboxClothesPresetImg, tboxClothesPresetText, preset, new Color(0.35f, 0.3f, 0.2f, 1f));
            StyleClothingModeButton(tboxClothesKeepImg, tboxClothesKeepText, keep, new Color(0.15f, 0.35f, 0.55f, 1f));
            StyleClothingModeButton(tboxClothesOnlyImg, tboxClothesOnlyText, only, new Color(0.15f, 0.45f, 0.28f, 1f));
            StyleClothingModeButton(tboxClothesMergeImg, tboxClothesMergeText, merge, new Color(0.40f, 0.22f, 0.45f, 1f));
        }

        private static void StyleClothingModeButton(Image img, Text text, bool selected, Color selectedColor)
        {
            if (img != null)
                img.color = selected ? selectedColor : GalleryUiColorTokens.SegmentIdle;
            if (text != null)
                text.color = selected ? GalleryUiColorTokens.TextOnAccent : GalleryUiColorTokens.SegmentIdleText;
        }

        private void SetAppearanceClothingMode(string mode)
        {
            AppearanceClothingApplyMode = mode;
            UpdateKeepClothingButtonState();
        }

        private void ToggleReplaceMode()
        {
            DragDropReplaceMode = !DragDropReplaceMode;
            UpdateReplaceButtonState();
        }

        private void UpdateApplyModeButtonState()
        {
            string text = ItemApplyMode == ApplyMode.SingleClick
                ? VPBTranslation.T("gallery.apply.one_click", "1-Click")
                : VPBTranslation.T("gallery.apply.two_click", "2-Click");
            Color color = ItemApplyMode == ApplyMode.SingleClick ? new Color(0.6f, 0.45f, 0.15f, 1f) : new Color(0.15f, 0.15f, 0.45f, 1f);

            Sprite modeSprite = ItemApplyMode == ApplyMode.SingleClick
                ? (galleryApplyOneClickSprite ?? galleryApplyTwoClickSprite)
                : (galleryApplyTwoClickSprite ?? galleryApplyOneClickSprite);

            if (rightApplyModeBtnIconImage != null && modeSprite != null)
            {
                if (rightApplyModeBtnText != null) rightApplyModeBtnText.gameObject.SetActive(false);
                UI.SetIconSprite(rightApplyModeBtnIconImage, modeSprite);
                rightApplyModeBtnIconImage.enabled = true;
            }
            else if (rightApplyModeBtnText != null)
            {
                rightApplyModeBtnText.gameObject.SetActive(true);
                rightApplyModeBtnText.text = text;
            }
            if (rightApplyModeBtnImage != null) rightApplyModeBtnImage.color = color;

            if (leftApplyModeBtnIconImage != null && modeSprite != null)
            {
                if (leftApplyModeBtnText != null) leftApplyModeBtnText.gameObject.SetActive(false);
                UI.SetIconSprite(leftApplyModeBtnIconImage, modeSprite);
                leftApplyModeBtnIconImage.enabled = true;
            }
            else if (leftApplyModeBtnText != null)
            {
                leftApplyModeBtnText.gameObject.SetActive(true);
                leftApplyModeBtnText.text = text;
            }
            if (leftApplyModeBtnImage != null) leftApplyModeBtnImage.color = color;

            // Hold-to-launch overrides 1-click apply: disable the toggle button while hold mode is on.
            // Task chrome sticky suppress also locks the toggle (ApplyTaskChromeApplyHoldPolicy dims peers).
            bool stickySuppress = false;
            try { stickySuppress = TaskChromeSuppressArmedApplyChrome(ResolveTaskChromeState()); } catch { }
            bool disableApplyToggle = holdToLaunchEnabled || stickySuppress;
            try
            {
                if (rightApplyModeBtnImage != null)
                {
                    var b = rightApplyModeBtnImage.GetComponent<Button>();
                    if (b != null) b.interactable = !disableApplyToggle;
                    if (disableApplyToggle) rightApplyModeBtnImage.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
                    // Tooltip swap (best-effort)
                    string tipKey = stickySuppress
                        ? "gallery.tooltip.apply_mode_disabled_sticky"
                        : (disableApplyToggle ? "gallery.tooltip.apply_mode_disabled_hold_to_launch" : "gallery.tooltip.apply_mode");
                    string tipFallback = stickySuppress
                        ? "Sticky tool active — Esc exits tool before changing apply mode."
                        : (disableApplyToggle
                            ? "Hold-launch ON — drag-drop paused. Turn Hold off to change 1-click/2-click and restore drag-drop."
                            : "Toggle 1-click vs 2-click apply.");
                    AddTooltip(rightApplyModeBtnImage.gameObject, tipKey, tipFallback);
                }
                if (leftApplyModeBtnImage != null)
                {
                    var b = leftApplyModeBtnImage.GetComponent<Button>();
                    if (b != null) b.interactable = !disableApplyToggle;
                    if (disableApplyToggle) leftApplyModeBtnImage.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
                    string tipKey = stickySuppress
                        ? "gallery.tooltip.apply_mode_disabled_sticky"
                        : (disableApplyToggle ? "gallery.tooltip.apply_mode_disabled_hold_to_launch" : "gallery.tooltip.apply_mode");
                    string tipFallback = stickySuppress
                        ? "Sticky tool active — Esc exits tool before changing apply mode."
                        : (disableApplyToggle
                            ? "Hold-launch ON — drag-drop paused. Turn Hold off to change 1-click/2-click and restore drag-drop."
                            : "Toggle 1-click vs 2-click apply.");
                    AddTooltip(leftApplyModeBtnImage.gameObject, tipKey, tipFallback);
                }
            }
            catch { }
        }

        private void ToggleApplyMode()
        {
            try
            {
                if (TaskChromeSuppressArmedApplyChrome(ResolveTaskChromeState()))
                    return;
            }
            catch { }
            if (holdToLaunchEnabled)
            {
                // Hold-to-launch overrides single-click apply; keep the toggle disabled until hold mode is off.
                return;
            }
            ApplyMode oldMode = ItemApplyMode;
            ApplyMode newMode = (oldMode == ApplyMode.SingleClick) ? ApplyMode.DoubleClick : ApplyMode.SingleClick;
            LogUtil.Log("[GalleryPanel] ToggleApplyMode: " + oldMode + " -> " + newMode);
            ItemApplyMode = newMode;
            UpdateApplyModeButtonState();
            try { RefreshModeAmbientChrome(); } catch { }
            ShowTemporaryStatus(
                newMode == ApplyMode.SingleClick
                    ? VPBTranslation.T("gallery.apply.mode_1click", "Apply mode: 1-Click")
                    : VPBTranslation.T("gallery.apply.mode_2click", "Apply mode: 2-Click"),
                1.5f);
        }


    }

}
