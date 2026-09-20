using System;
using System.Collections.Generic;

namespace VPB
{
    internal static class VpbPackageInsightStore
    {
        private static readonly object WriteLock = new object();

        private static volatile Dictionary<string, string> _reviewed =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static volatile Dictionary<string, PackageInsightRecord> _map =
            new Dictionary<string, PackageInsightRecord>(StringComparer.OrdinalIgnoreCase);

        private static volatile InsightRollup _rollup;
        private static volatile bool _rollupDirty = true;

        internal static bool HasAnyData { get { return _map.Count > 0; } }

        internal static event Action Changed;

        internal static PackageInsightRecord Get(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            Dictionary<string, PackageInsightRecord> map = _map;
            PackageInsightRecord rec;
            return map.TryGetValue(uid, out rec) ? rec : null;
        }

        internal static string ResolvePackageUid(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                VarFileEntry vfe = file as VarFileEntry;
                if (vfe != null) return vfe.GetRowPackageUid() ?? "";

                PackageListEntry ple = file as PackageListEntry;
                if (ple != null) return ple.GetPackageUidForGalleryUserTags() ?? "";
            }
            catch { }
            return "";
        }

        internal static InsightRollup Rollup
        {
            get
            {
                if (!_rollupDirty && _rollup != null) return _rollup;
                lock (WriteLock)
                {
                    if (!_rollupDirty && _rollup != null) return _rollup;
                    _rollup = BuildRollupLocked();
                    _rollupDirty = false;
                    return _rollup;
                }
            }
        }

        internal static bool IsReviewed(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            PackageInsightRecord rec = Get(uid);
            if (rec == null) return false;
            Dictionary<string, string> reviewed = _reviewed;
            string have;
            if (!reviewed.TryGetValue(uid, out have)) return false;
            return string.Equals(have, rec.BuildReviewSignature(), StringComparison.Ordinal);
        }

        internal static bool NeedsReview(string uid)
        {
            PackageInsightRecord rec = Get(uid);
            if (rec == null || !rec.HasPluginContent) return false;
            return !IsReviewed(uid);
        }

        internal static void SetReviewed(string uid, bool reviewed)
        {
            if (string.IsNullOrEmpty(uid)) return;
            PackageInsightRecord rec = Get(uid);
            if (rec == null) return;
            string signature = rec.BuildReviewSignature();
            lock (WriteLock)
            {
                var next = new Dictionary<string, string>(_reviewed, StringComparer.OrdinalIgnoreCase);
                if (reviewed) next[uid] = signature;
                else next.Remove(uid);
                _reviewed = next;
            }
            try { VpbLocalDatabase.TrySaveInsightReview(uid, reviewed ? signature : null); } catch { }
            RecomputeRollup();
            RaiseChanged();
        }

