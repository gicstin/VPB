using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    // Identical you-then-them rows on both machines.
    public static class VpbNetContentUi
    {
        const string HostName = "VPB_NetContent";
        const float WidthRef = 640f;
        const float DefaultY = -72f;
        const int SortingOrder = 1007;
        const float RefreshSeconds = 0.2f;
        const float BarRef = 10f;
        const float NameColRef = 96f;
        const float SilentPeerSeconds = 12f;

        static readonly StringBuilder _sb = new StringBuilder(256);

        static VpbNetUiKit.Shell _shell;
        static Text _scene;
        static Text _from;
        static Image _headlineFill;
        static Image _headlineDot;
        static Text _headline;
        static Text _mineName;
        static Text _mineText;
        static Text _peerName;
        static Text _peerText;
        static Text _note;
        static VpbNetUiKit.Bar _mineBar;
        static VpbNetUiKit.Bar _peerBar;
        static VpbNetUiKit.Chip _accept;
        static VpbNetUiKit.Chip _anyway;
        static VpbNetUiKit.Chip _retry;
        static VpbNetUiKit.Chip _stop;
        static VpbNetUiKit.Chip _decline;
        static VpbNetUiKit.Chip _again;
        static VpbNetUiKit.Chip _done;

        static float _scale = 1f;
        static float _nextRefresh;
        static bool _rescaleLock;
        static uint _shownOffer;
        static float _mineLoadAt;
        static float _peerLoadAt;
        static float _peerFilesAt;
        static bool _peerSawLoading;

        public static bool IsOpen { get { return _shell != null; } }

        public static void Poll()
        {
            if (!Wanted())
            {
                if (_shell != null) Destroy();
                return;
            }
            if (_shell != null && _shownOffer != VpbNetContentSync.Offer.OfferId) Destroy();
            if (_shell == null) Create();
        }

        public static void Tick()
        {
            if (VpbNetUiKit.Lost(_shell)) Destroy();
            if (_shell == null) return;
            RescaleIfNeeded();
            if (_shell == null) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshSeconds;
            Refresh();
        }

        public static void RescaleIfNeeded()
        {
            if (_shell == null || _rescaleLock) return;
            if (!VpbNetUiKit.ScaleDrifted(_scale)) return;
            _rescaleLock = true;
            try
            {
                Destroy();
                Create();
            }
            finally { _rescaleLock = false; }
        }

        // Ready-to-start = both report the offered scene, not "same as each other".
        static bool Wanted()
        {
            try
            {
                if (!VpbNetRuntime.IsEnabled) return false;
                if (!VpbNetPresence.IsActive) return false;
                if (!VpbNetContentSync.HasOffer) return false;
                if (VpbNetContentSync.Dismissed) return false;
                return true;
            }
            catch { return false; }
        }

        static bool InOfferedScene(string sceneUid)
        {
            if (string.IsNullOrEmpty(sceneUid) || !VpbNetContentSync.HasOffer) return false;
            return VpbNetContentContract.SameSceneRef(sceneUid, VpbNetContentSync.Offer.ScenePath);
        }

        static bool Create()
        {
            try
            {
                _scale = VpbNetUiKit.Scale();
                float s = _scale;
                _shownOffer = VpbNetContentSync.Offer.OfferId;

                _shell = VpbNetUiKit.BuildWindow(HostName, TitleFor(VpbNetContentSync.Mine, false),
                    WidthRef, s, SortingOrder, new Vector2(0f, DefaultY), null);

                RectTransform rt = _shell.PanelRT;
                if (rt != null && !_shell.WorldSpace)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, DefaultY * s);
                }

                VpbNetUiKit.TitleChip(_shell, "X", s, CloseClicked);

                _scene = VpbNetUiKit.Line(_shell.Body, string.Empty, VpbNetUiKit.FontTitle,
                    UI.TextPrimary, VpbNetUiKit.LineRef * 1.2f, s, false);
                _from = VpbNetUiKit.Line(_shell.Body, string.Empty, VpbNetUiKit.FontCaption,
                    UI.TextDim, VpbNetUiKit.LineRef, s, false);

                VpbNetUiKit.Spacer(_shell.Body, VpbNetUiKit.GapRef, s);
                _headline = VpbNetUiKit.StatusRow(_shell.Body, s, out _headlineFill, out _headlineDot);

                VpbNetUiKit.Spacer(_shell.Body, VpbNetUiKit.GapRef, s);

                BuildSide(_shell.Body, s, out _mineName, out _mineBar, out _mineText);
                VpbNetUiKit.Spacer(_shell.Body, VpbNetUiKit.GapRef, s);
                BuildSide(_shell.Body, s, out _peerName, out _peerBar, out _peerText);

                _note = VpbNetUiKit.Line(_shell.Body, string.Empty, VpbNetUiKit.FontCaption,
                    UI.WarnText, VpbNetUiKit.LineRef * 2f, s, true);
                _note.alignment = TextAnchor.UpperLeft;

                GameObject row = VpbNetUiKit.WrapRow(_shell.Body, 190f, VpbNetUiKit.ButtonRef, s);
                _accept = VpbNetUiKit.PrimaryBtn(row,
                    VPBTranslation.T("net_content.accept", "Download and join"), 0f, s, AcceptClicked);
                _again = VpbNetUiKit.PrimaryBtn(row,
                    VPBTranslation.T("net_content.again", "Ask them again"), 0f, s, AgainClicked);
                _retry = VpbNetUiKit.Btn(row,
                    VPBTranslation.T("net_content.retry", "Try again"), 0f, s, RetryClicked);
                _anyway = VpbNetUiKit.Btn(row,
                    VPBTranslation.T("net_content.anyway", "Join without them"), 0f, s, AnywayClicked);
                _stop = VpbNetUiKit.DangerBtn(row,
                    VPBTranslation.T("net_content.stop", "Stop"), 0f, s, StopClicked);
                _decline = VpbNetUiKit.DangerBtn(row,
                    VPBTranslation.T("net_content.decline", "Not now"), 0f, s, DeclineClicked);
                _done = VpbNetUiKit.PrimaryBtn(row,
                    VPBTranslation.T("net_content.close", "Close"), 0f, s, CloseClicked);
                VpbNetUiKit.FitWrapRow(row);

                _nextRefresh = 0f;
                Refresh();
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] content panel create failed: " + e.Message);
                Destroy();
                return false;
            }
        }

        static void BuildSide(GameObject body, float s, out Text name, out VpbNetUiKit.Bar bar, out Text text)
        {
            GameObject head = VpbNetUiKit.Row(body, VpbNetUiKit.LineRef, s);
            name = VpbNetUiKit.Line(head, string.Empty, VpbNetUiKit.FontBody,
                UI.TextPrimary, VpbNetUiKit.LineRef, s, false);
            VpbNetUiKit.FixWidth(name.gameObject, NameColRef, s);
            bar = VpbNetUiKit.ProgressBar(head, BarRef, s);

            text = VpbNetUiKit.Line(body, string.Empty, VpbNetUiKit.FontBody,
                UI.TextMuted, VpbNetUiKit.LineRef, s, true);
        }

        static void Refresh()
        {
            if (_shell == null) return;

            VpbNetOfferInfo offer = VpbNetContentSync.Offer;
            VpbNetContentStatus mine = VpbNetContentSync.Local;
            VpbNetContentStatus theirs = VpbNetContentSync.Peer;
            bool host = VpbNetContentSync.Mine;
            string who = PeerLabel();
            bool localIn = InOfferedScene(VpbNetPresence.LocalScene);
            bool peerIn = VpbNetPresence.HavePeerScene
                && InOfferedScene(VpbNetPresence.PeerScene);
            bool bothIn = localIn && peerIn
                && VpbNetContentPhase.CanLoad(mine.Phase)
                && VpbNetContentSync.PeerKnown
                && VpbNetContentPhase.CanLoad(theirs.Phase);

            NoteLoad(mine.Phase, true);
            NoteLoad(theirs.Phase, false);
            NotePeerFiles(theirs);

            SetText(_scene, VpbNetContentSync.DisplayTitle());
            if (_shell.Title != null)
                SetText(_shell.Title, TitleFor(host, bothIn));

            _sb.Length = 0;
            if (host)
            {
                _sb.Append("You invited ");
                _sb.Append(who);
                if (!offer.FromPackage)
                    _sb.Append(" - this scene is not in a package, so they can only join if they already have it");
            }
            else
            {
                _sb.Append(who);
                _sb.Append(" is in this scene and wants you there too");
                if (offer.ManifestCount > 0)
                {
                    _sb.Append("  ·  ");
                    _sb.Append(offer.ManifestCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _sb.Append(" packages in total");
                }
            }
            SetText(_from, _sb.ToString());

            SetHeadline(mine, theirs, host, who, localIn, peerIn, bothIn);
            SetSide(_mineName, _mineBar, _mineText,
                VPBTranslation.T("net_content.you", "You"), mine, true, localIn, _mineLoadAt);
            SetSide(_peerName, _peerBar, _peerText, who, theirs,
                VpbNetContentSync.PeerKnown, peerIn, _peerLoadAt);

            SetText(_note, ComposeNote(mine, host, localIn, peerIn, bothIn));
            SyncButtons(mine, host, bothIn);
        }

        static string TitleFor(bool host, bool bothIn)
        {
            if (bothIn)
                return VPBTranslation.T("net_content.title_ready", "Ready to start");
            return host
                ? VPBTranslation.T("net_content.title_host", "Bring them into this scene")
                : VPBTranslation.T("net_content.title", "Join their scene");
        }

        static void NoteLoad(byte phase, bool mine)
        {
            if (phase == VpbNetContentPhase.Loading)
            {
                if (mine)
                {
                    if (_mineLoadAt <= 0f) _mineLoadAt = Time.realtimeSinceStartup;
                }
                else
                {
                    _peerSawLoading = true;
                    if (_peerLoadAt <= 0f) _peerLoadAt = Time.realtimeSinceStartup;
                }
                return;
            }
            if (mine) _mineLoadAt = 0f;
            else _peerLoadAt = 0f;
        }

        static void NotePeerFiles(VpbNetContentStatus theirs)
        {
            if (!VpbNetContentSync.PeerKnown || !VpbNetContentPhase.CanLoad(theirs.Phase))
            {
                _peerFilesAt = 0f;
                return;
            }
            if (_peerFilesAt <= 0f) _peerFilesAt = Time.realtimeSinceStartup;
        }

        static void SetHeadline(VpbNetContentStatus mine, VpbNetContentStatus theirs,
            bool host, string who, bool localIn, bool peerIn, bool bothIn)
        {
            Color fill = UI.ChromeDark;
            Color dot = UI.TextDim;
            Color text = UI.TextPrimary;
            _sb.Length = 0;

            byte peerPhase = theirs.Phase;
            bool peerKnown = VpbNetContentSync.PeerKnown;

            if (bothIn)
            {
                _sb.Append("Both in this scene. You can start.");
                fill = UI.AccentGreen;
                dot = UI.AccentLive;
            }
            else if (mine.Phase == VpbNetContentPhase.Loading)
            {
                _sb.Append("This machine is loading. They are waiting.");
                AppendElapsed(_sb, _mineLoadAt);
                fill = UI.WarnSurface;
                dot = UI.WarnText;
                text = UI.WarnText;
            }
            else if (peerKnown && peerPhase == VpbNetContentPhase.Loading)
            {
                _sb.Append("WAIT — ");
                _sb.Append(who);
                _sb.Append(" is loading the scene.");
                AppendElapsed(_sb, _peerLoadAt);
                fill = UI.WarnSurface;
                dot = UI.WarnText;
                text = UI.WarnText;
            }
            else if (peerKnown && VpbNetContentPhase.IsWorking(peerPhase))
            {
                _sb.Append(who);
                _sb.Append(" is still getting content. Wait to start.");
                fill = UI.AccentBlue;
                dot = UI.AccentBlue;
            }
            else if (peerKnown && VpbNetContentPhase.CanLoad(peerPhase) && !peerIn)
            {
                _sb.Append("WAIT — ");
                _sb.Append(who);
                _sb.Append(" has the files. They are not in this scene yet.");
                fill = UI.WarnSurface;
                dot = UI.WarnText;
                text = UI.WarnText;
            }
            else if (host && !peerKnown)
            {
                _sb.Append("Waiting for an answer from ");
                _sb.Append(who);
                _sb.Append(".");
                fill = UI.AccentBlue;
                dot = UI.AccentBlue;
            }
            else if (host && peerPhase == VpbNetContentPhase.Waiting)
            {
                _sb.Append("WAIT — ");
                _sb.Append(who);
                _sb.Append(" has not accepted yet.");
                fill = UI.AccentBlue;
                dot = UI.AccentBlue;
            }
            else if (peerPhase == VpbNetContentPhase.Refused)
            {
                _sb.Append(who);
                _sb.Append(" declined. They are still in the old scene.");
                fill = UI.AccentRed;
                dot = UI.AccentRed;
                text = UI.WarnText;
            }
            else if (peerPhase == VpbNetContentPhase.Failed)
            {
                _sb.Append(who);
                _sb.Append(" could not get the scene.");
                fill = UI.AccentRed;
                dot = UI.AccentRed;
                text = UI.WarnText;
            }
            else if (!host && mine.Phase == VpbNetContentPhase.Waiting)
            {
                _sb.Append("Accept to download and load, or decline.");
                fill = UI.AccentBlue;
                dot = UI.AccentBlue;
            }
            else if (localIn && !peerIn)
            {
                _sb.Append("WAIT — you are in. They are not.");
                fill = UI.WarnSurface;
                dot = UI.WarnText;
                text = UI.WarnText;
            }
            else
            {
                _sb.Append("Not ready to start yet.");
                fill = UI.WarnSurface;
                dot = UI.WarnText;
                text = UI.WarnText;
            }

            SetText(_headline, _sb.ToString(), text);
            if (_headlineFill != null && _headlineFill.color != fill) _headlineFill.color = fill;
            if (_headlineDot != null && _headlineDot.color != dot) _headlineDot.color = dot;
        }

        static void SetSide(Text name, VpbNetUiKit.Bar bar, Text text, string label,
            VpbNetContentStatus st, bool known, bool inOffer, float loadAt)
        {
            SetText(name, label);

            bool loading = known && st.Phase == VpbNetContentPhase.Loading;
            bool filesReady = known && VpbNetContentPhase.CanLoad(st.Phase);
            bool seated = known && filesReady && !loading && inOffer;

            if (bar != null)
            {
                if (!known) bar.Set(0f);
                else if (loading) bar.Set(Busy01());
                else bar.Set(st.Fraction01);

                Color tone;
                if (!known) tone = UI.ChromeMid;
                else if (loading) tone = UI.AccentBlue;
                else if (seated) tone = UI.AccentGreen;
                else if (filesReady) tone = UI.WarnText;
                else tone = ToneFor(st.Phase);
                bar.SetTone(tone);
            }

            _sb.Length = 0;
            Color tc = UI.TextDim;
            if (!known)
            {
                _sb.Append("No answer yet");
            }
            else if (loading)
            {
                _sb.Append("Loading the scene — wait");
                AppendElapsed(_sb, loadAt);
                tc = UI.WarnText;
            }
            else if (seated)
            {
                AppendInScene(_sb, st);
                tc = UI.TextPrimary;
            }
            else if (filesReady)
            {
                _sb.Append("Has the files — not in this scene yet");
                tc = UI.WarnText;
            }
            else
            {
                st.Describe(_sb);
                tc = TextToneFor(st.Phase);
            }
            SetText(text, _sb.ToString(), tc);

            if (known && st.Current.Length > 0 && VpbNetContentPhase.IsWorking(st.Phase))
            {
                _sb.Append("   ");
                _sb.Append(st.Current);
                SetText(text, _sb.ToString(), tc);
            }
        }

        static float Busy01()
        {
            float t = Time.realtimeSinceStartup * 2.4f;
            return 0.22f + 0.56f * (0.5f + 0.5f * Mathf.Sin(t));
        }

        static void AppendElapsed(StringBuilder sb, float since)
        {
            if (sb == null || since <= 0f) return;
            int sec = (int)(Time.realtimeSinceStartup - since);
            if (sec < 1) return;
            sb.Append("  ·  ");
            int m = sec / 60;
            int s = sec % 60;
            sb.Append(m.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(':');
            if (s < 10) sb.Append('0');
            sb.Append(s.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        static void AppendInScene(StringBuilder sb, VpbNetContentStatus st)
        {
            sb.Append("In the scene — ready");
            if (st.Phase != VpbNetContentPhase.Degraded) return;

            int shortBy = st.Need - st.Have;
            if (shortBy < 1) shortBy = 1;
            sb.Append(" — missing ");
            sb.Append(shortBy.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(shortBy == 1 ? " package" : " packages");
        }

        static string ComposeNote(VpbNetContentStatus mine, bool host, bool localIn, bool peerIn, bool bothIn)
        {
            _sb.Length = 0;

            byte peerPhase = VpbNetContentSync.Peer.Phase;
            bool peerKnown = VpbNetContentSync.PeerKnown;

            if (bothIn)
            {
                _sb.Append("Avatars can be claimed now. Close this card when you are done.");
                return _sb.ToString();
            }

            if (mine.Phase == VpbNetContentPhase.Loading)
            {
                _sb.Append("This machine cannot answer while VaM loads. ");
                _sb.Append(PeerLabel());
                _sb.Append(" has been told to wait.");
                return _sb.ToString();
            }

            if (peerKnown && peerPhase == VpbNetContentPhase.Loading)
            {
                _sb.Append(PeerLabel());
                _sb.Append(" has the files and VaM is loading them now. Their machine goes quiet until it finishes — do not start until their row says In the scene.");
                return _sb.ToString();
            }

            if (peerKnown && VpbNetContentPhase.CanLoad(peerPhase) && !peerIn)
            {
                if (!_peerSawLoading && _peerFilesAt > 0f
                    && (Time.realtimeSinceStartup - _peerFilesAt) >= SilentPeerSeconds)
                {
                    _sb.Append("Their VPB reported files ready but never entered this scene. Older builds skip the loading report. They need a current VPB, or they load this scene themselves.");
                    return _sb.ToString();
                }
                _sb.Append("They have the files. Ready to start only after their row says In the scene — ready.");
                return _sb.ToString();
            }

            if (VpbNetContentPhase.CanLoad(mine.Phase) && localIn && !peerIn)
            {
                _sb.Append("You are in this scene. Wait — they have not reported it yet.");
                return _sb.ToString();
            }

            if (host)
            {
                if (!peerKnown)
                    _sb.Append("Nothing has come back from them yet. If their build is older it cannot answer this at all - they will have to open the scene themselves.");
                else if (peerPhase == VpbNetContentPhase.Waiting)
                    _sb.Append("Their session rules ask first, so this is now their decision. Wait — they are not loading yet.");
                else if (peerPhase == VpbNetContentPhase.Refused)
                    _sb.Append("They said no. Nothing was downloaded on their machine and they are still in the old scene.");
                else if (peerPhase == VpbNetContentPhase.Degraded)
                    _sb.Append("They are coming, but some of it was not on the Hub. Parts of the scene will be missing on their side.");
                return _sb.ToString();
            }

            int missing = VpbNetContentResolver.NotOnHubCount;
            if (missing > 0)
            {
                _sb.Append(missing.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _sb.Append(missing == 1
                    ? " package is not on the Hub, so it will be missing: "
                    : " packages are not on the Hub, so they will be missing: ");
                int shown = missing < VpbNetContentResolver.MaxNamedMissing
                    ? missing : VpbNetContentResolver.MaxNamedMissing;
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    _sb.Append(VpbNetContentResolver.NotOnHub(i));
                }
                if (missing > shown)
                {
                    _sb.Append(" and ");
                    _sb.Append((missing - shown).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _sb.Append(" more");
                }
                return _sb.ToString();
            }

            if (mine.Phase == VpbNetContentPhase.Waiting)
            {
                if (VpbNetContentSync.SceneRuleHeld)
                    _sb.Append("Loading their scene replaces every atom here, and a scene file can carry plugins that run on this machine. Only accept from someone you would already open a scene file from.");
                else
                    _sb.Append("Everything comes from the VaM Hub, exactly as if you had pressed the download yourself. Nothing is transferred between the two machines.");
                return _sb.ToString();
            }

            if (mine.Phase == VpbNetContentPhase.Refused && mine.Fail == VpbNetContentFail.Blocked)
            {
                _sb.Append(VpbNetContentSync.SceneRuleHeld
                    ? "Session rules have \"load scene content on my machine\" set to blocked, so their scene was not opened here."
                    : "Session rules have \"fetch missing content\" set to blocked. Change it under Rules if you want this to happen without asking.");
                return _sb.ToString();
            }

            if (mine.Fail == VpbNetContentFail.NeedsLogin)
            {
                _sb.Append("Sign in to the Hub from VaM's own Hub tab, then press Try again.");
                return _sb.ToString();
            }

            if (mine.Fail == VpbNetContentFail.TooBig)
            {
                _sb.Append("Raise Net.ContentMaxMB in settings if you want fetches this large, then press Try again.");
                return _sb.ToString();
            }

            if (mine.Fail == VpbNetContentFail.HubDisabled)
            {
                _sb.Append("The Hub is switched off in VaM, so nothing can be fetched. Turn it on, then press Try again.");
                return _sb.ToString();
            }

            return string.Empty;
        }

        static void SyncButtons(VpbNetContentStatus mine, bool host, bool bothIn)
        {
            bool working = VpbNetContentPhase.IsWorking(mine.Phase);
            bool loading = mine.Phase == VpbNetContentPhase.Loading;

            Show(_accept, !bothIn && VpbNetContentSync.AwaitingAnswer);
            Show(_retry, !bothIn && VpbNetContentSync.CanRetry);
            Show(_anyway, !bothIn && VpbNetContentSync.CanJoinAnyway);
            Show(_stop, !bothIn && !host && working);
            Show(_decline, !bothIn && !host && !working && !loading && !VpbNetContentPhase.CanLoad(mine.Phase));
            Show(_again, !bothIn && host && ShouldOfferAgain());
            Show(_done, bothIn);

            if (_accept != null && VpbNetContentSync.AwaitingAnswer)
            {
                _sb.Length = 0;
                if (VpbNetContentSync.Plan.NeedsNothing)
                {
                    _sb.Append(VPBTranslation.T("net_content.accept_load", "Load it and join"));
                }
                else
                {
                    _sb.Append(VPBTranslation.T("net_content.accept", "Download and join"));
                    uint kib = VpbNetContentSync.EstimateKiB();
                    if (kib > 0)
                    {
                        _sb.Append("  ");
                        VpbNetContentStatus.AppendSize(_sb, kib);
                    }
                }
                _accept.SetText(_sb.ToString());
            }
        }

        static bool ShouldOfferAgain()
        {
            if (!VpbNetContentSync.PeerKnown) return true;
            byte p = VpbNetContentSync.Peer.Phase;
            return p == VpbNetContentPhase.Refused
                || p == VpbNetContentPhase.Failed
                || p == VpbNetContentPhase.Unknown;
        }

        static void Show(VpbNetUiKit.Chip c, bool on)
        {
            if (c != null) c.SetActive(on);
        }

        static Color ToneFor(byte phase)
        {
            switch (phase)
            {
                case VpbNetContentPhase.Ready: return UI.AccentGreen;
                case VpbNetContentPhase.Degraded: return UI.WarnText;
                case VpbNetContentPhase.Failed:
                case VpbNetContentPhase.Refused: return UI.AccentRed;
                case VpbNetContentPhase.Fetching:
                case VpbNetContentPhase.Installing:
                case VpbNetContentPhase.Loading:
                case VpbNetContentPhase.Checking: return UI.AccentBlue;
            }
            return UI.ChromeMid;
        }

        static Color TextToneFor(byte phase)
        {
            switch (phase)
            {
                case VpbNetContentPhase.Failed:
                case VpbNetContentPhase.Refused: return UI.WarnText;
                case VpbNetContentPhase.Ready: return UI.TextPrimary;
                case VpbNetContentPhase.Loading: return UI.WarnText;
            }
            return UI.TextMuted;
        }

        static string PeerLabel()
        {
            try
            {
                string n = VpbNetPresence.PeerName;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return VPBTranslation.T("net_content.them", "Them");
        }

        static double NowMs()
        {
            try { return VpbNetRulebook.LastTickMs; }
            catch { return 0.0; }
        }

        static void AcceptClicked()
        {
            VpbNetContentSync.Accept(NowMs());
            _nextRefresh = 0f;
        }

        static void AnywayClicked()
        {
            VpbNetContentSync.JoinAnyway();
            _nextRefresh = 0f;
        }

        static void RetryClicked()
        {
            VpbNetContentSync.Retry(NowMs());
            _nextRefresh = 0f;
        }

        static void StopClicked()
        {
            VpbNetContentResolver.Cancel();
            _nextRefresh = 0f;
        }

        static void DeclineClicked()
        {
            VpbNetContentSync.Decline(NowMs());
            _nextRefresh = 0f;
        }

        static void AgainClicked()
        {
            VpbNetContentSync.ResendOffer(NowMs());
            _nextRefresh = 0f;
        }

        static void CloseClicked()
        {
            VpbNetContentSync.Dismiss();
            Destroy();
        }

        static void SetText(Text t, string s)
        {
            if (t == null || s == null) return;
            if (!string.Equals(t.text, s, StringComparison.Ordinal)) t.text = s;
        }

        static void SetText(Text t, string s, Color c)
        {
            if (t == null || s == null) return;
            if (!string.Equals(t.text, s, StringComparison.Ordinal)) t.text = s;
            if (t.color != c) t.color = c;
        }

        public static void Destroy()
        {
            if (_shell != null)
            {
                GameObject root = _shell.Root;
                VpbNetUiKit.Destroy(ref root);
            }
            _shell = null;
            _scene = null;
            _from = null;
            _headline = null;
            _headlineFill = null;
            _headlineDot = null;
            _mineName = null;
            _mineText = null;
            _peerName = null;
            _peerText = null;
            _note = null;
            _mineBar = null;
            _peerBar = null;
            _accept = null;
            _anyway = null;
            _retry = null;
            _stop = null;
            _decline = null;
            _again = null;
            _done = null;
            _shownOffer = 0;
            _mineLoadAt = 0f;
            _peerLoadAt = 0f;
            _peerFilesAt = 0f;
            _peerSawLoading = false;
        }
    }
}
