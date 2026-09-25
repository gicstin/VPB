using System;

namespace VPB
{
    /// <summary>Main-thread guard for clothing/hair-only native catalog refreshes.</summary>
    internal static class VpbCatalogRefreshGuard
    {
        static int s_SkipPackageMorphDepth;

        public static bool SkipPackageMorphRefresh
        {
            get { return s_SkipPackageMorphDepth > 0; }
        }

        public static void BeginSkipPackageMorphRefresh()
        {
            s_SkipPackageMorphDepth++;
        }

        public static void EndSkipPackageMorphRefresh()
        {
            if (s_SkipPackageMorphDepth > 0)
                s_SkipPackageMorphDepth--;
        }

        public static void RunSkippingPackageMorphRefresh(Action action)
        {
            if (action == null) return;
            BeginSkipPackageMorphRefresh();
            try { action(); }
            finally { EndSkipPackageMorphRefresh(); }
        }
    }
}
