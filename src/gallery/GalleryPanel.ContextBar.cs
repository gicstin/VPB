using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Context Bar coordination: filter chip row + +N overflow popup.
    /// Warm/cold only — reused lists, no per-frame alloc.
    /// </summary>
    public partial class GalleryPanel
    {
        private readonly List<ActiveFilterChipSpec> _filterChipSpecScratch = new List<ActiveFilterChipSpec>(16);
        private readonly List<ActiveFilterChipSpec> _filterChipOverflowSpecs = new List<ActiveFilterChipSpec>(12);
        private readonly List<int> _filterChipPackBodyIdx = new List<int>(16);

        private GameObject _filterChipOverflowMenuGO;
        private bool _filterChipOverflowOpen;
        private RectTransform _filterChipOverflowAnchorRT;

        private void InvalidateModeSemanticsBannerCache()
        {
            _modeSemanticsBannerCacheKey = null;
        }

        // ── Filter chip +N overflow popup ───────────────────────────────────

        private void EnsureFilterChipOverflowMenu()
        {
            if (_filterChipOverflowMenuGO != null || backgroundBoxGO == null) return;

            _filterChipOverflowMenuGO = UI.CreatePopupMenuRoot(
                backgroundBoxGO,
                "FilterChipOverflowMenu",
                CloseFilterChipOverflowMenu);
            _filterChipOverflowMenuGO.SetActive(false);

            UI.CreatePopupMenuPanel(
                _filterChipOverflowMenuGO,
                "OverflowMenuPanel",
                AnchorPresets.topMiddle,
                new Vector2(GalleryUiDesignTokens.OverflowMenuPanelWidthRef, 50f),
                Vector2.zero);
        }

        private void ToggleFilterChipOverflowMenu()
        {
            if (_filterChipOverflowOpen)
            {
                CloseFilterChipOverflowMenu();
                return;
            }
            OpenFilterChipOverflowMenu();
        }

        private void OpenFilterChipOverflowMenu()
        {
            if (_filterChipOverflowSpecs.Count == 0) return;
            EnsureFilterChipOverflowMenu();
            if (_filterChipOverflowMenuGO == null) return;

            Transform panel = _filterChipOverflowMenuGO.transform.Find("OverflowMenuPanel");
            if (panel != null)
                RebuildFilterChipOverflowMenuRows(panel);

            _filterChipOverflowOpen = true;
            _filterChipOverflowMenuGO.SetActive(true);
            try { _filterChipOverflowMenuGO.transform.SetAsLastSibling(); } catch { }
            try { PositionFilterChipOverflowMenuPanel(); } catch { }
        }

        private void CloseFilterChipOverflowMenu()
        {
            _filterChipOverflowOpen = false;
            if (_filterChipOverflowMenuGO != null)
                _filterChipOverflowMenuGO.SetActive(false);
        }

        private bool TryHandleFilterChipOverflowEsc()
        {
            if (!_filterChipOverflowOpen) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            CloseFilterChipOverflowMenu();
            return true;
        }

        private void RebuildFilterChipOverflowMenuRows(Transform panel)
        {
            if (panel == null) return;
            UI.DestroyAllChildren(panel);

            for (int i = 0; i < _filterChipOverflowSpecs.Count; i++)
            {
                ActiveFilterChipSpec spec = _filterChipOverflowSpecs[i];
                string label = spec.Label ?? "";
                UnityAction dismiss = spec.OnDismiss;
                UI.AddStretchPopupMenuRow(
                    panel,
                    label,
                    () =>
                    {
                        CloseFilterChipOverflowMenu();
                        try { if (dismiss != null) dismiss(); } catch { }
                    },
                    isActive: false,
                    enabled: dismiss != null);
            }
        }

        private void PositionFilterChipOverflowMenuPanel()
        {
            if (_filterChipOverflowMenuGO == null || !_filterChipOverflowOpen) return;
            Transform panelT = _filterChipOverflowMenuGO.transform.Find("OverflowMenuPanel");
            RectTransform panelRT = panelT != null ? panelT.GetComponent<RectTransform>() : null;
            if (panelRT == null) return;

            RectTransform overlayRT = _filterChipOverflowMenuGO.GetComponent<RectTransform>();
            if (overlayRT == null) return;

            float s = ChromeScale <= 0f ? 1f : ChromeScale;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;

            RectTransform anchor = _filterChipOverflowAnchorRT;
            if (anchor == null && _activeFilterChipBarGO != null)
                anchor = _activeFilterChipBarGO.GetComponent<RectTransform>();
            if (anchor == null) return;

            Vector3 world = anchor.TransformPoint(new Vector3(
                anchor.rect.xMax,
                anchor.rect.yMin,
                0f));
            Vector3 local = overlayRT.InverseTransformPoint(world);

            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(1f, 1f);
            panelRT.anchoredPosition = new Vector2(local.x, local.y - gap);
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
        }

        /// <summary>Approx compact chip width for one-row pack reserve (warm path).</summary>
        private static float EstimateCompactFilterChipWidth(string label, float s, int fontSize)
        {
            float pad = 20f * s;
            float charW = fontSize * 0.58f;
            int len = string.IsNullOrEmpty(label) ? 1 : label.Length;
            return pad + len * charW;
        }

        /// <summary>Approx standard (label + dismiss) chip width for one-row pack.</summary>
        private static float EstimateStandardFilterChipWidth(string label, float s, int fontSize, float chipH)
        {
            float padLeft = 10f * s;
            float dismiss = Mathf.Max(16f, chipH - 4f * s);
            float gap = GalleryUiDesignTokens.FilterChipLabelDismissGapRef * s;
            float charW = fontSize * 0.55f;
            int len = string.IsNullOrEmpty(label) ? 1 : label.Length;
            return padLeft + len * charW + gap + dismiss;
        }

        private float EstimateFilterChipSpecWidth(ActiveFilterChipSpec spec, float s, int fontSize, float chipH)
        {
            if (IsCompactFilterChipKind(spec.Kind))
                return EstimateCompactFilterChipWidth(spec.Label, s, fontSize);
            return EstimateStandardFilterChipWidth(spec.Label, s, fontSize, chipH);
        }

        /// <summary>
        /// Pack chips into one Context Bar row: Back · body… · +N · Clear all.
        /// Overflow specs kept for popup. Sets <see cref="_activeFilterChipRowCount"/> = 1.
        /// </summary>
        private void PackActiveFilterChipsOneRow(
            List<ActiveFilterChipSpec> specs,
            float s,
            int fontSize,
            float chipH)
        {
            ReturnActiveFilterChipsToPool();
            _filterChipOverflowSpecs.Clear();
            CloseFilterChipOverflowMenu();
            _filterChipOverflowAnchorRT = null;

            if (specs == null || specs.Count == 0 || _activeFilterChipScrollContentRT == null)
            {
                _activeFilterChipRowCount = 1;
                return;
            }

            float availW = _lastChipBarAvailWidth;
            if (availW <= 1f) availW = _activeFilterChipScrollContentRT.rect.width;
            if (availW <= 1f) availW = float.MaxValue;

            float colSpacing = FilterChipColumnSpacingRef * s;

            int backIdx = -1;
            int clearAllIdx = -1;
            _filterChipPackBodyIdx.Clear();
            for (int i = 0; i < specs.Count; i++)
            {
                FilterChipKind k = specs[i].Kind;
                if (k == FilterChipKind.PackageFilterBack)
                {
                    if (backIdx < 0) backIdx = i;
                    continue;
                }
                if (k == FilterChipKind.ClearAll)
                {
                    clearAllIdx = i;
                    continue;
                }
                if (k == FilterChipKind.Overflow)
                    continue;
                _filterChipPackBodyIdx.Add(i);
            }

            bool hasClearAll = clearAllIdx >= 0;
            float clearAllW = 0f;
            if (hasClearAll)
                clearAllW = EstimateFilterChipSpecWidth(specs[clearAllIdx], s, fontSize, chipH);

            string overflowProbe = string.Format(
                VPBTranslation.T("gallery.filter_chip.overflow_fmt", "+{0}"),
                99);
            float overflowUnitW = EstimateCompactFilterChipWidth(overflowProbe, s, fontSize);

            float x = 0f;

            if (backIdx >= 0)
            {
                GameObject backChip = AcquireFilterChipControl(
                    _activeFilterChipScrollContentRT, specs[backIdx], chipH, fontSize, s);
                if (backChip != null)
                {
                    _activeFilterChipButtons.Add(backChip);
                    float bw = MeasureFilterChipPreferredWidth(backChip);
                    if (bw <= 1f)
                        bw = EstimateFilterChipSpecWidth(specs[backIdx], s, fontSize, chipH);
                    x = bw + colSpacing;
                }
            }

            int bodyCount = _filterChipPackBodyIdx.Count;
            int shownBody = 0;
            for (int bi = 0; bi < bodyCount; bi++)
            {
                int specIdx = _filterChipPackBodyIdx[bi];
                ActiveFilterChipSpec spec = specs[specIdx];
                float estW = EstimateFilterChipSpecWidth(spec, s, fontSize, chipH);

                int remainAfter = bodyCount - (bi + 1);
                bool needOverflowAfter = remainAfter > 0;
                float trailing = 0f;
                if (needOverflowAfter)
                    trailing += overflowUnitW + colSpacing;
                if (hasClearAll)
                    trailing += clearAllW + colSpacing;

                if (x > 0.5f && x + estW + trailing > availW + 0.5f)
                {
                    // This chip and the rest → overflow popup.
                    for (int oj = bi; oj < bodyCount; oj++)
                        _filterChipOverflowSpecs.Add(specs[_filterChipPackBodyIdx[oj]]);
                    break;
                }

                GameObject chip = AcquireFilterChipControl(
                    _activeFilterChipScrollContentRT, spec, chipH, fontSize, s);
                if (chip == null)
                {
                    _filterChipOverflowSpecs.Add(spec);
                    continue;
                }

                float w = MeasureFilterChipPreferredWidth(chip);
                if (w <= 1f) w = estW;

                // Re-check with measured width (estimate may have been optimistic).
                if (x > 0.5f && x + w + trailing > availW + 0.5f)
                {
                    try { ReturnFilterChipToPool(chip); } catch { }
                    for (int oj = bi; oj < bodyCount; oj++)
                        _filterChipOverflowSpecs.Add(specs[_filterChipPackBodyIdx[oj]]);
                    break;
                }

                _activeFilterChipButtons.Add(chip);
                x += w + colSpacing;
                shownBody++;
            }

            if (_filterChipOverflowSpecs.Count > 0)
            {
                int overN = _filterChipOverflowSpecs.Count;
                ActiveFilterChipSpec overSpec = new ActiveFilterChipSpec
                {
                    Label = string.Format(
                        VPBTranslation.T("gallery.filter_chip.overflow_fmt", "+{0}"),
                        overN),
                    Kind = FilterChipKind.Overflow,
                    OnDismiss = ToggleFilterChipOverflowMenu
                };
                GameObject overChip = AcquireFilterChipControl(
                    _activeFilterChipScrollContentRT, overSpec, chipH, fontSize, s);
                if (overChip != null)
                {
                    _activeFilterChipButtons.Add(overChip);
                    _filterChipOverflowAnchorRT = overChip.GetComponent<RectTransform>();
                    float ow = MeasureFilterChipPreferredWidth(overChip);
                    if (ow <= 1f) ow = overflowUnitW;
                    x += ow + colSpacing;
                }
            }

            if (hasClearAll)
            {
                GameObject clearChip = AcquireFilterChipControl(
                    _activeFilterChipScrollContentRT, specs[clearAllIdx], chipH, fontSize, s);
                if (clearChip != null)
                    _activeFilterChipButtons.Add(clearChip);
            }

            FlowActiveFilterChips(s);
            // Hard cap inset — Context Bar never grows past one filter row.
            _activeFilterChipRowCount = 1;
        }

        private static float MeasureFilterChipPreferredWidth(GameObject chip)
        {
            if (chip == null) return 0f;
            RectTransform rt = chip.GetComponent<RectTransform>();
            if (rt == null) return 0f;
            try { LayoutRebuilder.ForceRebuildLayoutImmediate(rt); } catch { }
            float w = LayoutUtility.GetPreferredWidth(rt);
            if (w <= 1f) w = rt.rect.width;
            return w;
        }
    }
}
