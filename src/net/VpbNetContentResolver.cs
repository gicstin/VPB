using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using VpbNet;

namespace VPB
{
    // Peer's list is a seed. Recurse from local metadata; terminate on visited/rounds/budget.
    public static class VpbNetContentResolver
    {
        public const int MaxRounds = 6;
        public const int MaxPackages = 512;
        public const int MaxNamedMissing = 6;

        const float RoundTimeoutSeconds = 900f;
        const float SettleTimeoutSeconds = 120f;
        const float ScanBudgetPerFrame = 0.5f;

        static readonly List<string> _pending = new List<string>(64);
        static readonly List<string> _installed = new List<string>(64);
        static readonly List<string> _notOnHub = new List<string>(16);
        static readonly HashSet<string> _visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<HubResourcePackage> _wave = new List<HubResourcePackage>(32);
        static readonly List<string> _query = new List<string>(64);

        static Coroutine _routine;
        static bool _running;
        static bool _cancel;
        static byte _phase = VpbNetContentPhase.Unknown;
        static byte _fail = VpbNetContentFail.None;
        static int _have;
        static int _need;
        static uint _doneKiB;
        static uint _totalKiB;
        static string _current = string.Empty;
        static long _budgetBytes;
        static long _spentBytes;
        static int _rounds;

        public static bool Busy { get { return _running; } }
        public static byte Phase { get { return _phase; } }
        public static byte Fail { get { return _fail; } }
        public static int Have { get { return _have; } }
        public static int Need { get { return _need; } }
        public static uint DoneKiB { get { return _doneKiB; } }
        public static uint TotalKiB { get { return _totalKiB; } }
        public static string Current { get { return _current; } }
        public static int NotOnHubCount { get { return _notOnHub.Count; } }
        public static int InstalledCount { get { return _installed.Count; } }
        public static int Rounds { get { return _rounds; } }

        public static string NotOnHub(int i)
        {
            return i >= 0 && i < _notOnHub.Count ? _notOnHub[i] : null;
        }

        public static void Reset()
        {
            _pending.Clear();
            _installed.Clear();
            _notOnHub.Clear();
            _visited.Clear();
            _wave.Clear();
            _query.Clear();
            _phase = VpbNetContentPhase.Unknown;
            _fail = VpbNetContentFail.None;
            _have = 0;
            _need = 0;
            _doneKiB = 0;
            _totalKiB = 0;
            _current = string.Empty;
            _spentBytes = 0;
            _rounds = 0;
            _installedBeforeWave = 0;
        }

        public static void Cancel()
        {
            if (!_running) return;
            _cancel = true;
        }

        public static void Stop()
        {
            _cancel = true;
            if (_routine != null)
            {
                try
                {
                    SuperController sc = SuperController.singleton;
                    if (sc != null) sc.StopCoroutine(_routine);
                }
                catch { }
            }
            _routine = null;
            _running = false;
        }

        // maxBytes 0 = auto-fetch off. Answered here so every entry point gets the same message.
        public static bool Begin(VpbNetContentPlan plan, uint estimateKiB, long maxBytes)
        {
            if (_running) return false;
            if (plan == null || plan.NeedsNothing)
            {
                Reset();
                _phase = VpbNetContentPhase.Ready;
                return true;
            }

            Reset();

            if (maxBytes <= 0)
            {
                _phase = VpbNetContentPhase.Failed;
                _fail = VpbNetContentFail.TooBig;
                return false;
            }

            HubBrowse hub = SafeHub();
            if (hub == null)
            {
                _phase = VpbNetContentPhase.Failed;
                _fail = VpbNetContentFail.HubDisabled;
                return false;
            }
            if (!SafeHubEnabled(hub))
            {
                _phase = VpbNetContentPhase.Failed;
                _fail = VpbNetContentFail.HubDisabled;
                return false;
            }

            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { }
            if (sc == null)
            {
                _phase = VpbNetContentPhase.Failed;
                _fail = VpbNetContentFail.HubOffline;
                return false;
            }

            for (int i = 0; i < plan.Count && _pending.Count < MaxPackages; i++)
            {
                string uid = plan.Wanted(i);
                if (string.IsNullOrEmpty(uid)) continue;
                if (!_visited.Add(uid)) continue;
                _pending.Add(uid);
            }

            if (_pending.Count == 0)
            {
                _phase = VpbNetContentPhase.Ready;
                return true;
            }

            _budgetBytes = maxBytes;
            _need = _pending.Count;
            _totalKiB = estimateKiB;
            _phase = VpbNetContentPhase.Checking;
            _cancel = false;
            _running = true;

            try { _routine = sc.StartCoroutine(Run(hub)); }
            catch (Exception e)
            {
                _running = false;
                _phase = VpbNetContentPhase.Failed;
                _fail = VpbNetContentFail.HubOffline;
                LogUtil.LogError("[VPB.Net] content fetch could not start: " + e.Message);
                return false;
            }
            return true;
        }

