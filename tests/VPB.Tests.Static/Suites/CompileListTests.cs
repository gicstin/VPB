using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class CompileListTests
    {
        private const string Allowlist = "known-uncompiled.txt";
        private static readonly XNamespace Msb = "http://schemas.microsoft.com/developer/msbuild/2003";

        private readonly ITestOutputHelper _out;
        public CompileListTests(ITestOutputHelper output) { _out = output; }

        public static IReadOnlyList<string> CompileItems()
        {
            var doc = XDocument.Load(Repo.Path_("VPB.csproj"));
            return doc.Descendants(Msb + "Compile")
                .Select(e => (string)e.Attribute("Include"))
                .Where(v => !string.IsNullOrEmpty(v) && !v.StartsWith("$"))
                .Select(v => v.Replace('\\', '/'))
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [Fact]
        public void EverySourceOnDiskIsCompiledOrDeliberatelyExcluded()
        {
            var compiled = new HashSet<string>(CompileItems(), StringComparer.OrdinalIgnoreCase);
            var allowed = new HashSet<string>(Repo.ReadAllowlist(Allowlist), StringComparer.OrdinalIgnoreCase);
            var onDisk = Repo.SourceFilesOnDisk();

            _out.WriteLine("csproj Compile items: " + compiled.Count);
            _out.WriteLine("src/**/*.cs on disk:  " + onDisk.Count);
            _out.WriteLine("allowlisted:          " + allowed.Count);

            var orphans = onDisk.Where(f => !compiled.Contains(f) && !allowed.Contains(f)).ToList();

            Assert.True(orphans.Count == 0,
                "These source files are committed but are NOT in VPB.csproj, so they are never compiled and never" + Environment.NewLine +
                "reach the shipped DLL - with no build error and no log line:" + Environment.NewLine +
                Repo.Bullets(orphans) + Environment.NewLine + Environment.NewLine +
                "Fix by adding <Compile Include=\"...\" /> to VPB.csproj, or - if the exclusion is deliberate -" + Environment.NewLine +
                "add the path to tests/" + Allowlist + " with a comment saying why.");
        }

        [Fact]
        public void EveryCompileItemExistsOnDisk()
        {
            var missing = CompileItems()
                .Where(rel => !File.Exists(Path.Combine(Repo.Root, rel.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();

            Assert.True(missing.Count == 0,
                "VPB.csproj lists <Compile Include> entries that do not exist on disk:" + Environment.NewLine +
                Repo.Bullets(missing));
        }

        [Fact]
        public void CompileListHasNoDuplicates()
        {
            var dupes = CompileItems()
                .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + " (x" + g.Count() + ")")
                .ToList();

            Assert.True(dupes.Count == 0,
                "VPB.csproj lists the same source file more than once:" + Environment.NewLine + Repo.Bullets(dupes));
        }

        [Fact]
        public void AllowlistEntriesStillExistOnDisk()
        {
            var stale = Repo.ReadAllowlist(Allowlist)
                .Where(rel => !File.Exists(Path.Combine(Repo.Root, rel.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();

            Assert.True(stale.Count == 0,
                "tests/" + Allowlist + " names files that no longer exist - remove these lines:" + Environment.NewLine +
                Repo.Bullets(stale));
        }
    }
}
