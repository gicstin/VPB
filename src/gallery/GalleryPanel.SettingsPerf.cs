using System;
using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        static bool PerfSettingBool(Func<VPBConfig, bool> read, bool fallback)
        {
            try
            {
                var c = VPBConfig.Instance;
                return c == null ? fallback : read(c);
            }
            catch { return fallback; }
        }

        void AppendGalleryPerfSettings(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;

            void SetPerfBool(Action<bool> assign, bool value)
            {
                try
                {
                    if (VPBConfig.Instance == null) return;
                    assign(value);
                    VPBConfig.Instance.Save(false, false);
                    VpbPerfController.OnApplyTargetsChanged();
                }
                catch { }
            }

            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyHair",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_hair", "Apply hair density × multiplier"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_hair",
                    "When quality is on, adjust curve density and hair multiplier per quality level. Off = leave hair unchanged."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyHair, true),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyHair = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyMirrors",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_mirrors", "Apply mirror texture size"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_mirrors",
                    "When quality is on, set MirrorRender texture size per quality level. Off = leave mirrors unchanged."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyMirrors, true),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyMirrors = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyPixelLights",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_pixel_lights", "Pixel light count"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_pixel_lights", "UserPreferences.pixelLightCount per quality level."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyPixelLightCount, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyPixelLightCount = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyMsaa",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_msaa", "MSAA level"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_msaa", "UserPreferences.msaaLevel (0, 2, 4, 8) per quality level."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyMsaa, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyMsaa = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyRenderScale",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_render_scale", "Render scale"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_render_scale", "UserPreferences.renderScale per quality level."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyRenderScale, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyRenderScale = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applySmoothPasses",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_smooth_passes", "Smooth passes"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_smooth_passes", "UserPreferences.smoothPasses per quality level."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplySmoothPasses, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplySmoothPasses = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyMirrorReflections",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_mirror_reflections", "Mirror reflections (global)"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_mirror_reflections",
                    "UserPreferences.mirrorReflections — scene-wide mirror reflection toggle, not per-atom texture size."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyMirrorReflections, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyMirrorReflections = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyReflectionProbes",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_reflection_probes", "Realtime reflection probes"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_reflection_probes", "UserPreferences.realtimeReflectionProbes per quality level."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyRealtimeReflectionProbes, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyRealtimeReflectionProbes = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applySoftPhysics",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_soft_physics", "Soft body physics"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_soft_physics", "UserPreferences.softPhysics per quality level."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplySoftPhysics, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplySoftPhysics = b, v),
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "performance.applyGlow",
                GroupKey = "performance",
                Label = VPBTranslation.T("settings.perf.apply_glow", "Glow effects"),
                Tooltip = VPBTranslation.T("settings.perf.tip.apply_glow", "UserPreferences.glowEffects Off/Low/High per quality level."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => PerfSettingBool(c => c.PerfApplyGlowEffects, false),
                SetBool = v => SetPerfBool(b => VPBConfig.Instance.PerfApplyGlowEffects = b, v),
            });
        }
    }
}
