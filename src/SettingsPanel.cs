using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public class SettingsPanel
    {
        private GameObject backgroundBoxGO;
        private GameObject settingsPaneGO;
        private RectTransform settingsPaneRT;
        private GameObject settingsScrollContent;
        
        private bool isSettingsOpen = false;
        private bool settingsOnRight = true;
        
        // Pending settings state
        private bool pendingEnableButtonGaps;
        private bool backupEnableButtonGaps;
        
        private string pendingShowSideButtons;
        private string backupShowSideButtons;

        private bool pendingFollowAngle;
        private bool backupFollowAngle;

        private bool pendingFollowDistance;
        private bool backupFollowDistance;

        private float pendingFollowDistanceMeters;
        private float backupFollowDistanceMeters;

        private bool pendingFollowEyeHeight;
        private bool backupFollowEyeHeight;

        private float pendingReorientStartAngle;
        private float backupReorientStartAngle;

        private float pendingMovementThreshold;
        private float backupMovementThreshold;

        private bool pendingEnableGalleryFade;
        private bool backupEnableGalleryFade;

        private bool pendingEnableGalleryTranslucency;
        private bool backupEnableGalleryTranslucency;

        private float pendingGalleryOpacity;
        private float backupGalleryOpacity;

        private bool pendingDragDropReplaceMode;
        private bool backupDragDropReplaceMode;

        private GameObject tooltipGO;
        private Text tooltipText;

        public SettingsPanel(GameObject parent)
        {
            this.backgroundBoxGO = parent;
        }

        public void Toggle(bool onRight)
        {
            if (isSettingsOpen && settingsOnRight == onRight)
            {
                Close();
            }
            else
            {
                Open(onRight);
            }
        }

        public void Open(bool onRight)
        {
            if (settingsPaneGO == null) CreatePane();
            
            isSettingsOpen = true;
            settingsOnRight = onRight;
            settingsPaneGO.SetActive(true);
            
            // Initialize pending settings from current config
            pendingEnableButtonGaps = VPBConfig.Instance.EnableButtonGaps;
            backupEnableButtonGaps = VPBConfig.Instance.EnableButtonGaps;
            
            pendingShowSideButtons = VPBConfig.Instance.ShowSideButtons;
            backupShowSideButtons = VPBConfig.Instance.ShowSideButtons;

            pendingFollowAngle = VPBConfig.Instance.FollowAngle;
            backupFollowAngle = VPBConfig.Instance.FollowAngle;

            pendingFollowDistance = VPBConfig.Instance._followDistance;
            backupFollowDistance = VPBConfig.Instance._followDistance;

            pendingFollowDistanceMeters = VPBConfig.Instance.FollowDistanceMeters;
            backupFollowDistanceMeters = VPBConfig.Instance.FollowDistanceMeters;

            pendingFollowEyeHeight = VPBConfig.Instance.FollowEyeHeight;
            backupFollowEyeHeight = VPBConfig.Instance.FollowEyeHeight;

            pendingReorientStartAngle = VPBConfig.Instance.ReorientStartAngle;
            backupReorientStartAngle = VPBConfig.Instance.ReorientStartAngle;

            pendingMovementThreshold = VPBConfig.Instance.MovementThreshold;
            backupMovementThreshold = VPBConfig.Instance.MovementThreshold;

            pendingEnableGalleryFade = VPBConfig.Instance.EnableGalleryFade;
            backupEnableGalleryFade = VPBConfig.Instance.EnableGalleryFade;

            pendingEnableGalleryTranslucency = VPBConfig.Instance.EnableGalleryTranslucency;
            backupEnableGalleryTranslucency = VPBConfig.Instance.EnableGalleryTranslucency;

            pendingGalleryOpacity = VPBConfig.Instance.GalleryOpacity;
            backupGalleryOpacity = VPBConfig.Instance.GalleryOpacity;

            pendingDragDropReplaceMode = VPBConfig.Instance.DragDropReplaceMode;
            backupDragDropReplaceMode = VPBConfig.Instance.DragDropReplaceMode;

            RectTransform rt = settingsPaneRT;
            if (onRight)
            {
                rt.anchorMin = new Vector2(1, 0.5f);
                rt.anchorMax = new Vector2(1, 0.5f);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(130, 0); 
            }
            else
            {
                rt.anchorMin = new Vector2(0, 0.5f);
                rt.anchorMax = new Vector2(0, 0.5f);
                rt.pivot = new Vector2(1, 0.5f);
                rt.anchoredPosition = new Vector2(-130, 0); 
            }

            RefreshUI();
        }

        public void Close()
        {
            isSettingsOpen = false;
            if (settingsPaneGO != null) settingsPaneGO.SetActive(false);
            if (tooltipGO != null) tooltipGO.SetActive(false);
            
            // Revert live changes from memory backup
            VPBConfig.Instance.EnableButtonGaps = backupEnableButtonGaps;
            VPBConfig.Instance.ShowSideButtons = backupShowSideButtons;
            VPBConfig.Instance.FollowAngle = backupFollowAngle;
            VPBConfig.Instance._followDistance = backupFollowDistance;
            VPBConfig.Instance.FollowDistanceMeters = backupFollowDistanceMeters;
            VPBConfig.Instance.FollowEyeHeight = backupFollowEyeHeight;
            VPBConfig.Instance.ReorientStartAngle = backupReorientStartAngle;
            VPBConfig.Instance.MovementThreshold = backupMovementThreshold;
            VPBConfig.Instance.EnableGalleryFade = backupEnableGalleryFade;
            VPBConfig.Instance.EnableGalleryTranslucency = backupEnableGalleryTranslucency;
            VPBConfig.Instance.GalleryOpacity = backupGalleryOpacity;
            VPBConfig.Instance.DragDropReplaceMode = backupDragDropReplaceMode;
            VPBConfig.Instance.TriggerChange();
        }

        private void CreatePane()
        {
            settingsPaneGO = UI.AddChildGOImage(backgroundBoxGO, new Color(0.15f, 0.15f, 0.15f, 0.95f), AnchorPresets.middleRight, 500, 750, new Vector2(130, 0));
            settingsPaneRT = settingsPaneGO.GetComponent<RectTransform>();
            
            // Header
            GameObject header = new GameObject("SettingsHeader");
            header.transform.SetParent(settingsPaneGO.transform, false);
            Text t = header.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = "Settings";
            t.fontSize = 28;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform hRT = header.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 1);
            hRT.anchorMax = new Vector2(1, 1);
            hRT.pivot = new Vector2(0.5f, 1);
            hRT.sizeDelta = new Vector2(0, 60);
            hRT.anchoredPosition = new Vector2(0, -10);

            // Scrollable Area
            GameObject scrollable = UI.CreateVScrollableContent(settingsPaneGO, new Color(0, 0, 0, 0.2f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            RectTransform sRT = scrollable.GetComponent<RectTransform>();
            sRT.offsetMin = new Vector2(10, 80); 
            sRT.offsetMax = new Vector2(-10, -70); 
            
            settingsScrollContent = scrollable.GetComponent<ScrollRect>().content.gameObject;
            VerticalLayoutGroup vlg = settingsScrollContent.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 10;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = settingsScrollContent.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Footer Buttons
            float footerY = 10;
            float btnW = 180;
            float btnH = 50;
            
            GameObject cancelBtn = UI.CreateUIButton(settingsPaneGO, btnW, btnH, "Cancel", 24, -120, footerY, AnchorPresets.bottomMiddle, Close);
            cancelBtn.GetComponent<Image>().color = new Color(0.6f, 0.25f, 0.25f, 1f);
            cancelBtn.GetComponentInChildren<Text>().color = Color.white;
            
            GameObject saveBtn = UI.CreateUIButton(settingsPaneGO, btnW, btnH, "Save", 24, 120, footerY, AnchorPresets.bottomMiddle, () => {
                VPBConfig.Instance.EnableButtonGaps = pendingEnableButtonGaps;
                VPBConfig.Instance.ShowSideButtons = pendingShowSideButtons;
                VPBConfig.Instance.FollowAngle = pendingFollowAngle;
                VPBConfig.Instance._followDistance = pendingFollowDistance;
                VPBConfig.Instance.FollowDistanceMeters = pendingFollowDistanceMeters;
                VPBConfig.Instance.FollowEyeHeight = pendingFollowEyeHeight;
                VPBConfig.Instance.ReorientStartAngle = pendingReorientStartAngle;
                VPBConfig.Instance.MovementThreshold = pendingMovementThreshold;
                VPBConfig.Instance.EnableGalleryFade = pendingEnableGalleryFade;
                VPBConfig.Instance.EnableGalleryTranslucency = pendingEnableGalleryTranslucency;
                VPBConfig.Instance.GalleryOpacity = pendingGalleryOpacity;
                VPBConfig.Instance.DragDropReplaceMode = pendingDragDropReplaceMode;
                VPBConfig.Instance.Save();
                
                isSettingsOpen = false;
                if (settingsPaneGO != null) settingsPaneGO.SetActive(false);
                if (tooltipGO != null) tooltipGO.SetActive(false);
            });
            saveBtn.GetComponent<Image>().color = new Color(0.25f, 0.6f, 0.25f, 1f);
            saveBtn.GetComponentInChildren<Text>().color = Color.white;
            saveBtn.AddComponent<UIHoverBorder>();

            // Close button (X) in top right
            GameObject xBtn = UI.CreateUIButton(settingsPaneGO, 40, 40, "X", 24, 0, 0, AnchorPresets.topRight, Close);
            xBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 0.8f);
            xBtn.GetComponentInChildren<Text>().color = Color.white;
            xBtn.AddComponent<UIHoverBorder>();

            // Tooltip (Initially hidden)
            tooltipGO = UI.AddChildGOImage(settingsPaneGO, new Color(0, 0, 0, 0.9f), AnchorPresets.bottomMiddle, 480, 100, new Vector2(0, -60));
            tooltipGO.SetActive(false);
            GameObject tTextGO = new GameObject("TooltipText");
            tTextGO.transform.SetParent(tooltipGO.transform, false);
            tooltipText = tTextGO.AddComponent<Text>();
            tooltipText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tooltipText.fontSize = 20;
            tooltipText.color = Color.white;
            tooltipText.alignment = TextAnchor.MiddleCenter;
            RectTransform ttRT = tooltipText.GetComponent<RectTransform>();
            ttRT.anchorMin = Vector2.zero; ttRT.anchorMax = Vector2.one;
            ttRT.sizeDelta = new Vector2(-20, -20);
        }

        private void RefreshUI()
        {
            foreach (Transform child in settingsScrollContent.transform) GameObject.Destroy(child.gameObject);

            // CATEGORY: Visuals
            CreateHeader("Visuals");

            // Enable Gallery Fade
            CreateToggleSetting("Side Button Fade", pendingEnableGalleryFade, (val) => {
                pendingEnableGalleryFade = val;
                VPBConfig.Instance.EnableGalleryFade = val;
                VPBConfig.Instance.TriggerChange();
            }, "Fades out side buttons when not hovering over them.");

            // Gallery Translucency
            CreateToggleSetting("Gallery Translucency", pendingEnableGalleryTranslucency, (val) => {
                pendingEnableGalleryTranslucency = val;
                VPBConfig.Instance.EnableGalleryTranslucency = val;
                VPBConfig.Instance.TriggerChange();
            }, "Makes the entire gallery pane translucent.");

            CreateSliderSetting("Gallery Opacity", pendingGalleryOpacity, 0.1f, 1.0f, (val) => {
                pendingGalleryOpacity = val;
                VPBConfig.Instance.GalleryOpacity = val;
                VPBConfig.Instance.TriggerChange();
            }, "The opacity of the gallery pane when translucency is enabled. 0.1 = 10% visible, 1.0 = Opaque.");

            // Side Button Gaps
            CreateToggleSetting("Side Button Gaps", pendingEnableButtonGaps, (val) => {
                pendingEnableButtonGaps = val;
                VPBConfig.Instance.EnableButtonGaps = val;
                VPBConfig.Instance.TriggerChange(); 
            }, "Adds small gaps between groups of side buttons for better visual separation.");
            
            // Show Side Buttons
            string[] sideButtonOptions = { "Both", "Left", "Right" };
            string[] sideButtonLabels = { "Both Sides", "Left Side", "Right Side" };
            CreateCycleSetting("Show Side Buttons", pendingShowSideButtons, sideButtonOptions, sideButtonLabels, (val) => {
                pendingShowSideButtons = val;
                VPBConfig.Instance.ShowSideButtons = val;
                VPBConfig.Instance.TriggerChange();
            }, "Choose which sides of the gallery show the action buttons.");

            // CATEGORY: Interaction
            //CreateHeader("Interaction");

            // Drag Drop Replace Mode
            /*
            CreateToggleSetting("Drag & Drop Replace", pendingDragDropReplaceMode, (val) => {
                pendingDragDropReplaceMode = val;
                VPBConfig.Instance.DragDropReplaceMode = val;
                VPBConfig.Instance.TriggerChange();
            }, "When ON, dragging an item onto another replaces it. When OFF, it adds to it.");
            */

            // CATEGORY: Follow Mode
            CreateHeader("Follow Mode");

            // Follow Angle
            CreateToggleSetting("Follow Angle", pendingFollowAngle, (val) => {
                pendingFollowAngle = val;
                VPBConfig.Instance.FollowAngle = val;
                VPBConfig.Instance.TriggerChange();
            }, "When ON, the panel will rotate to face the user.");

            // Follow Eye Height
            CreateToggleSetting("Follow Eye Height", pendingFollowEyeHeight, (val) => {
                pendingFollowEyeHeight = val;
                VPBConfig.Instance.FollowEyeHeight = val;
                VPBConfig.Instance.TriggerChange();
            }, "When ON, the panel will move vertically to stay at your eye level.");

            // Follow Distance (ON/OFF)
            CreateToggleSetting("Follow Distance", pendingFollowDistance, (val) => {
                pendingFollowDistance = val;
                VPBConfig.Instance.FollowDistance = val;
                VPBConfig.Instance.TriggerChange();
            }, "When ON, the panel will maintain the specified horizontal distance from the user.");

            // Follow Distance (meters)
            CreateSliderSetting("Distance (m)", pendingFollowDistanceMeters, 1.0f, 5.0f, (val) => {
                pendingFollowDistanceMeters = val;
                VPBConfig.Instance.FollowDistanceMeters = val;
                VPBConfig.Instance.TriggerChange();
            }, "The target horizontal distance to maintain when Follow Distance is enabled.");

            // Reorient Start Angle
            CreateSliderSetting("Reorient Angle", pendingReorientStartAngle, 5f, 90f, (val) => {
                pendingReorientStartAngle = val;
                VPBConfig.Instance.ReorientStartAngle = val;
                VPBConfig.Instance.TriggerChange();
            }, "The angle difference required before the panel starts rotating to face you. Higher values reduce frequent rotations.");

            // Movement Threshold
            CreateSliderSetting("Move Threshold", pendingMovementThreshold, 0.01f, 1.0f, (val) => {
                pendingMovementThreshold = val;
                VPBConfig.Instance.MovementThreshold = val;
                VPBConfig.Instance.TriggerChange();
            }, "The distance you must move before the panel updates its position. Higher values provide more stable 'discrete' updates.");
        }

        private void CreateHeader(string title)
        {
            GameObject container = new GameObject("Header_" + title);
            container.transform.SetParent(settingsScrollContent.transform, false);
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 40; le.preferredHeight = 40;
            le.flexibleWidth = 1;

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(container.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = title.ToUpper();
            t.fontSize = 20;
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(0.7f, 0.7f, 0.7f);
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform rt = textGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10, 0);
            rt.offsetMax = new Vector2(-10, 0);

            // Add a small underline
            GameObject line = new GameObject("Underline");
            line.transform.SetParent(container.transform, false);
            Image img = line.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f);
            RectTransform lrt = line.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(1, 0);
            lrt.sizeDelta = new Vector2(-20, 2);
            lrt.anchoredPosition = new Vector2(0, 2);
        }

        private void AddTooltipIcon(GameObject container, string tooltip)
        {
            GameObject iconGO = new GameObject("TooltipIcon");
            iconGO.transform.SetParent(container.transform, false);
            Image img = iconGO.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            RectTransform rt = iconGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(5, -20);
            rt.sizeDelta = new Vector2(30, 30);

            GameObject textGO = new GameObject("i");
            textGO.transform.SetParent(iconGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = "i";
            t.fontSize = 20;
            t.fontStyle = FontStyle.Italic;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform tRT = textGO.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.sizeDelta = Vector2.zero;

            UIHoverDelegate hd = iconGO.AddComponent<UIHoverDelegate>();
            hd.OnHoverChange = (isHovering) => {
                if (isHovering)
                {
                    if (tooltipText != null) tooltipText.text = tooltip;
                    if (tooltipGO != null) tooltipGO.SetActive(true);
                }
                else
                {
                    if (tooltipGO != null) tooltipGO.SetActive(false);
                }
            };
        }

        private void CreateSliderSetting(string label, float currentVal, float min, float max, Action<float> onChange, string tooltip)
        {
            GameObject container = new GameObject("Setting_" + label);
            container.transform.SetParent(settingsScrollContent.transform, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 100; le.preferredHeight = 100;
            le.flexibleWidth = 1;

            AddTooltipIcon(container, tooltip);

            // Row 1: Label and Numeric Entry
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = label; t.fontSize = 22; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0.5f);
            labelRT.anchorMax = new Vector2(0.6f, 1f);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(40, 0);
            labelRT.sizeDelta = Vector2.zero;

            // Numeric Entry (InputField)
            GameObject inputGO = new GameObject("NumericInput");
            inputGO.transform.SetParent(container.transform, false);
            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            inputBg.raycastTarget = true;
            InputField inputField = inputGO.AddComponent<InputField>();
            inputField.targetGraphic = inputBg;
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.anchorMin = new Vector2(0.7f, 0.6f);
            inputRT.anchorMax = new Vector2(0.95f, 0.9f);
            inputRT.sizeDelta = Vector2.zero;
            inputGO.AddComponent<UIHoverBorder>();

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(inputGO.transform, false);
            Text inputText = textGO.AddComponent<Text>();
            inputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            inputText.fontSize = 20; inputText.color = Color.white;
            inputText.alignment = TextAnchor.MiddleCenter;
            inputText.raycastTarget = false;
            RectTransform inputTextRT = textGO.GetComponent<RectTransform>();
            inputTextRT.anchorMin = Vector2.zero; inputTextRT.anchorMax = Vector2.one;
            inputTextRT.sizeDelta = Vector2.zero;
            inputField.textComponent = inputText;
            inputField.text = currentVal.ToString("F1");
            inputField.contentType = InputField.ContentType.DecimalNumber;

            // Row 2: Slider
            GameObject sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(container.transform, false);
            Slider slider = sliderGO.AddComponent<Slider>();
            sliderGO.AddComponent<UIHoverBorder>();
            RectTransform sliderRT = sliderGO.GetComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0.05f, 0.1f);
            sliderRT.anchorMax = new Vector2(0.95f, 0.4f);
            sliderRT.sizeDelta = Vector2.zero;

            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderGO.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.2f);
            bgImg.raycastTarget = true;
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.4f);
            bgRT.anchorMax = new Vector2(1, 0.6f);
            bgRT.sizeDelta = Vector2.zero;

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGO.transform, false);
            RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.4f);
            fillAreaRT.anchorMax = new Vector2(1, 0.6f);
            fillAreaRT.sizeDelta = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.25f, 0.5f, 0.8f);
            fillImg.raycastTarget = false;
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.sizeDelta = Vector2.zero;
            slider.fillRect = fillRT;

            // Handle Area
            GameObject handleArea = new GameObject("Handle Area");
            handleArea.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRT = handleArea.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = new Vector2(0, 0);
            handleAreaRT.anchorMax = new Vector2(1, 1);
            handleAreaRT.sizeDelta = Vector2.zero;

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            handleImg.raycastTarget = true;
            RectTransform handleRT = handle.GetComponent<RectTransform>();
            handleRT.anchorMin = new Vector2(0, 0);
            handleRT.anchorMax = new Vector2(0, 1);
            handleRT.sizeDelta = new Vector2(30, 0);
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;

            slider.minValue = min;
            slider.maxValue = max;
            slider.value = currentVal;

            UIScrollWheelHandler swh = inputGO.AddComponent<UIScrollWheelHandler>();
            swh.Sensitivity = 1.0f;
            swh.OnScrollValue = (delta) => {
                float step = 0.1f * Mathf.Sign(delta);
                float newVal = Mathf.Clamp(slider.value + step, min, max);
                slider.value = newVal;
                inputField.text = newVal.ToString("F1");
                onChange(newVal);
            };

            // Synchronization
            slider.onValueChanged.AddListener((val) => {
                inputField.text = val.ToString("F1");
                // We don't call onChange here to avoid loops during drag
            });

            // Add EventTrigger for PointerUp to trigger onChange only on release
            EventTrigger trigger = sliderGO.AddComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerUp;
            entry.callback.AddListener((data) => {
                onChange(slider.value);
            });
            trigger.triggers.Add(entry);

            inputField.onEndEdit.AddListener((val) => {
                if (float.TryParse(val, out float res))
                {
                    res = Mathf.Clamp(res, min, max);
                    slider.value = res;
                    inputField.text = res.ToString("F1");
                    onChange(res);
                }
                else
                {
                    inputField.text = slider.value.ToString("F1");
                }
            });
        }

        private void CreateCycleSetting(string label, string currentVal, string[] options, string[] labels, Action<string> onCycle, string tooltip)
        {
            GameObject container = new GameObject("Setting_" + label);
            container.transform.SetParent(settingsScrollContent.transform, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 60; le.preferredHeight = 60;
            le.flexibleWidth = 1;

            AddTooltipIcon(container, tooltip);

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = label; t.fontSize = 22; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0.4f, 1);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(40, 0);
            labelRT.sizeDelta = Vector2.zero;

            float btnW = 150;
            float btnH = 45;
            float btnX = 300;

            GameObject cycleBtn = UI.CreateUIButton(container, btnW, btnH, labels[Array.IndexOf(options, currentVal)], 18, btnX, 0, AnchorPresets.middleLeft, null);
            cycleBtn.AddComponent<UIHoverBorder>();
            Text cycleTxt = cycleBtn.GetComponentInChildren<Text>();
            cycleTxt.color = Color.white;
            cycleBtn.GetComponent<Image>().color = new Color(0.25f, 0.5f, 0.8f, 1f);

            cycleBtn.GetComponent<Button>().onClick.AddListener(() => {
                int index = Array.IndexOf(options, currentVal);
                index = (index + 1) % options.Length;
                currentVal = options[index];
                cycleTxt.text = labels[index];
                onCycle(currentVal);
            });
        }

        private void CreateToggleSetting(string label, bool currentVal, Action<bool> onToggle, string tooltip)
        {
            GameObject container = new GameObject("Setting_" + label);
            container.transform.SetParent(settingsScrollContent.transform, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 60; le.preferredHeight = 60;
            le.flexibleWidth = 1;

            AddTooltipIcon(container, tooltip);

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = label; t.fontSize = 22; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0.4f, 1);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(40, 0);
            labelRT.sizeDelta = Vector2.zero;

            float btnW = 70;
            float btnH = 45;
            float btnX = 300; 

            GameObject offBtn = UI.CreateUIButton(container, btnW, btnH, "OFF", 18, btnX, 0, AnchorPresets.middleLeft, null);
            GameObject onBtn = UI.CreateUIButton(container, btnW, btnH, "ON", 18, btnX + btnW + 5, 0, AnchorPresets.middleLeft, null);
            
            offBtn.AddComponent<UIHoverBorder>();
            onBtn.AddComponent<UIHoverBorder>();
            
            Image offImg = offBtn.GetComponent<Image>();
            Image onImg = onBtn.GetComponent<Image>();
            Text offTxt = offBtn.GetComponentInChildren<Text>();
            Text onTxt = onBtn.GetComponentInChildren<Text>();

            Action updateColors = () => {
                offImg.color = currentVal ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.7f, 0.3f, 0.3f);
                offTxt.color = currentVal ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
                onImg.color = currentVal ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.25f, 0.25f, 0.25f);
                onTxt.color = currentVal ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            };
            updateColors();

            offBtn.GetComponent<Button>().onClick.AddListener(() => {
                if (currentVal) {
                    currentVal = false;
                    updateColors();
                    onToggle(false);
                }
            });

            onBtn.GetComponent<Button>().onClick.AddListener(() => {
                if (!currentVal) {
                    currentVal = true;
                    updateColors();
                    onToggle(true);
                }
            });
        }
    }
}
