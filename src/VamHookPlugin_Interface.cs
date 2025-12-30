using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace var_browser
{
    public partial class VamHookPlugin
    {
        private enum GalleryPage
        {
            CategoryScene,
            CategoryClothing,
            CategoryHair,
            CategoryPose,
            CustomScene,
            CustomSavedPerson,
            CustomPersonPreset,
            PresetPerson,
            PresetClothing,
            PresetHair,
            PresetOther,
            MiscAssetBundle,
            MiscAll
        }

        private void SetLastGalleryPage(GalleryPage page)
        {
            if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
            {
                Settings.Instance.LastGalleryPage.Value = page.ToString();
            }
        }

        private GalleryPage GetLastGalleryPage()
        {
            try
            {
                if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                {
                    var v = Settings.Instance.LastGalleryPage.Value;
                    if (!string.IsNullOrEmpty(v))
                    {
                        return (GalleryPage)Enum.Parse(typeof(GalleryPage), v);
                    }
                }
            }
            catch { }
            return GalleryPage.CategoryHair;
        }

        public void OpenGallery()
        {
            switch (GetLastGalleryPage())
            {
                case GalleryPage.CategoryScene: OpenCategoryScene(); break;
                case GalleryPage.CategoryClothing: OpenCategoryClothing(); break;
                case GalleryPage.CategoryHair: OpenCategoryHair(); break;
                case GalleryPage.CategoryPose: OpenCategoryPose(); break;
                case GalleryPage.CustomScene: OpenCustomScene(); break;
                case GalleryPage.CustomSavedPerson: OpenCustomSavedPerson(); break;
                case GalleryPage.CustomPersonPreset: OpenPersonPreset(); break;
                case GalleryPage.PresetPerson: OpenPresetPerson(); break;
                case GalleryPage.PresetClothing: OpenPresetClothing(); break;
                case GalleryPage.PresetHair: OpenPresetHair(); break;
                case GalleryPage.PresetOther: OpenPresetOther(); break;
                case GalleryPage.MiscAssetBundle: OpenMiscCUA(); break;
                case GalleryPage.MiscAll: OpenMiscAll(); break;
                default: OpenCategoryHair(); break;
            }
        }

		// liu modification: show/hide
		public void LgShow()
		{
			VamHookPlugin.m_Show = !VamHookPlugin.m_Show;
		}
        void OpenFileBrowser(string msg)
        {
            LogUtil.Log("receive OpenFileBrowser "+ msg);
        }

        public void Refresh()
        {
            FileManager.Refresh(true);
            MVR.FileManagement.FileManager.Refresh();
            RemoveEmptyFolder("AllPackages");
        }
        public void RemoveInvalidVars()
        {
            FileManager.Refresh(true, true);
            MVR.FileManagement.FileManager.Refresh();
        }
        public void RemoveOldVersion()
        {
            FileManager.Refresh(true, true, true);
            MVR.FileManagement.FileManager.Refresh();
        }
        //https://stackoverflow.com/questions/2811509/c-sharp-remove-all-empty-subdirectories
        private static void RemoveEmptyFolder(string startLocation)
        {
            foreach (var directory in Directory.GetDirectories(startLocation))
            {
                RemoveEmptyFolder(directory);
                if (Directory.GetFiles(directory).Length == 0 &&
                    Directory.GetDirectories(directory).Length == 0)
                {
                    Directory.Delete(directory, false);
                }
            }
        }
        private void ProtectPackage(string packageUid, HashSet<string> protectedPackages)
        {
            if (string.IsNullOrEmpty(packageUid)) return;
            if (protectedPackages.Contains(packageUid)) return;

            protectedPackages.Add(packageUid);

            VarPackage currentPackage = FileManager.GetPackage(packageUid);
            if (currentPackage != null && currentPackage.RecursivePackageDependencies != null)
            {
                foreach (var depUid in currentPackage.RecursivePackageDependencies)
                {
                    VarPackage depPackage = FileManager.ResolveDependency(depUid);
                    if (depPackage != null)
                    {
                        protectedPackages.Add(depPackage.Uid);
                    }
                    protectedPackages.Add(depUid);
                }
            }
        }

        private string GetPackageFromPath(string path)
        {
             if (string.IsNullOrEmpty(path)) return null;
             int idx = path.IndexOf(':');
             if (idx > 0) return path.Substring(0, idx);
             return null;
        }

        public void UninstallAll()
        {
            HashSet<string> protectedPackages = new HashSet<string>();
            if (FileEntry.AutoInstallLookup != null)
            {
                foreach (var item in FileEntry.AutoInstallLookup)
                {
                    ProtectPackage(item, protectedPackages);
                    VarPackage p = FileManager.ResolveDependency(item);
                    if (p != null) ProtectPackage(p.Uid, protectedPackages);
                }
            }

            // Protect currently loaded scene and its dependencies
            string currentPackageUid = CurrentScenePackageUid;
            if (string.IsNullOrEmpty(currentPackageUid))
            {
                currentPackageUid = FileManager.CurrentPackageUid;
            }
            ProtectPackage(currentPackageUid, protectedPackages);

            // Protect active plugins
            try
            {
                var plugins = UnityEngine.Object.FindObjectsOfType<MVRScript>();
                foreach (var p in plugins)
                {
                     var fields = p.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                     foreach(var f in fields)
                     {
                         if (f.FieldType == typeof(JSONStorableUrl))
                         {
                             var jUrl = f.GetValue(p) as JSONStorableUrl;
                             if (jUrl != null && !string.IsNullOrEmpty(jUrl.val))
                             {
                                 string pkg = GetPackageFromPath(jUrl.val);
                                 if (pkg != null) ProtectPackage(pkg, protectedPackages);
                             }
                         }
                     }
                }
            }
            catch (Exception ex)
            {
                 LogUtil.LogError("Error scanning plugins: " + ex.Message);
            }

            string[] addonVarPaths = Directory.GetFiles("AddonPackages", "*.var", SearchOption.AllDirectories);
            m_UnloadList.Clear();
            foreach (var item in addonVarPaths)
            {
                string name = Path.GetFileNameWithoutExtension(item);
                
                bool isProtected = protectedPackages.Contains(name);

                if (item.StartsWith("AddonPackages"))
                {
                    m_UnloadList.Add(new UnloadItem {
                        Uid = name,
                        Path = item,
                        Type = DeterminePackageType(name),
                        Checked = !isProtected,
                        IsActive = isProtected
                    });
                }
            }
            m_ShowUnloadWindow = true;
        }
        public void OpenHubBrowse()
        {
            SuperController.singleton.ActivateWorldUI();
            m_HubBrowse.Show();
        }
        public void OpenCustomScene()
        {
            SetLastGalleryPage(GalleryPage.CustomScene);
            // Custom content does not require installation
            m_FileBrowser.onlyInstalled = false;
            ShowFileBrowser("Custom Scene", "json", "Saves/scene", true);
        }
        public void OpenCustomSavedPerson()
        {
            SetLastGalleryPage(GalleryPage.CustomSavedPerson);
            // Custom content does not require installation
            m_FileBrowser.onlyInstalled = false;
            ShowFileBrowser("Custom Saved Person", "json", "Saves/Person", true);
        }
        public void OpenPersonPreset()
        {
            SetLastGalleryPage(GalleryPage.CustomPersonPreset);
            // Custom content does not require installation
            m_FileBrowser.onlyInstalled = false;
            ShowFileBrowser("Custom Person Preset", "vap", "Custom/Atom/Person", true, false);
        }
        public void OpenCategoryScene()
        {
            SetLastGalleryPage(GalleryPage.CategoryScene);
            ShowFileBrowser("Category Scene", "json", "Saves/scene");
        }
        public void OpenCategoryClothing()
        {
            SetLastGalleryPage(GalleryPage.CategoryClothing);
            ShowFileBrowser("Category Clothing", "vam", "Custom/Clothing", false, false);
        }
        public void OpenCategoryHair()
        {
            SetLastGalleryPage(GalleryPage.CategoryHair);
            ShowFileBrowser("Category Hair", "vam", "Custom/Hair", false, false);
        }
        public void OpenCategoryPose()
        {
            SetLastGalleryPage(GalleryPage.CategoryPose);
            ShowFileBrowser("Category Pose", "json|vap", "Custom/Atom/Person/Pose", false, false);
        }
        public void OpenPresetPerson()
        {
            SetLastGalleryPage(GalleryPage.PresetPerson);
            ShowFileBrowser("Preset Person", "vap", "Custom/Atom/Person", false, false);
        }
        public void OpenPresetClothing()
        {
            SetLastGalleryPage(GalleryPage.PresetClothing);
            ShowFileBrowser("Preset Clothing", "vap", "Custom/Clothing", false, false);
        }
        public void OpenPresetHair()
        {
            SetLastGalleryPage(GalleryPage.PresetHair);
            ShowFileBrowser("Preset Hair", "vap", "Custom/Hair", false, false);
        }
        public void OpenPresetOther()
        {
            SetLastGalleryPage(GalleryPage.PresetOther);
            ShowFileBrowser("Preset Other", "vap", "Custom", false, false);
        }
        public void OpenMiscCUA()
        {
            SetLastGalleryPage(GalleryPage.MiscAssetBundle);
            ShowFileBrowser("AssetBundle", "assetbundle", "Custom", false, false);
        }
        public void OpenMiscAll()
        {
            SetLastGalleryPage(GalleryPage.MiscAll);
            ShowFileBrowser("All", "var", "", false, false);
        }
    }
}
