using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    internal struct ThumbPlaceholderLabelParts
    {
        public string Header;
        public string Item;
    }

    public partial class GalleryPanel
    {
        // Pretty-name + search-scope diagnostics. Flip to true when investigating label/search behavior.
        internal static bool LogPrettyNameDiagnostics = false;
        private static int s_PrettyNameSampleCount;
        private const int PrettyNameSampleMax = 12;

        private static void LogPrettyNameSample(FileEntry file, string returned, string caller)
        {
            if (!LogPrettyNameDiagnostics) return;
            if (s_PrettyNameSampleCount >= PrettyNameSampleMax) return;
            try
            {
                s_PrettyNameSampleCount++;
                string kind = file == null ? "null" : file.GetType().Name;
                string nameRaw = file != null ? (file.Name ?? "<null>") : "<null>";
                bool pretty = VPBConfig.Instance != null && VPBConfig.Instance.GalleryPrettyPresetNames;
                LogUtil.LogWarning("[VPB] PRETTY " + caller + " pretty=" + pretty + " kind=" + kind + " raw='" + nameRaw + "' -> '" + (returned ?? "<null>") + "'");
            }
            catch { }
        }

        internal static void ResetPrettyNameDiagnosticsSample()
        {
            s_PrettyNameSampleCount = 0;
        }

        private static readonly Color ColorInactiveRow = GalleryUiColorTokens.RowIdle;
        /// <summary>Path side row with category count 0 — still listed, visually quieter.</summary>
        private static readonly Color ColorPathZeroCount = GalleryUiColorTokens.RowZero;
        private static readonly Color ColorPathZeroCountText = GalleryUiColorTokens.TextZeroCount;
        private static readonly Color ColorCancelRow = GalleryUiColorTokens.SurfaceMid;
        private static readonly Color ColorGroupRow = GalleryUiColorTokens.SurfacePanel;
        private static readonly Color ColorDangerRow = GalleryUiColorTokens.AccentDanger;
        private static readonly Color ColorDangerAllRow = GalleryUiColorTokens.AccentDangerStrong;
        private static readonly Color ColorNewItemRow = GalleryUiColorTokens.AccentNew;
        private static readonly Color ColorFacetActiveRow = GalleryUiColorTokens.AccentFacetGeneric;

        /// <summary>List row label: package uid (Creator.Package.Version) unless legacy file-name mode is on, or pretty mode is on (then the BA-style stripped name wins for every entry kind).</summary>
        private string GetGalleryListRowDisplayName(FileEntry file)
        {
            if (file == null) return "[UNNAMED]";
            bool legacy = VPBConfig.Instance != null && VPBConfig.Instance.GalleryListNamesLegacyFileName;
            bool pretty = VPBConfig.Instance != null && VPBConfig.Instance.GalleryPrettyPresetNames;

            if (pretty)
            {
                string r = GetPrettyEntryDisplayName(file, currentCategoryTitle);
                LogPrettyNameSample(file, r, "ListRow");
                return r;
            }

            if (legacy)
                return string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name;

            // Non-pretty: Creator.Package.N/leaf (same universal scheme, full uid package part).
            if (file is VarFileEntry vfeTitle)
                return FormatVarEntryGalleryTitle(vfeTitle, prettyPackage: false);

            try
            {
                if (file is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                    return ple.Package.Uid;
                if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                    return mple.RequestedUid;
            }
            catch { }
            return string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name;
        }

        private void SetGalleryListRowNameTooltip(GameObject nameGO, FileEntry file)
        {
            if (nameGO == null || file == null) return;
            try
            {
                bool legacy = VPBConfig.Instance != null && VPBConfig.Instance.GalleryListNamesLegacyFileName;
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    if (legacy)
                        AddTooltipPlain(
                            nameGO,
                            string.Format(
                                VPBTranslation.T("gallery.tooltip.package_uid", "Package: {0}.var"),
                                vfe.Package.Uid));
                    else
                    {
                        string hint = string.IsNullOrEmpty(vfe.InternalPath) ? vfe.Name : vfe.InternalPath.Replace('\\', '/');
                        AddTooltipPlain(nameGO, hint);
                    }
                }
                else if (file is PackageListEntry ple && ple.Package != null)
                {
                    if (legacy)
                        AddTooltipPlain(
                            nameGO,
                            string.Format(
                                VPBTranslation.T("gallery.tooltip.package_uid", "Package: {0}.var"),
                                ple.Package.Uid));
                    else if (!string.IsNullOrEmpty(ple.Path))
                        AddTooltipPlain(nameGO, ple.Path);
                }
            }
            catch { }
        }

        /// <summary>
        /// Mirrors BA's resourceDisplayName for every entry kind so pretty mode = stripped label everywhere it renders.
        /// Order of precedence:
        ///   1. .vap presets strip 7-char "Preset_" (BA ResourceManifest.cs:4029).
        ///   2. .json session plugin presets strip 8-char "Plugins_" (BA ResourceManifest.cs:4030).
        ///   3. VAR package rows (PackageListEntry) render <see cref="VarPackage.Name"/>.
        ///   4. VarFileEntry (#90 universal): <c>package/leaf</c> (or <c>package.vN/leaf</c> when old versions shown).
        ///   5. Missing-package rows keep their RequestedUid.
        ///   6. Loose system files fall back to filename without extension.
        /// Couples display with search so what users see equals what they can type.
        /// </summary>
        internal static string GetPrettyEntryDisplayName(FileEntry file)
        {
            return GetPrettyEntryDisplayName(file, null);
        }

        internal static string GetPrettyEntryDisplayName(FileEntry file, string categoryHint)
        {
            if (file == null) return "[UNNAMED]";
            // categoryHint retained for call-site compatibility; dual caption = leaf≠package (no sibling SQL).

            string raw = file.Name;
            if (!string.IsNullOrEmpty(raw))
            {
                int dot = raw.LastIndexOf('.');
                string stem = dot > 0 ? raw.Substring(0, dot) : raw;
                string ext = dot > 0 ? raw.Substring(dot + 1) : "";

                if (string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase)
                    && stem.StartsWith("Preset_", StringComparison.Ordinal)
                    && stem.Length > 7)
                    return stem.Substring(7);

                if (string.Equals(ext, "json", StringComparison.OrdinalIgnoreCase)
                    && stem.StartsWith("Plugins_", StringComparison.Ordinal)
                    && stem.Length > 8)
                    return stem.Substring(8);
            }

            try
            {
                if (file is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Name))
                    return ple.Package.Name;
                if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                    return mple.RequestedUid;
                if (file is VarFileEntry vfe)
                    return FormatVarEntryGalleryTitle(vfe, prettyPackage: true);
            }
            catch { }

            if (!string.IsNullOrEmpty(raw))
            {
                int dot2 = raw.LastIndexOf('.');
                return dot2 > 0 ? raw.Substring(0, dot2) : raw;
            }
            return file.Path ?? "[UNNAMED]";
        }

        /// <summary>
        /// Universal VAR item title for all categories (#90).
        /// Pretty: <c>package/leaf</c>; when old versions visible: <c>package.vN/leaf</c>.
        /// Non-pretty: <c>Creator.Package.N/leaf</c>.
        /// Leaf = file basename stem (same as thumb placeholder). Scrubs creator prefix then collapses when leaf ≈ package name.
        /// Scroll-safe: uid parse + string ops only — no SQLite.
        /// </summary>
        private static string FormatVarEntryGalleryTitle(VarFileEntry vfe, bool prettyPackage)
        {
            string pkgPart;
            string leaf;
            string creator;
            bool dual;
            if (!TryGetVarGalleryTitleParts(vfe, prettyPackage, out pkgPart, out leaf, out creator, out dual))
                return "[UNNAMED]";
            if (!dual) return pkgPart;
            return pkgPart + "/" + leaf;
        }

        /// <summary>
        /// Split VAR title into package + leaf (+ creator) for grid captions.
        /// <paramref name="dual"/> false → use <paramref name="pkgPart"/> alone (collapsed / parse-fail leaf-only).
        /// When dual, primary UI line = leaf, secondary = pkgPart; creator sits on primary row right.
        /// Creator scrub on package + leaf (prefix/suffix/brackets/glued). Collapse: seps, dots≈underscores, alnum, 1-edit typo.
        /// Warm-path: uid parse + index compares; Substring/humanize/alnum-key alloc only when needed.
        /// </summary>
        private static bool TryGetVarGalleryTitleParts(
            VarFileEntry vfe,
            bool prettyPackage,
            out string pkgPart,
            out string leaf,
            out string creator,
            out bool dual)
        {
            pkgPart = null;
            leaf = null;
            creator = null;
            dual = false;
            if (vfe == null) return false;

            leaf = GetThumbPlaceholderItemLine(vfe);
            string uid = vfe.GetRowPackageUid();
            string packageName;
            int version;
            if (!TryParseVarUidParts(uid, out creator, out packageName, out version))
            {
                creator = null;
                if (!string.IsNullOrEmpty(leaf))
                {
                    pkgPart = leaf;
                    leaf = null;
                    return true;
                }
                if (!string.IsNullOrEmpty(uid))
                {
                    pkgPart = uid;
                    return true;
                }
                return false;
            }

            // Creator lives on the right — strip from package + leaf titles (prefix + suffix).
            packageName = ScrubCreatorFromGalleryName(packageName, creator);
            leaf = ScrubCreatorFromGalleryName(leaf, creator);

            // Collapse when leaf ≈ package (seps / version dots / apostrophe noise / 1-char typo).
            if (string.IsNullOrEmpty(leaf)
                || GalleryTitlesShouldCollapse(leaf, packageName))
            {
                string core = string.IsNullOrEmpty(leaf)
                    ? HumanizeGalleryNameSeparators(packageName)
                    : PickPreferredGalleryDisplayName(leaf, packageName);
                pkgPart = prettyPackage
                    ? FormatPackageNameForGalleryLabel(core, version)
                    : uid;
                leaf = null;
                dual = false;
                return true;
            }

            // True dual — different content; still humanize separators for scan.
            if (prettyPackage)
            {
                leaf = HumanizeGalleryNameSeparators(leaf);
                pkgPart = FormatPackageNameForGalleryLabel(
                    HumanizeGalleryNameSeparators(packageName), version);
            }
            else
            {
                pkgPart = uid;
            }
            dual = true;
            return true;
        }

        /// <summary>
        /// Package-list row caption from deferred UID — no <see cref="PackageListEntry.Package"/> resolve on bind.
        /// </summary>
        private static bool TryGetPackageListGalleryLabel(
            PackageListEntry ple,
            out string pkgPart,
            out string creator)
        {
            pkgPart = null;
            creator = null;
            if (ple == null) return false;
            string uid = null;
            try { uid = ple.GetPackageUidForGalleryUserTags(); }
            catch { uid = null; }
            if (string.IsNullOrEmpty(uid)) return false;

            string packageName;
            int version;
            if (!TryParseVarUidParts(uid, out creator, out packageName, out version))
            {
                creator = null;
                pkgPart = uid;
                return true;
            }
            packageName = ScrubCreatorFromGalleryName(packageName, creator);
            pkgPart = FormatPackageNameForGalleryLabel(
                HumanizeGalleryNameSeparators(packageName), version);
            return true;
        }

        /// <summary>Package display name; appends <c>.vN</c> only when old versions are visible.</summary>
        private static string FormatPackageNameForGalleryLabel(string packageName, int version)
        {
            if (string.IsNullOrEmpty(packageName)) return packageName;
            if (!ShouldShowPackageVersionInGalleryTitle()) return packageName;
            if (version < 0) return packageName;
            return packageName + ".v" + version.ToString();
        }

        /// <summary>Prefix then suffix creator scrub.</summary>
        private static string ScrubCreatorFromGalleryName(string name, string creator)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(creator)) return name;
            name = ScrubCreatorPrefixFromName(name, creator);
            return ScrubCreatorSuffixFromName(name, creator);
        }

        /// <summary>
        /// Strip leading creator from a package/leaf title when boundary is clear.
        /// Matches: <c>Creator_rest</c>, <c>Creator.rest</c>, <c>Creator-rest</c>, <c>Creator rest</c>,
        /// or glued catalog form <c>Creator013_rest</c> (digit immediately after creator).
        /// No alloc on miss; one Substring on hit. Empty remainder → original.
        /// </summary>
        private static string ScrubCreatorPrefixFromName(string name, string creator)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(creator)) return name;
            int clen = creator.Length;
            if (name.Length <= clen) return name;
            if (string.Compare(name, 0, creator, 0, clen, StringComparison.OrdinalIgnoreCase) != 0)
                return name;

            char next = name[clen];
            int start;
            if (next == '_' || next == '.' || next == '-' || next == ' ')
            {
                start = clen + 1;
                while (start < name.Length)
                {
                    char c = name[start];
                    if (c != '_' && c != '.' && c != '-' && c != ' ') break;
                    start++;
                }
            }
            else if (next >= '0' && next <= '9')
            {
                start = clen;
            }
            else
            {
                return name;
            }

            if (start >= name.Length) return name;
            return name.Substring(start);
        }

        /// <summary>
        /// Strip trailing creator: <c>Name_Creator</c>, <c>Name - Creator</c>, <c>Name[Creator]</c>,
        /// or glued <c>NameCreator</c> when creator length >= 4 and remainder >= 3.
        /// Mined from local Scenes index (~545 separator-suffix cases).
        /// </summary>
        private static string ScrubCreatorSuffixFromName(string name, string creator)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(creator)) return name;
            int clen = creator.Length;
            if (name.Length <= clen) return name;

            char last = name[name.Length - 1];
            if ((last == ']' || last == ')') && name.Length >= clen + 2)
            {
                char open = last == ']' ? '[' : '(';
                int openIdx = name.Length - clen - 2;
                if (openIdx >= 0
                    && name[openIdx] == open
                    && string.Compare(name, openIdx + 1, creator, 0, clen, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int end = openIdx;
                    while (end > 0 && IsGalleryNameCompareSeparator(name[end - 1])) end--;
                    if (end > 0) return name.Substring(0, end);
                }
            }

            if (string.Compare(name, name.Length - clen, creator, 0, clen, StringComparison.OrdinalIgnoreCase) != 0)
                return name;

            int before = name.Length - clen - 1;
            if (before < 0) return name;
            char prev = name[before];
            if (prev == '_' || prev == '.' || prev == '-' || prev == ' ')
            {
                int end = before;
                while (end > 0 && IsGalleryNameCompareSeparator(name[end - 1])) end--;
                if (end <= 0) return name;
                return name.Substring(0, end);
            }

            if (clen >= 4 && (before + 1) >= 3 && IsGalleryNameLetterOrDigit(prev))
                return name.Substring(0, before + 1);

            return name;
        }

        /// <summary>
        /// True when leaf and package should share one caption.
        /// Order: exact → sep-loose (incl. <c>.</c> for 1_1≈1.1) → alnum-key → 1-edit typo (min len 6).
        /// </summary>
        private static bool GalleryTitlesShouldCollapse(string leaf, string packageName)
        {
            if (string.IsNullOrEmpty(leaf) || string.IsNullOrEmpty(packageName)) return false;
            if (string.Equals(leaf, packageName, StringComparison.OrdinalIgnoreCase)) return true;
            if (GalleryNamesEquivalentLoose(leaf, packageName)) return true;
            if (GalleryNamesEquivalentAlnum(leaf, packageName)) return true;
            return GalleryNamesAlnumEditDistanceAtMostOne(leaf, packageName);
        }

        /// <summary>Separators for equality walks — includes <c>.</c> so <c>1_1</c> ≈ <c>1.1</c>.</summary>
        private static bool IsGalleryNameCompareSeparator(char c)
        {
            return c == '_' || c == ' ' || c == '-' || c == '.';
        }

        /// <summary>Word separators converted to spaces for display (dots kept for versions).</summary>
        private static bool IsGalleryNameSeparator(char c)
        {
            return c == '_' || c == ' ' || c == '-';
        }

        private static bool IsGalleryNameLetterOrDigit(char c)
        {
            if (c >= '0' && c <= '9') return true;
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= 'a' && c <= 'z') return true;
            return char.IsLetterOrDigit(c);
        }

        private static bool IsGalleryNameAlnum(char c)
        {
            if (c >= '0' && c <= '9') return true;
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= 'a' && c <= 'z') return true;
            return char.IsLetterOrDigit(c);
        }

        private static char GalleryFoldAscii(char c)
        {
            if (c >= 'A' && c <= 'Z') return (char)(c + 32);
            return c;
        }

        /// <summary>
        /// True when names match ignoring case and treating <c>_</c>/<c> </c>/<c>-</c>/<c>.</c> runs as equivalent.
        /// Zero alloc. <c>Hero II</c> ≈ <c>Hero_II</c>; <c>marob 4 VAM 1.1</c> ≈ <c>marob_4_VAM_1_1</c>.
        /// </summary>
        private static bool GalleryNamesEquivalentLoose(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

            int i = 0;
            int j = 0;
            int na = a.Length;
            int nb = b.Length;
            while (true)
            {
                while (i < na && IsGalleryNameCompareSeparator(a[i])) i++;
                while (j < nb && IsGalleryNameCompareSeparator(b[j])) j++;
                if (i >= na || j >= nb) break;

                char ca = GalleryFoldAscii(a[i]);
                char cb = GalleryFoldAscii(b[j]);
                if (ca != cb)
                {
                    if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[j]))
                        return false;
                }
                i++;
                j++;
            }
            while (i < na && IsGalleryNameCompareSeparator(a[i])) i++;
            while (j < nb && IsGalleryNameCompareSeparator(b[j])) j++;
            return i == na && j == nb;
        }

        /// <summary>
        /// Equality on letters/digits only (apostrophes, brackets, plus signs ignored).
        /// Zero alloc. <c>Becky's</c> ≈ <c>Beckys</c>; <c>[A3] Saya</c> ≈ <c>A3_saya</c>.
        /// </summary>
        private static bool GalleryNamesEquivalentAlnum(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

            int i = 0;
            int j = 0;
            int na = a.Length;
            int nb = b.Length;
            while (true)
            {
                while (i < na && !IsGalleryNameAlnum(a[i])) i++;
                while (j < nb && !IsGalleryNameAlnum(b[j])) j++;
                if (i >= na || j >= nb) break;

                char ca = GalleryFoldAscii(a[i]);
                char cb = GalleryFoldAscii(b[j]);
                if (ca != cb)
                {
                    if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[j]))
                        return false;
                }
                i++;
                j++;
            }
            while (i < na && !IsGalleryNameAlnum(a[i])) i++;
            while (j < nb && !IsGalleryNameAlnum(b[j])) j++;
            return i == na && j == nb;
        }

        /// <summary>
        /// One insert/delete/replace on alnum stream (min alnum len 6) — catches <c>Transgender</c>/<c>Trasgender</c>.
        /// Allocates two short keys only on this rare path.
        /// </summary>
        private static bool GalleryNamesAlnumEditDistanceAtMostOne(string a, string b)
        {
            string ka = BuildGalleryAlnumKey(a);
            string kb = BuildGalleryAlnumKey(b);
            if (ka.Length < 6 || kb.Length < 6) return false;
            if (ka.Length == kb.Length)
            {
                int diff = 0;
                for (int i = 0; i < ka.Length; i++)
                {
                    if (ka[i] == kb[i]) continue;
                    diff++;
                    if (diff > 1) return false;
                }
                return diff == 1;
            }
            string shorter;
            string longer;
            if (ka.Length + 1 == kb.Length)
            {
                shorter = ka;
                longer = kb;
            }
            else if (kb.Length + 1 == ka.Length)
            {
                shorter = kb;
                longer = ka;
            }
            else return false;

            int si = 0;
            int li = 0;
            int skipped = 0;
            while (si < shorter.Length && li < longer.Length)
            {
                if (shorter[si] == longer[li])
                {
                    si++;
                    li++;
                    continue;
                }
                skipped++;
                if (skipped > 1) return false;
                li++;
            }
            return true;
        }

        private static string BuildGalleryAlnumKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (IsGalleryNameAlnum(s[i])) n++;
            }
            if (n == 0) return "";
            char[] buf = new char[n];
            int w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!IsGalleryNameAlnum(c)) continue;
                buf[w++] = GalleryFoldAscii(c);
            }
            return new string(buf, 0, w);
        }

        /// <summary>
        /// Prefer spaced form and version dots (<c>1.1</c> over <c>1_1</c>); preserve dots when humanizing.
        /// On near-typo pairs prefer longer alnum (usually correct spelling in package uid).
        /// </summary>
        private static string PickPreferredGalleryDisplayName(string a, string b)
        {
            int aDots = GalleryVersionDotScore(a);
            int bDots = GalleryVersionDotScore(b);
            if (aDots != bDots)
                return HumanizeGalleryNameSeparators(aDots > bDots ? a : b);

            bool aSpace = GalleryNameHasChar(a, ' ');
            bool bSpace = GalleryNameHasChar(b, ' ');
            if (aSpace && !bSpace) return HumanizeGalleryNameSeparators(a);
            if (bSpace && !aSpace) return HumanizeGalleryNameSeparators(b);

            int aUgly = GalleryNameSeparatorUglyScore(a);
            int bUgly = GalleryNameSeparatorUglyScore(b);
            if (aUgly != bUgly)
                return HumanizeGalleryNameSeparators(aUgly < bUgly ? a : b);

            int aLen = CountGalleryAlnum(a);
            int bLen = CountGalleryAlnum(b);
            if (aLen != bLen)
                return HumanizeGalleryNameSeparators(aLen > bLen ? a : b);

            return HumanizeGalleryNameSeparators(a);
        }

        private static int GalleryVersionDotScore(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3) return 0;
            int n = 0;
            for (int i = 1; i < s.Length - 1; i++)
            {
                if (s[i] != '.') continue;
                char p = s[i - 1];
                char q = s[i + 1];
                if (p >= '0' && p <= '9' && q >= '0' && q <= '9') n++;
            }
            return n;
        }

        private static int CountGalleryAlnum(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (IsGalleryNameAlnum(s[i])) n++;
            }
            return n;
        }

        private static bool GalleryNameHasChar(string s, char c)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == c) return true;
            }
            return false;
        }

        private static int GalleryNameSeparatorUglyScore(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_' || c == '-') n++;
            }
            return n;
        }

        /// <summary>
        /// Replace <c>_</c>/<c>-</c> with spaces; collapse separator runs; trim. Dots kept (versions).
        /// No alloc when already clean.
        /// </summary>
        private static string HumanizeGalleryNameSeparators(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            bool needs = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_' || c == '-')
                {
                    needs = true;
                    break;
                }
                if (c == ' ')
                {
                    if (i == 0 || i == s.Length - 1)
                    {
                        needs = true;
                        break;
                    }
                    if (i + 1 < s.Length && s[i + 1] == ' ')
                    {
                        needs = true;
                        break;
                    }
                }
            }
            if (!needs) return s;

            char[] buf = new char[s.Length];
            int w = 0;
            bool pendingSpace = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (IsGalleryNameSeparator(c))
                {
                    if (w == 0) continue;
                    pendingSpace = true;
                    continue;
                }
                if (pendingSpace)
                {
                    buf[w++] = ' ';
                    pendingSpace = false;
                }
                buf[w++] = c;
            }
            if (w == 0) return s;
            return new string(buf, 0, w);
        }

        private static bool ShouldShowPackageVersionInGalleryTitle()
        {
            try
            {
                if (Settings.Instance != null && Settings.Instance.HideOldVersions != null
                    && Settings.Instance.HideOldVersions.Value)
                    return false;
            }
            catch { }
            return true;
        }

        private static string FormatBytesForList(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            double d = bytes;
            int i = 0;
            while (d >= 1024.0 && i < suffix.Length - 1)
            {
                d /= 1024.0;
                i++;
            }
            if (i == 0) return bytes.ToString() + " " + suffix[i];
            return d.ToString("0.0") + " " + suffix[i];
        }

        /// <summary>
        /// Compact integer for dense chrome (scrub index, badges). Warm/cold only.
        /// 999 → "999"; 1000 → "1K"; 1200 → "1.2K"; 12_000 → "12K"; 1_000_000 → "1M".
        /// </summary>
        private static string FormatCompactCount(int value)
        {
            if (value < 0) value = 0;
            if (value < 1000) return value.ToString();

            if (value < 10000)
            {
                // Round to 0.1K for 1.0K..9.9K
                int tenths = (value + 50) / 100;
                int whole = tenths / 10;
                int frac = tenths % 10;
                if (whole >= 10) return "10K";
                if (frac == 0) return whole.ToString() + "K";
                return whole.ToString() + "." + frac.ToString() + "K";
            }

            if (value < 1000000)
            {
                int k = (value + 500) / 1000;
                if (k >= 1000) return "1M";
                return k.ToString() + "K";
            }

            if (value < 10000000)
            {
                int tenths = (value + 50000) / 100000;
                int whole = tenths / 10;
                int frac = tenths % 10;
                if (whole >= 10) return "10M";
                if (frac == 0) return whole.ToString() + "M";
                return whole.ToString() + "." + frac.ToString() + "M";
            }

            int m = (value + 500000) / 1000000;
            return m.ToString() + "M";
        }

        private static void AddBorderEdge(GameObject parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        {
            AddBorderEdge(parent, anchorMin, anchorMax, pivot, sizeDelta, Color.white);
        }

        private static void AddBorderEdge(GameObject parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Color color)
        {
            GameObject go = new GameObject("E");
            go.transform.SetParent(parent.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        /// <summary>Removes side-tab rows that pair a primary tab button with optional trailing controls (see <see cref="UI.CreateSideTabSquareIconButton"/>).</summary>
        private static void CleanupSideTabLabeledRows(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Transform ch = container.GetChild(i);
                if (ch == null) continue;
                string n = ch.gameObject.name;
                if (string.Equals(n, "SideTabLabeledRow", StringComparison.Ordinal)
                    || string.Equals(n, "TargetPersonRow", StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(ch.gameObject);
            }
        }

        private void RefreshFilesAndTabs()
        {
            if (settingsListViewActive)
            {
                RefreshInternalSettingsListRows(true);
                // Settings chrome only — full side-tab rebuild not required for settings list rows.
                try { UpdateTabsImpl(rebuildSideTabLists: false, rebuildSubPaneSideTabLists: false); } catch { }
                return;
            }
            ReconcileAutoGenderForCurrentTarget();
            RefreshFiles();
            // Do NOT call UpdateTabs() here. RefreshFiles bumps _deferredSubPaneSessionId, cancels
            // tag/loose-merge coroutines, and schedules DeferredGallerySideTabsAfterGridReady — which
            // rebuilds side strips + facets once the grid is ready. Sync UpdateTabs was a second full
            // rebuild and restarted cancelled scans (chip/subfilter storm).
            try { SyncBrowseFilterChipChrome(); } catch { }
        }

        private string GetSelectedTargetGenderLabel()
        {
            try
            {
                Atom atom = SelectedTargetAtom;
                if (atom == null) return "None";
                if (AtomGenderUtils.IsMale(atom)) return "Male";
                if (AtomGenderUtils.IsFemale(atom)) return "Female";
                return "Unknown";
            }
            catch { return "Unknown"; }
        }

        private void ReconcileAutoGenderForCurrentTarget()
        {
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryAutoGenderFilter) return;
                string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
                bool isClothing = !string.IsNullOrEmpty(title) && title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHair = !string.IsNullOrEmpty(title) && title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isClothing && !isHair) return;

                string genderLabel = GetSelectedTargetGenderLabel();
                if (genderLabel != "Male" && genderLabel != "Female") return;
                bool atomMale = (genderLabel == "Male");

                if (isClothing && !_clothingGenderUserOverride)
                {
                    ClothingSubfilter targetFlag = atomMale ? ClothingSubfilter.Male : ClothingSubfilter.Female;
                    ClothingSubfilter genderBits = clothingSubfilter & (ClothingSubfilter.Male | ClothingSubfilter.Female);
                    if (genderBits == 0)
                    {
                        clothingSubfilter |= targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender apply: Clothing -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                    else if (genderBits != targetFlag)
                    {
                        clothingSubfilter = (clothingSubfilter & ~genderBits) | targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender swap: Clothing " + genderBits + " -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                }
                else if (isHair && !_hairGenderUserOverride)
                {
                    HairSubfilter targetFlag = atomMale ? HairSubfilter.Male : HairSubfilter.Female;
                    HairSubfilter genderBits = hairSubfilter & (HairSubfilter.Male | HairSubfilter.Female);
                    if (genderBits == 0)
                    {
                        hairSubfilter |= targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender apply: Hair -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                    else if (genderBits != targetFlag)
                    {
                        hairSubfilter = (hairSubfilter & ~genderBits) | targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender swap: Hair " + genderBits + " -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Gallery] ReconcileAutoGenderForCurrentTarget failed: " + ex.Message);
            }
        }

        private void OnTargetAtomChanged(string source)
        {
            try
            {
                string uid = "(none)";
                try { Atom a = SelectedTargetAtom; if (a != null) uid = a.uid; } catch { }
                string genderLabel = GetSelectedTargetGenderLabel();
                LogUtil.Log("[VPB.Gallery] target changed via " + (source ?? "unknown") + " -> uid='" + uid + "' gender=" + genderLabel);

                string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
                bool isClothing = !string.IsNullOrEmpty(title) && title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHair = !string.IsNullOrEmpty(title) && title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isClothing && !isHair)
                {
                    LogUtil.Log("[VPB.Gallery] target change ignored for grid (active category '" + title + "' is not Clothing/Hair)");
                    return;
                }
                RefreshFilesAndTabs();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Gallery] OnTargetAtomChanged failed: " + ex.Message);
            }
        }

        private void CloseSidePane(bool isLeft)
        {
            ContentType? closing = isLeft ? leftActiveContent : rightActiveContent;
            if (isLeft) leftActiveContent = leftPrevActiveContent;
            else rightActiveContent = rightPrevActiveContent;

            // Remove Mode's layout sync would reopen the remove list unless we mark dismiss.
            if (_removeModeActive
                && closing.HasValue
                && (closing.Value == ContentType.RemoveClothing
                    || closing.Value == ContentType.RemoveHair
                    || closing.Value == ContentType.RemoveAtom))
                _removeModeSiderailDismissed = true;

            // SyncSideRailChrome (inside UpdateLayout) is what hides the tab scroll column.
            UpdateLayout();
            UpdateTabs();
        }

        private void AddCloseSidePaneRow(Transform container, List<GameObject> trackedButtons, bool isLeft, Color cancelColor)
        {
            if (container == null || trackedButtons == null) return;
            CreateTabButton(container, VPBTranslation.T("gallery.side.close", "Close"), cancelColor, false, () => CloseSidePane(isLeft), trackedButtons);
        }

        private void AddPersonHeaderRow(Transform container, List<GameObject> trackedButtons, string uid, Color groupColor)
        {
            if (container == null || trackedButtons == null) return;
            CreateTabButton(container, " PERSON: " + (uid ?? "") + " ", groupColor, true, null, trackedButtons);
        }

        private static bool MatchesFilter(string value, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<KeyValuePair<string, string>> DistinctSortFilterOptions(IEnumerable<KeyValuePair<string, string>> items, string filter)
        {
            if (items == null) return new List<KeyValuePair<string, string>>();
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in items)
            {
                if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value)) continue;
                if (!MatchesFilter(kvp.Value, filter)) continue;
                if (!seen.ContainsKey(kvp.Key)) seen[kvp.Key] = kvp.Value;
            }
            return seen.Select(k => new KeyValuePair<string, string>(k.Key, k.Value))
                .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ShouldOfferThumbPlaceholderForEntry(FileEntry file)
        {
            if (file == null) return false;
            if (file is InternalSettingRowEntry) return false;
            return true;
        }

        /// <summary>
        /// True when row has no resolvable preview path. Must run after <see cref="GalleryPanel.LoadThumbnail"/>
        /// so <see cref="ThumbnailBindingTag.ExpectedTag"/> reflects path resolution (texture may still be null while decoding).
        /// </summary>
        private static bool ShouldShowThumbPlaceholder(FileEntry file, RawImage thumbImg)
        {
            if (thumbImg == null) return true;
            if (thumbImg.texture != null) return false;
            ThumbnailBindingTag bind = thumbImg.GetComponent<ThumbnailBindingTag>();
            if (bind != null)
            {
                if (bind.CurrentTexture != null) return false;
                if (!string.IsNullOrEmpty(bind.ExpectedTag))
                {
                    int sep = bind.ExpectedTag.IndexOf('|');
                    if (sep >= 0 && sep < bind.ExpectedTag.Length - 1)
                        return false;
                }
            }
            return true;
        }

        private static string GetThumbPlaceholderItemLine(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                if (file is VarFileEntry vfe && !string.IsNullOrEmpty(vfe.InternalPath))
                {
                    string leaf = Path.GetFileName(vfe.InternalPath.Replace('\\', '/'));
                    if (!string.IsNullOrEmpty(leaf))
                    {
                        int dot = leaf.LastIndexOf('.');
                        return dot > 0 ? leaf.Substring(0, dot) : leaf;
                    }
                }
            }
            catch { }

            string raw = file.Name;
            if (string.IsNullOrEmpty(raw))
            {
                string p = file.Path;
                if (string.IsNullOrEmpty(p)) return "";
                raw = Path.GetFileName(p.Replace('\\', '/'));
            }
            if (string.IsNullOrEmpty(raw)) return "";
            int dot2 = raw.LastIndexOf('.');
            return dot2 > 0 ? raw.Substring(0, dot2) : raw;
        }

        private static VarPackage TryResolvePackageForThumbPlaceholder(FileEntry file)
        {
            if (file == null) return null;
            try
            {
                if (file is VarFileEntry vfe)
                {
                    VarPackage pkg = null;
                    try { pkg = vfe.Package; } catch { pkg = null; }
                    if (pkg != null) return pkg;
                    try
                    {
                        string rowUid = vfe.GetRowPackageUid();
                        if (!string.IsNullOrEmpty(rowUid))
                            return FileManager.GetPackage(rowUid, ensureInstalled: false);
                    }
                    catch { }
                }
                else if (file is PackageListEntry ple)
                {
                    try { return ple.Package; } catch { return null; }
                }
                else if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                {
                    try { return FileManager.GetPackage(mple.RequestedUid, ensureInstalled: false); } catch { return null; }
                }
            }
            catch { }
            return null;
        }

        private static bool TryParseVarUidParts(string uid, out string creator, out string packageName, out int version)
        {
            creator = null;
            packageName = null;
            version = -1;
            if (string.IsNullOrEmpty(uid)) return false;

            int lastDot = uid.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= uid.Length - 1) return false;
            if (!int.TryParse(uid.Substring(lastDot + 1), out version) || version < 0) return false;

            string rest = uid.Substring(0, lastDot);
            int firstDot = rest.IndexOf('.');
            if (firstDot <= 0 || firstDot >= rest.Length - 1) return false;
            creator = rest.Substring(0, firstDot);
            packageName = rest.Substring(firstDot + 1);
            return creator.Length > 0 && packageName.Length > 0;
        }

        private static bool TryExtractVarPackageUidFromPath(string path, out string uid)
        {
            uid = null;
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('\\', '/');
            int sep = p.IndexOf(".var:", StringComparison.OrdinalIgnoreCase);
            if (sep < 0) return false;
            int slash = p.LastIndexOf('/', sep);
            uid = slash >= 0 ? p.Substring(slash + 1, sep - slash - 1) : p.Substring(0, sep);
            return !string.IsNullOrEmpty(uid);
        }

        private ThumbPlaceholderLabelParts BuildPluginThumbPlaceholderParts(FileEntry file)
        {
            if (file == null) return default;

            string cacheKey = file.Uid;
            if (string.IsNullOrEmpty(cacheKey)) cacheKey = file.Path;
            if (!string.IsNullOrEmpty(cacheKey))
            {
                try
                {
                    if (thumbPlaceholderLabelCache != null
                        && thumbPlaceholderLabelCache.TryGetValue(cacheKey, out ThumbPlaceholderLabelParts cached))
                        return cached;
                }
                catch { }
            }

            string item = GetThumbPlaceholderItemLine(file);
            string creator = null;
            string pkgName = null;
            int version = -1;

            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg != null)
                {
                    creator = pkg.Creator;
                    pkgName = pkg.Name;
                    version = pkg.Version;
                }
                else
                {
                    string pkgUid = null;
                    if (file is VarFileEntry vfe2)
                    {
                        string u = vfe2.Uid ?? file.Path;
                        if (!string.IsNullOrEmpty(u))
                        {
                            int ix = u.IndexOf(":/", StringComparison.Ordinal);
                            if (ix > 0) pkgUid = u.Substring(0, ix);
                        }
                    }
                    else if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                        pkgUid = mple.RequestedUid;
                    if (string.IsNullOrEmpty(pkgUid))
                        TryExtractVarPackageUidFromPath(file.Path, out pkgUid);
                    if (!string.IsNullOrEmpty(pkgUid))
                        TryParseVarUidParts(pkgUid, out creator, out pkgName, out version);
                }
            }
            catch { }

            var headerSb = new StringBuilder(64);
            if (!string.IsNullOrEmpty(creator))
                headerSb.Append(creator);
            if (!string.IsNullOrEmpty(pkgName))
            {
                if (headerSb.Length > 0) headerSb.Append('\n');
                headerSb.Append(pkgName);
                if (version >= 0)
                {
                    headerSb.Append('.');
                    headerSb.Append(version);
                }
            }

            string header = headerSb.Length > 0 ? headerSb.ToString() : "";
            string itemLine = item ?? "";
            if (string.IsNullOrEmpty(header) && string.IsNullOrEmpty(itemLine))
                itemLine = GetThumbPlaceholderItemLine(file);

            var result = new ThumbPlaceholderLabelParts { Header = header, Item = itemLine };

            try
            {
                if (!string.IsNullOrEmpty(cacheKey) && thumbPlaceholderLabelCache != null)
                {
                    if (thumbPlaceholderLabelCache.Count > 12000) thumbPlaceholderLabelCache.Clear();
                    thumbPlaceholderLabelCache[cacheKey] = result;
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Square thumb side for placeholder fonts. Grid: cell width (1:1 region above caption).
        /// List: list thumb height.
        /// </summary>
        private float GetCanonicalThumbCellSidePx(bool isListMode)
        {
            if (isListMode)
                return Mathf.Max(16f, listThumbSize);

            float side = 100f;
            try
            {
                if (recyclingGrid != null)
                    side = recyclingGrid.CellWidth;
            }
            catch { }

            float pad = 3f;
            try
            {
                if (VPBConfig.Instance != null)
                    pad = Mathf.Clamp(VPBConfig.Instance.GalleryGridThumbnailPadding, 0f, 40f);
            }
            catch { pad = 3f; }
            return Mathf.Max(16f, side - pad * 2f);
        }

        private const float ThumbPlaceholderLineHeightMul = 1.12f;
        /// <summary>~4 lines: creator, name.version, item (+ wrap).</summary>
        private const float ThumbPlaceholderTotalLineBudget = 4f;

        private void InvalidateThumbPlaceholderFontCache()
        {
            _thumbPlaceholderFontLayoutSig = int.MinValue;
        }

        private static bool GalleryThumbPlaceholderLabelsActive()
        {
            try
            {
                return VPBConfig.Instance == null || VPBConfig.Instance.GalleryThumbPlaceholderLabelsEnabled;
            }
            catch { return true; }
        }

        private bool IsActivePluginsGalleryCategory()
        {
            string t = currentCategoryTitle;
            if (string.IsNullOrEmpty(t) && titleText != null) t = titleText.text;
            if (string.IsNullOrEmpty(t)) return false;
            return t.IndexOf("Plugins", StringComparison.OrdinalIgnoreCase) >= 0
                   && t.IndexOf("Preset", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal bool ShouldForcePluginsCategoryLabelOnly(FileEntry file)
        {
            if (file == null) return false;
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.PluginGalleryCategoryLabelsOnly)
                    return false;
            }
            catch { return false; }
            if (!IsActivePluginsGalleryCategory()) return false;
            return IsPluginScriptGalleryFile(file);
        }

        private static int FontSizeFromCanonicalCellSidePx(float cellSidePx, float sizeScale)
        {
            float inner = Mathf.Max(12f, cellSidePx - 6f);
            sizeScale = VPBConfig.ClampGalleryThumbPlaceholderSizeScale(sizeScale);
            float maxByHeight = inner / (ThumbPlaceholderTotalLineBudget * ThumbPlaceholderLineHeightMul);
            float maxByWidth = inner / 5.5f;
            int fs = Mathf.FloorToInt(Mathf.Min(maxByHeight, maxByWidth) * sizeScale);
            return Mathf.Clamp(fs, 6, 28);
        }

        private int GetThumbPlaceholderFontSize(bool isListMode)
        {
            float side = GetCanonicalThumbCellSidePx(isListMode);
            float sizeScale = 0.7f;
            try
            {
                if (VPBConfig.Instance != null)
                    sizeScale = VPBConfig.Instance.GetGalleryThumbPlaceholderSizeScale();
            }
            catch { }
            int sig = Mathf.RoundToInt(side * 4f)
                      ^ (Mathf.RoundToInt(sizeScale * 100f) << 8)
                      ^ (isListMode ? unchecked((int)0x40000000) : 0);
            if (sig == _thumbPlaceholderFontLayoutSig)
                return _thumbPlaceholderFontSize;
            _thumbPlaceholderFontLayoutSig = sig;
            _thumbPlaceholderFontSize = FontSizeFromCanonicalCellSidePx(side, sizeScale);
            return _thumbPlaceholderFontSize;
        }

        private void RefreshThumbPlaceholderLabelLayout()
        {
            InvalidateThumbPlaceholderFontCache();
            try
            {
                if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                else RefreshFiles(true);
                VPBConfig.Instance.TriggerChange();
            }
            catch { }
        }

        private static string SoftBreakLongUnspacedTokens(string text, int maxRun = 14)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxRun) return text ?? "";
            if (text.IndexOf(' ') >= 0 || text.IndexOf('\n') >= 0) return text;

            var sb = new StringBuilder(text.Length + text.Length / maxRun + 4);
            int run = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                sb.Append(c);
                run++;
                if (run >= maxRun)
                {
                    sb.Append('\u200B');
                    run = 0;
                }
            }
            return sb.ToString();
        }

        private static string CombineThumbPlaceholderDisplayText(string header, string item)
        {
            if (!string.IsNullOrEmpty(header))
                header = SoftBreakLongUnspacedTokens(header);
            if (!string.IsNullOrEmpty(item))
                item = SoftBreakLongUnspacedTokens(item);

            var sb = new StringBuilder(96);
            if (!string.IsNullOrEmpty(header))
                sb.Append(header);
            if (!string.IsNullOrEmpty(item))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(item);
            }
            return sb.ToString();
        }

        private const bool ThumbPlaceholderBitmapLabelsEnabled = false;

        private static bool PluginThumbPlaceholderRefsIsUsable(PluginThumbPlaceholderRefs refs)
        {
            return refs != null
                   && refs.Root != null
                   && refs.Label != null
                   && refs.Root.GetComponent<RectMask2D>() != null;
        }

        private static void DestroyPluginThumbPlaceholderRefs(PluginThumbPlaceholderRefs refs)
        {
            if (refs == null) return;
            try
            {
                if (refs.Root != null) UnityEngine.Object.Destroy(refs.Root);
                UnityEngine.Object.Destroy(refs);
            }
            catch { }
        }

        private static void HidePluginThumbPlaceholder(Transform thumbTr)
        {
            if (thumbTr == null) return;
            try
            {
                PluginThumbPlaceholderRefs refs = thumbTr.GetComponent<PluginThumbPlaceholderRefs>();
                if (refs == null) return;
                refs.WantsLabel = false;
                if (refs.Root != null && refs.Root.activeSelf)
                    refs.Root.SetActive(false);
            }
            catch { }
        }

        private static PluginThumbPlaceholderRefs GetOrCreatePluginThumbPlaceholderRefs(Transform thumbTr)
        {
            if (thumbTr == null) return null;

            PluginThumbPlaceholderRefs[] all = thumbTr.GetComponents<PluginThumbPlaceholderRefs>();
            for (int i = 0; i < all.Length; i++)
                TryWirePluginThumbPlaceholderLabel(all[i]);

            PluginThumbPlaceholderRefs primary = null;
            for (int i = 0; i < all.Length; i++)
            {
                PluginThumbPlaceholderRefs r = all[i];
                if (r == null) continue;
                if (PluginThumbPlaceholderRefsIsUsable(r))
                {
                    primary = r;
                    break;
                }
            }

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i] != primary)
                    DestroyPluginThumbPlaceholderRefs(all[i]);
            }

            if (primary != null)
            {
                if (primary.LabelImage == null && primary.Root != null)
                    primary.LabelImage = CreatePluginThumbPlaceholderLabelImage(primary.Root.transform);
                return primary;
            }

            for (int i = 0; i < all.Length; i++)
                DestroyPluginThumbPlaceholderRefs(all[i]);

            EnsurePluginThumbPlaceholderUi(thumbTr);
            return thumbTr.GetComponent<PluginThumbPlaceholderRefs>();
        }

        private static void DestroyLegacyPluginThumbPlaceholder(Transform thumbTr)
        {
            if (thumbTr == null) return;
            try
            {
                PluginThumbPlaceholderRefs[] all = thumbTr.GetComponents<PluginThumbPlaceholderRefs>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (!PluginThumbPlaceholderRefsIsUsable(all[i]))
                        DestroyPluginThumbPlaceholderRefs(all[i]);
                }
            }
            catch { }
        }

        private static void TryWirePluginThumbPlaceholderLabel(PluginThumbPlaceholderRefs refs)
        {
            if (refs == null || refs.Label != null || refs.Root == null) return;
            try
            {
                Text t = refs.Root.GetComponentInChildren<Text>(true);
                if (t != null) refs.Label = t;
            }
            catch { }
        }

        private static Text CreatePluginThumbPlaceholderLabelText(Transform phTr)
        {
            Text label = UI.CreateLabel(phTr.gameObject, "", GalleryUiDesignTokens.FontBodyRef, new Color(0.88f, 0.88f, 0.92f, 1f), TextAnchor.MiddleCenter, richText: false, raycastTarget: false, name: "Text");
            return label;
        }

        private static RawImage CreatePluginThumbPlaceholderLabelImage(Transform phTr)
        {
            GameObject imgGO = UI.CreateChildRT(phTr.gameObject, "LabelImage", AnchorPresets.stretchAll);
            RawImage labelImage = imgGO.AddComponent<RawImage>();
            labelImage.raycastTarget = false;
            labelImage.color = Color.white;
            labelImage.gameObject.SetActive(false);
            return labelImage;
        }

        private static void EnsurePluginThumbPlaceholderUi(Transform thumbTr)
        {
            if (thumbTr == null) return;
            DestroyLegacyPluginThumbPlaceholder(thumbTr);
            PluginThumbPlaceholderRefs existing = thumbTr.GetComponent<PluginThumbPlaceholderRefs>();
            TryWirePluginThumbPlaceholderLabel(existing);
            if (PluginThumbPlaceholderRefsIsUsable(existing)) return;

            GameObject phGO = UI.CreateChildRT(thumbTr.gameObject, "PluginPlaceholder", AnchorPresets.stretchAll);
            RectTransform phRT = phGO.GetComponent<RectTransform>();
            phRT.offsetMin = new Vector2(3f, 3f);
            phRT.offsetMax = new Vector2(-3f, -3f);
            phGO.AddComponent<RectMask2D>();

            Text label = CreatePluginThumbPlaceholderLabelText(phGO.transform);
            RawImage labelImage = CreatePluginThumbPlaceholderLabelImage(phGO.transform);

            phGO.SetActive(false);

            PluginThumbPlaceholderRefs refs = thumbTr.gameObject.AddComponent<PluginThumbPlaceholderRefs>();
            refs.Root = phGO;
            refs.Label = label;
            refs.LabelImage = labelImage;
            refs.UseBitmapLabel = false;
        }

        private void ResolveThumbPlaceholderUi(
            Transform thumbTr,
            RawImage thumbImg,
            FileEntry file,
            bool isListMode,
            out bool noUsableThumb,
            out bool showThumbLabels)
        {
            noUsableThumb = false;
            showThumbLabels = false;
            if (file == null || thumbImg == null) return;

            bool forcePluginLabelsOnly = ShouldForcePluginsCategoryLabelOnly(file);
            noUsableThumb = ShouldOfferThumbPlaceholderForEntry(file)
                            && (forcePluginLabelsOnly || ShouldShowThumbPlaceholder(file, thumbImg));
            showThumbLabels = ShouldOfferThumbPlaceholderForEntry(file)
                              && (forcePluginLabelsOnly
                                  || (GalleryThumbPlaceholderLabelsActive() && noUsableThumb));
            thumbImg.color = noUsableThumb
                ? ThumbnailPlaceholderBackdrop
                : (thumbImg.texture != null ? Color.white : ThumbnailPlaceholderBackdrop);
        }

        internal void SyncThumbPlaceholderForFile(Transform thumbTr, RawImage thumbImg, FileEntry file)
        {
            if (thumbTr == null || thumbImg == null || file == null) return;
            try
            {
                bool listMode = layoutMode == GalleryLayoutMode.List || settingsListViewActive;
                ResolveThumbPlaceholderUi(thumbTr, thumbImg, file, listMode, out bool noUsableThumb, out bool showThumbLabels);
                ApplyPluginThumbPlaceholder(thumbTr, thumbImg, file, listMode, showThumbLabels);
            }
            catch { }
        }

        private static void ApplyThumbPlaceholderLabelContent(
            PluginThumbPlaceholderRefs refs,
            string display,
            int fontSize,
            int pixelSize)
        {
            if (refs == null || refs.Root == null || refs.Label == null) return;

            long bitmapKey = ThumbPlaceholderLabelBitmapCache.MakeKey(display, fontSize, pixelSize);
            bool canTryBitmap = ThumbPlaceholderBitmapLabelsEnabled
                                && !string.IsNullOrEmpty(display)
                                && refs.LabelImage != null
                                && fontSize >= 1
                                && pixelSize >= 32;

            if (canTryBitmap
                && (refs.UseBitmapLabel || refs.CachedBitmapKey != bitmapKey || refs.LabelImage.texture == null))
            {
                Texture2D labelTex;
                if (ThumbPlaceholderLabelBitmapCache.TryGetTexture(display, fontSize, pixelSize, out labelTex)
                    && labelTex != null)
                {
                    refs.LabelImage.texture = labelTex;
                    refs.LabelImage.gameObject.SetActive(true);
                    refs.Label.gameObject.SetActive(false);
                    refs.UseBitmapLabel = true;
                    refs.CachedBitmapKey = bitmapKey;
                    refs.CachedText = display;
                    refs.CachedFontSize = fontSize;
                    return;
                }
            }

            refs.UseBitmapLabel = false;
            refs.CachedBitmapKey = 0;
            if (refs.LabelImage != null)
            {
                refs.LabelImage.texture = null;
                refs.LabelImage.gameObject.SetActive(false);
            }
            refs.Label.gameObject.SetActive(true);
            if (!string.Equals(refs.CachedText, display, StringComparison.Ordinal))
            {
                refs.Label.text = display;
                refs.CachedText = display;
            }
            if (refs.CachedFontSize != fontSize)
            {
                refs.Label.fontSize = fontSize;
                refs.CachedFontSize = fontSize;
            }
        }

        private void ApplyPluginThumbPlaceholder(Transform thumbTr, RawImage thumbImg, FileEntry file, bool isListMode, bool showPlaceholder)
        {
            if (thumbTr == null) return;

            if (!showPlaceholder)
            {
                HidePluginThumbPlaceholder(thumbTr);
                return;
            }

            PluginThumbPlaceholderRefs refs = GetOrCreatePluginThumbPlaceholderRefs(thumbTr);
            if (refs == null || refs.Root == null || refs.Label == null) return;

            refs.WantsLabel = true;

            ThumbPlaceholderLabelParts parts = BuildPluginThumbPlaceholderParts(file);
            int fontSize = GetThumbPlaceholderFontSize(isListMode);
            string display = CombineThumbPlaceholderDisplayText(parts.Header, parts.Item);
            if (string.IsNullOrEmpty(display))
                display = GetThumbPlaceholderItemLine(file);
            int pixelSize = ThumbPlaceholderLabelBitmapCache.QuantizeBakePixelSize(GetCanonicalThumbCellSidePx(isListMode));

            ApplyThumbPlaceholderLabelContent(refs, display, fontSize, pixelSize);

            if (!refs.Root.activeSelf) refs.Root.SetActive(true);
        }
    }
}

