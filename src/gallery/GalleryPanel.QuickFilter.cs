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
            var settings = QuickFilterSettings.Instance;
            if (settings != null && settings.Filters != null)
            {
                var existingNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var f in settings.Filters)
                    if (!string.IsNullOrEmpty(f?.Name)) existingNames.Add(f.Name);

                string candidate;
                do { candidate = "Preset#" + nextNum++; }
                while (existingNames.Contains(candidate));

                entry.Name = candidate;
            }
            else entry.Name = "Preset#" + nextNum;
            entry.CategoryPath = currentPath;
            entry.SearchText = nameFilter;
            entry.Creator = currentCreator;
            entry.Tags = new List<string>(activeTags);
            
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
                Gallery.Category? cat = null;
                if (categories != null)
                {
                    for (int i = 0; i < categories.Count; i++)
                    {
                        var c = categories[i];
                        if (c.path == entry.CategoryPath) { cat = c; break; }
                    }
                }

                if (cat.HasValue && !string.IsNullOrEmpty(cat.Value.name))
                {
                    var v = cat.Value;
                    currentPath = v.path;
                    currentPaths = v.paths;
                    currentExtension = v.extension;
                    currentCategoryTitle = v.name;
                    if (titleText != null) titleText.text = v.name;
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
            _currentCreatorSetSrc = null;
            try { UpdateTitleCreatorButtonVisual(); } catch { }

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
            if (quickFiltersUI == null) return;
            bool on = quickFiltersUI.IsVisible;
            if (quickFiltersToggleBtnIconImage != null)
                quickFiltersToggleBtnIconImage.color = on ? Color.green : Color.white;
            else if (quickFiltersToggleBtnText != null)
                quickFiltersToggleBtnText.color = on ? Color.green : Color.white;
        }
    }
}
