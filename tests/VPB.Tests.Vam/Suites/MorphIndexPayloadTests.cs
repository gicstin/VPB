using System.Collections.Generic;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class MorphIndexPayloadTests
    {
        public MorphIndexPayloadTests(VamFixture vam) { }

        [Fact]
        public void ManifestBlobRoundTripKeepsMorphPaths()
        {
            var src = new SerializableVarPackage();
            src.FileEntryNames = new List<string> { "meta.json" };
            src.MorphFileEntryNames = new List<string>
            {
                "Custom/Atom/Person/Morphs/female/a.vmi",
                "Custom/Atom/Person/Morphs/male/b.vmi"
            };

            byte[] blob = src.SerializePayloadBody();
            Assert.True(blob != null && blob.Length > 0,
                "Empty morph index trailer would force a ZIP walk of every package on the next launch.");

            var dst = new SerializableVarPackage();
            dst.DeserializePayloadBody(blob);
            Assert.NotNull(dst.MorphFileEntryNames);
            Assert.Equal(2, dst.MorphFileEntryNames.Count);
            Assert.Equal(src.MorphFileEntryNames[0], dst.MorphFileEntryNames[0]);
            Assert.Equal(src.MorphFileEntryNames[1], dst.MorphFileEntryNames[1]);
        }

        [Fact]
        public void MissingMorphIndexStaysUnknownAfterRoundTrip()
        {
            var src = new SerializableVarPackage();
            src.FileEntryNames = new List<string> { "meta.json" };
            src.MorphFileEntryNames = null;

            var dst = new SerializableVarPackage();
            dst.DeserializePayloadBody(src.SerializePayloadBody());
            Assert.Null(dst.MorphFileEntryNames);
        }
    }
}
