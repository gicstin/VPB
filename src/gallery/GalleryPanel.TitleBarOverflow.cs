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

            _titleBarOverflowBtnGO = UI.CreateUIButton(titleBarGO, 40, 40, "\u2026", 22, 0, 0, AnchorPresets.middleCenter, ToggleTitleBarOverflowMenu);
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

            _titleBarOverflowMenuGO = new GameObject("TitleBarOverflowMenu");
            _titleBarOverflowMenuGO.transform.SetParent(backgroundBoxGO != null ? backgroundBoxGO.transform : titleBarGO.transform, false);
            RectTransform popRT = _titleBarOverflowMenuGO.AddComponent<RectTransform>();
            popRT.anchorMin = Vector2.zero;
            popRT.anchorMax = Vector2.one;
            popRT.offsetMin = Vector2.zero;
            popRT.offsetMax = Vector2.zero;
            Image popBg = _titleBarOverflowMenuGO.AddComponent<Image>();
            popBg.color = new Color(0f, 0f, 0f, 0.001f);
            popBg.raycastTarget = true;
            Button popBtn = _titleBarOverflowMenuGO.AddComponent<Button>();
            popBtn.transition = Selectable.Transition.None;
            popBtn.onClick.AddListener(CloseTitleBarOverflowMenu);
            _titleBarOverflowMenuGO.SetActive(false);

            GameObject panel = new GameObject("OverflowMenuPanel");
            panel.transform.SetParent(_titleBarOverflowMenuGO.transform, false);
            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 1f);
            panelRT.anchorMax = new Vector2(0.5f, 1f);
            panelRT.pivot = new Vector2(0.5f, 1f);
            panelRT.anchoredPosition = new Vector2(-200f, -72f);
            panelRT.sizeDelta = new Vector2(220f, 50f);
            Image panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f);
            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RebuildTitleBarOverflowMenuRows(panel.transform);
        }

        private void RebuildTitleBarOverflowMenuRows(Transform panel)
        {
            if (panel == null) return;
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

            bool ratingActive = !string.IsNullOrEmpty(currentRatingFilter);
            bool fpsActive = fpsText != null && fpsText.gameObject != null && fpsText.gameObject.activeSelf;

            AddOverflowMenuRow(panel, VPBTranslation.T("i18n.switcher.tooltip", "Language"), () => { CloseTitleBarOverflowMenu(); ToggleLanguageMenu(); });
            AddOverflowMenuRow(panel, VPBTranslation.T("gallery.title.filter_presets", "Filter presets"), () => { CloseTitleBarOverflowMenu(); ToggleQuickFilters(); });
            AddOverflowMenuRow(panel, VPBTranslation.T("gallery.title.creator_filter", "Creator filter"), () => { CloseTitleBarOverflowMenu(); ToggleTitleCreatorDropdown(); });
            AddOverflowMenuRow(panel, VPBTranslation.T("gallery.title.source_filter", "Source filter"), () => { CloseTitleBarOverflowMenu(); ToggleGlobalSourceFilterDropdown(); });
            AddOverflowMenuRow(panel, VPBTranslation.T("gallery.title.rated_only", "Rated only"), () => { CloseTitleBarOverflowMenu(); ToggleRatingSort(); }, ratingActive);
            AddOverflowMenuRow(panel, VPBTranslation.T("gallery.title.fps_counter", "FPS counter"), () => { CloseTitleBarOverflowMenu(); QuickMenu_ToggleFpsCounter(); }, fpsActive);
        }

        private static void AddOverflowMenuRow(Transform panel, string label, UnityAction onClick, bool active = false)
        {
            if (panel == null || onClick == null) return;
            GameObject row = UI.CreateUIButton(panel.gameObject, 0f, 36f, label, 15, 0f, 0f, AnchorPresets.stretchAll, onClick);
            if (row == null) return;
            Image img = row.GetComponent<Image>();
            if (img != null) img.color = active ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;
            Text t = row.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleLeft;
                t.color = Color.white;
            }
            LayoutElement le = row.GetComponent<LayoutElement>();
            if (le == null) le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.flexibleWidth = 1f;
        }

        private void ToggleTitleBarOverflowMenu()
        {
            if (_titleBarOverflowMenuGO == null) return;
            _titleBarOverflowOpen = !_titleBarOverflowOpen;
            if (_titleBarOverflowOpen)
            {
                Transform panel = _titleBarOverflowMenuGO.transform.Find("OverflowMenuPanel");
                if (panel != null) RebuildTitleBarOverflowMenuRows(panel);
                try
                {
                    var panelRT = panel as RectTransform;
                    if (panelRT != null && _titleBarOverflowBtnRT != null)
                    {
                        float cs = ChromeScale;
                        float yOff = -(GalleryUiDesignTokens.TitleBarHeightRef + GalleryUiDesignTokens.PopupMenuAnchorGapRef * cs) * cs;
                        panelRT.anchoredPosition = new Vector2(_titleBarOverflowBtnRT.anchoredPosition.x, yOff);
                    }
                }
                catch { }
                _titleBarOverflowMenuGO.transform.SetAsLastSibling();
            }
            _titleBarOverflowMenuGO.SetActive(_titleBarOverflowOpen);
        }

        private void CloseTitleBarOverflowMenu()
        {
            _titleBarOverflowOpen = false;
            if (_titleBarOverflowMenuGO != null) _titleBarOverflowMenuGO.SetActive(false);
        }

        private void RescaleTitleBarOverflowMenuInternal(float s)
        {
            if (_titleBarOverflowMenuGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = _titleBarOverflowMenuGO.transform.Find("OverflowMenuPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(panel.gameObject, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuOverflowFontRef);
            RectTransform panelRT = panel as RectTransform;
            if (panelRT != null && _titleBarOverflowBtnRT != null)
            {
                float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
                panelRT.anchoredPosition = new Vector2(
                    _titleBarOverflowBtnRT.anchoredPosition.x,
                    -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
            }
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
