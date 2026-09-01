using System;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    // Join/drop failure is a message box, not a VaM splash. Splash auto-hides and
    // teardown logs replace it before anyone can read the diagnostic.
    public static class VpbNetAlertUi
    {
        const string HostName = "VPB_NetAlert";
        const float WidthRef = 560f;
        const float DefaultY = -72f;
        const int SortingOrder = 1008;

        static VpbNetUiKit.Shell _shell;
        static Text _body;
        static float _scale = 1f;
        static bool _rescaleLock;
        static string _title = string.Empty;
        static string _text = string.Empty;

        public static bool IsOpen { get { return _shell != null; } }

        public static void Show(VpbNetDropReason reason, bool asHost, bool hadPeer, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _title = TitleFor(reason, asHost, hadPeer);
            _text = text;
            try
            {
                UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null) es.SetSelectedGameObject(null);
            }
            catch { }
            if (_shell != null)
            {
                Paint();
                return;
            }
            Create();
        }

        public static void Tick()
        {
            if (VpbNetUiKit.Lost(_shell)) Destroy();
            if (_shell == null) return;
            RescaleIfNeeded();
            if (_shell == null) return;

            bool esc = Input.GetKeyDown(KeyCode.Escape);
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (esc || enter) Dismiss();
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

        public static void Dismiss()
        {
            Destroy();
            try { VpbNetSessionUi.RefreshNow(); }
            catch { }
        }

        static string TitleFor(VpbNetDropReason reason, bool asHost, bool hadPeer)
        {
            switch (reason)
            {
                case VpbNetDropReason.ConnectTimeout:
                case VpbNetDropReason.AuthFailed:
                    return asHost
                        ? VPBTranslation.T("net_alert.title_host_fail", "Room did not connect")
                        : VPBTranslation.T("net_alert.title_join", "Could not join");
                case VpbNetDropReason.VersionMismatch:
                    return VPBTranslation.T("net_alert.title_join", "Could not join");
                case VpbNetDropReason.Kicked:
                    return VPBTranslation.T("net_alert.title_kicked", "Removed from the room");
                case VpbNetDropReason.PeerLeave:
                    return VPBTranslation.T("net_alert.title_left", "They left");
                case VpbNetDropReason.ReconnectExhausted:
                    return VPBTranslation.T("net_alert.title_rejoin", "Could not rejoin");
                default:
                    return hadPeer
                        ? VPBTranslation.T("net_alert.title_ended", "Session ended")
                        : VPBTranslation.T("net_alert.title_join", "Could not join");
            }
        }

        static bool Create()
        {
            try
            {
                _scale = VpbNetUiKit.Scale();
                float s = _scale;
                _shell = VpbNetUiKit.BuildWindow(HostName, _title,
                    WidthRef, s, SortingOrder, new Vector2(0f, DefaultY), null);

                RectTransform rt = _shell.PanelRT;
                if (rt != null && !_shell.WorldSpace)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, DefaultY * s);
                }

                VpbNetUiKit.TitleChip(_shell, "X", s, Dismiss);

                Image fill = _shell.Panel != null ? _shell.Panel.GetComponent<Image>() : null;
                if (fill != null) fill.color = UI.WarnSurface;

                _body = VpbNetUiKit.Line(_shell.Body, string.Empty, VpbNetUiKit.FontBody,
                    UI.WarnText, VpbNetUiKit.LineRef * 4f, s, true);
                _body.alignment = TextAnchor.UpperLeft;

                GameObject row = VpbNetUiKit.Row(_shell.Body, VpbNetUiKit.ButtonRef, s);
                VpbNetUiKit.PrimaryBtn(row,
                    VPBTranslation.T("net_alert.ok", "OK"), 0f, s, Dismiss);

                Paint();
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] alert create failed: " + e.Message);
                Destroy();
                return false;
            }
        }

        static void Paint()
        {
            if (_shell == null) return;
            if (_shell.Title != null) _shell.Title.text = _title;
            if (_body != null) _body.text = _text;
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
        }
    }
}
