using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class HarmonyPatchTargetTests
    {
        private readonly ITestOutputHelper _out;
        private readonly VamFixture _vam;

        public HarmonyPatchTargetTests(VamFixture vam, ITestOutputHelper output)
        {
            _vam = vam;
            _out = output;
        }

        [Fact]
        public void EveryHarmonyPatchAttributeResolvesToARealVamMethod()
        {
            var broken = new List<string>();
            int checkedTargets = 0;

            foreach (Type type in SafeTypes(typeof(HarmonyPatchTargetTests).Assembly))
            {
                List<HarmonyMethod> classLevel = AttributesOn(type);

                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    List<HarmonyMethod> methodLevel = AttributesOn(method);
                    if (methodLevel.Count == 0) continue;

                    var merged = new List<HarmonyMethod>(classLevel);
                    merged.AddRange(methodLevel);
                    HarmonyMethod spec = Merge(merged);
                    checkedTargets++;

                    try
                    {
                        if (Resolve(spec) == null)
                            broken.Add(type.FullName + "." + method.Name + "  ->  " + Describe(spec));
                    }
                    catch (Exception ex)
                    {
                        broken.Add(type.FullName + "." + method.Name + "  ->  " + Describe(spec) +
                                   "   [" + HeadlessVam.DescribeUnwrapped(ex) + "]");
                    }
                }
            }

            _out.WriteLine("VaM install: " + _vam.VaMPath);
            _out.WriteLine("[HarmonyPatch] targets checked: " + checkedTargets);

            Assert.True(checkedTargets > 0,
                "No [HarmonyPatch] attributes were found in the test assembly. The VPB sources were probably not " +
                "imported - check the VpbImportSources target in VPB.Tests.Vam.csproj.");

            Assert.True(broken.Count == 0,
                "These Harmony patches no longer resolve against the VaM assemblies in " + _vam.VaMPath + "." + Environment.NewLine +
                "In the game they install as no-ops or throw at patch time - the hooked behaviour just stops" + Environment.NewLine +
                "happening, with nothing in the log to say so:" + Environment.NewLine +
                Bullets(broken));
        }

        [Fact]
        public void PatchClassesDeclareTargetsRatherThanRelyingOnTypeByNameOnly()
        {
            var unresolvable = new List<string>();
            foreach (Type type in SafeTypes(typeof(HarmonyPatchTargetTests).Assembly))
            {
                foreach (HarmonyPatch a in type.GetCustomAttributes(typeof(HarmonyPatch), true))
                {
                    if (a.info == null) continue;
                    if (a.info.declaringType == null && !string.IsNullOrEmpty(a.info.methodName))
                        unresolvable.Add(type.FullName + " -> " + a.info.methodName + " (no declaring type)");
                }
            }

            Assert.True(unresolvable.Count == 0,
                "Class-level [HarmonyPatch] attributes name a method without a declaring type:" + Environment.NewLine +
                Bullets(unresolvable));
        }

        private static List<HarmonyMethod> AttributesOn(MemberInfo member)
        {
            var list = new List<HarmonyMethod>();
            foreach (HarmonyPatch a in member.GetCustomAttributes(typeof(HarmonyPatch), true))
                if (a.info != null) list.Add(a.info);
            return list;
        }

        private static HarmonyMethod Merge(List<HarmonyMethod> parts)
        {
            var result = new HarmonyMethod();
            foreach (HarmonyMethod p in parts)
            {
                if (p == null) continue;
                if (p.declaringType != null) result.declaringType = p.declaringType;
                if (!string.IsNullOrEmpty(p.methodName)) result.methodName = p.methodName;
                if (p.argumentTypes != null) result.argumentTypes = p.argumentTypes;
                if (p.methodType.HasValue) result.methodType = p.methodType;
            }
            return result;
        }

        private static MethodBase Resolve(HarmonyMethod spec)
        {
            if (spec == null || spec.declaringType == null) return null;

            MethodType kind = spec.methodType ?? MethodType.Normal;
            switch (kind)
            {
                case MethodType.Constructor:
                    return AccessTools.Constructor(spec.declaringType, spec.argumentTypes);
                case MethodType.StaticConstructor:
                    return AccessTools.GetDeclaredConstructors(spec.declaringType, true)
                        .FirstOrDefault(c => c.IsStatic);
                case MethodType.Getter:
                    return AccessTools.PropertyGetter(spec.declaringType, spec.methodName);
                case MethodType.Setter:
                    return AccessTools.PropertySetter(spec.declaringType, spec.methodName);
            }

            if (string.IsNullOrEmpty(spec.methodName)) return null;
            return spec.argumentTypes != null
                ? AccessTools.Method(spec.declaringType, spec.methodName, spec.argumentTypes)
                : AccessTools.Method(spec.declaringType, spec.methodName);
        }

        private static string Describe(HarmonyMethod spec)
        {
            string args = "(any overload)";
            if (spec.argumentTypes != null)
            {
                var names = new string[spec.argumentTypes.Length];
                for (int i = 0; i < spec.argumentTypes.Length; i++) names[i] = spec.argumentTypes[i].Name;
                args = "(" + string.Join(", ", names) + ")";
            }
            string owner = spec.declaringType == null ? "<no declaring type>" : spec.declaringType.FullName;
            return owner + "." + (spec.methodName ?? "<no method name>") + args;
        }

        internal static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        }

        internal static string Bullets(IEnumerable<string> items, int max = 40)
        {
            var list = items.ToList();
            string s = string.Join(Environment.NewLine, list.Take(max).Select(i => "  - " + i).ToArray());
            if (list.Count > max) s += Environment.NewLine + "  ... and " + (list.Count - max) + " more";
            return s;
        }
    }
}
