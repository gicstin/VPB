using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>Resolved chrome scale for this panel instance (pane × host × DPI).</summary>
        internal GalleryUiMetrics UiMetrics => GalleryUiMetrics.ForPanel(this);

        /// <summary>Single layout/font scale factor for gallery chrome on this panel.</summary>
        internal float ChromeScale => UiMetrics.ChromeScale;

        /// <summary>In-app help uses chrome scale with a readability floor.</summary>
        internal float InAppHelpChromeScale => UiMetrics.HelpChromeScale();

        /// <summary>Last HostScale applied via <see cref="ApplyInnerPaneScale"/> (desktop live VaM monitorUIScale sync).</summary>
        private float _lastAppliedHostScale = float.NaN;

        internal bool IsFixedLocallyForUiScale() => isFixedLocally;

        internal float TabScrollTopOffsetPublic() => TabScrollTopOffset();

        internal void ApplyInnerPaneScaleLegacyActions(float chromeScale)
        {
            float s = chromeScale <= 0f ? 1f : chromeScale;
            foreach (var action in innerPaneScaleActions)
            {
                try { action(s); } catch { }
            }
        }

        internal static void SyncSideTabScrollContentVerticalLayoutOn(VerticalLayoutGroup vlg, float s)
        {
            if (vlg == null) return;
            if (s <= 0f) s = 1f;
            int padInner = Mathf.RoundToInt(GalleryUiDesignTokens.SideTabRowPadRef * s);
            vlg.spacing = GalleryUiDesignTokens.SideTabRowSpacingRef * s;
            // Column margin handles outer edge; reserve right pad only for scrollbar gutter.
            vlg.padding = new RectOffset(0, padInner, 0, 0);
        }

        internal static void SyncSideTabListHolderVerticalLayoutOn(VerticalLayoutGroup vlg, float s)
        {
            if (vlg == null) return;
            if (s <= 0f) s = 1f;
            vlg.spacing = GalleryUiDesignTokens.SideTabRowSpacingRef * s;
            vlg.padding = new RectOffset(0, 0, 0, 0);
        }

        internal static void SyncSideTabListVerticalLayoutOn(VerticalLayoutGroup vlg, float s)
        {
            SyncSideTabScrollContentVerticalLayoutOn(vlg, s);
        }

        private void SyncSideTabListVerticalLayout(float s)
        {
            if (s <= 0f) s = 1f;
            SyncSideTabScrollContentVerticalLayoutOn(leftTabContainerGO != null ? leftTabContainerGO.GetComponent<VerticalLayoutGroup>() : null, s);
            SyncSideTabScrollContentVerticalLayoutOn(rightTabContainerGO != null ? rightTabContainerGO.GetComponent<VerticalLayoutGroup>() : null, s);
            SyncSideTabScrollContentVerticalLayoutOn(leftSubTabContainerGO != null ? leftSubTabContainerGO.GetComponent<VerticalLayoutGroup>() : null, s);
            SyncSideTabScrollContentVerticalLayoutOn(rightSubTabContainerGO != null ? rightSubTabContainerGO.GetComponent<VerticalLayoutGroup>() : null, s);
            SyncSideTabListHolderVerticalLayoutOn(leftCategoryTabHolder != null ? leftCategoryTabHolder.GetComponent<VerticalLayoutGroup>() : null, s);
            SyncSideTabListHolderVerticalLayoutOn(rightCategoryTabHolder != null ? rightCategoryTabHolder.GetComponent<VerticalLayoutGroup>() : null, s);
        }

        internal void ApplySideTabFilterRowVerticalLayoutInternal(float chromeScale)
        {
            ApplySideTabFilterRowVerticalLayout(chromeScale);
        }

        internal void ApplyTitleBarResponsiveLayoutInternal(float chromeScale)
        {
            ApplyTitleBarResponsiveLayout(chromeScale);
        }

        internal void ApplyUserTagsStickyScrollChromeInternal(float tabTopOffset)
        {
            ApplyUserTagsStickyScrollChrome(tabTopOffset);
        }

        /// <summary>Re-applies all gallery chrome scaling after settings or host DPI changes.</summary>
        public void ApplyInnerPaneScale()
        {
            GalleryUiMetrics m = UiMetrics;
            float chromeS = m.ChromeScale;
            _lastAppliedHostScale = m.HostScale;
            try { ApplyInnerPaneScaleLegacyActions(chromeS); } catch { }
            try { SyncSideTabColumnHorizontalInsets(chromeS); } catch { }
            try { SyncSideTabListVerticalLayout(chromeS); } catch { }
            try { ApplySideTabFilterRowVerticalLayoutInternal(chromeS); } catch { }
            try { ApplyUserTagsStickyScrollChromeInternal(TabScrollTopOffsetPublic()); } catch { }
            try { RescaleExistingTabButtonsInternal(m); } catch { }
            try { RescaleInitChromeTextsInternal(m); } catch { }
            try { RescaleTitleBarChromeInternal(m); } catch { }
            try { RescaleFooterPerfControlsInternal(m); } catch { }
            try { RescaleAllSearchInputsInternal(m); } catch { }
            try { ApplySideButtonScale(m); } catch { }
            try
            {
                if (IsFixedTopDockMode() && paginationRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(paginationRT);
                Canvas.ForceUpdateCanvases();
            }
            catch { }
            try { if (IsFixedTopDockMode()) ApplyTopDockSideButtonsLayout(chromeS); } catch { }
            try { ApplyCategoryQuickChromeLayout(chromeS); } catch { }
            try { RescalePopupMenusInternal(chromeS); } catch { }
            try { RescaleFooterInfoBarInternal(chromeS); } catch { }
            try { ApplyTitleBarResponsiveLayoutInternal(chromeS); } catch { }
            try { InvalidateFooterOverflowLayout(); } catch { }
            try { ApplyFooterOverflowLayout(chromeS); } catch { }
            try { if (IsFixedTopDockMode()) ApplyTopDockSideButtonsLayout(chromeS); } catch { }
            try { RescaleRemapAtomUidsIfOpen(chromeS); } catch { }
            try { RescaleSettingsFloatIfOpen(chromeS); } catch { }
            try { RescalePluginsFloatIfOpen(chromeS); } catch { }
            try { RescaleCommandPaletteIfOpen(); } catch { }
        }

        /// <summary>Scales hover-path tooltip text, collapsed tbox labels, and detail strip chrome.</summary>
        private void RescaleFooterInfoBarInternal(float s)
        {
            if (s <= 0f) s = 1f;
            // Keep expand-height math on the same scale as chrome (ConfigChanged used to ApplyInnerPaneScale
            // before UpdateLayout, leaving stale tboxTopOffsetBase → crushed/shifted InfoBar).
            tboxTopOffsetBase = GalleryUiDesignTokens.FooterToolboxTopRef * s;
            tboxInfoRowHeight = GalleryUiDesignTokens.FooterInfoRowHeightRef * s;
            // Same global chrome font as rest of gallery UI.
            if (hoverPathText != null)
                GalleryUiMetrics.ApplyFont(hoverPathText, GalleryUiDesignTokens.FooterHoverPathFontRef, s, GalleryUiDesignTokens.FontMinRef);
            if (statusBarText != null)
                GalleryUiMetrics.ApplyFont(statusBarText, GalleryUiDesignTokens.StatusBarFontRef, s, GalleryUiDesignTokens.FontMinRef);
            if (tboxLabel != null)
                GalleryUiMetrics.ApplyFont(tboxLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            try { SyncTboxFooterRowChrome(s); } catch { }
            try { DetailStripRescaleForUiScale(s); } catch { }
            // Recompute InfoBar offsetMax now — wait for Update tick leaves stale height / black band.
            try { UpdateSelectionContextMenu(); } catch { }
        }

        /// <summary>Grid label strip: anchor fraction from config; font is the single gallery chrome font.</summary>
        internal void ApplyGridLabelStripLayout(GameObject btnGO, FileEntry file = null)
        {
            if (btnGO == null || layoutMode == GalleryLayoutMode.List || settingsListViewActive) return;
            Transform gridLabelTr = btnGO.transform.Find("GridLabel");
            if (gridLabelTr == null) return;

            RectTransform cellRt = btnGO.GetComponent<RectTransform>();
            float cellW = cellRt != null && cellRt.rect.width > 1f ? cellRt.rect.width : GalleryUiDesignTokens.GridCellRefSize;
            float cellH = cellRt != null && cellRt.rect.height > 1f ? cellRt.rect.height : GalleryUiDesignTokens.GridCellRefSize;
            float overlay = GalleryUiMetrics.CellOverlayScale(cellW, cellH);

            bool show = VPBConfig.Instance != null && VPBConfig.Instance.GalleryGridLabelsStripVisible();
            gridLabelTr.gameObject.SetActive(show);
            if (!show) return;

            float labelFrac = GetGridLabelFraction();
            RectTransform glRT = gridLabelTr as RectTransform;
            if (glRT != null)
            {
                glRT.anchorMin = new Vector2(0f, 0f);
                glRT.anchorMax = new Vector2(1f, labelFrac);
                glRT.pivot = new Vector2(0.5f, 0f);
                glRT.offsetMin = Vector2.zero;
                glRT.offsetMax = Vector2.zero;
            }

            Transform glTextTr = gridLabelTr.Find("Text");
            if (glTextTr == null) return;
            Text t = glTextTr.GetComponent<Text>();
            RectTransform glTextRT = glTextTr as RectTransform;
            if (t == null) return;

            // Grid labels use the single gallery chrome font, not a per-cell overlay size.
            GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, ChromeScale, GalleryUiDesignTokens.FontMinRef);
            if (glTextRT != null)
            {
                float pad = 2f * overlay;
                glTextRT.offsetMin = new Vector2(pad, 0f);
                glTextRT.offsetMax = new Vector2(-pad, 0f);
            }

            if (file != null)
            {
                string labelText = GetGridItemLabelText(file);
                float availWidth = glRT != null && glRT.rect.width > 1f ? glRT.rect.width : cellW;
                t.text = TruncateGridLabelTextByWidth(t, labelText, availWidth);
            }
        }

        /// <summary>Scales row height + fonts on a vertical popup menu panel.</summary>
        internal static void ScaleVerticalPopupMenuRows(GameObject panelGO, float s, float rowHeightRef, int fontRef, float panelWidthRef = 0f)
        {
            if (panelGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = panelGO.transform;
            VerticalLayoutGroup vlg = panelGO.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                int pad = Mathf.RoundToInt(GalleryUiDesignTokens.PopupMenuPaddingRef * s);
                vlg.padding = new RectOffset(pad, pad, pad, pad);
                vlg.spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
            }
            if (panelWidthRef > 0f)
            {
                RectTransform rt = panelGO.GetComponent<RectTransform>();
                if (rt != null)
                    rt.sizeDelta = new Vector2(panelWidthRef * s, rt.sizeDelta.y);
            }
            float rowH = rowHeightRef * s;
            float iconSz = GalleryUiDesignTokens.PopupMenuRowIconSizeRef * s;
            float iconPad = GalleryUiDesignTokens.PopupMenuRowTextPadXRef * s;
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform ch = panel.GetChild(i);
                if (ch == null) continue;
                LayoutElement le = ch.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = rowH;
                    le.minHeight = rowH;
                }
                float leftExtraRef = 0f;
                Transform iconTr = ch.Find("RowIcon");
                if (iconTr != null)
                {
                    RectTransform irt = iconTr as RectTransform;
                    if (irt != null)
                    {
                        irt.sizeDelta = new Vector2(iconSz, iconSz);
                        irt.anchoredPosition = new Vector2(iconPad, 0f);
                    }
                    leftExtraRef = GalleryUiDesignTokens.PopupMenuRowIconSizeRef + GalleryUiDesignTokens.PopupMenuRowIconGapRef;
                }
                Text t = ch.GetComponentInChildren<Text>(true);
                if (t != null)
                {
                    GalleryUiMetrics.ApplyFont(t, fontRef, s, GalleryUiDesignTokens.FontMinRef);
                    UI.ApplyPopupMenuRowTextPadding(t, s, leftExtraRef);
                }
            }
        }

        private void RescalePopupMenusInternal(float s)
        {
            if (s <= 0f) s = 1f;
            SyncFileSortTypeMenuLayout(s);
            SyncSidePaneSortMenuLayout(s);
            try { ApplyCategoryQuickMenuRowsLayout(s); } catch { }
            try { ApplyTitleCreatorDropdownLayout(s); } catch { }
            try { RescaleTitleBarOverflowMenuInternal(s); } catch { }
            try { RescaleFooterOverflowMenuInternal(s); } catch { }
            try { RescaleSideRailOverflowMenuInternal(s); } catch { }
            try { RescaleLanguageMenuInternal(s); } catch { }
            try { RescaleGlobalSourceFilterMenuInternal(s); } catch { }
            try { quickFiltersUI?.ApplyLayout(s); } catch { }
            try { RescaleSaveMenuPopupInternal(s); } catch { }
            try { DetailStripSyncTagMenuLayout(s); } catch { }
            try { RescaleStripKeepSelectorInternal(s); } catch { }
            if (tboxTargetMenuOpen)
            {
                try { RebuildTboxTargetMenuOptions(); } catch { }
            }
        }

        internal RectTransform QuickFiltersToggleBtnRT => _titleBarQfToggleBtnRT;

        private static void ApplyTitleBarChipScale(RectTransform rt, Text txt, int fontRef, float scale, int minFont = GalleryUiDesignTokens.FontMinRef, bool glyph = false)
        {
            if (rt != null)
            {
                float chip = GalleryUiDesignTokens.TitleBarChipRef * scale;
                rt.sizeDelta = new Vector2(chip, chip);
            }
            if (txt == null) return;
            if (glyph)
                GalleryUiMetrics.ApplyGlyphFont(txt, GalleryUiDesignTokens.TitleBarChipRef, scale, minFont);
            else if (fontRef > 0)
                GalleryUiMetrics.ApplyFont(txt, fontRef, scale, minFont);
        }

        internal void RescaleTitleBarChromeInternal(GalleryUiMetrics metrics)
        {
            float s = metrics.ChromeScale;

            ApplyTitleBarChipScale(_titleBarSettingsBtnRT, titleBarSettingsBtnText, GalleryUiDesignTokens.TitleBarChipFontRef, s);
            ApplyTitleBarChipScale(_titleBarQfToggleBtnRT, quickFiltersToggleBtnText, GalleryUiDesignTokens.TitleBarChipFontRef, s);
            ApplyTitleBarChipScale(_titleBarRatingSortToggleBtnRT, ratingSortToggleBtnText, GalleryUiDesignTokens.TitleBarRatingFontRef, s);
            ApplyTitleBarChipScale(_titleBarRefreshBtnRT, titleBarRefreshBtnText, GalleryUiDesignTokens.TitleBarRefreshFontRef, s);
            ApplyTitleBarChipScale(_titleBarFileSortTypeBtnRT, fileSortTypeText, GalleryUiDesignTokens.TitleBarChipFontRef, s);
            ApplyTitleBarChipScale(_titleBarFileSortDirBtnRT, fileSortDirText, GalleryUiDesignTokens.TitleBarChipFontRef, s);

            if (titleCreatorBtn != null)
                ApplyTitleBarChipScale(titleCreatorBtn.GetComponent<RectTransform>(), titleCreatorBtnText, GalleryUiDesignTokens.TitleBarChipFontRef, s);

            if (languageSwitcherBtnGO != null)
            {
                ApplyTitleBarChipScale(languageSwitcherBtnGO.GetComponent<RectTransform>(), _langBtnText, GalleryUiDesignTokens.TitleBarChipFontRef, s);
                if (_langBtnText != null)
                    _langBtnText.resizeTextForBestFit = false;
            }

            if (_titleSearchCompactRT != null)
            {
                float compactSz = GalleryUiDesignTokens.TitleBarChipRef * s;
                _titleSearchCompactRT.sizeDelta = new Vector2(compactSz, compactSz);
            }

            ApplyTitleBarChipScale(_titleBarHelpBtnRT, _titleBarHelpBtnRT != null ? _titleBarHelpBtnRT.GetComponentInChildren<Text>() : null, GalleryUiDesignTokens.TitleBarHelpFontRef, s);
            ApplyTitleBarChipScale(_titleBarOverflowBtnRT, _titleBarOverflowBtnGO != null ? _titleBarOverflowBtnGO.GetComponentInChildren<Text>() : null, GalleryUiDesignTokens.TitleBarOverflowFontRef, s);
            ApplyTitleBarChipScale(_titleBarMinimizeBtnRT, _titleBarMinimizeBtnRT != null ? _titleBarMinimizeBtnRT.GetComponentInChildren<Text>() : null, 0, s, GalleryUiDesignTokens.FontMinRef, glyph: true);
            ApplyTitleBarChipScale(_titleBarCloseBtnRT, _titleBarCloseBtnRT != null ? _titleBarCloseBtnRT.GetComponentInChildren<Text>() : null, 0, s, GalleryUiDesignTokens.FontMinRef, glyph: true);

            ScaleTitleBarIconButtonPadding(s);

            if (globalSourceFilterBtn != null)
            {
                RectTransform rt = globalSourceFilterBtn.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float sourceW = _globalSourceFilterCompact
                        ? GalleryUiDesignTokens.TitleBarChipRef * s
                        : GlobalSourceFilterButtonWidth * s;
                    rt.sizeDelta = new Vector2(sourceW, GlobalSourceFilterButtonHeight * s);
                    if (_globalSourceFilterCompact)
                        ScaleButtonIconPadding(rt, s);
                }
                if (globalSourceFilterBtnText != null)
                    GalleryUiMetrics.ApplyFont(globalSourceFilterBtnText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            if (titleSearchInput != null)
            {
                RectTransform searchRT = titleSearchInput.GetComponent<RectTransform>();
                if (searchRT != null)
                    searchRT.sizeDelta = new Vector2(searchRT.sizeDelta.x, GalleryUiDesignTokens.TitleBarChipRef * s);
            }

            if (_titleBarFpsRT != null)
                _titleBarFpsRT.sizeDelta = new Vector2(100f * s, GalleryUiDesignTokens.TitleBarChipRef * s);

            if (_categoryQuickArrowLE != null || _categoryQuickArrowIconRT != null)
                ApplyCategoryQuickArrowChromeLayout(s);

            if (titleSearchInput != null)
            {
                RectTransform titleBarRT = titleSearchInput.transform.parent as RectTransform;
                if (titleBarRT != null)
                    titleBarRT.sizeDelta = new Vector2(0f, metrics.Px(GalleryUiDesignTokens.TitleBarHeightRef));
            }

            RescaleSideTabFilterTextsInternal(s);
        }

        private void RescaleFooterPerfControlsInternal(GalleryUiMetrics metrics)
        {
            float s = metrics.ChromeScale;
            ApplyTitleBarChipScale(footerPerfToggleBtn != null ? footerPerfToggleBtn.GetComponent<RectTransform>() : null,
                footerPerfToggleBtnText, GalleryUiDesignTokens.FooterPerfToggleFontRef, s);
            if (footerPerfToggleBtnText != null)
                footerPerfToggleBtnText.resizeTextForBestFit = false;

            ApplyTitleBarChipScale(footerPerfMinusBtn != null ? footerPerfMinusBtn.GetComponent<RectTransform>() : null,
                footerPerfMinusBtn != null ? footerPerfMinusBtn.GetComponentInChildren<Text>() : null,
                0, s, GalleryUiDesignTokens.FontMinRef, glyph: true);
            ApplyTitleBarChipScale(footerPerfPlusBtn != null ? footerPerfPlusBtn.GetComponent<RectTransform>() : null,
                footerPerfPlusBtn != null ? footerPerfPlusBtn.GetComponentInChildren<Text>() : null,
                0, s, GalleryUiDesignTokens.FontMinRef, glyph: true);

            ScaleButtonIconPadding(footerDockBtn != null ? footerDockBtn.GetComponent<RectTransform>() : null, s);
            ScaleButtonIconPadding(footerHeightBtn != null ? footerHeightBtn.GetComponent<RectTransform>() : null, s);
            ScaleButtonIconPadding(footerLayoutBtn != null ? footerLayoutBtn.GetComponent<RectTransform>() : null, s);
            ScaleButtonIconPadding(footerPerfMinusBtn != null ? footerPerfMinusBtn.GetComponent<RectTransform>() : null, s);
            ScaleButtonIconPadding(footerPerfPlusBtn != null ? footerPerfPlusBtn.GetComponent<RectTransform>() : null, s);

            if (paginationRT != null && paginationRT.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(paginationRT);
        }

        private void ScaleTitleBarIconButtonPadding(float scale)
        {
            ScaleButtonIconPadding(_titleBarSettingsBtnRT, scale);
            ScaleButtonIconPadding(_titleBarQfToggleBtnRT, scale);
            ScaleButtonIconPadding(_titleBarRatingSortToggleBtnRT, scale);
            ScaleButtonIconPadding(_titleBarRefreshBtnRT, scale);
            ScaleButtonIconPadding(_titleBarFileSortTypeBtnRT, scale);
            ScaleButtonIconPadding(_titleBarFileSortDirBtnRT, scale);
            if (titleCreatorBtn != null)
                ScaleButtonIconPadding(titleCreatorBtn.GetComponent<RectTransform>(), scale);
            if (languageSwitcherBtnGO != null)
                ScaleButtonIconPadding(languageSwitcherBtnGO.GetComponent<RectTransform>(), scale);
            ScaleButtonIconPadding(_titleBarHelpBtnRT, scale);
            ScaleButtonIconPadding(_titleBarOverflowBtnRT, scale);
            ScaleButtonIconPadding(_titleBarMinimizeBtnRT, scale);
            ScaleButtonIconPadding(_titleBarCloseBtnRT, scale);
            if (_titleSearchCompactRT != null)
                ScaleButtonIconPadding(_titleSearchCompactRT, scale);
        }

        private static void ScaleButtonIconPadding(RectTransform btnRT, float scale)
        {
            if (btnRT == null) return;
            Transform icon = btnRT.Find("Icon");
            if (icon == null) return;
            RectTransform irt = icon as RectTransform;
            if (irt == null) return;
            float pad = GalleryUiDesignTokens.SearchIconButtonPadRef * scale;
            irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
        }

        private void RescaleAllSearchInputsInternal(GalleryUiMetrics metrics)
        {
            float s = metrics.ChromeScale;
            ApplyMainSideSearchRowLayout(true, s);
            ApplyMainSideSearchRowLayout(false, s);
            SyncSideTabSubFilterRowChrome(s);
            RescaleSearchInput(titleSearchInput, s, GalleryUiDesignTokens.TitleBarChipRef);
            RescaleSearchInput(titleCreatorDropdownSearchInput, s);
            RescaleSearchInput(_titleSearchPopupField, s, GalleryUiDesignTokens.TitleBarChipRef);
            RescaleSearchInput(_inAppHelpSearchInput, metrics.HelpChromeScale());
        }

        internal static void RescaleSearchInput(InputField input, float scale, float heightRef = 0f)
        {
            if (input == null) return;
            float s = scale <= 0f ? 1f : scale;
            float hRef = heightRef > 0f ? heightRef : GalleryUiDesignTokens.SearchFieldHeightRef;

            RectTransform rt = input.GetComponent<RectTransform>();
            if (rt != null && rt.sizeDelta.y > 0f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, hRef * s);

            Transform icon = input.transform.Find("SearchIcon");
            if (icon != null)
            {
                RectTransform iconRT = icon as RectTransform;
                if (iconRT != null)
                {
                    float iconSz = GalleryUiDesignTokens.SearchIconSizeRef * s;
                    iconRT.anchoredPosition = new Vector2(GalleryUiDesignTokens.SearchIconLeftPadRef * s, 0f);
                    iconRT.sizeDelta = new Vector2(iconSz, iconSz);
                }
            }

            Transform textArea = input.transform.Find("TextArea");
            if (textArea != null)
            {
                RectTransform taRT = textArea as RectTransform;
                if (taRT != null)
                {
                    taRT.offsetMin = new Vector2(GalleryUiDesignTokens.SearchTextLeftInsetRef * s, 0f);
                    taRT.offsetMax = new Vector2(-GalleryUiDesignTokens.SearchTextRightInsetRef * s, 0f);
                }
            }

            if (input.textComponent != null)
                GalleryUiMetrics.ApplyFont(input.textComponent, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            if (input.placeholder is Text ph)
                GalleryUiMetrics.ApplyFont(ph, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);

            for (int i = 0; i < input.transform.childCount; i++)
            {
                Transform ch = input.transform.GetChild(i);
                if (ch == null || ch.name == "TextArea" || ch.name == "SearchIcon") continue;
                RectTransform btnRT = ch as RectTransform;
                if (btnRT == null || ch.GetComponent<Button>() == null) continue;
                float clearW = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                btnRT.anchorMin = new Vector2(1f, 0f);
                btnRT.anchorMax = new Vector2(1f, 1f);
                btnRT.pivot = new Vector2(1f, 0.5f);
                btnRT.sizeDelta = new Vector2(clearW, 0f);
                btnRT.anchoredPosition = Vector2.zero;
                ScaleButtonIconPadding(btnRT, s);
                Text clearText = ch.GetComponentInChildren<Text>();
                if (clearText != null)
                    GalleryUiMetrics.ApplyGlyphFont(clearText, GalleryUiDesignTokens.SearchClearBtnSizeRef, s, GalleryUiDesignTokens.FontMinRef);
            }
        }

        private void RescaleSideTabFilterTextsInternal(float s)
        {
            ApplySideTabFilterFont(leftSubClearBtnText, GalleryUiDesignTokens.FontCaptionRef, s);
            ApplySideTabFilterFont(rightSubClearBtnText, GalleryUiDesignTokens.FontCaptionRef, s);
            ApplySideTabFilterFont(rightRefreshBtnText, GalleryUiDesignTokens.FontBodyRef, s);
        }

        private static void ApplySideTabFilterFont(Text txt, int designPt, float s)
        {
            if (txt != null)
                GalleryUiMetrics.ApplyFont(txt, designPt, s, GalleryUiDesignTokens.FontMinRef);
        }

        internal void RescaleExistingTabButtonsInternal(GalleryUiMetrics metrics)
        {
            RescaleTabButtonList(leftActiveTabButtons, metrics);
            RescaleTabButtonList(rightActiveTabButtons, metrics);
            RescaleTabButtonList(leftSubActiveTabButtons, metrics);
            RescaleTabButtonList(rightSubActiveTabButtons, metrics);
            RescaleTabButtonList(leftCategoryTabButtons, metrics);
            RescaleTabButtonList(rightCategoryTabButtons, metrics);
            RescaleTabButtonList(_leftCreatorVirtButtons, metrics);
            RescaleTabButtonList(_rightCreatorVirtButtons, metrics);

            // Tab buttons size via LayoutElement inside a VerticalLayoutGroup; force the side-tab
            // list containers to reflow now so the new row height/width applies on slider release
            // instead of waiting for the next layout-invalidating event.
            ForceReflowTabContainer(leftTabContainerGO);
            ForceReflowTabContainer(rightTabContainerGO);
            ForceReflowTabContainer(leftSubTabContainerGO);
            ForceReflowTabContainer(rightSubTabContainerGO);
        }

        private static void ForceReflowTabContainer(GameObject containerGO)
        {
            if (containerGO == null) return;
            RectTransform rt = containerGO.GetComponent<RectTransform>();
            if (rt != null && containerGO.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        private static void RescaleTabButtonList(List<GameObject> buttons, GalleryUiMetrics metrics)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Count; i++)
                RescaleTabButton(buttons[i], metrics);
        }

        internal static void RescaleTabButton(GameObject btnGO, GalleryUiMetrics metrics)
        {
            if (btnGO == null) return;
            float s = metrics.ChromeScale;
            Text txt = btnGO.GetComponentInChildren<Text>();
            if (txt != null)
                GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.TabButtonFontRef, s, GalleryUiDesignTokens.TabButtonFontMin);

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = GalleryUiDesignTokens.TabButtonMinWidthRef * s;
                le.preferredWidth = GalleryUiDesignTokens.TabButtonPreferredWidthRef * s;
                le.minHeight = GalleryUiDesignTokens.SideTabRowHeightRef * s;
                le.preferredHeight = GalleryUiDesignTokens.SideTabRowHeightRef * s;
            }

            RescaleTabLeftIcon(btnGO, s);

            ConfigureSideTabRowHoverBorder(btnGO);

            RectTransform rt = btnGO.GetComponent<RectTransform>();
            if (rt != null && rt.anchorMin.y >= 0.99f && rt.anchorMax.y >= 0.99f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, GalleryUiDesignTokens.SideTabRowHeightRef * s);
        }

        /// <summary>
        /// Live-scale category/side-tab left icons from <c>ApplyTabLeftIcon</c>.
        /// Create-time refs: 28 (+backdrop) / 20, left 4, gap 6.
        /// </summary>
        private static void RescaleTabLeftIcon(GameObject btnGO, float s)
        {
            if (btnGO == null) return;
            if (s <= 0f) s = 1f;
            Transform iconTr = btnGO.transform.Find("TabLeftIcon");
            if (iconTr == null) return;

            RectTransform iconRT = iconTr as RectTransform;
            if (iconRT == null) return;

            bool hasBackdrop = iconTr.Find("Backdrop") != null;
            float iconSize = (hasBackdrop ? 28f : 20f) * s;
            float iconLeft = 4f * s;
            float gap = 6f * s;

            iconRT.anchoredPosition = new Vector2(iconLeft, iconRT.anchoredPosition.y);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);

            Transform glyphTr = iconTr.Find("Glyph");
            if (glyphTr != null)
            {
                RectTransform glyphRT = glyphTr as RectTransform;
                if (glyphRT != null)
                {
                    float pad = hasBackdrop ? iconSize * 0.12f : 0f;
                    glyphRT.offsetMin = new Vector2(pad, pad);
                    glyphRT.offsetMax = new Vector2(-pad, -pad);
                }
            }

            Text txt = btnGO.GetComponentInChildren<Text>();
            if (txt == null) return;
            RectTransform txtRT = txt.GetComponent<RectTransform>();
            if (txtRT == null) return;
            float insetL = iconLeft + iconSize + gap;
            txtRT.offsetMin = new Vector2(insetL, txtRT.offsetMin.y);
        }

        internal void RescaleInitChromeTextsInternal(GalleryUiMetrics metrics)
        {
            if (titleText != null) metrics.ApplyTitleFont(titleText);
            if (fpsText != null) metrics.ApplyCaptionFont(fpsText);
            if (statusBarText != null) metrics.ApplyCaptionFont(statusBarText);
            if (collapseHandleText != null) metrics.ApplyBodyFont(collapseHandleText);
            if (collapseHandleLeftText != null) metrics.ApplyBodyFont(collapseHandleLeftText);
            if (collapseHandleTopText != null) metrics.ApplyBodyFont(collapseHandleTopText);
        }

        public void ApplySideButtonScale()
        {
            ApplySideButtonScale(UiMetrics, true);
        }

        public void ApplySideButtonScale(GalleryUiMetrics metrics)
        {
            ApplySideButtonScale(metrics, true);
        }

        public void ApplySideButtonScale(GalleryUiMetrics metrics, bool updatePositions)
        {
            float scale = metrics.ChromeScale;
            bool topDock = IsFixedTopDockMode();
            float w = metrics.Px(GalleryUiDesignTokens.SideButtonWidthRef);
            float h = metrics.Px(GalleryUiDesignTokens.SideButtonHeightRef);
            float squareW = metrics.Px(GalleryUiDesignTokens.SideButtonSquareRef);
            float containerW = metrics.Px(GalleryUiDesignTokens.SideButtonContainerWidthRef);
            float containerOffset = metrics.Px(GalleryUiDesignTokens.SideButtonContainerOffsetRef);
            float hoverStripW = metrics.Px(GalleryUiDesignTokens.SideHoverStripWidthRef);
            float hoverStripOffset = metrics.Px(GalleryUiDesignTokens.SideHoverStripOffsetRef);
            float subW = metrics.Px(GalleryUiDesignTokens.SideButtonWidthRef * GalleryUiDesignTokens.SideButtonSubmenuWidthFactorRef);
            int subFontSize = metrics.FontBody();

            // Top dock: footer row owns button chrome — side-rail sizeDelta here only fights outer scale.
            if (!topDock)
            {
                for (int i = 0; i < rightSideButtons.Count; i++)
                {
                    RectTransform rt = rightSideButtons[i];
                    if (rt == null) continue;
                    bool square = UsesSquareChromeSideButton(rt, rightSideButtons);
                    float bw = square ? squareW : w;
                    float bh = square ? squareW : h;
                    rt.sizeDelta = new Vector2(bw, bh);
                    Text t = rt.GetComponentInChildren<Text>(true);
                    if (t != null)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.SideButtonFontRef, scale, GalleryUiDesignTokens.SideButtonFontMin);
                    ScaleButtonIconPadding(rt, scale);
                }
                for (int i = 0; i < leftSideButtons.Count; i++)
                {
                    RectTransform rt = leftSideButtons[i];
                    if (rt == null) continue;
                    bool square = UsesSquareChromeSideButton(rt, leftSideButtons);
                    float bw = square ? squareW : w;
                    float bh = square ? squareW : h;
                    rt.sizeDelta = new Vector2(bw, bh);
                    Text t = rt.GetComponentInChildren<Text>(true);
                    if (t != null)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.SideButtonFontRef, scale, GalleryUiDesignTokens.SideButtonFontMin);
                    ScaleButtonIconPadding(rt, scale);
                }
            }

            // Match pane height so tall floating/VR stacks stay inside rail hit bounds.
            float containerH = 700f;
            try
            {
                if (backgroundBoxGO != null)
                {
                    RectTransform bg = backgroundBoxGO.GetComponent<RectTransform>();
                    if (bg != null && bg.rect.height > 8f)
                        containerH = Mathf.Max(700f, bg.rect.height);
                }
            }
            catch { }

            if (rightSideContainer != null)
            {
                RectTransform rt = rightSideContainer.GetComponent<RectTransform>();
                if (rt != null) { rt.sizeDelta = new Vector2(containerW, containerH); rt.anchoredPosition = new Vector2(containerOffset, 0); }
            }
            if (rightSideHoverStrip != null)
            {
                RectTransform rt = rightSideHoverStrip.GetComponent<RectTransform>();
                if (rt != null) { rt.sizeDelta = new Vector2(hoverStripW, 0f); rt.anchoredPosition = new Vector2(hoverStripOffset, 0); }
            }
            if (leftSideContainer != null)
            {
                RectTransform rt = leftSideContainer.GetComponent<RectTransform>();
                if (rt != null) { rt.sizeDelta = new Vector2(containerW, containerH); rt.anchoredPosition = new Vector2(-containerOffset, 0); }
            }
            if (leftSideHoverStrip != null)
            {
                RectTransform rt = leftSideHoverStrip.GetComponent<RectTransform>();
                if (rt != null) { rt.sizeDelta = new Vector2(hoverStripW, 0f); rt.anchoredPosition = new Vector2(-hoverStripOffset, 0); }
            }

            foreach (var go in rightSaveSubmenuButtons)
            {
                if (go == null) continue;
                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(subW, h);
                Text t = go.GetComponentInChildren<Text>();
                if (t != null) t.fontSize = subFontSize;
            }
            foreach (var go in leftSaveSubmenuButtons)
            {
                if (go == null) continue;
                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(subW, h);
                Text t = go.GetComponentInChildren<Text>();
                if (t != null) t.fontSize = subFontSize;
            }

            if (!updatePositions) return;
            if (topDock)
                ApplyTopDockSideButtonsLayout(scale);
            else
                UpdateSideButtonPositions();
        }

        /// <summary>Scales grid labels from cell size.</summary>
        internal void ApplyGridCellChromeScale(GameObject btnGO)
        {
            if (btnGO == null || layoutMode == GalleryLayoutMode.List || settingsListViewActive) return;
            ApplyGridLabelStripLayout(btnGO);
        }
    }
}
