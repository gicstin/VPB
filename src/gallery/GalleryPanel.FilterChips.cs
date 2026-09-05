using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const float FilterChipRowHeightRef = 32f;
        private const float FilterChipRowVerticalMarginRef = GalleryUiDesignTokens.FilterChipRowMarginRef;
        private const float FilterChipHorizontalPaddingRef = GalleryUiDesignTokens.TightGapRef;
        private const float FilterChipRowSpacingRef = GalleryUiDesignTokens.TightGapRef;
        private const float FilterChipColumnSpacingRef = GalleryUiDesignTokens.TightGapRef;

        private GameObject _activeFilterChipBarGO;
        private RectTransform _activeFilterChipScrollContentRT;
        private readonly List<GameObject> _activeFilterChipButtons = new List<GameObject>(12);
        // Warm-path pool: reuse chip GOs instead of Destroy+new on every SyncBrowseFilterChipChrome.
        private readonly List<GameObject> _filterChipPoolStandard = new List<GameObject>(16);
        private readonly List<GameObject> _filterChipPoolCompact = new List<GameObject>(4);
        private const int FilterChipPoolMaxIdle = 24;
        private bool _activeFilterChipBarVisible;
        private int _activeFilterChipRowCount = 1;
        private float _lastChipBarAvailWidth = -1f;
        private float _lastBrowseGridLeftInset = 20f;
        private float _lastBrowseGridRightInset = -20f;

        private void CreateActiveFilterChipBar()
        {
            if (backgroundBoxGO == null || _activeFilterChipBarGO != null) return;

            _activeFilterChipBarGO = new GameObject("ActiveFilterChipBar");
            _activeFilterChipBarGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform barRT = _activeFilterChipBarGO.AddComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 1f);
            barRT.anchorMax = new Vector2(1f, 1f);
            barRT.pivot = new Vector2(0.5f, 1f);

            Image barBg = UI.AddImage(_activeFilterChipBarGO, new Color(0f, 0f, 0f, 0f), false);

            // Manual flow-wrap host: chips are positioned by hand (top-left origin) so the row
            // wraps to a new line whenever the next chip would exceed the available width.
            GameObject contentGO = UI.CreateChildRT(_activeFilterChipBarGO, "Content", AnchorPresets.stretchAll);
            _activeFilterChipScrollContentRT = contentGO.GetComponent<RectTransform>();

            _activeFilterChipBarGO.SetActive(false);
        }

        /// <summary>Extra top inset for main grid when browse filter chips are visible (not side tab columns).</summary>
        public float ActiveFilterChromeTopInsetPx(float paneScale)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            float total = TitleSearchChipChromeTopInsetPx(s);
            if (_activeFilterChipBarVisible)
            {
                // Context Bar: hard-cap filter chip rows (overflow goes to +N popup).
                int maxRows = GalleryUiDesignTokens.ContextBarMaxFilterChipRows;
                if (maxRows < 1) maxRows = 1;
                int rows = _activeFilterChipRowCount < 1 ? 1 : _activeFilterChipRowCount;
                if (rows > maxRows) rows = maxRows;
                float rowsH = rows * FilterChipRowHeightRef + (rows - 1) * FilterChipRowSpacingRef;
                total += (rowsH + FilterChipRowVerticalMarginRef) * s;
            }
            return total;
        }

        private bool ShouldShowActiveFilterChipBar()
        {
            if (!IsVisible || isCollapsed) return false;
            if (settingsListViewActive) return false;
            if (cleanupModeActive) return false;
            if (!IsBrowseFilterChipContextActive()) return false;
            return HasActiveBrowseFilters();
        }

        private bool IsBrowseFilterChipContextActive()
        {
            // History browse uses its own side panel; title-bar chips would disagree with that mode.
            if (activeContentType == ContentType.History) return false;
            if (leftActiveContent == ContentType.History || rightActiveContent == ContentType.History) return false;
            return true;
        }

        private bool HasActiveBrowseFilters()
        {
            if (IsFilterActive) return true;
            if (!string.IsNullOrEmpty(nameFilter) && nameFilter.Trim().Length > 0) return true;
            if (activeTags != null && activeTags.Count > 0) return true;
            return HasActiveBrowseFiltersExcludingTitleSearch();
        }

        private bool HasActiveSubPaneOrExtraBrowseFilters()
        {
            // Scene/Appearance Local merged into global Source (HasTitleBarBrowseFilterActive).
            if (clothingSubfilter != 0) return true;
            if (hairSubfilter != 0) return true;
            if (appearanceSubfilter != 0) return true;
            if (SceneHubSubfilterActiveOnCurrentCategory()) return true;
            if (posePeopleFilter != PosePeopleFilter.All) return true;
            if (IsUserTagIncludeExcludeFilterArmed())
                return true;
            return false;
        }

        private void RefreshActiveFilterChips(float availWidth = -1f)
        {
            if (_activeFilterChipBarGO == null) return;

            if (availWidth > 1f) _lastChipBarAvailWidth = availWidth;

            // Reuse scratch list — warm path, no per-refresh List alloc.
            _filterChipSpecScratch.Clear();
            CollectActiveFilterChipSpecs(_filterChipSpecScratch);

            _activeFilterChipBarVisible = ShouldShowActiveFilterChipBar();
            // Title-search chips own their host; empty filter specs → no bar strip.
            if (_filterChipSpecScratch.Count == 0)
                _activeFilterChipBarVisible = false;

            _activeFilterChipBarGO.SetActive(_activeFilterChipBarVisible);
            if (!_activeFilterChipBarVisible)
            {
                ClearActiveFilterChipButtons();
                _filterChipOverflowSpecs.Clear();
                CloseFilterChipOverflowMenu();
                _activeFilterChipRowCount = 1;
                return;
            }

            if (_activeFilterChipScrollContentRT == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            int fontSize = UiMetrics.FontBody();
            float chipH = FilterChipRowHeightRef * s;

            // Context Bar: hard-cap one row; rest → +N overflow popup.
            PackActiveFilterChipsOneRow(_filterChipSpecScratch, s, fontSize, chipH);
        }

        /// <summary>
        /// Position chips LTR on one row (Context Bar). Packer already capped contents;
        /// row count forced to <see cref="GalleryUiDesignTokens.ContextBarMaxFilterChipRows"/>.
        /// </summary>
        private void FlowActiveFilterChips(float s)
        {
            if (_activeFilterChipScrollContentRT == null) return;
            if (s <= 0f) s = 1f;

            float chipH = FilterChipRowHeightRef * s;
            float colSpacing = FilterChipColumnSpacingRef * s;

            float availW = _lastChipBarAvailWidth;
            if (availW <= 1f) availW = _activeFilterChipScrollContentRT.rect.width;
            if (availW <= 1f) availW = float.MaxValue;

            int n = _activeFilterChipButtons.Count;
            float x = 0f;
            const float y = 0f;

            for (int i = 0; i < n; i++)
            {
                GameObject chip = _activeFilterChipButtons[i];
                if (chip == null) continue;
                RectTransform rt = chip.GetComponent<RectTransform>();
                if (rt == null) continue;

                try { LayoutRebuilder.ForceRebuildLayoutImmediate(rt); } catch { }
                float w = LayoutUtility.GetPreferredWidth(rt);
                if (w <= 1f) w = rt.rect.width;

                // One-row Context Bar — never wrap (packer owns overflow).
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, y);

                x += w + colSpacing;
            }

            int maxRows = GalleryUiDesignTokens.ContextBarMaxFilterChipRows;
            if (maxRows < 1) maxRows = 1;
            _activeFilterChipRowCount = maxRows;
        }

        private struct ActiveFilterChipSpec
        {
            public string Label;
            public FilterChipKind Kind;
            public UnityAction OnDismiss;
        }

        private enum FilterChipKind
        {
            Search,
            Tag,
            Creator,
            Rating,
            Source,
            Subfilter,
            UserTag,
            UntaggedOnly,
            PackageDeps,
            PackageDependents,
            PackageMissing,
            PackageFilterBack,
            ClearAll,
            HiddenOnly,
            AlwaysLoaded,
            HideOldVersions,
            ShowHiddenItems,
            License,
            /// <summary>Context Bar compact "+N" — opens overflow popup (not a dismissible filter).</summary>
            Overflow,
        }

        private static Color ResolveFilterChipAccent(FilterChipKind kind)
        {
            switch (kind)
            {
                case FilterChipKind.Search: return ColorTitleSearchFilterActive;
                case FilterChipKind.Tag: return ColorTagFilter;
                case FilterChipKind.Creator: return ColorCreator;
                case FilterChipKind.Rating: return ColorRatingFilter;
                case FilterChipKind.Source: return ColorSourceFilter;
                case FilterChipKind.Subfilter: return ColorSubfilterFilter;
                case FilterChipKind.UserTag: return ColorUserTagFilter;
                case FilterChipKind.UntaggedOnly: return ColorUserTagFilter;
                case FilterChipKind.PackageDeps: return DetailStripColorDeps;
                case FilterChipKind.PackageDependents: return DetailStripColorDependents;
                case FilterChipKind.PackageMissing: return DetailStripColorMissingBad;
                case FilterChipKind.PackageFilterBack: return new Color(0.28f, 0.42f, 0.62f, 1f);
                case FilterChipKind.ClearAll: return ColorCategory;
                case FilterChipKind.HiddenOnly: return new Color(0.55f, 0.35f, 0.55f, 1f);
                case FilterChipKind.AlwaysLoaded: return new Color(0.35f, 0.55f, 0.75f, 1f);
                case FilterChipKind.HideOldVersions: return new Color(0.45f, 0.50f, 0.40f, 1f);
                case FilterChipKind.ShowHiddenItems: return new Color(0.55f, 0.40f, 0.35f, 1f);
                case FilterChipKind.License: return ColorLicense;
                case FilterChipKind.Overflow: return ColorCategory;
                default: return ColorTitleSearchFilterActive;
            }
        }

        private static Color FilterChipDismissBackdrop(Color accent)
        {
            return new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, 0.9f);
        }

        private static Image AddFilterChipRoundedBg(GameObject go, Color color, bool raycastTarget = true)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = color;
            rr.raycastTarget = raycastTarget;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            return rr;
        }

        private static bool IsCompactFilterChipKind(FilterChipKind kind)
        {
            return kind == FilterChipKind.ClearAll
                || kind == FilterChipKind.PackageFilterBack
                || kind == FilterChipKind.Overflow;
        }

        private GameObject AcquireFilterChipControl(Transform parent, ActiveFilterChipSpec spec, float chipH, int fontSize, float s = 1f)
        {
            if (parent == null || spec.OnDismiss == null) return null;
            if (s <= 0f) s = 1f;

            bool isCompact = IsCompactFilterChipKind(spec.Kind);
            List<GameObject> pool = isCompact ? _filterChipPoolCompact : _filterChipPoolStandard;
            GameObject chip = null;
            while (pool.Count > 0 && chip == null)
            {
                int last = pool.Count - 1;
                chip = pool[last];
                pool.RemoveAt(last);
                if (chip == null) continue;
            }

            if (chip == null)
                chip = CreateFilterChipControlScaffold(parent, isCompact, chipH, fontSize, s);
            else
            {
                chip.transform.SetParent(parent, false);
                chip.SetActive(true);
            }

            BindFilterChipControl(chip, spec, chipH, fontSize, s, isCompact);
            return chip;
        }

        private GameObject CreateFilterChipControlScaffold(Transform parent, bool isCompactAction, float chipH, int fontSize, float s)
        {
            GameObject chip = new GameObject(isCompactAction ? "FilterChip_Compact" : "FilterChip_Standard");
            chip.transform.SetParent(parent, false);

            AddFilterChipRoundedBg(chip, Color.white);

            int padLeft = Mathf.RoundToInt(GalleryUiDesignTokens.ControlGapRef * s);
            int padV = Mathf.Max(1, Mathf.RoundToInt(GalleryUiDesignTokens.HairGapRef * s));
            float innerH = Mathf.Max(16f, chipH - padV * 2f);

            UI.AddHLG(chip,
                spacing: isCompactAction ? 0f : GalleryUiDesignTokens.FilterChipLabelDismissGapRef * s,
                padding: isCompactAction
                    ? new RectOffset(padLeft, padLeft, padV, padV)
                    : new RectOffset(padLeft, 0, padV, padV),
                childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: false, childForceExpandHeight: true);

            UI.AddLE(chip, minHeight: chipH, preferredHeight: chipH);

            ContentSizeFitter chipCsf = chip.AddComponent<ContentSizeFitter>();
            chipCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Self-size height (chip is flow-positioned with no parent layout group driving it).
            chipCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text labelTxt = UI.CreateLabel(chip, "", fontSize, Color.white, TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, raycastTarget: false, name: "Label");
            GameObject labelGO = labelTxt.gameObject;
            ContentSizeFitter labelCsf = labelGO.AddComponent<ContentSizeFitter>();
            labelCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            UI.AddLE(labelGO, preferredHeight: innerH, flexibleHeight: 0f);

            if (!isCompactAction)
            {
                Button chipBodyBtn = chip.AddComponent<Button>();
                UI.NeutralizeSelectableColorTint(chipBodyBtn);

                float dismissSize = innerH;
                GameObject dismissGO = new GameObject("Dismiss");
                dismissGO.transform.SetParent(chip.transform, false);
                AddFilterChipRoundedBg(dismissGO, Color.gray);
                Button dismissBtn = dismissGO.AddComponent<Button>();
                dismissBtn.targetGraphic = dismissGO.GetComponent<Image>();
                UI.NeutralizeSelectableColorTint(dismissBtn);
                dismissGO.AddComponent<UIChipDismissClick>();

                float iconPad = GalleryUiDesignTokens.SearchIconButtonPadRef * s;
                Sprite closeSpr = UI.LoadIconSprite("x", Color.white);
                if (closeSpr != null)
                    UI.AddIconToButton(dismissGO, closeSpr, iconPad, Color.gray);
                else
                {
                    Text xTxt = UI.CreateLabel(dismissGO, "\u00d7", (int)GalleryUiDesignTokens.FilterChipDismissSizeRef, Color.white, TextAnchor.MiddleCenter, raycastTarget: false, name: "X");
                    GalleryUiMetrics.ApplyGlyphFont(xTxt, GalleryUiDesignTokens.FilterChipDismissSizeRef, s, GalleryUiDesignTokens.FontMinRef);
                }

                var dismissHover = dismissGO.AddComponent<UIHoverBorder>();
                dismissHover.hoverColor = Color.white;
                dismissHover.borderSize = 2f;
                dismissHover.inward = true;
                dismissHover.ApplyBorderSettings();

                UI.AddLE(dismissGO, minWidth: dismissSize, minHeight: dismissSize, preferredWidth: dismissSize, preferredHeight: dismissSize, flexibleHeight: 0f);
            }
            else
            {
                Button chipBtn = chip.AddComponent<Button>();
                UI.NeutralizeSelectableColorTint(chipBtn);
            }

            chip.AddComponent<UIHoverBorder>();
            try
            {
                var hb = chip.GetComponent<UIHoverBorder>();
                if (hb != null) hb.ApplyBorderSettings();
            }
            catch { }
            return chip;
        }

        private void BindFilterChipControl(GameObject chip, ActiveFilterChipSpec spec, float chipH, int fontSize, float s, bool isCompactAction)
        {
            if (chip == null) return;

            Color accent = ResolveFilterChipAccent(spec.Kind);
            chip.name = "FilterChip_" + spec.Kind;

            Image bg = chip.GetComponent<Image>();
            if (bg != null)
                bg.color = new Color(accent.r, accent.g, accent.b, isCompactAction ? 0.94f : 0.96f);

            int padLeft = Mathf.RoundToInt(GalleryUiDesignTokens.ControlGapRef * s);
            int padV = Mathf.Max(1, Mathf.RoundToInt(GalleryUiDesignTokens.HairGapRef * s));
            float innerH = Mathf.Max(16f, chipH - padV * 2f);

            HorizontalLayoutGroup hlg = chip.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.spacing = isCompactAction ? 0f : GalleryUiDesignTokens.FilterChipLabelDismissGapRef * s;
                hlg.padding = isCompactAction
                    ? new RectOffset(padLeft, padLeft, padV, padV)
                    : new RectOffset(padLeft, 0, padV, padV);
            }

            LayoutElement chipLE = chip.GetComponent<LayoutElement>();
            if (chipLE != null)
            {
                chipLE.minHeight = chipH;
                chipLE.preferredHeight = chipH;
            }

            Transform labelT = chip.transform.Find("Label");
            Text labelTxt = labelT != null ? labelT.GetComponent<Text>() : null;
            if (labelTxt != null)
            {
                labelTxt.text = spec.Label ?? "";
                labelTxt.fontSize = fontSize;
                LayoutElement labelLE = labelTxt.GetComponent<LayoutElement>();
                if (labelLE != null) labelLE.preferredHeight = innerH;
            }

            UnityAction dismiss = spec.OnDismiss;
            Button bodyBtn = chip.GetComponent<Button>();
            if (bodyBtn != null)
            {
                bodyBtn.onClick.RemoveAllListeners();
                bodyBtn.onClick.AddListener(() => { try { dismiss?.Invoke(); } catch { } });
            }

            Transform dismissT = chip.transform.Find("Dismiss");
            if (dismissT != null)
            {
                float dismissSize = innerH;
                Image dismissBg = dismissT.GetComponent<Image>();
                Color dismissColor = FilterChipDismissBackdrop(accent);
                if (dismissBg != null) dismissBg.color = dismissColor;

                LayoutElement dismissLE = dismissT.GetComponent<LayoutElement>();
                if (dismissLE != null)
                {
                    dismissLE.minWidth = dismissSize;
                    dismissLE.minHeight = dismissSize;
                    dismissLE.preferredWidth = dismissSize;
                    dismissLE.preferredHeight = dismissSize;
                }

                Button dismissBtn = dismissT.GetComponent<Button>();
                if (dismissBtn != null)
                    dismissBtn.onClick.RemoveAllListeners();

                UIChipDismissClick dismissClick = dismissT.GetComponent<UIChipDismissClick>();
                if (dismissClick == null)
                    dismissClick = dismissT.gameObject.AddComponent<UIChipDismissClick>();
                UnityAction dismissAction = dismiss;
                dismissClick.OnDismiss = () =>
                {
                    try
                    {
                        if (dismissAction != null) dismissAction.Invoke();
                    }
                    catch { }
                };

                // Icon button tint (if present) follows dismiss backdrop.
                Image iconImg = null;
                for (int i = 0; i < dismissT.childCount; i++)
                {
                    Transform child = dismissT.GetChild(i);
                    if (child != null && child.name != "X")
                    {
                        iconImg = child.GetComponent<Image>();
                        if (iconImg != null) break;
                    }
                }
                if (iconImg != null) iconImg.color = Color.white;
            }

            try
            {
                if (isCompactAction)
                {
                    if (spec.Kind == FilterChipKind.PackageFilterBack)
                        AddTooltip(chip, "gallery.tooltip.filter_back", "Back");
                    else if (spec.Kind == FilterChipKind.Overflow)
                        AddTooltip(chip, "gallery.filter_chip.overflow_tip", "Show more active filters");
                    else
                        AddTooltip(chip, "gallery.filter_chip.clear_all_tip", "Clear all active filters");
                }
                else
                    AddTooltip(chip, "gallery.filter_chip.remove_tip", "Remove this filter");
            }
            catch { }
        }

        private void CollectActiveFilterChipSpecs(List<ActiveFilterChipSpec> specs)
        {
            if (specs == null) return;

            // Package dep/dependent/missing mode — primary constraint; list first.
            CollectPackageFilterChipSpecs(specs);

            // Committed title-search chips live in TitleSearchChipHost (incl/excl rows).
            // Only show aggregate Search chip while live-typing (no committed chips yet).
            if (!HasTitleSearchChips())
            {
                string search = nameFilter != null ? nameFilter.Trim() : "";
                if (search.Length > 0)
                {
                    specs.Add(new ActiveFilterChipSpec
                    {
                        Label = VPBTranslation.T("gallery.filter_chip.search", "Search") + ": " + TruncateFilterChipLabel(search, 28),
                        Kind = FilterChipKind.Search,
                        OnDismiss = () =>
                        {
                            try { ClearTitleBarSearchAndSyncChrome(); } catch { }
                        }
                    });
                }
            }

            if (activeTags != null && activeTags.Count > 0)
            {
                var tags = new List<string>(activeTags);
                tags.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < tags.Count; i++)
                {
                    string tag = tags[i];
                    specs.Add(new ActiveFilterChipSpec
                    {
                        Label = VPBTranslation.T("gallery.filter_chip.tag", "Tag") + ": " + TruncateFilterChipLabel(tag, 22),
                        Kind = FilterChipKind.Tag,
                        OnDismiss = () => DismissTagFilterChip(tag)
                    });
                }
            }

            EnsureCurrentCreatorSet();
            if (_currentCreatorSet.Count > 0)
            {
                var creators = new List<string>(_currentCreatorSet);
                creators.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < creators.Count; i++)
                {
                    string creator = creators[i];
                    specs.Add(new ActiveFilterChipSpec
                    {
                        Label = VPBTranslation.T("gallery.filter_chip.creator", "Creator") + ": " + TruncateFilterChipLabel(creator, 24),
                        Kind = FilterChipKind.Creator,
                        OnDismiss = () => DismissCreatorFilterChip(creator)
                    });
                }
            }

            if (!string.IsNullOrEmpty(currentRatingFilter))
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.rating", "Rating") + ": " + currentRatingFilter,
                    Kind = FilterChipKind.Rating,
                    OnDismiss = () =>
                    {
                        currentRatingFilter = "";
                        try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
                        SyncBrowseFilterChipChrome();
                    }
                });
            }

            if (HasRatingPresenceFilter())
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = ResolveRatingPresenceFilterLabel(),
                    Kind = FilterChipKind.Rating,
                    OnDismiss = () => SetRatingPresenceFilterMode(RatingPresenceFilterMode.Off, refresh: true, showStatus: true)
                });
            }

            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
            {
                string sourceLabel;
                switch (currentGlobalSourceFilter)
                {
                    case VPBConfig.GlobalSourceFilterValue.Local:
                        sourceLabel = VPBTranslation.T("gallery.filter_chip.source_local", "Local only");
                        break;
                    case VPBConfig.GlobalSourceFilterValue.Var:
                        sourceLabel = VPBTranslation.T("gallery.filter_chip.source_var", ".var only");
                        break;
                    default:
                        sourceLabel = VPBTranslation.T("gallery.filter_chip.source", "Source");
                        break;
                }
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = sourceLabel,
                    Kind = FilterChipKind.Source,
                    OnDismiss = () =>
                    {
                        try { OnGlobalSourceFilterRowClicked(VPBConfig.GlobalSourceFilterValue.All); } catch { }
                    }
                });
            }

            if (HasLicenseFilter())
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.license", "License") + ": " + currentLicenseFilter,
                    Kind = FilterChipKind.License,
                    OnDismiss = () => ClearLicenseFilter(refresh: true)
                });
            }

            if (_browseHiddenCycle != BrowseFilterCycle.Off)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = ResolveBrowseHiddenCycleLabel(),
                    Kind = _browseHiddenCycle == BrowseFilterCycle.Only
                        ? FilterChipKind.HiddenOnly
                        : FilterChipKind.ShowHiddenItems,
                    OnDismiss = () => SetBrowseHiddenCycle(BrowseFilterCycle.Off, refresh: true)
                });
            }

            if (_browseAlwaysLoadedCycle != BrowseFilterCycle.Off)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = ResolveBrowseAlwaysLoadedCycleLabel(),
                    Kind = FilterChipKind.AlwaysLoaded,
                    OnDismiss = () => SetBrowseAlwaysLoadedCycle(BrowseFilterCycle.Off, refresh: true)
                });
            }

            if (IsBrowseOldVersionsNonDefault(_browseOldVersionsCycle))
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = ResolveBrowseOldVersionsCycleLabel(),
                    Kind = FilterChipKind.HideOldVersions,
                    OnDismiss = () => SetBrowseOldVersionsCycle(DefaultBrowseOldVersionsCycle, refresh: true)
                });
            }

            if (_browseLoadedMode != BrowseLoadedMode.Off)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = ResolveBrowseLoadedModeLabel(),
                    Kind = FilterChipKind.Subfilter,
                    OnDismiss = () => SetBrowseLoadedMode(BrowseLoadedMode.Off, refresh: true)
                });
            }

            if (_browseUnusedCycle != BrowseFilterCycle.Off)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = ResolveBrowseUnusedCycleLabel(),
                    Kind = FilterChipKind.Subfilter,
                    OnDismiss = () => SetBrowseUnusedCycle(BrowseFilterCycle.Off, refresh: true)
                });
            }

            string subfilterLabel = ResolveActiveCategorySubfilterChipLabel();
            if (!string.IsNullOrEmpty(subfilterLabel))
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = subfilterLabel,
                    Kind = FilterChipKind.Subfilter,
                    OnDismiss = () => DismissCategorySubfilterChip()
                });
            }

            if (posePeopleFilter != PosePeopleFilter.All)
            {
                string poseLabel = posePeopleFilter == PosePeopleFilter.Dual
                    ? VPBTranslation.T("gallery.filter_chip.pose_dual", "Pose: Dual")
                    : VPBTranslation.T("gallery.filter_chip.pose_single", "Pose: Single");
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = poseLabel,
                    Kind = FilterChipKind.Subfilter,
                    OnDismiss = () => DismissPosePeopleFilterChip()
                });
            }

            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.untagged_only", "Untagged only"),
                    Kind = FilterChipKind.UntaggedOnly,
                    OnDismiss = () => DismissUntaggedOnlyFilterChip()
                });
            }
            else if (_userTagAvailMode == UserTagAvailMode.FilterTaggedOnly)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.tagged_only", "Tagged only"),
                    Kind = FilterChipKind.UntaggedOnly,
                    OnDismiss = () => DismissUntaggedOnlyFilterChip()
                });
            }
            else if (IsUserTagIncludeExcludeFilterArmed())
            {
                if (activeUserTags != null && activeUserTags.Count > 0)
                {
                    var userTags = new List<string>(activeUserTags);
                    userTags.Sort(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < userTags.Count; i++)
                    {
                        string ut = userTags[i];
                        specs.Add(new ActiveFilterChipSpec
                        {
                            Label = VPBTranslation.T("gallery.filter_chip.user_tag", "User tag") + ": " + TruncateFilterChipLabel(ut, 22),
                            Kind = FilterChipKind.UserTag,
                            OnDismiss = () => DismissUserTagFilterChip(ut)
                        });
                    }
                }
                if (excludedUserTags != null && excludedUserTags.Count > 0)
                {
                    var xUserTags = new List<string>(excludedUserTags);
                    xUserTags.Sort(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < xUserTags.Count; i++)
                    {
                        string xut = xUserTags[i];
                        specs.Add(new ActiveFilterChipSpec
                        {
                            Label = VPBTranslation.T("gallery.filter_chip.user_tag_exclude", "Exclude tag") + ": " + TruncateFilterChipLabel(xut, 22),
                            Kind = FilterChipKind.UserTag,
                            OnDismiss = () => DismissExcludedUserTagFilterChip(xut)
                        });
                    }
                }
            }

            // Back / Overflow are chrome — don't count toward Clear all eligibility.
            int clearAllEligible = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                FilterChipKind k = specs[i].Kind;
                if (k == FilterChipKind.PackageFilterBack || k == FilterChipKind.Overflow)
                    continue;
                clearAllEligible++;
            }
            if (clearAllEligible >= 2)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.clear_all", "Clear all"),
                    Kind = FilterChipKind.ClearAll,
                    OnDismiss = () =>
                    {
                        try { ClearAllBrowseFiltersKeepCategory(); } catch { RefreshFiles(true); }
                    }
                });
            }
        }

        private void CollectPackageFilterChipSpecs(List<ActiveFilterChipSpec> specs)
        {
            if (specs == null || !IsFilterActive) return;

            string modeLabel = GetFilterModeLabel;
            if (string.IsNullOrEmpty(modeLabel))
                modeLabel = VPBTranslation.T("gallery.filter_chip.package_filter", "Filter");

            FilterChipKind kind;
            if (string.Equals(modeLabel, "Missing", StringComparison.OrdinalIgnoreCase))
                kind = FilterChipKind.PackageMissing;
            else if (currentPackageFilterMode == PackageFilterMode.Dependents)
                kind = FilterChipKind.PackageDependents;
            else
                kind = FilterChipKind.PackageDeps;

            specs.Add(new ActiveFilterChipSpec
            {
                Label = modeLabel + ": " + GetFilterModeCount,
                Kind = kind,
                OnDismiss = () =>
                {
                    try { ClearPackageFilter(); } catch { }
                }
            });

            if (_filterStack.Count > 1)
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.back", "Back"),
                    Kind = FilterChipKind.PackageFilterBack,
                    OnDismiss = () =>
                    {
                        try { NavigateBack(); } catch { }
                    }
                });
            }
        }

        private void DismissTagFilterChip(string tag)
        {
            if (string.IsNullOrEmpty(tag) || activeTags == null) return;
            RemoveActiveTagFilter(tag);
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
        }

        private void RemoveActiveTagFilter(string tag)
        {
            if (activeTags == null || string.IsNullOrEmpty(tag)) return;
            if (activeTags.Remove(tag)) return;
            string found = null;
            foreach (string t in activeTags)
            {
                if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                {
                    found = t;
                    break;
                }
            }
            if (found != null) activeTags.Remove(found);
        }

        private void DismissCategorySubfilterChip()
        {
            clothingSubfilter = 0;
            hairSubfilter = 0;
            appearanceSubfilter = 0;
            SetSceneHubSubfilterExplicit(0);
            _clothingGenderUserOverride = false;
            _hairGenderUserOverride = false;
            tagsCached = false;
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
        }

        private void DismissPosePeopleFilterChip()
        {
            posePeopleFilter = PosePeopleFilter.All;
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
        }

        private void DismissUntaggedOnlyFilterChip()
        {
            UserTagAvailMode restore = _userTagModeBeforeUntagged == UserTagAvailMode.Tag
                ? UserTagAvailMode.Tag
                : UserTagAvailMode.FilterByTags;
            SetUserTagAvailMode(restore);
            SyncBrowseFilterChipChrome();
        }

        private void DismissUserTagFilterChip(string tag)
        {
            if (activeUserTags == null || string.IsNullOrEmpty(tag)) return;
            if (!activeUserTags.Remove(tag))
            {
                string found = null;
                foreach (string t in activeUserTags)
                {
                    if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        found = t;
                        break;
                    }
                }
                if (found != null) activeUserTags.Remove(found);
            }
            try { BridgeTitleSearchTagChipFromFilterSet(tag); } catch { }
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
        }

        private void DismissExcludedUserTagFilterChip(string tag)
        {
            if (excludedUserTags == null || string.IsNullOrEmpty(tag)) return;
            if (!excludedUserTags.Remove(tag))
            {
                string found = null;
                foreach (string t in excludedUserTags)
                {
                    if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        found = t;
                        break;
                    }
                }
                if (found != null) excludedUserTags.Remove(found);
            }
            try { BridgeTitleSearchTagChipFromFilterSet(tag); } catch { }
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
        }

        private string ResolveActiveCategorySubfilterChipLabel()
        {
            string title = currentCategoryTitle ?? "";
            if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0 && clothingSubfilter != 0)
                return VPBTranslation.T("gallery.filter_chip.clothing_sub", "Clothing") + ": " + DescribeClothingSubfilter(clothingSubfilter);
            if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 && hairSubfilter != 0)
                return VPBTranslation.T("gallery.filter_chip.hair_sub", "Hair") + ": " + DescribeHairSubfilter(hairSubfilter);
            if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0 && appearanceSubfilter != 0)
                return VPBTranslation.T("gallery.filter_chip.appearance_sub", "Appearance") + ": " + DescribeAppearanceSubfilter(appearanceSubfilter);
            if (SceneHubSubfilterActiveOnCurrentCategory())
                return VPBTranslation.T("gallery.filter_chip.scene_hub", "Hub type") + ": "
                    + DescribeSceneHubSubfilter(EffectiveSceneHubSubfilter());
            return null;
        }

        private static string DescribeClothingSubfilter(ClothingSubfilter f)
        {
            var parts = new System.Collections.Generic.List<string>(4);
            if ((f & ClothingSubfilter.RealClothing) != 0) parts.Add("Real Clothing");
            if ((f & ClothingSubfilter.Presets) != 0) parts.Add("Presets");
            if ((f & ClothingSubfilter.Custom) != 0) parts.Add("Custom");
            if ((f & ClothingSubfilter.CustomPreset) != 0) parts.Add("Custom Preset");
            if ((f & ClothingSubfilter.Items) != 0) parts.Add("Base Clothing");
            if ((f & ClothingSubfilter.Male) != 0) parts.Add("Male");
            if ((f & ClothingSubfilter.Female) != 0) parts.Add("Female");
            if ((f & ClothingSubfilter.Decals) != 0) parts.Add("Decals");
            return parts.Count > 0 ? string.Join("+", parts.ToArray()) : "Active";
        }

        private static string DescribeHairSubfilter(HairSubfilter f)
        {
            var parts = new System.Collections.Generic.List<string>(4);
            if ((f & HairSubfilter.Presets) != 0) parts.Add("Presets");
            if ((f & HairSubfilter.Custom) != 0) parts.Add("Custom");
            if ((f & HairSubfilter.CustomPreset) != 0) parts.Add("Custom Preset");
            if ((f & HairSubfilter.Items) != 0) parts.Add("Base Hair");
            if ((f & HairSubfilter.Male) != 0) parts.Add("Male");
            if ((f & HairSubfilter.Female) != 0) parts.Add("Female");
            return parts.Count > 0 ? string.Join("+", parts.ToArray()) : "Active";
        }

        private static string DescribeAppearanceSubfilter(AppearanceSubfilter f)
        {
            var parts = new System.Collections.Generic.List<string>(4);
            if ((f & AppearanceSubfilter.Presets) != 0) parts.Add("Presets");
            if ((f & AppearanceSubfilter.Custom) != 0) parts.Add("Custom");
            if ((f & AppearanceSubfilter.Male) != 0) parts.Add("Male");
            if ((f & AppearanceSubfilter.Female) != 0) parts.Add("Female");
            if ((f & AppearanceSubfilter.Futa) != 0) parts.Add("Futa");
            if ((f & AppearanceSubfilter.Unknown) != 0) parts.Add("Unknown");
            return parts.Count > 0 ? string.Join("+", parts.ToArray()) : "Active";
        }

        private static string DescribeSceneHubSubfilter(SceneHubSubfilter f)
        {
            var parts = new System.Collections.Generic.List<string>(4);
            if ((f & SceneHubSubfilter.HubScenes) != 0) parts.Add("Scenes");
            if ((f & SceneHubSubfilter.HubLooks) != 0) parts.Add("Looks");
            if ((f & SceneHubSubfilter.Unclassified) != 0) parts.Add("Unclassified");
            if ((f & SceneHubSubfilter.Other) != 0) parts.Add("Other");
            return parts.Count > 0 ? string.Join("+", parts.ToArray()) : "Active";
        }

        private void DismissCreatorFilterChip(string creator)
        {
            RemoveCreatorFromActiveFilter(creator);
            SetCreatorFilterFromSetAndSync();
            try { OnCreatorFilterChanged(refreshFilesAndTabs: true); } catch { RefreshFiles(true); }
            try { HideTitleCreatorDropdown(); } catch { }
        }

        /// <summary>Refresh filter chip row immediately after browse filter state changes.</summary>
        private void SyncBrowseFilterChipChrome()
        {
            bool wasVisible = _activeFilterChipBarVisible || _titleSearchChipHostVisible;
            int wasSearchRows = _titleSearchChipRowCount;
            try { RebuildTitleSearchChipUi(); } catch { }
            try { RefreshActiveFilterChips(); } catch { }
            // Keep mode banner cache coherent if sticky chrome is up.
            try
            {
                InvalidateModeSemanticsBannerCache();
                SyncModeSemanticsBanner(GetActiveStickyToolMode(), ApplySemanticsLabel());
            }
            catch { }
            try
            {
                bool nowVisible = _activeFilterChipBarVisible || _titleSearchChipHostVisible;
                if (wasVisible != nowVisible || wasSearchRows != _titleSearchChipRowCount)
                    UpdateLayout();
                else if (nowVisible)
                    ApplyActiveFilterChipBarLayout(_lastBrowseGridLeftInset, _lastBrowseGridRightInset,
                        ChromeScale);
            }
            catch { }
            try { UpdateEmptyGridState(); } catch { }
        }

        private static string TruncateFilterChipLabel(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen - 1) + "\u2026";
        }

        private void ClearActiveFilterChipButtons()
        {
            ReturnActiveFilterChipsToPool();
        }

        private void ReturnActiveFilterChipsToPool()
        {
            for (int i = 0; i < _activeFilterChipButtons.Count; i++)
            {
                GameObject go = _activeFilterChipButtons[i];
                if (go == null) continue;
                try { ReturnFilterChipToPool(go); } catch { }
            }
            _activeFilterChipButtons.Clear();
        }

        private void ReturnFilterChipToPool(GameObject go)
        {
            if (go == null) return;

            Button bodyBtn = go.GetComponent<Button>();
            if (bodyBtn != null) bodyBtn.onClick.RemoveAllListeners();
            Transform dismissT = go.transform.Find("Dismiss");
            if (dismissT != null)
            {
                Button dismissBtn = dismissT.GetComponent<Button>();
                if (dismissBtn != null) dismissBtn.onClick.RemoveAllListeners();
                UIChipDismissClick dismissClick = dismissT.GetComponent<UIChipDismissClick>();
                if (dismissClick != null) dismissClick.OnDismiss = null;
            }

            bool isCompact = dismissT == null;
            List<GameObject> pool = isCompact ? _filterChipPoolCompact : _filterChipPoolStandard;
            if (pool.Count >= FilterChipPoolMaxIdle)
            {
                try { Destroy(go); } catch { }
                return;
            }

            go.SetActive(false);
            if (_activeFilterChipScrollContentRT != null)
                go.transform.SetParent(_activeFilterChipScrollContentRT, false);
            pool.Add(go);
        }

        /// <summary>Align chip bar with main grid column — same horizontal insets as <see cref="contentScrollRT"/>.</summary>
        private void ApplyActiveFilterChipBarLayout(float leftOffset, float rightOffset, float paneScale)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            _lastBrowseGridLeftInset = leftOffset;
            _lastBrowseGridRightInset = rightOffset;

            try { ApplyTitleSearchChipHostLayout(leftOffset, rightOffset, s); } catch { }

            if (_activeFilterChipBarGO == null || !_activeFilterChipBarVisible) return;

            // Title-search chip host sits under title bar; ActiveFilterChipBar stacks below it.
            float titleBottom = -GalleryUiDesignTokens.SideTabTopOffsetRef * s;
            float searchChipH = TitleSearchChipChromeTopInsetPx(s);
            float barTop = titleBottom - searchChipH;
            float gridTop = titleBottom - ActiveFilterChromeTopInsetPx(s);
            float pad = FilterChipHorizontalPaddingRef * s;
            float margin = FilterChipRowVerticalMarginRef * 0.5f * s;

            RectTransform rt = _activeFilterChipBarGO.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // Match grid column exactly (contentScrollRT offsetMin/Max X).
            rt.offsetMin = new Vector2(leftOffset + pad, gridTop + margin);
            rt.offsetMax = new Vector2(rightOffset - pad, barTop - margin);

            // Re-flow against the resolved bar width (covers cases where the up-front estimate differs).
            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                float availW = rt.rect.width;
                if (availW > 1f)
                {
                    _lastChipBarAvailWidth = availW;
                    FlowActiveFilterChips(s);
                }
            }
            catch { }

            try
            {
                // Above grid, below side-tab scroll columns. Title search chips stay above this bar.
                if (contentScrollRT != null)
                {
                    int gridIdx = contentScrollRT.transform.GetSiblingIndex();
                    if (_titleSearchChipHostGO != null && _titleSearchChipHostVisible)
                    {
                        _titleSearchChipHostGO.transform.SetSiblingIndex(gridIdx + 1);
                        _activeFilterChipBarGO.transform.SetSiblingIndex(gridIdx + 2);
                    }
                    else
                        _activeFilterChipBarGO.transform.SetSiblingIndex(gridIdx + 1);
                }
                else
                    _activeFilterChipBarGO.transform.SetAsLastSibling();
            }
            catch { }
        }
    }
}
