using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        internal sealed class SearchVocabularyEntry
        {
            public string Word;
            public string Display;
            public int[] SegmentStarts;
            public int Weight;
        }

        internal const int SearchVocabularyMinWordLength = 3;
        internal const int SearchVocabularyMaxPartLength = 48;
        internal const int SearchVocabularyMaxWords = 80000;

        private static readonly object s_SearchVocabLock = new object();
        private static List<SearchVocabularyEntry> s_SearchVocab;
        private static int s_SearchVocabBuilding;

        internal static bool IsSearchVocabularyReady
        {
            get { lock (s_SearchVocabLock) { return s_SearchVocab != null; } }
        }

        internal static void InvalidateSearchVocabulary()
        {
            lock (s_SearchVocabLock)
            {
                s_SearchVocab = null;
            }
        }

        internal static List<SearchVocabularyEntry> GetSearchVocabularyOrNull()
        {
            lock (s_SearchVocabLock) { return s_SearchVocab; }
        }

        internal static void EnsureSearchVocabularyAsync()
        {
            lock (s_SearchVocabLock)
            {
                if (s_SearchVocab != null) return;
            }
            if (Interlocked.CompareExchange(ref s_SearchVocabBuilding, 1, 0) != 0) return;
            try
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    List<SearchVocabularyEntry> built = null;
                    try { built = BuildSearchVocabulary(); }
                    catch { built = null; }
                    finally
                    {
                        lock (s_SearchVocabLock)
                        {
                            if (built != null)
                                s_SearchVocab = built;
                        }
                        Interlocked.Exchange(ref s_SearchVocabBuilding, 0);
                    }
                });
            }
            catch { Interlocked.Exchange(ref s_SearchVocabBuilding, 0); }
        }

        private static List<SearchVocabularyEntry> BuildSearchVocabulary()
        {
            if (!VpbSqlite3.IsAvailable) return null;

            var byWord = new Dictionary<string, SearchVocabularyEntry>(8192, StringComparer.Ordinal);
            var parts = new List<string>(8);

            using (var conn = new VpbSqlite3.Connection(DbPath))
            {
                using (var st = conn.Prepare("SELECT uid, creator FROM pkg"))
                {
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string uid = st.ColumnText(0) ?? "";
                        string creator = st.ColumnText(1) ?? "";

                        if (uid.Length > 0)
                        {
                            SplitVocabularyParts(uid, parts);
                            for (int i = 0; i < parts.Count; i++)
                                AddVocabularyPart(byWord, parts[i], 1);
                        }
                        if (creator.Length > 0)
                            AddVocabularyPart(byWord, creator.Trim(), 8);
                    }
                }

                try
                {
                    using (var st = conn.Prepare("SELECT name FROM gallery_user_tag"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string name = st.ColumnText(0) ?? "";
                            if (name.Length == 0) continue;
                            AddVocabularyPart(byWord, name.Trim(), 4);
                        }
                    }
                }
                catch { }
            }

            var list = new List<SearchVocabularyEntry>(byWord.Count);
            foreach (var kv in byWord)
            {
                list.Add(kv.Value);
                if (list.Count >= SearchVocabularyMaxWords) break;
            }
            return list;
        }

        private static void SplitVocabularyParts(string uid, List<string> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(uid)) return;

            int start = 0;
            for (int i = 0; i <= uid.Length; i++)
            {
                if (i < uid.Length && uid[i] != '.') continue;
                int len = i - start;
                if (len > 0 && len <= SearchVocabularyMaxPartLength)
                    into.Add(uid.Substring(start, len));
                start = i + 1;
            }
        }

        private static void AddVocabularyPart(
            Dictionary<string, SearchVocabularyEntry> byWord,
            string part,
            int weight)
        {
            if (byWord == null || string.IsNullOrEmpty(part)) return;
            if (part.Length > SearchVocabularyMaxPartLength) return;
            if (!IsUsableAsSearchTerm(part)) return;

            string word;
            int[] segments;
            if (!TryBuildVocabularyWord(part, out word, out segments)) return;
            if (word.Length < SearchVocabularyMinWordLength) return;

            SearchVocabularyEntry e;
            if (byWord.TryGetValue(word, out e))
            {
                e.Weight += weight;
                return;
            }
            if (byWord.Count >= SearchVocabularyMaxWords) return;

            byWord[word] = new SearchVocabularyEntry
            {
                Word = word,
                Display = part,
                SegmentStarts = segments,
                Weight = weight,
            };
        }

        private static bool IsUsableAsSearchTerm(string s)
        {
            if (s.Length == 0) return false;
            if (s[0] == '-') return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) return false;
                if (c == ',' || c == ':' || c == '#' || c == '@' || c == '"') return false;
            }
            return true;
        }

        internal static bool TryBuildVocabularyWord(string part, out string word, out int[] segmentStarts)
        {
            word = null;
            segmentStarts = null;

            var sb = new StringBuilder(part.Length);
            var starts = new List<int>(4);
            bool prevWasSeparator = true;
            char prevKept = '\0';
            bool sawLetter = false;

            for (int i = 0; i < part.Length; i++)
            {
                char c = part[i];
                if (!IsVocabularyWordChar(c))
                {
                    prevWasSeparator = true;
                    continue;
                }

                bool upper = c >= 'A' && c <= 'Z';
                bool digit = c >= '0' && c <= '9';
                if (!digit) sawLetter = true;

                bool boundary = prevWasSeparator || sb.Length == 0;
                if (!boundary)
                {
                    bool prevUpper = prevKept >= 'A' && prevKept <= 'Z';
                    bool prevDigit = prevKept >= '0' && prevKept <= '9';
                    if (upper && !prevUpper) boundary = true;
                    else if (upper && prevUpper && i + 1 < part.Length
                        && part[i + 1] >= 'a' && part[i + 1] <= 'z') boundary = true;
                    else if (digit != prevDigit) boundary = true;
                }

                if (boundary) starts.Add(sb.Length);
                sb.Append(upper ? (char)(c + 32) : c);
                prevKept = c;
                prevWasSeparator = false;
            }

            if (sb.Length == 0 || !sawLetter) return false;
            word = sb.ToString();
            segmentStarts = starts.ToArray();
            return true;
        }

        private static bool IsVocabularyWordChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        internal static bool TryFindSearchVocabularySuggestion(string termLower, out string suggestion)
        {
            suggestion = null;

            List<SearchVocabularyEntry> vocab = GetSearchVocabularyOrNull();
            if (vocab == null || string.IsNullOrEmpty(termLower)) return false;
            if (termLower.Length < GallerySearchRescue.MinTermLengthForTypo) return false;

            SearchVocabularyEntry best = null;
            int bestChunks = int.MaxValue;
            int bestWeight = -1;
            int bestLength = int.MaxValue;

            for (int i = 0; i < vocab.Count; i++)
            {
                SearchVocabularyEntry e = vocab[i];
                if (e == null || e.Word == null) continue;
                if (e.Word.Length < termLower.Length) continue;
                if (string.Equals(e.Word, termLower, StringComparison.Ordinal)) return false;

                int chunks;
                if (!GallerySearchRescue.TryChunkMatch(termLower, e.Word, e.SegmentStarts, out chunks)) continue;

                if (chunks < bestChunks
                    || (chunks == bestChunks && e.Weight > bestWeight)
                    || (chunks == bestChunks && e.Weight == bestWeight && e.Word.Length < bestLength))
                {
                    best = e;
                    bestChunks = chunks;
                    bestWeight = e.Weight;
                    bestLength = e.Word.Length;
                }
            }

            if (best != null)
            {
                suggestion = best.Display;
                return true;
            }

            return TryFindNearestSearchVocabularyWord(termLower, out suggestion);
        }

        private static bool TryFindNearestSearchVocabularyWord(string termLower, out string suggestion)
        {
            suggestion = null;

            List<SearchVocabularyEntry> vocab = GetSearchVocabularyOrNull();
            if (vocab == null || string.IsNullOrEmpty(termLower)) return false;

            int budget = GallerySearchRescue.EditBudgetForLength(termLower.Length);
            if (budget <= 0) return false;

            var prev = new int[64];
            var cur = new int[64];
            int bestDistance = budget + 1;
            int bestWeight = -1;
            SearchVocabularyEntry best = null;

            for (int i = 0; i < vocab.Count; i++)
            {
                SearchVocabularyEntry e = vocab[i];
                if (e == null || e.Word == null) continue;
                if (e.Word.Length >= prev.Length) continue;
                int diff = e.Word.Length - termLower.Length;
                if (diff > budget || diff < -budget) continue;
                if (string.Equals(e.Word, termLower, StringComparison.Ordinal)) return false;

                int d = GallerySearchRescue.BoundedEditDistance(termLower, e.Word, budget, prev, cur);
                if (d > budget) continue;
                if (d < bestDistance || (d == bestDistance && e.Weight > bestWeight))
                {
                    bestDistance = d;
                    bestWeight = e.Weight;
                    best = e;
                }
            }

            if (best == null) return false;
            suggestion = best.Display;
            return true;
        }
    }
}
