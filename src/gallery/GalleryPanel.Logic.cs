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
        private void CacheCategoryCounts()
        {
            if (categories == null) return;
            categoryCounts.Clear();
            
            var catExtensions = new Dictionary<string, string[]>();
            foreach (var c in categories) 
            {
                categoryCounts[c.name] = 0;
                catExtensions[c.name] = c.extension.Split('|');
            }

            var sortedCategories = categories.OrderByDescending(c => c.path.Length).ToList();

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
                    foreach (var entry in pkg.FileEntries)
                    {
                        foreach (var cat in sortedCategories)
                        {
                            if (IsMatch(entry, cat.paths, cat.path, catExtensions[cat.name]))
                            {
                                categoryCounts[cat.name]++;
                                break;
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
            string[] extensions = currentExtension.Split('|');
            
            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (string.IsNullOrEmpty(pkg.Creator)) continue;
                if (pkg.FileEntries == null) continue;

                foreach (var entry in pkg.FileEntries)
                {
                     if (IsMatch(entry, currentPaths, currentPath, extensions))
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

        private void CacheTagCounts()
        {
            tagCounts.Clear();
            if (FileManager.PackagesByUid == null) return;

            string[] extensions = currentExtension.Split('|');
            
            // Collect all relevant tags to count
            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string title = titleText != null ? titleText.text : "";
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
            
            if (tagsToCount.Count == 0) return;

            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (pkg.FileEntries == null) continue;
                
                // If filtering by creator, respect it
                if (!string.IsNullOrEmpty(currentCreator))
                {
                    if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                }

                foreach (var entry in pkg.FileEntries)
                {
                    if (IsMatch(entry, currentPaths, currentPath, extensions))
                    {
                        string pathLower = entry.Path.ToLowerInvariant();
                        foreach(var tag in tagsToCount)
                        {
                            if (pathLower.Contains(tag)) // tagsToCount are lowercase
                            {
                                if (!tagCounts.ContainsKey(tag)) tagCounts[tag] = 0;
                                tagCounts[tag]++;
                            }
                        }
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
            if (string.IsNullOrEmpty(currentPath) && Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
            {
                string lastPageName = Settings.Instance.LastGalleryPage.Value;
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
                    if (checkPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            
            if (!string.IsNullOrEmpty(singlePath))
            {
                return checkPath.StartsWith(singlePath, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
    }
}
