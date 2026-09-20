using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal enum GallerySearchRescueKind
    {
        AllCategories = 0,
        ClearFilters = 1,
        AnyWord = 2,
        DidYouMean = 3,
    }

    internal struct GallerySearchRescueOption
    {
        public GallerySearchRescueKind Kind;
        public string Query;
        public string Payload;
        public int Count;
    }

    internal static class GallerySearchRescue
    {
        internal const int MaxOptions = 3;
        internal const int MaxTypoTokensProbed = 3;
        internal const int MinTermLengthForTypo = 4;
        internal const int TwoEditMinTermLength = 8;
        internal const int MaxChunksPerMatch = 4;
        internal const int MinCharsPerChunk = 2;
        internal const int MinCharsFirstChunk = 3;

        internal static void CollectBroadTokens(string raw, GallerySearchQuery query, List<string> into)
        {
            if (into == null) return;
            into.Clear();
            if (string.IsNullOrEmpty(raw) || query == null || query.BroadTerms.Count == 0) return;

            string[] tokens = GallerySearchQuery.SplitSearchTokens(raw);
            if (tokens == null) return;
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (string.IsNullOrEmpty(tok)) continue;
                string lower = tok.ToLowerInvariant();
                if (!ListContainsOrdinal(query.BroadTerms, lower)) continue;
                if (ListContainsOrdinal(into, lower)) continue;
                into.Add(lower);
            }
        }

        internal static bool CanRelaxToAnyWord(GallerySearchQuery query)
        {
            if (query == null || query.Branches.Count != 1) return false;
            GallerySearchBranch br = query.Branches[0];
            if (br == null) return false;
            if (br.BroadTerms.Count < 2) return false;
            return br.BroadExclude.Count == 0
                && br.TagInclude.Count == 0
                && br.TagExclude.Count == 0
                && br.CreatorTerms.Count == 0
                && !br.HasDataPackAtoms
                && br.Status == GallerySearchQuery.StatusFlags.None;
        }

        internal static string BuildAnyWordQuery(GallerySearchQuery query)
        {
            if (!CanRelaxToAnyWord(query)) return null;
            List<string> terms = query.Branches[0].BroadTerms;
            var sb = new StringBuilder(64);
            for (int i = 0; i < terms.Count; i++)
            {
                if (i > 0) sb.Append(" OR ");
                sb.Append(terms[i]);
            }
            return sb.ToString();
        }

        internal static string BuildTermReplacementQuery(string raw, GallerySearchQuery query, string fromLower, string to)
        {
            if (string.IsNullOrEmpty(raw) || query == null) return null;
            if (string.IsNullOrEmpty(fromLower) || string.IsNullOrEmpty(to)) return null;

            string[] tokens = GallerySearchQuery.SplitSearchTokens(raw);
            if (tokens == null || tokens.Length == 0) return null;

            bool replaced = false;
            var sb = new StringBuilder(raw.Length + to.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (string.IsNullOrEmpty(tok)) continue;
                if (sb.Length > 0) sb.Append(' ');
                string lower = tok.ToLowerInvariant();
                if (string.Equals(lower, fromLower, StringComparison.Ordinal)
                    && ListContainsOrdinal(query.BroadTerms, lower))
                {
                    sb.Append(to);
                    replaced = true;
                }
                else sb.Append(tok);
            }
            return replaced ? sb.ToString() : null;
        }

        internal static bool TryChunkMatch(string termLower, string word, int[] segmentStarts, out int chunks)
        {
            chunks = 0;
            if (string.IsNullOrEmpty(termLower) || string.IsNullOrEmpty(word)) return false;
            if (termLower.Length < MinTermLengthForTypo) return false;
            if (word.Length < termLower.Length) return false;

            int ti = 0;
            int pos = 0;
            int count = 0;

            while (ti < termLower.Length)
            {
                if (count >= MaxChunksPerMatch) return false;

                int bestStart = -1;
                int bestRun = 0;
                for (int s = pos; s < word.Length; s++)
                {
                    if (!IsSegmentStart(segmentStarts, s)) continue;
                    if (word[s] != termLower[ti]) continue;
                    int run = CommonPrefixLength(termLower, ti, word, s);
                    if (run > bestRun)
                    {
                        bestRun = run;
                        bestStart = s;
                    }
                }

                int minRun = count == 0 ? MinCharsFirstChunk : MinCharsPerChunk;
                if (bestStart < 0 || bestRun < minRun) return false;

                ti += bestRun;
                pos = bestStart + bestRun;
                count++;
            }

            if (count < 2) return false;
            chunks = count;
            return true;
        }

        internal static bool IsSegmentStart(int[] segmentStarts, int index)
        {
            if (segmentStarts == null) return index == 0;
            for (int i = 0; i < segmentStarts.Length; i++)
            {
                if (segmentStarts[i] == index) return true;
                if (segmentStarts[i] > index) return false;
            }
            return false;
        }

        private static int CommonPrefixLength(string term, int ti, string word, int wi)
        {
            int n = 0;
            while (ti + n < term.Length && wi + n < word.Length && term[ti + n] == word[wi + n]) n++;
            return n;
        }

        internal static int BoundedEditDistance(string a, string b, int max, int[] prev, int[] cur)
        {
            if (a == null || b == null) return max + 1;
            int la = a.Length, lb = b.Length;
            if (la - lb > max || lb - la > max) return max + 1;
            if (la == 0) return lb <= max ? lb : max + 1;
            if (lb == 0) return la <= max ? la : max + 1;
            if (prev == null || cur == null || prev.Length <= lb || cur.Length <= lb) return max + 1;

            for (int j = 0; j <= lb; j++) prev[j] = j;

            for (int i = 1; i <= la; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                char ca = a[i - 1];
                int from = i - max; if (from < 1) from = 1;
                int to = i + max; if (to > lb) to = lb;
                for (int j = 1; j <= lb; j++)
                {
                    if (j < from || j > to) { cur[j] = max + 1; continue; }
                    int cost = ca == b[j - 1] ? 0 : 1;
                    int del = prev[j] + 1;
                    int ins = cur[j - 1] + 1;
                    int sub = prev[j - 1] + cost;
                    int v = del < ins ? del : ins;
                    if (sub < v) v = sub;
                    if (v > max + 1) v = max + 1;
                    cur[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > max) return max + 1;

                int[] swap = prev; prev = cur; cur = swap;
            }
            return prev[lb];
        }

        internal static int EditBudgetForLength(int len)
        {
            if (len < MinTermLengthForTypo) return 0;
            return len >= TwoEditMinTermLength ? 2 : 1;
        }

        internal static bool ListContainsOrdinal(List<string> list, string value)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
