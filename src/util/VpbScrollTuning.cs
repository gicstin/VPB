using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    internal static class VpbScrollTuning
    {
        internal const float VamScrollUnitsPerNotch = 100f;
        internal const float WatchPreviewUnitsPerNotch = 180f; // farther than gallery/QM before next item
        internal const float VamNativeScrollSensitivity = 1f;

        private static bool VrActive
        {
            get
            {
                try { return XrUtils.IsVrActive(); }
                catch { return false; }
            }
        }

        internal static float Sensitivity(float desktopValue, float uiScale)
        {
            if (!VrActive) return desktopValue;
            if (uiScale <= 0f) uiScale = 1f;
            float native = VamNativeScrollSensitivity * uiScale;
            return native < desktopValue ? native : desktopValue;
        }

        internal static void Apply(ScrollRect sr, float desktopValue, float uiScale)
        {
            if (sr == null) return;
            sr.scrollSensitivity = Sensitivity(desktopValue, uiScale);
        }

        internal static int TakeNotches(ref float accumulator, float scrollDeltaY)
        {
            return TakeNotches(ref accumulator, scrollDeltaY, VamScrollUnitsPerNotch);
        }

        internal static int TakeNotches(ref float accumulator, float scrollDeltaY, float unitsPerNotch)
        {
            if (float.IsNaN(scrollDeltaY) || float.IsInfinity(scrollDeltaY)) return 0;
            if ((accumulator > 0f && scrollDeltaY < 0f) || (accumulator < 0f && scrollDeltaY > 0f))
                accumulator = 0f;

            accumulator += scrollDeltaY;
            float unit = unitsPerNotch > 1f ? unitsPerNotch : VamScrollUnitsPerNotch;
            int notches = (int)(accumulator / unit);
            if (notches == 0) return 0;
            accumulator -= notches * unit;
            return notches;
        }
    }
}
