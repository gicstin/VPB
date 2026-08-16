using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Resolves a UI <see cref="Font"/> suitable for the current locale (CJK vs Latin).
    /// Prefers vpb_fonts/NotoSansSC-Regular.ttf next to the DLL when present.
    /// </summary>
    public static class VPBUiFont
    {
        /// <summary>Checked in order under vpb_fonts/ next to VPB.dll (Google Fonts often ships *-VariableFont_wght.ttf).</summary>
        private static readonly string[] BundledFontFileNames =
        {
            "NotoSansSC-VariableFont_wght.ttf",
            "NotoSansSC-Regular.ttf",
            "NotoSansTC-VariableFont_wght.ttf",
            "NotoSansTC-Regular.ttf",
        };

        private static Font _latinFont;
        private static Font _cachedDynamic;
        private static string _lastLocaleForDynamic;

        public static void InvalidateCache()
        {
            _cachedDynamic = null;
            _lastLocaleForDynamic = null;
        }

        /// <summary>Cache builtin Arial before any OS CJK font is created. Unity 2018 font manager can pollute later GetBuiltinResource lookups.</summary>
        private static Font EnsureLatinFont()
        {
            if (_latinFont != null) return _latinFont;
            try
            {
                _latinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch { }
            return _latinFont;
        }

        private static string GetPluginDir()
        {
            try
            {
                var asm = typeof(VPBUiFont).Assembly;
                string p = asm != null ? asm.Location : null;
                if (string.IsNullOrEmpty(p))
                {
                    try
                    {
                        string cb = asm != null ? asm.CodeBase : null;
                        if (!string.IsNullOrEmpty(cb)) p = new Uri(cb).LocalPath;
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(p)) return Path.GetDirectoryName(p);
            }
            catch { }
            return null;
        }

        private static bool LocaleNeedsCjkFallback(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return false;
            string l = locale.ToLowerInvariant();
            return l.StartsWith("zh", StringComparison.Ordinal)
                || l == "ja"
                || l == "ko"
                || l.StartsWith("ja_", StringComparison.Ordinal)
                || l.StartsWith("ko_", StringComparison.Ordinal);
        }

        /// <summary>Font for Unity UI Text: OS CJK font for CJK locales; else cached builtin Arial.</summary>
        public static Font GetUiFont()
        {
            VPBTranslation.EnsureInitialized();
            Font latin = EnsureLatinFont();
            string locale = VPBTranslation.CurrentLocale;

            // new Font(filePath) does NOT load a TTF from disk in Unity — it looks up an OS font by name.
            // Passing a file path creates a broken Font object with no glyphs (invisible text).
            // Bundled font file loading via new Font() is therefore skipped entirely.
            // For CJK locales, fall back to OS system fonts instead.

            if (LocaleNeedsCjkFallback(locale))
            {
                if (_cachedDynamic == null || !string.Equals(_lastLocaleForDynamic, locale, StringComparison.OrdinalIgnoreCase))
                {
                    _cachedDynamic = TryCreateOsCjkFont(locale);
                    _lastLocaleForDynamic = locale;
                }
                if (_cachedDynamic != null) return _cachedDynamic;
            }

            return latin;
        }

        private static Font TryCreateOsCjkFont(string locale)
        {
            try
            {
                string[] names;
                string l = locale.ToLowerInvariant();
                if (l.StartsWith("zh_tw", StringComparison.Ordinal) || l == "zh_hk")
                    names = new[] { "Microsoft JhengHei", "Microsoft YaHei", "SimHei", "MingLiU" };
                else if (l.StartsWith("zh", StringComparison.Ordinal))
                    names = new[] { "Microsoft YaHei", "Microsoft JhengHei", "SimHei", "SimSun" };
                else if (l == "ja" || l.StartsWith("ja", StringComparison.Ordinal))
                    names = new[] { "MS Gothic", "Yu Gothic", "Meiryo", "Microsoft YaHei" };
                else
                    names = new[] { "Malgun Gothic", "Microsoft YaHei", "Malgun Gothic Semilight" };

                foreach (string n in names)
                {
                    try
                    {
                        Font f = Font.CreateDynamicFontFromOSFont(n, 16);
                        if (f != null) return f;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public static void ApplyTo(Text text)
        {
            if (text == null) return;
            try
            {
                Font f = GetUiFont();
                if (f == null) return;

                // CreateDynamicFontFromOSFont can mutate size/style on assign. Snapshot, restore, rebuild.
                int fontSize = text.fontSize;
                FontStyle fontStyle = text.fontStyle;
                bool bestFit = text.resizeTextForBestFit;
                int minSize = text.resizeTextMinSize;
                int maxSize = text.resizeTextMaxSize;

                if (text.font != f)
                    text.font = f;

                if (text.fontSize != fontSize) text.fontSize = fontSize;
                if (text.fontStyle != fontStyle) text.fontStyle = fontStyle;
                text.resizeTextForBestFit = bestFit;
                text.resizeTextMinSize = minSize;
                text.resizeTextMaxSize = maxSize;
                text.SetAllDirty();
                text.FontTextureChanged();
            }
            catch { }
        }
    }
}
