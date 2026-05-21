using UnityEngine;

namespace VPB.src.util
{
    public static class XrUtils
    {
        public static bool IsVrActive()
        {
            try { return UnityEngine.XR.XRSettings.enabled; } catch { return false; }
        }
    }
}

