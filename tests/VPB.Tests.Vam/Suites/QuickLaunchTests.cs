using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class QuickLaunchTests
    {
        public QuickLaunchTests(VamFixture vam) { }

        [Fact]
        public void OfferedInDefaultDoubleClickBrowsing()
        {
            Assert.True(
                GalleryPanel.ShouldOfferQuickLaunch(false, false, false, false, false, false, false, ApplyMode.DoubleClick),
                "Default double-click browsing hides the one-click launch button, so launching still needs a double click.");
        }

        [Fact]
        public void HiddenWhenSingleClickAlreadyLaunches()
        {
            Assert.False(
                GalleryPanel.ShouldOfferQuickLaunch(false, false, false, false, false, false, false, ApplyMode.SingleClick),
                "Single-click mode shows a redundant launch button on every tile.");
        }

        [Fact]
        public void HiddenWhenHoldToLaunchIsOn()
        {
            Assert.False(
                GalleryPanel.ShouldOfferQuickLaunch(false, false, false, false, false, false, true, ApplyMode.DoubleClick),
                "Hold-to-launch exists to prevent accidental loads; a one-click button would bypass it.");
        }

        [Theory]
        [InlineData(true, false, false, false, false, false)]
        [InlineData(false, true, false, false, false, false)]
        [InlineData(false, false, true, false, false, false)]
        [InlineData(false, false, false, true, false, false)]
        [InlineData(false, false, false, false, true, false)]
        [InlineData(false, false, false, false, false, true)]
        public void HiddenInModesWhereClicksDoNotLaunch(bool settingRow, bool settingsList, bool importSidebar, bool subScenePick, bool removeMode, bool cleanupMode)
        {
            Assert.False(
                GalleryPanel.ShouldOfferQuickLaunch(settingRow, settingsList, importSidebar, subScenePick, removeMode, cleanupMode, false, ApplyMode.DoubleClick),
                "Launch button appears in a mode where clicking a tile must not load content into the scene.");
        }
    }
}
