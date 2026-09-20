using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal sealed class OutlinerFilterQuery
    {
        internal const int Unset = -1;
        internal const int No = 0;
        internal const int Yes = 1;

        internal readonly List<string> Terms = new List<string>(4);
        internal readonly List<string> Types = new List<string>(2);
        internal readonly List<string> Packages = new List<string>(2);
        internal int On = Unset;
        internal int Hidden = Unset;
        internal int Collision = Unset;
        internal int Parented = Unset;
        internal int Packaged = Unset;

        internal bool IsEmpty
        {
            get
            {
                return Terms.Count == 0 && Types.Count == 0 && Packages.Count == 0
                    && On == Unset && Hidden == Unset && Collision == Unset
                    && Parented == Unset && Packaged == Unset;
            }
        }

        internal void Reset()
        {
            Terms.Clear();
            Types.Clear();
            Packages.Clear();
            On = Unset;
            Hidden = Unset;
            Collision = Unset;
            Parented = Unset;
            Packaged = Unset;
        }
    }

    internal static class OutlinerFilter
    {
        static readonly char[] PathSeparators = { '/', '\\' };
        static readonly char[] TermSeparators = { ' ', '\t' };
        static readonly OutlinerFilterQuery Scratch = new OutlinerFilterQuery();

        internal static OutlinerFilterQuery Parse(string query)
        {
            OutlinerFilterQuery q = Scratch;
            q.Reset();
            if (string.IsNullOrEmpty(query)) return q;
            string[] parts = query.Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                ParseTerm(q, parts[i]);
            return q;
        }

        static void ParseTerm(OutlinerFilterQuery q, string term)
        {
            if (string.IsNullOrEmpty(term)) return;
            int colon = term.IndexOf(':');
            if (colon <= 0 || colon >= term.Length - 1)
            {
                q.Terms.Add(term);
                return;
            }
            string key = term.Substring(0, colon);
            string value = term.Substring(colon + 1);
            if (EqualsKey(key, "t") || EqualsKey(key, "type"))
            {
                q.Types.Add(value);
                return;
            }
            if (EqualsKey(key, "pkg") || EqualsKey(key, "package"))
            {
                if (EqualsKey(value, "none")) q.Packaged = OutlinerFilterQuery.No;
                else if (EqualsKey(value, "any")) q.Packaged = OutlinerFilterQuery.Yes;
                else q.Packages.Add(value);
                return;
            }
            if (EqualsKey(key, "is"))
            {
                if (EqualsKey(value, "on")) q.On = OutlinerFilterQuery.Yes;
                else if (EqualsKey(value, "off")) q.On = OutlinerFilterQuery.No;
                else if (EqualsKey(value, "hidden")) q.Hidden = OutlinerFilterQuery.Yes;
                else if (EqualsKey(value, "visible")) q.Hidden = OutlinerFilterQuery.No;
                else if (EqualsKey(value, "collision")) q.Collision = OutlinerFilterQuery.Yes;
                else if (EqualsKey(value, "nocollision")) q.Collision = OutlinerFilterQuery.No;
                else if (EqualsKey(value, "parented")) q.Parented = OutlinerFilterQuery.Yes;
                else if (EqualsKey(value, "root")) q.Parented = OutlinerFilterQuery.No;
                else q.Terms.Add(term);
                return;
            }
            q.Terms.Add(term);
        }

        static bool EqualsKey(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Matches(OutlinerAtomFacts facts, string query)
        {
            return Matches(facts, Parse(query));
        }

        internal static bool Matches(OutlinerAtomFacts facts, OutlinerFilterQuery query)
        {
            if (query == null || query.IsEmpty) return true;
            if (facts == null) return false;

            for (int i = 0; i < query.Terms.Count; i++)
            {
                string term = query.Terms[i];
                bool hit = Contains(facts.Uid, term)
                    || Contains(facts.Type, term)
                    || Contains(facts.DisplayName, term)
                    || Contains(facts.PackageUid, term);
                if (!hit) return false;
            }
            if (query.Types.Count > 0 && !AnyContains(facts.Type, query.Types)) return false;
            if (query.Packages.Count > 0 && !AnyContains(facts.PackageUid, query.Packages)) return false;
            if (!FlagMatches(query.On, facts.On)) return false;
            if (!FlagMatches(query.Hidden, facts.Hidden)) return false;
            if (!FlagMatches(query.Collision, facts.Collision)) return false;
            if (!FlagMatches(query.Packaged, !string.IsNullOrEmpty(facts.PackageUid))) return false;
            if (!FlagMatches(query.Parented, HasParent(facts))) return false;
            return true;
        }

        static bool HasParent(OutlinerAtomFacts facts)
        {
            return !string.IsNullOrEmpty(facts.ParentUid) || !string.IsNullOrEmpty(facts.SubSceneUid);
        }

        static bool FlagMatches(int want, bool actual)
        {
            if (want == OutlinerFilterQuery.Unset) return true;
            return (want == OutlinerFilterQuery.Yes) == actual;
        }

        static bool AnyContains(string haystack, List<string> needles)
        {
            for (int i = 0; i < needles.Count; i++)
            {
                if (Contains(haystack, needles[i])) return true;
            }
            return false;
        }

        internal static bool Contains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string[] DistinctPathLabels(string[] fullNames)
        {
            if (fullNames == null || fullNames.Length == 0)
                return new string[0];
            int n = fullNames.Length;
            var parts = new string[n][];
            for (int i = 0; i < n; i++)
                parts[i] = SplitPath(fullNames[i]);
            var result = new string[n];
            for (int i = 0; i < n; i++)
                result[i] = DistinctTail(fullNames[i], parts, i);
            return result;
        }

        static string[] SplitPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return new string[0];
            return name.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        }

        static string DistinctTail(string full, string[][] allParts, int self)
        {
            string[] mine = allParts[self];
            if (mine == null || mine.Length <= 1)
                return full ?? "";
            for (int take = 1; take <= mine.Length; take++)
            {
                if (TailIsUnique(mine, take, allParts, self))
                    return JoinTail(mine, take);
            }
            return full ?? "";
        }

        static bool TailIsUnique(string[] mine, int take, string[][] allParts, int self)
        {
            for (int i = 0; i < allParts.Length; i++)
            {
                if (i == self) continue;
                string[] other = allParts[i];
                if (other == null || other.Length < take) continue;
                if (TailsEqual(mine, other, take)) return false;
            }
            return true;
        }

        static bool TailsEqual(string[] a, string[] b, int take)
        {
            int a0 = a.Length - take;
            int b0 = b.Length - take;
            for (int i = 0; i < take; i++)
            {
                if (!string.Equals(a[a0 + i], b[b0 + i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        static string JoinTail(string[] parts, int take)
        {
            int start = parts.Length - take;
            string s = parts[start];
            for (int i = start + 1; i < parts.Length; i++)
                s = s + " · " + parts[i];
            return s;
        }
    }
}
