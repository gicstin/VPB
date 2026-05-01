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
            s.SceneSourceFilter = currentSceneSourceFilter ?? "";
            s.AppearanceSourceFilter = currentAppearanceSourceFilter ?? "";
            s.PackagePathFilter = currentPackagePathFilter ?? "";
            s.ClothingSubfilter = (int)clothingSubfilter;
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
            string key = MakeCategoryFilterKey(categoryTitle, path);

            CategoryFilterState state = null;

            if (_categoryFilterStates.TryGetValue(key, out state) && state != null)
            {
                ApplyCategoryFilterState(state);
                return;
            }

            string stateJson;
            if (VpbLocalDatabase.TryLoadCategoryFilterState(PanelId, key, out stateJson))
            {
                state = CategoryFilterState.FromJson(stateJson);
                if (state != null)
                {
                    _categoryFilterStates[key] = state;
                    ApplyCategoryFilterState(state);
                    return;
                }
            }

            ClearFiltersForNewCategory();
        }

        private void ApplyCategoryFilterState(CategoryFilterState state)
        {
            // Set search fields directly — RefreshFiles (called after Show returns) will build
            // currentFilteredFiles using these terms. topSearchBaseFiles must be null so that
            // the first SetNameFilter call after RefreshFiles captures the correct unfiltered base.
            topSearchBaseFiles = null;
            string restoredSearch = state.NameFilter ?? "";
            nameFilter = restoredSearch;
            nameFilterLower = restoredSearch.ToLowerInvariant();
            nameFilterTerms = SplitSearchTerms(restoredSearch);
            if (titleSearchInput != null) titleSearchInput.text = restoredSearch;

            currentCreator = state.Creator ?? "";

            activeTags.Clear();
            if (state.Tags != null)
                foreach (var t in state.Tags) activeTags.Add(t);

            currentSceneSourceFilter = state.SceneSourceFilter ?? "";
            currentAppearanceSourceFilter = state.AppearanceSourceFilter ?? "";
            currentPackagePathFilter = state.PackagePathFilter ?? "";
            clothingSubfilter = (ClothingSubfilter)state.ClothingSubfilter;
            appearanceSubfilter = (AppearanceSubfilter)state.AppearanceSubfilter;
            posePeopleFilter = (PosePeopleFilter)state.PosePeopleFilter;

            if (state.FileSortState != null)
            {
                SaveSortState("Files", state.FileSortState);
                try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, state.FileSortState); } catch { }
                try { SyncRatingSortToggleState(); } catch { }
            }
        }

        private void ClearFiltersForNewCategory()
        {
            nameFilter = "";
            nameFilterLower = "";
            nameFilterTerms = new string[0];
            if (titleSearchInput != null) titleSearchInput.text = "";

            // Category navigation reset: creator selection is a filter and should not silently carry
            // into unrelated categories (causes side-tab counts like ALL VAR to drop to 0).
            currentCreator = "";

            activeTags.Clear();
            currentSceneSourceFilter = "";
            currentAppearanceSourceFilter = "";
            currentPackagePathFilter = "";
            clothingSubfilter = 0;
            appearanceSubfilter = 0;
            posePeopleFilter = PosePeopleFilter.All;
        }
    }
}
