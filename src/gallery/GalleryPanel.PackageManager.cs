using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject packageManagerContainer;

        private bool IsPackageManagerUIVisible()
        {
            return packageManagerContainer != null && packageManagerContainer.activeInHierarchy;
        }

        private void ShowPackageManagerUI()
        {
             if (scrollRect != null) scrollRect.gameObject.SetActive(false);
             
             // Hide QuickFilters if they are showing, as PM has its own filtering
             if (quickFiltersUI != null) quickFiltersUI.SetVisible(false);

             // Hide Actions Panel (Details)
             if (actionsPanel != null) actionsPanel.Hide();

             if (packageManagerContainer == null)
             {
                 packageManagerContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0,0,0,0), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
                 
                 // Call plugin to build UI
                 VamHookPlugin.singleton?.EmbedPackageManager(packageManagerContainer.GetComponent<RectTransform>());
             }
             
             // Optim: Set filters directly without triggering multiple refreshes
             if (VamHookPlugin.singleton != null)
             {
                 VamHookPlugin.singleton.SetPkgMgrFilter(nameFilter);
                 VamHookPlugin.singleton.SetPkgMgrCreatorFilter(currentCreator);
                 VamHookPlugin.singleton.ClearPkgMgrCategoryFilters();
                 
                 var sortState = GetSortState("Files");
                 if (sortState != null)
                 {
                    string sortField = "Name";
                    switch (sortState.Type)
                    {
                        case SortType.Date: sortField = "Age"; break;
                        case SortType.Size: sortField = "Size"; break;
                        case SortType.Rating: sortField = "Name"; break;
                        default: sortField = "Name"; break;
                    }
                    VamHookPlugin.singleton.SetPkgMgrSortField(sortField);
                    VamHookPlugin.singleton.SetPkgMgrSortDirection(sortState.Direction == SortDirection.Ascending);
                 }
                 
                 if (string.IsNullOrEmpty(currentCategoryTitle))
                 {
                    VamHookPlugin.singleton.SetPkgMgrCategoryFilterByType("All");
                 }
                 else if (categories != null)
                 {
                    var cat = categories.Find(c => c.name == currentCategoryTitle);
                    if (!string.IsNullOrEmpty(cat.name))
                    {
                        string pmType = cat.name;
                        if (string.Equals(pmType, "Scenes", System.StringComparison.OrdinalIgnoreCase)) pmType = "Scene";
                        else if (string.Equals(pmType, "SubScenes", System.StringComparison.OrdinalIgnoreCase)) pmType = "SubScene";
                        else if (string.Equals(pmType, "Scripts", System.StringComparison.OrdinalIgnoreCase)) pmType = "Script";

                        VamHookPlugin.singleton.SetPkgMgrCategoryFilterByType(pmType);
                    }
                 }
                 
                 float rowHeight = 400f / Mathf.Max(1, gridColumnCount);
                 VamHookPlugin.singleton.SetPkgMgrZoom(rowHeight);
             }

             packageManagerContainer.SetActive(true);
             VamHookPlugin.singleton?.SetPackageManagerVisible(true);
             
             currentPage = 0;
             UpdatePackageManagerPage();
             UpdateLayout();
        }

        private void HidePackageManagerUI()
        {
             if (packageManagerContainer != null) packageManagerContainer.SetActive(false);
             VamHookPlugin.singleton?.SetPackageManagerVisible(false);
        }
        
        // Called when search input changes
        private void UpdatePackageManagerFilter(string val)
        {
            VamHookPlugin.singleton?.SetPkgMgrFilter(val);
            UpdatePackageManagerPage();
        }

        // Called when creator filter changes
        private void UpdatePackageManagerCreatorFilter(string creator)
        {
            VamHookPlugin.singleton?.SetPkgMgrCreatorFilter(creator);
            UpdatePackageManagerPage();
        }

        // Called when category filter changes
        private void UpdatePackageManagerCategoryFilter()
        {
            VamHookPlugin.singleton?.ClearPkgMgrCategoryFilters();
            UpdatePackageManagerPage();
        }

        // Called when sort state changes
        private void UpdatePackageManagerSort(string context)
        {
            if (context != "Files") return;

            var sortState = GetSortState(context);
            if (sortState == null) return;

            string sortField = "Name";
            switch (sortState.Type)
            {
                case SortType.Date: sortField = "Age"; break;
                case SortType.Size: sortField = "Size"; break;
                case SortType.Rating: sortField = "Name"; break;
                default: sortField = "Name"; break;
            }

            VamHookPlugin.singleton?.SetPkgMgrSortField(sortField);
            VamHookPlugin.singleton?.SetPkgMgrSortDirection(sortState.Direction == SortDirection.Ascending);
            UpdatePackageManagerPage();
        }

        // Called when category is selected
        private void UpdatePackageManagerCategoryByName(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
            {
                VamHookPlugin.singleton?.SetPkgMgrCategoryFilterByType("All");
            }
            else if (categories != null)
            {
                var cat = categories.Find(c => c.name == categoryName);
                if (!string.IsNullOrEmpty(cat.name))
                {
                    string pmType = cat.name;
                    if (string.Equals(pmType, "Scenes", System.StringComparison.OrdinalIgnoreCase)) pmType = "Scene";
                    else if (string.Equals(pmType, "SubScenes", System.StringComparison.OrdinalIgnoreCase)) pmType = "SubScene";
                    else if (string.Equals(pmType, "Scripts", System.StringComparison.OrdinalIgnoreCase)) pmType = "Script";

                    VamHookPlugin.singleton?.SetPkgMgrCategoryFilterByType(pmType);
                }
            }
            UpdatePackageManagerPage();
        }

        private void UpdatePackageManagerZoom()
        {
            if (VamHookPlugin.singleton == null) return;
            // gridColumnCount: 1 (large) to 12 (small)
            // Default 5 -> 80f
            // 400 / 5 = 80.
            float rowHeight = 400f / Mathf.Max(1, gridColumnCount);
            VamHookPlugin.singleton.SetPkgMgrZoom(rowHeight);
            
            // Also need to update page size because row height changed
            UpdatePackageManagerPage();
        }

        private void UpdatePackageManagerPage()
        {
            if (VamHookPlugin.singleton == null) return;
            
            float rowHeight = 400f / Mathf.Max(1, gridColumnCount);
            // Approx visible height = 650 (safe estimate)
            // User requested larger pages: 100 entries per page
            int itemsPerPage = 100;
            
            VamHookPlugin.singleton.SetPkgMgrPage(currentPage, itemsPerPage);
            
            // Update pagination text
            int totalItems = VamHookPlugin.singleton.GetPkgMgrTotalVisibleCount();
            int totalPages = Mathf.CeilToInt((float)totalItems / itemsPerPage);
            
            // Update GalleryPanel state so UI text reflects it
            lastTotalItems = totalItems;
            lastTotalPages = totalPages;
            
            // Prevent current page from exceeding total pages
            if (currentPage >= totalPages)
            {
                 currentPage = Mathf.Max(0, totalPages - 1);
                 VamHookPlugin.singleton.SetPkgMgrPage(currentPage, itemsPerPage);
            }
            
            UpdatePaginationText();
        }
    }
}
