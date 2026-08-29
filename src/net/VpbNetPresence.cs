using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using VPB.src.util;
using VpbNet;

namespace VPB
{
    public static class VpbNetPresence
    {
        const string HostName = "VPBNetPresence";

        static GameObject _host;
        static VpbNetPresenceRunner _runner;
        static float _nextPoll;
        static bool _startedAsHost;

        public static bool IsActive { get { return _runner != null; } }

        public static VpbNetSessionState State = VpbNetSessionState.Idle;
        public static VpbNetDropReason Reason = VpbNetDropReason.None;
        public static string Status = "not in a session";
        public static string Hint = string.Empty;
        public static string ContentWarning = string.Empty;
        public static string ReasonText = string.Empty;
        public static string Invite = string.Empty;
        public static string PeerName = string.Empty;
        public static string LocalName = string.Empty;
        public static string Room = string.Empty;
        public static string Address = string.Empty;
        public static bool AsHost;
        public static bool PeerBusy;
        public static bool LocalBusy;
        public static bool PeerUp;
        public static int PeerId = -1;
        public static string MyAvatar = string.Empty;
        public static string PeerAvatar = string.Empty;
        public static string PendingAvatar = string.Empty;
        public static bool AvatarClaimDenied;
        public static string ClaimDenyReason = string.Empty;
        public static bool SeatPickWanted;
        public static uint ClaimRevision;
        public static bool ScenesMatch;
        /// <summary>Local SceneState uid. Empty until a scene has loaded.</summary>
        public static string LocalScene = string.Empty;
        /// <summary>Last SceneState uid the peer published.</summary>
        public static string PeerScene = string.Empty;
        public static bool HavePeerScene;

        public static byte PeerClaimRule
        {
            get
            {
                if (!VpbNetRulebook.PeerPublished) return VpbNetRuleLevel.Allowed;
                return VpbNetRulebook.Peer.Effective(VpbNetRuleDomain.AvatarClaim,
                    VpbNetRuleAxis.Control);
            }
        }

        public static void ClaimAvatar(string uid)
        {
            if (_runner != null) _runner.ClaimAvatar(uid);
        }

        public static void Spectate()
        {
            ClaimAvatar(string.Empty);
        }

        /// <summary>Dual-pose jump — keyframe so peer does not interpolate across the room.</summary>
        public static void MarkLocalPoseJump()
        {
            if (_runner != null) _runner.MarkLocalPoseJump();
        }

        public static bool ShareObjects
        {
            get
            {
                try
                {
                    Settings s = Settings.Instance;
                    return s != null && s.NetSyncAtoms != null && s.NetSyncAtoms.Value;
                }
                catch { return false; }
            }
        }

        public static bool ShareObjectsLive;

        // Session-panel live toggle — no reconnect (used to live on Dev tab).
        public static void ToggleShareObjects()
        {
            bool want = !ShareObjects;
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetSyncAtoms == null) return;
                s.NetSyncAtoms.Value = want;
                Settings.SaveConfig();
            }
            catch { }

            LogUtil.LogWarning("[VPB.Net] sharing objects being added and deleted is now "
                + (want ? "ON" : "OFF")
                + (want
                    ? " - the other player must switch it on too, and a subscene they add will be loaded from this machine's own library"
                    : string.Empty));

