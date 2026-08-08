using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Ctrl+Alt+= / Ctrl+Alt+- gallery chrome scale nudge.
    /// Shared so gallery + external busy chrome never double-step the same frame.
    /// Warm path: debounce key-repeat; live TriggerChange only; disk save coalesced via <see cref="TickDeferredSave"/>.
    /// </summary>
    internal static class GalleryUiScaleHotkey
    {
        private static int s_HandledFrame = -1;
        private static float s_LastNudgeUnscaledTime = -999f;
        /// <summary>Min gap between nudges — kills OS key-repeat storms (~30Hz) that re-ran full ConfigChanged.</summary>
        private const float MinNudgeIntervalSeconds = 0.12f;
        private const float DiskSaveDelaySeconds = 0.35f;

        private static bool s_PendingDiskSave;
        private static float s_DiskSaveDueUnscaledTime = -1f;

        /// <returns>True when chord consumed (even if scale already at clamp / debounced).</returns>
        public static bool TryNudgeFromKeyboard()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!ctrl || !alt) return false;

            float delta = 0f;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                delta = 0.1f;
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                delta = -0.1f;
            else
                return false;

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

        /// <summary>
        /// Call from any Update that may own the hotkey path. Writes VPB.cfg once after quiet period.
        /// Idle: one bool check.
        /// </summary>
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
