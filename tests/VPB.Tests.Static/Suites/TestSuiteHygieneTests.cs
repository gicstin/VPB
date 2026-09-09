using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class TestSuiteHygieneTests
    {
        private readonly ITestOutputHelper _out;
        public TestSuiteHygieneTests(ITestOutputHelper output) { _out = output; }

        private sealed class TestType
        {
            public string RelativePath;
            public ClassDeclarationSyntax Declaration;
            public string Name { get { return Declaration.Identifier.ValueText; } }
        }

        private static IEnumerable<TestType> VamSuiteTypes()
        {
            string dir = Repo.Path_("tests", "VPB.Tests.Vam", "Suites");
            if (!Directory.Exists(dir)) yield break;

            var options = new CSharpParseOptions(LanguageVersion.CSharp7_3);
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file)), options, file);
                foreach (ClassDeclarationSyntax cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!HasTestMethod(cls)) continue;
                    yield return new TestType { RelativePath = Repo.ToRepoRelative(file), Declaration = cls };
                }
            }
        }

        private static bool HasTestMethod(ClassDeclarationSyntax cls)
        {
            foreach (MethodDeclarationSyntax m in cls.Members.OfType<MethodDeclarationSyntax>())
                if (HasAttribute(m.AttributeLists, "Fact") || HasAttribute(m.AttributeLists, "Theory"))
                    return true;
            return false;
        }

        private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name)
        {
            foreach (AttributeListSyntax list in lists)
                foreach (AttributeSyntax attribute in list.Attributes)
                {
                    string text = attribute.Name.ToString();
                    if (text == name || text.EndsWith("." + name, StringComparison.Ordinal)) return true;
                }
            return false;
        }

        [Fact]
        public void EveryVamSuiteJoinsTheSerialisingCollection()
        {
            var offenders = new List<string>();
            int checked_ = 0;

            foreach (TestType type in VamSuiteTypes())
            {
                checked_++;
                if (!HasAttribute(type.Declaration.AttributeLists, "Collection"))
                    offenders.Add(type.RelativePath + "  ->  " + type.Name);
            }

            _out.WriteLine("VaM suite types checked: " + checked_);

            Assert.True(checked_ > 0,
                "No test classes were found under tests/VPB.Tests.Vam/Suites. This guard is not running.");

            Assert.True(offenders.Count == 0,
                "These VaM test classes are missing [Collection(VamCollection.Name)]:" + Environment.NewLine +
                Repo.Bullets(offenders) + Environment.NewLine + Environment.NewLine +
                "The collection is what serialises the suites. TempInstall changes the process working " +
                "directory, so a class outside it runs in parallel with one that is moving the CWD out from " +
                "under it - producing failures that move around between runs and vanish under -Filter.");
        }

        [Fact]
        public void EveryVamSuiteTakesTheFixtureSoTheHarnessIsInstalled()
        {
            var offenders = new List<string>();

            foreach (TestType type in VamSuiteTypes())
            {
                bool takesFixture = type.Declaration.Members
                    .OfType<ConstructorDeclarationSyntax>()
                    .Any(c => c.ParameterList.Parameters.Any(p => p.Type != null && p.Type.ToString() == "VamFixture"));

                if (!takesFixture) offenders.Add(type.RelativePath + "  ->  " + type.Name);
            }

            Assert.True(offenders.Count == 0,
                "These VaM test classes never take a VamFixture in their constructor:" + Environment.NewLine +
                Repo.Bullets(offenders) + Environment.NewLine + Environment.NewLine +
                "VamFixture is what calls HeadlessVam.Install(). Without it the log seam is not patched and " +
                "the first VPB call that logs throws the ECall SecurityException instead of running.");
        }

        [Fact]
        public void EveryRuntimeTestHasTheShapeTheRunnerCanInvoke()
        {
            string dir = Repo.Path_("tests", "VPB.Tests.Runtime", "Suites");
            Assert.True(Directory.Exists(dir),
                "tests/VPB.Tests.Runtime/Suites is gone. Either the in-game tier was deleted - in which case " +
                "delete this guard deliberately - or a move left it behind and every shape check below is " +
                "silently checking nothing.");

            var options = new CSharpParseOptions(LanguageVersion.CSharp7_3);
            var offenders = new List<string>();
            int checked_ = 0;

            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file)), options, file);
                string relative = Repo.ToRepoRelative(file);

                foreach (MethodDeclarationSyntax method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!HasAttribute(method.AttributeLists, "VpbRuntimeTest")) continue;
                    checked_++;

                    var problems = new List<string>();
                    if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) problems.Add("not public");
                    if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) problems.Add("not static");
                    if (method.ParameterList.Parameters.Count != 0) problems.Add("takes parameters");

                    string returnType = method.ReturnType.ToString();
                    if (returnType != "void" && returnType != "IEnumerator" && !returnType.EndsWith(".IEnumerator", StringComparison.Ordinal))
                        problems.Add("returns " + returnType);

                    if (problems.Count > 0)
                        offenders.Add(relative + "  ->  " + method.Identifier.ValueText + "  (" + string.Join(", ", problems.ToArray()) + ")");
                }
            }

            _out.WriteLine("[VpbRuntimeTest] methods checked: " + checked_);

            Assert.True(checked_ > 0,
                "No [VpbRuntimeTest] methods were found. This guard is not running.");

            Assert.True(offenders.Count == 0,
                "These in-game tests do not have the shape RuntimeTestRunner invokes:" + Environment.NewLine +
                Repo.Bullets(offenders) + Environment.NewLine + Environment.NewLine +
                "The runner reflects for public static methods with no parameters returning void or IEnumerator. " +
                "Anything else is discovered, fails to invoke, and reports as an error inside VaM - where nobody " +
                "is watching unless they armed the run.");
        }

        private static IEnumerable<InvocationExpressionSyntax> RuntimeAssertCalls(
            MethodDeclarationSyntax method, Dictionary<string, MethodDeclarationSyntax> siblings)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<MethodDeclarationSyntax>();
            pending.Push(method);
            seen.Add(method.Identifier.ValueText);

            while (pending.Count > 0)
            {
                MethodDeclarationSyntax current = pending.Pop();

                foreach (InvocationExpressionSyntax call in current.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var member = call.Expression as MemberAccessExpressionSyntax;
                    if (member != null && member.Expression.ToString() == "RuntimeAssert")
                    {
                        yield return call;
                        continue;
                    }

                    string callee = member != null ? member.Name.Identifier.ValueText
                                                   : (call.Expression as IdentifierNameSyntax)?.Identifier.ValueText;
                    if (callee == null || !seen.Add(callee)) continue;

                    MethodDeclarationSyntax target;
                    if (siblings.TryGetValue(callee, out target)) pending.Push(target);
                }
            }
        }

        [Fact]
        public void NoRuntimeTestPassesWithoutAssertingAnything()
        {
            string dir = Repo.Path_("tests", "VPB.Tests.Runtime", "Suites");
            Assert.True(Directory.Exists(dir), "tests/VPB.Tests.Runtime/Suites is gone - this guard is not running.");

            var options = new CSharpParseOptions(LanguageVersion.CSharp7_3);
            var constantAsserts = new List<string>();
            var assertless = new List<string>();
            int checked_ = 0;

            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file)), options, file);
                string relative = Repo.ToRepoRelative(file);

                foreach (ClassDeclarationSyntax cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var siblings = new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);
                    foreach (MethodDeclarationSyntax m in cls.Members.OfType<MethodDeclarationSyntax>())
                        siblings[m.Identifier.ValueText] = m;

                    foreach (MethodDeclarationSyntax method in cls.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (!HasAttribute(method.AttributeLists, "VpbRuntimeTest")) continue;
                        checked_++;

                        foreach (InvocationExpressionSyntax call in RuntimeAssertCalls(method, siblings))
                        {
                            var member = (MemberAccessExpressionSyntax)call.Expression;
                            ArgumentSyntax first = call.ArgumentList.Arguments.FirstOrDefault();
                            if (first == null) continue;

                            var literal = first.Expression as LiteralExpressionSyntax;
                            if (literal == null) continue;
                            if (!literal.Token.IsKind(SyntaxKind.TrueKeyword) && !literal.Token.IsKind(SyntaxKind.FalseKeyword)) continue;

                            int line = tree.GetLineSpan(call.Span).StartLinePosition.Line + 1;
                            constantAsserts.Add(relative + ":" + line + "  ->  " + method.Identifier.ValueText +
                                                "  (" + member.Name.Identifier.ValueText + "(" + literal.Token.ValueText + ", ...))");
                        }

                        if (!RuntimeAssertCalls(method, siblings).Any())
                            assertless.Add(relative + "  ->  " + method.Identifier.ValueText);
                    }
                }
            }

            _out.WriteLine("[VpbRuntimeTest] methods checked: " + checked_);
            Assert.True(checked_ > 0, "No [VpbRuntimeTest] methods were found. This guard is not running.");

            Assert.True(constantAsserts.Count == 0,
                "An in-game test asserts a constant:" + Environment.NewLine + Repo.Bullets(constantAsserts) +
                Environment.NewLine + Environment.NewLine +
                "That is how a test passes on an install where its precondition is absent - an empty library, " +
                "an unbuilt index - and reports green for a path it never touched. Use " +
                "RuntimeAssert.Inconclusive(reason), which the runner reports as skipped.");

            Assert.True(assertless.Count == 0,
                "An in-game test calls no RuntimeAssert at all:" + Environment.NewLine + Repo.Bullets(assertless) +
                Environment.NewLine + Environment.NewLine +
                "A test body that only exercises code reports green whatever the code did. It must either " +
                "assert an outcome or declare itself Inconclusive.");
        }

        [Fact]
        public void TestProjectsAreNotReferencedByTheShippedPlugin()
        {
            string csproj = File.ReadAllText(Repo.Path_("VPB.csproj"));

            Assert.DoesNotContain("tests\\VPB.Tests.Static", csproj, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tests\\VPB.Tests.Vam", csproj, StringComparison.OrdinalIgnoreCase);

            var compiled = new HashSet<string>(CompileListTests.CompileItems(), StringComparer.OrdinalIgnoreCase);
            var leaked = compiled.Where(p => p.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)).ToList();

            Assert.True(leaked.Count == 0,
                "VPB.csproj compiles files from tests/, so test code would ship inside VPB.dll:" +
                Environment.NewLine + Repo.Bullets(leaked));
        }
    }
}
