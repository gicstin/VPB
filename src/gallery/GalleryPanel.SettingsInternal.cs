using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    /// <summary>Commits a deferred settings slider's value when the drag/click is released.
    /// Lives on the slider host alongside the Slider so it receives the same pointer-up/end-drag events.</summary>
    internal sealed class SettingsSliderReleaseCommit : MonoBehaviour, IPointerUpHandler, IEndDragHandler
    {
        public Action OnRelease;
        public void OnPointerUp(PointerEventData eventData) { try { OnRelease?.Invoke(); } catch { } }
        public void OnEndDrag(PointerEventData eventData) { try { OnRelease?.Invoke(); } catch { } }
    }

    /// <summary>Pointer-down, not Button.onClick — VR laser + ScrollRect drag often cancels click.
    /// Do not add IDragHandler: steals list drag and drops idle-rim restore on pointer-exit.</summary>
    internal sealed class SettingsPointerDownAction : MonoBehaviour, IPointerDownHandler
    {
        public Action OnDown;
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            try { if (OnDown != null) OnDown(); } catch { }
        }
    }

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
            /// <summary>Authoring sub-classifier. After <c>RemapSettingDefinitionGroups</c> this holds the
            /// fine-grained group key (e.g. <c>hover</c>) used for behavioural special-cases; the legacy
            /// <c>categories</c> group also uses it pre-remap to pick <c>options</c> vs <c>visibility</c>.</summary>
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

            /// <summary>Slider only: when true, the live value is shown while dragging but
            /// <see cref="SetFloat"/> is committed on pointer/drag release. Used by settings whose
            /// change rebuilds the settings list rows (UI scale) — applying live would destroy the
            /// slider mid-drag and drop the gesture.</summary>
            public bool DeferLiveApply;

            public string[] Options;
            public Func<string> GetString;
            public Action<string> SetString;

            /// <summary>When non-null and returns false, row omitted from settings list (e.g. slider hidden until parent toggle on).</summary>
            public Func<bool> RowVisible;

            /// <summary>Fired when a Button-type row is clicked (primary or secondary click).</summary>
            public Action OnAction;
            public Func<bool> ActionEnabled;

            public Func<Color> GetColor;
            public Action<Color> SetColor;

            /// <summary>True when this row has a factory default and may show Reset.</summary>
            public bool HasDefault;
            public bool DefaultBool;
            public float DefaultFloat;
            public string DefaultString;
            public Color DefaultColor;

            internal void SetDefault(bool v)
            {
                HasDefault = true;
                DefaultBool = v;
            }

            internal void SetDefault(float v)
            {
                HasDefault = true;
                DefaultFloat = v;
            }

            internal void SetDefault(string v)
            {
                HasDefault = true;
                DefaultString = v ?? "";
            }

            internal void SetDefault(Color v)
            {
                HasDefault = true;
                DefaultColor = v;
            }

            internal void ApplyFactoryDefault()
            {
                if (!HasDefault) return;
                switch (ControlType)
                {
                    case InternalSettingControlType.Toggle:
                        if (SetBool != null) SetBool(DefaultBool);
                        break;
                    case InternalSettingControlType.Slider:
                        if (SetFloat != null) SetFloat(DefaultFloat);
                        break;
                    case InternalSettingControlType.Cycle:
                    case InternalSettingControlType.TextArea:
                    case InternalSettingControlType.Hotkey:
                        if (SetString != null) SetString(DefaultString ?? "");
                        break;
                    case InternalSettingControlType.ColorRgb:
                        if (SetColor != null) SetColor(DefaultColor);
                        break;
                }
            }
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

        // ── Settings group consolidation ──
        // Each row's original fine-grained group key is mapped onto one of a small set of broad
        // groups shown as a single layer of top-level settings tabs (no sub-tabs). row[0] is the
        // displayed group key; row[1..] are the fine keys folded into it, in authoring order.
        private static readonly string[][] SettingsGroupStructure = new[]
        {
            new[] { "appearance",      "visuals", "hover" },
            new[] { "grid_highlights", "grid", "scan_wl_border" },
            new[] { "layout",          "follow", "desktop" },
            new[] { "vr",              "vr" },
            new[] { "browsing",        "lists", "cat_general", "tags", "search" },
            new[] { "cat_visibility",  "cat_visibility" },
            new[] { "interaction",     "interaction", "plugin_quickmenu" },
            new[] { "shortcuts",       "keys_rules", "plugin_hotkeys", "keys_chrome", "keys_browse", "keys_selection", "keys_tools", "keys_world" },
            new[] { "performance",     "performance", "plugin_zstd", "plugin_scan_whitelist" },
            new[] { "maintenance",     "helpers", "updater", "ba_migration" },
        };

        private static Dictionary<string, string> _settingsFineToGroup;

        private static Dictionary<string, string> SettingsFineToGroup()
        {
            if (_settingsFineToGroup == null)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in SettingsGroupStructure)
                    for (int i = 1; i < row.Length; i++) d[row[i]] = row[0];
                _settingsFineToGroup = d;
            }
            return _settingsFineToGroup;
        }

        private sealed class SettingsGroupTab { public string Key; public string Label; public string Icon; }

        private static string SettingsGroupLabel(string key)
        {
            switch (key)
            {
                case "appearance":      return VPBTranslation.T("settings.group.tab.appearance", "Appearance");
                case "grid_highlights": return VPBTranslation.T("settings.group.tab.grid_highlights", "Grid & Highlights");
                case "layout":          return VPBTranslation.T("settings.group.tab.layout", "Layout & Position");
                case "vr":              return VPBTranslation.T("settings.group.tab.vr", "VR");
                case "browsing":        return VPBTranslation.T("settings.group.tab.browsing", "Browsing");
                case "cat_visibility":  return VPBTranslation.T("settings.group.category_visibility", "Category visibility");
                case "interaction":     return VPBTranslation.T("settings.group.tab.interaction", "Interaction");
                case "shortcuts":       return VPBTranslation.T("settings.group.tab.shortcuts", "Shortcuts");
                case "performance":     return VPBTranslation.T("settings.group.tab.performance", "Performance");
                case "maintenance":     return VPBTranslation.T("settings.group.tab.maintenance", "Maintenance");
                default:                return key;
            }
        }

        /// <summary>Toggle chip label. Canonical ON/OFF stay as stored state; display follows locale.</summary>
        private static string FormatSettingsToggleLabel(bool on)
        {
            return on
                ? VPBTranslation.T("settings.toggle.on", "ON")
                : VPBTranslation.T("settings.toggle.off", "OFF");
        }

        /// <summary>Cycle chip label. Stored option strings stay English; display is translated.
        /// English keeps compact uppercase chips; other locales use natural script (no forced caps).</summary>
        private static string FormatSettingsCycleOption(string value)
        {
            string display = TranslateSettingsCycleOption(value);
            if (string.IsNullOrEmpty(display)) return "";
            if (string.Equals(VPBTranslation.CurrentLocale, "en", StringComparison.OrdinalIgnoreCase))
                return display.ToUpperInvariant();
            return display;
        }

        private static string TranslateSettingsCycleOption(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.follow.off", "Off");
            if (string.Equals(value, "Both", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.follow.both", "Both");
            if (string.Equals(value, "Desktop", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.follow.desktop", "Desktop");
            if (string.Equals(value, "VR", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.follow.vr", "VR");
            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.side_left", "Left");
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.side_right", "Right");
            if (string.Equals(value, "Top", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.side_top", "Top");
            if (string.Equals(value, "replace", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.appearance.preset_outfit", "Preset look");
            if (string.Equals(value, "keep", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.appearance.keep_body", "Keep body clothes");
            if (string.Equals(value, "clothingonly", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.appearance.clothes_only", "Clothes only");
            if (string.Equals(value, "mergeoutfit", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.appearance.merge_outfit", "Merge outfit");
            if (string.Equals(value, "Scenes", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.initial_gallery.scenes", "Scenes");
            if (string.Equals(value, "Clothing", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.initial_gallery.clothing", "Clothing");
            if (string.Equals(value, "Hair", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.initial_gallery.hair", "Hair");
            if (string.Equals(value, "Pose", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.initial_gallery.pose", "Pose");
            if (string.Equals(value, "Appearance", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.initial_gallery.appearance", "Appearance");
            if (string.Equals(value, "Plugins", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.initial_gallery.plugins", "Plugins");
            if (string.Equals(value, "LastUsed", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.initial_gallery.last_used", "Last used");
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.none", "None");
            if (string.Equals(value, "Import", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.side.scene_import_short", "Import");
            if (string.Equals(value, "Tags", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.side.tags", "User Tags");
            if (string.Equals(value, "Category", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.side.category", "Categories");
            if (string.Equals(value, "Creator", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.side.creator", "Creators");
            if (string.Equals(value, "Path", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.side.path", "Path");
            if (string.Equals(value, "History", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.history.row.history", "History");
            if (string.Equals(value, "Filter tags", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.filter_tags", "Filter tags");
            if (string.Equals(value, "Apply tags", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.apply_tags", "Apply tags");
            if (string.Equals(value, "Untagged only", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.untagged_only", "Untagged only");
            if (string.Equals(value, "Compound", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.compound", "Compound");
            if (string.Equals(value, "Isolate", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.isolate", "Isolate");
            if (string.Equals(value, "Desktop Only", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.desktop_only", "Desktop Only");
            if (string.Equals(value, "VR Only", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.vr_only", "VR Only");
            if (string.Equals(value, "Desktop & VR", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.desktop_and_vr", "Desktop & VR");
            if (string.Equals(value, "Path + Name", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.path_and_name", "Path + Name");
            if (string.Equals(value, "Name only", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.name_only", "Name only");
            if (string.Equals(value, "Name starts with", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.name_starts_with", "Name starts with");
            if (string.Equals(value, "Independent", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.independent", "Independent");
            if (string.Equals(value, "Synced", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.synced", "Synced");
            if (string.Equals(value, "List", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.list", "List");
            if (string.Equals(value, "Grid", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.grid", "Grid");
            if (string.Equals(value, "Left only", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.left_only", "Left only");
            if (string.Equals(value, "Right only", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.right_only", "Right only");
            if (string.Equals(value, "Opposite to menu", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.opposite_menu", "Opposite to menu");
            if (string.Equals(value, "Same hand", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.same_hand", "Same hand");
            if (string.Equals(value, "Glance", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.glance", "Glance");
            if (string.Equals(value, "Menu", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.menu", "Menu");
            if (string.Equals(value, "Always", StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.opt.always", "Always");
            return value;
        }

        /// <summary>Atlas role for settings group chips. Label stays primary; glyph is redundant landmark.</summary>
        private static string SettingsGroupIcon(string key)
        {
            switch (key)
            {
                case "all":             return "stack-2";
                case "appearance":      return "palette";
                case "grid_highlights": return "grid-scan";
                case "layout":          return "layout-sidebar";
                case "vr":              return "device-watch";
                case "browsing":        return "list-search";
                case "cat_visibility":  return "eye";
                case "interaction":     return "hand-finger";
                case "shortcuts":       return "hexagon-letter-k";
                case "performance":     return "gauge";
                case "maintenance":     return "tools";
                default:                return null;
            }
        }

        /// <summary>Ordered single-layer settings group tabs. VR chip only when running in VR.</summary>
        private List<SettingsGroupTab> GetSettingsGroupTabs()
        {
            bool isVr = false;
            try { isVr = VPBConfig.Instance != null && VPBConfig.Instance.IsVR; } catch { isVr = false; }
            var list = new List<SettingsGroupTab>(SettingsGroupStructure.Length);
            foreach (var row in SettingsGroupStructure)
            {
                if (row == null || row.Length == 0) continue;
                if (string.Equals(row[0], "vr", StringComparison.OrdinalIgnoreCase) && !isVr)
                    continue;
                list.Add(new SettingsGroupTab { Key = row[0], Label = SettingsGroupLabel(row[0]), Icon = SettingsGroupIcon(row[0]) });
            }
            return list;
        }

#if DEBUG
        // Fine keys already flagged as unmapped, so the dev warning fires at most once per key
        // (the remap runs on every settings-defs cache rebuild).
        private static HashSet<string> _settingsUnmappedFineWarned;
#endif

        /// <summary>Re-point each definition's GroupKey onto its single-layer display group. The fine
        /// key is preserved in SubGroupKey (used only for the hover live-preview special-case). The
        /// legacy two-level "categories" group is split into "cat_general" (-> Browsing) and
        /// "cat_visibility" (-> its own tab). A missing/unmapped key falls back to "maintenance" so no
        /// setting is ever left without a group; in DEBUG this is reported once per key so a new fine
        /// group is added to <see cref="SettingsGroupStructure"/> rather than silently absorbed.</summary>
        private void RemapSettingDefinitionGroups(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;
            var map = SettingsFineToGroup();
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null) continue;

                string fine;
                if (string.IsNullOrEmpty(d.GroupKey))
                    fine = "";
                else if (string.Equals(d.GroupKey, "categories", StringComparison.OrdinalIgnoreCase))
                    fine = string.Equals(d.SubGroupKey, "visibility", StringComparison.OrdinalIgnoreCase)
                        ? "cat_visibility" : "cat_general";
                else
                    fine = d.GroupKey;

                string group;
                if (string.IsNullOrEmpty(fine) || !map.TryGetValue(fine, out group))
                {
                    group = "maintenance";
#if DEBUG
                    string warnKey = string.IsNullOrEmpty(fine) ? ("<empty:" + (d.Key ?? "?") + ">") : fine;
                    if (_settingsUnmappedFineWarned == null)
                        _settingsUnmappedFineWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (_settingsUnmappedFineWarned.Add(warnKey))
                        Debug.LogWarning("VPB settings: definition '" + (d.Key ?? "?") + "' has group key '" + warnKey
                            + "' with no entry in SettingsGroupStructure; routed to 'maintenance'. Add it to a group.");
#endif
                }
                d.GroupKey = group;
                d.SubGroupKey = fine;
            }
        }

        private int ComputeInternalSettingsDefsCacheSignature()
        {
            int sig = 0;
            try
            {
                string loc = VPBTranslation.CurrentLocale;
                if (!string.IsNullOrEmpty(loc))
                {
                    for (int i = 0; i < loc.Length; i++)
                        sig = unchecked(sig * 31 + loc[i]);
                }
            }
            catch { }
            try { if (categories != null) sig = unchecked(sig * 31 + categories.Count); } catch { }
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
            RemapSettingDefinitionGroups(defs);
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
            rgv.fixedBottomChromePx = 0f;
            rgv.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh);
            rgv.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh);
        }

        private sealed class InternalSettingRowEntry : VirtualFileEntry
        {
            public string RowKey;
            public string GroupKey;
            public bool IsHeader;
            public bool IsCategoryHeader;

            public InternalSettingRowEntry(string rowKey, string groupKey, string label)
                : base("[SETTING] " + rowKey)
            {
                RowKey = rowKey ?? "";
                GroupKey = groupKey ?? SettingsDefaultGroupKey;
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

        private static bool GalleryElementCornerRadiusSubSettingsVisible()
        {
            try { return VPBConfig.Instance != null && VPBConfig.Instance.EnableGalleryElementRounding; }
            catch { return false; }
        }

        private static void ApplyGalleryElementCornerRadiusFromSettings()
        {
            try { UI.ApplyGalleryElementCornerRadiusGlobally(); } catch { }
            try { VPBConfig.Instance?.TriggerChange(); } catch { }
        }

        private sealed class InternalSettingsSnapshot
        {
            public float LayoutPresetRevertBarSeconds;
            public bool LayoutPresetSuggestOnModeSwitch;
            public bool DisableGalleryTransparency;
            public bool DisableGalleryPaneTransparency;
            public bool DisableGalleryAssignableButtonsTransparency;
            public bool DisableGalleryDockHoverTransparency;
            public bool EnableGalleryFade;
            public bool EnableGalleryTranslucency;
            public bool GalleryManualRefreshOnly;
            public int SceneImportCacheLimitMb;
            public bool GalleryDetailStripSideInfoEnabled;
            public bool GalleryDetailStripThumbOnRight;
            public float GalleryDetailStripHeightRef;
            public float GalleryOpacity;
            public float SideButtonScaleVR;
            public float SideButtonScaleDesktop;
            public float InnerPaneScaleVR;
            public float InnerPaneScaleDesktop;
            public bool EnableButtonGaps;
            public bool EnableGalleryElementRounding;
            public bool EnableGalleryButtonChromeRims;
            public float GalleryElementCornerRadiusFraction;
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
            public bool VerticalMoveKeysEnabled;
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
            public string SpringScrollButtonMode;
            public bool GalleryVrThumbstickScrollEnabled;
            public bool GalleryHideCreatorSideButtons;
            public bool GalleryShowCategoryIcons;
            public bool GalleryConsolidateCreatorNames;
            public bool PluginGalleryGridThumbnails;
            public bool PluginGalleryCategoryLabelsOnly;
            public bool GalleryThumbPlaceholderLabelsEnabled;
            public float GalleryThumbPlaceholderSizeScale;
            public bool GalleryListNamesLegacyFileName;
            public string GalleryHoverPreviewMode;
            public bool QuickMenuRandomHoverPreview;
            public float GalleryListHoverPreviewSize;
            public float GalleryListHoverPreviewOffsetX;
            public float GalleryListHoverPreviewOffsetY;
            public bool GalleryGridLabelsEnabled;
            public bool GalleryGridLabelsAutoHideAtHighDensity;
            public bool GalleryGridHoverBadgesEnabled;
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
            public bool GalleryFloatsOnlyMode;
            public bool GalleryAnchorToVamMenu;
            public float GalleryVrMenuAnchorTiltDeg;
            public string GalleryCategoryQuickOrder;
            public string GalleryCategoryQuickSwitchHidden;
            public HashSet<string> HiddenCategories;

            public string PluginGalleryKey;
            public string PluginCreateGalleryKey;
            public string PluginHubKey;
            public string PluginClearConsoleKey;
            public bool PluginDownscale8kTo4k;
            public bool PluginScanWhitelistEnabled;
            public string[] ShortcutPatterns;
            public bool ShortcutsRequireWindowFocus;
            public bool ShortcutsNeedVisiblePane;
            public bool CategoryNumberKeysEnabled;
            public string BlockInGameMessages;
            public bool HideMissingDependencyLogs;
            public bool ClearInGameLogsOnSceneLaunch;
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
                Label = VPBTranslation.T("settings.gallery_idle_transparency", "Transparency when idle"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_idle_transparency", "Makes the gallery pane translucent when idle (pointer away and no text field / search chrome active). Fully opaque while hovered or editing."),
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
                Label = VPBTranslation.T("settings.gallery_idle_opacity", "Opacity when idle"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_idle_opacity", "How visible the gallery pane is when idle (pointer away and no text focus). 1.0 = fully opaque, 0.1 = barely visible."),
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
                Label = VPBTranslation.T("settings.side_button_fade_idle", "Fade side buttons when idle"),
                Tooltip = VPBTranslation.T("settings.tip.side_button_fade_idle", "Hides side buttons when idle (pointer away and no text field / search chrome active)."),
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
                Key = "visuals.detailStripSideInfo", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.detail_strip_side_info", "Show description & package tags"),
                Tooltip = VPBTranslation.T(
                    "settings.tip.detail_strip_side_info",
                    "When on (default), a wide selection detail strip can show a right column with the package description and native package tags. Turn off to keep the strip compact — description stays under the actions when available."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryDetailStripSideInfoEnabled,
                SetBool = v =>
                {
                    VPBConfig.Instance.GalleryDetailStripSideInfoEnabled = v;
                    try { _detailStripCacheKey = ""; DetailStripRefresh(); } catch { }
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.detailStripThumbSide", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.detail_strip_thumb_side", "Detail preview side"),
                Tooltip = VPBTranslation.T(
                    "settings.tip.detail_strip_thumb_side",
                    "Place the selection detail-strip image preview on the left or right. Drag the thin bar at the top of the strip to resize height; the preview stays square."),
                ControlType = InternalSettingControlType.Cycle,
                Options = new[] { "Left", "Right" },
                GetString = () => VPBConfig.Instance.GalleryDetailStripThumbOnRight ? "Right" : "Left",
                SetString = v =>
                {
                    bool right = string.Equals(v, "Right", StringComparison.OrdinalIgnoreCase);
                    VPBConfig.Instance.GalleryDetailStripThumbOnRight = right;
                    try { DetailStripApplyLayoutPrefs(); } catch { }
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.galleryUiScaleVr", GroupKey = "visuals", Label = VPBTranslation.T("settings.gallery_ui_scale_vr", "Gallery UI Scale (VR)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_ui_scale_vr", "Scales gallery chrome, side buttons, and in-pane controls in VR. 1.0 = default size."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.InnerPaneScaleVR,
                SetFloat = v => { VPBConfig.Instance.InnerPaneScaleVR = Mathf.Clamp(v, VPBConfig.MinUiScale, VPBConfig.MaxUiScale); VPBConfig.Instance.TriggerChange(); },
                Min = VPBConfig.MinUiScale, Max = VPBConfig.MaxUiScale, Step = 0.1f, Decimals = 1,
                DeferLiveApply = true,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.IsVR
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.galleryUiScaleDesktop", GroupKey = "visuals", Label = VPBTranslation.T("settings.gallery_ui_scale_desktop", "Gallery UI Scale (Desktop)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_ui_scale_desktop", "Scales gallery chrome, side buttons, and in-pane controls on desktop. Also multiplies by VaM Monitor UI Scale. New installs auto-pick a starting value from screen height."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.InnerPaneScaleDesktop,
                SetFloat = v => { VPBConfig.Instance.InnerPaneScaleDesktop = Mathf.Clamp(v, VPBConfig.MinUiScale, VPBConfig.MaxUiScale); VPBConfig.Instance.TriggerChange(); },
                Min = VPBConfig.MinUiScale, Max = VPBConfig.MaxUiScale, Step = 0.1f, Decimals = 1,
                DeferLiveApply = true,
                RowVisible = () => VPBConfig.Instance != null && !VPBConfig.Instance.IsVR
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.sideGaps", GroupKey = "visuals", Label = VPBTranslation.T("settings.side_button_gaps", "Side Button Gaps"),
                Tooltip = VPBTranslation.T("settings.tip.side_button_gaps", "Adds small gaps between groups of side buttons for better visual separation."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.EnableButtonGaps,
                SetBool = v => { VPBConfig.Instance.EnableButtonGaps = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.elementRounding", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.gallery_element_rounding", "Rounded element corners"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_element_rounding", "Rounds gallery buttons and other UI element corners. Turn off for square corners."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.EnableGalleryElementRounding,
                SetBool = v => {
                    VPBConfig.Instance.EnableGalleryElementRounding = v;
                    ApplyGalleryElementCornerRadiusFromSettings();
                    if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.buttonChromeRims", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.gallery_button_chrome_rims", "Button edge rims"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_button_chrome_rims", "Shows a quiet outline on idle and selected chrome buttons so muted fills stay readable. Hover highlight still works when off."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.EnableGalleryButtonChromeRims,
                SetBool = v => {
                    VPBConfig.Instance.EnableGalleryButtonChromeRims = v;
                    try { UI.ApplyGalleryButtonChromeRimsGlobally(); } catch { }
                    try { VPBConfig.Instance.TriggerChange(); } catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.elementCornerRadius", GroupKey = "visuals",
                Label = VPBTranslation.T("settings.gallery_element_corner_radius", "Element corner radius"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_element_corner_radius", "Corner roundness as a fraction of each element's shorter side (0.05 = subtle, 0.5 = maximum)."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.GalleryElementCornerRadiusFraction,
                SetFloat = v => {
                    VPBConfig.Instance.GalleryElementCornerRadiusFraction = VPBConfig.ClampGalleryElementCornerRadiusFraction(v);
                    ApplyGalleryElementCornerRadiusFromSettings();
                },
                Min = VPBConfig.MinGalleryElementCornerRadiusFraction,
                Max = VPBConfig.MaxGalleryElementCornerRadiusFraction,
                Step = 0.01f, Decimals = 2,
                RowVisible = GalleryElementCornerRadiusSubSettingsVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "visuals.showSideButtons", GroupKey = "visuals", Label = VPBTranslation.T("settings.show_side_buttons", "Show Side Buttons"),
                Tooltip = VPBTranslation.T("settings.tip.show_side_buttons", "Auto = one rail on the free edge (flip from title …). Left / Right / Both override."),
                ControlType = InternalSettingControlType.Cycle, Options = new [] { "Auto", "Left", "Right", "Both" },
                GetString = () => VPBConfig.NormalizeShowSideButtons(VPBConfig.Instance.ShowSideButtons),
                SetString = v => { VPBConfig.Instance.ShowSideButtons = VPBConfig.NormalizeShowSideButtons(v); VPBConfig.Instance.TriggerChange(); }
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
                Tooltip = VPBTranslation.T("settings.tip.enable_drag_drop", "Off by default. Desktop: click-drag a row (~22px) onto an atom/scene (scroll with wheel/scrollbar). VR: hold then drag. Disabled while Hold-to-Launch is on."),
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
                Key = "interaction.tryOnMode", GroupKey = "interaction", Label = VPBTranslation.T("settings.try_on_mode", "Try-On Mode"),
                Tooltip = VPBTranslation.T("settings.tip.try_on_mode", "When ON, applying clothing/hair/skin/morphs/appearance/pose/plugin presets is non-destructive: a Keep / Compare (hold to peek) / Revert bar appears so you can preview before committing. Works in desktop and VR."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.TryOnModeEnabled,
                SetBool = v => { VPBConfig.Instance.TryOnModeEnabled = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "interaction.verticalMoveKeys", GroupKey = "interaction", Label = VPBTranslation.T("settings.vertical_move_keys", "Vertical move keys (E/C)"),
                Tooltip = VPBTranslation.T("settings.tip.vertical_move_keys", "When ON, press E to move up and C to move down in the world, complementing WASD. Ignored while typing in a text field."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.VerticalMoveKeysEnabled,
                SetBool = v => { VPBConfig.Instance.VerticalMoveKeysEnabled = v; VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "interaction.dragHoldSec", GroupKey = "interaction", Label = VPBTranslation.T("settings.drag_hold_threshold", "VR hold duration (s)"),
                Tooltip = VPBTranslation.T("settings.tip.drag_hold_threshold", "VR only: how long to hold before an item drag starts (min " + VPBConfig.DragHoldThresholdMin.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s). Desktop ignores this — click-drag after a short move starts drag-and-drop."),
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
                Tooltip = VPBTranslation.T("settings.tip.appearance_clothing", "Full look, keep outfit, outfit only, or merge outfit (pick items to add on top)."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "replace", "keep", "clothingonly", "mergeoutfit" },
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
                Tooltip = VPBTranslation.T("settings.tip.desktop.fixed_auto_hide_seconds", "Seconds pane must stay idle (pointer outside, no text focus / search chrome) before auto-hide collapses (Desktop fixed mode)."),
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
                Key = "desktop.layoutPresets", GroupKey = "desktop",
                Label = VPBTranslation.T("settings.layout_preset.open", "Layout presets…"),
                Tooltip = VPBTranslation.T("settings.tip.layout_preset.open",
                    "Save and restore window arrangements: docked panes, floating panes, open float windows, category and filters. VR and desktop layouts are kept separate. Alt+L."),
                ControlType = InternalSettingControlType.Button,
                OnAction = () => { ToggleLayoutPresetsFloat(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "desktop.layoutRevertSeconds", GroupKey = "desktop",
                Label = VPBTranslation.T("settings.layout_preset.revert_seconds", "Layout undo bar (s)"),
                Tooltip = VPBTranslation.T("settings.tip.layout_preset.revert_seconds",
                    "How long the Revert bar stays up after a layout is applied."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance.LayoutPresetRevertBarSeconds,
                SetFloat = v => {
                    VPBConfig.Instance.LayoutPresetRevertBarSeconds = Mathf.Clamp(v, 2f, 30f);
                    try { VPBConfig.Instance.Save(false, true); } catch { }
                },
                Min = 2f, Max = 30f, Step = 1f, Decimals = 0
            });

            defs.Add(new InternalSettingDefinition {
                Key = "desktop.layoutSuggestOnModeSwitch", GroupKey = "desktop",
                Label = VPBTranslation.T("settings.layout_preset.suggest", "Offer layout on VR/desktop switch"),
                Tooltip = VPBTranslation.T("settings.tip.layout_preset.suggest",
                    "When the mode changes, offer that mode's startup layout instead of applying it silently."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.LayoutPresetSuggestOnModeSwitch,
                SetBool = v => { VPBConfig.Instance.LayoutPresetSuggestOnModeSwitch = v; VPBConfig.Instance.TriggerChange(); }
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
                Tooltip = VPBTranslation.T("settings.tip.initial_gallery_category", "Category when VaM starts (cold launch). Close/reopen gallery during the same session restores the last category you used. Choose LastUsed to restore last category even on cold launch."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" },
                GetString = () => VPBConfig.NormalizeInitialGalleryCategory(VPBConfig.Instance.InitialGalleryCategory),
                SetString = v => { VPBConfig.Instance.InitialGalleryCategory = v; VPBConfig.Instance.TriggerChange(); }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "lists.defaultLeft", GroupKey = "lists", Label = VPBTranslation.T("settings.gallery_default_left_panel", "Left side list (default)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_left_panel", "Left list / Import on cold VaM launch. During the same session, Close/reopen restores the rails you last had open."),
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
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_right_panel", "Right list / Import on cold VaM launch. During the same session, Close/reopen restores the rails you last had open."),
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
                Tooltip = VPBTranslation.T("settings.tip.gallery_default_user_tag_mode", "Mode when opening the User Tags side panel: filter grid by tags, or apply tags to selection. Untagged only also available from title-bar Filter menu."),
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
                Key = "lists.springScrollButton", GroupKey = "lists",
                Label = VPBTranslation.T("settings.spring_scroll_button", "Spring scroll button"),
                Tooltip = VPBTranslation.T("settings.tip.spring_scroll_button", "Shows the floating spring-scroll drag control next to the scrollbar. Off / Desktop Only / VR Only / Desktop & VR."),
                ControlType = InternalSettingControlType.Cycle,
                Options = new[] { "Off", "Desktop Only", "VR Only", "Desktop & VR" },
                GetString = () => VPBConfig.NormalizeSpringScrollButtonMode(VPBConfig.Instance.SpringScrollButtonMode),
                SetString = v =>
                {
                    VPBConfig.Instance.SpringScrollButtonMode = VPBConfig.NormalizeSpringScrollButtonMode(v);
                    try { VPBConfig.Instance.Save(false); } catch { }
                    VPBConfig.Instance.TriggerChange();
                    try { ApplySpringScrollButtonFromConfig(); } catch { }
                }
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
                Tooltip = VPBTranslation.T("settings.tip.gallery_hide_creator_side_buttons", "Does not create side-rail Creator buttons. Use title-bar creator control only. Closes open creator side lists."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryHideCreatorSideButtons,
                SetBool = v => {
                    VPBConfig.Instance.GalleryHideCreatorSideButtons = v;
                    try { VPBConfig.Instance.Save(false); } catch { }
                    // Presence sync once (create or destroy). Do not ToggleChange-layout thrash chips.
                    try { SyncCreatorSideRailPresence(); } catch { }
                    try { UpdateSideButtonPositions(); } catch { }
                    try { VPBConfig.Instance.TriggerChange(); } catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "lists.showCategoryIcons", GroupKey = "lists",
                Label = VPBTranslation.T("settings.gallery_show_category_icons", "Show category icons"),
                Tooltip = VPBTranslation.T("settings.tip.gallery_show_category_icons", "Shows per-category icons on the left of each row in side-rail Category mode."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryShowCategoryIcons,
                SetBool = v => {
                    VPBConfig.Instance.GalleryShowCategoryIcons = v;
                    try { VPBConfig.Instance.Save(false); } catch { }
                    VPBConfig.Instance.TriggerChange();
                    try { UpdateTabs(); } catch { }
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
                    GalleryFileListSnapshotCache.Clear();
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
                Key = "helpers.blockInGameMessages", GroupKey = "helpers",
                Label = VPBTranslation.T("settings.helpers_block_ingame_messages", "Block in-game messages"),
                Tooltip = VPBTranslation.T("settings.tip.helpers_block_ingame_messages", "Suppress VaM in-game error and warning notification popups. Off = show all; VR Only = suppress in VR; Desktop Only = suppress on desktop; Both = always suppress."),
                ControlType = InternalSettingControlType.Cycle,
                Options = new[] { "Off", "VR Only", "Desktop Only", "Both" },
                GetString = () => VPBConfig.Instance?.BlockInGameMessages ?? "Off",
                SetString = v => {
                    if (VPBConfig.Instance != null) VPBConfig.Instance.BlockInGameMessages = v ?? "Off";
                    try { VPBConfig.Instance?.Save(false); } catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "helpers.hideMissingDependencyLogs", GroupKey = "helpers",
                Label = VPBTranslation.T("settings.helpers_hide_missing_dep_logs", "Hide missing dependency logs"),
                Tooltip = VPBTranslation.T("settings.tip.helpers_hide_missing_dep_logs", "Suppress VaM \"Missing addon package … depends on …\" messages in the in-game error log and BepInEx console. Turn off to see missing dependency warnings again."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance?.HideMissingDependencyLogs ?? true,
                SetBool = v => {
                    if (VPBConfig.Instance != null) VPBConfig.Instance.HideMissingDependencyLogs = v;
                    try { VPBConfig.Instance?.Save(false); } catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "helpers.clearInGameLogsOnSceneLaunch", GroupKey = "helpers",
                Label = VPBTranslation.T("settings.helpers_clear_logs_on_scene_launch", "Clear logs on scene launch"),
                Tooltip = VPBTranslation.T("settings.tip.helpers_clear_logs_on_scene_launch", "Clear VaM in-game error and message logs when a full scene is loaded. Merge loads are not cleared. Logs from the previous scene otherwise stay until you clear them manually."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance?.ClearInGameLogsOnSceneLaunch ?? false,
                SetBool = v => {
                    if (VPBConfig.Instance != null) VPBConfig.Instance.ClearInGameLogsOnSceneLaunch = v;
                    try { VPBConfig.Instance?.Save(false); } catch { }
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
                Key = "search.sourceFilterScope", GroupKey = "search",
                Label = VPBTranslation.T("settings.gallery_source_filter_scope", "Source filter per category"),
                Tooltip = VPBTranslation.T(
                    "settings.tip.gallery_source_filter_scope",
                    "Independent (default): Scenes can be .var while Character Presets stay Local. Synced: one All/Local/.var filter for every category."),
                ControlType = InternalSettingControlType.Cycle,
                Options = new[] { "Independent", "Synced" },
                GetString = () => VPBConfig.FormatGallerySourceFilterScopeForSettings(VPBConfig.Instance.GallerySourceFilterIndependent),
                SetString = v =>
                {
                    ApplySourceFilterScopeFromSettings(v);
                    VPBConfig.Instance.TriggerChange();
                }
            });

            defs.Add(new InternalSettingDefinition {
                Key = "hover.mode", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_mode", "Hover preview"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_mode", "Show larger image preview while hovering items. Position is fixed (drag the placeholder here to place it)."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Off", "List", "Grid", "Both" },
                GetString = () => VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode),
                SetString = v => { VPBConfig.Instance.GalleryHoverPreviewMode = VPBConfig.NormalizeHoverPreviewMode(v); VPBConfig.Instance.TriggerChange(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "hover.randomButtonPreview", GroupKey = "hover",
                Label = VPBTranslation.T("settings.hover_random_preview", "Random preview"),
                Tooltip = VPBTranslation.T("settings.tip.hover_random_preview", "On (default): pointing at a desktop quick-menu random button shows a card above that pane. Gallery toolbox Random greys the grid and shows a centered preview. Wrist-watch random buttons keep the watch card. Click launches the item you saw. Scroll wheel while hovering re-rolls the preview (same as leave and re-enter). Off: random buttons still launch, with no hover card."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuRandomHoverPreview,
                SetBool = v => {
                    VPBConfig.Instance.QuickMenuRandomHoverPreview = v;
                    try { TboxSyncRandomPreviewLiveScale(); } catch { }
                    try
                    {
                        VamHookPlugin p = VamHookPlugin.singleton;
                        if (p != null) p.QuickMenuOnRandomPreviewEnabledChanged();
                    }
                    catch { }
                    VPBConfig.Instance.TriggerChange();
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "hover.size", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_size", "Hover preview size"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_size", "Size in pixels of square hover preview."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryListHoverPreviewSize,
                SetFloat = v => { VPBConfig.Instance.GalleryListHoverPreviewSize = v; VPBConfig.Instance.TriggerChange(); },
                Min = VPBConfig.GalleryHoverPreviewSizeMin, Max = VPBConfig.GalleryHoverPreviewSizeMax, Step = 10f, Decimals = 0,
                RowVisible = () => VPBConfig.Instance != null && !string.Equals(VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode), "Off", StringComparison.OrdinalIgnoreCase)
            });
            defs.Add(new InternalSettingDefinition {
                Key = "hover.offsetX", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_offset_x", "Hover preview X"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_offset_x", "Horizontal position. Prefer drag the on-screen placeholder while Settings is open."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryListHoverPreviewOffsetX,
                SetFloat = v => { VPBConfig.Instance.GalleryListHoverPreviewOffsetX = v; VPBConfig.Instance.TriggerChange(); },
                Min = -4000f, Max = 4000f, Step = 25f, Decimals = 0, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance != null && !string.Equals(VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode), "Off", StringComparison.OrdinalIgnoreCase)
            });
            defs.Add(new InternalSettingDefinition {
                Key = "hover.offsetY", GroupKey = "hover", Label = VPBTranslation.T("settings.hover_preview_offset_y", "Hover preview Y"),
                Tooltip = VPBTranslation.T("settings.tip.hover_preview_offset_y", "Vertical position. Prefer drag the on-screen placeholder while Settings is open."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.GalleryListHoverPreviewOffsetY,
                SetFloat = v => { VPBConfig.Instance.GalleryListHoverPreviewOffsetY = v; VPBConfig.Instance.TriggerChange(); },
                Min = -4000f, Max = 4000f, Step = 25f, Decimals = 0, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance != null && !string.Equals(VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode), "Off", StringComparison.OrdinalIgnoreCase)
            });

            defs.Add(new InternalSettingDefinition {
                Key = "grid.enabled", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_labels_enabled", "Always-on grid labels"),
                Tooltip = VPBTranslation.T("settings.tip.grid_labels_enabled", "Show labels under grid thumbnails: cleaned package/leaf name left, creator muted right. Creator prefix stripped from leaf stems; strip is one line when leaf matches package. Hover tip keeps full caption + path."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryGridLabelsEnabled,
                SetBool = v => { VPBConfig.Instance.GalleryGridLabelsEnabled = v; RebuildGridLayout(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "grid.hoverBadges", GroupKey = "grid", Label = VPBTranslation.T("settings.grid_hover_badges", "Hover rating digit"),
                Tooltip = VPBTranslation.T("settings.tip.grid_hover_badges", "Show colored rating digit on grid hover for quick rate. Off keeps dense grids faster."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryGridHoverBadgesEnabled,
                SetBool = v => { VPBConfig.Instance.GalleryGridHoverBadgesEnabled = v; VPBConfig.Instance.TriggerChange(); }
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
                Tooltip = VPBTranslation.T("settings.tip.grid_border_inward_square", "List rows: when enabled, draw hover/selection border inward. Grid cells always inset (outward rims bleed into neighbors when spacing is 0)."),
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
                Label = VPBTranslation.T("settings.scan_wl_border_enabled", "Legacy: persistent full-cell rim"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_border_enabled", "Optional legacy cue. Off by default — gallery uses the W badge for whitelist status (teal = saved/folder, amber ring = temporary). Full-cell rims fight hover/selection borders."),
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
                Label = VPBTranslation.T("settings.scan_wl_temp_border_enabled", "Legacy: temporary full-cell rim"),
                Tooltip = VPBTranslation.T("settings.tip.scan_wl_temp_border_enabled", "Optional legacy cue. Off by default — temporary whitelist uses the W badge with an amber outline ring instead of painting the whole cell."),
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
                Key = "vr.floatsOnly", GroupKey = "vr",
                Label = VPBTranslation.T("settings.gallery.floats_only", "Floating windows only (hide gallery pane)"),
                Tooltip = VPBTranslation.T("settings.tip.gallery.floats_only", "Hide the gallery pane itself — including the one anchored to the VaM menu in VR — while Settings, Plugins, Quick Filters and Import stay on screen. Opening the gallery turns this back off."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.GalleryFloatsOnlyMode,
                SetBool = v => SetFloatsOnlyAllPanes(v)
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.anchor", GroupKey = "vr", Label = VPBTranslation.T("settings.gallery.vam_menu_anchor", "Anchor to VaM Menu in VR"),
                Tooltip = VPBTranslation.T("settings.tip.gallery.vam_menu_anchor", "Anchor pane relative to VaM menu in VR."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.GalleryAnchorToVamMenu,
                SetBool = v => { VPBConfig.Instance.GalleryAnchorToVamMenu = v; VPBConfig.Instance.TriggerChange(); ResetFollowOffsets(); }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.anchorTilt", GroupKey = "vr",
                Label = VPBTranslation.T("settings.gallery.vam_menu_anchor_tilt", "VR menu angle"),
                Tooltip = VPBTranslation.T("settings.tip.gallery.vam_menu_anchor_tilt", "When anchored to VaM menu in VR, tip gallery pane toward you. Pivot is bottom edge (0–20°). Gallery pane only."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.ClampGalleryVrMenuAnchorTiltDeg(VPBConfig.Instance.GalleryVrMenuAnchorTiltDeg),
                SetFloat = v =>
                {
                    VPBConfig.Instance.GalleryVrMenuAnchorTiltDeg = VPBConfig.ClampGalleryVrMenuAnchorTiltDeg(v);
                    VPBConfig.Instance.TriggerChange();
                },
                Min = VPBConfig.MinGalleryVrMenuAnchorTiltDeg, Max = VPBConfig.MaxGalleryVrMenuAnchorTiltDeg, Step = 1f, Decimals = 0,
                RowVisible = () => VPBConfig.Instance != null && VPBConfig.Instance.GalleryAnchorToVamMenu
            });

            // VR wrist watch rows deliberately do not TriggerChange: the watch re-reads every one of
            // these from config each frame (QuickMenuPullWatchSettings), while a config-changed
            // broadcast costs a full gallery chrome/layout pass on every panel — once per slider step
            // while dragging, which is the watch-settings frame-rate collapse. Dependent RowVisible
            // gates still re-evaluate via RefreshInternalSettingsListRows on the toggle/button paths.
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchVisible", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_visible", "Show VR wrist watch"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_visible", "Wrist face: core HUD actions in the center, 8 assignable buttons on the sides, pager for watch pages only (HUD grid pages stay). Look at inner wrist to show (Glance)."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchVisible,
                SetBool = v =>
                {
                    VPBConfig.Instance.QuickMenuVrWatchVisible = v;
                    UpdateFooterVrWatchState();
                    try
                    {
                        if (VamHookPlugin.singleton != null)
                            VamHookPlugin.singleton.QuickMenuOnWatchVisibleToggled(v);
                    }
                    catch { }
                }
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchMode", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_mode", "Watch hand"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_mode", "Which controller the watch rides. Left/Right stay on that hand even if the other controller is used. Opposite = other hand than the one that opened the VaM menu. Switch-hand on the watch is sticky this session unless Remember hand is on."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Left only", "Right only", "Opposite to menu", "Same hand" },
                GetString = () => VPBConfig.Instance.QuickMenuVrWatchMode, SetString = v => { VPBConfig.Instance.QuickMenuVrWatchMode = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchShowWhen", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_show_when", "Show watch when"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_show_when", "Glance: inner wrist toward headset. Menu: while VaM menu is open. Always: keep last hand even with menu closed. Pin on the watch keeps it up this session."),
                ControlType = InternalSettingControlType.Cycle, Options = new[] { "Glance", "Menu", "Always" },
                GetString = () => VPBConfig.Instance.QuickMenuVrWatchShowWhen, SetString = v => { VPBConfig.Instance.QuickMenuVrWatchShowWhen = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchRememberHand", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_remember_hand", "Remember switched hand"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_remember_hand", "When on, Switch Watch Hand writes Left/Right into settings so it survives restart. When off, the flip still sticks this session (other controller will not steal the watch)."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchRememberHand,
                SetBool = v => { VPBConfig.Instance.QuickMenuVrWatchRememberHand = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchFaceUser", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_face_user", "Watch faces player"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_face_user", "Watch face billboards toward the eye. Glance still uses wrist angle. Off = locked to wrist, and the tilt X/Y/Z sliders below let you aim it by hand."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchFaceUser,
                SetBool = v => { VPBConfig.Instance.QuickMenuVrWatchFaceUser = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            // Manual tilt sits directly under the toggle that reveals it: with billboarding off the
            // face is welded to a fixed wrist rotation, and these three are the only way to aim it.
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchRotX", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_rot_x", "Watch tilt X (pitch)"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_rotation", "Degrees added to the face's own axes on top of the wrist rotation. X tips the top toward or away from you, Y swings it left/right, Z rolls it. Not mirrored when the watch changes hands. Hidden while Watch faces player is on, which aims the face for you."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchFaceRotation.x,
                SetFloat = v => { var r = VPBConfig.Instance.QuickMenuVrWatchFaceRotation; r.x = VPBConfig.ClampWatchFaceRotationDeg(v); VPBConfig.Instance.QuickMenuVrWatchFaceRotation = r; },
                Min = -VPBConfig.QuickMenuVrWatchFaceRotationMaxDeg, Max = VPBConfig.QuickMenuVrWatchFaceRotationMaxDeg,
                Step = 1f, Decimals = 0, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible && !VPBConfig.Instance.QuickMenuVrWatchFaceUser
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchRotY", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_rot_y", "Watch tilt Y (yaw)"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_rotation", "Degrees added to the face's own axes on top of the wrist rotation. X tips the top toward or away from you, Y swings it left/right, Z rolls it. Not mirrored when the watch changes hands. Hidden while Watch faces player is on, which aims the face for you."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchFaceRotation.y,
                SetFloat = v => { var r = VPBConfig.Instance.QuickMenuVrWatchFaceRotation; r.y = VPBConfig.ClampWatchFaceRotationDeg(v); VPBConfig.Instance.QuickMenuVrWatchFaceRotation = r; },
                Min = -VPBConfig.QuickMenuVrWatchFaceRotationMaxDeg, Max = VPBConfig.QuickMenuVrWatchFaceRotationMaxDeg,
                Step = 1f, Decimals = 0, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible && !VPBConfig.Instance.QuickMenuVrWatchFaceUser
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchRotZ", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_rot_z", "Watch tilt Z (roll)"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_rotation", "Degrees added to the face's own axes on top of the wrist rotation. X tips the top toward or away from you, Y swings it left/right, Z rolls it. Not mirrored when the watch changes hands. Hidden while Watch faces player is on, which aims the face for you."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchFaceRotation.z,
                SetFloat = v => { var r = VPBConfig.Instance.QuickMenuVrWatchFaceRotation; r.z = VPBConfig.ClampWatchFaceRotationDeg(v); VPBConfig.Instance.QuickMenuVrWatchFaceRotation = r; },
                Min = -VPBConfig.QuickMenuVrWatchFaceRotationMaxDeg, Max = VPBConfig.QuickMenuVrWatchFaceRotationMaxDeg,
                Step = 1f, Decimals = 0, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible && !VPBConfig.Instance.QuickMenuVrWatchFaceUser
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchScale", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_scale", "Watch scale"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_scale", "Size of the watch face. Default 0.75. 1.0 = same meters-per-pixel as HUD (world-scale compensated)."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchScaleMul,
                SetFloat = v => { VPBConfig.Instance.QuickMenuVrWatchScaleMul = v; },
                Min = VPBConfig.QuickMenuVrWatchScaleMulMin, Max = VPBConfig.QuickMenuVrWatchScaleMulMax, Step = 0.05f, Decimals = 2,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchExpanded", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_expanded", "Full watch face"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_expanded", "Off (default): compact face — 6 assignable buttons and a small chrome row, about half the angular size. On: full face — HUD core row in the centre, 8 side-rail buttons and the page pager. The chevron button on the watch toggles this too."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchExpanded,
                SetBool = v => { VPBConfig.Instance.QuickMenuVrWatchExpanded = v; VPBConfig.Instance.QuickMenuVrWatchCollapsed = false; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchLabels", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_labels", "Button labels"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_labels", "Draw a short text label under each watch icon so actions can be read without hovering. Makes the face taller."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchLabels,
                SetBool = v => { VPBConfig.Instance.QuickMenuVrWatchLabels = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchFreeze", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_freeze", "Hold still when reaching"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_freeze", "Freezes the face in place once the other controller comes within 14cm, so buttons stop moving while you aim at them. Releases at 20cm, or when you move the watch arm away."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchFreezeOnApproach,
                SetBool = v => { VPBConfig.Instance.QuickMenuVrWatchFreezeOnApproach = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchGripPin", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_grip_pin", "Grip to pin / unpin"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_grip_pin", "Squeeze and hold the grip on the hand wearing the watch to take it off and leave it hanging in place; squeeze again to put it back on. Pinning only triggers while the face is turned toward you, so it will not fire when you grab something else."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchGripPin,
                SetBool = v => { VPBConfig.Instance.QuickMenuVrWatchGripPin = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchHoldConfirm", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_hold_confirm", "Hold to confirm"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_hold_confirm", "Remove All Clothing, Remove All Hair and Close All need a 0.4s press-and-hold instead of a single tap."),
                ControlType = InternalSettingControlType.Toggle, GetBool = () => VPBConfig.Instance.QuickMenuVrWatchHoldConfirm,
                SetBool = v => { VPBConfig.Instance.QuickMenuVrWatchHoldConfirm = v; },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchShoulder", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_shoulder", "Angle toward reaching hand"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_shoulder", "0 (default) faces the face squarely at your eyes. Raising it tilts the face toward the opposite shoulder so the pressing controller meets it straight on — at 1.0 it looks angled away from you."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchShoulderBlend,
                SetFloat = v => { VPBConfig.Instance.QuickMenuVrWatchShoulderBlend = v; },
                Min = 0f, Max = 1f, Step = 0.05f, Decimals = 2,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible && VPBConfig.Instance.QuickMenuVrWatchFaceUser
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchGlanceDwell", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_glance_dwell", "Glance dwell"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_glance_dwell", "Seconds the wrist must stay turned toward you before the face appears. Raise it if the watch pops up while you gesture; 0 shows instantly."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchGlanceDwell,
                SetFloat = v => { VPBConfig.Instance.QuickMenuVrWatchGlanceDwell = v; },
                Min = 0f, Max = VPBConfig.QuickMenuVrWatchGlanceDwellMax, Step = 0.02f, Decimals = 2,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchToward", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_toward", "Watch pull toward you"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_toward", "Meters pulled from the controller toward the eye. Drag the watch bezel in VR to place it."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchTowardUserDist,
                SetFloat = v => { VPBConfig.Instance.QuickMenuVrWatchTowardUserDist = v; },
                Min = -0.5f, Max = 0.5f, Step = 0.01f, Decimals = 2,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchOffsetX", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_offset_x", "Watch offset X"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_offset", "Controller-local offset. X flips on the right hand so the face sits on the inner wrist. Drag the bezel in VR to calibrate."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchOffset.x,
                SetFloat = v => { var o = VPBConfig.Instance.QuickMenuVrWatchOffset; o.x = v; VPBConfig.Instance.QuickMenuVrWatchOffset = o; },
                Min = -0.15f, Max = 0.15f, Step = 0.005f, Decimals = 3, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchOffsetY", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_offset_y", "Watch offset Y"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_offset", "Controller-local offset. X flips on the right hand so the face sits on the inner wrist. Drag the bezel in VR to calibrate."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchOffset.y,
                SetFloat = v => { var o = VPBConfig.Instance.QuickMenuVrWatchOffset; o.y = v; VPBConfig.Instance.QuickMenuVrWatchOffset = o; },
                Min = -0.15f, Max = 0.15f, Step = 0.005f, Decimals = 3, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchOffsetZ", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_offset_z", "Watch offset Z"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_offset", "Controller-local offset. X flips on the right hand so the face sits on the inner wrist. Drag the bezel in VR to calibrate."),
                ControlType = InternalSettingControlType.Slider, GetFloat = () => VPBConfig.Instance.QuickMenuVrWatchOffset.z,
                SetFloat = v => { var o = VPBConfig.Instance.QuickMenuVrWatchOffset; o.z = v; VPBConfig.Instance.QuickMenuVrWatchOffset = o; },
                Min = -0.15f, Max = 0.15f, Step = 0.005f, Decimals = 3, AllowNegative = true,
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
            });
            defs.Add(new InternalSettingDefinition {
                Key = "vr.watchResetPose", GroupKey = "vr", Label = VPBTranslation.T("settings.vr.watch_reset_pose", "Reset watch pose"),
                Tooltip = VPBTranslation.T("settings.tip.vr.watch_reset_pose", "Restore default offset, rotation, scale, pull, and face-user."),
                ControlType = InternalSettingControlType.Button,
                OnAction = () => { VPBConfig.Instance.ResetVrWatchPose(); },
                RowVisible = () => VPBConfig.Instance.QuickMenuVrWatchVisible
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
            defs.Add(new InternalSettingDefinition {
                Key = "performance.sceneAtomCacheLimit", GroupKey = "performance",
                Label = VPBTranslation.T("settings.scene_atom_cache_limit", "Scene Import Cache Limit (GB)"),
                Tooltip = VPBTranslation.T("settings.tip.scene_atom_cache_limit", "Maximum cached Scene Import Person data. Oldest cached scenes are removed first."),
                ControlType = InternalSettingControlType.Slider,
                GetFloat = () => VPBConfig.Instance == null
                    ? 1f
                    : VPBConfig.ClampSceneImportCacheLimitMb(VPBConfig.Instance.SceneImportCacheLimitMb) / 1024f,
                SetFloat = v => {
                    if (VPBConfig.Instance == null) return;
                    VPBConfig.Instance.SceneImportCacheLimitMb = VPBConfig.ClampSceneImportCacheLimitMb(Mathf.RoundToInt(v * 1024f));
                },
                Min = 0.25f, Max = 8f, Step = 0.25f, Decimals = 2,
                DeferLiveApply = true
            });
            defs.Add(new InternalSettingDefinition {
                Key = "performance.sceneAtomCache", GroupKey = "performance",
                Label = VPBTranslation.T("settings.scene_atom_cache", "Clear Scene Import Cache"),
                Tooltip = VPBTranslation.T("settings.tip.scene_atom_cache", "Clear cached Person atom data used by Scene Import, then compact VPB database. Cache rebuilds when scenes are imported again."),
                ControlType = InternalSettingControlType.Button,
                OnAction = RequestSceneAtomCacheCleanup,
                ActionEnabled = () => !_sceneAtomCacheCleanupRunning
            });

            BindSettingsRowFactoryDefaults(defs);
            return defs;
        }

        private static string GetUpdaterCheckLabel(VpbUpdaterService updater)
        {
            if (updater.IsBusy)
                return updater.StatusMessage ?? VPBTranslation.T("settings.updater.checking", "Checking...");
            if (updater.HasPendingUpdate)
            {
                string av = updater.AvailableVersion ?? "?";
                return string.Format(
                    VPBTranslation.T("settings.updater.updating", "Updating {0} → {1}  (restart VaM)"),
                    PluginVersionInfo.Version, av);
            }
            if (updater.Status == VpbUpdateStatus.UpToDate)
                return updater.StatusMessage ?? VPBTranslation.T("settings.updater.up_to_date", "Up to date");
            if (updater.Status == VpbUpdateStatus.Error)
                return updater.StatusMessage ?? VPBTranslation.T("settings.updater.error", "Update error");
            return string.Format(
                VPBTranslation.T("settings.updater.check", "Check for Updates (VPB {0})"),
                PluginVersionInfo.Version);
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
                SceneImportCacheLimitMb = VPBConfig.Instance.SceneImportCacheLimitMb,
                GalleryDetailStripSideInfoEnabled = VPBConfig.Instance.GalleryDetailStripSideInfoEnabled,
                GalleryDetailStripThumbOnRight = VPBConfig.Instance.GalleryDetailStripThumbOnRight,
                GalleryDetailStripHeightRef = VPBConfig.Instance.GalleryDetailStripHeightRef,
                GalleryOpacity = VPBConfig.Instance.GalleryOpacity,
                SideButtonScaleVR = VPBConfig.Instance.SideButtonScaleVR,
                SideButtonScaleDesktop = VPBConfig.Instance.SideButtonScaleDesktop,
                InnerPaneScaleVR = VPBConfig.Instance.InnerPaneScaleVR,
                InnerPaneScaleDesktop = VPBConfig.Instance.InnerPaneScaleDesktop,
                EnableButtonGaps = VPBConfig.Instance.EnableButtonGaps,
                EnableGalleryElementRounding = VPBConfig.Instance.EnableGalleryElementRounding,
                EnableGalleryButtonChromeRims = VPBConfig.Instance.EnableGalleryButtonChromeRims,
                GalleryElementCornerRadiusFraction = VPBConfig.Instance.GalleryElementCornerRadiusFraction,
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
                VerticalMoveKeysEnabled = VPBConfig.Instance.VerticalMoveKeysEnabled,
                ShortcutsRequireWindowFocus = VPBConfig.Instance.ShortcutsRequireWindowFocus,
                ShortcutsNeedVisiblePane = VPBConfig.Instance.ShortcutsNeedVisiblePane,
                CategoryNumberKeysEnabled = VPBConfig.Instance.CategoryNumberKeysEnabled,
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
                SpringScrollButtonMode = VPBConfig.NormalizeSpringScrollButtonMode(VPBConfig.Instance.SpringScrollButtonMode),
                GalleryVrThumbstickScrollEnabled = VPBConfig.Instance.GalleryVrThumbstickScrollEnabled,
                GalleryHideCreatorSideButtons = VPBConfig.Instance.GalleryHideCreatorSideButtons,
                GalleryShowCategoryIcons = VPBConfig.Instance.GalleryShowCategoryIcons,
                GalleryConsolidateCreatorNames = VPBConfig.Instance.GalleryConsolidateCreatorNames,
                PluginGalleryGridThumbnails = VPBConfig.Instance.PluginGalleryGridThumbnails,
                PluginGalleryCategoryLabelsOnly = VPBConfig.Instance.PluginGalleryCategoryLabelsOnly,
                GalleryThumbPlaceholderLabelsEnabled = VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled,
                GalleryThumbPlaceholderSizeScale = VPBConfig.Instance.GetGalleryThumbPlaceholderSizeScale(),
                GalleryListNamesLegacyFileName = VPBConfig.Instance.GalleryListNamesLegacyFileName,
                GalleryHoverPreviewMode = VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode),
                QuickMenuRandomHoverPreview = VPBConfig.Instance.QuickMenuRandomHoverPreview,
                GalleryListHoverPreviewSize = VPBConfig.Instance.GalleryListHoverPreviewSize,
                GalleryListHoverPreviewOffsetX = VPBConfig.Instance.GalleryListHoverPreviewOffsetX,
                GalleryListHoverPreviewOffsetY = VPBConfig.Instance.GalleryListHoverPreviewOffsetY,
                GalleryGridLabelsEnabled = VPBConfig.Instance.GalleryGridLabelsEnabled,
                GalleryGridLabelsAutoHideAtHighDensity = VPBConfig.Instance.GalleryGridLabelsAutoHideAtHighDensity,
                GalleryGridHoverBadgesEnabled = VPBConfig.Instance.GalleryGridHoverBadgesEnabled,
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
                GalleryFloatsOnlyMode = VPBConfig.Instance.GalleryFloatsOnlyMode,
                GalleryAnchorToVamMenu = VPBConfig.Instance.GalleryAnchorToVamMenu,
                LayoutPresetRevertBarSeconds = VPBConfig.Instance.LayoutPresetRevertBarSeconds,
                LayoutPresetSuggestOnModeSwitch = VPBConfig.Instance.LayoutPresetSuggestOnModeSwitch,
                GalleryVrMenuAnchorTiltDeg = VPBConfig.ClampGalleryVrMenuAnchorTiltDeg(VPBConfig.Instance.GalleryVrMenuAnchorTiltDeg),
                GalleryCategoryQuickOrder = VPBConfig.Instance.GalleryCategoryQuickOrder ?? "",
                GalleryCategoryQuickSwitchHidden = VPBConfig.Instance.GalleryCategoryQuickSwitchHidden ?? "",
                HiddenCategories = VPBConfig.Instance.HiddenCategories != null
                    ? new HashSet<string>(VPBConfig.Instance.HiddenCategories, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                BlockInGameMessages = VPBConfig.Instance.BlockInGameMessages ?? "Off",
                HideMissingDependencyLogs = VPBConfig.Instance.HideMissingDependencyLogs,
                ClearInGameLogsOnSceneLaunch = VPBConfig.Instance.ClearInGameLogsOnSceneLaunch
            };
            CapturePluginSettingsIntoSnapshot(snap);
            return snap;
        }

        private void EnsureInternalSettingsSession()
        {
            if (internalSettingsSessionActive) return;
            internalSettingsListRowHeightSession = GalleryUiDesignTokens.SettingsFloatRowHeightRef;
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

        /// <summary>Open gallery Settings floating window (title bar gear / shortcuts). Toggle: Save+close when already open.</summary>
        public void OpenSettingsSideTab()
        {
            if (IsSettingsPanelOpen())
            {
                ExitInternalSettingsMode(true);
                return;
            }
            try { CancelPluginHotkeyCapture(false); } catch { }
            EnsureSettingsFloatBuilt();
            ShowSettingsFloat();
            ResolveSettingsLastGroupOnOpen();
            EnsureInternalSettingsSession();
            try { SyncSettingsSideSearchInputFromFilter(); } catch { }
            RefreshInternalSettingsListRows(false);
            try { TryShowBaMigrationPromptOnSettingsEnter(); } catch { }
        }

        /// <summary>Open gallery Settings on a specific category tab (e.g. updater).</summary>
        public void OpenSettingsGroup(string groupKey)
        {
            _settingsGroupExplicit = true;
            SetActiveSettingsGroup(groupKey);
            try { CancelPluginHotkeyCapture(false); } catch { }
            if (!IsSettingsPanelOpen())
                OpenSettingsSideTab();
            else
                RefreshInternalSettingsListRows(true);
        }

        private bool IsSettingsPanelOpen()
        {
            return _settingsFloatRoot != null && _settingsFloatRoot.activeInHierarchy;
        }

        /// <summary>
        /// Settings list filter: backing <see cref="settingsFilter"/> is source of truth.
        /// Live side-rail field wins only while focused (typing). Title search never owns this.
        /// </summary>
        private string CanonicalSettingsSideSearchText()
        {
            if (!IsSettingsPanelOpen())
                return settingsFilter ?? "";

            InputField sideBox = GetSettingsSideSearchInput();
            try
            {
                if (sideBox != null && sideBox.isFocused)
                    return sideBox.text ?? "";
            }
            catch { }

            return settingsFilter ?? "";
        }

        /// <summary>Closes Settings float — use when navigating to Tags so Save→Tags never leaves Settings open.</summary>
        private void ForceCloseSettingsSidePanels()
        {
            if (!IsSettingsPanelOpen()) return;
            ExitInternalSettingsMode(true);
        }

        /// <summary>
        /// Drop settings rows from the middle pane before any Grid restore / browse Refresh.
        /// Prevents InternalSettingRowEntry cells painting as gallery tiles during the async handoff
        /// (esp. VR, where browse Refresh can lag chrome/layout churn).
        /// </summary>
        private void ClearMiddlePaneOfSettingsRows()
        {
            try
            {
                if (currentFilteredFiles != null)
                    currentFilteredFiles.Clear();
                if (selectedFiles != null)
                    selectedFiles.Clear();
                if (selectedFilePaths != null)
                    selectedFilePaths.Clear();
                selectedPath = null;

                RecyclingGridView rgv = recyclingGrid;
                if (rgv == null && contentGO != null)
                {
                    try { rgv = contentGO.GetComponent<RecyclingGridView>(); } catch { }
                }
                if (rgv != null)
                {
                    // Keep 1-col list config until browse Refresh commits real layout — empty grid is OK.
                    try { ApplyInternalSettingsListGridConfig(rgv, deferRefresh: true); } catch { }
                    rgv.SetItemCount(0, deferRefresh: false);
                }
            }
            catch { }
        }

        private void SyncInternalSettingsListView()
        {
            if (IsSettingsPanelOpen())
            {
                InvalidateInternalSettingsDefsCache();
                RefreshInternalSettingsListRows();
                return;
            }

            if (internalSettingsSessionActive)
                CancelInternalSettingsSession();
        }

        private void RefreshGalleryScanWlBorderVisuals()
        {
            try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
        }

        private void RefreshInternalSettingsListRows(bool keepScroll = false)
        {
            if (!IsSettingsPanelOpen()) return;
            StopCo(ref refreshCoroutine);
            try
            {
                CommitSettingsSideSearchIntoFilter();
                string c = CanonicalSettingsSideSearchText();
                settingsFilter = c ?? "";
            }
            catch { }
            EnsureInternalSettingsSession();
            float paneScale = ChromeScale;
            internalSettingsListRowHeightSession = GalleryUiDesignTokens.SettingsFloatRowHeightRef * Mathf.Clamp(paneScale, 0.01f, 100f);

            float s = InternalSettingsChromeScale();
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float rowH = internalSettingsListRowHeightSession;

            try { SyncSettingsSideSearchInputFromFilter(); } catch { }
            RebuildSettingsFloatSidebar(font, s, chromeSz);
            RebuildSettingsFloatRows(font, s, rowH, keepScroll);
        }

        private List<FileEntry> BuildInternalSettingsRows()
        {
            string f = (CanonicalSettingsSideSearchText() ?? "").Trim();
            bool searching = !string.IsNullOrEmpty(f);
            var rows = new List<FileEntry>(64);
            FillSettingsMatchCounts(f);

            string lastGroup = null;
            string lastSection = null;
            var defs = GetInternalSettingDefinitionsCached();
            for (int i = 0; i < defs.Count; i++)
            {
                InternalSettingDefinition def = defs[i];
                if (!SettingsDefPassesViewFilters(def, f, searching)) continue;

                string group = def.GroupKey ?? "";
                string section = SettingsSectionKey(def);
                string label = string.Equals(def.Key, "performance.sceneAtomCache", StringComparison.OrdinalIgnoreCase)
                    ? GetSceneAtomCacheSettingsLabel()
                    : def.Label;

                if (searching && !string.Equals(lastGroup, group, StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(MakeSettingsHeaderRow(group, group, SettingsGroupLabel(group), true));
                    lastGroup = group;
                    lastSection = null;
                }

                string sectionLabel = SettingsSubGroupLabel(section);
                bool showSection = !string.IsNullOrEmpty(sectionLabel)
                    && !string.Equals(section, group, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(lastSection, section, StringComparison.OrdinalIgnoreCase);
                if (showSection)
                {
                    rows.Add(MakeSettingsHeaderRow(section, group, sectionLabel, false));
                    lastSection = section;
                }

                rows.Add(new InternalSettingRowEntry(def.Key, group, label));
            }
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
                        float step = ResolveSettingsSliderStep(def);
                        float v = SnapSettingsSliderValue(def.GetFloat() + (step * dir), def.Min, def.Max, step, def.Decimals);
                        def.SetFloat(v);
                    }
                    break;
                case InternalSettingControlType.TextArea:
                    break;
                case InternalSettingControlType.Button:
                    if (def.ActionEnabled == null || def.ActionEnabled()) def.OnAction?.Invoke();
                    break;
                case InternalSettingControlType.ColorRgb:
                    break;
            }
        }

        internal bool HandleInternalSettingsRowClick(FileEntry file, bool secondary)
        {
            if (hoverPreviewSuppressSettingsClick || hoverPreviewDragging)
            {
                hoverPreviewSuppressSettingsClick = false;
                return true;
            }
            var row = file as InternalSettingRowEntry;
            if (row == null || row.IsHeader) return false;
            InternalSettingDefinition def = GetInternalSettingDefinition(row.RowKey);
            if (def == null) return false;
            if (def.ControlType == InternalSettingControlType.TextArea) return false;
            if (def.ControlType == InternalSettingControlType.ColorRgb) return false;
            if (def.ControlType == InternalSettingControlType.Hotkey) return false;
            ApplyInternalSettingDefinition(def, secondary);
            if (string.Equals(def.SubGroupKey, "hover", StringComparison.OrdinalIgnoreCase))
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

        private float InternalSettingsChromeScale()
        {
            float s = ChromeScale;
            return s <= 0f ? 1f : s;
        }

        /// <summary>
        /// Step for slider nudge / −+ steppers. Prefer authored <see cref="InternalSettingDefinition.Step"/>;
        /// else derive from decimals and span (0–100 → 1, 0.5–1.5 → 0.1, large spans → coarser).
        /// </summary>
        private static float ResolveSettingsSliderStep(InternalSettingDefinition def)
        {
            if (def == null) return 1f;
            if (def.Step > 0f) return def.Step;

            if (def.Decimals > 0)
            {
                float d = 1f;
                for (int i = 0; i < def.Decimals; i++) d *= 0.1f;
                return d;
            }

            float span = Mathf.Abs(def.Max - def.Min);
            if (span <= 0f) return 1f;
            if (span <= 2f) return 0.1f;
            if (span <= 200f) return 1f;
            if (span <= 2000f) return 10f;
            return 25f;
        }

        private static float SnapSettingsSliderValue(float v, float min, float max, float step, int decimals)
        {
            v = Mathf.Clamp(v, min, max);
            if (step > 0f)
            {
                float n = Mathf.Round((v - min) / step);
                v = min + n * step;
                v = Mathf.Clamp(v, min, max);
            }
            int d = Math.Max(0, decimals);
            v = (float)Math.Round(v, d);
            return Mathf.Clamp(v, min, max);
        }

        private static Image AddSettingsControlRoundedBg(GameObject go, Color color, bool raycastTarget = true)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = color;
            rr.raycastTarget = raycastTarget;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            return rr;
        }

        private GameObject CreateMiniButton(Transform parent, string label, float width, Color bg, Action onClick)
        {
            GameObject go = new GameObject("SettingsControlBtn");
            go.transform.SetParent(parent, false);
            Image img = AddSettingsControlRoundedBg(go, bg);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            if (onClick != null)
            {
                var down = go.AddComponent<SettingsPointerDownAction>();
                down.OnDown = onClick;
            }
            UI.NeutralizeSelectableColorTint(b);

            float uiS = InternalSettingsChromeScale();
            float chipH = GalleryUiDesignTokens.ButtonSizeRef * uiS;

            float bw = width * uiS;
            UI.AddLE(go, minWidth: bw, minHeight: chipH, preferredWidth: bw, preferredHeight: chipH, flexibleWidth: 0f);
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.sizeDelta = new Vector2(bw, chipH);
            UI.EnsureFloatChromeHoverBorder(go, inward: true);

            Text t = UI.CreateLabel(go, label, GalleryUiDesignTokens.SettingsListRowDetailFontRef, Color.white, TextAnchor.MiddleCenter, name: "Text");
            if (t != null) t.raycastTarget = false;
            GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.SettingsListRowDetailFontRef, uiS, GalleryUiDesignTokens.FontMinRef);
            return go;
        }

        private static void SettleSettingsRowLayout(Transform detailsTr, Transform listRowTr, bool settleLayout)
        {
            if (!settleLayout) return;
            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(detailsTr as RectTransform);
                LayoutRebuilder.ForceRebuildLayoutImmediate(listRowTr as RectTransform);
            }
            catch { }
        }

        /// <param name="settleLayout">Force the control row to lay out now. Needed when re-binding a row
        /// that is already on screen (value changed, recycled row); wasted work on a freshly created row,
        /// which the next normal layout pass sizes anyway.</param>
        private void RebuildSettingsRowControls(GameObject btnGO, InternalSettingDefinition def, bool settleLayout = true)
        {
            if (btnGO == null || def == null) return;

            string rowTip = def.Tooltip ?? def.Label ?? "";
            if (def.HasDefault)
            {
                string dv = FormatSettingsResetDefaultValue(def);
                if (!string.IsNullOrEmpty(dv))
                    rowTip = rowTip + "\n" + VPBTranslation.T("settings.row.reset.default", "Default") + ": " + dv;
            }
            AddTooltipPlain(btnGO, rowTip);

            float rowH = IsSettingsPanelOpen()
                ? internalSettingsListRowHeightSession
                : EffectiveListRowHeightForGallery();
            float uiS = InternalSettingsChromeScale();
            float chipH = GalleryUiDesignTokens.ButtonSizeRef * uiS;

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
                    GalleryUiMetrics.ApplyFont(nameText, GalleryUiDesignTokens.SettingsListRowNameFontRef, uiS, GalleryUiDesignTokens.FontMinRef);
                    bool modified = def.HasDefault && !SettingsRowIsAtDefault(def);
                    nameText.fontStyle = modified ? FontStyle.Bold : FontStyle.Normal;
                    nameText.color = modified ? Color.white : GalleryUiColorTokens.TextMuted;
                }
                LayoutElement nameLe = nameTr != null ? nameTr.GetComponent<LayoutElement>() : null;
                if (nameLe != null)
                    nameLe.minHeight = chipH;
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
            HorizontalLayoutGroup hlg = UI.AddHLG(controls, spacing: UI.GapTight(uiS), childAlignment: TextAnchor.MiddleRight, childForceExpandWidth: false);
            LayoutElement cle = UI.AddLE(controls, minHeight: chipH, flexibleWidth: 1f);

            try
            {
            if (def.ControlType == InternalSettingControlType.Toggle && def.GetBool != null && def.SetBool != null)
            {
                bool cur = def.GetBool();
                CreateMiniButton(controls.transform, FormatSettingsToggleLabel(false), 58f, cur ? UI.ChromePanel : UI.AccentRed, () => {
                    def.SetBool(false);
                    RefreshInternalSettingsListRows(true);
                });
                CreateMiniButton(controls.transform, FormatSettingsToggleLabel(true), 58f, cur ? UI.AccentGreen : UI.ChromePanel, () => {
                    def.SetBool(true);
                    RefreshInternalSettingsListRows(true);
                });
                SettleSettingsRowLayout(detailsTr, listRowTr, settleLayout);
                return;
            }

            if (def.ControlType == InternalSettingControlType.Cycle && def.GetString != null && def.SetString != null)
            {
                string cur = def.GetString() ?? "";
                string display = FormatSettingsCycleOption(cur);
                GameObject cycleBtn = null;
                cycleBtn = CreateMiniButton(controls.transform, display, 150f, GalleryUiColorTokens.SurfaceMid, () => {
                    // Read current value at click time (avoid stale captured value when row reuses objects).
                    string curNow = def.GetString() ?? "";
                    string next = NextOf(curNow, def.Options);
                    def.SetString(next);
                    try
                    {
                        // Update label immediately; pooled list rows can keep old text until rebind.
                        var t = cycleBtn != null ? cycleBtn.GetComponentInChildren<Text>(true) : null;
                        if (t != null) t.text = FormatSettingsCycleOption(next);
                    }
                    catch { }
                    if (string.Equals(def.SubGroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                        NotifyInternalSettingsHoverPreviewChanged();
                    RefreshInternalSettingsListRows(true);
                });
                try
                {
                    // Ensure control row sizes settle immediately (prevents clipping when switching cycle values).
                    if (settleLayout)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(detailsTr as RectTransform);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(listRowTr as RectTransform);
                    }
                }
                catch { }
                return;
            }

            if (def.ControlType == InternalSettingControlType.ColorRgb && def.GetColor != null && def.SetColor != null)
            {
                GameObject swatch = new GameObject("SettingsBorderColorSwatch");
                swatch.transform.SetParent(controls.transform, false);
                LayoutElement swle = UI.AddLE(swatch, minWidth: 48f * uiS, minHeight: chipH - 4f * uiS, preferredWidth: 72f * uiS, preferredHeight: chipH - 4f * uiS, flexibleWidth: 0f);
                Image swImg = UI.AddImage(swatch, def.GetColor(), false);

                CreateMiniButton(
                    controls.transform,
                    VPBTranslation.T("settings.grid_border_color.choose", "CHOOSE…"),
                    120f,
                    GalleryUiColorTokens.SurfaceMid,
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
                    if (settleLayout)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(detailsTr as RectTransform);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(listRowTr as RectTransform);
                    }
                }
                catch { }
                return;
            }

            if (def.ControlType == InternalSettingControlType.Slider && def.GetFloat != null && def.SetFloat != null)
            {
                float cur = def.GetFloat();

                GameObject sliderHost = new GameObject("SettingsSliderHost");
                sliderHost.transform.SetParent(controls.transform, false);
                LayoutElement sle = UI.AddLE(sliderHost, minWidth: 120f * uiS, minHeight: chipH, preferredWidth: 320f * uiS, preferredHeight: chipH, flexibleWidth: 1f);

                Slider slider = sliderHost.AddComponent<Slider>();
                slider.minValue = def.Min;
                slider.maxValue = def.Max;
                slider.value = Mathf.Clamp(cur, def.Min, def.Max);
                slider.wholeNumbers = def.Decimals <= 0;

                float handleW = 20f * uiS;
                float trackEndPad = handleW * 0.5f;

                // Full-height transparent raycast target so the whole control row area (not just the
                // thin bar) receives hover, click, and drag events. Pointer events bubble up to the
                // Slider on sliderHost, which fixes click-drag being swallowed by the parent scroll view.
                GameObject hitbox = new GameObject("Hitbox");
                hitbox.transform.SetParent(sliderHost.transform, false);
                var hitImg = UI.AddImage(hitbox, new Color(1f, 1f, 1f, 0f));
                RectTransform hitRT = hitbox.GetComponent<RectTransform>();
                hitRT.anchorMin = Vector2.zero; hitRT.anchorMax = Vector2.one; hitRT.sizeDelta = Vector2.zero;

                GameObject bg = new GameObject("Background");
                bg.transform.SetParent(sliderHost.transform, false);
                var bgImg = AddSettingsControlRoundedBg(bg, GalleryUiColorTokens.SurfacePanel, false);
                RectTransform bgRT = bg.GetComponent<RectTransform>();
                bgRT.anchorMin = new Vector2(0, 0.28f); bgRT.anchorMax = new Vector2(1, 0.72f);
                bgRT.offsetMin = new Vector2(trackEndPad, 0f); bgRT.offsetMax = new Vector2(-trackEndPad, 0f);

                GameObject fillArea = new GameObject("Fill Area");
                fillArea.transform.SetParent(sliderHost.transform, false);
                RectTransform faRT = fillArea.AddComponent<RectTransform>();
                faRT.anchorMin = new Vector2(0, 0.28f); faRT.anchorMax = new Vector2(1, 0.72f);
                faRT.offsetMin = new Vector2(trackEndPad, 0f); faRT.offsetMax = new Vector2(-trackEndPad, 0f);

                GameObject fill = new GameObject("Fill");
                fill.transform.SetParent(fillArea.transform, false);
                var fillImg = AddSettingsControlRoundedBg(fill, GalleryUiColorTokens.SurfaceMid, false);
                RectTransform fillRT = fill.GetComponent<RectTransform>();
                fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one; fillRT.sizeDelta = Vector2.zero;
                slider.fillRect = fillRT;

                GameObject handleArea = new GameObject("Handle Area");
                handleArea.transform.SetParent(sliderHost.transform, false);
                RectTransform haRT = handleArea.AddComponent<RectTransform>();
                haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one;
                haRT.offsetMin = new Vector2(trackEndPad, 0f); haRT.offsetMax = new Vector2(-trackEndPad, 0f);

                GameObject handle = new GameObject("Handle");
                handle.transform.SetParent(handleArea.transform, false);
                var handleImg = AddSettingsControlRoundedBg(handle, Color.white);
                RectTransform handleRT = handle.GetComponent<RectTransform>();
                handleRT.anchorMin = new Vector2(0, 0); handleRT.anchorMax = new Vector2(0, 1); handleRT.sizeDelta = new Vector2(handleW, 0);
                slider.handleRect = handleRT;
                slider.targetGraphic = handleImg;

                bool deferLive = def.DeferLiveApply;
                float step = ResolveSettingsSliderStep(def);
                int decimals = Math.Max(0, def.Decimals);
                string fmt = "F" + decimals;

                GameObject stepperHost = new GameObject("SettingsValueStepper");
                stepperHost.transform.SetParent(controls.transform, false);
                // Gestalt: − field + read as one spin unit (Galitz spin + slider hybrid; Fitts square hits).
                UI.AddHLG(stepperHost, spacing: UI.GapHair(uiS), childAlignment: TextAnchor.MiddleRight, childForceExpandWidth: false);
                float stepBtnW = GalleryUiDesignTokens.ButtonSizeRef;
                float inputW = 78f;
                UI.AddLE(
                    stepperHost,
                    minWidth: (stepBtnW * 2f + inputW + 4f) * uiS,
                    minHeight: chipH,
                    preferredWidth: (stepBtnW * 2f + inputW + 4f) * uiS,
                    preferredHeight: chipH);

                Button minusBtn = null;
                Button plusBtn = null;
                InputField input = null;

                Action syncStepperEnabled = () =>
                {
                    float curV = slider.value;
                    if (minusBtn != null) minusBtn.interactable = curV > def.Min + 1e-5f;
                    if (plusBtn != null) plusBtn.interactable = curV < def.Max - 1e-5f;
                };

                Action<float> applyDiscreteValue = v =>
                {
                    v = SnapSettingsSliderValue(v, def.Min, def.Max, step, decimals);
                    slider.value = v;
                    if (input != null) input.text = v.ToString(fmt);
                    def.SetFloat(v);
                    if (string.Equals(def.SubGroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                        NotifyInternalSettingsHoverPreviewChanged();
                    // Discrete −/+ / typed commit OK to rebuild; only slider drag must defer.
                    if (deferLive)
                    {
                        try { ApplyInnerPaneScale(); } catch { }
                    }
                    syncStepperEnabled();
                };

                GameObject minusGO = CreateMiniButton(stepperHost.transform, "-", stepBtnW, GalleryUiColorTokens.SurfaceMid, () =>
                {
                    applyDiscreteValue(slider.value - step);
                });
                minusBtn = minusGO != null ? minusGO.GetComponent<Button>() : null;

                GameObject inputGO = new GameObject("SettingsValueInput");
                inputGO.transform.SetParent(stepperHost.transform, false);
                UI.AddLE(inputGO, minWidth: inputW * uiS, minHeight: chipH, preferredWidth: inputW * uiS, preferredHeight: chipH);
                Image inputBg = AddSettingsControlRoundedBg(inputGO, UI.ChromeDarker);
                input = inputGO.AddComponent<InputField>();
                input.targetGraphic = inputBg;
                input.contentType = def.AllowNegative ? InputField.ContentType.Standard : InputField.ContentType.DecimalNumber;

                Text it = UI.CreateLabel(inputGO, "", GalleryUiDesignTokens.SettingsListRowDetailFontRef, Color.white, TextAnchor.MiddleCenter, name: "Text");
                GalleryUiMetrics.ApplyFont(it, GalleryUiDesignTokens.SettingsListRowDetailFontRef, uiS, GalleryUiDesignTokens.FontMinRef);
                input.textComponent = it;
                input.text = slider.value.ToString(fmt);

                GameObject plusGO = CreateMiniButton(stepperHost.transform, "+", stepBtnW, GalleryUiColorTokens.SurfaceMid, () =>
                {
                    applyDiscreteValue(slider.value + step);
                });
                plusBtn = plusGO != null ? plusGO.GetComponent<Button>() : null;

                slider.onValueChanged.AddListener(v =>
                {
                    v = SnapSettingsSliderValue(v, def.Min, def.Max, step, decimals);
                    if (input != null) input.text = v.ToString(fmt);
                    syncStepperEnabled();
                    // Deferred sliders (e.g. UI scale) only show the live value while dragging; applying
                    // would rebuild the settings list rows and destroy this slider mid-drag. Commit on release.
                    if (deferLive) return;
                    def.SetFloat(v);
                    if (string.Equals(def.SubGroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                        NotifyInternalSettingsHoverPreviewChanged();
                });
                if (deferLive)
                {
                    var commit = sliderHost.AddComponent<SettingsSliderReleaseCommit>();
                    commit.OnRelease = () =>
                    {
                        def.SetFloat(slider.value);
                        if (string.Equals(def.SubGroupKey, "hover", StringComparison.OrdinalIgnoreCase))
                            NotifyInternalSettingsHoverPreviewChanged();
                        // UI-scale sliders defer until release; ApplyInnerPaneScale → RescaleSettingsFloatIfOpen
                        // already rebuilds rows once. Do not Refresh again (double Destroy+rebuild hitch).
                        try { ApplyInnerPaneScale(); } catch { }
                        syncStepperEnabled();
                    };
                }
                input.onEndEdit.AddListener(s =>
                {
                    float parsed;
                    if (!float.TryParse(s, out parsed))
                    {
                        input.text = slider.value.ToString(fmt);
                        return;
                    }
                    applyDiscreteValue(parsed);
                });
                syncStepperEnabled();
                SettleSettingsRowLayout(detailsTr, listRowTr, true);
                return;
            }

            if (def.ControlType == InternalSettingControlType.TextArea && def.GetString != null && def.SetString != null)
            {
                if (string.Equals(def.Key, "quick.categoryEditor", StringComparison.OrdinalIgnoreCase))
                {
                    cle.minHeight = 40f * uiS;
                    GameObject btnRow = new GameObject("SettingsTextAreaButtons");
                    btnRow.transform.SetParent(controls.transform, false);
                    HorizontalLayoutGroup bh = UI.AddHLG(btnRow, spacing: UI.GapTight(uiS), childAlignment: TextAnchor.MiddleRight, childForceExpandWidth: false);
                    LayoutElement ble = UI.AddLE(btnRow, minHeight: chipH);

                    CreateMiniButton(btnRow.transform, "EDIT…", 96f, GalleryUiColorTokens.SurfaceMid, () =>
                    {
                        ShowCategoryQuickEditor();
                    });
                    return;
                }

                cle.minHeight = 96f * uiS;
                GameObject taHost = new GameObject("SettingsTextAreaHost");
                taHost.transform.SetParent(controls.transform, false);
                LayoutElement tle = UI.AddLE(taHost, minWidth: 120f * uiS, minHeight: 72f * uiS, preferredWidth: 320f * uiS, preferredHeight: 72f * uiS, flexibleWidth: 1f);

                Image taBg = AddSettingsControlRoundedBg(taHost, new Color(0.16f, 0.16f, 0.18f, 1f));
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

                Text taTxt = UI.CreateLabel(taHost, "", GalleryUiDesignTokens.SettingsListRowDetailFontRef, new Color(0.95f, 0.95f, 0.97f, 1f), TextAnchor.UpperLeft, richText: false, name: "Text");
                GalleryUiMetrics.ApplyFont(taTxt, GalleryUiDesignTokens.SettingsListRowDetailFontRef, uiS, GalleryUiDesignTokens.FontMinRef);
                RectTransform taTxtRt = taTxt.GetComponent<RectTransform>();
                taTxtRt.offsetMin = new Vector2(6f * uiS, 6f * uiS);
                taTxtRt.offsetMax = new Vector2(-6f * uiS, -6f * uiS);
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
                RebuildPluginHotkeyRowControls(controls.transform, def, uiS);
                return;
            }

            if (def.ControlType == InternalSettingControlType.Button)
            {
                if (def.OnAction == null
                    && string.Equals(def.Key, "plugin.scan_whitelist.empty_warn", StringComparison.OrdinalIgnoreCase))
                {
                    Text wt = UI.CreateLabel(controls, def.Label ?? "", GalleryUiDesignTokens.SettingsListRowDetailFontRef, new Color(1f, 0.75f, 0.2f, 1f), TextAnchor.MiddleRight, richText: false, name: "SettingsWarningLabel");
                    GalleryUiMetrics.ApplyFont(wt, GalleryUiDesignTokens.SettingsListRowDetailFontRef, uiS, GalleryUiDesignTokens.FontMinRef);
                    LayoutElement wle = UI.AddLE(wt.gameObject, preferredHeight: chipH, flexibleWidth: 1f);
                    return;
                }
                if (def.OnAction != null)
                {
                    string btnLabel = VPBTranslation.T("settings.row.action", "CLICK");
                    bool isSceneAtomCache = string.Equals(def.Key, "performance.sceneAtomCache", StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(def.Key, "plugin.scan_whitelist.manage", StringComparison.OrdinalIgnoreCase))
                        btnLabel = VPBTranslation.T("settings.row.manage", "MANAGE");
                    else if (string.Equals(def.Key, "plugin.qm_positions", StringComparison.OrdinalIgnoreCase))
                        btnLabel = VPBTranslation.T("settings.row.adjust", "ADJUST");
                    else if (isSceneAtomCache)
                        btnLabel = VPBTranslation.T("settings.row.clear", "CLEAR");
                    GameObject actionGO = CreateMiniButton(controls.transform, btnLabel, 150f, new Color(0.7f, 0.4f, 0.2f, 1f), () => {
                        if (def.ActionEnabled != null && !def.ActionEnabled()) return;
                        def.OnAction?.Invoke();
                        RefreshInternalSettingsListRows(true);
                    });
                    if (def.ActionEnabled != null)
                    {
                        Button actionButton = actionGO != null ? actionGO.GetComponent<Button>() : null;
                        if (actionButton != null) actionButton.interactable = def.ActionEnabled();
                    }
                }
                return;
            }
            }
            finally
            {
                if (def.HasDefault)
                    AppendSettingsRowResetButton(controls.transform, def, uiS);
            }
        }

        internal bool ConfigureInternalSettingsRowUI(GameObject btnGO, FileEntry file)
        {
            var row = file as InternalSettingRowEntry;
            if (row == null || row.IsHeader) return false;
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
            VpbLocalDatabase.RequestSceneAtomCacheTrim();
            try { SetHoverPreviewDummyActive(false); } catch { }
            PluginSettingsEndSession();
            internalSettingsSessionActive = false;
            internalSettingsBackup = null;
        }

        internal void ExitInternalSettingsMode(bool saveChanges)
        {
            PersistSettingsLastGroup();
            settingsModifiedOnly = false;
            // Live property sheet: Close / X / Esc keep current values. Revert is a separate footer action.
            SaveInternalSettingsSession();

            if (leftActiveContent == ContentType.Settings)
                leftActiveContent = null;
            if (rightActiveContent == ContentType.Settings)
                rightActiveContent = null;

            HideSettingsFloat();

            // Do not ApplySidePanelDefaultsFromConfig here — that re-syncs Last*/Default side rails
            // (incl. Import) and reopens Scene Import after Cancel/Save even when user closed it.
            // Settings is a float; Default* still apply on cold pane open. Scale/chrome restore
            // already ran in Cancel/Save session helpers; UpdateLayout below refreshes rails as-is.
            try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, GetTitleSearchBrowseFieldText(), _titleBarSearchOnValueChanged); } catch { }
            try { UpdateLayout(); } catch { }
            try { UpdateTabs(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
        }

        /// <summary>Restore snapshot from window-open, stay open, start a fresh revert baseline.</summary>
        private void RevertInternalSettingsSession()
        {
            if (!internalSettingsSessionActive || internalSettingsBackup == null) return;
            CancelInternalSettingsSession();
            EnsureInternalSettingsSession();
            if (IsSettingsPanelOpen())
                RefreshInternalSettingsListRows(true);
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
            VPBConfig.Instance.SceneImportCacheLimitMb = b.SceneImportCacheLimitMb;
            VPBConfig.Instance.GalleryDetailStripSideInfoEnabled = b.GalleryDetailStripSideInfoEnabled;
            VPBConfig.Instance.GalleryDetailStripThumbOnRight = b.GalleryDetailStripThumbOnRight;
            VPBConfig.Instance.GalleryDetailStripHeightRef = b.GalleryDetailStripHeightRef;
            VPBConfig.Instance.GalleryOpacity = b.GalleryOpacity;
            VPBConfig.Instance.SideButtonScaleVR = b.SideButtonScaleVR;
            VPBConfig.Instance.SideButtonScaleDesktop = b.SideButtonScaleDesktop;
            VPBConfig.Instance.InnerPaneScaleVR = b.InnerPaneScaleVR;
            VPBConfig.Instance.InnerPaneScaleDesktop = b.InnerPaneScaleDesktop;
            VPBConfig.Instance.EnableButtonGaps = b.EnableButtonGaps;
            VPBConfig.Instance.EnableGalleryElementRounding = b.EnableGalleryElementRounding;
            VPBConfig.Instance.EnableGalleryButtonChromeRims = b.EnableGalleryButtonChromeRims;
            VPBConfig.Instance.GalleryElementCornerRadiusFraction = VPBConfig.ClampGalleryElementCornerRadiusFraction(b.GalleryElementCornerRadiusFraction);
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
            VPBConfig.Instance.VerticalMoveKeysEnabled = b.VerticalMoveKeysEnabled;
            VPBConfig.Instance.ShortcutsRequireWindowFocus = b.ShortcutsRequireWindowFocus;
            VPBConfig.Instance.ShortcutsNeedVisiblePane = b.ShortcutsNeedVisiblePane;
            VPBConfig.Instance.CategoryNumberKeysEnabled = b.CategoryNumberKeysEnabled;
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
            VPBConfig.Instance.SpringScrollButtonMode = VPBConfig.NormalizeSpringScrollButtonMode(b.SpringScrollButtonMode);
            VPBConfig.Instance.GalleryVrThumbstickScrollEnabled = b.GalleryVrThumbstickScrollEnabled;
            VPBConfig.Instance.GalleryHideCreatorSideButtons = b.GalleryHideCreatorSideButtons;
            VPBConfig.Instance.GalleryShowCategoryIcons = b.GalleryShowCategoryIcons;
            VPBConfig.Instance.GalleryConsolidateCreatorNames = b.GalleryConsolidateCreatorNames;
            VPBConfig.Instance.PluginGalleryGridThumbnails = b.PluginGalleryGridThumbnails;
            VPBConfig.Instance.PluginGalleryCategoryLabelsOnly = b.PluginGalleryCategoryLabelsOnly;
            VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled = b.GalleryThumbPlaceholderLabelsEnabled;
            VPBConfig.Instance.GalleryThumbPlaceholderSizeScale = VPBConfig.ClampGalleryThumbPlaceholderSizeScale(b.GalleryThumbPlaceholderSizeScale);
            VPBConfig.Instance.GalleryListNamesLegacyFileName = b.GalleryListNamesLegacyFileName;
            VPBConfig.Instance.GalleryHoverPreviewMode = b.GalleryHoverPreviewMode;
            VPBConfig.Instance.QuickMenuRandomHoverPreview = b.QuickMenuRandomHoverPreview;
            VPBConfig.Instance.GalleryListHoverPreviewSize = b.GalleryListHoverPreviewSize;
            VPBConfig.Instance.GalleryListHoverPreviewOffsetX = b.GalleryListHoverPreviewOffsetX;
            VPBConfig.Instance.GalleryListHoverPreviewOffsetY = b.GalleryListHoverPreviewOffsetY;
            VPBConfig.Instance.GalleryGridLabelsEnabled = b.GalleryGridLabelsEnabled;
            VPBConfig.Instance.GalleryGridLabelsAutoHideAtHighDensity = b.GalleryGridLabelsAutoHideAtHighDensity;
            VPBConfig.Instance.GalleryGridHoverBadgesEnabled = b.GalleryGridHoverBadgesEnabled;
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
            // Live pane state — rewriting config alone would leave the gallery dark with the pref off.
            SetFloatsOnlyAllPanes(b.GalleryFloatsOnlyMode);
            VPBConfig.Instance.GalleryAnchorToVamMenu = b.GalleryAnchorToVamMenu;
            VPBConfig.Instance.LayoutPresetRevertBarSeconds = b.LayoutPresetRevertBarSeconds;
            VPBConfig.Instance.LayoutPresetSuggestOnModeSwitch = b.LayoutPresetSuggestOnModeSwitch;
            VPBConfig.Instance.GalleryVrMenuAnchorTiltDeg = VPBConfig.ClampGalleryVrMenuAnchorTiltDeg(b.GalleryVrMenuAnchorTiltDeg);
            VPBConfig.Instance.GalleryCategoryQuickOrder = b.GalleryCategoryQuickOrder ?? "";
            VPBConfig.Instance.GalleryCategoryQuickSwitchHidden = b.GalleryCategoryQuickSwitchHidden ?? "";
            VPBConfig.Instance.HiddenCategories = b.HiddenCategories != null
                ? new HashSet<string>(b.HiddenCategories, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            VPBConfig.Instance.BlockInGameMessages = b.BlockInGameMessages ?? "Off";
            VPBConfig.Instance.HideMissingDependencyLogs = b.HideMissingDependencyLogs;
            VPBConfig.Instance.ClearInGameLogsOnSceneLaunch = b.ClearInGameLogsOnSceneLaunch;

            RestorePluginSettingsFromSnapshot(b);

            if (this != null)
            {
                ApplyInnerPaneScale();
                categoriesCached = false;
                // Layout restore + browse RefreshFiles owned by SyncInternalSettingsListView /
                // ExitInternalSettingsMode — avoid Grid refresh while settings rows still bound.
                try { _detailStripCacheKey = ""; DetailStripRefresh(); } catch { }
            }
            ApplyGalleryTransparencyToAllPanels();
            try { UI.ApplyGalleryElementCornerRadiusGlobally(); } catch { }
            try { UI.ApplyGalleryButtonChromeRimsGlobally(); } catch { }
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
                boxBg.color = UI.ChromeDark;

                // Layout for text + buttons
                UnityEngine.UI.VerticalLayoutGroup vl = box.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                vl.padding = new RectOffset(16, 16, 16, 16);
                vl.spacing = 12f;
                vl.childAlignment = TextAnchor.UpperCenter;
                vl.childForceExpandWidth = true;
                vl.childForceExpandHeight = false;

                // Message text
                Text msg = UI.CreateLabel(box, VPBTranslation.T("ba.prompt.msg",
                    "BrowserAssist data detected.\nImport available in Settings.\nOpen Settings → BrowserAssist section."), GalleryUiDesignTokens.FontRef, Color.white, TextAnchor.MiddleCenter, name: "Message");
                UnityEngine.UI.LayoutElement textLe = msg.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
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
                        try { OpenSettingsGroup("ba_migration"); } catch { }
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
