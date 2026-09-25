using System;
using System.Collections.Generic;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ForceLatestDependencyPolicyTests
    {
        public ForceLatestDependencyPolicyTests(VamFixture vam) { }

        [Theory]
        [InlineData("Creator.Pack.3", "Creator.Pack", "Exact", 3)]
        [InlineData("Creator.Pack.12", "Creator.Pack", "Exact", 12)]
        [InlineData("Creator.Pack.min4", "Creator.Pack", "Minimum", 4)]
        [InlineData("Creator.Pack.MIN4", "Creator.Pack", "Minimum", 4)]
        [InlineData("Creator.Pack.latest", "Creator.Pack", "Latest", -1)]
        [InlineData("Creator.Pack.Latest", "Creator.Pack", "Latest", -1)]
        [InlineData("Some Creator.Pack Name.2", "Some Creator.Pack Name", "Exact", 2)]
        [InlineData("  Creator.Pack.3  ", "Creator.Pack", "Exact", 3)]
        public void EveryDependencyReferenceFormIsParsed(string id, string group, string kind, int version)
        {
            DependencyVersionRequest request;
            Assert.True(ForceLatestDependencyPolicy.TryParse(id, out request),
                "A dependency named '" + id + "' would never be upgraded to the newest installed version.");
            Assert.Equal(group, request.Group);
            Assert.Equal(kind, request.Kind.ToString());
            Assert.Equal(version, request.Version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Creator")]
        [InlineData("Creator.Pack")]
        [InlineData("Creator.Pack.")]
        [InlineData(".Pack.3")]
        [InlineData("Creator.Pack.x")]
        [InlineData("Creator.Pack.min")]
        [InlineData("Creator.Pack.3a")]
        [InlineData("Creator.Pack.Extra.3")]
        [InlineData("Creator.Pack.3:/Custom/x.vam")]
        [InlineData("AddonPackages/Creator.Pack.3")]
        [InlineData("Creator.Pack.1234567890")]
        public void NonDependencyStringsAreRejected(string id)
        {
            DependencyVersionRequest request;
            Assert.False(ForceLatestDependencyPolicy.TryParse(id, out request),
                "'" + id + "' is not a package dependency; treating it as one would rewrite unrelated paths during scene load.");
        }

        [Theory]
        [InlineData("Exact", 3, 5, true)]
        [InlineData("Exact", 5, 5, false)]
        [InlineData("Exact", 7, 5, false)]
        [InlineData("Minimum", 3, 5, true)]
        [InlineData("Minimum", 5, 5, true)]
        [InlineData("Minimum", 7, 5, false)]
        [InlineData("Latest", -1, 5, false)]
        [InlineData("Exact", 3, -1, false)]
        public void UpgradesOnlyEverMoveForward(string kind, int requested, int newestInstalled, bool expected)
        {
            var request = new DependencyVersionRequest { Group = "Creator.Pack", Kind = (DependencyVersionKind)Enum.Parse(typeof(DependencyVersionKind), kind), Version = requested };
            Assert.True(expected == ForceLatestDependencyPolicy.ShouldUpgrade(request, newestInstalled),
                expected
                    ? "The newest installed version was not used although it satisfies the request."
                    : "An older or equal version would replace the request: a scene asking for a newer version would load an outdated one instead of being reported missing and fetched.");
        }

        [Fact]
        public void ForceAllAppliesToEveryGroupExceptExclusions()
        {
            var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MacGruber.Life" };

            Assert.True(ForceLatestDependencyPolicy.AppliesToGroup("Creator.Pack", true, false, none, excluded, false));
            Assert.False(ForceLatestDependencyPolicy.AppliesToGroup("MacGruber.Life", true, false, none, excluded, false),
                "An excluded package would still be upgraded.");
            Assert.False(ForceLatestDependencyPolicy.AppliesToGroup("macgruber.life", true, false, none, excluded, false),
                "Exclusions must ignore letter case, like VaM package names do.");
        }

        [Fact]
        public void ExactVersionsSettingOverridesForceAll()
        {
            var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.False(ForceLatestDependencyPolicy.AppliesToGroup("Creator.Pack", true, true, none, none, true),
                "ForceExactPackageVersions promises pinned versions are never rewritten.");
        }

        [Fact]
        public void LegacyListModeOnlyForcesListedGroups()
        {
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Creator.Pack" };
            var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.True(ForceLatestDependencyPolicy.AppliesToGroup("Creator.Pack", false, true, listed, none, false));
            Assert.False(ForceLatestDependencyPolicy.AppliesToGroup("Other.Pack", false, true, listed, none, false));
            Assert.False(ForceLatestDependencyPolicy.AppliesToGroup("Creator.Pack", false, false, listed, none, false),
                "With both modes off nothing may be forced.");
            Assert.False(ForceLatestDependencyPolicy.AppliesToGroup("Creator.Pack", false, true, listed, listed, false),
                "An exclusion must win over the legacy include list.");
        }

        [Theory]
        [InlineData("Creator.Pack", "Creator.Pack")]
        [InlineData("  Creator.Pack  ", "Creator.Pack")]
        [InlineData("Creator.Pack.12", "Creator.Pack")]
        [InlineData("Creator.Pack.latest", "Creator.Pack")]
        [InlineData("Creator.Pack.min3", "Creator.Pack")]
        [InlineData("Creator.Pack.12:/Custom/Scripts/x.cslist", "Creator.Pack")]
        [InlineData("AddonPackages/sub/Creator.Pack.12.var", "Creator.Pack")]
        [InlineData("Some Creator.Pack Name.2", "Some Creator.Pack Name")]
        public void ExclusionEntriesAreStoredAsPackageGroups(string raw, string expected)
        {
            Assert.Equal(expected, ForceLatestDependencyPolicy.NormalizeExclusionEntry(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Creator")]
        [InlineData("a.b.c.d")]
        [InlineData("Saves/scene/local.json")]
        public void InvalidExclusionEntriesAreRejected(string raw)
        {
            Assert.Null(ForceLatestDependencyPolicy.NormalizeExclusionEntry(raw));
        }

        [Theory]
        [InlineData("Creator.Pack", "Creator.Pack.3", true)]
        [InlineData("Creator.Pack", "creator.pack.7", true)]
        [InlineData("Creator.Pack", "Creator.Pack.3:/Saves/scene/a.json", true)]
        [InlineData("Creator.Pack", "AddonPackages/Creator.Pack.3.var", true)]
        [InlineData("Creator.Pack", "Creator.Other.3", false)]
        [InlineData("Creator.Pack", "", false)]
        [InlineData("Creator.Pack", null, false)]
        public void SameGroupDetectionMatchesEveryReferrerForm(string group, string referrer, bool expected)
        {
            Assert.True(expected == ForceLatestDependencyPolicy.IsSameGroup(group, referrer),
                "A scene opened from an old version would mix files from the newest version of its own package.");
        }

        [Theory]
        [InlineData(":/Custom/Clothing/x.vam", "Custom/Clothing/x.vam")]
        [InlineData(":\\Custom\\Scripts\\a.cs", "Custom/Scripts/a.cs")]
        [InlineData(":", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void EntryPathsAreComparedWithoutTheUidSeparator(string afterUid, string expected)
        {
            Assert.Equal(expected, ForceLatestDependencyPolicy.NormalizeEntryInternalPath(afterUid));
        }

        [Theory]
        [InlineData("Creator.Pack.12", true, 12)]
        [InlineData("Creator.Pack.latest", false, -1)]
        [InlineData("Creator.Pack.min3", false, -1)]
        [InlineData(null, false, -1)]
        public void OnlyConcreteUidsReportAVersion(string uid, bool ok, int version)
        {
            int parsed;
            Assert.Equal(ok, ForceLatestDependencyPolicy.TryParseVersionOfUid(uid, out parsed));
            Assert.Equal(version, parsed);
        }
    }
}
