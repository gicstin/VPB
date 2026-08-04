using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Filter-presets list: dropdown under title chip, or detachable floating window
    /// (drag / resize / collapse / close-docks). Pos+size+detached flag live in <see cref="VPBConfig"/>.
    /// </summary>
    public class QuickFiltersUI
    {
        private const float PanelMaxHeightRef = 500f;
        private const float ScrollBarWidthRef = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef;
        private const float RowTextPadLeftRef = 12f;
        private const float RowTextPadRightRef = 8f;
        private const float SplitterHeightRef = 8f;
        private const float HeaderPadRef = 6f;
        private const float HeaderSortGapRef = 6f;
        /// <summary>Icon pad on ButtonSizeRef squares — matches side-rail / sort chips (glyph fills hit).</summary>
        private const float RowActionIconPadRef = 5f;
        private const float ChromeIconPadRef = 5f;

        private static readonly Color FloatTitleBarBg = new Color(0.10f, 0.22f, 0.34f, 1f);
        private static readonly Color FloatFooterBarBg = new Color(0.08f, 0.10f, 0.13f, 1f);

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
        private GameObject detachListBtnGO;
        private Text detachListBtnText;
        private GameObject collapseBtnGO;
        private Image collapseBtnIcon;
        private GameObject closeBtnGO;
        private GameObject floatUndoBtnGO;
        private GameObject floatRedoBtnGO;
        private GameObject floatRemoveModeBtnGO;
        private Image floatRemoveModeBtnIconImage;
        private GameObject resizeHandleGO;
        private List<GameObject> activeButtons = new List<GameObject>();
        private Dictionary<GameObject, QuickFilterEntry> buttonToEntry = new Dictionary<GameObject, QuickFilterEntry>();
        private bool editMode;
        private bool mergeMode;
        private QuickFilterEntry renamingEntry;
        /// <summary>Edit-mode delete armed on this row — inline Confirm/Cancel (no external dialog).</summary>
        private QuickFilterEntry pendingDeleteEntry;
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
            // Backdrop only for docked dropdown (click-away). Floating stays open until X (reattach) / bar toggle.
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
                try { if (searchInput != null) searchInput.Select(); } catch { }
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
            UI.AddImage(containerGO, new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 1f));
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
                titleBarGO, spacing: 4f, padding: UI.Pad(6, 6, 4, 4),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            Text grip = UI.CreateLabel(titleBarGO, "\u2807", GalleryUiDesignTokens.PopupMenuRowFontRef,
                new Color(0.65f, 0.72f, 0.80f, 1f), TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            UI.AddLE(grip.gameObject, minWidth: 18f, preferredWidth: 18f);

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
            StyleChromeIconBtn(collapseBtnGO, chromeSz, "vpb_icons/chevron_up.png");
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

            closeBtnGO = UI.CreateUIButton(titleBarGO, chromeSz, chromeSz, " ", 16, 0, 0, AnchorPresets.middleCenter, CloseAndDock);
            closeBtnGO.name = "TitleClose";
            StyleChromeIconBtn(closeBtnGO, chromeSz, "vpb_icons/x.png");
            var closeHover = closeBtnGO.AddComponent<UIHoverDelegate>();
            closeHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.close", "Close and reattach under title bar"));
                else panel.SetStatus(null);
            };

            var headerDrag = titleBarGO.AddComponent<QuickFiltersPanelDrag>();
            headerDrag.Target = containerRT;
            headerDrag.OnMoved = OnFloatMoved;

            // Fixed header: search + sort (float detach is a full-width list button).
            headerGO = UI.CreateChildRT(containerGO, "Header", AnchorPresets.hStretchTop,
                new Vector2(0f, GalleryUiDesignTokens.SearchFieldHeightRef + HeaderPadRef * 2f),
                new Vector2(0f, 0f));
            RectTransform headerRT = headerGO.GetComponent<RectTransform>();
            headerRT.pivot = new Vector2(0.5f, 1f);

            float sortSq = GalleryUiDesignTokens.ButtonSizeRef;
            sortBtnGO = UI.CreateUIButton(headerGO, sortSq, sortSq, " ", 16, 0, 0, AnchorPresets.topRight, null);
            if (sortBtnGO != null)
            {
                sortBtnGO.name = "SortBtn";
                RectTransform srtBtn = sortBtnGO.GetComponent<RectTransform>();
                if (srtBtn != null)
                {
                    srtBtn.anchorMin = new Vector2(1f, 1f);
                    srtBtn.anchorMax = new Vector2(1f, 1f);
                    srtBtn.pivot = new Vector2(1f, 1f);
                    srtBtn.anchoredPosition = new Vector2(-HeaderPadRef, -HeaderPadRef);
                    srtBtn.sizeDelta = new Vector2(sortSq, sortSq);
                }
                Color sortBackdrop = new Color(0.22f, 0.42f, 0.58f, 1f);
                Sprite sortSpr = UI.LoadIconSprite("vpb_icons/sort_name_asc.png", UI.BarIconGlyphTint);
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

            if (panel != null)
            {
                searchInput = panel.CreateSearchInput(headerGO, 200f, val =>
                {
                    listFilter = val ?? "";
                    Refresh();
                }, () =>
                {
                    listFilter = "";
                    Refresh();
                });
                if (searchInput != null)
                {
                    if (searchInput.placeholder is Text ph)
                        ph.text = VPBTranslation.T("quickfilters.search_ph", "Filter presets…");
                    RectTransform srt = searchInput.GetComponent<RectTransform>();
                    if (srt != null)
                    {
                        srt.anchorMin = new Vector2(0f, 1f);
                        srt.anchorMax = new Vector2(0f, 1f);
                        srt.pivot = new Vector2(0f, 1f);
                        srt.anchoredPosition = new Vector2(HeaderPadRef, -HeaderPadRef);
                        srt.sizeDelta = new Vector2(200f, GalleryUiDesignTokens.SearchFieldHeightRef);
                    }
                }
            }

            // Scroll host sits between header/footer — viewport + scrollbar stay inside this rect.
            scrollHostGO = UI.CreateChildRT(containerGO, "ScrollHost", AnchorPresets.stretchAll);
            if (scrollHostGO.GetComponent<RectMask2D>() == null)
                scrollHostGO.AddComponent<RectMask2D>();

            scrollRect = scrollHostGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25f;
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

            // Float footer: undo/redo + Remove Item Mode (left) · spacer · resize (right).
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef;
            footerGO = UI.CreateChildRT(containerGO, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            Image footerBg = UI.AddImage(footerGO, FloatFooterBarBg);
            if (footerBg != null) footerBg.raycastTarget = true;
            RectTransform footerRT = footerGO.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                footerGO, spacing: 4f, padding: UI.Pad(6, 6, 4, 4),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (footerGO.GetComponent<RectMask2D>() == null)
                footerGO.AddComponent<RectMask2D>();

            floatUndoBtnGO = UI.CreateUIButton(footerGO, chromeSz, chromeSz, " ", 14, 0, 0, AnchorPresets.middleCenter, () =>
            {
                if (panel != null) panel.QuickMenu_Undo();
            });
            floatUndoBtnGO.name = "FloatUndo";
            StyleChromeIconBtn(floatUndoBtnGO, chromeSz, "vpb_icons/undo.png", new Color(0f, 0f, 0f, 0.5f));
            var undoHover = floatUndoBtnGO.AddComponent<UIHoverDelegate>();
            undoHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("gallery.tooltip.undo", "Undo last change (Ctrl+Z)"));
                else panel.SetStatus(null);
            };

            floatRedoBtnGO = UI.CreateUIButton(footerGO, chromeSz, chromeSz, " ", 14, 0, 0, AnchorPresets.middleCenter, () =>
            {
                if (panel != null) panel.QuickMenu_Redo();
            });
            floatRedoBtnGO.name = "FloatRedo";
            StyleChromeIconBtn(floatRedoBtnGO, chromeSz, "vpb_icons/redo.png", new Color(0f, 0f, 0f, 0.5f));
            var redoHover = floatRedoBtnGO.AddComponent<UIHoverDelegate>();
            redoHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("gallery.tooltip.redo", "Redo (Ctrl+Y / Ctrl+Shift+Z)"));
                else panel.SetStatus(null);
            };

            // Same Remove Item Mode toggle as side rails (point → fade → click remove).
            floatRemoveModeBtnGO = UI.CreateUIButton(footerGO, chromeSz, chromeSz, " ", 14, 0, 0, AnchorPresets.middleCenter, () =>
            {
                if (panel != null) panel.ToggleRemoveMode(false, false);
            });
            floatRemoveModeBtnGO.name = "FloatRemoveMode";
            StyleChromeIconBtn(floatRemoveModeBtnGO, chromeSz, "vpb_icons/delete.png", RemoveModeRailBackdrop);
            Transform rmIconTr = floatRemoveModeBtnGO.transform.Find("Icon");
            floatRemoveModeBtnIconImage = rmIconTr != null ? rmIconTr.GetComponent<Image>() : null;
            if (floatRemoveModeBtnGO.GetComponent<UIHoverBorder>() == null)
                floatRemoveModeBtnGO.AddComponent<UIHoverBorder>();
            var rmHover = floatRemoveModeBtnGO.AddComponent<UIHoverDelegate>();
            rmHover.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter)
                {
                    panel.SetStatus(VPBTranslation.T(
                        "gallery.tooltip.remove_mode",
                        "Remove Item Mode: point at an item to fade it, click to remove. Also opens the remove list siderail for clothing/hair/scene."));
                }
                else panel.SetStatus(null);
            };
            try { SyncRemoveModeButton(panel != null && panel.IsRemoveModeActive); } catch { }

            // Flexible spacer so resize sits visually on the right.
            GameObject footerSpacer = new GameObject("Spacer");
            footerSpacer.transform.SetParent(footerGO.transform, false);
            footerSpacer.AddComponent<RectTransform>();
            UI.AddLE(footerSpacer, flexibleWidth: 1f, minWidth: 8f);

            float rh = GalleryUiDesignTokens.ButtonSizeRef;
            resizeHandleGO = UI.AddChildGOImage(
                footerGO, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                rh, rh, Vector2.zero, rounded: true);
            resizeHandleGO.name = "ResizeHandle";
            Image rhImg = resizeHandleGO.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            StyleChromeIconBtn(resizeHandleGO, rh, "vpb_icons/chevrons_down_right.png", UI.IconButtonBackdrop);
            var resizer = resizeHandleGO.AddComponent<QuickFiltersPanelResize>();
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
        }

        private static void StyleChromeIconBtn(GameObject go, float size, string iconPath, Color? backdropOverride = null)
        {
            if (go == null) return;
            Image img = go.GetComponent<Image>();
            if (img != null)
                img.color = backdropOverride.HasValue ? backdropOverride.Value : new Color(0f, 0f, 0f, 0.01f);
            Button btn = go.GetComponent<Button>();
            if (btn != null) btn.transition = Selectable.Transition.None;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            Text label = go.GetComponentInChildren<Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            float pad = ChromeIconPadRef;
            try
            {
                Sprite spr = UI.LoadIconSprite(iconPath, UI.BarIconGlyphTint);
                if (spr != null)
                {
                    Transform existing = go.transform.Find("Icon");
                    if (existing != null)
                    {
                        Image iconImg = existing.GetComponent<Image>();
                        if (iconImg != null)
                        {
                            iconImg.sprite = spr;
                            iconImg.color = Color.white;
                            RectTransform irt = existing as RectTransform;
                            if (irt != null)
                                irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
                        }
                    }
                    else
                        UI.AddIconToButton(go, spr, pad, backdropOverride ?? new Color(0f, 0f, 0f, 0f));
                }
            }
            catch { }
        }

        private static void ScaleChromeIconBtn(GameObject go, float size, float s)
        {
            if (go == null || size <= 0f) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(size, size);
            Transform iconTr = go.transform.Find("Icon");
            if (iconTr != null)
            {
                RectTransform irt = iconTr as RectTransform;
                if (irt != null)
                {
                    float pad = ChromeIconPadRef * s;
                    irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
                }
            }
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
            string path = "vpb_icons/sort_name_asc.png";
            switch (listSortMode)
            {
                case ListSortMode.NameDesc:
                    path = "vpb_icons/sort_name_desc.png";
                    break;
                case ListSortMode.PinnedFirst:
                    path = "vpb_icons/pin_on.png";
                    break;
                case ListSortMode.Manual:
                    path = "vpb_icons/sort_number_asc.png";
                    break;
            }
            Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
            if (spr == null) return;
            if (sortBtnIcon != null)
            {
                sortBtnIcon.sprite = spr;
                sortBtnIcon.color = Color.white;
            }
            else
            {
                UI.AddIconToButton(sortBtnGO, spr, 4f, new Color(0.22f, 0.42f, 0.58f, 1f));
                Transform iconTr = sortBtnGO.transform.Find("Icon");
                sortBtnIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }
        }

        private float HeaderHeightRef()
        {
            return GalleryUiDesignTokens.SearchFieldHeightRef + HeaderPadRef * 2f;
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
            float headerPad = HeaderPadRef * s;
            float sortSq = GalleryUiDesignTokens.ButtonSizeRef * s;
            float chromeReserve = sortSq + HeaderSortGapRef * s;
            bool showBody = !detached || !floatCollapsed;

            if (titleBarGO != null)
            {
                titleBarGO.SetActive(detached);
                RectTransform trt = titleBarGO.GetComponent<RectTransform>();
                if (trt != null)
                    trt.sizeDelta = new Vector2(0f, titleH);
                if (titleBarLabel != null)
                    GalleryUiMetrics.ApplyFont(titleBarLabel, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                HorizontalLayoutGroup titleHlg = titleBarGO.GetComponent<HorizontalLayoutGroup>();
                if (titleHlg != null)
                {
                    titleHlg.spacing = 4f * s;
                    titleHlg.padding = UI.Pad(6, 6, 4, 4, s);
                }
                ScaleChromeIconBtn(collapseBtnGO, sortSq, s);
                ScaleChromeIconBtn(closeBtnGO, sortSq, s);
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
            }

            if (sortBtnGO != null)
            {
                RectTransform srtBtn = sortBtnGO.GetComponent<RectTransform>();
                if (srtBtn != null)
                {
                    srtBtn.anchoredPosition = new Vector2(-headerPad, -headerPad);
                    srtBtn.sizeDelta = new Vector2(sortSq, sortSq);
                }
            }

            if (searchInput != null)
            {
                RectTransform srt = searchInput.GetComponent<RectTransform>();
                if (srt != null)
                {
                    float searchW = Mathf.Max(80f * s, panelW - headerPad * 2f - chromeReserve - sbW);
                    srt.anchoredPosition = new Vector2(headerPad, -headerPad);
                    srt.sizeDelta = new Vector2(searchW, GalleryUiDesignTokens.SearchFieldHeightRef * s);
                }
                GalleryPanel.RescaleSearchInput(searchInput, s);
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
            float iconSq = rowH;
            float iconPadR = 4f * s;
            float iconGap = 4f * s;
            float actionGap = 6f * s;
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

                    if (ch.name == "DetachListBtn")
                    {
                        Text dt = ch.Find("Text") != null ? ch.Find("Text").GetComponent<Text>() : null;
                        if (dt == null) dt = ch.GetComponentInChildren<Text>();
                        if (dt != null)
                            GalleryUiMetrics.ApplyFont(dt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                        ApplyPopupRowTextInsets(ch, textPadL, textPadR);
                        continue;
                    }

                    int actionCount = 0;
                    if (ch.Find("RandomBtn") != null) actionCount++;
                    if (ch.Find("PinBtn") != null) actionCount++;
                    if (ch.Find("RenameBtn") != null) actionCount++;
                    if (ch.Find("DeleteBtn") != null) actionCount++;
                    if (ch.Find("ConfirmDeleteBtn") != null) actionCount++;
                    if (ch.Find("CancelDeleteBtn") != null) actionCount++;
                    float editReserve = actionCount > 0
                        ? (iconPadR + actionCount * iconSq + Mathf.Max(0, actionCount - 1) * iconGap)
                        : 0f;
                    ApplyPopupRowTextInsets(ch, textPadL, textPadR, editReserve);

                    Text t = ch.Find("Text") != null ? ch.Find("Text").GetComponent<Text>() : null;
                    if (t != null)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);

                    // Right-edge stack. Browse: dice only. Edit: pin / rename / delete (or inline delete confirm).
                    if (mergeMode)
                    {
                        // Selection mode — no row action chips.
                    }
                    else if (editMode)
                    {
                        if (ch.Find("ConfirmDeleteBtn") != null)
                        {
                            PlaceRowAction(ch, "ConfirmDeleteBtn", iconSq, iconPadR, iconGap, 0);
                            PlaceRowAction(ch, "CancelDeleteBtn", iconSq, iconPadR, iconGap, 1);
                        }
                        else
                        {
                            PlaceRowAction(ch, "DeleteBtn", iconSq, iconPadR, iconGap, 0);
                            PlaceRowAction(ch, "RenameBtn", iconSq, iconPadR, iconGap, 1);
                            PlaceRowAction(ch, "PinBtn", iconSq, iconPadR, iconGap, 2);
                        }
                    }
                    else
                    {
                        PlaceRowAction(ch, "RandomBtn", iconSq, iconPadR, iconGap, 0);
                    }

                    float iconPad = RowActionIconPadRef * s;
                    ScaleRowActionIcon(ch, "RandomBtn", iconPad);
                    ScaleRowActionIcon(ch, "PinBtn", iconPad);
                    ScaleRowActionIcon(ch, "RenameBtn", iconPad);
                    ScaleRowActionIcon(ch, "DeleteBtn", iconPad);
                    ScaleRowActionIcon(ch, "ConfirmDeleteBtn", iconPad);
                    ScaleRowActionIcon(ch, "CancelDeleteBtn", iconPad);
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
                    footerHlg.padding = UI.Pad(6, 6, 4, 4, s);
                }
                ScaleChromeIconBtn(floatUndoBtnGO, sortSq, s);
                ScaleChromeIconBtn(floatRedoBtnGO, sortSq, s);
                ScaleChromeIconBtn(floatRemoveModeBtnGO, sortSq, s);
                ScaleChromeIconBtn(resizeHandleGO, sortSq, s);
                SyncMergeChrome();
                try { SyncRemoveModeButton(panel != null && panel.IsRemoveModeActive); } catch { }
            }

            if (containerRT != null && panel != null)
            {
                if (detached)
                {
                    // Collapsed: keep top edge fixed (pivot top-left) — body collapses up.
                    if (floatCollapsed && collapsedTopLeftPos.HasValue)
                        containerRT.anchoredPosition = collapsedTopLeftPos.Value;
                    else
                        ApplyFloatAnchorsAndPos(s);
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

        private void ApplyFloatAnchorsAndPos(float s)
        {
            if (containerRT == null) return;
            containerRT.anchorMin = new Vector2(0.5f, 0.5f);
            containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            containerRT.pivot = new Vector2(0f, 1f);
            Vector2 size = containerRT.sizeDelta;
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
            renamingEntry = null;
            // pendingDeleteEntry kept — row rebuilds into inline Confirm/Cancel.
            foreach (var btn in activeButtons) GameObject.Destroy(btn);
            activeButtons.Clear();
            buttonToEntry.Clear();

            CreateActionsRow();
            CreateDetachListButton();
            CreateSplitter("Splitter");

            List<QuickFilterEntry> display = BuildDisplayList();
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

        /// <summary>Leading non-filter rows in scroll content (ActionsRow, optional Detach, Splitter).</summary>
        private int ListChromeChildCount => detached ? 2 : 3;

        private void CreateActionsRow()
        {
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            float rowW = GalleryUiDesignTokens.QuickFiltersPanelWidthRef - 12f;

            GameObject row = new GameObject("ActionsRow");
            row.transform.SetParent(scrollContentGO.transform, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(rowW, rowH);
            UI.AddHLG(
                row,
                spacing: 6f,
                padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: true,
                childForceExpandHeight: true);
            UI.AddLE(row, preferredHeight: rowH, flexibleWidth: 1f);

            GameObject saveBtn = UI.CreateUIButton(
                row,
                rowW * 0.33f,
                rowH,
                VPBTranslation.T("quickfilters.save_preset", "Save Preset"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                () => { CaptureCurrentFilter(); });
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
                    if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.save", "Capture current filters into new preset (dice randomizes from it later)."));
                    else panel.SetStatus(null);
                };
            }

            string editLabel;
            if (mergeMode)
                editLabel = VPBTranslation.T("quickfilters.merge_cancel", "Cancel");
            else if (editMode)
                editLabel = VPBTranslation.T("quickfilters.done_editing", "Done Editing");
            else
                editLabel = VPBTranslation.T("quickfilters.edit_presets", "Edit Presets");
            GameObject editBtn = UI.CreateUIButton(
                row,
                rowW * 0.33f,
                rowH,
                editLabel,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                () =>
                {
                    if (mergeMode)
                    {
                        ExitMergeMode();
                        return;
                    }
                    editMode = !editMode;
                    if (!editMode) pendingDeleteEntry = null;
                    Refresh();
                });
            if (editBtn != null)
            {
                editBtn.name = "EditBtn";
                Image img = editBtn.GetComponent<Image>();
                // Idle = same readable weight as Save (not muted/disabled look). Active = warm mode cue.
                if (img != null)
                {
                    if (mergeMode)
                        img.color = UI.PopupRowActiveBackdrop;
                    else if (editMode)
                        img.color = new Color(0.40f, 0.34f, 0.16f, 1f);
                    else
                        img.color = new Color(0.28f, 0.26f, 0.18f, 1f);
                }
                Text txt = editBtn.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.color = UI.PopupText;
                    VPBUiFont.ApplyTo(txt);
                }
                ApplyPopupRowTextInsets(editBtn.transform, RowTextPadLeftRef, RowTextPadRightRef);
                UI.AddLE(editBtn, preferredHeight: rowH, flexibleWidth: 1f, minWidth: 60f);
            }

            // Actions row: Save | Edit/Cancel | Merge — single chrome (float footer has no duplicate).
            string mergeLabel = mergeMode
                ? string.Format(
                    VPBTranslation.T("quickfilters.merge_create_n", "Create ({0}/{1})"),
                    mergeSelection.Count,
                    GalleryUiDesignTokens.QuickFiltersMergeMaxMembers)
                : VPBTranslation.T("quickfilters.merge_to_new", "Merge to New");
            GameObject mergeBtn = UI.CreateUIButton(
                row,
                rowW * 0.33f,
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

        private void CreateDetachListButton()
        {
            detachListBtnGO = null;
            detachListBtnText = null;
            if (detached) return;

            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            float rowW = GalleryUiDesignTokens.QuickFiltersPanelWidthRef - 12f;
            GameObject btn = UI.CreateUIButton(
                scrollContentGO,
                rowW,
                rowH,
                VPBTranslation.T("quickfilters.detach_list", "Float as Window"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                Detach);
            if (btn == null) return;

            btn.name = "DetachListBtn";
            detachListBtnGO = btn;
            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = new Color(0.18f, 0.30f, 0.40f, 1f);
            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                detachListBtnText = txt;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = UI.PopupText;
                VPBUiFont.ApplyTo(txt);
            }
            ApplyPopupRowTextInsets(btn.transform, RowTextPadLeftRef, RowTextPadRightRef);
            UI.AddLE(btn, preferredHeight: rowH, flexibleWidth: 1f);

            var del = btn.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.detach", "Detach as floating window (move / resize)"));
                else panel.SetStatus(null);
            };

            activeButtons.Add(btn);
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
            var b = btn != null ? btn.GetComponent<Button>() : null;
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() =>
                {
                    if (mergeMode)
                    {
                        ToggleMergeSelection(entry);
                        return;
                    }
                    if (editMode)
                    {
                        if (pendingDeleteEntry != null && ReferenceEquals(pendingDeleteEntry, entry))
                            return;
                        if (pendingDeleteEntry != null)
                        {
                            pendingDeleteEntry = null;
                            Refresh();
                            return;
                        }
                        ShowContextMenu(btn, entry);
                        return;
                    }

                    ApplyFilter(entry);
                    HideAfterApplyIfDocked();
                });
            }

            Image img = btn.GetComponent<Image>();
            bool selectedForMerge = mergeMode && IsMergeSelected(entry);
            bool pendingDelete = editMode && !mergeMode
                && pendingDeleteEntry != null
                && ReferenceEquals(pendingDeleteEntry, entry);
            if (img != null)
            {
                if (pendingDelete)
                    img.color = new Color(0.42f, 0.18f, 0.18f, 1f);
                else if (selectedForMerge)
                    img.color = new Color(0.18f, 0.48f, 0.38f, 1f);
                else
                    img.color = entry.ButtonColor;
            }

            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
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
                    if (selectedForMerge)
                        label = "✓ " + label;
                    txt.color = entry.TextColor;
                }
                txt.text = label;
                txt.alignment = TextAnchor.MiddleLeft;
                VPBUiFont.ApplyTo(txt);
            }

            // Browse: dice only. Edit: pin + rename + delete (or Confirm/Cancel). Hide in merge select.
            float iconReserve;
            if (mergeMode) iconReserve = 8f;
            else if (pendingDelete) iconReserve = 80f;
            else if (editMode) iconReserve = 120f;
            else iconReserve = 40f;
            ApplyPopupRowTextInsets(btn.transform, RowTextPadLeftRef, RowTextPadRightRef, iconReserve);

            UI.AddLE(btn, preferredHeight: rowH, flexibleWidth: 1f);

            bool canDrag = AllowDragReorder() && !mergeMode && !pendingDelete;
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
            Sprite sprRandom = UI.LoadIconSprite("vpb_icons/random.png", Color.white);
            Sprite sprPinOn = UI.LoadIconSprite("vpb_icons/pin_on.png", Color.white);
            Sprite sprPinOff = UI.LoadIconSprite("vpb_icons/pin_off.png", Color.white);
            Sprite sprRename = UI.LoadIconSprite("vpb_icons/rename.png", Color.white);
            Sprite sprDelete = UI.LoadIconSprite("vpb_icons/delete.png", Color.white);
            Sprite sprCancel = UI.LoadIconSprite("vpb_icons/x.png", Color.white);

            void setupSquare(GameObject go, Sprite icon, Color iconTint, float x, Color backdrop)
            {
                if (go == null) return;
                var img2 = go.GetComponent<Image>();
                if (img2 != null) img2.color = backdrop;
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
                    if (iconImg != null) iconImg.color = iconTint;
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

            if (!mergeMode)
            {
            if (!editMode)
            {
                // Dice — full row-height square like side-rail / title chips (browse only).
                GameObject randomBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (randomBtn != null) randomBtn.name = "RandomBtn";
                Color randomBackdrop = new Color(0.18f, 0.32f, 0.28f, 1f);
                setupSquare(randomBtn, sprRandom, Color.white, -padR, randomBackdrop);
                addReorderHandle(randomBtn);
                var randB = randomBtn != null ? randomBtn.GetComponent<Button>() : null;
                if (randB != null)
                {
                    randB.onClick.RemoveAllListeners();
                    randB.onClick.AddListener(() =>
                    {
                        if (panel == null) return;
                        // Keep presets list open so user can dice again / pick another.
                        panel.RandomizeFromFilterPreset(entry, true);
                    });
                }
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
                                    VPBTranslation.T("quickfilters.tip.randomize_merge", "Randomize '{0}' — loads each merged filter in order"),
                                    entry.Name)
                                : string.Format(
                                    VPBTranslation.T("quickfilters.tip.randomize", "Randomize from '{0}' (loads random item; restores current view)"),
                                    entry.Name);
                            panel.SetStatus(tip);
                        }
                        else panel.SetStatus(null);
                    };
                }
            }
            else if (pendingDelete)
            {
                // Inline confirm — stay in list (no external DisplayConfirm modal).
                GameObject cancelBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject confirmBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (cancelBtn != null) cancelBtn.name = "CancelDeleteBtn";
                if (confirmBtn != null) confirmBtn.name = "ConfirmDeleteBtn";
                Color cancelBackdrop = new Color(0.22f, 0.24f, 0.28f, 1f);
                Color confirmBackdrop = new Color(0.55f, 0.16f, 0.16f, 1f);
                setupSquare(confirmBtn, sprDelete, Color.white, -padR, confirmBackdrop);
                setupSquare(cancelBtn, sprCancel, Color.white, -(padR + sq + gap), cancelBackdrop);

                var cb = confirmBtn != null ? confirmBtn.GetComponent<Button>() : null;
                if (cb != null)
                {
                    cb.onClick.RemoveAllListeners();
                    cb.onClick.AddListener(() =>
                    {
                        QuickFilterSettings.Instance.RemoveFilter(entry);
                        pendingDeleteEntry = null;
                        Refresh();
                    });
                }
                var xb = cancelBtn != null ? cancelBtn.GetComponent<Button>() : null;
                if (xb != null)
                {
                    xb.onClick.RemoveAllListeners();
                    xb.onClick.AddListener(() =>
                    {
                        pendingDeleteEntry = null;
                        Refresh();
                    });
                }

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
            else
            {
                // Edit: pin (overflow investment) + rename + delete — pin hidden outside edit.
                GameObject pinBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject renameBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject deleteBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (pinBtn != null) pinBtn.name = "PinBtn";
                if (renameBtn != null) renameBtn.name = "RenameBtn";
                if (deleteBtn != null) deleteBtn.name = "DeleteBtn";
                if (pinBtn != null && renameBtn != null && deleteBtn != null)
                {
                    Color pinBackdrop = entry.Pinned
                        ? new Color(0.32f, 0.28f, 0.16f, 1f)
                        : new Color(0.22f, 0.24f, 0.28f, 1f);
                    Color renameBackdrop = new Color(0.20f, 0.26f, 0.34f, 1f);
                    Color deleteBackdrop = new Color(0.45f, 0.22f, 0.22f, 1f);
                    // From right: delete, rename, pin
                    setupSquare(deleteBtn, sprDelete, Color.white, -padR, deleteBackdrop);
                    setupSquare(renameBtn, sprRename, Color.white, -(padR + sq + gap), renameBackdrop);
                    setupSquare(pinBtn, entry.Pinned ? sprPinOn : sprPinOff, Color.white, -(padR + 2f * (sq + gap)), pinBackdrop);

                    addReorderHandle(pinBtn);
                    addReorderHandle(renameBtn);
                    addReorderHandle(deleteBtn);

                    var pb = pinBtn.GetComponent<Button>();
                    if (pb != null)
                    {
                        pb.onClick.RemoveAllListeners();
                        pb.onClick.AddListener(() =>
                        {
                            QuickFilterSettings.Instance.TogglePinned(entry);
                            Refresh();
                        });
                    }
                    var ph = pinBtn.AddComponent<UIHoverDelegate>();
                    ph.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter)
                        {
                            panel.SetStatus(entry.Pinned
                                ? string.Format(VPBTranslation.T("quickfilters.tip.unpin", "Unpin '{0}' from overflow quick-random"), entry.Name)
                                : string.Format(VPBTranslation.T("quickfilters.tip.pin", "Pin '{0}' to overflow as quick-random"), entry.Name));
                        }
                        else panel.SetStatus(null);
                    };

                    var rb = renameBtn.GetComponent<Button>();
                    if (rb != null)
                    {
                        rb.onClick.RemoveAllListeners();
                        rb.onClick.AddListener(() => BeginInlineRename(btn, entry));
                    }

                    var db = deleteBtn.GetComponent<Button>();
                    if (db != null)
                    {
                        db.onClick.RemoveAllListeners();
                        db.onClick.AddListener(() => ArmInlineDelete(entry));
                    }

                    var rh = renameBtn.AddComponent<UIHoverDelegate>();
                    rh.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(string.Format(VPBTranslation.T("quickfilters.tip.rename", "Rename '{0}' inline"), entry.Name));
                        else panel.SetStatus(null);
                    };
                    var dh = deleteBtn.AddComponent<UIHoverDelegate>();
                    dh.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(string.Format(VPBTranslation.T("quickfilters.tip.delete", "Delete '{0}'"), entry.Name));
                        else panel.SetStatus(null);
                    };
                }
            }
            } // !mergeMode

            if (!mergeMode && !pendingDelete)
            {
            var rightClick = btn.AddComponent<UIRightClickDelegate>();
            rightClick.OnRightClick = () => {
                ShowContextMenu(btn, entry);
            };
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
                    else if (pendingDelete)
                    {
                        info = string.Format(
                            VPBTranslation.T("quickfilters.delete_inline_hint", "Confirm or cancel delete of '{0}' (Esc cancels)"),
                            entry.Name);
                    }
                    else if (editMode)
                    {
                        info = string.Format(VPBTranslation.T("quickfilters.edit_hint", "Edit '{0}' (Icons: pin / rename / delete. Drag when Manual sort.)"), entry.Name);
                    }
                    else if (entry.IsMerged)
                    {
                        info = string.Format(VPBTranslation.T("quickfilters.merge_apply_hint", "Merged '{0}' — showing first filter. Dice loads all {1}."), entry.Name, entry.MergeMembers != null ? entry.MergeMembers.Count : 0);
                    }
                    else
                    {
                        info = string.Format(VPBTranslation.T("quickfilters.apply_hint", "Apply '{0}' (Dice = randomize. Drag to reorder when Manual sort.)"), entry.Name);
                    }
                    panel.SetStatus(info);
                }
                else if (panel != null) panel.SetStatus(null);
            };

            activeButtons.Add(btn);
            buttonToEntry[btn] = entry;
        }

        private void ArmInlineDelete(QuickFilterEntry entry)
        {
            if (entry == null) return;
            renamingEntry = null;
            pendingDeleteEntry = entry;
            if (!editMode) editMode = true;
            Refresh();
            if (panel != null)
            {
                panel.SetStatus(string.Format(
                    VPBTranslation.T("quickfilters.tip.delete_armed", "Delete '{0}'? Confirm on row or Esc to cancel"),
                    entry.Name));
            }
        }

        /// <summary>Esc / click-away cancel for armed inline delete. Returns true if consumed.</summary>
        public bool TryCancelPendingDelete()
        {
            if (pendingDeleteEntry == null) return false;
            pendingDeleteEntry = null;
            Refresh();
            return true;
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

        /// <summary>ALT+F path: ensure floating chrome + visible (keep detach on close).</summary>
        public void EnsureDetachedAndVisible()
        {
            if (!detached)
                Detach();
            SetVisible(true);
        }

        private void BeginInlineRename(GameObject row, QuickFilterEntry entry)
        {
            if (row == null || entry == null || !editMode || mergeMode) return;
            if (pendingDeleteEntry != null)
            {
                pendingDeleteEntry = null;
                Refresh();
                GameObject rebuiltAfterCancel = FindRowForEntry(entry);
                if (rebuiltAfterCancel != null) BeginInlineRename(rebuiltAfterCancel, entry);
                return;
            }
            if (renamingEntry != null)
            {
                if (ReferenceEquals(renamingEntry, entry)) return;
                renamingEntry = null;
                Refresh();
                GameObject rebuilt = FindRowForEntry(entry);
                if (rebuilt != null) BeginInlineRename(rebuilt, entry);
                return;
            }

            renamingEntry = entry;

            Transform textT = row.transform.Find("Text");
            if (textT != null) textT.gameObject.SetActive(false);

            Transform renameBtnT = row.transform.Find("RenameBtn");
            if (renameBtnT != null) renameBtnT.gameObject.SetActive(false);

            Button rowBtn = row.GetComponent<Button>();
            if (rowBtn != null) rowBtn.interactable = false;

            UIListReorderable[] reorders = row.GetComponentsInChildren<UIListReorderable>(true);
            if (reorders != null)
            {
                for (int i = 0; i < reorders.Length; i++)
                {
                    if (reorders[i] != null) reorders[i].enabled = false;
                }
            }

            float s = panel != null ? panel.ChromeScale : 1f;
            float padL = RowTextPadLeftRef * s;
            float iconSq = GalleryUiDesignTokens.ButtonSizeRef * s;
            float padR = 4f * s;
            float gap = 4f * s;
            // Pin + delete still visible (rename chip hidden).
            float rightReserve = padR + 2f * iconSq + gap;

            GameObject inputHost = new GameObject("RenameInput");
            inputHost.transform.SetParent(row.transform, false);
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
            if (field == null)
            {
                renamingEntry = null;
                Refresh();
                return;
            }

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
            field.onEndEdit.AddListener(val => FinishInlineRename(val));

            try
            {
                var esc = inputHost.AddComponent<SearchInputESCHandler>();
                esc.Initialize(field, null, () =>
                {
                    renamingEntry = null;
                    Refresh();
                });
            }
            catch { }
            try
            {
                inputHost.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(field);
            }
            catch { }

            try
            {
                field.ActivateInputField();
                field.Select();
                field.MoveTextEnd(false);
            }
            catch { }

            if (panel != null)
                panel.SetStatus(VPBTranslation.T("quickfilters.tip.rename_inline", "Enter to save · Esc to cancel"));
        }

        private void FinishInlineRename(string val)
        {
            if (renamingEntry == null) return;
            QuickFilterEntry entry = renamingEntry;
            renamingEntry = null;

            string trimmed = val != null ? val.Trim() : "";
            if (!string.IsNullOrEmpty(trimmed))
                QuickFilterSettings.Instance.RenameFilter(entry, trimmed);

            Refresh();
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
            editMode = false;
            pendingDeleteEntry = null;
            mergeMode = true;
            mergeSelection.Clear();
            if (panel != null)
            {
                panel.SetStatus(string.Format(
                    VPBTranslation.T("quickfilters.merge_pick", "Select 2–{0} presets to merge. Dice on result loads each."),
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
            // Seed browse fields from first leaf for apply-first behavior consistency.
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
            if (panel == null) return;

            var entry = panel.CaptureQuickFilterState();
            if (entry != null)
            {
                QuickFilterSettings.Instance.AddFilter(entry);
                Refresh();
            }
        }

        private void ApplyFilter(QuickFilterEntry entry)
        {
            if (panel == null) return;
            panel.ApplyQuickFilterState(entry);
        }

        private void ShowContextMenu(GameObject btn, QuickFilterEntry entry)
        {
            var options = new List<ContextMenuPanel.Option>();

            options.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("quickfilters.ctx.randomize", "Randomize"),
                VPBTranslation.T("quickfilters.ctx.randomize_sub", "Load random item from this filter set"),
                () => {
                    if (panel == null) return;
                    panel.RandomizeFromFilterPreset(entry, true);
                },
                ContextMenuPanel.OptionKind.Normal));

            options.Add(new ContextMenuPanel.Option(
                entry.Pinned
                    ? VPBTranslation.T("quickfilters.ctx.unpin", "Unpin")
                    : VPBTranslation.T("quickfilters.ctx.pin", "Pin"),
                entry.Pinned
                    ? VPBTranslation.T("quickfilters.ctx.unpin_sub", "Remove from overflow quick-random")
                    : VPBTranslation.T("quickfilters.ctx.pin_sub", "Add to overflow as quick-random"),
                () => {
                    QuickFilterSettings.Instance.TogglePinned(entry);
                    Refresh();
                },
                ContextMenuPanel.OptionKind.Normal));

            options.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("quickfilters.ctx.rename", "Rename"),
                VPBTranslation.T("quickfilters.ctx.rename_sub", "Change name inline"),
                () => {
                    if (!editMode)
                    {
                        editMode = true;
                        Refresh();
                        // Find rebuilt row for this entry, then rename.
                        GameObject rebuilt = FindRowForEntry(entry);
                        if (rebuilt != null) BeginInlineRename(rebuilt, entry);
                        return;
                    }
                    BeginInlineRename(btn, entry);
                },
                ContextMenuPanel.OptionKind.Normal));

            options.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("quickfilters.ctx.change_color", "Change Color"),
                VPBTranslation.T("quickfilters.ctx.color_sub", "Button tint"),
                () => {
                    panel.DisplayColorPicker(VPBTranslation.T("quickfilters.edit_color_title", "Edit Color"), entry.ButtonColor, (Color val) => {
                        entry.ButtonColor = val;
                        QuickFilterSettings.Instance.Save();
                        Refresh();
                    });
                },
                ContextMenuPanel.OptionKind.Normal));

            options.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("quickfilters.ctx.delete", "Delete"),
                VPBTranslation.T("quickfilters.ctx.delete_sub", "Remove this quick filter"),
                () => { ArmInlineDelete(entry); },
                ContextMenuPanel.OptionKind.Destructive));

            ContextMenuPanel.Instance.Show(btn.transform.position, options, VPBTranslation.T("quickfilters.ctx.header_prefix", "Filter: ") + entry.Name);
        }

        public void RefreshLocalizedUi()
        {
            if (searchInput != null && searchInput.placeholder is Text ph)
                ph.text = VPBTranslation.T("quickfilters.search_ph", "Filter presets…");
            if (titleBarLabel != null && !mergeMode)
                titleBarLabel.text = VPBTranslation.T("quickfilters.float_title", "Filter Presets");
            if (detachListBtnText != null)
                detachListBtnText.text = VPBTranslation.T("quickfilters.detach_list", "Float as Window");
            SyncSortButtonVisual();
            SyncMergeChrome();
            Refresh();
            if (containerGO != null)
            {
                foreach (Text t in containerGO.GetComponentsInChildren<Text>(true))
                    VPBUiFont.ApplyTo(t);
            }
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
            if (detached)
            {
                if (!floatCollapsed)
                    CaptureFloatGeometryToMemory();
                else
                    RestoreExpandHeightIntoSavedSize();
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
            string path = floatCollapsed ? "vpb_icons/chevron_down.png" : "vpb_icons/chevron_up.png";
            Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
            if (spr == null) return;
            if (collapseBtnIcon != null)
            {
                collapseBtnIcon.sprite = spr;
                collapseBtnIcon.color = Color.white;
            }
            else
            {
                StyleChromeIconBtn(collapseBtnGO, GalleryUiDesignTokens.ButtonSizeRef, path);
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
            if (detachListBtnGO != null) detachListBtnGO.SetActive(false);

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
                        // Keep on-screen place after pop-out (recognition > jump).
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
            // Detach list button rebuilt on next Refresh when docked.

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
            CaptureFloatGeometryToMemory();
            try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
        }

        private void OnFloatResized()
        {
            CaptureFloatGeometryToMemory();
            PersistGeometry();
            try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
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

        private sealed class QuickFiltersPanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public RectTransform Target;
            public Action OnMoved;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null || eventData == null) return;
                Target.anchoredPosition += eventData.delta;
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (OnMoved != null) OnMoved();
            }
        }

        private sealed class QuickFiltersPanelResize : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public RectTransform Target;
            public Func<Vector2> GetMinSize;
            public Func<Vector2> GetMaxSize;
            public Action OnResizing;
            public Action OnResized;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null || eventData == null) return;
                Vector2 size = Target.sizeDelta;
                size.x += eventData.delta.x;
                size.y -= eventData.delta.y;
                Vector2 min = GetMinSize != null ? GetMinSize() : new Vector2(220f, 200f);
                Vector2 max = GetMaxSize != null ? GetMaxSize() : new Vector2(640f, 900f);
                size.x = Mathf.Clamp(size.x, min.x, max.x);
                size.y = Mathf.Clamp(size.y, min.y, max.y);
                Target.sizeDelta = size;
                if (OnResizing != null) OnResizing();
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (OnResized != null) OnResized();
            }
        }
    }
}
