using Xunit;
using VPB.src.util;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class DiagnosticLogLevelTests
    {
        public DiagnosticLogLevelTests(VamFixture vam) { }

        [Theory]
        [InlineData(null, VpbDiagnosticLogLevel.Normal)]
        [InlineData("", VpbDiagnosticLogLevel.Normal)]
        [InlineData("Off", VpbDiagnosticLogLevel.Normal)]
        [InlineData("Normal", VpbDiagnosticLogLevel.Normal)]
        [InlineData("Detailed", VpbDiagnosticLogLevel.Extra)]
        [InlineData("Extra", VpbDiagnosticLogLevel.Extra)]
        [InlineData("Everything", VpbDiagnosticLogLevel.Full)]
        [InlineData("Full", VpbDiagnosticLogLevel.Full)]
        [InlineData("extra", VpbDiagnosticLogLevel.Extra)]
        public void HelperSpeakAndOldNamesMapToTheThreeVisibleLevels(string raw, string expected)
        {
            string got = VpbDiagnosticLogLevel.Normalize(raw);
            Assert.Equal(expected, got);
        }

        [Theory]
        [InlineData(false, false, VpbDiagnosticLogLevel.Normal)]
        [InlineData(true, false, VpbDiagnosticLogLevel.Extra)]
        [InlineData(true, true, VpbDiagnosticLogLevel.Full)]
        [InlineData(false, true, VpbDiagnosticLogLevel.Full)]
        public void SwitchBundlesReadBackAsTheNearestLevel(bool extra, bool full, string expected)
        {
            Assert.Equal(expected, VpbDiagnosticLogLevel.FromFlags(extra, full));
        }

        [Fact]
        public void ExtraTurnsOnTheDetailBundleAndLeavesStartupOff()
        {
            bool extra;
            bool full;
            VpbDiagnosticLogLevel.Decode(VpbDiagnosticLogLevel.Extra, out extra, out full);
            Assert.True(extra, "Extra must record Hub and gallery detail when a helper asks for a log.");
            Assert.False(full, "Extra must not turn on startup/slowdown probes.");
        }

        [Fact]
        public void FullIncludesTheExtraBundle()
        {
            bool extra;
            bool full;
            VpbDiagnosticLogLevel.Decode(VpbDiagnosticLogLevel.Full, out extra, out full);
            Assert.True(full, "Full must include startup and slowdown probes.");
            Assert.True(extra, "Full must also keep Hub and gallery detail.");
        }

        [Fact]
        public void NormalTurnsEveryBundleOff()
        {
            bool extra;
            bool full;
            VpbDiagnosticLogLevel.Decode(VpbDiagnosticLogLevel.Normal, out extra, out full);
            Assert.False(extra);
            Assert.False(full);
            Assert.False(VpbDiagnosticLogLevel.IsElevated(VpbDiagnosticLogLevel.Normal));
        }

        [Fact]
        public void OldEverythingAliasStillEnablesFull()
        {
            bool extra;
            bool full;
            VpbDiagnosticLogLevel.Decode("Everything", out extra, out full);
            Assert.True(full);
            Assert.True(VpbDiagnosticLogLevel.IsElevated("Everything"));
        }
    }
}
