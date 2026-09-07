using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class RuntimePluginTargetingTests
    {
        private const string ProjectFile = "tests/VPB.Tests.Runtime/VPB.Tests.Runtime.csproj";

        private readonly ITestOutputHelper _out;
        public RuntimePluginTargetingTests(ITestOutputHelper output) { _out = output; }

        private static string Project()
        {
            string path = Repo.Path_(ProjectFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), ProjectFile + " is missing - update RuntimePluginTargetingTests.");
            return File.ReadAllText(path);
        }

        [Fact]
        public void TheInGamePluginTargetsTheFrameworkVaMActuallyRuns()
        {
            Match tfm = Regex.Match(Project(), @"<TargetFramework>(?<tfm>[^<]+)</TargetFramework>");
            Assert.True(tfm.Success, "No <TargetFramework> in " + ProjectFile);

            string value = tfm.Groups["tfm"].Value.Trim();
            _out.WriteLine("TargetFramework = " + value);

            Assert.True(value == "net35",
                "VPB.Tests.Runtime targets " + value + ". VaM runs Unity 2018 Mono on the .NET 3.5 profile. " +
                "On a higher target the assembly resolver picks the newer mscorlib over VaM's own and the " +
                "compiler happily emits members Mono does not have - [IteratorStateMachine] on every " +
                "iterator, Assembly.op_Equality - with no warning and no error. The plugin then loads and " +
                "dies on its first yield.");
        }

        [Fact]
        public void TheInGamePluginResolvesItsFrameworkFromTheVaMInstall()
        {
            string project = Project();

            Assert.True(project.IndexOf("<FrameworkPathOverride>", StringComparison.Ordinal) >= 0,
                "VPB.Tests.Runtime has no <FrameworkPathOverride>. TargetFramework alone does not decide " +
                "which mscorlib wins - the framework directory does. Point it at VaM's VaM_Data\\Managed so " +
                "the assemblies compiled against are the same files loaded at runtime.");

            Assert.True(Regex.IsMatch(project, @"<FrameworkPathOverride>[^<]*VaM_Data[\\/]Managed"),
                "<FrameworkPathOverride> does not point into VaM_Data\\Managed.");
        }

        [Fact]
        public void TheReferenceConflictWarningIsAnErrorAndIsNotSuppressed()
        {
            string project = Project();

            Match noWarn = Regex.Match(project, @"<NoWarn>(?<v>[^<]*)</NoWarn>");
            if (noWarn.Success)
            {
                _out.WriteLine("NoWarn = " + noWarn.Groups["v"].Value);
                Assert.DoesNotContain("MSB3277", noWarn.Groups["v"].Value);
            }

            Assert.True(Regex.IsMatch(project, @"<MSBuildWarningsAsErrors>[^<]*MSB3277"),
                "MSB3277 must be an error in VPB.Tests.Runtime. It is the warning MSBuild raises when it " +
                "discards VaM's mscorlib for a newer one, and it is the only signal the build gives before " +
                "the plugin becomes unloadable. As a warning it scrolls past; suppressed, it was invisible " +
                "for the entire time the in-game tier was broken.");
        }

        [Fact]
        public void ADebugBuildArmsTheNextInGameRun()
        {
            string project = Project();

            Assert.True(Regex.IsMatch(project, @"<Touch\b[^>]*AlwaysCreate=""true""", RegexOptions.Singleline),
                "The deploy target must create the marker with <Touch AlwaysCreate=\"true\">. Deploying " +
                "without arming is how the in-game tier ends up installed on every build and run only " +
                "when someone remembers a terminal command.");

            Assert.Contains("run-tests.marker", project);

            Assert.Contains("SkipVPBRuntimeTestArm", project);
        }

        [Fact]
        public void TheLastInGameResultIsReportedByThePluginBuildItself()
        {
            string shared = File.ReadAllText(Repo.Path_(
                "tests/VpbRuntimeTestReport.targets".Replace('/', Path.DirectorySeparatorChar)));
            string plugin = File.ReadAllText(Repo.Path_("VPB.csproj"));

            Assert.Contains("VpbRuntimeTestReport.targets", plugin);
            Assert.True(Regex.IsMatch(plugin, @"<CallTarget\s+Targets=""VpbRuntimeReportLastRun"""),
                "VPB.csproj must run VpbRuntimeReportLastRun itself. It builds the test plugin through " +
                "Exec with IgnoreStandardErrorWarningFormat - which is what stops a broken suite failing " +
                "the plugin build, and equally what would reduce the child's warnings to plain text that " +
                "never reaches the IDE error list.");

            Assert.True(Regex.IsMatch(plugin, @"-p:SkipVPBRuntimeTestReport=true"),
                "The child build must be told not to report as well, or every result is printed twice.");

            Assert.True(Regex.IsMatch(shared, @"<Warning[^>]*run-tests\.marker", RegexOptions.Singleline),
                "A surviving marker means the last armed run never happened, so the results being reported " +
                "are from older code. Reporting them without saying so reads exactly like a pass.");

            Assert.DoesNotContain("<Error ", shared);
        }

        [Fact]
        public void TheInGamePluginWritesAReportEvenWhenTheRunDies()
        {
            string plugin = File.ReadAllText(Repo.Path_(
                "tests/VPB.Tests.Runtime/Runtime/VpbRuntimeTestPlugin.cs".Replace('/', Path.DirectorySeparatorChar)));

            Assert.True(plugin.IndexOf("WriteCrashReport", StringComparison.Ordinal) >= 0,
                "The plugin must write a failing report before it starts running, so a crash mid-run is " +
                "reported as a failure rather than as an absent file. An absent file reads as 'you forgot " +
                "to arm it'.");

            Assert.True(Regex.IsMatch(plugin, @"catch\s*\(\s*Exception[\s\S]{0,200}?WriteCrashReport"),
                "The runner loop must be driven inside a try/catch that writes a crash report. An exception " +
                "out of a Unity coroutine stops the coroutine and lands only in the player log.");
        }
    }
}
