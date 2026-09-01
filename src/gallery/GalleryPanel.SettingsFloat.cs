using System;
using System.Collections;
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
        private const float SettingsFloatFooterCloseBtnWRef = 88f;
        private const float SettingsFloatSidebarPadRef = GalleryUiDesignTokens.TightGapRef;
        private const float SettingsFloatSidebarRowGapRef = GalleryUiDesignTokens.HairGapRef;
        private const float SettingsFloatGroupChipIconSizeRef = GalleryUiDesignTokens.RegionGapRef;
        private const float SettingsFloatGroupChipIconGapRef = GalleryUiDesignTokens.TightGapRef;
        private const float SettingsFloatGroupChipPadLRef = GalleryUiDesignTokens.TightGapRef;
        private const float SettingsFloatGroupChipPadRRef = GalleryUiDesignTokens.ControlGapRef;
        private const float SettingsFloatRowsSpacingRef = GalleryUiDesignTokens.HairGapRef;
        private const float SettingsFloatRowsPadRef = GalleryUiDesignTokens.TightGapRef;

        // Row build is windowed: the full list is ~165 definitions and a single row costs ~15 GameObjects
        // (rounded backgrounds, mini buttons, slider parts, hover borders), so building them all in one
        // frame is a multi-hundred-ms stall. Only rows covering the viewport are built synchronously;
        // the rest stand in as exact-height empty placeholders so scroll range and position never shift,
        // then stream in on a per-frame budget.
        private const int SettingsFloatDeferredRowBudgetMs = 4;
        private const int SettingsFloatRowOverscan = 2;

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

        private Coroutine _settingsFloatRowsDeferCo;
        private readonly List<InternalSettingRowEntry> _settingsFloatRowQueue = new List<InternalSettingRowEntry>(192);
        private readonly List<float> _settingsFloatRowHeights = new List<float>(192);
        private readonly System.Diagnostics.Stopwatch _settingsFloatRowsClock = new System.Diagnostics.Stopwatch();
        private GameObject _settingsFloatRowsLeadPad;
        private GameObject _settingsFloatRowsTailPad;
        private int _settingsFloatRowsBuiltFirst;
        private int _settingsFloatRowsBuiltLast = -1;
        private int _settingsFloatRowsFont;
        private float _settingsFloatRowsScale = 1f;
        private float _settingsFloatRowsBaseHeight;
        private float _settingsFloatRowsSpacing;

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
            StopSettingsFloatDeferredRows();
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
            StopSettingsFloatDeferredRows();
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
            _settingsFloatModifiedBtn = null;
            _settingsFloatModifiedBtnBg = null;
            _settingsFloatModifiedBtnText = null;
            _settingsFloatEmptyGo = null;
            _settingsFloatEmptyText = null;
            _settingsFloatEmptyClearBtn = null;
            _settingsFloatRevertBtn = null;
            _settingsFloatCloseBtn = null;
            _settingsFloatGroupTabsH = 0f;
        }

        private void StopSettingsFloatDeferredRows()
        {
            StopCo(ref _settingsFloatRowsDeferCo);
            _settingsFloatRowsClock.Reset();
            _settingsFloatRowQueue.Clear();
            _settingsFloatRowHeights.Clear();
            _settingsFloatRowsLeadPad = null;
            _settingsFloatRowsTailPad = null;
            _settingsFloatRowsBuiltFirst = 0;
            _settingsFloatRowsBuiltLast = -1;
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
            UI.CreateFloatTitleWindowIcon(titleBar, "settings", winIconSz);

            Text title = UI.CreateEmphasisTitleLabel(
                titleBar,
                VPBTranslation.T("settings.title", "Settings"),
                font, Color.white, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(title.gameObject, flexibleWidth: 1f, minWidth: 60f * s);

            _settingsFloatCollapseBtn = SettingsFloatSquareIconButton(
                titleBar.transform, chromeSz, "chevron-up",
                GalleryUiColorTokens.ChromeIconWell, ToggleSettingsFloatCollapsed);
            if (_settingsFloatCollapseBtn != null)
            {
                _settingsFloatCollapseBtn.name = "CollapseBtn";
                Transform iconTr = _settingsFloatCollapseBtn.transform.Find("Icon");
                _settingsFloatCollapseIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }

            GameObject closeBtn = SettingsFloatSquareIconButton(
                titleBar.transform, chromeSz, "x",
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
            UI.AddHLG(_settingsFloatFilterRow, spacing: UI.GapTight(s),
                padding: UI.Pad(
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef, s),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            _settingsFloatFilterInput = UI.CreateChromeLayoutInputField(
                _settingsFloatFilterRow.transform,
                font,
                searchH,
                1f,
                8f * s,
                2f * s,
                SettingsFloatFilterBg,
                UI.InputFieldPlaceholderColor,
                VPBTranslation.T("settings.search.placeholder", "Search settings…"),
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
                        Sprite xSpr = UI.LoadIconSprite("x", GalleryUiColorTokens.SearchClearIconTint);
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
                    AddTooltip(_settingsFloatFilterClearGo, "gallery.creator.strip_filter_clear", "Clear filter");
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

            _settingsFloatModifiedBtn = UI.CreateUIButton(
                _settingsFloatFilterRow, chromeSz, chromeSz, "M", font, 0, 0, AnchorPresets.middleCenter, null);
            if (_settingsFloatModifiedBtn != null)
            {
                _settingsFloatModifiedBtn.name = "ModifiedOnly";
                UI.AddLE(_settingsFloatModifiedBtn, minWidth: chromeSz, preferredWidth: chromeSz,
                    minHeight: chromeSz, preferredHeight: chromeSz, flexibleWidth: 0f);
                _settingsFloatModifiedBtnBg = _settingsFloatModifiedBtn.GetComponent<Image>();
                _settingsFloatModifiedBtnText = _settingsFloatModifiedBtn.GetComponentInChildren<Text>(true);
                if (_settingsFloatModifiedBtnText != null)
                    _settingsFloatModifiedBtnText.text = VPBTranslation.T("settings.modified_only.abbrev", "≠");
                Button mb = _settingsFloatModifiedBtn.GetComponent<Button>();
                if (mb != null)
                {
                    mb.onClick.RemoveAllListeners();
                    mb.onClick.AddListener(ToggleSettingsModifiedOnly);
                }
                AddTooltip(_settingsFloatModifiedBtn, "settings.modified_only.tip",
                    "Show only settings that differ from defaults");
                SyncSettingsModifiedOnlyButton();
            }

            // Left category list — stable nav (never hidden by search).
            float sidebarW = GalleryUiDesignTokens.SettingsFloatSidebarWidthRef * s;
            _settingsFloatGroupTabsH = sidebarW;
            _settingsFloatGroupTabsRow = UI.CreateChildRT(panel, "Sidebar", AnchorPresets.vStretchLeft,
                new Vector2(sidebarW, 0f), Vector2.zero);
            RectTransform groupRT = _settingsFloatGroupTabsRow.GetComponent<RectTransform>();
            if (groupRT != null)
            {
                groupRT.anchorMin = new Vector2(0f, 0f);
                groupRT.anchorMax = new Vector2(0f, 1f);
                groupRT.pivot = new Vector2(0f, 1f);
                groupRT.offsetMin = new Vector2(0f, footerH);
                groupRT.offsetMax = new Vector2(sidebarW, -(titleH + filterH));
                groupRT.sizeDelta = new Vector2(sidebarW, 0f);
            }
            UI.AddImage(_settingsFloatGroupTabsRow, GalleryUiColorTokens.SurfaceDarker);
            GameObject groupContent = new GameObject("Content");
            groupContent.transform.SetParent(_settingsFloatGroupTabsRow.transform, false);
            _settingsFloatGroupTabsContentRT = groupContent.AddComponent<RectTransform>();
            _settingsFloatGroupTabsContentRT.anchorMin = Vector2.zero;
            _settingsFloatGroupTabsContentRT.anchorMax = Vector2.one;
            _settingsFloatGroupTabsContentRT.offsetMin = Vector2.zero;
            _settingsFloatGroupTabsContentRT.offsetMax = Vector2.zero;
            _settingsFloatGroupTabsParent = groupContent.transform;
            VerticalLayoutGroup sidebarVlg = UI.AddVLG(
                groupContent,
                spacing: SettingsFloatSidebarRowGapRef * s,
                padding: UI.Pad(SettingsFloatSidebarPadRef, SettingsFloatSidebarPadRef, SettingsFloatSidebarPadRef, SettingsFloatSidebarPadRef, s));
            sidebarVlg.childForceExpandWidth = true;
            sidebarVlg.childForceExpandHeight = false;
            sidebarVlg.childControlWidth = true;
            sidebarVlg.childControlHeight = true;
            sidebarVlg.childAlignment = TextAnchor.UpperLeft;

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
                _settingsFloatFooter, spacing: UI.GapTight(s), padding: UI.PadFloatFooter(s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (_settingsFloatFooter.GetComponent<RectMask2D>() == null)
                _settingsFloatFooter.AddComponent<RectMask2D>();

            // Full-footer drag hit (behind Revert/Close/resize) — same job as title bar.
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

            _settingsFloatRevertBtn = SettingsFloatChromeButton(_settingsFloatFooter.transform, SettingsFloatFooterBtnWRef * s, chromeSz,
                VPBTranslation.T("settings.footer.revert", "Revert"), font, s,
                SettingsFloatCancelBg, RevertInternalSettingsSession);
            if (_settingsFloatRevertBtn != null)
            {
                _settingsFloatRevertBtn.name = "FooterRevert";
                AddTooltip(_settingsFloatRevertBtn, "settings.footer.revert.tip",
                    "Restore values from when this window opened");
            }

            _settingsFloatCloseBtn = SettingsFloatChromeButton(_settingsFloatFooter.transform, SettingsFloatFooterCloseBtnWRef * s, chromeSz,
                VPBTranslation.T("settings.footer.close", "Close"), font, s,
                SettingsFloatGroupActive, () => ExitInternalSettingsMode(true));
            if (_settingsFloatCloseBtn != null)
            {
                _settingsFloatCloseBtn.name = "FooterClose";
                AddTooltip(_settingsFloatCloseBtn, "settings.footer.close.tip",
                    "Keep changes and close");
            }

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
                Sprite rhSpr = UI.LoadIconSprite("chevrons-down-right", UI.BarIconGlyphTint);
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
                scrollRT.offsetMin = new Vector2(sidebarW, footerH);
                scrollRT.offsetMax = new Vector2(0f, -(titleH + filterH));
            }
            UI.AddImage(_settingsFloatScrollHost, SettingsFloatScrollBg);
            if (_settingsFloatScrollHost.GetComponent<RectMask2D>() == null)
                _settingsFloatScrollHost.AddComponent<RectMask2D>();

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            _settingsFloatScrollRect = _settingsFloatScrollHost.AddComponent<ScrollRect>();
            _settingsFloatScrollRect.horizontal = false;
            _settingsFloatScrollRect.vertical = true;
            _settingsFloatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _settingsFloatScrollRect.scrollSensitivity =
                VpbScrollTuning.Sensitivity(GalleryUiDesignTokens.SettingsFloatScrollSensitivityRef, 1f);
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
            VerticalLayoutGroup cv = UI.AddVLG(
                content,
                spacing: SettingsFloatRowsSpacingRef * s,
                padding: UI.Pad(SettingsFloatRowsPadRef, SettingsFloatRowsPadRef, SettingsFloatRowsPadRef, SettingsFloatRowsPadRef, s));
            cv.childForceExpandHeight = false;
            cv.childForceExpandWidth = true;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _settingsFloatRowsParent = content.transform;

            _settingsFloatEmptyGo = new GameObject("Empty");
            _settingsFloatEmptyGo.transform.SetParent(viewport.transform, false);
            RectTransform emptyRT = _settingsFloatEmptyGo.AddComponent<RectTransform>();
            emptyRT.anchorMin = Vector2.zero;
            emptyRT.anchorMax = Vector2.one;
            emptyRT.offsetMin = new Vector2(12f * s, 12f * s);
            emptyRT.offsetMax = new Vector2(-12f * s, -12f * s);
            VerticalLayoutGroup emptyVlg = UI.AddVLG( _settingsFloatEmptyGo, spacing: UI.GapGroup(s), padding: UI.PadControl(s));
            emptyVlg.childAlignment = TextAnchor.MiddleCenter;
            emptyVlg.childForceExpandHeight = false;
            emptyVlg.childForceExpandWidth = true;
            _settingsFloatEmptyText = UI.CreateLabel(
                _settingsFloatEmptyGo,
                VPBTranslation.T("settings.empty.none", "No settings in this category"),
                font, GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "EmptyLabel");
            GalleryUiMetrics.ApplyFont(_settingsFloatEmptyText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(_settingsFloatEmptyText.gameObject, flexibleWidth: 1f, minHeight: 24f * s);
            _settingsFloatEmptyClearBtn = SettingsFloatChromeButton(
                _settingsFloatEmptyGo.transform, 140f * s, chromeSz,
                VPBTranslation.T("settings.empty.clear", "Clear search"), font, s,
                SettingsFloatGroupActive, ClearSettingsFinderAndModified);
            if (_settingsFloatEmptyClearBtn != null)
            {
                _settingsFloatEmptyClearBtn.name = "EmptyClear";
                LayoutElement cle = _settingsFloatEmptyClearBtn.GetComponent<LayoutElement>();
                if (cle != null) cle.flexibleWidth = 0f;
            }
            _settingsFloatEmptyGo.SetActive(false);

            _settingsFloatCollapsed = false;
            _settingsFloatExpandHeightRef = panelHRef;
            try { SyncSettingsSideSearchInputFromFilter(); } catch { }
            RebuildSettingsFloatSidebar(font, s, chromeSz);
            // Shell only — rows come from RefreshInternalSettingsListRows on open. Building them here
            // too meant the whole list was constructed twice on first open (build, then destroy+rebuild).
            try { UI.ApplyFloatRootHoverPolicy(_settingsFloatRoot); } catch { }
            _settingsFloatRoot.SetActive(false);
        }

        /// <summary>
        /// Left atlas glyph beside chip label. Does not hide text (unlike <see cref="UI.AddIconToButton"/>).
        /// No colored icon well — selected state stays the chip fill.
        /// </summary>
        private static bool ApplySettingsGroupChipIcon(GameObject btn, string iconRole, float s)
        {
            if (btn == null || string.IsNullOrEmpty(iconRole) || s <= 0f) return false;
            Sprite spr = UI.LoadIconSprite(iconRole, UI.BarIconGlyphTint);
            if (spr == null) return false;

            float iconSz = SettingsFloatGroupChipIconSizeRef * s;
            float gap = SettingsFloatGroupChipIconGapRef * s;
            if (btn.GetComponent<HorizontalLayoutGroup>() == null)
            {
                UI.AddHLG(
                    btn,
                    spacing: gap,
                    padding: UI.Pad(SettingsFloatGroupChipPadLRef, SettingsFloatGroupChipPadRRef, 0f, 0f, s),
                    childAlignment: TextAnchor.MiddleCenter,
                    childForceExpandWidth: false,
                    childForceExpandHeight: false);
            }

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(btn.transform, false);
            iconGO.transform.SetAsFirstSibling();
            Image img = UI.AddImage(iconGO, Color.white, false);
            img.preserveAspect = true;
            UI.SetIconSprite(img, spr);
            UI.AddLE(
                iconGO,
                minWidth: iconSz, minHeight: iconSz,
                preferredWidth: iconSz, preferredHeight: iconSz,
                flexibleWidth: 0f, flexibleHeight: 0f);

            Text t = btn.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleLeft;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.raycastTarget = false;
                LayoutElement tle = t.GetComponent<LayoutElement>();
                if (tle == null) tle = t.gameObject.AddComponent<LayoutElement>();
                tle.preferredWidth = t.preferredWidth;
                tle.flexibleWidth = 0f;
            }
            return true;
        }

        private void RefreshSettingsFloatFilterClearVisible()
        {
            if (_settingsFloatFilterClearGo == null) return;
            bool show = _settingsFloatFilterInput != null
                && !string.IsNullOrEmpty(_settingsFloatFilterInput.text);
            if (_settingsFloatFilterClearGo.activeSelf != show)
                _settingsFloatFilterClearGo.SetActive(show);
        }

        private void RelayoutSettingsFloatGroupTabs(float s, float chromeSz)
        {
            ApplySettingsFloatScrollHostInsets(s);
        }

        private void ApplySettingsFloatScrollHostInsets(float s)
        {
            if (_settingsFloatScrollHost == null) return;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float sidebarW = GalleryUiDesignTokens.SettingsFloatSidebarWidthRef * s;
            _settingsFloatGroupTabsH = sidebarW;

            RectTransform scrollRT = _settingsFloatScrollHost.GetComponent<RectTransform>();
            if (scrollRT != null)
            {
                scrollRT.offsetMin = new Vector2(sidebarW, footerH);
                scrollRT.offsetMax = new Vector2(0f, -(titleH + filterH));
            }

            if (_settingsFloatGroupTabsRow != null)
            {
                RectTransform groupRT = _settingsFloatGroupTabsRow.GetComponent<RectTransform>();
                if (groupRT != null)
                {
                    groupRT.anchorMin = new Vector2(0f, 0f);
                    groupRT.anchorMax = new Vector2(0f, 1f);
                    groupRT.pivot = new Vector2(0f, 1f);
                    groupRT.offsetMin = new Vector2(0f, footerH);
                    groupRT.offsetMax = new Vector2(sidebarW, -(titleH + filterH));
                    groupRT.sizeDelta = new Vector2(sidebarW, 0f);
                }
            }
        }

        /// <summary>
        /// Rebuilds the settings list. Only the rows covering the viewport (plus a small overscan) are
        /// built in this frame; everything above and below is a single exact-height placeholder so the
        /// scroll range and position match a fully built list, and the remaining rows stream in over the
        /// next frames. Every row click routes back through here, so this governs click latency too.
        /// </summary>
        private void RebuildSettingsFloatRows(int font, float s, float rowH, bool keepScroll)
        {
            if (_settingsFloatRowsParent == null) return;

            StopSettingsFloatDeferredRows();

            float scrollPos = 1f;
            if (keepScroll && _settingsFloatScrollRect != null)
                scrollPos = Mathf.Clamp01(_settingsFloatScrollRect.verticalNormalizedPosition);

            for (int c = _settingsFloatRowsParent.childCount - 1; c >= 0; c--)
            {
                try
                {
                    // Destroy is deferred to end of frame — deactivate now so the layout pass this frame
                    // does not size the content from old rows plus new ones.
                    GameObject old = _settingsFloatRowsParent.GetChild(c).gameObject;
                    old.SetActive(false);
                    UnityEngine.Object.Destroy(old);
                }
                catch { }
            }

            _settingsFloatRowsFont = font;
            _settingsFloatRowsScale = s;
            _settingsFloatRowsBaseHeight = rowH;
            _settingsFloatRowsSpacing = SettingsFloatRowsSpacingRef * s;

            List<FileEntry> rows = BuildInternalSettingsRows();
            float rowsH = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                InternalSettingRowEntry row = rows[i] as InternalSettingRowEntry;
                if (row == null) continue;
                InternalSettingDefinition def = row.IsHeader ? null : GetInternalSettingDefinition(row.RowKey);
                if (!row.IsHeader && def == null) continue;
                float h = SettingsFloatRowHeight(row, def, rowH, s);
                _settingsFloatRowQueue.Add(row);
                _settingsFloatRowHeights.Add(h);
                rowsH += h;
            }

            int n = _settingsFloatRowQueue.Count;
            SyncSettingsFloatEmptyState(n, s, font);
            if (n == 0) return;

            float spacing = _settingsFloatRowsSpacing;
            float pad = SettingsFloatRowsPadRef * s;
            float totalH = rowsH + spacing * (n - 1) + pad * 2f;
            float viewH = SettingsFloatViewportHeight(s);
            float topY = (1f - scrollPos) * Mathf.Max(0f, totalH - viewH);

            int first = int.MaxValue;
            int last = -1;
            float y = pad;
            for (int i = 0; i < n; i++)
            {
                float h = _settingsFloatRowHeights[i];
                if (y + h > topY && y < topY + viewH)
                {
                    if (i < first) first = i;
                    if (i > last) last = i;
                }
                y += h + spacing;
            }
            if (last < 0) { first = 0; last = 0; }
            first = Mathf.Max(0, first - SettingsFloatRowOverscan);
            last = Mathf.Min(n - 1, last + SettingsFloatRowOverscan);

            if (first > 0)
                _settingsFloatRowsLeadPad = CreateSettingsFloatRowPad("PendingRowsLead", SettingsFloatRowsSpanHeight(0, first - 1));

            for (int i = first; i <= last; i++)
            {
                InternalSettingRowEntry row = _settingsFloatRowQueue[i];
                BuildSettingsFloatRow(row, row != null && !row.IsHeader ? GetInternalSettingDefinition(row.RowKey) : null, font, s, rowH);
            }

            if (last < n - 1)
                _settingsFloatRowsTailPad = CreateSettingsFloatRowPad("PendingRowsTail", SettingsFloatRowsSpanHeight(last + 1, n - 1));

            _settingsFloatRowsBuiltFirst = first;
            _settingsFloatRowsBuiltLast = last;

            if (keepScroll && _settingsFloatScrollRect != null)
                _settingsFloatScrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollPos);
            else
                ApplySettingsFloatScrollToGroup();

            if (first > 0 || last < n - 1)
                _settingsFloatRowsDeferCo = StartCoroutine(BuildSettingsFloatRowsDeferredCo());
        }

        /// <summary>Height a row will occupy, resolved without building it (drives the placeholders).</summary>
        private float SettingsFloatRowHeight(InternalSettingRowEntry row, InternalSettingDefinition def, float rowH, float s)
        {
            if (row != null && row.IsHeader)
                return GalleryUiDesignTokens.SettingsFloatSectionHeaderHeightRef * s;
            if (def != null
                && def.ControlType == InternalSettingControlType.TextArea
                && !def.SingleLineText
                && !string.Equals(def.Key, "quick.categoryEditor", StringComparison.OrdinalIgnoreCase))
            {
                return GalleryUiDesignTokens.SettingsFloatTextAreaRowHeightRef * s;
            }
            return rowH;
        }

        /// <summary>Height of rows [fromIndex..toIndex] collapsed into one child: their own heights plus
        /// the layout spacings between them (the spacing to the neighbouring child stays with the child).</summary>
        private float SettingsFloatRowsSpanHeight(int fromIndex, int toIndex)
        {
            float h = 0f;
            for (int i = fromIndex; i <= toIndex && i < _settingsFloatRowHeights.Count; i++)
                h += _settingsFloatRowHeights[i];
            int count = toIndex - fromIndex + 1;
            if (count > 1) h += _settingsFloatRowsSpacing * (count - 1);
            return h;
        }

        private float SettingsFloatViewportHeight(float s)
        {
            float h = 0f;
            try
            {
                if (_settingsFloatScrollRect != null && _settingsFloatScrollRect.viewport != null)
                    h = _settingsFloatScrollRect.viewport.rect.height;
            }
            catch { }
            if (h <= 1f)
            {
                try { if (_settingsFloatPanelRT != null) h = _settingsFloatPanelRT.rect.height; } catch { }
            }
            if (h <= 1f) h = GalleryUiDesignTokens.SettingsFloatDefaultHeightRef * s;
            return h;
        }

        private GameObject CreateSettingsFloatRowPad(string name, float height)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_settingsFloatRowsParent, false);
            go.AddComponent<RectTransform>();
            UI.AddLE(go, minHeight: height, preferredHeight: height, flexibleWidth: 1f, flexibleHeight: 0f);
            return go;
        }

        private IEnumerator BuildSettingsFloatRowsDeferredCo()
        {
            int n = _settingsFloatRowQueue.Count;
            while (_settingsFloatRowsParent != null
                   && (_settingsFloatRowsBuiltFirst > 0 || _settingsFloatRowsBuiltLast < n - 1))
            {
                yield return null;
                if (_settingsFloatRowsParent == null) break;

                _settingsFloatRowsClock.Reset();
                _settingsFloatRowsClock.Start();
                while (_settingsFloatRowsClock.ElapsedMilliseconds < SettingsFloatDeferredRowBudgetMs)
                {
                    // Fill downward first (what the user scrolls into), then back up.
                    if (_settingsFloatRowsBuiltLast < n - 1) BuildSettingsFloatDeferredRow(true);
                    else if (_settingsFloatRowsBuiltFirst > 0) BuildSettingsFloatDeferredRow(false);
                    else break;
                }
                _settingsFloatRowsClock.Stop();
            }
            _settingsFloatRowsDeferCo = null;
        }

        private void BuildSettingsFloatDeferredRow(bool down)
        {
            int n = _settingsFloatRowQueue.Count;
            int idx = down ? _settingsFloatRowsBuiltLast + 1 : _settingsFloatRowsBuiltFirst - 1;
            if (idx < 0 || idx >= n) return;

            GameObject padGO = down ? _settingsFloatRowsTailPad : _settingsFloatRowsLeadPad;
            InternalSettingRowEntry row = _settingsFloatRowQueue[idx];
            InternalSettingDefinition def = (row != null && !row.IsHeader) ? GetInternalSettingDefinition(row.RowKey) : null;
            GameObject rowGO = row != null
                ? BuildSettingsFloatRow(row, def, _settingsFloatRowsFont, _settingsFloatRowsScale, _settingsFloatRowsBaseHeight)
                : null;

            if (rowGO != null && padGO != null)
            {
                int padIdx = padGO.transform.GetSiblingIndex();
                try { rowGO.transform.SetSiblingIndex(down ? padIdx : padIdx + 1); } catch { }
            }

            if (down) _settingsFloatRowsBuiltLast = idx;
            else _settingsFloatRowsBuiltFirst = idx;

            if (padGO == null) return;

            bool exhausted = down ? idx >= n - 1 : idx <= 0;
            if (exhausted)
            {
                try { padGO.SetActive(false); UnityEngine.Object.Destroy(padGO); } catch { }
                if (down) _settingsFloatRowsTailPad = null;
                else _settingsFloatRowsLeadPad = null;
                return;
            }

            LayoutElement le = padGO.GetComponent<LayoutElement>();
            if (le == null) return;
            float shrunk = Mathf.Max(0f, le.preferredHeight - (_settingsFloatRowHeights[idx] + _settingsFloatRowsSpacing));
            le.minHeight = shrunk;
            le.preferredHeight = shrunk;
        }

        private GameObject BuildSettingsFloatRow(InternalSettingRowEntry row, InternalSettingDefinition def, int font, float s, float rowH)
        {
            if (row == null) return null;
            if (row.IsHeader) return BuildSettingsFloatHeaderRow(row, font, s);

            if (def == null) return null;

            float effectiveRowH = SettingsFloatRowHeight(row, def, rowH, s);

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
                listRowGO, spacing: UI.GapControl(s), padding: UI.PadHV(GalleryUiDesignTokens.HairGapRef, GalleryUiDesignTokens.TightGapRef, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: true);

            bool modified = def.HasDefault && !SettingsRowIsAtDefault(def);
            GameObject dotGO = new GameObject("ModifiedDot");
            dotGO.transform.SetParent(listRowGO.transform, false);
            float dotSz = GalleryUiDesignTokens.SettingsFloatModifiedDotSizeRef * s;
            Image dotImg = UI.AddImage(dotGO, modified ? Color.white : new Color(1f, 1f, 1f, 0f));
            if (dotImg != null) dotImg.raycastTarget = false;
            UI.AddLE(dotGO, minWidth: dotSz, minHeight: dotSz, preferredWidth: dotSz, preferredHeight: dotSz, flexibleWidth: 0f);
            if (modified)
                AddTooltipPlain(dotGO, VPBTranslation.T("settings.row.modified.tip", "Differs from default"));

            Text nameText = UI.CreateLabel(
                listRowGO, row.Name ?? def.Label ?? row.RowKey ?? "", GalleryUiDesignTokens.SettingsListRowNameFontRef,
                modified ? Color.white : GalleryUiColorTokens.TextMuted, TextAnchor.MiddleLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "Name");
            GalleryUiMetrics.ApplyFont(nameText, GalleryUiDesignTokens.SettingsListRowNameFontRef, s, GalleryUiDesignTokens.FontMinRef);
            if (modified) nameText.fontStyle = FontStyle.Bold;
            UI.AddLE(nameText.gameObject, flexibleWidth: 0.45f, minWidth: 80f * s, preferredHeight: effectiveRowH * 0.9f);

            GameObject detailsRowGO = new GameObject("Details");
            detailsRowGO.transform.SetParent(listRowGO.transform, false);
            UI.AddHLG(detailsRowGO, spacing: UI.GapTight(s), childAlignment: TextAnchor.MiddleRight, childForceExpandWidth: false);
            UI.AddLE(detailsRowGO, flexibleWidth: 0.55f, minHeight: effectiveRowH * 0.85f, preferredHeight: effectiveRowH * 0.85f);

            RebuildSettingsRowControls(rowGO, def, settleLayout: false);
            // Per row — the root-wide pass walked every Selectable/UIHoverBorder in the whole float twice
            // per rebuild, and deferred rows would miss it entirely.
            try { UI.ApplyFloatRootHoverPolicy(rowGO); } catch { }
            return rowGO;
        }

        private GameObject BuildSettingsFloatHeaderRow(InternalSettingRowEntry row, int font, float s)
        {
            float h = GalleryUiDesignTokens.SettingsFloatSectionHeaderHeightRef * s;
            GameObject rowGO = new GameObject("SettingsHdr_" + (row.RowKey ?? "hdr"));
            rowGO.transform.SetParent(_settingsFloatRowsParent, false);
            UI.AddLE(rowGO, minHeight: h, preferredHeight: h, flexibleWidth: 1f);
            Color bg = row.IsCategoryHeader ? SettingsFloatGroupActive : GalleryUiColorTokens.SurfacePanel;
            UI.AddImage(rowGO, bg);

            Text t = UI.CreateLabel(
                rowGO, row.Name ?? "", GalleryUiDesignTokens.FontCaptionRef,
                row.IsCategoryHeader ? Color.white : GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "Name");
            GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            t.fontStyle = FontStyle.Bold;
            RectTransform tr = t.rectTransform;
            if (tr != null)
            {
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(10f * s, 0f);
                tr.offsetMax = new Vector2(-8f * s, 0f);
            }
            return rowGO;
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
                string path = _settingsFloatCollapsed ? "chevron-down" : "chevron-up";
                Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
                if (spr != null)
                {
                    UI.SetIconSprite(_settingsFloatCollapseIcon, spr);
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

        /// <summary>Esc ladder: clear search → clear modified-only → Close (keep values). Must gate on GetKeyDown (warm Update).</summary>
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

            if (settingsModifiedOnly)
            {
                settingsModifiedOnly = false;
                SyncSettingsModifiedOnlyButton();
                RefreshInternalSettingsListRows(true);
                return true;
            }

            ExitInternalSettingsMode(true);
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

            if (_settingsFloatModifiedBtn != null)
                RescaleSettingsFloatSquareChrome(_settingsFloatModifiedBtn, chromeSz, s);

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

            ApplySettingsFloatScrollHostInsets(s);
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

        /// <summary>Re-bind settings float chrome strings after locale change. Rows rebuild separately.</summary>
        private void RefreshSettingsFloatLocalizedChrome()
        {
            if (_settingsFloatRoot == null) return;

            if (_settingsFloatTitleBarRT != null)
            {
                Transform titleTr = _settingsFloatTitleBarRT.Find("Title");
                Text title = titleTr != null ? titleTr.GetComponent<Text>() : null;
                if (title != null)
                    title.text = VPBTranslation.T("settings.title", "Settings");
            }

            if (_settingsFloatFilterInput != null)
            {
                Text ph = _settingsFloatFilterInput.placeholder as Text;
                if (ph != null)
                    ph.text = VPBTranslation.T("settings.search.placeholder", "Search settings…");
            }

            if (_settingsFloatModifiedBtnText != null)
                _settingsFloatModifiedBtnText.text = VPBTranslation.T("settings.modified_only.abbrev", "≠");

            if (_settingsFloatFooter != null)
            {
                SetChildButtonLabel(_settingsFloatFooter.transform, "FooterRevert",
                    VPBTranslation.T("settings.footer.revert", "Revert"));
                SetChildButtonLabel(_settingsFloatFooter.transform, "FooterClose",
                    VPBTranslation.T("settings.footer.close", "Close"));
            }
        }

        private static void SetChildButtonLabel(Transform parent, string childName, string label)
        {
            if (parent == null || string.IsNullOrEmpty(childName)) return;
            Transform tr = parent.Find(childName);
            if (tr == null) return;
            Text t = tr.GetComponentInChildren<Text>(true);
            if (t != null) t.text = label ?? "";
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
