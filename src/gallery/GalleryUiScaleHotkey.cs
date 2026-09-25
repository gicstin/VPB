using UnityEngine;

namespace VPB
{
    internal static class GalleryUiScaleHotkey
    {
        private static int s_HandledFrame = -1;
        private static float s_LastNudgeUnscaledTime = -999f;
        private const float MinNudgeIntervalSeconds = 0.12f;
        private const float DiskSaveDelaySeconds = 0.35f;

        private static bool s_PendingDiskSave;
        private static float s_DiskSaveDueUnscaledTime = -1f;

        public static bool TryNudgeFromKeyboard()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!ctrl || !alt) return false;

            int dir = 0;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                dir = 1;
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                dir = -1;
            else
                return false;

            return TryNudge(dir);
        }

        public static bool TryNudge(int dir)
        {
            if (dir == 0) return false;
            float delta = dir > 0 ? 0.1f : -0.1f;

            int frame = Time.frameCount;
            if (s_HandledFrame == frame)
                return true;
            s_HandledFrame = frame;

            float now = Time.unscaledTime;
            if (now - s_LastNudgeUnscaledTime < MinNudgeIntervalSeconds)
                return true;
            s_LastNudgeUnscaledTime = now;

            var cfg = VPBConfig.Instance;
            if (cfg == null) return true;

            float before = cfg.InnerPaneScale;
            float after = Mathf.Clamp(Mathf.Round((before + delta) * 10f) / 10f, VPBConfig.MinUiScale, VPBConfig.MaxUiScale);
            if (!Mathf.Approximately(before, after))
            {
                cfg.InnerPaneScale = after;
                // Live chrome only — never Save() here (JSON disk write on key-repeat was multi-ms hitch).
                try { cfg.TriggerChange(); } catch { }
                s_PendingDiskSave = true;
                s_DiskSaveDueUnscaledTime = now + DiskSaveDelaySeconds;
            }

            return true;
        }

        public static void TickDeferredSave()
        {
            if (!s_PendingDiskSave) return;
            if (Time.unscaledTime < s_DiskSaveDueUnscaledTime) return;
            s_PendingDiskSave = false;
            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.Save(false);
            }
            catch { }
        }
    }
}
