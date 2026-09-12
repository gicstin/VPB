using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class ReleaseIndexTests
    {
        private static readonly Regex VersionShape = new Regex(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
        private static readonly Regex Sha1Shape = new Regex(@"^[0-9a-f]{40}$", RegexOptions.Compiled);

        private readonly ITestOutputHelper _out;
        public ReleaseIndexTests(ITestOutputHelper output) { _out = output; }

        private sealed class Release
        {
            public string Version;
            public string Tag;
            public string Commit;
            public DateTime DateUtc;
            public string Notes;
            public int Schema;
        }

        private static string IndexPath => Repo.Path_("releases", "index.json");

        private static string MinRollbackVersion()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(IndexPath));
            return doc.RootElement.GetProperty("MinRollbackVersion").GetString();
        }

        private static List<Release> ReadReleases(out int indexVersion)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(IndexPath));
            indexVersion = doc.RootElement.GetProperty("IndexVersion").GetInt32();

            var list = new List<Release>();
            foreach (JsonElement e in doc.RootElement.GetProperty("Releases").EnumerateArray())
            {
                list.Add(new Release
                {
                    Version = e.GetProperty("Version").GetString(),
                    Tag = e.GetProperty("Tag").GetString(),
                    Commit = e.GetProperty("Commit").GetString(),
                    DateUtc = DateTime.Parse(e.GetProperty("DateUtc").GetString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                    Notes = e.GetProperty("Notes").GetString(),
                    Schema = e.GetProperty("Schema").GetInt32(),
                });
            }
            return list;
        }

        [Fact]
        public void ReleaseIndexExistsAndIsWellFormed()
        {
            Assert.True(File.Exists(IndexPath),
                "releases/index.json is missing. Rebuild it with scripts/PublishRelease.ps1 -Backfill.");

            var releases = ReadReleases(out int indexVersion);

            Assert.True(indexVersion == 1, "releases/index.json must declare IndexVersion 1.");
            Assert.True(releases.Count > 0, "releases/index.json lists no releases, so the rollback picker would be empty.");

            _out.WriteLine("releases: " + releases.Count);
            _out.WriteLine("newest:   " + releases[0].Version + "  " + releases[0].DateUtc.ToString("u"));
            _out.WriteLine("oldest:   " + releases[releases.Count - 1].Version + "  " + releases[releases.Count - 1].DateUtc.ToString("u"));
        }

        [Fact]
        public void EveryReleaseHasAUsableVersionTagAndCommit()
        {
            var bad = new List<string>();
            foreach (Release r in ReadReleases(out _))
            {
                if (r.Version == null || !VersionShape.IsMatch(r.Version))
                {
                    bad.Add("version '" + r.Version + "' is not n.n.n");
                }
                else
                {
                    bool current = r.Tag != null && Regex.IsMatch(r.Tag, "^build-" + Regex.Escape(r.Version) + "-[0-9a-f]{7,40}$");
                    bool legacy = r.Tag == "build-" + r.Version;
                    if (!current && !legacy)
                        bad.Add(r.Version + ": tag '" + r.Tag + "' is neither 'build-" + r.Version
                            + "-<sha>' nor the legacy 'build-" + r.Version + "'");
                }

                if (r.Commit == null || !Sha1Shape.IsMatch(r.Commit))
                    bad.Add((r.Version ?? "?") + ": commit '" + r.Commit + "' is not a full 40-char sha");
            }

            Assert.True(bad.Count == 0,
                "releases/index.json has entries the updater cannot resolve to a ref:" +
                Environment.NewLine + Repo.Bullets(bad));
        }

        [Fact]
        public void ReleaseTagsContainNoSlash()
        {
            var slashed = ReadReleases(out _)
                .Where(r => r.Tag != null && r.Tag.Contains("/"))
                .Select(r => r.Tag)
                .ToList();

            Assert.True(slashed.Count == 0,
                "Release tags must be flat. The updater interpolates the ref straight into" +
                Environment.NewLine +
                "api.github.com/repos/OWNER/REPO/git/trees/{ref}?recursive=1, so a slash makes the path" +
                Environment.NewLine +
                "git/trees/build/0.32.610 - not an endpoint. It 404s, ParseTreeShas returns null, and the" +
                Environment.NewLine +
                "updater then refetches all 42 shipped files with no checksum verification at all:" +
                Environment.NewLine + Repo.Bullets(slashed));
        }

        [Fact]
        public void EveryReleaseTagNamesADistinctBuild()
        {
            var releases = ReadReleases(out _);

            var shared = releases
                .GroupBy(r => r.Tag, StringComparer.Ordinal)
                .Where(g => g.Select(r => r.Commit).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(g => g.Key + " -> " + string.Join(", ", g.Select(r => r.Commit.Substring(0, 8))))
                .ToList();

            Assert.True(shared.Count == 0,
                "One tag name is claimed by two different commits. The updater resolves a pin by tag, so users would" +
                Environment.NewLine +
                "get a build other than the one the picker described:" + Environment.NewLine + Repo.Bullets(shared));
        }

        [Fact]
        public void ReleaseVersionsAreUniqueAndNewestFirst()
        {
            var releases = ReadReleases(out _);

            var dupes = releases
                .GroupBy(r => r.Version, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + " x" + g.Count())
                .ToList();

            Assert.True(dupes.Count == 0,
                "releases/index.json lists the same version more than once, so a rollback to it is ambiguous:" +
                Environment.NewLine + Repo.Bullets(dupes));

            var outOfOrder = new List<string>();
            for (int i = 1; i < releases.Count; i++)
            {
                if (releases[i].DateUtc > releases[i - 1].DateUtc)
                    outOfOrder.Add(releases[i - 1].Version + " (" + releases[i - 1].DateUtc.ToString("u") + ") is listed before " +
                                   releases[i].Version + " (" + releases[i].DateUtc.ToString("u") + ")");
            }

            Assert.True(outOfOrder.Count == 0,
                "releases/index.json must be newest-first; the version picker shows it in file order:" +
                Environment.NewLine + Repo.Bullets(outOfOrder));
        }

        [Fact]
        public void NoReleaseIsOfferedBelowTheSingleFolderLayoutFloor()
        {
            string minText = MinRollbackVersion();
            Assert.True(minText != null && VersionShape.IsMatch(minText),
                "releases/index.json must declare MinRollbackVersion as n.n.n (got '" + minText + "').");

            var min = Version.Parse(minText);
            var tooOld = ReadReleases(out _)
                .Where(r => VersionShape.IsMatch(r.Version ?? "") && Version.Parse(r.Version) < min)
                .Select(r => r.Version)
                .ToList();

            Assert.True(tooOld.Count == 0,
                "releases/index.json offers builds below MinRollbackVersion " + minText + ". 0.32.406 moved the shipped" +
                Environment.NewLine +
                "tree into the single BepInEx/plugins/VPB folder, and the patcher prune plus the VpbLegacyLayout sweep" +
                Environment.NewLine +
                "only migrate forward - rolling an install back across that boundary is unsupported:" +
                Environment.NewLine + Repo.Bullets(tooOld));
        }

        [Fact]
        public void RetentionKeepsTheIndexBounded()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(IndexPath));

            if (!doc.RootElement.TryGetProperty("Keep", out JsonElement keepElement)) return;
            int keep = keepElement.GetInt32();
            if (keep <= 0) return;

            int count = doc.RootElement.GetProperty("Releases").GetArrayLength();
            _out.WriteLine("releases " + count + " / keep " + keep);

            Assert.True(count <= keep,
                "releases/index.json holds " + count + " releases but declares Keep=" + keep + "." +
                Environment.NewLine +
                "Every client fetches this file on every check, so an unbounded list is a growing download for" +
                Environment.NewLine +
                "everyone. Re-run scripts/PublishRelease.ps1 to apply retention.");
        }

        [Fact]
        public void SchemaVersionsNeverGoBackwardsOverTime()
        {
            var known = ReadReleases(out _)
                .Where(r => r.Schema > 0)
                .OrderBy(r => r.DateUtc)
                .ToList();

            var regressions = new List<string>();
            for (int i = 1; i < known.Count; i++)
            {
                if (known[i].Schema < known[i - 1].Schema)
                    regressions.Add(known[i - 1].Version + " schema " + known[i - 1].Schema + " -> " +
                                    known[i].Version + " schema " + known[i].Schema);
            }

            int unknown = ReadReleases(out _).Count(r => r.Schema == 0);
            _out.WriteLine("releases with an unknown schema (recorded as 0): " + unknown);
            _out.WriteLine("a rollback must treat 0 as unknown, not as older than the local database");

            if (regressions.Count > 0)
            {
                _out.WriteLine("schema goes backwards between these releases (expected after merging a branch that ran ahead):");
                foreach (string r in regressions) _out.WriteLine("  " + r);
            }
        }
    }
}
