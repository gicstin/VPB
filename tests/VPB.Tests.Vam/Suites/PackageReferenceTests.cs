using System;
using SimpleJSON;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class PackageReferenceTests
    {
        private readonly ITestOutputHelper _out;
        public PackageReferenceTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        [Theory]
        [InlineData("Creator.Pack.3:/Saves/scene/one.json", "Creator.Pack.3")]
        [InlineData("Creator.Pack.3:", "Creator.Pack.3")]
        [InlineData("Creator.Pack.latest:/Custom/x.vam", "Creator.Pack.latest")]
        [InlineData("AddonPackages/Creator.Pack.3.var", "Creator.Pack.3")]
        [InlineData("AddonPackages\\Creator.Pack.3.var", "Creator.Pack.3")]
        [InlineData("AddonPackages/sub/Creator.Pack.3.VAR", "Creator.Pack.3")]
        [InlineData("Creator.Pack.3.var", "Creator.Pack.3")]
        [InlineData("  Creator.Pack.3.var  ", "Creator.Pack.3")]
        public void PackageUidIsExtractedFromEveryReferenceForm(string input, string expected)
        {
            Assert.Equal(expected, PackageReferenceVersionResolver.TryExtractPackageUid(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Saves/scene/loose.json")]
        [InlineData("Custom/Clothing/Female/dress/dress.vam")]
        [InlineData(":leadingcolon")]
        public void NonPackageReferencesYieldNoUid(string input)
        {
            Assert.Null(PackageReferenceVersionResolver.TryExtractPackageUid(input));
        }

        [Theory]
        [InlineData("C:/vam/AddonPackages/Creator.Pack.3.var", "Creator.Pack.3")]
        [InlineData("D:\\Games\\VaM\\AddonPackages\\Creator.Pack.3.var", "Creator.Pack.3")]
        [InlineData("E:/vam/AddonPackages/Some Creator.Pack Name.2.var", "Some Creator.Pack Name.2")]
        public void AbsolutePathsResolveToThePackageNotTheDriveLetter(string absolutePath, string expected)
        {
            string uid = PackageReferenceVersionResolver.TryExtractPackageUid(absolutePath);

            Assert.Equal(expected, uid);
            Assert.True(uid == null || uid.Length > 1,
                "A one-character uid means the Windows drive letter was read as the package. " +
                "SceneUtils feeds these straight into SetActiveLoadReferrer, so the whole scene load would " +
                "resolve dependency versions against a package that does not exist - silently.");
        }

        [Theory]
        [InlineData("C:/vam/Saves/scene/local.json")]
        [InlineData("D:\\Games\\VaM\\Saves\\vpb_temp_undo_atom_1234.json")]
        [InlineData("C:")]
        [InlineData("C:/")]
        public void AbsolutePathsToNonPackageFilesYieldNoUid(string absolutePath)
        {
            Assert.Null(PackageReferenceVersionResolver.TryExtractPackageUid(absolutePath));
        }

        [Fact]
        public void SpacedPackageNamesKeepTheirSpaces()
        {
            Assert.Equal("Some Creator.Pack Name.2",
                PackageReferenceVersionResolver.TryExtractPackageUid("AddonPackages/Some Creator.Pack Name.2.var"));

            Assert.Equal("Some Creator.Pack Name.2",
                PackageReferenceVersionResolver.TryExtractPackageUid("Some Creator.Pack Name.2:/meta.json"));
        }

        [Theory]
        [InlineData("Exact", VarPackage.ReferenceVersionOption.Exact)]
        [InlineData("exact", VarPackage.ReferenceVersionOption.Exact)]
        [InlineData("  Exact  ", VarPackage.ReferenceVersionOption.Exact)]
        [InlineData("Minimum", VarPackage.ReferenceVersionOption.Minimum)]
        [InlineData("Min", VarPackage.ReferenceVersionOption.Minimum)]
        [InlineData("Latest", VarPackage.ReferenceVersionOption.Latest)]
        public void ReferenceVersionOptionsParse(string raw, VarPackage.ReferenceVersionOption expected)
        {
            VarPackage.ReferenceVersionOption option;
            Assert.True(PackageReferenceVersionResolver.TryParseOption(raw, out option), "failed to parse '" + raw + "'");
            Assert.Equal(expected, option);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Newest")]
        [InlineData("exactly")]
        [InlineData("0")]
        public void UnknownReferenceVersionOptionsAreRejectedRatherThanDefaulted(string raw)
        {
            VarPackage.ReferenceVersionOption option;
            Assert.False(PackageReferenceVersionResolver.TryParseOption(raw, out option),
                "'" + raw + "' parsed as " + option + ". Silently accepting an unknown option makes a typo in " +
                "meta.json look like a deliberate Latest, and dependency resolution quietly changes version.");
        }

        [Fact]
        public void MetaJsonOptionsAreAppliedToThePackage()
        {
            using (var install = new TempInstall("refver"))
            {
                VarFixture fixture = IndexFixture.SceneVar("Creator", "Pack", 3);
                fixture.WriteTo(install.AddonPackagesDir);
                VarPackage pkg = fixture.AsPackage();

                JSONClass meta = JSON.Parse(
                    "{ \"standardReferenceVersionOption\" : \"Exact\", \"scriptReferenceVersionOption\" : \"Minimum\" }").AsObject;

                PackageReferenceVersionResolver.ApplyFromMetaJson(pkg, meta);

                _out.WriteLine("standard=" + pkg.StandardReferenceVersionOption + " script=" + pkg.ScriptReferenceVersionOption);
                Assert.Equal(VarPackage.ReferenceVersionOption.Exact, pkg.StandardReferenceVersionOption);
                Assert.Equal(VarPackage.ReferenceVersionOption.Minimum, pkg.ScriptReferenceVersionOption);
            }
        }

        [Fact]
        public void MetaJsonWithoutOptionsLeavesThePackageAlone()
        {
            using (var install = new TempInstall("refver_none"))
            {
                VarFixture fixture = IndexFixture.SceneVar("Creator", "Pack", 3);
                fixture.WriteTo(install.AddonPackagesDir);
                VarPackage pkg = fixture.AsPackage();

                VarPackage.ReferenceVersionOption before = pkg.StandardReferenceVersionOption;
                PackageReferenceVersionResolver.ApplyFromMetaJson(pkg, JSON.Parse("{ }").AsObject);

                Assert.Equal(before, pkg.StandardReferenceVersionOption);
            }
        }

        [Fact]
        public void ApplyFromMetaJsonToleratesNulls()
        {
            PackageReferenceVersionResolver.ApplyFromMetaJson(null, JSON.Parse("{ }").AsObject);
            using (var install = new TempInstall("refver_null"))
            {
                VarFixture fixture = IndexFixture.SceneVar("Creator", "Pack", 3);
                fixture.WriteTo(install.AddonPackagesDir);
                PackageReferenceVersionResolver.ApplyFromMetaJson(fixture.AsPackage(), null);
            }
        }

        [Fact]
        public void ReferrerContextNestsAndUnwinds()
        {
            PackageReferenceVersionResolver.ClearActiveLoadReferrer();
            Assert.True(string.IsNullOrEmpty(PackageReferenceVersionResolver.PeekReferrerUid()));

            PackageReferenceVersionResolver.BeginReferrerContext("Creator.Outer.1");
            Assert.Equal("Creator.Outer.1", PackageReferenceVersionResolver.PeekReferrerUid());

            PackageReferenceVersionResolver.BeginReferrerContext("Creator.Inner.2");
            Assert.Equal("Creator.Inner.2", PackageReferenceVersionResolver.PeekReferrerUid());

            PackageReferenceVersionResolver.EndReferrerContext();
            Assert.Equal("Creator.Outer.1", PackageReferenceVersionResolver.PeekReferrerUid());

            PackageReferenceVersionResolver.EndReferrerContext();
            Assert.True(string.IsNullOrEmpty(PackageReferenceVersionResolver.PeekReferrerUid()),
                "An unbalanced referrer stack leaks the last package as the referrer for every later load, " +
                "which silently resolves other packages' dependencies against the wrong version policy.");
        }

        [Fact]
        public void UnbalancedEndReferrerContextDoesNotThrow()
        {
            PackageReferenceVersionResolver.ClearActiveLoadReferrer();
            PackageReferenceVersionResolver.EndReferrerContext();
            PackageReferenceVersionResolver.EndReferrerContext();
            Assert.True(string.IsNullOrEmpty(PackageReferenceVersionResolver.PeekReferrerUid()));
        }
    }
}
