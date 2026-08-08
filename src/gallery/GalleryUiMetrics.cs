using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Resolved gallery UI scale. User pane scale is independent of grid cell size.
    /// Layout/chrome uses <see cref="ChromeScale"/>; grid overlays use <see cref="CellOverlayScale"/>.
    /// </summary>
    public readonly struct GalleryUiMetrics
    {
        public readonly float PaneScale;
        public readonly float HostScale;
        public readonly bool IsDesktop;
        public readonly bool RespectHostScale;
        public readonly bool DesktopFixedPanel;

        /// <summary>Pane × host (VaM UI scale on desktop). Single factor for layout + chrome fonts.</summary>
        public float ChromeScale
        {
            get
            {
                float s = PaneScale;
                if (RespectHostScale)
                    s *= HostScale;
                return s;
            }
        }

        public GalleryUiMetrics(float paneScale, float hostScale, bool isDesktop, bool respectHostScale, bool desktopFixedPanel)
        {
            PaneScale = paneScale <= 0f ? 1f : paneScale;
            HostScale = hostScale <= 0f ? 1f : hostScale;
            IsDesktop = isDesktop;
            RespectHostScale = respectHostScale;
            DesktopFixedPanel = desktopFixedPanel;
        }

        /// <param name="desktopFixedPanel">True when gallery uses screen-space overlay (fixed dock).</param>
        public static GalleryUiMetrics Resolve(bool desktopFixedPanel = false)
        {
            float pane = 1f;
            bool isDesktop = false;
            bool respectHost = false;
            try
            {
                if (VPBConfig.Instance != null)
                {
                    pane = VPBConfig.Instance.CurrentGalleryUiScale;
                    isDesktop = !VPBConfig.Instance.IsVR;
                    respectHost = isDesktop;
                }
            }
            catch { }

            float host = 1f;
            if (respectHost)
            {
                try
                {
                    // Follow VaM Monitor UI Scale (User Preferences), not BepInEx Settings.UIScale.
                    float vam = VPBConfig.Instance != null
                        ? VPBConfig.Instance.UiScale
                        : GalleryUiDesignTokens.VamMonitorUiScaleDesignBaseline;
                    float baseline = GalleryUiDesignTokens.VamMonitorUiScaleDesignBaseline;
                    if (baseline <= 0f) baseline = 1f;
                    host = vam / baseline;
                    if (host <= 0f) host = 1f;
                }
                catch { host = 1f; }
            }

            return new GalleryUiMetrics(pane, host, isDesktop, respectHost, desktopFixedPanel);
        }

        public static GalleryUiMetrics ForPanel(GalleryPanel panel)
        {
            return Resolve(panel != null && panel.IsFixedLocallyForUiScale());
        }

        public float Px(float designPx) => designPx * ChromeScale;

        public float PxPane(float designPx) => designPx * PaneScale;

        public int RoundPx(float designPx) => Mathf.RoundToInt(Px(designPx));

        public int Font(int designPt, int minPt = GalleryUiDesignTokens.FontMinRef)
            => ScaledFontSize(designPt, ChromeScale, minPt);

        public int FontPane(int designPt, int minPt = GalleryUiDesignTokens.FontMinRef)
            => ScaledFontSize(designPt, PaneScale, minPt);

        public int FontTitle(int minPt = GalleryUiDesignTokens.FontMinRef)
            => Font(GalleryUiDesignTokens.FontTitleRef, minPt);

        public int FontBody(int minPt = GalleryUiDesignTokens.FontMinRef)
            => Font(GalleryUiDesignTokens.FontBodyRef, minPt);

        public int FontCaption(int minPt = GalleryUiDesignTokens.FontMinRef)
            => Font(GalleryUiDesignTokens.FontCaptionRef, minPt);

        /// <param name="designControlHeightPx">Unscaled control height (design ref).</param>
        public static int GlyphFontFromControlHeight(float designControlHeightPx, float scale, int minPt = GalleryUiDesignTokens.FontMinRef)
        {
            if (designControlHeightPx <= 0f) designControlHeightPx = GalleryUiDesignTokens.TitleBarChipRef;
            if (scale <= 0f) scale = 1f;
            if (minPt < 1) minPt = 1;
            return Mathf.Max(minPt, Mathf.RoundToInt(designControlHeightPx * scale * GalleryUiDesignTokens.GlyphFontHeightFactor));
        }

        public RectOffset Pad(float left, float top, float right, float bottom)
            => new RectOffset(RoundPx(left), RoundPx(top), RoundPx(right), RoundPx(bottom));

        public RectOffset Pad(float uniform) => Pad(uniform, uniform, uniform, uniform);

        public static int ScaledFontSize(int designPt, float scale, int minPt = 10)
        {
            if (designPt <= 0) designPt = 1;
            if (minPt < 1) minPt = 1;
            float fontScale = Mathf.Clamp(scale, (float)minPt / (float)designPt, 100f);
            return Mathf.RoundToInt(designPt * fontScale);
        }

        public static float FontExtraScale(float scale, int designPt, int minPt = 10)
        {
            if (designPt <= 0) designPt = 1;
            float fontScale = Mathf.Clamp(scale, (float)minPt / (float)designPt, 100f);
            return (fontScale > 0f) ? (scale / fontScale) : 1f;
        }

        public void ApplyFont(Text txt, int designPt, int minPt = 10)
        {
            ApplyFont(txt, designPt, ChromeScale, minPt);
        }

        public void ApplyFontPane(Text txt, int designPt, int minPt = 10)
        {
            ApplyFont(txt, designPt, PaneScale, minPt);
        }

        public static void ApplyFont(Text txt, int designPt, float scale, int minPt = GalleryUiDesignTokens.FontMinRef)
        {
            if (txt == null) return;
            txt.fontSize = ScaledFontSize(designPt, scale, minPt);
            float extra = FontExtraScale(scale, designPt, minPt);
            txt.transform.localScale = new Vector3(extra, extra, 1f);

            // Always re-apply pivot. Old code only set pivot when extra != 1, so after dipping
            // below FontMin floor (UI scale ~0.55) bottom-anchored tooltip/status rows kept a
            // center pivot and stayed shifted down until panel recreate.
            RectTransform rt = txt.rectTransform;
            if (rt == null) return;
            Vector2 pivot = Mathf.Abs(extra - 1f) > 0.001f
                ? PivotFromTextAnchor(txt.alignment)
                : new Vector2(0.5f, 0.5f);
            // Fixed vertical anchor (status / hover-path row): keep pivot on that edge so
            // localScale never drops half the rect below the InfoBar.
            if (Mathf.Abs(rt.anchorMin.y - rt.anchorMax.y) < 0.001f)
            {
                if (rt.anchorMin.y <= 0.01f) pivot.y = 0f;
                else if (rt.anchorMin.y >= 0.99f) pivot.y = 1f;
            }
            rt.pivot = pivot;
        }

        /// <summary>Pivot matching <see cref="Text.alignment"/> so localScale shrinks from the reading edge.</summary>
        public static Vector2 PivotFromTextAnchor(TextAnchor alignment)
        {
            float x = 0.5f;
            float y = 0.5f;
            switch (alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    x = 0f;
                    break;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    x = 1f;
                    break;
            }
            switch (alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    y = 1f;
                    break;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    y = 0f;
                    break;
            }
            return new Vector2(x, y);
        }

        public void ApplyTitleFont(Text txt, int minPt = GalleryUiDesignTokens.FontMinRef)
        {
            ApplyFont(txt, GalleryUiDesignTokens.FontTitleRef, minPt);
            if (txt != null) txt.fontStyle = FontStyle.Bold;
        }

        public void ApplyBodyFont(Text txt, int minPt = GalleryUiDesignTokens.FontMinRef)
        {
            ApplyFont(txt, GalleryUiDesignTokens.FontBodyRef, minPt);
            if (txt != null) txt.fontStyle = FontStyle.Normal;
        }

        /// <summary>Modal / section header — title size + bold weight.</summary>
        public static void ApplyEmphasisTitle(Text txt, int scaledFontSize)
        {
            if (txt == null) return;
            txt.fontSize = scaledFontSize;
            txt.fontStyle = FontStyle.Bold;
            txt.transform.localScale = Vector3.one;
        }

        public void ApplyCaptionFont(Text txt, int minPt = GalleryUiDesignTokens.FontMinRef)
        {
            ApplyFont(txt, GalleryUiDesignTokens.FontCaptionRef, minPt);
            if (txt != null) txt.fontStyle = FontStyle.Normal;
        }

        public static void ApplyGlyphFont(Text txt, float designControlHeightPx, float scale, int minPt = GalleryUiDesignTokens.FontMinRef)
        {
            if (txt == null) return;
            txt.fontSize = GlyphFontFromControlHeight(designControlHeightPx, scale, minPt);
            txt.transform.localScale = Vector3.one;
        }

        /// <summary>Overlay/badge scale from cell geometry — never scales the thumbnail cell itself.</summary>
        public static float CellOverlayScale(float cellWidth, float cellHeight)
        {
            float w = cellWidth > 0f ? cellWidth : GalleryUiDesignTokens.GridCellRefSize;
            float h = cellHeight > 0f ? cellHeight : GalleryUiDesignTokens.GridCellRefSize;
            float cell = Mathf.Min(w, h);
            return Mathf.Clamp(
                cell / GalleryUiDesignTokens.GridCellRefSize,
                GalleryUiDesignTokens.GridCellOverlayMin,
                GalleryUiDesignTokens.GridCellOverlayMax);
        }

        public float HelpChromeScale()
            => Mathf.Max(GalleryUiDesignTokens.InAppHelpScaleFloor, ChromeScale);
    }
}
