using UnityEngine;

namespace VPB
{
    public static class VpbNetRuntime
    {
        const float RecheckSeconds = 1f;

        static float _nextCheck;
        static bool _enabled;
        static bool _everEnabled;
        static bool _checked;
        static bool _scaleHooked;
        static bool _armCleared;

        public static bool IsEnabled { get { return _enabled; } }

        public static void NotifyChromeScale()
        {
            try { VpbNetSessionUi.RescaleIfNeeded(); } catch { }
            try { VpbNetRulesUi.RescaleIfNeeded(); } catch { }
            try { VpbNetAskUi.RescaleIfNeeded(); } catch { }
            try { VpbNetAlertUi.RescaleIfNeeded(); } catch { }
            try { VpbNetSceneLaunchGuard.RescaleIfNeeded(); } catch { }
            try { VpbNetContentUi.RescaleIfNeeded(); } catch { }
        }

        static void ClearStaleSessionArm()
        {
            if (_armCleared) return;
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetHostSession == null || s.NetJoinSession == null) return;

                bool wasHost = s.NetHostSession.Value;
                bool wasJoin = s.NetJoinSession.Value;
                _armCleared = true;
                if (!wasHost && !wasJoin) return;

                if (s.NetReopenOnStart != null && s.NetReopenOnStart.Value)
                {
                    LogUtil.LogWarning("[VPB.Net] Net.ReopenOnStart is on, so the "
                        + (wasHost ? "room you were hosting is opening" : "session you had joined is being dialled")
                        + " again by itself - turn that setting off if you would rather press "
                        + (wasHost ? "Host" : "Join") + " each time");
                    return;
                }

                s.NetHostSession.Value = false;
                s.NetJoinSession.Value = false;
                LogUtil.LogWarning("[VPB.Net] the " + (wasHost ? "room you were hosting" : "session you had joined")
                    + " was not reopened on startup - your room codes are still saved,"
                    + " so press " + (wasHost ? "Host" : "Join") + " when the other person is actually there");
            }
            catch { }
        }

        public static void Tick()
        {
            ClearStaleSessionArm();

            float now = Time.realtimeSinceStartup;
            if (!_checked || now >= _nextCheck)
            {
                _checked = true;
                _nextCheck = now + RecheckSeconds;
                bool want = VpbNetBrokerLink.MultiplayerEnabled;
                if (want != _enabled)
                {
                    _enabled = want;
                    if (want) _everEnabled = true;
                    else if (_everEnabled) TearDown();
                }
                HookScale(want);
            }

            if (!_enabled) return;

            VpbNetRecordReplay.Tick();
            VpbNetPresence.Poll();
            VpbNetSessionUi.Poll();
            VpbNetAskUi.Poll();
            VpbNetContentUi.Poll();
            VpbNetOverlay.Poll();
            VpbNetOverlay.Tick();
            VpbNetSessionUi.Tick();
            VpbNetAskUi.Tick();
            VpbNetAlertUi.Tick();
            VpbNetContentUi.Tick();
            VpbNetSceneLaunchGuard.Tick();
            VpbNetRulesUi.Tick();
            VpbNetUiKit.TickShells();
            VpbNetBrokerLink.Tick();
            VpbNetPresence.Tick();
        }

        static void HookScale(bool on)
        {
            if (on == _scaleHooked) return;
            try
            {
                if (VPBConfig.Instance == null) return;
                if (on) VPBConfig.Instance.ConfigChanged += NotifyChromeScale;
                else VPBConfig.Instance.ConfigChanged -= NotifyChromeScale;
                _scaleHooked = on;
            }
            catch { }
        }

        public static void TearDown()
        {
            _enabled = false;
            HookScale(false);
            try { VpbNetPresence.Stop("multiplayer switched off"); }
            catch { }
            try { VpbNetRecordReplay.Stop(); }
            catch { }
            try { VpbNetSessionUi.Destroy(); }
            catch { }
            try { VpbNetAskUi.Destroy(); }
            catch { }
            try { VpbNetAlertUi.Destroy(); }
            catch { }
            try { VpbNetContentUi.Destroy(); }
            catch { }
            try { VpbNetContentSync.Shutdown(); }
            catch { }
            try { VpbNetSceneLaunchGuard.Shutdown(); }
            catch { }
            try { VpbNetRulesUi.Destroy(); }
            catch { }
            try { VpbNetOverlay.Destroy(); }
            catch { }
            try { VpbNetBrokerLink.Stop("multiplayer switched off"); }
            catch { }
            LogUtil.LogWarning("[VPB.Net] Net.Enabled is off: the session, the broker and every net panel are shut down"
                + " and nothing net-related runs per frame until it is switched back on");
        }
    }
}
