using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private enum InternalSettingControlType
        {
            Toggle,
            Slider,
            Cycle,
            TextArea,
            Button,
            ColorRgb,
        }

        private sealed class InternalSettingDefinition
        {
            public string Key;
            public string GroupKey;
            public string Label;
            public string Tooltip;
            public InternalSettingControlType ControlType;

            public Func<bool> GetBool;
            public Action<bool> SetBool;

            public Func<float> GetFloat;
            public Action<float> SetFloat;
            public float Min;
            public float Max;
            public float Step;
            public int Decimals;
            public bool AllowNegative;

            public string[] Options;
            public Func<string> GetString;
            public Action<string> SetString;

            /// <summary>When non-null and returns false, row omitted from settings list (e.g. slider hidden until parent toggle on).</summary>
            public Func<bool> RowVisible;

            /// <summary>Fired when a Button-type row is clicked (primary or secondary click).</summary>
            public Action OnAction;

            public Func<Color> GetColor;
            public Action<Color> SetColor;
        }

        private sealed class InternalSettingRowEntry : VirtualFileEntry
        {
            public string RowKey;
            public string GroupKey;

            public InternalSettingRowEntry(string rowKey, string groupKey, string label)
                : base("[SETTING] " + rowKey)
            {
                RowKey = rowKey ?? "";
                GroupKey = groupKey ?? "all";
                Uid = "[SETTING]:" + RowKey;
                Name = label ?? RowKey;
                Path = Uid;
            }
        }

        private sealed class InternalSettingsSnapshot
        {
            public bool EnableGalleryFade;
            public bool EnableGalleryTranslucency;
            public bool GalleryManualRefreshOnly;
            public float GalleryOpacity;
            public float SideButtonScaleVR;
            public float SideButtonScaleDesktop;
            public float InnerPaneScaleVR;
            public float InnerPaneScaleDesktop;
            public bool EnableButtonGaps;
            public string ShowSideButtons;
            public string FollowAngle;
            public string FollowEyeHeight;
            public string FollowDistance;
            public float ReorientStartAngle;
            public float MovementThreshold;
            public float BringToFrontDistance;
            public bool EnableDragDrop;
            public bool GalleryAutoGenderFilter;
            public bool RequireDragHoldBeforeMove;
            public float DragHoldThreshold;
            public float HoldToLaunchHoldSeconds;
            public string AppearanceClothingApplyMode;
            public bool EnableAutoFixedGallery;
            public string InitialGalleryCategory;
            public string GalleryDefaultLeftSidePanel;
            public string GalleryDefaultRightSidePanel;
            public bool GalleryHideCreatorSideButtons;
            public bool PluginGalleryGridThumbnails;
            public bool GalleryListNamesLegacyFileName;
            public string GalleryHoverPreviewMode;
            public float GalleryListHoverPreviewSize;
            public float GalleryListHoverPreviewOffsetX;
            public float GalleryListHoverPreviewOffsetY;
            public bool GalleryGridLabelsEnabled;
            public bool GalleryGridLabelsAutoHideAtHighDensity;
            public float GalleryGridLabelFontSize;
            public float GalleryGridSpacingX;
            public float GalleryGridSpacingY;
            public float GalleryGridThumbnailPadding;
            public float GalleryGridHoverBorderWidth;
            public float GalleryGridSelectedBorderWidth;
            public bool GalleryGridBorderInwardWhenSquare;
            public float GalleryGridBorderColorR;
            public float GalleryGridBorderColorG;
            public float GalleryGridBorderColorB;
            public float GalleryGridBorderColorA;
            public bool GalleryOnlyWhenVamMenuVisible;
            public bool GalleryAnchorToVamMenu;
            public string GalleryCategoryQuickOrder;
            public string GalleryCategoryQuickSwitchHidden;
            public HashSet<string> HiddenCategories;
        }

        private static string NextOf(string cur, string[] options)
        {
            if (options == null || options.Length == 0) return cur ?? "";
            int idx = -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i], cur ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) idx = 0;
            return options[(idx + 1) % options.Length];
        }

        private static string PrevOf(string cur, string[] options)
        {
            if (options == null || options.Length == 0) return cur ?? "";
            int idx = -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i], cur ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) idx = 0;
            return options[(idx + options.Length - 1) % options.Length];
        }

        private List<string> BuildCategoryVisibilityNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (categories != null)
                {
                    for (int i = 0; i < categories.Count; i++)
                    {
                        var c = categories[i];
                        if (string.IsNullOrEmpty(c.name)) continue;
                        names.Add(c.name);
                    }
                }
            }
            catch { }

            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.HiddenCategories != null)
                {
                    foreach (string hidden in VPBConfig.Instance.HiddenCategories)
                    {
                        if (string.IsNullOrEmpty(hidden)) continue;
                        names.Add(hidden);
                    }
                }
            }
            catch { }

            var list = new List<string>(names);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private List<InternalSettingDefinition> BuildInternalSettingDefinitions()
        {
            bool FollowAngleActive()
            {
                try { return VPBConfig.Instance != null && !string.Equals(VPBConfig.Instance.FollowAngle, "Off", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
            bool FollowPositionTrackingActive()
            {
                try
                {
                    if (VPBConfig.Instance == null) return false;
                    return !string.Equals(VPBConfig.Instance.FollowEyeHeight, "Off", StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(VPBConfig.Instance.FollowDistance, "Off", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }

            var defs = new List<InternalSettingDefinition>(64);
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.fade", GroupKey = "visuals", Label = VPBTranslation.T("settings.side_button_fade", "Side Button Fade"),
                Tooltip = VPBTranslation.T("settings.tip.side_button_fade", "Fades out side buttons when not hovering over them."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableGalleryFade,
                SetBool = v => { VPBConfig.Instance.EnableGalleryFade = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.translucency", GroupKey = "visuals", Label = VPBTranslation.T("settings.gallery_translucency", "Gallery Translucency"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_translucency", "Makes the entire gallery pane translucent."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableGalleryTranslucency,
                SetBool = v => { VPBConfig.Instance.EnableGalleryTranslucency = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.manualRefresh", GroupKey = "visuals", Label = VPBTranslation.T("settings.gallery_manual_refresh_only", "Manual gallery refresh only"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_manual_refresh_only", "When enabled, package scans do not update the file grid until you press Refresh in the gallery. Reduces scroll jumps and load when the package index changes often."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryManualRefreshOnly,
                SetBool = v => { VPBConfig.Instance.GalleryManualRefreshOnly = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.opacity", GroupKey = "visuals", Label = VPBTranslation.T("settings.gallery_opacity", "Gallery Opacity"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_opacity", "The opacity of the gallery pane when translucency is enabled. 0.1 = 10% visible, 1.0 = Opaque."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryOpacity,
                SetFloat = v => { VPBConfig.Instance.GalleryOpacity = v; VPBConfig.Instance.TriggerChange(); },
                Min = 0.1f, Max = 1.0f, Step = 0.1f, Decimals = 1,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.EnableGalleryTranslucency
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.sideScaleVr", GroupKey = "visuals", Label = VPBTranslation.T("settings.side_button_scale_vr", "Side Button Scale (VR)"),
                Tooltip = VPBTranslation.T("settings.tip.side_button_scale_vr", "Scales the size of the side buttons in VR mode. 1.0 = default size."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.SideButtonScaleVR,
                SetFloat = v => { VPBConfig.Instance.SideButtonScaleVR = v; ApplySideButtonScale(); },
                Min = 0.5f, Max = 2.0f, Step = 0.1f, Decimals = 1,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.IsVR
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.sideScaleDesktop", GroupKey = "visuals", Label = VPBTranslation.T("settings.side_button_scale_desktop", "Side Button Scale (Desktop)"),
                Tooltip = VPBTranslation.T("settings.tip.side_button_scale_desktop", "Scales the size of the side buttons in Desktop mode. 1.0 = default size."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.SideButtonScaleDesktop,
                SetFloat = v => { VPBConfig.Instance.SideButtonScaleDesktop = v; ApplySideButtonScale(); },
                Min = 0.5f, Max = 2.0f, Step = 0.1f, Decimals = 1,
                RowVisible = () => VPBConfig.Instance != null && !VPBConfig.Instance.IsVR
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.innerScaleVr", GroupKey = "visuals", Label = VPBTranslation.T("settings.inner_pane_scale_vr", "Inner Pane Scale (VR)"),
                Tooltip = VPBTranslation.T("settings.tip.inner_pane_scale_vr", "Scales all UI elements inside the gallery pane in VR mode. 1.0 = default size."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.InnerPaneScaleVR,
                SetFloat = v => { VPBConfig.Instance.InnerPaneScaleVR = Mathf.Clamp(v, VPBConfig.MinUiScale, VPBConfig.MaxUiScale); VPBConfig.Instance.TriggerChange(); },
                Min = VPBConfig.MinUiScale, Max = VPBConfig.MaxUiScale, Step = 0.1f, Decimals = 1,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.IsVR
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.innerScaleDesktop", GroupKey = "visuals", Label = VPBTranslation.T("settings.inner_pane_scale_desktop", "Inner Pane Scale (Desktop)"),
                Tooltip = VPBTranslation.T("settings.tip.inner_pane_scale_desktop", "Scales all UI elements inside the gallery pane in Desktop mode. 1.0 = default size."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.InnerPaneScaleDesktop,
                SetFloat = v => { VPBConfig.Instance.InnerPaneScaleDesktop = Mathf.Clamp(v, VPBConfig.MinUiScale, VPBConfig.MaxUiScale); VPBConfig.Instance.TriggerChange(); },
                Min = VPBConfig.MinUiScale, Max = VPBConfig.MaxUiScale, Step = 0.1f, Decimals = 1,
                RowVisible = () => VPBConfig.Instance != null && !VPBConfig.Instance.IsVR
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.sideGaps", GroupKey = "visuals", Label = VPBTranslation.T("settings.side_button_gaps", "Side Button Gaps"),
                Tooltip = VPBTranslation.T("settings.tip.side_button_gaps", "Adds small gaps between groups of side buttons for better visual separation."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableButtonGaps,
                SetBool = v => { VPBConfig.Instance.EnableButtonGaps = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.showSideButtons", GroupKey = "visuals", Label = VPBTranslation.T("settings.show_side_buttons", "Show Side Buttons"),
                Tooltip = VPBTranslation.T("settings.tip.show_side_buttons", "Choose which sides of the gallery show the action buttons."),
                ControlType = InternalSettingControlType.Cycle, Options = new [] { "Both", "Left", "Right" },
                GetString = () => VPBConfig.Instance.ShowSideButtons,
                SetString = v => { VPBConfig.Instance.ShowSideButtons = v; VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "follow.angle", GroupKey = "follow", Label = VPBTranslation.T("settings.follow_angle", "Follow Angle"),
                Tooltip = VPBTranslation.T("settings.tip.follow_angle", "When enabled, the panel will rotate to face the user. 'Both' = both VR and Desktop."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Off", "Desktop", "VR", "Both" },
                GetString = () => VPBConfig.Instance.FollowAngle, SetString = v => { VPBConfig.Instance.FollowAngle = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "follow.eyeHeight", GroupKey = "follow", Label = VPBTranslation.T("settings.follow_eye_height", "Follow Eye Height"),
                Tooltip = VPBTranslation.T("settings.tip.follow_eye_height", "When enabled, the panel will stay at eye level. 'Both' = both VR and Desktop."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Off", "Desktop", "VR", "Both" },
                GetString = () => VPBConfig.Instance.FollowEyeHeight, SetString = v => { VPBConfig.Instance.FollowEyeHeight = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "follow.distance", GroupKey = "follow", Label = VPBTranslation.T("settings.follow_distance", "Follow Distance"),
                Tooltip = VPBTranslation.T("settings.tip.follow_distance", "When enabled, the panel will maintain its distance from the user. 'Both' = both VR and Desktop."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Off", "Desktop", "VR", "Both" },
                GetString = () => VPBConfig.Instance.FollowDistance, SetString = v => { VPBConfig.Instance.FollowDistance = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "follow.reorient", GroupKey = "follow", Label = VPBTranslation.T("settings.reorient_angle", "Reorient Angle"),
                Tooltip = VPBTranslation.T("settings.tip.reorient_angle", "The angle difference required before the panel starts rotating to face you. Higher values reduce frequent rotations."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.ReorientStartAngle,
                SetFloat = v => { VPBConfig.Instance.ReorientStartAngle = v; VPBConfig.Instance.TriggerChange(); },
                Min = 5f, Max = 90f, Step = 1f, Decimals = 1,
                RowVisible = FollowAngleActive
            });
            defs.Add(new InternalSettingDefinition {
                Key = "follow.moveThreshold", GroupKey = "follow", Label = VPBTranslation.T("settings.move_threshold", "Move Threshold"),
                Tooltip = VPBTranslation.T("settings.tip.move_threshold", "The distance you must move before the panel updates its position. Higher values provide more stable discrete updates."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.MovementThreshold,
                SetFloat = v => { VPBConfig.Instance.MovementThreshold = v; VPBConfig.Instance.TriggerChange(); },
                Min = 0.01f, Max = 1f, Step = 0.01f, Decimals = 2,
                RowVisible = FollowPositionTrackingActive
            });
            defs.Add(new InternalSettingDefinition {
                Key = "follow.bringFront", GroupKey = "follow", Label = VPBTranslation.T("settings.bring_front_dist", "Bring Front Dist"),
                Tooltip = VPBTranslation.T("settings.tip.bring_front_dist", "The distance (in meters) from your view where panels will appear when using Bring to Front."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.BringToFrontDistance,
                SetFloat = v => { VPBConfig.Instance.BringToFrontDistance = v; },
                Min = 0.5f, Max = 2.5f, Step = 0.1f, Decimals = 1
            });

            defs.Add(new InternalSettingDefinition {
                Key = "interaction.dragDrop", GroupKey = "interaction", Label = VPBTranslation.T("settings.enable_drag_drop", "Enable Drag & Drop"),
                Tooltip = VPBTranslation.T("settings.tip.enable_drag_drop", "Off by default. Turn on to drag items from the gallery onto atoms or the scene."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableDragDrop,
                SetBool = v =>
                {
                    VPBConfig.Instance.EnableDragDrop = v;
                    VPBConfig.Instance.NormalizeDragDropHoldSettings();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "interaction.autoGenderFilter", GroupKey = "interaction", Label = VPBTranslation.T("settings.gallery_auto_gender_filter", "Auto gender filter (Hair/Clothing)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_auto_gender_filter", "When ON, Hair/Clothing categories auto-filter Male/Female items to match selected target atom gender."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryAutoGenderFilter,
                SetBool = v => { VPBConfig.Instance.GalleryAutoGenderFilter = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "interaction.dragHoldSec", GroupKey = "interaction", Label = VPBTranslation.T("settings.drag_hold_threshold", "Hold duration (s)"),
                Tooltip = VPBTranslation.T("settings.tip.drag_hold_threshold", "When drag-and-drop is on: how long pointer must stay held before drag starts (minimum " + VPBConfig.DragHoldThresholdMin.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s)."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.DragHoldThreshold,
                SetFloat = v => { VPBConfig.Instance.DragHoldThreshold = VPBConfig.ClampDragHoldThreshold(v); },
                Min = VPBConfig.DragHoldThresholdMin, Max = 1f, Step = 0.1f, Decimals = 1,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.EnableDragDrop
            });
            defs.Add(new InternalSettingDefinition {
                Key = "interaction.holdToLaunchSec", GroupKey = "interaction", Label = VPBTranslation.T("settings.hold_to_launch_seconds", "Hold-to-launch time (s)"),
                Tooltip = VPBTranslation.T("settings.tip.hold_to_launch_seconds", "When hold-to-launch is on: seconds trigger/button must stay pressed on item."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.HoldToLaunchHoldSeconds,
                SetFloat = v => { VPBConfig.Instance.HoldToLaunchHoldSeconds = Mathf.Clamp(v, 0.2f, 1f); },
                Min = 0.2f, Max = 1f, Step = 0.05f, Decimals = 2,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.HoldToLaunchEnabled
            });
            defs.Add(new InternalSettingDefinition {
                Key = "interaction.appearanceClothing", GroupKey = "interaction", Label = VPBTranslation.T("settings.appearance_clothing", "Appearance clothing"),
                Tooltip = VPBTranslation.T("settings.tip.appearance_clothing", "Preset outfit, keep body clothes, or clothes-only apply mode."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "replace", "keep", "clothingonly" },
                GetString = () => VPBConfig.Instance.AppearanceClothingApplyMode,
                SetString = v => { VPBConfig.Instance.AppearanceClothingApplyMode = v; RefreshAppearanceClothingSideButton(); VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "desktop.startFixed", GroupKey = "desktop", Label = VPBTranslation.T("settings.startup_fixed_gallery", "Startup Gallery (Fixed)"),
                Tooltip = VPBTranslation.T("settings.tip.startup_fixed_gallery", "Automatically create a pinned fixed gallery pane at startup."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableAutoFixedGallery,
                SetBool = v => { VPBConfig.Instance.EnableAutoFixedGallery = v; VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "desktop.fixedAutoHideSeconds", GroupKey = "desktop", Label = VPBTranslation.T("settings.desktop.fixed_auto_hide_seconds", "Fixed auto-hide delay (s)"),
                Tooltip = VPBTranslation.T("settings.tip.desktop.fixed_auto_hide_seconds", "Seconds cursor must be outside pane before auto-hide collapses (Desktop fixed mode)."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.DesktopFixedAutoHideSeconds,
                SetFloat = v => {
                    VPBConfig.Instance.DesktopFixedAutoHideSeconds = Mathf.Clamp(v, 0.1f, 10f);
                    try { VPBConfig.Instance.Save(false, true); } catch { }
                    VPBConfig.Instance.TriggerChange();
                },
                Min = 0.1f, Max = 10f, Step = 0.1f, Decimals = 1,
                RowVisible = () => VPBConfig.Instance != null && !VPBConfig.Instance.IsVR
            });

            defs.Add(new InternalSettingDefinition {
                Key = "desktop.fixedDefaultDock", GroupKey = "desktop", Label = VPBTranslation.T("settings.desktop.fixed_default_dock", "Fixed dock default"),
                Tooltip = VPBTranslation.T("settings.tip.desktop.fixed_default_dock", "Default dock side when switching to fixed mode."),
                ControlType = InternalSettingControlType.Cycle, Options = new [] { "Left", "Right", "Top" },
                GetString = () => VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDefaultDockSide),
                SetString = v => { VPBConfig.Instance.DesktopFixedDefaultDockSide = VPBConfig.NormalizeDesktopFixedDockSide(v); VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "desktop.fixedEnforceDockEnabled", GroupKey = "desktop", Label = VPBTranslation.T("settings.desktop.fixed_enforce_dock", "Always enforce fixed dock side"),
                Tooltip = VPBTranslation.T("settings.tip.desktop.fixed_enforce_dock", "When enabled, dock side ignores which anchor button you click."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.DesktopFixedEnforceDockSide,
                SetBool = v => { VPBConfig.Instance.DesktopFixedEnforceDockSide = v; VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "desktop.fixedEnforceDockSide", GroupKey = "desktop", Label = VPBTranslation.T("settings.desktop.fixed_enforce_dock_side", "Enforced fixed dock side"),
                Tooltip = VPBTranslation.T("settings.tip.desktop.fixed_enforce_dock_side", "Dock side used while enforcement is enabled."),
                ControlType = InternalSettingControlType.Cycle, Options = new [] { "Left", "Right", "Top" },
                GetString = () => VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedEnforcedDockSide),
                SetString = v => { VPBConfig.Instance.DesktopFixedEnforcedDockSide = VPBConfig.NormalizeDesktopFixedDockSide(v); VPBConfig.Instance.DesktopFixedDockSide = VPBConfig.Instance.DesktopFixedEnforcedDockSide; VPBConfig.Instance.TriggerChange(); },
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedEnforceDockSide
            });
            defs.Add(new InternalSettingDefinition {
                Key = "desktop.initialCategory", GroupKey = "desktop", Label = VPBTranslation.T("settings.initial_gallery_category", "Gallery opens on"),
                Tooltip = VPBTranslation.T("settings.tip.initial_gallery_category", "Which category is shown when gallery opens."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" },
                GetString = () => VPBConfig.NormalizeInitialGalleryCategory(VPBConfig.Instance.InitialGalleryCategory),
                SetString = v => { VPBConfig.Instance.InitialGalleryCategory = v; VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "lists.defaultLeft", GroupKey = "lists", Label = VPBTranslation.T("settings.gallery_default_left_panel", "Left side list (default)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_left_panel", "Which filter list opens on the left for new panes."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "None", "Category", "Creator" },
                GetString = () => VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.GalleryDefaultLeftSidePanel),
                SetString = v => {
                    VPBConfig.Instance.GalleryDefaultLeftSidePanel = v;
                    // Avoid clobbering the active Settings side tab while user is interacting with Settings UI.
                    if (!IsSettingsPanelOpen()) ApplySidePanelDefaultsFromConfig();
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.defaultRight", GroupKey = "lists", Label = VPBTranslation.T("settings.gallery_default_right_panel", "Right side list (default)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_right_panel", "Which filter list opens on the right for new panes."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "None", "Category", "Creator" },
                GetString = () => VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.GalleryDefaultRightSidePanel),
                SetString = v => {
                    VPBConfig.Instance.GalleryDefaultRightSidePanel = v;
                    // Avoid clobbering the active Settings side tab while user is interacting with Settings UI.
                    if (!IsSettingsPanelOpen()) ApplySidePanelDefaultsFromConfig();
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.hideCreatorSideButtons", GroupKey = "lists",
                Label = VPBTranslation.T("settings.gallery_hide_creator_side_buttons", "Hide creator side buttons"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_hide_creator_side_buttons", "Hides side-rail Creator buttons. Use title-bar creator control only. Closes open creator side lists."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryHideCreatorSideButtons,
                SetBool = v => {
                    VPBConfig.Instance.GalleryHideCreatorSideButtons = v;
                    try { VPBConfig.Instance.Save(false); } catch { }
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.pluginThumbs", GroupKey = "lists", Label = VPBTranslation.T("settings.plugin_gallery_grid_thumbnails", "Plugin thumbnails in grid"),
                Tooltip = VPBTranslation.T("settings.tip.plugin_gallery_grid_thumbnails", "Use sister-image thumbnails for plugin files in grid."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.PluginGalleryGridThumbnails,
                SetBool = v => {
                    VPBConfig.Instance.PluginGalleryGridThumbnails = v;
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    else RefreshFiles(true);
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.legacyNames", GroupKey = "lists", Label = VPBTranslation.T("settings.gallery_list_legacy_names", "Legacy gallery list names"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_list_legacy_names", "Use old file/item name mode in list rows."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryListNamesLegacyFileName,
                SetBool = v => {
                    VPBConfig.Instance.GalleryListNamesLegacyFileName = v;
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    else RefreshFiles(true);
                    VPBConfig.Instance.TriggerChange();
                }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "hover.mode", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_mode", "Hover preview"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_mode", "Show larger image preview while hovering items."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Off", "List", "Grid", "Both" },
                GetString = () => VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode),
                SetString = v => { VPBConfig.Instance.GalleryHoverPreviewMode = VPBConfig.NormalizeHoverPreviewMode(v); VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "hover.size", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_size", "Hover preview size"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_size", "Size in pixels of square hover preview."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryListHoverPreviewSize,
                SetFloat = v => { VPBConfig.Instance.GalleryListHoverPreviewSize = v; VPBConfig.Instance.TriggerChange(); },
                Min = 200f, Max = 600f, Step = 10f, Decimals = 0,
                RowVisible = () => VPBConfig.Instance != null && !string.Equals(VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode), "Off", StringComparison.OrdinalIgnoreCase)
            });
            defs.Add(new InternalSettingDefinition {
                Key = "hover.offsetX", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_offset_x", "Hover preview X offset"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_offset_x", "Move hover preview left/right."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryListHoverPreviewOffsetX,
                SetFloat = v => { VPBConfig.Instance.GalleryListHoverPreviewOffsetX = v; VPBConfig.Instance.TriggerChange(); },
                Min = -2000f, Max = 2000f, Step = 25f, Decimals = 0, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance != null && !string.Equals(VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode), "Off", StringComparison.OrdinalIgnoreCase)
            });
            defs.Add(new InternalSettingDefinition {
                Key = "hover.offsetY", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_offset_y", "Hover preview Y offset"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_offset_y", "Move hover preview up/down."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryListHoverPreviewOffsetY,
                SetFloat = v => { VPBConfig.Instance.GalleryListHoverPreviewOffsetY = v; VPBConfig.Instance.TriggerChange(); },
                Min = -2000f, Max = 2000f, Step = 25f, Decimals = 0, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance != null && !string.Equals(VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode), "Off", StringComparison.OrdinalIgnoreCase)
            });

            defs.Add(new InternalSettingDefinition {
                Key = "grid.enabled", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_labels_enabled", "Always-on grid labels"),
                Tooltip = VPBTranslation.T("settings.tip.grid_labels_enabled", "Show Creator.Package.Version labels under grid thumbnails."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryGridLabelsEnabled,
                SetBool = v => { VPBConfig.Instance.GalleryGridLabelsEnabled = v; RebuildGridLayout(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.autoHideHighDensity", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_labels_auto_hide_high_density", "Hide labels at max grid density"),
                Tooltip = VPBTranslation.T("settings.tip.grid_labels_auto_hide_high_density", "When grid is at 11 or 12 columns (minus pressed to limit), hide label strips."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryGridLabelsAutoHideAtHighDensity,
                SetBool = v => { VPBConfig.Instance.GalleryGridLabelsAutoHideAtHighDensity = v; RebuildGridLayout(); },
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.GalleryGridLabelsEnabled
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.font", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_label_font_size", "Label font size"),
                Tooltip = VPBTranslation.T("settings.tip.grid_label_font_size", "Grid label strip font size."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryGridLabelFontSize,
                SetFloat = v => { VPBConfig.Instance.GalleryGridLabelFontSize = v; RebuildGridLayout(); },
                Min = 8f, Max = 32f, Step = 1f, Decimals = 0,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.GalleryGridLabelsEnabled
            });

            defs.Add(new InternalSettingDefinition {
                Key = "grid.spacingX", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_spacing_x", "Grid spacing X"),
                Tooltip = VPBTranslation.T("settings.tip.grid_spacing_x", "Horizontal spacing between grid previews (pixels)."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryGridSpacingX,
                SetFloat = v => { VPBConfig.Instance.GalleryGridSpacingX = v; RebuildGridLayout(); },
                Min = 0f, Max = 40f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.spacingY", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_spacing_y", "Grid spacing Y"),
                Tooltip = VPBTranslation.T("settings.tip.grid_spacing_y", "Vertical spacing between grid previews (pixels)."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryGridSpacingY,
                SetFloat = v => { VPBConfig.Instance.GalleryGridSpacingY = v; RebuildGridLayout(); },
                Min = 0f, Max = 40f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.thumbPad", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_thumb_padding", "Thumbnail padding"),
                Tooltip = VPBTranslation.T("settings.tip.grid_thumb_padding", "Padding between cell edge and thumbnail (pixels). 0 = flush to edge."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryGridThumbnailPadding,
                SetFloat = v => { VPBConfig.Instance.GalleryGridThumbnailPadding = v; RebuildGridLayout(); try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { } },
                Min = 0f, Max = 12f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.hoverBorder", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_hover_border_width", "Hover border width"),
                Tooltip = VPBTranslation.T("settings.tip.grid_hover_border_width", "Hover border thickness for grid previews (pixels)."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryGridHoverBorderWidth,
                SetFloat = v => { VPBConfig.Instance.GalleryGridHoverBorderWidth = v; try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { } },
                Min = 0f, Max = 10f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.selBorder", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_selected_border_width", "Selected border width"),
                Tooltip = VPBTranslation.T("settings.tip.grid_selected_border_width", "Selected border thickness for grid previews (pixels)."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryGridSelectedBorderWidth,
                SetFloat = v => { VPBConfig.Instance.GalleryGridSelectedBorderWidth = v; try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { } },
                Min = 0f, Max = 14f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.inwardSquare", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_border_inward_square", "Inward border when padding = 0"),
                Tooltip = VPBTranslation.T("settings.tip.grid_border_inward_square", "When padding is 0 (square/flush), draw hover/selection border inward instead of outward."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryGridBorderInwardWhenSquare,
                SetBool = v => { VPBConfig.Instance.GalleryGridBorderInwardWhenSquare = v; try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { } }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.borderColor", GroupKey = "grid",
                Label = VPBTranslation.T("settings.grid_border_color", "Hover / selection border color"),
                Tooltip = VPBTranslation.T("settings.tip.grid_border_color", "Color for hover and selection borders in grid and list layout."),
                ControlType = InternalSettingControlType.ColorRgb,
                GetColor = () => VPBConfig.Instance.GetGalleryGridBorderColor(),
                SetColor = c =>
                {
                    VPBConfig.Instance.SetGalleryGridBorderColor(c);
                    try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
                }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "vr.menuGate", GroupKey = "vr", Label = VPBTranslation.T("settings.gallery.vam_menu_gate", "Show only when VaM menu is visible"),
                Tooltip = VPBTranslation.T("settings.tip.gallery.vam_menu_gate", "Hide gallery panes automatically when VaM menu is closed."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible,
                SetBool = v => { VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible = v; VPBConfig.Instance.TriggerChange(); },
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.IsVR
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.anchor", GroupKey = "vr", Label = VPBTranslation.T("settings.gallery.vam_menu_anchor", "Anchor to VaM Menu in VR"),
                Tooltip = VPBTranslation.T("settings.tip.gallery.vam_menu_anchor", "Anchor pane relative to VaM menu in VR."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryAnchorToVamMenu,
                SetBool = v => { VPBConfig.Instance.GalleryAnchorToVamMenu = v; VPBConfig.Instance.TriggerChange(); ResetFollowOffsets(); },
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.IsVR
            });

            defs.Add(new InternalSettingDefinition {
                Key = "quick.categoryEditor",
                GroupKey = "quick",
                Label = VPBTranslation.T("settings.category_quick.editor.title", "Edit header category dropdown"),
                Tooltip = VPBTranslation.T("settings.tip.category_quick.editor", "Edit header dropdown order + hidden list."),
                ControlType = InternalSettingControlType.TextArea,
                GetString = () => "",
                SetString = v => { }
            });

            var categoryVisibilityNames = BuildCategoryVisibilityNames();
            for (int i = 0; i < categoryVisibilityNames.Count; i++)
            {
                string categoryName = categoryVisibilityNames[i];
                string capturedName = categoryName;
                defs.Add(new InternalSettingDefinition
                {
                    Key = "categories.show." + capturedName,
                    GroupKey = "categories",
                    Label = VPBTranslation.T("settings.category_visibility.show", "Show category: ") + capturedName,
                    Tooltip = VPBTranslation.T("settings.tip.category_visibility.show", "Toggle whether this category appears in the Categories side list."),
                    ControlType = InternalSettingControlType.Toggle,
                    GetBool = () => VPBConfig.Instance != null && !VPBConfig.Instance.IsHiddenCategory(capturedName),
                    SetBool = v =>
                    {
                        if (VPBConfig.Instance == null) return;
                        if (VPBConfig.Instance.HiddenCategories == null)
                            VPBConfig.Instance.HiddenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (v) VPBConfig.Instance.HiddenCategories.Remove(capturedName);
                        else VPBConfig.Instance.HiddenCategories.Add(capturedName);
                        categoriesCached = false;
                        UpdateTabs();
                    }
                });
            }

            // BrowserAssist migration section (only shown when BA data dir exists)
            if (BaImporter.TryDetectBaDataDir(out _))
            {
                defs.Add(new InternalSettingDefinition
                {
                    Key = "ba.import",
                    GroupKey = "ba_migration",
                    Label = VPBTranslation.T("settings.ba.import", "Import tags from BrowserAssist"),
                    Tooltip = VPBTranslation.T("settings.tip.ba.import",
                        "Import user tags from BrowserAssist into VPB. Re-running first undoes any previous BA import, then re-imports fresh. Manually added tags are preserved."),
                    ControlType = InternalSettingControlType.Button,
                    OnAction = () =>
                    {
                        if (!BaImporter.TryDetectBaDataDir(out string baDir))
                        {
                            ShowTemporaryStatus(VPBTranslation.T("settings.ba.import.notfound",
                                "BrowserAssist data not found."), 3f);
                            return;
                        }
                        ShowTemporaryStatus(VPBTranslation.T("settings.ba.import.running", "Importing..."), 60f);
                        BaImporter.BaMigrationResult r;
                        BaImporter.RunImport(baDir, out r);
                        string msg = r.Success
                            ? string.Format(VPBTranslation.T("settings.ba.import.done",
                                "Imported {0} tag rows across {1} packages. {2} hide markers. {3} skipped."),
                                r.TagRowsImported, r.PackagesTagged, r.HideMarkersWritten, r.ItemsSkipped)
                            : VPBTranslation.T("settings.ba.import.failed", "Import failed — see log.");
                        ShowTemporaryStatus(msg, 5f);
                        RefreshInternalSettingsListRows(true);
                    }
                });

                if (BaImporter.MigrationManifestExists())
                {
                    defs.Add(new InternalSettingDefinition
                    {
                        Key = "ba.reset",
                        GroupKey = "ba_migration",
                        Label = VPBTranslation.T("settings.ba.reset", "[DEV] Reset BA migration"),
                        Tooltip = VPBTranslation.T("settings.tip.ba.reset",
                            "Removes only the tags and hide markers added by the last BA migration. Does not affect manually added tags."),
                        ControlType = InternalSettingControlType.Button,
                        OnAction = () =>
                        {
                            int tags, hides;
                            BaImporter.TryResetMigration(out tags, out hides);
                            ShowTemporaryStatus(string.Format(
                                VPBTranslation.T("settings.ba.reset.done", "Reset: {0} tag entries removed, {1} hide markers removed."),
                                tags, hides), 5f);
                            RefreshInternalSettingsListRows(true);
                        }
                    });
                }
            }

            return defs;
        }

        private InternalSettingDefinition GetInternalSettingDefinition(string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey)) return null;
            var defs = BuildInternalSettingDefinitions();
            for (int i = 0; i < defs.Count; i++)
            {
                if (string.Equals(defs[i].Key, rowKey, StringComparison.OrdinalIgnoreCase))
                    return defs[i];
            }
            return null;
        }

        private InternalSettingsSnapshot CreateInternalSettingsSnapshot()
        {
            return new InternalSettingsSnapshot
            {
                EnableGalleryFade = VPBConfig.Instance.EnableGalleryFade,
                EnableGalleryTranslucency = VPBConfig.Instance.EnableGalleryTranslucency,
                GalleryManualRefreshOnly = VPBConfig.Instance.GalleryManualRefreshOnly,
                GalleryOpacity = VPBConfig.Instance.GalleryOpacity,
                SideButtonScaleVR = VPBConfig.Instance.SideButtonScaleVR,
                SideButtonScaleDesktop = VPBConfig.Instance.SideButtonScaleDesktop,
                InnerPaneScaleVR = VPBConfig.Instance.InnerPaneScaleVR,
                InnerPaneScaleDesktop = VPBConfig.Instance.InnerPaneScaleDesktop,
                EnableButtonGaps = VPBConfig.Instance.EnableButtonGaps,
                ShowSideButtons = VPBConfig.Instance.ShowSideButtons,
                FollowAngle = VPBConfig.Instance.FollowAngle,
                FollowEyeHeight = VPBConfig.Instance.FollowEyeHeight,
                FollowDistance = VPBConfig.Instance.FollowDistance,
                ReorientStartAngle = VPBConfig.Instance.ReorientStartAngle,
                MovementThreshold = VPBConfig.Instance.MovementThreshold,
                BringToFrontDistance = VPBConfig.Instance.BringToFrontDistance,
                EnableDragDrop = VPBConfig.Instance.EnableDragDrop,
                GalleryAutoGenderFilter = VPBConfig.Instance.GalleryAutoGenderFilter,
                RequireDragHoldBeforeMove = VPBConfig.Instance.RequireDragHoldBeforeMove,
                DragHoldThreshold = VPBConfig.Instance.DragHoldThreshold,
                HoldToLaunchHoldSeconds = VPBConfig.Instance.HoldToLaunchHoldSeconds,
                AppearanceClothingApplyMode = VPBConfig.Instance.AppearanceClothingApplyMode,
                EnableAutoFixedGallery = VPBConfig.Instance.EnableAutoFixedGallery,
                InitialGalleryCategory = VPBConfig.Instance.InitialGalleryCategory,
                GalleryDefaultLeftSidePanel = VPBConfig.Instance.GalleryDefaultLeftSidePanel,
                GalleryDefaultRightSidePanel = VPBConfig.Instance.GalleryDefaultRightSidePanel,
                GalleryHideCreatorSideButtons = VPBConfig.Instance.GalleryHideCreatorSideButtons,
                PluginGalleryGridThumbnails = VPBConfig.Instance.PluginGalleryGridThumbnails,
                GalleryListNamesLegacyFileName = VPBConfig.Instance.GalleryListNamesLegacyFileName,
                GalleryHoverPreviewMode = VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode),
                GalleryListHoverPreviewSize = VPBConfig.Instance.GalleryListHoverPreviewSize,
                GalleryListHoverPreviewOffsetX = VPBConfig.Instance.GalleryListHoverPreviewOffsetX,
                GalleryListHoverPreviewOffsetY = VPBConfig.Instance.GalleryListHoverPreviewOffsetY,
                GalleryGridLabelsEnabled = VPBConfig.Instance.GalleryGridLabelsEnabled,
                GalleryGridLabelsAutoHideAtHighDensity = VPBConfig.Instance.GalleryGridLabelsAutoHideAtHighDensity,
                GalleryGridLabelFontSize = VPBConfig.Instance.GalleryGridLabelFontSize,
                GalleryGridSpacingX = VPBConfig.Instance.GalleryGridSpacingX,
                GalleryGridSpacingY = VPBConfig.Instance.GalleryGridSpacingY,
                GalleryGridThumbnailPadding = VPBConfig.Instance.GalleryGridThumbnailPadding,
                GalleryGridHoverBorderWidth = VPBConfig.Instance.GalleryGridHoverBorderWidth,
                GalleryGridSelectedBorderWidth = VPBConfig.Instance.GalleryGridSelectedBorderWidth,
                GalleryGridBorderInwardWhenSquare = VPBConfig.Instance.GalleryGridBorderInwardWhenSquare,
                GalleryGridBorderColorR = VPBConfig.Instance.GalleryGridBorderColorR,
                GalleryGridBorderColorG = VPBConfig.Instance.GalleryGridBorderColorG,
                GalleryGridBorderColorB = VPBConfig.Instance.GalleryGridBorderColorB,
                GalleryGridBorderColorA = VPBConfig.Instance.GalleryGridBorderColorA,
                GalleryOnlyWhenVamMenuVisible = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible,
                GalleryAnchorToVamMenu = VPBConfig.Instance.GalleryAnchorToVamMenu,
                GalleryCategoryQuickOrder = VPBConfig.Instance.GalleryCategoryQuickOrder ?? "",
                GalleryCategoryQuickSwitchHidden = VPBConfig.Instance.GalleryCategoryQuickSwitchHidden ?? "",
                HiddenCategories = VPBConfig.Instance.HiddenCategories != null
                    ? new HashSet<string>(VPBConfig.Instance.HiddenCategories, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private void EnsureInternalSettingsSession()
        {
            if (internalSettingsSessionActive) return;
            internalSettingsListRowHeightSession = 80f;
            internalSettingsPreSessionLayoutMode = layoutMode;
            internalSettingsPreSessionScrollNormalized = (scrollRect != null) ? scrollRect.verticalNormalizedPosition : 1f;
            internalSettingsHadPreSessionViewState = true;
            internalSettingsBackup = CreateInternalSettingsSnapshot();
            internalSettingsSessionActive = true;
        }

        private bool IsSettingsPanelOpen()
        {
            return leftActiveContent == ContentType.Settings || rightActiveContent == ContentType.Settings;
        }

        /// <summary>Merges backing <see cref="settingsFilter"/>, title bar search (primary UX while settings list is open), and side-rail search.</summary>
        private string CanonicalSettingsSideSearchText()
        {
            if (!IsSettingsPanelOpen())
                return settingsFilter ?? "";

            string fromVar = (settingsFilter ?? "").Trim();
            string fromTitle = titleSearchInput != null ? (titleSearchInput.text ?? "").Trim() : "";
            InputField sideBox = null;
            if (leftActiveContent == ContentType.Settings) sideBox = leftSearchInput;
            else if (rightActiveContent == ContentType.Settings) sideBox = rightSearchInput;
            string fromSide = sideBox != null ? (sideBox.text ?? "").Trim() : "";

            if (fromTitle.Length > 0 && fromVar.Length > 0 && fromSide.Length > 0) return settingsFilter ?? "";
            if (fromVar.Length > 0 && fromTitle.Length > 0) return settingsFilter ?? "";
            if (fromVar.Length > 0 && fromSide.Length > 0) return settingsFilter ?? "";
            if (fromTitle.Length > 0 && fromSide.Length > 0) return titleSearchInput.text ?? "";
            if (fromVar.Length > 0) return settingsFilter ?? "";
            if (fromTitle.Length > 0) return titleSearchInput.text ?? "";
            if (fromSide.Length > 0) return sideBox.text ?? "";
            return "";
        }

        /// <summary>Closes Settings side tab(s) and syncs internal session — use when navigating to Tags so Save→Tags never leaves Settings open on other rail.</summary>
        private void ForceCloseSettingsSidePanels()
        {
            if (leftActiveContent != ContentType.Settings && rightActiveContent != ContentType.Settings)
                return;
            if (leftActiveContent == ContentType.Settings) leftActiveContent = null;
            if (rightActiveContent == ContentType.Settings) rightActiveContent = null;
            try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, nameFilter ?? "", _titleBarSearchOnValueChanged); } catch { }
            SyncInternalSettingsListView();
            try { RefreshTboxConditionalActionButtons(); } catch { }
        }

        private void SyncInternalSettingsListView()
        {
            bool open = IsSettingsPanelOpen();
            if (open)
            {
                settingsListViewActive = true;
                RefreshInternalSettingsListRows();
                return;
            }

            // settingsListViewActive is also set in RefreshInternalSettingsListRows; still allow exit if pre-session restore pending (fixes Save after paths that never toggled Settings tab through Sync).
            if (!settingsListViewActive && !internalSettingsHadPreSessionViewState) return;
            settingsListViewActive = false;
            if (internalSettingsSessionActive) CancelInternalSettingsSession();
            if (internalSettingsHadPreSessionViewState)
            {
                SetLayoutMode(internalSettingsPreSessionLayoutMode);
                if (scrollRect != null)
                    scrollRect.verticalNormalizedPosition = Mathf.Clamp01(internalSettingsPreSessionScrollNormalized);
            }
            RefreshFiles(true);
            internalSettingsHadPreSessionViewState = false;
        }

        private void RefreshInternalSettingsListRows(bool keepScroll = false)
        {
            if (!IsSettingsPanelOpen()) return;
            try
            {
                string c = CanonicalSettingsSideSearchText();
                if (!string.IsNullOrEmpty((c ?? "").Trim()))
                    settingsFilter = c;
            }
            catch { }
            settingsListViewActive = true;
            EnsureInternalSettingsSession();
            // Settings list view: always minimum row height (no +/- scaling),
            // but still respect InnerPaneScale so text/controls remain readable.
            float paneScale = 1f;
            try { if (VPBConfig.Instance != null) paneScale = VPBConfig.Instance.CurrentInnerPaneScale; } catch { paneScale = 1f; }
            internalSettingsListRowHeightSession = 80f * Mathf.Clamp(paneScale, 0.01f, 100f);

            if (layoutMode != GalleryLayoutMode.List)
                SetLayoutMode(GalleryLayoutMode.List, false, true);
            // SetLayoutMode no-ops when already List — must reapply session min row height for settings.
            try
            {
                if (contentGO != null && layoutMode == GalleryLayoutMode.List)
                {
                    var rgv = contentGO.GetComponent<RecyclingGridView>();
                    if (rgv != null)
                    {
                        rgv.fixedColumns = 1;
                        rgv.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1);
                        // Settings list must adapt to viewport width (Top dock/full width resize).
                        rgv.SetAdaptiveConfig(true, 0f, 1, true);
                    }
                }
            }
            catch { }

            if (titleText != null)
                titleText.text = VPBTranslation.T("settings.title", "Settings");

            List<FileEntry> rows = BuildInternalSettingsRows();
            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(rows);
            selectedFiles.Clear();
            selectedFilePaths.Clear();

            if (recyclingGrid != null)
            {
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                if (!keepScroll) ScrollGalleryToTop();
                recyclingGrid.Refresh();
            }
            try { UpdatePaginationText(); } catch { }
            try { UpdateFooterLayoutState(); } catch { }
        }

        private List<FileEntry> BuildInternalSettingsRows()
        {
            string f = (CanonicalSettingsSideSearchText() ?? "").Trim();
            var rows = new List<FileEntry>(64);

            bool GroupAllowed(string group) =>
                string.Equals(currentSettingsGroup, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(currentSettingsGroup, group, StringComparison.OrdinalIgnoreCase);
            bool FilterAllowed(string label) =>
                string.IsNullOrEmpty(f) || (label ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
            void Add(InternalSettingDefinition def)
            {
                if (def == null) return;
                string key = def.Key;
                string group = def.GroupKey;
                string label = def.Label;
                if (!GroupAllowed(group)) return;
                if (!FilterAllowed(label)) return;
                try
                {
                    if (def.RowVisible != null && !def.RowVisible()) return;
                }
                catch { }
                rows.Add(new InternalSettingRowEntry(key, group, label));
            }

            var defs = BuildInternalSettingDefinitions();
            for (int i = 0; i < defs.Count; i++) Add(defs[i]);
            return rows;
        }

        /// <summary>Show semi-transparent hover preview frame while adjusting hover settings (sliders update live).</summary>
        private void NotifyInternalSettingsHoverPreviewChanged()
        {
            if (!internalSettingsSessionActive || VPBConfig.Instance == null)
            {
                try { SetHoverPreviewDummyActive(false); } catch { }
                return;
            }
            string m = VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode);
            if (string.Equals(m, "Off", StringComparison.OrdinalIgnoreCase))
            {
                SetHoverPreviewDummyActive(false);
                RefreshHoverPreviewLayoutImmediate();
                return;
            }
            SetHoverPreviewDummyActive(true);
            RefreshHoverPreviewLayoutImmediate();
        }

        private void ApplyInternalSettingDefinition(InternalSettingDefinition def, bool secondary)
        {
            if (def == null) return;
            switch (def.ControlType)
            {
                case InternalSettingControlType.Toggle:
                    if (def.GetBool != null && def.SetBool != null) def.SetBool(!def.GetBool());
                    break;
                case InternalSettingControlType.Cycle:
                    if (def.GetString != null && def.SetString != null)
                    {
                        string cur = def.GetString();
                        def.SetString(secondary ? PrevOf(cur, def.Options) : NextOf(cur, def.Options));
                    }
                    break;
                case InternalSettingControlType.Slider:
                    if (def.GetFloat != null && def.SetFloat != null)
                    {
                        float dir = secondary ? -1f : 1f;
                        float v = Mathf.Clamp(def.GetFloat() + (def.Step * dir), def.Min, def.Max);
                        def.SetFloat(v);
                    }
                    break;
                case InternalSettingControlType.TextArea:
                    break;
                case InternalSettingControlType.Button:
                    def.OnAction?.Invoke();
                    break;
                case InternalSettingControlType.ColorRgb:
                    break;
            }
        }

        internal bool HandleInternalSettingsRowClick(FileEntry file, bool secondary)
        {
            var row = file as InternalSettingRowEntry;
            if (row == null) return false;
            InternalSettingDefinition def = GetInternalSettingDefinition(row.RowKey);
            if (def == null) return false;
            if (def.ControlType == InternalSettingControlType.TextArea) return false;
            if (def.ControlType == InternalSettingControlType.ColorRgb) return false;
            ApplyInternalSettingDefinition(def, secondary);
            if (string.Equals(row.GroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                NotifyInternalSettingsHoverPreviewChanged();

            RefreshInternalSettingsListRows(true);
            return true;
        }

        private static void DestroyChildrenByName(Transform parent, string childName)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform ch = parent.GetChild(i);
                if (ch == null) continue;
                if (string.Equals(ch.name, childName, StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(ch.gameObject);
            }
        }

        private static GameObject CreateMiniButton(Transform parent, string label, float width, Color bg, Action onClick)
        {
            GameObject go = new GameObject("SettingsControlBtn");
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = bg;
            Button b = go.AddComponent<Button>();
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            b.colors = cb;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };

            float paneScale = 1f;
            try { if (VPBConfig.Instance != null) paneScale = VPBConfig.Instance.CurrentInnerPaneScale; } catch { paneScale = 1f; }
            paneScale = Mathf.Clamp(paneScale, 0.01f, 100f);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width * paneScale;
            le.minWidth = width * paneScale;
            le.preferredHeight = 32f * paneScale;
            le.minHeight = 32f * paneScale;
            le.flexibleWidth = 0f;

            GameObject tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);
            Text t = tgo.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = Mathf.Max(11, Mathf.RoundToInt(18f * paneScale));
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = label;
            RectTransform trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.sizeDelta = Vector2.zero;
            return go;
        }

        private void RebuildSettingsRowControls(GameObject btnGO, InternalSettingDefinition def)
        {
            if (btnGO == null || def == null) return;

            AddTooltipPlain(btnGO, def.Tooltip ?? def.Label ?? "");

            float paneScale = 1f;
            try { if (VPBConfig.Instance != null) paneScale = VPBConfig.Instance.CurrentInnerPaneScale; } catch { paneScale = 1f; }
            paneScale = Mathf.Clamp(paneScale, 0.01f, 100f);

            Transform listRowTr = btnGO.transform.Find("ListRow");
            if (listRowTr == null) return;
            Transform detailsTr = listRowTr.Find("Details");
            if (detailsTr == null) return;

            // Scale row label text ("ListRow/Name") for settings rows; base list UI scales elsewhere,
            // but settings rows rebuild controls and were skipping label font scaling.
            try
            {
                Transform nameTr = listRowTr.Find("Name");
                Text nameText = nameTr != null ? nameTr.GetComponent<Text>() : null;
                if (nameText != null)
                {
                    nameText.resizeTextForBestFit = false;
                    nameText.fontSize = Mathf.Max(12, Mathf.RoundToInt(28f * paneScale));
                }
                LayoutElement nameLe = nameTr != null ? nameTr.GetComponent<LayoutElement>() : null;
                if (nameLe != null)
                {
                    nameLe.minHeight = 32f * paneScale;
                }
            }
            catch { }

            for (int i = 0; i < detailsTr.childCount; i++)
            {
                Transform ch = detailsTr.GetChild(i);
                if (ch == null) continue;
                ch.gameObject.SetActive(false);
            }
            detailsTr.gameObject.SetActive(true);
            DestroyChildrenByName(detailsTr, "SettingsControlContainer");

            GameObject controls = new GameObject("SettingsControlContainer");
            controls.transform.SetParent(detailsTr, false);
            HorizontalLayoutGroup hlg = controls.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childControlHeight = true;
            hlg.childControlWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 6f * paneScale;
            LayoutElement cle = controls.AddComponent<LayoutElement>();
            cle.flexibleWidth = 1f;
            cle.minHeight = 32f * paneScale;

            if (def.ControlType == InternalSettingControlType.Toggle && def.GetBool != null && def.SetBool != null)
            {
                bool cur = def.GetBool();
                CreateMiniButton(controls.transform, "OFF", 58f, cur ? new Color(0.2f, 0.2f, 0.2f, 1f) : new Color(0.6f, 0.2f, 0.2f, 1f), () => {
                    def.SetBool(false);
                    RefreshInternalSettingsListRows(true);
                });
                CreateMiniButton(controls.transform, "ON", 58f, cur ? new Color(0.2f, 0.6f, 0.2f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f), () => {
                    def.SetBool(true);
                    RefreshInternalSettingsListRows(true);
                });
                return;
            }

            if (def.ControlType == InternalSettingControlType.Cycle && def.GetString != null && def.SetString != null)
            {
                string cur = def.GetString() ?? "";
                string display = (cur ?? "").ToUpperInvariant();
                GameObject cycleBtn = null;
                cycleBtn = CreateMiniButton(controls.transform, display, 150f, new Color(0.25f, 0.5f, 0.8f, 1f), () => {
                    // Read current value at click time (avoid stale captured value when row reuses objects).
                    string curNow = def.GetString() ?? "";
                    string next = NextOf(curNow, def.Options);
                    def.SetString(next);
                    try
                    {
                        // Update label immediately; pooled list rows can keep old text until rebind.
                        var t = cycleBtn != null ? cycleBtn.GetComponentInChildren<Text>(true) : null;
                        if (t != null) t.text = (next ?? "").ToUpperInvariant();
                    }
                    catch { }
                    if (string.Equals(def.GroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                        NotifyInternalSettingsHoverPreviewChanged();
                    RefreshInternalSettingsListRows(true);
                });
                try
                {
                    // Ensure control row sizes settle immediately (prevents clipping when switching cycle values).
                    LayoutRebuilder.ForceRebuildLayoutImmediate(detailsTr as RectTransform);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(listRowTr as RectTransform);
                }
                catch { }
                return;
            }

            if (def.ControlType == InternalSettingControlType.ColorRgb && def.GetColor != null && def.SetColor != null)
            {
                GameObject swatch = new GameObject("SettingsBorderColorSwatch");
                swatch.transform.SetParent(controls.transform, false);
                LayoutElement swle = swatch.AddComponent<LayoutElement>();
                swle.preferredWidth = 72f * paneScale;
                swle.minWidth = 48f * paneScale;
                swle.preferredHeight = 28f * paneScale;
                swle.minHeight = 28f * paneScale;
                swle.flexibleWidth = 0f;
                Image swImg = swatch.AddComponent<Image>();
                swImg.color = def.GetColor();
                swImg.raycastTarget = false;

                CreateMiniButton(
                    controls.transform,
                    VPBTranslation.T("settings.grid_border_color.choose", "CHOOSE…"),
                    120f,
                    new Color(0.25f, 0.5f, 0.8f, 1f),
                    () =>
                    {
                        Color initial = def.GetColor();
                        VPBUiPickers.PickColorRgb(this, def.Label, initial, picked =>
                        {
                            def.SetColor(picked);
                            try { swImg.color = def.GetColor(); } catch { }
                            RefreshInternalSettingsListRows(true);
                        });
                    });
                try
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(detailsTr as RectTransform);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(listRowTr as RectTransform);
                }
                catch { }
                return;
            }

            if (def.ControlType == InternalSettingControlType.Slider && def.GetFloat != null && def.SetFloat != null)
            {
                float cur = def.GetFloat();

                GameObject sliderHost = new GameObject("SettingsSliderHost");
                sliderHost.transform.SetParent(controls.transform, false);
                LayoutElement sle = sliderHost.AddComponent<LayoutElement>();
                sle.preferredWidth = 320f * paneScale;
                sle.minWidth = 120f * paneScale;
                sle.preferredHeight = 32f * paneScale;
                sle.minHeight = 32f * paneScale;
                sle.flexibleWidth = 1f;

                Slider slider = sliderHost.AddComponent<Slider>();
                slider.minValue = def.Min;
                slider.maxValue = def.Max;
                slider.value = Mathf.Clamp(cur, def.Min, def.Max);
                slider.wholeNumbers = def.Decimals <= 0;

                GameObject bg = new GameObject("Background");
                bg.transform.SetParent(sliderHost.transform, false);
                var bgImg = bg.AddComponent<Image>();
                bgImg.color = new Color(0.2f, 0.2f, 0.2f);
                RectTransform bgRT = bg.GetComponent<RectTransform>();
                bgRT.anchorMin = new Vector2(0, 0.4f); bgRT.anchorMax = new Vector2(1, 0.6f); bgRT.sizeDelta = Vector2.zero;

                GameObject fillArea = new GameObject("Fill Area");
                fillArea.transform.SetParent(sliderHost.transform, false);
                RectTransform faRT = fillArea.AddComponent<RectTransform>();
                faRT.anchorMin = new Vector2(0, 0.4f); faRT.anchorMax = new Vector2(1, 0.6f); faRT.sizeDelta = Vector2.zero;

                GameObject fill = new GameObject("Fill");
                fill.transform.SetParent(fillArea.transform, false);
                var fillImg = fill.AddComponent<Image>();
                fillImg.color = new Color(0.25f, 0.5f, 0.8f);
                RectTransform fillRT = fill.GetComponent<RectTransform>();
                fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one; fillRT.sizeDelta = Vector2.zero;
                slider.fillRect = fillRT;

                GameObject handleArea = new GameObject("Handle Area");
                handleArea.transform.SetParent(sliderHost.transform, false);
                RectTransform haRT = handleArea.AddComponent<RectTransform>();
                haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one; haRT.sizeDelta = Vector2.zero;

                GameObject handle = new GameObject("Handle");
                handle.transform.SetParent(handleArea.transform, false);
                var handleImg = handle.AddComponent<Image>();
                handleImg.color = Color.white;
                RectTransform handleRT = handle.GetComponent<RectTransform>();
                handleRT.anchorMin = new Vector2(0, 0); handleRT.anchorMax = new Vector2(0, 1); handleRT.sizeDelta = new Vector2(20f * paneScale, 0);
                slider.handleRect = handleRT;
                slider.targetGraphic = handleImg;

                GameObject inputGO = new GameObject("SettingsValueInput");
                inputGO.transform.SetParent(controls.transform, false);
                LayoutElement ile = inputGO.AddComponent<LayoutElement>();
                ile.preferredWidth = 78f * paneScale;
                ile.minWidth = 78f * paneScale;
                ile.preferredHeight = 32f * paneScale;
                ile.minHeight = 32f * paneScale;
                Image inputBg = inputGO.AddComponent<Image>();
                inputBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                InputField input = inputGO.AddComponent<InputField>();
                input.targetGraphic = inputBg;
                input.contentType = def.AllowNegative ? InputField.ContentType.Standard : InputField.ContentType.DecimalNumber;

                GameObject tgo = new GameObject("Text");
                tgo.transform.SetParent(inputGO.transform, false);
                Text it = tgo.AddComponent<Text>();
                it.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                it.fontSize = Mathf.Max(11, Mathf.RoundToInt(18f * paneScale));
                it.color = Color.white;
                it.alignment = TextAnchor.MiddleCenter;
                RectTransform itRT = tgo.GetComponent<RectTransform>();
                itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one; itRT.sizeDelta = Vector2.zero;
                input.textComponent = it;
                input.text = slider.value.ToString("F" + Math.Max(0, def.Decimals));

                slider.onValueChanged.AddListener(v =>
                {
                    input.text = v.ToString("F" + Math.Max(0, def.Decimals));
                    def.SetFloat(v);
                    if (string.Equals(def.GroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                        NotifyInternalSettingsHoverPreviewChanged();
                });
                input.onEndEdit.AddListener(s =>
                {
                    float parsed;
                    if (!float.TryParse(s, out parsed))
                    {
                        input.text = slider.value.ToString("F" + Math.Max(0, def.Decimals));
                        return;
                    }
                    parsed = Mathf.Clamp(parsed, def.Min, def.Max);
                    slider.value = parsed;
                    def.SetFloat(parsed);
                    input.text = parsed.ToString("F" + Math.Max(0, def.Decimals));
                    if (string.Equals(def.GroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                        NotifyInternalSettingsHoverPreviewChanged();
                });
                return;
            }

            if (def.ControlType == InternalSettingControlType.TextArea && def.GetString != null && def.SetString != null)
            {
                if (string.Equals(def.Key, "quick.categoryEditor", StringComparison.OrdinalIgnoreCase))
                {
                    cle.minHeight = 40f * paneScale;
                    GameObject btnRow = new GameObject("SettingsTextAreaButtons");
                    btnRow.transform.SetParent(controls.transform, false);
                    HorizontalLayoutGroup bh = btnRow.AddComponent<HorizontalLayoutGroup>();
                    bh.childAlignment = TextAnchor.MiddleRight;
                    bh.spacing = 6f * paneScale;
                    bh.childControlWidth = true;
                    bh.childControlHeight = true;
                    bh.childForceExpandWidth = false;
                    bh.childForceExpandHeight = false;
                    LayoutElement ble = btnRow.AddComponent<LayoutElement>();
                    ble.minHeight = 32f * paneScale;

                    CreateMiniButton(btnRow.transform, "EDIT…", 96f, new Color(0.25f, 0.5f, 0.8f, 1f), () =>
                    {
                        ShowCategoryQuickEditor();
                    });
                    return;
                }

                cle.minHeight = 96f * paneScale;
                GameObject taHost = new GameObject("SettingsTextAreaHost");
                taHost.transform.SetParent(controls.transform, false);
                LayoutElement tle = taHost.AddComponent<LayoutElement>();
                tle.flexibleWidth = 1f;
                tle.preferredWidth = 320f * paneScale;
                tle.minWidth = 120f * paneScale;
                tle.preferredHeight = 72f * paneScale;
                tle.minHeight = 72f * paneScale;

                Image taBg = taHost.AddComponent<Image>();
                taBg.color = new Color(0.16f, 0.16f, 0.18f, 1f);
                taBg.raycastTarget = true;
                InputField inf = taHost.AddComponent<InputField>();
                inf.lineType = InputField.LineType.MultiLineNewline;
                inf.targetGraphic = taBg;
                inf.interactable = true;
                inf.navigation = new Navigation { mode = Navigation.Mode.None };
                ColorBlock cb = inf.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.96f, 0.96f, 0.98f, 1f);
                cb.pressedColor = new Color(0.9f, 0.9f, 0.92f, 1f);
                cb.disabledColor = new Color(0.85f, 0.85f, 0.88f, 0.55f);
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0f;
                inf.colors = cb;

                GameObject textGo = new GameObject("Text");
                textGo.transform.SetParent(taHost.transform, false);
                Text taTxt = textGo.AddComponent<Text>();
                taTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                taTxt.fontSize = Mathf.Max(11, Mathf.RoundToInt(16f * paneScale));
                taTxt.color = new Color(0.95f, 0.95f, 0.97f, 1f);
                taTxt.alignment = TextAnchor.UpperLeft;
                taTxt.supportRichText = false;
                taTxt.raycastTarget = true;
                try { VPBUiFont.ApplyTo(taTxt); } catch { }
                RectTransform taTxtRt = textGo.GetComponent<RectTransform>();
                taTxtRt.anchorMin = Vector2.zero;
                taTxtRt.anchorMax = Vector2.one;
                taTxtRt.offsetMin = new Vector2(6f * paneScale, 6f * paneScale);
                taTxtRt.offsetMax = new Vector2(-6f * paneScale, -6f * paneScale);
                inf.textComponent = taTxt;
                inf.text = def.GetString() ?? "";

                inf.onValueChanged.AddListener(s => def.SetString(s ?? ""));
                inf.onEndEdit.AddListener(s =>
                {
                    def.SetString(s ?? "");
                    if (VPBConfig.Instance != null)
                        VPBConfig.Instance.TriggerChange();
                });
                return;
            }

            if (def.ControlType == InternalSettingControlType.Button && def.OnAction != null)
            {
                CreateMiniButton(controls.transform, "CLICK", 150f, new Color(0.7f, 0.4f, 0.2f, 1f), () => {
                    def.OnAction?.Invoke();
                    RefreshInternalSettingsListRows(true);
                });
                return;
            }
        }

        internal bool ConfigureInternalSettingsRowUI(GameObject btnGO, FileEntry file)
        {
            var row = file as InternalSettingRowEntry;
            if (row == null) return false;
            InternalSettingDefinition def = GetInternalSettingDefinition(row.RowKey);
            if (def == null) return false;
            RebuildSettingsRowControls(btnGO, def);
            return true;
        }

        private void SaveInternalSettingsSession()
        {
            if (!internalSettingsSessionActive) return;
            internalSettingsBackup = CreateInternalSettingsSnapshot();
            try { VPBConfig.Instance.Save(false); } catch { }
            VPBConfig.Instance.TriggerChange();
            try { SetHoverPreviewDummyActive(false); } catch { }
            internalSettingsSessionActive = false;
            internalSettingsBackup = null;
        }

        internal void ExitInternalSettingsMode(bool saveChanges)
        {
            if (saveChanges) SaveInternalSettingsSession();
            else CancelInternalSettingsSession();

            bool changed = false;
            if (leftActiveContent == ContentType.Settings)
            {
                leftActiveContent = null;
                changed = true;
            }
            if (rightActiveContent == ContentType.Settings)
            {
                rightActiveContent = null;
                changed = true;
            }

            if (!changed) return;
            UpdateLayout();
            UpdateTabs();
            SyncInternalSettingsListView();
            // Ensure toolbox exits Settings chrome immediately (Delete/etc reappear)
            try { RefreshTboxConditionalActionButtons(); } catch { }
        }

        private void CancelInternalSettingsSession()
        {
            if (!internalSettingsSessionActive || internalSettingsBackup == null) return;
            try { SetHoverPreviewDummyActive(false); } catch { }
            var b = internalSettingsBackup;
            VPBConfig.Instance.EnableGalleryFade = b.EnableGalleryFade;
            VPBConfig.Instance.EnableGalleryTranslucency = b.EnableGalleryTranslucency;
            VPBConfig.Instance.GalleryManualRefreshOnly = b.GalleryManualRefreshOnly;
            VPBConfig.Instance.GalleryOpacity = b.GalleryOpacity;
            VPBConfig.Instance.SideButtonScaleVR = b.SideButtonScaleVR;
            VPBConfig.Instance.SideButtonScaleDesktop = b.SideButtonScaleDesktop;
            VPBConfig.Instance.InnerPaneScaleVR = b.InnerPaneScaleVR;
            VPBConfig.Instance.InnerPaneScaleDesktop = b.InnerPaneScaleDesktop;
            VPBConfig.Instance.EnableButtonGaps = b.EnableButtonGaps;
            VPBConfig.Instance.ShowSideButtons = b.ShowSideButtons;
            VPBConfig.Instance.FollowAngle = b.FollowAngle;
            VPBConfig.Instance.FollowEyeHeight = b.FollowEyeHeight;
            VPBConfig.Instance.FollowDistance = b.FollowDistance;
            VPBConfig.Instance.ReorientStartAngle = b.ReorientStartAngle;
            VPBConfig.Instance.MovementThreshold = b.MovementThreshold;
            VPBConfig.Instance.BringToFrontDistance = b.BringToFrontDistance;
            VPBConfig.Instance.EnableDragDrop = b.EnableDragDrop;
            VPBConfig.Instance.GalleryAutoGenderFilter = b.GalleryAutoGenderFilter;
            VPBConfig.Instance.RequireDragHoldBeforeMove = b.RequireDragHoldBeforeMove;
            VPBConfig.Instance.DragHoldThreshold = b.DragHoldThreshold;
            VPBConfig.Instance.HoldToLaunchHoldSeconds = b.HoldToLaunchHoldSeconds;
            VPBConfig.Instance.AppearanceClothingApplyMode = b.AppearanceClothingApplyMode;
            VPBConfig.Instance.EnableAutoFixedGallery = b.EnableAutoFixedGallery;
            VPBConfig.Instance.InitialGalleryCategory = b.InitialGalleryCategory;
            VPBConfig.Instance.GalleryDefaultLeftSidePanel = b.GalleryDefaultLeftSidePanel;
            VPBConfig.Instance.GalleryDefaultRightSidePanel = b.GalleryDefaultRightSidePanel;
            VPBConfig.Instance.GalleryHideCreatorSideButtons = b.GalleryHideCreatorSideButtons;
            VPBConfig.Instance.PluginGalleryGridThumbnails = b.PluginGalleryGridThumbnails;
            VPBConfig.Instance.GalleryListNamesLegacyFileName = b.GalleryListNamesLegacyFileName;
            VPBConfig.Instance.GalleryHoverPreviewMode = b.GalleryHoverPreviewMode;
            VPBConfig.Instance.GalleryListHoverPreviewSize = b.GalleryListHoverPreviewSize;
            VPBConfig.Instance.GalleryListHoverPreviewOffsetX = b.GalleryListHoverPreviewOffsetX;
            VPBConfig.Instance.GalleryListHoverPreviewOffsetY = b.GalleryListHoverPreviewOffsetY;
            VPBConfig.Instance.GalleryGridLabelsEnabled = b.GalleryGridLabelsEnabled;
            VPBConfig.Instance.GalleryGridLabelsAutoHideAtHighDensity = b.GalleryGridLabelsAutoHideAtHighDensity;
            VPBConfig.Instance.GalleryGridLabelFontSize = b.GalleryGridLabelFontSize;
            VPBConfig.Instance.GalleryGridSpacingX = b.GalleryGridSpacingX;
            VPBConfig.Instance.GalleryGridSpacingY = b.GalleryGridSpacingY;
            VPBConfig.Instance.GalleryGridThumbnailPadding = b.GalleryGridThumbnailPadding;
            VPBConfig.Instance.GalleryGridHoverBorderWidth = b.GalleryGridHoverBorderWidth;
            VPBConfig.Instance.GalleryGridSelectedBorderWidth = b.GalleryGridSelectedBorderWidth;
            VPBConfig.Instance.GalleryGridBorderInwardWhenSquare = b.GalleryGridBorderInwardWhenSquare;
            VPBConfig.Instance.GalleryGridBorderColorR = b.GalleryGridBorderColorR;
            VPBConfig.Instance.GalleryGridBorderColorG = b.GalleryGridBorderColorG;
            VPBConfig.Instance.GalleryGridBorderColorB = b.GalleryGridBorderColorB;
            VPBConfig.Instance.GalleryGridBorderColorA = b.GalleryGridBorderColorA;
            VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible = b.GalleryOnlyWhenVamMenuVisible;
            VPBConfig.Instance.GalleryAnchorToVamMenu = b.GalleryAnchorToVamMenu;
            VPBConfig.Instance.GalleryCategoryQuickOrder = b.GalleryCategoryQuickOrder ?? "";
            VPBConfig.Instance.GalleryCategoryQuickSwitchHidden = b.GalleryCategoryQuickSwitchHidden ?? "";
            VPBConfig.Instance.HiddenCategories = b.HiddenCategories != null
                ? new HashSet<string>(b.HiddenCategories, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (this != null)
            {
                ApplySideButtonScale();
                categoriesCached = false;
                RebuildGridLayout();
                RefreshFiles(true);
            }
            VPBConfig.Instance.TriggerChange();
            internalSettingsSessionActive = false;
            internalSettingsBackup = null;
        }

        /// <summary>
        /// Shows a one-time BA migration prompt overlay on this panel.
        /// Called by Gallery after initial FileManager refresh when BA data dir is detected.
        /// </summary>
        internal void ShowBaMigrationPrompt()
        {
            if (this == null || gameObject == null) return;
            try
            {
                if (backgroundBoxGO == null) return;

                // Outer overlay — dims the gallery panel
                GameObject overlay = new GameObject("BA_MigrationPrompt");
                overlay.transform.SetParent(backgroundBoxGO.transform, false);
                RectTransform overlayRt = overlay.AddComponent<RectTransform>();
                overlayRt.anchorMin = Vector2.zero;
                overlayRt.anchorMax = Vector2.one;
                overlayRt.offsetMin = Vector2.zero;
                overlayRt.offsetMax = Vector2.zero;
                UnityEngine.UI.Image overlayBg = overlay.AddComponent<UnityEngine.UI.Image>();
                overlayBg.color = new Color(0f, 0f, 0f, 0.6f);
                overlay.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                try { SetLayerRecursive(overlay, backgroundBoxGO.layer); } catch { }

                // Dialog box
                GameObject box = new GameObject("DialogBox");
                box.transform.SetParent(overlay.transform, false);
                RectTransform boxRt = box.AddComponent<RectTransform>();
                boxRt.anchorMin = new Vector2(0.5f, 0.5f);
                boxRt.anchorMax = new Vector2(0.5f, 0.5f);
                boxRt.sizeDelta = new Vector2(560f, 260f);
                boxRt.anchoredPosition = Vector2.zero;
                UnityEngine.UI.Image boxBg = box.AddComponent<UnityEngine.UI.Image>();
                boxBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

                // Layout for text + buttons
                UnityEngine.UI.VerticalLayoutGroup vl = box.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                vl.padding = new RectOffset(16, 16, 16, 16);
                vl.spacing = 12f;
                vl.childAlignment = TextAnchor.UpperCenter;
                vl.childForceExpandWidth = true;
                vl.childForceExpandHeight = false;

                // Message text
                GameObject textGo = new GameObject("Message");
                textGo.transform.SetParent(box.transform, false);
                UnityEngine.UI.Text msg = textGo.AddComponent<UnityEngine.UI.Text>();
                msg.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                msg.fontSize = 22;
                msg.alignment = TextAnchor.MiddleCenter;
                msg.color = Color.white;
                msg.text = VPBTranslation.T("ba.prompt.msg",
                    "BrowserAssist data detected.\nImport available in Settings.\nOpen Settings → BrowserAssist section.");
                UnityEngine.UI.LayoutElement textLe = textGo.AddComponent<UnityEngine.UI.LayoutElement>();
                textLe.preferredHeight = 130f;
                textLe.flexibleWidth = 1f;

                // Button row
                GameObject btnRow = new GameObject("BtnRow");
                btnRow.transform.SetParent(box.transform, false);
                UnityEngine.UI.LayoutElement rowLe = btnRow.AddComponent<UnityEngine.UI.LayoutElement>();
                rowLe.preferredHeight = 48f;
                rowLe.flexibleWidth = 1f;

                Action dismiss = () => { try { UnityEngine.Object.Destroy(overlay); } catch { } };

                void SetDismissed()
                {
                    try
                    {
                        if (VPBConfig.Instance == null) return;
                        VPBConfig.Instance.BaMigrationPromptDismissed = true;
                        VPBConfig.Instance.Save();
                    }
                    catch { }
                }

                // TAKE ME THERE button
                UI.CreateUIButton(btnRow, 240f, 44f, VPBTranslation.T("ba.prompt.take_me_there", "Take me there"),
                    18, -140f, 0f, AnchorPresets.middleCenter, () =>
                    {
                        dismiss();
                        SetDismissed();
                        try
                        {
                            if (!IsSettingsPanelOpen())
                            {
                                // If prompt ever called outside Settings, force Settings open on right by default.
                                ToggleRight(ContentType.Settings);
                            }
                        }
                        catch { }
                        try
                        {
                            currentSettingsGroup = "ba_migration";
                            UpdateTabs();
                            RefreshInternalSettingsListRows(false);
                        }
                        catch { }
                    });

                // OK button
                UI.CreateUIButton(btnRow, 140f, 44f, VPBTranslation.T("ba.prompt.ok", "OK"),
                    18, 160f, 0f, AnchorPresets.middleCenter, () =>
                    {
                        dismiss();
                        SetDismissed();
                    });
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB BA] ShowBaMigrationPrompt failed: " + ex.Message);
            }
        }

        private void TryShowBaMigrationPromptOnSettingsEnter()
        {
            if (!IsSettingsPanelOpen()) return;
            if (VPBConfig.Instance == null || VPBConfig.Instance.BaMigrationPromptDismissed) return;
            if (!Gallery.TryConsumeBaMigrationPromptPending()) return;
            if (!BaImporter.TryDetectBaDataDir(out _)) return;
            ShowBaMigrationPrompt();
        }
    }
}
