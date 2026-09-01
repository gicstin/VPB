using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    // Session window. Setup collapsed so host/join is first.
    public static class VpbNetSessionUi
    {
        const string HostName = "VPB_NetSession";
        const float RefreshSeconds = 0.25f;
        const float InviteProbeSeconds = 2f;
        const float PanelWidthRef = 560f;
        const float PersonRideRef = 140f;
        const float PersonDotRef = 10f;
        const float RideCellRef = 108f;
        const float NewChipRef = 96f;
        const float LockChipRef = 104f;
        const float SetupChipRef = 120f;
        const float MoreChipRef = 88f;
        const float RulesChipRef = 120f;
        const float HudCopyChipRef = 88f;
        const float HudToastSeconds = 3.5f;
        const float DefaultX = 24f;
        const float DefaultY = -80f;
        const int SortingOrder = 1001;
        const int MaxAvatarButtons = VpbNetAvatarRoster.MaxAvatars;

        static readonly StringBuilder _sb = new StringBuilder(256);
        static readonly int[] _snap = new int[34];
        static readonly int[] _prev = new int[34];
        static readonly List<VpbNetUiKit.Chip> _avatarChips = new List<VpbNetUiKit.Chip>(MaxAvatarButtons);
        static readonly List<string> _avatarUids = new List<string>(MaxAvatarButtons);
        static readonly List<int> _avatarStates = new List<int>(MaxAvatarButtons);

        const int SeatLocked = 0;
        const int SeatFree = 1;
        const int SeatWanted = 2;
        const int SeatMine = 3;
        const int SeatTheirs = 4;
        const int SeatPending = 5;

        static VpbNetUiKit.Shell _shell;
        static VpbNetUiKit.Chip _collapseChip;
        static VpbNetUiKit.Chip _closeChip;
        static VpbNetUiKit.Chip _hudCopyChip;
        static VpbNetUiKit.Chip _rulesChip;
        static GameObject _rulesPane;

        static Image _headDot;
        static Text _headCount;

        static Image _statusFill;
        static Image _statusDot;
        static Text _statusLine;
        static Text _hintLine;

        static GameObject _idlePane;
        static GameObject _livePane;
        static GameObject _gatesPane;
        static Text _gateScene;
        static Text _gateMine;
        static Text _gateTheirs;
        static Text _gateCap;

        static GameObject _setupBody;
        static VpbNetUiKit.Chip _setupChip;
        static Text _setupSummary;
        static Text _setupRulesHint;
        static VpbNetUiKit.Chip _presetWatchChip;
        static VpbNetUiKit.Chip _presetCustomChip;

        static VpbNetUiKit.Chip _transportDirect;
        static VpbNetUiKit.Chip _transportSteam;
        static Text _transportHint;
        static GameObject _steamGate;
        static Text _steamGateText;
        static VpbNetUiKit.Chip _steamAckChip;
        static VpbNetUiKit.Chip _steamRepairChip;
        static VpbNetUiKit.Chip _steamDirectChip;
        static GameObject _directGate;
        static Text _directGateText;

        static Image _youDot;
        static Text _youName;
        static Text _youRide;
        static Image _themDot;
        static Text _themName;
        static Text _themRide;

        static GameObject _peoplePane;
        static GameObject _ridePane;
        static GameObject _roomPane;
        static GameObject _worldPane;
        static GameObject _peerPane;
        static GameObject _liveFooter;
        static GameObject _leavePane;
        static GameObject _kickPane;
        static VpbNetUiKit.Chip _moreChip;
        static GameObject _avatarRow;
        static VpbNetUiKit.Chip _spectateChip;
        static Text _rideHint;

        static Text _liveRoomText;
        static Text _liveRoomHint;
        static VpbNetUiKit.Chip _copyChip;
        static VpbNetUiKit.Chip _liveNewChip;
        static VpbNetUiKit.Chip _liveLockChip;

        static VpbNetUiKit.Chip _collideChip;

        static VpbNetUiKit.Chip _kickChip;
        static VpbNetUiKit.Chip _resyncChip;
        static VpbNetUiKit.Chip _inviteChip;
        const float InviteErrorSeconds = 12f;
        static string _inviteError;
        static float _inviteErrorUntil;

        static float _nextInviteProbe;
        static bool _canInvite;

        static int _avatarRevision = -1;
        static float _nextPoll;
        static float _nextRefresh;
        static bool _visible;
        static bool _collapsed;
        static bool _havePrev;
        static bool _inSession;
        static bool _setupOpen;
        static bool _moreOpen;
        static bool _rulesOpen;
        static bool _leaveConfirm;
        static bool _kickConfirm;
        static bool _copiedLive;
        static bool _hudToastShown;
        static float _hudToastUntil;
        static float _scale = 1f;
        static bool _rescaleLock;

        public static bool IsOpen { get { return _shell != null; } }

        public static bool IsWanted
        {
            get
            {
                try
                {
                    Settings s = Settings.Instance;
                    return s != null && s.NetSessionUi != null && s.NetSessionUi.Value;
                }
                catch { return false; }
            }
        }

        public static void Toggle()
        {
            if (InSession() && _shell != null)
            {
                if (_collapsed) ExpandLive();
                else CollapseLive(true);
                return;
            }
            SetWanted(!IsWanted);
        }

        public static void SetWanted(bool want)
        {
            if (!want && InSession())
            {
                CollapseLive(true);
                return;
            }

            try
            {
                Settings s = Settings.Instance;
                if (s != null)
                {
                    if (want && s.NetEnabled != null && !s.NetEnabled.Value)
                        s.NetEnabled.Value = true;
                    if (s.NetSessionUi != null) s.NetSessionUi.Value = want;
                    Settings.SaveConfig();
                }
            }
            catch { }

            if (want)
            {
                if (_shell == null) Create();
                _visible = _shell != null;
            }
            else
            {
                VpbNetSteamFlow.ResetToChoose();
                _copiedLive = false;
                _leaveConfirm = false;
                _kickConfirm = false;
                Destroy();
            }
            NotifyChrome();
        }

        public static void Poll()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.5f;

            bool want = false;
            bool live = false;
            bool ui = false;
            try
            {
                Settings s = Settings.Instance;
                if (s != null)
                {
                    ui = s.NetSessionUi != null && s.NetSessionUi.Value;
                    bool on = s.NetEnabled != null && s.NetEnabled.Value;
                    live = InSession();
                    want = on && (ui || live);
                }
            }
            catch { }

            if (!want)
            {
                if (_shell != null)
                {
                    Destroy();
                    NotifyChrome();
                }
                return;
            }

            if (_shell == null)
            {
                if (!Create()) return;
                if (live && !ui)
                {
                    _collapsed = true;
                    ApplyCollapsed();
                }
                _visible = true;
                NotifyChrome();
                return;
            }
            _visible = true;
        }

        public static void Tick()
        {
            // In VR the window rides a gallery pane; if that pane went away, Poll rebuilds on a new host.
            if (VpbNetUiKit.Lost(_shell)) Destroy();
            if (!_visible || _shell == null) return;
            RescaleIfNeeded();
            HandleKeys();

            float now = Time.realtimeSinceStartup;
            if (_hudToastUntil > 0f && now >= _hudToastUntil)
            {
                _hudToastUntil = 0f;
                SyncTitle();
            }

            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshSeconds;

            if (!_collapsed) VpbNetAvatarRoster.Poll();
            if (!HasChanged()) return;
            Refresh();
        }

        static bool HasChanged()
        {
            _snap[0] = (int)VpbNetPresence.State;
            _snap[1] = (int)VpbNetPresence.Reason;
            _snap[2] = VpbNetPresence.PeerUp ? 1 : 0;
            _snap[3] = (VpbNetPresence.AsHost ? 1 : 0)
                | (VpbNetPresence.Wanted ? 2 : 0)
                | (VpbNetPresence.IsActive ? 4 : 0);
            _snap[4] = VpbNetPresence.PeerId;
            _snap[5] = Hash(VpbNetPresence.Status);
            _snap[6] = Hash(VpbNetPresence.PeerName);
            _snap[7] = Hash(VpbNetPresence.Invite);
            _snap[8] = Hash(VpbNetPresence.Address);
            _snap[9] = Hash(VpbNetPresence.Room);
            _snap[10] = Hash(VpbNetPresence.LocalName);
            _snap[11] = Hash(VpbNetPresence.ReasonText);
            _snap[12] = Hash(VpbNetPresence.Hint);
            _snap[13] = VpbNetPresence.RoomCodeLocked ? 1 : 0;
            _snap[14] = Hash(ConfiguredRoom());
            _snap[15] = VpbNetAvatarRoster.Revision;
            _snap[16] = Hash(VpbNetPresence.MyAvatar);
            _snap[17] = Hash(VpbNetPresence.PeerAvatar)
                ^ (VpbNetPresence.ScenesMatch ? 1 : 0)
                ^ (VpbNetPresence.ShareObjects ? 2 : 0)
                ^ (VpbNetPresence.ShareObjectsLive ? 4 : 0);
            _snap[18] = VpbNetPresence.CollisionsOff ? 1 : 0;
            _snap[19] = unchecked((int)VpbNetRulebook.Local.Revision);
            _snap[20] = Hash(VpbNetPresence.ContentWarning);
            _snap[21] = InSession() ? 1 : 0;
            _snap[22] = _copiedLive ? 1 : 0;
            _snap[23] = Hash(VpbNetPresence.LocalName);
            _snap[24] = (int)VpbNetRulebook.LocalPreset();
            _snap[25] = _setupOpen ? 1 : 0;
            _snap[26] = (VpbNetTransportChoice.IsSteam() ? 1 : 0)
                | (VpbNetTransportChoice.IdentityAcknowledged() ? 2 : 0)
                | (VpbNetTransportChoice.LibraryPresent() ? 4 : 0)
                | (VpbNetTransportChoice.DirectIpAcknowledged() ? 8 : 0);
            _snap[27] = VpbNetSteamFlow.Snapshot();
            _snap[28] = _rulesOpen ? 1 : 0;
            _snap[29] = Hash(VpbNetPresence.PendingAvatar)
                ^ (VpbNetPresence.AvatarClaimDenied ? 1 : 0)
                ^ (VpbNetPresence.SeatPickWanted ? 2 : 0)
                ^ Hash(VpbNetPresence.ClaimDenyReason)
                ^ (VpbNetPresence.PeerClaimRule << 8);
            _snap[30] = unchecked((int)VpbNetPresence.ClaimRevision)
                ^ Hash(VpbNetPresence.MyAvatar)
                ^ Hash(VpbNetPresence.PeerAvatar);
            _snap[31] = _leaveConfirm ? 1 : 0;
            _snap[32] = _moreOpen ? 1 : 0;
            _snap[33] = _kickConfirm ? 1 : 0;

            if (!_havePrev)
            {
                for (int i = 0; i < _snap.Length; i++) _prev[i] = _snap[i];
                _havePrev = true;
                return true;
            }

            bool changed = false;
            for (int i = 0; i < _snap.Length; i++)
            {
                if (_snap[i] == _prev[i]) continue;
                _prev[i] = _snap[i];
                changed = true;
            }
            return changed;
        }

        static int Hash(string s)
        {
            if (s == null) return 0;
            int h = s.Length;
            for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
            return h;
        }

        public static void RescaleIfNeeded()
        {
            if (_shell == null || _rescaleLock) return;
            if (!VpbNetUiKit.ScaleDrifted(_scale)) return;
            _rescaleLock = true;
            try
            {
                bool collapsed = _collapsed;
                bool setupOpen = _setupOpen;
                bool moreOpen = _moreOpen;
                bool rulesOpen = _rulesOpen;
                bool leaveConfirm = _leaveConfirm;
                bool kickConfirm = _kickConfirm;
                bool copiedLive = _copiedLive;
                bool hudToastShown = _hudToastShown;
                float hudToastUntil = _hudToastUntil;
                Destroy();
                if (!Create()) return;
                _collapsed = collapsed;
                _setupOpen = setupOpen;
                _moreOpen = moreOpen;
                _rulesOpen = rulesOpen;
                _leaveConfirm = leaveConfirm;
                _kickConfirm = kickConfirm;
                _copiedLive = copiedLive;
                _hudToastShown = hudToastShown;
                _hudToastUntil = hudToastUntil;
                ApplyCollapsed();
                ApplySetup();
                RefreshNow();
            }
            finally { _rescaleLock = false; }
        }

        static bool InSession()
        {
            return VpbNetPresence.IsActive || VpbNetPresence.Wanted;
        }

        static string ConfiguredRoom()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetLanRoomCode != null && s.NetLanRoomCode.Value != null)
                    return s.NetLanRoomCode.Value;
            }
            catch { }
            return string.Empty;
        }

        static bool Create()
        {
            try
            {
                _scale = VpbNetUiKit.Scale();
                float s = _scale;

                _shell = VpbNetUiKit.BuildWindow(HostName,
                    VPBTranslation.T("net_session.title", "Play with a friend"),
                    PanelWidthRef, s, SortingOrder,
                    new Vector2(PersistedX(), PersistedY()), SavePosition);

                _headDot = VpbNetUiKit.TitleDot(_shell, s, out _headCount);
                _hudCopyChip = VpbNetUiKit.TitleIconTextChip(_shell, "clipboard-copy",
                    VPBTranslation.T("net_session.copy_short", "Copy"), HudCopyChipRef, s, CopyClicked);
                _rulesChip = VpbNetUiKit.TitleIconTextChip(_shell, "list-check",
                    VPBTranslation.T("net_session.my_rules", "My rules"), RulesChipRef, s,
                    VpbNetRulesUi.Toggle);
                _collapseChip = VpbNetUiKit.TitleIconChip(_shell, "chevron-up", s, ToggleCollapse);
                _closeChip = VpbNetUiKit.TitleIconChip(_shell, "x", s, CloseFromButton);

                GameObject body = _shell.Body;
                _statusLine = VpbNetUiKit.StatusRow(body, s, out _statusFill, out _statusDot);
                _hintLine = VpbNetUiKit.Line(body, string.Empty, VpbNetUiKit.FontCaption,
                    UI.TextDim, VpbNetUiKit.LineRef, s, true);

                BuildIdle(body, s);
                BuildLive(body, s);
                BuildRules(body, s);

                _avatarRevision = -1;
                ApplyCollapsed();
                ApplySetup();
                _havePrev = false;
                _nextRefresh = 0f;
                Refresh();
                if (_shell.PanelRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_shell.PanelRT);
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] session panel create failed: " + e.Message);
                Destroy();
                return false;
            }
        }

        static void BuildIdle(GameObject body, float s)
        {
            _idlePane = VpbNetUiKit.Pane(body, "Idle", s);

            _steamGate = VpbNetUiKit.Card(_idlePane, s);
            _steamGateText = VpbNetUiKit.Line(_steamGate, string.Empty,
                VpbNetUiKit.FontCaption, UI.WarnText, VpbNetUiKit.LineRef, s, true);
            GameObject gateRow = VpbNetUiKit.Row(_steamGate, VpbNetUiKit.ButtonRef, s);
            _steamAckChip = VpbNetUiKit.PrimaryIconBtn(gateRow, "circle-check",
                VPBTranslation.T("net_session.steam_ack", "I understand"), 0f, s, AckSteamClicked);
            _steamRepairChip = VpbNetUiKit.PrimaryIconBtn(gateRow, "refresh",
                VPBTranslation.T("net_session.steam_repair", "Repair install"), 0f, s, RepairClicked);
            _steamDirectChip = VpbNetUiKit.IconBtn(gateRow, "network", "plug-connected",
                VPBTranslation.T("net_session.route.use_direct", "Use Direct P2P"), 0f, s, PickDirect);

            _directGate = VpbNetUiKit.Card(_idlePane, s);
            Image directFill = _directGate.GetComponent<Image>();
            if (directFill != null) directFill.color = UI.WarnSurface;
            _directGateText = VpbNetUiKit.Line(_directGate, string.Empty,
                VpbNetUiKit.FontCaption, UI.WarnText, VpbNetUiKit.LineRef, s, true);
            GameObject directGateRow = VpbNetUiKit.Row(_directGate, VpbNetUiKit.ButtonRef, s);
            VpbNetUiKit.PrimaryIconBtn(directGateRow, "circle-check",
                VPBTranslation.T("net_session.direct_ack", "I understand - no privacy"), 0f, s,
                AckDirectClicked);
            VpbNetUiKit.IconBtn(directGateRow, "brand-steam", "hexagon-letter-s",
                VPBTranslation.T("net_session.route.stay_steam", "Stay on Steam"), 0f, s, PickSteam);

            VpbNetSteamFlow.Build(_idlePane, s);

            GameObject setupHead = VpbNetUiKit.Row(_idlePane, VpbNetUiKit.ButtonRef, s);
            _setupChip = VpbNetUiKit.IconBtn(setupHead, "chevron-down",
                VPBTranslation.T("net_session.setup", "Advanced"), SetupChipRef, s, ToggleSetup);
            _setupSummary = VpbNetUiKit.Line(setupHead, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, false);

            _setupBody = VpbNetUiKit.Pane(_idlePane, "Setup", s);
            GameObject rulesRow = VpbNetUiKit.Row(_setupBody, VpbNetUiKit.ButtonRef, s);
            _presetWatchChip = VpbNetUiKit.IconBtn(rulesRow, "eye",
                VPBTranslation.T("net_rules.preset.watch", "Watch together"), 0f, s, ApplyWatchPreset);
            _presetCustomChip = VpbNetUiKit.IconBtn(rulesRow, "list-check",
                VPBTranslation.T("net_session.rules_custom", "What I allow here"), 0f, s,
                VpbNetRulesUi.Toggle);
            _setupRulesHint = VpbNetUiKit.Line(_setupBody, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, true);

            VpbNetUiKit.SectionHeader(_setupBody,
                VPBTranslation.T("net_session.route", "How you reach each other"), s, false);
            GameObject routeRow = VpbNetUiKit.Row(_setupBody, VpbNetUiKit.ButtonRef, s);
            _transportSteam = VpbNetUiKit.IconBtn(routeRow, "brand-steam", "hexagon-letter-s",
                VPBTranslation.T("net_session.route.steam", "Steam"), 0f, s, PickSteam);
            _transportDirect = VpbNetUiKit.IconBtn(routeRow, "network", "plug-connected",
                VPBTranslation.T("net_session.route.direct", "Direct P2P"), 0f, s, PickDirect);
            _transportHint = VpbNetUiKit.Line(_setupBody, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, true);
        }

        static void BuildLive(GameObject body, float s)
        {
            _livePane = VpbNetUiKit.Pane(body, "Live", s);

            _gatesPane = VpbNetUiKit.Pane(_livePane, "Gates", s);
            VpbNetUiKit.SectionHeader(_gatesPane,
                VPBTranslation.T("net_session.gates", "Before you can play"), s, true);
            _gateScene = VpbNetUiKit.Line(_gatesPane, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, true);
            _gateMine = VpbNetUiKit.Line(_gatesPane, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, true);
            _gateTheirs = VpbNetUiKit.Line(_gatesPane, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, true);
            _gateCap = VpbNetUiKit.Line(_gatesPane,
                VPBTranslation.T("net_session.gates.cap",
                    "A room holds two people. Watching still uses one of the two places."),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);

            _peoplePane = VpbNetUiKit.Pane(_livePane, "People", s);
            VpbNetUiKit.SectionHeader(_peoplePane,
                VPBTranslation.T("net_session.people", "Players"), s, true);
            VpbNetUiKit.Line(_peoplePane,
                VPBTranslation.T("net_session.people.hint",
                    "Names here are the people in the scene, not the people playing."
                    + " Nothing about either of you is sent or shown."),
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, s, true);
            BuildPersonRow(_peoplePane, s, out _youDot, out _youName, out _youRide);
            BuildPersonRow(_peoplePane, s, out _themDot, out _themName, out _themRide);

            _ridePane = VpbNetUiKit.Pane(_livePane, "Ride", s);
            VpbNetUiKit.SectionHeader(_ridePane,
                VPBTranslation.T("net_session.ride", "Person you control"), s, false);
            _avatarRow = VpbNetUiKit.WrapRow(_ridePane, RideCellRef, VpbNetUiKit.ButtonRef, s);
            _rideHint = VpbNetUiKit.Line(_ridePane, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, true);

            _roomPane = VpbNetUiKit.Card(_livePane, s);
            _liveRoomText = VpbNetUiKit.Line(_roomPane, string.Empty, VpbNetUiKit.FontDisplay,
                UI.TextMuted, VpbNetUiKit.ButtonRef, s, true);
            GameObject roomAct = VpbNetUiKit.Row(_roomPane, VpbNetUiKit.ButtonRef, s);
            _copyChip = VpbNetUiKit.PrimaryIconBtn(roomAct, "clipboard-copy",
                VPBTranslation.T("net_session.copy", "Copy invite"), 0f, s, CopyClicked);
            _liveNewChip = VpbNetUiKit.IconBtn(roomAct, "refresh",
                VPBTranslation.T("net_session.new_code", "New"), NewChipRef, s, GenerateClicked);
            _liveLockChip = VpbNetUiKit.IconBtn(roomAct, "lock", "pin",
                VPBTranslation.T("net_session.lock", "Lock"), LockChipRef, s, ProtectClicked);
            _liveRoomHint = VpbNetUiKit.Line(_roomPane, string.Empty,
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, s, true);

            _worldPane = VpbNetUiKit.Pane(_livePane, "World", s);
            GameObject worldRow = VpbNetUiKit.Row(_worldPane, VpbNetUiKit.ButtonRef, s);
            _collideChip = VpbNetUiKit.IconBtn(worldRow, "target", string.Empty, 0f, s, CollideClicked);

            _peerPane = VpbNetUiKit.Pane(_livePane, "Peer", s);
            GameObject peerActs = VpbNetUiKit.Row(_peerPane, VpbNetUiKit.ButtonRef, s);
            _kickChip = VpbNetUiKit.IconBtn(peerActs, "user-off",
                VPBTranslation.T("net_session.kick", "Kick"), 0f, s, AskKick);
            _resyncChip = VpbNetUiKit.IconBtn(peerActs, "refresh",
                VPBTranslation.T("net_session.resync", "Resync"), 0f, s, VpbNetPresence.RequestResync);

            _leavePane = VpbNetUiKit.Card(_livePane, s);
            Image leaveFill = _leavePane.GetComponent<Image>();
            if (leaveFill != null) leaveFill.color = UI.WarnSurface;
            VpbNetUiKit.Line(_leavePane,
                VPBTranslation.T("net_session.leave_warn",
                    "Leave this session? They will be disconnected."),
                VpbNetUiKit.FontBody, UI.WarnText, VpbNetUiKit.LineRef, s, true);
            GameObject leaveRow = VpbNetUiKit.Row(_leavePane, VpbNetUiKit.ButtonRef, s);
            VpbNetUiKit.PrimaryBtn(leaveRow,
                VPBTranslation.T("net_session.leave_stay", "Stay"), 0f, s, CancelLeave);
            VpbNetUiKit.DangerIconBtn(leaveRow, "door-exit",
                VPBTranslation.T("net_session.leave", "Leave"), 0f, s, ConfirmLeave);

            _kickPane = VpbNetUiKit.Card(_livePane, s);
            Image kickFill = _kickPane.GetComponent<Image>();
            if (kickFill != null) kickFill.color = UI.WarnSurface;
            VpbNetUiKit.Line(_kickPane,
                VPBTranslation.T("net_session.kick_warn",
                    "Remove them from this room? They will be disconnected."),
                VpbNetUiKit.FontBody, UI.WarnText, VpbNetUiKit.LineRef, s, true);
            GameObject kickRow = VpbNetUiKit.Row(_kickPane, VpbNetUiKit.ButtonRef, s);
            VpbNetUiKit.PrimaryBtn(kickRow,
                VPBTranslation.T("net_session.leave_stay", "Stay"), 0f, s, CancelKick);
            VpbNetUiKit.DangerIconBtn(kickRow, "user-off",
                VPBTranslation.T("net_session.kick", "Kick"), 0f, s, ConfirmKick);

            _liveFooter = VpbNetUiKit.Pane(_livePane, "Footer", s);
            VpbNetUiKit.Rule(_liveFooter, s);
            VpbNetUiKit.MakeDragBar(_shell, _liveFooter, SavePosition);
            GameObject footer = VpbNetUiKit.Row(_liveFooter, VpbNetUiKit.ButtonRef, s);
            VpbNetUiKit.DangerIconBtn(footer, "door-exit",
                VPBTranslation.T("net_session.leave", "Leave"), 0f, s, AskLeave);
            _inviteChip = VpbNetUiKit.PrimaryIconBtn(footer, "door-enter",
                VPBTranslation.T("net_session.invite_scene", "Bring them here"), 0f, s, InviteClicked);
            _moreChip = VpbNetUiKit.IconBtn(footer, "settings",
                VPBTranslation.T("net_session.more", "More"), MoreChipRef, s, ToggleMore);
        }

        static void BuildRules(GameObject body, float s)
        {
            _rulesPane = VpbNetUiKit.Pane(body, "Rules", s);
            VpbNetRulesUi.Attach(_rulesPane, _shell != null ? _shell.Root : null, s);
            VpbNetUiKit.Show(_rulesPane, false);
        }

        static void InviteClicked()
        {
            string reason;
            if (VpbNetPresence.InviteToCurrentScene(out reason))
            {
                _inviteError = null;
                _inviteErrorUntil = 0f;
            }
            else
            {
                _inviteError = string.IsNullOrEmpty(reason)
                    ? VPBTranslation.T("net_session.invite_failed",
                        "They were not invited - nothing was sent.")
                    : reason;
                _inviteErrorUntil = Time.realtimeSinceStartup + InviteErrorSeconds;
                LogUtil.LogWarning("[VPB.Net] invite not sent: " + _inviteError);
            }
            _nextRefresh = 0f;
            _nextInviteProbe = 0f;
        }

        static void AskLeave()
        {
            _leaveConfirm = true;
            _kickConfirm = false;
            _collapsed = false;
            ApplyCollapsed();
            RefreshNow();
        }

        static void CancelLeave()
        {
            _leaveConfirm = false;
            RefreshNow();
        }

        static void ConfirmLeave()
        {
            _leaveConfirm = false;
            _kickConfirm = false;
            _hudToastShown = false;
            _hudToastUntil = 0f;
            VpbNetPresence.Leave();
            NotifyChrome();
            RefreshNow();
        }

        static void AskKick()
        {
            _kickConfirm = true;
            _leaveConfirm = false;
            _collapsed = false;
            ApplyCollapsed();
            RefreshNow();
        }

        static void CancelKick()
        {
            _kickConfirm = false;
            RefreshNow();
        }

        static void ConfirmKick()
        {
            _kickConfirm = false;
            VpbNetPresence.KickPeer();
            RefreshNow();
        }

        static void ToggleMore()
        {
            _moreOpen = !_moreOpen;
            RefreshNow();
        }

        static void ApplyWatchPreset()
        {
            VpbNetRulebook.ApplyPreset(VpbNetRulePreset.WatchTogether);
            RefreshNow();
        }

        static void RepairClicked()
        {
            try
            {
                VamHookPlugin plugin = VamHookPlugin.singleton;
                if (plugin != null && plugin.Updater != null)
                    plugin.Updater.CheckForUpdateAsync();
            }
            catch { }

            try
            {
                Gallery g = Gallery.singleton;
                if (g == null || g.Panels == null) return;
                for (int i = 0; i < g.Panels.Count; i++)
                {
                    GalleryPanel p = g.Panels[i];
                    if (p == null) continue;
                    p.OpenSettingsGroup("updater");
                    return;
                }
            }
            catch { }
        }

        static void HandleKeys()
        {
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            bool up = Input.GetKeyDown(KeyCode.UpArrow);
            bool down = Input.GetKeyDown(KeyCode.DownArrow);
            if (!esc && !enter && !up && !down) return;

            try
            {
                if (VpbNetRulebook.HasPending) return;
                if (VpbNetContentUi.IsOpen) return;
                if (VpbNetAlertUi.IsOpen) return;
            }
            catch { }

            if (_leaveConfirm)
            {
                if (esc) CancelLeave();
                return;
            }
            if (_kickConfirm)
            {
                if (esc) CancelKick();
                return;
            }

            if (_rulesOpen)
            {
                if (esc) HideRules();
                return;
            }

            if (_moreOpen && esc && InSession())
            {
                _moreOpen = false;
                RefreshNow();
                return;
            }

            if (_setupOpen && esc && !InSession())
            {
                ToggleSetup();
                return;
            }

            if ((up || down) && !InSession() && !VpbNetSteamFlow.JoinFocused())
            {
                VpbNetSteamFlow.OnArrow(up);
                return;
            }
            if (VpbNetSteamFlow.JoinFocused())
            {
                if (esc) VpbNetSteamFlow.OnEscape();
                return;
            }

            if (esc)
            {
                if (!InSession() && VpbNetSteamFlow.OnEscape()) return;
                if (InSession())
                {
                    if (!_collapsed) CollapseLive(true);
                    return;
                }
                SetWanted(false);
                return;
            }

            if (enter && !InSession() && !_collapsed)
                VpbNetSteamFlow.OnSubmit();
        }

        static void BuildPersonRow(GameObject parent, float s, out Image dot,
            out Text name, out Text ride)
        {
            GameObject row = VpbNetUiKit.Row(parent, VpbNetUiKit.LineRef, s);

            GameObject dgo = UI.CreateChildRT(row, "Dot");
            dot = UI.AddGalleryElementRoundedBg(dgo, UI.TextDim, false);
            RoundedRect rr = dot as RoundedRect;
            if (rr != null)
            {
                rr.excludeFromGlobalRadiusSync = true;
                rr.cornerRadiusFraction = 0.5f;
            }
            float d = PersonDotRef * s;
            UI.AddLE(dgo, minWidth: d, preferredWidth: d, flexibleWidth: 0f,
                minHeight: d, preferredHeight: d, flexibleHeight: 0f);

            name = VpbNetUiKit.Line(row, string.Empty, VpbNetUiKit.FontBody,
                UI.TextPrimary, VpbNetUiKit.LineRef, s, false);
            ride = VpbNetUiKit.Line(row, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextMuted, VpbNetUiKit.LineRef, s, false);
            ride.alignment = TextAnchor.MiddleRight;
            VpbNetUiKit.FixWidth(ride.gameObject, PersonRideRef, s);
        }

        static void Refresh()
        {
            if (_shell == null) return;

            SyncTitle();
            SyncHeadCount();
            SyncRulesChip();
            SyncHudChrome();
            if (_collapsed) return;

            bool live = InSession();
            bool paneChanged = live != _inSession;
            if (paneChanged)
            {
                _inSession = live;
                _copiedLive = false;
                _leaveConfirm = false;
                _kickConfirm = false;
                if (!live)
                {
                    _moreOpen = false;
                    _hudToastShown = false;
                    _hudToastUntil = 0f;
                }
            }
            bool rules = _rulesOpen;
            VpbNetUiKit.Show(_idlePane, !live && !rules);
            VpbNetUiKit.Show(_livePane, live && !rules);
            VpbNetUiKit.Show(_rulesPane, rules);
            if (rules) VpbNetRulesUi.SyncIfVisible();

            SyncStatus();
            if (live)
            {
                SyncGates();
                SyncPeople();
                SyncRoomLive();
                SyncWorld();
                SyncAvatarButtons();
                SyncFooter();
            }
            else
            {
                SyncIdle();
            }
            if (_shell.PanelRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_shell.PanelRT);
        }

        internal static void RefreshNow()
        {
            _havePrev = false;
            _nextRefresh = 0f;
            Refresh();
            if (_shell != null && _shell.PanelRT != null && !_collapsed)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_shell.PanelRT);
        }

        static void SyncTitle()
        {
            if (_shell == null || _shell.Title == null) return;

            _sb.Length = 0;
            if (_hudToastUntil > Time.realtimeSinceStartup)
            {
                _sb.Append(VPBTranslation.T("net_session.still_in", "Still in session"));
            }
            else if (!InSession())
            {
                _sb.Append(VPBTranslation.T("net_session.title", "Play with a friend"));
            }
            else
            {
                _sb.Append(VpbNetPresence.AsHost
                    ? VPBTranslation.T("net_session.title_host", "Room · Hosting")
                    : VPBTranslation.T("net_session.title_join", "Room · Joining"));
            }
            SetText(_shell.Title, _sb.ToString());
        }

        // Collapsed title bar is the whole window — link-up and head count live here.
        static void SyncHeadCount()
        {
            bool live = InSession();
            Color tone = UI.TextDim;
            if (live)
            {
                VpbNetSessionState st = VpbNetPresence.State;
                if (st == VpbNetSessionState.Dropped) tone = UI.AccentRed;
                else if (st == VpbNetSessionState.Reconnecting || st == VpbNetSessionState.Stalled)
                    tone = UI.WarnText;
                else if (VpbNetPresence.PeerUp && st == VpbNetSessionState.Running) tone = UI.AccentLive;
                else tone = UI.AccentBlue;
            }

            if (_headDot != null) _headDot.color = tone;
            if (_headCount == null) return;
            SetText(_headCount, !live ? string.Empty : (VpbNetPresence.PeerUp ? "2" : "1"));
            _headCount.color = live ? UI.TextPrimary : UI.TextDim;
        }

        // Title bar, so it stays reachable while the window is collapsed to that strip.
        static void SyncRulesChip()
        {
            if (_rulesChip == null) return;
            _rulesChip.SetRole(_rulesOpen ? UI.AccentBlue : UI.ChromePanel, UI.TextPrimary);
        }

        static void SyncStatus()
        {
            Color dot = UI.TextDim;
            Color text = UI.TextPrimary;
            Color banner = UI.ChromeDark;
            _sb.Length = 0;

            if (_rulesOpen && !InSession())
            {
                SetText(_statusLine, VPBTranslation.T("net_session.status.rules",
                    "Your answers — they cannot change these"));
                if (_statusLine != null) _statusLine.color = UI.TextPrimary;
                if (_statusDot != null) _statusDot.color = UI.AccentBlue;
                if (_statusFill != null) _statusFill.color = UI.ChromeDark;
                SetLine(_hintLine, null);
                return;
            }

            if (!InSession())
            {
                if (VpbNetTransportChoice.BlockedReason() != null)
                {
                    _sb.Append(VPBTranslation.T("net_session.status.confirm",
                        "Confirm below, then pick I have a code or I'll make a room"));
                }
                else
                {
                    int step = VpbNetSteamFlow.Step;
                    bool steam = VpbNetTransportChoice.IsSteam();
                    if (step == VpbNetSteamFlow.StepHost)
                    {
                        _sb.Append(steam
                            ? VPBTranslation.T("net_session.status.host_steam",
                                "Copy the code, send it, then Open room")
                            : VPBTranslation.T("net_session.status.host_direct",
                                "Press Open room, then copy the invite"));
                    }
                    else if (step == VpbNetSteamFlow.StepJoin)
                    {
                        _sb.Append(steam
                            ? (VpbNetRoomBookStore.JoinCount > 0
                                ? VPBTranslation.T("net_session.status.join_steam_recent",
                                    "Pick a recent, or paste their room code, then Join")
                                : VPBTranslation.T("net_session.status.join_steam",
                                    "Paste their room code, then Join"))
                            : (VpbNetRoomBookStore.JoinCount > 0
                                ? VPBTranslation.T("net_session.status.join_direct_recent",
                                    "Pick a recent, or paste their invite, then Join")
                                : VPBTranslation.T("net_session.status.join_direct",
                                    "Paste their invite, then Join")));
                    }
                    else
                    {
                        _sb.Append(VPBTranslation.T("net_session.status.pick",
                            "I have a code, or I'll make a room"));
                    }
                }
            }
            else
            {
                VpbNetSessionState st = VpbNetPresence.State;
                if (VpbNetPresence.PeerUp && st == VpbNetSessionState.Running)
                {
                    if (VpbNetRulebook.HasPending)
                    {
                        int waiting = VpbNetRulebook.PendingCount;
                        _sb.Append(waiting > 1
                            ? VPBTranslation.T("net_session.status.asks",
                                "They are waiting on you to answer ") + waiting
                                + VPBTranslation.T("net_session.status.asks_tail", " requests")
                            : VPBTranslation.T("net_session.status.ask",
                                "They are waiting on you to answer a request"));
                        dot = UI.WarnText;
                        text = UI.WarnText;
                        banner = UI.WarnSurface;
                    }
                    else if (VpbNetPresence.PendingAvatar.Length > 0)
                    {
                        _sb.Append(VPBTranslation.T("net_session.status.claim_wait",
                            "Waiting for them to let you play as "))
                            .Append(VpbNetPresence.PendingAvatar);
                        dot = UI.AccentBlue;
                        text = UI.TextPrimary;
                        banner = UI.AccentBlue;
                    }
                    else if (VpbNetPresence.AvatarClaimDenied
                        && !string.IsNullOrEmpty(VpbNetPresence.ClaimDenyReason))
                    {
                        _sb.Append(VpbNetPresence.ClaimDenyReason);
                        dot = UI.WarnText;
                        text = UI.WarnText;
                        banner = UI.WarnSurface;
                    }
                    else if (!VpbNetPresence.ScenesMatch)
                    {
                        AppendSceneMismatch();
                        dot = UI.WarnText;
                        text = UI.WarnText;
                        banner = UI.WarnSurface;
                    }
                    else if (string.IsNullOrEmpty(VpbNetPresence.MyAvatar))
                    {
                        _sb.Append(VPBTranslation.T("net_session.status.ride",
                            "Click a Person to control"));
                        dot = UI.AccentBlue;
                        text = UI.TextPrimary;
                        banner = UI.AccentBlue;
                    }
                    else
                    {
                        _sb.Append(VPBTranslation.T("net_session.status.connected", "Connected"));
                        dot = UI.RuleAllowed;
                        text = UI.RuleAllowedText;
                        banner = UI.ChromeDark;
                    }
                }
                else if (st == VpbNetSessionState.Reconnecting || st == VpbNetSessionState.Stalled)
                {
                    _sb.Append(VpbNetPresence.Status);
                    dot = UI.WarnText;
                    text = UI.WarnText;
                    banner = UI.WarnSurface;
                }
                else if (st == VpbNetSessionState.Dropped)
                {
                    _sb.Append(VpbNetPresence.Status);
                    dot = UI.AccentRed;
                    text = UI.RuleBlockedText;
                    banner = UI.RuleBlocked;
                }
                else if (VpbNetPresence.AsHost)
                {
                    _sb.Append(VpbNetTransportChoice.IsSteam()
                        ? VPBTranslation.T("net_session.status.waiting_steam",
                            "Send this code, then wait")
                        : VPBTranslation.T("net_session.status.waiting_direct",
                            "Copy the invite and send it"));
                    dot = UI.AccentBlue;
                    banner = UI.AccentBlue;
                }
                else
                {
                    _sb.Append(VpbNetPresence.Status);
                    dot = UI.AccentBlue;
                    banner = UI.AccentBlue;
                }
            }

            SetText(_statusLine, _sb.ToString());
            if (_statusLine != null) _statusLine.color = text;
            if (_statusDot != null) _statusDot.color = dot;
            if (_statusFill != null) _statusFill.color = banner;

            _sb.Length = 0;
            if (_inviteErrorUntil > Time.realtimeSinceStartup && !string.IsNullOrEmpty(_inviteError))
                Append(_sb, _inviteError);
            else _inviteError = null;
            if (!string.IsNullOrEmpty(VpbNetPresence.ContentWarning))
                Append(_sb, VpbNetPresence.ContentWarning);
            if (!string.IsNullOrEmpty(VpbNetPresence.ReasonText)
                && VpbNetPresence.State == VpbNetSessionState.Dropped)
                Append(_sb, VpbNetPresence.ReasonText);
            else if (!string.IsNullOrEmpty(VpbNetPresence.Hint) && !VpbNetPresence.PeerUp)
                Append(_sb, VpbNetPresence.Hint);

            SetLine(_hintLine, _sb.Length > 0 ? _sb.ToString() : null);
        }

        // Naming both sides saves asking "what scene are you in?" over chat.
        static void AppendSceneMismatch()
        {
            _sb.Append(VPBTranslation.T("net_session.hint.scene",
                "Both sides must load the same scene before you can control a Person."));

            string theirs = VpbNetPresence.HavePeerScene ? VpbNetPresence.PeerScene : null;
            if (string.IsNullOrEmpty(theirs)) return;

            _sb.Append(VPBTranslation.T("net_session.hint.scene_theirs", " They are in "))
                .Append(LeafOf(theirs));

            string mine = VpbNetPresence.LocalScene;
            if (string.IsNullOrEmpty(mine)) return;
            _sb.Append(VPBTranslation.T("net_session.hint.scene_mine", ", you are in "))
                .Append(LeafOf(mine)).Append('.');
        }

        static string LeafOf(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return uid;
            int slash = uid.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < uid.Length) return uid.Substring(slash + 1);
            return uid;
        }

        static void Append(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (sb.Length > 0) sb.Append("  ·  ");
            sb.Append(s);
        }

        static void SyncRoute()
        {
            bool steam = VpbNetTransportChoice.IsSteam();
            SetPresetChip(_transportDirect, !steam);
            SetPresetChip(_transportSteam, steam);

            if (steam)
            {
                SetText(_transportHint, VPBTranslation.T("net_session.route.steam_hint2",
                    "Default. A room code is the only thing you exchange. Neither of you sees an IP address."),
                    UI.TextDim);
            }
            else
            {
                SetText(_transportHint, VPBTranslation.T("net_session.route.direct_hint",
                    "No privacy. Each of you sees the other's IP address. There is no relay."),
                    UI.WarnText);
            }

            string steamBlocked = steam ? VpbNetTransportChoice.BlockedReason() : null;
            VpbNetUiKit.Show(_steamGate, steamBlocked != null);
            bool ident = steam && !VpbNetTransportChoice.IdentityAcknowledged();
            bool lib = steam && !ident && !VpbNetTransportChoice.LibraryPresent();
            if (steamBlocked != null)
            {
                SetText(_steamGateText, ident
                    ? steamBlocked
                    : VPBTranslation.T("net_session.steam_missing",
                        "Steam file is missing from the VPB install. Repair the install, or use Direct as a last resort."));
            }
            if (_steamAckChip != null) _steamAckChip.SetActive(ident);
            if (_steamRepairChip != null) _steamRepairChip.SetActive(lib);
            if (_steamDirectChip != null) _steamDirectChip.SetActive(lib);

            bool directBlocked = !steam && !VpbNetTransportChoice.DirectIpAcknowledged();
            VpbNetUiKit.Show(_directGate, directBlocked);
            if (directBlocked)
                SetText(_directGateText, VpbNetTransportChoice.DirectIpWarning());

            bool ready = VpbNetTransportChoice.ReadyToConnect();
            VpbNetSteamFlow.Show(ready);
            if (ready) VpbNetSteamFlow.Sync(ready);
        }

        static void PickDirect()
        {
            VpbNetTransportChoice.Set(VpbNetTransportChoice.Direct);
            VpbNetSteamFlow.ResetToChoose();
            NotifySettings();
            RefreshNow();
        }

        static void PickSteam()
        {
            VpbNetTransportChoice.Set(VpbNetTransportChoice.Steam);
            VpbNetTransportChoice.ForgetLibrary();
            VpbNetPresence.EnsureRoomCode();
            VpbNetSteamFlow.ResetToChoose();
            NotifySettings();
            RefreshNow();
        }

        static void AckDirectClicked()
        {
            VpbNetTransportChoice.AcknowledgeDirectIp();
            RefreshNow();
        }

        static void AckSteamClicked()
        {
            VpbNetTransportChoice.AcknowledgeIdentity();
            VpbNetTransportChoice.ForgetLibrary();
            RefreshNow();
        }

        static void SyncIdle()
        {
            SyncRoute();

            byte preset = VpbNetRulebook.LocalPreset();
            SetText(_setupRulesHint, PresetHint(preset));
            SetText(_setupSummary, SetupSummary(preset));
            SetPresetChip(_presetWatchChip, preset == VpbNetRulePreset.WatchTogether);
            SetPresetChip(_presetCustomChip, _rulesOpen || preset == VpbNetRulePreset.Custom);
            ApplySetup();
        }

        static string SetupSummary(byte preset)
        {
            _sb.Length = 0;
            _sb.Append(VpbNetTransportChoice.IsSteam()
                ? VPBTranslation.T("net_session.route.steam", "Steam")
                : VPBTranslation.T("net_session.route.direct", "Direct P2P"));
            _sb.Append("  ·  ");
            switch (preset)
            {
                case VpbNetRulePreset.LockedDown:
                    _sb.Append(VPBTranslation.T("net_rules.preset.locked", "Locked down"));
                    break;
                case VpbNetRulePreset.FullTrust:
                    _sb.Append(VPBTranslation.T("net_rules.preset.trust", "Full trust"));
                    break;
                case VpbNetRulePreset.Custom:
                    _sb.Append(VPBTranslation.T("net_rules.preset.custom", "Custom"));
                    break;
                default:
                    _sb.Append(VPBTranslation.T("net_rules.preset.watch", "Watch together"));
                    break;
            }
            return _sb.ToString();
        }

        // The three preconditions, in order, until all are met. Nobody could work this sequence out.
        static void SyncGates()
        {
            bool peer = VpbNetPresence.PeerUp;
            bool scenes = VpbNetPresence.ScenesMatch;
            bool mine = !string.IsNullOrEmpty(VpbNetPresence.MyAvatar);
            bool theirs = !string.IsNullOrEmpty(VpbNetPresence.PeerAvatar);

            bool show = peer && !(scenes && mine && theirs);
            VpbNetUiKit.Show(_gatesPane, show);
            if (!show) return;

            SetGate(_gateScene, scenes,
                VPBTranslation.T("net_session.gate.scene_ok", "Both of you are in the same scene"),
                VPBTranslation.T("net_session.gate.scene_todo",
                    "Load the same scene on both machines - use Bring them here, or load theirs yourself"));

            SetGate(_gateMine, mine,
                VPBTranslation.T("net_session.gate.mine_ok", "You are playing as ")
                    + Shorten(VpbNetPresence.MyAvatar),
                scenes
                    ? VPBTranslation.T("net_session.gate.mine_todo",
                        "Pick the person you want to play as, below")
                    : VPBTranslation.T("net_session.gate.mine_wait",
                        "Then pick the person you want to play as"));

            SetGate(_gateTheirs, theirs,
                VPBTranslation.T("net_session.gate.theirs_ok", "They are playing as ")
                    + Shorten(VpbNetPresence.PeerAvatar),
                VPBTranslation.T("net_session.gate.theirs_todo",
                    "They are watching - until they pick someone, you will not see them on anybody"));

            if (_gateCap != null) VpbNetUiKit.Show(_gateCap.gameObject, !theirs);
        }

        static void SetGate(Text t, bool done, string okText, string todoText)
        {
            if (t == null) return;
            SetText(t, (done ? "[x]  " : "[ ]  ") + (done ? okText : todoText));
            t.color = done ? UI.RuleAllowedText : UI.TextPrimary;
        }

        static void SyncPeople()
        {
            SetText(_youName, VPBTranslation.T("net_session.you", "You"));
            SetRide(_youRide, _youDot, VpbNetPresence.MyAvatar, VpbNetPresence.PendingAvatar,
                UI.RuleAllowedText, UI.AccentGreen);

            if (!VpbNetPresence.PeerUp)
            {
                SetText(_themName, VpbNetPresence.AsHost
                    ? VPBTranslation.T("net_session.nobody", "Nobody yet")
                    : VPBTranslation.T("net_session.connecting", "Connecting…"));
                SetText(_themRide, "—");
                if (_themRide != null) _themRide.color = UI.TextDim;
                if (_themDot != null) _themDot.color = UI.ChromeMid;
                if (_themName != null) _themName.color = UI.TextDim;
                return;
            }

            SetText(_themName, VPBTranslation.T("net_session.seat.them", "Them"));
            SetRide(_themRide, _themDot, VpbNetPresence.PeerAvatar, null,
                UI.TextMuted, UI.AccentBlue);
            if (_themName != null) _themName.color = UI.TextPrimary;
        }

        static void SetRide(Text ride, Image dot, string uid, string pending, Color text, Color lit)
        {
            bool has = !string.IsNullOrEmpty(uid);
            bool waiting = !has && !string.IsNullOrEmpty(pending);

            if (ride != null)
            {
                if (has) SetText(ride, Shorten(uid));
                else if (waiting)
                    SetText(ride, VPBTranslation.T("net_session.ride.asking", "asking…"));
                else SetText(ride, VPBTranslation.T("net_session.spectating", "Watching"));
                ride.color = has ? text : (waiting ? UI.WarnText : UI.TextDim);
            }
            if (dot != null) dot.color = has ? lit : (waiting ? UI.WarnText : UI.ChromeMid);
        }

        static void SyncRoomLive()
        {
            string live = VpbNetPresence.IsActive ? VpbNetPresence.Room : null;
            string cfg = ConfiguredRoom();
            string shown = !string.IsNullOrEmpty(live) ? live : cfg;
            SetText(_liveRoomText, DisplayRoom(shown));

            bool host = VpbNetPresence.AsHost;
            bool peer = VpbNetPresence.PeerUp;
            bool steam = VpbNetTransportChoice.IsSteam();
            bool have = steam
                ? VpbNetRoomCode.IsWellFormed(cfg) || HaveInvite()
                : HaveInvite();
            if (_copyChip != null)
            {
                _copyChip.SetText(steam
                    ? VPBTranslation.T("net_session.copy_room", "Copy room code")
                    : VPBTranslation.T("net_session.copy", "Copy invite"));
                _copyChip.SetEnabled(have);
                bool shout = host && !peer && have && !_copiedLive;
                _copyChip.SetRole(shout ? UI.AccentGreen : UI.ChromePanel, UI.TextPrimary);
            }
            if (_liveNewChip != null)
            {
                bool showNew = host && _moreOpen;
                _liveNewChip.SetActive(showNew);
                _liveNewChip.SetEnabled(showNew && !VpbNetPresence.RoomCodeLocked);
            }
            if (_liveLockChip != null)
            {
                bool showLock = host && _moreOpen;
                _liveLockChip.SetActive(showLock);
                if (showLock) SyncLockChip(_liveLockChip);
            }

            string note = null;
            if (_copiedLive)
            {
                note = steam
                    ? VPBTranslation.T("net_steam.copied_live",
                        "Copied. Paste it to your friend.")
                    : VPBTranslation.T("net_session.copied_invite",
                        "Copied. Paste the invite to your friend.");
            }
            else if (!peer)
            {
                if (steam)
                    note = host
                        ? VPBTranslation.T("net_session.live_hint.steam_host",
                            "Send this code. They press I have a code, pick a recent or paste it, then Join. Keep Steam running.")
                        : VPBTranslation.T("net_session.live_hint.steam_join",
                            "Looking for that room on Steam. It only appears once they have pressed Open room, and only if they also picked Steam.");
                else if (host)
                    note = VPBTranslation.T("net_session.live_hint.direct_host",
                        "Copy the invite and send it to them. They paste it and press Join.");
            }
            SetLine(_liveRoomHint, note);
            OrderLivePanes(host && !peer);
        }

        static void OrderLivePanes(bool waitingHost)
        {
            bool peer = VpbNetPresence.PeerUp;
            bool scenes = VpbNetPresence.ScenesMatch;
            bool more = _moreOpen;
            bool confirm = _leaveConfirm || _kickConfirm;

            VpbNetUiKit.Show(_roomPane, (waitingHost || more) && !confirm);
            VpbNetUiKit.Show(_ridePane, peer && scenes && !waitingHost && !confirm);
            VpbNetUiKit.Show(_worldPane, more && peer && !confirm);
            VpbNetUiKit.Show(_peerPane, more && peer && !confirm);
            VpbNetUiKit.Show(_leavePane, _leaveConfirm);
            VpbNetUiKit.Show(_kickPane, _kickConfirm);
            VpbNetUiKit.Show(_liveFooter, !confirm);
            if (_moreChip != null)
                _moreChip.SetRole(more ? UI.AccentBlue : UI.ChromePanel, UI.TextPrimary);

            if (_roomPane == null || _peoplePane == null) return;
            int i = 0;
            if (waitingHost)
            {
                if (_roomPane.activeSelf) _roomPane.transform.SetSiblingIndex(i++);
                _peoplePane.transform.SetSiblingIndex(i++);
            }
            else
            {
                _peoplePane.transform.SetSiblingIndex(i++);
                if (_ridePane != null && _ridePane.activeSelf) _ridePane.transform.SetSiblingIndex(i++);
                if (_roomPane.activeSelf) _roomPane.transform.SetSiblingIndex(i++);
                if (_worldPane != null && _worldPane.activeSelf)
                    _worldPane.transform.SetSiblingIndex(i++);
                if (_peerPane != null && _peerPane.activeSelf)
                    _peerPane.transform.SetSiblingIndex(i++);
            }
            if (_leavePane != null && _leavePane.activeSelf) _leavePane.transform.SetSiblingIndex(i++);
            if (_kickPane != null && _kickPane.activeSelf) _kickPane.transform.SetSiblingIndex(i++);
            if (_liveFooter != null && _liveFooter.activeSelf) _liveFooter.transform.SetSiblingIndex(i);
        }

        static void SyncWorld()
        {
            if (_collideChip == null) return;
            bool off = VpbNetPresence.CollisionsOff;
            _collideChip.SetText(off
                ? VPBTranslation.T("net_session.collide.off", "Collide: off")
                : VPBTranslation.T("net_session.collide.on", "Collide: on"));
            _collideChip.SetIcon(off ? "target-off" : "target");
            _collideChip.SetRole(off ? UI.RuleBlocked : UI.ChromePanel,
                off ? UI.RuleBlockedText : UI.TextPrimary);
        }

        static void SyncFooter()
        {
            bool peer = VpbNetPresence.PeerUp;
            bool host = VpbNetPresence.AsHost;
            if (_kickChip != null)
            {
                _kickChip.SetActive(host && peer);
                _kickChip.SetEnabled(host && peer);
            }
            if (_resyncChip != null)
            {
                _resyncChip.SetActive(peer);
                _resyncChip.SetEnabled(peer);
            }
            if (_inviteChip != null)
            {
                bool want = peer && !VpbNetPresence.ScenesMatch && VpbNetPresence.PeerTakesContent;
                _inviteChip.SetActive(want && CanInviteToThisScene(want));
                _inviteChip.SetEnabled(true);
            }
        }

        // Second ask / joiner chip. Timer, not every refresh.
        static bool CanInviteToThisScene(bool wanted)
        {
            if (!wanted)
            {
                _canInvite = false;
                return false;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextInviteProbe) return _canInvite;
            _nextInviteProbe = now + InviteProbeSeconds;

            _canInvite = !string.IsNullOrEmpty(VpbNetPresence.InvitableScenePath);
            return _canInvite;
        }

        static void SyncLockChip(VpbNetUiKit.Chip chip)
        {
            if (chip == null) return;
            bool locked = VpbNetPresence.RoomCodeLocked;
            chip.SetText(locked
                ? VPBTranslation.T("net_session.unlock", "Unlock")
                : VPBTranslation.T("net_session.lock", "Lock"));
            chip.SetIcon(locked ? "lock-open" : "lock");
            chip.SetRole(locked ? UI.AccentBlue : UI.ChromePanel, UI.TextPrimary);
        }

        static void SetPresetChip(VpbNetUiKit.Chip c, bool on)
        {
            if (c == null) return;
            c.SetRole(on ? UI.AccentBlue : UI.ChromePanel, on ? UI.TextPrimary : UI.TextMuted);
        }

        static string PresetHint(byte preset)
        {
            switch (preset)
            {
                case VpbNetRulePreset.LockedDown:
                    return VPBTranslation.T("net_session.preset.locked",
                        "They can only move. Nothing of yours changes. Open Rules to see every line.");
                case VpbNetRulePreset.FullTrust:
                    return VPBTranslation.T("net_session.preset.trust",
                        "They may change anything of yours. This window will not ask.");
                case VpbNetRulePreset.Custom:
                    return VPBTranslation.T("net_session.preset.custom",
                        "Custom rules. Open Rules to read or change them.");
            }
            return VPBTranslation.T("net_session.preset.watch",
                "They share the room and their own look. A change to your body still asks — in its own prompt, not here.");
        }

        static string DisplayRoom(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "—";
            if (VpbNetInviteCode.LooksLikeInvite(raw)) return VpbNetInviteCode.Group(raw);
            string norm = VpbNetRoomCode.Normalize(raw);
            if (norm != null) return VpbNetRoomCode.Group(norm);
            return raw;
        }

        static bool HaveInvite()
        {
            if (!string.IsNullOrEmpty(VpbNetPresence.Invite)) return true;
            return !string.IsNullOrEmpty(VpbNetPresence.Address);
        }

        static void SyncAvatarButtons()
        {
            if (_avatarRow == null) return;

            bool live = ClaimsPossible();
            int revision = VpbNetAvatarRoster.Revision;
            if (revision != _avatarRevision) RebuildAvatarButtons();

            string mine = VpbNetPresence.MyAvatar;
            string pending = VpbNetPresence.PendingAvatar;
            bool suggest = live && VpbNetPresence.SeatPickWanted;

            for (int i = 0; i < _avatarChips.Count; i++)
            {
                VpbNetUiKit.Chip chip = _avatarChips[i];
                if (chip == null) continue;

                int state = SeatStateOf(_avatarUids[i], mine, pending, live, suggest);
                if (_avatarStates[i] == state) continue;
                _avatarStates[i] = state;
                ApplySeatState(chip, _avatarUids[i], state);
            }

            if (_spectateChip != null)
            {
                bool canDrop = live && !string.IsNullOrEmpty(mine);
                if (_spectateChip.Button != null) _spectateChip.Button.interactable = canDrop;
                _spectateChip.SetTone(canDrop ? UI.ChromePanel : UI.ChromeDark,
                    canDrop ? UI.TextPrimary : UI.TextDim);
            }

            SetLine(_rideHint, RideHint(mine, pending));
            if (_rideHint != null) _rideHint.color = RideHintTone();
        }

        static bool ClaimsPossible()
        {
            if (!VpbNetPresence.IsActive) return false;
            if (!VpbNetPresence.PeerUp) return true;
            if (!VpbNetPresence.ScenesMatch) return false;
            return VpbNetPresence.AsHost
                || VpbNetPresence.PeerClaimRule != VpbNetRuleLevel.Blocked;
        }

        static int SeatStateOf(string uid, string mine, string pending, bool live, bool suggest)
        {
            if (!string.IsNullOrEmpty(mine) && string.Equals(uid, mine, StringComparison.Ordinal))
                return SeatMine;
            if (VpbNetPresence.IsPeers(uid)) return SeatTheirs;
            if (!string.IsNullOrEmpty(pending)
                && string.Equals(uid, pending, StringComparison.Ordinal))
                return SeatPending;
            if (!live) return SeatLocked;
            return suggest ? SeatWanted : SeatFree;
        }

        static void ApplySeatState(VpbNetUiKit.Chip chip, string uid, int state)
        {
            bool on = state == SeatFree || state == SeatWanted;
            if (chip.Button != null) chip.Button.interactable = on;

            // Tone after interactable — SetRole greys a non-interactable chip (ridden avatar looked dead).
            switch (state)
            {
                case SeatMine:
                    chip.SetText(Shorten(uid));
                    chip.SetIcon("circle-check");
                    chip.SetTone(UI.AccentGreen, UI.TextPrimary);
                    return;
                case SeatTheirs:
                    chip.SetText(Shorten(uid));
                    chip.SetIcon("user-minus");
                    chip.SetTone(UI.ChromeMid, UI.TextDim);
                    return;
                case SeatPending:
                    chip.SetText(Shorten(uid));
                    chip.SetIcon("clock-play");
                    chip.SetTone(UI.WarnSurface, UI.WarnText);
                    return;
                case SeatWanted:
                    chip.SetText(Shorten(uid));
                    chip.SetIcon("door-enter");
                    chip.SetTone(UI.AccentBlue, UI.TextPrimary);
                    return;
                case SeatFree:
                    chip.SetText(Shorten(uid));
                    chip.SetIcon("user");
                    chip.SetTone(UI.ChromePanel, UI.TextPrimary);
                    return;
            }

            chip.SetText(Shorten(uid));
            chip.SetIcon("user");
            chip.SetTone(UI.ChromeDark, UI.TextDim);
        }

        static string RideHint(string mine, string pending)
        {
            if (!VpbNetPresence.IsActive)
                return VPBTranslation.T("net_session.ride.opening", "Room is still opening.");

            if (VpbNetPresence.PeerUp && !VpbNetPresence.ScenesMatch)
                return VPBTranslation.T("net_session.hint.scene",
                    "Both sides must load the same scene before you can control a Person.");

            if (_avatarChips.Count == 0)
                return VPBTranslation.T("net_session.ride.empty",
                    "Add a Person in VaM, then click it here.");

            if (!string.IsNullOrEmpty(pending))
                return VPBTranslation.T("net_session.ride.waiting",
                    "Asked for that Person. Waiting for them to allow it - the prompt is on their screen.");

            if (VpbNetPresence.AvatarClaimDenied)
            {
                string why = VpbNetPresence.ClaimDenyReason;
                if (!string.IsNullOrEmpty(why)) return why;
                return VPBTranslation.T("net_session.ride.denied",
                    "They did not allow it. Their session rules decide this, not yours; you can ask again in a minute.");
            }

            // Host arbitrates; on a joiner the host's published claim rule is the one that matters.
            if (!VpbNetPresence.AsHost && VpbNetPresence.PeerUp)
            {
                byte rule = VpbNetPresence.PeerClaimRule;
                if (rule == VpbNetRuleLevel.Blocked)
                    return VPBTranslation.T("net_session.ride.peer_blocked",
                        "They do not let a visitor control a Person in their scene. You stay watching until they change that.");
                if (rule == VpbNetRuleLevel.Ask && string.IsNullOrEmpty(mine))
                    return VPBTranslation.T("net_session.ride.peer_ask",
                        "Click a Person to ask for it. They have to allow it on their screen before you take it.");
            }

            if (VpbNetPresence.SeatPickWanted)
                return VpbNetPresence.AsHost && VpbNetPresence.PeerUp
                    ? VPBTranslation.T("net_session.ride.host_pick",
                        "Pick your Person. The other player is seated in whatever is left over.")
                    : VPBTranslation.T("net_session.ride.pick", "Click a Person to control.");

            if (string.IsNullOrEmpty(mine))
                return VPBTranslation.T("net_session.ride.spectating",
                    "You are watching. Click a Person to control one.");

            return null;
        }

        static Color RideHintTone()
        {
            if (VpbNetPresence.PeerUp && !VpbNetPresence.ScenesMatch) return UI.WarnText;
            if (!string.IsNullOrEmpty(VpbNetPresence.PendingAvatar)) return UI.WarnText;
            if (VpbNetPresence.AvatarClaimDenied) return UI.RuleBlockedText;
            if (VpbNetPresence.SeatPickWanted) return UI.TextPrimary;
            return UI.TextDim;
        }

        static void RebuildAvatarButtons()
        {
            _avatarRevision = VpbNetAvatarRoster.Revision;

            for (int i = 0; i < _avatarChips.Count; i++)
            {
                VpbNetUiKit.Chip chip = _avatarChips[i];
                if (chip == null || chip.Go == null) continue;
                try { UnityEngine.Object.Destroy(chip.Go); }
                catch { }
            }
            _avatarChips.Clear();
            _avatarUids.Clear();
            _avatarStates.Clear();

            if (_spectateChip != null && _spectateChip.Go != null)
            {
                try { UnityEngine.Object.Destroy(_spectateChip.Go); }
                catch { }
            }
            _spectateChip = null;
            if (_avatarRow == null) return;

            float s = _scale > 0f ? _scale : 1f;
            int n = VpbNetAvatarRoster.Count;
            if (n > MaxAvatarButtons) n = MaxAvatarButtons;

            for (int i = 0; i < n; i++)
            {
                string uid = VpbNetAvatarRoster.Uid(i);
                if (string.IsNullOrEmpty(uid)) continue;

                string captured = uid;
                VpbNetUiKit.Chip chip = VpbNetUiKit.IconBtn(_avatarRow, "user", Shorten(uid), 0f, s,
                    () => VpbNetPresence.ClaimAvatar(captured));
                _avatarChips.Add(chip);
                _avatarUids.Add(uid);
                _avatarStates.Add(-1);
            }

            _spectateChip = VpbNetUiKit.IconBtn(_avatarRow, "eye",
                VPBTranslation.T("net_session.spectate", "Watch only"), RideCellRef, s, VpbNetPresence.Spectate);
            VpbNetUiKit.FitWrapRow(_avatarRow);
            if (_shell != null && _shell.PanelRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_shell.PanelRT);
        }

        static string Shorten(string uid)
        {
            if (uid.Length <= 16) return uid;
            return uid.Substring(0, 15) + "…";
        }

        internal static void StartHost(string roomText)
        {
            if (VpbNetTransportChoice.BlockedReason() != null) return;
            if (roomText != null) OnHostRoomEnded(roomText);
            MarkFlowSeen();
            VpbNetPresence.Host();
            RefreshNow();
        }

        internal static string StartJoin(string typed)
        {
            if (VpbNetTransportChoice.BlockedReason() != null)
                return VpbNetTransportChoice.BlockedReason();
            bool steam = VpbNetTransportChoice.IsSteam();
            string need = steam
                ? VPBTranslation.T("net_session.join_need_room", "Type their room code first.")
                : VPBTranslation.T("net_session.join_need", "Type their room code, or paste their invite.");

            if (string.IsNullOrEmpty(typed) || typed.Trim().Length == 0)
            {
                if (!VpbNetPresence.PasteJoinFromClipboard()) return need;
            }
            else if (!VpbNetPresence.ApplyJoinText(typed))
            {
                return need;
            }

            if (steam && !VpbNetRoomCode.IsWellFormed(ConfiguredRoom())
                && !VpbNetInviteCode.LooksLikeInvite(ConfiguredRoom()))
                return VpbNetRoomCode.Explain(ConfiguredRoom());

            string refused = VpbNetPresence.JoinBlockedReason();
            if (refused != null) return refused;

            MarkFlowSeen();
            VpbNetPresence.Join();
            return null;
        }

        static void MarkFlowSeen()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetSessionFlowSeen == null) return;
                if (s.NetSessionFlowSeen.Value) return;
                s.NetSessionFlowSeen.Value = true;
                Settings.SaveConfig();
            }
            catch { }
        }

        static void CopyClicked()
        {
            bool steam = VpbNetTransportChoice.IsSteam();
            if (steam)
            {
                if (!VpbNetPresence.CopyRoomCode()) VpbNetPresence.CopyInvite();
            }
            else
            {
                VpbNetPresence.CopyInvite();
            }
            _copiedLive = true;
            RefreshNow();
        }

        static void GenerateClicked()
        {
            VpbNetPresence.ReplaceSelectedHost();
            _copiedLive = false;
            RefreshNow();
        }

        static void ProtectClicked()
        {
            VpbNetPresence.ToggleRoomCodeLock();
            RefreshNow();
        }

        static void CollideClicked()
        {
            VpbNetPresence.ToggleCollisions();
            RefreshNow();
        }

        static void OnHostRoomEnded(string v)
        {
            if (VpbNetPresence.RoomCodeLocked) return;
            string norm = VpbNetRoomCode.Normalize(v);
            if (norm != null)
            {
                VpbNetRoomBookStore.ReplaceSelectedHost(norm);
                return;
            }
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetLanRoomCode != null) s.NetLanRoomCode.Value = v ?? string.Empty;
            }
            catch { }
        }

        static void ToggleSetup()
        {
            _setupOpen = !_setupOpen;
            ApplySetup();
            if (_shell != null && _shell.PanelRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_shell.PanelRT);
        }

        public static void RevealRules()
        {
            _rulesOpen = true;
            _leaveConfirm = false;
            _kickConfirm = false;
            _collapsed = false;
            if (!IsWanted)
            {
                SetWanted(true);
                NotifySettings();
                return;
            }
            if (_shell == null)
            {
                if (!Create()) return;
                _visible = true;
                NotifyChrome();
            }
            ApplyCollapsed();
            RefreshNow();
            NotifySettings();
        }

        public static void HideRules()
        {
            if (!_rulesOpen) return;
            _rulesOpen = false;
            if (_shell != null) RefreshNow();
            NotifySettings();
        }

        static void ApplySetup()
        {
            VpbNetUiKit.Show(_setupBody, _setupOpen);
            if (_setupChip != null)
                _setupChip.SetIcon(_setupOpen ? "chevron-up" : "chevron-down");
        }

        static void SetText(Text t, string s)
        {
            if (t == null || s == null) return;
            if (!string.Equals(t.text, s, StringComparison.Ordinal)) t.text = s;
        }

        static void SetText(Text t, string s, Color c)
        {
            SetText(t, s);
            if (t != null) t.color = c;
        }

        static void SetLine(Text t, string s)
        {
            if (t == null) return;
            bool show = !string.IsNullOrEmpty(s);
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            if (show) SetText(t, s);
        }

        static void ToggleCollapse()
        {
            if (_collapsed) ExpandLive();
            else CollapseLive(InSession());
        }

        static void CollapseLive(bool toast)
        {
            _leaveConfirm = false;
            _kickConfirm = false;
            if (_rulesOpen)
            {
                _rulesOpen = false;
                NotifySettings();
            }
            if (!_collapsed)
            {
                _collapsed = true;
                ApplyCollapsed();
            }
            if (toast) ShowHudToast();
            RefreshNow();
        }

        static void ExpandLive()
        {
            _collapsed = false;
            ApplyCollapsed();
            _havePrev = false;
            _nextRefresh = 0f;
            Refresh();
        }

        static void ShowHudToast()
        {
            if (_hudToastShown) return;
            _hudToastShown = true;
            _hudToastUntil = Time.realtimeSinceStartup + HudToastSeconds;
        }

        static void SyncHudChrome()
        {
            bool live = InSession();
            if (_closeChip != null) _closeChip.SetActive(!live);

            bool wait = live && _collapsed && VpbNetPresence.AsHost && !VpbNetPresence.PeerUp;
            bool steam = VpbNetTransportChoice.IsSteam();
            string cfg = ConfiguredRoom();
            bool have = steam
                ? VpbNetRoomCode.IsWellFormed(cfg) || HaveInvite()
                : HaveInvite();
            if (_hudCopyChip != null)
            {
                _hudCopyChip.SetActive(wait && have);
                if (wait && have)
                {
                    _hudCopyChip.SetText(VPBTranslation.T("net_session.copy_short", "Copy"));
                    _hudCopyChip.SetRole(!_copiedLive ? UI.AccentGreen : UI.ChromePanel, UI.TextPrimary);
                }
            }
        }

        static void ApplyCollapsed()
        {
            if (_shell != null && _shell.Body != null) _shell.Body.SetActive(!_collapsed);
            if (_collapseChip != null)
                _collapseChip.SetIcon(_collapsed ? "chevron-down" : "chevron-up");
        }

        static void CloseFromButton()
        {
            SetWanted(false);
        }

        static void SavePosition()
        {
            if (_shell == null || _shell.PanelRT == null) return;
            try
            {
                Settings s = Settings.Instance;
                if (s == null) return;
                Vector2 p = _shell.PanelRT.anchoredPosition;
                if (s.NetSessionUiX != null) s.NetSessionUiX.Value = p.x;
                if (s.NetSessionUiY != null) s.NetSessionUiY.Value = p.y;
            }
            catch { }
        }

        static float PersistedX()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetSessionUiX != null) return s.NetSessionUiX.Value;
            }
            catch { }
            return DefaultX;
        }

        static float PersistedY()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetSessionUiY != null) return s.NetSessionUiY.Value;
            }
            catch { }
            return DefaultY;
        }

        static void NotifyChrome()
        {
            try { GalleryPanel.RefreshNetSessionChrome(); }
            catch { }
        }

        static void NotifySettings()
        {
            NotifyChrome();
        }

        public static void Destroy()
        {
            VpbNetRulesUi.Detach();
            if (_shell != null)
            {
                GameObject root = _shell.Root;
                VpbNetUiKit.Destroy(ref root);
            }

            VpbNetSteamFlow.Destroy();
            _avatarChips.Clear();
            _avatarUids.Clear();
            _avatarStates.Clear();
            _shell = null;
            _collapseChip = null;
            _closeChip = null;
            _hudCopyChip = null;
            _rulesChip = null;
            _rulesPane = null;
            _headDot = null;
            _headCount = null;
            _statusFill = null;
            _statusDot = null;
            _statusLine = null;
            _hintLine = null;
            _idlePane = null;
            _livePane = null;
            _gatesPane = null;
            _gateScene = null;
            _gateMine = null;
            _gateTheirs = null;
            _gateCap = null;
            _setupBody = null;
            _setupChip = null;
            _setupSummary = null;
            _setupRulesHint = null;
            _presetWatchChip = null;
            _presetCustomChip = null;
            _transportDirect = null;
            _transportSteam = null;
            _transportHint = null;
            _steamGate = null;
            _steamGateText = null;
            _steamAckChip = null;
            _steamRepairChip = null;
            _steamDirectChip = null;
            _directGate = null;
            _directGateText = null;
            _youDot = null;
            _youName = null;
            _youRide = null;
            _themDot = null;
            _themName = null;
            _themRide = null;
            _peoplePane = null;
            _ridePane = null;
            _roomPane = null;
            _worldPane = null;
            _peerPane = null;
            _liveFooter = null;
            _leavePane = null;
            _kickPane = null;
            _moreChip = null;
            _avatarRow = null;
            _spectateChip = null;
            _rideHint = null;
            _liveRoomText = null;
            _liveRoomHint = null;
            _copyChip = null;
            _liveNewChip = null;
            _liveLockChip = null;
            _collideChip = null;
            _kickChip = null;
            _resyncChip = null;
            _inviteChip = null;
            _nextInviteProbe = 0f;
            _canInvite = false;
            _avatarRevision = -1;
            _visible = false;
            _havePrev = false;
            _inSession = false;
            _leaveConfirm = false;
            _kickConfirm = false;
            _moreOpen = false;
            _rulesOpen = false;
            _hudToastShown = false;
            _hudToastUntil = 0f;
            _copiedLive = false;
        }
    }
}
