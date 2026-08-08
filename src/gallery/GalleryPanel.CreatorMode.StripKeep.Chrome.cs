using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Strip keep floating chrome: titlebar drag, X close, corner resize, size/pos persist.
    /// Same float family as Settings / Plugins — work surface first, not locked modal.
    /// </summary>
    public partial class GalleryPanel
    {
        // Neutral gallery greys — match Settings / Plugins / filter-presets floats.
        private static readonly Color StripKeepTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color StripKeepFooterBarBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color StripKeepPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color StripKeepScrollBg = GalleryUiColorTokens.ModalSurface;
        private static readonly Color StripKeepCancelBg = GalleryUiColorTokens.SurfaceMid;

        private RectTransform _stripKeepPanelRT;
        private GameObject _stripKeepResizeGO;
        private Coroutine _stripKeepPanelSaveCo;

        /// <summary>
        /// Chip/preset width = glyph advances + pad. No fixed cell — short labels stay tight.
        /// </summary>
        private static float StripKeepChipWidth(string label, int fontSize, float s)
        {
            float pad = 14f * s;
            float minW = 32f * s;
            if (string.IsNullOrEmpty(label)) return minW + pad;

            float textW = 0f;
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { font = null; }
            if (font != null)
            {
                try { font.RequestCharactersInTexture(label, fontSize, FontStyle.Normal); } catch { }
                for (int i = 0; i < label.Length; i++)
                {
                    CharacterInfo ci;
                    if (font.GetCharacterInfo(label[i], out ci, fontSize, FontStyle.Normal))
                        textW += ci.advance;
                    else
                        textW += fontSize * 0.55f;
                }
            }
            else
            {
                textW = label.Length * fontSize * 0.55f;
            }
            return Mathf.Max(minW, textW + pad);
        }

        /// <summary>
        /// Clamp UI.Text into its layout cell — no wrap bleed. Parent should RectMask2D.
        /// </summary>
        private static void StripKeepClampText(Text t)
        {
            if (t == null) return;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = false;
        }

        private static void StripKeepStyleChromeButtonText(GameObject go, float s)
        {
            if (go == null) return;
            // No RectMask2D on button root — clips outward/inward hover rim (sides-only bug).
            // Truncate on Text is enough for glyph overflow.
            Text t = go.GetComponentInChildren<Text>(true);
            if (t == null) return;
            GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            StripKeepClampText(t);
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            RectTransform lrt = t.GetComponent<RectTransform>();
            if (lrt != null)
            {
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(4f * s, 1f * s);
                lrt.offsetMax = new Vector2(-4f * s, -1f * s);
            }
        }

        /// <summary>
        /// Persist config as panel-center coords (stable across pivot). Runtime uses top-left pivot.
        /// </summary>
        private static Vector2 StripKeepCenterPosToTopLeft(Vector2 centerPos, Vector2 size)
        {
            return new Vector2(centerPos.x - size.x * 0.5f, centerPos.y + size.y * 0.5f);
        }

        private static Vector2 StripKeepTopLeftPosToCenter(Vector2 topLeftPos, Vector2 size)
        {
            return new Vector2(topLeftPos.x + size.x * 0.5f, topLeftPos.y - size.y * 0.5f);
        }

        private Vector2 StripKeepResolvePanelSize(float s)
        {
            float w = GalleryUiDesignTokens.StripKeepFloatDefaultWidthRef;
            float h = GalleryUiDesignTokens.StripKeepFloatDefaultHeightRef;
            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.CreatorStripPanelSizeSaved)
                {
                    w = VPBConfig.Instance.CreatorStripPanelWidthRef;
                    h = VPBConfig.Instance.CreatorStripPanelHeightRef;
                }
            }
            catch { }
            w = Mathf.Clamp(
                w,
                GalleryUiDesignTokens.StripKeepFloatMinWidthRef,
                GalleryUiDesignTokens.StripKeepFloatMaxWidthRef);
            h = Mathf.Clamp(
                h,
                GalleryUiDesignTokens.StripKeepFloatMinHeightRef,
                GalleryUiDesignTokens.StripKeepFloatMaxHeightRef);
            return new Vector2(w * s, h * s);
        }

        private Vector2 StripKeepResolvePanelPos()
        {
            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.CreatorStripPanelPosSaved)
                {
                    return new Vector2(
                        VPBConfig.Instance.CreatorStripPanelPosX,
                        VPBConfig.Instance.CreatorStripPanelPosY);
                }
            }
            catch { }
            return Vector2.zero;
        }

        private void StripKeepPersistPanelGeometry()
        {
            if (_stripKeepPanelRT == null) return;
            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            try
            {
                if (VPBConfig.Instance == null) return;
                Vector2 center = StripKeepTopLeftPosToCenter(
                    _stripKeepPanelRT.anchoredPosition, _stripKeepPanelRT.sizeDelta);
                VPBConfig.Instance.CreatorStripPanelPosSaved = true;
                VPBConfig.Instance.CreatorStripPanelPosX = center.x;
                VPBConfig.Instance.CreatorStripPanelPosY = center.y;
                VPBConfig.Instance.CreatorStripPanelSizeSaved = true;
                VPBConfig.Instance.CreatorStripPanelWidthRef = Mathf.Clamp(
                    _stripKeepPanelRT.sizeDelta.x / s,
                    GalleryUiDesignTokens.StripKeepFloatMinWidthRef,
                    GalleryUiDesignTokens.StripKeepFloatMaxWidthRef);
                VPBConfig.Instance.CreatorStripPanelHeightRef = Mathf.Clamp(
                    _stripKeepPanelRT.sizeDelta.y / s,
                    GalleryUiDesignTokens.StripKeepFloatMinHeightRef,
                    GalleryUiDesignTokens.StripKeepFloatMaxHeightRef);
            }
            catch { return; }
            StripKeepSchedulePanelSave();
        }

        private void StripKeepSchedulePanelSave()
        {
            if (!isActiveAndEnabled) return;
            try
            {
                if (_stripKeepPanelSaveCo != null)
                    StopCoroutine(_stripKeepPanelSaveCo);
            }
            catch { }
            try { _stripKeepPanelSaveCo = StartCoroutine(StripKeepPanelSaveCo()); }
            catch { }
        }

        private System.Collections.IEnumerator StripKeepPanelSaveCo()
        {
            yield return null;
            yield return null;
            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.Save(false);
            }
            catch { }
            _stripKeepPanelSaveCo = null;
        }

        private void StripKeepOnPanelMoved()
        {
            StripKeepPersistPanelGeometry();
        }

        private void StripKeepOnPanelResized()
        {
            StripKeepPersistPanelGeometry();
            // Reflow preset chips to new width — wrap is not sticky at open-time width.
            ScheduleStripKeepPresetChipFlow();
        }

        private Vector2 StripKeepPanelMinSizeScaled()
        {
            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            return new Vector2(
                GalleryUiDesignTokens.StripKeepFloatMinWidthRef * s,
                GalleryUiDesignTokens.StripKeepFloatMinHeightRef * s);
        }

        private Vector2 StripKeepPanelMaxSizeScaled()
        {
            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            return new Vector2(
                GalleryUiDesignTokens.StripKeepFloatMaxWidthRef * s,
                GalleryUiDesignTokens.StripKeepFloatMaxHeightRef * s);
        }

        /// <summary>
        /// Horizontal chip strip that scrolls instead of overflowing panel width.
        /// Returns content transform for chip children; hostGo is the scroll strip root.
        /// </summary>
        private static Transform StripKeepCreateHScrollChipStrip(
            Transform parent, string name, float height, float s,
            out ScrollRect scroll, out GameObject hostGo)
        {
            scroll = null;
            hostGo = new GameObject(name);
            hostGo.transform.SetParent(parent, false);
            UI.AddLE(hostGo, minHeight: height, preferredHeight: height, flexibleWidth: 1f);
            UI.AddImage(hostGo, GalleryUiColorTokens.ModalSurface);

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(hostGo.transform, false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(2f * s, 2f * s);
            vpRt.offsetMax = new Vector2(-2f * s, -2f * s);
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(0f, 1f);
            contentRt.pivot = new Vector2(0f, 0.5f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            // childForceExpandWidth must stay false — expand equalizes chip cells (clip / waste).
            HorizontalLayoutGroup hlg = UI.AddHLG(
                content, spacing: 4f * s, padding: UI.Pad(2, 2, 2, 2, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: true);
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll = hostGo.AddComponent<ScrollRect>();
            scroll.viewport = vpRt;
            scroll.content = contentRt;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f * s;
            return content.transform;
        }

        /// <summary>
        /// Wrap-flow chip host (FilterChips pattern). One row until full, then more rows.
        /// </summary>
        private static Transform StripKeepCreateWrapChipHost(
            Transform parent, string name, float rowH, float s,
            out GameObject hostGo, out LayoutElement hostLe, out RectTransform contentRt)
        {
            hostGo = new GameObject(name);
            hostGo.transform.SetParent(parent, false);
            hostLe = UI.AddLE(hostGo, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f, flexibleHeight: 0f);
            UI.AddImage(hostGo, GalleryUiColorTokens.ModalSurface);

            GameObject content = new GameObject("Content");
            content.transform.SetParent(hostGo.transform, false);
            contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            float pad = 2f * s;
            // Height driven by FlowWrapChips (offsetMin.y = -totalH) — do not lock to one row.
            contentRt.offsetMin = new Vector2(pad, -rowH);
            contentRt.offsetMax = new Vector2(-pad, -pad);
            return content.transform;
        }

        /// <summary>
        /// Manual wrap: place chip children left→right, new row when width exceeded.
        /// Returns row count (≥1). Updates host LE height + content insets (Settings tabs pattern).
        /// </summary>
        private static int StripKeepFlowWrapChips(
            RectTransform contentRt, LayoutElement hostLe, float rowH, float s, float availW)
        {
            if (contentRt == null) return 1;
            float colSpacing = 4f * s;
            float rowSpacing = 4f * s;
            float pad = 2f * s;
            if (availW <= 1f)
            {
                try { availW = contentRt.rect.width; } catch { availW = 0f; }
            }
            if (availW <= 1f) availW = 400f * s;
            float x = 0f;
            float y = 0f;
            int rows = 1;
            int n = contentRt.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform child = contentRt.GetChild(i);
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
            contentRt.offsetMin = new Vector2(pad, -totalH);
            contentRt.offsetMax = new Vector2(-pad, -pad);
            contentRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalH);
            if (hostLe != null)
            {
                hostLe.minHeight = totalH;
                hostLe.preferredHeight = totalH;
            }
            return rows < 1 ? 1 : rows;
        }
    }
}
