using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const string LookFacetSideRailBtnNameLeft = "VPB_SideRail_LookFacet_L";
        private const string LookFacetSideRailBtnNameRight = "VPB_SideRail_LookFacet_R";
        private const string LookFacetVirtHolderName = "_VPB_LookFacet_Virt";

        private static bool LookFacetPackEnabled()
        {
            try
            {
                var cfg = VPBConfig.Instance;
                return cfg != null && (cfg.DataPackLookapediaEnabled || cfg.DataPackHubTagsEnabled);
            }
            catch { return false; }
        }

        private void CreateLeftLookFacetSideRailButton()
        {
            CreateLookFacetSideRailButton(true);
        }

        private void CreateRightLookFacetSideRailButton()
        {
            CreateLookFacetSideRailButton(false);
        }

        private void CreateLookFacetSideRailButton(bool isLeft)
        {
            // Pack facets live in Tags Available. Do not add a dedicated rail chip.
            if (isLeft)
            {
                if (leftLookFacetSideBtnGO != null) leftLookFacetSideBtnGO.SetActive(false);
            }
            else if (rightLookFacetSideBtnGO != null)
                rightLookFacetSideBtnGO.SetActive(false);
        }

        private void InsertLookFacetSideButtonIntoList(List<RectTransform> list, RectTransform lookRt, bool isLeft)
        {
            if (list == null || lookRt == null) return;
            if (list.Contains(lookRt)) return;

            GameObject afterGo = isLeft ? leftCreatorSideBtnGO : rightCreatorSideBtnGO;
            if (afterGo == null) afterGo = isLeft ? leftUserTagsSideBtn : rightUserTagsSideBtn;
            Text pathTxt = isLeft ? leftPathBtnText : rightPathBtnText;

            int insertAt = -1;
            if (afterGo != null)
            {
                int afterIdx = list.FindIndex(rt => rt != null && rt.gameObject == afterGo);
                if (afterIdx >= 0) insertAt = afterIdx + 1;
            }
            if (insertAt < 0 && pathTxt != null)
            {
                int pathIdx = list.FindIndex(rt => rt != null && rt.GetComponentInChildren<Text>(true) == pathTxt);
                if (pathIdx >= 0) insertAt = pathIdx;
            }
            if (insertAt >= 0 && insertAt <= list.Count) list.Insert(insertAt, lookRt);
            else list.Add(lookRt);
        }

        private static bool IsLookFacetSideRailButtonGO(GameObject go)
        {
            if (go == null) return false;
            string n = go.name;
            return n == LookFacetSideRailBtnNameLeft || n == LookFacetSideRailBtnNameRight;
        }

        private void SyncLookFacetRailVisible()
        {
            RetireLookFacetSideRail(remapOpenPane: true);
        }

        private void ShowLookFacetRailButtonsIfPackOn()
        {
            RetireLookFacetSideRail(remapOpenPane: true);
        }

        /// <summary>Pack facets moved into Tags. Hide leftover rail chips; remap an open Lookapedia pane to Tags.</summary>
        private void RetireLookFacetSideRail(bool remapOpenPane)
        {
            if (leftLookFacetSideBtnGO != null && leftLookFacetSideBtnGO.activeSelf)
                leftLookFacetSideBtnGO.SetActive(false);
            if (rightLookFacetSideBtnGO != null && rightLookFacetSideBtnGO.activeSelf)
                rightLookFacetSideBtnGO.SetActive(false);

            bool leftWas = remapOpenPane && leftActiveContent == ContentType.Lookapedia;
            bool rightWas = remapOpenPane && rightActiveContent == ContentType.Lookapedia;
            if (leftWas) leftActiveContent = ContentType.UserTags;
            if (rightWas) rightActiveContent = ContentType.UserTags;
            if (leftWas || rightWas)
            {
                try { SyncActiveContentTypeFromSidePanels(); } catch { }
                try { UpdateTabs(); } catch { }
                try { UpdateLayout(); } catch { }
            }
            try { UpdateSideButtonPositions(); } catch { }
        }

        /// <summary>Command palette / expert path: open Tags and expand the relevant pack bucket.</summary>
        private void OpenUserTagsForPackFacet()
        {
            if (LookFacetSubjectModeAvailable())
                _userTagShowLooksBucket = true;
            else if (LookFacetHubModeAvailable())
                _userTagShowHubBucket = true;
            if (LookFacetHubModeAvailable())
                _userTagShowHubCatBucket = true;
            _userTagVirtViewSig = null;
            ToggleSideFromRailButton(ContentType.UserTags, true, false);
        }

        private static float LookFacetModeRowHeightPx(float s)
        {
            if (s <= 0f) s = 1f;
            return GalleryUiDesignTokens.SideTabRowHeightRef * s;
        }

        private bool LookFacetModeChromeVisible(bool isLeft)
        {
            ContentType? ct = isLeft ? leftActiveContent : rightActiveContent;
            return ct.HasValue && ct.Value == ContentType.Lookapedia;
        }

        private static bool LookFacetSubjectModeAvailable()
        {
            try
            {
                var cfg = VPBConfig.Instance;
                if (cfg == null || !cfg.DataPackLookapediaEnabled) return false;
                string[] ids = VpbLocalDatabase.DataPackSubjectPackIds();
                return ids == null || ids.Length > 0;
            }
            catch { return true; }
        }

        private static bool LookFacetHubModeAvailable()
        {
            try
            {
                var cfg = VPBConfig.Instance;
                return cfg != null && cfg.DataPackHubTagsEnabled;
            }
            catch { return true; }
        }

        /// <summary>Never leave the list parked on a mode that has no pack behind it.</summary>
        private void EnsureLookFacetModeValid()
        {
            bool want = _lookFacetHubMode;
            if (!want && !LookFacetSubjectModeAvailable() && LookFacetHubModeAvailable()) want = true;
            else if (want && !LookFacetHubModeAvailable() && LookFacetSubjectModeAvailable()) want = false;
            if (want == _lookFacetHubMode) return;
            _lookFacetHubMode = want;
            _lookFacetVirtViewSig = null;
            try { SyncLookFacetModeChrome(ChromeScale); } catch { }
        }

        private string LookFacetEmptyText()
        {
            return _lookFacetHubMode
                ? VPBTranslation.T("gallery.lookfacet.empty_hub", "No Hub tags matched this library.")
                : VPBTranslation.T("gallery.lookfacet.empty", "No Look-A-Pedia matches in this library.");
        }

        private void SetLookFacetHubMode(bool hub)
        {
            if (_lookFacetHubMode == hub) return;
            _lookFacetHubMode = hub;
            ClearLookapediaListSearch();
            _lookFacetVirtViewSig = null;
            try { SyncLookFacetModeChrome(ChromeScale); } catch { }
            try { UpdateTabs(); } catch { }
        }

        private void ClearLookapediaListSearch()
        {
            lookapediaFilter = "";
            if (leftActiveContent == ContentType.Lookapedia)
                SetSideSearchInputTextWithoutNotify(leftSearchInput, "", _leftMainSideSearchOnValueChanged);
            if (rightActiveContent == ContentType.Lookapedia)
                SetSideSearchInputTextWithoutNotify(rightSearchInput, "", _rightMainSideSearchOnValueChanged);
        }

        private void RefreshLookFacetListsIfOpen()
        {
            if (_lookFacetVirtBuilding) return;
            _lookFacetVirtBuilding = true;
            try
            {
                _lookFacetVirtViewSig = null;
                if (leftActiveContent == ContentType.Lookapedia && leftTabContainerGO != null)
                    TryUpdateLookFacetMainPane(leftTabContainerGO, leftActiveTabButtons, true);
                if (rightActiveContent == ContentType.Lookapedia && rightTabContainerGO != null)
                    TryUpdateLookFacetMainPane(rightTabContainerGO, rightActiveTabButtons, false);
            }
            catch { }
            finally { _lookFacetVirtBuilding = false; }
        }

        /// <summary>Chip on/off: keep the virt list, only refresh selected rims.</summary>
        private void RebindLookFacetVirtHighlightsIfOpen()
        {
            if (leftActiveContent == ContentType.Lookapedia)
                try { UpdateLookFacetVirtualVisible(true); } catch { }
            if (rightActiveContent == ContentType.Lookapedia)
                try { UpdateLookFacetVirtualVisible(false); } catch { }
        }

        private void ApplyLookFacetSideFilterIfOpen()
        {
            List<CreatorCacheEntry> src = _lookFacetHubMode ? _lookFacetHubRows : _lookFacetSubjectRows;
            if (src == null || src.Count == 0)
            {
                RefreshLookFacetListsIfOpen();
                return;
            }
            FillLookFacetVirtViewFromRows(src);
            SortLookFacetVirtView();
            _lookFacetVirtViewSig = LookFacetViewSigToCache(_lookFacetHubMode);
            if (leftActiveContent == ContentType.Lookapedia)
            {
                _leftLookFacetVirtLastFirstIdx = -1;
                try { UpdateLookFacetVirtualVisible(true); } catch { }
            }
            if (rightActiveContent == ContentType.Lookapedia)
            {
                _rightLookFacetVirtLastFirstIdx = -1;
                try { UpdateLookFacetVirtualVisible(false); } catch { }
            }
        }

        private void FillLookFacetVirtViewFromRows(List<CreatorCacheEntry> src)
        {
            _lookFacetVirtView.Clear();
            if (src == null) return;
            string filterNow = lookapediaFilter ?? "";
            for (int i = 0; i < src.Count; i++)
            {
                CreatorCacheEntry e = src[i];
                if (string.IsNullOrEmpty(e.Name)) continue;
                if (!string.IsNullOrEmpty(filterNow)
                    && e.Name.IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _lookFacetVirtView.Add(e);
            }
        }

        private SortState LookFacetSortState()
        {
            SortState st = GetSortState("Lookapedia");
            SortDirection dir = st != null ? st.Direction : SortDirection.Ascending;
            SortType type = st != null && st.Type == SortType.Count ? SortType.Count : SortType.Name;
            return new SortState(type, dir);
        }

        private void SortLookFacetVirtView()
        {
            try { GallerySortManager.Instance.SortCreators(_lookFacetVirtView, LookFacetSortState()); }
            catch { }
        }

        private string LookFacetViewSigToCache(bool hub)
        {
            List<CreatorCacheEntry> rows = LookFacetRowsForMode(hub);
            return (rows != null && rows.Count > 0) ? ComputeLookFacetVirtViewSignature() : null;
        }

        private List<CreatorCacheEntry> LookFacetRowsForMode(bool hub)
        {
            return hub ? _lookFacetHubRows : _lookFacetSubjectRows;
        }

        private bool EnsureLookFacetRowsForMode(bool hub)
        {
            List<CreatorCacheEntry> dest = LookFacetRowsForMode(hub);
            string collectSig = ComputeLookFacetCollectSignature(hub);
            string last = hub ? _lookFacetHubCollectSig : _lookFacetSubjectCollectSig;
            if (string.Equals(last, collectSig, StringComparison.Ordinal) && dest != null)
                return true;
            if (dest == null) return false;
            if (!VpbLocalDatabase.TryCollectLookFacetRows(
                    hub, currentExtension, currentPaths, currentPath, activeTags,
                    currentCategoryTitle, currentPackagePathFilter, activeUserTags, dest))
                return dest.Count > 0;
            if (hub) _lookFacetHubCollectSig = collectSig;
            else _lookFacetSubjectCollectSig = collectSig;
            return true;
        }

        private void SyncLookFacetModeChrome(float paneScale)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            SyncLookFacetModeChromeSide(true, s);
            SyncLookFacetModeChromeSide(false, s);
        }

        private void SyncLookFacetModeChromeSide(bool isLeft, float s)
        {
            EnsureLookFacetModeChrome(isLeft);
            GameObject row = isLeft ? _leftLookFacetModeRowGO : _rightLookFacetModeRowGO;
            GameObject looksBtn = isLeft ? _leftLookFacetModeLooksBtn : _rightLookFacetModeLooksBtn;
            GameObject hubBtn = isLeft ? _leftLookFacetModeHubBtn : _rightLookFacetModeHubBtn;
            bool show = LookFacetModeChromeVisible(isLeft);
            if (row != null) row.SetActive(show);
            if (!show || row == null) return;

            HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) hlg.spacing = GalleryUiDesignTokens.ControlGapRef * s;

            GameObject headerGo = isLeft ? _leftSidePanelHeaderGO : _rightSidePanelHeaderGO;
            if (headerGo != null)
            {
                int hi = headerGo.transform.GetSiblingIndex();
                row.transform.SetSiblingIndex(hi + 1);
            }

            float headerH = SidePanelHeaderHeightRef * s;
            float modeH = LookFacetModeRowHeightPx(s);
            float gap = SidePanelHeaderGapRef * s;
            float colW = SidePanelHeaderColumnWidthRef * s;
            RectTransform rt = row.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(colW, modeH);
                float sideInsetX = isLeft ? SideTabColumnLeftInsetX(s) : SideTabColumnRightInsetX(s);
                rt.anchoredPosition = new Vector2(sideInsetX, SidePanelHeaderAnchorY(s) - headerH - gap);
            }

            ApplyLookFacetModeSegment(looksBtn, !_lookFacetHubMode, s,
                VPBTranslation.T("gallery.side.looks_like", "Looks like"));
            ApplyLookFacetModeSegment(hubBtn, _lookFacetHubMode, s,
                VPBTranslation.T("gallery.side.hub_tags", "Hub tags"));
        }

        private void EnsureLookFacetModeChrome(bool isLeft)
        {
            if (backgroundBoxGO == null) return;
            GameObject existing = isLeft ? _leftLookFacetModeRowGO : _rightLookFacetModeRowGO;
            if (existing != null) return;

            float s = ChromeScale;
            float modeH = LookFacetModeRowHeightPx(s);
            float colW = SidePanelHeaderColumnWidthRef * s;
            float sideInsetX = isLeft ? SideTabColumnLeftInsetX(s) : SideTabColumnRightInsetX(s);

            GameObject row = new GameObject(isLeft ? "VPB_LookFacetModeRow_L" : "VPB_LookFacetModeRow_R");
            row.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = isLeft ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            rowRt.pivot = isLeft ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            rowRt.sizeDelta = new Vector2(colW, modeH);
            rowRt.anchoredPosition = new Vector2(sideInsetX, 0f);
            UI.AddHLG(
                row,
                spacing: GalleryUiDesignTokens.ControlGapRef * s,
                padding: new RectOffset(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: true,
                childForceExpandHeight: true);

            GameObject looksBtn = CreateLookFacetModeSegmentButton(row, "Looks", false);
            GameObject hubBtn = CreateLookFacetModeSegmentButton(row, "Hub", true);
            row.SetActive(false);

            if (isLeft)
            {
                _leftLookFacetModeRowGO = row;
                _leftLookFacetModeLooksBtn = looksBtn;
                _leftLookFacetModeHubBtn = hubBtn;
            }
            else
            {
                _rightLookFacetModeRowGO = row;
                _rightLookFacetModeLooksBtn = looksBtn;
                _rightLookFacetModeHubBtn = hubBtn;
            }
        }

        private GameObject CreateLookFacetModeSegmentButton(GameObject parent, string name, bool hub)
        {
            float s = ChromeScale;
            int font = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            GameObject go = UI.CreateUIButton(
                parent,
                0f,
                0f,
                name,
                font,
                0f,
                0f,
                AnchorPresets.stretchAll,
                () => SetLookFacetHubMode(hub));
            go.name = hub ? "VPB_LookFacetModeHub" : "VPB_LookFacetModeLooks";
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 56f * s;
            le.preferredWidth = 0f;
            le.minHeight = LookFacetModeRowHeightPx(s);
            le.preferredHeight = LookFacetModeRowHeightPx(s);
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
            AddTooltip(go,
                hub ? "gallery.tooltip.hub_tags_mode" : "gallery.tooltip.looks_like_mode",
                hub
                    ? "Filter by Hub tags from Look-A-Pedia."
                    : "Filter by who this look resembles.");
            return go;
        }

        private void ApplyLookFacetModeSegment(GameObject btn, bool on, float s, string label)
        {
            if (btn == null) return;
            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = on ? ColorLooksLike : ColorInactiveRow;
            UI.SetControlSelectedRim(btn, on);
            Text txt = btn.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                txt.text = label ?? "";
                GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le != null)
            {
                float h = LookFacetModeRowHeightPx(s);
                le.minHeight = h;
                le.preferredHeight = h;
                le.minWidth = 56f * s;
            }
        }

        private bool TryUpdateLookFacetMainPane(GameObject tabContainer, List<GameObject> trackedButtons, bool isLeft)
        {
            if (tabContainer == null) return false;
            EnsureLookFacetHolder(tabContainer, isLeft);
            GameObject holder = isLeft ? leftLookFacetTabHolder : rightLookFacetTabHolder;
            if (holder == null) return false;
            holder.SetActive(true);
            UpdateTabs(ContentType.Lookapedia, holder, trackedButtons, isLeft);
            return true;
        }

        private void EnsureLookFacetHolder(GameObject tabContainer, bool isLeft)
        {
            GameObject existing = isLeft ? leftLookFacetTabHolder : rightLookFacetTabHolder;
            if (existing != null) return;

            List<GameObject> legacy = isLeft ? leftActiveTabButtons : rightActiveTabButtons;
            if (legacy != null)
            {
                foreach (var b in legacy) ReturnTabButton(b);
                legacy.Clear();
            }
            ClearTabContainerChildrenForDualBufferInit(tabContainer);

            GameObject holder = CreateCreatorVirtualHolder(LookFacetVirtHolderName, tabContainer.transform);
            if (isLeft) leftLookFacetTabHolder = holder;
            else rightLookFacetTabHolder = holder;
        }

        private void TeardownLookFacetPaneOneSide(bool isLeft)
        {
            GameObject holder = isLeft ? leftLookFacetTabHolder : rightLookFacetTabHolder;
            TeardownLookFacetVirt(isLeft);
            if (holder != null)
            {
                try { UnityEngine.Object.Destroy(holder); } catch { }
            }
            if (isLeft) leftLookFacetTabHolder = null;
            else rightLookFacetTabHolder = null;
        }

        private void BuildLookFacetTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;

            if (!LookFacetPackEnabled())
            {
                CreateTabButton(container.transform,
                    VPBTranslation.T("gallery.lookfacet.off", "Data packs are off (Settings)."),
                    ColorInactiveRow, false, () => { }, trackedButtons);
                HideLookFacetVirtButtons(isLeft);
                return;
            }

            if (!VpbLocalDatabase.DataPackIndexReady)
            {
                CreateTabButton(container.transform,
                    VPBTranslation.T("gallery.lookfacet.loading", "Data pack syncing…"),
                    ColorInactiveRow, false, () => { }, trackedButtons);
                HideLookFacetVirtButtons(isLeft);
                return;
            }

            EnsureLookFacetModeValid();
            bool hub = _lookFacetHubMode;
            if (!EnsureLookFacetRowsForMode(hub))
            {
                if (_lookFacetVirtView.Count == 0)
                {
                    CreateTabButton(container.transform,
                        LookFacetEmptyText(),
                        ColorInactiveRow, false, () => { }, trackedButtons);
                    HideLookFacetVirtButtons(isLeft);
                }
                else
                {
                    EnsureLookFacetVirtScrollHook(isLeft, container);
                    UpdateLookFacetVirtualVisible(isLeft);
                }
                return;
            }

            string sig = ComputeLookFacetVirtViewSignature();
            if (!string.Equals(_lookFacetVirtViewSig, sig, StringComparison.Ordinal))
            {
                FillLookFacetVirtViewFromRows(LookFacetRowsForMode(hub));
                SortLookFacetVirtView();
                _lookFacetVirtViewSig = LookFacetViewSigToCache(hub);
                ScrollRect sr = container.GetComponentInParent<ScrollRect>();
                if (sr != null) sr.verticalNormalizedPosition = 1f;
                if (isLeft) _leftLookFacetVirtLastFirstIdx = -1;
                else _rightLookFacetVirtLastFirstIdx = -1;
            }

            if (_lookFacetVirtView.Count == 0)
            {
                string empty = string.IsNullOrEmpty(lookapediaFilter)
                    ? LookFacetEmptyText()
                    : VPBTranslation.T("gallery.lookfacet.empty_filter", "No rows match this list search.");
                CreateTabButton(container.transform, empty, ColorInactiveRow, false, () => { }, trackedButtons);
                HideLookFacetVirtButtons(isLeft);
                return;
            }

            EnsureLookFacetVirtScrollHook(isLeft, container);
            UpdateLookFacetVirtualVisible(isLeft);
        }

        private string ComputeLookFacetCollectSignature(bool hub)
        {
            int rev = 0;
            try { rev = VpbDataPackService.StatusRevision; } catch { }
            var sb = new System.Text.StringBuilder(96);
            sb.Append("c2|").Append(rev).Append('|').Append(hub ? 'h' : 's');
            sb.Append('|').Append(currentCategoryTitle ?? "");
            sb.Append('|').Append(currentExtension ?? "");
            sb.Append('|').Append(currentPath ?? "");
            sb.Append('|').Append(currentPackagePathFilter ?? "");
            if (currentPaths != null)
            {
                for (int i = 0; i < currentPaths.Count; i++) sb.Append('/').Append(currentPaths[i] ?? "");
            }
            AppendLookFacetTagSig(sb, activeTags);
            AppendLookFacetTagSig(sb, activeUserTags);
            return sb.ToString();
        }

        private static void AppendLookFacetTagSig(System.Text.StringBuilder sb, HashSet<string> tags)
        {
            sb.Append('|');
            if (tags == null || tags.Count == 0) return;
            sb.Append(tags.Count).Append(':');
            foreach (string t in tags) sb.Append(t ?? "").Append(',');
        }

        private string ComputeLookFacetVirtViewSignature()
        {
            SortState st = GetSortState("Lookapedia");
            float scale = ChromeScale;
            int rev = 0;
            try { rev = VpbDataPackService.StatusRevision; } catch { }
            return "v4|" + rev
                + "|" + (_lookFacetHubMode ? "h" : "s")
                + "|" + (lookapediaFilter ?? "")
                + "|" + (int)(st != null ? st.Type : 0)
                + "|" + (int)(st != null ? st.Direction : 0)
                + "|" + scale.ToString("R");
        }

        private void HideLookFacetVirtButtons(bool isLeft)
        {
            List<GameObject> pool = isLeft ? _leftLookFacetVirtButtons : _rightLookFacetVirtButtons;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null) pool[i].SetActive(false);
            }
            GameObject holder = isLeft ? leftLookFacetTabHolder : rightLookFacetTabHolder;
            LayoutElement holderLe = holder != null ? holder.GetComponent<LayoutElement>() : null;
            if (holderLe != null) holderLe.preferredHeight = 0f;
        }

        private void EnsureLookFacetVirtScrollHook(bool isLeft, GameObject holder)
        {
            if (holder == null) return;
            ScrollRect sr = holder.GetComponentInParent<ScrollRect>();
            if (sr == null) return;

            if (isLeft)
            {
                _leftLookFacetVirtScroll = sr;
                if (_leftLookFacetVirtHooked) return;
                _leftLookFacetVirtHooked = true;
                sr.onValueChanged.AddListener(OnLookFacetVirtScrollLeft);
            }
            else
            {
                _rightLookFacetVirtScroll = sr;
                if (_rightLookFacetVirtHooked) return;
                _rightLookFacetVirtHooked = true;
                sr.onValueChanged.AddListener(OnLookFacetVirtScrollRight);
            }
        }

        private void OnLookFacetVirtScrollLeft(Vector2 _)
        {
            try { UpdateLookFacetVirtualVisible(true); } catch { }
        }

        private void OnLookFacetVirtScrollRight(Vector2 _)
        {
            try { UpdateLookFacetVirtualVisible(false); } catch { }
        }

        private void TeardownLookFacetVirt(bool isLeft)
        {
            if (isLeft)
            {
                if (_leftLookFacetVirtScroll != null && _leftLookFacetVirtHooked)
                {
                    try { _leftLookFacetVirtScroll.onValueChanged.RemoveListener(OnLookFacetVirtScrollLeft); } catch { }
                }
                _leftLookFacetVirtButtons.Clear();
                _leftLookFacetVirtScroll = null;
                _leftLookFacetVirtHooked = false;
                _leftLookFacetVirtLastFirstIdx = -1;
            }
            else
            {
                if (_rightLookFacetVirtScroll != null && _rightLookFacetVirtHooked)
                {
                    try { _rightLookFacetVirtScroll.onValueChanged.RemoveListener(OnLookFacetVirtScrollRight); } catch { }
                }
                _rightLookFacetVirtButtons.Clear();
                _rightLookFacetVirtScroll = null;
                _rightLookFacetVirtHooked = false;
                _rightLookFacetVirtLastFirstIdx = -1;
            }
        }

        private void UpdateLookFacetVirtualVisible(bool isLeft)
        {
            if (_lookFacetVirtView == null) return;
            GameObject holder = isLeft ? leftLookFacetTabHolder : rightLookFacetTabHolder;
            if (holder == null || !holder.activeInHierarchy) return;

            ScrollRect sr = isLeft ? _leftLookFacetVirtScroll : _rightLookFacetVirtScroll;
            if (sr == null) sr = holder.GetComponentInParent<ScrollRect>();
            if (sr == null) return;

            float rowH = CreatorVirtRowHeight();
            if (rowH <= 1f) rowH = 37f;

            RectTransform viewport = sr.viewport != null ? sr.viewport : (sr.transform as RectTransform);
            float viewportH = viewport != null ? viewport.rect.height : 600f;

            int total = _lookFacetVirtView.Count;
            LayoutElement holderLe = holder.GetComponent<LayoutElement>();
            if (total == 0)
            {
                HideLookFacetVirtButtons(isLeft);
                return;
            }
            float contentH = total * rowH;
            if (holderLe != null) holderLe.preferredHeight = contentH;

            float scrollRange = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - Mathf.Clamp01(sr.verticalNormalizedPosition)) * scrollRange;
            int firstIdx = (rowH > 0f) ? Mathf.FloorToInt(scrollY / rowH) : 0;
            if (firstIdx < 0) firstIdx = 0;
            if (firstIdx > total - 1) firstIdx = Mathf.Max(0, total - 1);

            int visible = Mathf.CeilToInt(viewportH / rowH) + 10;
            EnsureSideTabVirtPool(isLeft ? _leftLookFacetVirtButtons : _rightLookFacetVirtButtons, holder.transform, visible);

            List<GameObject> pool = isLeft ? _leftLookFacetVirtButtons : _rightLookFacetVirtButtons;
            if (isLeft) _leftLookFacetVirtLastFirstIdx = firstIdx;
            else _rightLookFacetVirtLastFirstIdx = firstIdx;

            for (int i = 0; i < pool.Count; i++)
            {
                int idx = firstIdx + i;
                GameObject btnGO = pool[i];
                if (btnGO == null) continue;

                if (idx >= 0 && idx < total)
                {
                    btnGO.SetActive(true);
                    BindLookFacetVirtButton(btnGO, _lookFacetVirtView[idx]);

                    RectTransform rt = btnGO.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        float s = ChromeScale;
                        ApplySideTabVirtRowHorizontalLayout(rt, s, SideTabRowHeightPx(s));
                        rt.anchoredPosition = new Vector2(0f, -idx * rowH);
                    }
                }
                else
                    btnGO.SetActive(false);
            }
        }

        private void BindLookFacetVirtButton(GameObject btnGO, CreatorCacheEntry row)
        {
            if (btnGO == null) return;
            string name = row.Name ?? "";
            bool hub = _lookFacetHubMode;
            string token = VpbLocalDatabase.DataPackFacetValueToken(name);
            bool isActive = !string.IsNullOrEmpty(token) && HasTitleSearchPackChip(
                hub ? TitleSearchChipKind.PackHubTag : TitleSearchChipKind.PackSubject, token);
            Color btnColor = isActive ? ColorLooksLike : ColorInactiveRow;
            string label = name + " (" + row.Count + ")";

            Button btnComp = btnGO.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                string capturedName = name;
                btnComp.onClick.AddListener(() =>
                {
                    OnLookFacetRowClick(capturedName, false);
                });
            }
            UIRightClickDelegate rightClickDelegate = btnGO.GetComponent<UIRightClickDelegate>();
            if (rightClickDelegate == null) rightClickDelegate = btnGO.AddComponent<UIRightClickDelegate>();
            rightClickDelegate.OnRightClick = () => OnLookFacetRowClick(name, true);

            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = btnColor;
            UI.SetControlSelectedRim(btnGO, isActive);

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
        }

        private void OnLookFacetRowClick(string rowName, bool rightClick)
        {
            if (rightClick)
            {
                ClearTitleSearchPackChips(_lookFacetHubMode ? TitleSearchChipKind.PackHubTag : TitleSearchChipKind.PackSubject);
                try { RebindLookFacetVirtHighlightsIfOpen(); } catch { }
                try { RefreshUserTagHubHighlightsIfOpen(); } catch { }
                return;
            }

            string token = VpbLocalDatabase.DataPackFacetValueToken(rowName);
            if (string.IsNullOrEmpty(token)) return;

            if (_lookFacetHubMode) ToggleTitleSearchPackHubTagChip(token, true);
            else ToggleTitleSearchPackSubjectChip(token, true);
            try { RebindLookFacetVirtHighlightsIfOpen(); } catch { }
            try { RefreshUserTagHubHighlightsIfOpen(); } catch { }
        }

        private void RefreshUserTagHubHighlightsIfOpen()
        {
            if (IsUserTagsSideTabOpen(true))
                try { RefreshUserTagsAvailPaneInPlace(true); } catch { }
            if (IsUserTagsSideTabOpen(false))
                try { RefreshUserTagsAvailPaneInPlace(false); } catch { }
        }
    }
}
