using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VPB
{
    /// <summary>Minimal YAML read/write for user-tag export (two files: tag→items, item→tags). No external deps (.NET 3.5).</summary>
    internal static class GalleryUserTagYamlBrain
    {
        internal const char ItemKeySep = '\t';
        internal const string TagFirstRoot = "tags";
        internal const string ItemFirstRoot = "items";

        private static bool IsNullOrWhiteSpace(string s)
        {
            if (s == null || s.Length == 0) return true;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsWhiteSpace(s[i])) return false;
            }
            return true;
        }

        internal static string EncodeItemKey(string category, string pkgUid, string internalPath)
        {
            return (category ?? "") + ItemKeySep + (pkgUid ?? "") + ItemKeySep + (internalPath ?? "");
        }

        internal static bool TryDecodeItemKey(string raw, out string category, out string pkgUid, out string internalPath)
        {
            category = pkgUid = internalPath = "";
            if (string.IsNullOrEmpty(raw)) return false;
            string s = raw.Trim();
            int i1 = s.IndexOf(ItemKeySep);
            if (i1 < 0) return false;
            int i2 = s.IndexOf(ItemKeySep, i1 + 1);
            if (i2 < 0) return false;
            int i3 = s.IndexOf(ItemKeySep, i2 + 1);
            if (i3 >= 0) return false;
            category = s.Substring(0, i1).Trim();
            pkgUid = s.Substring(i1 + 1, i2 - i1 - 1).Trim();
            internalPath = s.Substring(i2 + 1).Trim();
            return category.Length > 0 && pkgUid.Length > 0 && internalPath.Length > 0;
        }

        internal static string BuildTagToItemsYaml(IDictionary<string, List<string>> tagToItems)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("# VPB user tags — tag → items. Item line = category<TAB>pkg_uid<TAB>internal_path");
            sb.AppendLine("vpb_user_tags_format: tag_to_items");
            sb.AppendLine("version: 1");
            sb.AppendLine(TagFirstRoot + ":");
            if (tagToItems == null || tagToItems.Count == 0) return sb.ToString();

            var tagNames = new List<string>(tagToItems.Keys);
            tagNames.Sort(StringComparer.OrdinalIgnoreCase);
            for (int ti = 0; ti < tagNames.Count; ti++)
            {
                string tag = tagNames[ti];
                if (string.IsNullOrEmpty(tag)) continue;
                sb.Append("  ");
                sb.Append(YamlQuoteKeyIfNeeded(tag));
                sb.AppendLine(":");
                List<string> items = tagToItems[tag];
                if (items == null) continue;
                var sorted = new List<string>(items);
                sorted.Sort(StringComparer.Ordinal);
                for (int ii = 0; ii < sorted.Count; ii++)
                {
                    string line = sorted[ii];
                    if (string.IsNullOrEmpty(line)) continue;
                    sb.Append("    - ");
                    sb.AppendLine(YamlDoubleQuotedScalar(line));
                }
            }
            return sb.ToString();
        }

        internal static string BuildItemToTagsYaml(IDictionary<string, List<string>> itemToTags)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("# VPB user tags — item → tags. Item key = category<TAB>pkg_uid<TAB>internal_path");
            sb.AppendLine("vpb_user_tags_format: item_to_tags");
            sb.AppendLine("version: 1");
            sb.AppendLine(ItemFirstRoot + ":");
            if (itemToTags == null || itemToTags.Count == 0) return sb.ToString();

            var keys = new List<string>(itemToTags.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int ki = 0; ki < keys.Count; ki++)
            {
                string itemKey = keys[ki];
                if (string.IsNullOrEmpty(itemKey)) continue;
                sb.Append("  ");
                sb.Append(YamlDoubleQuotedScalar(itemKey));
                sb.AppendLine(":");
                List<string> tags = itemToTags[itemKey];
                if (tags == null) continue;
                var sorted = new List<string>(tags);
                sorted.Sort(StringComparer.OrdinalIgnoreCase);
                for (int ti = 0; ti < sorted.Count; ti++)
                {
                    string t = sorted[ti];
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append("    - ");
                    sb.AppendLine(YamlQuoteKeyIfNeeded(t));
                }
            }
            return sb.ToString();
        }

        /// <summary>Returns normalized tag → item-keys and item-key → tags from one YAML file. Sniffs tag-first vs item-first.</summary>
        internal static bool TryParseImport(
            string yamlText,
            out Dictionary<string, List<string>> tagToItemKeys,
            out Dictionary<string, List<string>> itemKeyToTags,
            out string errorOut)
        {
            tagToItemKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            itemKeyToTags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            errorOut = null;
            if (string.IsNullOrEmpty(yamlText))
            {
                errorOut = "empty";
                return false;
            }
            string text = yamlText;
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            bool tagFirst;
            if (!SniffFormat(text, out tagFirst))
            {
                errorOut = "unrecognized layout";
                return false;
            }

            string body = StripHeaderMetadata(text);
            if (tagFirst)
            {
                if (!TryParseTagFirstBody(body, tagToItemKeys, out errorOut))
                    return false;
            }
            else
            {
                if (!TryParseItemFirstBody(body, itemKeyToTags, out errorOut))
                    return false;
            }
            return true;
        }

        private static bool SniffFormat(string text, out bool tagFirst)
        {
            tagFirst = true;
            if (text.IndexOf("vpb_user_tags_format: tag_to_items", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagFirst = true;
                return true;
            }
            if (text.IndexOf("vpb_user_tags_format: item_to_tags", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagFirst = false;
                return true;
            }
            if (text.IndexOf("\n" + TagFirstRoot + ":", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.TrimStart().StartsWith(TagFirstRoot + ":", StringComparison.OrdinalIgnoreCase))
            {
                tagFirst = true;
                return true;
            }
            if (text.IndexOf("\n" + ItemFirstRoot + ":", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.TrimStart().StartsWith(ItemFirstRoot + ":", StringComparison.OrdinalIgnoreCase))
            {
                tagFirst = false;
                return true;
            }
            return HeuristicFlatSniff(text, out tagFirst);
        }

        private static bool HeuristicFlatSniff(string text, out bool tagFirst)
        {
            tagFirst = true;
            var lines = SplitLines(text);
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].TrimEnd('\r');
                if (IsSkippableYamlLine(ln)) continue;
                if (!TryParseIndentKeyLine(ln, 0, out string key, out bool onlyKey))
                {
                    continue;
                }
                if (!onlyKey) continue;
                string next = NextNonEmptyLine(lines, i + 1);
                if (string.IsNullOrEmpty(next) || !IsListItemLine(next, out string listVal))
                    continue;
                bool keyIsItem = TryDecodeItemKey(UnquoteYamlKey(key), out _, out _, out _);
                bool valIsItem = TryDecodeItemKey(ParseListScalar(next), out _, out _, out _);
                if (keyIsItem && !valIsItem)
                {
                    tagFirst = false;
                    return true;
                }
                if (!keyIsItem && valIsItem)
                {
                    tagFirst = true;
                    return true;
                }
                if (keyIsItem)
                {
                    tagFirst = false;
                    return true;
                }
                if (valIsItem)
                {
                    tagFirst = true;
                    return true;
                }
                return false;
            }
            return false;
        }

        private static string NextNonEmptyLine(string[] lines, int start)
        {
            for (int i = start; i < lines.Length; i++)
            {
                string ln = lines[i].TrimEnd('\r');
                if (IsSkippableYamlLine(ln)) continue;
                return ln;
            }
            return null;
        }

        private static string StripHeaderMetadata(string text)
        {
            var lines = SplitLines(text);
            var sb = new StringBuilder(text.Length);
            bool seenData = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].TrimEnd('\r');
                if (IsSkippableYamlLine(ln)) continue;
                string t = ln.TrimStart();
                if (!seenData && t.StartsWith("vpb_", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seenData && t.StartsWith("version:", StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Equals(TagFirstRoot + ":", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith(TagFirstRoot + ":", StringComparison.OrdinalIgnoreCase))
                {
                    seenData = true;
                    continue;
                }
                if (t.Equals(ItemFirstRoot + ":", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith(ItemFirstRoot + ":", StringComparison.OrdinalIgnoreCase))
                {
                    seenData = true;
                    continue;
                }
                seenData = true;
                sb.AppendLine(ln);
            }
            return sb.ToString();
        }

        private static bool TryParseTagFirstBody(string body, Dictionary<string, List<string>> tagToItemKeys, out string err)
        {
            err = null;
            if (IsNullOrWhiteSpace(body)) return true;
            var lines = SplitLines(body);
            string curTag = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].TrimEnd('\r');
                if (IsSkippableYamlLine(ln)) continue;
                bool tagKeyLine =
                    (TryParseIndentKeyLine(ln, 0, out string tagKey, out bool tagOnly) && tagOnly)
                    || (TryParseIndentKeyLine(ln, 2, out tagKey, out tagOnly) && tagOnly);
                if (tagKeyLine)
                {
                    curTag = UnquoteYamlKey(tagKey);
                    string nt = VpbLocalDatabase.NormalizeGalleryUserTagName(curTag);
                    if (string.IsNullOrEmpty(nt))
                    {
                        err = "bad tag name: " + curTag;
                        return false;
                    }
                    curTag = nt;
                    if (!tagToItemKeys.ContainsKey(curTag))
                        tagToItemKeys[curTag] = new List<string>();
                    continue;
                }
                if (curTag != null && IsListItemLine(ln, out _))
                {
                    string rawScalar = ParseListScalar(ln);
                    if (string.IsNullOrEmpty(rawScalar)) continue;
                    if (!TryDecodeItemKey(rawScalar, out _, out _, out _))
                    {
                        err = "item must be category<TAB>pkg_uid<TAB>path: " + rawScalar;
                        return false;
                    }
                    tagToItemKeys[curTag].Add(rawScalar);
                    continue;
                }
            }
            return true;
        }

        private static bool TryParseItemFirstBody(string body, Dictionary<string, List<string>> itemKeyToTags, out string err)
        {
            err = null;
            if (IsNullOrWhiteSpace(body)) return true;
            var lines = SplitLines(body);
            string curItem = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].TrimEnd('\r');
                if (IsSkippableYamlLine(ln)) continue;
                bool itemKeyLine =
                    (TryParseIndentKeyLine(ln, 0, out string itemKeyRaw, out bool keyOnly) && keyOnly)
                    || (TryParseIndentKeyLine(ln, 2, out itemKeyRaw, out keyOnly) && keyOnly);
                if (itemKeyLine)
                {
                    string ik = UnquoteYamlKey(itemKeyRaw);
                    if (!TryDecodeItemKey(ik, out _, out _, out _))
                    {
                        err = "item key must be category<TAB>pkg_uid<TAB>path";
                        return false;
                    }
                    curItem = ik;
                    if (!itemKeyToTags.ContainsKey(curItem))
                        itemKeyToTags[curItem] = new List<string>();
                    continue;
                }
                if (curItem != null && IsListItemLine(ln, out _))
                {
                    string tagRaw = ParseListScalar(ln).Trim();
                    if (string.IsNullOrEmpty(tagRaw)) continue;
                    string nt = VpbLocalDatabase.NormalizeGalleryUserTagName(tagRaw);
                    if (string.IsNullOrEmpty(nt))
                    {
                        err = "bad tag: " + tagRaw;
                        return false;
                    }
                    itemKeyToTags[curItem].Add(nt);
                    continue;
                }
            }
            return true;
        }

        private static string[] SplitLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        }

        private static bool IsSkippableYamlLine(string ln)
        {
            if (IsNullOrWhiteSpace(ln)) return true;
            string t = ln.TrimStart();
            return t.Length > 0 && t[0] == '#';
        }

        private static int CountLeadingSpaces(string ln)
        {
            int n = 0;
            for (int i = 0; i < ln.Length; i++)
            {
                if (ln[i] == ' ') n++;
                else break;
            }
            return n;
        }

        /// <summary>At column 0: "Key:" or "key: rest" — onlyKey true when nothing after colon.</summary>
        private static bool TryParseIndentKeyLine(string ln, int expectIndent, out string keyPart, out bool onlyKey)
        {
            keyPart = null;
            onlyKey = false;
            if (CountLeadingSpaces(ln) != expectIndent) return false;
            string s = ln.Substring(expectIndent).TrimEnd();
            int colon = s.IndexOf(':');
            if (colon < 0) return false;
            string after = colon + 1 < s.Length ? s.Substring(colon + 1).Trim() : "";
            keyPart = s.Substring(0, colon).Trim();
            onlyKey = after.Length == 0;
            return keyPart.Length > 0;
        }

        private static bool IsListItemLine(string ln, out string payload)
        {
            payload = null;
            string t = ln.TrimStart();
            if (!t.StartsWith("-")) return false;
            payload = t.Substring(1).Trim();
            return true;
        }

        private static string ParseListScalar(string ln)
        {
            if (!IsListItemLine(ln, out string payload)) return "";
            return ParseYamlScalar(payload);
        }

        private static string ParseYamlScalar(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return "";
            if (s[0] == '"' || s[0] == '\'')
                return UnquoteYamlString(s);
            return s;
        }

        private static string UnquoteYamlKey(string key)
        {
            key = key.Trim();
            return ParseYamlScalar(key);
        }

        private static string UnquoteYamlString(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return UnescapeYamlDoubleQuoted(s.Substring(1, s.Length - 2));
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                return s.Substring(1, s.Length - 2).Replace("''", "'");
            return s;
        }

        private static string UnescapeYamlDoubleQuoted(string inner)
        {
            var sb = new StringBuilder(inner.Length);
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '\\' && i + 1 < inner.Length)
                {
                    char c = inner[i + 1];
                    switch (c)
                    {
                        case 'n': sb.Append('\n'); i++; continue;
                        case 'r': sb.Append('\r'); i++; continue;
                        case 't': sb.Append('\t'); i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                        case '"': sb.Append('"'); i++; continue;
                        default: sb.Append(c); i++; continue;
                    }
                }
                sb.Append(inner[i]);
            }
            return sb.ToString();
        }

        private static string YamlDoubleQuotedScalar(string raw)
        {
            var sb = new StringBuilder(raw.Length + 8);
            sb.Append('"');
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string YamlQuoteKeyIfNeeded(string key)
        {
            if (string.IsNullOrEmpty(key)) return YamlDoubleQuotedScalar("");
            bool safe = true;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c <= ' ' || c == ':' || c == '#' || c == '"' || c == '\'' || c == '\\' || c == '[' || c == ']' || c == '{' || c == '}')
                {
                    safe = false;
                    break;
                }
            }
            if (safe) return key;
            return YamlDoubleQuotedScalar(key);
        }
    }
}
