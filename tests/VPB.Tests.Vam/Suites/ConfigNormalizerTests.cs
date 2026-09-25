using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ConfigNormalizerTests
    {
        public ConfigNormalizerTests(VamFixture vam) { }

        [Theory]
        [InlineData(" grid ", "Grid")]
        [InlineData("BOTH", "Both")]
        [InlineData("", "List")]
        [InlineData(null, "List")]
        [InlineData("sideways", "List")]
        public void HoverPreviewModeIsCanonical(string raw, string expected)
        {
            Assert.True(VPBConfig.NormalizeHoverPreviewMode(raw) == expected,
                "A hand-edited or old VPB.cfg value '" + raw + "' must map to hover preview '" + expected + "'.");
        }

        [Theory]
        [InlineData("left", "Left")]
        [InlineData("TOP", "Top")]
        [InlineData("middle", "Right")]
        [InlineData(null, "Right")]
        public void DockSideIsCanonical(string raw, string expected)
        {
            Assert.True(VPBConfig.NormalizeDesktopFixedDockSide(raw) == expected,
                "Dock side '" + raw + "' must dock the gallery " + expected + ".");
        }

        [Theory]
        [InlineData("tag", "Tag")]
        [InlineData("Apply tags", "Tag")]
        [InlineData(" untagged ", "FilterUntagged")]
        [InlineData("Filter Mode", "FilterByTags")]
        [InlineData("nonsense", "FilterByTags")]
        [InlineData("", "FilterByTags")]
        public void UserTagModeAcceptsLegacyLabels(string raw, string expected)
        {
            Assert.True(VPBConfig.NormalizeGalleryDefaultUserTagAvailMode(raw) == expected,
                "User tag mode '" + raw + "' from an older VPB.cfg must open the tag pane in " + expected + " mode.");
        }

        [Theory]
        [InlineData("desktop only", "Desktop Only")]
        [InlineData("Both", "Desktop & VR")]
        [InlineData("VR", "VR Only")]
        [InlineData("off", "Off")]
        [InlineData(null, "Desktop & VR")]
        public void SpringScrollModeAcceptsLegacyLabels(string raw, string expected)
        {
            Assert.True(VPBConfig.NormalizeSpringScrollButtonMode(raw) == expected,
                "Spring scroll setting '" + raw + "' must resolve to '" + expected + "'.");
        }
    }
}
