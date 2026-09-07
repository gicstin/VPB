using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class DependencyVersionSortTests
    {
        private readonly ITestOutputHelper _out;
        public DependencyVersionSortTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static int CompareVersions(string a, string b)
        {
            MethodInfo m = typeof(GallerySortManager).GetMethod("CompareVersions",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.True(m != null,
                "GallerySortManager.CompareVersions is gone. It decides which version of a duplicated " +
                "dependency the gallery keeps; if it was renamed, update this test rather than deleting it.");
            return (int)m.Invoke(null, new object[] { a, b });
        }

        private static HashSet<string> Dedup(params string[] deps)
        {
            return GallerySortManager.DeduplicateDependenciesByLatestVersion(
                new HashSet<string>(deps, StringComparer.OrdinalIgnoreCase));
        }

        private static string[] Sorted(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        [Theory]
        [InlineData("2", "1", 1)]
        [InlineData("1", "2", -1)]
        [InlineData("3", "3", 0)]
        [InlineData("10", "9", 1)]
        [InlineData("100", "99", 1)]
        public void NumericVersionsCompareNumericallyNotAsText(string a, string b, int expectedSign)
        {
            int actual = CompareVersions(a, b);
            _out.WriteLine(a + " vs " + b + " -> " + actual);

            Assert.Equal(expectedSign, Math.Sign(actual));
        }

        [Fact]
        public void TenBeatsNineWhichStringComparisonWouldGetBackwards()
        {
            Assert.True(CompareVersions("10", "9") > 0,
                "Version 10 must sort above version 9. Ordinal string comparison puts '10' before '9', which " +
                "makes the gallery keep the older package when both versions are installed.");
        }

        [Theory]
        [InlineData("latest", "9")]
        [InlineData("latest", "1")]
        [InlineData("latest", "999")]
        public void LatestOutranksEveryPinnedVersion(string latest, string pinned)
        {
            Assert.True(CompareVersions(latest, pinned) > 0);
            Assert.True(CompareVersions(pinned, latest) < 0);
        }

        [Fact]
        public void DuplicateDependenciesCollapseToTheHighestVersion()
        {
            HashSet<string> result = Dedup("Creator.Pack.1", "Creator.Pack.4", "Creator.Pack.2");

            _out.WriteLine("kept: " + string.Join(", ", Sorted(result)));
            Assert.Equal(new[] { "Creator.Pack.4" }, Sorted(result));
        }

        [Fact]
        public void TenBeatsNineThroughTheDeduplicator()
        {
            HashSet<string> result = Dedup("Creator.Pack.9", "Creator.Pack.10");

            Assert.Equal(new[] { "Creator.Pack.10" }, Sorted(result));
        }

        [Fact]
        public void LatestWinsThroughTheDeduplicator()
        {
            HashSet<string> result = Dedup("Creator.Pack.3", "Creator.Pack.latest");

            Assert.Equal(new[] { "Creator.Pack.latest" }, Sorted(result));
        }

        [Fact]
        public void DifferentPackagesAreNeverCollapsedTogether()
        {
            HashSet<string> result = Dedup(
                "Creator.PackOne.1", "Creator.PackTwo.1", "Other.PackOne.1");

            Assert.Equal(
                new[] { "Creator.PackOne.1", "Creator.PackTwo.1", "Other.PackOne.1" },
                Sorted(result));
        }

        [Fact]
        public void PackageNamesContainingDotsKeepTheirIdentity()
        {
            HashSet<string> result = Dedup("Creator.Pack.Extra.1", "Creator.Pack.Extra.2");

            _out.WriteLine("kept: " + string.Join(", ", Sorted(result)));
            Assert.Single(result);
        }

        [Fact]
        public void EntriesThatAreNotPackageReferencesAreKeptAsIs()
        {
            HashSet<string> result = Dedup("Creator.Pack.1", "notapackage", "also.not");

            Assert.Contains("notapackage", result);
            Assert.Contains("also.not", result);
            Assert.Contains("Creator.Pack.1", result);
        }

        [Fact]
        public void AnEmptySetStaysEmpty()
        {
            Assert.Empty(Dedup());
        }

        [Fact]
        public void DeduplicationIsIndependentOfInsertionOrder()
        {
            string[] a = Sorted(Dedup("Creator.Pack.1", "Creator.Pack.7", "Creator.Pack.3"));
            string[] b = Sorted(Dedup("Creator.Pack.7", "Creator.Pack.3", "Creator.Pack.1"));
            string[] c = Sorted(Dedup("Creator.Pack.3", "Creator.Pack.1", "Creator.Pack.7"));

            Assert.Equal(a, b);
            Assert.Equal(b, c);
            Assert.Equal(new[] { "Creator.Pack.7" }, a);
        }

        [Fact]
        public void ShuffleFilesLeavesTheSameEntriesBehind()
        {
            var files = new List<VPB.FileEntry>();
            Exception thrown = Record.Exception(() => GallerySortManager.ShuffleFiles(files));

            Assert.True(thrown == null,
                "ShuffleFiles threw on an empty list: " + (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
            Assert.Empty(files);

            Assert.Null(Record.Exception(() => GallerySortManager.ShuffleFiles(null)));
        }
    }
}
