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
    public class AssetLinkTests
    {
        private static readonly Regex IconCall = new Regex(@"LoadIconSprite\(\s*""([^""]+)""", RegexOptions.Compiled);

        private readonly ITestOutputHelper _out;
        public AssetLinkTests(ITestOutputHelper output) { _out = output; }

        private static Dictionary<string, string> IconMap()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Path_("assets", "icons", "icon_map.json")));
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                map[p.Name] = p.Value.GetString();
            return map;
        }

        private static string ResolveIconFile(string mapValue)
        {
            string direct = Repo.Path_("assets", "icons", mapValue.Replace('/', Path.DirectorySeparatorChar) + ".svg");
            if (File.Exists(direct)) return direct;
            string tabler = Repo.Path_("assets", "icons", "tabler", mapValue + ".svg");
            if (File.Exists(tabler)) return tabler;
            return null;
        }

        private static List<KeyValuePair<string, string>> IconReferencesInCode()
        {
            var refs = new List<KeyValuePair<string, string>>();
            foreach (SourceFile f in SourceIndex.Files)
            {
                foreach (Match m in IconCall.Matches(f.Text))
                {
                    if (f.IsInert(m.Index)) continue;
                    refs.Add(new KeyValuePair<string, string>(m.Groups[1].Value, f.RelativePath + ":" + f.LineOf(m.Index)));
                }
            }
            return refs;
        }

        [Fact]
        public void EveryIconReferencedByCodeExistsInIconMap()
        {
            var map = IconMap();
            var dangling = IconReferencesInCode()
                .Where(r => !map.ContainsKey(r.Key))
                .Select(r => r.Key + "   (" + r.Value + ")")
                .Distinct()
                .ToList();

            Assert.True(dangling.Count == 0,
                "UI.LoadIconSprite() is called with names that assets/icons/icon_map.json does not define." + Environment.NewLine +
                "At runtime these render as a blank button with no error:" + Environment.NewLine +
                Repo.Bullets(dangling));
        }

        [Fact]
        public void EveryIconMapEntryResolvesToAnSvgOnDisk()
        {
            var broken = IconMap()
                .Where(kv => ResolveIconFile(kv.Value) == null)
                .Select(kv => kv.Key + " -> " + kv.Value + ".svg")
                .ToList();

            Assert.True(broken.Count == 0,
                "assets/icons/icon_map.json points at SVG files that do not exist:" + Environment.NewLine +
                Repo.Bullets(broken));
        }

        [Fact]
        public void EverySvgOnDiskIsClaimedByIconMap()
        {
            var claimed = new HashSet<string>(
                IconMap().Values.Select(v => ResolveIconFile(v)).Where(p => p != null),
                StringComparer.OrdinalIgnoreCase);

            var orphans = Directory
                .GetFiles(Repo.Path_("assets", "icons"), "*.svg", SearchOption.AllDirectories)
                .Where(p => !claimed.Contains(p))
                .Select(Repo.ToRepoRelative)
                .ToList();

            Assert.True(orphans.Count == 0,
                "SVG files ship in assets/icons but no icon_map.json entry references them (dead weight in the atlas):" +
                Environment.NewLine + Repo.Bullets(orphans));
        }

        [Fact]
        public void UnusedIconMapEntriesAreReported()
        {
            var used = new HashSet<string>(IconReferencesInCode().Select(r => r.Key), StringComparer.Ordinal);
            var unused = IconMap().Keys.Where(k => !used.Contains(k)).OrderBy(k => k).ToList();

            _out.WriteLine("icon_map entries never passed to LoadIconSprite(): " + unused.Count);
            foreach (string u in unused) _out.WriteLine("  " + u);
            _out.WriteLine("(informational - icons may be referenced from data or reserved for upcoming UI)");
        }

        [Fact]
        public void PatchManifestMatchesDisk()
        {
            string manifestPath = Repo.Path_("vam_patch", "patch_manifest.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));

            var missing = new List<string>();
            int entries = 0;
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                entries++;
                string rel = e.GetProperty("RelativePath").GetString();
                bool isDir = e.TryGetProperty("IsDirectory", out JsonElement d) && d.GetBoolean();
                string full = Repo.Path_("vam_patch", rel.Replace('/', Path.DirectorySeparatorChar));
                bool ok = isDir ? Directory.Exists(full) : File.Exists(full);
                if (!ok) missing.Add((isDir ? "dir  " : "file ") + rel);
            }

            _out.WriteLine("patch_manifest.json entries: " + entries);
            Assert.True(missing.Count == 0,
                "vam_patch/patch_manifest.json lists entries that are not in vam_patch/ - the patcher would fail" +
                Environment.NewLine + "or silently skip them on a user's install:" + Environment.NewLine +
                Repo.Bullets(missing));
        }
    }
}
