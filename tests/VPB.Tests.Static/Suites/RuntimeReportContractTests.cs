using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class RuntimeReportContractTests
    {
        private const string RunnerFile = "tests/VPB.Tests.Runtime/Runtime/RuntimeTestRunner.cs";
        private const string ReaderFile = "tests/run.ps1";

        private readonly ITestOutputHelper _out;
        public RuntimeReportContractTests(ITestOutputHelper output) { _out = output; }

        private static string Runner()
        {
            string path = Repo.Path_(RunnerFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), RunnerFile + " is missing - update RuntimeReportContractTests.");
            return File.ReadAllText(path);
        }

        private static string Reader()
        {
            string path = Repo.Path_(ReaderFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), ReaderFile + " is missing - update RuntimeReportContractTests.");
            return File.ReadAllText(path);
        }

        /// <summary>Reader source with comment lines dropped, so prose explaining a past bug is not
        /// mistaken for the bug.</summary>
        private static string ReaderCode()
        {
            var kept = new List<string>();
            foreach (string line in Reader().Split('\n'))
            {
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                kept.Add(line);
            }
            return string.Join("\n", kept.ToArray());
        }

        [Fact]
        public void TheReaderExpectsTheRootElementTheRunnerWrites()
        {
            Match written = Regex.Match(Runner(), @"AppendLine\(""<\?xml[\s\S]{0,400}?<(?<root>[a-zA-Z]+)");
            Assert.True(written.Success,
                "Could not find the JUnit document element in " + RunnerFile + ". If BuildJUnitXml was " +
                "restructured, update this contract test rather than deleting it.");

            string root = written.Groups["root"].Value;
            _out.WriteLine("runner writes <" + root + ">");

            string reader = ReaderCode();
            Assert.Contains("'" + root + "'", reader);

            Assert.DoesNotContain("$xml." + root + "s", reader);
            Assert.True(reader.IndexOf("$xml.DocumentElement", StringComparison.Ordinal) >= 0,
                "run.ps1 must reach the report root through $xml.DocumentElement. Navigating by a guessed " +
                "property name yields $null when it is wrong, [int]$null is 0, and a failed in-game run " +
                "then exits 0.");
        }

        [Fact]
        public void TheReaderAndTheRunnerAgreeOnTheReportFileName()
        {
            Match written = Regex.Match(Runner(), @"""(?<file>[\w\-.]+\.xml)""");
            Assert.True(written.Success, "No report file name found in " + RunnerFile);

            string fileName = written.Groups["file"].Value;
            _out.WriteLine("runner writes " + fileName);

            Assert.Contains(fileName, Reader());
        }

        [Fact]
        public void TheReaderReadsFailuresThroughLocalNameNotName()
        {
            Assert.Contains("LocalName", ReaderCode());

            Assert.DoesNotContain("$suite.Name -ne", ReaderCode());
            _out.WriteLine("PowerShell's XML adapter shadows .Name with the element's own 'name' attribute,");
            _out.WriteLine("so $suite.Name on <testsuite name=\"VPB.Tests.Runtime\"> returns the suite name,");
            _out.WriteLine("never 'testsuite'. LocalName is the element's real name.");
        }

        [Fact]
        public void TheReaderFailsTheRunWhenTheReportRecordsFailures()
        {
            string reader = ReaderCode();

            Assert.True(Regex.IsMatch(reader, @"\$failures\s+-gt\s+0"),
                "run.ps1 must exit non-zero when the report records failures. Without it the reader prints " +
                "FAILED lines and still exits 0, which is exactly the green-washing the in-game tier exists " +
                "to avoid.");

            Assert.Contains("exit 1", reader);
        }

        [Fact]
        public void TheRunnerCountsSkippedSeparatelyFromPassed()
        {
            string runner = Runner();

            Assert.Contains("skipped=", runner);
            Assert.True(Regex.IsMatch(runner, @"<skipped message="),
                "The runner must emit a <skipped> element with its reason, or a test whose precondition was " +
                "absent is indistinguishable from one that ran.");
        }
    }
}
