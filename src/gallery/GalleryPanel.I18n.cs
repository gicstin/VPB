using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace VPB
{
    public partial class GalleryPanel
    {
        // ── Colors for the language button ──────────────────────────────────────
        private static readonly Color LangBtnColorNormal = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color LangBtnColorOpen   = new Color(0.15f, 0.15f, 0.15f, 1f);

        private void SubscribeLocaleChanged()
        {
            try { VPBTranslation.LocaleChanged -= OnVpBLocaleChanged; } catch { }
            VPBTranslation.LocaleChanged += OnVpBLocaleChanged;
        }

        private void UnsubscribeLocaleChanged()
        {
            try { VPBTranslation.LocaleChanged -= OnVpBLocaleChanged; } catch { }
        }

        private void OnVpBLocaleChanged()
        {
            try
            {
                CloseLanguageMenu();
                try { CloseFileSortTypeMenu(); } catch { }
                RefreshLocalizedUi();
                try { if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true); } catch { }
                try { quickFiltersUI?.RefreshLocalizedUi(); } catch { }
                try { ReloadInAppHelpContent(); } catch { }
            }
            catch { }
        }

        /// <summary>Short code shown on the language switcher button label.</summary>
        private static string GetLocaleShortCode(string localeId)
        {
            if (string.IsNullOrEmpty(localeId)) return "EN";
            switch (localeId.ToLowerInvariant())
            {
                case "en":    return "EN";
                case "zh_cn": return "中文";
                case "zh_tw": return "繁中";
                case "ja":    return "日本";
                case "ko":    return "한국";
                default:      return localeId.Length > 4
                                  ? localeId.Substring(0, 4).ToUpperInvariant()
                                  : localeId.ToUpperInvariant();
            }
        }

        internal static string GetGalleryHistoryFilterRowLabel(GalleryHistoryFilterMode mode)
        {
            switch (mode)
            {
                case GalleryHistoryFilterMode.Recent:
                    return VPBTranslation.T("gallery.history.row.history", "History");
                case GalleryHistoryFilterMode.MostUsed:
                    return VPBTranslation.T("gallery.history.row.most_used", "Most used");
                case GalleryHistoryFilterMode.Scenes:
                    return VPBTranslation.T("gallery.history.row.scenes", "Scenes");
                case GalleryHistoryFilterMode.Appearance:
                    return VPBTranslation.T("gallery.history.row.appearance", "Appearance");
                case GalleryHistoryFilterMode.Clothing:
                    return VPBTranslation.T("gallery.history.row.clothing", "Clothing");
                case GalleryHistoryFilterMode.Hair:
                    return VPBTranslation.T("gallery.history.row.hair", "Hair");
                case GalleryHistoryFilterMode.Plugins:
                    return VPBTranslation.T("gallery.history.row.plugins", "Plugins");
                case GalleryHistoryFilterMode.Pose:
                    return VPBTranslation.T("gallery.history.row.pose", "Poses");
                case GalleryHistoryFilterMode.Body:
                    return VPBTranslation.T("gallery.history.row.body", "Body");
                case GalleryHistoryFilterMode.Misc:
                    return VPBTranslation.T("gallery.history.row.misc", "Misc");
                default:
                    return VPBTranslation.T("gallery.history.row.history", "History");
            }
        }

        // ── Main UI refresh ─────────────────────────────────────────────────────

        /// <summary>Update the first Text child of <paramref name="go"/> with a translated string.</summary>
        private static void RefreshGoText(GameObject go, string key, string fallback)
        {
            if (go == null) return;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null) t.text = VPBTranslation.T(key, fallback);
        }

        /// <summary>Apply CJK-capable font (if available) to all gallery texts and refresh visible strings.</summary>
        public void RefreshLocalizedUi()
        {
            if (backgroundBoxGO == null) return;
            VPBTranslation.EnsureInitialized();

            foreach (Text t in backgroundBoxGO.GetComponentsInChildren<Text>(true))
                VPBUiFont.ApplyTo(t);

            if (canvas != null)
            {
                foreach (Text t in canvas.gameObject.GetComponentsInChildren<Text>(true))
                {
                    if (t.transform.IsChildOf(backgroundBoxGO.transform)) continue;
                    VPBUiFont.ApplyTo(t);
                }
            }

            if (quickFiltersToggleBtnText != null)
                quickFiltersToggleBtnText.text = VPBTranslation.T("gallery.title.filter_presets", "P");
            if (titleBarRefreshBtnText != null)
                titleBarRefreshBtnText.text = VPBTranslation.T("gallery.title.refresh", "Refresh");

            UpdateSortButtonText(fileSortTypeText, fileSortDirText, GetSortState("Files"));
            try { SyncSceneSourceSortButtonHighlights(); } catch { }
            try { RebuildFileSortTypeMenuOptions(); } catch { }
            try { SyncSidePaneTopSortButtonVisuals(); } catch { }

            if (leftSubClearBtnText != null)
                leftSubClearBtnText.text = VPBTranslation.T("gallery.tags.clear_selected", "Clear Selected");
            if (rightSubClearBtnText != null)
                rightSubClearBtnText.text = VPBTranslation.T("gallery.tags.clear_selected", "Clear Selected");

            if (rightCategoryBtnIconImage == null && rightCategoryBtnText != null)
                rightCategoryBtnText.text = VPBTranslation.T("gallery.side.category", "Category");
            if (leftCategoryBtnIconImage == null && leftCategoryBtnText != null)
                leftCategoryBtnText.text = VPBTranslation.T("gallery.side.category", "Category");
            if (rightCreatorBtnIconImage == null && rightCreatorBtnText != null)
                rightCreatorBtnText.text = VPBTranslation.T("gallery.side.creator", "Creator");
            if (leftCreatorBtnIconImage == null && leftCreatorBtnText != null)
                leftCreatorBtnText.text = VPBTranslation.T("gallery.side.creator", "Creator");
            if (rightPathBtnIconImage == null && rightPathBtnText != null)
                rightPathBtnText.text = VPBTranslation.T("gallery.side.path", "Path");
            if (leftPathBtnIconImage == null && leftPathBtnText != null)
                leftPathBtnText.text = VPBTranslation.T("gallery.side.path", "Path");
            RefreshGoText(footerHubBtnGO, "gallery.side.hub", "Hub");
            if (rightReplaceBtnIconImage == null && rightReplaceBtnText != null)
                rightReplaceBtnText.text = VPBTranslation.T("gallery.side.add", "Add");
            if (leftReplaceBtnIconImage == null && leftReplaceBtnText != null)
                leftReplaceBtnText.text = VPBTranslation.T("gallery.side.add", "Add");
            try { UpdateTargetDropdownUI(); } catch { }

            // Buttons that store Text refs directly
            if (titleBarSettingsBtnText != null)
            {
                bool hasIcon = _titleBarSettingsBtnRT != null && _titleBarSettingsBtnRT.Find("Icon") != null;
                if (!hasIcon)
                    titleBarSettingsBtnText.text = VPBTranslation.T("gallery.title.settings_abbrev", "S");
            }
            if (rightCloneBtnIconImage == null && rightCloneBtnText != null)
                rightCloneBtnText.text = VPBTranslation.T("gallery.side.clone", "Clone");
            if (leftCloneBtnIconImage == null && leftCloneBtnText != null)
                leftCloneBtnText.text = VPBTranslation.T("gallery.side.clone", "Clone");

            // Buttons stored as GOs – reach the Text child at refresh time
            RefreshGoText(footerLoadRandomBtn, "gallery.footer.random_abbrev", "Rdm");
            if (rightSaveBtnIconImage == null) RefreshGoText(rightSaveBtnGO, "gallery.side.save", "Save");
            if (leftSaveBtnIconImage == null) RefreshGoText(leftSaveBtnGO, "gallery.side.save", "Save");
            if (rightRemoveAtomBtnIconImage == null) RefreshGoText(rightRemoveAtomBtn, "gallery.side.remove", "Remove");
            if (leftRemoveAtomBtnIconImage == null) RefreshGoText(leftRemoveAtomBtn, "gallery.side.remove", "Remove");
            if (rightRemoveAllClothingBtnIconImage == null) RefreshGoText(rightRemoveAllClothingBtn, "gallery.side.remove_clothing", "Remove\nClothing");
            if (leftRemoveAllClothingBtnIconImage == null) RefreshGoText(leftRemoveAllClothingBtn, "gallery.side.remove_clothing", "Remove\nClothing");

            // Selection toolbox: Copy/Delete/Autoinstall/Hide/Unhide/No autoinstall — labels from selection (counts).
            try { RefreshTboxConditionalActionButtons(); } catch { }
            RefreshGoText(tboxLoadBtn, "gallery.tbox.load", "Load");
            RefreshGoText(tboxUnloadBtn, "gallery.tbox.unload", "Unload");
            RefreshGoText(tboxLoadDepsBtn, "gallery.tbox.load_deps", "Load Deps");
            RefreshGoText(tboxCacheTexturesBtn, "gallery.tbox.cache_textures", "Cache Textures");
            if (tboxHintLabel != null)
            {
                int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
                if (sel == 0 && tboxPinned)
                    tboxHintLabel.text = VPBTranslation.T("gallery.tbox.pinned_select", "Pinned — select items for actions");
                else
                    tboxHintLabel.text = VPBTranslation.T("gallery.tbox.hover_expand", "Hover to expand");
            }

            // Undo / Redo labels include the stack count – delegate to the dedicated updater
            try { UpdateUndoRedoButtonLabels(); } catch { }

            // Main search bar placeholder
            try { SyncTitleSearchChromeForActiveMode(); } catch { }

            // Pagination text
            try { UpdatePaginationText(); } catch { }

            // Sync language button label to show the active locale
            if (_langBtnText != null)
                _langBtnText.text = GetLocaleShortCode(VPBTranslation.CurrentLocale);

            UpdateDesktopModeButton();
            UpdateFollowButtonState();
            UpdateReplaceButtonState();
            UpdateKeepClothingButtonState();
            UpdateApplyModeButtonState();
            UpdateTabs();
            UpdateFooterFollowStates();
            UpdateFooterHeightState();
            UpdateFooterShowHiddenPackagesState();
            UpdateFooterAutoHideState();
            UpdateFooterLayoutState();
            SyncRatingSortToggleState();

            try { RebuildLanguageMenuOptions(); } catch { }
            try { RefreshFirstRunHintStrip(); } catch { }
            try { RefreshActiveFilterChips(); } catch { }
        }

        // ── Language switcher setup ──────────────────────────────────────────────

        private void SetupLanguageSwitcher(GameObject titleBarGO)
        {
            // Keep language beside the settings icon to avoid overlap with floating-mode top-left resize handle.

            languageSwitcherBtnGO = UI.CreateUIButton(
                titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef,
                GetLocaleShortCode(VPBTranslation.CurrentLocale),
                14, 0, 0, AnchorPresets.middleCenter,
                ToggleLanguageMenu);
            languageSwitcherBtnGO.name = "LanguageSwitcher";

            _langBtnImage = languageSwitcherBtnGO.GetComponent<Image>();
            _langBtnImage.color = LangBtnColorNormal;

            // Icon
            try
            {
                var icon = UI.LoadIconSprite("vpb_icons/language.png", new Color(1f, 1f, 1f, 1f));
                if (icon != null)
                {
                    UI.AddIconToButton(languageSwitcherBtnGO, icon, padding: 6f);
                }
            }
            catch { }

            _langBtnText = languageSwitcherBtnGO.GetComponentInChildren<Text>();
            if (_langBtnText != null)
            {
                _langBtnText.color = Color.white;
                _langBtnText.alignment = TextAnchor.MiddleCenter;
                _langBtnText.resizeTextForBestFit = true;
                _langBtnText.resizeTextMinSize = GalleryUiDesignTokens.FontMinRef;
                _langBtnText.resizeTextMaxSize = GalleryUiDesignTokens.FontBodyRef;
                VPBUiFont.ApplyTo(_langBtnText);
            }

            RectTransform langRT = languageSwitcherBtnGO.GetComponent<RectTransform>();
            // Keep button row consistent: Language sits left of Settings (-230) with a 6px gap.
            langRT.anchorMin = new Vector2(0.5f, 0.5f);
            langRT.anchorMax = new Vector2(0.5f, 0.5f);
            langRT.pivot     = new Vector2(0.5f, 0.5f);
            langRT.anchoredPosition = new Vector2(-276f, 0f);
            langRT.sizeDelta = new Vector2(40f, 40f);
            // Ensure square sizing (defensive; button factory may override later in some layouts)
            if (Mathf.Abs(langRT.sizeDelta.x - langRT.sizeDelta.y) > 0.01f)
                langRT.sizeDelta = new Vector2(langRT.sizeDelta.x, langRT.sizeDelta.x);

            AddTooltip(languageSwitcherBtnGO, "i18n.switcher.tooltip", "Language / 语言 / 言語");

            // ── Full-screen backdrop (click-outside-to-close) ──────────────────
            languageMenuPopupGO = new GameObject("LanguageMenuPopup");
            languageMenuPopupGO.transform.SetParent(backgroundBoxGO.transform, false);

            RectTransform popRT = languageMenuPopupGO.AddComponent<RectTransform>();
            popRT.anchorMin = Vector2.zero;
            popRT.anchorMax = Vector2.one;
            popRT.offsetMin = Vector2.zero;
            popRT.offsetMax = Vector2.zero;

            Image popBg = languageMenuPopupGO.AddComponent<Image>();
            popBg.color = new Color(0f, 0f, 0f, 0.001f);
            popBg.raycastTarget = true;

            Button popBtn = languageMenuPopupGO.AddComponent<Button>();
            popBtn.transition = Selectable.Transition.None;
            popBtn.onClick.AddListener(CloseLanguageMenu);

            languageMenuPopupGO.SetActive(false);

            // ── Dropdown panel ─────────────────────────────────────────────────
            // Position below the language button on the left title-bar icon cluster.
            GameObject panel = new GameObject("LanguageMenuPanel");
            panel.transform.SetParent(languageMenuPopupGO.transform, false);

            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0f, 1f);
            panelRT.anchorMax = new Vector2(0f, 1f);
            panelRT.pivot     = new Vector2(0f, 1f);
            panelRT.anchoredPosition = new Vector2(114f, -72f);
            panelRT.sizeDelta = new Vector2(230f, 50f); // height grows via ContentSizeFitter

            Image panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f);

            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding           = new RectOffset(6, 6, 6, 6);
            vlg.spacing           = 4;
            {
                var v = vlg;
                innerPaneScaleActions.Add(s => { if (v) { v.spacing = 4f * s; v.padding = new RectOffset(Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s)); } });
            }
            vlg.childControlHeight    = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth     = true;
            vlg.childForceExpandWidth  = true;
            vlg.childAlignment    = TextAnchor.UpperCenter;

            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RebuildLanguageMenuOptions();
        }

        // ── Dropdown options ─────────────────────────────────────────────────────

        private void RescaleLanguageMenuInternal(float s)
        {
            if (languageMenuPopupGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = languageMenuPopupGO.transform.Find("LanguageMenuPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(panel.gameObject, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiDesignTokens.PopupMenuPanelWidthRef);
            RectTransform panelRT = panel as RectTransform;
            if (panelRT != null)
            {
                panelRT.anchorMin = new Vector2(0.5f, 1f);
                panelRT.anchorMax = new Vector2(0.5f, 1f);
                panelRT.pivot = new Vector2(0.5f, 1f);
                if (languageSwitcherBtnGO != null)
                {
                    RectTransform btnRT = languageSwitcherBtnGO.GetComponent<RectTransform>();
                    if (btnRT != null)
                    {
                        float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
                        panelRT.anchoredPosition = new Vector2(
                            btnRT.anchoredPosition.x,
                            -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
                    }
                }
            }
        }

        private void RebuildLanguageMenuOptions()
        {
            if (languageMenuPopupGO == null) return;
            Transform panel = languageMenuPopupGO.transform.Find("LanguageMenuPanel");
            if (panel == null) return;

            // Remove all previous rows
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(panel.GetChild(i).gameObject);

            string currentLocale = VPBTranslation.CurrentLocale;
            List<string> locales = VPBTranslation.GetAvailableLocaleIds();

            foreach (string loc in locales)
            {
                string id = loc;
                bool isCurrent = string.Equals(id, currentLocale, StringComparison.OrdinalIgnoreCase);

                // "\u2713" = ✓ checkmark; four spaces align non-active items
                string label = (isCurrent ? "\u2713  " : "    ") + VPBTranslation.GetLocaleDisplayName(id);

                GameObject row = UI.CreateUIButton(
                    panel.gameObject,
                    GalleryUiDesignTokens.PopupMenuPanelWidthRef - 12f,
                    GalleryUiDesignTokens.PopupMenuRowHeightRef,
                    label,
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0, 0,
                    AnchorPresets.middleCenter,
                    () =>
                    {
                        VPBTranslation.SetLocale(id, saveConfig: true);
                        CloseLanguageMenu();
                    });

                Image rowImg = row.GetComponent<Image>();
                rowImg.color = isCurrent ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;

                Text rowT = row.GetComponentInChildren<Text>();
                if (rowT != null)
                {
                    rowT.color     = isCurrent ? UI.PopupText : UI.PopupMutedText;
                    rowT.fontStyle = FontStyle.Normal;
                    rowT.alignment = TextAnchor.MiddleLeft;
                    VPBUiFont.ApplyTo(rowT);
                }

                // Let layout control height
                LayoutElement le = row.AddComponent<LayoutElement>();
                le.preferredHeight = GalleryUiDesignTokens.PopupMenuRowHeightRef;
                le.flexibleWidth   = 1f;
            }

            try { RescaleLanguageMenuInternal(ChromeScale); } catch { }
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.GetComponent<RectTransform>());
        }

        // ── Toggle / close ───────────────────────────────────────────────────────

        private void ToggleLanguageMenu()
        {
            if (languageMenuPopupGO == null) return;
            languageMenuOpen = !languageMenuOpen;
            if (languageMenuOpen)
            {
                RebuildLanguageMenuOptions();
                try { RescaleLanguageMenuInternal(ChromeScale); } catch { }
                languageMenuPopupGO.transform.SetAsLastSibling();
            }
            languageMenuPopupGO.SetActive(languageMenuOpen);

            if (_langBtnImage != null)
                _langBtnImage.color = languageMenuOpen ? LangBtnColorOpen : LangBtnColorNormal;
        }

        private void CloseLanguageMenu()
        {
            languageMenuOpen = false;
            if (languageMenuPopupGO != null) languageMenuPopupGO.SetActive(false);
            if (_langBtnImage != null)
                _langBtnImage.color = LangBtnColorNormal;
        }
    }
}
