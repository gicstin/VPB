using System;
using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Dictionary<string, HashSet<string>> _appearanceUserTagsByRowKey;
        private Dictionary<string, AppearanceGender> _appearanceGenderByUid;
        private string _appearanceGenderCacheCategory;

        private void ClearAppearanceGenderRefreshCaches()
        {
            _appearanceUserTagsByRowKey = null;
            _appearanceGenderByUid = null;
            _appearanceGenderCacheCategory = null;
        }

        private void EnsureAppearanceGenderRefreshCaches(string categoryTitle)
        {
            if (string.IsNullOrEmpty(categoryTitle)) return;
            if (_appearanceUserTagsByRowKey != null
                && _appearanceGenderByUid != null
                && string.Equals(_appearanceGenderCacheCategory, categoryTitle, StringComparison.OrdinalIgnoreCase))
                return;

            ClearAppearanceGenderRefreshCaches();
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            try { VpbLocalDatabase.TryLoadGalleryUserTagsForCategory(categoryTitle, map); } catch { }
            _appearanceUserTagsByRowKey = map;
            _appearanceGenderByUid = new Dictionary<string, AppearanceGender>(StringComparer.OrdinalIgnoreCase);
            _appearanceGenderCacheCategory = categoryTitle;
        }

        /// <summary>Fast SQL facet counts so Local / gender sub-pane renders without waiting for parallel scan.</summary>
        private bool TryApplyAppearanceFacetCountsFromSql()
        {
            if (!IsAppearanceCategoryTitle()) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            if (!string.IsNullOrEmpty(title))
                EnsureAppearanceGenderRefreshCaches(title);
            string extJ = string.IsNullOrEmpty(currentExtension) ? "" : currentExtension;
            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            VpbLocalDatabase.TagScanTotals sqlFacets;
            if (!VpbLocalDatabase.TryReadTagCounts(
                    title,
                    extJ,
                    currentCreator ?? "",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    tagCounts,
                    out sqlFacets,
                    clothingSubfilter,
                    hairSubfilter,
                    appearanceSubfilter,
                    activeTags,
                    _appearanceUserTagsByRowKey))
                return false;

            appearanceSourceCountAll = sqlFacets.AppearanceSourceCountAll;
            appearanceSourceCountPresets = sqlFacets.AppearanceSourceCountPresets;
            appearanceSourceCountCustom = sqlFacets.AppearanceSourceCountCustom;
            appearanceSubfilterCountAll = sqlFacets.AppearanceSubfilterCountAll;
            appearanceSubfilterCountPresets = sqlFacets.AppearanceSubfilterCountPresets;
            appearanceSubfilterCountCustom = sqlFacets.AppearanceSubfilterCountCustom;
            appearanceSubfilterCountMale = sqlFacets.AppearanceSubfilterCountMale;
            appearanceSubfilterCountFemale = sqlFacets.AppearanceSubfilterCountFemale;
            appearanceSubfilterCountFuta = sqlFacets.AppearanceSubfilterCountFuta;
            appearanceSubfilterCountUnknown = sqlFacets.AppearanceSubfilterCountUnknown;
            appearanceSubfilterFacetCountPresets = sqlFacets.AppearanceSubfilterFacetCountPresets;
            appearanceSubfilterFacetCountCustom = sqlFacets.AppearanceSubfilterFacetCountCustom;
            appearanceSubfilterFacetCountMale = sqlFacets.AppearanceSubfilterFacetCountMale;
            appearanceSubfilterFacetCountFemale = sqlFacets.AppearanceSubfilterFacetCountFemale;
            appearanceSubfilterFacetCountFuta = sqlFacets.AppearanceSubfilterFacetCountFuta;
            appearanceSubfilterFacetCountUnknown = sqlFacets.AppearanceSubfilterFacetCountUnknown;
            appearanceSubfilterCurrentCountAll = sqlFacets.AppearanceSubfilterCurrentCountAll;
            appearanceSubfilterCurrentCountMale = sqlFacets.AppearanceSubfilterCurrentCountMale;
            appearanceSubfilterCurrentCountFemale = sqlFacets.AppearanceSubfilterCurrentCountFemale;
            appearanceSubfilterCurrentCountFuta = sqlFacets.AppearanceSubfilterCurrentCountFuta;
            appearanceSubfilterCurrentCountUnknown = sqlFacets.AppearanceSubfilterCurrentCountUnknown;
            return true;
        }

        /// <summary>Populate appearance sub-pane counts immediately (folder recount or SQL fallback).</summary>
        private bool TryPrimeAppearanceSubPaneCounts()
        {
            string cat = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            if (!string.IsNullOrEmpty(cat))
                EnsureAppearanceGenderRefreshCaches(cat);

            if (TryRecomputeAppearanceGenderFacetCountsScoped())
            {
                tagsCached = true;
                return true;
            }

            if (TryApplyAppearanceFacetCountsFromSql())
            {
                TryMergeLooseVapAppearanceGenderFacetCounts();
                tagsCached = true;
                return true;
            }

            return false;
        }

        private void OnAppearanceGenderFilterChanged()
        {
            ClearAppearanceGenderRefreshCaches();
            string cat = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            if (!string.IsNullOrEmpty(cat))
                EnsureAppearanceGenderRefreshCaches(cat ?? "");

            if (TryRecomputeAppearanceGenderFacetCountsScoped())
                tagsCached = true;
            else if (TryApplyAppearanceFacetCountsFromSql())
            {
                TryMergeLooseVapAppearanceGenderFacetCounts();
                tagsCached = true;
            }
            else
                InvalidateTags();

            RefreshFilesAndTabs();
        }

        /// <summary>
        /// After gallery user-tag assign/remove: refresh gender chips and grid when Female/Male/Unknown tags change.
        /// </summary>
        internal bool TryHandleAppearanceGenderTagLiveUpdate(List<string> tags, bool remove, List<VpbLocalDatabase.GalleryUserTagRowKey> updatedRows)
        {
            if (!IsAppearanceCategoryTitle() || tags == null || tags.Count == 0) return false;
            if (!AppearanceGenderClassifier.TagsAffectAppearanceGender(tags)) return false;
            if (updatedRows == null || updatedRows.Count == 0) return false;

            string cat = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            EnsureAppearanceGenderRefreshCaches(cat ?? "");
            PatchAppearanceUserTagsForRows(updatedRows, tags, remove);

            var updatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < updatedRows.Count; i++)
            {
                VpbLocalDatabase.GalleryUserTagRowKey r = updatedRows[i];
                updatedKeys.Add(VpbLocalDatabase.FormatCatMemRowLookupKey(r.PkgUid ?? "", r.InternalPath ?? ""));
            }

            if (appearanceSubfilter != 0)
            {
                bool mightAddToActiveFacet = false;
                if (!remove)
                {
                    AppearanceGender? fromTags = AppearanceGenderClassifier.GenderFromTagNamesOnly(tags);
                    if (fromTags.HasValue
                        && AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(fromTags.Value, appearanceSubfilter))
                        mightAddToActiveFacet = true;
                }

                if (mightAddToActiveFacet)
                {
                    ClearAppearanceGenderRefreshCaches();
                    EnsureAppearanceGenderRefreshCaches(cat ?? "");
                    try { RefreshFiles(); } catch { }
                }
                else
                {
                    TryPruneAppearanceGridAfterGenderTagChange(cat ?? "", updatedKeys);
                }
            }

            if (TryRecomputeAppearanceGenderFacetCountsScoped())
                tagsCached = true;
            else if (TryApplyAppearanceFacetCountsFromSql())
            {
                TryMergeLooseVapAppearanceGenderFacetCounts();
                tagsCached = true;
            }

            string tckPut;
            if (TryBuildTagCountCacheKey(out tckPut))
            {
                try { GalleryTagCountSnapshotCache.Put(tckPut, CaptureTagCountSnapshot()); } catch { }
            }

            return true;
        }

        private void PatchAppearanceUserTagsForRows(List<VpbLocalDatabase.GalleryUserTagRowKey> rows, List<string> tags, bool remove)
        {
            if (rows == null || tags == null || _appearanceUserTagsByRowKey == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                VpbLocalDatabase.GalleryUserTagRowKey r = rows[i];
                string key = VpbLocalDatabase.FormatCatMemRowLookupKey(r.PkgUid ?? "", r.InternalPath ?? "");
                HashSet<string> set;
                if (!_appearanceUserTagsByRowKey.TryGetValue(key, out set) || set == null)
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _appearanceUserTagsByRowKey[key] = set;
                }
                for (int ti = 0; ti < tags.Count; ti++)
                {
                    string t = tags[ti];
                    if (string.IsNullOrEmpty(t)) continue;
                    if (remove) set.Remove(t);
                    else set.Add(t);
                }
            }
            InvalidateAppearanceGenderCacheForRowKeys(rows);
        }

        private void InvalidateAppearanceGenderCacheForRowKeys(List<VpbLocalDatabase.GalleryUserTagRowKey> rows)
        {
            if (rows == null || _appearanceGenderByUid == null) return;
            if (currentFilteredFiles == null) return;
            for (int fi = 0; fi < currentFilteredFiles.Count; fi++)
            {
                FileEntry fe = currentFilteredFiles[fi];
                if (fe == null || string.IsNullOrEmpty(fe.Uid)) continue;
                if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out string ip)) continue;
                string key = VpbLocalDatabase.FormatCatMemRowLookupKey(pkg ?? "", ip ?? "");
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    VpbLocalDatabase.GalleryUserTagRowKey r = rows[ri];
                    string rk = VpbLocalDatabase.FormatCatMemRowLookupKey(r.PkgUid ?? "", r.InternalPath ?? "");
                    if (string.Equals(key, rk, StringComparison.OrdinalIgnoreCase))
                    {
                        _appearanceGenderByUid.Remove(fe.Uid);
                        break;
                    }
                }
            }
        }

        private bool TryPruneAppearanceGridAfterGenderTagChange(string categoryTitle, HashSet<string> updatedRowKeys)
        {
            if (updatedRowKeys == null || updatedRowKeys.Count == 0) return false;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return false;
            if (appearanceSubfilter == 0) return false;

            var removeRefs = new HashSet<FileEntry>();
            for (int i = 0; i < currentFilteredFiles.Count; i++)
            {
                FileEntry fe = currentFilteredFiles[i];
                if (fe == null) continue;
                if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out string ip)) continue;
                string key = VpbLocalDatabase.FormatCatMemRowLookupKey(pkg ?? "", ip ?? "");
                if (!updatedRowKeys.Contains(key)) continue;

                AppearanceGender g;
                try { g = AppearanceGenderClassifier.ClassifyForGenderFilter(fe, categoryTitle, _appearanceUserTagsByRowKey); }
                catch { g = AppearanceGender.Unknown; }

                if (!string.IsNullOrEmpty(fe.Uid) && _appearanceGenderByUid != null)
                    _appearanceGenderByUid[fe.Uid] = g;

                if (!AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(g, appearanceSubfilter))
                    removeRefs.Add(fe);
            }

            if (removeRefs.Count == 0) return false;

            RemoveFileEntriesFromLists(currentFilteredFiles, removeRefs);
            RemoveFileEntriesFromLists(lastFilteredFiles, removeRefs);
            InvalidateGalleryPreHideFileListSnapshot();
            RemoveFileEntriesFromLists(topSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(filterSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(selectedFiles, removeRefs);

            if (recyclingGrid != null)
            {
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                recyclingGrid.Refresh();
            }
            try { UpdatePaginationText(); } catch { }
            return true;
        }
    }
}
