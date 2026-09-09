using System.Collections.Generic;
using System.IO;
using VPB.Shared;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class UpdaterPatcherTests
    {
        public UpdaterPatcherTests(VamFixture vam) { }

        [Fact]
        public void NestedPluginDllIsOwnedAsVpbDllNotAPluginsRootFile()
        {
            var files = new List<string>();
            var dirs = new List<string>();
            bool ok = VpbUpdateManifest.TryReadOwnedManifest(
                "[" +
                "{\"RelativePath\":\"BepInEx/plugins/VPB\",\"IsDirectory\":true}," +
                "{\"RelativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"IsDirectory\":false}," +
                "{\"RelativePath\":\"BepInEx/plugins/I18N.dll\",\"IsDirectory\":false}" +
                "]",
                files, dirs);

            Assert.True(ok, "Shipped patch_manifest.json must parse; a miss means the patcher skips prune and leftover files pile up.");
            Assert.Contains("vpb.dll", files);
            Assert.DoesNotContain("vpb", files);
            Assert.DoesNotContain("VPB", dirs);
            Assert.DoesNotContain("i18n.dll", files);
        }

        [Fact]
        public void NestedAssetPathKeepsFoldersUnderThePluginDir()
        {
            var files = new List<string>();
            var dirs = new List<string>();
            VpbUpdateManifest.TryReadOwnedManifest(
                "[{\"RelativePath\":\"BepInEx/plugins/VPB/assets/help/en.md\",\"IsDirectory\":false}]",
                files, dirs);

            Assert.Contains("assets/help/en.md", files);
        }

        [Fact]
        public void MultiplayerManifestMustNotTriggerNetFolderSweep()
        {
            var files = new List<string>();
            var dirs = new List<string>();
            VpbUpdateManifest.TryReadOwnedManifest(
                "[" +
                "{\"RelativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"IsDirectory\":false}," +
                "{\"RelativePath\":\"BepInEx/plugins/VPB/net\",\"IsDirectory\":true}," +
                "{\"RelativePath\":\"BepInEx/plugins/VPB/net/VpbNet.exe\",\"IsDirectory\":false}" +
                "]",
                files, dirs);

            Assert.False(VpbUpdateManifest.ShouldSweepMpOnly(files, dirs),
                "Updating onto the multiplayer branch ships net/VpbNet.exe. Sweeping that folder on the next launch " +
                "deletes the companion the just-applied plugin needs, so the in-game updater looks like it did nothing.");
        }

        [Fact]
        public void ExperimentsManifestStillSweepsLeftoverNet()
        {
            var files = new List<string>();
            var dirs = new List<string>();
            VpbUpdateManifest.TryReadOwnedManifest(
                "[{\"RelativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"IsDirectory\":false}]",
                files, dirs);

            Assert.True(VpbUpdateManifest.ShouldSweepMpOnly(files, dirs),
                "Switching back to experiments must still retire leftover net/ so a stale multiplayer companion does not keep running.");
        }

        [Fact]
        public void PendingParserReadsNestedPluginTargets()
        {
            VpbUpdateManifest.PendingUpdate pending = VpbUpdateManifest.ParsePending(
                "{" +
                "\"version\":\"0.32.790\"," +
                "\"branch\":\"multiplayer\"," +
                "\"files\":[" +
                "{\"relativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"stagedFileName\":\"a.tmp\",\"sha\":\"abc\"}," +
                "{\"relativePath\":\"BepInEx/patchers/VPB.Patcher.dll\",\"stagedFileName\":\"b.tmp\",\"sha\":\"def\"}" +
                "]}");

            Assert.Equal("0.32.790", pending.Version);
            Assert.Equal("multiplayer", pending.Branch);
            Assert.Equal(2, pending.Files.Count);
            Assert.Equal("BepInEx/plugins/VPB/VPB.dll", pending.Files[0].RelativePath);
            Assert.Equal("BepInEx/patchers/VPB.Patcher.dll", pending.Files[1].RelativePath);
        }

        [Fact]
        public void ApplyPendingCreatesThePluginSubfolderAndMovesTheDll()
        {
            using (var install = new TempInstall("upd_apply"))
            {
                string staging = VpbUpdateManifest.NewStagingDir(VpbUpdateManifest.PluginsDir(install.Root));
                string filesDir = Path.Combine(staging, "files");
                Directory.CreateDirectory(filesDir);
                File.WriteAllText(Path.Combine(filesDir, "staged.tmp"), "new-dll");
                File.WriteAllText(VpbUpdateManifest.PendingPath(staging),
                    "{\"version\":\"1\",\"branch\":\"main\",\"files\":[" +
                    "{\"relativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"stagedFileName\":\"staged.tmp\",\"sha\":\"\"}]}");

                VpbUpdateManifest.ApplyResult result = VpbUpdateManifest.ApplyPendingUpdate(
                    install.Root, staging, null, null, null);

                string dest = install.PathTo("BepInEx", "plugins", "VPB", "VPB.dll");
                Assert.True(result.HadPending, "Patcher must see pending.json under BepInEx/plugins/VPB/vpb_update_staging after the subfolder move.");
                Assert.Equal(1, result.Applied);
                Assert.True(File.Exists(dest), "Staged VPB.dll must land in BepInEx/plugins/VPB/, not the plugins root.");
                Assert.Equal("new-dll", File.ReadAllText(dest));
                Assert.False(File.Exists(VpbUpdateManifest.PendingPath(staging)),
                    "Successful apply must consume pending.json so the next launch does not re-apply.");
            }
        }

        [Fact]
        public void LegacyStagingDirIsWhereOldPatchersLook()
        {
            using (var install = new TempInstall("upd_stage"))
            {
                string plugins = VpbUpdateManifest.PluginsDir(install.Root);
                string neu = VpbUpdateManifest.NewStagingDir(plugins);
                string old = VpbUpdateManifest.LegacyStagingDir(plugins);

                Assert.Equal(Path.Combine(Path.Combine(plugins, "VPB"), "vpb_update_staging"), neu);
                Assert.Equal(Path.Combine(plugins, "vpb_update_staging"), old);
                Assert.NotEqual(neu, old);
            }
        }

        [Fact]
        public void GitHubRawPathEncodesSpacesButKeepsFolders()
        {
            string encoded = VpbUpdateManifest.EncodeGitHubRawPath("VaM (Log Mode).bat");
            Assert.Equal("VaM%20%28Log%20Mode%29.bat", encoded);

            string nested = VpbUpdateManifest.EncodeGitHubRawPath("BepInEx/plugins/VPB/VPB.dll");
            Assert.Equal("BepInEx/plugins/VPB/VPB.dll", nested);
        }

        [Fact]
        public void CopyStagingLeavesAPendingTheOldPatcherCanApply()
        {
            using (var install = new TempInstall("upd_copy"))
            {
                string plugins = VpbUpdateManifest.PluginsDir(install.Root);
                string neu = VpbUpdateManifest.NewStagingDir(plugins);
                string old = VpbUpdateManifest.LegacyStagingDir(plugins);
                Directory.CreateDirectory(Path.Combine(neu, "files"));
                File.WriteAllText(VpbUpdateManifest.PendingPath(neu), "{\"files\":[]}");
                File.WriteAllText(Path.Combine(neu, "files", "x.tmp"), "x");

                Assert.True(VpbUpdateManifest.CopyStaging(neu, old),
                    "Old VPB.Patcher.dll only looks in BepInEx/plugins/vpb_update_staging. Without a copy there, a staged update never applies.");
                Assert.True(VpbUpdateManifest.StagingHasPending(old));
                Assert.True(File.Exists(Path.Combine(old, "files", "x.tmp")));
            }
        }

        [Fact]
        public void ApplyStagedUpdatesPrefersNewStagingAndClearsLegacyMirror()
        {
            using (var install = new TempInstall("upd_apply_new"))
            {
                string plugins = VpbUpdateManifest.PluginsDir(install.Root);
                string neu = VpbUpdateManifest.NewStagingDir(plugins);
                string old = VpbUpdateManifest.LegacyStagingDir(plugins);
                Directory.CreateDirectory(Path.Combine(neu, "files"));
                File.WriteAllText(Path.Combine(neu, "files", "staged.tmp"), "new-dll");
                File.WriteAllText(VpbUpdateManifest.PendingPath(neu),
                    "{\"version\":\"1\",\"branch\":\"main\",\"files\":[" +
                    "{\"relativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"stagedFileName\":\"staged.tmp\",\"sha\":\"\"}]}");
                Assert.True(VpbUpdateManifest.CopyStaging(neu, old));

                VpbUpdateManifest.ApplyResult result = VpbUpdateManifest.ApplyStagedUpdates(
                    install.Root, null, null, null);

                Assert.True(result.HadPending);
                Assert.Equal(1, result.Applied);
                Assert.True(File.Exists(install.PathTo("BepInEx", "plugins", "VPB", "VPB.dll")));
                Assert.False(VpbUpdateManifest.StagingHasPending(neu));
                Assert.False(Directory.Exists(old),
                    "After new staging apply, legacy mirror must go or plugins/vpb_update_staging keeps a stale pending forever.");
            }
        }

        [Fact]
        public void ApplyStagedUpdatesFallsBackToLegacyWhenNewStagingMissing()
        {
            using (var install = new TempInstall("upd_apply_old"))
            {
                string plugins = VpbUpdateManifest.PluginsDir(install.Root);
                string neu = VpbUpdateManifest.NewStagingDir(plugins);
                string old = VpbUpdateManifest.LegacyStagingDir(plugins);
                Directory.CreateDirectory(Path.Combine(old, "files"));
                File.WriteAllText(Path.Combine(old, "files", "staged.tmp"), "from-legacy");
                File.WriteAllText(VpbUpdateManifest.PendingPath(old),
                    "{\"version\":\"1\",\"branch\":\"main\",\"files\":[" +
                    "{\"relativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"stagedFileName\":\"staged.tmp\",\"sha\":\"\"}]}");
                Directory.CreateDirectory(neu);

                VpbUpdateManifest.ApplyResult result = VpbUpdateManifest.ApplyStagedUpdates(
                    install.Root, null, null, null);

                Assert.True(result.HadPending,
                    "Old VPB.Patcher.dll only wrote plugins/vpb_update_staging; new patcher must still apply that pending.");
                Assert.Equal(1, result.Applied);
                Assert.Equal("from-legacy", File.ReadAllText(install.PathTo("BepInEx", "plugins", "VPB", "VPB.dll")));
                Assert.False(VpbUpdateManifest.StagingHasPending(old));
                Assert.False(Directory.Exists(neu),
                    "Successful legacy apply must clear leftover plugins/VPB/vpb_update_staging so the next boot does not re-apply.");
            }
        }

        [Fact]
        public void EmptyNewPendingMustNotWipeAGoodLegacyMirror()
        {
            using (var install = new TempInstall("upd_empty_new"))
            {
                string plugins = VpbUpdateManifest.PluginsDir(install.Root);
                string neu = VpbUpdateManifest.NewStagingDir(plugins);
                string old = VpbUpdateManifest.LegacyStagingDir(plugins);
                Directory.CreateDirectory(neu);
                File.WriteAllText(VpbUpdateManifest.PendingPath(neu), "{\"version\":\"1\",\"files\":[]}");
                Directory.CreateDirectory(Path.Combine(old, "files"));
                File.WriteAllText(Path.Combine(old, "files", "staged.tmp"), "keep-me");
                File.WriteAllText(VpbUpdateManifest.PendingPath(old),
                    "{\"version\":\"1\",\"branch\":\"main\",\"files\":[" +
                    "{\"relativePath\":\"BepInEx/plugins/VPB/VPB.dll\",\"stagedFileName\":\"staged.tmp\",\"sha\":\"\"}]}");

                VpbUpdateManifest.ApplyResult result = VpbUpdateManifest.ApplyStagedUpdates(
                    install.Root, null, null, null);

                Assert.True(result.HadPending);
                Assert.Equal(1, result.Applied);
                Assert.Equal("keep-me", File.ReadAllText(install.PathTo("BepInEx", "plugins", "VPB", "VPB.dll")));
            }
        }
    }
}
