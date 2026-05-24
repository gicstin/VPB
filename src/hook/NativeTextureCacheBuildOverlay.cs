using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    internal sealed class NativeTextureCacheBuildOverlay : MonoBehaviour
    {
        private static NativeTextureCacheBuildOverlay s_Instance;

        private Canvas m_Canvas;
        private GameObject m_BannerGO;
        private RectTransform m_BannerRT;
        private Text m_TitleText;
        private Text m_SubText;
        private RectTransform m_BarBgRT;
        private RectTransform m_BarFillRT;
        private RectTransform m_BarStripRT;
        private Text m_ReportText;
        private GameObject m_CancelButtonGO;
        private RectTransform m_CancelButtonRT;
        private GameObject m_OkButtonGO;
        private RectTransform m_OkButtonRT;

        private float m_ForwardProgress01;
        private const float IndeterminateStripWidth01 = 0.28f;
        private const float IndeterminateCycleSec = 1.35f;

        public static void EnsureCreated()
        {
            if (s_Instance != null) return;

            var host = new GameObject("VPB_NativeTextureCacheBuildOverlay");
            UnityEngine.Object.DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<NativeTextureCacheBuildOverlay>();
        }

        private void Awake()
        {
            CreateUI();
        }

        private void Update()
        {
            try { VpbProgressService.PollStartupCompletion(); } catch { }
            try { VpbProgressService.PollBulkZstdProgress(); } catch { }

            VpbProgressService.DisplaySnapshot active;
            if (VpbProgressService.TryGetActiveDisplaySnapshot(out active))
            {
                ApplyActiveProgressSnapshot(active);
                return;
            }

            var snapshot = NativeTextureOnDemandCache.GetUiSnapshot();
            if (!snapshot.Visible)
            {
                if (m_BannerGO != null && m_BannerGO.activeSelf) m_BannerGO.SetActive(false);
                ResetProgressBarMotion();
                return;
            }

            if (snapshot.ShowSummary)
            {
                if (m_BannerGO != null && !m_BannerGO.activeSelf) m_BannerGO.SetActive(true);
                EnterSummaryMode(snapshot.Title, snapshot.SummaryText);
                return;
            }

            ExitSummaryMode();

            if (m_BannerGO != null && !m_BannerGO.activeSelf) m_BannerGO.SetActive(true);

            if (m_TitleText != null) m_TitleText.text = snapshot.Title ?? string.Empty;
            if (m_SubText != null) m_SubText.text = snapshot.Subtitle ?? string.Empty;

            if (m_CancelButtonGO != null && !m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(true);

            SetMovingStripVisible(false);
            UpdateProgressBar(snapshot.Progress01);
        }

        private void ApplyActiveProgressSnapshot(VpbProgressService.DisplaySnapshot snapshot)
        {
            ExitSummaryMode();
            if (m_BannerGO != null && !m_BannerGO.activeSelf) m_BannerGO.SetActive(true);
            if (m_TitleText != null) m_TitleText.text = snapshot.Title ?? string.Empty;
            if (m_SubText != null) m_SubText.text = snapshot.Subtitle ?? string.Empty;

            bool showCancel = snapshot.Cancellable;
            if (m_CancelButtonGO != null)
            {
                if (showCancel && !m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(true);
                if (!showCancel && m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(false);
            }

            if (snapshot.Progress01 < 0f)
            {
                ClearProgressBarFill();
                SetMovingStripVisible(snapshot.ShowMovingStrip);
                if (snapshot.ShowMovingStrip) UpdateProgressBarStrip();
            }
            else
            {
                UpdateProgressBar(snapshot.Progress01);
                SetMovingStripVisible(snapshot.ShowMovingStrip);
                if (snapshot.ShowMovingStrip) UpdateProgressBarStrip();
            }
        }

        private void ResetProgressBarMotion()
        {
            m_ForwardProgress01 = 0f;
            SetMovingStripVisible(false);
            ClearProgressBarFill();
        }

        private void CreateUI()
        {
            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 6000;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            m_BannerGO = new GameObject("Banner");
            m_BannerGO.transform.SetParent(transform, false);

            m_BannerRT = m_BannerGO.AddComponent<RectTransform>();
            m_BannerRT.anchorMin = new Vector2(0.5f, 1f);
            m_BannerRT.anchorMax = new Vector2(0.5f, 1f);
            m_BannerRT.pivot = new Vector2(0.5f, 1f);
            m_BannerRT.anchoredPosition = new Vector2(0, -20);
            m_BannerRT.sizeDelta = new Vector2(760, 96);

            var bg = m_BannerGO.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);
            bg.raycastTarget = false;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(m_BannerGO.transform, false);
            m_TitleText = titleGO.AddComponent<Text>();
            m_TitleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_TitleText.fontSize = 22;
            m_TitleText.color = Color.white;
            m_TitleText.alignment = TextAnchor.UpperCenter;
            m_TitleText.supportRichText = true;
            m_TitleText.raycastTarget = false;
            var titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 0.45f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.offsetMin = new Vector2(12, 0);
            titleRT.offsetMax = new Vector2(-12, -8);

            var subGO = new GameObject("Sub");
            subGO.transform.SetParent(m_BannerGO.transform, false);
            m_SubText = subGO.AddComponent<Text>();
            m_SubText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_SubText.fontSize = 16;
            m_SubText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            m_SubText.alignment = TextAnchor.UpperCenter;
            m_SubText.raycastTarget = false;
            var subRT = subGO.GetComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0f, 0.18f);
            subRT.anchorMax = new Vector2(1f, 0.55f);
            subRT.offsetMin = new Vector2(12, 0);
            subRT.offsetMax = new Vector2(-12, 0);

            var barBgGO = new GameObject("BarBg");
            barBgGO.transform.SetParent(m_BannerGO.transform, false);
            m_BarBgRT = barBgGO.AddComponent<RectTransform>();
            m_BarBgRT.anchorMin = new Vector2(0f, 0f);
            m_BarBgRT.anchorMax = new Vector2(1f, 0.18f);
            m_BarBgRT.offsetMin = new Vector2(12, 12);
            m_BarBgRT.offsetMax = new Vector2(-12, 6);
            var barBg = barBgGO.AddComponent<Image>();
            barBg.color = new Color(1f, 1f, 1f, 0.12f);
            barBg.raycastTarget = false;

            var barFillGO = new GameObject("BarFill");
            barFillGO.transform.SetParent(barBgGO.transform, false);
            m_BarFillRT = barFillGO.AddComponent<RectTransform>();
            m_BarFillRT.anchorMin = new Vector2(0f, 0f);
            m_BarFillRT.anchorMax = new Vector2(0f, 1f);
            m_BarFillRT.pivot = new Vector2(0f, 0.5f);
            m_BarFillRT.anchoredPosition = Vector2.zero;
            m_BarFillRT.sizeDelta = new Vector2(0f, 0f);
            var barFillImg = barFillGO.AddComponent<Image>();
            barFillImg.color = new Color(0.3f, 0.8f, 0.35f, 0.9f);
            barFillImg.raycastTarget = false;

            var barStripGO = new GameObject("BarStrip");
            barStripGO.transform.SetParent(barBgGO.transform, false);
            m_BarStripRT = barStripGO.AddComponent<RectTransform>();
            m_BarStripRT.anchorMin = new Vector2(0f, 0f);
            m_BarStripRT.anchorMax = new Vector2(0f, 1f);
            m_BarStripRT.pivot = new Vector2(0f, 0.5f);
            m_BarStripRT.anchoredPosition = Vector2.zero;
            m_BarStripRT.sizeDelta = new Vector2(0f, 0f);
            var barStripImg = barStripGO.AddComponent<Image>();
            barStripImg.color = new Color(0.55f, 0.95f, 0.6f, 0.95f);
            barStripImg.raycastTarget = false;
            barStripGO.SetActive(false);

            var reportGO = new GameObject("Report");
            reportGO.transform.SetParent(m_BannerGO.transform, false);
            m_ReportText = reportGO.AddComponent<Text>();
            m_ReportText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_ReportText.fontSize = 16;
            m_ReportText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            m_ReportText.alignment = TextAnchor.UpperLeft;
            m_ReportText.supportRichText = true;
            m_ReportText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_ReportText.verticalOverflow = VerticalWrapMode.Truncate;
            m_ReportText.raycastTarget = false;
            var reportRT = reportGO.GetComponent<RectTransform>();
            reportRT.anchorMin = new Vector2(0f, 0f);
            reportRT.anchorMax = new Vector2(1f, 1f);
            reportRT.offsetMin = new Vector2(18, 78);
            reportRT.offsetMax = new Vector2(-18, -70);
            reportGO.SetActive(false);

            m_CancelButtonGO = UI.CreateUIButton(m_BannerGO, 132, 40, "Cancel", 18, 0, 0, AnchorPresets.middleCenter, () =>
            {
                VpbProgressService.RequestCancelActiveJob();
            });
            m_CancelButtonRT = m_CancelButtonGO.GetComponent<RectTransform>();
            if (m_CancelButtonRT != null)
            {
                m_CancelButtonRT.anchorMin = new Vector2(0f, 1f);
                m_CancelButtonRT.anchorMax = new Vector2(0f, 1f);
                m_CancelButtonRT.pivot = new Vector2(0f, 1f);
                m_CancelButtonRT.anchoredPosition = new Vector2(14, -10);
            }

            try
            {
                var img = m_CancelButtonGO.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color;
                    img.color = new Color(c.r, c.g, c.b, 0.45f);
                }
            }
            catch { }
            m_CancelButtonGO.SetActive(false);

            m_OkButtonGO = UI.CreateUIButton(m_BannerGO, 180, 48, "OK", 20, 0, 0, AnchorPresets.middleCenter, () =>
            {
                NativeTextureOnDemandCache.DismissSummary();
            });
            m_OkButtonRT = m_OkButtonGO.GetComponent<RectTransform>();
            if (m_OkButtonRT != null)
            {
                m_OkButtonRT.anchorMin = new Vector2(1f, 0f);
                m_OkButtonRT.anchorMax = new Vector2(1f, 0f);
                m_OkButtonRT.pivot = new Vector2(1f, 0f);
                m_OkButtonRT.anchoredPosition = new Vector2(-14, 14);
            }
            m_OkButtonGO.SetActive(false);

            m_BannerGO.SetActive(false);
        }

        private void ClearProgressBarFill()
        {
            if (m_BarFillRT == null) return;
            m_BarFillRT.anchorMin = new Vector2(0f, 0f);
            m_BarFillRT.anchorMax = new Vector2(0f, 1f);
            m_BarFillRT.offsetMin = Vector2.zero;
            m_BarFillRT.offsetMax = Vector2.zero;
        }

        private void SetMovingStripVisible(bool visible)
        {
            if (m_BarStripRT == null) return;
            if (m_BarStripRT.gameObject.activeSelf != visible)
                m_BarStripRT.gameObject.SetActive(visible);
        }

        private void UpdateProgressBar(float progress01)
        {
            if (m_BarBgRT == null || m_BarFillRT == null) return;

            float p = progress01;
            if (p < 0f) p = 0f;
            if (p > 1f) p = 1f;
            if (p > m_ForwardProgress01) m_ForwardProgress01 = p;
            p = m_ForwardProgress01;

            m_BarFillRT.anchorMin = new Vector2(0f, 0f);
            m_BarFillRT.anchorMax = new Vector2(p, 1f);
            m_BarFillRT.offsetMin = Vector2.zero;
            m_BarFillRT.offsetMax = Vector2.zero;
        }

        /// <summary>Indeterminate: fixed-width strip marches left → right, then loops.</summary>
        private void UpdateProgressBarStrip()
        {
            if (m_BarStripRT == null) return;

            float cycle = IndeterminateCycleSec > 0.001f
                ? (Time.unscaledTime % IndeterminateCycleSec) / IndeterminateCycleSec
                : 0f;
            float stripW = Mathf.Clamp(IndeterminateStripWidth01, 0.08f, 0.45f);
            float maxStart = 1f - stripW;
            float start = cycle * maxStart;

            m_BarStripRT.anchorMin = new Vector2(start, 0f);
            m_BarStripRT.anchorMax = new Vector2(start + stripW, 1f);
            m_BarStripRT.offsetMin = Vector2.zero;
            m_BarStripRT.offsetMax = Vector2.zero;
        }

        private void EnterSummaryMode(string title, string summary)
        {
            if (m_TitleText != null) m_TitleText.text = title ?? string.Empty;
            if (m_SubText != null) m_SubText.gameObject.SetActive(false);
            if (m_BarBgRT != null) m_BarBgRT.gameObject.SetActive(false);

            if (m_ReportText != null)
            {
                m_ReportText.text = summary ?? string.Empty;
                m_ReportText.verticalOverflow = VerticalWrapMode.Overflow;
                if (!m_ReportText.gameObject.activeSelf) m_ReportText.gameObject.SetActive(true);
            }

            if (m_OkButtonGO != null && !m_OkButtonGO.activeSelf) m_OkButtonGO.SetActive(true);
            if (m_CancelButtonGO != null && m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(false);

            if (m_BannerRT != null)
            {
                if (m_BannerRT.sizeDelta.y < 320f) m_BannerRT.sizeDelta = new Vector2(m_BannerRT.sizeDelta.x, 320f);
            }
        }

        private void ExitSummaryMode()
        {
            if (m_SubText != null && !m_SubText.gameObject.activeSelf) m_SubText.gameObject.SetActive(true);
            if (m_BarBgRT != null && !m_BarBgRT.gameObject.activeSelf) m_BarBgRT.gameObject.SetActive(true);

            if (m_ReportText != null && m_ReportText.gameObject.activeSelf) m_ReportText.gameObject.SetActive(false);
            if (m_ReportText != null) m_ReportText.verticalOverflow = VerticalWrapMode.Truncate;
            if (m_OkButtonGO != null && m_OkButtonGO.activeSelf) m_OkButtonGO.SetActive(false);

            if (m_CancelButtonGO != null && !m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(true);

            if (m_BannerRT != null)
            {
                if (m_BannerRT.sizeDelta.y != 96f) m_BannerRT.sizeDelta = new Vector2(m_BannerRT.sizeDelta.x, 96f);
            }
        }
    }
}
