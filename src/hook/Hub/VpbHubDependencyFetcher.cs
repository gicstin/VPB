using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class VpbHubDependencyFetcher
    {
        public const int MaxRounds = 6;
        public const int MaxPackages = 512;

        const int DependencyDepth = 3;
        const float LookupTimeoutSeconds = 120f;
        const float DownloadTimeoutSeconds = 900f;
        const float InstallSettleSeconds = 120f;
        const float ConfirmTimeoutSeconds = 600f;
        const float ScanBudgetPerFrame = 0.5f;
        const float ProgressTextInterval = 0.25f;
        const int MaxUidChars = 220;
        const string PluginScriptMarker = ":/Custom/Scripts/";

        public enum Outcome
        {
            NothingToDo,
            Complete,
            Partial,
            Declined,
            Failed
        }

        public sealed class FetchPlan
        {
            public readonly List<string> Names = new List<string>(16);
            public readonly List<long> Sizes = new List<long>(16);
            public readonly List<string> NotOnHub = new List<string>(4);
            public long TotalBytes;
            public long BudgetBytes;
            public int UnknownSizeCount;

            public bool OverBudget
            {
                get { return BudgetBytes > 0 && TotalBytes > BudgetBytes; }
            }

            public bool IsEmpty
            {
                get { return Names.Count == 0 && NotOnHub.Count == 0; }
            }

            public bool HasUnknownSizes
            {
                get { return UnknownSizeCount > 0; }
            }
        }

        public sealed class FetchResult
        {
            public Outcome Outcome = Outcome.NothingToDo;
            public string FailReason;
            public int Installed;
            public int Rounds;
            public long Bytes;
            public readonly List<string> NotOnHub = new List<string>(4);
            public readonly List<string> Unresolved = new List<string>(4);

            public bool InstalledAny
            {
                get { return Installed > 0; }
            }
        }

        public delegate void ConfirmRequest(FetchPlan plan, Action<bool> answer);

        static readonly List<string> _pending = new List<string>(64);
        static readonly List<string> _query = new List<string>(64);
        static readonly List<string> _installedUids = new List<string>(64);
        static readonly HashSet<string> _visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<HubResourcePackage> _wave = new List<HubResourcePackage>(32);

        static bool _running;
        static bool _cancel;
        static string _failDetail;
        static long _approvedBytes;
        static float _nextProgressText;

        public static bool Busy
        {
            get { return _running; }
        }

        public static void Cancel()
        {
            if (_running) _cancel = true;
        }

        public static string ModeOff { get { return "Off"; } }
        public static string ModeAsk { get { return "Ask"; } }
        public static string ModeAlways { get { return "Always"; } }

        public static string NormalizeMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return ModeAsk;
            if (string.Equals(mode, ModeOff, StringComparison.OrdinalIgnoreCase)) return ModeOff;
            if (string.Equals(mode, ModeAlways, StringComparison.OrdinalIgnoreCase)) return ModeAlways;
            return ModeAsk;
        }

        public static string Mode
        {
            get
            {
                try { return NormalizeMode(VPBConfig.Instance.HubFetchMissingMode); }
                catch { return ModeAsk; }
            }
        }

        public static bool AutoFetchEnabled
        {
            get { return !string.Equals(Mode, ModeOff, StringComparison.OrdinalIgnoreCase); }
        }

        public static long BudgetBytes
        {
            get
            {
                try
                {
                    int mb = VPBConfig.Instance.HubFetchMissingMaxMB;
                    if (mb <= 0) return 0L;
                    return (long)mb * 1024L * 1024L;
                }
                catch { return 0L; }
            }
        }

        public static bool HubAvailable(out string reason)
        {
            reason = null;
            HubBrowse hub = SafeHub();
            if (hub == null)
            {
                reason = VPBTranslation.T("gallery.hubfetch.fail.no_hub", "VaM's Hub browser is not available.");
                return false;
            }
            bool enabled = false;
            try { enabled = hub.HubEnabled; }
            catch { enabled = false; }
            if (!enabled)
            {
                reason = VPBTranslation.T("gallery.hubfetch.fail.hub_disabled", "The Hub is disabled — enable it in VaM's Hub tab.");
                return false;
            }
            return true;
        }

        public static IEnumerator Fetch(
            IList<string> wanted,
            string bannerTitle,
            ConfirmRequest confirm,
            Action<FetchResult> onDone)
        {
            FetchResult result = new FetchResult();

            if (_running)
            {
                result.Outcome = Outcome.Failed;
                result.FailReason = VPBTranslation.T("gallery.hubfetch.fail.busy", "Another Hub download is already running.");
                if (onDone != null) onDone(result);
                yield break;
            }

            ResetState();

            if (!SeedPending(wanted))
            {
                result.Outcome = Outcome.NothingToDo;
                if (onDone != null) onDone(result);
                yield break;
            }

            string unavailable;
            if (!HubAvailable(out unavailable))
            {
                result.Outcome = Outcome.Failed;
                result.FailReason = unavailable;
                if (onDone != null) onDone(result);
                yield break;
            }

            HubBrowse hub = SafeHub();
            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { sc = null; }
            if (hub == null || sc == null)
            {
                result.Outcome = Outcome.Failed;
                result.FailReason = VPBTranslation.T("gallery.hubfetch.fail.no_hub", "VaM's Hub browser is not available.");
                if (onDone != null) onDone(result);
                yield break;
            }

            _running = true;
            _cancel = false;

            try
            {
                VpbProgressService.BeginHubFetch(bannerTitle);

                for (result.Rounds = 0; result.Rounds < MaxRounds; result.Rounds++)
                {
                    if (_cancel || _pending.Count == 0) break;

                    _query.Clear();
                    for (int i = 0; i < _pending.Count; i++) _query.Add(_pending[i]);
                    _pending.Clear();

                    VpbProgressService.ReportHubFetchPhase(VPBTranslation.T(
                        "gallery.hubfetch.phase.checking", "Asking the Hub what is available…"));

                    Dictionary<string, JSONClass> found = null;
                    string lookupError = null;
                    bool lookupDone = false;

                    try
                    {
                        hub.FindPackages(_query,
                            delegate (Dictionary<string, JSONClass> map) { found = map; lookupDone = true; },
                            delegate (string err) { lookupError = err; lookupDone = true; });
                    }
                    catch (Exception e)
                    {
                        lookupError = e.Message;
                        lookupDone = true;
                    }

                    float lookupDeadline = Time.realtimeSinceStartup + LookupTimeoutSeconds;
                    while (!lookupDone && !_cancel && Time.realtimeSinceStartup < lookupDeadline)
                        yield return null;

                    if (_cancel) break;

                    if (!lookupDone)
                    {
                        Finish(result, Outcome.Failed, VPBTranslation.T(
                            "gallery.hubfetch.fail.timeout", "The Hub did not answer in time."));
                        yield break;
                    }
                    if (found == null)
                    {
                        LogUtil.LogWarning("[VPB.HubFetch] the Hub did not answer: "
                            + (string.IsNullOrEmpty(lookupError) ? "no reason given" : lookupError));
                        Finish(result, Outcome.Failed, VPBTranslation.T(
                            "gallery.hubfetch.fail.offline", "The Hub could not be reached."));
                        yield break;
                    }

                    FetchPlan plan = BuildPlan(hub, found, result);

                    if (plan.IsEmpty)
                    {
                        yield return CollectNextRound(result);
                        continue;
                    }

                    if (result.Rounds > 0 && plan.BudgetBytes > 0
                        && _approvedBytes + plan.TotalBytes > plan.BudgetBytes)
                    {
                        LogUtil.LogWarning("[VPB.HubFetch] stopping at the "
                            + FormatBytes(plan.BudgetBytes) + " download limit with "
                            + plan.Names.Count + " package(s) left");
                        for (int i = 0; i < _query.Count; i++)
                        {
                            if (!result.Unresolved.Contains(_query[i])) result.Unresolved.Add(_query[i]);
                        }
                        Finish(result, Outcome.Partial, string.Format(
                            VPBTranslation.T("gallery.hubfetch.fail.limit_reached",
                                "Stopped at the {0} download limit — {1} package(s) were not fetched."),
                            FormatBytes(plan.BudgetBytes), plan.Names.Count + plan.NotOnHub.Count));
                        yield break;
                    }

                    if (result.Rounds == 0)
                    {
                        bool proceed = true;
                        if (confirm != null)
                        {
                            bool answered = false;
                            VpbProgressService.ReportHubFetchPhase(VPBTranslation.T(
                                "gallery.hubfetch.phase.waiting", "Waiting for your answer…"));
                            try
                            {
                                confirm(plan, delegate (bool ok) { proceed = ok; answered = true; });
                            }
                            catch (Exception e)
                            {
                                LogUtil.LogWarning("[VPB.HubFetch] confirmation prompt failed: " + e.Message);
                                answered = true;
                                proceed = false;
                            }

                            float confirmDeadline = Time.realtimeSinceStartup + ConfirmTimeoutSeconds;
                            while (!answered && !_cancel && Time.realtimeSinceStartup < confirmDeadline)
                                yield return null;

                            if (!answered) proceed = false;
                        }
                        else if (plan.OverBudget || plan.HasUnknownSizes)
                        {
                            proceed = false;
                            Finish(result, Outcome.Failed, plan.OverBudget
                                ? OverBudgetMessage(plan)
                                : UnknownSizeMessage(plan));
                            yield break;
                        }

                        if (_cancel) break;
                        if (!proceed)
                        {
                            Finish(result, Outcome.Declined, null);
                            yield break;
                        }
                    }

                    _approvedBytes += plan.TotalBytes;

                    yield return DownloadWave(hub, result);
                    if (_cancel) break;
                    if (result.Outcome == Outcome.Failed) yield break;

                    yield return WaitForInstall(hub);
                    if (_cancel) break;

                    yield return CollectNextRound(result);
                }

                if (_cancel)
                {
                    Finish(result, result.Installed > 0 ? Outcome.Partial : Outcome.Declined,
                        VPBTranslation.T("gallery.hubfetch.fail.cancelled", "Download cancelled."));
                    yield break;
                }

                for (int i = 0; i < _pending.Count; i++) result.Unresolved.Add(_pending[i]);

                bool complete = result.NotOnHub.Count == 0 && result.Unresolved.Count == 0;
                Finish(result, complete ? Outcome.Complete : Outcome.Partial, _failDetail);
            }
            finally
            {
                _running = false;
                _cancel = false;
                try { VpbProgressService.EndHubFetch(); } catch { }
                _wave.Clear();
                if (onDone != null) onDone(result);
            }
        }

        static void ResetState()
        {
            _pending.Clear();
            _query.Clear();
            _installedUids.Clear();
            _visited.Clear();
            _wave.Clear();
            _failDetail = null;
            _approvedBytes = 0L;
            _nextProgressText = 0f;
            _installedBeforeWave = 0;
        }

        static bool SeedPending(IList<string> wanted)
        {
            if (wanted == null) return false;
            for (int i = 0; i < wanted.Count && _pending.Count < MaxPackages; i++)
            {
                string uid = wanted[i];
                if (!IsFetchableUid(uid)) continue;
                if (!_visited.Add(uid)) continue;
                _pending.Add(uid);
            }
            return _pending.Count > 0;
        }

        public static bool IsFetchableUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            if (uid.Length > MaxUidChars) return false;
            if (uid.IndexOf(PluginScriptMarker, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (uid.IndexOf(':') >= 0) return false;
            if (uid.IndexOf('/') >= 0 || uid.IndexOf('\\') >= 0) return false;
            if (uid.IndexOf('.') < 0) return false;
            if (string.Equals(uid, "SELF", StringComparison.OrdinalIgnoreCase)) return false;
            if (uid.StartsWith("SELF.", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        static FetchPlan BuildPlan(HubBrowse hub, Dictionary<string, JSONClass> found, FetchResult result)
        {
            FetchPlan plan = new FetchPlan();
            plan.BudgetBytes = BudgetBytes;
            _wave.Clear();

            for (int i = 0; i < _query.Count; i++)
            {
                string uid = _query[i];
                JSONClass entry;
                if (!found.TryGetValue(uid, out entry) || entry == null)
                {
                    NoteNotOnHub(result, plan, uid);
                    continue;
                }

                HubResourcePackage pkg = null;
                try { pkg = new HubResourcePackage(entry, hub, true); }
                catch (Exception e)
                {
                    LogUtil.LogWarning("[VPB.HubFetch] the Hub's answer for " + uid + " could not be read: " + e.Message);
                    pkg = null;
                }

                if (pkg == null || !pkg.HasValidDownloadUrl)
                {
                    NoteNotOnHub(result, plan, uid);
                    continue;
                }

                bool needs = true;
                try { needs = pkg.NeedsDownload; }
                catch { needs = true; }
                if (!needs) continue;

                long size = pkg.FileSize > 0 ? pkg.FileSize : 0L;
                if (size <= 0L) plan.UnknownSizeCount++;
                plan.Names.Add(SafeName(pkg, uid));
                plan.Sizes.Add(size);
                plan.TotalBytes += size;
                _wave.Add(pkg);
            }

            return plan;
        }

        static void NoteNotOnHub(FetchResult result, FetchPlan plan, string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            for (int i = 0; i < result.NotOnHub.Count; i++)
            {
                if (string.Equals(result.NotOnHub[i], uid, StringComparison.OrdinalIgnoreCase)) return;
            }
            result.NotOnHub.Add(uid);
            if (plan != null) plan.NotOnHub.Add(uid);
        }

        static IEnumerator DownloadWave(HubBrowse hub, FetchResult result)
        {
            int n = _wave.Count;
            if (n == 0) yield break;

            try { hub.DeferRefreshUntilQueueDrains(); }
            catch { }

            long waveBytes = 0L;
            for (int i = 0; i < n; i++)
                waveBytes += _wave[i].FileSize > 0 ? _wave[i].FileSize : 0L;

            int[] state = new int[n];
            for (int i = 0; i < n; i++)
            {
                HubResourcePackage p = _wave[i];
                int slot = i;
                p.OnDownloadSucceeded = delegate { state[slot] = 1; };
                p.OnDownloadFailed = delegate (string err) { state[slot] = 2; NoteDownloadError(err); };
                try { p.Download(); }
                catch (Exception e)
                {
                    state[slot] = 2;
                    LogUtil.LogWarning("[VPB.HubFetch] " + SafeName(p, null) + " would not start downloading: " + e.Message);
                }
            }

            long baseBytes = result.Bytes;
            int baseInstalled = result.Installed;
            float deadline = Time.realtimeSinceStartup + DownloadTimeoutSeconds;

            while (!_cancel)
            {
                int settled = 0;
                int done = 0;
                double liveBytes = 0.0;
                string current = null;

                for (int i = 0; i < n; i++)
                {
                    HubResourcePackage p = _wave[i];
                    long size = p.FileSize > 0 ? p.FileSize : 0L;

                    if (state[i] != 0)
                    {
                        settled++;
                        if (state[i] == 1)
                        {
                            done++;
                            liveBytes += size;
                        }
                        continue;
                    }

                    float f = 0f;
                    try { f = p.DownloadProgress01; }
                    catch { f = 0f; }
                    if (f < 0f) f = 0f;
                    else if (f > 1f) f = 1f;
                    liveBytes += size * f;
                    if (current == null) current = SafeName(p, null);
                }

                result.Installed = baseInstalled + done;
                result.Bytes = baseBytes + (long)liveBytes;
                ReportProgress(done, n, (long)liveBytes, waveBytes, current, false);

                if (settled >= n) break;

                if (Time.realtimeSinceStartup >= deadline)
                {
                    LogUtil.LogWarning("[VPB.HubFetch] a Hub download did not finish in "
                        + (int)(DownloadTimeoutSeconds / 60f) + " minutes; giving up on this batch");
                    Finish(result, Outcome.Failed, VPBTranslation.T(
                        "gallery.hubfetch.fail.timeout", "The Hub did not answer in time."));
                    yield break;
                }
                yield return null;
            }

            for (int i = 0; i < n; i++)
            {
                if (state[i] == 1)
                {
                    string uid = SafeUid(_wave[i]);
                    if (!string.IsNullOrEmpty(uid)) _installedUids.Add(uid);
                }
                _wave[i].OnDownloadSucceeded = null;
                _wave[i].OnDownloadFailed = null;
            }

            result.Bytes = baseBytes + waveBytes;
        }

        static int _installedBeforeWave;

        static IEnumerator WaitForInstall(HubBrowse hub)
        {
            VpbProgressService.ReportHubFetchPhase(VPBTranslation.T(
                "gallery.hubfetch.phase.installing", "Registering the new packages…"));

            float deadline = Time.realtimeSinceStartup + InstallSettleSeconds;
            while (!_cancel && Time.realtimeSinceStartup < deadline)
            {
                bool pendingRefresh = false;
                try { pendingRefresh = hub.ShouldDeferDownloadRefresh; }
                catch { pendingRefresh = false; }
                if (!pendingRefresh) break;
                yield return null;
            }
            yield return null;
        }

        static IEnumerator CollectNextRound(FetchResult result)
        {
            if (_installedUids.Count == 0) yield break;

            int from = _installedBeforeWave;
            _installedBeforeWave = _installedUids.Count;
            if (from >= _installedUids.Count) yield break;

            float sliceEnd = Time.realtimeSinceStartup + ScanBudgetPerFrame;

            for (int i = from; i < _installedUids.Count; i++)
            {
                if (_cancel) yield break;

                string uid = _installedUids[i];
                HashSet<string> deps = null;
                try { deps = FileManager.GetDependenciesDeep(uid, DependencyDepth); }
                catch { deps = null; }

                if (deps != null)
                {
                    foreach (string dep in deps)
                    {
                        if (!IsFetchableUid(dep)) continue;
                        if (_visited.Contains(dep)) continue;
                        if (_visited.Count >= MaxPackages) break;

                        bool satisfied;
                        try { satisfied = FileManager.IsDependencySatisfiedByInstalled(dep); }
                        catch { satisfied = true; }
                        if (satisfied) continue;

                        _visited.Add(dep);
                        _pending.Add(dep);
                    }
                }

                if (Time.realtimeSinceStartup >= sliceEnd)
                {
                    yield return null;
                    sliceEnd = Time.realtimeSinceStartup + ScanBudgetPerFrame;
                }
            }

            if (_pending.Count > 0)
                LogUtil.Log("[VPB.HubFetch] the packages that just installed need "
                    + _pending.Count + " more; fetching those too");
        }

        static void NoteDownloadError(string err)
        {
            if (string.IsNullOrEmpty(err)) return;
            LogUtil.LogWarning("[VPB.HubFetch] a Hub download failed: " + err);

            if (_failDetail != null) return;

            if (err.IndexOf("401", StringComparison.Ordinal) >= 0
                || err.IndexOf("403", StringComparison.Ordinal) >= 0)
            {
                _failDetail = VPBTranslation.T("gallery.hubfetch.fail.login",
                    "The Hub refused the download — sign in to the Hub in VaM and try again.");
                return;
            }
            if (err.StartsWith("save:", StringComparison.Ordinal))
            {
                _failDetail = VPBTranslation.T("gallery.hubfetch.fail.save",
                    "A package downloaded but could not be saved to AddonPackages.");
                return;
            }
            _failDetail = VPBTranslation.T("gallery.hubfetch.fail.download",
                "One or more downloads failed — see the log.");
        }

        static void Finish(FetchResult result, Outcome outcome, string failReason)
        {
            result.Outcome = outcome;
            if (!string.IsNullOrEmpty(failReason)) result.FailReason = failReason;
            else if (!string.IsNullOrEmpty(_failDetail)) result.FailReason = _failDetail;
        }

        static void ReportProgress(int done, int total, long bytesDone, long bytesTotal, string current, bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now < _nextProgressText) return;
            _nextProgressText = now + ProgressTextInterval;

            float p01 = bytesTotal > 0 ? Mathf.Clamp01((float)((double)bytesDone / bytesTotal)) : -1f;
            string subtitle;
            if (total > 0)
            {
                subtitle = string.Format(
                    VPBTranslation.T("gallery.hubfetch.phase.downloading", "Downloading {0}/{1} — {2} of {3}"),
                    done, total, FormatBytes(bytesDone), FormatBytes(bytesTotal));
                if (!string.IsNullOrEmpty(current))
                    subtitle = subtitle + "  ·  " + current;
            }
            else
            {
                subtitle = VPBTranslation.T("gallery.hubfetch.phase.checking", "Asking the Hub what is available…");
            }

            VpbProgressService.ReportHubFetch(p01, subtitle);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0L) bytes = 0L;
            if (bytes < 1024L) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024.0) return kb.ToString("0") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024.0) return mb.ToString("0.0") + " MB";
            return (mb / 1024.0).ToString("0.00") + " GB";
        }

        public static string OverBudgetMessage(FetchPlan plan)
        {
            return string.Format(
                VPBTranslation.T("gallery.hubfetch.fail.too_big",
                    "The missing packages are {0}, over the {1} download limit in Settings."),
                FormatBytes(plan != null ? plan.TotalBytes : 0L),
                FormatBytes(plan != null ? plan.BudgetBytes : 0L));
        }

        public static string UnknownSizeMessage(FetchPlan plan)
        {
            int count = plan != null ? plan.UnknownSizeCount : 0;
            return string.Format(
                count == 1
                    ? VPBTranslation.T("gallery.hubfetch.fail.unknown_size_one",
                        "The Hub did not report one package's download size. Switch to Ask to review it before downloading.")
                    : VPBTranslation.T("gallery.hubfetch.fail.unknown_size",
                        "The Hub did not report {0} package download sizes. Switch to Ask to review them before downloading."),
                count);
        }

        static string SafeName(HubResourcePackage p, string fallback)
        {
            if (p == null) return fallback ?? string.Empty;
            try
            {
                string n = p.Name;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return fallback ?? string.Empty;
        }

        static string SafeUid(HubResourcePackage p)
        {
            string name = SafeName(p, null);
            if (string.IsNullOrEmpty(name)) return null;
            if (name.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        static HubBrowse SafeHub()
        {
            try { return HubBrowse.singleton; }
            catch { return null; }
        }
    }
}
