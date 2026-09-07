using UnityEngine;

namespace VPB.src.util
{
    public static class XrUtils
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool XrSettingsEnabled()
        {
            return UnityEngine.XR.XRSettings.enabled;
        }

        public static bool IsVrActive()
        {
            try { return XrSettingsEnabled(); } catch { return false; }
        }
    }
}

