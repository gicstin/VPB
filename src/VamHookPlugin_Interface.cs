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
            CategoryPlugins,
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

        private void TryFillLastGalleryPageFromPersisted(ref string lastPageName)
        {
            if (!string.IsNullOrEmpty(lastPageName)) return;
            string diskLast = "";
            try { diskLast = VPBConfig.ReadLastGalleryCategoryFromDisk(); } catch { }
            if (!string.IsNullOrEmpty(diskLast))
            {
                lastPageName = diskLast;
                LogUtil.Log("[Gallery] OpenGallery using disk LastGalleryCategory='" + lastPageName + "'");
                return;
            }
            if (VPBConfig.Instance != null && !string.IsNullOrEmpty(VPBConfig.Instance.LastGalleryCategory))
            {
                lastPageName = VPBConfig.Instance.LastGalleryCategory;
                LogUtil.Log("[Gallery] OpenGallery using memory LastGalleryCategory='" + lastPageName + "'");
                return;
            }
            if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
            {
                lastPageName = Settings.Instance.LastGalleryPage.Value;
                if (!string.IsNullOrEmpty(lastPageName))
                    LogUtil.Log("[Gallery] OpenGallery using Settings.LastGalleryPage='" + lastPageName + "'");
            }
        }

        public void OpenGallery()
        {
            // 1. Try to restore using category name (supports "Scenes", "Clothing" etc. stored by Gallery UI)
            if (Gallery.singleton != null)
            {
                if (!m_GalleryCatsInited) InitGalleryCategories();

                string lastPageName = "";

                // First open this session: use InitialGalleryCategory unless set to LastUsed (then restore saved tab).
                // Reopen within session: always restore last used category.
                bool isFirstOpen = !Gallery.singleton.AnyPanelHasLoadedContent;
                if (!isFirstOpen)
                    TryFillLastGalleryPageFromPersisted(ref lastPageName);
                else if (VPBConfig.Instance != null)
                {
                    string resolved = VPBConfig.Instance.ResolveInitialGalleryCategoryName();
                    if (resolved != null)
                        lastPageName = resolved;
                    else
                        TryFillLastGalleryPageFromPersisted(ref lastPageName);
                }

                if (string.IsNullOrEmpty(lastPageName))
                {
                    lastPageName = "Scenes";
                    LogUtil.Log("[Gallery] OpenGallery defaulting to Scenes" + (isFirstOpen ? " (startup)" : ""));
                }

                if (!string.IsNullOrEmpty(lastPageName) && m_GalleryCategories != null)
                {
                    string rawLastPageName = lastPageName;

                    // Normalize common variants written by legacy callers:
                    // "Category Hair" / "CategoryHair" -> "Hair"
                    // "Preset Hair" / "PresetHair" -> "Hair"
                    // "Scene" -> "Scenes"
                    lastPageName = lastPageName.Trim();
                    if (lastPageName.StartsWith("Category ", StringComparison.OrdinalIgnoreCase))
                        lastPageName = lastPageName.Substring("Category ".Length);
                    else if (lastPageName.StartsWith("Category", StringComparison.OrdinalIgnoreCase) && lastPageName.Length > "Category".Length)
                        lastPageName = lastPageName.Substring("Category".Length);

                    if (lastPageName.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                        lastPageName = lastPageName.Substring("Preset ".Length);
                    else if (lastPageName.StartsWith("Preset", StringComparison.OrdinalIgnoreCase) && lastPageName.Length > "Preset".Length)
                        lastPageName = lastPageName.Substring("Preset".Length);

                    lastPageName = lastPageName.Trim();

                    if (string.Equals(lastPageName, "Scene", StringComparison.OrdinalIgnoreCase))
                        lastPageName = "Scenes";

                    LogUtil.Log("[Gallery] OpenGallery restore raw='" + rawLastPageName + "' normalized='" + lastPageName + "'");

                    foreach (var cat in m_GalleryCategories)
                    {
                        if (string.Equals(cat.name, lastPageName, StringComparison.OrdinalIgnoreCase))
                        {
                            LogUtil.Log("[Gallery] OpenGallery matched category='" + cat.name + "' path='" + cat.path + "'");
                            Gallery.singleton.Show(cat.name, cat.extension, cat.path);
                            return;
                        }
                    }

                    LogUtil.LogWarning("[Gallery] OpenGallery no match for '" + lastPageName + "'");
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
                case GalleryPage.CategoryPlugins: OpenCategoryPlugins(); break;
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

        public void OpenCreateGallery()
        {
            if (Gallery.singleton != null)
            {
                Gallery.MarkCreateGalleryPaneRequested();
                if (!m_GalleryCatsInited) InitGalleryCategories();
                Gallery.singleton.CreatePane();
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
                    // Keep hair presets as separate category (used to be separate "subcategory" in side list)
                    if (name.Equals("Person Hair", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Hair", StringComparison.OrdinalIgnoreCase)) name = "Hair Presets";
                    if (name.Equals("Person Clothing", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Clothing", StringComparison.OrdinalIgnoreCase)) name = "Clothing";
                    if (name.Equals("Person Appearance", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Appearance", StringComparison.OrdinalIgnoreCase)) name = "Appearance";
                    if (name.Equals("Person AppearancePresets", StringComparison.OrdinalIgnoreCase) || name.Equals("Person Appearance Presets", StringComparison.OrdinalIgnoreCase)) name = "Appearance";
                    if (name.Equals("Person Pose", StringComparison.OrdinalIgnoreCase)) name = "Pose";
                    if (name.Equals("Person", StringComparison.OrdinalIgnoreCase)) name = "Pose"; // Merge Person into Pose as requested

                    // Consolidate physics categories
                    if (name.Equals("Person GlutePhysics", StringComparison.OrdinalIgnoreCase)) name = "Body Physics";
                    if (name.Equals("Person BreastPhysics", StringComparison.OrdinalIgnoreCase)) name = "Body Physics";

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
                addCat("Plugins", "cs|cslist|dll", "Custom/Scripts");
                addCat("Clothing", "vam|vap", "Custom/Clothing");
                addCat("Clothing", "vap", "Custom/Atom/Person/Clothing");
                addCat("Clothing", "vam|vap", "Saves/Person/Clothing");
                addCat("Hair", "vam|vap", "Custom/Hair");
                // Include hair presets saved under Person preset folders (Issue #101 hair parity).
                addCat("Hair", "vap", "Custom/Atom/Person/Hair");
                addCat("Hair", "vam|vap", "Saves/Person/Hair");
                addCat("Pose", "json", "Saves/Person"); // Was Person
                addCat("Appearance", "json|vap", "Saves/Person/appearance");
                // Clothing/Hair presets are included in the unified Clothing/Hair categories.
                addCat("CUA", "assetbundle|unity3d", "Custom/Assets");

                // 2. Dynamic Discovery from Custom/Atom
                string atomRoot = "Custom/Atom";
                if (Directory.Exists(atomRoot))
                {
                    try
                    {
                        // Cache directory enumeration to avoid walking the Custom/Atom tree repeatedly.
                        List<string> atomDirs = null;
                        try
                        {
                            string sig = "0";
                            try { sig = Directory.GetLastWriteTimeUtc(atomRoot).ToBinary().ToString(); } catch { sig = "0"; }
                            string cacheKey = "dirs:custom_atom|root=" + (Path.GetFullPath(atomRoot).Replace('\\', '/').TrimEnd('/'));
                            var cached = new List<VpbLocalDatabase.SystemFileRow>();
                            if (VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey, sig, cached) && cached.Count > 0)
                            {
                                atomDirs = new List<string>(cached.Count);
                                for (int i = 0; i < cached.Count; i++) atomDirs.Add(cached[i].Path);
                            }
                            else
                            {
                                atomDirs = new List<string>(Directory.GetDirectories(atomRoot));
                                var rows = new List<VpbLocalDatabase.SystemFileRow>(atomDirs.Count);
                                for (int i = 0; i < atomDirs.Count; i++)
                                {
                                    var r = new VpbLocalDatabase.SystemFileRow();
                                    r.Path = atomDirs[i];
                                    r.LastWriteBinaryOrInvalid = long.MinValue;
                                    r.SizeOrInvalid = long.MinValue;
                                    rows.Add(r);
                                }
                                if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey, sig, rows);
                            }
                        }
                        catch { atomDirs = null; }

                        var atomDirEnum = atomDirs != null ? atomDirs : new List<string>(Directory.GetDirectories(atomRoot));
                        foreach (string atomPath in atomDirEnum)
                        {
                            string atomType = Path.GetFileName(atomPath);
                            
                            List<string> resDirs = null;
                            try
                            {
                                string sig2 = "0";
                                try { sig2 = Directory.GetLastWriteTimeUtc(atomPath).ToBinary().ToString(); } catch { sig2 = "0"; }
                                string cacheKey2 = "dirs:custom_atom_child|root=" + (Path.GetFullPath(atomPath).Replace('\\', '/').TrimEnd('/'));
                                var cached2 = new List<VpbLocalDatabase.SystemFileRow>();
                                if (VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey2, sig2, cached2) && cached2.Count > 0)
                                {
                                    resDirs = new List<string>(cached2.Count);
                                    for (int i = 0; i < cached2.Count; i++) resDirs.Add(cached2[i].Path);
                                }
                                else
                                {
                                    resDirs = new List<string>(Directory.GetDirectories(atomPath));
                                    var rows2 = new List<VpbLocalDatabase.SystemFileRow>(resDirs.Count);
                                    for (int i = 0; i < resDirs.Count; i++)
                                    {
                                        var r = new VpbLocalDatabase.SystemFileRow();
                                        r.Path = resDirs[i];
                                        r.LastWriteBinaryOrInvalid = long.MinValue;
                                        r.SizeOrInvalid = long.MinValue;
                                        rows2.Add(r);
                                    }
                                    if (rows2.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey2, sig2, rows2);
                                }
                            }
                            catch { resDirs = null; }

                            var resEnum = resDirs != null ? resDirs : new List<string>(Directory.GetDirectories(atomPath));
                            foreach (string resourcePath in resEnum)
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
                // List all .var packages as rows (no internal scan). Uses PackageListEntry rows in gallery.
                addCat("ALL VAR", "varpkg", "");
                // Union of all VAR-internal paths (all types) + loose roots below.
                addCat(Gallery.EverythingCategoryName, Gallery.EverythingExtensionToken, "");

                if (catDict.TryGetValue(Gallery.EverythingCategoryName, out CategoryInfo everythingInfo))
                {
                    string[] evLoose = new[]
                    {
                        "Saves/scene", "Custom/SubScene", "Custom/Scripts", "Custom/Clothing", "Custom/Hair",
                        "Saves/Person", "Custom/Assets", "Custom/Atom",
                    };
                    for (int ei = 0; ei < evLoose.Length; ei++)
                    {
                        if (!everythingInfo.paths.Contains(evLoose[ei]))
                            everythingInfo.paths.Add(evLoose[ei]);
                    }
                }

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

                // Persist only on explicit navigation (hotkeys/menu). Do NOT persist in GalleryPanel.Show()
                // to avoid overwriting saved state during initial open/restore.
                try
                {
                    if (VPBConfig.Instance != null)
                    {
                        string name = title;
                        if (!string.IsNullOrEmpty(name) && name.StartsWith("Category ", StringComparison.OrdinalIgnoreCase))
                            name = name.Substring("Category ".Length);
                        if (string.Equals(name, "Scene", StringComparison.OrdinalIgnoreCase))
                            name = "Scenes";

                        if (!string.IsNullOrEmpty(name) && m_GalleryCategories != null)
                        {
                            for (int i = 0; i < m_GalleryCategories.Count; i++)
                            {
                                var c = m_GalleryCategories[i];
                                if (string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase))
                                {
                                    VPBConfig.Instance.LastGalleryCategory = c.name;
                                    try { VPBConfig.Instance.Save(false); } catch { } // disk only; Show() below updates UI (Save(true) would ConfigChanged/UpdateLayout)
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }

                Gallery.singleton.Show(title, extension, path);
            }
        }

        public void Refresh()
        {
            Refresh(null);
        }

        /// <summary>
        /// Refresh with an explicit reason tag (e.g. "autoload", "autoinstall", "manual").
        /// Reasons propagate to FileManager scan-stats logging so coalesced startup passes
        /// can be diagnosed without a stack trace.
        /// </summary>
        public void Refresh(string reason)
        {
            FileManager.Refresh(reason, true);
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
            // Cache listing to avoid repeated recursion costs during bulk uninstall cleanup.
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(startLocation); } catch { return; }
            foreach (var directory in subdirs)
            {
                RemoveEmptyFolder(directory);
                int fileN = 0, dirN = 0;
                try { fileN = Directory.GetFiles(directory).Length; } catch { fileN = 0; }
                try { dirN = Directory.GetDirectories(directory).Length; } catch { dirN = 0; }
                if (fileN == 0 && dirN == 0)
                {
                    Directory.Delete(directory, false);
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
            // ScanPackageManagerPackages(); // Removed
            // OpenPackageManagerGallery(); // Removed
        }
        public void OpenHubBrowse()
        {
            SuperController.singleton.ActivateWorldUI();
            if (m_HubBrowse == null)
            {
                if (MVR.Hub.HubBrowse.singleton == null)
                {
                    LogUtil.LogWarning("[VPB] HubBrowse is not available yet");
                    return;
                }
                CreateHubBrowse();
            }
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
            // Include .vap so Issue #101 subfilters can split base vs presets.
            ShowGallery("Category Clothing", "vam|vap", "Custom/Clothing");
        }
        public void OpenCategoryHair()
        {
            SetLastGalleryPage(GalleryPage.CategoryHair);
            // Include .vap so Issue #101 subfilters can split base vs presets.
            ShowGallery("Category Hair", "vam|vap", "Custom/Hair");
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
        public void OpenCategoryPlugins()
        {
            SetLastGalleryPage(GalleryPage.CategoryPlugins);
            ShowGallery("Plugins", "cs|cslist|dll", "Custom/Scripts");
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
