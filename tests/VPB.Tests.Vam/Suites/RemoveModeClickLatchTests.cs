using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class RemoveModeClickLatchTests
    {
        public RemoveModeClickLatchTests(VamFixture vam) { }

        [Fact]
        public void IdlePressErases()
        {
            bool eat = false;
            bool swallow = RemoveModeClickLatch.Swallow(ref eat, pressHeld: true, clickEdge: true);
            Assert.False(swallow, "Scene Eraser must still erase on a normal click.");
            Assert.False(eat);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void ToolPressAndConfirmDismissDoNotEraseWhileHeld(bool pressHeld, bool clickEdge)
        {
            bool eat = true;
            bool swallow = RemoveModeClickLatch.Swallow(ref eat, pressHeld, clickEdge);
            Assert.True(swallow, "Click that opened Scene Eraser or dismissed confirm must not erase the atom under the cursor.");
            Assert.True(eat, "Latch must stay armed until the mouse button is up, or Cancel looks stuck while the gallery is open.");
        }

        [Fact]
        public void SelectEdgeWithoutLevelStillDoesNotErase()
        {
            bool eat = true;
            bool swallow = RemoveModeClickLatch.Swallow(ref eat, pressHeld: false, clickEdge: true);
            Assert.True(swallow, "A select edge on the arming frame must not open the remove confirm.");
            Assert.False(eat, "Latch must release after that edge so the next real click can erase.");
        }

        [Fact]
        public void ReleaseArmsTheNextClick()
        {
            bool eat = true;
            bool swallow = RemoveModeClickLatch.Swallow(ref eat, pressHeld: false, clickEdge: false);
            Assert.False(swallow, "Releasing the mouse must not erase.");
            Assert.False(eat, "After release, the next scene click must be allowed to erase.");

            swallow = RemoveModeClickLatch.Swallow(ref eat, pressHeld: true, clickEdge: true);
            Assert.False(swallow, "The click after release is the real erase click.");
        }
    }
}
