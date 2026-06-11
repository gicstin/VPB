using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        /// <summary>
        /// Scene-load refresh: yields while VPB <see cref="FileManager.RefreshCo"/> runs,
        /// then runs native VaM refresh on a later frame so the top banner can animate.
        /// Native <c>MVR.FileManagement.FileManager.Refresh</c> still completes in one main-thread
        /// slice — Step 1 removes VPB scan blocking and adds frame breaks around native work.
        /// </summary>
        public static IEnumerator RefreshForSceneLoadCoroutine(
            string reason,
            RefreshScope scope,
            ICollection<string> installMovedUids = null,
            bool init = false,
            bool clean = false,
            bool removeOldVersion = false)
        {
            string r = string.IsNullOrEmpty(reason) ? "manual" : reason;
            bool startedVpbRefresh = scope == RefreshScope.VpbOnly || scope == RefreshScope.Both;

            switch (scope)
            {
                case RefreshScope.VpbOnly:
                    FileManager.Refresh(r, init, clean, removeOldVersion);
                    break;

                case RefreshScope.NativeOnly:
                    break;

                case RefreshScope.Both:
                    FileManager.Refresh(r, init, clean, removeOldVersion);
                    break;

                case RefreshScope.InstallOnly:
                    if (installMovedUids != null && installMovedUids.Count > 0)
                    {
                        try { FileManager.NotifyInstalled(installMovedUids); } catch { }
                    }
                    break;
            }

            if (startedVpbRefresh)
            {
                float waitStart = Time.realtimeSinceStartup;
                const float maxWaitSec = 120f;
                while (FileManager.IsScanning)
                {
                    float waited = Time.realtimeSinceStartup - waitStart;
                    try
                    {
                        VpbProgressService.ReportSceneLoadPrepPhase(
                            waited >= 0.5f
                                ? ("Refreshing package catalog (" + waited.ToString("0") + "s)")
                                : "Refreshing package catalog");
                    }
                    catch { }
                    yield return null;
                    if (waited >= maxWaitSec)
                    {
                        LogUtil.LogWarning("[VPB] RefreshForSceneLoadCoroutine: VPB scan wait timeout");
                        break;
                    }
                }
            }
            else
            {
                yield return null;
            }

            yield return null;

            if (scope == RefreshScope.NativeOnly
                || scope == RefreshScope.Both
                || scope == RefreshScope.InstallOnly)
            {
                try { VpbProgressService.ReportSceneLoadPrepPhase("Updating VaM package catalog"); } catch { }
                yield return null;

                try
                {
                    if (!VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh(r))
                        VamOnDemandLoader.RunVamFileManagerRefreshNow(r);
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB] RefreshForSceneLoadCoroutine native refresh failed: " + ex.Message);
                }

                yield return null;
            }
        }
    }
}
