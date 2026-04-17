using MeshVR;
using MVR.FileManagement;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
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

                string normalizedPath = presetFile.Path;
                string packageName;
                string fileName;
                var parts = presetFile.Path.Split(':');
                if (parts.Length > 1)
                {
                    packageName = parts[0].Split('/').Last().Replace(".var", string.Empty).Replace(".zip", string.Empty);
                    fileName = parts[1];
                    normalizedPath = packageName + ":" + fileName;
                }
                else
                {

                    packageName = null;
                    fileName = parts[0].TrimStart('/');
                    normalizedPath = fileName;

                }
                var convertedPath = "Saves/scene/VPB/" + packageName  + "/" + fileName.Split('/').Last();

                JSONClass personPreset;
                if (File.Exists(convertedPath))
                {
                    LogUtil.Log($"Reading pre-converted file {convertedPath}");
                    var json = SuperController.singleton.LoadJSON(convertedPath).AsObject.RemoveNonPersonAtomsMutable();
                    personPreset = json["atoms"][0].AsObject;
                }
                else
                {
                    LogUtil.Log($"Reading original file {normalizedPath}");

                    var json = CUAConverter.GetConvertedScene(normalizedPath);

                    Directory.CreateDirectory(Path.GetDirectoryName(convertedPath));
                    SuperController.singleton.SaveJSON(json, convertedPath);

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

                    personPreset = json["atoms"][0].AsObject;
                }


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
