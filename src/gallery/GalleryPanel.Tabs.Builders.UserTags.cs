using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void BuildUserTagsTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            renderTagsPanel(container, trackedButtons, isLeft);
        }

        private void renderTagsPanel(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            EnsureUserTagAvailScrollTrackingHooks();
            SnapshotUserTagAvailScrollForPreserve(isLeft);
            EnsureUserTagSideTabBulkBlock(container.transform, isLeft);
            try { ApplyUserTagsStickyScrollChrome(TabScrollTopOffset()); } catch { }
            Transform utBulk = container.transform.Find("VPB_UserTagBulkBlock_v3");
            if (utBulk == null && isLeft && leftUserTagsAvailStickyGO != null)
                utBulk = leftUserTagsAvailStickyGO.transform.Find("VPB_UserTagBulkBlock_v3");
            if (utBulk == null && !isLeft && rightUserTagsAvailStickyGO != null)
                utBulk = rightUserTagsAvailStickyGO.transform.Find("VPB_UserTagBulkBlock_v3");
            if (utBulk != null) utBulk.SetAsFirstSibling();

            try { EnsureUserTagPinOrderRuntimeLoaded(); } catch { }
            if (!userTagsCached) CacheUserTagsSideTab();
            CacheAppliedUserTagsForSelection();

            string sigNew = ComputeUserTagVirtDataSignature();
            bool dataSigChanged = !string.Equals(_userTagVirtViewSig, sigNew, StringComparison.Ordinal);
            if (dataSigChanged)
            {
                _userTagVirtViewSig = sigNew;
                RebuildUserTagVirtViewList(isLeft, resetScrollToTop: false);
            }

            if (activeUserTags.Count > 0 || excludedUserTags.Count > 0)
            {
                CreateTabButton(container.transform,
                    VPBTranslation.T("gallery.usertags.clear_filters", "Clear user tag filters"),
                    new Color(0.55f, 0.22f, 0.22f, 1f), false,
                    () =>
                    {
                        activeUserTags.Clear();
                        excludedUserTags.Clear();
                        userTagsCached = false;
                        if (_userTagAvailMode != UserTagAvailMode.Tag) { try { RefreshFiles(true, false, false, "user_tag_clear_filters"); } catch { } }
                        try { RefreshUserTagsAvailPaneInPlace(isLeft); } catch { }
                    }, trackedButtons);
            }

            GameObject utVirtHolder = EnsureUserTagPickVirtualHolder(container.transform);
            EnsureUserTagVirtScrollHook(isLeft, utVirtHolder);
            SyncUserTagAvailPinnedStickyRows(isLeft, UserTagStateOnColor, container.transform);
            UpdateUserTagVirtualVisible(isLeft, UserTagStateOnColor, container.transform);
            RequestUserTagVirtLayoutRefresh(isLeft, container.transform, preserveScroll: true);
            SyncUserTagAvailTitleCount(isLeft);
            SyncUserTagApplyBtnCount(isLeft);
            try { ApplyUserTagsStickyScrollChrome(TabScrollTopOffset()); } catch { }
            RestorePreservedUserTagAvailScroll();
        }

        private void BuildUserTagsAppliedTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            try { EnsureUserTagPinOrderRuntimeLoaded(); } catch { }
            EnsureUserTagsAppliedToolbar(container.transform, isLeft);
            Transform utAppTb = container.transform.Find("VPB_UserTagsAppliedToolbar_v3");
            if (utAppTb == null && isLeft && leftUserTagsAppliedStickyGO != null)
                utAppTb = leftUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v3");
            if (utAppTb == null && !isLeft && rightUserTagsAppliedStickyGO != null)
                utAppTb = rightUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v3");
            if (utAppTb != null) utAppTb.SetAsFirstSibling();

            CacheAppliedUserTagsForSelection();
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                userTagAppliedRemoveSelection.Clear();
                userTagAppliedRemoveAnchor = null;
            }
            else
                PruneUserTagAppliedRemoveSelectionToCache();

            Color utAppAccent = new Color(0.38f, 0.42f, 0.52f, 1f);
            var sortApp = GetSortState("UserTagsApplied");
            var rowsApp = new List<UserTagSideTabEntry>(cachedAppliedUserTagsSelection);
            if (sortApp.Type == SortType.Count)
            {
                if (sortApp.Direction == SortDirection.Ascending)
                    rowsApp.Sort((a, b) => a.Count.CompareTo(b.Count));
                else
                    rowsApp.Sort((a, b) => b.Count.CompareTo(a.Count));
            }
            else
            {
                if (sortApp.Direction == SortDirection.Ascending)
                    rowsApp.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                else
                    rowsApp.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
            }

            string filterApp = userTagAppliedFilter ?? "";
            var visibleApplied = new List<UserTagSideTabEntry>(rowsApp.Count);
            for (int ai = 0; ai < rowsApp.Count; ai++)
            {
                UserTagSideTabEntry ae = rowsApp[ai];
                if (string.IsNullOrEmpty(ae.Name)) continue;
                if (!string.IsNullOrEmpty(filterApp) && ae.Name.IndexOf(filterApp, StringComparison.OrdinalIgnoreCase) < 0) continue;
                visibleApplied.Add(ae);
            }

            var pinnedApplied = new List<UserTagSideTabEntry>(8);
            var normalApplied = new List<UserTagSideTabEntry>(visibleApplied.Count);
            PartitionUserTagRowsPinnedFirst(visibleApplied, pinnedApplied, normalApplied);
            _userTagAppliedPinnedRows.Clear();
            _userTagAppliedPinnedRows.AddRange(pinnedApplied);
            visibleApplied.Clear();
            visibleApplied.AddRange(normalApplied);

            float sApp = ChromeScale;
            float pinInsetApp = 34f * sApp;
            SyncUserTagAppliedPinnedStickyRows(isLeft, pinnedApplied, utAppAccent, sApp);
            int appliedVisibleCount = pinnedApplied.Count + visibleApplied.Count;
            for (int vi = 0; vi < visibleApplied.Count; vi++)
            {
                UserTagSideTabEntry ae = visibleApplied[vi];
                bool isSel = userTagAppliedRemoveSelection.Contains(ae.Name);
                // Always show per-selection hit count (e.g. "(1)" for one package in ALL VAR); selTot>1 alone hid it.
                string labelA = ae.Name + " (" + ae.Count + ")";
                string tagFocusSnap = ae.Name;
                int viCapture = vi;
                int trackedBefore = trackedButtons != null ? trackedButtons.Count : 0;
                CreateTabButton(container.transform, labelA,
                    isSel ? utAppAccent : ColorInactiveRow,
                    isSel,
                    () => { OnAppliedUserTagRowClicked(viCapture, visibleApplied, tagFocusSnap); },
                    trackedButtons, null, null, tagFocusSnap, TextAnchor.MiddleCenter, pinInsetApp, 0f);
                if (trackedButtons != null && trackedButtons.Count > trackedBefore)
                    SyncUserTagRowPinButton(trackedButtons[trackedButtons.Count - 1], tagFocusSnap, false, sApp, isLeft, appliedRow: true);
            }

            SyncUserTagAppliedTitleCount(appliedVisibleCount, isLeft);
            SyncUserTagsAppliedToolbarDropZones(container.transform);
            EnsureUserTagApplyDropCatchStrip(container.transform);
            EnsureUserTagApplyDropOverlay(container.transform);
            try { ApplyUserTagsStickyScrollChrome(TabScrollTopOffset()); } catch { }
        }
    }
}

