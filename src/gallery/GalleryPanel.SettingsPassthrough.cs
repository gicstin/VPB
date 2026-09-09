using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static string[] _passthroughPresetOptions;

        private static readonly string[] PassthroughHideSceneOptions =
        {
            VpbPassthrough.HideAllButPeople, VpbPassthrough.HideEnvironment, VpbPassthrough.HideNothing,
        };

        private static readonly string[] PassthroughLightFocusOptions = VpbPassthroughLights.SlotNames;

        private static string[] PassthroughPresetOptions()
        {
            if (_passthroughPresetOptions == null)
            {
                var presets = VpbPassthrough.Presets;
                var list = new string[presets.Length + 1];
                for (int i = 0; i < presets.Length; i++) list[i] = presets[i].Name;
                list[presets.Length] = VpbPassthrough.PresetCustom;
                _passthroughPresetOptions = list;
            }
            return _passthroughPresetOptions;
        }

        private static int PassthroughKeyByte(int channel)
        {
            var cfg = VPBConfig.Instance;
            if (cfg == null) return 0;
            if (channel == 0) return VpbPassthrough.ToByte(cfg.PassthroughKeyColorR);
            if (channel == 1) return VpbPassthrough.ToByte(cfg.PassthroughKeyColorG);
            return VpbPassthrough.ToByte(cfg.PassthroughKeyColorB);
        }

        private void SetPassthroughKeyByte(int channel, float value)
        {
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;
            float v = VpbPassthrough.FromByte(value);
            if (channel == 0) cfg.PassthroughKeyColorR = v;
            else if (channel == 1) cfg.PassthroughKeyColorG = v;
            else cfg.PassthroughKeyColorB = v;
            cfg.PassthroughKeyCustom = true;
            PassthroughSettingChanged();
            RefreshInternalSettingsListRows(true);
        }

        private static string PassthroughKeyRgbText()
        {
            return "R " + PassthroughKeyByte(0)
                + "    G " + PassthroughKeyByte(1)
                + "    B " + PassthroughKeyByte(2);
        }

        private static bool PassthroughKeyIsCustom()
        {
            return string.Equals(
                VpbPassthrough.PresetNameFor(PassthroughKeyByte(0), PassthroughKeyByte(1), PassthroughKeyByte(2)),
                VpbPassthrough.PresetCustom,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool PassthroughLightsOn()
        {
            try { return VPBConfig.Instance != null && VPBConfig.Instance.PassthroughLightsEnabled; }
            catch { return false; }
        }

        private static bool PassthroughLightsPlacing()
        {
            return PassthroughLightsOn() && VpbPassthroughLights.HandlesVisible;
        }

        private static void PassthroughSettingChanged()
        {
            try { VpbPassthrough.NotifySettingsChanged(); }
            catch { }
            try { VPBConfig.Instance.TriggerChange(); }
            catch { }
        }

        internal static void NotifyPassthroughLightsUi()
        {
            try
            {
                if (Gallery.singleton == null) return;
                var panels = Gallery.singleton.Panels;
                if (panels == null) return;
                for (int i = 0; i < panels.Count; i++)
                {
                    GalleryPanel p = panels[i];
                    if (p == null) continue;
                    if (p.IsSettingsPanelOpen())
                    {
                        p.InvalidateInternalSettingsDefsCache();
                        p.RefreshInternalSettingsListRows(true);
                    }
                }
            }
            catch { }
        }

        private void AppendPassthroughInternalSettingDefinitions(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;
            VpbPassthroughLights.UiNeedsRefresh = NotifyPassthroughLightsUi;

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.status",
                GroupKey = "passthrough",
                Label = VPBTranslation.T("settings.passthrough.status", "Status"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.status",
                    "What passthrough is doing right now. Placing means VaM is in Edit with only the real-world lamp handles up — finish with Done. Select a handle when you want that lamp's own panel."),
                ControlType = InternalSettingControlType.ReadOnlyText,
                WrapValue = true,
                GetString = PassthroughStatusText
            });

            var master = new InternalSettingDefinition
            {
                Key = "passthrough.enabled",
                GroupKey = "passthrough",
                Label = VPBTranslation.T("settings.passthrough.enabled", "Passthrough"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.enabled",
                    "Paints the whole background one flat colour so Virtual Desktop can replace it with your room. Set the same colour on both sides — here, and in the Streamer's VR Passthrough chroma key.\n\nRecommended settings: key colour R 0 G 30 B 60, Similarity 5, Smoothness 0.\n\nWhat else decides how clean the cut-out looks is the codec and the link: HEVC 10-bit carries colour far more precisely than H.264, a high bitrate means less compression fringe, and the VDXR runtime avoids an extra compositor pass that widens the edge.\n\nThe desktop mirror stays black while this is on — only the headset feeds the chroma filter. Everything it changes is put back when you turn it off."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PassthroughEnabled,
                SetBool = v =>
                {
                    VPBConfig.Instance.PassthroughEnabled = v;
                    PassthroughSettingChanged();
                }
            };
            master.SetDefault(false);
            defs.Add(master);

            var preset = new InternalSettingDefinition
            {
                Key = "passthrough.preset",
                GroupKey = "pt_chroma",
                Label = VPBTranslation.T("settings.passthrough.preset", "Preset"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.preset",
                    "Recommended is a very dark blue, R 0 G 30 B 60. Start here. Being dark and unsaturated it spills almost no colour onto anything in front of it, and it sits well inside what the video encoder can carry, which is what keeps the cut-out edge from fringing. With it, Similarity 5 and Smoothness 0 in the Streamer give a tight, clean cut.\n\nBlue, Green, Black and White are the Streamer's own built-in swatches, there if you prefer one. Black and White are risky: they key out every dark or bright pixel in the scene along with the background.\n\nCustom shows the three sliders below. Moving a slider switches to Custom on its own."),
                ControlType = InternalSettingControlType.Cycle,
                Options = PassthroughPresetOptions(),
                GetString = () => VpbPassthrough.PresetNameFor(
                    PassthroughKeyByte(0), PassthroughKeyByte(1), PassthroughKeyByte(2)),
                SetString = v =>
                {
                    if (string.Equals(v, VpbPassthrough.PresetCustom, StringComparison.OrdinalIgnoreCase))
                    {
                        VPBConfig.Instance.PassthroughKeyCustom = true;
                        PassthroughSettingChanged();
                        return;
                    }
                    VpbPassthrough.KeyPreset p;
                    if (!VpbPassthrough.TryGetPreset(v, out p)) return;
                    VPBConfig.Instance.PassthroughKeyCustom = false;
                    VPBConfig.Instance.SetPassthroughKeyColor(new Color(
                        VpbPassthrough.FromByte(p.R),
                        VpbPassthrough.FromByte(p.G),
                        VpbPassthrough.FromByte(p.B),
                        1f));
                }
            };
            preset.SetDefault(VpbPassthrough.Presets[0].Name);
            defs.Add(preset);

            AppendPassthroughChannelRow(defs, 0,
                VPBTranslation.T("settings.passthrough.red", "Red"), 0f);
            AppendPassthroughChannelRow(defs, 1,
                VPBTranslation.T("settings.passthrough.green", "Green"), 30f);
            AppendPassthroughChannelRow(defs, 2,
                VPBTranslation.T("settings.passthrough.blue", "Blue"), 60f);

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.rgb",
                GroupKey = "pt_chroma",
                Label = VPBTranslation.T("settings.passthrough.rgb", "Virtual Desktop RGB"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.rgb",
                    "Type these three numbers into Virtual Desktop. They must match exactly on both sides."),
                ControlType = InternalSettingControlType.ReadOnlyText,
                GetString = PassthroughKeyRgbText
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.copyVd",
                GroupKey = "pt_chroma",
                Label = VPBTranslation.T("settings.passthrough.copy_vd", "Copy for Virtual Desktop"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.copy_vd",
                    "Puts the key colour and the Virtual Desktop settings that go with it on your clipboard, so you can read them off next to the Streamer window."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VPBTranslation.T("settings.passthrough.copy_vd_do", "COPY"),
                OnAction = CopyPassthroughChromaSettings
            });

            var clean = new InternalSettingDefinition
            {
                Key = "passthrough.cleanKey",
                GroupKey = "pt_cutout",
                Label = VPBTranslation.T("settings.passthrough.clean_key", "Turn off camera effects"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.clean_key",
                    "Bloom, depth of field, sun shafts, fog, colour grading and ambient occlusion all smear the key colour outward. That smear is what makes hair and edges glow. They come back when you turn passthrough off."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PassthroughCleanKey,
                SetBool = v =>
                {
                    VPBConfig.Instance.PassthroughCleanKey = v;
                    PassthroughSettingChanged();
                }
            };
            clean.SetDefault(true);
            defs.Add(clean);

            var exact = new InternalSettingDefinition
            {
                Key = "passthrough.exactColor",
                GroupKey = "pt_cutout",
                Label = VPBTranslation.T("settings.passthrough.exact_color", "Exact key colour"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.exact_color",
                    "Drops the camera's HDR pass and VaM's glow, so the background lands on exactly the R/G/B above instead of near it. Without this the colour drifts and Virtual Desktop needs a high Similarity to catch it, which eats fine detail like stray hair."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PassthroughExactColor,
                SetBool = v =>
                {
                    VPBConfig.Instance.PassthroughExactColor = v;
                    PassthroughSettingChanged();
                }
            };
            exact.SetDefault(true);
            defs.Add(exact);

            var hard = new InternalSettingDefinition
            {
                Key = "passthrough.hardEdges",
                GroupKey = "pt_cutout",
                Label = VPBTranslation.T("settings.passthrough.hard_edges", "Hard edges"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.hard_edges",
                    "This is the shimmer fix. Anti-aliasing blends the outline of a person halfway into the key colour, and those half-key pixels are exactly the ones Virtual Desktop cannot decide about — so they flicker in and out, frame by frame, as a shimmer around the body.\n\nTurning MSAA off makes the outline a clean hard edge that keys the same way every frame. Edges look slightly more jagged in return. Your MSAA setting is put back when you turn passthrough off."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PassthroughHardEdges,
                SetBool = v =>
                {
                    VPBConfig.Instance.PassthroughHardEdges = v;
                    PassthroughSettingChanged();
                }
            };
            hard.SetDefault(true);
            defs.Add(hard);

            var hide = new InternalSettingDefinition
            {
                Key = "passthrough.hideScene",
                GroupKey = "pt_cutout",
                Label = VPBTranslation.T("settings.passthrough.hide_scene", "Hide"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.hide_scene",
                    "Cuts scene meshes out of the headset key so your real room can show through. This is temporary and is never saved into the scene.\n\nPeople — only person atoms stay. Walls, furniture, custom assets and other objects are cut out.\nRooms — hides rooms, walls, furniture and custom assets; keeps people, lights and animation.\nKeep — leaves the scene alone."),
                ControlType = InternalSettingControlType.Choice,
                Options = PassthroughHideSceneOptions,
                GetString = () => VPBConfig.NormalizePassthroughHideScene(VPBConfig.Instance.PassthroughHideScene),
                SetString = v =>
                {
                    VPBConfig.Instance.PassthroughHideScene = VPBConfig.NormalizePassthroughHideScene(v);
                    PassthroughSettingChanged();
                }
            };
            hide.SetDefault(VpbPassthrough.HideAllButPeople);
            defs.Add(hide);

            AppendPassthroughLightRows(defs);
        }

        private void AppendPassthroughChannelRow(List<InternalSettingDefinition> defs, int channel, string label, float defaultByte)
        {
            var def = new InternalSettingDefinition
            {
                Key = "passthrough.key" + channel,
                GroupKey = "pt_chroma",
                Label = label,
                Tooltip = VPBTranslation.T("settings.tip.passthrough.channel",
                    "0–255, the same scale the streamer uses. Whatever you set here has to match the streamer's chroma key exactly or it will not cut out. Moving this switches the preset above to Custom; the value commits when you let go of the slider."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => PassthroughKeyByte(channel),
                SetFloat = v => SetPassthroughKeyByte(channel, v),
                Min = 0f,
                Max = 255f,
                Step = 1f,
                Decimals = 0,
                DeferLiveApply = true,
                RowVisible = PassthroughKeyIsCustom
            };
            def.SetDefault(defaultByte);
            defs.Add(def);
        }

        private void AppendPassthroughLightRows(List<InternalSettingDefinition> defs)
        {
            var lightsOn = new InternalSettingDefinition
            {
                Key = "passthrough.lightsEnabled",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.lights_enabled", "Real-world lights"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.lights_enabled",
                    "Turns on lamps that live in your real room, not in the scene. They only exist while passthrough itself is on, and they are removed when either switch goes off — scene lights come back at the same time.\n\nThey are pinned to your play space, so walking, turning or teleporting in game moves them with you and they stay exactly where you put them in the room."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PassthroughLightsEnabled,
                SetBool = v =>
                {
                    VPBConfig.Instance.PassthroughLightsEnabled = v;
                    if (!v) VpbPassthroughLights.TeardownIfNeeded();
                    PassthroughSettingChanged();
                }
            };
            lightsOn.SetDefault(false);
            defs.Add(lightsOn);

            var count = new InternalSettingDefinition
            {
                Key = "passthrough.lightCount",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.light_count", "Lamps"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.light_count",
                    "How many real-world lamps are on, from 1 to 4 (front, right, left, back). Unused lamps keep their last place and look, and come back when you raise this again.\n\nIntensity, colour and reach live on each lamp's own VaM panel — Place in room, then use that panel."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VpbPassthroughLights.GetActiveCount(),
                SetFloat = v =>
                {
                    VpbPassthroughLights.SetActiveCount(Mathf.RoundToInt(v));
                    PassthroughSettingChanged();
                },
                Min = 1f,
                Max = VpbPassthroughLights.MaxLights,
                Step = 1f,
                Decimals = 0,
                DeferLiveApply = true,
                RowVisible = PassthroughLightsOn
            };
            count.SetDefault(1f);
            defs.Add(count);

            var overrideScene = new InternalSettingDefinition
            {
                Key = "passthrough.lightsOverride",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.lights_override", "Replace scene lights"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.lights_override",
                    "Switches off every light the scene brought with it, so the only lighting is the lamps you placed in your room. This is what makes a person look like they are lit by your actual room rather than by their scene.\n\nOff means your lamps are added on top of the scene's own. Scene lights come back when real-world lights are turned off, or when passthrough ends."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PassthroughLightsOverrideScene,
                SetBool = v =>
                {
                    VPBConfig.Instance.PassthroughLightsOverrideScene = v;
                    PassthroughSettingChanged();
                },
                RowVisible = PassthroughLightsOn
            };
            overrideScene.SetDefault(true);
            defs.Add(overrideScene);

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightsPlace",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.lights_place", "Place in room"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.lights_place",
                    "Puts VaM in Edit and shows only the real-world lamp handles. Grip a handle to move it. Select a handle (VaM Select) when you want that lamp's intensity, colour and reach panel. Done hides the handles and returns to the mode you were in."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VpbPassthroughLights.HandlesVisible
                    ? VPBTranslation.T("settings.passthrough.lights_place_done", "DONE")
                    : VPBTranslation.T("settings.passthrough.lights_place_start", "PLACE"),
                OnAction = PassthroughToggleHandles,
                RowVisible = PassthroughLightsOn
            });

            var group = new InternalSettingDefinition
            {
                Key = "passthrough.lightsGroup",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.lights_group", "Move together"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.lights_group",
                    "Grabbing any one lamp carries the others with it, keeping the shape of the whole set. Use it once the lamps are arranged the way you like and you only want to shift the arrangement to a different part of the room.\n\nOff, each lamp moves on its own."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.PassthroughLightsMoveAsGroup,
                SetBool = v =>
                {
                    VPBConfig.Instance.PassthroughLightsMoveAsGroup = v;
                    PassthroughSettingChanged();
                },
                RowVisible = PassthroughLightsPlacing
            };
            group.SetDefault(false);
            defs.Add(group);

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightFocus",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.light_focus", "Lamp"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.light_focus",
                    "Which lamp Bring here moves. Grabbing or selecting a lamp in the room picks it here too."),
                ControlType = InternalSettingControlType.Cycle,
                Options = PassthroughLightFocusOptionsForCount(),
                GetString = () =>
                {
                    int i = VpbPassthroughLights.SelectedIndex;
                    string[] opts = PassthroughLightFocusOptionsForCount();
                    if (i < 0) i = 0;
                    if (i >= opts.Length) i = opts.Length - 1;
                    return opts[i];
                },
                SetString = v =>
                {
                    VpbPassthroughLights.Select(VpbPassthroughLights.SlotIndexFromName(v));
                },
                RowVisible = PassthroughLightsPlacing
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightsBring",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.lights_bring", "Bring here"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.lights_bring",
                    "Moves only the lamp named above to just in front of you. The others stay put. Use it if that lamp ended up behind a wall, above the ceiling, or somewhere you cannot find."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VPBTranslation.T("settings.passthrough.lights_bring_do", "BRING"),
                OnAction = PassthroughBringFocusedLightToPlayer,
                RowVisible = PassthroughLightsPlacing
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightPreset",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.light_preset", "Preset"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.light_preset",
                    "Saved real-world lamp layouts: how many lamps, where they sit, and each lamp's look. Pick one, then Load. Save writes the current layout under the name below. The last saved or loaded preset is applied when real-world lights start."),
                ControlType = InternalSettingControlType.Cycle,
                Options = VpbPassthroughLights.PresetNames(),
                GetString = VpbPassthroughLights.SelectedPresetName,
                SetString = v =>
                {
                    VpbPassthroughLights.SetSelectedPresetName(v);
                    InvalidateInternalSettingsDefsCache();
                },
                RowVisible = PassthroughLightsOn
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightPresetName",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.light_preset_name", "Preset name"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.light_preset_name",
                    "Name used when you press Save. Leave empty to overwrite the selected preset, or to create Lights 1, Lights 2, and so on."),
                ControlType = InternalSettingControlType.TextArea,
                SingleLine = true,
                GetString = VpbPassthroughLights.DraftPresetName,
                SetString = VpbPassthroughLights.SetDraftPresetName,
                RowVisible = PassthroughLightsOn
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightPresetLoad",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.light_preset_load", "Load preset"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.light_preset_load",
                    "Restores that preset's lamp count, positions, and each lamp's look."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VPBTranslation.T("settings.passthrough.light_preset_load_do", "LOAD"),
                OnAction = PassthroughLoadLightPreset,
                RowVisible = PassthroughLightsOn
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightPresetSave",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.light_preset_save", "Save preset"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.light_preset_save",
                    "Stores the current lamps — count, place, and look — under the name above."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VPBTranslation.T("settings.passthrough.light_preset_save_do", "SAVE"),
                OnAction = PassthroughSaveLightPreset,
                RowVisible = PassthroughLightsOn
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "passthrough.lightPresetDelete",
                GroupKey = "pt_lights",
                Label = VPBTranslation.T("settings.passthrough.light_preset_delete", "Delete preset"),
                Tooltip = VPBTranslation.T("settings.tip.passthrough.light_preset_delete",
                    "Removes the selected preset. The lamps currently in the room stay as they are."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VPBTranslation.T("settings.passthrough.light_preset_delete_do", "DELETE"),
                OnAction = PassthroughDeleteLightPreset,
                RowVisible = PassthroughLightsOn
            });
        }

        private void PassthroughToggleHandles()
        {
            if (VpbPassthroughLights.HandlesVisible)
            {
                VpbPassthroughLights.SetHandlesVisible(false);
                InvalidateInternalSettingsDefsCache();
                RefreshInternalSettingsListRows(true);
                ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.lights_place_ended",
                    "Light positions saved."), 2f);
                return;
            }

            VpbPassthroughLights.SetHandlesVisible(true);
            InvalidateInternalSettingsDefsCache();
            RefreshInternalSettingsListRows(true);
            if (!VpbPassthroughLights.HandlesVisible)
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.lights_place_unavailable",
                    "Passthrough and Real-world lights both have to be on, in VR, before the lamps can be placed."), 4f);
                return;
            }
            ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.lights_place_started",
                "Edit mode — grip a handle to move. Select a handle for that lamp's panel. Press Done when finished."), 6f);
        }

        private static string[] PassthroughLightFocusOptionsForCount()
        {
            int n = VpbPassthroughLights.GetActiveCount();
            if (n < 1) n = 1;
            if (n > PassthroughLightFocusOptions.Length) n = PassthroughLightFocusOptions.Length;
            if (n == PassthroughLightFocusOptions.Length) return PassthroughLightFocusOptions;
            string[] opts = new string[n];
            for (int i = 0; i < n; i++) opts[i] = PassthroughLightFocusOptions[i];
            return opts;
        }

        private void PassthroughBringFocusedLightToPlayer()
        {
            int i = VpbPassthroughLights.SelectedIndex;
            VpbPassthroughLights.BringSlotToPlayer(i);
            RefreshInternalSettingsListRows(true);
            ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.lights_brought_one",
                "Lamp moved in front of you.") + " — " + VpbPassthroughLights.SlotName(i), 2.5f);
        }

        private void PassthroughSaveLightPreset()
        {
            if (!VpbPassthroughLights.TrySavePreset(VpbPassthroughLights.DraftPresetName()))
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.light_preset_save_fail",
                    "Could not save that preset. Use a short name, or delete one if you are at 16."), 4f);
                return;
            }
            InvalidateInternalSettingsDefsCache();
            RefreshInternalSettingsListRows(true);
            ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.light_preset_save_ok",
                "Saved preset") + " — " + VpbPassthroughLights.SelectedPresetName(), 3f);
        }

        private void PassthroughLoadLightPreset()
        {
            if (!VpbPassthroughLights.TryLoadPreset(VpbPassthroughLights.SelectedPresetName()))
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.light_preset_load_fail",
                    "No preset to load. Save one first."), 3f);
                return;
            }
            InvalidateInternalSettingsDefsCache();
            PassthroughSettingChanged();
            RefreshInternalSettingsListRows(true);
            ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.light_preset_load_ok",
                "Loaded preset") + " — " + VpbPassthroughLights.SelectedPresetName(), 3f);
        }

        private void PassthroughDeleteLightPreset()
        {
            string name = VpbPassthroughLights.SelectedPresetName();
            if (!VpbPassthroughLights.TryDeletePreset(name))
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.light_preset_delete_fail",
                    "Nothing to delete."), 2.5f);
                return;
            }
            InvalidateInternalSettingsDefsCache();
            RefreshInternalSettingsListRows(true);
            ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.light_preset_delete_ok",
                "Deleted preset") + " — " + name, 3f);
        }

        private static string PassthroughStatusText()
        {
            if (!VpbPassthrough.IsVrActive())
                return VPBTranslation.T("settings.passthrough.status_desktop", "Desktop — headset only");
            if (!VpbPassthrough.IsActive)
                return VPBTranslation.T("settings.passthrough.status_off", "Off");

            if (VpbPassthroughLightAtoms.IsSpawning)
                return VPBTranslation.T("settings.passthrough.status_spawning", "Spawning real-world lights");

            if (VpbPassthroughLights.HandlesVisible)
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.PassthroughLightsMoveAsGroup)
                    return VPBTranslation.T("settings.passthrough.status_placing_group", "Edit — grip to move together");
                return VPBTranslation.T("settings.passthrough.status_placing", "Edit — grip to move; Select for that lamp's panel");
            }

            return VPBTranslation.T("settings.passthrough.status_on", "On")
                + "  ·  " + VPBTranslation.T("settings.passthrough.status_cameras", "cameras") + " " + VpbPassthrough.PatchedCameraCount
                + "  ·  " + VPBTranslation.T("settings.passthrough.status_hidden", "hidden") + " " + VpbPassthrough.HiddenAtomCount
                + "  ·  " + VPBTranslation.T("settings.passthrough.status_lights", "lights") + " " + VpbPassthroughLights.ActiveLightCount;
        }

        private void CopyPassthroughChromaSettings()
        {
            string text = "Virtual Desktop  >  Streaming  >  VR Passthrough  >  Configure"
                + "\r\nR " + PassthroughKeyByte(0)
                + "   G " + PassthroughKeyByte(1)
                + "   B " + PassthroughKeyByte(2)
                + "\r\nSimilarity " + VpbPassthrough.VdSimilarity
                + "\r\nSmoothness " + VpbPassthrough.VdSmoothness
                + "\r\nCodec HEVC 10-bit, bitrate as high as your link holds, runtime VDXR";
            try
            {
                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus(VPBTranslation.T("settings.passthrough.copied",
                    "Chroma key settings copied to clipboard."), 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Copy passthrough chroma settings failed: " + ex.Message);
                ShowTemporaryStatus(PassthroughKeyRgbText(), 6f);
            }
        }

        private static void CapturePassthroughSettingsIntoSnapshot(InternalSettingsSnapshot snap)
        {
            if (snap == null) return;
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;
            snap.PassthroughEnabled = cfg.PassthroughEnabled;
            snap.PassthroughKeyColorR = cfg.PassthroughKeyColorR;
            snap.PassthroughKeyColorG = cfg.PassthroughKeyColorG;
            snap.PassthroughKeyColorB = cfg.PassthroughKeyColorB;
            snap.PassthroughKeyCustom = cfg.PassthroughKeyCustom;
            snap.PassthroughLightsEnabled = cfg.PassthroughLightsEnabled;
            snap.PassthroughLightsOverrideScene = cfg.PassthroughLightsOverrideScene;
            snap.PassthroughLightsMoveAsGroup = cfg.PassthroughLightsMoveAsGroup;
            snap.PassthroughLightCount = VpbPassthroughLights.GetActiveCount();
            snap.PassthroughCleanKey = cfg.PassthroughCleanKey;
            snap.PassthroughExactColor = cfg.PassthroughExactColor;
            snap.PassthroughHardEdges = cfg.PassthroughHardEdges;
            snap.PassthroughHideScene = VPBConfig.NormalizePassthroughHideScene(cfg.PassthroughHideScene);
        }

        private static void RestorePassthroughSettingsFromSnapshot(InternalSettingsSnapshot b)
        {
            if (b == null) return;
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;
            cfg.PassthroughEnabled = b.PassthroughEnabled;
            cfg.PassthroughKeyColorR = b.PassthroughKeyColorR;
            cfg.PassthroughKeyColorG = b.PassthroughKeyColorG;
            cfg.PassthroughKeyColorB = b.PassthroughKeyColorB;
            cfg.PassthroughKeyCustom = b.PassthroughKeyCustom;
            cfg.PassthroughLightsEnabled = b.PassthroughLightsEnabled;
            cfg.PassthroughLightsOverrideScene = b.PassthroughLightsOverrideScene;
            cfg.PassthroughLightsMoveAsGroup = b.PassthroughLightsMoveAsGroup;
            try { VpbPassthroughLights.SetActiveCount(b.PassthroughLightCount); }
            catch { }
            cfg.PassthroughCleanKey = b.PassthroughCleanKey;
            cfg.PassthroughExactColor = b.PassthroughExactColor;
            cfg.PassthroughHardEdges = b.PassthroughHardEdges;
            cfg.PassthroughHideScene = VPBConfig.NormalizePassthroughHideScene(b.PassthroughHideScene);
            try { VpbPassthrough.NotifySettingsChanged(); }
            catch { }
        }
    }
}
