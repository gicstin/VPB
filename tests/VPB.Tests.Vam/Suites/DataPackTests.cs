using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class DataPackTests
    {
        private readonly ITestOutputHelper _out;
        public DataPackTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static string Write(TempInstall install, DataPackFixture pack, string name = "pack.tsv")
        {
            return pack.WriteTo(install.PathTo("assets", "datapacks", name));
        }

        [Fact]
        public void HeaderFieldsAreReadBack()
        {
            using (var install = new TempInstall("pack_header"))
            {
                string path = Write(install, new DataPackFixture("hubtags")
                    .WithHeaderLine("source_url", "https://example.invalid/hub")
                    .WithHeaderLine("attribution", "Fixture")
                    .WithHeaderLine("content_hash", "deadbeef")
                    .WithEntry(1, title: "One"));

                VpbDataPackHeader header;
                Assert.True(VpbDataPackFileProbe.TryReadHeader(path, out header), "TryReadHeader rejected the fixture pack.");

                Assert.Equal("hubtags", header.PackId);
                Assert.Equal("1", header.PackVersion);
                Assert.Equal("2024-06-01", header.BuiltDate);
                Assert.Equal("https://example.invalid/hub", header.SourceUrl);
                Assert.Equal("Fixture", header.Attribution);
                Assert.Equal("deadbeef", header.ContentHash);
                Assert.Equal(3, header.FormatVersion);
                Assert.Equal(1, header.EntryCount);
            }
        }

        [Fact]
        public void AFileWithNoPackIdIsNotAcceptedAsAPack()
        {
            using (var install = new TempInstall("pack_nopackid"))
            {
                string path = install.PathTo("assets", "datapacks", "bogus.tsv");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "# just a comment\nentry_id\tvars\ttags\n1\t\t\n");

                VpbDataPackHeader header;
                Assert.False(VpbDataPackFileProbe.TryReadHeader(path, out header),
                    "A file without #pack_id must not be accepted - otherwise any TSV in the datapacks folder " +
                    "loads as a pack and its rows enter the gallery index.");
            }
        }

        [Fact]
        public void MissingFileIsReportedRatherThanThrowing()
        {
            VpbDataPackHeader header;
            Assert.False(VpbDataPackFileProbe.TryReadHeader(Path.Combine(Path.GetTempPath(), "no_such_pack.tsv"), out header));
        }

        [Fact]
        public void EntriesRoundTripThroughTheReader()
        {
            using (var install = new TempInstall("pack_entries"))
            {
                string path = Write(install, new DataPackFixture()
                    .WithEntry(101, title: "Blonde Look", creator: "Alpha", category: "Looks",
                               vars: "alpha.blondelook:3:1", tags: "hub:realistic|meta:female", downloads: 4200)
                    .WithEntry(102, title: "Dark Look", creator: "Beta", category: "Looks",
                               vars: "beta.darklook:1:0", tags: "hub:stylised"));

                var entries = new List<string>();
                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.True(reader.Open(path, out error), "Open failed: " + error);

                    while (reader.ReadEntry())
                    {
                        entries.Add(reader.EntryId + "|" + reader.Title + "|" + reader.Creator + "|" +
                                    string.Join(",", reader.VarKeys.ToArray()) + "|" +
                                    string.Join(",", reader.TagTexts.ToArray()) + "|" + reader.Downloads);
                    }

                    Assert.Equal(0, reader.MalformedLines);
                }

                foreach (string e in entries) _out.WriteLine(e);
                Assert.Equal(2, entries.Count);
                Assert.Contains("101|Blonde Look|Alpha|alpha.blondelook|realistic,female|4200", entries);
                Assert.Contains("102|Dark Look|Beta|beta.darklook|stylised|0", entries);
            }
        }

        [Fact]
        public void VarKeysCarryVersionAndExactFlag()
        {
            using (var install = new TempInstall("pack_vars"))
            {
                string path = Write(install, new DataPackFixture()
                    .WithEntry(1, vars: "alpha.pack:3:1|beta.other:7:0"));

                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.True(reader.Open(path, out error), error);
                    Assert.True(reader.ReadEntry());

                    Assert.Equal(new[] { "alpha.pack", "beta.other" }, reader.VarKeys.ToArray());
                    Assert.Equal(new[] { "3", "7" }, reader.VarVersions.ToArray());
                    Assert.Equal(new[] { 1, 0 }, reader.VarExact.ToArray());
                }
            }
        }

        [Fact]
        public void TagNamespacesSplitOnTheFirstColonOnly()
        {
            using (var install = new TempInstall("pack_tags"))
            {
                string path = Write(install, new DataPackFixture()
                    .WithEntry(1, tags: "hub:realistic|meta:has:colon|bare"));

                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.True(reader.Open(path, out error), error);
                    Assert.True(reader.ReadEntry());

                    for (int i = 0; i < reader.TagTexts.Count; i++)
                        _out.WriteLine("tag[" + i + "] ns='" + reader.TagNamespaces[i] + "' text='" + reader.TagTexts[i] + "'");

                    int idx = reader.TagTexts.IndexOf("has:colon");
                    Assert.True(idx >= 0,
                        "A tag value containing a colon must keep everything after the FIRST colon. " +
                        "Splitting on every colon silently truncates Hub tags.");
                    Assert.Equal("meta", reader.TagNamespaces[idx]);
                }
            }
        }

        [Fact]
        public void ColumnOrderIsResolvedByNameNotPosition()
        {
            using (var install = new TempInstall("pack_colorder"))
            {
                string path = Write(install, new DataPackFixture()
                    .WithColumns("tags", "entry_id", "creator", "vars", "title")
                    .WithRawRow("hub:realistic", "77", "Alpha", "alpha.pack:1:1", "Reordered"));

                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.True(reader.Open(path, out error), "Open failed on a reordered column row: " + error);
                    Assert.True(reader.ReadEntry());

                    Assert.Equal(77, reader.EntryId);
                    Assert.Equal("Reordered", reader.Title);
                    Assert.Equal("Alpha", reader.Creator);
                    Assert.Equal(new[] { "alpha.pack" }, reader.VarKeys.ToArray());
                    Assert.Equal(new[] { "realistic" }, reader.TagTexts.ToArray());
                }
            }
        }

        [Fact]
        public void APackMissingARequiredColumnIsRejected()
        {
            using (var install = new TempInstall("pack_missingcol"))
            {
                string path = Write(install, new DataPackFixture()
                    .WithColumns("entry_id", "title", "creator")
                    .WithRawRow("1", "No vars or tags column", "Alpha"));

                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.False(reader.Open(path, out error),
                        "A pack without the vars/tags columns must be rejected at Open, not read as empty rows.");
                    _out.WriteLine("rejected with: " + error);
                    Assert.False(string.IsNullOrEmpty(error), "Rejection must come with a reason for the log.");
                }
            }
        }

        [Fact]
        public void ANewerFormatVersionIsRefused()
        {
            using (var install = new TempInstall("pack_future"))
            {
                string path = Write(install, new DataPackFixture("hubtags", VpbDataPackReader.MaxSupportedFormatVersion + 1)
                    .WithEntry(1, title: "From the future"));

                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.False(reader.Open(path, out error),
                        "A pack newer than this build must be refused. Reading it with today's column meanings " +
                        "would file entries under the wrong fields with no error.");
                    _out.WriteLine("refused with: " + error);
                    Assert.Contains("newer", error ?? "", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact]
        public void MalformedRowsAreCountedAndSkippedRatherThanAborting()
        {
            using (var install = new TempInstall("pack_malformed"))
            {
                string path = Write(install, new DataPackFixture()
                    .WithEntry(1, title: "Good one")
                    .WithRawRow("not-a-number", "junk")
                    .WithEntry(3, title: "Good two"));

                var titles = new List<string>();
                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.True(reader.Open(path, out error), error);
                    while (reader.ReadEntry()) titles.Add(reader.Title);

                    _out.WriteLine("titles: " + string.Join(", ", titles.ToArray()) +
                                   "  malformed: " + reader.MalformedLines);

                    Assert.Contains("Good one", titles);
                    Assert.Contains("Good two", titles);
                    Assert.True(reader.MalformedLines > 0,
                        "A junk row must be counted in MalformedLines so a corrupted pack is visible, " +
                        "not silently smaller than it should be.");
                }
            }
        }

        [Fact]
        public void AnEmptyPackReadsCleanly()
        {
            using (var install = new TempInstall("pack_empty"))
            {
                string path = Write(install, new DataPackFixture());

                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.True(reader.Open(path, out error), error);
                    Assert.False(reader.ReadEntry());
                    Assert.Equal(0, reader.MalformedLines);
                }
            }
        }

        [Theory]
        [InlineData("Creator.Pack Name.3", "Creator.PackName.3")]
        [InlineData("Some Creator.Some Pack", "SomeCreator.SomePack")]
        [InlineData("nospaces.here", "nospaces.here")]
        [InlineData("  leading.trailing  ", "leading.trailing")]
        public void VarKeyAliasStripsWhitespaceAndKeepsCase(string varKey, string expected)
        {
            Assert.Equal(expected, VpbDataPackReader.AliasForVarKey(varKey));
        }

        [Theory]
        [InlineData("https://hub.virtamate.com/resources/some-look.12345/", "12345")]
        [InlineData("https://hub.virtamate.com/resources/another.999/", "999")]
        public void HubResourceIdIsParsedFromTheLink(string link, string expected)
        {
            Assert.Equal(expected, VpbDataPackReader.ParseHubResourceIdFromLink(link));
        }

        [Fact]
        public void TheShippedPacksStillOpenWithThisBuild()
        {
            string dir = Path.Combine(Path.Combine(Path.Combine(TestEnvironment.VaMPath, "BepInEx"), "plugins"),
                Path.Combine("VPB", Path.Combine("assets", "datapacks")));

            if (!Directory.Exists(dir))
            {
                _out.WriteLine("no shipped datapacks at " + dir + " - nothing to check");
                return;
            }

            string[] packs = Directory.GetFiles(dir, "*.tsv");
            _out.WriteLine("shipped packs: " + packs.Length);

            foreach (string pack in packs)
            {
                VpbDataPackHeader header;
                Assert.True(VpbDataPackFileProbe.TryReadHeader(pack, out header),
                    Path.GetFileName(pack) + " no longer parses as a data pack header.");

                using (var reader = new VpbDataPackReader())
                {
                    string error;
                    Assert.True(reader.Open(pack, out error),
                        Path.GetFileName(pack) + " (pack_format_version " + header.FormatVersion +
                        ") no longer opens with this build: " + error);

                    int read = 0;
                    while (read < 200 && reader.ReadEntry()) read++;
                    _out.WriteLine("  " + Path.GetFileName(pack) + ": id=" + header.PackId +
                                   " fmt=" + header.FormatVersion + " declared=" + header.EntryCount +
                                   " sampled=" + read + " malformed=" + reader.MalformedLines);

                    Assert.True(read > 0, Path.GetFileName(pack) + " opened but yielded no entries.");
                    Assert.Equal(0, reader.MalformedLines);
                }
            }
        }
    }

    internal static class VpbDataPackFileProbe
    {
        internal static bool TryReadHeader(string path, out VpbDataPackHeader header)
        {
            return VpbDataPackReader.TryReadHeader(path, out header);
        }
    }
}
