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

                LogUtil.Log($"Attempting to import from {presetFile}");

                if (!TryGetTboxResolvablePackageState(presetFile, out string uid, out FileEntry f, out _, out _, out _))
                    return;

                if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out string absLocalJson, out _, false))
                {
                    LogUtil.Log($"Importing {uid} from local scene {absLocalJson}");
                    return;
                }

                string path = ResolveVarPathForUid(uid);
                if (string.IsNullOrEmpty(path)) return;
                LogUtil.Log($"Normalizing {presetFile.Path}");
                var normalizedPath = Regex.Replace(
                    Regex.Match(presetFile.Path, "([^/]*:/?.*)").Groups[0].Value, "\\.var|\\.zip", string.Empty
                );
                LogUtil.Log($"Reading file {normalizedPath}");

                var json = CUAConverter.GetConvertedScene(normalizedPath);
                var personPreset = json["atoms"][0].AsObject;


                SelectedTargetAtom.PreRestore(restorePhysical: false, restoreAppearance: true);
                SelectedTargetAtom.Restore(personPreset, restorePhysical: false, restoreAppearance: true, restoreCore: false);
                SelectedTargetAtom.LateRestore(personPreset, restorePhysical: false, restoreAppearance: true, restoreCore: false);
                SelectedTargetAtom.PostRestore(restorePhysical: false, restoreAppearance: true);
                if (SuperController.singleton != null && personPreset["id"] != null)
                {
                    SuperController.singleton.RenameAtom(SelectedTargetAtom, personPreset["id"]);
                }



                //string convertedPath = "Saves/scene/VPB/" + Regex.Match(normalizedPath, "([^:/]*\\.json)").Groups[1].Value;
                //Directory.CreateDirectory(Path.GetDirectoryName(convertedPath));
                //if (File.Exists(convertedPath))
                //{
                //    File.Delete(convertedPath);
                //}
                //LogUtil.Log($"Saving to file {convertedPath}");
                //SuperController.singleton.SaveJSON(json, convertedPath);

                //var builder = SuperController.singleton.packageBuilder;
                //var oldCreatorName = UserPreferences.singleton.creatorName;
                //UserPreferences.singleton.creatorName = "VPB";
                //builder.LoadMetaFromPackageUid(normalizedPath.Split(':')[0]);
                //builder.ClearContentItems();
                //builder.AddContentItem("Custom/Clothing/Female/VPB/CUA Clothing/CUA Clothing Dummy.vam");
                //builder.AddContentItem(convertedPath);
                //builder.PrepPackage();
                //LogUtil.Log("Finalizing...");
                //builder.FinalizePackage();
                //File.Delete(convertedPath);
                //UserPreferences.singleton.creatorName = oldCreatorName;

                ShowTemporaryStatus($"Done", 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TboxAutoInstallSelectedPackages error: " + ex);
                ShowTemporaryStatus("Autoinstall failed. See log.", 2f);
            }
        }

    }
}
