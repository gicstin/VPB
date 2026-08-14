using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Coroutine _quickFiltersConfigSaveCo;
        /// <summary>One-shot: expand <see cref="currentPaths"/> from merge leaves after category restore, before refresh.</summary>
        private List<QuickFilterEntry> _pendingMergePathExpandLeaves;

        private const int QuickFilterSuggestedNameMaxLen = 48;
        private const int QuickFilterSuggestedNameMaxTokens = 4;

        private static readonly string[] QuickFilterSideTabSortContexts =
        {
            "Category", "Creator", "Tags", "SceneSource", "UserTags", "UserTagsApplied", "Path"
        };

        public QuickFilterEntry CaptureQuickFilterState()
        {
            return CaptureQuickFilterState(null);
        }

        /// <param name="preferredName">
        /// Optional display name (e.g. text typed in presets list search — Postel: honor mistaken name intent).
        /// Empty → suggest from live filter tokens.
        /// </param>
        public QuickFilterEntry CaptureQuickFilterState(string preferredName)
        {
            var entry = new QuickFilterEntry();

            entry.CategoryPath = currentPath;
            entry.CategoryTitle = currentCategoryTitle;
            PopulateQuickFilterEntryFromCategoryFilterState(entry, CaptureCurrentFilterState());
            entry.GlobalSourceFilter = QuickFilterEntry.ClampGlobalSourceFilter((int)currentGlobalSourceFilter);
            CaptureQuickFilterSideTabState(entry);

            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            var settings = QuickFilterSettings.Instance;
            if (settings != null && settings.Filters != null)
            {
                for (int i = 0; i < settings.Filters.Count; i++)
                {
                    QuickFilterEntry f = settings.Filters[i];
                    if (f != null && !string.IsNullOrEmpty(f.Name))
                        existingNames.Add(f.Name);
                }
            }

            string preferred = preferredName != null ? preferredName.Trim() : "";
            if (preferred.Length > 0)
            {
                if (preferred.Length > QuickFilterSuggestedNameMaxLen)
                    preferred = preferred.Substring(0, QuickFilterSuggestedNameMaxLen);
                entry.Name = EnsureUniqueQuickFilterName(preferred, existingNames);
            }
            else
                entry.Name = BuildSuggestedQuickFilterName(entry, existingNames);

            return entry;
        }

        /// <summary>
        /// Cold-path name for new presets: search / creator / tags / category.
        /// Uniquify with " 2", " 3"…; empty filters fall back to Preset#N.
        /// </summary>
        private static string BuildSuggestedQuickFilterName(QuickFilterEntry entry, HashSet<string> existingNames)
        {
            if (existingNames == null)
                existingNames = new HashSet<string>(StringComparer.Ordinal);

            var tokens = new List<string>(QuickFilterSuggestedNameMaxTokens);
            AppendQuickFilterNameToken(tokens, entry != null ? entry.SearchText : null);
            AppendQuickFilterNameToken(tokens, entry != null ? entry.Creator : null);
            AppendQuickFilterNameListTokens(tokens, entry != null ? entry.Tags : null);
            AppendQuickFilterNameListTokens(tokens, entry != null ? entry.UserTags : null);
            if (tokens.Count == 0)
                AppendQuickFilterNameToken(tokens, entry != null ? entry.CategoryTitle : null);

            string baseName;
            if (tokens.Count == 0)
                baseName = AllocatePresetNumberName(existingNames);
            else
                baseName = JoinQuickFilterNameTokens(tokens, QuickFilterSuggestedNameMaxLen);

            return EnsureUniqueQuickFilterName(baseName, existingNames);
        }

        private static void AppendQuickFilterNameToken(List<string> tokens, string raw)
        {
            if (tokens == null || tokens.Count >= QuickFilterSuggestedNameMaxTokens) return;
            if (string.IsNullOrEmpty(raw)) return;
            string t = raw.Trim();
            if (t.Length == 0) return;
            // Collapse internal whitespace (cold path — rare).
            if (t.IndexOf('\n') >= 0 || t.IndexOf('\t') >= 0)
                t = t.Replace('\n', ' ').Replace('\t', ' ');
            while (t.IndexOf("  ", StringComparison.Ordinal) >= 0)
                t = t.Replace("  ", " ");
            if (t.Length == 0) return;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], t, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            tokens.Add(t);
        }

        private static void AppendQuickFilterNameListTokens(List<string> tokens, List<string> list)
        {
            if (tokens == null || list == null) return;
            for (int i = 0; i < list.Count && tokens.Count < QuickFilterSuggestedNameMaxTokens; i++)
                AppendQuickFilterNameToken(tokens, list[i]);
        }

        private static string JoinQuickFilterNameTokens(List<string> tokens, int maxLen)
        {
            if (tokens == null || tokens.Count == 0) return "";
            var sb = new StringBuilder(Mathf.Min(maxLen, 64));
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = tokens[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (sb.Length > 0)
                {
                    if (sb.Length + 3 + t.Length > maxLen) break;
                    sb.Append(" · ");
                }
                else if (t.Length > maxLen)
                {
                    sb.Append(t, 0, maxLen);
                    break;
                }
                int remain = maxLen - sb.Length;
                if (t.Length <= remain) sb.Append(t);
                else
                {
                    sb.Append(t, 0, remain);
                    break;
                }
            }
            return sb.ToString();
        }

        private static string AllocatePresetNumberName(HashSet<string> existingNames)
        {
            int nextNum = 1;
            string candidate;
            do { candidate = "Preset#" + nextNum++; }
            while (existingNames != null && existingNames.Contains(candidate));
            return candidate;
        }

        private static string EnsureUniqueQuickFilterName(string baseName, HashSet<string> existingNames)
        {
            if (string.IsNullOrEmpty(baseName))
                baseName = AllocatePresetNumberName(existingNames);
            if (existingNames == null || !existingNames.Contains(baseName))
                return baseName;

            // Preset#N already unique from Allocate — numeric suffix for token names.
            if (baseName.Length >= 7 && baseName.StartsWith("Preset#", StringComparison.Ordinal))
                return AllocatePresetNumberName(existingNames);

            int n = 2;
            string candidate;
            do { candidate = baseName + " " + n++; }
            while (existingNames.Contains(candidate) && n < 10000);
            return candidate;
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

            // Drop pending keystroke search refresh — otherwise debounce can fire mid-apply
            // and briefly widen the list (search restored then wiped by stale empty apply).
            try { CancelTitleSearchSqlDebounce(); } catch { }
            try { CancelTitleSearchInMemoryDebounce(); } catch { }

            // Merged: OR-combine all leaf filters for browse (not first-only).
            // Dice still expands leaves in FilterRandomizer — does not pass IsMerged here.
            if (entry.IsMerged && entry.MergeMembers != null && entry.MergeMembers.Count > 0)
            {
                var leaves = new List<QuickFilterEntry>(entry.MergeMembers.Count);
                QuickFilterEntry.CollectMergeLeaves(
                    entry, leaves, GalleryUiDesignTokens.QuickFiltersMergeMaxMembers);
                if (leaves.Count == 0) return;

                QuickFilterEntry combined = QuickFilterEntry.BuildCombinedBrowseEntry(leaves);
                if (combined == null) combined = leaves[0];
                if (combined == null) return;

                if (string.IsNullOrEmpty(combined.Name) && !string.IsNullOrEmpty(entry.Name))
                    combined.Name = entry.Name;

                // Expand multi-category paths inside nested apply (before its RefreshFiles).
                _pendingMergePathExpandLeaves = leaves;
                try
                {
                    ApplyQuickFilterState(combined, false, quietUi);
                }
                finally
                {
                    _pendingMergePathExpandLeaves = null;
                }

                if (announce && !quietUi)
                {
                    ShowTemporaryStatus(string.Format(
                        VPBTranslation.T("quickfilters.merge_apply_hint", "Merged '{0}' — all {1} filters (OR). Dice loads each in order."),
                        entry.Name ?? "",
                        leaves.Count));
                }
                if (!quietUi)
                    RememberAppliedQuickFilter(entry);
                return;
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

            // Merged browse: union leaf category folders before filter refresh.
            if (_pendingMergePathExpandLeaves != null)
            {
                try { ExpandCurrentPathsFromMergeLeaves(_pendingMergePathExpandLeaves); }
                catch { }
                _pendingMergePathExpandLeaves = null;
            }

            // 2. Restore full filter state (scene/appearance local-only, untagged, subfilters, etc.)
            ApplyCategoryFilterState(CategoryFilterStateFromQuickFilterEntry(entry), restoreUserTagFilter: true, quietUi: quietUi);
            // Preset Source All/Local/.var always applies (Independent: this category; Synced: shared live value).
            ApplyQuickFilterGlobalSourceFilter(entry, quietUi);
            try { ReconcileAutoGenderForCurrentTarget(); } catch { }

            // 3. Restore side-tab panels and their list configurations (skip when quiet — UI only)
            if (!quietUi && entry.HasSideTabState)
                ApplyQuickFilterSideTabState(entry);

            // 4. Refresh (license filter applied in SQLite via pkg.license)
            if (!quietUi)
            {
                UpdateLayout();
                UpdateTabs();
            }
            RefreshFiles();
            if (!quietUi)
            {
                try { SyncSidePaneTopSortButtonVisuals(); } catch { }
                try { SyncSceneSourceSortButtonHighlights(); } catch { }
            }
            
            if (announce)
            {
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("quickfilters.applied", "Filters applied: {0}"),
                    entry.Name ?? ""));
            }
            if (!quietUi)
                RememberAppliedQuickFilter(entry);
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

        /// <summary>
        /// After merged browse apply: union folder prefixes from every leaf category into
        /// <see cref="currentPaths"/> so multi-category merges show all members' items.
        /// Returns true when paths changed (caller should refresh).
        /// </summary>
        private bool ExpandCurrentPathsFromMergeLeaves(IList<QuickFilterEntry> leaves)
        {
            if (leaves == null || leaves.Count < 2 || categories == null) return false;

            // Defensive copy — category.paths may be shared with live currentPaths.
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (currentPaths != null)
            {
                for (int i = 0; i < currentPaths.Count; i++)
                {
                    string p = currentPaths[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (!seen.Add(p)) continue;
                    merged.Add(p);
                }
            }
            else if (!string.IsNullOrEmpty(currentPath))
            {
                seen.Add(currentPath);
                merged.Add(currentPath);
            }

            int before = merged.Count;
            for (int li = 0; li < leaves.Count; li++)
            {
                QuickFilterEntry leaf = leaves[li];
                if (leaf == null || string.IsNullOrEmpty(leaf.CategoryPath)) continue;

                Gallery.Category? cat = null;
                for (int i = 0; i < categories.Count; i++)
                {
                    var c = categories[i];
                    if (c.path != leaf.CategoryPath) continue;
                    if (!string.IsNullOrEmpty(leaf.CategoryTitle) && c.name != leaf.CategoryTitle) continue;
                    cat = c;
                    break;
                }
                if (!cat.HasValue) continue;

                List<string> paths = cat.Value.paths;
                if (paths != null && paths.Count > 0)
                {
                    for (int p = 0; p < paths.Count; p++)
                    {
                        string pref = paths[p];
                        if (string.IsNullOrEmpty(pref)) continue;
                        if (!seen.Add(pref)) continue;
                        merged.Add(pref);
                    }
                }
                else if (!string.IsNullOrEmpty(cat.Value.path))
                {
                    if (seen.Add(cat.Value.path))
                        merged.Add(cat.Value.path);
                }
            }

            if (merged.Count <= before) return false;
            currentPaths = merged;
            return true;
        }

        private void ApplyQuickFilterSideTabState(QuickFilterEntry entry)
        {
            if (entry == null) return;

            try
            {
                if (settingsListViewActive)
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
            if (state.HasSourceFilter)
                entry.GlobalSourceFilter = QuickFilterEntry.ClampGlobalSourceFilter(state.SourceFilter);
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
            state.SourceFilter = QuickFilterEntry.ClampGlobalSourceFilter(entry.GlobalSourceFilter);
            state.HasSourceFilter = true;
            return state;
        }

        /// <summary>
        /// Warm path: set title-bar Source from preset without RefreshFilesAndTabs
        /// (caller already refreshes). Clears Local linger when preset is All.
        /// </summary>
        private void ApplyQuickFilterGlobalSourceFilter(QuickFilterEntry entry, bool quietUi)
        {
            VPBConfig.GlobalSourceFilterValue desired = VPBConfig.GlobalSourceFilterValue.All;
            if (entry != null)
                desired = (VPBConfig.GlobalSourceFilterValue)QuickFilterEntry.ClampGlobalSourceFilter(entry.GlobalSourceFilter);

            if (currentGlobalSourceFilter != desired)
            {
                currentGlobalSourceFilter = desired;
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.GlobalSourceFilter = desired;
                    try { VPBConfig.Instance.Save(); } catch { }
                }
            }

            // Mirror ApplyGlobalSourceFilterValue: Local and creator filters are mutually exclusive.
            if (desired == VPBConfig.GlobalSourceFilterValue.Local && HasCreatorFilter())
            {
                ClearCreatorFilters();
                if (!quietUi)
                {
                    try { UpdateTitleCreatorButtonVisual(); } catch { }
                }
            }

            if (!quietUi)
            {
                try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
                try { SyncBrowseFilterChipChrome(); } catch { }
                if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
                {
                    try { RebuildGlobalSourceFilterMenuOptions(); } catch { }
                }
            }
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
