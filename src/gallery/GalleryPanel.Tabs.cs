using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel
    {

        /// <summary>
        /// Rebuilds only split-view bottom panes (tags, hub tags, scene/appearance source rows). Used after
        /// <see cref="UpdateTabsImpl(bool,bool)"/> with <c>rebuildSubPaneSideTabLists: false</c> so heavy tag UI
        /// can run on the next frame while category/creator strips already match the new category.
        /// </summary>
        private void RebuildSubPaneSideTabListsOnly()
        {
            RebuildSubPaneSideTabListForSide(isLeft: true);
            RebuildSubPaneSideTabListForSide(isLeft: false);
            try { ApplyUserTagsStickyScrollChrome(TabScrollTopOffset()); } catch { }
        }

        private void RebuildSubPaneSideTabListForSide(bool isLeft)
        {
            ContentType? activeContent = isLeft ? leftActiveContent : rightActiveContent;
            if (!activeContent.HasValue || activeContent.Value != ContentType.Category)
                return;

            string title = titleText != null ? titleText.text : "";
            if (!CategoryNeedsSplitView(title))
                return;

            GameObject subScroll = isLeft ? leftSubTabScrollGO : rightSubTabScrollGO;
            GameObject subContainer = isLeft ? leftSubTabContainerGO : rightSubTabContainerGO;
            if (subScroll == null || subContainer == null)
                return;

            ContentType subType = InferCategorySubPaneTypeFromTitle(title);
            List<GameObject> activeButtons = isLeft ? leftSubActiveTabButtons : rightSubActiveTabButtons;
            UpdateTabs(subType, subContainer, activeButtons, isLeft);
        }

        private void TeardownCategoryCreatorDualBufferOneSide(bool isLeft)
        {
            List<GameObject> catList = isLeft ? leftCategoryTabButtons : rightCategoryTabButtons;
            List<GameObject> crList = isLeft ? leftCreatorTabButtons : rightCreatorTabButtons;
            GameObject catH = isLeft ? leftCategoryTabHolder : rightCategoryTabHolder;
            GameObject crH = isLeft ? leftCreatorTabHolder : rightCreatorTabHolder;

            if (catList != null)
            {
                foreach (var b in catList) ReturnTabButton(b);
                catList.Clear();
            }
            if (crList != null)
            {
                foreach (var b in crList) ReturnTabButton(b);
                crList.Clear();
            }
            
            // Clear virtual pool references
            if (isLeft) _leftCreatorVirtButtons.Clear();
            else _rightCreatorVirtButtons.Clear();

            if (catH != null)
            {
                try { UnityEngine.Object.Destroy(catH); } catch { }
            }
            if (crH != null)
            {
                try { UnityEngine.Object.Destroy(crH); } catch { }
            }
            if (isLeft)
            {
                leftCategoryTabHolder = null;
                leftCreatorTabHolder = null;
                leftCategoryTabsLastSig = null;
                leftCreatorTabsLastSig = null;
                _leftCreatorVirtHooked = false;
                _leftCreatorVirtScroll = null;
            }
            else
            {
                rightCategoryTabHolder = null;
                rightCreatorTabHolder = null;
                rightCategoryTabsLastSig = null;
                rightCreatorTabsLastSig = null;
                _rightCreatorVirtHooked = false;
                _rightCreatorVirtScroll = null;
            }
        }

        private void ClearTabContainerChildrenForDualBufferInit(GameObject tabContainer)
        {
            if (tabContainer == null) return;
            Transform t = tabContainer.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject go = t.GetChild(i).gameObject;
                if (go.GetComponent<Button>() != null)
                    ReturnTabButton(go);
                else
                    UnityEngine.Object.Destroy(go);
            }
        }

        private GameObject CreateCategoryCreatorTabStackHolder(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            VerticalLayoutGroup v = UI.AddVLG(go, spacing: GalleryUiDesignTokens.SideTabRowSpacingRef, childAlignment: TextAnchor.UpperCenter);
            {
                var vlg = v;
                innerPaneScaleActions.Add(s => SyncSideTabListHolderVerticalLayoutOn(vlg, s));
            }
            ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement le = UI.AddLE(go, flexibleWidth: 1f);
            return go;
        }

        private GameObject CreateCreatorVirtualHolder(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, 0);

            LayoutElement le = UI.AddLE(go, minHeight: 1f, preferredHeight: 1f, flexibleWidth: 1f);
            return go;
        }

        private bool IsUserTagsSideTabOpen(bool isLeft)
        {
            return isLeft
                ? leftActiveContent == ContentType.UserTags
                : rightActiveContent == ContentType.UserTags;
        }

        /// <summary>Remove User Tags rows/blocks from the main side scroll when another pane owns that rail.</summary>
        private void PurgeUserTagsSideTabArtifactsFromMainPane(bool isLeft)
        {
            if (IsUserTagsSideTabOpen(isLeft)) return;
            GameObject container = isLeft ? leftTabContainerGO : rightTabContainerGO;
            if (container == null) return;
            DestroyEphemeralSideTabBlocksForContentType(container.transform, ContentType.Category);
            GameObject pinnedStrip = isLeft ? leftUserTagsAvailPinnedStickyGO : rightUserTagsAvailPinnedStickyGO;
            if (pinnedStrip != null) pinnedStrip.SetActive(false);
        }

        private void EnsureCategoryCreatorHolders(GameObject tabContainer, bool isLeft)
        {
            GameObject catH = isLeft ? leftCategoryTabHolder : rightCategoryTabHolder;
            GameObject crH = isLeft ? leftCreatorTabHolder : rightCreatorTabHolder;
            if (catH != null && crH != null)
            {
                PurgeUserTagsSideTabArtifactsFromMainPane(isLeft);
                return;
            }

            List<GameObject> legacy = isLeft ? leftActiveTabButtons : rightActiveTabButtons;
            if (legacy != null)
            {
                foreach (var b in legacy) ReturnTabButton(b);
                legacy.Clear();
            }
            ClearTabContainerChildrenForDualBufferInit(tabContainer);

            catH = CreateCategoryCreatorTabStackHolder("_VPB_CategoryTabs", tabContainer.transform);
            crH = CreateCreatorVirtualHolder("_VPB_CreatorTabs_Virt", tabContainer.transform);
            if (isLeft)
            {
                leftCategoryTabHolder = catH;
                leftCreatorTabHolder = crH;
            }
            else
            {
                rightCategoryTabHolder = catH;
                rightCreatorTabHolder = crH;
            }
        }

        private string ComputeCreatorVirtViewSignature()
        {
            SortState st = GetSortState("Creator");
            float scale = ChromeScale;
            // Include cache revision + filter + sort + scale so we rebuild view list only when needed.
            return "v1|" + creatorSideTabDataRevision
                + "|" + (creatorFilter ?? "")
                + "|" + (nameFilterLower ?? "")
                + "|" + (VPBConfig.Instance != null ? VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope) : "PathAndName")
                + "|" + (currentExtension ?? "")
                + "|" + CurrentPathsSignatureFragment()
                + "|" + (int)(st != null ? st.Type : 0)
                + "|" + (int)(st != null ? st.Direction : 0)
                + "|" + scale.ToString("R")
                + "|crR" + CreatorRatingRevisionFragment()
                + "|crF" + (creatorRatedOnlyFilter ? "1" : "0");
        }

        private void EnsureCreatorVirtScrollHook(bool isLeft, GameObject holder)
        {
            if (holder == null) return;
            ScrollRect sr = holder.GetComponentInParent<ScrollRect>();
            if (sr == null) return;

            if (isLeft)
            {
                _leftCreatorVirtScroll = sr;
                if (_leftCreatorVirtHooked) return;
                _leftCreatorVirtHooked = true;
                sr.onValueChanged.AddListener(_ =>
                {
                    try { UpdateCreatorVirtualVisible(true); } catch { }
                });
            }
            else
            {
                _rightCreatorVirtScroll = sr;
                if (_rightCreatorVirtHooked) return;
                _rightCreatorVirtHooked = true;
                sr.onValueChanged.AddListener(_ =>
                {
                    try { UpdateCreatorVirtualVisible(false); } catch { }
                });
            }
        }

        private float CreatorVirtRowHeight()
        {
            float s = ChromeScale;
            return (GalleryUiDesignTokens.SideTabRowHeightRef + GalleryUiDesignTokens.SideTabRowSpacingRef) * s;
        }

        /// <summary>Virtualized side-tab row stride (button height + gap). Same for creators and user tags.</summary>
        private float SideTabVirtRowStridePx()
        {
            return CreatorVirtRowHeight();
        }

        private static void SyncRoundedFractionOnTabButton(GameObject btn, float frac)
        {
            if (btn == null) return;
            RoundedRect rr = btn.GetComponent<RoundedRect>();
            if (rr != null) rr.cornerRadiusFraction = frac;
            // Category icon color chip (TabLeftIcon/Backdrop) — same fraction as rest of chrome.
            Transform iconBackdrop = btn.transform.Find("TabLeftIcon/Backdrop");
            if (iconBackdrop != null)
            {
                RoundedRect backdropRr = iconBackdrop.GetComponent<RoundedRect>();
                if (backdropRr != null) backdropRr.cornerRadiusFraction = frac;
            }
            ConfigureSideTabRowHoverBorder(btn);
        }

        private static void SyncRoundedFractionOnTabButtons(List<GameObject> buttons, float frac)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Count; i++)
                SyncRoundedFractionOnTabButton(buttons[i], frac);
        }

        private void SyncRoundedFractionOnTabButtonPool(float frac)
        {
            if (tabButtonPool == null || tabButtonPool.Count == 0) return;
            foreach (GameObject btn in tabButtonPool)
                SyncRoundedFractionOnTabButton(btn, frac);
        }

        internal void SyncLiveElementCornerRadiusChrome()
        {
            float frac = UI.ResolveGalleryElementCornerRadiusFraction();
            SyncRoundedFractionOnTabButtons(leftSubActiveTabButtons, frac);
            SyncRoundedFractionOnTabButtons(rightSubActiveTabButtons, frac);
            SyncRoundedFractionOnTabButtons(leftCategoryTabButtons, frac);
            SyncRoundedFractionOnTabButtons(rightCategoryTabButtons, frac);
            SyncRoundedFractionOnTabButtons(leftCreatorTabButtons, frac);
            SyncRoundedFractionOnTabButtons(rightCreatorTabButtons, frac);
            SyncRoundedFractionOnTabButtons(leftActiveTabButtons, frac);
            SyncRoundedFractionOnTabButtons(rightActiveTabButtons, frac);
            SyncRoundedFractionOnTabButtons(_leftCreatorVirtButtons, frac);
            SyncRoundedFractionOnTabButtons(_rightCreatorVirtButtons, frac);
            SyncRoundedFractionOnTabButtonPool(frac);
            try { ApplyCategoryQuickChromeLayout(ChromeScale); } catch { }
            try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
        }

        private static void ConfigureSideTabRowHoverBorder(GameObject btnGO)
        {
            if (btnGO == null) return;
            UIHoverBorder hb = btnGO.GetComponent<UIHoverBorder>();
            if (hb == null) return;
            hb.inward = true;
            try { hb.ApplyBorderSettings(); } catch { }
        }

        private static float SideTabRowHeightPx(float s)
        {
            return GalleryUiDesignTokens.SideTabRowHeightRef * s;
        }

        private static void ApplySideTabVirtRowHorizontalLayout(RectTransform rt, float s, float rowH)
        {
            if (rt == null) return;
            float pad = GalleryUiDesignTokens.SideTabRowPadRef * s;
            rt.sizeDelta = new Vector2(-pad, rowH);
            rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
            rt.offsetMax = new Vector2(-pad, rt.offsetMax.y);
        }

        private void EnsureSideTabVirtPool(List<GameObject> pool, Transform parent, int desired)
        {
            if (parent == null) return;
            if (desired < 8) desired = 8;

            // If we have buttons in the pool that are parented elsewhere (e.g. returned to shared pool),
            // we need to clear our local list and start fresh.
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null || pool[i].transform.parent != parent)
                {
                    pool.Clear();
                    break;
                }
            }

            while (pool.Count < desired)
            {
                // Create private buttons for the virtual list that are NOT part of the shared tabButtonPool.
                // This prevents UpdateTabs from stealing them back every frame.
                GameObject btnGO = UI.CreateUIButton(parent.gameObject,
                    GalleryUiDesignTokens.TabButtonPreferredWidthRef,
                    GalleryUiDesignTokens.SideTabRowHeightRef, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                AddHoverDelegate(btnGO);
                ConfigureSideTabRowHoverBorder(btnGO);
                btnGO.SetActive(true);

                RectTransform rt = btnGO.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float s = ChromeScale;
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = Vector2.zero;
                    ApplySideTabVirtRowHorizontalLayout(rt, s, SideTabRowHeightPx(s));
                }

                pool.Add(btnGO);
            }
        }

        private void EnsureCreatorVirtPool(bool isLeft, Transform parent, int desired)
        {
            EnsureSideTabVirtPool(isLeft ? _leftCreatorVirtButtons : _rightCreatorVirtButtons, parent, desired);
        }

        /// <summary>
        /// Binds a pooled button to a specific creator entry.
        /// </summary>
        private void BindCreatorVirtButton(GameObject btnGO, CreatorCacheEntry creator)
        {
            if (btnGO == null) return;
            string cName = creator.Name ?? "";
            // Selection highlight — not CreatorFilterContains (that is true for everyone when filter empty).
            bool isActive = ActiveFilterContainsCreatorSelection(cName);
            Color btnColor = isActive ? ColorCreator : ColorInactiveRow;
            string label = cName + " (" + creator.Count + ")";

            Button btnComp = btnGO.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                btnComp.onClick.AddListener(() =>
                {
                    CreatorRatingRowHandler.CloseAnyOpen();
                    ToggleCreatorFilter(cName);
                    OnCreatorFilterChanged(refreshFilesAndTabs: true);
                });
            }
            UIRightClickDelegate rightClickDelegate = btnGO.GetComponent<UIRightClickDelegate>();
            if (rightClickDelegate == null) rightClickDelegate = btnGO.AddComponent<UIRightClickDelegate>();
            rightClickDelegate.OnRightClick = () =>
            {
                CreatorRatingRowHandler.CloseAnyOpen();
                SaveCurrentCategoryFilterState(currentCategoryTitle, currentPath);
                ClearCreatorFilters();
                OnCreatorFilterChanged(refreshFilesAndTabs: true);
            };

            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = btnColor;

            float s = ChromeScale;
            Text txt = null;
            Transform textTr = btnGO.transform.Find("Text");
            if (textTr != null) txt = textTr.GetComponent<Text>();
            if (txt != null)
            {
                txt.text = label;
                GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le == null) le = btnGO.AddComponent<LayoutElement>();
            le.minWidth = GalleryUiDesignTokens.TabButtonMinWidthRef * s;
            le.preferredWidth = GalleryUiDesignTokens.TabButtonPreferredWidthRef * s;
            le.minHeight = SideTabRowHeightPx(s);
            le.preferredHeight = SideTabRowHeightPx(s);
            le.flexibleWidth = 1;

            BindCreatorRatingChrome(btnGO, cName);
        }

        /// <summary>
        /// Updates the visible creators in the virtualized list based on the current scroll position.
        /// </summary>
        private void UpdateCreatorVirtualVisible(bool isLeft)
        {
            if (_creatorVirtView == null) return;
            GameObject holder = isLeft ? leftCreatorTabHolder : rightCreatorTabHolder;
            if (holder == null || !holder.activeInHierarchy) return;

            ScrollRect sr = isLeft ? _leftCreatorVirtScroll : _rightCreatorVirtScroll;
            if (sr == null) sr = holder.GetComponentInParent<ScrollRect>();
            if (sr == null) return;

            float rowH = CreatorVirtRowHeight();
            if (rowH <= 1f) rowH = 37f;

            RectTransform viewport = sr.viewport != null ? sr.viewport : (sr.transform as RectTransform);
            float viewportH = viewport != null ? viewport.rect.height : 600f;

            int total = _creatorVirtView != null ? _creatorVirtView.Count : 0;
            LayoutElement holderLe = holder.GetComponent<LayoutElement>();
            if (total == 0)
            {
                if (isLeft) foreach (var b in _leftCreatorVirtButtons) b.SetActive(false);
                else foreach (var b in _rightCreatorVirtButtons) b.SetActive(false);
                if (holderLe != null) holderLe.preferredHeight = 0f;
                return;
            }
            float contentH = total * rowH;

            if (holderLe != null) holderLe.preferredHeight = contentH;

            // Compute scroll position in pixels.
            float scrollRange = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - Mathf.Clamp01(sr.verticalNormalizedPosition)) * scrollRange;
            int firstIdx = (rowH > 0f) ? Mathf.FloorToInt(scrollY / rowH) : 0;
            if (firstIdx < 0) firstIdx = 0;
            if (firstIdx > total - 1) firstIdx = Mathf.Max(0, total - 1);

            int visible = Mathf.CeilToInt(viewportH / rowH) + 10; // buffer
            EnsureCreatorVirtPool(isLeft, holder.transform, visible);

            List<GameObject> pool = isLeft ? _leftCreatorVirtButtons : _rightCreatorVirtButtons;
            if (isLeft) _leftCreatorVirtLastFirstIdx = firstIdx;
            else _rightCreatorVirtLastFirstIdx = firstIdx;

            for (int i = 0; i < pool.Count; i++)
            {
                int idx = firstIdx + i;
                GameObject btnGO = pool[i];
                if (btnGO == null) continue;

                if (idx >= 0 && idx < total)
                {
                    btnGO.SetActive(true);
                    BindCreatorVirtButton(btnGO, _creatorVirtView[idx]);

                    RectTransform rt = btnGO.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        float s = ChromeScale;
                        ApplySideTabVirtRowHorizontalLayout(rt, s, SideTabRowHeightPx(s));
                        float y = -idx * rowH;
                        rt.anchoredPosition = new Vector2(0f, y);
                    }
                }
                else
                {
                    btnGO.SetActive(false);
                }
            }
        }

        private string ComputeUserTagVirtDataSignature()
        {
            SortState st = GetSortState("UserTags");
            float scale = ChromeScale;
            string cat = currentCategoryTitle ?? "";
            if (titleText != null && string.IsNullOrEmpty(cat)) cat = titleText.text ?? "";
            return "v1|" + userTagSideTabDataRevision
                + "|" + (userTagFilter ?? "")
                + "|" + cat
                + "|" + (int)(st != null ? st.Type : 0)
                + "|" + (int)(st != null ? st.Direction : 0)
                + "|" + scale.ToString("R")
                + "|" + _userTagPinRevision
                + "|" + (int)_userTagAvailMode
                + "|" + (VPBConfig.Instance != null && VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode ? 1 : 0)
                + "|" + (_userTagShowUnusedBucket ? 1 : 0)
                + "|" + (userTagsCached ? 1 : 0)
                + "|sel:" + BuildUserTagSelectionVirtSignature();
        }

        private const int UserTagCreateRowCountSentinel = int.MinValue;
        private const int UserTagUnusedBucketHeaderSentinel = int.MinValue + 1;

        private void RebuildUserTagVirtViewList(bool isLeft, bool resetScrollToTop)
        {
            float offsetPx = 0f;
            if (!resetScrollToTop)
                offsetPx = TryGetUserTagAvailScrollOffsetPx(isLeft) ?? 0f;

            _userTagStickyRows.Clear();
            _userTagVirtView.Clear();
            var sortUt = GetSortState("UserTags");
            var rowsUt = new List<UserTagSideTabEntry>(cachedUserTagSideTab.Count);
            for (int i = 0; i < cachedUserTagSideTab.Count; i++)
                rowsUt.Add(cachedUserTagSideTab[i]);
            if (sortUt.Type == SortType.Count)
            {
                if (sortUt.Direction == SortDirection.Ascending)
                    rowsUt.Sort((a, b) => a.Count.CompareTo(b.Count));
                else
                    rowsUt.Sort((a, b) => b.Count.CompareTo(a.Count));
            }
            else
            {
                if (sortUt.Direction == SortDirection.Ascending)
                    rowsUt.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                else
                    rowsUt.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
            }
            string filterUt = userTagFilter ?? "";

            // "Create Tag" synthetic top row when user typed text in search box.
            // Uses Count sentinel so BindUserTagVirtButton can render different UI/behavior.
            if (!string.IsNullOrEmpty(filterUt))
            {
                string normCandidate = VpbLocalDatabase.NormalizeGalleryUserTagName(filterUt);
                if (!string.IsNullOrEmpty(normCandidate))
                {
                    bool alreadyExists = false;
                    for (int i = 0; i < rowsUt.Count; i++)
                    {
                        if (string.Equals(rowsUt[i].Name, normCandidate, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                    if (!alreadyExists)
                        _userTagStickyRows.Add(new UserTagSideTabEntry { Name = normCandidate, Count = UserTagCreateRowCountSentinel });
                }
            }

            bool hideUnused = _userTagAvailMode == UserTagAvailMode.FilterByTags
                && string.IsNullOrEmpty(filterUt)
                && VPBConfig.Instance != null
                && VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode
                && _userTagSideTabCountsReady
                && _userTagAnyAssignmentExists;

            var filteredUt = new List<UserTagSideTabEntry>(rowsUt.Count);
            int unusedHiddenCount = 0;

            for (int ui = 0; ui < rowsUt.Count; ui++)
            {
                UserTagSideTabEntry ut = rowsUt[ui];
                if (string.IsNullOrEmpty(ut.Name)) continue;
                if (!string.IsNullOrEmpty(filterUt) && ut.Name.IndexOf(filterUt, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (hideUnused && ShouldHideUnusedUserTagInFilterAvailList(ut))
                {
                    unusedHiddenCount++;
                    continue;
                }

                // Expanded unused bucket: still hide from main path via ShouldHide=false when expanded;
                // when collapsed we counted above. When expanded, zero-count rows flow into filteredUt.
                filteredUt.Add(ut);
            }

            // Collapsed Unused bucket header (only when hide-unused active and some zeros hidden).
            if (hideUnused && !_userTagShowUnusedBucket && unusedHiddenCount > 0)
            {
                filteredUt.Add(new UserTagSideTabEntry
                {
                    Name = string.Format(
                        VPBTranslation.T("gallery.usertags.unused_bucket", "Unused ({0})"),
                        unusedHiddenCount),
                    Count = UserTagUnusedBucketHeaderSentinel
                });
            }
            else if (hideUnused && _userTagShowUnusedBucket)
            {
                // Header stays at end of used section: insert before zero-count rows by partitioning.
                var usedOnly = new List<UserTagSideTabEntry>(filteredUt.Count);
                var unusedOnly = new List<UserTagSideTabEntry>(16);
                for (int i = 0; i < filteredUt.Count; i++)
                {
                    UserTagSideTabEntry e = filteredUt[i];
                    if (e.Count <= 0
                        && e.Count != UserTagCreateRowCountSentinel
                        && e.Count != UserTagUnusedBucketHeaderSentinel
                        && !UserTagNameIsInIncludeOrExcludeFilter(e.Name))
                    {
                        UserTagSelectionState st = GetUserTagSelectionState(e.Name);
                        if (st == UserTagSelectionState.Off)
                        {
                            unusedOnly.Add(e);
                            continue;
                        }
                    }
                    usedOnly.Add(e);
                }
                filteredUt.Clear();
                for (int i = 0; i < usedOnly.Count; i++) filteredUt.Add(usedOnly[i]);
                filteredUt.Add(new UserTagSideTabEntry
                {
                    Name = string.Format(
                        VPBTranslation.T("gallery.usertags.unused_bucket_hide", "Hide unused ({0})"),
                        unusedOnly.Count),
                    Count = UserTagUnusedBucketHeaderSentinel
                });
                for (int i = 0; i < unusedOnly.Count; i++) filteredUt.Add(unusedOnly[i]);
            }

            EnsureSelectionUserTagsInAvailList(filteredUt);
            var pinnedUt = new List<UserTagSideTabEntry>(8);
            var normalUt = new List<UserTagSideTabEntry>(filteredUt.Count);
            PartitionUserTagRowsPinnedFirst(filteredUt, pinnedUt, normalUt);
            for (int pi = 0; pi < pinnedUt.Count; pi++) _userTagStickyRows.Add(pinnedUt[pi]);
            for (int ni = 0; ni < normalUt.Count; ni++) _userTagVirtView.Add(normalUt[ni]);

            if (resetScrollToTop)
            {
                try
                {
                    ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
                    if (sr != null) sr.verticalNormalizedPosition = 1f;
                }
                catch { }
            }
            else
                RequestRestoreUserTagAvailScrollPx(isLeft, offsetPx);
        }

        private GameObject EnsureUserTagPickVirtualHolder(Transform parent)
        {
            if (parent == null) return null;
            Transform t = parent.Find("_VPB_UserTagPick_Virt");
            if (t != null) return t.gameObject;
            return CreateCreatorVirtualHolder("_VPB_UserTagPick_Virt", parent);
        }

        private void EnsureUserTagVirtScrollHook(bool isLeft, GameObject holder)
        {
            if (holder == null) return;
            ScrollRect sr = holder.GetComponentInParent<ScrollRect>();
            if (sr == null) return;
            if (isLeft)
            {
                _leftUserTagVirtScroll = sr;
                EnsureUserTagScrollStepButtons(true, sr);
                if (_leftUserTagVirtHooked) return;
                _leftUserTagVirtHooked = true;
                sr.onValueChanged.AddListener(OnUserTagVirtScrollLeft);
            }
            else
            {
                _rightUserTagVirtScroll = sr;
                EnsureUserTagScrollStepButtons(false, sr);
                if (_rightUserTagVirtHooked) return;
                _rightUserTagVirtHooked = true;
                sr.onValueChanged.AddListener(OnUserTagVirtScrollRight);
            }
        }

        private void EnsureUserTagScrollStepButtons(bool isLeft, ScrollRect sr)
        {
            if (sr == null || sr.gameObject == null) return;
            if (!ShouldShowGalleryScrollStepButtons())
            {
                SetUserTagScrollStepButtonsActive(isLeft, false);
                return;
            }
            Transform sb = null;
            try { sb = sr.gameObject.transform.Find("Scrollbar"); } catch { sb = null; }
            if (sb == null) return;

            GameObject up = isLeft ? leftUserTagScrollStepUpBtn : rightUserTagScrollStepUpBtn;
            GameObject down = isLeft ? leftUserTagScrollStepDownBtn : rightUserTagScrollStepDownBtn;
            if (up == null)
            {
                up = UI.CreateUIButton(sb.gameObject, 40, 40, "▲", 22, 0, 0, AnchorPresets.middleCenter, () => ScrollUserTagPanelStep(isLeft, 1f));
                up.name = isLeft ? "LeftUserTagScrollStepUp" : "RightUserTagScrollStepUp";
                { var s = UI.LoadIconSprite("vpb_icons/chevron_up.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(up, s); }
                AddHoverDelegate(up);
                AddTooltip(up, "gallery.tooltip.usertags_scroll_up", "Scroll tags up");
                if (isLeft) leftUserTagScrollStepUpBtn = up; else rightUserTagScrollStepUpBtn = up;
            }
            if (down == null)
            {
                down = UI.CreateUIButton(sb.gameObject, 40, 40, "▼", 22, 0, 0, AnchorPresets.middleCenter, () => ScrollUserTagPanelStep(isLeft, -1f));
                down.name = isLeft ? "LeftUserTagScrollStepDown" : "RightUserTagScrollStepDown";
                { var s = UI.LoadIconSprite("vpb_icons/chevron_down.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(down, s); }
                AddHoverDelegate(down);
                AddTooltip(down, "gallery.tooltip.usertags_scroll_down", "Scroll tags down");
                if (isLeft) leftUserTagScrollStepDownBtn = down; else rightUserTagScrollStepDownBtn = down;
            }

            LayoutUserTagScrollStepButtons(up, down);
            SetUserTagScrollStepButtonsActive(isLeft, true);
        }

        private void SetUserTagScrollStepButtonsActive(bool isLeft, bool active)
        {
            GameObject up = isLeft ? leftUserTagScrollStepUpBtn : rightUserTagScrollStepUpBtn;
            GameObject down = isLeft ? leftUserTagScrollStepDownBtn : rightUserTagScrollStepDownBtn;
            if (up != null) up.SetActive(active);
            if (down != null) down.SetActive(active);
        }

        private void LayoutUserTagScrollStepButtons(GameObject up, GameObject down)
        {
            float paneS = ChromeScale;
            float btnSz = Mathf.Round(Mathf.Clamp(40f * paneS, 28f, 56f));
            const float gap = 6f;
            GameObject[] gos = { up, down };
            for (int i = 0; i < gos.Length; i++)
            {
                GameObject go = gos[i];
                if (go == null) continue;
                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt == null) continue;
                bool isUp = i == 0;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, isUp ? 1f : 0f);
                rt.pivot = new Vector2(0.5f, isUp ? 1f : 0f);
                rt.sizeDelta = new Vector2(btnSz, btnSz);
                rt.anchoredPosition = new Vector2(0f, isUp ? -gap : gap);
                SyncScrollbarJumpButtonCollider(go);
            }
        }

        private void ScrollUserTagPanelStep(bool isLeft, float direction)
        {
            ScrollRect sr = isLeft ? _leftUserTagVirtScroll : _rightUserTagVirtScroll;
            ScrollRectByConfiguredStep(sr, direction);
            try
            {
                if (isLeft) OnUserTagVirtScrollLeft(Vector2.zero);
                else OnUserTagVirtScrollRight(Vector2.zero);
            }
            catch { }
        }

        private void OnUserTagVirtScrollLeft(UnityEngine.Vector2 _)
        {
            try
            {
                if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.UserTagScrollCb++;
                if (leftTabContainerGO == null || leftActiveContent != ContentType.UserTags) return;
                UpdateUserTagVirtualVisible(true, UserTagStateOnColor, leftTabContainerGO.transform, fromScroll: true);
            }
            catch { }
        }

        private void OnUserTagVirtScrollRight(UnityEngine.Vector2 _)
        {
            try
            {
                if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.UserTagScrollCb++;
                if (rightTabContainerGO == null || rightActiveContent != ContentType.UserTags) return;
                UpdateUserTagVirtualVisible(false, UserTagStateOnColor, rightTabContainerGO.transform, fromScroll: true);
            }
            catch { }
        }

        private void EnsureUserTagVirtPool(bool isLeft, Transform parent, int desired)
        {
            EnsureSideTabVirtPool(isLeft ? _leftUserTagVirtButtons : _rightUserTagVirtButtons, parent, desired);
        }

        private void BindUserTagVirtButton(GameObject btnGO, UserTagSideTabEntry ut, Color utAccent, string pickTooltip, bool isLeft)
        {
            if (btnGO == null) return;
            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.UserTagBind++;
            const string CreateLabelKey = "gallery.usertags.create_from_search";
            const string CreateTipKey = "gallery.usertags.create_from_search_tip";

            string tagSnap = ut.Name ?? "";
            bool isCreateRow = ut.Count == UserTagCreateRowCountSentinel;
            bool isUnusedBucketHeader = ut.Count == UserTagUnusedBucketHeaderSentinel;
            UserTagSelectionState state = (isCreateRow || isUnusedBucketHeader)
                ? UserTagSelectionState.Off
                : GetUserTagSelectionState(tagSnap);
            bool isFilterActive = !isCreateRow && !isUnusedBucketHeader
                && activeUserTags != null
                && activeUserTags.Contains(tagSnap);
            bool isFilterExcluded = !isCreateRow && !isUnusedBucketHeader
                && excludedUserTags != null
                && excludedUserTags.Contains(tagSnap);
            bool isPulsing = !isCreateRow && !isUnusedBucketHeader
                && !string.IsNullOrEmpty(_userTagPulseTag)
                && string.Equals(_userTagPulseTag, tagSnap, StringComparison.OrdinalIgnoreCase)
                && Time.unscaledTime < _userTagPulseUntil;
            bool hasGridSelection = selectedFiles != null && selectedFiles.Count > 0;
            bool isOnSelection = !isCreateRow && !isUnusedBucketHeader
                && (state == UserTagSelectionState.On || state == UserTagSelectionState.Mixed);
            bool preferSelectionColor = hasGridSelection && isOnSelection && !isFilterActive && !isFilterExcluded;
            // Resting (fully-inactive) slot is tinted by the tag's category color when assigned (US-02);
            // filter/selection states above still take visual priority.
            Color restingRowColor = ColorInactiveRow;
            if (!isCreateRow && !isUnusedBucketHeader)
            {
                Color? catCol = TryGetUserTagCategoryColor(tagSnap);
                if (catCol.HasValue) restingRowColor = catCol.Value;
            }
            Color btnColor = isCreateRow ? new Color(0.25f, 0.45f, 0.28f, 1f)
                : (isUnusedBucketHeader ? new Color(0.28f, 0.28f, 0.32f, 1f)
                : (isFilterExcluded ? UserTagFilterExcludedColor
                : (isFilterActive ? UserTagFilterActiveColor
                : (preferSelectionColor ? UserTagStateOnColor
                : (isPulsing ? UserTagStatePulseColor
                : (state == UserTagSelectionState.On ? UserTagStateOnColor
                : (state == UserTagSelectionState.Mixed ? UserTagStateMixedColor : restingRowColor)))))));
            string labelUt = isCreateRow
                ? (VPBTranslation.T(CreateLabelKey, "Create Tag") + ": " + tagSnap)
                : (isUnusedBucketHeader ? tagSnap : (tagSnap + " (" + ut.Count + ")"));

            Button btnComp = btnGO.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                bool sideLeft = isLeft;
                btnComp.onClick.AddListener(() =>
                {
                    try
                    {
                        UserTagPickDragSource dragSrc = btnGO.GetComponent<UserTagPickDragSource>();
                        if (dragSrc != null && dragSrc.ConsumedByDrag) return;
                        if (isUnusedBucketHeader)
                        {
                            _userTagShowUnusedBucket = !_userTagShowUnusedBucket;
                            _userTagVirtViewSig = null;
                            try { RefreshUserTagsAvailPaneInPlace(sideLeft); } catch { }
                            return;
                        }
                        if (isCreateRow)
                        {
                            if (VpbLocalDatabase.TryEnsureGalleryUserTagInVocabulary(tagSnap, out string norm) && !string.IsNullOrEmpty(norm))
                            {
                                userTagsCached = false;
                                _userTagVirtViewSig = null;
                                // Drop list filter so new tag joins full avail list (not stuck matching typed text).
                                ClearUserTagSideListFilter(sideLeft);
                                // Fresh vocabulary rows have Count=0; default Filter Mode hide-unused omits them.
                                // Land in Tag Mode so Create tag row is visible (same as editor CreateTagRows).
                                if (_userTagAvailMode == UserTagAvailMode.FilterByTags
                                    && VPBConfig.Instance != null
                                    && VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode)
                                {
                                    try { RequestUserTagWorkMode(UserTagAvailMode.Tag); }
                                    catch { try { RefreshUserTagsAvailPaneInPlace(sideLeft); } catch { } }
                                    return;
                                }
                            }
                        }
                        else if (_userTagAvailMode == UserTagAvailMode.FilterByTags)
                        {
                            // Tap: Off ↔ Include. Exclude via right-click or drag to title Excl row.
                            if (activeUserTags.Contains(tagSnap))
                            {
                                activeUserTags.Remove(tagSnap);
                            }
                            else
                            {
                                activeUserTags.Add(tagSnap);
                                excludedUserTags.Remove(tagSnap);
                            }
                            try { BridgeTitleSearchTagChipFromFilterSet(tagSnap); } catch { }
                            RefreshFiles(true, false, false, "user_tag_filter_toggle");
                        }
                        else
                        {
                            toggleTagForSelectedItems(tagSnap);
                        }

                        RefreshUserTagsAvailPaneInPlace(sideLeft);
                    }
                    catch { }
                });
            }
            UIRightClickDelegate rightClickDelegate = btnGO.GetComponent<UIRightClickDelegate>();
            if (rightClickDelegate == null) rightClickDelegate = btnGO.AddComponent<UIRightClickDelegate>();
            rightClickDelegate.OnRightClick = null;
            if (!isCreateRow && !isUnusedBucketHeader && _userTagAvailMode == UserTagAvailMode.FilterByTags)
            {
                bool sideLeftRc = isLeft;
                rightClickDelegate.OnRightClick = () =>
                {
                    try
                    {
                        // Right-click cycles the exclude (none-of) state for this tag.
                        if (excludedUserTags.Contains(tagSnap))
                            excludedUserTags.Remove(tagSnap);
                        else
                        {
                            excludedUserTags.Add(tagSnap);
                            activeUserTags.Remove(tagSnap);
                        }
                        try { BridgeTitleSearchTagChipFromFilterSet(tagSnap); } catch { }
                        RefreshFiles(true, false, false, "user_tag_filter_exclude_toggle");
                        RefreshUserTagsAvailPaneInPlace(sideLeftRc);
                    }
                    catch { }
                };
            }

            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = btnColor;

            float s = ChromeScale;
            Text txt = btnGO.GetComponentInChildren<Text>();
            if (txt != null)
            {
                GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Truncate;
                txt.resizeTextForBestFit = false;
                RectTransform txtRt = txt.GetComponent<RectTransform>();
                float pinReserve = (isCreateRow || isUnusedBucketHeader) ? 0f : 34f * s;
                float filterReserve = isFilterActive ? 34f * s : 0f;
                if (txtRt != null)
                {
                    txtRt.offsetMin = new Vector2(pinReserve, txtRt.offsetMin.y);
                    txtRt.offsetMax = new Vector2(-filterReserve, txtRt.offsetMax.y);
                }
                RectTransform btnRtUt = btnGO.GetComponent<RectTransform>();
                float padUt = 10f * s;
                float iconReserve = pinReserve + filterReserve;
                float btnW = btnRtUt != null ? btnRtUt.rect.width : 0f;
                // Sticky Create row often binds before stretch; prefer parent strip width over ~170 create size.
                float preferW = GalleryUiDesignTokens.TabButtonPreferredWidthRef * s;
                if (btnW <= preferW + 1f)
                {
                    RectTransform parentRt = btnGO.transform.parent as RectTransform;
                    if (parentRt != null && parentRt.rect.width > btnW + 1f)
                        btnW = parentRt.rect.width - (GalleryUiDesignTokens.SideTabRowPadRef * 2f * s);
                }
                float innerUt = btnW - padUt - iconReserve;
                if (innerUt <= 2f) innerUt = preferW;
                string shownUt = isCreateRow
                    ? EllipsizeCreateTagLabel(txt, VPBTranslation.T(CreateLabelKey, "Create Tag") + ": ", tagSnap, innerUt)
                    : EllipsizeTextPreferredWidth(txt, labelUt, innerUt);
                txt.text = shownUt;
            }
            SyncUserTagRowFilterIcon(btnGO, isFilterActive, s);
            SyncUserTagRowPinButton(btnGO, tagSnap, isCreateRow || isUnusedBucketHeader, s, isLeft, appliedRow: false, availSelectionState: state);

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le == null) le = btnGO.AddComponent<LayoutElement>();
            le.minWidth = GalleryUiDesignTokens.TabButtonMinWidthRef * s;
            le.preferredWidth = GalleryUiDesignTokens.TabButtonPreferredWidthRef * s;
            le.minHeight = SideTabRowHeightPx(s);
            le.preferredHeight = SideTabRowHeightPx(s);
            le.flexibleWidth = 1;

            if (txt != null)
            {
                bool tr = !string.Equals(txt.text, labelUt, StringComparison.Ordinal);
                string tip = isCreateRow
                    ? VPBTranslation.T(CreateTipKey, "Create new tag from search text (adds to database, selects tag).")
                    : (isUnusedBucketHeader
                        ? VPBTranslation.T("gallery.usertags.unused_bucket_tip", "Show or hide tags with no items in this category.")
                        : pickTooltip);
                if (tr && !string.IsNullOrEmpty(tip))
                    AddTooltipPlain(btnGO, labelUt + "\n\n" + tip);
                else if (tr)
                    AddTooltipPlain(btnGO, labelUt);
                else if (!string.IsNullOrEmpty(tip))
                    AddTooltipPlain(btnGO, tip);
            }
            else
            {
                string tip = isCreateRow
                    ? VPBTranslation.T(CreateTipKey, "Create new tag from search text (adds to database, selects tag).")
                    : (isUnusedBucketHeader
                        ? VPBTranslation.T("gallery.usertags.unused_bucket_tip", "Show or hide tags with no items in this category.")
                        : pickTooltip);
                if (!string.IsNullOrEmpty(tip))
                    AddTooltipPlain(btnGO, tip);
            }

            UserTagPickDragSource dr = btnGO.GetComponent<UserTagPickDragSource>();
            if (dr == null) dr = btnGO.AddComponent<UserTagPickDragSource>();
            dr.Panel = this;
            dr.PrimaryTag = (isCreateRow || isUnusedBucketHeader) ? "" : tagSnap;
            dr.IsAppliedRowDrag = false;
            dr.DetailStripAppliedReorder = false;
        }

        private void UpdateUserTagVirtualVisible(bool isLeft, Color utAccent, Transform tabContainer, bool fromScroll = false)
        {
            if (tabContainer == null || !IsUserTagsSideTabOpen(isLeft)) return;
            GameObject holderGo = EnsureUserTagPickVirtualHolder(tabContainer);
            if (holderGo == null) return;

            ScrollRect sr = isLeft ? _leftUserTagVirtScroll : _rightUserTagVirtScroll;
            if (sr == null) sr = holderGo.GetComponentInParent<ScrollRect>();
            if (sr == null) return;

            string pickTip = _userTagAvailMode == UserTagAvailMode.FilterByTags
                ? GetUserTagPickRowTooltipFilter()
                : VPBTranslation.T("gallery.usertags.pick_row_tooltip", "Click: toggle this tag on selected item(s). Drag to Applied below.");
            float rowH = SideTabVirtRowStridePx();
            if (rowH <= 1f) rowH = 37f;

            RectTransform viewport = sr.viewport != null ? sr.viewport : (sr.transform as RectTransform);
            float viewportH = MeasureUserTagVirtViewportHeight(viewport, rowH);

            int total = _userTagVirtView != null ? _userTagVirtView.Count : 0;
            LayoutElement holderLe = holderGo.GetComponent<LayoutElement>();
            if (total == 0)
            {
                if (isLeft) foreach (var b in _leftUserTagVirtButtons) { if (b != null) b.SetActive(false); }
                else foreach (var b in _rightUserTagVirtButtons) { if (b != null) b.SetActive(false); }
                if (holderLe != null) holderLe.preferredHeight = 0f;
                return;
            }
            float contentH = total * rowH;
            // Collapsed Mask after sticky over-inset: keep content height, skip bind (layout co rebinds after clamp).
            if (viewportH <= 0.5f)
            {
                if (holderLe != null) holderLe.preferredHeight = contentH;
                return;
            }

            float scrollRange = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - Mathf.Clamp01(sr.verticalNormalizedPosition)) * scrollRange;
            int firstIdx = (rowH > 0f) ? Mathf.FloorToInt(scrollY / rowH) : 0;
            if (firstIdx < 0) firstIdx = 0;
            if (firstIdx > total - 1) firstIdx = Mathf.Max(0, total - 1);

            int visible = Mathf.CeilToInt(viewportH / rowH) + 6;

            // onValueChanged fires every frame from pointer micro-jitter; rebind only when the visible window shifts. Forced repaints pass fromScroll=false.
            if (fromScroll
                && firstIdx == (isLeft ? _lastUserTagVirtFirstIdxLeft : _lastUserTagVirtFirstIdxRight)
                && visible == (isLeft ? _lastUserTagVirtVisibleLeft : _lastUserTagVirtVisibleRight)
                && total == (isLeft ? _lastUserTagVirtTotalLeft : _lastUserTagVirtTotalRight))
                return;
            if (isLeft) { _lastUserTagVirtFirstIdxLeft = firstIdx; _lastUserTagVirtVisibleLeft = visible; _lastUserTagVirtTotalLeft = total; }
            else { _lastUserTagVirtFirstIdxRight = firstIdx; _lastUserTagVirtVisibleRight = visible; _lastUserTagVirtTotalRight = total; }
            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.UserTagVirtVis++;

            // Below the gate so a skipped scroll callback doesn't re-dirty layout; forced and window-shift rebinds still set it.
            if (holderLe != null) holderLe.preferredHeight = contentH;

            EnsureUserTagVirtPool(isLeft, holderGo.transform, visible);

            List<GameObject> pool = isLeft ? _leftUserTagVirtButtons : _rightUserTagVirtButtons;
            float btnH = SideTabRowHeightPx(ChromeScale);
            for (int i = 0; i < pool.Count; i++)
            {
                int idx = firstIdx + i;
                GameObject btnGO = pool[i];
                if (btnGO == null) continue;
                if (idx >= 0 && idx < total)
                {
                    btnGO.SetActive(true);
                    BindUserTagVirtButton(btnGO, _userTagVirtView[idx], utAccent, pickTip, isLeft);
                    RectTransform rt = btnGO.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        float s = ChromeScale;
                        ApplySideTabVirtRowHorizontalLayout(rt, s, btnH);
                        float y = -idx * rowH;
                        rt.anchoredPosition = new Vector2(0f, y);
                    }
                }
                else
                    btnGO.SetActive(false);
            }
        }

        private string CurrentPathsSignatureFragment()
        {
            if (currentPaths == null || currentPaths.Count == 0)
                return currentPath ?? "";
            var arr = new List<string>(currentPaths);
            arr.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("\x1e", arr.ToArray());
        }

        private string ComputeCategorySideTabSignature()
        {
            SortState st = GetSortState("Category");
            float scale = ChromeScale;
            return categorySideTabDataRevision + "|" + (categoryFilter ?? "") + "|" + (currentPath ?? "") + "|" + (currentExtension ?? "") + "|" + (currentCreator ?? "") + "|" + (int)st.Type + "|" + (int)st.Direction + "|" + scale.ToString("R") + "|" + (categories != null ? categories.Count : 0);
        }

        private string ComputeCreatorSideTabSignature()
        {
            SortState st = GetSortState("Creator");
            float scale = ChromeScale;
            return creatorSideTabDataRevision + CreatorConsolidationSignatureFragment() + "|" + (creatorFilter ?? "") + "|" + CurrentPathsSignatureFragment() + "|" + (currentExtension ?? "") + "|" + (currentCreator ?? "") + "|" + (int)st.Type + "|" + (int)st.Direction + "|" + scale.ToString("R") + "|crR" + CreatorRatingRevisionFragment() + "|crF" + (creatorRatedOnlyFilter ? "1" : "0");
        }


        /// <summary>All/Addon/Custom row order from persisted <c>SceneSource</c> sort (same 4 modes as icon cycle). Unreferenced: Task 8 replaced BuildSceneSourceTabs with a single toggle.</summary>
        private List<string> GetOrderedSceneSourceFilterLabels()
        {
            SortState st = GetSortState("SceneSource");
            int mode = TryGetSidePaneFourModeIndex(st);
            if (mode < 0) mode = 0;
            switch (mode)
            {
                case 1:
                    return new List<string> { "Custom Scenes", "Addon Scenes", "All Scenes" };
                case 2:
                    return new List<string> { "Addon Scenes", "All Scenes", "Custom Scenes" };
                case 3:
                    return new List<string> { "Custom Scenes", "All Scenes", "Addon Scenes" };
                default:
                    return new List<string> { "All Scenes", "Addon Scenes", "Custom Scenes" };
            }
        }

        private void UpdateTabs(ContentType contentType, GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;

            foreach (var btn in trackedButtons)
            {
                ReturnTabButton(btn);
            }
            trackedButtons.Clear();

            CleanupSideTabLabeledRows(container.transform);
            DestroyEphemeralSideTabBlocksForContentType(container.transform, contentType);

            if (contentType == ContentType.Category)
            {
                BuildCategoryTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.Creator)
            {
                BuildCreatorTabs(container, isLeft);
            }
            else if (contentType == ContentType.Path)
            {
                BuildPathTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.UserTags)
            {
                BuildUserTagsTabs(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.UserTagsApplied)
            {
                BuildUserTagsAppliedTabs(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.History)
            {
                BuildHistoryTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.Settings)
            {
                BuildSettingsTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.Ratings)
            {
                BuildRatingsTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.AppearanceSource)
            {
                BuildAppearanceSourceTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.Size)
            {
                BuildSizeTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.CleanupCategories)
            {
                BuildCleanupCategoriesTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.CleanupStaleBuckets)
            {
                BuildCleanupStaleBucketsTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.SceneSource)
            {
                BuildSceneSourceTabs(container, trackedButtons);
            }
            else if (contentType == ContentType.Tags)
            {
                BuildTagsTabs(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.RemoveClothing)
            {
                BuildRemoveClothingTabs(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.RemoveHair)
            {
                BuildRemoveHairTabs(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.RemoveAtom)
            {
                BuildRemoveAtomTabs(container, trackedButtons, isLeft);
            }
            else if (contentType == ContentType.SavePresets)
            {
                var options = BuildSaveMenuOptions();
                Color saveColor = UI.ChromePanel;
                Color cancelColor = ColorCancelRow;

                AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);

                foreach (var opt in options)
                {
                    var o = opt;
                    CreateTabButton(container.transform, o.Label, saveColor, false, () => {
                        o.Action?.Invoke();
                        if (o.AutoClose) CloseSidePane(isLeft);
                    }, trackedButtons, null, o.Tooltip);
                }

                if (options.Count > 0)
                {
                    AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);
                }
            }
            
            SetLayerRecursive(container, 5);
        }


        /// <summary>Strip ephemeral User Tags UI blocks when rebuilding a different tab in the same scroll content.</summary>
        private void DestroyEphemeralSideTabBlocksForContentType(Transform container, ContentType contentType)
        {
            if (container == null) return;
            bool mainLeft = leftTabContainerGO != null && container == leftTabContainerGO.transform;
            bool mainRight = rightTabContainerGO != null && container == rightTabContainerGO.transform;
            bool subLeft = leftSubTabContainerGO != null && container == leftSubTabContainerGO.transform;
            bool subRight = rightSubTabContainerGO != null && container == rightSubTabContainerGO.transform;

            if (contentType != ContentType.UserTags)
            {
                DestroyChildIfPresent(container, "VPB_UserTagBulkBlock");
                DestroyChildIfPresent(container, "VPB_UserTagBulkBlock_v2");
                DestroyChildIfPresent(container, "VPB_UserTagBulkBlock_v3");
                DestroyChildIfPresent(container, "_VPB_UserTagPick_Virt");
                TeardownUserTagPickVirtForContainer(container);
                if (mainLeft && leftUserTagsAvailStickyGO != null)
                {
                    DestroyChildIfPresent(leftUserTagsAvailStickyGO.transform, "VPB_UserTagBulkBlock_v3");
                    leftUserTagAvailTitleText = null;
                    leftUserTagApplyBtnText = null;
                }
                if (mainRight && rightUserTagsAvailStickyGO != null)
                {
                    DestroyChildIfPresent(rightUserTagsAvailStickyGO.transform, "VPB_UserTagBulkBlock_v3");
                    rightUserTagAvailTitleText = null;
                    rightUserTagApplyBtnText = null;
                }
            }
            if (contentType != ContentType.UserTagsApplied)
            {
                DestroyChildIfPresent(container, "VPB_UserTagsAppliedToolbar_v1");
                DestroyChildIfPresent(container, "VPB_UserTagsAppliedToolbar_v2");
                DestroyChildIfPresent(container, "VPB_UserTagsAppliedToolbar_v3");
                if (subLeft && leftUserTagsAppliedStickyGO != null)
                {
                    DestroyChildIfPresent(leftUserTagsAppliedStickyGO.transform, "VPB_UserTagsAppliedToolbar_v2");
                    DestroyChildIfPresent(leftUserTagsAppliedStickyGO.transform, "VPB_UserTagsAppliedToolbar_v3");
                    leftUserTagAppliedTitleText = null;
                }
                if (subRight && rightUserTagsAppliedStickyGO != null)
                {
                    DestroyChildIfPresent(rightUserTagsAppliedStickyGO.transform, "VPB_UserTagsAppliedToolbar_v2");
                    DestroyChildIfPresent(rightUserTagsAppliedStickyGO.transform, "VPB_UserTagsAppliedToolbar_v3");
                    rightUserTagAppliedTitleText = null;
                }
            }
        }

        private void TeardownUserTagPickVirtForContainer(Transform container)
        {
            if (container == null) return;
            if (leftTabContainerGO != null && container == leftTabContainerGO.transform)
                TeardownUserTagPickVirt(true);
            else if (rightTabContainerGO != null && container == rightTabContainerGO.transform)
                TeardownUserTagPickVirt(false);
        }

        private void TeardownUserTagPickVirt(bool isLeft)
        {
            if (isLeft)
            {
                if (_leftUserTagVirtScroll != null && _leftUserTagVirtHooked)
                {
                    try { _leftUserTagVirtScroll.onValueChanged.RemoveListener(OnUserTagVirtScrollLeft); } catch { }
                }
                _leftUserTagVirtButtons.Clear();
                _leftUserTagVirtScroll = null;
                _leftUserTagVirtHooked = false;
            }
            else
            {
                if (_rightUserTagVirtScroll != null && _rightUserTagVirtHooked)
                {
                    try { _rightUserTagVirtScroll.onValueChanged.RemoveListener(OnUserTagVirtScrollRight); } catch { }
                }
                _rightUserTagVirtButtons.Clear();
                _rightUserTagVirtScroll = null;
                _rightUserTagVirtHooked = false;
            }
            // Shared virt view — only wipe when neither rail still owns User Tags pick list.
            bool otherStillOpen = isLeft
                ? (rightActiveContent == ContentType.UserTags && rightTabContainerGO != null)
                : (leftActiveContent == ContentType.UserTags && leftTabContainerGO != null);
            if (!otherStillOpen)
            {
                _userTagVirtView.Clear();
                _userTagVirtViewSig = null;
            }
        }

        private static void DestroyChildIfPresent(Transform container, string childName)
        {
            Transform ch = container.Find(childName);
            if (ch != null)
                UnityEngine.Object.Destroy(ch.gameObject);
        }

        /// <summary>Legacy UI Text: shrink with "..." when wider than <paramref name="maxInnerWidth"/> (uses <see cref="Text.preferredWidth"/>).</summary>
        private static string EllipsizeTextPreferredWidth(Text txt, string fullLabel, float maxInnerWidth)
        {
            if (txt == null || string.IsNullOrEmpty(fullLabel)) return fullLabel ?? "";
            if (maxInnerWidth <= 4f) return fullLabel;
            txt.text = fullLabel;
            try
            {
                if (txt.preferredWidth <= maxInnerWidth) return fullLabel;
            }
            catch { return fullLabel; }

            const string ell = "...";
            for (int n = fullLabel.Length; n >= 0; n--)
            {
                string trial = n <= 0 ? ell : fullLabel.Substring(0, n) + ell;
                txt.text = trial;
                try
                {
                    if (txt.preferredWidth <= maxInnerWidth) return trial;
                }
                catch { }
            }
            return ell;
        }

        /// <summary>
        /// Keep "Create Tag: " prefix; ellipsize typed name only (recognition of draft tag beats truncating verb).
        /// </summary>
        private static string EllipsizeCreateTagLabel(Text txt, string prefix, string tagName, float maxInnerWidth)
        {
            string name = tagName ?? "";
            string full = (prefix ?? "") + name;
            if (txt == null) return full;
            if (maxInnerWidth <= 4f) return full;
            txt.text = full;
            try
            {
                if (txt.preferredWidth <= maxInnerWidth) return full;
            }
            catch { return full; }

            string p = prefix ?? "";
            if (string.IsNullOrEmpty(p))
                return EllipsizeTextPreferredWidth(txt, full, maxInnerWidth);

            txt.text = p;
            float prefixW;
            try { prefixW = txt.preferredWidth; }
            catch { return EllipsizeTextPreferredWidth(txt, full, maxInnerWidth); }

            float remain = maxInnerWidth - prefixW;
            if (remain < 12f)
                return EllipsizeTextPreferredWidth(txt, full, maxInnerWidth);

            return p + EllipsizeTextPreferredWidth(txt, name, remain);
        }

        /// <summary>Clear User Tags side-list search after Create Tag so full vocabulary shows again.</summary>
        private void ClearUserTagSideListFilter(bool isLeft)
        {
            userTagFilter = "";
            InputField input = isLeft ? leftSearchInput : rightSearchInput;
            UnityAction<string> handler = isLeft
                ? _leftMainSideSearchOnValueChanged
                : _rightMainSideSearchOnValueChanged;
            SetSideSearchInputTextWithoutNotify(input, "", handler);
        }

        private GameObject CreateTabButton(Transform parent, string label, Color color, bool isActive, UnityAction onClick, List<GameObject> targetList, UnityAction onRightClick = null, string tooltip = null, string userTagAppliedDragPrimary = null, TextAnchor labelAnchor = TextAnchor.MiddleCenter, float labelInsetLeft = 0f, float labelInsetRight = 0f, Sprite leftIcon = null, Color? leftIconBackdrop = null)
        {
            GameObject btnGO = GetTabButton(parent);
            if (btnGO == null)
            {
                btnGO = UI.CreateUIButton(parent.gameObject,
                    GalleryUiDesignTokens.TabButtonPreferredWidthRef,
                    GalleryUiDesignTokens.SideTabRowHeightRef, "", 18, 0, 0, AnchorPresets.middleLeft, null);
            }
            RoundedRect tabRounded = btnGO != null ? btnGO.GetComponent<RoundedRect>() : null;
            if (tabRounded != null)
                tabRounded.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            ConfigureSideTabRowHoverBorder(btnGO);
            // Always ensure hover delegate exists (for both new and pooled buttons)
            var hoverDel = btnGO.GetComponent<UIHoverDelegate>();
            if (hoverDel == null)
                hoverDel = btnGO.AddComponent<UIHoverDelegate>();
            // Add hover count tracking handler (ReturnTabButton clears handlers, so this is safe)
            hoverDel.OnHoverChange += (enter) => {
                if (enter) hoverCount++;
                else hoverCount--;
                if (hoverCount < 0) hoverCount = 0;
            };
            hoverDel.OnPointerEnterEvent += (d) => {
                currentPointerData = d;
            };
            
            // Standard Button Configuration
            Button btnComp = btnGO.GetComponent<Button>();
            btnComp.onClick.RemoveAllListeners();
            if (onClick != null)
            {
                UnityAction click = onClick;
                btnComp.onClick.AddListener(() =>
                {
                    UserTagPickDragSource dragSrc = btnGO.GetComponent<UserTagPickDragSource>();
                    if (dragSrc != null && dragSrc.ConsumedByDrag) return;
                    click();
                });
            }

            UIRightClickDelegate rightClickDelegate = btnGO.GetComponent<UIRightClickDelegate>();
            if (rightClickDelegate == null) rightClickDelegate = btnGO.AddComponent<UIRightClickDelegate>();
            rightClickDelegate.OnRightClick = (onRightClick != null) ? (Action)(() => onRightClick.Invoke()) : null;
            
            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = color;

            float s = ChromeScale;
            float insetL = Mathf.Max(0f, labelInsetLeft);
            float insetR = Mathf.Max(0f, labelInsetRight);
            TextAnchor anchor = labelAnchor;
            ClearTabLeftIcon(btnGO);
            if (leftIcon != null)
            {
                // Backdrop circle needs extra room so glyph stays ~old 20px after inset pad.
                float iconSize = (leftIconBackdrop.HasValue ? 28f : 20f) * s;
                float iconLeft = 4f * s;
                float gap = 6f * s;
                insetL = Mathf.Max(insetL, iconLeft + iconSize + gap);
                if (anchor == TextAnchor.MiddleCenter)
                    anchor = TextAnchor.MiddleLeft;
                ApplyTabLeftIcon(btnGO, leftIcon, iconLeft, iconSize, leftIconBackdrop);
            }

            Text txt = btnGO.GetComponentInChildren<Text>();
            GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            txt.alignment = anchor;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.resizeTextForBestFit = false;

            RectTransform txtRT = txt.GetComponent<RectTransform>();
            if (txtRT != null)
            {
                txtRT.anchorMin = Vector2.zero;
                txtRT.anchorMax = Vector2.one;
                txtRT.offsetMin = new Vector2(insetL, 0f);
                txtRT.offsetMax = new Vector2(-insetR, 0f);
            }

            // Ensure LayoutElement
            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le == null) le = btnGO.AddComponent<LayoutElement>();
            le.minWidth = GalleryUiDesignTokens.TabButtonMinWidthRef * s;
            le.preferredWidth = GalleryUiDesignTokens.TabButtonPreferredWidthRef * s;
            le.minHeight = SideTabRowHeightPx(s);
            le.preferredHeight = SideTabRowHeightPx(s);
            le.flexibleWidth = 1;

            float pad = 10f * s;
            // Rows use flexibleWidth and stretch to the side-tab column. preferredWidth (170) is only
            // a layout hint — using it alone clips labels early once a left icon takes inset space.
            float rowW = le.preferredWidth;
            float stretchW = (GalleryUiDesignTokens.SideTabColumnWidthRef
                - 2f * GalleryUiDesignTokens.SideTabSideMarginRef
                - GalleryUiDesignTokens.SideTabScrollBarWidthRef) * s;
            if (stretchW > rowW) rowW = stretchW;
            RectTransform btnRt = btnGO.GetComponent<RectTransform>();
            if (btnRt != null && btnRt.rect.width > rowW + 0.5f)
                rowW = btnRt.rect.width;
            float inner = rowW - pad - insetL - insetR;
            if (inner <= 2f) inner = 125f * s;
            string shown = EllipsizeTextPreferredWidth(txt, label, inner);
            txt.text = shown;
            bool truncated = !string.Equals(shown, label, StringComparison.Ordinal);
            string tipFinal = null;
            if (!string.IsNullOrEmpty(tooltip))
                tipFinal = truncated ? label + "\n\n" + tooltip : tooltip;
            else if (truncated)
                tipFinal = label;
            if (!string.IsNullOrEmpty(tipFinal))
                AddTooltipPlain(btnGO, tipFinal);

            if (!string.IsNullOrEmpty(userTagAppliedDragPrimary))
            {
                UserTagPickDragSource pickSrc = btnGO.GetComponent<UserTagPickDragSource>();
                if (pickSrc == null) pickSrc = btnGO.AddComponent<UserTagPickDragSource>();
                pickSrc.Panel = this;
                pickSrc.PrimaryTag = userTagAppliedDragPrimary;
                pickSrc.IsAppliedRowDrag = true;
            }
            else
            {
                UserTagPickDragSource pickSrc = btnGO.GetComponent<UserTagPickDragSource>();
                if (pickSrc != null && pickSrc.IsAppliedRowDrag)
                    UnityEngine.Object.Destroy(pickSrc);
                else if (pickSrc != null)
                    pickSrc.IsAppliedRowDrag = false;
            }

            if (targetList != null) targetList.Add(btnGO);
            return btnGO;
        }

        private static void ClearTabLeftIcon(GameObject buttonGO)
        {
            if (buttonGO == null) return;
            try
            {
                Transform old = buttonGO.transform.Find("TabLeftIcon");
                if (old != null)
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            catch { }
        }

        private static void ApplyTabLeftIcon(GameObject buttonGO, Sprite icon, float left, float size, Color? backdrop = null)
        {
            if (buttonGO == null || icon == null) return;
            GameObject rootGO = new GameObject("TabLeftIcon");
            rootGO.transform.SetParent(buttonGO.transform, false);
            // Parenting under a RectTransform already installs RectTransform — do not AddComponent again.
            RectTransform rootRT = rootGO.GetComponent<RectTransform>();
            if (rootRT == null) rootRT = rootGO.AddComponent<RectTransform>();
            // Overlay only — never feed ContentSizeFitter / VLG preferred height.
            LayoutElement iconLe = rootGO.GetComponent<LayoutElement>();
            if (iconLe == null) iconLe = rootGO.AddComponent<LayoutElement>();
            iconLe.ignoreLayout = true;
            rootRT.anchorMin = new Vector2(0f, 0.5f);
            rootRT.anchorMax = new Vector2(0f, 0.5f);
            rootRT.pivot = new Vector2(0f, 0.5f);
            rootRT.anchoredPosition = new Vector2(left, 0f);
            rootRT.sizeDelta = new Vector2(size, size);

            if (backdrop.HasValue)
            {
                GameObject bgGO = new GameObject("Backdrop");
                bgGO.transform.SetParent(rootGO.transform, false);
                RoundedRect rr = bgGO.AddComponent<RoundedRect>();
                rr.color = backdrop.Value;
                rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
                rr.raycastTarget = false;
                RectTransform bgRT = bgGO.GetComponent<RectTransform>();
                bgRT.anchorMin = Vector2.zero;
                bgRT.anchorMax = Vector2.one;
                bgRT.offsetMin = Vector2.zero;
                bgRT.offsetMax = Vector2.zero;
            }

            GameObject glyphGO = new GameObject("Glyph");
            glyphGO.transform.SetParent(rootGO.transform, false);
            Image img = glyphGO.AddComponent<Image>();
            img.sprite = icon;
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;
            RectTransform rt = glyphGO.GetComponent<RectTransform>();
            float pad = backdrop.HasValue ? size * 0.12f : 0f;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        private InputField CreateSearchInput(GameObject parent, float width, UnityAction<string> onValueChanged, Action onClear = null, Action onEscape = null)
        {
            GameObject inputGO = new GameObject("SearchInput");
            inputGO.transform.SetParent(parent.transform, false);
            
            RoundedRect bg = inputGO.AddComponent<RoundedRect>();
            bg.color = UI.InputFieldBg;
            bg.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            
            // Add Hover Border
            inputGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(inputGO);

            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.sizeDelta = new Vector2(width, 35);
            
            // Text Area
            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(38, 0); // Left offset accounts for search icon
            textAreaRT.offsetMax = new Vector2(-GalleryUiDesignTokens.SearchTextRightInsetRef, 0);

            // Search icon (left side of input)
            {
                var s = UI.LoadIconSprite("vpb_icons/search.png", new Color(0.5f, 0.5f, 0.5f, 1f));
                if (s != null)
                {
                    GameObject iconGO = new GameObject("SearchIcon");
                    iconGO.transform.SetParent(inputGO.transform, false);
                    Image iconImg = UI.AddImage(iconGO, new Color(0.5f, 0.5f, 0.5f, 1f));
                    iconImg.sprite = s;
                    RectTransform iconRT = iconGO.GetComponent<RectTransform>();
                    iconRT.anchorMin = new Vector2(0, 0.5f);
                    iconRT.anchorMax = new Vector2(0, 0.5f);
                    iconRT.pivot = new Vector2(0, 0.5f);
                    iconRT.anchoredPosition = new Vector2(6, 0);
                    iconRT.sizeDelta = new Vector2(24, 24);
                }
            }
            
            // Placeholder
            GameObject placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);
            Text placeholderText = placeholder.AddComponent<Text>();
            placeholderText.text = VPBTranslation.T("gallery.search.main", "Search name, #tag, OR, badge…");
            placeholderText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            placeholderText.fontSize = GalleryUiDesignTokens.FontBodyRef;
            placeholderText.color = UI.InputFieldPlaceholderColor;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.alignment = TextAnchor.MiddleLeft; // Vertically centered
            RectTransform placeholderRT = placeholder.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = Vector2.zero;
            
            // Text
            Text textComponent = UI.CreateLabel(textArea, "", GalleryUiDesignTokens.FontBodyRef, UI.InputFieldTextColor, TextAnchor.MiddleLeft, richText: false); // Vertically centered

            input.textComponent = textComponent;
            input.placeholder = placeholderText;
            input.onValueChanged.AddListener(onValueChanged);
            
            // Clear button — flush right, full field height so hover rim meets search border.
            GameObject clearBtn = UI.CreateUIButton(inputGO, GalleryUiDesignTokens.SearchClearBtnSizeRef, GalleryUiDesignTokens.SearchFieldHeightRef, "X", 24, 0, 0, AnchorPresets.middleRight, () => {
                input.text = "";
                input.ActivateInputField();
                input.MoveTextEnd(false);
                onClear?.Invoke();
            });
            RectTransform clearRT = clearBtn.GetComponent<RectTransform>();
            clearRT.anchorMin = new Vector2(1f, 0f);
            clearRT.anchorMax = new Vector2(1f, 1f);
            clearRT.pivot = new Vector2(1f, 0.5f);
            clearRT.anchoredPosition = Vector2.zero;
            clearRT.sizeDelta = new Vector2(GalleryUiDesignTokens.SearchClearBtnSizeRef, 0f);
            clearBtn.GetComponent<Image>().color = new Color(0,0,0,0); // Transparent bg
            { var s = UI.LoadIconSprite("vpb_icons/backspace.png", new Color(0.6f, 0.6f, 0.6f)); if (s != null) UI.AddIconToButton(clearBtn, s, 6f, new Color(0, 0, 0, 0)); }

            // Border-only hover (avoid text color fill); inward so rim stays inside search field edge.
            var clearHoverBorder = clearBtn.AddComponent<UIHoverBorder>();
            clearHoverBorder.hoverColor = new Color(1f, 0.2f, 0.2f, 1f);
            clearHoverBorder.borderSize = 2f;
            clearHoverBorder.inward = true;

            // ESC: default clears field; title search passes onEscape to blur without wiping chips.
            Button clearBtnComponent = clearBtn.GetComponent<Button>();
            inputGO.AddComponent<SearchInputESCHandler>().Initialize(input, clearBtnComponent, onEscape);
            // Standard editor shortcut: Ctrl+Backspace deletes previous word
            inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input);

            return input;
        }

        private GameObject GetTabButton(Transform parent)
        {
            if (tabButtonPool.Count > 0)
            {
                GameObject btn = tabButtonPool.Pop();
                btn.transform.SetParent(parent, false);
                btn.SetActive(true);
                return btn;
            }
            return null;
        }

        private void ReturnTabButton(GameObject btn)
        {
            if (btn == null) return;
            // Drop the active-chip handle if this is the chip being recycled, so a later pool reuse for
            // a different row can't have its label stamped by UpdateSelectionContextMenu.
            if (_activeSubfilterChipText != null)
            {
                var textComp = btn.GetComponentInChildren<Text>();
                if (textComp == _activeSubfilterChipText) { _activeSubfilterChipText = null; _activeSubfilterChipLabelPrefix = null; }
            }
            btn.SetActive(false);
            // Clear hover event handlers to prevent old handlers from submenu modes persisting when buttons are reused
            var hoverDel = btn.GetComponent<UIHoverDelegate>();
            if (hoverDel != null)
            {
                hoverDel.OnHoverChange = null;
                hoverDel.OnPointerEnterEvent = null;
            }
            var pickDrag = btn.GetComponent<UserTagPickDragSource>();
            if (pickDrag != null)
                UnityEngine.Object.Destroy(pickDrag);
            // Keep parented to ensure cleanup on destroy
            if (backgroundBoxGO != null) btn.transform.SetParent(backgroundBoxGO.transform, false);
            tabButtonPool.Push(btn);
        }

        public GameObject InjectButton(string label, UnityAction action)
        {
            GameObject btnGO;
            if (navButtonPool.Count > 0)
            {
                btnGO = navButtonPool.Pop();
                btnGO.SetActive(true);
            }
            else
            {
                btnGO = CreateNewNavButtonGO();
            }

            // Reset/Configure for Navigation
            BindNavigationButton(btnGO, label, action);
            activeButtons.Add(btnGO);
            return btnGO;
        }

        private GameObject CreateNewNavButtonGO()
        {
            GameObject btnGO = new GameObject("NavButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = UI.AddGalleryElementRoundedBg(btnGO, new Color(0.2f, 0.4f, 0.6f, 1f));

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;

            UI.CreateLabel(btnGO, "", GalleryUiDesignTokens.FontRef, Color.white, TextAnchor.MiddleCenter, raycastTarget: false, name: "NavText");

            return btnGO;
        }

        private void BindNavigationButton(GameObject btnGO, string label, UnityAction action)
        {
            btnGO.name = "NavButton_" + label.Replace("\n", ""); // Identification for Pool

            // Reset common elements
            Button btn = btnGO.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            if (action != null) btn.onClick.AddListener(action);

            // Set Text
            Transform navTextT = btnGO.transform.Find("NavText");
            if (navTextT != null)
            {
                Text t = navTextT.GetComponent<Text>();
                if (t != null) t.text = label;
            }

            // Set BG Color (Optional reset if changed elsewhere)
            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = new Color(0.2f, 0.4f, 0.6f, 1f); 
        }


        private static float GetGridLabelUnits()
            => Mathf.Max(0f, VPBConfig.Instance.GalleryGridLabelFontSize * 0.6f);

        private static float GetGridLabelFraction()
        {
            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryGridLabelsStripVisible()) return 0f;
            float L = GetGridLabelUnits();
            return L / 100f;
        }

        internal float GetGridCellConfigHeight() => 100f;

        private static string GetGridItemLabelText(FileEntry file)
        {
            if (file == null) return "";

            // Pretty mode strips creator/version/prefix for every entry kind, matching BA across all tabs.
            bool pretty = VPBConfig.Instance != null && VPBConfig.Instance.GalleryPrettyPresetNames;
            if (pretty)
            {
                string r = GetPrettyEntryDisplayName(file);
                LogPrettyNameSample(file, r, "GridLabel");
                return r;
            }

            VarPackage pkg = null;
            if (file is VarFileEntry vfe)         pkg = vfe.Package;
            else if (file is PackageListEntry ple) pkg = ple.Package;
            if (pkg != null && !string.IsNullOrEmpty(pkg.Uid)) return pkg.Uid;
            return System.IO.Path.GetFileNameWithoutExtension(file.Name ?? "");
        }

        /// <summary>True when filename matches BA's preset prefix rule (Preset_*.vap or Plugins_*.json). Drives the override-uid-with-pretty-name decision in <see cref="GetGridItemLabelText"/>.</summary>
        internal static bool IsPresetLikeFileName(FileEntry file)
        {
            if (file == null) return false;
            string raw = file.Name;
            if (string.IsNullOrEmpty(raw)) return false;
            int dot = raw.LastIndexOf('.');
            if (dot <= 0) return false;
            string stem = raw.Substring(0, dot);
            string ext = raw.Substring(dot + 1);
            if (string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase) && stem.StartsWith("Preset_", StringComparison.Ordinal))
                return true;
            if (string.Equals(ext, "json", StringComparison.OrdinalIgnoreCase) && stem.StartsWith("Plugins_", StringComparison.Ordinal))
                return true;
            return false;
        }

        private static string TruncateGridLabelTextByWidth(Text textComponent, string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (textComponent == null || textComponent.font == null) return text;

            float availWidth = Mathf.Max(10f, maxWidth - 4f);

            textComponent.text = text;
            float fullWidth = LayoutUtility.GetPreferredWidth(textComponent.GetComponent<RectTransform>());
            if (fullWidth <= availWidth) return text;

            string ellipsis = "...";
            textComponent.text = ellipsis;
            float ellipsisWidth = LayoutUtility.GetPreferredWidth(textComponent.GetComponent<RectTransform>());
            float targetWidth = availWidth - ellipsisWidth - 2f;

            if (targetWidth <= 0) return ellipsis;

            string current = text;
            for (int i = 0; i < text.Length; i++)
            {
                current = text.Substring(0, text.Length - i);
                textComponent.text = current;
                float width = LayoutUtility.GetPreferredWidth(textComponent.GetComponent<RectTransform>());
                if (width <= targetWidth) return current + ellipsis;
            }

            return ellipsis;
        }

        private void CreateFileButton(FileEntry file)
        {
            GameObject btnGO;
            if (fileButtonPool.Count > 0)
            {
                btnGO = fileButtonPool.Pop();
                btnGO.SetActive(true);
            }
            else
            {
                btnGO = CreateNewFileButtonGO();
            }

            BindFileButton(btnGO, file);
            btnGO.transform.SetAsLastSibling();
            activeButtons.Add(btnGO);
        }

        public GameObject CreateNewFileButtonGO()
        {
            GameObject btnGO = new GameObject("FileButton_Template");
            btnGO.transform.SetParent(contentGO.transform, false);
            
            Image img = UI.AddImage(btnGO, new Color(0.2f, 0.2f, 0.2f, 0.5f));

            // Add Hover Border
            btnGO.AddComponent<UIHoverBorder>();
            AddHoverDelegate(btnGO);

            Button btn = btnGO.AddComponent<Button>();
            UI.ConfigButtonFlat(btn);

            // Thumbnail (Fill 1x1)
            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(btnGO.transform, false);
            RawImage thumbImg = thumbGO.AddComponent<RawImage>();
            thumbImg.color = new Color(0, 0, 0, 0.5f);
            thumbImg.raycastTarget = false;
            RectTransform thumbRT = thumbGO.GetComponent<RectTransform>();
            thumbRT.anchorMin = Vector2.zero;
            thumbRT.anchorMax = Vector2.one;
            thumbRT.sizeDelta = Vector2.zero;
            float pad = 3f;
            try { if (VPBConfig.Instance != null) pad = Mathf.Clamp(VPBConfig.Instance.GalleryGridThumbnailPadding, 0f, 40f); } catch { pad = 3f; }
            thumbRT.offsetMin = new Vector2(pad, pad);
            thumbRT.offsetMax = new Vector2(-pad, -pad);

            // Scan-whitelist included ring (always inward, parented under thumbnail in grid).
            GameObject scanWlBorderGO = new GameObject("ScanWlBorder");
            scanWlBorderGO.transform.SetParent(thumbGO.transform, false);
            RectTransform swbRT = scanWlBorderGO.AddComponent<RectTransform>();
            swbRT.anchorMin = Vector2.zero;
            swbRT.anchorMax = Vector2.one;
            swbRT.offsetMin = Vector2.zero;
            swbRT.offsetMax = Vector2.zero;
            AddBorderEdgeNamed(scanWlBorderGO, "Top",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, 4));
            AddBorderEdgeNamed(scanWlBorderGO, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 4));
            AddBorderEdgeNamed(scanWlBorderGO, "Left",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f), new Vector2(4, 0));
            AddBorderEdgeNamed(scanWlBorderGO, "Right",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f), new Vector2(4, 0));
            scanWlBorderGO.SetActive(false);

            GameObject scanWlTempBorderGO = new GameObject("ScanWlTempBorder");
            scanWlTempBorderGO.transform.SetParent(thumbGO.transform, false);
            RectTransform swtRT = scanWlTempBorderGO.AddComponent<RectTransform>();
            swtRT.anchorMin = Vector2.zero;
            swtRT.anchorMax = Vector2.one;
            swtRT.offsetMin = Vector2.zero;
            swtRT.offsetMax = Vector2.zero;
            AddBorderEdgeNamed(scanWlTempBorderGO, "Top",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, 4));
            AddBorderEdgeNamed(scanWlTempBorderGO, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 4));
            AddBorderEdgeNamed(scanWlTempBorderGO, "Left",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f), new Vector2(4, 0));
            AddBorderEdgeNamed(scanWlTempBorderGO, "Right",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f), new Vector2(4, 0));
            scanWlTempBorderGO.SetActive(false);

            EnsurePluginThumbPlaceholderUi(thumbGO.transform);

            // Grid-mode inward border (4 edge Images inside cell). Used when padding = 0.
            GameObject gridInnerBorderGO = new GameObject("GridInnerBorder");
            gridInnerBorderGO.transform.SetParent(btnGO.transform, false);
            RectTransform gibRT = gridInnerBorderGO.AddComponent<RectTransform>();
            gibRT.anchorMin = Vector2.zero;
            gibRT.anchorMax = Vector2.one;
            gibRT.offsetMin = Vector2.zero;
            gibRT.offsetMax = Vector2.zero;
            // child edge names are stable: Top/Bottom/Left/Right
            AddBorderEdgeNamed(gridInnerBorderGO, "Top",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, 2));
            AddBorderEdgeNamed(gridInnerBorderGO, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 2));
            AddBorderEdgeNamed(gridInnerBorderGO, "Left",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f), new Vector2(2, 0));
            AddBorderEdgeNamed(gridInnerBorderGO, "Right",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f), new Vector2(2, 0));
            gridInnerBorderGO.SetActive(false);

            GameObject gridLabelGO = new GameObject("GridLabel");
            gridLabelGO.transform.SetParent(btnGO.transform, false);
            gridLabelGO.SetActive(false);

            RectTransform gridLabelRT = gridLabelGO.AddComponent<RectTransform>();
            gridLabelRT.anchorMin = new Vector2(0f, 0f);
            gridLabelRT.anchorMax = new Vector2(1f, 0f);
            gridLabelRT.offsetMin = Vector2.zero;
            gridLabelRT.offsetMax = Vector2.zero;

            Image gridLabelBg = UI.AddImage(gridLabelGO, GalleryItemLabelBarBackdrop, false);

            Text gridLabelText = UI.CreateLabel(gridLabelGO, "", GalleryUiDesignTokens.FontBodyRef, Color.white, TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow, raycastTarget: false, name: "Text");
            GameObject gridLabelTextGO = gridLabelText.gameObject;
            RectTransform gridLabelTextRT = gridLabelTextGO.GetComponent<RectTransform>();
            gridLabelTextRT.offsetMin = new Vector2(2f, 0f);
            gridLabelTextRT.offsetMax = new Vector2(-2f, 0f);

            Shadow gridLabelShadow = gridLabelTextGO.AddComponent<Shadow>();
            gridLabelShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            gridLabelShadow.effectDistance = new Vector2(1f, -1f);

            // Card Container (Hidden by default, positions below)
            GameObject cardGO = new GameObject("Card");
            cardGO.transform.SetParent(btnGO.transform, false);
            cardGO.SetActive(false);

            RectTransform cardRT = cardGO.AddComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0, 0); // Bottom
            cardRT.anchorMax = new Vector2(1, 0); // Bottom
            cardRT.pivot = new Vector2(0.5f, 0);  // Pivot Bottom (Inside)
            cardRT.anchoredPosition = Vector2.zero;
            cardRT.sizeDelta = new Vector2(0, 0); // Width stretch

            // Dynamic height based on content
            VerticalLayoutGroup cardVLG = UI.AddVLG(cardGO, 0f, UI.Pad(5f, 5f, 5f, 5f));

            ContentSizeFitter cardCSF = cardGO.AddComponent<ContentSizeFitter>();
            cardCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Background — same see-through bar as GridLabel / hover badges
            Image cardBg = UI.AddImage(cardGO, GalleryItemLabelBarBackdrop, false);

            // Label
            Text labelText = UI.CreateLabel(cardGO, "", GalleryUiDesignTokens.FontBodyRef, Color.white, TextAnchor.MiddleCenter, verticalWrap: VerticalWrapMode.Overflow, raycastTarget: false, name: "Label");
            GameObject labelGO = labelText.gameObject;

            // Label Layout
            LayoutElement labelLE = UI.AddLE(labelGO, minHeight: 30);

            // Hover Logic
            UIHoverReveal hover = btnGO.AddComponent<UIHoverReveal>();
            hover.card = cardGO;
            hover.panel = this;

            // List Row (Table mode)
            GameObject listRowGO = new GameObject("ListRow");
            listRowGO.transform.SetParent(btnGO.transform, false);
            listRowGO.SetActive(false);
            RectTransform listRowRT = listRowGO.AddComponent<RectTransform>();
            listRowRT.anchorMin = new Vector2(0, 0);
            listRowRT.anchorMax = new Vector2(1, 1);
            listRowRT.pivot = new Vector2(0, 0.5f);
            listRowRT.offsetMin = new Vector2(60, 0);
            listRowRT.offsetMax = new Vector2(-50, 0);

            VerticalLayoutGroup listVLG = UI.AddVLG(listRowGO, 0f, UI.Pad(5f, 5f, 5f, 5f), TextAnchor.MiddleLeft);

            // Name
            Text listNameText = UI.CreateLabel(listRowGO, "", GalleryUiDesignTokens.FontRef, Color.white, TextAnchor.LowerLeft, HorizontalWrapMode.Overflow, raycastTarget: false, name: "Name");
            GameObject listNameGO = listNameText.gameObject;
            LayoutElement listNameLE = UI.AddLE(listNameGO, minHeight: 32, flexibleWidth: 1);

            // Details Row
            GameObject detailsRowGO = new GameObject("Details");
            detailsRowGO.transform.SetParent(listRowGO.transform, false);
            HorizontalLayoutGroup detailsHLG = UI.AddHLG(detailsRowGO, 15f, UI.Pad(0f, 0f, 0f, 0f), childForceExpandWidth: false);
            LayoutElement detailsLE = UI.AddLE(detailsRowGO, minHeight: 24, flexibleWidth: 1);

            // Helper to create detail text
            GameObject CreateDetailText(string name, string placeholder, float width)
            {
                Text t = UI.CreateLabel(detailsRowGO, placeholder, GalleryUiDesignTokens.FontBodyRef, new Color(0.75f, 0.75f, 0.75f, 1f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, raycastTarget: false, name: name);
                GameObject go = t.gameObject;
                LayoutElement le = UI.AddLE(go, minWidth: width * 0.5f, preferredWidth: width);
                return go;
            }

            CreateDetailText("Size", "Size", 110);
            CreateDetailText("Date", "Date", 130);
            CreateDetailText("Category", "Category", 160);
            CreateDetailText("Deps", "D:", 80);
            CreateDetailText("Missing", "M:", 80);
            CreateDetailText("Dependents", "Dn:", 80);

            // List mode: badge strip below Size/Date row (horizontal layout; not over thumbnail)
            GameObject listBadgesGO = new GameObject("ListBadges");
            listBadgesGO.transform.SetParent(listRowGO.transform, false);
            // Plain VerticalLayoutGroup child like Name/Details: let the group drive position/size.
            // Custom bottom-stretch anchors here fight the group and mis-place the strip.
            listBadgesGO.AddComponent<RectTransform>();
            HorizontalLayoutGroup listBadgesHLG = UI.AddHLG(listBadgesGO, spacing: 4f, childForceExpandWidth: false);
            LayoutElement listBadgesLE = UI.AddLE(listBadgesGO, minHeight: 32f, preferredHeight: 32f, flexibleWidth: 1f, flexibleHeight: 0f);

            // Rating (Top-right corner)
            GameObject ratingGO = new GameObject("Rating");
            ratingGO.transform.SetParent(btnGO.transform, false);
            RectTransform ratingRT = ratingGO.AddComponent<RectTransform>();
            ratingRT.anchorMin = new Vector2(1, 1); // Top Right
            ratingRT.anchorMax = new Vector2(1, 1);
            ratingRT.pivot = new Vector2(1, 1);
            ratingRT.sizeDelta = new Vector2(40, 40);
            ratingRT.anchoredPosition = new Vector2(-2, -2);

            GameObject starBtnGO = UI.CreateUIButton(ratingGO, 32, 32, "0", 20, 0, 0, AnchorPresets.middleCenter, null);
            starBtnGO.name = "Star";
            starBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
            Text starIconText = starBtnGO.GetComponentInChildren<Text>();

            GameObject selectorGO = new GameObject("RatingSelector");
            selectorGO.transform.SetParent(btnGO.transform, false);
            RectTransform selectorRT = selectorGO.AddComponent<RectTransform>();
            // 3-row × 2-col grid: [X][1] / [2][3] / [4][5] — drops below star icon, aligns to right edge
            selectorRT.anchorMin = new Vector2(1, 1);
            selectorRT.anchorMax = new Vector2(1, 1);
            selectorRT.pivot = new Vector2(1, 1);
            selectorRT.sizeDelta = new Vector2(80, 114);
            selectorRT.anchoredPosition = new Vector2(-2, -44);

            CanvasGroup selectorCG = selectorGO.AddComponent<CanvasGroup>();
            selectorCG.alpha = 0f;
            selectorCG.interactable = false;
            selectorCG.blocksRaycasts = false;

            Image selectorBg = UI.AddImage(selectorGO, new Color(0.05f, 0.05f, 0.05f, 0.95f));

            GridLayoutGroup selectorGrid = selectorGO.AddComponent<GridLayoutGroup>();
            selectorGrid.cellSize = new Vector2(38, 36);
            selectorGrid.spacing = new Vector2(2, 2);
            selectorGrid.padding = new RectOffset(1, 1, 1, 1);
            selectorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            selectorGrid.constraintCount = 2;
            selectorGrid.childAlignment = TextAnchor.UpperLeft;

            RatingHandler ratingHandler = btnGO.AddComponent<RatingHandler>();
            Image[] optImages = new Image[6];
            Text[] optTexts = new Text[6];
            GameObject[] optBorders = new GameObject[6];
            for (int i = 0; i <= 5; i++)
            {
                int ratingValue = i;
                string label = i == 0 ? "X" : i.ToString();
                GameObject optBtnGO = UI.CreateUIButton(selectorGO, 38, 36, label, 22, 0, 0, AnchorPresets.middleCenter, () => ratingHandler.SetRating(ratingValue));
                optBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
                optImages[i] = optBtnGO.GetComponent<Image>();
                optImages[i].color = RatingHandler.RatingColors[i];
                optTexts[i] = optBtnGO.GetComponentInChildren<Text>();
                optTexts[i].color = i == 0 ? Color.red : Color.black;

                // Selection border: 4 white edge images inside the button, rendered before the label
                GameObject borderGO = new GameObject("SelectionBorder");
                borderGO.transform.SetParent(optBtnGO.transform, false);
                borderGO.transform.SetSiblingIndex(0);
                RectTransform borderRT = borderGO.AddComponent<RectTransform>();
                borderRT.anchorMin = Vector2.zero;
                borderRT.anchorMax = Vector2.one;
                borderRT.offsetMin = Vector2.zero;
                borderRT.offsetMax = Vector2.zero;
                AddBorderEdge(borderGO, new Vector2(0,1), new Vector2(1,1), new Vector2(0.5f,1), new Vector2(0,3));
                AddBorderEdge(borderGO, new Vector2(0,0), new Vector2(1,0), new Vector2(0.5f,0), new Vector2(0,3));
                AddBorderEdge(borderGO, new Vector2(0,0), new Vector2(0,1), new Vector2(0,0.5f), new Vector2(3,0));
                AddBorderEdge(borderGO, new Vector2(1,0), new Vector2(1,1), new Vector2(1,0.5f), new Vector2(3,0));
                borderGO.SetActive(false);
                optBorders[i] = borderGO;
            }
            ratingHandler.SetOptionRefs(optImages, optTexts, optBorders);

            Button starBtn = starBtnGO.GetComponent<Button>();
            starBtn.onClick.AddListener(() => ratingHandler.ToggleSelector());
            
            // Drag Logic
            UIDraggableItem draggable = btnGO.AddComponent<UIDraggableItem>();
            draggable.ThumbnailImage = thumbImg;
            draggable.Panel = this;

            // AutoInstall Badge (Top-left corner, opposite the star rating)
            GameObject aiBadgeGO = new GameObject("AutoInstallBadge");
            aiBadgeGO.transform.SetParent(btnGO.transform, false);
            RectTransform aiBadgeRT = aiBadgeGO.AddComponent<RectTransform>();
            aiBadgeRT.anchorMin = new Vector2(0, 1); // Top Left
            aiBadgeRT.anchorMax = new Vector2(0, 1);
            aiBadgeRT.pivot = new Vector2(0, 1);
            aiBadgeRT.sizeDelta = new Vector2(32, 32);
            aiBadgeRT.anchoredPosition = new Vector2(6, -6);
            AddGalleryBadgeBackground(aiBadgeGO);
            Text aiBadgeText = UI.CreateLabel(aiBadgeGO, "A", GalleryUiDesignTokens.FontBodyRef, GalleryBadgeLetterAutoInstall, TextAnchor.MiddleCenter, raycastTarget: false, name: "Text");
            LayoutElement aiBadgeLE = UI.AddLE(aiBadgeGO, minWidth: 32f, minHeight: 32f, preferredWidth: 32f, preferredHeight: 32f);
            aiBadgeGO.SetActive(false);

            // Hidden package badge (top-left, to the right of AutoInstall "A")
            GameObject hideBadgeGO = new GameObject("HidePackageBadge");
            hideBadgeGO.transform.SetParent(btnGO.transform, false);
            RectTransform hideBadgeRT = hideBadgeGO.AddComponent<RectTransform>();
            hideBadgeRT.anchorMin = new Vector2(0, 1);
            hideBadgeRT.anchorMax = new Vector2(0, 1);
            hideBadgeRT.pivot = new Vector2(0, 1);
            hideBadgeRT.sizeDelta = new Vector2(32, 32);
            hideBadgeRT.anchoredPosition = new Vector2(42, -6);
            AddGalleryBadgeBackground(hideBadgeGO);
            Text hideBadgeText = UI.CreateLabel(hideBadgeGO, "H", GalleryUiDesignTokens.FontBodyRef, GalleryBadgeLetterHide, TextAnchor.MiddleCenter, raycastTarget: false, name: "Text");
            LayoutElement hideBadgeLE = UI.AddLE(hideBadgeGO, minWidth: 32f, minHeight: 32f, preferredWidth: 32f, preferredHeight: 32f);
            hideBadgeGO.SetActive(false);

            // Scan-whitelist included badge (top-left). Ambient in grid+list; temp gets outline ring.
            GameObject scanExBadgeGO = new GameObject("ScanExcludedBadge");
            scanExBadgeGO.transform.SetParent(btnGO.transform, false);
            RectTransform scanExBadgeRT = scanExBadgeGO.AddComponent<RectTransform>();
            scanExBadgeRT.anchorMin = new Vector2(0, 1);
            scanExBadgeRT.anchorMax = new Vector2(0, 1);
            scanExBadgeRT.pivot = new Vector2(0, 1);
            scanExBadgeRT.sizeDelta = new Vector2(32, 32);
            scanExBadgeRT.anchoredPosition = new Vector2(80, -6);
            AddGalleryBadgeBackground(scanExBadgeGO);
            EnsureScanWlBadgeTempRing(scanExBadgeGO);
            Text scanExBadgeText = UI.CreateLabel(scanExBadgeGO, "W", GalleryUiDesignTokens.FontBodyRef, GalleryBadgeLetterScanWlPersistent, TextAnchor.MiddleCenter, raycastTarget: false, name: "Text");
            LayoutElement scanExBadgeLE = UI.AddLE(scanExBadgeGO, minWidth: 32f, minHeight: 32f, preferredWidth: 32f, preferredHeight: 32f);
            scanExBadgeGO.SetActive(false);

            // User tags badge (top-left; slot order via ApplyDynamicTopLeftBadgeLayout)
            GameObject userTagsBadgeGO = new GameObject("UserTagsBadge");
            userTagsBadgeGO.transform.SetParent(btnGO.transform, false);
            RectTransform userTagsBadgeRT = userTagsBadgeGO.AddComponent<RectTransform>();
            userTagsBadgeRT.anchorMin = new Vector2(0, 1);
            userTagsBadgeRT.anchorMax = new Vector2(0, 1);
            userTagsBadgeRT.pivot = new Vector2(0, 1);
            userTagsBadgeRT.sizeDelta = new Vector2(32, 32);
            userTagsBadgeRT.anchoredPosition = new Vector2(118, -6);
            AddGalleryBadgeBackground(userTagsBadgeGO);
            Text userTagsBadgeText = UI.CreateLabel(userTagsBadgeGO, "T", GalleryUiDesignTokens.FontBodyRef, GalleryBadgeLetterUserTags, TextAnchor.MiddleCenter, raycastTarget: false, name: "Text");
            LayoutElement userTagsBadgeLE = UI.AddLE(userTagsBadgeGO, minWidth: 32f, minHeight: 32f, preferredWidth: 32f, preferredHeight: 32f);
            userTagsBadgeGO.SetActive(false);

            // Deps badge (inactive on grid; detail strip owns deps). Kept for pooled-cell layout slots.
            GameObject depsBadgeGO = new GameObject("DepsBadge");
            depsBadgeGO.transform.SetParent(btnGO.transform, false);
            RectTransform depsBadgeRT = depsBadgeGO.AddComponent<RectTransform>();
            depsBadgeRT.anchorMin = new Vector2(0, 0);
            depsBadgeRT.anchorMax = new Vector2(0, 0);
            depsBadgeRT.pivot = new Vector2(0, 0);
            depsBadgeRT.sizeDelta = new Vector2(72, 28);
            depsBadgeRT.anchoredPosition = new Vector2(6, 6);
            AddGalleryBadgeBackground(depsBadgeGO);
            UI.CreateLabel(
                depsBadgeGO, "", GalleryUiDesignTokens.FontBodyRef, Color.white, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow, raycastTarget: false, richText: true, name: "Text");
            UI.AddLE(depsBadgeGO, minWidth: 48f, minHeight: 28f, preferredWidth: 72f, preferredHeight: 28f);
            depsBadgeGO.SetActive(false);

            // Hub dep download (inactive on grid; detail strip owns hub deps). Kept for pooled-cell layout slots.
            GameObject depsDlGO = new GameObject("DepsDownloadBtn");
            depsDlGO.transform.SetParent(btnGO.transform, false);
            RectTransform depsDlRT = depsDlGO.AddComponent<RectTransform>();
            depsDlRT.anchorMin = new Vector2(0, 0);
            depsDlRT.anchorMax = new Vector2(0, 0);
            depsDlRT.pivot = new Vector2(0, 0);
            depsDlRT.sizeDelta = new Vector2(28, 28);
            depsDlRT.anchoredPosition = new Vector2(84, 6);
            RoundedRect depsDlRr = AddGalleryBadgeBackground(depsDlGO);
            UI.CreateLabel(
                depsDlGO, "↓", GalleryUiDesignTokens.FontBodyRef, GalleryBadgeLetterDepsDownload, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, raycastTarget: false, richText: false, name: "Text");
            UI.AddLE(depsDlGO, minWidth: 24f, minHeight: 28f, preferredWidth: 28f, preferredHeight: 28f);
            Button depsDlBtn = depsDlGO.AddComponent<Button>();
            depsDlBtn.targetGraphic = depsDlRr;
            depsDlBtn.transition = Selectable.Transition.ColorTint;
            var depsDlColors = depsDlBtn.colors;
            depsDlColors.normalColor = Color.white;
            depsDlColors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            depsDlColors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            depsDlBtn.colors = depsDlColors;
            depsDlGO.AddComponent<GalleryDepsDownloadHoverButton>();
            depsDlGO.SetActive(false);

            // List-mode hover indicator: thin vertical line at left edge of thumbnail (white, semi-transparent)
            GameObject listHoverBarGO = new GameObject("ListHoverBar");
            listHoverBarGO.transform.SetParent(btnGO.transform, false);
            Image listHoverBarImg = UI.AddImage(listHoverBarGO, UI.White(0.45f), false);
            RectTransform listHoverBarRT = listHoverBarGO.GetComponent<RectTransform>();
            listHoverBarRT.anchorMin = new Vector2(0, 0);
            listHoverBarRT.anchorMax = new Vector2(0, 1);
            listHoverBarRT.pivot = new Vector2(0, 0.5f);
            listHoverBarRT.sizeDelta = new Vector2(2, 0);
            listHoverBarRT.anchoredPosition = Vector2.zero;
            listHoverBarGO.SetActive(false);

            // List-mode selection indicator: left accent bar (yellow, opaque)
            GameObject listSelBarGO = new GameObject("ListSelectionBar");
            listSelBarGO.transform.SetParent(btnGO.transform, false);
            Image listSelBarImg = UI.AddImage(listSelBarGO, new Color(1f, 0.85f, 0f, 1f), false);
            RectTransform listSelBarRT = listSelBarGO.GetComponent<RectTransform>();
            listSelBarRT.anchorMin = new Vector2(0, 0);
            listSelBarRT.anchorMax = new Vector2(0, 1);
            listSelBarRT.pivot = new Vector2(0, 0.5f);
            listSelBarRT.sizeDelta = new Vector2(3, 0);
            listSelBarRT.anchoredPosition = Vector2.zero;
            listSelBarGO.SetActive(false);

            // Wire hover bar into UIHoverBorder; selection bar is managed by UpdateFileButtonVisuals
            UIHoverBorder hoverBorderComp = btnGO.GetComponent<UIHoverBorder>();
            if (hoverBorderComp != null) hoverBorderComp.hoverBorderGO = listHoverBarGO;

            SetLayerRecursive(btnGO, 5);
            FileButtonBinder.Attach(btnGO);
            MarkGalleryPaneChromeDirty();
            return btnGO;
        }

        private static void AddBorderEdgeNamed(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        {
            if (parent == null) return;
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = Color.yellow;
            img.raycastTarget = false;
        }

        private static void SetBorderThickness(GameObject borderGO, float thickness)
        {
            if (borderGO == null) return;
            float t = Mathf.Max(0f, thickness);
            var top = borderGO.transform.Find("Top") as RectTransform;
            var bottom = borderGO.transform.Find("Bottom") as RectTransform;
            var left = borderGO.transform.Find("Left") as RectTransform;
            var right = borderGO.transform.Find("Right") as RectTransform;
            if (top != null) top.sizeDelta = new Vector2(0, t);
            if (bottom != null) bottom.sizeDelta = new Vector2(0, t);
            if (left != null) left.sizeDelta = new Vector2(t, 0);
            if (right != null) right.sizeDelta = new Vector2(t, 0);
        }

        /// <summary>Recolors GridInnerBorder edge strips (built with yellow defaults).</summary>
        private static void SetGalleryInnerBorderEdgeTint(GameObject borderGO, Color tint)
        {
            if (borderGO == null) return;
            for (int i = 0; i < borderGO.transform.childCount; i++)
            {
                Transform ch = borderGO.transform.GetChild(i);
                if (ch == null) continue;
                var im = ch.GetComponent<Image>();
                if (im != null) im.color = tint;
            }
        }

        private static void SetGalleryBorderRectInset(GameObject borderGO, float inset)
        {
            if (borderGO == null) return;
            RectTransform rt = borderGO.GetComponent<RectTransform>();
            if (rt == null) return;
            float i = Mathf.Max(0f, inset);
            rt.offsetMin = new Vector2(i, i);
            rt.offsetMax = new Vector2(-i, -i);
        }

        private static Transform FindScanWlBorderTransform(Transform btnRoot)
        {
            if (btnRoot == null) return null;
            Transform t = btnRoot.Find("Thumbnail/ScanWlBorder");
            if (t != null) return t;
            return btnRoot.Find("ScanWlBorder");
        }

        private static Transform FindScanWlTempBorderTransform(Transform btnRoot)
        {
            if (btnRoot == null) return null;
            Transform t = btnRoot.Find("Thumbnail/ScanWlTempBorder");
            if (t != null) return t;
            return btnRoot.Find("ScanWlTempBorder");
        }

        /// <summary>Applies inward edge strips on thumbnail (grid) or full row (list).</summary>
        private static void ApplyInwardGalleryEdgeBorder(GameObject borderGO, float thickness, Color tint, float frameInset)
        {
            if (borderGO == null) return;
            SetGalleryBorderRectInset(borderGO, frameInset);
            SetBorderThickness(borderGO, thickness);
            SetGalleryInnerBorderEdgeTint(borderGO, tint);
        }

        private void ApplyGalleryScanWlBorderLayer(
            GameObject btnGO,
            bool isListRow,
            Transform borderTr,
            bool packageMatches,
            bool settingsEnabled,
            bool showInLayout,
            float width,
            Color color,
            float gridInset,
            float listInset,
            bool onThumbnail)
        {
            if (btnGO == null || borderTr == null) return;
            GameObject borderGO = borderTr.gameObject;

            bool show = packageMatches && settingsEnabled && width > 0.01f && showInLayout;
            if (!show)
            {
                borderGO.SetActive(false);
                return;
            }

            Transform thumbTr = btnGO.transform.Find("Thumbnail");
            Transform frameParent = isListRow ? btnGO.transform : (onThumbnail && thumbTr != null ? thumbTr : btnGO.transform);
            if (borderTr.parent != frameParent)
                borderTr.SetParent(frameParent, false);

            RectTransform borderRT = borderGO.GetComponent<RectTransform>();
            if (borderRT != null)
            {
                borderRT.anchorMin = Vector2.zero;
                borderRT.anchorMax = Vector2.one;
                borderRT.pivot = new Vector2(0.5f, 0.5f);
                borderRT.anchoredPosition = Vector2.zero;
                borderRT.localScale = Vector3.one;
            }

            float frameInset = isListRow ? listInset : gridInset;
            ApplyInwardGalleryEdgeBorder(borderGO, width, color, frameInset);
            borderTr.SetAsLastSibling();
            borderGO.SetActive(true);
        }

        /// <summary>Persistent inward ring (folder or persisted UID override).</summary>
        private void ApplyScanWhitelistIncludedBorderVisual(GameObject btnGO, FileEntry file, bool isListRow)
        {
            Transform wlTr = FindScanWlBorderTransform(btnGO != null ? btnGO.transform : null);
            ApplyGalleryScanWlBorderLayer(
                btnGO,
                isListRow,
                wlTr,
                ScanWhitelistManager.IsGalleryPersistentScanWhitelistBorderVisible(file),
                EffectiveGalleryScanWlBorderEnabled(),
                isListRow ? EffectiveGalleryScanWlBorderShowInList() : EffectiveGalleryScanWlBorderShowInGrid(),
                EffectiveGalleryScanWlBorderWidth(),
                EffectiveGalleryScanWlBorderColor(),
                EffectiveGalleryScanWlGridFrameInset(),
                EffectiveGalleryScanWlListFrameInset(),
                EffectiveGalleryScanWlBorderOnThumbnail());
        }

        /// <summary>Session-only temporary UID override inward ring.</summary>
        private void ApplyScanWhitelistTemporaryBorderVisual(GameObject btnGO, FileEntry file, bool isListRow)
        {
            Transform wlTr = FindScanWlTempBorderTransform(btnGO != null ? btnGO.transform : null);
            ApplyGalleryScanWlBorderLayer(
                btnGO,
                isListRow,
                wlTr,
                ScanWhitelistManager.IsGalleryTemporaryScanWhitelistBorderVisible(file),
                EffectiveGalleryScanWlTempBorderEnabled(),
                isListRow ? EffectiveGalleryScanWlTempBorderShowInList() : EffectiveGalleryScanWlTempBorderShowInGrid(),
                EffectiveGalleryScanWlTempBorderWidth(),
                EffectiveGalleryScanWlTempBorderColor(),
                EffectiveGalleryScanWlTempGridFrameInset(),
                EffectiveGalleryScanWlTempListFrameInset(),
                EffectiveGalleryScanWlTempBorderOnThumbnail());
        }

        private const float GalleryBadgeSlotStartX = 6f;
        private const float GalleryBadgeSlotStartY = -6f;
        private const float GalleryBadgeSlotStepX = 36f;

        private void ApplyTopLeftBadgeSlot(RectTransform badgeRT, int slotIndex)
        {
            if (badgeRT == null) return;
            badgeRT.anchorMin = new Vector2(0f, 1f);
            badgeRT.anchorMax = new Vector2(0f, 1f);
            badgeRT.pivot = new Vector2(0f, 1f);
            badgeRT.anchoredPosition = new Vector2(
                GalleryBadgeSlotStartX + (GalleryBadgeSlotStepX * Mathf.Max(0, slotIndex)),
                GalleryBadgeSlotStartY
            );
        }

        private static Transform FindGalleryBadgeTransform(Transform btnRoot, string badgeName)
        {
            if (btnRoot == null || string.IsNullOrEmpty(badgeName)) return null;
            FileButtonBinder binder = btnRoot.GetComponent<FileButtonBinder>();
            if (binder != null)
            {
                binder.Ensure();
                Transform cached = binder.GetBadge(badgeName);
                if (cached != null) return cached;
            }
            Transform t = btnRoot.Find(badgeName);
            if (t != null) return t;
            return btnRoot.Find("ListRow/ListBadges/" + badgeName);
        }

        private static void ApplyListRowBadgeSlot(RectTransform badgeRT)
        {
            if (badgeRT == null) return;
            badgeRT.anchorMin = new Vector2(0f, 0.5f);
            badgeRT.anchorMax = new Vector2(0f, 0.5f);
            badgeRT.pivot = new Vector2(0f, 0.5f);
            badgeRT.sizeDelta = new Vector2(32f, 32f);
            badgeRT.anchoredPosition = Vector2.zero;
        }

        private void EnsureGalleryBadgeParentForLayoutMode(GameObject btnGO, bool listMode)
        {
            if (btnGO == null) return;
            FileButtonBinder b = FileButtonBinder.GetOrAdd(btnGO);
            Transform listBadges = b != null ? b.listBadgesTr : btnGO.transform.Find("ListRow/ListBadges");
            if (listBadges == null) return;

            string[] names = FileButtonBinder.TopLeftBadgeNames;
            Transform targetParent = listMode ? listBadges : btnGO.transform;

            for (int i = 0; i < names.Length; i++)
            {
                Transform tr = b != null ? b.GetBadge(names[i]) : FindGalleryBadgeTransform(btnGO.transform, names[i]);
                if (tr != null) tr.SetParent(targetParent, false);
            }

            if (listMode)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    Transform tr = b != null ? b.GetBadge(names[i]) : listBadges.Find(names[i]);
                    if (tr != null) tr.SetSiblingIndex(i);
                }
            }
            else
            {
                for (int i = 0; i < names.Length; i++)
                {
                    Transform tr = b != null ? b.GetBadge(names[i]) : btnGO.transform.Find(names[i]);
                    if (tr != null) ApplyTopLeftBadgeSlot(tr as RectTransform, i);
                }
            }
        }

        /// <summary>List: badges in ListBadges (layout packs inactive). Grid: compact top-left slots for visible badges.</summary>
        private void ApplyDynamicTopLeftBadgeLayout(GameObject btnGO, bool showAutoInstall, bool showHide, bool showWhitelist, bool showUserTags)
        {
            if (btnGO == null) return;
            FileButtonBinder b = FileButtonBinder.GetOrAdd(btnGO);
            string[] names = FileButtonBinder.TopLeftBadgeNames;
            bool[] show = { showAutoInstall, showHide, showWhitelist, showUserTags };

            if (layoutMode == GalleryLayoutMode.Grid)
            {
                int slot = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    Transform tr = b != null ? b.GetBadge(names[i]) : FindGalleryBadgeTransform(btnGO.transform, names[i]);
                    if (tr == null) continue;
                    if (show[i])
                        ApplyTopLeftBadgeSlot(tr as RectTransform, slot++);
                }
                return;
            }

            for (int i = 0; i < names.Length; i++)
            {
                Transform tr = b != null ? b.GetBadge(names[i]) : FindGalleryBadgeTransform(btnGO.transform, names[i]);
                if (tr != null) ApplyListRowBadgeSlot(tr as RectTransform);
            }
        }

        /// <summary>Same black see-through fill as GridLabel / Card name bar.</summary>
        private static readonly Color GalleryItemLabelBarBackdrop = new Color(0f, 0f, 0f, 0.6f);

        // Former solid badge fills → letter colors (lifted for readability on translucent black).
        private static readonly Color GalleryBadgeLetterAutoInstall = new Color(0.35f, 0.65f, 1f, 1f);
        private static readonly Color GalleryBadgeLetterHide = new Color(0.78f, 0.78f, 0.84f, 1f);
        /// <summary>Persistent scan-whitelist inclusion (status teal — not interaction magenta).</summary>
        private static readonly Color GalleryBadgeLetterScanWlPersistent = new Color(0.42f, 0.82f, 0.58f, 1f);
        /// <summary>Session temporary scan-whitelist (amber + ring — shape, not color alone).</summary>
        private static readonly Color GalleryBadgeLetterScanWlTemporary = new Color(0.95f, 0.72f, 0.28f, 1f);
        private static readonly Color GalleryBadgeScanWlTempRingColor = new Color(0.95f, 0.72f, 0.28f, 0.95f);
        private static readonly Color GalleryBadgeLetterScanWlPulse = new Color(1f, 1f, 1f, 1f);
        // Legacy alias used by detail-strip create site.
        private static readonly Color GalleryBadgeLetterScanExcluded = GalleryBadgeLetterScanWlPersistent;
        private static readonly Color GalleryBadgeLetterUserTags = new Color(0.35f, 0.88f, 0.92f, 1f);
        private static readonly Color GalleryBadgeLetterDepsDownload = new Color(0.35f, 0.65f, 1f, 1f);

        /// <summary>Badge fill with gallery corner radius + label-bar translucency.</summary>
        private static RoundedRect AddGalleryBadgeBackground(GameObject go)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = GalleryItemLabelBarBackdrop;
            // Raycast on — UIHoverBorder + tooltips need hits (text stays non-raycast).
            rr.raycastTarget = true;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            EnsureGalleryBadgeHoverBorder(go, rr);
            return rr;
        }

        /// <summary>Temp-WL shape cue: inward ring on badge (not full-cell rim).</summary>
        private static void EnsureScanWlBadgeTempRing(GameObject badgeGO)
        {
            if (badgeGO == null) return;
            Transform existing = badgeGO.transform.Find("TempRing");
            if (existing != null) return;

            GameObject ringGO = new GameObject("TempRing");
            ringGO.transform.SetParent(badgeGO.transform, false);
            RectTransform rt = ringGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            RoundedRectOutline outline = ringGO.AddComponent<RoundedRectOutline>();
            outline.raycastTarget = false;
            outline.borderThickness = 2f;
            outline.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            outline.color = GalleryBadgeScanWlTempRingColor;
            ringGO.SetActive(false);
        }

        /// <summary>
        /// Apply W badge for scan-whitelist inclusion. Persistent = solid letter; temporary = amber letter + ring.
        /// Returns whether badge is shown.
        /// </summary>
        private bool ApplyScanWhitelistBadgeVisual(GameObject badgeGO, FileEntry file)
        {
            if (badgeGO == null) return false;
            EnsureScanWlBadgeTempRing(badgeGO);

            ScanWhitelistManager.GalleryScanWlBadgeKind kind = ScanWhitelistManager.GalleryScanWlBadgeKind.None;
            try { kind = ScanWhitelistManager.GetGalleryScanWhitelistBadgeKind(file); } catch { kind = ScanWhitelistManager.GalleryScanWlBadgeKind.None; }

            bool show = kind != ScanWhitelistManager.GalleryScanWlBadgeKind.None;
            if (badgeGO.activeSelf != show)
                badgeGO.SetActive(show);
            if (!show) return false;

            bool temporary = kind == ScanWhitelistManager.GalleryScanWlBadgeKind.Temporary;
            bool pulsing = IsScanWlBadgePulsing(file);

            Transform textTr = badgeGO.transform.Find("Text");
            Text letter = textTr != null ? textTr.GetComponent<Text>() : badgeGO.GetComponentInChildren<Text>(true);
            if (letter != null)
            {
                letter.text = "W";
                if (pulsing)
                    letter.color = GalleryBadgeLetterScanWlPulse;
                else
                    letter.color = temporary ? GalleryBadgeLetterScanWlTemporary : GalleryBadgeLetterScanWlPersistent;
            }

            Transform ringTr = badgeGO.transform.Find("TempRing");
            if (ringTr != null)
            {
                bool ringOn = temporary || pulsing;
                if (ringTr.gameObject.activeSelf != ringOn)
                    ringTr.gameObject.SetActive(ringOn);
                RoundedRectOutline outline = ringTr.GetComponent<RoundedRectOutline>();
                if (outline != null)
                {
                    outline.color = pulsing ? GalleryBadgeLetterScanWlPulse : GalleryBadgeScanWlTempRingColor;
                    outline.borderThickness = pulsing ? 3f : 2f;
                }
            }

            string tip = temporary
                ? VPBTranslation.T(
                    "gallery.badge.tip.scan_wl_temporary",
                    "Scan whitelist (temporary): included for this session only. Not saved to scan_whitelist.json.")
                : VPBTranslation.T(
                    "gallery.badge.tip.scan_wl_persistent",
                    "Scan whitelist: included via whitelisted folder or saved UID override.");
            AddTooltipPlain(badgeGO, tip);
            return true;
        }

        private bool IsScanWlBadgePulsing(FileEntry file)
        {
            if (file == null || Time.unscaledTime >= _scanWlBadgePulseUntil) return false;
            if (_scanWlBadgePulseUids == null || _scanWlBadgePulseUids.Count == 0) return false;
            string uid = null;
            try { uid = file.Uid; } catch { uid = null; }
            if (string.IsNullOrEmpty(uid)) return false;
            return _scanWlBadgePulseUids.Contains(uid);
        }

        private void BeginScanWlBadgePulse(IEnumerable<string> uids)
        {
            if (uids == null) return;
            if (_scanWlBadgePulseUids == null)
                _scanWlBadgePulseUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            else
                _scanWlBadgePulseUids.Clear();

            foreach (string uid in uids)
            {
                if (!string.IsNullOrEmpty(uid))
                    _scanWlBadgePulseUids.Add(uid);
            }
            if (_scanWlBadgePulseUids.Count == 0) return;

            _scanWlBadgePulseUntil = Time.unscaledTime + ScanWlBadgePulseSeconds;
            if (_scanWlBadgePulseCoroutine == null && isActiveAndEnabled)
                _scanWlBadgePulseCoroutine = StartCoroutine(ScanWlBadgePulseCoroutine());
        }

        private void BeginScanWlBadgePulseFromFiles(List<FileEntry> files)
        {
            if (files == null || files.Count == 0) return;
            var uids = new List<string>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                FileEntry f = files[i];
                if (f == null) continue;
                string uid = null;
                try { uid = f.Uid; } catch { uid = null; }
                if (!string.IsNullOrEmpty(uid)) uids.Add(uid);
            }
            BeginScanWlBadgePulse(uids);
        }

        private System.Collections.IEnumerator ScanWlBadgePulseCoroutine()
        {
            try { RefreshVisibleGridVisualsOnly(); } catch { }
            try { DetailStripRefreshBadgesForSelection(); } catch { }
            while (Time.unscaledTime < _scanWlBadgePulseUntil)
                yield return null;
            if (_scanWlBadgePulseUids != null) _scanWlBadgePulseUids.Clear();
            try { RefreshVisibleGridVisualsOnly(); } catch { }
            try { DetailStripRefreshBadgesForSelection(); } catch { }
            _scanWlBadgePulseCoroutine = null;
        }

        /// <summary>Same yellow rim as CreateUIButton / star rating on hover.</summary>
        private static void EnsureGalleryBadgeHoverBorder(GameObject go, Graphic target = null)
        {
            if (go == null) return;
            UIHoverBorder hb = go.GetComponent<UIHoverBorder>();
            if (hb == null) hb = go.AddComponent<UIHoverBorder>();
            if (target != null) hb.targetGraphic = target;
            else if (hb.targetGraphic == null) hb.targetGraphic = go.GetComponent<Graphic>();
            try { hb.ApplyBorderSettings(); } catch { }
        }

        private static void ApplyGalleryBadgeLabelBarBackdrop(GameObject go)
        {
            if (go == null) return;
            Image img = go.GetComponent<Image>();
            if (img != null)
            {
                img.color = GalleryItemLabelBarBackdrop;
                img.raycastTarget = true;
            }
            EnsureGalleryBadgeHoverBorder(go, img);
        }

        private void GetGridHoverRatingMetrics(out float ratingBadge, out int digitFont, out float edge)
        {
            int badgeFont = UiMetrics.FontBody();
            digitFont = Mathf.Max(badgeFont + 2, Mathf.RoundToInt(badgeFont * 1.15f));
            // Square chrome hugs colored digit — old letterBadge*1.3 left empty band above/below glyph.
            ratingBadge = Mathf.Max(24f, digitFont + 6f);
            edge = Mathf.Max(4f, Mathf.Round(ratingBadge * 0.15f));
        }

        private void SetGridHoverBadgeTooltip(GameObject badgeGO, string tip)
        {
            if (badgeGO == null || string.IsNullOrEmpty(tip)) return;
            Image img = badgeGO.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;
            AddTooltipPlain(badgeGO, tip);
        }

        /// <summary>Grid hover: show top-right colored rating digit for quick rate. Other badges stay on detail strip.</summary>
        internal void ShowGridHoverBadges(GameObject btnGO, FileEntry file)
        {
            if (btnGO == null || file == null) return;
            if (layoutMode != GalleryLayoutMode.Grid) return;
            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryGridHoverBadgesEnabled)
            {
                HideGridHoverBadges(btnGO);
                return;
            }

            GetGridHoverRatingMetrics(out float ratingBadge, out int digitFont, out float edge);

            Transform ratingTr = btnGO.transform.Find("Rating");
            Transform selectorTr = btnGO.transform.Find("RatingSelector");
            // Grid bind keeps RatingSelector inactive — must enable or star click does nothing.
            if (selectorTr != null)
            {
                selectorTr.gameObject.SetActive(true);
                selectorTr.SetAsLastSibling();
            }
            if (ratingTr != null)
            {
                ratingTr.gameObject.SetActive(true);
                ratingTr.SetAsLastSibling();
                RectTransform ratingRT = ratingTr as RectTransform;
                if (ratingRT != null)
                {
                    ratingRT.sizeDelta = new Vector2(ratingBadge, ratingBadge);
                    ratingRT.anchoredPosition = new Vector2(-edge, -edge);
                }
                Transform starTr = ratingTr.Find("Star");
                if (starTr != null)
                {
                    ApplyGalleryBadgeLabelBarBackdrop(starTr.gameObject);
                    RectTransform starRT = starTr as RectTransform;
                    if (starRT != null)
                    {
                        starRT.anchorMin = new Vector2(0.5f, 0.5f);
                        starRT.anchorMax = new Vector2(0.5f, 0.5f);
                        starRT.pivot = new Vector2(0.5f, 0.5f);
                        starRT.anchoredPosition = Vector2.zero;
                        starRT.sizeDelta = new Vector2(ratingBadge, ratingBadge);
                    }
                    Text starLabel = starTr.GetComponentInChildren<Text>(true);
                    if (starLabel != null)
                    {
                        starLabel.fontSize = digitFont;
                        starLabel.transform.localScale = Vector3.one;
                    }
                }
                if (selectorTr != null)
                {
                    selectorTr.SetAsLastSibling();
                    // 2×3 grid: panel size from cell size so option backdrops hug the digits.
                    float cellW = Mathf.Max(28f, ratingBadge * 0.95f);
                    float cellH = Mathf.Max(26f, digitFont + 8f);
                    float gap = 2f;
                    float pad = 1f;
                    RectTransform selRT = selectorTr as RectTransform;
                    if (selRT != null)
                    {
                        float selW = pad + cellW + gap + cellW + pad;
                        float selH = pad + cellH + gap + cellH + gap + cellH + pad;
                        selRT.sizeDelta = new Vector2(selW, selH);
                        selRT.anchoredPosition = new Vector2(-edge, -(ratingBadge + edge));
                    }
                    GridLayoutGroup glg = selectorTr.GetComponent<GridLayoutGroup>();
                    if (glg != null)
                    {
                        glg.cellSize = new Vector2(cellW, cellH);
                        glg.spacing = new Vector2(gap, gap);
                        glg.padding = new RectOffset(
                            Mathf.RoundToInt(pad), Mathf.RoundToInt(pad),
                            Mathf.RoundToInt(pad), Mathf.RoundToInt(pad));
                    }
                    int optFont = Mathf.Max(14, digitFont);
                    for (int i = 0; i < selectorTr.childCount; i++)
                    {
                        Transform opt = selectorTr.GetChild(i);
                        if (opt == null) continue;
                        RectTransform optRT = opt as RectTransform;
                        if (optRT != null) optRT.sizeDelta = new Vector2(cellW, cellH);
                        Text optLabel = opt.GetComponentInChildren<Text>(true);
                        if (optLabel != null) optLabel.fontSize = optFont;
                    }
                }

                RatingHandler rh = btnGO.GetComponent<RatingHandler>();
                if (rh != null)
                {
                    rh.panel = this;
                    // Digit+color always — same pattern as list / creator ratings.
                    rh.SetShowDigitMode(true);
                    // Don't close if picker already open (re-enter / tooltip child hops).
                    if (!rh.IsSelectorOpen)
                        rh.CloseSelector();
                    int r = rh.CurrentRating;
                    string rateTip = r <= 0
                        ? VPBTranslation.T("gallery.badge.tip.rating_none", "Rating: unrated. Click to set 0–5.")
                        : string.Format(
                            VPBTranslation.T("gallery.badge.tip.rating", "Rating: {0} of 5. Click to change."),
                            r);
                    SetGridHoverBadgeTooltip(ratingTr.gameObject, rateTip);
                    if (starTr != null) SetGridHoverBadgeTooltip(starTr.gameObject, rateTip);
                }
            }

            _gridHoverBadgeBtnGO = btnGO;
        }

        /// <summary>Grid hover exit / recycle: deactivate rating badge on this cell. No-op outside grid.</summary>
        /// <param name="force">Recycle/disable must force-close even if rating picker is open.</param>
        internal void HideGridHoverBadges(GameObject btnGO, bool force = false)
        {
            if (btnGO == null || layoutMode != GalleryLayoutMode.Grid) return;
            RatingHandler rh = null;
            try { rh = btnGO.GetComponent<RatingHandler>(); } catch { }

            // Keep rating chrome while picker is open — hover-exit used to kill ToggleSelector immediately.
            if (!force && rh != null && rh.IsSelectorOpen)
                return;

            try
            {
                if (rh != null)
                    rh.CloseSelector();
            }
            catch { }

            Transform selectorTr = btnGO.transform.Find("RatingSelector");
            if (selectorTr != null && selectorTr.gameObject.activeSelf)
                selectorTr.gameObject.SetActive(false);

            Transform ratingTr = btnGO.transform.Find("Rating");
            if (ratingTr != null && ratingTr.gameObject.activeSelf)
                ratingTr.gameObject.SetActive(false);

            string[] badgeNames = FileButtonBinder.GridBadgeHideNames;
            for (int bi = 0; bi < badgeNames.Length; bi++)
            {
                Transform t = FindGalleryBadgeTransform(btnGO.transform, badgeNames[bi]);
                if (t != null && t.gameObject.activeSelf)
                    t.gameObject.SetActive(false);
            }

            if (_gridHoverBadgeBtnGO == btnGO)
                _gridHoverBadgeBtnGO = null;
        }

        // Scale list-row fonts + sub-row heights off row height (ref 100) so the stacked content stays
        // proportional and fits the cell at any zoom; bases are constants so repeated binds don't compound.
        private void ApplyListRowScale(Transform listRowTr, float rowHeight)
        {
            if (listRowTr == null) return;
            float s = settingsListViewActive
                ? Mathf.Clamp(InternalSettingsChromeScale(), 0.6f, 2.0f)
                : Mathf.Clamp(rowHeight / 100f, 0.6f, 2.0f);

            VerticalLayoutGroup vlg = listRowTr.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                int p = Mathf.RoundToInt(5f * s);
                vlg.padding = new RectOffset(p, p, p, p);
            }

            Transform nameTr = listRowTr.Find("Name");
            if (nameTr != null)
            {
                Text t = nameTr.GetComponent<Text>();
                if (t != null)
                {
                    if (settingsListViewActive)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.SettingsListRowNameFontRef, s, GalleryUiDesignTokens.FontMinRef);
                    else
                        t.fontSize = Mathf.RoundToInt(GalleryUiDesignTokens.FontRef * s);
                    t.fontStyle = FontStyle.Normal;
                }
                LayoutElement le = nameTr.GetComponent<LayoutElement>();
                if (le != null)
                    le.minHeight = (settingsListViewActive ? GalleryUiDesignTokens.ButtonSizeRef : 32f) * s;
            }

            Transform detailsTr = listRowTr.Find("Details");
            if (detailsTr != null)
            {
                LayoutElement le = detailsTr.GetComponent<LayoutElement>();
                if (le != null) le.minHeight = 24f * s;
                int detailFont = Mathf.RoundToInt(GalleryUiDesignTokens.FontBodyRef * s);
                int dn = detailsTr.childCount;
                for (int i = 0; i < dn; i++)
                {
                    Text ct = detailsTr.GetChild(i).GetComponent<Text>();
                    if (ct != null) ct.fontSize = detailFont;
                }
            }

            Transform badgesTr = listRowTr.Find("ListBadges");
            if (badgesTr != null)
            {
                LayoutElement le = badgesTr.GetComponent<LayoutElement>();
                if (le != null) { le.minHeight = 32f * s; le.preferredHeight = 32f * s; }
                int badgeFont = Mathf.RoundToInt(GalleryUiDesignTokens.FontBodyRef * s);
                float badgeSize = 32f * s;
                int bn = badgesTr.childCount;
                for (int i = 0; i < bn; i++)
                {
                    Transform b = badgesTr.GetChild(i);
                    LayoutElement ble = b.GetComponent<LayoutElement>();
                    if (ble != null)
                    {
                        ble.preferredWidth = badgeSize; ble.minWidth = badgeSize;
                        ble.preferredHeight = badgeSize; ble.minHeight = badgeSize;
                    }
                    Text bt = b.GetComponentInChildren<Text>();
                    if (bt != null) bt.fontSize = badgeFont;
                }
            }
        }

        public void UpdateFileButtonVisuals(GameObject btnGO, FileEntry file)
        {
            if (btnGO == null)
            {
                LogUtil.LogError("[VPB] UpdateFileButtonVisuals: btnGO is null");
                return;
            }
            if (file == null)
            {
                LogUtil.LogError("[VPB] UpdateFileButtonVisuals: file is null");
                return;
            }

            FileButtonBinder b = FileButtonBinder.GetOrAdd(btnGO);
            Image img = b != null ? b.image : btnGO.GetComponent<Image>();
            string selKey = GetSelectionIdentityKey(file, false);
            bool isSelected = (!string.IsNullOrEmpty(selKey) && selectedFilePaths.Contains(selKey));

            bool isListRow = layoutMode == GalleryLayoutMode.List || settingsListViewActive;
            if (img != null)
            {
                if (isListRow)
                {
                    bool isMaster = false;
                    try { isMaster = IsFilterActive && IsFilterMasterEntry(file); } catch { isMaster = false; }
                    img.color = isMaster ? new Color(0.1f, 0.25f, 0.45f, 0.55f) : new Color(0f, 0f, 0f, 0.4f);
                }
                else if (isSelected)
                    img.color = new Color(0.7f, 0.7f, 0.2f, 1f);
                else
                    img.color = Color.gray;
            }

            FileButtonBinder.SetActive(b != null ? b.listHoverBarTr : null, false);
            FileButtonBinder.SetActive(b != null ? b.listSelectionBarTr : null, false);

            float hoverW = EffectiveGridHoverBorderWidth();
            float selW = EffectiveGridSelectedBorderWidth();
            float w = isSelected ? selW : hoverW;
            bool inwardCell = EffectiveGridBorderInwardForGalleryCell();
            Color borderTint = EffectiveGalleryGridBorderColor();

            UIHoverBorder hoverBorder = b != null ? b.hoverBorder : btnGO.GetComponent<UIHoverBorder>();
            if (hoverBorder != null)
            {
                hoverBorder.enabled = true;
                hoverBorder.hoverColor = borderTint;
            }
            Transform innerBorderTr = b != null ? b.gridInnerBorderTr : btnGO.transform.Find("GridInnerBorder");
            GameObject innerBorderGO = innerBorderTr != null ? innerBorderTr.gameObject : null;
            bool useInner = inwardCell && innerBorderGO != null;

            if (useInner)
            {
                try { var oldO = btnGO.GetComponent<Outline>(); if (oldO != null) Destroy(oldO); } catch { }
                if (hoverBorder != null)
                {
                    hoverBorder.hoverBorderGO = innerBorderGO;
                    hoverBorder.hoverIndicatorUsesSeparateSelectionVisual = false;
                    hoverBorder.isSelected = isSelected;
                }
                SetGalleryBorderRectInset(innerBorderGO, 0f);
                SetBorderThickness(innerBorderGO, w);
                SetGalleryInnerBorderEdgeTint(innerBorderGO, borderTint);
                innerBorderGO.SetActive(isSelected);
            }
            else
            {
                bool listOutlineInwardFallback = isListRow;
                if (hoverBorder != null)
                {
                    hoverBorder.hoverBorderGO = null;
                    hoverBorder.hoverIndicatorUsesSeparateSelectionVisual = false;
                    hoverBorder.isSelected = isSelected;
                    hoverBorder.borderSize = w;
                    hoverBorder.inward = listOutlineInwardFallback;
                    hoverBorder.ApplyBorderSettings();
                }
                if (innerBorderGO != null)
                {
                    SetGalleryBorderRectInset(innerBorderGO, 0f);
                    innerBorderGO.SetActive(false);
                }
            }
            ApplyUserTagDropVisual(btnGO, file);
            ApplyScanWhitelistIncludedBorderVisual(btnGO, file, isListRow);
            ApplyScanWhitelistTemporaryBorderVisual(btnGO, file, isListRow);
            if (!isListRow)
                try { ApplyGridCellChromeScale(btnGO); } catch { }
        }

        public void BindFileButton(GameObject btnGO, FileEntry file)
        {
            // Validate inputs
            if (btnGO == null || file == null)
            {
                LogUtil.LogError("[VPB] BindFileButton: btnGO or file is null");
                return;
            }

            FileButtonBinder b = FileButtonBinder.GetOrAdd(btnGO);

            // File rows pooled/reused across modes (including Settings).
            // Clear prior hover handlers (e.g. settings tooltips) so they don't leak into other categories.
            UIHoverDelegate hoverDelReset = b != null ? b.hoverDelegate : btnGO.GetComponent<UIHoverDelegate>();
            if (hoverDelReset != null)
            {
                hoverDelReset.OnHoverChange = null;
                hoverDelReset.OnPointerEnterEvent = null;
            }
            // Restore baseline hover tracking for this row.
            AddHoverDelegate(btnGO);
            if (b != null) b.hoverDelegate = btnGO.GetComponent<UIHoverDelegate>();

            // Identity key (Path preferred; fall back to Uid). Needed because some rows (e.g. ALL VAR package list)
            // can arrive from SQLite without a resolved/installed var path hint.
            string idKey = GetSelectionIdentityKey(file, false);
            if (string.IsNullOrEmpty(file.Name) && string.IsNullOrEmpty(idKey))
            {
                LogUtil.LogError($"[VPB] BindFileButton: Invalid entry - Name={file.Name}, Path={file.Path}, Uid={file.Uid}");
                // Still clear thumbnail so pooled rows don't show stale previews
                try
                {
                    RawImage ri = b != null ? b.thumbRaw : null;
                    if (ri == null)
                    {
                        Transform thumbTrBad = btnGO.transform.Find("Thumbnail");
                        if (thumbTrBad == null) thumbTrBad = btnGO.transform.Find("ThumbContainer/Thumbnail");
                        if (thumbTrBad != null) ri = thumbTrBad.GetComponent<RawImage>();
                    }
                    if (ri != null) LoadThumbnail(file, ri);
                }
                catch { }
                return;
            }

            btnGO.name = "FileButton_" + (file.Name ?? idKey ?? "Unknown");

            // Recycled row defaults (TextArea settings rows disable root raycast — restore before rebind)
            Button rb0 = b != null ? b.button : btnGO.GetComponent<Button>();
            if (rb0 != null) rb0.interactable = true;
            Image ri0 = b != null ? b.image : btnGO.GetComponent<Image>();
            if (ri0 != null) ri0.raycastTarget = true;
            UIFileEntryLeftReleaseSelect lu0 = b != null ? b.leftRelease : btnGO.GetComponent<UIFileEntryLeftReleaseSelect>();
            if (lu0 != null) lu0.enabled = true;

            // Update mapping
            Image img = ri0;
            if (img != null)
            {
                // Map by identity key so empty Path rows don't collide
                if (!string.IsNullOrEmpty(idKey))
                    fileButtonImages[idKey] = img;
            }

            // Color missing entries red
            if (file is VirtualFileEntry && !(file is InternalSettingRowEntry))
            {
                if (img != null) img.color = new Color(0.4f, 0.15f, 0.15f, 0.8f); // Red shade
            }

            // Update Visuals
            UpdateFileButtonVisuals(btnGO, file);

            // Button + row pointer routing (left/right/middle)
            UIFileEntryLeftReleaseSelect leftUp = b != null ? b.leftRelease : null;
            if (leftUp == null)
            {
                leftUp = btnGO.GetComponent<UIFileEntryLeftReleaseSelect>();
                if (leftUp == null) leftUp = btnGO.AddComponent<UIFileEntryLeftReleaseSelect>();
                if (b != null) b.leftRelease = leftUp;
            }
            leftUp.Panel = this;
            leftUp.File = file;
            leftUp.enabled = true;

            Button btn = rb0;
            if (btn != null) btn.onClick.RemoveAllListeners();

            bool isListMode = (layoutMode == GalleryLayoutMode.List);
            bool isSettingsRow = file is InternalSettingRowEntry;

            if (isSettingsRow)
            {
                // Special settings list-row mode: no package affordances (thumb/rating/badges/meta columns).
                Transform listRowTrSpecial = b != null ? b.listRowTr : btnGO.transform.Find("ListRow");
                if (listRowTrSpecial != null)
                {
                    listRowTrSpecial.gameObject.SetActive(true);
                    RectTransform listRowRT = b != null && b.listRowRT != null ? b.listRowRT : listRowTrSpecial as RectTransform;
                    if (listRowRT != null)
                    {
                        listRowRT.offsetMin = new Vector2(8, 0);
                        listRowRT.offsetMax = new Vector2(-8, 0);
                    }

                    Transform nameTr = b != null ? b.listNameTr : listRowTrSpecial.Find("Name");
                    if (nameTr != null)
                    {
                        Text t = b != null && b.listNameText != null ? b.listNameText : nameTr.GetComponent<Text>();
                        if (t != null)
                        {
                            t.text = file.Name ?? "";
                            t.alignment = TextAnchor.MiddleLeft;
                        }
                    }

                    Transform detailsTr = b != null ? b.listDetailsTr : listRowTrSpecial.Find("Details");
                    if (detailsTr != null) detailsTr.gameObject.SetActive(false);
                }

                ConfigureInternalSettingsRowUI(btnGO, file);

                {
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.interactable = false;
                    }
                    if (img != null) img.raycastTarget = false;
                    if (leftUp != null) leftUp.enabled = false;
                }

                FileButtonBinder.SetActive(b != null ? b.gridLabelTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.thumbTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.ratingTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.ratingSelectorTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.autoInstallBadgeTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.hidePackageBadgeTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.scanExcludedBadgeTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.userTagsBadgeTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.depsBadgeTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.depsDownloadBtnTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.listHoverBarTr : null, false);
                FileButtonBinder.SetActive(b != null ? b.listSelectionBarTr : null, false);

                UIHoverReveal hoverSpecial = b != null ? b.hoverReveal : btnGO.GetComponent<UIHoverReveal>();
                if (hoverSpecial != null) hoverSpecial.file = null;
                HoldToApplyOnHover holdSpecial = b != null ? b.holdToApply : btnGO.GetComponent<HoldToApplyOnHover>();
                if (holdSpecial != null) holdSpecial.enabled = false;
                return;
            }

            EnsureFileEntryPointerForwarding(btnGO, leftUp);

            // Reset any settings-only controls on recycled rows when binding normal files.
            Transform listRowTrReset = b != null ? b.listRowTr : btnGO.transform.Find("ListRow");
            if (listRowTrReset != null)
            {
                Transform detailsTrReset = b != null ? b.listDetailsTr : listRowTrReset.Find("Details");
                if (detailsTrReset != null)
                {
                    for (int i = 0; i < detailsTrReset.childCount; i++)
                    {
                        Transform ch = detailsTrReset.GetChild(i);
                        if (ch == null) continue;
                        if (string.Equals(ch.name, "SettingsControlContainer", StringComparison.Ordinal)
                            || string.Equals(ch.name, "SettingsHotkeyHost", StringComparison.Ordinal))
                        {
                            UnityEngine.Object.Destroy(ch.gameObject);
                            continue;
                        }
                        ch.gameObject.SetActive(true);
                    }
                    // Settings rows deactivate the Details root; the loop only reactivates children, so
                    // re-enable the root or a normal row recycled from a settings row shows a blank Details line.
                    if (!detailsTrReset.gameObject.activeSelf) detailsTrReset.gameObject.SetActive(true);
                }
            }

            EnsureGalleryBadgeParentForLayoutMode(btnGO, isListMode);

            // List Row + Rating selector visibility (List/Table mode)
            Transform listRowTr = b != null ? b.listRowTr : btnGO.transform.Find("ListRow");
            if (listRowTr != null)
            {
                listRowTr.gameObject.SetActive(isListMode);
                if (isListMode)
                {
                    RectTransform listRowRT = b != null && b.listRowRT != null ? b.listRowRT : listRowTr as RectTransform;
                    if (listRowRT != null)
                    {
                        float leftPad = listThumbSize + 15f;
                        listRowRT.offsetMin = new Vector2(leftPad, 0);
                        listRowRT.offsetMax = new Vector2(-50, 0);
                    }
                }
            }

            if (isListMode)
            {
                Transform gridLabelTr = b != null ? b.gridLabelTr : btnGO.transform.Find("GridLabel");
                if (gridLabelTr != null && gridLabelTr.gameObject.activeSelf)
                    gridLabelTr.gameObject.SetActive(false);
            }

            // CloseSelector first — grid picker may be reparented under backgroundBoxGO while open.
            RatingHandler rhSel = b != null ? b.ratingHandler : btnGO.GetComponent<RatingHandler>();
            if (rhSel != null) rhSel.CloseSelector();
            Transform selectorTr = b != null ? b.ratingSelectorTr : btnGO.transform.Find("RatingSelector");
            if (selectorTr != null)
            {
                if (isListMode)
                    selectorTr.gameObject.SetActive(true);
                else
                    selectorTr.gameObject.SetActive(false);
            }

            // Card Container (Hidden in List mode, Visible in Grid mode? No, Card is for VerticalCard mode which is removed or mapped to Grid if we had it)
            // Wait, Grid mode uses the old style overlay? Or does Grid mode use Card?
            // In the previous code, Grid mode had "Card" active only if VerticalCard.
            // layoutMode == GalleryLayoutMode.Grid means standard grid which usually has hover reveal or overlay.
            // Let's check CreateNewFileButtonGO. CardGO is hidden by default.

            Transform cardTr = b != null ? b.cardTr : btnGO.transform.Find("Card");
            if (cardTr != null)
            {
                // In the new 2-mode system, Grid usually implies the simple thumbnail + optional overlay.
                // If we want "Grid" to look like cards, we set this true.
                // But typically Grid = just thumbnail with hover name.
                // VerticalCard was the one with persistent text below.
                // Since we only have Grid and List, let's assume Grid means "Thumbnail Grid".

                // So Card is hidden in both Grid (standard) and List.
                cardTr.gameObject.SetActive(false);
            }

            // Thumbnail
            Transform thumbTr = b != null ? b.thumbTr : null;
            if (thumbTr == null)
            {
                thumbTr = btnGO.transform.Find("Thumbnail");
                if (thumbTr == null) thumbTr = btnGO.transform.Find("ThumbContainer/Thumbnail");
            }

            if (thumbTr != null)
            {
                if (!thumbTr.gameObject.activeSelf) thumbTr.gameObject.SetActive(true);
                RectTransform thumbRT = b != null && b.thumbRT != null ? b.thumbRT : thumbTr as RectTransform;

                if (isListMode)
                {
                    // Full height square on left
                    thumbRT.anchorMin = new Vector2(0, 0);
                    thumbRT.anchorMax = new Vector2(0, 1);
                    thumbRT.pivot = new Vector2(0, 0.5f);
                    thumbRT.offsetMin = new Vector2(0, 0);
                    thumbRT.offsetMax = new Vector2(listThumbSize, 0);
                }
                else
                {
                    thumbRT.anchorMin = Vector2.zero;
                    thumbRT.anchorMax = Vector2.one;
                    thumbRT.pivot = new Vector2(0.5f, 0.5f);
                    thumbRT.anchoredPosition = Vector2.zero;
                    float pad = 3f;
                    try { if (VPBConfig.Instance != null) pad = Mathf.Clamp(VPBConfig.Instance.GalleryGridThumbnailPadding, 0f, 40f); } catch { pad = 3f; }
                    thumbRT.offsetMin = new Vector2(pad, pad);
                    thumbRT.offsetMax = new Vector2(-pad, -pad);
                    ApplyGridLabelStripLayout(btnGO, file);
                }

                RawImage thumbImg = b != null ? b.thumbRaw : thumbTr.GetComponent<RawImage>();
                if (thumbImg != null)
                {
                    // Let LoadThumbnail decide whether this is a true rebind or the same
                    // thumbnail; unconditional clearing causes a visible flash on reopen.
                    bool forcePluginLabelsOnly = ShouldForcePluginsCategoryLabelOnly(file);
                    if (forcePluginLabelsOnly)
                        ClearThumbnailTarget(thumbImg);
                    else
                        LoadThumbnail(file, thumbImg);
                    ResolveThumbPlaceholderUi(
                        thumbTr,
                        thumbImg,
                        file,
                        isListMode,
                        out bool noUableThumb,
                        out bool showThumbLabels);
                    ApplyPluginThumbPlaceholder(thumbTr, thumbImg, file, isListMode, showThumbLabels);

                    // List-layout hover preview: bind hover handler to the thumbnail only.
                    // (Use the thumbnail rect so the full row doesn't trigger the popup.)
                    UIHoverPreviewTrigger hp = b != null ? b.hoverPreview : null;
                    if (hp == null)
                    {
                        hp = thumbTr.GetComponent<UIHoverPreviewTrigger>();
                        if (hp == null) hp = thumbTr.gameObject.AddComponent<UIHoverPreviewTrigger>();
                        if (b != null) b.hoverPreview = hp;
                    }
                    hp.panel = this;
                    hp.file = file;
                    hp.SyncHoverPreviewAfterRebind();
                    thumbImg.raycastTarget = true;
                    // RawImage steals raycasts; forward to row root handler (UIDraggableItem + slop live on btnGO).
                    try
                    {
                        var staleThumbLu = thumbTr.gameObject.GetComponent<UIFileEntryLeftReleaseSelect>();
                        if (staleThumbLu != null) UnityEngine.Object.Destroy(staleThumbLu);
                        if (leftUp != null)
                        {
                            UIFileEntryPointerForwarder fwd = b != null ? b.thumbPointerFwd : null;
                            if (fwd == null)
                            {
                                fwd = thumbTr.gameObject.GetComponent<UIFileEntryPointerForwarder>();
                                if (b != null) b.thumbPointerFwd = fwd;
                            }
                            if (fwd != null)
                            {
                                fwd.Target = leftUp;
                                fwd.ForwardLeftPointerUp = true;
                            }
                        }
                    }
                    catch { }
                }
            }

            // Hide NavText
            Transform navTextTr = b != null ? b.navTextTr : btnGO.transform.Find("NavText");
            if (navTextTr != null && navTextTr.gameObject.activeSelf) navTextTr.gameObject.SetActive(false);

            // Hover Path
            UIHoverReveal hover = b != null ? b.hoverReveal : btnGO.GetComponent<UIHoverReveal>();
            if (hover != null) hover.file = file;

            // Hold-to-launch/apply: pointer must stay pressed; duration from VPBConfig.HoldToLaunchHoldSeconds.
            // Kept always attached for pooling; enabled/disabled by panel toggle at runtime.
            try
            {
                HoldToApplyOnHover h = b != null ? b.holdToApply : null;
                if (h == null)
                {
                    h = btnGO.GetComponent<HoldToApplyOnHover>();
                    if (h == null) h = btnGO.AddComponent<HoldToApplyOnHover>();
                    if (b != null) b.holdToApply = h;
                }
                h.panel = this;
                h.file = file;
            }
            catch { }

            // Label
            Transform labelTr = b != null ? b.cardLabelTr : btnGO.transform.Find("Card/Label");
            if (labelTr != null)
            {
                Text labelText = b != null && b.cardLabelText != null ? b.cardLabelText : labelTr.GetComponent<Text>();
                if (labelText != null)
                {
                    bool pretty = VPBConfig.Instance != null && VPBConfig.Instance.GalleryPrettyPresetNames;
                    string displayName = pretty
                        ? GetPrettyEntryDisplayName(file)
                        : (string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name);
                    labelText.text = displayName;
                    // Tooltip now surfaces internal path so users can see where the preset lives; falls back to package uid if path missing.
                    try
                    {
                        if (file is VarFileEntry vfe && vfe.Package != null)
                        {
                            string hint = string.IsNullOrEmpty(vfe.InternalPath)
                                ? string.Format(VPBTranslation.T("gallery.tooltip.package_uid", "Package: {0}.var"), vfe.Package.Uid)
                                : vfe.InternalPath.Replace('\\', '/');
                            AddTooltipPlain(labelTr.gameObject, hint);
                        }
                    }
                    catch { }
                }
            }

            // List: star + full badge strip. Grid: thumbnail + GridLabel only; all badges hidden.
            Transform ratingTr = b != null ? b.ratingTr : btnGO.transform.Find("Rating");
            if (isListMode)
            {
                if (ratingTr != null)
                    ratingTr.gameObject.SetActive(true);

                bool showAutoInstallBadge = file.IsAutoInstall();
                Transform aiBadgeTr = b != null ? b.autoInstallBadgeTr : FindGalleryBadgeTransform(btnGO.transform, "AutoInstallBadge");
                if (aiBadgeTr != null)
                    aiBadgeTr.gameObject.SetActive(showAutoInstallBadge);

                bool showHideBadge = PackageHidePrefs.IsGalleryHideBadgeVisible(file);
                Transform hideBadgeTr = b != null ? b.hidePackageBadgeTr : FindGalleryBadgeTransform(btnGO.transform, "HidePackageBadge");
                if (hideBadgeTr != null)
                    hideBadgeTr.gameObject.SetActive(showHideBadge);

                Transform scanExBadgeTr = b != null ? b.scanExcludedBadgeTr : FindGalleryBadgeTransform(btnGO.transform, "ScanExcludedBadge");
                bool showScanWlBadge = ApplyScanWhitelistBadgeVisual(scanExBadgeTr != null ? scanExBadgeTr.gameObject : null, file);

                bool showUserTagsBadge = IsGalleryUserTagBadgeVisible(file);
                Transform userTagsBadgeTr = b != null ? b.userTagsBadgeTr : FindGalleryBadgeTransform(btnGO.transform, "UserTagsBadge");
                if (userTagsBadgeTr != null)
                    userTagsBadgeTr.gameObject.SetActive(showUserTagsBadge);

                ApplyDynamicTopLeftBadgeLayout(btnGO, showAutoInstallBadge, showHideBadge, showScanWlBadge, showUserTagsBadge);

                // An empty strip still reserves its row height in the VLG and pushes the Details line
                // out of a compact row; deactivate it so the group ignores it when no badge shows.
                bool anyListBadge = showAutoInstallBadge || showHideBadge || showScanWlBadge || showUserTagsBadge;
                if (listRowTr != null)
                {
                    Transform listBadgesRowTr = b != null ? b.listBadgesTr : listRowTr.Find("ListBadges");
                    if (listBadgesRowTr != null && listBadgesRowTr.gameObject.activeSelf != anyListBadge)
                        listBadgesRowTr.gameObject.SetActive(anyListBadge);
                }
            }
            else
            {
                // Grid: only W badge is ambient status. Other badges stay off (detail strip / hover rating).
                if (ratingTr != null)
                    ratingTr.gameObject.SetActive(false);

                string[] badgeNames = FileButtonBinder.GridBadgeHideNames;
                for (int bi = 0; bi < badgeNames.Length; bi++)
                {
                    Transform t = b != null ? b.GetBadge(badgeNames[bi]) : FindGalleryBadgeTransform(btnGO.transform, badgeNames[bi]);
                    if (t != null) t.gameObject.SetActive(false);
                }

                Transform scanExBadgeTr = b != null ? b.scanExcludedBadgeTr : FindGalleryBadgeTransform(btnGO.transform, "ScanExcludedBadge");
                bool showScanWlBadge = ApplyScanWhitelistBadgeVisual(scanExBadgeTr != null ? scanExBadgeTr.gameObject : null, file);
                ApplyDynamicTopLeftBadgeLayout(btnGO, false, false, showScanWlBadge, false);
            }

            // List Row Bind
            if (isListMode)
            {
                if (listRowTr != null && !listRowTr.gameObject.activeSelf) listRowTr.gameObject.SetActive(true);

                ApplyListRowScale(listRowTr, EffectiveListRowHeightForGallery());

                Transform nameTr = b != null ? b.listNameTr : btnGO.transform.Find("ListRow/Name");
                if (nameTr != null)
                {
                    Text t = b != null && b.listNameText != null ? b.listNameText : nameTr.GetComponent<Text>();
                    if (t != null)
                    {
                        string displayName = GetGalleryListRowDisplayName(file);
                        t.text = displayName;
                        SetGalleryListRowNameTooltip(nameTr.gameObject, file);
                    }
                }

                Transform depsTr = b != null ? b.depsTr : btnGO.transform.Find("ListRow/Details/Deps");
                if (depsTr != null)
                {
                    Text t = b != null && b.depsText != null ? b.depsText : depsTr.GetComponent<Text>();
                    if (t != null)
                    {
                        int deps = GallerySortManager.GetDepsCount(file);
                        string v = deps.ToString().PadLeft(3);
                        t.text = "D: " + v + "  |  ";
                        t.raycastTarget = true;

                        // Hover-highlight only the value; Set() resets color on recycle.
                        try
                        {
                            UIRichValueHover hv = b != null ? b.EnsureDepsHover() : null;
                            if (hv == null)
                            {
                                hv = depsTr.GetComponent<UIRichValueHover>();
                                if (hv == null) hv = depsTr.gameObject.AddComponent<UIRichValueHover>();
                            }
                            hv.target = t;
                            hv.Set("D: ", v, "  |  ");
                        }
                        catch { }
                    }
                    // Keep ScrollRect scrolling even when hovering over clickable text.
                    try
                    {
                        UIScrollPassthrough sp = b != null ? b.EnsureDepsScrollPassthrough() : null;
                        if (sp == null)
                        {
                            sp = depsTr.GetComponent<UIScrollPassthrough>();
                            if (sp == null) sp = depsTr.gameObject.AddComponent<UIScrollPassthrough>();
                        }
                        sp.target = scrollRect;
                    }
                    catch { }
                    // Make clickable to filter by dependencies using EventTrigger (non-invasive)
                    EventTrigger et = b != null ? b.EnsureDepsEventTrigger() : null;
                    if (et == null)
                    {
                        et = depsTr.GetComponent<EventTrigger>();
                        if (et == null) et = depsTr.gameObject.AddComponent<EventTrigger>();
                    }
                    var pointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                    pointerClickEntry.callback.AddListener((data) => {
                        if (GallerySortManager.GetDepsCount(file) > 0)
                            ApplyDependenciesFilter(file);
                    });
                    et.triggers.Clear();
                    et.triggers.Add(pointerClickEntry);
                    // Add tooltip
                    try { AddTooltip(depsTr.gameObject, "gallery.tooltip.dependencies", "Dependencies"); } catch { }
                }

                Transform missingTr = b != null ? b.missingTr : btnGO.transform.Find("ListRow/Details/Missing");
                if (missingTr != null)
                {
                    Text t = b != null && b.missingText != null ? b.missingText : missingTr.GetComponent<Text>();
                    if (t != null)
                    {
                        int missing = GallerySortManager.GetMissingDepsCount(file);
                        string v = missing.ToString().PadLeft(3);
                        t.text = "M: " + v + "  |  ";
                        t.raycastTarget = true;

                        // Hover-highlight only the value; Set() resets color on recycle.
                        try
                        {
                            UIRichValueHover hv = b != null ? b.EnsureMissingHover() : null;
                            if (hv == null)
                            {
                                hv = missingTr.GetComponent<UIRichValueHover>();
                                if (hv == null) hv = missingTr.gameObject.AddComponent<UIRichValueHover>();
                            }
                            hv.target = t;
                            hv.useConditionalColoring = true;
                            hv.zeroValueColor = Color.green;  // Green when no missing
                            hv.nonZeroValueColor = Color.red; // Red when missing deps exist
                            hv.Set("M: ", v, "  |  ");
                        }
                        catch { }
                    }
                    // Keep ScrollRect scrolling even when hovering over clickable text.
                    try
                    {
                        UIScrollPassthrough sp = b != null ? b.EnsureMissingScrollPassthrough() : null;
                        if (sp == null)
                        {
                            sp = missingTr.GetComponent<UIScrollPassthrough>();
                            if (sp == null) sp = missingTr.gameObject.AddComponent<UIScrollPassthrough>();
                        }
                        sp.target = scrollRect;
                    }
                    catch { }
                    // Make clickable to filter by missing dependencies using EventTrigger (non-invasive)
                    EventTrigger et = b != null ? b.EnsureMissingEventTrigger() : null;
                    if (et == null)
                    {
                        et = missingTr.GetComponent<EventTrigger>();
                        if (et == null) et = missingTr.gameObject.AddComponent<EventTrigger>();
                    }
                    var pointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                    pointerClickEntry.callback.AddListener((data) => {
                        try
                        {
                            int missingCount = GallerySortManager.GetMissingDepsCount(file);
                            if (missingCount > 0)
                                ApplyMissingDependenciesFilter(file);
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError($"[VPB] Missing click handler error: {ex}");
                        }
                    });
                    et.triggers.Clear();
                    et.triggers.Add(pointerClickEntry);
                    // Add tooltip
                    try { AddTooltip(missingTr.gameObject, "gallery.tooltip.missing_dependencies", "Missing Dependencies"); } catch { }
                }

                Transform catTr = b != null ? b.categoryTr : btnGO.transform.Find("ListRow/Details/Category");
                if (catTr != null)
                {
                    Text t = b != null && b.categoryText != null ? b.categoryText : catTr.GetComponent<Text>();
                    if (t != null)
                    {
                        string catLabel = "";
                        bool isMissing = file is VirtualFileEntry || file is MissingPackageListEntry;

                        if (isMissing)
                        {
                            catLabel = "Missing";
                            t.text = "Missing";
                            // Color missing label red
                            try { t.color = new Color(0.8f, 0.2f, 0.2f, 1f); } catch { }
                        }
                        else
                        {
                            try
                            {
                                if (file is CleanupFileEntry cfe && cfe.Candidate != null)
                                {
                                    catLabel = cfe.Candidate.GetFlagsLabel();
                                }
                                else
                                {
                                if (IsFilterActive)
                                {
                                    if (file is PackageListEntry ple && ple.Package != null)
                                        catLabel = GetBestCategoryLabelForPackage(ple.Package);
                                    else if (file is VarFileEntry vfe3 && vfe3.Package != null)
                                        catLabel = GetBestCategoryLabelForPackage(vfe3.Package);
                                }
                                }
                            }
                            catch { catLabel = ""; }

                            // Display just the category value (no "Cat:" prefix).
                            t.text = string.IsNullOrEmpty(catLabel) ? "" : catLabel;
                            // Color category label based on type.
                            try { t.color = GetCategoryTintColor(catLabel); } catch { try { t.color = Color.white; } catch { } }
                        }
                    }
                }

                Transform dependentsTr = b != null ? b.dependentsTr : btnGO.transform.Find("ListRow/Details/Dependents");
                if (dependentsTr != null)
                {
                    Text t = b != null && b.dependentsText != null ? b.dependentsText : dependentsTr.GetComponent<Text>();
                    if (t != null)
                    {
                        int dependents = GallerySortManager.GetDependentsCount(file);
                        string v = dependents.ToString().PadLeft(3);
                        t.text = "Dn: " + v;
                        t.raycastTarget = true;

                        // Hover-highlight only the value; Set() resets color on recycle.
                        try
                        {
                            UIRichValueHover hv = b != null ? b.EnsureDependentsHover() : null;
                            if (hv == null)
                            {
                                hv = dependentsTr.GetComponent<UIRichValueHover>();
                                if (hv == null) hv = dependentsTr.gameObject.AddComponent<UIRichValueHover>();
                            }
                            hv.target = t;
                            hv.Set("Dn: ", v, "");
                        }
                        catch { }
                    }
                    // Keep ScrollRect scrolling even when hovering over clickable text.
                    try
                    {
                        UIScrollPassthrough sp = b != null ? b.EnsureDependentsScrollPassthrough() : null;
                        if (sp == null)
                        {
                            sp = dependentsTr.GetComponent<UIScrollPassthrough>();
                            if (sp == null) sp = dependentsTr.gameObject.AddComponent<UIScrollPassthrough>();
                        }
                        sp.target = scrollRect;
                    }
                    catch { }
                    // Make clickable to filter by dependents using EventTrigger (non-invasive)
                    EventTrigger et = b != null ? b.EnsureDependentsEventTrigger() : null;
                    if (et == null)
                    {
                        et = dependentsTr.GetComponent<EventTrigger>();
                        if (et == null) et = dependentsTr.gameObject.AddComponent<EventTrigger>();
                    }
                    var pointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                    pointerClickEntry.callback.AddListener((data) => {
                        if (GallerySortManager.GetDependentsCount(file) > 0)
                            ApplyDependentsFilter(file);
                    });
                    et.triggers.Clear();
                    et.triggers.Add(pointerClickEntry);
                    // Add tooltip
                    try { AddTooltip(dependentsTr.gameObject, "gallery.tooltip.dependents", "Dependents"); } catch { }
                }

                Transform sizeTr = b != null ? b.sizeTr : btnGO.transform.Find("ListRow/Details/Size");
                if (sizeTr != null)
                {
                    Text t = b != null && b.sizeText != null ? b.sizeText : sizeTr.GetComponent<Text>();
                    if (t != null) t.text = FormatBytesForList(file.Size);
                }

                Transform dateTr = b != null ? b.dateTr : btnGO.transform.Find("ListRow/Details/Date");
                if (dateTr != null)
                {
                    Text t = b != null && b.dateText != null ? b.dateText : dateTr.GetComponent<Text>();
                    if (t != null)
                    {
                        // Prefer when we first indexed this uid (= when user actually got it / got the update)
                        // over file mtime which is often the creator's original build date carried by the .var.
                        try
                        {
                            DateTime dt = GallerySortManager.ResolveDisplayDateForRow(file);
                            if (dt.Year < 1980) t.text = "Unknown";
                            else t.text = dt.ToString("yy-MM-dd");
                        }
                        catch { t.text = ""; }
                    }
                }

            }

            // Init RatingHandler in both list and grid mode
            {
                Text starText = b != null ? b.ratingStarText : null;
                if (starText == null)
                {
                    Transform starBtnTr = b != null ? b.ratingStarTr : btnGO.transform.Find("Rating/Star");
                    if (starBtnTr != null) starText = starBtnTr.GetComponentInChildren<Text>();

                    if (starText == null)
                    {
                        Transform oldStar = btnGO.transform.Find("ListRow/Details/Rating/Star");
                        if (oldStar != null) starText = oldStar.GetComponentInChildren<Text>();
                    }
                    if (b != null && starText != null) b.ratingStarText = starText;
                }

                Transform selector2Tr = b != null ? b.ratingSelectorTr : btnGO.transform.Find("RatingSelector");
                RatingHandler rh = b != null ? b.ratingHandler : btnGO.GetComponent<RatingHandler>();
                if (rh != null && selector2Tr != null && starText != null)
                {
                    rh.Init(file, starText, selector2Tr.gameObject);
                    // Colored digit always (list + grid) — never color-only ★.
                    rh.SetShowDigitMode(true);
                }
            }

            // Draggable
            UIDraggableItem draggable = b != null ? b.draggable : btnGO.GetComponent<UIDraggableItem>();
            if (draggable != null) draggable.FileEntry = file;
        }

        private string GetBestCategoryLabelForPackage(VarPackage pkg)
        {
            if (pkg == null) return "";
            try
            {
                if (packageCategoryLabelCache != null && packageCategoryLabelCache.TryGetValue(pkg.Uid, out string cached))
                    return cached ?? "";
            }
            catch { }

            string result = "Unknown";
            try
            {
                if (categories == null || categories.Count == 0) return result;

                List<string> names; List<long> ticks; List<long> sizes;
                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) return result;

                int best = 0;
                int bestCount = 0;
                int ties = 0;

                for (int ci = 0; ci < categories.Count; ci++)
                {
                    var cat = categories[ci];
                    if (string.IsNullOrEmpty(cat.name) || string.IsNullOrEmpty(cat.extension)) continue;

                    string[] exts = cat.extension.Split('|');
                    if (exts == null || exts.Length == 0) continue;

                    int hits = 0;
                    for (int i = 0; i < names.Count; i++)
                    {
                        string ip = names[i];
                        if (string.IsNullOrEmpty(ip)) continue;

                        // ext match
                        string entryExt = System.IO.Path.GetExtension(ip);
                        if (string.IsNullOrEmpty(entryExt) || entryExt.Length < 2) continue;
                        string ext = entryExt.Substring(1);
                        bool extMatch = false;
                        for (int e = 0; e < exts.Length; e++)
                        {
                            var ce = exts[e];
                            if (!string.IsNullOrEmpty(ce) && string.Equals(ext, ce.Trim(), StringComparison.OrdinalIgnoreCase))
                            { extMatch = true; break; }
                        }
                        if (!extMatch) continue;

                        // path match
                        bool pathOk = false;
                        if (cat.paths != null && cat.paths.Count > 0)
                        {
                            for (int p = 0; p < cat.paths.Count; p++)
                            {
                                var pref = cat.paths[p];
                                if (!string.IsNullOrEmpty(pref) && GalleryInternalPathStartsWithPrefix(ip, pref))
                                { pathOk = true; break; }
                            }
                        }
                        else if (!string.IsNullOrEmpty(cat.path))
                        {
                            if (GalleryInternalPathStartsWithPrefix(ip, cat.path)) pathOk = true;
                        }
                        else
                        {
                            pathOk = true;
                        }
                        if (!pathOk) continue;

                        hits++;
                        if (hits >= 8) break; // cap work per category
                    }

                    if (hits > bestCount)
                    {
                        bestCount = hits;
                        best = ci;
                        ties = 0;
                    }
                    else if (hits > 0 && hits == bestCount)
                    {
                        ties++;
                    }
                }

                if (bestCount > 0)
                {
                    if (ties > 0) result = "Mixed";
                    else result = categories[best].name;
                }
            }
            catch { }

            try
            {
                if (packageCategoryLabelCache != null)
                {
                    if (packageCategoryLabelCache.Count > 8000) packageCategoryLabelCache.Clear();
                    packageCategoryLabelCache[pkg.Uid] = result;
                }
            }
            catch { }

            return result;
        }

        private static Color GetCategoryTintColor(string categoryLabel)
        {
            if (string.IsNullOrEmpty(categoryLabel)) return Color.white;

            string s = categoryLabel.Trim();
            if (s.Length == 0) return Color.white;
            string sl = s.ToLowerInvariant();

            // Special / meta
            if (sl == "unknown") return new Color(0.65f, 0.65f, 0.65f, 1f);
            if (sl == "mixed") return new Color(0.85f, 0.65f, 0.15f, 1f);

            // Cleanup types
            if (sl.Contains("stale cache")) return new Color(0.62f, 0.40f, 0.20f, 1f); // brown (matches tab)
            if (sl.Contains("duplicate")) return new Color(0.80f, 0.35f, 0.15f, 1f);   // reddish-orange
            if (sl.Contains("damaged")) return new Color(0.85f, 0.20f, 0.20f, 1f);     // red
            if (sl.Contains("old version")) return new Color(0.55f, 0.55f, 0.55f, 1f); // gray
            if (sl.Contains("excluded")) return new Color(0.40f, 0.40f, 0.40f, 1f);    // dark gray

            // Common VPB/VaM gallery types (heuristic)
            if (sl.Contains("scene")) return new Color(0.95f, 0.55f, 0.10f, 1f);     // orange
            if (sl.Contains("subscene")) return new Color(0.95f, 0.55f, 0.10f, 1f);  // orange
            if (sl.Contains("hair")) return new Color(0.85f, 0.35f, 0.85f, 1f);      // purple
            if (sl.Contains("clothing")) return new Color(0.35f, 0.70f, 0.95f, 1f);  // blue
            if (sl.Contains("skin")) return new Color(0.90f, 0.75f, 0.55f, 1f);      // tan
            if (sl.Contains("morph")) return new Color(0.40f, 0.85f, 0.65f, 1f);     // green-teal
            if (sl.Contains("appearance")) return new Color(0.55f, 0.80f, 0.40f, 1f);// green
            if (sl.Contains("pose")) return new Color(0.95f, 0.85f, 0.30f, 1f);      // yellow
            if (sl.Contains("asset") || sl.Contains("cua")) return new Color(0.55f, 0.85f, 0.95f, 1f); // cyan
            if (sl.Contains("plugin") || sl.Contains("script")) return new Color(0.70f, 0.70f, 0.95f, 1f); // lavender

            return Color.white;
        }

        /// <summary>Update filter indicator UI when filter state changes.</summary>
        public void UpdateFilterIndicator()
        {
            // Top filter label removed; keep filter exit control in the footer only.
            HideFilterIndicator();
        }

        private GameObject GetOrCreateFilterIndicator()
        {
            if (scrollRect == null) return null;

            Transform parent = scrollRect.transform.parent;
            if (parent == null) return null;

            Transform existingIndicator = parent.Find("FilterIndicator");
            if (existingIndicator != null) return existingIndicator.gameObject;

            // Create new filter indicator
            GameObject indicatorGO = new GameObject("FilterIndicator");
            indicatorGO.transform.SetParent(parent, false);

            RectTransform rt = indicatorGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(0, 28);
            rt.anchoredPosition = new Vector2(0, -2);

            Image bgImg = UI.AddImage(indicatorGO, new Color(0.2f, 0.35f, 0.2f, 0.9f));

            HorizontalLayoutGroup hgroup = indicatorGO.AddComponent<HorizontalLayoutGroup>();
            hgroup.padding = new RectOffset(8, 8, 4, 4);
            hgroup.spacing = 8f;
            hgroup.childForceExpandWidth = false;
            hgroup.childForceExpandHeight = false;

            // Description text
            Text descText = UI.CreateLabel(indicatorGO, "Filtered", GalleryUiDesignTokens.FontBodyRef, Color.white, raycastTarget: false, name: "Description");
            GameObject descGO = descText.gameObject;
            UI.AddLE(descGO, preferredWidth: 200);

            // Clear button
            GameObject clearBtnGO = new GameObject("ClearButton");
            clearBtnGO.transform.SetParent(indicatorGO.transform, false);
            Image clearBtnImg = UI.AddGalleryElementRoundedBg(clearBtnGO, new Color(0.8f, 0.2f, 0.2f, 0.8f));
            Button clearBtn = clearBtnGO.AddComponent<Button>();
            clearBtn.targetGraphic = clearBtnImg;

            Text clearBtnText = new GameObject("Text").AddComponent<Text>();
            clearBtnText.transform.SetParent(clearBtnGO.transform, false);
            clearBtnText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            clearBtnText.fontSize = GalleryUiDesignTokens.FontBodyRef;
            clearBtnText.fontStyle = FontStyle.Normal;
            clearBtnText.color = Color.white;
            clearBtnText.text = "Clear Filter";
            clearBtnText.alignment = TextAnchor.MiddleCenter;
            clearBtnText.raycastTarget = false;

            RectTransform clearBtnRT = clearBtnGO.GetComponent<RectTransform>();
            clearBtnRT.sizeDelta = new Vector2(90, 20);

            UI.AddLE(clearBtnGO, preferredWidth: 90);

            return indicatorGO;
        }

        private void HideFilterIndicator()
        {
            if (scrollRect == null) return;
            Transform parent = scrollRect.transform.parent;
            if (parent == null) return;

            Transform indicator = parent.Find("FilterIndicator");
            if (indicator != null) indicator.gameObject.SetActive(false);
        }

        private static bool ShouldSkipFileEntryPointerForwarder(Transform t, Transform rowRoot)
        {
            if (t == null || rowRoot == null) return true;
            Transform p = t;
            while (p != null && p != rowRoot)
            {
                string n = p.name ?? "";
                if (string.Equals(n, "RatingSelector", StringComparison.Ordinal)
                    || string.Equals(n, "Rating", StringComparison.Ordinal)
                    || string.Equals(n, "Star", StringComparison.Ordinal))
                    return true;
                p = p.parent;
            }
            return false;
        }

        /// <summary>
        /// Child graphics (thumbnail, list detail columns) steal raycasts — forward alt-clicks to row root handler.
        /// </summary>
        private void EnsureFileEntryPointerForwarding(GameObject btnGO, UIFileEntryLeftReleaseSelect handler)
        {
            if (btnGO == null || handler == null) return;
            Transform rowRoot = btnGO.transform;
            Graphic[] graphics = btnGO.GetComponentsInChildren<Graphic>(true);
            if (graphics == null) return;

            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic g = graphics[i];
                if (g == null || !g.raycastTarget) continue;
                Transform gt = g.transform;
                if (gt == rowRoot) continue;
                if (ShouldSkipFileEntryPointerForwarder(gt, rowRoot)) continue;

                UIFileEntryPointerForwarder fwd = gt.GetComponent<UIFileEntryPointerForwarder>();
                if (fwd == null) fwd = gt.gameObject.AddComponent<UIFileEntryPointerForwarder>();
                fwd.Target = handler;
                fwd.ForwardLeftPointerUp = string.Equals(gt.name, "Thumbnail", StringComparison.Ordinal);
            }
        }

    }
}
