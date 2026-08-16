using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const string SettingsRowResetIconPath = "arrow-back-up";

        private static VPBConfig s_settingsFactoryDefaults;

        private static VPBConfig SettingsFactoryDefaults
        {
            get
            {
                if (s_settingsFactoryDefaults == null)
                    s_settingsFactoryDefaults = new VPBConfig();
                return s_settingsFactoryDefaults;
            }
        }

        /// <summary>Warm path: bind factory defaults once per defs-cache rebuild. Button/action rows stay without Reset.</summary>
        private void BindSettingsRowFactoryDefaults(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;
            VPBConfig fd = SettingsFactoryDefaults;
            for (int i = 0; i < defs.Count; i++)
            {
                InternalSettingDefinition d = defs[i];
                if (d == null || string.IsNullOrEmpty(d.Key)) continue;
                if (d.ControlType == InternalSettingControlType.Button) continue;
                if (string.Equals(d.Key, "quick.categoryEditor", StringComparison.OrdinalIgnoreCase)) continue;

                if (d.Key.StartsWith("categories.show.", StringComparison.Ordinal))
                {
                    d.SetDefault(true);
                    continue;
                }

                switch (d.Key)
                {
                    case "visuals.disableTransparency": d.SetDefault(fd.DisableGalleryTransparency); break;
                    case "visuals.disablePaneTransparency": d.SetDefault(fd.DisableGalleryPaneTransparency); break;
                    case "visuals.disableAssignableTransparency": d.SetDefault(fd.DisableGalleryAssignableButtonsTransparency); break;
                    case "visuals.disableDockHoverTransparency": d.SetDefault(fd.DisableGalleryDockHoverTransparency); break;
                    case "visuals.idleTransparency": d.SetDefault(fd.EnableGalleryTranslucency); break;
                    case "visuals.idleOpacity": d.SetDefault(fd.GalleryOpacity); break;
                    case "visuals.fade": d.SetDefault(fd.EnableGalleryFade); break;
                    case "visuals.manualRefresh": d.SetDefault(fd.GalleryManualRefreshOnly); break;
                    case "visuals.detailStripSideInfo": d.SetDefault(fd.GalleryDetailStripSideInfoEnabled); break;
                    case "visuals.detailStripThumbSide": d.SetDefault(fd.GalleryDetailStripThumbOnRight ? "Right" : "Left"); break;
                    case "visuals.galleryUiScaleVr": d.SetDefault(fd.InnerPaneScaleVR); break;
                    case "visuals.galleryUiScaleDesktop": d.SetDefault(fd.InnerPaneScaleDesktop); break;
                    case "visuals.sideGaps": d.SetDefault(fd.EnableButtonGaps); break;
                    case "visuals.elementRounding": d.SetDefault(fd.EnableGalleryElementRounding); break;
                    case "visuals.buttonChromeRims": d.SetDefault(fd.EnableGalleryButtonChromeRims); break;
                    case "visuals.elementCornerRadius": d.SetDefault(fd.GalleryElementCornerRadiusFraction); break;
                    case "visuals.showSideButtons": d.SetDefault(fd.ShowSideButtons); break;

                    case "follow.angle": d.SetDefault(fd.FollowAngle); break;
                    case "follow.eyeHeight": d.SetDefault(fd.FollowEyeHeight); break;
                    case "follow.distance": d.SetDefault(fd.FollowDistance); break;
                    case "follow.reorient": d.SetDefault(fd.ReorientStartAngle); break;
                    case "follow.moveThreshold": d.SetDefault(fd.MovementThreshold); break;
                    case "follow.bringFront": d.SetDefault(fd.BringToFrontDistance); break;

                    case "interaction.dragDrop": d.SetDefault(fd.EnableDragDrop); break;
                    case "interaction.autoGenderFilter": d.SetDefault(fd.GalleryAutoGenderFilter); break;
                    case "interaction.collapseOnSceneLaunch": d.SetDefault(fd.GalleryCollapseOnSceneLaunch); break;
                    case "interaction.tryOnMode": d.SetDefault(fd.TryOnModeEnabled); break;
                    case "interaction.verticalMoveKeys": d.SetDefault(fd.VerticalMoveKeysEnabled); break;
                    case "interaction.dragHoldSec": d.SetDefault(fd.DragHoldThreshold); break;
                    case "interaction.holdToLaunchSec": d.SetDefault(fd.HoldToLaunchHoldSeconds); break;
                    case "interaction.appearanceClothing": d.SetDefault(fd.AppearanceClothingApplyMode); break;

                    case "desktop.startFixed": d.SetDefault(fd.EnableAutoFixedGallery); break;
                    case "desktop.fixedAutoHideSeconds": d.SetDefault(fd.DesktopFixedAutoHideSeconds); break;
                    case "desktop.fixedDefaultDock": d.SetDefault(VPBConfig.NormalizeDesktopFixedDockSide(fd.DesktopFixedDefaultDockSide)); break;
                    case "desktop.fixedEnforceDockEnabled": d.SetDefault(fd.DesktopFixedEnforceDockSide); break;
                    case "desktop.fixedEnforceDockSide": d.SetDefault(VPBConfig.NormalizeDesktopFixedDockSide(fd.DesktopFixedEnforcedDockSide)); break;
                    case "desktop.initialCategory": d.SetDefault(VPBConfig.NormalizeInitialGalleryCategory(fd.InitialGalleryCategory)); break;

                    case "lists.defaultLeft": d.SetDefault(VPBConfig.NormalizeGallerySidePanel(fd.GalleryDefaultLeftSidePanel)); break;
                    case "lists.defaultRight": d.SetDefault(VPBConfig.NormalizeGallerySidePanel(fd.GalleryDefaultRightSidePanel)); break;
                    case "lists.scrollButtons": d.SetDefault(fd.GalleryScrollButtonsEnabled); break;
                    case "lists.springScrollButton": d.SetDefault(VPBConfig.NormalizeSpringScrollButtonMode(fd.SpringScrollButtonMode)); break;
                    case "lists.vrThumbstickScroll": d.SetDefault(fd.GalleryVrThumbstickScrollEnabled); break;
                    case "lists.scrollStep": d.SetDefault(fd.GalleryScrollButtonStepViewportFraction); break;
                    case "lists.hideCreatorSideButtons": d.SetDefault(fd.GalleryHideCreatorSideButtons); break;
                    case "lists.showCategoryIcons": d.SetDefault(fd.GalleryShowCategoryIcons); break;
                    case "lists.pluginThumbs": d.SetDefault(fd.PluginGalleryGridThumbnails); break;
                    case "lists.pluginLabelsOnly": d.SetDefault(fd.PluginGalleryCategoryLabelsOnly); break;
                    case "lists.pluginConsolidateCslist": d.SetDefault(true); break;
                    case "lists.legacyNames": d.SetDefault(fd.GalleryListNamesLegacyFileName); break;

                    case "tags.defaultAction": d.SetDefault(VPBConfig.FormatGalleryDefaultUserTagAvailModeForSettings(fd.GalleryDefaultUserTagAvailMode)); break;
                    case "tags.hideUnusedInFilterMode": d.SetDefault(fd.GalleryHideUnusedUserTagsInFilterMode); break;
                    case "tags.filterCombineMode": d.SetDefault(VPBConfig.NormalizeGalleryUserTagFilterCombineMode(fd.GalleryUserTagFilterCombineMode)); break;

                    case "helpers.consolidateCreatorNames": d.SetDefault(fd.GalleryConsolidateCreatorNames); break;
                    case "helpers.hairSwapKeepVisible": d.SetDefault(true); break;
                    case "helpers.returnToSceneViewOnStartup": d.SetDefault(false); break;
                    case "helpers.blockInGameMessages": d.SetDefault(fd.BlockInGameMessages ?? "Off"); break;
                    case "helpers.hideMissingDependencyLogs": d.SetDefault(fd.HideMissingDependencyLogs); break;
                    case "helpers.clearInGameLogsOnSceneLaunch": d.SetDefault(fd.ClearInGameLogsOnSceneLaunch); break;

                    case "grid.prettyPresetNames": d.SetDefault(fd.GalleryPrettyPresetNames); break;
                    case "grid.enabled": d.SetDefault(fd.GalleryGridLabelsEnabled); break;
                    case "grid.hoverBadges": d.SetDefault(fd.GalleryGridHoverBadgesEnabled); break;
                    case "grid.autoHideHighDensity": d.SetDefault(fd.GalleryGridLabelsAutoHideAtHighDensity); break;
                    case "grid.font": d.SetDefault(fd.GalleryGridLabelFontSize); break;
                    case "grid.thumbPlaceholder": d.SetDefault(fd.GalleryThumbPlaceholderLabelsEnabled); break;
                    case "grid.thumbPlaceholderScale": d.SetDefault(fd.GetGalleryThumbPlaceholderSizeScale()); break;
                    case "grid.spacingX": d.SetDefault(fd.GalleryGridSpacingX); break;
                    case "grid.spacingY": d.SetDefault(fd.GalleryGridSpacingY); break;
                    case "grid.thumbPad": d.SetDefault(fd.GalleryGridThumbnailPadding); break;
                    case "grid.hoverBorder": d.SetDefault(fd.GalleryGridHoverBorderWidth); break;
                    case "grid.selBorder": d.SetDefault(fd.GalleryGridSelectedBorderWidth); break;
                    case "grid.inwardSquare": d.SetDefault(fd.GalleryGridBorderInwardWhenSquare); break;
                    case "grid.borderColor": d.SetDefault(fd.GetGalleryGridBorderColor()); break;

                    case "search.scope": d.SetDefault(GallerySearchScopeToLabel(VPBConfig.NormalizeGallerySearchScope(fd.GallerySearchScope))); break;
                    case "search.sourceFilterScope": d.SetDefault(VPBConfig.FormatGallerySourceFilterScopeForSettings(fd.GallerySourceFilterIndependent)); break;

                    case "hover.mode": d.SetDefault(VPBConfig.NormalizeHoverPreviewMode(fd.GalleryHoverPreviewMode)); break;
                    case "hover.randomButtonPreview": d.SetDefault(fd.QuickMenuRandomHoverPreview); break;
                    case "hover.size": d.SetDefault(fd.GalleryListHoverPreviewSize); break;
                    case "hover.offsetX": d.SetDefault(fd.GalleryListHoverPreviewOffsetX); break;
                    case "hover.offsetY": d.SetDefault(fd.GalleryListHoverPreviewOffsetY); break;

                    case "scanWlBorder.enabled": d.SetDefault(fd.GalleryScanWlBorderEnabled); break;
                    case "scanWlBorder.showGrid": d.SetDefault(fd.GalleryScanWlBorderShowInGrid); break;
                    case "scanWlBorder.showList": d.SetDefault(fd.GalleryScanWlBorderShowInList); break;
                    case "scanWlBorder.width": d.SetDefault(fd.GalleryScanWlBorderWidth); break;
                    case "scanWlBorder.color": d.SetDefault(fd.GetGalleryScanWlBorderColor()); break;
                    case "scanWlBorder.gridInset": d.SetDefault(fd.GalleryScanWlGridFrameInset); break;
                    case "scanWlBorder.listInset": d.SetDefault(fd.GalleryScanWlListFrameInset); break;
                    case "scanWlBorder.onThumbnail": d.SetDefault(fd.GalleryScanWlBorderOnThumbnail); break;

                    case "scanWlTempBorder.enabled": d.SetDefault(fd.GalleryScanWlTempBorderEnabled); break;
                    case "scanWlTempBorder.showGrid": d.SetDefault(fd.GalleryScanWlTempBorderShowInGrid); break;
                    case "scanWlTempBorder.showList": d.SetDefault(fd.GalleryScanWlTempBorderShowInList); break;
                    case "scanWlTempBorder.width": d.SetDefault(fd.GalleryScanWlTempBorderWidth); break;
                    case "scanWlTempBorder.color": d.SetDefault(fd.GetGalleryScanWlTempBorderColor()); break;
                    case "scanWlTempBorder.gridInset": d.SetDefault(fd.GalleryScanWlTempGridFrameInset); break;
                    case "scanWlTempBorder.listInset": d.SetDefault(fd.GalleryScanWlTempListFrameInset); break;
                    case "scanWlTempBorder.onThumbnail": d.SetDefault(fd.GalleryScanWlTempBorderOnThumbnail); break;

                    case "vr.menuGate": d.SetDefault(fd.GalleryOnlyWhenVamMenuVisible); break;
                    case "vr.anchor": d.SetDefault(fd.GalleryAnchorToVamMenu); break;
                    case "vr.anchorTilt": d.SetDefault(VPBConfig.ClampGalleryVrMenuAnchorTiltDeg(fd.GalleryVrMenuAnchorTiltDeg)); break;
                    case "vr.watchVisible": d.SetDefault(fd.QuickMenuVrWatchVisible); break;
                    case "vr.watchMode": d.SetDefault(fd.QuickMenuVrWatchMode); break;
                    case "vr.watchShowWhen": d.SetDefault(fd.QuickMenuVrWatchShowWhen); break;
                    case "vr.watchRememberHand": d.SetDefault(fd.QuickMenuVrWatchRememberHand); break;
                    case "vr.watchFaceUser": d.SetDefault(fd.QuickMenuVrWatchFaceUser); break;
                    case "vr.watchRotX": d.SetDefault(VPBConfig.QuickMenuVrWatchFaceRotationDefault.x); break;
                    case "vr.watchRotY": d.SetDefault(VPBConfig.QuickMenuVrWatchFaceRotationDefault.y); break;
                    case "vr.watchRotZ": d.SetDefault(VPBConfig.QuickMenuVrWatchFaceRotationDefault.z); break;
                    case "vr.watchScale": d.SetDefault(VPBConfig.QuickMenuVrWatchScaleMulDefault); break;
                    case "vr.watchExpanded": d.SetDefault(fd.QuickMenuVrWatchExpanded); break;
                    case "vr.watchLabels": d.SetDefault(fd.QuickMenuVrWatchLabels); break;
                    case "vr.watchFreeze": d.SetDefault(fd.QuickMenuVrWatchFreezeOnApproach); break;
                    case "vr.watchGripPin": d.SetDefault(fd.QuickMenuVrWatchGripPin); break;
                    case "vr.watchHoldConfirm": d.SetDefault(fd.QuickMenuVrWatchHoldConfirm); break;
                    case "vr.watchShoulder": d.SetDefault(VPBConfig.QuickMenuVrWatchShoulderBlendDefault); break;
                    case "vr.watchGlanceDwell": d.SetDefault(VPBConfig.QuickMenuVrWatchGlanceDwellDefault); break;
                    case "vr.watchToward": d.SetDefault(VPBConfig.QuickMenuVrWatchTowardDefault); break;
                    case "vr.watchOffsetX": d.SetDefault(VPBConfig.QuickMenuVrWatchOffsetDefault.x); break;
                    case "vr.watchOffsetY": d.SetDefault(VPBConfig.QuickMenuVrWatchOffsetDefault.y); break;
                    case "vr.watchOffsetZ": d.SetDefault(VPBConfig.QuickMenuVrWatchOffsetDefault.z); break;

                    case "performance.applyHair": d.SetDefault(fd.PerfApplyHair); break;
                    case "performance.applyMirrors": d.SetDefault(fd.PerfApplyMirrors); break;
                    case "performance.applyPixelLights": d.SetDefault(fd.PerfApplyPixelLightCount); break;
                    case "performance.applyMsaa": d.SetDefault(fd.PerfApplyMsaa); break;
                    case "performance.applyRenderScale": d.SetDefault(fd.PerfApplyRenderScale); break;
                    case "performance.applySmoothPasses": d.SetDefault(fd.PerfApplySmoothPasses); break;
                    case "performance.applyMirrorReflections": d.SetDefault(fd.PerfApplyMirrorReflections); break;
                    case "performance.applyReflectionProbes": d.SetDefault(fd.PerfApplyRealtimeReflectionProbes); break;
                    case "performance.applySoftPhysics": d.SetDefault(fd.PerfApplySoftPhysics); break;
                    case "performance.applyGlow": d.SetDefault(fd.PerfApplyGlowEffects); break;
                    case "performance.sceneAtomCacheLimit":
                        d.SetDefault(VPBConfig.ClampSceneImportCacheLimitMb(fd.SceneImportCacheLimitMb) / 1024f);
                        break;

                    case "plugin.hotkey.gallery": d.SetDefault("Ctrl+G"); break;
                    case "plugin.hotkey.create_gallery": d.SetDefault("Ctrl+N"); break;
                    case "plugin.hotkey.hub": d.SetDefault("Ctrl+H"); break;
                    case "plugin.hotkey.clear_console": d.SetDefault("F2"); break;
                    case "plugin.zstd.downscale_8k": d.SetDefault(false); break;
                    case "plugin.scan_whitelist.enable": d.SetDefault(false); break;

                    case "updater.auto": d.SetDefault(false); break;
                    case "updater.branch": d.SetDefault("main"); break;
                }
            }
        }

        private bool SettingsRowIsAtDefault(InternalSettingDefinition def)
        {
            if (def == null || !def.HasDefault) return true;
            try
            {
                switch (def.ControlType)
                {
                    case InternalSettingControlType.Toggle:
                        return def.GetBool == null || def.GetBool() == def.DefaultBool;
                    case InternalSettingControlType.Slider:
                    {
                        if (def.GetFloat == null) return true;
                        float step = ResolveSettingsSliderStep(def);
                        float a = SnapSettingsSliderValue(def.GetFloat(), def.Min, def.Max, step, def.Decimals);
                        float b = SnapSettingsSliderValue(def.DefaultFloat, def.Min, def.Max, step, def.Decimals);
                        return Mathf.Abs(a - b) <= 1e-4f;
                    }
                    case InternalSettingControlType.Cycle:
                    case InternalSettingControlType.TextArea:
                    case InternalSettingControlType.Hotkey:
                        return string.Equals(def.GetString != null ? def.GetString() : null, def.DefaultString ?? "", StringComparison.OrdinalIgnoreCase);
                    case InternalSettingControlType.ColorRgb:
                    {
                        if (def.GetColor == null) return true;
                        Color c = def.GetColor();
                        Color d = def.DefaultColor;
                        return Mathf.Abs(c.r - d.r) < 0.004f
                            && Mathf.Abs(c.g - d.g) < 0.004f
                            && Mathf.Abs(c.b - d.b) < 0.004f
                            && Mathf.Abs(c.a - d.a) < 0.004f;
                    }
                    default:
                        return true;
                }
            }
            catch { return true; }
        }

        private static string FormatSettingsResetDefaultValue(InternalSettingDefinition def)
        {
            if (def == null) return "";
            switch (def.ControlType)
            {
                case InternalSettingControlType.Toggle:
                    return FormatSettingsToggleLabel(def.DefaultBool);
                case InternalSettingControlType.Slider:
                {
                    int decimals = def.Decimals < 0 ? 0 : def.Decimals;
                    return def.DefaultFloat.ToString("F" + decimals);
                }
                case InternalSettingControlType.Cycle:
                    return FormatSettingsCycleOption(def.DefaultString ?? "");
                case InternalSettingControlType.Hotkey:
                case InternalSettingControlType.TextArea:
                    return def.DefaultString ?? "";
                default:
                    return "";
            }
        }

        private static string FormatSettingsResetTooltip(InternalSettingDefinition def, bool atDefault)
        {
            string val = FormatSettingsResetDefaultValue(def);
            string baseTip = atDefault
                ? VPBTranslation.T("settings.row.reset.already", "Already at default")
                : VPBTranslation.T("settings.row.reset.tip", "Reset to default");
            if (string.IsNullOrEmpty(val)) return baseTip;
            return baseTip + ": " + val;
        }

        private void ApplySettingsRowFactoryDefault(InternalSettingDefinition def)
        {
            if (def == null || !def.HasDefault) return;
            if (SettingsRowIsAtDefault(def)) return;
            try { def.ApplyFactoryDefault(); }
            catch { return; }
            if (string.Equals(def.SubGroupKey, "hover", StringComparison.OrdinalIgnoreCase)
                || (def.Key != null && def.Key.StartsWith("hover.", StringComparison.Ordinal)))
            {
                try { NotifyInternalSettingsHoverPreviewChanged(); } catch { }
            }
            // UI-scale sliders: ApplyInnerPaneScale already rebuilds settings rows.
            bool uiScale = string.Equals(def.Key, "visuals.galleryUiScaleVr", StringComparison.OrdinalIgnoreCase)
                || string.Equals(def.Key, "visuals.galleryUiScaleDesktop", StringComparison.OrdinalIgnoreCase);
            if (uiScale)
            {
                try { ApplyInnerPaneScale(); } catch { }
                return;
            }
            RefreshInternalSettingsListRows(true);
        }

        private void AppendSettingsRowResetButton(Transform parent, InternalSettingDefinition def, float uiS)
        {
            if (parent == null || def == null || !def.HasDefault) return;

            bool atDefault = SettingsRowIsAtDefault(def);
            float btnW = GalleryUiDesignTokens.ButtonSizeRef;
            Color bg = atDefault ? GalleryUiColorTokens.SurfacePanel : GalleryUiColorTokens.SurfaceMid;
            GameObject go = CreateMiniButton(parent, "↺", btnW, bg, () => ApplySettingsRowFactoryDefault(def));
            if (go == null) return;
            go.name = "SettingsResetBtn";

            try { UI.ApplyBarIconFromPath(go, SettingsRowResetIconPath, 4f * uiS, bg); }
            catch { }

            Button b = go.GetComponent<Button>();
            if (b != null) b.interactable = !atDefault;

            if (atDefault)
            {
                try
                {
                    Transform iconTr = go.transform.Find("Icon");
                    Image iconImg = iconTr != null ? iconTr.GetComponent<Image>() : null;
                    if (iconImg != null)
                    {
                        Color c = iconImg.color;
                        c.a = 0.4f;
                        iconImg.color = c;
                    }
                    Text t = go.GetComponentInChildren<Text>(true);
                    if (t != null)
                    {
                        Color c = t.color;
                        c.a = 0.4f;
                        t.color = c;
                    }
                }
                catch { }
            }

            AddTooltipPlain(go, FormatSettingsResetTooltip(def, atDefault));
        }
    }
}
