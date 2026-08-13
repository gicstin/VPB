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
        // Footer overflow: hide chips one-by-one until packs fit; extreme = only “…”.

        private const float FooterOverflowGapRef = 10f;

        private GameObject _footerOverflowBtnGO;
        private RectTransform _footerOverflowBtnRT;
        private GameObject _footerOverflowMenuGO;
        private bool _footerOverflowOpen;
        private bool _footerOverflowActive;
        private readonly List<GameObject> _footerOverflowCollapsed = new List<GameObject>(20);
        private readonly List<GameObject> _footerOverflowCandidates = new List<GameObject>(20);
        private int _footerOverflowLayoutSig = int.MinValue;

        private void EnsureFooterOverflowChrome(GameObject rightSectionGO)
        {
            if (rightSectionGO == null || _footerOverflowBtnGO != null) return;

            _footerOverflowBtnGO = UI.CreateUIButton(
                rightSectionGO,
                GalleryUiDesignTokens.ButtonSizeRef,
                GalleryUiDesignTokens.ButtonSizeRef,
                "\u2026",
                GalleryUiDesignTokens.TitleBarOverflowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                ToggleFooterOverflowMenu);
            _footerOverflowBtnGO.name = "FooterOverflowBtn";
            _footerOverflowBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            var overflowTxt = _footerOverflowBtnGO.GetComponentInChildren<Text>();
            if (overflowTxt != null) overflowTxt.color = Color.white;
            _footerOverflowBtnRT = _footerOverflowBtnGO.GetComponent<RectTransform>();
            _footerOverflowBtnGO.SetActive(false);
            AddTooltip(_footerOverflowBtnGO, "gallery.footer.overflow", "More footer actions");
            AddHoverDelegate(_footerOverflowBtnGO);

            _footerOverflowMenuGO = UI.CreatePopupMenuRoot(
                backgroundBoxGO != null ? backgroundBoxGO : rightSectionGO,
                "FooterOverflowMenu",
                CloseFooterOverflowMenu);
            _footerOverflowMenuGO.SetActive(false);

            GameObject panel = UI.CreatePopupMenuPanel(
                _footerOverflowMenuGO, "OverflowMenuPanel",
                AnchorPresets.bottomMiddle,
                new Vector2(GalleryUiDesignTokens.OverflowMenuPanelWidthRef, 50f),
                new Vector2(0f, GalleryUiDesignTokens.FooterBarHeightRef
                    + GalleryUiDesignTokens.FooterInfoRowHeightRef
                    + GalleryUiDesignTokens.PopupMenuAnchorGapRef));
            RebuildFooterOverflowMenuRows(panel.transform);
        }

        private bool IsFooterOverflowCollapsed(GameObject go)
        {
            return go != null && _footerOverflowCollapsed != null && _footerOverflowCollapsed.Contains(go);
        }

        /// <summary>
        /// Fixed vs floating footer chrome. Must run after overflow restore so mode-hidden
        /// chips (follow in fixed, dock/height/autohide in floating) stay hidden.
        /// </summary>
        private void ApplyFooterModeButtonVisibility()
        {
            bool fixedMode = isFixedLocally;
            SetFooterOverflowTargetActive(footerFollowAngleBtn, !fixedMode && !IsFooterOverflowCollapsed(footerFollowAngleBtn));
            SetFooterOverflowTargetActive(footerFollowDistanceBtn, !fixedMode && !IsFooterOverflowCollapsed(footerFollowDistanceBtn));
            SetFooterOverflowTargetActive(footerFollowHeightBtn, !fixedMode && !IsFooterOverflowCollapsed(footerFollowHeightBtn));
            SetFooterOverflowTargetActive(footerDockBtn, fixedMode && !IsFooterOverflowCollapsed(footerDockBtn));
            SetFooterOverflowTargetActive(footerHeightBtn, fixedMode && !IsFooterOverflowCollapsed(footerHeightBtn));
            SetFooterOverflowTargetActive(footerAutoHideBtn, fixedMode && !IsFooterOverflowCollapsed(footerAutoHideBtn));
        }

        /// <summary>Hide order: least critical first. Extreme end-state = only “…” (+ resize grips).</summary>
        private void CollectFooterOverflowCandidates(List<GameObject> into)
        {
            into.Clear();
            bool fixedMode = isFixedLocally;
            if (fixedMode)
            {
                if (footerAutoHideBtn != null) into.Add(footerAutoHideBtn);
                if (footerHeightBtn != null) into.Add(footerHeightBtn);
                if (footerDockBtn != null) into.Add(footerDockBtn);
            }
            if (footerLayoutBtn != null) into.Add(footerLayoutBtn);
            if (footerHoldToLaunchToggleBtn != null) into.Add(footerHoldToLaunchToggleBtn);
            bool isVR = false;
            try { isVR = XrUtils.IsVrActive(); } catch { }
            if (isVR && footerWatchToggleBtn != null) into.Add(footerWatchToggleBtn);
            if (footerMenuGateBtn != null) into.Add(footerMenuGateBtn);
            if (!fixedMode)
            {
                if (footerFollowHeightBtn != null) into.Add(footerFollowHeightBtn);
                if (footerFollowDistanceBtn != null) into.Add(footerFollowDistanceBtn);
                if (footerFollowAngleBtn != null) into.Add(footerFollowAngleBtn);
            }
            if (gridSizePlusBtn != null) into.Add(gridSizePlusBtn);
            if (gridSizeMinusBtn != null) into.Add(gridSizeMinusBtn);
            if (footerPerfPlusBtn != null) into.Add(footerPerfPlusBtn);
            if (footerPerfMinusBtn != null) into.Add(footerPerfMinusBtn);
            if (footerPerfToggleBtn != null) into.Add(footerPerfToggleBtn);
            if (footerHubBtnGO != null) into.Add(footerHubBtnGO);
            if (footerCommandPaletteBtnGO != null) into.Add(footerCommandPaletteBtnGO);
            // Redo may collapse; Undo stays pinned (never added) for recovery Fitts path.
            if (footerRedoBtnGO != null) into.Add(footerRedoBtnGO);
        }

        private int ComputeFooterOverflowLayoutSig(float footerW, float s)
        {
            int sig = (Mathf.RoundToInt(footerW) * 397) ^ (Mathf.RoundToInt(s * 100f) * 17);
            if (footerBackBtn != null && footerBackBtn.activeSelf) sig ^= 1 << 1;
            if (footerClearFilterBtn != null && footerClearFilterBtn.activeSelf) sig ^= 1 << 2;
            if (footerFilterModeText != null && footerFilterModeText.gameObject != null && footerFilterModeText.gameObject.activeSelf)
                sig ^= 1 << 3;
            try { if (XrUtils.IsVrActive()) sig ^= 1 << 4; } catch { }
            if (_resizeHandleFixedBottomGO != null && _resizeHandleFixedBottomGO.activeSelf) sig ^= 1 << 5;
            if (_resizeHandleFixedBottomRightGO != null && _resizeHandleFixedBottomRightGO.activeSelf) sig ^= 1 << 6;
            if (_resizeHandleBottomLeftGO != null && _resizeHandleBottomLeftGO.activeSelf) sig ^= 1 << 7;
            if (_resizeHandleBottomRightGO != null && _resizeHandleBottomRightGO.activeSelf) sig ^= 1 << 8;
            if (IsFixedTopDockMode() && !isCollapsed) sig ^= 1 << 9;
            if (isFixedLocally) sig ^= 1 << 10;
            if (isCollapsed) sig ^= 1 << 11;
            return sig;
        }

        /// <summary>Force next <see cref="ApplyFooterOverflowLayout"/> to remeasure (mode/dock chrome change).</summary>
        private void InvalidateFooterOverflowLayout()
        {
            _footerOverflowLayoutSig = int.MinValue;
        }

        private static float FooterChipPackWidth(int count, float chip, float gap)
        {
            if (count <= 0) return 0f;
            return count * chip + (count - 1) * gap;
        }

        private float EstimateFooterCenterChromeWidthWithoutSideButtons(float chip, float gap, float s)
        {
            int perfN = 0;
            if (footerPerfToggleBtn != null && footerPerfToggleBtn.activeSelf) perfN++;
            if (footerPerfMinusBtn != null && footerPerfMinusBtn.activeSelf) perfN++;
            if (footerPerfPlusBtn != null && footerPerfPlusBtn.activeSelf) perfN++;
            float w = FooterChipPackWidth(perfN, chip, gap);
            if (footerBackBtn != null && footerBackBtn.activeSelf) w += (w > 0f ? gap : 0f) + chip;
            if (footerClearFilterBtn != null && footerClearFilterBtn.activeSelf) w += gap + chip;
            if (footerFilterModeText != null && footerFilterModeText.gameObject != null && footerFilterModeText.gameObject.activeSelf)
                w += gap + 180f * s;
            if (footerFilterModeSpacerGO != null && footerFilterModeSpacerGO.activeSelf)
                w += 12f * s;
            return w;
        }

        private float EstimateFooterCenterMinWidth(float chip, float gap, float s)
        {
            float w = EstimateFooterCenterChromeWidthWithoutSideButtons(chip, gap, s);
            // Top dock: side strip + quality share CenterSection — reserve both (not Max).
            if (IsFixedTopDockMode() && !isCollapsed && _footerSideButtonsGroupRT != null && _footerSideButtonsGroupGO != null
                && _footerSideButtonsGroupGO.activeSelf)
            {
                float sideW = _footerSideButtonsGroupRT.rect.width;
                if (sideW < 1f) sideW = _footerSideButtonsGroupRT.sizeDelta.x;
                if (sideW > 1f) w += (w > 0f ? gap : 0f) + sideW;
            }
            return w;
        }

        private float MeasureFooterSectionPreferredWidth(RectTransform sectionRT, float s)
        {
            if (sectionRT == null) return 0f;
            HorizontalLayoutGroup hlg = sectionRT.GetComponent<HorizontalLayoutGroup>();
            float gap = hlg != null ? hlg.spacing : FooterOverflowGapRef * s;
            float pad = hlg != null ? (hlg.padding.left + hlg.padding.right) : 0f;
            float chipFallback = GalleryUiDesignTokens.ButtonSizeRef * s;
            float w = pad;
            int n = 0;
            for (int i = 0; i < sectionRT.childCount; i++)
            {
                RectTransform ch = sectionRT.GetChild(i) as RectTransform;
                if (ch == null || !ch.gameObject.activeSelf) continue;
                // Nested perf group: measure its children pack.
                if (ch.name == "FooterPerfGroup")
                {
                    float nested = MeasureFooterSectionPreferredWidth(ch, s);
                    if (nested <= 0f) continue;
                    if (n > 0) w += gap;
                    w += nested;
                    n++;
                    continue;
                }
                // Top-dock side strip: counted via EstimateFooterCenterMinWidth — skip if under CenterSection.
                if (ch.name == "SideButtonsGroup")
                    continue;
                float cw = ch.sizeDelta.x;
                if (cw < 1f) cw = ch.rect.width;
                if (cw < 1f) cw = chipFallback;
                if (n > 0) w += gap;
                w += cw;
                n++;
            }
            return w;
        }

        private bool FooterRightPackNeedsMoreCollapse(float s)
        {
            if (paginationRT == null || _footerLeftSectionRT == null || _footerRightSectionRT == null)
                return false;

            float padL = 10f * s;
            float padR = 10f * s;
            if (footerHLG != null)
            {
                padL = footerHLG.padding.left;
                padR = footerHLG.padding.right;
            }

            float chip = GalleryUiDesignTokens.ButtonSizeRef * s;
            float gap = FooterOverflowGapRef * s;
            float centerMin = EstimateFooterCenterMinWidth(chip, gap, s);
            float contentW = paginationRT.rect.width - padL - padR;
            if (contentW < 8f) return false;

            float leftW = MeasureFooterSectionPreferredWidth(_footerLeftSectionRT, s);
            float rightW = MeasureFooterSectionPreferredWidth(_footerRightSectionRT, s);
            return leftW + rightW + centerMin > contentW - gap;
        }

        private void RebuildFooterOverflowMenuRows(Transform panel)
        {
            if (panel == null) return;
            UI.DestroyAllChildren(panel);

            for (int i = 0; i < _footerOverflowCollapsed.Count; i++)
            {
                GameObject go = _footerOverflowCollapsed[i];
                if (go == null) continue;
                AddFooterOverflowRowForButton(panel, go);
            }
        }

        private void AddFooterOverflowRowForButton(Transform panel, GameObject go)
        {
            Sprite icon = UI.GetButtonIconSprite(go);

            if (go == footerFollowAngleBtn)
            {
                bool on = VPBConfig.Instance != null && VPBConfig.Instance.FollowAngle != "Off";
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_follow_angle", "Follow Angle"),
                    () => { CloseFooterOverflowMenu(); ToggleFollowQuick("Angle"); }, on, icon: icon,
                    tipKey: "gallery.tooltip.follow_angle", tipDefault: "Follow Angle");
            }
            else if (go == footerFollowDistanceBtn)
            {
                bool on = VPBConfig.Instance != null && VPBConfig.Instance.FollowDistance != "Off";
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_follow_distance", "Follow Distance"),
                    () => { CloseFooterOverflowMenu(); ToggleFollowQuick("Distance"); }, on, icon: icon,
                    tipKey: "gallery.tooltip.follow_distance", tipDefault: "Follow Distance");
            }
            else if (go == footerFollowHeightBtn)
            {
                bool on = VPBConfig.Instance != null && VPBConfig.Instance.FollowEyeHeight != "Off";
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_follow_height", "Follow Eye Height"),
                    () => { CloseFooterOverflowMenu(); ToggleFollowQuick("Height"); }, on, icon: icon,
                    tipKey: "gallery.tooltip.follow_eye_height", tipDefault: "Follow Eye Height");
            }
            else if (go == footerMenuGateBtn)
            {
                bool on = VPBConfig.Instance != null && VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible;
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_menu_gate", "VaM menu gate"),
                    () => { CloseFooterOverflowMenu(); ToggleVamMenuGateMode(); }, on, icon: icon,
                    tipKey: "gallery.tooltip.vam_menu_gate", tipDefault: "Show only when VaM menu is visible");
            }
            else if (go == footerWatchToggleBtn)
            {
                bool on = VPBConfig.Instance != null && VPBConfig.Instance.QuickMenuVrWatchVisible;
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_vr_watch", "VR wrist watch"),
                    () => { CloseFooterOverflowMenu(); ToggleVrWatchVisible(); }, on, icon: icon,
                    tipKey: "gallery.tooltip.vr_watch_toggle", tipDefault: "Show/hide VR wrist watch (look at inner wrist)");
            }
            else if (go == footerHoldToLaunchToggleBtn)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_hold", "Hold to launch"),
                    () => { CloseFooterOverflowMenu(); ToggleHoldToLaunch(); }, holdToLaunchEnabled, icon: icon,
                    tipKey: "gallery.tooltip.hold_to_launch_toggle",
                    tipDefault: "Hold trigger/button on item to apply/launch (see Settings for duration)");
            }
            else if (go == footerLayoutBtn)
            {
                bool listLayout = layoutMode == GalleryLayoutMode.List;
                bool blockLayout = false;
                try { blockLayout = settingsListViewActive; } catch { }
                AddFooterOverflowMenuRow(
                    panel,
                    listLayout
                        ? VPBTranslation.T("gallery.footer.overflow_layout_grid", "Grid layout")
                        : VPBTranslation.T("gallery.footer.overflow_layout_list", "List layout"),
                    () => { CloseFooterOverflowMenu(); ToggleLayoutMode(); },
                    listLayout,
                    enabled: !blockLayout,
                    icon: icon,
                    tipKey: "gallery.tooltip.layout_mode",
                    tipDefault: "Toggle grid vs list layout");
            }
            else if (go == footerDockBtn)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_dock", "Dock side"),
                    () => { CloseFooterOverflowMenu(); CycleDesktopFixedDockSide(); }, icon: icon,
                    tipKey: "gallery.tooltip.dock_side", tipDefault: "Dock side (Left/Right/Top)");
            }
            else if (go == footerHeightBtn)
            {
                bool fixedHeight = VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedHeightMode > 0;
                AddFooterOverflowMenuRow(
                    panel,
                    fixedHeight
                        ? VPBTranslation.T("gallery.footer.overflow_height_free", "Adjustable height")
                        : VPBTranslation.T("gallery.footer.overflow_height_fixed", "Full height"),
                    () => { CloseFooterOverflowMenu(); ToggleFixedHeightMode(); },
                    fixedHeight,
                    icon: icon,
                    tipKey: "gallery.tooltip.height_mode",
                    tipDefault: "Toggle full-height vs adjustable height");
            }
            else if (go == footerAutoHideBtn)
            {
                bool autoHide = VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedAutoCollapse;
                AddFooterOverflowMenuRow(
                    panel,
                    autoHide
                        ? VPBTranslation.T("gallery.tooltip.autohide_enabled", "Auto-Hide (Enabled)")
                        : VPBTranslation.T("gallery.tooltip.autohide_disabled", "Auto-Hide (Disabled)"),
                    () => { CloseFooterOverflowMenu(); ToggleAutoHideMode(); },
                    autoHide,
                    icon: icon,
                    tipKey: "gallery.tooltip.autohide",
                    tipDefault: "Auto-hide docked panel when pointer leaves");
            }
            else if (go == gridSizeMinusBtn)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_grid_minus", "Fewer columns"),
                    () => { CloseFooterOverflowMenu(); AdjustGridColumns(1); }, icon: icon,
                    tipKey: "gallery.tooltip.grid_minus", tipDefault: "Decrease columns (Ctrl+scroll wheel over gallery)");
            }
            else if (go == gridSizePlusBtn)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_grid_plus", "More columns"),
                    () => { CloseFooterOverflowMenu(); AdjustGridColumns(-1); }, icon: icon,
                    tipKey: "gallery.tooltip.grid_plus", tipDefault: "Increase columns (Ctrl+scroll wheel over gallery)");
            }
            else if (go == footerPerfMinusBtn)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_perf_down", "Quality down"),
                    () => { CloseFooterOverflowMenu(); FooterPerfStepDown(); }, icon: icon,
                    tipKey: "gallery.footer.overflow_perf_down", tipDefault: "Lower quality preset");
            }
            else if (go == footerPerfPlusBtn)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_perf_up", "Quality up"),
                    () => { CloseFooterOverflowMenu(); FooterPerfStepUp(); }, icon: icon,
                    tipKey: "gallery.footer.overflow_perf_up", tipDefault: "Raise quality preset");
            }
            else if (go == footerPerfToggleBtn)
            {
                string label = footerPerfToggleBtnText != null && !string.IsNullOrEmpty(footerPerfToggleBtnText.text)
                    ? footerPerfToggleBtnText.text
                    : VPBTranslation.T("gallery.footer.overflow_perf", "Quality");
                AddFooterOverflowMenuRow(panel, label,
                    () => { CloseFooterOverflowMenu(); ToggleFooterPerfMode(); }, icon: icon,
                    tipKey: "gallery.footer.overflow_perf", tipDefault: "Cycle quality preset");
            }
            else if (go == footerHubBtnGO)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_hub", "Hub"),
                    () =>
                    {
                        CloseFooterOverflowMenu();
                        VamHookPlugin.singleton?.OpenHubBrowse();
                        Hide();
                    }, icon: icon,
                    tipKey: "gallery.tooltip.hub", tipDefault: "Hub browse / dev Hub panel");
            }
            else if (go == footerCommandPaletteBtnGO)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_command_palette", "Command palette"),
                    () => { CloseFooterOverflowMenu(); ToggleCommandPalette(); }, icon: icon,
                    tipKey: "gallery.tooltip.command_palette", tipDefault: "Command palette (Ctrl+Shift+P)");
            }
            else if (go == footerUndoBtnGO)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_undo", "Undo"),
                    () => { CloseFooterOverflowMenu(); Undo(); }, icon: icon,
                    tipKey: "gallery.tooltip.undo", tipDefault: "Undo last change (Ctrl+Z)");
            }
            else if (go == footerRedoBtnGO)
            {
                AddFooterOverflowMenuRow(panel, VPBTranslation.T("gallery.footer.overflow_redo", "Redo"),
                    () => { CloseFooterOverflowMenu(); Redo(); }, icon: icon,
                    tipKey: "gallery.tooltip.redo", tipDefault: "Redo (Ctrl+Y / Ctrl+Shift+Z)");
            }
        }

        private void AddFooterOverflowMenuRow(
            Transform panel, string label, UnityAction onClick, bool active = false, bool enabled = true, Sprite icon = null,
            string tipKey = null, string tipDefault = null)
        {
            GameObject row = UI.AddStretchPopupMenuRow(panel, label, onClick, active, enabled, icon: icon);
            if (row == null) return;
            if (!string.IsNullOrEmpty(tipKey))
                AddTooltip(row, tipKey, tipDefault ?? label);
            else if (!string.IsNullOrEmpty(tipDefault))
                AddTooltipPlain(row, tipDefault);
        }

        private void ToggleFooterOverflowMenu()
        {
            if (_footerOverflowMenuGO == null) return;
            _footerOverflowOpen = !_footerOverflowOpen;
            if (_footerOverflowOpen)
            {
                Transform panel = _footerOverflowMenuGO.transform.Find("OverflowMenuPanel");
                if (panel != null) RebuildFooterOverflowMenuRows(panel);
                try { RescaleFooterOverflowMenuInternal(ChromeScale); } catch { }
                _footerOverflowMenuGO.transform.SetAsLastSibling();
            }
            _footerOverflowMenuGO.SetActive(_footerOverflowOpen);
        }

        private void CloseFooterOverflowMenu()
        {
            _footerOverflowOpen = false;
            if (_footerOverflowMenuGO != null) _footerOverflowMenuGO.SetActive(false);
        }

        private void PositionFooterOverflowMenuPanel(RectTransform panelRT)
        {
            if (panelRT == null || _footerOverflowBtnRT == null || _footerOverflowMenuGO == null) return;
            RectTransform overlayRT = _footerOverflowMenuGO.GetComponent<RectTransform>();
            if (overlayRT == null) return;

            float s = ChromeScale <= 0f ? 1f : ChromeScale;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;

            // X from … button; Y clears tooltip/info bar (hoverPath) so status text stays readable.
            Vector3 worldX = _footerOverflowBtnRT.TransformPoint(_footerOverflowBtnRT.rect.center);
            float localX = overlayRT.InverseTransformPoint(worldX).x;
            float localY;
            if (hoverPathRT != null && hoverPathRT.gameObject.activeInHierarchy)
            {
                Vector3 worldInfoTop = hoverPathRT.TransformPoint(new Vector3(
                    hoverPathRT.rect.center.x,
                    hoverPathRT.rect.yMax,
                    0f));
                localY = overlayRT.InverseTransformPoint(worldInfoTop).y + gap;
            }
            else
            {
                Vector3 worldBtnTop = _footerOverflowBtnRT.TransformPoint(new Vector3(
                    _footerOverflowBtnRT.rect.center.x,
                    _footerOverflowBtnRT.rect.yMax,
                    0f));
                localY = overlayRT.InverseTransformPoint(worldBtnTop).y
                    + GalleryUiDesignTokens.FooterInfoRowHeightRef * s
                    + gap;
            }

            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0f);
            panelRT.anchoredPosition = new Vector2(localX, localY);
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
        }

        private void RescaleFooterOverflowMenuInternal(float s)
        {
            if (_footerOverflowMenuGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = _footerOverflowMenuGO.transform.Find("OverflowMenuPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(
                panel.gameObject,
                s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuOverflowFontRef,
                GalleryUiDesignTokens.OverflowMenuPanelWidthRef);
            if (_footerOverflowOpen)
                PositionFooterOverflowMenuPanel(panel as RectTransform);
        }

        private void ApplyFooterOverflowLayout(float paneScale)
        {
            if (paginationRT == null || _footerOverflowBtnGO == null) return;
            float s = paneScale <= 0f ? 1f : paneScale;
            float footerW = paginationRT.rect.width;
            if (footerW < 8f) return;

            int sig = ComputeFooterOverflowLayoutSig(footerW, s);
            // Hard early-out: width/scale/visibility unchanged — skip Collect + Collapse every visible frame.
            // Note: undo/redo label text does not change chip size (fixed ButtonSizeRef), so omit from sig.
            if (sig == _footerOverflowLayoutSig)
                return;

            CollectFooterOverflowCandidates(_footerOverflowCandidates);
            _footerOverflowLayoutSig = sig;

            for (int i = 0; i < _footerOverflowCandidates.Count; i++)
                SetFooterOverflowTargetActive(_footerOverflowCandidates[i], true);

            _footerOverflowCollapsed.Clear();
            if (_footerOverflowBtnGO != null)
                _footerOverflowBtnGO.SetActive(false);

            CollapseFooterOverflowUntilFits(s);

            if (!_footerOverflowActive)
                CloseFooterOverflowMenu();
            else if (_footerOverflowOpen)
            {
                Transform panel = _footerOverflowMenuGO != null
                    ? _footerOverflowMenuGO.transform.Find("OverflowMenuPanel")
                    : null;
                if (panel != null)
                {
                    RebuildFooterOverflowMenuRows(panel);
                    try { RescaleFooterOverflowMenuInternal(s); } catch { }
                }
            }
        }

        private void CollapseFooterOverflowUntilFits(float s)
        {
            while (true)
            {
                bool showOverflow = _footerOverflowCollapsed.Count > 0;
                if (_footerOverflowBtnGO != null && _footerOverflowBtnGO.activeSelf != showOverflow)
                    _footerOverflowBtnGO.SetActive(showOverflow);

                if (!FooterRightPackNeedsMoreCollapse(s))
                    break;

                GameObject hide = null;
                for (int i = 0; i < _footerOverflowCandidates.Count; i++)
                {
                    GameObject c = _footerOverflowCandidates[i];
                    if (c == null || !c.activeSelf) continue;
                    if (_footerOverflowCollapsed.Contains(c)) continue;
                    hide = c;
                    break;
                }
                if (hide == null) break;

                SetFooterOverflowTargetActive(hide, false);
                _footerOverflowCollapsed.Add(hide);
            }

            _footerOverflowActive = _footerOverflowCollapsed.Count > 0;
            if (_footerOverflowBtnGO != null)
                _footerOverflowBtnGO.SetActive(_footerOverflowActive);

            ApplyFooterModeButtonVisibility();
        }

        private static void SetFooterOverflowTargetActive(GameObject go, bool active)
        {
            if (go == null) return;
            if (go.activeSelf != active)
                go.SetActive(active);
        }
    }
}
