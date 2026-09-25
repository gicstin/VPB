using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class NativePackageListingTests
    {
        public NativePackageListingTests(VamFixture vam) { }

        [Fact]
        public void VamRefreshStillScansThePackageFolderThroughBothRedirectableCalls()
        {
            MethodInfo refresh = AccessTools.Method(typeof(MVR.FileManagement.FileManager), "Refresh", Type.EmptyTypes);
            Assert.NotNull(refresh);

            List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(refresh);
            List<CodeInstruction> rewritten = VamNativePackageListing.RedirectPackageFolderListing(original).ToList();

            var callees = rewritten
                .Select(i => i.operand as MethodInfo)
                .Where(m => m != null)
                .ToList();

            Assert.True(VamNativePackageListing.RedirectedCallSites >= 2,
                "FileManager.Refresh no longer calls Directory.GetFiles/GetDirectories(path, pattern, SearchOption) the way VPB " +
                "expects, so every native package refresh walks all of AddonPackages again and offers each scan-excluded " +
                "package to the scan filter. Redirected " + VamNativePackageListing.RedirectedCallSites + " call sites.");
            Assert.Contains(callees, m => m.DeclaringType == typeof(VamNativePackageListing) && m.Name == "ListFiles");
            Assert.Contains(callees, m => m.DeclaringType == typeof(VamNativePackageListing) && m.Name == "ListDirectories");
        }

        [Fact]
        public void ScanExcludedUnregisteredPackagesAreDroppedButOneStaysSoVamStillRebuildsItsCatalogs()
        {
            string[] entries =
            {
                "AddonPackages\\Allowed.One.1.var",
                "AddonPackages\\Blocked.A.1.var",
                "AddonPackages\\Blocked.B.1.var",
                "AddonPackages\\Blocked.C.1.var",
                "AddonPackages\\Allowed.Two.1.var"
            };
            bool[] blocked = { false, true, true, true, false };

            int skipped;
            string[] kept = VamNativePackageListing.KeepRegistrableEntries(
                entries, blocked, new Dictionary<string, MVR.FileManagement.VarPackage>(), out skipped);

            Assert.Equal(new[]
            {
                "AddonPackages\\Allowed.One.1.var",
                "AddonPackages\\Blocked.A.1.var",
                "AddonPackages\\Allowed.Two.1.var"
            }, kept);
            Assert.True(skipped == 2,
                "Scan-excluded packages must be reported as blocked even when VaM never sees them, or the scan filter " +
                "summary under-reports what the whitelist held back.");
        }

        [Fact]
        public void ARegisteredPackageStaysListedEvenWhenTheWhitelistNoLongerAllowsIt()
        {
            string[] entries =
            {
                "AddonPackages\\Blocked.First.1.var",
                "AddonPackages\\OnDemand.Loaded.1.var",
                "AddonPackages\\Blocked.Last.1.var"
            };
            bool[] blocked = { true, true, true };
            var registered = new Dictionary<string, MVR.FileManagement.VarPackage>
            {
                { "AddonPackages\\OnDemand.Loaded.1.var", null }
            };

            int skipped;
            string[] kept = VamNativePackageListing.KeepRegistrableEntries(entries, blocked, registered, out skipped);

            Assert.True(kept.Contains("AddonPackages\\OnDemand.Loaded.1.var"),
                "A package VPB registered on demand vanished from VaM's listing, so VaM's Refresh unregisters it and the " +
                "scene that needed it loses its clothing, morphs or scripts mid-session.");
            Assert.Equal(1, skipped);
        }

        [Fact]
        public void NothingIsDroppedWhenNoPackageIsScanExcluded()
        {
            string[] entries = { "AddonPackages\\A.B.1.var", "AddonPackages\\C.D.2.var" };

            int skipped;
            string[] kept = VamNativePackageListing.KeepRegistrableEntries(
                entries, new bool[entries.Length], null, out skipped);

            Assert.Equal(entries, kept);
            Assert.Equal(0, skipped);
        }

        [Fact]
        public void AnUnchangedPackageFolderIsServedFromTheListingCacheAndAnAddedPackageInvalidatesIt()
        {
            using (var install = new TempInstall("native_listing"))
            {
                ScanWhitelistManager.Instance.SetEnabled(false);
                try
                {
                    VamNativePackageListing.ResetForTests();
                    string sub = install.EnsureDir("AddonPackages", "Creator");
                    File.WriteAllText(Path.Combine(install.AddonPackagesDir, "A.First.1.var"), "x");
                    File.WriteAllText(Path.Combine(sub, "B.Nested.1.var"), "x");
                    AgeDirectories(install.AddonPackagesDir, sub);

                    string[] first = VamNativePackageListing.ListFiles("AddonPackages", "*.var", SearchOption.AllDirectories);
                    Assert.False(VamNativePackageListing.LastListingWasCached);
                    Assert.Equal(2, first.Length);

                    string[] second = VamNativePackageListing.ListFiles("AddonPackages", "*.var", SearchOption.AllDirectories);
                    Assert.True(VamNativePackageListing.LastListingWasCached,
                        "An unchanged AddonPackages folder was walked again, so every VaM package refresh still pays the full " +
                        "directory scan.");
                    Assert.Equal(first.OrderBy(p => p), second.OrderBy(p => p));

                    File.WriteAllText(Path.Combine(sub, "C.Added.1.var"), "x");
                    string[] third = VamNativePackageListing.ListFiles("AddonPackages", "*.var", SearchOption.AllDirectories);
                    Assert.False(VamNativePackageListing.LastListingWasCached,
                        "A package copied into a subfolder of AddonPackages was hidden behind a stale listing, so VaM's rescan " +
                        "never registers it.");
                    Assert.Equal(3, third.Length);
                }
                finally
                {
                    VamNativePackageListing.ResetForTests();
                    ScanWhitelistManager.Reload();
                }
            }
        }

        [Fact]
        public void APrewarmedListingServesVamsFirstRefreshWithoutAnotherFolderWalk()
        {
            using (var install = new TempInstall("native_listing_prewarm"))
            {
                ScanWhitelistManager.Instance.SetEnabled(false);
                try
                {
                    VamNativePackageListing.ResetForTests();
                    File.WriteAllText(Path.Combine(install.AddonPackagesDir, "A.Warm.1.var"), "x");
                    File.WriteAllText(Path.Combine(install.AddonPackagesDir, "B.Warm.2.var"), "x");
                    AgeDirectories(install.AddonPackagesDir);

                    Assert.True(VamNativePackageListing.Prewarm("AddonPackages"),
                        "The startup prewarm did not store a listing, so VaM's first refresh walks AddonPackages on the main thread.");

                    string[] dirs = VamNativePackageListing.ListDirectories("AddonPackages", "*.var", SearchOption.AllDirectories);
                    Assert.True(VamNativePackageListing.LastListingWasCached);
                    Assert.Empty(dirs);

                    string[] files = VamNativePackageListing.ListFiles("AddonPackages", "*.var", SearchOption.AllDirectories);
                    Assert.True(VamNativePackageListing.LastListingWasCached);
                    Assert.Equal(
                        new[] { "A.Warm.1.var", "B.Warm.2.var" },
                        files.Select(Path.GetFileName).OrderBy(n => n).ToArray());
                }
                finally
                {
                    VamNativePackageListing.ResetForTests();
                    ScanWhitelistManager.Reload();
                }
            }
        }

        [Fact]
        public void PrewarmLeavesNothingBehindWhenThePackageFolderIsMissing()
        {
            using (new TempInstall("native_listing_prewarm_missing"))
            {
                try
                {
                    VamNativePackageListing.ResetForTests();
                    Assert.False(VamNativePackageListing.Prewarm("NoSuchPackageFolder"));
                }
                finally
                {
                    VamNativePackageListing.ResetForTests();
                }
            }
        }

        [Fact]
        public void AFolderChangedMomentsAgoIsNeverCachedBecauseItsTimestampCannotProveLaterChanges()
        {
            using (var install = new TempInstall("native_listing_fresh"))
            {
                ScanWhitelistManager.Instance.SetEnabled(false);
                try
                {
                    VamNativePackageListing.ResetForTests();
                    File.WriteAllText(Path.Combine(install.AddonPackagesDir, "A.Fresh.1.var"), "x");

                    VamNativePackageListing.ListFiles("AddonPackages", "*.var", SearchOption.AllDirectories);
                    VamNativePackageListing.ListFiles("AddonPackages", "*.var", SearchOption.AllDirectories);

                    Assert.False(VamNativePackageListing.LastListingWasCached,
                        "A listing was cached while the folder's timestamp was still within the filesystem's resolution, so " +
                        "a package added in the same tick could stay invisible to VaM.");
                }
                finally
                {
                    VamNativePackageListing.ResetForTests();
                    ScanWhitelistManager.Reload();
                }
            }
        }

        [Fact]
        public void EveryWhitelistChangeMovesTheStateVersionSoCachedScanDecisionsAreRecomputed()
        {
            using (new TempInstall("native_listing_version"))
            {
                try
                {
                    ScanWhitelistManager whitelist = ScanWhitelistManager.Instance;
                    int v0 = ScanWhitelistManager.StateVersion;

                    whitelist.AddTemporaryUidOverrides(new[] { "Creator.Pack.1" });
                    int v1 = ScanWhitelistManager.StateVersion;
                    whitelist.RemoveTemporaryUidOverrides(new[] { "Creator.Pack.1" });
                    int v2 = ScanWhitelistManager.StateVersion;
                    whitelist.AddFolder("AddonPackages/Creator");
                    int v3 = ScanWhitelistManager.StateVersion;
                    whitelist.AddUidOverride("Other.Pack.2");
                    int v4 = ScanWhitelistManager.StateVersion;

                    Assert.True(v0 < v1 && v1 < v2 && v2 < v3 && v3 < v4,
                        "A scene-load allow-list change did not invalidate cached scan decisions, so VaM's refresh keeps " +
                        "treating the scene's dependencies as excluded and they never register.");

                    whitelist.AddTemporaryUidOverrides(new[] { "Other.Pack.2" });
                    Assert.Equal(v4, ScanWhitelistManager.StateVersion);
                }
                finally
                {
                    ScanWhitelistManager.Reload();
                }
            }
        }

        private static void AgeDirectories(params string[] dirs)
        {
            DateTime old = DateTime.UtcNow.AddHours(-1);
            foreach (string dir in dirs)
                Directory.SetLastWriteTimeUtc(dir, old);
        }
    }
}
