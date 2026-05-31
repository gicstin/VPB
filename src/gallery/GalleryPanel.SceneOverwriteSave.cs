using System;
using System.IO;

namespace VPB
{
    public partial class GalleryPanel
    {
        private struct SceneOverwriteSaveContext
        {
            public string DefaultFolder;
            public string DefaultFileName;

            public bool HasPrefill => !string.IsNullOrEmpty(DefaultFileName);
        }

        private static bool TryParseSavesSceneRelativePath(string galleryRelativePath, out string defaultFolder, out string defaultFileName)
        {
            defaultFolder = "Saves/scene";
            defaultFileName = null;
            if (string.IsNullOrEmpty(galleryRelativePath)) return false;

            string p = galleryRelativePath.Replace('\\', '/');
            if (!p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            defaultFileName = Path.GetFileNameWithoutExtension(p);
            string dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir))
                defaultFolder = dir.Replace('\\', '/');
            return !string.IsNullOrEmpty(defaultFileName);
        }

        private static string DeriveSceneSaveBaseNameFromFileEntry(FileEntry f)
        {
            if (f == null) return null;

            string name = null;
            try { name = f.Name; } catch { name = null; }
            if (!string.IsNullOrEmpty(name))
            {
                name = Path.GetFileNameWithoutExtension(name.Trim());
                if (!string.IsNullOrEmpty(name))
                    return NormalizePresetSaveBaseName(name);
            }

            string path = null;
            try { path = f.Path; } catch { path = null; }
            if (!string.IsNullOrEmpty(path))
            {
                path = path.Replace('\\', '/');
                int sep = path.IndexOf(":/", StringComparison.Ordinal);
                if (sep >= 0)
                {
                    string internalPath = path.Substring(sep + 2);
                    name = Path.GetFileNameWithoutExtension(internalPath);
                }
                else
                {
                    name = Path.GetFileNameWithoutExtension(path);
                }
                if (!string.IsNullOrEmpty(name))
                    return NormalizePresetSaveBaseName(name);
            }

            try
            {
                if (!string.IsNullOrEmpty(f.Uid))
                    return NormalizePresetSaveBaseName(f.Uid);
            }
            catch { }

            return null;
        }

        private bool IsSceneGallerySelection(FileEntry f)
        {
            if (f == null) return false;

            if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false))
                return true;

            if (TryResolveSceneCategoryPackageRowToSceneJson(f) != null)
                return true;

            string pathLower = (f.Path ?? "").ToLowerInvariant();
            bool isSubScene = pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\")
                || (!string.IsNullOrEmpty(currentCategoryTitle) && currentCategoryTitle.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0);
            if (isSubScene) return false;

            if (pathLower.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene")))
                return true;

            if (!string.IsNullOrEmpty(currentCategoryTitle)
                && currentCategoryTitle.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) < 0
                && currentCategoryTitle.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (f is VarFileEntry || (f.Path ?? "").Replace('\\', '/').EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool TryGetSceneOverwriteSaveContext(out SceneOverwriteSaveContext ctx)
        {
            ctx = default;
            if (selectedFiles == null || selectedFiles.Count != 1) return false;

            FileEntry f = selectedFiles[0];
            if (!IsSceneGallerySelection(f)) return false;

            if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string rel, false)
                && TryParseSavesSceneRelativePath(rel, out string folder, out string fileName))
            {
                ctx = new SceneOverwriteSaveContext
                {
                    DefaultFolder = folder,
                    DefaultFileName = fileName
                };
                return true;
            }

            FileEntry resolved = TryResolveSceneCategoryPackageRowToSceneJson(f);
            if (resolved != null
                && LocalSceneGallerySupport.TryResolveSavesSceneJson(resolved, out _, out string resolvedRel, false)
                && TryParseSavesSceneRelativePath(resolvedRel, out string rFolder, out string rName))
            {
                ctx = new SceneOverwriteSaveContext
                {
                    DefaultFolder = rFolder,
                    DefaultFileName = rName
                };
                return true;
            }

            string baseName = DeriveSceneSaveBaseNameFromFileEntry(resolved ?? f);
            if (string.IsNullOrEmpty(baseName))
                baseName = "scene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            ctx = new SceneOverwriteSaveContext
            {
                DefaultFolder = "Saves/scene",
                DefaultFileName = baseName
            };
            return true;
        }

        private bool IsSceneSaveInProgress()
        {
            return _canvasesHiddenForSave != null || _sceneSaveFinalizeCoroutine != null;
        }

        private void TboxOverwriteSaveSceneClicked()
        {
            if (SuperController.singleton == null) return;
            if (IsSceneSaveInProgress())
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.save.scene_busy", "Scene save already in progress."), 2f);
                return;
            }

            if (!TryGetSceneOverwriteSaveContext(out SceneOverwriteSaveContext ctx))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.save.overwrite_select_one", "Select one scene in the gallery to overwrite or save as."),
                    2.5f);
                return;
            }

            SaveSceneWithDialog(ctx);
        }

        private void SaveSceneFromGallery()
        {
            SaveSceneWithDialog(null);
        }

        private void OverwriteSaveSceneFromGallery()
        {
            if (SuperController.singleton == null) return;
            if (IsSceneSaveInProgress())
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.save.scene_busy", "Scene save already in progress."), 2f);
                return;
            }

            if (!TryGetSceneOverwriteSaveContext(out SceneOverwriteSaveContext ctx))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.save.overwrite_select_one", "Select one scene in the gallery to overwrite or save as."),
                    2.5f);
                return;
            }

            SaveSceneWithDialog(ctx);
        }

        private void SaveSceneWithDialog(SceneOverwriteSaveContext? context)
        {
            if (SuperController.singleton == null) return;

            string defaultFolder = "Saves/scene";
            string defaultName = "scene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            if (context.HasValue && context.Value.HasPrefill)
            {
                SceneOverwriteSaveContext c = context.Value;
                if (!string.IsNullOrEmpty(c.DefaultFolder)) defaultFolder = c.DefaultFolder;
                if (!string.IsNullOrEmpty(c.DefaultFileName)) defaultName = c.DefaultFileName;
            }

            BeginSaveMode();

            if (SuperController.singleton.mainHUD != null && !SuperController.singleton.mainHUD.gameObject.activeSelf)
                SuperController.singleton.ShowMainHUDMonitor();
            SuperController.singleton.GetMediaPathDialog((selectedPath) =>
            {
                if (string.IsNullOrEmpty(selectedPath))
                {
                    EndSaveMode();
                    return;
                }
                string path = selectedPath;
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
                path = NormalizeSceneSavePath(path);
                bool exists = false;
                try { exists = MVR.FileManagementSecure.FileManagerSecure.FileExists(path); } catch { exists = false; }

                if (exists)
                {
                    VpbSaveCacheSupport.ConfirmOverwriteThenSave(
                        path,
                        "Resource " + path + " already exists. Overwrite?",
                        () => SaveSceneFinal(path, true),
                        () => { EndSaveMode(); });
                    return;
                }

                SaveSceneFinal(path, false);
            }, "json", defaultFolder, false, true, false, null, true);

            try
            {
                if (SuperController.singleton.mediaFileBrowserUI != null)
                {
                    SuperController.singleton.mediaFileBrowserUI.SetTextEntry(true);
                    if (SuperController.singleton.mediaFileBrowserUI.fileEntryField != null)
                    {
                        SuperController.singleton.mediaFileBrowserUI.fileEntryField.text = defaultName;
                        SuperController.singleton.mediaFileBrowserUI.ActivateFileNameField();
                    }
                }
            }
            catch { }
        }
    }
}
