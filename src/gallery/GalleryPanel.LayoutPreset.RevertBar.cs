using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const float LayoutRevertBarHeightRef = 34f;
        private const float LayoutRevertBarWidthRef = 320f;
        private const float LayoutRevertBarBottomPadRef = 54f;

        private GameObject _layoutRevertBarGO;
        private Text _layoutRevertBarLabel;
        private float _layoutRevertBarUntil;
        private int _layoutRevertBarLastSecond = -1;
        private string _layoutRevertBarName = "";

        /// <summary>Undo window after an apply. Nothing about a layout change should feel one-way.</summary>
        private void ShowLayoutRevertBar(string presetName)
        {
            float seconds = 8f;
            try
            {
                if (VPBConfig.Instance != null)
                    seconds = Mathf.Clamp(VPBConfig.Instance.LayoutPresetRevertBarSeconds, 2f, 30f);
            }
            catch { }

            _layoutRevertBarName = presetName ?? "";
            _layoutRevertBarUntil = Time.unscaledTime + seconds;
            _layoutRevertBarLastSecond = -1;

            EnsureLayoutRevertBar();
            if (_layoutRevertBarGO == null) return;
            LayoutRevertBarPlace();
            _layoutRevertBarGO.SetActive(true);
            try { _layoutRevertBarGO.transform.SetAsLastSibling(); } catch { }
        }

        internal void HideLayoutRevertBar()
        {
            _layoutRevertBarUntil = 0f;
            if (_layoutRevertBarGO != null && _layoutRevertBarGO.activeSelf)
                _layoutRevertBarGO.SetActive(false);
        }

        private void EnsureLayoutRevertBar()
        {
            if (_layoutRevertBarGO != null) return;
            if (backgroundBoxGO == null) return;

            float s = ChromeScale > 0f ? ChromeScale : 1f;
            int font = GalleryUiDesignTokens.FontBodyRef;
            float h = LayoutRevertBarHeightRef * s;

            _layoutRevertBarGO = UI.CreateChildRT(backgroundBoxGO, "LayoutRevertBar", AnchorPresets.bottomMiddle,
                new Vector2(LayoutRevertBarWidthRef * s, h), Vector2.zero);
            UI.AddImage(_layoutRevertBarGO, UI.ChromeDarker);
            UI.AddHLG(_layoutRevertBarGO, spacing: UI.GapControl(s), padding: UI.PadFloatFooter(s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            _layoutRevertBarLabel = UI.CreateLabel(_layoutRevertBarGO, "", font, Color.white,
                TextAnchor.MiddleLeft, raycastTarget: false, name: "Label");
            GalleryUiMetrics.ApplyFont(_layoutRevertBarLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(_layoutRevertBarLabel.gameObject, flexibleWidth: 1f, minWidth: 40f * s);

            GameObject revertBtn = UI.CreateChromeLayoutButton(
                _layoutRevertBarGO.transform, 84f * s, GalleryUiDesignTokens.ButtonSizeRef * s,
                VPBTranslation.T("gallery.layout_preset.revert", "Revert"),
                font, UI.AccentRed, RevertLayoutFromBar);
            if (revertBtn != null) revertBtn.name = "RevertBtn";

            GameObject dismissBtn = UI.CreateFloatChromeIconButton(
                _layoutRevertBarGO.transform, GalleryUiDesignTokens.ButtonSizeRef * s, "x",
                GalleryUiColorTokens.ChromeIconWell, HideLayoutRevertBar);
            if (dismissBtn != null) dismissBtn.name = "DismissBtn";

            _layoutRevertBarGO.SetActive(false);
        }

        private void LayoutRevertBarPlace()
        {
            if (_layoutRevertBarGO == null) return;
            RectTransform rt = _layoutRevertBarGO.GetComponent<RectTransform>();
            if (rt == null) return;

            float s = ChromeScale > 0f ? ChromeScale : 1f;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(LayoutRevertBarWidthRef * s, LayoutRevertBarHeightRef * s);
            rt.anchoredPosition = new Vector2(0f, LayoutRevertBarBottomPadRef * s);
        }

        private void RevertLayoutFromBar()
        {
            HideLayoutRevertBar();
            try { RevertLayoutToSnapshot(); } catch { }
        }

        /// <summary>
        /// One float compare when idle; the countdown text is written only when the whole second
        /// changes, so an open bar costs nothing per frame.
        /// </summary>
        private void TickLayoutRevertBar()
        {
            if (_layoutRevertBarUntil <= 0f) return;

            float remaining = _layoutRevertBarUntil - Time.unscaledTime;
            if (remaining <= 0f)
            {
                HideLayoutRevertBar();
                return;
            }

            int whole = Mathf.CeilToInt(remaining);
            if (whole == _layoutRevertBarLastSecond) return;
            _layoutRevertBarLastSecond = whole;

            if (_layoutRevertBarLabel == null) return;
            string baseText = string.IsNullOrEmpty(_layoutRevertBarName)
                ? VPBTranslation.T("gallery.layout_preset.applied_generic", "Layout applied")
                : string.Format(VPBTranslation.T("gallery.layout_preset.applied", "Layout \"{0}\" applied"), _layoutRevertBarName);
            _layoutRevertBarLabel.text = baseText + "  " + whole + "s";
        }
    }
}