            if (_runner != null) _runner.ReloadShareObjects();
        }

        public static bool CollisionsOff { get { return VpbNetCollisionGuard.ForcedOff; } }

        // Hold auto collision guard open — pose throws shove overlapping ragdolls.
        public static void ToggleCollisions()
        {
            VpbNetCollisionGuard.SetForcedOff(!VpbNetCollisionGuard.ForcedOff);
        }

        public static void RepairBodies()
        {
            int n = 0;
            try { VpbNetAvatarRoster.Poll(); }
            catch { }
            for (int i = 0; i < VpbNetAvatarRoster.Count; i++)
            {
                Atom a = VpbNetAvatarRoster.AtomAt(i);
                if (a == null) continue;
                VpbNetBodyGuard.RepairNow(a, "you pressed Repair");
                n++;
            }
            LogUtil.LogWarning("[VPB.Net] repaired " + n + " person(s): velocities zeroed, collisions off"
                + " while they settle, VaM's own physics reset run on each");
        }

        public static bool IsMine(string uid)
        {
            return !string.IsNullOrEmpty(uid) && string.Equals(uid, MyAvatar, StringComparison.Ordinal);
        }

        public static bool IsPeers(string uid)
        {
            return !string.IsNullOrEmpty(uid) && string.Equals(uid, PeerAvatar, StringComparison.Ordinal);
        }

        // False on older builds that predate scene invites — not an error.
        public static bool PeerTakesContent
        {
            get
            {
                try { return _runner != null && _runner.PeerTakesContent; }
                catch { return false; }
            }
        }

        public static bool InviteToScene(string scenePath, bool editMode)
        {
            try
            {
                if (_runner == null) return false;
                return _runner.InviteToScene(scenePath, editMode);
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] could not invite them to that scene: " + e.Message);
                return false;
            }
        }

        // Null if unsaved or unindexed — UI hides invite rather than a failing button.
        public static string InvitableScenePath
        {
            get
            {
                try { return VpbNetContentContract.CurrentScenePath(); }
                catch { return null; }
            }
        }

        public static bool InviteToCurrentScene()
        {
            string path = InvitableScenePath;
            if (string.IsNullOrEmpty(path))
            {
                LogUtil.LogWarning("[VPB.Net] this scene cannot be named to the other machine,"
                    + " so they cannot be invited to it - save it under Saves/scene first");
                return false;
            }
            return InviteToScene(path, false);
        }

        public static bool Wanted
        {
            get
            {
                try
                {
                    Settings s = Settings.Instance;
                    if (s == null) return false;
                    bool host = s.NetHostSession != null && s.NetHostSession.Value;
                    bool join = s.NetJoinSession != null && s.NetJoinSession.Value;
                    return host || join;
                }
                catch { return false; }
            }
        }

        public static void Poll()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 1f;

            bool want = Wanted;
            if (want)
            {
                bool wantHost = SettingOn(s => s.NetHostSession);
                if (_runner != null && _startedAsHost != wantHost)
                    Stop("role change");
                if (_runner == null) Start();
                return;
            }
            if (_runner != null) Stop("session toggled off");
        }

        public static void GenerateRoomCode()
        {
            AddHostRoom();
        }

        public static void AddHostRoom()
        {
            string code = MakeRoomCode();
            if (code == null) return;
            if (!VpbNetRoomBookStore.AddGeneratedHost(code))
            {
                LogUtil.LogWarning("[VPB.Net] no room was added - forget an unlocked room first, or unlock one");
                return;
            }
            LogUtil.LogWarning("[VPB.Net] new room code " + VpbNetRedact.Code(code)
                + " - the panel has it in full; send the whole thing to the other person."
                + " It is not case sensitive and hyphens are optional");
        }

        public static void ReplaceSelectedHost()
        {
            if (RoomCodeLocked)
            {
                LogUtil.LogWarning("[VPB.Net] the room code is protected, so it was not replaced - press Unlock first if you really want a new one");
                return;
            }
            string code = MakeRoomCode();
            if (code == null) return;
            if (!VpbNetRoomBookStore.ReplaceSelectedHost(code))
            {
                LogUtil.LogWarning("[VPB.Net] the room code is protected, so it was not replaced");
                return;
            }
            LogUtil.LogWarning("[VPB.Net] replaced room code " + VpbNetRedact.Code(code)
                + " - the panel has it in full; send the whole thing to the other person."
                + " It is not case sensitive and hyphens are optional");
        }

        static string MakeRoomCode()
        {
            try
            {
                byte[] entropy = new byte[VpbNetRoomCode.EntropyBytes];
                if (!VpbNetBrokerLink.FillRandom(entropy))
                {
                    LogUtil.LogError("[VPB.Net] the system random number generator is unavailable, so no room code was generated - do not invent one by hand");
                    return null;
                }

                string code = VpbNetRoomCode.FromEntropy(entropy, 0);
                Array.Clear(entropy, 0, entropy.Length);
                return code;
            }
            catch { return null; }
        }

        public static bool RoomCodeLocked
        {
            get
            {
                try { return VpbNetRoomBookStore.SelectedHostLocked; }
                catch { return false; }
            }
        }

        public static void ToggleRoomCodeLock()
        {
            try
            {
                bool next = !RoomCodeLocked;
                if (!next)
                {
                    VpbNetRoomBookStore.SetSelectedLock(false);
                    LogUtil.LogWarning("[VPB.Net] room code released - New still adds a room, Replace is allowed again");
                    return;
                }

                string code = VpbNetRoomBookStore.SelectedHostCode;
                if (!VpbNetRoomCode.IsWellFormed(code))
                {
                    LogUtil.LogWarning("[VPB.Net] there is no room code to protect - press New first");
                    return;
                }

                VpbNetRoomBookStore.SetSelectedLock(true);
                LogUtil.LogWarning("[VPB.Net] room code protected - this room cannot be replaced until you press Unlock. New still adds another room.");
            }
            catch { }
        }

        public static void Tick()
        {
            if (_runner != null) _runner.Pump();
        }

        public static void Host()
        {
            VpbNetRoomBookStore.ApplyHostToActive();
            EnsureRoomCode();
            WriteBool("host", true);
            WriteBool("join", false);
            WriteBool("ui", true);
        }

        public static void Join()
        {
            WriteBool("join", true);
            WriteBool("host", false);
            WriteBool("ui", true);
        }

        public static void EnsureRoomCode()
        {
            try
            {
                string host = VpbNetRoomBookStore.SelectedHostCode;
                if (VpbNetRoomCode.IsWellFormed(host))
                {
                    VpbNetRoomBookStore.ApplyHostToActive();
                    return;
                }
                AddHostRoom();
            }
            catch { }
        }

        // Shared dial gate — Join UI, config, and open used to disagree and silently never connect.
        public static string JoinBlockedReason()
        {
            if (VpbNetTransportChoice.IsSteam()) return null;

            string addr = string.Empty;
            string rv = string.Empty;
            string room = string.Empty;
            bool discover = true;
            try
            {
                Settings s = Settings.Instance;
                if (s != null)
                {
                    if (s.NetLanAddress != null && s.NetLanAddress.Value != null) addr = s.NetLanAddress.Value.Trim();
                    if (s.NetRendezvousAddress != null && s.NetRendezvousAddress.Value != null) rv = s.NetRendezvousAddress.Value.Trim();
                    if (s.NetLanRoomCode != null && s.NetLanRoomCode.Value != null) room = s.NetLanRoomCode.Value.Trim();
                    if (s.NetLanDiscovery != null) discover = s.NetLanDiscovery.Value;
                }
            }
            catch { }

            if (addr.Length > 0) return null;
            if (rv.Length > 0) return null;
            if (VpbNetInviteCode.LooksLikeInvite(room)) return null;
            if (discover && VpbNetRoomCode.IsWellFormed(room)) return null;

            if (discover)
                return "join has nothing to dial: type the room code the host generated and it will be found on this"
                    + " subnet, or paste their invite. \"" + room + "\" is not a generated code - "
                    + VpbNetRoomCode.Explain(room);

            return "join needs Net.LanAddress set to the address the host printed, an invite pasted into"
                + " Net.LanRoomCode, or Net.RendezvousAddress set to a rendezvous you were given."
                + " Turning Net.LanDiscovery on would let a room code alone be enough on this subnet.";
        }

        public static bool ApplyJoinText(string raw)
        {
            if (!VpbNetRoomBookStore.ApplyJoinToActive(raw)) return false;
            try
            {
                Settings s = Settings.Instance;
                if (s != null && VpbNetInviteCode.LooksLikeInvite(raw) && s.NetLanAddress != null)
                    s.NetLanAddress.Value = string.Empty;
            }
            catch { }
            return true;
        }

        public static bool PasteJoinFromClipboard()
        {
            string clip = null;
            try { clip = GUIUtility.systemCopyBuffer; }
            catch { }
            return ApplyJoinText(clip);
        }

        public static void Leave()
        {
            WriteBool("host", false);
            WriteBool("join", false);
        }

        public static void KickPeer()
        {
            if (_runner != null) _runner.Kick();
        }

        public static void RequestResync()
        {
            if (_runner != null) _runner.RequestResync();
        }

        public static void CopyInvite()
        {
            string blob = Invite;
            if (string.IsNullOrEmpty(blob))
            {
                try
                {
                    Settings s = Settings.Instance;
                    if (s != null && s.NetLanAddress != null) blob = s.NetLanAddress.Value;
                }
                catch { }
            }
            if (string.IsNullOrEmpty(blob)) return;
            try { GUIUtility.systemCopyBuffer = blob; }
            catch { }
            // It is on the clipboard, which is where it was wanted. The log is not private.
            LogUtil.LogWarning("[VPB.Net] copied the invite to the clipboard - paste it to the other player");
        }

        // Steam joiners need the grouped 12-char code, not the broker invite blob.
        public static bool CopyRoomCode()
        {
            string raw = null;
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetLanRoomCode != null) raw = s.NetLanRoomCode.Value;
            }
            catch { }
            string norm = VpbNetRoomCode.Normalize(raw);
            if (norm == null) return false;
            string grouped = VpbNetRoomCode.Group(norm);
            try { GUIUtility.systemCopyBuffer = grouped; }
            catch { return false; }
            LogUtil.LogWarning("[VPB.Net] copied the room code to the clipboard - paste it to the other player");
            return true;
        }

        // Log only — panel/clipboard hold the real thing.
        public static string DescribeInviteForLog(string invite)
        {
            if (VpbNetTransportChoice.IsSteam())
            {
                if (string.IsNullOrEmpty(invite)) return "opening the room on Steam";
                return "the joiner types this room code, picks Steam, and presses Join - nothing else";
            }
            if (string.IsNullOrEmpty(invite)) return "waiting for an address to publish";
            if (invite.StartsWith("rv:", StringComparison.OrdinalIgnoreCase))
                return "the joiner uses the same room code and sets Net.RendezvousAddress to "
                    + VpbNetRedact.Endpoint(invite.Substring(3));
            if (VpbNetInviteCode.LooksLikeInvite(invite))
                return "the joiner pastes the invite into Net.LanRoomCode and leaves Net.LanAddress EMPTY";
            return "the joiner sets Net.LanAddress = " + VpbNetRedact.Endpoint(invite)
                + " and matches the room code";
        }

        static void WriteBool(string which, bool v)
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null) return;
                if (which == "host" && s.NetHostSession != null) s.NetHostSession.Value = v;
                else if (which == "join" && s.NetJoinSession != null) s.NetJoinSession.Value = v;
                else if (which == "ui" && s.NetSessionUi != null) s.NetSessionUi.Value = v;
            }
            catch { }
        }

        static void Start()
        {
            if (_runner != null) return;

            if (!VpbNetBrokerLink.MultiplayerEnabled)
            {
                LogUtil.LogError("[VPB.Net] session needs Net.Enabled = true (kill switch is on)");
                Leave();
                _nextPoll = Time.realtimeSinceStartup + 5f;
                return;
            }

            bool asHost = SettingOn(s => s.NetHostSession);
            string blocked = VpbNetTransportChoice.BlockedReason();
            if (blocked != null)
            {
                LogUtil.LogError("[VPB.Net] " + blocked);
                Leave();
                _nextPoll = Time.realtimeSinceStartup + 5f;
                return;
            }
            if (!asHost)
            {
                string refused = JoinBlockedReason();
                if (refused != null)
                {
                    LogUtil.LogError("[VPB.Net] " + refused);
                    Leave();
                    _nextPoll = Time.realtimeSinceStartup + 5f;
                    return;
                }
            }

            try
            {
                _host = new GameObject(HostName);
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _runner = _host.AddComponent<VpbNetPresenceRunner>();
                _startedAsHost = SettingOn(s => s.NetHostSession);
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] session start failed: " + e.Message);
                Stop("start failed");
            }
        }

        static bool SettingOn(Func<Settings, ConfigEntry<bool>> pick)
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null) return false;
                ConfigEntry<bool> e = pick(s);
                return e != null && e.Value;
            }
            catch { return false; }
        }

        public static void Stop(string reason)
        {
            try
            {
                if (_runner != null)
                {
                    _runner.Shutdown(reason);
                    _runner = null;
                }
                if (_host != null)
                {
                    UnityEngine.Object.Destroy(_host);
                    _host = null;
                }
            }
            catch { }

            State = VpbNetSessionState.Idle;
            Reason = VpbNetDropReason.None;
            Status = "not in a session";
            Hint = string.Empty;
            ContentWarning = string.Empty;
            ReasonText = string.Empty;
            Invite = string.Empty;
            PeerName = string.Empty;
            LocalName = string.Empty;
            PeerUp = false;
            PeerId = -1;
            MyAvatar = string.Empty;
            PeerAvatar = string.Empty;
            PendingAvatar = string.Empty;
            AvatarClaimDenied = false;
            ClaimDenyReason = string.Empty;
            SeatPickWanted = false;
            ClaimRevision = 0;
            ScenesMatch = false;
            try { VpbNetAvatarRoster.Shutdown(); }
            catch { }
        }

        public static void OnSessionState(VpbIpcSession state, string invite, string text)
        {
            if (_runner != null) _runner.OnSessionState(state, invite, text);
        }

        public static void OnPeerEvent(int peerId, VpbIpcPeerEvent kind, string text)
        {
            if (_runner != null) _runner.OnPeerEvent(peerId, kind, text);
        }

        public static void OnData(int peerId, byte channel, byte[] buf, int offset, int len, long arrivalTicks)
        {
            if (_runner != null) _runner.OnData(peerId, channel, buf, offset, len, arrivalTicks);
        }
    }

    public sealed class VpbNetPresenceRunner : MonoBehaviour
    {
        const byte CtrlPing = 1;
        const byte CtrlPong = 2;
        const byte CtrlResync = 3;
        const float LookPollSeconds = 1f;
        const int LeaveRepeats = 3;
        const int JoinRetryMs = 1000;
        const float ResolveRetrySeconds = 2f;
        const float AvatarPollSeconds = 0.5f;
        const float PendingClaimSeconds = 34f;
        const float SceneRebaseGraceSeconds = 8f;

        // 2s past load flags — VaM atoms still settling after loading clears.
        const float ThawSettleSeconds = 2f;
        const int ReRequestLimit = 3;
        const double KeyframeRetryMs = 1500.0;
        const double PeriodicResyncMs = 30000.0;
        const double ResyncMinIntervalMs = 1500.0;
        const double BrokerRetryMs = 2000.0;
        const double LinkSampleMs = 1000.0;
        const int DualPoseResends = 2;
        const double DualPoseResendMs = 250.0;
        const double AutoInviteRetryMs = 3000.0;
        const double JoinerAutoInviteDelayMs = 7000.0;

        // Burst copies of busy notice — SendBusy has no spaced retry, only random-loss cover.
        const int BusyBursts = 3;
        const int KeyframeMaxAttempts = 20;
        const float SettleSeconds = 2f;
        const float PropPollSeconds = 1f / 15f;
        const float ParamPollSeconds = 0.1f;

        readonly VpbNetPoseSampler _sampler = new VpbNetPoseSampler();
        readonly VpbNetPoseApplier _applier = new VpbNetPoseApplier();
        readonly VpbNetSession _session = new VpbNetSession();
        readonly VpbNetPerfHarness _perf = new VpbNetPerfHarness();
        readonly VpbNetEventWriter _eventW = new VpbNetEventWriter(VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize);
        readonly VpbNetEventReader _eventR = new VpbNetEventReader();
        readonly VpbNetPeerLook _localLook = new VpbNetPeerLook();
        readonly VpbNetPeerLook _remoteLook = new VpbNetPeerLook();
        readonly VpbNetPeerState _localState = new VpbNetPeerState();
        readonly VpbNetPeerState _peerState = new VpbNetPeerState();
        readonly VpbNetKeyframeAssembler _kfIn = new VpbNetKeyframeAssembler();
        readonly VpbNetKeyframeAssembler _contractIn = new VpbNetKeyframeAssembler();
        readonly VpbNetKeyframeAssembler _manifestIn = new VpbNetKeyframeAssembler();
        readonly VpbNetManifest _manifestRx = new VpbNetManifest();
        readonly VpbNetContract _localContract = new VpbNetContract();
        readonly VpbNetContract _peerContract = new VpbNetContract();
        readonly VpbNetContractReport _contractReport = new VpbNetContractReport();
        readonly VpbNetContentCatalog _catalog = new VpbNetContentCatalog();
        readonly VpbNetAvatarAssignment _claims = new VpbNetAvatarAssignment();

        int _mySeat = VpbNetAvatarAssignment.Unseated;

        int MySeat { get { return _mySeat; } }
        int TheirSeat { get { return VpbNetAvatarAssignment.OtherSeat(_mySeat); } }

        void BindLocalSeat(int seat)
        {
            _mySeat = VpbNetAvatarAssignment.IsSeat(seat) ? seat : VpbNetAvatarAssignment.Unseated;
            if (_mySeat == VpbNetAvatarAssignment.Unseated)
                LogUtil.LogError("[VPB.Net] seat " + seat + " is not a seat in this room - this side will stay a spectator");
        }
        readonly VpbNetAvatarLock _avatarLock = new VpbNetAvatarLock();
        readonly List<string> _changeIds = new List<string>(VpbNetPeerLook.MaxChangesPerPoll);
        readonly List<bool> _changeOn = new List<bool>(VpbNetPeerLook.MaxChangesPerPoll);
        readonly List<float> _changeValue = new List<float>(VpbNetPeerLook.MaxChangesPerPoll);
        readonly List<string> _triggerStorables = new List<string>(VpbNetEventLimits.MaxTriggersPerEvent);
        readonly List<string> _hairIds = new List<string>(VpbNetEventLimits.MaxHairItems);
        readonly List<string> _peerHair = new List<string>(VpbNetEventLimits.MaxHairItems);
        readonly VpbNetPropSync _props = new VpbNetPropSync();
        readonly VpbNetAtomParamSync _params = new VpbNetAtomParamSync();
        readonly VpbNetAtomParamBatch _paramTx = new VpbNetAtomParamBatch();
        readonly VpbNetPropFrame _propTx = new VpbNetPropFrame();
        readonly VpbNetPropFrame _propRx = new VpbNetPropFrame();
        readonly byte[] _propBuf = new byte[VpbIpc.MaxDataPayload];
        readonly List<string> _atomUids = new List<string>(VpbNetEventLimits.MaxAtomsPerEvent);
        readonly List<string> _atomTypes = new List<string>(VpbNetEventLimits.MaxAtomsPerEvent);
        readonly List<string> _atomRefs = new List<string>(VpbNetEventLimits.MaxAtomsPerEvent);
        readonly List<Vector3> _atomPos = new List<Vector3>(VpbNetEventLimits.MaxAtomsPerEvent);
        readonly List<Quaternion> _atomRot = new List<Quaternion>(VpbNetEventLimits.MaxAtomsPerEvent);
        const int MaxHeldSubScenes = 8;
        readonly List<string> _heldSubUid = new List<string>(MaxHeldSubScenes);
        readonly List<string> _heldSubRef = new List<string>(MaxHeldSubScenes);
        int _atomAddCap = VpbNetEventLimits.MaxAtomsPerEvent;
        readonly byte[] _kfWhole = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
        readonly byte[] _kfFrag = new byte[VpbIpc.MaxDataPayload];
        readonly byte[] _kfRx = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
        readonly byte[] _contractWhole = new byte[VpbNetContractLimits.MaxContractBytes];
        readonly byte[] _contractRx = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
        readonly byte[] _manifestWhole = new byte[VpbNetManifestLimits.MaxBytes];
        readonly byte[] _manifestRxBytes = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
        readonly byte[] _contentReplayBuf = new byte[VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize];
        readonly Stopwatch _clockWatch = new Stopwatch();
        readonly byte[] _send = new byte[VpbIpc.MaxDataPayload];
        readonly byte[] _poseRx = new byte[VpbIpc.MaxDataPayload];
        readonly byte[] _ctrl = new byte[32];
        readonly byte[] _dualPoseTx = new byte[VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize];

        VpbNetClock _netClock;
        VpbNetTimeline _timeline;
        VpbNetRigDescriptor _localRig;
        Atom _localAtom;
        Atom _remoteAtom;

        bool _shutdown;
        bool _asHost;
        bool _bookedPeer;
        bool _opened;
        bool _dialed;
        bool _autoClaimed;
        bool _sentJoin;
        bool _gotJoin;
        bool _soakStarted;
        bool _soakReported;
        bool _loggedInvite;
        bool _dropLogged;
        bool _brokerLossLogged;
        bool _rigOk;
        bool _loggedRig;
        uint _loggedCaps = 0xFFFFFFFFu;
        uint _peerCapsRaw;
        bool _havePeerCaps;
        string _localScene = string.Empty;
        string _peerScene = string.Empty;
        bool _havePeerScene;
        bool _scenesMatched;
        string _autoInvitedScene = string.Empty;
        double _autoInviteAtMs;
        double _mismatchSinceMs;
        float _pendingClaimUntil;

        readonly byte[] _askReplayBuf = new byte[VpbNetRulebook.AskSlotBytes];
        byte[] _evBuf;
        int _evOffset;
        int _evLen;
        bool _askReplay;
        uint _rulesWatermark;
        bool _triggersOn;
        bool _syncAtoms;
        bool _bgForced;
        bool _bgPrior;
        bool _propsOn;
        bool _paramsOn;
        bool _keyframeSent;
        bool _warnedLookCap;
        bool _contractSent;
        bool _haveContractSig;
        uint _contractSig;
        double _lastResyncAskMs = double.NegativeInfinity;

        int _peerId = -1;
        int _hz = VpbNetPoseSampler.DefaultHz;
        int _interp;
        int _extrap;
        int _frozen;
        int _empty;
        int _acquireResets;
        int _acquireResetsAfterGap;
        int _joinRetries;
        uint _eventSeq = 1;
        uint _sharedCaps;
        uint _localCaps = VpbNetCapability.Local;
        int _kfGen;
        int _kfApplied;
        int _kfSent;
        int _kfStale;
        bool _remoteLookPrimed;
        int _contractGen;
        int _manifestGen;
        uint _offerWatermark;
        uint _contentStateWatermark;
        int _lastResumes;
        int _lastFrameLen;
        int _triggersSent;
        int _paramsSent;
        int _keyframeAttempts;
        bool _keyframeIncomplete;
        double _nextKeyframeMs;
        double _nextPeriodicResyncMs;
        double _nextBrokerRetryMs;
        int _periodicResyncs;
        // Per-kind watermark — shared seq drops overtaken clothing events.
        uint _triggerWatermark;
        uint _clothingWatermark;
        uint _morphWatermark;
        uint _hairWatermark;
        uint _presetWatermark;
        uint _dualPoseWatermark;
        uint _busyWatermark;
        int _busySent;
        int _busyHeld;
        int _dualPoseTxLen;
        int _dualPoseTxLeft;
        double _dualPoseTxNextMs;
        int _clothingDroppedStale;
        int _morphDroppedStale;
        int _clothingApplied;
        int _hairApplied;
        int _hairSent;
        uint _propSeq = 1;
        uint _propWatermark;
        uint _paramWatermark;
        int _propFramesSent;
        int _propFramesApplied;
        int _propRefused;
        float _nextPropPoll;
        float _nextParamPoll;
        float _nextLookPoll;
        int _clothingSigTold;
        int _clothingSigSeen;
        bool _haveClothingSig;
        int _hairSigTold;
        bool _haveHairSig;
        int _clothingRepairs;
        int _subScenesSent;
        int _subScenesReceived;

        string _room = "vpb-lan-test";
        string _address = string.Empty;
        string _rendezvous = string.Empty;
        bool _steam;
        bool _roomIsInvite;
        bool _lanDiscovery = true;
        bool _configRefused;
        string _localWanted = string.Empty;
        string _transportFail = string.Empty;
        string _boundLocalUid = string.Empty;
        string _boundRemoteUid = string.Empty;

        // No handles cross the wire: a peer is the Person they ride.
        string PeerName { get { return _boundRemoteUid; } }

        string PeerLabel()
        {
            return PeerLabel(false);
        }

        string PeerLabel(bool sentenceStart)
        {
            if (_boundRemoteUid.Length > 0) return _boundRemoteUid;
            return sentenceStart
                ? VPBTranslation.T("net_peer.they_caps", "The other player")
                : VPBTranslation.T("net_peer.they", "the other player");
        }
        string _spawningAvatar;
        string _desiredUid = string.Empty;
        string _heldClaimUid = string.Empty;
        readonly string[] _seatScan = new string[VpbNetAvatarRoster.MaxAvatars];
        bool _seatChoiceMade;
        bool _peerSeatChoiceMade;
        bool _seatsNeedRebase;
        float _sceneRebaseUntil;
        bool _syncFrozen;
        float _thawAt;
        int _frozenDrops;
        int _reRequests;

        float _nextResolve;
        float _soakStart;
        double _nextPingMs;
        double _nextJoinMs;
        double _nowMs;
        double _lastRenderMs;
        double _peerServiceMs;
        double _nextLinkSampleMs;
        readonly VpbNetHistogram _rttHist = new VpbNetHistogram(2.0);
        readonly VpbNetHistogram _delayHist = new VpbNetHistogram(4.0);
        readonly VpbNetHistogram _transitHist = new VpbNetHistogram(2.0);
        readonly VpbNetHistogram _bufferHist = new VpbNetHistogram(2.0);
        readonly VpbNetHistogram _serviceHist = new VpbNetHistogram(2.0);
        long _epochTicks;

        void Awake()
        {
            ReadConfig();
            BindLocalSeat(_asHost ? VpbNetAvatarAssignment.SeatA : VpbNetAvatarAssignment.SeatB);
            RefreshLocalCaps();
            _netClock = new VpbNetClock(Stopwatch.Frequency);
            _timeline = new VpbNetTimeline(_netClock, 1000.0 / _hz);
            _sampler.MeasureTiming = true;
            _applier.MeasureTiming = true;
            _clockWatch.Start();
            _epochTicks = Stopwatch.GetTimestamp() - _clockWatch.ElapsedTicks;
            VpbNetPresetRelay.ResetCounters();
            VpbNetPresetRelay.SetSender(SendPresetApply);
            VpbNetDualPoseRelay.ResetCounters();
            VpbNetDualPoseRelay.SetSender(SendDualPose);
            VpbNetBusy.ResetCounters();
            VpbNetBusy.SetSender(SendBusy);
            VpbNetRulebook.ResetForSession();
            VpbNetRulebook.SetSender(OnLocalRulesChanged);
            VpbNetContentSync.ResetForSession();
            VpbNetContentSync.SetSenders(SendSceneOffer, SendManifest, SendContentState);
            Publish();
            LogUtil.LogWarning("[VPB.Net] session starting as " + (_asHost ? "host" : "joiner")
                + ", room \"" + _room + "\""
                + (_asHost ? string.Empty : DescribeJoinTarget()));
        }

        void ReadConfig()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null) return;
                _asHost = s.NetHostSession != null && s.NetHostSession.Value;
                if (s.NetLanRoomCode != null && s.NetLanRoomCode.Value != null) _room = s.NetLanRoomCode.Value;
                if (s.NetLanAddress != null && s.NetLanAddress.Value != null) _address = s.NetLanAddress.Value.Trim();
                if (s.NetRendezvousAddress != null && s.NetRendezvousAddress.Value != null) _rendezvous = s.NetRendezvousAddress.Value.Trim();
                if (s.NetLanDiscovery != null) _lanDiscovery = s.NetLanDiscovery.Value;
                if (s.NetLocalAtom != null && s.NetLocalAtom.Value != null) _localWanted = s.NetLocalAtom.Value.Trim();
                if (s.NetSamplerHz != null) _hz = Mathf.Clamp(s.NetSamplerHz.Value, 1, 200);
                if (s.NetSyncAtoms != null) _syncAtoms = s.NetSyncAtoms.Value;
            }
            catch { }

            if (string.IsNullOrEmpty(_room)) _room = "vpb-lan-test";
            _roomIsInvite = VpbNetInviteCode.LooksLikeInvite(_room);

            _steam = VpbNetTransportChoice.IsSteam();
            string blocked = VpbNetTransportChoice.BlockedReason();
            if (blocked != null)
            {
                LogUtil.LogError("[VPB.Net] " + blocked);
                _configRefused = true;
                return;
            }
            if (_steam)
            {
                _address = string.Empty;
                _rendezvous = string.Empty;

                if (!VpbNetRoomCode.IsWellFormed(_room))
                {
                    LogUtil.LogError("[VPB.Net] a Steam session needs a generated 12-character room code"
                        + " - it is the only thing the other side has to find you by, and a guessable code is a guessable room."
                        + " " + VpbNetRoomCode.Explain(_room)
                        + " Press New to make one.");
                    _configRefused = true;
                    return;
                }

                _configRefused = false;
                return;
            }

            if (_rendezvous.Length > 0 && !VpbNetRoomCode.IsWellFormed(_room))
            {
                LogUtil.LogError("[VPB.Net] Net.RendezvousAddress is set, so Net.LanRoomCode must be a generated 12-character code"
                    + " - the rendezvous publishes a token derived from it, and a guessable code is a guessable token."
                    + " " + VpbNetRoomCode.Explain(_room)
                    + " Press Generate to make one.");
                _rendezvous = string.Empty;
                _configRefused = true;
                return;
            }

            _configRefused = false;
            if (!_asHost && !HaveJoinTarget())
            {
                string refused = VpbNetPresence.JoinBlockedReason();
                LogUtil.LogError("[VPB.Net] " + (refused != null ? refused : "join has nothing to dial"));
            }
        }

        string DescribeJoinTarget()
        {
            if (_steam) return " over Steam";
            if (_rendezvous.Length > 0) return " via rendezvous " + _rendezvous;
            if (!string.IsNullOrEmpty(_address)) return " address " + _address;
            if (_roomIsInvite) return " via invite";
            return " by finding the host on this subnet";
        }

        bool HaveJoinTarget()
        {
            if (_steam) return true;
            if (_rendezvous.Length > 0) return true;
            if (!string.IsNullOrEmpty(_address)) return true;
            if (_roomIsInvite) return true;

            // Default room code must not qualify for discovery.
            return _lanDiscovery && VpbNetRoomCode.IsWellFormed(_room);
        }

        string ConnectBlobForOpen()
        {
            if (_steam) return VpbNetTransportChoice.ConnectBlob();
            if (_rendezvous.Length > 0) return "rv:" + _rendezvous;
            return _asHost ? string.Empty : _address;
        }

        VpbIpcBackend BackendForOpen()
        {
            return _steam ? VpbIpcBackend.Steam : VpbIpcBackend.Lan;
        }

        public void Shutdown(string reason)
        {
            if (_shutdown) return;
            _shutdown = true;
            if (_soakStarted && !_soakReported) EmitSessionReport(true);
            SendLeave();
            try { _sampler.Unbind(); } catch { }
            try { _applier.Unbind(); } catch { }
            try { _localLook.Unbind(); } catch { }
            try { _remoteLook.Unbind(); } catch { }
            try { VpbNetTriggerRelay.End(); } catch { }
            _triggersOn = false;
            try { _props.Unbind(); } catch { }
            _propsOn = false;
            try { _params.Unbind(); } catch { }
            _paramsOn = false;
            _heldSubUid.Clear();
            _heldSubRef.Clear();
            SyncRunInBackground(false);
            try { VpbNetBodyGuard.ReleaseAll(); } catch { }
            try { VpbNetCollisionGuard.ReleaseAll(); } catch { }
            try { VpbNetPresetRelay.SetSender(null); } catch { }
            try { VpbNetDualPoseRelay.SetSender(null); } catch { }
            try { VpbNetBusy.SetSender(null); } catch { }
            try { VpbNetRulebook.SetSender(null); } catch { }
            try { VpbNetContentSync.Shutdown(); } catch { }
            _dualPoseTxLeft = 0;
            try { _avatarLock.ReleaseAll(); } catch { }
            _claims.Reset();
            _boundLocalUid = string.Empty;
            _boundRemoteUid = string.Empty;
            VpbNetPresence.MyAvatar = string.Empty;
            VpbNetPresence.PeerAvatar = string.Empty;
            VpbNetPresence.PendingAvatar = string.Empty;
            VpbNetPresence.AvatarClaimDenied = false;
            VpbNetPresence.ClaimDenyReason = string.Empty;
            VpbNetPresence.SeatPickWanted = false;
            _seatChoiceMade = false;
            _peerSeatChoiceMade = false;
            _heldClaimUid = string.Empty;
            try { GalleryPanel.NotifyAllPanelsSceneTargetsChanged(); } catch { }
            _localAtom = null;
            _remoteAtom = null;
            try { _session.LocalLeave(_nowMs); } catch { }
            try { VpbNetBrokerLink.CloseSession(); } catch { }
            try { VpbNetBrokerLink.Stop(reason); } catch { }
            Publish();
            VPBLogger.Main.LogWarning("[VPB.Net] session stopped (" + reason + ")", false);
        }

        void OnDestroy()
        {
            if (!_shutdown) Shutdown("destroyed");
        }

        // Force runInBackground while session live — VaM throttles unfocused windows.
        void SyncRunInBackground(bool want)
        {
            if (want == _bgForced) return;

            try
            {
                if (want)
                {
                    _bgPrior = Application.runInBackground;
                    if (!_bgPrior)
                        LogUtil.LogWarning("[VPB.Net] VaM is set to throttle itself when its window is not"
                            + " focused, which would make your avatar freeze and stutter for the other"
                            + " player every time you alt-tab. Keeping it running at full rate until the"
                            + " session ends, then putting it back.");
                    Application.runInBackground = true;
                }
                else
                {
                    Application.runInBackground = _bgPrior;
                }
            }
            catch { }
            _bgForced = want;
        }

        public void Pump()
        {
            if (_shutdown) return;

            _nowMs = _clockWatch.Elapsed.TotalMilliseconds;
            _session.Tick(_nowMs);
            VpbNetBusy.Poll();
            VpbNetRulebook.Tick(_nowMs);
            PumpApprovedAsks();
            PumpClaimRefusals();
            VpbNetContentSync.Tick(_nowMs);
            PumpContentReplays();
            SyncRunInBackground(_session.State != VpbNetSessionState.Idle);
            MaintainAvatars();
            TryOpen();
            TryReconnect();
            MaybeJoin();
            MaybeSendKeyframe();
            MaybeSendContract();
            MaybeResendDualPose();
            MaybeResumeResync();
            MaybePeriodicResync();
            PollLook();
            PollTriggers();
            PollProps();
            PollParams();
            _kfIn.Tick(_nowMs);
            _manifestIn.Tick(_nowMs);
            Publish();

            if (_session.State == VpbNetSessionState.Dropped
                || _session.State == VpbNetSessionState.Idle)
            {
                if (_session.Reason == VpbNetDropReason.LocalLeave) return;
                if (_session.Reason == VpbNetDropReason.Kicked
                    || _session.Reason == VpbNetDropReason.PeerLeave
                    || _session.Reason == VpbNetDropReason.ReconnectExhausted
                    || _session.Reason == VpbNetDropReason.ConnectTimeout
                    || _session.Reason == VpbNetDropReason.AuthFailed
                    || _session.Reason == VpbNetDropReason.VersionMismatch)
                {
                    AnnounceFatalAndLeave();
                }
            }
        }

        void AnnounceFatalAndLeave()
        {
            if (_dropLogged)
            {
                VpbNetPresence.Leave();
                return;
            }
            _dropLogged = true;

            string why = _session.DescribeReason();
            if (string.IsNullOrEmpty(why)) why = "Disconnected.";
            bool asHost = _asHost;
            bool hadPeer = _gotJoin;
            VpbNetDropReason reason = _session.Reason;
            if (_steam && WantsSteamCheck(reason))
            {
                if (_transportFail.Length > 0) why = _transportFail + "\n\n" + why;
                why = VPBTranslation.T("net_alert.steam_running",
                    "Check Steam is running. The Steam client has to be open and signed in on both machines for the whole session - it is what carries the connection, VaM is not enough on its own. Steam closed, or quitting after VaM started, looks exactly like a room nobody answered.")
                    + "\n\n" + why;
            }

            try { VpbNetAlertUi.Show(reason, asHost, hadPeer, why); }
            catch { }

            try
            {
                // The alert above keeps the address; the log is the copy that gets shared.
                string logged = VpbNetRedact.Scrub(why);
                if (reason == VpbNetDropReason.VersionMismatch)
                    VPBLogger.Main.LogError("[VPB.Net] " + logged, false);
                else
                    VPBLogger.Main.LogWarning("[VPB.Net] " + logged, false);
            }
            catch { }

            if (!asHost)
            {
                try { VpbNetSteamFlow.RememberJoinError(why); }
                catch { }
            }

            VpbNetPresence.Leave();
            try { VpbNetSessionUi.SetWanted(true); }
            catch { }
            try { VpbNetSessionUi.RefreshNow(); }
            catch { }
        }

        // Steam down is indistinguishable from a silent host, so name it on every reach failure.
        static bool WantsSteamCheck(VpbNetDropReason reason)
        {
            return reason == VpbNetDropReason.ConnectTimeout
                || reason == VpbNetDropReason.TransportError
                || reason == VpbNetDropReason.ReconnectExhausted;
        }

        void FixedUpdate()
        {
            if (_shutdown) return;

            _nowMs = _clockWatch.Elapsed.TotalMilliseconds;
            _perf.AddFrame(Time.unscaledDeltaTime * 1000f);

            if (_sampler.IsBound && !_sampler.IsAlive())
            {
                _sampler.Unbind();
                _localLook.Unbind();
                _boundLocalUid = string.Empty;
                _localAtom = null;
            }

            if (_applier.IsBound && !_applier.IsAlive())
            {
                _applier.Unbind();
                _remoteLook.Unbind();
                _boundRemoteUid = string.Empty;
                _remoteAtom = null;
            }

            UpdateSyncFreeze();

            uint tickMs = (uint)_clockWatch.ElapsedMilliseconds;
            if (_peerId >= 0 && _session.State != VpbNetSessionState.Idle)
            {
                // Spectator still pings — timeline must stay synced for later avatar claim.
                if (_sampler.IsBound && _sampler.Tick(Time.fixedDeltaTime, tickMs))
                {
                    int n = _sampler.Outbound.TryDequeue(_send);
                    if (n > 0)
                    {
                        VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Pose, _send, n, false);
                        _perf.SamplerUs.Add(_sampler.LastEncodeUs);
                        _lastFrameLen = n;
                    }
                }
                MaybePing();
            }

            if (!_syncFrozen && _applier.IsBound && _session.CanDriveAvatar && _timeline.Ready && _rigOk)
            {
                double render = _timeline.RenderRemoteMs(_nowMs);
                _lastRenderMs = render;
                VpbNetSampleState st = _applier.Apply(render);
                _perf.ApplierUs.Add(_applier.LastApplyUs);
                if (st == VpbNetSampleState.Interpolated) _interp++;
                else if (st == VpbNetSampleState.Extrapolated) _extrap++;
                else if (st == VpbNetSampleState.Frozen) _frozen++;
                else _empty++;
            }

            VpbNetCollisionGuard.SetLoadHold(_syncFrozen);
            VpbNetCollisionGuard.Tick();
            VpbNetBodyGuard.Tick();
            StepSessionStats();
            if (VpbNetOverlay.IsVisible) FeedOverlay();
        }

        bool ContentLoadInFlight()
        {
            if (VpbNetBusy.Active) return true;
            if (_peerId >= 0 && _session.PeerBusy) return true;
            try { return LogUtil.IsSceneLoadActive() || LogUtil.IsSceneLoading(); }
            catch { return false; }
        }

        // Don't drive pose/outfit while loading — thaw re-keys.
        void UpdateSyncFreeze()
        {
            float now = Time.realtimeSinceStartup;

            if (ContentLoadInFlight())
            {
                _thawAt = now + ThawSettleSeconds;
                if (_syncFrozen) return;
                _syncFrozen = true;
                LogUtil.LogWarning("[VPB.Net] content is loading - the room is paused;"
                    + " nothing is driven on this side until both machines are standing still");
                return;
            }

            if (!_syncFrozen || now < _thawAt) return;
            _syncFrozen = false;
            _frozenDrops = 0;
            Thaw();
        }

        void Thaw()
        {
            // Snap render cursor past pause — buffered frames describe a room that no longer exists.
            _timeline.Reset();
            _applier.Buffer.Clear();
            _applier.Buffer.ResetCounters();
            _applier.Rebase("the room finished loading");

            // Resync both ways; clear hair sig or resend is skipped as unchanged.
            _sampler.MarkKeyframe();
            _haveHairSig = false;
            _remoteLookPrimed = false;
            if (_peerId >= 0 && _rigOk)
            {
                _ctrl[0] = CtrlResync;
                VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Ctrl, _ctrl, 1, true);
                SendKeyframe();
            }

            LogUtil.LogWarning("[VPB.Net] the room is running again - both sides re-keyed"
                + " and the avatars pick up from where the scene put them");
        }

        // Refuse (don't queue) look/pose traffic while paused; control traffic must still flow.
        bool FrozenTo(byte type)
        {
            if (!_syncFrozen) return false;
            if (type != VpbNetEventType.Clothing
                && type != VpbNetEventType.Hair
                && type != VpbNetEventType.Morphs
                && type != VpbNetEventType.PresetApply
                && type != VpbNetEventType.DualPose)
                return false;

            _frozenDrops++;
            if (_frozenDrops == 1)
                LogUtil.LogWarning("[VPB.Net] holding off the peer's look and preset changes"
                    + " while content loads; they are re-sent in full once the room is running again");
            return true;
        }

        void MaintainAvatars()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextResolve) return;
            _nextResolve = now + AvatarPollSeconds;

            if (VpbNetPresence.PendingAvatar.Length > 0 && now >= _pendingClaimUntil)
            {
                LogUtil.LogWarning("[VPB.Net] the other player never allowed you to ride "
                    + VpbNetPresence.PendingAvatar
                    + "; their session rules answer that question, not yours");
                VpbNetPresence.PendingAvatar = string.Empty;
                VpbNetPresence.AvatarClaimDenied = true;
                VpbNetPresence.ClaimDenyReason = VPBTranslation.T("net_session.ride.silent",
                    "They never answered.");
                _desiredUid = string.Empty;
            }

            // No bind/release mid-load — Person still building; seats rebase on thaw.
            if (_syncFrozen) return;

            TrackLocalScene();
            int rosterBefore = VpbNetAvatarRoster.Revision;
            VpbNetAvatarRoster.Poll();
            if (VpbNetAvatarRoster.Revision != rosterBefore)
                _avatarLock.Apply(_claims.SeatUid(MySeat), _peerId >= 0);

            // No claims until both in same scene — else spawn a Person the peer hasn't loaded.
            if (!ScenesReady())
            {
                MaybeAutoInvite();
                VpbNetPresence.SeatPickWanted = false;
                return;
            }

            RebaseSeatsOnScene();
            AutoClaimFromSettings();
            AutoSeatPeer();
            ApplyClaimBinding();

            VpbNetPresence.SeatPickWanted = !_seatChoiceMade
                && _claims.IsSpectator(MySeat)
                && VpbNetAvatarRoster.Count > 0;
        }

        // Mismatch auto-offers scene.
        void MaybeAutoInvite()
        {
            if (_peerId < 0 || !PeerTakesContent) return;
            if (!_havePeerScene || _localScene.Length == 0) return;

            if (_mismatchSinceMs <= 0.0) _mismatchSinceMs = _nowMs;
            if (!_asHost && _nowMs - _mismatchSinceMs < JoinerAutoInviteDelayMs) return;
            if (_nowMs < _autoInviteAtMs) return;

            // One offer per scene; decline is final until host hits Ask again.
            if (string.Equals(_autoInvitedScene, _localScene, StringComparison.Ordinal)) return;

            if (!VpbNetContentSync.Mine && VpbNetContentSync.ExchangeLive(_nowMs))
            {
                _autoInviteAtMs = _nowMs + AutoInviteRetryMs;
                return;
            }

            string path = null;
            try { path = VpbNetContentContract.CurrentScenePath(); }
            catch { path = null; }

            _autoInviteAtMs = _nowMs + AutoInviteRetryMs;

            if (string.IsNullOrEmpty(path))
            {
                // Retry if gallery index still building — scene becomes describable when it lands.
                return;
            }

            // Match offer by path, not presence — declined card would block every later scene.
            if (VpbNetContentSync.HasOffer && VpbNetContentSync.Mine
                && string.Equals(VpbNetContentSync.Offer.ScenePath, path, StringComparison.Ordinal))
            {
                _autoInvitedScene = _localScene;
                return;
            }

            _autoInvitedScene = _localScene;
            if (VpbNetContentSync.BeginHostOffer(path, false, _nowMs))
                LogUtil.LogWarning("[VPB.Net] " + PeerLabel()
                    + " is in a different scene, so they were invited to this one");
        }

        void TrackLocalScene()
        {
            string uid = null;
            try { uid = VpbNetContentContract.CurrentSceneUid(); }
            catch { }
            if (uid == null) uid = string.Empty;
            if (string.Equals(uid, _localScene, StringComparison.Ordinal)) return;

            _localScene = uid;
            VpbNetPresence.LocalScene = uid;
            _scenesMatched = false;
            _seatChoiceMade = false;
            _peerSeatChoiceMade = false;
            _heldClaimUid = string.Empty;

            // Rebase seats on scene change — old uids freeze new Persons and BindRemote spawns blanks.
            _seatsNeedRebase = true;
            _autoClaimed = false;
            _desiredUid = string.Empty;
            _spawningAvatar = null;
            _sceneRebaseUntil = Time.realtimeSinceStartup + SceneRebaseGraceSeconds;
            SendSceneState();
        }

        // Host-only seat rebase — joiner dropping locally would desync from incoming broadcast.
        void RebaseSeatsOnScene()
        {
            if (!_seatsNeedRebase) return;

            if (!_asHost)
            {
                // Wait for host state; grace covers host whose scene never changed so never rebroadcasts.
                if (Time.realtimeSinceStartup >= _sceneRebaseUntil) _seatsNeedRebase = false;
                return;
            }

            _seatsNeedRebase = false;

            uint before = _claims.Generation;
            for (int seat = 0; seat < VpbNetAvatarAssignment.SeatCount; seat++)
            {
                string uid = _claims.SeatUid(seat);
                if (uid.Length == 0) continue;
                if (VpbNetAvatarRoster.Contains(uid)) continue;

                LogUtil.LogWarning("[VPB.Net] " + uid + " is not in "
                    + (_localScene.Length > 0 ? _localScene : "this scene")
                    + ", so seat " + VpbNetAvatarAssignment.SeatName(seat)
                    + " is empty again - pick a Person from the session panel");
                _claims.ClearSeat(seat);
            }

            if (_claims.Generation == before) return;
            ApplyClaimBinding();
            BroadcastClaimState();
        }

        bool ScenesReady()
        {
            bool ready = _peerId < 0
                ? _localScene.Length > 0
                : _havePeerScene && _localScene.Length > 0
                    && string.Equals(_localScene, _peerScene, StringComparison.Ordinal);

            if (ready == _scenesMatched)
            {
                VpbNetPresence.ScenesMatch = ready;
                return ready;
            }
            _scenesMatched = ready;
            VpbNetPresence.ScenesMatch = ready;

            if (ready)
            {
                _mismatchSinceMs = 0.0;
                LogUtil.LogWarning("[VPB.Net] both sides are in "
                    + (_localScene.Length > 0 ? _localScene : "the same scene")
                    + "; avatars can be claimed now");
                RenegotiateCaps(null);
            }
            else if (_peerId >= 0)
            {
                LogUtil.LogWarning("[VPB.Net] waiting for the same scene on both sides - you have "
                    + (_localScene.Length > 0 ? _localScene : "no scene")
                    + ", they have " + (!_havePeerScene ? "not said yet"
                        : (_peerScene.Length > 0 ? _peerScene : "no scene"))
                    + "; nobody rides an avatar until these match");
                SetPropSync(false, false);
                SetParamSync(false);
            }
            return ready;
        }

        void SendSceneState()
        {
            if (_peerId < 0) return;
            _eventW.Begin(VpbNetEventType.SceneState, _eventSeq++);
            _eventW.WriteString(_localScene, VpbNetEventLimits.MaxIdentifier);
            int n = _eventW.End();
            if (n > 0) VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
        }

        void HandleSceneStateEvent()
        {
            string uid = _eventR.ReadText(VpbNetEventLimits.MaxIdentifier);
            if (_eventR.Failed || uid == null) return;
            if (string.Equals(uid, _peerScene, StringComparison.Ordinal) && _havePeerScene) return;

            _peerScene = uid;
            _havePeerScene = true;
            VpbNetPresence.PeerScene = uid;
            VpbNetPresence.HavePeerScene = true;
            _scenesMatched = false;
            SendSceneState();
        }

        void ApplyClaimBinding()
        {
            string mine = _claims.SeatUid(MySeat);
            string theirs = _claims.SeatUid(TheirSeat);

            // Release before bind — swap would double-hold one atom (sampler + applier).
            if (!string.Equals(mine, _boundLocalUid, StringComparison.Ordinal)) ReleaseLocal();
            if (!string.Equals(theirs, _boundRemoteUid, StringComparison.Ordinal)) ReleaseRemote();

            BindLocal(mine);
            BindRemote(theirs);

            bool claimChanged = !string.Equals(mine, VpbNetPresence.MyAvatar, StringComparison.Ordinal)
                || !string.Equals(theirs, VpbNetPresence.PeerAvatar, StringComparison.Ordinal);

            VpbNetPresence.ShareObjectsLive = _props.LifecycleOn;
            VpbNetPresence.MyAvatar = mine;
            VpbNetPresence.PeerAvatar = theirs;
            VpbNetPresence.ClaimRevision = _claims.Generation;
            if (VpbNetPresence.PendingAvatar.Length > 0
                && string.Equals(mine, VpbNetPresence.PendingAvatar, StringComparison.Ordinal))
            {
                VpbNetPresence.PendingAvatar = string.Empty;
                VpbNetPresence.AvatarClaimDenied = false;
            }

            if (claimChanged)
            {
                try { GalleryPanel.NotifyAllPanelsSceneTargetsChanged(); }
                catch { }
            }

            // Lock grab handles on everyone you don't ride.
            _avatarLock.Apply(mine, _peerId >= 0);
        }

        void BindLocal(string uid)
        {
            if (string.Equals(uid, _boundLocalUid, StringComparison.Ordinal)
                && _sampler.IsBound == (uid.Length > 0))
                return;

            if (uid.Length == 0)
            {
                if (_sampler.IsBound)
                {
                    ReleaseLocal();
                    LogUtil.LogWarning("[VPB.Net] you are spectating: nothing of yours is being sent");
                }
                _boundLocalUid = string.Empty;
                _localAtom = null;
                RefreshLocalCaps();
                return;
            }

            Atom a = VpbNetAvatarRoster.Find(uid);
            if (a == null)
            {
                ReleaseLocal();
                return;
            }

            _sampler.SetHz(_hz);
            _sampler.PeerId = 1;
            _sampler.WantFingers = true;
            _sampler.WantGaze = true;
            _sampler.WantJaw = true;
            _sampler.GuardCollisions = true;
            if (!_sampler.Bind(a, _hz))
            {
                LogUtil.LogError("[VPB.Net] could not sample " + uid);
                return;
            }

            _localLook.Bind(a, MorphScanWanted());
            _localLook.ExcludeMorphUid =
                (_sampler.FidelityCaps & VpbNetCapability.Jaw) != 0 ? _sampler.JawMorphUid : null;
            _localAtom = a;
            _boundLocalUid = uid;
            _timeline.SetFrameInterval(1000.0 / _hz);
            _perf.CaptureContext();
            RefreshLocalCaps();
            _contractSent = false;
            LogUtil.LogWarning("[VPB.Net] you are riding " + uid);
        }

        void BindRemote(string uid)
        {
            _session.SetPeerStreaming(uid.Length > 0);

            if (string.Equals(uid, _boundRemoteUid, StringComparison.Ordinal)
                && _applier.IsBound == (uid.Length > 0))
                return;

            if (uid.Length == 0)
            {
                if (_applier.IsBound)
                {
                    ReleaseRemote();
                    LogUtil.LogWarning("[VPB.Net] the other player is spectating: no avatar of theirs to drive");
                }
                _boundRemoteUid = string.Empty;
                _remoteAtom = null;
                return;
            }

            Atom a = VpbNetAvatarRoster.Find(uid);
            if (a == null)
            {
                ReleaseRemote();
                RequestPeerAvatar(uid);
                return;
            }

            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetLockRoot != null) _applier.LockRoot = s.NetLockRoot.Value;
            }
            catch { }

            _applier.SetFidelityCaps(_sharedCaps & VpbNetCapability.FidelityTier);
            if (!_applier.Bind(a))
            {
                LogUtil.LogError("[VPB.Net] could not drive " + uid);
                return;
            }

            _remoteLook.Bind(a, false);
            _remoteLookPrimed = false;
            _remoteAtom = a;
            _boundRemoteUid = uid;
            RequestKeyframeFromPeer();
            _keyframeSent = false;
            _keyframeIncomplete = false;
            _keyframeAttempts = 0;
            _nextKeyframeMs = 0.0;
            LogUtil.LogWarning("[VPB.Net] the other player is riding " + uid);
        }

        void RequestKeyframeFromPeer()
        {
            if (_peerId < 0) return;
            _ctrl[0] = CtrlResync;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Ctrl, _ctrl, 1, true);
        }

        void ReleaseLocal()
        {
            if (!_sampler.IsBound && _boundLocalUid.Length == 0) return;
            _sampler.Unbind();
            _localLook.Unbind();
            _boundLocalUid = string.Empty;
            _localAtom = null;
        }

        void ReleaseRemote()
        {
            if (!_applier.IsBound && _boundRemoteUid.Length == 0) return;
            _applier.Unbind();
            _remoteLook.Unbind();
            _remoteLookPrimed = false;
            _boundRemoteUid = string.Empty;
            _remoteAtom = null;
        }

        void RequestPeerAvatar(string uid)
        {
            if (!_scenesMatched) return;

            // Don't spawn stand-in during rebase grace — seat table still names last scene's Persons.
            if (_seatsNeedRebase || Time.realtimeSinceStartup < _sceneRebaseUntil) return;
            if (_spawningAvatar != null && string.Equals(_spawningAvatar, uid, StringComparison.Ordinal)) return;
            if (!VpbNetAvatarAssignment.IsValidUid(uid) || uid.Length == 0) return;

            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            _spawningAvatar = uid;
            LogUtil.LogWarning("[VPB.Net] the other player is riding " + uid
                + ", which this scene does not have; creating an empty Person by that name to drive");
            try { sc.StartCoroutine(SpawnAvatarCo(sc, uid)); }
            catch (Exception e)
            {
                _spawningAvatar = null;
                LogUtil.LogWarning("[VPB.Net] could not create " + uid + ": " + e.Message);
            }
        }

        IEnumerator SpawnAvatarCo(SuperController sc, string uid)
        {
            IEnumerator add = null;
            try { add = sc.AddAtomByType("Person", uid, false, false, false); }
            catch (Exception e)
            {
                LogUtil.LogWarning("[VPB.Net] AddAtomByType(Person) failed: " + e.Message);
            }
            if (add != null) yield return sc.StartCoroutine(add);

            VpbNetAvatarRoster.Invalidate();
            VpbNetAvatarRoster.Poll();
            if (VpbNetAvatarRoster.Find(uid) == null)
                LogUtil.LogWarning("[VPB.Net] could not create a Person named " + uid
                    + "; the other player's avatar will not show on this side");
            _spawningAvatar = null;
        }

        // Triggers/props advertised always; object add/delete is a load-consent cap, not a rule.
        void RefreshLocalCaps()
        {
            uint fidelity = _sampler.IsBound ? _sampler.FidelityCaps : 0u;
            uint caps = VpbNetCapability.LocalWith(
                (fidelity & VpbNetCapability.Fingers) != 0,
                (fidelity & VpbNetCapability.Eyes) != 0,
                (fidelity & VpbNetCapability.Jaw) != 0,
                true,
                true,
                _syncAtoms,
                true);
            if (caps == _localCaps && _localRig.IsPresent) return;

            _localCaps = caps;
            _localRig = VpbNetRig.Describe(VpbNetRigId.VamPerson17, VpbNetControllerSet.Names, _localCaps);
            if (_peerId < 0 || !_gotJoin) return;
            SendJoin();
            RenegotiateCaps(null);
        }

        public void ReloadShareObjects()
        {
            bool want = VpbNetPresence.ShareObjects;
            if (want == _syncAtoms) return;
            _syncAtoms = want;

            // RefreshLocalCaps re-advertises — no reconnect.
            RefreshLocalCaps();
            if (_peerId < 0) return;
            if (!want) SetPropSync((_sharedCaps & VpbNetCapability.Props) != 0, false);
        }

        public void ClaimAvatar(string uid)
        {
            if (uid == null) uid = string.Empty;

            if (!VpbNetAvatarAssignment.IsValidUid(uid))
            {
                LogUtil.LogWarning("[VPB.Net] " + VpbNetAvatarAssignment.Explain(
                    VpbNetClaimResult.BadIdentifier, uid));
                return;
            }

            _desiredUid = uid;
            _reRequests = 0;
            _seatChoiceMade = true;
            VpbNetPresence.AvatarClaimDenied = false;
            VpbNetPresence.ClaimDenyReason = string.Empty;
            VpbNetPresence.PendingAvatar = string.Empty;

            if (_asHost)
            {
                VpbNetClaimResult r = _claims.Arbitrate(MySeat, uid);
                if (r == VpbNetClaimResult.Taken || r == VpbNetClaimResult.BadIdentifier)
                {
                    LogUtil.LogWarning("[VPB.Net] " + VpbNetAvatarAssignment.Explain(r, uid));
                    return;
                }
                if (r == VpbNetClaimResult.Unchanged) return;
                ApplyClaimBinding();
                BroadcastClaimState();
                return;
            }

            if (_peerId < 0) return;

            SendClaimRequest(uid);
            if (string.Equals(uid, _claims.SeatUid(MySeat), StringComparison.Ordinal)) return;
            VpbNetPresence.PendingAvatar = uid;
            _pendingClaimUntil = Time.realtimeSinceStartup + PendingClaimSeconds;
        }

        void SendClaimRequest(string uid)
        {
            if (_peerId < 0) return;
            _eventW.Begin(VpbNetEventType.AvatarClaim, _eventSeq++);
            _eventW.WriteByte(VpbNetAvatarClaimKind.Request);
            _eventW.WriteString(uid, VpbNetEventLimits.MaxIdentifier);
            int n = _eventW.End();
            if (n > 0) VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
        }

        void SendClaimDeny(string uid, byte reason)
        {
            if (!_asHost || _peerId < 0) return;
            if (!VpbNetAvatarAssignment.IsValidUid(uid)) uid = string.Empty;
            _eventW.Begin(VpbNetEventType.AvatarClaim, _eventSeq++);
            _eventW.WriteByte(VpbNetAvatarClaimKind.Deny);
            _eventW.WriteByte(reason);
            _eventW.WriteString(uid, VpbNetEventLimits.MaxIdentifier);
            int n = _eventW.End();
            if (n > 0) VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
        }

        void PumpClaimRefusals()
        {
            if (_heldClaimUid.Length == 0) return;
            if (!_asHost || _peerId < 0)
            {
                _heldClaimUid = string.Empty;
                return;
            }
            if (VpbNetRulebook.HasPendingFor(VpbNetRuleDomain.AvatarClaim,
                    VpbNetRuleAxis.Control))
                return;

            string uid = _heldClaimUid;
            _heldClaimUid = string.Empty;
            if (string.Equals(_claims.SeatUid(TheirSeat), uid, StringComparison.Ordinal)) return;
            SendClaimDeny(uid, VpbNetClaimDeny.Vetoed);
        }

        void BroadcastClaimState()
        {
            if (!_asHost || _peerId < 0) return;
            _eventW.Begin(VpbNetEventType.AvatarClaim, _eventSeq++);
            _eventW.WriteByte(VpbNetAvatarClaimKind.State);
            _eventW.WriteU32(_claims.Generation);
            _eventW.WriteString(_claims.SeatUid(VpbNetAvatarAssignment.SeatA), VpbNetEventLimits.MaxIdentifier);
            _eventW.WriteString(_claims.SeatUid(VpbNetAvatarAssignment.SeatB), VpbNetEventLimits.MaxIdentifier);
            int n = _eventW.End();
            if (n > 0) VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
        }

        void HandleAvatarClaimEvent()
        {
            byte kind = _eventR.ReadByte();
            if (_eventR.Failed) return;

            if (kind == VpbNetAvatarClaimKind.Request)
            {
                string uid = _eventR.ReadText(VpbNetEventLimits.MaxIdentifier);
                if (_eventR.Failed || uid == null) return;
                if (!_asHost) return;

                _peerSeatChoiceMade = true;

                if (!VpbNetAvatarAssignment.IsValidUid(uid))
                {
                    SendClaimDeny(uid, VpbNetClaimDeny.BadIdentifier);
                    return;
                }

                if (uid.Length > 0)
                {
                    if (_claims.IsClaimedByAnotherSeat(TheirSeat, uid))
                    {
                        LogUtil.LogWarning("[VPB.Net] the other player asked for " + uid
                            + ", which you are riding; they stay where they were");
                        SendClaimDeny(uid, VpbNetClaimDeny.Taken);
                        BroadcastClaimState();
                        return;
                    }

                    if (!VpbNetAvatarRoster.Contains(uid))
                    {
                        try { VpbNetAvatarRoster.Poll(); }
                        catch { }
                        if (!VpbNetAvatarRoster.Contains(uid))
                        {
                            SendClaimDeny(uid, VpbNetClaimDeny.Missing);
                            return;
                        }
                    }

                    // Only request is a rule question.
                    byte level = _askReplay
                        ? VpbNetRuleLevel.Allowed
                        : VpbNetRulebook.Decide(VpbNetRuleDomain.AvatarClaim,
                            VpbNetRuleAxis.Control);
                    if (!RulePasses(VpbNetRuleDomain.AvatarClaim, VpbNetRuleAxis.Control, uid))
                    {
                        if (VpbNetRulebook.HasPendingFor(VpbNetRuleDomain.AvatarClaim,
                                VpbNetRuleAxis.Control))
                            _heldClaimUid = uid;
                        else
                            SendClaimDeny(uid, level == VpbNetRuleLevel.Blocked
                                ? VpbNetClaimDeny.Blocked : VpbNetClaimDeny.Vetoed);
                        return;
                    }
                }

                VpbNetClaimResult r = _claims.Arbitrate(TheirSeat, uid);
                if (r == VpbNetClaimResult.Granted || r == VpbNetClaimResult.Released)
                {
                    _heldClaimUid = string.Empty;
                    ApplyClaimBinding();
                    BroadcastClaimState();
                }
                return;
            }

            if (kind == VpbNetAvatarClaimKind.Deny)
            {
                byte reason = _eventR.ReadByte();
                string uid = _eventR.ReadText(VpbNetEventLimits.MaxIdentifier);
                if (_eventR.Failed || uid == null || _asHost) return;

                if (_desiredUid.Length > 0
                    && string.Equals(uid, _desiredUid, StringComparison.Ordinal))
                    _desiredUid = string.Empty;

                VpbNetPresence.PendingAvatar = string.Empty;
                VpbNetPresence.AvatarClaimDenied = true;
                VpbNetPresence.ClaimDenyReason = VpbNetClaimDeny.Explain(reason, uid);
                LogUtil.LogWarning("[VPB.Net] " + VpbNetPresence.ClaimDenyReason);
                return;
            }

            if (kind != VpbNetAvatarClaimKind.State || _asHost) return;

            uint gen = _eventR.ReadU32();
            string uidA = _eventR.ReadText(VpbNetEventLimits.MaxIdentifier);
            string uidB = _eventR.ReadText(VpbNetEventLimits.MaxIdentifier);
            if (_eventR.Failed || uidA == null || uidB == null) return;

            if (!_claims.AcceptState(gen, uidA, uidB)) return;
            _seatsNeedRebase = false;
            ApplyClaimBinding();

            // Re-ask once after host's first state — pick made before connect is not lost.
            if (_desiredUid.Length == 0 || _reRequests >= ReRequestLimit) return;
            if (string.Equals(_claims.SeatUid(MySeat), _desiredUid, StringComparison.Ordinal))
            {
                _desiredUid = string.Empty;
                return;
            }
            if (_claims.IsClaimedByAnotherSeat(MySeat, _desiredUid))
            {
                LogUtil.LogWarning("[VPB.Net] " + VpbNetAvatarAssignment.Explain(
                    VpbNetClaimResult.Taken, _desiredUid));
                _desiredUid = string.Empty;
                return;
            }
            _reRequests++;
            SendClaimRequest(_desiredUid);
        }

        void AutoClaimFromSettings()
        {
            if (_autoClaimed) return;
            if (VpbNetAvatarRoster.Count == 0) return;
            _autoClaimed = true;
            if (string.IsNullOrEmpty(_localWanted)) return;
            if (!VpbNetAvatarRoster.Contains(_localWanted)) return;
            ClaimAvatar(_localWanted);
        }

        void AutoSeatPeer()
        {
            if (!_asHost || _peerId < 0) return;
            if (!_seatChoiceMade || _peerSeatChoiceMade) return;
            if (!_claims.IsSpectator(TheirSeat)) return;
            if (VpbNetRulebook.Decide(VpbNetRuleDomain.AvatarClaim, VpbNetRuleAxis.Control)
                == VpbNetRuleLevel.Blocked)
                return;

            string only = OnlyFreeAvatar();
            if (only == null) return;
            if (_claims.Arbitrate(TheirSeat, only) != VpbNetClaimResult.Granted) return;

            _peerSeatChoiceMade = true;
            ApplyClaimBinding();
            BroadcastClaimState();
            LogUtil.LogWarning("[VPB.Net] " + PeerLabel()
                + " was seated in " + only + ", the only Person left free");
        }

        string OnlyFreeAvatar()
        {
            int n = VpbNetAvatarRoster.Count;
            if (n > _seatScan.Length) n = _seatScan.Length;
            for (int i = 0; i < n; i++) _seatScan[i] = VpbNetAvatarRoster.Uid(i);
            return _claims.SoleFreeUid(_seatScan, n);
        }

        void TryOpen()
        {
            // Relaunch if broker dies — otherwise this side stays "hosting" with nothing listening.
            if ((_opened || _dialed)
                && (VpbNetBrokerLink.State == VpbNetLinkState.Idle
                    || VpbNetBrokerLink.State == VpbNetLinkState.Failed))
            {
                if (!_brokerLossLogged)
                {
                    _brokerLossLogged = true;
                    LogUtil.LogWarning("[VPB.Net] the broker process is gone ("
                        + (string.IsNullOrEmpty(VpbNetBrokerLink.LastError)
                            ? "no reason given" : VpbNetBrokerLink.LastError)
                        + "); relaunching it and "
                        + (_asHost ? "listening again" : "dialling the host again"));
                }
                _opened = false;
                _dialed = false;
                _sentJoin = false;
                _gotJoin = false;
                _keyframeSent = false;
                _contractSent = false;
            }

            if (_opened || _dialed) return;
            if (!VpbNetBrokerLink.IsReady)
            {
                // Throttle broker Start — Pump is every frame.
                if (_nowMs < _nextBrokerRetryMs) return;
                _nextBrokerRetryMs = _nowMs + BrokerRetryMs;
                VpbNetBrokerLink.Start("session");
                return;
            }
            _brokerLossLogged = false;
            if (_configRefused) return;
            if (!_asHost && !HaveJoinTarget()) return;

            _session.Begin(_nowMs);
            _opened = true;
            _dialed = true;
            _transportFail = string.Empty;
            if (!VpbNetBrokerLink.OpenSession(BackendForOpen(), _asHost, _room, ConnectBlobForOpen()))
            {
                _session.OnFatal(_nowMs, VpbNetDropReason.TransportError);
                LogUtil.LogError("[VPB.Net] could not open the session");
                return;
            }
            if (_asHost) _session.AwaitPeer(_nowMs);
        }

        void TryReconnect()
        {
            if (_session.State != VpbNetSessionState.Reconnecting) return;
            if (!_session.ConnectWanted) return;
            if (_dialed) return;
            if (!VpbNetBrokerLink.IsReady) return;

            _dialed = true;
            _sentJoin = false;
            _gotJoin = false;
            _netClock.Reset();
            _timeline.Reset();
            _applier.Buffer.Clear();
            _applier.Buffer.ResetCounters();
            _session.OnClockReady(false, _nowMs);

            VpbNetBrokerLink.CloseSession();
            if (!VpbNetBrokerLink.OpenSession(BackendForOpen(), _asHost, _room, ConnectBlobForOpen()))
            {
                _dialed = false;
                _session.OnAttemptFailed(_nowMs);
            }
        }

        public void OnSessionState(VpbIpcSession state, string invite, string text)
        {
            if (_shutdown) return;
            if (!string.IsNullOrEmpty(invite))
            {
                VpbNetPresence.Invite = invite;
                if (_asHost && !_loggedInvite)
                {
                    _loggedInvite = true;
                    LogUtil.LogWarning("[VPB.Net] hosting - " + VpbNetPresence.DescribeInviteForLog(invite));
                }
            }

            if (state == VpbIpcSession.Listening || state == VpbIpcSession.Connecting)
                VpbNetPresence.Hint = _peerId >= 0 ? string.Empty : (text == null ? string.Empty : text);
            else
                VpbNetPresence.Hint = string.Empty;

            if (state == VpbIpcSession.Failed)
            {
                _transportFail = text == null ? string.Empty : text.Trim();
                VpbNetDropReason why = VpbNetDropReason.TransportError;
                if (_session.State == VpbNetSessionState.Connecting) why = VpbNetDropReason.ConnectTimeout;
                _session.OnFatal(_nowMs, why);
            }
        }

        public void OnPeerEvent(int peerId, VpbIpcPeerEvent kind, string text)
        {
            if (_shutdown) return;
            _nowMs = _clockWatch.Elapsed.TotalMilliseconds;

            if (kind == VpbIpcPeerEvent.Up)
            {
                if (_peerId >= 0 && _peerId != peerId)
                {
                    LogUtil.LogWarning("[VPB.Net] peer " + peerId
                        + " tried to join a room that already holds two players; ignoring it");
                    return;
                }

                _peerId = peerId;
                _dialed = false;
                _sentJoin = false;
                _gotJoin = false;
                _rigOk = false;
                _keyframeSent = false;
                _keyframeIncomplete = false;
                _keyframeAttempts = 0;
                _nextKeyframeMs = 0.0;
                _contractSent = false;
                VpbNetPresence.ContentWarning = string.Empty;
                _kfIn.Reset();
                _contractIn.Reset();
                _haveContractSig = false;
                _nextJoinMs = _nowMs;
                _nextPingMs = _nowMs;
                _sharedCaps = 0;
                _peerCapsRaw = 0;
                _havePeerCaps = false;
                _loggedCaps = 0xFFFFFFFFu;
                _peerScene = string.Empty;
                _havePeerScene = false;
                VpbNetPresence.PeerScene = string.Empty;
                VpbNetPresence.HavePeerScene = false;
                _scenesMatched = false;
                _autoInvitedScene = string.Empty;
                _autoInviteAtMs = 0.0;
                _mismatchSinceMs = 0.0;
                _sampler.SetPeerCaps(0xFFFFFFFFu);
                _applier.SetFidelityCaps(0);
                SetTriggerRelay(false);
                SetPropSync(false, false);
                SetParamSync(false);
                _triggerWatermark = 0;
                _propWatermark = 0;
                _paramWatermark = 0;
                _busyWatermark = 0;
                _rulesWatermark = 0;
                _offerWatermark = 0;
                _contentStateWatermark = 0;
                _manifestIn.Reset();
                VpbNetRulebook.ResetForSession();
                _session.NotePeerReady();
                if (!_asHost) _claims.Reset();
                _reRequests = 0;
                _peerSeatChoiceMade = false;
                _heldClaimUid = string.Empty;
                VpbNetPresence.PendingAvatar = string.Empty;
                VpbNetPresence.AvatarClaimDenied = false;
                VpbNetPresence.ClaimDenyReason = string.Empty;
                ApplyClaimBinding();
                _sampler.MarkKeyframe();
                _session.OnTransportUp(_nowMs);
                LogUtil.LogWarning("[VPB.Net] peer " + peerId + " up");
                return;
            }

            if (kind == VpbIpcPeerEvent.Down)
            {
                if (_peerId >= 0 && _peerId != peerId) return;

                _peerId = -1;
                _sentJoin = false;
                _gotJoin = false;
                _dialed = false;
                _rigOk = false;
                _keyframeSent = false;
                _keyframeIncomplete = false;
                _keyframeAttempts = 0;
                _nextKeyframeMs = 0.0;
                _contractSent = false;
                _sharedCaps = 0;
                _peerCapsRaw = 0;
                _havePeerCaps = false;
                _loggedCaps = 0xFFFFFFFFu;
                _peerScene = string.Empty;
                _havePeerScene = false;
                VpbNetPresence.PeerScene = string.Empty;
                VpbNetPresence.HavePeerScene = false;
                _scenesMatched = false;
                _autoInvitedScene = string.Empty;
                _autoInviteAtMs = 0.0;
                _mismatchSinceMs = 0.0;
                VpbNetPresence.ScenesMatch = false;
                _sampler.SetPeerCaps(0xFFFFFFFFu);
                _applier.SetFidelityCaps(0);
                SetTriggerRelay(false);
                SetPropSync(false, false);
                SetParamSync(false);
                if (_asHost) _claims.ClearSeat(TheirSeat);
                else _claims.Reset();
                _peerSeatChoiceMade = false;
                _heldClaimUid = string.Empty;
                VpbNetPresence.PendingAvatar = string.Empty;
                VpbNetPresence.AvatarClaimDenied = false;
                VpbNetPresence.ClaimDenyReason = string.Empty;
                ApplyClaimBinding();
                VpbNetPresence.ContentWarning = string.Empty;
                _kfIn.Reset();
                _contractIn.Reset();
                _haveContractSig = false;
                if (_asHost) _session.AwaitPeer(_nowMs);
                else _session.OnTransportDown(_nowMs, VpbNetDropReason.TransportError);
            }
        }

        public void OnData(int peerId, byte channel, byte[] buf, int offset, int len, long arrivalTicks)
        {
            if (_shutdown || buf == null || len <= 0) return;
            _nowMs = _clockWatch.Elapsed.TotalMilliseconds;
            if (peerId != _peerId && _peerId >= 0) return;

            if (channel == VpbNetChannel.Pose)
            {
                if (len > _poseRx.Length) return;
                // Count as alive even when frozen — buffer is cleared at thaw anyway.
                if (!_syncFrozen && _applier.IsBound)
                {
                    Buffer.BlockCopy(buf, offset, _poseRx, 0, len);
                    _applier.PushFrame(_poseRx, len);
                }
                _session.OnData(_nowMs);
                return;
            }

            if (channel == VpbNetChannel.Ctrl)
            {
                HandleCtrl(peerId, buf, offset, len, arrivalTicks);
                return;
            }

            if (channel == VpbNetChannel.Keyframe)
            {
                OnKeyframeFragment(buf, offset, len);
                _session.OnData(_nowMs);
                return;
            }

            if (channel == VpbNetChannel.Contract)
            {
                OnContractFragment(buf, offset, len);
                _session.OnData(_nowMs);
                return;
            }

            if (channel == VpbNetChannel.Manifest)
            {
                OnManifestFragment(buf, offset, len);
                _session.OnData(_nowMs);
                return;
            }

            if (channel == VpbNetChannel.Props)
            {
                OnPropFrame(buf, offset, len);
                _session.OnData(_nowMs);
                return;
            }

            if (channel == VpbNetChannel.Event)
            {
                DispatchEvent(buf, offset, len);
            }
        }

        // Same dispatch path for held-then-approved events — do not handle a second copy.
        void DispatchEvent(byte[] buf, int offset, int len)
        {
            if (!_eventR.Begin(buf, offset, len))
            {
                // Older event format cannot handshake — name VersionMismatch, not a silent timeout.
                if (_eventR.Reject == VpbNetEventReject.BadVersion && !_gotJoin && !_askReplay)
                {
                    _session.OnFatal(_nowMs, VpbNetDropReason.VersionMismatch);
                    AnnounceFatalAndLeave();
                }
                return;
            }

            _evBuf = buf;
            _evOffset = offset;
            _evLen = len;

            byte type = _eventR.Type;
            // Don't drop a user-approved replay just because a load is running.
            if (!_askReplay && FrozenTo(type)) return;
            if (type == VpbNetEventType.Join) HandleJoin();
            else if (type == VpbNetEventType.Leave)
            {
                if (_asHost) _session.AwaitPeer(_nowMs);
                else _session.OnPeerBye(_nowMs);
            }
            else if (type == VpbNetEventType.Kick)
            {
                _session.OnKicked(_nowMs);
                AnnounceFatalAndLeave();
            }
            else if (type == VpbNetEventType.Clothing) HandleClothingEvent();
            else if (type == VpbNetEventType.Hair) HandleHairEvent();
            else if (type == VpbNetEventType.PresetApply) HandlePresetApplyEvent();
            else if (type == VpbNetEventType.DualPose) HandleDualPoseEvent();
            else if (type == VpbNetEventType.Morphs) HandleMorphEvent();
            else if (type == VpbNetEventType.Trigger) HandleTriggerEvent();
            else if (type == VpbNetEventType.AtomAdd) HandleAtomAddEvent();
            else if (type == VpbNetEventType.AtomRemove) HandleAtomRemoveEvent();
            else if (type == VpbNetEventType.AtomParam) HandleAtomParamEvent();
            else if (type == VpbNetEventType.SubSceneRef) HandleSubSceneEvent();
            else if (type == VpbNetEventType.AvatarClaim) HandleAvatarClaimEvent();
            else if (type == VpbNetEventType.SceneState) HandleSceneStateEvent();
            else if (type == VpbNetEventType.Busy) HandleBusyEvent();
            else if (type == VpbNetEventType.Rules) HandleRulesEvent();
            else if (type == VpbNetEventType.SceneOffer) HandleSceneOfferEvent();
            else if (type == VpbNetEventType.ContentState) HandleContentStateEvent();
            else if (type == VpbNetEventType.SceneGo) HandleSceneGoEvent();
        }

        // Local table only — never consult peer entitlements (patched peer has no check).
        bool RulePasses(byte domain, byte axis, string subject)
        {
            if (_askReplay) return true;

            byte level = VpbNetRulebook.Decide(domain, axis);
            if (level == VpbNetRuleLevel.Allowed) return true;

            if (level == VpbNetRuleLevel.Ask
                && VpbNetRulebook.Hold(domain, axis, AskText(domain, axis, subject),
                    _evBuf, _evOffset, _evLen, _nowMs))
            {
                return false;
            }

            VpbNetRulebook.NoteRefused();
            LogUtil.LogWarning("[VPB.Net] refused: " + Describe(domain, axis, subject)
                + " - your session rules have that set to " + VpbNetRuleLevel.Name(level));
            return false;
        }

        string AskText(byte domain, byte axis, string subject)
        {
            return PeerLabel(true) + " wants to " + Describe(domain, axis, subject) + ".";
        }

        static string Describe(byte domain, byte axis, string subject)
        {
            string what = string.IsNullOrEmpty(subject) ? VpbNetRuleDomain.Name(domain) : subject;
            return axis == VpbNetRuleAxis.Control
                ? "put " + what + " on your side"
                : "change " + what + " on their own avatar here";
        }

        void PumpApprovedAsks()
        {
            for (int guard = 0; guard < VpbNetRulebook.MaxPendingAsks; guard++)
            {
                int len = VpbNetRulebook.TryTakeApproved(_askReplayBuf);
                if (len <= 0) return;

                _askReplay = true;
                try { DispatchEvent(_askReplayBuf, 0, len); }
                catch (Exception e)
                {
                    LogUtil.LogWarning("[VPB.Net] an approved change failed to apply: " + e.Message);
                }
                finally { _askReplay = false; }
            }
        }

        void SendRules()
        {
            if (_peerId < 0) return;
            _eventW.Begin(VpbNetEventType.Rules, _eventSeq);
            VpbNetRuleTable.Write(_eventW, VpbNetRulebook.Local);
            int len = _eventW.End();
            if (len <= 0) return;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _eventSeq++;
        }

        void HandleRulesEvent()
        {
            uint seq = _eventR.Seq;
            VpbNetRuleTable t;
            if (!VpbNetRuleTable.Read(_eventR, out t))
            {
                // Unreadable table is not guessed at — fall back to what an older build could mean.
                VpbNetRulebook.NotePeerCaps(0u);
                return;
            }
            if (!Newer(seq, _rulesWatermark)) return;
            _rulesWatermark = seq;

            VpbNetRulebook.NotePeerTable(t);
            LogUtil.LogWarning("[VPB.Net] the other player lets you change: "
                + VpbNetRulebook.DescribePeer());
        }

        void HandleCtrl(int peerId, byte[] buf, int offset, int len, long arrivalTicks)
        {
            if (len < 1) return;

            // Ping feeds liveness only — a dead pose stream with a live link must still stall.
            _session.NotePeerAlive(_nowMs);

            byte type = buf[offset];
            if (type == CtrlPing && len >= 9)
            {
                long t0 = VpbIpc.ReadI64(buf, offset + 1);
                long now = _clockWatch.ElapsedTicks;
                _ctrl[0] = CtrlPong;
                VpbIpc.WriteI64(_ctrl, 1, t0);
                VpbIpc.WriteI64(_ctrl, 9, now);
                VpbIpc.WriteI64(_ctrl, 17, now - (arrivalTicks - _epochTicks));
                VpbNetBrokerLink.SendData(peerId, VpbNetChannel.Ctrl, _ctrl, 25, false);
                return;
            }

            if (type == CtrlResync)
            {
                // Asker's applier waits for a flagged pose.
                _sampler.MarkKeyframe();
                // Don't send by hand too — retry path sends the one keyframe.
                _keyframeSent = false;
                _keyframeIncomplete = false;
                _keyframeAttempts = 0;
                _nextKeyframeMs = 0.0;
                // Hair is not in the keyframe — clear sig so it resends.
                _haveHairSig = false;
                return;
            }

            if (type == CtrlPong && len >= 17)
            {
                long t0 = VpbIpc.ReadI64(buf, offset + 1);
                long remote = VpbIpc.ReadI64(buf, offset + 9);

                if (len >= 25) _peerServiceMs = VpbNetBrokerLink.TicksToMs(VpbIpc.ReadI64(buf, offset + 17));

                _netClock.AddSample(t0, remote, _clockWatch.ElapsedTicks);
                _session.OnClockReady(_netClock.Synced, _nowMs);
            }
        }

        void MaybePing()
        {
            if (_peerId < 0) return;
            if (_nowMs < _nextPingMs) return;
            _nextPingMs = _nowMs + (_netClock.Synced ? 1000.0 : 100.0);
            _ctrl[0] = CtrlPing;
            VpbIpc.WriteI64(_ctrl, 1, _clockWatch.ElapsedTicks);
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Ctrl, _ctrl, 9, false);
        }

        void MaybeJoin()
        {
            if (_peerId < 0 || _sentJoin && _gotJoin) return;
            if (_nowMs < _nextJoinMs) return;
            if (_joinRetries > 10 && _sentJoin) return;
            _nextJoinMs = _nowMs + JoinRetryMs;
            SendJoin();
            SendRules();
            _sentJoin = true;
            _joinRetries++;
        }

        void SendJoin()
        {
            if (_peerId < 0) return;
            _eventW.Begin(VpbNetEventType.Join, _eventSeq++);
            _eventW.WriteU16(_peerId);
            _eventW.WriteU32(_localCaps);
            VpbNetRig.Write(_eventW, _localRig);
            int n = _eventW.End();
            if (n > 0) VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
        }

        void SendLeave()
        {
            if (_peerId < 0) return;
            for (int i = 0; i < LeaveRepeats; i++)
            {
                _eventW.Begin(VpbNetEventType.Leave, _eventSeq++);
                int n = _eventW.End();
                if (n > 0) VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
            }
        }

        public void Kick()
        {
            if (!_asHost)
            {
                LogUtil.LogWarning("[VPB.Net] only the host can remove someone");
                return;
            }
            if (_peerId < 0)
            {
                LogUtil.LogWarning("[VPB.Net] no peer to remove");
                return;
            }
            for (int i = 0; i < LeaveRepeats; i++)
            {
                _eventW.Begin(VpbNetEventType.Kick, _eventSeq++);
                int n = _eventW.End();
                if (n > 0) VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
            }
            LogUtil.LogWarning("[VPB.Net] removed " + PeerLabel());
        }

        void HandleJoin()
        {
            _eventR.ReadU16();
            uint caps = _eventR.ReadU32();
            if (_eventR.Failed) return;

            VpbNetRigDescriptor theirs = VpbNetRig.Read(_eventR, caps);
            VpbNetRigCompat compat = VpbNetRig.Check(_localRig, theirs);
            if (VpbNetRig.IsFatal(compat))
            {
                LogUtil.LogError("[VPB.Net] " + VpbNetRig.Explain(compat, _localRig, theirs));
                _rigOk = false;
                _session.OnFatal(_nowMs, VpbNetDropReason.VersionMismatch);
                VpbNetPresence.Leave();
                return;
            }

            _rigOk = true;
            _gotJoin = true;
            _peerCapsRaw = caps;
            _havePeerCaps = true;

            // Peer restart recounts from 1 — keep old watermarks and every event looks already seen.
            _clothingWatermark = 0;
            _morphWatermark = 0;
            _hairWatermark = 0;
            _presetWatermark = 0;
            _dualPoseWatermark = 0;
            _triggerWatermark = 0;
            _busyWatermark = 0;
            _rulesWatermark = 0;
            VpbNetRulebook.NotePeerCaps(caps);
            _session.NotePeerReady();
            _haveHairSig = false;
            _nextPeriodicResyncMs = _nowMs + PeriodicResyncMs;
            if (_asHost) BroadcastClaimState();
            SendSceneState();
            RenegotiateCaps(VpbNetRig.Explain(compat, _localRig, theirs));
            if (!_sentJoin) SendJoin();
            SendRules();
        }

        // Re-arm everything off _sharedCaps whenever either side's intersection bits move.
        void RenegotiateCaps(string rigNote)
        {
            if (!_havePeerCaps) return;

            _sharedCaps = VpbNetCapability.Intersect(_localCaps, _peerCapsRaw);
            _applier.SetFidelityCaps(_sharedCaps & VpbNetCapability.FidelityTier);
            _sampler.SetPeerCaps(_peerCapsRaw);
            SetTriggerRelay((_sharedCaps & VpbNetCapability.Triggers) != 0
                && RuleArms(VpbNetRuleDomain.Triggers));
            SetPropSync((_sharedCaps & VpbNetCapability.Props) != 0
                    && RuleArms(VpbNetRuleDomain.Objects),
                (_sharedCaps & VpbNetCapability.Atoms) != 0
                    && RuleArms(VpbNetRuleDomain.Objects));
            SetParamSync((_sharedCaps & VpbNetCapability.Params) != 0
                && RuleArms(VpbNetRuleDomain.Params));

            if (_loggedRig && _sharedCaps == _loggedCaps) return;
            _loggedRig = true;
            _loggedCaps = _sharedCaps;

            StringBuilder sb = new StringBuilder(64);
            VpbNetCapability.Describe(sb, _sharedCaps);
            LogUtil.LogWarning("[VPB.Net] " + (rigNote == null ? "capabilities changed" : rigNote)
                + ", running at " + sb.ToString());

            uint mineOnly = _localCaps & ~_peerCapsRaw
                & (VpbNetCapability.FidelityTier | VpbNetCapability.Triggers
                    | VpbNetCapability.Props | VpbNetCapability.Atoms | VpbNetCapability.Params);
            if (mineOnly == 0) return;

            sb.Length = 0;
            VpbNetCapability.Describe(sb, mineOnly);
            LogUtil.LogWarning("[VPB.Net] that peer does not take " + sb.ToString()
                + "; this side keeps sending nothing for it rather than sending it into a void");
        }

        // Streams: Ask == Allowed (arm if not Blocked). Discrete add/delete still uses RulePasses.
        bool RuleArms(byte domain)
        {
            return VpbNetRulebook.Decide(domain, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Blocked;
        }

        // Push local table and re-arm streams — panel must match what the session does.
        void OnLocalRulesChanged()
        {
            SendRules();
            RenegotiateCaps(null);
        }

        void PollLook()
        {
            if (_peerId < 0 || !_rigOk) return;
            if ((_sharedCaps & VpbNetCapability.Events) == 0) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextLookPoll) return;
            _nextLookPoll = now + LookPollSeconds;

            if (_remoteLook.IsAlive())
            {
                // Replay look recorded before this avatar existed, first poll it is really there.
                if (!_remoteLookPrimed)
                {
                    _remoteLookPrimed = true;
                    if (_peerState.ClothingCount > 0 || _peerState.MorphCount > 0)
                    {
                        _remoteLook.ApplyState(_peerState);
                        LogUtil.LogWarning("[VPB.Net] put the peer's known look on their avatar: "
                            + _peerState.ClothingCount + " clothing, " + _peerState.MorphCount + " morphs");
                    }
                    if (_peerHair.Count > 0) _hairApplied += _remoteLook.ApplyHairSet(_peerHair);
                }

                _remoteLook.RetryPendingClothing();
                _remoteLook.RetryPendingHair();
                _remoteLook.RetryPendingMorphs();
            }
            else _remoteLookPrimed = false;

            if (!_localLook.IsAlive())
            {
                _haveClothingSig = false;
                return;
            }

            int found = _localLook.CollectClothingChanges(_changeIds, _changeOn);
            if (found > 0) SendClothing();
            RepairClothing();

            SendHairIfChanged();

            _localLook.SyncMorphs = MorphScanWanted();
            if (_localLook.CollectMorphChanges(_changeIds, _changeValue) > 0)
                SendMorphs();
        }

        // Advisory scan only — unpublished peer table is legacy grant.
        static bool MorphScanWanted()
        {
            return VpbNetRulebook.PeerWouldAccept(VpbNetRuleDomain.Morphs, VpbNetRuleAxis.Mirror);
        }

        // Hair uses clothing switch; whole worn set each change.
        void SendHairIfChanged()
        {
            int count;
            int sig = _localLook.ActiveHairSignature(out count);
            if (_haveHairSig && sig == _hairSigTold) return;

            _haveHairSig = true;
            _hairSigTold = sig;
            SendHair();
        }

        void SendHair()
        {
            if (_peerId < 0 || !_localLook.IsAlive()) return;

            int n = _localLook.CollectActiveHair(_hairIds, VpbNetEventLimits.MaxHairItems);

            _eventW.Begin(VpbNetEventType.Hair, _eventSeq);
            _eventW.WriteByte(VpbNetEventTarget.Self);
            _eventW.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
                _eventW.WriteString(_hairIds[i], VpbNetEventLimits.MaxIdentifier);

            int len = _eventW.End();
            if (len <= 0)
            {
                LogUtil.LogWarning("[VPB.Net] this avatar's hair names are too long to fit one"
                    + " datagram; the other side keeps the hair it has");
                return;
            }

            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _eventSeq++;
            _hairSent++;
            LogUtil.LogWarning("[VPB.Net] sent this avatar's hair: " + n + " item(s)"
                + (n > 0 ? ", first " + _hairIds[0] : " (bald)"));
        }

        // Per-item clothing is best-effort; settled mismatch ships one authoritative keyframe.
        void RepairClothing()
        {
            int count;
            int sig = _localLook.ActiveClothingSignature(out count);

            bool settled = _haveClothingSig && sig == _clothingSigSeen;
            _clothingSigSeen = sig;
            _haveClothingSig = true;
            if (!settled || sig == _clothingSigTold) return;

            _clothingRepairs++;
            LogUtil.LogWarning("[VPB.Net] outfit settled at " + count
                + " item(s) and does not match what the other side was told;"
                + " sending the whole thing");
            _clothingSigTold = sig;
            SendKeyframe();
        }

        int SendClothing()
        {
            int n = _changeIds.Count;
            if (n > VpbNetEventPack.MaxItemsPerEvent) n = VpbNetEventPack.MaxItemsPerEvent;
            while (n > 0 && !FitsAll(n, 1)) n--;
            if (n <= 0) return 0;

            _eventW.Begin(VpbNetEventType.Clothing, _eventSeq);
            _eventW.WriteByte(VpbNetEventTarget.Self);
            _eventW.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                _eventW.WriteString(_changeIds[i], VpbNetEventLimits.MaxIdentifier);
                _eventW.WriteByte((byte)(_changeOn[i] ? 1 : 0));
                if (!_localState.SetClothing(_changeIds[i], _changeOn[i], _eventSeq)) NoteLookCap();
            }

            int len = _eventW.End();
            if (len <= 0) return 0;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _localLook.CommitClothing(n);
            _eventSeq++;

            LogUtil.LogWarning("[VPB.Net] sent " + n + " clothing change(s), first: "
                + _changeIds[0] + (_changeOn[0] ? " on" : " off"));
            return n;
        }

        void SendMorphs()
        {
            int n = _changeIds.Count;
            if (n > VpbNetEventPack.MaxItemsPerEvent) n = VpbNetEventPack.MaxItemsPerEvent;
            while (n > 0 && !FitsAll(n, 2)) n--;
            if (n <= 0) return;

            _eventW.Begin(VpbNetEventType.Morphs, _eventSeq);
            _eventW.WriteByte(VpbNetEventTarget.Self);
            _eventW.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                _eventW.WriteString(_changeIds[i], VpbNetEventLimits.MaxIdentifier);
                _eventW.WriteI16(VpbNetEventCodec.QuantizeMorph(_changeValue[i]));
                if (!_localState.SetMorph(_changeIds[i], _changeValue[i], _eventSeq)) NoteLookCap();
            }

            int len = _eventW.End();
            if (len <= 0) return;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _localLook.CommitMorphs(n);
            _eventSeq++;
        }

        bool FitsAll(int count, int valueBytes)
        {
            int used = 1;
            for (int i = 0; i < count; i++)
            {
                if (!VpbNetEventPack.Fits(used, _changeIds[i], valueBytes)) return false;
                used += VpbNetEventPack.ItemBytes(_changeIds[i], valueBytes);
            }
            return true;
        }

        void NoteLookCap()
        {
            if (_warnedLookCap) return;
            _warnedLookCap = true;
            LogUtil.LogWarning("[VPB.Net] this session has changed more clothing or morphs than a resync can carry ("
                + VpbNetEventLimits.MaxClothingItems + " items / " + VpbNetEventLimits.MaxMorphs
                + " morphs); the newest changes still apply live, but a rejoin will not replay them");
        }

        public void RequestResync()
        {
            if (_peerId < 0)
            {
                LogUtil.LogWarning("[VPB.Net] no peer to resync with");
                return;
            }

            // Don't re-press while a keyframe is in flight — newer gen abandons the one about to land.
            if (_nowMs - _lastResyncAskMs < ResyncMinIntervalMs)
            {
                LogUtil.LogWarning("[VPB.Net] a resync is already on its way - pressing again"
                    + " throws it away and starts over. Give it a second.");
                return;
            }
            _lastResyncAskMs = _nowMs;

            _ctrl[0] = CtrlResync;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Ctrl, _ctrl, 1, true);
            SendKeyframe();
            LogUtil.LogWarning("[VPB.Net] resync requested");
        }

        void MaybeResumeResync()
        {
            int resumes = _session.Resumes;
            if (resumes == _lastResumes) return;
            _lastResumes = resumes;
            if (_peerId < 0 || !_rigOk) return;

            _ctrl[0] = CtrlResync;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Ctrl, _ctrl, 1, true);
            SendKeyframe();
            LogUtil.LogWarning("[VPB.Net] peer resumed after a stall; resyncing look state");
        }

        // Timer resync — catches silent drift nothing else noticed.
        void MaybePeriodicResync()
        {
            if (_peerId < 0 || !_gotJoin || !_rigOk) return;
            if ((_sharedCaps & VpbNetCapability.Keyframe) == 0) return;
            if (_nowMs < _nextPeriodicResyncMs) return;

            _nextPeriodicResyncMs = _nowMs + PeriodicResyncMs;
            if (!_localLook.IsAlive()) return;

            _periodicResyncs++;
            SendKeyframe();
            _haveHairSig = false;
        }

        // Retry keyframe until ClothingAuthoritative.
        void MaybeSendKeyframe()
        {
            if (_peerId < 0 || !_gotJoin || !_rigOk) return;
            if ((_sharedCaps & VpbNetCapability.Keyframe) == 0) return;
            if (_keyframeSent && !_keyframeIncomplete) return;
            if (_nowMs < _nextKeyframeMs) return;

            _nextKeyframeMs = _nowMs + KeyframeRetryMs;
            SendKeyframe();
            _keyframeSent = true;

            bool ready = _localState.ClothingAuthoritative || !_localLook.IsAlive();
            if (ready)
            {
                if (_keyframeIncomplete)
                    LogUtil.LogWarning("[VPB.Net] this avatar's outfit is readable now ("
                        + _localState.ClothingCount + " items) and has been sent after "
                        + _keyframeAttempts + " attempts");
                _keyframeIncomplete = false;
                _keyframeAttempts = 0;
                return;
            }

            _keyframeAttempts++;
            if (_keyframeAttempts >= KeyframeMaxAttempts)
            {
                _keyframeIncomplete = false;
                LogUtil.LogWarning("[VPB.Net] this avatar's clothing never became readable after "
                    + _keyframeAttempts + " tries; the other side may show it undressed."
                    + " Press Resync on the session panel once it looks right here.");
                return;
            }

            _keyframeIncomplete = true;
            if (_keyframeAttempts == 1)
                LogUtil.LogWarning("[VPB.Net] this avatar's clothing is not readable yet"
                    + " (tracked " + _localLook.ClothingTracked + " items); retrying the outfit send");
        }

        void SendKeyframe()
        {
            if (_peerId < 0) return;

            _localState.PeerId = (ushort)(_peerId & 0xFFFF);
            if (_lastFrameLen > 0) _localState.SetPose(_send, 0, _lastFrameLen);
            RefreshLocalClothing();

            // Outfit in this keyframe is now what the peer knows.
            if (_localLook.IsAlive())
            {
                int shipped;
                _clothingSigTold = _localLook.ActiveClothingSignature(out shipped);
                _clothingSigSeen = _clothingSigTold;
                _haveClothingSig = true;
            }

            int whole = _localState.Write(_kfWhole);
            if (whole <= 0)
            {
                LogUtil.LogWarning("[VPB.Net] could not build a resync keyframe");
                return;
            }

            _kfGen = (_kfGen + 1) & 0xFFFF;
            int count = VpbNetKeyframeAssembler.FragmentCount(whole);
            for (int i = 0; i < count; i++)
            {
                int n = VpbNetKeyframeAssembler.WriteFragment(_kfFrag, _kfWhole, whole, _kfGen, i);
                if (n <= 0) return;
                VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Keyframe, _kfFrag, n, true);
            }
            _kfSent++;

            LogUtil.LogWarning("[VPB.Net] sent resync: " + _localState.ClothingCount
                + " clothing, " + _localState.MorphCount + " morphs"
                + (_localState.ClothingAuthoritative
                    ? string.Empty
                    : " (partial - only " + _localLook.ClothingTracked
                        + " clothing slots readable on this avatar so far)"));
        }

        void MaybeSendContract()
        {
            if (_contractSent || _peerId < 0 || !_gotJoin || !_rigOk) return;
            if ((_sharedCaps & VpbNetCapability.Contract) == 0)
            {
                _contractSent = true;
                LogUtil.LogWarning("[VPB.Net] peer does not exchange content contracts; "
                    + "missing packages will show up as a broken scene, not as a report");
                return;
            }
            _contractSent = true;
            SendContract();
        }

        void SendContract()
        {
            if (_peerId < 0) return;

            string note;
            try
            {
                if (!VpbNetContentContract.Build(_localContract, _localAtom, _asHost, out note))
                {
                    LogUtil.LogWarning("[VPB.Net] could not describe this machine's content");
                    return;
                }
            }
            catch (Exception e)
            {
                LogUtil.LogWarning("[VPB.Net] could not describe this machine's content: " + e.Message);
                return;
            }
            if (!string.IsNullOrEmpty(note)) LogUtil.LogWarning("[VPB.Net] content contract: " + note);

            int whole = _localContract.Write(_contractWhole);
            if (whole <= 0)
            {
                LogUtil.LogWarning("[VPB.Net] could not pack this machine's content contract");
                return;
            }

            _contractGen = (_contractGen + 1) & 0xFFFF;
            int count = VpbNetKeyframeAssembler.FragmentCount(whole);
            for (int i = 0; i < count; i++)
            {
                int n = VpbNetKeyframeAssembler.WriteFragment(_kfFrag, _contractWhole, whole, _contractGen, i);
                if (n <= 0) return;
                VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Contract, _kfFrag, n, true);
            }
            LogUtil.LogWarning("[VPB.Net] sent content contract: " + _localContract.Count
                + " packages, " + whole + " B in " + count + " fragments");
        }

        public bool PeerTakesContent
        {
            get { return _peerId >= 0 && (_sharedCaps & VpbNetCapability.Content) != 0; }
        }

        public bool InviteToScene(string scenePath, bool editMode)
        {
            if (!PeerTakesContent) return false;
            return VpbNetContentSync.BeginHostOffer(scenePath, editMode, _nowMs);
        }

        void SendSceneOffer(VpbNetOfferInfo offer)
        {
            if (_peerId < 0) return;
            if ((_sharedCaps & VpbNetCapability.Content) == 0)
            {
                LogUtil.LogWarning("[VPB.Net] the other player's build cannot be invited to a scene;"
                    + " they will have to open it themselves");
                return;
            }

            _eventW.Begin(VpbNetEventType.SceneOffer, _eventSeq);
            offer.Write(_eventW);
            int n = _eventW.End();
            if (n <= 0)
            {
                LogUtil.LogWarning("[VPB.Net] that scene could not be packed into an invite");
                return;
            }
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
            _eventSeq++;
        }

        void SendContentState(VpbNetContentStatus status)
        {
            if (_peerId < 0) return;
            if ((_sharedCaps & VpbNetCapability.Content) == 0) return;

            _eventW.Begin(VpbNetEventType.ContentState, _eventSeq);
            status.Write(_eventW);
            int n = _eventW.End();
            if (n <= 0) return;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, n, true);
            _eventSeq++;
        }

        void SendManifest(VpbNetManifest manifest)
        {
            if (_peerId < 0 || manifest == null) return;
            if ((_sharedCaps & VpbNetCapability.Content) == 0) return;

            int whole = manifest.Write(_manifestWhole);
            if (whole <= 0)
            {
                LogUtil.LogWarning("[VPB.Net] the package list for that scene could not be packed");
                return;
            }

            _manifestGen = (_manifestGen + 1) & 0xFFFF;
            int count = VpbNetKeyframeAssembler.FragmentCount(whole);
            for (int i = 0; i < count; i++)
            {
                int n = VpbNetKeyframeAssembler.WriteFragment(_kfFrag, _manifestWhole, whole, _manifestGen, i);
                if (n <= 0) return;
                VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Manifest, _kfFrag, n, true);
            }
            LogUtil.LogWarning("[VPB.Net] sent the package list: " + manifest.Count
                + " packages, " + whole + " B in " + count + " fragments");
        }

        void OnManifestFragment(byte[] buf, int offset, int len)
        {
            VpbNetKeyframeReject r = _manifestIn.Offer(buf, offset, len, _nowMs);
            if (r != VpbNetKeyframeReject.None && r != VpbNetKeyframeReject.Duplicate)
            {
                LogUtil.LogWarning("[VPB.Net] package list fragment refused: " + r);
                return;
            }
            if (!_manifestIn.IsComplete) return;

            int n = _manifestIn.Take(_manifestRxBytes);
            if (n <= 0) return;

            VpbNetContractReject read = _manifestRx.Read(_manifestRxBytes, 0, n);
            if (read != VpbNetContractReject.None)
            {
                LogUtil.LogError("[VPB.Net] the package list could not be read: "
                    + VpbNetContract.Explain(read));
                return;
            }

            VpbNetContentSync.NoteManifest(_manifestRx, _nowMs);
        }

        // Offers skip RulePasses — card is the prompt; Evaluate gates LoadSceneAs.
        void HandleSceneOfferEvent()
        {
            uint seq = _eventR.Seq;
            VpbNetOfferInfo offer;
            if (!VpbNetOfferInfo.TryRead(_eventR, out offer))
            {
                LogUtil.LogWarning("[VPB.Net] an invite arrived that this build refuses to read");
                return;
            }
            if (!Newer(seq, _offerWatermark)) return;
            _offerWatermark = seq;

            if (_asHost && VpbNetContentSync.HasOffer && VpbNetContentSync.Mine
                && VpbNetContentSync.ExchangeLive(_nowMs))
            {
                LogUtil.LogWarning("[VPB.Net] " + PeerLabel() + " invited you to their scene at the same"
                    + " moment you invited them to yours - yours stands, so theirs was dropped;"
                    + " load their scene yourself if you would rather go there");
                return;
            }

            VpbNetContentSync.NoteOffer(offer, _nowMs);
        }

        void HandleContentStateEvent()
        {
            uint seq = _eventR.Seq;
            VpbNetContentStatus status;
            if (!VpbNetContentStatus.TryRead(_eventR, out status)) return;
            if (!Newer(seq, _contentStateWatermark)) return;
            _contentStateWatermark = seq;

            VpbNetContentSync.NoteStatus(status);
        }

        void HandleSceneGoEvent()
        {
            uint id = _eventR.ReadU32();
            if (_eventR.Failed) return;
            VpbNetContentSync.NoteGo(id);
        }

        // Hold missing-package refs instead of applying into a silent VaM fail.
        bool HoldIfContentMissing(string reference)
        {
            if (_askReplay) return false;
            if (_evBuf == null || _evLen <= 0) return false;

            string pkg = VpbNetContentContract.PackageUidOf(reference);
            if (string.IsNullOrEmpty(pkg)) return false;
            if (ContentSatisfied(pkg)) return false;

            return VpbNetContentSync.HoldForAsset(pkg, reference, _evBuf, _evOffset, _evLen, _nowMs);
        }

        void WantContentFor(string reference)
        {
            if (_askReplay) return;
            string pkg = VpbNetContentContract.PackageUidOf(reference);
            if (string.IsNullOrEmpty(pkg)) return;
            if (ContentSatisfied(pkg)) return;
            VpbNetContentSync.WantAsset(pkg, reference, _nowMs);
        }

        static bool ContentSatisfied(string packageUid)
        {
            try { return FileManager.IsDependencySatisfiedByInstalled(packageUid); }
            catch { return true; }
        }

        void PumpContentReplays()
        {
            for (int guard = 0; guard < VpbNetContentSync.MaxHolds; guard++)
            {
                int len = VpbNetContentSync.TryTakeReady(_contentReplayBuf);
                if (len <= 0) return;

                _askReplay = true;
                try { DispatchEvent(_contentReplayBuf, 0, len); }
                catch (Exception e)
                {
                    LogUtil.LogWarning("[VPB.Net] a change that was waiting on a download failed to apply: "
                        + e.Message);
                }
                finally { _askReplay = false; }
            }
        }

        void OnContractFragment(byte[] buf, int offset, int len)
        {
            VpbNetKeyframeReject r = _contractIn.Offer(buf, offset, len, _nowMs);
            if (r != VpbNetKeyframeReject.None && r != VpbNetKeyframeReject.Duplicate)
            {
                LogUtil.LogWarning("[VPB.Net] contract fragment refused: " + r);
                return;
            }
            if (!_contractIn.IsComplete) return;

            int n = _contractIn.Take(_contractRx);
            if (n <= 0) return;

            VpbNetContractReject read = _peerContract.Read(_contractRx, 0, n);
            if (read != VpbNetContractReject.None)
            {
                LogUtil.LogError("[VPB.Net] " + VpbNetContract.Explain(read));
                return;
            }

            CheckContract();
        }

        void CheckContract()
        {
            VpbNetContractCheck.Compare(_peerContract, _catalog, _contractReport);

            string summary = _contractReport.Summary();

            // Don't reprint the same contract verdict on every avatar rebind.
            uint sig = ContractSignature();
            bool changed = !_haveContractSig || sig != _contractSig;
            _contractSig = sig;
            _haveContractSig = true;

            if (changed)
            {
                LogUtil.LogWarning("[VPB.Net] content: " + summary
                    + (_peerContract.HasScene ? " (peer scene " + _peerContract.SceneUid + ")" : string.Empty));

                for (int i = 0; i < _contractReport.IssueCount; i++)
                {
                    string line = _contractReport.Describe(i);
                    if (line.Length == 0) continue;

                    if (_contractReport.Kind(i) == VpbNetContractIssueKind.MissingPackage)
                        line += " - " + HubHint(_contractReport.Uid(i));

                    LogUtil.LogWarning("[VPB.Net] content: " + line);
                }

                if (_contractReport.OverflowedIssues > 0)
                    LogUtil.LogWarning("[VPB.Net] content: and " + _contractReport.OverflowedIssues + " more not listed");
            }

            VpbNetPresence.ContentWarning =
                _contractReport.Verdict == VpbNetContractVerdict.Match ? string.Empty : summary;
        }

        uint ContractSignature()
        {
            uint h = 2166136261u;
            h = Mix(h, (uint)_contractReport.Verdict);
            h = Mix(h, (uint)_contractReport.IssueCount);
            h = Mix(h, (uint)_contractReport.OverflowedIssues);
            for (int i = 0; i < _contractReport.IssueCount; i++)
            {
                h = Mix(h, (uint)_contractReport.Kind(i));
                string uid = _contractReport.Uid(i);
                if (uid == null) continue;
                for (int c = 0; c < uid.Length; c++) h = Mix(h, uid[c]);
            }
            return h;
        }

        static uint Mix(uint h, uint v)
        {
            h ^= v;
            return h * 16777619u;
        }

        static string HubHint(string packageUid)
        {
            if (string.IsNullOrEmpty(packageUid)) return "no Hub page";
            try
            {
                HubBrowse hub = HubBrowse.singleton;
                if (hub == null) return "open the Hub and search for " + packageUid;

                string rid = null;
                try { rid = hub.GetPackageHubResourceId(packageUid); }
                catch { rid = null; }

                if (!string.IsNullOrEmpty(rid) && rid != "null")
                    return "Hub resource " + rid;

                return "not on the Hub (delisted, paid, or the host made it); ask them for it directly";
            }
            catch { return "no Hub page"; }
        }

        void RefreshLocalClothing()
        {
            _localState.ClearClothing();
            if (!_localLook.IsAlive()) return;

            int n = _localLook.CollectActiveClothing(_changeIds, _changeOn, VpbNetEventLimits.MaxClothingItems);
            uint seq = _eventSeq == 0 ? 0u : _eventSeq - 1u;

            bool all = true;
            for (int i = 0; i < n; i++)
            {
                if (!_localState.SetClothing(_changeIds[i], _changeOn[i], seq)) all = false;
            }

            _localState.ClothingAuthoritative =
                all && n < VpbNetEventLimits.MaxClothingItems && _localLook.ClothingTracked > 0;
            if (!_localState.ClothingAuthoritative) NoteLookCap();
        }

        void OnKeyframeFragment(byte[] buf, int offset, int len)
        {
            // Don't apply a keyframe mid-load — redressing a Person still assembling tears it.
            if (_syncFrozen) return;

            VpbNetKeyframeReject r = _kfIn.Offer(buf, offset, len, _nowMs);
            if (r != VpbNetKeyframeReject.None && r != VpbNetKeyframeReject.Duplicate)
            {
                LogUtil.LogWarning("[VPB.Net] resync fragment refused: " + r);
                return;
            }
            if (!_kfIn.IsComplete) return;

            int n = _kfIn.Take(_kfRx);
            if (n <= 0) return;

            // Don't Read() a keyframe older than applied events.
            uint incoming;
            if (VpbNetPeerState.TryPeekEventSeq(_kfRx, 0, n, out incoming)
                && _peerState.EventSeq != 0 && incoming < _peerState.EventSeq)
            {
                _kfStale++;
                LogUtil.LogWarning("[VPB.Net] ignored a resync keyframe older than what has already"
                    + " been applied (" + incoming + " < " + _peerState.EventSeq + ")");
                return;
            }

            VpbNetKeyframeReject read = _peerState.Read(_kfRx, 0, n);
            if (read != VpbNetKeyframeReject.None)
            {
                LogUtil.LogWarning("[VPB.Net] resync keyframe refused: " + read);
                return;
            }

            if (_remoteLook.IsAlive()) _remoteLook.ApplyState(_peerState);
            _kfApplied++;
            LogUtil.LogWarning("[VPB.Net] applied peer resync: " + _peerState.ClothingCount
                + " clothing, " + _peerState.MorphCount + " morphs");
        }

        void SetTriggerRelay(bool on)
        {
            if (on == _triggersOn) return;
            _triggersOn = on;
            if (on) VpbNetTriggerRelay.Begin();
            else VpbNetTriggerRelay.End();

            if (!on && _peerId >= 0 && _havePeerCaps)
                LogUtil.LogWarning("[VPB.Net] that peer does not relay triggers, so a door one of you opens"
                    + " stays shut on the other side");
        }

        void PollTriggers()
        {
            if (!_triggersOn || _peerId < 0 || !_rigOk) return;
            if (VpbNetTriggerRelay.Pending <= 0) return;

            int n = VpbNetTriggerRelay.Take(_changeIds, _triggerStorables, _changeOn,
                VpbNetEventLimits.MaxTriggersPerEvent);
            while (n > 0 && !FitsTriggers(n)) n--;
            if (n <= 0) return;

            _eventW.Begin(VpbNetEventType.Trigger, _eventSeq);
            _eventW.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                _eventW.WriteString(_changeIds[i], VpbNetEventLimits.MaxIdentifier);
                _eventW.WriteString(_triggerStorables[i], VpbNetEventLimits.MaxIdentifier);
                _eventW.WriteByte((byte)(_changeOn[i] ? 1 : 0));
            }

            int len = _eventW.End();
            if (len <= 0) return;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            VpbNetTriggerRelay.Commit(n);
            _eventSeq++;
            _triggersSent += n;
        }

        bool FitsTriggers(int count)
        {
            int used = 1;
            for (int i = 0; i < count; i++)
            {
                int need = VpbNetEventPack.ItemBytes(_changeIds[i], 0)
                    + VpbNetEventPack.ItemBytes(_triggerStorables[i], 1);
                if (used + need > VpbNetEventLimits.MaxPayload) return false;
                used += need;
            }
            return true;
        }

        void SetPropSync(bool props, bool atoms)
        {
            bool want = (props || atoms) && (_peerId < 0 || _scenesMatched);
            if (want == _propsOn && (!want || _props.LifecycleOn == atoms)) return;
            _propsOn = want;

            if (!want)
            {
                _props.Unbind();
                _heldSubUid.Clear();
                _heldSubRef.Clear();
                return;
            }

            _props.Bind(_localAtom, _remoteAtom, atoms);
            _props.AnnounceExisting = _asHost;
            _props.RefreshNow();
            LogUtil.LogWarning("[VPB.Net] object sync on: " + _props.Watched + " movable objects"
                + (atoms
                    ? "; adding and deleting objects is shared too, and a subscene the peer adds will be loaded from this machine's own library"
                    : "; adding and deleting objects is NOT shared - both sides need \"Load objects they add\" on under Rules"));

            if (!props)
                LogUtil.LogWarning("[VPB.Net] that peer does not sync object positions, so a prop you move stays put on their side");
            if (_syncAtoms && !atoms)
                LogUtil.LogWarning("[VPB.Net] that peer does not sync objects being added or deleted");
        }

        void SetParamSync(bool on)
        {
            bool want = on && (_peerId < 0 || _scenesMatched);
            if (want == _paramsOn) return;
            _paramsOn = want;

            if (!want)
            {
                _params.Unbind();
                if (!on && _peerId >= 0 && _havePeerCaps)
                    LogUtil.LogWarning("[VPB.Net] that peer does not sync object settings, so a light you dim"
                        + " stays bright on their side");
                return;
            }

            _params.Bind(_localAtom, _remoteAtom);
            _params.RefreshNow();
            LogUtil.LogWarning("[VPB.Net] object settings sync on: watching " + _params.Watched
                + " objects");
        }

        void PollParams()
        {
            if (_peerId < 0 || !_rigOk) return;
            if (!_paramsOn)
                SetParamSync((_sharedCaps & VpbNetCapability.Params) != 0);
            if (!_paramsOn) return;
            if ((_sharedCaps & VpbNetCapability.Params) == 0) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextParamPoll) return;
            _nextParamPoll = now + ParamPollSeconds;

            int n = _params.Collect(_paramTx, VpbNetEventLimits.MaxParamsPerEvent);
            while (n > 0 && !FitsParams(_paramTx, n)) n--;
            if (n <= 0) return;

            _eventW.Begin(VpbNetEventType.AtomParam, _eventSeq);
            _eventW.WriteByte((byte)n);
            for (int i = 0; i < n; i++) WriteParam(_paramTx, i);

            int len = _eventW.End();
            if (len <= 0) return;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _params.Commit(_paramTx, n);
            _eventSeq++;
            _paramsSent += n;
        }

        bool FitsParams(VpbNetAtomParamBatch batch, int count)
        {
            int used = 1;
            for (int i = 0; i < count; i++)
            {
                int need = VpbNetEventPack.ItemBytes(batch.Uid[i], 0)
                    + VpbNetEventPack.ItemBytes(batch.Storable[i], 0)
                    + VpbNetEventPack.ItemBytes(batch.Name[i], 0)
                    + 1
                    + ParamValueBytes(batch.Kind[i], batch.Text[i]);
                if (used + need > VpbNetEventLimits.MaxPayload) return false;
                used += need;
            }
            return true;
        }

        static int ParamValueBytes(byte kind, string text)
        {
            if (kind == VpbNetAtomParamKind.Float) return 4;
            if (kind == VpbNetAtomParamKind.Bool) return 1;
            if (kind == VpbNetAtomParamKind.Color) return 12;
            return VpbNetEventPack.ItemBytes(text, 0);
        }

        void WriteParam(VpbNetAtomParamBatch batch, int i)
        {
            _eventW.WriteString(batch.Uid[i], VpbNetEventLimits.MaxIdentifier);
            _eventW.WriteString(batch.Storable[i], VpbNetStorableLimits.MaxStorableChars);
            _eventW.WriteString(batch.Name[i], VpbNetStorableLimits.MaxParamChars);
            byte kind = batch.Kind[i];
            _eventW.WriteByte(kind);
            if (kind == VpbNetAtomParamKind.Float) _eventW.WriteF32(batch.Number[i]);
            else if (kind == VpbNetAtomParamKind.Bool) _eventW.WriteByte((byte)(batch.Switch[i] ? 1 : 0));
            else if (kind == VpbNetAtomParamKind.Color)
            {
                _eventW.WriteF32(batch.H[i]);
                _eventW.WriteF32(batch.S[i]);
                _eventW.WriteF32(batch.V[i]);
            }
            else _eventW.WriteString(batch.Text[i] ?? string.Empty, VpbNetEventLimits.MaxEntryPath);
        }

        void PollProps()
        {
            if (_peerId < 0 || !_rigOk) return;

            // Re-arm props if atoms rebound mid-session — join will not come again.
            if (!_propsOn)
                SetPropSync((_sharedCaps & VpbNetCapability.Props) != 0,
                    (_sharedCaps & VpbNetCapability.Atoms) != 0);
            if (!_propsOn) return;

            // Drain held refs before collect.
            DrainHeldSubSceneRefs();

            float now = Time.realtimeSinceStartup;
            if (now < _nextPropPoll) return;
            _nextPropPoll = now + PropPollSeconds;

            SendAtomLifecycle();
            SendSubSceneRefs();
            _props.RetryPendingSubScenes();

            if ((_sharedCaps & VpbNetCapability.Props) == 0) return;
            if (_props.Collect(_propTx) <= 0) return;

            int n = _propTx.Write(_propBuf, _propSeq);
            if (n <= 0) return;

            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Props, _propBuf, n, false);
            _props.Commit(_propTx);
            _propSeq++;
            _propFramesSent++;
        }

        void SendAtomLifecycle()
        {
            if ((_sharedCaps & VpbNetCapability.Atoms) == 0) return;

            int adds = _props.TakeAdds(_atomUids, _atomTypes, _atomRefs, _atomPos, _atomRot, _atomAddCap);
            if (adds > 0)
            {
                _eventW.Begin(VpbNetEventType.AtomAdd, _eventSeq);
                _eventW.WriteByte((byte)adds);
                for (int i = 0; i < adds; i++)
                {
                    _eventW.WriteString(_atomUids[i], VpbNetEventLimits.MaxIdentifier);
                    _eventW.WriteString(_atomTypes[i], VpbNetEventLimits.MaxIdentifier);
                    _eventW.WriteString(_atomRefs[i] ?? string.Empty, VpbNetEventLimits.MaxIdentifier);

                    Vector3 p = _atomPos[i];
                    Quaternion q = _atomRot[i];
                    _eventW.WriteF32(p.x);
                    _eventW.WriteF32(p.y);
                    _eventW.WriteF32(p.z);
                    _eventW.WriteU32(VpbPose.PackQuat(q.x, q.y, q.z, q.w));
                }
                int len = _eventW.End();
                if (len > 0)
                {
                    VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
                    _props.CommitAdds(adds);
                    _eventSeq++;
                    _atomAddCap = VpbNetEventLimits.MaxAtomsPerEvent;
                }
                else if (adds > 1)
                {
                    _atomAddCap = 1;
                }
                else
                {
                    _props.CommitAdds(adds);
                    LogUtil.LogWarning("[VPB.Net] " + _atomUids[0]
                        + " has names too long to fit one datagram; the other side does not get it");
                }
            }

            int removes = _props.TakeRemoves(_atomUids, VpbNetEventLimits.MaxAtomsPerEvent);
            if (removes <= 0) return;

            _eventW.Begin(VpbNetEventType.AtomRemove, _eventSeq);
            _eventW.WriteByte((byte)removes);
            for (int i = 0; i < removes; i++)
                _eventW.WriteString(_atomUids[i], VpbNetEventLimits.MaxIdentifier);

            int rlen = _eventW.End();
            if (rlen <= 0) return;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, rlen, true);
            _props.CommitRemoves(removes);
            _eventSeq++;
        }

        void SendSubSceneRefs()
        {
            if ((_sharedCaps & VpbNetCapability.Atoms) == 0)
            {
                // Still collect when sharing is off — that's how we explain the skipped change.
                _props.CollectSubSceneChanges(_atomUids, _atomRefs, 0);
                return;
            }

            int n = _props.CollectSubSceneChanges(_atomUids, _atomRefs,
                VpbNetEventLimits.MaxSubScenesPerEvent);
            if (n <= 0) return;

            _eventW.Begin(VpbNetEventType.SubSceneRef, _eventSeq);
            _eventW.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                _eventW.WriteString(_atomUids[i], VpbNetEventLimits.MaxIdentifier);
                _eventW.WriteString(_atomRefs[i], VpbNetEventLimits.MaxIdentifier);
            }

            int len = _eventW.End();
            if (len <= 0)
            {
                // One long pair does not fit a datagram. Saying so beats retrying it forever.
                _props.CommitSubSceneChanges(n);
                LogUtil.LogWarning("[VPB.Net] " + _atomUids[0] + " -> " + _atomRefs[0]
                    + " has names too long to fit one datagram; the other side does not get it");
                return;
            }

            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _props.CommitSubSceneChanges(n);
            _eventSeq++;
            _subScenesSent += n;
        }

        void HandleSubSceneEvent()
        {
            int n = _eventR.ReadCount(VpbNetEventLimits.MaxSubScenesPerEvent);
            for (int i = 0; i < n; i++)
            {
                string uid = _eventR.ReadIdentifier();
                string reference = _eventR.ReadIdentifier();
                if (_eventR.Failed || uid == null || reference == null) return;

                if (_props.AbsorbSameSubSceneRef(uid, reference))
                {
                    LogUtil.LogWarning("[VPB.Net] the peer has " + reference + " in " + uid
                        + "; this side already has that same subscene, so there is nothing to"
                        + " load and nothing to ask you about");
                    continue;
                }

                // Subscene is a scene file (can carry plugins) — Scene rule, not Objects.
                if (!RulePasses(VpbNetRuleDomain.Scene, VpbNetRuleAxis.Control, reference)) return;
                if (HoldIfContentMissing(reference)) return;
                if ((_sharedCaps & VpbNetCapability.Atoms) == 0)
                {
                    LogUtil.LogWarning("[VPB.Net] the peer loaded " + reference + " into " + uid
                        + " but subscene sharing is not on for this session, so nothing happened here."
                        + " Both machines need \"Load objects they add\" on under Rules.");
                    continue;
                }
                if (!_propsOn)
                {
                    HoldSubSceneRef(uid, reference);
                    continue;
                }
                _subScenesReceived++;
                _props.ApplySubSceneRef(uid, reference);
            }
        }

        // Hold refs that beat our bind by a frame.
        void HoldSubSceneRef(string uid, string reference)
        {
            for (int i = 0; i < _heldSubUid.Count; i++)
            {
                if (!string.Equals(_heldSubUid[i], uid, StringComparison.Ordinal)) continue;
                _heldSubRef[i] = reference;
                return;
            }
            if (_heldSubUid.Count >= MaxHeldSubScenes) return;

            _heldSubUid.Add(uid);
            _heldSubRef.Add(reference);
            LogUtil.LogWarning("[VPB.Net] the peer loaded " + reference + " into " + uid
                + " before object sync was up on this side; holding it until it is");
        }

        void DrainHeldSubSceneRefs()
        {
            if (_heldSubUid.Count == 0) return;
            for (int i = 0; i < _heldSubUid.Count; i++)
            {
                _subScenesReceived++;
                _props.ApplySubSceneRef(_heldSubUid[i], _heldSubRef[i]);
            }
            _heldSubUid.Clear();
            _heldSubRef.Clear();
        }

        void OnPropFrame(byte[] buf, int offset, int len)
        {
            if (!_propsOn || (_sharedCaps & VpbNetCapability.Props) == 0) return;

            VpbNetPropReject r = _propRx.Read(buf, offset, len);
            if (r != VpbNetPropReject.None)
            {
                _propRefused++;
                if (_propRefused <= 8) LogUtil.LogWarning("[VPB.Net] " + VpbNetPropFrame.Explain(r));
                return;
            }

            if (_propWatermark != 0 && (int)(_propRx.Seq - _propWatermark) <= 0) return;
            _propWatermark = _propRx.Seq;

            _props.Apply(_propRx);
            _propFramesApplied++;
        }

        void HandleAtomAddEvent()
        {
            int n = _eventR.ReadCount(VpbNetEventLimits.MaxAtomsPerEvent);
            for (int i = 0; i < n; i++)
            {
                string uid = _eventR.ReadIdentifier();
                string type = _eventR.ReadIdentifier();
                string reference = _eventR.ReadText(VpbNetEventLimits.MaxIdentifier);

                Vector3 pos;
                pos.x = _eventR.ReadF32();
                pos.y = _eventR.ReadF32();
                pos.z = _eventR.ReadF32();

                float qx, qy, qz, qw;
                VpbPose.UnpackQuat(_eventR.ReadU32(), out qx, out qy, out qz, out qw);

                if (_eventR.Failed || uid == null || type == null) return;
                if (!RulePasses(VpbNetRuleDomain.Objects, VpbNetRuleAxis.Control, uid)) return;
                if (!_propsOn || (_sharedCaps & VpbNetCapability.Atoms) == 0) continue;
                _props.ApplyAdd(uid, type, reference, pos, new Quaternion(qx, qy, qz, qw));
            }
        }

        void HandleAtomRemoveEvent()
        {
            int n = _eventR.ReadCount(VpbNetEventLimits.MaxAtomsPerEvent);
            for (int i = 0; i < n; i++)
            {
                string uid = _eventR.ReadIdentifier();
                if (_eventR.Failed || uid == null) return;
                if (!RulePasses(VpbNetRuleDomain.Objects, VpbNetRuleAxis.Control, uid)) return;
                if (!_propsOn || (_sharedCaps & VpbNetCapability.Atoms) == 0) continue;
                _props.ApplyRemove(uid);
            }
        }

        void HandleTriggerEvent()
        {
            uint seq = _eventR.Seq;
            bool stale = seq != 0 && seq <= _triggerWatermark;
            if (!stale) _triggerWatermark = seq;

            int n = _eventR.ReadCount(VpbNetEventLimits.MaxTriggersPerEvent);
            for (int i = 0; i < n; i++)
            {
                string atomUid = _eventR.ReadIdentifier();
                string storableId = _eventR.ReadIdentifier();
                bool on = _eventR.ReadByte() != 0;
                if (_eventR.Failed || atomUid == null || storableId == null) return;
                if (stale || !_triggersOn) continue;
                VpbNetTriggerRelay.Apply(atomUid, storableId, on);
            }
        }

        void HandleAtomParamEvent()
        {
            uint seq = _eventR.Seq;
            bool stale = seq != 0 && seq <= _paramWatermark;
            if (!stale) _paramWatermark = seq;

            int n = _eventR.ReadCount(VpbNetEventLimits.MaxParamsPerEvent);
            for (int i = 0; i < n; i++)
            {
                string uid = _eventR.ReadIdentifier();
                string storableId = _eventR.ReadIdentifier(VpbNetStorableLimits.MaxStorableChars);
                string name = _eventR.ReadIdentifier(VpbNetStorableLimits.MaxParamChars);
                byte kind = _eventR.ReadByte();
                float number = 0f;
                bool flag = false;
                float h = 0f;
                float s = 0f;
                float v = 0f;
                string text = null;

                if (kind == VpbNetAtomParamKind.Float) number = _eventR.ReadF32();
                else if (kind == VpbNetAtomParamKind.Bool) flag = _eventR.ReadByte() != 0;
                else if (kind == VpbNetAtomParamKind.Color)
                {
                    h = _eventR.ReadF32();
                    s = _eventR.ReadF32();
                    v = _eventR.ReadF32();
                }
                else if (kind == VpbNetAtomParamKind.Chooser || kind == VpbNetAtomParamKind.Text)
                    text = _eventR.ReadText(VpbNetEventLimits.MaxEntryPath);
                else
                    return;

                if (_eventR.Failed || uid == null || storableId == null || name == null) return;
                if (stale || !_paramsOn) continue;
                _params.Apply(uid, storableId, name, kind, number, flag, h, s, v, text);
            }
        }

        static bool Newer(uint seq, uint watermark)
        {
            return seq != 0 && (int)(seq - watermark) > 0;
        }

        void HandleClothingEvent()
        {
            uint seq = _eventR.Seq;
            byte target = _eventR.ReadByte();
            if (_eventR.Failed || !VpbNetEventTarget.IsKnown(target)) return;

            bool onMe = target == VpbNetEventTarget.Peer;
            if (!RulePasses(VpbNetRuleDomain.Clothing,
                onMe ? VpbNetRuleAxis.Control : VpbNetRuleAxis.Mirror, null)) return;

            bool stale = !Newer(seq, _clothingWatermark);
            if (!stale) _clothingWatermark = seq;

            VpbNetPeerLook look = onMe ? _localLook : _remoteLook;

            int n = _eventR.ReadCount(VpbNetEventLimits.MaxClothingItems);
            for (int i = 0; i < n; i++)
            {
                string id = _eventR.ReadIdentifier();
                bool on = _eventR.ReadByte() != 0;
                if (_eventR.Failed || id == null) return;
                if (stale)
                {
                    _clothingDroppedStale++;
                    if (_clothingDroppedStale <= 8)
                        LogUtil.LogWarning("[VPB.Net] dropped a clothing change for " + id
                            + " that arrived out of order (seq " + seq + " behind "
                            + _clothingWatermark + ")");
                    continue;
                }
                if (!onMe) _peerState.SetClothing(id, on, seq);
                _clothingApplied++;

                // Don't hold the whole clothing batch for one missing package — fetch in bg, resync later.
                if (on) WantContentFor(id);

                if (!look.IsAlive()) continue;
                look.ApplyClothing(id, on);

                // Fold inbound clothing into baseline or next poll sends the peer's change back to them.
                if (onMe) look.RebaseClothing(id, on);
            }
        }

        bool SendPresetApply(string action, string reference, bool toPeer)
        {
            if (_peerId < 0 || !_gotJoin || !_rigOk) return false;
            if ((_sharedCaps & VpbNetCapability.Events) == 0) return false;

            _eventW.Begin(VpbNetEventType.PresetApply, _eventSeq);
            _eventW.WriteByte(toPeer ? VpbNetEventTarget.Peer : VpbNetEventTarget.Self);
            _eventW.WriteString(action, VpbNetEventLimits.MaxPresetAction);
            _eventW.WriteString(reference, VpbNetEventLimits.MaxEntryPath);

            int len = _eventW.End();
            if (len <= 0)
            {
                LogUtil.LogWarning("[VPB.Net] " + reference
                    + " has a name too long to fit one datagram; the other side does not get it");
                return false;
            }

            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _eventSeq++;
            return true;
        }

        bool SendDualPose(string reference, int senderRole, int receiverRole, VpbDualPose.Anchor anchor)
        {
            if (_peerId < 0 || !_gotJoin || !_rigOk) return false;
            if ((_sharedCaps & VpbNetCapability.Events) == 0) return false;
            if (senderRole == receiverRole) return false;

            _eventW.Begin(VpbNetEventType.DualPose, _eventSeq);
            _eventW.WriteString(reference, VpbNetEventLimits.MaxEntryPath);
            _eventW.WriteByte((byte)senderRole);
            _eventW.WriteByte((byte)receiverRole);
            _eventW.WriteByte(anchor.Active ? (byte)1 : (byte)0);
            _eventW.WriteF32(anchor.Pivot.x);
            _eventW.WriteF32(anchor.Pivot.y);
            _eventW.WriteF32(anchor.Pivot.z);
            _eventW.WriteF32(anchor.Position.x);
            _eventW.WriteF32(anchor.Position.y);
            _eventW.WriteF32(anchor.Position.z);
            _eventW.WriteF32(anchor.Yaw);

            int len = _eventW.End();
            if (len <= 0)
            {
                LogUtil.LogWarning("[VPB.Net] " + reference
                    + " has a name too long to fit one datagram; the other side does not get their half");
                return false;
            }

            // Burst copies of the same seq — receiver watermark drops dupes.
            Buffer.BlockCopy(_eventW.Buffer, 0, _dualPoseTx, 0, len);
            _dualPoseTxLen = len;
            _dualPoseTxLeft = DualPoseResends;
            _dualPoseTxNextMs = _nowMs + DualPoseResendMs;

            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _eventSeq++;
            return true;
        }

        // Burst now — next Pump waits until the load finishes.
        void SendBusy(bool begin, int seconds, byte kind)
        {
            if (_peerId < 0 || !_gotJoin) return;
            if ((_sharedCaps & VpbNetCapability.Events) == 0) return;

            _eventW.Begin(VpbNetEventType.Busy, _eventSeq);
            _eventW.WriteByte(begin ? (byte)1 : (byte)0);
            _eventW.WriteU16(seconds);
            _eventW.WriteByte(kind);

            int len = _eventW.End();
            if (len <= 0) return;

            for (int i = 0; i < BusyBursts; i++)
                VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _eventW.Buffer, len, true);
            _eventSeq++;
            _busySent++;
        }

        void HandleBusyEvent()
        {
            uint seq = _eventR.Seq;
            bool begin = _eventR.ReadByte() != 0;
            int seconds = _eventR.ReadU16();
            byte kind = _eventR.ReadByte();
            if (_eventR.Failed) return;

            if (!Newer(seq, _busyWatermark)) return;
            _busyWatermark = seq;

            if (begin)
            {
                string reason = VpbNetBusyKind.Describe(kind);
                _session.NotePeerBusy(_nowMs, seconds * 1000.0, reason);
                _busyHeld++;
                LogUtil.LogWarning("[VPB.Net] the other person is " + reason
                    + " and expects to be gone about " + seconds
                    + "s - holding their avatar where it stands instead of dropping them");
                return;
            }

            if (_session.PeerBusy)
                LogUtil.LogWarning("[VPB.Net] the other person finished loading and is back");
            _session.NotePeerReady();
        }

        void MaybeResendDualPose()
        {
            if (_dualPoseTxLeft <= 0 || _dualPoseTxLen <= 0) return;
            if (_peerId < 0)
            {
                _dualPoseTxLeft = 0;
                return;
            }
            if (_nowMs < _dualPoseTxNextMs) return;

            _dualPoseTxLeft--;
            _dualPoseTxNextMs = _nowMs + DualPoseResendMs;
            VpbNetBrokerLink.SendData(_peerId, VpbNetChannel.Event, _dualPoseTx, _dualPoseTxLen, true);
        }

        void HandleDualPoseEvent()
        {
            uint seq = _eventR.Seq;
            string reference = _eventR.ReadIdentifier(VpbNetEventLimits.MaxEntryPath);
            int senderRole = _eventR.ReadByte();
            int myRole = _eventR.ReadByte();
            bool anchored = _eventR.ReadByte() != 0;

            float pivotX = _eventR.ReadF32();
            float pivotY = _eventR.ReadF32();
            float pivotZ = _eventR.ReadF32();
            float posX = _eventR.ReadF32();
            float posY = _eventR.ReadF32();
            float posZ = _eventR.ReadF32();
            float yaw = _eventR.ReadF32();

            if (_eventR.Failed || reference == null) return;
            if (myRole > 1 || senderRole > 1 || myRole == senderRole) return;

            // Dual-pose always writes the body this side rides — Control, never Mirror.
            if (!RulePasses(VpbNetRuleDomain.DualPose, VpbNetRuleAxis.Control, reference)) return;

            // One-shot instruction; sender repeats to survive a drop — not-newer is a duplicate.
            if (!Newer(seq, _dualPoseWatermark)) return;
            _dualPoseWatermark = seq;

            VpbDualPose.Anchor anchor = VpbDualPose.Anchor.None;
            anchor.Active = anchored;
            anchor.Pivot = new Vector3(pivotX, pivotY, pivotZ);
            anchor.Position = new Vector3(posX, posY, posZ);
            anchor.Yaw = yaw;

            VpbNetDualPoseRelay.Apply(reference, myRole, anchor);
        }

        public void MarkLocalPoseJump()
        {
            try { _sampler.MarkKeyframe(); }
            catch { }
            try
            {
                if (_localAtom != null)
                {
                    VpbNetCollisionGuard.Suspend(_localAtom, VpbNetCollisionGuard.JumpFrames,
                        "a two-person pose landed on the avatar you ride");
                }
            }
            catch { }
        }

        void HandlePresetApplyEvent()
        {
            uint seq = _eventR.Seq;
            byte target = _eventR.ReadByte();
            string action = _eventR.ReadText(VpbNetEventLimits.MaxPresetAction);
            string reference = _eventR.ReadIdentifier(VpbNetEventLimits.MaxEntryPath);
            if (_eventR.Failed || action == null || reference == null) return;
            if (!VpbNetEventTarget.IsKnown(target)) return;

            // Unknown action has no rule — refuse, don't fall back to the loosest permission.
            byte domain;
            if (!VpbNetRuleDomain.FromPresetAction(action, out domain))
            {
                VpbNetRulebook.NoteRefused();
                LogUtil.LogWarning("[VPB.Net] refused a peer look: unknown action " + action);
                return;
            }

            bool onMe = target == VpbNetEventTarget.Peer;
            if (!RulePasses(domain, onMe ? VpbNetRuleAxis.Control : VpbNetRuleAxis.Mirror,
                reference)) return;

            // Hold for download before watermark — else replay is judged stale by this pass.
            if (HoldIfContentMissing(reference)) return;

            // One-shot load; a duplicate is a second full file load and a freeze for no change.
            if (!Newer(seq, _presetWatermark)) return;
            _presetWatermark = seq;

            // Target is local/peer atom chosen here — no uid off the wire picks a body.
            Atom onto = onMe ? _localAtom : _remoteAtom;
            if (onto == null)
            {
                LogUtil.LogWarning("[VPB.Net] the peer applied " + reference + " but "
                    + (onMe ? "you are" : "they are")
                    + " not riding an avatar here, so there is nothing to put it on");
                return;
            }
            VpbNetPresetRelay.Apply(action, reference, onto);
            if (onMe) RebaseLocalLook();
        }

        // Reseat outbound look baselines after a whole-look apply.
        void RebaseLocalLook()
        {
            if (!_localLook.IsAlive()) return;

            _localLook.CollectClothingChanges(_changeIds, _changeOn);
            _localLook.CommitClothing(_changeIds.Count);
            _localLook.CollectMorphChanges(_changeIds, _changeValue);
            _localLook.CommitMorphs(_changeIds.Count);

            int worn;
            _hairSigTold = _localLook.ActiveHairSignature(out worn);
            _haveHairSig = true;
            _clothingSigTold = _localLook.ActiveClothingSignature(out worn);
            _clothingSigSeen = _clothingSigTold;
            _haveClothingSig = true;
        }

        void HandleHairEvent()
        {
            uint seq = _eventR.Seq;
            byte target = _eventR.ReadByte();
            if (_eventR.Failed || !VpbNetEventTarget.IsKnown(target)) return;

            bool onMe = target == VpbNetEventTarget.Peer;
            if (!RulePasses(VpbNetRuleDomain.Hair,
                onMe ? VpbNetRuleAxis.Control : VpbNetRuleAxis.Mirror, null)) return;

            bool stale = !Newer(seq, _hairWatermark);
            if (!stale) _hairWatermark = seq;

            _peerHair.Clear();
            int n = _eventR.ReadCount(VpbNetEventLimits.MaxHairItems);
            for (int i = 0; i < n; i++)
            {
                string id = _eventR.ReadIdentifier();
                if (_eventR.Failed || id == null) return;
                if (stale) continue;
                _peerHair.Add(id);
                WantContentFor(id);
            }
            if (stale) return;

            if (onMe)
            {
                if (!_localLook.IsAlive()) return;
                _hairApplied += _localLook.ApplyHairSet(_peerHair);
                int worn;
                _hairSigTold = _localLook.ActiveHairSignature(out worn);
                _haveHairSig = true;
                return;
            }

            _peerState.NoteEventSeq(seq);
            if (!_remoteLook.IsAlive()) return;
            _hairApplied += _remoteLook.ApplyHairSet(_peerHair);
        }

        void HandleMorphEvent()
        {
            uint seq = _eventR.Seq;
            byte target = _eventR.ReadByte();
            if (_eventR.Failed || !VpbNetEventTarget.IsKnown(target)) return;

            bool onMe = target == VpbNetEventTarget.Peer;
            if (!RulePasses(VpbNetRuleDomain.Morphs,
                onMe ? VpbNetRuleAxis.Control : VpbNetRuleAxis.Mirror, null)) return;

            bool stale = !Newer(seq, _morphWatermark);
            if (!stale) _morphWatermark = seq;

            VpbNetPeerLook look = onMe ? _localLook : _remoteLook;

            int n = _eventR.ReadCount(VpbNetEventLimits.MaxMorphs);
            for (int i = 0; i < n; i++)
            {
                string id = _eventR.ReadIdentifier();
                float v = VpbNetEventCodec.DequantizeMorph(_eventR.ReadI16());
                if (_eventR.Failed || id == null) return;
                if (stale)
                {
                    _morphDroppedStale++;
                    continue;
                }
                if (!onMe) _peerState.SetMorph(id, v, seq);
                if (!look.IsAlive()) continue;
                look.ApplyMorph(id, v);
                if (onMe) look.RebaseMorph(id, v);
            }
        }

        // Zero soak counters when session actually Running — handshake would look like a stall.
        void StepSessionStats()
        {
            if (_session.State != VpbNetSessionState.Running) return;

            if (!_soakStarted)
            {
                _soakStarted = true;
                _soakStart = Time.fixedTime;
                _acquireResets = _applier.HardResets;
                _acquireResetsAfterGap = _applier.HardResetsAfterGap;
                _interp = 0;
                _extrap = 0;
                _frozen = 0;
                _empty = 0;
                _perf.Reset();
                _rttHist.Reset();
                _delayHist.Reset();
                _transitHist.Reset();
                _bufferHist.Reset();
                _serviceHist.Reset();
                _nextLinkSampleMs = 0.0;
                LogUtil.LogWarning("[VPB.Net] session running");
            }

            if (_peerId > 0 && _nowMs >= _nextLinkSampleMs)
            {
                _nextLinkSampleMs = _nowMs + LinkSampleMs;

                double rtt = VpbNetBrokerLink.PeerRttMs(_peerId);
                if (rtt > 0.0) _rttHist.Add(rtt);
                if (_peerServiceMs > 0.0) _serviceHist.Add(_peerServiceMs);

                _delayHist.Add(_timeline.DelayMs);
                _transitHist.Add(_timeline.TransitMs);
                _bufferHist.Add(_timeline.BufferMs);
            }
        }

        void EmitSessionReport(bool aborted)
        {
            if (_soakReported) return;
            _soakReported = true;
            float elapsed = Time.fixedTime - _soakStart;
            if (elapsed < 0.001f) elapsed = 0.001f;
            int total = _interp + _extrap + _frozen + _empty;
            if (total < 1) total = 1;
            int resetsTotal = _applier.HardResets - _acquireResets;
            int gapResets = _applier.HardResetsAfterGap - _acquireResetsAfterGap;
            int steadyResets = resetsTotal - gapResets;
            double interpPercent = _interp * 100.0 / total;
            _perf.EmitContext("session");

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] ===== session report ===== {0:0.0} min", elapsed / 60f));
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] session: {0}  stalls {1} resumes {2} frozen {3}/{4} resets {5} ({6} steady, {7} post-gap) decodeFail {8}",
                _session.DescribeState(), _session.Stalls, _session.Resumes, _frozen, total,
                resetsTotal, steadyResets, gapResets, _applier.DecodeFailures));
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] freezes: this machine stopped running {0} time(s) for {1:0.0}s total,"
                + " worst {2:0.0}s - none of that counted against the peer;"
                + " you warned them {3} time(s) ({4} unclosed), they warned you {5}",
                _session.LocalStalls, _session.LocalStallTotalMs / 1000.0,
                _session.LongestLocalStallMs / 1000.0,
                _busySent, VpbNetBusy.ForcedEnds, _busyHeld));
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] cost: sampler p99 {0:0.0}us applier p99 {1:0.0}us",
                _perf.SamplerUs.Percentile(0.99), _perf.ApplierUs.Percentile(0.99)));
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] frames: interp {0} ({1:0.0}%) extrap {2} frozen {3} empty {4}",
                _interp, interpPercent, _extrap, _frozen, _empty));
            if (_delayHist.Count > 0)
            {
                LogUtil.LogWarning(string.Format(
                    "[VPB.Net] delay mean {0:0}ms worst {1:0}ms over {2} sample(s);"
                    + " it was chasing transit {3:0} + buffer {4:0} = {5:0}ms",
                    _delayHist.Mean, _delayHist.Max, _delayHist.Count,
                    _transitHist.Mean, _bufferHist.Mean, _transitHist.Mean + _bufferHist.Mean));
            }
            else
            {
                LogUtil.LogWarning("[VPB.Net] delay never measured - no peer stayed attached for a full second");
            }

            string peerService = _serviceHist.Count > 0
                ? string.Format("mean {0:0.0}ms worst {1:0.0}ms", _serviceHist.Mean, _serviceHist.Max)
                : "never reported one";
            string rtt = _rttHist.Count > 0
                ? string.Format("mean {0:0.000}ms worst {1:0.000}ms over {2} sample(s)",
                    _rttHist.Mean, _rttHist.Max, _rttHist.Count)
                : "never measured - the peer never reported one";
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] service yours mean {0:0.0}ms; peer {1}; wire rtt {2}",
                VpbNetBrokerLink.MeanDrainLagMs, peerService, rtt));
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] applier guards: {0} stale frames dropped before a keyframe, {1} frames held on silence",
                _applier.PreKeyframeDropped, _applier.HeldStale));
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] resync: keyframes sent {0} applied {1} ignored as stale {2};"
                + " {3} of those sent on the {4}s timer; peer state {5} clothing {6} morphs",
                _kfSent, _kfApplied, _kfStale, _periodicResyncs, (int)(PeriodicResyncMs / 1000.0),
                _peerState.ClothingCount, _peerState.MorphCount));

            if (_propsOn)
                LogUtil.LogWarning(string.Format(
                    "[VPB.Net] objects: watching {0} ({1} not watched: {2}); sent {3} frames applied {4} refused {5}",
                    _props.Watched, _props.SkippedPerson + _props.SkippedInSubScene + _props.SkippedPlayerLocal,
                    _props.DescribeExclusions(), _propFramesSent, _propFramesApplied, _propRefused));

            if (_paramsOn)
                LogUtil.LogWarning(string.Format(
                    "[VPB.Net] object settings: watching {0} objects, {1} values; sent {2} applied {3}"
                    + " refused {4} missing {5}",
                    _params.Watched, _params.Tracked, _paramsSent, _params.Applied,
                    _params.Refused, _params.Missing));
            else
                LogUtil.LogWarning("[VPB.Net] object settings: OFF - a light you dim stayed bright"
                    + " on the other side, because that peer does not speak object settings.");

            LogUtil.LogWarning(_props.LifecycleOn
                ? string.Format(
                    "[VPB.Net] lifecycle: on; created {0} removed {1} rejected {2} dropped {3}"
                    + " pruned {4} (queued, then turned out to be subscene content)",
                    _props.Created, _props.Removed, _props.Refused, _props.Dropped, _props.Pruned)
                : string.Format(
                    "[VPB.Net] lifecycle: OFF - {0} adds, {1} deletes and {2} subscene loads were"
                    + " NOT shared. Both sides need \"Load objects they add\" on under Rules.",
                    _props.SuppressedAdds, _props.SuppressedRemoves, _props.SubSceneSuppressed));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] subscenes: watching {0}; sent {1} loads, received {2}, applied {3},"
                + " already matched {4}, refused {5}, waiting for an atom {6}",
                _props.SubScenesWatched, _subScenesSent, _subScenesReceived, _props.SubSceneApplied,
                _props.SubSceneSkipped, _props.SubSceneRefused, _props.SubScenePendingApplies));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] outfit: {0} full resend(s) after a look apply the difference could not"
                + " explain; peer holds {1} item(s); took {2} clothing change(s) from them,"
                + " dropped {3} as out of order; morphs dropped {4}",
                _clothingRepairs, _peerState.ClothingCount, _clothingApplied,
                _clothingDroppedStale, _morphDroppedStale));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] looks: told them about {0} preset(s) you applied, loaded {1} of"
                + " theirs, refused or missing {2}",
                VpbNetPresetRelay.Sent, VpbNetPresetRelay.Applied, VpbNetPresetRelay.Refused));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] two-person poses: {0} split with them, {1} of theirs taken onto"
                + " your avatar, {2} refused or missing",
                VpbNetDualPoseRelay.Sent, VpbNetDualPoseRelay.Applied, VpbNetDualPoseRelay.Refused));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] hair: sent {0} update(s), put {1} item(s) on the peer's avatar,"
                + " {2} still waiting for a package",
                _hairSent, _hairApplied, _remoteLook.HairRetryPending));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] collisions: {0} pause(s) around jumps ({1} local pose jumps, worst {2:0.00}m,"
                + " {3} guarded applier frames), {4} restored, manual switch {5}",
                VpbNetCollisionGuard.Suspends, _sampler.Jumps, _sampler.LastJumpMeters,
                _applier.CollisionHolds, VpbNetCollisionGuard.Restores,
                VpbNetCollisionGuard.ForcedOff ? "OFF (collisions held off)" : "on"));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] bodies: {0} frame(s) where a control was moved slower than the peer asked"
                + " (worst ask {1:0.00}m in one frame), {2} blow-up(s) caught and repaired, repair {3}",
                _applier.GovernedFrames, _applier.GovernedWorstMeters,
                VpbNetBodyGuard.Repairs, VpbNetBodyGuard.Enabled ? "on" : "OFF"));

            if (_triggersOn)
                LogUtil.LogWarning(string.Format(
                    "[VPB.Net] triggers: observed {0} sent {1} applied {2} refused {3} dropped {4}",
                    VpbNetTriggerRelay.Observed, _triggersSent, VpbNetTriggerRelay.Applied,
                    VpbNetTriggerRelay.Refused, VpbNetTriggerRelay.Dropped));

            if ((_sharedCaps & VpbNetCapability.FidelityTier) != 0)
            {
                StringBuilder tier = new StringBuilder(48);
                VpbNetCapability.Describe(tier, _sharedCaps & VpbNetCapability.FidelityTier);
                LogUtil.LogWarning(string.Format(
                    "[VPB.Net] fidelity {0}: sent {1} finger, {2} gaze, {3} jaw blocks; applied {4} frames, {5} refused",
                    tier.ToString(), _sampler.FingerBlocksSent, _sampler.GazeBlocksSent, _sampler.JawBlocksSent,
                    _applier.FidelityFrames, _applier.FidelityRefused));
            }

            if (aborted)
                LogUtil.LogWarning("[VPB.Net] the session ended while still connecting or reconnecting,"
                    + " so the numbers above are partial");
            LogUtil.LogWarning("[VPB.Net] ===== end session report =====");
        }

        void FeedOverlay()
        {
            VpbNetDiagnostics d = VpbNetOverlay.Stats;
            VpbNetSnapshotBuffer buf = _applier.Buffer;
            d.State = _session.State;
            d.Reason = _session.Reason;
            d.TransportMode = "lan";
            d.PeerName = PeerName;
            d.PeerCount = _peerId >= 0 ? 1 : 0;
            int pid = _peerId >= 0 ? _peerId : VpbNetBrokerLink.FirstPeer();
            d.RttMs = pid >= 0 ? VpbNetBrokerLink.PeerRttMs(pid) : _netClock.RttMs;
            d.JitterMs = pid >= 0 ? VpbNetBrokerLink.PeerJitterMs(pid) : _netClock.JitterMs;
            d.OffsetMs = _netClock.OffsetMs;
            d.TransitMs = _timeline.TransitMs;
            d.BufferMs = _timeline.BufferMs;
            d.DelayMs = _timeline.DelayMs;
            d.BufferDepth = buf.Count;
            uint sent = pid >= 0 ? VpbNetBrokerLink.PeerRemoteSentCount(pid) : 0u;
            uint lost = pid >= 0 ? VpbNetBrokerLink.PeerLostCount(pid) : 0u;
            d.LossPercent = sent > 0 ? lost * 100.0 / sent : 0.0;
            d.FrameAgeMs = buf.Count > 0 && _timeline.Ready ? buf.NewestMs - _lastRenderMs : 0.0;
            d.ServiceLocalMs = VpbNetBrokerLink.MeanDrainLagMs;
            d.ServicePeerMs = _peerServiceMs;
            d.SamplerUs = _sampler.LastEncodeUs;
            d.ApplierUs = _applier.LastApplyUs;
            d.Stalls = _session.Stalls;
            d.Reconnects = _session.ReconnectAttempts;
            d.Interpolated = _interp;
            d.Extrapolated = _extrap;
            d.Frozen = _frozen;
        }

        void Publish()
        {
            VpbNetPresence.State = _session.State;
            VpbNetPresence.Reason = _session.Reason;
            VpbNetPresence.Status = _session.State == VpbNetSessionState.Idle
                ? "waiting for broker"
                : _session.DescribeState();
            VpbNetPresence.ReasonText = _session.DescribeReason();
            VpbNetPresence.PeerName = PeerName;
            VpbNetPresence.LocalName = _boundLocalUid;
            VpbNetPresence.Room = _room;
            VpbNetPresence.Address = string.IsNullOrEmpty(VpbNetPresence.Invite) ? _address : VpbNetPresence.Invite;
            VpbNetPresence.AsHost = _asHost;
            VpbNetPresence.PeerBusy = _session.PeerBusy;
            VpbNetPresence.LocalBusy = VpbNetBusy.Active;
            VpbNetPresence.PeerUp = _peerId >= 0;
            VpbNetPresence.PeerId = _peerId;
            if (!string.IsNullOrEmpty(VpbNetBrokerLink.InviteBlob))
                VpbNetPresence.Invite = VpbNetBrokerLink.InviteBlob;

            bool up = _peerId >= 0;
            if (up)
            {
                if (!_bookedPeer)
                {
                    try { VpbNetRoomBookStore.RememberConnected(_asHost, _room, string.Empty); }
                    catch { }
                    _bookedPeer = true;
                }
            }
            else
            {
                _bookedPeer = false;
            }
        }
    }
}
