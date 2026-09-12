using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const long DiagLogCopyContentsMaxBytes = 256 * 1024;

        private static int _diagStartupCaptureState;
        private static bool _diagStartupWasOnAtLaunch;

        private GameObject footerLogLevelBtn;
        private Image footerLogLevelBtnImage;
        private Text footerLogLevelBtnText;

        private void AppendDiagnosticsInternalSettingDefinitions(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;
            if (Settings.Instance == null) return;
            StartupLoggingActiveThisLaunch();

            var level = new InternalSettingDefinition
            {
                Key = "diag.level",
                GroupKey = "diag_logs",
                Label = VPBTranslation.T("settings.diag_level", "Log detail"),
                Tooltip = VPBTranslation.T("settings.tip.diag_level",
                    "Turn this up only when someone helping you asks for a log, then do the bug once and send this session's file.\n\nNormal — everyday logging.\nExtra — records what VPB is doing, including the Hub and the gallery window. Use this when a helper asks for a log.\nFull — Extra plus slowdown reports for scene loading, frame rate, memory and saving. Restart VaM once if they need startup detail.\n\nSwitch back to Normal afterwards: Extra logs are much larger and Full makes VaM a little slower.\n\nAlso matches: off, detailed, verbose, everything."),
                ControlType = InternalSettingControlType.Choice,
                Options = new[] { VpbDiagnosticLogLevel.Normal, VpbDiagnosticLogLevel.Extra, VpbDiagnosticLogLevel.Full },
                GetString = CurrentDiagnosticLevel,
                SetString = ApplyDiagnosticLevel
            };
            level.SetDefault(VpbDiagnosticLogLevel.Normal);
            defs.Add(level);

            defs.Add(new InternalSettingDefinition
            {
                Key = "diag.recipe",
                GroupKey = "diag_logs",
                Label = VPBTranslation.T("settings.diag_recipe", "Next"),
                Tooltip = VPBTranslation.T("settings.tip.diag_recipe",
                    "Leave logging up, reproduce the problem once, copy this session's log, then switch back to Normal."),
                ControlType = InternalSettingControlType.ReadOnlyText,
                WrapValue = true,
                GetString = DiagnosticRecipeText,
                RowVisible = () => VpbDiagnosticLogLevel.IsElevated(CurrentDiagnosticLevel())
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "diag.restartHint",
                GroupKey = "diag_logs",
                Label = VPBTranslation.T("settings.diag_restart_hint", "Startup not recorded yet"),
                Tooltip = VPBTranslation.T("settings.tip.diag_restart_hint",
                    "VaM had already finished starting up before you switched this on, so this session's log has no startup detail. Restart VaM and the next one will."),
                ControlType = InternalSettingControlType.ReadOnlyText,
                GetString = () => VPBTranslation.T("settings.diag_restart_hint_value", "Restart VaM to include it"),
                RowVisible = () => !StartupLoggingActiveThisLaunch()
                    && string.Equals(CurrentDiagnosticLevel(), VpbDiagnosticLogLevel.Full, StringComparison.OrdinalIgnoreCase)
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "diag.logFile",
                GroupKey = "diag_logs",
                Label = VPBTranslation.T("settings.diag_log_file", "This session's log"),
                Tooltip = VPBTranslation.T("settings.tip.diag_log_file",
                    "VPB writes a fresh log every time VaM starts. If someone asks for \"the VPB log\", this is the file they mean — not one of the older files in the folder."),
                ControlType = InternalSettingControlType.ReadOnlyText,
                GetString = () =>
                {
                    string name = null;
                    try { name = VPBLogger.SessionFileName; } catch { }
                    return string.IsNullOrEmpty(name)
                        ? VPBTranslation.T("settings.diag_log_file_none", "Not started yet — launch a scene or click Copy after VPB has started.")
                        : name;
                }
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "diag.copyLogFile",
                GroupKey = "diag_logs",
                Label = VPBTranslation.T("settings.diag_copy_log_file", "Copy this log"),
                Tooltip = VPBTranslation.T("settings.tip.diag_copy_log_file",
                    "Copy the full path of this session's log file so you can paste it or attach that file. This is the primary Send-a-log action."),
                ControlType = InternalSettingControlType.Button,
                OnAction = CopyDiagnosticsLogFilePath
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "diag.copyLogText",
                GroupKey = "diag_logs",
                Label = VPBTranslation.T("settings.diag_copy_log_text", "Copy log text"),
                Tooltip = VPBTranslation.T("settings.tip.diag_copy_log_text",
                    "Copy the log contents to the clipboard so you can paste into Discord or a message. Use this in VR when a file window is hard to reach. Very large logs copy the path instead."),
                ControlType = InternalSettingControlType.Button,
                OnAction = CopyDiagnosticsLogContents,
                RowVisible = DiagnosticCopyTextRowVisible
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "diag.openLogFolder",
                GroupKey = "diag_logs",
                Label = VPBTranslation.T("settings.diag_open_log_folder", "Show in folder"),
                Tooltip = VPBTranslation.T("settings.tip.diag_open_log_folder",
                    "Open the folder holding your VPB logs in a file window. Attach this session's file (named above), not an older one. VPB keeps the current log plus the last four."),
                ControlType = InternalSettingControlType.Button,
                OnAction = OpenDiagnosticsLogFolder
            });
        }

        private static bool DiagnosticCopyTextRowVisible()
        {
            try { return XrUtils.IsVrActive(); }
            catch { return false; }
        }

        private static string DiagnosticRecipeText()
        {
            if (string.Equals(CurrentDiagnosticLevel(), VpbDiagnosticLogLevel.Full, StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("settings.diag_recipe.full",
                    "1. Restart VaM if they need startup.  2. Do the bug once.  3. Copy this log.  4. Switch back to Normal.");
            return VPBTranslation.T("settings.diag_recipe.extra",
                "1. Leave Extra on.  2. Do the bug once.  3. Copy this log.  4. Switch back to Normal.");
        }

        private static bool DiagnosticToggleOn(ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }

        private static void SetDiagnosticToggle(ConfigEntry<bool> entry, bool value)
        {
            try { if (entry != null && entry.Value != value) entry.Value = value; }
            catch { }
        }

        private static bool StartupLoggingActiveThisLaunch()
        {
            if (_diagStartupCaptureState == 0)
            {
                _diagStartupCaptureState = 1;
                var s = Settings.Instance;
                _diagStartupWasOnAtLaunch = s != null && DiagnosticToggleOn(s.LogStartupDetails);
            }
            return _diagStartupWasOnAtLaunch;
        }

        private static bool DiagnosticExtraBundleOn()
        {
            var s = Settings.Instance;
            if (s == null) return false;
            return DiagnosticToggleOn(s.VerboseLogging)
                || DiagnosticToggleOn(s.LogHubRequests)
                || DiagnosticToggleOn(s.LogVerboseUi);
        }

        private static bool DiagnosticFullBundleOn()
        {
            var s = Settings.Instance;
            if (s == null) return false;
            return DiagnosticToggleOn(s.LogStartupDetails)
                || DiagnosticToggleOn(s.LoadProfileScenePhases)
                || DiagnosticToggleOn(s.LogPerfDiagnostics)
                || DiagnosticToggleOn(s.LogPerfTelemetry)
                || DiagnosticToggleOn(s.LogSavePerf);
        }

        private static string CurrentDiagnosticLevel()
        {
            return VpbDiagnosticLogLevel.FromFlags(DiagnosticExtraBundleOn(), DiagnosticFullBundleOn());
        }

        private void ApplyDiagnosticLevel(string level)
        {
            ApplyDiagnosticLevelCore(level, true);
        }

        private void ApplyDiagnosticLevelCore(string level, bool refreshSettingsRows)
        {
            var s = Settings.Instance;
            if (s == null) return;
            StartupLoggingActiveThisLaunch();

            bool extraBundle;
            bool fullBundle;
            VpbDiagnosticLogLevel.Decode(level, out extraBundle, out fullBundle);

            SetDiagnosticToggle(s.VerboseLogging, extraBundle);
            SetDiagnosticToggle(s.LogHubRequests, extraBundle);
            SetDiagnosticToggle(s.LogVerboseUi, extraBundle);
            SetDiagnosticToggle(s.LogStartupDetails, fullBundle);
            SetDiagnosticToggle(s.LoadProfileScenePhases, fullBundle);
            SetDiagnosticToggle(s.LogPerfDiagnostics, fullBundle);
            SetDiagnosticToggle(s.LogPerfTelemetry, fullBundle);
            SetDiagnosticToggle(s.LogSavePerf, fullBundle);

            try { Settings.SaveConfig(); } catch { }
            try { VPBLogger.RefreshOptions(); } catch { }
            try { VamLoadPerfHooks.ApplySettings(); } catch { }
            NotifyDiagnosticLogLevelChrome(refreshSettingsRows);
        }

        private static void CaptureDiagnosticLogLevelIntoSnapshot(InternalSettingsSnapshot snap)
        {
            if (snap == null) return;
            snap.DiagnosticLogLevel = CurrentDiagnosticLevel();
        }

        private void RestoreDiagnosticLogLevelFromSnapshot(InternalSettingsSnapshot b)
        {
            if (b == null) return;
            ApplyDiagnosticLevelCore(b.DiagnosticLogLevel ?? VpbDiagnosticLogLevel.Normal, false);
        }

        private static void NotifyDiagnosticLogLevelChrome(bool refreshSettingsRows)
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
                    try { p.UpdateFooterLogLevelChip(); } catch { }
                    try
                    {
                        p.InvalidateFooterOverflowLayout();
                        p.ApplyFooterOverflowLayout(p.ChromeScale);
                    }
                    catch { }
                    if (!refreshSettingsRows) continue;
                    try
                    {
                        if (p.IsSettingsPanelOpen())
                            p.RefreshInternalSettingsListRows(true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void OpenFooterLogLevelSettings()
        {
            try { CloseFooterOverflowMenu(); } catch { }
            OpenSettingsGroup("troubleshooting");
        }

        private string FooterLogLevelChipLabel()
        {
            string level = CurrentDiagnosticLevel();
            if (string.Equals(level, VpbDiagnosticLogLevel.Full, StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.footer.log_full", "Log Full");
            return VPBTranslation.T("gallery.footer.log_extra", "Log Extra");
        }

        private string FooterLogLevelChipTooltip()
        {
            string level = CurrentDiagnosticLevel();
            if (string.Equals(level, VpbDiagnosticLogLevel.Full, StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("gallery.tooltip.log_level_full",
                    "Full logging is on (larger file, VaM a little slower). Click to open Troubleshooting — switch back to Normal when the log is sent.");
            return VPBTranslation.T("gallery.tooltip.log_level_extra",
                "Extra logging is on. Click to open Troubleshooting — switch back to Normal when the log is sent.");
        }

        private void UpdateFooterLogLevelChip()
        {
            if (footerLogLevelBtn == null) return;
            bool elevated = VpbDiagnosticLogLevel.IsElevated(CurrentDiagnosticLevel());
            bool collapsed = false;
            try { collapsed = _footerOverflowCollapsed != null && _footerOverflowCollapsed.Contains(footerLogLevelBtn); } catch { }
            bool show = elevated && !collapsed;
            if (footerLogLevelBtn.activeSelf != show)
                footerLogLevelBtn.SetActive(show);
            if (!elevated) return;

            if (footerLogLevelBtnText != null)
                footerLogLevelBtnText.text = FooterLogLevelChipLabel();
            if (footerLogLevelBtnImage != null)
            {
                bool full = string.Equals(CurrentDiagnosticLevel(), VpbDiagnosticLogLevel.Full, StringComparison.OrdinalIgnoreCase);
                footerLogLevelBtnImage.color = full ? UI.AccentRed : UI.AccentBlue;
            }
        }

        private void OpenDiagnosticsLogFolder()
        {
            string dir = DiagnosticsLogFolder();
            if (string.IsNullOrEmpty(dir))
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_folder_missing", "Log folder not available yet."), 2.5f);
                return;
            }
            try
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(dir);
                ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_folder_opened", "Opened the log folder on your desktop. Attach this session's file."), 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Open log folder failed: " + ex.Message);
                CopyDiagnosticsLogFilePath();
            }
        }

        private void CopyDiagnosticsLogFilePath()
        {
            string path = DiagnosticsLogFilePath();
            if (string.IsNullOrEmpty(path))
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_file_missing", "Log file not available yet."), 2.5f);
                return;
            }
            try
            {
                GUIUtility.systemCopyBuffer = path;
                ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_file_copied", "Copied this session's log path."), 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Copy log file path failed: " + ex.Message);
                ShowTemporaryStatus(path, 6f);
            }
        }

        private void CopyDiagnosticsLogContents()
        {
            string path = DiagnosticsLogFilePath();
            if (string.IsNullOrEmpty(path))
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_file_missing", "Log file not available yet."), 2.5f);
                return;
            }
            try { VPBLogger.Flush(); } catch { }
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_file_missing", "Log file not available yet."), 2.5f);
                    return;
                }
                if (info.Length > DiagLogCopyContentsMaxBytes)
                {
                    CopyDiagnosticsLogFilePath();
                    ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_text_too_large", "Log too large to copy; path copied instead."), 3.5f);
                    return;
                }
                string text = File.ReadAllText(path);
                GUIUtility.systemCopyBuffer = text ?? "";
                ShowTemporaryStatus(VPBTranslation.T("settings.diag_log_text_copied", "Copied log text. Paste it into the message."), 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Copy log text failed: " + ex.Message);
                CopyDiagnosticsLogFilePath();
            }
        }

        private static string DiagnosticsLogFolder()
        {
            try { return VPBLogger.SessionDirectory ?? ""; }
            catch { return ""; }
        }

        private static string DiagnosticsLogFilePath()
        {
            try { return VPBLogger.SessionFilePath ?? ""; }
            catch { return ""; }
        }
    }
}
