using System.Collections.Generic;

namespace VPB
{
    public enum RefreshScope
    {
        /// <summary>VPB.FileManager.Refresh only — full var scan / gallery observers.</summary>
        VpbOnly,
        /// <summary>Coalesced MVR.FileManagement.FileManager.Refresh only (preset / on-demand catalog).</summary>
        NativeOnly,
        /// <summary>Full VPB scan plus coalesced native refresh.</summary>
        Both,
        /// <summary>Lightweight path sync (NotifyInstalled) plus coalesced native — no full VPB walk.</summary>
        InstallOnly
    }

    /// <summary>
    /// Single entry point for package / native FileManager refresh.
    /// Replaces ad-hoc dual calls to MVR and VPB FileManager.Refresh.
    /// </summary>
    public static class FileManagerBridge
    {
        public static void Refresh(string reason, RefreshScope scope, bool init = false, bool clean = false, bool removeOldVersion = false, bool flushNativeImmediately = false)
        {
            Refresh(reason, scope, null, init, clean, removeOldVersion, flushNativeImmediately);
        }

        public static void Refresh(string reason, RefreshScope scope, ICollection<string> installMovedUids, bool init = false, bool clean = false, bool removeOldVersion = false, bool flushNativeImmediately = false)
        {
            string r = string.IsNullOrEmpty(reason) ? "manual" : reason;

            switch (scope)
            {
                case RefreshScope.VpbOnly:
                    FileManager.Refresh(r, init, clean, removeOldVersion);
                    break;

                case RefreshScope.NativeOnly:
                    if (flushNativeImmediately)
                    {
                        if (!VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh(r))
                            VamOnDemandLoader.RunVamFileManagerRefreshNow(r);
                    }
                    else
                        VamOnDemandLoader.RequestCoalescedVamRefresh(r);
                    break;

                case RefreshScope.Both:
                    FileManager.Refresh(r, init, clean, removeOldVersion);
                    RequestCoalescedNativeRefresh(r, flushNativeImmediately);
                    break;

                case RefreshScope.InstallOnly:
                    if (installMovedUids != null && installMovedUids.Count > 0)
                    {
                        try { FileManager.NotifyInstalled(installMovedUids); } catch { }
                    }
                    RequestCoalescedNativeRefresh(r, flushNativeImmediately);
                    break;
            }
        }

        static void RequestCoalescedNativeRefresh(string reason, bool flushImmediately)
        {
            if (flushImmediately)
            {
                // ForceRunPendingCoalescedVamRefresh returns false when nothing is queued; fall through
                // to a direct Refresh so flushNativeImmediately:true callers actually get one.
                try
                {
                    if (!VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh(reason))
                        VamOnDemandLoader.RunVamFileManagerRefreshNow(reason);
                }
                catch { }
                return;
            }
            FileManager.ScheduleCoalescedNativeRefresh();
        }
    }
}
