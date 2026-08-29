using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    // Consent toast, not session chrome.
    public static class VpbNetAskUi
    {
        const string HostName = "VPB_NetAsk";
        // Three hugging chips on one row; 420 left Refuse hanging off the right edge.
        const float WidthRef = 560f;
        const float DefaultY = -72f;
        const int SortingOrder = 1005;
        const float RefreshSeconds = 0.25f;

        static readonly StringBuilder _sb = new StringBuilder(192);

        static VpbNetUiKit.Shell _shell;
        static Text _body;
        static Text _clock;
        static float _scale = 1f;
        static float _nextRefresh;
        static string _seenWhat;
        static int _seenLeft = -1;
        static int _seenCount = -1;
        static bool _rescaleLock;

        public static void Poll()
        {
            if (!VpbNetRulebook.HasPending)
            {
                if (_shell != null) Destroy();
                return;
            }
            if (_shell == null) Create();
        }

        public static void Tick()
        {
            if (VpbNetUiKit.Lost(_shell)) Destroy();
            if (_shell == null) return;
            RescaleIfNeeded();

            float now = Time.realtimeSinceStartup;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshSeconds;

            if (!VpbNetRulebook.HasPending)
            {
                Destroy();
                return;
            }

            string what = VpbNetRulebook.PendingText;
            int left = VpbNetRulebook.PendingSecondsLeft;
            int count = VpbNetRulebook.PendingCount;
            if (string.Equals(what, _seenWhat, StringComparison.Ordinal)
                && left == _seenLeft && count == _seenCount)
                return;

            _seenWhat = what;
            _seenLeft = left;
            _seenCount = count;
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

        static bool Create()
        {
            try
            {
                _scale = VpbNetUiKit.Scale();
                float s = _scale;
                _shell = VpbNetUiKit.BuildWindow(HostName,
                    VPBTranslation.T("net_ask.title", "They want to change something of yours"),
                    WidthRef, s, SortingOrder, new Vector2(0f, DefaultY), null);

                RectTransform rt = _shell.PanelRT;
                if (rt != null && !_shell.WorldSpace)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, DefaultY * s);
                }

                VpbNetUiKit.TitleChip(_shell, "X", s, Refuse);

                Image fill = _shell.Panel != null ? _shell.Panel.GetComponent<Image>() : null;
                if (fill != null) fill.color = UI.WarnSurface;

                _body = VpbNetUiKit.Line(_shell.Body, string.Empty, VpbNetUiKit.FontBody,
                    UI.WarnText, VpbNetUiKit.LineRef * 3f, s, true);
                _body.alignment = TextAnchor.UpperLeft;

                _clock = VpbNetUiKit.Line(_shell.Body, string.Empty, VpbNetUiKit.FontCaption,
                    UI.TextDim, VpbNetUiKit.LineRef, s, true);

                GameObject row = VpbNetUiKit.Row(_shell.Body, VpbNetUiKit.ButtonRef, s);
                VpbNetUiKit.PrimaryBtn(row,
                    VPBTranslation.T("net_ask.allow", "Allow this once"), 0f, s, Allow);
                VpbNetUiKit.Btn(row,
                    VPBTranslation.T("net_ask.always", "Always allow this"), 0f, s, Always);
                VpbNetUiKit.DangerBtn(row,
                    VPBTranslation.T("net_ask.refuse", "Refuse"), 0f, s, Refuse);

                _seenWhat = null;
                _seenLeft = -1;
                _seenCount = -1;
                _nextRefresh = 0f;
                Refresh();
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] ask toast create failed: " + e.Message);
                Destroy();
                return false;
            }
        }

        static void Refresh()
        {
            if (_shell == null) return;

            string what = VpbNetRulebook.PendingText;
            SetText(_body, string.IsNullOrEmpty(what)
                ? VPBTranslation.T("net_ask.empty", "They want to change something of yours.")
                : what);

            if (_shell != null && _shell.Title != null)
            {
                byte domain;
                byte axis;
                string title = null;
                if (VpbNetRulebook.TryOldestPending(out domain, out axis))
                    title = VpbNetRuleCatalog.LabelOf(domain, axis);
                if (string.IsNullOrEmpty(title))
                    title = VPBTranslation.T("net_ask.title", "They want to change something of yours");
                SetText(_shell.Title, title);
            }

            _sb.Length = 0;
            int left = VpbNetRulebook.PendingSecondsLeft;
            _sb.Append(left.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _sb.Append(" s left. No answer is a no.");
            int extra = VpbNetRulebook.PendingCount - 1;
            if (extra > 0)
            {
                _sb.Append("  ·  ");
                _sb.Append(extra.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _sb.Append(" more waiting");
            }
            SetText(_clock, _sb.ToString());
        }

        static void Allow()
        {
            VpbNetRulebook.ApproveOldest();
            AfterAnswer();
        }

        static void Always()
        {
            byte domain;
            byte axis;
            bool had = VpbNetRulebook.TryOldestPending(out domain, out axis);
            VpbNetRulebook.ApproveOldestAlways();
            if (had)
            {
                VpbNetRulesUi.Open();
                VpbNetRulesUi.Pulse(domain, axis);
            }
            AfterAnswer();
        }

        static void Refuse()
        {
            VpbNetRulebook.DenyOldest();
            AfterAnswer();
        }

        static void AfterAnswer()
        {
            if (!VpbNetRulebook.HasPending)
            {
                Destroy();
                return;
            }
            _seenWhat = null;
            _nextRefresh = 0f;
            Refresh();
        }

        static void SetText(Text t, string s)
        {
            if (t == null || s == null) return;
            if (!string.Equals(t.text, s, StringComparison.Ordinal)) t.text = s;
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
            _clock = null;
            _seenWhat = null;
        }
    }
}
