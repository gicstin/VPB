using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
{        private void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        private void ShowLoadingOverlay(string message)
        {
            // BusyChrome only for first load / empty grid — populated grid change is enough feedback.
            // Quiet refresh already skips this caller; EndBrowseRefresh stays idempotent.
            bool needChrome = !hasLoadedContent
                || currentFilteredFiles == null
                || currentFilteredFiles.Count == 0;
            if (!needChrome) return;
            try
            {
                VpbProgressService.BeginBrowseRefresh(
                    string.IsNullOrEmpty(message)
                        ? "Preparing items for browse"
                        : message);
            }
            catch { }
        }

        private void HideLoadingOverlay()
        {
            try { VpbProgressService.EndBrowseRefresh(); } catch { }
        }

        public void DisplayColorPicker(string title, Color initialColor, UnityAction<Color> onConfirm)
        {
            // Full gallery card so dim covers rails + footer; UIColorPicker uses own Canvas sorting to stay above grid.
            Transform host = backgroundBoxGO != null ? backgroundBoxGO.transform
                : canvas != null ? canvas.transform : null;
            if (UIColorPicker.Instance != null)
                UIColorPicker.Instance.Show(initialColor, c => { if (onConfirm != null) onConfirm.Invoke(c); }, title, host);
        }

        public void DisplayTextInput(string title, string initialValue, UnityAction<string> onConfirm)
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            GalleryModalTypography type = new GalleryModalTypography(s);

            GameObject panelGO;
            GameObject overlayGO = UI.CreateModalChrome(
                backgroundBoxGO, "TextInputOverlay", 400f * s, 200f * s, UI.ChromeDarker, null, out panelGO, dimAlpha: 0.5f);

            Text titleText = UI.CreateLabel(
                panelGO, title, type.Title, Color.white, TextAnchor.MiddleCenter,
                anchorPreset: AnchorPresets.hStretchTop,
                size: new Vector2(0, 40f * s),
                anchoredPosition: new Vector2(0, -10f * s),
                name: "Title");

            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(panelGO.transform, false);
            Image inputBg = UI.AddImage(inputGO, UI.ChromePanel);
            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.sizeDelta = new Vector2(350f * s, 40f * s);
            inputRT.anchoredPosition = new Vector2(0, 10f * s);

            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.sizeDelta = new Vector2(-20f * s, -10f * s);

            Text t = UI.CreateLabel(textArea, "", type.Body, Color.white, TextAnchor.MiddleLeft, name: "Text");

            input.textComponent = t;
            input.text = initialValue;
            inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input);

            float btnW = 140f * s;
            float btnH = GalleryUiDesignTokens.ButtonSizeRef * s;
            float btnY = -60f * s;
            GameObject confirmBtn = UI.CreateUIButton(panelGO, btnW, btnH, "Confirm", type.Body, 80f * s, btnY, AnchorPresets.middleCenter, () => {
                onConfirm?.Invoke(input.text);
                Destroy(overlayGO);
            });
            
            GameObject cancelBtn = UI.CreateUIButton(panelGO, btnW, btnH, "Cancel", type.Body, -80f * s, btnY, AnchorPresets.middleCenter, () => {
                Destroy(overlayGO);
            });

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
            input.ActivateInputField();
        }

        /// <summary>
        /// Modal rename dialog over the file preview viewport (Grid/List scroll area).
        /// Category-specific side tabs can reuse the same UX later; trailing row actions use <see cref="UI.CreateSideTabSquareIconButton"/>.
        /// </summary>
        private void ShowPersonAtomRenameOverlay(global::Atom atom)
        {
            if (atom == null || backgroundBoxGO == null) return;
            string oldUid = null;
            try { oldUid = atom.uid; } catch { }
            if (string.IsNullOrEmpty(oldUid)) return;

            Transform overlayParent = null;
            try
            {
                if (scrollRect != null && scrollRect.viewport != null)
                    overlayParent = scrollRect.viewport.transform;
            }
            catch { }
            if (overlayParent == null) overlayParent = backgroundBoxGO.transform;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            GalleryModalTypography type = new GalleryModalTypography(s);

            GameObject panelGO;
            GameObject overlayGO = UI.CreateModalChrome(
                overlayParent.gameObject, "PersonAtomRenameOverlay", 440f * s, 300f * s,
                new Color(0.12f, 0.12f, 0.12f, 1f), null, out panelGO, dimAlpha: 0.55f);

            UI.CreateLabel(panelGO, VPBTranslation.T("gallery.rename.title", "Rename Person Atom"), type.Title, Color.white, TextAnchor.MiddleCenter, anchorPreset: AnchorPresets.hStretchTop, size: new Vector2(-24f * s, 36f * s), anchoredPosition: new Vector2(0, -12f * s), name: "Title");

            UI.CreateLabel(panelGO, VPBTranslation.T("gallery.rename.old_name_label", "Old name"), type.Body, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleLeft, anchorPreset: AnchorPresets.hStretchTop, size: new Vector2(-28f * s, 22f * s), anchoredPosition: new Vector2(0, -54f * s), name: "OldNameLabel");

            GameObject oldValGO = new GameObject("OldNameValue");
            oldValGO.transform.SetParent(panelGO.transform, false);
            Image oldValBg = UI.AddImage(oldValGO, new Color(0.18f, 0.18f, 0.2f, 1f));
            Text oldValTxt = UI.CreateLabel(oldValGO, oldUid, type.Body, Color.white, TextAnchor.MiddleLeft, name: "Text");
            RectTransform oldValTxtRt = oldValTxt.GetComponent<RectTransform>();
            oldValTxtRt.offsetMin = new Vector2(10f * s, 4f * s);
            oldValTxtRt.offsetMax = new Vector2(-10f * s, -4f * s);
            RectTransform oldValRt = oldValGO.GetComponent<RectTransform>();
            oldValRt.anchorMin = new Vector2(0, 1);
            oldValRt.anchorMax = new Vector2(1, 1);
            oldValRt.pivot = new Vector2(0.5f, 1f);
            oldValRt.anchoredPosition = new Vector2(0, -82f * s);
            oldValRt.sizeDelta = new Vector2(-28f * s, 38f * s);

            UI.CreateLabel(panelGO, VPBTranslation.T("gallery.rename.rename_to_label", "Rename to"), type.Body, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleLeft, anchorPreset: AnchorPresets.hStretchTop, size: new Vector2(-28f * s, 22f * s), anchoredPosition: new Vector2(0, -126f * s), name: "RenameToLabel");

            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(panelGO.transform, false);
            Image inputBg = UI.AddImage(inputGO, new Color(0.22f, 0.22f, 0.22f, 1f));
            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRt = inputGO.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0, 1);
            inputRt.anchorMax = new Vector2(1, 1);
            inputRt.pivot = new Vector2(0.5f, 1f);
            inputRt.anchoredPosition = new Vector2(0, -154f * s);
            inputRt.sizeDelta = new Vector2(-28f * s, 40f * s);

            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRt = textArea.AddComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(10f * s, 6f * s);
            textAreaRt.offsetMax = new Vector2(-10f * s, -6f * s);

            Text tComp = UI.CreateLabel(textArea, "", type.Body, Color.white, TextAnchor.MiddleLeft, name: "Text");

            input.textComponent = tComp;
            input.text = oldUid;
            inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input);

            UnityAction close = () => { try { Destroy(overlayGO); } catch { } };

            float btnW = 150f * s;
            float btnH = GalleryUiDesignTokens.ButtonSizeRef * s;
            float btnY = -116f * s;
            GameObject renameBtn = UI.CreateUIButton(panelGO, btnW, btnH, VPBTranslation.T("gallery.rename.rename_btn", "Rename"), type.Body, 78f * s, btnY, AnchorPresets.middleCenter, () =>
            {
                string newName = input != null ? input.text : null;
                if (newName != null) newName = newName.Trim();
                if (string.IsNullOrEmpty(newName))
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.rename.empty", "Enter a name."), 2f);
                    return;
                }
                if (string.Equals(newName, oldUid, StringComparison.Ordinal))
                {
                    close();
                    return;
                }
                if (SuperController.singleton == null) return;
                try
                {
                    global::Atom clash = SuperController.singleton.GetAtomByUid(newName);
                    if (clash != null && clash != atom)
                    {
                        ShowTemporaryStatus(VPBTranslation.T("gallery.rename.in_use", "That name is already used."), 2.5f);
                        return;
                    }
                }
                catch { }

                try
                {
                    SuperController.singleton.RenameAtom(atom, newName);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] RenameAtom failed: " + ex);
                    ShowTemporaryStatus(VPBTranslation.T("gallery.rename.failed", "Rename failed. See log."), 2f);
                    return;
                }

                close();
                RefreshTargetDropdown();
                try
                {
                    for (int i = 0; i < personAtoms.Count; i++)
                    {
                        if (personAtoms[i] == atom)
                        {
                            targetDropdownValue = i;
                            break;
                        }
                    }
                }
                catch { }
                try { UpdateTargetDropdownUI(); } catch { }
                try { UpdateTabs(); } catch { }
                try { NotifyAllPanelsSceneTargetsChanged(); } catch { }
            });

            GameObject cancelBtn = UI.CreateUIButton(panelGO, btnW, btnH, VPBTranslation.T("gallery.rename.cancel_btn", "Cancel"), type.Body, -78f * s, btnY, AnchorPresets.middleCenter, close);

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
            input.ActivateInputField();
            input.MoveTextEnd(false);
        }

        public void DisplayConfirm(string title, string message, UnityAction onConfirm)
        {
            DisplayConfirm(title, message, onConfirm, null, null, null);
        }

        /// <summary>
        /// Modal confirm. Esc → cancel (or onCancel), Enter → confirm (unless hideConfirm).
        /// Custom button labels optional (e.g. Keep / Revert). Scaled with ChromeScale.
        /// Optional alt button is never Enter (secondary / extra-risk path).
        /// </summary>
        public void DisplayConfirm(
            string title,
            string message,
            UnityAction onConfirm,
            UnityAction onCancel,
            string confirmLabel,
            string cancelLabel,
            UnityAction onAlt = null,
            string altLabel = null,
            bool hideConfirm = false)
        {
            try { CloseConfirmOverlay(invokeCancel: false); } catch { }

            _confirmOnConfirm = onConfirm;
            _confirmOnCancel = onCancel;
            _confirmOnAlt = onAlt;
            _confirmTitle = title ?? "";
            _confirmMessage = message ?? "";
            _confirmConfirmLabel = confirmLabel;
            _confirmCancelLabel = cancelLabel;
            _confirmAltLabel = altLabel;
            _confirmHideConfirm = hideConfirm;

            BuildConfirmOverlayUi();
        }

        private void BuildConfirmOverlayUi()
        {
            if (backgroundBoxGO == null) return;

            if (_confirmOverlayGO != null)
            {
                try { Destroy(_confirmOverlayGO); } catch { }
                _confirmOverlayGO = null;
            }

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            GalleryModalTypography type = new GalleryModalTypography(s);

            // Galitz dialog: content-sized shell (no empty cavern); title → body → copy → actions.
            bool hasAlt = !_confirmHideConfirm && !string.IsNullOrEmpty(_confirmAltLabel) && _confirmOnAlt != null;
            bool reportMode = !_confirmHideConfirm;
            float panelWRef = reportMode ? 600f : 460f;
            if (hasAlt && panelWRef < 600f) panelWRef = 600f;
            float panelW = panelWRef * s;
            GameObject panelGO;
            GameObject overlayGO = UI.CreateModalChrome(
                backgroundBoxGO,
                "ConfirmOverlay",
                panelW,
                120f * s,
                GalleryUiColorTokens.ModalSurface,
                null,
                out panelGO,
                dimAlpha: 0.55f);

            _confirmOverlayGO = overlayGO;

            // Opaque raised panel edge (von Restorff: dialog distinct from dim).
            Image panelImg = panelGO.GetComponent<Image>();
            if (panelImg != null) panelImg.color = GalleryUiColorTokens.ModalSurface;
            Outline panelOutline = panelGO.GetComponent<Outline>();
            if (panelOutline == null) panelOutline = panelGO.AddComponent<Outline>();
            panelOutline.effectColor = new Color(1f, 1f, 1f, 0.14f);
            panelOutline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup v = panelGO.AddComponent<VerticalLayoutGroup>();
            int pad = Mathf.RoundToInt(20f * s);
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.spacing = 12f * s;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;

            ContentSizeFitter csf = panelGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string title = string.IsNullOrEmpty(_confirmTitle)
                ? VPBTranslation.T("gallery.confirm.title", "Confirm")
                : _confirmTitle;
            Text titleTxt = UI.CreateEmphasisTitleLabel(
                panelGO, title, type.Title, Color.white, TextAnchor.MiddleCenter);
            ConfirmPrepLayoutRow(titleTxt != null ? titleTxt.rectTransform : null, 32f * s);
            if (titleTxt != null)
                UI.AddLE(titleTxt.gameObject, preferredHeight: 32f * s, minHeight: 28f * s, flexibleHeight: 0f);

            if (reportMode)
            {
                float minReportH = 80f * s;
                float maxReportH = 440f * s;
                float reportH;
                GameObject reportHost = BuildConfirmReportField(panelGO, panelW, pad, type, s, minReportH, maxReportH, out reportH);
                ConfirmPrepLayoutRow(reportHost.GetComponent<RectTransform>(), reportH);
                UI.AddLE(reportHost, preferredHeight: reportH, minHeight: minReportH, flexibleHeight: 0f);
            }
            else
            {
                Text msgText = UI.CreateLabel(
                    panelGO,
                    _confirmMessage ?? "",
                    type.Body,
                    GalleryUiColorTokens.TextMuted,
                    TextAnchor.UpperLeft,
                    HorizontalWrapMode.Wrap,
                    VerticalWrapMode.Overflow,
                    raycastTarget: false,
                    name: "Message");
                if (msgText != null)
                {
                    ConfirmPrepLayoutRow(msgText.rectTransform, 48f * s);
                    UI.AddLE(msgText.gameObject, preferredHeight: 48f * s, minHeight: 40f * s, flexibleHeight: 0f);
                }
            }

            GameObject btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(panelGO.transform, false);
            ConfirmPrepLayoutRow(btnRow.GetComponent<RectTransform>() ?? btnRow.AddComponent<RectTransform>(), GalleryUiDesignTokens.ButtonSizeRef * s);
            HorizontalLayoutGroup brh = btnRow.AddComponent<HorizontalLayoutGroup>();
            brh.spacing = 12f * s;
            brh.padding = new RectOffset(0, 0, 0, 0);
            brh.childAlignment = reportMode ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            brh.childForceExpandWidth = false;
            brh.childControlWidth = true;
            brh.childForceExpandHeight = true;
            brh.childControlHeight = true;
            float btnRowH = (GalleryUiDesignTokens.ButtonSizeRef + 4f) * s;
            UI.AddLE(btnRow, preferredHeight: btnRowH, minHeight: btnRowH, flexibleHeight: 0f);

            string cancelText = string.IsNullOrEmpty(_confirmCancelLabel)
                ? VPBTranslation.T("hook.cancel", "Cancel")
                : _confirmCancelLabel;
            string confirmText = string.IsNullOrEmpty(_confirmConfirmLabel)
                ? VPBTranslation.T("gallery.confirm.ok", "Confirm")
                : _confirmConfirmLabel;

            float btnH = GalleryUiDesignTokens.ButtonSizeRef * s;
            float btnW = hasAlt ? 0f : 132f * s;
            if (reportMode)
            {
                GameObject copyBtn = UI.CreateFloatChromeIconButton(
                    btnRow.transform,
                    btnH,
                    "clipboard-list",
                    GalleryUiColorTokens.SurfaceMid,
                    ConfirmOverlayCopy);
                if (copyBtn != null)
                {
                    copyBtn.name = "ConfirmCopy";
                    AddTooltip(copyBtn, "gallery.confirm.copy_tip", "Copy report to clipboard (Ctrl+C)");
                }
                GameObject spacer = new GameObject("BtnSpacer");
                spacer.transform.SetParent(btnRow.transform, false);
                UI.AddLE(spacer, minWidth: 8f * s, flexibleWidth: 1f, minHeight: 1f, preferredHeight: 1f);
            }
            UI.CreateChromeLayoutButton(
                btnRow.transform, btnW, btnH, cancelText, type.Body,
                GalleryUiColorTokens.SurfaceMid, ConfirmOverlayCancel);
            if (hasAlt)
            {
                UI.CreateChromeLayoutButton(
                    btnRow.transform, btnW, btnH, _confirmAltLabel, type.Body,
                    GalleryUiColorTokens.ActiveWarnHeader, ConfirmOverlayAlt);
            }
            if (!_confirmHideConfirm)
            {
                UI.CreateChromeLayoutButton(
                    btnRow.transform, btnW, btnH, confirmText, type.Body,
                    new Color(0.48f, 0.22f, 0.22f, 1f), ConfirmOverlayConfirm);
            }

            try { LayoutRebuilder.ForceRebuildLayoutImmediate(panelGO.GetComponent<RectTransform>()); } catch { }
            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
        }

        /// <summary>
        /// Unity UI Text ~16k verts/mesh; keep chunks under that so long delete lists still layout.
        /// </summary>
        private const int ConfirmReportChunkChars = 3500;

        private GameObject BuildConfirmReportField(
            GameObject panelGO,
            float panelW,
            int pad,
            GalleryModalTypography type,
            float s,
            float minReportH,
            float maxReportH,
            out float reportH)
        {
            reportH = minReportH;
            GameObject host = new GameObject("ReportHost");
            host.transform.SetParent(panelGO.transform, false);
            Image hostBg = UI.AddImage(host, GalleryUiColorTokens.SurfaceDarker);
            if (hostBg != null) hostBg.raycastTarget = true;

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            float inset = 8f * s;
            ScrollRect sr = host.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = VpbScrollTuning.Sensitivity(25f, 1f);

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(host.transform, false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(inset, inset);
            vpRt.offsetMax = new Vector2(-(inset + sbW), -inset);
            viewport.AddComponent<RectMask2D>();
            Image vpHit = viewport.AddComponent<Image>();
            vpHit.color = new Color(1f, 1f, 1f, 0.004f);
            vpHit.raycastTarget = true;
            sr.viewport = vpRt;

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup cv = content.AddComponent<VerticalLayoutGroup>();
            int contentPad = Mathf.RoundToInt(4f * s);
            cv.padding = new RectOffset(contentPad, contentPad, contentPad, contentPad);
            cv.spacing = 0f;
            cv.childAlignment = TextAnchor.UpperLeft;
            cv.childControlWidth = true;
            cv.childForceExpandWidth = true;
            cv.childControlHeight = true;
            cv.childForceExpandHeight = false;

            float innerW = Mathf.Max(40f, panelW - pad * 2f - inset * 2f - sbW - contentPad * 2f);
            List<string> chunks = ConfirmSplitReportChunks(_confirmMessage ?? "");
            float totalH = contentPad * 2f;
            for (int i = 0; i < chunks.Count; i++)
            {
                Text text = UI.CreateLabel(
                    content,
                    chunks[i],
                    type.Body,
                    GalleryUiColorTokens.TextPrimary,
                    TextAnchor.UpperLeft,
                    HorizontalWrapMode.Wrap,
                    VerticalWrapMode.Overflow,
                    raycastTarget: true,
                    richText: false,
                    anchorPreset: AnchorPresets.hStretchTop,
                    name: "Text" + i);
                if (text == null) continue;
                text.supportRichText = false;
                text.lineSpacing = 1.15f;
                float h = ConfirmMeasureTextHeight(text, chunks[i], innerW);
                UI.AddLE(text.gameObject, minHeight: h, preferredHeight: h, flexibleWidth: 1f, flexibleHeight: 0f);
                totalH += h;
            }

            contentRt.sizeDelta = new Vector2(0f, Mathf.Max(minReportH, totalH));
            sr.content = contentRt;
            reportH = Mathf.Clamp(totalH + inset * 2f, minReportH, maxReportH);

            GameObject scrollbarGO = UI.CreateScrollBar(host, sbW, reportH, Scrollbar.Direction.BottomToTop);
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            if (sbRT != null)
            {
                sbRT.anchorMin = new Vector2(1f, 0f);
                sbRT.anchorMax = new Vector2(1f, 1f);
                sbRT.pivot = new Vector2(1f, 1f);
                sbRT.sizeDelta = new Vector2(sbW, 0f);
                sbRT.anchoredPosition = Vector2.zero;
            }
            Scrollbar sb = scrollbarGO.GetComponent<Scrollbar>();
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            var minHandle = scrollbarGO.AddComponent<ScrollbarMinHandleHeight>();
            minHandle.minHandlePixels = 24f * s;
            sr.verticalNormalizedPosition = 1f;
            return host;
        }

        private static List<string> ConfirmSplitReportChunks(string message)
        {
            var chunks = new List<string>(4);
            if (string.IsNullOrEmpty(message))
            {
                chunks.Add("");
                return chunks;
            }
            int start = 0;
            int n = message.Length;
            while (start < n)
            {
                int remaining = n - start;
                if (remaining <= ConfirmReportChunkChars)
                {
                    chunks.Add(message.Substring(start));
                    break;
                }
                int take = ConfirmReportChunkChars;
                int nl = message.LastIndexOf('\n', start + take - 1, take);
                if (nl >= start)
                    take = nl - start + 1;
                if (take <= 0) take = ConfirmReportChunkChars;
                chunks.Add(message.Substring(start, take));
                start += take;
            }
            return chunks;
        }

        private static float ConfirmMeasureTextHeight(Text text, string str, float width)
        {
            if (text == null) return 0f;
            float lineH = Mathf.Max(12f, text.fontSize * Mathf.Max(1f, text.lineSpacing) * 1.2f);
            if (string.IsNullOrEmpty(str)) return lineH;
            if (width < 8f) width = 8f;
            try
            {
                TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(width, 0f));
                settings.horizontalOverflow = HorizontalWrapMode.Wrap;
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                settings.generateOutOfBounds = true;
                settings.updateBounds = true;
                TextGenerator tg = text.cachedTextGeneratorForLayout;
                if (tg != null)
                {
                    float h = tg.GetPreferredHeight(str, settings) / Mathf.Max(0.01f, text.pixelsPerUnit);
                    if (h > 1f) return h;
                }
            }
            catch { }
            int lines = 1;
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '\n') lines++;
            }
            return lines * lineH;
        }

        private void ConfirmOverlayCopy()
        {
            try
            {
                GUIUtility.systemCopyBuffer = _confirmMessage ?? "";
                ShowTemporaryStatus(VPBTranslation.T("gallery.confirm.copied", "Copied report"), 1.5f);
            }
            catch { }
        }

        /// <summary>
        /// Layout-group children must not use stretch-all (fills parent and overlaps siblings).
        /// Top-stretch width + explicit height driven by LayoutElement.
        /// </summary>
        private static void ConfirmPrepLayoutRow(RectTransform rt, float height)
        {
            if (rt == null) return;
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>Rebuild open confirm at current ChromeScale (live UI-scale change).</summary>
        private void RescaleConfirmOverlayIfOpen()
        {
            if (_confirmOverlayGO == null) return;
            try { BuildConfirmOverlayUi(); } catch { }
        }

        private bool IsConfirmOverlayOpen()
        {
            return _confirmOverlayGO != null;
        }

        private bool TryHandleConfirmOverlayKeys()
        {
            if (_confirmOverlayGO == null) return false;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_confirmEscIsDismiss)
                {
                    _confirmEscIsDismiss = false;
                    _deferredStickyEnter = StickyToolMode.None;
                    CloseConfirmOverlay(invokeCancel: false);
                    return true;
                }
                ConfirmOverlayCancel();
                return true;
            }
            if (!_confirmHideConfirm && IsCtrlHeld() && Input.GetKeyDown(KeyCode.C))
            {
                ConfirmOverlayCopy();
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_confirmHideConfirm) return true;
                ConfirmOverlayConfirm();
                return true;
            }
            return false;
        }

        private void ConfirmOverlayConfirm()
        {
            if (_confirmHideConfirm) return;
            _confirmEscIsDismiss = false;
            UnityAction act = _confirmOnConfirm;
            CloseConfirmOverlay(invokeCancel: false);
            if (act != null)
            {
                try { act.Invoke(); } catch (Exception ex) { LogUtil.LogError("[VPB] ConfirmOverlayConfirm: " + ex); }
            }
        }

        private void ConfirmOverlayAlt()
        {
            _confirmEscIsDismiss = false;
            UnityAction act = _confirmOnAlt;
            CloseConfirmOverlay(invokeCancel: false);
            if (act != null)
            {
                try { act.Invoke(); } catch (Exception ex) { LogUtil.LogError("[VPB] ConfirmOverlayAlt: " + ex); }
            }
        }

        private void ConfirmOverlayCancel()
        {
            _confirmEscIsDismiss = false;
            UnityAction act = _confirmOnCancel;
            CloseConfirmOverlay(invokeCancel: false);
            if (act != null)
            {
                try { act.Invoke(); } catch (Exception ex) { LogUtil.LogError("[VPB] ConfirmOverlayCancel: " + ex); }
            }
        }

        private void CloseConfirmOverlay(bool invokeCancel)
        {
            if (invokeCancel && _confirmOnCancel != null)
            {
                UnityAction act = _confirmOnCancel;
                _confirmOnCancel = null;
                _confirmOnConfirm = null;
                _confirmOnAlt = null;
                try { act.Invoke(); } catch { }
            }
            else
            {
                _confirmOnConfirm = null;
                _confirmOnCancel = null;
                _confirmOnAlt = null;
            }

            _confirmTitle = null;
            _confirmMessage = null;
            _confirmConfirmLabel = null;
            _confirmCancelLabel = null;
            _confirmAltLabel = null;
            _confirmHideConfirm = false;

            if (_confirmOverlayGO != null)
            {
                try { Destroy(_confirmOverlayGO); } catch { }
                _confirmOverlayGO = null;
            }
        }

        public void DisplayClothingSlotPicker(string title, System.Action<string> onSelect)
        {
            if (backgroundBoxGO == null) return;
            DisplayClothingSlotPicker(title, null, null, false, onSelect);
        }

        private void CloseClothingSlotPicker()
        {
            try
            {
                if (clothingSlotPickerPanelGO != null) Destroy(clothingSlotPickerPanelGO);
                if (clothingSlotPickerOverlayGO != null) Destroy(clothingSlotPickerOverlayGO);
            }
            catch { }
            clothingSlotPickerPanelGO = null;
            clothingSlotPickerOverlayGO = null;
        }

        private void ToggleClothingSlotPicker(string title, Atom target, RectTransform anchorRT, bool openToLeft, System.Action<string> onSelect)
        {
            if (clothingSlotPickerOverlayGO != null || clothingSlotPickerPanelGO != null)
            {
                CloseClothingSlotPicker();
                return;
            }

            DisplayClothingSlotPicker(title, target, anchorRT, openToLeft, onSelect);
        }

        private void CloseHairSlotPicker()
        {
            try
            {
                if (hairSlotPickerPanelGO != null) Destroy(hairSlotPickerPanelGO);
                if (hairSlotPickerOverlayGO != null) Destroy(hairSlotPickerOverlayGO);
            }
            catch { }
            hairSlotPickerPanelGO = null;
            hairSlotPickerOverlayGO = null;
        }

        private void ToggleHairSlotPicker(string title, Atom target, RectTransform anchorRT, bool openToLeft, System.Action<string> onSelect)
        {
            if (hairSlotPickerOverlayGO != null || hairSlotPickerPanelGO != null)
            {
                CloseHairSlotPicker();
                return;
            }

            DisplayHairSlotPicker(title, target, anchorRT, openToLeft, onSelect);
        }

        private void CloseRemoveHairSubmenu(bool isRight)
        {
            try
            {
                if (isRight)
                {
                    if (rightRemoveHairSubmenuPanelGO != null) Destroy(rightRemoveHairSubmenuPanelGO);
                    rightRemoveHairSubmenuPanelGO = null;
                }
                else
                {
                    if (leftRemoveHairSubmenuPanelGO != null) Destroy(leftRemoveHairSubmenuPanelGO);
                    leftRemoveHairSubmenuPanelGO = null;
                }
            }
            catch { }
        }

        private void ToggleRemoveHairSubmenu(string title, Atom target, RectTransform anchorRT, bool openToLeft, bool isRight, System.Action<string> onSelect)
        {
            if (isRight)
            {
                if (rightRemoveHairSubmenuPanelGO != null)
                {
                    CloseRemoveHairSubmenu(true);
                    return;
                }
            }
            else
            {
                if (leftRemoveHairSubmenuPanelGO != null)
                {
                    CloseRemoveHairSubmenu(false);
                    return;
                }
            }

            DisplayRemoveHairSubmenu(title, target, anchorRT, openToLeft, isRight, onSelect);
        }

        private void DisplayRemoveHairSubmenu(string title, Atom target, RectTransform anchorRT, bool openToLeft, bool isRight, System.Action<string> onSelect)
        {
            if (backgroundBoxGO == null) return;

            CloseRemoveHairSubmenu(isRight);

            List<KeyValuePair<string, string>> options = null;
            if (target != null)
            {
                try
                {
                    var items = new List<KeyValuePair<string, string>>();
                    DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                    if (dcs != null && dcs.hairItems != null)
                    {
                        foreach (var item in dcs.hairItems)
                        {
                            if (item == null || !item.active) continue;

                            string path = null;
                            try { path = item.uid; } catch { }
                            if (string.IsNullOrEmpty(path) || (!path.Contains(":/") && !path.Contains(":\\")))
                            {
                                try
                                {
                                    string internalId = null;
                                    string containingVAMDir = null;
                                    Type it = item.GetType();

                                    FieldInfo fInternalId = it.GetField("internalId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fInternalId != null) internalId = fInternalId.GetValue(item) as string;

                                    FieldInfo fVamDir = it.GetField("containingVAMDir", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fVamDir != null) containingVAMDir = fVamDir.GetValue(item) as string;

                                    if (string.IsNullOrEmpty(internalId))
                                    {
                                        FieldInfo fItemPath = it.GetField("itemPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fItemPath != null) internalId = fItemPath.GetValue(item) as string;
                                    }

                                    if (!string.IsNullOrEmpty(containingVAMDir) && !string.IsNullOrEmpty(internalId))
                                    {
                                        path = containingVAMDir.Replace("\\", "/").TrimEnd('/') + "/" + internalId.Replace("\\", "/").TrimStart('/');
                                    }
                                }
                                catch { }
                            }

                            if (string.IsNullOrEmpty(path)) continue;
                            string p = path.Replace("\\", "/");
                            string pl = p.ToLowerInvariant();
                            int idx = pl.IndexOf("/custom/hair/");
                            if (idx < 0) idx = pl.IndexOf("/hair/");
                            if (idx >= 0)
                            {
                                string sub = p.Substring(idx);
                                string[] parts = sub.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                for (int pi = 0; pi < parts.Length; pi++) parts[pi] = parts[pi].Trim();

                                string typeFolder = (parts.Length >= 4) ? parts[3] : null;
                                string fileName = null;
                                try
                                {
                                    string last = parts.Length > 0 ? parts[parts.Length - 1] : null;
                                    if (!string.IsNullOrEmpty(last))
                                    {
                                        int dot = last.LastIndexOf('.');
                                        fileName = dot > 0 ? last.Substring(0, dot) : last;
                                    }
                                }
                                catch { }

                                if (string.IsNullOrEmpty(fileName))
                                {
                                    try { fileName = item.name; }
                                    catch { }
                                }

                                string label = !string.IsNullOrEmpty(typeFolder)
                                    ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + (fileName ?? ""))
                                    : (fileName ?? "");

                                if (!string.IsNullOrEmpty(label))
                                {
                                    items.Add(new KeyValuePair<string, string>(item.uid, label));
                                }
                            }
                        }
                    }
                    options = items
                        .Where(kvp => !string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                        .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch { }
            }

            if (options == null) options = new List<KeyValuePair<string, string>>();
            if (options.Count == 0)
            {
                LogUtil.LogWarning("[VPB] No hair slot options available.");
                return;
            }

            // Side-button style submenu: no full-screen overlay. Panel is a child of the arrow/anchor.
            GameObject panelGO = new GameObject(isRight ? "RightRemoveHairSubmenu" : "LeftRemoveHairSubmenu");
            Transform panelParent = (anchorRT != null ? anchorRT.transform : backgroundBoxGO.transform);
            panelGO.transform.SetParent(panelParent, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.pivot = openToLeft ? new Vector2(1, 0.5f) : new Vector2(0, 0.5f);
            panelRT.anchorMin = new Vector2(openToLeft ? 0f : 1f, 0.5f);
            panelRT.anchorMax = new Vector2(openToLeft ? 0f : 1f, 0.5f);
            panelRT.anchoredPosition = new Vector2(openToLeft ? -4f : 4f, 0f);

            AddHoverDelegate(panelGO);

            Image panelImg = UI.AddImage(panelGO, UI.ChromeDarker);

            // Layout
            int rows = Mathf.Clamp(options.Count, 1, 10);
            float rowH = 42f;
            float rowGap = 6f;
            float panelW = 260f;
            float titleH = 24f;
            float padTop = 10f;
            float innerBottom = 10f;
            float listH = rows * rowH + Mathf.Max(0, rows - 1) * rowGap;
            float panelH = padTop + titleH + innerBottom + listH + 18f;
            panelRT.sizeDelta = new Vector2(panelW, panelH);

            UI.CreateLabel(panelGO, title, GalleryUiDesignTokens.FontRef, Color.white, TextAnchor.MiddleCenter, anchorPreset: AnchorPresets.hStretchTop, size: new Vector2(0, 24), anchoredPosition: new Vector2(0, -5), name: "Title");

            GameObject listGO = new GameObject("List");
            listGO.transform.SetParent(panelGO.transform, false);
            RectTransform listRT = listGO.AddComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0, 0);
            listRT.anchorMax = new Vector2(1, 1);
            listRT.offsetMin = new Vector2(GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.ControlGapRef);
            listRT.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef, -34);

            VerticalLayoutGroup vlg = UI.AddVLG(listGO, spacing: rowGap);

            ContentSizeFitter csf = listGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            for (int i = 0; i < options.Count; i++)
            {
                string itemUid = options[i].Key;
                string buttonLabel = options[i].Value;

                GameObject btn = UI.CreateUIButton(listGO, panelW - 20f, rowH, buttonLabel, 16, 0, 0, AnchorPresets.middleCenter, () => {
                    try { onSelect?.Invoke(itemUid); }
                    finally { CloseRemoveHairSubmenu(isRight); }
                });
                btn.GetComponent<Image>().color = UI.ChromePanel;
                AddHoverDelegate(btn);
            }

            if (isRight) rightRemoveHairSubmenuPanelGO = panelGO;
            else leftRemoveHairSubmenuPanelGO = panelGO;

            SetLayerRecursive(panelGO, backgroundBoxGO.layer);
        }

        public void DisplayHairSlotPicker(string title, Atom target, RectTransform anchorRT, bool openToLeft, System.Action<string> onSelect)
        {
            if (backgroundBoxGO == null) return;

            CloseHairSlotPicker();

            List<KeyValuePair<string, string>> options = null;
            if (target != null)
            {
                try
                {
                    var items = new List<KeyValuePair<string, string>>();
                    DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                    if (dcs != null && dcs.hairItems != null)
                    {
                        foreach (var item in dcs.hairItems)
                        {
                            if (item == null || !item.active) continue;

                            string path = null;
                            try { path = item.uid; } catch { }
                            if (string.IsNullOrEmpty(path) || (!path.Contains(":/") && !path.Contains(":\\")))
                            {
                                try
                                {
                                    string internalId = null;
                                    string containingVAMDir = null;
                                    Type it = item.GetType();

                                    FieldInfo fInternalId = it.GetField("internalId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fInternalId != null) internalId = fInternalId.GetValue(item) as string;

                                    FieldInfo fVamDir = it.GetField("containingVAMDir", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fVamDir != null) containingVAMDir = fVamDir.GetValue(item) as string;

                                    if (string.IsNullOrEmpty(internalId))
                                    {
                                        FieldInfo fItemPath = it.GetField("itemPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fItemPath != null) internalId = fItemPath.GetValue(item) as string;
                                    }

                                    if (!string.IsNullOrEmpty(containingVAMDir) && !string.IsNullOrEmpty(internalId))
                                    {
                                        path = containingVAMDir.Replace("\\", "/").TrimEnd('/') + "/" + internalId.Replace("\\", "/").TrimStart('/');
                                    }
                                }
                                catch { }
                            }

                            if (string.IsNullOrEmpty(path)) continue;
                            string p = path.Replace("\\", "/");
                            string pl = p.ToLowerInvariant();
                            int idx = pl.IndexOf("/custom/hair/");
                            if (idx < 0) idx = pl.IndexOf("/hair/");
                            if (idx >= 0)
                            {
                                string sub = p.Substring(idx);
                                string[] parts = sub.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                for (int pi = 0; pi < parts.Length; pi++) parts[pi] = parts[pi].Trim();

                                string typeFolder = (parts.Length >= 4) ? parts[3] : null;
                                string fileName = null;
                                try
                                {
                                    string last = parts.Length > 0 ? parts[parts.Length - 1] : null;
                                    if (!string.IsNullOrEmpty(last))
                                    {
                                        int dot = last.LastIndexOf('.');
                                        fileName = dot > 0 ? last.Substring(0, dot) : last;
                                    }
                                }
                                catch { }

                                if (string.IsNullOrEmpty(fileName))
                                {
                                    try { fileName = item.name; }
                                    catch { }
                                }

                                string label = !string.IsNullOrEmpty(typeFolder)
                                    ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + (fileName ?? ""))
                                    : (fileName ?? "");

                                if (!string.IsNullOrEmpty(label))
                                {
                                    items.Add(new KeyValuePair<string, string>(item.uid, label));
                                }
                            }
                        }
                    }
                    options = items
                        .Where(kvp => !string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                        .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch { }
            }

            if (options == null) options = new List<KeyValuePair<string, string>>();

            if (options.Count == 0)
            {
                LogUtil.LogWarning("[VPB] No hair slot options available.");
                return;
            }

            GameObject overlayGO = new GameObject("HairSlotPickerOverlay");
            overlayGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;

            AddHoverDelegate(overlayGO);

            Image overlayImg = UI.AddImage(overlayGO, new Color(0, 0, 0, 0.01f));

            Button overlayBtn = overlayGO.AddComponent<Button>();

            GameObject panelGO = new GameObject("Panel");
            Transform panelParent = (anchorRT != null ? anchorRT.transform : overlayGO.transform);
            panelGO.transform.SetParent(panelParent, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.pivot = openToLeft ? new Vector2(1, 0.5f) : new Vector2(0, 0.5f);
            panelRT.anchorMin = new Vector2(openToLeft ? 0f : 1f, 0.5f);
            panelRT.anchorMax = new Vector2(openToLeft ? 0f : 1f, 0.5f);
            panelRT.anchoredPosition = new Vector2(openToLeft ? -4f : 4f, 0f);

            AddHoverDelegate(panelGO);

            // Ensure the anchored panel consistently renders/raycasts above the full-screen overlay.
            try
            {
                Canvas panelCanvas = panelGO.AddComponent<Canvas>();
                panelCanvas.overrideSorting = true;
                panelCanvas.sortingOrder = 1000;
            }
            catch { }

            // Prevent side-button auto-hide CanvasGroups (on parent containers) from disabling picker interaction.
            try
            {
                CanvasGroup cg = panelGO.AddComponent<CanvasGroup>();
                cg.ignoreParentGroups = true;
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            catch { }

            Image panelImg = UI.AddImage(panelGO, UI.ChromeDarker);

            int cols = 1;
            int rows = Mathf.Clamp(options.Count, 1, 10);
            float rowH = 42f;
            float rowGap = 6f;
            float panelW = 260f;
            float titleH = 24f;
            float padTop = 10f;
            float innerBottom = 10f;
            float listH = rows * rowH + Mathf.Max(0, rows - 1) * rowGap;
            float panelH = padTop + titleH + innerBottom + listH + 18f;
            panelRT.sizeDelta = new Vector2(panelW, panelH);

            UI.CreateLabel(panelGO, title, GalleryUiDesignTokens.FontRef, Color.white, TextAnchor.MiddleCenter, anchorPreset: AnchorPresets.hStretchTop, size: new Vector2(0, 24), anchoredPosition: new Vector2(0, -5), name: "Title");

            GameObject listGO = new GameObject("List");
            listGO.transform.SetParent(panelGO.transform, false);
            RectTransform listRT = listGO.AddComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0, 0);
            listRT.anchorMax = new Vector2(1, 1);
            listRT.offsetMin = new Vector2(GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.ControlGapRef);
            listRT.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef, -34);

            GridLayoutGroup glg = listGO.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(panelW - 20f, rowH);
            glg.spacing = new Vector2(0, 6);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = cols;

            for (int i = 0; i < options.Count; i++)
            {
                string itemUid = options[i].Key;
                string buttonLabel = options[i].Value;
                GameObject btn = UI.CreateUIButton(listGO, 200, 42, buttonLabel, 16, 0, 0, AnchorPresets.middleCenter, () => {
                    try { onSelect?.Invoke(itemUid); }
                    finally
                    {
                        try
                        {
                            if (hairSlotPickerPanelGO != null) Destroy(hairSlotPickerPanelGO);
                            if (hairSlotPickerOverlayGO != null) Destroy(hairSlotPickerOverlayGO);
                        }
                        catch { }
                    }
                });
                btn.GetComponent<Image>().color = UI.ChromePanel;
                AddHoverDelegate(btn);
            }

            hairSlotPickerOverlayGO = overlayGO;
            hairSlotPickerPanelGO = panelGO;

            overlayBtn.onClick.AddListener(() => {
                try
                {
                    if (hairSlotPickerPanelGO != null) Destroy(hairSlotPickerPanelGO);
                    if (hairSlotPickerOverlayGO != null) Destroy(hairSlotPickerOverlayGO);
                }
                catch { }
            });

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
            if (anchorRT != null) SetLayerRecursive(panelGO, backgroundBoxGO.layer);
        }

        public void DisplayClothingSlotPicker(string title, Atom target, RectTransform anchorRT, bool openToLeft, System.Action<string> onSelect)
        {
            if (backgroundBoxGO == null) return;

            CloseClothingSlotPicker();

            List<KeyValuePair<string, string>> options = null;
            if (target != null)
            {
                try
                {
                    var items = new List<KeyValuePair<string, string>>();
                    DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                    if (dcs != null && dcs.clothingItems != null)
                    {
                        foreach (var item in dcs.clothingItems)
                        {
                            if (item == null || !item.active) continue;

                            string path = null;
                            try { path = item.uid; } catch { }
                            if (string.IsNullOrEmpty(path) || (!path.Contains(":/") && !path.Contains(":\\")))
                            {
                                try
                                {
                                    string internalId = null;
                                    string containingVAMDir = null;
                                    Type it = item.GetType();

                                    FieldInfo fInternalId = it.GetField("internalId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fInternalId != null) internalId = fInternalId.GetValue(item) as string;

                                    FieldInfo fVamDir = it.GetField("containingVAMDir", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fVamDir != null) containingVAMDir = fVamDir.GetValue(item) as string;

                                    if (string.IsNullOrEmpty(internalId))
                                    {
                                        FieldInfo fItemPath = it.GetField("itemPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fItemPath != null) internalId = fItemPath.GetValue(item) as string;
                                    }

                                    if (!string.IsNullOrEmpty(containingVAMDir) && !string.IsNullOrEmpty(internalId))
                                    {
                                        path = containingVAMDir.Replace("\\", "/").TrimEnd('/') + "/" + internalId.Replace("\\", "/").TrimStart('/');
                                    }
                                }
                                catch { }
                            }

                            if (string.IsNullOrEmpty(path)) continue;
                            string p = path.Replace("\\", "/");
                            string pl = p.ToLowerInvariant();
                            int idx = pl.IndexOf("/custom/clothing/");
                            if (idx < 0) idx = pl.IndexOf("/clothing/");
                            if (idx >= 0)
                            {
                                string sub = p.Substring(idx);
                                string[] parts = sub.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                for (int pi = 0; pi < parts.Length; pi++) parts[pi] = parts[pi].Trim();

                                string typeFolder = (parts.Length >= 4) ? parts[3] : null;
                                string fileName = null;
                                try
                                {
                                    string last = parts.Length > 0 ? parts[parts.Length - 1] : null;
                                    if (!string.IsNullOrEmpty(last))
                                    {
                                        int dot = last.LastIndexOf('.');
                                        fileName = dot > 0 ? last.Substring(0, dot) : last;
                                    }
                                }
                                catch { }

                                if (string.IsNullOrEmpty(fileName))
                                {
                                    try { fileName = item.name; }
                                    catch { }
                                }

                                string label = !string.IsNullOrEmpty(typeFolder)
                                    ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + (fileName ?? ""))
                                    : (fileName ?? "");

                                if (!string.IsNullOrEmpty(label))
                                {
                                    items.Add(new KeyValuePair<string, string>(item.uid, label));
                                }
                            }
                        }
                    }
                    options = items
                        .Where(kvp => !string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                        .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch { }
            }

            if (options == null) options = new List<KeyValuePair<string, string>>();

            if (options.Count == 0)
            {
                LogUtil.LogWarning("[VPB] No clothing slot options available.");
                return;
            }

            GameObject overlayGO = new GameObject("ClothingSlotPickerOverlay");
            overlayGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;

            AddHoverDelegate(overlayGO);

            Image overlayImg = UI.AddImage(overlayGO, new Color(0, 0, 0, 0.01f));

            Button overlayBtn = overlayGO.AddComponent<Button>();

            GameObject panelGO = new GameObject("Panel");
            // Parent to anchor so it follows the arrow button exactly in fixed/floating modes.
            Transform panelParent = (anchorRT != null ? anchorRT.transform : overlayGO.transform);
            panelGO.transform.SetParent(panelParent, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.pivot = openToLeft ? new Vector2(1, 0.5f) : new Vector2(0, 0.5f);
            panelRT.anchorMin = new Vector2(openToLeft ? 0f : 1f, 0.5f);
            panelRT.anchorMax = new Vector2(openToLeft ? 0f : 1f, 0.5f);
            panelRT.anchoredPosition = new Vector2(openToLeft ? -4f : 4f, 0f);

            AddHoverDelegate(panelGO);

            // Prevent side-button auto-hide CanvasGroups (on parent containers) from disabling picker interaction.
            try
            {
                CanvasGroup cg = panelGO.AddComponent<CanvasGroup>();
                cg.ignoreParentGroups = true;
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            catch { }

            Image panelImg = UI.AddImage(panelGO, UI.ChromeDarker);

            int cols = 1;
            int rows = Mathf.Clamp(options.Count, 1, 10);
            float rowH = 42f;
            float rowGap = 6f;
            float panelW = 260f;
            float titleH = 24f;
            float padTop = 10f;
            float innerBottom = 10f;
            float listH = rows * rowH + Mathf.Max(0, rows - 1) * rowGap;
            float panelH = padTop + titleH + innerBottom + listH + 18f;
            panelRT.sizeDelta = new Vector2(panelW, panelH);

            UI.CreateLabel(panelGO, title, GalleryUiDesignTokens.FontRef, Color.white, TextAnchor.MiddleCenter, anchorPreset: AnchorPresets.hStretchTop, size: new Vector2(0, 24), anchoredPosition: new Vector2(0, -5), name: "Title");

            GameObject listGO = new GameObject("List");
            listGO.transform.SetParent(panelGO.transform, false);
            RectTransform listRT = listGO.AddComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0, 0);
            listRT.anchorMax = new Vector2(1, 1);
            listRT.offsetMin = new Vector2(GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.ControlGapRef);
            listRT.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef, -34);

            GridLayoutGroup glg = listGO.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(panelW - 20f, rowH);
            glg.spacing = new Vector2(0, 6);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = cols;

            for (int i = 0; i < options.Count; i++)
            {
                string itemUid = options[i].Key;
                string buttonLabel = options[i].Value;
                GameObject btn = UI.CreateUIButton(listGO, 200, 42, buttonLabel, 16, 0, 0, AnchorPresets.middleCenter, () => {
                    try { onSelect?.Invoke(itemUid); }
                    finally
                    {
                        try
                        {
                            if (clothingSlotPickerPanelGO != null) Destroy(clothingSlotPickerPanelGO);
                            if (clothingSlotPickerOverlayGO != null) Destroy(clothingSlotPickerOverlayGO);
                        }
                        catch { }
                    }
                });
                btn.GetComponent<Image>().color = UI.ChromePanel;
                AddHoverDelegate(btn);
            }

            clothingSlotPickerOverlayGO = overlayGO;
            clothingSlotPickerPanelGO = panelGO;

            overlayBtn.onClick.AddListener(() => {
                try
                {
                    if (clothingSlotPickerPanelGO != null) Destroy(clothingSlotPickerPanelGO);
                    if (clothingSlotPickerOverlayGO != null) Destroy(clothingSlotPickerOverlayGO);
                }
                catch { }
            });

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
            if (anchorRT != null) SetLayerRecursive(panelGO, backgroundBoxGO.layer);
        }
    }

}
