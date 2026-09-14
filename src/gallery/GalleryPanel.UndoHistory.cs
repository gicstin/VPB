using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const int UndoHistoryMaxRows = 12;

        private GameObject _undoHistoryMenuGO;
        private bool _undoHistoryOpen;
        private bool _undoStepsRunning;
        private Text _undoHistoryHeaderText;
        private string _undoHistoryHeaderIdle = "";
        private readonly List<Image> _undoHistoryRowImgs = new List<Image>(UndoHistoryMaxRows);
        private readonly List<string> _undoHistoryLabels = new List<string>(UndoHistoryMaxRows);

        private void EnsureUndoHistoryChrome()
        {
            if (_undoHistoryMenuGO != null) return;
            if (backgroundBoxGO == null) return;

            _undoHistoryMenuGO = UI.CreatePopupMenuRoot(backgroundBoxGO, "UndoHistoryMenu", CloseUndoHistoryMenu);
            _undoHistoryMenuGO.SetActive(false);

            UI.CreatePopupMenuPanel(
                _undoHistoryMenuGO, "UndoHistoryPanel",
                AnchorPresets.bottomMiddle,
                new Vector2(GalleryUiDesignTokens.OverflowMenuPanelWidthRef, 50f),
                new Vector2(0f, GalleryUiDesignTokens.FooterBarHeightRef
                    + GalleryUiDesignTokens.FooterInfoRowHeightRef
                    + GalleryUiDesignTokens.PopupMenuAnchorGapRef));
        }

        private void CollectUndoHistoryLabels(List<string> into)
        {
            into.Clear();
            if (undoLabelStack == null || undoLabelStack.Count == 0) return;
            string[] topFirst = undoLabelStack.ToArray();
            for (int i = 0; i < topFirst.Length; i++)
                into.Add(string.IsNullOrEmpty(topFirst[i])
                    ? VPBTranslation.T("gallery.undo.default_label", "Change")
                    : topFirst[i]);
        }

        private void RebuildUndoHistoryRows(Transform panel)
        {
            if (panel == null) return;
            UI.DestroyAllChildren(panel);
            _undoHistoryRowImgs.Clear();
            _undoHistoryHeaderText = null;

            CollectUndoHistoryLabels(_undoHistoryLabels);

            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            GalleryModalTypography type = new GalleryModalTypography(s);
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            float headerH = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;

            _undoHistoryHeaderIdle = _undoHistoryLabels.Count > 0
                ? string.Format(
                    VPBTranslation.T("gallery.undo.history_header", "Undo history ({0}) — pick how far back"),
                    _undoHistoryLabels.Count)
                : VPBTranslation.T("gallery.undo.history_empty", "Nothing to undo");

            GameObject hdr = new GameObject("UndoHistoryHeader");
            hdr.transform.SetParent(panel, false);
            LayoutElement hle = hdr.AddComponent<LayoutElement>();
            hle.preferredHeight = headerH;
            hle.minHeight = headerH;
            hle.flexibleWidth = 1f;
            hle.flexibleHeight = 0f;
            _undoHistoryHeaderText = UI.CreateLabel(
                hdr,
                _undoHistoryHeaderIdle,
                type.Caption,
                UI.PopupMutedText,
                TextAnchor.MiddleLeft,
                HorizontalWrapMode.Overflow,
                VerticalWrapMode.Truncate,
                raycastTarget: false,
                name: "Text");
            GalleryUiMetrics.ApplyFont(_undoHistoryHeaderText, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyPopupMenuRowTextPadding(_undoHistoryHeaderText, s);

            int shown = _undoHistoryLabels.Count < UndoHistoryMaxRows
                ? _undoHistoryLabels.Count
                : UndoHistoryMaxRows;

            for (int i = 0; i < shown; i++)
            {
                int steps = i + 1;
                string rowLabel = steps + "   " + _undoHistoryLabels[i];
                GameObject row = UI.CreateChromeLayoutButton(
                    panel,
                    0f,
                    rowH,
                    rowLabel,
                    type.Body,
                    UI.PopupRowBackdrop,
                    () => { CloseUndoHistoryMenu(); UndoSteps(steps); });
                if (row == null) continue;
                row.name = "UndoHistoryRow_" + i;

                Text rowTxt = row.GetComponentInChildren<Text>(true);
                if (rowTxt != null)
                {
                    rowTxt.alignment = TextAnchor.MiddleLeft;
                    rowTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    UI.ApplyPopupMenuRowTextPadding(rowTxt, s);
                }

                LayoutElement le = row.GetComponent<LayoutElement>();
                if (le == null) le = row.AddComponent<LayoutElement>();
                le.preferredHeight = rowH;
                le.minHeight = rowH;
                le.flexibleHeight = 0f;
                le.flexibleWidth = 1f;

                _undoHistoryRowImgs.Add(row.GetComponent<Image>());

                int depth = i;
                UIHoverDelegate hov = row.GetComponent<UIHoverDelegate>();
                if (hov == null) hov = row.AddComponent<UIHoverDelegate>();
                hov.OnHoverChange = on => ApplyUndoHistoryHoverCascade(on ? depth : -1);

                AddTooltipPlain(row, string.Format(
                    VPBTranslation.T("gallery.undo.history_row_tip", "Roll back {0} step(s), ending with: {1}"),
                    steps, _undoHistoryLabels[i]));
            }

            int older = _undoHistoryLabels.Count - shown;
            if (older > 0)
            {
                GameObject more = new GameObject("UndoHistoryOlder");
                more.transform.SetParent(panel, false);
                LayoutElement mle = more.AddComponent<LayoutElement>();
                mle.preferredHeight = headerH;
                mle.minHeight = headerH;
                mle.flexibleWidth = 1f;
                mle.flexibleHeight = 0f;
                Text moreTxt = UI.CreateLabel(
                    more,
                    string.Format(VPBTranslation.T("gallery.undo.history_older", "+{0} older step(s)"), older),
                    type.Caption,
                    UI.PopupMutedText,
                    TextAnchor.MiddleLeft,
                    HorizontalWrapMode.Overflow,
                    VerticalWrapMode.Truncate,
                    raycastTarget: false,
                    name: "Text");
                GalleryUiMetrics.ApplyFont(moreTxt, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
                UI.ApplyPopupMenuRowTextPadding(moreTxt, s);
            }
        }

        private void ApplyUndoHistoryHoverCascade(int depth)
        {
            for (int i = 0; i < _undoHistoryRowImgs.Count; i++)
            {
                Image img = _undoHistoryRowImgs[i];
                if (img == null) continue;
                img.color = i <= depth ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;
            }

            if (_undoHistoryHeaderText == null) return;
            if (depth < 0 || depth >= _undoHistoryLabels.Count)
            {
                _undoHistoryHeaderText.text = _undoHistoryHeaderIdle;
                return;
            }
            _undoHistoryHeaderText.text = string.Format(
                VPBTranslation.T("gallery.undo.history_header_hover", "Undo {0} step(s) — back to before “{1}”"),
                depth + 1,
                _undoHistoryLabels[depth]);
        }

        private void ToggleUndoHistoryMenu()
        {
            EnsureUndoHistoryChrome();
            if (_undoHistoryMenuGO == null) return;

            if (_undoHistoryOpen)
            {
                CloseUndoHistoryMenu();
                return;
            }

            if (undoStack == null || undoStack.Count == 0)
            {
                try
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.undo.empty", "Nothing to undo."), 1.5f);
                }
                catch { }
                return;
            }

            Transform panel = _undoHistoryMenuGO.transform.Find("UndoHistoryPanel");
            if (panel != null)
            {
                RebuildUndoHistoryRows(panel);
                try { RescaleUndoHistoryMenuInternal(ChromeScale); } catch { }
            }
            _undoHistoryOpen = true;
            _undoHistoryMenuGO.transform.SetAsLastSibling();
            _undoHistoryMenuGO.SetActive(true);
        }

        private void CloseUndoHistoryMenu()
        {
            _undoHistoryOpen = false;
            if (_undoHistoryMenuGO != null) _undoHistoryMenuGO.SetActive(false);
        }

        private void PositionUndoHistoryPanel(RectTransform panelRT)
        {
            if (panelRT == null || _undoHistoryMenuGO == null) return;
            RectTransform overlayRT = _undoHistoryMenuGO.GetComponent<RectTransform>();
            if (overlayRT == null) return;

            float s = ChromeScale <= 0f ? 1f : ChromeScale;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;

            RectTransform anchorRT = footerUndoBtnGO != null && footerUndoBtnGO.activeInHierarchy
                ? footerUndoBtnGO.GetComponent<RectTransform>()
                : null;

            float localX = 0f;
            float localY;
            if (anchorRT != null)
            {
                Vector3 worldX = anchorRT.TransformPoint(anchorRT.rect.center);
                localX = overlayRT.InverseTransformPoint(worldX).x;
            }

            if (hoverPathRT != null && hoverPathRT.gameObject.activeInHierarchy)
            {
                Vector3 worldInfoTop = hoverPathRT.TransformPoint(new Vector3(
                    hoverPathRT.rect.center.x,
                    hoverPathRT.rect.yMax,
                    0f));
                localY = overlayRT.InverseTransformPoint(worldInfoTop).y + gap;
            }
            else if (anchorRT != null)
            {
                Vector3 worldBtnTop = anchorRT.TransformPoint(new Vector3(
                    anchorRT.rect.center.x,
                    anchorRT.rect.yMax,
                    0f));
                localY = overlayRT.InverseTransformPoint(worldBtnTop).y
                    + GalleryUiDesignTokens.FooterInfoRowHeightRef * s
                    + gap;
            }
            else
            {
                localY = overlayRT.rect.yMin
                    + GalleryUiDesignTokens.FooterBarHeightRef * s
                    + gap;
            }

            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0f);
            panelRT.anchoredPosition = new Vector2(localX, localY);
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, GalleryUiDesignTokens.ControlGapRef * s);
        }

        private void RescaleUndoHistoryMenuInternal(float s)
        {
            if (_undoHistoryMenuGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = _undoHistoryMenuGO.transform.Find("UndoHistoryPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(
                panel.gameObject,
                s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuOverflowFontRef,
                GalleryUiDesignTokens.OverflowMenuPanelWidthRef);
            RestoreUndoHistoryCaptionRow(panel.Find("UndoHistoryHeader"), s);
            RestoreUndoHistoryCaptionRow(panel.Find("UndoHistoryOlder"), s);
            if (_undoHistoryOpen)
                PositionUndoHistoryPanel(panel as RectTransform);
        }

        private static void RestoreUndoHistoryCaptionRow(Transform row, float s)
        {
            if (row == null) return;
            float h = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;
            LayoutElement le = row.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredHeight = h;
                le.minHeight = h;
            }
            Text t = row.GetComponentInChildren<Text>(true);
            if (t == null) return;
            GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyPopupMenuRowTextPadding(t, s);
        }

        private void UndoSteps(int steps)
        {
            if (steps <= 0) return;
            if (_undoStepsRunning) return;

            int available = undoStack != null ? undoStack.Count : 0;
            if (available <= 0)
            {
                Undo();
                return;
            }
            if (steps > available) steps = available;
            if (steps == 1)
            {
                Undo();
                return;
            }

            string deepest = VPBTranslation.T("gallery.undo.default_label", "Change");
            if (undoLabelStack != null && undoLabelStack.Count >= steps)
            {
                string[] topFirst = undoLabelStack.ToArray();
                if (!string.IsNullOrEmpty(topFirst[steps - 1])) deepest = topFirst[steps - 1];
            }

            try { StartCoroutine(UndoStepsCoroutine(steps, deepest)); }
            catch { for (int i = 0; i < steps; i++) Undo(); }
        }

        private IEnumerator UndoStepsCoroutine(int steps, string deepest)
        {
            _undoStepsRunning = true;
            int done = 0;
            for (int i = 0; i < steps; i++)
            {
                if (undoStack == null || undoStack.Count == 0) break;
                Undo();
                done++;
                yield return null;
            }
            _undoStepsRunning = false;

            try
            {
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("gallery.undo.done_multi", "Undid {0} steps — back to before “{1}”"),
                        done, deepest),
                    2.5f);
            }
            catch { }
        }
    }
}
