using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private const float TitleBarResponsiveGap = 6f;
        /// <summary>Below this width (× scale), title search becomes square icon + popup field.</summary>
        private const float TitleSearchCollapseWidthPx = 72f;

        /// <summary>
        /// Sizes category quick-switch and main search so they do not overlap Language / sort row.
        /// When search cannot fit, shows compact square + dropdown field.
        /// </summary>
        private void ApplyTitleBarResponsiveLayout(float paneScale)
        {
            if (titleSearchInput == null || backgroundBoxGO == null) return;
            float s = paneScale <= 0f ? 1f : paneScale;

            RectTransform titleBarRT = titleSearchInput.transform.parent as RectTransform;
            if (titleBarRT == null) return;

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(titleBarRT);
                Canvas.ForceUpdateCanvases();
            }
            catch { }

            // Title-bar controls use anchoredPosition in *title bar* space — halfWidth must match.
            float W = titleBarRT.rect.width;
            if (W < 8f) return;

            float halfW = W * 0.5f;
            float gap = TitleBarResponsiveGap * s;
            float searchCX = -40f * s;

            bool flushLeft = CategoryQuickSwitchFlushLeftEdge();
            float leftInset = flushLeft ? 0f : 60f * s;

            // Row order: [ Category ][ Lang ][ Settings ][ QF ][ Search ][ Az ][ ↑ ][ ★ ][ ⟳ ] …
            // Category grows from left; must stop before Language (first icon right of category).
            float langBtnHalf = 20f * s;
            float langLeftX = -276f * s - langBtnHalf;
            float maxCatRightAllowed = langLeftX - gap;

            bool catVisible = _categoryQuickChromeRootGO != null && _categoryQuickChromeRootGO.activeSelf;

            float catW = 0f;
            if (catVisible && _categoryQuickChromeRootRT != null)
            {
                float catLeftX = -halfW + leftInset;
                float maxCatW = maxCatRightAllowed - catLeftX;
                catW = Mathf.Clamp(maxCatW, 0f, 380f * s);
                _categoryQuickChromeRootRT.sizeDelta = new Vector2(catW, 44f * s);
            }

            // Search sits immediately right of QF (P). Bound left by QF right + gap.
            float qfHalf = 20f * s;
            float qfRightX = -184f * s + qfHalf;
            float leftSearchEdgeMin = qfRightX + gap;

            // Right edge: first sort control (Az) left edge, from rect if present.
            float sortLeftX = 108f * s - 17.5f * s;
            if (_titleBarFileSortTypeBtnRT != null)
            {
                try
                {
                    Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(titleBarRT, _titleBarFileSortTypeBtnRT);
                    sortLeftX = b.min.x;
                }
                catch { }
            }
            float rightSearchEdgeMax = sortLeftX - gap;

            // Center search at searchCX: width <= 2*(center - leftEdgeMin) and <= 2*(rightEdgeMax - center)
            float maxSearchByLeft = 2f * (searchCX - leftSearchEdgeMin);
            float maxSearchByRight = 2f * (rightSearchEdgeMax - searchCX);
            float maxSearchW = Mathf.Min(maxSearchByLeft, maxSearchByRight, 240f * s);
            maxSearchW = Mathf.Max(0f, maxSearchW);

            bool useCompact = maxSearchW < TitleSearchCollapseWidthPx * s - 0.5f;

            RectTransform searchRT = titleSearchInput.GetComponent<RectTransform>();

            if (useCompact)
            {
                if (titleSearchInput.gameObject.activeSelf)
                    titleSearchInput.gameObject.SetActive(false);
                if (_titleSearchCompactGO != null)
                {
                    _titleSearchCompactGO.SetActive(true);
                    if (_titleSearchCompactRT != null)
                    {
                        _titleSearchCompactRT.anchoredPosition = new Vector2(-40f * s, 0f);
                        _titleSearchCompactRT.sizeDelta = new Vector2(40f * s, 40f * s);
                    }
                }
            }
            else
            {
                CloseTitleSearchPopup();
                if (_titleSearchCompactGO != null)
                    _titleSearchCompactGO.SetActive(false);
                titleSearchInput.gameObject.SetActive(true);
                float w = Mathf.Max(40f * s, maxSearchW);
                searchRT.sizeDelta = new Vector2(w, 40f * s);
                searchRT.anchoredPosition = new Vector2(searchCX, 0f);
            }
        }

        private void SetupTitleSearchCompactControl(GameObject titleBarGO)
        {
            if (titleBarGO == null) return;
            _titleSearchCompactGO = UI.CreateUIButton(titleBarGO, 40, 40, "", 18, 0, 0, AnchorPresets.middleCenter, () => OpenTitleSearchPopup());
            _titleSearchCompactGO.name = "TitleSearchCompact";
            _titleSearchCompactGO.SetActive(false);
            RectTransform crt = _titleSearchCompactGO.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(-40f, 0f);
            crt.sizeDelta = new Vector2(40f, 40f);
            var img = _titleSearchCompactGO.GetComponent<Image>();
            if (img != null) img.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            try
            {
                var sp = UI.LoadIconSprite("vpb_icons/search.png", new Color(0.75f, 0.75f, 0.75f, 1f));
                if (sp != null) UI.AddIconToButton(_titleSearchCompactGO, sp, padding: 8f);
            }
            catch { }
            _titleSearchCompactRT = crt;
            try { AddTooltip(_titleSearchCompactGO, "gallery.search.main", "Search..."); } catch { }
        }

        private void EnsureTitleSearchPopupBuilt()
        {
            if (_titleSearchPopupRootGO != null || backgroundBoxGO == null || titleSearchInput == null) return;

            _titleSearchPopupRootGO = new GameObject("TitleSearchPopupBackdrop");
            _titleSearchPopupRootGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRT = _titleSearchPopupRootGO.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;
            Image rootImg = _titleSearchPopupRootGO.AddComponent<Image>();
            rootImg.color = new Color(0f, 0f, 0f, 0.001f);
            rootImg.raycastTarget = true;
            Button rootBtn = _titleSearchPopupRootGO.AddComponent<Button>();
            rootBtn.transition = Selectable.Transition.None;
            rootBtn.targetGraphic = rootImg;
            rootBtn.onClick.AddListener(CloseTitleSearchPopup);

            GameObject panel = new GameObject("TitleSearchPopupPanel");
            panel.transform.SetParent(_titleSearchPopupRootGO.transform, false);
            _titleSearchPopupPanelRT = panel.AddComponent<RectTransform>();
            _titleSearchPopupPanelRT.anchorMin = new Vector2(0.5f, 1f);
            _titleSearchPopupPanelRT.anchorMax = new Vector2(0.5f, 1f);
            _titleSearchPopupPanelRT.pivot = new Vector2(0.5f, 1f);
            Image pbg = panel.AddComponent<Image>();
            pbg.color = new Color(0.14f, 0.14f, 0.16f, 1f);
            pbg.raycastTarget = true;

            float w0 = 320f;
            _titleSearchPopupField = CreateSearchInput(panel, w0, (val) =>
            {
                if (_suppressTitleBarSearchValueChanged) return;
                _titleBarSearchOnValueChanged?.Invoke(val);
            });
            RectTransform ifrt = _titleSearchPopupField.GetComponent<RectTransform>();
            ifrt.anchorMin = new Vector2(0.5f, 0.5f);
            ifrt.anchorMax = new Vector2(0.5f, 0.5f);
            ifrt.pivot = new Vector2(0.5f, 0.5f);
            ifrt.anchoredPosition = Vector2.zero;

            _titleSearchPopupRootGO.SetActive(false);
        }

        private void OpenTitleSearchPopup()
        {
            if (titleSearchInput == null || backgroundBoxGO == null) return;
            EnsureTitleSearchPopupBuilt();
            if (_titleSearchPopupRootGO == null || _titleSearchPopupField == null || _titleSearchPopupPanelRT == null) return;

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            RectTransform bgRT = backgroundBoxGO.GetComponent<RectTransform>();
            float bw = bgRT != null ? bgRT.rect.width : 600f;
            float pw = Mathf.Clamp(bw - 24f * s, 160f * s, 560f * s);

            _titleSearchPopupPanelRT.sizeDelta = new Vector2(pw, 44f * s + 10f);
            _titleSearchPopupPanelRT.anchoredPosition = new Vector2(0f, -70f * s - 6f);

            RectTransform ifrt = _titleSearchPopupField.GetComponent<RectTransform>();
            ifrt.sizeDelta = new Vector2(pw - 12f * s, 40f * s);

            string t = titleSearchInput.text ?? "";
            try
            {
                _suppressTitleBarSearchValueChanged = true;
                _titleSearchPopupField.text = t;
            }
            finally { _suppressTitleBarSearchValueChanged = false; }

            _titleSearchPopupRootGO.transform.SetAsLastSibling();
            _titleSearchPopupRootGO.SetActive(true);
            _titleSearchPopupOpen = true;

            try { _titleSearchPopupField.ActivateInputField(); } catch { }
            try { _titleSearchPopupField.MoveTextEnd(false); } catch { }
        }

        private void CloseTitleSearchPopup()
        {
            if (!_titleSearchPopupOpen) return;
            _titleSearchPopupOpen = false;
            if (_titleSearchPopupRootGO != null)
                _titleSearchPopupRootGO.SetActive(false);

            if (_titleSearchPopupField != null && titleSearchInput != null && _titleBarSearchOnValueChanged != null)
            {
                try
                {
                    SetTitleSearchInputTextWithoutNotify(titleSearchInput, _titleSearchPopupField.text ?? "", _titleBarSearchOnValueChanged);
                }
                catch { }
            }
        }
    }
}
