using System;
using System.IO;
using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private VpbUpdaterService _updaterService;
        private float _updaterLastAutoCheck;

        public VpbUpdaterService Updater => _updaterService;

        private void InitUpdater()
        {
            try
            {
                string gameRoot = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(gameRoot))
                {
                    _updaterService = new VpbUpdaterService(gameRoot, this);
                    _updaterService.OnStatusChanged = RefreshGallerySettingsRows;
                    _updaterService.FetchBranchesAsync();
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Updater] Init failed: " + ex.Message);
            }
        }

        private void UpdateUpdater()
        {
            if (_updaterService == null) return;

            if (_updaterService.Config.AutoCheck && _updaterLastAutoCheck == 0f &&
                _updaterService.Status == VpbUpdateStatus.Idle && !_updaterService.HasPendingUpdate)
            {
                _updaterLastAutoCheck = Time.realtimeSinceStartup;
                _updaterService.CheckForUpdateAsync();
            }
        }

        private static void RefreshGallerySettingsRows()
        {
            try
            {
                var g = Gallery.singleton;
                if (g == null) return;
                var panels = g.Panels;
                if (panels == null) return;
                for (int i = 0; i < panels.Count; i++)
                    panels[i].NotifyUpdaterStatusChanged();
            }
            catch { }
        }
    }
}
