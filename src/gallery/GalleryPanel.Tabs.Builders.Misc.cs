using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void BuildHistoryTabs(GameObject container, List<GameObject> trackedButtons)
        {
            string filterNow = (historyTabFilter ?? "").Trim();
            var countsByMode = new Dictionary<GalleryHistoryFilterMode, int>();
            bool hasCounts = VpbLocalDatabase.TryGetGalleryHistoryModeCountsCached(countsByMode);
            if (!hasCounts)
                ScheduleGalleryHistoryModeCountsNonBlocking();
            foreach (GalleryHistoryFilterMode mode in Enum.GetValues(typeof(GalleryHistoryFilterMode)))
            {
                string modeLabel = GetGalleryHistoryFilterRowLabel(mode);
                if (!string.IsNullOrEmpty(filterNow)
                    && modeLabel.IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool isActive = galleryHistoryFilterMode == mode;
                int count = 0;
                if (hasCounts) countsByMode.TryGetValue(mode, out count);
                string label = hasCounts ? (modeLabel + " (" + count + ")") : modeLabel;
                Color btnColor = isActive ? ColorHistoryAccent : ColorInactiveRow;
                GalleryHistoryFilterMode modeSnap = mode;
                CreateTabButton(container.transform, label, btnColor, isActive, () =>
                {
                    if (galleryHistoryFilterMode == modeSnap) return;
                    galleryHistoryFilterMode = modeSnap;
                    ApplyHistoryBrowseTitle();
                    ApplyHistorySortPresetForMode(modeSnap);
                    RefreshHistoryBrowsePreferLight(true);
                    UpdateTabs();
                }, trackedButtons);
            }
        }

        private void ScheduleGalleryHistoryModeCountsNonBlocking()
        {
            if (_historyModeCountsCo != null)
                return;
            _historyModeCountsCo = StartCoroutine(CoGalleryHistoryModeCountsForSideTabs());
        }

        private IEnumerator CoGalleryHistoryModeCountsForSideTabs()
        {
            try
            {
                var probe = new Dictionary<GalleryHistoryFilterMode, int>();
                if (VpbLocalDatabase.TryGetGalleryHistoryModeCountsCached(probe))
                    yield break;

                var counts = new Dictionary<GalleryHistoryFilterMode, int>();
                var workerDone = new int[1];
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { VpbLocalDatabase.TryReadGalleryHistoryModeCounts(counts); }
                    finally { workerDone[0] = 1; }
                });
                while (workerDone[0] == 0)
                    yield return null;

                if (leftActiveContent != ContentType.History && rightActiveContent != ContentType.History)
                    yield break;
                try { RebuildHistorySideTabListsOnly(); } catch { }
            }
            finally
            {
                _historyModeCountsCo = null;
            }
        }

        private void RebuildHistorySideTabListsOnly()
        {
            if (leftActiveContent == ContentType.History && leftTabContainerGO != null)
                UpdateTabs(ContentType.History, leftTabContainerGO, leftActiveTabButtons, true);
            if (rightActiveContent == ContentType.History && rightTabContainerGO != null)
                UpdateTabs(ContentType.History, rightTabContainerGO, rightActiveTabButtons, false);
        }

        private void BuildSettingsTabs(GameObject container, List<GameObject> trackedButtons)
        {
            Color groupActive = ColorTitleSearchFilterActive;
            Color groupInactive = ColorInactiveRow;
            string filterNow = (CanonicalSettingsSideSearchText() ?? "").Trim();
            bool MatchFilter(string label) =>
                string.IsNullOrEmpty(filterNow) || (label ?? "").IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) >= 0;
            float groupTabScale = (VPBConfig.Instance != null) ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            void AddGroupRow(string key, string label)
            {
                if (!string.Equals(key, "all", StringComparison.OrdinalIgnoreCase) && !MatchFilter(label)) return;
                bool isActive = string.Equals(currentSettingsGroup, key, StringComparison.OrdinalIgnoreCase);
                CreateTabButton(container.transform, label, isActive ? groupActive : groupInactive, isActive, () =>
                {
                    currentSettingsGroup = key;
                    UpdateTabs();
                    RefreshInternalSettingsListRows(true);
                }, trackedButtons, null, null, null, TextAnchor.MiddleLeft, 10f * groupTabScale, 8f * groupTabScale);
            }

            AddGroupRow("all", VPBTranslation.T("settings.group.all", "All"));
            AddGroupRow("visuals", VPBTranslation.T("settings.header.visuals", "Visuals"));
            AddGroupRow("follow", VPBTranslation.T("settings.header.follow_mode", "Follow Mode"));
            AddGroupRow("interaction", VPBTranslation.T("settings.header.interaction", "Interaction"));
            AddGroupRow("desktop", VPBTranslation.T("settings.header.desktop", "Desktop"));
            AddGroupRow("lists", VPBTranslation.T("settings.header.gallery_side_lists", "Gallery side lists"));
            AddGroupRow("categories", VPBTranslation.T("settings.header.category_visibility", "Category Visibility"));
            AddGroupRow("hover", VPBTranslation.T("settings.header.hover_preview", "Hover preview"));
            AddGroupRow("grid", VPBTranslation.T("settings.header.grid_labels", "Grid Labels"));
            AddGroupRow("search", VPBTranslation.T("settings.header.search", "Search"));
            AddGroupRow("quick", VPBTranslation.T("settings.group.category_quick", "Header category menu"));
            AddGroupRow("vr", VPBTranslation.T("settings.header.vr_integration", "VR & Game Integration"));
            if (BaImporter.TryDetectBaDataDir(out _))
                AddGroupRow("ba_migration", VPBTranslation.T("settings.group.ba_migration", "BrowserAssist Migration"));
            AddGroupRow("updater", VPBTranslation.T("settings.group.updater", "Auto-Updater"));
        }

        private void BuildRatingsTabs(GameObject container, List<GameObject> trackedButtons)
        {
            var ratingsList = new List<string> { "All Ratings", "5 Stars", "4 Stars", "3 Stars", "2 Stars", "1 Star", "No Ratings" };

            Color ratingColor = new Color(0.7f, 0.6f, 0.2f, 1f);

            foreach (var rating in ratingsList)
            {
                bool isActive = (currentRatingFilter == rating);
                Color btnColor = isActive ? ratingColor : ColorInactiveRow;

                CreateTabButton(container.transform, rating, btnColor, isActive, () => {
                    if (currentRatingFilter == rating) currentRatingFilter = "";
                    else currentRatingFilter = rating;

                    RefreshFilesAndTabs();
                }, trackedButtons);
            }
        }

        private void BuildAppearanceSourceTabs(GameObject container, List<GameObject> trackedButtons)
        {
            Color appearanceColor = new Color(0.2f, 0.4f, 0.7f, 1f);

            if (!tagsCached)
            {
                if (!TryPrimeAppearanceSubPaneCounts())
                    ScheduleTagCountsForSideTabsNonBlocking();
            }

            bool localOnly = string.Equals(currentAppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase);

            // Single toggle: "Local only" overrides the global source filter on Appearance when ON.
            // When OFF, the title-bar global filter applies normally.
            string label = "Local only (" + appearanceSourceCountCustom + ")";
            Color btnColor = localOnly ? appearanceColor : ColorInactiveRow;

            CreateTabButton(container.transform, label, btnColor, localOnly, () =>
            {
                currentAppearanceSourceFilter = localOnly ? "" : "local";
                InvalidateTags();
                RefreshFilesAndTabs();
            }, trackedButtons);

            {
                Color inactive = ColorInactiveRow;
                Color active = ColorFacetActiveRow;

                // Futa fold: no UI chip; clear stale Futa bit from prior configs.
                if ((appearanceSubfilter & AppearanceSubfilter.Futa) != 0)
                    appearanceSubfilter &= ~AppearanceSubfilter.Futa;

                string[] options = new string[] { "Female", "Male", "Unknown" };
                for (int gi = 0; gi < options.Length; gi++)
                {
                    string opt = options[gi];
                    AppearanceSubfilter flag = 0;
                    if (opt == "Male") flag = AppearanceSubfilter.Male;
                    else if (opt == "Female") flag = AppearanceSubfilter.Female;
                    else if (opt == "Unknown") flag = AppearanceSubfilter.Unknown;

                    bool isGenderActive = (flag != 0) && ((appearanceSubfilter & flag) != 0);
                    Color btnColor2 = isGenderActive ? active : inactive;

                    int cnt = 0;
                    if (opt == "Male") cnt = isGenderActive ? appearanceSubfilterCurrentCountMale : appearanceSubfilterFacetCountMale;
                    else if (opt == "Female") cnt = isGenderActive ? appearanceSubfilterCurrentCountFemale : appearanceSubfilterFacetCountFemale;
                    else if (opt == "Unknown") cnt = isGenderActive ? appearanceSubfilterCurrentCountUnknown : appearanceSubfilterFacetCountUnknown;

                    string label2 = opt + " (" + cnt + ")";

                    CreateTabButton(container.transform, label2, btnColor2, isGenderActive, () => {
                        if (flag != 0)
                        {
                            if ((appearanceSubfilter & flag) != 0) appearanceSubfilter &= ~flag;
                            else appearanceSubfilter |= flag;
                        }
                        OnAppearanceGenderFilterChanged();
                    }, trackedButtons);
                }
            }
        }

        private void BuildSizeTabs(GameObject container, List<GameObject> trackedButtons)
        {
            var sizeFilters = new List<string> { "All Sizes", "Tiny (< 10MB)", "Small (10-100MB)", "Medium (100-500MB)", "Large (500MB-1GB)", "Very Large (> 1GB)" };

            Color sizeColor = new Color(0.2f, 0.7f, 0.4f, 1f);

            foreach (var size in sizeFilters)
            {
                bool isActive = (currentSizeFilter == size);
                Color btnColor = isActive ? sizeColor : ColorInactiveRow;

                CreateTabButton(container.transform, size, btnColor, isActive, () => {
                    if (currentSizeFilter == size) currentSizeFilter = "";
                    else currentSizeFilter = size;

                    RefreshFilesAndTabs();
                }, trackedButtons);
            }
        }

        private void BuildCleanupCategoriesTabs(GameObject container, List<GameObject> trackedButtons)
        {
            int mode = GetCleanupFilterMode();
            Color cleanupColor = new Color(0.62f, 0.40f, 0.20f, 1f);
            Color inactive = ColorInactiveRow;

            string labelAll = VPBTranslation.T("gallery.cleanup.tab.all", "All") + " (" + GetCleanupTabCount(0) + ")";
            string labelDup = VPBTranslation.T("gallery.cleanup.tab.dup", "Duplicates") + " (" + GetCleanupTabCount(1) + ")";
            string labelOld = VPBTranslation.T("gallery.cleanup.tab.old", "Old Versions") + " (" + GetCleanupTabCount(2) + ")";
            string labelDamaged = VPBTranslation.T("gallery.cleanup.tab.damaged", "Damaged") + " (" + GetCleanupTabCount(3) + ")";
            string labelStale = VPBTranslation.T("gallery.cleanup.tab.stale", "Stale Cache") + " (" + GetCleanupTabCount(4) + ")";
            string labelExcluded = VPBTranslation.T("gallery.cleanup.tab.excluded", "Excluded") + " (" + GetCleanupTabCount(5) + ")";

            CreateTabButton(container.transform, labelAll, mode == 0 ? cleanupColor : inactive, mode == 0, () =>
            {
                SetCleanupFilterMode(0);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, labelDup, mode == 1 ? cleanupColor : inactive, mode == 1, () =>
            {
                SetCleanupFilterMode(1);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, labelOld, mode == 2 ? cleanupColor : inactive, mode == 2, () =>
            {
                SetCleanupFilterMode(2);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, labelDamaged, mode == 3 ? cleanupColor : inactive, mode == 3, () =>
            {
                SetCleanupFilterMode(3);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, labelStale, mode == 4 ? cleanupColor : inactive, mode == 4, () =>
            {
                SetCleanupFilterMode(4);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, labelExcluded, mode == 5 ? cleanupColor : inactive, mode == 5, () =>
            {
                SetCleanupFilterMode(5);
                UpdateTabs();
            }, trackedButtons);
        }

        private void BuildCleanupStaleBucketsTabs(GameObject container, List<GameObject> trackedButtons)
        {
            int mode = GetCleanupStaleBucketMode();
            Color cleanupColor = new Color(0.62f, 0.40f, 0.20f, 1f);
            Color inactive = ColorInactiveRow;

            string labelAll = VPBTranslation.T("gallery.cleanup.stale.all", "All") + " (" + GetCleanupStaleBucketCount(0) + ")";
            string label1w = VPBTranslation.T("gallery.cleanup.stale.1w", "One Week Old") + " (" + GetCleanupStaleBucketCount(1) + ")";
            string label2w = VPBTranslation.T("gallery.cleanup.stale.2w", "Two Weeks Old") + " (" + GetCleanupStaleBucketCount(2) + ")";
            string label1m = VPBTranslation.T("gallery.cleanup.stale.1m", "One Month Old") + " (" + GetCleanupStaleBucketCount(3) + ")";
            string label2m = VPBTranslation.T("gallery.cleanup.stale.2m", "Two Months Old") + " (" + GetCleanupStaleBucketCount(4) + ")";
            string label6m = VPBTranslation.T("gallery.cleanup.stale.6m", "Half Year Old") + " (" + GetCleanupStaleBucketCount(5) + ")";

            CreateTabButton(container.transform, labelAll, mode == 0 ? cleanupColor : inactive, mode == 0, () =>
            {
                SetCleanupStaleBucketMode(0);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, label1w, mode == 1 ? cleanupColor : inactive, mode == 1, () =>
            {
                SetCleanupStaleBucketMode(1);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, label2w, mode == 2 ? cleanupColor : inactive, mode == 2, () =>
            {
                SetCleanupStaleBucketMode(2);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, label1m, mode == 3 ? cleanupColor : inactive, mode == 3, () =>
            {
                SetCleanupStaleBucketMode(3);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, label2m, mode == 4 ? cleanupColor : inactive, mode == 4, () =>
            {
                SetCleanupStaleBucketMode(4);
                UpdateTabs();
            }, trackedButtons);
            CreateTabButton(container.transform, label6m, mode == 5 ? cleanupColor : inactive, mode == 5, () =>
            {
                SetCleanupStaleBucketMode(5);
                UpdateTabs();
            }, trackedButtons);
        }

        private void BuildSceneSourceTabs(GameObject container, List<GameObject> trackedButtons)
        {
            Color sceneColor = new Color(0.2f, 0.4f, 0.7f, 1f);

            bool localOnly = string.Equals(currentSceneSourceFilter, "local", StringComparison.OrdinalIgnoreCase);

            // Single toggle: "Local only" overrides the global source filter on Scenes when ON.
            string label = "Local only";
            Color btnColor = localOnly ? sceneColor : ColorInactiveRow;

            CreateTabButton(container.transform, label, btnColor, localOnly, () =>
            {
                currentSceneSourceFilter = localOnly ? "" : "local";
                RefreshFilesAndTabs();
            }, trackedButtons);
        }
    }
}

