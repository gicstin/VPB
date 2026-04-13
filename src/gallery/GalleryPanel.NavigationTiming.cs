using System.Diagnostics;

namespace VPB
{
    /// <summary>
    /// Stopwatch-based navigation timing (not frame/coroutine based). Category-type clicks are tracked until
    /// the view is interactively ready: Show + file grid refresh + deferred main side tabs (category/creator).
    /// Tag / appearance-source sub-pane fill runs afterward and is logged separately as <c>subPane_async</c>.
    /// </summary>
    public partial class GalleryPanel
    {
        private void BeginGalleryCategoryTypeNavigationTiming(string categoryName)
        {
            if (!LogGalleryCategoryTypeSwitchTiming) return;
            if (_categoryTypeNavStopwatch != null)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Timing] catNav#" + _categoryTypeNavTargetSession + " ABORTED after " + _categoryTypeNavStopwatch.ElapsedMilliseconds + "ms (new click) prev='" + (_categoryTypeNavLabel ?? "") + "'");
                }
                catch { }
            }
            _categoryTypeNavTargetSession++;
            _categoryTypeNavLabel = categoryName == null ? "" : categoryName.Replace("|", "/");
            _categoryTypeNavStopwatch = Stopwatch.StartNew();
            try
            {
                LogUtil.Log("[VPB.Gallery.Timing] catNav#" + _categoryTypeNavTargetSession + " START '" + _categoryTypeNavLabel + "'");
            }
            catch { }
        }

        private void LogGalleryCategoryTypeNavPhase(string phase)
        {
            if (!LogGalleryCategoryTypeSwitchTiming || _categoryTypeNavStopwatch == null) return;
            try
            {
                LogUtil.Log("[VPB.Gallery.Timing] catNav#" + _categoryTypeNavTargetSession + " +" + _categoryTypeNavStopwatch.ElapsedMilliseconds + "ms " + phase + " | '" + (_categoryTypeNavLabel ?? "") + "'");
            }
            catch { }
        }

        private void CancelGalleryCategoryTypeNavigationTiming(string reason)
        {
            if (!LogGalleryCategoryTypeSwitchTiming || _categoryTypeNavStopwatch == null) return;
            try
            {
                LogUtil.Log("[VPB.Gallery.Timing] catNav#" + _categoryTypeNavTargetSession + " CANCEL " + reason + " at " + _categoryTypeNavStopwatch.ElapsedMilliseconds + "ms");
            }
            catch { }
            _categoryTypeNavStopwatch = null;
            _categoryTypeNavLabel = null;
        }

        private void FinalizeGalleryCategoryTypeNavigationSync(string reason)
        {
            if (!LogGalleryCategoryTypeSwitchTiming || _categoryTypeNavStopwatch == null) return;
            long ms = _categoryTypeNavStopwatch.ElapsedMilliseconds;
            try
            {
                LogUtil.Log("[VPB.Gallery.Timing] catNav#" + _categoryTypeNavTargetSession + " DONE " + reason + " total=" + ms + "ms '" + (_categoryTypeNavLabel ?? "") + "'");
            }
            catch { }
            _categoryTypeNavStopwatch = null;
            _categoryTypeNavLabel = null;
        }

        /// <returns>False if this refresh’s navigation was superseded (do not run tag sub-pane work for it).</returns>
        private bool TryFinalizeGalleryCategoryTypeNavigationFromDeferred(int sessionWhenRefreshScheduled)
        {
            if (!LogGalleryCategoryTypeSwitchTiming)
                return true;
            if (_categoryTypeNavStopwatch == null)
                return true;
            if (sessionWhenRefreshScheduled == 0)
                return true;
            if (sessionWhenRefreshScheduled != _categoryTypeNavTargetSession)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Timing] catNav deferred end SKIP (refresh was for session " + sessionWhenRefreshScheduled + ", current " + _categoryTypeNavTargetSession + ")");
                }
                catch { }
                return false;
            }
            long ms = _categoryTypeNavStopwatch.ElapsedMilliseconds;
            try
            {
                LogUtil.Log("[VPB.Gallery.Timing] catNav#" + _categoryTypeNavTargetSession + " DONE interactive total=" + ms + "ms '" + (_categoryTypeNavLabel ?? "") + "' (Show + RefreshFilesRoutine + main side tabs; tag sub-pane fills async)");
            }
            catch { }
            _categoryTypeNavStopwatch = null;
            _categoryTypeNavLabel = null;
            return true;
        }

        private void BeginSideTabCategoryCreatorTiming(string sideLabel)
        {
            if (!LogCategoryCreatorSideTabSwitchTiming) return;
            _sideTabCategoryCreatorTimingSide = sideLabel ?? "";
            _sideTabCategoryCreatorTimingSw = Stopwatch.StartNew();
        }

        private void EndSideTabCategoryCreatorTiming()
        {
            if (!LogCategoryCreatorSideTabSwitchTiming || _sideTabCategoryCreatorTimingSw == null) return;
            long ms = _sideTabCategoryCreatorTimingSw.ElapsedMilliseconds;
            string side = _sideTabCategoryCreatorTimingSide ?? "";
            _sideTabCategoryCreatorTimingSw = null;
            _sideTabCategoryCreatorTimingSide = "";
            try
            {
                LogUtil.Log("[VPB.Gallery.Timing] sideTab Category/Creator '" + side + "' sync_total=" + ms + "ms (ToggleLeft/Right through UpdateTabs; no WaitForEndOfFrame)");
            }
            catch { }
        }
    }
}
