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
                return uid + ":/" + path.Substring(sep + 2).TrimStart('/');

            // Path has no ":/" — may already be a plain saves path or virtual Uid-only row.
            if (!string.IsNullOrEmpty(entry.Uid))
            {
                string u = entry.Uid.Replace('\\', '/');
                int us = u.IndexOf(":/", StringComparison.Ordinal);
                if (us > 0)
                {
                    string pref = u.Substring(0, us);
                    // Valid virtual ref: "creator.pkg.1:/Saves/..." — reject "AllPackages/x.var:/..." masquerading as Uid.
                    if (pref.IndexOf('/') < 0)
                        return u;
                }
            }

            if (sep >= 0)
            {
                string prefix = sep > 0 ? path.Substring(0, sep) : "";
                uid = prefix.Split('/').Last().Replace(".var", string.Empty).Replace(".zip", string.Empty);
                return uid + ":/" + path.Substring(sep + 2).TrimStart('/');
            }

            return path;
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

                string normalizedPath = BuildVarScopedJsonLoadPath(presetFile);
                if (string.IsNullOrEmpty(normalizedPath))
                {
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

                    // TODO: this is not always necessary
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

                // Same as LoadPose / preset import: move referenced VARs from AllPackages → AddonPackages so Restore can resolve paths.
                try
                {
                    if (FileButton.EnsureInstalledByText(sceneJson.ToString()))
                    {
                        MVR.FileManagement.FileManager.Refresh();
                        FileManager.Refresh();
                    }
                }
                catch (Exception ensureEx)
                {
                    LogUtil.LogWarning("[VPB] Scene import EnsureInstalled: " + ensureEx.Message);
                }

                sceneJson = sceneJson.RemoveNonPersonAtomsMutable();
                if (sceneJson["atoms"] == null || sceneJson["atoms"].AsArray.Count == 0)
                {
                    ShowTemporaryStatus("No Person atom in scene.");
                    return;
                }

                JSONClass personPreset = sceneJson["atoms"][0].AsObject;

                SelectedTargetAtom.PreRestore(restorePhysical: false, restoreAppearance: true);
                SelectedTargetAtom.Restore(personPreset, restorePhysical: false, restoreAppearance: true, restoreCore: false);
                SelectedTargetAtom.PostRestore(restorePhysical: false, restoreAppearance: true);
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

    }
}
