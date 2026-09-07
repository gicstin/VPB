using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class SettingsBindingTests
    {
        private const string SettingsFile = "src/Settings.cs";
        private const string Allowlist = "known-unbound-settings.txt";

        private readonly ITestOutputHelper _out;
        public SettingsBindingTests(ITestOutputHelper output) { _out = output; }

        private static ClassDeclarationSyntax SettingsClass()
        {
            SourceFile file = SourceIndex.Files.SingleOrDefault(f =>
                string.Equals(f.RelativePath, SettingsFile, StringComparison.OrdinalIgnoreCase));
            Assert.True(file != null, SettingsFile + " was not found - update SettingsBindingTests.");

            ClassDeclarationSyntax cls = file.Tree.GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .SingleOrDefault(c => c.Identifier.ValueText == "Settings");
            Assert.True(cls != null, "class Settings was not found in " + SettingsFile);
            return cls;
        }

        private static MethodDeclarationSyntax LoadMethod(ClassDeclarationSyntax cls)
        {
            MethodDeclarationSyntax load = cls.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == "Load");
            Assert.True(load != null,
                "Settings.Load was not found. It is where every ConfigEntry is bound; if it was renamed, " +
                "update this test rather than deleting it.");
            return load;
        }

        private static List<string> ConfigEntryFieldNames(ClassDeclarationSyntax cls)
        {
            var names = new List<string>();
            foreach (FieldDeclarationSyntax field in cls.Members.OfType<FieldDeclarationSyntax>())
            {
                string type = field.Declaration.Type.ToString();
                if (!type.StartsWith("ConfigEntry<", StringComparison.Ordinal)) continue;
                if (field.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) continue;

                foreach (VariableDeclaratorSyntax v in field.Declaration.Variables)
                    names.Add(v.Identifier.ValueText);
            }
            return names;
        }

        private sealed class Binding
        {
            public string Field;
            public string Section;
            public string Key;
            public int Line;
        }

        private static List<Binding> Bindings(SourceFile file, MethodDeclarationSyntax load)
        {
            var bindings = new List<Binding>();

            foreach (AssignmentExpressionSyntax assignment in load.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var invocation = assignment.Right as InvocationExpressionSyntax;
                if (invocation == null) continue;

                string callee = invocation.Expression.ToString();
                if (!callee.EndsWith(".Bind", StringComparison.Ordinal)
                    && callee.IndexOf(".Bind<", StringComparison.Ordinal) < 0) continue;

                var arguments = invocation.ArgumentList.Arguments;
                if (arguments.Count < 2) continue;

                string section = LiteralOf(arguments[0].Expression);
                string key = LiteralOf(arguments[1].Expression);
                if (section == null || key == null) continue;

                bindings.Add(new Binding
                {
                    Field = assignment.Left.ToString(),
                    Section = section,
                    Key = key,
                    Line = file.LineOf(assignment.SpanStart),
                });
            }

            return bindings;
        }

        private static string LiteralOf(ExpressionSyntax expression)
        {
            var literal = expression as LiteralExpressionSyntax;
            return literal != null && literal.IsKind(SyntaxKind.StringLiteralExpression)
                ? literal.Token.ValueText
                : null;
        }

        private static SourceFile SettingsSource()
        {
            return SourceIndex.Files.Single(f =>
                string.Equals(f.RelativePath, SettingsFile, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void EveryConfigEntryFieldIsBoundInLoad()
        {
            ClassDeclarationSyntax cls = SettingsClass();
            List<string> fields = ConfigEntryFieldNames(cls);
            List<Binding> bindings = Bindings(SettingsSource(), LoadMethod(cls));

            var bound = new HashSet<string>(bindings.Select(b => b.Field), StringComparer.Ordinal);
            var allowed = new HashSet<string>(Repo.ReadAllowlist(Allowlist), StringComparer.Ordinal);

            _out.WriteLine("ConfigEntry fields: " + fields.Count + ", bound in Load: " + bound.Count);

            var unbound = fields.Where(f => !bound.Contains(f) && !allowed.Contains(f)).ToList();

            Assert.True(unbound.Count == 0,
                "These Settings.ConfigEntry fields are declared but never bound in Load():" + Environment.NewLine +
                Repo.Bullets(unbound) + Environment.NewLine + Environment.NewLine +
                "An unbound entry stays null, so the first read throws a NullReferenceException inside whatever " +
                "feature uses it - usually far from here and usually only on the machine that enables it." +
                Environment.NewLine +
                "If a field is deliberately bound elsewhere, add its name to tests/" + Allowlist + " with a reason.");
        }

        [Fact]
        public void NoTwoSettingsShareASectionAndKey()
        {
            List<Binding> bindings = Bindings(SettingsSource(), LoadMethod(SettingsClass()));

            var duplicates = bindings
                .GroupBy(b => b.Section + "/" + b.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + "  ->  " + string.Join(", ", g.Select(b => b.Field + " (line " + b.Line + ")").ToArray()))
                .ToList();

            Assert.True(duplicates.Count == 0,
                "These Settings entries bind the same (section, key) more than once:" + Environment.NewLine +
                Repo.Bullets(duplicates) + Environment.NewLine + Environment.NewLine +
                "BepInEx returns the existing entry for a repeated key, so the fields become aliases of one " +
                "value. Changing one setting silently changes the other, and the config file only ever holds one.");
        }

        [Fact]
        public void EveryBoundEntryUsesALiteralSectionAndKey()
        {
            ClassDeclarationSyntax cls = SettingsClass();
            MethodDeclarationSyntax load = LoadMethod(cls);
            SourceFile file = SettingsSource();

            var dynamic = new List<string>();
            foreach (AssignmentExpressionSyntax assignment in load.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var invocation = assignment.Right as InvocationExpressionSyntax;
                if (invocation == null) continue;

                string callee = invocation.Expression.ToString();
                if (!callee.EndsWith(".Bind", StringComparison.Ordinal)
                    && callee.IndexOf(".Bind<", StringComparison.Ordinal) < 0) continue;

                var arguments = invocation.ArgumentList.Arguments;
                if (arguments.Count >= 2 && LiteralOf(arguments[0].Expression) != null && LiteralOf(arguments[1].Expression) != null)
                    continue;

                dynamic.Add(assignment.Left + " (line " + file.LineOf(assignment.SpanStart) + ")");
            }

            Assert.True(dynamic.Count == 0,
                "These Bind calls compute their section or key instead of using string literals:" + Environment.NewLine +
                Repo.Bullets(dynamic) + Environment.NewLine + Environment.NewLine +
                "A computed key cannot be checked for collisions here, and a change to it silently orphans the " +
                "user's saved value under the old name.");
        }

        [Fact]
        public void AllowlistedFieldsStillExist()
        {
            var fields = new HashSet<string>(ConfigEntryFieldNames(SettingsClass()), StringComparer.Ordinal);
            var stale = Repo.ReadAllowlist(Allowlist).Where(n => !fields.Contains(n)).ToList();

            Assert.True(stale.Count == 0,
                "tests/" + Allowlist + " names Settings fields that no longer exist - remove these lines:" +
                Environment.NewLine + Repo.Bullets(stale));
        }
    }
}
