using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    /// <summary>
    /// One AND-group inside a title-bar search (all atoms must match).
    /// Top-level query is OR of these groups.
    /// </summary>
    internal sealed class GallerySearchBranch
    {
        internal readonly List<string> BroadTerms = new List<string>();
        /// <summary>Bare terms that must NOT match name/path/creator/uid/user-tag (AND).</summary>
        internal readonly List<string> BroadExclude = new List<string>();
        internal readonly List<string> TagInclude = new List<string>();
        internal readonly List<string> TagExclude = new List<string>();
        internal readonly List<string> CreatorTerms = new List<string>();
        /// <summary>Data-pack atoms, allocated only when a query actually uses one.</summary>
        internal List<string> PackSubjectInclude;
        internal List<string> PackSubjectExclude;
        internal List<string> PackHubTagInclude;
        internal List<string> PackHubTagExclude;
        internal List<string> PackHubCatInclude;
        internal List<string> PackHubCatExclude;
        internal List<string> PackAnyInclude;
        internal List<string> PackAnyExclude;
        internal GallerySearchQuery.StatusFlags Status = GallerySearchQuery.StatusFlags.None;

        internal bool HasDataPackAtoms
        {
            get
            {
                return GallerySearchQuery.Any(PackSubjectInclude)
                    || GallerySearchQuery.Any(PackSubjectExclude)
                    || GallerySearchQuery.Any(PackHubTagInclude)
                    || GallerySearchQuery.Any(PackHubTagExclude)
                    || GallerySearchQuery.Any(PackHubCatInclude)
                    || GallerySearchQuery.Any(PackHubCatExclude)
                    || GallerySearchQuery.Any(PackAnyInclude)
                    || GallerySearchQuery.Any(PackAnyExclude);
            }
        }

        internal bool IsEmpty
        {
            get
            {
                return BroadTerms.Count == 0
                    && BroadExclude.Count == 0
                    && TagInclude.Count == 0
                    && TagExclude.Count == 0
                    && CreatorTerms.Count == 0
                    && !HasDataPackAtoms
                    && Status == GallerySearchQuery.StatusFlags.None;
            }
        }

        internal bool HasFlag(GallerySearchQuery.StatusFlags f)
        {
            return (Status & f) != 0;
        }
    }

    /// <summary>
    /// Parsed title-bar search AST.
    /// </summary>
    internal sealed class GallerySearchQuery
    {
        [Flags]
        internal enum StatusFlags
        {
            None = 0,
            Loaded = 1 << 0,
            Unloaded = 1 << 1,
            Starred = 1 << 2,
            Unrated = 1 << 8,
            Tagged = 1 << 3,
            Untagged = 1 << 4,
            AutoInstall = 1 << 5,
            Hidden = 1 << 6,
            ScanExcluded = 1 << 7,
        }

        /// <summary>Fresh empty query (not a shared mutable singleton).</summary>
        internal static GallerySearchQuery Empty { get { return new GallerySearchQuery(); } }

        /// <summary>OR of AND-branches. Empty list = empty query.</summary>
        internal readonly List<GallerySearchBranch> Branches = new List<GallerySearchBranch>();

        /// <summary>Union of all branch broad terms (tag-key lookup / legacy helpers).</summary>
        internal readonly List<string> BroadTerms = new List<string>();
        /// <summary>Union of all branch broad excludes.</summary>
        internal readonly List<string> BroadExclude = new List<string>();
        /// <summary>Union of all branch tag includes (tag-key lookup).</summary>
        internal readonly List<string> TagInclude = new List<string>();
        /// <summary>Union of all branch tag excludes (tag-key lookup).</summary>
        internal readonly List<string> TagExclude = new List<string>();
        /// <summary>Union of all branch creator terms.</summary>
        internal readonly List<string> CreatorTerms = new List<string>();
        /// <summary>Union of status flags across branches (for RequiresSqlRefresh).</summary>
        internal StatusFlags Status = StatusFlags.None;

        internal bool IsEmpty
        {
            get
            {
                if (Branches.Count == 0) return true;
                for (int i = 0; i < Branches.Count; i++)
                {
                    if (Branches[i] != null && !Branches[i].IsEmpty) return false;
                }
                return true;
            }
        }

        internal bool HasStatusFlags { get { return Status != StatusFlags.None; } }

        /// <summary>Which shipped-data-pack field an atom searches.</summary>
        internal enum DataPackAtomKind
        {
            Subject = 0,
            HubTag = 1,
            Any = 2,
            HubCategory = 3,
        }

        internal readonly List<string> PackSubjectTerms = new List<string>();
        internal readonly List<string> PackHubTagTerms = new List<string>();
        internal readonly List<string> PackHubCatTerms = new List<string>();
        internal readonly List<string> PackAnyTerms = new List<string>();

        /// <summary>True when any branch carries a data-pack atom (include or exclude).</summary>
        internal bool HasDataPackAtoms
        {
            get
            {
                for (int i = 0; i < Branches.Count; i++)
                {
                    GallerySearchBranch br = Branches[i];
                    if (br != null && br.HasDataPackAtoms) return true;
                }
                return false;
            }
        }

        internal static bool Any(List<string> list) { return list != null && list.Count > 0; }

        /// <summary>Needs SQL refresh (loaded / tagged) rather than pure in-memory name scan.</summary>
        internal bool RequiresSqlRefresh
        {
            get
            {
                return (Status & (StatusFlags.Loaded | StatusFlags.Unloaded
                    | StatusFlags.Tagged | StatusFlags.Untagged)) != 0;
            }
        }

        internal bool NeedsSqlOrTagLookup
        {
            get
            {
                return TagInclude.Count > 0
                    || TagExclude.Count > 0
                    || CreatorTerms.Count > 0
                    || HasStatusFlags
                    || BroadTerms.Count > 0
                    || BroadExclude.Count > 0
                    || HasDataPackAtoms;
            }
        }

        internal bool HasFlag(StatusFlags f)
        {
            return (Status & f) != 0;
        }

        internal string[] BroadTermsArray()
        {
            if (BroadTerms.Count == 0) return new string[0];
            return BroadTerms.ToArray();
        }

        internal static GallerySearchQuery FromLegacyNameTerms(string[] nameTerms)
        {
            if (nameTerms == null || nameTerms.Length == 0) return Empty;
            var q = new GallerySearchQuery();
            var branch = new GallerySearchBranch();
            for (int i = 0; i < nameTerms.Length; i++)
            {
                string t = nameTerms[i];
                if (string.IsNullOrEmpty(t)) continue;
                AddUnique(branch.BroadTerms, t.ToLowerInvariant());
            }
            if (branch.IsEmpty) return new GallerySearchQuery();
            q.Branches.Add(branch);
            q.RebuildFlatViews();
            return q;
        }

        internal static GallerySearchQuery Parse(string raw)
        {
            if (raw == null) return new GallerySearchQuery();
            string s = raw.Trim();
            if (s.Length == 0) return new GallerySearchQuery();

            string[] parts = SplitSearchTokens(s);
            if (parts == null || parts.Length == 0) return new GallerySearchQuery();

            var q = new GallerySearchQuery();
            var branch = new GallerySearchBranch();

            for (int i = 0; i < parts.Length; i++)
            {
                string tok = parts[i];
                if (string.IsNullOrEmpty(tok)) continue;
                string lower = tok.ToLowerInvariant();

                // Boolean keywords
                if (lower == "or")
                {
                    if (!branch.IsEmpty)
                    {
                        q.Branches.Add(branch);
                        branch = new GallerySearchBranch();
                    }
                    continue;
                }
                if (lower == "and" || lower == "if")
                    continue; // AND default; IF optional preface before status/atoms

                if (TryConsumeStatusToken(lower, branch)) continue;
                if (TryConsumeDataPackToken(lower, branch)) continue;

                if (lower.StartsWith("-tag:", StringComparison.Ordinal) && lower.Length > 5)
                {
                    AddCommaTagList(branch, lower.Substring(5), forceExclude: true);
                    continue;
                }
                if (lower.StartsWith("-#", StringComparison.Ordinal) && lower.Length > 2)
                {
                    AddCommaTagList(branch, lower.Substring(2), forceExclude: true);
                    continue;
                }
                if (lower.StartsWith("tag:", StringComparison.Ordinal) && lower.Length > 4)
                {
                    AddCommaTagList(branch, lower.Substring(4), forceExclude: false);
                    continue;
                }
                if (lower.StartsWith("tags:", StringComparison.Ordinal) && lower.Length > 5)
                {
                    AddCommaTagList(branch, lower.Substring(5), forceExclude: false);
                    continue;
                }
                if (lower.Length > 1 && lower[0] == '#')
                {
                    AddCommaTagList(branch, lower.Substring(1), forceExclude: false);
                    continue;
                }
                if (lower.StartsWith("creator:", StringComparison.Ordinal) && lower.Length > 8)
                {
                    AddCommaCreatorList(branch, lower.Substring(8));
                    continue;
                }
                if (lower.Length > 1 && lower[0] == '@')
                {
                    AddCommaCreatorList(branch, lower.Substring(1));
                    continue;
                }
                if (lower.StartsWith("badge:", StringComparison.Ordinal) && lower.Length > 6)
                {
                    ApplyBadgeAlias(branch, lower.Substring(6));
                    continue;
                }

                // Comma-only tag shorthand: wet,shiny,-nsfw (no tag: prefix)
                if (lower.IndexOf(',') >= 0)
                {
                    AddCommaTagList(branch, lower, forceExclude: false);
                    continue;
                }

                // Bare exclude: -wet (not -# / -tag:). Must not match name/path/creator/uid/user-tag.
                if (lower.Length > 1 && lower[0] == '-')
                {
                    string rest = lower.Substring(1).Trim();
                    if (rest.Length > 0)
                        AddUnique(branch.BroadExclude, rest);
                    continue;
                }

                AddUnique(branch.BroadTerms, lower);
            }

            if (!branch.IsEmpty)
                q.Branches.Add(branch);

            if (q.IsEmpty) return new GallerySearchQuery();
            q.RebuildFlatViews();
            return q;
        }

        /// <summary>
        /// Clone with BroadTerms/BroadExclude stripped (NameOnly / NameStartsWith cannot use path LIKE).
        /// Keeps tag:/creator:/status so SQL can still narrow structured atoms.
        /// </summary>
        internal GallerySearchQuery WithoutBroadTerms()
        {
            if (IsEmpty || Branches == null || Branches.Count == 0) return Empty;
            var q = new GallerySearchQuery();
            for (int bi = 0; bi < Branches.Count; bi++)
            {
                GallerySearchBranch src = Branches[bi];
                if (src == null) continue;
                var br = new GallerySearchBranch();
                if (src.TagInclude != null)
                    for (int i = 0; i < src.TagInclude.Count; i++) AddUnique(br.TagInclude, src.TagInclude[i]);
                if (src.TagExclude != null)
                    for (int i = 0; i < src.TagExclude.Count; i++) AddUnique(br.TagExclude, src.TagExclude[i]);
                if (src.CreatorTerms != null)
                    for (int i = 0; i < src.CreatorTerms.Count; i++) AddUnique(br.CreatorTerms, src.CreatorTerms[i]);
                CopyPackAtoms(src, br);
                br.Status = src.Status;
                if (!br.IsEmpty) q.Branches.Add(br);
            }
            if (q.IsEmpty) return Empty;
            q.RebuildFlatViews();
            return q;
        }

        private void RebuildFlatViews()
        {
            BroadTerms.Clear();
            BroadExclude.Clear();
            TagInclude.Clear();
            TagExclude.Clear();
            CreatorTerms.Clear();
            PackSubjectTerms.Clear();
            PackHubTagTerms.Clear();
            PackHubCatTerms.Clear();
            PackAnyTerms.Clear();
            Status = StatusFlags.None;
            for (int b = 0; b < Branches.Count; b++)
            {
                GallerySearchBranch br = Branches[b];
                if (br == null) continue;
                for (int i = 0; i < br.BroadTerms.Count; i++) AddUnique(BroadTerms, br.BroadTerms[i]);
                for (int i = 0; i < br.BroadExclude.Count; i++) AddUnique(BroadExclude, br.BroadExclude[i]);
                for (int i = 0; i < br.TagInclude.Count; i++) AddUnique(TagInclude, br.TagInclude[i]);
                for (int i = 0; i < br.TagExclude.Count; i++) AddUnique(TagExclude, br.TagExclude[i]);
                for (int i = 0; i < br.CreatorTerms.Count; i++) AddUnique(CreatorTerms, br.CreatorTerms[i]);
                AddUniqueRange(PackSubjectTerms, br.PackSubjectInclude);
                AddUniqueRange(PackSubjectTerms, br.PackSubjectExclude);
                AddUniqueRange(PackHubTagTerms, br.PackHubTagInclude);
                AddUniqueRange(PackHubTagTerms, br.PackHubTagExclude);
                AddUniqueRange(PackHubCatTerms, br.PackHubCatInclude);
                AddUniqueRange(PackHubCatTerms, br.PackHubCatExclude);
                AddUniqueRange(PackAnyTerms, br.PackAnyInclude);
                AddUniqueRange(PackAnyTerms, br.PackAnyExclude);
                Status |= br.Status;
            }
        }

        /// <summary>
        /// Comma-separated tag list. Leading <c>-</c> on a part = exclude.
        /// Examples: <c>wet,shiny,-nsfw</c>, <c>#a,#b,-c</c>.
        /// </summary>
        private static void AddCommaTagList(GallerySearchBranch branch, string body, bool forceExclude)
        {
            if (branch == null || string.IsNullOrEmpty(body)) return;
            string[] parts = body.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p == null) continue;
                p = p.Trim();
                if (p.Length == 0) continue;
                if (p.Length > 0 && p[0] == '#')
                    p = p.Substring(1).Trim();
                if (p.Length == 0) continue;

                bool exclude = forceExclude;
                if (p[0] == '-')
                {
                    exclude = true;
                    p = p.Substring(1).Trim();
                    if (p.Length > 0 && p[0] == '#')
                        p = p.Substring(1).Trim();
                }
                if (p.Length == 0) continue;
                p = p.ToLowerInvariant();
                if (exclude) AddUnique(branch.TagExclude, p);
                else AddUnique(branch.TagInclude, p);
            }
        }

        private static void AddCommaCreatorList(GallerySearchBranch branch, string body)
        {
            if (branch == null || string.IsNullOrEmpty(body)) return;
            string[] parts = body.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p == null) continue;
                p = p.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                if (p[0] == '@') p = p.Substring(1).Trim();
                if (p.Length == 0) continue;
                AddUnique(branch.CreatorTerms, p);
            }
        }

        private static string[] SplitSearchTokens(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            var list = new List<string>(8);
            var sb = new StringBuilder(s.Length);
            bool inQuote = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    sb.Append(c);
                    continue;
                }
                if (!inQuote && (c == ' ' || c == '\t' || c == '\r' || c == '\n'))
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            if (list.Count == 0) return new string[0];
            return list.ToArray();
        }

        private static bool TryConsumeDataPackToken(string lower, GallerySearchBranch branch)
        {
            if (branch == null || string.IsNullOrEmpty(lower)) return false;

            bool negate = false;
            string s = lower;
            if (s.Length > 1 && s[0] == '-')
            {
                negate = true;
                s = s.Substring(1);
            }

            if (s.StartsWith("looks:", StringComparison.Ordinal) && s.Length > 6)
            {
                AddCommaPackList(branch, s.Substring(6), DataPackAtomKind.Subject, negate);
                return true;
            }
            if (s.StartsWith("hubtag:", StringComparison.Ordinal) && s.Length > 7)
            {
                AddCommaPackList(branch, s.Substring(7), DataPackAtomKind.HubTag, negate);
                return true;
            }
            if (s.StartsWith("hubcat:", StringComparison.Ordinal) && s.Length > 7)
            {
                AddCommaPackList(branch, s.Substring(7), DataPackAtomKind.HubCategory, negate);
                return true;
            }
            if (s.StartsWith("lap:", StringComparison.Ordinal) && s.Length > 4)
            {
                AddCommaPackList(branch, s.Substring(4), DataPackAtomKind.Any, negate);
                return true;
            }
            return false;
        }

        private static void AddCommaPackList(
            GallerySearchBranch branch, string body, DataPackAtomKind kind, bool forceExclude)
        {
            if (branch == null || string.IsNullOrEmpty(body)) return;
            string b = body.Trim();
            if (b.Length >= 2 && b[0] == '"' && b[b.Length - 1] == '"')
            {
                b = b.Substring(1, b.Length - 2).Trim();
                if (b.Length == 0) return;
                AddPackTerm(branch, kind, forceExclude, "=" + b.ToLowerInvariant());
                return;
            }

            string[] parts = b.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p == null) continue;
                p = p.Trim();
                if (p.Length == 0) continue;

                bool exclude = forceExclude;
                if (p[0] == '-')
                {
                    exclude = true;
                    p = p.Substring(1).Trim();
                }
                if (p.Length == 0) continue;
                AddPackTerm(branch, kind, exclude, p.ToLowerInvariant());
            }
        }

        internal static void AddPackTerm(
            GallerySearchBranch branch, DataPackAtomKind kind, bool exclude, string term)
        {
            if (branch == null || string.IsNullOrEmpty(term)) return;
            if (kind == DataPackAtomKind.Subject)
            {
                if (exclude) AddUniqueLazy(ref branch.PackSubjectExclude, term);
                else AddUniqueLazy(ref branch.PackSubjectInclude, term);
            }
            else if (kind == DataPackAtomKind.HubTag)
            {
                if (exclude) AddUniqueLazy(ref branch.PackHubTagExclude, term);
                else AddUniqueLazy(ref branch.PackHubTagInclude, term);
            }
            else if (kind == DataPackAtomKind.HubCategory)
            {
                if (exclude) AddUniqueLazy(ref branch.PackHubCatExclude, term);
                else AddUniqueLazy(ref branch.PackHubCatInclude, term);
            }
            else
            {
                if (exclude) AddUniqueLazy(ref branch.PackAnyExclude, term);
                else AddUniqueLazy(ref branch.PackAnyInclude, term);
            }
        }

        private static void AddUniqueLazy(ref List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (list == null) list = new List<string>(2);
            AddUnique(list, value);
        }

        private static void CopyPackAtoms(GallerySearchBranch src, GallerySearchBranch dst)
        {
            if (src == null || dst == null) return;
            CopyPackList(src.PackSubjectInclude, ref dst.PackSubjectInclude);
            CopyPackList(src.PackSubjectExclude, ref dst.PackSubjectExclude);
            CopyPackList(src.PackHubTagInclude, ref dst.PackHubTagInclude);
            CopyPackList(src.PackHubTagExclude, ref dst.PackHubTagExclude);
            CopyPackList(src.PackHubCatInclude, ref dst.PackHubCatInclude);
            CopyPackList(src.PackHubCatExclude, ref dst.PackHubCatExclude);
            CopyPackList(src.PackAnyInclude, ref dst.PackAnyInclude);
            CopyPackList(src.PackAnyExclude, ref dst.PackAnyExclude);
        }

        private static void CopyPackList(List<string> src, ref List<string> dst)
        {
            if (src == null) return;
            for (int i = 0; i < src.Count; i++) AddUniqueLazy(ref dst, src[i]);
        }

        private static void AddUniqueRange(List<string> dest, List<string> src)
        {
            if (dest == null || src == null) return;
            for (int i = 0; i < src.Count; i++) AddUnique(dest, src[i]);
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(value);
        }

        private static bool TryConsumeStatusToken(string lower, GallerySearchBranch branch)
        {
            if (branch == null || string.IsNullOrEmpty(lower)) return false;
            switch (lower)
            {
                case "loaded":
                case "installed":
                    branch.Status |= StatusFlags.Loaded;
                    return true;
                case "unloaded":
                    branch.Status |= StatusFlags.Unloaded;
                    return true;
                case "starred":
                case "rated":
                case "star":
                    branch.Status |= StatusFlags.Starred;
                    return true;
                case "unrated":
                case "not-rated":
                case "notrated":
                case "unstarred":
                    branch.Status |= StatusFlags.Unrated;
                    return true;
                case "tagged":
                    branch.Status |= StatusFlags.Tagged;
                    return true;
                case "untagged":
                    branch.Status |= StatusFlags.Untagged;
                    return true;
                case "autoinstall":
                case "auto-install":
                    branch.Status |= StatusFlags.AutoInstall;
                    return true;
                case "hidden":
                case "hide":
                    branch.Status |= StatusFlags.Hidden;
                    return true;
                case "whitelist":
                case "whitelisted":
                case "scanexcluded":
                case "scan-excluded":
                    branch.Status |= StatusFlags.ScanExcluded;
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyBadgeAlias(GallerySearchBranch branch, string alias)
        {
            if (branch == null || string.IsNullOrEmpty(alias)) return;
            alias = alias.Trim().ToLowerInvariant();
            switch (alias)
            {
                case "a":
                case "ai":
                case "auto":
                case "autoinstall":
                case "auto-install":
                    branch.Status |= StatusFlags.AutoInstall;
                    break;
                case "h":
                case "hide":
                case "hidden":
                    branch.Status |= StatusFlags.Hidden;
                    break;
                case "w":
                case "whitelist":
                case "scan":
                case "scanexcluded":
                    branch.Status |= StatusFlags.ScanExcluded;
                    break;
                case "t":
                case "tag":
                case "tags":
                case "tagged":
                    branch.Status |= StatusFlags.Tagged;
                    break;
                case "star":
                case "starred":
                case "rated":
                case "*":
                    branch.Status |= StatusFlags.Starred;
                    break;
                case "unrated":
                case "not-rated":
                case "notrated":
                case "unstarred":
                    branch.Status |= StatusFlags.Unrated;
                    break;
            }
        }
    }
}
