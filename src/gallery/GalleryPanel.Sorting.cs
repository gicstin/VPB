using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly int SortTypeCount = Enum.GetValues(typeof(SortType)).Length;

        private void ApplyHistorySortPresetForMode(GalleryHistoryFilterMode mode)
        {
            SortState st = GetSortState("Files");
            if (st == null) return;

            if (mode == GalleryHistoryFilterMode.MostUsed)
            {
                st.Type = SortType.UsageCount;
                st.Direction = SortDirection.Descending;
                ShowTemporaryStatus(VPBTranslation.T("gallery.history.sort_preset_most_used", "History sort preset: Usage count (desc)."), 1.8f);
            }
            else
            {
                st.Type = SortType.Date;
                st.Direction = SortDirection.Descending;
                ShowTemporaryStatus(VPBTranslation.T("gallery.history.sort_preset_recent", "History sort preset: Last used (desc)."), 1.8f);
            }

            SaveSortState("Files", st);
            UpdateSortButtonText(fileSortTypeText, fileSortDirText, st);
        }

        private bool NameFilterUsesRatingStatus()
        {
            try
            {
                if (nameFilterQuery == null || nameFilterQuery.IsEmpty) return false;
                return nameFilterQuery.HasFlag(GallerySearchQuery.StatusFlags.Starred)
                    || nameFilterQuery.HasFlag(GallerySearchQuery.StatusFlags.Unrated);
            }
            catch { return false; }
        }

        private bool IsLiveRatingFilterArmed()
        {
            return HasRatingPresenceFilter()
                || !string.IsNullOrEmpty(currentRatingFilter)
                || NameFilterUsesRatingStatus();
        }

        /// <summary>Fail-soft star rating read (treat missing/throw as 0 / unrated).</summary>
        private static int GetEntryStarRatingOrZero(FileEntry entry)
        {
            if (entry == null) return 0;
            int r = 0;
            try { r = RatingsManager.Instance != null ? RatingsManager.Instance.GetRating(entry) : 0; }
            catch { r = 0; }
            return r;
        }

        /// <summary>Star-count tab filter (Filter menu). Empty filter = pass.</summary>
        private bool PassesStarCountFilter(int rating)
        {
            if (string.IsNullOrEmpty(currentRatingFilter)) return true;
            if (currentRatingFilter == "All Ratings") return rating > 0;
            if (currentRatingFilter == "5 Stars") return rating == 5;
            if (currentRatingFilter == "4 Stars") return rating == 4;
            if (currentRatingFilter == "3 Stars") return rating == 3;
            if (currentRatingFilter == "2 Stars") return rating == 2;
            if (currentRatingFilter == "1 Star") return rating == 1;
            if (currentRatingFilter == "No Ratings") return rating == 0;
            return true;
        }

        /// <summary>
        /// Title-bar ★ presence + star-count tab (single GetRating). Used by <see cref="PassesFilters"/>
        /// including History so ★ chrome matches visible rows.
        /// </summary>
        private bool PassesLiveStarFilters(FileEntry entry)
        {
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.Off
                && string.IsNullOrEmpty(currentRatingFilter))
                return true;

            int r = GetEntryStarRatingOrZero(entry);
            if (!PassesStarCountFilter(r)) return false;
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.RatedOnly)
                return r > 0;
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.UnratedOnly)
                return r <= 0;
            return true;
        }

        /// <summary>
        /// After item ratings mutate: prune rows that fail rating filters (Not rated / Rated only / star tab / search),
        /// else rebind badge visuals. Skips full <see cref="RefreshFiles"/> (same pattern as user-tag prune).
        /// Selection moves to a surviving neighbor before prune so detail strip stays populated.
        /// Prefer overloads that pass mutated rows — avoids O(n) PassesFilters over whole grid.
        /// </summary>
        internal void AfterItemRatingsMutated()
        {
            AfterItemRatingsMutatedEntries(null);
        }

        internal void AfterItemRatingsMutated(FileEntry mutated)
        {
            _ratingMutatedScratch.Clear();
            if (mutated != null) _ratingMutatedScratch.Add(mutated);
            AfterItemRatingsMutatedEntries(_ratingMutatedScratch);
        }

        internal void AfterItemRatingsMutated(IList<FileEntry> mutated)
        {
            AfterItemRatingsMutatedEntries(mutated);
        }

        internal void AfterItemRatingsMutatedByUid(string uid)
        {
            _ratingMutatedScratch.Clear();
            if (!string.IsNullOrEmpty(uid) && currentFilteredFiles != null)
            {
                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    FileEntry fe = currentFilteredFiles[i];
                    if (fe == null) continue;
                    string feUid = null;
                    try { feUid = fe.Uid; } catch { feUid = null; }
                    if (!string.IsNullOrEmpty(feUid)
                        && string.Equals(feUid, uid, StringComparison.OrdinalIgnoreCase))
                        _ratingMutatedScratch.Add(fe);
                }
            }
            AfterItemRatingsMutatedEntries(_ratingMutatedScratch);
        }

        private void AfterItemRatingsMutatedEntries(IList<FileEntry> mutatedHint)
        {
            if (IsLiveRatingFilterArmed() && TryPruneVisibleGridAfterRatingChange(mutatedHint))
            {
                if (recyclingGrid != null)
                {
                    try
                    {
                        recyclingGrid.SetItemCount(currentFilteredFiles != null ? currentFilteredFiles.Count : 0);
                        recyclingGrid.Refresh();
                    }
                    catch { }
                }

                try
                {
                    bool historyBrowse = activeContentType == ContentType.History;
                    string navKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse);
                    int idx = FindIndexBySelectionIdentity(currentFilteredFiles, navKey, historyBrowse);
                    if (idx >= 0)
                    {
                        _detailStripScrubIndex = idx;
                        if (recyclingGrid != null) recyclingGrid.EnsureItemVisible(idx);
                    }
                    else
                        _detailStripScrubIndex = -1;
                }
                catch { }

                try { RefreshSelectionVisuals(); } catch { }
                try { UpdatePaginationText(); } catch { }
                try { UpdateEmptyGridState(); } catch { }
                try { _detailStripCacheKey = ""; DetailStripRefresh(); } catch { }
                return;
            }

            try { RefreshVisibleGridVisualsOnly(); } catch { }
        }

        /// <summary>
        /// Drop visible rows that no longer pass rating presence / star / search-status filters.
        /// Only evaluates mutated hints (or selection fallback) — not the full grid.
        /// Keeps filter/search base snapshots intact so clearing ★ can restore rows without RefreshFiles.
        /// </summary>
        private bool TryPruneVisibleGridAfterRatingChange(IList<FileEntry> mutatedHint)
        {
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return false;

            bool historyBrowse = activeContentType == ContentType.History;
            if (_ratingPruneRemoveRefs == null) _ratingPruneRemoveRefs = new HashSet<FileEntry>();
            else _ratingPruneRemoveRefs.Clear();
            if (_ratingPruneRemoveKeys == null)
                _ratingPruneRemoveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            else _ratingPruneRemoveKeys.Clear();

            HashSet<FileEntry> removeRefs = _ratingPruneRemoveRefs;
            HashSet<string> removeKeys = _ratingPruneRemoveKeys;

            IList<FileEntry> hints = mutatedHint;
            if (hints == null || hints.Count == 0)
                hints = selectedFiles;

            if (hints == null || hints.Count == 0) return false;

            for (int h = 0; h < hints.Count; h++)
            {
                FileEntry hint = hints[h];
                if (hint == null) continue;

                // Resolve to the list instance (hint may be a different FileEntry with same identity).
                string hintKey = GetSelectionIdentityKey(hint, historyBrowse);
                FileEntry visible = null;
                if (!string.IsNullOrEmpty(hintKey))
                {
                    int idx = FindIndexBySelectionIdentity(currentFilteredFiles, hintKey, historyBrowse);
                    if (idx >= 0) visible = currentFilteredFiles[idx];
                }
                if (visible == null)
                {
                    // Same-ref fast path when identity key empty / not found.
                    for (int i = 0; i < currentFilteredFiles.Count; i++)
                    {
                        if (ReferenceEquals(currentFilteredFiles[i], hint))
                        {
                            visible = hint;
                            break;
                        }
                    }
                }
                if (visible == null) continue;

                bool keep;
                try { keep = PassesFilters(visible, true); }
                catch { keep = true; }
                if (keep) continue;

                removeRefs.Add(visible);
                string k = GetSelectionIdentityKey(visible, historyBrowse);
                if (!string.IsNullOrEmpty(k)) removeKeys.Add(k);
            }

            if (removeRefs.Count == 0) return false;

            try { ReselectBeforeRatingPrune(removeRefs, removeKeys, historyBrowse); } catch { }

            // Visible + selection only. Do not hollow topSearchBaseFiles / filterSearchBaseFiles —
            // clearing ★ / presence rebuilds the view from those bases.
            RemoveFileEntriesFromLists(currentFilteredFiles, removeRefs);
            RemoveFileEntriesFromLists(lastFilteredFiles, removeRefs);
            try { InvalidateGalleryPreHideFileListSnapshot(); } catch { }
            RemoveFileEntriesFromLists(selectedFiles, removeRefs);
            try { PruneSelectedFilePathsAfterRatingPrune(removeKeys); } catch { }
            return true;
        }

        private bool IsFileRemovedForRatingPrune(
            FileEntry fe,
            HashSet<FileEntry> removeRefs,
            HashSet<string> removeKeys,
            bool historyBrowse)
        {
            if (fe == null) return false;
            if (removeRefs != null && removeRefs.Contains(fe)) return true;
            if (removeKeys == null || removeKeys.Count == 0) return false;
            string k = GetSelectionIdentityKey(fe, historyBrowse);
            return !string.IsNullOrEmpty(k) && removeKeys.Contains(k);
        }

        /// <summary>
        /// Before prune: keep surviving multi-select, or move single/empty selection to nearest survivor
        /// (prefer next, else previous). Last surviving item → clear selection.
        /// </summary>
        private void ReselectBeforeRatingPrune(
            HashSet<FileEntry> removeRefs,
            HashSet<string> removeKeys,
            bool historyBrowse)
        {
            if (removeRefs == null || removeRefs.Count == 0) return;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;

            bool anySelectedRemoved = false;
            int firstRemovedSelIdx = -1;
            int anchorRemovedIdx = -1;
            _ratingPruneSurvivingSelected.Clear();
            List<FileEntry> survivingSelected = _ratingPruneSurvivingSelected;

            string anchorKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse);
            if (!string.IsNullOrEmpty(anchorKey)
                && removeKeys != null
                && removeKeys.Contains(anchorKey))
            {
                anchorRemovedIdx = FindIndexBySelectionIdentity(currentFilteredFiles, anchorKey, historyBrowse);
            }

            if (selectedFiles != null)
            {
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    FileEntry sel = selectedFiles[i];
                    if (sel == null) continue;
                    if (IsFileRemovedForRatingPrune(sel, removeRefs, removeKeys, historyBrowse))
                    {
                        anySelectedRemoved = true;
                        int idx = FindIndexBySelectionIdentity(
                            currentFilteredFiles,
                            GetSelectionIdentityKey(sel, historyBrowse),
                            historyBrowse);
                        if (idx >= 0 && (firstRemovedSelIdx < 0 || idx < firstRemovedSelIdx))
                            firstRemovedSelIdx = idx;
                    }
                    else
                        survivingSelected.Add(sel);
                }
            }

            if (!anySelectedRemoved) return;

            try { DetailStripUnlockAfterExternalSelectionChange(); } catch { }

            // Partial multi-select survival: keep survivors only.
            if (survivingSelected.Count > 0)
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                for (int i = 0; i < survivingSelected.Count; i++)
                    AddFileToSelection(survivingSelected[i], historyBrowse);

                FileEntry anchorKeep = survivingSelected[0];
                if (!string.IsNullOrEmpty(anchorKey))
                {
                    for (int i = 0; i < survivingSelected.Count; i++)
                    {
                        if (string.Equals(
                            GetSelectionIdentityKey(survivingSelected[i], historyBrowse),
                            anchorKey,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            anchorKeep = survivingSelected[i];
                            break;
                        }
                    }
                }
                SetSelectionAnchor(anchorKeep, historyBrowse);
                selectedPath = historyBrowse
                    ? GetSelectionIdentityKey(anchorKeep, true)
                    : anchorKeep.Path;
                return;
            }

            // All selected rows leave the filter — pick nearest survivor.
            int pivot = anchorRemovedIdx >= 0 ? anchorRemovedIdx : firstRemovedSelIdx;
            if (pivot < 0)
            {
                // Rated an unselected? Should not reach here (anySelectedRemoved). Fallback: first remove.
                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    if (IsFileRemovedForRatingPrune(currentFilteredFiles[i], removeRefs, removeKeys, historyBrowse))
                    {
                        pivot = i;
                        break;
                    }
                }
            }

            FileEntry next = FindNearestSurvivorForRatingPrune(
                currentFilteredFiles, removeRefs, removeKeys, pivot, historyBrowse);

            selectedFiles.Clear();
            selectedFilePaths.Clear();

            if (next == null)
            {
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                selectedPath = null;
                _detailStripScrubIndex = -1;
                return;
            }

            AddFileToSelection(next, historyBrowse);
            SetSelectionAnchor(next, historyBrowse);
            selectedPath = historyBrowse ? GetSelectionIdentityKey(next, true) : next.Path;
        }

        private FileEntry FindNearestSurvivorForRatingPrune(
            List<FileEntry> files,
            HashSet<FileEntry> removeRefs,
            HashSet<string> removeKeys,
            int pivotIndex,
            bool historyBrowse)
        {
            if (files == null || files.Count == 0) return null;
            int n = files.Count;
            if (pivotIndex < 0) pivotIndex = 0;
            if (pivotIndex >= n) pivotIndex = n - 1;

            for (int i = pivotIndex + 1; i < n; i++)
            {
                FileEntry fe = files[i];
                if (fe != null && !IsFileRemovedForRatingPrune(fe, removeRefs, removeKeys, historyBrowse))
                    return fe;
            }
            for (int i = pivotIndex - 1; i >= 0; i--)
            {
                FileEntry fe = files[i];
                if (fe != null && !IsFileRemovedForRatingPrune(fe, removeRefs, removeKeys, historyBrowse))
                    return fe;
            }
            return null;
        }

        private void PruneSelectedFilePathsAfterRatingPrune(HashSet<string> removeKeys)
        {
            if (selectedFilePaths == null || selectedFilePaths.Count == 0) return;
            if (removeKeys == null || removeKeys.Count == 0) return;

            // selectedFilePaths may use Path while removeKeys use identity — rebuild from selectedFiles.
            selectedFilePaths.Clear();
            if (selectedFiles == null) return;
            bool historyBrowse = activeContentType == ContentType.History;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                string addKey = !string.IsNullOrEmpty(f.Path) ? f.Path : (f.Uid ?? "");
                if (string.IsNullOrEmpty(addKey)) continue;
                if (removeKeys.Contains(addKey)) continue;
                string idKey = GetSelectionIdentityKey(f, historyBrowse);
                if (!string.IsNullOrEmpty(idKey) && removeKeys.Contains(idKey)) continue;
                selectedFilePaths.Add(addKey);
            }
        }

        private bool HasRatingPresenceFilter()
        {
            return _ratingPresenceFilterMode != RatingPresenceFilterMode.Off;
        }

        private string ResolveRatingPresenceFilterLabel()
        {
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.RatedOnly)
                return VPBTranslation.T("gallery.title.rated_only", "Rated only");
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.UnratedOnly)
                return VPBTranslation.T("gallery.title.unrated_only", "Not rated");
            return VPBTranslation.T("gallery.title.rated_only", "Rated only");
        }

        private string BuildRatingPresenceFilterTooltip()
        {
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.RatedOnly)
            {
                return VPBTranslation.T(
                    "gallery.tooltip.rated_only_on",
                    "Rated only — click again for Not rated (VR OK). Right-click clears.");
            }
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.UnratedOnly)
            {
                return VPBTranslation.T(
                    "gallery.tooltip.unrated_only_on",
                    "Not rated — click again to clear. Right-click clears.");
            }
            return VPBTranslation.T(
                "gallery.tooltip.rated_only",
                "Cycle: Rated only → Not rated → Off. Right-click clears.");
        }

        /// <summary>
        /// Primary click cycles Off → RatedOnly → UnratedOnly → Off.
        /// VR laser only fires left-click — full cycle must live on primary.
        /// </summary>
        private void ToggleRatingSort()
        {
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.Off)
                _ratingPresenceFilterMode = RatingPresenceFilterMode.RatedOnly;
            else if (_ratingPresenceFilterMode == RatingPresenceFilterMode.RatedOnly)
                _ratingPresenceFilterMode = RatingPresenceFilterMode.UnratedOnly;
            else
                _ratingPresenceFilterMode = RatingPresenceFilterMode.Off;
            ApplyRatingSortFilterChange(showStatus: true);
        }

        /// <summary>Right-click on ★ while armed: clear without advancing cycle.</summary>
        private void DisableRatingSortFilterIfEnabled()
        {
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.Off) return;
            _ratingPresenceFilterMode = RatingPresenceFilterMode.Off;
            ApplyRatingSortFilterChange(showStatus: true);
        }

        private void SetRatingPresenceFilterMode(RatingPresenceFilterMode mode, bool refresh, bool showStatus)
        {
            if (_ratingPresenceFilterMode == mode)
            {
                if (showStatus) ShowRatingPresenceFilterStatus();
                return;
            }
            _ratingPresenceFilterMode = mode;
            if (refresh) ApplyRatingSortFilterChange(showStatus);
            else
            {
                SyncRatingSortToggleState();
                try { SyncBrowseFilterChipChrome(); } catch { }
                if (showStatus) ShowRatingPresenceFilterStatus();
            }
        }

        private void ApplyRatingSortFilterChange(bool showStatus = false)
        {
            SyncRatingSortToggleState();
            try { SyncBrowseFilterChipChrome(); } catch { }
            if (showStatus) ShowRatingPresenceFilterStatus();
            if (IsFilterActive)
            {
                try { ApplySearchWithinFilter(nameFilter); } catch { }
                return;
            }
            RefreshFiles();
        }

        private void ShowRatingPresenceFilterStatus()
        {
            string msg;
            if (_ratingPresenceFilterMode == RatingPresenceFilterMode.RatedOnly)
                msg = VPBTranslation.T("gallery.status.rated_only", "Filter: Rated only");
            else if (_ratingPresenceFilterMode == RatingPresenceFilterMode.UnratedOnly)
                msg = VPBTranslation.T("gallery.status.unrated_only", "Filter: Not rated");
            else
                msg = VPBTranslation.T("gallery.status.rating_filter_off", "Filter: Rating off");
            try { ShowTemporaryStatus(msg, 1.6f); } catch { }
        }

        private void SyncRatingSortToggleState()
        {
            bool rated = _ratingPresenceFilterMode == RatingPresenceFilterMode.RatedOnly;
            bool unrated = _ratingPresenceFilterMode == RatingPresenceFilterMode.UnratedOnly;
            bool armed = rated || unrated;

            if (ratingSortToggleBtnText != null)
            {
                if (rated) ratingSortToggleBtnText.color = Color.green;
                else if (unrated) ratingSortToggleBtnText.color = ColorUnratedFilterLabel;
                else ratingSortToggleBtnText.color = Color.white;
            }
            if (ratingSortIconImage != null)
            {
                Sprite target = armed ? ratingStarOffSprite : ratingStarNormalSprite;
                if (target != null) ratingSortIconImage.sprite = target;
                if (armed)
                    ratingSortIconImage.color = rated ? Color.white : ColorUnratedFilterIcon;
                else
                    ratingSortIconImage.color = Color.white;
            }
            if (ratingSortToggleBtn != null)
            {
                Image backdrop = ratingSortToggleBtn.GetComponent<Image>();
                if (backdrop != null)
                {
                    if (rated) backdrop.color = ColorHistoryAccent;
                    else if (unrated) backdrop.color = ColorUnratedFilterAccent;
                    else backdrop.color = UI.ChromeDark;
                }
            }
        }

        private SortState GetSortState(string context)
        {
            if (!contentSortStates.TryGetValue(context, out SortState state) || state == null)
            {
                state = GallerySortManager.Instance.GetDefaultSortState(context);
                contentSortStates[context] = state;
            }
            return state;
        }

        // Overload: Old method for backward compatibility
        private void CycleSort(string context, Text buttonText)
        {
            CycleSort(context, buttonText, null);
        }

        private void CycleSort(string context, Text typeText, Text dirText)
        {
            var state = GetSortState(context);
            int currentType = (int)state.Type;

            SortType nextType = state.Type;
            do
            {
                currentType = (currentType + 1) % SortTypeCount;
                nextType = (SortType)currentType;
            } while (!IsSortTypeValid(context, nextType));

            CommitSortTypeChange(context, nextType, typeText, dirText);
        }

        /// <summary>Applies a new sort type with the same default directions and refresh behavior as <see cref="CycleSort"/>.</summary>
        private void CommitSortTypeChange(string context, SortType newType, Text typeText, Text dirText)
        {
            if (!IsSortTypeValid(context, newType)) return;

            var state = GetSortState(context);
            SortType prevType = state.Type;
            state.Type = newType;
            if (state.Type == SortType.Name)
                state.Direction = SortDirection.Ascending;
            else
                state.Direction = SortDirection.Descending;

            SaveSortState(context, state);
            UpdateSortButtonText(typeText, dirText, state);

            if (context == "Files")
            {
                if (IsFilterActive)
                {
                    try
                    {
                        if (activeContentType == ContentType.History)
                        {
                            RefreshHistoryListInPlace(true);
                        }
                        else
                        {
                            if (filterSearchBaseFiles != null)
                            {
                                List<FileEntry> rebuilt = BuildFilterModeView(filterSearchBaseFiles, filterSearchLower);
                                currentFilteredFiles.Clear();
                                currentFilteredFiles.AddRange(rebuilt);
                            }
                            ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, state.Type);
                            GallerySortManager.Instance.SortFiles(currentFilteredFiles, state);
                            if (recyclingGrid != null)
                            {
                                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                                recyclingGrid.Refresh();
                            }
                            ScrollGalleryToTop();
                            UpdatePaginationText();
                        }
                    }
                    catch { }
                }
                else
                {
                    // Exclusive prune modes now live on Filter; UnusedOnly kept for legacy migrate only.
                    bool prevExclusive = false;
                    bool nextExclusive = false;

                    // Exclusive "only" modes prune the list in-place. Switching to/from them must rebuild the base list,
                    // otherwise the user can't "clear" the mode without changing categories.
                    if (prevExclusive || nextExclusive)
                    {
                        RefreshFiles(keepScroll: false);
                    }
                    else
                    {
                        if (!TryReapplyFilesSortWithoutFullRefresh())
                            RefreshFiles();
                    }
                }
            }
            else UpdateTabs();
        }

        /// <summary>
        /// Re-sorts the loaded file list and refreshes the recycling grid without re-running
        /// <see cref="GalleryPanel.RefreshFiles"/> (package scan / coroutine), then resets to top.
        /// </summary>
        private bool TryReapplyFilesSortWithoutFullRefresh()
        {
            if (!hasLoadedContent || currentFilteredFiles == null || recyclingGrid == null) return false;
            if (refreshCoroutine != null) return false;

            try
            {
                if (activeContentType == ContentType.History)
                {
                    RefreshHistoryListInPlace(true);
                    return true;
                }

                SortState st = GetSortState("Files");
                ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);
                GallerySortManager.Instance.SortFiles(currentFilteredFiles, st);

                if (lastFilteredFiles != null)
                {
                    lastFilteredFiles.Clear();
                    lastFilteredFiles.AddRange(currentFilteredFiles);
                }

                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                recyclingGrid.Refresh();
                ScrollGalleryToTop();

                UpdatePaginationText();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly SortType[] FileSortDropdownOrder =
        {
            SortType.Name, SortType.Date, SortType.DateCreated,
            SortType.DateAdded, SortType.DateUpdated,
            SortType.Size, SortType.Rating,
            SortType.UsageCount,
            SortType.Random,
            SortType.Deps, SortType.Dependents, SortType.Missing
        };

        private static string FileSortTypeFullLabel(SortType type)
        {
            switch (type)
            {
                case SortType.Name: return VPBTranslation.T("gallery.sort.full.name", "Alphabetical (name)");
                case SortType.Date: return VPBTranslation.T("gallery.sort.full.date", "Date modified");
                case SortType.DateCreated: return VPBTranslation.T("gallery.sort.full.date_created", "Date created");
                case SortType.DateAdded: return VPBTranslation.T("gallery.sort.full.date_added", "Date added (New)");
                case SortType.DateUpdated: return VPBTranslation.T("gallery.sort.full.date_updated", "Date updated");
                case SortType.Size: return VPBTranslation.T("gallery.sort.full.size", "File size");
                case SortType.Rating: return VPBTranslation.T("gallery.sort.full.rating", "Rating");
                case SortType.UsageCount: return VPBTranslation.T("gallery.sort.full.usage_count", "Usage count");
                case SortType.Random: return VPBTranslation.T("gallery.sort.full.random", "Random");
                case SortType.UnusedOnly: return VPBTranslation.T("gallery.sort.full.unused_only", "Unused (only)");
                case SortType.Deps: return VPBTranslation.T("gallery.sort.full.deps", "Dependencies");
                case SortType.Dependents: return VPBTranslation.T("gallery.sort.full.dependents", "Dependents");
                case SortType.Missing: return VPBTranslation.T("gallery.sort.full.missing", "Missing dependencies");
                case SortType.Hidden: return VPBTranslation.T("gallery.sort.full.hidden", "Hidden");
                case SortType.AutoInstall: return VPBTranslation.T("gallery.sort.full.autoinstall", "Always loaded");
                case SortType.LoadedOnly: return VPBTranslation.T("gallery.sort.full.loaded_only", "All Loaded");
                case SortType.UnloadedOnly: return VPBTranslation.T("gallery.sort.full.unloaded_only", "All Unloaded");
                default: return type.ToString();
            }
        }

        private void SetupFileSortTypeMenu()
        {
            if (fileSortTypeMenuRoot != null || backgroundBoxGO == null) return;

            fileSortTypeMenuRoot = UI.CreatePopupMenuRoot(backgroundBoxGO, "FileSortTypeMenu", CloseFileSortTypeMenu);
            fileSortTypeMenuPanelGO = UI.CreatePopupMenuPanel(
                fileSortTypeMenuRoot, "FileSortTypeMenuPanel",
                AnchorPresets.topMiddle, new Vector2(260f, 50f), new Vector2(108f, -72f));

            fileSortTypeMenuRoot.SetActive(false);
            RebuildFileSortTypeMenuOptions();
        }

        private static string SidePaneSortFullLabel(SortType type, SortDirection dir)
        {
            // Side panes generally only support Name / Count; include direction in the label.
            if (type == SortType.Name)
            {
                return dir == SortDirection.Ascending
                    ? VPBTranslation.T("gallery.sort.full.name_az", "Name (A→Z)")
                    : VPBTranslation.T("gallery.sort.full.name_za", "Name (Z→A)");
            }
            if (type == SortType.Count)
            {
                return dir == SortDirection.Ascending
                    ? VPBTranslation.T("gallery.sort.full.count_low_high", "Count (low→high)")
                    : VPBTranslation.T("gallery.sort.full.count_high_low", "Count (high→low)");
            }
            if (type == SortType.Rating)
            {
                return dir == SortDirection.Ascending
                    ? VPBTranslation.T("gallery.sort.full.creator_rating_low_high", "Rating (low→high)")
                    : VPBTranslation.T("gallery.sort.full.creator_rating_high_low", "Rating (high→low)");
            }
            return type.ToString() + " " + (dir == SortDirection.Ascending ? "↑" : "↓");
        }

        private void SetupSidePaneSortMenu()
        {
            if (sidePaneSortMenuRoot != null || backgroundBoxGO == null) return;

            sidePaneSortMenuRoot = UI.CreatePopupMenuRoot(backgroundBoxGO, "SidePaneSortMenu", CloseSidePaneSortMenu);
            // Anchors / position are set at open-time based on which button was clicked.
            sidePaneSortMenuPanelGO = UI.CreatePopupMenuPanel(
                sidePaneSortMenuRoot, "SidePaneSortMenuPanel",
                AnchorPresets.topLeft, new Vector2(240f, 50f), new Vector2(10f, -95f));
            sidePaneSortMenuPanelRT = sidePaneSortMenuPanelGO.GetComponent<RectTransform>();

            sidePaneSortMenuRoot.SetActive(false);
        }

        private void CloseSidePaneSortMenu()
        {
            if (sidePaneSortMenuRoot != null)
                sidePaneSortMenuRoot.SetActive(false);
            sidePaneSortMenuContext = null;
        }

        private void RebuildSidePaneSortMenuOptions(string context)
        {
            if (sidePaneSortMenuPanelGO == null) return;
            if (string.IsNullOrEmpty(context)) return;

            Transform panel = sidePaneSortMenuPanelGO.transform;
            UI.DestroyAllChildren(panel);

            SortState current = GetSortState(context);
            if (current == null) current = new SortState(SortType.Name, SortDirection.Ascending);

            // Side panes: Name/Count asc/desc. Creator also gets Rating + rated-only filter toggle.
            bool isCreator = string.Equals(context, "Creator", StringComparison.Ordinal);
            SortType[] optTypes;
            SortDirection[] optDirs;
            if (isCreator)
            {
                optTypes = new SortType[]
                {
                    SortType.Name, SortType.Name, SortType.Count, SortType.Count,
                    SortType.Rating, SortType.Rating
                };
                optDirs = new SortDirection[]
                {
                    SortDirection.Ascending, SortDirection.Descending,
                    SortDirection.Ascending, SortDirection.Descending,
                    SortDirection.Ascending, SortDirection.Descending
                };
            }
            else
            {
                optTypes = new SortType[] { SortType.Name, SortType.Name, SortType.Count, SortType.Count };
                optDirs = new SortDirection[] { SortDirection.Ascending, SortDirection.Descending, SortDirection.Ascending, SortDirection.Descending };
            }
            for (int oi = 0; oi < optTypes.Length && oi < optDirs.Length; oi++)
            {
                SortType optType = optTypes[oi];
                SortDirection optDir = optDirs[oi];
                if (!IsSortTypeValid(context, optType)) continue;

                bool isCurrent = current.Type == optType && current.Direction == optDir;
                string label = (isCurrent ? "\u2713  " : "    ") + SidePaneSortFullLabel(optType, optDir);
                SortType capturedType = optType;
                SortDirection capturedDir = optDir;

                GameObject row = UI.AddPopupMenuRow(
                    sidePaneSortMenuPanelGO, 228, 34, label, 14, isCurrent,
                    () =>
                    {
                        if (SupportsSidePaneFourModeSort(context) && capturedType != SortType.Rating)
                            ApplySidePaneFourModeSort(context, capturedType, capturedDir);
                        else
                        {
                            SortState st = GetSortState(context);
                            st.Type = capturedType;
                            st.Direction = capturedDir;
                            SaveSortState(context, st);
                            try { SyncSidePaneTopSortButtonVisuals(); } catch { }
                            UpdateTabs();
                        }
                        CloseSidePaneSortMenu();
                    },
                    GalleryUiDesignTokens.PopupMenuRowHeightCompactRef);
            }

            if (isCreator)
            {
                bool ratedOn = creatorRatedOnlyFilter;
                string ratedLabel = (ratedOn ? "\u2713  " : "    ")
                    + VPBTranslation.T("gallery.sort.full.creator_rated_only", "Rated only (filter)");
                UI.AddPopupMenuRow(
                    sidePaneSortMenuPanelGO, 228, 34, ratedLabel, 14, ratedOn,
                    () =>
                    {
                        ToggleCreatorRatedOnlyFilter();
                        CloseSidePaneSortMenu();
                    },
                    GalleryUiDesignTokens.PopupMenuRowHeightCompactRef);
            }

            SyncSidePaneSortMenuLayout(ChromeScale);
        }

        private void SyncSidePaneSortMenuLayout(float s)
        {
            if (sidePaneSortMenuPanelGO == null) return;
            if (s <= 0f) s = 1f;
            ScaleVerticalPopupMenuRows(sidePaneSortMenuPanelGO, s,
                GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiDesignTokens.SidePaneSortMenuPanelWidthRef);
        }

        private void ToggleSidePaneSortMenu(string context, RectTransform anchorButtonRT)
        {
            if (sidePaneSortMenuRoot == null) SetupSidePaneSortMenu();
            if (sidePaneSortMenuRoot == null || sidePaneSortMenuPanelRT == null) return;

            if (sidePaneSortMenuRoot.activeSelf && string.Equals(sidePaneSortMenuContext ?? "", context ?? "", StringComparison.OrdinalIgnoreCase))
            {
                CloseSidePaneSortMenu();
                return;
            }

            sidePaneSortMenuContext = context ?? "";
            RebuildSidePaneSortMenuOptions(sidePaneSortMenuContext);

            // Position directly under the clicked sort button.
            bool isRight = false;
            try { isRight = anchorButtonRT != null && anchorButtonRT.anchorMin.x > 0.5f; } catch { isRight = false; }
            float sc = ChromeScale;
            float gapY = 6f * sc;
            Vector2 btnPos = anchorButtonRT != null ? anchorButtonRT.anchoredPosition : new Vector2(10f * sc, -65f * sc);
            Vector2 btnSize = anchorButtonRT != null ? anchorButtonRT.sizeDelta : new Vector2(35f * sc, 35f * sc);

            sidePaneSortMenuPanelRT.anchorMin = sidePaneSortMenuPanelRT.anchorMax = (anchorButtonRT != null ? anchorButtonRT.anchorMin : new Vector2(0f, 1f));
            sidePaneSortMenuPanelRT.pivot = isRight ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            sidePaneSortMenuPanelRT.anchoredPosition = btnPos + new Vector2(0f, -(btnSize.y + gapY));

            sidePaneSortMenuRoot.SetActive(true);
            sidePaneSortMenuRoot.transform.SetAsLastSibling();
        }

        private void ToggleFileSortTypeMenu()
        {
            if (fileSortTypeMenuRoot == null) SetupFileSortTypeMenu();
            if (fileSortTypeMenuRoot == null) return;

            if (fileSortTypeMenuRoot.activeSelf)
            {
                CloseFileSortTypeMenu();
                return;
            }

            RebuildFileSortTypeMenuOptions();
            SyncFileSortTypeMenuLayout(ChromeScale);
            fileSortTypeMenuRoot.SetActive(true);
            fileSortTypeMenuRoot.transform.SetAsLastSibling();
        }

        private void CloseFileSortTypeMenu()
        {
            if (fileSortTypeMenuRoot != null)
                fileSortTypeMenuRoot.SetActive(false);
        }

        private void RebuildFileSortTypeMenuOptions()
        {
            if (fileSortTypeMenuPanelGO == null) return;

            Transform panel = fileSortTypeMenuPanelGO.transform;
            UI.DestroyAllChildren(panel);

            SortState current = GetSortState("Files");

            foreach (SortType sortType in FileSortDropdownOrder)
            {
                if (!IsSortTypeValid("Files", sortType)) continue;

                bool isCurrent = current.Type == sortType;
                string label = (isCurrent ? "\u2713  " : "    ") + FileSortTypeFullLabel(sortType);
                SortType captured = sortType;

                UI.AddPopupMenuRow(
                    fileSortTypeMenuPanelGO, 248, 36, label, 14, isCurrent,
                    () =>
                    {
                        CommitSortTypeChange("Files", captured, fileSortTypeText, fileSortDirText);
                        CloseFileSortTypeMenu();
                    },
                    38f);
            }

            SyncFileSortTypeMenuLayout(ChromeScale);
        }

        private void SyncFileSortTypeMenuLayout(float s)
        {
            if (fileSortTypeMenuPanelGO == null) return;
            if (s <= 0f) s = 1f;
            RectTransform panelRT = fileSortTypeMenuPanelGO.GetComponent<RectTransform>();
            if (panelRT == null) return;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            if (_titleBarFileSortTypeBtnRT != null)
            {
                panelRT.anchoredPosition = _titleBarFileSortTypeBtnRT.anchoredPosition
                    + new Vector2(0f, -(_titleBarFileSortTypeBtnRT.sizeDelta.y + gap));
            }
            else
            {
                panelRT.anchoredPosition = new Vector2(108f * s, -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
            }
            panelRT.sizeDelta = new Vector2(GalleryUiDesignTokens.FileSortMenuPanelWidthRef * s, panelRT.sizeDelta.y);
            ScaleVerticalPopupMenuRows(fileSortTypeMenuPanelGO, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiDesignTokens.FileSortMenuPanelWidthRef);
        }

        private void ToggleFileSortDirection()
        {
            SortState st = GetSortState("Files");
            SortDirection next = st.Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
            CommitSortDirectionChange("Files", next, fileSortTypeText, fileSortDirText);
        }

        private void CommitSortDirectionChange(string context, SortDirection dir, Text typeText, Text dirText)
        {
            var state = GetSortState(context);
            state.Direction = dir;
            SaveSortState(context, state);
            UpdateSortButtonText(typeText, dirText, state);

            if (context == "Files")
            {
                if (IsFilterActive)
                {
                    try
                    {
                        if (activeContentType == ContentType.History)
                            RefreshHistoryListInPlace(true);
                        else
                        {
                            GallerySortManager.Instance.SortFiles(currentFilteredFiles, state);
                            if (recyclingGrid != null)
                            {
                                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                                recyclingGrid.Refresh();
                            }
                            ScrollGalleryToTop();
                            UpdatePaginationText();
                        }
                    }
                    catch { }
                }
                else
                {
                    if (!TryReapplyFilesSortWithoutFullRefresh())
                        RefreshFiles();
                }
            }
            else UpdateTabs();
        }

        private bool IsSortTypeValid(string context, SortType type)
        {
            if (context == "Files")
            {
                // Hidden / Always loaded / Loaded / Unloaded live on Filter button.
                return type == SortType.Name || type == SortType.Date || type == SortType.DateCreated
                    || type == SortType.DateAdded || type == SortType.DateUpdated
                    || type == SortType.Size || type == SortType.Rating || type == SortType.Deps || type == SortType.Dependents || type == SortType.Missing
                    || type == SortType.UsageCount
                    || type == SortType.Random
                    // Prefer-sort still used by Always-loaded / Unused Filter cycles (not shown in sort menu).
                    || type == SortType.AutoInstall;
            }
            else if (context == "Creator")
            {
                return type == SortType.Name || type == SortType.Count || type == SortType.Rating;
            }
            else if (context == "Category" || context == "Path" || context == "UserTags" || context == "UserTagsApplied" || context == "Status" || context == "Tags" || context == "SceneSource" || context == "DetailStripTagMenu")
            {
                return type == SortType.Name || type == SortType.Count;
            }
            return false;
        }

        private static bool SupportsSidePaneFourModeSort(string context)
        {
            return context == "Category" || context == "Creator" || context == "Path" || context == "UserTags" || context == "UserTagsApplied" || context == "Status" || context == "Tags";
        }

        /// <summary>Upper side pane: name A→Z, name Z→A, count low→high, count high→low (same icons as scene file sort).</summary>
        private static void SidePaneFourModeToState(int mode, out SortType type, out SortDirection dir)
        {
            switch (mode)
            {
                case 1: type = SortType.Name; dir = SortDirection.Descending; break;
                case 2: type = SortType.Count; dir = SortDirection.Ascending; break;
                case 3: type = SortType.Count; dir = SortDirection.Descending; break;
                default: type = SortType.Name; dir = SortDirection.Ascending; break;
            }
        }

        private static int TryGetSidePaneFourModeIndex(SortState st)
        {
            if (st == null) return -1;
            if (st.Type == SortType.Name && st.Direction == SortDirection.Ascending) return 0;
            if (st.Type == SortType.Name && st.Direction == SortDirection.Descending) return 1;
            if (st.Type == SortType.Count && st.Direction == SortDirection.Ascending) return 2;
            if (st.Type == SortType.Count && st.Direction == SortDirection.Descending) return 3;
            return -1;
        }

        private void CycleSidePaneTopSort(bool isLeft)
        {
            ContentType? ct = isLeft ? leftActiveContent : rightActiveContent;
            if (!ct.HasValue) return;
            string ctx = ct.Value.ToString();
            if (!SupportsSidePaneFourModeSort(ctx))
            {
                Text t = isLeft ? leftSortBtnText : rightSortBtnText;
                CycleSort(ctx, t);
                return;
            }
            SortState st = GetSortState(ctx);
            int i = TryGetSidePaneFourModeIndex(st);
            int next = (i < 0) ? 0 : (i + 1) % 4;
            SidePaneFourModeToState(next, out SortType ty, out SortDirection d);
            ApplySidePaneFourModeSort(ctx, ty, d);
        }

        /// <summary>Lower split row (tags / hub tags): same 4-mode cycle as upper pane, persisted as <c>Tags</c>.</summary>
        private void CycleSidePaneSubTagSort()
        {
            const string ctx = "Tags";
            SortState st = GetSortState(ctx);
            int i = TryGetSidePaneFourModeIndex(st);
            int next = (i < 0) ? 0 : (i + 1) % 4;
            SidePaneFourModeToState(next, out SortType ty, out SortDirection d);
            ApplySidePaneFourModeSort(ctx, ty, d);
        }

        private void ApplySidePaneFourModeSort(string context, SortType type, SortDirection direction)
        {
            if (!IsSortTypeValid(context, type)) return;
            SortState state = GetSortState(context);
            state.Type = type;
            state.Direction = direction;
            SaveSortState(context, state);
            UpdateTabs();
        }

        /// <summary>Upper side sort + tag sub-row: one place for vpb_icons vs legacy text.</summary>
        private void SyncSidePaneTopSortButtonVisuals()
        {
            if (leftSortBtn != null && leftActiveContent.HasValue && !ContentTypeSuppressesSideSort(leftActiveContent.Value))
            {
                string ctx = leftActiveContent.Value.ToString();
                SyncSidePaneFourModeSortButtonVisual(leftSortBtnBackdrop, leftSortBtnIconImage, leftSortBtnText, GetSortState(ctx), SupportsSidePaneFourModeSort(ctx));
            }
            if (rightSortBtn != null && rightActiveContent.HasValue && !ContentTypeSuppressesSideSort(rightActiveContent.Value))
            {
                string ctx = rightActiveContent.Value.ToString();
                SyncSidePaneFourModeSortButtonVisual(rightSortBtnBackdrop, rightSortBtnIconImage, rightSortBtnText, GetSortState(ctx), SupportsSidePaneFourModeSort(ctx));
            }

            if (leftSubSortBtn != null && leftSubSortBtn.activeSelf)
            {
                string subCtx = "Tags";
                if (leftActiveContent == ContentType.UserTags) subCtx = "UserTagsApplied";
                SortState tagSt = GetSortState(subCtx);
                bool tagIcon = SupportsSidePaneFourModeSort(subCtx);
                SyncSidePaneFourModeSortButtonVisual(leftSubSortBtnBackdrop, leftSubSortBtnIconImage, leftSubSortBtnText, tagSt, tagIcon);
            }
            if (rightSubSortBtn != null && rightSubSortBtn.activeSelf)
            {
                string subCtxR = "Tags";
                if (rightActiveContent == ContentType.UserTags) subCtxR = "UserTagsApplied";
                SortState tagStR = GetSortState(subCtxR);
                bool tagIconR = SupportsSidePaneFourModeSort(subCtxR);
                SyncSidePaneFourModeSortButtonVisual(rightSubSortBtnBackdrop, rightSubSortBtnIconImage, rightSubSortBtnText, tagStR, tagIconR);
            }
        }

        private void SyncSidePaneFourModeSortButtonVisual(Image backdrop, Image iconImg, Text legacyText, SortState st, bool iconMode)
        {
            if (backdrop == null) return;
            int idx = TryGetSidePaneFourModeIndex(st);
            // Rating (and any non-4-mode sort) uses text — do not fake Name A→Z icon.
            if (iconMode && idx >= 0 && sceneSourceSortModeSprites != null && iconImg != null)
            {
                if (legacyText != null) legacyText.gameObject.SetActive(false);
                iconImg.gameObject.SetActive(true);
                Sprite sp = idx < sceneSourceSortModeSprites.Length ? sceneSourceSortModeSprites[idx] : null;
                if (sp != null)
                {
                    iconImg.sprite = sp;
                    iconImg.enabled = true;
                }
                else
                    iconImg.enabled = false;
                backdrop.color = SceneSourceSortBtnActive;
            }
            else
            {
                if (iconImg != null)
                {
                    iconImg.enabled = false;
                    iconImg.gameObject.SetActive(false);
                }
                if (legacyText != null)
                {
                    legacyText.gameObject.SetActive(true);
                    if (st != null) UpdateSortButtonText(legacyText, st);
                }
                backdrop.color = (st != null && st.Type == SortType.Rating)
                    ? SceneSourceSortBtnActive
                    : SceneSourceSortBtnIdle;
            }
        }

        // Overload: Old method for combined button
        private void UpdateSortButtonText(Text t, SortState state)
        {
            if (t == null) return;
            string symbol = "";
            switch(state.Type)
            {
                case SortType.Name: symbol = "Az"; break;
                case SortType.Date: symbol = "Dt"; break;
                case SortType.DateCreated: symbol = "Dc"; break;
                case SortType.DateAdded: symbol = "Nw"; break;
                case SortType.DateUpdated: symbol = "Up"; break;
                case SortType.Size: symbol = "Sz"; break;
                case SortType.Count: symbol = "#"; break;
                case SortType.Score: symbol = "Sc"; break;
                case SortType.Rating: symbol = "Rt"; break;
                case SortType.UsageCount: symbol = "Us"; break;
                case SortType.Random: symbol = "Rnd"; break;
                case SortType.UnusedOnly: symbol = "U0"; break;
                case SortType.Deps: symbol = "Dp"; break;
                case SortType.Dependents: symbol = "Dn"; break;
                case SortType.Missing: symbol = "Ms"; break;
                case SortType.Hidden: symbol = "Hd"; break;
                case SortType.HiddenOnly: symbol = "HO"; break;
                case SortType.AutoInstall: symbol = "Ai"; break;
                case SortType.AutoInstallOnly: symbol = "AO"; break;
                case SortType.LoadedOnly: symbol = "LO"; break;
                case SortType.UnloadedOnly: symbol = "UO"; break;
            }
            string arrow = state.Direction == SortDirection.Ascending ? "↑" : "↓";
            t.text = symbol + arrow;
        }

        // New method: Update separate type and direction buttons
        private void UpdateSortButtonText(Text typeText, Text dirText, SortState state)
        {
            if (typeText != null)
            {
                string symbol = "";
                switch(state.Type)
                {
                    case SortType.Name: symbol = "Az"; break;
                    case SortType.Date: symbol = "Dt"; break;
                    case SortType.DateCreated: symbol = "Dc"; break;
                    case SortType.DateAdded: symbol = "Nw"; break;
                    case SortType.DateUpdated: symbol = "Up"; break;
                    case SortType.Size: symbol = "Sz"; break;
                    case SortType.Count: symbol = "#"; break;
                    case SortType.Score: symbol = "Sc"; break;
                    case SortType.Rating: symbol = "Rt"; break;
                    case SortType.UsageCount: symbol = "Us"; break;
                    case SortType.Random: symbol = "Rnd"; break;
                    case SortType.UnusedOnly: symbol = "U0"; break;
                    case SortType.Deps: symbol = "Dp"; break;
                    case SortType.Dependents: symbol = "Dn"; break;
                    case SortType.Missing: symbol = "Ms"; break;
                    case SortType.Hidden: symbol = "Hd"; break;
                    case SortType.HiddenOnly: symbol = "HO"; break;
                    case SortType.AutoInstall: symbol = "Ai"; break;
                    case SortType.AutoInstallOnly: symbol = "AO"; break;
                    case SortType.LoadedOnly: symbol = "LO"; break;
                    case SortType.UnloadedOnly: symbol = "UO"; break;
                }
                typeText.text = symbol;
            }
            if (dirText != null)
            {
                string arrow = state.Direction == SortDirection.Ascending ? "↑" : "↓";
                dirText.text = arrow;
            }

            // Swap sort-direction icon sprite
            if (fileSortDirIconImage != null)
            {
                Sprite target = state.Direction == SortDirection.Ascending ? fileSortDirAscSprite : fileSortDirDescSprite;
                if (target != null) fileSortDirIconImage.sprite = target;
            }
        }

        private void SaveSortState(string context, SortState state)
        {
            contentSortStates[context] = state;
            GallerySortManager.Instance.SaveSortState(context, state);
        }

        /// <summary>Scene sub-pane: cycle sort order of All/Addon/Custom tabs only (not main file list).</summary>
        private void CycleSceneSourceTabSort()
        {
            SortState st = GetSortState("SceneSource");
            int i = TryGetSidePaneFourModeIndex(st);
            int next = (i < 0) ? 0 : (i + 1) % 4;
            SidePaneFourModeToState(next, out SortType t, out SortDirection d);
            ApplySceneSourceTabSort(t, d);
        }

        private void ApplySceneSourceTabSort(SortType type, SortDirection direction)
        {
            if (!IsSortTypeValid("SceneSource", type)) return;
            SortState state = GetSortState("SceneSource");
            state.Type = type;
            state.Direction = direction;
            SaveSortState("SceneSource", state);
            SyncSceneSourceSortButtonHighlights();
            UpdateTabs();
        }

        private static readonly Color SceneSourceSortBtnIdle = UI.ChromeDark;
        private static readonly Color SceneSourceSortBtnActive = new Color(0.15f, 0.30f, 0.52f, 1f);

        private void SyncSceneSourceSortButtonHighlights()
        {
            SortState st = GetSortState("SceneSource");
            int idx = TryGetSidePaneFourModeIndex(st);
            int spriteIdx = idx >= 0 ? idx : 0;
            Sprite sp = null;
            if (sceneSourceSortModeSprites != null && spriteIdx >= 0 && spriteIdx < sceneSourceSortModeSprites.Length)
                sp = sceneSourceSortModeSprites[spriteIdx];
            Color backdropCol = idx >= 0 ? SceneSourceSortBtnActive : SceneSourceSortBtnIdle;

            void ApplyOne(Image backdropImg, Image iconImg)
            {
                if (backdropImg != null) backdropImg.color = backdropCol;
                if (iconImg != null)
                {
                    if (sp != null)
                    {
                        iconImg.sprite = sp;
                        iconImg.enabled = true;
                    }
                    else
                        iconImg.enabled = false;
                }
            }

            ApplyOne(leftSubSceneSortBtnBackdrop, leftSubSceneSortIconImage);
            ApplyOne(rightSubSceneSortBtnBackdrop, rightSubSceneSortIconImage);
        }
    }
}
