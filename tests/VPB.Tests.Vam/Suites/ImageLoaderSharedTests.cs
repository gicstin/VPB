using System.Collections.Generic;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ImageLoaderSharedTests
    {
        public ImageLoaderSharedTests(VamFixture vam) { }

        private static byte[] Rgba(params byte[] px) { return px; }

        [Fact]
        public void QueueHandsOutImagesInPriorityOrder()
        {
            var q = new PriorityQueue<int>((a, b) => a.CompareTo(b));
            foreach (int v in new[] { 5, 1, 9, 3, 7, 2, 8 }) q.Enqueue(v);
            q.Remove(7);

            var order = new List<int>();
            while (q.Count > 0) order.Add(q.Dequeue());

            Assert.True(string.Join(",", order.ConvertAll(x => x.ToString()).ToArray()) == "1,2,3,5,8,9",
                "Thumbnails must load highest-priority first, and a cancelled image must leave the queue order intact; got " +
                string.Join(",", order.ConvertAll(x => x.ToString()).ToArray()));
        }

        [Fact]
        public void InvertFlipsEveryChannel()
        {
            byte[] raw = Rgba(0, 100, 255, 10);
            ImageLoaderShared.ApplyTransformations(raw, 4, 1, 1, false, true, false, false, 1f, false, "a.jpg");
            Assert.True(raw[0] == 255 && raw[1] == 155 && raw[2] == 0 && raw[3] == 245, "An inverted texture must invert all four channels.");
        }

        [Fact]
        public void NormalMapIsOpaque()
        {
            byte[] raw = Rgba(1, 2, 3, 4, 5, 6, 7, 8);
            ImageLoaderShared.ApplyTransformations(raw, 8, 2, 1, true, false, false, false, 1f, false, "n.png");
            Assert.True(raw[3] == 255 && raw[7] == 255, "Normal maps must load with full alpha or lighting goes transparent.");
        }

        [Fact]
        public void AlphaFromGrayscaleOnlyWhenImageHasNoAlpha()
        {
            byte[] opaque = Rgba(30, 60, 90, 255);
            ImageLoaderShared.ApplyTransformations(opaque, 4, 1, 1, false, false, true, false, 1f, false, "a.jpg");
            Assert.True(opaque[3] == 60, "An opaque texture asked for grayscale alpha must get the average brightness as alpha.");

            byte[] hasAlpha = Rgba(30, 60, 90, 17);
            ImageLoaderShared.ApplyTransformations(hasAlpha, 4, 1, 1, false, false, true, false, 1f, false, "a.jpg");
            Assert.True(hasAlpha[3] == 17, "A texture that already carries alpha must keep it.");

            byte[] png = Rgba(30, 60, 90, 255);
            ImageLoaderShared.ApplyTransformations(png, 4, 1, 1, false, false, true, false, 1f, true, "a.PNG");
            Assert.True(png[3] == 128, "A compressed PNG with grayscale alpha must force a DXT5-worthy alpha on the first pixel.");
        }

        [Fact]
        public void FlatBumpBecomesStraightUpNormal()
        {
            byte[] raw = new byte[4 * 4];
            for (int i = 0; i < raw.Length; i++) raw[i] = 128;
            ImageLoaderShared.ApplyTransformations(raw, raw.Length, 2, 2, false, false, false, true, 1f, false, "b.jpg");
            Assert.True(raw[0] > 120 && raw[0] < 135 && raw[2] > 250 && raw[3] == 255,
                "A flat bump map must produce a normal pointing straight out of the surface; got " + raw[0] + "," + raw[1] + "," + raw[2] + "," + raw[3]);
        }
    }
}
