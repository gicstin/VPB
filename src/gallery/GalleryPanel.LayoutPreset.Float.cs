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
        private const float LayoutFloatDefaultWidthRef = 420f;
        private const float LayoutFloatDefaultHeightRef = 460f;
        private const float LayoutFloatMinWidthRef = 320f;
        private const float LayoutFloatMinHeightRef = 240f;
        private const float LayoutFloatMaxWidthRef = 720f;
        private const float LayoutFloatMaxHeightRef = 900f;
        /// <summary>Match filter-presets list row: one line + <see cref="GalleryUiDesignTokens.ButtonSizeRef"/> chips.</summary>
        private const float LayoutPresetRowHeightRef = GalleryUiDesignTokens.PopupMenuRowHeightRef;
        private const float LayoutPresetRowGapRef = GalleryUiDesignTokens.PopupMenuRowSpacingRef;
        private const float LayoutPresetRowPadHRef = 6f;
        private const float LayoutPresetRowPadVRef = 2f;
        /// <summary>Applied-preset marker: a left edge stripe, not a glyph competing with the name.</summary>
        private const float LayoutPresetActiveStripeWidthRef = 3f;
        private const float LayoutPresetMiniMapWidthRef = 36f;
        private const float LayoutPresetMiniMapInsetRef = 4f;
        private const int LayoutPresetMiniMapCellCount = 4;
        /// <summary>Rows built beyond the viewport so a flick does not expose empty space.</summary>
        private const int LayoutPresetWindowMargin = 3;
        private const int LayoutPresetsOverlaySortingOrder = 5000;
        /// <summary>
        /// VR: the pointer laser draws in the default sorting band, so a high-order canvas paints over it
        /// and the beam reads as passing behind the window. Sit in the pane band (just above panes).
        /// </summary>
        private const int LayoutPresetsWorldSortingOrder = DockBaseSortingOrder + 1000;
        /// <summary>WorldSpace overlay has no screen to size itself from — same virtual surface as a pane.</summary>
        private static readonly Vector2 LayoutOverlayWorldSizePx = new Vector2(1200f, 800f);
        private const float LayoutOverlayWorldDistanceMeters = 1.2f;

        private static GameObject s_layoutOverlayGO;
        private static Canvas s_layoutOverlayCanvas;
        private static GalleryPanel s_layoutFloatOwner;

        private GameObject _layoutFloatRoot;
        private RectTransform _layoutFloatPanelRT;
        private RectTransform _layoutFloatTitleBarRT;
        private Text _layoutFloatTitleText;
        private GameObject _layoutFloatSearchRow;
        private GameObject _layoutFloatScrollHost;
        private GameObject _layoutFloatFooter;
        private GameObject _layoutFloatSaveBtn;
        private GameObject _layoutFloatImportBtn;
        private GameObject _layoutFloatCloseBtn;
        private GameObject _layoutFloatResizeHandle;
        private ScrollRect _layoutFloatScrollRect;
        private RectTransform _layoutFloatContentRT;
        private Transform _layoutFloatRowsParent;
        private GameObject _layoutFloatTopSpacer;
        private GameObject _layoutFloatBottomSpacer;
        private GameObject _layoutFloatEmptyGo;
        private Text _layoutFloatEmptyText;
        private InputField _layoutFloatSearchInput;
        private UnityAction<string> _layoutFloatSearchOnValueChanged;

        private Vector2? _layoutFloatSavedPosCenter;
        private Vector2? _layoutFloatSavedSizeRef;
        private float _layoutFloatChromeScale = 1f;
        private string _layoutFloatSearch = "";
        private int _layoutFloatWindowStart = -1;
        private int _layoutFloatWindowCount;

        private readonly List<GalleryLayoutPreset> _layoutFloatVisible = new List<GalleryLayoutPreset>(32);
        private readonly List<GameObject> _layoutFloatRowPool = new List<GameObject>(24);

        internal bool IsLayoutPresetsFloatOpen()
        {
            return _layoutFloatRoot != null && _layoutFloatRoot.activeSelf;
        }

        internal static bool LayoutPresetsFloatIsOpen()
        {
            return s_layoutFloatOwner != null && s_layoutFloatOwner.IsLayoutPresetsFloatOpen();
        }

        /// <summary>
        /// Opens the manager from surfaces that may run with no pane on screen (quick menu, VR watch).
        /// Lives on its own overlay canvas — gallery hide must not swallow it.
        /// </summary>
        internal static void OpenLayoutPresetsFloatAnywhere()
        {
            Gallery g = Gallery.singleton;
            if (g == null) return;

            if (s_layoutFloatOwner != null && s_layoutFloatOwner.IsLayoutPresetsFloatOpen())
            {
                s_layoutFloatOwner.ToggleLayoutPresetsFloat();
                return;
            }

            GalleryPanel host = ResolveLayoutPresetsFloatHost(g);
            if (host == null) return;
            host.ToggleLayoutPresetsFloat();
        }

        /// <summary>
        /// Owner for apply/save coroutines. Overlay canvas is independent, so a hidden pane is
        /// enough — do not OpenGallery just to show the manager.
        /// </summary>
        private static GalleryPanel ResolveLayoutPresetsFloatHost(Gallery g)
        {
            if (g == null) return null;

            GalleryPanel host = FirstVisibleGalleryPane(g);
            if (host != null) return host;

            List<GalleryPanel> panels = g.Panels;
            if (panels != null)
            {
                for (int i = 0; i < panels.Count; i++)
                {
                    if (panels[i] != null) return panels[i];
                }
            }

            try
            {
                VamHookPlugin hook = VamHookPlugin.singleton;
                if (hook != null) hook.EnsureGalleryCategories();
                g.CreatePane(null, false);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB][Layout] host pane: " + ex.Message); }

            panels = g.Panels;
            if (panels == null || panels.Count == 0) return null;
            host = panels[panels.Count - 1];
            if (host != null)
            {
                try { host.Hide(); } catch { }
            }
            return host;
        }

        private static GalleryPanel FirstVisibleGalleryPane(Gallery g)
        {
            List<GalleryPanel> panels = g != null ? g.Panels : null;
            if (panels == null) return null;
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i] != null && panels[i].IsVisible) return panels[i];
            }
            return null;
        }

        internal void ToggleLayoutPresetsFloat()
        {
            if (s_layoutFloatOwner != null && s_layoutFloatOwner != this && s_layoutFloatOwner.IsLayoutPresetsFloatOpen())
            {
                s_layoutFloatOwner.HideLayoutPresetsFloat();
                return;
            }
            if (IsLayoutPresetsFloatOpen())
            {
                HideLayoutPresetsFloat();
                return;
            }
            EnsureLayoutPresetsFloatBuilt();
            ShowLayoutPresetsFloat();
        }

        private void ShowLayoutPresetsFloat()
        {
            if (_layoutFloatRoot == null) return;
            s_layoutFloatOwner = this;
            _layoutFloatRoot.SetActive(true);
            try { _layoutFloatRoot.transform.SetAsLastSibling(); } catch { }
            if (s_layoutOverlayGO != null) s_layoutOverlayGO.SetActive(true);
            ApplyLayoutPresetsOverlayRenderMode();
            PlaceLayoutPresetsOverlayInFrontOfPlayer();
            GalleryLayoutPresetStore.EnsureLoaded();
            float s = ResolveLayoutFloatChromeScale();
            if (Mathf.Abs(s - _layoutFloatChromeScale) > 0.0005f)
                RescaleLayoutPresetsFloatIfOpen(s);
            RefreshLayoutPresetsList(true);
            SyncAllLayoutPresetToggleStates();
        }

        private void HideLayoutPresetsFloat()
        {
            CaptureLayoutFloatGeometryToMemory();
            PersistLayoutFloatGeometry();
            CloseLayoutPresetRowMenu();
            _layoutRenamingId = 0;
            _layoutDeletingId = 0;
            if (_layoutFloatRoot != null) _layoutFloatRoot.SetActive(false);
            if (s_layoutOverlayGO != null) s_layoutOverlayGO.SetActive(false);
            if (s_layoutFloatOwner == this) s_layoutFloatOwner = null;
            SyncAllLayoutPresetToggleStates();
        }

        private static void SyncAllLayoutPresetToggleStates()
        {
            Gallery g = Gallery.singleton;
            if (g == null) return;
            List<GalleryPanel> panels = g.Panels;
            if (panels == null) return;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p != null) p.SyncLayoutPresetToggleState();
            }
        }

        private void SyncLayoutPresetToggleState()
        {
            bool on = LayoutPresetsFloatIsOpen();
            if (layoutPresetsToggleBtnIconImage != null)
                layoutPresetsToggleBtnIconImage.color = on ? Color.green : Color.white;
            else if (layoutPresetsToggleBtnText != null)
                layoutPresetsToggleBtnText.color = on ? Color.green : Color.white;
        }

        internal static void TickLayoutPresetsOverlay()
        {
            GalleryPanel owner = s_layoutFloatOwner;
            if (owner == null || !owner.IsLayoutPresetsFloatOpen()) return;
            // Pane Update early-outs when canvas is off — hotkey + Esc live here then.
            try { GalleryUiScaleHotkey.TryNudgeFromKeyboard(); } catch { }
            try { GalleryUiScaleHotkey.TickDeferredSave(); } catch { }
            bool paneAlive = false;
            try { paneAlive = owner.canvas != null && owner.canvas.enabled; } catch { }
            if (!paneAlive)
            {
                try { owner.TryHandleLayoutPresetsFloatKeyboard(); } catch { }
            }
            try
            {
                float s = ResolveLayoutFloatChromeScale();
                owner.RescaleLayoutPresetsFloatIfOpen(s);
            }
            catch { }
        }

        private static float ResolveLayoutFloatChromeScale()
        {
            try
            {
                float s = GalleryUiMetrics.Resolve(true).ChromeScale;
                if (s > 0.01f) return s;
            }
            catch { }
            return 1f;
        }

        private static GameObject EnsureLayoutPresetsOverlay()
        {
            if (s_layoutOverlayGO != null) return s_layoutOverlayGO;

            Transform parent = null;
            try
            {
                if (Gallery.singleton != null) parent = Gallery.singleton.transform;
                else if (VamHookPlugin.singleton != null) parent = VamHookPlugin.singleton.transform;
            }
            catch { }

            GameObject go = new GameObject("VPB_LayoutPresetsOverlay");
            go.layer = 5;
            if (parent != null) go.transform.SetParent(parent, false);

            Canvas c = go.AddComponent<Canvas>();
            c.pixelPerfect = false;
            c.overrideSorting = true;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4;

            s_layoutOverlayGO = go;
            s_layoutOverlayCanvas = c;
            // Render mode before registration: VaM binds the canvas by the mode it is in.
            ApplyLayoutPresetsOverlayRenderMode();

            go.AddComponent<GraphicRaycaster>();
            try
            {
                if (SuperController.singleton != null)
                    SuperController.singleton.AddCanvas(c);
            }
            catch { }

            go.AddComponent<LayoutPresetsOverlayTick>();
            return go;
        }

        /// <summary>
        /// VR renders through the HMD camera, where a ScreenSpaceOverlay canvas only ever reaches the
        /// companion window — the manager was built and toggled green but never drawn in the headset.
        /// Mirror the pane's own WorldSpace setup and sit in player-UI space so worldScale cannot resize it.
        /// </summary>
        private static void ApplyLayoutPresetsOverlayRenderMode()
        {
            GameObject go = s_layoutOverlayGO;
            Canvas c = s_layoutOverlayCanvas;
            if (go == null || c == null) return;

            bool vr = false;
            try { vr = XrUtils.IsVrActive(); } catch { vr = false; }

            if (!vr)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = LayoutPresetsOverlaySortingOrder;
                c.worldCamera = null;
                go.transform.localScale = Vector3.one;
                return;
            }

            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = LayoutPresetsWorldSortingOrder;

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null && rt.sizeDelta != LayoutOverlayWorldSizePx)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = LayoutOverlayWorldSizePx;
            }

            try { if (Camera.main != null) c.worldCamera = Camera.main; } catch { }

            int layerBefore = go.layer;
            VpbWorldSpaceUiScale.AttachToPlayerUiSpace(go.transform);
            if (go.layer != layerBefore)
            {
                try { SetLayerRecursiveLocal(go, go.layer); } catch { }
            }
        }

        /// <summary>
        /// Park the WorldSpace overlay in front of the player on every open: without a screen to anchor to,
        /// a pose change since last use otherwise leaves the manager behind the player or inside a pane.
        /// </summary>
        private static void PlaceLayoutPresetsOverlayInFrontOfPlayer()
        {
            GameObject go = s_layoutOverlayGO;
            Canvas c = s_layoutOverlayCanvas;
            if (go == null || c == null || c.renderMode != RenderMode.WorldSpace) return;

            Transform camTf = null;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null && sc.centerCameraTarget != null)
                    camTf = sc.centerCameraTarget.transform;
            }
            catch { }
            if (camTf == null && Camera.main != null) camTf = Camera.main.transform;
            if (camTf == null) return;

            Vector3 forward = camTf.forward * LayoutOverlayWorldDistanceMeters;
            Transform tf = go.transform;
            tf.position = camTf.position + forward;
            tf.rotation = Quaternion.LookRotation(forward, Vector3.up);
            VpbWorldSpaceUiScale.ApplyMetersPerPixelLocalScale(tf);
        }

        /// <summary>Null for the desktop overlay canvas; the HMD camera when the manager is WorldSpace.</summary>
        private static Camera ResolveLayoutFloatUiCamera()
        {
            Canvas c = s_layoutOverlayCanvas;
            if (c == null || c.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return c.worldCamera != null ? c.worldCamera : Camera.main;
        }

        /// <summary>
        /// In VR the overlay leaves Gallery's transform for player-UI space, so Gallery teardown no
        /// longer reaches it by parenting — destroy it explicitly.
        /// </summary>
        internal static void DestroyLayoutPresetsOverlay()
        {
            s_layoutFloatOwner = null;
            if (s_layoutOverlayGO != null)
            {
                try { UnityEngine.Object.Destroy(s_layoutOverlayGO); } catch { }
            }
            s_layoutOverlayGO = null;
            s_layoutOverlayCanvas = null;
        }

        private void EnsureLayoutPresetsFloatBuilt()
        {
            if (_layoutFloatRoot != null) return;

            GameObject host = EnsureLayoutPresetsOverlay();
            if (host == null) return;

            LoadLayoutFloatGeometryFromConfig();

            float s = ResolveLayoutFloatChromeScale();
            _layoutFloatChromeScale = s;

            int font = GalleryUiDesignTokens.PopupMenuRowFontRef;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float searchRowH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;

            float panelWRef = _layoutFloatSavedSizeRef.HasValue
                ? Mathf.Clamp(_layoutFloatSavedSizeRef.Value.x, LayoutFloatMinWidthRef, LayoutFloatMaxWidthRef)
                : LayoutFloatDefaultWidthRef;
            float panelHRef = _layoutFloatSavedSizeRef.HasValue
                ? Mathf.Clamp(_layoutFloatSavedSizeRef.Value.y, LayoutFloatMinHeightRef, LayoutFloatMaxHeightRef)
                : LayoutFloatDefaultHeightRef;

            _layoutFloatRoot = UI.CreateChildRT(host, "VPB_LayoutPresetsFloat", AnchorPresets.stretchAll);
            try { SetLayerRecursiveLocal(_layoutFloatRoot, host.layer); } catch { }

            GameObject panel = UI.CreateChildRT(
                _layoutFloatRoot, "Panel", AnchorPresets.middleCenter,
                new Vector2(panelWRef * s, panelHRef * s), Vector2.zero);
            UI.AddImage(panel, GalleryUiColorTokens.SurfaceDeep);
            if (panel.GetComponent<RectMask2D>() == null) panel.AddComponent<RectMask2D>();

            _layoutFloatPanelRT = panel.GetComponent<RectTransform>();
            if (_layoutFloatPanelRT != null)
            {
                _layoutFloatPanelRT.pivot = new Vector2(0f, 1f);
                _layoutFloatPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _layoutFloatPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _layoutFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, panelHRef * s);
                Vector2 center = _layoutFloatSavedPosCenter.HasValue ? _layoutFloatSavedPosCenter.Value : Vector2.zero;
                _layoutFloatPanelRT.anchoredPosition = new Vector2(
                    center.x - _layoutFloatPanelRT.sizeDelta.x * 0.5f,
                    center.y + _layoutFloatPanelRT.sizeDelta.y * 0.5f);
            }

            BuildLayoutFloatTitleBar(panel, font, s, titleH, chromeSz);
            BuildLayoutFloatSearchRow(panel, font, s, titleH, searchRowH, searchH, chromeSz);
            BuildLayoutFloatFooter(panel, font, s, footerH, chromeSz);
            BuildLayoutFloatList(panel, font, s, titleH + searchRowH, footerH);

            _layoutFloatRoot.SetActive(false);
            if (s_layoutOverlayGO != null) s_layoutOverlayGO.SetActive(false);
        }

        private void BuildLayoutFloatTitleBar(GameObject panel, int font, float s, float titleH, float chromeSz)
        {
            GameObject titleBar = UI.CreateChildRT(panel, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            UI.AddImage(titleBar, GalleryUiColorTokens.SurfaceDark);
            _layoutFloatTitleBarRT = titleBar.GetComponent<RectTransform>();
            if (_layoutFloatTitleBarRT != null)
            {
                _layoutFloatTitleBarRT.pivot = new Vector2(0.5f, 1f);
                _layoutFloatTitleBarRT.anchoredPosition = Vector2.zero;
                _layoutFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
            }

            HorizontalLayoutGroup hlg = UI.AddHLG(
                titleBar, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (titleBar.GetComponent<RectMask2D>() == null) titleBar.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(titleBar, "\u2807", GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter, raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyFloatTitleBarMetrics(hlg, grip.gameObject, s);

            UI.CreateFloatTitleWindowIcon(titleBar, "layout-board-split", GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);

            _layoutFloatTitleText = UI.CreateLabel(
                titleBar,
                VPBTranslation.T("gallery.layout_preset.title", "Layout presets"),
                GalleryUiDesignTokens.PopupMenuRowFontRef, Color.white, TextAnchor.MiddleLeft,
                raycastTarget: false, name: "Title");
            GalleryUiMetrics.ApplyFont(_layoutFloatTitleText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(_layoutFloatTitleText.gameObject, flexibleWidth: 1f, minWidth: 60f * s);

            _layoutFloatCloseBtn = UI.CreateFloatChromeIconButton(
                titleBar.transform, chromeSz, "x", GalleryUiColorTokens.ChromeIconWell, HideLayoutPresetsFloat);
            if (_layoutFloatCloseBtn != null)
            {
                _layoutFloatCloseBtn.name = "TitleClose";
                AddTooltip(_layoutFloatCloseBtn, "gallery.layout_preset.close", "Close");
            }

            var headerDrag = titleBar.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = _layoutFloatPanelRT;
            headerDrag.OnMoved = OnLayoutFloatMoved;
        }

        private void BuildLayoutFloatSearchRow(
            GameObject panel, int font, float s, float titleH, float rowH, float searchH, float chromeSz)
        {
            GameObject row = UI.CreateChildRT(panel, "SearchRow", AnchorPresets.hStretchTop,
                new Vector2(0f, rowH), new Vector2(0f, -titleH));
            _layoutFloatSearchRow = row;
            RectTransform rt = row.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, rowH);
                rt.anchoredPosition = new Vector2(0f, -titleH);
            }
            UI.AddHLG(row, spacing: UI.GapTight(s),
                padding: UI.Pad(
                    GalleryUiDesignTokens.FloatSearchRowPadRef, GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef, GalleryUiDesignTokens.FloatSearchRowPadRef, s),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            _layoutFloatSearchInput = UI.CreateChromeLayoutInputField(
                row.transform, font, searchH, 1f, 8f * s, 2f * s,
                GalleryUiColorTokens.SurfaceDarker, UI.InputFieldPlaceholderColor,
                VPBTranslation.T("gallery.layout_preset.search", "Search layouts…"),
                "LayoutSearch");
            if (_layoutFloatSearchInput != null)
            {
                try { UI.LayoutChromeSearchIcon(_layoutFloatSearchInput.gameObject, s); } catch { }
                if (_layoutFloatSearchInput.textComponent != null)
                    GalleryUiMetrics.ApplyFont(_layoutFloatSearchInput.textComponent, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                Text ph = _layoutFloatSearchInput.placeholder as Text;
                if (ph != null)
                    GalleryUiMetrics.ApplyFont(ph, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                _layoutFloatSearchOnValueChanged = v =>
                {
                    _layoutFloatSearch = v ?? "";
                    RefreshLayoutPresetsList(true);
                };
                _layoutFloatSearchInput.onValueChanged.AddListener(_layoutFloatSearchOnValueChanged);
            }
        }

        private void BuildLayoutFloatFooter(GameObject panel, int font, float s, float footerH, float chromeSz)
        {
            _layoutFloatFooter = UI.CreateChildRT(panel, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(_layoutFloatFooter, GalleryUiColorTokens.SurfaceDarker);
            RectTransform footerRT = _layoutFloatFooter.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(_layoutFloatFooter, spacing: UI.GapTight(s), padding: UI.PadFloatFooter(s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (_layoutFloatFooter.GetComponent<RectMask2D>() == null)
                _layoutFloatFooter.AddComponent<RectMask2D>();

            GameObject dragArea = UI.CreateFloatFooterDragArea(_layoutFloatFooter);
            if (dragArea != null)
            {
                var d = dragArea.AddComponent<UIFloatPanelDrag>();
                d.Target = _layoutFloatPanelRT;
                d.OnMoved = OnLayoutFloatMoved;
            }

            _layoutFloatSaveBtn = UI.CreateChromeLayoutButton(
                _layoutFloatFooter.transform, 150f * s, chromeSz,
                VPBTranslation.T("gallery.layout_preset.save_current", "Save current layout"),
                GalleryUiDesignTokens.PopupMenuRowFontRef, UI.AccentGreen, SaveCurrentLayoutFromFloat);
            if (_layoutFloatSaveBtn != null)
            {
                _layoutFloatSaveBtn.name = "FooterSave";
                AddTooltip(_layoutFloatSaveBtn, "gallery.layout_preset.save_current.tip",
                    "Store the current window arrangement as a new preset");
                Text saveTxt = _layoutFloatSaveBtn.GetComponentInChildren<Text>();
                if (saveTxt != null)
                    GalleryUiMetrics.ApplyFont(saveTxt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            _layoutFloatImportBtn = UI.CreateFloatChromeIconButton(
                _layoutFloatFooter.transform, chromeSz, "arrow-bar-to-down",
                GalleryUiColorTokens.ChromeIconWell, ImportLayoutPresetsFromFloat);
            if (_layoutFloatImportBtn != null)
            {
                _layoutFloatImportBtn.name = "FooterImport";
                AddTooltip(_layoutFloatImportBtn, "gallery.layout_preset.import",
                    "Import shared layouts from Saves/PluginData/VPB/Layouts");
            }

            GameObject spacer = new GameObject("Spacer");
            spacer.transform.SetParent(_layoutFloatFooter.transform, false);
            spacer.AddComponent<RectTransform>();
            UI.AddLE(spacer, flexibleWidth: 1f, minWidth: 8f * s);
            UI.EnsureFloatFooterSpacerDragHit(spacer);
            var spacerDrag = spacer.AddComponent<UIFloatPanelDrag>();
            spacerDrag.Target = _layoutFloatPanelRT;
            spacerDrag.OnMoved = OnLayoutFloatMoved;

            GameObject resizeHandle = UI.AddChildGOImage(
                _layoutFloatFooter, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
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
                if (rhSpr != null) UI.AddIconToButton(resizeHandle, rhSpr, 5f * s, UI.IconButtonBackdrop);
            }
            catch { }

            var resizer = resizeHandle.AddComponent<UIFloatPanelResize>();
            resizer.Target = _layoutFloatPanelRT;
            resizer.GetMinSize = () => new Vector2(
                LayoutFloatMinWidthRef * _layoutFloatChromeScale, LayoutFloatMinHeightRef * _layoutFloatChromeScale);
            resizer.GetMaxSize = () => new Vector2(
                LayoutFloatMaxWidthRef * _layoutFloatChromeScale, LayoutFloatMaxHeightRef * _layoutFloatChromeScale);
            resizer.OnResized = OnLayoutFloatResized;
            _layoutFloatResizeHandle = resizeHandle;
        }

        private void BuildLayoutFloatList(GameObject panel, int font, float s, float topInset, float footerH)
        {
            GameObject scrollHost = UI.CreateChildRT(panel, "ScrollHost", AnchorPresets.stretchAll);
            _layoutFloatScrollHost = scrollHost;
            RectTransform hostRT = scrollHost.GetComponent<RectTransform>();
            if (hostRT != null)
            {
                hostRT.offsetMin = new Vector2(0f, footerH);
                hostRT.offsetMax = new Vector2(0f, -topInset);
            }
            UI.AddImage(scrollHost, GalleryUiColorTokens.ModalSurface);
            if (scrollHost.GetComponent<RectMask2D>() == null) scrollHost.AddComponent<RectMask2D>();

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            _layoutFloatScrollRect = scrollHost.AddComponent<ScrollRect>();
            _layoutFloatScrollRect.horizontal = false;
            _layoutFloatScrollRect.vertical = true;
            _layoutFloatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _layoutFloatScrollRect.scrollSensitivity =
                VpbScrollTuning.Sensitivity(GalleryUiDesignTokens.SettingsFloatScrollSensitivityRef, 1f);
            _layoutFloatScrollRect.verticalScrollbar = null;

            GameObject viewport = UI.CreateChildRT(scrollHost, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            if (vpRt != null) vpRt.offsetMax = new Vector2(-sbW, 0f);
            viewport.AddComponent<RectMask2D>();
            _layoutFloatScrollRect.viewport = vpRt;

            GameObject scrollbarGO = UI.CreateScrollBar(scrollHost, sbW, 0f, Scrollbar.Direction.BottomToTop);
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
            sync.scrollRect = _layoutFloatScrollRect;
            sync.scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            sync.minSizePixels = 20f;

            GameObject content = UI.CreateChildRT(viewport, "Content", AnchorPresets.hStretchTop);
            _layoutFloatContentRT = content.GetComponent<RectTransform>();
            _layoutFloatScrollRect.content = _layoutFloatContentRT;
            VerticalLayoutGroup cv = UI.AddVLG(content, spacing: LayoutPresetRowGapRef * s,
                padding: UI.PadPopup(s));
            cv.childForceExpandHeight = false;
            cv.childForceExpandWidth = true;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _layoutFloatRowsParent = content.transform;

            _layoutFloatTopSpacer = CreateLayoutRowSpacer(content, "TopSpacer");
            _layoutFloatBottomSpacer = CreateLayoutRowSpacer(content, "BottomSpacer");

            _layoutFloatScrollRect.onValueChanged.AddListener(OnLayoutFloatScrolled);

            _layoutFloatEmptyGo = UI.CreateChildRT(viewport, "Empty", AnchorPresets.stretchAll);
            VerticalLayoutGroup ev = UI.AddVLG(_layoutFloatEmptyGo, spacing: UI.GapControl(s), padding: UI.PadGroup(s));
            ev.childAlignment = TextAnchor.MiddleCenter;
            ev.childForceExpandHeight = false;
            ev.childForceExpandWidth = true;
            _layoutFloatEmptyText = UI.CreateLabel(
                _layoutFloatEmptyGo,
                VPBTranslation.T("gallery.layout_preset.empty", "No saved layouts yet"),
                font, GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "EmptyLabel");
            GalleryUiMetrics.ApplyFont(_layoutFloatEmptyText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            _layoutFloatEmptyGo.SetActive(false);
        }

        private static GameObject CreateLayoutRowSpacer(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 0f;
            le.preferredHeight = 0f;
            le.flexibleHeight = 0f;
            return go;
        }

        /// <summary>Live ChromeScale adapt — resize shell + rebuild pooled rows once.</summary>
        internal void RescaleLayoutPresetsFloatIfOpen(float chromeScale)
        {
            if (!IsLayoutPresetsFloatOpen()) return;
            if (_layoutFloatPanelRT == null) return;

            float s = chromeScale > 0f ? chromeScale : ResolveLayoutFloatChromeScale();
            if (s <= 0.01f) s = 1f;
            if (Mathf.Abs(s - _layoutFloatChromeScale) < 0.0005f) return;

            CaptureLayoutFloatGeometryToMemory();
            _layoutFloatChromeScale = s;

            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float searchRowH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;

            float panelWRef = _layoutFloatSavedSizeRef.HasValue
                ? _layoutFloatSavedSizeRef.Value.x
                : LayoutFloatDefaultWidthRef;
            float panelHRef = _layoutFloatSavedSizeRef.HasValue
                ? _layoutFloatSavedSizeRef.Value.y
                : LayoutFloatDefaultHeightRef;
            panelWRef = Mathf.Clamp(panelWRef, LayoutFloatMinWidthRef, LayoutFloatMaxWidthRef);
            panelHRef = Mathf.Clamp(panelHRef, LayoutFloatMinHeightRef, LayoutFloatMaxHeightRef);

            Vector2 keepTopLeft = _layoutFloatPanelRT.anchoredPosition;
            _layoutFloatPanelRT.sizeDelta = new Vector2(panelWRef * s, panelHRef * s);
            _layoutFloatPanelRT.anchoredPosition = keepTopLeft;

            if (_layoutFloatTitleBarRT != null)
            {
                _layoutFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
                UI.LayoutFloatTitleWindowIcon(
                    _layoutFloatTitleBarRT.gameObject,
                    GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                HorizontalLayoutGroup titleHlg = _layoutFloatTitleBarRT.GetComponent<HorizontalLayoutGroup>();
                Transform gripTr = _layoutFloatTitleBarRT.Find("Grip");
                UI.ApplyFloatTitleBarMetrics(
                    titleHlg, gripTr != null ? gripTr.gameObject : null, s);
                if (gripTr != null)
                {
                    Text grip = gripTr.GetComponent<Text>();
                    if (grip != null)
                        GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                }
            }
            if (_layoutFloatTitleText != null)
                GalleryUiMetrics.ApplyFont(_layoutFloatTitleText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ScaleFloatChromeIconButton(_layoutFloatCloseBtn, chromeSz, s);

            if (_layoutFloatSearchRow != null)
            {
                RectTransform searchRT = _layoutFloatSearchRow.GetComponent<RectTransform>();
                if (searchRT != null)
                {
                    searchRT.sizeDelta = new Vector2(0f, searchRowH);
                    searchRT.anchoredPosition = new Vector2(0f, -titleH);
                }
                HorizontalLayoutGroup searchHlg = _layoutFloatSearchRow.GetComponent<HorizontalLayoutGroup>();
                if (searchHlg != null)
                {
                    searchHlg.spacing = 6f * s;
                    searchHlg.padding = UI.Pad(
                        GalleryUiDesignTokens.FloatSearchRowPadRef, GalleryUiDesignTokens.FloatSearchRowPadRef,
                        GalleryUiDesignTokens.FloatSearchRowPadRef, GalleryUiDesignTokens.FloatSearchRowPadRef, s);
                }
            }
            if (_layoutFloatSearchInput != null)
            {
                LayoutElement inputLe = _layoutFloatSearchInput.GetComponent<LayoutElement>();
                if (inputLe != null)
                {
                    inputLe.minHeight = searchH;
                    inputLe.preferredHeight = searchH;
                }
                UI.LayoutChromeSearchIcon(_layoutFloatSearchInput.gameObject, s);
                if (_layoutFloatSearchInput.textComponent != null)
                    GalleryUiMetrics.ApplyFont(_layoutFloatSearchInput.textComponent, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                Text ph = _layoutFloatSearchInput.placeholder as Text;
                if (ph != null)
                    GalleryUiMetrics.ApplyFont(ph, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            if (_layoutFloatFooter != null)
            {
                RectTransform footerRT = _layoutFloatFooter.GetComponent<RectTransform>();
                if (footerRT != null)
                    footerRT.sizeDelta = new Vector2(0f, footerH);
                HorizontalLayoutGroup footerHlg = _layoutFloatFooter.GetComponent<HorizontalLayoutGroup>();
                if (footerHlg != null)
                {
                    footerHlg.spacing = 6f * s;
                    footerHlg.padding = UI.PadFloatFooter(s);
                }
            }
            if (_layoutFloatSaveBtn != null)
            {
                LayoutElement saveLe = _layoutFloatSaveBtn.GetComponent<LayoutElement>();
                if (saveLe != null)
                {
                    saveLe.minWidth = saveLe.preferredWidth = 150f * s;
                    saveLe.minHeight = saveLe.preferredHeight = chromeSz;
                }
                Text saveTxt = _layoutFloatSaveBtn.GetComponentInChildren<Text>();
                if (saveTxt != null)
                    GalleryUiMetrics.ApplyFont(saveTxt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            UI.ScaleFloatChromeIconButton(_layoutFloatImportBtn, chromeSz, s);
            UI.ScaleFloatChromeIconButton(_layoutFloatResizeHandle, chromeSz, s);

            if (_layoutFloatScrollHost != null)
            {
                RectTransform hostRT = _layoutFloatScrollHost.GetComponent<RectTransform>();
                if (hostRT != null)
                {
                    hostRT.offsetMin = new Vector2(0f, footerH);
                    hostRT.offsetMax = new Vector2(0f, -(titleH + searchRowH));
                }
            }
            if (_layoutFloatEmptyText != null)
                GalleryUiMetrics.ApplyFont(_layoutFloatEmptyText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);

            if (_layoutFloatContentRT != null)
            {
                VerticalLayoutGroup cv = _layoutFloatContentRT.GetComponent<VerticalLayoutGroup>();
                if (cv != null)
                {
                    cv.spacing = LayoutPresetRowGapRef * s;
                    cv.padding = UI.PadPopup(s);
                }
            }

            DestroyLayoutRowPool();
            CloseLayoutPresetRowMenu();
            RefreshLayoutPresetsList(true);
            CaptureLayoutFloatGeometryToMemory();
            PersistLayoutFloatGeometry();
        }

        private void DestroyLayoutRowPool()
        {
            for (int i = 0; i < _layoutFloatRowPool.Count; i++)
            {
                GameObject row = _layoutFloatRowPool[i];
                if (row != null) UnityEngine.Object.Destroy(row);
            }
            _layoutFloatRowPool.Clear();
            _layoutFloatWindowStart = -1;
        }

        /// <summary>Esc: menu → rename/delete → close window. Overlay tick + pane Update share this.</summary>
        internal bool TryHandleLayoutPresetsFloatKeyboard()
        {
            if (!IsLayoutPresetsFloatOpen()) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;

            if (_layoutRowMenuGO != null && _layoutRowMenuGO.activeSelf)
            {
                CloseLayoutPresetRowMenu();
                return true;
            }
            if (_layoutRenamingId != 0)
            {
                CancelLayoutPresetRename();
                return true;
            }
            if (_layoutDeletingId != 0)
            {
                CancelLayoutPresetDelete();
                return true;
            }
            HideLayoutPresetsFloat();
            return true;
        }

        internal void TeardownLayoutPresetsFloat()
        {
            CloseLayoutPresetRowMenu();
            DestroyLayoutRowPool();
            if (_layoutFloatRoot != null)
            {
                try { UnityEngine.Object.Destroy(_layoutFloatRoot); } catch { }
                _layoutFloatRoot = null;
            }
            if (s_layoutFloatOwner == this)
            {
                s_layoutFloatOwner = null;
                if (s_layoutOverlayGO != null) s_layoutOverlayGO.SetActive(false);
            }
            SyncAllLayoutPresetToggleStates();
        }
    }

    /// <summary>Esc + live scale while the manager sits on a canvas that outlives a hidden pane.</summary>
    internal sealed class LayoutPresetsOverlayTick : MonoBehaviour
    {
        private void Update()
        {
            try { GalleryPanel.TickLayoutPresetsOverlay(); } catch { }
        }
    }
}
