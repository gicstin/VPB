using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private string _pluginDraftGalleryKey;
        private string _pluginDraftCreateGalleryKey;
        private string _pluginDraftHubKey;
        private string _pluginDraftClearConsoleKey;
        private string _pluginHotkeyCaptureRowKey;
        private float _pluginHotkeyCaptureIgnoreUntilRealtime;

        /// <summary>True while settings UI is waiting for the user to press a hotkey binding.</summary>
        public bool IsPluginHotkeyCaptureActive()
        {
            return IsSettingsPanelOpen() && !string.IsNullOrEmpty(_pluginHotkeyCaptureRowKey);
        }

        private void PluginSettingsBeginSession()
        {
            try
            {
                var s = Settings.Instance;
                _pluginDraftGalleryKey = SanitizePluginHotkeyDraft(s?.GalleryKey != null ? s.GalleryKey.Value : "", "Ctrl+G");
                _pluginDraftCreateGalleryKey = SanitizePluginHotkeyDraft(s?.CreateGalleryKey != null ? s.CreateGalleryKey.Value : "", "Ctrl+N");
                _pluginDraftHubKey = SanitizePluginHotkeyDraft(s?.HubKey != null ? s.HubKey.Value : "", "Ctrl+H");
                _pluginDraftClearConsoleKey = SanitizePluginHotkeyDraft(s?.ClearConsoleKey != null ? s.ClearConsoleKey.Value : "", "F2");
            }
            catch
            {
                _pluginDraftGalleryKey = "";
                _pluginDraftCreateGalleryKey = "";
                _pluginDraftHubKey = "";
                _pluginDraftClearConsoleKey = "";
            }
            ShortcutSettingsBeginSession();
            CancelPluginHotkeyCapture(false);
        }

        private void PluginSettingsEndSession()
        {
            CancelPluginHotkeyCapture(false);
        }

        private static void CapturePluginSettingsIntoSnapshot(InternalSettingsSnapshot snap)
        {
            if (snap == null) return;
            try
            {
                var s = Settings.Instance;
                snap.PluginGalleryKey = s?.GalleryKey != null ? s.GalleryKey.Value : "";
                snap.PluginCreateGalleryKey = s?.CreateGalleryKey != null ? s.CreateGalleryKey.Value : "";
                snap.PluginHubKey = s?.HubKey != null ? s.HubKey.Value : "";
                snap.PluginClearConsoleKey = s?.ClearConsoleKey != null ? s.ClearConsoleKey.Value : "";
                snap.PluginDownscale8kTo4k = s?.Downscale8kTo4kBeforeZstdCache != null && s.Downscale8kTo4kBeforeZstdCache.Value;
            }
            catch { }
            try { snap.ShortcutPatterns = VpbShortcutMap.CapturePatterns(); }
            catch { snap.ShortcutPatterns = null; }
            try { snap.PluginScanWhitelistEnabled = ScanWhitelistManager.Instance.IsEnabled; }
            catch { snap.PluginScanWhitelistEnabled = false; }
        }

        private void RestorePluginSettingsFromSnapshot(InternalSettingsSnapshot b)
        {
            if (b == null) return;
            try
            {
                var s = Settings.Instance;
                if (s?.GalleryKey != null) s.GalleryKey.Value = b.PluginGalleryKey ?? "";
                if (s?.CreateGalleryKey != null) s.CreateGalleryKey.Value = b.PluginCreateGalleryKey ?? "";
                if (s?.HubKey != null) s.HubKey.Value = b.PluginHubKey ?? "";
                if (s?.ClearConsoleKey != null) s.ClearConsoleKey.Value = b.PluginClearConsoleKey ?? "";
                if (s?.Downscale8kTo4kBeforeZstdCache != null) s.Downscale8kTo4kBeforeZstdCache.Value = b.PluginDownscale8kTo4k;
            }
            catch { }
            try
            {
                if (ScanWhitelistManager.Instance.IsEnabled != b.PluginScanWhitelistEnabled)
                {
                    ScanWhitelistManager.Instance.SetEnabled(b.PluginScanWhitelistEnabled);
                    ScanWhitelistManager.Instance.Save();
                    try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
                }
            }
            catch { }
            _pluginDraftGalleryKey = b.PluginGalleryKey ?? "";
            _pluginDraftCreateGalleryKey = b.PluginCreateGalleryKey ?? "";
            _pluginDraftHubKey = b.PluginHubKey ?? "";
            _pluginDraftClearConsoleKey = b.PluginClearConsoleKey ?? "";
            try
            {
                if (b.ShortcutPatterns != null)
                {
                    VpbShortcutMap.ApplyPatterns(b.ShortcutPatterns);
                    VpbShortcutMap.SaveToConfig();
                }
                _shortcutDrafts = VpbShortcutMap.CapturePatterns();
                _shortcutConflictDirty = true;
            }
            catch { }
            try { VamHookPlugin.singleton?.ApplyGalleryPluginHotkeysFromSettings(); } catch { }
            NotifyShortcutBindingsChanged();
        }

        private bool CommitPluginSettingsFromDrafts(bool showErrors)
        {
            var plugin = VamHookPlugin.singleton;
            if (plugin == null) return true;
            string err;
            if (!plugin.TryApplyGalleryPluginHotkeys(
                    _pluginDraftGalleryKey ?? "",
                    _pluginDraftCreateGalleryKey ?? "",
                    _pluginDraftHubKey ?? "",
                    _pluginDraftClearConsoleKey ?? "",
                    out err))
            {
                if (showErrors) ShowTemporaryStatus(err ?? VPBTranslation.T("hook.settings.error.invalid_hotkey", "Invalid setting. Example hotkey: Ctrl+Shift+V"), 4f);
                return false;
            }
            try { CommitShortcutDrafts(); } catch { }
            try { Settings.SaveConfig(); } catch { }
            NotifyShortcutBindingsChanged();
            return true;
        }

        private void AppendPluginInternalSettingDefinitions(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;

            AppendShortcutRuleSettingDefinitions(defs);

            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.hotkey.gallery",
                GroupKey = "plugin_hotkeys",
                Label = VPBTranslation.T("hook.settings.key.gallery", "Show/Hide Panes"),
                Tooltip = VPBTranslation.T("settings.tip.plugin_hotkey_gallery", "Toggle visibility of gallery panes."),
                ControlType = InternalSettingControlType.Hotkey,
                GetString = () => _pluginDraftGalleryKey ?? "",
                SetString = v => _pluginDraftGalleryKey = v ?? ""
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.hotkey.create_gallery",
                GroupKey = "plugin_hotkeys",
                Label = VPBTranslation.T("hook.settings.key.create_gallery", "Create Gallery Pane"),
                Tooltip = VPBTranslation.T("settings.tip.plugin_hotkey_create_gallery", "Open a new gallery pane."),
                ControlType = InternalSettingControlType.Hotkey,
                GetString = () => _pluginDraftCreateGalleryKey ?? "",
                SetString = v => _pluginDraftCreateGalleryKey = v ?? ""
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.hotkey.hub",
                GroupKey = "plugin_hotkeys",
                Label = VPBTranslation.T("hook.settings.key.hub", "Open Hub Browser"),
                Tooltip = VPBTranslation.T("settings.tip.plugin_hotkey_hub", "Open Hub browse."),
                ControlType = InternalSettingControlType.Hotkey,
                GetString = () => _pluginDraftHubKey ?? "",
                SetString = v => _pluginDraftHubKey = v ?? ""
            });
            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.hotkey.clear_console",
                GroupKey = "plugin_hotkeys",
                Label = VPBTranslation.T("hook.settings.key.clear_console", "Clear Console"),
                Tooltip = VPBTranslation.T("settings.tip.plugin_hotkey_clear_console", "Clear BepInEx console output."),
                ControlType = InternalSettingControlType.Hotkey,
                GetString = () => _pluginDraftClearConsoleKey ?? "",
                SetString = v => _pluginDraftClearConsoleKey = v ?? ""
            });

            AppendShortcutBindingSettingDefinitions(defs);

            int downscaleActive = 0;
            try { downscaleActive = TextureUtil.GetDownscaledActiveCount(); } catch { }
            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.zstd.compress_run",
                GroupKey = "plugin_zstd",
                Label = VPBTranslation.T("hook.compress_cache", "Compress Cache"),
                Tooltip = VPBTranslation.T("settings.tip.compress_cache_run", "Start bulk .vamcache → Zstd migration. Quiet progress in the top bar; report when done."),
                ControlType = InternalSettingControlType.Button,
                OnAction = () => GalleryTriggerBulkZstdCompression()
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.zstd.downscale_8k",
                GroupKey = "plugin_zstd",
                Label = string.Format(VPBTranslation.T("hook.downscale_8k_4k", "Downscale 8K->4K (In Scene: {0})"), downscaleActive),
                Tooltip = VPBTranslation.T("settings.tip.downscale_8k_4k", "Downscale 8192² textures to 4096² before writing Zstd cache entries."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => Settings.Instance != null && Settings.Instance.Downscale8kTo4kBeforeZstdCache != null && Settings.Instance.Downscale8kTo4kBeforeZstdCache.Value,
                SetBool = v => { if (Settings.Instance?.Downscale8kTo4kBeforeZstdCache != null) Settings.Instance.Downscale8kTo4kBeforeZstdCache.Value = v; }
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.scan_whitelist.enable",
                GroupKey = "plugin_scan_whitelist",
                Label = VPBTranslation.T("hook.settings.scan_whitelist.enable", "Enable VaM scan whitelist"),
                Tooltip = VPBTranslation.T("settings.tip.scan_whitelist_enable", "When enabled, VaM only scans whitelisted package paths at startup; VPB index can still see all local .var files."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => { try { return ScanWhitelistManager.Instance.IsEnabled; } catch { return false; } },
                SetBool = v => PluginSettingsSetScanWhitelistEnabled(v)
            });

            if (ScanWhitelistManager.Instance.IsEnabledButEmpty())
            {
                defs.Add(new InternalSettingDefinition
                {
                    Key = "plugin.scan_whitelist.empty_warn",
                    GroupKey = "plugin_scan_whitelist",
                    Label = VPBTranslation.T("hook.settings.scan_whitelist.empty_warning", "⚠ Warning: whitelist is enabled but empty — all packages will be excluded from VaM's scan!"),
                    Tooltip = VPBTranslation.T("settings.tip.scan_whitelist_empty", "Add folders or package overrides in Manage Scan Whitelist."),
                    ControlType = InternalSettingControlType.Button,
                    OnAction = null
                });
            }

            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.scan_whitelist.manage",
                GroupKey = "plugin_scan_whitelist",
                Label = VPBTranslation.T("hook.settings.scan_whitelist.manage", "Manage Scan Whitelist..."),
                Tooltip = VPBTranslation.T("settings.tip.scan_whitelist_manage", "Edit whitelisted folders and per-package UID overrides."),
                ControlType = InternalSettingControlType.Button,
                OnAction = () =>
                {
                    try { VamHookPlugin.singleton?.OpenScanWhitelistManagerFromGallery(); }
                    catch (Exception ex) { LogUtil.LogError("[VPB] OpenScanWhitelistManager: " + ex.Message); }
                }
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "plugin.qm_positions",
                GroupKey = "plugin_quickmenu",
                Label = VPBTranslation.T("hook.settings.adjust_position", "Adjust Position"),
                Tooltip = VPBTranslation.T("settings.tip.qm_positions", "Edit Quick Menu Create Gallery grid anchor (desktop). Live preview while adjusting."),
                ControlType = InternalSettingControlType.Button,
                OnAction = () =>
                {
                    try { VamHookPlugin.singleton?.OpenQuickMenuPositionEditorFromGallery(); }
                    catch (Exception ex) { LogUtil.LogError("[VPB] OpenQuickMenuPositionEditor: " + ex.Message); }
                }
            });
        }

        private void PluginSettingsSetScanWhitelistEnabled(bool enabled)
        {
            try
            {
                if (!enabled && ScanWhitelistManager.Instance.IsEnabled)
                {
                    VamHookPlugin.singleton?.RequestScanWhitelistDisableConfirmFromGallery();
                    RefreshInternalSettingsListRows(true);
                    return;
                }
                if (enabled && !ScanWhitelistManager.Instance.IsEnabled)
                {
                    ScanWhitelistManager.Instance.SetEnabled(true);
                    ScanWhitelistManager.Instance.Save();
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PluginSettingsSetScanWhitelistEnabled: " + ex.Message);
            }
            InvalidateInternalSettingsDefsCache();
            RefreshInternalSettingsListRows(true);
        }

        private void CancelPluginHotkeyCapture(bool refreshRows = true)
        {
            if (string.IsNullOrEmpty(_pluginHotkeyCaptureRowKey)) return;
            _pluginHotkeyCaptureRowKey = null;
            if (refreshRows)
                RefreshInternalSettingsListRows(true);
        }

        private static string SanitizePluginHotkeyDraft(string value, string fallback)
        {
            if (KeyUtil.UsesDisallowedHotkeyKey(value)) return fallback ?? "";
            return value ?? "";
        }

        private void BeginPluginHotkeyCapture(string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey)) return;
            _pluginHotkeyCaptureRowKey = rowKey;
            _pluginHotkeyCaptureIgnoreUntilRealtime = Time.unscaledTime + 0.25f;
            RefreshInternalSettingsListRows(true);
        }

        private void PluginSettingsHotkeyCaptureUpdate()
        {
            if (!IsSettingsPanelOpen() || string.IsNullOrEmpty(_pluginHotkeyCaptureRowKey))
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPluginHotkeyCapture(true);
                return;
            }

            if (Time.unscaledTime < _pluginHotkeyCaptureIgnoreUntilRealtime)
                return;

            if (!Input.anyKeyDown) return;

            string pattern;
            if (!TryReadCapturedHotkeyPattern(out pattern))
                return;

            ApplyPluginHotkeyDraft(_pluginHotkeyCaptureRowKey, pattern);
            CancelPluginHotkeyCapture(true);
        }

        private static bool IsHotkeyModifierKey(KeyCode kc)
        {
            return kc == KeyCode.LeftControl || kc == KeyCode.RightControl
                || kc == KeyCode.LeftShift || kc == KeyCode.RightShift
                || kc == KeyCode.LeftAlt || kc == KeyCode.RightAlt;
        }

        private static string KeyCodeToHotkeyToken(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.BackQuote: return "`";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Alpha0: return "0";
                case KeyCode.Alpha1: return "1";
                case KeyCode.Alpha2: return "2";
                case KeyCode.Alpha3: return "3";
                case KeyCode.Alpha4: return "4";
                case KeyCode.Alpha5: return "5";
                case KeyCode.Alpha6: return "6";
                case KeyCode.Alpha7: return "7";
                case KeyCode.Alpha8: return "8";
                case KeyCode.Alpha9: return "9";
                default: return kc.ToString();
            }
        }

        private static bool TryReadCapturedHotkeyPattern(out string pattern)
        {
            pattern = null;
            KeyCode main = KeyCode.None;
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None || !Input.GetKeyDown(kc)) continue;
                if (kc == KeyCode.Escape || IsHotkeyModifierKey(kc) || KeyUtil.IsDisallowedHotkeyKey(kc)) continue;
                main = kc;
                break;
            }
            if (main == KeyCode.None) return false;

            var parts = new List<string>(4);
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) parts.Add("Ctrl");
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) parts.Add("Shift");
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) parts.Add("Alt");
            parts.Add(KeyCodeToHotkeyToken(main));
            pattern = string.Join("+", parts.ToArray());
            return !string.IsNullOrEmpty(pattern);
        }

        private void ApplyPluginHotkeyDraft(string rowKey, string pattern)
        {
            int shortcutIndex;
            if (TryGetShortcutRowIndex(rowKey, out shortcutIndex))
            {
                SetShortcutDraft(shortcutIndex, pattern);
                return;
            }

            if (string.Equals(rowKey, "plugin.hotkey.gallery", StringComparison.OrdinalIgnoreCase))
                _pluginDraftGalleryKey = pattern;
            else if (string.Equals(rowKey, "plugin.hotkey.create_gallery", StringComparison.OrdinalIgnoreCase))
                _pluginDraftCreateGalleryKey = pattern;
            else if (string.Equals(rowKey, "plugin.hotkey.hub", StringComparison.OrdinalIgnoreCase))
                _pluginDraftHubKey = pattern;
            else if (string.Equals(rowKey, "plugin.hotkey.clear_console", StringComparison.OrdinalIgnoreCase))
                _pluginDraftClearConsoleKey = pattern;
            _shortcutConflictDirty = true;
        }

        private string GetPluginHotkeyDraftDisplay(string rowKey)
        {
            int shortcutIndex;
            if (TryGetShortcutRowIndex(rowKey, out shortcutIndex))
                return GetShortcutDraft(shortcutIndex);

            if (string.Equals(rowKey, "plugin.hotkey.gallery", StringComparison.OrdinalIgnoreCase)) return _pluginDraftGalleryKey ?? "";
            if (string.Equals(rowKey, "plugin.hotkey.create_gallery", StringComparison.OrdinalIgnoreCase)) return _pluginDraftCreateGalleryKey ?? "";
            if (string.Equals(rowKey, "plugin.hotkey.hub", StringComparison.OrdinalIgnoreCase)) return _pluginDraftHubKey ?? "";
            if (string.Equals(rowKey, "plugin.hotkey.clear_console", StringComparison.OrdinalIgnoreCase)) return _pluginDraftClearConsoleKey ?? "";
            return "";
        }

        private void RebuildPluginHotkeyRowControls(Transform controls, InternalSettingDefinition def, float paneScale)
        {
            string rowKey = def.Key ?? "";
            string display = GetPluginHotkeyDraftDisplay(rowKey);
            bool capturing = string.Equals(_pluginHotkeyCaptureRowKey, rowKey, StringComparison.OrdinalIgnoreCase);
            bool unassigned = string.IsNullOrEmpty(display);
            bool conflict = !capturing && !unassigned && ShortcutPatternHasConflict(display);

            string displayText = capturing
                ? VPBTranslation.T("settings.hotkey.press_key", "Press key…")
                : (unassigned ? VPBTranslation.T("settings.hotkey.unassigned", "Off") : VpbShortcutText.PrettyPattern(display));

            GameObject host = new GameObject("SettingsHotkeyHost");
            host.transform.SetParent(controls, false);
            float chipH = GalleryUiDesignTokens.ButtonSizeRef * paneScale;
            LayoutElement hle = UI.AddLE(host, minWidth: 120f * paneScale, minHeight: chipH, preferredWidth: 220f * paneScale, preferredHeight: chipH, flexibleWidth: 1f);

            Color chipBg = UI.ChromeDarker;
            if (capturing) chipBg = new Color(0.35f, 0.35f, 0.15f, 1f);
            else if (conflict) chipBg = new Color(0.40f, 0.20f, 0.16f, 1f);

            Image bg = AddSettingsControlRoundedBg(host, chipBg);
            Button hit = host.AddComponent<Button>();
            hit.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(hit);

            Color chipText = Color.white;
            if (capturing) chipText = new Color(1f, 0.95f, 0.6f, 1f);
            else if (conflict) chipText = new Color(1f, 0.72f, 0.62f, 1f);
            else if (unassigned) chipText = new Color(1f, 1f, 1f, 0.45f);

            Text it = UI.CreateLabel(host, displayText, GalleryUiDesignTokens.SettingsListRowDetailFontRef, chipText, TextAnchor.MiddleCenter, richText: false, name: "Text");
            GalleryUiMetrics.ApplyFont(it, GalleryUiDesignTokens.SettingsListRowDetailFontRef, paneScale, GalleryUiDesignTokens.FontMinRef);

            string capturedKey = rowKey;
            string capturedPattern = display;
            hit.onClick.AddListener(() => BeginPluginHotkeyCapture(capturedKey));
            AddDynamicTooltip(host, () => BuildHotkeyChipTooltip(capturedKey, capturedPattern));

            if (capturing)
            {
                CreateMiniButton(controls, VPBTranslation.T("settings.hotkey.cancel", "CANCEL"), 72f, new Color(0.45f, 0.25f, 0.25f, 1f), () =>
                {
                    CancelPluginHotkeyCapture(true);
                });
            }
            else
            {
                CreateMiniButton(controls, VPBTranslation.T("settings.hotkey.set", "SET"), 56f, new Color(0.25f, 0.5f, 0.8f, 1f), () =>
                {
                    BeginPluginHotkeyCapture(capturedKey);
                });
                if (!unassigned)
                {
                    CreateMiniButton(controls, VPBTranslation.T("settings.hotkey.clear", "CLEAR"), 64f, new Color(0.38f, 0.30f, 0.22f, 1f), () =>
                    {
                        ApplyPluginHotkeyDraft(capturedKey, "");
                        RefreshInternalSettingsListRows(true);
                    });
                }
            }
        }

        private string BuildHotkeyChipTooltip(string rowKey, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return VPBTranslation.T("settings.hotkey.tip.unassigned",
                    "This shortcut is off. Click, or press SET, then press the keys you want.");

            string conflict = BuildShortcutConflictTooltip(rowKey, pattern);
            string baseTip = VPBTranslation.T("settings.hotkey.tip.assigned",
                "Click to record a new key. CLEAR turns this shortcut off.");
            return string.IsNullOrEmpty(conflict) ? baseTip : conflict + "\n" + baseTip;
        }

        internal bool TryCommitPluginSettingsOnSave()
        {
            return CommitPluginSettingsFromDrafts(true);
        }
    }
}
