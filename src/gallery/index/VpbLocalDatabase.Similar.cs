using System;
using System.Collections.Generic;

namespace VPB
{
    internal struct SimilarNeighbour
    {
        public string Uid;
        public int Score10k;
        public int SharedDeps;
        public int SharedTags;
    }

    internal enum SimilarIndexState
    {
        Missing = 0,
        Stale = 1,
        Ready = 2,
    }

    internal static partial class VpbLocalDatabase
    {
        private const int SimilarAlgoVersion = 1;
        private const string SimilarSigMetaKey = "pkg_similar:sig";

        private const int SimilarChannelTopK = 48;
        private const int SimilarStoredTopN = 24;
        private const int SimilarMinSharedFeatures = 2;
        private const int SimilarScoreFloor10k = 600;
        private const int SimilarCrossCreatorReserve = 8;
        private const float SimilarDepChannelWeight = 0.60f;
        private const float SimilarTagChannelWeight = 0.40f;
        private const float SimilarCommonFeatureFraction = 0.10f;

        internal static void EnsureSimilarSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS pkg_similar (" +
                "src_uid TEXT NOT NULL, rank INTEGER NOT NULL, dst_uid TEXT NOT NULL, " +
                "score10k INTEGER NOT NULL, shared_deps INTEGER NOT NULL, shared_tags INTEGER NOT NULL, " +
                "PRIMARY KEY(src_uid, rank));");
        }

        internal static SimilarIndexState GetSimilarIndexState()
        {
            string stored, current;
            return GetSimilarIndexState(out stored, out current);
        }

