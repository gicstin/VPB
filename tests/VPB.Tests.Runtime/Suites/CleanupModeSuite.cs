using System;
using System.Collections;
using System.Reflection;

namespace VPB.Tests.Runtime
{
    [VpbRuntimeSuite(Name = "CleanupMode", Order = 100)]
    public static class CleanupModeSuite
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

        [VpbRuntimeTest]
        public static void OpeningCleanupScansAndRestoresThePreviousGalleryOnExit()
        {
            GalleryPanel panel = FindPopulatedBrowsePanel();
            Type type = typeof(GalleryPanel);
            string title = panel.GetTitle();
            string path = panel.GetCurrentPath();
            string extension = panel.GetCurrentExtension();
            GalleryLayoutMode layout = panel.LayoutMode;
            ContentType? left = panel.GetLeftActiveContent();
            ContentType? right = panel.GetRightActiveContent();
            type.GetMethod("InvalidateCleanupCandidatesCache", Members).Invoke(panel, null);
            try
            {
                panel.QuickMenu_OpenCleanupMode();
                RuntimeAssert.True((bool)type.GetField("cleanupModeActive", Members).GetValue(panel),
                    "Opening Cleanup from a populated gallery canceled Cleanup before its scan could run.");
                RuntimeAssert.False((bool)type.GetField("cleanupScanInProgress", Members).GetValue(panel),
                    "Cleanup finished opening but still blocks another scan.");
                RuntimeAssert.NotEqual(long.MinValue,
                    (long)type.GetField("cleanupCandidatesCachedScanClockBinary", Members).GetValue(panel),
                    "Cleanup reported ready without completing a fresh candidate scan.");
                RuntimeAssert.Equal(GalleryLayoutMode.List, panel.LayoutMode,
                    "Cleanup candidates must use the temporary list layout.");
            }
            finally
            {
                type.GetMethod("ExitCleanupModeForSidePanelNavigation", Members)
                    .Invoke(panel, new object[] { true, true });
            }
            RuntimeAssert.False((bool)type.GetField("cleanupModeActive", Members).GetValue(panel),
                "Leaving Cleanup must restore normal gallery actions.");
            RuntimeAssert.Equal(title, panel.GetTitle(), "Cleanup exit changed the previous gallery category.");
            RuntimeAssert.Equal(path, panel.GetCurrentPath(), "Cleanup exit changed the previous gallery path.");
            RuntimeAssert.Equal(extension, panel.GetCurrentExtension(), "Cleanup exit changed the previous file types.");
            RuntimeAssert.Equal(layout, panel.LayoutMode, "Cleanup exit did not restore the previous layout.");
            RuntimeAssert.Equal(left, panel.GetLeftActiveContent(), "Cleanup exit changed the previous left sidebar.");
            RuntimeAssert.Equal(right, panel.GetRightActiveContent(), "Cleanup exit changed the previous right sidebar.");
        }

        private static GalleryPanel FindPopulatedBrowsePanel()
        {
            Type type = typeof(GalleryPanel);
            if (Gallery.singleton != null)
            {
                foreach (GalleryPanel panel in Gallery.singleton.Panels)
                {
                    if (panel == null || !panel.gameObject.activeInHierarchy) continue;
                    if ((bool)type.GetMethod("IsSettingsPanelOpen", Members).Invoke(panel, null)) continue;
                    if (type.GetMethod("GetActiveStickyToolMode", Members).Invoke(panel, null).ToString() != "None") continue;
                    var files = type.GetField("currentFilteredFiles", Members).GetValue(panel) as IList;
                    if (files != null && files.Count > 0) return panel;
                }
            }
            RuntimeAssert.Inconclusive("Open a populated gallery with no active tool or Settings window before running CleanupMode.");
            return null;
        }
    }
}
