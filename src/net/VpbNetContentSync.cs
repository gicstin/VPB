using System;
using System.Collections;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public delegate void VpbNetOfferSend(VpbNetOfferInfo offer);
    public delegate void VpbNetManifestSend(VpbNetManifest manifest);
    public delegate void VpbNetStatusSend(VpbNetContentStatus status);

    // Host loads immediately; joiner catches up. Missing packages must not freeze the room.
    public static class VpbNetContentSync
    {
        public const double StatusIntervalMs = 900.0;
        public const double StatusHeartbeatMs = 4000.0;
        public const double ManifestWaitMs = 12000.0;
        public const int MaxHolds = 4;
        public const double HoldTimeoutMs = 600000.0;
        public const int LoadBusySeconds = 90;
        public const int SettledRepeats = 4;
        public const double OfferAnswerGraceMs = 15000.0;

        public sealed class Hold
        {
            public byte[] Bytes = new byte[VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize];
            public int Len;
            public string PackageUid = string.Empty;
            public string Label = string.Empty;
            public double DeadlineMs;
            public bool Used;
            public bool Running;
            public bool Ready;
        }

        static readonly VpbNetManifest _manifest = new VpbNetManifest();
        static readonly VpbNetContentPlan _plan = new VpbNetContentPlan();

        // Separate from _plan — sharing wiped the scene card when a clothing fetch settled after it.
        static readonly VpbNetContentPlan _holdPlan = new VpbNetContentPlan();
        static readonly VpbNetContentCatalog _catalog = new VpbNetContentCatalog();
        static readonly Hold[] _holds = new Hold[MaxHolds];

        static VpbNetOfferSend _sendOffer;
        static VpbNetManifestSend _sendManifest;
        static VpbNetStatusSend _sendStatus;

        static VpbNetOfferInfo _offer;
        static VpbNetContentStatus _local;
        static VpbNetContentStatus _peer;
        static VpbNetContentStatus _sent;

        static bool _mine;
        static bool _haveOffer;
        static bool _haveManifest;
        static bool _peerKnown;
        static bool _dismissed;
        static bool _loadStarted;
        static bool _fetchStarted;
        static bool _loadPhaseHeld;
        static bool _sceneRuleHeld;
        static byte _phaseBeforeLoad;
        static double _offerAtMs;
        static double _nextStatusMs;
        static double _lastSentMs;
        static int _settledSends;
        static uint _nextOfferId = 1;
        static Coroutine _loadWatch;

        public static bool HasOffer { get { return _haveOffer; } }
        public static bool Mine { get { return _mine; } }
        public static bool Dismissed { get { return _dismissed; } }
        public static VpbNetOfferInfo Offer { get { return _offer; } }
        public static VpbNetContentStatus Local { get { return _local; } }
        public static VpbNetContentStatus Peer { get { return _peer; } }
        public static bool PeerKnown { get { return _peerKnown; } }
        public static bool LoadStarted { get { return _loadStarted; } }
        public static VpbNetContentPlan Plan { get { return _plan; } }

        // Which rule stopped it — the card must not blame the download rule for a scene refusal.
        public static bool SceneRuleHeld { get { return _sceneRuleHeld; } }

        public static bool AwaitingAnswer
        {
            get { return _haveOffer && !_mine && _local.Phase == VpbNetContentPhase.Waiting; }
        }

        public static bool ExchangeLive(double nowMs)
        {
            if (!_haveOffer) return false;
            if (!_mine) return _loadStarted || !VpbNetContentPhase.IsSettled(_local.Phase);
            if (!_peerKnown) return nowMs - _offerAtMs <= OfferAnswerGraceMs;
            return _peer.Phase != VpbNetContentPhase.Refused
                && _peer.Phase != VpbNetContentPhase.Failed;
        }

        public static bool CanRetry
        {
            get
            {
                return _haveOffer && !_mine
                    && (_local.Phase == VpbNetContentPhase.Failed
                        || _local.Phase == VpbNetContentPhase.Refused);
            }
        }

        public static bool CanJoinAnyway
        {
            get
            {
                if (!_haveOffer || _mine || _loadStarted) return false;
                // Nothing missing means Accept already loads it whole — "anyway" would only mislabel it.
                if (_plan.NeedsNothing && _local.Phase == VpbNetContentPhase.Waiting) return false;
                return _local.Phase == VpbNetContentPhase.Waiting
                    || _local.Phase == VpbNetContentPhase.Failed
                    || _local.Phase == VpbNetContentPhase.Refused
                    || _local.Phase == VpbNetContentPhase.Degraded;
            }
        }

        static VpbNetContentSync()
        {
            for (int i = 0; i < _holds.Length; i++) _holds[i] = new Hold();
            _offer.Clear();
            _local.Clear();
            _peer.Clear();
            _sent.Clear();
        }

        public static void SetSenders(VpbNetOfferSend offer, VpbNetManifestSend manifest, VpbNetStatusSend status)
        {
            _sendOffer = offer;
            _sendManifest = manifest;
            _sendStatus = status;
        }

        public static void ResetForSession()
        {
            ClearOffer();
            for (int i = 0; i < _holds.Length; i++) Release(_holds[i]);
            _peer.Clear();
            _peerKnown = false;
            VpbNetContentResolver.Stop();
            VpbNetContentResolver.Reset();
        }

        public static void Shutdown()
        {
            _sendOffer = null;
            _sendManifest = null;
            _sendStatus = null;
            StopLoadWatch();
            ResetForSession();
        }

        static void ClearOffer()
        {
            _offer.Clear();
            _local.Clear();
            _sent.Clear();
            _manifest.Clear();
            _plan.Clear();
            _haveOffer = false;
            _haveManifest = false;
            _mine = false;
            _dismissed = false;
            _loadStarted = false;
            _fetchStarted = false;
            _loadPhaseHeld = false;
            _sceneRuleHeld = false;
            _phaseBeforeLoad = VpbNetContentPhase.Unknown;
            _offerAtMs = 0.0;
            _nextStatusMs = 0.0;
            _lastSentMs = 0.0;
            _settledSends = 0;
        }

        // LoadedSceneName has no extension — CurrentScenePath restores a loadable uid.
        public static bool BeginHostOffer(string scenePath, bool editMode, double nowMs)
        {
            string reason;
            return BeginHostOffer(scenePath, editMode, nowMs, out reason);
        }

        public static bool BeginHostOffer(string scenePath, bool editMode, double nowMs, out string reason)
        {
            reason = null;

            if (_sendOffer == null)
            {
                reason = VPBTranslation.T("net_content.fail.nolink",
                    "The session is not carrying content yet - wait for it to finish connecting.");
                return false;
            }
            if (string.IsNullOrEmpty(scenePath))
            {
                reason = VPBTranslation.T("net_content.fail.nopath",
                    "This scene has no file on disk to point them at - save it under Saves/scene first.");
                return false;
            }

            VpbNetContentResolver.Stop();
            VpbNetContentResolver.Reset();
            ClearOffer();

            string note;
            bool fromPackage = false;
            try { fromPackage = VpbNetContentContract.BuildManifest(scenePath, _manifest, out note); }
            catch (Exception e)
            {
                note = e.Message;
                fromPackage = false;
            }
            if (!string.IsNullOrEmpty(note)) LogUtil.LogWarning("[VPB.Net] content offer: " + note);

            _offer.Clear();
            _offer.OfferId = NextOfferId();
            _offer.Flags = (byte)((fromPackage ? VpbNetOfferInfo.FlagFromPackage : 0)
                | (editMode ? VpbNetOfferInfo.FlagEditMode : 0));
            _offer.ScenePath = scenePath;
            _offer.PackageUid = fromPackage ? VpbNetContentContract.PackageUidOf(scenePath) : string.Empty;
            if (_offer.PackageUid == null) _offer.PackageUid = string.Empty;
            _offer.PackageHash = _offer.PackageUid.Length > 0
                ? VpbNetContentContract.HashFor(_offer.PackageUid) : 0u;
            _offer.Title = VpbNetContentContract.TitleOf(scenePath);
            _offer.ManifestGen = (int)(_offer.OfferId & 0xFFFF);
            _offer.ManifestCount = fromPackage ? _manifest.Count : 0;
            _offer.TotalKiB = fromPackage ? _manifest.TotalKiB : 0u;

            if (!_offer.IsPresent)
            {
                reason = VPBTranslation.T("net_content.fail.undescribable",
                    "That scene cannot be described to the other machine, so they were not invited.");
                LogUtil.LogWarning("[VPB.Net] that scene path cannot be described to the other machine,"
                    + " so they were not invited to it");
                ClearOffer();
                return false;
            }

            _manifest.SetGeneration(_offer.ManifestGen);
            _mine = true;
            _haveOffer = true;
            _haveManifest = fromPackage;
            _offerAtMs = nowMs;

            _local.Clear();
            _local.OfferId = _offer.OfferId;
            _local.Phase = VpbNetContentPhase.Ready;
            _local.Have = _manifest.Count;
            _local.Need = _manifest.Count;
            _local.TotalKiB = _offer.TotalKiB;
            _local.DoneKiB = _offer.TotalKiB;

            _peer.Clear();
            _peer.OfferId = _offer.OfferId;
            _peer.Phase = VpbNetContentPhase.Unknown;
            _peerKnown = false;

            try { _sendOffer(_offer); }
            catch { }
            if (fromPackage && _sendManifest != null)
            {
                try { _sendManifest(_manifest); }
                catch { }
            }

            LogUtil.LogWarning("[VPB.Net] invited the other player to " + _offer.Title
                + (fromPackage
                    ? " (" + _manifest.Count + " packages, " + SizeText(_offer.TotalKiB) + ")"
                    : " - it is not in a package, so they can only join if they already have it"));

            ForceStatusSend(nowMs);
            return true;
        }

        public static void ResendOffer(double nowMs)
        {
            if (!_haveOffer || !_mine || _sendOffer == null) return;
            try { _sendOffer(_offer); }
            catch { }
            if (_haveManifest && _sendManifest != null)
            {
                try { _sendManifest(_manifest); }
                catch { }
            }
            _offerAtMs = nowMs;
        }

        public static void NoteOffer(VpbNetOfferInfo o, double nowMs)
        {
            if (!o.IsPresent) return;
            if (_haveOffer && !_mine && _offer.OfferId == o.OfferId) return;

            // Being offered a scene we are standing in means their copy of our scene state is stale.
            if (AlreadyInOfferedScene(o.ScenePath))
            {
                LogUtil.LogWarning("[VPB.Net] they offered " + VpbNetContentContract.TitleOf(o.ScenePath)
                    + ", which is the scene already open here, so nothing was asked."
                    + " Telling them again where this machine is.");
                VpbNetPresence.AnnounceSceneAgain();
                return;
            }

            VpbNetContentResolver.Stop();
            VpbNetContentResolver.Reset();
            ClearOffer();

            _offer = o;
            _mine = false;
            _haveOffer = true;
            _haveManifest = false;
            _offerAtMs = nowMs;

            _local.Clear();
            _local.OfferId = o.OfferId;
            _local.Phase = VpbNetContentPhase.Checking;
            _local.Need = o.ManifestCount;
            _local.TotalKiB = o.TotalKiB;

            // Clear peer row — a carried "Ready" from the previous scene would sit until the next status.
            _peer.Clear();
            _peer.OfferId = o.OfferId;
            _peerKnown = false;

            LogUtil.LogWarning("[VPB.Net] the other player wants you in " + DisplayTitle()
                + (o.ManifestCount > 0
                    ? " (" + o.ManifestCount + " packages, " + SizeText(o.TotalKiB) + " if you are missing all of it)"
                    : string.Empty));

            ForceStatusSend(nowMs);

            // ManifestCount 0 means none is coming — do not wait for a list.
            if (o.ManifestCount == 0) Evaluate(nowMs);
        }

        public static void NoteManifest(VpbNetManifest incoming, double nowMs)
        {
            if (incoming == null || _mine || !_haveOffer) return;
            if (incoming.Generation != _offer.ManifestGen) return;

            _manifest.Clear();
            for (int i = 0; i < incoming.Count; i++) _manifest.TryAdd(incoming.Uid(i), incoming.Role(i));
            _manifest.SetGeneration(incoming.Generation);
            _manifest.AddKiB(incoming.TotalKiB);
            _haveManifest = true;

            Evaluate(nowMs);
        }

        // Drop status for a different offer — never paint their old Ready onto this scene's row.
        public static void NoteStatus(VpbNetContentStatus s)
        {
            if (_haveOffer && s.OfferId != _offer.OfferId)
            {
                _peer.Clear();
                _peer.OfferId = _offer.OfferId;
                _peerKnown = false;
                return;
            }

            _peer = s;
            _peerKnown = s.Phase != VpbNetContentPhase.Unknown;
        }

        // Their "go" nudges a machine that is already ready; it never answers a prompt standing here.
        public static void NoteGo(uint offerId)
        {
            if (!_haveOffer || _mine || _offer.OfferId != offerId) return;
            if (_loadStarted) return;
            if (VpbNetContentPhase.CanLoad(_local.Phase)) LoadOffered();
        }

        static byte Decide(byte domain)
        {
            try { return VpbNetRulebook.Decide(domain, VpbNetRuleAxis.Control); }
            catch { return VpbNetRuleLevel.Blocked; }
        }

        // Scene rule gates load; Content only who pays disk/bandwidth.
        static void Evaluate(double nowMs)
        {
            if (!_haveOffer || _mine) return;

            byte scene = Decide(VpbNetRuleDomain.Scene);
            _sceneRuleHeld = scene != VpbNetRuleLevel.Allowed;

            if (scene == VpbNetRuleLevel.Blocked)
            {
                Settle(VpbNetContentPhase.Refused, VpbNetContentFail.Blocked, nowMs);
                LogUtil.LogWarning("[VPB.Net] did not load " + DisplayTitle()
                    + " - your session rules have \"load scene content on my machine\" set to blocked");
                return;
            }

            _plan.Clear();
            if (_haveManifest && _manifest.Count > 0)
            {
                _plan.Build(_manifest, _catalog);
            }
            else if (_offer.PackageUid.Length > 0)
            {
                uint hash;
                if (!_catalog.TryResolveExact(_offer.PackageUid, out hash))
                    _plan.AddSeed(_offer.PackageUid, VpbNetContractRole.Scene);
            }
            else if (!SceneExistsLocally())
            {
                Settle(VpbNetContentPhase.Failed, VpbNetContentFail.NoScene, nowMs);
                return;
            }

            if (_plan.NeedsNothing)
            {
                // Nothing to fetch, so the only question left is whether their scene may load here.
                if (scene == VpbNetRuleLevel.Ask)
                {
                    _local.Have = 0;
                    _local.Need = 0;
                    _local.TotalKiB = 0;
                    Settle(VpbNetContentPhase.Waiting, VpbNetContentFail.None, nowMs);
                    return;
                }
                _local.Have = _plan.Present;
                _local.Need = _plan.Present;
                Settle(VpbNetContentPhase.Ready, VpbNetContentFail.None, nowMs);
                LoadOffered();
                return;
            }

            _local.Need = _plan.Count;
            _local.Have = 0;
            _local.TotalKiB = EstimateKiB();

            long budget = VpbNetContentResolver.BudgetBytes();
            if (budget <= 0)
            {
                Settle(VpbNetContentPhase.Failed, VpbNetContentFail.TooBig, nowMs);
                return;
            }
            if ((long)_local.TotalKiB * 1024L > budget)
            {
                Settle(VpbNetContentPhase.Failed, VpbNetContentFail.TooBig, nowMs);
                LogUtil.LogWarning("[VPB.Net] that scene wants " + SizeText(_local.TotalKiB)
                    + " and Net.ContentMaxMB allows " + (budget / (1024L * 1024L))
                    + " MB, so nothing was downloaded");
                return;
            }

            byte content = Decide(VpbNetRuleDomain.Content);
            if (content == VpbNetRuleLevel.Blocked)
            {
                _sceneRuleHeld = false;
                Settle(VpbNetContentPhase.Refused, VpbNetContentFail.Blocked, nowMs);
                LogUtil.LogWarning("[VPB.Net] refused to fetch content for " + DisplayTitle()
                    + " - your session rules have that set to blocked");
                return;
            }

            // One answer covers both questions - the card names the scene, the count and the size.
            if (scene == VpbNetRuleLevel.Ask || content == VpbNetRuleLevel.Ask)
            {
                Settle(VpbNetContentPhase.Waiting, VpbNetContentFail.None, nowMs);
                return;
            }

            StartFetch(nowMs);
        }

        static void StartFetch(double nowMs)
        {
            _fetchStarted = true;
            _local.Phase = VpbNetContentPhase.Fetching;
            _local.Fail = VpbNetContentFail.None;
            ForceStatusSend(nowMs);

            if (!VpbNetContentResolver.Begin(_plan, EstimateKiB(), VpbNetContentResolver.BudgetBytes()))
            {
                MirrorResolver();
                if (!VpbNetContentPhase.IsSettled(_local.Phase))
                    Settle(VpbNetContentPhase.Failed, VpbNetContentResolver.Fail, nowMs);
                return;
            }

            LogUtil.LogWarning("[VPB.Net] fetching " + _plan.Count
                + " package(s) for " + DisplayTitle() + " - " + SizeText(EstimateKiB()));
        }

        public static void Accept(double nowMs)
        {
            if (!_haveOffer || _mine) return;
            if (_local.Phase == VpbNetContentPhase.Fetching
                || _local.Phase == VpbNetContentPhase.Installing) return;
            if (_plan.NeedsNothing)
            {
                Settle(VpbNetContentPhase.Ready, VpbNetContentFail.None, nowMs);
                LoadOffered();
                return;
            }
            StartFetch(nowMs);
        }

        public static void JoinAnyway()
        {
            if (!_haveOffer || _mine || _loadStarted) return;
            VpbNetContentResolver.Cancel();
            _local.Phase = VpbNetContentPhase.Degraded;
            _local.Fail = _local.Fail == VpbNetContentFail.None
                ? VpbNetContentFail.NotOnHub : _local.Fail;
            LoadOffered();
        }

        public static void Decline(double nowMs)
        {
            if (!_haveOffer || _mine) return;
            VpbNetContentResolver.Cancel();
            Settle(VpbNetContentPhase.Refused, VpbNetContentFail.Cancelled, nowMs);
            LogUtil.LogWarning("[VPB.Net] declined " + DisplayTitle()
                + " - they carry on there, you stay where you are");
        }

        public static void Retry(double nowMs)
        {
            if (!_haveOffer || _mine) return;
            VpbNetContentResolver.Stop();
            VpbNetContentResolver.Reset();
            _local.Fail = VpbNetContentFail.None;
            _fetchStarted = false;
            Evaluate(nowMs);
        }

        public static void Dismiss()
        {
            _dismissed = true;
        }

        public static bool AlreadyInOfferedScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;

            string here = null;
            try { here = VpbNetContentContract.CurrentSceneUid(); }
            catch { here = null; }
            if (string.IsNullOrEmpty(here)) return false;

            return VpbNetContentContract.SameScenePath(here, scenePath);
        }

        static void LoadOffered()
        {
            if (_loadStarted || !_haveOffer) return;
            _loadStarted = true;

            string path = _offer.ScenePath;
            bool edit = _offer.EditMode;

            LogUtil.LogWarning("[VPB.Net] loading " + DisplayTitle()
                + " - staying in the room while it loads, so nobody has to press Join again");

            try { VpbNetSceneLaunchGuard.AllowNext(); }
            catch { }

            // Busy before LoadSceneAs — that call is what stops this machine answering.
            try { VpbNetBusy.Begin(VpbNetBusyKind.Scene, LoadBusySeconds); }
            catch { }

            BeginLoadPhase(NowMs());
            StartLoadWatch();

            bool ok = false;
            try { ok = SceneLoadingUtils.LoadSceneAs(path, false, edit); }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] that scene would not load: " + e.Message);
                ok = false;
            }

            if (!ok)
            {
                StopLoadWatch();
                EndLoadPhase();
                try { VpbNetBusy.End(); }
                catch { }
                _loadStarted = false;
                _local.Phase = VpbNetContentPhase.Failed;
                _local.Fail = VpbNetContentFail.NoScene;
            }
        }

        // Ready ≠ standing in the scene. Publish Loading before LoadSceneAs or the card stays green.
        static void BeginLoadPhase(double nowMs)
        {
            if (!_haveOffer || _loadPhaseHeld) return;
            if (_local.Phase == VpbNetContentPhase.Loading) return;

            _phaseBeforeLoad = _local.Phase;
            _loadPhaseHeld = true;
            _local.Phase = VpbNetContentPhase.Loading;
            _local.Current = string.Empty;
            ForceStatusSend(nowMs);
        }

        // No clock: coroutine arms the next send; session Tick publishes next frame.
        static void EndLoadPhase()
        {
            if (!_loadPhaseHeld) return;
            _loadPhaseHeld = false;
            if (_local.Phase != VpbNetContentPhase.Loading) return;

            _local.Phase = VpbNetContentPhase.IsSettled(_phaseBeforeLoad)
                ? _phaseBeforeLoad : VpbNetContentPhase.Ready;
            _local.Current = string.Empty;
            _nextStatusMs = 0.0;
        }

        static double NowMs()
        {
            try { return VpbNetRulebook.LastTickMs; }
            catch { return 0.0; }
        }

        // Launch-guard "bring them with me" closes busy here — do not lean on overrun grace.
        public static void WatchLocalLoad()
        {
            BeginLoadPhase(NowMs());
            StartLoadWatch();
        }

        static void StartLoadWatch()
        {
            StopLoadWatch();
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) _loadWatch = sc.StartCoroutine(LoadWatchRoutine());
            }
            catch { _loadWatch = null; }
        }

        static void StopLoadWatch()
        {
            if (_loadWatch == null) return;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) sc.StopCoroutine(_loadWatch);
            }
            catch { }
            _loadWatch = null;
        }

        static IEnumerator LoadWatchRoutine()
        {
            float startDeadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < startDeadline && !SceneLoadBusy()) yield return null;

            float endDeadline = Time.realtimeSinceStartup + 600f;
            while (Time.realtimeSinceStartup < endDeadline && SceneLoadBusy()) yield return null;

            _loadWatch = null;
            EndLoadPhase();
            try { VpbNetBusy.End(); }
            catch { }
        }

        static bool SceneLoadBusy()
        {
            try { return LogUtil.IsSceneLoadActive() || LogUtil.IsSceneLoading(); }
            catch { return false; }
        }

        // Park the event, fetch the package, replay the same bytes.
        public static bool HoldForAsset(string packageUid, string label,
            byte[] buf, int offset, int len, double nowMs)
        {
            if (string.IsNullOrEmpty(packageUid) || buf == null || len <= 0) return false;
            if (offset < 0 || offset + len > buf.Length) return false;

            byte level;
            try { level = VpbNetRulebook.Decide(VpbNetRuleDomain.Content, VpbNetRuleAxis.Control); }
            catch { return false; }
            if (level != VpbNetRuleLevel.Allowed) return false;
            if (VpbNetContentResolver.BudgetBytes() <= 0) return false;

            for (int i = 0; i < _holds.Length; i++)
            {
                Hold h = _holds[i];
                if (!h.Used) continue;
                if (string.Equals(h.PackageUid, packageUid, StringComparison.OrdinalIgnoreCase))
                {
                    if (h.Ready) return false;
                    CopyInto(h, buf, offset, len, label, nowMs);
                    return true;
                }
            }

            for (int i = 0; i < _holds.Length; i++)
            {
                Hold h = _holds[i];
                if (h.Used) continue;
                h.PackageUid = packageUid;
                CopyInto(h, buf, offset, len, label, nowMs);
                h.Used = true;
                h.Running = false;
                h.Ready = false;
                LogUtil.LogWarning("[VPB.Net] " + (string.IsNullOrEmpty(label) ? packageUid : label)
                    + " is not installed here; fetching it and applying it when it lands");
                return true;
            }

            return false;
        }

        // Same queue, no parked message — clothing/hair resync whole.
        public static bool WantAsset(string packageUid, string label, double nowMs)
        {
            if (string.IsNullOrEmpty(packageUid)) return false;

            byte level;
            try { level = VpbNetRulebook.Decide(VpbNetRuleDomain.Content, VpbNetRuleAxis.Control); }
            catch { return false; }
            if (level != VpbNetRuleLevel.Allowed) return false;
            if (VpbNetContentResolver.BudgetBytes() <= 0) return false;

            for (int i = 0; i < _holds.Length; i++)
            {
                if (!_holds[i].Used) continue;
                if (string.Equals(_holds[i].PackageUid, packageUid, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            for (int i = 0; i < _holds.Length; i++)
            {
                Hold h = _holds[i];
                if (h.Used) continue;
                h.PackageUid = packageUid;
                h.Label = label ?? string.Empty;
                h.Len = 0;
                h.DeadlineMs = nowMs + HoldTimeoutMs;
                h.Used = true;
                h.Running = false;
                h.Ready = false;
                LogUtil.LogWarning("[VPB.Net] " + packageUid + " is not installed here; fetching it"
                    + " so the next resync can show " + (string.IsNullOrEmpty(label) ? "it" : label));
                return true;
            }
            return false;
        }

        static void CopyInto(Hold h, byte[] buf, int offset, int len, string label, double nowMs)
        {
            if (len > h.Bytes.Length) len = h.Bytes.Length;
            Buffer.BlockCopy(buf, offset, h.Bytes, 0, len);
            h.Len = len;
            h.Label = label ?? string.Empty;
            h.DeadlineMs = nowMs + HoldTimeoutMs;
        }

        public static int TryTakeReady(byte[] dst)
        {
            if (dst == null) return 0;
            for (int i = 0; i < _holds.Length; i++)
            {
                Hold h = _holds[i];
                if (!h.Used || !h.Ready) continue;

                int len = h.Len;
                if (len > dst.Length) len = dst.Length;
                Buffer.BlockCopy(h.Bytes, 0, dst, 0, len);
                Release(h);
                return len;
            }
            return 0;
        }

        static void Release(Hold h)
        {
            h.Used = false;
            h.Running = false;
            h.Ready = false;
            h.Len = 0;
            h.PackageUid = string.Empty;
            h.Label = string.Empty;
            h.DeadlineMs = 0.0;
        }

        static void PumpHolds(double nowMs)
        {
            Hold running = null;
            for (int i = 0; i < _holds.Length; i++)
            {
                Hold h = _holds[i];
                if (!h.Used) continue;

                if (!h.Ready && nowMs >= h.DeadlineMs)
                {
                    LogUtil.LogWarning("[VPB.Net] gave up fetching " + h.PackageUid
                        + "; that change will not appear on this machine");
                    Release(h);
                    continue;
                }
                if (h.Running) running = h;
            }

            if (running != null)
            {
                if (VpbNetContentResolver.Busy) return;
                bool installed = SatisfiedNow(running.PackageUid);
                running.Running = false;
                if (installed)
                {
                    if (running.Len <= 0)
                    {
                        LogUtil.LogWarning("[VPB.Net] " + running.PackageUid
                            + " installed; it will appear on the next resync");
                        Release(running);
                        return;
                    }
                    running.Ready = true;
                    LogUtil.LogWarning("[VPB.Net] " + running.PackageUid
                        + " installed; applying the change that was waiting on it");
                }
                else
                {
                    LogUtil.LogWarning("[VPB.Net] " + running.PackageUid
                        + " could not be fetched, so that change stays as it was");
                    Release(running);
                }
                return;
            }

            // Never start an asset fetch on top of a scene fetch — one Hub queue, one progress bar.
            if (VpbNetContentResolver.Busy) return;
            if (_haveOffer && !_mine && !VpbNetContentPhase.IsSettled(_local.Phase)) return;

            for (int i = 0; i < _holds.Length; i++)
            {
                Hold h = _holds[i];
                if (!h.Used || h.Ready || h.Running) continue;

                if (SatisfiedNow(h.PackageUid))
                {
                    if (h.Len <= 0) Release(h);
                    else h.Ready = true;
                    return;
                }

                _holdPlan.Clear();
                _holdPlan.AddSeed(h.PackageUid, VpbNetContractRole.Look);
                if (!VpbNetContentResolver.Begin(_holdPlan, 0u, VpbNetContentResolver.BudgetBytes()))
                {
                    Release(h);
                    return;
                }
                h.Running = true;
                return;
            }
        }

        static bool SatisfiedNow(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            try { return FileManager.IsDependencySatisfiedByInstalled(uid); }
            catch { return false; }
        }

        public static void Tick(double nowMs)
        {
            PumpHolds(nowMs);

            if (!_haveOffer)
            {
                MaybeSendStatus(nowMs);
                return;
            }

            if (!_mine)
            {
                if (_fetchStarted) MirrorResolver();

                if (!_haveManifest
                    && _local.Phase == VpbNetContentPhase.Checking
                    && _offer.ManifestCount > 0
                    && nowMs - _offerAtMs > ManifestWaitMs)
                {
                    LogUtil.LogWarning("[VPB.Net] the package list for " + DisplayTitle()
                        + " never arrived; working from the scene's own package instead");
                    _haveManifest = false;
                    _offer.ManifestCount = 0;
                    Evaluate(nowMs);
                }

                if (!_loadStarted && VpbNetContentPhase.CanLoad(_local.Phase) && _fetchStarted)
                    LoadOffered();
            }

            MaybeSendStatus(nowMs);
        }

        static void MirrorResolver()
        {
            byte phase = VpbNetContentResolver.Phase;
            if (phase == VpbNetContentPhase.Unknown) return;

            _local.Phase = phase;
            _local.Fail = VpbNetContentResolver.Fail;
            _local.Have = VpbNetContentResolver.Have;
            if (VpbNetContentResolver.Need > _local.Need) _local.Need = VpbNetContentResolver.Need;
            _local.DoneKiB = VpbNetContentResolver.DoneKiB;
            if (VpbNetContentResolver.TotalKiB > _local.TotalKiB)
                _local.TotalKiB = VpbNetContentResolver.TotalKiB;
            _local.Current = VpbNetContentResolver.Current ?? string.Empty;
        }

        static void Settle(byte phase, byte fail, double nowMs)
        {
            _local.Phase = phase;
            _local.Fail = fail;
            _local.Current = string.Empty;
            ForceStatusSend(nowMs);
        }

        static void ForceStatusSend(double nowMs)
        {
            _nextStatusMs = 0.0;
            MaybeSendStatus(nowMs);
        }

        static void MaybeSendStatus(double nowMs)
        {
            if (_sendStatus == null) return;
            if (nowMs < _nextStatusMs) return;

            bool changed = !_local.SameAs(_sent);
            bool heartbeat = nowMs - _lastSentMs >= StatusHeartbeatMs;
            if (!changed && !heartbeat) return;

            // Bounded Ready repeats cover reconnect; unbounded is a datagram forever.
            if (changed) _settledSends = 0;
            else if (VpbNetContentPhase.IsSettled(_local.Phase))
            {
                if (_settledSends >= SettledRepeats) return;
                _settledSends++;
            }

            _nextStatusMs = nowMs + StatusIntervalMs;
            _lastSentMs = nowMs;
            _sent = _local;

            try { _sendStatus(_local); }
            catch { }
        }

        public static string DisplayTitle()
        {
            if (!_haveOffer) return string.Empty;
            if (!string.IsNullOrEmpty(_offer.Title)) return _offer.Title;
            return VpbNetContentContract.TitleOf(_offer.ScenePath);
        }

        public static string SizeText(uint kib)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(16);
            VpbNetContentStatus.AppendSize(sb, kib);
            return sb.ToString();
        }

        public static uint EstimateKiB()
        {
            if (!_haveOffer) return 0;
            if (_plan.Checked <= 0 || _plan.Count <= 0) return _offer.TotalKiB;
            if (_offer.TotalKiB == 0) return 0;

            // Host priced the whole closure; scale by missing count.
            double share = (double)_plan.Count / (_plan.Checked > 0 ? _plan.Checked : 1);
            double kib = _offer.TotalKiB * share;
            if (kib < 0.0) kib = 0.0;
            if (kib > uint.MaxValue) kib = uint.MaxValue;
            return (uint)kib;
        }

        static bool SceneExistsLocally()
        {
            if (!_haveOffer || string.IsNullOrEmpty(_offer.ScenePath)) return false;
            try { return FileManager.FileExists(_offer.ScenePath); }
            catch { return false; }
        }

        // Random, not sequential — a stale offer from a previous session/host must not match this one.
        static uint NextOfferId()
        {
            _nextOfferId++;
            uint salt;
            try { salt = VpbRandom.NextUInt(); }
            catch { salt = _nextOfferId * 2654435761u; }
            uint id = salt ^ (_nextOfferId << 16);
            if (id == 0 || id == _offer.OfferId) id = _nextOfferId | 0x40000000u;
            return id;
        }
    }
}
