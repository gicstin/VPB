using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    internal enum InsightsFloatTab
    {
        Overview = 0,
        Package = 1,
        Content = 2
    }

    public partial class GalleryPanel
    {
        private enum InsightsContentMode
        {
            Files = 0,
            Duplicates = 1
        }

        private static readonly Color InsightsFloatTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color InsightsFloatFooterBarBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color InsightsFloatPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color InsightsFloatScrollBg = GalleryUiColorTokens.ModalSurface;

        private const int InsightsContentSearchLimit = 300;
        private const int InsightsDuplicateLimit = 300;
        private const long InsightsDuplicateMinBytes = 256 * 1024;

        private GameObject _insightsFloatRoot;
        private RectTransform _insightsFloatPanelRT;
        private RectTransform _insightsFloatTitleBarRT;
        private GameObject _insightsFloatTabRow;
        private GameObject _insightsFloatProgressRow;
        private GameObject _insightsFloatScrollHost;
        private GameObject _insightsFloatFooter;
        private ScrollRect _insightsFloatScrollRect;
        private Transform _insightsBodyParent;
        private GameObject _insightsFloatCollapseBtn;
        private Image _insightsFloatCollapseIcon;
        private RectTransform _insightsProgressFillRT;
        private Text _insightsProgressText;
        private GameObject _insightsScanBtn;
        private Text _insightsScanBtnText;
        private Text _insightsFooterText;
        private readonly List<GameObject> _insightsTabButtons = new List<GameObject>(3);

        private float _insightsFloatChromeScale = 1f;
        private Vector2? _insightsFloatSavedPosCenter;
        private Vector2? _insightsFloatSavedSizeRef;
        private float? _insightsFloatExpandHeightRef;
        private bool _insightsFloatCollapsed;
        private Vector2? _insightsFloatCollapsedTopLeftPos;

        private InsightsFloatTab _insightsTab = InsightsFloatTab.Overview;
        private InsightsContentMode _insightsContentMode = InsightsContentMode.Files;
        private string _insightsFocusUid = "";
        private InputField _insightsContentInput;
        private string _insightsContentQuery = "";
        private readonly List<ContentFileHit> _insightsContentHits = new List<ContentFileHit>(64);
        private readonly List<DuplicateAssetRow> _insightsDuplicates = new List<DuplicateAssetRow>(64);
        private bool _insightsContentSearched;
        private bool _insightsDuplicatesLoaded;
        private string _insightsContentStatus = "";

        private static readonly Color InsightsHeadingColor = new Color(0.84f, 0.88f, 0.94f, 1f);
        private static readonly Color InsightsRowBgA = new Color(0.10f, 0.10f, 0.12f, 1f);
        private static readonly Color InsightsRowBgB = new Color(0.13f, 0.13f, 0.15f, 1f);
        private static readonly Color InsightsZeroText = new Color(0.52f, 0.52f, 0.54f, 1f);
        private static readonly Color InsightsProgressTrack = new Color(0.16f, 0.16f, 0.18f, 1f);
        private static readonly Color InsightsProgressFillColor = new Color(0.34f, 0.56f, 0.44f, 1f);

        private GameObject ResolveInsightsFloatHost()
        {
            if (canvas != null) return canvas.gameObject;
            return backgroundBoxGO;
        }

        internal bool IsInsightsFloatOpen()
        {
            return _insightsFloatRoot != null && _insightsFloatRoot.activeSelf;
        }

        internal void ShowInsightsFloat(string focusUid = null, InsightsFloatTab tab = InsightsFloatTab.Overview)
        {
            EnsureInsightsHooked();

            if (!string.IsNullOrEmpty(focusUid)) _insightsFocusUid = focusUid;
            if (string.IsNullOrEmpty(_insightsFocusUid)) _insightsFocusUid = InsightsResolveFocusUid();
            _insightsTab = tab;
            if (_insightsTab == InsightsFloatTab.Package && string.IsNullOrEmpty(_insightsFocusUid))
                _insightsTab = InsightsFloatTab.Overview;

            EnsureInsightsFloatBuilt();
            if (_insightsFloatRoot == null) return;

            if (_insightsFloatCollapsed) ToggleInsightsFloatCollapsed();

            _insightsFloatRoot.SetActive(true);
            try { _insightsFloatRoot.transform.SetAsLastSibling(); } catch { }
            RebuildInsightsFloatBody();
            RefreshInsightsProgressChrome();
            try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
        }

        internal void ToggleInsightsFloat()
        {
            if (IsInsightsFloatOpen()) HideInsightsFloat();
            else ShowInsightsFloat();
        }

        private void EnsureInsightsFloatBuilt()
        {
            if (_insightsFloatRoot != null) return;
            BuildInsightsFloat();
        }

        private void HideInsightsFloat()
        {
            CaptureInsightsFloatGeometryToMemory();
            PersistInsightsFloatGeometry();
            if (_insightsFloatRoot != null)
                _insightsFloatRoot.SetActive(false);
            try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
        }

        private void DestroyInsightsFloatChrome()
        {
            if (_insightsFloatRoot != null)
            {
                try { UnityEngine.Object.Destroy(_insightsFloatRoot); } catch { }
                _insightsFloatRoot = null;
            }
            _insightsFloatPanelRT = null;
            _insightsFloatTitleBarRT = null;
            _insightsFloatTabRow = null;
            _insightsFloatProgressRow = null;
            _insightsFloatScrollHost = null;
            _insightsFloatFooter = null;
            _insightsFloatScrollRect = null;
            _insightsBodyParent = null;
            _insightsFloatCollapseBtn = null;
            _insightsFloatCollapseIcon = null;
            _insightsProgressFillRT = null;
            _insightsProgressText = null;
            _insightsScanBtn = null;
            _insightsScanBtnText = null;
            _insightsFooterText = null;
            _insightsContentInput = null;
            _insightsTabButtons.Clear();
        }

        private void RefreshInsightsFloatIfOpen()
        {
            if (!IsInsightsFloatOpen()) return;
            if (_insightsFloatCollapsed) return;
            RebuildInsightsFloatBody();
        }

        internal bool TryHandleInsightsFloatEsc()
        {
            if (!IsInsightsFloatOpen()) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;

            if (_insightsContentInput != null && !string.IsNullOrEmpty(_insightsContentInput.text))
            {
                _insightsContentInput.text = "";
                _insightsContentQuery = "";
                _insightsContentSearched = false;
                _insightsContentStatus = "";
                RebuildInsightsFloatBody();
                return true;
            }

            HideInsightsFloat();
            return true;
        }

        private void BuildInsightsFloat()
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            _insightsFloatChromeScale = s;
            var type = new GalleryModalTypography(s);
            int font = type.Body;

            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float tabH = GalleryUiDesignTokens.InsightsFloatTabRowHeightRef * s;

            LoadInsightsFloatGeometryFromConfig();
            float panelWRef = GalleryUiDesignTokens.InsightsFloatDefaultWidthRef;
            float panelHRef = GalleryUiDesignTokens.InsightsFloatDefaultHeightRef;
            if (_insightsFloatSavedSizeRef.HasValue)
            {
                panelWRef = Mathf.Clamp(
                    _insightsFloatSavedSizeRef.Value.x,
                    GalleryUiDesignTokens.InsightsFloatMinWidthRef,
                    GalleryUiDesignTokens.InsightsFloatMaxWidthRef);
                panelHRef = Mathf.Clamp(
                    _insightsFloatSavedSizeRef.Value.y,
                    GalleryUiDesignTokens.InsightsFloatMinHeightRef,
                    GalleryUiDesignTokens.InsightsFloatMaxHeightRef);
            }
            float panelW = panelWRef * s;
            float panelH = panelHRef * s;

            GameObject host = ResolveInsightsFloatHost();
            if (host == null) return;

            _insightsFloatRoot = UI.CreateChildRT(host, "VPB_InsightsFloat", AnchorPresets.stretchAll);
            try { SetLayerRecursive(_insightsFloatRoot, host.layer); } catch { }

            GameObject panel = UI.CreateChildRT(
                _insightsFloatRoot, "Panel", AnchorPresets.middleCenter,
                new Vector2(panelW, panelH), Vector2.zero);
            UI.AddImage(panel, InsightsFloatPanelBg);
            if (panel.GetComponent<RectMask2D>() == null)
                panel.AddComponent<RectMask2D>();
            _insightsFloatPanelRT = panel.GetComponent<RectTransform>();
            if (_insightsFloatPanelRT != null)
            {
                _insightsFloatPanelRT.pivot = new Vector2(0f, 1f);
                _insightsFloatPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _insightsFloatPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _insightsFloatPanelRT.sizeDelta = new Vector2(panelW, panelH);
                Vector2 center = _insightsFloatSavedPosCenter.HasValue
                    ? _insightsFloatSavedPosCenter.Value
                    : new Vector2(120f, 20f);
                _insightsFloatPanelRT.anchoredPosition =
                    InsightsFloatCenterToTopLeft(center, _insightsFloatPanelRT.sizeDelta);
            }

            BuildInsightsFloatTitleBar(panel, font, s, titleH, chromeSz);
            BuildInsightsFloatTabRow(panel, type, s, titleH, tabH);
            BuildInsightsFloatProgressRow(panel, type, s, titleH, tabH);
            BuildInsightsFloatFooter(panel, font, s, footerH, chromeSz);
            BuildInsightsFloatScrollHost(panel, s, titleH, tabH, footerH);

            if (_insightsFloatCollapsed)
                SyncInsightsFloatCollapseChrome(titleH);
            try { UI.ApplyFloatRootHoverPolicy(_insightsFloatRoot); } catch { }
        }

        private void BuildInsightsFloatTitleBar(GameObject panel, int font, float s, float titleH, float chromeSz)
        {
            GameObject titleBar = UI.CreateChildRT(panel, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            Image titleBg = UI.AddImage(titleBar, InsightsFloatTitleBarBg);
            if (titleBg != null) titleBg.raycastTarget = true;
            _insightsFloatTitleBarRT = titleBar.GetComponent<RectTransform>();
            if (_insightsFloatTitleBarRT != null)
            {
                _insightsFloatTitleBarRT.pivot = new Vector2(0.5f, 1f);
                _insightsFloatTitleBarRT.anchoredPosition = Vector2.zero;
                _insightsFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
            }
            HorizontalLayoutGroup titleHlg = UI.AddHLG(
                titleBar, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (titleBar.GetComponent<RectMask2D>() == null)
                titleBar.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(titleBar, "⠇", font,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyFloatTitleBarMetrics(titleHlg, grip.gameObject, s);

            float winIconSz = GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s;
            UI.CreateFloatTitleWindowIcon(titleBar, "info-square", winIconSz);

            Text title = UI.CreateEmphasisTitleLabel(
                titleBar,
                VPBTranslation.T("insights.title", "Package Insights"),
                font, Color.white, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(title.gameObject, flexibleWidth: 1f, minWidth: 80f * s);

            _insightsScanBtn = UI.CreateChromeLayoutButton(
                titleBar.transform, 132f * s, chromeSz,
                VPBTranslation.T("insights.action.scan", "Scan library"), font,
                GalleryUiColorTokens.AccentConfirm, () => InsightsStartFullScan(false));
            _insightsScanBtnText = _insightsScanBtn != null ? _insightsScanBtn.GetComponentInChildren<Text>() : null;
            AddTooltipPlain(_insightsScanBtn, VPBTranslation.T("insights.tip.scan",
                "Reads each package once and caches the result. Packages that have not changed since the last scan are skipped."));

            _insightsFloatCollapseBtn = UI.CreateFloatChromeIconButton(
                titleBar.transform, chromeSz, "chevron-up",
                GalleryUiColorTokens.ChromeIconWell, ToggleInsightsFloatCollapsed);
            if (_insightsFloatCollapseBtn != null)
            {
                _insightsFloatCollapseBtn.name = "CollapseBtn";
                Transform iconTr = _insightsFloatCollapseBtn.transform.Find("Icon");
                _insightsFloatCollapseIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
                AddTooltip(_insightsFloatCollapseBtn, "insights.float_collapse", "Collapse to title bar");
            }

            GameObject closeBtn = UI.CreateFloatChromeIconButton(
                titleBar.transform, chromeSz, "x",
                GalleryUiColorTokens.ChromeIconWell, HideInsightsFloat);
            if (closeBtn != null)
            {
                closeBtn.name = "TitleClose";
                AddTooltip(closeBtn, "insights.float_close", "Close (scans keep running)");
            }

            var headerDrag = titleBar.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = _insightsFloatPanelRT;
            headerDrag.OnMoved = OnInsightsFloatMoved;
        }

        private void BuildInsightsFloatTabRow(GameObject panel, GalleryModalTypography type, float s, float titleH, float tabH)
        {
            _insightsFloatTabRow = UI.CreateChildRT(panel, "TabRow", AnchorPresets.hStretchTop,
                new Vector2(0f, tabH), new Vector2(0f, -titleH));
            RectTransform tabRT = _insightsFloatTabRow.GetComponent<RectTransform>();
            if (tabRT != null)
            {
                tabRT.pivot = new Vector2(0.5f, 1f);
                tabRT.sizeDelta = new Vector2(0f, tabH);
                tabRT.anchoredPosition = new Vector2(0f, -titleH);
            }
            UI.AddImage(_insightsFloatTabRow, GalleryUiColorTokens.SurfaceDarker);
            UI.AddHLG(_insightsFloatTabRow, spacing: 4f * s, padding: UI.Pad(6, 6, 3, 3, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            _insightsTabButtons.Clear();
            float btnH = tabH - 6f * s;
            AddInsightsTabButton(_insightsFloatTabRow, type, s, btnH, InsightsFloatTab.Overview,
                VPBTranslation.T("insights.tab.overview", "Overview"),
                VPBTranslation.T("insights.tip.tab_overview", "What the scan found across the whole library."));
            AddInsightsTabButton(_insightsFloatTabRow, type, s, btnH, InsightsFloatTab.Package,
                VPBTranslation.T("insights.tab.package", "Package"),
                VPBTranslation.T("insights.tip.tab_package", "The full report for one package."));
            AddInsightsTabButton(_insightsFloatTabRow, type, s, btnH, InsightsFloatTab.Content,
                VPBTranslation.T("insights.tab.content", "Content"),
                VPBTranslation.T("insights.tip.tab_content", "Find which package holds a file, and which assets are shipped more than once."));

            GameObject spacer = UI.CreateChildRT(_insightsFloatTabRow, "Spacer");
            UI.AddLE(spacer, minWidth: 0f, flexibleWidth: 1f);
        }

        private void BuildInsightsFloatProgressRow(GameObject panel, GalleryModalTypography type, float s, float titleH, float tabH)
        {
            float progH = GalleryUiDesignTokens.InsightsFloatProgressRowHeightRef * s;
            _insightsFloatProgressRow = UI.CreateChildRT(panel, "ProgressRow", AnchorPresets.hStretchTop,
                new Vector2(0f, progH), new Vector2(0f, -(titleH + tabH)));
            RectTransform progRT = _insightsFloatProgressRow.GetComponent<RectTransform>();
            if (progRT != null)
            {
                progRT.pivot = new Vector2(0.5f, 1f);
                progRT.sizeDelta = new Vector2(0f, progH);
                progRT.anchoredPosition = new Vector2(0f, -(titleH + tabH));
            }
            UI.AddImage(_insightsFloatProgressRow, GalleryUiColorTokens.SurfaceDarker);
            UI.AddHLG(_insightsFloatProgressRow, spacing: 8f * s, padding: UI.Pad(10, 8, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (_insightsFloatProgressRow.GetComponent<RectMask2D>() == null)
                _insightsFloatProgressRow.AddComponent<RectMask2D>();

            float barH = progH - 12f * s;
            GameObject track = UI.CreateChildRT(_insightsFloatProgressRow, "Track");
            UI.AddImage(track, InsightsProgressTrack);
            UI.AddLE(track, minWidth: 80f * s, preferredWidth: 120f * s,
                minHeight: barH, preferredHeight: barH);

            GameObject fill = UI.CreateChildRT(track, "Fill", AnchorPresets.stretchAll);
            UI.AddImage(fill, InsightsProgressFillColor, raycastTarget: false);
            _insightsProgressFillRT = fill.GetComponent<RectTransform>();
            if (_insightsProgressFillRT != null)
            {
                _insightsProgressFillRT.anchorMin = new Vector2(0f, 0f);
                _insightsProgressFillRT.anchorMax = new Vector2(0f, 1f);
                _insightsProgressFillRT.pivot = new Vector2(0f, 0.5f);
                _insightsProgressFillRT.anchoredPosition = Vector2.zero;
                _insightsProgressFillRT.sizeDelta = Vector2.zero;
            }

            _insightsProgressText = UI.CreateLabel(
                _insightsFloatProgressRow, "", type.Caption, GalleryUiColorTokens.TextMuted,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "ProgressText");
            UI.AddLE(_insightsProgressText.gameObject, minWidth: 0f, flexibleWidth: 1f);

            GameObject stopBtn = UI.CreateChromeLayoutButton(
                _insightsFloatProgressRow.transform, 72f * s, progH - 8f * s,
                VPBTranslation.T("insights.action.stop", "Stop"), type.Caption,
                GalleryUiColorTokens.AccentDanger, InsightsCancelScan);
            AddTooltipPlain(stopBtn, VPBTranslation.T("insights.tip.stop",
                "Stops the scan. Packages already read keep their cached result."));

            _insightsFloatProgressRow.SetActive(false);
        }

        private void BuildInsightsFloatFooter(GameObject panel, int font, float s, float footerH, float chromeSz)
        {
            _insightsFloatFooter = UI.CreateChildRT(panel, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(_insightsFloatFooter, InsightsFloatFooterBarBg);
            RectTransform footerRT = _insightsFloatFooter.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(_insightsFloatFooter, spacing: 4f * s, padding: UI.Pad(8, 6, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (_insightsFloatFooter.GetComponent<RectMask2D>() == null)
                _insightsFloatFooter.AddComponent<RectMask2D>();

            GameObject footerDragArea = UI.CreateFloatFooterDragArea(_insightsFloatFooter);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = _insightsFloatPanelRT;
                footerDrag.OnMoved = OnInsightsFloatMoved;
            }

            _insightsFooterText = UI.CreateLabel(
                _insightsFloatFooter, "", font, GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow,
                raycastTarget: false, name: "Hint");
            GalleryUiMetrics.ApplyFont(_insightsFooterText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            if (_insightsFooterText != null)
                _insightsFooterText.verticalOverflow = VerticalWrapMode.Truncate;
            UI.AddLE(_insightsFooterText.gameObject, flexibleWidth: 1f, minWidth: 24f * s);

            GameObject resizeHandle = UI.AddChildGOImage(
                _insightsFloatFooter, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
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
            resizer.Target = _insightsFloatPanelRT;
            resizer.GetMinSize = () => new Vector2(
                GalleryUiDesignTokens.InsightsFloatMinWidthRef * _insightsFloatChromeScale,
                GalleryUiDesignTokens.InsightsFloatMinHeightRef * _insightsFloatChromeScale);
            resizer.GetMaxSize = () => new Vector2(
                GalleryUiDesignTokens.InsightsFloatMaxWidthRef * _insightsFloatChromeScale,
                GalleryUiDesignTokens.InsightsFloatMaxHeightRef * _insightsFloatChromeScale);
            resizer.OnResized = OnInsightsFloatResized;
        }

        private float InsightsFloatScrollTopInset()
        {
            float s = _insightsFloatChromeScale > 0f ? _insightsFloatChromeScale : 1f;
            float inset = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s
                + GalleryUiDesignTokens.InsightsFloatTabRowHeightRef * s;
            if (_insightsFloatProgressRow != null && _insightsFloatProgressRow.activeSelf)
                inset += GalleryUiDesignTokens.InsightsFloatProgressRowHeightRef * s;
            return inset;
        }

        private void SyncInsightsFloatScrollInset()
        {
            if (_insightsFloatScrollHost == null) return;
            RectTransform rt = _insightsFloatScrollHost.GetComponent<RectTransform>();
            if (rt == null) return;
            float want = -InsightsFloatScrollTopInset();
            if (Mathf.Abs(rt.offsetMax.y - want) < 0.5f) return;
            rt.offsetMax = new Vector2(rt.offsetMax.x, want);
        }

        private void BuildInsightsFloatScrollHost(GameObject panel, float s, float titleH, float tabH, float footerH)
        {
            _insightsFloatScrollHost = UI.CreateChildRT(panel, "ScrollHost", AnchorPresets.stretchAll);
            RectTransform scrollRT = _insightsFloatScrollHost.GetComponent<RectTransform>();
            if (scrollRT != null)
            {
                scrollRT.offsetMin = new Vector2(0f, footerH);
                scrollRT.offsetMax = new Vector2(0f, -(titleH + tabH));
            }
            UI.AddImage(_insightsFloatScrollHost, InsightsFloatScrollBg);
            if (_insightsFloatScrollHost.GetComponent<RectMask2D>() == null)
                _insightsFloatScrollHost.AddComponent<RectMask2D>();

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            _insightsFloatScrollRect = _insightsFloatScrollHost.AddComponent<ScrollRect>();
            _insightsFloatScrollRect.horizontal = false;
            _insightsFloatScrollRect.vertical = true;
            _insightsFloatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _insightsFloatScrollRect.scrollSensitivity = VpbScrollTuning.Sensitivity(25f, 1f);

            GameObject viewport = UI.CreateChildRT(_insightsFloatScrollHost, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            if (vpRt != null) vpRt.offsetMax = new Vector2(-sbW, 0f);
            viewport.AddComponent<RectMask2D>();
            _insightsFloatScrollRect.viewport = vpRt;

            GameObject scrollbarGO = UI.CreateScrollBar(_insightsFloatScrollHost, sbW, 0f, Scrollbar.Direction.BottomToTop);
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
            _insightsFloatScrollRect.verticalScrollbar = sb;
            _insightsFloatScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            var minHandle = scrollbarGO.AddComponent<ScrollbarMinHandleHeight>();
            minHandle.minHandlePixels = GalleryUiDesignTokens.PluginsFloatScrollbarMinHandleRef * s;

            GameObject content = UI.CreateChildRT(viewport, "Content");
            RectTransform contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = Vector2.zero;
            UI.AddVLG(content, spacing: 4f * s, padding: UI.Pad(10, 10, 8, 12, s));
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _insightsFloatScrollRect.content = contentRT;
            _insightsBodyParent = content.transform;
        }

        private void ToggleInsightsFloatCollapsed()
        {
            if (_insightsFloatPanelRT == null) return;
            float s = _insightsFloatChromeScale > 0f ? _insightsFloatChromeScale : 1f;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;

            if (!_insightsFloatCollapsed)
            {
                CaptureInsightsFloatGeometryToMemory();
                _insightsFloatExpandHeightRef = _insightsFloatSavedSizeRef.HasValue
                    ? _insightsFloatSavedSizeRef.Value.y
                    : GalleryUiDesignTokens.InsightsFloatDefaultHeightRef;
                _insightsFloatCollapsedTopLeftPos = _insightsFloatPanelRT.anchoredPosition;
                _insightsFloatCollapsed = true;
            }
            else
            {
                float h = _insightsFloatExpandHeightRef.HasValue
                    ? _insightsFloatExpandHeightRef.Value
                    : GalleryUiDesignTokens.InsightsFloatDefaultHeightRef;
                h = Mathf.Clamp(h,
                    GalleryUiDesignTokens.InsightsFloatMinHeightRef,
                    GalleryUiDesignTokens.InsightsFloatMaxHeightRef);
                float w = _insightsFloatSavedSizeRef.HasValue
                    ? _insightsFloatSavedSizeRef.Value.x
                    : GalleryUiDesignTokens.InsightsFloatDefaultWidthRef;
                _insightsFloatSavedSizeRef = new Vector2(
                    Mathf.Clamp(w, GalleryUiDesignTokens.InsightsFloatMinWidthRef, GalleryUiDesignTokens.InsightsFloatMaxWidthRef),
                    h);
                _insightsFloatCollapsed = false;
                _insightsFloatCollapsedTopLeftPos = null;
            }

            SyncInsightsFloatCollapseChrome(titleH);
            if (!_insightsFloatCollapsed) RebuildInsightsFloatBody();
            CaptureInsightsFloatGeometryToMemory();
            PersistInsightsFloatGeometry();
        }

        private void SyncInsightsFloatCollapseChrome(float titleH)
        {
            if (_insightsFloatTabRow != null) _insightsFloatTabRow.SetActive(!_insightsFloatCollapsed);
            if (_insightsFloatScrollHost != null) _insightsFloatScrollHost.SetActive(!_insightsFloatCollapsed);
            if (_insightsFloatFooter != null) _insightsFloatFooter.SetActive(!_insightsFloatCollapsed);
            if (_insightsFloatProgressRow != null)
            {
                bool wantProgress = !_insightsFloatCollapsed && VpbPackageInsightScanner.IsRunning;
                if (_insightsFloatProgressRow.activeSelf != wantProgress)
                    _insightsFloatProgressRow.SetActive(wantProgress);
            }
            if (!_insightsFloatCollapsed) SyncInsightsFloatScrollInset();

            if (_insightsFloatCollapseIcon != null)
            {
                string path = _insightsFloatCollapsed ? "chevron-down" : "chevron-up";
                Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
                if (spr != null)
                {
                    _insightsFloatCollapseIcon.sprite = spr;
                    _insightsFloatCollapseIcon.color = Color.white;
                }
            }

            if (_insightsFloatPanelRT != null)
            {
                float s = _insightsFloatChromeScale > 0f ? _insightsFloatChromeScale : 1f;
                if (_insightsFloatCollapsed)
                {
                    Vector2 size = _insightsFloatPanelRT.sizeDelta;
                    size.y = titleH;
                    _insightsFloatPanelRT.sizeDelta = size;
                    if (_insightsFloatCollapsedTopLeftPos.HasValue)
                        _insightsFloatPanelRT.anchoredPosition = _insightsFloatCollapsedTopLeftPos.Value;
                }
                else if (_insightsFloatSavedSizeRef.HasValue)
                {
                    _insightsFloatPanelRT.sizeDelta = new Vector2(
                        _insightsFloatSavedSizeRef.Value.x * s,
                        _insightsFloatSavedSizeRef.Value.y * s);
                }
            }
        }

        private void LoadInsightsFloatGeometryFromConfig()
        {
            _insightsFloatSavedPosCenter = null;
            _insightsFloatSavedSizeRef = null;
            try
            {
                if (VPBConfig.Instance == null) return;
                if (VPBConfig.Instance.GalleryInsightsFloatPosSaved)
                {
                    _insightsFloatSavedPosCenter = new Vector2(
                        VPBConfig.Instance.GalleryInsightsFloatPosX,
                        VPBConfig.Instance.GalleryInsightsFloatPosY);
                }
                if (VPBConfig.Instance.GalleryInsightsFloatSizeSaved)
                {
                    float w = VPBConfig.Instance.GalleryInsightsFloatWidthRef;
                    float h = VPBConfig.Instance.GalleryInsightsFloatHeightRef;
                    if (w >= GalleryUiDesignTokens.InsightsFloatMinWidthRef
                        && h >= GalleryUiDesignTokens.InsightsFloatMinHeightRef)
                    {
                        _insightsFloatSavedSizeRef = new Vector2(
                            Mathf.Clamp(w, GalleryUiDesignTokens.InsightsFloatMinWidthRef, GalleryUiDesignTokens.InsightsFloatMaxWidthRef),
                            Mathf.Clamp(h, GalleryUiDesignTokens.InsightsFloatMinHeightRef, GalleryUiDesignTokens.InsightsFloatMaxHeightRef));
                    }
                }
            }
            catch { }
        }

        private void CaptureInsightsFloatGeometryToMemory()
        {
            if (_insightsFloatPanelRT == null) return;
            float s = _insightsFloatChromeScale > 0f ? _insightsFloatChromeScale : 1f;
            _insightsFloatSavedPosCenter = InsightsFloatTopLeftToCenter(
                _insightsFloatPanelRT.anchoredPosition, _insightsFloatPanelRT.sizeDelta);
            if (!_insightsFloatCollapsed)
            {
                _insightsFloatSavedSizeRef = new Vector2(
                    Mathf.Clamp(_insightsFloatPanelRT.sizeDelta.x / s, GalleryUiDesignTokens.InsightsFloatMinWidthRef, GalleryUiDesignTokens.InsightsFloatMaxWidthRef),
                    Mathf.Clamp(_insightsFloatPanelRT.sizeDelta.y / s, GalleryUiDesignTokens.InsightsFloatMinHeightRef, GalleryUiDesignTokens.InsightsFloatMaxHeightRef));
            }
        }

        private void PersistInsightsFloatGeometry()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                if (_insightsFloatSavedPosCenter.HasValue)
                {
                    VPBConfig.Instance.GalleryInsightsFloatPosSaved = true;
                    VPBConfig.Instance.GalleryInsightsFloatPosX = _insightsFloatSavedPosCenter.Value.x;
                    VPBConfig.Instance.GalleryInsightsFloatPosY = _insightsFloatSavedPosCenter.Value.y;
                }
                if (_insightsFloatSavedSizeRef.HasValue)
                {
                    VPBConfig.Instance.GalleryInsightsFloatSizeSaved = true;
                    VPBConfig.Instance.GalleryInsightsFloatWidthRef = _insightsFloatSavedSizeRef.Value.x;
                    VPBConfig.Instance.GalleryInsightsFloatHeightRef = _insightsFloatSavedSizeRef.Value.y;
                }
            }
            catch { return; }
            try { ScheduleQuickFiltersConfigSave(); } catch { }
        }

        private void OnInsightsFloatMoved()
        {
            CaptureInsightsFloatGeometryToMemory();
            PersistInsightsFloatGeometry();
        }

        private void OnInsightsFloatResized()
        {
            if (_insightsFloatCollapsed) return;
            CaptureInsightsFloatGeometryToMemory();
            PersistInsightsFloatGeometry();
            RebuildInsightsFloatBody();
        }

        private static Vector2 InsightsFloatCenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 InsightsFloatTopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private void RescaleInsightsFloatIfOpen(float chromeScale)
        {
            if (!IsInsightsFloatOpen()) return;
            if (_insightsFloatPanelRT == null) return;

            float s = chromeScale > 0f ? chromeScale : ChromeScale;
            if (s <= 0.01f) s = 1f;
            if (Mathf.Abs(s - _insightsFloatChromeScale) < 0.0005f) return;

            CaptureInsightsFloatGeometryToMemory();
            _insightsFloatChromeScale = s;

            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float tabH = GalleryUiDesignTokens.InsightsFloatTabRowHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;

            float panelWRef = _insightsFloatSavedSizeRef.HasValue
                ? _insightsFloatSavedSizeRef.Value.x
                : GalleryUiDesignTokens.InsightsFloatDefaultWidthRef;
            float panelHRef = _insightsFloatSavedSizeRef.HasValue
                ? _insightsFloatSavedSizeRef.Value.y
                : GalleryUiDesignTokens.InsightsFloatDefaultHeightRef;
            panelWRef = Mathf.Clamp(panelWRef, GalleryUiDesignTokens.InsightsFloatMinWidthRef, GalleryUiDesignTokens.InsightsFloatMaxWidthRef);
            panelHRef = Mathf.Clamp(panelHRef, GalleryUiDesignTokens.InsightsFloatMinHeightRef, GalleryUiDesignTokens.InsightsFloatMaxHeightRef);

            Vector2 keepTopLeft = _insightsFloatPanelRT.anchoredPosition;
            if (_insightsFloatCollapsed)
                _insightsFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, titleH);
            else
            {
                _insightsFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, panelHRef * s);
                _insightsFloatExpandHeightRef = panelHRef;
            }
            _insightsFloatPanelRT.anchoredPosition = keepTopLeft;

            if (_insightsFloatTitleBarRT != null)
            {
                _insightsFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
                UI.LayoutFloatTitleWindowIcon(
                    _insightsFloatTitleBarRT.gameObject,
                    GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                HorizontalLayoutGroup titleHlg = _insightsFloatTitleBarRT.GetComponent<HorizontalLayoutGroup>();
                Transform gripTr = _insightsFloatTitleBarRT.Find("Grip");
                UI.ApplyFloatTitleBarMetrics(titleHlg, gripTr != null ? gripTr.gameObject : null, s);

                Transform closeTr = _insightsFloatTitleBarRT.Find("TitleClose");
                if (closeTr != null) UI.ScaleFloatChromeIconButton(closeTr.gameObject, chromeSz, s);
                UI.ScaleFloatChromeIconButton(_insightsFloatCollapseBtn, chromeSz, s);
            }

            if (_insightsFloatTabRow != null)
            {
                RectTransform tabRT = _insightsFloatTabRow.GetComponent<RectTransform>();
                if (tabRT != null)
                {
                    tabRT.sizeDelta = new Vector2(0f, tabH);
                    tabRT.anchoredPosition = new Vector2(0f, -titleH);
                }
            }

            if (_insightsFloatProgressRow != null)
            {
                RectTransform progRT = _insightsFloatProgressRow.GetComponent<RectTransform>();
                float progH = GalleryUiDesignTokens.InsightsFloatProgressRowHeightRef * s;
                if (progRT != null)
                {
                    progRT.sizeDelta = new Vector2(0f, progH);
                    progRT.anchoredPosition = new Vector2(0f, -(titleH + tabH));
                }
            }

            if (_insightsFloatFooter != null)
            {
                RectTransform footerRT = _insightsFloatFooter.GetComponent<RectTransform>();
                if (footerRT != null) footerRT.sizeDelta = new Vector2(0f, footerH);
            }

            if (_insightsFloatScrollHost != null)
            {
                RectTransform scrollRT = _insightsFloatScrollHost.GetComponent<RectTransform>();
                if (scrollRT != null)
                    scrollRT.offsetMin = new Vector2(0f, footerH);
                SyncInsightsFloatScrollInset();
            }

            SyncInsightsFloatCollapseChrome(titleH);
            RebuildInsightsFloatBody();
            CaptureInsightsFloatGeometryToMemory();
            PersistInsightsFloatGeometry();
        }

        private void AddInsightsTabButton(
            GameObject row, GalleryModalTypography type, float s, float btnH,
            InsightsFloatTab tab, string label, string tip)
        {
            InsightsFloatTab snap = tab;
            GameObject btn = UI.CreateChromeLayoutButton(
                row.transform, 108f * s, btnH, label, type.Body,
                _insightsTab == tab ? GalleryUiColorTokens.AccentSelected : GalleryUiColorTokens.SegmentIdle,
                () => SetInsightsTab(snap));
            AddTooltipPlain(btn, tip);
            _insightsTabButtons.Add(btn);
        }

        private void SetInsightsTab(InsightsFloatTab tab)
        {
            _insightsTab = tab;
            SyncInsightsTabChrome();
            RebuildInsightsFloatBody();
        }

        private void SyncInsightsTabChrome()
        {
            for (int i = 0; i < _insightsTabButtons.Count; i++)
            {
                GameObject go = _insightsTabButtons[i];
                if (go == null) continue;
                Image img = go.GetComponent<Image>();
                if (img == null) continue;
                img.color = (int)_insightsTab == i
                    ? GalleryUiColorTokens.AccentSelected
                    : GalleryUiColorTokens.SegmentIdle;
            }
        }

        private void RefreshInsightsProgressChrome()
        {
            if (_insightsFloatRoot == null || !_insightsFloatRoot.activeSelf) return;
            bool running = VpbPackageInsightScanner.IsRunning;

            if (_insightsFloatProgressRow != null && _insightsFloatProgressRow.activeSelf != running)
            {
                _insightsFloatProgressRow.SetActive(running);
                SyncInsightsFloatScrollInset();
            }

            if (_insightsScanBtn != null)
            {
                Button b = _insightsScanBtn.GetComponent<Button>();
                if (b != null) b.interactable = !running;
                if (_insightsScanBtnText != null)
                {
                    string want = running
                        ? VPBTranslation.T("insights.action.scanning", "Scanning…")
                        : VPBTranslation.T("insights.action.scan", "Scan library");
                    if (!string.Equals(_insightsScanBtnText.text, want, StringComparison.Ordinal))
                        _insightsScanBtnText.text = want;
                }
            }

            if (!running) return;

            if (_insightsProgressFillRT != null)
            {
                RectTransform track = _insightsProgressFillRT.parent as RectTransform;
                float w = track != null ? track.rect.width : 0f;
                _insightsProgressFillRT.sizeDelta =
                    new Vector2(Mathf.Max(0f, w * VpbPackageInsightScanner.Progress), 0f);
            }

            if (_insightsProgressText != null)
            {
                string txt = string.Format(
                    VPBTranslation.T("insights.progress_fmt", "{0} / {1} · {2} rescanned"),
                    VpbPackageInsightScanner.Processed,
                    VpbPackageInsightScanner.Total,
                    VpbPackageInsightScanner.ScannedThisRun);
                if (!string.Equals(_insightsProgressText.text, txt, StringComparison.Ordinal))
                    _insightsProgressText.text = txt;
            }
        }
    }
}
