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
        // Property-sheet popup: label column + 3-way segments. Wider than a plain menu so
        // "Always loaded" / "Untagged" stay readable (Galitz form fill-in; Johnson proximity).
        private const float BrowseFilterMenuPanelWidthRef = 432f;
        private const float BrowseFilterMenuPadRef = GalleryUiDesignTokens.BandPadRef;
        private const float BrowseFilterMenuSpacingRef = GalleryUiDesignTokens.ControlRowGapRef;
        private const float BrowseFilterSectionHeightRef = 22f;
        private const float BrowseFilterLabelColWidthRef = 148f;
        private const float BrowseFilterFieldIconSizeRef = 22f;
        private const float BrowseFilterSegIconSizeRef = 16f;
        private const float BrowseFilterSectionIconSizeRef = 16f;
        private const float BrowseFilterDividerHeightRef = 7f;
        private const float BrowseFilterArmedStripeWidthRef = 3f;
        private const float BrowseFilterLabeledRowPadHRef = GalleryUiDesignTokens.BandPadRef;
        private static readonly Vector3[] BrowseFilterWorldCornersScratch = new Vector3[4];

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
                "Filter: source, visibility, load, license. Icon rows = 3-way choice. Hover a label for meaning. Right-click or Reset clears.");

            // Compact: filter-off idle / filter when active (filter-search is Filter Presets).
            try
            {
                Sprite sp = UI.LoadIconSprite("filter-off", UI.BarIconGlyphTint);
                if (sp != null && globalSourceFilterBtn != null)
                {
                    GameObject iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(globalSourceFilterBtn.transform, false);
                    globalSourceFilterBtnIcon = UI.AddImage(iconGO, Color.white, false);
                    UI.SetIconSprite(globalSourceFilterBtnIcon, sp);
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
                new Vector2(GlobalSourceFilterButtonCenterRelativeX, -72f),
                childAlignment: TextAnchor.UpperCenter,
                configureVlg: vlg =>
                {
                    if (vlg == null) return;
                    vlg.spacing = BrowseFilterMenuSpacingRef;
                    vlg.padding = UI.Pad(BrowseFilterMenuPadRef, BrowseFilterMenuPadRef, BrowseFilterMenuPadRef, BrowseFilterMenuPadRef);
                    vlg.childForceExpandHeight = false;
                    vlg.childForceExpandWidth = true;
                });
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
            try { globalSourceFilterMenuRoot.transform.SetAsLastSibling(); } catch { }
            globalSourceFilterMenuRoot.SetActive(true);
            RebuildGlobalSourceFilterMenuOptions();
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

            // Primary: Source as labeled row (no extra section — von Restorff one focus).
            int sourceSeg = currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local
                ? 1
                : (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var ? 2 : 0);
            AddBrowseFilterLabeledRow(
                VPBTranslation.T("gallery.filter.section_source", "Source"),
                "package",
                currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All,
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
                },
                "gallery.filter.tip.source",
                "All packages, Local (loose files), or .var only.",
                new string[] { "layers-union", "folder-open", "package" });

            AddBrowseFilterDivider();
            AddBrowseFilterSection(VPBTranslation.T("gallery.filter.section_visibility", "Visibility"), "eye");

            AddBrowseFilterLabeledRow(
                VPBTranslation.T("gallery.filter.hidden", "Hidden"),
                "ghost",
                _browseHiddenCycle != BrowseFilterCycle.Off,
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_show", "Show"),
                    VPBTranslation.T("gallery.filter.seg_only", "Only")
                },
                (int)_browseHiddenCycle,
                i => SetBrowseHiddenCycle((BrowseFilterCycle)i, refresh: true),
                "gallery.filter.tip.hidden",
                "Off hides hidden packages. Show includes them. Only = hidden packages only.");

            AddBrowseFilterLabeledRow(
                VPBTranslation.T("gallery.filter.old_versions", "Old versions"),
                "versions",
                IsBrowseOldVersionsNonDefault(_browseOldVersionsCycle),
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_all_versions", "All"),
                    VPBTranslation.T("gallery.filter.seg_newest", "Newest"),
                    VPBTranslation.T("gallery.filter.seg_old_only", "Old only")
                },
                (int)_browseOldVersionsCycle,
                i => SetBrowseOldVersionsCycle((BrowseFilterCycle)i, refresh: true),
                "gallery.filter.tip.old_versions",
                "Newest (default) hides older .var revisions. All = every version. Old only = superseded.");

            int tagSeg = _userTagAvailMode == UserTagAvailMode.FilterUntagged
                ? 1
                : (_userTagAvailMode == UserTagAvailMode.FilterTaggedOnly ? 2 : 0);
            AddBrowseFilterLabeledRow(
                VPBTranslation.T("gallery.filter.user_tags", "User tags"),
                "tags",
                _userTagAvailMode == UserTagAvailMode.FilterUntagged
                    || _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly,
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_untagged", "Untagged"),
                    VPBTranslation.T("gallery.filter.seg_tagged", "Tagged")
                },
                tagSeg,
                i => SetBrowseTagPresenceSegment(i),
                "gallery.filter.tip.user_tags",
                "Off = no tag-presence filter. Untagged / Tagged filter by your tags.");

            AddBrowseFilterDivider();
            AddBrowseFilterSection(VPBTranslation.T("gallery.filter.section_in_scene", "In scene"), "player-play");

            AddBrowseFilterLabeledRow(
                VPBTranslation.T("gallery.filter.always_loaded", "Always loaded"),
                "plug-connected",
                _browseAlwaysLoadedCycle != BrowseFilterCycle.Off,
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_first", "First"),
                    VPBTranslation.T("gallery.filter.seg_only", "Only")
                },
                (int)_browseAlwaysLoadedCycle,
                i => SetBrowseAlwaysLoadedCycle((BrowseFilterCycle)i, refresh: true),
                "gallery.filter.tip.always_loaded",
                "First sorts always-loaded packages to top. Only shows those packages.");

            int loadedSeg = _browseLoadedMode == BrowseLoadedMode.Off
                ? 0
                : (_browseLoadedMode == BrowseLoadedMode.LoadedOnly ? 1 : 2);
            AddBrowseFilterLabeledRow(
                VPBTranslation.T("gallery.filter.loaded", "Loaded"),
                "circle-check",
                _browseLoadedMode != BrowseLoadedMode.Off,
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
                },
                "gallery.filter.tip.loaded",
                "Loaded / Unloaded filter by whether the package is in the current scene.");

            AddBrowseFilterLabeledRow(
                VPBTranslation.T("gallery.filter.unused", "Unused"),
                "history-toggle",
                _browseUnusedCycle != BrowseFilterCycle.Off,
                new string[]
                {
                    VPBTranslation.T("gallery.filter.seg_off", "Off"),
                    VPBTranslation.T("gallery.filter.seg_first", "First"),
                    VPBTranslation.T("gallery.filter.seg_only", "Only")
                },
                (int)_browseUnusedCycle,
                i => SetBrowseUnusedCycle((BrowseFilterCycle)i, refresh: true),
                "gallery.filter.tip.unused",
                "First sorts unused packages up. Only shows packages with no load history.");

            AddBrowseFilterDivider();
            AddBrowseFilterSection(VPBTranslation.T("gallery.filter.section_license", "License"), "book");
            AddBrowseFilterLicenseSegmentGrid();

            if (HasTitleBarBrowseFilterActive())
            {
                AddBrowseFilterDivider();
                AddBrowseFilterResetRow();
            }

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

        private void AddBrowseFilterSection(string text, string iconRelativePath = null)
        {
            if (globalSourceFilterMenuPanelGO == null || string.IsNullOrEmpty(text)) return;

            GameObject header = new GameObject("BrowseFilterSection");
            header.transform.SetParent(globalSourceFilterMenuPanelGO.transform, false);

            Sprite headerIcon = TryLoadBrowseFilterIcon(iconRelativePath, GalleryUiColorTokens.TextDim);

            Text label = UI.CreateLabel(
                header,
                text,
                GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "Text");
            if (label != null)
            {
                label.fontStyle = FontStyle.Bold;
                RectTransform lrt = label.rectTransform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(headerIcon != null ? 26f : 4f, 0f);
                lrt.offsetMax = new Vector2(-4f, 0f);
            }

            if (headerIcon != null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(header.transform, false);
                Image iconImg = UI.AddImage(iconGO, Color.white, false);
                UI.SetIconSprite(iconImg, headerIcon);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                RectTransform irt = iconGO.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(BrowseFilterSectionIconSizeRef, BrowseFilterSectionIconSizeRef);
                irt.anchoredPosition = new Vector2(4f, 0f);
            }

            Image bg = UI.AddImage(header, new Color(0f, 0f, 0f, 0f), false);
            if (bg != null) bg.raycastTarget = false;

            UI.AddLE(header, preferredHeight: BrowseFilterSectionHeightRef, minHeight: BrowseFilterSectionHeightRef, flexibleWidth: 1f);
            RectTransform rt = header.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
            }
        }

        private void AddBrowseFilterDivider()
        {
            if (globalSourceFilterMenuPanelGO == null) return;
            GameObject sep = new GameObject("BrowseFilterDivider");
            sep.transform.SetParent(globalSourceFilterMenuPanelGO.transform, false);
            // Transparent spacer; 1px hairline child — Image on root would paint a 7px slab.
            Image bg = UI.AddImage(sep, new Color(0f, 0f, 0f, 0f), false);
            if (bg != null) bg.raycastTarget = false;
            UI.AddLE(sep, preferredHeight: BrowseFilterDividerHeightRef, minHeight: BrowseFilterDividerHeightRef, flexibleWidth: 1f);
            GameObject line = new GameObject("Line");
            line.transform.SetParent(sep.transform, false);
            Image lineImg = UI.AddImage(line, GalleryUiColorTokens.RimIdle, false);
            if (lineImg != null) lineImg.raycastTarget = false;
            RectTransform lrt = line.GetComponent<RectTransform>();
            if (lrt != null)
            {
                lrt.anchorMin = new Vector2(0f, 0.5f);
                lrt.anchorMax = new Vector2(1f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(0f, 1f);
                lrt.anchoredPosition = Vector2.zero;
            }
        }

        private void AddBrowseFilterResetRow()
        {
            if (globalSourceFilterMenuPanelGO == null) return;
            Sprite icon = TryLoadBrowseFilterIcon("filter-off", GalleryUiColorTokens.TextMuted);
            GameObject row = UI.AddStretchPopupMenuRow(
                globalSourceFilterMenuPanelGO.transform,
                VPBTranslation.T("gallery.filter.reset", "Reset filters"),
                () =>
                {
                    try { ClearTitleBarBrowseFilters(refresh: true); } catch { }
                    if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
                        RebuildGlobalSourceFilterMenuOptions();
                },
                isActive: false,
                enabled: true,
                rowHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef,
                icon: icon);
            if (row != null) row.name = "BrowseFilterReset";
            AddTooltip(row, "gallery.filter.tip.reset", "Clear source, visibility, load, and license filters. Right-click Filter button does the same.");
        }

        /// <summary>
        /// Property-sheet row: icon + field name left, exclusive segments right.
        /// Armed rows get a left stripe + lifted fill (not color-only — Johnson / WCAG).
        /// </summary>
        private void AddBrowseFilterLabeledRow(
            string label,
            string iconRole,
            bool armed,
            string[] segmentLabels,
            int selectedIndex,
            Action<int> onSelect,
            string tooltipKey = null,
            string tooltipDefault = null,
            string[] segmentIcons = null)
        {
            if (globalSourceFilterMenuPanelGO == null || string.IsNullOrEmpty(label) || segmentLabels == null || onSelect == null)
                return;

            GameObject row = new GameObject("BrowseFilterLabeledRow");
            row.transform.SetParent(globalSourceFilterMenuPanelGO.transform, false);
            Color rowFill = armed ? GalleryUiColorTokens.SurfacePanel : new Color(0f, 0f, 0f, 0f);
            Image rowBg = UI.AddImage(row, rowFill, false);
            if (rowBg != null) rowBg.raycastTarget = false;
            UI.AddHLG(row, spacing: GalleryUiDesignTokens.ControlGapRef,
                padding: UI.Pad(BrowseFilterLabeledRowPadHRef, BrowseFilterLabeledRowPadHRef,
                                GalleryUiDesignTokens.ControlRimGutterRef, GalleryUiDesignTokens.ControlRimGutterRef),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: true);
            UI.AddLE(row,
                preferredHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef,
                minHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef,
                flexibleWidth: 1f);

            GameObject stripe = new GameObject("ArmedStripe");
            stripe.transform.SetParent(row.transform, false);
            Color stripeCol = armed ? GalleryUiColorTokens.AccentSelected : new Color(0f, 0f, 0f, 0f);
            Image stripeImg = UI.AddImage(stripe, stripeCol, false);
            if (stripeImg != null) stripeImg.raycastTarget = false;
            UI.AddLE(stripe,
                minWidth: BrowseFilterArmedStripeWidthRef,
                preferredWidth: BrowseFilterArmedStripeWidthRef,
                flexibleWidth: 0f,
                flexibleHeight: 1f);

            GameObject labelCol = new GameObject("Label");
            labelCol.transform.SetParent(row.transform, false);
            Image labelHit = UI.AddImage(labelCol, new Color(0f, 0f, 0f, 0f), true);
            if (labelHit != null) labelHit.raycastTarget = true;
            UI.AddHLG(labelCol, spacing: GalleryUiDesignTokens.ControlGapRef, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: false);
            UI.AddLE(labelCol,
                minWidth: 72f,
                preferredWidth: BrowseFilterLabelColWidthRef,
                flexibleWidth: 0f,
                flexibleHeight: 1f);

            Color iconTint = armed ? GalleryUiColorTokens.TextPrimary : GalleryUiColorTokens.TextDim;
            Sprite fieldIcon = TryLoadBrowseFilterIcon(iconRole, iconTint);
            if (fieldIcon != null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(labelCol.transform, false);
                Image iconImg = UI.AddImage(iconGO, Color.white, false);
                UI.SetIconSprite(iconImg, fieldIcon);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                UI.AddLE(iconGO,
                    minWidth: BrowseFilterFieldIconSizeRef,
                    preferredWidth: BrowseFilterFieldIconSizeRef,
                    minHeight: BrowseFilterFieldIconSizeRef,
                    preferredHeight: BrowseFilterFieldIconSizeRef,
                    flexibleWidth: 0f,
                    flexibleHeight: 0f);
            }

            Color labelColr = armed ? GalleryUiColorTokens.TextPrimary : GalleryUiColorTokens.TextMuted;
            Text labelTxt = UI.CreateLabel(
                labelCol,
                label,
                GalleryUiDesignTokens.FontBodyRef,
                labelColr,
                TextAnchor.MiddleLeft,
                horizontalWrap: HorizontalWrapMode.Overflow,
                raycastTarget: false,
                name: "Text");
            if (labelTxt != null)
            {
                labelTxt.fontStyle = armed ? FontStyle.Bold : FontStyle.Normal;
                UI.AddLE(labelTxt.gameObject, flexibleWidth: 1f, flexibleHeight: 0f);
            }

            if (!string.IsNullOrEmpty(tooltipKey))
                AddTooltip(labelCol, tooltipKey, tooltipDefault ?? "");

            AddBrowseFilterSegmentRow(segmentLabels, selectedIndex, onSelect, allowEmptySlots: false, parent: row, iconRoles: segmentIcons);
        }

        private static Sprite TryLoadBrowseFilterIcon(string iconRole, Color tint)
        {
            if (string.IsNullOrEmpty(iconRole)) return null;
            try { return UI.LoadIconSprite(iconRole, tint); }
            catch { return null; }
        }

        /// <summary>
        /// Visible exclusive segment row (usually 3). Recognition over Shift+click.
        /// Empty labels skipped when <paramref name="allowEmptySlots"/>.
        /// Optional <paramref name="parent"/> nests inside a labeled row.
        /// </summary>
        private void AddBrowseFilterSegmentRow(
            string[] labels,
            int selectedIndex,
            Action<int> onSelect,
            bool allowEmptySlots = false,
            GameObject parent = null,
            string[] iconRoles = null)
        {
            GameObject host = parent != null ? parent : globalSourceFilterMenuPanelGO;
            if (host == null || labels == null || labels.Length == 0 || onSelect == null)
                return;

            bool nested = parent != null && parent != globalSourceFilterMenuPanelGO;

            GameObject row = new GameObject("BrowseFilterSegments");
            row.transform.SetParent(host.transform, false);
            float segPadV = nested ? 0f : GalleryUiDesignTokens.ControlRimGutterRef;
            UI.AddHLG(row, spacing: GalleryUiDesignTokens.ControlRowGapRef,
                padding: UI.Pad(0f, 0f, segPadV, segPadV),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: true,
                childForceExpandHeight: true);
            if (nested)
            {
                UI.AddLE(row, flexibleWidth: 1f, flexibleHeight: 1f, minWidth: 0f);
            }
            else
            {
                UI.AddLE(row,
                    preferredHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef,
                    minHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef,
                    flexibleWidth: 1f);
            }

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
                string iconRole = null;
                if (iconRoles != null && idx < iconRoles.Length)
                    iconRole = iconRoles[idx];
                Sprite segIcon = TryLoadBrowseFilterIcon(iconRole, active ? GalleryUiColorTokens.TextOnAccent : GalleryUiColorTokens.TextDim);

                GameObject seg = UI.CreateUIButton(
                    row,
                    0f,
                    GalleryUiDesignTokens.PopupMenuRowHeightRef,
                    lab,
                    GalleryUiDesignTokens.FontBodyRef,
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
                    t.alignment = segIcon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.verticalOverflow = VerticalWrapMode.Truncate;
                    t.color = active ? GalleryUiColorTokens.TextOnAccent : GalleryUiColorTokens.SegmentIdleText;
                    VPBUiFont.ApplyTo(t);
                    RectTransform trt = t.rectTransform;
                    if (segIcon != null)
                    {
                        float left = BrowseFilterSegIconSizeRef + GalleryUiDesignTokens.Space4Ref;
                        trt.offsetMin = new Vector2(left, 0f);
                        trt.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef, 0f);
                    }
                    else
                    {
                        trt.offsetMin = new Vector2(GalleryUiDesignTokens.ControlGapRef, 0f);
                        trt.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef, 0f);
                    }
                }
                UI.AddLE(seg,
                    minWidth: BrowseFilterSegWidthFor(t, segIcon != null, 1f),
                    preferredWidth: BrowseFilterSegWidthFor(t, segIcon != null, 1f),
                    flexibleWidth: 1f);
                if (segIcon != null)
                {
                    GameObject iconGO = new GameObject("SegIcon");
                    iconGO.transform.SetParent(seg.transform, false);
                    Image iconImg = UI.AddImage(iconGO, Color.white, false);
                    UI.SetIconSprite(iconImg, segIcon);
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.anchorMin = new Vector2(0f, 0.5f);
                    irt.anchorMax = new Vector2(0f, 0.5f);
                    irt.pivot = new Vector2(0f, 0.5f);
                    irt.sizeDelta = new Vector2(BrowseFilterSegIconSizeRef, BrowseFilterSegIconSizeRef);
                    irt.anchoredPosition = new Vector2(5f, 0f);
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

            VerticalLayoutGroup vlg = globalSourceFilterMenuPanelGO.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                int pad = Mathf.RoundToInt(BrowseFilterMenuPadRef * s);
                vlg.padding = new RectOffset(pad, pad, pad, pad);
                vlg.spacing = BrowseFilterMenuSpacingRef * s;
            }

            RectTransform panelRT = globalSourceFilterMenuPanelGO.GetComponent<RectTransform>();
            if (panelRT != null)
                panelRT.sizeDelta = new Vector2(BrowseFilterMenuPanelWidthRef * s, panelRT.sizeDelta.y);

            Transform panel = globalSourceFilterMenuPanelGO.transform;
            for (int i = 0; i < panel.childCount; i++)
                ScaleBrowseFilterMenuChild(panel.GetChild(i), s);

            if (panelRT == null) return;
            panelRT.anchorMin = new Vector2(0.5f, 1f);
            panelRT.anchorMax = new Vector2(0.5f, 1f);
            panelRT.pivot = new Vector2(0.5f, 1f);
            PositionBrowseFilterMenuBelowButton(panelRT, s);
        }

        /// <summary>
        /// Hang panel from Filter button bottom-center in overlay space.
        /// Do not copy click Y or clamp the menu above the button (tall content used to slide up).
        /// </summary>
        private void PositionBrowseFilterMenuBelowButton(RectTransform panelRT, float s)
        {
            if (panelRT == null || globalSourceFilterBtn == null) return;
            RectTransform btnRT = globalSourceFilterBtn.GetComponent<RectTransform>();
            RectTransform overlayRT = panelRT.parent as RectTransform;
            if (overlayRT == null && globalSourceFilterMenuRoot != null)
                overlayRT = globalSourceFilterMenuRoot.GetComponent<RectTransform>();
            if (btnRT == null || overlayRT == null) return;
            if (s <= 0f) s = 1f;

            Camera cam = null;
            try
            {
                Canvas canvas = overlayRT.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = canvas.worldCamera;
            }
            catch { }

            btnRT.GetWorldCorners(BrowseFilterWorldCornersScratch);
            Vector3 bottomMid = (BrowseFilterWorldCornersScratch[0] + BrowseFilterWorldCornersScratch[3]) * 0.5f;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, bottomMid);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRT, screen, cam, out local))
            {
                float gapFallback = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
                panelRT.anchoredPosition = new Vector2(
                    btnRT.anchoredPosition.x,
                    -(GalleryUiDesignTokens.TitleBarHeightRef + gapFallback) * s);
            }
            else
            {
                float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
                Rect o = overlayRT.rect;
                panelRT.anchoredPosition = new Vector2(local.x, local.y - o.yMax - gap);
            }

            try { LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT); } catch { }
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
        }

        private void ScaleBrowseFilterMenuChild(Transform ch, float s)
        {
            if (ch == null) return;
            string name = ch.name;
            LayoutElement le = ch.GetComponent<LayoutElement>();

            if (name == "BrowseFilterSection")
            {
                float h = BrowseFilterSectionHeightRef * s;
                if (le != null)
                {
                    le.preferredHeight = h;
                    le.minHeight = h;
                }
                Text t = ch.GetComponentInChildren<Text>(true);
                if (t != null)
                    GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                Transform iconTr = ch.Find("Icon");
                if (iconTr != null)
                {
                    RectTransform irt = iconTr as RectTransform;
                    if (irt != null)
                    {
                        float sz = BrowseFilterSectionIconSizeRef * s;
                        irt.sizeDelta = new Vector2(sz, sz);
                        irt.anchoredPosition = new Vector2(4f * s, 0f);
                    }
                }
                Text lt = ch.GetComponentInChildren<Text>(true);
                if (lt != null)
                {
                    RectTransform lrt = lt.rectTransform;
                    bool hasIcon = iconTr != null;
                    lrt.offsetMin = new Vector2((hasIcon ? 26f : 4f) * s, 0f);
                    lrt.offsetMax = new Vector2(-4f * s, 0f);
                }
                return;
            }

            if (name == "BrowseFilterDivider")
            {
                float h = BrowseFilterDividerHeightRef * s;
                if (le != null)
                {
                    le.preferredHeight = h;
                    le.minHeight = h;
                }
                Transform lineTr = ch.Find("Line");
                if (lineTr != null)
                {
                    RectTransform lrt = lineTr as RectTransform;
                    if (lrt != null)
                        lrt.sizeDelta = new Vector2(0f, Mathf.Max(1f, s));
                }
                return;
            }

            if (name == "BrowseFilterLabeledRow")
            {
                float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
                if (le != null)
                {
                    le.preferredHeight = rowH;
                    le.minHeight = rowH;
                }
                HorizontalLayoutGroup hlg = ch.GetComponent<HorizontalLayoutGroup>();
                if (hlg != null)
                {
                    hlg.spacing = GalleryUiDesignTokens.ControlGapRef * s;
                    hlg.padding = UI.Pad(BrowseFilterLabeledRowPadHRef, BrowseFilterLabeledRowPadHRef,
                        GalleryUiDesignTokens.ControlRimGutterRef, GalleryUiDesignTokens.ControlRimGutterRef, s);
                }
                Transform stripe = ch.Find("ArmedStripe");
                if (stripe != null)
                {
                    LayoutElement sle = stripe.GetComponent<LayoutElement>();
                    if (sle != null)
                    {
                        float w = BrowseFilterArmedStripeWidthRef * s;
                        sle.minWidth = w;
                        sle.preferredWidth = w;
                    }
                }
                Transform labelTr = ch.Find("Label");
                if (labelTr != null)
                {
                    LayoutElement lle = labelTr.GetComponent<LayoutElement>();
                    if (lle != null)
                    {
                        lle.minWidth = 72f * s;
                        lle.preferredWidth = BrowseFilterLabelColWidthRef * s;
                    }
                    HorizontalLayoutGroup lhlg = labelTr.GetComponent<HorizontalLayoutGroup>();
                    if (lhlg != null) lhlg.spacing = GalleryUiDesignTokens.ControlGapRef * s;
                    Transform iconTr = labelTr.Find("Icon");
                    if (iconTr != null)
                    {
                        LayoutElement ile = iconTr.GetComponent<LayoutElement>();
                        if (ile != null)
                        {
                            float sz = BrowseFilterFieldIconSizeRef * s;
                            ile.minWidth = sz;
                            ile.preferredWidth = sz;
                            ile.minHeight = sz;
                            ile.preferredHeight = sz;
                        }
                    }
                    Transform textTr = labelTr.Find("Text");
                    if (textTr != null)
                    {
                        Text lt = textTr.GetComponent<Text>();
                        if (lt != null)
                            GalleryUiMetrics.ApplyFont(lt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                    }
                }
                Transform segs = ch.Find("BrowseFilterSegments");
                if (segs != null)
                    ScaleBrowseFilterSegmentHost(segs, s, nested: true);
                return;
            }

            if (name == "BrowseFilterSegments")
            {
                ScaleBrowseFilterSegmentHost(ch, s, nested: false);
                return;
            }

            if (name == "BrowseFilterReset")
            {
                float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
                if (le != null)
                {
                    le.preferredHeight = rowH;
                    le.minHeight = rowH;
                }
                Text t = ch.GetComponentInChildren<Text>(true);
                if (t != null)
                    GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuOverflowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                Transform iconTr = ch.Find("RowIcon");
                if (iconTr != null)
                {
                    RectTransform irt = iconTr as RectTransform;
                    if (irt != null)
                    {
                        float iconSz = GalleryUiDesignTokens.PopupMenuRowIconSizeRef * s;
                        irt.sizeDelta = new Vector2(iconSz, iconSz);
                        irt.anchoredPosition = new Vector2(GalleryUiDesignTokens.PopupMenuRowTextPadXRef * s, 0f);
                    }
                    float leftExtraRef = GalleryUiDesignTokens.PopupMenuRowIconSizeRef + GalleryUiDesignTokens.PopupMenuRowIconGapRef;
                    if (t != null)
                        UI.ApplyPopupMenuRowTextPadding(t, s, leftExtraRef);
                }
            }
        }

        private static float BrowseFilterSegWidthFor(Text label, bool hasIcon, float s)
        {
            if (s <= 0f) s = 1f;
            float w = GalleryUiDesignTokens.ControlGapRef * 2f * s;
            if (label != null)
            {
                float pref = 0f;
                try { pref = label.preferredWidth; } catch { pref = 0f; }
                w += pref;
            }
            if (hasIcon)
                w += (BrowseFilterSegIconSizeRef + GalleryUiDesignTokens.Space4Ref) * s;
            return w;
        }

        private void ScaleBrowseFilterSegmentHost(Transform segs, float s, bool nested)
        {
            if (segs == null) return;
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            LayoutElement le = segs.GetComponent<LayoutElement>();
            if (le != null && !nested)
            {
                le.preferredHeight = rowH;
                le.minHeight = rowH;
            }
            HorizontalLayoutGroup hlg = segs.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.spacing = GalleryUiDesignTokens.ControlRowGapRef * s;
                float segPadV = nested ? 0f : GalleryUiDesignTokens.ControlRimGutterRef;
                hlg.padding = UI.Pad(0f, 0f, segPadV, segPadV, s);
            }

            float iconSz = BrowseFilterSegIconSizeRef * s;
            for (int c = 0; c < segs.childCount; c++)
            {
                Transform seg = segs.GetChild(c);
                if (seg == null) continue;
                Text t = seg.GetComponentInChildren<Text>(true);
                if (t != null)
                    GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                Transform iconTr = seg.Find("SegIcon");
                if (iconTr != null)
                {
                    RectTransform irt = iconTr as RectTransform;
                    if (irt != null)
                    {
                        irt.sizeDelta = new Vector2(iconSz, iconSz);
                        irt.anchoredPosition = new Vector2(GalleryUiDesignTokens.ControlGapRef * s, 0f);
                    }
                    if (t != null)
                    {
                        RectTransform trt = t.rectTransform;
                        trt.offsetMin = new Vector2(
                            (BrowseFilterSegIconSizeRef + GalleryUiDesignTokens.Space4Ref) * s, 0f);
                        trt.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef * s, 0f);
                    }
                }
                else if (t != null)
                {
                    RectTransform trt = t.rectTransform;
                    trt.offsetMin = new Vector2(GalleryUiDesignTokens.ControlGapRef * s, 0f);
                    trt.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef * s, 0f);
                }

                LayoutElement segLe = seg.GetComponent<LayoutElement>();
                if (segLe != null)
                {
                    float need = BrowseFilterSegWidthFor(t, iconTr != null, s);
                    segLe.minWidth = need;
                    segLe.preferredWidth = need;
                    segLe.flexibleWidth = 1f;
                }
            }
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
                    string iconPath = nowArmed ? "filter" : "filter-off";
                    Sprite sp = UI.LoadIconSprite(iconPath, UI.BarIconGlyphTint);
                    if (sp != null) UI.SetIconSprite(globalSourceFilterBtnIcon, sp);
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
