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
        // Neutral gallery greys — match main pane chrome (not blue title/chips).
        private static readonly Color SettingsFloatTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color SettingsFloatFooterBarBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color SettingsFloatPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color SettingsFloatScrollBg = GalleryUiColorTokens.ModalSurface;
        private static readonly Color SettingsFloatGroupActive = GalleryUiColorTokens.SurfaceMid;
        private static readonly Color SettingsFloatGroupInactive = GalleryUiColorTokens.SurfacePanel;
        private static readonly Color SettingsFloatFilterBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color SettingsFloatRowBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color SettingsFloatCancelBg = GalleryUiColorTokens.SurfaceMid;

        private const float SettingsFloatFooterBtnWRef = 96f;
        private const float SettingsFloatFooterSaveBtnWRef = 88f;
        private const float SettingsFloatGroupTabPadRef = 6f;
        private const float SettingsFloatGroupTabRowGapRef = 4f;

        private GameObject _settingsFloatRoot;
        private RectTransform _settingsFloatPanelRT;
        private RectTransform _settingsFloatTitleBarRT;
        private Transform _settingsFloatRowsParent;
        private Transform _settingsFloatGroupTabsParent;
        private RectTransform _settingsFloatGroupTabsContentRT;
        private ScrollRect _settingsFloatScrollRect;
        private InputField _settingsFloatFilterInput;
        private UnityAction<string> _settingsFloatFilterOnValueChanged;
        private GameObject _settingsFloatFilterClearGo;
        private GameObject _settingsFloatFilterRow;
        private GameObject _settingsFloatGroupTabsRow;
        private GameObject _settingsFloatScrollHost;
        private GameObject _settingsFloatFooter;
        private GameObject _settingsFloatCollapseBtn;
        private Image _settingsFloatCollapseIcon;
        private float _settingsFloatChromeScale = 1f;
        private float _settingsFloatGroupTabsH;
        private Vector2? _settingsFloatSavedPosCenter;
        private Vector2? _settingsFloatSavedSizeRef;
        private float? _settingsFloatExpandHeightRef;
        private bool _settingsFloatCollapsed;
        private Vector2? _settingsFloatCollapsedTopLeftPos;

        private GameObject ResolveSettingsFloatHost()
        {
            if (canvas != null) return canvas.gameObject;
            return backgroundBoxGO;
        }

        private void EnsureSettingsFloatBuilt()
        {
            if (_settingsFloatRoot != null) return;
            BuildSettingsFloat();
        }

        private void ShowSettingsFloat()
        {
            EnsureSettingsFloatBuilt();
            if (_settingsFloatRoot == null) return;
            _settingsFloatRoot.SetActive(true);
            try { _settingsFloatRoot.transform.SetAsLastSibling(); } catch { }
            // Import rail stays available — refresh peers only (not full UpdateLayout).
            try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
            try
            {
                if (IsFixedTopDockMode())
                    ApplyTopDockSideButtonsLayout(ChromeScale);
            }
            catch { }
        }

        private void HideSettingsFloat()
        {
            CaptureSettingsFloatGeometryToMemory();
            PersistSettingsFloatGeometry();
            if (_settingsFloatRoot != null)
                _settingsFloatRoot.SetActive(false);
            try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
            try
            {
                if (IsFixedTopDockMode())
                    ApplyTopDockSideButtonsLayout(ChromeScale);
            }
            catch { }
        }

        private void DestroySettingsFloatChrome()
        {
            if (_settingsFloatRoot != null)
            {
                try { UnityEngine.Object.Destroy(_settingsFloatRoot); } catch { }
                _settingsFloatRoot = null;
            }
            _settingsFloatPanelRT = null;
            _settingsFloatTitleBarRT = null;
            _settingsFloatRowsParent = null;
            _settingsFloatGroupTabsParent = null;
            _settingsFloatGroupTabsContentRT = null;
            _settingsFloatScrollRect = null;
            _settingsFloatFilterInput = null;
            _settingsFloatFilterOnValueChanged = null;
            _settingsFloatFilterClearGo = null;
            _settingsFloatFilterRow = null;
            _settingsFloatGroupTabsRow = null;
            _settingsFloatScrollHost = null;
            _settingsFloatFooter = null;
            _settingsFloatCollapseBtn = null;
            _settingsFloatCollapseIcon = null;
            _settingsFloatGroupTabsH = 0f;
        }

        private void BuildSettingsFloat()
        {
            float s = ChromeScale;
            _settingsFloatChromeScale = s;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef * s;
            float rowH = GalleryUiDesignTokens.SettingsFloatRowHeightRef * s;

            LoadSettingsFloatGeometryFromConfig();
            float panelWRef = GalleryUiDesignTokens.SettingsFloatDefaultWidthRef;
            float panelHRef = GalleryUiDesignTokens.SettingsFloatDefaultHeightRef;
            if (_settingsFloatSavedSizeRef.HasValue)
            {
                panelWRef = Mathf.Clamp(
                    _settingsFloatSavedSizeRef.Value.x,
                    GalleryUiDesignTokens.SettingsFloatMinWidthRef,
                    GalleryUiDesignTokens.SettingsFloatMaxWidthRef);
                panelHRef = Mathf.Clamp(
                    _settingsFloatSavedSizeRef.Value.y,
                    GalleryUiDesignTokens.SettingsFloatMinHeightRef,
                    GalleryUiDesignTokens.SettingsFloatMaxHeightRef);
            }
            float panelW = panelWRef * s;
            float panelH = panelHRef * s;

            GameObject host = ResolveSettingsFloatHost();
            if (host == null) return;

            _settingsFloatRoot = UI.CreateChildRT(host, "VPB_SettingsFloat", AnchorPresets.stretchAll);
            try { SetLayerRecursiveLocal(_settingsFloatRoot, host.layer); } catch { }

            GameObject panel = UI.CreateChildRT(
                _settingsFloatRoot, "Panel", AnchorPresets.middleCenter,
                new Vector2(panelW, panelH), Vector2.zero);
            UI.AddImage(panel, SettingsFloatPanelBg);
            if (panel.GetComponent<RectMask2D>() == null)
                panel.AddComponent<RectMask2D>();
            _settingsFloatPanelRT = panel.GetComponent<RectTransform>();
            if (_settingsFloatPanelRT != null)
            {
                _settingsFloatPanelRT.pivot = new Vector2(0f, 1f);
                _settingsFloatPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _settingsFloatPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _settingsFloatPanelRT.sizeDelta = new Vector2(panelW, panelH);
                Vector2 center = _settingsFloatSavedPosCenter.HasValue
                    ? _settingsFloatSavedPosCenter.Value
                    : Vector2.zero;
                _settingsFloatPanelRT.anchoredPosition = SettingsFloatCenterToTopLeft(center, _settingsFloatPanelRT.sizeDelta);
            }

            // Title bar (drag)
            GameObject titleBar = UI.CreateChildRT(panel, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            Image titleBg = UI.AddImage(titleBar, SettingsFloatTitleBarBg);
            if (titleBg != null) titleBg.raycastTarget = true;
            _settingsFloatTitleBarRT = titleBar.GetComponent<RectTransform>();
            if (_settingsFloatTitleBarRT != null)
            {
                _settingsFloatTitleBarRT.pivot = new Vector2(0.5f, 1f);
                _settingsFloatTitleBarRT.anchoredPosition = Vector2.zero;
                _settingsFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
            }
            HorizontalLayoutGroup titleHlg = UI.AddHLG(
                titleBar, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (titleBar.GetComponent<RectMask2D>() == null)
                titleBar.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(titleBar, "\u2807", font,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyFloatTitleBarMetrics(titleHlg, grip.gameObject, s);

            float winIconSz = GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s;
            UI.CreateFloatTitleWindowIcon(titleBar, "vpb_icons/settings.png", winIconSz);

            Text title = UI.CreateEmphasisTitleLabel(
                titleBar,
                VPBTranslation.T("settings.title", "Settings"),
                font, Color.white, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(title.gameObject, flexibleWidth: 1f, minWidth: 60f * s);

            _settingsFloatCollapseBtn = SettingsFloatSquareIconButton(
                titleBar.transform, chromeSz, "vpb_icons/chevron_up.png",
                GalleryUiColorTokens.ChromeIconWell, ToggleSettingsFloatCollapsed);
            if (_settingsFloatCollapseBtn != null)
            {
                _settingsFloatCollapseBtn.name = "CollapseBtn";
                Transform iconTr = _settingsFloatCollapseBtn.transform.Find("Icon");
                _settingsFloatCollapseIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }

            GameObject closeBtn = SettingsFloatSquareIconButton(
                titleBar.transform, chromeSz, "vpb_icons/x.png",
                GalleryUiColorTokens.ChromeIconWell, () => ExitInternalSettingsMode(true));
            if (closeBtn != null)
            {
                closeBtn.name = "TitleClose";
                AddTooltip(closeBtn, "settings.float.close", "Close");
            }
            if (_settingsFloatCollapseBtn != null)
            {
                AddTooltip(_settingsFloatCollapseBtn, "settings.float.collapse", "Collapse to title bar");
            }

            var headerDrag = titleBar.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = _settingsFloatPanelRT;
            headerDrag.OnMoved = OnSettingsFloatMoved;

            // Filter row
            _settingsFloatFilterRow = UI.CreateChildRT(panel, "FilterRow", AnchorPresets.hStretchTop,
                new Vector2(0f, filterH), new Vector2(0f, -titleH));
            RectTransform filterRT = _settingsFloatFilterRow.GetComponent<RectTransform>();
            if (filterRT != null)
            {
                filterRT.pivot = new Vector2(0.5f, 1f);
                filterRT.sizeDelta = new Vector2(0f, filterH);
                filterRT.anchoredPosition = new Vector2(0f, -titleH);
            }
            UI.AddHLG(_settingsFloatFilterRow, spacing: 0f,
                padding: UI.Pad(
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef, s),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);

            _settingsFloatFilterInput = UI.CreateChromeLayoutInputField(
                _settingsFloatFilterRow.transform,
                font,
                searchH,
                1f,
                8f * s,
                2f * s,
                SettingsFloatFilterBg,
                UI.InputFieldPlaceholderColor,
                VPBTranslation.T("gallery.search.settings", "Filter settings…"),
                "SettingsFilter");
            if (_settingsFloatFilterInput != null)
            {
                // Search glyph + left inset; reserve right for clear (X).
                UI.LayoutChromeSearchIcon(_settingsFloatFilterInput.gameObject, s);
                float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                Transform textAreaTr = _settingsFloatFilterInput.transform.Find("TextArea");
                if (textAreaTr != null)
                {
                    RectTransform taRt = textAreaTr as RectTransform;
                    if (taRt != null)
                        taRt.offsetMax = new Vector2(-clearSz, taRt.offsetMax.y);
                }

                _settingsFloatFilterClearGo = UI.CreateUIButton(
                    _settingsFloatFilterInput.gameObject,
                    clearSz, searchH, "X", 24, 0, 0, AnchorPresets.middleRight,
                    () =>
                    {
                        SetSettingsFloatFilterTextWithoutNotify("");
                        settingsFilter = "";
                        RefreshInternalSettingsListRows(true);
                        try
                        {
                            _settingsFloatFilterInput.ActivateInputField();
                            _settingsFloatFilterInput.MoveTextEnd(false);
                        }
                        catch { }
                        RefreshSettingsFloatFilterClearVisible();
                    });
                if (_settingsFloatFilterClearGo != null)
                {
                    _settingsFloatFilterClearGo.name = "FilterClear";
                    RectTransform clearRT = _settingsFloatFilterClearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                    {
                        clearRT.anchorMin = new Vector2(1f, 0f);
                        clearRT.anchorMax = new Vector2(1f, 1f);
                        clearRT.pivot = new Vector2(1f, 0.5f);
                        clearRT.anchoredPosition = Vector2.zero;
                        clearRT.sizeDelta = new Vector2(clearSz, 0f);
                    }
                    Image clearBg = _settingsFloatFilterClearGo.GetComponent<Image>();
                    if (clearBg != null) clearBg.color = new Color(0f, 0f, 0f, 0f);
                    try
                    {
                        Sprite xSpr = UI.LoadIconSprite("vpb_icons/x.png", GalleryUiColorTokens.SearchClearIconTint);
                        if (xSpr != null)
                            UI.AddIconToButton(_settingsFloatFilterClearGo, xSpr, 6f * s, new Color(0f, 0f, 0f, 0f));
                    }
                    catch { }
                    Text clearLabel = _settingsFloatFilterClearGo.GetComponentInChildren<Text>(true);
                    if (clearLabel != null) clearLabel.text = "";
                    var clearHover = _settingsFloatFilterClearGo.AddComponent<UIHoverBorder>();
                    clearHover.hoverColor = new Color(1f, 0.2f, 0.2f, 1f);
                    clearHover.borderSize = 2f;
                    clearHover.inward = true;
                    AddTooltipPlain(_settingsFloatFilterClearGo,
                        VPBTranslation.T("gallery.creator.strip_filter_clear", "Clear filter"));
                }

                _settingsFloatFilterOnValueChanged = val =>
                {
                    settingsFilter = val ?? "";
                    RefreshSettingsFloatFilterClearVisible();
                    RefreshInternalSettingsListRows(true);
                };
                _settingsFloatFilterInput.onValueChanged.AddListener(_settingsFloatFilterOnValueChanged);
                try
                {
                    _settingsFloatFilterInput.gameObject.AddComponent<CtrlBackspaceWordDeleteHandler>()
                        .Initialize(_settingsFloatFilterInput);
                }
                catch { }
                RefreshSettingsFloatFilterClearVisible();
            }

            // Group tabs — wrap to multiple rows (no horizontal clip/scroll).
            _settingsFloatGroupTabsH = chromeSz + 8f * s;
            _settingsFloatGroupTabsRow = UI.CreateChildRT(panel, "GroupTabs", AnchorPresets.hStretchTop,
                new Vector2(0f, _settingsFloatGroupTabsH), new Vector2(0f, -(titleH + filterH)));
            RectTransform groupRT = _settingsFloatGroupTabsRow.GetComponent<RectTransform>();
            if (groupRT != null)
            {
                groupRT.pivot = new Vector2(0.5f, 1f);
                groupRT.sizeDelta = new Vector2(0f, _settingsFloatGroupTabsH);
                groupRT.anchoredPosition = new Vector2(0f, -(titleH + filterH));
            }
            UI.AddImage(_settingsFloatGroupTabsRow, GalleryUiColorTokens.SurfaceDarker);
            GameObject groupContent = new GameObject("Content");
            groupContent.transform.SetParent(_settingsFloatGroupTabsRow.transform, false);
            _settingsFloatGroupTabsContentRT = groupContent.AddComponent<RectTransform>();
            _settingsFloatGroupTabsContentRT.anchorMin = new Vector2(0f, 1f);
            _settingsFloatGroupTabsContentRT.anchorMax = new Vector2(1f, 1f);
            _settingsFloatGroupTabsContentRT.pivot = new Vector2(0f, 1f);
            _settingsFloatGroupTabsContentRT.anchoredPosition = Vector2.zero;
            float pad = SettingsFloatGroupTabPadRef * s;
            _settingsFloatGroupTabsContentRT.offsetMin = new Vector2(pad, -_settingsFloatGroupTabsH);
            _settingsFloatGroupTabsContentRT.offsetMax = new Vector2(-pad, -pad);
            _settingsFloatGroupTabsParent = groupContent.transform;

            // Footer
            _settingsFloatFooter = UI.CreateChildRT(panel, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(_settingsFloatFooter, SettingsFloatFooterBarBg);
            RectTransform footerRT = _settingsFloatFooter.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                _settingsFloatFooter, spacing: 6f * s, padding: UI.Pad(8, 8, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (_settingsFloatFooter.GetComponent<RectMask2D>() == null)
                _settingsFloatFooter.AddComponent<RectMask2D>();

            // Full-footer drag hit (behind Cancel/Save/resize) — same job as title bar.
            GameObject footerDragArea = UI.CreateFloatFooterDragArea(_settingsFloatFooter);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = _settingsFloatPanelRT;
                footerDrag.OnMoved = OnSettingsFloatMoved;
            }

            GameObject footerSpacer = new GameObject("Spacer");
            footerSpacer.transform.SetParent(_settingsFloatFooter.transform, false);
            footerSpacer.AddComponent<RectTransform>();
            UI.AddLE(footerSpacer, flexibleWidth: 1f, minWidth: 8f * s);
            UI.EnsureFloatFooterSpacerDragHit(footerSpacer);
            var spacerDrag = footerSpacer.AddComponent<UIFloatPanelDrag>();
            spacerDrag.Target = _settingsFloatPanelRT;
            spacerDrag.OnMoved = OnSettingsFloatMoved;

            SettingsFloatChromeButton(_settingsFloatFooter.transform, SettingsFloatFooterBtnWRef * s, chromeSz,
                VPBTranslation.T("settings.tbox.cancel", "Cancel"), font, s,
                SettingsFloatCancelBg, () => ExitInternalSettingsMode(false));

            SettingsFloatChromeButton(_settingsFloatFooter.transform, SettingsFloatFooterSaveBtnWRef * s, chromeSz,
                VPBTranslation.T("settings.tbox.save", "Save"), font, s,
                GalleryUiColorTokens.AccentConfirm, () => ExitInternalSettingsMode(true));

            GameObject resizeHandle = UI.AddChildGOImage(
                _settingsFloatFooter, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                chromeSz, chromeSz, Vector2.zero, rounded: true);
            resizeHandle.name = "ResizeHandle";
            Image rhImg = resizeHandle.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            UI.EnsureFloatChromeHoverBorder(resizeHandle);
            LayoutElement rhLe = resizeHandle.GetComponent<LayoutElement>();
            if (rhLe == null) rhLe = resizeHandle.AddComponent<LayoutElement>();
            rhLe.minWidth = rhLe.preferredWidth = chromeSz;
            rhLe.minHeight = rhLe.preferredHeight = chromeSz;
            rhLe.flexibleWidth = 0f;
            try
            {
                Sprite rhSpr = UI.LoadIconSprite("vpb_icons/chevrons_down_right.png", UI.BarIconGlyphTint);
                if (rhSpr != null)
                    UI.AddIconToButton(resizeHandle, rhSpr, 5f * s, UI.IconButtonBackdrop);
            }
            catch { }
            var resizer = resizeHandle.AddComponent<UIFloatPanelResize>();
            resizer.Target = _settingsFloatPanelRT;
            resizer.GetMinSize = () => new Vector2(
                GalleryUiDesignTokens.SettingsFloatMinWidthRef * _settingsFloatChromeScale,
                GalleryUiDesignTokens.SettingsFloatMinHeightRef * _settingsFloatChromeScale);
            resizer.GetMaxSize = () => new Vector2(
                GalleryUiDesignTokens.SettingsFloatMaxWidthRef * _settingsFloatChromeScale,
                GalleryUiDesignTokens.SettingsFloatMaxHeightRef * _settingsFloatChromeScale);
            resizer.OnResized = OnSettingsFloatResized;

            // Scroll list
            _settingsFloatScrollHost = UI.CreateChildRT(panel, "ScrollHost", AnchorPresets.stretchAll);
            RectTransform scrollRT = _settingsFloatScrollHost.GetComponent<RectTransform>();
            if (scrollRT != null)
            {
                scrollRT.offsetMin = new Vector2(0f, footerH);
                scrollRT.offsetMax = new Vector2(0f, -(titleH + filterH + _settingsFloatGroupTabsH));
            }
            UI.AddImage(_settingsFloatScrollHost, SettingsFloatScrollBg);
            if (_settingsFloatScrollHost.GetComponent<RectMask2D>() == null)
                _settingsFloatScrollHost.AddComponent<RectMask2D>();

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            _settingsFloatScrollRect = _settingsFloatScrollHost.AddComponent<ScrollRect>();
            _settingsFloatScrollRect.horizontal = false;
            _settingsFloatScrollRect.vertical = true;
            _settingsFloatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _settingsFloatScrollRect.scrollSensitivity = GalleryUiDesignTokens.SettingsFloatScrollSensitivityRef;
            _settingsFloatScrollRect.verticalScrollbar = null;

            GameObject viewport = UI.CreateChildRT(_settingsFloatScrollHost, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            if (vpRt != null) vpRt.offsetMax = new Vector2(-sbW, 0f);
            viewport.AddComponent<RectMask2D>();
            _settingsFloatScrollRect.viewport = vpRt;

            GameObject scrollbarGO = UI.CreateScrollBar(_settingsFloatScrollHost, sbW, 0f, Scrollbar.Direction.BottomToTop);
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            if (sbRT != null)
            {
                sbRT.anchorMin = new Vector2(1f, 0f);
                sbRT.anchorMax = new Vector2(1f, 1f);
                sbRT.pivot = new Vector2(1f, 0.5f);
                sbRT.offsetMin = new Vector2(-sbW, 0f);
                sbRT.offsetMax = Vector2.zero;
            }
            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = _settingsFloatScrollRect;
            sync.scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            sync.minSizePixels = 20f;

            GameObject content = UI.CreateChildRT(viewport, "Content", AnchorPresets.hStretchTop);
            RectTransform contentRt = content.GetComponent<RectTransform>();
            _settingsFloatScrollRect.content = contentRt;
            VerticalLayoutGroup cv = UI.AddVLG(content, spacing: 2f * s, padding: UI.Pad(6, 6, 6, 6, s));
            cv.childForceExpandHeight = false;
            cv.childForceExpandWidth = true;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _settingsFloatRowsParent = content.transform;

            _settingsFloatCollapsed = false;
            _settingsFloatExpandHeightRef = panelHRef;
            try { SyncSettingsSideSearchInputFromFilter(); } catch { }
            RebuildSettingsFloatGroupTabs(font, s, chromeSz);
            RebuildSettingsFloatRows(font, s, rowH, false);
            try { UI.ApplyFloatRootHoverPolicy(_settingsFloatRoot); } catch { }
            _settingsFloatRoot.SetActive(false);
        }

        private void RebuildSettingsFloatGroupTabs(int font, float s, float chromeSz)
        {
            if (_settingsFloatGroupTabsParent == null) return;
            for (int c = _settingsFloatGroupTabsParent.childCount - 1; c >= 0; c--)
            {
                try
                {
                    GameObject go = _settingsFloatGroupTabsParent.GetChild(c).gameObject;
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
                catch { }
            }

            string filterNow = (CanonicalSettingsSideSearchText() ?? "").Trim();
            bool MatchFilter(string label) =>
                string.IsNullOrEmpty(filterNow) || (label ?? "").IndexOf(filterNow, StringComparison.OrdinalIgnoreCase) >= 0;

            void AddChip(string key, string label)
            {
                if (!string.Equals(key, "all", StringComparison.OrdinalIgnoreCase) && !MatchFilter(label)) return;
                bool active = string.Equals(currentSettingsGroup, key, StringComparison.OrdinalIgnoreCase);
                Color bg = active ? SettingsFloatGroupActive : SettingsFloatGroupInactive;
                string capturedKey = key;
                GameObject btn = UI.CreateChromeLayoutButton(
                    _settingsFloatGroupTabsParent, 0f, chromeSz, label, font, bg,
                    () =>
                    {
                        currentSettingsGroup = capturedKey;
                        try { CancelPluginHotkeyCapture(false); } catch { }
                        RefreshInternalSettingsListRows(true);
                    });
                if (btn != null)
                {
                    LayoutElement le = btn.GetComponent<LayoutElement>();
                    if (le == null) le = btn.AddComponent<LayoutElement>();
                    le.flexibleWidth = 0f;
                    le.minWidth = 48f * s;
                    le.preferredHeight = chromeSz;
                    le.minHeight = chromeSz;
                    Text chipTxt = btn.GetComponentInChildren<Text>(true);
                    float padX = 16f * s;
                    float textW = chipTxt != null ? chipTxt.preferredWidth : 0f;
                    le.preferredWidth = Mathf.Max(48f * s, textW + padX);
                }
            }

            AddChip("all", VPBTranslation.T("settings.group.all", "All"));
            List<SettingsGroupTab> tabs = GetSettingsGroupTabs();
            for (int i = 0; i < tabs.Count; i++)
            {
                SettingsGroupTab g = tabs[i];
                if (g == null) continue;
                AddChip(g.Key, g.Label);
            }

            RelayoutSettingsFloatGroupTabs(s, chromeSz);
            try { UI.ApplyFloatRootHoverPolicy(_settingsFloatGroupTabsParent != null ? _settingsFloatGroupTabsParent.gameObject : _settingsFloatRoot); } catch { }
        }

        private void RefreshSettingsFloatFilterClearVisible()
        {
            if (_settingsFloatFilterClearGo == null) return;
            bool show = _settingsFloatFilterInput != null
                && !string.IsNullOrEmpty(_settingsFloatFilterInput.text);
            if (_settingsFloatFilterClearGo.activeSelf != show)
                _settingsFloatFilterClearGo.SetActive(show);
        }

        /// <summary>
        /// Manual wrap for category chips (FilterChips / StripKeep pattern).
        /// Grows GroupTabs row height; updates scroll host top inset.
        /// </summary>
        private void RelayoutSettingsFloatGroupTabs(float s, float chromeSz)
        {
            if (_settingsFloatGroupTabsContentRT == null || _settingsFloatGroupTabsRow == null) return;

            float rowH = chromeSz;
            float colSpacing = SettingsFloatGroupTabRowGapRef * s;
            float rowSpacing = SettingsFloatGroupTabRowGapRef * s;
            float pad = SettingsFloatGroupTabPadRef * s;

            float availW = 0f;
            try
            {
                if (_settingsFloatPanelRT != null)
                    availW = _settingsFloatPanelRT.rect.width - pad * 2f;
            }
            catch { }
            if (availW <= 1f)
            {
                try { availW = _settingsFloatGroupTabsContentRT.rect.width; } catch { availW = 0f; }
            }
            if (availW <= 1f) availW = 400f * s;

            float x = 0f;
            float y = 0f;
            int rows = 1;
            int n = _settingsFloatGroupTabsContentRT.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform child = _settingsFloatGroupTabsContentRT.GetChild(i);
                if (child == null || !child.gameObject.activeSelf) continue;
                RectTransform rt = child as RectTransform;
                if (rt == null) continue;

                float w = 0f;
                LayoutElement le = child.GetComponent<LayoutElement>();
                if (le != null && le.preferredWidth > 1f) w = le.preferredWidth;
                if (w <= 1f)
                {
                    try { LayoutRebuilder.ForceRebuildLayoutImmediate(rt); } catch { }
                    w = LayoutUtility.GetPreferredWidth(rt);
                }
                if (w <= 1f) w = rt.sizeDelta.x;
                if (w <= 1f) w = 48f * s;

                if (x > 0f && x + w > availW + 0.5f)
                {
                    x = 0f;
                    y -= rowH + rowSpacing;
                    rows++;
                }

                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, y);
                rt.sizeDelta = new Vector2(w, rowH);

                x += w + colSpacing;
            }

            float totalH = rows * rowH + (rows - 1) * rowSpacing + pad * 2f;
            if (totalH < chromeSz + 8f * s) totalH = chromeSz + 8f * s;
            _settingsFloatGroupTabsH = totalH;

            RectTransform groupRT = _settingsFloatGroupTabsRow.GetComponent<RectTransform>();
            if (groupRT != null)
                groupRT.sizeDelta = new Vector2(0f, totalH);

            _settingsFloatGroupTabsContentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalH);
            _settingsFloatGroupTabsContentRT.offsetMin = new Vector2(pad, -totalH);
            _settingsFloatGroupTabsContentRT.offsetMax = new Vector2(-pad, -pad);

            ApplySettingsFloatScrollHostInsets(s);
        }

        private void ApplySettingsFloatScrollHostInsets(float s)
        {
            if (_settingsFloatScrollHost == null) return;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float groupH = _settingsFloatGroupTabsH > 1f ? _settingsFloatGroupTabsH : (chromeSz + 8f * s);

            RectTransform scrollRT = _settingsFloatScrollHost.GetComponent<RectTransform>();
            if (scrollRT == null) return;
            scrollRT.offsetMin = new Vector2(0f, footerH);
            scrollRT.offsetMax = new Vector2(0f, -(titleH + filterH + groupH));

            // Keep GroupTabs under title+filter when height changes.
            if (_settingsFloatGroupTabsRow != null)
            {
                RectTransform groupRT = _settingsFloatGroupTabsRow.GetComponent<RectTransform>();
                if (groupRT != null)
                    groupRT.anchoredPosition = new Vector2(0f, -(titleH + filterH));
            }
        }

        private void RebuildSettingsFloatRows(int font, float s, float rowH, bool keepScroll)
        {
            if (_settingsFloatRowsParent == null) return;

            float scrollPos = 1f;
            if (keepScroll && _settingsFloatScrollRect != null)
                scrollPos = _settingsFloatScrollRect.verticalNormalizedPosition;

            for (int c = _settingsFloatRowsParent.childCount - 1; c >= 0; c--)
            {
                try { UnityEngine.Object.Destroy(_settingsFloatRowsParent.GetChild(c).gameObject); } catch { }
            }

            List<FileEntry> rows = BuildInternalSettingsRows();
            for (int i = 0; i < rows.Count; i++)
            {
                InternalSettingRowEntry row = rows[i] as InternalSettingRowEntry;
                if (row == null) continue;
                InternalSettingDefinition def = GetInternalSettingDefinition(row.RowKey);
                if (def == null) continue;
                BuildSettingsFloatRow(row, def, font, s, rowH);
            }

            if (keepScroll && _settingsFloatScrollRect != null)
                _settingsFloatScrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollPos);
            try { UI.ApplyFloatRootHoverPolicy(_settingsFloatRoot); } catch { }
        }

        private void BuildSettingsFloatRow(InternalSettingRowEntry row, InternalSettingDefinition def, int font, float s, float rowH)
        {
            float effectiveRowH = rowH;
            if (def != null
                && def.ControlType == InternalSettingControlType.TextArea
                && !string.Equals(def.Key, "quick.categoryEditor", StringComparison.OrdinalIgnoreCase))
            {
                effectiveRowH = GalleryUiDesignTokens.SettingsFloatTextAreaRowHeightRef * s;
            }

            GameObject rowGO = new GameObject("SettingsRow_" + (row.RowKey ?? "row"));
            rowGO.transform.SetParent(_settingsFloatRowsParent, false);
            UI.AddLE(rowGO, minHeight: effectiveRowH, preferredHeight: effectiveRowH, flexibleWidth: 1f);
            Image rowBg = UI.AddImage(rowGO, SettingsFloatRowBg);

            GameObject listRowGO = new GameObject("ListRow");
            listRowGO.transform.SetParent(rowGO.transform, false);
            listRowGO.SetActive(true);
            RectTransform listRowRT = listRowGO.AddComponent<RectTransform>();
            listRowRT.anchorMin = Vector2.zero;
            listRowRT.anchorMax = Vector2.one;
            listRowRT.offsetMin = new Vector2(8f * s, 2f * s);
            listRowRT.offsetMax = new Vector2(-8f * s, -2f * s);

            HorizontalLayoutGroup listHlg = UI.AddHLG(
                listRowGO, spacing: 8f * s, padding: UI.Pad(2, 2, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: true);

            Text nameText = UI.CreateLabel(
                listRowGO, def.Label ?? row.RowKey ?? "", GalleryUiDesignTokens.SettingsListRowNameFontRef,
                Color.white, TextAnchor.MiddleLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "Name");
            GalleryUiMetrics.ApplyFont(nameText, GalleryUiDesignTokens.SettingsListRowNameFontRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(nameText.gameObject, flexibleWidth: 0.45f, minWidth: 80f * s, preferredHeight: effectiveRowH * 0.9f);

            GameObject detailsRowGO = new GameObject("Details");
            detailsRowGO.transform.SetParent(listRowGO.transform, false);
            UI.AddHLG(detailsRowGO, spacing: 6f * s, childAlignment: TextAnchor.MiddleRight, childForceExpandWidth: false);
            UI.AddLE(detailsRowGO, flexibleWidth: 0.55f, minHeight: effectiveRowH * 0.85f, preferredHeight: effectiveRowH * 0.85f);

            RebuildSettingsRowControls(rowGO, def);
        }

        internal InputField GetSettingsFloatFilterInput()
        {
            return _settingsFloatFilterInput;
        }

        internal void SetSettingsFloatFilterTextWithoutNotify(string text)
        {
            if (_settingsFloatFilterInput == null) return;
            string t = text ?? "";
            if (_settingsFloatFilterInput.text == t)
            {
                RefreshSettingsFloatFilterClearVisible();
                return;
            }
            if (_settingsFloatFilterOnValueChanged != null)
            {
                _settingsFloatFilterInput.onValueChanged.RemoveListener(_settingsFloatFilterOnValueChanged);
                _settingsFloatFilterInput.text = t;
                _settingsFloatFilterInput.onValueChanged.AddListener(_settingsFloatFilterOnValueChanged);
            }
            else
            {
                _settingsFloatFilterInput.text = t;
            }
            RefreshSettingsFloatFilterClearVisible();
        }

        private void ToggleSettingsFloatCollapsed()
        {
            if (_settingsFloatPanelRT == null) return;
            float s = _settingsFloatChromeScale > 0f ? _settingsFloatChromeScale : 1f;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;

            if (!_settingsFloatCollapsed)
            {
                CaptureSettingsFloatGeometryToMemory();
                _settingsFloatExpandHeightRef = _settingsFloatSavedSizeRef.HasValue
                    ? _settingsFloatSavedSizeRef.Value.y
                    : GalleryUiDesignTokens.SettingsFloatDefaultHeightRef;
                _settingsFloatCollapsedTopLeftPos = _settingsFloatPanelRT.anchoredPosition;
                _settingsFloatCollapsed = true;
            }
            else
            {
                RestoreSettingsFloatExpandHeightIntoSavedSize();
                _settingsFloatCollapsed = false;
                _settingsFloatCollapsedTopLeftPos = null;
            }

            SyncSettingsFloatCollapseChrome(titleH);
            CaptureSettingsFloatGeometryToMemory();
            PersistSettingsFloatGeometry();
        }

        private void RestoreSettingsFloatExpandHeightIntoSavedSize()
        {
            float h = _settingsFloatExpandHeightRef.HasValue
                ? _settingsFloatExpandHeightRef.Value
                : GalleryUiDesignTokens.SettingsFloatDefaultHeightRef;
            h = Mathf.Clamp(h,
                GalleryUiDesignTokens.SettingsFloatMinHeightRef,
                GalleryUiDesignTokens.SettingsFloatMaxHeightRef);
            float w = _settingsFloatSavedSizeRef.HasValue
                ? _settingsFloatSavedSizeRef.Value.x
                : GalleryUiDesignTokens.SettingsFloatDefaultWidthRef;
            _settingsFloatSavedSizeRef = new Vector2(
                Mathf.Clamp(w, GalleryUiDesignTokens.SettingsFloatMinWidthRef, GalleryUiDesignTokens.SettingsFloatMaxWidthRef),
                h);
        }

        private void SyncSettingsFloatCollapseChrome(float titleH)
        {
            if (_settingsFloatFilterRow != null) _settingsFloatFilterRow.SetActive(!_settingsFloatCollapsed);
            if (_settingsFloatGroupTabsRow != null) _settingsFloatGroupTabsRow.SetActive(!_settingsFloatCollapsed);
            if (_settingsFloatScrollHost != null) _settingsFloatScrollHost.SetActive(!_settingsFloatCollapsed);
            if (_settingsFloatFooter != null) _settingsFloatFooter.SetActive(!_settingsFloatCollapsed);

            if (_settingsFloatCollapseIcon != null)
            {
                string path = _settingsFloatCollapsed ? "vpb_icons/chevron_down.png" : "vpb_icons/chevron_up.png";
                Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
                if (spr != null)
                {
                    _settingsFloatCollapseIcon.sprite = spr;
                    _settingsFloatCollapseIcon.color = Color.white;
                }
            }

            if (_settingsFloatPanelRT != null)
            {
                float s = _settingsFloatChromeScale > 0f ? _settingsFloatChromeScale : 1f;
                if (_settingsFloatCollapsed)
                {
                    Vector2 size = _settingsFloatPanelRT.sizeDelta;
                    size.y = titleH;
                    _settingsFloatPanelRT.sizeDelta = size;
                    if (_settingsFloatCollapsedTopLeftPos.HasValue)
                        _settingsFloatPanelRT.anchoredPosition = _settingsFloatCollapsedTopLeftPos.Value;
                }
                else if (_settingsFloatSavedSizeRef.HasValue)
                {
                    _settingsFloatPanelRT.sizeDelta = new Vector2(
                        _settingsFloatSavedSizeRef.Value.x * s,
                        _settingsFloatSavedSizeRef.Value.y * s);
                }
            }
        }

        /// <summary>Esc ladder: clear filter → Cancel+close. Must gate on GetKeyDown (warm Update).</summary>
        internal bool TryHandleSettingsFloatEsc()
        {
            if (!IsSettingsPanelOpen()) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;

            if (_settingsFloatFilterInput != null
                && !string.IsNullOrEmpty(_settingsFloatFilterInput.text))
            {
                SetSettingsFloatFilterTextWithoutNotify("");
                settingsFilter = "";
                RefreshInternalSettingsListRows(true);
                return true;
            }

            ExitInternalSettingsMode(false);
            return true;
        }

        /// <summary>
        /// Live ChromeScale adapt — resize shell + rebuild rows once.
        /// Never Destroy+Build (that recreated every row/hover rim and hitch on scale hotkeys).
        /// </summary>
        private void RescaleSettingsFloatIfOpen(float chromeScale)
        {
            if (!IsSettingsPanelOpen()) return;
            if (_settingsFloatPanelRT == null) return;

            float s = chromeScale > 0f ? chromeScale : ChromeScale;
            if (s <= 0.01f) s = 1f;
            if (Mathf.Abs(s - _settingsFloatChromeScale) < 0.0005f) return;

            try { CommitSettingsSideSearchIntoFilter(); } catch { }

            // Convert current size → ref units with OLD scale before swapping factor.
            CaptureSettingsFloatGeometryToMemory();
            _settingsFloatChromeScale = s;

            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef * s;

            float panelWRef = _settingsFloatSavedSizeRef.HasValue
                ? _settingsFloatSavedSizeRef.Value.x
                : GalleryUiDesignTokens.SettingsFloatDefaultWidthRef;
            float panelHRef = _settingsFloatSavedSizeRef.HasValue
                ? _settingsFloatSavedSizeRef.Value.y
                : GalleryUiDesignTokens.SettingsFloatDefaultHeightRef;
            panelWRef = Mathf.Clamp(panelWRef, GalleryUiDesignTokens.SettingsFloatMinWidthRef, GalleryUiDesignTokens.SettingsFloatMaxWidthRef);
            panelHRef = Mathf.Clamp(panelHRef, GalleryUiDesignTokens.SettingsFloatMinHeightRef, GalleryUiDesignTokens.SettingsFloatMaxHeightRef);

            // Pivot top-left: resize sizeDelta only — keep anchoredPosition (title corner fixed).
            Vector2 keepTopLeft = _settingsFloatPanelRT.anchoredPosition;

            if (_settingsFloatCollapsed)
            {
                _settingsFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, titleH);
            }
            else
            {
                _settingsFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, panelHRef * s);
                _settingsFloatExpandHeightRef = panelHRef;
            }
            _settingsFloatPanelRT.anchoredPosition = keepTopLeft;

            if (_settingsFloatTitleBarRT != null)
            {
                _settingsFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
                UI.LayoutFloatTitleWindowIcon(
                    _settingsFloatTitleBarRT.gameObject,
                    GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                HorizontalLayoutGroup titleHlg = _settingsFloatTitleBarRT.GetComponent<HorizontalLayoutGroup>();
                Transform gripTr = _settingsFloatTitleBarRT.Find("Grip");
                UI.ApplyFloatTitleBarMetrics(
                    titleHlg, gripTr != null ? gripTr.gameObject : null, s);
            }

            if (_settingsFloatFilterRow != null)
            {
                RectTransform filterRT = _settingsFloatFilterRow.GetComponent<RectTransform>();
                if (filterRT != null)
                {
                    filterRT.sizeDelta = new Vector2(0f, filterH);
                    filterRT.anchoredPosition = new Vector2(0f, -titleH);
                }
                HorizontalLayoutGroup filterHlg = _settingsFloatFilterRow.GetComponent<HorizontalLayoutGroup>();
                if (filterHlg != null)
                {
                    filterHlg.padding = UI.Pad(
                        GalleryUiDesignTokens.FloatSearchRowPadRef,
                        GalleryUiDesignTokens.FloatSearchRowPadRef,
                        GalleryUiDesignTokens.FloatSearchRowPadRef,
                        GalleryUiDesignTokens.FloatSearchRowPadRef, s);
                }
            }

            if (_settingsFloatFilterInput != null)
            {
                LayoutElement inputLe = _settingsFloatFilterInput.GetComponent<LayoutElement>();
                if (inputLe != null)
                {
                    inputLe.minHeight = searchH;
                    inputLe.preferredHeight = searchH;
                }

                float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                float padY = 2f * s;
                UI.LayoutChromeSearchIcon(_settingsFloatFilterInput.gameObject, s);
                Transform textAreaTr = _settingsFloatFilterInput.transform.Find("TextArea");
                if (textAreaTr != null)
                {
                    RectTransform taRt = textAreaTr as RectTransform;
                    if (taRt != null)
                    {
                        taRt.offsetMin = new Vector2(taRt.offsetMin.x, padY);
                        taRt.offsetMax = new Vector2(-clearSz, -padY);
                    }
                }

                if (_settingsFloatFilterInput.textComponent != null)
                    GalleryUiMetrics.ApplyFont(_settingsFloatFilterInput.textComponent, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                Text ph = _settingsFloatFilterInput.placeholder as Text;
                if (ph != null)
                    GalleryUiMetrics.ApplyFont(ph, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);

                if (_settingsFloatFilterClearGo != null)
                {
                    RectTransform clearRT = _settingsFloatFilterClearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                        clearRT.sizeDelta = new Vector2(clearSz, searchH);
                }
            }

            if (_settingsFloatFooter != null)
            {
                RectTransform footerRT = _settingsFloatFooter.GetComponent<RectTransform>();
                if (footerRT != null)
                    footerRT.sizeDelta = new Vector2(0f, footerH);
            }

            RescaleSettingsFloatSquareChrome(_settingsFloatCollapseBtn, chromeSz, s);
            if (_settingsFloatTitleBarRT != null)
            {
                Transform closeTr = _settingsFloatTitleBarRT.Find("TitleClose");
                if (closeTr != null)
                    RescaleSettingsFloatSquareChrome(closeTr.gameObject, chromeSz, s);
            }

            SyncSettingsFloatCollapseChrome(titleH);
            try { SyncSettingsSideSearchInputFromFilter(); } catch { }
            // One row/tab rebuild at new scale — shell reused.
            RefreshInternalSettingsListRows(true);
            // Refresh saved center from kept top-left + new size (warm path; no alloc).
            CaptureSettingsFloatGeometryToMemory();
            PersistSettingsFloatGeometry();
        }

        private static void RescaleSettingsFloatSquareChrome(GameObject go, float size, float scale)
        {
            UI.ScaleFloatChromeIconButton(go, size, scale);
        }

        private void LoadSettingsFloatGeometryFromConfig()
        {
            _settingsFloatSavedPosCenter = null;
            _settingsFloatSavedSizeRef = null;
            try
            {
                if (VPBConfig.Instance == null) return;
                if (VPBConfig.Instance.GallerySettingsFloatPosSaved)
                {
                    _settingsFloatSavedPosCenter = new Vector2(
                        VPBConfig.Instance.GallerySettingsFloatPosX,
                        VPBConfig.Instance.GallerySettingsFloatPosY);
                }
                if (VPBConfig.Instance.GallerySettingsFloatSizeSaved)
                {
                    float w = VPBConfig.Instance.GallerySettingsFloatWidthRef;
                    float h = VPBConfig.Instance.GallerySettingsFloatHeightRef;
                    if (w >= GalleryUiDesignTokens.SettingsFloatMinWidthRef
                        && h >= GalleryUiDesignTokens.SettingsFloatMinHeightRef)
                    {
                        _settingsFloatSavedSizeRef = new Vector2(
                            Mathf.Clamp(w, GalleryUiDesignTokens.SettingsFloatMinWidthRef, GalleryUiDesignTokens.SettingsFloatMaxWidthRef),
                            Mathf.Clamp(h, GalleryUiDesignTokens.SettingsFloatMinHeightRef, GalleryUiDesignTokens.SettingsFloatMaxHeightRef));
                    }
                }
            }
            catch { }
        }

        private void CaptureSettingsFloatGeometryToMemory()
        {
            if (_settingsFloatPanelRT == null) return;
            float s = _settingsFloatChromeScale > 0f ? _settingsFloatChromeScale : 1f;
            _settingsFloatSavedPosCenter = SettingsFloatTopLeftToCenter(
                _settingsFloatPanelRT.anchoredPosition, _settingsFloatPanelRT.sizeDelta);
            if (!_settingsFloatCollapsed)
            {
                _settingsFloatSavedSizeRef = new Vector2(
                    Mathf.Clamp(_settingsFloatPanelRT.sizeDelta.x / s, GalleryUiDesignTokens.SettingsFloatMinWidthRef, GalleryUiDesignTokens.SettingsFloatMaxWidthRef),
                    Mathf.Clamp(_settingsFloatPanelRT.sizeDelta.y / s, GalleryUiDesignTokens.SettingsFloatMinHeightRef, GalleryUiDesignTokens.SettingsFloatMaxHeightRef));
            }
        }

        private void PersistSettingsFloatGeometry()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                if (_settingsFloatSavedPosCenter.HasValue)
                {
                    VPBConfig.Instance.GallerySettingsFloatPosSaved = true;
                    VPBConfig.Instance.GallerySettingsFloatPosX = _settingsFloatSavedPosCenter.Value.x;
                    VPBConfig.Instance.GallerySettingsFloatPosY = _settingsFloatSavedPosCenter.Value.y;
                }
                if (_settingsFloatSavedSizeRef.HasValue)
                {
                    VPBConfig.Instance.GallerySettingsFloatSizeSaved = true;
                    VPBConfig.Instance.GallerySettingsFloatWidthRef = _settingsFloatSavedSizeRef.Value.x;
                    VPBConfig.Instance.GallerySettingsFloatHeightRef = _settingsFloatSavedSizeRef.Value.y;
                }
            }
            catch { return; }
            try { ScheduleQuickFiltersConfigSave(); } catch { }
        }

        private void OnSettingsFloatMoved()
        {
            CaptureSettingsFloatGeometryToMemory();
            PersistSettingsFloatGeometry();
        }

        private void OnSettingsFloatResized()
        {
            if (_settingsFloatCollapsed) return;
            float s = _settingsFloatChromeScale > 0f ? _settingsFloatChromeScale : 1f;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            try { RelayoutSettingsFloatGroupTabs(s, chromeSz); } catch { }
            CaptureSettingsFloatGeometryToMemory();
            PersistSettingsFloatGeometry();
        }

        private static Vector2 SettingsFloatCenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 SettingsFloatTopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private static GameObject SettingsFloatSquareIconButton(
            Transform parent, float size, string iconPath, Color backdrop, UnityAction onClick)
        {
            return UI.CreateFloatChromeIconButton(parent, size, iconPath, backdrop, onClick);
        }

        private static GameObject SettingsFloatChromeButton(
            Transform parent, float width, float height, string label, int font, float s,
            Color bg, UnityAction onClick)
        {
            GameObject go = UI.CreateChromeLayoutButton(parent, width, height, label, font, bg, onClick);
            LayoutElement le = go != null ? go.GetComponent<LayoutElement>() : null;
            if (le == null && go != null) le = go.AddComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = width;
                le.preferredWidth = width;
                le.minHeight = height;
                le.preferredHeight = height;
            }
            return go;
        }
    }
}
