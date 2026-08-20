using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    internal enum VpbShortcut
    {
        Help = 0,
        HelpAlt,
        CommandPalette,
        UiScaleUp,
        UiScaleUpAlt,
        UiScaleDown,
        UiScaleDownAlt,

        FocusSearch,
        FilterPresets,
        ImportSidebar,
        LayoutPresets,
        HistoryRefresh,

        Apply,
        ApplyAlt,
        SelectAll,
        DeleteSelection,
        DeleteSelectionAlt,
        Undo,
        Redo,
        RedoAlt,

        SceneTools,
        StripScene,
        SceneEraser,

        NavUp,
        NavDown,

        Count
    }

    internal sealed class VpbShortcutDef
    {
        public VpbShortcut Id;
        public string Key;
        public string Section;
        public string LabelKey;
        public string LabelFallback;
        public string TipKey;
        public string TipFallback;
        public string DefaultPattern;
        public bool Held;
    }

    internal static class VpbShortcutMap
    {
        private struct Binding
        {
            public KeyCode Key;
            public bool Ctrl;
            public bool Shift;
            public bool Alt;
            public bool Valid;
        }

        private const int N = (int)VpbShortcut.Count;

        private static readonly string[] s_Patterns = new string[N];
        private static readonly Binding[] s_Bindings = new Binding[N];
        private static bool s_Initialized;
        private static int s_LastLoadAttemptFrame = -1000;

        private static int s_GateFrame = -1;
        private static bool s_GateOpen = true;

        private static readonly VpbShortcutDef[] s_Defs = BuildDefs();

        internal static VpbShortcutDef[] Defs { get { return s_Defs; } }

        private static VpbShortcutDef[] BuildDefs()
        {
            var d = new VpbShortcutDef[N];

            Add(d, VpbShortcut.Help, "shortcut.help", "keys_chrome",
                "shortcut.help", "Open / close Hotkeys sheet",
                "shortcut.tip.help", "Shows the in-app help panel scrolled to the Hotkeys section.", "F1");
            Add(d, VpbShortcut.HelpAlt, "shortcut.help_alt", "keys_chrome",
                "shortcut.help_alt", "Open / close Hotkeys sheet (alternate)",
                "shortcut.tip.help_alt", "Second binding for the Hotkeys sheet. Ignored while typing in a text field.", "Shift+/");
            Add(d, VpbShortcut.CommandPalette, "shortcut.command_palette", "keys_chrome",
                "shortcut.command_palette", "Command palette",
                "shortcut.tip.command_palette", "Search and run any gallery command.", "Ctrl+Shift+P");
            Add(d, VpbShortcut.UiScaleUp, "shortcut.ui_scale_up", "keys_chrome",
                "shortcut.ui_scale_up", "UI scale up",
                "shortcut.tip.ui_scale_up", "Enlarge gallery chrome. Separate from grid zoom (Ctrl + wheel).", "Ctrl+Alt+=");
            Add(d, VpbShortcut.UiScaleUpAlt, "shortcut.ui_scale_up_alt", "keys_chrome",
                "shortcut.ui_scale_up_alt", "UI scale up (alternate)",
                "shortcut.tip.ui_scale_up_alt", "Second binding for UI scale up.", "Ctrl+Alt+KeypadPlus");
            Add(d, VpbShortcut.UiScaleDown, "shortcut.ui_scale_down", "keys_chrome",
                "shortcut.ui_scale_down", "UI scale down",
                "shortcut.tip.ui_scale_down", "Shrink gallery chrome. Separate from grid zoom (Ctrl + wheel).", "Ctrl+Alt+-");
            Add(d, VpbShortcut.UiScaleDownAlt, "shortcut.ui_scale_down_alt", "keys_chrome",
                "shortcut.ui_scale_down_alt", "UI scale down (alternate)",
                "shortcut.tip.ui_scale_down_alt", "Second binding for UI scale down.", "Ctrl+Alt+KeypadMinus");

            Add(d, VpbShortcut.FocusSearch, "shortcut.focus_search", "keys_browse",
                "shortcut.focus_search", "Focus search",
                "shortcut.tip.focus_search", "Focus the title search field (settings filter while Settings is open).", "Ctrl+F");
            Add(d, VpbShortcut.FilterPresets, "shortcut.filter_presets", "keys_browse",
                "shortcut.filter_presets", "Filter presets",
                "shortcut.tip.filter_presets", "Open / close the floating filter presets window.", "Alt+F");
            Add(d, VpbShortcut.ImportSidebar, "shortcut.import_sidebar", "keys_browse",
                "shortcut.import_sidebar", "Scene Import panel",
                "shortcut.tip.import_sidebar", "Open / close the floating Scene Import panel.", "Alt+I");
            Add(d, VpbShortcut.LayoutPresets, "shortcut.layout_presets", "keys_browse",
                "shortcut.layout_presets", "Layout presets",
                "shortcut.tip.layout_presets", "Open / close the layout presets manager.", "Alt+L");
            Add(d, VpbShortcut.HistoryRefresh, "shortcut.history_refresh", "keys_browse",
                "shortcut.history_refresh", "Refresh History",
                "shortcut.tip.history_refresh", "Re-query the History category. Only active while browsing History.", "Ctrl+R");

            Add(d, VpbShortcut.Apply, "shortcut.apply", "keys_selection",
                "shortcut.apply", "Apply / load selection",
                "shortcut.tip.apply", "Loads or applies the selected item. Unassigned by default — press SET and choose a key if you want one.", "");
            Add(d, VpbShortcut.ApplyAlt, "shortcut.apply_alt", "keys_selection",
                "shortcut.apply_alt", "Apply / load selection (alternate)",
                "shortcut.tip.apply_alt", "Second binding for apply. Unassigned by default.", "");
            Add(d, VpbShortcut.SelectAll, "shortcut.select_all", "keys_selection",
                "shortcut.select_all", "Select all visible",
                "shortcut.tip.select_all", "Select every item in the current filtered view.", "Ctrl+A");
            Add(d, VpbShortcut.DeleteSelection, "shortcut.delete", "keys_selection",
                "shortcut.delete", "Delete selection",
                "shortcut.tip.delete", "Delete eligible selected packages (History: remove from history).", "Delete");
            Add(d, VpbShortcut.DeleteSelectionAlt, "shortcut.delete_alt", "keys_selection",
                "shortcut.delete_alt", "Delete selection (alternate)",
                "shortcut.tip.delete_alt", "Second binding for delete.", "Backspace");
            Add(d, VpbShortcut.Undo, "shortcut.undo", "keys_selection",
                "shortcut.undo", "Undo",
                "shortcut.tip.undo", "Undo the last supported gallery edit.", "Ctrl+Z");
            Add(d, VpbShortcut.Redo, "shortcut.redo", "keys_selection",
                "shortcut.redo", "Redo",
                "shortcut.tip.redo", "Redo the last undone gallery edit.", "Ctrl+Y");
            Add(d, VpbShortcut.RedoAlt, "shortcut.redo_alt", "keys_selection",
                "shortcut.redo_alt", "Redo (alternate)",
                "shortcut.tip.redo_alt", "Second binding for redo.", "Ctrl+Shift+Z");

            Add(d, VpbShortcut.SceneTools, "shortcut.scene_tools", "keys_tools",
                "shortcut.scene_tools", "Toggle Scene Tools",
                "shortcut.tip.scene_tools", "Open / close Scene Tools.", "Ctrl+Shift+K");
            Add(d, VpbShortcut.StripScene, "shortcut.strip_scene", "keys_tools",
                "shortcut.strip_scene", "Strip Scene picker",
                "shortcut.tip.strip_scene", "Open / close the Strip Scene keep picker directly.", "Ctrl+Shift+S");
            Add(d, VpbShortcut.SceneEraser, "shortcut.scene_eraser", "keys_tools",
                "shortcut.scene_eraser", "Toggle Scene Eraser",
                "shortcut.tip.scene_eraser", "Point-and-click removal of scene items.", "Ctrl+Shift+E");

            Add(d, VpbShortcut.NavUp, "shortcut.nav_up", "keys_world",
                "shortcut.nav_up", "Move up (hold)",
                "shortcut.tip.nav_up", "Raise the navigation rig, complementing WASD. Hold Shift for double speed.", "E", true);
            Add(d, VpbShortcut.NavDown, "shortcut.nav_down", "keys_world",
                "shortcut.nav_down", "Move down (hold)",
                "shortcut.tip.nav_down", "Lower the navigation rig, complementing WASD. Hold Shift for double speed.", "C", true);

            return d;
        }

        private static void Add(VpbShortcutDef[] arr, VpbShortcut id, string key, string section,
            string labelKey, string labelFallback, string tipKey, string tipFallback,
            string defaultPattern, bool held = false)
        {
            arr[(int)id] = new VpbShortcutDef
            {
                Id = id,
                Key = key,
                Section = section,
                LabelKey = labelKey,
                LabelFallback = labelFallback,
                TipKey = tipKey,
                TipFallback = tipFallback,
                DefaultPattern = defaultPattern ?? "",
                Held = held
            };
        }

        internal static void LoadFromConfig(VPBConfig cfg = null)
        {
            for (int i = 0; i < N; i++)
                SetPatternInternal(i, s_Defs[i].DefaultPattern);

            if (cfg == null) cfg = VPBConfig.Instance;

            try
            {
                JSONClass node = cfg != null ? cfg.ShortcutBindings : null;
                if (node != null)
                {
                    for (int i = 0; i < N; i++)
                    {
                        JSONNode v = node[s_Defs[i].Key];
                        if (v == null) continue;
                        SetPatternInternal(i, v.Value ?? "");
                    }
                }
            }
            catch { }

            s_Initialized = cfg != null;
        }

        internal static void SaveToConfig()
        {
            if (!s_Initialized) return;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return;
                JSONClass node = new JSONClass();
                for (int i = 0; i < N; i++)
                {
                    string cur = s_Patterns[i] ?? "";
                    if (string.Equals(cur, s_Defs[i].DefaultPattern ?? "", StringComparison.Ordinal)) continue;
                    node[s_Defs[i].Key] = new JSONData(cur);
                }
                cfg.ShortcutBindings = node;
            }
            catch { }
        }

        private static void EnsureInitialized()
        {
            if (s_Initialized) return;
            int frame = Time.frameCount;
            if (frame - s_LastLoadAttemptFrame < 60) return;
            s_LastLoadAttemptFrame = frame;
            LoadFromConfig();
        }

        internal static string GetPattern(VpbShortcut id)
        {
            EnsureInitialized();
            return s_Patterns[(int)id] ?? "";
        }

        internal static string GetPattern(int index)
        {
            EnsureInitialized();
            if (index < 0 || index >= N) return "";
            return s_Patterns[index] ?? "";
        }

        internal static void SetPattern(int index, string pattern)
        {
            EnsureInitialized();
            if (index < 0 || index >= N) return;
            SetPatternInternal(index, pattern);
        }

        internal static void ApplyPatterns(string[] patterns)
        {
            EnsureInitialized();
            if (patterns == null) return;
            int n = Math.Min(patterns.Length, N);
            for (int i = 0; i < n; i++)
                SetPatternInternal(i, patterns[i]);
        }

        internal static string[] CapturePatterns()
        {
            EnsureInitialized();
            var copy = new string[N];
            for (int i = 0; i < N; i++) copy[i] = s_Patterns[i] ?? "";
            return copy;
        }

        internal static int IndexOfKey(string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey)) return -1;
            for (int i = 0; i < N; i++)
            {
                if (string.Equals(s_Defs[i].Key, rowKey, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static void SetPatternInternal(int index, string pattern)
        {
            string p = pattern ?? "";
            if (string.Equals(s_Patterns[index], p, StringComparison.Ordinal)) return;
            s_Patterns[index] = p;
            s_Bindings[index] = ParseBinding(p);
            VpbShortcutText.BumpRevision();
        }

        private static Dictionary<string, int> s_ShortIdIndex;

        internal static string GetPatternByShortId(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return "";
            EnsureInitialized();

            if (s_ShortIdIndex == null)
            {
                var map = new Dictionary<string, int>(N, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < N; i++)
                {
                    string k = s_Defs[i].Key ?? "";
                    if (k.StartsWith(ShortIdPrefix, StringComparison.OrdinalIgnoreCase))
                        map[k.Substring(ShortIdPrefix.Length)] = i;
                }
                s_ShortIdIndex = map;
            }

            int index;
            return s_ShortIdIndex.TryGetValue(shortId, out index) ? (s_Patterns[index] ?? "") : "";
        }

        private const string ShortIdPrefix = "shortcut.";

        private static Binding ParseBinding(string pattern)
        {
            Binding b = default(Binding);
            if (string.IsNullOrEmpty(pattern)) return b;

            string[] parts = pattern.Split('+');
            int last = parts.Length - 1;
            if (last > 0 && parts[last].Length == 0) parts[last] = "+";

            for (int i = 0; i < last; i++)
            {
                string m = parts[i].Trim().ToLowerInvariant();
                if (m.Length == 0) continue;
                if (m == "ctrl" || m == "control") b.Ctrl = true;
                else if (m == "shift") b.Shift = true;
                else if (m == "alt") b.Alt = true;
                else return default(Binding);
            }

            KeyCode k = KeyUtil.TryParseKeyToken(parts[last]);
            if (k == KeyCode.None || KeyUtil.IsDisallowedHotkeyKey(k)) return default(Binding);

            b.Key = k;
            b.Valid = true;
            return b;
        }

        internal static bool Down(VpbShortcut id)
        {
            if (!Input.anyKeyDown) return false;
            EnsureInitialized();
            if (!GateOpen()) return false;

            Binding b = s_Bindings[(int)id];
            if (!b.Valid) return false;
            if (!Input.GetKeyDown(b.Key)) return false;
            if (!ModifiersMatchExact(b)) return false;
            return !VpbShortcutGate.IsClaimedByGlobalHotkey(b.Key);
        }

        internal static bool DownIgnoringPaneGate(VpbShortcut id)
        {
            if (!Input.anyKeyDown) return false;
            EnsureInitialized();
            if (!VpbShortcutGate.WindowFocusAllowed()) return false;

            Binding b = s_Bindings[(int)id];
            if (!b.Valid) return false;
            if (!Input.GetKeyDown(b.Key)) return false;
            if (!ModifiersMatchExact(b)) return false;
            return !VpbShortcutGate.IsClaimedByGlobalHotkey(b.Key);
        }

        internal static bool Held(VpbShortcut id)
        {
            EnsureInitialized();
            if (!VpbShortcutGate.WindowFocusAllowed()) return false;

            Binding b = s_Bindings[(int)id];
            if (!b.Valid) return false;
            if (!Input.GetKey(b.Key)) return false;

            if (b.Ctrl != (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return false;
            if (b.Alt != (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) return false;
            if (b.Shift && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return false;
            return true;
        }

        private static bool ModifiersMatchExact(Binding b)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl != b.Ctrl) return false;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift != b.Shift) return false;
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            return alt == b.Alt;
        }

        private static bool GateOpen()
        {
            int frame = Time.frameCount;
            if (s_GateFrame == frame) return s_GateOpen;
            s_GateFrame = frame;
            s_GateOpen = VpbShortcutGate.PaneShortcutsAllowed();
            return s_GateOpen;
        }

        internal static string NormalizePattern(string pattern)
        {
            Binding b = ParseBinding(pattern);
            if (!b.Valid) return "";
            string s = "";
            if (b.Ctrl) s += "Ctrl+";
            if (b.Shift) s += "Shift+";
            if (b.Alt) s += "Alt+";
            return s + b.Key.ToString();
        }
    }

    internal static class VpbShortcutGate
    {
        private static int s_FocusFrame = -1;
        private static bool s_FocusOk = true;

        private static int s_VisibleFrame = -1;
        private static bool s_AnyPaneVisible;

        private static KeyCode[] s_GlobalKeys = new KeyCode[0];
        private static int s_GlobalKeyCount;

        internal static void SetGlobalHotkeyKeys(KeyCode[] keys, int count)
        {
            s_GlobalKeys = keys ?? new KeyCode[0];
            s_GlobalKeyCount = Mathf.Clamp(count, 0, s_GlobalKeys.Length);
        }

        internal static bool IsClaimedByGlobalHotkey(KeyCode k)
        {
            for (int i = 0; i < s_GlobalKeyCount; i++)
            {
                if (s_GlobalKeys[i] == k) return true;
            }
            return false;
        }

        internal static bool WindowFocusAllowed()
        {
            int frame = Time.frameCount;
            if (s_FocusFrame == frame) return s_FocusOk;
            s_FocusFrame = frame;

            bool ok = true;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null && cfg.ShortcutsRequireWindowFocus && !XrUtils.IsVrActive())
                    ok = Application.isFocused;
            }
            catch { ok = true; }

            s_FocusOk = ok;
            return ok;
        }

        internal static bool AnyPaneVisible()
        {
            int frame = Time.frameCount;
            if (s_VisibleFrame == frame) return s_AnyPaneVisible;
            s_VisibleFrame = frame;

            bool vis = false;
            try
            {
                var g = Gallery.singleton;
                var panels = g != null ? g.Panels : null;
                if (panels != null)
                {
                    for (int i = 0; i < panels.Count; i++)
                    {
                        var p = panels[i];
                        if (p != null && p.HasOnScreenUi) { vis = true; break; }
                    }
                }
            }
            catch { vis = false; }

            s_AnyPaneVisible = vis;
            return vis;
        }

        internal static bool PaneShortcutsAllowed()
        {
            if (!WindowFocusAllowed()) return false;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null && cfg.ShortcutsNeedVisiblePane && !AnyPaneVisible()) return false;
            }
            catch { }
            return true;
        }

        internal static bool GlobalHotkeyAllowed(bool opensUi)
        {
            if (!WindowFocusAllowed()) return false;
            if (opensUi) return true;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null && cfg.ShortcutsNeedVisiblePane && !AnyPaneVisible()) return false;
            }
            catch { }
            return true;
        }
    }
}
