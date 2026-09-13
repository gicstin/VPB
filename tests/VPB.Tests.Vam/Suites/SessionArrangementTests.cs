using UnityEngine;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class SessionArrangementTests
    {
        public SessionArrangementTests(VamFixture vam) { }

        private static GalleryLayoutPreset DeskWithLeftRightAndOneFloat()
        {
            var snap = new GalleryLayoutPreset();
            snap.Mode = (int)LayoutPresetMode.Desktop;
            snap.RestoreFilters = false;

            var left = new LayoutPaneState();
            left.DockSlot = (int)GalleryDockSide.Left;
            left.Collapsed = true;
            snap.Panes.Add(left);

            var right = new LayoutPaneState();
            right.DockSlot = (int)GalleryDockSide.Right;
            snap.Panes.Add(right);

            var floater = new LayoutPaneState();
            floater.DockSlot = (int)GalleryDockSide.None;
            floater.LocalPos = new Vector3(0.25f, -0.15f, 1.1f);
            floater.SizeRef = new Vector2(1440f, 900f);
            snap.Panes.Add(floater);

            return snap;
        }

        [Fact]
        public void TheSessionSnapshotSurvivesAVamRestartWithItsFloatingPane()
        {
            using (new TempInstall("session"))
            {
                VPBConfig.Instance.LastLayoutSnapshotDesktop = DeskWithLeftRightAndOneFloat().ToJsonString();
                VPBConfig.Instance.Save(false, true);

                VPBConfig.ReloadFromDisk();
                GalleryLayoutPreset back =
                    GalleryLayoutPreset.FromJsonString(VPBConfig.Instance.LastLayoutSnapshotDesktop);

                Assert.NotNull(back);
                Assert.Equal(3, back.Panes.Count);
                Assert.Equal((int)GalleryDockSide.Left, back.Panes[0].DockSlot);
                Assert.True(back.Panes[0].Collapsed,
                    "The left dock came back expanded; it was collapsed when the session ended.");
                Assert.Equal((int)GalleryDockSide.Right, back.Panes[1].DockSlot);
                Assert.Equal((int)GalleryDockSide.None, back.Panes[2].DockSlot);
                Assert.Equal(1440f, back.Panes[2].SizeRef.x);
                Assert.True(Vector3.Distance(new Vector3(0.25f, -0.15f, 1.1f), back.Panes[2].LocalPos) < 0.0005f,
                    "The floating pane came back somewhere else than where the user left it.");
            }
        }

        [Fact]
        public void AVrSnapshotIsNeverRestoredIntoADesktopSession()
        {
            using (new TempInstall("sessionmode"))
            {
                GalleryLayoutPreset vr = DeskWithLeftRightAndOneFloat();
                vr.Mode = (int)LayoutPresetMode.VR;
                VPBConfig.Instance.LastLayoutSnapshotVR = vr.ToJsonString();
                VPBConfig.Instance.Save(false, true);

                VPBConfig.ReloadFromDisk();

                Assert.Equal("", VPBConfig.Instance.LastLayoutSnapshotDesktop);
                GalleryLayoutPreset back =
                    GalleryLayoutPreset.FromJsonString(VPBConfig.Instance.LastLayoutSnapshotVR);
                Assert.Equal((int)LayoutPresetMode.VR, back.Mode);
            }
        }
    }
}
