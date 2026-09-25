using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static int _insightsRevision;
        private static bool _insightsHooked;
        private int _insightsSeenRevision = -1;

        internal static void EnsureInsightsHooked()
        {
            if (_insightsHooked) return;
            _insightsHooked = true;
            VpbPackageInsightStore.Changed += () => { _insightsRevision++; };
        }

        private void InsightsPumpMainThread()
        {
            InsightsFollowSelectionIfOpen();

            int rev = _insightsRevision;
            if (rev == _insightsSeenRevision)
            {
                if (VpbPackageInsightScanner.IsRunning) RefreshInsightsProgressChrome();
                return;
            }
            _insightsSeenRevision = rev;
            _insightFileTermCache = null;
            _insightFileTermCacheFor = null;
            try { FileManager.InvalidateAllMissingDepsCounts(); } catch { }
            try { RefreshInsightsFloatIfOpen(); } catch { }
            try { RefreshInsightsProgressChrome(); } catch { }
            try { DetailStripRefresh(); } catch { }
        }

        private string _insightsFollowedSelectionUid = "";

        private void InsightsFollowSelectionIfOpen()
        {
            if (!IsInsightsFloatOpen()) return;
            if (_insightsTab != InsightsFloatTab.Package) return;
            if (_insightsFloatCollapsed) return;

            string uid = InsightsResolveFocusUid();
            if (string.IsNullOrEmpty(uid)) return;
            if (string.Equals(uid, _insightsFollowedSelectionUid, StringComparison.OrdinalIgnoreCase)) return;

            _insightsFollowedSelectionUid = uid;
            if (string.Equals(uid, _insightsFocusUid, StringComparison.OrdinalIgnoreCase)) return;
            _insightsFocusUid = uid;
            RebuildInsightsFloatBody();
        }

        private static bool _insightsInitialized;

        internal void InsightsInitialize()
        {
            EnsureInsightsHooked();
            if (_insightsInitialized) return;
            _insightsInitialized = true;
            try { VpbPackageInsightScanner.LoadCachedIntoStore(); } catch { }
            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.InsightsAutoScan)
                    VpbPackageInsightScanner.StartScan(null, force: false);
            }
            catch { }
        }

        internal void InsightsStartFullScan(bool force)
        {
            EnsureInsightsHooked();
            if (VpbPackageInsightScanner.IsRunning)
            {
                ShowTemporaryStatus(VPBTranslation.T("insights.status.already_running", "Package scan already running."), 2f);
                return;
            }
            if (!VpbPackageInsightScanner.StartScan(null, force))
            {
                ShowTemporaryStatus(VPBTranslation.T("insights.status.nothing_to_scan", "No packages to scan."), 2.5f);
                return;
            }
            ShowTemporaryStatus(VPBTranslation.T("insights.status.started", "Scanning packages in the background…"), 2.5f);
        }

        internal void InsightsCancelScan()
        {
            VpbPackageInsightScanner.RequestCancel();
            ShowTemporaryStatus(VPBTranslation.T("insights.status.cancelling", "Stopping package scan…"), 2f);
        }

        internal void InsightsRescanSelection()
        {
            EnsureInsightsHooked();
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectSelectedPackageUidsForInsights(uids);
            if (uids.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("insights.status.no_package_selected", "Select a package first."), 2.5f);
                return;
            }
            if (!VpbPackageInsightScanner.StartScan(uids, force: true))
            {
                ShowTemporaryStatus(VPBTranslation.T("insights.status.already_running", "Package scan already running."), 2f);
                return;
            }
            ShowTemporaryStatus(string.Format(
                VPBTranslation.T("insights.status.rescan_fmt", "Rescanning {0} package(s)…"), uids.Count), 2.5f);
        }

        private void CollectSelectedPackageUidsForInsights(HashSet<string> into)
        {
            if (into == null) return;
            if (selectedFiles != null)
            {
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    string uid = VpbPackageInsightStore.ResolvePackageUid(selectedFiles[i]);
                    if (!string.IsNullOrEmpty(uid)) into.Add(uid);
                }
            }
            if (into.Count == 0)
            {
                string uid = VpbPackageInsightStore.ResolvePackageUid(_detailStripBoundFile);
                if (!string.IsNullOrEmpty(uid)) into.Add(uid);
            }
        }

        internal string InsightsResolveFocusUid()
        {
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectSelectedPackageUidsForInsights(uids);
            foreach (string uid in uids) return uid;
            return "";
        }

        private Dictionary<string, HashSet<string>> _insightFileTermCache;
        private string _insightFileTermCacheFor;

        private bool MatchesInsightSearchBranch(FileEntry file, GallerySearchBranch br)
        {
            if (br == null) return true;
            bool wantsStatus = (br.Status & GallerySearchQuery.InsightStatusMask) != 0;
            bool wantsIssueMask = br.IssueMask != PkgIssueFlags.None;
            bool wantsFiles = br.FileTerms != null && br.FileTerms.Count > 0;
            if (!wantsStatus && !wantsIssueMask && !wantsFiles) return true;

            string uid = VpbPackageInsightStore.ResolvePackageUid(file);

            if (wantsStatus || wantsIssueMask)
            {
                if (string.IsNullOrEmpty(uid)) return false;
                PackageInsightRecord rec = VpbPackageInsightStore.Get(uid);
                if (rec == null) return false;

                if (wantsIssueMask && (rec.Issues & br.IssueMask) != br.IssueMask) return false;

                if (br.HasFlag(GallerySearchQuery.StatusFlags.Issues) && !rec.HasIssues) return false;
                if (br.HasFlag(GallerySearchQuery.StatusFlags.PluginContent) && !rec.HasPluginContent) return false;
                if (br.HasFlag(GallerySearchQuery.StatusFlags.Flagged) && !rec.IsFlagged) return false;
                if (br.HasFlag(GallerySearchQuery.StatusFlags.Undeclared)
                    && (rec.Issues & PkgIssueFlags.UndeclaredDeps) == 0) return false;
                if (br.HasFlag(GallerySearchQuery.StatusFlags.Unreviewed)
                    && !VpbPackageInsightStore.NeedsReview(uid)) return false;
            }

            if (wantsFiles)
            {
                if (string.IsNullOrEmpty(uid)) return false;
                Dictionary<string, HashSet<string>> byTerm = GetInsightFileTermSetsCached();
                for (int i = 0; i < br.FileTerms.Count; i++)
                {
                    string term = br.FileTerms[i];
                    if (string.IsNullOrEmpty(term)) continue;
                    HashSet<string> uids;
                    if (byTerm == null || !byTerm.TryGetValue(term, out uids) || uids == null) return false;
                    if (!uids.Contains(uid)) return false;
                }
            }

            return true;
        }

        private Dictionary<string, HashSet<string>> GetInsightFileTermSetsCached()
        {
            string key = nameFilter ?? "";
            if (_insightFileTermCache != null
                && string.Equals(_insightFileTermCacheFor, key, StringComparison.Ordinal))
                return _insightFileTermCache;

            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            GallerySearchQuery q = nameFilterQuery;
            if (q != null && q.FileTerms.Count > 0)
            {
                for (int i = 0; i < q.FileTerms.Count; i++)
                {
                    string term = q.FileTerms[i];
                    if (string.IsNullOrEmpty(term) || map.ContainsKey(term)) continue;
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var hits = new List<ContentFileHit>(4096);
                    try { VpbLocalDatabase.TrySearchPackageFiles(term, InsightsFileTermUidLimit, hits, allowIndexBuild: false); }
                    catch { }
                    for (int h = 0; h < hits.Count; h++)
                    {
                        string u = hits[h].PackageUid;
                        if (!string.IsNullOrEmpty(u)) set.Add(u);
                    }
                    map[term] = set;
                }
            }

            _insightFileTermCache = map;
            _insightFileTermCacheFor = key;
            return map;
        }

        private const int InsightsFileTermUidLimit = 20000;

        private static readonly Color InsightsColorClean = new Color(0.62f, 0.76f, 0.62f, 1f);
        private static readonly Color InsightsColorIssue = new Color(0.92f, 0.76f, 0.42f, 1f);
        private static readonly Color InsightsColorFlagged = new Color(0.92f, 0.56f, 0.48f, 1f);
        private static readonly Color InsightsColorUnknown = GalleryUiColorTokens.TextDim;

        private bool InsightsResolveDetailField(FileEntry file, out string value, out Color color, out string tip)
        {
            value = "";
            color = InsightsColorUnknown;
            tip = "";

            if (!VpbPackageInsightStore.HasAnyData) return false;

            string uid = VpbPackageInsightStore.ResolvePackageUid(file);
            if (string.IsNullOrEmpty(uid)) return false;

            PackageInsightRecord rec = VpbPackageInsightStore.Get(uid);
            if (rec == null)
            {
                value = VPBTranslation.T("insights.detail.not_scanned", "Not scanned");
                color = InsightsColorUnknown;
                tip = VPBTranslation.T("insights.detail.not_scanned_tip",
                    "This package has not been scanned yet. Click to open Package Insights and scan it.");
                return true;
            }

            int issueCount = CountFlagBits((int)rec.Issues);
            bool needsReview = VpbPackageInsightStore.NeedsReview(uid);

            if (issueCount == 0 && !rec.HasPluginContent)
            {
                value = VPBTranslation.T("insights.detail.clean", "Clean");
                color = InsightsColorClean;
                tip = VPBTranslation.T("insights.detail.clean_tip",
                    "No integrity findings and no bundled plugin code. Click for the full report.");
                return true;
            }

            var parts = new List<string>(3);
            if (issueCount > 0)
                parts.Add(string.Format(VPBTranslation.T("insights.detail.issues_fmt", "{0} issue(s)"), issueCount));
            if (rec.IsFlagged)
                parts.Add(VPBTranslation.T("insights.detail.flagged_short", "flagged code"));
            else if (needsReview)
                parts.Add(VPBTranslation.T("insights.detail.unreviewed_short", "unreviewed code"));
            else if (rec.HasPluginContent)
                parts.Add(VPBTranslation.T("insights.detail.code_short", "code"));

            value = string.Join(" · ", parts.ToArray());
            color = rec.IsFlagged ? InsightsColorFlagged : (issueCount > 0 ? InsightsColorIssue : InsightsColorUnknown);
            tip = BuildInsightsSummaryTooltip(rec, needsReview);
            return true;
        }

        private static string BuildInsightsSummaryTooltip(PackageInsightRecord rec, bool needsReview)
        {
            var sb = new System.Text.StringBuilder(256);
            for (int i = 0; i < VpbInsightLabels.AllIssues.Length; i++)
            {
                PkgIssueFlags f = VpbInsightLabels.AllIssues[i];
                if ((rec.Issues & f) == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("• ").Append(VpbInsightLabels.Issue(f));
            }
            for (int i = 0; i < VpbInsightLabels.AllRisks.Length; i++)
            {
                PkgRiskFlags f = VpbInsightLabels.AllRisks[i];
                if ((rec.Risk & f) == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("• ").Append(VpbInsightLabels.Risk(f));
            }
            if (needsReview)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(VPBTranslation.T("insights.detail.unreviewed_tip", "Not yet marked reviewed."));
            }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(VPBTranslation.T("insights.detail.open_tip", "Click for the full report."));
            return sb.ToString();
        }

        private static int CountFlagBits(int v)
        {
            int n = 0;
            while (v != 0)
            {
                v &= v - 1;
                n++;
            }
            return n;
        }

        private string _insightsPendingLaunchUid;

        internal bool InsightsShouldBlockLaunchForReview(FileEntry file)
        {
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.InsightsConfirmUnreviewedPlugins) return false;
                string uid = VpbPackageInsightStore.ResolvePackageUid(file);
                if (string.IsNullOrEmpty(uid)) return false;
                if (string.Equals(uid, _insightsPendingLaunchUid, StringComparison.OrdinalIgnoreCase)) return false;
                if (!VpbPackageInsightStore.NeedsReview(uid)) return false;
                _insightsPendingLaunchUid = uid;
                ShowInsightsFloat(uid, InsightsFloatTab.Package);
                ShowTemporaryStatus(VPBTranslation.T("insights.status.review_prompt",
                    "This package bundles plugin code you have not reviewed."), 4f);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal void InsightsClearPendingLaunchGate()
        {
            _insightsPendingLaunchUid = null;
        }
    }
}
