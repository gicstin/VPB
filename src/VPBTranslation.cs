using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Loads flat JSON key/value files from vpb_translations next to VPB.dll. UTF-8 only.
    /// English defaults live in code as the second argument to <see cref="T"/>.
    /// </summary>
    public static class VPBTranslation
    {
        public static event Action LocaleChanged;

        private static readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _currentLocale = "en";
        private static string _pluginDir;
        private static bool _initialized;

        public static string CurrentLocale => _currentLocale;

        public static string TranslationsDirectory
        {
            get
            {
                EnsurePluginDir();
                return string.IsNullOrEmpty(_pluginDir)
                    ? null
                    : Path.Combine(_pluginDir, "vpb_translations");
            }
        }

        /// <summary>Localized display name for the language switcher; falls back to locale id.</summary>
        public static string GetLocaleDisplayName(string localeId)
        {
            if (string.IsNullOrEmpty(localeId)) return "en";
            switch (localeId.ToLowerInvariant())
            {
                case "en": return T("i18n.locale.en", "English");
                case "zh_cn": return T("i18n.locale.zh_cn", "简体中文");
                case "zh_tw": return T("i18n.locale.zh_tw", "繁體中文");
                case "ja": return T("i18n.locale.ja", "日本語");
                case "ko": return T("i18n.locale.ko", "한국어");
                default: return localeId;
            }
        }

        public static void InitializeFromConfig()
        {
            string id = "en";
            try
            {
                if (VPBConfig.Instance != null && !string.IsNullOrEmpty(VPBConfig.Instance.UiLocale))
                    id = NormalizeLocaleId(VPBConfig.Instance.UiLocale);
            }
            catch { }
            SetLocale(id, saveConfig: false);
            _initialized = true;
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

        /// <summary>Locale ids to show in the switcher: always includes en; plus each *.json stem in vpb_translations.</summary>
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
