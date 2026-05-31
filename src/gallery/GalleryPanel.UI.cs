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

            if (IsHubMode) return options;

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
            CloseOtherSideIfSubmenu(useLeftSide);
            if (useLeftSide)
            {
                if (leftActiveContent == ContentType.SavePresets)
                {
                    leftActiveContent = leftPrevActiveContent;
                }
                else
                {
                    leftPrevActiveContent = leftActiveContent;
                    leftActiveContent = ContentType.SavePresets;
                }
            }
            else
            {
                if (rightActiveContent == ContentType.SavePresets)
                {
                    rightActiveContent = rightPrevActiveContent;
                }
                else
                {
                    rightPrevActiveContent = rightActiveContent;
                    rightActiveContent = ContentType.SavePresets;
                }
            }
            UpdateLayout();
            UpdateTabs();
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

            // Let VaM finish writing the .jpg before we invalidate caches and reload thumbs.
            yield return new WaitForSecondsRealtime(0.2f);
            // Restore gallery visibility first — RefreshVisibleGridVisualsOnly no-ops while hidden.
            EndSaveMode();
            InvalidateSceneSaveGalleryCaches(path);
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
            // Pagination Container (Footer Bar)
            GameObject pageContainer = new GameObject("PaginationContainer");
            pageContainer.transform.SetParent(backgroundBoxGO.transform, false);
            paginationRT = pageContainer.AddComponent<RectTransform>();
            paginationRT.anchorMin = new Vector2(0, 0);
            paginationRT.anchorMax = new Vector2(1, 0); // Stretch horizontally
            paginationRT.pivot = new Vector2(0.5f, 0);
            paginationRT.anchoredPosition = new Vector2(0, 0);
            paginationRT.sizeDelta = new Vector2(0, 40); // Footer bar height for buttons
            
            footerHLG = pageContainer.AddComponent<HorizontalLayoutGroup>();
            footerHLG.padding = new RectOffset(60, 10, 0, 0); // left reserved space (existing behavior)
            {
                var hlg = footerHLG;
                innerPaneScaleActions.Add(s => { if (hlg) { hlg.padding = new RectOffset(Mathf.RoundToInt(60 * s), Mathf.RoundToInt(10 * s), 0, 0); } });
            }
            footerHLG.childControlWidth = true;
            footerHLG.childControlHeight = true;
            footerHLG.childForceExpandWidth = true;
            _footerHLGLastRightPadding = footerHLG.padding != null ? footerHLG.padding.right : -1;

            // Fixed dock "Top": side rail buttons group (overlays footer, centered in free space).
            _footerSideButtonsGroupGO = new GameObject("SideButtonsGroup");
            _footerSideButtonsGroupGO.transform.SetParent(pageContainer.transform, false);
            _footerSideButtonsGroupRT = _footerSideButtonsGroupGO.AddComponent<RectTransform>();
            {
                var le = _footerSideButtonsGroupGO.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
            }
            _footerSideButtonsGroupRT.anchorMin = new Vector2(0.5f, 0.5f);
            _footerSideButtonsGroupRT.anchorMax = new Vector2(0.5f, 0.5f);
            _footerSideButtonsGroupRT.pivot = new Vector2(0.5f, 0.5f);
            _footerSideButtonsGroupRT.anchoredPosition = Vector2.zero;
            _footerSideButtonsGroupRT.sizeDelta = new Vector2(0f, 40f);
            _footerSideButtonsGroupGO.SetActive(false);

            // --- Left Section (Follow Controls) ---
            GameObject leftSection = new GameObject("LeftSection");
            leftSection.transform.SetParent(pageContainer.transform, false);
            _footerLeftSectionRT = leftSection.AddComponent<RectTransform>();
            leftSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup leftHLG = leftSection.AddComponent<HorizontalLayoutGroup>();
            leftHLG.childControlWidth = false;
            leftHLG.childControlHeight = false;
            leftHLG.childForceExpandWidth = false;
            leftHLG.childForceExpandHeight = false;
            leftHLG.childAlignment = TextAnchor.MiddleLeft;
            leftHLG.spacing = 10;
            {
                var hlg = leftHLG;
                innerPaneScaleActions.Add(s => { if (hlg) hlg.spacing = 10f * s; });
            }

            // Undo / Redo / Random (footer left; compact labels U/R/Rdm)
            footerUndoBtnGO = UI.CreateUIButton(leftSection, 40, 40, VPBTranslation.T("gallery.footer.undo_abbrev", "U") + " (0)", 14, 0, 0, AnchorPresets.middleCenter, Undo);
            footerUndoBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            { var s = UI.LoadIconSprite("vpb_icons/undo.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerUndoBtnGO, s); }
            footerRedoBtnGO = UI.CreateUIButton(leftSection, 40, 40, VPBTranslation.T("gallery.footer.redo_abbrev", "R") + " (0)", 14, 0, 0, AnchorPresets.middleCenter, Redo);
            footerRedoBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            { var s = UI.LoadIconSprite("vpb_icons/redo.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerRedoBtnGO, s); }
            footerLoadRandomBtn = UI.CreateUIButton(leftSection, 40, 40, VPBTranslation.T("gallery.footer.random_abbrev", "Rdm"), 14, 0, 0, AnchorPresets.middleCenter, LoadRandom);
            footerLoadRandomBtn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            { var s = UI.LoadIconSprite("vpb_icons/random.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerLoadRandomBtn, s); }

            footerHubBtnGO = UI.CreateUIButton(leftSection, 40, 40, VPBTranslation.T("gallery.side.hub", "Hub"), 14, 0, 0, AnchorPresets.middleCenter, () => {
                VamHookPlugin.singleton?.OpenHubBrowse();
                Hide();
            });
            footerHubBtnImage = footerHubBtnGO.GetComponent<Image>();
            footerHubBtnImage.color = UI.IconButtonBackdrop;
            footerHubBtnText = footerHubBtnGO.GetComponentInChildren<Text>();
            AddRightClickDelegate(footerHubBtnGO, () => {
                VamHookPlugin.singleton?.OpenHubBrowse();
                Hide();
            });
            { var s = UI.LoadIconSprite("vpb_icons/hub.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerHubBtnGO, s); }

            // Follow Quick Toggles
            footerFollowAngleBtn = UI.CreateUIButton(leftSection, 40, 40, "∡", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Angle"));
            footerFollowAngleImage = footerFollowAngleBtn.GetComponent<Image>();
            { var s = UI.LoadIconSprite("vpb_icons/eye_angle.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerFollowAngleBtn, s); }
            AddTooltip(footerFollowAngleBtn, "gallery.tooltip.follow_angle", "Follow Angle");
            
            footerFollowDistanceBtn = UI.CreateUIButton(leftSection, 40, 40, "↕", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Distance"));
            footerFollowDistanceImage = footerFollowDistanceBtn.GetComponent<Image>();
            { var s = UI.LoadIconSprite("vpb_icons/eye_distance.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerFollowDistanceBtn, s); }
            AddTooltip(footerFollowDistanceBtn, "gallery.tooltip.follow_distance", "Follow Distance");
            
            footerFollowHeightBtn = UI.CreateUIButton(leftSection, 40, 40, "⊙", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Height"));
            footerFollowHeightImage = footerFollowHeightBtn.GetComponent<Image>();
            { var s = UI.LoadIconSprite("vpb_icons/eye_height.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerFollowHeightBtn, s); }
            AddTooltip(footerFollowHeightBtn, "gallery.tooltip.follow_eye_height", "Follow Eye Height");

            // --- Center Section (Pagination) ---
            GameObject centerSection = new GameObject("CenterSection");
            centerSection.transform.SetParent(pageContainer.transform, false);
            centerSection.AddComponent<RectTransform>();
            centerSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup centerHLG = centerSection.AddComponent<HorizontalLayoutGroup>();
            centerHLG.childControlWidth = false;
            centerHLG.childForceExpandWidth = false;
            centerHLG.childControlHeight = false;
            centerHLG.childForceExpandHeight = false;
            centerHLG.childAlignment = TextAnchor.MiddleCenter;
            centerHLG.spacing = 10;
            {
                var hlg = centerHLG;
                innerPaneScaleActions.Add(s => { if (hlg) hlg.spacing = 10f * s; });
            }

            paginationFirstBtn = UI.CreateUIButton(centerSection, 40, 40, "|<", 18, 0, 0, AnchorPresets.middleCenter, FirstPage);
            paginationPrev10Btn = UI.CreateUIButton(centerSection, 40, 40, "<<", 18, 0, 0, AnchorPresets.middleCenter, Prev10Page);
            paginationPrevBtn = UI.CreateUIButton(centerSection, 40, 40, "<", 20, 0, 0, AnchorPresets.middleCenter, PrevPage);

            GameObject textGO = new GameObject("PageText");
            textGO.transform.SetParent(centerSection.transform, false);
            paginationText = textGO.AddComponent<Text>();
            paginationText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            paginationText.fontSize = 18;
            paginationText.color = Color.white;
            paginationText.alignment = TextAnchor.MiddleCenter;
            paginationText.text = VPBTranslation.T("gallery.items.zero", "0 Items");
            paginationText.horizontalOverflow = HorizontalWrapMode.Overflow;
            paginationText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.sizeDelta = new Vector2(200, 40);
            {
                var rt = textRT;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt) rt.sizeDelta = new Vector2(200f * s, 40f * s);
                    if (paginationText)
                    {
                        const int baseFont = 18;
                        const int minFont = 10;
                        float fontScale = Mathf.Clamp(s, (float)minFont / (float)baseFont, 100f);
                        paginationText.fontSize = Mathf.RoundToInt(baseFont * fontScale);
                        float extra = (fontScale > 0f) ? (s / fontScale) : 1f;
                        paginationText.transform.localScale = new Vector3(extra, extra, 1f);
                    }
                });
            }

            // Filter Mode Label (shown in filter mode, left of clear button)
            {
                GameObject modeGO = new GameObject("FilterModeLabel");
                modeGO.transform.SetParent(centerSection.transform, false);
                footerFilterModeText = modeGO.AddComponent<Text>();
                footerFilterModeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                footerFilterModeText.fontSize = 22;
                footerFilterModeText.color = new Color(1f, 0.85f, 0f, 1f);
                footerFilterModeText.alignment = TextAnchor.MiddleRight;
                footerFilterModeText.text = "";
                footerFilterModeText.horizontalOverflow = HorizontalWrapMode.Overflow;
                footerFilterModeText.verticalOverflow = VerticalWrapMode.Truncate;
                footerFilterModeText.raycastTarget = false;
                RectTransform modeRT = modeGO.GetComponent<RectTransform>();
                modeRT.sizeDelta = new Vector2(180, 40);
                modeGO.SetActive(false);
            }

            // Small spacer between mode label and clear button (only visible in filter mode)
            {
                footerFilterModeSpacerGO = new GameObject("FilterModeSpacer");
                footerFilterModeSpacerGO.transform.SetParent(centerSection.transform, false);
                footerFilterModeSpacerGO.AddComponent<RectTransform>();
                var le = footerFilterModeSpacerGO.AddComponent<LayoutElement>();
                le.preferredWidth = 12f;
                le.minWidth = 12f;
                footerFilterModeSpacerGO.SetActive(false);
            }

            // Back Button — icon-only, shown whenever filter mode is active
            footerBackBtn = UI.CreateUIButton(centerSection, 40, 40, "", 18, 0, 0, AnchorPresets.middleCenter, NavigateBack);
            footerBackBtn.name = "BackButton";
            footerBackBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.6f, 0.9f);
            { var s = UI.LoadIconSprite("vpb_icons/arrow_left.png", Color.white); if (s != null) UI.AddIconToButton(footerBackBtn, s); }
            footerBackBtn.SetActive(false);

            // Clear Filter Button — icon-only, clears all filter levels
            footerClearFilterBtn = UI.CreateUIButton(centerSection, 40, 40, "", 18, 0, 0, AnchorPresets.middleCenter, ClearPackageFilter);
            footerClearFilterBtn.name = "ClearFilterButton";
            footerClearFilterBtn.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
            { var s = UI.LoadIconSprite("vpb_icons/filter_off.png", Color.white); if (s != null) UI.AddIconToButton(footerClearFilterBtn, s); }
            footerClearFilterBtn.SetActive(false);

            paginationNextBtn = UI.CreateUIButton(centerSection, 40, 40, ">", 20, 0, 0, AnchorPresets.middleCenter, NextPage);
            paginationNext10Btn = UI.CreateUIButton(centerSection, 40, 40, ">>", 18, 0, 0, AnchorPresets.middleCenter, Next10Page);
            paginationLastBtn = UI.CreateUIButton(centerSection, 40, 40, ">|", 18, 0, 0, AnchorPresets.middleCenter, LastPage);

            // --- Right Section (Utility Controls) ---
            GameObject rightSection = new GameObject("RightSection");
            rightSection.transform.SetParent(pageContainer.transform, false);
            _footerRightSectionRT = rightSection.AddComponent<RectTransform>();
            rightSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup rightHLG = rightSection.AddComponent<HorizontalLayoutGroup>();
            rightHLG.childControlWidth = false;
            rightHLG.childControlHeight = false;
            rightHLG.childForceExpandWidth = false;
            rightHLG.childForceExpandHeight = false;
            rightHLG.childAlignment = TextAnchor.MiddleRight;
            rightHLG.spacing = 10;
            {
                var hlg = rightHLG;
                innerPaneScaleActions.Add(s => { if (hlg) hlg.spacing = 10f * s; });
            }

            // VaM Menu Gate (show gallery only when VaM menu is visible) — placed left of Select All
            footerMenuGateBtn = UI.CreateUIButton(rightSection, 40, 40, "M", 20, 0, 0, AnchorPresets.middleCenter, ToggleVamMenuGateMode);
            footerMenuGateBtnImage = footerMenuGateBtn.GetComponent<Image>();
            footerMenuGateOffSprite = UI.LoadIconSprite("vpb_icons/visibility_independent.png", Color.white);
            footerMenuGateOnSprite  = UI.LoadIconSprite("vpb_icons/visibility_linked.png",      Color.white);
            { Sprite init = footerMenuGateOffSprite ?? footerMenuGateOnSprite; if (init != null) { UI.AddIconToButton(footerMenuGateBtn, init); footerMenuGateIconImage = footerMenuGateBtn.transform.Find("Icon")?.GetComponent<Image>(); } }
            AddTooltip(footerMenuGateBtn, "gallery.tooltip.vam_menu_gate", "Show only when VaM menu is visible");

            footerShowHiddenPackagesBtn = UI.CreateUIButton(rightSection, 40, 40, "H", 20, 0, 0, AnchorPresets.middleCenter, ToggleGalleryShowHiddenPackages);
            footerShowHiddenPackagesBtnImage = footerShowHiddenPackagesBtn.GetComponent<Image>();
            footerShowHiddenPackagesBtnText = footerShowHiddenPackagesBtn.GetComponentInChildren<Text>();
            footerShowHiddenPackagesBtn.name = "Footer_ShowHiddenPackages";
            footerShowHiddenOffSprite = UI.LoadIconSprite("vpb_icons/show_hidden_off.png", UI.BarIconGlyphTint);
            footerShowHiddenOnSprite  = UI.LoadIconSprite("vpb_icons/show_hidden.png",     UI.BarIconGlyphTint);
            { Sprite init = footerShowHiddenOffSprite ?? footerShowHiddenOnSprite; if (init != null) { UI.AddIconToButton(footerShowHiddenPackagesBtn, init); footerShowHiddenIconImage = footerShowHiddenPackagesBtn.transform.Find("Icon")?.GetComponent<Image>(); } }

            gridSizeMinusBtn = UI.CreateUIButton(rightSection, 40, 40, "-", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(1));
            { var s = UI.LoadIconSprite("vpb_icons/zoom_out.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(gridSizeMinusBtn, s); }
            gridSizePlusBtn = UI.CreateUIButton(rightSection, 40, 40, "+", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(-1));
            { var s = UI.LoadIconSprite("vpb_icons/zoom_in.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(gridSizePlusBtn, s); }

            // Toggle big spring-scroll drag button (floating panes only; default ON)
            footerSpringScrollToggleBtn = UI.CreateUIButton(rightSection, 40, 40, "S", 20, 0, 0, AnchorPresets.middleCenter, ToggleSpringScrollButton);
            footerSpringScrollToggleBtnImage = footerSpringScrollToggleBtn.GetComponent<Image>();
            { var s = UI.LoadIconSprite("vpb_icons/scroll.png", UI.BarIconGlyphTint); if (s != null) { UI.AddIconToButton(footerSpringScrollToggleBtn, s); footerSpringScrollToggleIconImage = footerSpringScrollToggleBtn.transform.Find("Icon")?.GetComponent<Image>(); } }
            AddTooltip(footerSpringScrollToggleBtn, "gallery.tooltip.spring_scroll_toggle", "Toggle spring scroll drag button (floating)");

            // Toggle hold-to-launch/apply (hold trigger/button on item; duration in Settings)
            footerHoldToLaunchToggleBtn = UI.CreateUIButton(rightSection, 40, 40, "H", 20, 0, 0, AnchorPresets.middleCenter, ToggleHoldToLaunch);
            footerHoldToLaunchToggleBtnImage = footerHoldToLaunchToggleBtn.GetComponent<Image>();
            footerHoldToLaunchOnSprite  = UI.LoadIconSprite("vpb_icons/hold.png",     UI.BarIconGlyphTint);
            footerHoldToLaunchOffSprite = UI.LoadIconSprite("vpb_icons/hold_off.png", UI.BarIconGlyphTint);
            {
                // Fallback to old icon if hold icons missing
                var fallback = UI.LoadIconSprite("vpb_icons/load.png", UI.BarIconGlyphTint);
                var init = (holdToLaunchEnabled ? footerHoldToLaunchOnSprite : footerHoldToLaunchOffSprite) ?? footerHoldToLaunchOnSprite ?? footerHoldToLaunchOffSprite ?? fallback;
                if (init != null)
                {
                    UI.AddIconToButton(footerHoldToLaunchToggleBtn, init);
                    footerHoldToLaunchToggleIconImage = footerHoldToLaunchToggleBtn.transform.Find("Icon")?.GetComponent<Image>();
                }
            }
            AddTooltip(footerHoldToLaunchToggleBtn, "gallery.tooltip.hold_to_launch_toggle", "Hold trigger/button on item to apply/launch (see Settings for duration)");

            footerLayoutBtn = UI.CreateUIButton(rightSection, 40, 40, "▤", 20, 0, 0, AnchorPresets.middleCenter, ToggleLayoutMode);
            footerLayoutBtnImage = footerLayoutBtn.GetComponent<Image>();
            footerLayoutBtnText = footerLayoutBtn.GetComponentInChildren<Text>();
            footerLayoutGridSprite = UI.LoadIconSprite("vpb_icons/layout_grid.png", UI.BarIconGlyphTint);
            footerLayoutListSprite = UI.LoadIconSprite("vpb_icons/layout_list.png", UI.BarIconGlyphTint);
            { Sprite init = footerLayoutListSprite ?? footerLayoutGridSprite; if (init != null) { UI.AddIconToButton(footerLayoutBtn, init); footerLayoutIconImage = footerLayoutBtn.transform.Find("Icon")?.GetComponent<Image>(); } }

            footerDockBtn = UI.CreateUIButton(rightSection, 40, 40, "", 20, 0, 0, AnchorPresets.middleCenter, CycleDesktopFixedDockSide);
            footerDockBtnImage = footerDockBtn.GetComponent<Image>();
            footerDockRightSprite = UI.LoadIconSprite("vpb_icons/anchor_right.png", UI.BarIconGlyphTint);
            footerDockLeftSprite  = UI.LoadIconSprite("vpb_icons/anchor_left.png",  UI.BarIconGlyphTint);
            footerDockTopSprite   = UI.LoadIconSprite("vpb_icons/anchor_top.png",   UI.BarIconGlyphTint);
            { Sprite init = footerDockRightSprite ?? footerDockLeftSprite ?? footerDockTopSprite; if (init != null) { UI.AddIconToButton(footerDockBtn, init); footerDockIconImage = footerDockBtn.transform.Find("Icon")?.GetComponent<Image>(); } }
            AddTooltipPlain(footerDockBtn, VPBTranslation.T("gallery.tooltip.dock_side", "Dock side (Left/Right/Top)"));

            footerHeightBtn = UI.CreateUIButton(rightSection, 40, 40, "↕", 20, 0, 0, AnchorPresets.middleCenter, ToggleFixedHeightMode);
            footerHeightBtnImage = footerHeightBtn.GetComponent<Image>();
            footerHeightBtnText = footerHeightBtn.GetComponentInChildren<Text>();
            footerHeightFreeSprite  = UI.LoadIconSprite("vpb_icons/height_free.png",  UI.BarIconGlyphTint);
            footerHeightFixedSprite = UI.LoadIconSprite("vpb_icons/height_fixed.png", UI.BarIconGlyphTint);
            { Sprite init = footerHeightFixedSprite ?? footerHeightFreeSprite; if (init != null) { UI.AddIconToButton(footerHeightBtn, init); footerHeightIconImage = footerHeightBtn.transform.Find("Icon")?.GetComponent<Image>(); } }

            footerAutoHideBtn = UI.CreateUIButton(rightSection, 40, 40, "A", 20, 0, 0, AnchorPresets.middleCenter, ToggleAutoHideMode);
            footerAutoHideBtnImage = footerAutoHideBtn.GetComponent<Image>();
            footerAutoHideBtnText = footerAutoHideBtn.GetComponentInChildren<Text>();
            footerAutoHideLeftOffSprite  = UI.LoadIconSprite("vpb_icons/auto_hide_left_off.png",  UI.BarIconGlyphTint);
            footerAutoHideLeftOnSprite   = UI.LoadIconSprite("vpb_icons/auto_hide_left_on.png",   UI.BarIconGlyphTint);
            footerAutoHideRightOffSprite = UI.LoadIconSprite("vpb_icons/auto_hide_right_off.png", UI.BarIconGlyphTint);
            footerAutoHideRightOnSprite  = UI.LoadIconSprite("vpb_icons/auto_hide_right_on.png",  UI.BarIconGlyphTint);
            footerAutoHideTopOffSprite   = UI.LoadIconSprite("vpb_icons/auto_hide_top_off.png",   UI.BarIconGlyphTint);
            footerAutoHideTopOnSprite    = UI.LoadIconSprite("vpb_icons/auto_hide_top_on.png",    UI.BarIconGlyphTint);
            footerAutoHideOffSprite = UI.LoadIconSprite("vpb_icons/auto_hide_off.png", UI.BarIconGlyphTint);
            footerAutoHideOnSprite  = UI.LoadIconSprite("vpb_icons/auto_hide_on.png",  UI.BarIconGlyphTint);
            {
                GetFooterAutoHideSpritesForCurrentDock(out Sprite initOff, out Sprite initOn);
                Sprite init = initOff ?? initOn;
                if (init != null) { UI.AddIconToButton(footerAutoHideBtn, init); footerAutoHideIconImage = footerAutoHideBtn.transform.Find("Icon")?.GetComponent<Image>(); }
            }

            // --- Context Actions (Category-aware) ---

            // Hover support for pagination (Hub mode)
            AddHoverDelegate(paginationFirstBtn);
            AddTooltip(paginationFirstBtn, "gallery.tooltip.page_first", "First Page");
            AddHoverDelegate(paginationPrev10Btn);
            AddTooltip(paginationPrev10Btn, "gallery.tooltip.page_back_10", "Back 10 Pages");
            AddHoverDelegate(paginationPrevBtn);
            AddTooltip(paginationPrevBtn, "gallery.tooltip.page_prev", "Previous Page");
            AddHoverDelegate(paginationNextBtn);
            AddTooltip(paginationNextBtn, "gallery.tooltip.page_next", "Next Page");
            AddHoverDelegate(paginationNext10Btn);
            AddTooltip(paginationNext10Btn, "gallery.tooltip.page_forward_10", "Forward 10 Pages");
            AddHoverDelegate(paginationLastBtn);
            AddTooltip(paginationLastBtn, "gallery.tooltip.page_last", "Last Page");

            AddHoverDelegate(gridSizeMinusBtn);
            AddTooltip(gridSizeMinusBtn, "gallery.tooltip.grid_minus", "Decrease columns (Ctrl+scroll wheel over gallery)");
            AddHoverDelegate(gridSizePlusBtn);
            AddTooltip(gridSizePlusBtn, "gallery.tooltip.grid_plus", "Increase columns (Ctrl+scroll wheel over gallery)");
            AddHoverDelegate(footerSpringScrollToggleBtn);
            AddHoverDelegate(footerMenuGateBtn);
            AddHoverDelegate(footerHoldToLaunchToggleBtn);
            AddHoverDelegate(footerBackBtn);
            AddTooltip(footerBackBtn, "gallery.tooltip.filter_back", "Go back one filter level");
            AddHoverDelegate(footerClearFilterBtn);
            AddTooltip(footerClearFilterBtn, "gallery.tooltip.clear_filter", "Clear all filters");
            AddHoverDelegate(footerUndoBtnGO);
            AddTooltip(footerUndoBtnGO, "gallery.tooltip.undo", "Undo last change");
            AddHoverDelegate(footerRedoBtnGO);
            AddTooltip(footerRedoBtnGO, "gallery.tooltip.redo", "Redo");
            AddHoverDelegate(footerLoadRandomBtn);
            AddTooltip(footerLoadRandomBtn, "gallery.tooltip.load_random", "Load random item");
            AddHoverDelegate(footerHubBtnGO);
            AddTooltip(footerHubBtnGO, "gallery.tooltip.hub", "Hub browse / dev Hub panel");
            AddHoverDelegate(footerFollowAngleBtn);
            AddHoverDelegate(footerFollowDistanceBtn);
            AddHoverDelegate(footerFollowHeightBtn);
            AddHoverDelegate(footerLayoutBtn);
            AddHoverDelegate(footerDockBtn);
            AddHoverDelegate(footerHeightBtn);
            AddHoverDelegate(footerShowHiddenPackagesBtn);
            AddHoverDelegate(footerAutoHideBtn);

            // Register inner pane button scale actions (footer/pagination)
            { var prt = paginationRT; innerPaneScaleActions.Add(s => { if (prt) prt.sizeDelta = new Vector2(0, 40f*s); }); }
            { var rt = _footerSideButtonsGroupRT; innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(rt.sizeDelta.x, 40f * s); }); }
            {
                var uRT = footerUndoBtnGO != null ? footerUndoBtnGO.GetComponent<RectTransform>() : null;
                var rRT = footerRedoBtnGO != null ? footerRedoBtnGO.GetComponent<RectTransform>() : null;
                var rndRT = footerLoadRandomBtn != null ? footerLoadRandomBtn.GetComponent<RectTransform>() : null;
                var uT = footerUndoBtnGO != null ? footerUndoBtnGO.GetComponentInChildren<Text>() : null;
                var rT = footerRedoBtnGO != null ? footerRedoBtnGO.GetComponentInChildren<Text>() : null;
                var rndT = footerLoadRandomBtn != null ? footerLoadRandomBtn.GetComponentInChildren<Text>() : null;
                var hRT = footerHubBtnGO != null ? footerHubBtnGO.GetComponent<RectTransform>() : null;
                var hT = footerHubBtnText;
                innerPaneScaleActions.Add(s => {
                    if (uRT != null) uRT.sizeDelta = new Vector2(40f * s, 40f * s);
                    if (rRT != null) rRT.sizeDelta = new Vector2(40f * s, 40f * s);
                    if (rndRT != null) rndRT.sizeDelta = new Vector2(40f * s, 40f * s);
                    if (hRT != null) hRT.sizeDelta = new Vector2(40f * s, 40f * s);
                    const int baseFont = 14;
                    const int minFont = 9;
                    float fontScale = Mathf.Clamp(s, (float)minFont / (float)baseFont, 100f);
                    float extra = (fontScale > 0f) ? (s / fontScale) : 1f;
                    if (uT != null) { uT.fontSize = Mathf.RoundToInt(baseFont * fontScale); uT.transform.localScale = new Vector3(extra, extra, 1f); }
                    if (rT != null) { rT.fontSize = Mathf.RoundToInt(baseFont * fontScale); rT.transform.localScale = new Vector3(extra, extra, 1f); }
                    if (rndT != null) { rndT.fontSize = Mathf.RoundToInt(baseFont * fontScale); rndT.transform.localScale = new Vector3(extra, extra, 1f); }
                    if (hT != null) { hT.fontSize = Mathf.RoundToInt(baseFont * fontScale); hT.transform.localScale = new Vector3(extra, extra, 1f); }
                });
            }
            var footerBtnGOs = new GameObject[] {
                footerFollowAngleBtn, footerFollowDistanceBtn, footerFollowHeightBtn,
                paginationFirstBtn, paginationPrev10Btn, paginationPrevBtn,
                paginationNextBtn, paginationNext10Btn, paginationLastBtn,
                footerMenuGateBtn, footerShowHiddenPackagesBtn,
                gridSizeMinusBtn, gridSizePlusBtn,
                footerSpringScrollToggleBtn,
                footerHoldToLaunchToggleBtn,
                footerLayoutBtn, footerHeightBtn, footerAutoHideBtn,
            };
            var footerBtnFonts = new int[] {
                20, 20, 20,
                18, 18, 20,
                20, 18, 18,
                20, 20,
                24, 24,
                20,
                20,
                20, 20, 20,
            };
            for (int i = 0; i < footerBtnGOs.Length; i++)
            {
                var rt = footerBtnGOs[i] != null ? footerBtnGOs[i].GetComponent<RectTransform>() : null;
                var t = footerBtnGOs[i] != null ? footerBtnGOs[i].GetComponentInChildren<Text>() : null;
                int f = footerBtnFonts[i];
                innerPaneScaleActions.Add(s =>
                {
                    if (rt) rt.sizeDelta = new Vector2(40f * s, 40f * s);
                    if (t)
                    {
                        int baseFont = f;
                        int minFont = Mathf.Max(8, Mathf.RoundToInt(f * 0.6f));
                        float fontScale = Mathf.Clamp(s, (float)minFont / (float)baseFont, 100f);
                        t.fontSize = Mathf.RoundToInt(baseFont * fontScale);
                        float extra = (fontScale > 0f) ? (s / fontScale) : 1f;
                        t.transform.localScale = new Vector3(extra, extra, 1f);
                    }
                });
            }

            innerPaneScaleActions.Add(s => { try { LayoutScrollbarJumpButtons(s); } catch { } });

            UpdateSpringScrollButtonToggleUI();
            UpdateHoldToLaunchToggleUI();

            // Scale the back button
            {
                var rt = footerBackBtn != null ? footerBackBtn.GetComponent<RectTransform>() : null;
                innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(40f*s, 40f*s); });
            }
            // Scale the clear filter button
            {
                var rt = footerClearFilterBtn != null ? footerClearFilterBtn.GetComponent<RectTransform>() : null;
                innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(40f*s, 40f*s); });
            }

            // Scale the filter mode label
            {
                var t = footerFilterModeText;
                var rt = t != null ? t.GetComponent<RectTransform>() : null;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt) rt.sizeDelta = new Vector2(180f * s, 40f * s);
                    if (t)
                    {
                        const int baseFont = 22;
                        const int minFont = 12;
                        float fontScale = Mathf.Clamp(s, (float)minFont / (float)baseFont, 100f);
                        t.fontSize = Mathf.RoundToInt(baseFont * fontScale);
                        float extra = (fontScale > 0f) ? (s / fontScale) : 1f;
                        t.transform.localScale = new Vector3(extra, extra, 1f);
                    }
                });
            }
            // Scale the spacer
            {
                var go = footerFilterModeSpacerGO;
                var rt = go != null ? go.GetComponent<RectTransform>() : null;
                var le = go != null ? go.GetComponent<LayoutElement>() : null;
                innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(12f*s, 40f*s); if (le != null) { le.preferredWidth = 12f*s; le.minWidth = 12f*s; } });
            }

            // Unified info bar — always visible; hosts hover path, status messages, and tbox label/buttons.
            GameObject pathGO = UI.AddChildGOImage(backgroundBoxGO, new Color(0.15f, 0.15f, 0.15f, 1f), AnchorPresets.hStretchBottom, 0, 40, new Vector2(0, 40));
            pathGO.name = "HoverPathContainer";
            pathGO.GetComponent<Image>().raycastTarget = true; // tbox hover delegate needs raycasts
            // Removed RectMask2D - it was causing visual glitches/flashing during height animation during category switches
            hoverPathRT = pathGO.GetComponent<RectTransform>();

            CreateHoverPreviewOverlay(backgroundBoxGO);

            // HoverPathText — anchored to the bottom (tooltip) row; CanvasGroup fades only the text
            GameObject hoverPathTextGO = new GameObject("HoverPathText");
            hoverPathTextGO.transform.SetParent(pathGO.transform, false);
            hoverPathCanvasGroup = hoverPathTextGO.AddComponent<CanvasGroup>();
            hoverPathCanvasGroup.alpha = 0;
            hoverPathCanvasGroup.blocksRaycasts = false;
            hoverPathCanvasGroup.interactable = false;
            var hoverPathTextRT2 = hoverPathTextGO.GetComponent<RectTransform>();
            if (hoverPathTextRT2 == null) hoverPathTextRT2 = hoverPathTextGO.AddComponent<RectTransform>();
            // Bottom-row anchor: pinned to bottom of bar, fixed 60 px tall (updated by scale actions)
            hoverPathTextRT2.anchorMin        = new Vector2(0f, 0f);
            hoverPathTextRT2.anchorMax        = new Vector2(1f, 0f);
            hoverPathTextRT2.pivot            = new Vector2(0.5f, 0f);
            hoverPathTextRT2.anchoredPosition = Vector2.zero;
            hoverPathTextRT2.sizeDelta        = new Vector2(0f, 60f);
            // Scale action keeps text wrapper in sync with row height
            {
                var hpRT = hoverPathTextRT2;
                innerPaneScaleActions.Add(s => { if (hpRT != null) hpRT.sizeDelta = new Vector2(0f, 60f * s); });
            }

            hoverPathText = hoverPathTextGO.AddComponent<Text>();
            hoverPathText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hoverPathText.fontSize = 20; // Slightly smaller to ensure 2 rows fit comfortably in 60px
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
            
            // RectTransform already configured above on hoverPathTextRT2

            UpdateSideButtonsVisibility();
            UpdateFooterFollowStates();
            UpdateFooterLayoutState();
            UpdateFooterHeightState();
            UpdateFooterShowHiddenPackagesState();
            UpdateFooterAutoHideState();
            UpdateFooterVamMenuGateState();
            UpdatePaginationText();
            try { UpdateUndoRedoButtonLabels(); } catch { }
        }

        private void ToggleSpringScrollButton()
        {
            springScrollButtonEnabled = !springScrollButtonEnabled;
            try
            {
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.SpringScrollButtonEnabled = springScrollButtonEnabled;
                    VPBConfig.Instance.Save(false);
                }
            }
            catch { }

            // Only create when enabling (never create just to immediately hide).
            if (springScrollButtonEnabled)
            {
                EnsureSpringScrollButtonExists();
            }

            if (springScrollButtonGO != null)
            {
                springScrollButtonGO.SetActive(springScrollButtonEnabled);
            }
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
                footerScrollTopBtn = UI.CreateUIButton(sb.gameObject, 40, 40, "↑", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryToTop);
                footerScrollTopBtn.name = "ScrollbarScrollTop";
                { var s = UI.LoadIconSprite("vpb_icons/scroll_top.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerScrollTopBtn, s); }
                AddHoverDelegate(footerScrollTopBtn);
                AddTooltip(footerScrollTopBtn, "gallery.tooltip.scroll_top", "Jump to top of list");
            }
            if (footerScrollBottomBtn == null)
            {
                footerScrollBottomBtn = UI.CreateUIButton(sb.gameObject, 40, 40, "↓", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryToBottom);
                footerScrollBottomBtn.name = "ScrollbarScrollBottom";
                { var s = UI.LoadIconSprite("vpb_icons/scroll_bottom.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(footerScrollBottomBtn, s); }
                AddHoverDelegate(footerScrollBottomBtn);
                AddTooltip(footerScrollBottomBtn, "gallery.tooltip.scroll_bottom", "Jump to bottom of list");
            }
            if (footerScrollStepUpBtn == null)
            {
                footerScrollStepUpBtn = UI.CreateUIButton(sb.gameObject, 40, 40, "▲", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryStepUp);
                footerScrollStepUpBtn.name = "ScrollbarScrollStepUp";
                AddHoverDelegate(footerScrollStepUpBtn);
                AddTooltip(footerScrollStepUpBtn, "gallery.tooltip.scroll_step_up", "Scroll up");
            }
            if (footerScrollStepDownBtn == null)
            {
                footerScrollStepDownBtn = UI.CreateUIButton(sb.gameObject, 40, 40, "▼", 22, 0, 0, AnchorPresets.middleCenter, ScrollGalleryStepDown);
                footerScrollStepDownBtn.name = "ScrollbarScrollStepDown";
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
            if (footerScrollTopBtn == null || footerScrollBottomBtn == null || springScrollButtonGO == null) return;

            float paneS = innerPaneScaleOverride ?? (VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f);

            float btnSz = Mathf.Round(Mathf.Clamp(40f * paneS, 24f, 56f));
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

            int fs = Mathf.RoundToInt(22f * paneS);
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

            int si = springScrollButtonGO.transform.GetSiblingIndex();
            footerScrollTopBtn.transform.SetSiblingIndex(si);
            if (footerScrollStepUpBtn != null) footerScrollStepUpBtn.transform.SetSiblingIndex(si + 1);
            if (footerScrollStepDownBtn != null) footerScrollStepDownBtn.transform.SetSiblingIndex(si + 3);
            footerScrollBottomBtn.transform.SetSiblingIndex(si + 4);

            UpdateScrollbarJumpButtonsVisibility();
        }

        private void EnsureSpringScrollButtonExists()
        {
            if (springScrollButtonGO != null) return;
            if (scrollRect == null) return;

            Transform sb = null;
            try { sb = scrollRect.gameObject != null ? scrollRect.gameObject.transform.Find("Scrollbar") : null; } catch { sb = null; }

            if (sb == null) return;

            float w = isFixedLocally ? 50f : 100f;
            float h = w * 1.618f;
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
                Sprite icon = UI.LoadIconSprite("vpb_icons/scroll.png", UI.BarIconGlyphTint);
                if (icon != null)
                {
                    GameObject iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(springBtn.transform, false);
                    Image img = iconGO.AddComponent<Image>();
                    img.sprite = icon;
                    img.color = Color.white;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    irt.sizeDelta = new Vector2(-24f, -24f);
                    irt.anchoredPosition = Vector2.zero;
                }
            }
            catch { }

            try
            {
                AddTooltip(springBtn, "gallery.tooltip.spring_scroll_drag", "Hold and drag up/down to scroll (farther = faster). Release to stop.");
            }
            catch { }

            springScrollButtonGO = springBtn;

            try
            {
                EnsureScrollbarJumpButtonsExist();
                LayoutScrollbarJumpButtons();
            }
            catch { }
        }

        private void ToggleHoldToLaunch()
        {
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
            try { if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true); } catch { }
        }

        private void UpdateHoldToLaunchToggleUI()
        {
            if (footerHoldToLaunchToggleBtnImage != null)
            {
                // Toggle color: green when enabled, dim when disabled
                footerHoldToLaunchToggleBtnImage.color = holdToLaunchEnabled
                    ? new Color(0.12f, 0.55f, 0.18f, 0.85f)
                    : new Color(0f, 0f, 0f, 0.25f);
            }
            if (footerHoldToLaunchToggleIconImage != null)
            {
                // Swap icons (hold / hold_off) when available
                try
                {
                    Sprite s = holdToLaunchEnabled ? footerHoldToLaunchOnSprite : footerHoldToLaunchOffSprite;
                    if (s != null) footerHoldToLaunchToggleIconImage.sprite = s;
                }
                catch { }

                footerHoldToLaunchToggleIconImage.color = holdToLaunchEnabled
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.45f);
            }
        }

        private void UpdateSpringScrollButtonToggleUI()
        {
            // If toggle is ON but the GO was lost (e.g. language/UI rebuild), recreate it.
            if (springScrollButtonEnabled && springScrollButtonGO == null)
            {
                try { EnsureSpringScrollButtonExists(); } catch { }
            }

            try
            {
                if (footerSpringScrollToggleBtn != null)
                {
                    var b = footerSpringScrollToggleBtn.GetComponent<Button>();
                    if (b != null) b.interactable = true;
                }
            }
            catch { }

            if (footerSpringScrollToggleBtnImage != null)
            {
                footerSpringScrollToggleBtnImage.color = springScrollButtonEnabled
                    ? new Color(0f, 0f, 0f, 0.5f)
                    : new Color(0f, 0f, 0f, 0.25f);
            }
            if (footerSpringScrollToggleIconImage != null)
            {
                footerSpringScrollToggleIconImage.color = springScrollButtonEnabled
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.45f);
            }

            // If this pane is fixed (desktop overlay), keep the toggle disabled-looking.
            try
            {
                if (isFixedLocally && footerSpringScrollToggleBtn != null)
                {
                    if (footerSpringScrollToggleBtnImage != null)
                        footerSpringScrollToggleBtnImage.color = springScrollButtonEnabled
                            ? new Color(0f, 0f, 0f, 0.5f)
                            : new Color(0f, 0f, 0f, 0.25f);
                }
            }
            catch { }

            // Resize to match fixed vs floating mode
            try
            {
                if (springScrollButtonGO != null)
                {
                    float w = isFixedLocally ? 50f : 100f;
                    float h = w * 1.618f;
                    var ssb = springScrollButtonGO.GetComponent<SpringScrollButton>();
                    if (ssb != null) ssb.SetSize(w, h);
                }
                LayoutScrollbarJumpButtons();
            }
            catch { }
        }

        private void CreateHoverPreviewOverlay(GameObject parentGO)
        {
            if (parentGO == null) return;
            if (hoverPreviewGO != null) return;

            hoverPreviewGO = new GameObject("HoverPreview");
            hoverPreviewGO.transform.SetParent(parentGO.transform, false);
            hoverPreviewRT = hoverPreviewGO.AddComponent<RectTransform>();
            hoverPreviewRT.anchorMin = new Vector2(0f, 0f);
            hoverPreviewRT.anchorMax = new Vector2(0f, 0f);
            hoverPreviewRT.pivot = new Vector2(0f, 0f);

            // Backdrop
            var bg = hoverPreviewGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;

            // Actual preview
            var imgGO = new GameObject("Image");
            imgGO.transform.SetParent(hoverPreviewGO.transform, false);
            var rt = imgGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(4f, 4f);
            rt.offsetMax = new Vector2(-4f, -4f);

            hoverPreviewImage = imgGO.AddComponent<RawImage>();
            hoverPreviewImage.color = new Color(1f, 1f, 1f, 1f);
            hoverPreviewImage.raycastTarget = false;

            // Keep it hidden until first hover
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
            if (source != null) hoverPreviewSource = source;
            ShowHoverPreview(file);
        }

        public void NotifyHoverPreviewTriggerExited(UIHoverPreviewTrigger source)
        {
            if (source != null && hoverPreviewSource != null && !ReferenceEquals(source, hoverPreviewSource)) return;
            hoverPreviewSource = null;
            HideHoverPreview(null);
        }

        public void ShowHoverPreview(FileEntry file)
        {
            if (!CanShowHoverPreviewForLayout(layoutMode)) { HideHoverPreview(null); return; }
            if (file == null) { HideHoverPreview(null); return; }
            if (hoverPreviewGO == null || hoverPreviewRT == null || hoverPreviewImage == null) return;

            hoverPreviewDummyActive = false;
            hoverPreviewFile = file;
            UpdateHoverPreviewLayout();
            hoverPreviewGO.SetActive(true);

            // Full-res decode/cache tier for hover; grid-only plugin thumb hiding still off via gridThumbnailContext.
            hoverPreviewImage.color = Color.white;
            LoadThumbnail(file, hoverPreviewImage, gridThumbnailContext: false, turboJpegThumbnailDenom: 1, thumbnailUnityDecodeOnly: true);
        }

        public void HideHoverPreview(FileEntry file)
        {
            if (hoverPreviewGO == null) return;
            if (file != null && hoverPreviewFile != null && !ReferenceEquals(file, hoverPreviewFile)) return;
            hoverPreviewFile = null;
            hoverPreviewSource = null;
            if (!hoverPreviewDummyActive)
                hoverPreviewGO.SetActive(false);
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
                || !hoverPreviewSource.IsHovering
                || !hoverPreviewSource.ContainsScreenPoint(Input.mousePosition);

            if (!stale)
            {
                try { stale = !IsPointerInsideGalleryWindowRect(); } catch { }
            }

            if (stale) HideHoverPreview(null);
        }

        public void SetHoverPreviewDummyActive(bool active)
        {
            hoverPreviewDummyActive = active;
            if (!active && hoverPreviewGO != null && hoverPreviewFile == null)
            {
                hoverPreviewGO.SetActive(false);
            }
            UpdateHoverPreviewLayout();
        }

        public void RefreshHoverPreviewLayoutImmediate()
        {
            UpdateHoverPreviewLayout();
        }

        private void UpdateHoverPreviewLayout()
        {
            if (hoverPreviewRT == null) return;
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
                    return;
                }
            }
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f;

            float size = 300f;
            if (VPBConfig.Instance != null) size = Mathf.Clamp(VPBConfig.Instance.GalleryListHoverPreviewSize, 200f, 600f);

            float ox = 0f;
            float oy = 0f;
            if (VPBConfig.Instance != null)
            {
                ox = Mathf.Clamp(VPBConfig.Instance.GalleryListHoverPreviewOffsetX, -2000f, 2000f);
                oy = Mathf.Clamp(VPBConfig.Instance.GalleryListHoverPreviewOffsetY, -2000f, 2000f);
            }

            float x = (10f + ox) * s;
            float y = GalleryMainAreaBottomInset() + ((6f + oy) * s);

            hoverPreviewRT.anchoredPosition = new Vector2(x, y);
            hoverPreviewRT.sizeDelta = new Vector2(size, size);

            if (hoverPreviewGO != null)
                hoverPreviewGO.SetActive(true);
            if (hoverPreviewImage != null && hoverPreviewDummyActive && hoverPreviewFile == null)
            {
                hoverPreviewImage.texture = null;
                hoverPreviewImage.color = new Color(1f, 1f, 1f, 0.18f);
            }
            else if (hoverPreviewImage != null && hoverPreviewFile != null)
            {
                // Ensure dummy alpha doesn't stick while the real preview loads.
                if (hoverPreviewImage.color.a < 0.95f) hoverPreviewImage.color = Color.white;
            }
        }

        public void UpdatePaginationText()
        {
            if (paginationText == null) return;

            if (IsHubMode)
            {
                if (footerBackBtn != null) footerBackBtn.SetActive(false);
                if (footerClearFilterBtn != null) footerClearFilterBtn.SetActive(false);
                if (footerFilterModeText != null) footerFilterModeText.gameObject.SetActive(false);
                if (footerFilterModeSpacerGO != null) footerFilterModeSpacerGO.SetActive(false);
                if (paginationText != null) paginationText.gameObject.SetActive(true);

                // Hub still uses pagination (server-side)
                paginationText.text = string.Format(VPBTranslation.T("gallery.page", "Page {0}"), currentPage + 1);

                bool canGoPrev = (currentPage > 0);
                // For Hub we assume Next is always possible unless we implement total count
                bool canGoNext = true;

                if (paginationPrevBtn != null) 
                {
                    paginationPrevBtn.SetActive(true);
                    paginationPrevBtn.GetComponent<Button>().interactable = canGoPrev;
                }
                if (paginationPrev10Btn != null) 
                {
                    paginationPrev10Btn.SetActive(true);
                    paginationPrev10Btn.GetComponent<Button>().interactable = canGoPrev;
                }
                if (paginationFirstBtn != null) 
                {
                    paginationFirstBtn.SetActive(true);
                    paginationFirstBtn.GetComponent<Button>().interactable = canGoPrev;
                }

                if (paginationNextBtn != null) 
                {
                    paginationNextBtn.SetActive(true);
                    paginationNextBtn.GetComponent<Button>().interactable = canGoNext;
                }
                if (paginationNext10Btn != null) 
                {
                    paginationNext10Btn.SetActive(true);
                    paginationNext10Btn.GetComponent<Button>().interactable = canGoNext;
                }
                if (paginationLastBtn != null) 
                {
                    paginationLastBtn.SetActive(true);
                    paginationLastBtn.GetComponent<Button>().interactable = canGoNext;
                }

                if (tboxSelectAllBtn != null)
                {
                    Button sab = tboxSelectAllBtn.GetComponent<Button>();
                    if (sab != null) sab.interactable = false;
                }
            }
            else
            {
                bool showClearFilter = IsFilterActive;
                bool showBackBtn = IsFilterActive;
                // Hide footer filter controls when in dependency filter mode (moved to toolbox)
                if (footerBackBtn != null) footerBackBtn.SetActive(false);
                if (footerClearFilterBtn != null) footerClearFilterBtn.SetActive(false);
                if (footerFilterModeText != null) footerFilterModeText.gameObject.SetActive(false);
                if (footerFilterModeSpacerGO != null) footerFilterModeSpacerGO.SetActive(false);
                // Item count moved to tbox bar — hide it from the bottom bar in gallery mode
                if (paginationText != null) paginationText.gameObject.SetActive(false);

                // Show toolbox filter controls when in dependency filter mode
                if (tboxFilterModeRowGO != null) tboxFilterModeRowGO.SetActive(showClearFilter);
                if (tboxFilterBackBtn != null) tboxFilterBackBtn.SetActive(showBackBtn);
                if (tboxFilterClearBtn != null) tboxFilterClearBtn.SetActive(showClearFilter);

                // Refresh toolbox layout to account for filter row height change
                try { RefreshTboxFlexButtonLayout(); } catch { }

                if (showClearFilter && tboxFilterClearBtn != null)
                {
                    var btn = tboxFilterClearBtn.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(ClearPackageFilter);
                    }
                    if (tboxFilterModeText != null)
                    {
                        tboxFilterModeText.text = string.IsNullOrEmpty(GetFilterModeLabel) ? "" : $"{GetFilterModeLabel} ({GetFilterModeCount})";
                        // Set color to red for Missing filter, yellow for other filters
                        if (GetFilterModeLabel == "Missing")
                            tboxFilterModeText.color = new Color(1f, 0.2f, 0.2f, 1f);  // Red
                        else
                            tboxFilterModeText.color = new Color(1f, 0.85f, 0f, 1f);   // Yellow
                    }
                }

                // Keep the hover-path count fallback in sync with filter/search refreshes.
                try { RefreshHoverPathCountTextIfNeeded(); } catch { }

                // With RecyclingGridView, we no longer have "pages".
                // Just show total count.
                if (currentFilteredFiles != null)
                {
                    int total = currentFilteredFiles.Count;
                    int selected = selectedFiles.Count;
                    bool historyBrowse = activeContentType == ContentType.History;
                    if (historyBrowse && total == 0)
                    {
                        bool hasNameFilter = nameFilterTerms != null && nameFilterTerms.Length > 0;
                        if (lastHistoryQueryFailed || !VpbSqlite3.IsAvailable)
                        {
                            paginationText.text = VPBTranslation.T(
                                "gallery.history.unavailable_retry",
                                "History unavailable (SQLite/index). Click Refresh/Ctrl+R to retry.");
                        }
                        else if (hasNameFilter)
                        {
                            paginationText.text = VPBTranslation.T(
                                "gallery.history.empty_filtered",
                                "No History matches current filter. Clear search/filter.");
                        }
                        else
                        {
                            paginationText.text = VPBTranslation.T(
                                "gallery.history.empty",
                                "History is empty.");
                        }
                    }
                    else if (selected > 0)
                    {
                        if (historyBrowse)
                            paginationText.text = string.Format(VPBTranslation.T("gallery.items.selected_unique", "{0} unique / {1} Items"), selected, total);
                        else
                            paginationText.text = string.Format(VPBTranslation.T("gallery.items.selected", "{0} / {1} Items"), selected, total);
                    }
                    else
                        paginationText.text = string.Format(VPBTranslation.T("gallery.items.count", "{0} Items"), total);
                }
                else
                {
                    paginationText.text = VPBTranslation.T("gallery.items.zero", "0 Items");
                }

                if (tboxSelectAllBtn != null)
                {
                    Button sab = tboxSelectAllBtn.GetComponent<Button>();
                    if (sab != null)
                    {
                        int totalForSelectAll = currentFilteredFiles != null ? currentFilteredFiles.Count : 0;
                        sab.interactable = totalForSelectAll > 0 && totalForSelectAll <= SelectAllSafetyMaxItemCount;
                    }
                }

                if (paginationFirstBtn != null) paginationFirstBtn.SetActive(false);
                if (paginationPrev10Btn != null) paginationPrev10Btn.SetActive(false);
                if (paginationPrevBtn != null) paginationPrevBtn.SetActive(false);
                if (paginationNextBtn != null) paginationNextBtn.SetActive(false);
                if (paginationNext10Btn != null) paginationNext10Btn.SetActive(false);
                if (paginationLastBtn != null) paginationLastBtn.SetActive(false);
            }
        }


        private void ToggleLayoutMode()
        {
            var next = (layoutMode == GalleryLayoutMode.Grid) ? GalleryLayoutMode.List : GalleryLayoutMode.Grid;
            SetLayoutMode(next);
        }

        private void UpdateFooterLayoutState()
        {
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

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
                if (target != null) footerLayoutIconImage.sprite = target;
            }

            if (footerLayoutBtn != null)
            {
                var del = footerLayoutBtn.GetComponent<UIHoverDelegate>();
                if (del != null) del.OnHoverChange = null;
                bool blockGridWhileSettings = IsSettingsPanelOpen() || settingsListViewActive;
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
            UpdateLayout();
        }

        private void UpdateFooterHeightState()
        {
            if (VPBConfig.Instance == null) return;

            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

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
                if (target != null) footerHeightIconImage.sprite = target;
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
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.GalleryShowHiddenPackages = !VPBConfig.Instance.GalleryShowHiddenPackages;
            VPBConfig.Instance.Save();
            UpdateFooterShowHiddenPackagesState();
            try
            {
                if (TryFastApplyGalleryShowHiddenToggle(true))
                    return;
            }
            catch { }
            try { RefreshFiles(true); } catch { }
        }

        private void UpdateFooterShowHiddenPackagesState()
        {
            if (VPBConfig.Instance == null) return;

            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            if (footerShowHiddenPackagesBtnImage != null)
                footerShowHiddenPackagesBtnImage.color = VPBConfig.Instance.GalleryShowHiddenPackages ? activeColor : inactiveColor;

            if (footerShowHiddenPackagesBtnText != null)
                footerShowHiddenPackagesBtnText.text = "H";

            if (footerShowHiddenIconImage != null)
            {
                Sprite target = VPBConfig.Instance.GalleryShowHiddenPackages ? footerShowHiddenOnSprite : footerShowHiddenOffSprite;
                if (target != null) footerShowHiddenIconImage.sprite = target;
            }

            if (footerShowHiddenPackagesBtn != null)
            {
                var del = footerShowHiddenPackagesBtn.GetComponent<UIHoverDelegate>();
                if (del != null) del.OnHoverChange = null;
                // True = gallery list includes .hide-marked packages; false = omit them (stricter filter).
                string modeText = VPBConfig.Instance.GalleryShowHiddenPackages ? "Show Hidden" : "Hide Hidden";
                AddTooltipPlain(footerShowHiddenPackagesBtn, modeText);
            }
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
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            if (footerMenuGateBtnImage != null)
                footerMenuGateBtnImage.color = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible ? activeColor : inactiveColor;
            if (footerMenuGateIconImage != null)
            {
                Sprite target = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible ? footerMenuGateOnSprite : footerMenuGateOffSprite;
                if (target != null) footerMenuGateIconImage.sprite = target;
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

            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

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
                if (target != null) footerAutoHideIconImage.sprite = target;
            }

            if (footerAutoHideBtn != null)
            {
                var del = footerAutoHideBtn.GetComponent<UIHoverDelegate>();
                if (del != null) del.OnHoverChange = null;
                AddTooltipPlain(
                    footerAutoHideBtn,
                    VPBConfig.Instance.DesktopFixedAutoCollapse
                        ? VPBTranslation.T("gallery.tooltip.autohide_enabled", "Auto-Hide (Enabled)")
                        : VPBTranslation.T("gallery.tooltip.autohide_disabled", "Auto-Hide (Disabled)"));
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
            
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            
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
                string msg = VPBTranslation.T(tooltipKey, englishDefault);
                if (enter)
                {
                    if (temporaryStatusCoroutine != null)
                    {
                        StopCoroutine(temporaryStatusCoroutine);
                        temporaryStatusCoroutine = null;
                    }
                    temporaryStatusMsg = msg;
                    temporaryStatusOwner = go;
                }
                else if (temporaryStatusMsg == msg)
                {
                    temporaryStatusMsg = null;
                    if (temporaryStatusOwner == go) temporaryStatusOwner = null;
                }
            };
            del.TooltipHandler = handler;
            del.OnHoverChange += handler;
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
                {
                    if (temporaryStatusCoroutine != null)
                    {
                        StopCoroutine(temporaryStatusCoroutine);
                        temporaryStatusCoroutine = null;
                    }
                    temporaryStatusMsg = tooltip;
                    temporaryStatusOwner = go;
                }
                else if (temporaryStatusMsg == tooltip)
                {
                    temporaryStatusMsg = null;
                    if (temporaryStatusOwner == go) temporaryStatusOwner = null;
                }
            };
            del.TooltipHandler = handler;
            del.OnHoverChange += handler;
        }

        private void UpdateDesktopModeButton()
        {
            if (VPBConfig.Instance == null) return;

            bool isVR = XrUtils.IsVrActive();

            bool fixedMode = isFixedLocally;
            string text = fixedMode
                ? VPBTranslation.T("gallery.desktop.floating", "Floating")
                : VPBTranslation.T("gallery.desktop.fixed", "Fixed");
            Color color = fixedMode ? new Color(0.15f, 0.45f, 0.6f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);
            Sprite deskSpr = fixedMode ? galleryFloatSprite : galleryFixedSprite;

            GameObject rightDeskGo = rightDesktopModeBtnImage != null ? rightDesktopModeBtnImage.gameObject : null;
            if (rightDeskGo != null) rightDeskGo.SetActive(!isVR);

            if (rightDesktopModeBtnIconImage != null && deskSpr != null)
            {
                rightDesktopModeBtnIconImage.sprite = deskSpr;
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
                leftDesktopModeBtnIconImage.sprite = deskSpr;
                leftDesktopModeBtnIconImage.enabled = true;
                if (leftDesktopModeBtnText != null) leftDesktopModeBtnText.gameObject.SetActive(false);
            }
            else if (leftDesktopModeBtnText != null)
            {
                leftDesktopModeBtnText.gameObject.SetActive(true);
                leftDesktopModeBtnText.text = text;
            }

            if (leftDesktopModeBtnImage != null) leftDesktopModeBtnImage.color = color;

            if (footerFollowAngleBtn != null) footerFollowAngleBtn.SetActive(!fixedMode);
            if (footerFollowDistanceBtn != null) footerFollowDistanceBtn.SetActive(!fixedMode);
            if (footerFollowHeightBtn != null) footerFollowHeightBtn.SetActive(!fixedMode);
            if (footerDockBtn != null) footerDockBtn.SetActive(fixedMode);
            if (footerHeightBtn != null) footerHeightBtn.SetActive(fixedMode);
            if (footerAutoHideBtn != null) footerAutoHideBtn.SetActive(fixedMode);

            SyncSideFollowRailButtonsVisibility();

            UpdateSideButtonPositions();
            UpdateFooterDockButtonState();
            UpdateFooterAutoHideState();
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
            if (spr != null) footerDockIconImage.sprite = spr;
        }

        /// <summary>Camera-follow is only meaningful when the panel is not fixed; hide side-rail follow controls in fixed mode.</summary>
        private void SyncSideFollowRailButtonsVisibility()
        {
            bool show = !isFixedLocally;
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
                }
                else
                {
                    leftPrevActiveContent = leftActiveContent;
                    leftActiveContent = ContentType.RemoveClothing;
                }
            }
            else
            {
                if (rightActiveContent == ContentType.RemoveClothing)
                {
                    rightActiveContent = rightPrevActiveContent;
                }
                else
                {
                    rightPrevActiveContent = rightActiveContent;
                    rightActiveContent = ContentType.RemoveClothing;
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
            UpdateRemoveButtonLabels(leftRemoveAllClothingBtn, rightRemoveAllClothingBtn, "Remove\nClothing", optionCount);
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
            isCollapsed = collapsed;
            collapseTimer = 0f;
            if (collapsed)
            {
                try { HideHoverPreview(null); } catch { }
            }

            if (backgroundBoxGO != null)
            {
                RectTransform rt = backgroundBoxGO.GetComponent<RectTransform>();
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
            }

            ApplyFixedCollapseTriggerVisuals();
            
            UpdateSideButtonsVisibility();
            UpdateLayout();
        }

        private void NextPage()
        {
            currentPage++;
            RefreshFiles();
        }

        private void Next10Page()
        {
            currentPage += 10;
            RefreshFiles();
        }

        private void FirstPage()
        {
            currentPage = 0;
            RefreshFiles(false, true);
        }

        private void LastPage()
        {
            currentPage = Mathf.Max(0, lastTotalPages - 1);
            RefreshFiles();
        }

        private void PrevPage()
        {
            currentPage = Mathf.Max(0, currentPage - 1);
            RefreshFiles(false, true);
        }

        private void Prev10Page()
        {
            if (currentPage > 0)
            {
                currentPage = Mathf.Max(0, currentPage - 10);
                RefreshFiles(false, true);
            }
        }

        /// <summary>Select every item in <see cref="currentFilteredFiles"/> when within <see cref="SelectAllSafetyMaxItemCount"/>.</summary>
        /// <returns>True if selection was applied.</returns>
        private bool TrySelectAllCurrentGalleryView(string source)
        {
            if (IsHubMode) return false;

            var list = currentFilteredFiles;
            if (list == null || list.Count == 0) return false;

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
            selectedHubItem = null;
            SetHoverPath("");
            RefreshSelectionVisuals();
            UpdatePaginationText();
        }

        private void AdjustGridColumns(int delta)
        {
            RecyclingGridView rgvState = null;
            if (contentGO != null) rgvState = contentGO.GetComponent<RecyclingGridView>();

            if (IsSettingsPanelOpen() || settingsListViewActive)
            {
                if (contentGO != null)
                {
                    RecyclingGridView rgv = rgvState != null ? rgvState : contentGO.GetComponent<RecyclingGridView>();
                    if (rgv != null)
                    {
                        rgv.fixedColumns = 1;
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

        private void ToggleRight(ContentType type)
        {
            if (type == ContentType.Settings) HideGlobalSourceFilterDropdownIfOpen();
            bool hadSettingsPanel = IsSettingsPanelOpen();
            bool userTagsWasOpen = leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags;
            if (type != ContentType.Settings && (hadSettingsPanel || settingsListViewActive))
                ExitInternalSettingsMode(true);
            if (type == ContentType.Creator && VPBConfig.Instance != null && VPBConfig.Instance.GalleryHideCreatorSideButtons
                && leftActiveContent != ContentType.Creator && rightActiveContent != ContentType.Creator)
                return;
            if (type == ContentType.UserTags)
                ForceCloseSettingsSidePanels();
            bool hadHistorySide = leftActiveContent == ContentType.History || rightActiveContent == ContentType.History;
            bool wasCleanup = cleanupModeActive;
            if (wasCleanup && (type == ContentType.Category || type == ContentType.Creator || type == ContentType.UserTags || type == ContentType.Path || type == ContentType.History))
                ExitCleanupModeForSidePanelNavigation();

            if (IsSubmenuContentType(type)) CloseOtherSideIfSubmenu(false);
            bool timeCategoryCreatorSwitch = LogCategoryCreatorSideTabSwitchTiming
                && (type == ContentType.Category || type == ContentType.Creator || type == ContentType.UserTags || type == ContentType.Path || type == ContentType.History);
            if (timeCategoryCreatorSwitch)
                BeginSideTabCategoryCreatorTiming("right");

            if (wasCleanup && (type == ContentType.Category || type == ContentType.Creator || type == ContentType.UserTags || type == ContentType.Path || type == ContentType.History))
            {
                // After leaving cleanup, explicit Category/Creator click should open the requested list.
                rightActiveContent = type;
                if (leftActiveContent == type) leftActiveContent = null;
            }
            else if (rightActiveContent == type) 
            {
                rightActiveContent = null;
            }
            else 
            {
                rightActiveContent = type;
                // Collapse Left IF it is the SAME type
                if (leftActiveContent == type) leftActiveContent = null;
            }

            SyncActiveContentTypeFromSidePanels();
            bool hasHistorySide = leftActiveContent == ContentType.History || rightActiveContent == ContentType.History;
            if (!hasHistorySide && hadHistorySide && titleText != null)
                titleText.text = currentCategoryTitle;

            bool hasSettingsPanel = IsSettingsPanelOpen();
            if (!hadSettingsPanel && hasSettingsPanel)
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, settingsFilter ?? "", _titleBarSearchOnValueChanged); } catch { }
            else if (hadSettingsPanel && !hasSettingsPanel)
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, nameFilter ?? "", _titleBarSearchOnValueChanged); } catch { }

            // Ensure settings list view active before layout pass so first click opens list immediately.
            if (type == ContentType.Settings)
                SyncInternalSettingsListView();

            UpdateLayout();
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

        private void ToggleLeft(ContentType type)
        {
            if (type == ContentType.Settings) HideGlobalSourceFilterDropdownIfOpen();
            bool hadSettingsPanel = IsSettingsPanelOpen();
            bool userTagsWasOpen = leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags;
            if (type != ContentType.Settings && (hadSettingsPanel || settingsListViewActive))
                ExitInternalSettingsMode(true);
            if (type == ContentType.Creator && VPBConfig.Instance != null && VPBConfig.Instance.GalleryHideCreatorSideButtons
                && leftActiveContent != ContentType.Creator && rightActiveContent != ContentType.Creator)
                return;
            if (type == ContentType.UserTags)
                ForceCloseSettingsSidePanels();
            bool hadHistorySide = leftActiveContent == ContentType.History || rightActiveContent == ContentType.History;
            bool wasCleanup = cleanupModeActive;
            if (wasCleanup && (type == ContentType.Category || type == ContentType.Creator || type == ContentType.UserTags || type == ContentType.Path || type == ContentType.History))
                ExitCleanupModeForSidePanelNavigation();

            if (IsSubmenuContentType(type)) CloseOtherSideIfSubmenu(true);
            bool timeCategoryCreatorSwitch = LogCategoryCreatorSideTabSwitchTiming
                && (type == ContentType.Category || type == ContentType.Creator || type == ContentType.UserTags || type == ContentType.Path || type == ContentType.History);
            if (timeCategoryCreatorSwitch)
                BeginSideTabCategoryCreatorTiming("left");

            if (wasCleanup && (type == ContentType.Category || type == ContentType.Creator || type == ContentType.UserTags || type == ContentType.Path || type == ContentType.History))
            {
                // After leaving cleanup, explicit Category/Creator click should open the requested list.
                leftActiveContent = type;
                if (rightActiveContent == type) rightActiveContent = null;
            }
            else if (leftActiveContent == type)
            {
                leftActiveContent = null;
            }
            else
            {
                leftActiveContent = type;
                // Collapse Right IF it is the SAME type
                if (rightActiveContent == type) rightActiveContent = null;
            }

            SyncActiveContentTypeFromSidePanels();
            bool hasHistorySide = leftActiveContent == ContentType.History || rightActiveContent == ContentType.History;
            if (!hasHistorySide && hadHistorySide && titleText != null)
                titleText.text = currentCategoryTitle;

            bool hasSettingsPanel = IsSettingsPanelOpen();
            if (!hadSettingsPanel && hasSettingsPanel)
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, settingsFilter ?? "", _titleBarSearchOnValueChanged); } catch { }
            else if (hadSettingsPanel && !hasSettingsPanel)
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, nameFilter ?? "", _titleBarSearchOnValueChanged); } catch { }

            // Ensure settings list view active before layout pass so first click opens list immediately.
            if (type == ContentType.Settings)
                SyncInternalSettingsListView();

            UpdateLayout();
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

            bool userTagsNowOpenLeft = leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags;
            if (!userTagsWasOpen && userTagsNowOpenLeft)
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

            if (rightReplaceBtnIconImage != null && modeSprite != null)
            {
                if (rightReplaceBtnText != null) rightReplaceBtnText.gameObject.SetActive(false);
                rightReplaceBtnIconImage.sprite = modeSprite;
                rightReplaceBtnIconImage.enabled = true;
            }
            else if (rightReplaceBtnText != null)
            {
                rightReplaceBtnText.gameObject.SetActive(true);
                rightReplaceBtnText.text = text;
            }
            if (rightReplaceBtnImage != null) rightReplaceBtnImage.color = color;

            if (leftReplaceBtnIconImage != null && modeSprite != null)
            {
                if (leftReplaceBtnText != null) leftReplaceBtnText.gameObject.SetActive(false);
                leftReplaceBtnIconImage.sprite = modeSprite;
                leftReplaceBtnIconImage.enabled = true;
            }
            else if (leftReplaceBtnText != null)
            {
                leftReplaceBtnText.gameObject.SetActive(true);
                leftReplaceBtnText.text = text;
            }
            if (leftReplaceBtnImage != null) leftReplaceBtnImage.color = color;
        }

        public void RefreshAppearanceClothingSideButton()
        {
            UpdateKeepClothingButtonState();
        }

        private void UpdateKeepClothingButtonState()
        {
            string m = AppearanceClothingApplyMode ?? "replace";
            string text;
            Color color;
            if (string.Equals(m, "keep", StringComparison.OrdinalIgnoreCase))
            {
                text = VPBTranslation.T("gallery.clothes.keep", "Clothes: Keep");
                color = new Color(0.15f, 0.35f, 0.55f, 1f);
            }
            else if (string.Equals(m, "clothingonly", StringComparison.OrdinalIgnoreCase))
            {
                text = VPBTranslation.T("gallery.clothes.only", "Clothes: Only");
                color = new Color(0.15f, 0.45f, 0.28f, 1f);
            }
            else
            {
                text = VPBTranslation.T("gallery.clothes.preset", "Clothes: Preset");
                color = new Color(0.35f, 0.3f, 0.2f, 1f);
            }

            if (rightKeepClothingBtnText != null) rightKeepClothingBtnText.text = text;
            if (rightKeepClothingBtnImage != null) rightKeepClothingBtnImage.color = color;

            if (leftKeepClothingBtnText != null) leftKeepClothingBtnText.text = text;
            if (leftKeepClothingBtnImage != null) leftKeepClothingBtnImage.color = color;
        }

        private void ToggleReplaceMode()
        {
            DragDropReplaceMode = !DragDropReplaceMode;
            UpdateReplaceButtonState();
        }

        private void ToggleKeepClothingMode()
        {
            string m = AppearanceClothingApplyMode ?? "replace";
            if (string.Equals(m, "replace", StringComparison.OrdinalIgnoreCase))
                AppearanceClothingApplyMode = "keep";
            else if (string.Equals(m, "keep", StringComparison.OrdinalIgnoreCase))
                AppearanceClothingApplyMode = "clothingonly";
            else
                AppearanceClothingApplyMode = "replace";
            UpdateKeepClothingButtonState();
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
                rightApplyModeBtnIconImage.sprite = modeSprite;
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
                leftApplyModeBtnIconImage.sprite = modeSprite;
                leftApplyModeBtnIconImage.enabled = true;
            }
            else if (leftApplyModeBtnText != null)
            {
                leftApplyModeBtnText.gameObject.SetActive(true);
                leftApplyModeBtnText.text = text;
            }
            if (leftApplyModeBtnImage != null) leftApplyModeBtnImage.color = color;

            // Hold-to-launch overrides 1-click apply: disable the toggle button while hold mode is on.
            bool disableApplyToggle = holdToLaunchEnabled;
            try
            {
                if (rightApplyModeBtnImage != null)
                {
                    var b = rightApplyModeBtnImage.GetComponent<Button>();
                    if (b != null) b.interactable = !disableApplyToggle;
                    if (disableApplyToggle) rightApplyModeBtnImage.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
                    // Tooltip swap (best-effort)
                    AddTooltip(rightApplyModeBtnImage.gameObject,
                        disableApplyToggle ? "gallery.tooltip.apply_mode_disabled_hold_to_launch" : "gallery.tooltip.apply_mode",
                        disableApplyToggle ? "Hold-to-launch is ON. Turn it off to change 1-click/2-click apply." : "Toggle 1-click vs 2-click apply.");
                }
                if (leftApplyModeBtnImage != null)
                {
                    var b = leftApplyModeBtnImage.GetComponent<Button>();
                    if (b != null) b.interactable = !disableApplyToggle;
                    if (disableApplyToggle) leftApplyModeBtnImage.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
                    AddTooltip(leftApplyModeBtnImage.gameObject,
                        disableApplyToggle ? "gallery.tooltip.apply_mode_disabled_hold_to_launch" : "gallery.tooltip.apply_mode",
                        disableApplyToggle ? "Hold-to-launch is ON. Turn it off to change 1-click/2-click apply." : "Toggle 1-click vs 2-click apply.");
                }
            }
            catch { }
        }

        private void ToggleApplyMode()
        {
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
        }


    }

}
