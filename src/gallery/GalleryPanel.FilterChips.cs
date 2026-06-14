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
        private const float FilterChipRowVerticalMarginRef = 6f;
        private const float FilterChipHorizontalPaddingRef = 4f;
        private const float FilterChipRowSpacingRef = 4f;
        private const float FilterChipColumnSpacingRef = 6f;

        private GameObject _activeFilterChipBarGO;
        private RectTransform _activeFilterChipScrollContentRT;
        private readonly List<GameObject> _activeFilterChipButtons = new List<GameObject>(12);
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

            Image barBg = _activeFilterChipBarGO.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0f);
            barBg.raycastTarget = false;

            // Manual flow-wrap host: chips are positioned by hand (top-left origin) so the row
            // wraps to a new line whenever the next chip would exceed the available width.
            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(_activeFilterChipBarGO.transform, false);
            _activeFilterChipScrollContentRT = contentGO.AddComponent<RectTransform>();
            _activeFilterChipScrollContentRT.anchorMin = Vector2.zero;
            _activeFilterChipScrollContentRT.anchorMax = Vector2.one;
            _activeFilterChipScrollContentRT.offsetMin = Vector2.zero;
            _activeFilterChipScrollContentRT.offsetMax = Vector2.zero;

            _activeFilterChipBarGO.SetActive(false);
        }

        /// <summary>Extra top inset for main grid when browse filter chips are visible (not side tab columns).</summary>
        public float ActiveFilterChromeTopInsetPx(float paneScale)
        {
            if (!_activeFilterChipBarVisible) return 0f;
            float s = paneScale <= 0f ? 1f : paneScale;
            int rows = _activeFilterChipRowCount < 1 ? 1 : _activeFilterChipRowCount;
            float rowsH = rows * FilterChipRowHeightRef + (rows - 1) * FilterChipRowSpacingRef;
            return (rowsH + FilterChipRowVerticalMarginRef) * s;
        }

        private bool ShouldShowActiveFilterChipBar()
        {
            if (!IsVisible || isCollapsed) return false;
            if (IsSettingsPanelOpen() || settingsListViewActive) return false;
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
            if (!string.IsNullOrEmpty(nameFilter) && nameFilter.Trim().Length > 0) return true;
            if (activeTags != null && activeTags.Count > 0) return true;
            return HasActiveBrowseFiltersExcludingTitleSearch();
        }

        private bool HasActiveSubPaneOrExtraBrowseFilters()
        {
            if (!string.IsNullOrEmpty(currentSceneSourceFilter)) return true;
            if (!string.IsNullOrEmpty(currentAppearanceSourceFilter)) return true;
            if (clothingSubfilter != 0) return true;
            if (hairSubfilter != 0) return true;
            if (appearanceSubfilter != 0) return true;
            if (posePeopleFilter != PosePeopleFilter.All) return true;
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged) return true;
            if (_userTagAvailMode == UserTagAvailMode.FilterByTags && activeUserTags != null && activeUserTags.Count > 0)
                return true;
            return false;
        }

        private void RefreshActiveFilterChips(float availWidth = -1f)
        {
            if (_activeFilterChipBarGO == null) return;

            _activeFilterChipBarVisible = ShouldShowActiveFilterChipBar();
            _activeFilterChipBarGO.SetActive(_activeFilterChipBarVisible);
            if (!_activeFilterChipBarVisible)
            {
                ClearActiveFilterChipButtons();
                _activeFilterChipRowCount = 1;
                return;
            }

            if (availWidth > 1f) _lastChipBarAvailWidth = availWidth;

            var specs = new List<ActiveFilterChipSpec>(12);
            CollectActiveFilterChipSpecs(specs);

            ClearActiveFilterChipButtons();
            if (_activeFilterChipScrollContentRT == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            int fontSize = UiMetrics.FontBody();
            float chipH = FilterChipRowHeightRef * s;

            for (int i = 0; i < specs.Count; i++)
            {
                ActiveFilterChipSpec spec = specs[i];
                GameObject chip = CreateFilterChipControl(_activeFilterChipScrollContentRT, spec, chipH, fontSize, s);
                if (chip != null) _activeFilterChipButtons.Add(chip);
            }

            FlowActiveFilterChips(s);
        }

        /// <summary>Wrap chips into rows that fit <see cref="_lastChipBarAvailWidth"/>; updates row count for grid inset.</summary>
        private void FlowActiveFilterChips(float s)
        {
            if (_activeFilterChipScrollContentRT == null) return;
            if (s <= 0f) s = 1f;

            float chipH = FilterChipRowHeightRef * s;
            float rowSpacing = FilterChipRowSpacingRef * s;
            float colSpacing = FilterChipColumnSpacingRef * s;

            float availW = _lastChipBarAvailWidth;
            if (availW <= 1f) availW = _activeFilterChipScrollContentRT.rect.width;
            if (availW <= 1f) availW = float.MaxValue;

            int n = _activeFilterChipButtons.Count;
            float x = 0f, y = 0f;
            int rows = 1;

            for (int i = 0; i < n; i++)
            {
                GameObject chip = _activeFilterChipButtons[i];
                if (chip == null) continue;
                RectTransform rt = chip.GetComponent<RectTransform>();
                if (rt == null) continue;

                try { LayoutRebuilder.ForceRebuildLayoutImmediate(rt); } catch { }
                float w = LayoutUtility.GetPreferredWidth(rt);
                if (w <= 1f) w = rt.rect.width;

                if (x > 0f && x + w > availW + 0.5f)
                {
                    x = 0f;
                    y -= chipH + rowSpacing;
                    rows++;
                }

                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, y);

                x += w + colSpacing;
            }

            _activeFilterChipRowCount = rows < 1 ? 1 : rows;
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
            ClearAll,
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
                case FilterChipKind.ClearAll: return ColorCategory;
                default: return ColorTitleSearchFilterActive;
            }
        }

        private static Color FilterChipDismissBackdrop(Color accent)
        {
            return new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, 0.9f);
        }

        private GameObject CreateFilterChipControl(Transform parent, ActiveFilterChipSpec spec, float chipH, int fontSize, float s = 1f)
        {
            if (parent == null || spec.OnDismiss == null) return null;
            if (s <= 0f) s = 1f;

            bool isClearAll = spec.Kind == FilterChipKind.ClearAll;
            Color accent = ResolveFilterChipAccent(spec.Kind);

            GameObject chip = new GameObject(isClearAll ? "FilterChipClearAll" : "FilterChip_" + spec.Kind);
            chip.transform.SetParent(parent, false);

            Image bg = chip.AddComponent<Image>();
            bg.raycastTarget = true;
            bg.color = new Color(accent.r, accent.g, accent.b, isClearAll ? 0.94f : 0.96f);

            HorizontalLayoutGroup row = chip.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(Mathf.RoundToInt(10 * s), Mathf.RoundToInt(4 * s), Mathf.RoundToInt(2 * s), Mathf.RoundToInt(2 * s));
            row.spacing = 2f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            LayoutElement chipLE = chip.AddComponent<LayoutElement>();
            chipLE.preferredHeight = chipH;
            chipLE.minHeight = chipH;

            ContentSizeFitter chipCsf = chip.AddComponent<ContentSizeFitter>();
            chipCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Self-size height (chip is flow-positioned with no parent layout group driving it).
            chipCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(chip.transform, false);
            Text labelTxt = labelGO.AddComponent<Text>();
            labelTxt.text = spec.Label;
            try { VPBUiFont.ApplyTo(labelTxt); } catch { labelTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            labelTxt.fontSize = fontSize;
            labelTxt.fontStyle = FontStyle.Normal;
            labelTxt.alignment = TextAnchor.MiddleLeft;
            labelTxt.color = Color.white;
            labelTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelTxt.raycastTarget = false;
            ContentSizeFitter labelCsf = labelGO.AddComponent<ContentSizeFitter>();
            labelCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            UnityAction dismiss = spec.OnDismiss;
            if (!isClearAll)
            {
                // Clicking anywhere on the chip (label area) dismisses it — child dismiss button
                // consumes its own click so the parent only fires when the label area is hit.
                Button chipBodyBtn = chip.AddComponent<Button>();
                UI.NeutralizeSelectableColorTint(chipBodyBtn);
                chipBodyBtn.onClick.AddListener(() => { try { dismiss?.Invoke(); } catch { } });

                float dismissSize = Mathf.Max(18f, chipH - 4f);
                GameObject dismissGO = new GameObject("Dismiss");
                dismissGO.transform.SetParent(chip.transform, false);
                Image dismissBg = dismissGO.AddComponent<Image>();
                dismissBg.color = FilterChipDismissBackdrop(accent);
                dismissBg.raycastTarget = true;
                Button dismissBtn = dismissGO.AddComponent<Button>();
                UI.NeutralizeSelectableColorTint(dismissBtn);
                dismissBtn.onClick.AddListener(() => { try { dismiss?.Invoke(); } catch { } });

                GameObject xGO = new GameObject("X");
                xGO.transform.SetParent(dismissGO.transform, false);
                Text xTxt = xGO.AddComponent<Text>();
                xTxt.text = "\u00d7";
                try { VPBUiFont.ApplyTo(xTxt); } catch { xTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                xTxt.fontSize = GalleryUiMetrics.GlyphFontFromControlHeight(
                    GalleryUiDesignTokens.FilterChipRowHeightRef - 4f, ChromeScale, GalleryUiDesignTokens.FontMinRef);
                xTxt.fontStyle = FontStyle.Normal;
                xTxt.alignment = TextAnchor.MiddleCenter;
                xTxt.color = Color.white;
                xTxt.raycastTarget = false;
                RectTransform xRT = xGO.GetComponent<RectTransform>();
                xRT.anchorMin = Vector2.zero;
                xRT.anchorMax = Vector2.one;
                xRT.offsetMin = Vector2.zero;
                xRT.offsetMax = Vector2.zero;

                LayoutElement dismissLE = dismissGO.AddComponent<LayoutElement>();
                dismissLE.preferredWidth = dismissSize;
                dismissLE.minWidth = dismissSize;
                dismissLE.preferredHeight = dismissSize;
                dismissLE.minHeight = dismissSize;

                try { AddTooltip(chip, "gallery.filter_chip.remove_tip", "Remove this filter"); } catch { }
            }
            else
            {
                Button chipBtn = chip.AddComponent<Button>();
                UI.NeutralizeSelectableColorTint(chipBtn);
                chipBtn.onClick.AddListener(() => { try { dismiss?.Invoke(); } catch { } });
                try { AddTooltip(chip, "gallery.filter_chip.clear_all_tip", "Clear all active filters"); } catch { }
            }

            chip.AddComponent<UIHoverBorder>();
            return chip;
        }

        private void CollectActiveFilterChipSpecs(List<ActiveFilterChipSpec> specs)
        {
            if (specs == null) return;

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

            if (string.Equals(currentSceneSourceFilter, "local", StringComparison.OrdinalIgnoreCase))
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.scene_local", "Scene: Local only"),
                    Kind = FilterChipKind.Source,
                    OnDismiss = () => DismissSceneSourceLocalFilterChip()
                });
            }

            if (string.Equals(currentAppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase))
            {
                specs.Add(new ActiveFilterChipSpec
                {
                    Label = VPBTranslation.T("gallery.filter_chip.appearance_local", "Appearance: Local only"),
                    Kind = FilterChipKind.Source,
                    OnDismiss = () => DismissAppearanceSourceLocalFilterChip()
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
            else if (_userTagAvailMode == UserTagAvailMode.FilterByTags && activeUserTags != null && activeUserTags.Count > 0)
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

            if (specs.Count >= 2)
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

        private void DismissSceneSourceLocalFilterChip()
        {
            currentSceneSourceFilter = "";
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
        }

        private void DismissAppearanceSourceLocalFilterChip()
        {
            currentAppearanceSourceFilter = "";
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
        }

        private void DismissCategorySubfilterChip()
        {
            clothingSubfilter = 0;
            hairSubfilter = 0;
            appearanceSubfilter = 0;
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
            _userTagAvailMode = ResolveDefaultUserTagAvailMode();
            try { ClearUntaggedTaggedPinKeys(); } catch { }
            try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
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
            bool wasVisible = _activeFilterChipBarVisible;
            try { RefreshActiveFilterChips(); } catch { }
            try
            {
                if (wasVisible != _activeFilterChipBarVisible)
                    UpdateLayout();
                else if (_activeFilterChipBarVisible)
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
            for (int i = 0; i < _activeFilterChipButtons.Count; i++)
            {
                GameObject go = _activeFilterChipButtons[i];
                if (go != null)
                {
                    try { Destroy(go); } catch { }
                }
            }
            _activeFilterChipButtons.Clear();
        }

        /// <summary>Align chip bar with main grid column — same horizontal insets as <see cref="contentScrollRT"/>.</summary>
        private void ApplyActiveFilterChipBarLayout(float leftOffset, float rightOffset, float paneScale)
        {
            if (_activeFilterChipBarGO == null || !_activeFilterChipBarVisible) return;

            float s = paneScale <= 0f ? 1f : paneScale;
            // Align top edge with the grid reference (SideTabTopOffsetRef), so chips tuck directly
            // under the title bar instead of leaving a scale-dependent gap.
            float titleBottom = -GalleryUiDesignTokens.SideTabTopOffsetRef * s;
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
            rt.offsetMax = new Vector2(rightOffset - pad, titleBottom - margin);

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
                // Above grid, below side-tab scroll columns.
                if (contentScrollRT != null)
                    _activeFilterChipBarGO.transform.SetSiblingIndex(contentScrollRT.transform.GetSiblingIndex() + 1);
                else
                    _activeFilterChipBarGO.transform.SetAsLastSibling();
            }
            catch { }
        }
    }
}
