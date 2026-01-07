using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private enum GalleryPage
        {
            CategoryScene,
            CategoryClothing,
            CategoryHair,
            CategoryPose,
            CategoryCUA,
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
            // 1. Try to restore using category name (supports "Scenes", "Clothing" etc. stored by Gallery UI)
            if (Gallery.singleton != null)
            {
                if (!m_GalleryCatsInited) InitGalleryCategories();
                
                string lastPageName = (Settings.Instance != null && Settings.Instance.LastGalleryPage != null) ? Settings.Instance.LastGalleryPage.Value : "";
                if (!string.IsNullOrEmpty(lastPageName) && m_GalleryCategories != null)
                {
                    foreach (var cat in m_GalleryCategories)
                    {
                        if (string.Equals(cat.name, lastPageName, StringComparison.OrdinalIgnoreCase))
                        {
                            Gallery.singleton.Show(cat.name, cat.extension, cat.path);
                            return;
                        }
                    }
                }
            }

            // 2. Fallback to Enum-based restore (supports "CategoryScene" etc. stored by hotkeys/legacy)
            switch (GetLastGalleryPage())
            {
                case GalleryPage.CategoryScene: OpenCategoryScene(); break;
                case GalleryPage.CategoryClothing: OpenCategoryClothing(); break;
                case GalleryPage.CategoryHair: OpenCategoryHair(); break;
                case GalleryPage.CategoryPose: OpenCategoryPose(); break;
                case GalleryPage.CategoryCUA: OpenCategoryCUA(); break;
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

        private bool m_GalleryCatsInited = false;
        private List<Gallery.Category> m_GalleryCategories;

        private class CategoryInfo
        {
            public string ext;
            public List<string> paths;
        }

        private void InitGalleryCategories()
        {
            if (Gallery.singleton == null) return;
            
            if (m_GalleryCategories == null)
            {
                m_GalleryCategories = new List<Gallery.Category>();
                var catDict = new Dictionary<string, CategoryInfo>(StringComparer.OrdinalIgnoreCase);

                // Helper to add categories while tracking names
                Action<string, string, string> addCat = (name, ext, path) => {
                    // Consolidate names
                    if (name.Equals("Person Hair", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Hair", StringComparison.OrdinalIgnoreCase)) name = "Hair";
                    if (name.Equals("Person Clothing", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Clothing", StringComparison.OrdinalIgnoreCase)) name = "Clothing";
                    if (name.Equals("Person Pose", StringComparison.OrdinalIgnoreCase)) name = "Pose";
                    if (name.Equals("Person", StringComparison.OrdinalIgnoreCase)) name = "Pose"; // Merge Person into Pose as requested

                    if (!catDict.ContainsKey(name)) {
                        catDict[name] = new CategoryInfo { ext = ext, paths = new List<string>() };
                    }
                    
                    var entry = catDict[name];
                    if (!entry.paths.Contains(path)) {
                        entry.paths.Add(path);
                    }
                    
                    // Merge extensions
                    var currentExts = new HashSet<string>(entry.ext.Split('|'), StringComparer.OrdinalIgnoreCase);
                    var newExts = ext.Split('|');
                    bool changed = false;
                    foreach(var e in newExts) {
                        if (currentExts.Add(e)) changed = true;
                    }
                    if (changed) {
                        var extList = new List<string>(currentExts);
                        entry.ext = string.Join("|", extList.ToArray());
                    }
                    
                    // catDict[name] = entry; // Class is reference type, no need to reassign
                };

                // 1. Static/Legacy Categories
                addCat("Scenes", "json", "Saves/scene");
                addCat("SubScenes", "json", "Custom/SubScene");
                addCat("Clothing", "vam", "Custom/Clothing");
                addCat("Hair", "vam", "Custom/Hair");
                addCat("Pose", "json", "Saves/Person"); // Was Person
                addCat("Clothing", "vap", "Custom/Clothing"); // Was P.Clothing
                addCat("Hair", "vap", "Custom/Hair"); // Was P.Hair
                // addCat("CUA", "assetbundle|unity3d", "Custom/Assets");

                // 2. Dynamic Discovery from Custom/Atom
                string atomRoot = "Custom/Atom";
                if (Directory.Exists(atomRoot))
                {
                    try
                    {
                        foreach (string atomPath in Directory.GetDirectories(atomRoot))
                        {
                            string atomType = Path.GetFileName(atomPath);
                            
                            foreach (string resourcePath in Directory.GetDirectories(atomPath))
                            {
                                string resourceName = Path.GetFileName(resourcePath);
                                string finalName = resourceName;

                                // Handle name collisions (e.g. if "Clothing" exists in Atom/Person/Clothing, rename to "Person Clothing")
                                // But here we want to consolidate, so we might strip "Person" if present?
                                // Actually, standard logic was adding "atomType + resourceName".
                                // Now we just let addCat handle normalization.
                                if (atomType.Equals("Person", StringComparison.OrdinalIgnoreCase))
                                {
                                     // "Person" + "Hair" -> "Person Hair" -> "Hair"
                                     finalName = atomType + " " + resourceName;
                                }

                                // Determine extension
                                string ext = "vap";
                                if (string.Equals(resourceName, "Pose", StringComparison.OrdinalIgnoreCase))
                                    ext = "json|vap";
                                
                                // Use forward slashes for path to maintain consistency
                                string finalPath = resourcePath.Replace("\\", "/");
                                
                                addCat(finalName, ext, finalPath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("Error discovering categories: " + ex.Message);
                    }
                }

                addCat("All", "var", "");

                // Build list
                foreach(var kvp in catDict)
                {
                    m_GalleryCategories.Add(new Gallery.Category { 
                        name = kvp.Key, 
                        extension = kvp.Value.ext, 
                        path = kvp.Value.paths.Count > 0 ? kvp.Value.paths[0] : "", 
                        paths = kvp.Value.paths 
                    });
                }
            }
            
            Gallery.singleton.SetCategories(m_GalleryCategories);
            m_GalleryCatsInited = true;
        }

        private void ShowGallery(string title, string extension, string path)
        {
            if (Gallery.singleton != null)
            {
                if (!m_GalleryCatsInited) InitGalleryCategories();
                Gallery.singleton.Show(title, extension, path);
            }
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
            ShowGallery("Custom Scene", "json", "Saves/scene");
        }
        public void OpenCustomSavedPerson()
        {
            SetLastGalleryPage(GalleryPage.CustomSavedPerson);
            ShowGallery("Custom Saved Person", "json", "Saves/Person");
        }
        public void OpenPersonPreset()
        {
            SetLastGalleryPage(GalleryPage.CustomPersonPreset);
            ShowGallery("Custom Person Preset", "vap", "Custom/Atom/Person");
        }
        public void OpenCategoryScene()
        {
            SetLastGalleryPage(GalleryPage.CategoryScene);
            ShowGallery("Category Scene", "json", "Saves/scene");
        }
        public void OpenCategoryClothing()
        {
            SetLastGalleryPage(GalleryPage.CategoryClothing);
            ShowGallery("Category Clothing", "vam", "Custom/Clothing");
        }
        public void OpenCategoryHair()
        {
            SetLastGalleryPage(GalleryPage.CategoryHair);
            ShowGallery("Category Hair", "vam", "Custom/Hair");
        }
        public void OpenCategoryPose()
        {
            SetLastGalleryPage(GalleryPage.CategoryPose);
            ShowGallery("Category Pose", "json|vap", "Custom/Atom/Person/Pose");
        }
        public void OpenPresetPerson()
        {
            SetLastGalleryPage(GalleryPage.PresetPerson);
            ShowGallery("Preset Person", "vap", "Custom/Atom/Person");
        }
        public void OpenPresetClothing()
        {
            SetLastGalleryPage(GalleryPage.PresetClothing);
            ShowGallery("Preset Clothing", "vap", "Custom/Clothing");
        }
        public void OpenPresetHair()
        {
            SetLastGalleryPage(GalleryPage.PresetHair);
            ShowGallery("Preset Hair", "vap", "Custom/Hair");
        }
        public void OpenPresetOther()
        {
            SetLastGalleryPage(GalleryPage.PresetOther);
            ShowGallery("Preset Other", "vap", "Custom");
        }
        public void OpenCategoryCUA()
        {
            SetLastGalleryPage(GalleryPage.CategoryCUA);
            ShowGallery("CUA", "assetbundle|unity3d", "Custom/Assets");
        }
        public void OpenMiscCUA()
        {
            SetLastGalleryPage(GalleryPage.MiscAssetBundle);
            ShowGallery("AssetBundle", "assetbundle", "Custom");
        }
        public void OpenMiscAll()
        {
            SetLastGalleryPage(GalleryPage.MiscAll);
            ShowGallery("All", "var", "");
        }
    }
}
