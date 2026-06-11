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
            Hotkey,
        }

        private sealed class InternalSettingDefinition
        {
            public string Key;
            public string GroupKey;
            /// <summary>When <see cref="GroupKey"/> is <c>categories</c>, filters rows under that settings tab.</summary>
            public string SubGroupKey;
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

        private List<InternalSettingDefinition> _internalSettingsDefsCache;
        private Dictionary<string, InternalSettingDefinition> _internalSettingsDefsByKey;
        private int _internalSettingsDefsCacheSig = int.MinValue;

        private void InvalidateInternalSettingsDefsCache()
        {
            _internalSettingsDefsCache = null;
            _internalSettingsDefsByKey = null;
            _internalSettingsDefsCacheSig = int.MinValue;
        }

        private int ComputeInternalSettingsDefsCacheSignature()
        {
            int sig = 0;
            try { if (categories != null) sig = categories.Count; } catch { }
            try
            {
                var hidden = VPBConfig.Instance != null ? VPBConfig.Instance.HiddenCategories : null;
                if (hidden != null) sig = unchecked(sig * 31 + hidden.Count);
            }
            catch { }
            if (BaImporter.TryDetectBaDataDir(out _)) sig = unchecked(sig * 31 + 1);
            if (BaImporter.MigrationManifestExists()) sig = unchecked(sig * 31 + 2);
            try
            {
                var c = VPBConfig.Instance;
                if (c != null)
                {
                    sig = unchecked(sig * 31 + (c.PerfApplyHair ? 1 : 0));
                    sig = unchecked(sig * 31 + (c.PerfApplyMirrors ? 2 : 0));
                    sig = unchecked(sig * 31 + (c.PerfApplyVaMQualityPreset ? 4 : 0));
                }
            }
            catch { }
            return sig;
        }

        private List<InternalSettingDefinition> GetInternalSettingDefinitionsCached()
        {
            int sig = ComputeInternalSettingsDefsCacheSignature();
            if (_internalSettingsDefsCache != null && _internalSettingsDefsCacheSig == sig)
                return _internalSettingsDefsCache;

            var defs = BuildInternalSettingDefinitions();
            var byKey = new Dictionary<string, InternalSettingDefinition>(defs.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d != null && !string.IsNullOrEmpty(d.Key))
                    byKey[d.Key] = d;
            }
            _internalSettingsDefsCache = defs;
            _internalSettingsDefsByKey = byKey;
            _internalSettingsDefsCacheSig = sig;
            return defs;
        }

        private void ApplyInternalSettingsListGridConfig(RecyclingGridView rgv, bool deferRefresh)
        {
            if (rgv == null) return;
            rgv.fixedColumns = 1;
            rgv.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh);
            rgv.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh);
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

        private static bool GalleryTransparencySubSettingsVisible()
        {
            try { return VPBConfig.Instance != null && !VPBConfig.Instance.DisableGalleryTransparency; }
            catch { return true; }
        }

        private static bool GalleryPaneTransparencySubSettingsVisible()
        {
            try
            {
                return GalleryTransparencySubSettingsVisible()
                    && VPBConfig.Instance != null
                    && !VPBConfig.Instance.ShouldDisableGalleryPaneTransparency();
            }
            catch { return false; }
        }

        private sealed class InternalSettingsSnapshot
        {
            public bool DisableGalleryTransparency;
            public bool DisableGalleryPaneTransparency;
            public bool DisableGalleryAssignableButtonsTransparency;
            public bool DisableGalleryDockHoverTransparency;
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
            public bool GalleryCollapseOnSceneLaunch;
            public bool RequireDragHoldBeforeMove;
            public float DragHoldThreshold;
            public float HoldToLaunchHoldSeconds;
            public string AppearanceClothingApplyMode;
            public bool EnableAutoFixedGallery;
            public string InitialGalleryCategory;
            public string GalleryDefaultLeftSidePanel;
            public string GalleryDefaultRightSidePanel;
            public string GalleryDefaultUserTagAvailMode;
            public bool GalleryHideUnusedUserTagsInFilterMode;
            public string GalleryUserTagFilterCombineMode;
            public float GalleryScrollButtonStepViewportFraction;
            public bool GalleryScrollButtonsEnabled;
            public bool GalleryVrThumbstickScrollEnabled;
            public bool GalleryHideCreatorSideButtons;
            public bool GalleryConsolidateCreatorNames;
            public bool PluginGalleryGridThumbnails;
            public bool PluginGalleryCategoryLabelsOnly;
            public bool GalleryThumbPlaceholderLabelsEnabled;
            public float GalleryThumbPlaceholderSizeScale;
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
            public bool GalleryScanWlBorderEnabled;
            public bool GalleryScanWlBorderShowInGrid;
            public bool GalleryScanWlBorderShowInList;
            public float GalleryScanWlBorderWidth;
            public float GalleryScanWlGridFrameInset;
            public float GalleryScanWlListFrameInset;
            public bool GalleryScanWlBorderOnThumbnail;
            public float GalleryScanWlBorderColorR;
            public float GalleryScanWlBorderColorG;
            public float GalleryScanWlBorderColorB;
            public float GalleryScanWlBorderColorA;
            public bool GalleryScanWlTempBorderEnabled;
            public bool GalleryScanWlTempBorderShowInGrid;
            public bool GalleryScanWlTempBorderShowInList;
            public float GalleryScanWlTempBorderWidth;
            public float GalleryScanWlTempGridFrameInset;
            public float GalleryScanWlTempListFrameInset;
            public bool GalleryScanWlTempBorderOnThumbnail;
            public float GalleryScanWlTempBorderColorR;
            public float GalleryScanWlTempBorderColorG;
            public float GalleryScanWlTempBorderColorB;
            public float GalleryScanWlTempBorderColorA;
            public bool GalleryOnlyWhenVamMenuVisible;
            public bool GalleryAnchorToVamMenu;
            public string GalleryCategoryQuickOrder;
            public string GalleryCategoryQuickSwitchHidden;
            public HashSet<string> HiddenCategories;

            public string PluginGalleryKey;
            public string PluginCreateGalleryKey;
            public string PluginHubKey;
            public string PluginClearConsoleKey;
            public bool PluginDownscale8kTo4k;
            public bool PluginScanWhitelistEnabled;
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
                Key = "visuals.disableTransparency", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.disable_all_transparency", "Disable all transparency"),
                Tooltip = VPBTranslation.T("settings.tip.disable_all_transparency", "Keeps assignable quick-menu slots, dock collapse strips, and the gallery pane fully opaque. Overrides all transparency sub-options."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.DisableGalleryTransparency,
                SetBool = v => {
                    VPBConfig.Instance.DisableGalleryTransparency = v;
                    ApplyGalleryTransparencyToAllPanels();
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.disablePaneTransparency", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.disable_gallery_transparency", "Disable gallery transparency"),
                Tooltip = VPBTranslation.T("settings.tip.disable_gallery_transparency", "Keeps the gallery pane fully opaque (no idle translucency). Does not affect assignable slots, dock strips, or side-button fade."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.DisableGalleryPaneTransparency,
                SetBool = v => {
                    VPBConfig.Instance.DisableGalleryPaneTransparency = v;
                    ApplyGalleryTransparencyToAllPanels();
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    VPBConfig.Instance.TriggerChange();
                },
                RowVisible = GalleryTransparencySubSettingsVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.disableAssignableTransparency", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.disable_assignable_buttons_transparency", "Disable assignable button transparency"),
                Tooltip = VPBTranslation.T("settings.tip.disable_assignable_buttons_transparency", "Makes quick-menu assignable slot backgrounds fully opaque (no see-through grid cells)."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.DisableGalleryAssignableButtonsTransparency,
                SetBool = v => {
                    VPBConfig.Instance.DisableGalleryAssignableButtonsTransparency = v;
                    ApplyGalleryTransparencyToAllPanels();
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    VPBConfig.Instance.TriggerChange();
                },
                RowVisible = GalleryTransparencySubSettingsVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.disableDockHoverTransparency", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.disable_dock_hover_transparency", "Disable dock hover-area transparency"),
                Tooltip = VPBTranslation.T("settings.tip.disable_dock_hover_transparency", "Makes fixed-mode collapse expand strips fully opaque. Side buttons stay independent with no panel backdrop."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.DisableGalleryDockHoverTransparency,
                SetBool = v => {
                    VPBConfig.Instance.DisableGalleryDockHoverTransparency = v;
                    ApplyGalleryTransparencyToAllPanels();
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    VPBConfig.Instance.TriggerChange();
                },
                RowVisible = GalleryTransparencySubSettingsVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.idleTransparency", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.gallery_idle_transparency", "Transparency when not hovered over"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_idle_transparency", "Makes the gallery pane translucent when the pointer is not over it. Fully opaque while hovered."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableGalleryTranslucency,
                SetBool = v => {
                    VPBConfig.Instance.EnableGalleryTranslucency = v;
                    ApplyGalleryTransparencyToAllPanels();
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    VPBConfig.Instance.TriggerChange();
                },
                RowVisible = GalleryPaneTransparencySubSettingsVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.idleOpacity", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.gallery_idle_opacity", "Opacity when not hovered over"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_idle_opacity", "How visible the gallery pane is when the pointer is not over it (1.0 = fully opaque, 0.1 = barely visible)."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryOpacity,
                SetFloat = v => {
                    VPBConfig.Instance.GalleryOpacity = v;
                    ApplyGalleryTransparencyToAllPanels();
                    VPBConfig.Instance.TriggerChange();
                },
                Min = 0.1f, Max = 1.0f, Step = 0.1f, Decimals = 1,
                RowVisible = () => GalleryPaneTransparencySubSettingsVisible()
                    && VPBConfig.Instance != null && VPBConfig.Instance.EnableGalleryTranslucency
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.fade", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.side_button_fade_idle", "Fade side buttons when not hovered over"),
                Tooltip = VPBTranslation.T("settings.tip.side_button_fade_idle", "Hides side buttons when the pointer is not over the gallery pane or side strip."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableGalleryFade,
                SetBool = v => {
                    VPBConfig.Instance.EnableGalleryFade = v;
                    ApplyGalleryTransparencyToAllPanels();
                    VPBConfig.Instance.TriggerChange();
                },
                RowVisible = GalleryTransparencySubSettingsVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.manualRefresh", GroupKey = "visuals", Label = VPBTranslation.T("settings.gallery_manual_refresh_only", "Manual gallery refresh only"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_manual_refresh_only", "When enabled, package scans do not update the file grid until you press Refresh in the gallery. Reduces scroll jumps and load when the package index changes often."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryManualRefreshOnly,
                SetBool = v => { VPBConfig.Instance.GalleryManualRefreshOnly = v; VPBConfig.Instance.TriggerChange(); }
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
                Key = "vr.hoverTooltip", GroupKey = "vr",
                Label = VPBTranslation.T("settings.vr_hover_tooltip", "VR hover tooltips"),
                Tooltip = VPBTranslation.T("settings.tip.vr_hover_tooltip", "After a short hover in VR, show a local label on controls (footer tooltips still apply on desktop)."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.VrHoverTooltipEnabled,
                SetBool = v => { VPBConfig.Instance.VrHoverTooltipEnabled = v; VPBConfig.Instance.TriggerChange(); }
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
                Key = "interaction.autoGenderFilter", GroupKey = "categories", SubGroupKey = "options", Label = VPBTranslation.T("settings.gallery_auto_gender_filter", "Auto gender filter (Hair/Clothing)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_auto_gender_filter", "When ON, Hair/Clothing categories auto-filter Male/Female items to match selected target atom gender."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryAutoGenderFilter,
                SetBool = v => { VPBConfig.Instance.GalleryAutoGenderFilter = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "interaction.collapseOnSceneLaunch", GroupKey = "interaction", Label = VPBTranslation.T("settings.gallery_collapse_on_scene_launch", "Collapse gallery on scene launch"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_collapse_on_scene_launch", "When ON, visible gallery panes collapse to the dock edge (fixed mode) or hide (floating) when you launch a scene."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryCollapseOnSceneLaunch,
                SetBool = v => { VPBConfig.Instance.GalleryCollapseOnSceneLaunch = v; VPBConfig.Instance.TriggerChange(); }
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
                Key = "desktop.initialCategory", GroupKey = "categories", SubGroupKey = "options", Label = VPBTranslation.T("settings.initial_gallery_category", "Gallery opens on"),
                Tooltip = VPBTranslation.T("settings.tip.initial_gallery_category", "Which category is shown when gallery opens."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" },
                GetString = () => VPBConfig.NormalizeInitialGalleryCategory(VPBConfig.Instance.InitialGalleryCategory),
                SetString = v => { VPBConfig.Instance.InitialGalleryCategory = v; VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "lists.defaultLeft", GroupKey = "lists", Label = VPBTranslation.T("settings.gallery_default_left_panel", "Left side list (default)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_left_panel", "Which filter list or Import sidebar opens on the left for new panes."),
                ControlType = InternalSettingControlType.Cycle, Options = VPBConfig.GallerySidePanelOptions,
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
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_right_panel", "Which filter list or Import sidebar opens on the right for new panes."),
                ControlType = InternalSettingControlType.Cycle, Options = VPBConfig.GallerySidePanelOptions,
                GetString = () => VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.GalleryDefaultRightSidePanel),
                SetString = v => {
                    VPBConfig.Instance.GalleryDefaultRightSidePanel = v;
                    // Avoid clobbering the active Settings side tab while user is interacting with Settings UI.
                    if (!IsSettingsPanelOpen()) ApplySidePanelDefaultsFromConfig();
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "tags.defaultAction", GroupKey = "tags",
                Label = VPBTranslation.T("settings.gallery_default_user_tag_mode", "Tags panel default action"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_user_tag_mode", "Mode when opening the User Tags side panel: filter grid by tags, apply tags to selection, or show untagged items only."),
                ControlType = InternalSettingControlType.Cycle,
                Options = new[] { "Filter tags", "Apply tags", "Untagged only" },
                GetString = () => VPBConfig.FormatGalleryDefaultUserTagAvailModeForSettings(VPBConfig.Instance.GalleryDefaultUserTagAvailMode),
                SetString = v => {
                    VPBConfig.Instance.GalleryDefaultUserTagAvailMode = VPBConfig.NormalizeGalleryDefaultUserTagAvailMode(v);
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "tags.hideUnusedInFilterMode", GroupKey = "tags",
                Label = VPBTranslation.T("settings.gallery_hide_unused_user_tags_in_filter", "Hide unused tags in filter mode"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_hide_unused_user_tags_in_filter", "In filter-by-tags mode, hide tags that are not on any item in the current category view."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode,
                SetBool = v => {
                    VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode = v;
                    VPBConfig.Instance.TriggerChange();
                    try { UpdateTabs(); } catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "tags.filterCombineMode", GroupKey = "tags",
                Label = VPBTranslation.T("settings.gallery_user_tag_filter_combine", "Multi-tag filter combine"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_user_tag_filter_combine", "With multiple tags selected in filter mode: Compound shows items with any selected tag; Isolate shows items that have all selected tags."),
                ControlType = InternalSettingControlType.Cycle,
                Options = new[] { "Compound", "Isolate" },
                GetString = () => VPBConfig.NormalizeGalleryUserTagFilterCombineMode(VPBConfig.Instance.GalleryUserTagFilterCombineMode),
                SetString = v => {
                    VPBConfig.Instance.GalleryUserTagFilterCombineMode = VPBConfig.NormalizeGalleryUserTagFilterCombineMode(v);
                    VPBConfig.Instance.TriggerChange();
                    try { RefreshFiles(true, false, false, "user_tag_filter_combine"); } catch { }
                    try { UpdateTabs(); } catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.scrollButtons", GroupKey = "lists", Label = VPBTranslation.T("settings.gallery_scroll_buttons", "VR scroll buttons"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_scroll_buttons", "Shows large up/down scroll buttons on gallery and tag lists in VR mode."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryScrollButtonsEnabled,
                SetBool = v => { VPBConfig.Instance.GalleryScrollButtonsEnabled = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.vrThumbstickScroll", GroupKey = "lists",
                Label = VPBTranslation.T("settings.gallery_vr_thumbstick_scroll", "VR thumbstick gallery scroll"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_vr_thumbstick_scroll", "When the VR pointer is over a gallery pane, thumbstick up/down scrolls the list instead of moving in the world."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryVrThumbstickScrollEnabled,
                SetBool = v => { VPBConfig.Instance.GalleryVrThumbstickScrollEnabled = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.scrollStep", GroupKey = "lists", Label = VPBTranslation.T("settings.gallery_scroll_button_step", "Scroll button step"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_scroll_button_step", "How far big up/down scroll buttons move, measured in visible panel heights."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryScrollButtonStepViewportFraction,
                SetFloat = v => { VPBConfig.Instance.GalleryScrollButtonStepViewportFraction = Mathf.Clamp(v, 0.10f, 2.00f); VPBConfig.Instance.TriggerChange(); },
                Min = 0.10f, Max = 2.00f, Step = 0.05f, Decimals = 2,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.GalleryScrollButtonsEnabled
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
                Key = "helpers.consolidateCreatorNames", GroupKey = "helpers",
                Label = VPBTranslation.T("settings.gallery_consolidate_creator_names", "Consolidate creator names"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_consolidate_creator_names", "Merge creator list entries that differ only by letter case. Shows the spelling with the most packages and sums counts. Filtering still matches all case variants."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryConsolidateCreatorNames,
                SetBool = v => {
                    VPBConfig.Instance.GalleryConsolidateCreatorNames = v;
                    try { VPBConfig.Instance.Save(false); } catch { }
                    try { ClearCreatorFilters(); } catch { }
                    try { GalleryFileListSnapshotCache.Clear(); } catch { }
                    PushCreatorFilterSqlModeForDatabase();
                    InvalidateDisplayCreatorsCache();
                    unchecked { creatorSideTabDataRevision++; }
                    try { RebuildTitleCreatorVirtView(force: true); UpdateTitleCreatorVirtualVisible(); } catch { }
                    try { UpdateTabs(); } catch { }
                    try { RefreshFilesAndTabs(); } catch { }
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "helpers.hairSwapKeepVisible", GroupKey = "helpers",
                Label = VPBTranslation.T("settings.helpers_hair_swap_keep_visible", "Keep hair visible during swap"),
                Tooltip = VPBTranslation.T("settings.tip.helpers_hair_swap_keep_visible", "While a hair preset loads, keep the previous hair visible (and its colors) until the new hair is ready. Outgoing hair collisions turn off first; old hair hides only after incoming hair finishes loading."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => {
                    try {
                        return Settings.Instance != null
                            && Settings.Instance.HairSwapKeepVisibleUntilLoaded != null
                            && Settings.Instance.HairSwapKeepVisibleUntilLoaded.Value;
                    } catch { return true; }
                },
                SetBool = v => {
                    try {
                        if (Settings.Instance != null && Settings.Instance.HairSwapKeepVisibleUntilLoaded != null)
                            Settings.Instance.HairSwapKeepVisibleUntilLoaded.Value = v;
                        Settings.SaveConfig();
                    } catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "helpers.returnToSceneViewOnStartup", GroupKey = "helpers",
                Label = VPBTranslation.T("settings.helpers_return_to_scene_on_startup", "Return to scene view on startup"),
                Tooltip = VPBTranslation.T("settings.tip.helpers_return_to_scene_on_startup", "On startup, skip VaM main menu (World UI) and go straight to scene view — same as Return To Scene View."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => {
                    try {
                        return Settings.Instance != null
                            && Settings.Instance.ReturnToSceneViewOnStartup != null
                            && Settings.Instance.ReturnToSceneViewOnStartup.Value;
                    } catch { return false; }
                },
                SetBool = v => {
                    try {
                        if (Settings.Instance != null && Settings.Instance.ReturnToSceneViewOnStartup != null)
                            Settings.Instance.ReturnToSceneViewOnStartup.Value = v;
                        Settings.SaveConfig();
                    } catch { }
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
                Key = "lists.pluginLabelsOnly", GroupKey = "categories", SubGroupKey = "options",
                Label = VPBTranslation.T("settings.plugin_gallery_category_labels_only", "Plugins category: labels only"),
                Tooltip = VPBTranslation.T("settings.tip.plugin_gallery_category_labels_only", "In the Plugins category, hide all thumbnails and show in-preview labels for every plugin row, including items that have sister images."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PluginGalleryCategoryLabelsOnly,
                SetBool = v => {
                    VPBConfig.Instance.PluginGalleryCategoryLabelsOnly = v;
                    RefreshThumbPlaceholderLabelLayout();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.pluginConsolidateCslist", GroupKey = "lists",
                Label = VPBTranslation.T("settings.plugin_consolidate_cslist", "Plugins: consolidate .cslist source files"),
                Tooltip = VPBTranslation.T("settings.tip.plugin_consolidate_cslist", "Hide .cs files that a .cslist already references, so multi-file plugins show as a single .cslist row. Standalone .cs files (not in any .cslist) always show."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => {
                    try {
                        return Settings.Instance != null
                            && Settings.Instance.PluginConsolidateCslist != null
                            && Settings.Instance.PluginConsolidateCslist.Value;
                    } catch { return false; }
                },
                SetBool = v => {
                    try {
                        if (Settings.Instance != null && Settings.Instance.PluginConsolidateCslist != null)
                            Settings.Instance.PluginConsolidateCslist.Value = v;
                        Settings.SaveConfig();
                        if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                        else RefreshFiles(true);
                    } catch { }
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
                Key = "grid.prettyPresetNames", GroupKey = "grid", Label = VPBTranslation.T("settings.gallery_pretty_preset_names", "Pretty preset names"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_pretty_preset_names", "Strip Preset_/Plugins_ prefix and file extension from preset labels. Path moves to hover tooltip."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryPrettyPresetNames,
                SetBool = v => {
                    VPBConfig.Instance.GalleryPrettyPresetNames = v;
                    LogUtil.LogWarning("[VPB] PRETTY toggle GalleryPrettyPresetNames=" + v);
                    ResetPrettyNameDiagnosticsSample();
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    else RefreshFiles(true);
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "search.scope", GroupKey = "search", Label = VPBTranslation.T("settings.gallery_search_scope", "Search Scope"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_search_scope", "What the gallery search box matches against. Path + Name = current; Name only = less verbose; Name starts with = prefix only."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Path + Name", "Name only", "Name starts with" },
                GetString = () => GallerySearchScopeToLabel(VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope)),
                SetString = v => {
                    VPBConfig.Instance.GallerySearchScope = GallerySearchScopeFromLabel(v);
                    LogUtil.LogWarning("[VPB] PRETTY toggle GallerySearchScope=" + VPBConfig.Instance.GallerySearchScope + " (raw='" + v + "')");
                    ResetPrettyNameDiagnosticsSample();
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
                Key = "grid.thumbPlaceholder", GroupKey = "grid",
                Label = VPBTranslation.T("settings.gallery_thumb_placeholder_labels", "In-preview labels (no thumbnail)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_thumb_placeholder_labels", "Show creator, package, and item name inside the preview when no thumbnail is available or the image is blank."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled,
                SetBool = v => {
                    VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled = v;
                    RefreshThumbPlaceholderLabelLayout();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.thumbPlaceholderScale", GroupKey = "grid",
                Label = VPBTranslation.T("settings.gallery_thumb_placeholder_size", "In-preview label size"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_thumb_placeholder_size", "Scales placeholder text with grid cell size. Lower values avoid overlap in dense grids."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GetGalleryThumbPlaceholderSizeScale(),
                SetFloat = v => {
                    VPBConfig.Instance.GalleryThumbPlaceholderSizeScale = VPBConfig.ClampGalleryThumbPlaceholderSizeScale(v);
                    RefreshThumbPlaceholderLabelLayout();
                },
                Min = 0.25f, Max = 2f, Step = 0.05f, Decimals = 2,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled
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
                Key = "scanWlBorder.enabled", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_enabled", "Persistent whitelist border"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_enabled", "Draw an inward border on gallery rows for packages in whitelisted folders or with a persisted UID override."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlBorderEnabled,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlBorderEnabled = v; RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlBorder.showGrid", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_show_grid", "Show in grid view"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_show_grid", "Show scan-whitelist border on included packages in grid layout."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlBorderShowInGrid,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlBorderShowInGrid = v; RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlBorder.showList", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_show_list", "Show in list view"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_show_list", "Show scan-whitelist border on included packages in list layout."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlBorderShowInList,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlBorderShowInList = v; RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlBorder.width", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_width", "Border width"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_width", "Thickness of the scan-whitelist border (pixels). Set to 0 to hide without disabling."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GalleryScanWlBorderWidth,
                SetFloat = v => { VPBConfig.Instance.GalleryScanWlBorderWidth = v; RefreshGalleryScanWlBorderVisuals(); },
                Min = 0f, Max = 12f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlBorder.color", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_color", "Border color"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_color", "Color of the scan-whitelist border in grid and list layout."),
                ControlType = InternalSettingControlType.ColorRgb,
                GetColor = () => VPBConfig.Instance.GetGalleryScanWlBorderColor(),
                SetColor = c => { VPBConfig.Instance.SetGalleryScanWlBorderColor(c); RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlBorder.gridInset", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_grid_inset", "Grid frame inset"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_grid_inset", "Inset of the border frame from the grid cell or thumbnail edge (pixels)."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GalleryScanWlGridFrameInset,
                SetFloat = v => { VPBConfig.Instance.GalleryScanWlGridFrameInset = v; RefreshGalleryScanWlBorderVisuals(); },
                Min = 0f, Max = 16f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlBorder.listInset", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_list_inset", "List frame inset"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_list_inset", "Inset of the border frame from the list row edge (pixels)."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GalleryScanWlListFrameInset,
                SetFloat = v => { VPBConfig.Instance.GalleryScanWlListFrameInset = v; RefreshGalleryScanWlBorderVisuals(); },
                Min = 0f, Max = 16f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlBorder.onThumbnail", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_border_on_thumbnail", "Grid: border on thumbnail"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_on_thumbnail", "When enabled, grid border hugs the thumbnail. When off, border uses the full cell."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlBorderOnThumbnail,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlBorderOnThumbnail = v; RefreshGalleryScanWlBorderVisuals(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.enabled", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_enabled", "Temporary whitelist border"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_enabled", "Draw an inward border on gallery rows for packages with a session-only temporary UID override."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlTempBorderEnabled,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlTempBorderEnabled = v; RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.showGrid", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_show_grid", "Temporary: show in grid view"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_show_grid", "Show temporary whitelist border in grid layout."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlTempBorderShowInGrid,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlTempBorderShowInGrid = v; RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.showList", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_show_list", "Temporary: show in list view"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_show_list", "Show temporary whitelist border in list layout."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlTempBorderShowInList,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlTempBorderShowInList = v; RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.width", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_width", "Temporary border width"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_width", "Thickness of the temporary whitelist border (pixels). Set to 0 to hide without disabling."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GalleryScanWlTempBorderWidth,
                SetFloat = v => { VPBConfig.Instance.GalleryScanWlTempBorderWidth = v; RefreshGalleryScanWlBorderVisuals(); },
                Min = 0f, Max = 12f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.color", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_color", "Temporary border color"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_color", "Color of the temporary whitelist border in grid and list layout."),
                ControlType = InternalSettingControlType.ColorRgb,
                GetColor = () => VPBConfig.Instance.GetGalleryScanWlTempBorderColor(),
                SetColor = c => { VPBConfig.Instance.SetGalleryScanWlTempBorderColor(c); RefreshGalleryScanWlBorderVisuals(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.gridInset", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_grid_inset", "Temporary grid frame inset"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_grid_inset", "Inset of the temporary border frame from the grid cell or thumbnail edge (pixels)."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GalleryScanWlTempGridFrameInset,
                SetFloat = v => { VPBConfig.Instance.GalleryScanWlTempGridFrameInset = v; RefreshGalleryScanWlBorderVisuals(); },
                Min = 0f, Max = 16f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.listInset", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_list_inset", "Temporary list frame inset"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_list_inset", "Inset of the temporary border frame from the list row edge (pixels)."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GalleryScanWlTempListFrameInset,
                SetFloat = v => { VPBConfig.Instance.GalleryScanWlTempListFrameInset = v; RefreshGalleryScanWlBorderVisuals(); },
                Min = 0f, Max = 16f, Step = 1f, Decimals = 0
            });
            defs.Add(new InternalSettingDefinition {
                Key = "scanWlTempBorder.onThumbnail", GroupKey = "scan_wl_border",
                Label = VPBTranslation.T("settings.scan_wl_temp_border_on_thumbnail", "Temporary grid: border on thumbnail"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_on_thumbnail", "When enabled, temporary grid border hugs the thumbnail. When off, border uses the full cell."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryScanWlTempBorderOnThumbnail,
                SetBool = v => { VPBConfig.Instance.GalleryScanWlTempBorderOnThumbnail = v; RefreshGalleryScanWlBorderVisuals(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "vr.menuGate", GroupKey = "vr", Label = VPBTranslation.T("settings.gallery.vam_menu_gate", "Show only when VaM menu is visible"),
                Tooltip = VPBTranslation.T("settings.tip.gallery.vam_menu_gate", "Hide gallery panes automatically when VaM menu is closed."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible,
                SetBool = v => { VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.anchor", GroupKey = "vr", Label = VPBTranslation.T("settings.gallery.vam_menu_anchor", "Anchor to VaM Menu in VR"),
                Tooltip = VPBTranslation.T("settings.tip.gallery.vam_menu_anchor", "Anchor pane relative to VaM menu in VR."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryAnchorToVamMenu,
                SetBool = v => { VPBConfig.Instance.GalleryAnchorToVamMenu = v; VPBConfig.Instance.TriggerChange(); ResetFollowOffsets(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "quick.categoryEditor",
                GroupKey = "categories",
                SubGroupKey = "options",
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
                    SubGroupKey = "visibility",
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
                        InvalidateInternalSettingsDefsCache();
                        UpdateTabs();
                        if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
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
                        InvalidateInternalSettingsDefsCache();
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
                            InvalidateInternalSettingsDefsCache();
                            RefreshInternalSettingsListRows(true);
                        }
                    });
                }
            }

            // ── Auto-Updater ──
            var updater = VamHookPlugin.singleton != null ? VamHookPlugin.singleton.Updater : null;
            if (updater != null)
            {
                defs.Add(new InternalSettingDefinition
                {
                    Key = "updater.check",
                    GroupKey = "updater",
                    Label = GetUpdaterCheckLabel(updater),
                    Tooltip = VPBTranslation.T("settings.tip.updater.check", "Check for VPB updates from GitHub and stage files for next restart."),
                    ControlType = InternalSettingControlType.Button,
                    OnAction = (updater.HasPendingUpdate || updater.IsBusy) ? (Action)null : () =>
                    {
                        updater.CheckForUpdateAsync();
                        InvalidateInternalSettingsDefsCache();
                        RefreshInternalSettingsListRows(true);
                    }
                });
                defs.Add(new InternalSettingDefinition
                {
                    Key = "updater.auto",
                    GroupKey = "updater",
                    Label = VPBTranslation.T("settings.updater.auto_check", "Auto-check on startup"),
                    Tooltip = VPBTranslation.T("settings.tip.updater.auto", "Automatically check for updates each time VaM starts."),
                    ControlType = InternalSettingControlType.Toggle,
                    GetBool = () => updater.Config.AutoCheck,
                    SetBool = v => { updater.Config.AutoCheck = v; updater.Config.Save(); }
                });
                defs.Add(new InternalSettingDefinition
                {
                    Key = "updater.branch",
                    GroupKey = "updater",
                    Label = VPBTranslation.T("settings.updater.branch", "Update branch"),
                    Tooltip = VPBTranslation.T("settings.tip.updater.branch", "GitHub branch to pull updates from (e.g. main, dev)."),
                    ControlType = InternalSettingControlType.Cycle,
                    Options = updater.GetAvailableBranches(),
                    GetString = () => updater.Config.Branch ?? "main",
                    SetString = v => updater.SetBranch(v)
                });
                if (updater.HasPendingUpdate)
                {
                    defs.Add(new InternalSettingDefinition
                    {
                        Key = "updater.clear",
                        GroupKey = "updater",
                        Label = VPBTranslation.T("settings.updater.clear_staged", "Clear staged update"),
                        Tooltip = VPBTranslation.T("settings.tip.updater.clear", "Remove the pending update so it will not be applied on restart."),
                        ControlType = InternalSettingControlType.Button,
                        OnAction = () =>
                        {
                            updater.ClearStagedUpdate();
                            InvalidateInternalSettingsDefsCache();
                            RefreshInternalSettingsListRows(true);
                        }
                    });
                }
            }

            AppendGalleryPerfSettings(defs);
            AppendPluginInternalSettingDefinitions(defs);

            return defs;
        }

        private static string GetUpdaterCheckLabel(VpbUpdaterService updater)
        {
            if (updater.IsBusy)
                return updater.StatusMessage ?? VPBTranslation.T("settings.updater.checking", "Checking...");
            if (updater.HasPendingUpdate)
            {
                string av = updater.AvailableVersion ?? "?";
                return "Updating " + PluginVersionInfo.Version + " → " + av + "  (restart VaM)";
            }
            if (updater.Status == VpbUpdateStatus.UpToDate)
                return updater.StatusMessage ?? VPBTranslation.T("settings.updater.up_to_date", "Up to date");
            if (updater.Status == VpbUpdateStatus.Error)
                return updater.StatusMessage ?? VPBTranslation.T("settings.updater.error", "Update error");
            return VPBTranslation.T("settings.updater.check", "Check for Updates (VPB " + PluginVersionInfo.Version + ")");
        }

        private InternalSettingDefinition GetInternalSettingDefinition(string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey)) return null;
            GetInternalSettingDefinitionsCached();
            if (_internalSettingsDefsByKey != null && _internalSettingsDefsByKey.TryGetValue(rowKey, out var def))
                return def;
            return null;
        }

        private InternalSettingsSnapshot CreateInternalSettingsSnapshot()
        {
            var snap = new InternalSettingsSnapshot
            {
                DisableGalleryTransparency = VPBConfig.Instance.DisableGalleryTransparency,
                DisableGalleryPaneTransparency = VPBConfig.Instance.DisableGalleryPaneTransparency,
                DisableGalleryAssignableButtonsTransparency = VPBConfig.Instance.DisableGalleryAssignableButtonsTransparency,
                DisableGalleryDockHoverTransparency = VPBConfig.Instance.DisableGalleryDockHoverTransparency,
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
                GalleryCollapseOnSceneLaunch = VPBConfig.Instance.GalleryCollapseOnSceneLaunch,
                RequireDragHoldBeforeMove = VPBConfig.Instance.RequireDragHoldBeforeMove,
                DragHoldThreshold = VPBConfig.Instance.DragHoldThreshold,
                HoldToLaunchHoldSeconds = VPBConfig.Instance.HoldToLaunchHoldSeconds,
                AppearanceClothingApplyMode = VPBConfig.Instance.AppearanceClothingApplyMode,
                EnableAutoFixedGallery = VPBConfig.Instance.EnableAutoFixedGallery,
                InitialGalleryCategory = VPBConfig.Instance.InitialGalleryCategory,
                GalleryDefaultLeftSidePanel = VPBConfig.Instance.GalleryDefaultLeftSidePanel,
                GalleryDefaultRightSidePanel = VPBConfig.Instance.GalleryDefaultRightSidePanel,
                GalleryDefaultUserTagAvailMode = VPBConfig.Instance.GalleryDefaultUserTagAvailMode,
                GalleryHideUnusedUserTagsInFilterMode = VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode,
                GalleryUserTagFilterCombineMode = VPBConfig.Instance.GalleryUserTagFilterCombineMode,
                GalleryScrollButtonStepViewportFraction = VPBConfig.Instance.GalleryScrollButtonStepViewportFraction,
                GalleryScrollButtonsEnabled = VPBConfig.Instance.GalleryScrollButtonsEnabled,
                GalleryVrThumbstickScrollEnabled = VPBConfig.Instance.GalleryVrThumbstickScrollEnabled,
                GalleryHideCreatorSideButtons = VPBConfig.Instance.GalleryHideCreatorSideButtons,
                GalleryConsolidateCreatorNames = VPBConfig.Instance.GalleryConsolidateCreatorNames,
                PluginGalleryGridThumbnails = VPBConfig.Instance.PluginGalleryGridThumbnails,
                PluginGalleryCategoryLabelsOnly = VPBConfig.Instance.PluginGalleryCategoryLabelsOnly,
                GalleryThumbPlaceholderLabelsEnabled = VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled,
                GalleryThumbPlaceholderSizeScale = VPBConfig.Instance.GetGalleryThumbPlaceholderSizeScale(),
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
                GalleryScanWlBorderEnabled = VPBConfig.Instance.GalleryScanWlBorderEnabled,
                GalleryScanWlBorderShowInGrid = VPBConfig.Instance.GalleryScanWlBorderShowInGrid,
                GalleryScanWlBorderShowInList = VPBConfig.Instance.GalleryScanWlBorderShowInList,
                GalleryScanWlBorderWidth = VPBConfig.Instance.GalleryScanWlBorderWidth,
                GalleryScanWlGridFrameInset = VPBConfig.Instance.GalleryScanWlGridFrameInset,
                GalleryScanWlListFrameInset = VPBConfig.Instance.GalleryScanWlListFrameInset,
                GalleryScanWlBorderOnThumbnail = VPBConfig.Instance.GalleryScanWlBorderOnThumbnail,
                GalleryScanWlBorderColorR = VPBConfig.Instance.GalleryScanWlBorderColorR,
                GalleryScanWlBorderColorG = VPBConfig.Instance.GalleryScanWlBorderColorG,
                GalleryScanWlBorderColorB = VPBConfig.Instance.GalleryScanWlBorderColorB,
                GalleryScanWlBorderColorA = VPBConfig.Instance.GalleryScanWlBorderColorA,
                GalleryScanWlTempBorderEnabled = VPBConfig.Instance.GalleryScanWlTempBorderEnabled,
                GalleryScanWlTempBorderShowInGrid = VPBConfig.Instance.GalleryScanWlTempBorderShowInGrid,
                GalleryScanWlTempBorderShowInList = VPBConfig.Instance.GalleryScanWlTempBorderShowInList,
                GalleryScanWlTempBorderWidth = VPBConfig.Instance.GalleryScanWlTempBorderWidth,
                GalleryScanWlTempGridFrameInset = VPBConfig.Instance.GalleryScanWlTempGridFrameInset,
                GalleryScanWlTempListFrameInset = VPBConfig.Instance.GalleryScanWlTempListFrameInset,
                GalleryScanWlTempBorderOnThumbnail = VPBConfig.Instance.GalleryScanWlTempBorderOnThumbnail,
                GalleryScanWlTempBorderColorR = VPBConfig.Instance.GalleryScanWlTempBorderColorR,
                GalleryScanWlTempBorderColorG = VPBConfig.Instance.GalleryScanWlTempBorderColorG,
                GalleryScanWlTempBorderColorB = VPBConfig.Instance.GalleryScanWlTempBorderColorB,
                GalleryScanWlTempBorderColorA = VPBConfig.Instance.GalleryScanWlTempBorderColorA,
                GalleryOnlyWhenVamMenuVisible = VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible,
                GalleryAnchorToVamMenu = VPBConfig.Instance.GalleryAnchorToVamMenu,
                GalleryCategoryQuickOrder = VPBConfig.Instance.GalleryCategoryQuickOrder ?? "",
                GalleryCategoryQuickSwitchHidden = VPBConfig.Instance.GalleryCategoryQuickSwitchHidden ?? "",
                HiddenCategories = VPBConfig.Instance.HiddenCategories != null
                    ? new HashSet<string>(VPBConfig.Instance.HiddenCategories, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };
            CapturePluginSettingsIntoSnapshot(snap);
            return snap;
        }

        private void EnsureInternalSettingsSession()
        {
            if (internalSettingsSessionActive) return;
            internalSettingsListRowHeightSession = 80f;
            internalSettingsPreSessionLayoutMode = layoutMode;
            internalSettingsPreSessionScrollNormalized = (scrollRect != null) ? scrollRect.verticalNormalizedPosition : 1f;
            internalSettingsHadPreSessionViewState = true;
            internalSettingsBackup = CreateInternalSettingsSnapshot();
            PluginSettingsBeginSession();
            internalSettingsSessionActive = true;
        }

        public void NotifyUpdaterStatusChanged()
        {
            InvalidateInternalSettingsDefsCache();
            try { FooterPluginInfoRefreshChrome(); } catch { }
            if (_footerPluginInfoHovering)
            {
                _footerPluginInfoTooltipKey = int.MinValue;
                try { FooterPluginInfoPollHoverTooltip(); } catch { }
            }
            if (IsSettingsPanelOpen())
                RefreshInternalSettingsListRows(true);
        }

        /// <summary>Open gallery Settings on a specific category tab (e.g. updater).</summary>
        public void OpenSettingsGroup(string groupKey)
        {
            if (string.IsNullOrEmpty(groupKey))
                groupKey = "all";
            currentSettingsGroup = groupKey;
            if (string.Equals(groupKey, "categories", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(currentSettingsCategoriesSubGroup)
                    || (!string.Equals(currentSettingsCategoriesSubGroup, "options", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(currentSettingsCategoriesSubGroup, "visibility", StringComparison.OrdinalIgnoreCase)))
                    currentSettingsCategoriesSubGroup = "options";
            }
            try { CancelPluginHotkeyCapture(false); } catch { }
            if (!IsSettingsPanelOpen())
            {
                if (isFixedLocally)
                    ToggleLeft(ContentType.Settings);
                else
                    ToggleRight(ContentType.Settings);
            }
            else
            {
                try { UpdateTabs(); } catch { }
                RefreshInternalSettingsListRows(true);
            }
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
                InvalidateInternalSettingsDefsCache();
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

        private void RefreshGalleryScanWlBorderVisuals()
        {
            try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
        }

        private void RefreshInternalSettingsListRows(bool keepScroll = false)
        {
            if (!IsSettingsPanelOpen()) return;
            if (refreshCoroutine != null)
            {
                try { StopCoroutine(refreshCoroutine); } catch { }
                refreshCoroutine = null;
            }
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

            if (titleText != null)
                titleText.text = VPBTranslation.T("settings.title", "Settings");

            List<FileEntry> rows = BuildInternalSettingsRows();
            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(rows);
            selectedFiles.Clear();
            selectedFilePaths.Clear();

            RecyclingGridView rgv = recyclingGrid;
            if (rgv == null && contentGO != null)
            {
                try { rgv = contentGO.GetComponent<RecyclingGridView>(); } catch { }
            }

            if (rgv != null)
                rgv.SetItemCount(currentFilteredFiles.Count, deferRefresh: true);

            if (layoutMode != GalleryLayoutMode.List)
                SetLayoutMode(GalleryLayoutMode.List, false, true);

            try { ApplyInternalSettingsListGridConfig(rgv, deferRefresh: true); } catch { }

            if (rgv != null)
            {
                if (!keepScroll) ScrollGalleryToTop();
                rgv.Refresh();
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
            bool SubGroupAllowed(InternalSettingDefinition def)
            {
                if (def == null) return true;
                if (!string.Equals(def.GroupKey, "categories", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(currentSettingsGroup, "all", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.Equals(currentSettingsGroup, "categories", StringComparison.OrdinalIgnoreCase)) return true;
                string sub = string.IsNullOrEmpty(def.SubGroupKey) ? "options" : def.SubGroupKey;
                return string.Equals(currentSettingsCategoriesSubGroup, sub, StringComparison.OrdinalIgnoreCase);
            }
            bool FilterAllowed(string label) =>
                string.IsNullOrEmpty(f) || (label ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
            void Add(InternalSettingDefinition def)
            {
                if (def == null) return;
                string key = def.Key;
                string group = def.GroupKey;
                string label = def.Label;
                if (!GroupAllowed(group)) return;
                if (!SubGroupAllowed(def)) return;
                if (!FilterAllowed(label)) return;
                try
                {
                    if (def.RowVisible != null && !def.RowVisible()) return;
                }
                catch { }
                rows.Add(new InternalSettingRowEntry(key, group, label));
            }

            var defs = GetInternalSettingDefinitionsCached();
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
            if (def.ControlType == InternalSettingControlType.Hotkey) return false;
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
            DestroyChildrenByName(detailsTr, "SettingsHotkeyHost");

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

            if (def.ControlType == InternalSettingControlType.Hotkey)
            {
                RebuildPluginHotkeyRowControls(controls.transform, def, paneScale);
                return;
            }

            if (def.ControlType == InternalSettingControlType.Button)
            {
                if (def.OnAction == null
                    && string.Equals(def.Key, "plugin.scan_whitelist.empty_warn", StringComparison.OrdinalIgnoreCase))
                {
                    GameObject warn = new GameObject("SettingsWarningLabel");
                    warn.transform.SetParent(controls.transform, false);
                    LayoutElement wle = warn.AddComponent<LayoutElement>();
                    wle.flexibleWidth = 1f;
                    wle.preferredHeight = 32f * paneScale;
                    Text wt = warn.AddComponent<Text>();
                    wt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    wt.fontSize = Mathf.Max(10, Mathf.RoundToInt(14f * paneScale));
                    wt.color = new Color(1f, 0.75f, 0.2f, 1f);
                    wt.alignment = TextAnchor.MiddleRight;
                    wt.supportRichText = false;
                    wt.text = def.Label ?? "";
                    try { VPBUiFont.ApplyTo(wt); } catch { }
                    return;
                }
                if (def.OnAction != null)
                {
                    string btnLabel = VPBTranslation.T("settings.row.action", "CLICK");
                    if (string.Equals(def.Key, "plugin.scan_whitelist.manage", StringComparison.OrdinalIgnoreCase))
                        btnLabel = VPBTranslation.T("settings.row.manage", "MANAGE");
                    else if (string.Equals(def.Key, "plugin.qm_positions", StringComparison.OrdinalIgnoreCase))
                        btnLabel = VPBTranslation.T("settings.row.adjust", "ADJUST");
                    else if (string.Equals(def.Key, "plugin.bench.configure", StringComparison.OrdinalIgnoreCase))
                        btnLabel = VPBTranslation.T("settings.row.configure", "CONFIGURE");
                    CreateMiniButton(controls.transform, btnLabel, 150f, new Color(0.7f, 0.4f, 0.2f, 1f), () => {
                        def.OnAction?.Invoke();
                        RefreshInternalSettingsListRows(true);
                    });
                }
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
            if (!TryCommitPluginSettingsOnSave())
                return;
            internalSettingsBackup = CreateInternalSettingsSnapshot();
            try { VPBConfig.Instance.Save(false); } catch { }
            VPBConfig.Instance.TriggerChange();
            try { Settings.SaveConfig(); } catch { }
            try { SetHoverPreviewDummyActive(false); } catch { }
            PluginSettingsEndSession();
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

            try { ApplySidePanelDefaultsFromConfig(); } catch { }

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
            VPBConfig.Instance.DisableGalleryTransparency = b.DisableGalleryTransparency;
            VPBConfig.Instance.DisableGalleryPaneTransparency = b.DisableGalleryPaneTransparency;
            VPBConfig.Instance.DisableGalleryAssignableButtonsTransparency = b.DisableGalleryAssignableButtonsTransparency;
            VPBConfig.Instance.DisableGalleryDockHoverTransparency = b.DisableGalleryDockHoverTransparency;
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
            VPBConfig.Instance.GalleryCollapseOnSceneLaunch = b.GalleryCollapseOnSceneLaunch;
            VPBConfig.Instance.RequireDragHoldBeforeMove = b.RequireDragHoldBeforeMove;
            VPBConfig.Instance.DragHoldThreshold = b.DragHoldThreshold;
            VPBConfig.Instance.HoldToLaunchHoldSeconds = b.HoldToLaunchHoldSeconds;
            VPBConfig.Instance.AppearanceClothingApplyMode = b.AppearanceClothingApplyMode;
            VPBConfig.Instance.EnableAutoFixedGallery = b.EnableAutoFixedGallery;
            VPBConfig.Instance.InitialGalleryCategory = b.InitialGalleryCategory;
            VPBConfig.Instance.GalleryDefaultLeftSidePanel = b.GalleryDefaultLeftSidePanel;
            VPBConfig.Instance.GalleryDefaultRightSidePanel = b.GalleryDefaultRightSidePanel;
            VPBConfig.Instance.GalleryDefaultUserTagAvailMode = b.GalleryDefaultUserTagAvailMode;
            VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode = b.GalleryHideUnusedUserTagsInFilterMode;
            VPBConfig.Instance.GalleryUserTagFilterCombineMode = b.GalleryUserTagFilterCombineMode;
            VPBConfig.Instance.GalleryScrollButtonStepViewportFraction = b.GalleryScrollButtonStepViewportFraction;
            VPBConfig.Instance.GalleryScrollButtonsEnabled = b.GalleryScrollButtonsEnabled;
            VPBConfig.Instance.GalleryVrThumbstickScrollEnabled = b.GalleryVrThumbstickScrollEnabled;
            VPBConfig.Instance.GalleryHideCreatorSideButtons = b.GalleryHideCreatorSideButtons;
            VPBConfig.Instance.GalleryConsolidateCreatorNames = b.GalleryConsolidateCreatorNames;
            VPBConfig.Instance.PluginGalleryGridThumbnails = b.PluginGalleryGridThumbnails;
            VPBConfig.Instance.PluginGalleryCategoryLabelsOnly = b.PluginGalleryCategoryLabelsOnly;
            VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled = b.GalleryThumbPlaceholderLabelsEnabled;
            VPBConfig.Instance.GalleryThumbPlaceholderSizeScale = VPBConfig.ClampGalleryThumbPlaceholderSizeScale(b.GalleryThumbPlaceholderSizeScale);
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
            VPBConfig.Instance.GalleryScanWlBorderEnabled = b.GalleryScanWlBorderEnabled;
            VPBConfig.Instance.GalleryScanWlBorderShowInGrid = b.GalleryScanWlBorderShowInGrid;
            VPBConfig.Instance.GalleryScanWlBorderShowInList = b.GalleryScanWlBorderShowInList;
            VPBConfig.Instance.GalleryScanWlBorderWidth = b.GalleryScanWlBorderWidth;
            VPBConfig.Instance.GalleryScanWlGridFrameInset = b.GalleryScanWlGridFrameInset;
            VPBConfig.Instance.GalleryScanWlListFrameInset = b.GalleryScanWlListFrameInset;
            VPBConfig.Instance.GalleryScanWlBorderOnThumbnail = b.GalleryScanWlBorderOnThumbnail;
            VPBConfig.Instance.GalleryScanWlBorderColorR = b.GalleryScanWlBorderColorR;
            VPBConfig.Instance.GalleryScanWlBorderColorG = b.GalleryScanWlBorderColorG;
            VPBConfig.Instance.GalleryScanWlBorderColorB = b.GalleryScanWlBorderColorB;
            VPBConfig.Instance.GalleryScanWlBorderColorA = b.GalleryScanWlBorderColorA;
            VPBConfig.Instance.GalleryScanWlTempBorderEnabled = b.GalleryScanWlTempBorderEnabled;
            VPBConfig.Instance.GalleryScanWlTempBorderShowInGrid = b.GalleryScanWlTempBorderShowInGrid;
            VPBConfig.Instance.GalleryScanWlTempBorderShowInList = b.GalleryScanWlTempBorderShowInList;
            VPBConfig.Instance.GalleryScanWlTempBorderWidth = b.GalleryScanWlTempBorderWidth;
            VPBConfig.Instance.GalleryScanWlTempGridFrameInset = b.GalleryScanWlTempGridFrameInset;
            VPBConfig.Instance.GalleryScanWlTempListFrameInset = b.GalleryScanWlTempListFrameInset;
            VPBConfig.Instance.GalleryScanWlTempBorderOnThumbnail = b.GalleryScanWlTempBorderOnThumbnail;
            VPBConfig.Instance.GalleryScanWlTempBorderColorR = b.GalleryScanWlTempBorderColorR;
            VPBConfig.Instance.GalleryScanWlTempBorderColorG = b.GalleryScanWlTempBorderColorG;
            VPBConfig.Instance.GalleryScanWlTempBorderColorB = b.GalleryScanWlTempBorderColorB;
            VPBConfig.Instance.GalleryScanWlTempBorderColorA = b.GalleryScanWlTempBorderColorA;
            VPBConfig.Instance.GalleryOnlyWhenVamMenuVisible = b.GalleryOnlyWhenVamMenuVisible;
            VPBConfig.Instance.GalleryAnchorToVamMenu = b.GalleryAnchorToVamMenu;
            VPBConfig.Instance.GalleryCategoryQuickOrder = b.GalleryCategoryQuickOrder ?? "";
            VPBConfig.Instance.GalleryCategoryQuickSwitchHidden = b.GalleryCategoryQuickSwitchHidden ?? "";
            VPBConfig.Instance.HiddenCategories = b.HiddenCategories != null
                ? new HashSet<string>(b.HiddenCategories, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            RestorePluginSettingsFromSnapshot(b);

            if (this != null)
            {
                ApplySideButtonScale();
                categoriesCached = false;
                RebuildGridLayout();
                RefreshFiles(true);
            }
            ApplyGalleryTransparencyToAllPanels();
            VPBConfig.Instance.TriggerChange();
            PluginSettingsEndSession();
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

        // Canonical token <-> UI cycle label for the GallerySearchScope setting; keeps storage stable while letting localization tweak the label.
        private static string GallerySearchScopeToLabel(string canonical)
        {
            if (canonical == "NameOnly") return "Name only";
            if (canonical == "NameStartsWith") return "Name starts with";
            return "Path + Name";
        }

        private static string GallerySearchScopeFromLabel(string label)
        {
            if (string.Equals(label, "Name only", StringComparison.OrdinalIgnoreCase)) return "NameOnly";
            if (string.Equals(label, "Name starts with", StringComparison.OrdinalIgnoreCase)) return "NameStartsWith";
            return "PathAndName";
        }
    }
}
