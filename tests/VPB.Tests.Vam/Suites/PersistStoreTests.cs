using System;
using System.IO;
using SimpleJSON;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class PersistStoreTests
    {
        public PersistStoreTests(VamFixture vam) { }

        [Fact]
        public void AtomicWrite_rotates_live_file_to_bak()
        {
            using (var install = new TempInstall("atomic"))
            {
                string path = Path.Combine(install.PluginDataDir, "probe.json");
                Assert.True(
                    VpbAtomicTextFile.TryWriteWithBackup(path, "[\"first\"]"),
                    "First persist must land in Saves/PluginData/VPB or VaM loses the lock list after a crash.");
                Assert.True(
                    VpbAtomicTextFile.TryWriteWithBackup(path, "[\"second\"]"),
                    "Second persist must replace the live file without truncating it mid-write.");
                Assert.Equal("[\"second\"]", File.ReadAllText(path));
                Assert.True(File.Exists(path + ".bak"), "Previous persist must sit in .bak so a crashed write can recover.");
                Assert.Equal("[\"first\"]", File.ReadAllText(path + ".bak"));
            }
        }

        [Fact]
        public void AtomicWrite_rejects_empty_payload_without_touching_live()
        {
            using (var install = new TempInstall("atomic-empty"))
            {
                string path = Path.Combine(install.PluginDataDir, "probe.json");
                File.WriteAllText(path, "[\"keep\"]");
                Assert.False(
                    VpbAtomicTextFile.TryWriteWithBackup(path, ""),
                    "Empty serialize must not replace a good persist file.");
                Assert.Equal("[\"keep\"]", File.ReadAllText(path));
            }
        }

        [Fact]
        public void JsonUidSetStore_round_trips_and_recovers_from_bak()
        {
            using (var install = new TempInstall("uid-set"))
            {
                var first = new JsonUidSetStore("locked_packages.json", "LockedPackagesManager");
                first.Set("Creator.Pack.1", true, true);
                first.Set("Creator.Pack.2", true, true);
                Assert.True(
                    first.Contains("Creator.Pack.1"),
                    "Locked package UIDs must stick in memory after SetLocked.");

                string path = Path.Combine(install.PluginDataDir, "locked_packages.json");
                Assert.True(File.Exists(path), "Locked packages must persist next to VPB.cfg.");

                var second = new JsonUidSetStore("locked_packages.json", "LockedPackagesManager");
                Assert.True(
                    second.Contains("Creator.Pack.1"),
                    "Reloading VaM must restore locked packages from disk.");

                File.WriteAllText(path, "{");
                var recovered = new JsonUidSetStore("locked_packages.json", "LockedPackagesManager");
                Assert.True(
                    recovered.Contains("Creator.Pack.1"),
                    "Corrupt locked_packages.json must fall back to .bak so locks do not vanish.");
            }
        }

        [Fact]
        public void JsonUidSetStore_trims_and_ignores_case_when_asked()
        {
            using (var install = new TempInstall("uid-set-ci"))
            {
                string path = Path.Combine(install.PluginDataDir, "dependency_whitelist.json");
                File.WriteAllText(path, "[\"  Creator.Pack  \"]");

                var store = new JsonUidSetStore(
                    "dependency_whitelist.json",
                    "DependencyWhitelistManager",
                    StringComparer.OrdinalIgnoreCase,
                    true);
                Assert.True(
                    store.Contains("creator.pack"),
                    "Force-latest ignore groups must match after trim and case fold or Hub latest-pin keeps breaking.");
                Assert.True(
                    store.Contains("  CREATOR.PACK  "),
                    "Settings UI whitespace around a package group must still hit the same ignore entry.");
            }
        }

        [Fact]
        public void TryReadSharedText_rejects_tiny_files()
        {
            using (var install = new TempInstall("atomic-read"))
            {
                string path = Path.Combine(install.PluginDataDir, "tiny.json");
                File.WriteAllText(path, "x");
                string text;
                Assert.False(
                    VpbAtomicTextFile.TryReadSharedText(path, out text),
                    "A 1-byte persist file is a crash leftover; load must skip it and try .bak.");
            }
        }

        [Fact]
        public void PluginFile_is_under_plugin_data_directory()
        {
            using (var install = new TempInstall("plugin-file"))
            {
                string path = GlobalInfo.PluginFile("VPB.cfg");
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(install.PluginDataDir, "VPB.cfg")),
                    Path.GetFullPath(path));
            }
        }

        [Theory]
        [InlineData("Person", "Person1")]
        [InlineData("InvisiblePerson", "Ghost1")]
        public void FindFirstPersonAtomId_uses_person_like_types(string type, string id)
        {
            var atoms = new JSONArray();
            JSONClass light = new JSONClass();
            light["id"] = "Lamp";
            light["type"] = "InvisibleLight";
            atoms.Add(light);
            JSONClass person = new JSONClass();
            person["id"] = id;
            person["type"] = type;
            atoms.Add(person);
            JSONClass root = new JSONClass();
            root["atoms"] = atoms;

            Assert.Equal(id, SceneUtils.FindFirstPersonAtomId(root));
        }

        [Fact]
        public void FindFirstPersonAtomId_skips_person_without_id()
        {
            var atoms = new JSONArray();
            JSONClass nameless = new JSONClass();
            nameless["type"] = "Person";
            atoms.Add(nameless);
            JSONClass named = new JSONClass();
            named["id"] = "Second";
            named["type"] = "Person";
            atoms.Add(named);

            Assert.Equal(
                "Second",
                SceneUtils.FindFirstPersonAtomId(atoms));
        }
    }
}
