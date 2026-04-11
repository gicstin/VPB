using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace VPB
{
    public partial class GalleryPanel
    {
        // ── Colors for the language button ──────────────────────────────────────
        private static readonly Color LangBtnColorNormal = new Color(0.15f, 0.20f, 0.45f, 1f);
        private static readonly Color LangBtnColorOpen   = new Color(0.28f, 0.40f, 0.72f, 1f);

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
                try { settingsPanel?.RefreshLocalizedUi(); } catch { }
                try { quickFiltersUI?.RefreshLocalizedUi(); } catch { }
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

        // ── Content-type search placeholder ─────────────────────────────────────

        internal static string GetContentTypePlaceholder(ContentType type)
        {
            switch (type)
            {
                case ContentType.Category:     return VPBTranslation.T("gallery.search.categories", "Categories...");
                case ContentType.Creator:
                case ContentType.HubCreators:  return VPBTranslation.T("gallery.search.creators", "Search Creators...");
                case ContentType.Tags:
                case ContentType.HubTags:      return VPBTranslation.T("gallery.search.tags", "Search Tags...");
                default:                       return VPBTranslation.T("gallery.search.main", "Search...");
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
            try { RebuildFileSortTypeMenuOptions(); } catch { }
            if (leftActiveContent.HasValue)
                UpdateSortButtonText(leftSortBtnText, GetSortState(leftActiveContent.Value.ToString()));
            if (rightActiveContent.HasValue)
                UpdateSortButtonText(rightSortBtnText, GetSortState(rightActiveContent.Value.ToString()));
            UpdateSortButtonText(leftSubSortBtnText, GetSortState("Tags"));
            UpdateSortButtonText(rightSubSortBtnText, GetSortState("Tags"));

            if (leftSubClearBtnText != null)
                leftSubClearBtnText.text = VPBTranslation.T("gallery.tags.clear_selected", "Clear Selected");
            if (rightSubClearBtnText != null)
                rightSubClearBtnText.text = VPBTranslation.T("gallery.tags.clear_selected", "Clear Selected");

            if (rightCategoryBtnText != null)
                rightCategoryBtnText.text = VPBTranslation.T("gallery.side.category", "Category");
            if (leftCategoryBtnText != null)
                leftCategoryBtnText.text = VPBTranslation.T("gallery.side.category", "Category");
            if (rightCreatorBtnText != null)
                rightCreatorBtnText.text = VPBTranslation.T("gallery.side.creator", "Creator");
            if (leftCreatorBtnText != null)
                leftCreatorBtnText.text = VPBTranslation.T("gallery.side.creator", "Creator");
            RefreshGoText(footerHubBtnGO, "gallery.side.hub", "Hub");
            if (rightReplaceBtnText != null)
                rightReplaceBtnText.text = VPBTranslation.T("gallery.side.add", "Add");
            if (leftReplaceBtnText != null)
                leftReplaceBtnText.text = VPBTranslation.T("gallery.side.add", "Add");
            try { UpdateTargetDropdownUI(); } catch { }

            // Buttons that store Text refs directly
            if (titleBarSettingsBtnText != null) titleBarSettingsBtnText.text = VPBTranslation.T("gallery.title.settings_abbrev", "S");
            if (rightCloneBtnText    != null) rightCloneBtnText.text    = VPBTranslation.T("gallery.side.clone",    "Clone");
            if (leftCloneBtnText     != null) leftCloneBtnText.text     = VPBTranslation.T("gallery.side.clone",    "Clone");

            // Buttons stored as GOs – reach the Text child at refresh time
            RefreshGoText(footerLoadRandomBtn, "gallery.footer.random_abbrev", "Rdm");
            RefreshGoText(rightSaveBtnGO,     "gallery.side.save",        "Save");
            RefreshGoText(leftSaveBtnGO,      "gallery.side.save",        "Save");
            RefreshGoText(rightRemoveAtomBtn,  "gallery.side.remove_atom", "Remove\nAtom");
            RefreshGoText(leftRemoveAtomBtn,   "gallery.side.remove_atom", "Remove\nAtom");

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
            if (titleSearchInput != null && titleSearchInput.placeholder is Text searchPh)
                searchPh.text = VPBTranslation.T("gallery.search.main", "Search...");

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
        }

        // ── Language switcher setup ──────────────────────────────────────────────

        private void SetupLanguageSwitcher(GameObject titleBarGO)
        {
            // Top-left of the title bar — the first thing the eye reaches.
            // The title text starts at x=60 to leave room for this 50×50 button.
            // close=[1150,1200], minimize=[1095,1145] are far to the right and unaffected.

            languageSwitcherBtnGO = UI.CreateUIButton(
                titleBarGO, 50, 50,
                GetLocaleShortCode(VPBTranslation.CurrentLocale),
                14, 0, 0, AnchorPresets.middleCenter,
                ToggleLanguageMenu);
            languageSwitcherBtnGO.name = "LanguageSwitcher";

            _langBtnImage = languageSwitcherBtnGO.GetComponent<Image>();
            _langBtnImage.color = LangBtnColorNormal;

            _langBtnText = languageSwitcherBtnGO.GetComponentInChildren<Text>();
            if (_langBtnText != null)
            {
                _langBtnText.color = Color.white;
                _langBtnText.alignment = TextAnchor.MiddleCenter;
                _langBtnText.resizeTextForBestFit = true;
                _langBtnText.resizeTextMinSize = 10;
                _langBtnText.resizeTextMaxSize = 16;
                VPBUiFont.ApplyTo(_langBtnText);
            }

            RectTransform langRT = languageSwitcherBtnGO.GetComponent<RectTransform>();
            langRT.anchorMin = new Vector2(0f, 1f);
            langRT.anchorMax = new Vector2(0f, 1f);
            langRT.pivot     = new Vector2(0f, 1f);
            langRT.anchoredPosition = new Vector2(0f, 0f);
            langRT.sizeDelta = new Vector2(50f, 50f);

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
            // Anchored to top-right of the backdrop overlay so it sits right below the button.
            // panel right edge at (backdrop.right - 112) ≈ button right edge at x=1088.
            GameObject panel = new GameObject("LanguageMenuPanel");
            panel.transform.SetParent(languageMenuPopupGO.transform, false);

            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0f, 1f);
            panelRT.anchorMax = new Vector2(0f, 1f);
            panelRT.pivot     = new Vector2(0f, 1f);
            panelRT.anchoredPosition = new Vector2(0f, -72f);
            panelRT.sizeDelta = new Vector2(230f, 50f); // height grows via ContentSizeFitter

            Image panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.09f, 0.09f, 0.16f, 0.97f);

            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding           = new RectOffset(6, 6, 6, 6);
            vlg.spacing           = 4;
            vlg.childControlHeight    = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth     = true;
            vlg.childForceExpandWidth  = true;
            vlg.childAlignment    = TextAnchor.UpperCenter;

            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Header label
            GameObject headerGO = new GameObject("LangMenuHeader");
            headerGO.transform.SetParent(panel.transform, false);

            Image headerImg = headerGO.AddComponent<Image>();
            headerImg.color = new Color(0.13f, 0.13f, 0.24f, 1f);

            LayoutElement headerLE = headerGO.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 28f;
            headerLE.flexibleWidth   = 1f;

            GameObject headerTextGO = new GameObject("Text");
            headerTextGO.transform.SetParent(headerGO.transform, false);

            Text headerT = headerTextGO.AddComponent<Text>();
            headerT.text      = "Language / \u8bed\u8a00";   // "语言" in Unicode escapes
            headerT.fontSize  = 12;
            headerT.color     = new Color(0.65f, 0.78f, 1f, 1f);
            headerT.alignment = TextAnchor.MiddleCenter;
            headerT.fontStyle = FontStyle.Bold;
            VPBUiFont.ApplyTo(headerT);

            RectTransform headerTextRT = headerTextGO.GetComponent<RectTransform>();
            headerTextRT.anchorMin = Vector2.zero;
            headerTextRT.anchorMax = Vector2.one;
            headerTextRT.offsetMin = Vector2.zero;
            headerTextRT.offsetMax = Vector2.zero;

            RebuildLanguageMenuOptions();
        }

        // ── Dropdown options ─────────────────────────────────────────────────────

        private void RebuildLanguageMenuOptions()
        {
            if (languageMenuPopupGO == null) return;
            Transform panel = languageMenuPopupGO.transform.Find("LanguageMenuPanel");
            if (panel == null) return;

            // Remove all children except the header row
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                if (panel.GetChild(i).name != "LangMenuHeader")
                    UnityEngine.Object.DestroyImmediate(panel.GetChild(i).gameObject);
            }

            string currentLocale = VPBTranslation.CurrentLocale;
            List<string> locales = VPBTranslation.GetAvailableLocaleIds();

            foreach (string loc in locales)
            {
                string id = loc;
                bool isCurrent = string.Equals(id, currentLocale, StringComparison.OrdinalIgnoreCase);

                // "\u2713" = ✓ checkmark; four spaces align non-active items
                string label = (isCurrent ? "\u2713  " : "    ") + VPBTranslation.GetLocaleDisplayName(id);

                GameObject row = UI.CreateUIButton(
                    panel.gameObject, 218, 38, label, 14, 0, 0,
                    AnchorPresets.middleCenter,
                    () =>
                    {
                        VPBTranslation.SetLocale(id, saveConfig: true);
                        CloseLanguageMenu();
                    });

                Image rowImg = row.GetComponent<Image>();
                rowImg.color = isCurrent
                    ? new Color(0.15f, 0.30f, 0.52f, 1f)   // blue highlight for active
                    : new Color(0.16f, 0.16f, 0.24f, 1f);  // dark tile for others

                Text rowT = row.GetComponentInChildren<Text>();
                if (rowT != null)
                {
                    rowT.color     = isCurrent ? Color.white : new Color(0.82f, 0.82f, 0.92f, 1f);
                    rowT.fontStyle = isCurrent ? FontStyle.Bold : FontStyle.Normal;
                    rowT.alignment = TextAnchor.MiddleLeft;
                    VPBUiFont.ApplyTo(rowT);
                }

                // Let layout control height
                LayoutElement le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 38f;
                le.flexibleWidth   = 1f;
            }

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
