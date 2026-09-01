using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    public static class VpbNetSceneLaunchGuard
    {
        const string HostName = "VPB_NetSceneGuard";
        const float WidthRef = 660f;
        const float ButtonMinRef = 150f;
        const float DefaultY = -72f;
        const int SortingOrder = 1006;
        const float ApprovalSeconds = 300f;
        const float LoadStartGraceSeconds = 90f;
        const float LoadEndTimeoutSeconds = 300f;
        const float SettleSeconds = 1.5f;

        static readonly StringBuilder _sb = new StringBuilder(320);

        static VpbNetUiKit.Shell _shell;
        static Text _body;
        static Text _note;
        static float _scale = 1f;
        static bool _rescaleLock;
        static Action _resume;
        static string _label = string.Empty;
        static string _path = string.Empty;
        static bool _edit;
        static bool _canInvite;
        static bool _wasHost;
        static float _allowUntil;
        static bool _comeBackRunning;

        public static bool Blocking
        {
            get
            {
                try
                {
                    if (Time.realtimeSinceStartup < _allowUntil) return false;
                    return VpbNetRuntime.IsEnabled && VpbNetPresence.IsActive && VpbNetPresence.PeerUp;
                }
                catch { return false; }
            }
        }

        public static void AllowNext()
        {
            _allowUntil = Time.realtimeSinceStartup + ApprovalSeconds;
        }

        public static void NotifyLaunchPassed()
        {
            _allowUntil = 0f;
        }

        public static bool HoldSceneLaunch(string label, Action resume)
        {
            return HoldSceneLaunch(label, null, false, resume);
        }

        public static bool HoldSceneLaunch(string label, string path, bool editMode, Action resume)
        {
            try
            {
                if (!Blocking) return false;

                if (_shell != null)
                {
                    LogUtil.LogWarning("[VPB.Net] another scene launch is already waiting for an answer"
                        + " - answer that one first");
                    return true;
                }

                _label = string.IsNullOrEmpty(label) ? "that scene" : label;
                _path = path ?? string.Empty;
                _edit = editMode;
                _canInvite = _path.Length > 0 && SafePeerTakesContent();
                _resume = resume;
                _wasHost = LooksLikeHost();

                if (!Create())
                {
                    _resume = null;
                    _label = string.Empty;
                    _path = string.Empty;
                    LogUtil.LogWarning("[VPB.Net] scene launch blocked: you are in a room with " + PeerLabel()
                        + " - leave the session from the multiplayer panel, then load the scene");
                }
                return true;
            }
            catch { return false; }
        }

        public static bool HoldNativeSceneLaunch(string saveName, bool editMode)
        {
            if (string.IsNullOrEmpty(saveName)) return false;
            if (!Blocking) return false;

            string path = saveName;
            bool edit = editMode;
            return HoldSceneLaunch(SceneLabel(path), path, edit, delegate
            {
                try { SceneLoadingUtils.LoadSceneAs(path, false, edit); }
                catch (Exception e)
                {
                    LogUtil.LogError("[VPB.Net] resumed scene launch failed: " + e.Message);
                }
            });
        }

        static bool SafePeerTakesContent()
        {
            try { return VpbNetPresence.PeerTakesContent; }
            catch { return false; }
        }

        public static void Tick()
        {
            if (VpbNetUiKit.Lost(_shell)) Destroy();
            if (_shell == null) return;
            RescaleIfNeeded();
            if (_shell == null) return;

            if (!VpbNetRuntime.IsEnabled || !VpbNetPresence.IsActive || !VpbNetPresence.PeerUp)
            {
                LogUtil.LogWarning("[VPB.Net] the room ended while the question was open, so " + _label
                    + " is loading now");
                Proceed(false, false);
            }
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

        static bool LooksLikeHost()
        {
            try
            {
                if (VpbNetPresence.AsHost) return true;
                Settings s = Settings.Instance;
                return s != null && s.NetHostSession != null && s.NetHostSession.Value;
            }
            catch { return false; }
        }

        static string PeerLabel()
        {
            try
            {
                string n = VpbNetPresence.PeerName;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return "the other player";
        }

        static string SceneLabel(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "that scene";
                string p = path.Replace('\\', '/');
                int slash = p.LastIndexOf('/');
                if (slash >= 0 && slash + 1 < p.Length) p = p.Substring(slash + 1);
                int dot = p.LastIndexOf('.');
                if (dot > 0) p = p.Substring(0, dot);
                return p.Length > 0 ? p : "that scene";
            }
            catch { return "that scene"; }
        }

        static bool Create()
        {
            try
            {
                _scale = VpbNetUiKit.Scale();
                float s = _scale;

                _shell = VpbNetUiKit.BuildWindow(HostName, _canInvite
                        ? VPBTranslation.T("net_scene_guard.title_invite", "Load this scene together?")
                        : VPBTranslation.T("net_scene_guard.title", "Loading a scene leaves the room"),
                    WidthRef, s, SortingOrder, new Vector2(0f, DefaultY), null);

                RectTransform rt = _shell.PanelRT;
                if (rt != null && !_shell.WorldSpace)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, DefaultY * s);
                }

                VpbNetUiKit.TitleChip(_shell, "X", s, Stay);

                Image fill = _shell.Panel != null ? _shell.Panel.GetComponent<Image>() : null;
                if (fill != null) fill.color = UI.WarnSurface;

                _body = VpbNetUiKit.Line(_shell.Body, ComposeBody(), VpbNetUiKit.FontBody,
                    UI.WarnText, VpbNetUiKit.LineRef * 2f, s, true);
                _body.alignment = TextAnchor.UpperLeft;

                _note = VpbNetUiKit.Line(_shell.Body, ComposeNote(), VpbNetUiKit.FontCaption,
                    UI.TextDim, VpbNetUiKit.LineRef, s, true);
                _note.alignment = TextAnchor.UpperLeft;

                // Floor low enough that three chips share a line — 220×3 in 660 always wrapped one off.
                GameObject row = VpbNetUiKit.WrapRow(_shell.Body, ButtonMinRef, VpbNetUiKit.ButtonRef, s);
                if (_canInvite)
                {
                    VpbNetUiKit.PrimaryBtn(row,
                        VPBTranslation.T("net_scene_guard.bring", "Bring them"), 0f, s, BringThem);
                    VpbNetUiKit.Btn(row,
                        VPBTranslation.T("net_scene_guard.stay", "Cancel"), 0f, s, Stay);
                    VpbNetUiKit.DangerBtn(row,
                        VPBTranslation.T("net_scene_guard.leave", "Leave and load"), 0f, s, LoadAndLeave);
                }
                else
                {
                    VpbNetUiKit.PrimaryBtn(row,
                        VPBTranslation.T("net_scene_guard.comeback", "Load and rejoin"), 0f, s, LoadAndComeBack);
                    VpbNetUiKit.Btn(row,
                        VPBTranslation.T("net_scene_guard.stay", "Cancel"), 0f, s, Stay);
                    VpbNetUiKit.DangerBtn(row,
                        VPBTranslation.T("net_scene_guard.leave", "Leave and load"), 0f, s, LoadAndLeave);
                }
                VpbNetUiKit.FitWrapRow(row);

                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] scene launch prompt create failed: " + e.Message);
                Destroy();
                return false;
            }
        }

        // Glance copy: what happens, then the one thing the buttons do not say.
        static string ComposeBody()
        {
            _sb.Length = 0;
            _sb.Append("Loading ");
            _sb.Append(_label);
            _sb.Append(" replaces every atom here, and the room is bound to them. ");
            if (_canInvite)
            {
                _sb.Append("Bring ");
                _sb.Append(PeerLabel());
                _sb.Append(" and you both load it - nobody leaves the room.");
                return _sb.ToString();
            }
            _sb.Append("It cannot be sent to ");
            _sb.Append(PeerLabel());
            _sb.Append(", so this machine leaves the room first.");
            return _sb.ToString();
        }

        static string ComposeNote()
        {
            _sb.Length = 0;
            if (_canInvite)
            {
                _sb.Append("They fetch anything missing from the Hub - you will see how far they get. They can decline and stay put.");
                return _sb.ToString();
            }

            _sb.Append("The room code survives. ");
            _sb.Append(_wasHost
                ? "Rejoin puts you back on the new scene and they press Join again."
                : "They stay in the old scene until one of you loads the other's.");
            return _sb.ToString();
        }

        // Invite path: nobody leaves. Avatars rebind after both report the scene.
        static void BringThem()
        {
            Action resume = _resume;
            string path = _path;
            bool edit = _edit;
            _resume = null;
            _label = string.Empty;
            _path = string.Empty;
            Destroy();

            bool invited = false;
            try { invited = VpbNetPresence.InviteToScene(path, edit); }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] the invite could not be sent: " + e.Message);
            }

            if (!invited)
                LogUtil.LogWarning("[VPB.Net] the invite did not go out, so they will have to open"
                    + " that scene themselves; loading it here anyway");

            AllowNext();

            try { VpbNetBusy.Begin(VpbNetBusyKind.Scene, VpbNetContentSync.LoadBusySeconds); }
            catch { }
            try { VpbNetContentSync.WatchLocalLoad(); }
            catch { }

            if (resume == null)
            {
                try { VpbNetBusy.End(); }
                catch { }
                return;
            }

            try { resume(); }
            catch (Exception e)
            {
                try { VpbNetBusy.End(); }
                catch { }
                LogUtil.LogError("[VPB.Net] scene launch failed: " + e.Message);
            }
        }

        static void Stay()
        {
            LogUtil.LogWarning("[VPB.Net] scene launch cancelled - you are still in the room with " + PeerLabel());
            _resume = null;
            _label = string.Empty;
            _path = string.Empty;
            Destroy();
        }

        static void LoadAndLeave()
        {
            Proceed(false, true);
        }

        static void LoadAndComeBack()
        {
            Proceed(true, true);
        }

        static void Proceed(bool comeBack, bool leaveFirst)
        {
            Action resume = _resume;
            bool asHost = _wasHost;
            _resume = null;
            _label = string.Empty;
            _path = string.Empty;
            Destroy();

            AllowNext();

            if (leaveFirst)
            {
                try { VpbNetPresence.Leave(); }
                catch { }
            }

            if (comeBack) StartComeBack(asHost);

            if (resume == null) return;
            try { resume(); }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] scene launch failed after leaving the room: " + e.Message);
            }
        }

        static void StartComeBack(bool asHost)
        {
            if (_comeBackRunning) return;

            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { }
            if (sc == null)
            {
                LogUtil.LogWarning("[VPB.Net] cannot rejoin on its own - press "
                    + (asHost ? "Host" : "Join") + " again once the scene is up");
                return;
            }

            _comeBackRunning = true;
            try { sc.StartCoroutine(ComeBackRoutine(asHost)); }
            catch (Exception e)
            {
                _comeBackRunning = false;
                LogUtil.LogError("[VPB.Net] rejoin watcher failed to start: " + e.Message);
            }
        }

        static IEnumerator ComeBackRoutine(bool asHost)
        {
            float startDeadline = Time.realtimeSinceStartup + LoadStartGraceSeconds;
            bool started = false;
            while (Time.realtimeSinceStartup < startDeadline)
            {
                if (SceneLoadBusy()) { started = true; break; }
                yield return null;
            }

            if (!started)
            {
                _comeBackRunning = false;
                LogUtil.LogWarning("[VPB.Net] no scene load started, so the room was not rejoined - press "
                    + (asHost ? "Host" : "Join") + " when you are ready");
                yield break;
            }

            float endDeadline = Time.realtimeSinceStartup + LoadEndTimeoutSeconds;
            while (Time.realtimeSinceStartup < endDeadline)
            {
                if (!SceneLoadBusy()) break;
                yield return null;
            }

            if (SceneLoadBusy())
            {
                _comeBackRunning = false;
                LogUtil.LogWarning("[VPB.Net] the scene is still loading, so the room was not rejoined - press "
                    + (asHost ? "Host" : "Join") + " once it settles");
                yield break;
            }

            float settleUntil = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < settleUntil) yield return null;

            _comeBackRunning = false;
            ComeBackNow(asHost);
        }

        static void ComeBackNow(bool asHost)
        {
            try
            {
                if (!VpbNetRuntime.IsEnabled)
                {
                    LogUtil.LogWarning("[VPB.Net] multiplayer was switched off while the scene loaded,"
                        + " so the room was not rejoined");
                    return;
                }
                if (VpbNetPresence.Wanted) return;

                if (asHost)
                {
                    VpbNetPresence.Host();
                    LogUtil.LogWarning("[VPB.Net] hosting again on the new scene with the same room code"
                        + " - the other player presses Join");
                }
                else
                {
                    VpbNetPresence.Join();
                    LogUtil.LogWarning("[VPB.Net] rejoining the same room on the new scene");
                }
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] rejoin failed: " + e.Message);
            }
        }

        static bool SceneLoadBusy()
        {
            try { return LogUtil.IsSceneLoadActive() || LogUtil.IsSceneLoading(); }
            catch { return false; }
        }

        public static void Destroy()
        {
            if (_shell != null)
            {
                GameObject root = _shell.Root;
                VpbNetUiKit.Destroy(ref root);
            }
            _shell = null;
            _body = null;
            _note = null;
        }

        public static void Shutdown()
        {
            _resume = null;
            _label = string.Empty;
            _path = string.Empty;
            _canInvite = false;
            _allowUntil = 0f;
            Destroy();
        }
    }
}
