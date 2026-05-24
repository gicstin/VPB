using MeshVR;
using MVR.FileManagement;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using global::VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        /// <summary>
        /// Path passed to <see cref="SuperController.LoadJSON"/> for a file inside a .var.
        /// Must use the registered package UID from the VAR meta (same as <see cref="VarPackage.Uid"/>),
        /// not the .var filename from disk — filenames can differ in casing/spelling from the UID.
        /// </summary>
        private static string BuildVarScopedJsonLoadPath(FileEntry entry)
        {
            if (entry == null) return null;

            // Prefer indexed Uid (packageUid:/exact/internal/path) — matches zip entry keys; rebuilding from Path
            // can diverge when folders/files have irregular spaces (e.g. "12 05/ KM214" vs "12 05/KM214").
            if (!string.IsNullOrEmpty(entry.Uid))
            {
                string u = entry.Uid.Replace('\\', '/');
                int ux = u.IndexOf(":/", StringComparison.Ordinal);
                if (ux > 0)
                {
                    string pref = u.Substring(0, ux);
                    if (pref.IndexOf('/') < 0)
                        return u;
                }
            }

            string path = (entry.Path ?? "").Replace('\\', '/');
            int sep = path.IndexOf(":/", StringComparison.Ordinal);

            string uid = null;
            if (entry is VarFileEntry vfe)
                uid = vfe.GetRowPackageUid();

            if (string.IsNullOrEmpty(uid))
                uid = TryGetPackageUidForEntry(entry);

            // Prefer manifest/index UID + path-after-colon from gallery Path (fixes UID vs .var filename mismatch).
            // VaM virtual refs require ":/" after the UID (same as VarFileEntry), not "uid:Saves/..." alone.
            if (sep >= 0 && !string.IsNullOrEmpty(uid))
                return uid + ":/" + NormalizeVarInternalPath(path.Substring(sep + 2));

            if (sep >= 0)
            {
                string prefix = sep > 0 ? path.Substring(0, sep) : "";
                uid = prefix.Split('/').Last().Replace(".var", string.Empty).Replace(".zip", string.Empty);
                return uid + ":/" + NormalizeVarInternalPath(path.Substring(sep + 2));
            }

            return path;
        }

        /// <summary>
        /// Zip/gallery paths sometimes contain a stray space after '/' (e.g. Saves/scene/ Emilie.json).
        /// VaM's LoadJSON is picky; collapse "/ " without stripping intentional spaces inside file names elsewhere.
        /// </summary>
        private static string NormalizeVarInternalPath(string inner)
        {
            if (string.IsNullOrEmpty(inner)) return inner;
            inner = inner.Replace('\\', '/').TrimStart('/');
            while (inner.Contains("/ "))
                inner = inner.Replace("/ ", "/");
            return inner;
        }

        private void TboxSceneImportSelectedPackage()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }
                else if (selectedFiles.Count != 1)
                {
                    ShowTemporaryStatus("Select only one package to import from (TODO)");
                    return;
                }

                var presetFile = selectedFiles[0];

                LogUtil.Log($"Attempting to import from {presetFile.Path}");

                if (SelectedTargetAtom == null)
                {
                    LogUtil.LogWarning("[VPB] Scene import aborted: no SelectedTargetAtom (choose target Person in gallery).");
                    ShowTemporaryStatus("Choose a target Person (gallery target menu) before importing.");
                    return;
                }

                string normalizedPath = BuildVarScopedJsonLoadPath(presetFile);
                if (string.IsNullOrEmpty(normalizedPath))
                {
                    LogUtil.LogWarning("[VPB] Scene import aborted: BuildVarScopedJsonLoadPath returned empty.");
                    ShowTemporaryStatus("Could not resolve package path.");
                    return;
                }

                string packageKey = normalizedPath;
                int colon = normalizedPath.IndexOf(":/", StringComparison.Ordinal);
                if (colon > 0)
                    packageKey = normalizedPath.Substring(0, colon);

                string fileLeaf = colon >= 0 && colon + 2 < normalizedPath.Length
                    ? Path.GetFileName(normalizedPath.Substring(colon + 2).TrimEnd('/'))
                    : Path.GetFileName(normalizedPath);

                if (string.IsNullOrEmpty(fileLeaf))
                    fileLeaf = "scene.json";

                var convertedPath = "Saves/scene/VPB/" + packageKey + "/" + fileLeaf;

                JSONClass sceneJson;
                if (File.Exists(convertedPath))
                {
                    LogUtil.Log($"Reading pre-converted file {convertedPath}");
                    sceneJson = SuperController.singleton.LoadJSON(convertedPath)?.AsObject;
                    if (sceneJson == null)
                    {
                        ShowTemporaryStatus("Could not read cached scene; delete Saves/scene/VPB cache and retry.");
                        return;
                    }
                }
                else
                {
                    LogUtil.Log($"Reading original file {normalizedPath}");

                    sceneJson = CUAConverter.GetConvertedScene(normalizedPath, onlyPersonAtoms: false, presetFile);

                    Directory.CreateDirectory(Path.GetDirectoryName(convertedPath));
                    SuperController.singleton.SaveJSON(sceneJson, convertedPath);
                    LocalSceneGallerySupport.TryEnsureVpbGeneratedSceneHideMarker(convertedPath);

                    var itemControl = SelectedTargetAtom.GetComponentInChildren<DAZClothingItemControl>();
                    if (itemControl)
                    {
                        LogUtil.Log($"Refreshing clothing items!");
                        itemControl.RefreshClothingItems();
                    }
                    else
                    {
                        LogUtil.LogError($"No DAZClothingItemControl!");
                    }
                }

                // Same goal as LoadPose / preset import: install host VAR + dependency VARs before Restore.
                // Do NOT use EnsureInstalledByText(sceneJson.ToString()) here — full-scene JSON strings duplicate
                // memory and can fragment Mono badly ("too many heap sections") on large scenes / repeated imports.
                // UI.EnsureInstalled scans deps via VarNameParser on the selected FileEntry stream (same as UI.LoadSceneFile).
                List<string> movedUids = new List<string>(48);
                bool depsChanged = false;
                try
                {
                    depsChanged = UI.EnsureInstalled(presetFile, movedUids);
                }
                catch (Exception ensureEx)
                {
                    LogUtil.LogWarning("[VPB] Scene import EnsureInstalled: " + ensureEx.Message);
                }

                if (depsChanged)
                {
                    try
                    {
                        RefreshScope scope = (movedUids != null && movedUids.Count > 0)
                            ? RefreshScope.InstallOnly
                            : RefreshScope.Both;
                        FileManagerBridge.Refresh("scene_import", scope, movedUids, flushNativeImmediately: true);
                    }
                    catch (Exception refreshEx)
                    {
                        LogUtil.LogWarning("[VPB] Scene import FileManager refresh: " + refreshEx.Message);
                    }
                }

                sceneJson = sceneJson.RemoveNonPersonAtomsMutable();
                if (sceneJson["atoms"] == null || sceneJson["atoms"].AsArray.Count == 0)
                {
                    ShowTemporaryStatus("No Person atom in scene.");
                    return;
                }

                JSONClass personPreset = sceneJson["atoms"][0].AsObject;

                string clothingMode = VPBConfig.Instance?.AppearanceClothingApplyMode ?? "replace";
                JSONClass importPreset = VpbImport.WrapAtomNodeAsPreset(personPreset);

                // Translate clothingMode string to ClothingApplyMode
                ClothingApplyMode applyMode;
                if (clothingMode == "keep")
                {
                    applyMode = ClothingApplyMode.Keep;
                }
                else if (clothingMode == "replace")
                {
                    applyMode = ClothingApplyMode.Replace;
                }
                else if (clothingMode == "merge")
                {
                    applyMode = ClothingApplyMode.Merge;
                }
                else if (string.Equals(clothingMode, "clothingonly", StringComparison.OrdinalIgnoreCase))
                {
                    applyMode = ClothingApplyMode.ClothingOnly;
                }
                else
                {
                    applyMode = ClothingApplyMode.Replace;
                }

                VpbImport.LoadPreset(presetFile, SelectedTargetAtom, VpbResourceType.Appearance, applyMode, presetJC: importPreset);
                if (SuperController.singleton != null && personPreset["id"] != null)
                {
                    SuperController.singleton.RenameAtom(SelectedTargetAtom, personPreset["id"]);
                }


                ShowTemporaryStatus($"Done", 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxSceneImportSelectedPackage error: " + ex);
                ShowTemporaryStatus("Import failed. See log.", 2f);
            }
        }

        private void ToggleSuppressAppearanceScale()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.SuppressAppearanceScaleChange =
                !VPBConfig.Instance.SuppressAppearanceScaleChange;
            try { VPBConfig.Instance.Save(true, true); } catch { }
            RefreshSuppressScaleBtnVisual();
            ShowTemporaryStatus(
                VPBConfig.Instance.SuppressAppearanceScaleChange
                    ? "Scale change on Appearance Import: SUPPRESSED"
                    : "Scale change on Appearance Import: ALLOWED",
                1.5f);
        }

        private void RefreshSuppressScaleBtnVisual()
        {
            if (tboxSuppressScaleBtn == null) return;
            var img = tboxSuppressScaleBtn.GetComponent<UnityEngine.UI.Image>();
            if (img == null) return;
            bool on = VPBConfig.Instance != null && VPBConfig.Instance.SuppressAppearanceScaleChange;
            img.color = on
                ? new Color(0.2f, 0.45f, 0.75f, 0.9f)
                : UI.IconButtonBackdrop;
        }

    }
}
