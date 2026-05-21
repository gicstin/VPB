using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    /// <summary>Parsing + validation for tag editor multiline field. Caveman want honest errors, no silent drop.</summary>
    internal static class GalleryUserTagEditorText
    {
        /// <summary>Below this many bad lines cave use status bar; at or above cave use popup list.</summary>
        internal const int RejectPopupMinBadLineCount = 2;

        /// <summary>Split paste: newline only, trim ends, skip empty. Order kept.</summary>
        internal static List<string> ParseMultilineTags(string raw)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(raw)) return lines;
            string norm = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            int len = norm.Length;
            int lineStart = 0;
            for (int i = 0; i <= len; i++)
            {
                bool atBreak = i == len || norm[i] == '\n';
                if (!atBreak) continue;
                int segLen = i - lineStart;
                string piece = segLen > 0 ? norm.Substring(lineStart, segLen).Trim() : "";
                if (piece.Length > 0)
                    lines.Add(piece);
                lineStart = i + 1;
            }
            return lines;
        }

        /// <summary>True if cave can store tag after <see cref="VpbLocalDatabase.NormalizeGalleryUserTagName"/>.</summary>
        internal static bool ValidateTagName(string rawLine, out string normalized, out string errorMessage)
        {
            normalized = VpbLocalDatabase.NormalizeGalleryUserTagName(rawLine);
            errorMessage = "";
            if (string.IsNullOrEmpty(normalized))
            {
                if (rawLine == null) rawLine = "";
                string t = rawLine.Trim();
                if (t.Length == 0)
                {
                    errorMessage = VPBTranslation.T("gallery.usertags.editor_err_blank_line", "Line empty after trim.");
                    return false;
                }
                errorMessage = string.Format(
                    VPBTranslation.T(
                        "gallery.usertags.editor_err_invalid_chars_or_len",
                        "Rejected (length 1–{0}, no line breaks or control chars except tab): «{1}»"),
                    VpbLocalDatabase.GalleryUserTagNameMaxLength,
                    SummarizeForUi(rawLine, VpbLocalDatabase.GalleryUserTagNameMaxLength + 12));
                return false;
            }
            return true;
        }

        internal static string SummarizeForUi(string s, int maxChars)
        {
            if (s == null) return "";
            if (s.Length <= maxChars) return s;
            return s.Substring(0, maxChars) + "…";
        }

        /// <summary>Build dialog body: every bad line listed, cave hide nothing.</summary>
        internal static string FormatTagValidationErrorBody(IList<KeyValuePair<string, string>> lineAndReason)
        {
            if (lineAndReason == null || lineAndReason.Count == 0) return "";
            var sb = new StringBuilder(lineAndReason.Count * 48);
            for (int i = 0; i < lineAndReason.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append("• «");
                sb.Append(lineAndReason[i].Key ?? "");
                sb.Append("» — ");
                sb.Append(lineAndReason[i].Value ?? "");
            }
            return sb.ToString();
        }
    }
}
