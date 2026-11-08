using System.Collections;
using System.IO;
using System.Reflection;

namespace VPB.Tests.Runtime
{
    [VpbRuntimeSuite(Name = "LayoutArrangement", Order = 110)]
    public static class LayoutArrangementSuite
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic;

        [VpbRuntimeTest]
        public static void MissingDesktopDockOnlyBlocksDesktopSnapshot()
        {
            GalleryPanel panel = FindReadyPanel();
            VPBConfig cfg = VPBConfig.Instance;
            foreach (GalleryPanel pane in Gallery.singleton.Panels)
                if (pane == null || pane.canvas == null || pane.isFixedLocally)
                    RuntimeAssert.Inconclusive("Open fully initialized floating gallery panes before testing a missing desktop dock.");

            if (!File.Exists(cfg.ConfigPathForDebug))
                RuntimeAssert.Inconclusive("Save the VPB configuration before testing layout persistence.");

            FieldInfo dirty = typeof(GalleryPanel).GetField("s_sessionArrangementDirty", StaticMembers);
            bool wasDirty = (bool)dirty.GetValue(null);
            bool wantedRight = cfg.DockRight.Wanted;
            string previousVr = cfg.LastLayoutSnapshotVR;
            string previousDesktop = cfg.LastLayoutSnapshotDesktop;
            string configPath = cfg.ConfigPathForDebug;
            byte[] previousConfig = File.ReadAllBytes(configPath);
            bool vr = GalleryPanel.CurrentLayoutPresetMode() == (int)LayoutPresetMode.VR;
            try
            {
                cfg.DockRight.Wanted = true;
                if (vr) cfg.LastLayoutSnapshotVR = "";
                else cfg.LastLayoutSnapshotDesktop = "";
                dirty.SetValue(null, true);

                typeof(GalleryPanel).GetMethod("WriteSessionArrangementSnapshot", InstanceMembers).Invoke(panel, null);

                if (vr)
                {
                    GalleryLayoutPreset saved = GalleryLayoutPreset.FromJsonString(cfg.LastLayoutSnapshotVR);
                    RuntimeAssert.NotNull(saved, "A remembered desktop dock prevented the VR layout from saving.");
                    RuntimeAssert.True(saved.IsVrPreset, "The VR arrangement was saved in desktop mode.");
                    RuntimeAssert.Equal(Gallery.singleton.PanelCount, saved.Panes.Count,
                        "Saving the VR arrangement lost a gallery pane.");
                    RuntimeAssert.False((bool)dirty.GetValue(null),
                        "A saved VR arrangement still retries saving every frame.");
                    RuntimeAssert.Equal(previousDesktop, cfg.LastLayoutSnapshotDesktop,
                        "Saving the VR arrangement overwrote the desktop arrangement.");
                }
                else
                {
                    RuntimeAssert.Equal("", cfg.LastLayoutSnapshotDesktop,
                        "A desktop layout with a missing wanted dock overwrote its saved arrangement.");
                    RuntimeAssert.True((bool)dirty.GetValue(null),
                        "A rejected desktop layout stopped waiting for its missing dock.");
                }
                RuntimeAssert.True(cfg.DockRight.Wanted,
                    "Saving a layout forgot the desktop dock wanted on the next desktop launch.");
            }
            finally
            {
                cfg.DockRight.Wanted = wantedRight;
                cfg.LastLayoutSnapshotVR = previousVr;
                cfg.LastLayoutSnapshotDesktop = previousDesktop;
                dirty.SetValue(null, wasDirty);
                File.WriteAllBytes(configPath, previousConfig);
            }
        }

        [VpbRuntimeTest]
        public static void VrRestoreDoesNotPollForDesktopDocks()
        {
            GalleryPanel panel = FindReadyPanel();
            MethodInfo method = typeof(GalleryPanel).GetMethod("TopUpWantedDocksAfterRestoreCo", InstanceMembers);
            var vrRecovery = (IEnumerator)method.Invoke(panel, new object[] { (int)LayoutPresetMode.VR });
            RuntimeAssert.False(vrRecovery.MoveNext(),
                "Restoring a VR layout started polling for desktop docks that cannot be rebuilt in VR.");
            var desktopRecovery = (IEnumerator)method.Invoke(panel, new object[] { (int)LayoutPresetMode.Desktop });
            RuntimeAssert.True(desktopRecovery.MoveNext(),
                "Restoring a desktop layout stopped watching for missing docked panes.");
        }

        private static GalleryPanel FindReadyPanel()
        {
            if (VPBConfig.Instance == null || VPBConfig.Instance.IsLoadingScene || GalleryPanel.IsLayoutApplyRunning
                || (bool)typeof(GalleryPanel).GetField("s_sessionArrangementRestoring", StaticMembers).GetValue(null))
                RuntimeAssert.Inconclusive("Wait for scene and layout restoration to finish before testing arrangement persistence.");

            if (Gallery.singleton != null)
                foreach (GalleryPanel panel in Gallery.singleton.Panels)
                    if (panel != null && panel.canvas != null
                        && (bool)typeof(GalleryPanel).GetField("hasLoadedContent", InstanceMembers).GetValue(panel))
                        return panel;

            RuntimeAssert.Inconclusive("Open a loaded VPB gallery before testing arrangement persistence.");
            return null;
        }
    }
}
