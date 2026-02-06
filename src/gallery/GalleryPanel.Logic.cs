using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        public string CurrentCategoryTitle => currentCategoryTitle;
        public GalleryLayoutMode LayoutMode => layoutMode;

        public static float BenchmarkStartTime = 0f;

        public void SetLayoutMode(GalleryLayoutMode mode)
        {
            if (layoutMode == mode && IsPackageManagerUIVisible() == (mode == GalleryLayoutMode.PackageManager)) return;
            
            if (mode == GalleryLayoutMode.PackageManager)
            {
                 BenchmarkStartTime = Time.realtimeSinceStartup;
                 UnityEngine.Debug.Log("[Benchmark] Starting Switch to PM Mode at " + BenchmarkStartTime);
            }

            layoutMode = mode;
            
            if (layoutMode == GalleryLayoutMode.PackageManager)
            {
                 ShowPackageManagerUI();
            }
            else
            {
                 HidePackageManagerUI();
                 if (scrollRect != null) scrollRect.gameObject.SetActive(true);
            }

            // Purge buttons as templates changed
            foreach (var go in fileButtonPool) if (go != null) Destroy(go);
            fileButtonPool.Clear();
            foreach (var go in activeButtons) if (go != null) Destroy(go);
            activeButtons.Clear();

            UpdateFooterLayoutState();
            UpdateLayout();
        }

        public Atom SelectedTargetAtom
        {
            get
            {
                if (personAtoms == null || targetDropdownValue < 0 || targetDropdownValue >= personAtoms.Count)
                    return null;
                return personAtoms[targetDropdownValue];
            }
        }

        private void CacheCategoryCounts()
        {
            if (categories == null) return;
            categoryCounts.Clear();
            
            // Build optimized lookup map for categories by extension
            // Map: Extension (lowercase, no dot) -> List of Categories
            Dictionary<string, List<Gallery.Category>> extToCats = new Dictionary<string, List<Gallery.Category>>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var c in categories) 
            {
                categoryCounts[c.name] = 0;
                if (string.IsNullOrEmpty(c.extension)) continue;
                string[] exts = c.extension.Split('|');
                foreach(string ext in exts)
                {
                    if (string.IsNullOrEmpty(ext)) continue;
                    string e = ext.Trim();
                    if (!extToCats.ContainsKey(e)) extToCats[e] = new List<Gallery.Category>();
                    extToCats[e].Add(c);
                }
            }

            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    // Filter by creator if set
                    if (!string.IsNullOrEmpty(currentCreator))
                    {
                        if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                    }

                    if (pkg.FileEntries == null) continue;
                    
                    int count = pkg.FileEntries.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var entry = pkg.FileEntries[i];
                        string internalPath = entry.InternalPath;
                        
                        // Fast extension extraction
                        int lastDot = internalPath.LastIndexOf('.');
                        if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                        
                        string ext = internalPath.Substring(lastDot + 1);
                        
                        List<Gallery.Category> candidates;
                        if (extToCats.TryGetValue(ext, out candidates))
                        {
                            int candCount = candidates.Count;
                            for (int j = 0; j < candCount; j++)
                            {
                                var cat = candidates[j];
                                // Check path match
                                bool pathMatch = false;
                                if (cat.paths != null && cat.paths.Count > 0)
                                {
                                    int pCount = cat.paths.Count;
                                    for(int k=0; k<pCount; k++)
                                    {
                                        if (internalPath.StartsWith(cat.paths[k], StringComparison.OrdinalIgnoreCase))
                                        {
                                            pathMatch = true;
                                            break;
                                        }
                                    }
                                }
                                else if (!string.IsNullOrEmpty(cat.path))
                                {
                                    if (internalPath.StartsWith(cat.path, StringComparison.OrdinalIgnoreCase))
                                        pathMatch = true;
                                }
                                else
                                {
                                    // No path specified means match all (unlikely for category but possible)
                                    pathMatch = true;
                                }

                                if (pathMatch)
                                {
                                    categoryCounts[cat.name]++;
                                    break; // File belongs to one category
                                }
                            }
                        }
                    }
                }
            }
            categoriesCached = true;
        }

        private void CacheCreators()
        {
            if (FileManager.PackagesByUid == null) return;
            
            Dictionary<string, int> counts = new Dictionary<string, int>();
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in extensions) if (!string.IsNullOrEmpty(e)) targetExts.Add(e.Trim());

            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (string.IsNullOrEmpty(pkg.Creator)) continue;
                if (pkg.FileEntries == null) continue;

                int count = pkg.FileEntries.Count;
                for (int i = 0; i < count; i++)
                {
                    var entry = pkg.FileEntries[i];
                    string internalPath = entry.InternalPath;

                    // 1. Check extension
                    int lastDot = internalPath.LastIndexOf('.');
                    if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                    string ext = internalPath.Substring(lastDot + 1);
                    if (!targetExts.Contains(ext)) continue;

                    // 2. Check path match
                    bool match = false;
                    if (currentPaths != null && currentPaths.Count > 0)
                    {
                         for(int k=0; k<currentPaths.Count; k++)
                         {
                             if (internalPath.StartsWith(currentPaths[k], StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                         }
                    }
                    else if (!string.IsNullOrEmpty(currentPath))
                    {
                         if (internalPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase)) match = true;
                    }
                    else
                    {
                        match = true;
                    }

                    if (match)
                    {
                        if (!counts.ContainsKey(pkg.Creator)) counts[pkg.Creator] = 0;
                        counts[pkg.Creator]++;
                    }
                }
            }
            
            cachedCreators = counts.Select(kv => new CreatorCacheEntry { Name = kv.Key, Count = kv.Value })
                                   .OrderBy(c => c.Name).ToList();
            creatorsCached = true;
        }

        public void InvalidateTags()
        {
            tagsCached = false;
        }

        private void CacheTagCounts()
        {
            tagCounts.Clear();
            if (FileManager.PackagesByUid == null) return;

            appearanceSourceCountAll = 0;
            appearanceSourceCountPresets = 0;
            appearanceSourceCountCustom = 0;

            clothingSubfilterCountAll = 0;
            clothingSubfilterCountReal = 0;
            clothingSubfilterCountPresets = 0;
            clothingSubfilterCountCustom = 0;
            clothingSubfilterCountItems = 0;
            clothingSubfilterCountMale = 0;
            clothingSubfilterCountFemale = 0;
            clothingSubfilterCountDecals = 0;

            appearanceSubfilterCountAll = 0;
            appearanceSubfilterCountPresets = 0;
            appearanceSubfilterCountCustom = 0;
            appearanceSubfilterCountMale = 0;
            appearanceSubfilterCountFemale = 0;
            appearanceSubfilterCountFuta = 0;

            clothingSubfilterFacetCountReal = 0;
            clothingSubfilterFacetCountPresets = 0;
            clothingSubfilterFacetCountCustom = 0;
            clothingSubfilterFacetCountItems = 0;
            clothingSubfilterFacetCountMale = 0;
            clothingSubfilterFacetCountFemale = 0;
            clothingSubfilterFacetCountDecals = 0;

            appearanceSubfilterFacetCountPresets = 0;
            appearanceSubfilterFacetCountCustom = 0;
            appearanceSubfilterFacetCountMale = 0;
            appearanceSubfilterFacetCountFemale = 0;
            appearanceSubfilterFacetCountFuta = 0;

            appearanceSubfilterCurrentCountAll = 0;
            appearanceSubfilterCurrentCountMale = 0;
            appearanceSubfilterCurrentCountFemale = 0;
            appearanceSubfilterCurrentCountFuta = 0;

            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            // Build extension set for fast lookup
            HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in extensions) if (!string.IsNullOrEmpty(e)) targetExts.Add(e.Trim());

            // Collect all relevant tags to count
            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string title = titleText != null ? titleText.text : "";
            bool isClothingTitle = (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0);
            bool isAppearanceTitle = (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0);
            if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagsToCount.UnionWith(TagFilter.AllClothingTags);
                tagsToCount.UnionWith(TagFilter.ClothingUnknownTags);
            }
            else if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagsToCount.UnionWith(TagFilter.AllHairTags);
                tagsToCount.UnionWith(TagFilter.HairUnknownTags);
            }
            
            // Include user-defined tags
            tagsToCount.UnionWith(TagsManager.Instance.GetAllUserTags());

            bool hasAnyTagsToCount = (tagsToCount.Count > 0);

            // Split tags into single-word and multi-word
            HashSet<string> singleWordTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> multiWordTags = new List<string>();
            char[] separators = new char[] { '/', '\\', '.', '_', '-', ' ' };

            if (hasAnyTagsToCount)
            {
                foreach (var t in tagsToCount)
                {
                    if (t.IndexOfAny(new char[] { ' ', '_', '-' }) >= 0) multiWordTags.Add(t);
                    else singleWordTags.Add(t);
                }
            }

            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (pkg.FileEntries == null) continue;
                
                // If filtering by creator, respect it
                if (!string.IsNullOrEmpty(currentCreator))
                {
                    if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                }

                int count = pkg.FileEntries.Count;
                for (int i = 0; i < count; i++)
                {
                    var entry = pkg.FileEntries[i];
                    string internalPath = entry.InternalPath;

                    // 1. Check extension
                    int lastDot = internalPath.LastIndexOf('.');
                    if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                    string ext = internalPath.Substring(lastDot + 1);
                    if (!targetExts.Contains(ext)) continue;

                    // 2. Check path match (Inline IsMatch logic)
                    bool match = false;
                    if (currentPaths != null && currentPaths.Count > 0)
                    {
                        for(int k=0; k<currentPaths.Count; k++)
                        {
                            if (internalPath.StartsWith(currentPaths[k], StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                        }
                    }
                    else if (!string.IsNullOrEmpty(currentPath))
                    {
                         if (internalPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase)) match = true;
                    }
                    else
                    {
                        match = true;
                    }

                    if (!match) continue;

                    if (isClothingTitle)
                    {
						ClothingLoadingUtils.ResourceKind ck = ClothingLoadingUtils.ResourceKind.Unknown;
						ClothingLoadingUtils.ResourceGender cg = ClothingLoadingUtils.ResourceGender.Unknown;
						bool isClothingEntry = false;
						bool isPresetEntry = false;
						bool isCustomPreset = false;

						ClothingLoadingUtils.ClassifyClothingHairPath(internalPath, out ck, out cg);
						isClothingEntry = (ck == ClothingLoadingUtils.ResourceKind.Clothing);
						if (isClothingEntry)
                        {
                            // For Clothing category we include both .vam and .vap, and subfilters split them.
							isPresetEntry = (ext.Equals("vap", StringComparison.OrdinalIgnoreCase));
							// VAR entries are never considered "Custom".
							isCustomPreset = false;

                            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(internalPath);

                            ClothingSubfilter cur = clothingSubfilter;
                            bool PassesClothingSubfilters(ClothingSubfilter f)
                            {
                                if (f == 0) return true;

                                bool wantsRealType = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                bool wantsDecalType = ((f & ClothingSubfilter.Decals) != 0);

                                bool typeExplicit = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                if (typeExplicit)
                                {
                                    bool okType = (!isDecal && (f & ClothingSubfilter.RealClothing) != 0) ||
                                                  (isDecal && (f & ClothingSubfilter.Decals) != 0);
                                    if (!okType) return false;
                                }
                                else
                                {
                                    if (wantsRealType && isDecal && !wantsDecalType) return false;
                                }

                                bool wantsPresets = (f & ClothingSubfilter.Presets) != 0;
								bool wantsCustom = (f & ClothingSubfilter.Custom) != 0;
								if (wantsPresets) { if (!isPresetEntry) return false; }
								if (wantsCustom) { if (!isCustomPreset) return false; }
								if ((f & ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
								if ((f & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return false; }
								if ((f & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return false; }

                                return true;
                            }

                            // Facet counts: how many would be shown if the user toggled that flag now.
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.RealClothing)) clothingSubfilterFacetCountReal++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Presets)) clothingSubfilterFacetCountPresets++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Custom)) clothingSubfilterFacetCountCustom++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Items)) clothingSubfilterFacetCountItems++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Male)) clothingSubfilterFacetCountMale++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Female)) clothingSubfilterFacetCountFemale++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Decals)) clothingSubfilterFacetCountDecals++;

							// All Clothing includes everything: real clothing + decals
							clothingSubfilterCountAll++;

                            // Decals are counted separately and excluded from real clothing filters by default.
                            if (isDecal)
                            {
                                clothingSubfilterCountDecals++;

                                // Apply active subfilters (if any) to tag counting.
                                if (clothingSubfilter != 0)
                                {
                                    bool wantsRealType = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                    bool wantsDecalType = ((clothingSubfilter & ClothingSubfilter.Decals) != 0);

                                    bool typeExplicit = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        if ((clothingSubfilter & ClothingSubfilter.Decals) == 0) continue;
                                    }
                                    else
                                    {
                                        if (wantsRealType && !wantsDecalType) continue;
                                    }

                                    // If user also selected real-only constraints, decals won't match.
                                    if ((clothingSubfilter & (ClothingSubfilter.Presets | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0) continue;
                                }
                            }
                            else
                            {
                                clothingSubfilterCountReal++;
                                if (isPresetEntry) clothingSubfilterCountPresets++;
								if (isCustomPreset) clothingSubfilterCountCustom++;
								if (!isPresetEntry) clothingSubfilterCountItems++;
                                if (cg == ClothingLoadingUtils.ResourceGender.Male) clothingSubfilterCountMale++;
                                else if (cg == ClothingLoadingUtils.ResourceGender.Female) clothingSubfilterCountFemale++;

                                // Apply active subfilters (if any) to tag counting.
                                if (clothingSubfilter != 0)
                                {
                                    bool typeExplicit = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        if ((clothingSubfilter & ClothingSubfilter.RealClothing) == 0) continue;
                                    }
                                    // Additional constraints
                                    if ((clothingSubfilter & ClothingSubfilter.Presets) != 0) { if (!isPresetEntry) continue; }
								if ((clothingSubfilter & ClothingSubfilter.Custom) != 0) { if (!isCustomPreset) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Items) != 0) { if (isPresetEntry) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) continue; }
                                }
                            }
                        }
                        else
                        {
                            // When browsing Clothing, ignore non-clothing entries for tag counts.
                            continue;
                        }
                    }

                    if (isAppearanceTitle)
                    {
                        string p = internalPath.Replace('\\', '/');
                        bool isAppearance = p.IndexOf("/appearance", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isAppearance)
                        {
                            // When browsing Appearance, ignore non-appearance entries for tag counts.
                            continue;
                        }

                        bool isCustomAppearance = p.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                        bool isPresetAppearance = p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);

                        AppearanceGender g = AppearanceGender.Unknown;
                        try { g = GetAppearanceGender(entry); } catch { g = AppearanceGender.Unknown; }

                        appearanceSubfilterCountAll++;
                        if (isPresetAppearance) appearanceSubfilterCountPresets++;
                        if (isCustomAppearance) appearanceSubfilterCountCustom++;
                        if (g == AppearanceGender.Male) appearanceSubfilterCountMale++;
                        if (g == AppearanceGender.Female) appearanceSubfilterCountFemale++;
                        if (g == AppearanceGender.Futa) appearanceSubfilterCountFuta++;

                        AppearanceSubfilter cur = appearanceSubfilter;
                        bool PassesAppearanceSubfilters(AppearanceSubfilter f)
                        {
                            if (f == 0) return true;
                            bool wantsPresets = (f & AppearanceSubfilter.Presets) != 0;
                            bool wantsCustom = (f & AppearanceSubfilter.Custom) != 0;
                            bool wantsMale = (f & AppearanceSubfilter.Male) != 0;
                            bool wantsFemale = (f & AppearanceSubfilter.Female) != 0;
                            bool wantsFuta = (f & AppearanceSubfilter.Futa) != 0;

                            // If both are selected, it's effectively no type restriction.
                            bool typeOk = true;
                            if (wantsPresets || wantsCustom)
                            {
                                if (wantsPresets && wantsCustom) typeOk = true;
                                else if (wantsPresets) typeOk = isPresetAppearance;
                                else if (wantsCustom) typeOk = isCustomAppearance;
                            }
                            if (!typeOk) return false;

                            bool wantsAnyGender = wantsMale || wantsFemale || wantsFuta;
                            if (wantsAnyGender)
                            {
                                bool genderOk = false;
                                if (wantsMale && g == AppearanceGender.Male) genderOk = true;
                                if (wantsFemale && g == AppearanceGender.Female) genderOk = true;
                                if (wantsFuta && g == AppearanceGender.Futa) genderOk = true;
                                if (!genderOk) return false;
                            }

                            return true;
                        }

                        // Facet counts: how many would be shown if the user toggled that flag now.
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Presets)) appearanceSubfilterFacetCountPresets++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Custom)) appearanceSubfilterFacetCountCustom++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Male)) appearanceSubfilterFacetCountMale++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Female)) appearanceSubfilterFacetCountFemale++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Futa)) appearanceSubfilterFacetCountFuta++;

                        // Current counts: how many are shown under the current active subfilter set.
                        if (PassesAppearanceSubfilters(appearanceSubfilter))
                        {
                            appearanceSubfilterCurrentCountAll++;
                            if (g == AppearanceGender.Male) appearanceSubfilterCurrentCountMale++;
                            if (g == AppearanceGender.Female) appearanceSubfilterCurrentCountFemale++;
                            if (g == AppearanceGender.Futa) appearanceSubfilterCurrentCountFuta++;
                        }

                        // Apply active subfilters (if any) to tag counting.
                        if (appearanceSubfilter != 0)
                        {
                            if (!PassesAppearanceSubfilters(appearanceSubfilter)) continue;
                        }
                    }

                    // Appearance split-pane counts (All/Presets/Custom)
                    if (isAppearanceTitle)
                    {
                        int lastDotAppearance = internalPath.LastIndexOf('.');
                        string extAppearance = (lastDotAppearance >= 0 && lastDotAppearance < internalPath.Length - 1) ? internalPath.Substring(lastDotAppearance + 1) : "";
                        if (string.Equals(extAppearance, "vap", StringComparison.OrdinalIgnoreCase))
                        {
                            if (internalPath.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                            {
                                // Presets = appearance .vap inside .var packages
                                appearanceSourceCountPresets++;
                                appearanceSourceCountAll++;
                            }
                        }
                    }

                    string pathLower = internalPath.ToLowerInvariant();
                    
                    if (hasAnyTagsToCount)
                    {
                        // 3. Count tags
                        // Optimization: Tokenize path for single-word tags
                        string[] tokens = pathLower.Split(separators);
                        
                        HashSet<string> foundTags = new HashSet<string>();

                        // Check tokens against single word tags
                        for (int k = 0; k < tokens.Length; k++)
                        {
                            if (singleWordTags.Contains(tokens[k]))
                            {
                                foundTags.Add(tokens[k]);
                            }
                        }

                        // Check multi-word tags using Contains
                        for (int k = 0; k < multiWordTags.Count; k++)
                        {
                            if (pathLower.Contains(multiWordTags[k]))
                            {
                                foundTags.Add(multiWordTags[k]);
                            }
                        }

                        // Check user-defined tags specifically for this entry
                        var uTags = TagsManager.Instance.GetTags(entry.Uid);
                        foreach (var ut in uTags)
                        {
                            // Ensure we only count it if it's in our tagsToCount (which it should be now)
                            if (tagsToCount.Contains(ut)) foundTags.Add(ut);
                        }

                        // Increment counts
                        foreach (var tag in foundTags)
                        {
                            if (!tagCounts.ContainsKey(tag)) tagCounts[tag] = 0;
                            tagCounts[tag]++;
                        }
                    }
                }
            }

            // Count Clothing (local filesystem) entries for subfilter facet counts.
            // This is intentionally separate from the package loop above.
            if (isClothingTitle)
            {
                if (string.IsNullOrEmpty(currentCreator))
                {
                    List<string> pathsToSearch = new List<string>();
                    if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
                    else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

                    for (int pi = 0; pi < pathsToSearch.Count; pi++)
                    {
                        string searchPath = pathsToSearch[pi];
                        if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                        for (int ei = 0; ei < extensions.Length; ei++)
                        {
                            string ext = extensions[ei];
                            if (string.IsNullOrEmpty(ext)) continue;

                            List<string> sysFileList = new List<string>();
                            try
                            {
                                FileManager.SafeGetFiles(searchPath, "*." + ext, sysFileList);
                            }
                            catch
                            {
                                continue;
                            }

                            for (int fi = 0; fi < sysFileList.Count; fi++)
                            {
                                string sysPath = sysFileList[fi] ?? "";
                                string norm = sysPath.Replace('\\', '/');
                                bool isPresetEntry = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);
                                bool isCustomPreset =
                                    (norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                     norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                     norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     norm.IndexOf("/Saves/", StringComparison.OrdinalIgnoreCase) >= 0);

                                ClothingLoadingUtils.ResourceKind ck = ClothingLoadingUtils.ResourceKind.Unknown;
                                ClothingLoadingUtils.ResourceGender cg = ClothingLoadingUtils.ResourceGender.Unknown;
                                ClothingLoadingUtils.ClassifyClothingHairPath(sysPath, out ck, out cg);
                                if (ck != ClothingLoadingUtils.ResourceKind.Clothing) continue;

                                bool isDecal = ClothingLoadingUtils.IsDecalLikePath(sysPath);

                                ClothingSubfilter cur = clothingSubfilter;
                                bool PassesClothingSubfilters(ClothingSubfilter f)
                                {
                                    if (f == 0) return true;

                                    bool wantsRealType = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                    bool wantsDecalType = ((f & ClothingSubfilter.Decals) != 0);

                                    bool typeExplicit = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        bool okType = (!isDecal && (f & ClothingSubfilter.RealClothing) != 0) ||
                                                      (isDecal && (f & ClothingSubfilter.Decals) != 0);
                                        if (!okType) return false;
                                    }
                                    else
                                    {
                                        if (wantsRealType && isDecal && !wantsDecalType) return false;
                                    }

                                    bool wantsPresets = (f & ClothingSubfilter.Presets) != 0;
                                    bool wantsCustom = (f & ClothingSubfilter.Custom) != 0;
                                    if (wantsPresets || wantsCustom)
                                    {
                                        if (!isPresetEntry) return false;
                                        if (wantsPresets && !wantsCustom) { if (isCustomPreset) return false; }
                                        if (wantsCustom && !wantsPresets) { if (!isCustomPreset) return false; }
                                    }
                                    if ((f & ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
                                    if ((f & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return false; }
                                    if ((f & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return false; }

                                    return true;
                                }

                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.RealClothing)) clothingSubfilterFacetCountReal++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Presets)) clothingSubfilterFacetCountPresets++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Custom)) clothingSubfilterFacetCountCustom++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Items)) clothingSubfilterFacetCountItems++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Male)) clothingSubfilterFacetCountMale++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Female)) clothingSubfilterFacetCountFemale++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Decals)) clothingSubfilterFacetCountDecals++;

                                clothingSubfilterCountAll++;
                                if (isDecal)
                                {
                                    clothingSubfilterCountDecals++;
                                }
                                else
                                {
                                    clothingSubfilterCountReal++;
                                    if (isPresetEntry) clothingSubfilterCountPresets++;
                                    if (isCustomPreset) clothingSubfilterCountCustom++;
                                    if (!isPresetEntry) clothingSubfilterCountItems++;
                                    if (cg == ClothingLoadingUtils.ResourceGender.Male) clothingSubfilterCountMale++;
                                    else if (cg == ClothingLoadingUtils.ResourceGender.Female) clothingSubfilterCountFemale++;
                                }
                            }
                        }
                    }
                }
            }

            // Count Custom (local filesystem) appearances for split-pane counts.
            // This is intentionally separate from the package loop above.
            if (isAppearanceTitle)
            {
                List<string> pathsToSearch = new List<string>();
                if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
                else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

                for (int pi = 0; pi < pathsToSearch.Count; pi++)
                {
                    string searchPath = pathsToSearch[pi];
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                    List<string> sysFileList = new List<string>();
                    try
                    {
                        FileManager.SafeGetFiles(searchPath, "*.vap", sysFileList);
                    }
                    catch
                    {
                        continue;
                    }

                    for (int fi = 0; fi < sysFileList.Count; fi++)
                    {
                        string sysPath = sysFileList[fi] ?? "";
                        string norm = sysPath.Replace('\\', '/');
                        if (!norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) &&
                            !norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        appearanceSourceCountCustom++;
                        appearanceSourceCountAll++;
                    }
                }
            }

            tagsCached = true;
        }

        public void SetCategories(List<Gallery.Category> cats)
        {
            categories = cats;
            categoriesCached = false;

            // Try to restore last tab if not specified
            string lastPageName = null;
            if (VPBConfig.Instance != null && !string.IsNullOrEmpty(VPBConfig.Instance.LastGalleryCategory))
            {
                lastPageName = VPBConfig.Instance.LastGalleryCategory;
            }
            else if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
            {
                lastPageName = Settings.Instance.LastGalleryPage.Value;
            }

            if (string.IsNullOrEmpty(currentPath) && !string.IsNullOrEmpty(lastPageName))
            {
                var cat = categories.FirstOrDefault(c => c.name == lastPageName);
                if (!string.IsNullOrEmpty(cat.name))
                {
                    currentPath = cat.path;
                    currentPaths = cat.paths;
                    currentExtension = cat.extension;
                    titleText.text = cat.name;
                    activeTags.Clear();
                }
            }

            if (string.IsNullOrEmpty(currentPath) && categories.Count > 0)
            {
                // Fallback to first category
                currentPath = categories[0].path;
                currentPaths = categories[0].paths;
                currentExtension = categories[0].extension;
                titleText.text = categories[0].name;
                activeTags.Clear();
            }

            UpdateTabs();
            // If we have categories but no path, set title to first category
            if (categories.Count > 0 && string.IsNullOrEmpty(currentPath))
            {
                titleText.text = categories[0].name;
            }
        }

        public void PushUndo(Action action)
        {
            if (action == null) return;
            undoStack.Push(action);
            if (!isApplyingUndoRedo)
            {
                try { redoStack.Clear(); } catch { }
            }
            UpdateUndoRedoButtonLabels();
            if (undoStack.Count > 20) // Limit stack size
            {
                // Stack doesn't have RemoveFromBottom, but 20 is small enough.
                // Or we can just let it grow a bit. 20 is safe.
            }
        }

        private void UpdateUndoRedoButtonLabels()
        {
            try
            {
                string undoText = "Undo (" + (undoStack != null ? undoStack.Count : 0) + ")";
                string redoText = "Redo (" + (redoStack != null ? redoStack.Count : 0) + ")";

                if (rightUndoBtnGO != null)
                {
                    Text t = null;
                    try { t = rightUndoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = undoText;
                }
                if (leftUndoBtnGO != null)
                {
                    Text t = null;
                    try { t = leftUndoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = undoText;
                }

                if (rightRedoBtnGO != null)
                {
                    Text t = null;
                    try { t = rightRedoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = redoText;
                }
                if (leftRedoBtnGO != null)
                {
                    Text t = null;
                    try { t = leftRedoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = redoText;
                }
            }
            catch { }
        }

        private Atom GetBestUndoRedoTargetAtom()
        {
            Atom a = null;
            try { a = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom; } catch { a = null; }
            if (a == null)
            {
                try { a = SelectedTargetAtom; } catch { a = null; }
            }
            if (a == null)
            {
                try
                {
                    if (SuperController.singleton != null)
                    {
                        var atoms = SuperController.singleton.GetAtoms();
                        if (atoms != null) a = atoms.FirstOrDefault(x => x != null && x.type == "Person");
                    }
                }
                catch { a = null; }
            }
            return a;
        }

        private Action CaptureAtomSnapshotAction(Atom atom)
        {
            if (atom == null) return null;
            string atomUid = null;
            try { atomUid = atom.uid; } catch { atomUid = null; }
            if (string.IsNullOrEmpty(atomUid)) return null;

            Dictionary<string, bool> geometryToggleSnapshot = null;
            List<JSONClass> storableSnapshots = new List<JSONClass>();

            bool ShouldSnapshotStorableId(string sid)
            {
                if (string.IsNullOrEmpty(sid)) return false;
                if (string.Equals(sid, "geometry", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(sid, "Skin", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.EndsWith("Presets", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.EndsWith("Preset", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.IndexOf("clothing", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (sid.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (sid.IndexOf("appearance", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }

            try
            {
                JSONStorable geometry = null;
                try { geometry = atom.GetStorableByID("geometry"); } catch { geometry = null; }
                if (geometry != null)
                {
                    geometryToggleSnapshot = new Dictionary<string, bool>();
                    List<string> names = null;
                    try { names = geometry.GetBoolParamNames(); } catch { names = null; }
                    if (names != null)
                    {
                        foreach (string key in names)
                        {
                            if (key == null) continue;
                            if (!(key.StartsWith("clothing:") || key.StartsWith("hair:"))) continue;
                            JSONStorableBool b = null;
                            try { b = geometry.GetBoolJSONParam(key); } catch { b = null; }
                            if (b != null) geometryToggleSnapshot[key] = b.val;
                        }
                    }
                }
            }
            catch { geometryToggleSnapshot = null; }

            try
            {
                List<string> ids = null;
                try { ids = atom.GetStorableIDs(); } catch { ids = null; }
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        string sid = ids[i];
                        if (string.IsNullOrEmpty(sid)) continue;
                        if (!ShouldSnapshotStorableId(sid)) continue;
                        JSONStorable s = null;
                        try { s = atom.GetStorableByID(sid); } catch { s = null; }
                        if (s == null) continue;
                        JSONClass snap = null;
                        try { snap = s.GetJSON(); } catch { snap = null; }
                        if (snap != null) storableSnapshots.Add(snap);
                    }
                }
            }
            catch { }

            return () =>
            {
                Atom targetAtom = null;
                try { targetAtom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null; } catch { targetAtom = null; }
                if (targetAtom == null) return;

                try
                {
                    if (geometryToggleSnapshot != null)
                    {
                        JSONStorable geo = null;
                        try { geo = targetAtom.GetStorableByID("geometry"); } catch { geo = null; }
                        if (geo != null)
                        {
                            foreach (var kvp in geometryToggleSnapshot)
                            {
                                JSONStorableBool b = null;
                                try { b = geo.GetBoolJSONParam(kvp.Key); } catch { b = null; }
                                if (b != null) b.val = kvp.Value;
                            }

                            List<string> currentNames = null;
                            try { currentNames = geo.GetBoolParamNames(); } catch { currentNames = null; }
                            if (currentNames != null)
                            {
                                foreach (string key2 in currentNames)
                                {
                                    if (string.IsNullOrEmpty(key2)) continue;
                                    if ((key2.StartsWith("clothing:") || key2.StartsWith("hair:")) && !geometryToggleSnapshot.ContainsKey(key2))
                                    {
                                        JSONStorableBool b2 = null;
                                        try { b2 = geo.GetBoolJSONParam(key2); } catch { b2 = null; }
                                        if (b2 != null) b2.val = false;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    for (int i = 0; i < storableSnapshots.Count; i++)
                    {
                        JSONClass snap = storableSnapshots[i];
                        if (snap == null) continue;
                        string sid = null;
                        try { sid = snap["id"].Value; } catch { sid = null; }
                        if (string.IsNullOrEmpty(sid)) continue;
                        if (!ShouldSnapshotStorableId(sid)) continue;
                        JSONStorable s = null;
                        try { s = targetAtom.GetStorableByID(sid); } catch { s = null; }
                        if (s == null) continue;
                        try { s.RestoreFromJSON(snap); } catch { }
                    }
                }
                catch { }
            };
        }

        private Action CaptureUndoRedoSnapshotAction()
        {
            Atom a = GetBestUndoRedoTargetAtom();
            if (a != null && string.Equals(a.type, "Person", StringComparison.OrdinalIgnoreCase))
            {
                Action atomSnap = CaptureAtomSnapshotAction(a);
                if (atomSnap != null) return atomSnap;
            }
            return CaptureSceneSnapshotAction();
        }

        private Action CaptureSceneSnapshotAction()
        {
            try
            {
                if (SuperController.singleton == null) return null;
                string tempPath = Path.Combine(SuperController.singleton.savesDir, "vpb_temp_undo_redo_scene_" + Guid.NewGuid().ToString() + ".json");

                JSONNode sceneRoot = null;
                try
                {
                    SuperController sc = SuperController.singleton;
                    if (sc == null) return null;

                    string[] candidates = new[]
                    {
                        "GetSaveJSON",
                        "GetSaveSceneJSON",
                        "GetSceneJSON",
                        "GetJSON",
                        "GetSaveJson",
                        "GetSceneJson",
                    };

                    object TryInvoke(MethodInfo mi)
                    {
                        if (mi == null) return null;
                        ParameterInfo[] ps = null;
                        try { ps = mi.GetParameters(); }
                        catch { ps = null; }

                        Atom bestAtom = null;
                        try { bestAtom = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom; } catch { }
                        if (bestAtom == null)
                        {
                            try { bestAtom = SelectedTargetAtom; } catch { bestAtom = null; }
                        }
                        if (bestAtom == null)
                        {
                            try
                            {
                                if (SuperController.singleton != null)
                                {
                                    var atoms = SuperController.singleton.GetAtoms();
                                    if (atoms != null) bestAtom = atoms.FirstOrDefault(a => a != null && a.type == "Person");
                                }
                            }
                            catch { bestAtom = null; }
                        }

                        object[] args = null;
                        if (ps != null && ps.Length > 0)
                        {
                            args = new object[ps.Length];
                            for (int pi = 0; pi < ps.Length; pi++)
                            {
                                Type t = ps[pi].ParameterType;
                                bool isByRef = false;
                                try { isByRef = t != null && t.IsByRef; } catch { isByRef = false; }
                                if (isByRef)
                                {
                                    try { t = t.GetElementType(); }
                                    catch { t = ps[pi].ParameterType; }
                                }

                                if (t == typeof(bool)) args[pi] = false;
                                else if (t == typeof(int)) args[pi] = 0;
                                else if (t == typeof(float)) args[pi] = 0f;
                                else if (t == typeof(string)) args[pi] = "";
                                else if (t == typeof(JSONNode) || t == typeof(JSONClass)) args[pi] = new JSONClass();
                                else if (t == typeof(Atom)) args[pi] = bestAtom;
                                else
                                {
                                    return null;
                                }
                            }
                        }

                        try { return mi.Invoke(sc, args); }
                        catch { return null; }
                    }

                    bool TrySetSceneRootFromResult(object result)
                    {
                        if (result == null) return false;
                        try
                        {
                            if (result is JSONNode node)
                            {
                                sceneRoot = node;
                                return true;
                            }

                            string s = null;
                            try { s = result.ToString(); }
                            catch { s = null; }
                            if (string.IsNullOrEmpty(s)) return false;

                            try
                            {
                                JSONNode parsed = JSON.Parse(s);
                                if (parsed != null)
                                {
                                    sceneRoot = parsed;
                                    return true;
                                }
                            }
                            catch { }
                        }
                        catch { }
                        return false;
                    }

                    for (int i = 0; i < candidates.Length && sceneRoot == null; i++)
                    {
                        MethodInfo[] methods = null;
                        try { methods = sc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => string.Equals(m.Name, candidates[i], StringComparison.Ordinal)).ToArray(); }
                        catch { methods = null; }
                        if (methods == null || methods.Length == 0) continue;

                        for (int m = 0; m < methods.Length && sceneRoot == null; m++)
                        {
                            object result = TryInvoke(methods[m]);
                            if (TrySetSceneRootFromResult(result)) break;
                        }
                    }
                }
                catch { sceneRoot = null; }

                if (sceneRoot == null) return null;

                try
                {
                    File.WriteAllText(tempPath, sceneRoot.ToString());
                }
                catch
                {
                    return null;
                }

                string loadPath = null;
                try { loadPath = UI.NormalizePath(tempPath); }
                catch { loadPath = tempPath; }

                return () =>
                {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        if (!File.Exists(tempPath)) return;
                        SceneLoadingUtils.LoadScene(loadPath, true);
                    }
                    catch { }
                    finally
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        public void RefreshTargetDropdown()
        {
            string currentSelectionUid = null;
            if (targetDropdownValue >= 0 && targetDropdownValue < personAtoms.Count)
            {
                currentSelectionUid = personAtoms[targetDropdownValue]?.uid;
            }

            personAtoms.Clear();
            targetDropdownOptions.Clear();

            if (SuperController.singleton != null)
            {
                foreach (Atom a in SuperController.singleton.GetAtoms())
                {
                    if (a.type == "Person")
                    {
                        personAtoms.Add(a);
                        targetDropdownOptions.Add(a.uid);
                    }
                }
            }

            if (targetDropdownOptions.Count == 0)
            {
                targetDropdownOptions.Add("None");
                personAtoms.Add(null);
            }

            // Try to restore selection
            if (currentSelectionUid != null)
            {
                int idx = personAtoms.FindIndex(a => a != null && a.uid == currentSelectionUid);
                if (idx >= 0) targetDropdownValue = idx;
                else targetDropdownValue = 0;
            }
            else
            {
                // Auto-select first person if we are in Clothing/Hair mode and nothing selected
                string title = titleText != null ? titleText.text : "";
                bool isClothingOrHair = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isClothingOrHair && personAtoms.Count > 0 && personAtoms[0] != null)
                {
                    targetDropdownValue = 0;
                }
                else
                {
                    targetDropdownValue = 0;
                }
            }
            
            UpdateTargetDropdownUI();
        }

        public void CycleTarget(bool forward)
        {
            if (targetDropdownOptions.Count > 0)
            {
                if (forward)
                    targetDropdownValue = (targetDropdownValue + 1) % targetDropdownOptions.Count;
                else
                    targetDropdownValue = (targetDropdownValue - 1 + targetDropdownOptions.Count) % targetDropdownOptions.Count;
                UpdateTargetDropdownUI();
            }
        }

        private void UpdateTargetDropdownUI()
        {
            string valText = (targetDropdownValue >= 0 && targetDropdownValue < targetDropdownOptions.Count) 
                ? targetDropdownOptions[targetDropdownValue] 
                : "None";
            string fullText = "Target: " + valText;

            if (leftTargetBtnText != null) leftTargetBtnText.text = fullText;
            if (rightTargetBtnText != null) rightTargetBtnText.text = fullText;
        }

        private void Undo()
        {
            if (undoStack.Count > 0)
            {
                Action action = undoStack.Pop();
                try
                {
                    Action redoAction = CaptureUndoRedoSnapshotAction();
                    if (redoAction != null) redoStack.Push(redoAction);
                    isApplyingUndoRedo = true;
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Undo: " + ex.Message);
                }
                finally
                {
                    isApplyingUndoRedo = false;
                }

                UpdateUndoRedoButtonLabels();
                try
                {
                    // Ensure context submenus refresh immediately after Undo restores items.
                    Atom tgt = null;
                    try { tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom; } catch { }
                    if (clothingSubmenuOpen) SyncClothingSubmenu(tgt, true);
                    if (hairSubmenuOpen) SyncHairSubmenu(tgt, true);
                    UpdateSideContextActions();
                }
                catch { }
            }
            else
            {
                LogUtil.Log("[VPB] Undo: stack empty");
                UpdateUndoRedoButtonLabels();
            }
        }

        private void Redo()
        {
            if (redoStack.Count > 0)
            {
                Action action = redoStack.Pop();
                try
                {
                    Action undoAction = CaptureUndoRedoSnapshotAction();
                    if (undoAction != null) undoStack.Push(undoAction);
                    isApplyingUndoRedo = true;
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Redo: " + ex.Message);
                }
                finally
                {
                    isApplyingUndoRedo = false;
                }

                UpdateUndoRedoButtonLabels();
                try
                {
                    Atom tgt = null;
                    try { tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom; } catch { }
                    if (clothingSubmenuOpen) SyncClothingSubmenu(tgt, true);
                    if (hairSubmenuOpen) SyncHairSubmenu(tgt, true);
                    UpdateSideContextActions();
                }
                catch { }
            }
            else
            {
                LogUtil.Log("[VPB] Redo: stack empty");
                UpdateUndoRedoButtonLabels();
            }
        }

        private bool IsMatch(FileEntry entry, List<string> paths, string singlePath, string[] extensions)
        {
            if (entry == null) return false;

            string checkPath = entry.Path;
            if (entry is VarFileEntry vfe)
            {
                checkPath = vfe.InternalPath;
            }
            
            // Extension Filter
            bool extMatch = false;
            if (extensions == null || extensions.Length == 0 || (extensions.Length == 1 && string.IsNullOrEmpty(extensions[0])))
            {
                extMatch = true;
            }
            else
            {
                string entryExt = Path.GetExtension(checkPath);
                if (!string.IsNullOrEmpty(entryExt))
                {
                    entryExt = entryExt.Substring(1); // remove dot
                    foreach (var ext in extensions)
                    {
                        if (string.Equals(entryExt, ext, StringComparison.OrdinalIgnoreCase))
                        {
                            extMatch = true;
                            break;
                        }
                    }
                }
            }
            if (!extMatch) return false;

            // Path Filter
            if (paths != null && paths.Count > 0)
            {
                foreach (var p in paths)
                {
                    if (checkPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)) 
                    {
                        // Special Case: "Saves/Person" is often used for Poses, but "Saves/Person/appearance" are Appearances.
                        // If we are looking for Poses (Saves/Person) and found an appearance, skip it unless specifically requested.
                        if (string.Equals(p, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                        {
                            if (checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        return true;
                    }
                }
                return false;
            }
            
            if (!string.IsNullOrEmpty(singlePath))
            {
                if (checkPath.StartsWith(singlePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(singlePath, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(singlePath, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    return true;
                }
                return false;
            }

            return true;
        }

        private void ClearCurrentFilter(bool isRight)
        {
            ContentType? type = isRight ? rightActiveContent : leftActiveContent;
            
            if (!type.HasValue) return;

            // Simply close the panel (toggle off)
            if (isRight) ToggleRight(type.Value);
            else ToggleLeft(type.Value);
            
            // Optionally clear filters if desired, but "X" on a side tab usually implies "Close this tab"
            // If the user meant "Clear Filter" specifically for search text, that's inside the panel.
            // "the X button should be on the outside of the side buttons... side buttons that are being hidden"
            // This strongly suggests a close button for the side panel overlay.
            
            UpdateTabs();
        }
    }
}
