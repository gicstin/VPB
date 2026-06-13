using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>Modal RGB picker: full-gallery dim + centered panel, own Canvas sorting so it stacks above gallery grid.</summary>
    public class UIColorPicker : MonoBehaviour
    {
        private static UIColorPicker _instance;
        private const float WindowWidth = 400f;
        private const float WindowHeight = 460f;

        public static UIColorPicker Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("UIColorPicker");
                    _instance = go.AddComponent<UIColorPicker>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private GameObject panelGO;
        private Image previewImg;
        private Slider sliderR, sliderG, sliderB;
        private InputField inputHex;
        private Action<Color> onConfirm;
        private Color currentColor = Color.white;
        private bool ignoreCallbacks;
        private Text titleTextComponent;

        public void Show(Color startColor, Action<Color> callback)
        {
            Show(startColor, callback, null, null);
        }

        /// <param name="modalHost">Typically gallery <see cref="GalleryPanel"/> background rect (full window card).</param>
        public void Show(Color startColor, Action<Color> callback, string optionalTitle, Transform modalHost)
        {
            Transform host = ResolveModalHost(modalHost);
            if (host == null) return;

            // Old picker layout lacked separate window / overlay Canvas.
            if (panelGO == null || panelGO.transform.Find("ColorPickerWindow") == null)
                BuildPanelUi(host);

            AttachPanelToHost(host);
            EnsureModalSorting(host);

            if (titleTextComponent != null)
                titleTextComponent.text = string.IsNullOrEmpty(optionalTitle) ? "Pick a Color" : optionalTitle;

            onConfirm = callback;
            panelGO.transform.SetAsLastSibling();
            panelGO.SetActive(true);
            SetColor(startColor, false);
        }

        public void Hide()
        {
            onConfirm = null;
            if (panelGO != null) panelGO.SetActive(false);
        }

        private static Transform ResolveModalHost(Transform modalHost)
        {
            if (modalHost != null) return modalHost;
            Canvas c = FindObjectOfType<Canvas>();
            return c != null ? c.transform : null;
        }

        private void AttachPanelToHost(Transform host)
        {
            if (panelGO == null || host == null) return;
            panelGO.transform.SetParent(host, false);
            StretchFull(panelGO.GetComponent<RectTransform>());
            try { ApplyLayerRecursive(panelGO, host.gameObject.layer); } catch { }
        }

        private void EnsureModalSorting(Transform host)
        {
            if (panelGO == null) return;
            Canvas overlayCanvas = panelGO.GetComponent<Canvas>();
            if (overlayCanvas == null) overlayCanvas = panelGO.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true;

            Canvas parentCanvas = host != null ? host.GetComponentInParent<Canvas>() : null;
            overlayCanvas.sortingOrder = parentCanvas != null ? parentCanvas.sortingOrder + 200 : 5000;

            if (panelGO.GetComponent<GraphicRaycaster>() == null)
                panelGO.AddComponent<GraphicRaycaster>();
        }

        private static void ApplyLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                ApplyLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        private void BuildPanelUi(Transform initialHost)
        {
            if (panelGO != null)
            {
                Destroy(panelGO);
                panelGO = null;
                previewImg = null;
                sliderR = sliderG = sliderB = null;
                inputHex = null;
                titleTextComponent = null;
            }

            panelGO = new GameObject("ColorPickerModalRoot");
            panelGO.transform.SetParent(initialHost, false);
            StretchFull(panelGO);

            Canvas canvas = panelGO.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.pixelPerfect = false;
            panelGO.AddComponent<GraphicRaycaster>();

            GameObject dim = new GameObject("DimBackdrop");
            dim.transform.SetParent(panelGO.transform, false);
            StretchFull(dim);
            Image dimImg = dim.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimImg.raycastTarget = true;

            GameObject window = new GameObject("ColorPickerWindow");
            window.transform.SetParent(panelGO.transform, false);
            RectTransform windowRT = window.AddComponent<RectTransform>();
            windowRT.anchorMin = new Vector2(0.5f, 0.5f);
            windowRT.anchorMax = new Vector2(0.5f, 0.5f);
            windowRT.pivot = new Vector2(0.5f, 0.5f);
            windowRT.anchoredPosition = Vector2.zero;
            windowRT.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            Image winBg = window.AddComponent<Image>();
            winBg.color = new Color(0.17f, 0.17f, 0.19f, 1f);
            winBg.raycastTarget = true;

            Outline winOutline = window.AddComponent<Outline>();
            winOutline.effectDistance = new Vector2(2f, -2f);
            winOutline.effectColor = new Color(0f, 0f, 0f, 0.6f);

            VerticalLayoutGroup vlg = window.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 22, 20);
            vlg.spacing = 12;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            GameObject titleGo = CreateWindowText(window, "Pick a Color", 20, FontStyle.Normal, Color.white, TextAnchor.UpperCenter, true);
            titleTextComponent = titleGo.GetComponent<Text>();

            GameObject hintGo = CreateWindowText(
                window,
                "Hex #RRGGBB, bare hex, or R,G,B (0–255 or 0–1).",
                13,
                FontStyle.Normal,
                new Color(0.74f, 0.74f, 0.76f),
                TextAnchor.MiddleCenter,
                false);
            Text hintT = hintGo.GetComponent<Text>();
            hintT.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintT.verticalOverflow = VerticalWrapMode.Truncate;
            LayoutElement hintLe = hintGo.AddComponent<LayoutElement>();
            hintLe.flexibleHeight = 0;
            hintLe.preferredHeight = 52;

            previewImg = CreatePreviewStripe(window).GetComponent<Image>();

            sliderR = CreateSliderRow(window, "R", Color.red);
            sliderG = CreateSliderRow(window, "G", Color.green);
            sliderB = CreateSliderRow(window, "B", Color.blue);

            inputHex = CreateCodeInput(window, TryApplyCodeInput);

            GameObject btnRow = new GameObject("ButtonRow");
            btnRow.transform.SetParent(window.transform, false);
            HorizontalLayoutGroup hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            LayoutElement rowLe = btnRow.AddComponent<LayoutElement>();
            rowLe.flexibleHeight = 0;
            rowLe.preferredHeight = 48;

            CreateDialogButton(hlg.transform, "Cancel", Hide);
            CreateDialogButton(hlg.transform, "OK", () =>
            {
                if (onConfirm != null) onConfirm(currentColor);
                Hide();
            });

            panelGO.SetActive(false);
        }

        private static GameObject CreatePreviewStripe(GameObject parent)
        {
            GameObject go = new GameObject("Preview");
            go.transform.SetParent(parent.transform, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = 0;
            le.preferredHeight = 52;
            Image img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
            return go;
        }

        private static GameObject CreateWindowText(
            GameObject parent,
            string text,
            int fontSize,
            FontStyle style,
            Color color,
            TextAnchor align,
            bool boldLine)
        {
            GameObject go = new GameObject("Text_" + (boldLine ? "Title" : "Body"));
            go.transform.SetParent(parent.transform, false);
            Text t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            try { VPBUiFont.ApplyTo(t); } catch { }

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = 0;
            le.preferredHeight = boldLine ? 30 : 48;
            le.minWidth = 1;
            return go;
        }

        private static void CreateDialogButton(Transform row, string label, UnityAction onClick)
        {
            GameObject go = new GameObject("Btn_" + label);
            go.transform.SetParent(row, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 44f;

            Image img = go.AddComponent<Image>();
            img.color = new Color(0.26f, 0.26f, 0.3f, 1f);
            img.raycastTarget = true;

            // Match gallery hover border behavior on buttons too.
            try
            {
                UIHoverBorder hb = go.AddComponent<UIHoverBorder>();
                hb.targetGraphic = img;
                hb.hoverColor = VPBConfig.Instance != null ? VPBConfig.Instance.GetGalleryGridBorderColor() : new Color(1f, 1f, 0f, 1f);
                hb.borderSize = 2f;
                hb.inward = false;
                hb.isSelected = false;
                hb.ApplyBorderSettings();
            }
            catch { }

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = cb;
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) btn.onClick.AddListener(onClick);

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = GalleryUiDesignTokens.FontBodyRef;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            try { VPBUiFont.ApplyTo(t); } catch { }
            StretchFull(textGO);
        }

        private Slider CreateSliderRow(GameObject parent, string label, Color tint)
        {
            GameObject row = new GameObject("Row_" + label);
            row.transform.SetParent(parent.transform, false);
            LayoutElement rowLe = row.AddComponent<LayoutElement>();
            rowLe.flexibleHeight = 0;
            rowLe.preferredHeight = 34;

            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;

            GameObject lab = new GameObject("Label");
            lab.transform.SetParent(row.transform, false);
            Text lt = lab.AddComponent<Text>();
            lt.text = label;
            lt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            lt.fontSize = GalleryUiDesignTokens.FontBodyRef;
            lt.fontStyle = FontStyle.Normal;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleLeft;
            lt.raycastTarget = false;
            try { VPBUiFont.ApplyTo(lt); } catch { }
            LayoutElement labLe = lab.AddComponent<LayoutElement>();
            labLe.flexibleWidth = 0;
            labLe.preferredWidth = 22;

            GameObject sliderHost = new GameObject("Slider");
            sliderHost.transform.SetParent(row.transform, false);
            LayoutElement shLe = sliderHost.AddComponent<LayoutElement>();
            shLe.flexibleWidth = 1f;
            shLe.minWidth = 50;
            shLe.preferredHeight = 30;

            Slider s = sliderHost.AddComponent<Slider>();
            s.minValue = 0;
            s.maxValue = 1;

            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderHost.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.09f, 1f);
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.2f);
            bgRT.anchorMax = new Vector2(1, 0.8f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderHost.transform, false);
            RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.2f);
            fillAreaRT.anchorMax = new Vector2(1, 0.8f);
            fillAreaRT.offsetMin = new Vector2(6, 0);
            fillAreaRT.offsetMax = new Vector2(-18, 0);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = tint;
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            s.fillRect = fillRT;

            GameObject handleArea = new GameObject("Handle Area");
            handleArea.transform.SetParent(sliderHost.transform, false);
            RectTransform handleAreaRT = handleArea.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = Vector2.zero;
            handleAreaRT.anchorMax = Vector2.one;
            handleAreaRT.offsetMin = new Vector2(10, 0);
            handleAreaRT.offsetMax = new Vector2(-18, 0);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            RectTransform handleRT = handle.GetComponent<RectTransform>();
            handleRT.anchorMin = new Vector2(0.5f, 0.5f);
            handleRT.anchorMax = new Vector2(0.5f, 0.5f);
            handleRT.sizeDelta = new Vector2(12f, 16f);

            s.handleRect = handleRT;
            s.targetGraphic = handleImg;
            s.onValueChanged.AddListener(_ => UpdateFromSliders());
            return s;
        }

        private InputField CreateCodeInput(GameObject parent, UnityAction<string> apply)
        {
            GameObject go = new GameObject("ColorCodeInput");
            go.transform.SetParent(parent.transform, false);
            LayoutElement rowLe = go.AddComponent<LayoutElement>();
            rowLe.flexibleHeight = 0;
            rowLe.preferredHeight = 40;

            Image bgImg = go.AddComponent<Image>();
            bgImg.color = new Color(0.07f, 0.07f, 0.08f, 1f);

            InputField input = go.AddComponent<InputField>();
            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(go.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(10, 5);
            textAreaRT.offsetMax = new Vector2(-10, -5);

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(textArea.transform, false);
            Text t = textGo.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = GalleryUiDesignTokens.FontBodyRef;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            try { VPBUiFont.ApplyTo(t); } catch { }
            StretchFull(textGo);

            GameObject placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);
            Text ph = placeholder.AddComponent<Text>();
            ph.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            ph.fontSize = GalleryUiDesignTokens.FontBodyRef;
            ph.color = new Color(1f, 1f, 1f, 0.35f);
            ph.text = "#RRGGBB or R,G,B";
            try { VPBUiFont.ApplyTo(ph); } catch { }
            StretchFull(placeholder);

            input.textComponent = t;
            input.placeholder = ph;
            input.text = "";

            StretchFull(go);
            go.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input);

            if (apply != null)
            {
                input.onValueChanged.AddListener(apply);
                input.onEndEdit.AddListener(apply);
            }
            return input;
        }

        private void TryApplyCodeInput(string val)
        {
            if (ignoreCallbacks) return;
            Color alphaFb = currentColor;
            if (TryParseFlexibleColor(val, alphaFb, out Color c))
                SetColor(c, false);
        }

        public static bool TryParseFlexibleColor(string raw, Color alphaFallback, out Color result)
        {
            result = alphaFallback;
            if (raw == null) return false;
            string s = raw.Trim();
            if (s.Length == 0) return false;

            int commaIdx = s.IndexOf(',');
            if (commaIdx >= 0)
            {
                string[] parts = s.Split(',');
                if (parts.Length >= 3)
                {
                    if (!TryParseRgbToken(parts[0], out float r0)) return false;
                    if (!TryParseRgbToken(parts[1], out float g0)) return false;
                    if (!TryParseRgbToken(parts[2], out float b0)) return false;
                    float r = NormalizeUnitOrByte(r0);
                    float g = NormalizeUnitOrByte(g0);
                    float b = NormalizeUnitOrByte(b0);
                    float a = alphaFallback.a;
                    if (parts.Length >= 4 && TryParseRgbToken(parts[3], out float aIn))
                        a = NormalizeUnitOrByte(aIn);
                    result = new Color(r, g, b, Mathf.Clamp01(a));
                    return true;
                }
                return false;
            }

            string hex = s;
            if (hex.Length > 0 && hex[0] != '#')
            {
                bool allHex = true;
                for (int i = 0; i < hex.Length; i++)
                {
                    char ch = hex[i];
                    bool ok = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                    if (!ok) { allHex = false; break; }
                }
                if ((hex.Length == 6 || hex.Length == 8) && allHex)
                    hex = "#" + hex;
            }

            if (ColorUtility.TryParseHtmlString(hex, out Color hc))
            {
                if (hex != null && hex.Length >= 9 && hex[0] == '#')
                    result = hc;
                else
                    result = new Color(hc.r, hc.g, hc.b, alphaFallback.a);
                return true;
            }

            return false;
        }

        private static bool TryParseRgbToken(string tok, out float v)
        {
            v = 0f;
            return float.TryParse(tok.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static float NormalizeUnitOrByte(float x)
        {
            if (x > 1.002f) return Mathf.Clamp01(x / 255f);
            return Mathf.Clamp01(x);
        }

        private static void StretchFull(GameObject go)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
        private static void StretchFull(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private void SetColor(Color c, bool updateInputsOnly)
        {
            currentColor = c;
            if (previewImg != null) previewImg.color = c;

            if (!updateInputsOnly)
            {
                ignoreCallbacks = true;
                if (sliderR != null) sliderR.value = c.r;
                if (sliderG != null) sliderG.value = c.g;
                if (sliderB != null) sliderB.value = c.b;
                if (inputHex != null)
                    inputHex.text = "#" + ColorUtility.ToHtmlStringRGBA(new Color(c.r, c.g, c.b, 1f)).Substring(0, 6);
                ignoreCallbacks = false;
            }
        }

        private void UpdateFromSliders()
        {
            if (ignoreCallbacks) return;
            if (sliderR == null || sliderG == null || sliderB == null) return;
            float aKeep = currentColor.a;
            Color c = new Color(sliderR.value, sliderG.value, sliderB.value, aKeep);
            currentColor = c;
            if (previewImg != null) previewImg.color = c;

            ignoreCallbacks = true;
            if (inputHex != null)
                inputHex.text = "#" + ColorUtility.ToHtmlStringRGBA(new Color(c.r, c.g, c.b, 1f)).Substring(0, 6);
            ignoreCallbacks = false;
        }
    }
}
