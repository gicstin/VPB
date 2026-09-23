using System.IO;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class LocalDiskEntryCacheTests
    {
        public LocalDiskEntryCacheTests(VamFixture vam) { }

        [Fact]
        public void LocalCustomFilesAndFoldersResolveLikeTheDisk()
        {
            using (var install = new TempInstall("localcustom"))
            {
                LocalDiskEntryCache.Invalidate();
                Directory.CreateDirectory(Path.Combine(install.Root, "Custom/Scripts/Me"));
                File.WriteAllText(Path.Combine(install.Root, "Custom/Scripts/Me/Plugin.cs"), "x");

                Assert.True(LocalDiskEntryCache.Exists("Custom/Scripts/Me/Plugin.cs"),
                    "A loose session plugin must still count as local, or VPB remaps it into a package copy.");
                Assert.True(LocalDiskEntryCache.Exists("Custom/Scripts/Me"),
                    "A local Custom folder must count as present.");
                Assert.True(LocalDiskEntryCache.Exists("custom\\scripts\\me\\plugin.CS"),
                    "Lookups must stay case-insensitive and accept backslashes like the Windows filesystem VaM expects.");
                Assert.False(LocalDiskEntryCache.Exists("Custom/Scripts/Me/Missing.cs"),
                    "A missing local file must stay missing so the package copy is used.");
                Assert.False(LocalDiskEntryCache.Exists("Custom/Atom/Person/Morphs/female/Nope.vmi"),
                    "A path under a folder that does not exist locally must be missing.");
            }
        }

        [Fact]
        public void RepeatedMissesUnderOneFolderListItOnce()
        {
            using (var install = new TempInstall("localcustom_once"))
            {
                LocalDiskEntryCache.Invalidate();
                Directory.CreateDirectory(Path.Combine(install.Root, "Custom/Atom/Person/Morphs/female"));
                int before = LocalDiskEntryCache.Listings;

                for (int i = 0; i < 200; i++)
                    LocalDiskEntryCache.Exists("Custom/Atom/Person/Morphs/female/m" + i + ".vmi");

                Assert.True(LocalDiskEntryCache.Listings - before <= 6,
                    "Morph loading asks about thousands of local paths; each folder must be read once, not probed per file, or every miss rescans it under Wine.");
            }
        }

        [Fact]
        public void FileCreatedMidSessionAppearsAfterCatalogRefresh()
        {
            using (var install = new TempInstall("localcustom_refresh"))
            {
                LocalDiskEntryCache.Invalidate();
                Directory.CreateDirectory(Path.Combine(install.Root, "Custom/Scripts"));
                Assert.False(LocalDiskEntryCache.Exists("Custom/Scripts/New.cs"), "Precondition: file not yet written.");

                File.WriteAllText(Path.Combine(install.Root, "Custom/Scripts/New.cs"), "x");
                VamOnDemandLoader.NotifyNativeCatalogRefreshed();

                Assert.True(LocalDiskEntryCache.Exists("Custom/Scripts/New.cs"),
                    "A script saved during the session must be seen as local after VaM refreshes its catalog.");
            }
        }

        [Fact]
        public void CachedListingExpiresWithoutARefresh()
        {
            using (var install = new TempInstall("localcustom_ttl"))
            {
                double ttl = LocalDiskEntryCache.TtlSeconds;
                try
                {
                    LocalDiskEntryCache.Invalidate();
                    LocalDiskEntryCache.TtlSeconds = 0;
                    Directory.CreateDirectory(Path.Combine(install.Root, "Custom/Scripts"));
                    Assert.False(LocalDiskEntryCache.Exists("Custom/Scripts/Late.cs"), "Precondition: file not yet written.");

                    File.WriteAllText(Path.Combine(install.Root, "Custom/Scripts/Late.cs"), "x");
                    System.Threading.Thread.Sleep(5);

                    Assert.True(LocalDiskEntryCache.Exists("Custom/Scripts/Late.cs"),
                        "A stale listing must expire on its own so a file written mid-session is not hidden indefinitely.");
                }
                finally
                {
                    LocalDiskEntryCache.TtlSeconds = ttl;
                    LocalDiskEntryCache.Invalidate();
                }
            }
        }
    }
}
