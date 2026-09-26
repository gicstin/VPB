using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void RebuildInsightsFloatBody()
        {
            if (_insightsBodyParent == null) return;
            UI.DestroyAllChildren(_insightsBodyParent);
            SyncInsightsTabChrome();

            float s = _insightsFloatChromeScale > 0f ? _insightsFloatChromeScale : ChromeScale;
            if (s <= 0f) s = 1f;
            var type = new GalleryModalTypography(s);

            switch (_insightsTab)
            {
                case InsightsFloatTab.Package:
                    BuildInsightsPackageBody(type, s);
                    break;
                case InsightsFloatTab.Content:
                    BuildInsightsContentBody(type, s);
                    break;
                default:
                    BuildInsightsOverviewBody(type, s);
                    break;
            }

            SetInsightsFooter(ResolveInsightsFooterHint());
        }

        private string ResolveInsightsFooterHint()
        {
            switch (_insightsTab)
            {
                case InsightsFloatTab.Package:
                    return VPBTranslation.T("insights.footer.package",
                        "Findings describe how a package was built — not a judgement of its author.");
                case InsightsFloatTab.Content:
                    return VPBTranslation.T("insights.footer.content",
                        "Searches the archive index — no package is opened.");
                default:
                    return VPBTranslation.T("insights.footer.overview",
                        "Show filters the gallery to that row. Same atoms work in search: issues, plugins, flagged, unreviewed, issue:<finding>, file:<name>.");
            }
        }

        private void SetInsightsFooter(string text)
        {
            if (_insightsFooterText == null) return;
            _insightsFooterText.text = text ?? "";
        }

        private void BuildInsightsOverviewBody(GalleryModalTypography type, float s)
        {
            InsightRollup roll = VpbPackageInsightStore.Rollup;
            int libraryCount = ResolveInstalledPackageCount();

            if (roll.Scanned == 0)
            {
                AddInsightsEmptyState(
                    VPBTranslation.T("insights.empty.title", "Nothing scanned yet"),
                    string.Format(VPBTranslation.T("insights.empty.body_fmt",
                        "Scan reads each of your {0} packages once and remembers the result, so later scans only look at packages that changed.\n\nIt runs in the background — you can keep browsing."),
                        libraryCount),
                    type, s);
                return;
            }

            AddInsightsSectionHeader(string.Format(
                VPBTranslation.T("insights.overview.scanned_fmt", "Scanned {0} of {1} packages"),
                roll.Scanned, libraryCount), type, s);

            AddInsightsCountRow(
                VPBTranslation.T("insights.overview.any_issue", "Packages with any finding"),
                roll.WithIssues,
                VPBTranslation.T("insights.tip.any_issue", "Every package with at least one integrity finding."),
                "issues", type, s, false,
                roll.WithIssues > 0 ? InsightsColorIssue : InsightsZeroText);

            AddInsightsSectionHeader(VPBTranslation.T("insights.overview.integrity", "Integrity"), type, s, spacedAbove: true);
            int stripe = 0;
            for (int i = 0; i < VpbInsightLabels.AllIssues.Length; i++)
            {
                PkgIssueFlags f = VpbInsightLabels.AllIssues[i];
                int n = roll.Issue(f);
                AddInsightsCountRow(
                    VpbInsightLabels.Issue(f), n, VpbInsightLabels.IssueTip(f),
                    InsightsSearchTokenForIssue(f), type, s, (stripe++ & 1) == 1,
                    n > 0 ? InsightsColorIssue : InsightsZeroText);
            }

            AddInsightsSectionHeader(VPBTranslation.T("insights.overview.plugins", "Plugin content"), type, s, spacedAbove: true);
            stripe = 0;
            for (int i = 0; i < VpbInsightLabels.AllRisks.Length; i++)
            {
                PkgRiskFlags f = VpbInsightLabels.AllRisks[i];
                int n = roll.RiskOf(f);
                AddInsightsCountRow(
                    VpbInsightLabels.Risk(f), n, VpbInsightLabels.RiskTip(f),
                    InsightsSearchTokenForRisk(f), type, s, (stripe++ & 1) == 1,
                    n > 0
                        ? (f == PkgRiskFlags.FlaggedHigh ? InsightsColorFlagged : InsightsColorIssue)
                        : InsightsZeroText);
            }
            AddInsightsCountRow(
                VPBTranslation.T("insights.overview.unreviewed", "Not marked reviewed"),
                roll.Unreviewed,
                VPBTranslation.T("insights.tip.unreviewed",
                    "Packages bundling plugin code that you have not acknowledged. Marking one reviewed remembers that decision until the package changes."),
                "unreviewed", type, s, (stripe++ & 1) == 1,
                roll.Unreviewed > 0 ? InsightsColorIssue : InsightsZeroText);

            AddInsightsSectionHeader(VPBTranslation.T("insights.overview.keywords_hdr", "Script keywords"), type, s, spacedAbove: true);
            AddInsightsNoteRow(string.Format(
                VPBTranslation.T("insights.overview.keywords_fmt",
                    "Using the {0}. Matching is a prompt to read a plugin, never proof of anything — a keyword can be avoided trivially, and most matches are ordinary code."),
                VpbInsightKeywords.SourceLabel), type, s);
            AddInsightsActionRow(
                VPBTranslation.T("insights.action.write_keywords", "Create editable keyword file"),
                VPBTranslation.T("insights.tip.write_keywords",
                    "Writes the built-in list to Saves/PluginData/VPB/insight_keywords.txt so you can edit it. Rescan afterwards to apply."),
                type, s, () =>
                {
                    string path;
                    if (VpbInsightKeywords.TryWriteDefaultUserFile(out path))
                        ShowTemporaryStatus(string.Format(
                            VPBTranslation.T("insights.status.keywords_written", "Keyword list written to {0}"), path), 5f);
                    else
                        ShowTemporaryStatus(VPBTranslation.T("insights.status.keywords_failed",
                            "Could not write the keyword list."), 3f);
                });
            AddInsightsActionRow(
                VPBTranslation.T("insights.action.rescan_all", "Rescan all packages"),
                VPBTranslation.T("insights.tip.rescan_all",
                    "Ignores the cache and reads every package again. Use after changing the keyword list."),
                type, s, () => InsightsStartFullScan(true));
        }

        private static string InsightsSearchTokenForIssue(PkgIssueFlags f)
        {
            string key = GallerySearchQuery.IssueKeyFor(f);
            return string.IsNullOrEmpty(key) ? "issues" : "issue:" + key;
        }

        private static string InsightsSearchTokenForRisk(PkgRiskFlags f)
        {
            switch (f)
            {
                case PkgRiskFlags.FlaggedHigh:
                case PkgRiskFlags.FlaggedLow:
                    return "flagged";
                default:
                    return "plugins";
            }
        }

        private void BuildInsightsPackageBody(GalleryModalTypography type, float s)
        {
            string uid = _insightsFocusUid;
            if (string.IsNullOrEmpty(uid)) uid = InsightsResolveFocusUid();

            if (string.IsNullOrEmpty(uid))
            {
                AddInsightsEmptyState(
                    VPBTranslation.T("insights.package.none_title", "No package selected"),
                    VPBTranslation.T("insights.package.none_body",
                        "Select a package in the gallery — this tab follows your selection."),
                    type, s);
                return;
            }

            _insightsFocusUid = uid;
            PackageInsightRecord rec = VpbPackageInsightStore.Get(uid);

            AddInsightsSectionHeader(uid, type, s);

            if (rec == null)
            {
                AddInsightsNoteRow(VPBTranslation.T("insights.package.not_scanned",
                    "This package has not been scanned yet."), type, s);
                AddInsightsActionRow(
                    VPBTranslation.T("insights.action.scan_this", "Scan this package"),
                    VPBTranslation.T("insights.tip.scan_this", "Reads this one archive now."),
                    type, s, () => InsightsRescanSingle(uid));
                return;
            }

            bool needsReview = VpbPackageInsightStore.NeedsReview(uid);
            int issueCount = CountFlagBits((int)rec.Issues);

            AddInsightsNoteRow(string.Format(
                VPBTranslation.T("insights.package.summary_fmt",
                    "{0} finding(s) · {1} morph file(s) · {2} script(s) · {3} DLL(s) · {4} assetbundle(s)"),
                issueCount, rec.MorphCount, rec.ScriptCount, rec.DllCount, rec.AssetBundleCount), type, s);

            GameObject actions = UI.CreateChildRT(_insightsBodyParent.gameObject, "PackageActions");
            UI.AddHLG(actions, spacing: 8f * s, childForceExpandWidth: false);
            UI.AddLE(actions, minHeight: 40f * s, preferredHeight: 40f * s);

            if (rec.HasPluginContent)
            {
                string reviewLabel = needsReview
                    ? VPBTranslation.T("insights.action.mark_reviewed", "Mark reviewed")
                    : VPBTranslation.T("insights.action.clear_review", "Clear review");
                GameObject reviewBtn = UI.CreateChromeLayoutButton(
                    actions.transform, 150f * s, 32f * s, reviewLabel, type.Body,
                    needsReview ? GalleryUiColorTokens.AccentConfirm : GalleryUiColorTokens.SurfaceMid,
                    () =>
                    {
                        VpbPackageInsightStore.SetReviewed(uid, needsReview);
                        InsightsClearPendingLaunchGate();
                        RebuildInsightsFloatBody();
                    });
                AddTooltipPlain(reviewBtn, needsReview
                    ? VPBTranslation.T("insights.tip.mark_reviewed",
                        "Records that you have looked at this package's plugin content. The mark clears automatically if the package changes.")
                    : VPBTranslation.T("insights.tip.clear_review", "Removes your review mark for this package."));
            }

            GameObject rescanBtn = UI.CreateChromeLayoutButton(
                actions.transform, 140f * s, 32f * s,
                VPBTranslation.T("insights.action.rescan_this", "Rescan package"), type.Body,
                GalleryUiColorTokens.SurfaceMid, () => InsightsRescanSingle(uid));
            AddTooltipPlain(rescanBtn, VPBTranslation.T("insights.tip.rescan_this",
                "Reads this archive again, ignoring the cached result."));

            GameObject showBtn = UI.CreateChromeLayoutButton(
                actions.transform, 140f * s, 32f * s,
                VPBTranslation.T("insights.action.show_in_gallery", "Show in gallery"), type.Body,
                GalleryUiColorTokens.SurfaceMid, () => InsightsApplySearch(uid));
            AddTooltipPlain(showBtn, VPBTranslation.T("insights.tip.show_in_gallery",
                "Filters the gallery to this package. The window stays open."));

            AddInsightsSectionHeader(VPBTranslation.T("insights.package.findings", "Findings"), type, s, spacedAbove: true);
            if (issueCount > 0)
            {
                int stripe = 0;
                for (int i = 0; i < VpbInsightLabels.AllIssues.Length; i++)
                {
                    PkgIssueFlags f = VpbInsightLabels.AllIssues[i];
                    if ((rec.Issues & f) == 0) continue;
                    AddInsightsFindingRow(VpbInsightLabels.Issue(f), VpbInsightLabels.IssueTip(f),
                        InsightsColorIssue, type, s, (stripe++ & 1) == 1);
                }
            }
            else
            {
                AddInsightsNoteRow(VPBTranslation.T("insights.package.no_findings",
                    "No integrity findings."), type, s);
            }

            if (rec.HasPluginContent)
            {
                AddInsightsSectionHeader(VPBTranslation.T("insights.package.plugin_content", "Plugin content"), type, s, spacedAbove: true);
                int stripe = 0;
                for (int i = 0; i < VpbInsightLabels.AllRisks.Length; i++)
                {
                    PkgRiskFlags f = VpbInsightLabels.AllRisks[i];
                    if ((rec.Risk & f) == 0) continue;
                    AddInsightsFindingRow(VpbInsightLabels.Risk(f), VpbInsightLabels.RiskTip(f),
                        f == PkgRiskFlags.FlaggedHigh ? InsightsColorFlagged : InsightsColorIssue,
                        type, s, (stripe++ & 1) == 1);
                }
            }

            if (rec.UndeclaredCount > 0)
            {
                AddInsightsSectionHeader(string.Format(
                    VPBTranslation.T("insights.package.undeclared_fmt", "Undeclared dependencies ({0})"),
                    rec.UndeclaredCount), type, s, spacedAbove: true);
                AddInsightsNoteRow(VPBTranslation.T("insights.package.undeclared_body",
                    "Referenced by this package's content but absent from its meta.json. VPB counts the missing ones as missing dependencies."), type, s);

                VarPackage pkg = TryResolvePackageByUid(uid);
                for (int i = 0; i < rec.UndeclaredDeps.Length; i++)
                {
                    string dep = rec.UndeclaredDeps[i];
                    if (string.IsNullOrEmpty(dep)) continue;
                    bool installed = true;
                    try { installed = FileManager.IsDependencySatisfiedByInstalled(dep, pkg); }
                    catch { installed = true; }
                    string suffix = installed
                        ? VPBTranslation.T("insights.package.dep_installed", "installed")
                        : VPBTranslation.T("insights.package.dep_missing", "MISSING");
                    AddInsightsFindingRow(dep + "   —   " + suffix, "",
                        installed ? InsightsColorClean : InsightsColorFlagged,
                        type, s, (i & 1) == 1);
                }
            }

            if (rec.Details != null && rec.Details.Length > 0)
            {
                AddInsightsSectionHeader(VPBTranslation.T("insights.package.details", "Detail"), type, s, spacedAbove: true);
                for (int i = 0; i < rec.Details.Length; i++)
                    AddInsightsFindingRow(rec.Details[i], "", GalleryUiColorTokens.TextMuted, type, s, (i & 1) == 1);
            }
        }

        private void InsightsRescanSingle(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(uid);
            if (!VpbPackageInsightScanner.StartScan(set, force: true))
            {
                ShowTemporaryStatus(VPBTranslation.T("insights.status.already_running", "Package scan already running."), 2f);
                return;
            }
            ShowTemporaryStatus(VPBTranslation.T("insights.status.rescan_one", "Rescanning package…"), 2f);
        }

        private static VarPackage TryResolvePackageByUid(string uid)
        {
            try
            {
                Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                VarPackage pkg;
                if (byUid != null && byUid.TryGetValue(uid, out pkg)) return pkg;
            }
            catch { }
            return null;
        }
        
        private void BuildInsightsContentBody(GalleryModalTypography type, float s)
        {
            GameObject modeRow = UI.CreateChildRT(_insightsBodyParent.gameObject, "ContentModeRow");
            UI.AddHLG(modeRow, spacing: 6f * s, childForceExpandWidth: false);
            UI.AddLE(modeRow, minHeight: 36f * s, preferredHeight: 36f * s);

            GameObject filesBtn = UI.CreateChromeLayoutButton(
                modeRow.transform, 130f * s, 30f * s,
                VPBTranslation.T("insights.content.mode_files", "Find a file"), type.Body,
                _insightsContentMode == InsightsContentMode.Files
                    ? GalleryUiColorTokens.AccentSelected : GalleryUiColorTokens.SegmentIdle,
                () => { _insightsContentMode = InsightsContentMode.Files; RebuildInsightsFloatBody(); });
            AddTooltipPlain(filesBtn, VPBTranslation.T("insights.tip.mode_files",
                "Type part of a file name or path to see which packages contain it."));

            GameObject dupBtn = UI.CreateChromeLayoutButton(
                modeRow.transform, 160f * s, 30f * s,
                VPBTranslation.T("insights.content.mode_dupes", "Duplicate assets"), type.Body,
                _insightsContentMode == InsightsContentMode.Duplicates
                    ? GalleryUiColorTokens.AccentSelected : GalleryUiColorTokens.SegmentIdle,
                () => { _insightsContentMode = InsightsContentMode.Duplicates; RebuildInsightsFloatBody(); });
            AddTooltipPlain(dupBtn, VPBTranslation.T("insights.tip.mode_dupes",
                "Assets shipped by more than one package. Whole-file dedupe cannot see these because the packages themselves differ."));

            GameObject spacer = UI.CreateChildRT(modeRow, "Spacer");
            UI.AddLE(spacer, minWidth: 0f, flexibleWidth: 1f);

            if (_insightsContentMode == InsightsContentMode.Files)
                BuildInsightsFileSearchBody(type, s);
            else
                BuildInsightsDuplicatesBody(type, s);
        }

        private void BuildInsightsFileSearchBody(GalleryModalTypography type, float s)
        {
            GameObject row = UI.CreateChildRT(_insightsBodyParent.gameObject, "ContentSearchRow");
            UI.AddHLG(row, spacing: 8f * s, childForceExpandWidth: false);
            UI.AddLE(row, minHeight: 40f * s, preferredHeight: 40f * s);

            _insightsContentInput = UI.CreateChromeLayoutInputField(
                row.transform, type.Body, 32f * s, 1f, 6f * s, 2f * s,
                GalleryUiColorTokens.SurfaceDarker, GalleryUiColorTokens.TextPlaceholder,
                VPBTranslation.T("insights.content.placeholder", "part of a file name or path, e.g. brow or /Morphs/"));
            if (_insightsContentInput != null)
            {
                _insightsContentInput.text = _insightsContentQuery ?? "";
                _insightsContentInput.onEndEdit.AddListener(v =>
                {
                    try { RunInsightsContentSearch(v); } catch { }
                });
            }

            GameObject go = UI.CreateChromeLayoutButton(
                row.transform, 110f * s, 32f * s,
                VPBTranslation.T("insights.content.search", "Search"), type.Body,
                GalleryUiColorTokens.AccentConfirm,
                () => RunInsightsContentSearch(_insightsContentInput != null ? _insightsContentInput.text : ""));
            AddTooltipPlain(go, VPBTranslation.T("insights.tip.content_search",
                "Searches the cached archive index in the background without opening packages."));

            if (!_insightsContentSearched)
            {
                AddInsightsNoteRow(VPBTranslation.T("insights.content.search_intro",
                    "Search file paths inside indexed packages."), type, s);
                return;
            }

            if (!string.IsNullOrEmpty(_insightsContentStatus))
                AddInsightsNoteRow(_insightsContentStatus, type, s);

            for (int i = 0; i < _insightsContentHits.Count; i++)
            {
                ContentFileHit hit = _insightsContentHits[i];
                string uidSnap = hit.PackageUid;
                AddInsightsLinkRow(
                    hit.InternalPath,
                    uidSnap + (hit.Size > 0 ? ("   ·   " + FormatBytesForList(hit.Size)) : ""),
                    VPBTranslation.T("insights.tip.content_hit", "Filter the gallery to this package."),
                    type, s, (i & 1) == 1,
                    () => InsightsApplySearch(uidSnap));
            }
        }

        private void RunInsightsContentSearch(string term)
        {
            _insightsContentQuery = (term ?? "").Trim();
            _insightsContentHits.Clear();
            _insightsContentSearched = true;
            _insightsContentStatus = "";

            if (_insightsContentQuery.Length < 2)
            {
                _insightsContentStatus = VPBTranslation.T("insights.content.too_short",
                    "Type at least two characters.");
                RebuildInsightsFloatBody();
                return;
            }

            _insightsContentStatus = VPBTranslation.T("insights.content.searching", "Searching...");
            StartInsightsContentSearchIfIdle();
            RebuildInsightsFloatBody();
        }

        private sealed class InsightContentSearchResult
        {
            internal string Query;
            internal readonly List<ContentFileHit> Hits = new List<ContentFileHit>(64);
            internal bool Success;
        }

        private bool _insightsContentWorkerPending;
        private volatile InsightContentSearchResult _insightsContentResult;

        private void StartInsightsContentSearchIfIdle()
        {
            if (_insightsContentWorkerPending || _insightsContentQuery.Length < 2) return;
            var result = new InsightContentSearchResult { Query = _insightsContentQuery };
            _insightsContentWorkerPending = true;
            try
            {
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        result.Success = VpbLocalDatabase.TrySearchPackageFiles(
                            result.Query, InsightsContentSearchLimit, result.Hits, allowIndexBuild: true);
                    }
                    catch { result.Success = false; }
                    finally { _insightsContentResult = result; }
                });
            }
            catch { _insightsContentResult = result; }
        }

        private void PumpInsightsContentSearch()
        {
            InsightContentSearchResult result = _insightsContentResult;
            if (result == null) return;
            if (_insightsContentInput != null && _insightsContentInput.isFocused) return;
            _insightsContentResult = null;
            _insightsContentWorkerPending = false;
            if (!string.Equals(result.Query, _insightsContentQuery, StringComparison.Ordinal))
            {
                StartInsightsContentSearchIfIdle();
                return;
            }
            _insightsContentHits.Clear();
            _insightsContentHits.AddRange(result.Hits);
            _insightsContentStatus = "";
            bool ok = result.Success;

            if (!ok)
            {
                _insightsContentStatus = VPBTranslation.T("insights.content.search_failed",
                    "The archive index is not available.");
            }
            else if (_insightsContentHits.Count == 0)
            {
                _insightsContentStatus = string.Format(
                    VPBTranslation.T("insights.content.no_hits_fmt", "No file matches \"{0}\"."), _insightsContentQuery);
            }
            else if (_insightsContentHits.Count >= InsightsContentSearchLimit)
            {
                _insightsContentStatus = string.Format(
                    VPBTranslation.T("insights.content.capped_fmt",
                        "Showing the first {0} matches — narrow the term to see the rest."), InsightsContentSearchLimit);
            }

            RebuildInsightsFloatBody();
        }

        private void BuildInsightsDuplicatesBody(GalleryModalTypography type, float s)
        {
            if (!_insightsDuplicatesLoaded)
            {
                AddInsightsNoteRow(VPBTranslation.T("insights.content.dupes_intro",
                    "Groups the archive index by internal path to find assets bundled by several packages. The first pass builds an index and can take a moment on a large library."), type, s);
                AddInsightsActionRow(
                    VPBTranslation.T("insights.content.find_dupes", "Find duplicate assets"),
                    VPBTranslation.T("insights.tip.find_dupes",
                        "Only files of at least 256 KB are considered, so small shared files do not drown the list."),
                    type, s, () =>
                    {
                        _insightsDuplicates.Clear();
                        try
                        {
                            VpbLocalDatabase.TryFindDuplicateAssets(
                                2, InsightsDuplicateMinBytes, InsightsDuplicateLimit, _insightsDuplicates);
                        }
                        catch { }
                        _insightsDuplicatesLoaded = true;
                        RebuildInsightsFloatBody();
                    });
                return;
            }

            if (_insightsDuplicates.Count == 0)
            {
                AddInsightsNoteRow(VPBTranslation.T("insights.content.no_dupes",
                    "No asset of that size is shipped by more than one package."), type, s);
                return;
            }

            long totalRedundant = 0;
            for (int i = 0; i < _insightsDuplicates.Count; i++) totalRedundant += _insightsDuplicates[i].RedundantBytes;
            AddInsightsNoteRow(string.Format(
                VPBTranslation.T("insights.content.dupes_summary_fmt",
                    "{0} duplicated assets · {1} stored beyond the first copy. Removing a package is the only safe fix — the copies live inside different archives."),
                _insightsDuplicates.Count, FormatBytesForList(totalRedundant)), type, s);

            for (int i = 0; i < _insightsDuplicates.Count; i++)
            {
                DuplicateAssetRow row = _insightsDuplicates[i];
                string pathSnap = row.InternalPath;
                string sub = string.Format(
                    VPBTranslation.T("insights.content.dupe_row_fmt", "{0} packages · {1} each · {2} redundant"),
                    row.PackageCount, FormatBytesForList(row.Size), FormatBytesForList(row.RedundantBytes));
                AddInsightsLinkRow(
                    row.InternalPath, sub,
                    VPBTranslation.T("insights.tip.dupe_row", "List the packages that ship this file."),
                    type, s, (i & 1) == 1,
                    () => ShowInsightsDuplicateOwners(pathSnap));
            }
        }

        private void ShowInsightsDuplicateOwners(string internalPath)
        {
            var uids = new List<string>(16);
            try { VpbLocalDatabase.TryGetPackagesForInternalPath(internalPath, 64, uids); }
            catch { }
            if (uids.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("insights.status.no_owners", "No packages found for that path."), 2.5f);
                return;
            }

            _insightsContentMode = InsightsContentMode.Files;
            _insightsContentSearched = true;
            _insightsContentHits.Clear();
            for (int i = 0; i < uids.Count; i++)
            {
                _insightsContentHits.Add(new ContentFileHit
                {
                    PackageUid = uids[i],
                    InternalPath = internalPath,
                    Size = 0
                });
            }
            _insightsContentStatus = string.Format(
                VPBTranslation.T("insights.content.owners_fmt", "{0} packages ship {1}"), uids.Count, internalPath);
            RebuildInsightsFloatBody();
        }
        
        private void AddInsightsSectionHeader(string text, GalleryModalTypography type, float s, bool spacedAbove = false)
        {
            if (_insightsBodyParent == null) return;
            if (spacedAbove)
            {
                GameObject gap = UI.CreateChildRT(_insightsBodyParent.gameObject, "Gap");
                UI.AddLE(gap, minHeight: 12f * s, preferredHeight: 12f * s);
            }
            Text t = UI.CreateLabel(
                _insightsBodyParent.gameObject, text, type.Body, InsightsHeadingColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "SectionHeader");
            UI.AddLE(t.gameObject, minHeight: 26f * s, preferredHeight: 26f * s);
        }

        private void AddInsightsNoteRow(string text, GalleryModalTypography type, float s)
        {
            if (_insightsBodyParent == null) return;
            UI.CreateLabel(
                _insightsBodyParent.gameObject, text, type.Caption, GalleryUiColorTokens.TextDim,
                TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow, name: "Note");
        }

        private void AddInsightsCountRow(
            string label, int count, string tip, string searchToken,
            GalleryModalTypography type, float s, bool altStripe, Color countColor)
        {
            if (_insightsBodyParent == null) return;
            float rowH = GalleryUiDesignTokens.InsightsFloatRowHeightRef * s;
            GameObject row = UI.CreateChildRT(_insightsBodyParent.gameObject, "CountRow");
            UI.AddImage(row, altStripe ? InsightsRowBgB : InsightsRowBgA);
            UI.AddHLG(row, spacing: 8f * s, padding: UI.Pad(10, 8, 3, 3, s), childForceExpandWidth: false);
            UI.AddLE(row, minHeight: rowH, preferredHeight: rowH);

            Text lt = UI.CreateLabel(row, label, type.Body,
                count > 0 ? GalleryUiColorTokens.TextPrimary : InsightsZeroText,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "Label");
            UI.AddLE(lt.gameObject, minWidth: 0f, flexibleWidth: 1f);

            Text ct = UI.CreateLabel(row, count.ToString(), type.Body, countColor,
                TextAnchor.MiddleRight, HorizontalWrapMode.Overflow, name: "Count");
            UI.AddLE(ct.gameObject, minWidth: 56f * s, preferredWidth: 56f * s);

            if (count > 0 && !string.IsNullOrEmpty(searchToken))
            {
                string tokenSnap = searchToken;
                GameObject show = UI.CreateChromeLayoutButton(
                    row.transform, 84f * s, rowH - 8f * s,
                    VPBTranslation.T("insights.action.show", "Show"), type.Caption,
                    GalleryUiColorTokens.SurfaceMid, () => InsightsApplySearch(tokenSnap));
                AddTooltipPlain(show, string.Format(
                    VPBTranslation.T("insights.tip.show_fmt", "Filter the gallery with \"{0}\"."), tokenSnap));
            }
            else
            {
                GameObject pad = UI.CreateChildRT(row, "Pad");
                UI.AddLE(pad, minWidth: 84f * s, preferredWidth: 84f * s);
            }

            if (!string.IsNullOrEmpty(tip)) AddTooltipPlain(row, tip);
        }

        private void AddInsightsFindingRow(string label, string tip, Color color, GalleryModalTypography type, float s, bool altStripe)
        {
            if (_insightsBodyParent == null) return;
            float rowH = GalleryUiDesignTokens.InsightsFloatRowHeightRef * s;
            GameObject row = UI.CreateChildRT(_insightsBodyParent.gameObject, "FindingRow");
            UI.AddImage(row, altStripe ? InsightsRowBgB : InsightsRowBgA);
            UI.AddHLG(row, spacing: 8f * s, padding: UI.Pad(10, 8, 3, 3, s), childForceExpandWidth: false);
            UI.AddLE(row, minHeight: rowH, preferredHeight: rowH);

            Text lt = UI.CreateLabel(row, label, type.Body, color,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "Label");
            UI.AddLE(lt.gameObject, minWidth: 0f, flexibleWidth: 1f);

            if (!string.IsNullOrEmpty(tip)) AddTooltipPlain(row, tip);
        }

        private void AddInsightsLinkRow(
            string primary, string secondary, string tip,
            GalleryModalTypography type, float s, bool altStripe, UnityAction onClick)
        {
            if (_insightsBodyParent == null) return;
            float rowH = GalleryUiDesignTokens.InsightsFloatRowHeightRef * s;
            GameObject row = UI.CreateChildRT(_insightsBodyParent.gameObject, "LinkRow");
            UI.AddImage(row, altStripe ? InsightsRowBgB : InsightsRowBgA);
            UI.AddHLG(row, spacing: 8f * s, padding: UI.Pad(10, 8, 3, 3, s), childForceExpandWidth: false);
            UI.AddLE(row, minHeight: rowH, preferredHeight: rowH);

            Text lt = UI.CreateLabel(row, primary, type.Body, GalleryUiColorTokens.TextPrimary,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "Primary");
            UI.AddLE(lt.gameObject, minWidth: 0f, flexibleWidth: 1f);

            Text st = UI.CreateLabel(row, secondary ?? "", type.Caption, GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleRight, HorizontalWrapMode.Overflow, name: "Secondary");
            UI.AddLE(st.gameObject, minWidth: 180f * s, preferredWidth: 220f * s);

            GameObject btn = UI.CreateChromeLayoutButton(
                row.transform, 84f * s, rowH - 8f * s,
                VPBTranslation.T("insights.action.show", "Show"), type.Caption,
                GalleryUiColorTokens.SurfaceMid, onClick);
            if (!string.IsNullOrEmpty(tip)) AddTooltipPlain(btn, tip);
        }

        private void AddInsightsActionRow(string label, string tip, GalleryModalTypography type, float s, UnityAction onClick)
        {
            if (_insightsBodyParent == null) return;
            GameObject row = UI.CreateChildRT(_insightsBodyParent.gameObject, "ActionRow");
            UI.AddHLG(row, spacing: 8f * s, childForceExpandWidth: false);
            UI.AddLE(row, minHeight: 40f * s, preferredHeight: 40f * s);

            GameObject btn = UI.CreateChromeLayoutButton(
                row.transform, 250f * s, 32f * s, label, type.Body,
                GalleryUiColorTokens.SurfaceMid, onClick);
            if (!string.IsNullOrEmpty(tip)) AddTooltipPlain(btn, tip);

            GameObject spacer = UI.CreateChildRT(row, "Spacer");
            UI.AddLE(spacer, minWidth: 0f, flexibleWidth: 1f);
        }

        private void AddInsightsEmptyState(string title, string body, GalleryModalTypography type, float s)
        {
            if (_insightsBodyParent == null) return;
            GameObject gap = UI.CreateChildRT(_insightsBodyParent.gameObject, "Gap");
            UI.AddLE(gap, minHeight: 30f * s, preferredHeight: 30f * s);

            Text t = UI.CreateLabel(
                _insightsBodyParent.gameObject, title, type.Title, GalleryUiColorTokens.TextPrimary,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, name: "EmptyTitle");
            UI.AddLE(t.gameObject, minHeight: 36f * s, preferredHeight: 36f * s);

            UI.CreateLabel(
                _insightsBodyParent.gameObject, body, type.Body, GalleryUiColorTokens.TextDim,
                TextAnchor.UpperCenter, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow, name: "EmptyBody");

            AddInsightsActionRow(
                VPBTranslation.T("insights.action.scan", "Scan library"),
                VPBTranslation.T("insights.tip.scan",
                    "Reads each package once and caches the result. Packages that have not changed since the last scan are skipped."),
                type, s, () => InsightsStartFullScan(false));
        }

        private void InsightsApplySearch(string term)
        {
            if (string.IsNullOrEmpty(term)) return;
            try
            {
                SetNameFilter(term);
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, term, _titleBarSearchOnValueChanged); }
                catch { }
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("insights.status.filtered_fmt", "Gallery filtered: {0}"), term), 2.5f);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Insights] filter apply failed: " + ex.Message);
            }
        }

        private static int ResolveInstalledPackageCount()
        {
            try
            {
                Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                return byUid != null ? byUid.Count : 0;
            }
            catch { return 0; }
        }
    }
}
