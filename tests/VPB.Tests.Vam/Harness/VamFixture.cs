using Xunit;

namespace VPB.Tests
{
    public sealed class VamFixture
    {
        public VamFixture()
        {
            HeadlessVam.Install();
        }

        public string VaMPath { get { return TestEnvironment.VaMPath; } }
        public string RepoRoot { get { return TestEnvironment.RepoRoot; } }
    }

    [CollectionDefinition(Name)]
    public sealed class VamCollection : ICollectionFixture<VamFixture>
    {
        public const string Name = "vam";
    }
}
