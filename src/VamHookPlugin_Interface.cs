using System.IO;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
                bool isFirstOpen = Gallery.singleton.PanelCount == 0;
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
                    if (name.Equals("Person Hair", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Hair", StringComparison.OrdinalIgnoreCase)) name = "Hair";
                    if (name.Equals("Person Clothing", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Clothing", StringComparison.OrdinalIgnoreCase)) name = "Clothing";
                    if (name.Equals("Person Appearance", StringComparison.OrdinalIgnoreCase) || name.Equals("P.Appearance", StringComparison.OrdinalIgnoreCase)) name = "Appearance";
                    if (name.Equals("Person AppearancePresets", StringComparison.OrdinalIgnoreCase) || name.Equals("Person Appearance Presets", StringComparison.OrdinalIgnoreCase)) name = "Appearance";
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
                addCat("Plugins", "cs|cslist|dll", "Custom/Scripts");
                addCat("Clothing", "vam|vap", "Custom/Clothing");
                addCat("Clothing", "vap", "Custom/Atom/Person/Clothing");
                addCat("Clothing", "vam|vap", "Saves/Person/Clothing");
                addCat("Hair", "vam|vap", "Custom/Hair");
                addCat("Pose", "json", "Saves/Person"); // Was Person
                addCat("Appearance", "json|vap", "Saves/Person/appearance");
                // Clothing/Hair presets are included in the unified Clothing/Hair categories.
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
                                    try { VPBConfig.Instance.Save(); } catch { }
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
        private static readonly Regex s_PackageUidRegex = new Regex(
            @"([A-Za-z0-9][A-Za-z0-9_\-]*\.[A-Za-z0-9][A-Za-z0-9_\-]*\.[0-9]+):/",
            RegexOptions.Compiled);

        private void CollectSceneReferencedPackages(HashSet<string> protectedPackages)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) { LogUtil.LogWarning("[UnloadAll] SuperController is null, skipping scene scan"); return; }
            int atomCount = 0, storableCount = 0, matchCount = 0;
            try
            {
                // GetStorableIDs / GetStorableByID are standard VAM Atom API, look them up once
                System.Reflection.MethodInfo getIds = null;
                System.Reflection.MethodInfo getStorable = null;

                foreach (Atom atom in sc.GetAtoms())
                {
                    if (atom == null) continue;
                    atomCount++;
                    try
                    {
                        if (getIds == null)
                        {
                            getIds = atom.GetType().GetMethod("GetStorableIDs",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                            getStorable = atom.GetType().GetMethod("GetStorableByID",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                                null, new Type[] { typeof(string) }, null);
                            LogUtil.Log($"[UnloadAll] GetStorableIDs found={getIds != null}, GetStorableByID found={getStorable != null}");
                        }
                        if (getIds == null || getStorable == null) continue;

                        var ids = getIds.Invoke(atom, null) as System.Collections.Generic.List<string>;
                        if (ids == null) { LogUtil.LogWarning($"[UnloadAll] atom '{atom.uid}': GetStorableIDs returned null"); continue; }

                        foreach (string sid in ids)
                        {
                            if (string.IsNullOrEmpty(sid)) continue;
                            try
                            {
                                var storable = getStorable.Invoke(atom, new object[] { sid });
                                if (storable == null) continue;

                                // Try GetJSON() with no params, then (bool,bool), then (bool,bool,bool)
                                string jsonStr = null;
                                foreach (var sig in new[] {
                                    new Type[0],
                                    new Type[] { typeof(bool), typeof(bool) },
                                    new Type[] { typeof(bool), typeof(bool), typeof(bool) }
                                })
                                {
                                    var m2 = storable.GetType().GetMethod("GetJSON",
                                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                                        null, sig, null);
                                    if (m2 == null) continue;
                                    object[] args = sig.Length == 0 ? null : sig.Length == 2
                                        ? new object[] { true, true }
                                        : new object[] { true, true, true };
                                    var r = m2.Invoke(storable, args);
                                    if (r != null) { jsonStr = r.ToString(); break; }
                                }
                                if (jsonStr == null) continue;

                                storableCount++;
                                int before = protectedPackages.Count;
                                foreach (Match m in s_PackageUidRegex.Matches(jsonStr))
                                    ProtectPackage(m.Groups[1].Value, protectedPackages);
                                matchCount += (protectedPackages.Count - before);
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex) { LogUtil.LogWarning($"[UnloadAll] atom '{atom.uid}': {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[UnloadAll] CollectSceneReferencedPackages: " + ex.Message);
            }
            LogUtil.Log($"[UnloadAll] Scene scan: {atomCount} atoms, {storableCount} storables scanned, {matchCount} packages protected");
        }

        public void UnloadAllPackages()
        {
            // Build protected set: AutoInstall packages + their recursive dependencies
            var protectedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var uid in FileEntry.AutoInstallLookup)
                ProtectPackage(uid, protectedPackages);
            LogUtil.Log($"[UnloadAll] After AutoInstall: {protectedPackages.Count} protected");

            // Also protect packages referenced by the currently active scene (hair, clothing, etc.)
            CollectSceneReferencedPackages(protectedPackages);
            LogUtil.Log($"[UnloadAll] After scene scan: {protectedPackages.Count} protected total");

            // Uninstall all packages that are installed but not protected
            bool dirty = false;
            var packages = FileManager.PackagesByUid;
            if (packages == null) { LogUtil.LogWarning("[UnloadAll] PackagesByUid is null, aborting"); return; }

            int installedCount = 0, skippedCount = 0, movedCount = 0, deletedDupCount = 0, failedCount = 0;
            foreach (var kvp in packages)
            {
                VarPackage pkg = kvp.Value;
                if (!pkg.IsInstalled()) continue;
                installedCount++;
                if (protectedPackages.Contains(pkg.Uid)) { skippedCount++; continue; }

                LogUtil.Log($"[UnloadAll] Unloading: {pkg.Uid}");
                string addonPath = pkg.Path;
                bool moved = pkg.UninstallSelf();
                if (moved) { movedCount++; dirty = true; continue; }

                // UninstallSelf failed — check if AllPackages already has this file (duplicate)
                if (addonPath.StartsWith("AddonPackages/"))
                {
                    string allPkgPath = "AllPackages" + addonPath.Substring("AddonPackages".Length);
                    if (File.Exists(allPkgPath))
                    {
                        // AllPackages already has it — remove the AddonPackages duplicate
                        try
                        {
                            File.Delete(addonPath);
                            // Update pkg.Path to reflect the actual file location (AllPackages)
                            // so InstallSelf works correctly on next load instead of returning false
                            pkg.CloseZipFile();
                            pkg.Path = allPkgPath.Replace('\\', '/');
                            pkg.RelativePath = allPkgPath.Substring("AllPackages/".Length);
                            deletedDupCount++;
                            dirty = true;
                            LogUtil.Log($"[UnloadAll] Deleted AddonPackages duplicate: {pkg.Uid}");
                        }
                        catch (Exception ex) { failedCount++; LogUtil.LogWarning($"[UnloadAll] Delete duplicate failed {pkg.Uid}: {ex.Message}"); }
                        continue;
                    }
                }
                failedCount++;
                LogUtil.LogWarning($"[UnloadAll] UninstallSelf failed for: {pkg.Uid}");
            }
            LogUtil.Log($"[UnloadAll] installed={installedCount} skipped={skippedCount} moved={movedCount} deletedDup={deletedDupCount} failed={failedCount}");

            if (dirty)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh(true);
                RemoveEmptyFolder("AddonPackages");
            }
            LogUtil.Log($"[UnloadAll] Complete. dirty={dirty}");
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
            // ScanPackageManagerPackages(); // Removed
            // OpenPackageManagerGallery(); // Removed
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
