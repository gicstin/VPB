using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Title bar overflow menu (narrow widths hide lang/presets/creator behind "...").

        private const float TitleBarOverflowWidthThresholdRef = 720f;

        private GameObject _titleBarOverflowBtnGO;
        private RectTransform _titleBarOverflowBtnRT;
        private GameObject _titleBarOverflowMenuGO;
        private bool _titleBarOverflowOpen;

        private void EnsureTitleBarOverflowChrome(GameObject titleBarGO)
        {
            if (titleBarGO == null || _titleBarOverflowBtnGO != null) return;

            _titleBarOverflowBtnGO = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, "\u2026", 22, 0, 0, AnchorPresets.middleCenter, ToggleTitleBarOverflowMenu);
            _titleBarOverflowBtnGO.name = "TitleBarOverflowBtn";
            _titleBarOverflowBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            var overflowTxt = _titleBarOverflowBtnGO.GetComponentInChildren<Text>();
            if (overflowTxt != null) overflowTxt.color = Color.white;
            _titleBarOverflowBtnRT = _titleBarOverflowBtnGO.GetComponent<RectTransform>();
            _titleBarOverflowBtnRT.anchorMin = new Vector2(0.5f, 0.5f);
            _titleBarOverflowBtnRT.anchorMax = new Vector2(0.5f, 0.5f);
            _titleBarOverflowBtnRT.pivot = new Vector2(0.5f, 0.5f);
            _titleBarOverflowBtnGO.SetActive(false);
            AddTooltip(_titleBarOverflowBtnGO, "gallery.title.overflow", "More title bar actions");

            _titleBarOverflowMenuGO = UI.CreatePopupMenuRoot(
                backgroundBoxGO != null ? backgroundBoxGO : titleBarGO,
                "TitleBarOverflowMenu",
                CloseTitleBarOverflowMenu);
            _titleBarOverflowMenuGO.SetActive(false);

            GameObject panel = UI.CreatePopupMenuPanel(
                _titleBarOverflowMenuGO, "OverflowMenuPanel",
                AnchorPresets.topMiddle,
                new Vector2(GalleryUiDesignTokens.OverflowMenuPanelWidthRef, 50f),
                new Vector2(-200f, -72f));
            RebuildTitleBarOverflowMenuRows(panel.transform);
        }

        private void RebuildTitleBarOverflowMenuRows(Transform panel)
        {
            if (panel == null) return;
            UI.DestroyAllChildren(panel);

            bool ratingActive = HasRatingPresenceFilter();
            bool fpsActive = fpsText != null && fpsText.gameObject != null && fpsText.gameObject.activeSelf;

            AddOverflowMenuRow(
                panel,
                VPBTranslation.T("gallery.title.overflow_language", "Language"),
                () => { CloseTitleBarOverflowMenu(); ToggleLanguageMenu(); },
                icon: UI.GetButtonIconSprite(languageSwitcherBtnGO) ?? UI.LoadIconSprite("vpb_icons/language.png", Color.white),
                tipKey: "i18n.switcher.tooltip", tipDefault: "Language / 语言 / 言語");
            AddOverflowMenuRow(
                panel,
                VPBTranslation.T("gallery.title.filter_presets", "Filter presets"),
                () => { CloseTitleBarOverflowMenu(); ToggleQuickFilters(); },
                icon: UI.GetButtonIconSprite(_titleBarQfToggleBtnRT != null ? _titleBarQfToggleBtnRT.gameObject : null)
                    ?? UI.LoadIconSprite("vpb_icons/filter.png", UI.BarIconGlyphTint),
                tipKey: "gallery.tooltip.filter_presets", tipDefault: "Filter Presets");
            AddOverflowMenuRow(
                panel,
                VPBTranslation.T("gallery.title.creator_filter", "Creator filter"),
                () => { CloseTitleBarOverflowMenu(); ToggleTitleCreatorDropdown(); },
                icon: UI.GetButtonIconSprite(titleCreatorBtn)
                    ?? UI.LoadIconSprite("vpb_icons/gallery_creator.png", UI.BarIconGlyphTint),
                tipKey: "gallery.tooltip.creator_filter",
                tipDefault: "Multi-select creators → filter grid. Right-click clear.");
            AddOverflowMenuRow(
                panel,
                VPBTranslation.T("gallery.title.browse_filter", "Filter"),
                () => { CloseTitleBarOverflowMenu(); ToggleGlobalSourceFilterDropdown(); },
                HasTitleBarBrowseFilterActive(),
                icon: UI.GetButtonIconSprite(globalSourceFilterBtn)
                    ?? UI.LoadIconSprite(
                        HasTitleBarBrowseFilterActive() ? "vpb_icons/filter_on.png" : "vpb_icons/filter_off.png",
                        UI.BarIconGlyphTint),
                tipKey: "gallery.tooltip.browse_filter",
                tipDefault: "Filter: source, hidden, always loaded, old versions. Click rows to cycle Off → apply → only. Right-click clears.");
            AddOverflowMenuRow(
                panel,
                ResolveRatingPresenceFilterLabel(),
                () => { CloseTitleBarOverflowMenu(); ToggleRatingSort(); },
                ratingActive,
                icon: UI.GetButtonIconSprite(ratingSortToggleBtn)
                    ?? (ratingActive ? ratingStarOffSprite : ratingStarNormalSprite)
                    ?? UI.LoadIconSprite("vpb_icons/star.png", UI.BarIconGlyphTint),
                tipKey: null,
                tipDefault: BuildRatingPresenceFilterTooltip());
            AddOverflowMenuRow(
                panel,
                VPBTranslation.T("gallery.title.fps_counter", "FPS counter"),
                () => { CloseTitleBarOverflowMenu(); QuickMenu_ToggleFpsCounter(); },
                fpsActive,
                tipKey: "gallery.tooltip.fps_counter",
                tipDefault: "Show or hide the FPS counter");
            AddOverflowMenuRow(
                panel,
                VPBTranslation.T("gallery.title.creator_mode", "Creator Mode"),
                () => { CloseTitleBarOverflowMenu(); ToggleCreatorMode(); },
                creatorModeActive,
                icon: UI.LoadIconSprite("vpb_icons/creator_mode.png", UI.BarIconGlyphTint),
                tipKey: "gallery.tooltip.creator_mode",
                tipDefault: "Creator Mode — sticky scene authoring tools. Not the Creators author list. Ctrl+Shift+K. Esc exits.");
        }

        private void AddOverflowMenuRow(
            Transform panel, string label, UnityAction onClick, bool active = false, Sprite icon = null,
            string tipKey = null, string tipDefault = null)
        {
            GameObject row = UI.AddStretchPopupMenuRow(panel, label, onClick, active, icon: icon);
            if (row == null) return;
            if (!string.IsNullOrEmpty(tipKey))
                AddTooltip(row, tipKey, tipDefault ?? label);
            else if (!string.IsNullOrEmpty(tipDefault))
                AddTooltipPlain(row, tipDefault);
        }

        private void ToggleTitleBarOverflowMenu()
        {
            if (_titleBarOverflowMenuGO == null) return;
            _titleBarOverflowOpen = !_titleBarOverflowOpen;
            if (_titleBarOverflowOpen)
            {
                Transform panel = _titleBarOverflowMenuGO.transform.Find("OverflowMenuPanel");
                if (panel != null) RebuildTitleBarOverflowMenuRows(panel);
                try { RescaleTitleBarOverflowMenuInternal(ChromeScale); } catch { }
                _titleBarOverflowMenuGO.transform.SetAsLastSibling();
            }
            _titleBarOverflowMenuGO.SetActive(_titleBarOverflowOpen);
        }

        private void CloseTitleBarOverflowMenu()
        {
            _titleBarOverflowOpen = false;
            if (_titleBarOverflowMenuGO != null) _titleBarOverflowMenuGO.SetActive(false);
        }

        private void PositionTitleBarOverflowMenuPanel(RectTransform panelRT)
        {
            if (panelRT == null || _titleBarOverflowBtnRT == null || _titleBarOverflowMenuGO == null) return;
            RectTransform overlayRT = _titleBarOverflowMenuGO.GetComponent<RectTransform>();
            if (overlayRT == null) return;

            float s = ChromeScale <= 0f ? 1f : ChromeScale;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            panelRT.anchorMin = new Vector2(0.5f, 1f);
            panelRT.anchorMax = new Vector2(0.5f, 1f);
            panelRT.pivot = new Vector2(0.5f, 1f);
            panelRT.anchoredPosition = new Vector2(
                _titleBarOverflowBtnRT.anchoredPosition.x,
                -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
        }

        private void RescaleTitleBarOverflowMenuInternal(float s)
        {
            if (_titleBarOverflowMenuGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = _titleBarOverflowMenuGO.transform.Find("OverflowMenuPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(panel.gameObject, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuOverflowFontRef,
                GalleryUiDesignTokens.OverflowMenuPanelWidthRef);
            if (_titleBarOverflowOpen)
                PositionTitleBarOverflowMenuPanel(panel as RectTransform);
        }

        private bool TitleBarUsesOverflowMenu(bool hasSourceFilter, float titleBarWidth, float paneScale)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            return titleBarWidth < TitleBarOverflowWidthThresholdRef * s;
        }

        /// <summary>Width of settings + overflow/lang/presets/creator/source cluster for title-bar layout math.</summary>
        private float TitleBarLeftPackWidthEstimate(bool overflowMode, bool hasSourceFilter, float sourceW, float chip, float gap)
        {
            int n = 0;
            if (_titleBarSettingsBtnRT != null) n++;
            if (overflowMode)
                n++;
            else
            {
                if (hasSourceFilter) n++;
                if (languageSwitcherBtnGO != null) n++;
                if (_titleBarQfToggleBtnRT != null) n++;
                if (titleCreatorBtn != null) n++;
            }
            if (n <= 0) return 0f;
            float w = n * chip + (n - 1) * gap;
            if (!overflowMode && hasSourceFilter) w += sourceW - chip;
            return w;
        }

        private bool ApplyTitleBarOverflowLayout(float paneScale, float titleBarWidth, float leftPackStart, float chip, float gap, ref float xlCursor)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            bool useOverflow = titleBarWidth < TitleBarOverflowWidthThresholdRef * s;
            float halfChip = chip * 0.5f;

            if (languageSwitcherBtnGO != null)
                languageSwitcherBtnGO.SetActive(!useOverflow);
            if (_titleBarQfToggleBtnRT != null)
                _titleBarQfToggleBtnRT.gameObject.SetActive(!useOverflow);
            if (titleCreatorBtn != null)
                titleCreatorBtn.SetActive(!useOverflow);
            if (globalSourceFilterBtn != null)
                globalSourceFilterBtn.SetActive(!useOverflow);
            if (_titleBarRatingSortToggleBtnRT != null)
                _titleBarRatingSortToggleBtnRT.gameObject.SetActive(!useOverflow);
            if (_titleBarFpsRT != null)
                _titleBarFpsRT.gameObject.SetActive(!useOverflow);

            if (_titleBarOverflowBtnGO != null)
            {
                _titleBarOverflowBtnGO.SetActive(useOverflow);
                if (useOverflow && _titleBarOverflowBtnRT != null)
                {
                    _titleBarOverflowBtnRT.anchoredPosition = new Vector2(xlCursor + halfChip, 0f);
                    xlCursor += chip + gap;
                }
            }

            if (!useOverflow)
                CloseTitleBarOverflowMenu();

            return useOverflow;
        }
    }
}
