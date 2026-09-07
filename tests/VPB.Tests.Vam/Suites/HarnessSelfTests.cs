using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class HarnessSelfTests
    {
        private readonly ITestOutputHelper _out;
        private readonly VamFixture _vam;

        public HarnessSelfTests(VamFixture vam, ITestOutputHelper output) { _vam = vam; _out = output; }

        [Fact]
        public void VpbSourcesAreCompiledIntoTheTestAssembly()
        {
            Assembly asm = typeof(HarnessSelfTests).Assembly;
            int vpbTypes = HarmonyPatchTargetTests.SafeTypes(asm)
                .Count(t => t.Namespace != null && t.Namespace.StartsWith("VPB", StringComparison.Ordinal)
                            && !t.Namespace.StartsWith("VPB.Tests", StringComparison.Ordinal));

            _out.WriteLine("VPB types in the test assembly: " + vpbTypes);
            Assert.True(vpbTypes > 200,
                "Only " + vpbTypes + " VPB types are present. The VpbImportSources target in VPB.Tests.Vam.csproj " +
                "is not pulling the <Compile Include> list out of VPB.csproj.");
        }

        [Fact]
        public void VamAssembliesAreSideBySideWithTheTestAssembly()
        {
            string dir = Path.GetDirectoryName(new Uri(typeof(HarnessSelfTests).Assembly.CodeBase).LocalPath);
            var required = new[] { "Assembly-CSharp.dll", "UnityEngine.dll", "UnityEngine.CoreModule.dll", "0Harmony20.dll" };
            var missing = required.Where(n => !File.Exists(Path.Combine(dir, n))).ToList();

            Assert.True(missing.Count == 0,
                "These VaM assemblies are not next to the test assembly in " + dir + ":" + Environment.NewLine +
                HarmonyPatchTargetTests.Bullets(missing) + Environment.NewLine + Environment.NewLine +
                "Without them xUnit reports 'Skipping: could not find dependent assembly ...' and discovers ZERO " +
                "tests instead of failing. Keep <Private>true</Private> on the VaM <Reference> items.");
        }

        [Fact]
        public void HarnessResolvedEverySeamItTriedToPatch()
        {
            var unresolved = HeadlessVam.UnresolvedSeams.ToList();
            Assert.True(unresolved.Count == 0,
                "HeadlessVam could not find these members to patch - they were renamed or re-signed:" +
                Environment.NewLine + HarmonyPatchTargetTests.Bullets(unresolved));
        }

        [Fact]
        public void HarmonyDetoursWorkInsideTheTestHost()
        {
            HeadlessVam.ClearLog();
            LogUtil.Log("[harness self-test] info line");
            LogUtil.LogWarning("[harness self-test] warning line");

            List<string> lines = HeadlessVam.LogLines.ToList();
            foreach (string l in lines) _out.WriteLine("captured: " + l);

            Assert.True(lines.Count >= 2,
                "The HeadlessVam log prefix did not capture VPB's own logging. Harmony patching is not taking " +
                "effect in this host, which means every other seam patch is also inert.");
            Assert.Contains(lines, l => l.Contains("[harness self-test] info line"));
            Assert.Contains(lines, l => l.Contains("[harness self-test] warning line"));
        }

        [Fact]
        public void UnityEcallsFailInTheDocumentedWay()
        {
            Exception caught = Record.Exception(() => { float t = UnityEngine.Time.realtimeSinceStartup; });

            Assert.True(caught != null,
                "UnityEngine.Time.realtimeSinceStartup succeeded outside the player. The harness assumes engine " +
                "internal calls throw; if that changed, HeadlessVam's seam model needs revisiting.");
            Assert.True(HeadlessVam.IsEcall(caught),
                "Expected the documented ECall failure, got: " + HeadlessVam.DescribeUnwrapped(caught));
        }

        [Fact]
        public void PureUnityValueTypesStillWork()
        {
            Assert.Equal(1f, UnityEngine.Mathf.Clamp01(5f));
            Assert.Equal(3f, new UnityEngine.Vector3(1f, 2f, 2f).magnitude, 3);
            Assert.True(new UnityEngine.Rect(0f, 0f, 10f, 10f).Contains(new UnityEngine.Vector2(5f, 5f)));
        }

        [Fact]
        public void TempInstallSandboxesTheWorkingDirectory()
        {
            string before = Directory.GetCurrentDirectory();
            string inside;
            using (var install = new TempInstall("sandbox"))
            {
                inside = Directory.GetCurrentDirectory();
                Assert.True(Directory.Exists(install.PluginDataDir));
                Assert.True(Directory.Exists(install.AddonPackagesDir));
            }
            Assert.NotEqual(before, inside);
            Assert.Equal(before, Directory.GetCurrentDirectory());
        }

        [Fact]
        public void PluginVersionStubIsInUseNotTheGeneratedOne()
        {
            Assert.Equal("tests", PluginVersionInfo.BuildBranch);
        }
    }
}
