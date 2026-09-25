using System;

namespace VPB
{
    internal static class TurboJpegStats
    {
        private static readonly object Gate = new object();
        private static int s_LoggedFirstConfirm;

        internal static void NoteFirstTurboSuccess()
        {
            lock (Gate)
            {
                if (s_LoggedFirstConfirm != 0) return;
                s_LoggedFirstConfirm = 1;
            }
            try
            {
                LogUtil.Log("[VPB] TurboJPEG: first JPEG decode this session used libjpeg-turbo (profiler: texture name TurboJpeg).");
            }
            catch { }
        }
    }
}
