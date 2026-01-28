using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        public string CurrentCategoryTitle => currentCategoryTitle;

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

            clothingSubfilterCountAll = 0;
            clothingSubfilterCountPresets = 0;
            clothingSubfilterCountItems = 0;
            clothingSubfilterCountMale = 0;
            clothingSubfilterCountFemale = 0;

            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            // Build extension set for fast lookup
            HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in extensions) if (!string.IsNullOrEmpty(e)) targetExts.Add(e.Trim());

            // Collect all relevant tags to count
            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string title = titleText != null ? titleText.text : "";
            bool isClothingTitle = (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0);
            string clothingSf = currentClothingSubfilter ?? "All Clothing";
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

            if (tagsToCount.Count == 0) return;

            // Split tags into single-word and multi-word
            HashSet<string> singleWordTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> multiWordTags = new List<string>();
            char[] separators = new char[] { '/', '\\', '.', '_', '-', ' ' };

            foreach (var t in tagsToCount)
            {
                if (t.IndexOfAny(new char[] { ' ', '_', '-' }) >= 0) multiWordTags.Add(t);
                else singleWordTags.Add(t);
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

						ClothingLoadingUtils.ClassifyClothingHairPath(internalPath, out ck, out cg);
						isClothingEntry = (ck == ClothingLoadingUtils.ResourceKind.Clothing);
						if (isClothingEntry)
                        {
                            // For Clothing category we include both .vam and .vap, and subfilters split them.
                            isPresetEntry = (ext.Equals("vap", StringComparison.OrdinalIgnoreCase));

                            clothingSubfilterCountAll++;
                            if (isPresetEntry) clothingSubfilterCountPresets++;
                            else clothingSubfilterCountItems++;
                            if (cg == ClothingLoadingUtils.ResourceGender.Male) clothingSubfilterCountMale++;
                            else if (cg == ClothingLoadingUtils.ResourceGender.Female) clothingSubfilterCountFemale++;

                            // Apply current subfilter to tag counting.
                            if (clothingSf == "Presets")
                            {
                                if (!isPresetEntry) continue;
                            }
                            else if (clothingSf == "Items")
                            {
                                if (isPresetEntry) continue;
                            }
                            else if (clothingSf == "Male")
                            {
                                if (cg != ClothingLoadingUtils.ResourceGender.Male) continue;
                            }
                            else if (clothingSf == "Female")
                            {
                                if (cg != ClothingLoadingUtils.ResourceGender.Female) continue;
                            }
                        }
                        else
                        {
                            // When browsing Clothing, ignore non-clothing entries for tag counts.
                            continue;
                        }
                    }

                    string pathLower = internalPath.ToLowerInvariant();
                    
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
            if (undoStack.Count > 20) // Limit stack size
            {
                // Stack doesn't have RemoveFromBottom, but 20 is small enough.
                // Or we can just let it grow a bit. 20 is safe.
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
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Undo: " + ex.Message);
                }
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
