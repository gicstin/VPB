using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal static class VpbShortcutText
    {
        private const string TokenKey = "{key:";
        private const string TokenHint = "{hint:";
        private const int MaxCacheEntries = 512;

        private static readonly Dictionary<string, string> s_GlobalPatterns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> s_Cache =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static int s_Revision;
        private static int s_CacheRevision = -1;

        internal static int Revision { get { return s_Revision; } }

        internal static void BumpRevision()
        {
            s_Revision++;
        }

        internal static void SetGlobalPattern(string id, string pattern)
        {
            if (string.IsNullOrEmpty(id)) return;
            string p = pattern ?? "";
            string existing;
            if (s_GlobalPatterns.TryGetValue(id, out existing) && string.Equals(existing, p, StringComparison.Ordinal))
                return;
            s_GlobalPatterns[id] = p;
            BumpRevision();
        }

        internal static string Resolve(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('{') < 0) return s;
            if (s.IndexOf(TokenKey, StringComparison.Ordinal) < 0
                && s.IndexOf(TokenHint, StringComparison.Ordinal) < 0)
                return s;

            if (s_CacheRevision != s_Revision)
            {
                s_Cache.Clear();
                s_CacheRevision = s_Revision;
            }

            string cached;
            if (s_Cache.TryGetValue(s, out cached)) return cached;

            string expanded;
            try { expanded = Expand(s); }
            catch { expanded = s; }

            if (s_Cache.Count >= MaxCacheEntries) s_Cache.Clear();
            s_Cache[s] = expanded;
            return expanded;
        }

        private static string Expand(string s)
        {
            var sb = new StringBuilder(s.Length + 24);
            int i = 0;
            while (i < s.Length)
            {
                int keyAt = s.IndexOf(TokenKey, i, StringComparison.Ordinal);
                int hintAt = s.IndexOf(TokenHint, i, StringComparison.Ordinal);

                int at;
                bool hint;
                if (keyAt < 0 && hintAt < 0) { sb.Append(s, i, s.Length - i); break; }
                if (keyAt < 0 || (hintAt >= 0 && hintAt < keyAt)) { at = hintAt; hint = true; }
                else { at = keyAt; hint = false; }

                int close = s.IndexOf('}', at);
                if (close < 0) { sb.Append(s, i, s.Length - i); break; }

                sb.Append(s, i, at - i);

                int idStart = at + (hint ? TokenHint.Length : TokenKey.Length);
                string ids = s.Substring(idStart, close - idStart);
                sb.Append(hint ? BuildHint(ids) : BuildKeys(ids, true));

                i = close + 1;
            }
            return sb.ToString();
        }

        private static string BuildKeys(string ids, bool markUnassigned)
        {
            string joined = JoinBoundPatterns(ids);
            if (!string.IsNullOrEmpty(joined)) return joined;
            return markUnassigned
                ? VPBTranslation.TRaw("shortcut.text.unassigned", "unassigned")
                : "";
        }

        private static string BuildHint(string ids)
        {
            string joined = JoinBoundPatterns(ids);
            if (string.IsNullOrEmpty(joined)) return "";
            string format = VPBTranslation.TRaw("shortcut.text.hint", " ({0})");
            try { return string.Format(format, joined); }
            catch { return " (" + joined + ")"; }
        }

        private static string JoinBoundPatterns(string ids)
        {
            if (string.IsNullOrEmpty(ids)) return "";
            string sep = VPBTranslation.TRaw("shortcut.text.sep", " / ");

            StringBuilder sb = null;
            int start = 0;
            while (start <= ids.Length)
            {
                int comma = ids.IndexOf(',', start);
                int end = comma < 0 ? ids.Length : comma;
                string id = ids.Substring(start, end - start).Trim();
                if (id.Length > 0)
                {
                    string p = PrettyPattern(LookupPattern(id));
                    if (!string.IsNullOrEmpty(p))
                    {
                        if (sb == null) sb = new StringBuilder(32);
                        else sb.Append(sep);
                        sb.Append(p);
                    }
                }
                if (comma < 0) break;
                start = comma + 1;
            }
            return sb == null ? "" : sb.ToString();
        }

        private static string LookupPattern(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("plugin.", StringComparison.OrdinalIgnoreCase))
            {
                string p;
                return s_GlobalPatterns.TryGetValue(id, out p) ? (p ?? "") : "";
            }
            return VpbShortcutMap.GetPatternByShortId(id);
        }

        internal static string PrettyPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            if (pattern.IndexOf('+') < 0) return PrettyToken(pattern);

            string[] parts = pattern.Split('+');
            int last = parts.Length - 1;
            if (last > 0 && parts[last].Length == 0) parts[last] = "+";

            var sb = new StringBuilder(pattern.Length + 8);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append(i == last ? PrettyToken(parts[i]) : parts[i]);
            }
            return sb.ToString();
        }

        private static string PrettyToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            switch (token)
            {
                case "Return": return VPBTranslation.TRaw("shortcut.keyname.return", "Enter");
                case "KeypadEnter": return VPBTranslation.TRaw("shortcut.keyname.keypad_enter", "Num Enter");
                case "KeypadPlus": return VPBTranslation.TRaw("shortcut.keyname.keypad_plus", "Num +");
                case "KeypadMinus": return VPBTranslation.TRaw("shortcut.keyname.keypad_minus", "Num -");
                case "Escape": return VPBTranslation.TRaw("shortcut.keyname.escape", "Esc");
                case "Delete": return VPBTranslation.TRaw("shortcut.keyname.delete", "Delete");
                case "Backspace": return VPBTranslation.TRaw("shortcut.keyname.backspace", "Backspace");
                case "Space": return VPBTranslation.TRaw("shortcut.keyname.space", "Space");
                case "UpArrow": return "↑";
                case "DownArrow": return "↓";
                case "LeftArrow": return "←";
                case "RightArrow": return "→";
                default: return token;
            }
        }
    }
}
