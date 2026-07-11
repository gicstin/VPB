using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void BuildCategoryTabs(GameObject container, List<GameObject> trackedButtons)
        {
            if (categories == null || categories.Count == 0) return;
            if (!categoriesCached) CacheCategoryCounts();

            var displayCategories = new List<Gallery.Category>(categories);
            var sortState = GetSortState("Category");
            GallerySortManager.Instance.SortCategories(displayCategories, sortState, categoryCounts);

            foreach (var cat in displayCategories)
            {
                if (!string.IsNullOrEmpty(categoryFilter) && cat.name.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var c = cat;
                bool isActive = (c.path == currentPath && c.extension == currentExtension);
                Color btnColor = isActive ? ColorCategory : ColorInactiveRow;

                int count = 0;
                if (categoryCounts.ContainsKey(c.name)) count = categoryCounts[c.name];

                // Keep some special rows visible even when count is 0.
                // - Plugins: mostly local Custom/Scripts files (fresh install -> 0)
                // - ALL VAR: package-level listing; should stay available as navigation root
                if (count == 0
                    && !isActive
                    && !string.Equals(c.name, "Plugins", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.name, "ALL VAR", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.name, Gallery.EverythingCategoryName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (VPBConfig.Instance != null && VPBConfig.Instance.IsHiddenCategory(c.name) && !isActive) continue;

                string label = c.name + " (" + count + ")";

                CreateTabButton(container.transform, label, btnColor, isActive, () => {
                    if (LogGalleryCategoryTypeSwitchTiming)
                        BeginGalleryCategoryTypeNavigationTiming(c.name);
                    Show(c.name, c.extension, c.path);
                    if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                    {
                        Settings.Instance.LastGalleryPage.Value = c.name;
                    }
                    if (VPBConfig.Instance != null)
                    {
                        VPBConfig.Instance.LastGalleryCategory = c.name;
                        // Write disk only: Save(true) runs ConfigChanged -> UpdateLayout (~seconds). Show/UpdateTabs already refreshed UI.
                        try { VPBConfig.Instance.Save(false); } catch { }
                    }
                    // Show() already ran UpdateTabs or UpdateTabsImpl(false) while refresh runs; a second
                    // full UpdateTabs() here blocked the UI for seconds. Side strips refresh when
                    // RefreshFilesRoutine finishes (DeferredGallerySideTabsAfterGridReady).
                }, trackedButtons, () => {
                    SaveCurrentCategoryFilterState(currentCategoryTitle, currentPath);
                    currentPath = "";
                    currentPaths = null;
                    currentExtension = "";
                    if (titleText != null) titleText.text = VPBTranslation.T("gallery.title.all_categories", "All Categories");
                    ClearFiltersForNewCategory();
                    RefreshFilesAndTabs();
                });
            }
        }

        private void BuildCreatorTabs(GameObject container, bool isLeft)
        {
            if (!creatorsCached) CacheCreators();
            var displayCreators = GetCreatorsForDisplay();
            if (displayCreators == null || displayCreators.Count == 0)
            {
                _creatorVirtView.Clear();
                _creatorVirtViewSig = null;
                UpdateCreatorVirtualVisible(isLeft);
                return;
            }

            // Sort once (in-place) then virtualize visible rows only.
            var sortState = GetSortState("Creator");
            GallerySortManager.Instance.SortCreators(displayCreators, sortState);

            string sig = ComputeCreatorVirtViewSignature();
            if (!string.Equals(_creatorVirtViewSig, sig, StringComparison.Ordinal))
            {
                _creatorVirtViewSig = sig;
                _creatorVirtView.Clear();
                string filterNow = creatorFilter ?? "";

                // Build set of creators present in current filtered file list when name search active.
                HashSet<string> creatorsInResults = null;
                bool hasNameFilter = nameFilterTerms != null && nameFilterTerms.Length > 0;
                if (hasNameFilter && currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                {
                    creatorsInResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < currentFilteredFiles.Count; i++)
                    {
                        var fe = currentFilteredFiles[i];
                        if (fe == null) continue;
                        string creator = null;
                        try { creator = fe.Uid; } catch { }
                        if (string.IsNullOrEmpty(creator)) continue;
                        int dot1 = creator.IndexOf('.');
                        if (dot1 > 0) creator = creator.Substring(0, dot1);
                        if (!string.IsNullOrEmpty(creator))
                            creatorsInResults.Add(creator);
                    }
                }

                for (int i = 0; i < displayCreators.Count; i++)
                {
                    var c = displayCreators[i];
                    if (string.IsNullOrEmpty(c.Name)) continue;
                    if (!string.IsNullOrEmpty(filterNow) && c.Name.IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (creatorsInResults != null && !creatorsInResults.Contains(c.Name)) continue;
                    _creatorVirtView.Add(c);
                }

                // New view list: reset scroll to top for stability.
                ScrollRect sr = container.GetComponentInParent<ScrollRect>();
                if (sr != null) sr.verticalNormalizedPosition = 1f;
                if (isLeft) _leftCreatorVirtLastFirstIdx = -1;
                else _rightCreatorVirtLastFirstIdx = -1;
            }

            EnsureCreatorVirtScrollHook(isLeft, container);

            // UpdateCreatorVirtualVisible handles its own pooling and tracking.
            // We do NOT add them to trackedButtons because that would return them to shared pool every UpdateTabs call.
            UpdateCreatorVirtualVisible(isLeft);
        }

        private void BuildPathTabs(GameObject container, List<GameObject> trackedButtons)
        {
            if (!pathsCached) CachePaths();
            if (cachedPaths == null || cachedPaths.Count == 0) return;

            var displayPaths = new List<PathCacheEntry>(cachedPaths);
            var sortState = GetSortState("Path");
            if (sortState.Type == SortType.Count)
            {
                if (sortState.Direction == SortDirection.Ascending)
                    displayPaths.Sort((a, b) => a.Count.CompareTo(b.Count));
                else
                    displayPaths.Sort((a, b) => b.Count.CompareTo(a.Count));
            }
            else
            {
                if (sortState.Direction == SortDirection.Ascending)
                    displayPaths.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
                else
                    displayPaths.Sort((a, b) => string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase));
            }

            string filterNow = pathFilter ?? "";
            for (int i = 0; i < displayPaths.Count; i++)
            {
                PathCacheEntry pe = displayPaths[i];
                if (string.IsNullOrEmpty(pe.Path)) continue;
                if (!string.IsNullOrEmpty(filterNow) && pe.Path.IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool isActive = string.Equals(currentPackagePathFilter, pe.Path, StringComparison.OrdinalIgnoreCase);
                if (pe.Count <= 0 && !isActive) continue;

                string label = pe.Path;
                Color btnColor = isActive ? ColorPath : ColorInactiveRow;
                string pathValue = pe.Path;
                CreateTabButton(container.transform, label, btnColor, isActive, () =>
                {
                    if (string.Equals(currentPackagePathFilter, pathValue, StringComparison.OrdinalIgnoreCase))
                        currentPackagePathFilter = "";
                    else
                        currentPackagePathFilter = pathValue;

                    categoriesCached = false;
                    creatorsCached = false;
                    tagsCached = false;
                    userTagsCached = false;
                    RefreshFilesAndTabs();
                }, trackedButtons, () =>
                {
                    currentPackagePathFilter = "";
                    categoriesCached = false;
                    creatorsCached = false;
                    tagsCached = false;
                    userTagsCached = false;
                    RefreshFilesAndTabs();
                }, pathValue);

                GameObject pathBtnGO = trackedButtons.Count > 0 ? trackedButtons[trackedButtons.Count - 1] : null;
                if (pathBtnGO != null)
                {
                    float s = ChromeScale;
                    float rowSingle = GalleryUiDesignTokens.SideTabRowHeightRef * s;
                    LayoutElement le = pathBtnGO.GetComponent<LayoutElement>();
                    if (le == null) le = pathBtnGO.AddComponent<LayoutElement>();
                    le.minHeight = rowSingle;
                    le.preferredHeight = rowSingle;

                    Text txt = pathBtnGO.GetComponentInChildren<Text>(true);
                    if (txt != null)
                    {
                        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                        txt.verticalOverflow = VerticalWrapMode.Truncate;
                        txt.alignment = TextAnchor.MiddleLeft;
                        txt.resizeTextForBestFit = false;

                        RectTransform txtRT = txt.GetComponent<RectTransform>();
                        if (txtRT != null)
                        {
                            float padX = 10f * s;
                            txtRT.offsetMin = new Vector2(padX, txtRT.offsetMin.y);
                            txtRT.offsetMax = new Vector2(-padX, txtRT.offsetMax.y);
                        }
                    }
                }
            }
        }
    }
}

