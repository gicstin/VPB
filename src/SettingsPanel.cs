using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public class SettingsPanel
    {
        public GameObject settingsPaneGO;
        private GalleryPanel parentPanel;
        private GameObject backgroundBoxGO;
        private RectTransform settingsPaneRT;
        private GameObject settingsScrollContent;

        private const float SettingsPaneDockOffsetX = 180f;
        
        private bool isSettingsOpen = false;
        private bool settingsOnRight = true;
        
        // Pending settings state
        private bool pendingEnableButtonGaps;
        private bool backupEnableButtonGaps;
        
        private string pendingShowSideButtons;
        private string backupShowSideButtons;

        private string pendingFollowAngle;
        private string backupFollowAngle;

        private string pendingFollowDistance;
        private string backupFollowDistance;

        private string pendingFollowEyeHeight;
        private string backupFollowEyeHeight;

        private float pendingReorientStartAngle;
        private float backupReorientStartAngle;

        private float pendingMovementThreshold;
        private float backupMovementThreshold;

        private float pendingBringToFrontDistance;
        private float backupBringToFrontDistance;

        private bool pendingEnableCurvature;
        private bool backupEnableCurvature;

        private float pendingCurvatureIntensity;
        private float backupCurvatureIntensity;

        private bool pendingEnableGalleryFade;
        private bool backupEnableGalleryFade;

        private bool pendingEnableGalleryTranslucency;
        private bool backupEnableGalleryTranslucency;

        private bool pendingGalleryManualRefreshOnly;
        private bool backupGalleryManualRefreshOnly;

        private float pendingGalleryOpacity;
        private float backupGalleryOpacity;

        private float pendingSideButtonScale;
        private float backupSideButtonScale;

        private float pendingInnerPaneScale;
        private float backupInnerPaneScale;

        private bool pendingDragDropReplaceMode;
        private bool backupDragDropReplaceMode;

        private string pendingAppearanceClothingApplyMode;
        private string backupAppearanceClothingApplyMode;

        private bool pendingEnableDragDrop;
        private bool backupEnableDragDrop;

        private float pendingDragHoldThreshold;
        private float backupDragHoldThreshold;
        
        private bool pendingIsDevMode;
        private bool backupIsDevMode;

        private bool pendingEnableAutoFixedGallery;
        private bool backupEnableAutoFixedGallery;

        private HashSet<string> pendingHiddenCategories;
        private HashSet<string> backupHiddenCategories;

        private GameObject tooltipGO;
        private Text tooltipText;
        private Text settingsTitleText;
        private Text settingsCancelBtnText;
        private Text settingsSaveBtnText;

        public SettingsPanel(GalleryPanel parentPanel, GameObject backgroundBoxGO)
        {
            this.parentPanel = parentPanel;
            this.backgroundBoxGO = backgroundBoxGO;
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
            UpdateCurvatureLayout();

            // Force a curvature refresh to ensure the newly active settings pane gets subdivided and colliders updated
            VPBConfig.Instance.TriggerChange();
            
            // Initialize pending settings from current config
            pendingEnableButtonGaps = VPBConfig.Instance.EnableButtonGaps;
            backupEnableButtonGaps = VPBConfig.Instance.EnableButtonGaps;
            
            pendingShowSideButtons = VPBConfig.Instance.ShowSideButtons;
            backupShowSideButtons = VPBConfig.Instance.ShowSideButtons;

            pendingFollowAngle = VPBConfig.Instance.FollowAngle;
            backupFollowAngle = VPBConfig.Instance.FollowAngle;

            pendingFollowDistance = VPBConfig.Instance._followDistance;
            backupFollowDistance = VPBConfig.Instance._followDistance;

            pendingFollowEyeHeight = VPBConfig.Instance._followEyeHeight;
            backupFollowEyeHeight = VPBConfig.Instance._followEyeHeight;

            pendingReorientStartAngle = VPBConfig.Instance.ReorientStartAngle;
            backupReorientStartAngle = VPBConfig.Instance.ReorientStartAngle;

            pendingMovementThreshold = VPBConfig.Instance.MovementThreshold;
            backupMovementThreshold = VPBConfig.Instance.MovementThreshold;

            pendingBringToFrontDistance = VPBConfig.Instance.BringToFrontDistance;
            backupBringToFrontDistance = VPBConfig.Instance.BringToFrontDistance;

            pendingEnableCurvature = VPBConfig.Instance.EnableCurvature;
            backupEnableCurvature = VPBConfig.Instance.EnableCurvature;

            pendingCurvatureIntensity = VPBConfig.Instance.CurvatureIntensity;
            backupCurvatureIntensity = VPBConfig.Instance.CurvatureIntensity;

            pendingEnableGalleryFade = VPBConfig.Instance.EnableGalleryFade;
            backupEnableGalleryFade = VPBConfig.Instance.EnableGalleryFade;

            pendingEnableGalleryTranslucency = VPBConfig.Instance.EnableGalleryTranslucency;
            backupEnableGalleryTranslucency = VPBConfig.Instance.EnableGalleryTranslucency;

            pendingGalleryManualRefreshOnly = VPBConfig.Instance.GalleryManualRefreshOnly;
            backupGalleryManualRefreshOnly = VPBConfig.Instance.GalleryManualRefreshOnly;

            pendingGalleryOpacity = VPBConfig.Instance.GalleryOpacity;
            backupGalleryOpacity = VPBConfig.Instance.GalleryOpacity;

            pendingSideButtonScale = VPBConfig.Instance.SideButtonScale;
            backupSideButtonScale = VPBConfig.Instance.SideButtonScale;

            pendingInnerPaneScale = VPBConfig.Instance.InnerPaneScale;
            backupInnerPaneScale = VPBConfig.Instance.InnerPaneScale;

            pendingDragDropReplaceMode = VPBConfig.Instance.DragDropReplaceMode;
            backupDragDropReplaceMode = VPBConfig.Instance.DragDropReplaceMode;

            pendingAppearanceClothingApplyMode = NormalizeSettingsAppearanceClothingMode(VPBConfig.Instance.AppearanceClothingApplyMode);
            backupAppearanceClothingApplyMode = pendingAppearanceClothingApplyMode;

            pendingEnableDragDrop = VPBConfig.Instance.EnableDragDrop;
            backupEnableDragDrop = VPBConfig.Instance.EnableDragDrop;

            pendingDragHoldThreshold = VPBConfig.Instance.DragHoldThreshold;
            backupDragHoldThreshold = VPBConfig.Instance.DragHoldThreshold;

            pendingIsDevMode = VPBConfig.Instance.IsDevMode;
            backupIsDevMode = VPBConfig.Instance.IsDevMode;

            pendingEnableAutoFixedGallery = VPBConfig.Instance.EnableAutoFixedGallery;
            backupEnableAutoFixedGallery = VPBConfig.Instance.EnableAutoFixedGallery;

            pendingHiddenCategories = new HashSet<string>(VPBConfig.Instance.HiddenCategories ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            backupHiddenCategories  = new HashSet<string>(pendingHiddenCategories, StringComparer.OrdinalIgnoreCase);

            RectTransform rt = settingsPaneRT;
            if (onRight)
            {
                rt.anchorMin = new Vector2(1, 0.5f);
                rt.anchorMax = new Vector2(1, 0.5f);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(SettingsPaneDockOffsetX, 0); 
            }
            else
            {
                rt.anchorMin = new Vector2(0, 0.5f);
                rt.anchorMax = new Vector2(0, 0.5f);
                rt.pivot = new Vector2(1, 0.5f);
                rt.anchoredPosition = new Vector2(-SettingsPaneDockOffsetX, 0); 
            }

            RefreshUI();
            ApplySettingsFonts();
        }

        public void RefreshLocalizedUi()
        {
            if (settingsScrollContent == null) return;
            if (settingsTitleText != null) settingsTitleText.text = VPBTranslation.T("settings.title", "Settings");
            if (settingsCancelBtnText != null) settingsCancelBtnText.text = VPBTranslation.T("settings.cancel", "Cancel");
            if (settingsSaveBtnText != null) settingsSaveBtnText.text = VPBTranslation.T("settings.save", "Save");
            RefreshUI();
            ApplySettingsFonts();
        }

        private void ApplySettingsFonts()
        {
            if (settingsPaneGO == null) return;
            foreach (Text tx in settingsPaneGO.GetComponentsInChildren<Text>(true))
                VPBUiFont.ApplyTo(tx);
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
            VPBConfig.Instance.FollowEyeHeight = backupFollowEyeHeight;
            VPBConfig.Instance.ReorientStartAngle = backupReorientStartAngle;
            VPBConfig.Instance.MovementThreshold = backupMovementThreshold;
            VPBConfig.Instance.BringToFrontDistance = backupBringToFrontDistance;
            VPBConfig.Instance.EnableCurvature = backupEnableCurvature;
            VPBConfig.Instance.CurvatureIntensity = backupCurvatureIntensity;
            VPBConfig.Instance.EnableGalleryFade = backupEnableGalleryFade;
            VPBConfig.Instance.EnableGalleryTranslucency = backupEnableGalleryTranslucency;
            VPBConfig.Instance.GalleryManualRefreshOnly = backupGalleryManualRefreshOnly;
            VPBConfig.Instance.GalleryOpacity = backupGalleryOpacity;
            VPBConfig.Instance.SideButtonScale = backupSideButtonScale;
            if (parentPanel != null) parentPanel.ApplySideButtonScale();
            VPBConfig.Instance.InnerPaneScale = backupInnerPaneScale;
            if (parentPanel != null) parentPanel.ApplyInnerPaneScale();
            VPBConfig.Instance.DragDropReplaceMode = backupDragDropReplaceMode;
            VPBConfig.Instance.AppearanceClothingApplyMode = backupAppearanceClothingApplyMode;
            VPBConfig.Instance.EnableDragDrop = backupEnableDragDrop;
            VPBConfig.Instance.DragHoldThreshold = backupDragHoldThreshold;
            VPBConfig.Instance.IsDevMode = backupIsDevMode;
            VPBConfig.Instance.EnableAutoFixedGallery = backupEnableAutoFixedGallery;
            VPBConfig.Instance.HiddenCategories = new HashSet<string>(backupHiddenCategories ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            pendingHiddenCategories = new HashSet<string>(backupHiddenCategories ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            VPBConfig.Instance.TriggerChange();
            if (parentPanel != null) parentPanel.RefreshAppearanceClothingSideButton();
        }

        private void CreatePane()
        {
            settingsPaneGO = UI.AddChildGOImage(backgroundBoxGO, new Color(0.15f, 0.15f, 0.15f, 0.95f), AnchorPresets.middleRight, 500, 750, new Vector2(SettingsPaneDockOffsetX, 0));
            settingsPaneRT = settingsPaneGO.GetComponent<RectTransform>();
            
            // Header
            GameObject header = new GameObject("SettingsHeader");
            header.transform.SetParent(settingsPaneGO.transform, false);
            Text t = header.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            settingsTitleText = t;
            t.text = VPBTranslation.T("settings.title", "Settings");
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
            
            GameObject cancelBtn = UI.CreateUIButton(settingsPaneGO, btnW, btnH, VPBTranslation.T("settings.cancel", "Cancel"), 24, -120, footerY, AnchorPresets.bottomMiddle, Close);
            cancelBtn.GetComponent<Image>().color = new Color(0.6f, 0.25f, 0.25f, 1f);
            settingsCancelBtnText = cancelBtn.GetComponentInChildren<Text>();
            settingsCancelBtnText.color = Color.white;
            
            GameObject saveBtn = UI.CreateUIButton(settingsPaneGO, btnW, btnH, VPBTranslation.T("settings.save", "Save"), 24, 120, footerY, AnchorPresets.bottomMiddle, () => {
                VPBConfig.Instance.EnableButtonGaps = pendingEnableButtonGaps;
                VPBConfig.Instance.ShowSideButtons = pendingShowSideButtons;
                VPBConfig.Instance.FollowAngle = pendingFollowAngle;
                VPBConfig.Instance._followDistance = pendingFollowDistance;
                VPBConfig.Instance._followEyeHeight = pendingFollowEyeHeight;
                VPBConfig.Instance.ReorientStartAngle = pendingReorientStartAngle;
                VPBConfig.Instance.MovementThreshold = pendingMovementThreshold;
                VPBConfig.Instance.BringToFrontDistance = pendingBringToFrontDistance;
                VPBConfig.Instance.EnableCurvature = pendingEnableCurvature;
                VPBConfig.Instance.CurvatureIntensity = pendingCurvatureIntensity;
                VPBConfig.Instance.EnableGalleryFade = pendingEnableGalleryFade;
                VPBConfig.Instance.EnableGalleryTranslucency = pendingEnableGalleryTranslucency;
                VPBConfig.Instance.GalleryManualRefreshOnly = pendingGalleryManualRefreshOnly;
                VPBConfig.Instance.GalleryOpacity = pendingGalleryOpacity;
                VPBConfig.Instance.SideButtonScale = pendingSideButtonScale;
                VPBConfig.Instance.InnerPaneScale = pendingInnerPaneScale;
                VPBConfig.Instance.DragDropReplaceMode = pendingDragDropReplaceMode;
                VPBConfig.Instance.AppearanceClothingApplyMode = pendingAppearanceClothingApplyMode;
                VPBConfig.Instance.EnableDragDrop = pendingEnableDragDrop;
                VPBConfig.Instance.DragHoldThreshold = pendingDragHoldThreshold;
                VPBConfig.Instance.IsDevMode = pendingIsDevMode;
                VPBConfig.Instance.EnableAutoFixedGallery = pendingEnableAutoFixedGallery;
                VPBConfig.Instance.Save();
                if (parentPanel != null) parentPanel.RefreshAppearanceClothingSideButton();
                
                isSettingsOpen = false;
                if (settingsPaneGO != null) settingsPaneGO.SetActive(false);
                if (tooltipGO != null) tooltipGO.SetActive(false);
            });
            saveBtn.GetComponent<Image>().color = new Color(0.25f, 0.6f, 0.25f, 1f);
            settingsSaveBtnText = saveBtn.GetComponentInChildren<Text>();
            settingsSaveBtnText.color = Color.white;
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
            CreateHeader(VPBTranslation.T("settings.header.visuals", "Visuals"));

            // Enable Gallery Fade
            CreateToggleSetting(VPBTranslation.T("settings.side_button_fade", "Side Button Fade"), pendingEnableGalleryFade, (val) => {
                pendingEnableGalleryFade = val;
                VPBConfig.Instance.EnableGalleryFade = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.side_button_fade", "Fades out side buttons when not hovering over them."));

            // Gallery Translucency
            CreateToggleSetting(VPBTranslation.T("settings.gallery_translucency", "Gallery Translucency"), pendingEnableGalleryTranslucency, (val) => {
                pendingEnableGalleryTranslucency = val;
                VPBConfig.Instance.EnableGalleryTranslucency = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.gallery_translucency", "Makes the entire gallery pane translucent."));

            CreateToggleSetting(VPBTranslation.T("settings.gallery_manual_refresh_only", "Manual gallery refresh only"), pendingGalleryManualRefreshOnly, (val) => {
                pendingGalleryManualRefreshOnly = val;
                VPBConfig.Instance.GalleryManualRefreshOnly = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.gallery_manual_refresh_only", "When enabled, package scans do not update the file grid until you press Refresh in the gallery. Reduces scroll jumps and load when the package index changes often."));

            CreateSliderSetting(VPBTranslation.T("settings.gallery_opacity", "Gallery Opacity"), pendingGalleryOpacity, 0.1f, 1.0f, (val) => {
                pendingGalleryOpacity = val;
                VPBConfig.Instance.GalleryOpacity = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.gallery_opacity", "The opacity of the gallery pane when translucency is enabled. 0.1 = 10% visible, 1.0 = Opaque."));

            CreateSliderSetting(VPBTranslation.T("settings.side_button_scale", "Side Button Scale"), pendingSideButtonScale, 0.5f, 2.0f, (val) => {
                pendingSideButtonScale = val;
                VPBConfig.Instance.SideButtonScale = val;
                if (parentPanel != null) parentPanel.ApplySideButtonScale();
            }, VPBTranslation.T("settings.tip.side_button_scale", "Scales the size of the side buttons. 1.0 = default size."));

            CreateSliderSetting(VPBTranslation.T("settings.inner_pane_scale", "Inner Pane Scale"), pendingInnerPaneScale, 0.5f, 2.0f, (val) => {
                pendingInnerPaneScale = val;
                VPBConfig.Instance.InnerPaneScale = val;
                if (parentPanel != null) parentPanel.ApplyInnerPaneScale();
            }, VPBTranslation.T("settings.tip.inner_pane_scale", "Scales all UI elements inside the gallery pane. 1.0 = default size."));

            // Side Button Gaps
            CreateToggleSetting(VPBTranslation.T("settings.side_button_gaps", "Side Button Gaps"), pendingEnableButtonGaps, (val) => {
                pendingEnableButtonGaps = val;
                VPBConfig.Instance.EnableButtonGaps = val;
                VPBConfig.Instance.TriggerChange(); 
            }, VPBTranslation.T("settings.tip.side_button_gaps", "Adds small gaps between groups of side buttons for better visual separation."));
            
            bool isFixed = parentPanel != null && parentPanel.isFixedLocally;

            if (!isFixed)
            {
                // Show Side Buttons
                string[] sideButtonOptions = { "Both", "Left", "Right" };
                string[] sideButtonLabels = {
                    VPBTranslation.T("settings.side_both", "Both Sides"),
                    VPBTranslation.T("settings.side_left", "Left Side"),
                    VPBTranslation.T("settings.side_right", "Right Side")
                };
                CreateCycleSetting(VPBTranslation.T("settings.show_side_buttons", "Show Side Buttons"), pendingShowSideButtons, sideButtonOptions, sideButtonLabels, (val) => {
                    pendingShowSideButtons = val;
                    VPBConfig.Instance.ShowSideButtons = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.show_side_buttons", "Choose which sides of the gallery show the action buttons."));
            }

            // CATEGORY: Interaction
            //CreateHeader("Interaction");

            if (!isFixed)
            {
                // CATEGORY: Follow Mode
                CreateHeader(VPBTranslation.T("settings.header.follow_mode", "Follow Mode"));

                string[] followOptions = { "Off", "Desktop", "VR", "Both" };
                string[] followLabels = {
                    VPBTranslation.T("settings.follow.off", "Off"),
                    VPBTranslation.T("settings.follow.desktop", "Desktop"),
                    VPBTranslation.T("settings.follow.vr", "VR"),
                    VPBTranslation.T("settings.follow.both", "Both")
                };

                // Follow Angle
                CreateCycleSetting(VPBTranslation.T("settings.follow_angle", "Follow Angle"), pendingFollowAngle, followOptions, followLabels, (val) => {
                    pendingFollowAngle = val;
                    VPBConfig.Instance.FollowAngle = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.follow_angle", "When enabled, the panel will rotate to face the user. 'Both' = both VR and Desktop."));

                // Follow Eye Height
                CreateCycleSetting(VPBTranslation.T("settings.follow_eye_height", "Follow Eye Height"), pendingFollowEyeHeight, followOptions, followLabels, (val) => {
                    pendingFollowEyeHeight = val;
                    VPBConfig.Instance.FollowEyeHeight = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.follow_eye_height", "When enabled, the panel will stay at eye level. 'Both' = both VR and Desktop."));

                // Follow Distance (ON/OFF)
                CreateCycleSetting(VPBTranslation.T("settings.follow_distance", "Follow Distance"), pendingFollowDistance, followOptions, followLabels, (val) => {
                    pendingFollowDistance = val;
                    VPBConfig.Instance.FollowDistance = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.follow_distance", "When enabled, the panel will maintain its distance from the user. 'Both' = both VR and Desktop."));

                // Reorient Start Angle
                CreateSliderSetting(VPBTranslation.T("settings.reorient_angle", "Reorient Angle"), pendingReorientStartAngle, 5f, 90f, (val) => {
                    pendingReorientStartAngle = val;
                    VPBConfig.Instance.ReorientStartAngle = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.reorient_angle", "The angle difference required before the panel starts rotating to face you. Higher values reduce frequent rotations."));

                // Movement Threshold
                CreateSliderSetting(VPBTranslation.T("settings.move_threshold", "Move Threshold"), pendingMovementThreshold, 0.01f, 1.0f, (val) => {
                    pendingMovementThreshold = val;
                    VPBConfig.Instance.MovementThreshold = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.move_threshold", "The distance you must move before the panel updates its position. Higher values provide more stable 'discrete' updates."));

                // Bring to Front Distance
                CreateSliderSetting(VPBTranslation.T("settings.bring_front_dist", "Bring Front Dist"), pendingBringToFrontDistance, 0.5f, 2.5f, (val) => {
                    pendingBringToFrontDistance = val;
                    VPBConfig.Instance.BringToFrontDistance = val;
                }, VPBTranslation.T("settings.tip.bring_front_dist", "The distance (in meters) from your view where panels will appear when using 'Bring to Front'."));
            }

            // CATEGORY: Interaction
            CreateHeader(VPBTranslation.T("settings.header.interaction", "Interaction"));
            CreateToggleSetting(VPBTranslation.T("settings.enable_drag_drop", "Enable Drag & Drop"), pendingEnableDragDrop, (val) => {
                pendingEnableDragDrop = val;
                VPBConfig.Instance.EnableDragDrop = val;
            }, VPBTranslation.T("settings.tip.enable_drag_drop", "When disabled, gallery items can only be applied via click — no drag & drop. Disables the context popup that appears on drag."));

            CreateSliderSetting(VPBTranslation.T("settings.drag_hold_threshold", "Drag Hold Threshold (s)"), pendingDragHoldThreshold, 0.1f, 1.0f, (val) => {
                pendingDragHoldThreshold = val;
                VPBConfig.Instance.DragHoldThreshold = val;
            }, VPBTranslation.T("settings.tip.drag_hold_threshold", "How long (in seconds) the mouse button must be held before a drag is initiated. Increase to reduce accidental drags on quick clicks."));

            string[] appearanceClothingOptions = { "replace", "keep", "clothingonly" };
            string[] appearanceClothingLabels = {
                VPBTranslation.T("settings.appearance.preset_outfit", "Preset outfit"),
                VPBTranslation.T("settings.appearance.keep_body", "Keep body clothes"),
                VPBTranslation.T("settings.appearance.clothes_only", "Clothes only")
            };
            CreateCycleSetting(VPBTranslation.T("settings.appearance_clothing", "Appearance clothing"), pendingAppearanceClothingApplyMode, appearanceClothingOptions, appearanceClothingLabels, (val) => {
                pendingAppearanceClothingApplyMode = val;
                VPBConfig.Instance.AppearanceClothingApplyMode = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) parentPanel.RefreshAppearanceClothingSideButton();
            }, VPBTranslation.T("settings.tip.appearance_clothing", "Preset outfit: full appearance. Keep body clothes: face/body/hair from preset, keep your garments. Clothes only: keep current person; apply only garment clothing from the preset (not hair or makeup-type items)."));

            // CATEGORY: Desktop
            CreateHeader(VPBTranslation.T("settings.header.desktop", "Desktop"));
            CreateToggleSetting(VPBTranslation.T("settings.startup_fixed_gallery", "Startup Gallery (Fixed)"), pendingEnableAutoFixedGallery, (val) => {
                pendingEnableAutoFixedGallery = val;
                VPBConfig.Instance.EnableAutoFixedGallery = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.startup_fixed_gallery", "When enabled, a pinned (Fixed) gallery pane with Autohide enabled will be automatically created on the right side of the screen when the plugin starts."));

            if (isFixed)
            {
                // Bring to Front Distance (for Fixed Mode too, as requested)
                CreateSliderSetting(VPBTranslation.T("settings.bring_front_dist", "Bring Front Dist"), pendingBringToFrontDistance, 0.5f, 2.5f, (val) => {
                    pendingBringToFrontDistance = val;
                    VPBConfig.Instance.BringToFrontDistance = val;
                }, VPBTranslation.T("settings.tip.bring_front_dist", "The distance (in meters) from your view where panels will appear when using 'Bring to Front'."));
            }

            // CATEGORY: Gallery Categories
            CreateHeader(VPBTranslation.T("settings.header.gallery_categories", "Gallery Categories"));
            var knownHideable = new[] {
                "Person", "Person BreastPhysics", "Person General",
                "Person GlutePhysics", "Person Morphs", "Person Textures", "SubScenes"
            };
            foreach (var catName in knownHideable)
            {
                string cn = catName;
                bool isHidden = pendingHiddenCategories != null && pendingHiddenCategories.Contains(cn);
                CreateToggleSetting(VPBTranslation.T("settings.hide_category." + cn.Replace(" ", "_").ToLowerInvariant(), "Hide: " + cn), isHidden, (val) => {
                    if (val) pendingHiddenCategories.Add(cn);
                    else     pendingHiddenCategories.Remove(cn);
                    VPBConfig.Instance.HiddenCategories = new HashSet<string>(pendingHiddenCategories, StringComparer.OrdinalIgnoreCase);
                    VPBConfig.Instance.TriggerChange();
                    if (parentPanel != null) parentPanel.UpdateTabs();
                }, VPBTranslation.T("settings.tip.hide_category", "Hide this category from the Categories tab list. The category is still accessible via search."));
            }

            if (VPBConfig.Instance.IsDevMode)
            {
                CreateHeader(VPBTranslation.T("settings.header.developer", "Developer"));
                CreateToggleSetting(VPBTranslation.T("settings.developer_mode", "Developer Mode"), pendingIsDevMode, (val) => {
                    pendingIsDevMode = val;
                }, VPBTranslation.T("settings.tip.developer_mode", "Enables developer-only features and debug tools. Requires restart to fully hide/show some elements."));
            }
        }

        private static string NormalizeSettingsAppearanceClothingMode(string m)
        {
            if (string.IsNullOrEmpty(m)) return "replace";
            string t = m.Trim().ToLowerInvariant();
            if (t == "keep") return "keep";
            if (t == "clothingonly") return "clothingonly";
            return "replace";
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
            t.color = Color.white;
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

            GameObject offBtn = UI.CreateUIButton(container, btnW, btnH, VPBTranslation.T("settings.toggle.off", "OFF"), 18, btnX, 0, AnchorPresets.middleLeft, null);
            GameObject onBtn = UI.CreateUIButton(container, btnW, btnH, VPBTranslation.T("settings.toggle.on", "ON"), 18, btnX + btnW + 5, 0, AnchorPresets.middleLeft, null);
            
            offBtn.AddComponent<UIHoverBorder>();
            onBtn.AddComponent<UIHoverBorder>();
            
            Image offImg = offBtn.GetComponent<Image>();
            Image onImg = onBtn.GetComponent<Image>();
            Text offTxt = offBtn.GetComponentInChildren<Text>();
            Text onTxt = onBtn.GetComponentInChildren<Text>();

            Action updateColors = () => {
                offImg.color = currentVal ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.6f, 0.2f, 0.2f);
                offTxt.color = Color.white;
                onImg.color = currentVal ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.2f, 0.2f, 0.2f);
                onTxt.color = Color.white;
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

        public void UpdateCurvatureLayout()
        {
            if (settingsPaneGO == null || backgroundBoxGO == null || settingsPaneRT == null) return;
            
            Transform canvasT = backgroundBoxGO.transform.parent;
            if (canvasT == null) return;
            RectTransform canvasRT = canvasT.GetComponent<RectTransform>();
            if (canvasRT == null) return;
            
            bool enabled = VPBConfig.Instance != null && VPBConfig.Instance.EnableCurvature;
            float intensity = VPBConfig.Instance != null ? VPBConfig.Instance.CurvatureIntensity : 1.0f;
            
            if (!enabled)
            {
                // Reset to standard side-docked position
                settingsPaneRT.anchorMin = new Vector2(settingsOnRight ? 1 : 0, 0.5f);
                settingsPaneRT.anchorMax = new Vector2(settingsOnRight ? 1 : 0, 0.5f);
                settingsPaneRT.anchoredPosition3D = new Vector3(settingsOnRight ? SettingsPaneDockOffsetX : -SettingsPaneDockOffsetX, 0, 0);
                settingsPaneRT.localRotation = Quaternion.identity;
                return;
            }

            // Set anchors to center to make 3D positioning absolute relative to parent center
            settingsPaneRT.anchorMin = new Vector2(0.5f, 0.5f);
            settingsPaneRT.anchorMax = new Vector2(0.5f, 0.5f);

            // Calculate radius and scale same as the vertex modifier
            float radius = 2.0f * (1.0f / intensity);
            if (radius < 0.1f) radius = 0.1f;
            float scaleX = canvasRT.lossyScale.x;
            if (scaleX == 0) scaleX = 0.001f;

            // Find where the main panel ends (Background is 1200 wide, so edge is at 600)
            float edgeX = 600f; 
            float worldEdgeX = edgeX * scaleX;
            float angleRad = worldEdgeX / radius;
            
            // Calculate edge position in curved space (relative to canvas center)
            float curvedX = Mathf.Sin(angleRad) * radius / scaleX;
            float curvedZ = (Mathf.Cos(angleRad) - 1.0f) * radius / scaleX;
            
            // If it's on the left, flip X and Angle
            float sideSign = settingsOnRight ? 1f : -1f;
            float finalAngleRad = angleRad * sideSign;
            float finalCurvedX = curvedX * sideSign;
            
            // Rotate the panel to be angled toward user
            // We use the same angle as the curve's end to make it a tangent "wing"
            settingsPaneRT.localRotation = Quaternion.Euler(0, finalAngleRad * Mathf.Rad2Deg, 0);
            
            // The settings pane pivot is middle (0.5, 0.5). 
            float halfWidth = settingsPaneRT.rect.width * 0.5f;
            // Overlap slightly with the main panel (as it was originally)
            float overlap = 20f; 
            
            // Direction of the wing (perpendicular to the radius at the edge)
            // We want the direction to point AWAY from the center on both sides.
            float dirX = Mathf.Cos(finalAngleRad) * sideSign;
            float dirZ = -Mathf.Sin(finalAngleRad) * sideSign;
            
            float centerX = finalCurvedX + (halfWidth - overlap) * dirX;
            float centerZ = curvedZ + (halfWidth - overlap) * dirZ;
            
            // Apply a small Z-bias toward the user to ensure it stays in front of the main panel where they overlap
            centerZ += 5f; 
            
            // Position relative to backgroundBoxGO center
            settingsPaneRT.anchoredPosition3D = new Vector3(centerX, 0, centerZ);
        }
    }
}
