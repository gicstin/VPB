using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class HideMarkerTests
    {
        private readonly ITestOutputHelper _out;
        public HideMarkerTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static void WriteMarker(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "");
        }

        private static void HidePackage(string uid)
        {
            WriteMarker(VpbHideIndex.BuildPackageHidePath(uid, null));
            VpbHideIndex.Invalidate();
        }

        private static void HideItem(string uid, string internalPath)
        {
            WriteMarker(VpbHideIndex.BuildVarEntryFlagPath(uid, internalPath, "hide"));
            VpbHideIndex.Invalidate();
        }

        [Fact]
        public void PackageMarkerHidesThePackageAndNotItsItems()
        {
            using (var install = new TempInstall("hide_pkg"))
            {
                HidePackage("Creator.Pack.1");

                Assert.True(VpbHideIndex.IsPackageHidden("Creator.Pack.1"));
                Assert.False(VpbHideIndex.IsPackageHidden("Creator.Other.1"));

                Assert.False(VpbHideIndex.PackageHasHiddenItems("Creator.Pack.1"),
                    "A package-level hide must never register as per-item hides. Fanning a package hide out into " +
                    "one marker per scene is the bug this invariant exists to catch: unhiding the package then " +
                    "leaves orphan item markers behind and items stay invisible.");
            }
        }

        [Fact]
        public void ItemMarkerHidesOnlyThatItem()
        {
            using (var install = new TempInstall("hide_item"))
            {
                HideItem("Creator.Pack.1", "Saves/scene/one.json");

                Assert.True(VpbHideIndex.IsItemHidden("Creator.Pack.1", "Saves/scene/one.json"));
                Assert.False(VpbHideIndex.IsItemHidden("Creator.Pack.1", "Saves/scene/two.json"));
                Assert.True(VpbHideIndex.PackageHasHiddenItems("Creator.Pack.1"));

                Assert.False(VpbHideIndex.IsPackageHidden("Creator.Pack.1"),
                    "Hiding one item must not hide the whole package.");
            }
        }

        [Fact]
        public void ItemKeyMatchesTheVarFileEntryUidForm()
        {
            Assert.Equal("Creator.Pack.1:/Saves/scene/one.json",
                VpbHideIndex.BuildItemKey("Creator.Pack.1", "Saves/scene/one.json"));

            Assert.Equal("Creator.Pack.1:/Saves/scene/one.json",
                VpbHideIndex.BuildItemKey("Creator.Pack.1", "Saves\\scene\\one.json"));
        }

        [Fact]
        public void ItemLookupIsCaseInsensitiveOnBothHalves()
        {
            using (var install = new TempInstall("hide_case"))
            {
                HideItem("Creator.Pack.1", "Saves/scene/one.json");

                Assert.True(VpbHideIndex.IsItemHidden("creator.pack.1", "saves/scene/one.json"));
                Assert.True(VpbHideIndex.IsPackageHidden("Creator.Pack.1") == false);
            }
        }

        [Fact]
        public void BackslashAndForwardSlashMarkersResolveToTheSameItem()
        {
            using (var install = new TempInstall("hide_slash"))
            {
                HideItem("Creator.Pack.1", "Saves\\scene\\one.json");
                Assert.True(VpbHideIndex.IsItemHidden("Creator.Pack.1", "Saves/scene/one.json"));
            }
        }

        [Fact]
        public void PackageAndItemMarkersCoexistWithoutBleedingIntoEachOther()
        {
            using (var install = new TempInstall("hide_mixed"))
            {
                HidePackage("Creator.Hidden.1");
                HideItem("Creator.Visible.2", "Custom/Clothing/Female/dress/dress.vam");

                Assert.True(VpbHideIndex.IsPackageHidden("Creator.Hidden.1"));
                Assert.False(VpbHideIndex.IsPackageHidden("Creator.Visible.2"));

                Assert.True(VpbHideIndex.PackageHasHiddenItems("Creator.Visible.2"));
                Assert.False(VpbHideIndex.PackageHasHiddenItems("Creator.Hidden.1"));
            }
        }

        [Fact]
        public void NoMarkersMeansNothingIsHidden()
        {
            using (var install = new TempInstall("hide_none"))
            {
                Assert.False(VpbHideIndex.IsPackageHidden("Creator.Pack.1"));
                Assert.False(VpbHideIndex.IsItemHidden("Creator.Pack.1", "Saves/scene/one.json"));
                Assert.False(VpbHideIndex.PackageHasHiddenItems("Creator.Pack.1"));
            }
        }

        [Fact]
        public void NullAndEmptyArgumentsAreRejectedRatherThanMatchingEverything()
        {
            using (var install = new TempInstall("hide_null"))
            {
                HidePackage("Creator.Pack.1");

                Assert.False(VpbHideIndex.IsPackageHidden(null));
                Assert.False(VpbHideIndex.IsPackageHidden(""));
                Assert.False(VpbHideIndex.IsItemHidden(null, "Saves/scene/one.json"));
                Assert.False(VpbHideIndex.IsItemHidden("Creator.Pack.1", null));
                Assert.False(VpbHideIndex.IsItemHiddenByEntryUid(""));
                Assert.Null(VpbHideIndex.BuildItemKey(null, "x"));
                Assert.Null(VpbHideIndex.BuildItemKey("x", null));
            }
        }

        [Fact]
        public void LooseFileHideFollowsASidecarNextToTheFile()
        {
            using (var install = new TempInstall("hide_loose"))
            {
                string scene = Path.Combine(install.EnsureDir("Saves", "scene"), "local.json");
                File.WriteAllText(scene, "{}");

                Assert.False(VpbHideIndex.IsLooseHidden(scene));

                File.WriteAllText(scene + ".hide", "");
                VpbHideIndex.InvalidateLoose(scene);

                Assert.True(VpbHideIndex.IsLooseHidden(scene),
                    "A loose scene is hidden by a .hide sidecar next to it, not by an AddonPackagesFilePrefs marker.");
            }
        }

        [Fact]
        public void LooseHideResultIsCachedUntilExplicitlyInvalidated()
        {
            using (var install = new TempInstall("hide_loose_cache"))
            {
                string scene = Path.Combine(install.EnsureDir("Saves", "scene"), "local.json");
                File.WriteAllText(scene, "{}");
                File.WriteAllText(scene + ".hide", "");
                VpbHideIndex.InvalidateLoose(scene);
                Assert.True(VpbHideIndex.IsLooseHidden(scene));

                File.Delete(scene + ".hide");
                Assert.True(VpbHideIndex.IsLooseHidden(scene),
                    "The loose-hide cache is deliberate; callers that delete a sidecar must call InvalidateLoose.");

                VpbHideIndex.InvalidateLoose(scene);
                Assert.False(VpbHideIndex.IsLooseHidden(scene));
            }
        }

        [Fact]
        public void MarkerPathsLiveUnderAddonPackagesFilePrefs()
        {
            using (var install = new TempInstall("hide_paths"))
            {
                string pkg = VpbHideIndex.BuildPackageHidePath("Creator.Pack.1", null);
                string item = VpbHideIndex.BuildVarEntryFlagPath("Creator.Pack.1", "Saves/scene/one.json", "hide");
                _out.WriteLine("package marker: " + pkg);
                _out.WriteLine("item marker:    " + item);

                string prefs = VpbHideIndex.PrefsDirFullPath;
                Assert.StartsWith(install.Root, prefs, StringComparison.OrdinalIgnoreCase);
                Assert.EndsWith("AddonPackagesFilePrefs", prefs.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

                Assert.StartsWith(prefs, pkg, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith(prefs, item, StringComparison.OrdinalIgnoreCase);
                Assert.EndsWith(".hide", pkg, StringComparison.Ordinal);
                Assert.EndsWith(".hide", item, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void OnlyVarPathMarkersCountAsPackageHides()
        {
            using (var install = new TempInstall("hide_shape"))
            {
                WriteMarker(Path.Combine(
                    Path.Combine(VpbHideIndex.PrefsDirFullPath, "Creator.Pack.1"),
                    "Saves" + Path.DirectorySeparatorChar + "scene" + Path.DirectorySeparatorChar + "one.json.hide"));
                VpbHideIndex.Invalidate();

                Assert.False(VpbHideIndex.IsPackageHidden("Creator.Pack.1"),
                    "Only a marker whose inner path is AddonPackages/<uid>.var may hide a whole package. " +
                    "Treating any marker as a package hide would make one hidden scene hide the entire package.");
                Assert.True(VpbHideIndex.IsItemHidden("Creator.Pack.1", "Saves/scene/one.json"));
            }
        }
    }
}
