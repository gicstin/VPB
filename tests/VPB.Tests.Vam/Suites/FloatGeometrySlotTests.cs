using UnityEngine;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class FloatGeometrySlotTests
    {
        public FloatGeometrySlotTests(VamFixture vam) { }

        private static readonly Vector2 Min = new Vector2(200f, 150f);
        private static readonly Vector2 Max = new Vector2(900f, 700f);

        [Fact]
        public void UnsavedSlotRestoresNothing()
        {
            var slot = new FloatGeometrySlot(400f, 300f);
            Assert.True(slot.SavedPos == null, "A float that was never moved must open at its default spot, not at 0,0.");
            Assert.True(slot.SavedSize(Min, Max) == null, "A float that was never resized must open at its default size.");
        }

        [Fact]
        public void StoredGeometryRoundTrips()
        {
            var slot = new FloatGeometrySlot(400f, 300f);
            slot.StorePos(new Vector2(12f, -34f));
            slot.StoreSize(new Vector2(500f, 400f));
            Assert.True(slot.SavedPos == new Vector2(12f, -34f), "A moved float must reopen where the user left it.");
            Assert.True(slot.SavedSize(Min, Max) == new Vector2(500f, 400f), "A resized float must reopen at the user's size.");
        }

        [Fact]
        public void StoringNothingKeepsThePreviousGeometry()
        {
            var slot = new FloatGeometrySlot(400f, 300f);
            slot.StorePos(new Vector2(1f, 2f));
            slot.StorePos(null);
            slot.StoreSize(null);
            Assert.True(slot.SavedPos == new Vector2(1f, 2f), "Persisting a float with no captured position must not forget the saved one.");
            Assert.True(slot.SavedSize(Min, Max) == null, "Persisting a collapsed float must not invent a saved size.");
        }

        [Theory]
        [InlineData(100f, 400f)]
        [InlineData(500f, 100f)]
        public void SizeBelowMinimumIsIgnored(float w, float h)
        {
            var slot = new FloatGeometrySlot(400f, 300f);
            slot.StoreSize(new Vector2(w, h));
            Assert.True(slot.SavedSize(Min, Max) == null, "A too-small saved size must fall back to the default so the float stays usable.");
        }

        [Fact]
        public void SizeAboveMaximumIsClamped()
        {
            var slot = new FloatGeometrySlot(400f, 300f);
            slot.StoreSize(new Vector2(5000f, 5000f));
            Assert.True(slot.SavedSize(Min, Max) == Max, "An oversized saved float must be clamped so it fits on screen.");
        }
    }
}
