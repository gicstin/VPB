using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// Plugins category floating work surface: search + ★ rated filter + cslist→cs tree.
    /// Chrome mirrors SettingsFloat (always-float palette). Drag rows → header Person / Session · void = session.
    /// </summary>
    public partial class GalleryPanel
    {
        private static readonly Color PluginsFloatTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color PluginsFloatFooterBarBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color PluginsFloatPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color PluginsFloatScrollBg = GalleryUiColorTokens.ModalSurface;
        private static readonly Color PluginsFloatFilterBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color PluginsFloatRowBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color PluginsFloatChildRowBg = GalleryUiColorTokens.SurfacePanel;

        private GameObject _pluginsFloatRoot;
        private RectTransform _pluginsFloatPanelRT;
        private RectTransform _pluginsFloatTitleBarRT;
        private Transform _pluginsFloatRowsParent;
        private ScrollRect _pluginsFloatScrollRect;
        private InputField _pluginsFloatFilterInput;
        private UnityAction<string> _pluginsFloatFilterOnValueChanged;
        private GameObject _pluginsFloatFilterClearGo;
        private GameObject _pluginsFloatFilterRow;
        private GameObject _pluginsFloatScrollHost;
        private GameObject _pluginsFloatFooter;
        private GameObject _pluginsFloatCollapseBtn;
        private Image _pluginsFloatCollapseIcon;
        private GameObject _pluginsFloatExpandAllBtn;
        private GameObject _pluginsFloatCollapseAllBtn;
        private GameObject _pluginsFloatRatedOnlyBtn;
        private Image _pluginsFloatRatedOnlyBtnBackdrop;
        private Text _pluginsFloatRatedOnlyBtnText;
        private Text _pluginsFloatStatusText;
        private GameObject _pluginsFloatEmptyGo;
        private Text _pluginsFloatEmptyText;

        private float _pluginsFloatChromeScale = 1f;
        private Vector2? _pluginsFloatSavedPosCenter;
        private Vector2? _pluginsFloatSavedSizeRef;
        private float? _pluginsFloatExpandHeightRef;
        private bool _pluginsFloatCollapsed;
        private Vector2? _pluginsFloatCollapsedTopLeftPos;

        private string _pluginsFloatFilter = "";
        private bool _pluginsFloatRatedOnly;
        private bool _pluginsFloatLatestOnly;
        private bool _pluginsFloatCslistOnly;
        private Toggle _pluginsFloatLatestOnlyToggle;
        private Toggle _pluginsFloatCslistOnlyToggle;
        private GameObject _pluginsFloatOptionsRow;
        private readonly HashSet<string> _pluginsFloatExpanded =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<PluginsFloatRoot> _pluginsFloatRoots = new List<PluginsFloatRoot>();
        private RectTransform _pluginsFloatContentRT;
        private readonly List<PluginsFloatFlatRow> _pluginsFloatVirtView = new List<PluginsFloatFlatRow>(512);
        private readonly List<GameObject> _pluginsFloatVirtPool = new List<GameObject>(32);
        private bool _pluginsFloatScrollHooked;
        private int _pluginsFloatVirtLastFirstIdx = -1;
        private HashSet<string> _pluginsFloatLatestUidSet;

        private GameObject ResolvePluginsFloatHost()
        {
            if (canvas != null) return canvas.gameObject;
            return backgroundBoxGO;
        }

        internal bool IsPluginsFloatOpen()
        {
            return _pluginsFloatRoot != null && _pluginsFloatRoot.activeSelf;
        }

        /// <summary>
        /// Open Plugins float work surface. Does NOT switch gallery category / grid
        /// (Clothing stays Clothing). forceShow=false toggles like Settings.
        /// </summary>
        public void OpenPluginsFloat(bool forceShow = true)
        {
            if (!forceShow && IsPluginsFloatOpen())
            {
                HidePluginsFloat();
                return;
            }

            try { LogUtil.Log("[VPB.PluginsFloat] open"); } catch { }
            EnsurePluginsFloatBuilt();
            ShowPluginsFloat();
            // Own catalog — never RefreshFiles / Show("Plugins").
            RequestPluginsFloatCatalog(force: false);
            try { RefreshPluginsFloatDestButtons(); } catch { }
        }

        private void EnsurePluginsFloatBuilt()
        {
            if (_pluginsFloatRoot != null) return;
            BuildPluginsFloat();
        }

        private void ShowPluginsFloat()
        {
            EnsurePluginsFloatBuilt();
            if (_pluginsFloatRoot == null) return;
            _pluginsFloatRoot.SetActive(true);
            try { _pluginsFloatRoot.transform.SetAsLastSibling(); } catch { }
            try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
            try
            {
                if (IsFixedTopDockMode())
                    ApplyTopDockSideButtonsLayout(ChromeScale);
            }
            catch { }
        }

        private void HidePluginsFloat()
        {
            CapturePluginsFloatGeometryToMemory();
            PersistPluginsFloatGeometry();
            if (_pluginsFloatRoot != null)
                _pluginsFloatRoot.SetActive(false);
            try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
            try
            {
                if (IsFixedTopDockMode())
                    ApplyTopDockSideButtonsLayout(ChromeScale);
            }
            catch { }
        }

        private void DestroyPluginsFloatChrome()
        {
            try { StopPluginsFloatCatalogLoad(); } catch { }
            if (_pluginsFloatRoot != null)
            {
                try { UnityEngine.Object.Destroy(_pluginsFloatRoot); } catch { }
                _pluginsFloatRoot = null;
            }
            _pluginsFloatPanelRT = null;
            _pluginsFloatTitleBarRT = null;
            _pluginsFloatRowsParent = null;
            _pluginsFloatScrollRect = null;
            _pluginsFloatFilterInput = null;
            _pluginsFloatFilterOnValueChanged = null;
            _pluginsFloatFilterClearGo = null;
            _pluginsFloatFilterRow = null;
            _pluginsFloatScrollHost = null;
            _pluginsFloatFooter = null;
            _pluginsFloatCollapseBtn = null;
            _pluginsFloatCollapseIcon = null;
            _pluginsFloatExpandAllBtn = null;
            _pluginsFloatCollapseAllBtn = null;
            _pluginsFloatRatedOnlyBtn = null;
            try { DestroyPluginsFloatDestBar(); } catch { }
            _pluginsFloatRatedOnlyBtnBackdrop = null;
            _pluginsFloatRatedOnlyBtnText = null;
            _pluginsFloatStatusText = null;
            _pluginsFloatEmptyGo = null;
            _pluginsFloatEmptyText = null;
            _pluginsFloatContentRT = null;
            _pluginsFloatOptionsRow = null;
            _pluginsFloatLatestOnlyToggle = null;
            _pluginsFloatCslistOnlyToggle = null;
            _pluginsFloatScrollHooked = false;
            _pluginsFloatVirtPool.Clear();
            _pluginsFloatVirtView.Clear();
            _pluginsFloatRoots.Clear();
            if (_pluginsFloatCreators != null) _pluginsFloatCreators.Clear();
            _pluginsFloatVirtLastFirstIdx = -1;
            _pluginsFloatLatestUidSet = null;
            _pluginsFloatFilterTerms = new string[0];
        }

        private void BuildPluginsFloat()
        {
            float s = ChromeScale;
            _pluginsFloatChromeScale = s;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float optionsH = GalleryUiDesignTokens.PluginsFloatOptionsRowHeightRef * s;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef * s;
            float headerBelowTitle = filterH + optionsH;

            LoadPluginsFloatGeometryFromConfig();
            float panelWRef = GalleryUiDesignTokens.PluginsFloatDefaultWidthRef;
            float panelHRef = GalleryUiDesignTokens.PluginsFloatDefaultHeightRef;
            if (_pluginsFloatSavedSizeRef.HasValue)
            {
                panelWRef = Mathf.Clamp(
                    _pluginsFloatSavedSizeRef.Value.x,
                    GalleryUiDesignTokens.PluginsFloatMinWidthRef,
                    GalleryUiDesignTokens.PluginsFloatMaxWidthRef);
                panelHRef = Mathf.Clamp(
                    _pluginsFloatSavedSizeRef.Value.y,
                    GalleryUiDesignTokens.PluginsFloatMinHeightRef,
                    GalleryUiDesignTokens.PluginsFloatMaxHeightRef);
            }
            float panelW = panelWRef * s;
            float panelH = panelHRef * s;

            GameObject host = ResolvePluginsFloatHost();
            if (host == null) return;

            _pluginsFloatRoot = UI.CreateChildRT(host, "VPB_PluginsFloat", AnchorPresets.stretchAll);
            try { SetLayerRecursiveLocal(_pluginsFloatRoot, host.layer); } catch { }

            GameObject panel = UI.CreateChildRT(
                _pluginsFloatRoot, "Panel", AnchorPresets.middleCenter,
                new Vector2(panelW, panelH), Vector2.zero);
            UI.AddImage(panel, PluginsFloatPanelBg);
            if (panel.GetComponent<RectMask2D>() == null)
                panel.AddComponent<RectMask2D>();
            _pluginsFloatPanelRT = panel.GetComponent<RectTransform>();
            if (_pluginsFloatPanelRT != null)
            {
                _pluginsFloatPanelRT.pivot = new Vector2(0f, 1f);
                _pluginsFloatPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _pluginsFloatPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _pluginsFloatPanelRT.sizeDelta = new Vector2(panelW, panelH);
                Vector2 center = _pluginsFloatSavedPosCenter.HasValue
                    ? _pluginsFloatSavedPosCenter.Value
                    : new Vector2(180f, 40f);
                _pluginsFloatPanelRT.anchoredPosition = PluginsFloatCenterToTopLeft(center, _pluginsFloatPanelRT.sizeDelta);
            }

            // Title bar
            GameObject titleBar = UI.CreateChildRT(panel, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            Image titleBg = UI.AddImage(titleBar, PluginsFloatTitleBarBg);
            if (titleBg != null) titleBg.raycastTarget = true;
            _pluginsFloatTitleBarRT = titleBar.GetComponent<RectTransform>();
            if (_pluginsFloatTitleBarRT != null)
            {
                _pluginsFloatTitleBarRT.pivot = new Vector2(0.5f, 1f);
                _pluginsFloatTitleBarRT.anchoredPosition = Vector2.zero;
                _pluginsFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
            }
            UI.AddHLG(
                titleBar, spacing: 4f * s, padding: UI.Pad(6, 6, 4, 4, s),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (titleBar.GetComponent<RectMask2D>() == null)
                titleBar.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(titleBar, "\u2807", font,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(grip.gameObject, minWidth: 18f * s, preferredWidth: 18f * s);

            float winIconSz = GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s;
            UI.CreateFloatTitleWindowIcon(titleBar, "vpb_icons/c_plugins.png", winIconSz);

            Text title = UI.CreateEmphasisTitleLabel(
                titleBar,
                VPBTranslation.T("gallery.plugins.float_title", "Plugins"),
                font, Color.white, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(title.gameObject, flexibleWidth: 0f, minWidth: 48f * s, preferredWidth: 64f * s);

            // Session / Person link chips live in header (also drop targets).
            BuildPluginsFloatDestBar(titleBar, s, font, chromeSz);

            _pluginsFloatCollapseBtn = PluginsFloatSquareIconButton(
                titleBar.transform, chromeSz, "vpb_icons/chevron_up.png",
                GalleryUiColorTokens.ChromeIconWell, TogglePluginsFloatCollapsed);
            if (_pluginsFloatCollapseBtn != null)
            {
                _pluginsFloatCollapseBtn.name = "CollapseBtn";
                Transform iconTr = _pluginsFloatCollapseBtn.transform.Find("Icon");
                _pluginsFloatCollapseIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }

            GameObject closeBtn = PluginsFloatSquareIconButton(
                titleBar.transform, chromeSz, "vpb_icons/x.png",
                GalleryUiColorTokens.ChromeIconWell, HidePluginsFloat);
            if (closeBtn != null) closeBtn.name = "TitleClose";

            var headerDrag = titleBar.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = _pluginsFloatPanelRT;
            headerDrag.OnMoved = OnPluginsFloatMoved;

            // Search + ★ rated-only
            _pluginsFloatFilterRow = UI.CreateChildRT(panel, "FilterRow", AnchorPresets.hStretchTop,
                new Vector2(0f, filterH), new Vector2(0f, -titleH));
            RectTransform filterRT = _pluginsFloatFilterRow.GetComponent<RectTransform>();
            if (filterRT != null)
            {
                filterRT.pivot = new Vector2(0.5f, 1f);
                filterRT.sizeDelta = new Vector2(0f, filterH);
                filterRT.anchoredPosition = new Vector2(0f, -titleH);
            }
            UI.AddHLG(_pluginsFloatFilterRow, spacing: 6f * s,
                padding: UI.Pad(
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef, s),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            _pluginsFloatFilterInput = UI.CreateChromeLayoutInputField(
                _pluginsFloatFilterRow.transform,
                font,
                searchH,
                1f,
                8f * s,
                2f * s,
                PluginsFloatFilterBg,
                UI.InputFieldPlaceholderColor,
                VPBTranslation.T("gallery.plugins.search", "Search creator plugin… (space = AND)"),
                "PluginsFilter");
            if (_pluginsFloatFilterInput != null)
            {
                UI.LayoutChromeSearchIcon(_pluginsFloatFilterInput.gameObject, s);
                float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                Transform textAreaTr = _pluginsFloatFilterInput.transform.Find("TextArea");
                if (textAreaTr != null)
                {
                    RectTransform taRt = textAreaTr as RectTransform;
                    if (taRt != null)
                        taRt.offsetMax = new Vector2(-clearSz, taRt.offsetMax.y);
                }

                _pluginsFloatFilterClearGo = UI.CreateUIButton(
                    _pluginsFloatFilterInput.gameObject,
                    clearSz, searchH, "X", 24, 0, 0, AnchorPresets.middleRight,
                    () =>
                    {
                        SetPluginsFloatFilterTextWithoutNotify("");
                        _pluginsFloatFilter = "";
                        _pluginsFloatSearchAutoExpandPending = false;
                        RefreshPluginsFloatTree(true);
                        try
                        {
                            _pluginsFloatFilterInput.ActivateInputField();
                            _pluginsFloatFilterInput.MoveTextEnd(false);
                        }
                        catch { }
                        RefreshPluginsFloatFilterClearVisible();
                    });
                if (_pluginsFloatFilterClearGo != null)
                {
                    _pluginsFloatFilterClearGo.name = "FilterClear";
                    RectTransform clearRT = _pluginsFloatFilterClearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                    {
                        clearRT.anchorMin = new Vector2(1f, 0f);
                        clearRT.anchorMax = new Vector2(1f, 1f);
                        clearRT.pivot = new Vector2(1f, 0.5f);
                        clearRT.anchoredPosition = Vector2.zero;
                        clearRT.sizeDelta = new Vector2(clearSz, 0f);
                    }
                    Image clearBg = _pluginsFloatFilterClearGo.GetComponent<Image>();
                    if (clearBg != null) clearBg.color = new Color(0f, 0f, 0f, 0f);
                    try
                    {
                        Sprite xSpr = UI.LoadIconSprite("vpb_icons/x.png", new Color(0.6f, 0.6f, 0.6f));
                        if (xSpr != null)
                            UI.AddIconToButton(_pluginsFloatFilterClearGo, xSpr, 6f * s, new Color(0f, 0f, 0f, 0f));
                    }
                    catch { }
                    Text clearLabel = _pluginsFloatFilterClearGo.GetComponentInChildren<Text>(true);
                    if (clearLabel != null) clearLabel.text = "";
                    AddTooltipPlain(_pluginsFloatFilterClearGo,
                        VPBTranslation.T("gallery.creator.strip_filter_clear", "Clear filter"));
                }

                _pluginsFloatFilterOnValueChanged = val =>
                {
                    string next = val ?? "";
                    // Seed expand once when search starts — further keystrokes must not re-open collapsed rows.
                    if ((_pluginsFloatFilter ?? "").Length == 0 && next.Length > 0)
                        _pluginsFloatSearchAutoExpandPending = true;
                    _pluginsFloatFilter = next;
                    RefreshPluginsFloatFilterClearVisible();
                    RefreshPluginsFloatTree(true);
                };
                _pluginsFloatFilterInput.onValueChanged.AddListener(_pluginsFloatFilterOnValueChanged);
                try
                {
                    _pluginsFloatFilterInput.gameObject.AddComponent<CtrlBackspaceWordDeleteHandler>()
                        .Initialize(_pluginsFloatFilterInput);
                }
                catch { }
                RefreshPluginsFloatFilterClearVisible();
            }

            // ★ rated-only — same job as creators list toggle (Jakob).
            _pluginsFloatRatedOnlyBtn = UI.CreateUIButton(
                _pluginsFloatFilterRow, chromeSz, chromeSz, "★", font, 0, 0, AnchorPresets.middleCenter, null);
            if (_pluginsFloatRatedOnlyBtn != null)
            {
                _pluginsFloatRatedOnlyBtn.name = "RatedOnly";
                UI.AddLE(_pluginsFloatRatedOnlyBtn, minWidth: chromeSz, preferredWidth: chromeSz,
                    minHeight: chromeSz, preferredHeight: chromeSz, flexibleWidth: 0f);
                _pluginsFloatRatedOnlyBtnBackdrop = _pluginsFloatRatedOnlyBtn.GetComponent<Image>();
                _pluginsFloatRatedOnlyBtnText = _pluginsFloatRatedOnlyBtn.GetComponentInChildren<Text>(true);
                Button rb = _pluginsFloatRatedOnlyBtn.GetComponent<Button>();
                if (rb != null)
                {
                    rb.onClick.RemoveAllListeners();
                    rb.onClick.AddListener(TogglePluginsFloatRatedOnlyFilter);
                }
                AddTooltip(_pluginsFloatRatedOnlyBtn, "gallery.tooltip.plugins_rated_only",
                    "Rated plugins only, sorted high→low.");
                SyncPluginsFloatRatedOnlyButton();
            }

            // Filter facets under search — equal columns (Gestalt proximity; Hick: two clear toggles).
            _pluginsFloatOptionsRow = UI.CreateChildRT(panel, "OptionsRow", AnchorPresets.hStretchTop,
                new Vector2(0f, optionsH), new Vector2(0f, -(titleH + filterH)));
            RectTransform optRT = _pluginsFloatOptionsRow.GetComponent<RectTransform>();
            if (optRT != null)
            {
                optRT.pivot = new Vector2(0.5f, 1f);
                optRT.sizeDelta = new Vector2(0f, optionsH);
                optRT.anchoredPosition = new Vector2(0f, -(titleH + filterH));
            }
            UI.AddHLG(_pluginsFloatOptionsRow, spacing: 8f * s,
                padding: UI.Pad(6, 6, 2, 2, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            float optToggleH = optionsH - 4f * s;
            GameObject latestToggleGO = UI.CreateUIToggle(
                _pluginsFloatOptionsRow, 0f, optToggleH,
                VPBTranslation.T("gallery.plugins.latest_only", "Latest only"),
                font, 0, 0, AnchorPresets.middleLeft,
                on =>
                {
                    _pluginsFloatLatestOnly = on;
                    _pluginsFloatLatestUidSet = null;
                    PersistPluginsFloatLatestOnly();
                    RefreshPluginsFloatTree(true);
                });
            if (latestToggleGO != null)
            {
                latestToggleGO.name = "LatestOnly";
                UI.AddLE(latestToggleGO, flexibleWidth: 1f, minWidth: 96f * s,
                    minHeight: optToggleH, preferredHeight: optToggleH);
                _pluginsFloatLatestOnlyToggle = latestToggleGO.GetComponent<Toggle>();
                if (_pluginsFloatLatestOnlyToggle != null)
                    _pluginsFloatLatestOnlyToggle.isOn = _pluginsFloatLatestOnly;
                AddTooltip(latestToggleGO, "gallery.tooltip.plugins_latest_only",
                    "Hide older package versions (parents + scripts). Keeps highest Author.Name.N only. Loose Custom/Scripts stay.");
            }

            GameObject cslistToggleGO = UI.CreateUIToggle(
                _pluginsFloatOptionsRow, 0f, optToggleH,
                VPBTranslation.T("gallery.plugins.cslist_only", "Show .cslist only"),
                font, 0, 0, AnchorPresets.middleLeft,
                on =>
                {
                    _pluginsFloatCslistOnly = on;
                    PersistPluginsFloatCslistOnly();
                    RefreshPluginsFloatTree(true);
                });
            if (cslistToggleGO != null)
            {
                cslistToggleGO.name = "CslistOnly";
                UI.AddLE(cslistToggleGO, flexibleWidth: 1f, minWidth: 110f * s,
                    minHeight: optToggleH, preferredHeight: optToggleH);
                _pluginsFloatCslistOnlyToggle = cslistToggleGO.GetComponent<Toggle>();
                if (_pluginsFloatCslistOnlyToggle != null)
                    _pluginsFloatCslistOnlyToggle.isOn = _pluginsFloatCslistOnly;
                AddTooltip(cslistToggleGO, "gallery.tooltip.plugins_cslist_only",
                    "Hide orphan .cs / .dll roots. Keep .cslist parents; expand still shows referenced scripts.");
            }

            // Footer: expand/collapse tree + hint + resize
            _pluginsFloatFooter = UI.CreateChildRT(panel, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(_pluginsFloatFooter, PluginsFloatFooterBarBg);
            RectTransform footerRT = _pluginsFloatFooter.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                _pluginsFloatFooter, spacing: 4f * s, padding: UI.Pad(6, 6, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (_pluginsFloatFooter.GetComponent<RectMask2D>() == null)
                _pluginsFloatFooter.AddComponent<RectMask2D>();

            // Full-footer drag hit (behind expand/hint/resize) — same job as title bar.
            GameObject footerDragArea = UI.CreateFloatFooterDragArea(_pluginsFloatFooter);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = _pluginsFloatPanelRT;
                footerDrag.OnMoved = OnPluginsFloatMoved;
            }

            _pluginsFloatExpandAllBtn = PluginsFloatSquareIconButton(
                _pluginsFloatFooter.transform, chromeSz, "vpb_icons/chevrons_down.png",
                GalleryUiColorTokens.ChromeIconWell, ExpandAllPluginsFloatCreators);
            if (_pluginsFloatExpandAllBtn != null)
            {
                _pluginsFloatExpandAllBtn.name = "ExpandAll";
                AddTooltip(_pluginsFloatExpandAllBtn, "gallery.tooltip.plugins_expand_all",
                    "Expand all creators (shows packages). Open ▸ on a package for scripts.");
            }
            _pluginsFloatCollapseAllBtn = PluginsFloatSquareIconButton(
                _pluginsFloatFooter.transform, chromeSz, "vpb_icons/chevron_up.png",
                GalleryUiColorTokens.ChromeIconWell, CollapseAllPluginsFloatTree);
            if (_pluginsFloatCollapseAllBtn != null)
            {
                _pluginsFloatCollapseAllBtn.name = "CollapseAll";
                AddTooltip(_pluginsFloatCollapseAllBtn, "gallery.tooltip.plugins_collapse_all",
                    "Collapse all creators and packages.");
            }

            _pluginsFloatStatusText = UI.CreateLabel(
                _pluginsFloatFooter,
                VPBTranslation.T("gallery.plugins.float_hint", "Drag → header Person / Session · void = session"),
                font, GalleryUiColorTokens.TextDim, TextAnchor.MiddleLeft,
                raycastTarget: false, name: "Hint");
            GalleryUiMetrics.ApplyFont(_pluginsFloatStatusText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            if (_pluginsFloatStatusText != null)
            {
                _pluginsFloatStatusText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _pluginsFloatStatusText.verticalOverflow = VerticalWrapMode.Truncate;
            }
            UI.AddLE(_pluginsFloatStatusText.gameObject, flexibleWidth: 1f, minWidth: 24f * s);

            GameObject resizeHandle = UI.AddChildGOImage(
                _pluginsFloatFooter, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                chromeSz, chromeSz, Vector2.zero, rounded: true);
            resizeHandle.name = "ResizeHandle";
            Image rhImg = resizeHandle.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
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
            resizer.Target = _pluginsFloatPanelRT;
            resizer.GetMinSize = () => new Vector2(
                GalleryUiDesignTokens.PluginsFloatMinWidthRef * _pluginsFloatChromeScale,
                GalleryUiDesignTokens.PluginsFloatMinHeightRef * _pluginsFloatChromeScale);
            resizer.GetMaxSize = () => new Vector2(
                GalleryUiDesignTokens.PluginsFloatMaxWidthRef * _pluginsFloatChromeScale,
                GalleryUiDesignTokens.PluginsFloatMaxHeightRef * _pluginsFloatChromeScale);
            resizer.OnResized = OnPluginsFloatResized;

            // Scroll list
            _pluginsFloatScrollHost = UI.CreateChildRT(panel, "ScrollHost", AnchorPresets.stretchAll);
            RectTransform scrollRT = _pluginsFloatScrollHost.GetComponent<RectTransform>();
            if (scrollRT != null)
            {
                scrollRT.offsetMin = new Vector2(0f, footerH);
                scrollRT.offsetMax = new Vector2(0f, -(titleH + headerBelowTitle));
            }
            UI.AddImage(_pluginsFloatScrollHost, PluginsFloatScrollBg);
            if (_pluginsFloatScrollHost.GetComponent<RectMask2D>() == null)
                _pluginsFloatScrollHost.AddComponent<RectMask2D>();

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            _pluginsFloatScrollRect = _pluginsFloatScrollHost.AddComponent<ScrollRect>();
            _pluginsFloatScrollRect.horizontal = false;
            _pluginsFloatScrollRect.vertical = true;
            _pluginsFloatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _pluginsFloatScrollRect.scrollSensitivity = 25f;
            _pluginsFloatScrollRect.verticalScrollbar = null;

            GameObject viewport = UI.CreateChildRT(_pluginsFloatScrollHost, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            if (vpRt != null) vpRt.offsetMax = new Vector2(-sbW, 0f);
            viewport.AddComponent<RectMask2D>();
            _pluginsFloatScrollRect.viewport = vpRt;

            GameObject scrollbarGO = UI.CreateScrollBar(_pluginsFloatScrollHost, sbW, 0f, Scrollbar.Direction.BottomToTop);
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            if (sbRT != null)
            {
                sbRT.anchorMin = new Vector2(1f, 0f);
                sbRT.anchorMax = new Vector2(1f, 1f);
                sbRT.pivot = new Vector2(1f, 1f);
                sbRT.sizeDelta = new Vector2(sbW, 0f);
                sbRT.anchoredPosition = Vector2.zero;
            }
            Scrollbar sb = scrollbarGO.GetComponent<Scrollbar>();
            _pluginsFloatScrollRect.verticalScrollbar = sb;
            _pluginsFloatScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            var minHandle = scrollbarGO.AddComponent<ScrollbarMinHandleHeight>();
            minHandle.minHandlePixels = GalleryUiDesignTokens.PluginsFloatScrollbarMinHandleRef * s;

            // Absolute-position content (creator-virt style). No VLG/CSF — 10k layout children = hitch.
            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0f, 0f);
            _pluginsFloatScrollRect.content = contentRT;
            _pluginsFloatRowsParent = content.transform;
            _pluginsFloatContentRT = contentRT;

            _pluginsFloatEmptyGo = new GameObject("Empty");
            _pluginsFloatEmptyGo.transform.SetParent(viewport.transform, false);
            RectTransform emptyRT = _pluginsFloatEmptyGo.AddComponent<RectTransform>();
            emptyRT.anchorMin = Vector2.zero;
            emptyRT.anchorMax = Vector2.one;
            emptyRT.offsetMin = Vector2.zero;
            emptyRT.offsetMax = Vector2.zero;
            _pluginsFloatEmptyText = UI.CreateLabel(
                _pluginsFloatEmptyGo,
                VPBTranslation.T("gallery.plugins.empty", "No plugins found."),
                font, GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "EmptyLabel");
            GalleryUiMetrics.ApplyFont(_pluginsFloatEmptyText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            _pluginsFloatEmptyGo.SetActive(false);

            if (_pluginsFloatCollapsed)
                SyncPluginsFloatCollapseChrome(titleH);
        }

        private void RefreshPluginsFloatFilterClearVisible()
        {
            if (_pluginsFloatFilterClearGo == null) return;
            bool has = _pluginsFloatFilterInput != null
                && !string.IsNullOrEmpty(_pluginsFloatFilterInput.text);
            if (_pluginsFloatFilterClearGo.activeSelf != has)
                _pluginsFloatFilterClearGo.SetActive(has);
        }

        private void SetPluginsFloatFilterTextWithoutNotify(string text)
        {
            if (_pluginsFloatFilterInput == null) return;
            string t = text ?? "";
            if (_pluginsFloatFilterInput.text == t)
            {
                RefreshPluginsFloatFilterClearVisible();
                return;
            }
            if (_pluginsFloatFilterOnValueChanged != null)
            {
                _pluginsFloatFilterInput.onValueChanged.RemoveListener(_pluginsFloatFilterOnValueChanged);
                _pluginsFloatFilterInput.text = t;
                _pluginsFloatFilterInput.onValueChanged.AddListener(_pluginsFloatFilterOnValueChanged);
            }
            else
            {
                _pluginsFloatFilterInput.text = t;
            }
            RefreshPluginsFloatFilterClearVisible();
        }

        private void TogglePluginsFloatRatedOnlyFilter()
        {
            _pluginsFloatRatedOnly = !_pluginsFloatRatedOnly;
            SyncPluginsFloatRatedOnlyButton();
            RefreshPluginsFloatTree(true);
            try
            {
                ShowTemporaryStatus(
                    _pluginsFloatRatedOnly
                        ? VPBTranslation.T("gallery.plugins.rated_only_on", "Plugins: rated only (high→low)")
                        : VPBTranslation.T("gallery.plugins.rated_only_off", "Plugins: show all"),
                    1.4f);
            }
            catch { }
        }

        private void SyncPluginsFloatRatedOnlyButton()
        {
            if (_pluginsFloatRatedOnlyBtnText != null)
                _pluginsFloatRatedOnlyBtnText.color = _pluginsFloatRatedOnly ? Color.green : Color.white;
            if (_pluginsFloatRatedOnlyBtnBackdrop != null)
            {
                _pluginsFloatRatedOnlyBtnBackdrop.color = _pluginsFloatRatedOnly
                    ? ColorHistoryAccent
                    : GalleryUiColorTokens.ChromeIconWell;
            }
        }

        private void TogglePluginsFloatCollapsed()
        {
            if (_pluginsFloatPanelRT == null) return;
            float s = _pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : 1f;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;

            if (!_pluginsFloatCollapsed)
            {
                CapturePluginsFloatGeometryToMemory();
                _pluginsFloatExpandHeightRef = _pluginsFloatSavedSizeRef.HasValue
                    ? _pluginsFloatSavedSizeRef.Value.y
                    : GalleryUiDesignTokens.PluginsFloatDefaultHeightRef;
                _pluginsFloatCollapsedTopLeftPos = _pluginsFloatPanelRT.anchoredPosition;
                _pluginsFloatCollapsed = true;
            }
            else
            {
                float h = _pluginsFloatExpandHeightRef.HasValue
                    ? _pluginsFloatExpandHeightRef.Value
                    : GalleryUiDesignTokens.PluginsFloatDefaultHeightRef;
                h = Mathf.Clamp(h,
                    GalleryUiDesignTokens.PluginsFloatMinHeightRef,
                    GalleryUiDesignTokens.PluginsFloatMaxHeightRef);
                float w = _pluginsFloatSavedSizeRef.HasValue
                    ? _pluginsFloatSavedSizeRef.Value.x
                    : GalleryUiDesignTokens.PluginsFloatDefaultWidthRef;
                _pluginsFloatSavedSizeRef = new Vector2(
                    Mathf.Clamp(w, GalleryUiDesignTokens.PluginsFloatMinWidthRef, GalleryUiDesignTokens.PluginsFloatMaxWidthRef),
                    h);
                _pluginsFloatCollapsed = false;
                _pluginsFloatCollapsedTopLeftPos = null;
            }

            SyncPluginsFloatCollapseChrome(titleH);
            if (!_pluginsFloatCollapsed)
            {
                try { RefreshPluginsFloatDestButtons(); } catch { }
            }
            CapturePluginsFloatGeometryToMemory();
            PersistPluginsFloatGeometry();
        }

        private void SyncPluginsFloatCollapseChrome(float titleH)
        {
            if (_pluginsFloatFilterRow != null) _pluginsFloatFilterRow.SetActive(!_pluginsFloatCollapsed);
            if (_pluginsFloatOptionsRow != null) _pluginsFloatOptionsRow.SetActive(!_pluginsFloatCollapsed);
            if (_pluginsFloatScrollHost != null) _pluginsFloatScrollHost.SetActive(!_pluginsFloatCollapsed);
            if (_pluginsFloatFooter != null) _pluginsFloatFooter.SetActive(!_pluginsFloatCollapsed);

            if (_pluginsFloatCollapseIcon != null)
            {
                string path = _pluginsFloatCollapsed ? "vpb_icons/chevron_down.png" : "vpb_icons/chevron_up.png";
                Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
                if (spr != null)
                {
                    _pluginsFloatCollapseIcon.sprite = spr;
                    _pluginsFloatCollapseIcon.color = Color.white;
                }
            }

            if (_pluginsFloatPanelRT != null)
            {
                float s = _pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : 1f;
                if (_pluginsFloatCollapsed)
                {
                    Vector2 size = _pluginsFloatPanelRT.sizeDelta;
                    size.y = titleH;
                    _pluginsFloatPanelRT.sizeDelta = size;
                    if (_pluginsFloatCollapsedTopLeftPos.HasValue)
                        _pluginsFloatPanelRT.anchoredPosition = _pluginsFloatCollapsedTopLeftPos.Value;
                }
                else if (_pluginsFloatSavedSizeRef.HasValue)
                {
                    _pluginsFloatPanelRT.sizeDelta = new Vector2(
                        _pluginsFloatSavedSizeRef.Value.x * s,
                        _pluginsFloatSavedSizeRef.Value.y * s);
                }
            }
        }

        internal bool TryHandlePluginsFloatEsc()
        {
            if (!IsPluginsFloatOpen()) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;

            if (_pluginsFloatFilterInput != null
                && !string.IsNullOrEmpty(_pluginsFloatFilterInput.text))
            {
                SetPluginsFloatFilterTextWithoutNotify("");
                _pluginsFloatFilter = "";
                _pluginsFloatSearchAutoExpandPending = false;
                RefreshPluginsFloatTree(true);
                return true;
            }

            HidePluginsFloat();
            return true;
        }

        private void RescalePluginsFloatIfOpen(float chromeScale)
        {
            if (!IsPluginsFloatOpen()) return;
            if (_pluginsFloatPanelRT == null) return;

            float s = chromeScale > 0f ? chromeScale : ChromeScale;
            if (s <= 0.01f) s = 1f;
            if (Mathf.Abs(s - _pluginsFloatChromeScale) < 0.0005f) return;

            CapturePluginsFloatGeometryToMemory();
            _pluginsFloatChromeScale = s;

            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef * s;

            float panelWRef = _pluginsFloatSavedSizeRef.HasValue
                ? _pluginsFloatSavedSizeRef.Value.x
                : GalleryUiDesignTokens.PluginsFloatDefaultWidthRef;
            float panelHRef = _pluginsFloatSavedSizeRef.HasValue
                ? _pluginsFloatSavedSizeRef.Value.y
                : GalleryUiDesignTokens.PluginsFloatDefaultHeightRef;
            panelWRef = Mathf.Clamp(panelWRef, GalleryUiDesignTokens.PluginsFloatMinWidthRef, GalleryUiDesignTokens.PluginsFloatMaxWidthRef);
            panelHRef = Mathf.Clamp(panelHRef, GalleryUiDesignTokens.PluginsFloatMinHeightRef, GalleryUiDesignTokens.PluginsFloatMaxHeightRef);

            Vector2 keepTopLeft = _pluginsFloatPanelRT.anchoredPosition;
            if (_pluginsFloatCollapsed)
                _pluginsFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, titleH);
            else
            {
                _pluginsFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, panelHRef * s);
                _pluginsFloatExpandHeightRef = panelHRef;
            }
            _pluginsFloatPanelRT.anchoredPosition = keepTopLeft;

            if (_pluginsFloatTitleBarRT != null)
            {
                _pluginsFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
                UI.LayoutFloatTitleWindowIcon(
                    _pluginsFloatTitleBarRT.gameObject,
                    GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
            }

            if (_pluginsFloatFilterRow != null)
            {
                RectTransform filterRT = _pluginsFloatFilterRow.GetComponent<RectTransform>();
                if (filterRT != null)
                {
                    filterRT.sizeDelta = new Vector2(0f, filterH);
                    filterRT.anchoredPosition = new Vector2(0f, -titleH);
                }
            }

            if (_pluginsFloatFilterInput != null)
            {
                LayoutElement inputLe = _pluginsFloatFilterInput.GetComponent<LayoutElement>();
                if (inputLe != null)
                {
                    inputLe.minHeight = searchH;
                    inputLe.preferredHeight = searchH;
                }
                UI.LayoutChromeSearchIcon(_pluginsFloatFilterInput.gameObject, s);
            }

            if (_pluginsFloatFooter != null)
            {
                RectTransform footerRT = _pluginsFloatFooter.GetComponent<RectTransform>();
                if (footerRT != null)
                    footerRT.sizeDelta = new Vector2(0f, footerH);
            }

            if (_pluginsFloatOptionsRow != null)
            {
                RectTransform optRT = _pluginsFloatOptionsRow.GetComponent<RectTransform>();
                float optionsH = GalleryUiDesignTokens.PluginsFloatOptionsRowHeightRef * chromeScale;
                if (optRT != null)
                {
                    optRT.sizeDelta = new Vector2(0f, optionsH);
                    optRT.anchoredPosition = new Vector2(0f, -(titleH + filterH));
                }
            }
            if (_pluginsFloatScrollHost != null)
            {
                RectTransform scrollRT = _pluginsFloatScrollHost.GetComponent<RectTransform>();
                float optionsH = GalleryUiDesignTokens.PluginsFloatOptionsRowHeightRef * chromeScale;
                if (scrollRT != null)
                {
                    scrollRT.offsetMin = new Vector2(0f, footerH);
                    scrollRT.offsetMax = new Vector2(0f, -(titleH + filterH + optionsH));
                }
            }

            RescalePluginsFloatSquareChrome(_pluginsFloatCollapseBtn, chromeSz);
            RescalePluginsFloatSquareChrome(_pluginsFloatExpandAllBtn, chromeSz);
            RescalePluginsFloatSquareChrome(_pluginsFloatCollapseAllBtn, chromeSz);
            RescalePluginsFloatSquareChrome(_pluginsFloatRatedOnlyBtn, chromeSz);
            if (_pluginsFloatDestHost != null)
            {
                LayoutElement destLe = _pluginsFloatDestHost.GetComponent<LayoutElement>();
                if (destLe != null)
                {
                    destLe.minHeight = chromeSz;
                    destLe.preferredHeight = chromeSz;
                    destLe.flexibleHeight = 0f;
                }
                Transform vpTr = _pluginsFloatDestHost.transform.Find("Viewport");
                if (vpTr != null)
                {
                    RectTransform vpRt = vpTr as RectTransform;
                    float rimPad = 2f * s;
                    if (vpRt != null)
                    {
                        vpRt.offsetMin = new Vector2(-rimPad, -rimPad);
                        vpRt.offsetMax = new Vector2(rimPad, rimPad);
                    }
                }
                if (_pluginsFloatDestContentRT != null)
                {
                    float rimPad = 2f * s;
                    _pluginsFloatDestContentRT.anchoredPosition = new Vector2(rimPad, 0f);
                    _pluginsFloatDestContentRT.sizeDelta = new Vector2(_pluginsFloatDestContentRT.sizeDelta.x, chromeSz);
                    HorizontalLayoutGroup hlg = _pluginsFloatDestContentRT.GetComponent<HorizontalLayoutGroup>();
                    if (hlg != null)
                    {
                        int rimPx = Mathf.Max(1, Mathf.RoundToInt(rimPad));
                        hlg.padding = new RectOffset(rimPx, rimPx, 0, 0);
                    }
                }
            }
            if (_pluginsFloatTitleBarRT != null)
            {
                Transform closeTr = _pluginsFloatTitleBarRT.Find("TitleClose");
                if (closeTr != null)
                    RescalePluginsFloatSquareChrome(closeTr.gameObject, chromeSz);
            }

            SyncPluginsFloatCollapseChrome(titleH);
            RefreshPluginsFloatTree(true);
            try { RefreshPluginsFloatDestButtons(); } catch { }
            CapturePluginsFloatGeometryToMemory();
            PersistPluginsFloatGeometry();
        }

        private static void RescalePluginsFloatSquareChrome(GameObject go, float size)
        {
            if (go == null) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) return;
            le.minWidth = le.preferredWidth = size;
            le.minHeight = le.preferredHeight = size;
        }

        private void LoadPluginsFloatGeometryFromConfig()
        {
            _pluginsFloatSavedPosCenter = null;
            _pluginsFloatSavedSizeRef = null;
            try
            {
                if (VPBConfig.Instance == null) return;
                _pluginsFloatLatestOnly = VPBConfig.Instance.GalleryPluginsFloatLatestOnly;
                _pluginsFloatCslistOnly = VPBConfig.Instance.GalleryPluginsFloatCslistOnly;
                if (VPBConfig.Instance.GalleryPluginsFloatPosSaved)
                {
                    _pluginsFloatSavedPosCenter = new Vector2(
                        VPBConfig.Instance.GalleryPluginsFloatPosX,
                        VPBConfig.Instance.GalleryPluginsFloatPosY);
                }
                if (VPBConfig.Instance.GalleryPluginsFloatSizeSaved)
                {
                    float w = VPBConfig.Instance.GalleryPluginsFloatWidthRef;
                    float h = VPBConfig.Instance.GalleryPluginsFloatHeightRef;
                    if (w >= GalleryUiDesignTokens.PluginsFloatMinWidthRef
                        && h >= GalleryUiDesignTokens.PluginsFloatMinHeightRef)
                    {
                        _pluginsFloatSavedSizeRef = new Vector2(
                            Mathf.Clamp(w, GalleryUiDesignTokens.PluginsFloatMinWidthRef, GalleryUiDesignTokens.PluginsFloatMaxWidthRef),
                            Mathf.Clamp(h, GalleryUiDesignTokens.PluginsFloatMinHeightRef, GalleryUiDesignTokens.PluginsFloatMaxHeightRef));
                    }
                }
            }
            catch { }
        }

        private void PersistPluginsFloatLatestOnly()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.GalleryPluginsFloatLatestOnly = _pluginsFloatLatestOnly;
                ScheduleQuickFiltersConfigSave();
            }
            catch { }
        }

        private void PersistPluginsFloatCslistOnly()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.GalleryPluginsFloatCslistOnly = _pluginsFloatCslistOnly;
                ScheduleQuickFiltersConfigSave();
            }
            catch { }
        }

        private void CapturePluginsFloatGeometryToMemory()
        {
            if (_pluginsFloatPanelRT == null) return;
            float s = _pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : 1f;
            _pluginsFloatSavedPosCenter = PluginsFloatTopLeftToCenter(
                _pluginsFloatPanelRT.anchoredPosition, _pluginsFloatPanelRT.sizeDelta);
            if (!_pluginsFloatCollapsed)
            {
                _pluginsFloatSavedSizeRef = new Vector2(
                    Mathf.Clamp(_pluginsFloatPanelRT.sizeDelta.x / s, GalleryUiDesignTokens.PluginsFloatMinWidthRef, GalleryUiDesignTokens.PluginsFloatMaxWidthRef),
                    Mathf.Clamp(_pluginsFloatPanelRT.sizeDelta.y / s, GalleryUiDesignTokens.PluginsFloatMinHeightRef, GalleryUiDesignTokens.PluginsFloatMaxHeightRef));
            }
        }

        private void PersistPluginsFloatGeometry()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                if (_pluginsFloatSavedPosCenter.HasValue)
                {
                    VPBConfig.Instance.GalleryPluginsFloatPosSaved = true;
                    VPBConfig.Instance.GalleryPluginsFloatPosX = _pluginsFloatSavedPosCenter.Value.x;
                    VPBConfig.Instance.GalleryPluginsFloatPosY = _pluginsFloatSavedPosCenter.Value.y;
                }
                if (_pluginsFloatSavedSizeRef.HasValue)
                {
                    VPBConfig.Instance.GalleryPluginsFloatSizeSaved = true;
                    VPBConfig.Instance.GalleryPluginsFloatWidthRef = _pluginsFloatSavedSizeRef.Value.x;
                    VPBConfig.Instance.GalleryPluginsFloatHeightRef = _pluginsFloatSavedSizeRef.Value.y;
                }
            }
            catch { return; }
            try { ScheduleQuickFiltersConfigSave(); } catch { }
        }

        private void OnPluginsFloatMoved()
        {
            CapturePluginsFloatGeometryToMemory();
            PersistPluginsFloatGeometry();
        }

        private void OnPluginsFloatResized()
        {
            if (_pluginsFloatCollapsed) return;
            CapturePluginsFloatGeometryToMemory();
            PersistPluginsFloatGeometry();
        }

        private static Vector2 PluginsFloatCenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 PluginsFloatTopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private static GameObject PluginsFloatSquareIconButton(
            Transform parent, float size, string iconPath, Color backdrop, UnityAction onClick)
        {
            GameObject go = UI.CreateUIButton(
                parent.gameObject, size, size, " ", 14, 0, 0, AnchorPresets.middleCenter, onClick);
            if (go == null) return null;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = size;
            le.minHeight = le.preferredHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            Image bg = go.GetComponent<Image>();
            if (bg != null) bg.color = backdrop;
            try
            {
                Sprite spr = UI.LoadIconSprite(iconPath, UI.BarIconGlyphTint);
                if (spr != null) UI.AddIconToButton(go, spr, 6f, backdrop);
            }
            catch { }
            return go;
        }

        /// <summary>Grid refresh no longer feeds Plugins float (independent catalog).</summary>
        internal void NotifyPluginsFloatAfterGridReady()
        {
            // Intentionally empty — float must not depend on gallery category snapshot.
        }

        internal void NotifyPluginsFloatRatingChanged()
        {
            if (!IsPluginsFloatOpen()) return;
            RefreshPluginsFloatTree(false);
        }
    }
}
