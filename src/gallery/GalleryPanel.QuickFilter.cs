using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        public QuickFilterEntry CaptureQuickFilterState()
        {
            var entry = new QuickFilterEntry();
            
            // Use Preset#N as default name, ensuring uniqueness
            int nextNum = 1;
            if (QuickFilterSettings.Instance != null)
            {
                var existingNames = new HashSet<string>(QuickFilterSettings.Instance.Filters.Select(f => f.Name));
                while (existingNames.Contains("Preset#" + nextNum))
                {
                    nextNum++;
                }
            }
            string title = "Preset#" + nextNum;
            
            entry.Name = title;
            entry.CategoryPath = currentPath;
            entry.SearchText = nameFilter;
            entry.Creator = currentCreator;
            entry.Status = currentStatus;
            entry.Tags = activeTags.ToList();
            
            var sort = GetSortState("Files");
            if (sort != null) entry.SortState = sort.Clone();
            
            return entry;
        }

        public void ApplyQuickFilterState(QuickFilterEntry entry)
        {
            if (entry == null) return;

            // 1. Restore Category
            if (!string.IsNullOrEmpty(entry.CategoryPath))
            {
                // Find category with this path
                var cat = categories.FirstOrDefault(c => c.path == entry.CategoryPath);
                
                // If found, update state
                if (!string.IsNullOrEmpty(cat.name))
                {
                    currentPath = cat.path;
                    currentPaths = cat.paths;
                    currentExtension = cat.extension;
                    currentCategoryTitle = cat.name;
                    if (titleText != null) titleText.text = cat.name;
                }
            }

            // 2. Restore Search
            SetNameFilter(entry.SearchText); 
            if (titleSearchInput != null) 
            {
                titleSearchInput.text = entry.SearchText ?? "";
            }

            // 3. Restore Creator
            currentCreator = entry.Creator ?? "";
            
            // 3b. Restore Status
            currentStatus = entry.Status ?? "";

            // 4. Restore Tags
            activeTags.Clear();
            if (entry.Tags != null)
            {
                foreach (var t in entry.Tags) activeTags.Add(t);
            }

            // 5. Restore Sort
            if (entry.SortState != null)
            {
                SaveSortState("Files", entry.SortState);
                if (fileSortBtnText != null)
                {
                    UpdateSortButtonText(fileSortBtnText, entry.SortState);
                }
                SyncRatingSortToggleState();
            }

            // 6. Refresh
            UpdateTabs(); // Refreshes sidebars
            RefreshFiles(); // Refreshes grid
            
            ShowTemporaryStatus("Quick Filter Applied: " + entry.Name);
        }

        public void ToggleQuickFilters()
        {
            if (quickFiltersUI == null) return;
            
            bool visible = !quickFiltersUI.IsVisible;
            quickFiltersUI.SetVisible(visible);
            
            SyncQuickFilterToggleState();
        }

        public void SyncQuickFilterToggleState()
        {
            if (quickFiltersToggleBtnText != null && quickFiltersUI != null)
            {
                quickFiltersToggleBtnText.color = quickFiltersUI.IsVisible ? Color.green : Color.white;
            }
        }
    }
}
