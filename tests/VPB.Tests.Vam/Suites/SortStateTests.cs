using System;
using System.Collections.Generic;
using System.Reflection;
using VPB;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Vam
{
    [Collection(VamCollection.Name)]
    public class SortStateTests
    {
        private readonly ITestOutputHelper _out;
        public SortStateTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static readonly Dictionary<string, int> PersistedSortTypeValues = new Dictionary<string, int>
        {
            { "Name", 0 }, { "Date", 1 }, { "Size", 2 }, { "Count", 3 }, { "Score", 4 },
            { "Rating", 5 }, { "Deps", 6 }, { "Dependents", 7 }, { "Missing", 8 }, { "Hidden", 9 },
            { "HiddenOnly", 10 }, { "AutoInstall", 11 }, { "AutoInstallOnly", 12 }, { "DateCreated", 13 },
            { "LoadedOnly", 14 }, { "UnloadedOnly", 15 }, { "UsageCount", 16 }, { "UnusedOnly", 17 },
            { "DateAdded", 18 }, { "DateUpdated", 19 }, { "Random", 20 }, { "HubDownloads", 21 },
            { "HubRating", 22 }, { "HubReleased", 23 }, { "HubUpdated", 24 },
        };

        [Fact]
        public void SortTypeOrdinalsNeverMoveBecauseCachesOnDiskHoldThem()
        {
            var moved = new List<string>();

            foreach (KeyValuePair<string, int> pinned in PersistedSortTypeValues)
            {
                if (!Enum.IsDefined(typeof(SortType), pinned.Key))
                {
                    moved.Add(pinned.Key + " no longer exists (was " + pinned.Value + ")");
                    continue;
                }

                int actual = (int)Enum.Parse(typeof(SortType), pinned.Key);
                if (actual != pinned.Value)
                    moved.Add(pinned.Key + " was " + pinned.Value + ", is now " + actual);
            }

            Assert.True(moved.Count == 0,
                "SortType ordinals changed:" + Environment.NewLine + Bullets(moved) + Environment.NewLine +
                Environment.NewLine +
                "These integers are written into snapshot cache files and into snapshot cache keys. A shifted " +
                "value makes an existing install read yesterday's cache under today's meaning - the gallery " +
                "comes up sorted by something the user did not pick, with no error anywhere. Renaming a member " +
                "is fine; renumbering one is not. New members append at the end, and get added here.");
        }

        [Fact]
        public void EverySortTypeIsPinnedSoANewOneCannotBeAddedInTheMiddle()
        {
            var unpinned = new List<string>();

            foreach (string name in Enum.GetNames(typeof(SortType)))
                if (!PersistedSortTypeValues.ContainsKey(name))
                    unpinned.Add(name + " = " + (int)Enum.Parse(typeof(SortType), name));

            _out.WriteLine("SortType members: " + Enum.GetNames(typeof(SortType)).Length);

            Assert.True(unpinned.Count == 0,
                "New SortType members are not pinned here:" + Environment.NewLine + Bullets(unpinned) +
                Environment.NewLine + Environment.NewLine +
                "Add them to PersistedSortTypeValues with the value they shipped with. Until they are listed, " +
                "nothing stops the next edit from inserting a member above them and renumbering the rest.");
        }

        [Fact]
        public void TheSidePaneFourModeSortRoundTripsThroughItsIndex()
        {
            MethodInfo toState = typeof(GalleryPanel).GetMethod(
                "SidePaneFourModeToState", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo toIndex = typeof(GalleryPanel).GetMethod(
                "TryGetSidePaneFourModeIndex", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.True(toState != null && toIndex != null,
                "GalleryPanel.SidePaneFourModeToState / TryGetSidePaneFourModeIndex are gone. If the four-mode " +
                "side-pane sort was replaced, update this test rather than deleting it.");

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int mode = 0; mode <= 3; mode++)
            {
                object[] args = { mode, null, null };
                toState.Invoke(null, args);

                var type = (SortType)args[1];
                var dir = (SortDirection)args[2];
                string state = type + "/" + dir;

                int earlier;
                Assert.False(seen.TryGetValue(state, out earlier),
                    "Modes " + earlier + " and " + mode + " both map to " + state +
                    ". The cycle button would appear to do nothing on one press.");
                seen[state] = mode;

                var back = (int)toIndex.Invoke(null, new object[] { new SortState(type, dir) });

                Assert.Equal(mode, back);
            }

            var unmapped = (int)toIndex.Invoke(null, new object[] { new SortState(SortType.Size, SortDirection.Ascending) });
            Assert.Equal(-1, unmapped);

            var nullState = (int)toIndex.Invoke(null, new object[] { null });
            Assert.Equal(-1, nullState);
        }

        [Fact]
        public void ACloneIsIndependentOfTheStateItCameFrom()
        {
            var original = new SortState(SortType.Rating, SortDirection.Descending);
            SortState copy = original.Clone();

            copy.Type = SortType.Name;
            copy.Direction = SortDirection.Ascending;

            Assert.Equal(SortType.Rating, original.Type);
            Assert.Equal(SortDirection.Descending, original.Direction);
        }

        private static string Bullets(List<string> lines)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++) sb.AppendLine("  - " + lines[i]);
            return sb.ToString();
        }
    }
}
