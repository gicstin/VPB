using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private readonly List<SimilarNeighbour> _similarNeighbourScratch = new List<SimilarNeighbour>(32);
        private readonly Dictionary<string, string> _similarReasonByPackageUid =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _similarSeedLabel;

        private bool IsSimilarFilterActive
        {
            get { return currentPackageFilterMode == PackageFilterMode.Similar; }
        }

        internal static bool EntrySupportsSimilarFilter(FileEntry file)
        {
            if (file == null) return false;
            if (file is VirtualFileEntry) return false;
            return !string.IsNullOrEmpty(TryGetPackageUidForEntry(file));
        }

        private void ApplySimilarFilter(FileEntry file)
        {
            if (file == null) return;

            string seedUid = TryGetPackageUidForEntry(file);
            if (string.IsNullOrEmpty(seedUid))
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.similar.not_a_package",
                    "Similar items need a .var package — this item is a loose file."));
                return;
            }

            if (!VpbLocalDatabase.TryReadSimilarNeighbours(seedUid, _similarNeighbourScratch))
            {
                ShowTemporaryStatus(BuildSimilarUnavailableMessage(seedUid));
                return;
            }

            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _similarReasonByPackageUid.Clear();
            for (int i = 0; i < _similarNeighbourScratch.Count; i++)
            {
                SimilarNeighbour n = _similarNeighbourScratch[i];
                if (string.IsNullOrEmpty(n.Uid)) continue;
                uids.Add(n.Uid);
                _similarReasonByPackageUid[n.Uid] = BuildSimilarReasonText(n);
            }
            if (uids.Count == 0)
            {
                ShowTemporaryStatus(BuildSimilarUnavailableMessage(seedUid));
                return;
            }

            EnsureFilterBaseCaptured();

            _similarSeedLabel = BuildSimilarSeedLabel(file, seedUid);

            List<FileEntry> filtered;
            if (PackageFilterUsesPackageListRows())
            {
                filtered = BuildPackageListEntriesForUids(uids);
            }
            else
            {
                filtered = new List<FileEntry>();
                AddVarFileEntriesWithPackageInUidSet(
                    filtered, file, ResolveSimilarFilterSourceList(), uids);
            }

            OrderEntriesBySimilarRank(filtered);
            currentPackageFilterCount = filtered.Count;
            currentPackageFilterMasterUid = seedUid;
            currentPackageFilterMode = PackageFilterMode.Similar;

            if (filtered.Count == 0)
            {
                NavigateBack();
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.similar.none_in_view",
                    "Nothing similar in this category."));
                return;
            }

            ApplyFilteredList(filtered, SimilarFilterDescription());
        }

        private IList<FileEntry> ResolveSimilarFilterSourceList()
        {
            if (_topSearchBaseIsClean && topSearchBaseFiles != null && topSearchBaseFiles.Count > 0)
                return topSearchBaseFiles;
            return currentFilteredFiles;
        }

        private void OrderEntriesBySimilarRank(List<FileEntry> entries)
        {
            if (entries == null || entries.Count < 2) return;

            var rankByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _similarNeighbourScratch.Count; i++)
            {
                string uid = _similarNeighbourScratch[i].Uid;
                if (!string.IsNullOrEmpty(uid) && !rankByUid.ContainsKey(uid)) rankByUid[uid] = i;
            }

            entries.Sort((a, b) =>
            {
                int ra = SimilarRankForEntry(rankByUid, a);
                int rb = SimilarRankForEntry(rankByUid, b);
                if (ra != rb) return ra.CompareTo(rb);
                return string.Compare(
                    a != null ? a.Name : "", b != null ? b.Name : "", StringComparison.OrdinalIgnoreCase);
            });
        }

        private static int SimilarRankForEntry(Dictionary<string, int> rankByUid, FileEntry entry)
        {
            if (entry == null) return int.MaxValue;
            string uid = TryGetPackageUidForEntry(entry);
            int rank;
            if (!string.IsNullOrEmpty(uid) && rankByUid.TryGetValue(uid, out rank)) return rank;
            return int.MaxValue;
        }

        private string SimilarFilterDescription()
        {
            string label = string.IsNullOrEmpty(_similarSeedLabel) ? "item" : _similarSeedLabel;
            return string.Format(
                VPBTranslation.T("gallery.similar.description", "Similar to {0}"), label);
        }

        internal string SimilarFilterChipLabel()
        {
            string label = string.IsNullOrEmpty(_similarSeedLabel) ? "item" : _similarSeedLabel;
            return string.Format(
                VPBTranslation.T("gallery.filter_chip.similar", "Similar to {0}"),
                TruncateFilterChipLabel(label, 22));
        }

        private static string BuildSimilarSeedLabel(FileEntry file, string seedUid)
        {
            string name = file != null ? file.Name : null;
            if (!string.IsNullOrEmpty(name)) return name;
            return seedUid ?? "";
        }

        private static string BuildSimilarReasonText(SimilarNeighbour n)
        {
            if (n.SharedTags > 0 && n.SharedDeps > 0)
                return string.Format(
                    VPBTranslation.T("gallery.similar.reason_both", "{0} shared tags, {1} shared deps"),
                    n.SharedTags, n.SharedDeps);
            if (n.SharedTags > 0)
                return string.Format(
                    VPBTranslation.T("gallery.similar.reason_tags", "{0} shared Hub tags"), n.SharedTags);
            if (n.SharedDeps > 0)
                return string.Format(
                    VPBTranslation.T("gallery.similar.reason_deps", "{0} shared dependencies"), n.SharedDeps);
            return VPBTranslation.T("gallery.similar.reason_unknown", "related");
        }

        private string SimilarReasonForEntry(FileEntry entry)
        {
            if (!IsSimilarFilterActive || entry == null) return null;
            if (_similarReasonByPackageUid.Count == 0) return null;

            string uid = TryGetPackageUidForEntry(entry);
            if (string.IsNullOrEmpty(uid)) return null;

            string reason;
            return _similarReasonByPackageUid.TryGetValue(uid, out reason) ? reason : null;
        }

        private static string BuildSimilarUnavailableMessage(string seedUid)
        {
            if (VpbSimilarIndexBuilder.IsRunning)
                return VPBTranslation.T(
                    "gallery.similar.building",
                    "Still working out what's similar — try again in a moment.");

            SimilarIndexState state = SimilarIndexState.Missing;
            try { state = VpbLocalDatabase.GetSimilarIndexState(); } catch { }
            if (state != SimilarIndexState.Ready)
            {
                try { VpbSimilarIndexBuilder.EnsureBuiltInBackground(); } catch { }
                return VPBTranslation.T(
                    "gallery.similar.building",
                    "Still working out what's similar — try again in a moment.");
            }

            return VPBTranslation.T(
                "gallery.similar.no_signal",
                "Not enough data to compare this one — it has no Hub tags and too few dependencies.");
        }

        private const int SimilarRandomBiasPercent = 66;
        private const int SimilarRandomBiasMinPool = 3;
        private readonly List<FileEntry> _similarRandomBiasPool = new List<FileEntry>(64);

        private List<FileEntry> ApplySimilarRandomBias(List<FileEntry> pool)
        {
            if (pool == null || pool.Count <= SimilarRandomBiasMinPool) return pool;
            if (IsSimilarFilterActive) return pool;
            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryRandomPrefersSimilar) return pool;
            if (VpbRandom.Next(0, 100) >= SimilarRandomBiasPercent) return pool;

            FileEntry seed = (selectedFiles != null && selectedFiles.Count == 1) ? selectedFiles[0] : null;
            string seedUid = seed != null ? TryGetPackageUidForEntry(seed) : null;
            if (string.IsNullOrEmpty(seedUid)) return pool;

            if (!VpbLocalDatabase.TryReadSimilarNeighbours(seedUid, _similarNeighbourScratch)) return pool;

            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _similarNeighbourScratch.Count; i++)
            {
                string uid = _similarNeighbourScratch[i].Uid;
                if (!string.IsNullOrEmpty(uid)) uids.Add(uid);
            }
            if (uids.Count == 0) return pool;

            _similarRandomBiasPool.Clear();
            for (int i = 0; i < pool.Count; i++)
            {
                FileEntry e = pool[i];
                if (e == null || e == seed) continue;
                string uid = TryGetPackageUidForEntry(e);
                if (!string.IsNullOrEmpty(uid) && uids.Contains(uid)) _similarRandomBiasPool.Add(e);
            }

            return _similarRandomBiasPool.Count >= SimilarRandomBiasMinPool ? _similarRandomBiasPool : pool;
        }

        private string _similarCountCacheUid;
        private int _similarCountCacheValue = -1;
        private readonly List<SimilarNeighbour> _similarCountScratch = new List<SimilarNeighbour>(32);

        private int SimilarNeighbourCountForEntry(FileEntry entry)
        {
            string uid = entry != null ? TryGetPackageUidForEntry(entry) : null;
            if (string.IsNullOrEmpty(uid)) return 0;

            if (_similarCountCacheValue >= 0
                && string.Equals(_similarCountCacheUid, uid, StringComparison.OrdinalIgnoreCase))
                return _similarCountCacheValue;

            int count = 0;
            try
            {
                if (VpbLocalDatabase.TryReadSimilarNeighbours(uid, _similarCountScratch))
                    count = _similarCountScratch.Count;
            }
            catch { count = 0; }

            _similarCountCacheUid = uid;
            _similarCountCacheValue = count;
            return count;
        }

        private void DetailStripOnSimilarClick()
        {
            FileEntry f = _detailStripBoundFile;
            if (f == null) return;
            try { ApplySimilarFilter(f); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip similar: " + ex.Message); }
        }

        private void ClearSimilarFilterState()
        {
            _similarCountCacheUid = null;
            _similarCountCacheValue = -1;
            _similarReasonByPackageUid.Clear();
            _similarNeighbourScratch.Clear();
            _similarSeedLabel = null;
        }
    }
}
