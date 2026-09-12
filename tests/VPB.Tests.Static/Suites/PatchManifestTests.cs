using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class PatchManifestTests
    {
        private static readonly Regex SchemaConst =
            new Regex(@"const\s+int\s+SchemaVersion\s*=\s*(\d+)\s*;", RegexOptions.Compiled);

        private static readonly Regex VersionShape =
            new Regex(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

        private readonly ITestOutputHelper _out;
        public PatchManifestTests(ITestOutputHelper output) { _out = output; }

        private sealed class Row
        {
            public string RelativePath;
            public bool IsDirectory;
            public string Sha1;
            public long Size;
        }

        private static string V1Path => Repo.Path_("vam_patch", "patch_manifest.json");
        private static string V2Path => Repo.Path_("vam_patch", "patch_manifest2.json");

        private static List<Row> ReadV1()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(V1Path));
            var rows = new List<Row>();
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                rows.Add(new Row
                {
                    RelativePath = e.GetProperty("RelativePath").GetString(),
                    IsDirectory = e.TryGetProperty("IsDirectory", out JsonElement d) && d.GetBoolean(),
                });
            }
            return rows;
        }

        private static JsonDocument ReadV2Doc() => JsonDocument.Parse(File.ReadAllText(V2Path));

        private static List<Row> ReadV2(JsonDocument doc)
        {
            var rows = new List<Row>();
            foreach (JsonElement e in doc.RootElement.GetProperty("Files").EnumerateArray())
            {
                rows.Add(new Row
                {
                    RelativePath = e.GetProperty("RelativePath").GetString(),
                    IsDirectory = e.GetProperty("IsDirectory").GetBoolean(),
                    Sha1 = e.GetProperty("Sha1").GetString(),
                    Size = e.GetProperty("Size").GetInt64(),
                });
            }
            return rows;
        }

        private static Dictionary<string, string> GitBlobSha1(IReadOnlyList<string> repoRelativePaths)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (repoRelativePaths.Count == 0) return map;

            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = Repo.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("hash-object");
            psi.ArgumentList.Add("--");
            foreach (string p in repoRelativePaths) psi.ArgumentList.Add(p);

            using Process proc = Process.Start(psi);
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException("git hash-object failed: " + proc.StandardError.ReadToEnd());

            string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != repoRelativePaths.Count)
                throw new InvalidOperationException(
                    "git hash-object returned " + lines.Length + " hashes for " + repoRelativePaths.Count + " paths.");

            for (int i = 0; i < lines.Length; i++)
                map[repoRelativePaths[i]] = lines[i].Trim();

            return map;
        }

        private static IEnumerable<string> ShippedFilesOnDisk()
        {
            string root = Repo.Path_("vam_patch");
            return Directory
                .GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(root.Length + 1).Replace('\\', '/'));
        }

        [Fact]
        public void ManifestV1KeepsTheShapeShippedClientsParse()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(V1Path));

            Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array,
                "vam_patch/patch_manifest.json must stay a JSON array. Every VPB install in the field parses this" +
                Environment.NewLine +
                "file to find its update, so a shape change strands them with no remote fix. Ship new fields in" +
                Environment.NewLine +
                "patch_manifest2.json instead.");

            var offenders = new List<string>();
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                var names = e.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                if (names.Count != 2 || names[0] != "IsDirectory" || names[1] != "RelativePath")
                    offenders.Add(e.TryGetProperty("RelativePath", out JsonElement rp)
                        ? rp.GetString() + "   [" + string.Join(", ", names) + "]"
                        : "(no RelativePath)   [" + string.Join(", ", names) + "]");
            }

            Assert.True(offenders.Count == 0,
                "patch_manifest.json rows must carry exactly RelativePath + IsDirectory:" +
                Environment.NewLine + Repo.Bullets(offenders));
        }

        [Fact]
        public void ManifestV2ExistsAndCoversExactlyTheShippedList()
        {
            Assert.True(File.Exists(V2Path),
                "vam_patch/patch_manifest2.json is missing. Run scripts/BuildPatchManifest.ps1 (a normal build does" +
                Environment.NewLine + "this via PostBuildDeploy.ps1).");

            var v1 = ReadV1();
            using var doc = ReadV2Doc();
            var v2 = ReadV2(doc);

            var v1Keys = new HashSet<string>(v1.Select(r => (r.IsDirectory ? "dir  " : "file ") + r.RelativePath), StringComparer.Ordinal);
            var v2Keys = new HashSet<string>(v2.Select(r => (r.IsDirectory ? "dir  " : "file ") + r.RelativePath), StringComparer.Ordinal);

            var onlyV1 = v1Keys.Except(v2Keys).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var onlyV2 = v2Keys.Except(v1Keys).OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.True(onlyV1.Count == 0 && onlyV2.Count == 0,
                "patch_manifest2.json has drifted from patch_manifest.json - regenerate it with" +
                Environment.NewLine + "scripts/BuildPatchManifest.ps1." + Environment.NewLine +
                (onlyV1.Count > 0 ? "Only in patch_manifest.json:" + Environment.NewLine + Repo.Bullets(onlyV1) + Environment.NewLine : "") +
                (onlyV2.Count > 0 ? "Only in patch_manifest2.json:" + Environment.NewLine + Repo.Bullets(onlyV2) : ""));
        }

        [Fact]
        public void ManifestV2HashesAndSizesMatchDisk()
        {
            using var doc = ReadV2Doc();
            var stale = new List<string>();
            var rows = new List<Row>();
            var paths = new List<string>();

            foreach (Row row in ReadV2(doc))
            {
                if (row.IsDirectory) continue;

                string full = Repo.Path_("vam_patch", row.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    stale.Add(row.RelativePath + "   (not on disk)");
                    continue;
                }

                rows.Add(row);
                paths.Add("vam_patch/" + row.RelativePath);
            }

            Dictionary<string, string> actual = GitBlobSha1(paths);

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                string sha = actual[paths[i]];
                if (!string.Equals(sha, row.Sha1, StringComparison.OrdinalIgnoreCase))
                    stale.Add(row.RelativePath + "   (sha1 " + row.Sha1 + " recorded, git says " + sha + ")");
            }

            _out.WriteLine("patch_manifest2.json files hashed: " + rows.Count);
            Assert.True(stale.Count == 0,
                "patch_manifest2.json no longer describes vam_patch/. The updater verifies downloads against these" +
                Environment.NewLine +
                "hashes, so shipping them stale makes every client reject the file as corrupt. Regenerate with" +
                Environment.NewLine + "scripts/BuildPatchManifest.ps1:" + Environment.NewLine + Repo.Bullets(stale));
        }

        [Fact]
        public void ManifestV2CarriesAUsableVersionAndSchema()
        {
            using var doc = ReadV2Doc();
            JsonElement root = doc.RootElement;

            Assert.True(root.GetProperty("ManifestVersion").GetInt32() == 2,
                "patch_manifest2.json must declare ManifestVersion 2.");

            string version = root.GetProperty("Version").GetString();
            Assert.True(version != null && VersionShape.IsMatch(version),
                "patch_manifest2.json Version must look like 0.32.610 (got '" + version + "'). It labels the build a" +
                Environment.NewLine + "user rolls back to, so a malformed value mislabels every entry in the release list.");

            string baseLine = File.ReadAllLines(Repo.Path_("plugin_version.txt"))
                .Select(l => l.Trim()).First(l => l.Length > 0).TrimStart('v', 'V');
            Assert.True(version.StartsWith(baseLine + ".", StringComparison.Ordinal),
                "patch_manifest2.json Version '" + version + "' does not sit on the base version '" + baseLine +
                "' from plugin_version.txt.");

            int declared = root.GetProperty("Schema").GetInt32();
            var m = SchemaConst.Match(File.ReadAllText(Repo.Path_("src", "gallery", "index", "VpbLocalDatabase.cs")));
            Assert.True(m.Success, "Could not find 'const int SchemaVersion' in src/gallery/index/VpbLocalDatabase.cs.");
            int actual = int.Parse(m.Groups[1].Value);

            if (declared < actual)
                _out.WriteLine("manifest schema " + declared + " lags source " + actual + " (expected until the next build ships)");

            Assert.True(declared <= actual,
                "patch_manifest2.json records schema " + declared + " but VpbLocalDatabase.SchemaVersion is only " + actual +
                "." + Environment.NewLine +
                "The manifest describes the binary that shipped, so it may lag the source between builds - but it can" +
                Environment.NewLine +
                "never lead it. A higher value means the manifest was hand-edited or generated from another tree, and" +
                Environment.NewLine +
                "a rollback would then under-report the downgrade risk. Regenerate with scripts/BuildPatchManifest.ps1.");
        }

        [Fact]
        public void EveryFileStagedInVamPatchIsListedOrAllowlisted()
        {
            var listed = new HashSet<string>(
                ReadV1().Where(r => !r.IsDirectory).Select(r => r.RelativePath),
                StringComparer.OrdinalIgnoreCase);

            var allowed = new HashSet<string>(
                Repo.ReadAllowlist("known-unshipped-patch-files.txt"),
                StringComparer.OrdinalIgnoreCase);

            allowed.Add("patch_manifest.json");
            allowed.Add("patch_manifest2.json");

            var unlisted = ShippedFilesOnDisk()
                .Where(p => !listed.Contains(p) && !allowed.Contains(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            Assert.True(unlisted.Count == 0,
                "Files are staged in vam_patch/ but no patch_manifest.json row names them, so they never reach a" +
                Environment.NewLine +
                "user install and the patcher prunes them from anyone who has them. Add a manifest row, or list" +
                Environment.NewLine +
                "the path in tests/known-unshipped-patch-files.txt if that is deliberate:" +
                Environment.NewLine + Repo.Bullets(unlisted));
        }
    }
}
