using System;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        private sealed class SimilarSparseAccumulator
        {
            internal readonly float[] Weight;
            internal readonly int[] Overlap;
            internal readonly int[] Touched;
            internal int TouchedCount;

            internal SimilarSparseAccumulator(int packageCount)
            {
                Weight = new float[packageCount];
                Overlap = new int[packageCount];
                Touched = new int[packageCount];
            }

            internal void Reset()
            {
                for (int i = 0; i < TouchedCount; i++)
                {
                    int id = Touched[i];
                    Weight[id] = 0f;
                    Overlap[id] = 0;
                }
                TouchedCount = 0;
            }
        }

        private sealed class SimilarTopList
        {
            internal readonly int[] Id;
            internal readonly float[] Score;
            internal readonly int[] Overlap;
            internal readonly int Capacity;
            internal int Count;

            internal SimilarTopList(int capacity)
            {
                Capacity = capacity;
                Id = new int[capacity];
                Score = new float[capacity];
                Overlap = new int[capacity];
            }

            internal void Clear() { Count = 0; }

            internal void Offer(int id, float score, int overlap)
            {
                if (Count == Capacity && score <= Score[Count - 1]) return;

                int at = Count < Capacity ? Count : Capacity - 1;
                while (at > 0 && Score[at - 1] < score)
                {
                    Id[at] = Id[at - 1];
                    Score[at] = Score[at - 1];
                    Overlap[at] = Overlap[at - 1];
                    at--;
                }
                Id[at] = id;
                Score[at] = score;
                Overlap[at] = overlap;
                if (Count < Capacity) Count++;
            }
        }

        private struct SimilarFusedCandidate
        {
            public int Id;
            public float Score;
            public float DepScore;
            public float TagScore;
            public int SharedDeps;
            public int SharedTags;
        }

        private static bool WriteSimilarIndex(
            VpbSqlite3.Connection conn,
            SimilarPackageTable packages,
            SimilarFeatureSets depSets,
            SimilarFeatureSets tagSets,
            string signature,
            Func<bool> abortRequested,
            out int seedCount,
            out int rowCount)
        {
            seedCount = 0;
            rowCount = 0;

            int n = packages.Count;
            var depAccumulator = new SimilarSparseAccumulator(n);
            var tagAccumulator = new SimilarSparseAccumulator(n);
            var depTop = new SimilarTopList(SimilarChannelTopK);
            var tagTop = new SimilarTopList(SimilarChannelTopK);
            var fused = new SimilarFusedCandidate[SimilarChannelTopK * 2];
            var kept = new SimilarFusedCandidate[SimilarStoredTopN];
            var seenFamily = new int[n];
            int familyStamp = 0;

            bool committed = false;
            conn.ExecUtf8("BEGIN IMMEDIATE;");
            try
            {
                conn.ExecUtf8("DELETE FROM pkg_similar;");
                using (var ins = conn.Prepare(
                    "INSERT OR REPLACE INTO pkg_similar(src_uid,rank,dst_uid,score10k,shared_deps,shared_tags) " +
                    "VALUES(?,?,?,?,?,?)"))
                {
                    for (int seed = 0; seed < n; seed++)
                    {
                        if ((seed & 255) == 0 && IsSimilarBuildAborted(abortRequested))
                        {
                            conn.ExecUtf8("ROLLBACK;");
                            return false;
                        }
                        if (!packages.IsNewest[seed]) continue;

                        int[] seedDepFeatures = depSets.FeaturesByPackage[seed];
                        int[] seedTagFeatures = tagSets.FeaturesByPackage[seed];
                        bool hasDepChannel = seedDepFeatures != null && depSets.Norm[seed] > 0f;
                        bool hasTagChannel = seedTagFeatures != null && tagSets.Norm[seed] > 0f;
                        if (!hasDepChannel && !hasTagChannel) continue;

                        familyStamp++;
                        ScoreSimilarChannel(packages, depSets, seed, depAccumulator, depTop);
                        ScoreSimilarChannel(packages, tagSets, seed, tagAccumulator, tagTop);

                        float depWeight = hasDepChannel ? SimilarDepChannelWeight : 0f;
                        float tagWeight = hasTagChannel ? SimilarTagChannelWeight : 0f;
                        float totalWeight = depWeight + tagWeight;
                        if (totalWeight <= 0f) continue;
                        depWeight /= totalWeight;
                        tagWeight /= totalWeight;

                        int fusedCount = FuseSimilarChannels(depTop, tagTop, depWeight, tagWeight, fused);
                        if (fusedCount == 0) continue;

                        SortSimilarCandidatesDescending(fused, fusedCount);
                        fusedCount = DedupeSimilarCandidatesByFamily(
                            packages, fused, fusedCount, seenFamily, familyStamp);
                        int keptCount = SelectSimilarCandidates(packages, seed, fused, fusedCount, kept);
                        if (keptCount == 0) continue;

                        string srcUid = packages.Uid[seed];
                        for (int r = 0; r < keptCount; r++)
                        {
                            ins.Reset();
                            ins.BindText(1, srcUid);
                            ins.BindInt64(2, r);
                            ins.BindText(3, packages.Uid[kept[r].Id]);
                            ins.BindInt64(4, (long)(kept[r].Score * 10000f));
                            ins.BindInt64(5, kept[r].SharedDeps);
                            ins.BindInt64(6, kept[r].SharedTags);
                            ins.Step();
                            rowCount++;
                        }
                        seedCount++;
                    }
                }

                MetaSet(conn, SimilarSigMetaKey, signature);
                conn.ExecUtf8("COMMIT;");
                committed = true;
            }
            finally
            {
                if (!committed) { try { conn.ExecUtf8("ROLLBACK;"); } catch { } }
            }
            return committed;
        }

        private static void ScoreSimilarChannel(
            SimilarPackageTable packages,
            SimilarFeatureSets sets,
            int seed,
            SimilarSparseAccumulator accumulator,
            SimilarTopList top)
        {
            accumulator.Reset();
            top.Clear();

            int[] features = sets.FeaturesByPackage[seed];
            float seedNorm = sets.Norm[seed];
            if (features == null || seedNorm <= 0f) return;

            int cap = sets.CommonFeaturePostingCap;
            for (int f = 0; f < features.Length; f++)
            {
                int feature = features[f];
                int[] postings = sets.PostingsByFeature[feature];
                if (postings == null || postings.Length > cap) continue;

                float w = sets.Weight[feature];
                float contribution = w * w;
                for (int p = 0; p < postings.Length; p++)
                {
                    int candidate = postings[p];
                    if (candidate == seed) continue;
                    if (accumulator.Overlap[candidate] == 0)
                        accumulator.Touched[accumulator.TouchedCount++] = candidate;
                    accumulator.Overlap[candidate]++;
                    accumulator.Weight[candidate] += contribution;
                }
            }

            int seedFamily = packages.FamilyId[seed];
            for (int t = 0; t < accumulator.TouchedCount; t++)
            {
                int candidate = accumulator.Touched[t];
                if (accumulator.Overlap[candidate] < SimilarMinSharedFeatures) continue;
                if (!packages.IsNewest[candidate]) continue;
                if (seedFamily >= 0 && packages.FamilyId[candidate] == seedFamily) continue;

                float candidateNorm = sets.Norm[candidate];
                if (candidateNorm <= 0f) continue;

                float cosine = accumulator.Weight[candidate] / (seedNorm * candidateNorm);
                if (cosine <= 0f) continue;
                top.Offer(candidate, cosine, accumulator.Overlap[candidate]);
            }
        }

        private static int FuseSimilarChannels(
            SimilarTopList depTop,
            SimilarTopList tagTop,
            float depWeight,
            float tagWeight,
            SimilarFusedCandidate[] fused)
        {
            int count = 0;
            for (int i = 0; i < depTop.Count; i++)
            {
                fused[count].Id = depTop.Id[i];
                fused[count].DepScore = depTop.Score[i];
                fused[count].TagScore = 0f;
                fused[count].SharedDeps = depTop.Overlap[i];
                fused[count].SharedTags = 0;
                count++;
            }
            for (int i = 0; i < tagTop.Count; i++)
            {
                int id = tagTop.Id[i];
                int at = -1;
                for (int j = 0; j < count; j++) { if (fused[j].Id == id) { at = j; break; } }
                if (at < 0)
                {
                    at = count++;
                    fused[at].Id = id;
                    fused[at].DepScore = 0f;
                    fused[at].SharedDeps = 0;
                }
                fused[at].TagScore = tagTop.Score[i];
                fused[at].SharedTags = tagTop.Overlap[i];
            }

            int write = 0;
            for (int i = 0; i < count; i++)
            {
                float score = depWeight * fused[i].DepScore + tagWeight * fused[i].TagScore;
                if ((int)(score * 10000f) < SimilarScoreFloor10k) continue;
                fused[write] = fused[i];
                fused[write].Score = score;
                write++;
            }
            return write;
        }

        private static void SortSimilarCandidatesDescending(SimilarFusedCandidate[] items, int count)
        {
            for (int i = 1; i < count; i++)
            {
                SimilarFusedCandidate key = items[i];
                int j = i - 1;
                while (j >= 0 && items[j].Score < key.Score)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = key;
            }
        }

        private static int DedupeSimilarCandidatesByFamily(
            SimilarPackageTable packages,
            SimilarFusedCandidate[] items,
            int count,
            int[] seenFamily,
            int stamp)
        {
            int write = 0;
            for (int i = 0; i < count; i++)
            {
                int family = packages.FamilyId[items[i].Id];
                if (family >= 0)
                {
                    if (seenFamily[family] == stamp) continue;
                    seenFamily[family] = stamp;
                }
                items[write++] = items[i];
            }
            return write;
        }

        private static int SelectSimilarCandidates(
            SimilarPackageTable packages,
            int seed,
            SimilarFusedCandidate[] items,
            int count,
            SimilarFusedCandidate[] kept)
        {
            int ownCreator = packages.CreatorId[seed];
            int sameCreatorBudget = Math.Max(0, SimilarStoredTopN - SimilarCrossCreatorReserve);
            int sameCreatorUsed = 0;
            int keptCount = 0;

            for (int i = 0; i < count && keptCount < SimilarStoredTopN; i++)
            {
                bool sameCreator = ownCreator >= 0 && packages.CreatorId[items[i].Id] == ownCreator;
                if (sameCreator)
                {
                    if (sameCreatorUsed >= sameCreatorBudget) continue;
                    sameCreatorUsed++;
                }
                kept[keptCount++] = items[i];
            }

            if (keptCount < SimilarStoredTopN)
            {
                for (int i = 0; i < count && keptCount < SimilarStoredTopN; i++)
                {
                    bool sameCreator = ownCreator >= 0 && packages.CreatorId[items[i].Id] == ownCreator;
                    if (!sameCreator) continue;

                    bool already = false;
                    for (int k = 0; k < keptCount; k++) { if (kept[k].Id == items[i].Id) { already = true; break; } }
                    if (already) continue;
                    kept[keptCount++] = items[i];
                }
                SortSimilarCandidatesDescending(kept, keptCount);
            }
            return keptCount;
        }
    }
}
