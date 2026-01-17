using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace VPB
{
    public class UIColorPicker : MonoBehaviour
    {
        private static UIColorPicker _instance;
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
        private Color currentColor;
        private bool ignoreCallbacks = false;

        private void Awake()
        {
            CreateUI();
        }

        private void CreateUI()
        {
            Canvas canvas = FindObjectOfType<Canvas>(); // Basic find, better if parented to main UI
            if (canvas == null) return;

            panelGO = new GameObject("ColorPickerPanel");
            panelGO.transform.SetParent(canvas.transform, false);
            
            // Backdrop
            Image bg = panelGO.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.8f);
            RectTransform rt = panelGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            // Center Box
            GameObject box = new GameObject("Box");
            box.transform.SetParent(panelGO.transform, false);
            Image boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            RectTransform boxRT = box.GetComponent<RectTransform>();
            boxRT.sizeDelta = new Vector2(300, 350);
            boxRT.anchorMin = new Vector2(0.5f, 0.5f);
            boxRT.anchorMax = new Vector2(0.5f, 0.5f);

            // VLayout
            VerticalLayoutGroup vlg = box.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.spacing = 10;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;

            // Title
            CreateText(box, "Pick a Color", 24, Color.white);

            // Preview
            GameObject previewGO = new GameObject("Preview");
            previewGO.transform.SetParent(box.transform, false);
            previewImg = previewGO.AddComponent<Image>();
            LayoutElement le = previewGO.AddComponent<LayoutElement>();
            le.preferredHeight = 50;
            le.preferredWidth = 260;

            // Sliders
            sliderR = CreateSlider(box, "R", Color.red);
            sliderG = CreateSlider(box, "G", Color.green);
            sliderB = CreateSlider(box, "B", Color.blue);

            // Hex Input
            GameObject hexGO = CreateInputField(box, 260, 30, "#FFFFFF", (val) => {
                if (ignoreCallbacks) return;
                if (ColorUtility.TryParseHtmlString(val, out Color c))
                {
                    SetColor(c, false);
                }
            });
            inputHex = hexGO.GetComponent<InputField>();

            // Buttons
            GameObject btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(box.transform, false);
            HorizontalLayoutGroup hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            LayoutElement rowLE = btnRow.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 40;
            rowLE.preferredWidth = 260;

            UI.CreateUIButton(btnRow, 100, 40, "Cancel", 18, 0, 0, AnchorPresets.middleCenter, Hide);
            UI.CreateUIButton(btnRow, 100, 40, "Confirm", 18, 0, 0, AnchorPresets.middleCenter, () => {
                onConfirm?.Invoke(currentColor);
                Hide();
            });

            panelGO.SetActive(false);
        }

        private GameObject CreateText(GameObject parent, string text, int fontSize, Color color)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent.transform, false);
            Text t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 4;
            le.preferredHeight = fontSize + 4;
            
            return go;
        }

        private GameObject CreateInputField(GameObject parent, float width, float height, string defaultText, UnityEngine.Events.UnityAction<string> onValueChanged)
        {
            GameObject go = new GameObject("InputField");
            go.transform.SetParent(parent.transform, false);
            
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            InputField input = go.AddComponent<InputField>();
            
            // Text Area
            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(go.transform, false);
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(5, 5);
            textAreaRT.offsetMax = new Vector2(-5, -5);

            GameObject text = new GameObject("Text");
            text.transform.SetParent(textArea.transform, false);
            Text t = text.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 14;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            
            input.textComponent = t;
            input.text = defaultText;
            
            if (onValueChanged != null) input.onValueChanged.AddListener(onValueChanged);

            return go;
        }

        private Slider CreateSlider(GameObject parent, string label, Color tint)
        {
            GameObject go = new GameObject("Slider" + label);
            go.transform.SetParent(parent.transform, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 30;
            le.preferredWidth = 260;

            Slider s = go.AddComponent<Slider>();
            s.minValue = 0;
            s.maxValue = 1;

            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.25f);
            bgRT.anchorMax = new Vector2(1, 0.75f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.25f);
            fillAreaRT.anchorMax = new Vector2(1, 0.75f);
            fillAreaRT.offsetMin = new Vector2(5, 0);
            fillAreaRT.offsetMax = new Vector2(-5, 0);

            // Fill
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = tint;
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.sizeDelta = Vector2.zero;

            s.fillRect = fillRT;

            // Handle Area
            GameObject handleArea = new GameObject("Handle Area");
            handleArea.transform.SetParent(go.transform, false);
            RectTransform handleAreaRT = handleArea.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = new Vector2(0, 0);
            handleAreaRT.anchorMax = new Vector2(1, 1);
            handleAreaRT.offsetMin = new Vector2(10, 0);
            handleAreaRT.offsetMax = new Vector2(-10, 0);

            // Handle
            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            RectTransform handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(20, 0);
            
            s.handleRect = handleRT;
            s.onValueChanged.AddListener((v) => UpdateFromSliders());

            return s;
        }

        public void Show(Color startColor, Action<Color> callback)
        {
            if (panelGO == null) CreateUI(); // Re-create if missing (e.g. scene change)
            
            // Ensure visible and on top
            panelGO.transform.SetAsLastSibling();
            panelGO.SetActive(true);
            
            onConfirm = callback;
            SetColor(startColor, false);
        }

        public void Hide()
        {
            if (panelGO != null) panelGO.SetActive(false);
        }

        private void SetColor(Color c, bool updateInputs)
        {
            currentColor = c;
            previewImg.color = c;
            
            if (!updateInputs)
            {
                ignoreCallbacks = true;
                sliderR.value = c.r;
                sliderG.value = c.g;
                sliderB.value = c.b;
                inputHex.text = "#" + ColorUtility.ToHtmlStringRGBA(c);
                ignoreCallbacks = false;
            }
        }

        private void UpdateFromSliders()
        {
            if (ignoreCallbacks) return;
            Color c = new Color(sliderR.value, sliderG.value, sliderB.value, 1f);
            SetColor(c, true);
            
            ignoreCallbacks = true;
            inputHex.text = "#" + ColorUtility.ToHtmlStringRGBA(c);
            ignoreCallbacks = false;
        }
    }
}