        internal static SimilarIndexState GetSimilarIndexState(out string stored, out string current)
        {
            stored = null;
            current = null;
            if (!VpbSqlite3.IsAvailable) return SimilarIndexState.Missing;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSimilarSchema(conn);
                    stored = MetaGet(conn, SimilarSigMetaKey);
                    if (string.IsNullOrEmpty(stored)) return SimilarIndexState.Missing;
                    current = ComputeSimilarSignature(conn);
                    return string.Equals(stored, current, StringComparison.Ordinal)
                        ? SimilarIndexState.Ready
                        : SimilarIndexState.Stale;
                }
            }
            catch
            {
                return SimilarIndexState.Missing;
            }
        }

        internal static bool TryReadSimilarNeighbours(string srcUid, List<SimilarNeighbour> results)
        {
            if (results == null) return false;
            results.Clear();
            if (string.IsNullOrEmpty(srcUid) || !VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSimilarSchema(conn);
                    if (!ReadSimilarRows(conn, srcUid, results))
                    {
                        string newest = ResolveNewestUidInSameFamily(conn, srcUid);
                        if (!string.IsNullOrEmpty(newest)
                            && !string.Equals(newest, srcUid, StringComparison.OrdinalIgnoreCase))
                            ReadSimilarRows(conn, newest, results);
                    }
                }
                return results.Count > 0;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] pkg_similar read failed: " + ex.Message); } catch { }
                return false;
            }
        }

        private static bool ReadSimilarRows(VpbSqlite3.Connection conn, string srcUid, List<SimilarNeighbour> results)
        {
            using (var st = conn.Prepare(
                "SELECT dst_uid, score10k, shared_deps, shared_tags FROM pkg_similar " +
                "WHERE src_uid = ? COLLATE NOCASE ORDER BY rank"))
            {
                st.BindText(1, srcUid);
                while (st.Step() == VpbSqlite3.SqliteRow)
                {
                    SimilarNeighbour n;
                    n.Uid = st.ColumnText(0);
                    n.Score10k = (int)st.ColumnInt64(1);
                    n.SharedDeps = (int)st.ColumnInt64(2);
                    n.SharedTags = (int)st.ColumnInt64(3);
                    if (!string.IsNullOrEmpty(n.Uid)) results.Add(n);
                }
            }
            return results.Count > 0;
        }

        private static string ResolveNewestUidInSameFamily(VpbSqlite3.Connection conn, string srcUid)
        {
            using (var st = conn.Prepare(
                "SELECT n.uid FROM pkg p JOIN pkg n ON n.family = p.family " +
                "WHERE p.uid = ? COLLATE NOCASE AND n.is_newest = 1 LIMIT 1"))
            {
                st.BindText(1, srcUid);
                if (st.Step() == VpbSqlite3.SqliteRow) return st.ColumnText(0);
            }
            return null;
        }

        private static string ComputeSimilarSignature(VpbSqlite3.Connection conn)
        {
            long pkgCount = 0, depCount = 0, linkCount = 0;
            using (var st = conn.Prepare("SELECT COUNT(*) FROM pkg"))
                if (st.Step() == VpbSqlite3.SqliteRow) pkgCount = st.ColumnInt64(0);
            using (var st = conn.Prepare("SELECT COUNT(*) FROM pkg_dep"))
                if (st.Step() == VpbSqlite3.SqliteRow) depCount = st.ColumnInt64(0);
            try
            {
                using (var st = conn.Prepare("SELECT COUNT(*) FROM datapack_link"))
                    if (st.Step() == VpbSqlite3.SqliteRow) linkCount = st.ColumnInt64(0);
            }
            catch { linkCount = -1; }

            return SimilarAlgoVersion.ToString()
                + ":" + pkgCount.ToString()
                + ":" + depCount.ToString()
                + ":" + linkCount.ToString();
        }

        private sealed class SimilarPackageTable
        {
            internal readonly Dictionary<string, int> IdByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            internal string[] Uid;
            internal int[] CreatorId;
            internal int[] FamilyId;
            internal bool[] IsNewest;
            internal int Count;
        }

        private sealed class SimilarFeatureSets
        {
            internal int[][] FeaturesByPackage;
            internal int[][] PostingsByFeature;
            internal float[] Weight;
            internal float[] Norm;
            internal int DocumentCount;
            internal int CommonFeaturePostingCap;
        }

        internal static bool BuildSimilarIndex(Func<bool> abortRequested, out int seedCount, out int rowCount)
        {
            seedCount = 0;
            rowCount = 0;
            if (!VpbSqlite3.IsAvailable) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSimilarSchema(conn);
                    string signature = ComputeSimilarSignature(conn);

                    SimilarPackageTable packages = LoadSimilarPackageTable(conn);
                    if (packages.Count == 0) return false;
                    if (IsSimilarBuildAborted(abortRequested)) return false;

                    SimilarFeatureSets depSets = LoadSimilarDependencyFeatures(conn, packages);
                    if (IsSimilarBuildAborted(abortRequested)) return false;

                    SimilarFeatureSets tagSets = LoadSimilarHubTagFeatures(conn, packages);
                    if (IsSimilarBuildAborted(abortRequested)) return false;

                    return WriteSimilarIndex(
                        conn, packages, depSets, tagSets, signature, abortRequested,
                        out seedCount, out rowCount);
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] pkg_similar build failed: " + ex.Message); } catch { }
                return false;
            }
        }

        private static bool IsSimilarBuildAborted(Func<bool> abortRequested)
        {
            if (abortRequested == null) return false;
            try { return abortRequested(); }
            catch { return false; }
        }

        private static SimilarPackageTable LoadSimilarPackageTable(VpbSqlite3.Connection conn)
        {
            var table = new SimilarPackageTable();
            var uids = new List<string>(24576);
            var creators = new List<int>(24576);
            var families = new List<int>(24576);
            var newest = new List<bool>(24576);

            var creatorIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var familyIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (var st = conn.Prepare("SELECT uid, creator, family, is_newest FROM pkg"))
            {
                while (st.Step() == VpbSqlite3.SqliteRow)
                {
                    string uid = st.ColumnText(0);
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (table.IdByUid.ContainsKey(uid)) continue;

                    table.IdByUid[uid] = uids.Count;
                    uids.Add(uid);
                    creators.Add(InternSimilarKey(creatorIds, st.ColumnText(1)));
                    families.Add(InternSimilarKey(familyIds, st.ColumnText(2)));
                    newest.Add(st.ColumnInt64(3) != 0);
                }
            }

            table.Count = uids.Count;
            table.Uid = uids.ToArray();
            table.CreatorId = creators.ToArray();
            table.FamilyId = families.ToArray();
            table.IsNewest = newest.ToArray();
            return table;
        }

        private static int InternSimilarKey(Dictionary<string, int> ids, string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            int id;
            if (ids.TryGetValue(key, out id)) return id;
            id = ids.Count;
            ids[key] = id;
            return id;
        }

        private static SimilarFeatureSets LoadSimilarDependencyFeatures(
            VpbSqlite3.Connection conn, SimilarPackageTable packages)
        {
            var featureIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var perPackage = new List<int>[packages.Count];

            using (var st = conn.Prepare("SELECT src_uid, dep_uid FROM pkg_dep"))
            {
                while (st.Step() == VpbSqlite3.SqliteRow)
                {
                    int src;
                    if (!packages.IdByUid.TryGetValue(st.ColumnText(0), out src)) continue;
                    string dep = st.ColumnText(1);
                    if (string.IsNullOrEmpty(dep)) continue;

                    int feature = InternSimilarKey(featureIds, dep);
                    List<int> list = perPackage[src];
                    if (list == null) { list = new List<int>(8); perPackage[src] = list; }
                    list.Add(feature);
                }
            }

            return FinalizeSimilarFeatureSets(perPackage, featureIds.Count);
        }

        private static SimilarFeatureSets LoadSimilarHubTagFeatures(
            VpbSqlite3.Connection conn, SimilarPackageTable packages)
        {
            var featureIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var perPackage = new List<int>[packages.Count];

            try
            {
                using (var st = conn.Prepare(
                    "SELECT l.pkg_uid, t.ns, t.tag FROM datapack_link l " +
                    "JOIN datapack_tag t ON t.pack_id = l.pack_id AND t.entry_id = l.entry_id"))
                {
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        int src;
                        if (!packages.IdByUid.TryGetValue(st.ColumnText(0), out src)) continue;
                        string ns = st.ColumnText(1);
                        string tag = st.ColumnText(2);
                        if (string.IsNullOrEmpty(tag)) continue;

                        int feature = InternSimilarKey(featureIds, ns + ":" + tag);
                        List<int> list = perPackage[src];
                        if (list == null) { list = new List<int>(8); perPackage[src] = list; }
                        list.Add(feature);
                    }
                }
            }
            catch
            {
                return FinalizeSimilarFeatureSets(perPackage, featureIds.Count);
            }

            return FinalizeSimilarFeatureSets(perPackage, featureIds.Count);
        }

        private static SimilarFeatureSets FinalizeSimilarFeatureSets(List<int>[] perPackage, int featureCount)
        {
            var sets = new SimilarFeatureSets();
            int n = perPackage.Length;
            sets.FeaturesByPackage = new int[n][];
            sets.Norm = new float[n];

            var postingCounts = new int[featureCount];
            for (int i = 0; i < n; i++)
            {
                List<int> list = perPackage[i];
                if (list == null || list.Count == 0) continue;

                int[] features = DistinctSortedFeatureArray(list);
                sets.FeaturesByPackage[i] = features;
                for (int f = 0; f < features.Length; f++) postingCounts[features[f]]++;
            }

            sets.PostingsByFeature = new int[featureCount][];
            for (int f = 0; f < featureCount; f++)
            {
                int c = postingCounts[f];
                sets.PostingsByFeature[f] = c > 0 ? new int[c] : null;
                postingCounts[f] = 0;
            }
            for (int i = 0; i < n; i++)
            {
                int[] features = sets.FeaturesByPackage[i];
                if (features == null) continue;
                for (int f = 0; f < features.Length; f++)
                {
                    int feature = features[f];
                    sets.PostingsByFeature[feature][postingCounts[feature]++] = i;
                }
            }

            int documentCount = 0;
            for (int i = 0; i < n; i++) if (sets.FeaturesByPackage[i] != null) documentCount++;
            sets.DocumentCount = documentCount;
            sets.CommonFeaturePostingCap = Math.Max(8, (int)(documentCount * SimilarCommonFeatureFraction));

            sets.Weight = new float[featureCount];
            for (int f = 0; f < featureCount; f++)
            {
                int df = sets.PostingsByFeature[f] != null ? sets.PostingsByFeature[f].Length : 0;
                sets.Weight[f] = (float)Math.Log((double)Math.Max(1, documentCount) / (1.0 + df));
            }

            for (int i = 0; i < n; i++)
            {
                int[] features = sets.FeaturesByPackage[i];
                if (features == null) continue;
                double acc = 0.0;
                for (int f = 0; f < features.Length; f++)
                {
                    float w = sets.Weight[features[f]];
                    acc += (double)w * w;
                }
                sets.Norm[i] = acc > 0.0 ? (float)Math.Sqrt(acc) : 0f;
            }
            return sets;
        }

        private static int[] DistinctSortedFeatureArray(List<int> list)
        {
            list.Sort();
            int write = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0 && list[i] == list[i - 1]) continue;
                list[write++] = list[i];
            }
            var result = new int[write];
            for (int i = 0; i < write; i++) result[i] = list[i];
            return result;
        }
    }
}
