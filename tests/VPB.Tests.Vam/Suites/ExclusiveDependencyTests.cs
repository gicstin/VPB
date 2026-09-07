using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ExclusiveDependencyTests
    {
        private readonly ITestOutputHelper _out;
        public ExclusiveDependencyTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private void Dump(List<string> exclusive)
        {
            _out.WriteLine("exclusive: " + (exclusive.Count == 0 ? "(none)" : string.Join(", ", exclusive.ToArray())));
        }

        [Fact]
        public void ADependencyUsedOnlyByTheSeedIsExclusive()
        {
            using (var install = new TempInstall("dep_simple"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Installed("Other.Unrelated.1")
                    .Seed("Alpha.Scene.1")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .Installed("Beta.Dress.2")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Equal(new[] { "Beta.Dress.2" }, exclusive.ToArray());
            }
        }

        [Fact]
        public void ADependencyAnotherInstalledPackageAlsoNeedsIsNotExclusive()
        {
            using (var install = new TempInstall("dep_shared"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2", "Gamma.OtherScene.1")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .DependsOn("Gamma.OtherScene.1", "Beta.Dress.2")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Empty(exclusive);
            }
        }

        [Fact]
        public void ExclusivityIsTransitiveThroughAChain()
        {
            using (var install = new TempInstall("dep_chain"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2", "Gamma.Texture.1")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .DependsOn("Beta.Dress.2", "Gamma.Texture.1")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Equal(new[] { "Beta.Dress.2", "Gamma.Texture.1" }, exclusive.ToArray());
            }
        }

        [Fact]
        public void AnOutsideReferenceDeepInTheChainProtectsEverythingBelowIt()
        {
            using (var install = new TempInstall("dep_chain_shared"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2", "Gamma.Texture.1", "Delta.Keeper.1")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .DependsOn("Beta.Dress.2", "Gamma.Texture.1")
                    .DependsOn("Delta.Keeper.1", "Gamma.Texture.1")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Contains("Beta.Dress.2", exclusive);
                Assert.DoesNotContain("Gamma.Texture.1", exclusive);
            }
        }

        [Fact]
        public void LockedPackagesAreNeverReportedAsExclusive()
        {
            using (var install = new TempInstall("dep_locked"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .Locked("Beta.Dress.2")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Empty(exclusive);
            }
        }

        [Fact]
        public void ALockedPackageAlsoProtectsWhatItDependsOn()
        {
            using (var install = new TempInstall("dep_locked_chain"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2", "Gamma.Texture.1")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .DependsOn("Beta.Dress.2", "Gamma.Texture.1")
                    .Locked("Beta.Dress.2")
                    .FindExclusive();

                Dump(exclusive);
                Assert.DoesNotContain("Beta.Dress.2", exclusive);
                Assert.DoesNotContain("Gamma.Texture.1", exclusive);
            }
        }

        [Fact]
        public void TheSeedItselfIsNeverListed()
        {
            using (var install = new TempInstall("dep_seed"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1", "Alpha.Other.1")
                    .DependsOn("Alpha.Scene.1", "Alpha.Other.1")
                    .FindExclusive();

                Dump(exclusive);
                Assert.DoesNotContain("Alpha.Scene.1", exclusive);
                Assert.DoesNotContain("Alpha.Other.1", exclusive);
            }
        }

        [Fact]
        public void UninstalledDependenciesAreIgnored()
        {
            using (var install = new TempInstall("dep_missing"))
            {
                DependencyGraphFixture fixture = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .DependsOn("Alpha.Scene.1", "NotInstalled.Pack.9");

                var result = fixture.Run();
                _out.WriteLine("candidates=" + result.Candidates + " resolveMisses=" + result.ResolveMisses);

                Assert.Empty(result.ExclusiveUids);
            }
        }

        [Fact]
        public void ALatestTokenResolvesToTheNewestInstalledVersion()
        {
            using (var install = new TempInstall("dep_latest"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.1", "Beta.Dress.4", "Beta.Dress.2")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.latest")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Contains("Beta.Dress.4", exclusive);
                Assert.DoesNotContain("Beta.Dress.1", exclusive);
                Assert.DoesNotContain("Beta.Dress.2", exclusive);
            }
        }

        [Fact]
        public void AMinimumTokenResolvesToAnInstalledVersionAtOrAboveIt()
        {
            using (var install = new TempInstall("dep_min"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.1", "Beta.Dress.3", "Beta.Dress.7")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.min3")
                    .FindExclusive();

                Dump(exclusive);
                Assert.NotEmpty(exclusive);
                foreach (string uid in exclusive)
                    Assert.True(DependencyGraphFixture.VersionOf(uid) >= 3,
                        "min3 resolved to " + uid + ", which is below the requested minimum. Resolving a minN " +
                        "pin downwards marks a version the scene cannot actually use as removable.");
            }
        }

        [Fact]
        public void AnExactPinThatIsNotInstalledFallsBackToTheNewestInGroup()
        {
            using (var install = new TempInstall("dep_pin_missing"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2", "Beta.Dress.5")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.9")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Contains("Beta.Dress.5", exclusive);
            }
        }

        [Fact]
        public void ACycleDoesNotHangOrReportTheSeed()
        {
            using (var install = new TempInstall("dep_cycle"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .DependsOn("Beta.Dress.2", "Alpha.Scene.1")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Equal(new[] { "Beta.Dress.2" }, exclusive.ToArray());
            }
        }

        [Fact]
        public void SelfReferenceIsIgnored()
        {
            using (var install = new TempInstall("dep_self"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .DependsOn("Beta.Dress.2", "Beta.Dress.2")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Equal(new[] { "Beta.Dress.2" }, exclusive.ToArray());
            }
        }

        [Fact]
        public void NoSeedsMeansNoResult()
        {
            using (var install = new TempInstall("dep_noseed"))
            {
                var result = new DependencyGraphFixture()
                    .Installed("Alpha.Scene.1", "Beta.Dress.2")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .Run();

                Assert.Empty(result.ExclusiveUids);
                Assert.Equal(0, result.SeedCount);
            }
        }

        [Fact]
        public void NullInputIsHandled()
        {
            var result = ExclusiveDependencyFinder.Find(null, null);
            Assert.NotNull(result);
            Assert.Empty(result.ExclusiveUids);
        }

        [Fact]
        public void AnAbortRequestStopsWithoutReportingAnything()
        {
            using (var install = new TempInstall("dep_abort"))
            {
                var input = new DependencyGraphFixture()
                    .Seed("Alpha.Scene.1")
                    .Installed("Beta.Dress.2")
                    .DependsOn("Alpha.Scene.1", "Beta.Dress.2")
                    .Build();

                var result = ExclusiveDependencyFinder.Find(input, () => true);

                Assert.Empty(result.ExclusiveUids);
            }
        }

        [Fact]
        public void TwoSeedsSharingADependencyStillReleaseIt()
        {
            using (var install = new TempInstall("dep_two_seeds"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.One.1", "Alpha.Two.1")
                    .Installed("Beta.Shared.1")
                    .DependsOn("Alpha.One.1", "Beta.Shared.1")
                    .DependsOn("Alpha.Two.1", "Beta.Shared.1")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Equal(new[] { "Beta.Shared.1" }, exclusive.ToArray());
            }
        }

        [Fact]
        public void RemovingOneOfTwoSeedsKeepsTheSharedDependency()
        {
            using (var install = new TempInstall("dep_one_seed"))
            {
                List<string> exclusive = new DependencyGraphFixture()
                    .Seed("Alpha.One.1")
                    .Installed("Alpha.Two.1", "Beta.Shared.1")
                    .DependsOn("Alpha.One.1", "Beta.Shared.1")
                    .DependsOn("Alpha.Two.1", "Beta.Shared.1")
                    .FindExclusive();

                Dump(exclusive);
                Assert.Empty(exclusive);
            }
        }
    }
}
