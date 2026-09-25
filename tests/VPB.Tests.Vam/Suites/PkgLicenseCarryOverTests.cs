using System.Collections.Generic;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class PkgLicenseCarryOverTests
    {
        public PkgLicenseCarryOverTests(VamFixture vam) { }

        private static Dictionary<string, VpbLocalDatabase.PkgLicenseCarryOver> Rows()
        {
            return new Dictionary<string, VpbLocalDatabase.PkgLicenseCarryOver>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Creator.Pack.1", new VpbLocalDatabase.PkgLicenseCarryOver { License = " CC BY ", WriteTime = 100, Size = 2048 } },
                { "Creator.NoLicense.1", new VpbLocalDatabase.PkgLicenseCarryOver { License = "", WriteTime = 7, Size = 9 } }
            };
        }

        [Fact]
        public void AnUnchangedArchiveKeepsItsIndexedLicense()
        {
            string license;
            Assert.True(VpbLocalDatabase.TryCarryOverLicense(Rows(), "creator.pack.1", 100, 2048, out license));
            Assert.Equal(VpbLocalDatabase.NormalizePkgLicense(" CC BY "), license);
        }

        [Fact]
        public void AnUnchangedArchiveWithNoLicenseIsNotReopenedEither()
        {
            string license;
            Assert.True(VpbLocalDatabase.TryCarryOverLicense(Rows(), "Creator.NoLicense.1", 7, 9, out license),
                "A package whose meta.json has no licenseType is reopened on every full rebuild.");
            Assert.Equal("", license);
        }

        [Theory]
        [InlineData("Creator.Pack.1", 101, 2048)]
        [InlineData("Creator.Pack.1", 100, 2049)]
        [InlineData("Creator.Other.1", 100, 2048)]
        [InlineData("", 100, 2048)]
        [InlineData(null, 100, 2048)]
        public void AReplacedOrUnknownArchiveIsReadAgain(string uid, long writeTime, long size)
        {
            string license;
            Assert.False(VpbLocalDatabase.TryCarryOverLicense(Rows(), uid, writeTime, size, out license),
                "A re-downloaded .var with the same UID kept the old row's license, so the gallery license filter shows " +
                "the previous version's terms.");
            Assert.Equal("", license);
        }

        [Fact]
        public void NoIndexedRowsMeansNothingToCarry()
        {
            string license;
            Assert.False(VpbLocalDatabase.TryCarryOverLicense(null, "Creator.Pack.1", 100, 2048, out license));
        }
    }
}
