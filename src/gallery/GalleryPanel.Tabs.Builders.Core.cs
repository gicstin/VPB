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
                Sprite catIcon = GetCategoryTabIcon(c.name);
                Color? catIconBackdrop = catIcon != null ? GetCategoryTabIconBackdrop(c.name) : (Color?)null;
                TextAnchor labelAnchor = catIcon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;

                CreateTabButton(container.transform, label, btnColor, isActive, () => {
                    // Plugins float is overlay palette — keep current gallery category/grid (e.g. Clothing).
                    if (string.Equals(c.name, "Plugins", StringComparison.OrdinalIgnoreCase))
                    {
                        try { OpenPluginsFloat(forceShow: true); } catch { }
                        return;
                    }
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
                }, null, null, labelAnchor, 0f, 0f, catIcon, catIconBackdrop);
            }
        }

        /// <summary>Per-category left icon for side-rail Category mode (c_*.png). Falls back to gallery_category. Null when setting off.</summary>
        private Sprite GetCategoryTabIcon(string categoryName)
        {
            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryShowCategoryIcons)
                return null;

            string path = null;
            if (!string.IsNullOrEmpty(categoryName))
            {
                if (string.Equals(categoryName, "Scenes", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_scene.png";
                else if (string.Equals(categoryName, "SubScenes", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_subscene.png";
                else if (string.Equals(categoryName, "Clothing", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_clothing.png";
                else if (string.Equals(categoryName, "Hair", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_hair.png";
                else if (string.Equals(categoryName, "Pose", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_pose.png";
                else if (string.Equals(categoryName, "Appearance", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_appearance.png";
                else if (string.Equals(categoryName, "Plugins", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_plugins.png";
                else if (string.Equals(categoryName, "Skin", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_skin.png";
                else if (string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryName, "ALL VAR", StringComparison.OrdinalIgnoreCase))
                    path = "vpb_icons/c_all.png";
            }

            if (path != null)
            {
                Sprite s = UI.LoadIconSprite(path, UI.SideRailIconGlyphTint);
                if (s != null) return s;
            }
            return galleryCategorySprite;
        }

        /// <summary>Colored chip behind category side-rail icons. Dark accents so white glyphs stay readable.</summary>
        private static Color GetCategoryTabIconBackdrop(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
                return new Color(0.28f, 0.18f, 0.22f, 1f);

            if (string.Equals(categoryName, Gallery.EverythingCategoryName, StringComparison.OrdinalIgnoreCase))
                return new Color(0.42f, 0.12f, 0.12f, 1f); // dark red
            if (string.Equals(categoryName, "Plugins", StringComparison.OrdinalIgnoreCase))
                return new Color(0.28f, 0.12f, 0.38f, 1f); // dark purple
            if (string.Equals(categoryName, "Clothing", StringComparison.OrdinalIgnoreCase))
                return new Color(0.14f, 0.26f, 0.48f, 1f); // dark blue
            if (string.Equals(categoryName, "ALL VAR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase))
                return new Color(0.45f, 0.12f, 0.32f, 1f); // dark magenta
            if (string.Equals(categoryName, "Pose", StringComparison.OrdinalIgnoreCase))
                return new Color(0.32f, 0.40f, 0.12f, 1f); // dark olive-lime
            if (string.Equals(categoryName, "Scenes", StringComparison.OrdinalIgnoreCase))
                return new Color(0.10f, 0.36f, 0.38f, 1f); // dark teal
            if (string.Equals(categoryName, "Hair", StringComparison.OrdinalIgnoreCase))
                return new Color(0.14f, 0.34f, 0.18f, 1f); // dark green
            if (string.Equals(categoryName, "CUA", StringComparison.OrdinalIgnoreCase))
                return new Color(0.32f, 0.34f, 0.12f, 1f); // dark olive
            if (string.Equals(categoryName, "Appearance", StringComparison.OrdinalIgnoreCase))
                return new Color(0.22f, 0.20f, 0.42f, 1f); // dark indigo
            if (string.Equals(categoryName, "SubScenes", StringComparison.OrdinalIgnoreCase))
                return new Color(0.48f, 0.28f, 0.10f, 1f); // dark orange
            if (string.Equals(categoryName, "Skin", StringComparison.OrdinalIgnoreCase))
                return new Color(0.36f, 0.26f, 0.16f, 1f); // dark brown
            if (string.Equals(categoryName, "Plugin Presets", StringComparison.OrdinalIgnoreCase))
                return new Color(0.42f, 0.18f, 0.32f, 1f); // dark pink
            if (string.Equals(categoryName, "Morphs", StringComparison.OrdinalIgnoreCase))
                return new Color(0.32f, 0.16f, 0.40f, 1f); // dark violet
            if (string.Equals(categoryName, "Hair Presets", StringComparison.OrdinalIgnoreCase))
                return new Color(0.16f, 0.34f, 0.28f, 1f); // dark teal-green
            if (string.Equals(categoryName, "Body Physics", StringComparison.OrdinalIgnoreCase))
                return new Color(0.36f, 0.22f, 0.14f, 1f); // dark warm brown
            if (string.Equals(categoryName, "Animation", StringComparison.OrdinalIgnoreCase))
                return new Color(0.14f, 0.28f, 0.38f, 1f); // dark steel
            if (string.Equals(categoryName, "General", StringComparison.OrdinalIgnoreCase))
                return new Color(0.24f, 0.26f, 0.30f, 1f); // dark slate

            // Unknown categories: stable dark hue from name hash (not launch-random).
            int h = 0;
            for (int i = 0; i < categoryName.Length; i++)
                h = unchecked(h * 31 + char.ToLowerInvariant(categoryName[i]));
            float hue = ((h % 360) + 360) % 360 / 360f;
            return Color.HSVToRGB(hue, 0.55f, 0.38f);
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
            // Rated-only may override display to Rating; saved Creator sort stays unless user picks Rating.
            var sortState = GetCreatorListSortState();
            GallerySortManager.Instance.SortCreators(displayCreators, sortState);

            string sig = ComputeCreatorVirtViewSignature();
            if (!string.Equals(_creatorVirtViewSig, sig, StringComparison.Ordinal))
            {
                _creatorVirtViewSig = sig;
                _creatorVirtView.Clear();
                string filterNow = creatorFilter ?? "";

                // Build set of creators present in current filtered file list when name search active.
                HashSet<string> creatorsInResults = null;
                bool hasNameFilter = HasActiveNameFilter();
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
                    if (!CreatorPassesRatedOnlyFilter(c.Name)) continue;
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
                bool zeroCount = pe.Count <= 0;

                // Keep zero-count folders visible (muted). Counts are category-scoped; folder tree is not.
                string label = pe.Path + " (" + pe.Count + ")";
                Color btnColor = isActive
                    ? ColorPath
                    : (zeroCount ? ColorPathZeroCount : ColorInactiveRow);
                string pathValue = pe.Path;
                int pathCountSnap = pe.Count;
                CreateTabButton(container.transform, label, btnColor, isActive, () =>
                {
                    bool selecting = !string.Equals(currentPackagePathFilter, pathValue, StringComparison.OrdinalIgnoreCase);
                    if (!selecting)
                        currentPackagePathFilter = "";
                    else
                        currentPackagePathFilter = pathValue;

                    categoriesCached = false;
                    creatorsCached = false;
                    tagsCached = false;
                    userTagsCached = false;
                    RefreshFilesAndTabs();

                    if (selecting && pathCountSnap <= 0)
                    {
                        string cat = currentCategoryTitle ?? "";
                        if (string.IsNullOrEmpty(cat) && titleText != null) cat = titleText.text ?? "";
                        if (string.IsNullOrEmpty(cat))
                            cat = VPBTranslation.T("gallery.status.path_empty_items", "items");
                        ShowTemporaryStatus(string.Format(
                            VPBTranslation.T("gallery.status.path_empty_for_category", "No {0} in this folder."),
                            cat), 2f);
                    }
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
                        txt.color = (!isActive && zeroCount)
                            ? ColorPathZeroCountText
                            : Color.white;

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

