using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static string MakeCategoryFilterKey(string categoryTitle, string path)
        {
            return (categoryTitle ?? "") + "\u001E" + (path ?? "");
        }

        private CategoryFilterState CaptureCurrentFilterState()
        {
            var s = new CategoryFilterState();
            // Chips own the live filter when present — serialize them (heals nameFilter desync).
            // Else capture nameFilter; fall back to title-field draft if filter state empty.
            if (HasTitleSearchChips())
            {
                string fromChips = GalleryTitleSearchChipUtil.Serialize(_titleSearchChips) ?? "";
                s.NameFilter = fromChips;
                if (!string.Equals(nameFilter ?? "", fromChips, StringComparison.Ordinal))
                {
                    try { AssignNameFilterState(fromChips); } catch { }
                }
            }
            else
            {
                s.NameFilter = nameFilter ?? "";
                if (string.IsNullOrEmpty(s.NameFilter) && titleSearchInput != null)
                {
                    string draft = (titleSearchInput.text ?? "").Trim();
                    if (draft.Length > 0)
                        s.NameFilter = draft;
                }
            }
            s.Creator = currentCreator ?? "";
            s.Tags = new List<string>(activeTags);
            s.UserTags = new List<string>(activeUserTags);
            s.ExcludedUserTags = new List<string>(excludedUserTags);
            s.UserTagAvailFilterMode = (int)_userTagAvailMode;
            s.UserTagInheritVarToChildren = _userTagInheritVarToChildren ? 1 : 0;
            // Legacy per-category Local fields retired — Local lives on global Source only.
            s.SceneSourceFilter = "";
            s.AppearanceSourceFilter = "";
            s.PackagePathFilter = currentPackagePathFilter ?? "";
            s.ClothingSubfilter = (int)clothingSubfilter;
            s.HairSubfilter = (int)hairSubfilter;
            s.AppearanceSubfilter = (int)appearanceSubfilter;
            s.PosePeopleFilter = (int)posePeopleFilter;
            var sort = GetSortState("Files");
            if (sort != null) s.FileSortState = sort.Clone();
            s.BrowseHiddenMode = (int)_browseHiddenCycle;
            s.BrowseAlwaysLoadedMode = (int)_browseAlwaysLoadedCycle;
            s.BrowseOldVersionsMode = (int)_browseOldVersionsCycle;
            s.BrowseLoadedMode = (int)_browseLoadedMode;
            s.BrowseUnusedMode = (int)_browseUnusedCycle;
            s.LicenseFilter = currentLicenseFilter ?? "";
            return s;
        }

        private void SaveCurrentCategoryFilterState(string categoryTitle, string path)
        {
            if (!hasLoadedContent) return;
            string key = MakeCategoryFilterKey(categoryTitle, path);
            var state = CaptureCurrentFilterState();
            _categoryFilterStates[key] = state;

            string panelId = PanelId;
            string json = state.ToJson();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { VpbLocalDatabase.TrySaveCategoryFilterState(panelId, key, json); }
                catch { }
            });
        }

        private void RestoreCategoryFilterState(string categoryTitle, string path)
        {
            // Drop pending keystroke search refresh — otherwise it fires mid category load (double RefreshFiles).
            try { CancelTitleSearchSqlDebounce(); } catch { }
            try { CancelTitleSearchInMemoryDebounce(); } catch { }

            string key = MakeCategoryFilterKey(categoryTitle, path);

            CategoryFilterState state = null;

            if (_categoryFilterStates.TryGetValue(key, out state) && state != null)
            {
                ApplyCategoryFilterState(state, restoreUserTagFilter: true);
                NotifyCategoryFiltersRestored(categoryTitle);
                return;
            }

            string stateJson;
            if (TryLoadPersistedCategoryFilterState(key, out stateJson))
            {
                state = CategoryFilterState.FromJson(stateJson);
                if (state != null)
                {
                    _categoryFilterStates[key] = state;
                    ApplyCategoryFilterState(state, restoreUserTagFilter: true);
                    NotifyCategoryFiltersRestored(categoryTitle);
                    return;
                }
            }

            ClearFiltersForNewCategory();
        }

        /// <summary>Gulf of evaluation: category switch restored hidden filters — surface in status.</summary>
        private void NotifyCategoryFiltersRestored(string categoryTitle)
        {
            try
            {
                if (!HasActiveBrowseFilters()) return;
                string cat = string.IsNullOrEmpty(categoryTitle) ? "category" : categoryTitle;
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T(
                            "gallery.filter.restored_for_category",
                            "Restored filters for {0}. Clear all in chip bar."),
                        cat),
                    2.25f);
            }
            catch { }
        }

        /// <summary>Load filter JSON for this pane; fall back to primary slot so Close/recreate and old single-pane rows still restore.</summary>
        private bool TryLoadPersistedCategoryFilterState(string catKey, out string stateJson)
        {
            stateJson = null;
            string id = PanelId;
            if (VpbLocalDatabase.TryLoadCategoryFilterState(id, catKey, out stateJson) && !string.IsNullOrEmpty(stateJson))
                return true;
            if (!string.Equals(id, PrimaryPanelId, StringComparison.Ordinal)
                && VpbLocalDatabase.TryLoadCategoryFilterState(PrimaryPanelId, catKey, out stateJson)
                && !string.IsNullOrEmpty(stateJson))
                return true;
            return false;
        }

        /// <param name="restoreUserTagFilter">
        /// When true (default for category restore + Quick Filters), restore include/exclude user-tag
        /// filter sets and work mode. Filter chips make armed filters visible (issue #64: silent hide
        /// was from restoring FilterUntagged / FilterByTags without clear affordance).
        /// Pass false only when deliberately wiping user-tag filter while applying other state.
        /// </param>
        /// <param name="quietUi">
        /// When true, apply filter fields for list rebuild but skip chip/button/search chrome updates
        /// (background filter-randomize).
        /// </param>
        private void ApplyCategoryFilterState(CategoryFilterState state, bool restoreUserTagFilter = true, bool quietUi = false)
        {
            // Set search fields directly — RefreshFiles (called after Show returns) will build
            // currentFilteredFiles using these terms. topSearchBaseFiles must be null so that
            // the first SetNameFilter call after RefreshFiles captures the correct unfiltered base.
            topSearchBaseFiles = null;
            string restoredSearch = state.NameFilter ?? "";
            AssignNameFilterState(restoredSearch);
            if (!quietUi)
            {
                HydrateTitleSearchChipsFromCurrentFilter();
                // WithoutNotify: assigning .text fires SetNameFilter and can schedule a second SQL refresh.
                // Chip mode: field stays empty (draft); live mode: show restored string.
                string fieldText = HasTitleSearchChips() ? "" : restoredSearch;
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, fieldText, _titleBarSearchOnValueChanged); } catch { }
            }

            currentCreator = state.Creator ?? "";
            _currentCreatorSetSrc = null;
            if (!quietUi)
            {
                try { UpdateTitleCreatorButtonVisual(); } catch { }
            }

            activeTags.Clear();
            if (state.Tags != null)
                foreach (var t in state.Tags) activeTags.Add(t);

            activeUserTags.Clear();
            excludedUserTags.Clear();
            if (restoreUserTagFilter)
            {
                if (state.UserTags != null)
                    foreach (var t in state.UserTags)
                    {
                        string n = VpbLocalDatabase.NormalizeGalleryUserTagName(t);
                        if (!string.IsNullOrEmpty(n)) activeUserTags.Add(n);
                    }
                if (state.ExcludedUserTags != null)
                    foreach (var t in state.ExcludedUserTags)
                    {
                        string n = VpbLocalDatabase.NormalizeGalleryUserTagName(t);
                        if (!string.IsNullOrEmpty(n)) excludedUserTags.Add(n);
                    }
                int utfm = state.UserTagAvailFilterMode;
                if (utfm < 0 || utfm > (int)UserTagAvailMode.FilterTaggedOnly) utfm = utfm != 0 ? 1 : 0;
                _userTagAvailMode = (UserTagAvailMode)utfm;
                if (_userTagAvailMode == UserTagAvailMode.FilterUntagged
                    || _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly)
                    _userTagModeBeforeUntagged = UserTagAvailMode.FilterByTags;
            }
            else
            {
                _userTagAvailMode = ResolveDefaultUserTagAvailMode();
                if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                    _userTagModeBeforeUntagged = UserTagAvailMode.FilterByTags;
            }
            _userTagShowUnusedBucket = false;
            if (_userTagAvailMode != UserTagAvailMode.FilterUntagged
                && _userTagAvailMode != UserTagAvailMode.FilterTaggedOnly)
                try { ClearUntaggedTaggedPinKeys(); } catch { }
            _userTagInheritVarToChildren = state.UserTagInheritVarToChildren != 0;

            // Migrate legacy per-category Scene/Appearance Local → global Source Local.
            try
            {
                bool legacyLocal =
                    string.Equals(state.SceneSourceFilter, "local", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.AppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase);
                if (legacyLocal && currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.Local)
                {
                    currentGlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.Local;
                    if (VPBConfig.Instance != null)
                    {
                        VPBConfig.Instance.GlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.Local;
                        try { VPBConfig.Instance.Save(); } catch { }
                    }
                }
            }
            catch { }
            currentPackagePathFilter = state.PackagePathFilter ?? "";
            clothingSubfilter = (ClothingSubfilter)state.ClothingSubfilter;
            hairSubfilter = (HairSubfilter)state.HairSubfilter;
            _clothingGenderUserOverride = false;
            _hairGenderUserOverride = false;
            appearanceSubfilter = (AppearanceSubfilter)state.AppearanceSubfilter;
            posePeopleFilter = (PosePeopleFilter)state.PosePeopleFilter;

            if (state.FileSortState != null)
            {
                SaveSortState("Files", state.FileSortState);
                if (!quietUi)
                {
                    try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, state.FileSortState); } catch { }
                    try { SyncRatingSortToggleState(); } catch { }
                }
            }

            _browseHiddenCycle = ClampBrowseFilterCycle(state.BrowseHiddenMode);
            _browseAlwaysLoadedCycle = ClampBrowseFilterCycle(state.BrowseAlwaysLoadedMode);
            _browseOldVersionsCycle = ClampBrowseFilterCycle(state.BrowseOldVersionsMode);
            _browseLoadedMode = ClampBrowseLoadedMode(state.BrowseLoadedMode);
            _browseUnusedCycle = ClampBrowseFilterCycle(state.BrowseUnusedMode);
            currentLicenseFilter = state.LicenseFilter ?? "";
            try { SyncShowHiddenPackagesFromCycle(); } catch { }
            try { SyncHideOldVersionsFromCycle(); } catch { }
            try { MigrateLegacyExclusiveFileSortIfNeeded(); } catch { }
            if (!quietUi)
            {
                try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
                try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                SyncBrowseFilterChipChrome();
            }
        }

        private void ClearFiltersForNewCategory()
        {
            try { CancelTitleSearchSqlDebounce(); } catch { }
            try { CancelTitleSearchInMemoryDebounce(); } catch { }
            ClearNameFilterState();
            try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, "", _titleBarSearchOnValueChanged); } catch { }

            // Category navigation reset: creator selection is a filter and should not silently carry
            // into unrelated categories (causes side-tab counts like ALL VAR to drop to 0).
            currentCreator = "";
            _currentCreatorSetSrc = null;
            try { UpdateTitleCreatorButtonVisual(); } catch { }

            activeTags.Clear();
            activeUserTags.Clear();
            excludedUserTags.Clear();
            _userTagShowUnusedBucket = false;
            _userTagAvailMode = ResolveDefaultUserTagAvailMode();
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                _userTagModeBeforeUntagged = UserTagAvailMode.FilterByTags;
            try { ClearUntaggedTaggedPinKeys(); } catch { }
            _userTagInheritVarToChildren = false;
            currentPackagePathFilter = "";
            clothingSubfilter = 0;
            hairSubfilter = 0;
            _clothingGenderUserOverride = false;
            _hairGenderUserOverride = false;
            appearanceSubfilter = 0;
            posePeopleFilter = PosePeopleFilter.All;
            if (_browseAlwaysLoadedCycle != BrowseFilterCycle.Off)
            {
                BrowseFilterCycle prevAl = _browseAlwaysLoadedCycle;
                _browseAlwaysLoadedCycle = BrowseFilterCycle.Off;
                try { ApplyAlwaysLoadedCycleSortSideEffects(prevAl, BrowseFilterCycle.Off); } catch { }
            }
            else
            {
                _browseAlwaysLoadedCycle = BrowseFilterCycle.Off;
                _browseAlwaysLoadedSavedSort = null;
            }
            _browseHiddenCycle = BrowseFilterCycle.Off;
            _browseOldVersionsCycle = BrowseFilterCycle.Apply;
            try { SyncHideOldVersionsFromCycle(); } catch { }
            _browseLoadedMode = BrowseLoadedMode.Off;
            if (_browseUnusedCycle != BrowseFilterCycle.Off)
            {
                BrowseFilterCycle prevU = _browseUnusedCycle;
                _browseUnusedCycle = BrowseFilterCycle.Off;
                try { ApplyUnusedCycleSortSideEffects(prevU, BrowseFilterCycle.Off); } catch { }
            }
            else
            {
                _browseUnusedCycle = BrowseFilterCycle.Off;
                _browseUnusedSavedSort = null;
            }
            currentLicenseFilter = "";
            try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
            SyncBrowseFilterChipChrome();
        }

        private static BrowseFilterCycle ClampBrowseFilterCycle(int v)
        {
            if (v <= 0) return BrowseFilterCycle.Off;
            if (v == 1) return BrowseFilterCycle.Apply;
            return BrowseFilterCycle.Only;
        }

        private static BrowseLoadedMode ClampBrowseLoadedMode(int v)
        {
            if (v <= 0) return BrowseLoadedMode.Off;
            if (v == 1) return BrowseLoadedMode.LoadedOnly;
            return BrowseLoadedMode.UnloadedOnly;
        }
    }
}
