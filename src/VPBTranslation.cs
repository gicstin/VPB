using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class VPBTranslation
    {
        public static event Action LocaleChanged;

        private static readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _currentLocale = "en";
        private static string _pluginDir;
        private static string _translationsDir;
        private static bool _initialized;

        public static string CurrentLocale => _currentLocale;

        public static string TranslationsDirectory
        {
            get
            {
                EnsurePluginDir();
                EnsureTranslationsDir();
                return _translationsDir;
            }
        }

        private static void EnsureTranslationsDir()
        {
            if (_translationsDir != null) return;
            try
            {
                string dir = VpbPaths.FindDir("assets/translations", "vpb_translations");
                _translationsDir = Directory.Exists(dir) ? dir : "";
            }
            catch { _translationsDir = ""; }
        }

        public static string GetLocaleDisplayName(string localeId)
        {
            if (string.IsNullOrEmpty(localeId)) return "English";
            switch (localeId.ToLowerInvariant())
            {
                case "en": return "English";
                case "zh_cn": return "简体中文";
                case "zh_tw": return "繁體中文";
                case "ja": return "日本語";
                case "ko": return "한국어";
                default: return localeId;
            }
        }

        public static void InitializeFromConfig()
        {
            string id = "en";
            try
            {
                if (VPBConfig.Instance != null)
                {
                    string configured = VPBConfig.Instance.UiLocale;
                    if (!string.IsNullOrEmpty(configured))
                    {
                        id = NormalizeLocaleId(configured);
                    }
                    else
                    {
                        string detected = DetectSystemLocale();
                        if (detected != "en")
                        {
                            VPBConfig.Instance.UiLocale = detected;
                            VPBConfig.Instance.Save();
                        }
                        else
                        {
                            // Mark as explicitly configured to "en" so we don't re-detect next time
                            VPBConfig.Instance.UiLocale = "en";
                            VPBConfig.Instance.Save();
                        }
                        id = detected;
                    }
                }
            }
            catch { }
            SetLocale(id, saveConfig: false);
            _initialized = true;
        }

        private static string DetectSystemLocale()
        {
            try
            {
                string cultureName = CultureInfo.CurrentUICulture.Name;
                string candidate = MapCultureNameToLocaleId(cultureName);
                if (candidate == "en") return "en";

                // Only use the detected locale if the translation file is actually present
                string dir = TranslationsDirectory;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string filePath = Path.Combine(dir, candidate + ".json");
                    if (File.Exists(filePath)) return candidate;
                }
            }
            catch { }
            return "en";
        }

        private static string MapCultureNameToLocaleId(string cultureName)
        {
            if (string.IsNullOrEmpty(cultureName)) return "en";
            string lower = cultureName.ToLowerInvariant();
            if (lower == "zh-cn" || lower == "zh-hans" || lower.StartsWith("zh-hans-", StringComparison.Ordinal)) return "zh_cn";
            if (lower == "zh-tw" || lower == "zh-hk" || lower == "zh-mo" ||
                lower == "zh-hant" || lower.StartsWith("zh-hant-", StringComparison.Ordinal)) return "zh_tw";
            if (lower == "ja" || lower.StartsWith("ja-", StringComparison.Ordinal)) return "ja";
            if (lower == "ko" || lower.StartsWith("ko-", StringComparison.Ordinal)) return "ko";
            return "en";
        }

        public static void EnsureInitialized()
        {
            if (!_initialized) InitializeFromConfig();
        }

        private static string NormalizeLocaleId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "en";
            return id.Trim().Replace(' ', '_').ToLowerInvariant();
        }

        private static void EnsurePluginDir()
        {
            if (_pluginDir != null) return;
            _pluginDir = "";
            try
            {
                var asm = typeof(VPBTranslation).Assembly;
                string asmPath = null;
                try { asmPath = asm != null ? asm.Location : null; } catch { }
                if (string.IsNullOrEmpty(asmPath))
                {
                    try
                    {
                        string codeBase = asm != null ? asm.CodeBase : null;
                        if (!string.IsNullOrEmpty(codeBase))
                            asmPath = new Uri(codeBase).LocalPath;
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(asmPath))
                    _pluginDir = Path.GetDirectoryName(asmPath);
            }
            catch { }
        }

        public static List<string> GetAvailableLocaleIds()
        {
            var list = new List<string> { "en" };
            try
            {
                string dir = TranslationsDirectory;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;

                foreach (string path in Directory.GetFiles(dir, "*.json"))
                {
                    string stem = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(stem)) continue;
                    string id = NormalizeLocaleId(stem);
                    if (id == "en") continue;
                    if (!list.Contains(id)) list.Add(id);
                }
            }
            catch { }

            return list;
        }

        public static void SetLocale(string localeId, bool saveConfig = true)
        {
            string id = NormalizeLocaleId(localeId);
            if (id == "en" || string.IsNullOrEmpty(id))
            {
                _currentLocale = "en";
                _map.Clear();
            }
            else
            {
                _currentLocale = id;
                LoadLocaleFile(id);
            }

            try
            {
                if (saveConfig && VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.UiLocale = _currentLocale;
                    VPBConfig.Instance.Save();
                }
            }
            catch { }

            try { VPBUiFont.InvalidateCache(); } catch { }

            try { LocaleChanged?.Invoke(); } catch { }
        }

        private static void LoadLocaleFile(string localeId)
        {
            _map.Clear();
            try
            {
                string dir = TranslationsDirectory;
                if (string.IsNullOrEmpty(dir)) return;
                string path = Path.Combine(dir, localeId + ".json");
                if (!File.Exists(path)) return;

                string json = File.ReadAllText(path, Encoding.UTF8);
                JSONNode root = JSON.Parse(json);
                var obj = root as JSONClass;
                if (obj == null) return;

                foreach (string key in obj.Keys)
                {
                    JSONNode n = obj[key];
                    if (n == null) continue;
                    string v = n.Value;
                    if (v != null) _map[key] = v;
                }
            }
            catch { }
        }

        public static string T(string key, string englishDefault)
        {
            return VpbShortcutText.Resolve(TRaw(key, englishDefault));
        }

        public static string TRaw(string key, string englishDefault)
        {
            if (string.IsNullOrEmpty(key)) return englishDefault ?? "";
            if (_map.Count > 0 && _map.TryGetValue(key, out string s) && !string.IsNullOrEmpty(s))
                return s;
            return englishDefault ?? "";
        }

        public static void Reload()
        {
            if (_currentLocale == "en" || string.IsNullOrEmpty(_currentLocale))
            {
                _map.Clear();
            }
            else
            {
                LoadLocaleFile(_currentLocale);
            }
            try { LocaleChanged?.Invoke(); } catch { }
        }
    }
}