        static IEnumerator Run(HubBrowse hub)
        {
            try
            {
                for (_rounds = 0; _rounds < MaxRounds; _rounds++)
                {
                    if (_cancel) break;
                    if (_pending.Count == 0) break;

                    _query.Clear();
                    for (int i = 0; i < _pending.Count; i++) _query.Add(_pending[i]);
                    _pending.Clear();

                    _phase = VpbNetContentPhase.Checking;
                    _current = string.Empty;

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

                    float lookupDeadline = Time.realtimeSinceStartup + SettleTimeoutSeconds;
                    while (!lookupDone && Time.realtimeSinceStartup < lookupDeadline && !_cancel)
                        yield return null;

                    if (_cancel) break;

                    if (!lookupDone)
                    {
                        Finish(VpbNetContentPhase.Failed, VpbNetContentFail.Timeout);
                        yield break;
                    }
                    if (found == null)
                    {
                        LogUtil.LogWarning("[VPB.Net] the Hub did not answer: "
                            + (string.IsNullOrEmpty(lookupError) ? "no reason given" : lookupError));
                        Finish(VpbNetContentPhase.Failed, VpbNetContentFail.HubOffline);
                        yield break;
                    }

                    BuildWave(hub, found);
                    if (_wave.Count == 0) continue;

                    if (_spentBytes > _budgetBytes)
                    {
                        Finish(VpbNetContentPhase.Failed, VpbNetContentFail.TooBig);
                        yield break;
                    }

                    yield return DownloadWave(hub);
                    if (_cancel) break;
                    if (VpbNetContentPhase.IsSettled(_phase) && _phase != VpbNetContentPhase.Ready) yield break;

                    yield return WaitForInstall(hub);
                    if (_cancel) break;

                    yield return CollectNextRound();
                    if (_cancel) break;
                }

                if (_cancel)
                {
                    Finish(VpbNetContentPhase.Failed, VpbNetContentFail.Cancelled);
                    yield break;
                }

                if (_pending.Count > 0)
                    LogUtil.LogWarning("[VPB.Net] content fetch stopped after " + MaxRounds
                        + " rounds with " + _pending.Count + " package(s) still unresolved;"
                        + " the scene will load without them");

                Finish(_notOnHub.Count > 0 || _pending.Count > 0
                    ? VpbNetContentPhase.Degraded
                    : VpbNetContentPhase.Ready,
                    _notOnHub.Count > 0 ? VpbNetContentFail.NotOnHub : VpbNetContentFail.None);
            }
            finally
            {
                _running = false;
                _routine = null;
                _current = string.Empty;
            }
        }

