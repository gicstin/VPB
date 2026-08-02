using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Title-bar Filter button + popup. Owns source (All / Local / .var) plus browse toggles
        // that used to live in the sort menu (Hidden only, Always loaded, Hide old versions,
        // Show hidden items). Active chrome + chips; right-click clears this button's filters.

        private const int GlobalSourceFilterButtonWidth = 88;
        private const float GlobalSourceFilterButtonHeight = GalleryUiDesignTokens.TitleBarChipRef;
        private const float GlobalSourceFilterButtonCenterRelativeX = -398f;
        private const float BrowseFilterMenuPanelWidthRef = GalleryUiDesignTokens.FileSortMenuPanelWidthRef;

        public void SetupGlobalSourceFilterDropdown(GameObject titleBarGO, GameObject backgroundBoxGO)
        {
            if (titleBarGO == null || backgroundBoxGO == null) return;

            if (VPBConfig.Instance != null)
                currentGlobalSourceFilter = VPBConfig.Instance.GlobalSourceFilter;

            BuildGlobalSourceFilterButton(titleBarGO);
            BuildGlobalSourceFilterDropdown(backgroundBoxGO);
            UpdateGlobalSourceFilterButtonLabel();
        }

        private void BuildGlobalSourceFilterButton(GameObject titleBarGO)
        {
            globalSourceFilterBtn = UI.CreateUIButton(
                titleBarGO,
                GlobalSourceFilterButtonWidth,
                GlobalSourceFilterButtonHeight,
                VPBTranslation.T("gallery.filter.button", "Filter"),
                16, 0, 0,
                AnchorPresets.middleCenter,
                null);

            Image backdrop = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<Image>() : null;
            if (backdrop != null) backdrop.color = new Color(0f, 0f, 0f, 0.5f);

            globalSourceFilterBtnText = globalSourceFilterBtn != null
                ? globalSourceFilterBtn.GetComponentInChildren<Text>(true)
                : null;
            if (globalSourceFilterBtnText != null)
            {
                globalSourceFilterBtnText.horizontalOverflow = HorizontalWrapMode.Wrap;
                globalSourceFilterBtnText.verticalOverflow = VerticalWrapMode.Truncate;
                globalSourceFilterBtnText.alignment = TextAnchor.MiddleCenter;
                RectTransform textRT = globalSourceFilterBtnText.rectTransform;
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(6f, 2f);
                textRT.offsetMax = new Vector2(-6f, -2f);
            }

            RectTransform btnRT = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<RectTransform>() : null;
            if (btnRT != null)
            {
                btnRT.anchorMin = new Vector2(0.5f, 0.5f);
                btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot = new Vector2(0.5f, 0.5f);
                btnRT.anchoredPosition = new Vector2(GlobalSourceFilterButtonCenterRelativeX, 0f);
            }
            if (globalSourceFilterBtn != null)
            {
                RectMask2D staleMask = globalSourceFilterBtn.GetComponent<RectMask2D>();
                if (staleMask != null) UnityEngine.Object.Destroy(staleMask);
            }

            Button btn = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<Button>() : null;
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => ToggleGlobalSourceFilterDropdown());
            }

            if (globalSourceFilterBtn != null)
            {
                var rc = globalSourceFilterBtn.AddComponent<UIRightClickDelegate>();
                rc.OnRightClick = () => ClearTitleBarBrowseFiltersFromButton();
            }

            AddTooltip(globalSourceFilterBtn, "gallery.tooltip.browse_filter",
                "Filter: source + visibility. Click row: on/off. Shift+click or right-click row: only-mode. Right-click this button clears.");

            // Compact: filter_off idle / filter_on when active (filter.png is Filter Presets).
            try
            {
                Sprite sp = UI.LoadIconSprite("vpb_icons/filter_off.png", UI.BarIconGlyphTint);
                if (sp != null && globalSourceFilterBtn != null)
                {
                    GameObject iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(globalSourceFilterBtn.transform, false);
                    globalSourceFilterBtnIcon = UI.AddImage(iconGO, Color.white, false);
                    globalSourceFilterBtnIcon.sprite = sp;
                    globalSourceFilterBtnIcon.preserveAspect = true;
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    float pad = GalleryUiDesignTokens.SearchIconButtonPadRef;
                    irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
                    irt.anchoredPosition = Vector2.zero;
                    iconGO.SetActive(false);
                }
            }
            catch { }

            {
                var rt = btnRT;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt == null) return;
                    float w = _globalSourceFilterCompact
                        ? GalleryUiDesignTokens.TitleBarChipRef * s
                        : GlobalSourceFilterButtonWidth * s;
                    rt.sizeDelta = new Vector2(w, GlobalSourceFilterButtonHeight * s);
                });
            }
        }

        private void SetGlobalSourceFilterCompactMode(bool compact, float paneScale)
        {
            if (paneScale <= 0f) paneScale = 1f;
            _globalSourceFilterCompact = compact;

            if (globalSourceFilterBtnText != null)
            {
                if (globalSourceFilterBtnText.gameObject.activeSelf == compact)
                    globalSourceFilterBtnText.gameObject.SetActive(!compact);
                globalSourceFilterBtnText.horizontalOverflow = HorizontalWrapMode.Wrap;
                globalSourceFilterBtnText.verticalOverflow = VerticalWrapMode.Truncate;
            }

            if (globalSourceFilterBtnIcon != null)
            {
                GameObject iconGO = globalSourceFilterBtnIcon.gameObject;
                if (iconGO.activeSelf != compact)
                    iconGO.SetActive(compact);
            }

            if (globalSourceFilterBtn == null) return;
            RectTransform rt = globalSourceFilterBtn.GetComponent<RectTransform>();
            if (rt == null) return;
            float w = compact
                ? GalleryUiDesignTokens.TitleBarChipRef * paneScale
                : GlobalSourceFilterButtonWidth * paneScale;
            float h = GlobalSourceFilterButtonHeight * paneScale;
            rt.sizeDelta = new Vector2(w, h);
            if (compact)
                ScaleButtonIconPadding(rt, paneScale);
        }

        private void BuildGlobalSourceFilterDropdown(GameObject backgroundBoxGO)
        {
            globalSourceFilterMenuRoot = UI.CreatePopupMenuRoot(backgroundBoxGO, "BrowseFilterMenu", HideGlobalSourceFilterDropdown);
            globalSourceFilterMenuRoot.SetActive(false);

            globalSourceFilterMenuPanelGO = UI.CreatePopupMenuPanel(
                globalSourceFilterMenuRoot,
                "BrowseFilterMenuPanel",
                AnchorPresets.topMiddle,
                new Vector2(BrowseFilterMenuPanelWidthRef, 50f),
                new Vector2(GlobalSourceFilterButtonCenterRelativeX, -72f));
        }

        private void ToggleGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterMenuRoot == null) return;
            if (globalSourceFilterMenuRoot.activeSelf) HideGlobalSourceFilterDropdown();
            else ShowGlobalSourceFilterDropdown();
        }

        private void ShowGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterMenuRoot == null) return;
            RebuildGlobalSourceFilterMenuOptions();
            try { RescaleGlobalSourceFilterMenuInternal(ChromeScale); } catch { }
            try { globalSourceFilterMenuRoot.transform.SetAsLastSibling(); } catch { }
            globalSourceFilterMenuRoot.SetActive(true);
        }

        private void HideGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterMenuRoot == null) return;
            globalSourceFilterMenuRoot.SetActive(false);
        }

        private void RebuildGlobalSourceFilterMenuOptions()
        {
            if (globalSourceFilterMenuPanelGO == null) return;

            UI.DestroyAllChildren(globalSourceFilterMenuPanelGO.transform);

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_source", "Source"));

            int allCount = 0, localCount = 0, varCount = 0;
            ComputeGlobalSourceFilterRowCounts(out allCount, out localCount, out varCount);

            AddGlobalSourceFilterMenuRow(
                VPBConfig.GlobalSourceFilterValue.All,
                VPBTranslation.T("gallery.filter.source_all", "All"),
                allCount,
                currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.All);
            AddGlobalSourceFilterMenuRow(
                VPBConfig.GlobalSourceFilterValue.Local,
                VPBTranslation.T("gallery.filter.source_local", "Local"),
                localCount,
                currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local);
            AddGlobalSourceFilterMenuRow(
                VPBConfig.GlobalSourceFilterValue.Var,
                VPBTranslation.T("gallery.filter.source_var", ".var"),
                varCount,
                currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var);

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_visibility", "Visibility"));

            // Daily: Hidden, Old versions, Not tagged.
            AddBrowseFilterCycleRow(
                ResolveBrowseHiddenCycleLabel(),
                _browseHiddenCycle,
                CycleBrowseHiddenFilter,
                CycleBrowseHiddenFilterOnly);

            AddBrowseFilterCycleRow(
                ResolveBrowseOldVersionsCycleLabel(),
                _browseOldVersionsCycle,
                CycleBrowseOldVersionsFilter,
                CycleBrowseOldVersionsFilterOnly);

            AddBrowseFilterToggleRow(
                VPBTranslation.T("gallery.filter.not_tagged", "Not tagged"),
                _userTagAvailMode == UserTagAvailMode.FilterUntagged,
                ToggleBrowseNotTaggedFilter);

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_more", "More"));

            // Less frequent: Always loaded, Loaded, Unused.
            AddBrowseFilterCycleRow(
                ResolveBrowseAlwaysLoadedCycleLabel(),
                _browseAlwaysLoadedCycle,
                CycleBrowseAlwaysLoadedFilter,
                CycleBrowseAlwaysLoadedFilterOnly);

            AddBrowseFilterCycleRow(
                ResolveBrowseLoadedModeLabel(),
                _browseLoadedMode == BrowseLoadedMode.Off
                    ? BrowseFilterCycle.Off
                    : (_browseLoadedMode == BrowseLoadedMode.LoadedOnly ? BrowseFilterCycle.Apply : BrowseFilterCycle.Only),
                CycleBrowseLoadedFilter,
                CycleBrowseLoadedFilterOnly);

            AddBrowseFilterCycleRow(
                ResolveBrowseUnusedCycleLabel(),
                _browseUnusedCycle,
                CycleBrowseUnusedFilter,
                CycleBrowseUnusedFilterOnly);

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_license", "License"));
            for (int i = 0; i < KnownPackageLicenseTypes.Length; i++)
            {
                string lic = KnownPackageLicenseTypes[i];
                bool isActive = HasLicenseFilter()
                    && string.Equals(currentLicenseFilter, lic, StringComparison.OrdinalIgnoreCase);
                AddBrowseFilterLicenseRow(lic, isActive);
            }

            try { RescaleGlobalSourceFilterMenuInternal(ChromeScale); } catch { }
            LayoutRebuilder.ForceRebuildLayoutImmediate(globalSourceFilterMenuPanelGO.GetComponent<RectTransform>());
        }

        private void AddBrowseFilterMenuHeader(string text)
        {
            if (globalSourceFilterMenuPanelGO == null || string.IsNullOrEmpty(text)) return;

            GameObject header = new GameObject("BrowseFilterHeader");
            header.transform.SetParent(globalSourceFilterMenuPanelGO.transform, false);

            Text label = UI.CreateLabel(
                header,
                text,
                GalleryUiDesignTokens.FontCaptionRef,
                new Color(0.72f, 0.76f, 0.84f, 1f),
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "Text");
            if (label != null)
            {
                label.fontStyle = FontStyle.Bold;
                RectTransform lrt = label.rectTransform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(10f, 0f);
                lrt.offsetMax = new Vector2(-6f, 0f);
            }

            Image bg = UI.AddImage(header, new Color(0f, 0f, 0f, 0f), false);
            if (bg != null) bg.raycastTarget = false;

            UI.AddLE(header, preferredHeight: 22f, flexibleWidth: 1f);
            RectTransform rt = header.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
            }
        }

        private void AddBrowseFilterCycleRow(string name, BrowseFilterCycle cycle, Action onPrimary, Action onOnly)
        {
            string mark = cycle == BrowseFilterCycle.Only ? "\u25cf  "
                : (cycle == BrowseFilterCycle.Apply ? "\u2713  " : "    ");
            bool active = cycle != BrowseFilterCycle.Off;
            GameObject row = UI.AddPopupMenuRow(
                globalSourceFilterMenuPanelGO,
                BrowseFilterMenuPanelWidthRef - 12f,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                mark + name,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                active,
                () =>
                {
                    try
                    {
                        if (IsBrowseFilterOnlyModifierHeld() && onOnly != null)
                            onOnly.Invoke();
                        else if (onPrimary != null)
                            onPrimary.Invoke();
                    }
                    catch { }
                },
                GalleryUiDesignTokens.PopupMenuRowHeightRef);
            if (row != null && onOnly != null)
            {
                UIRightClickDelegate rc = row.GetComponent<UIRightClickDelegate>();
                if (rc == null) rc = row.AddComponent<UIRightClickDelegate>();
                rc.OnRightClick = () =>
                {
                    try { onOnly.Invoke(); } catch { }
                };
            }
        }

        private static bool IsBrowseFilterOnlyModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private void AddBrowseFilterToggleRow(string name, bool isOn, Action onToggle)
        {
            string mark = isOn ? "\u2713  " : "    ";
            UI.AddPopupMenuRow(
                globalSourceFilterMenuPanelGO,
                BrowseFilterMenuPanelWidthRef - 12f,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                mark + name,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                isOn,
                () =>
                {
                    try { onToggle?.Invoke(); } catch { }
                },
                GalleryUiDesignTokens.PopupMenuRowHeightRef);
        }

        private void AddBrowseFilterLicenseRow(string licenseType, bool isActive)
        {
            if (string.IsNullOrEmpty(licenseType)) return;
            string mark = isActive ? "\u2713  " : "    ";
            string snap = licenseType;
            UI.AddPopupMenuRow(
                globalSourceFilterMenuPanelGO,
                BrowseFilterMenuPanelWidthRef - 12f,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                mark + licenseType,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                isActive,
                () =>
                {
                    try { SetLicenseFilter(snap, refresh: true); } catch { }
                },
                GalleryUiDesignTokens.PopupMenuRowHeightRef);
        }

        private void ToggleBrowseNotTaggedFilter()
        {
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
            {
                UserTagAvailMode restore = _userTagModeBeforeUntagged == UserTagAvailMode.Tag
                    ? UserTagAvailMode.Tag
                    : UserTagAvailMode.FilterByTags;
                SetUserTagAvailMode(restore);
            }
            else
            {
                SetUserTagAvailMode(UserTagAvailMode.FilterUntagged);
            }
        }

        /// <summary>Primary click: Off ↔ Apply. Only → Off.</summary>
        private static BrowseFilterCycle PrimaryToggleBrowseFilterCycle(BrowseFilterCycle cur)
        {
            if (cur == BrowseFilterCycle.Off) return BrowseFilterCycle.Apply;
            return BrowseFilterCycle.Off;
        }

        /// <summary>RMB / Shift+click: arm Only; if already Only → Apply.</summary>
        private static BrowseFilterCycle SecondaryToggleBrowseFilterOnly(BrowseFilterCycle cur)
        {
            if (cur == BrowseFilterCycle.Only) return BrowseFilterCycle.Apply;
            return BrowseFilterCycle.Only;
        }

        private string ResolveBrowseHiddenCycleLabel()
        {
            if (_browseHiddenCycle == BrowseFilterCycle.Only)
                return VPBTranslation.T("gallery.filter.hidden_only", "Hidden only");
            if (_browseHiddenCycle == BrowseFilterCycle.Apply)
                return VPBTranslation.T("gallery.filter.show_hidden_items", "Show hidden items");
            return VPBTranslation.T("gallery.filter.hidden", "Hidden");
        }

        private string ResolveBrowseAlwaysLoadedCycleLabel()
        {
            if (_browseAlwaysLoadedCycle == BrowseFilterCycle.Only)
                return VPBTranslation.T("gallery.filter.always_loaded_only", "Always loaded only");
            if (_browseAlwaysLoadedCycle == BrowseFilterCycle.Apply)
                return VPBTranslation.T("gallery.filter.always_loaded_first", "Always loaded first");
            return VPBTranslation.T("gallery.filter.always_loaded", "Always loaded");
        }

        private string ResolveBrowseOldVersionsCycleLabel()
        {
            if (_browseOldVersionsCycle == BrowseFilterCycle.Only)
                return VPBTranslation.T("gallery.filter.old_versions_only", "Old versions only");
            if (_browseOldVersionsCycle == BrowseFilterCycle.Apply)
                return VPBTranslation.T("gallery.filter.hide_old_versions", "Hide old versions");
            return VPBTranslation.T("gallery.filter.old_versions", "Old versions");
        }

        private string ResolveBrowseLoadedModeLabel()
        {
            if (_browseLoadedMode == BrowseLoadedMode.LoadedOnly)
                return VPBTranslation.T("gallery.filter.all_loaded", "All Loaded");
            if (_browseLoadedMode == BrowseLoadedMode.UnloadedOnly)
                return VPBTranslation.T("gallery.filter.all_unloaded", "All Unloaded");
            return VPBTranslation.T("gallery.filter.loaded", "Loaded");
        }

        private string ResolveBrowseUnusedCycleLabel()
        {
            if (_browseUnusedCycle == BrowseFilterCycle.Only)
                return VPBTranslation.T("gallery.filter.unused_only", "Unused only");
            if (_browseUnusedCycle == BrowseFilterCycle.Apply)
                return VPBTranslation.T("gallery.filter.unused_first", "Unused first");
            return VPBTranslation.T("gallery.filter.unused", "Unused");
        }

        private void AddGlobalSourceFilterMenuRow(
            VPBConfig.GlobalSourceFilterValue value,
            string name,
            int count,
            bool isActive)
        {
            string label = (isActive ? "\u2713  " : "    ") + name + " (" + count + ")";
            UI.AddPopupMenuRow(
                globalSourceFilterMenuPanelGO,
                BrowseFilterMenuPanelWidthRef - 12f,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                label,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                isActive,
                () => OnGlobalSourceFilterRowClicked(value),
                GalleryUiDesignTokens.PopupMenuRowHeightRef);
        }

        private void RescaleGlobalSourceFilterMenuInternal(float s)
        {
            if (globalSourceFilterMenuPanelGO == null) return;
            if (s <= 0f) s = 1f;

            ScaleVerticalPopupMenuRows(
                globalSourceFilterMenuPanelGO,
                s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                BrowseFilterMenuPanelWidthRef);

            RectTransform panelRT = globalSourceFilterMenuPanelGO.GetComponent<RectTransform>();
            if (panelRT == null) return;
            panelRT.anchorMin = new Vector2(0.5f, 1f);
            panelRT.anchorMax = new Vector2(0.5f, 1f);
            panelRT.pivot = new Vector2(0.5f, 1f);
            if (globalSourceFilterBtn == null) return;
            RectTransform btnRT = globalSourceFilterBtn.GetComponent<RectTransform>();
            if (btnRT == null) return;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            panelRT.anchoredPosition = new Vector2(
                btnRT.anchoredPosition.x,
                -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
        }

        private void OnGlobalSourceFilterRowClicked(VPBConfig.GlobalSourceFilterValue value)
        {
            // Re-click active Local/.var → All (toggle off). All stays selected.
            if (currentGlobalSourceFilter == value)
            {
                if (value != VPBConfig.GlobalSourceFilterValue.All)
                {
                    ApplyGlobalSourceFilterValue(VPBConfig.GlobalSourceFilterValue.All);
                    return;
                }
                if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
                    RebuildGlobalSourceFilterMenuOptions();
                return;
            }

            ApplyGlobalSourceFilterValue(value);
        }

        private void ApplyGlobalSourceFilterValue(VPBConfig.GlobalSourceFilterValue value)
        {
            currentGlobalSourceFilter = value;
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.GlobalSourceFilter = value;
                try { VPBConfig.Instance.Save(); } catch { }
            }

            if (value == VPBConfig.GlobalSourceFilterValue.Local && HasCreatorFilter())
            {
                ClearCreatorFilters();
                try { UpdateTitleCreatorButtonVisual(); } catch { }
                LogUtil.Log("[VPB] Global source filter set to Local; cleared creator filter.");
            }

            UpdateGlobalSourceFilterButtonLabel();
            RefreshFilesAndTabs();
            SyncBrowseFilterChipChrome();
            if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
                RebuildGlobalSourceFilterMenuOptions();
        }

        private void CycleBrowseHiddenFilter()
        {
            SetBrowseHiddenCycle(PrimaryToggleBrowseFilterCycle(_browseHiddenCycle), refresh: true);
        }

        private void CycleBrowseHiddenFilterOnly()
        {
            SetBrowseHiddenCycle(SecondaryToggleBrowseFilterOnly(_browseHiddenCycle), refresh: true);
        }

        private void CycleBrowseAlwaysLoadedFilter()
        {
            SetBrowseAlwaysLoadedCycle(PrimaryToggleBrowseFilterCycle(_browseAlwaysLoadedCycle), refresh: true);
        }

        private void CycleBrowseAlwaysLoadedFilterOnly()
        {
            SetBrowseAlwaysLoadedCycle(SecondaryToggleBrowseFilterOnly(_browseAlwaysLoadedCycle), refresh: true);
        }

        private void CycleBrowseOldVersionsFilter()
        {
            SetBrowseOldVersionsCycle(PrimaryToggleBrowseFilterCycle(_browseOldVersionsCycle), refresh: true);
        }

        private void CycleBrowseOldVersionsFilterOnly()
        {
            SetBrowseOldVersionsCycle(SecondaryToggleBrowseFilterOnly(_browseOldVersionsCycle), refresh: true);
        }

        private void CycleBrowseLoadedFilter()
        {
            // Off ↔ All Loaded. UnloadedOnly + click → Off.
            BrowseLoadedMode next = _browseLoadedMode == BrowseLoadedMode.Off
                ? BrowseLoadedMode.LoadedOnly
                : BrowseLoadedMode.Off;
            SetBrowseLoadedMode(next, refresh: true);
        }

        private void CycleBrowseLoadedFilterOnly()
        {
            // RMB/Shift: Off/Loaded → Unloaded; Unloaded → Loaded.
            BrowseLoadedMode next = _browseLoadedMode == BrowseLoadedMode.UnloadedOnly
                ? BrowseLoadedMode.LoadedOnly
                : BrowseLoadedMode.UnloadedOnly;
            SetBrowseLoadedMode(next, refresh: true);
        }

        private void CycleBrowseUnusedFilter()
        {
            SetBrowseUnusedCycle(PrimaryToggleBrowseFilterCycle(_browseUnusedCycle), refresh: true);
        }

        private void CycleBrowseUnusedFilterOnly()
        {
            SetBrowseUnusedCycle(SecondaryToggleBrowseFilterOnly(_browseUnusedCycle), refresh: true);
        }

        private void SetBrowseHiddenCycle(BrowseFilterCycle cycle, bool refresh)
        {
            if (_browseHiddenCycle == cycle)
            {
                if (refresh) AfterBrowseFilterCycleChanged();
                return;
            }
            _browseHiddenCycle = cycle;
            SyncShowHiddenPackagesFromCycle();
            if (refresh) AfterBrowseFilterCycleChanged(fullRefresh: true);
        }

        private void SetBrowseAlwaysLoadedCycle(BrowseFilterCycle cycle, bool refresh)
        {
            if (_browseAlwaysLoadedCycle == cycle)
            {
                if (refresh) AfterBrowseFilterCycleChanged();
                return;
            }

            BrowseFilterCycle prev = _browseAlwaysLoadedCycle;
            _browseAlwaysLoadedCycle = cycle;
            ApplyAlwaysLoadedCycleSortSideEffects(prev, cycle);
            if (refresh) AfterBrowseFilterCycleChanged(fullRefresh: true);
        }

        private void SetBrowseOldVersionsCycle(BrowseFilterCycle cycle, bool refresh)
        {
            if (_browseOldVersionsCycle == cycle)
            {
                if (refresh) AfterBrowseFilterCycleChanged();
                return;
            }
            _browseOldVersionsCycle = cycle;
            SyncHideOldVersionsFromCycle();
            if (refresh) AfterBrowseFilterCycleChanged(fullRefresh: true);
        }

        private void SetBrowseLoadedMode(BrowseLoadedMode mode, bool refresh)
        {
            if (_browseLoadedMode == mode)
            {
                if (refresh) AfterBrowseFilterCycleChanged();
                return;
            }
            _browseLoadedMode = mode;
            if (refresh) AfterBrowseFilterCycleChanged(fullRefresh: true);
        }

        private void SetBrowseUnusedCycle(BrowseFilterCycle cycle, bool refresh)
        {
            if (_browseUnusedCycle == cycle)
            {
                if (refresh) AfterBrowseFilterCycleChanged();
                return;
            }
            BrowseFilterCycle prev = _browseUnusedCycle;
            _browseUnusedCycle = cycle;
            ApplyUnusedCycleSortSideEffects(prev, cycle);
            if (refresh) AfterBrowseFilterCycleChanged(fullRefresh: true);
        }

        private void SyncShowHiddenPackagesFromCycle()
        {
            bool wantShow = _browseHiddenCycle != BrowseFilterCycle.Off;
            try
            {
                if (VPBConfig.Instance == null) return;
                if (VPBConfig.Instance.GalleryShowHiddenPackages == wantShow) return;
                VPBConfig.Instance.GalleryShowHiddenPackages = wantShow;
                VPBConfig.Instance.Save();
                GalleryFileListSnapshotCache.InvalidateAll();
            }
            catch { }
        }

        private void SyncHideOldVersionsFromCycle()
        {
            // Apply = hide old via VAM setting. Only = inverse filter (setting must be off).
            bool wantHide = _browseOldVersionsCycle == BrowseFilterCycle.Apply;
            try
            {
                if (Settings.Instance == null || Settings.Instance.HideOldVersions == null) return;
                if (Settings.Instance.HideOldVersions.Value == wantHide) return;
                Settings.Instance.HideOldVersions.Value = wantHide;
            }
            catch { }
        }

        private void ApplyAlwaysLoadedCycleSortSideEffects(BrowseFilterCycle prev, BrowseFilterCycle next)
        {
            SortState st = GetSortState("Files");
            if (st == null) return;

            if (prev == BrowseFilterCycle.Off && next != BrowseFilterCycle.Off)
            {
                _browseAlwaysLoadedSavedSort = st.Clone();
                st.Type = SortType.AutoInstall;
                st.Direction = SortDirection.Descending;
                SaveSortState("Files", st);
                try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, st); } catch { }
            }
            else if (prev != BrowseFilterCycle.Off && next == BrowseFilterCycle.Off)
            {
                if (_browseAlwaysLoadedSavedSort != null)
                {
                    st.Type = _browseAlwaysLoadedSavedSort.Type;
                    st.Direction = _browseAlwaysLoadedSavedSort.Direction;
                    SaveSortState("Files", st);
                    try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, st); } catch { }
                }
                _browseAlwaysLoadedSavedSort = null;
            }
            // Apply → Only: keep Prefer sort (AutoInstall).
        }

        private void ApplyUnusedCycleSortSideEffects(BrowseFilterCycle prev, BrowseFilterCycle next)
        {
            SortState st = GetSortState("Files");
            if (st == null) return;

            if (prev == BrowseFilterCycle.Off && next != BrowseFilterCycle.Off)
            {
                _browseUnusedSavedSort = st.Clone();
                st.Type = SortType.UsageCount;
                st.Direction = SortDirection.Ascending;
                SaveSortState("Files", st);
                try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, st); } catch { }
            }
            else if (prev != BrowseFilterCycle.Off && next == BrowseFilterCycle.Off)
            {
                if (_browseUnusedSavedSort != null)
                {
                    st.Type = _browseUnusedSavedSort.Type;
                    st.Direction = _browseUnusedSavedSort.Direction;
                    SaveSortState("Files", st);
                    try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, st); } catch { }
                }
                _browseUnusedSavedSort = null;
            }
        }

        private void AfterBrowseFilterCycleChanged(bool fullRefresh = false)
        {
            MigrateLegacyExclusiveFileSortIfNeeded();
            UpdateGlobalSourceFilterButtonLabel();
            if (fullRefresh)
            {
                try { RefreshFiles(true); } catch { }
            }
            SyncBrowseFilterChipChrome();
            if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
                RebuildGlobalSourceFilterMenuOptions();
        }

        /// <summary>Right-click Filter button: clear filters owned by this control.</summary>
        private void ClearTitleBarBrowseFiltersFromButton()
        {
            bool changed = ClearTitleBarBrowseFilters(refresh: true);
            if (!changed) return;
            HideGlobalSourceFilterDropdown();
        }

        /// <returns>True when any owned filter changed.</returns>
        private bool ClearTitleBarBrowseFilters(bool refresh)
        {
            bool changed = false;

            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
            {
                currentGlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.GlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                    try { VPBConfig.Instance.Save(); } catch { }
                }
                changed = true;
            }

            if (_browseHiddenCycle != BrowseFilterCycle.Off)
            {
                _browseHiddenCycle = BrowseFilterCycle.Off;
                SyncShowHiddenPackagesFromCycle();
                changed = true;
            }
            if (_browseAlwaysLoadedCycle != BrowseFilterCycle.Off)
            {
                BrowseFilterCycle prevAl = _browseAlwaysLoadedCycle;
                _browseAlwaysLoadedCycle = BrowseFilterCycle.Off;
                ApplyAlwaysLoadedCycleSortSideEffects(prevAl, BrowseFilterCycle.Off);
                changed = true;
            }
            if (_browseOldVersionsCycle != BrowseFilterCycle.Off)
            {
                _browseOldVersionsCycle = BrowseFilterCycle.Off;
                SyncHideOldVersionsFromCycle();
                changed = true;
            }
            if (_browseLoadedMode != BrowseLoadedMode.Off)
            {
                _browseLoadedMode = BrowseLoadedMode.Off;
                changed = true;
            }
            if (_browseUnusedCycle != BrowseFilterCycle.Off)
            {
                BrowseFilterCycle prevU = _browseUnusedCycle;
                _browseUnusedCycle = BrowseFilterCycle.Off;
                ApplyUnusedCycleSortSideEffects(prevU, BrowseFilterCycle.Off);
                changed = true;
            }
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
            {
                UserTagAvailMode restore = _userTagModeBeforeUntagged == UserTagAvailMode.Tag
                    ? UserTagAvailMode.Tag
                    : UserTagAvailMode.FilterByTags;
                _userTagAvailMode = restore;
                try { ClearUntaggedTaggedPinKeys(); } catch { }
                try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                changed = true;
            }
            if (HasLicenseFilter())
            {
                currentLicenseFilter = "";
                CancelLicenseFilterHydrate();
                changed = true;
            }

            MigrateLegacyExclusiveFileSortIfNeeded();
            UpdateGlobalSourceFilterButtonLabel();
            if (changed && refresh)
            {
                RefreshFilesAndTabs();
                SyncBrowseFilterChipChrome();
            }
            else if (changed)
            {
                SyncBrowseFilterChipChrome();
            }
            return changed;
        }

        private bool HasTitleBarBrowseFilterActive()
        {
            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All) return true;
            if (_browseHiddenCycle != BrowseFilterCycle.Off) return true;
            if (_browseAlwaysLoadedCycle != BrowseFilterCycle.Off) return true;
            if (_browseOldVersionsCycle != BrowseFilterCycle.Off) return true;
            if (_browseLoadedMode != BrowseLoadedMode.Off) return true;
            if (_browseUnusedCycle != BrowseFilterCycle.Off) return true;
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged) return true;
            if (HasLicenseFilter()) return true;
            return false;
        }

        private int CountTitleBarBrowseFiltersActive()
        {
            int n = 0;
            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All) n++;
            if (_browseHiddenCycle != BrowseFilterCycle.Off) n++;
            if (_browseAlwaysLoadedCycle != BrowseFilterCycle.Off) n++;
            if (_browseOldVersionsCycle != BrowseFilterCycle.Off) n++;
            if (_browseLoadedMode != BrowseLoadedMode.Off) n++;
            if (_browseUnusedCycle != BrowseFilterCycle.Off) n++;
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged) n++;
            if (HasLicenseFilter()) n++;
            return n;
        }

        private void UpdateGlobalSourceFilterButtonLabel()
        {
            int n = CountTitleBarBrowseFiltersActive();
            string label;
            if (n <= 0)
                label = VPBTranslation.T("gallery.filter.button", "Filter");
            else if (n == 1)
                label = ResolveSingleActiveBrowseFilterShortLabel();
            else
                label = VPBTranslation.T("gallery.filter.button_active", "Filter") + " \u00b7 " + n.ToString();

            if (globalSourceFilterBtnText != null
                && !string.Equals(label, _globalSourceFilterBtnLabelCached, StringComparison.Ordinal))
            {
                globalSourceFilterBtnText.text = label;
                _globalSourceFilterBtnLabelCached = label;
            }

            bool active = n > 0;
            Image backdrop = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<Image>() : null;
            if (backdrop != null)
                backdrop.color = active ? ColorSourceFilter : new Color(0f, 0f, 0f, 0.5f);

            // Icon sprite load is relatively expensive — only when armed count crosses 0.
            bool wasArmed = _globalSourceFilterBtnArmedCount > 0;
            bool nowArmed = active;
            if (globalSourceFilterBtnIcon != null && (wasArmed != nowArmed || _globalSourceFilterBtnArmedCount < 0))
            {
                try
                {
                    string iconPath = nowArmed ? "vpb_icons/filter_on.png" : "vpb_icons/filter_off.png";
                    Sprite sp = UI.LoadIconSprite(iconPath, UI.BarIconGlyphTint);
                    if (sp != null) globalSourceFilterBtnIcon.sprite = sp;
                }
                catch { }
            }
            _globalSourceFilterBtnArmedCount = n;
        }

        /// <summary>Short button text when exactly one browse filter is armed (no alloc beyond T()).</summary>
        private string ResolveSingleActiveBrowseFilterShortLabel()
        {
            if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local)
                return VPBTranslation.T("gallery.filter.source_local", "Local");
            if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var)
                return VPBTranslation.T("gallery.filter.source_var", ".var");
            if (_browseHiddenCycle != BrowseFilterCycle.Off)
                return ResolveBrowseHiddenCycleLabel();
            if (_browseOldVersionsCycle != BrowseFilterCycle.Off)
                return ResolveBrowseOldVersionsCycleLabel();
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                return VPBTranslation.T("gallery.filter.not_tagged", "Not tagged");
            if (_browseAlwaysLoadedCycle != BrowseFilterCycle.Off)
                return ResolveBrowseAlwaysLoadedCycleLabel();
            if (_browseLoadedMode != BrowseLoadedMode.Off)
                return ResolveBrowseLoadedModeLabel();
            if (_browseUnusedCycle != BrowseFilterCycle.Off)
                return ResolveBrowseUnusedCycleLabel();
            if (HasLicenseFilter())
                return ResolveBrowseLicenseFilterLabel();
            return VPBTranslation.T("gallery.filter.button_active", "Filter") + " \u00b7 1";
        }

        private void ComputeGlobalSourceFilterRowCounts(out int allCount, out int localCount, out int varCount)
        {
            allCount = 0;
            localCount = 0;
            varCount = 0;
            if (lastFilteredFiles == null) return;
            for (int i = 0; i < lastFilteredFiles.Count; i++)
            {
                FileEntry e = lastFilteredFiles[i];
                if (e == null) continue;
                allCount++;
                if (IsVarBacked(e)) varCount++;
                else localCount++;
            }
        }

        public void HideGlobalSourceFilterDropdownIfOpen()
        {
            if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
                HideGlobalSourceFilterDropdown();
        }

        /// <summary>
        /// Legacy sort modes HiddenOnly / AutoInstallOnly → Filter cycles.
        /// Keeps enum values stable for persisted cache keys.
        /// </summary>
        private void MigrateLegacyExclusiveFileSortIfNeeded()
        {
            SortState st = GetSortState("Files");
            if (st == null) return;
            bool migrated = false;
            if (st.Type == SortType.HiddenOnly)
            {
                _browseHiddenCycle = BrowseFilterCycle.Only;
                SyncShowHiddenPackagesFromCycle();
                st.Type = SortType.Name;
                st.Direction = SortDirection.Ascending;
                migrated = true;
            }
            else if (st.Type == SortType.AutoInstallOnly)
            {
                if (_browseAlwaysLoadedCycle == BrowseFilterCycle.Off)
                {
                    _browseAlwaysLoadedSavedSort = st.Clone();
                    _browseAlwaysLoadedSavedSort.Type = SortType.Name;
                    _browseAlwaysLoadedSavedSort.Direction = SortDirection.Ascending;
                }
                _browseAlwaysLoadedCycle = BrowseFilterCycle.Only;
                st.Type = SortType.AutoInstall;
                st.Direction = SortDirection.Descending;
                migrated = true;
            }
            else if (st.Type == SortType.LoadedOnly)
            {
                _browseLoadedMode = BrowseLoadedMode.LoadedOnly;
                st.Type = SortType.Name;
                st.Direction = SortDirection.Ascending;
                migrated = true;
            }
            else if (st.Type == SortType.UnloadedOnly)
            {
                _browseLoadedMode = BrowseLoadedMode.UnloadedOnly;
                st.Type = SortType.Name;
                st.Direction = SortDirection.Ascending;
                migrated = true;
            }
            else if (st.Type == SortType.Hidden)
            {
                // Sort-by-hidden removed from menu; escalate to Show-hidden cycle if idle.
                if (_browseHiddenCycle == BrowseFilterCycle.Off)
                {
                    _browseHiddenCycle = BrowseFilterCycle.Apply;
                    SyncShowHiddenPackagesFromCycle();
                }
                st.Type = SortType.Name;
                st.Direction = SortDirection.Ascending;
                migrated = true;
            }
            else if (st.Type == SortType.UnusedOnly)
            {
                if (_browseUnusedCycle == BrowseFilterCycle.Off)
                {
                    _browseUnusedSavedSort = new SortState(SortType.Name, SortDirection.Ascending);
                }
                _browseUnusedCycle = BrowseFilterCycle.Only;
                st.Type = SortType.UsageCount;
                st.Direction = SortDirection.Ascending;
                migrated = true;
            }
            if (!migrated) return;
            SaveSortState("Files", st);
            try { UpdateSortButtonText(fileSortTypeText, fileSortDirText, st); } catch { }
        }

        /// <summary>Hydrate cycles from mirrored settings when cycles still Off (startup / external toggle).</summary>
        private void SyncBrowseFilterCyclesFromMirroredSettings()
        {
            if (_browseHiddenCycle == BrowseFilterCycle.Off)
            {
                try
                {
                    if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages)
                        _browseHiddenCycle = BrowseFilterCycle.Apply;
                }
                catch { }
            }
            if (_browseOldVersionsCycle == BrowseFilterCycle.Off)
            {
                try
                {
                    if (Settings.Instance != null && Settings.Instance.HideOldVersions != null && Settings.Instance.HideOldVersions.Value)
                        _browseOldVersionsCycle = BrowseFilterCycle.Apply;
                }
                catch { }
            }
        }

        internal static bool IsVarBacked(FileEntry entry)
        {
            if (entry == null) return false;
            if (entry is PackageListEntry) return true;
            if (entry is MissingPackageListEntry) return true;
            if (entry is VarFileEntry) return true;
            SystemFileEntry sfe = entry as SystemFileEntry;
            return sfe != null && sfe.isVar;
        }
    }
}
