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

        public bool CanExclude
        {
            // Broad / Tag may exclude. Creator / Status may not.
            get { return Kind == TitleSearchChipKind.Tag || Kind == TitleSearchChipKind.Broad; }
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
                default:
                    return Polarity == TitleSearchChipPolarity.Exclude ? "-" + v : v;
            }
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
            if (dest == null || string.IsNullOrEmpty(value)) return false;
            string v = value.Trim().ToLowerInvariant();
            if (v.Length == 0) return false;

            // Creator / Status cannot exclude.
            if (polarity == TitleSearchChipPolarity.Exclude
                && kind != TitleSearchChipKind.Tag
                && kind != TitleSearchChipKind.Broad)
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
                dest[i] = c;
                return true;
            }

            dest.Add(new TitleSearchChip
            {
                Kind = kind,
                Polarity = polarity,
                Value = v,
                BranchIndex = branchIndex < 0 ? 0 : branchIndex
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

        private static void AppendStatusChips(List<TitleSearchChip> dest, GallerySearchQuery.StatusFlags status, int branchIndex)
        {
            if (status == GallerySearchQuery.StatusFlags.None) return;
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Loaded, "loaded", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Unloaded, "unloaded", branchIndex);
            TryStatus(dest, status, GallerySearchQuery.StatusFlags.Starred, "starred", branchIndex);
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
