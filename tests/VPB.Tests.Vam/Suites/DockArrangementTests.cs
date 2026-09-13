using System;
using System.Collections.Generic;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class DockArrangementTests
    {
        public DockArrangementTests(VamFixture vam) { }

        private static readonly GalleryDockSide[] AllSides =
        {
            GalleryDockSide.Left, GalleryDockSide.Top, GalleryDockSide.Right
        };

        private static GalleryDockSide[] Sides(string combo)
        {
            if (combo.Length == 0) return new GalleryDockSide[0];
            string[] parts = combo.Split('+');
            var sides = new GalleryDockSide[parts.Length];
            for (int i = 0; i < parts.Length; i++) sides[i] = GalleryDockLayout.Parse(parts[i]);
            return sides;
        }

        private static string Describe()
        {
            var live = new List<string>();
            for (int i = 0; i < AllSides.Length; i++)
            {
                GalleryDockSlot slot = GalleryDockLayout.Slot(AllSides[i]);
                if (slot != null && slot.Occupied)
                    live.Add(GalleryDockLayout.ToConfigString(AllSides[i]) + "=" + slot.PanelId);
            }
            return live.Count == 0 ? "no dock" : string.Join(", ", live.ToArray());
        }

        private static void ApplyArrangement(GalleryDockSide[] sides)
        {
            for (int i = 0; i < AllSides.Length; i++) GalleryDockLayout.ReleaseByUser(PanelIdOfSlot(AllSides[i]));
            for (int i = 0; i < sides.Length; i++)
                GalleryDockLayout.TryClaim(sides[i], i == 0 ? GalleryPanel.PrimaryPanelId : "panel_" + i);
            GalleryDockLayout.ReconcileDesktopStateFromClaims(true);
        }

        private static string PanelIdOfSlot(GalleryDockSide side)
        {
            GalleryDockSlot slot = GalleryDockLayout.Slot(side);
            return slot != null ? slot.PanelId : "";
        }

        /// <summary>The gallery a fresh VaM builds: one pane, which takes the edge it owned, then a pane per remaining edge.</summary>
        private static void SimulateGalleryStartup()
        {
            GalleryDockLayout.SelfHeal();

            VPBConfig cfg = VPBConfig.Instance;
            bool wantDock = cfg.DesktopFixedMode || GalleryDockLayout.AnySideWanted();
            if (!wantDock) return;

            GalleryDockSide preferred = GalleryDockLayout.WantedSideForPanel(GalleryPanel.PrimaryPanelId);
            if (preferred == GalleryDockSide.None) preferred = GalleryDockLayout.FirstUnclaimedWantedSide();
            if (preferred == GalleryDockSide.None) preferred = GalleryDockLayout.Parse(cfg.DesktopFixedDockSide);
            GalleryDockLayout.TryClaim(preferred, GalleryPanel.PrimaryPanelId);

            for (int extra = 1; extra <= AllSides.Length; extra++)
            {
                GalleryDockSide side = GalleryDockLayout.FirstUnclaimedWantedSide();
                if (side == GalleryDockSide.None) break;
                GalleryDockLayout.TryClaim(side, "panel_" + extra);
            }
        }

        [Theory]
        [InlineData("Right")]
        [InlineData("Left")]
        [InlineData("Top")]
        [InlineData("Left+Right")]
        [InlineData("Left+Top")]
        [InlineData("Top+Right")]
        [InlineData("Left+Top+Right")]
        public void EveryDockedArrangementComesBackOnTheSameEdges(string combo)
        {
            using (new TempInstall("dockarrange"))
            {
                GalleryDockSide[] sides = Sides(combo);
                ApplyArrangement(sides);
                string before = Describe();
                VPBConfig.Instance.Save(false, true);

                VPBConfig.ReloadFromDisk();
                SimulateGalleryStartup();

                for (int i = 0; i < AllSides.Length; i++)
                {
                    GalleryDockSide side = AllSides[i];
                    bool want = Array.IndexOf(sides, side) >= 0;
                    GalleryDockSlot slot = GalleryDockLayout.Slot(side);
                    Assert.True(want == slot.Occupied,
                        "Layout " + combo + " came back as \"" + Describe() + "\" instead of \"" + before +
                        "\": the " + GalleryDockLayout.ToConfigString(side) + " dock " +
                        (want ? "is missing after a VaM restart." : "appeared after a VaM restart."));
                }

                Assert.True(VPBConfig.Instance.DesktopFixedMode,
                    "Layout " + combo + " lost DesktopFixedMode across a VaM restart, so the gallery opens undocked.");
            }
        }

        [Theory]
        [InlineData("Right")]
        [InlineData("Left")]
        [InlineData("Top")]
        [InlineData("Left+Right")]
        [InlineData("Left+Top")]
        [InlineData("Top+Right")]
        [InlineData("Left+Top+Right")]
        public void TheFirstPaneKeepsItsOwnEdge(string combo)
        {
            using (new TempInstall("dockprimary"))
            {
                GalleryDockSide[] sides = Sides(combo);
                ApplyArrangement(sides);
                GalleryDockSide primaryBefore = GalleryDockLayout.SideOf(GalleryPanel.PrimaryPanelId);
                VPBConfig.Instance.Save(false, true);

                VPBConfig.ReloadFromDisk();
                SimulateGalleryStartup();

                Assert.Equal(primaryBefore, GalleryDockLayout.SideOf(GalleryPanel.PrimaryPanelId));
            }
        }

        [Fact]
        public void SingleFloatingPaneStaysUndockedAcrossRestart()
        {
            using (new TempInstall("dockfloat"))
            {
                ApplyArrangement(Sides("Left+Top+Right"));
                ApplyArrangement(Sides(""));

                Assert.False(VPBConfig.Instance.DesktopFixedMode,
                    "Switching to the dockless layout left DesktopFixedMode on, so VPB.cfg still asks for a dock.");
                VPBConfig.Instance.Save(false, true);

                VPBConfig.ReloadFromDisk();
                SimulateGalleryStartup();

                Assert.Equal(0, GalleryDockLayout.OccupiedCount());
                Assert.False(VPBConfig.Instance.DesktopFixedMode,
                    "The dock came back after a VaM restart; the user has to hand-edit VPB.cfg to get rid of it.");
            }
        }

        [Fact]
        public void AFailedRestoreDoesNotDeleteTheEdgeItCouldNotRefill()
        {
            using (new TempInstall("dockdecay"))
            {
                ApplyArrangement(Sides("Left+Top+Right"));
                VPBConfig.Instance.Save(false, true);

                VPBConfig.ReloadFromDisk();
                GalleryDockLayout.SelfHeal();
                GalleryDockLayout.TryClaim(GalleryDockSide.Top, GalleryPanel.PrimaryPanelId);
                GalleryDockLayout.TryClaim(GalleryDockSide.Right, "panel_1");
                GalleryDockLayout.ReconcileDesktopStateFromClaims(false);
                VPBConfig.Instance.Save(false, true);

                VPBConfig.ReloadFromDisk();
                SimulateGalleryStartup();

                Assert.Equal(3, GalleryDockLayout.OccupiedCount());
                Assert.True(GalleryDockLayout.Slot(GalleryDockSide.Left).Occupied,
                    "One launch that could not rebuild the Left dock erased it from the arrangement for good.");
            }
        }

        [Fact]
        public void APaneDyingWithVamKeepsItsEdgeButUndockingGivesItUp()
        {
            using (new TempInstall("dockintent"))
            {
                ApplyArrangement(Sides("Left+Top+Right"));

                GalleryDockLayout.Release("panel_1");
                Assert.Equal(3, GalleryDockLayout.WantedSideCount());
                Assert.False(GalleryDockLayout.Slot(GalleryDockSide.Top).Occupied);

                GalleryDockLayout.ReleaseByUser("panel_2");
                Assert.Equal(2, GalleryDockLayout.WantedSideCount());
                Assert.False(GalleryDockLayout.WantsSide(GalleryDockSide.Right),
                    "Undocking a pane left its edge in the arrangement, so it comes back next launch.");

                VPBConfig.Instance.Save(false, true);
                VPBConfig.ReloadFromDisk();
                SimulateGalleryStartup();

                Assert.True(GalleryDockLayout.Slot(GalleryDockSide.Top).Occupied,
                    "A pane that died with VaM lost its edge; only undocking should do that.");
                Assert.False(GalleryDockLayout.Slot(GalleryDockSide.Right).Occupied);
            }
        }

        [Fact]
        public void ConfigsWrittenBeforeArrangementsWereRememberedKeepTheirDock()
        {
            using (new TempInstall("docklegacy"))
            {
                ApplyArrangement(Sides("Left"));
                VPBConfig.Instance.Save(false, true);

                string cfgPath = VPBConfig.Instance.ConfigPathForDebug;
                string text = System.IO.File.ReadAllText(cfgPath).Replace("\"DockLeftWanted\"", "\"DockLeftWantedLegacy\"");
                System.IO.File.WriteAllText(cfgPath, text);

                VPBConfig.ReloadFromDisk();
                SimulateGalleryStartup();

                Assert.Equal(GalleryDockSide.Left, GalleryDockLayout.SideOf(GalleryPanel.PrimaryPanelId));
            }
        }
    }
}
