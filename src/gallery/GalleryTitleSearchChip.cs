using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal enum TitleSearchChipKind
    {
        Broad = 0,
        Tag = 1,
        Creator = 2,
        Status = 3,
        PackSubject = 4,
        PackHubTag = 5,
        PackAny = 6,
        PackHubCat = 7,
    }

    internal enum TitleSearchChipPolarity
    {
        Include = 0,
        Exclude = 1,
    }

    /// <summary>One committed title-search atom (Enter-committed chip).</summary>
    internal struct TitleSearchChip
    {
        public TitleSearchChipKind Kind;
        public TitleSearchChipPolarity Polarity;
        public string Value;
        public int BranchIndex;
        /// <summary>Quoted on serialize → exact pack-field match (facet click / multi-word).</summary>
        public bool Exact;

        public bool CanExclude
        {
            // Broad / Tag / data-pack atoms may exclude. Creator / Status may not.
            get
            {
                return Kind == TitleSearchChipKind.Tag
                    || Kind == TitleSearchChipKind.Broad
                    || Kind == TitleSearchChipKind.PackSubject
                    || Kind == TitleSearchChipKind.PackHubTag
                    || Kind == TitleSearchChipKind.PackAny
                    || Kind == TitleSearchChipKind.PackHubCat;
            }
        }

        public string ToDisplayLabel()
        {
            string v = Value ?? "";
            switch (Kind)
            {
                case TitleSearchChipKind.Tag:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-#" : "#") + v;
                case TitleSearchChipKind.Creator:
                    return "@" + v;
                case TitleSearchChipKind.Status:
                    return v;
                case TitleSearchChipKind.PackSubject:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-looks:" : "looks:") + v;
                case TitleSearchChipKind.PackHubTag:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-hubtag:" : "hubtag:") + v;
                case TitleSearchChipKind.PackHubCat:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-" : "")
                        + "Hub: " + GalleryHubCategoryNames.Display(v);
                case TitleSearchChipKind.PackAny:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-lap:" : "lap:") + v;
                default:
                    // Broad: bare word; exclude serializes as -term (honest broad exclude).
                    return Polarity == TitleSearchChipPolarity.Exclude ? "-" + v : v;
            }
        }

        public string ToToken()
        {
            string v = Value ?? "";
            switch (Kind)
            {
                case TitleSearchChipKind.Tag:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-#" : "#") + v;
                case TitleSearchChipKind.Creator:
                    return "@" + v;
                case TitleSearchChipKind.Status:
                    return v;
                case TitleSearchChipKind.PackSubject:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-looks:" : "looks:")
                        + QuotePackTokenValue(v, Exact);
                case TitleSearchChipKind.PackHubTag:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-hubtag:" : "hubtag:")
                        + QuotePackTokenValue(v, Exact);
                case TitleSearchChipKind.PackHubCat:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-hubcat:" : "hubcat:")
                        + QuotePackTokenValue(v, Exact);
                case TitleSearchChipKind.PackAny:
                    return (Polarity == TitleSearchChipPolarity.Exclude ? "-lap:" : "lap:")
                        + QuotePackTokenValue(v, Exact);
                default:
                    return Polarity == TitleSearchChipPolarity.Exclude ? "-" + v : v;
            }
        }

        internal static string QuotePackTokenValue(string v, bool exact)
        {
            if (string.IsNullOrEmpty(v)) return "";
            bool quote = exact;
            if (!quote)
            {
                for (int i = 0; i < v.Length; i++)
                {
                    char c = v[i];
                    if (c == ' ' || c == '\t' || c == '"' || c == ',')
                    {
                        quote = true;
                        break;
                    }
                }
            }
            if (!quote) return v;
            string inner = v.IndexOf('"') >= 0 ? v.Replace("\"", "") : v;
            return "\"" + inner + "\"";
        }
    }

    /// <summary>Serialize / hydrate title-search chips ↔ <see cref="GallerySearchQuery"/> string.</summary>
    internal static class GalleryTitleSearchChipUtil
    {
        private static readonly StringBuilder _sb = new StringBuilder(64);

        internal static void Clear(List<TitleSearchChip> chips)
        {
            if (chips != null) chips.Clear();
        }

        internal static void HydrateFromQuery(GallerySearchQuery query, List<TitleSearchChip> dest)
        {
            if (dest == null) return;
            dest.Clear();
            AppendFromQuery(query, dest);
        }

        internal static void AppendFromQuery(GallerySearchQuery query, List<TitleSearchChip> dest, bool forceExclude = false)
        {
            if (dest == null || query == null || query.IsEmpty) return;
            if (query.Branches == null || query.Branches.Count == 0) return;

            for (int bi = 0; bi < query.Branches.Count; bi++)
            {
                GallerySearchBranch br = query.Branches[bi];
                if (br == null || br.IsEmpty) continue;

                if (br.BroadTerms != null)
                {
                    for (int i = 0; i < br.BroadTerms.Count; i++)
                    {
                        TryAdd(dest, TitleSearchChipKind.Broad,
                            forceExclude ? TitleSearchChipPolarity.Exclude : TitleSearchChipPolarity.Include,
                            br.BroadTerms[i], bi);
                    }
                }
                if (br.BroadExclude != null)
                {
                    for (int i = 0; i < br.BroadExclude.Count; i++)
                        TryAdd(dest, TitleSearchChipKind.Broad, TitleSearchChipPolarity.Exclude, br.BroadExclude[i], bi);
                }
                if (br.TagInclude != null)
                {
                    for (int i = 0; i < br.TagInclude.Count; i++)
                    {
                        TryAdd(dest, TitleSearchChipKind.Tag,
                            forceExclude ? TitleSearchChipPolarity.Exclude : TitleSearchChipPolarity.Include,
                            br.TagInclude[i], bi);
                    }
                }
                if (br.TagExclude != null)
                {
                    for (int i = 0; i < br.TagExclude.Count; i++)
                        TryAdd(dest, TitleSearchChipKind.Tag, TitleSearchChipPolarity.Exclude, br.TagExclude[i], bi);
                }
                if (br.CreatorTerms != null)
                {
                    for (int i = 0; i < br.CreatorTerms.Count; i++)
                        TryAdd(dest, TitleSearchChipKind.Creator, TitleSearchChipPolarity.Include, br.CreatorTerms[i], bi);
                }
                AppendPackChips(dest, br, bi, forceExclude);
                AppendStatusChips(dest, br.Status, bi);
            }
        }

        internal static string Serialize(List<TitleSearchChip> chips)
        {
            if (chips == null || chips.Count == 0) return "";

            int maxBranch = 0;
            for (int i = 0; i < chips.Count; i++)
            {
                if (chips[i].BranchIndex > maxBranch) maxBranch = chips[i].BranchIndex;
            }

            _sb.Length = 0;
            bool anyBranch = false;
            for (int b = 0; b <= maxBranch; b++)
            {
                bool branchHas = false;
                for (int i = 0; i < chips.Count; i++)
                {
                    if (chips[i].BranchIndex != b) continue;
                    string tok = chips[i].ToToken();
                    if (string.IsNullOrEmpty(tok)) continue;
                    if (!branchHas)
                    {
                        if (anyBranch) _sb.Append(" OR ");
                        anyBranch = true;
                        branchHas = true;
                    }
                    else _sb.Append(' ');
                    _sb.Append(tok);
                }
            }
            return _sb.ToString();
        }

        internal static bool TryAdd(
            List<TitleSearchChip> dest,
            TitleSearchChipKind kind,
            TitleSearchChipPolarity polarity,
            string value,
            int branchIndex)
        {
            return TryAdd(dest, kind, polarity, value, branchIndex, false);
        }

        internal static bool TryAdd(
            List<TitleSearchChip> dest,
            TitleSearchChipKind kind,
            TitleSearchChipPolarity polarity,
            string value,
            int branchIndex,
            bool exact)
        {
            if (dest == null || string.IsNullOrEmpty(value)) return false;
            string v = value.Trim().ToLowerInvariant();
            if (v.Length == 0) return false;

            // Creator / Status cannot exclude.
            if (polarity == TitleSearchChipPolarity.Exclude
                && kind != TitleSearchChipKind.Tag
                && kind != TitleSearchChipKind.Broad
                && kind != TitleSearchChipKind.PackSubject
                && kind != TitleSearchChipKind.PackHubTag
                && kind != TitleSearchChipKind.PackAny
                && kind != TitleSearchChipKind.PackHubCat)
            {
                polarity = TitleSearchChipPolarity.Include;
            }

            for (int i = 0; i < dest.Count; i++)
            {
                TitleSearchChip c = dest[i];
                if (c.BranchIndex != branchIndex) continue;
                if (c.Kind != kind) continue;
                if (!string.Equals(c.Value, v, StringComparison.OrdinalIgnoreCase)) continue;

                // Same kind+value: update polarity (e.g. Shift+Enter / drag Incl↔Excl).
                c.Polarity = polarity;
                if (exact) c.Exact = true;
                dest[i] = c;
                return true;
            }

            dest.Add(new TitleSearchChip
            {
                Kind = kind,
                Polarity = polarity,
                Value = v,
                BranchIndex = branchIndex < 0 ? 0 : branchIndex,
                Exact = exact
            });
            return true;
        }

        internal static bool TogglePolarity(List<TitleSearchChip> chips, int index)
        {
            if (chips == null || index < 0 || index >= chips.Count) return false;
            TitleSearchChipPolarity next = chips[index].Polarity == TitleSearchChipPolarity.Include
                ? TitleSearchChipPolarity.Exclude
                : TitleSearchChipPolarity.Include;
            return SetPolarity(chips, index, next);
        }

        /// <summary>
        /// Set include/exclude. Broad stays Broad (<c>-term</c>); Tag stays Tag (<c>-#term</c>).
        /// Creator/Status cannot exclude.
        /// </summary>
        internal static bool SetPolarity(List<TitleSearchChip> chips, int index, TitleSearchChipPolarity polarity)
        {
            if (chips == null || index < 0 || index >= chips.Count) return false;
            TitleSearchChip c = chips[index];

            if (polarity == TitleSearchChipPolarity.Exclude)
            {
                if (c.Kind == TitleSearchChipKind.Creator || c.Kind == TitleSearchChipKind.Status)
                    return false;
            }

            if (c.Polarity == polarity)
                return true;

            c.Polarity = polarity;
            chips[index] = c;
            return true;
        }

        private static void AppendPackChips(
            List<TitleSearchChip> dest, GallerySearchBranch br, int branchIndex, bool forceExclude)
        {
            if (br == null) return;
            AppendPackList(dest, br.PackSubjectInclude, TitleSearchChipKind.PackSubject, branchIndex, forceExclude);
            AppendPackList(dest, br.PackSubjectExclude, TitleSearchChipKind.PackSubject, branchIndex, true);
            AppendPackList(dest, br.PackHubTagInclude, TitleSearchChipKind.PackHubTag, branchIndex, forceExclude);
            AppendPackList(dest, br.PackHubTagExclude, TitleSearchChipKind.PackHubTag, branchIndex, true);
            AppendPackList(dest, br.PackHubCatInclude, TitleSearchChipKind.PackHubCat, branchIndex, forceExclude);
            AppendPackList(dest, br.PackHubCatExclude, TitleSearchChipKind.PackHubCat, branchIndex, true);
            AppendPackList(dest, br.PackAnyInclude, TitleSearchChipKind.PackAny, branchIndex, forceExclude);
            AppendPackList(dest, br.PackAnyExclude, TitleSearchChipKind.PackAny, branchIndex, true);
        }

        private static void AppendPackList(
            List<TitleSearchChip> dest, List<string> values, TitleSearchChipKind kind,
            int branchIndex, bool exclude)
        {
            if (values == null) return;
            TitleSearchChipPolarity polarity = exclude
                ? TitleSearchChipPolarity.Exclude
                : TitleSearchChipPolarity.Include;
            for (int i = 0; i < values.Count; i++)
            {
                string v = values[i];
                bool exact = false;
                if (v != null && v.Length > 1 && v[0] == '=')
                {
                    exact = true;
                    v = v.Substring(1);
                }
                TryAdd(dest, kind, polarity, v, branchIndex, exact);
            }
        }

        private static void AppendStatusChips(List<TitleSearchChip> dest, GallerySearchQuery.StatusFlags status, int branchIndex)
        {
            if (status == GallerySearchQuery.StatusFlags.None) return;
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Loaded, "loaded", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Unloaded, "unloaded", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Starred, "starred", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Unrated, "unrated", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Tagged, "tagged", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Untagged, "untagged", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.AutoInstall, "autoinstall", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Hidden, "hidden", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.ScanExcluded, "whitelist", branchIndex);
        }

        private static void TryStatus(
            List<TitleSearchChip> dest,
            GallerySearchQuery.StatusFlags status,
            GallerySearchQuery.StatusFlags flag,
            string token,
            int branchIndex)
        {
            if ((status & flag) == 0) return;
            TryAdd(dest, TitleSearchChipKind.Status, TitleSearchChipPolarity.Include, token, branchIndex);
        }
    }
}
