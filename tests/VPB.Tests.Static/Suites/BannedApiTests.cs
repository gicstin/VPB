using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class BannedApiTests
    {
        private readonly ITestOutputHelper _out;
        public BannedApiTests(ITestOutputHelper output) { _out = output; }

        public sealed class Rule
        {
            public string Name;
            public string Pattern;
            public string Why;
            public Rule(string name, string pattern, string why) { Name = name; Pattern = pattern; Why = why; }
            public override string ToString() { return Name; }
        }

        public static readonly Rule[] Rules =
        {
            new Rule("UnityEngine.Random",
                @"\bUnityEngine\.Random\b|(?<![\w.])Random\.(Range|value|insideUnitSphere|ColorHSV|rotation)\b",
                "VPB uses VpbRandom (xoshiro128**) everywhere - UnityEngine.Random is main-thread-only and was deliberately removed."),

            new Rule("new System.Random",
                @"\bnew\s+(System\.)?Random\s*\(",
                "Time-seeded System.Random collides across threads and across quick successive calls; use VpbRandom."),

            new Rule("System.Threading.Tasks",
                @"System\.Threading\.Tasks|(?<![\w])async\s+(void|Task|ValueTask)\b|(?<![\w])await\s",
                "The TPL and async/await do not exist on the .NET 3.5 profile VaM runs."),

            new Rule("Concurrent collections",
                @"\bConcurrent(Dictionary|Queue|Bag|Stack)\s*<",
                "System.Collections.Concurrent is .NET 4.0+; use lock + Dictionary."),

            new Rule("Span/Memory",
                @"\b(ReadOnly)?(Span|Memory)\s*<",
                "Span<T>/Memory<T> require a modern BCL and runtime support that Mono 2018 does not have."),

            new Rule("Array.Empty",
                @"\bArray\.Empty\s*<",
                "Array.Empty<T>() is .NET 4.6+."),

            new Rule("Lazy<T>",
                @"\bLazy\s*<",
                "System.Lazy<T> is .NET 4.0+."),

            new Rule("System.Tuple",
                @"\b(System\.)?Tuple\s*<|\bTuple\.Create\s*<?\(",
                "System.Tuple is .NET 4.0+."),

            new Rule("IReadOnly* interfaces",
                @"\bIReadOnly(List|Collection|Dictionary)\s*<",
                "IReadOnlyList<T> and friends are .NET 4.5+."),

            new Rule("Directory.Enumerate*",
                @"\bDirectory\.Enumerate\w+\s*\(",
                "Directory.EnumerateFiles/Directories are .NET 4.0+; use GetFiles/GetDirectories."),

            new Rule("File.ReadLines",
                @"\bFile\.ReadLines\s*\(",
                "File.ReadLines is .NET 4.0+; use ReadAllLines or a StreamReader."),

            new Rule("CallerMemberName",
                @"\bCaller(MemberName|FilePath|LineNumber)\b",
                "Caller info attributes are .NET 4.5+."),

            new Rule("Path.Combine with 3+ parts",
                @"Path\.Combine\s*\([^();]*,[^();]*,[^();]*\)",
                "The 3- and 4-argument Path.Combine overloads are .NET 4.0+; nest two-argument calls."),

            new Rule("string.Join<T>",
                @"string\.Join\s*<",
                "The generic string.Join<T> overload is .NET 4.0+."),
        };

        public static IEnumerable<object[]> RuleCases()
        {
            foreach (var r in Rules) yield return new object[] { r.Name };
        }

        [Theory]
        [MemberData(nameof(RuleCases))]
        public void SourceTreeIsFreeOf(string ruleName)
        {
            Rule rule = Rules.Single(r => r.Name == ruleName);
            var regex = new Regex(rule.Pattern, RegexOptions.Compiled);
            var hits = new List<string>();

            foreach (SourceFile f in SourceIndex.Files)
            {
                foreach (Match m in regex.Matches(f.Text))
                {
                    if (f.IsInert(m.Index)) continue;
                    hits.Add(f.RelativePath + ":" + f.LineOf(m.Index) + "  " + m.Value.Trim());
                }
            }

            Assert.True(hits.Count == 0,
                "Banned API '" + rule.Name + "' appears in VPB source." + Environment.NewLine +
                rule.Why + Environment.NewLine +
                Repo.Bullets(hits));
        }

        [Fact]
        public void NoValueTupleSyntax()
        {
            var hits = new List<string>();
            foreach (SourceFile f in SourceIndex.Files)
            {
                foreach (SyntaxNode n in f.Tree.GetRoot().DescendantNodes())
                {
                    if (n is TupleTypeSyntax || n is TupleExpressionSyntax)
                        hits.Add(f.RelativePath + ":" + f.LineOf(n.SpanStart) + "  " + n.ToString().Replace('\n', ' ').Trim());
                }
            }

            Assert.True(hits.Count == 0,
                "C# tuple syntax needs System.ValueTuple, which the .NET 3.5 profile does not ship:" + Environment.NewLine +
                Repo.Bullets(hits));
        }

        [Fact]
        public void SourceTreeParsesWithoutSyntaxErrors()
        {
            var bad = new List<string>();
            foreach (SourceFile f in SourceIndex.Files)
            {
                foreach (Diagnostic d in f.Tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
                    bad.Add(f.RelativePath + ":" + f.LineOf(d.Location.SourceSpan.Start) + "  " + d.GetMessage());
            }

            _out.WriteLine("parsed " + SourceIndex.Files.Count + " source files");
            Assert.True(bad.Count == 0, "C# 7.3 parse errors in the source tree:" + Environment.NewLine + Repo.Bullets(bad));
        }
    }
}
