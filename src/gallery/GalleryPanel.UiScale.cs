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
            try { ApplyInnerPaneScaleLegacyActions(chromeS); } catch { }
            try { ApplySideTabFilterRowVerticalLayoutInternal(chromeS); } catch { }
            try { ApplyUserTagsStickyScrollChromeInternal(TabScrollTopOffsetPublic()); } catch { }
            try { RescaleExistingTabButtonsInternal(m); } catch { }
            try { RescaleInitChromeTextsInternal(m); } catch { }
            try { RescaleTitleBarChromeInternal(m); } catch { }
            try { RescaleFooterPerfControlsInternal(m); } catch { }
            try { RescaleAllSearchInputsInternal(m); } catch { }
            try { ApplySideButtonScale(m); } catch { }
            try { ApplyCategoryQuickChromeLayout(chromeS); } catch { }
            try { RescalePopupMenusInternal(chromeS); } catch { }
            try { RescaleFooterInfoBarInternal(chromeS); } catch { }
            try { ApplyTitleBarResponsiveLayoutInternal(chromeS); } catch { }
        }

        /// <summary>Scales hover-path tooltip text and collapsed tbox label fonts.</summary>
        private void RescaleFooterInfoBarInternal(float s)
        {
            if (s <= 0f) s = 1f;
            if (hoverPathText != null)
                GalleryUiMetrics.ApplyFont(hoverPathText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            if (tboxLabel != null)
                GalleryUiMetrics.ApplyFont(tboxLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            if (tboxHintLabel != null)
                GalleryUiMetrics.ApplyFont(tboxHintLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
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
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform ch = panel.GetChild(i);
                if (ch == null) continue;
                LayoutElement le = ch.GetComponent<LayoutElement>();
                if (le != null)
                    le.preferredHeight = rowH;
                Text t = ch.GetComponentInChildren<Text>(true);
                if (t != null)
                    GalleryUiMetrics.ApplyFont(t, fontRef, s, GalleryUiDesignTokens.FontMinRef);
            }
        }

        private void RescalePopupMenusInternal(float s)
        {
            if (s <= 0f) s = 1f;
            SyncFileSortTypeMenuLayout(s);
            ScaleVerticalPopupMenuRows(fileSortTypeMenuPanelGO, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiDesignTokens.FileSortMenuPanelWidthRef);
            ScaleVerticalPopupMenuRows(sidePaneSortMenuPanelGO, s,
                GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiDesignTokens.SidePaneSortMenuPanelWidthRef);
            try { ApplyCategoryQuickMenuRowsLayout(s); } catch { }
            try { ApplyTitleCreatorDropdownLayout(s); } catch { }
            try { RescaleTitleBarOverflowMenuInternal(s); } catch { }
            if (tboxTargetMenuOpen)
            {
                try { RebuildTboxTargetMenuOptions(); } catch { }
            }
        }

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

            ApplyTitleBarChipScale(_titleBarHelpBtnRT, _titleBarHelpBtnRT != null ? _titleBarHelpBtnRT.GetComponentInChildren<Text>() : null, GalleryUiDesignTokens.TitleBarHelpFontRef, s);
            ApplyTitleBarChipScale(_titleBarOverflowBtnRT, _titleBarOverflowBtnGO != null ? _titleBarOverflowBtnGO.GetComponentInChildren<Text>() : null, GalleryUiDesignTokens.TitleBarOverflowFontRef, s);
            ApplyTitleBarChipScale(_titleBarMinimizeBtnRT, _titleBarMinimizeBtnRT != null ? _titleBarMinimizeBtnRT.GetComponentInChildren<Text>() : null, 0, s, GalleryUiDesignTokens.FontMinRef, glyph: true);
            ApplyTitleBarChipScale(_titleBarCloseBtnRT, _titleBarCloseBtnRT != null ? _titleBarCloseBtnRT.GetComponentInChildren<Text>() : null, 0, s, GalleryUiDesignTokens.FontMinRef, glyph: true);

            if (globalSourceFilterBtn != null)
            {
                RectTransform rt = globalSourceFilterBtn.GetComponent<RectTransform>();
                if (rt != null)
                    rt.sizeDelta = new Vector2(GlobalSourceFilterButtonWidth * s, GlobalSourceFilterButtonHeight * s);
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

            if (_categoryQuickArrowText != null)
            {
                GalleryUiMetrics.ApplyGlyphFont(_categoryQuickArrowText, GalleryUiDesignTokens.TitleBarCategoryRowHeightRef, s, GalleryUiDesignTokens.FontMinRef);
                if (_categoryQuickArrowLE != null)
                {
                    _categoryQuickArrowLE.preferredWidth = 28f * s;
                    _categoryQuickArrowLE.minWidth = 28f * s;
                    _categoryQuickArrowLE.preferredHeight = GalleryUiDesignTokens.TitleBarCategoryRowHeightRef * s;
                }
            }

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
            ApplyLeftSubSearchLayoutScaled(s);
            ApplyRightSubSearchLayoutScaled(s);
            RescaleSearchInput(titleSearchInput, s);
            RescaleSearchInput(titleCreatorDropdownSearchInput, s);
            RescaleSearchInput(_titleSearchPopupField, s);
            RescaleSearchInput(_inAppHelpSearchInput, metrics.HelpChromeScale());
        }

        internal static void RescaleSearchInput(InputField input, float scale)
        {
            if (input == null) return;
            float s = scale <= 0f ? 1f : scale;

            RectTransform rt = input.GetComponent<RectTransform>();
            if (rt != null && rt.sizeDelta.y > 0f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, GalleryUiDesignTokens.SearchFieldHeightRef * s);

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
                float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                btnRT.sizeDelta = new Vector2(clearSz, clearSz);
                btnRT.anchoredPosition = new Vector2(-GalleryUiDesignTokens.SearchClearBtnRightInsetRef * s, 0f);
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

            RectTransform rt = btnGO.GetComponent<RectTransform>();
            if (rt != null && rt.anchorMin.y >= 0.99f && rt.anchorMax.y >= 0.99f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, GalleryUiDesignTokens.SideTabRowHeightRef * s);
        }

        internal void RescaleInitChromeTextsInternal(GalleryUiMetrics metrics)
        {
            if (titleText != null) metrics.ApplyTitleFont(titleText);
            if (fpsText != null) metrics.ApplyBodyFont(fpsText);
            if (statusBarText != null) metrics.ApplyTitleFont(statusBarText, 11);
            if (collapseHandleText != null) metrics.ApplyBodyFont(collapseHandleText);
            if (collapseHandleLeftText != null) metrics.ApplyBodyFont(collapseHandleLeftText);
            if (collapseHandleTopText != null) metrics.ApplyBodyFont(collapseHandleTopText);
        }

        public void ApplySideButtonScale()
        {
            ApplySideButtonScale(UiMetrics);
        }

        public void ApplySideButtonScale(GalleryUiMetrics metrics)
        {
            float scale = metrics.ChromeScale;
            float w = metrics.Px(GalleryUiDesignTokens.SideButtonWidthRef);
            float h = metrics.Px(GalleryUiDesignTokens.SideButtonHeightRef);
            float squareW = metrics.Px(GalleryUiDesignTokens.SideButtonSquareRef);
            float containerW = metrics.Px(GalleryUiDesignTokens.SideButtonContainerWidthRef);
            float containerOffset = metrics.Px(GalleryUiDesignTokens.SideButtonContainerOffsetRef);
            float hoverStripW = metrics.Px(GalleryUiDesignTokens.SideHoverStripWidthRef);
            float hoverStripOffset = metrics.Px(GalleryUiDesignTokens.SideHoverStripOffsetRef);
            float subW = metrics.Px(GalleryUiDesignTokens.SideButtonWidthRef * GalleryUiDesignTokens.SideButtonSubmenuWidthFactorRef);
            int subFontSize = metrics.FontBody();

            for (int i = 0; i < rightSideButtons.Count; i++)
            {
                RectTransform rt = rightSideButtons[i];
                if (rt == null) continue;
                bool square = UsesSquareChromeSideButton(rt, rightSideButtons);
                rt.sizeDelta = new Vector2(square ? squareW : w, h);
                Text t = rt.GetComponentInChildren<Text>(true);
                if (t != null)
                    GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.SideButtonFontRef, scale, GalleryUiDesignTokens.SideButtonFontMin);
            }
            for (int i = 0; i < leftSideButtons.Count; i++)
            {
                RectTransform rt = leftSideButtons[i];
                if (rt == null) continue;
                bool square = UsesSquareChromeSideButton(rt, leftSideButtons);
                rt.sizeDelta = new Vector2(square ? squareW : w, h);
                Text t = rt.GetComponentInChildren<Text>(true);
                if (t != null)
                    GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.SideButtonFontRef, scale, GalleryUiDesignTokens.SideButtonFontMin);
            }

            if (rightSideContainer != null)
            {
                RectTransform rt = rightSideContainer.GetComponent<RectTransform>();
                if (rt != null) { rt.sizeDelta = new Vector2(containerW, 700f); rt.anchoredPosition = new Vector2(containerOffset, 0); }
            }
            if (rightSideHoverStrip != null)
            {
                RectTransform rt = rightSideHoverStrip.GetComponent<RectTransform>();
                if (rt != null) { rt.sizeDelta = new Vector2(hoverStripW, 0f); rt.anchoredPosition = new Vector2(hoverStripOffset, 0); }
            }
            if (leftSideContainer != null)
            {
                RectTransform rt = leftSideContainer.GetComponent<RectTransform>();
                if (rt != null) { rt.sizeDelta = new Vector2(containerW, 700f); rt.anchoredPosition = new Vector2(-containerOffset, 0); }
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

            UpdateSideButtonPositions();
        }

        /// <summary>Scales grid badges/labels from cell size — does not resize the cell.</summary>
        internal void ApplyGridCellChromeScale(GameObject btnGO)
        {
            if (btnGO == null || layoutMode == GalleryLayoutMode.List || settingsListViewActive) return;

            RectTransform rt = btnGO.GetComponent<RectTransform>();
            float cellW = rt != null ? rt.rect.width : GalleryUiDesignTokens.GridCellRefSize;
            float cellH = rt != null ? rt.rect.height : GalleryUiDesignTokens.GridCellRefSize;
            float overlay = GalleryUiMetrics.CellOverlayScale(cellW, cellH);
            float badge = GalleryUiDesignTokens.GridBadgeSizeRef * overlay;
            int badgeFont = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.GridBadgeFontRef, overlay, 10);

            ScaleGridBadge(btnGO.transform, "AutoInstallBadge", badge, badgeFont, 6f * overlay, -6f * overlay);
            ScaleGridBadge(btnGO.transform, "HidePackageBadge", badge, badgeFont, 42f * overlay, -6f * overlay);
            ScaleGridBadge(btnGO.transform, "ScanExcludedBadge", badge, badgeFont, 80f * overlay, -6f * overlay);
            ScaleGridBadge(btnGO.transform, "UserTagsBadge", badge, badgeFont, 118f * overlay, -6f * overlay);

            ApplyGridLabelStripLayout(btnGO);
        }

        private static void ScaleGridBadge(Transform root, string name, float size, int fontSize, float posX, float posY)
        {
            Transform tr = root.Find(name);
            if (tr == null) return;
            RectTransform rt = tr as RectTransform;
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(posX, posY);
            }
            LayoutElement le = tr.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = size;
                le.minWidth = size;
                le.preferredHeight = size;
                le.minHeight = size;
            }
            Text t = tr.GetComponentInChildren<Text>();
            if (t != null) t.fontSize = fontSize;
        }
    }
}
