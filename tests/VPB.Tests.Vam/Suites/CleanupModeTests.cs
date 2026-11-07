using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class CleanupModeTests
    {
        public CleanupModeTests(VamFixture vam) { }

        [Fact]
        public void OpeningCleanupFromPopulatedGalleryDoesNotCancelItsScan()
        {
            var files = new List<FileEntry> { new PackageListEntry(null) };
            Assert.False(GalleryPanel.ShouldExitCleanupForBrowseItems(true, true, files),
                "Refreshing the toolbar before Cleanup replaces gallery rows must not cancel the scan and leave every count at zero.");
        }

        [Fact]
        public void ReturningToBrowseItemsAfterTheScanStillExitsCleanup()
        {
            var files = new List<FileEntry> { new PackageListEntry(null) };
            Assert.True(GalleryPanel.ShouldExitCleanupForBrowseItems(true, false, files),
                "Returning to ordinary gallery rows after scanning must still clear stale Cleanup mode.");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OrdinaryBrowsingDoesNotTriggerCleanupExit(bool scanning)
        {
            var files = new List<FileEntry> { new PackageListEntry(null) };
            Assert.False(GalleryPanel.ShouldExitCleanupForBrowseItems(false, scanning, files),
                "Refreshing the toolbar outside Cleanup must leave the gallery alone.");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EmptyResultsKeepCleanupOpen(bool scanning)
        {
            Assert.False(GalleryPanel.ShouldExitCleanupForBrowseItems(true, scanning, null),
                "Cleanup must stay open before its file list is available.");
            Assert.False(GalleryPanel.ShouldExitCleanupForBrowseItems(true, scanning, new List<FileEntry>()),
                "A scan or filter with no candidates must leave Cleanup open.");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CleanupCandidatesKeepCleanupOpen(bool scanning)
        {
            Type entryType = typeof(GalleryPanel).GetNestedType("CleanupFileEntry", BindingFlags.NonPublic);
            var candidate = (FileEntry)Activator.CreateInstance(entryType, new object[] { null });
            var files = new List<FileEntry> { new PackageListEntry(null), candidate };
            Assert.False(GalleryPanel.ShouldExitCleanupForBrowseItems(true, scanning, files),
                "A visible Cleanup candidate must keep the cleanup actions available.");
        }
    }
}
