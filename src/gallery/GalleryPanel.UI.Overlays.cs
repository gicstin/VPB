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

        private void CreateLoadingOverlay(GameObject parentGO)
        {
            if (parentGO == null) return;
            if (loadingOverlayGO != null) return;

            loadingOverlayGO = new GameObject("LoadingOverlay");
            loadingOverlayGO.transform.SetParent(parentGO.transform, false);
            RectTransform overlayRT = loadingOverlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;
            overlayRT.anchoredPosition = Vector2.zero;

            Image overlayImg = loadingOverlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.35f);
            overlayImg.raycastTarget = true;

            GameObject barGO = new GameObject("LoadingBar");
            barGO.transform.SetParent(loadingOverlayGO.transform, false);
            loadingBarContainerRT = barGO.AddComponent<RectTransform>();
            loadingBarContainerRT.anchorMin = new Vector2(0.5f, 0.5f);
            loadingBarContainerRT.anchorMax = new Vector2(0.5f, 0.5f);
            loadingBarContainerRT.pivot = new Vector2(0.5f, 0.5f);
            loadingBarContainerRT.anchoredPosition = Vector2.zero;
            loadingBarContainerRT.sizeDelta = new Vector2(420, 10);
            Image barBg = barGO.AddComponent<Image>();
            barBg.color = new Color(1f, 1f, 1f, 0.18f);
            barBg.raycastTarget = false;

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(barGO.transform, false);
            loadingBarFillRT = fillGO.AddComponent<RectTransform>();
            loadingBarFillRT.anchorMin = new Vector2(0.5f, 0.5f);
            loadingBarFillRT.anchorMax = new Vector2(0.5f, 0.5f);
            loadingBarFillRT.pivot = new Vector2(0.5f, 0.5f);
            loadingBarFillRT.sizeDelta = new Vector2(120, 10);
            loadingBarFillRT.anchoredPosition = Vector2.zero;
            Image fillImg = fillGO.AddComponent<Image>();
            fillImg.color = new Color(1f, 1f, 1f, 0.85f);
            fillImg.raycastTarget = false;

            SetLayerRecursive(loadingOverlayGO, parentGO.layer);
            loadingOverlayGO.SetActive(false);
        }

        private void ShowLoadingOverlay(string message)
        {
            if (loadingOverlayGO == null) return;
            _loadingOverlayPulseStart = Time.unscaledTime;
            loadingOverlayGO.SetActive(true);
        }

        private void UpdateLoadingOverlayPulse()
        {
            if (loadingOverlayGO == null || !loadingOverlayGO.activeSelf || loadingBarFillRT == null || loadingBarContainerRT == null)
                return;
            if (_loadingOverlayPulseStart < 0f) _loadingOverlayPulseStart = Time.unscaledTime;
            float trackW = loadingBarContainerRT.sizeDelta.x;
            if (trackW <= 1f) trackW = 420f;
            float fillW = Mathf.Max(48f, trackW * 0.28f);
            float cycle = 1.35f;
            float t = ((Time.unscaledTime - _loadingOverlayPulseStart) % cycle) / cycle;
            float travel = trackW - fillW;
            loadingBarFillRT.sizeDelta = new Vector2(fillW, loadingBarFillRT.sizeDelta.y);
            loadingBarFillRT.anchoredPosition = new Vector2(-travel * 0.5f + travel * t, 0f);
        }

        private void HideLoadingOverlay()
        {
            _loadingOverlayPulseStart = -1f;
            if (loadingOverlayGO != null) loadingOverlayGO.SetActive(false);
        }

        // ── Thumbnail cache progress bar (1px, bottom of viewport) ─────────

        private void CreateThumbnailCacheProgressPanel(GameObject viewportGO)
        {
            if (viewportGO == null || _thumbCacheProgressGO != null) return;

            // 1px full-width bar anchored to the very bottom of the viewport
            _thumbCacheProgressGO = new GameObject("ThumbCacheProgress");
            _thumbCacheProgressGO.transform.SetParent(viewportGO.transform, false);
            RectTransform panelRT = _thumbCacheProgressGO.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0f, 0f);
            panelRT.anchorMax = new Vector2(1f, 0f);
            panelRT.pivot     = new Vector2(0.5f, 0f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta = new Vector2(0f, 1f);

            // Dark track (background)
            Image trackImg = _thumbCacheProgressGO.AddComponent<Image>();
            trackImg.color = new Color(1f, 1f, 1f, 0.12f);
            trackImg.raycastTarget = false;

            SetLayerRecursive(_thumbCacheProgressGO, viewportGO.layer);

            // Blue fill — grows from anchorMax.x=0 to 1
            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(_thumbCacheProgressGO.transform, false);
            _thumbCacheBarFillRT = fillGO.AddComponent<RectTransform>();
            _thumbCacheBarFillRT.anchorMin = Vector2.zero;
            _thumbCacheBarFillRT.anchorMax = new Vector2(0f, 1f);
            _thumbCacheBarFillRT.sizeDelta  = Vector2.zero;
            Image fillImg = fillGO.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.7f, 1f, 1f);
            fillImg.raycastTarget = false;

            _thumbCacheProgressGO.SetActive(false);
        }

        private void ShowThumbnailCacheProgress()
        {
            if (_thumbCacheProgressGO == null) return;
            _thumbCacheFinishTime = -1f;
            _thumbCacheProgressGO.SetActive(true);
        }

        private void HideThumbnailCacheProgress()
        {
            if (_thumbCacheProgressGO != null) _thumbCacheProgressGO.SetActive(false);
            _thumbCacheTotalEnqueued = 0;
            _thumbCacheSaved = 0;
            _thumbCacheFinishTime = -1f;
        }

        private void UpdateThumbnailCacheProgressDisplay()
        {
            if (_thumbCacheBarFillRT == null) return;
            float fraction = (_thumbCacheTotalEnqueued > 0)
                ? Mathf.Clamp01((float)_thumbCacheSaved / _thumbCacheTotalEnqueued)
                : 0f;
            _thumbCacheBarFillRT.anchorMax = new Vector2(fraction, 1f);
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
            GameObject overlayGO = new GameObject("TextInputOverlay");
            overlayGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;
            
            Image overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.5f);
            
            // Panel
            GameObject panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(overlayGO.transform, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.sizeDelta = new Vector2(400, 200);
            
            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            
            // Title
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 24;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -10);
            titleRT.sizeDelta = new Vector2(0, 40);

            // Input - Using CreateSearchInput logic from Tabs.cs but since it's private there, we re-implement or call if possible.
            // Actually, CreateSearchInput is private in GalleryPanel.Tabs.cs.
            // Let's create a simple InputField here.
            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(panelGO.transform, false);
            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.sizeDelta = new Vector2(350, 40);
            inputRT.anchoredPosition = new Vector2(0, 10);

            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.sizeDelta = new Vector2(-20, -10);

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 20;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform tRT = textGO.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.sizeDelta = Vector2.zero;

            input.textComponent = t;
            input.text = initialValue;
            // Match typical text field behavior: Ctrl+Backspace deletes previous word.
            inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input);

            // Buttons
            GameObject confirmBtn = UI.CreateUIButton(panelGO, 140, 45, "Confirm", 18, 80, -60, AnchorPresets.middleCenter, () => {
                onConfirm?.Invoke(input.text);
                Destroy(overlayGO);
            });
            
            GameObject cancelBtn = UI.CreateUIButton(panelGO, 140, 45, "Cancel", 18, -80, -60, AnchorPresets.middleCenter, () => {
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

            GameObject overlayGO = new GameObject("PersonAtomRenameOverlay");
            overlayGO.transform.SetParent(overlayParent, false);
            RectTransform overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            Image overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.55f);

            GameObject panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(overlayGO.transform, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(440f, 300f);

            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleTxt = titleGO.AddComponent<Text>();
            titleTxt.font = arial;
            titleTxt.fontSize = 22;
            titleTxt.color = Color.white;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.text = VPBTranslation.T("gallery.rename.title", "Rename Person Atom");
            RectTransform titleRt = titleGO.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0, -12f);
            titleRt.sizeDelta = new Vector2(-24f, 36f);

            GameObject oldLabelGO = new GameObject("OldNameLabel");
            oldLabelGO.transform.SetParent(panelGO.transform, false);
            Text oldLabelTxt = oldLabelGO.AddComponent<Text>();
            oldLabelTxt.font = arial;
            oldLabelTxt.fontSize = 16;
            oldLabelTxt.color = new Color(0.85f, 0.85f, 0.85f);
            oldLabelTxt.alignment = TextAnchor.MiddleLeft;
            oldLabelTxt.text = VPBTranslation.T("gallery.rename.old_name_label", "Old name");
            RectTransform oldLabelRt = oldLabelGO.GetComponent<RectTransform>();
            oldLabelRt.anchorMin = new Vector2(0, 1);
            oldLabelRt.anchorMax = new Vector2(1, 1);
            oldLabelRt.pivot = new Vector2(0.5f, 1f);
            oldLabelRt.anchoredPosition = new Vector2(0, -54f);
            oldLabelRt.sizeDelta = new Vector2(-28f, 22f);

            GameObject oldValGO = new GameObject("OldNameValue");
            oldValGO.transform.SetParent(panelGO.transform, false);
            Image oldValBg = oldValGO.AddComponent<Image>();
            oldValBg.color = new Color(0.18f, 0.18f, 0.2f, 1f);
            GameObject oldValTextGO = new GameObject("Text");
            oldValTextGO.transform.SetParent(oldValGO.transform, false);
            Text oldValTxt = oldValTextGO.AddComponent<Text>();
            oldValTxt.font = arial;
            oldValTxt.fontSize = 17;
            oldValTxt.color = Color.white;
            oldValTxt.alignment = TextAnchor.MiddleLeft;
            oldValTxt.text = oldUid;
            RectTransform oldValTxtRt = oldValTextGO.GetComponent<RectTransform>();
            oldValTxtRt.anchorMin = Vector2.zero;
            oldValTxtRt.anchorMax = Vector2.one;
            oldValTxtRt.offsetMin = new Vector2(10f, 4f);
            oldValTxtRt.offsetMax = new Vector2(-10f, -4f);
            RectTransform oldValRt = oldValGO.GetComponent<RectTransform>();
            oldValRt.anchorMin = new Vector2(0, 1);
            oldValRt.anchorMax = new Vector2(1, 1);
            oldValRt.pivot = new Vector2(0.5f, 1f);
            oldValRt.anchoredPosition = new Vector2(0, -82f);
            oldValRt.sizeDelta = new Vector2(-28f, 38f);

            GameObject renameLabelGO = new GameObject("RenameToLabel");
            renameLabelGO.transform.SetParent(panelGO.transform, false);
            Text renameLabelTxt = renameLabelGO.AddComponent<Text>();
            renameLabelTxt.font = arial;
            renameLabelTxt.fontSize = 16;
            renameLabelTxt.color = new Color(0.85f, 0.85f, 0.85f);
            renameLabelTxt.alignment = TextAnchor.MiddleLeft;
            renameLabelTxt.text = VPBTranslation.T("gallery.rename.rename_to_label", "Rename to");
            RectTransform renameLabelRt = renameLabelGO.GetComponent<RectTransform>();
            renameLabelRt.anchorMin = new Vector2(0, 1);
            renameLabelRt.anchorMax = new Vector2(1, 1);
            renameLabelRt.pivot = new Vector2(0.5f, 1f);
            renameLabelRt.anchoredPosition = new Vector2(0, -126f);
            renameLabelRt.sizeDelta = new Vector2(-28f, 22f);

            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(panelGO.transform, false);
            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            InputField input = inputGO.AddComponent<InputField>();
            RectTransform inputRt = inputGO.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0, 1);
            inputRt.anchorMax = new Vector2(1, 1);
            inputRt.pivot = new Vector2(0.5f, 1f);
            inputRt.anchoredPosition = new Vector2(0, -154f);
            inputRt.sizeDelta = new Vector2(-28f, 40f);

            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRt = textArea.AddComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(10f, 6f);
            textAreaRt.offsetMax = new Vector2(-10f, -6f);

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            Text tComp = textGO.AddComponent<Text>();
            tComp.font = arial;
            tComp.fontSize = 18;
            tComp.color = Color.white;
            tComp.alignment = TextAnchor.MiddleLeft;
            RectTransform tRt = textGO.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.sizeDelta = Vector2.zero;

            input.textComponent = tComp;
            input.text = oldUid;
            inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input);

            UnityAction close = () => { try { Destroy(overlayGO); } catch { } };

            GameObject renameBtn = UI.CreateUIButton(panelGO, 150f, 44f, VPBTranslation.T("gallery.rename.rename_btn", "Rename"), 18, 78f, -116f, AnchorPresets.middleCenter, () =>
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

            GameObject cancelBtn = UI.CreateUIButton(panelGO, 150f, 44f, VPBTranslation.T("gallery.rename.cancel_btn", "Cancel"), 18, -78f, -116f, AnchorPresets.middleCenter, close);

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
            input.ActivateInputField();
            input.MoveTextEnd(false);
        }

        public void DisplayConfirm(string title, string message, UnityAction onConfirm)
        {
            GameObject overlayGO = new GameObject("ConfirmOverlay");
            overlayGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;
            
            Image overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.5f);
            
            // Panel
            GameObject panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(overlayGO.transform, false);
            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.sizeDelta = new Vector2(500, 420);
            
            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            
            // Title
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 24;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -15);
            titleRT.sizeDelta = new Vector2(0, 40);

            // Message
            GameObject msgGO = new GameObject("Message");
            msgGO.transform.SetParent(panelGO.transform, false);
            Text msgText = msgGO.AddComponent<Text>();
            msgText.text = message;
            msgText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            msgText.fontSize = 18;
            msgText.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            msgText.alignment = TextAnchor.MiddleCenter;
            RectTransform msgRT = msgGO.GetComponent<RectTransform>();
            msgRT.anchorMin = Vector2.zero;
            msgRT.anchorMax = Vector2.one;
            msgRT.offsetMin = new Vector2(20, 80);
            msgRT.offsetMax = new Vector2(-20, -60);

            // Buttons
            GameObject cancelBtn = UI.CreateUIButton(panelGO, 160, 45, "Cancel", 18, -100, 40, AnchorPresets.bottomMiddle, () => Destroy(overlayGO));
            GameObject confirmBtn = UI.CreateUIButton(panelGO, 160, 45, "Confirm", 18, 100, 40, AnchorPresets.bottomMiddle, () => {
                onConfirm?.Invoke();
                Destroy(overlayGO);
            });
            confirmBtn.GetComponent<Image>().color = new Color(0.4f, 0.2f, 0.2f, 1f);

            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);
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

            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);

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

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 18;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -5);
            titleRT.sizeDelta = new Vector2(0, 24);

            GameObject listGO = new GameObject("List");
            listGO.transform.SetParent(panelGO.transform, false);
            RectTransform listRT = listGO.AddComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0, 0);
            listRT.anchorMax = new Vector2(1, 1);
            listRT.offsetMin = new Vector2(10, 10);
            listRT.offsetMax = new Vector2(-10, -34);

            VerticalLayoutGroup vlg = listGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = rowGap;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

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
                btn.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
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

            Image overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.01f);
            overlayImg.raycastTarget = true;

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

            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);

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

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 18;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -5);
            titleRT.sizeDelta = new Vector2(0, 24);

            GameObject listGO = new GameObject("List");
            listGO.transform.SetParent(panelGO.transform, false);
            RectTransform listRT = listGO.AddComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0, 0);
            listRT.anchorMax = new Vector2(1, 1);
            listRT.offsetMin = new Vector2(10, 10);
            listRT.offsetMax = new Vector2(-10, -34);

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
                btn.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
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

            Image overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.01f);
            overlayImg.raycastTarget = true;

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

            Image panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);

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

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 18;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -5);
            titleRT.sizeDelta = new Vector2(0, 24);

            GameObject listGO = new GameObject("List");
            listGO.transform.SetParent(panelGO.transform, false);
            RectTransform listRT = listGO.AddComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0, 0);
            listRT.anchorMax = new Vector2(1, 1);
            listRT.offsetMin = new Vector2(10, 10);
            listRT.offsetMax = new Vector2(-10, -34);

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
                btn.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
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
