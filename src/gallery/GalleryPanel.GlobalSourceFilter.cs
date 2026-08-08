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
        private const float BrowseFilterMenuPanelWidthRef = 320f;

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
                "Filter: source + visibility. Each row is a 3-way choice. Right-click this button clears non-default filters.");

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

            // Opaque fill — thumbs behind must not bleed through.
            Image panelImg = globalSourceFilterMenuPanelGO.GetComponent<Image>();
            if (panelImg != null)
                panelImg.color = GalleryUiColorTokens.PopupSurface;

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_source", "Source"));
            int sourceSeg = currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local
                ? 1
                : (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var ? 2 : 0);
            AddBrowseFilterSegmentRow(
                new string[]
                {
                    VPBTranslation.T("gallery.filter.source_all", "All"),
                    VPBTranslation.T("gallery.filter.source_local", "Local"),
                    VPBTranslation.T("gallery.filter.source_var", ".var")
                },
                sourceSeg,
                i =>
                {
                    VPBConfig.GlobalSourceFilterValue v = i == 1
                        ? VPBConfig.GlobalSourceFilterValue.Local
                        : (i == 2 ? VPBConfig.GlobalSourceFilterValue.Var : VPBConfig.GlobalSourceFilterValue.All);
                    OnGlobalSourceFilterRowClicked(v);
                });

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_visibility", "Visibility"));

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.hidden", "Hidden"));
            AddBrowseFilterSegmentRow(
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_show", "Show"),
                    VPBTranslation.T("gallery.filter.seg_only", "Only")
                },
                (int)_browseHiddenCycle,
                i => SetBrowseHiddenCycle((BrowseFilterCycle)i, refresh: true));

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.old_versions", "Old versions"));
            AddBrowseFilterSegmentRow(
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_all_versions", "All"),
                    VPBTranslation.T("gallery.filter.seg_newest", "Newest"),
                    VPBTranslation.T("gallery.filter.seg_old_only", "Old only")
                },
                (int)_browseOldVersionsCycle,
                i => SetBrowseOldVersionsCycle((BrowseFilterCycle)i, refresh: true));

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.user_tags", "User tags"));
            int tagSeg = _userTagAvailMode == UserTagAvailMode.FilterUntagged
                ? 1
                : (_userTagAvailMode == UserTagAvailMode.FilterTaggedOnly ? 2 : 0);
            AddBrowseFilterSegmentRow(
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_untagged", "Untagged"),
                    VPBTranslation.T("gallery.filter.seg_tagged", "Tagged")
                },
                tagSeg,
                i => SetBrowseTagPresenceSegment(i));

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_more", "More"));

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.always_loaded", "Always loaded"));
            AddBrowseFilterSegmentRow(
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_first", "First"),
                    VPBTranslation.T("gallery.filter.seg_only", "Only")
                },
                (int)_browseAlwaysLoadedCycle,
                i => SetBrowseAlwaysLoadedCycle((BrowseFilterCycle)i, refresh: true));

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.loaded", "Loaded"));
            int loadedSeg = _browseLoadedMode == BrowseLoadedMode.Off
                ? 0
                : (_browseLoadedMode == BrowseLoadedMode.LoadedOnly ? 1 : 2);
            AddBrowseFilterSegmentRow(
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_all", "All"),
                    VPBTranslation.T("gallery.filter.seg_loaded", "Loaded"),
                    VPBTranslation.T("gallery.filter.seg_unloaded", "Unloaded")
                },
                loadedSeg,
                i =>
                {
                    BrowseLoadedMode m = i == 0
                        ? BrowseLoadedMode.Off
                        : (i == 1 ? BrowseLoadedMode.LoadedOnly : BrowseLoadedMode.UnloadedOnly);
                    SetBrowseLoadedMode(m, refresh: true);
                });

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.unused", "Unused"));
            AddBrowseFilterSegmentRow(
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_first", "First"),
                    VPBTranslation.T("gallery.filter.seg_only", "Only")
                },
                (int)_browseUnusedCycle,
                i => SetBrowseUnusedCycle((BrowseFilterCycle)i, refresh: true));

            AddBrowseFilterMenuHeader(VPBTranslation.T("gallery.filter.section_license", "License"));
            AddBrowseFilterLicenseSegmentGrid();

            try { RescaleGlobalSourceFilterMenuInternal(ChromeScale); } catch { }
            LayoutRebuilder.ForceRebuildLayoutImmediate(globalSourceFilterMenuPanelGO.GetComponent<RectTransform>());
        }

        private void SetBrowseTagPresenceSegment(int seg)
        {
            if (seg <= 0)
            {
                if (_userTagAvailMode == UserTagAvailMode.FilterUntagged
                    || _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly)
                {
                    UserTagAvailMode restore = _userTagModeBeforeUntagged == UserTagAvailMode.Tag
                        ? UserTagAvailMode.Tag
                        : UserTagAvailMode.FilterByTags;
                    SetUserTagAvailMode(restore);
                }
                return;
            }
            if (seg == 1)
                SetUserTagAvailMode(UserTagAvailMode.FilterUntagged);
            else
                SetUserTagAvailMode(UserTagAvailMode.FilterTaggedOnly);
        }

        private void AddBrowseFilterLicenseSegmentGrid()
        {
            if (KnownPackageLicenseTypes == null || KnownPackageLicenseTypes.Length == 0) return;
            const int cols = 3;
            // Leading "Any" clears license filter — keeps grid consistent with Off/All pattern.
            int total = KnownPackageLicenseTypes.Length + 1;
            int rows = (total + cols - 1) / cols;
            for (int r = 0; r < rows; r++)
            {
                var labels = new string[cols];
                var indices = new int[cols];
                int selectedInRow = -1;
                for (int c = 0; c < cols; c++)
                {
                    int flat = r * cols + c;
                    if (flat >= total)
                    {
                        labels[c] = "";
                        indices[c] = -1;
                        continue;
                    }
                    if (flat == 0)
                    {
                        labels[c] = VPBTranslation.T("gallery.filter.seg_any", "Any");
                        indices[c] = -1;
                        if (!HasLicenseFilter()) selectedInRow = c;
                    }
                    else
                    {
                        string lic = KnownPackageLicenseTypes[flat - 1];
                        labels[c] = lic;
                        indices[c] = flat - 1;
                        if (HasLicenseFilter()
                            && string.Equals(currentLicenseFilter, lic, StringComparison.OrdinalIgnoreCase))
                            selectedInRow = c;
                    }
                }
                AddBrowseFilterSegmentRow(labels, selectedInRow, c =>
                {
                    int licIdx = indices[c];
                    if (licIdx < 0)
                        ClearLicenseFilter(refresh: true);
                    else
                        SetLicenseFilter(KnownPackageLicenseTypes[licIdx], refresh: true);
                }, allowEmptySlots: true);
            }
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

        /// <summary>
        /// Visible exclusive segment row (usually 3). Recognition over Shift+click.
        /// Empty labels skipped when <paramref name="allowEmptySlots"/>.
        /// </summary>
        private void AddBrowseFilterSegmentRow(
            string[] labels,
            int selectedIndex,
            Action<int> onSelect,
            bool allowEmptySlots = false)
        {
            if (globalSourceFilterMenuPanelGO == null || labels == null || labels.Length == 0 || onSelect == null)
                return;

            GameObject row = new GameObject("BrowseFilterSegments");
            row.transform.SetParent(globalSourceFilterMenuPanelGO.transform, false);
            UI.AddHLG(row, spacing: 3f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: true,
                childForceExpandHeight: true);
            UI.AddLE(row,
                preferredHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef,
                flexibleWidth: 1f);

            if (selectedIndex < -1) selectedIndex = -1;
            if (selectedIndex >= labels.Length) selectedIndex = -1;

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                string lab = labels[i] ?? "";
                if (allowEmptySlots && lab.Length == 0)
                {
                    GameObject spacer = new GameObject("SegSpacer");
                    spacer.transform.SetParent(row.transform, false);
                    LayoutElement sle = spacer.AddComponent<LayoutElement>();
                    sle.flexibleWidth = 1f;
                    sle.minWidth = 0f;
                    continue;
                }

                bool active = idx == selectedIndex;
                GameObject seg = UI.CreateUIButton(
                    row,
                    0f,
                    GalleryUiDesignTokens.PopupMenuRowHeightRef,
                    lab,
                    GalleryUiDesignTokens.FontCaptionRef,
                    0f, 0f,
                    AnchorPresets.stretchAll,
                    () =>
                    {
                        try { onSelect.Invoke(idx); } catch { }
                    });
                if (seg == null) continue;
                seg.name = "Seg" + idx;
                Image img = seg.GetComponent<Image>();
                if (img != null)
                    img.color = active ? GalleryUiColorTokens.AccentSelected : GalleryUiColorTokens.SegmentIdle;
                Text t = seg.GetComponentInChildren<Text>(true);
                if (t != null)
                {
                    t.alignment = TextAnchor.MiddleCenter;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.verticalOverflow = VerticalWrapMode.Truncate;
                    t.color = active ? GalleryUiColorTokens.TextOnAccent : GalleryUiColorTokens.SegmentIdleText;
                    VPBUiFont.ApplyTo(t);
                }
                LayoutElement fle = seg.GetComponent<LayoutElement>();
                if (fle == null) fle = seg.AddComponent<LayoutElement>();
                fle.flexibleWidth = 1f;
                fle.minWidth = 0f;
                fle.preferredWidth = 0f;
                fle.flexibleHeight = 1f;
            }
        }

        /// <summary>Default browse mode: newest package per family.</summary>
        private static BrowseFilterCycle DefaultBrowseOldVersionsCycle
        {
            get { return BrowseFilterCycle.Apply; }
        }

        private static bool IsBrowseOldVersionsNonDefault(BrowseFilterCycle cycle)
        {
            return cycle != DefaultBrowseOldVersionsCycle;
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
                return VPBTranslation.T("gallery.filter.newest_only", "Newest only");
            return VPBTranslation.T("gallery.filter.all_versions", "All versions");
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

            // Segment rows: scale caption font on each Seg* child (ScaleVerticalPopupMenuRows only hits first Text).
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            Transform panel = globalSourceFilterMenuPanelGO.transform;
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform ch = panel.GetChild(i);
                if (ch == null || ch.name != "BrowseFilterSegments") continue;
                LayoutElement le = ch.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = rowH;
                    le.minHeight = rowH;
                }
                for (int c = 0; c < ch.childCount; c++)
                {
                    Transform seg = ch.GetChild(c);
                    if (seg == null) continue;
                    Text t = seg.GetComponentInChildren<Text>(true);
                    if (t != null)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
                }
            }

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
            if (_browseOldVersionsCycle != DefaultBrowseOldVersionsCycle)
            {
                _browseOldVersionsCycle = DefaultBrowseOldVersionsCycle;
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
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged
                || _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly)
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
            if (IsBrowseOldVersionsNonDefault(_browseOldVersionsCycle)) return true;
            if (_browseLoadedMode != BrowseLoadedMode.Off) return true;
            if (_browseUnusedCycle != BrowseFilterCycle.Off) return true;
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged
                || _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly) return true;
            if (HasLicenseFilter()) return true;
            return false;
        }

        private int CountTitleBarBrowseFiltersActive()
        {
            int n = 0;
            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All) n++;
            if (_browseHiddenCycle != BrowseFilterCycle.Off) n++;
            if (_browseAlwaysLoadedCycle != BrowseFilterCycle.Off) n++;
            if (IsBrowseOldVersionsNonDefault(_browseOldVersionsCycle)) n++;
            if (_browseLoadedMode != BrowseLoadedMode.Off) n++;
            if (_browseUnusedCycle != BrowseFilterCycle.Off) n++;
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged
                || _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly) n++;
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
            if (IsBrowseOldVersionsNonDefault(_browseOldVersionsCycle))
                return ResolveBrowseOldVersionsCycleLabel();
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                return VPBTranslation.T("gallery.filter.not_tagged", "Not tagged");
            if (_userTagAvailMode == UserTagAvailMode.FilterTaggedOnly)
                return VPBTranslation.T("gallery.filter.tagged_only", "Tagged only");
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
                    // Legacy cfg: HideOldVersions true → Newest. False keeps All versions (Off).
                    if (Settings.Instance != null && Settings.Instance.HideOldVersions != null
                        && Settings.Instance.HideOldVersions.Value)
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
