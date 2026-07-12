using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject _qmPosModalRoot;
        private GameObject _qmPosVrSection;
        private Vector2 _qmPosOriginalCreate;
        private float _qmPosCreateX;
        private float _qmPosCreateY;
        private float _qmPosCreateXVR;
        private float _qmPosCreateYVR;
        private bool _qmPosUseSameInVR = true;
        private Slider _qmPosSliderX;
        private Slider _qmPosSliderY;
        private Slider _qmPosSliderXVR;
        private Slider _qmPosSliderYVR;
        private Text _qmPosSliderXLabel;
        private Text _qmPosSliderYLabel;
        private Text _qmPosSliderXVRLabel;
        private Text _qmPosSliderYVRLabel;
        private Text _qmPosSameVrCheckText;

        private const float QmPosXMin = -1000f;
        private const float QmPosXMax = 2000f;
        private const float QmPosYMin = -500f;
        private const float QmPosYMax = 1500f;

        public bool TryGetQuickMenuPositionEditorPreview(out float relX, out float relY)
        {
            relX = 0f;
            relY = 0f;
            if (_qmPosModalRoot == null || !_qmPosModalRoot.activeInHierarchy)
                return false;
            relX = _qmPosCreateX;
            relY = _qmPosCreateY;
            return true;
        }

        public void ShowQuickMenuPositionEditorModal()
        {
            if (backgroundBoxGO == null) return;

            var plugin = VamHookPlugin.singleton;
            if (plugin != null && !plugin.CanOpenQuickMenuPositionEditor())
            {
                ShowTemporaryStatus(VPBTranslation.T("hook.qmpos.vr_blocked", "Quick Menu position editor is desktop-only."), 3f);
                return;
            }

            if (_qmPosModalRoot != null)
            {
                _qmPosModalRoot.SetActive(true);
                return;
            }

            LoadQuickMenuPositionDraftFromSettings();
            BuildQuickMenuPositionEditorModal();
            ApplyQuickMenuPositionPreview();
        }

        private void LoadQuickMenuPositionDraftFromSettings()
        {
            Vector2 createPos = Vector2.zero;
            Vector2 createPosVR = Vector2.zero;
            try
            {
                if (Settings.Instance?.QuickMenuCreateGalleryPosDesktop != null)
                    createPos = Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value;
                if (Settings.Instance?.QuickMenuCreateGalleryPosVR != null)
                    createPosVR = Settings.Instance.QuickMenuCreateGalleryPosVR.Value;
            }
            catch { }

            _qmPosOriginalCreate = createPos;
            _qmPosCreateX = createPos.x - VamHookPlugin.QuickMenuAnchorBaseline.x;
            _qmPosCreateY = createPos.y - VamHookPlugin.QuickMenuAnchorBaseline.y;
            _qmPosCreateXVR = createPosVR.x - VamHookPlugin.QuickMenuAnchorBaseline.x;
            _qmPosCreateYVR = createPosVR.y - VamHookPlugin.QuickMenuAnchorBaseline.y;
            try
            {
                _qmPosUseSameInVR = Settings.Instance?.QuickMenuCreateGalleryUseSameInVR == null ||
                                    Settings.Instance.QuickMenuCreateGalleryUseSameInVR.Value;
            }
            catch { _qmPosUseSameInVR = true; }
        }

        private void ApplyQuickMenuPositionPreview()
        {
            try { VamHookPlugin.singleton?.GalleryPreviewQuickMenuGrid(_qmPosCreateX, _qmPosCreateY); }
            catch { }
        }

        private void HideQuickMenuPositionEditorModal(bool save)
        {
            if (_qmPosModalRoot == null) return;

            var plugin = VamHookPlugin.singleton;
            if (save)
            {
                try
                {
                    plugin?.GallerySaveQuickMenuGridPosition(_qmPosCreateX, _qmPosCreateY, _qmPosCreateXVR, _qmPosCreateYVR, _qmPosUseSameInVR);
                }
                catch { }
            }
            else
            {
                try { plugin?.GalleryRestoreQuickMenuGridPreview(_qmPosOriginalCreate); }
                catch { }
            }

            try { UnityEngine.Object.Destroy(_qmPosModalRoot); } catch { }
            _qmPosModalRoot = null;
            _qmPosVrSection = null;
            _qmPosSliderX = null;
            _qmPosSliderY = null;
            _qmPosSliderXVR = null;
            _qmPosSliderYVR = null;
        }

        private void BuildQuickMenuPositionEditorModal()
        {
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int headerFont = type.Title;
            int bodyFont = type.Body;

            _qmPosModalRoot = new GameObject("VPB_QuickMenuPosModal");
            _qmPosModalRoot.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRt = _qmPosModalRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Image dim = _qmPosModalRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true;
            Button dimBtn = _qmPosModalRoot.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(() => HideQuickMenuPositionEditorModal(false));

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_qmPosModalRoot.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f * s, 420f * s);
            Image pbg = panel.AddComponent<Image>();
            pbg.color = new Color(0.06f, 0.06f, 0.08f, 1f);
            pbg.raycastTarget = true;

            VerticalLayoutGroup v = UI.AddVLG(panel, spacing: 8f * s, padding: UI.Pad(14, 14, 14, 14, s));

            GameObject header = new GameObject("Header");
            header.transform.SetParent(panel.transform, false);
            Text ht = header.AddComponent<Text>();
            ht.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            GalleryUiMetrics.ApplyEmphasisTitle(ht, headerFont);
            ht.fontStyle = FontStyle.Normal;
            ht.color = Color.white;
            ht.text = VPBTranslation.T("hook.qmpos.title", "Quick Menu Positions (Desktop)");
            try { VPBUiFont.ApplyTo(ht); } catch { }
            LayoutElement hle = UI.AddLE(header, minHeight: 32f * s);

            QmPosBuildAxisSliderRow(panel.transform, VPBTranslation.T("hook.qmpos.create_gallery", "Create Gallery"), bodyFont, s,
                _qmPosCreateX, _qmPosCreateY, QmPosXMin, QmPosXMax, QmPosYMin, QmPosYMax,
                out _qmPosSliderX, out _qmPosSliderY, out _qmPosSliderXLabel, out _qmPosSliderYLabel,
                (x, y) =>
                {
                    _qmPosCreateX = x;
                    _qmPosCreateY = y;
                    UpdateQmPosSliderLabels();
                    ApplyQuickMenuPositionPreview();
                });

            GameObject sameRow = new GameObject("SameVrRow");
            sameRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup srh = sameRow.AddComponent<HorizontalLayoutGroup>();
            srh.spacing = 8f * s;
            srh.childAlignment = TextAnchor.MiddleLeft;
            LayoutElement srLe = UI.AddLE(sameRow, minHeight: 34f * s);

            GameObject sameCheckBtn = QmPosCreateTextButton(sameRow.transform, 36f * s, 30f * s, _qmPosUseSameInVR ? "✓" : " ", bodyFont, new Color(0.2f, 0.35f, 0.5f, 1f), () =>
            {
                _qmPosUseSameInVR = !_qmPosUseSameInVR;
                SyncQmPosVrSectionVisibility();
                if (_qmPosSameVrCheckText != null)
                    _qmPosSameVrCheckText.text = _qmPosUseSameInVR ? "✓" : " ";
            });
            Transform sameCheckTextTr = sameCheckBtn != null ? sameCheckBtn.transform.Find("Text") : null;
            _qmPosSameVrCheckText = sameCheckTextTr != null ? sameCheckTextTr.GetComponent<Text>() : null;

            Text sl = UI.CreateLabel(sameRow, VPBTranslation.T("hook.qmpos.same_vr", "Use same position in VR mode"), bodyFont, Color.white, name: "SameVrLabel");
            LayoutElement sll = UI.AddLE(sl.gameObject, flexibleWidth: 1f);

            _qmPosVrSection = new GameObject("VrSection");
            _qmPosVrSection.transform.SetParent(panel.transform, false);
            VerticalLayoutGroup vvg = _qmPosVrSection.AddComponent<VerticalLayoutGroup>();
            vvg.spacing = 6f * s;
            vvg.childControlWidth = true;
            vvg.childControlHeight = true;
            vvg.childForceExpandWidth = true;
            LayoutElement vsLe = UI.AddLE(_qmPosVrSection, flexibleWidth: 1f);

            QmPosBuildAxisSliderRow(_qmPosVrSection.transform, VPBTranslation.T("hook.qmpos.create_gallery_vr", "Create Gallery (VR)"), bodyFont, s,
                _qmPosCreateXVR, _qmPosCreateYVR, QmPosXMin, QmPosXMax, QmPosYMin, QmPosYMax,
                out _qmPosSliderXVR, out _qmPosSliderYVR, out _qmPosSliderXVRLabel, out _qmPosSliderYVRLabel,
                (x, y) =>
                {
                    _qmPosCreateXVR = x;
                    _qmPosCreateYVR = y;
                    UpdateQmPosSliderLabels();
                });

            SyncQmPosVrSectionVisibility();

            GameObject footer = new GameObject("Footer");
            footer.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup fh = footer.AddComponent<HorizontalLayoutGroup>();
            fh.spacing = 8f * s;
            fh.childControlWidth = true;
            fh.childControlHeight = true;
            fh.childForceExpandWidth = true;
            LayoutElement fLe = UI.AddLE(footer, minHeight: 40f * s);

            QmPosCreateTextButton(footer.transform, 0f, 36f * s, VPBTranslation.T("hook.cancel", "Cancel"), bodyFont, new Color(0.4f, 0.36f, 0.28f, 1f), () => HideQuickMenuPositionEditorModal(false));
            QmPosCreateTextButton(footer.transform, 0f, 36f * s, VPBTranslation.T("hook.defaults", "Defaults"), bodyFont, new Color(0.28f, 0.28f, 0.32f, 1f), () =>
            {
                try
                {
                    VamHookPlugin.singleton?.GalleryResetQuickMenuGridPositionDefaults(out _qmPosCreateX, out _qmPosCreateY);
                    _qmPosCreateXVR = 0f;
                    _qmPosCreateYVR = 0f;
                    SyncQmPosSlidersFromDraft();
                    ApplyQuickMenuPositionPreview();
                }
                catch { }
            });
            QmPosCreateTextButton(footer.transform, 0f, 36f * s, VPBTranslation.T("hook.save", "Save"), bodyFont, new Color(0.22f, 0.42f, 0.58f, 1f), () => HideQuickMenuPositionEditorModal(true));
        }

        private void SyncQmPosVrSectionVisibility()
        {
            if (_qmPosVrSection != null)
                _qmPosVrSection.SetActive(!_qmPosUseSameInVR);
        }

        private void SyncQmPosSlidersFromDraft()
        {
            if (_qmPosSliderX != null) _qmPosSliderX.value = _qmPosCreateX;
            if (_qmPosSliderY != null) _qmPosSliderY.value = _qmPosCreateY;
            if (_qmPosSliderXVR != null) _qmPosSliderXVR.value = _qmPosCreateXVR;
            if (_qmPosSliderYVR != null) _qmPosSliderYVR.value = _qmPosCreateYVR;
            UpdateQmPosSliderLabels();
        }

        private void UpdateQmPosSliderLabels()
        {
            if (_qmPosSliderXLabel != null) _qmPosSliderXLabel.text = "X: " + Mathf.RoundToInt(_qmPosCreateX);
            if (_qmPosSliderYLabel != null) _qmPosSliderYLabel.text = "Y: " + Mathf.RoundToInt(_qmPosCreateY);
            if (_qmPosSliderXVRLabel != null) _qmPosSliderXVRLabel.text = "X: " + Mathf.RoundToInt(_qmPosCreateXVR);
            if (_qmPosSliderYVRLabel != null) _qmPosSliderYVRLabel.text = "Y: " + Mathf.RoundToInt(_qmPosCreateYVR);
        }

        private void QmPosBuildAxisSliderRow(Transform parent, string sectionTitle, int bodyFont, float s,
            float initX, float initY, float xMin, float xMax, float yMin, float yMax,
            out Slider sliderX, out Slider sliderY, out Text labelX, out Text labelY,
            Action<float, float> onChanged)
        {
            sliderX = null;
            sliderY = null;
            labelX = null;
            labelY = null;

            GameObject block = new GameObject("AxisBlock");
            block.transform.SetParent(parent, false);
            VerticalLayoutGroup bvg = block.AddComponent<VerticalLayoutGroup>();
            bvg.spacing = 4f * s;
            bvg.childControlWidth = true;
            bvg.childControlHeight = true;
            bvg.childForceExpandWidth = true;
            LayoutElement ble = UI.AddLE(block, minHeight: 88f * s);

            Text st = UI.CreateLabel(block, sectionTitle ?? "", bodyFont, new Color(0.85f, 0.9f, 1f, 1f), name: "SectionTitle");

            QmPosBuildSingleAxisSlider(block.transform, "X", bodyFont, s, initX, xMin, xMax, out sliderX, out labelX);
            QmPosBuildSingleAxisSlider(block.transform, "Y", bodyFont, s, initY, yMin, yMax, out sliderY, out labelY);

            Slider sx = sliderX;
            Slider sy = sliderY;
            Text lx = labelX;
            Text ly = labelY;

            if (sx != null)
            {
                sx.onValueChanged.AddListener(v =>
                {
                    float rx = Mathf.Round(Mathf.Clamp(v, xMin, xMax));
                    if (lx != null) lx.text = "X: " + (int)rx;
                    float cy = sy != null ? Mathf.Round(sy.value) : initY;
                    onChanged?.Invoke(rx, cy);
                });
            }

            if (sy != null)
            {
                sy.onValueChanged.AddListener(v =>
                {
                    float ry = Mathf.Round(Mathf.Clamp(v, yMin, yMax));
                    if (ly != null) ly.text = "Y: " + (int)ry;
                    float cx = sx != null ? Mathf.Round(sx.value) : initX;
                    onChanged?.Invoke(cx, ry);
                });
            }
        }

        private static void QmPosBuildSingleAxisSlider(Transform parent, string axis, int fontSize, float s,
            float value, float min, float max, out Slider slider, out Text valueLabel)
        {
            GameObject row = new GameObject(axis + "Row");
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f * s;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            LayoutElement rle = UI.AddLE(row, minHeight: 32f * s);

            valueLabel = UI.CreateLabel(row, axis + ": " + Mathf.RoundToInt(value), fontSize, Color.white, name: "ValueLabel");
            LayoutElement lblLe = UI.AddLE(valueLabel.gameObject, minWidth: 56f * s);

            GameObject sliderHost = new GameObject("Slider");
            sliderHost.transform.SetParent(row.transform, false);
            LayoutElement sle = UI.AddLE(sliderHost, minHeight: 28f * s, flexibleWidth: 1f);

            slider = sliderHost.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.value = Mathf.Clamp(Mathf.Round(value), min, max);

            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderHost.transform, false);
            Image bgImg = UI.AddGalleryElementRoundedBg(bg, new Color(0.2f, 0.2f, 0.22f, 1f));
            RectTransform bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.25f);
            bgRt.anchorMax = new Vector2(1, 0.75f);
            bgRt.sizeDelta = Vector2.zero;

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderHost.transform, false);
            RectTransform faRt = fillArea.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0, 0.25f);
            faRt.anchorMax = new Vector2(1, 0.75f);
            faRt.sizeDelta = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.25f, 0.5f, 0.8f, 1f);
            RectTransform fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            slider.fillRect = fillRt;

            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderHost.transform, false);
            RectTransform haRt = handleArea.AddComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero;
            haRt.anchorMax = Vector2.one;
            haRt.sizeDelta = Vector2.zero;

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            RectTransform handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(16f, 0f);
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
        }

        private static GameObject QmPosCreateTextButton(Transform parent, float width, float height, string label, int fontSize, Color bg, UnityAction onClick)
        {
            GameObject go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            Image img = UI.AddGalleryElementRoundedBg(go, bg);
            Button b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            UIHoverBorder hb = go.AddComponent<UIHoverBorder>();
            try { hb.ApplyBorderSettings(); } catch { }

            LayoutElement le = go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                le.minWidth = le.preferredWidth = width;
                le.flexibleWidth = 0f;
            }
            else
                le.flexibleWidth = 1f;
            le.minHeight = le.preferredHeight = height;

            UI.CreateLabel(go, label ?? "", fontSize, Color.white, TextAnchor.MiddleCenter, name: "Text");
            return go;
        }
    }
}
