using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class TagFacetCountsTests
    {
        public TagFacetCountsTests(VamFixture vam) { }

        private static FieldInfo[] Counters()
        {
            return typeof(TagFacetCounts).GetFields(BindingFlags.Public | BindingFlags.Instance);
        }

        private static void FillDistinct(TagFacetCounts counts)
        {
            FieldInfo[] fields = Counters();
            for (int i = 0; i < fields.Length; i++)
                fields[i].SetValue(counts, 1000 + i);
        }

        private static List<string> Differences(TagFacetCounts expected, TagFacetCounts actual)
        {
            return Counters()
                .Where(f => !Equals(f.GetValue(expected), f.GetValue(actual)))
                .Select(f => f.Name + " expected=" + f.GetValue(expected) + " got=" + f.GetValue(actual))
                .ToList();
        }

        [Fact]
        public void EveryFacetCounterIsAnInt()
        {
            List<string> odd = Counters().Where(f => f.FieldType != typeof(int)).Select(f => f.Name).ToList();
            Assert.True(odd.Count == 0,
                "TagFacetCounts.CopyFrom copies every public field; a non-counter field here would be shared between " +
                "gallery panes and snapshots instead of copied:" + Environment.NewLine + string.Join(Environment.NewLine, odd.ToArray()));
        }

        [Fact]
        public void CopyFromCarriesEveryCounter()
        {
            var source = new TagFacetCounts();
            FillDistinct(source);
            var target = new TagFacetCounts();
            target.CopyFrom(source);

            List<string> diff = Differences(source, target);
            Assert.True(diff.Count == 0,
                "Facet chip counts lost while copying between the SQL reader, the background scan and the gallery pane - " +
                "the chip shows a stale or zero count:" + Environment.NewLine + string.Join(Environment.NewLine, diff.ToArray()));
        }

        [Fact]
        public void SnapshotCacheRoundTripKeepsEveryCounterAndTag()
        {
            GalleryTagCountSnapshotCache.Clear();
            var snap = new TagCountSnapshot { TagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "Hair", 7 } } };
            FillDistinct(snap);

            GalleryTagCountSnapshotCache.Put("facet-test", snap);
            snap.TagCounts["Hair"] = 99;
            snap.ClothingSubfilterCountAll = -1;

            TagCountSnapshot back;
            Assert.True(GalleryTagCountSnapshotCache.TryGet("facet-test", out back), "A stored tag-count snapshot must be retrievable.");

            var expected = new TagFacetCounts();
            FillDistinct(expected);
            List<string> diff = Differences(expected, back);
            Assert.True(diff.Count == 0,
                "Reopening a cached category shows wrong facet chip counts:" + Environment.NewLine + string.Join(Environment.NewLine, diff.ToArray()));
            Assert.True(back.TagCounts["Hair"] == 7,
                "The snapshot cache must copy tag counts on Put, or a later refresh edits the cached counts in place.");
            GalleryTagCountSnapshotCache.Clear();
        }
    }
}