        internal static void SeedReviewed(Dictionary<string, string> rows)
        {
            if (rows == null) return;
            var next = new Dictionary<string, string>(rows.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in rows)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                next[kv.Key] = kv.Value ?? "";
            }
            lock (WriteLock)
            {
                _reviewed = next;
            }
            RecomputeRollup();
        }

        internal static void PublishAll(Dictionary<string, PackageInsightRecord> records)
        {
            if (records == null) return;
            lock (WriteLock)
            {
                _map = records;
            }
            RecomputeRollup();
            RaiseChanged();
        }

        internal static void PublishBatch(List<PackageInsightRecord> batch)
        {
            if (batch == null || batch.Count == 0) return;
            lock (WriteLock)
            {
                var next = new Dictionary<string, PackageInsightRecord>(_map, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < batch.Count; i++)
                {
                    PackageInsightRecord r = batch[i];
                    if (r == null || string.IsNullOrEmpty(r.Uid)) continue;
                    next[r.Uid] = r;
                }
                _map = next;
            }
            RecomputeRollup();
            RaiseChanged();
        }

        internal static void RemoveMissingPackages(HashSet<string> livePackageUids)
        {
            if (livePackageUids == null || livePackageUids.Count == 0) return;
            lock (WriteLock)
            {
                Dictionary<string, PackageInsightRecord> cur = _map;
                List<string> drop = null;
                foreach (KeyValuePair<string, PackageInsightRecord> kv in cur)
                {
                    if (livePackageUids.Contains(kv.Key)) continue;
                    if (drop == null) drop = new List<string>(16);
                    drop.Add(kv.Key);
                }
                if (drop == null) return;
                var next = new Dictionary<string, PackageInsightRecord>(cur, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < drop.Count; i++) next.Remove(drop[i]);
                _map = next;
            }
            RecomputeRollup();
            RaiseChanged();
        }

        private static void RecomputeRollup()
        {
            _rollupDirty = true;
        }

        private static InsightRollup BuildRollupLocked()
        {
            var roll = new InsightRollup();
            Dictionary<string, PackageInsightRecord> map = _map;
            Dictionary<string, string> reviewed = _reviewed;
            PkgIssueFlags[] issueFlags = VpbInsightLabels.AllIssues;
            PkgRiskFlags[] riskFlags = VpbInsightLabels.AllRisks;

            foreach (KeyValuePair<string, PackageInsightRecord> kv in map)
            {
                PackageInsightRecord r = kv.Value;
                if (r == null) continue;
                roll.Scanned++;
                if (r.HasIssues) roll.WithIssues++;
                if (r.HasPluginContent) roll.WithPluginContent++;
                if (r.IsFlagged) roll.Flagged++;
                if (r.HasPluginContent && !IsReviewedIn(reviewed, r)) roll.Unreviewed++;

                for (int i = 0; i < issueFlags.Length; i++)
                {
                    if ((r.Issues & issueFlags[i]) != 0) roll.IssueCounts[i]++;
                }
                for (int i = 0; i < riskFlags.Length; i++)
                {
                    if ((r.Risk & riskFlags[i]) != 0) roll.RiskCounts[i]++;
                }
            }
            return roll;
        }

        private static bool IsReviewedIn(Dictionary<string, string> reviewed, PackageInsightRecord rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.Uid)) return false;
            string have;
            if (!reviewed.TryGetValue(rec.Uid, out have)) return false;
            return string.Equals(have, rec.BuildReviewSignature(), StringComparison.Ordinal);
        }

        private static void RaiseChanged()
        {
            Action h = Changed;
            if (h == null) return;
            try { h(); } catch { }
        }
        
        internal static void AppendUnsatisfiedUndeclared(VarPackage pkg, List<string> into)
        {
            if (pkg == null || into == null) return;
            PackageInsightRecord rec = Get(pkg.Uid);
            if (rec == null || rec.UndeclaredDeps == null) return;
            string[] deps = rec.UndeclaredDeps;
            for (int i = 0; i < deps.Length; i++)
            {
                string dep = deps[i];
                if (string.IsNullOrEmpty(dep)) continue;
                if (ContainsOrdinalIgnoreCase(into, dep)) continue;
                bool satisfied;
                try { satisfied = FileManager.IsDependencySatisfiedByInstalled(dep, pkg); }
                catch { satisfied = true; }
                if (!satisfied) into.Add(dep);
            }
        }

        internal static int CountUnsatisfiedUndeclared(VarPackage pkg, ICollection<string> alreadyCounted)
        {
            if (pkg == null) return 0;
            PackageInsightRecord rec = Get(pkg.Uid);
            if (rec == null || rec.UndeclaredDeps == null) return 0;
            int n = 0;
            string[] deps = rec.UndeclaredDeps;
            for (int i = 0; i < deps.Length; i++)
            {
                string dep = deps[i];
                if (string.IsNullOrEmpty(dep)) continue;
                if (alreadyCounted != null && alreadyCounted.Contains(dep)) continue;
                bool satisfied;
                try { satisfied = FileManager.IsDependencySatisfiedByInstalled(dep, pkg); }
                catch { satisfied = true; }
                if (!satisfied) n++;
            }
            return n;
        }

        private static bool ContainsOrdinalIgnoreCase(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
