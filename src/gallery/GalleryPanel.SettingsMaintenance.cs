using System;
using System.Collections;
using System.Globalization;
using System.Threading;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const long OneGigabyte = 1024L * 1024L * 1024L;
        private bool _sceneAtomCacheCleanupRunning;

        private string GetSceneAtomCacheSettingsLabel()
        {
            string value = _sceneAtomCacheCleanupRunning
                ? VPBTranslation.T("settings.scene_atom_cache.clearing", "Clearing...")
                : FormatDatabaseFileSize(VpbLocalDatabase.GetDatabaseFileLength());
            return VPBTranslation.T("settings.scene_atom_cache", "Clear Scene Import Cache") + " (" + value + ")";
        }

        private static string FormatDatabaseFileSize(long bytes)
        {
            double divisor = bytes < OneGigabyte ? 1024d * 1024d : OneGigabyte;
            string unit = bytes < OneGigabyte ? " MB" : " GB";
            return (bytes / divisor).ToString("0.##", CultureInfo.InvariantCulture) + unit;
        }

        private void RequestSceneAtomCacheCleanup()
        {
            if (_sceneAtomCacheCleanupRunning) return;
            DisplayConfirm(
                VPBTranslation.T("settings.scene_atom_cache.confirm_title", "Clear Scene Import Cache?"),
                VPBTranslation.T("settings.scene_atom_cache.confirm_message", "Deletes cached Person atom data used by Scene Import, then compacts VPB database. All packages stay intact. This will lock the database and can take several minutes."),
                () =>
                {
                    if (_sceneAtomCacheCleanupRunning) return;
                    StartCoroutine(ClearSceneAtomCacheCoroutine());
                },
                null,
                VPBTranslation.T("settings.row.clear", "CLEAR"),
                VPBTranslation.T("gallery.confirm.cancel", "CANCEL"));
        }

        private IEnumerator ClearSceneAtomCacheCoroutine()
        {
            _sceneAtomCacheCleanupRunning = true;
            RefreshInternalSettingsListRows(true);
            ShowTemporaryStatus(VPBTranslation.T("settings.scene_atom_cache.clearing", "Clearing..."), 60f);

            bool success = false;
            bool cacheCleared = false;
            Exception error = null;
            int done = 0;
            try
            {
                bool queued = ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { success = VpbLocalDatabase.TryClearSceneAtomCacheAndVacuum(out cacheCleared, out error); }
                    finally { Interlocked.Exchange(ref done, 1); }
                });
                if (!queued)
                {
                    error = new InvalidOperationException("Could not start database cleanup worker");
                    Interlocked.Exchange(ref done, 1);
                }
            }
            catch (Exception ex)
            {
                error = ex;
                Interlocked.Exchange(ref done, 1);
            }
            while (Interlocked.CompareExchange(ref done, 0, 0) == 0)
                yield return null;

            _sceneAtomCacheCleanupRunning = false;
            RefreshInternalSettingsListRows(true);

            if (success)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("settings.scene_atom_cache.done", "VPB database cache cleared. Database is now ")
                    + FormatDatabaseFileSize(VpbLocalDatabase.GetDatabaseFileLength()) + ".",
                    5f);
                yield break;
            }

            LogUtil.LogError("[VPB] Scene cache cleanup failed: " + error);
            ShowTemporaryStatus(
                cacheCleared
                    ? VPBTranslation.T("settings.scene_atom_cache.compaction_failed", "Cache cleared, but database compaction failed. See log.")
                    : VPBTranslation.T("settings.scene_atom_cache.failed", "Database cache cleanup failed. See log."),
                6f);
        }
    }
}
