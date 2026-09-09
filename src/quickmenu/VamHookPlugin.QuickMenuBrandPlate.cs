using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private const float QmBrandPlateHeight = 46f;
        private const float QmBrandStripeWidth = 4f;
        private const float QmBrandWordmarkLeft = 16f;
        private const float QmBrandWordmarkWidth = 56f;
        private const float QmBrandDividerX = 80f;
        private const float QmBrandMetaLeft = 92f;
        private const float QmBrandRightPad = 18f;
        private const float QmBrandTextRefreshSec = 300f;
        private const int QmBrandBranchMaxChars = 16;

        private static readonly Color QmBrandBackdropOpaque = new Color(0.09f, 0.085f, 0.075f, 1f);
        private static readonly Color QmBrandBackdropTransparent = new Color(0.06f, 0.055f, 0.045f, 0.62f);
        private static readonly Color QmBrandWordmarkColor = new Color(0.98f, 0.80f, 0.40f, 1f);
        private static readonly Color QmBrandStripeColor = new Color(0.95f, 0.74f, 0.31f, 1f);
        private static readonly Color QmBrandDividerColor = new Color(0.95f, 0.74f, 0.31f, 0.35f);
        private static readonly Color QmBrandVersionColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color QmBrandTextShadow = new Color(0f, 0f, 0f, 0.85f);

        private GameObject m_QmBrandGo;
        private RectTransform m_QmBrandRT;
        private Image m_QmBrandBackdrop;
        private Text m_QmBrandWordmark;
        private Text m_QmBrandMeta;
        private string m_QmBrandMetaIdle;
        private string m_QmBrandMetaHover;
        private string m_QmBrandMetaApplied;
        private float m_QmBrandMetaBuiltAt = -99999f;
        private float m_QmBrandFitWidth;
        private bool m_QmBrandHovering;

        private void QuickMenuEnsureBrandPlate()
        {
            if (m_QuickMenuCanvas == null) return;
            if (m_QmBrandGo != null) return;

            GameObject go = new GameObject("VPB_QM_BrandPlate");
            go.transform.SetParent(m_QuickMenuCanvas.transform, false);
            m_QmBrandGo = go;

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(QuickMenuGridCell * 4f - QuickMenuGridGap, QmBrandPlateHeight);
            m_QmBrandRT = rt;

            m_QmBrandBackdrop = AddQuickMenuRoundedBg(go, QuickMenuAssignablesForceOpaque()
                ? QmBrandBackdropOpaque
                : QmBrandBackdropTransparent);

            var hover = go.AddComponent<QuickMenuBrandPlateHoverHandler>();
            hover.owner = this;

            GameObject stripe = new GameObject("Stripe");
            stripe.transform.SetParent(go.transform, false);
            RectTransform srt = stripe.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(QmBrandStripeWidth, -16f);
            srt.anchoredPosition = new Vector2(7f, 0f);
            Image simg = stripe.AddComponent<Image>();
            simg.color = QmBrandStripeColor;
            simg.raycastTarget = false;

            m_QmBrandWordmark = QuickMenuBuildBrandText(go, "Wordmark", 26, QmBrandWordmarkColor);
            RectTransform wrt = m_QmBrandWordmark.rectTransform;
            wrt.anchorMin = new Vector2(0f, 0f);
            wrt.anchorMax = new Vector2(0f, 1f);
            wrt.pivot = new Vector2(0f, 0.5f);
            wrt.sizeDelta = new Vector2(QmBrandWordmarkWidth, 0f);
            wrt.anchoredPosition = new Vector2(QmBrandWordmarkLeft, 0f);
            m_QmBrandWordmark.text = "VPB";

            GameObject divider = new GameObject("Divider");
            divider.transform.SetParent(go.transform, false);
            RectTransform drt = divider.AddComponent<RectTransform>();
            drt.anchorMin = new Vector2(0f, 0f);
            drt.anchorMax = new Vector2(0f, 1f);
            drt.pivot = new Vector2(0f, 0.5f);
            drt.sizeDelta = new Vector2(1f, -20f);
            drt.anchoredPosition = new Vector2(QmBrandDividerX, 0f);
            Image dimg = divider.AddComponent<Image>();
            dimg.color = QmBrandDividerColor;
            dimg.raycastTarget = false;

            m_QmBrandMeta = QuickMenuBuildBrandText(go, "Meta", 20, QmBrandVersionColor);
            m_QmBrandMeta.alignment = TextAnchor.MiddleCenter;
            RectTransform mrt = m_QmBrandMeta.rectTransform;
            mrt.anchorMin = Vector2.zero;
            mrt.anchorMax = Vector2.one;
            mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.offsetMin = new Vector2(QmBrandMetaLeft, 5f);
            mrt.offsetMax = new Vector2(-QmBrandRightPad, -5f);

            QuickMenuRefreshBrandPlateText(force: true);
            QuickMenuSyncBrandPlate();
        }

        private static Text QuickMenuBuildBrandText(GameObject parent, string name, int fontSize, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();

            Text t = go.AddComponent<Text>();
            try { VPBUiFont.ApplyTo(t); } catch { }
            t.alignment = TextAnchor.MiddleLeft;
            t.color = color;
            t.fontSize = fontSize;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.resizeTextForBestFit = false;

            Shadow sh = go.AddComponent<Shadow>();
            sh.effectColor = QmBrandTextShadow;
            sh.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        private void QuickMenuRefreshBrandPlateText(bool force)
        {
            if (m_QmBrandMeta == null) return;
            float now;
            try { now = Time.unscaledTime; } catch { now = 0f; }
            if (!force && (now - m_QmBrandMetaBuiltAt) < QmBrandTextRefreshSec) return;
            m_QmBrandMetaBuiltAt = now;

            string idle = QuickMenuBuildBrandIdleText();
            string hover = QuickMenuBuildBrandHoverText();
            if (string.Equals(idle, m_QmBrandMetaIdle, StringComparison.Ordinal) &&
                string.Equals(hover, m_QmBrandMetaHover, StringComparison.Ordinal))
                return;

            m_QmBrandMetaIdle = idle;
            m_QmBrandMetaHover = hover;
            QuickMenuFitBrandPlateWidth();
            QuickMenuApplyBrandMetaText();
        }

        private static string QuickMenuBuildBrandIdleText()
        {
            int days = QuickMenuBrandBuildAgeDays();
            if (days < 0) return PluginVersionInfo.Version;

            var sb = new StringBuilder(64);
            sb.Append(PluginVersionInfo.Version);
            sb.Append("<size=15><color=#c8b189>   ");
            sb.Append(string.Format(VPBTranslation.T("hook.qmbrand.age_days", "{0} d"), days));
            sb.Append("</color></size>");
            return sb.ToString();
        }

        private static string QuickMenuBuildBrandHoverText()
        {
            string branch = null;
            try { branch = PluginVersionInfo.BuildBranch; } catch { branch = null; }
            if (string.IsNullOrEmpty(branch))
                branch = VPBTranslation.T("hook.qmbrand.branch_unknown", "local");
            if (branch.Length > QmBrandBranchMaxChars)
                branch = branch.Substring(0, QmBrandBranchMaxChars - 1) + "…";
            return "<size=17><color=#c8b189>" + branch + "</color></size>";
        }

        private void QuickMenuApplyBrandMetaText()
        {
            if (m_QmBrandMeta == null) return;
            string want = m_QmBrandHovering ? m_QmBrandMetaHover : m_QmBrandMetaIdle;
            if (want == null) want = "";
            if (string.Equals(want, m_QmBrandMetaApplied, StringComparison.Ordinal)) return;
            m_QmBrandMetaApplied = want;
            m_QmBrandMeta.text = want;
        }

        private void QuickMenuSetBrandPlateHover(bool hovering)
        {
            if (m_QmBrandHovering == hovering) return;
            m_QmBrandHovering = hovering;
            QuickMenuApplyBrandMetaText();
        }

        private void QuickMenuFitBrandPlateWidth()
        {
            if (m_QmBrandRT == null || m_QmBrandMeta == null) return;

            float want = QmBrandMetaLeft + QmBrandRightPad
                + Mathf.Max(QuickMenuMeasureBrandMeta(m_QmBrandMetaIdle),
                            QuickMenuMeasureBrandMeta(m_QmBrandMetaHover));
            float min = QuickMenuGridCell * 4f - QuickMenuGridGap;
            if (want < min) want = min;

            m_QmBrandFitWidth = want;
            Vector2 sd = m_QmBrandRT.sizeDelta;
            if (Mathf.Abs(sd.x - want) > 0.5f) m_QmBrandRT.sizeDelta = new Vector2(want, QmBrandPlateHeight);
            QuickMenuApplyBrandMetaText();
        }

        private float QuickMenuMeasureBrandMeta(string text)
        {
            if (m_QmBrandMeta == null || string.IsNullOrEmpty(text)) return 0f;
            m_QmBrandMetaApplied = text;
            m_QmBrandMeta.text = text;
            try { Canvas.ForceUpdateCanvases(); } catch { }
            try { return m_QmBrandMeta.preferredWidth; } catch { return 0f; }
        }

        private static int QuickMenuBrandBuildAgeDays()
        {
            try
            {
                DateTime built;
                if (!DateTime.TryParseExact(PluginVersionInfo.BuildDate, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out built))
                    return -1;
                int days = (DateTime.UtcNow.Date - built.Date).Days;
                return days < 0 ? -1 : days;
            }
            catch { return -1; }
        }

        private static bool QuickMenuNativeHudOverlayOpen()
        {
            SuperController sc;
            try { sc = SuperController.singleton; }
            catch { return false; }
            if (sc == null) return false;

            SuperController.ActiveUI aui;
            try { aui = sc.activeUI; }
            catch { return false; }

            if (aui != SuperController.ActiveUI.None && aui != SuperController.ActiveUI.SelectedOptions)
                return true;

            if (aui == SuperController.ActiveUI.SelectedOptions)
            {
                try
                {
                    FreeControllerV3 ctrl = sc.GetSelectedController();
                    if (ctrl != null && !ctrl.guihidden && sc.gameMode == SuperController.GameMode.Edit)
                        return true;
                }
                catch { }
            }

            try
            {
                if (sc.fileBrowserUI != null && sc.fileBrowserUI.window != null && sc.fileBrowserUI.window.activeSelf)
                    return true;
                if (sc.mediaFileBrowserUI != null && sc.mediaFileBrowserUI.window != null && sc.mediaFileBrowserUI.window.activeSelf)
                    return true;
                if (sc.directoryBrowserUI != null && sc.directoryBrowserUI.window != null && sc.directoryBrowserUI.window.activeSelf)
                    return true;
            }
            catch { }

            return false;
        }

        private void QuickMenuSyncBrandPlate()
        {
            if (m_QmBrandGo == null) return;
            bool show = string.IsNullOrEmpty(m_QmTooltipCurrent) && !QuickMenuNativeHudOverlayOpen();
            if (m_QmBrandGo.activeSelf != show)
            {
                if (!show) QuickMenuSetBrandPlateHover(false);
                m_QmBrandGo.SetActive(show);
                QuickMenuPositionDeskPreview();
            }
            if (!show) return;

            if (m_QmBrandBackdrop != null)
            {
                Color want = QuickMenuAssignablesForceOpaque() ? QmBrandBackdropOpaque : QmBrandBackdropTransparent;
                if (m_QmBrandBackdrop.color != want) m_QmBrandBackdrop.color = want;
            }
            QuickMenuRefreshBrandPlateText(force: false);
        }

        private void QuickMenuLayoutBrandPlate()
        {
            if (m_QmBrandRT == null || m_QmTooltipRT == null) return;
            m_QmBrandRT.anchoredPosition = m_QmTooltipRT.anchoredPosition;

            float w = m_QmBrandFitWidth;
            float min = QuickMenuGridCell * 4f - QuickMenuGridGap;
            if (w < min) w = min;
            Vector2 sd = m_QmBrandRT.sizeDelta;
            if (Mathf.Abs(sd.x - w) > 0.5f) m_QmBrandRT.sizeDelta = new Vector2(w, QmBrandPlateHeight);
        }

        private float QuickMenuTipLaneTopY()
        {
            float top = float.NegativeInfinity;
            if (m_QmTooltipRT != null)
                top = m_QmTooltipRT.anchoredPosition.y + m_QmTooltipRT.sizeDelta.y;
            if (m_QmBrandGo != null && m_QmBrandGo.activeSelf && m_QmBrandRT != null)
            {
                float brandTop = m_QmBrandRT.anchoredPosition.y + m_QmBrandRT.sizeDelta.y;
                if (brandTop > top) top = brandTop;
            }
            return top;
        }

        internal void SyncQuickMenuBrandPlateCornerRadius(float frac)
        {
            RoundedRect rr = m_QmBrandBackdrop as RoundedRect;
            if (rr != null) rr.cornerRadiusFraction = frac;
        }

        private class QuickMenuBrandPlateHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public VamHookPlugin owner;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (owner == null) return;
                try { owner.QuickMenuSetBrandPlateHover(true); } catch { }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (owner == null) return;
                try { owner.QuickMenuSetBrandPlateHover(false); } catch { }
            }

            void OnDisable()
            {
                if (owner == null) return;
                try { owner.QuickMenuSetBrandPlateHover(false); } catch { }
            }
        }
    }
}
