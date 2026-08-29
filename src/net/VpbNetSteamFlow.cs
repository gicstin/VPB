using System;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    // Host/join wizard. One role at a time — primary action is the only one on the card.
    public static class VpbNetSteamFlow
    {
        public const int StepChoose = 0;
        public const int StepHost = 1;
        public const int StepJoin = 2;

        const float BackRef = 88f;
        const float NewRef = 80f;
        const float PasteRef = 88f;
        const float ForgetRef = 88f;
        const float LockRef = 104f;

        sealed class BookRow
        {
            public int Index;
            public bool Host;
            public GameObject Go;
            public VpbNetUiKit.Chip Pick;
            public VpbNetUiKit.Chip Forget;

            public void PickClicked()
            {
                if (Host) PickHost(Index);
                else PickJoin(Index);
            }

            public void ForgetClicked()
            {
                AskForget(Host, Index);
            }
        }

        static GameObject _pane;
        static GameObject _chooseCard;
        static GameObject _hostCard;
        static GameObject _joinCard;
        static GameObject _hostSteamPane;
        static GameObject _hostDirectPane;
        static GameObject _hostBookPane;
        static GameObject _joinBookPane;

        static Text _chooseTitle;
        static Text _chooseIntro;
        static Text _chooseHint;

        static Text _host1;
        static Text _host2;
        static Text _host3;
        static Text _hostDirect1;
        static Text _hostDirect2;
        static Text _hostTitle;
        static Text _hostBookHead;
        static Text _codeText;
        static Text _hostNote;
        static Text _hostDirectIntro;
        static VpbNetUiKit.Chip _newChip;
        static VpbNetUiKit.Chip _lockChip;
        static VpbNetUiKit.Chip _copyChip;
        static VpbNetUiKit.Chip _openChip;
        static Text _hostGate;

        static Text _joinTitle;
        static Text _joinBookHead;
        static Text _join1;
        static Text _join2;
        static Text _join3;
        static InputField _joinField;
        static VpbNetUiKit.Chip _pasteChip;
        static VpbNetUiKit.Chip _joinChip;
        static Text _joinErr;

        static readonly BookRow[] _hostRows = new BookRow[VpbNetRoomBook.Cap];
        static readonly BookRow[] _joinRows = new BookRow[VpbNetRoomBook.Cap];

        static int _step;
        static bool _copied;
        static string _draft;
        static string _error;
        static int _joinPick;
        static int _forgetIndex = -1;
        static bool _forgetHost;

        public static int Step { get { return _step; } }

        public static bool JoinFocused()
        {
            return _joinField != null && _joinField.isFocused;
        }

        public static bool OnEscape()
        {
            if (_forgetIndex >= 0)
            {
                ClearForget();
                VpbNetSessionUi.RefreshNow();
                return true;
            }
            if (_step != StepHost && _step != StepJoin) return false;
            ChooseBack();
            return true;
        }

        public static bool OnSubmit()
        {
            if (_step == StepJoin)
            {
                JoinClicked();
                return true;
            }
            if (_step == StepHost)
            {
                OpenClicked();
                return true;
            }
            return false;
        }

        public static bool OnArrow(bool up)
        {
            if (_step == StepJoin)
            {
                int n = VpbNetRoomBookStore.JoinCount;
                if (n <= 0) return false;
                int next = _joinPick + (up ? -1 : 1);
                if (next < 0) next = n - 1;
                if (next >= n) next = 0;
                PickJoin(next);
                return true;
            }
            if (_step == StepHost)
            {
                int n = VpbNetRoomBookStore.HostCount;
                if (n <= 1) return false;
                int cur = VpbNetRoomBookStore.SelectedHostIndex;
                int next = cur + (up ? -1 : 1);
                if (next < 0) next = n - 1;
                if (next >= n) next = 0;
                PickHost(next);
                return true;
            }
            return false;
        }

        public static int Snapshot()
        {
            int h = _step * 397;
            if (_copied) h ^= 0x5F5F;
            h = h * 31 + Hash(_draft);
            h = h * 31 + Hash(_error);
            h = h * 31 + _joinPick;
            h = h * 31 + VpbNetRoomBookStore.Snapshot();
            h = h * 31 + _forgetIndex;
            if (_forgetHost) h ^= 0x1111;
            return h;
        }

        static int Hash(string s)
        {
            if (s == null) return 0;
            int h = s.Length;
            for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
            return h;
        }

        public static void ResetToChoose()
        {
            _step = StepChoose;
            _copied = false;
            _error = null;
            ClearForget();
        }

        public static void Build(GameObject parent, float s)
        {
            _pane = VpbNetUiKit.Pane(parent, "SteamFlow", s);
            BuildChoose(s);
            BuildHost(s);
            BuildJoin(s);
        }

        static void BuildChoose(float s)
        {
            _chooseCard = VpbNetUiKit.Card(_pane, s);

            _chooseTitle = VpbNetUiKit.Line(_chooseCard, string.Empty,
                VpbNetUiKit.FontBody, UI.TextPrimary, VpbNetUiKit.LineRef, s, true);
            _chooseIntro = VpbNetUiKit.Line(_chooseCard, string.Empty,
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, s, true);

            GameObject row = VpbNetUiKit.Row(_chooseCard, VpbNetUiKit.ButtonRef, s);
            VpbNetUiKit.IconBtn(row, "login-2", "user",
                VPBTranslation.T("net_steam.pick_join", "I have a code"), 0f, s, ChooseJoin);
            VpbNetUiKit.IconBtn(row, "player-play",
                VPBTranslation.T("net_steam.pick_host", "I'll make a room"), 0f, s, ChooseHost);

            _chooseHint = VpbNetUiKit.Line(_chooseCard, string.Empty,
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, s, true);
        }

        static void BuildHost(float s)
        {
            _hostCard = VpbNetUiKit.Card(_pane, s);

            GameObject head = VpbNetUiKit.Row(_hostCard, VpbNetUiKit.ButtonRef, s);
            _hostTitle = VpbNetUiKit.Line(head, string.Empty,
                VpbNetUiKit.FontBody, UI.TextPrimary, VpbNetUiKit.LineRef, s, false);
            VpbNetUiKit.IconBtn(head, "arrow-left",
                VPBTranslation.T("net_steam.back", "Back"), BackRef, s, ChooseBack);

            _hostBookPane = VpbNetUiKit.Pane(_hostCard, "HostBook", s);
            _hostBookHead = VpbNetUiKit.Line(_hostBookPane,
                VPBTranslation.T("net_steam.host_rooms", "Your rooms"),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            for (int i = 0; i < _hostRows.Length; i++)
            {
                BookRow row = new BookRow();
                row.Index = i;
                row.Host = true;
                row.Go = VpbNetUiKit.Row(_hostBookPane, VpbNetUiKit.ButtonRef, s);
                row.Pick = VpbNetUiKit.IconBtn(row.Go, "player-play",
                    string.Empty, 0f, s, row.PickClicked);
                row.Forget = VpbNetUiKit.IconBtn(row.Go, "x",
                    VPBTranslation.T("net_steam.forget", "Forget"), ForgetRef, s, row.ForgetClicked);
                _hostRows[i] = row;
            }

            _hostSteamPane = VpbNetUiKit.Pane(_hostCard, "HostSteam", s);
            _host1 = VpbNetUiKit.Line(_hostSteamPane,
                VPBTranslation.T("net_steam.host_1", "1.  This is your room code."),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            GameObject codeRow = VpbNetUiKit.Row(_hostSteamPane, VpbNetUiKit.ButtonRef, s);
            _codeText = VpbNetUiKit.Line(codeRow, "—", VpbNetUiKit.FontDisplay,
                UI.TextPrimary, VpbNetUiKit.ButtonRef, s, false);
            _newChip = VpbNetUiKit.IconBtn(codeRow, "refresh",
                VPBTranslation.T("net_steam.new", "New"), NewRef, s, NewClicked);
            _lockChip = VpbNetUiKit.IconBtn(codeRow, "lock",
                VPBTranslation.T("net_session.lock", "Lock"), LockRef, s, LockClicked);

            _host2 = VpbNetUiKit.Line(_hostSteamPane,
                VPBTranslation.T("net_steam.host_2",
                    "2.  Send it to your friend — chat, voice, however you talk to them."),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            GameObject copyRow = VpbNetUiKit.Row(_hostSteamPane, VpbNetUiKit.ButtonRef, s);
            _copyChip = VpbNetUiKit.IconBtn(copyRow, "clipboard-copy",
                VPBTranslation.T("net_steam.copy", "Copy code"), 0f, s, CopyClicked);
            _hostNote = VpbNetUiKit.Line(_hostSteamPane, string.Empty,
                VpbNetUiKit.FontCaption, UI.RuleAllowedText, VpbNetUiKit.LineRef, s, true);

            _host3 = VpbNetUiKit.Line(_hostSteamPane,
                VPBTranslation.T("net_steam.host_3",
                    "3.  Press Open room, then wait. They press I have a code, pick a recent or paste it, then Join — you do nothing else."),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);

            _hostDirectPane = VpbNetUiKit.Pane(_hostCard, "HostDirect", s);
            _hostDirectIntro = VpbNetUiKit.Line(_hostDirectPane, string.Empty,
                VpbNetUiKit.FontCaption, UI.WarnText, VpbNetUiKit.LineRef, s, true);
            _hostDirect1 = VpbNetUiKit.Line(_hostDirectPane,
                VPBTranslation.T("net_steam.host_direct_1",
                    "1.  Press Open room."),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            _hostDirect2 = VpbNetUiKit.Line(_hostDirectPane,
                VPBTranslation.T("net_steam.host_direct_2",
                    "2.  Copy the invite that appears and send it. They paste it and press Join. You both see IP addresses."),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);

            GameObject act = VpbNetUiKit.Row(_hostCard, VpbNetUiKit.ButtonRef, s);
            _openChip = VpbNetUiKit.PrimaryIconBtn(act, "player-play",
                VPBTranslation.T("net_steam.open", "Open room"), 0f, s, OpenClicked);
            _hostGate = VpbNetUiKit.Line(_hostCard, string.Empty,
                VpbNetUiKit.FontCaption, UI.WarnText, VpbNetUiKit.LineRef, s, true);
        }

        static void BuildJoin(float s)
        {
            _joinCard = VpbNetUiKit.Card(_pane, s);

            GameObject head = VpbNetUiKit.Row(_joinCard, VpbNetUiKit.ButtonRef, s);
            _joinTitle = VpbNetUiKit.Line(head, string.Empty,
                VpbNetUiKit.FontBody, UI.TextPrimary, VpbNetUiKit.LineRef, s, false);
            VpbNetUiKit.IconBtn(head, "arrow-left",
                VPBTranslation.T("net_steam.back", "Back"), BackRef, s, ChooseBack);

            _joinBookPane = VpbNetUiKit.Pane(_joinCard, "JoinBook", s);
            _joinBookHead = VpbNetUiKit.Line(_joinBookPane,
                VPBTranslation.T("net_steam.join_recent", "Recent"),
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            for (int i = 0; i < _joinRows.Length; i++)
            {
                BookRow row = new BookRow();
                row.Index = i;
                row.Host = false;
                row.Go = VpbNetUiKit.Row(_joinBookPane, VpbNetUiKit.ButtonRef, s);
                row.Pick = VpbNetUiKit.IconBtn(row.Go, "login-2", "user",
                    string.Empty, 0f, s, row.PickClicked);
                row.Forget = VpbNetUiKit.IconBtn(row.Go, "x",
                    VPBTranslation.T("net_steam.forget", "Forget"), ForgetRef, s, row.ForgetClicked);
                _joinRows[i] = row;
            }

            _join1 = VpbNetUiKit.Line(_joinCard, string.Empty,
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            _join2 = VpbNetUiKit.Line(_joinCard, string.Empty,
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            GameObject fieldRow = VpbNetUiKit.Row(_joinCard, VpbNetUiKit.ButtonRef, s);
            _joinField = VpbNetUiKit.Field(fieldRow,
                VPBTranslation.T("net_steam.code_ph", "Room code"), VpbNetUiKit.ButtonRef, s);
            _joinField.onEndEdit.AddListener(OnDraftEnded);
            _pasteChip = VpbNetUiKit.IconBtn(fieldRow, "clipboard-list",
                VPBTranslation.T("net_steam.paste", "Paste"), PasteRef, s, PasteClicked);
            _joinErr = VpbNetUiKit.Line(_joinCard, string.Empty,
                VpbNetUiKit.FontCaption, UI.WarnText, VpbNetUiKit.LineRef * 3f, s, true);

            _join3 = VpbNetUiKit.Line(_joinCard, string.Empty,
                VpbNetUiKit.FontCaption, UI.TextMuted, VpbNetUiKit.LineRef, s, true);
            GameObject act = VpbNetUiKit.Row(_joinCard, VpbNetUiKit.ButtonRef, s);
            _joinChip = VpbNetUiKit.PrimaryIconBtn(act, "login-2", "user",
                VPBTranslation.T("net_steam.join", "Join"), 0f, s, JoinClicked);
        }

        public static void Show(bool on)
        {
            VpbNetUiKit.Show(_pane, on);
        }

        public static void Sync(bool ready)
        {
            if (_pane == null) return;

            bool steam = VpbNetTransportChoice.IsSteam();
            SyncChoose(steam);
            SyncHostCard(steam, ready);
            SyncJoinCard(steam, ready);

            VpbNetUiKit.Show(_chooseCard, _step == StepChoose);
            VpbNetUiKit.Show(_hostCard, _step == StepHost);
            VpbNetUiKit.Show(_joinCard, _step == StepJoin);
        }

        static void SyncChoose(bool steam)
        {
            if (steam)
            {
                SetText(_chooseTitle, VPBTranslation.T("net_steam.title", "Play with a friend over Steam"));
                SetText(_chooseIntro, VPBTranslation.T("net_steam.intro",
                    "Steam carries it. Both of you sign into Steam. No address, no port forwarding."),
                    UI.TextDim);
                SetText(_chooseHint, VPBTranslation.T("net_steam.pick_hint",
                    "One of you makes a room and sends the code. The other pastes it. Either side can make the room."),
                    UI.TextDim);
            }
            else
            {
                SetText(_chooseTitle, VPBTranslation.T("net_steam.title_direct", "Play with a friend over Direct P2P"));
                SetText(_chooseIntro, VPBTranslation.T("net_steam.intro_direct",
                    "Each of you sees the other's IP address. Use Steam if you do not want that."),
                    UI.WarnText);
                SetText(_chooseHint, VPBTranslation.T("net_steam.pick_hint_direct",
                    "One of you makes a room and sends the invite. The other pastes it. You both see IP addresses."),
                    UI.TextDim);
            }
        }

        static void SyncHostCard(bool steam, bool ready)
        {
            VpbNetUiKit.Show(_hostSteamPane, steam);
            VpbNetUiKit.Show(_hostDirectPane, !steam);

            if (steam)
            {
                SetText(_hostTitle, VPBTranslation.T("net_steam.host_title", "Hosting over Steam"));
            }
            else
            {
                SetText(_hostTitle, VPBTranslation.T("net_steam.host_title_direct", "Hosting over Direct P2P"));
                SetText(_hostDirectIntro, VPBTranslation.T("net_session.route.direct_hint",
                    "No privacy. Each of you sees the other's IP address. There is no relay."),
                    UI.WarnText);
            }

            int hosts = VpbNetRoomBookStore.HostCount;
            VpbNetUiKit.Show(_hostBookPane, _step == StepHost && hosts > 1);
            int selected = VpbNetRoomBookStore.SelectedHostIndex;
            for (int i = 0; i < _hostRows.Length; i++)
            {
                BookRow row = _hostRows[i];
                if (row == null || row.Go == null) continue;
                bool on = i < hosts;
                VpbNetUiKit.Show(row.Go, on);
                if (!on) continue;
                row.Pick.SetText(VpbNetRoomBookStore.HostLabel(i));
                row.Pick.SetRole(i == selected ? UI.AccentBlue : UI.ChromePanel, UI.TextPrimary);
                VpbNetRoomBookEntry e = VpbNetRoomBookStore.HostAt(i);
                row.Pick.SetIcon(e != null && e.Locked ? "lock" : "player-play");
                SyncForgetChip(row, true, i);
            }

            if (_step != StepHost) return;

            string norm = VpbNetRoomCode.Normalize(VpbNetRoomBookStore.SelectedHostCode);
            bool have = norm != null;
            SetText(_codeText, have ? VpbNetRoomCode.Group(norm) : "—");

            bool locked = VpbNetPresence.RoomCodeLocked;
            if (_newChip != null) _newChip.SetEnabled(true);
            if (_lockChip != null)
            {
                _lockChip.SetEnabled(have);
                _lockChip.SetText(locked
                    ? VPBTranslation.T("net_session.unlock", "Unlock")
                    : VPBTranslation.T("net_session.lock", "Lock"));
                _lockChip.SetIcon(locked ? "lock-open" : "lock");
                _lockChip.SetRole(locked ? UI.AccentBlue : UI.ChromePanel, UI.TextPrimary);
            }
            if (_copyChip != null)
            {
                _copyChip.SetEnabled(have);
            }
            if (_openChip != null)
            {
                _openChip.SetEnabled(ready && have);
            }
            SetLine(_hostGate, have ? null : VPBTranslation.T("net_steam.host_nocode",
                "There is no room code on this machine, so there is nothing to open. Press Back, then I'll make a room."));

            SetLine(_hostNote, _copied
                ? VPBTranslation.T("net_steam.copied",
                    "Copied. Paste it to your friend, then press Open room.")
                : null);

            bool steps = !KnowsFlow();
            SetLine(_host1, steps ? VPBTranslation.T("net_steam.host_1", "1.  This is your room code.") : null);
            SetLine(_host2, steps
                ? VPBTranslation.T("net_steam.host_2",
                    "2.  Send it to your friend — chat, voice, however you talk to them.")
                : null);
            SetLine(_host3, steps
                ? VPBTranslation.T("net_steam.host_3",
                    "3.  Press Open room, then wait. They press I have a code, pick a recent or paste it, then Join — you do nothing else.")
                : null);
            SetLine(_hostDirect1, steps
                ? VPBTranslation.T("net_steam.host_direct_1", "1.  Press Open room.")
                : null);
            SetLine(_hostDirect2, steps
                ? VPBTranslation.T("net_steam.host_direct_2",
                    "2.  Copy the invite that appears and send it. They paste it and press Join. You both see IP addresses.")
                : null);
        }

        static void SyncJoinCard(bool steam, bool ready)
        {
            int recents = VpbNetRoomBookStore.JoinCount;
            if (steam)
            {
                SetText(_joinTitle, VPBTranslation.T("net_steam.join_title", "Joining over Steam"));
                SetText(_join1, recents > 0
                    ? VPBTranslation.T("net_steam.join_1_recent",
                        "1.  Pick a recent, or get a new 12-character room code from whoever is hosting.")
                    : VPBTranslation.T("net_steam.join_1",
                        "1.  Get the 12-character room code from whoever is hosting."));
                SetText(_join2, VPBTranslation.T("net_steam.join_2",
                    "2.  Type or paste it here. Case and hyphens do not matter."));
                SetText(_join3, VPBTranslation.T("net_steam.join_3",
                    "3.  Press Join. They have to have pressed Open room first, or there is nothing to find."));
                SetPlaceholder(_joinField, VPBTranslation.T("net_steam.code_ph", "Room code"));
            }
            else
            {
                SetText(_joinTitle, VPBTranslation.T("net_steam.join_title_direct", "Joining over Direct P2P"));
                SetText(_join1, recents > 0
                    ? VPBTranslation.T("net_steam.join_direct_1_recent",
                        "1.  Pick a recent, or get a new invite from whoever is hosting.")
                    : VPBTranslation.T("net_steam.join_direct_1",
                        "1.  Get the invite from whoever is hosting."));
                SetText(_join2, VPBTranslation.T("net_steam.join_direct_2",
                    "2.  Paste it here. You will see their IP address."));
                SetText(_join3, VPBTranslation.T("net_steam.join_direct_3",
                    "3.  Press Join. They have to have pressed Open room first."));
                SetPlaceholder(_joinField, VPBTranslation.T("net_session.invite_ph", "Invite or room code"));
            }

            VpbNetUiKit.Show(_joinBookPane, _step == StepJoin && recents > 0);
            if (_joinPick >= recents) _joinPick = 0;
            for (int i = 0; i < _joinRows.Length; i++)
            {
                BookRow row = _joinRows[i];
                if (row == null || row.Go == null) continue;
                bool on = i < recents;
                VpbNetUiKit.Show(row.Go, on);
                if (!on) continue;
                row.Pick.SetText(VpbNetRoomBookStore.JoinLabel(i));
                row.Pick.SetRole(i == _joinPick ? UI.AccentBlue : UI.ChromePanel, UI.TextPrimary);
                SyncForgetChip(row, false, i);
            }

            if (_step != StepJoin) return;

            if (_joinField != null && !_joinField.isFocused)
                VpbNetUiKit.SetField(_joinField, _draft ?? string.Empty);
            if (_pasteChip != null) _pasteChip.SetEnabled(ready);
            if (_joinChip != null) _joinChip.SetEnabled(ready && !VpbNetAlertUi.IsOpen);
            SetLine(_joinErr, _error);

            bool steps = !KnowsFlow();
            if (_join1 != null) VpbNetUiKit.Show(_join1.gameObject, steps);
            if (_join2 != null) VpbNetUiKit.Show(_join2.gameObject, steps);
            if (_join3 != null) VpbNetUiKit.Show(_join3.gameObject, steps);
        }

        static void ChooseHost()
        {
            VpbNetPresence.EnsureRoomCode();
            _step = StepHost;
            _copied = false;
            _error = null;
            ClearForget();
            VpbNetSessionUi.RefreshNow();
        }

        static void ChooseJoin()
        {
            _step = StepJoin;
            _error = null;
            ClearForget();
            if (string.IsNullOrEmpty(_draft))
            {
                string last = VpbNetRoomBookStore.LastJoinRaw;
                if (!string.IsNullOrEmpty(last)) _draft = last;
            }
            _joinPick = 0;
            VpbNetSessionUi.RefreshNow();
        }

        static void ChooseBack()
        {
            _step = StepChoose;
            _error = null;
            ClearForget();
            VpbNetSessionUi.RefreshNow();
        }

        static void NewClicked()
        {
            VpbNetPresence.AddHostRoom();
            _copied = false;
            VpbNetSessionUi.RefreshNow();
        }

        static void LockClicked()
        {
            VpbNetPresence.ToggleRoomCodeLock();
            VpbNetSessionUi.RefreshNow();
        }

        static void CopyClicked()
        {
            if (!VpbNetPresence.CopyRoomCode()) return;
            _copied = true;
            VpbNetSessionUi.RefreshNow();
        }

        static void OpenClicked()
        {
            VpbNetSessionUi.StartHost(null);
        }

        static void PasteClicked()
        {
            string clip = null;
            try { clip = GUIUtility.systemCopyBuffer; }
            catch { }
            if (string.IsNullOrEmpty(clip) || clip.Trim().Length == 0)
            {
                _error = VpbNetTransportChoice.IsSteam()
                    ? VPBTranslation.T("net_steam.paste_empty",
                        "Nothing on the clipboard. Have them send the code, copy it, then press Paste.")
                    : VPBTranslation.T("net_session.paste_empty",
                        "Clipboard is empty. Copy their invite, then Paste.");
                VpbNetSessionUi.RefreshNow();
                return;
            }
            _draft = clip.Trim();
            _error = null;
            VpbNetUiKit.SetField(_joinField, _draft);
            VpbNetSessionUi.RefreshNow();
        }

        public static void RememberJoinError(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _error = text;
            _step = StepJoin;
        }

        static void JoinClicked()
        {
            if (VpbNetAlertUi.IsOpen) return;
            string typed = _joinField != null ? _joinField.text : _draft;
            _error = VpbNetSessionUi.StartJoin(typed);
            VpbNetSessionUi.RefreshNow();
        }

        static void OnDraftEnded(string v)
        {
            _draft = v == null ? string.Empty : v.Trim();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                JoinClicked();
        }

        static void PickHost(int index)
        {
            VpbNetRoomBookStore.SelectHost(index);
            _copied = false;
            ClearForget();
            VpbNetSessionUi.RefreshNow();
        }

        static void PickJoin(int index)
        {
            string token = VpbNetRoomBookStore.JoinToken(index);
            if (string.IsNullOrEmpty(token)) return;
            _joinPick = index;
            _draft = token;
            _error = null;
            ClearForget();
            VpbNetUiKit.SetField(_joinField, _draft);
            VpbNetSessionUi.RefreshNow();
        }

        static void AskForget(bool host, int index)
        {
            if (_forgetIndex == index && _forgetHost == host)
            {
                if (host) ForgetHost(index);
                else ForgetJoin(index);
                ClearForget();
                VpbNetSessionUi.RefreshNow();
                return;
            }
            _forgetHost = host;
            _forgetIndex = index;
            VpbNetSessionUi.RefreshNow();
        }

        static void ClearForget()
        {
            _forgetIndex = -1;
        }

        static void SyncForgetChip(BookRow row, bool host, int index)
        {
            if (row == null || row.Forget == null) return;
            bool sure = _forgetIndex == index && _forgetHost == host;
            row.Forget.SetText(sure
                ? VPBTranslation.T("net_steam.forget_sure", "Sure?")
                : VPBTranslation.T("net_steam.forget", "Forget"));
            row.Forget.SetRole(sure ? UI.AccentRed : UI.ChromePanel, UI.TextPrimary);
        }

        static bool KnowsFlow()
        {
            try
            {
                Settings s = Settings.Instance;
                return s != null && s.NetSessionFlowSeen != null && s.NetSessionFlowSeen.Value;
            }
            catch { return false; }
        }

        static void ForgetHost(int index)
        {
            VpbNetRoomBookStore.ForgetHost(index);
            _copied = false;
            VpbNetSessionUi.RefreshNow();
        }

        static void ForgetJoin(int index)
        {
            VpbNetRoomBookStore.ForgetJoin(index);
            if (_joinPick >= VpbNetRoomBookStore.JoinCount) _joinPick = 0;
            VpbNetSessionUi.RefreshNow();
        }

        static void SetPlaceholder(InputField field, string text)
        {
            if (field == null || text == null) return;
            Text t = field.placeholder as Text;
            if (t == null) return;
            if (!string.Equals(t.text, text, StringComparison.Ordinal)) t.text = text;
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

        public static void Destroy()
        {
            _pane = null;
            _chooseCard = null;
            _hostCard = null;
            _joinCard = null;
            _hostSteamPane = null;
            _hostDirectPane = null;
            _hostBookPane = null;
            _joinBookPane = null;
            _chooseTitle = null;
            _chooseIntro = null;
            _chooseHint = null;
            _hostTitle = null;
            _host1 = null;
            _host2 = null;
            _host3 = null;
            _hostDirect1 = null;
            _hostDirect2 = null;
            _hostBookHead = null;
            _codeText = null;
            _hostNote = null;
            _hostDirectIntro = null;
            _newChip = null;
            _lockChip = null;
            _copyChip = null;
            _openChip = null;
            _hostGate = null;
            _joinTitle = null;
            _joinBookHead = null;
            _join1 = null;
            _join2 = null;
            _join3 = null;
            _joinField = null;
            _pasteChip = null;
            _joinChip = null;
            _joinErr = null;
            _forgetIndex = -1;
            for (int i = 0; i < _hostRows.Length; i++) _hostRows[i] = null;
            for (int i = 0; i < _joinRows.Length; i++) _joinRows[i] = null;
        }
    }
}
