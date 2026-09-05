using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void BuildCategoryTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (categories == null || categories.Count == 0) return;
            if (!categoriesCached) CacheCategoryCounts();

            ClearCategoryAccordionAnchor(isLeft);
            ClearCategoryAccordionButtons(isLeft);

            AppendHubTypeCategoryBrowseRows(container, trackedButtons, isLeft);

            var displayCategories = new List<Gallery.Category>(categories);
            var sortState = GetSortState("Category");
            GallerySortManager.Instance.SortCategories(displayCategories, sortState, categoryCounts);

            foreach (var cat in displayCategories)
            {
                var c = cat;
                bool isActive = (c.path == currentPath && c.extension == currentExtension)
                    && !IsHubTypeBrowseActive();
                // Keep selected row visible so accordion facets have a parent (current location).
                if (!isActive && !string.IsNullOrEmpty(categoryFilter) && cat.name.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

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
                if (isActive && CategoryHasFacetChildren(c.name) && trackedButtons != null && trackedButtons.Count > 0)
                {
                    GameObject anchor = trackedButtons[trackedButtons.Count - 1];
                    if (isLeft) _leftCategoryAccordionAnchor = anchor;
                    else _rightCategoryAccordionAnchor = anchor;
                }
            }
        }

        /// <summary>Per-category left icon for side-rail Category mode. Falls back to category-2. Null when setting off.</summary>
        private Sprite GetCategoryTabIcon(string categoryName)
        {
            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryShowCategoryIcons)
                return null;

            string path = null;
            if (!string.IsNullOrEmpty(categoryName))
            {
                if (string.Equals(categoryName, "Scenes", StringComparison.OrdinalIgnoreCase))
                    path = "chair-director";
                else if (string.Equals(categoryName, "SubScenes", StringComparison.OrdinalIgnoreCase))
                    path = "lamp-2";
                else if (string.Equals(categoryName, "Clothing", StringComparison.OrdinalIgnoreCase))
                    path = "shirt";
                else if (string.Equals(categoryName, "Hair", StringComparison.OrdinalIgnoreCase))
                    path = "scissors";
                else if (string.Equals(categoryName, "Pose", StringComparison.OrdinalIgnoreCase))
                    path = "yoga";
                else if (string.Equals(categoryName, "Appearance", StringComparison.OrdinalIgnoreCase))
                    path = "masks-theater";
                else if (string.Equals(categoryName, "Plugins", StringComparison.OrdinalIgnoreCase))
                    path = "plug-connected";
                else if (string.Equals(categoryName, "Skin", StringComparison.OrdinalIgnoreCase))
                    path = "body-scan";
                else if (string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryName, "ALL VAR", StringComparison.OrdinalIgnoreCase))
                    path = "apps";
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

        private void ApplyGalleryTitleText()
        {
            if (titleText == null) return;
            if (IsHubTypeBrowseActive())
            {
                titleText.text = "Hub: " + VpbLocalDatabase.DataPackHubCategoryDisplayName(_hubTypeBrowseToken);
                return;
            }
            titleText.text = currentCategoryTitle ?? "";
        }

        private static string FormatHubTypeBucketLabel(int matchCount)
        {
            return "HUB TYPE (" + matchCount.ToString() + ")";
        }

        private string BuildHubTypeRowTooltip(string displayName)
        {
            List<string> scope = ResolveHubItemScopeCategories(displayName);
            if (scope == null || scope.Count == 0)
            {
                return VPBTranslation.T(
                    "gallery.hubtype.row_tip_all",
                    "Browse every item inside packages listed as this Hub type. Item grid, not a package list.");
            }
            var sb = new StringBuilder(96);
            for (int i = 0; i < scope.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(scope[i]);
            }
            return string.Format(
                VPBTranslation.T(
                    "gallery.hubtype.row_tip_scoped",
                    "Browse {0} items from packages listed as this Hub type — including the ones whose file type says otherwise."),
                sb.ToString());
        }

        private static bool HubTypeRowPassesSideFilter(string displayName, string filterNow)
        {
            if (string.IsNullOrEmpty(filterNow)) return true;
            if (!string.IsNullOrEmpty(displayName)
                && displayName.IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return "HUB TYPE".IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string MapHubListingTypeToCategoryIconKey(string hubDisplayName)
        {
            if (string.IsNullOrEmpty(hubDisplayName)) return hubDisplayName;
            if (string.Equals(hubDisplayName, "Looks", StringComparison.OrdinalIgnoreCase))
                return "Appearance";
            if (string.Equals(hubDisplayName, "Hairstyles", StringComparison.OrdinalIgnoreCase))
                return "Hair";
            if (string.Equals(hubDisplayName, "Plugins + Scripts", StringComparison.OrdinalIgnoreCase))
                return "Plugins";
            if (string.Equals(hubDisplayName, "Poses", StringComparison.OrdinalIgnoreCase))
                return "Pose";
            if (string.Equals(hubDisplayName, "Demo + Lite", StringComparison.OrdinalIgnoreCase))
                return "Scenes";
            if (string.Equals(hubDisplayName, "Environments", StringComparison.OrdinalIgnoreCase))
                return "SubScenes";
            if (string.Equals(hubDisplayName, "Assets + Accessories", StringComparison.OrdinalIgnoreCase))
                return "CUA";
            if (string.Equals(hubDisplayName, "Textures", StringComparison.OrdinalIgnoreCase))
                return "Skin";
            if (string.Equals(hubDisplayName, "Mocap + Animation", StringComparison.OrdinalIgnoreCase))
                return "Animation";
            return hubDisplayName;
        }

        private static Sprite GetHubTypeHeaderIcon(bool expand)
        {
            return UI.LoadIconSprite(expand ? "chevron-down" : "chevron-right", UI.SideRailIconGlyphTint);
        }

        private static Color GetHubTypeHeaderIconBackdrop()
        {
            return new Color(0.10f, 0.32f, 0.42f, 1f);
        }

        private void RebuildCategorySideListsNow()
        {
            try
            {
                if (leftActiveContent == ContentType.Category)
                {
                    leftCategoryTabsLastSig = null;
                    GameObject catH = leftCategoryTabHolder;
                    if (catH != null && leftCategoryTabButtons != null)
                    {
                        UpdateTabs(ContentType.Category, catH, leftCategoryTabButtons, true);
                        FillCategoryAccordion(true);
                    }
                }
                if (rightActiveContent == ContentType.Category)
                {
                    rightCategoryTabsLastSig = null;
                    GameObject catH = rightCategoryTabHolder;
                    if (catH != null && rightCategoryTabButtons != null)
                    {
                        UpdateTabs(ContentType.Category, catH, rightCategoryTabButtons, false);
                        FillCategoryAccordion(false);
                    }
                }
            }
            catch { }
        }

        private bool TryGetCategoryByName(string name, out Gallery.Category cat)
        {
            cat = default(Gallery.Category);
            if (categories == null || string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < categories.Count; i++)
            {
                if (string.Equals(categories[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    cat = categories[i];
                    return !string.IsNullOrEmpty(cat.name);
                }
            }
            return false;
        }

        private List<string> ResolveHubItemScopeCategories(string displayName)
        {
            string[] wanted = GalleryHubTypeItemScope.CategoriesFor(displayName);
            if (wanted == null || wanted.Length == 0) return null;
            List<string> resolved = null;
            for (int i = 0; i < wanted.Length; i++)
            {
                Gallery.Category c;
                if (!TryGetCategoryByName(wanted[i], out c)) continue;
                if (resolved == null) resolved = new List<string>(wanted.Length);
                resolved.Add(c.name);
            }
            return resolved;
        }

        private void ApplyHubItemScopePathsToCurrentPaths()
        {
            List<string> scope = _hubItemScopeCategories;
            if (scope == null || scope.Count == 0) return;
            var merged = new List<string>(8);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < scope.Count; i++)
            {
                Gallery.Category c;
                if (!TryGetCategoryByName(scope[i], out c)) continue;
                if (c.paths != null && c.paths.Count > 0)
                {
                    for (int p = 0; p < c.paths.Count; p++)
                    {
                        string pref = c.paths[p];
                        if (string.IsNullOrEmpty(pref)) continue;
                        if (seen.Add(pref)) merged.Add(pref);
                    }
                }
                else if (!string.IsNullOrEmpty(c.path) && seen.Add(c.path))
                    merged.Add(c.path);
            }
            if (merged.Count > 0) currentPaths = merged;
        }

        private void EnterHubTypeBrowse(string displayName)
        {
            string token = VpbLocalDatabase.DataPackHubCategorySearchToken(displayName);
            if (string.IsNullOrEmpty(token)) return;
            Gallery.Category host;
            if (!TryGetCategoryByName(Gallery.EverythingCategoryName, out host))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.hubtype.need_everything", "Hub type browse needs the EVERYTHING category."),
                    2.2f);
                return;
            }
            // Captured before Show(), which overwrites the current view.
            if (!IsHubTypeBrowseActive())
            {
                _hubBrowseReturnTitle = currentCategoryTitle ?? "";
                _hubBrowseReturnExtension = currentExtension ?? "";
                _hubBrowseReturnPath = currentPath ?? "";
            }
            string canonical = VpbLocalDatabase.DataPackHubCategoryDisplayName(token);
            _categoryShowHubTypeBucket = true;
            _pendingHubTypeBrowseToken = token;
            _pendingHubItemScopeCategories = ResolveHubItemScopeCategories(canonical);
            if (LogGalleryCategoryTypeSwitchTiming)
                BeginGalleryCategoryTypeNavigationTiming("Hub: " + canonical);
            Show(host.name, host.extension, host.path);
            RebuildCategorySideListsNow();
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T(
                        "gallery.hubtype.browse_status",
                        "Hub: {0} — items from packages listed as {0}."),
                    canonical),
                2.4f);
        }

        private void LeaveHubTypeBrowse()
        {
            if (string.IsNullOrEmpty(_hubTypeBrowseToken)) return;
            Gallery.Category back;
            if (string.IsNullOrEmpty(_hubBrowseReturnTitle)
                || !TryGetCategoryByName(_hubBrowseReturnTitle, out back))
            {
                if (!TryGetCategoryByName("ALL VAR", out back)) return;
                _pendingHubTypeBrowseToken = "";
                _pendingHubItemScopeCategories = null;
                Show(back.name, back.extension, back.path);
                RebuildCategorySideListsNow();
                return;
            }
            _pendingHubTypeBrowseToken = "";
            _pendingHubItemScopeCategories = null;
            Show(
                back.name,
                string.IsNullOrEmpty(_hubBrowseReturnExtension) ? back.extension : _hubBrowseReturnExtension,
                string.IsNullOrEmpty(_hubBrowseReturnPath) ? back.path : _hubBrowseReturnPath);
            RebuildCategorySideListsNow();
        }

        private void EnsureHubTypeBrowseSearchChip(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            if (!HasTitleSearchChips())
                HydrateTitleSearchChipsFromCurrentFilter();
            if (HasTitleSearchPackChip(TitleSearchChipKind.PackHubCat, token)
                && !HasOtherHubCatChip(token))
                return;
            if (_titleSearchChips != null)
            {
                for (int i = _titleSearchChips.Count - 1; i >= 0; i--)
                {
                    if (_titleSearchChips[i].Kind == TitleSearchChipKind.PackHubCat)
                        _titleSearchChips.RemoveAt(i);
                }
            }
            GalleryTitleSearchChipUtil.TryAdd(
                _titleSearchChips,
                TitleSearchChipKind.PackHubCat,
                TitleSearchChipPolarity.Include,
                token,
                0,
                true);
            string serialized = GalleryTitleSearchChipUtil.Serialize(_titleSearchChips);
            try { AssignNameFilterState(serialized); } catch { }
            try { SetTitleSearchDraftText("", null); } catch { }
        }

        private bool HasOtherHubCatChip(string keepToken)
        {
            if (_titleSearchChips == null) return false;
            for (int i = 0; i < _titleSearchChips.Count; i++)
            {
                TitleSearchChip c = _titleSearchChips[i];
                if (c.Kind != TitleSearchChipKind.PackHubCat) continue;
                if (!string.Equals(c.Value, keepToken, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void OpenCategoryHubTypeFacet()
        {
            _categoryShowHubTypeBucket = true;
            ToggleSideFromRailButton(ContentType.Category, true, false);
            RebuildCategorySideListsNow();
        }

        private bool EnsureHubCatItemFacetRows()
        {
            if (!LookFacetHubModeAvailable()) return false;
            if (!VpbLocalDatabase.DataPackIndexReady) return false;
            string sig = VpbDataPackService.StatusRevision.ToString()
                + "|" + categorySideTabDataRevision.ToString();
            if (string.Equals(_hubCatItemFacetSig, sig, StringComparison.Ordinal)
                && _hubCatItemFacetRows != null)
                return _hubCatItemFacetRows.Count > 0;
            _hubCatItemFacetRows.Clear();
            if (!VpbLocalDatabase.TryCollectHubCategoryItemFacetRows(
                    _hubCatItemFacetRows, ResolveHubItemScopeCategories))
                return false;
            _hubCatItemFacetSig = sig;
            return _hubCatItemFacetRows.Count > 0;
        }

        private void AppendHubTypeCategoryBrowseRows(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;
            if (!string.IsNullOrEmpty(_hubTypeBrowseToken) && !IsHubTypeBrowseActive())
            {
                _hubTypeBrowseToken = "";
                _hubItemScopeCategories = null;
            }
            if (!LookFacetHubModeAvailable()) return;

            string filterNow = categoryFilter ?? "";
            bool filterOn = !string.IsNullOrEmpty(filterNow);
            bool browsingHub = IsHubTypeBrowseActive();
            bool expand = _categoryShowHubTypeBucket || filterOn;
            Color headerCol = browsingHub ? ColorHubType : ColorInactiveRow;
            Sprite headerIcon = GetHubTypeHeaderIcon(expand);
            Color hubIconBg = GetHubTypeHeaderIconBackdrop();
            string headerTip = VPBTranslation.T(
                "gallery.hubtype.bucket_tip",
                "HUB TYPE — what the creator uploaded it as, not the file type. Expand and click Looks to browse looks as items, even the ones shipped as scenes.");

            if (!expand)
            {
                string collapsedLabel = browsingHub
                    ? "Hub: " + VpbLocalDatabase.DataPackHubCategoryDisplayName(_hubTypeBrowseToken)
                    : VPBTranslation.T("gallery.hubtype.bucket_idle", "HUB TYPE");
                CreateTabButton(container.transform, collapsedLabel, headerCol, browsingHub, () =>
                {
                    _categoryShowHubTypeBucket = true;
                    RebuildCategorySideListsNow();
                }, trackedButtons, null, headerTip,
                    null, TextAnchor.MiddleLeft, 0f, 0f, headerIcon, hubIconBg);
                return;
            }

            if (!EnsureHubCatItemFacetRows()) return;
            List<CreatorCacheEntry> src = _hubCatItemFacetRows;
            if (src == null || src.Count == 0) return;

            int matchCount = 0;
            if (filterOn)
            {
                for (int i = 0; i < src.Count; i++)
                {
                    string n = src[i].Name;
                    if (string.IsNullOrEmpty(n)) continue;
                    if (!HubTypeRowPassesSideFilter(n, filterNow)) continue;
                    matchCount++;
                }
                if (matchCount == 0) return;
            }
            else
                matchCount = src.Count;

            string headerLabel = FormatHubTypeBucketLabel(matchCount);

            CreateTabButton(container.transform, headerLabel, headerCol, browsingHub, () =>
            {
                _categoryShowHubTypeBucket = false;
                RebuildCategorySideListsNow();
            }, trackedButtons, null, headerTip,
                null, TextAnchor.MiddleLeft, 0f, 0f, headerIcon, hubIconBg);

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float inset = GalleryUiDesignTokens.SideTabAccordionIndentRef * s;

            for (int i = 0; i < src.Count; i++)
            {
                CreatorCacheEntry e = src[i];
                if (string.IsNullOrEmpty(e.Name)) continue;
                if (filterOn && !HubTypeRowPassesSideFilter(e.Name, filterNow))
                    continue;
                string tok = VpbLocalDatabase.DataPackHubCategorySearchToken(e.Name);
                bool rowOn = browsingHub
                    && string.Equals(tok, _hubTypeBrowseToken, StringComparison.OrdinalIgnoreCase);
                string label = e.Name + " (" + e.Count + ")";
                string nameSnap = e.Name;
                string iconKey = MapHubListingTypeToCategoryIconKey(e.Name);
                Sprite rowIcon = GetCategoryTabIcon(iconKey);
                Color? rowIconBg = rowIcon != null ? GetCategoryTabIconBackdrop(iconKey) : (Color?)null;
                CreateTabButton(
                    container.transform,
                    label,
                    rowOn ? ColorHubType : GalleryUiColorTokens.HubTypeRow,
                    rowOn,
                    // The selected row is the only way back out, so it toggles rather than re-entering.
                    rowOn
                        ? (UnityAction)(() => { LeaveHubTypeBrowse(); })
                        : (UnityAction)(() => { EnterHubTypeBrowse(nameSnap); }),
                    trackedButtons,
                    null,
                    rowOn
                        ? VPBTranslation.T(
                            "gallery.hubtype.row_tip_on",
                            "Browsing this Hub type. Click again to leave and go back where you were.")
                        : BuildHubTypeRowTooltip(nameSnap),
                    null,
                    TextAnchor.MiddleLeft,
                    inset,
                    0f,
                    rowIcon,
                    rowIconBg);
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


