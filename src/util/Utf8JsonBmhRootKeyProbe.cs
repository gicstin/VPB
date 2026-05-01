using System;
using System.Text;
using SimpleJSON;

namespace VPB.Util
{
    /// <summary>
    /// Two-phase extraction: <see cref="BoyerMooreHorspool"/> locates quoted key bytes,
    /// then a UTF-8–aware scanner confirms a root-object property and slices the JSON value token.
    /// Optional <see cref="JSON.Parse"/> on the slice aligns display strings with SimpleJSON for benches.
    /// </summary>
    public static class Utf8JsonBmhRootKeyProbe
    {
        public static bool TryGetRootPropertyDisplayForBench(byte[] jsonUtf8, byte[] quotedKeyUtf8, int maxTrim,
            out string display)
        {
            return TryGetRootPropertyDisplayForBench(jsonUtf8, quotedKeyUtf8, maxTrim, out display, out _);
        }

        public static bool TryGetRootPropertyDisplayForBench(byte[] jsonUtf8, byte[] quotedKeyUtf8, int maxTrim,
            out string display, out string source)
        {
            display = null;
            source = "miss";
            if (jsonUtf8 == null || quotedKeyUtf8 == null || quotedKeyUtf8.Length == 0)
                return false;
            // Keep BMH in the path as fast precheck.
            if (BoyerMooreHorspool.IndexOf(jsonUtf8, quotedKeyUtf8) < 0)
                return false;
            if (!TryExtractValueText(jsonUtf8, quotedKeyUtf8, out string slice))
                return false;
            source = "probe-bmh";

            try
            {
                JSONNode node = JSON.Parse(slice);
                if (node == null) return false;
                if (node is JSONArray || node is JSONClass)
                    display = TrimBenchString(node.ToString(), maxTrim);
                else
                    display = TrimBenchString(node.Value, maxTrim);
                return true;
            }
            catch { return false; }
        }

        private static string TrimBenchString(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            string one = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (one.Length <= maxLen) return one;
            return one.Substring(0, Math.Max(0, maxLen - 3)) + "...";
        }

        private static bool TryExtractValueText(byte[] jsonUtf8, byte[] quotedKeyUtf8, out string valueText)
        {
            valueText = null;
            string json;
            string quotedKey;
            try
            {
                json = Encoding.UTF8.GetString(jsonUtf8);
                quotedKey = Encoding.UTF8.GetString(quotedKeyUtf8);
            }
            catch
            {
                return false;
            }

            int search = 0;
            while (true)
            {
                int idx = json.IndexOf(quotedKey, search, StringComparison.Ordinal);
                if (idx < 0) return false;
                if (IsInsideString(json, idx))
                {
                    search = idx + 1;
                    continue;
                }
                int prev = PrevNonWs(json, idx - 1);
                if (prev >= 0 && json[prev] != '{' && json[prev] != ',')
                {
                    search = idx + 1;
                    continue;
                }
                int p = SkipWs(json, idx + quotedKey.Length);
                if (p >= json.Length || json[p] != ':')
                {
                    search = idx + 1;
                    continue;
                }
                int valueStart = SkipWs(json, p + 1);
                if (valueStart >= json.Length) return false;
                int valueEnd = EndOfValue(json, valueStart);
                if (valueEnd <= valueStart)
                {
                    search = idx + 1;
                    continue;
                }
                valueText = json.Substring(valueStart, valueEnd - valueStart);
                return true;
            }
        }

        private static int SkipWs(string s, int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n') break;
                i++;
            }
            return i;
        }

        private static int PrevNonWs(string s, int i)
        {
            while (i >= 0)
            {
                char c = s[i];
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n') return i;
                i--;
            }
            return -1;
        }

        private static bool IsInsideString(string s, int index)
        {
            bool inString = false;
            bool escape = false;
            for (int i = 0; i < index; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
            }
            return inString;
        }

        private static int EndOfQuotedString(string s, int startQuote)
        {
            if (startQuote >= s.Length || s[startQuote] != '"') return -1;
            bool escape = false;
            for (int i = startQuote + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') return i + 1;
            }
            return -1;
        }

        private static int EndOfValue(string s, int start)
        {
            if (start >= s.Length) return -1;
            char c = s[start];
            if (c == '"') return EndOfQuotedString(s, start);
            if (c == '{' || c == '[') return EndBalancedContainer(s, start);
            if (start + 4 <= s.Length && string.CompareOrdinal(s, start, "true", 0, 4) == 0) return start + 4;
            if (start + 5 <= s.Length && string.CompareOrdinal(s, start, "false", 0, 5) == 0) return start + 5;
            if (start + 4 <= s.Length && string.CompareOrdinal(s, start, "null", 0, 4) == 0) return start + 4;
            int i = start;
            while (i < s.Length)
            {
                char k = s[i];
                if (k == ',' || k == '}' || k == ']') break;
                if (k == ' ' || k == '\t' || k == '\r' || k == '\n') break;
                i++;
            }
            return i;
        }

        private static int EndBalancedContainer(string s, int start)
        {
            if (start >= s.Length) return -1;
            char open = s[start];
            if (open != '{' && open != '[') return -1;
            int obj = open == '{' ? 1 : 0;
            int arr = open == '[' ? 1 : 0;
            bool inString = false;
            bool escape = false;
            for (int i = start + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == '"') { inString = false; }
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{') obj++;
                else if (c == '}')
                {
                    obj--;
                    if (obj == 0 && arr == 0) return i + 1;
                }
                else if (c == '[') arr++;
                else if (c == ']')
                {
                    arr--;
                    if (obj == 0 && arr == 0) return i + 1;
                }
            }
            return -1;
        }
    }
}