        static void BuildWave(HubBrowse hub, Dictionary<string, JSONClass> found)
        {
            _wave.Clear();

            for (int i = 0; i < _query.Count; i++)
            {
                string uid = _query[i];
                JSONClass entry;
                if (!found.TryGetValue(uid, out entry) || entry == null)
                {
                    NoteNotOnHub(uid);
                    continue;
                }

                HubResourcePackage pkg = null;
                try { pkg = new HubResourcePackage(entry, hub, true); }
                catch (Exception e)
                {
                    LogUtil.LogWarning("[VPB.Net] the Hub's answer for " + uid
                        + " could not be read: " + e.Message);
                    pkg = null;
                }
                if (pkg == null)
                {
                    NoteNotOnHub(uid);
                    continue;
                }

                if (!pkg.HasValidDownloadUrl)
                {
                    NoteNotOnHub(uid);
                    continue;
                }

                // Package may have arrived another way since the manifest was built — skip re-download.
                if (!pkg.NeedsDownload)
                {
                    _have++;
                    _installed.Add(uid);
                    continue;
                }

                _spentBytes += pkg.FileSize > 0 ? pkg.FileSize : 0;
                _wave.Add(pkg);
            }
        }

        static void NoteNotOnHub(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            for (int i = 0; i < _notOnHub.Count; i++)
            {
                if (string.Equals(_notOnHub[i], uid, StringComparison.OrdinalIgnoreCase)) return;
            }
            _notOnHub.Add(uid);
        }

        static IEnumerator DownloadWave(HubBrowse hub)
        {
            int n = _wave.Count;
            if (n == 0) yield break;

            _phase = VpbNetContentPhase.Fetching;

            // One refresh for the whole wave — per-file is a full library scan each time (minutes frozen).
            try { hub.DeferRefreshUntilQueueDrains(); }
            catch { }

            long waveBytes = 0;
            for (int i = 0; i < n; i++) waveBytes += _wave[i].FileSize > 0 ? _wave[i].FileSize : 0;
            if (waveBytes > 0)
            {
                uint kib = (uint)(waveBytes / 1024L);
                if (_totalKiB < _doneKiB + kib) _totalKiB = _doneKiB + kib;
            }

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
                    LogUtil.LogWarning("[VPB.Net] " + SafeName(p) + " would not start downloading: " + e.Message);
                }
            }

            uint baseKiB = _doneKiB;
            int haveBase = _have;
            float deadline = Time.realtimeSinceStartup + RoundTimeoutSeconds;

            while (!_cancel)
            {
                int settled = 0;
                double liveBytes = 0.0;
                string current = null;

                for (int i = 0; i < n; i++)
                {
                    HubResourcePackage p = _wave[i];
                    long size = p.FileSize > 0 ? p.FileSize : 0;

                    if (state[i] != 0)
                    {
                        settled++;
                        if (state[i] == 1) liveBytes += size;
                        continue;
                    }

                    float f = 0f;
                    try { f = p.DownloadProgress01; }
                    catch { f = 0f; }
                    if (f < 0f) f = 0f;
                    else if (f > 1f) f = 1f;
                    liveBytes += size * f;
                    if (current == null && f > 0f) current = SafeName(p);
                }

                int done = 0;
                for (int i = 0; i < n; i++) if (state[i] == 1) done++;
                _have = haveBase + done;

                _doneKiB = baseKiB + (uint)(liveBytes / 1024.0);
                if (_totalKiB < _doneKiB) _totalKiB = _doneKiB;
                _current = current ?? (n > 0 ? SafeName(_wave[0]) : string.Empty);

                if (settled >= n) break;
                if (Time.realtimeSinceStartup >= deadline)
                {
                    LogUtil.LogWarning("[VPB.Net] a Hub download has not finished in "
                        + (int)(RoundTimeoutSeconds / 60f) + " minutes; giving up on this batch");
                    Finish(VpbNetContentPhase.Failed, VpbNetContentFail.Timeout);
                    yield break;
                }
                yield return null;
            }

            for (int i = 0; i < n; i++)
            {
                if (state[i] != 1) continue;
                string name = SafeName(_wave[i]);
                if (!string.IsNullOrEmpty(name)) _installed.Add(name);
            }

