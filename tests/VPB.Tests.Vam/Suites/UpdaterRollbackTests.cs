using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class UpdaterRollbackTests
    {
        private readonly ITestOutputHelper _out;
        public UpdaterRollbackTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static string ShippedIndexJson()
        {
            string path = Path.Combine(Path.Combine(TestEnvironment.RepoRoot, "releases"), "index.json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        [Fact]
        public void ShippedReleaseIndexParsesIntoTheRollbackList()
        {
            string json = ShippedIndexJson();
            Assert.True(json != null, "releases/index.json is missing; the version picker would have nothing to offer.");

            var catalog = VpbReleaseCatalog.Parse(json);

            Assert.False(catalog.IsEmpty,
                "VpbReleaseCatalog parsed releases/index.json into an empty list, so the in-game version picker" +
                Environment.NewLine + "silently shows nothing at all.");

            _out.WriteLine("releases parsed: " + catalog.Releases.Length);
            _out.WriteLine("floor:           " + catalog.MinRollbackVersion);
            _out.WriteLine("latest:          " + catalog.Latest.Version);

            Assert.False(string.IsNullOrEmpty(catalog.MinRollbackVersion),
                "The parsed catalog has no MinRollbackVersion, so nothing stops a pin below the single-folder layout change.");

            foreach (VpbRelease r in catalog.Releases)
            {
                Assert.False(catalog.IsBelowFloor(r.Version),
                    "Release " + r.Version + " is below the floor " + catalog.MinRollbackVersion + " yet is offered for pinning.");
                Assert.True(r.Tag.IndexOf('/') < 0,
                    "Release " + r.Version + " carries a slashed tag; the GitHub tree call would 404 and the update " +
                    "would download every file unverified.");
            }
        }

        [Fact]
        public void ReleasesWithSlashedTagsAreDropped()
        {
            const string json = @"{
                ""IndexVersion"": 1,
                ""MinRollbackVersion"": ""0.32.406"",
                ""Releases"": [
                    { ""Version"": ""0.32.500"", ""Tag"": ""build/0.32.500"", ""Schema"": 13 },
                    { ""Version"": ""0.32.501"", ""Tag"": ""build-0.32.501"", ""Schema"": 13 }
                ]
            }";

            var catalog = VpbReleaseCatalog.Parse(json);

            Assert.Single(catalog.Releases);
            Assert.Equal("0.32.501", catalog.Releases[0].Version);
            Assert.Null(catalog.Find("0.32.500"));
        }

        [Fact]
        public void VersionCompareIsNumericNotLexical()
        {
            Assert.True(VpbReleaseCatalog.CompareVersions("0.32.9", "0.32.10") < 0,
                "0.32.9 must sort below 0.32.10. A lexical compare puts build 9 above build 10 and the updater then " +
                "reads a rollback as an upgrade.");
            Assert.True(VpbReleaseCatalog.CompareVersions("0.32.610", "0.32.678") < 0);
            Assert.True(VpbReleaseCatalog.CompareVersions("0.33.1", "0.32.999") > 0);
            Assert.Equal(0, VpbReleaseCatalog.CompareVersions("0.32.406", "0.32.406"));
            Assert.True(VpbReleaseCatalog.CompareVersions("0.32", "0.32.0") == 0);
        }

        [Fact]
        public void FloorRejectsBuildsBelowTheSingleFolderLayout()
        {
            var catalog = VpbReleaseCatalog.Parse(@"{ ""MinRollbackVersion"": ""0.32.406"", ""Releases"": [] }");

            Assert.True(catalog.IsBelowFloor("0.32.405"));
            Assert.True(catalog.IsBelowFloor("0.32.203"));
            Assert.False(catalog.IsBelowFloor("0.32.406"));
            Assert.False(catalog.IsBelowFloor("0.32.678"));
        }

        [Fact]
        public void SchemaRiskOnlyWarnsWhenTheTargetIsOlder()
        {
            int local = VpbLocalDatabase.CurrentSchemaVersion;

            Assert.Null(VpbUpdaterService.DescribeSchemaRisk(local));
            Assert.Null(VpbUpdaterService.DescribeSchemaRisk(local + 1));
            Assert.NotNull(VpbUpdaterService.DescribeSchemaRisk(local - 1));
            Assert.NotNull(VpbUpdaterService.DescribeSchemaRisk(0));
        }

        [Fact]
        public void PinnedConfigRoundTripsAndStaysReadableByOlderClients()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vpb_update_cfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var config = VpbUpdateConfig.Load(dir);
                config.Branch = "main";
                config.Pinned = true;
                config.PinnedTag = "build-0.32.610";
                config.PinnedVersion = "0.32.610";
                config.PinnedSchema = 12;
                config.Save();

                string raw = File.ReadAllText(Path.Combine(dir, "vpb_update_config.json"));
                Assert.Contains("build-0.32.610", raw);

                var reloaded = VpbUpdateConfig.Load(dir);
                Assert.True(reloaded.Pinned);
                Assert.Equal("build-0.32.610", reloaded.PinnedTag);
                Assert.Equal("0.32.610", reloaded.PinnedVersion);
                Assert.Equal(12, reloaded.PinnedSchema);
                Assert.Equal("build-0.32.610", reloaded.EffectiveRef);
                Assert.Equal("main", reloaded.Branch);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void AnOlderConfigWithOnlyABranchStillLoads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vpb_update_cfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "vpb_update_config.json"),
                    "{ \"branch\": \"multiplayer\", \"autoCheck\": true }");

                var config = VpbUpdateConfig.Load(dir);

                Assert.Equal("multiplayer", config.Branch);
                Assert.Equal("multiplayer", config.EffectiveRef);
                Assert.False(config.Pinned);
                Assert.True(config.AutoCheck);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void RetentionCannotSilentlyStrandAPinnedInstall()
        {
            const string indexAfterRetention = @"{
                ""IndexVersion"": 1,
                ""MinRollbackVersion"": ""0.32.406"",
                ""Keep"": 2,
                ""Releases"": [
                    { ""Version"": ""0.32.678"", ""Tag"": ""build-0.32.678"", ""Schema"": 13 },
                    { ""Version"": ""0.32.668"", ""Tag"": ""build-0.32.668"", ""Schema"": 13 }
                ]
            }";

            var catalog = VpbReleaseCatalog.Parse(indexAfterRetention);

            Assert.NotNull(catalog.Find("0.32.668"));
            Assert.Null(catalog.Find("0.32.610"));

            Assert.True(catalog.Find("0.32.610") == null,
                "A build pinned before retention pruned it must be detectable as unlisted. Without that the updater " +
                "reports a plain fetch failure and the user has no way to know unpinning is the fix.");
        }

        [Fact]
        public void ANewerBuildWithALowerVersionIsNotTreatedAsARollback()
        {
            const string json = @"{
                ""IndexVersion"": 1,
                ""MinRollbackVersion"": ""0.32.406"",
                ""Releases"": [
                    { ""Version"": ""0.32.500"", ""Tag"": ""build-0.32.500"", ""DateUtc"": ""2026-09-11T00:00:00Z"", ""Schema"": 13 },
                    { ""Version"": ""0.32.600"", ""Tag"": ""build-0.32.600"", ""DateUtc"": ""2026-09-01T00:00:00Z"", ""Schema"": 13 }
                ]
            }";

            var catalog = VpbReleaseCatalog.Parse(json);

            Assert.True(VpbReleaseCatalog.CompareVersions("0.32.500", "0.32.600") < 0);

            Assert.False(VpbReleaseCatalog.IsOlderThan(catalog, "0.32.500", "0.32.600"),
                "0.32.500 shipped after 0.32.600, so moving to it is an update, not a rollback. Version numbers are " +
                "not monotonic here - a branch can be versioned ahead and merged back - so ship order decides, and " +
                "a plain version compare would label a forward update 'Rolling back'.");

            Assert.True(VpbReleaseCatalog.IsOlderThan(catalog, "0.32.600", "0.32.500"),
                "Going from the newest build to the one before it is a rollback regardless of the numbers.");
        }

        [Fact]
        public void ShipOrderFallsBackToVersionsWhenTheCatalogCannotAnswer()
        {
            var catalog = VpbReleaseCatalog.Parse(@"{ ""Releases"": [] }");

            Assert.True(VpbReleaseCatalog.IsOlderThan(catalog, "0.32.500", "0.32.600"));
            Assert.True(VpbReleaseCatalog.IsOlderThan(null, "0.32.500", "0.32.600"));
            Assert.False(VpbReleaseCatalog.IsOlderThan(null, "0.32.600", "0.32.500"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("{")]
        [InlineData("[]")]
        [InlineData("{ \"Releases\": null }")]
        [InlineData("{ \"Releases\": [] }")]
        [InlineData("{ \"Releases\": [ { \"Version\": \"\", \"Tag\": \"\" } ] }")]
        [InlineData("{ \"Releases\": [ { \"nope\": 1 } ] }")]
        public void AMissingOrBrokenReleaseIndexParsesToAnEmptyCatalog(string json)
        {
            var catalog = VpbReleaseCatalog.Parse(json);

            Assert.NotNull(catalog);
            Assert.True(catalog.IsEmpty,
                "A missing, unreachable or malformed release index must degrade to an empty catalog. Throwing here " +
                "would take out the settings panel, and a half-parsed catalog would offer builds that do not exist.");
            Assert.Null(catalog.Latest);
            Assert.Null(catalog.Find("0.32.610"));
            Assert.False(catalog.IsBelowFloor("0.32.100"),
                "With no index there is no floor to enforce, so nothing may be rejected as below it.");
        }

        [Theory]
        [InlineData(0f, "[          ]", "0%")]
        [InlineData(0.5f, "[=====     ]", "50%")]
        [InlineData(1f, "[==========]", "100%")]
        public void ProgressRendersAcrossItsWholeRange(float fraction, string bar, string percent)
        {
            Assert.Equal(bar, VpbUpdaterService.RenderProgressBar(fraction));
            Assert.Equal(percent, VpbUpdaterService.FormatPercent(fraction));
        }

        [Theory]
        [InlineData(-0.5f)]
        [InlineData(2f)]
        [InlineData(float.NaN)]
        public void ProgressOutsideZeroToOneStillRenders(float fraction)
        {
            string bar = VpbUpdaterService.RenderProgressBar(fraction);

            Assert.Equal(12, bar.Length);
            Assert.StartsWith("[", bar);
            Assert.EndsWith("]", bar);

            string pct = VpbUpdaterService.FormatPercent(fraction);
            Assert.EndsWith("%", pct);
            int value = int.Parse(pct.TrimEnd('%'));
            Assert.InRange(value, 0, 100);
        }

        [Fact]
        public void ByteSizesReadAsHumanUnits()
        {
            Assert.Equal("0 MB", VpbUpdaterService.FormatBytes(0));
            Assert.Equal("0 MB", VpbUpdaterService.FormatBytes(-1));
            Assert.Equal("1 KB", VpbUpdaterService.FormatBytes(1024));
            Assert.Equal("6.1 MB", VpbUpdaterService.FormatBytes(6417408));
            Assert.Equal("16.9 MB", VpbUpdaterService.FormatBytes(17773248));
        }

        [Fact]
        public void ReleaseAgeIsReportedInWholeDays()
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string tenDaysAgo = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ssZ");

            string json = @"{ ""Releases"": [
                { ""Version"": ""0.32.700"", ""Tag"": ""build-0.32.700"", ""DateUtc"": """ + today + @""" },
                { ""Version"": ""0.32.690"", ""Tag"": ""build-0.32.690"", ""DateUtc"": """ + tenDaysAgo + @""" },
                { ""Version"": ""0.32.680"", ""Tag"": ""build-0.32.680"", ""DateUtc"": """" }
            ] }";

            var catalog = VpbReleaseCatalog.Parse(json);

            Assert.Equal(0, catalog.Find("0.32.700").DaysAgo);
            Assert.Equal(10, catalog.Find("0.32.690").DaysAgo);

            Assert.Equal(-1, catalog.Find("0.32.680").DaysAgo);
            Assert.Equal("", catalog.Find("0.32.680").DatePart);
        }

        [Fact]
        public void AFutureDatedReleaseDoesNotReportNegativeAge()
        {
            string tomorrow = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var catalog = VpbReleaseCatalog.Parse(
                @"{ ""Releases"": [ { ""Version"": ""0.32.700"", ""Tag"": ""build-0.32.700"", ""DateUtc"": """ + tomorrow + @""" } ] }");

            Assert.Equal(0, catalog.Find("0.32.700").DaysAgo);
        }

        [Fact]
        public void APinClaimWithNoTagDoesNotStrandTheUpdater()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vpb_update_cfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "vpb_update_config.json"),
                    "{ \"channel\": \"main\", \"pinned\": true, \"pinnedTag\": \"\" }");

                var config = VpbUpdateConfig.Load(dir);

                Assert.False(config.Pinned);
                Assert.Equal("main", config.EffectiveRef);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
