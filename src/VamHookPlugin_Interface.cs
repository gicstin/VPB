using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine;

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
        public void UninstallAll()
        {
            string[] addonVarPaths = Directory.GetFiles("AddonPackages", "*.var", SearchOption.AllDirectories);
            foreach (var item in addonVarPaths)
            {
                string name = Path.GetFileNameWithoutExtension(item);
                if (FileEntry.AutoInstallLookup.Contains(name)) continue;
                if (item.StartsWith("AddonPackages"))
                {
                    string targetPath = "AllPackages" + item.Substring("AddonPackages".Length);
                    string dir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(targetPath)) continue;
                    File.Move(item, targetPath);
                }
            }
            MVR.FileManagement.FileManager.Refresh();
            var_browser.FileManager.Refresh();
            RemoveEmptyFolder("AddonPackages");
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
