using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VPB
{
    /// <summary>Loads locale help markdown from vpb_help/{locale}.md next to VPB.dll (e.g. en.md, ko.md).</summary>
    public static class VPBHelpContent
    {
        public enum HelpInlineKind
        {
            Text,
            IconRef
        }

        public sealed class HelpInlineSegment
        {
            public HelpInlineKind Kind;
            public string Text;
            public string IconId;
            public string IconLabel;
        }

        public sealed class HelpContentLine
        {
            public bool IsBlank;
            public bool IsSubheading;
            public bool IsBullet;
            public bool IsNumbered;
            public string NumberPrefix;
            public List<HelpInlineSegment> Segments = new List<HelpInlineSegment>(4);
        }

        public sealed class HelpSection
        {
            public string Id;
            public string Title;
            public string RichTextBody;
            public List<HelpContentLine> ContentLines = new List<HelpContentLine>(16);
        }

        public struct HelpIconSpec
        {
            public string Path;
            public Color Backdrop;
        }

        private static readonly Regex s_IconRefRegex = new Regex(@"\{\{icon:([a-z0-9_]+)\|(.+?)\}\}", RegexOptions.Compiled);

        private static readonly Dictionary<string, HelpIconSpec> s_IconMap = new Dictionary<string, HelpIconSpec>(StringComparer.OrdinalIgnoreCase)
        {
            { "tags", new HelpIconSpec { Path = "vpb_icons/tags.png", Backdrop = new Color(0.14f, 0.42f, 0.48f, 1f) } },
            { "category", new HelpIconSpec { Path = "vpb_icons/gallery_category.png", Backdrop = new Color(0.60f, 0.15f, 0.15f, 1f) } },
            { "path", new HelpIconSpec { Path = "vpb_icons/folder.png", Backdrop = new Color(0.15f, 0.15f, 0.45f, 1f) } },
            { "creator", new HelpIconSpec { Path = "vpb_icons/gallery_creator.png", Backdrop = new Color(0.60f, 0.45f, 0.15f, 1f) } },
            { "history", new HelpIconSpec { Path = "vpb_icons/history.png", Backdrop = new Color(0.50f, 0.20f, 0.50f, 1f) } },
            { "import", new HelpIconSpec { Path = "vpb_icons/import.png", Backdrop = new Color(0.10f, 0.26f, 0.44f, 1f) } },
            { "cache_texture", new HelpIconSpec { Path = "vpb_icons/cache_texture.png", Backdrop = new Color(0.22f, 0.38f, 0.28f, 1f) } },
            { "compress", new HelpIconSpec { Path = "vpb_icons/compress.png", Backdrop = new Color(0.22f, 0.38f, 0.28f, 1f) } },
            { "search", new HelpIconSpec { Path = "vpb_icons/search.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "filter", new HelpIconSpec { Path = "vpb_icons/filter_on.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "star", new HelpIconSpec { Path = "vpb_icons/star.png", Backdrop = new Color(0.45f, 0.38f, 0.12f, 1f) } },
            { "settings", new HelpIconSpec { Path = "vpb_icons/settings.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "refresh", new HelpIconSpec { Path = "vpb_icons/refresh.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "hold", new HelpIconSpec { Path = "vpb_icons/hold.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "layout_grid", new HelpIconSpec { Path = "vpb_icons/layout_grid.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "cleanup", new HelpIconSpec { Path = "vpb_icons/cleanup.png", Backdrop = new Color(0.35f, 0.28f, 0.18f, 1f) } },
            { "help", new HelpIconSpec { Path = "vpb_icons/info_square.png", Backdrop = new Color(0.12f, 0.28f, 0.42f, 1f) } },
            { "delete", new HelpIconSpec { Path = "vpb_icons/delete.png", Backdrop = new Color(0.45f, 0.18f, 0.18f, 1f) } },
            { "load", new HelpIconSpec { Path = "vpb_icons/load.png", Backdrop = new Color(0.18f, 0.32f, 0.22f, 1f) } },
            { "save", new HelpIconSpec { Path = "vpb_icons/gallery_save.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "target", new HelpIconSpec { Path = "vpb_icons/gallery_target.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "layout_list", new HelpIconSpec { Path = "vpb_icons/layout_list.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "undo", new HelpIconSpec { Path = "vpb_icons/undo.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "select_all", new HelpIconSpec { Path = "vpb_icons/select_all.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "pin", new HelpIconSpec { Path = "vpb_icons/pin_on.png", Backdrop = new Color(0.20f, 0.24f, 0.30f, 1f) } },
            { "hub", new HelpIconSpec { Path = "vpb_icons/hub.png", Backdrop = new Color(0.28f, 0.22f, 0.38f, 1f) } },
        };

        private static string _helpDir;

        public static string HelpDirectory
        {
            get
            {
                EnsureHelpDir();
                return _helpDir ?? "";
            }
        }

        public static bool TryResolveHelpIcon(string iconId, out HelpIconSpec spec)
        {
            spec = default(HelpIconSpec);
            if (string.IsNullOrEmpty(iconId)) return false;
            return s_IconMap.TryGetValue(iconId.Trim(), out spec);
        }

        private static void EnsureHelpDir()
        {
            if (_helpDir != null) return;
            _helpDir = "";
            try
            {
                string pluginDir = ResolvePluginDir();
                if (!string.IsNullOrEmpty(pluginDir))
                {
                    string local = Path.Combine(pluginDir, "vpb_help");
                    if (Directory.Exists(local)) { _helpDir = local; return; }
                }
                if (!string.IsNullOrEmpty(pluginDir))
                {
                    string d = pluginDir;
                    for (int i = 0; i < 8 && !string.IsNullOrEmpty(d); i++)
                    {
                        string direct = Path.Combine(d, "vpb_help");
                        if (Directory.Exists(direct)) { _helpDir = direct; return; }
                        string bep = Path.Combine(Path.Combine(Path.Combine(d, "BepInEx"), "plugins"), "vpb_help");
                        if (Directory.Exists(bep)) { _helpDir = bep; return; }
                        try
                        {
                            string parent = Path.GetDirectoryName(d);
                            if (string.IsNullOrEmpty(parent) || string.Equals(parent, d, StringComparison.OrdinalIgnoreCase))
                                break;
                            d = parent;
                        }
                        catch { break; }
                    }
                }
                try
                {
                    string cwd = Directory.GetCurrentDirectory();
                    if (!string.IsNullOrEmpty(cwd))
                    {
                        string bep = Path.Combine(Path.Combine(Path.Combine(cwd, "BepInEx"), "plugins"), "vpb_help");
                        if (Directory.Exists(bep)) { _helpDir = bep; return; }
                    }
                }
                catch { }
            }
            catch { }
        }

        private static string ResolvePluginDir()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc))
                    return Path.GetDirectoryName(loc);
            }
            catch { }
            return null;
        }

        public static List<HelpSection> LoadSections(string localeId)
        {
            string md = LoadMarkdown(localeId);
            return ParseSections(md);
        }

        public static string LoadMarkdown(string localeId)
        {
            EnsureHelpDir();
            string id = NormalizeLocale(localeId);
            if (!string.IsNullOrEmpty(_helpDir))
            {
                string path = Path.Combine(_helpDir, id + ".md");
                if (File.Exists(path))
                {
                    try { return File.ReadAllText(path, Encoding.UTF8); }
                    catch { }
                }
            }
            if (id != "en" && !string.IsNullOrEmpty(_helpDir))
            {
                string enPath = Path.Combine(_helpDir, "en.md");
                if (File.Exists(enPath))
                {
                    try { return File.ReadAllText(enPath, Encoding.UTF8); }
                    catch { }
                }
            }
            return MinimalBuiltInEnglishMarkdown();
        }

        private static string NormalizeLocale(string localeId)
        {
            if (string.IsNullOrEmpty(localeId)) return "en";
            return localeId.ToLowerInvariant().Replace('-', '_');
        }

        public static List<HelpSection> ParseSections(string markdown)
        {
            var list = new List<HelpSection>(8);
            if (string.IsNullOrEmpty(markdown)) return list;

            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var body = new StringBuilder(512);
            HelpSection current = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        FinalizeSection(current, body.ToString());
                        list.Add(current);
                        body.Length = 0;
                    }
                    string title = line.Substring(3).Trim();
                    current = new HelpSection
                    {
                        Id = Slugify(title),
                        Title = title,
                        RichTextBody = ""
                    };
                    continue;
                }
                if (current == null) continue;
                body.AppendLine(line);
            }
            if (current != null)
            {
                FinalizeSection(current, body.ToString());
                list.Add(current);
            }
            return list;
        }

        private static void FinalizeSection(HelpSection sec, string rawBody)
        {
            if (sec == null) return;
            sec.ContentLines = ParseContentLines(rawBody);
            sec.RichTextBody = MarkdownBlockToRichText(rawBody);
        }

        public static List<HelpContentLine> ParseContentLines(string block)
        {
            var list = new List<HelpContentLine>(24);
            if (string.IsNullOrEmpty(block)) return list;

            string[] lines = block.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimEnd();
                if (trimmed.Length == 0)
                {
                    list.Add(new HelpContentLine { IsBlank = true });
                    continue;
                }

                var row = new HelpContentLine();
                if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                {
                    row.IsSubheading = true;
                    row.Segments.Add(new HelpInlineSegment
                    {
                        Kind = HelpInlineKind.Text,
                        Text = InlineMarkdownToRichText(trimmed.Substring(4).Trim())
                    });
                    list.Add(row);
                    continue;
                }
                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    row.IsSubheading = true;
                    row.Segments.Add(new HelpInlineSegment
                    {
                        Kind = HelpInlineKind.Text,
                        Text = InlineMarkdownToRichText(trimmed.Substring(2).Trim())
                    });
                    list.Add(row);
                    continue;
                }
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    row.IsBullet = true;
                    AppendInlineSegments(row.Segments, trimmed.Substring(2).Trim());
                    list.Add(row);
                    continue;
                }
                int dotIdx = trimmed.IndexOf('.');
                if (dotIdx > 0 && dotIdx <= 4)
                {
                    bool allDigits = true;
                    for (int k = 0; k < dotIdx; k++)
                    {
                        if (!char.IsDigit(trimmed[k])) { allDigits = false; break; }
                    }
                    if (allDigits)
                    {
                        int j = dotIdx + 1;
                        while (j < trimmed.Length && trimmed[j] == ' ') j++;
                        row.IsNumbered = true;
                        row.NumberPrefix = trimmed.Substring(0, dotIdx);
                        AppendInlineSegments(row.Segments, trimmed.Substring(j).Trim());
                        list.Add(row);
                        continue;
                    }
                }

                AppendInlineSegments(row.Segments, trimmed);
                list.Add(row);
            }
            return list;
        }

        private static void AppendInlineSegments(List<HelpInlineSegment> segments, string line)
        {
            if (segments == null) return;
            if (string.IsNullOrEmpty(line))
            {
                segments.Add(new HelpInlineSegment { Kind = HelpInlineKind.Text, Text = "" });
                return;
            }

            int pos = 0;
            Match m = s_IconRefRegex.Match(line);
            while (m.Success)
            {
                if (m.Index > pos)
                {
                    string chunk = line.Substring(pos, m.Index - pos);
                    if (chunk.Length > 0)
                        segments.Add(new HelpInlineSegment { Kind = HelpInlineKind.Text, Text = InlineMarkdownToRichText(chunk) });
                }
                segments.Add(new HelpInlineSegment
                {
                    Kind = HelpInlineKind.IconRef,
                    IconId = m.Groups[1].Value.Trim(),
                    IconLabel = StripInlineMarkdown(m.Groups[2].Value.Trim())
                });
                pos = m.Index + m.Length;
                m = s_IconRefRegex.Match(line, pos);
            }
            if (pos < line.Length)
            {
                string tail = line.Substring(pos);
                if (tail.Length > 0)
                    segments.Add(new HelpInlineSegment { Kind = HelpInlineKind.Text, Text = InlineMarkdownToRichText(tail) });
            }
        }

        public static string MarkdownBlockToRichText(string block)
        {
            if (string.IsNullOrEmpty(block)) return "";
            var sb = new StringBuilder(block.Length + 64);
            string[] lines = block.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    sb.Append("\n<color=#88CCFF>").Append(EscapeRichText(line.Substring(4).Trim())).Append("</color>\n");
                    continue;
                }
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    sb.Append("\n<color=#88CCFF>").Append(EscapeRichText(line.Substring(2).Trim())).Append("</color>\n");
                    continue;
                }
                string trimmed = line.TrimEnd();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    sb.Append("\n  \u2022 ").Append(InlineMarkdownToRichText(ReplaceIconRefsForRichText(trimmed.Substring(2).Trim()))).Append("\n");
                    continue;
                }
                if (trimmed.Length == 0)
                {
                    sb.Append("\n");
                    continue;
                }
                sb.Append("\n").Append(InlineMarkdownToRichText(ReplaceIconRefsForRichText(trimmed))).Append("\n");
            }
            return sb.ToString().Trim();
        }

        private static string ReplaceIconRefsForRichText(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            try
            {
                return s_IconRefRegex.Replace(line, m =>
                {
                    string label = StripInlineMarkdown(m.Groups[2].Value.Trim());
                    return "<color=#88CCFF>" + EscapeRichText(label) + "</color>";
                });
            }
            catch { return line; }
        }

        private static string StripInlineMarkdown(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
                s = Regex.Replace(s, @"`(.+?)`", "$1");
            }
            catch { }
            return s;
        }

        public static string InlineMarkdownToRichText(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            string s = EscapeRichText(line);
            try
            {
                s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
                s = Regex.Replace(s, @"`(.+?)`", "<color=#A8D4FF>$1</color>");
            }
            catch { }
            return s;
        }

        private static string EscapeRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string Slugify(string title)
        {
            if (string.IsNullOrEmpty(title)) return "section";
            var sb = new StringBuilder(title.Length);
            bool lastDash = false;
            for (int i = 0; i < title.Length; i++)
            {
                char c = char.ToLowerInvariant(title[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastDash = false;
                }
                else if (c == ' ' || c == '-' || c == '/')
                {
                    if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
                }
            }
            string r = sb.ToString().Trim('-');
            return r.Length > 0 ? r : "section";
        }

        public struct HelpIconRefCharSpan
        {
            public string IconId;
            public string Label;
            public int Start;
            public int End;
        }

        public static string BuildLinePlainText(HelpContentLine line)
        {
            if (line == null || line.Segments == null) return "";

            var sb = new StringBuilder(128);
            if (line.IsBullet)
                sb.Append("  \u2022 ");
            else if (line.IsNumbered && !string.IsNullOrEmpty(line.NumberPrefix))
                sb.Append(line.NumberPrefix).Append(". ");

            for (int i = 0; i < line.Segments.Count; i++)
            {
                HelpInlineSegment seg = line.Segments[i];
                if (seg == null) continue;
                if (seg.Kind == HelpInlineKind.Text)
                {
                    if (!string.IsNullOrEmpty(seg.Text)) sb.Append(seg.Text);
                }
                else if (seg.Kind == HelpInlineKind.IconRef)
                {
                    string label = string.IsNullOrEmpty(seg.IconLabel) ? seg.IconId : seg.IconLabel;
                    sb.Append(EscapeRichText(label));
                }
            }
            return sb.ToString();
        }

        public static string BuildLineRichText(HelpContentLine line, List<HelpIconRefCharSpan> spansOut)
        {
            if (spansOut != null) spansOut.Clear();
            if (line == null || line.Segments == null) return "";

            var sb = new StringBuilder(128);
            int plainLen = 0;

            if (line.IsBullet)
            {
                sb.Append("  \u2022 ");
                plainLen += 4;
            }
            else if (line.IsNumbered && !string.IsNullOrEmpty(line.NumberPrefix))
            {
                string pref = line.NumberPrefix + ". ";
                sb.Append(pref);
                plainLen += pref.Length;
            }

            for (int i = 0; i < line.Segments.Count; i++)
            {
                HelpInlineSegment seg = line.Segments[i];
                if (seg == null) continue;
                if (seg.Kind == HelpInlineKind.Text)
                {
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    sb.Append(seg.Text);
                    plainLen += VisibleRichTextLength(seg.Text);
                }
                else if (seg.Kind == HelpInlineKind.IconRef)
                {
                    string label = string.IsNullOrEmpty(seg.IconLabel) ? seg.IconId : seg.IconLabel;
                    int start = plainLen;
                    sb.Append(EscapeRichText(label));
                    plainLen += label.Length;
                    if (spansOut != null)
                    {
                        spansOut.Add(new HelpIconRefCharSpan
                        {
                            IconId = seg.IconId,
                            Label = label,
                            Start = start,
                            End = plainLen
                        });
                    }
                }
            }
            return sb.ToString();
        }

        private static int VisibleRichTextLength(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return 0;
            var sb = new StringBuilder(rich.Length);
            bool inTag = false;
            for (int i = 0; i < rich.Length; i++)
            {
                char c = rich[i];
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) sb.Append(c);
            }
            string visible = sb.ToString();
            if (visible.IndexOf('&') < 0) return visible.Length;
            visible = visible.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
            return visible.Length;
        }

        public static string SegmentsToRichTextLine(List<HelpInlineSegment> segments)
        {
            if (segments == null || segments.Count == 0) return "";
            var sb = new StringBuilder(128);
            for (int i = 0; i < segments.Count; i++)
            {
                HelpInlineSegment seg = segments[i];
                if (seg == null) continue;
                if (seg.Kind == HelpInlineKind.Text)
                {
                    if (!string.IsNullOrEmpty(seg.Text)) sb.Append(seg.Text);
                }
                else if (seg.Kind == HelpInlineKind.IconRef)
                {
                    string label = string.IsNullOrEmpty(seg.IconLabel) ? seg.IconId : seg.IconLabel;
                    sb.Append("<color=#88CCFF>").Append(EscapeRichText(label)).Append("</color>");
                }
            }
            return sb.ToString();
        }

        public static void CollectIconRefSegments(List<HelpInlineSegment> segments, List<HelpInlineSegment> iconRefsOut)
        {
            if (segments == null || iconRefsOut == null) return;
            for (int i = 0; i < segments.Count; i++)
            {
                HelpInlineSegment seg = segments[i];
                if (seg != null && seg.Kind == HelpInlineKind.IconRef)
                    iconRefsOut.Add(seg);
            }
        }

        public static bool LineHasIconRef(HelpContentLine line)
        {
            if (line == null || line.Segments == null) return false;
            for (int i = 0; i < line.Segments.Count; i++)
            {
                HelpInlineSegment seg = line.Segments[i];
                if (seg != null && seg.Kind == HelpInlineKind.IconRef)
                    return true;
            }
            return false;
        }

        public static string JoinTextSegments(List<HelpInlineSegment> segments)
        {
            if (segments == null || segments.Count == 0) return "";
            var sb = new StringBuilder(128);
            for (int i = 0; i < segments.Count; i++)
            {
                HelpInlineSegment seg = segments[i];
                if (seg != null && seg.Kind == HelpInlineKind.Text && !string.IsNullOrEmpty(seg.Text))
                    sb.Append(seg.Text);
            }
            return sb.ToString();
        }

        private static string MinimalBuiltInEnglishMarkdown()
        {
            return @"## Filtering

### Browse by category
Tap the **category name** in the title bar (top-left) to open the quick-switch menu.

Or open the {{icon:category|Category}} side button — **red** category grid icon (not {{icon:path|Path}} folder).

## Advanced

### On-demand texture cache
Select packages/scenes, then {{icon:cache_texture|Cache Textures}} in the toolbox. **Ctrl+click** rewrites zstd; **Ctrl+Shift+click** purges cache.

### Bulk Zstd compress
{{icon:settings|Settings}} → Plugin → {{icon:compress|Compress Cache}} migrates .vamcache to zstd in bulk.
";
        }
    }
}
