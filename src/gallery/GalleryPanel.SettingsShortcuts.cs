using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const string ShortcutRowKeyPrefix = "shortcut.";

        private string[] _shortcutDrafts;
        private HashSet<string> _shortcutConflictSet;
        private bool _shortcutConflictDirty = true;

        private void ShortcutSettingsBeginSession()
        {
            _shortcutDrafts = VpbShortcutMap.CapturePatterns();
            _shortcutConflictDirty = true;
        }

        private string GetShortcutDraft(int index)
        {
            if (_shortcutDrafts == null || index < 0 || index >= _shortcutDrafts.Length) return "";
            return _shortcutDrafts[index] ?? "";
        }

        private void SetShortcutDraft(int index, string pattern)
        {
            if (_shortcutDrafts == null || index < 0 || index >= _shortcutDrafts.Length) return;
            _shortcutDrafts[index] = pattern ?? "";
            _shortcutConflictDirty = true;
        }

        private void CommitShortcutDrafts()
        {
            if (_shortcutDrafts == null) return;
            VpbShortcutMap.ApplyPatterns(_shortcutDrafts);
            VpbShortcutMap.SaveToConfig();
        }

        internal static void NotifyShortcutBindingsChanged()
        {
            try
            {
                var g = Gallery.singleton;
                var panels = g != null ? g.Panels : null;
                if (panels == null) return;
                for (int i = 0; i < panels.Count; i++)
                {
                    GalleryPanel p = panels[i];
                    if (p == null) continue;
                    try { p.InvalidateCommandPaletteCatalog(); } catch { }
                    try { if (p._inAppHelpOpen) p.ReloadInAppHelpContent(); } catch { }
                }
            }
            catch { }
        }

        private HashSet<string> ShortcutConflictSet()
        {
            if (!_shortcutConflictDirty && _shortcutConflictSet != null)
                return _shortcutConflictSet;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dupes = new HashSet<string>(StringComparer.Ordinal);

            Action<string> feed = p =>
            {
                string norm = VpbShortcutMap.NormalizePattern(p);
                if (string.IsNullOrEmpty(norm)) return;
                if (!seen.Add(norm)) dupes.Add(norm);
            };

            feed(_pluginDraftGalleryKey);
            feed(_pluginDraftCreateGalleryKey);
            feed(_pluginDraftHubKey);
            feed(_pluginDraftClearConsoleKey);
            if (_shortcutDrafts != null)
            {
                for (int i = 0; i < _shortcutDrafts.Length; i++) feed(_shortcutDrafts[i]);
            }

            _shortcutConflictSet = dupes;
            _shortcutConflictDirty = false;
            return dupes;
        }

        private bool ShortcutPatternHasConflict(string pattern)
        {
            string norm = VpbShortcutMap.NormalizePattern(pattern);
            if (string.IsNullOrEmpty(norm)) return false;
            return ShortcutConflictSet().Contains(norm);
        }

        private string BuildShortcutConflictTooltip(string rowKey, string pattern)
        {
            string norm = VpbShortcutMap.NormalizePattern(pattern);
            if (string.IsNullOrEmpty(norm)) return null;

            var others = new List<string>(3);
            AppendConflictName(others, rowKey, norm, "plugin.hotkey.gallery", _pluginDraftGalleryKey,
                VPBTranslation.T("hook.settings.key.gallery", "Show/Hide Panes"));
            AppendConflictName(others, rowKey, norm, "plugin.hotkey.create_gallery", _pluginDraftCreateGalleryKey,
                VPBTranslation.T("hook.settings.key.create_gallery", "Create Gallery Pane"));
            AppendConflictName(others, rowKey, norm, "plugin.hotkey.hub", _pluginDraftHubKey,
                VPBTranslation.T("hook.settings.key.hub", "Open Hub Browser"));
            AppendConflictName(others, rowKey, norm, "plugin.hotkey.clear_console", _pluginDraftClearConsoleKey,
                VPBTranslation.T("hook.settings.key.clear_console", "Clear Console"));

            VpbShortcutDef[] defs = VpbShortcutMap.Defs;
            for (int i = 0; i < defs.Length; i++)
            {
                VpbShortcutDef d = defs[i];
                if (d == null) continue;
                AppendConflictName(others, rowKey, norm, d.Key, GetShortcutDraft(i),
                    VPBTranslation.T(d.LabelKey, d.LabelFallback));
            }

            if (others.Count == 0) return null;
            return string.Format(
                VPBTranslation.T("settings.hotkey.conflict", "Also bound to: {0}. Whichever runs first wins — clear or move one."),
                string.Join(", ", others.ToArray()));
        }

        private void AppendConflictName(List<string> into, string rowKey, string norm,
            string candidateKey, string candidatePattern, string candidateLabel)
        {
            if (string.Equals(rowKey, candidateKey, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.Equals(VpbShortcutMap.NormalizePattern(candidatePattern), norm, StringComparison.Ordinal)) return;
            into.Add(candidateLabel);
        }

        private void AppendShortcutRuleSettingDefinitions(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;
            if (_shortcutDrafts == null) _shortcutDrafts = VpbShortcutMap.CapturePatterns();

            var rules = new InternalSettingDefinition
            {
                Key = "keys.requireWindowFocus",
                GroupKey = "keys_rules",
                Label = VPBTranslation.T("settings.keys.require_focus", "Only while the VaM window is focused"),
                Tooltip = VPBTranslation.T("settings.tip.keys_require_focus",
                    "VaM keeps running in the background, so keys typed in another app can reach it. When ON, VPB ignores every hotkey unless VaM has focus. Always bypassed in VR, where the session legitimately runs unfocused."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.ShortcutsRequireWindowFocus,
                SetBool = v => { VPBConfig.Instance.ShortcutsRequireWindowFocus = v; VPBConfig.Instance.TriggerChange(); }
            };
            rules.SetDefault(true);
            defs.Add(rules);

            var needPane = new InternalSettingDefinition
            {
                Key = "keys.needVisiblePane",
                GroupKey = "keys_rules",
                Label = VPBTranslation.T("settings.keys.need_visible_pane", "Only while a gallery pane is on screen"),
                Tooltip = VPBTranslation.T("settings.tip.keys_need_visible_pane",
                    "When ON, VPB shortcuts do nothing while the panes are hidden. The hotkeys that OPEN something (Show/Hide Panes, Create Gallery Pane, Open Hub Browser) stay live so you can always get back in."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.ShortcutsNeedVisiblePane,
                SetBool = v => { VPBConfig.Instance.ShortcutsNeedVisiblePane = v; VPBConfig.Instance.TriggerChange(); }
            };
            needPane.SetDefault(true);
            defs.Add(needPane);

            var numberKeys = new InternalSettingDefinition
            {
                Key = "keys.categoryNumbers",
                GroupKey = "keys_rules",
                Label = VPBTranslation.T("settings.keys.category_numbers", "0–9 jump to category"),
                Tooltip = VPBTranslation.T("settings.tip.keys_category_numbers",
                    "When ON, the number row switches category while the gallery is focused. Turn OFF if you use the number keys for something else."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => VPBConfig.Instance.CategoryNumberKeysEnabled,
                SetBool = v => { VPBConfig.Instance.CategoryNumberKeysEnabled = v; VPBConfig.Instance.TriggerChange(); }
            };
            numberKeys.SetDefault(true);
            defs.Add(numberKeys);
        }

        private void AppendShortcutBindingSettingDefinitions(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;
            if (_shortcutDrafts == null) _shortcutDrafts = VpbShortcutMap.CapturePatterns();

            VpbShortcutDef[] all = VpbShortcutMap.Defs;
            for (int i = 0; i < all.Length; i++)
            {
                VpbShortcutDef d = all[i];
                if (d == null) continue;
                int index = i;
                var row = new InternalSettingDefinition
                {
                    Key = d.Key,
                    GroupKey = d.Section,
                    Label = VPBTranslation.T(d.LabelKey, d.LabelFallback),
                    Tooltip = VPBTranslation.T(d.TipKey, d.TipFallback),
                    ControlType = InternalSettingControlType.Hotkey,
                    GetString = () => GetShortcutDraft(index),
                    SetString = v => SetShortcutDraft(index, v)
                };
                row.SetDefault(d.DefaultPattern ?? "");
                defs.Add(row);
            }
        }

        private bool TryGetShortcutRowIndex(string rowKey, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(rowKey)) return false;
            if (!rowKey.StartsWith(ShortcutRowKeyPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            index = VpbShortcutMap.IndexOfKey(rowKey);
            return index >= 0;
        }
    }
}