            _doneKiB = baseKiB + (uint)(waveBytes / 1024L);
            for (int i = 0; i < n; i++)
            {
                _wave[i].OnDownloadSucceeded = null;
                _wave[i].OnDownloadFailed = null;
            }
        }

        static int _installedBeforeWave;

        static IEnumerator WaitForInstall(HubBrowse hub)
        {
            _phase = VpbNetContentPhase.Installing;
            _current = string.Empty;

            // Wait for Hub's deferred library refresh.
            float deadline = Time.realtimeSinceStartup + SettleTimeoutSeconds;
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

        static IEnumerator CollectNextRound()
        {
            if (_installed.Count == 0) yield break;

            int from = _installedBeforeWave;
            _installedBeforeWave = _installed.Count;

            float sliceEnd = Time.realtimeSinceStartup + ScanBudgetPerFrame;

            for (int i = from; i < _installed.Count; i++)
            {
                if (_cancel) yield break;

                string uid = _installed[i];
                HashSet<string> deps = null;
                try { deps = FileManager.GetDependenciesDeep(uid, VpbNetContentContract.MaxDependencyDepth); }
                catch { deps = null; }

                if (deps != null)
                {
                    foreach (string dep in deps)
                    {
                        if (string.IsNullOrEmpty(dep)) continue;
                        if (_visited.Contains(dep)) continue;
                        if (_pending.Count + _visited.Count >= MaxPackages) break;

                        bool satisfied;
                        try { satisfied = FileManager.IsDependencySatisfiedByInstalled(dep); }
                        catch { satisfied = true; }
                        if (satisfied) continue;

                        if (!VpbNetEventCodec.IsSafeIdentifier(dep, VpbNetManifestLimits.MaxUidChars)) continue;
                        if (VpbNetEventCodec.IsPluginReference(dep)) continue;

                        _visited.Add(dep);
                        _pending.Add(dep);
                    }
                }

                // Scan() reads the .var; a whole round in one frame is a visible VR stall.
                if (Time.realtimeSinceStartup >= sliceEnd)
                {
                    yield return null;
                    sliceEnd = Time.realtimeSinceStartup + ScanBudgetPerFrame;
                }
            }

            if (_pending.Count > 0)
            {
                _need += _pending.Count;
                LogUtil.LogWarning("[VPB.Net] the packages that just installed need "
                    + _pending.Count + " more; fetching those too");
            }
        }

        static void NoteDownloadError(string err)
        {
            if (string.IsNullOrEmpty(err)) return;
            LogUtil.LogWarning("[VPB.Net] a Hub download failed: " + err);

            if (err.IndexOf("401", StringComparison.Ordinal) >= 0
                || err.IndexOf("403", StringComparison.Ordinal) >= 0)
            {
                if (_fail == VpbNetContentFail.None) _fail = VpbNetContentFail.NeedsLogin;
                return;
            }
            if (err.StartsWith("save:", StringComparison.Ordinal))
            {
                if (_fail == VpbNetContentFail.None) _fail = VpbNetContentFail.SaveFailed;
                return;
            }
            if (_fail == VpbNetContentFail.None) _fail = VpbNetContentFail.NotOnHub;
        }

        static void Finish(byte phase, byte fail)
        {
            _phase = phase;
            // A download-time reason beats the summary ("sign in" is actionable; "not on Hub" is not).
            if (_fail == VpbNetContentFail.None || fail == VpbNetContentFail.Cancelled) _fail = fail;
            _current = string.Empty;
            _running = false;
        }

        static string SafeName(HubResourcePackage p)
        {
            if (p == null) return string.Empty;
            try { return p.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        static HubBrowse SafeHub()
        {
            try { return HubBrowse.singleton; }
            catch { return null; }
        }

        static bool SafeHubEnabled(HubBrowse hub)
        {
            try { return hub.HubEnabled; }
            catch { return false; }
        }

        public static long BudgetBytes()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetContentMaxMB == null) return 0L;
                int mb = s.NetContentMaxMB.Value;
                if (mb <= 0) return 0L;
                return (long)mb * 1024L * 1024L;
            }
            catch { return 0L; }
        }
    }
}
