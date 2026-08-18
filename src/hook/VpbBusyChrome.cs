using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// External ScreenSpaceOverlay busy chrome above game layer.
    /// Driven by <see cref="VpbProgressService"/>. Progress mode: centered row at Screen.width/φ
    /// + thin bottom progress line. Layout/fonts follow gallery
    /// <see cref="GalleryUiMetrics.ChromeScale"/> live (Ctrl+Alt+=/− and host monitor UI scale).
    /// Summary report uses same scale.
    /// </summary>
    internal sealed class VpbBusyChrome : MonoBehaviour
    {
        private static VpbBusyChrome s_Instance;

        private Canvas m_Canvas;
        private GameObject m_BannerGO;
        private RectTransform m_BannerRT;
        private Image m_BannerBg;
        private Text m_VersionText;
        private RectTransform m_VersionRT;
        private Text m_TitleText;
        private RectTransform m_TitleRT;
        private Text m_SubText;
        private RectTransform m_SubRT;
        private RectTransform m_BarBgRT;
        private RectTransform m_BarFillRT;
        private RectTransform m_BarStripRT;
        private Image m_BarBgImg;
        private Text m_ReportText;
        private RectTransform m_ReportRT;
        private Text m_ReportTextRight;
        private RectTransform m_ReportRightRT;
        private GameObject m_HeaderSepGO;
        private RectTransform m_HeaderSepRT;
        private GameObject m_ColSepGO;
        private RectTransform m_ColSepRT;
        private GameObject m_CancelButtonGO;
        private RectTransform m_CancelButtonRT;
        private GameObject m_OkButtonGO;
        private RectTransform m_OkButtonRT;

        private bool m_SummaryMode;
        private float m_ForwardProgress01;
        private float m_AppliedChromeScale = float.NaN;
        private int m_AppliedScreenWidth = -1;
        private bool m_ConfigSubscribed;
        private const float IndeterminateStripWidth01 = 0.28f;
        private const float IndeterminateCycleSec = 1.35f;

        // Design refs @ ChromeScale 1.0
        /// <summary>Progress banner width = Screen.width / φ (golden section of screen).</summary>
        private const float BannerWidthProgressPhi = 1.618f;
        private const float BannerWidthSummaryRef = 900f;
        /// <summary>Match gallery footer info row height (FontBody at ChromeScale 1).</summary>
        private const float BannerHeightProgressRef = GalleryUiDesignTokens.FooterInfoRowHeightRef;
        private const float BannerHeightSummaryRef = 268f;
        private const float BannerTopInsetRef = GalleryUiDesignTokens.ControlGapRef;
        /// <summary>Discrete 2px meter — not full-row underlay.</summary>
        private const float BarHeightRef = GalleryUiDesignTokens.HairGapRef;
        private const float ContentAboveBarGapRef = GalleryUiDesignTokens.TightGapRef;
        private const float TextRowPadTopRef = GalleryUiDesignTokens.HairGapRef;
        private const float TextRowSideInsetRef = GalleryUiDesignTokens.ControlGapRef;
        private const float CancelButtonWidthRef = 84f;
        private const float CancelButtonHeightRef = 28f;
        private const float VersionSlotWidthRef = 148f;
        private const float VersionTitleGapRef = GalleryUiDesignTokens.ControlGapRef;
        private const float CancelSlotWidthRef = 94f;
        private const float SummaryHeaderHeightRef = 40f;
        private const float SummaryFooterHeightRef = 48f;
        private const float SummaryPadHRef = GalleryUiDesignTokens.SectionGapRef;
        private const float SummaryColGapRef = GalleryUiDesignTokens.SectionGapRef;
        private const float SummaryColSepWidthRef = 1f;
        private const float OkButtonWidthRef = 100f;
        private const float OkButtonHeightRef = GalleryUiDesignTokens.ButtonSizeRef;
        private const float OkButtonBottomInsetRef = 12f;

        private static readonly Color BannerBgOpaque = new Color(0.06f, 0.06f, 0.08f, 1f);
        private static readonly Color CancelButtonColor = new Color(0.28f, 0.28f, 0.32f, 1f);
        private static readonly Color ProgressTrackColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color ProgressFillColor = new Color(0.42f, 0.80f, 0.95f, 0.92f);
        private static readonly Color ProgressStripColor = new Color(0.55f, 0.88f, 1f, 0.95f);
        private static readonly Color HeaderSepColor = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Color TitleColor = new Color(0.96f, 0.98f, 1f, 1f);
        private static readonly Color VersionColor = new Color(0.78f, 0.86f, 0.94f, 1f);
        private static readonly Color SubtitleColor = new Color(0.68f, 0.74f, 0.80f, 1f);
        private static readonly Color ReportBodyColor = new Color(0.88f, 0.90f, 0.93f, 1f);
        private static readonly Color OkButtonColor = new Color(0.18f, 0.42f, 0.55f, 1f);

        private float Scale
        {
            get
            {
                float s = m_AppliedChromeScale;
                if (float.IsNaN(s) || s <= 0f) return 1f;
                return s;
            }
        }

        private float ContentBottomReserve
        {
            get { return (BarHeightRef + ContentAboveBarGapRef) * Scale; }
        }

        public static void EnsureCreated()
        {
            if (s_Instance != null) return;

            var host = new GameObject("VPB_BusyChrome");
            UnityEngine.Object.DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<VpbBusyChrome>();
        }

        private void Awake()
        {
            CreateUI();
            SyncChromeScaleIfNeeded(true);
        }

        private void OnEnable()
        {
            TrySubscribeConfigChanged();
        }

        private void OnDisable()
        {
            TryUnsubscribeConfigChanged();
        }

        private void TrySubscribeConfigChanged()
        {
            if (m_ConfigSubscribed) return;
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.ConfigChanged += OnConfigChanged;
                m_ConfigSubscribed = true;
            }
            catch { }
        }

        private void TryUnsubscribeConfigChanged()
        {
            if (!m_ConfigSubscribed) return;
            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.ConfigChanged -= OnConfigChanged;
            }
            catch { }
            m_ConfigSubscribed = false;
        }

        private void OnConfigChanged()
        {
            SyncChromeScaleIfNeeded(true);
        }

        private void Update()
        {
            TrySubscribeConfigChanged();

            // Own hotkey path so scale works while busy chrome is up (shared frame guard).
            try { GalleryUiScaleHotkey.TryNudgeFromKeyboard(); } catch { }
            try { GalleryUiScaleHotkey.TickDeferredSave(); } catch { }

            SyncChromeScaleIfNeeded(false);

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
                ExitSummaryMode();
                if (m_BannerGO != null && m_BannerGO.activeSelf) m_BannerGO.SetActive(false);
                ResetProgressBarMotion();
                return;
            }

            if (snapshot.ShowSummary)
            {
                if (m_BannerGO != null && !m_BannerGO.activeSelf) m_BannerGO.SetActive(true);
                EnterSummaryMode(snapshot.Title, snapshot.SummaryText, snapshot.SummaryTextRight);
                PollSummaryDismissKeys();
                return;
            }

            ExitSummaryMode();

            if (m_BannerGO != null && !m_BannerGO.activeSelf) m_BannerGO.SetActive(true);

            if (m_TitleText != null) m_TitleText.text = snapshot.Title ?? string.Empty;
            if (m_SubText != null) m_SubText.text = snapshot.Subtitle ?? string.Empty;

            if (m_CancelButtonGO != null && !m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(true);
            ApplyTextRowInsets(true);

            SetMovingStripVisible(false);
            UpdateProgressBar(snapshot.Progress01);
        }

        private void PollSummaryDismissKeys()
        {
            if (Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Escape))
            {
                NativeTextureOnDemandCache.DismissSummary();
            }
        }

        private void ApplyActiveProgressSnapshot(VpbProgressService.DisplaySnapshot snapshot)
        {
            ExitSummaryMode();
            if (m_BannerGO != null && !m_BannerGO.activeSelf) m_BannerGO.SetActive(true);
            if (m_TitleText != null) m_TitleText.text = snapshot.Title ?? string.Empty;
            if (m_SubText != null) m_SubText.text = snapshot.Subtitle ?? string.Empty;

            bool showCancel = snapshot.Cancellable && !snapshot.Blocking;
            if (m_CancelButtonGO != null)
            {
                if (showCancel && !m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(true);
                if (!showCancel && m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(false);
            }
            ApplyTextRowInsets(showCancel);

            bool strip = snapshot.ShowMovingStrip;
            if (snapshot.Progress01 < 0f)
            {
                ClearProgressBarFill();
                SetMovingStripVisible(strip);
                if (strip) UpdateProgressBarStrip();
            }
            else
            {
                UpdateProgressBar(snapshot.Progress01);
                SetMovingStripVisible(strip);
                if (strip) UpdateProgressBarStrip();
            }
        }

        private void ResetProgressBarMotion()
        {
            m_ForwardProgress01 = 0f;
            SetMovingStripVisible(false);
            ClearProgressBarFill();
        }

        private static float ResolveChromeScale()
        {
            try
            {
                // Screen-space overlay — same host/pane factors as fixed desktop dock.
                float s = GalleryUiMetrics.Resolve(true).ChromeScale;
                if (s > 0f) return s;
            }
            catch { }
            return 1f;
        }

        private void SyncChromeScaleIfNeeded(bool force)
        {
            float s = ResolveChromeScale();
            int sw = Screen.width;
            if (!force
                && !float.IsNaN(m_AppliedChromeScale)
                && Mathf.Abs(m_AppliedChromeScale - s) < 0.0005f
                && m_AppliedScreenWidth == sw)
                return;
            m_AppliedScreenWidth = sw;
            ApplyChromeScale(s);
        }

        private void ApplyChromeScale(float s)
        {
            if (s <= 0f) s = 1f;
            m_AppliedChromeScale = s;

            ApplyScaledFonts(s);
            ApplyProgressTrackLayout();

            if (m_SummaryMode)
            {
                ApplySummaryBannerFrame();
                ApplySummaryHeaderLayout();
                ApplySummaryChromeLayout();
                if (m_ReportRT != null && m_ReportText != null && m_ReportText.gameObject.activeSelf)
                {
                    bool hasRight = m_ReportTextRight != null && m_ReportTextRight.gameObject.activeSelf;
                    if (hasRight) ApplyReportColumnAnchors(m_ReportRT, true);
                    else ApplyReportFullWidthAnchors(m_ReportRT);
                    if (hasRight) ApplyReportColumnAnchors(m_ReportRightRT, false);
                }
            }
            else
            {
                ApplyProgressBannerFrame();
                bool cancelOn = m_CancelButtonGO != null && m_CancelButtonGO.activeSelf;
                ApplyTextRowInsets(cancelOn);
            }
        }

        private void ApplyScaledFonts(float s)
        {
            // Same FontBodyRef + ApplyFont path as gallery chrome (size match).
            ApplyGalleryBodyFont(m_VersionText, s);
            if (m_VersionText != null) m_VersionText.fontStyle = FontStyle.Bold;

            ApplyGalleryBodyFont(m_TitleText, s);
            if (m_TitleText != null)
            {
                m_TitleText.fontStyle = m_SummaryMode ? FontStyle.Bold : FontStyle.Normal;
                if (m_SummaryMode) m_TitleText.alignment = TextAnchor.MiddleLeft;
            }

            ApplyGalleryBodyFont(m_SubText, s);
            ApplyGalleryBodyFont(m_ReportText, s);
            ApplyGalleryBodyFont(m_ReportTextRight, s);
            ApplyGalleryBodyFontOnButton(m_CancelButtonGO, s);
            ApplyGalleryBodyFontOnButton(m_OkButtonGO, s);
        }

        private static void ApplyGalleryBodyFont(Text txt, float scale)
        {
            if (txt == null) return;
            GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.FontBodyRef, scale, GalleryUiDesignTokens.FontMinRef);
        }

        private static void ApplyGalleryBodyFontOnButton(GameObject btn, float scale)
        {
            if (btn == null) return;
            try
            {
                var t = btn.GetComponentInChildren<Text>(true);
                ApplyGalleryBodyFont(t, scale);
            }
            catch { }
        }

        private void CreateUI()
        {
            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 6000;

            // Match gallery pane: ConstantPixelSize + dynamicPixelsPerUnit.
            // ChromeScale alone drives size — ScaleWithScreenSize was double-scaling vs gallery.
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4;

            gameObject.AddComponent<GraphicRaycaster>();

            m_BannerGO = new GameObject("Banner");
            m_BannerGO.transform.SetParent(transform, false);

            m_BannerRT = m_BannerGO.AddComponent<RectTransform>();
            m_BannerBg = m_BannerGO.AddComponent<Image>();
            m_BannerBg.color = BannerBgOpaque;
            m_BannerBg.raycastTarget = false;

            // Thin bottom progress line (discrete meter under the single text row).
            var barBgGO = new GameObject("BarBg");
            barBgGO.transform.SetParent(m_BannerGO.transform, false);
            m_BarBgRT = barBgGO.AddComponent<RectTransform>();
            m_BarBgImg = barBgGO.AddComponent<Image>();
            m_BarBgImg.color = ProgressTrackColor;
            m_BarBgImg.raycastTarget = false;

            var barFillGO = new GameObject("BarFill");
            barFillGO.transform.SetParent(barBgGO.transform, false);
            m_BarFillRT = barFillGO.AddComponent<RectTransform>();
            m_BarFillRT.anchorMin = new Vector2(0f, 0f);
            m_BarFillRT.anchorMax = new Vector2(0f, 1f);
            m_BarFillRT.pivot = new Vector2(0f, 0.5f);
            m_BarFillRT.anchoredPosition = Vector2.zero;
            m_BarFillRT.sizeDelta = new Vector2(0f, 0f);
            var barFillImg = barFillGO.AddComponent<Image>();
            barFillImg.color = ProgressFillColor;
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
            barStripImg.color = ProgressStripColor;
            barStripImg.raycastTarget = false;
            barStripGO.SetActive(false);

            var versionGO = new GameObject("Version");
            versionGO.transform.SetParent(m_BannerGO.transform, false);
            m_VersionText = versionGO.AddComponent<Text>();
            m_VersionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_VersionText.fontStyle = FontStyle.Bold;
            m_VersionText.color = VersionColor;
            m_VersionText.alignment = TextAnchor.MiddleLeft;
            m_VersionText.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_VersionText.verticalOverflow = VerticalWrapMode.Truncate;
            m_VersionText.raycastTarget = false;
            m_VersionText.text = FormatBannerVersionLabel();
            m_VersionRT = versionGO.GetComponent<RectTransform>();

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(m_BannerGO.transform, false);
            m_TitleText = titleGO.AddComponent<Text>();
            m_TitleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_TitleText.color = TitleColor;
            m_TitleText.alignment = TextAnchor.MiddleLeft;
            m_TitleText.supportRichText = true;
            m_TitleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_TitleText.verticalOverflow = VerticalWrapMode.Truncate;
            m_TitleText.raycastTarget = false;
            m_TitleRT = titleGO.GetComponent<RectTransform>();

            var subGO = new GameObject("Sub");
            subGO.transform.SetParent(m_BannerGO.transform, false);
            m_SubText = subGO.AddComponent<Text>();
            m_SubText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_SubText.color = SubtitleColor;
            m_SubText.alignment = TextAnchor.MiddleRight;
            m_SubText.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_SubText.verticalOverflow = VerticalWrapMode.Truncate;
            m_SubText.raycastTarget = false;
            m_SubRT = subGO.GetComponent<RectTransform>();

            var headerSepGO = new GameObject("HeaderSep");
            headerSepGO.transform.SetParent(m_BannerGO.transform, false);
            m_HeaderSepGO = headerSepGO;
            m_HeaderSepRT = headerSepGO.AddComponent<RectTransform>();
            var sepImg = headerSepGO.AddComponent<Image>();
            sepImg.color = HeaderSepColor;
            sepImg.raycastTarget = false;
            headerSepGO.SetActive(false);

            m_ReportText = CreateReportColumn("ReportLeft", out m_ReportRT);
            m_ReportText.gameObject.SetActive(false);

            m_ReportTextRight = CreateReportColumn("ReportRight", out m_ReportRightRT);
            m_ReportTextRight.gameObject.SetActive(false);

            var colSepGO = new GameObject("ColSep");
            colSepGO.transform.SetParent(m_BannerGO.transform, false);
            m_ColSepGO = colSepGO;
            m_ColSepRT = colSepGO.AddComponent<RectTransform>();
            var colSepImg = colSepGO.AddComponent<Image>();
            colSepImg.color = HeaderSepColor;
            colSepImg.raycastTarget = false;
            colSepGO.SetActive(false);

            m_CancelButtonGO = UI.CreateUIButton(
                m_BannerGO, CancelButtonWidthRef, CancelButtonHeightRef, "Cancel",
                GalleryUiDesignTokens.FontBodyRef, 0, 0, AnchorPresets.middleCenter,
                () => { VpbProgressService.RequestCancelActiveJob(); });
            m_CancelButtonRT = m_CancelButtonGO.GetComponent<RectTransform>();
            try
            {
                var img = m_CancelButtonGO.GetComponent<Image>();
                if (img != null) img.color = CancelButtonColor;
            }
            catch { }
            m_CancelButtonGO.SetActive(false);

            m_OkButtonGO = UI.CreateUIButton(
                m_BannerGO, OkButtonWidthRef, OkButtonHeightRef, "OK",
                GalleryUiDesignTokens.FontBodyRef, 0, 0, AnchorPresets.middleCenter,
                () => { NativeTextureOnDemandCache.DismissSummary(); });
            m_OkButtonRT = m_OkButtonGO.GetComponent<RectTransform>();
            try
            {
                var okImg = m_OkButtonGO.GetComponent<Image>();
                if (okImg != null) okImg.color = OkButtonColor;
            }
            catch { }
            m_OkButtonGO.SetActive(false);

            m_BannerGO.SetActive(false);
        }

        private Text CreateReportColumn(string name, out RectTransform rt)
        {
            var reportGO = new GameObject(name);
            reportGO.transform.SetParent(m_BannerGO.transform, false);
            var text = reportGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.lineSpacing = 1.12f;
            text.color = ReportBodyColor;
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            rt = reportGO.GetComponent<RectTransform>();
            return text;
        }

        private void ApplyProgressTrackLayout()
        {
            if (m_BarBgRT == null) return;
            float h = BarHeightRef * Scale;
            m_BarBgRT.anchorMin = new Vector2(0f, 0f);
            m_BarBgRT.anchorMax = new Vector2(1f, 0f);
            m_BarBgRT.pivot = new Vector2(0.5f, 0f);
            m_BarBgRT.anchoredPosition = Vector2.zero;
            m_BarBgRT.sizeDelta = new Vector2(0f, h);
        }

        private void ApplyReportColumnAnchors(RectTransform rt, bool leftColumn)
        {
            if (rt == null) return;
            float s = Scale;
            float halfGap = SummaryColGapRef * s * 0.5f;
            float top = -(SummaryHeaderHeightRef * s + 10f * s);
            float bottom = SummaryFooterHeightRef * s;
            float padH = SummaryPadHRef * s;
            if (leftColumn)
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(padH, bottom);
                rt.offsetMax = new Vector2(-halfGap, top);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = new Vector2(halfGap, bottom);
                rt.offsetMax = new Vector2(-padH, top);
            }
        }

        private void ApplyReportFullWidthAnchors(RectTransform rt)
        {
            if (rt == null) return;
            float s = Scale;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(SummaryPadHRef * s, SummaryFooterHeightRef * s);
            rt.offsetMax = new Vector2(-SummaryPadHRef * s, -(SummaryHeaderHeightRef * s + 10f * s));
        }

        private static string FormatBannerVersionLabel()
        {
            return "VPB " + PluginVersionInfo.BaseVersion
                + " · " + PluginVersionInfo.BuildVersion;
        }

        private void ApplyProgressBannerFrame()
        {
            if (m_BannerRT == null) return;
            float s = Scale;
            // Golden section of screen: W/φ — longer than fixed 720, still not full-bleed.
            float w = Screen.width / BannerWidthProgressPhi;
            if (w < 1f) w = 1f;
            m_BannerRT.anchorMin = new Vector2(0.5f, 1f);
            m_BannerRT.anchorMax = new Vector2(0.5f, 1f);
            m_BannerRT.pivot = new Vector2(0.5f, 1f);
            m_BannerRT.anchoredPosition = new Vector2(0f, -BannerTopInsetRef * s);
            m_BannerRT.sizeDelta = new Vector2(w, BannerHeightProgressRef * s);
            if (m_BannerBg != null) m_BannerBg.color = BannerBgOpaque;
        }

        private void ApplySummaryBannerFrame()
        {
            if (m_BannerRT == null) return;
            float s = Scale;
            m_BannerRT.anchorMin = new Vector2(0.5f, 1f);
            m_BannerRT.anchorMax = new Vector2(0.5f, 1f);
            m_BannerRT.pivot = new Vector2(0.5f, 1f);
            m_BannerRT.anchoredPosition = new Vector2(0f, -BannerTopInsetRef * s);
            m_BannerRT.sizeDelta = new Vector2(BannerWidthSummaryRef * s, BannerHeightSummaryRef * s);
            if (m_BannerBg != null) m_BannerBg.color = BannerBgOpaque;
        }

        private void ApplySummaryChromeLayout()
        {
            float s = Scale;
            float padH = SummaryPadHRef * s;
            float headerH = SummaryHeaderHeightRef * s;
            float footerH = SummaryFooterHeightRef * s;

            if (m_HeaderSepRT != null)
            {
                m_HeaderSepRT.anchorMin = new Vector2(0f, 1f);
                m_HeaderSepRT.anchorMax = new Vector2(1f, 1f);
                m_HeaderSepRT.pivot = new Vector2(0.5f, 1f);
                m_HeaderSepRT.anchoredPosition = new Vector2(0f, -headerH);
                m_HeaderSepRT.sizeDelta = new Vector2(-(padH * 2f), 1f);
            }

            if (m_ColSepRT != null)
            {
                float sepW = SummaryColSepWidthRef * s;
                m_ColSepRT.anchorMin = new Vector2(0.5f, 0f);
                m_ColSepRT.anchorMax = new Vector2(0.5f, 1f);
                m_ColSepRT.pivot = new Vector2(0.5f, 0.5f);
                m_ColSepRT.offsetMin = new Vector2(-sepW * 0.5f, footerH + 6f * s);
                m_ColSepRT.offsetMax = new Vector2(sepW * 0.5f, -(headerH + 12f * s));
            }

            if (m_OkButtonRT != null)
            {
                m_OkButtonRT.anchorMin = new Vector2(1f, 0f);
                m_OkButtonRT.anchorMax = new Vector2(1f, 0f);
                m_OkButtonRT.pivot = new Vector2(1f, 0f);
                m_OkButtonRT.sizeDelta = new Vector2(OkButtonWidthRef * s, OkButtonHeightRef * s);
                m_OkButtonRT.anchoredPosition = new Vector2(-padH, OkButtonBottomInsetRef * s);
            }
        }

        /// <summary>Cancel centered in content band above the thin progress line.</summary>
        private void ApplyCancelButtonLayout()
        {
            if (m_CancelButtonRT == null) return;
            float s = Scale;
            float contentBottom = ContentBottomReserve;
            float yFromBannerMid = contentBottom * 0.5f;
            m_CancelButtonRT.anchorMin = new Vector2(0f, 0.5f);
            m_CancelButtonRT.anchorMax = new Vector2(0f, 0.5f);
            m_CancelButtonRT.pivot = new Vector2(0f, 0.5f);
            m_CancelButtonRT.sizeDelta = new Vector2(CancelButtonWidthRef * s, CancelButtonHeightRef * s);
            m_CancelButtonRT.anchoredPosition = new Vector2(TextRowSideInsetRef * s, yFromBannerMid);
        }

        private void ApplyTextRowInsets(bool reserveCancelSlot)
        {
            float s = Scale;
            float cancelReserve = reserveCancelSlot ? CancelSlotWidthRef * s : 0f;
            float side = TextRowSideInsetRef * s;
            float chromeLeft = side + cancelReserve;
            float bottom = ContentBottomReserve;
            float topPad = TextRowPadTopRef * s;
            float versionW = VersionSlotWidthRef * s;
            float versionGap = VersionTitleGapRef * s;

            if (m_VersionRT != null)
            {
                m_VersionRT.anchorMin = new Vector2(0f, 0f);
                m_VersionRT.anchorMax = new Vector2(0f, 1f);
                m_VersionRT.pivot = new Vector2(0f, 0.5f);
                m_VersionRT.anchoredPosition = new Vector2(chromeLeft, (bottom - topPad) * 0.5f);
                m_VersionRT.sizeDelta = new Vector2(versionW, -(bottom + topPad));
                if (m_VersionText != null && !m_VersionText.gameObject.activeSelf)
                    m_VersionText.gameObject.SetActive(true);
            }

            if (m_TitleRT == null) return;
            m_TitleRT.anchorMin = new Vector2(0f, 0f);
            m_TitleRT.anchorMax = new Vector2(0.55f, 1f);
            float titleLeft = chromeLeft + versionW + versionGap;
            m_TitleRT.offsetMin = new Vector2(titleLeft, bottom);
            m_TitleRT.offsetMax = new Vector2(-6f * s, -topPad);

            if (m_SubRT != null)
            {
                m_SubRT.anchorMin = new Vector2(0.45f, 0f);
                m_SubRT.anchorMax = new Vector2(1f, 1f);
                m_SubRT.offsetMin = new Vector2(6f * s, bottom);
                m_SubRT.offsetMax = new Vector2(-side, -topPad);
            }

            if (reserveCancelSlot) ApplyCancelButtonLayout();
        }

        private void ApplySummaryHeaderLayout()
        {
            float s = Scale;
            float padH = SummaryPadHRef * s;
            float headerH = SummaryHeaderHeightRef * s;
            float versionW = VersionSlotWidthRef * s;
            float versionGap = VersionTitleGapRef * s;

            if (m_VersionRT != null)
            {
                m_VersionRT.anchorMin = new Vector2(0f, 1f);
                m_VersionRT.anchorMax = new Vector2(0f, 1f);
                m_VersionRT.pivot = new Vector2(0f, 1f);
                m_VersionRT.anchoredPosition = new Vector2(padH, 0f);
                m_VersionRT.sizeDelta = new Vector2(versionW, headerH);
            }

            if (m_TitleRT != null)
            {
                m_TitleRT.anchorMin = new Vector2(0f, 1f);
                m_TitleRT.anchorMax = new Vector2(1f, 1f);
                m_TitleRT.pivot = new Vector2(0.5f, 1f);
                m_TitleRT.offsetMin = new Vector2(padH + versionW + versionGap, -headerH);
                m_TitleRT.offsetMax = new Vector2(-padH, 0f);
            }

            // Do not ApplyFont here — it rewrites pivots and undoes version left inset.
            if (m_TitleText != null)
            {
                m_TitleText.fontStyle = FontStyle.Bold;
                m_TitleText.alignment = TextAnchor.MiddleLeft;
            }
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

        private void EnterSummaryMode(string title, string summaryLeft, string summaryRight)
        {
            bool hasRight = !string.IsNullOrEmpty(summaryRight);

            if (!m_SummaryMode)
            {
                m_SummaryMode = true;
                if (m_BannerBg != null) m_BannerBg.color = BannerBgOpaque;
                if (m_SubText != null) m_SubText.gameObject.SetActive(false);
                if (m_BarBgRT != null) m_BarBgRT.gameObject.SetActive(false);
                if (m_HeaderSepGO != null) m_HeaderSepGO.SetActive(true);
                if (m_OkButtonGO != null && !m_OkButtonGO.activeSelf) m_OkButtonGO.SetActive(true);
                if (m_CancelButtonGO != null && m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(false);
                // Fonts before layout: ApplyFont may reset Text pivots (center when
                // FontExtraScale≈1); summary header needs left-top version pivot after.
                ApplyScaledFonts(Scale);
                ApplySummaryBannerFrame();
                ApplySummaryHeaderLayout();
                ApplySummaryChromeLayout();
            }

            if (m_TitleText != null) m_TitleText.text = title ?? string.Empty;

            if (m_ReportText != null)
            {
                m_ReportText.text = summaryLeft ?? string.Empty;
                m_ReportText.verticalOverflow = VerticalWrapMode.Overflow;
                if (!m_ReportText.gameObject.activeSelf) m_ReportText.gameObject.SetActive(true);
                if (hasRight) ApplyReportColumnAnchors(m_ReportRT, true);
                else ApplyReportFullWidthAnchors(m_ReportRT);
            }

            if (m_ReportTextRight != null)
            {
                if (hasRight)
                {
                    m_ReportTextRight.text = summaryRight;
                    m_ReportTextRight.verticalOverflow = VerticalWrapMode.Overflow;
                    ApplyReportColumnAnchors(m_ReportRightRT, false);
                    if (!m_ReportTextRight.gameObject.activeSelf) m_ReportTextRight.gameObject.SetActive(true);
                    if (m_ColSepGO != null && !m_ColSepGO.activeSelf) m_ColSepGO.SetActive(true);
                }
                else
                {
                    if (m_ReportTextRight.gameObject.activeSelf) m_ReportTextRight.gameObject.SetActive(false);
                    if (m_ColSepGO != null && m_ColSepGO.activeSelf) m_ColSepGO.SetActive(false);
                }
            }
        }

        private void ExitSummaryMode()
        {
            if (!m_SummaryMode) return;
            m_SummaryMode = false;

            if (m_SubText != null && !m_SubText.gameObject.activeSelf) m_SubText.gameObject.SetActive(true);
            if (m_BarBgRT != null && !m_BarBgRT.gameObject.activeSelf) m_BarBgRT.gameObject.SetActive(true);
            if (m_HeaderSepGO != null && m_HeaderSepGO.activeSelf) m_HeaderSepGO.SetActive(false);
            if (m_ColSepGO != null && m_ColSepGO.activeSelf) m_ColSepGO.SetActive(false);

            if (m_ReportText != null && m_ReportText.gameObject.activeSelf) m_ReportText.gameObject.SetActive(false);
            if (m_ReportText != null) m_ReportText.verticalOverflow = VerticalWrapMode.Truncate;
            if (m_ReportTextRight != null && m_ReportTextRight.gameObject.activeSelf) m_ReportTextRight.gameObject.SetActive(false);
            if (m_ReportTextRight != null) m_ReportTextRight.verticalOverflow = VerticalWrapMode.Truncate;
            if (m_OkButtonGO != null && m_OkButtonGO.activeSelf) m_OkButtonGO.SetActive(false);

            if (m_CancelButtonGO != null && !m_CancelButtonGO.activeSelf) m_CancelButtonGO.SetActive(true);
            ApplyProgressBannerFrame();
            ApplyProgressTrackLayout();
            ApplyScaledFonts(Scale);
            ApplyTextRowInsets(true);
        }
    }
}
