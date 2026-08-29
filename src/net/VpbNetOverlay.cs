using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    public static class VpbNetOverlay
    {
        const string HostName = "VPB_NetOverlay";
        const float RefreshSeconds = 0.25f;
        const float PanelWidthRef = 560f;
        const float TitleHeightRef = 34f;
        const float BodyHeightRef = 132f;
        const float CornerRadiusRef = 10f;
        const float PadRef = 12f;
        const float DefaultX = 24f;
        const float DefaultY = -24f;

        static readonly VpbNetDiagnostics _stats = new VpbNetDiagnostics();
        static readonly StringBuilder _sb = new StringBuilder(512);

        static GameObject _root;
        static Canvas _canvas;
        static GameObject _panel;
        static RectTransform _panelRT;
        static GameObject _body;
        static Text _text;
        static Text _collapseGlyph;
        static float _nextPoll;
        static float _nextRefresh;
        static bool _visible;
        static bool _collapsed;
        static float _scale = 1f;

        public static VpbNetDiagnostics Stats { get { return _stats; } }
        public static bool IsVisible { get { return _visible && !_collapsed; } }

        public static void Poll()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.5f;

            bool want = false;
            try
            {
                Settings s = Settings.Instance;
                want = s != null && s.NetOverlay != null && s.NetOverlay.Value;
            }
            catch { }

            if (!want)
            {
                if (_root != null) Destroy();
                return;
            }

            if (_root == null && !Create()) return;
            _visible = true;
        }

        public static void Tick()
        {
            if (!_visible || _collapsed || _text == null) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshSeconds;

            if (!_stats.HasChanged()) return;

            _stats.Format(_sb);
            _text.text = _sb.ToString();
        }

        static bool Create()
        {
            try
            {
                _scale = GalleryUiMetrics.Resolve(true).ChromeScale;
                if (_scale <= 0f) _scale = 1f;

                _root = new GameObject(HostName);
                _canvas = _root.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 1000;
                _root.AddComponent<GraphicRaycaster>();

                CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                RectTransform rootRT = _root.GetComponent<RectTransform>();
                if (rootRT != null)
                {
                    rootRT.anchorMin = Vector2.zero;
                    rootRT.anchorMax = Vector2.one;
                    rootRT.sizeDelta = Vector2.zero;
                }

                ReadPersisted();

                float s = _scale;
                float titleH = TitleHeightRef * s;
                float pad = PadRef * s;
                float btnW = 30f * s;
                float btnH = 26f * s;
                int fontPt = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, s,
                    GalleryUiDesignTokens.FontMinRef);

                Color panelColor = UI.ChromeDarker;
                panelColor.a = 0.94f;
                _panel = UI.AddChildGOImage(_root, panelColor, AnchorPresets.topLeft,
                    PanelWidthRef * s, titleH + BodyHeightRef * s,
                    new Vector2(PersistedX(), PersistedY()), true);
                _panel.name = "Panel";
                _panelRT = _panel.GetComponent<RectTransform>();

                RoundedRect panelRounded = _panel.GetComponent<RoundedRect>();
                if (panelRounded != null)
                {
                    panelRounded.excludeFromGlobalRadiusSync = true;
                    panelRounded.cornerRadiusFraction = 0f;
                    panelRounded.cornerRadius = CornerRadiusRef * s;
                }

                GameObject title = UI.AddChildGOImage(_panel, UI.ChromePanel, AnchorPresets.hStretchTop,
                    0f, titleH, Vector2.zero);
                title.name = "TitleBar";
                RectTransform titleRT = title.GetComponent<RectTransform>();
                titleRT.anchorMin = new Vector2(0f, 1f);
                titleRT.anchorMax = new Vector2(1f, 1f);
                titleRT.pivot = new Vector2(0.5f, 1f);
                titleRT.offsetMin = new Vector2(0f, -titleH);
                titleRT.offsetMax = new Vector2(-(btnW * 2f + pad), 0f);

                UIFloatPanelDrag drag = title.AddComponent<UIFloatPanelDrag>();
                drag.Target = _panelRT;
                drag.OnMoved = SavePosition;

                Text titleText = UI.CreateLabel(title, "Net diagnostics", GalleryUiDesignTokens.FontRef,
                    UI.TextPrimary, TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow,
                    VerticalWrapMode.Truncate, false, false, AnchorPresets.stretchAll,
                    Vector2.zero, new Vector2(pad, 0f), "Title");
                GalleryUiMetrics.ApplyFont(titleText, GalleryUiDesignTokens.FontRef, s,
                    GalleryUiDesignTokens.FontMinRef);

                GameObject collapseBtn = UI.CreateUIButton(_panel, btnW, btnH, _collapsed ? "+" : "-",
                    fontPt, -(btnW + pad * 0.5f), -(titleH - btnH) * 0.5f,
                    AnchorPresets.topRight, ToggleCollapse);
                collapseBtn.name = "CollapseButton";
                _collapseGlyph = collapseBtn.GetComponentInChildren<Text>();

                GameObject closeBtn = UI.CreateUIButton(_panel, btnW, btnH, "X",
                    fontPt, -pad * 0.5f, -(titleH - btnH) * 0.5f,
                    AnchorPresets.topRight, CloseFromButton);
                closeBtn.name = "CloseButton";

                _body = UI.CreateChildRT(_panel, "Body", AnchorPresets.stretchAll, Vector2.zero, Vector2.zero);
                RectTransform bodyRT = _body.GetComponent<RectTransform>();
                bodyRT.offsetMin = new Vector2(pad, pad * 0.7f);
                bodyRT.offsetMax = new Vector2(-pad, -(titleH + pad * 0.3f));

                _text = UI.CreateLabel(_body, string.Empty, GalleryUiDesignTokens.FontRef,
                    UI.TextPrimary, TextAnchor.UpperLeft, HorizontalWrapMode.Overflow,
                    VerticalWrapMode.Overflow, false, false, AnchorPresets.stretchAll,
                    Vector2.zero, Vector2.zero, "NetDiag");
                GalleryUiMetrics.ApplyFont(_text, GalleryUiDesignTokens.FontRef, s,
                    GalleryUiDesignTokens.FontMinRef);

                ApplyCollapsed();

                _stats.Reset();
                _nextRefresh = 0f;
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] overlay create failed: " + e.Message);
                Destroy();
                return false;
            }
        }

        static void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            ApplyCollapsed();

            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetOverlayCollapsed != null) s.NetOverlayCollapsed.Value = _collapsed;
            }
            catch { }

            if (!_collapsed)
            {
                _stats.Reset();
                _nextRefresh = 0f;
            }
        }

        static void ApplyCollapsed()
        {
            float s = _scale > 0f ? _scale : 1f;
            if (_body != null) _body.SetActive(!_collapsed);
            if (_panelRT != null)
                _panelRT.sizeDelta = new Vector2(PanelWidthRef * s,
                    _collapsed ? TitleHeightRef * s : (TitleHeightRef + BodyHeightRef) * s);
            if (_collapseGlyph != null) _collapseGlyph.text = _collapsed ? "+" : "-";
        }

        static void CloseFromButton()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetOverlay != null) s.NetOverlay.Value = false;
            }
            catch { }
            Destroy();
        }

        static void SavePosition()
        {
            if (_panelRT == null) return;
            try
            {
                Settings s = Settings.Instance;
                if (s == null) return;
                Vector2 p = _panelRT.anchoredPosition;
                if (s.NetOverlayX != null) s.NetOverlayX.Value = p.x;
                if (s.NetOverlayY != null) s.NetOverlayY.Value = p.y;
            }
            catch { }
        }

        static void ReadPersisted()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetOverlayCollapsed != null) _collapsed = s.NetOverlayCollapsed.Value;
            }
            catch { _collapsed = false; }
        }

        static float PersistedX()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetOverlayX != null) return s.NetOverlayX.Value;
            }
            catch { }
            return DefaultX;
        }

        static float PersistedY()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetOverlayY != null) return s.NetOverlayY.Value;
            }
            catch { }
            return DefaultY;
        }

        public static void Destroy()
        {
            try
            {
                if (_root != null) UnityEngine.Object.Destroy(_root);
            }
            catch { }

            _root = null;
            _canvas = null;
            _panel = null;
            _panelRT = null;
            _body = null;
            _text = null;
            _collapseGlyph = null;
            _visible = false;
        }
    }
}
