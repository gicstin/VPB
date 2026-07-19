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
            s.NameFilter = nameFilter ?? "";
            s.Creator = currentCreator ?? "";
            s.Tags = new List<string>(activeTags);
            s.UserTags = new List<string>(activeUserTags);
            s.ExcludedUserTags = new List<string>(excludedUserTags);
            s.UserTagAvailFilterMode = (int)_userTagAvailMode;
            s.UserTagInheritVarToChildren = _userTagInheritVarToChildren ? 1 : 0;
            s.SceneSourceFilter = currentSceneSourceFilter ?? "";
            s.AppearanceSourceFilter = currentAppearanceSourceFilter ?? "";
            s.PackagePathFilter = currentPackagePathFilter ?? "";
            s.ClothingSubfilter = (int)clothingSubfilter;
            s.HairSubfilter = (int)hairSubfilter;
            s.AppearanceSubfilter = (int)appearanceSubfilter;
            s.PosePeopleFilter = (int)posePeopleFilter;
            var sort = GetSortState("Files");
            if (sort != null) s.FileSortState = sort.Clone();
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
                ApplyCategoryFilterState(state, restoreUserTagFilter: false);
                return;
            }

            string stateJson;
            if (VpbLocalDatabase.TryLoadCategoryFilterState(PanelId, key, out stateJson))
            {
                state = CategoryFilterState.FromJson(stateJson);
                if (state != null)
                {
                    _categoryFilterStates[key] = state;
                    ApplyCategoryFilterState(state, restoreUserTagFilter: false);
                    return;
                }
            }

            ClearFiltersForNewCategory();
        }

        /// <param name="restoreUserTagFilter">
        /// When false (auto-persisted per-category state, e.g. after restart), the user-tag availability
        /// filter (FilterByTags / FilterUntagged + selected tags) is NOT restored — it resets to the default
        /// non-filtering selection so re-entering a category never silently hides scenes by tag (issue #64).
        /// Explicit Quick Filter presets pass true so a deliberately saved tag filter is honored.
        /// </param>
        private void ApplyCategoryFilterState(CategoryFilterState state, bool restoreUserTagFilter = true)
        {
            // Set search fields directly — RefreshFiles (called after Show returns) will build
            // currentFilteredFiles using these terms. topSearchBaseFiles must be null so that
            // the first SetNameFilter call after RefreshFiles captures the correct unfiltered base.
            topSearchBaseFiles = null;
            string restoredSearch = state.NameFilter ?? "";
            AssignNameFilterState(restoredSearch);
            // WithoutNotify: assigning .text fires SetNameFilter and can schedule a second SQL refresh.
            try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, restoredSearch, _titleBarSearchOnValueChanged); } catch { }

            currentCreator = state.Creator ?? "";
            _currentCreatorSetSrc = null;
            try { UpdateTitleCreatorButtonVisual(); } catch { }

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
                if (utfm < 0 || utfm > (int)UserTagAvailMode.FilterUntagged) utfm = utfm != 0 ? 1 : 0;
                _userTagAvailMode = (UserTagAvailMode)utfm;
            }
            else
            {
                _userTagAvailMode = ResolveDefaultUserTagAvailMode();
            }
            if (_userTagAvailMode != UserTagAvailMode.FilterUntagged)
                try { ClearUntaggedTaggedPinKeys(); } catch { }
            _userTagInheritVarToChildren = state.UserTagInheritVarToChildren != 0;

            currentSceneSourceFilter = state.SceneSourceFilter ?? "";
            currentAppearanceSourceFilter = state.AppearanceSourceFilter ?? "";
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
                try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, state.FileSortState); } catch { }
                try { SyncRatingSortToggleState(); } catch { }
            }

            try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
            SyncBrowseFilterChipChrome();
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
            _userTagAvailMode = ResolveDefaultUserTagAvailMode();
            try { ClearUntaggedTaggedPinKeys(); } catch { }
            _userTagInheritVarToChildren = false;
            currentSceneSourceFilter = "";
            currentAppearanceSourceFilter = "";
            currentPackagePathFilter = "";
            clothingSubfilter = 0;
            hairSubfilter = 0;
            _clothingGenderUserOverride = false;
            _hairGenderUserOverride = false;
            appearanceSubfilter = 0;
            posePeopleFilter = PosePeopleFilter.All;
            SyncBrowseFilterChipChrome();
        }
    }
}
