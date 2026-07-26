using System;
using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
        /// <summary>Apply hotkeys edited in gallery Settings; keeps legacy UIKey synced to GalleryKey.</summary>
        public bool TryApplyGalleryPluginHotkeys(
            string galleryKey,
            string createGalleryKey,
            string hubKey,
            string clearConsoleKey,
            out string error)
        {
            error = null;
            try
            {
                if (KeyUtil.UsesDisallowedHotkeyKey(galleryKey) || KeyUtil.UsesDisallowedHotkeyKey(createGalleryKey)
                    || KeyUtil.UsesDisallowedHotkeyKey(hubKey) || KeyUtil.UsesDisallowedHotkeyKey(clearConsoleKey))
                {
                    error = VPBTranslation.T("hook.settings.error.disallowed_hotkey", "Mouse buttons cannot be used as hotkeys.");
                    return false;
                }

                var parsedGallery = KeyUtil.Parse(galleryKey ?? "");
                var parsedCreate = KeyUtil.Parse(createGalleryKey ?? "");
                var parsedHub = KeyUtil.Parse(hubKey ?? "");
                var parsedClear = KeyUtil.Parse(clearConsoleKey ?? "");

                if (KeyUtil.IsDisallowedHotkeyKey(parsedGallery.key) || KeyUtil.IsDisallowedHotkeyKey(parsedCreate.key)
                    || KeyUtil.IsDisallowedHotkeyKey(parsedHub.key) || KeyUtil.IsDisallowedHotkeyKey(parsedClear.key))
                {
                    error = VPBTranslation.T("hook.settings.error.disallowed_hotkey", "Mouse buttons cannot be used as hotkeys.");
                    return false;
                }

                if (parsedGallery.IsSame(parsedCreate) || parsedGallery.IsSame(parsedHub) || parsedGallery.IsSame(parsedClear)
                    || parsedCreate.IsSame(parsedHub) || parsedCreate.IsSame(parsedClear)
                    || parsedHub.IsSame(parsedClear))
                {
                    error = VPBTranslation.T("hook.settings.error.duplicate_hotkeys", "Duplicate hotkeys are not allowed.");
                    return false;
                }

                GalleryKey = parsedGallery;
                CreateGalleryKey = parsedCreate;
                HubKey = parsedHub;
                ClearConsoleKey = parsedClear;
                UIKey = parsedGallery;

                if (Settings.Instance != null)
                {
                    if (Settings.Instance.GalleryKey != null)
                        Settings.Instance.GalleryKey.Value = parsedGallery.keyPattern;
                    if (Settings.Instance.CreateGalleryKey != null)
                        Settings.Instance.CreateGalleryKey.Value = parsedCreate.keyPattern;
                    if (Settings.Instance.HubKey != null)
                        Settings.Instance.HubKey.Value = parsedHub.keyPattern;
                    if (Settings.Instance.ClearConsoleKey != null)
                        Settings.Instance.ClearConsoleKey.Value = parsedClear.keyPattern;
                    if (Settings.Instance.UIKey != null)
                        Settings.Instance.UIKey.Value = parsedGallery.keyPattern;
                }

                try { Config.Save(); } catch { }
                return true;
            }
            catch
            {
                error = VPBTranslation.T("hook.settings.error.invalid_hotkey", "Invalid setting. Example hotkey: Ctrl+Shift+V");
                return false;
            }
        }

        public void ApplyGalleryPluginHotkeysFromSettings()
        {
            try
            {
                if (Settings.Instance == null) return;
                bool repaired = false;
                repaired |= TryRepairPluginHotkeyConfigEntry(Settings.Instance.GalleryKey, "Ctrl+G");
                repaired |= TryRepairPluginHotkeyConfigEntry(Settings.Instance.CreateGalleryKey, "Ctrl+N");
                repaired |= TryRepairPluginHotkeyConfigEntry(Settings.Instance.HubKey, "Ctrl+H");
                repaired |= TryRepairPluginHotkeyConfigEntry(Settings.Instance.ClearConsoleKey, "F2");
                repaired |= TryRepairPluginHotkeyConfigEntry(Settings.Instance.UIKey, "Ctrl+G");
                if (repaired) try { Config.Save(); } catch { }

                GalleryKey = KeyUtil.Parse(Settings.Instance.GalleryKey?.Value ?? "Ctrl+G");
                CreateGalleryKey = KeyUtil.Parse(Settings.Instance.CreateGalleryKey?.Value ?? "Ctrl+N");
                HubKey = KeyUtil.Parse(Settings.Instance.HubKey?.Value ?? "Ctrl+H");
                ClearConsoleKey = KeyUtil.Parse(Settings.Instance.ClearConsoleKey?.Value ?? "F2");
                UIKey = KeyUtil.Parse(Settings.Instance.UIKey?.Value ?? Settings.Instance.GalleryKey?.Value ?? "Ctrl+G");
            }
            catch { }
        }

        private static bool TryRepairPluginHotkeyConfigEntry(BepInEx.Configuration.ConfigEntry<string> entry, string fallback)
        {
            if (entry == null) return false;
            string raw = entry.Value ?? "";
            string fixedValue = SanitizePluginHotkeyConfig(raw, fallback);
            if (string.Equals(raw, fixedValue, StringComparison.Ordinal)) return false;
            entry.Value = fixedValue;
            return true;
        }

        private static string SanitizePluginHotkeyConfig(string value, string fallback)
        {
            if (KeyUtil.UsesDisallowedHotkeyKey(value)) return fallback ?? "";
            return string.IsNullOrEmpty(value) ? (fallback ?? "") : value;
        }
    }
}
