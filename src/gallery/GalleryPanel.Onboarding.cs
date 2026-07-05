using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Empty grid state when filters/search hide all rows.

        private GameObject _emptyGridStateGO;
        private Text _emptyGridStateMessage;
        private GameObject _emptyGridStateActionBtn;
        private Text _emptyGridStateActionText;

        private void CreateEmptyGridStateOverlay(GameObject viewportGO)
        {
            if (viewportGO == null || _emptyGridStateGO != null) return;

            _emptyGridStateGO = new GameObject("EmptyGridState");
            _emptyGridStateGO.transform.SetParent(viewportGO.transform, false);
            RectTransform rootRT = _emptyGridStateGO.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            Image blocker = _emptyGridStateGO.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.01f);
            blocker.raycastTarget = false;

            var colGO = new GameObject("Column");
            colGO.transform.SetParent(_emptyGridStateGO.transform, false);
            RectTransform colRT = colGO.AddComponent<RectTransform>();
            colRT.anchorMin = new Vector2(0.5f, 0.5f);
            colRT.anchorMax = new Vector2(0.5f, 0.5f);
            colRT.pivot = new Vector2(0.5f, 0.5f);
            colRT.sizeDelta = new Vector2(460f, 140f);

            var vlg = colGO.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(colGO.transform, false);
            _emptyGridStateMessage = msgGO.AddComponent<Text>();
            try { VPBUiFont.ApplyTo(_emptyGridStateMessage); } catch { _emptyGridStateMessage.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            _emptyGridStateMessage.fontSize = GalleryUiDesignTokens.FontBodyRef;
            _emptyGridStateMessage.fontStyle = FontStyle.Normal;
            _emptyGridStateMessage.alignment = TextAnchor.MiddleCenter;
            _emptyGridStateMessage.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            _emptyGridStateMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            _emptyGridStateMessage.verticalOverflow = VerticalWrapMode.Overflow;
            _emptyGridStateMessage.raycastTarget = false;
            var msgLE = msgGO.AddComponent<LayoutElement>();
            msgLE.preferredHeight = 52f;

            _emptyGridStateActionBtn = UI.CreateUIButton(
                colGO, 200, 36,
                VPBTranslation.T("gallery.empty.clear_search", "Clear search"),
                16, 0, 0, AnchorPresets.middleCenter,
                OnEmptyGridStateActionClicked);
            _emptyGridStateActionBtn.name = "EmptyGridAction";
            _emptyGridStateActionText = _emptyGridStateActionBtn.GetComponentInChildren<Text>(true);
            var btnLE = _emptyGridStateActionBtn.GetComponent<LayoutElement>();
            if (btnLE == null) btnLE = _emptyGridStateActionBtn.AddComponent<LayoutElement>();
            btnLE.preferredWidth = 220f;
            btnLE.preferredHeight = 36f;

            _emptyGridStateGO.SetActive(false);
        }

        private void OnEmptyGridStateActionClicked()
        {
            try
            {
                if (!string.IsNullOrEmpty(nameFilter) && nameFilter.Trim().Length > 0)
                {
                    ClearTitleBarSearch();
                    return;
                }
                if (HasActiveBrowseFiltersExcludingTitleSearch())
                {
                    try { ClearAllBrowseFiltersKeepCategory(); } catch { RefreshFiles(true); }
                    return;
                }
                RefreshFiles(true);
            }
            catch { RefreshFiles(true); }
        }

        private void ClearSubPaneAndExtraBrowseFilters()
        {
            currentSceneSourceFilter = "";
            currentAppearanceSourceFilter = "";
            clothingSubfilter = 0;
            hairSubfilter = 0;
            appearanceSubfilter = 0;
            posePeopleFilter = PosePeopleFilter.All;
            _clothingGenderUserOverride = false;
            _hairGenderUserOverride = false;
            if (_userTagAvailMode == UserTagAvailMode.FilterByTags)
            {
                try { activeUserTags?.Clear(); } catch { }
                try { excludedUserTags?.Clear(); } catch { }
            }
            else if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
            {
                _userTagAvailMode = ResolveDefaultUserTagAvailMode();
                try { ClearUntaggedTaggedPinKeys(); } catch { }
            }
            try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
        }

        public void UpdateEmptyGridState()
        {
            if (_emptyGridStateGO == null) return;

            bool show = hasLoadedContent
                && !IsSettingsPanelOpen()
                && !settingsListViewActive
                && (currentFilteredFiles == null || currentFilteredFiles.Count == 0)
                && (loadingOverlayGO == null || !loadingOverlayGO.activeSelf);

            _emptyGridStateGO.SetActive(show);
            if (!show) return;

            bool hasSearch = !string.IsNullOrEmpty(nameFilter) && nameFilter.Trim().Length > 0;
            bool hasOtherFilters = HasActiveBrowseFiltersExcludingTitleSearch();

            if (hasSearch)
            {
                _emptyGridStateMessage.text = VPBTranslation.T("gallery.empty.no_match_search", "No items match your search.");
                if (_emptyGridStateActionText != null)
                    _emptyGridStateActionText.text = VPBTranslation.T("gallery.empty.clear_search", "Clear search");
            }
            else if (hasOtherFilters)
            {
                _emptyGridStateMessage.text = hasSearch
                    ? VPBTranslation.T("gallery.empty.no_match_search_and_filters",
                        "No items match. Title bar search filters the grid; side panel search filters that list.")
                    : VPBTranslation.T("gallery.empty.no_match_filters", "No items match the current filters.");
                if (_emptyGridStateActionText != null)
                    _emptyGridStateActionText.text = VPBTranslation.T("gallery.empty.clear_filters", "Clear filters");
            }
            else
            {
                _emptyGridStateMessage.text = VPBTranslation.T("gallery.empty.no_items", "No items in this category.");
                if (_emptyGridStateActionText != null)
                    _emptyGridStateActionText.text = VPBTranslation.T("gallery.empty.refresh", "Refresh");
            }
        }

        // First-run hint strip under the title bar.

        private bool _showPostWizardFirstRunHint;
        private GameObject _firstRunHintGO;
        private Text _firstRunHintText;

        private void CreateFirstRunHintStrip()
        {
            if (backgroundBoxGO == null || _firstRunHintGO != null) return;

            _firstRunHintGO = new GameObject("FirstRunHintStrip");
            _firstRunHintGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rt = _firstRunHintGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = new Vector2(20f, -97f);
            rt.offsetMax = new Vector2(-20f, -65f);

            Image bg = _firstRunHintGO.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.22f, 0.34f, 0.92f);
            bg.raycastTarget = true;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(_firstRunHintGO.transform, false);
            RectTransform textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10f, 0f);
            textRT.offsetMax = new Vector2(-76f, 0f);
            _firstRunHintText = textGO.AddComponent<Text>();
            try { VPBUiFont.ApplyTo(_firstRunHintText); } catch { _firstRunHintText.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            _firstRunHintText.fontSize = GalleryUiDesignTokens.FontCaptionRef;
            _firstRunHintText.alignment = TextAnchor.MiddleLeft;
            _firstRunHintText.color = new Color(0.92f, 0.94f, 0.98f, 1f);
            _firstRunHintText.raycastTarget = false;
            _firstRunHintText.text = VPBTranslation.T(
                "gallery.first_run.hint",
                "Tip: Ctrl+V toggles gallery · Side buttons open filters · Select items for actions in the bar below");

            var helpBtn = UI.CreateUIButton(_firstRunHintGO, 32, 32, "?", 18, 0, 0, AnchorPresets.vStretchRight, OpenFirstRunHintHelp);
            helpBtn.name = "Help";
            RectTransform hrt = helpBtn.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(32f, 0f);
            hrt.anchoredPosition = new Vector2(-36f, 0f);
            helpBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f, 0.85f);
            AddTooltip(helpBtn, "gallery.first_run.open_help", "Open gallery help");

            var dismissBtn = UI.CreateUIButton(_firstRunHintGO, 32, 32, "×", 20, 0, 0, AnchorPresets.vStretchRight, DismissFirstRunHintStrip);
            dismissBtn.name = "Dismiss";
            RectTransform drt = dismissBtn.GetComponent<RectTransform>();
            drt.sizeDelta = new Vector2(32f, 0f);
            dismissBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f, 0.85f);

            _firstRunHintGO.SetActive(false);
        }

        /// <summary>Keep first-run hint strip aligned with main grid column (not under side panels).</summary>
        private void ApplyFirstRunHintStripLayout(float leftOffset, float rightOffset, float paneScale)
        {
            if (_firstRunHintGO == null) return;
            RectTransform rt = _firstRunHintGO.GetComponent<RectTransform>();
            if (rt == null) return;
            float h = 32f * paneScale;
            float gridTop = -65f * paneScale - ActiveFilterChromeTopInsetPx(paneScale);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = new Vector2(leftOffset, gridTop - h);
            rt.offsetMax = new Vector2(rightOffset, gridTop);
        }

        private void OpenFirstRunHintHelp()
        {
            try { OpenInAppHelpToSection("filtering"); } catch { }
        }

        private void DismissFirstRunHintStrip()
        {
            _showPostWizardFirstRunHint = false;
            try
            {
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.FirstRunHintsDismissed = true;
                    VPBConfig.Instance.Save();
                }
            }
            catch { }
            if (_firstRunHintGO != null) _firstRunHintGO.SetActive(false);
            try { UpdateLayout(); } catch { }
        }

        public void RefreshFirstRunHintStrip()
        {
            if (_firstRunHintGO == null) return;
            bool dismissed = false;
            try { dismissed = VPBConfig.Instance != null && VPBConfig.Instance.FirstRunHintsDismissed; } catch { }
            bool wizardBlocksHint = false;
            try
            {
                wizardBlocksHint = IsModeSetupPending()
                    || (_modeSetupWizardGO != null && _modeSetupWizardGO.activeSelf);
            }
            catch { }
            bool show = IsVisible && !dismissed && !isCollapsed && !wizardBlocksHint;
            _firstRunHintGO.SetActive(show);
            if (show)
            {
                try { _firstRunHintGO.transform.SetAsLastSibling(); } catch { }
            }
            if (show && _firstRunHintText != null)
            {
                if (_showPostWizardFirstRunHint)
                {
                    _firstRunHintText.text = VPBTranslation.T(
                        "gallery.first_run.hint_post_wizard",
                        "Side buttons filter the grid · Title search filters item names · Select rows for the action bar below · ? opens help");
                }
                else
                {
                    _firstRunHintText.text = VPBTranslation.T(
                        "gallery.first_run.hint",
                        "Tip: Ctrl+V toggles gallery · Side buttons open filters · Select items for actions in the bar below");
                }
            }
        }

        // Title search field styling when Settings side panel is open.

        public static readonly Color ColorTitleSearchSettingsMode = new Color(0.14f, 0.28f, 0.42f, 1f);

        public void SyncTitleSearchChromeForActiveMode()
        {
            if (titleSearchInput == null) return;

            bool settingsMode = IsSettingsPanelOpen() || settingsListViewActive;
            if (titleSearchInput.placeholder is Text ph)
            {
                ph.text = settingsMode
                    ? VPBTranslation.T("gallery.search.settings", "Filter settings...")
                    : VPBTranslation.T("gallery.search.main", "Search...");
            }

            string tSearch = titleSearchInput.text ?? "";
            bool hasTerm = tSearch.Trim().Length > 0;
            Color c;
            if (settingsMode)
                c = hasTerm ? ColorTitleSearchFilterActive : ColorTitleSearchSettingsMode;
            else
                c = hasTerm ? ColorTitleSearchFilterActive : ColorTitleSearchBackdropIdle;

            Image fieldBg = titleSearchInput.GetComponent<Image>();
            if (fieldBg != null) fieldBg.color = c;
            if (_titleSearchCompactGO != null)
            {
                Image cmpBg = _titleSearchCompactGO.GetComponent<Image>();
                if (cmpBg != null) cmpBg.color = c;
            }
        }
    }

}
