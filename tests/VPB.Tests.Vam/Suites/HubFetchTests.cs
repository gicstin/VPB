using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class HubFetchTests
    {
        public HubFetchTests(VamFixture vam) { }

        [Theory]
        [InlineData("Creator.Package.1")]
        [InlineData("Creator.Package.latest")]
        [InlineData("Some.Long_Name.12")]
        public void RealPackageIdsAreFetchable(string uid)
        {
            Assert.True(VpbHubDependencyFetcher.IsFetchableUid(uid),
                uid + " is a normal package id; refusing it means a scene's missing dependency is never fetched.");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("SELF")]
        [InlineData("SELF.unknown.latest")]
        [InlineData("Creator.Package.1:/Custom/Scripts/thing.cs")]
        [InlineData("Creator.Package.1:/Saves/scene.json")]
        [InlineData("Custom/Scripts/loose.cs")]
        [InlineData("nodots")]
        public void NonPackageReferencesAreRejected(string uid)
        {
            Assert.False(VpbHubDependencyFetcher.IsFetchableUid(uid),
                "'" + (uid ?? "<null>") + "' is not a package id. Sending it to the Hub wastes a lookup " +
                "and can report a phantom missing package to the user.");
        }

        [Fact]
        public void OverlongIdsAreRejected()
        {
            Assert.False(VpbHubDependencyFetcher.IsFetchableUid(new string('a', 300) + ".pkg.1"));
        }

        [Theory]
        [InlineData(null, "Ask")]
        [InlineData("", "Ask")]
        [InlineData("nonsense", "Ask")]
        [InlineData("off", "Off")]
        [InlineData("ALWAYS", "Always")]
        [InlineData("Ask", "Ask")]
        public void ModeNormalizationFallsBackToAsk(string stored, string expected)
        {
            Assert.Equal(expected, VpbHubDependencyFetcher.NormalizeMode(stored));
        }

        [Fact]
        public void PlanIsOverBudgetOnlyWhenABudgetExists()
        {
            VpbHubDependencyFetcher.FetchPlan plan = new VpbHubDependencyFetcher.FetchPlan();
            plan.TotalBytes = 900L * 1024L * 1024L;

            plan.BudgetBytes = 0L;
            Assert.False(plan.OverBudget, "A zero budget means 'no limit', not 'block everything'.");

            plan.BudgetBytes = 1024L * 1024L * 1024L;
            Assert.False(plan.OverBudget);

            plan.BudgetBytes = 512L * 1024L * 1024L;
            Assert.True(plan.OverBudget);
        }

        [Fact]
        public void PlanMarksUnknownSizesForConfirmation()
        {
            VpbHubDependencyFetcher.FetchPlan plan = new VpbHubDependencyFetcher.FetchPlan();
            Assert.False(plan.HasUnknownSizes);

            plan.UnknownSizeCount = 1;
            Assert.True(plan.HasUnknownSizes,
                "A missing Hub size must not look like a zero-byte automatic download.");
        }

        [Fact]
        public void EmptyPlanIsEmpty()
        {
            VpbHubDependencyFetcher.FetchPlan plan = new VpbHubDependencyFetcher.FetchPlan();
            Assert.True(plan.IsEmpty);
            plan.Names.Add("Creator.Package.1");
            Assert.False(plan.IsEmpty);
        }

        [Fact]
        public void UnavailablePackagesStillMakeAPlanVisible()
        {
            VpbHubDependencyFetcher.FetchPlan plan = new VpbHubDependencyFetcher.FetchPlan();
            plan.NotOnHub.Add("Creator.PaidPackage.1");

            Assert.False(plan.IsEmpty,
                "A package that cannot be fetched must still be shown to the user instead of disappearing behind a generic failure.");
        }

        [Theory]
        [InlineData(0L, "0 B")]
        [InlineData(900L, "900 B")]
        [InlineData(2048L, "2 KB")]
        [InlineData(5L * 1024L * 1024L, "5.0 MB")]
        public void ByteSizesReadAsHumanUnits(long bytes, string expected)
        {
            Assert.Equal(expected, VpbHubDependencyFetcher.FormatBytes(bytes));
        }
    }
}
