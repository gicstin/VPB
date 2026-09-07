using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ReflectionLookupTests
    {
        private const string TypeAllowlist = "known-unresolvable-types.txt";

        private const string ArgumentSpec =
            @"(?:\s*,\s*(?<empty>Type\.EmptyTypes)|\s*,\s*new\s*(?:[\w\.]*\s*\[\s*\])\s*\{(?<args>[^}]*)\})?";

        private static readonly Regex MethodCall = new Regex(
            @"AccessTools\.Method\(\s*typeof\(\s*(?<type>[\w\.]+)\s*\)\s*,\s*""(?<member>[^""]+)""" + ArgumentSpec,
            RegexOptions.Compiled);

        private static readonly Regex FieldCall = new Regex(
            @"AccessTools\.Field\(\s*typeof\(\s*(?<type>[\w\.]+)\s*\)\s*,\s*""(?<member>[^""]+)""",
            RegexOptions.Compiled);

        private static readonly Regex PropertyCall = new Regex(
            @"AccessTools\.(?:Property|PropertyGetter|PropertySetter)\(\s*typeof\(\s*(?<type>[\w\.]+)\s*\)\s*,\s*""(?<member>[^""]+)""",
            RegexOptions.Compiled);

        private static readonly Regex TypeByNameCall = new Regex(
            @"AccessTools\.TypeByName\(\s*""(?<type>[^""]+)""\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex TypeOfArgument = new Regex(@"^\s*typeof\(\s*([\w\.]+)\s*\)\s*$", RegexOptions.Compiled);

        private static readonly Regex VariableReceiverCall = new Regex(
            @"AccessTools\.(?<kind>Method|Field|Property|PropertyGetter|PropertySetter)\(\s*(?<var>[A-Za-z_]\w*)\s*,\s*""(?<member>[^""]+)""",
            RegexOptions.Compiled);

        private static readonly Regex TypeVariableFromTypeOf = new Regex(
            @"(?:^|[^\w.])(?:var|Type)\s+(?<var>[A-Za-z_]\w*)\s*=\s*typeof\(\s*(?<type>[\w\.]+)\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex TypeVariableFromTypeByName = new Regex(
            @"(?:^|[^\w.])(?:var|Type)\s+(?<var>[A-Za-z_]\w*)\s*=\s*AccessTools\.TypeByName\(\s*""(?<type>[^""]+)""\s*\)",
            RegexOptions.Compiled);

        private readonly ITestOutputHelper _out;
        public ReflectionLookupTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private sealed class Site
        {
            public string TypeName;
            public string MemberName;
            public Type[] ArgumentTypes;
            public bool ArgumentsKnown;
            public string Where;

            public override string ToString()
            {
                string args = ArgumentsKnown
                    ? "(" + string.Join(", ", ArgumentTypes.Select(a => a.Name).ToArray()) + ")"
                    : "(any overload)";
                return TypeName + "." + MemberName + args + "   (" + Where + ")";
            }
        }

        private static IEnumerable<string> SourceFiles()
        {
            return Directory.GetFiles(Path.Combine(TestEnvironment.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories);
        }

        private static string Relative(string path)
        {
            string root = TestEnvironment.RepoRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(root.Length).Replace('\\', '/')
                : path;
        }

        private static int LineAt(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private static List<Site> Scan(Regex pattern)
        {
            var sites = new List<Site>();
            foreach (string path in SourceFiles())
            {
                string text = File.ReadAllText(path);
                foreach (Match m in pattern.Matches(text))
                {
                    var site = new Site
                    {
                        TypeName = m.Groups["type"].Value,
                        MemberName = m.Groups["member"].Value,
                        Where = Relative(path) + ":" + LineAt(text, m.Index),
                    };
                    ResolveArgumentTypes(m, site);
                    sites.Add(site);
                }
            }
            return sites;
        }

        private static void ResolveArgumentTypes(Match m, Site site)
        {
            if (m.Groups["empty"].Success)
            {
                site.ArgumentTypes = Type.EmptyTypes;
                site.ArgumentsKnown = true;
                return;
            }

            Group args = m.Groups["args"];
            if (!args.Success) return;

            string body = args.Value.Trim();
            if (body.Length == 0)
            {
                site.ArgumentTypes = Type.EmptyTypes;
                site.ArgumentsKnown = true;
                return;
            }

            var parsed = new List<Type>();
            foreach (string part in body.Split(','))
            {
                Match one = TypeOfArgument.Match(part);
                if (!one.Success) return;
                Type t = ResolveType(one.Groups[1].Value);
                if (t == null) return;
                parsed.Add(t);
            }

            site.ArgumentTypes = parsed.ToArray();
            site.ArgumentsKnown = true;
        }

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Type> CSharpAliases = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            { "bool", typeof(bool) }, { "byte", typeof(byte) }, { "sbyte", typeof(sbyte) },
            { "char", typeof(char) }, { "short", typeof(short) }, { "ushort", typeof(ushort) },
            { "int", typeof(int) }, { "uint", typeof(uint) }, { "long", typeof(long) },
            { "ulong", typeof(ulong) }, { "float", typeof(float) }, { "double", typeof(double) },
            { "decimal", typeof(decimal) }, { "string", typeof(string) }, { "object", typeof(object) },
            { "void", typeof(void) },
        };

        private static Type ResolveType(string name)
        {
            Type alias;
            if (CSharpAliases.TryGetValue(name, out alias)) return alias;

            Type cached;
            if (TypeCache.TryGetValue(name, out cached)) return cached;

            HeadlessVam.EnsureVamAssembliesLoaded();

            Type resolved = SafeTypeByName(name);
            if (resolved == null)
            {
                var exact = new List<Type>();
                var bySimpleName = new List<Type>();
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (Type candidate in HarmonyPatchTargetTests.SafeTypes(a))
                    {
                        if (candidate.FullName == name) exact.Add(candidate);
                        else if (candidate.Name == name) bySimpleName.Add(candidate);
                    }
                }
                if (exact.Count == 1) resolved = exact[0];
                else if (exact.Count == 0 && bySimpleName.Count == 1) resolved = bySimpleName[0];
            }

            TypeCache[name] = resolved;
            return resolved;
        }

        private static Type SafeTypeByName(string name)
        {
            try
            {
                Type t = Type.GetType(name, false);
                if (t != null) return t;
            }
            catch { }

            try { return AccessTools.TypeByName(name); }
            catch { return null; }
        }

        private static HashSet<string> Allowlist()
        {
            string path = Path.Combine(Path.Combine(TestEnvironment.RepoRoot, "tests"), TypeAllowlist);
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return set;
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) set.Add(t);
            }
            return set;
        }

        private static bool HasMethod(Type type, Site site)
        {
            if (site.ArgumentsKnown)
            {
                try { if (AccessTools.Method(type, site.MemberName, site.ArgumentTypes) != null) return true; }
                catch { }
            }

            try
            {
                foreach (MethodInfo mi in type.GetMethods(AccessTools.all))
                    if (mi.Name == site.MemberName) return true;
            }
            catch { }

            return false;
        }

        private static bool HasField(Type type, Site site)
        {
            try { return AccessTools.Field(type, site.MemberName) != null; }
            catch { return false; }
        }

        private static bool HasProperty(Type type, Site site)
        {
            try { return AccessTools.Property(type, site.MemberName) != null; }
            catch { return false; }
        }

        [Fact]
        public void EveryAccessToolsMethodLookupResolves()
        {
            Check(Scan(MethodCall), "method", HasMethod);
        }

        [Fact]
        public void EveryAccessToolsFieldLookupResolves()
        {
            Check(Scan(FieldCall), "field", HasField);
        }

        [Fact]
        public void EveryAccessToolsPropertyLookupResolves()
        {
            Check(Scan(PropertyCall), "property", HasProperty);
        }

        [Fact]
        public void LookupsThroughATypeVariableAlsoResolve()
        {
            HashSet<string> allowedTypes = Allowlist();
            HashSet<string> allowedSites = SiteAllowlist();

            var missingMembers = new List<string>();
            var unresolvableReceivers = new List<string>();
            int total = 0;
            int resolved = 0;

            foreach (string path in SourceFiles())
            {
                string text = File.ReadAllText(path);
                if (text.IndexOf("AccessTools.", StringComparison.Ordinal) < 0) continue;

                Dictionary<string, string> receivers = TypeVariablesIn(text);

                foreach (Match m in VariableReceiverCall.Matches(text))
                {
                    string variable = m.Groups["var"].Value;
                    if (variable == "typeof") continue;

                    total++;
                    string where = Relative(path) + ":" + LineAt(text, m.Index);
                    string member = m.Groups["member"].Value;
                    string site = variable + "." + member;

                    string typeName;
                    if (!receivers.TryGetValue(variable, out typeName))
                    {
                        if (!allowedSites.Contains(site)) unresolvableReceivers.Add(site + "   (" + where + ")");
                        continue;
                    }

                    if (allowedTypes.Contains(typeName)) { resolved++; continue; }

                    Type type = ResolveType(typeName);
                    if (type == null)
                    {
                        if (!allowedSites.Contains(site)) unresolvableReceivers.Add(site + " -> " + typeName + "   (" + where + ")");
                        continue;
                    }

                    var probe = new Site { TypeName = typeName, MemberName = member, Where = where };
                    string kind = m.Groups["kind"].Value;
                    bool ok = kind == "Field" ? HasField(type, probe)
                            : kind == "Method" ? HasMethod(type, probe)
                            : HasProperty(type, probe);

                    if (ok) resolved++;
                    else missingMembers.Add(typeName + "." + member + "   (" + where + ")");
                }
            }

            _out.WriteLine("AccessTools lookups through a Type variable: " + total + ", resolved: " + resolved);

            Assert.True(missingMembers.Count == 0,
                "These AccessTools lookups go through a Type variable and the member does not exist on it:" +
                Environment.NewLine + HarmonyPatchTargetTests.Bullets(missingMembers));

            Assert.True(unresolvableReceivers.Count == 0,
                "These AccessTools lookups use a Type variable this test cannot trace to a typeof() or" + Environment.NewLine +
                "TypeByName(\"...\") in the same file, so the member behind them is NOT verified:" + Environment.NewLine +
                HarmonyPatchTargetTests.Bullets(unresolvableReceivers) + Environment.NewLine + Environment.NewLine +
                "Assign the type from typeof(...) or AccessTools.TypeByName(\"...\") at the call site, or add" +
                Environment.NewLine + "'<variable>.<member>' to tests/" + SiteAllowlistFile + " with a reason.");
        }

        private static Dictionary<string, string> TypeVariablesIn(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in TypeVariableFromTypeOf.Matches(text))
                map[m.Groups["var"].Value] = m.Groups["type"].Value;
            foreach (Match m in TypeVariableFromTypeByName.Matches(text))
                map[m.Groups["var"].Value] = m.Groups["type"].Value;
            return map;
        }

        private const string SiteAllowlistFile = "known-untraceable-lookups.txt";

        private static HashSet<string> SiteAllowlist()
        {
            string path = Path.Combine(Path.Combine(TestEnvironment.RepoRoot, "tests"), SiteAllowlistFile);
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return set;
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) set.Add(t);
            }
            return set;
        }

        [Fact]
        public void EveryTypeByNameStringResolves()
        {
            HashSet<string> allowed = Allowlist();
            var missing = new List<string>();
            int total = 0;

            foreach (string path in SourceFiles())
            {
                string text = File.ReadAllText(path);
                foreach (Match m in TypeByNameCall.Matches(text))
                {
                    total++;
                    string name = m.Groups["type"].Value;
                    if (allowed.Contains(name)) continue;
                    if (ResolveType(name) == null)
                        missing.Add(name + "   (" + Relative(path) + ":" + LineAt(text, m.Index) + ")");
                }
            }

            _out.WriteLine("AccessTools.TypeByName string literals checked: " + total);
            Assert.True(missing.Count == 0,
                "AccessTools.TypeByName() is called with names that do not exist in the VaM assemblies." + Environment.NewLine +
                "These return null and the dependent hook is silently skipped:" + Environment.NewLine +
                HarmonyPatchTargetTests.Bullets(missing) + Environment.NewLine + Environment.NewLine +
                "If the type belongs to an optional third-party plugin, add it to tests/" + TypeAllowlist + ".");
        }

        private static string Capitalize(string s)
        {
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static int CountCallSites(string kindLabel)
        {
            var probe = new Regex(@"AccessTools\.(" + Capitalize(kindLabel) + @"|" + Capitalize(kindLabel) + @"Getter|" +
                                  Capitalize(kindLabel) + @"Setter)\s*\(", RegexOptions.Compiled);
            int n = 0;
            foreach (string path in SourceFiles()) n += probe.Matches(File.ReadAllText(path)).Count;
            return n;
        }

        private void Check(List<Site> sites, string kindLabel, Func<Type, Site, bool> resolves)
        {
            HashSet<string> allowed = Allowlist();
            var unresolvableTypes = new List<string>();
            var missingMembers = new List<string>();
            int resolved = 0;
            int overloadChecked = 0;

            foreach (Site s in sites)
            {
                Type t = ResolveType(s.TypeName);
                if (t == null)
                {
                    if (!allowed.Contains(s.TypeName)) unresolvableTypes.Add(s.ToString());
                    continue;
                }
                if (resolves(t, s))
                {
                    resolved++;
                    if (s.ArgumentsKnown) overloadChecked++;
                    continue;
                }
                missingMembers.Add(s.ToString());
            }

            _out.WriteLine("AccessTools " + kindLabel + " lookups with a typeof() receiver: " + sites.Count +
                           ", resolved: " + resolved + ", of which overload-exact: " + overloadChecked);
            _out.WriteLine("AccessTools." + Capitalize(kindLabel) + "( call sites in src/: " + CountCallSites(kindLabel) +
                           " (the remainder take a Type variable and are covered by LookupsThroughATypeVariableAlsoResolve)");

            Assert.True(missingMembers.Count == 0,
                "These AccessTools " + kindLabel + " lookups return null against the VaM assemblies in " +
                TestEnvironment.VaMPath + "." + Environment.NewLine +
                "The name is a string, so nothing fails at compile time - the dependent hook just never installs:" +
                Environment.NewLine + HarmonyPatchTargetTests.Bullets(missingMembers));

            Assert.True(unresolvableTypes.Count == 0,
                "These AccessTools " + kindLabel + " lookups name a type this test could not resolve, so the member" +
                Environment.NewLine + "behind it was NOT verified:" + Environment.NewLine +
                HarmonyPatchTargetTests.Bullets(unresolvableTypes) + Environment.NewLine + Environment.NewLine +
                "Prefer fully qualifying the type at the call site; otherwise add it to tests/" + TypeAllowlist + ".");
        }
    }
}
