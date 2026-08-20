using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Filter-presets list: dropdown under title chip, or detachable floating window
    /// (drag / resize / collapse / close-hides; Dock reattaches). Pos+size+detached flag live in <see cref="VPBConfig"/>.
    /// </summary>
    public partial class QuickFiltersUI
    {
        private const float PanelMaxHeightRef = 500f;
        private const float ScrollBarWidthRef = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef;
        private const float RowTextPadLeftRef = GalleryUiDesignTokens.Space4Ref;
        private const float RowTextPadRightRef = GalleryUiDesignTokens.BandPadRef;
        private const float SplitterHeightRef = GalleryUiDesignTokens.BandPadRef;
        private const float HeaderPadRef = GalleryUiDesignTokens.FloatSearchRowPadRef;
        private const float HeaderSortGapRef = GalleryUiDesignTokens.ControlGapRef;
        /// <summary>Icon pad on ButtonSizeRef squares — matches side-rail / sort chips (glyph fills hit).</summary>
        private const float RowActionIconPadRef = GalleryUiDesignTokens.TightGapRef;
        private const float SoftDeleteUndoSeconds = 5f;

        // Match Settings float chrome (neutral greys).
        private static readonly Color FloatTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color FloatFooterBarBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color FloatPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color ActiveRowAccent = GalleryUiColorTokens.ActiveOn;
        private static readonly Color KeyboardFocusAccent = GalleryUiColorTokens.ActiveFocus;
        private static readonly Color DirtyMarkColor = GalleryUiColorTokens.ActiveWarn;

        private enum ListSortMode
        {
            Manual = 0,
            NameAsc = 1,
            NameDesc = 2,
            PinnedFirst = 3
        }

        private GalleryPanel panel;
        private GameObject dockParentGO;
        private GameObject containerGO;
        private GameObject backdropGO;
        private GameObject titleBarGO;
        private Text titleBarLabel;
        private GameObject headerGO;
        private GameObject footerGO;
        private GameObject scrollHostGO;
        private GameObject scrollContentGO;
        private GameObject scrollbarGO;
        private GameObject viewportGO;
        private ScrollRect scrollRect;
        private RectTransform containerRT;
        private InputField searchInput;
        private GameObject sortBtnGO;
        private Image sortBtnIcon;
        /// <summary>Docked header Float chip (import-sidebar pattern). Hidden while detached.</summary>
        private GameObject headerFloatBtnGO;
        private Text headerFloatBtnText;
        private GameObject collapseBtnGO;
        private Image collapseBtnIcon;
        private GameObject closeBtnGO;
        private GameObject floatDockBtnGO;
        private GameObject floatUndoBtnGO;
        private GameObject floatRedoBtnGO;
        private GameObject floatRemoveModeBtnGO;
        private Image floatRemoveModeBtnIconImage;
        private GameObject floatPresetUndoBtnGO;
        private GameObject searchFilterClearGo;
        private GameObject resizeHandleGO;
        private GameObject collapsePaletteGO;
        private List<GameObject> activeButtons = new List<GameObject>();
        private Dictionary<GameObject, QuickFilterEntry> buttonToEntry = new Dictionary<GameObject, QuickFilterEntry>();
        private QuickFilterEntry renamingEntry;
        /// <summary>Skip InputField onEndEdit when Cancel / Confirm already handled rename.</summary>
        private bool ignoreRenameEndEdit;
        private bool mergeMode;
        /// <summary>Armed delete on this row — inline Confirm/Cancel (browse or edit; no external dialog).</summary>
        private QuickFilterEntry pendingDeleteEntry;
        /// <summary>Row with inline More actions expanded (pin / rename / delete).</summary>
        private QuickFilterEntry expandedMoreEntry;
        /// <summary>Just-saved row — paint once with highlight (change blindness cue).</summary>
        private QuickFilterEntry flashEntry;
        /// <summary>Keyboard highlight in display list (recognition; arrows/Enter/D).</summary>
        private QuickFilterEntry keyboardFocusEntry;
        private QuickFilterEntry softDeleteEntry;
        private int softDeleteIndex = -1;
        private float softDeleteExpireTime;
        private readonly List<QuickFilterEntry> mergeSelection = new List<QuickFilterEntry>();
        private static readonly Color RemoveModeRailBackdrop = new Color(0.62f, 0.16f, 0.16f, 1f);
        private static readonly Color RemoveModeOutlineIdle = Color.white;
        private static readonly Color RemoveModeOutlineActive = new Color(1f, 0.2f, 0.9f, 1f);
        private string listFilter = "";
        private ListSortMode listSortMode = ListSortMode.Manual;
        private bool detached;
        private bool floatCollapsed;
        private float? expandHeightRef;
        private Vector2? savedFloatPosCenter;
        private Vector2? savedFloatSizeRef;
        /// <summary>Top-left anchored pos while collapsed — keeps title edge fixed (pivot top-left).</summary>
        private Vector2? collapsedTopLeftPos;

        public QuickFiltersUI(GalleryPanel panel, GameObject parent)
        {
            this.panel = panel;
            dockParentGO = parent;
            LoadGeometryFromConfig();
            CreateUI(parent);
            SetLayerRecursive(containerGO, parent.layer);
            SetLayerRecursive(backdropGO, parent.layer);
            if (detached)
                ApplyDetachChrome(reposition: true, persist: false);
            else
                ApplyDockChrome(persist: false);
            SetVisible(false);
            Refresh();
        }

        private void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        public GameObject ContainerGO => containerGO;
        public bool IsDetached => detached;

        public void SetVisible(bool visible)
        {
            // Backdrop only for docked dropdown (click-away). Floating: X/Alt+F hide keep detach; Dock reattaches.
            if (backdropGO != null)
            {
                bool showBackdrop = visible && !detached;
                backdropGO.SetActive(showBackdrop);
                if (showBackdrop) backdropGO.transform.SetAsLastSibling();
            }
            if (containerGO != null)
            {
                containerGO.SetActive(visible);
                if (visible) containerGO.transform.SetAsLastSibling();
            }

            if (visible)
            {
                try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
                // Do not focus list-search — trains "type name here" (false affordance).
            }

            if (panel != null)
                panel.SyncQuickFilterToggleState();
        }

        public bool IsVisible => containerGO != null && containerGO.activeSelf;

        private void HideAfterApplyIfDocked()
        {
            if (!detached) SetVisible(false);
        }

        private void CreateUI(GameObject parent)
        {
            backdropGO = UI.CreateChildRT(parent, "QuickFiltersBackdrop", AnchorPresets.stretchAll);
            Image backdropImg = UI.AddImage(backdropGO, new Color(0, 0, 0, 0.001f));
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.targetGraphic = backdropImg;
            backdropBtn.onClick.AddListener(() => SetVisible(false));

            containerGO = UI.CreateChildRT(
                parent,
                "QuickFiltersDropdown",
                AnchorPresets.topMiddle,
                new Vector2(GalleryUiDesignTokens.QuickFiltersPanelWidthRef, 50f),
                new Vector2(-228f, -72f));
            containerRT = containerGO.GetComponent<RectTransform>();
            UI.AddImage(containerGO, FloatPanelBg);
            // Clip chrome + scroll so scrollbar / rows never paint outside panel.
            if (containerGO.GetComponent<RectMask2D>() == null)
                containerGO.AddComponent<RectMask2D>();

            // Floating title bar (grip · title · collapse · close) — shown only when detached.
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef;
            titleBarGO = UI.CreateChildRT(containerGO, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            Image titleBg = UI.AddImage(titleBarGO, FloatTitleBarBg);
            if (titleBg != null) titleBg.raycastTarget = true;
            RectTransform titleRT = titleBarGO.GetComponent<RectTransform>();
            if (titleRT != null)
            {
                titleRT.pivot = new Vector2(0.5f, 1f);
                titleRT.anchoredPosition = Vector2.zero;
                titleRT.sizeDelta = new Vector2(0f, titleH);
            }
            UI.AddHLG(
                titleBarGO, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            Text grip = UI.CreateLabel(titleBarGO, "\u2807", GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            UI.ApplyFloatTitleBarMetrics(
                titleBarGO.GetComponent<HorizontalLayoutGroup>(), grip.gameObject, 1f);

            UI.CreateFloatTitleWindowIcon(
                titleBarGO, "filter-cog", GalleryUiDesignTokens.FloatTitleWindowIconSizeRef);

            titleBarLabel = UI.CreateLabel(
                titleBarGO,
                VPBTranslation.T("quickfilters.float_title", "Filter Presets"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                Color.white, TextAnchor.MiddleLeft, raycastTarget: false, name: "Title");
            UI.AddLE(titleBarLabel.gameObject, flexibleWidth: 1f, minWidth: 60f);
            if (titleBarGO.GetComponent<RectMask2D>() == null)
                titleBarGO.AddComponent<RectMask2D>();

            collapseBtnGO = UI.CreateUIButton(titleBarGO, chromeSz, chromeSz, " ", 16, 0, 0, AnchorPresets.middleCenter, ToggleFloatCollapsed);
            collapseBtnGO.name = "CollapseBtn";
            StyleChromeIconBtn(collapseBtnGO, chromeSz, "chevron-up", GalleryUiColorTokens.ChromeIconWell);
            Transform collapseIconTr = collapseBtnGO.transform.Find("Icon");
            collapseBtnIcon = collapseIconTr != null ? collapseIconTr.GetComponent<Image>() : null;
            var collapseHover = collapseBtnGO.AddComponent<UIHoverDelegate>();
            collapseHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter)
                {
                    panel.SetStatus(floatCollapsed
                        ? VPBTranslation.T("quickfilters.tip.expand", "Expand window")
                        : VPBTranslation.T("quickfilters.tip.collapse", "Collapse to title bar"));
                }
                else panel.SetStatus(null);
            };

            closeBtnGO = UI.CreateUIButton(titleBarGO, chromeSz, chromeSz, " ", 16, 0, 0, AnchorPresets.middleCenter, HideFloatKeepDetach);
            closeBtnGO.name = "TitleClose";
            StyleChromeIconBtn(closeBtnGO, chromeSz, "x", GalleryUiColorTokens.ChromeIconWell);
            var closeHover = closeBtnGO.AddComponent<UIHoverDelegate>();
            closeHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T(
                    "quickfilters.tip.close_hide",
                    "Hide window (keeps floating position). Use Dock in footer to reattach."));
                else panel.SetStatus(null);
            };

            // Collapsed mini palette host (pinned Dice) — filled when collapsed.
            collapsePaletteGO = UI.CreateChildRT(titleBarGO, "CollapsePalette", AnchorPresets.middleCenter,
                new Vector2(0f, chromeSz), Vector2.zero);
            UI.AddHLG(
                collapsePaletteGO, spacing: UI.GapHair(), padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            UI.AddLE(collapsePaletteGO, flexibleWidth: 0f, minWidth: 0f);
            collapsePaletteGO.SetActive(false);
            // Sit between title and collapse/close chrome.
            if (collapseBtnGO != null)
                collapsePaletteGO.transform.SetSiblingIndex(collapseBtnGO.transform.GetSiblingIndex());

            var headerDrag = titleBarGO.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = containerRT;
            headerDrag.OnMoved = OnFloatMoved;

            // Fixed header: search · Float (docked) · sort — match import sidebar chrome (Jakob).
            headerGO = UI.CreateChildRT(containerGO, "Header", AnchorPresets.hStretchTop,
                new Vector2(0f, GalleryUiDesignTokens.FloatSearchRowHeightRef),
                new Vector2(0f, 0f));
            RectTransform headerRT = headerGO.GetComponent<RectTransform>();
            headerRT.pivot = new Vector2(0.5f, 1f);
            UI.AddHLG(
                headerGO,
                spacing: HeaderSortGapRef,
                padding: UI.Pad(HeaderPadRef, HeaderPadRef, HeaderPadRef, HeaderPadRef),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: true);

            float sortSq = GalleryUiDesignTokens.ButtonSizeRef;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef;
            int searchFont = GalleryUiDesignTokens.PopupMenuRowFontRef;

            // Settings-master filter: chrome input + search glyph.
            searchInput = UI.CreateChromeLayoutInputField(
                headerGO.transform,
                searchFont,
                searchH,
                1f,
                8f,
                2f,
                GalleryUiColorTokens.SurfaceDarker,
                UI.InputFieldPlaceholderColor,
                VPBTranslation.T("quickfilters.search_ph", "Search saved presets…"),
                "PresetsFilter");
            if (searchInput != null)
            {
                UI.LayoutChromeSearchIcon(searchInput.gameObject, 1f);
                float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef;
                Transform textAreaTr = searchInput.transform.Find("TextArea");
                if (textAreaTr != null)
                {
                    RectTransform taRt = textAreaTr as RectTransform;
                    if (taRt != null)
                        taRt.offsetMax = new Vector2(-clearSz, taRt.offsetMax.y);
                }
                GameObject clearGo = UI.CreateUIButton(
                    searchInput.gameObject,
                    clearSz, searchH, "X", 24, 0, 0, AnchorPresets.middleRight,
                    () =>
                    {
                        listFilter = "";
                        try { searchInput.text = ""; } catch { }
                        Refresh();
                        try
                        {
                            searchInput.ActivateInputField();
                            searchInput.MoveTextEnd(false);
                        }
                        catch { }
                        RefreshPresetsFilterClearVisible(searchFilterClearGo);
                    });
                searchFilterClearGo = clearGo;
                if (clearGo != null)
                {
                    clearGo.name = "FilterClear";
                    RectTransform clearRT = clearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                    {
                        clearRT.anchorMin = new Vector2(1f, 0f);
                        clearRT.anchorMax = new Vector2(1f, 1f);
                        clearRT.pivot = new Vector2(1f, 0.5f);
                        clearRT.anchoredPosition = Vector2.zero;
                        clearRT.sizeDelta = new Vector2(clearSz, 0f);
                    }
                    Image clearBg = clearGo.GetComponent<Image>();
                    if (clearBg != null) clearBg.color = new Color(0f, 0f, 0f, 0f);
                    try
                    {
                        Sprite xSpr = UI.LoadIconSprite("x", GalleryUiColorTokens.SearchClearIconTint);
                        if (xSpr != null)
                            UI.AddIconToButton(clearGo, xSpr, 6f, new Color(0f, 0f, 0f, 0f));
                    }
                    catch { }
                    Text clearLabel = clearGo.GetComponentInChildren<Text>(true);
                    if (clearLabel != null) clearLabel.text = "";
                    var clearHover = clearGo.AddComponent<UIHoverBorder>();
                    clearHover.hoverColor = new Color(1f, 0.2f, 0.2f, 1f);
                    clearHover.borderSize = 2f;
                    clearHover.inward = true;
                    RefreshPresetsFilterClearVisible(clearGo);
                }

                searchInput.onValueChanged.AddListener(val =>
                {
                    listFilter = val ?? "";
                    RefreshPresetsFilterClearVisible(searchFilterClearGo);
                    Refresh();
                });
                try
                {
                    searchInput.gameObject.AddComponent<CtrlBackspaceWordDeleteHandler>()
                        .Initialize(searchInput);
                }
                catch { }
                var searchHover = searchInput.gameObject.AddComponent<UIHoverDelegate>();
                searchHover.OnHoverChange += (enter) =>
                {
                    if (panel == null) return;
                    if (enter)
                    {
                        panel.SetStatus(VPBTranslation.T(
                            "quickfilters.tip.list_search",
                            "Search saved presets by name. Ctrl+S with text here names a new preset."));
                    }
                    else panel.SetStatus(null);
                };
            }

            // Compact Float chip — docked only (same contract as import header Float).
            headerFloatBtnGO = UI.CreateUIButton(
                headerGO,
                sortSq * 1.6f,
                sortSq,
                VPBTranslation.T("quickfilters.detach_short", "Float"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                Detach);
            if (headerFloatBtnGO != null)
            {
                headerFloatBtnGO.name = "HeaderFloat";
                Image floatImg = headerFloatBtnGO.GetComponent<Image>();
                if (floatImg != null) floatImg.color = GalleryUiColorTokens.ActiveFloatChip;
                Text floatTxt = headerFloatBtnGO.GetComponentInChildren<Text>();
                if (floatTxt != null)
                {
                    headerFloatBtnText = floatTxt;
                    floatTxt.alignment = TextAnchor.MiddleCenter;
                    floatTxt.color = UI.PopupText;
                    try { VPBUiFont.ApplyTo(floatTxt); } catch { }
                }
                UI.AddLE(
                    headerFloatBtnGO,
                    preferredWidth: sortSq * 1.6f,
                    preferredHeight: sortSq,
                    flexibleWidth: 0f);
                var floatHover = headerFloatBtnGO.AddComponent<UIHoverDelegate>();
                floatHover.OnHoverChange += (enter) =>
                {
                    if (panel == null) return;
                    if (enter) panel.SetStatus(VPBTranslation.T(
                        "quickfilters.tip.detach",
                        "Detach as floating window (move / resize). Alt+F toggles."));
                    else panel.SetStatus(null);
                };
                headerFloatBtnGO.SetActive(!detached);
            }

            sortBtnGO = UI.CreateUIButton(headerGO, sortSq, sortSq, " ", 16, 0, 0, AnchorPresets.middleCenter, null);
            if (sortBtnGO != null)
            {
                sortBtnGO.name = "SortBtn";
                UI.AddLE(sortBtnGO, preferredWidth: sortSq, preferredHeight: sortSq, flexibleWidth: 0f);
                Color sortBackdrop = GalleryUiColorTokens.ActiveUtility;
                Sprite sortSpr = UI.LoadIconSprite("sort-ascending-letters", UI.BarIconGlyphTint);
                if (sortSpr != null)
                    UI.AddIconToButton(sortBtnGO, sortSpr, 4f, sortBackdrop);
                Transform iconTr = sortBtnGO.transform.Find("Icon");
                sortBtnIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
                Button sb = sortBtnGO.GetComponent<Button>();
                if (sb != null)
                {
                    sb.onClick.RemoveAllListeners();
                    sb.onClick.AddListener(CycleListSort);
                }
                var sortHover = sortBtnGO.AddComponent<UIHoverDelegate>();
                sortHover.OnHoverChange += (enter) =>
                {
                    if (panel == null) return;
                    if (enter) panel.SetStatus(GetSortStatusTip());
                    else panel.SetStatus(null);
                };
            }

            // Scroll host sits between header/footer — viewport + scrollbar stay inside this rect.
            scrollHostGO = UI.CreateChildRT(containerGO, "ScrollHost", AnchorPresets.stretchAll);
            UI.AddImage(scrollHostGO, GalleryUiColorTokens.ModalSurface);
            if (scrollHostGO.GetComponent<RectMask2D>() == null)
                scrollHostGO.AddComponent<RectMask2D>();

            scrollRect = scrollHostGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = VpbScrollTuning.Sensitivity(25f, 1f);
            scrollRect.verticalScrollbar = null;

            viewportGO = UI.CreateChildRT(scrollHostGO, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRT = viewportGO.GetComponent<RectTransform>();
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = new Vector2(-ScrollBarWidthRef, 0f);
            viewportGO.AddComponent<RectMask2D>();
            scrollRect.viewport = vpRT;

            scrollbarGO = UI.CreateScrollBar(scrollHostGO, ScrollBarWidthRef, 0f, Scrollbar.Direction.BottomToTop);
            Scrollbar scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 0.5f);
            sbRT.offsetMin = new Vector2(-ScrollBarWidthRef, 0f);
            sbRT.offsetMax = Vector2.zero;

            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = scrollRect;
            sync.scrollbar = scrollbar;
            sync.minSizePixels = 20f;

            scrollContentGO = UI.CreateChildRT(viewportGO, "Content", AnchorPresets.hStretchTop);
            RectTransform contentRT = scrollContentGO.GetComponent<RectTransform>();
            scrollRect.content = contentRT;

            UI.AddVLG(
                scrollContentGO,
                spacing: GalleryUiDesignTokens.PopupMenuRowSpacingRef,
                padding: UI.Pad(
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef),
                childAlignment: TextAnchor.UpperCenter,
                childControlHeight: false);

            ContentSizeFitter csf = scrollContentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Float footer: Dock · Undo/Redo/Remove · soft-delete Undo as square icons + resize.
            // Icons keep chrome under QuickFiltersFloatMinWidthRef (text labels overflowed → squeeze/drift).
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef;
            footerGO = UI.CreateChildRT(containerGO, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(footerGO, FloatFooterBarBg);
            RectTransform footerRT = footerGO.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                footerGO, spacing: UI.GapTight(), padding: UI.PadFloatFooter(),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            // No footer RectMask2D — clips bottom-right resize grip / hover rim (DetailStrip same).

            // Full-footer drag hit (behind Dock/Undo/resize) — same job as title bar.
            GameObject footerDragArea = UI.CreateFloatFooterDragArea(footerGO);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = containerRT;
                footerDrag.OnMoved = OnFloatMoved;
            }

            Color footerIconBg = GalleryUiColorTokens.ChromeIconWell;
            floatDockBtnGO = UI.CreateFloatChromeIconButton(
                footerGO.transform, chromeSz, "layout-bottombar", footerIconBg, CloseAndDock);
            if (floatDockBtnGO != null)
            {
                floatDockBtnGO.name = "FloatDock";
                if (floatDockBtnGO.transform.Find("Icon") == null)
                    StyleChromeIconBtn(floatDockBtnGO, chromeSz, "chevron-down", footerIconBg);
            }
            var dockHover = floatDockBtnGO.AddComponent<UIHoverDelegate>();
            dockHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T(
                    "quickfilters.tip.dock",
                    "Dock — reattach under title bar"));
                else panel.SetStatus(null);
            };

            floatUndoBtnGO = UI.CreateFloatChromeIconButton(
                footerGO.transform, chromeSz, "arrow-back-up", footerIconBg, () =>
                {
                    if (panel != null) panel.QuickMenu_Undo();
                });
            if (floatUndoBtnGO != null) floatUndoBtnGO.name = "FloatUndo";
            var undoHover = floatUndoBtnGO.AddComponent<UIHoverDelegate>();
            undoHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("gallery.tooltip.undo", "Undo last change{hint:undo}"));
                else panel.SetStatus(null);
            };

            floatRedoBtnGO = UI.CreateFloatChromeIconButton(
                footerGO.transform, chromeSz, "arrow-forward-up", footerIconBg, () =>
                {
                    if (panel != null) panel.QuickMenu_Redo();
                });
            if (floatRedoBtnGO != null) floatRedoBtnGO.name = "FloatRedo";
            var redoHover = floatRedoBtnGO.AddComponent<UIHoverDelegate>();
            redoHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("gallery.tooltip.redo", "Redo{hint:redo,redo_alt}"));
                else panel.SetStatus(null);
            };

            // Scene Remove Item Mode (not preset delete) — gallery_remove glyph to avoid trash collision.
            floatRemoveModeBtnGO = UI.CreateFloatChromeIconButton(
                footerGO.transform, chromeSz, "backspace",
                RemoveModeRailBackdrop, () =>
                {
                    if (panel != null) panel.ToggleRemoveMode(false, false);
                });
            if (floatRemoveModeBtnGO != null)
            {
                floatRemoveModeBtnGO.name = "FloatRemoveMode";
                if (floatRemoveModeBtnGO.transform.Find("Icon") == null)
                    StyleChromeIconBtn(floatRemoveModeBtnGO, chromeSz, "playlist-x", RemoveModeRailBackdrop);
                Transform rmIconTr = floatRemoveModeBtnGO.transform.Find("Icon");
                floatRemoveModeBtnIconImage = rmIconTr != null ? rmIconTr.GetComponent<Image>() : null;
                if (floatRemoveModeBtnGO.GetComponent<UIHoverBorder>() == null)
                    floatRemoveModeBtnGO.AddComponent<UIHoverBorder>();
            }
            var rmHover = floatRemoveModeBtnGO.AddComponent<UIHoverDelegate>();
            rmHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter)
                {
                    panel.SetStatus(VPBTranslation.T(
                        "quickfilters.tip.scene_remove_mode",
                        "Remove from scene (not presets): point at a loaded item to fade it, click to remove. Preset delete is the trash on each preset row."));
                }
                else panel.SetStatus(null);
            };
            try { SyncRemoveModeButton(panel != null && panel.IsRemoveModeActive); } catch { }

            floatPresetUndoBtnGO = UI.CreateFloatChromeIconButton(
                footerGO.transform, chromeSz, "arrow-back-up",
                new Color(0.16f, 0.36f, 0.28f, 1f), TryUndoSoftDelete);
            if (floatPresetUndoBtnGO != null) floatPresetUndoBtnGO.name = "FloatPresetUndo";
            var presetUndoHover = floatPresetUndoBtnGO.AddComponent<UIHoverDelegate>();
            presetUndoHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T(
                    "quickfilters.tip.soft_undo",
                    "Undo last deleted preset (U)"));
                else panel.SetStatus(null);
            };
            floatPresetUndoBtnGO.SetActive(false);

            // Flexible spacer so resize sits visually on the right.
            GameObject footerSpacer = new GameObject("Spacer");
            footerSpacer.transform.SetParent(footerGO.transform, false);
            footerSpacer.AddComponent<RectTransform>();
            UI.AddLE(footerSpacer, flexibleWidth: 1f, minWidth: 8f);
            UI.EnsureFloatFooterSpacerDragHit(footerSpacer);
            var spacerDrag = footerSpacer.AddComponent<UIFloatPanelDrag>();
            spacerDrag.Target = containerRT;
            spacerDrag.OnMoved = OnFloatMoved;

            float rh = GalleryUiDesignTokens.ButtonSizeRef;
            resizeHandleGO = UI.AddChildGOImage(
                footerGO, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                rh, rh, Vector2.zero, rounded: true);
            resizeHandleGO.name = "ResizeHandle";
            Image rhImg = resizeHandleGO.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            UI.EnsureFloatChromeHoverBorder(resizeHandleGO);
            StyleChromeIconBtn(resizeHandleGO, rh, "chevrons-down-right", UI.IconButtonBackdrop);
            var resizer = resizeHandleGO.AddComponent<UIFloatPanelResize>();
            resizer.Target = containerRT;
            resizer.GetMinSize = GetFloatMinSizeScaled;
            resizer.GetMaxSize = GetFloatMaxSizeScaled;
            resizer.OnResizing = OnFloatResizing;
            resizer.OnResized = OnFloatResized;
            var rhHover = resizeHandleGO.AddComponent<UIHoverDelegate>();
            rhHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.resize", "Drag to resize"));
                else panel.SetStatus(null);
            };

            footerGO.SetActive(false);
            SyncSortButtonVisual();
            SyncMergeChrome();
            SyncSoftDeleteUndoButton();
            try { UI.ApplyFloatRootHoverPolicy(containerGO); } catch { }
        }

        private static void StyleChromeIconBtn(GameObject go, float size, string iconPath, Color? backdropOverride = null)
        {
            UI.StyleFloatChromeIconButton(go, size, iconPath, backdropOverride);
        }

        private static void ScaleChromeIconBtn(GameObject go, float size, float s)
        {
            UI.ScaleFloatChromeIconButton(go, size, s);
        }

        private void RefreshPresetsFilterClearVisible(GameObject clearGo)
        {
            if (clearGo == null) clearGo = searchFilterClearGo;
            if (clearGo == null) return;
            bool has = searchInput != null && !string.IsNullOrEmpty(searchInput.text);
            try { clearGo.SetActive(has); } catch { }
        }

        private string GetSortStatusTip()
        {
            switch (listSortMode)
            {
                case ListSortMode.NameAsc:
                    return VPBTranslation.T("quickfilters.tip.sort_az", "Sort: Name A→Z (click cycle)");
                case ListSortMode.NameDesc:
                    return VPBTranslation.T("quickfilters.tip.sort_za", "Sort: Name Z→A (click cycle)");
                case ListSortMode.PinnedFirst:
                    return VPBTranslation.T("quickfilters.tip.sort_pinned", "Sort: Pinned first (click cycle)");
                default:
                    return VPBTranslation.T("quickfilters.tip.sort_manual", "Sort: Manual order — drag rows (click cycle)");
            }
        }

        private void CycleListSort()
        {
            listSortMode = (ListSortMode)(((int)listSortMode + 1) % 4);
            SyncSortButtonVisual();
            Refresh();
            if (panel != null) panel.SetStatus(GetSortStatusTip());
        }

        private void SyncSortButtonVisual()
        {
            if (sortBtnGO == null) return;
            string path = "sort-ascending-letters";
            switch (listSortMode)
            {
                case ListSortMode.NameDesc:
                    path = "sort-descending-letters";
                    break;
                case ListSortMode.PinnedFirst:
                    path = "pin";
                    break;
                case ListSortMode.Manual:
                    path = "sort-ascending-numbers";
                    break;
            }
            Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
            if (spr == null) return;
            if (sortBtnIcon != null)
            {
                UI.SetIconSprite(sortBtnIcon, spr);
            }
            else
            {
                UI.AddIconToButton(sortBtnGO, spr, 4f, GalleryUiColorTokens.ActiveUtility);
                Transform iconTr = sortBtnGO.transform.Find("Icon");
                sortBtnIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }
        }

        private float HeaderHeightRef()
        {
            return GalleryUiDesignTokens.FloatSearchRowHeightRef;
        }

        private float TitleBarHeightRef()
        {
            return detached ? GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef : 0f;
        }

        private float FooterHeightRef()
        {
            return (detached && !floatCollapsed) ? GalleryUiDesignTokens.QuickFiltersFooterHeightRef : 0f;
        }

        public void ApplyLayout(float s)
        {
            if (containerGO == null) return;
            if (s <= 0f) s = 1f;

            float panelW = ResolvePanelWidthRef() * s;
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            float maxH = PanelMaxHeightRef * s;
            int pad = Mathf.RoundToInt(GalleryUiDesignTokens.PopupMenuPaddingRef * s);
            float spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
            float sbW = ScrollBarWidthRef * s;
            float headerH = HeaderHeightRef() * s;
            float titleH = TitleBarHeightRef() * s;
            float footerH = FooterHeightRef() * s;
            float sortSq = GalleryUiDesignTokens.ButtonSizeRef * s;
            bool showBody = !detached || !floatCollapsed;

            if (titleBarGO != null)
            {
                titleBarGO.SetActive(detached);
                RectTransform trt = titleBarGO.GetComponent<RectTransform>();
                if (trt != null)
                    trt.sizeDelta = new Vector2(0f, titleH);
                if (titleBarLabel != null)
                    GalleryUiMetrics.ApplyFont(titleBarLabel, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                UI.LayoutFloatTitleWindowIcon(titleBarGO, GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                HorizontalLayoutGroup titleHlg = titleBarGO.GetComponent<HorizontalLayoutGroup>();
                Transform gripTr = titleBarGO.transform.Find("Grip");
                UI.ApplyFloatTitleBarMetrics(
                    titleHlg, gripTr != null ? gripTr.gameObject : null, s);
                ScaleChromeIconBtn(collapseBtnGO, sortSq, s);
                ScaleChromeIconBtn(closeBtnGO, sortSq, s);
                ScaleCollapsePaletteChrome(sortSq, s);
                SyncCollapseButtonVisual();
            }

            if (headerGO != null)
            {
                headerGO.SetActive(showBody);
                RectTransform hrt = headerGO.GetComponent<RectTransform>();
                if (hrt != null)
                {
                    hrt.sizeDelta = new Vector2(0f, headerH);
                    hrt.anchoredPosition = new Vector2(0f, -titleH);
                }
                HorizontalLayoutGroup headerHlg = headerGO.GetComponent<HorizontalLayoutGroup>();
                if (headerHlg != null)
                {
                    headerHlg.spacing = HeaderSortGapRef * s;
                    headerHlg.padding = UI.Pad(HeaderPadRef, HeaderPadRef, HeaderPadRef, HeaderPadRef, s);
                }
                // Paint above scroll so search never disappears under list (change-blindness).
                headerGO.transform.SetAsLastSibling();
            }

            if (headerFloatBtnGO != null)
            {
                bool showFloat = showBody && !detached;
                headerFloatBtnGO.SetActive(showFloat);
                LayoutElement floatLe = headerFloatBtnGO.GetComponent<LayoutElement>();
                if (floatLe != null)
                {
                    floatLe.preferredWidth = sortSq * 1.6f;
                    floatLe.preferredHeight = sortSq;
                }
                if (headerFloatBtnText != null)
                    GalleryUiMetrics.ApplyFont(
                        headerFloatBtnText,
                        GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            if (sortBtnGO != null)
            {
                LayoutElement sortLe = sortBtnGO.GetComponent<LayoutElement>();
                if (sortLe != null)
                {
                    sortLe.preferredWidth = sortSq;
                    sortLe.preferredHeight = sortSq;
                }
            }

            if (searchInput != null)
            {
                // Keep search always visible — recognition over hide-when-few (Norman/Jakob).
                if (!searchInput.gameObject.activeSelf)
                    searchInput.gameObject.SetActive(true);
                float searchFieldH = GalleryUiDesignTokens.SearchFieldHeightRef * s;
                LayoutElement searchLe = searchInput.GetComponent<LayoutElement>();
                if (searchLe != null)
                {
                    searchLe.preferredHeight = searchFieldH;
                    searchLe.minHeight = searchFieldH;
                    searchLe.minWidth = 80f * s;
                    searchLe.flexibleWidth = 1f;
                }
                if (searchFilterClearGo != null)
                {
                    float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                    RectTransform clearRT = searchFilterClearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                        clearRT.sizeDelta = new Vector2(clearSz, 0f);
                    UI.LayoutChromeSearchIcon(searchInput.gameObject, s);
                    Transform textAreaTr = searchInput.transform.Find("TextArea");
                    if (textAreaTr != null)
                    {
                        RectTransform taRt = textAreaTr as RectTransform;
                        if (taRt != null)
                            taRt.offsetMax = new Vector2(-clearSz, taRt.offsetMax.y);
                    }
                }
                else
                {
                    UI.LayoutChromeSearchIcon(searchInput.gameObject, s);
                }
                Text ph = searchInput.placeholder as Text;
                if (ph != null)
                    GalleryUiMetrics.ApplyFont(ph, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                if (searchInput.textComponent != null)
                    GalleryUiMetrics.ApplyFont(searchInput.textComponent, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                RefreshPresetsFilterClearVisible(searchFilterClearGo);
            }

            VerticalLayoutGroup vlg = scrollContentGO != null ? scrollContentGO.GetComponent<VerticalLayoutGroup>() : null;
            if (vlg != null)
            {
                vlg.padding = new RectOffset(pad, pad, pad, pad);
                vlg.spacing = spacing;
            }

            float splitH = SplitterHeightRef * s;
            float textPadL = RowTextPadLeftRef * s;
            float textPadR = RowTextPadRightRef * s;
            float iconSq = GalleryUiDesignTokens.ButtonSizeRef * s;
            float iconPadR = GalleryUiDesignTokens.BandPadRef * s;
            float iconGap = GalleryUiDesignTokens.ControlRowGapRef * s;
            float actionGap = GalleryUiDesignTokens.ControlGapRef * s;
            int rowCount = 0;
            int splitterCount = 0;

            if (scrollContentGO != null)
            {
                for (int i = 0; i < scrollContentGO.transform.childCount; i++)
                {
                    Transform ch = scrollContentGO.transform.GetChild(i);
                    if (ch == null || !ch.gameObject.activeSelf) continue;
                    if (ch.name == "Splitter" || ch.name == "PinnedSplitter")
                    {
                        splitterCount++;
                        LayoutElement splitLe = ch.GetComponent<LayoutElement>();
                        if (splitLe != null) splitLe.preferredHeight = splitH;
                        RectTransform splitRT = ch as RectTransform;
                        if (splitRT != null)
                            splitRT.sizeDelta = new Vector2(panelW - pad * 2f - sbW, splitH);
                        Transform line = ch.Find("Line");
                        if (line != null)
                        {
                            RectTransform lineRT = line as RectTransform;
                            if (lineRT != null)
                                lineRT.sizeDelta = new Vector2(-20f * s, Mathf.Max(1f, 1f * s));
                        }
                        continue;
                    }

                    rowCount++;
                    LayoutElement le = ch.GetComponent<LayoutElement>();
                    if (le != null) le.preferredHeight = rowH;

                    RectTransform rowRT = ch as RectTransform;
                    if (rowRT != null)
                        rowRT.sizeDelta = new Vector2(panelW - pad * 2f - sbW, rowH);

                    if (ch.name == "ActionsRow")
                    {
                        HorizontalLayoutGroup hlg = ch.GetComponent<HorizontalLayoutGroup>();
                        if (hlg != null) hlg.spacing = actionGap;
                        for (int ci = 0; ci < ch.childCount; ci++)
                        {
                            Transform act = ch.GetChild(ci);
                            if (act == null) continue;
                            LayoutElement actLe = act.GetComponent<LayoutElement>();
                            if (actLe != null)
                            {
                                actLe.preferredHeight = rowH;
                                actLe.flexibleWidth = 1f;
                            }
                            Text actTxt = act.Find("Text") != null ? act.Find("Text").GetComponent<Text>() : null;
                            if (actTxt == null) actTxt = act.GetComponentInChildren<Text>();
                            if (actTxt != null)
                                GalleryUiMetrics.ApplyFont(actTxt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                            ApplyPopupRowTextInsets(act, textPadL, textPadR);
                        }
                        continue;
                    }

                    if (ch.name == "EmptyHint")
                    {
                        float hintH = rowH * 1.35f;
                        LayoutElement leEmpty = ch.GetComponent<LayoutElement>();
                        if (leEmpty != null) leEmpty.preferredHeight = hintH;
                        if (rowRT != null)
                            rowRT.sizeDelta = new Vector2(panelW - pad * 2f - sbW, hintH);
                        Text et = ch.Find("Text") != null ? ch.Find("Text").GetComponent<Text>() : null;
                        if (et != null)
                            GalleryUiMetrics.ApplyFont(et, GalleryUiDesignTokens.PopupMenuRowFontRef - 1, s, GalleryUiDesignTokens.FontMinRef);
                        ApplyPopupRowTextInsets(ch, textPadL, textPadR);
                        continue;
                    }

                    int actionCount = 0;
                    if (ch.Find("RandomBtn") != null) actionCount++;
                    if (ch.Find("MoreBtn") != null) actionCount++;
                    if (ch.Find("PinBtn") != null) actionCount++;
                    if (ch.Find("RenameBtn") != null) actionCount++;
                    if (ch.Find("DeleteBtn") != null) actionCount++;
                    if (ch.Find("MoreCloseBtn") != null) actionCount++;
                    if (ch.Find("ConfirmDeleteBtn") != null) actionCount++;
                    if (ch.Find("CancelDeleteBtn") != null) actionCount++;
                    if (ch.Find("ConfirmRenameBtn") != null) actionCount++;
                    if (ch.Find("CancelRenameBtn") != null) actionCount++;
                    float editReserve = actionCount > 0
                        ? (iconPadR + actionCount * iconSq + Mathf.Max(0, actionCount - 1) * iconGap)
                        : 0f;
                    ApplyPopupRowTextInsets(ch, textPadL, textPadR, editReserve);

                    Text t = ch.Find("Text") != null ? ch.Find("Text").GetComponent<Text>() : null;
                    if (t != null)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);

                    // Right-edge stack.
                    if (mergeMode)
                    {
                        // Selection mode — no row action chips.
                    }
                    else if (ch.Find("ConfirmRenameBtn") != null)
                    {
                        PlaceRowAction(ch, "ConfirmRenameBtn", iconSq, iconPadR, iconGap, 0);
                        PlaceRowAction(ch, "CancelRenameBtn", iconSq, iconPadR, iconGap, 1);
                    }
                    else if (ch.Find("ConfirmDeleteBtn") != null)
                    {
                        PlaceRowAction(ch, "ConfirmDeleteBtn", iconSq, iconPadR, iconGap, 0);
                        PlaceRowAction(ch, "CancelDeleteBtn", iconSq, iconPadR, iconGap, 1);
                    }
                    else if (ch.Find("MoreCloseBtn") != null)
                    {
                        // Expanded more: close · delete · rename · pin (no color).
                        PlaceRowAction(ch, "MoreCloseBtn", iconSq, iconPadR, iconGap, 0);
                        PlaceRowAction(ch, "DeleteBtn", iconSq, iconPadR, iconGap, 1);
                        PlaceRowAction(ch, "RenameBtn", iconSq, iconPadR, iconGap, 2);
                        PlaceRowAction(ch, "PinBtn", iconSq, iconPadR, iconGap, 3);
                    }
                    else
                    {
                        PlaceRowAction(ch, "RandomBtn", iconSq, iconPadR, iconGap, 0);
                        PlaceRowAction(ch, "MoreBtn", iconSq, iconPadR, iconGap, 1);
                    }

                    float iconPad = RowActionIconPadRef * s;
                    ScaleRowActionIcon(ch, "RandomBtn", iconPad);
                    ScaleRowActionIcon(ch, "MoreBtn", iconPad);
                    ScaleRowActionIcon(ch, "PinBtn", iconPad);
                    ScaleRowActionIcon(ch, "RenameBtn", iconPad);
                    ScaleRowActionIcon(ch, "DeleteBtn", iconPad);
                    ScaleRowActionIcon(ch, "MoreCloseBtn", iconPad);
                    ScaleRowActionIcon(ch, "ConfirmDeleteBtn", iconPad);
                    ScaleRowActionIcon(ch, "CancelDeleteBtn", iconPad);
                    ScaleRowActionIcon(ch, "ConfirmRenameBtn", iconPad);
                    ScaleRowActionIcon(ch, "CancelRenameBtn", iconPad);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContentGO.GetComponent<RectTransform>());
            }

            int childCount = 0;
            if (scrollContentGO != null)
            {
                for (int i = 0; i < scrollContentGO.transform.childCount; i++)
                {
                    Transform ch = scrollContentGO.transform.GetChild(i);
                    if (ch != null && ch.gameObject.activeSelf) childCount++;
                }
            }
            float contentH = pad * 2f
                + rowCount * rowH
                + splitterCount * splitH
                + Mathf.Max(0, childCount - 1) * spacing;
            RectTransform contentRT = scrollContentGO != null ? scrollContentGO.GetComponent<RectTransform>() : null;
            if (contentRT != null && contentRT.rect.height > 1f)
                contentH = contentRT.rect.height;

            float panelH;
            if (detached)
            {
                if (floatCollapsed)
                    panelH = titleH;
                else
                    panelH = ResolvePanelHeightRef() * s;
            }
            else
            {
                panelH = Mathf.Min(maxH, titleH + headerH + contentH);
            }

            if (containerRT != null)
                containerRT.sizeDelta = new Vector2(panelW, panelH);

            float topChrome = showBody ? (titleH + headerH) : titleH;
            float bottomChrome = footerH;

            if (scrollHostGO != null)
            {
                scrollHostGO.SetActive(showBody);
                RectTransform shRT = scrollHostGO.GetComponent<RectTransform>();
                if (shRT != null)
                {
                    shRT.offsetMin = new Vector2(0f, bottomChrome);
                    shRT.offsetMax = new Vector2(0f, -topChrome);
                }
            }

            if (viewportGO != null)
            {
                viewportGO.SetActive(showBody);
                RectTransform vpRT = viewportGO.GetComponent<RectTransform>();
                if (vpRT != null)
                {
                    // Fill scroll host; leave right strip for scrollbar only.
                    vpRT.offsetMin = Vector2.zero;
                    vpRT.offsetMax = new Vector2(-sbW, 0f);
                }
            }

            if (scrollbarGO != null)
            {
                scrollbarGO.SetActive(showBody);
                RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
                if (sbRT != null)
                {
                    sbRT.anchorMin = new Vector2(1f, 0f);
                    sbRT.anchorMax = new Vector2(1f, 1f);
                    sbRT.pivot = new Vector2(1f, 0.5f);
                    sbRT.offsetMin = new Vector2(-sbW, 0f);
                    sbRT.offsetMax = Vector2.zero;
                    sbRT.sizeDelta = new Vector2(sbW, 0f);
                }
                BoxCollider bc = scrollbarGO.GetComponent<BoxCollider>();
                if (bc != null && sbRT != null)
                {
                    float trackH = Mathf.Max(1f, sbRT.rect.height);
                    bc.size = new Vector3(sbW, trackH, 1f);
                    bc.center = new Vector3(-sbW * 0.5f, 0f, 0f);
                }
            }

            if (footerGO != null)
            {
                bool showFooter = detached && !floatCollapsed;
                footerGO.SetActive(showFooter);
                RectTransform frt = footerGO.GetComponent<RectTransform>();
                if (frt != null)
                    frt.sizeDelta = new Vector2(0f, footerH);
                HorizontalLayoutGroup footerHlg = footerGO.GetComponent<HorizontalLayoutGroup>();
                if (footerHlg != null)
                {
                    footerHlg.spacing = 4f * s;
                    footerHlg.padding = UI.PadFloatFooter(s);
                }
                ScaleChromeIconBtn(floatDockBtnGO, sortSq, s);
                ScaleChromeIconBtn(floatUndoBtnGO, sortSq, s);
                ScaleChromeIconBtn(floatRedoBtnGO, sortSq, s);
                ScaleChromeIconBtn(floatRemoveModeBtnGO, sortSq, s);
                ScaleChromeIconBtn(floatPresetUndoBtnGO, sortSq, s);
                ScaleChromeIconBtn(resizeHandleGO, sortSq, s);
                SyncMergeChrome();
                SyncSoftDeleteUndoButton();
                try { SyncRemoveModeButton(panel != null && panel.IsRemoveModeActive); } catch { }
                if (showFooter)
                    footerGO.transform.SetAsLastSibling();
            }

            if (titleBarGO != null && detached)
                titleBarGO.transform.SetAsLastSibling();

            if (containerRT != null && panel != null)
            {
                if (detached)
                {
                    // Pivot top-left: keep title corner fixed on UI scale / resize.
                    // Re-applying saved center→topLeft drifts the window when size changes.
                    // Size already applied above from savedFloatSizeRef — do not touch anchors from docked parent.
                    containerRT.anchorMin = new Vector2(0.5f, 0.5f);
                    containerRT.anchorMax = new Vector2(0.5f, 0.5f);
                    containerRT.pivot = new Vector2(0f, 1f);
                    if (floatCollapsed && collapsedTopLeftPos.HasValue)
                        containerRT.anchoredPosition = collapsedTopLeftPos.Value;
                    else
                    {
                        // Keep top-left after sizeDelta write (pivot top-left); sync memory only.
                        // Memory + Instance fields only — no ScheduleSave (ApplyLayout is hot on rebuild).
                        // UI-scale hotkey deferred Save flushes disk.
                        CaptureFloatGeometryToMemory();
                        try { PersistGeometryFieldsOnly(); } catch { }
                    }
                }
                else
                    ApplyDockedAnchorsAndPos(s);
            }
        }

        private void ApplyDockedAnchorsAndPos(float s)
        {
            if (containerRT == null) return;
            RectTransform btnRT = panel != null ? panel.QuickFiltersToggleBtnRT : null;
            containerRT.anchorMin = new Vector2(0.5f, 1f);
            containerRT.anchorMax = new Vector2(0.5f, 1f);
            containerRT.pivot = new Vector2(0.5f, 1f);
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            float x = btnRT != null ? btnRT.anchoredPosition.x : -228f * s;
            containerRT.anchoredPosition = new Vector2(
                x,
                -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
        }

        /// <summary>
        /// Apply float anchors + saved size + center→topLeft together.
        /// Must set size before position — docked sizeDelta with saved center places wrong
        /// top-left; ApplyLayout then grows size and Capture corrupts saved center (Import pattern).
        /// </summary>
        private void ApplyFloatAnchorsAndPos(float s)
        {
            if (containerRT == null) return;
            float ss = s > 0f ? s : 1f;
            containerRT.anchorMin = new Vector2(0.5f, 0.5f);
            containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            containerRT.pivot = new Vector2(0f, 1f);

            float wRef = ResolvePanelWidthRef();
            float hRef = floatCollapsed
                ? GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef
                : ResolvePanelHeightRef();
            Vector2 size = new Vector2(wRef * ss, hRef * ss);
            containerRT.sizeDelta = size;

            Vector2 center = ResolveFloatCenterPos();
            containerRT.anchoredPosition = CenterToTopLeft(center, size);
        }

        private float ResolvePanelWidthRef()
        {
            if (detached && savedFloatSizeRef.HasValue)
            {
                return Mathf.Clamp(
                    savedFloatSizeRef.Value.x,
                    GalleryUiDesignTokens.QuickFiltersFloatMinWidthRef,
                    GalleryUiDesignTokens.QuickFiltersFloatMaxWidthRef);
            }
            return GalleryUiDesignTokens.QuickFiltersPanelWidthRef;
        }

        private float ResolvePanelHeightRef()
        {
            if (savedFloatSizeRef.HasValue)
            {
                return Mathf.Clamp(
                    savedFloatSizeRef.Value.y,
                    GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef,
                    GalleryUiDesignTokens.QuickFiltersFloatMaxHeightRef);
            }
            return GalleryUiDesignTokens.QuickFiltersFloatDefaultHeightRef;
        }

        private Vector2 ResolveFloatCenterPos()
        {
            if (savedFloatPosCenter.HasValue)
                return savedFloatPosCenter.Value;
            return Vector2.zero;
        }

        private static Vector2 CenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 TopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private static void PlaceRowAction(Transform row, string name, float sq, float padR, float gap, int fromRight)
        {
            Transform t = row.Find(name);
            if (t == null) return;
            RectTransform irt = t as RectTransform;
            if (irt == null) return;
            irt.sizeDelta = new Vector2(sq, sq);
            irt.anchoredPosition = new Vector2(-(padR + fromRight * (sq + gap)), 0f);
        }

        private static void ScaleRowActionIcon(Transform row, string name, float iconPad)
        {
            Transform t = row.Find(name);
            if (t == null) return;
            Transform icon = t.Find("Icon");
            if (icon == null) return;
            RectTransform irt = icon as RectTransform;
            if (irt == null) return;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(-iconPad * 2f, -iconPad * 2f);
        }

        public void Refresh()
        {
            // renamingEntry / pendingDeleteEntry / expandedMoreEntry kept — row rebuilds into that chrome.
            // flashEntry kept for this rebuild only — cleared at end.
            foreach (var btn in activeButtons) GameObject.Destroy(btn);
            activeButtons.Clear();
            buttonToEntry.Clear();

            CreateActionsRow();
            CreateSplitter("Splitter");

            List<QuickFilterEntry> display = BuildDisplayList();
            if (display.Count == 0)
                CreateEmptyHintRow();

            bool anyPinned = false;
            bool insertedPinSep = false;
            for (int i = 0; i < display.Count; i++)
            {
                QuickFilterEntry e = display[i];
                if (e == null) continue;
                if (e.Pinned) anyPinned = true;
                else if (anyPinned && !insertedPinSep)
                {
                    CreateSplitter("PinnedSplitter");
                    insertedPinSep = true;
                }
                CreateFilterButton(e);
            }

            if (containerGO != null)
                SetLayerRecursive(containerGO, containerGO.layer);

            try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
            flashEntry = null;
            SyncSoftDeleteUndoButton();
            SyncCollapsePalette();
            if (renamingEntry != null)
                FocusRenameField(renamingEntry);
        }

        private void CreateEmptyHintRow()
        {
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * 1.35f;
            float rowW = GalleryUiDesignTokens.QuickFiltersPanelWidthRef - 12f;

            bool filtered = !string.IsNullOrEmpty((listFilter ?? "").Trim());
            if (!filtered)
            {
                GameObject cta = UI.CreateUIButton(
                    scrollContentGO,
                    rowW,
                    rowH,
                    VPBTranslation.T("quickfilters.empty_cta", "Save current filters"),
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0, 0,
                    AnchorPresets.middleCenter,
                    () => CaptureCurrentFilter(false));
                if (cta != null)
                {
                    cta.name = "EmptyHintCta";
                    Image img = cta.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.16f, 0.36f, 0.30f, 1f);
                    Text txt = cta.GetComponentInChildren<Text>();
                    if (txt != null)
                    {
                        txt.alignment = TextAnchor.MiddleCenter;
                        txt.color = UI.PopupText;
                        VPBUiFont.ApplyTo(txt);
                    }
                    ApplyPopupRowTextInsets(cta.transform, RowTextPadLeftRef, RowTextPadRightRef);
                    UI.AddLE(cta, preferredHeight: rowH, flexibleWidth: 1f);
                    var h = cta.AddComponent<UIHoverDelegate>();
                    h.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(VPBTranslation.T(
                            "quickfilters.empty_hint",
                            "Save stores the gallery’s current filters. Then click a row or use Dice."));
                        else panel.SetStatus(null);
                    };
                    activeButtons.Add(cta);
                }
                return;
            }

            GameObject row = new GameObject("EmptyHint");
            row.transform.SetParent(scrollContentGO.transform, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(rowW, rowH);
            UI.AddLE(row, preferredHeight: rowH, flexibleWidth: 1f);

            Text label = UI.CreateLabel(
                row,
                VPBTranslation.T("quickfilters.empty_filtered", "No presets match this search."),
                GalleryUiDesignTokens.PopupMenuRowFontRef - 1,
                new Color(0.72f, 0.74f, 0.78f, 1f),
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "Text");
            if (label != null)
            {
                VPBUiFont.ApplyTo(label);
                ApplyPopupRowTextInsets(row.transform, RowTextPadLeftRef, RowTextPadRightRef);
            }

            activeButtons.Add(row);
        }

        private List<QuickFilterEntry> BuildDisplayList()
        {
            var src = QuickFilterSettings.Instance.Filters;
            var list = new List<QuickFilterEntry>();
            if (src == null) return list;

            string q = (listFilter ?? "").Trim();
            bool filter = q.Length > 0;
            string qLower = filter ? q.ToLowerInvariant() : null;

            for (int i = 0; i < src.Count; i++)
            {
                QuickFilterEntry e = src[i];
                if (e == null) continue;
                if (filter)
                {
                    string name = e.Name ?? "";
                    if (name.ToLowerInvariant().IndexOf(qLower) < 0)
                        continue;
                }
                list.Add(e);
            }

            // Always pin-group at top (chunking) — sort applies inside each group.
            var pinned = new List<QuickFilterEntry>();
            var unpinned = new List<QuickFilterEntry>();
            for (int i = 0; i < list.Count; i++)
            {
                QuickFilterEntry e = list[i];
                if (e != null && e.Pinned) pinned.Add(e);
                else unpinned.Add(e);
            }

            Comparison<QuickFilterEntry> byNameAsc = (a, b) =>
                string.Compare(a != null ? a.Name : "", b != null ? b.Name : "", StringComparison.OrdinalIgnoreCase);
            Comparison<QuickFilterEntry> byNameDesc = (a, b) =>
                string.Compare(b != null ? b.Name : "", a != null ? a.Name : "", StringComparison.OrdinalIgnoreCase);

            if (listSortMode == ListSortMode.NameAsc)
            {
                pinned.Sort(byNameAsc);
                unpinned.Sort(byNameAsc);
            }
            else if (listSortMode == ListSortMode.NameDesc)
            {
                pinned.Sort(byNameDesc);
                unpinned.Sort(byNameDesc);
            }
            // Manual + PinnedFirst: keep source-relative order inside each group.

            list.Clear();
            list.AddRange(pinned);
            list.AddRange(unpinned);
            return list;
        }

        private bool AllowDragReorder()
        {
            return listSortMode == ListSortMode.Manual
                && string.IsNullOrEmpty((listFilter ?? "").Trim());
        }

        private static void ApplyPopupRowTextInsets(Transform row, float padLeft, float padRight, float editIconsReserve = 0f)
        {
            if (row == null) return;
            Transform textT = row.Find("Text");
            if (textT == null) return;
            RectTransform txtRT = textT as RectTransform;
            if (txtRT == null) return;
            float right = editIconsReserve > 0f ? Mathf.Max(padRight, editIconsReserve) : padRight;
            txtRT.offsetMin = new Vector2(padLeft, 0f);
            txtRT.offsetMax = new Vector2(-right, 0f);
        }

        /// <summary>Leading non-filter rows in scroll content (ActionsRow, Splitter).</summary>
        private int ListChromeChildCount => 2;

        private void CreateActionsRow()
        {
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            float rowW = GalleryUiDesignTokens.QuickFiltersPanelWidthRef - 12f;

            GameObject row = new GameObject("ActionsRow");
            row.transform.SetParent(scrollContentGO.transform, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(rowW, rowH);
            UI.AddHLG(
                row,
                spacing: UI.GapTight(),
                padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: true,
                childForceExpandHeight: true);
            UI.AddLE(row, preferredHeight: rowH, flexibleWidth: 1f);

            GameObject saveBtn = UI.CreateUIButton(
                row,
                rowW * 0.4f,
                rowH,
                VPBTranslation.T("quickfilters.save_preset", "Save Preset"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                () => { CaptureCurrentFilter(false); });
            if (saveBtn != null)
            {
                saveBtn.name = "SaveBtn";
                Image img = saveBtn.GetComponent<Image>();
                if (img != null) img.color = UI.PopupRowBackdrop;
                Text txt = saveBtn.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.color = UI.PopupText;
                    VPBUiFont.ApplyTo(txt);
                }
                ApplyPopupRowTextInsets(saveBtn.transform, RowTextPadLeftRef, RowTextPadRightRef);
                UI.AddLE(saveBtn, preferredHeight: rowH, flexibleWidth: 1f, minWidth: 60f);
                var del = saveBtn.AddComponent<UIHoverDelegate>();
                del.OnHoverChange += (enter) =>
                {
                    if (panel == null) return;
                    if (enter) panel.SetStatus(VPBTranslation.T(
                        "quickfilters.tip.save",
                        "Save gallery filters as a new preset, then rename. Ctrl+S: update dirty active, or name from search."));
                    else panel.SetStatus(null);
                };
            }

            bool canUpdate = panel != null
                && panel.ActiveQuickFilter != null
                && !mergeMode;
            if (canUpdate)
            {
                bool dirty = false;
                try { dirty = panel.IsActiveQuickFilterDirty(); } catch { }
                GameObject updateBtn = UI.CreateUIButton(
                    row,
                    rowW * 0.3f,
                    rowH,
                    dirty
                        ? VPBTranslation.T("quickfilters.update_dirty", "Update *")
                        : VPBTranslation.T("quickfilters.update_preset", "Update"),
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0, 0,
                    AnchorPresets.middleCenter,
                    UpdateActivePreset);
                if (updateBtn != null)
                {
                    updateBtn.name = "UpdateBtn";
                    Image img = updateBtn.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = dirty
                            ? new Color(0.32f, 0.42f, 0.22f, 1f)
                            : new Color(0.22f, 0.34f, 0.28f, 1f);
                    }
                    Text txt = updateBtn.GetComponentInChildren<Text>();
                    if (txt != null)
                    {
                        txt.alignment = TextAnchor.MiddleCenter;
                        txt.color = dirty ? DirtyMarkColor : UI.PopupText;
                        VPBUiFont.ApplyTo(txt);
                    }
                    ApplyPopupRowTextInsets(updateBtn.transform, RowTextPadLeftRef, RowTextPadRightRef);
                    UI.AddLE(updateBtn, preferredHeight: rowH, flexibleWidth: 1f, minWidth: 50f);
                    var uh = updateBtn.AddComponent<UIHoverDelegate>();
                    uh.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter)
                        {
                            string n = panel.ActiveQuickFilter != null ? panel.ActiveQuickFilter.Name : "";
                            panel.SetStatus(string.Format(
                                VPBTranslation.T("quickfilters.tip.update", "Overwrite '{0}' with current gallery filters (keeps name)"),
                                n ?? ""));
                        }
                        else panel.SetStatus(null);
                    };
                }
            }

            // Merge Cancel occupies former Edit slot — only while picking merge members.
            if (mergeMode)
            {
                GameObject cancelBtn = UI.CreateUIButton(
                    row,
                    rowW * 0.3f,
                    rowH,
                    VPBTranslation.T("quickfilters.merge_cancel", "Cancel"),
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0, 0,
                    AnchorPresets.middleCenter,
                    ExitMergeMode);
                if (cancelBtn != null)
                {
                    cancelBtn.name = "MergeCancelBtn";
                    Image img = cancelBtn.GetComponent<Image>();
                    if (img != null) img.color = UI.PopupRowActiveBackdrop;
                    Text txt = cancelBtn.GetComponentInChildren<Text>();
                    if (txt != null)
                    {
                        txt.alignment = TextAnchor.MiddleCenter;
                        txt.color = UI.PopupText;
                        VPBUiFont.ApplyTo(txt);
                    }
                    ApplyPopupRowTextInsets(cancelBtn.transform, RowTextPadLeftRef, RowTextPadRightRef);
                    UI.AddLE(cancelBtn, preferredHeight: rowH, flexibleWidth: 1f, minWidth: 50f);
                }
            }

            string mergeLabel = mergeMode
                ? string.Format(
                    VPBTranslation.T("quickfilters.merge_create_n", "Create ({0}/{1})"),
                    mergeSelection.Count,
                    GalleryUiDesignTokens.QuickFiltersMergeMaxMembers)
                : VPBTranslation.T("quickfilters.merge_to_new", "Merge to New");
            GameObject mergeBtn = UI.CreateUIButton(
                row,
                rowW * 0.4f,
                rowH,
                mergeLabel,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                OnMergePrimary);
            if (mergeBtn != null)
            {
                mergeBtn.name = "MergeBtn";
                Image img = mergeBtn.GetComponent<Image>();
                bool ready = mergeMode && mergeSelection.Count >= 2;
                if (img != null)
                {
                    img.color = mergeMode
                        ? (ready ? new Color(0.16f, 0.42f, 0.32f, 1f) : UI.PopupRowActiveBackdrop)
                        : new Color(0.18f, 0.34f, 0.42f, 1f);
                }
                Text txt = mergeBtn.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.color = ready || !mergeMode ? UI.PopupText : UI.PopupMutedText;
                    VPBUiFont.ApplyTo(txt);
                }
                ApplyPopupRowTextInsets(mergeBtn.transform, RowTextPadLeftRef, RowTextPadRightRef);
                UI.AddLE(mergeBtn, preferredHeight: rowH, flexibleWidth: 1f, minWidth: 60f);
                var mh = mergeBtn.AddComponent<UIHoverDelegate>();
                mh.OnHoverChange += (enter) =>
                {
                    if (panel == null) return;
                    if (enter)
                    {
                        panel.SetStatus(mergeMode
                            ? VPBTranslation.T("quickfilters.tip.merge_confirm", "Create merged preset from selection (dice loads each filter)")
                            : VPBTranslation.T("quickfilters.tip.merge", "Merge 2+ presets into one multi-random preset"));
                    }
                    else panel.SetStatus(null);
                };
            }

            activeButtons.Add(row);
        }

        private void CreateSplitter(string name = "Splitter")
        {
            GameObject splitter = new GameObject(name);
            splitter.transform.SetParent(scrollContentGO.transform, false);
            var splitRT = splitter.AddComponent<RectTransform>();
            splitRT.sizeDelta = new Vector2(0f, SplitterHeightRef);
            UI.AddLE(splitter, preferredHeight: SplitterHeightRef, flexibleWidth: 1f);

            GameObject line = UI.CreateChildRT(splitter, "Line", AnchorPresets.hStretchMiddle, new Vector2(-20f, 1f));
            UI.AddImage(line, new Color(0.5f, 0.5f, 0.5f, 0.35f));

            activeButtons.Add(splitter);
        }

        private void CreateFilterButton(QuickFilterEntry entry)
        {
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            float rowW = GalleryUiDesignTokens.QuickFiltersPanelWidthRef - 12f;
            GameObject btn = UI.CreateUIButton(
                scrollContentGO,
                rowW,
                rowH,
                entry.Name,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                null);
            // Disable row Button — nested chips bubble OnPointerClick to it and steal ⋮ / dice.
            // Apply uses UILeftClickDelegate that ignores hits under action child names.
            var rowBtn = btn != null ? btn.GetComponent<Button>() : null;
            if (rowBtn != null)
            {
                rowBtn.onClick.RemoveAllListeners();
                rowBtn.enabled = false;
                rowBtn.transition = Selectable.Transition.None;
            }
            var rowClick = btn != null ? btn.AddComponent<UILeftClickDelegate>() : null;
            if (rowClick != null)
            {
                rowClick.SkipWhenUnderChildNames = new[]
                {
                    "RandomBtn", "MoreBtn", "PinBtn", "RenameBtn", "DeleteBtn",
                    "MoreCloseBtn", "ConfirmDeleteBtn", "CancelDeleteBtn",
                    "ConfirmRenameBtn", "CancelRenameBtn", "RenameInput"
                };
                rowClick.OnLeftClick = () =>
                {
                    if (mergeMode)
                    {
                        ToggleMergeSelection(entry);
                        return;
                    }
                    if (renamingEntry != null)
                    {
                        if (ReferenceEquals(renamingEntry, entry))
                            return;
                        CancelInlineRename();
                        return;
                    }
                    if (pendingDeleteEntry != null)
                    {
                        if (ReferenceEquals(pendingDeleteEntry, entry))
                            return;
                        pendingDeleteEntry = null;
                        Refresh();
                        return;
                    }
                    if (expandedMoreEntry != null && !ReferenceEquals(expandedMoreEntry, entry))
                    {
                        expandedMoreEntry = null;
                        Refresh();
                    }

                    ApplyFilter(entry);
                    HideAfterApplyIfDocked();
                };
            }

            Image img = btn.GetComponent<Image>();
            bool selectedForMerge = mergeMode && IsMergeSelected(entry);
            bool pendingDelete = !mergeMode
                && pendingDeleteEntry != null
                && ReferenceEquals(pendingDeleteEntry, entry);
            bool renaming = !mergeMode && !pendingDelete
                && renamingEntry != null
                && ReferenceEquals(renamingEntry, entry);
            bool moreExpanded = !mergeMode && !pendingDelete && !renaming
                && expandedMoreEntry != null
                && ReferenceEquals(expandedMoreEntry, entry);
            bool flash = !pendingDelete && !renaming
                && flashEntry != null
                && ReferenceEquals(flashEntry, entry);
            bool isActive = !pendingDelete && !renaming && !mergeMode
                && panel != null
                && panel.ActiveQuickFilter != null
                && ReferenceEquals(panel.ActiveQuickFilter, entry);
            bool isKbFocus = !pendingDelete && !renaming && !mergeMode
                && keyboardFocusEntry != null
                && ReferenceEquals(keyboardFocusEntry, entry);
            bool dirtyActive = false;
            if (isActive)
            {
                try { dirtyActive = panel.IsActiveQuickFilterDirty(); } catch { }
            }
            if (img != null)
            {
                if (pendingDelete)
                    img.color = new Color(0.42f, 0.18f, 0.18f, 1f);
                else if (renaming)
                    img.color = Color.Lerp(entry.ButtonColor, new Color(0.22f, 0.32f, 0.42f, 1f), 0.5f);
                else if (moreExpanded)
                    img.color = Color.Lerp(entry.ButtonColor, new Color(0.32f, 0.34f, 0.40f, 1f), 0.45f);
                else if (selectedForMerge)
                    img.color = new Color(0.18f, 0.48f, 0.38f, 1f);
                else if (flash)
                    img.color = Color.Lerp(entry.ButtonColor, new Color(0.55f, 0.72f, 0.58f, 1f), 0.55f);
                else if (isKbFocus)
                    img.color = Color.Lerp(entry.ButtonColor, KeyboardFocusAccent, 0.55f);
                else if (isActive)
                    img.color = Color.Lerp(entry.ButtonColor, ActiveRowAccent, 0.55f);
                else
                    img.color = entry.ButtonColor;
            }

            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                if (renaming)
                {
                    txt.gameObject.SetActive(false);
                }
                else
                {
                    string label = entry.Name ?? "";
                    if (pendingDelete)
                    {
                        label = string.Format(
                            VPBTranslation.T("quickfilters.delete_inline", "Delete '{0}'?"),
                            entry.Name ?? "");
                        txt.color = Color.white;
                    }
                    else
                    {
                        if (entry.IsMerged)
                        {
                            int n = entry.MergeMembers != null ? entry.MergeMembers.Count : 0;
                            label = string.Format("{0} · {1}", label, n);
                        }
                        if (isActive)
                            label = (dirtyActive ? "●* " : "● ") + label;
                        if (selectedForMerge)
                            label = "✓ " + label;
                        txt.color = dirtyActive ? DirtyMarkColor : entry.TextColor;
                    }
                    txt.text = label;
                    txt.alignment = TextAnchor.MiddleLeft;
                    txt.gameObject.SetActive(true);
                    VPBUiFont.ApplyTo(txt);
                }
            }

            // Browse: dice+more. Expanded: 4 chips. Rename/delete: confirm pair.
            float iconReserve;
            if (mergeMode) iconReserve = 8f;
            else if (pendingDelete || renaming) iconReserve = 80f;
            else if (moreExpanded) iconReserve = 140f;
            else iconReserve = 80f;
            ApplyPopupRowTextInsets(btn.transform, RowTextPadLeftRef, RowTextPadRightRef, iconReserve);

            UI.AddLE(btn, preferredHeight: rowH, flexibleWidth: 1f);

            bool canDrag = AllowDragReorder() && !mergeMode && !pendingDelete && !moreExpanded && !renaming;
            UIListReorderable reorder = null;
            if (canDrag)
            {
                reorder = btn.AddComponent<UIListReorderable>();
                reorder.target = btn.GetComponent<RectTransform>();
                reorder.minIndex = ListChromeChildCount;
                reorder.OnReorder = SyncFiltersFromUI;
            }

            float sq = GalleryUiDesignTokens.ButtonSizeRef;
            float padR = 4f;
            float gap = 4f;
            Sprite sprRandom = UI.LoadIconSprite("dice-3", Color.white);
            Sprite sprMore = UI.LoadIconSprite("edit", Color.white)
                ?? UI.LoadIconSprite("clipboard-list", Color.white);
            Sprite sprPinOn = UI.LoadIconSprite("pin", Color.white);
            Sprite sprPinOff = UI.LoadIconSprite("pinned-off", Color.white);
            Sprite sprRename = UI.LoadIconSprite("pencil-check", Color.white);
            Sprite sprDelete = UI.LoadIconSprite("trash", Color.white);
            Sprite sprCancel = UI.LoadIconSprite("x", Color.white);
            Sprite sprConfirm = UI.LoadIconSprite("clipboard-check", Color.white)
                ?? UI.LoadIconSprite("player-play", Color.white);

            void setupSquare(GameObject go, Sprite icon, Color iconTint, float x, Color backdrop)
            {
                if (go == null) return;
                var img2 = go.GetComponent<Image>();
                if (img2 != null)
                {
                    img2.color = backdrop;
                    img2.raycastTarget = true;
                }
                var t = go.GetComponentInChildren<Text>(true);
                if (t != null) t.gameObject.SetActive(false);
                var rt2 = go.GetComponent<RectTransform>();
                if (rt2 != null)
                {
                    rt2.anchorMin = new Vector2(1, 0.5f);
                    rt2.anchorMax = new Vector2(1, 0.5f);
                    rt2.pivot = new Vector2(1, 0.5f);
                    rt2.anchoredPosition = new Vector2(x, 0f);
                    rt2.sizeDelta = new Vector2(sq, sq);
                }
                if (icon != null)
                {
                    UI.AddIconToButton(go, icon, padding: RowActionIconPadRef, backdropOverride: backdrop);
                    var iconImg = go.transform.Find("Icon") != null ? go.transform.Find("Icon").GetComponent<Image>() : null;
                    if (iconImg != null)
                    {
                        iconImg.color = iconTint;
                        iconImg.raycastTarget = false;
                    }
                }
            }

            void addReorderHandle(GameObject handle)
            {
                if (handle == null || reorder == null) return;
                var h = handle.AddComponent<UIListReorderable>();
                h.target = reorder.target;
                h.minIndex = reorder.minIndex;
                h.OnReorder = reorder.OnReorder;
            }

            void wireChipClick(GameObject go, Action action)
            {
                if (go == null || action == null) return;
                var chipBtn = go.GetComponent<Button>();
                if (chipBtn != null)
                {
                    chipBtn.onClick.RemoveAllListeners();
                    chipBtn.enabled = true;
                    chipBtn.onClick.AddListener(() => action());
                }
            }

            if (!mergeMode)
            {
            if (renaming)
            {
                float s = panel != null ? panel.ChromeScale : 1f;
                float padL = RowTextPadLeftRef * s;
                float padRs = 4f * s;
                float iconSq = GalleryUiDesignTokens.ButtonSizeRef * s;
                float rightReserve = padRs + 2f * iconSq + 4f * s;

                GameObject cancelBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject confirmBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (cancelBtn != null) cancelBtn.name = "CancelRenameBtn";
                if (confirmBtn != null) confirmBtn.name = "ConfirmRenameBtn";
                Color cancelBackdrop = GalleryUiColorTokens.SurfaceMid;
                Color confirmBackdrop = GalleryUiColorTokens.AccentConfirm;
                setupSquare(confirmBtn, sprConfirm, Color.white, -padR, confirmBackdrop);
                setupSquare(cancelBtn, sprCancel, Color.white, -(padR + sq + gap), cancelBackdrop);

                GameObject inputHost = new GameObject("RenameInput");
                inputHost.transform.SetParent(btn.transform, false);
                RectTransform hostRT = inputHost.AddComponent<RectTransform>();
                hostRT.anchorMin = Vector2.zero;
                hostRT.anchorMax = Vector2.one;
                hostRT.offsetMin = new Vector2(padL, 2f * s);
                hostRT.offsetMax = new Vector2(-rightReserve, -2f * s);

                float fieldH = Mathf.Max(18f, GalleryUiDesignTokens.PopupMenuRowHeightRef * s - 4f * s);
                int font = Mathf.Max(
                    GalleryUiDesignTokens.FontMinRef,
                    Mathf.RoundToInt(GalleryUiDesignTokens.PopupMenuRowFontRef * s));

                InputField field = UI.CreateChromeLayoutInputField(
                    inputHost.transform,
                    font,
                    fieldH,
                    1f,
                    6f * s,
                    2f * s,
                    UI.InputFieldBg,
                    UI.InputFieldPlaceholderColor,
                    VPBTranslation.T("quickfilters.rename_ph", "Preset name"),
                    "InlineRename");
                if (field != null)
                {
                    RectTransform fieldRT = field.GetComponent<RectTransform>();
                    if (fieldRT != null)
                    {
                        fieldRT.anchorMin = Vector2.zero;
                        fieldRT.anchorMax = Vector2.one;
                        fieldRT.offsetMin = Vector2.zero;
                        fieldRT.offsetMax = Vector2.zero;
                    }
                    LayoutElement fle = field.GetComponent<LayoutElement>();
                    if (fle != null) fle.ignoreLayout = true;
                    field.text = entry.Name ?? "";
                    field.onEndEdit.AddListener(val =>
                    {
                        if (ignoreRenameEndEdit)
                        {
                            ignoreRenameEndEdit = false;
                            return;
                        }
                        ConfirmInlineRename(val);
                    });
                    try
                    {
                        var esc = inputHost.AddComponent<SearchInputESCHandler>();
                        esc.Initialize(field, null, CancelInlineRename);
                    }
                    catch { }
                    try { inputHost.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(field); }
                    catch { }
                }

                wireChipClick(confirmBtn, () =>
                {
                    ignoreRenameEndEdit = true;
                    string val = field != null ? field.text : (entry.Name ?? "");
                    ConfirmInlineRename(val);
                });
                wireChipClick(cancelBtn, () =>
                {
                    ignoreRenameEndEdit = true;
                    CancelInlineRename();
                });

                var tipC = confirmBtn != null ? confirmBtn.AddComponent<UIHoverDelegate>() : null;
                if (tipC != null)
                {
                    tipC.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.rename_confirm", "Confirm new name"));
                        else panel.SetStatus(null);
                    };
                }
                var tipX = cancelBtn != null ? cancelBtn.AddComponent<UIHoverDelegate>() : null;
                if (tipX != null)
                {
                    tipX.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.rename_cancel", "Cancel rename"));
                        else panel.SetStatus(null);
                    };
                }
            }
            else if (pendingDelete)
            {
                GameObject cancelBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject confirmBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (cancelBtn != null) cancelBtn.name = "CancelDeleteBtn";
                if (confirmBtn != null) confirmBtn.name = "ConfirmDeleteBtn";
                Color cancelBackdrop = GalleryUiColorTokens.SurfaceMid;
                Color confirmBackdrop = GalleryUiColorTokens.AccentDanger;
                setupSquare(confirmBtn, sprDelete, Color.white, -padR, confirmBackdrop);
                setupSquare(cancelBtn, sprCancel, Color.white, -(padR + sq + gap), cancelBackdrop);

                wireChipClick(confirmBtn, () =>
                {
                    CommitSoftDelete(entry);
                });
                wireChipClick(cancelBtn, () =>
                {
                    pendingDeleteEntry = null;
                    Refresh();
                });

                var chConfirm = confirmBtn != null ? confirmBtn.AddComponent<UIHoverDelegate>() : null;
                if (chConfirm != null)
                {
                    chConfirm.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter)
                        {
                            panel.SetStatus(string.Format(
                                VPBTranslation.T("quickfilters.tip.delete_confirm", "Confirm delete '{0}'"),
                                entry.Name));
                        }
                        else panel.SetStatus(null);
                    };
                }
                var xh = cancelBtn != null ? cancelBtn.AddComponent<UIHoverDelegate>() : null;
                if (xh != null)
                {
                    xh.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.delete_cancel", "Cancel delete"));
                        else panel.SetStatus(null);
                    };
                }
            }
            else if (moreExpanded)
            {
                // Inline overflow: pin · rename · delete · close (no color/star).
                GameObject pinBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject renameBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject deleteBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject closeBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (pinBtn != null) pinBtn.name = "PinBtn";
                if (renameBtn != null) renameBtn.name = "RenameBtn";
                if (deleteBtn != null) deleteBtn.name = "DeleteBtn";
                if (closeBtn != null) closeBtn.name = "MoreCloseBtn";

                Color pinBackdrop = entry.Pinned
                    ? GalleryUiColorTokens.ActiveWarnHeader
                    : GalleryUiColorTokens.SurfaceMid;
                Color renameBackdrop = GalleryUiColorTokens.ActiveUtility;
                Color deleteBackdrop = GalleryUiColorTokens.AccentDanger;
                Color closeBackdrop = GalleryUiColorTokens.SurfaceMid;

                setupSquare(closeBtn, sprCancel, Color.white, -padR, closeBackdrop);
                setupSquare(deleteBtn, sprDelete, Color.white, -(padR + sq + gap), deleteBackdrop);
                setupSquare(renameBtn, sprRename, Color.white, -(padR + 2f * (sq + gap)), renameBackdrop);
                // Affordance: show action you can take — pinned → unpin (pin_off); unpinned → pin (pin_on).
                setupSquare(pinBtn, entry.Pinned ? sprPinOff : sprPinOn, Color.white, -(padR + 3f * (sq + gap)), pinBackdrop);

                wireChipClick(closeBtn, () =>
                {
                    expandedMoreEntry = null;
                    Refresh();
                });
                wireChipClick(deleteBtn, () =>
                {
                    expandedMoreEntry = null;
                    ArmInlineDelete(entry);
                });
                wireChipClick(renameBtn, () => StartInlineRename(entry));
                wireChipClick(pinBtn, () =>
                {
                    QuickFilterSettings.Instance.TogglePinned(entry);
                    expandedMoreEntry = null;
                    Refresh();
                });

                void tip(GameObject go, string idle)
                {
                    if (go == null) return;
                    var h = go.AddComponent<UIHoverDelegate>();
                    h.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(idle);
                        else panel.SetStatus(null);
                    };
                }
                tip(closeBtn, VPBTranslation.T("quickfilters.tip.more_close", "Close actions"));
                tip(deleteBtn, string.Format(VPBTranslation.T("quickfilters.tip.delete", "Delete preset '{0}'"), entry.Name));
                tip(renameBtn, string.Format(VPBTranslation.T("quickfilters.tip.rename", "Rename '{0}'"), entry.Name));
                tip(pinBtn, entry.Pinned
                    ? string.Format(VPBTranslation.T("quickfilters.tip.unpin", "Unpin '{0}' from overflow quick-random"), entry.Name)
                    : string.Format(VPBTranslation.T("quickfilters.tip.pin", "Pin '{0}' to overflow as quick-random"), entry.Name));
            }
            else
            {
                // Browse: dice + more (edit — solid hit target like dice).
                GameObject randomBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject moreBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (randomBtn != null) randomBtn.name = "RandomBtn";
                if (moreBtn != null) moreBtn.name = "MoreBtn";
                Color randomBackdrop = GalleryUiColorTokens.ActiveOn;
                Color moreBackdrop = GalleryUiColorTokens.SurfaceMid;
                setupSquare(randomBtn, sprRandom, Color.white, -padR, randomBackdrop);
                setupSquare(moreBtn, sprMore, Color.white, -(padR + sq + gap), moreBackdrop);
                addReorderHandle(randomBtn);

                wireChipClick(randomBtn, () =>
                {
                    if (panel == null) return;
                    panel.RandomizeFromFilterPreset(entry, true);
                });
                wireChipClick(moreBtn, () => ToggleRowMore(entry));

                var randH = randomBtn != null ? randomBtn.AddComponent<UIHoverDelegate>() : null;
                if (randH != null)
                {
                    randH.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter)
                        {
                            string tip = entry.IsMerged
                                ? string.Format(
                                    VPBTranslation.T("quickfilters.tip.randomize_merge", "Dice: random from each merge member in order — restores view"),
                                    entry.Name)
                                : string.Format(
                                    VPBTranslation.T("quickfilters.tip.randomize", "Dice: random item from '{0}', restores current view (D)"),
                                    entry.Name);
                            panel.SetStatus(tip);
                        }
                        else panel.SetStatus(null);
                    };
                }
                var moreH = moreBtn != null ? moreBtn.AddComponent<UIHoverDelegate>() : null;
                if (moreH != null)
                {
                    moreH.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter)
                        {
                            panel.SetStatus(string.Format(
                                VPBTranslation.T("quickfilters.tip.more", "More for '{0}': pin · rename · delete"),
                                entry.Name));
                        }
                        else panel.SetStatus(null);
                    };
                }
            }
            } // !mergeMode

            if (!mergeMode && !pendingDelete && !renaming)
            {
            var rightClick = btn.AddComponent<UIRightClickDelegate>();
            rightClick.OnRightClick = () => ToggleRowMore(entry);
            }

            var del = btn.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) => {
                if (enter && panel != null)
                {
                    string info;
                    if (mergeMode)
                    {
                        info = selectedForMerge
                            ? string.Format(VPBTranslation.T("quickfilters.merge_deselect_hint", "Deselect '{0}' from merge"), entry.Name)
                            : string.Format(VPBTranslation.T("quickfilters.merge_select_hint", "Select '{0}' for merge (2–{1})"), entry.Name, GalleryUiDesignTokens.QuickFiltersMergeMaxMembers);
                    }
                    else if (renaming)
                    {
                        info = VPBTranslation.T("quickfilters.rename_hint", "Edit name — Confirm to save · Cancel / Esc to discard");
                    }
                    else if (pendingDelete)
                    {
                        info = string.Format(
                            VPBTranslation.T("quickfilters.delete_inline_hint", "Confirm or cancel delete of '{0}' (Esc cancels)"),
                            entry.Name);
                    }
                    else if (moreExpanded)
                    {
                        info = string.Format(
                            VPBTranslation.T("quickfilters.more_expanded_hint", "Actions for '{0}' — Esc or ✕ to close"),
                            entry.Name);
                    }
                    else if (entry.IsMerged)
                    {
                        info = string.Format(VPBTranslation.T("quickfilters.merge_apply_hint", "Merged '{0}' — click = all {1} filters (OR browse). Dice = load each in order (D)."), entry.Name, entry.MergeMembers != null ? entry.MergeMembers.Count : 0);
                    }
                    else
                    {
                        info = string.Format(VPBTranslation.T("quickfilters.apply_hint", "Apply '{0}' (Enter). Dice = random item, keep view (D). More = pin / rename / delete"), entry.Name);
                    }
                    panel.SetStatus(info);
                }
                else if (panel != null) panel.SetStatus(null);
            };

            activeButtons.Add(btn);
            buttonToEntry[btn] = entry;
        }

        private void ToggleRowMore(QuickFilterEntry entry)
        {
            if (entry == null || mergeMode) return;
            renamingEntry = null;
            pendingDeleteEntry = null;
            if (expandedMoreEntry != null && ReferenceEquals(expandedMoreEntry, entry))
                expandedMoreEntry = null;
            else
                expandedMoreEntry = entry;
            Refresh();
            if (panel != null && expandedMoreEntry != null)
            {
                panel.SetStatus(string.Format(
                    VPBTranslation.T("quickfilters.tip.more_open", "Actions for '{0}' — pin · rename · delete"),
                    entry.Name));
            }
        }

        private void ArmInlineDelete(QuickFilterEntry entry)
        {
            if (entry == null || mergeMode) return;
            renamingEntry = null;
            expandedMoreEntry = null;
            pendingDeleteEntry = entry;
            Refresh();
            if (panel != null)
            {
                panel.SetStatus(string.Format(
                    VPBTranslation.T("quickfilters.tip.delete_armed", "Delete '{0}'? Confirm on row or Esc to cancel"),
                    entry.Name));
            }
        }

        /// <summary>Esc cancel for rename, armed delete, or expanded row actions.</summary>
        public bool TryCancelPendingDelete()
        {
            bool any = false;
            if (renamingEntry != null)
            {
                renamingEntry = null;
                any = true;
            }
            if (pendingDeleteEntry != null)
            {
                pendingDeleteEntry = null;
                any = true;
            }
            if (expandedMoreEntry != null)
            {
                expandedMoreEntry = null;
                any = true;
            }
            if (!any) return false;
            Refresh();
            return true;
        }

        private void StartInlineRename(QuickFilterEntry entry)
        {
            if (entry == null || mergeMode) return;
            pendingDeleteEntry = null;
            expandedMoreEntry = null;
            renamingEntry = entry;
            Refresh();
            if (panel != null)
                panel.SetStatus(VPBTranslation.T("quickfilters.tip.rename_inline", "Confirm to save · Cancel / Esc to discard"));
        }

        private void FocusRenameField(QuickFilterEntry entry)
        {
            GameObject row = FindRowForEntry(entry);
            if (row == null) return;
            Transform host = row.transform.Find("RenameInput");
            if (host == null) return;
            InputField field = host.GetComponentInChildren<InputField>(true);
            if (field == null) return;
            try
            {
                field.ActivateInputField();
                field.Select();
                field.MoveTextEnd(false);
            }
            catch { }
        }

        private void ConfirmInlineRename(string val)
        {
            if (renamingEntry == null) return;
            QuickFilterEntry entry = renamingEntry;
            renamingEntry = null;
            expandedMoreEntry = null;

            string trimmed = val != null ? val.Trim() : "";
            if (!string.IsNullOrEmpty(trimmed))
                QuickFilterSettings.Instance.RenameFilter(entry, trimmed);

            Refresh();
        }

        private void CancelInlineRename()
        {
            if (renamingEntry == null) return;
            renamingEntry = null;
            expandedMoreEntry = null;
            Refresh();
        }

        /// <summary>Match side-rail Remove Item Mode selected rim + glyph tint.</summary>
        public void SyncRemoveModeButton(bool active)
        {
            Color c = active ? RemoveModeOutlineActive : RemoveModeOutlineIdle;
            try
            {
                if (floatRemoveModeBtnIconImage != null)
                    floatRemoveModeBtnIconImage.color = c;
            }
            catch { }
            try
            {
                if (floatRemoveModeBtnGO == null) return;
                UIHoverBorder hb = floatRemoveModeBtnGO.GetComponent<UIHoverBorder>();
                if (hb == null) return;
                hb.isSelected = active;
                hb.hoverColor = c;
                hb.ApplyBorderSettings();
            }
            catch { }
        }

        /// <summary>Scale collapsed title Dice chips to match collapse/close chrome.</summary>
        private void ScaleCollapsePaletteChrome(float sortSq, float s)
        {
            if (collapsePaletteGO == null) return;
            HorizontalLayoutGroup hlg = collapsePaletteGO.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.spacing = 2f * s;
                hlg.padding = UI.Pad(0, 0, 0, 0, s);
            }
            LayoutElement hostLe = collapsePaletteGO.GetComponent<LayoutElement>();
            if (hostLe != null)
            {
                hostLe.minHeight = sortSq;
                hostLe.preferredHeight = sortSq;
            }
            for (int i = 0; i < collapsePaletteGO.transform.childCount; i++)
            {
                Transform ch = collapsePaletteGO.transform.GetChild(i);
                if (ch == null) continue;
                ScaleChromeIconBtn(ch.gameObject, sortSq, s);
            }
        }

        /// <summary>X / Esc hide: keep detach + geometry.</summary>
        private void HideFloatKeepDetach()
        {
            if (mergeMode) ExitMergeMode();
            pendingDeleteEntry = null;
            expandedMoreEntry = null;
            renamingEntry = null;
            if (detached)
            {
                if (!floatCollapsed)
                    CaptureFloatGeometryToMemory();
                else
                    RestoreExpandHeightIntoSavedSize();
            }
            SetVisible(false);
            if (panel != null) panel.SyncQuickFilterToggleState();
        }

        /// <summary>ALT+F path: ensure floating chrome + visible (keep detach on close).</summary>
        public void EnsureDetachedAndVisible()
        {
            if (!detached)
                Detach();
            SetVisible(true);
            SyncCollapsePalette();
        }

        private GameObject FindRowForEntry(QuickFilterEntry entry)
        {
            if (entry == null || buttonToEntry == null) return null;
            foreach (var kv in buttonToEntry)
            {
                if (kv.Value != null && ReferenceEquals(kv.Value, entry))
                    return kv.Key;
            }
            return null;
        }

        private void SyncFiltersFromUI()
        {
            if (!AllowDragReorder()) return;

            var newList = new List<QuickFilterEntry>();
            for (int i = ListChromeChildCount; i < scrollContentGO.transform.childCount; i++)
            {
                Transform child = scrollContentGO.transform.GetChild(i);
                if (child == null) continue;
                if (child.name == "Splitter" || child.name == "PinnedSplitter") continue;
                GameObject go = child.gameObject;
                if (buttonToEntry.TryGetValue(go, out QuickFilterEntry entry))
                    newList.Add(entry);
            }

            QuickFilterSettings.Instance.Filters = newList;
            QuickFilterSettings.Instance.Save();
            // Re-assert pin group + separator after drag.
            Refresh();
        }

        private void SyncMergeChrome()
        {
            if (titleBarLabel != null && detached)
            {
                titleBarLabel.text = mergeMode
                    ? string.Format(
                        VPBTranslation.T("quickfilters.merge_title", "Merge presets ({0}/{1})"),
                        mergeSelection.Count,
                        GalleryUiDesignTokens.QuickFiltersMergeMaxMembers)
                    : VPBTranslation.T("quickfilters.float_title", "Filter Presets");
            }
        }

        private void OnMergePrimary()
        {
            if (!mergeMode)
            {
                EnterMergeMode();
                return;
            }
            ConfirmMergeSelection();
        }

        private void EnterMergeMode()
        {
            pendingDeleteEntry = null;
            expandedMoreEntry = null;
            renamingEntry = null;
            mergeMode = true;
            mergeSelection.Clear();
            if (panel != null)
            {
                panel.SetStatus(string.Format(
                    VPBTranslation.T("quickfilters.merge_pick", "Select 2–{0} presets to merge. Click = all filters (OR); dice loads each."),
                    GalleryUiDesignTokens.QuickFiltersMergeMaxMembers));
            }
            SyncMergeChrome();
            Refresh();
        }

        private void ExitMergeMode()
        {
            if (!mergeMode) return;
            mergeMode = false;
            mergeSelection.Clear();
            SyncMergeChrome();
            Refresh();
            if (panel != null) panel.SetStatus(null);
        }

        private bool IsMergeSelected(QuickFilterEntry entry)
        {
            if (entry == null) return false;
            for (int i = 0; i < mergeSelection.Count; i++)
            {
                if (ReferenceEquals(mergeSelection[i], entry)) return true;
            }
            return false;
        }

        private void ToggleMergeSelection(QuickFilterEntry entry)
        {
            if (entry == null || !mergeMode) return;
            if (IsMergeSelected(entry))
            {
                mergeSelection.Remove(entry);
            }
            else
            {
                int max = GalleryUiDesignTokens.QuickFiltersMergeMaxMembers;
                // Count leaf capacity if selecting a merged preset.
                int addLeaves = 1;
                if (entry.IsMerged && entry.MergeMembers != null)
                    addLeaves = entry.MergeMembers.Count;

                int curLeaves = 0;
                for (int i = 0; i < mergeSelection.Count; i++)
                {
                    QuickFilterEntry e = mergeSelection[i];
                    if (e == null) continue;
                    if (e.IsMerged && e.MergeMembers != null) curLeaves += e.MergeMembers.Count;
                    else curLeaves++;
                }

                if (curLeaves + addLeaves > max)
                {
                    if (panel != null)
                    {
                        panel.ShowTemporaryStatus(
                            string.Format(
                                VPBTranslation.T("quickfilters.merge_limit", "Merge limit is {0} filters"),
                                max),
                            2f);
                    }
                    return;
                }
                mergeSelection.Add(entry);
            }
            SyncMergeChrome();
            Refresh();
        }

        private void ConfirmMergeSelection()
        {
            if (!mergeMode) return;
            if (mergeSelection.Count < 2)
            {
                if (panel != null)
                {
                    panel.ShowTemporaryStatus(
                        VPBTranslation.T("quickfilters.merge_need_two", "Select at least 2 presets to merge"),
                        2f);
                }
                return;
            }

            int max = GalleryUiDesignTokens.QuickFiltersMergeMaxMembers;
            var leaves = new List<QuickFilterEntry>();
            for (int i = 0; i < mergeSelection.Count; i++)
                QuickFilterEntry.CollectMergeLeaves(mergeSelection[i], leaves, max);

            if (leaves.Count < 2)
            {
                if (panel != null)
                {
                    panel.ShowTemporaryStatus(
                        VPBTranslation.T("quickfilters.merge_need_two", "Select at least 2 presets to merge"),
                        2f);
                }
                return;
            }

            // Name = selected preset names joined with + — no external name dialog (power path).
            string name = BuildMergeNameFromSelection(mergeSelection);
            FinishCreateMergedPreset(name, leaves);
        }

        private static string BuildMergeNameFromSelection(List<QuickFilterEntry> selected)
        {
            if (selected == null || selected.Count == 0) return "Merged";
            var parts = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                QuickFilterEntry e = selected[i];
                if (e == null) continue;
                string p = !string.IsNullOrEmpty(e.Name) ? e.Name : (e.CategoryTitle ?? "");
                if (!string.IsNullOrEmpty(p)) parts.Add(p);
            }
            if (parts.Count == 0) return "Merged";
            return string.Join(" + ", parts.ToArray());
        }

        private void FinishCreateMergedPreset(string name, List<QuickFilterEntry> leaves)
        {
            if (leaves == null || leaves.Count < 2) return;
            var entry = new QuickFilterEntry();
            entry.Name = string.IsNullOrEmpty(name) ? "Merged" : name;
            entry.MergeMembers = leaves;
            // Visual: blend toward teal so merged rows scan differently (von Restorff).
            entry.ButtonColor = new Color(0.14f, 0.28f, 0.30f, 1f);
            entry.TextColor = Color.white;
            // Seed browse fields from first leaf; ApplyQuickFilterState OR-combines all leaves.
            QuickFilterEntry first = leaves[0];
            if (first != null)
            {
                entry.CategoryPath = first.CategoryPath;
                entry.CategoryTitle = first.CategoryTitle;
            }
            QuickFilterSettings.Instance.AddFilter(entry);
            mergeMode = false;
            mergeSelection.Clear();
            SyncMergeChrome();
            Refresh();
            if (panel != null)
            {
                panel.ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("quickfilters.merge_created", "Merged preset '{0}' ({1} filters)"),
                        entry.Name,
                        leaves.Count),
                    2.5f);
            }
        }

        private void CaptureCurrentFilter()
        {
            CaptureCurrentFilter(useListSearchAsName: false);
        }

        /// <param name="useListSearchAsName">
        /// True only for Ctrl+S expert path. Button Save always suggests + inline rename (intentional naming).
        /// </param>
        private void CaptureCurrentFilter(bool useListSearchAsName)
        {
            if (panel == null) return;

            string typedName = "";
            if (useListSearchAsName)
            {
                typedName = (listFilter ?? "").Trim();
                if (typedName.Length == 0 && searchInput != null)
                    typedName = (searchInput.text ?? "").Trim();
            }

            var entry = panel.CaptureQuickFilterState(typedName.Length > 0 ? typedName : null);
            if (entry == null) return;

            if (typedName.Length > 0)
            {
                listFilter = "";
                try
                {
                    if (searchInput != null)
                        searchInput.text = "";
                }
                catch { }
            }

            QuickFilterSettings.Instance.AddFilter(entry);
            flashEntry = entry;
            keyboardFocusEntry = entry;
            Refresh();

            panel.ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("quickfilters.saved", "Saved preset '{0}'"),
                    entry.Name ?? ""),
                2.5f);

            // Typed name already applied — skip rename. Else open rename for intentional naming.
            if (typedName.Length == 0)
                StartInlineRename(entry);
        }

        private void ApplyFilter(QuickFilterEntry entry)
        {
            if (panel == null) return;
            keyboardFocusEntry = entry;
            panel.ApplyQuickFilterState(entry);
        }

        public void RefreshLocalizedUi()
        {
            if (searchInput != null && searchInput.placeholder is Text ph)
                ph.text = VPBTranslation.T("quickfilters.search_ph", "Search saved presets…");
            if (titleBarLabel != null && !mergeMode)
                titleBarLabel.text = VPBTranslation.T("quickfilters.float_title", "Filter Presets");
            if (headerFloatBtnText != null)
                headerFloatBtnText.text = VPBTranslation.T("quickfilters.detach_short", "Float");
            SyncSortButtonVisual();
            SyncMergeChrome();
            Refresh();
            if (containerGO != null)
            {
                foreach (Text t in containerGO.GetComponentsInChildren<Text>(true))
                    VPBUiFont.ApplyTo(t);
            }
        }

        /// <summary>Re-reads persisted float geometry after a layout preset rewrites the config slot.</summary>
        internal void ReloadGeometryFromConfig()
        {
            LoadGeometryFromConfig();
        }

        private void LoadGeometryFromConfig()
        {
            detached = false;
            savedFloatPosCenter = null;
            savedFloatSizeRef = null;
            try
            {
                if (VPBConfig.Instance == null) return;
                detached = VPBConfig.Instance.GalleryQuickFiltersDetached;
                if (VPBConfig.Instance.GalleryQuickFiltersPosSaved)
                {
                    savedFloatPosCenter = new Vector2(
                        VPBConfig.Instance.GalleryQuickFiltersPosX,
                        VPBConfig.Instance.GalleryQuickFiltersPosY);
                }
                if (VPBConfig.Instance.GalleryQuickFiltersSizeSaved)
                {
                    float w = VPBConfig.Instance.GalleryQuickFiltersWidthRef;
                    float h = VPBConfig.Instance.GalleryQuickFiltersHeightRef;
                    if (w >= GalleryUiDesignTokens.QuickFiltersFloatMinWidthRef
                        && h >= GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef)
                    {
                        savedFloatSizeRef = new Vector2(
                            Mathf.Clamp(w, GalleryUiDesignTokens.QuickFiltersFloatMinWidthRef, GalleryUiDesignTokens.QuickFiltersFloatMaxWidthRef),
                            Mathf.Clamp(h, GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef, GalleryUiDesignTokens.QuickFiltersFloatMaxHeightRef));
                    }
                }
            }
            catch { }
        }

        private void Detach()
        {
            if (detached) return;
            floatCollapsed = false;
            expandHeightRef = null;
            // Seed size from current dropdown height so first float isn't tiny.
            if (!savedFloatSizeRef.HasValue && containerRT != null)
            {
                float s = panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
                float w = containerRT.sizeDelta.x / s;
                float h = Mathf.Max(
                    GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef,
                    containerRT.sizeDelta.y / s);
                savedFloatSizeRef = new Vector2(
                    Mathf.Clamp(w, GalleryUiDesignTokens.QuickFiltersFloatMinWidthRef, GalleryUiDesignTokens.QuickFiltersFloatMaxWidthRef),
                    Mathf.Clamp(h, GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef, GalleryUiDesignTokens.QuickFiltersFloatMaxHeightRef));
            }
            ApplyDetachChrome(reposition: true, persist: true);
            Refresh();
            if (panel != null) panel.SyncQuickFilterToggleState();
        }

        /// <summary>Close float: reattach under title chip (X = dock + hide).</summary>
        private void CloseAndDock()
        {
            if (mergeMode) ExitMergeMode();
            pendingDeleteEntry = null;
            expandedMoreEntry = null;
            renamingEntry = null;
            if (detached)
            {
                if (!floatCollapsed)
                    CaptureFloatGeometryToMemory();
                else
                    RestoreExpandHeightIntoSavedSize();
                // Flush geometry before dock — PersistDetachedState(false) skips size/pos write.
                try { PersistGeometryFieldsOnly(); } catch { }
                floatCollapsed = false;
                expandHeightRef = null;
                collapsedTopLeftPos = null;
                ApplyDockChrome(persist: true);
                Refresh();
            }
            SetVisible(false);
            if (panel != null) panel.SyncQuickFilterToggleState();
        }

        private void ToggleFloatCollapsed()
        {
            if (!detached) return;
            if (!floatCollapsed)
            {
                CaptureFloatGeometryToMemory();
                expandHeightRef = ResolvePanelHeightRef();
                // Pin current top-left before height shrink (pivot is top-left).
                if (containerRT != null)
                    collapsedTopLeftPos = containerRT.anchoredPosition;
                floatCollapsed = true;
            }
            else
            {
                RestoreExpandHeightIntoSavedSize();
                floatCollapsed = false;
                collapsedTopLeftPos = null;
            }
            SyncCollapseButtonVisual();
            SyncCollapsePalette();
            try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
        }

        private void RestoreExpandHeightIntoSavedSize()
        {
            float h = expandHeightRef.HasValue
                ? expandHeightRef.Value
                : GalleryUiDesignTokens.QuickFiltersFloatDefaultHeightRef;
            h = Mathf.Clamp(h, GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef, GalleryUiDesignTokens.QuickFiltersFloatMaxHeightRef);
            float w = savedFloatSizeRef.HasValue
                ? savedFloatSizeRef.Value.x
                : GalleryUiDesignTokens.QuickFiltersPanelWidthRef;
            savedFloatSizeRef = new Vector2(
                Mathf.Clamp(w, GalleryUiDesignTokens.QuickFiltersFloatMinWidthRef, GalleryUiDesignTokens.QuickFiltersFloatMaxWidthRef),
                h);
        }

        private void SyncCollapseButtonVisual()
        {
            if (collapseBtnGO == null) return;
            string path = floatCollapsed ? "chevron-down" : "chevron-up";
            Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
            if (spr == null) return;
            if (collapseBtnIcon != null)
            {
                UI.SetIconSprite(collapseBtnIcon, spr);
            }
            else
            {
                StyleChromeIconBtn(collapseBtnGO, GalleryUiDesignTokens.ButtonSizeRef, path, GalleryUiColorTokens.ChromeIconWell);
                Transform iconTr = collapseBtnGO.transform.Find("Icon");
                collapseBtnIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }
        }

        private void ApplyDetachChrome(bool reposition, bool persist)
        {
            detached = true;
            GameObject floatHost = ResolveFloatHost();
            Vector3 keepWorld = containerRT != null ? containerRT.position : Vector3.zero;
            bool hadWorld = containerRT != null && containerGO != null
                && containerGO.transform.parent != null
                && (floatHost == null || containerGO.transform.parent != floatHost.transform);

            if (containerGO != null && floatHost != null && containerGO.transform.parent != floatHost.transform)
            {
                containerGO.transform.SetParent(floatHost.transform, false);
                SetLayerRecursive(containerGO, floatHost.layer);
            }
            if (backdropGO != null) backdropGO.SetActive(false);
            if (titleBarGO != null) titleBarGO.SetActive(true);
            if (footerGO != null) footerGO.SetActive(!floatCollapsed);
            if (headerFloatBtnGO != null) headerFloatBtnGO.SetActive(false);

            if (containerRT != null)
            {
                containerRT.anchorMin = new Vector2(0.5f, 0.5f);
                containerRT.anchorMax = new Vector2(0.5f, 0.5f);
                containerRT.pivot = new Vector2(0f, 1f);

                if (reposition)
                {
                    if (savedFloatPosCenter.HasValue)
                    {
                        ApplyFloatAnchorsAndPos(panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f);
                    }
                    else if (hadWorld && floatHost != null)
                    {
                        // Seed float size first, then keep title corner in world space (recognition > jump).
                        float ss = panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
                        float wRef = ResolvePanelWidthRef();
                        float hRef = floatCollapsed
                            ? GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef
                            : ResolvePanelHeightRef();
                        containerRT.sizeDelta = new Vector2(wRef * ss, hRef * ss);
                        containerRT.position = keepWorld;
                        savedFloatPosCenter = TopLeftToCenter(containerRT.anchoredPosition, containerRT.sizeDelta);
                    }
                    else
                    {
                        savedFloatPosCenter = Vector2.zero;
                        ApplyFloatAnchorsAndPos(panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f);
                    }
                }
            }

            if (containerGO != null)
                containerGO.transform.SetAsLastSibling();

            if (persist) PersistDetachedState(true);
        }

        private void ApplyDockChrome(bool persist)
        {
            detached = false;
            floatCollapsed = false;
            if (containerGO != null && dockParentGO != null && containerGO.transform.parent != dockParentGO.transform)
            {
                containerGO.transform.SetParent(dockParentGO.transform, false);
                SetLayerRecursive(containerGO, dockParentGO.layer);
            }
            if (titleBarGO != null) titleBarGO.SetActive(false);
            if (footerGO != null) footerGO.SetActive(false);
            if (scrollHostGO != null) scrollHostGO.SetActive(true);
            if (viewportGO != null) viewportGO.SetActive(true);
            if (scrollbarGO != null) scrollbarGO.SetActive(true);
            if (headerGO != null) headerGO.SetActive(true);
            if (headerFloatBtnGO != null) headerFloatBtnGO.SetActive(true);

            if (persist) PersistDetachedState(false);
        }

        private GameObject ResolveFloatHost()
        {
            if (panel != null && panel.canvas != null)
                return panel.canvas.gameObject;
            return dockParentGO;
        }

        private void OnFloatMoved()
        {
            if (floatCollapsed && containerRT != null)
            {
                collapsedTopLeftPos = containerRT.anchoredPosition;
                // Center as if expanded height so expand keeps same top edge.
                float s = panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
                float w = ResolvePanelWidthRef() * s;
                float h = ResolvePanelHeightRef() * s;
                savedFloatPosCenter = TopLeftToCenter(collapsedTopLeftPos.Value, new Vector2(w, h));
                PersistGeometry();
                return;
            }
            CaptureFloatGeometryToMemory();
            PersistGeometry();
        }

        private void OnFloatResizing()
        {
            // Capture live size only — full ApplyLayout rewrites sizeDelta via ref÷scale×scale
            // every tick (drift) and rebuilds chrome (jitter / grip clip). End-drag applies layout.
            // Same contract as ImportSidebar OnImportSidebarFloatResizing.
            CaptureFloatGeometryToMemory();
            try { SyncFloatScrollHostInsetsDuringResize(); } catch { }
        }

        private void OnFloatResized()
        {
            CaptureFloatGeometryToMemory();
            PersistGeometry();
            try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
        }

        /// <summary>Live resize: keep scroll band under title/header and above footer without full ApplyLayout.</summary>
        private void SyncFloatScrollHostInsetsDuringResize()
        {
            if (!detached || floatCollapsed || scrollHostGO == null) return;
            float s = panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float headerH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            RectTransform shRT = scrollHostGO.GetComponent<RectTransform>();
            if (shRT == null) return;
            shRT.offsetMin = new Vector2(0f, footerH);
            shRT.offsetMax = new Vector2(0f, -(titleH + headerH));
        }

        private void CaptureFloatGeometryToMemory()
        {
            if (containerRT == null || floatCollapsed) return;
            float s = panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
            savedFloatPosCenter = TopLeftToCenter(containerRT.anchoredPosition, containerRT.sizeDelta);
            savedFloatSizeRef = new Vector2(
                Mathf.Clamp(containerRT.sizeDelta.x / s, GalleryUiDesignTokens.QuickFiltersFloatMinWidthRef, GalleryUiDesignTokens.QuickFiltersFloatMaxWidthRef),
                Mathf.Clamp(containerRT.sizeDelta.y / s, GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef, GalleryUiDesignTokens.QuickFiltersFloatMaxHeightRef));
        }

        private void PersistDetachedState(bool isDetached)
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.GalleryQuickFiltersDetached = isDetached;
                if (isDetached)
                    PersistGeometryFieldsOnly();
            }
            catch { return; }
            if (panel != null) panel.ScheduleQuickFiltersConfigSave();
        }

        private void PersistGeometry()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                PersistGeometryFieldsOnly();
            }
            catch { return; }
            if (panel != null) panel.ScheduleQuickFiltersConfigSave();
        }

        private void PersistGeometryFieldsOnly()
        {
            if (VPBConfig.Instance == null) return;
            if (savedFloatPosCenter.HasValue)
            {
                VPBConfig.Instance.GalleryQuickFiltersPosSaved = true;
                VPBConfig.Instance.GalleryQuickFiltersPosX = savedFloatPosCenter.Value.x;
                VPBConfig.Instance.GalleryQuickFiltersPosY = savedFloatPosCenter.Value.y;
            }
            if (savedFloatSizeRef.HasValue)
            {
                VPBConfig.Instance.GalleryQuickFiltersSizeSaved = true;
                VPBConfig.Instance.GalleryQuickFiltersWidthRef = savedFloatSizeRef.Value.x;
                VPBConfig.Instance.GalleryQuickFiltersHeightRef = savedFloatSizeRef.Value.y;
            }
        }

        private Vector2 GetFloatMinSizeScaled()
        {
            float s = panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
            float minH = floatCollapsed
                ? GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef
                : GalleryUiDesignTokens.QuickFiltersFloatMinHeightRef;
            return new Vector2(
                GalleryUiDesignTokens.QuickFiltersFloatMinWidthRef * s,
                minH * s);
        }

        private Vector2 GetFloatMaxSizeScaled()
        {
            float s = panel != null && panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
            return new Vector2(
                GalleryUiDesignTokens.QuickFiltersFloatMaxWidthRef * s,
                GalleryUiDesignTokens.QuickFiltersFloatMaxHeightRef * s);
        }
    }
}
