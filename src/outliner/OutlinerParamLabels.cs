using System;
using System.Collections.Generic;
using System.Text;

namespace VPB.Outliner
{
    internal static class OutlinerParamLabels
    {
        const int MaxCacheEntries = 512;

        static readonly Dictionary<string, string> Cache =
            new Dictionary<string, string>(256, StringComparer.Ordinal);

        static readonly StringBuilder Builder = new StringBuilder(64);

        static readonly string[] Acronyms =
        {
            "url", "URL",
            "uid", "UID",
            "ui", "UI",
            "id", "ID",
            "fov", "FOV",
            "hud", "HUD",
            "dll", "DLL",
            "hsv", "HSV",
            "rgb", "RGB",
            "cua", "CUA",
            "vr", "VR",
            "fps", "FPS"
        };

        internal static string Humanize(string paramId)
        {
            if (string.IsNullOrEmpty(paramId)) return "";
            string cached;
            if (Cache.TryGetValue(paramId, out cached)) return cached;
            string built = Build(paramId);
            if (Cache.Count >= MaxCacheEntries) Cache.Clear();
            Cache[paramId] = built;
            return built;
        }

        static string Build(string paramId)
        {
            Builder.Length = 0;
            int wordStart = 0;
            int len = paramId.Length;
            bool first = true;
            for (int i = 1; i <= len; i++)
            {
                if (i < len && !IsBoundary(paramId, i)) continue;
                AppendWord(paramId, wordStart, i - wordStart, first);
                first = false;
                wordStart = i;
                while (wordStart < len && paramId[wordStart] == '_') wordStart++;
                i = wordStart;
            }
            if (Builder.Length == 0) return paramId;
            return Builder.ToString();
        }

        static bool IsBoundary(string value, int i)
        {
            char c = value[i];
            if (c == '_') return true;
            char prev = value[i - 1];
            if (char.IsDigit(c) && !char.IsDigit(prev)) return true;
            if (!char.IsUpper(c)) return false;
            if (!char.IsUpper(prev)) return true;
            if (i + 1 < value.Length && char.IsLower(value[i + 1])) return true;
            return false;
        }

        static void AppendWord(string source, int start, int count, bool first)
        {
            if (count <= 0) return;
            if (Builder.Length > 0) Builder.Append(' ');
            string acronym = MatchAcronym(source, start, count);
            if (acronym != null)
            {
                Builder.Append(acronym);
                return;
            }
            if (IsAllUpper(source, start, count))
            {
                Builder.Append(source, start, count);
                return;
            }
            char head = source[start];
            Builder.Append(first ? char.ToUpperInvariant(head) : char.ToLowerInvariant(head));
            for (int i = 1; i < count; i++)
                Builder.Append(char.ToLowerInvariant(source[start + i]));
        }

        static string MatchAcronym(string source, int start, int count)
        {
            for (int a = 0; a < Acronyms.Length; a += 2)
            {
                string key = Acronyms[a];
                if (key.Length != count) continue;
                bool same = true;
                for (int i = 0; i < count; i++)
                {
                    if (char.ToLowerInvariant(source[start + i]) == key[i]) continue;
                    same = false;
                    break;
                }
                if (same) return Acronyms[a + 1];
            }
            return null;
        }

        static bool IsAllUpper(string source, int start, int count)
        {
            if (count < 2) return false;
            for (int i = 0; i < count; i++)
            {
                if (!char.IsUpper(source[start + i])) return false;
            }
            return true;
        }
    }
}
