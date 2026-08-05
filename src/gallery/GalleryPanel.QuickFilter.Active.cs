using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Active / recent / dirty / update for filter presets (saved-view recognition).
    /// Warm path — click/apply only; no per-frame work.
    /// </summary>
    public partial class GalleryPanel
    {
        private const int QuickFilterRecentMax = 3;

        private QuickFilterEntry _activeQuickFilter;
        private readonly List<QuickFilterEntry> _recentQuickFilters = new List<QuickFilterEntry>(QuickFilterRecentMax);

        /// <summary>Preset last applied via browse (not dice-only restore).</summary>
        public QuickFilterEntry ActiveQuickFilter
        {
            get { return _activeQuickFilter; }
        }

        /// <summary>Session MRU applied presets (newest first), max <see cref="QuickFilterRecentMax"/>.</summary>
        public void CollectRecentQuickFilters(List<QuickFilterEntry> into)
        {
            if (into == null) return;
            into.Clear();
            for (int i = 0; i < _recentQuickFilters.Count; i++)
            {
                QuickFilterEntry e = _recentQuickFilters[i];
                if (e == null) continue;
                if (!IsQuickFilterStillInSettings(e)) continue;
                into.Add(e);
            }
        }

        internal void RememberAppliedQuickFilter(QuickFilterEntry entry)
        {
            if (entry == null) return;
            if (!IsQuickFilterStillInSettings(entry))
            {
                // Applied combined browse clone is not in settings — keep prior active.
                return;
            }
            _activeQuickFilter = entry;
            PushRecentQuickFilter(entry);
            try
            {
                if (quickFiltersUI != null && quickFiltersUI.IsVisible)
                    quickFiltersUI.Refresh();
            }
            catch { }
        }

        internal void ClearActiveQuickFilter()
        {
            _activeQuickFilter = null;
        }

        internal void NotifyQuickFilterRemoved(QuickFilterEntry entry)
        {
            if (entry == null) return;
            if (ReferenceEquals(_activeQuickFilter, entry))
                _activeQuickFilter = null;
            for (int i = _recentQuickFilters.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_recentQuickFilters[i], entry))
                    _recentQuickFilters.RemoveAt(i);
            }
        }

        /// <summary>True when live gallery filters differ from active preset snapshot.</summary>
        public bool IsActiveQuickFilterDirty()
        {
            QuickFilterEntry active = _activeQuickFilter;
            if (active == null || !IsQuickFilterStillInSettings(active)) return false;

            QuickFilterEntry live = CaptureQuickFilterStateForCompare();
            if (live == null) return true;

            QuickFilterEntry baseline = active;
            if (active.IsMerged)
            {
                var leaves = new List<QuickFilterEntry>(GalleryUiDesignTokens.QuickFiltersMergeMaxMembers);
                QuickFilterEntry.CollectMergeLeaves(
                    active, leaves, GalleryUiDesignTokens.QuickFiltersMergeMaxMembers);
                QuickFilterEntry combined = QuickFilterEntry.BuildCombinedBrowseEntry(leaves);
                if (combined != null) baseline = combined;
            }

            return !QuickFilterEntry.ContentSignaturesEqual(live, baseline);
        }

        /// <summary>
        /// Overwrite active preset from live gallery filters. Keeps Name/Id/Pinned/colors.
        /// Clears merge membership (Update = single snapshot).
        /// </summary>
        public bool UpdateActiveQuickFilterFromLive()
        {
            QuickFilterEntry active = _activeQuickFilter;
            if (active == null || !IsQuickFilterStillInSettings(active)) return false;

            QuickFilterEntry live = CaptureQuickFilterState(null);
            if (live == null) return false;

            QuickFilterEntry.CopyFilterContentFrom(live, active);
            active.MergeMembers = null;

            try { QuickFilterSettings.Instance.Save(); } catch { }

            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("quickfilters.updated", "Updated preset '{0}'"),
                    active.Name ?? ""),
                2.5f);
            return true;
        }

        private QuickFilterEntry CaptureQuickFilterStateForCompare()
        {
            var entry = new QuickFilterEntry();
            entry.CategoryPath = currentPath;
            entry.CategoryTitle = currentCategoryTitle;
            PopulateQuickFilterEntryFromCategoryFilterState(entry, CaptureCurrentFilterState());
            entry.GlobalSourceFilter = QuickFilterEntry.ClampGlobalSourceFilter((int)currentGlobalSourceFilter);
            CaptureQuickFilterSideTabState(entry);
            entry.Name = "";
            return entry;
        }

        private void PushRecentQuickFilter(QuickFilterEntry entry)
        {
            if (entry == null) return;
            for (int i = _recentQuickFilters.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_recentQuickFilters[i], entry))
                    _recentQuickFilters.RemoveAt(i);
            }
            _recentQuickFilters.Insert(0, entry);
            while (_recentQuickFilters.Count > QuickFilterRecentMax)
                _recentQuickFilters.RemoveAt(_recentQuickFilters.Count - 1);
        }

        private static bool IsQuickFilterStillInSettings(QuickFilterEntry entry)
        {
            if (entry == null) return false;
            try
            {
                var filters = QuickFilterSettings.Instance != null
                    ? QuickFilterSettings.Instance.Filters
                    : null;
                if (filters == null) return false;
                for (int i = 0; i < filters.Count; i++)
                {
                    if (ReferenceEquals(filters[i], entry)) return true;
                }
            }
            catch { }
            return false;
        }
    }

    public partial class QuickFilterEntry
    {
        private static readonly StringBuilder s_ContentSigSb = new StringBuilder(384);

        /// <summary>
        /// Copy filter/browse fields from <paramref name="src"/> onto <paramref name="dest"/>.
        /// Does not copy Id, Name, Pinned, colors, or MergeMembers.
        /// </summary>
        public static void CopyFilterContentFrom(QuickFilterEntry src, QuickFilterEntry dest)
        {
            if (src == null || dest == null) return;

            dest.CategoryPath = src.CategoryPath;
            dest.CategoryTitle = src.CategoryTitle;
            dest.SearchText = src.SearchText;
            dest.Creator = src.Creator;
            dest.UserTagAvailFilterMode = src.UserTagAvailFilterMode;
            dest.UserTagInheritVarToChildren = src.UserTagInheritVarToChildren;
            dest.GlobalSourceFilter = src.GlobalSourceFilter;
            dest.SceneSourceFilter = src.SceneSourceFilter;
            dest.AppearanceSourceFilter = src.AppearanceSourceFilter;
            dest.PackagePathFilter = src.PackagePathFilter;
            dest.ClothingSubfilter = src.ClothingSubfilter;
            dest.HairSubfilter = src.HairSubfilter;
            dest.AppearanceSubfilter = src.AppearanceSubfilter;
            dest.PosePeopleFilter = src.PosePeopleFilter;
            dest.SortState = src.SortState;
            dest.BrowseHiddenMode = src.BrowseHiddenMode;
            dest.BrowseAlwaysLoadedMode = src.BrowseAlwaysLoadedMode;
            dest.BrowseOldVersionsMode = src.BrowseOldVersionsMode;
            dest.BrowseLoadedMode = src.BrowseLoadedMode;
            dest.BrowseUnusedMode = src.BrowseUnusedMode;
            dest.LicenseFilter = src.LicenseFilter;

            CopyStringList(src.Tags, ref dest.Tags);
            CopyStringList(src.UserTags, ref dest.UserTags);
            CopyStringList(src.ExcludedUserTags, ref dest.ExcludedUserTags);

            dest.HasSideTabState = src.HasSideTabState;
            dest.LeftActiveContent = src.LeftActiveContent;
            dest.RightActiveContent = src.RightActiveContent;
            dest.CategorySideFilter = src.CategorySideFilter;
            dest.CreatorSideFilter = src.CreatorSideFilter;
            dest.UserTagSideFilter = src.UserTagSideFilter;
            dest.PathSideFilter = src.PathSideFilter;
            dest.HistoryTabFilter = src.HistoryTabFilter;
            dest.TagSubPaneFilter = src.TagSubPaneFilter;
            dest.HistoryFilterMode = src.HistoryFilterMode;

            if (dest.SideTabSortStates == null)
                dest.SideTabSortStates = new List<QuickFilterSideTabSortEntry>();
            else
                dest.SideTabSortStates.Clear();
            if (src.SideTabSortStates != null)
            {
                for (int i = 0; i < src.SideTabSortStates.Count; i++)
                {
                    QuickFilterSideTabSortEntry s = src.SideTabSortStates[i];
                    if (s == null || s.SortState == null) continue;
                    dest.SideTabSortStates.Add(new QuickFilterSideTabSortEntry
                    {
                        Context = s.Context,
                        SortState = new SortState(s.SortState.Type, s.SortState.Direction)
                    });
                }
            }
        }

        public static bool ContentSignaturesEqual(QuickFilterEntry a, QuickFilterEntry b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return string.Equals(BuildContentSignature(a), BuildContentSignature(b), StringComparison.Ordinal);
        }

        /// <summary>Stable content fingerprint (excludes Id/Name/Pinned/colors/MergeMembers).</summary>
        public static string BuildContentSignature(QuickFilterEntry e)
        {
            s_ContentSigSb.Length = 0;
            if (e == null) return "";

            AppendSig(s_ContentSigSb, e.CategoryPath);
            AppendSig(s_ContentSigSb, e.CategoryTitle);
            AppendSig(s_ContentSigSb, e.SearchText);
            AppendSig(s_ContentSigSb, e.Creator);
            s_ContentSigSb.Append('|').Append(e.UserTagAvailFilterMode);
            s_ContentSigSb.Append('|').Append(e.UserTagInheritVarToChildren);
            s_ContentSigSb.Append('|').Append(e.GlobalSourceFilter);
            AppendSig(s_ContentSigSb, e.SceneSourceFilter);
            AppendSig(s_ContentSigSb, e.AppearanceSourceFilter);
            AppendSig(s_ContentSigSb, e.PackagePathFilter);
            s_ContentSigSb.Append('|').Append(e.ClothingSubfilter);
            s_ContentSigSb.Append('|').Append(e.HairSubfilter);
            s_ContentSigSb.Append('|').Append(e.AppearanceSubfilter);
            s_ContentSigSb.Append('|').Append(e.PosePeopleFilter);
            s_ContentSigSb.Append('|').Append(e.BrowseHiddenMode);
            s_ContentSigSb.Append('|').Append(e.BrowseAlwaysLoadedMode);
            s_ContentSigSb.Append('|').Append(e.BrowseOldVersionsMode);
            s_ContentSigSb.Append('|').Append(e.BrowseLoadedMode);
            s_ContentSigSb.Append('|').Append(e.BrowseUnusedMode);
            AppendSig(s_ContentSigSb, e.LicenseFilter);
            AppendStringListSig(s_ContentSigSb, e.Tags);
            AppendStringListSig(s_ContentSigSb, e.UserTags);
            AppendStringListSig(s_ContentSigSb, e.ExcludedUserTags);

            if (e.SortState != null)
            {
                s_ContentSigSb.Append('|').Append((int)e.SortState.Type);
                s_ContentSigSb.Append(':').Append((int)e.SortState.Direction);
            }

            s_ContentSigSb.Append('|').Append(e.HasSideTabState ? 1 : 0);
            s_ContentSigSb.Append('|').Append(e.LeftActiveContent);
            s_ContentSigSb.Append('|').Append(e.RightActiveContent);
            AppendSig(s_ContentSigSb, e.CategorySideFilter);
            AppendSig(s_ContentSigSb, e.CreatorSideFilter);
            AppendSig(s_ContentSigSb, e.UserTagSideFilter);
            AppendSig(s_ContentSigSb, e.PathSideFilter);
            AppendSig(s_ContentSigSb, e.HistoryTabFilter);
            AppendSig(s_ContentSigSb, e.TagSubPaneFilter);
            s_ContentSigSb.Append('|').Append(e.HistoryFilterMode);

            if (e.SideTabSortStates != null)
            {
                for (int i = 0; i < e.SideTabSortStates.Count; i++)
                {
                    QuickFilterSideTabSortEntry s = e.SideTabSortStates[i];
                    if (s == null || s.SortState == null) continue;
                    AppendSig(s_ContentSigSb, s.Context);
                    s_ContentSigSb.Append(':').Append((int)s.SortState.Type)
                        .Append(':').Append((int)s.SortState.Direction);
                }
            }

            return s_ContentSigSb.ToString();
        }

        private static void AppendSig(StringBuilder sb, string v)
        {
            sb.Append('|');
            if (!string.IsNullOrEmpty(v)) sb.Append(v);
        }

        private static void AppendStringListSig(StringBuilder sb, List<string> list)
        {
            sb.Append('|');
            if (list == null || list.Count == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                string t = list[i];
                if (!string.IsNullOrEmpty(t)) sb.Append(t);
            }
        }

        private static void CopyStringList(List<string> src, ref List<string> dest)
        {
            if (dest == null) dest = new List<string>();
            else dest.Clear();
            if (src == null) return;
            for (int i = 0; i < src.Count; i++)
                dest.Add(src[i]);
        }
    }
}
