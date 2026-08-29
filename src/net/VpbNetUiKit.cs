using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    // Layout-group chrome for net windows. Fills opaque — these sit over a lit 3D scene.
    public static class VpbNetUiKit
    {
        // One step up from gallery chrome — burst use over a lit scene, not a dense daily grid.
        public const int FontCaption = 16;
        public const int FontBody = 18;
        public const int FontTitle = 20;
        public const int FontDisplay = 24;
        public const int FontMin = 13;

        public const float ButtonRef = 40f;
        public const float RowRef = ButtonRef;
        public const float LineRef = 24f;
        public const float PadRef = GalleryUiDesignTokens.GroupGapRef;
        public const float GapRef = GalleryUiDesignTokens.ControlGapRef;
        public const float StackGapRef = GalleryUiDesignTokens.ControlGapRef;
        public const float SectionGapRef = GalleryUiDesignTokens.RegionGapRef;
        public const float CardPadRef = GalleryUiDesignTokens.GroupGapRef;
        // Inset must clear the 10px corner radius or labels glue to the pill edge.
        public const float ChipInsetRef = GalleryUiDesignTokens.GroupGapRef;
        public const float ChipPadVRef = GalleryUiDesignTokens.TightGapRef;
        public const float IconRef = 22f;
        public const float IconPadRef = GalleryUiDesignTokens.ControlGapRef;
        public const float TitleBarPadHRef = GalleryUiDesignTokens.ControlGapRef;
        public const float TitleBarPadVRef = GalleryUiDesignTokens.TightGapRef;
        public const float TitleBarRef = ButtonRef + TitleBarPadVRef * 2f;
        public const float CornerRef = 10f;
        public const float ChipRef = ButtonRef;
        public const float DotRef = 12f;
        public const float TitleDotRef = 16f;
        public const float CountColRef = 18f;
        public const float ScaleEpsilon = 0.02f;
        public const float FooterBarRef = ButtonRef + TitleBarPadVRef * 2f;
        public const float TipMaxRef = 360f;
        public const float TipPadRef = GalleryUiDesignTokens.GroupGapRef;
        public const float TipMinWRef = 96f;

        // VR: ride gallery pane canvas. Own WorldSpace canvas breaks VaM raycasts.
        const float HostProbeSeconds = 0.25f;

        static readonly List<Shell> _shells = new List<Shell>(8);

        public sealed class Shell
        {
            public GameObject Root;
            public GameObject Panel;
            public RectTransform PanelRT;
            public GameObject TitleBar;
            public GameObject Body;
            public GameObject Footer;
            public Text Title;
            /// <summary>Desktop overlay only. Null in VR.</summary>
            public Canvas Canvas;
            public GraphicRaycaster Raycaster;
            public RectTransform RootRT;
            public int ScreenSortingOrder;
            /// <summary>VR pane-local coords. Do not persist desktop screen coords here.</summary>
            public bool WorldSpace;
            public bool AddedToSuperController;
            /// <summary>Fit may run this frame; owner fills the body after BuildWindow.</summary>
            public int FitFrame;
            /// <summary>Pane canvas GO; null while waiting.</summary>
            public GameObject PaneHost;
            /// <summary>Settings-float anchors already applied.</summary>
            public bool PaneLaidOut;
            /// <summary>First canvas fit done. Later remounts must not yank a placed window.</summary>
            public bool CanvasFitDone;
            /// <summary>VR local pos from last drag/hide. Desktop persist is the owner callback.</summary>
            public bool HasHeldPos;
            public Vector2 HeldPos;
            public float NextHostProbe;
        }

        public sealed class TipLayer
        {
            public GameObject Go;
            public RectTransform RT;
            public Text Label;
            public LayoutElement LabelLE;
            public GameObject Owner;
            public float Scale = 1f;
        }

        // Button plus the fields a refresh writes — avoid GetComponent every quarter second.
        public sealed class Chip
        {
            public GameObject Go;
            public Image Fill;
            public Image Icon;
            public Text Label;
            public Button Button;
            public LayoutElement GoLE;
            public Color IdleFill = UI.ChromePanel;
            public Color IdleText = UI.TextPrimary;
            public float Scale = 1f;
            public float WidthRef;
            public float HeightRef;

            public void SetText(string s)
            {
                if (Label == null || s == null) return;
                if (string.Equals(Label.text, s, StringComparison.Ordinal)) return;
                Label.text = s;
                RelayoutChip(this);
            }

            public void SetIcon(string role)
            {
                if (Icon == null || string.IsNullOrEmpty(role)) return;
                Sprite spr = UI.LoadIconSprite(role, UI.BarIconGlyphTint);
                if (spr == null) return;
                UI.SetIconSprite(Icon, spr);
                Icon.color = Label != null ? Label.color : IdleText;
            }

            public void SetTone(Color fill, Color text)
            {
                if (Fill != null) Fill.color = fill;
                if (Label != null) Label.color = text;
                if (Icon != null) Icon.color = text;
            }

            public void SetRole(Color fill, Color text)
            {
                IdleFill = fill;
                IdleText = text;
                bool on = Button == null || Button.interactable;
                SetTone(on ? fill : UI.ChromeDark, on ? text : UI.TextDim);
            }

            public void SetEnabled(bool on)
            {
                if (Button != null) Button.interactable = on;
                SetTone(on ? IdleFill : UI.ChromeDark, on ? IdleText : UI.TextDim);
            }

            public void SetActive(bool on)
            {
                if (Go != null && Go.activeSelf != on) Go.SetActive(on);
            }
        }

        public static float Scale()
        {
            float s = 1f;
            try { s = GalleryUiMetrics.Resolve(true).ChromeScale; }
            catch { }
            return s > 0f ? s : 1f;
        }

        public static int Font(int designPt, float scale)
        {
            return GalleryUiMetrics.ScaledFontSize(designPt, scale, FontMin);
        }

        public static Shell BuildWindow(string hostName, string title, float widthRef, float scale,
            int sortingOrder, Vector2 anchoredPos, Action onMoved)
        {
            return BuildWindow(hostName, title, widthRef, 0f, scale, sortingOrder, anchoredPos, onMoved);
        }

        public static Shell BuildWindow(string hostName, string title, float widthRef, float heightRef, float scale,
            int sortingOrder, Vector2 anchoredPos, Action onMoved)
        {
            float s = scale;
            Shell sh = new Shell();

            sh.Root = new GameObject(hostName);
            sh.Root.layer = 5;
            sh.RootRT = sh.Root.AddComponent<RectTransform>();
            sh.ScreenSortingOrder = sortingOrder;

            HostWindow(sh);
            _shells.Add(sh);

            float panelH = heightRef > 0f ? heightRef * s : TitleBarRef * s;
            sh.Panel = UI.AddChildGOImage(sh.Root, UI.ChromeDarker, AnchorPresets.topLeft,
                widthRef * s, panelH, sh.WorldSpace ? Vector2.zero : anchoredPos, true);
            sh.Panel.name = "Panel";
            sh.PanelRT = sh.Panel.GetComponent<RectTransform>();

            RoundedRect rr = sh.Panel.GetComponent<RoundedRect>();
            if (rr != null)
            {
                rr.excludeFromGlobalRadiusSync = true;
                rr.cornerRadiusFraction = 0f;
                rr.cornerRadius = CornerRef * s;
            }

            UI.AddVLG(sh.Panel, 0f, null, TextAnchor.UpperLeft, true, true, true, false);
            if (heightRef <= 0f)
            {
                ContentSizeFitter fit = sh.Panel.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            sh.TitleBar = UI.CreateChildRT(sh.Panel, "TitleBar");
            UI.AddImage(sh.TitleBar, UI.ChromePanel);
            UI.AddHLG(sh.TitleBar, UI.Gap(GalleryUiDesignTokens.ControlGapRef, s),
                UI.Pad(TitleBarPadHRef, TitleBarPadHRef, TitleBarPadVRef, TitleBarPadVRef, s),
                TextAnchor.MiddleLeft, true, true, false, false);
            UI.AddLE(sh.TitleBar, minHeight: TitleBarRef * s, preferredHeight: TitleBarRef * s,
                flexibleHeight: 0f);

            GameObject grip = UI.CreateChildRT(sh.TitleBar, "Grip");
            UI.AddImage(grip, UI.ChromePanel);
            UI.AddLE(grip, minWidth: 0f, flexibleWidth: 1f,
                minHeight: TitleBarRef * s, preferredHeight: TitleBarRef * s, flexibleHeight: 0f);
            UIFloatPanelDrag drag = grip.AddComponent<UIFloatPanelDrag>();
            drag.Target = sh.PanelRT;
            drag.OnMoved = MoveCallback(sh, onMoved);

            sh.Title = UI.CreateLabel(grip, title, FontTitle, UI.TextPrimary,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "Title");
            GalleryUiMetrics.ApplyFont(sh.Title, FontTitle, s, FontMin);
            FitLabel(sh.Title, Font(FontTitle, s));

            sh.Body = UI.CreateChildRT(sh.Panel, "Body");
            UI.AddVLG(sh.Body, UI.Gap(StackGapRef, s), UI.Pad(PadRef, PadRef, PadRef, PadRef, s),
                TextAnchor.UpperLeft, true, true, true, false);
            if (heightRef > 0f)
                UI.AddLE(sh.Body, minWidth: 0f, flexibleWidth: 1f, minHeight: 0f, flexibleHeight: 1f);

            if (sh.WorldSpace)
                ApplyPaneFloatPanel(sh);
            return sh;
        }

        // Persist screen coords only. VR local pos is remembered across hide/reopen.
        static Action MoveCallback(Shell sh, Action onMoved)
        {
            return delegate
            {
                RememberPos(sh);
                if (sh != null && sh.WorldSpace) return;
                if (onMoved != null) onMoved();
            };
        }

        static void RememberPos(Shell sh)
        {
            if (sh == null || sh.PanelRT == null) return;
            sh.HeldPos = sh.PanelRT.anchoredPosition;
            sh.HasHeldPos = true;
        }

        static void RestorePos(Shell sh)
        {
            if (sh == null || sh.PanelRT == null || !sh.HasHeldPos) return;
            sh.PanelRT.anchoredPosition = sh.HeldPos;
        }

        /// <summary>Desktop overlay canvas. VR rides the pane.</summary>
        static void HostWindow(Shell sh)
        {
            bool vr = false;
            try { vr = XrUtils.IsVrActive(); }
            catch { vr = false; }

            sh.WorldSpace = vr;
            if (!vr)
            {
                HostDesktop(sh);
                return;
            }

            StripOwnCanvas(sh);
            Rehome(sh, ResolvePaneHost());
        }

        static void HostDesktop(Shell sh)
        {
            sh.PaneHost = null;
            sh.PaneLaidOut = false;
            Transform tf = sh.Root.transform;
            if (tf.parent != null)
                tf.SetParent(null, false);
            if (!sh.Root.activeSelf)
                sh.Root.SetActive(true);

            EnsureDesktopCanvas(sh);
            sh.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            sh.Canvas.sortingOrder = sh.ScreenSortingOrder;
            sh.Canvas.worldCamera = null;
            sh.Root.layer = 5;

            RectTransform rt = sh.RootRT;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            tf.localPosition = Vector3.zero;
            tf.localRotation = Quaternion.identity;
            tf.localScale = Vector3.one;
        }

        static void EnsureDesktopCanvas(Shell sh)
        {
            if (sh.Canvas == null)
                sh.Canvas = sh.Root.AddComponent<Canvas>();
            sh.Canvas.enabled = true;
            if (sh.Raycaster == null)
                sh.Raycaster = sh.Root.AddComponent<GraphicRaycaster>();
            sh.Raycaster.enabled = true;
            CanvasScaler scaler = sh.Root.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = sh.Root.AddComponent<CanvasScaler>();
            // ConstantPixelSize: ChromeScale alone. ScaleWithScreenSize double-scaled vs gallery.
            scaler.dynamicPixelsPerUnit = 4;
        }

        static void StripOwnCanvas(Shell sh)
        {
            RemoveFromSuperController(sh);
            if (sh.Raycaster != null)
            {
                sh.Raycaster.enabled = false;
                UnityEngine.Object.Destroy(sh.Raycaster);
                sh.Raycaster = null;
            }
            if (sh.Canvas != null)
            {
                sh.Canvas.enabled = false;
                UnityEngine.Object.Destroy(sh.Canvas);
                sh.Canvas = null;
            }
            if (sh.Root == null) return;
            CanvasScaler scaler = sh.Root.GetComponent<CanvasScaler>();
            if (scaler != null)
                UnityEngine.Object.Destroy(scaler);
        }

        static void Rehome(Shell sh, GameObject pane)
        {
            if (pane != null) RidePane(sh, pane);
            else WaitForPane(sh);
        }

        /// <summary>Child of pane canvas. Nested Canvas even disabled offsets the VR plane.</summary>
        static void RidePane(Shell sh, GameObject pane)
        {
            sh.PaneHost = pane;
            if (!sh.Root.activeSelf)
                sh.Root.SetActive(true);

            Transform tf = sh.Root.transform;
            tf.SetParent(pane.transform, false);
            tf.localPosition = Vector3.zero;
            tf.localRotation = Quaternion.identity;
            tf.localScale = Vector3.one;
            SetLayerRecursive(sh.Root, pane.layer);

            RectTransform rt = sh.RootRT;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;

            // Within one canvas, sibling order is render order.
            tf.SetAsLastSibling();
            sh.FitFrame = Time.frameCount + 2;
        }

        /// <summary>No pane yet: stay hidden.</summary>
        static void WaitForPane(Shell sh)
        {
            RememberPos(sh);
            sh.PaneHost = null;
            if (sh.Root.transform.parent != null)
                sh.Root.transform.SetParent(null, false);
            if (sh.Root.activeSelf)
                sh.Root.SetActive(false);
        }

        /// <summary>Hidden gallery still owns the canvas; do not unparent+Fit on Show.</summary>
        static bool StillOnHost(Shell sh)
        {
            if (sh == null || sh.Root == null || sh.PaneHost == null) return false;
            Transform p = sh.Root.transform.parent;
            if (p == null) return false;
            return p.gameObject == sh.PaneHost;
        }

        /// <summary>Center anchors, top-left pivot. Desktop screen coords make VR drag jump.</summary>
        static void ApplyPaneFloatPanel(Shell sh)
        {
            RectTransform rt = sh.PanelRT;
            if (rt == null) return;
            sh.PaneLaidOut = true;
            Vector2 size = rt.sizeDelta;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            // Auto-height windows only know the title bar yet — pin the top and let the body grow down.
            float y = size.y * 0.5f;
            if (y < 80f) y = size.x * 0.45f;
            rt.anchoredPosition = new Vector2(-size.x * 0.5f, y);
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        /// <summary>Visible pane the player is facing — never spawn one.</summary>
        static GameObject ResolvePaneHost()
        {
            try
            {
                Gallery g = Gallery.singleton;
                if (g == null) return null;
                List<GalleryPanel> panes = g.Panels;
                if (panes == null) return null;

                Transform camTf = PlayerCamera();
                GameObject best = null;
                float bestFacing = float.NegativeInfinity;
                for (int i = 0; i < panes.Count; i++)
                {
                    GalleryPanel p = panes[i];
                    if (p == null || !p.IsVisible) continue;
                    GameObject go = PaneCanvasGo(p);
                    if (go == null) continue;
                    if (camTf == null) return go;
                    Vector3 to = go.transform.position - camTf.position;
                    if (to.sqrMagnitude < 0.0001f)
                    {
                        if (best == null) best = go;
                        continue;
                    }
                    float facing = Vector3.Dot(to.normalized, camTf.forward);
                    if (facing <= bestFacing) continue;
                    bestFacing = facing;
                    best = go;
                }
                if (best != null) return best;
                for (int i = 0; i < panes.Count; i++)
                {
                    GalleryPanel p = panes[i];
                    if (p == null || !p.HasLiveCanvas) continue;
                    GameObject go = PaneCanvasGo(p);
                    if (go != null) return go;
                }
                return null;
            }
            catch { return null; }
        }

        static GameObject PaneCanvasGo(GalleryPanel p)
        {
            Canvas c = p.canvas;
            if (c == null || !c.isActiveAndEnabled) return null;
            return c.gameObject;
        }

        static Transform PlayerCamera()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null && sc.centerCameraTarget != null) return sc.centerCameraTarget.transform;
            }
            catch { }
            try { if (Camera.main != null) return Camera.main.transform; }
            catch { }
            return null;
        }

        /// <summary>Follow whichever pane is on screen now.</summary>
        static void RehomeIfNeeded(Shell sh)
        {
            float now = Time.realtimeSinceStartup;
            if (now < sh.NextHostProbe) return;
            sh.NextHostProbe = now + HostProbeSeconds;

            bool vr = false;
            try { vr = XrUtils.IsVrActive(); }
            catch { vr = false; }

            if (vr != sh.WorldSpace)
            {
                HostWindow(sh);
                if (sh.WorldSpace && sh.PanelRT != null && !sh.PaneLaidOut)
                    ApplyPaneFloatPanel(sh);
                return;
            }
            if (!vr) return;

            GameObject pane = ResolvePaneHost();
            if (pane == null)
            {
                if (StillOnHost(sh)) return;
                if (sh.PaneHost != null || sh.Root.activeSelf)
                    WaitForPane(sh);
                return;
            }
            if (sh.PaneHost == pane && sh.Root.transform.parent == pane.transform && sh.Root.activeSelf)
                return;
            RidePane(sh, pane);
            if (sh.PanelRT != null && !sh.PaneLaidOut)
                ApplyPaneFloatPanel(sh);
            else
                RestorePos(sh);
        }

        /// <summary>Ridden pane was destroyed; owner rebuilds next poll.</summary>
        public static bool Lost(Shell sh)
        {
            return sh != null && sh.Root == null;
        }

        /// <summary>Keep panel on the pane board.</summary>
        static void FitPanelToCanvas(Shell sh)
        {
            if (sh == null || sh.Root == null || sh.PanelRT == null) return;
            RectTransform rootRT = sh.RootRT;
            if (rootRT == null) return;

            Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(rootRT, sh.PanelRT);
            if (b.size.x <= 0f && b.size.y <= 0f) return;

            Rect r = rootRT.rect;
            float dx = 0f;
            float dy = 0f;
            if (b.max.x > r.xMax) dx = r.xMax - b.max.x;
            if (b.min.x + dx < r.xMin) dx = r.xMin - b.min.x;
            if (b.min.y < r.yMin) dy = r.yMin - b.min.y;
            if (b.max.y + dy > r.yMax) dy = r.yMax - b.max.y;
            sh.CanvasFitDone = true;
            if (dx == 0f && dy == 0f) return;

            Vector2 p = sh.PanelRT.anchoredPosition;
            sh.PanelRT.anchoredPosition = new Vector2(p.x + dx, p.y + dy);
        }

        static void RemoveFromSuperController(Shell sh)
        {
            if (sh == null || !sh.AddedToSuperController) return;
            sh.AddedToSuperController = false;
            // ReferenceEquals, not ==: an already-destroyed canvas still has to leave VaM's list.
            if (ReferenceEquals(sh.Canvas, null)) return;
            try
            {
                if (SuperController.singleton != null) SuperController.singleton.RemoveCanvas(sh.Canvas);
            }
            catch { }
        }

        /// <summary>Per-frame for live windows.</summary>
        public static void TickShells()
        {
            for (int i = _shells.Count - 1; i >= 0; i--)
            {
                Shell sh = _shells[i];
                if (sh == null || sh.Root == null)
                {
                    if (sh != null) RemoveFromSuperController(sh);
                    _shells.RemoveAt(i);
                    continue;
                }
                RehomeIfNeeded(sh);
                if (sh.FitFrame == 0 || Time.frameCount < sh.FitFrame) continue;
                sh.FitFrame = 0;
                if (sh.WorldSpace && sh.Root != null)
                    SetLayerRecursive(sh.Root, sh.Root.layer);
                if (sh.WorldSpace && (sh.HasHeldPos || sh.CanvasFitDone))
                {
                    RestorePos(sh);
                    continue;
                }
                FitPanelToCanvas(sh);
            }
        }

        public static GameObject AttachFooter(Shell sh, float scale, Action onMoved)
        {
            float s = scale;
            GameObject footer = UI.CreateChildRT(sh.Panel, "Footer");
            UI.AddImage(footer, UI.ChromePanel);
            UI.AddHLG(footer, UI.GapTight(s), UI.PadFloatFooter(s),
                TextAnchor.MiddleLeft, true, true, false, false);
            UI.AddLE(footer, minHeight: FooterBarRef * s, preferredHeight: FooterBarRef * s,
                flexibleHeight: 0f);
            if (footer.GetComponent<RectMask2D>() == null)
                footer.AddComponent<RectMask2D>();

            GameObject dragArea = UI.CreateFloatFooterDragArea(footer);
            if (dragArea != null)
            {
                UIFloatPanelDrag drag = dragArea.AddComponent<UIFloatPanelDrag>();
                drag.Target = sh.PanelRT;
                drag.OnMoved = MoveCallback(sh, onMoved);
            }

            sh.Footer = footer;
            return footer;
        }

        /// <summary>Drag strip behind the footer row, never over it.</summary>
        public static void MakeDragBar(Shell sh, GameObject bar, Action onMoved)
        {
            if (sh == null || bar == null || sh.PanelRT == null) return;
            GameObject area = UI.CreateFloatFooterDragArea(bar);
            if (area == null) return;
            UIFloatPanelDrag drag = area.AddComponent<UIFloatPanelDrag>();
            drag.Target = sh.PanelRT;
            drag.OnMoved = MoveCallback(sh, onMoved);
        }

        public static void AttachResize(GameObject footer, Shell sh, float scale,
            Func<Vector2> getMin, Func<Vector2> getMax, Action onResized)
        {
            if (footer == null || sh == null) return;
            float s = scale;
            float chrome = ChipRef * s;
            GameObject handle = UI.AddChildGOImage(footer, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                chrome, chrome, Vector2.zero, true);
            handle.name = "ResizeHandle";
            Image rhImg = handle.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            UI.EnsureFloatChromeHoverBorder(handle);
            LayoutElement rhLe = handle.GetComponent<LayoutElement>();
            if (rhLe == null) rhLe = handle.AddComponent<LayoutElement>();
            rhLe.minWidth = rhLe.preferredWidth = chrome;
            rhLe.minHeight = rhLe.preferredHeight = chrome;
            rhLe.flexibleWidth = 0f;
            try
            {
                Sprite rhSpr = UI.LoadIconSprite("chevrons-down-right", UI.BarIconGlyphTint);
                if (rhSpr != null)
                    UI.AddIconToButton(handle, rhSpr, IconPadRef * s, UI.IconButtonBackdrop);
            }
            catch { }

            UIFloatPanelResize resizer = handle.AddComponent<UIFloatPanelResize>();
            resizer.Target = sh.PanelRT;
            resizer.GetMinSize = getMin;
            resizer.GetMaxSize = getMax;
            if (onResized != null) resizer.OnResized = onResized;
        }

        public static TipLayer MakeTip(GameObject root, float scale)
        {
            TipLayer t = new TipLayer();
            float s = scale;
            t.Scale = s > 0f ? s : 1f;
            t.Go = UI.AddChildGOImage(root, UI.ChromePanel, AnchorPresets.middleCenter,
                TipMaxRef * s, LineRef * 2f * s, Vector2.zero, true);
            t.Go.name = "Tip";
            t.RT = t.Go.GetComponent<RectTransform>();
            t.RT.pivot = new Vector2(0f, 1f);
            Image bg = t.Go.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = false;

            RoundedRect rr = t.Go.GetComponent<RoundedRect>();
            if (rr != null)
            {
                rr.excludeFromGlobalRadiusSync = true;
                rr.cornerRadiusFraction = 0f;
                rr.cornerRadius = CornerRef * s;
            }

            Outline rim = t.Go.AddComponent<Outline>();
            rim.effectColor = new Color(1f, 1f, 1f, 0.14f);
            rim.effectDistance = new Vector2(1f, -1f);
            Shadow drop = t.Go.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.55f);
            drop.effectDistance = new Vector2(2f * s, -2f * s);

            UI.AddVLG(t.Go, 0f, UI.Pad(TipPadRef, TipPadRef, TipPadRef, TipPadRef, s),
                TextAnchor.UpperLeft, true, true, false, false);
            ContentSizeFitter fit = t.Go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text label = UI.CreateLabel(t.Go, string.Empty, FontBody, UI.TextPrimary,
                TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow,
                false, false, AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "TipText");
            GalleryUiMetrics.ApplyFont(label, FontBody, s, FontMin);
            // Unity 2018 lineSpacing is a factor of line height, not pixels — extras stretched the popover.
            label.lineSpacing = 1.2f;
            t.LabelLE = UI.AddLE(label.gameObject, minWidth: TipMinWRef * s,
                preferredWidth: TipMaxRef * s, flexibleWidth: 0f,
                minHeight: 0f, flexibleHeight: 0f);
            t.Label = label;

            t.Go.SetActive(false);
            return t;
        }

        public static void BindTip(GameObject target, string text, TipLayer tip)
        {
            if (target == null || tip == null || string.IsNullOrEmpty(text)) return;
            UIHoverDelegate del = target.GetComponent<UIHoverDelegate>();
            if (del == null) del = target.AddComponent<UIHoverDelegate>();
            string captured = text;
            if (del.TooltipHandler != null) del.OnHoverChange -= del.TooltipHandler;
            Action<bool> handler = enter =>
            {
                if (enter) ShowTip(tip, captured, target.transform as RectTransform);
                else HideTip(tip, target);
            };
            del.TooltipHandler = handler;
            del.OnHoverChange += handler;
        }

        public static void HideTip(TipLayer tip, GameObject owner)
        {
            if (tip == null) return;
            if (owner != null && tip.Owner != owner) return;
            tip.Owner = null;
            if (tip.Go != null && tip.Go.activeSelf) tip.Go.SetActive(false);
        }

        public static void HideTip(TipLayer tip)
        {
            HideTip(tip, tip != null ? tip.Owner : null);
        }

        static void ShowTip(TipLayer tip, string text, RectTransform from)
        {
            if (tip == null || tip.Go == null || tip.Label == null || from == null) return;
            if (string.IsNullOrEmpty(text)) return;
            tip.Owner = from.gameObject;
            if (!string.Equals(tip.Label.text, text, StringComparison.Ordinal))
                tip.Label.text = text;
            if (!tip.Go.activeSelf) tip.Go.SetActive(true);
            FitTipSize(tip);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tip.RT);

            RectTransform canvasRT = tip.Go.transform.parent as RectTransform;
            if (canvasRT == null) return;

            // WorldSpace (VR) needs the eye camera; a null camera only maps overlay canvases.
            Camera cam = TipCamera(tip);

            Vector3[] corners = new Vector3[4];
            from.GetWorldCorners(corners);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, cam, out local))
                return;

            float tipH = tip.RT.rect.height;
            float tipW = tip.RT.rect.width;
            float gap = GalleryUiDesignTokens.ControlGapRef * (tip.Scale > 0f ? tip.Scale : 1f);
            Vector2 pos = new Vector2(local.x, local.y - gap);
            Rect cr = canvasRT.rect;
            float minX = cr.xMin + gap;
            float maxX = cr.xMax - tipW - gap;
            if (maxX < minX) maxX = minX;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            float minY = cr.yMin + tipH + gap;
            if (pos.y < minY)
            {
                Vector2 screenTop = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
                Vector2 localTop;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenTop, cam, out localTop))
                    pos.y = localTop.y + tipH + gap;
            }
            tip.RT.anchoredPosition = pos;
        }

        static Camera TipCamera(TipLayer tip)
        {
            if (tip == null || tip.Go == null) return null;
            Canvas canvas = tip.Go.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        // Hug copy: wrap at TipMaxRef. Measure unwrapped so a two-line tip is not a 360px empty card.
        static void FitTipSize(TipLayer tip)
        {
            if (tip == null || tip.Label == null || tip.LabelLE == null) return;
            float s = tip.Scale > 0f ? tip.Scale : 1f;
            float minW = TipMinWRef * s;
            float maxW = TipMaxRef * s;

            tip.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            float raw = tip.Label.preferredWidth;
            tip.Label.horizontalOverflow = HorizontalWrapMode.Wrap;

            float w = raw;
            if (w > maxW) w = maxW;
            if (w < minW) w = minW;
            tip.LabelLE.minWidth = w;
            tip.LabelLE.preferredWidth = w;
            tip.LabelLE.flexibleWidth = 0f;
            tip.LabelLE.minHeight = 0f;
            tip.LabelLE.preferredHeight = -1f;
            tip.LabelLE.flexibleHeight = 0f;
        }

        // Head of the title bar — that strip is the whole window while collapsed.
        public static Image TitleDot(Shell sh, float scale, out Text count)
        {
            float s = scale;
            GameObject host = UI.CreateChildRT(sh.TitleBar, "Badge");
            // Vertical room around disc, minus TightGap — title-bar pad already sits to the left.
            float insetRef = (ChipRef - TitleDotRef) * 0.5f - GalleryUiDesignTokens.TightGapRef;
            if (insetRef < 0f) insetRef = 0f;
            UI.AddHLG(host, UI.Gap(GapRef, s), UI.Pad(insetRef, 0f, 0f, 0f, s),
                TextAnchor.MiddleLeft, true, true, false, false);
            UI.AddLE(host, minHeight: ChipRef * s, preferredHeight: ChipRef * s,
                flexibleHeight: 0f, flexibleWidth: 0f);

            GameObject dgo = UI.CreateChildRT(host, "Dot");
            Image dot = UI.AddGalleryElementRoundedBg(dgo, UI.TextDim, false);
            RoundedRect rr = dot as RoundedRect;
            if (rr != null)
            {
                rr.excludeFromGlobalRadiusSync = true;
                rr.cornerRadiusFraction = 0.5f;
            }
            float d = TitleDotRef * s;
            UI.AddLE(dgo, minWidth: d, preferredWidth: d, flexibleWidth: 0f,
                minHeight: d, preferredHeight: d, flexibleHeight: 0f);

            count = Line(host, string.Empty, FontBody, UI.TextPrimary, ChipRef, s, false);
            count.alignment = TextAnchor.MiddleLeft;
            FixWidth(count.gameObject, CountColRef, s);

            host.transform.SetAsFirstSibling();
            return dot;
        }

        public static Chip TitleChip(Shell sh, string label, float scale, UnityAction onClick)
        {
            Chip c = Btn(sh.TitleBar, label, ChipRef, ChipRef, scale,
                Font(FontBody, scale), onClick);
            return c;
        }

        public static Chip TitleTextChip(Shell sh, string label, float widthRef, float scale, UnityAction onClick)
        {
            return Btn(sh.TitleBar, label, widthRef, ChipRef, scale,
                Font(FontBody, scale), onClick);
        }

        // Icon-only: VR lasers cannot hover, so collapse/close keep familiar window glyphs.
        public static Chip TitleIconChip(Shell sh, string iconRole, float scale, UnityAction onClick)
        {
            Chip c = TitleChip(sh, string.Empty, scale, onClick);
            BindTitleIcon(c, iconRole, scale);
            return c;
        }

        public static Chip TitleIconTextChip(Shell sh, string iconRole, string label, float widthRef,
            float scale, UnityAction onClick)
        {
            Chip c = TitleTextChip(sh, label, widthRef, scale, onClick);
            ApplyChipIcon(c, iconRole, scale);
            return c;
        }

        public static void BindTitleIcon(Chip c, string iconRole, float scale)
        {
            if (c == null || c.Go == null || string.IsNullOrEmpty(iconRole)) return;
            try { UI.ApplyBarIconFromPath(c.Go, iconRole, IconPadRef * scale, UI.ChromePanel); }
            catch { return; }
            Transform t = c.Go.transform.Find("Icon");
            if (t != null) c.Icon = t.GetComponent<Image>();
        }

        // Headers carry grouping; boxing every rule group reads as a form to fill in.
        public static Text SectionHeader(GameObject parent, string text, float scale, bool first)
        {
            if (!first) Spacer(parent, SectionGapRef - StackGapRef, scale);

            Text t = UI.CreateLabel(parent, text, FontBody, UI.TextMuted,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "SectionHeader");
            GalleryUiMetrics.ApplyFont(t, FontBody, scale, FontMin);
            UI.AddLE(t.gameObject, minHeight: LineRef * scale, preferredHeight: LineRef * scale,
                flexibleHeight: 0f);
            Rule(parent, scale);
            return t;
        }

        public static void Rule(GameObject parent, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, "Rule");
            UI.AddImage(go, UI.ChromeMid, false);
            float h = Mathf.Max(1f, Mathf.Round(scale));
            UI.AddLE(go, minHeight: h, preferredHeight: h, flexibleHeight: 0f);
        }

        public static void Spacer(GameObject parent, float heightRef, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, "Spacer");
            UI.AddLE(go, minHeight: heightRef * scale, preferredHeight: heightRef * scale,
                flexibleHeight: 0f);
        }

        public static bool ScaleDrifted(float applied)
        {
            return Mathf.Abs(Scale() - applied) >= ScaleEpsilon;
        }

        public static GameObject Row(GameObject parent, float heightRef, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, "Row");
            UI.AddHLG(go, UI.Gap(GapRef, scale), null, TextAnchor.MiddleLeft, true, true, false, false);
            UI.AddLE(go, minHeight: heightRef * scale, preferredHeight: heightRef * scale,
                flexibleHeight: 0f);
            return go;
        }

        // Chips hug copy then wrap; FitWrapRow grows the cell to the widest so Spectate is not crushed.
        public static GameObject WrapRow(GameObject parent, float cellWRef, float cellHRef, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, "Wrap");
            GridLayoutGroup grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellWRef * scale, cellHRef * scale);
            float g = UI.Gap(GapRef, scale);
            grid.spacing = new Vector2(g, g);
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UI.AddLE(go, minWidth: 0f, flexibleWidth: 1f, flexibleHeight: 0f);
            return go;
        }

        // Two equal columns sized to the taller card. Stretching height left empty bands.
        public static GameObject Split(GameObject parent, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, "Split");
            UI.AddHLG(go, UI.Gap(GapRef, scale), null, TextAnchor.UpperLeft, true, true, false, false);
            ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UI.AddLE(go, minWidth: 0f, flexibleWidth: 1f, flexibleHeight: 0f);
            return go;
        }

        public static Text Line(GameObject parent, string text, int designPt, Color color,
            float heightRef, float scale, bool wrap)
        {
            Text t = UI.CreateLabel(parent, text, designPt, color, wrap ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft,
                wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow,
                wrap ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate, false, false, AnchorPresets.stretchAll,
                Vector2.zero, Vector2.zero, "Line");
            GalleryUiMetrics.ApplyFont(t, designPt, scale, FontMin);
            int pt = Font(designPt, scale);
            if (wrap)
            {
                t.lineSpacing = 1.3f;
                UI.AddLE(t.gameObject, minHeight: heightRef * scale, flexibleHeight: 0f,
                    minWidth: 0f, flexibleWidth: 1f);
                ContentSizeFitter fit = t.gameObject.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                UI.AddLE(t.gameObject, minHeight: heightRef * scale, preferredHeight: heightRef * scale,
                    flexibleHeight: 0f, minWidth: 0f, flexibleWidth: 1f);
                FitLabel(t, pt);
            }
            return t;
        }

        // Unity 2018 Text has no ellipsis. BestFit keeps long names/codes inside the rect.
        public static void FitLabel(Text t, int maxPt)
        {
            if (t == null) return;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = FontMin;
            t.resizeTextMaxSize = maxPt > 0 ? maxPt : t.fontSize;
        }

        // Do not add a second LayoutElement — it keeps the first's flexibleWidth and undoes the column.
        public static void FixWidth(GameObject go, float widthRef, float scale)
        {
            if (go == null) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minWidth = widthRef * scale;
            le.preferredWidth = widthRef * scale;
            le.flexibleWidth = 0f;
        }

        public static Chip Btn(GameObject parent, string label, float widthRef, float heightRef,
            float scale, int fontPt, UnityAction onClick)
        {
            Chip c = new Chip();
            c.WidthRef = widthRef;
            c.HeightRef = heightRef;
            c.Scale = scale > 0f ? scale : 1f;
            c.Go = UI.CreateChromeLayoutButton(parent.transform,
                widthRef > 0f ? widthRef * scale : 0f, heightRef * scale,
                label, fontPt, UI.ChromePanel, onClick);
            c.Go.name = "Btn_" + label;
            c.Fill = c.Go.GetComponent<Image>();
            c.Label = c.Go.GetComponentInChildren<Text>();
            c.Button = c.Go.GetComponent<Button>();
            c.GoLE = c.Go.GetComponent<LayoutElement>();
            c.IdleFill = UI.ChromePanel;
            c.IdleText = UI.TextPrimary;
            if (c.Label != null) c.Label.color = UI.TextPrimary;
            PadChipLabel(c);
            return c;
        }

        public static Chip Btn(GameObject parent, string label, float widthRef, float scale,
            UnityAction onClick)
        {
            return Btn(parent, label, widthRef, ButtonRef, scale,
                Font(FontBody, scale), onClick);
        }

        public static Chip PrimaryBtn(GameObject parent, string label, float widthRef, float scale,
            UnityAction onClick)
        {
            Chip c = Btn(parent, label, widthRef, scale, onClick);
            c.SetRole(UI.AccentGreen, UI.TextPrimary);
            return c;
        }

        public static Chip DangerBtn(GameObject parent, string label, float widthRef, float scale,
            UnityAction onClick)
        {
            Chip c = Btn(parent, label, widthRef, scale, onClick);
            c.SetRole(UI.AccentRed, UI.TextPrimary);
            return c;
        }

        // Label stays visible. Icon-only is reserved for title-bar window commands.
        public static Chip IconBtn(GameObject parent, string iconRole, string label, float widthRef,
            float scale, UnityAction onClick)
        {
            return IconBtn(parent, iconRole, null, label, widthRef, scale, onClick);
        }

        public static Chip IconBtn(GameObject parent, string iconRole, string fallbackRole, string label,
            float widthRef, float scale, UnityAction onClick)
        {
            Chip c = Btn(parent, label, widthRef, scale, onClick);
            if (!ApplyChipIcon(c, iconRole, scale) && !string.IsNullOrEmpty(fallbackRole))
                ApplyChipIcon(c, fallbackRole, scale);
            return c;
        }

        public static Chip PrimaryIconBtn(GameObject parent, string iconRole, string label, float widthRef,
            float scale, UnityAction onClick)
        {
            return PrimaryIconBtn(parent, iconRole, null, label, widthRef, scale, onClick);
        }

        public static Chip PrimaryIconBtn(GameObject parent, string iconRole, string fallbackRole,
            string label, float widthRef, float scale, UnityAction onClick)
        {
            Chip c = IconBtn(parent, iconRole, fallbackRole, label, widthRef, scale, onClick);
            c.SetRole(UI.AccentGreen, UI.TextPrimary);
            return c;
        }

        public static Chip DangerIconBtn(GameObject parent, string iconRole, string label, float widthRef,
            float scale, UnityAction onClick)
        {
            Chip c = IconBtn(parent, iconRole, label, widthRef, scale, onClick);
            c.SetRole(UI.AccentRed, UI.TextPrimary);
            return c;
        }

        // Square chips: glyph edge-to-edge. Text chips pad so labels clear the rounded fill.
        static bool IsSquareChip(float widthRef, float heightRef)
        {
            if (widthRef <= 0f || heightRef <= 0f) return false;
            return Mathf.Abs(widthRef - heightRef) < 0.5f && widthRef <= ChipRef + 0.5f;
        }

        static void PadChipLabel(Chip c)
        {
            if (c == null || c.Go == null || c.Label == null || c.Scale <= 0f) return;
            if (IsSquareChip(c.WidthRef, c.HeightRef)) return;

            EnsureChipRow(c);
            PrepareChipLabel(c);
            RelayoutChip(c);
        }

        static void EnsureChipRow(Chip c)
        {
            float s = c.Scale;
            HorizontalLayoutGroup hlg = c.Go.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null)
            {
                hlg = UI.AddHLG(c.Go, UI.Gap(GapRef, s),
                    UI.Pad(ChipInsetRef, ChipInsetRef, ChipPadVRef, ChipPadVRef, s),
                    TextAnchor.MiddleCenter, true, true, false, true);
            }
            else
            {
                hlg.spacing = UI.Gap(GapRef, s);
                hlg.padding = UI.Pad(ChipInsetRef, ChipInsetRef, ChipPadVRef, ChipPadVRef, s);
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
            }
        }

        static void PrepareChipLabel(Chip c)
        {
            Text t = c.Label;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;

            RectTransform lrt = t.rectTransform;
            lrt.anchorMin = new Vector2(0.5f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = Vector2.zero;
        }

        static float MeasureChipText(Text t)
        {
            if (t == null || t.font == null || string.IsNullOrEmpty(t.text)) return 0f;
            TextGenerationSettings settings = t.GetGenerationSettings(new Vector2(4000f, 0f));
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.generateOutOfBounds = true;
            settings.resizeTextForBestFit = false;
            float ppu = t.pixelsPerUnit;
            if (ppu < 0.01f) ppu = 1f;
            float w = t.cachedTextGeneratorForLayout.GetPreferredWidth(t.text, settings) / ppu;
            if (w < 1f) w = t.preferredWidth;
            if (w < 0f) w = 0f;
            return Mathf.Ceil(w) + 2f;
        }

        public static void RelayoutChip(Chip c)
        {
            if (c == null || c.Go == null || c.Scale <= 0f) return;
            if (IsSquareChip(c.WidthRef, c.HeightRef)) return;

            float pad = ChipInsetRef * c.Scale;
            float gap = UI.Gap(GapRef, c.Scale);
            float iconW = 0f;
            if (c.Icon != null && c.Icon.gameObject.activeSelf)
                iconW = IconRef * c.Scale + gap;

            float textW = MeasureChipText(c.Label);
            if (c.Label != null)
            {
                c.Label.resizeTextForBestFit = false;
                c.Label.horizontalOverflow = HorizontalWrapMode.Overflow;

                LayoutElement tle = c.Label.GetComponent<LayoutElement>();
                if (tle == null) tle = c.Label.gameObject.AddComponent<LayoutElement>();
                tle.minWidth = textW;
                tle.preferredWidth = textW;
                tle.flexibleWidth = 0f;
                tle.minHeight = 0f;
                tle.flexibleHeight = 1f;
            }

            float content = pad * 2f + iconW + textW;
            float minW = c.WidthRef > 0f ? c.WidthRef * c.Scale : 0f;
            if (content < minW) content = minW;

            LayoutElement gle = c.GoLE != null ? c.GoLE : c.Go.GetComponent<LayoutElement>();
            if (gle == null) return;
            gle.minWidth = content;
            gle.preferredWidth = content;
            gle.flexibleWidth = 0f;
        }

        public static void FitWrapRow(GameObject wrap)
        {
            if (wrap == null) return;
            GridLayoutGroup grid = wrap.GetComponent<GridLayoutGroup>();
            if (grid == null) return;
            float maxW = grid.cellSize.x;
            float h = grid.cellSize.y;
            Transform t = wrap.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                LayoutElement le = t.GetChild(i).GetComponent<LayoutElement>();
                if (le == null) continue;
                float w = le.minWidth;
                if (le.preferredWidth > w) w = le.preferredWidth;
                if (w > maxW) maxW = w;
            }
            grid.cellSize = new Vector2(maxW, h);
        }

        public static bool ApplyChipIcon(Chip c, string iconRole, float scale)
        {
            if (c == null || c.Go == null || string.IsNullOrEmpty(iconRole) || scale <= 0f) return false;
            Sprite spr = UI.LoadIconSprite(iconRole, UI.BarIconGlyphTint);
            if (spr == null) return false;

            c.Scale = scale;
            EnsureChipRow(c);
            if (c.Label != null) PrepareChipLabel(c);

            float iconSz = IconRef * scale;
            if (c.Icon == null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(c.Go.transform, false);
                iconGO.transform.SetAsFirstSibling();
                RectTransform irt = iconGO.GetComponent<RectTransform>();
                if (irt == null) irt = iconGO.AddComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.5f, 0.5f);
                irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.sizeDelta = new Vector2(iconSz, iconSz);
                Image img = UI.AddImage(iconGO, Color.white, false);
                img.preserveAspect = true;
                img.raycastTarget = false;
                UI.AddLE(iconGO, minWidth: iconSz, minHeight: iconSz,
                    preferredWidth: iconSz, preferredHeight: iconSz,
                    flexibleWidth: 0f, flexibleHeight: 0f);
                c.Icon = img;
            }

            UI.SetIconSprite(c.Icon, spr);
            c.Icon.color = c.Label != null ? c.Label.color : c.IdleText;
            RelayoutChip(c);
            return true;
        }

        public static GameObject Card(GameObject parent, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, "Card");
            UI.AddImage(go, UI.ChromeDark, false);
            UI.AddVLG(go, UI.Gap(StackGapRef, scale),
                UI.Pad(CardPadRef, CardPadRef, CardPadRef, CardPadRef, scale),
                TextAnchor.UpperLeft, true, true, true, false);
            UI.AddLE(go, minWidth: 0f, flexibleWidth: 1f, minHeight: 0f, flexibleHeight: 0f);
            return go;
        }

        public static GameObject Pane(GameObject parent, string name, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, name);
            UI.AddVLG(go, UI.Gap(StackGapRef, scale), null,
                TextAnchor.UpperLeft, true, true, true, false);
            ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UI.AddLE(go, minWidth: 0f, flexibleWidth: 1f, flexibleHeight: 0f);
            return go;
        }

        public static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        public static InputField Field(GameObject parent, string placeholder, float heightRef, float scale)
        {
            GameObject go = UI.CreateChildRT(parent, "Field");
            UI.AddGalleryElementRoundedBg(go, UI.InputFieldBg);
            UI.AddLE(go, minHeight: heightRef * scale, preferredHeight: heightRef * scale,
                flexibleHeight: 0f, minWidth: 0f, flexibleWidth: 1f);

            InputField input = go.AddComponent<InputField>();
            float pad = ChipInsetRef * scale;
            int bodyPt = Font(FontBody, scale);

            Text t = UI.CreateLabel(go, string.Empty, bodyPt, UI.InputFieldTextColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "Text");
            GalleryUiMetrics.ApplyFont(t, FontBody, scale, FontMin);
            t.supportRichText = false;
            RectTransform trt = t.rectTransform;
            trt.offsetMin = new Vector2(pad, 0f);
            trt.offsetMax = new Vector2(-pad, 0f);

            Text ph = UI.CreateLabel(go, placeholder ?? string.Empty, bodyPt,
                UI.InputFieldPlaceholderColor, TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow,
                VerticalWrapMode.Truncate, false, false, AnchorPresets.stretchAll,
                Vector2.zero, Vector2.zero, "Placeholder");
            GalleryUiMetrics.ApplyFont(ph, FontBody, scale, FontMin);
            ph.fontStyle = FontStyle.Italic;
            RectTransform prt = ph.rectTransform;
            prt.offsetMin = new Vector2(pad, 0f);
            prt.offsetMax = new Vector2(-pad, 0f);

            input.textComponent = t;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        public static void SetField(InputField field, string s)
        {
            if (field == null || s == null) return;
            if (field.isFocused) return;
            if (!string.Equals(field.text, s, StringComparison.Ordinal)) field.text = s;
        }

        public static Text StatusRow(GameObject parent, float scale, out Image fill, out Image dot)
        {
            GameObject row = UI.CreateChildRT(parent, "Status");
            fill = UI.AddGalleryElementRoundedBg(row, UI.ChromeDark);
            if (fill != null) fill.raycastTarget = false;
            UI.AddHLG(row, UI.Gap(GapRef, scale),
                UI.Pad(ChipPadVRef, ChipInsetRef, ChipPadVRef, ChipPadVRef, scale),
                TextAnchor.MiddleLeft, true, true, false, false);
            UI.AddLE(row, minHeight: ButtonRef * scale, preferredHeight: ButtonRef * scale,
                flexibleHeight: 0f, minWidth: 0f, flexibleWidth: 1f);

            GameObject dgo = UI.CreateChildRT(row, "Dot");
            dot = UI.AddImage(dgo, UI.TextDim, false);
            float d = DotRef * scale;
            UI.AddLE(dgo, minWidth: d, preferredWidth: d, flexibleWidth: 0f,
                minHeight: d, preferredHeight: d, flexibleHeight: 0f);
            return Line(row, string.Empty, FontBody, UI.TextPrimary,
                LineRef, scale, true);
        }

        public sealed class Bar
        {
            public GameObject Go;
            public Image Track;
            public Image Fill;
            public RectTransform FillRT;
            float _at = -1f;

            // Width by anchor, not sizeDelta — layout-group pixel width is unknown until after the pass.
            public void Set(float f01)
            {
                if (FillRT == null) return;
                if (f01 < 0f) f01 = 0f;
                else if (f01 > 1f) f01 = 1f;
                if (Mathf.Abs(f01 - _at) < 0.004f) return;
                _at = f01;
                FillRT.anchorMax = new Vector2(f01, 1f);
                FillRT.offsetMin = Vector2.zero;
                FillRT.offsetMax = Vector2.zero;
            }

            public void SetTone(Color fill)
            {
                if (Fill != null && Fill.color != fill) Fill.color = fill;
            }

            public void SetActive(bool on)
            {
                if (Go != null && Go.activeSelf != on) Go.SetActive(on);
            }
        }

        public static Bar ProgressBar(GameObject parent, float heightRef, float scale)
        {
            Bar b = new Bar();
            b.Go = UI.CreateChildRT(parent, "Bar");
            b.Track = UI.AddImage(b.Go, UI.ChromeMid, false);
            if (b.Track != null) b.Track.raycastTarget = false;
            UI.AddLE(b.Go, minHeight: heightRef * scale, preferredHeight: heightRef * scale,
                flexibleHeight: 0f, minWidth: 0f, flexibleWidth: 1f);

            GameObject fill = UI.CreateChildRT(b.Go, "Fill");
            b.Fill = UI.AddImage(fill, UI.AccentBlue, false);
            if (b.Fill != null) b.Fill.raycastTarget = false;

            b.FillRT = fill.GetComponent<RectTransform>();
            if (b.FillRT != null)
            {
                b.FillRT.anchorMin = new Vector2(0f, 0f);
                b.FillRT.anchorMax = new Vector2(0f, 1f);
                b.FillRT.pivot = new Vector2(0f, 0.5f);
                b.FillRT.offsetMin = Vector2.zero;
                b.FillRT.offsetMax = Vector2.zero;
            }
            return b;
        }

        public static void Destroy(ref GameObject root)
        {
            Forget(root);
            try
            {
                if (root != null) UnityEngine.Object.Destroy(root);
            }
            catch { }
            root = null;
        }

        static void Forget(GameObject root)
        {
            for (int i = _shells.Count - 1; i >= 0; i--)
            {
                Shell sh = _shells[i];
                if (sh != null && sh.Root != null && sh.Root != root) continue;
                if (sh != null) RemoveFromSuperController(sh);
                _shells.RemoveAt(i);
            }
        }
    }
}
