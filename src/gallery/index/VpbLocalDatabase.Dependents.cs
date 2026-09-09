using System;
using System.Collections.Generic;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        // Below this measured crossover, indexed lookups avoid scanning unrelated edges.
        private const int DependentScanMinimumTargets = 4096;

        private struct DependentPrefix
        {
            internal string Text;
            internal int Length;
            internal DependentPrefix(string text, int length) { Text = text; Length = length; }
        }

        private sealed class DependentPrefixComparer : IEqualityComparer<DependentPrefix>
        {
            // SQLite LIKE folds ASCII only; OrdinalIgnoreCase also folds non-ASCII letters.
            private static char Fold(char c) { return c >= 'A' && c <= 'Z' ? (char)(c + 32) : c; }

            public bool Equals(DependentPrefix a, DependentPrefix b)
            {
                if (a.Length != b.Length) return false;
                if (ReferenceEquals(a.Text, b.Text)) return true;
                for (int i = 0; i < a.Length; i++)
                    if (Fold(a.Text[i]) != Fold(b.Text[i])) return false;
                return true;
            }

            public int GetHashCode(DependentPrefix value)
            {
                unchecked
                {
                    int hash = 17;
                    for (int i = 0; i < value.Length; i++) hash = hash * 31 + Fold(value.Text[i]);
                    return hash;
                }
            }
        }

        private sealed class DependentFamilyCount
        {
            internal int Count;
            internal string LastSource;
        }

        private sealed class DependentTargetCount
        {
            internal DependentFamilyCount Family;
            internal int ExactExtra;
        }

        private static void FinishDependentSource(List<DependentTargetCount> matched, string source)
        {
            // An exact edge outside its family still counts, unless another edge matched that family.
            foreach (var target in matched)
                if (target.Family == null || !string.Equals(target.Family.LastSource, source, StringComparison.Ordinal))
                    target.ExactExtra++;
            matched.Clear();
        }

        private static bool TryCountDependentUidsByScan(VpbSqlite3.Connection conn,
            Dictionary<string, string> uidToShort, Dictionary<string, int> result)
        {
            bool completed = false;
            try
            {
                var exact = new Dictionary<string, DependentTargetCount>(uidToShort.Count, StringComparer.Ordinal);
                var families = new Dictionary<DependentPrefix, DependentFamilyCount>(new DependentPrefixComparer());
                foreach (var pair in uidToShort)
                {
                    if (string.IsNullOrEmpty(pair.Key) || pair.Key.IndexOf('\0') >= 0
                        || (pair.Value != null && pair.Value.IndexOf('\0') >= 0)) return false;
                    DependentFamilyCount family = null;
                    if (!string.IsNullOrEmpty(pair.Value))
                    {
                        var prefix = new DependentPrefix(pair.Value, pair.Value.Length);
                        if (!families.TryGetValue(prefix, out family))
                        {
                            family = new DependentFamilyCount();
                            families.Add(prefix, family);
                        }
                    }
                    exact.Add(pair.Key, new DependentTargetCount { Family = family });
                }

                var matchedExact = new List<DependentTargetCount>();
                string previous = null;
                // ColumnText truncates NUL. Detect it in SQLite before interpreting either UID.
                using (var st = conn.Prepare("SELECT src_uid, dep_uid, instr(src_uid,char(0)) OR instr(dep_uid,char(0)) FROM pkg_dep ORDER BY src_uid"))
                {
                    int rc;
                    while ((rc = st.Step()) == VpbSqlite3.SqliteRow)
                    {
                        if (st.ColumnInt64(2) != 0) return false;
                        string source = st.ColumnText(0);
                        string dependency = st.ColumnText(1);
                        if (!string.Equals(previous, source, StringComparison.Ordinal))
                        {
                            if (previous != null) FinishDependentSource(matchedExact, previous);
                            previous = source;
                        }
                        else source = previous;

                        for (int dot = dependency.IndexOf('.'); dot >= 0; dot = dependency.IndexOf('.', dot + 1))
                        {
                            DependentFamilyCount family;
                            if (families.TryGetValue(new DependentPrefix(dependency, dot), out family)
                                && !string.Equals(family.LastSource, source, StringComparison.Ordinal))
                            {
                                family.Count++;
                                family.LastSource = source;
                            }
                        }
                        DependentTargetCount target;
                        // pkg_dep PRIMARY KEY(src_uid,dep_uid) makes this target unique within this source.
                        if (exact.TryGetValue(dependency, out target)) matchedExact.Add(target);
                    }
                    if (rc != VpbSqlite3.SqliteDone) return false;
                }
                if (previous != null) FinishDependentSource(matchedExact, previous);
                foreach (var pair in exact)
                    result.Add(pair.Key, Math.Max(0, pair.Value.ExactExtra + (pair.Value.Family == null ? 0 : pair.Value.Family.Count)));
                completed = true;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (!completed) result.Clear();
            }
        }
    }
}
