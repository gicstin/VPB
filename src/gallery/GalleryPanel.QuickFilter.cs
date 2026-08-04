using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Coroutine _quickFiltersConfigSaveCo;

        private static readonly string[] QuickFilterSideTabSortContexts =
        {
            "Category", "Creator", "Tags", "SceneSource", "UserTags", "UserTagsApplied", "Path"
        };

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
            entry.CategoryTitle = currentCategoryTitle;
            PopulateQuickFilterEntryFromCategoryFilterState(entry, CaptureCurrentFilterState());
            CaptureQuickFilterSideTabState(entry);
            
            return entry;
        }

        public void ApplyQuickFilterState(QuickFilterEntry entry)
        {
            ApplyQuickFilterState(entry, true);
        }

        public void ApplyQuickFilterState(QuickFilterEntry entry, bool announce)
        {
            ApplyQuickFilterState(entry, announce, quietUi: false);
        }

        /// <param name="quietUi">
        /// Background randomize: mutate category + filter fields and refresh lists without
        /// title/side-tab/layout thrash. Caller must bookend with quiet gallery refresh.
        /// </param>
        public void ApplyQuickFilterState(QuickFilterEntry entry, bool announce, bool quietUi)
        {
            if (entry == null) return;

            // Merged multi-random: apply first leaf for browse; dice walks all members.
            if (entry.IsMerged && entry.MergeMembers != null && entry.MergeMembers.Count > 0)
            {
                QuickFilterEntry first = entry.MergeMembers[0];
                if (first != null)
                {
                    ApplyQuickFilterState(first, false, quietUi);
                    if (announce)
                    {
                        ShowTemporaryStatus(string.Format(
                            VPBTranslation.T("quickfilters.merge_apply_hint", "Merged '{0}' — showing first filter. Dice loads all {1}."),
                            entry.Name ?? "",
                            entry.MergeMembers.Count));
                    }
                    return;
                }
            }

            // 1. Restore Category
            if (!string.IsNullOrEmpty(entry.CategoryPath))
            {
                Gallery.Category? cat = null;
                if (categories != null)
                {
                    for (int i = 0; i < categories.Count; i++)
                    {
                        var c = categories[i];
                        if (c.path != entry.CategoryPath) continue;
                        if (!string.IsNullOrEmpty(entry.CategoryTitle) && c.name != entry.CategoryTitle) continue;
                        cat = c;
                        break;
                    }
                }

                if (cat.HasValue && !string.IsNullOrEmpty(cat.Value.name))
                {
                    var v = cat.Value;
                    currentPath = v.path;
                    currentPaths = v.paths;
                    currentExtension = v.extension;
                    currentCategoryTitle = v.name;
                    if (!quietUi && titleText != null) titleText.text = v.name;
                }
            }

            // 2. Restore full filter state (scene/appearance local-only, untagged, subfilters, etc.)
            ApplyCategoryFilterState(CategoryFilterStateFromQuickFilterEntry(entry), restoreUserTagFilter: true, quietUi: quietUi);
            try { ReconcileAutoGenderForCurrentTarget(); } catch { }

            // 3. Restore side-tab panels and their list configurations (skip when quiet — UI only)
            if (!quietUi && entry.HasSideTabState)
                ApplyQuickFilterSideTabState(entry);

            // 4. Refresh (license filter hydrates package meta first)
            if (!quietUi)
            {
                UpdateLayout();
                UpdateTabs();
            }
            if (HasLicenseFilter())
            {
                CancelLicenseFilterHydrate();
                _licenseFilterHydrateCo = StartCoroutine(LicenseFilterHydrateThenRefreshCo());
            }
            else
            {
                RefreshFiles();
            }
            if (!quietUi)
            {
                try { SyncSidePaneTopSortButtonVisuals(); } catch { }
                try { SyncSceneSourceSortButtonHighlights(); } catch { }
            }
            
            if (announce)
                ShowTemporaryStatus("Quick Filter Applied: " + entry.Name);
        }

        private void CaptureQuickFilterSideTabState(QuickFilterEntry entry)
        {
            if (entry == null) return;

            entry.HasSideTabState = true;
            entry.LeftActiveContent = ContentTypeToPresetInt(NormalizePersistableSideTabContent(leftActiveContent));
            entry.RightActiveContent = ContentTypeToPresetInt(NormalizePersistableSideTabContent(rightActiveContent));
            entry.CategorySideFilter = categoryFilter ?? "";
            entry.CreatorSideFilter = creatorFilter ?? "";
            entry.UserTagSideFilter = userTagFilter ?? "";
            entry.PathSideFilter = pathFilter ?? "";
            entry.HistoryTabFilter = historyTabFilter ?? "";
            entry.TagSubPaneFilter = tagFilter ?? "";
            entry.HistoryFilterMode = (int)galleryHistoryFilterMode;

            entry.SideTabSortStates.Clear();
            for (int i = 0; i < QuickFilterSideTabSortContexts.Length; i++)
            {
                string ctx = QuickFilterSideTabSortContexts[i];
                SortState st = GetSortState(ctx);
                if (st == null) continue;
                entry.SideTabSortStates.Add(new QuickFilterSideTabSortEntry
                {
                    Context = ctx,
                    SortState = st.Clone()
                });
            }
        }

        private void ApplyQuickFilterSideTabState(QuickFilterEntry entry)
        {
            if (entry == null) return;

            try
            {
                if (IsSettingsPanelOpen() || settingsListViewActive)
                    ExitInternalSettingsMode(true);
            }
            catch { }
            try
            {
                if (cleanupModeActive)
                    ExitCleanupModeForSidePanelNavigation();
            }
            catch { }

            categoryFilter = entry.CategorySideFilter ?? "";
            creatorFilter = entry.CreatorSideFilter ?? "";
            userTagFilter = entry.UserTagSideFilter ?? "";
            pathFilter = entry.PathSideFilter ?? "";
            historyTabFilter = entry.HistoryTabFilter ?? "";
            tagFilter = entry.TagSubPaneFilter ?? "";

            int histMode = entry.HistoryFilterMode;
            if (histMode >= 0 && histMode <= (int)GalleryHistoryFilterMode.Misc)
                galleryHistoryFilterMode = (GalleryHistoryFilterMode)histMode;

            if (entry.SideTabSortStates != null)
            {
                for (int i = 0; i < entry.SideTabSortStates.Count; i++)
                {
                    var row = entry.SideTabSortStates[i];
                    if (row == null || string.IsNullOrEmpty(row.Context) || row.SortState == null) continue;
                    SaveSortState(row.Context, row.SortState.Clone());
                }
            }

            ContentType? left = NormalizePersistableSideTabContent(PresetIntToContentType(entry.LeftActiveContent));
            ContentType? right = NormalizePersistableSideTabContent(PresetIntToContentType(entry.RightActiveContent));
            if (left.HasValue && right.HasValue && left.Value == right.Value)
                right = null;

            leftActiveContent = left;
            rightActiveContent = right;
            SyncActiveContentTypeFromSidePanels();

            bool hasHistorySide = leftActiveContent == ContentType.History || rightActiveContent == ContentType.History;
            if (hasHistorySide)
                try { ApplyHistoryBrowseTitle(); } catch { }
            else if (titleText != null)
                titleText.text = currentCategoryTitle;
        }

        private ContentType? NormalizePersistableSideTabContent(ContentType? type)
        {
            if (!IsPersistableSideTabContent(type)) return null;
            if (type == ContentType.Creator
                && VPBConfig.Instance != null
                && VPBConfig.Instance.GalleryHideCreatorSideButtons)
                return null;
            return type;
        }

        private static bool IsPersistableSideTabContent(ContentType? type)
        {
            if (!type.HasValue) return false;
            switch (type.Value)
            {
                case ContentType.Category:
                case ContentType.Creator:
                case ContentType.UserTags:
                case ContentType.Path:
                case ContentType.History:
                    return true;
                default:
                    return false;
            }
        }

        private static int ContentTypeToPresetInt(ContentType? type)
        {
            return type.HasValue ? (int)type.Value : -1;
        }

        private static ContentType? PresetIntToContentType(int value)
        {
            if (value < 0) return null;
            if (!Enum.IsDefined(typeof(ContentType), value)) return null;
            var ct = (ContentType)value;
            return IsPersistableSideTabContent(ct) ? (ContentType?)ct : null;
        }

        private static void PopulateQuickFilterEntryFromCategoryFilterState(QuickFilterEntry entry, CategoryFilterState state)
        {
            if (entry == null || state == null) return;

            entry.SearchText = state.NameFilter ?? "";
            entry.Creator = state.Creator ?? "";
            entry.Tags = state.Tags != null ? new List<string>(state.Tags) : new List<string>();
            entry.UserTags = state.UserTags != null ? new List<string>(state.UserTags) : new List<string>();
            entry.ExcludedUserTags = state.ExcludedUserTags != null ? new List<string>(state.ExcludedUserTags) : new List<string>();
            entry.UserTagAvailFilterMode = state.UserTagAvailFilterMode;
            entry.UserTagInheritVarToChildren = state.UserTagInheritVarToChildren;
            entry.SceneSourceFilter = state.SceneSourceFilter ?? "";
            entry.AppearanceSourceFilter = state.AppearanceSourceFilter ?? "";
            entry.PackagePathFilter = state.PackagePathFilter ?? "";
            entry.ClothingSubfilter = state.ClothingSubfilter;
            entry.HairSubfilter = state.HairSubfilter;
            entry.AppearanceSubfilter = state.AppearanceSubfilter;
            entry.PosePeopleFilter = state.PosePeopleFilter;
            entry.SortState = state.FileSortState != null ? state.FileSortState.Clone() : null;
            entry.BrowseHiddenMode = state.BrowseHiddenMode;
            entry.BrowseAlwaysLoadedMode = state.BrowseAlwaysLoadedMode;
            entry.BrowseOldVersionsMode = state.BrowseOldVersionsMode;
            entry.BrowseLoadedMode = state.BrowseLoadedMode;
            entry.BrowseUnusedMode = state.BrowseUnusedMode;
            entry.LicenseFilter = state.LicenseFilter ?? "";
        }

        private static CategoryFilterState CategoryFilterStateFromQuickFilterEntry(QuickFilterEntry entry)
        {
            var state = new CategoryFilterState();
            if (entry == null) return state;

            state.NameFilter = entry.SearchText ?? "";
            state.Creator = entry.Creator ?? "";
            state.Tags = entry.Tags != null ? new List<string>(entry.Tags) : new List<string>();
            state.UserTags = entry.UserTags != null ? new List<string>(entry.UserTags) : new List<string>();
            state.ExcludedUserTags = entry.ExcludedUserTags != null ? new List<string>(entry.ExcludedUserTags) : new List<string>();
            state.UserTagAvailFilterMode = entry.UserTagAvailFilterMode;
            state.UserTagInheritVarToChildren = entry.UserTagInheritVarToChildren;
            state.SceneSourceFilter = entry.SceneSourceFilter ?? "";
            state.AppearanceSourceFilter = entry.AppearanceSourceFilter ?? "";
            state.PackagePathFilter = entry.PackagePathFilter ?? "";
            state.ClothingSubfilter = entry.ClothingSubfilter;
            state.HairSubfilter = entry.HairSubfilter;
            state.AppearanceSubfilter = entry.AppearanceSubfilter;
            state.PosePeopleFilter = entry.PosePeopleFilter;
            if (entry.SortState != null) state.FileSortState = entry.SortState.Clone();
            state.BrowseHiddenMode = entry.BrowseHiddenMode;
            state.BrowseAlwaysLoadedMode = entry.BrowseAlwaysLoadedMode;
            state.BrowseOldVersionsMode = entry.BrowseOldVersionsMode;
            state.BrowseLoadedMode = entry.BrowseLoadedMode;
            state.BrowseUnusedMode = entry.BrowseUnusedMode;
            state.LicenseFilter = entry.LicenseFilter ?? "";
            return state;
        }

        public void ToggleQuickFilters()
        {
            if (quickFiltersUI == null) return;
            
            bool visible = !quickFiltersUI.IsVisible;
            quickFiltersUI.SetVisible(visible);
            
            SyncQuickFilterToggleState();
        }

        /// <summary>ALT+F — open/close floating filter-presets window (detach if needed; hide keeps float).</summary>
        public void ToggleFloatingQuickFilters()
        {
            if (quickFiltersUI == null) return;
            if (quickFiltersUI.IsVisible && quickFiltersUI.IsDetached)
            {
                quickFiltersUI.SetVisible(false);
                SyncQuickFilterToggleState();
                return;
            }
            quickFiltersUI.EnsureDetachedAndVisible();
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

        /// <summary>Debounced VPBConfig.Save after filter-presets float geometry / detach changes.</summary>
        internal void ScheduleQuickFiltersConfigSave()
        {
            if (!isActiveAndEnabled) return;
            try
            {
                if (_quickFiltersConfigSaveCo != null)
                    StopCoroutine(_quickFiltersConfigSaveCo);
            }
            catch { }
            try { _quickFiltersConfigSaveCo = StartCoroutine(QuickFiltersConfigSaveCo()); }
            catch { _quickFiltersConfigSaveCo = null; }
        }

        private IEnumerator QuickFiltersConfigSaveCo()
        {
            yield return new WaitForSecondsRealtime(0.35f);
            _quickFiltersConfigSaveCo = null;
            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.Save(false);
            }
            catch { }
        }
    }
}
