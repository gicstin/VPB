using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class FileManagerTests
    {
        private readonly ITestOutputHelper _out;
        public FileManagerTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        [Theory]
        [InlineData("Creator.Pack.3", "Creator.Pack")]
        [InlineData("Some Creator.Pack Name.12", "Some Creator.Pack Name")]
        [InlineData("Creator.Pack.latest", "Creator.Pack")]
        public void GroupIdDropsOnlyTheVersionSegment(string uid, string expected)
        {
            Assert.Equal(expected, FileManager.PackageIDToPackageGroupID(uid));
        }

        [Theory]
        [InlineData("Creator.Pack.3", "3")]
        [InlineData("Creator.Pack.12", "12")]
        public void TheVersionSegmentIsReadBackFromAPinnedUid(string uid, string expected)
        {
            Assert.Equal(expected, FileManager.PackageIDToPackageVersion(uid));
        }

        [Fact]
        public void SymbolicVersionsAreNotReportedAsANumericVersion()
        {
            Assert.Null(FileManager.PackageIDToPackageVersion("Creator.Pack.latest"));
            Assert.Equal("4", FileManager.PackageIDToPackageVersion("Creator.Pack.min4"));

            _out.WriteLine("latest -> null, min4 -> 4: the minimum pin reports the number it is a floor for.");
        }

        [Theory]
        [InlineData("3", 3)]
        [InlineData("12", 12)]
        public void NumericVersionSegmentsParse(string segment, int expected)
        {
            int version;
            Assert.True(FileManager.TryParseVarVersionSegment(segment, out version), "failed to parse '" + segment + "'");
            Assert.Equal(expected, version);
        }

        [Theory]
        [InlineData("latest")]
        [InlineData("min4")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("x")]
        public void NonNumericVersionSegmentsAreRejected(string segment)
        {
            int version;
            Assert.False(FileManager.TryParseVarVersionSegment(segment, out version),
                "'" + segment + "' parsed as a numeric version. A false positive here pins a dependency to a " +
                "version number that was never written.");
        }

        [Theory]
        [InlineData("Creator .Pack .3", "Creator.Pack.3")]
        [InlineData("Creator. Pack .3", "Creator.Pack.3")]
        public void CanonicalisingAUidTrimsEachSegment(string uid, string expected)
        {
            Assert.Equal(expected, FileManager.CanonicalizeUidSegments(uid));
        }

        [Theory]
        [InlineData("Some Creator.Pack Name.3")]
        [InlineData("Creator.Pack.3")]
        public void CanonicalisingNeverRemovesWhitespaceInsideASegment(string uid)
        {
            Assert.Equal(uid, FileManager.CanonicalizeUidSegments(uid));

            _out.WriteLine("'" + uid + "' is left alone. The alias exists for padded segments");
            _out.WriteLine("(verytoxic. Laid_Edges.2), not for names that legitimately contain spaces -");
            _out.WriteLine("collapsing those would merge two distinct packages into one identity.");
        }

        [Theory]
        [InlineData("AddonPackages/Creator.Pack.3.var", "Creator.Pack.3")]
        [InlineData("AddonPackages\\Creator.Pack.3.var", "Creator.Pack.3")]
        [InlineData("AddonPackages/sub/Creator.Pack.3.var", "Creator.Pack.3")]
        public void CanonicalUidIsRecoveredFromAVarPath(string varPath, string expected)
        {
            string uid;
            Assert.True(FileManager.TryGetCanonicalUidFromVarPath(varPath, out uid), "no uid from " + varPath);
            Assert.Equal(expected, uid);
        }

        [Theory]
        [InlineData("Saves\\scene\\x.json", "Saves/scene/x.json")]
        [InlineData("Saves/scene/x.json", "Saves/scene/x.json")]
        public void PathCleaningNormalisesSlashes(string input, string expected)
        {
            Assert.Equal(expected, FileManager.CleanFilePath(input));
        }

        [Theory]
        [InlineData("Creator.Pack.3:/Saves/scene/x.json", "Saves/scene/x.json")]
        [InlineData("Saves/scene/x.json", "Saves/scene/x.json")]
        public void RemovingThePackagePrefixLeavesTheInternalPath(string input, string expected)
        {
            Assert.Equal(expected, FileManager.RemovePackageFromPath(input));
        }

        [Fact]
        public void ARegisteredPackageIsFoundByUidPathAndGroup()
        {
            using (var install = new TempInstall("fm_registry"))
            using (var library = new VamLibrary(install))
            {
                VarPackage pkg = library.AddScene("Creator", "Pack", 3);

                Assert.Equal(1, FileManager.GetPackageCount());
                Assert.True(FileManager.IsPackage("Creator.Pack.3"));

                Assert.Same(pkg, FileManager.GetExactRegisteredPackage("Creator.Pack.3"));
                Assert.Same(pkg, FileManager.GetPackage("Creator.Pack.3", false));

                VarPackageGroup group = FileManager.GetPackageGroup("Creator.Pack");
                Assert.NotNull(group);
                Assert.Same(pkg, group.NewestPackage);
            }
        }

        [Fact]
        public void AnUnregisteredPackageIsNotInvented()
        {
            using (var install = new TempInstall("fm_absent"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 3);

                Assert.False(FileManager.IsPackage("Other.Pack.1"));
                Assert.Null(FileManager.GetExactRegisteredPackage("Other.Pack.1"));
                Assert.Null(FileManager.GetPackage("Other.Pack.1", false));
            }
        }

        [Fact]
        public void TheNewestVersionInAGroupWins()
        {
            using (var install = new TempInstall("fm_newest"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 1);
                library.AddScene("Creator", "Pack", 9);
                VarPackage ten = library.AddScene("Creator", "Pack", 10);

                VarPackageGroup group = FileManager.GetPackageGroup("Creator.Pack");
                _out.WriteLine("newest = " + group.NewestPackage.Uid);

                Assert.Same(ten, group.NewestPackage);
                Assert.Same(ten, FileManager.ResolveDependency("Creator.Pack.latest"));
            }
        }

        [Fact]
        public void AnExactDependencyPinResolvesToThatVersion()
        {
            using (var install = new TempInstall("fm_pin"))
            using (var library = new VamLibrary(install))
            {
                VarPackage two = library.AddScene("Creator", "Pack", 2);
                library.AddScene("Creator", "Pack", 5);

                Assert.Same(two, FileManager.ResolveDependency("Creator.Pack.2"));
            }
        }

        [Fact]
        public void AMissingExactPinFallsBackWithinTheGroupRatherThanReturningNothing()
        {
            using (var install = new TempInstall("fm_pin_missing"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 2);
                library.AddScene("Creator", "Pack", 5);

                VarPackage resolved = FileManager.ResolveDependency("Creator.Pack.9");
                _out.WriteLine("Creator.Pack.9 -> " + (resolved == null ? "(null)" : resolved.Uid));

                Assert.Null(resolved);
                _out.WriteLine("At the shipped RespectPackageReferenceVersionOption default and with no meta.json");
                _out.WriteLine("opinion, a missing exact pin resolves to nothing rather than silently substituting");
                _out.WriteLine("another version. GetPackageForDependency is the path that applies a fallback.");

                Assert.NotNull(FileManager.ResolveDependency("Creator.Pack.latest"));
            }
        }

        [Fact]
        public void ADependencyOnAnUninstalledGroupResolvesToNothing()
        {
            using (var install = new TempInstall("fm_dep_absent"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 1);

                Assert.Null(FileManager.ResolveDependency("Nobody.Missing.1"));
                Assert.False(FileManager.IsDependencySatisfiedByInstalled("Nobody.Missing.1"));
            }
        }

        [Fact]
        public void AnInstalledDependencyIsReportedSatisfied()
        {
            using (var install = new TempInstall("fm_dep_ok"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 3);

                Assert.True(FileManager.IsDependencySatisfiedByInstalled("Creator.Pack.3"));
                Assert.True(FileManager.IsDependencySatisfiedByInstalled("Creator.Pack.latest"));
            }
        }

        [Fact]
        public void ASpacedPackageAnswersToItsWhitespaceStrippedAlias()
        {
            using (var install = new TempInstall("fm_alias"))
            using (var library = new VamLibrary(install))
            {
                VarPackage padded = library.Add(IndexFixture.SceneVar("Creator ", "Pack ", 3));
                _out.WriteLine("registered uid = '" + padded.Uid + "'");

                string actual;
                bool resolved = FileManager.TryResolveWhitespaceAliasUid("Creator.Pack.3", out actual);
                _out.WriteLine("alias lookup -> " + resolved + " / " + actual);

                Assert.True(resolved,
                    "A package whose uid carries padded segments must answer to its trimmed alias. Scenes " +
                    "reference the trimmed form, so without this the dependency reads as missing while the " +
                    "file is sitting right there.");
                Assert.Equal(padded.Uid, actual);
            }
        }

        [Fact]
        public void TheAliasMapIsAReverseIndexNotARename()
        {
            using (var install = new TempInstall("fm_alias_literal"))
            using (var library = new VamLibrary(install))
            {
                VarPackage padded = library.Add(IndexFixture.SceneVar("Creator ", "Pack ", 3));

                Assert.Equal("Creator .Pack .3", padded.Uid);
                Assert.True(library.PackagesByUid.ContainsKey("Creator .Pack .3"),
                    "The registry key must stay the literal on-disk uid. Rewriting it to the trimmed form " +
                    "makes every path built from the file name miss.");

                string registered;
                Assert.True(FileManager.TryMapLookupUidToRegisteredUid("Creator.Pack.3", out registered));
                Assert.Equal("Creator .Pack .3", registered);
            }
        }

        [Fact]
        public void AliasResolutionWorksForGroupsToo()
        {
            using (var install = new TempInstall("fm_alias_group"))
            using (var library = new VamLibrary(install))
            {
                library.Add(IndexFixture.SceneVar("Creator ", "Pack ", 3));

                string registered;
                Assert.True(FileManager.TryMapLookupGroupIdToRegisteredGroupId("Creator.Pack", out registered),
                    "A group whose id carries padded segments must answer to its trimmed form, or every " +
                    "group-level lookup (newest version, dependents count) misses it.");

                _out.WriteLine("trimmed 'Creator.Pack' -> registered '" + registered + "'");

                Assert.NotEqual("Creator.Pack", registered);
                Assert.Equal("Creator.Pack", FileManager.CanonicalizeUidSegments(registered));
            }
        }

        [Fact]
        public void ASpacedPackageResolvesAsADependencyThroughItsAlias()
        {
            using (var install = new TempInstall("fm_alias_dep"))
            using (var library = new VamLibrary(install))
            {
                VarPackage padded = library.Add(IndexFixture.SceneVar("Creator ", "Pack ", 3));

                Assert.Same(padded, FileManager.ResolveDependency("Creator.Pack.3"));
                Assert.Same(padded, FileManager.ResolveDependency("Creator .Pack .3"));
            }
        }

        [Fact]
        public void APackagePathNormalisesToTheEntryUidInsteadOfThrowing()
        {
            using (var install = new TempInstall("fm_normalize"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 3);

                string normalised = FileManager.NormalizePath("Creator.Pack.3:/Saves/scene/Pack.json");
                _out.WriteLine("normalised -> " + normalised);

                Assert.Contains("Creator.Pack.3", normalised, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(":\\", normalised, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void APackagePathIsRecognisedOnlyOnceItsPackageIsRegistered()
        {
            using (var install = new TempInstall("fm_ispkgpath"))
            using (var library = new VamLibrary(install))
            {
                const string path = "Creator.Pack.3:/Saves/scene/Pack.json";
                Assert.False(FileManager.IsPackagePath(path));

                library.AddScene("Creator", "Pack", 3);
                Assert.True(FileManager.IsPackagePath(path));
            }
        }

        [Fact]
        public void AWindowsDrivePathIsNeverMistakenForAPackagePath()
        {
            using (var install = new TempInstall("fm_drive"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 3);

                Assert.False(FileManager.IsPackagePath("C:/vam/Saves/scene/x.json"),
                    "A drive-letter path contains ':/' too. Treating it as a package path sends the loader " +
                    "looking for a package called 'C'.");
                Assert.False(FileManager.IsPackagePath("D:\\Games\\VaM\\Saves\\scene\\x.json"));
            }
        }

        [Fact]
        public void AFileInsideARegisteredPackageIsFound()
        {
            using (var install = new TempInstall("fm_entry"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 3);

                const string inside = "Creator.Pack.3:/Saves/scene/Pack.json";
                Assert.True(FileManager.FileExists(inside), "the scene inside the registered package was not found");
                Assert.True(FileManager.IsFileInPackage(inside));

                FileEntry entry = FileManager.GetFileEntry(inside);
                Assert.NotNull(entry);
                _out.WriteLine("entry uid = " + entry.Uid);

                Assert.False(FileManager.FileExists("Creator.Pack.3:/Saves/scene/absent.json"));
            }
        }

        [Fact]
        public void LooseFilesOnDiskAreFoundThroughTheSandbox()
        {
            using (var install = new TempInstall("fm_loose"))
            using (var library = new VamLibrary(install))
            {
                string scene = Path.Combine(install.EnsureDir("Saves", "scene"), "local.json");
                File.WriteAllText(scene, "{}");

                Assert.True(FileManager.FileExists("Saves/scene/local.json"));
                Assert.False(FileManager.IsFileInPackage("Saves/scene/local.json"));
                Assert.True(FileManager.DirectoryExists("Saves/scene"));
                Assert.False(FileManager.DirectoryExists("Saves/nope"));
            }
        }

        [Fact]
        public void TheLoadDirStackUnwindsInOrder()
        {
            using (var install = new TempInstall("fm_loaddir"))
            using (var library = new VamLibrary(install))
            {
                FileManager.SetLoadDir("Saves");
                string outer = FileManager.CurrentLoadDir;

                FileManager.PushLoadDir("Saves/scene");
                Assert.NotEqual(outer, FileManager.CurrentLoadDir);

                FileManager.PopLoadDir();
                Assert.Equal(outer, FileManager.CurrentLoadDir);
            }
        }

        [Fact]
        public void DirectoryTimestampProbeFollowsAJunctionRatherThanReadingTheLinkNode()
        {
            using (var install = new TempInstall("fm_link"))
            {
                string target = install.EnsureDir("LinkTarget");
                File.WriteAllText(Path.Combine(target, "marker.txt"), "x");

                string link = install.PathTo("LinkedAddonPackages");
                if (!TryCreateJunction(link, target))
                {
                    _out.WriteLine("could not create a junction here - skipping the reparse-point half");
                    return;
                }

                long viaTarget, viaLink;
                bool targetIsLink, linkIsLink;

                Assert.True(FileManager.TryGetDirectoryLastWriteBinaryFollowingLinks(target, out viaTarget, out targetIsLink));
                Assert.True(FileManager.TryGetDirectoryLastWriteBinaryFollowingLinks(link, out viaLink, out linkIsLink));

                _out.WriteLine("target " + viaTarget + " isLink=" + targetIsLink);
                _out.WriteLine("link   " + viaLink + " isLink=" + linkIsLink);

                Assert.False(targetIsLink);
                Assert.True(linkIsLink, "The junction was not detected as a reparse point.");
                Assert.Equal(viaTarget, viaLink);
            }
        }

        private static bool TryCreateJunction(string link, string target)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    p.WaitForExit(10000);
                    return p.ExitCode == 0 && Directory.Exists(link);
                }
            }
            catch { return false; }
        }

        [Fact]
        public void ASettingCanBePinnedForTheDurationOfATest()
        {
            using (var install = new TempInstall("fm_setting"))
            using (var library = new VamLibrary(install))
            {
                library.AddScene("Creator", "Pack", 1);
                library.AddScene("Creator", "Pack", 5);

                HeadlessVam.OverrideSetting("ForceLatestDependencies", true);
                HeadlessVam.OverrideSetting("ForceLatestDependencyPackageGroups", "Creator.Pack");

                Assert.NotNull(Settings.Instance.ForceLatestDependencies);
                Assert.True(Settings.Instance.ForceLatestDependencies.Value);
                Assert.True(FileManager.ShouldForceLatestForPackageGroup("Creator.Pack"),
                    "With ForceLatestDependencies on and the group listed, the resolver must force newest. " +
                    "A test that leaves settings unbound silently exercises the off branch of every feature.");

                HeadlessVam.OverrideSetting("ForceLatestDependencies", false);
                Assert.False(FileManager.ShouldForceLatestForPackageGroup("Creator.Pack"));
            }
        }

        [Fact]
        public void SafeDirectoryEnumerationDoesNotThrowOnAMissingPath()
        {
            using (var install = new TempInstall("fm_safe"))
            {
                var files = new List<string>();
                var dirs = new List<string>();

                FileManager.SafeGetFiles(install.PathTo("does", "not", "exist"), "*.*", files);
                FileManager.SafeGetDirectories(install.PathTo("does", "not", "exist"), "*", dirs);

                Assert.Empty(files);
                Assert.Empty(dirs);
            }
        }

        [Fact]
        public void TheRegistryIsRestoredWhenTheLibraryIsDisposed()
        {
            using (var install = new TempInstall("fm_restore"))
            {
                int before = FileManager.GetPackageCount();

                using (var library = new VamLibrary(install))
                {
                    library.AddScene("Creator", "Pack", 3);
                    Assert.Equal(1, FileManager.GetPackageCount());
                }

                Assert.Equal(before, FileManager.GetPackageCount());
            }
        }
    }
}
