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
    public class ConfigSerializationTests
    {
        private const string ConfigFile = "src/VPBConfig.cs";
        private const string Allowlist = "known-unpersisted-config-fields.txt";

        private readonly ITestOutputHelper _out;
        public ConfigSerializationTests(ITestOutputHelper output) { _out = output; }

        private static ClassDeclarationSyntax ConfigClass()
        {
            SourceFile f = SourceIndex.Files.SingleOrDefault(x =>
                string.Equals(x.RelativePath, ConfigFile, StringComparison.OrdinalIgnoreCase));
            Assert.True(f != null, ConfigFile + " was not found - update ConfigSerializationTests.");

            ClassDeclarationSyntax c = f.Tree.GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .SingleOrDefault(x => x.Identifier.ValueText == "VPBConfig");
            Assert.True(c != null, "class VPBConfig was not found in " + ConfigFile);
            return c;
        }

        private static List<string> PersistableFieldNames(ClassDeclarationSyntax cls)
        {
            var names = new List<string>();
            foreach (FieldDeclarationSyntax fd in cls.Members.OfType<FieldDeclarationSyntax>())
            {
                bool isPublic = fd.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
                bool isStatic = fd.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                bool isConst = fd.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
                if (!isPublic || isStatic || isConst) continue;
                foreach (VariableDeclaratorSyntax v in fd.Declaration.Variables)
                    names.Add(v.Identifier.ValueText);
            }
            return names;
        }

        private static MethodDeclarationSyntax Method(ClassDeclarationSyntax cls, string name, int parameterCount)
        {
            return cls.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == name && m.ParameterList.Parameters.Count == parameterCount);
        }

        private static HashSet<string> SerializationNamesIn(SyntaxNode node)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (IdentifierNameSyntax id in node.DescendantNodes().OfType<IdentifierNameSyntax>())
                names.Add(id.Identifier.ValueText);

            foreach (LiteralExpressionSyntax lit in node.DescendantNodes().OfType<LiteralExpressionSyntax>())
                if (lit.IsKind(SyntaxKind.StringLiteralExpression))
                    names.Add(lit.Token.ValueText);

            return names;
        }

        [Theory]
        [InlineData("Save", 2)]
        [InlineData("Load", 0)]
        public void EveryPublicConfigFieldIsNamedIn(string methodName, int parameterCount)
        {
            ClassDeclarationSyntax cls = ConfigClass();
            MethodDeclarationSyntax method = Method(cls, methodName, parameterCount);
            Assert.True(method != null,
                "VPBConfig." + methodName + " with " + parameterCount + " parameter(s) was not found - " +
                "the serializer was renamed or re-shaped; update ConfigSerializationTests.");

            HashSet<string> mentioned = SerializationNamesIn(method);
            var allowed = new HashSet<string>(Repo.ReadAllowlist(Allowlist), StringComparer.Ordinal);
            List<string> fields = PersistableFieldNames(cls);

            _out.WriteLine("public instance fields on VPBConfig: " + fields.Count);
            _out.WriteLine("identifiers and JSON keys used inside " + methodName + ": " + mentioned.Count);

            var forgotten = fields.Where(n => !mentioned.Contains(n) && !allowed.Contains(n)).ToList();

            Assert.True(forgotten.Count == 0,
                "These public VPBConfig fields appear inside " + methodName + "() neither by name nor as a JSON key." + Environment.NewLine +
                "A setting that is missing from Save() silently resets on the next launch; one missing from Load()" + Environment.NewLine +
                "silently ignores what the user chose. Neither produces an error or a log line:" + Environment.NewLine +
                Repo.Bullets(forgotten) + Environment.NewLine + Environment.NewLine +
                "If a field is intentionally runtime-only, add its name to tests/" + Allowlist + " with a reason.");
        }

        [Fact]
        public void AllowlistedFieldsStillExist()
        {
            var fields = new HashSet<string>(PersistableFieldNames(ConfigClass()), StringComparer.Ordinal);
            var stale = Repo.ReadAllowlist(Allowlist).Where(n => !fields.Contains(n)).ToList();

            Assert.True(stale.Count == 0,
                "tests/" + Allowlist + " names VPBConfig fields that no longer exist - remove these lines:" +
                Environment.NewLine + Repo.Bullets(stale));
        }
    }
}
