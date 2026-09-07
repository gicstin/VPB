using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace VPB.Tests.Runtime
{
    [VpbRuntimeSuite(Name = "HarmonyInstall", Order = 0)]
    public static class HarmonyInstallSuite
    {
        [VpbRuntimeTest]
        public static void VpbHasPatchedSomething()
        {
            int patched = CountVpbPatchedMethods();
            RuntimeAssert.True(patched > 0,
                "No patched method in this process carries a VPB owner id. Either the plugin failed to install " +
                "its Harmony patches, or the owner id changed and this suite can no longer see them.");
        }

        [VpbRuntimeTest]
        public static void EveryDeclaredPatchAttributeIsInstalledOnItsTarget()
        {
            Assembly plugin = FindVpbAssembly();
            RuntimeAssert.NotNull(plugin, "Could not find the loaded VPB assembly in this AppDomain.");

            var missing = new List<string>();
            int declared = 0;

            foreach (Type type in SafeTypes(plugin))
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
                    declared++;

                    MethodBase target = Resolve(spec);
                    if (target == null)
                    {
                        missing.Add(Describe(spec) + "  (target no longer resolves)");
                        continue;
                    }

                    if (!IsPatchedBy(target, method))
                        missing.Add(Describe(spec) + "  (declared by " + type.Name + "." + method.Name + ", not installed)");
                }
            }

            RuntimeAssert.True(declared > 0, "Found no [HarmonyPatch] attributes on the loaded VPB assembly.");
            RuntimeAssert.True(missing.Count == 0,
                "These patches are declared but are not installed on their target in the running game:"
                + Environment.NewLine + Bullets(missing));
        }

        [VpbRuntimeTest]
        public static void PatchedMethodsAreNotDoublePatchedByVpb()
        {
            var doubled = new List<string>();

            foreach (MethodBase target in Harmony.GetAllPatchedMethods())
            {
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;

                string targetName = target.DeclaringType != null
                    ? target.DeclaringType.Name + "." + target.Name
                    : target.Name;

                CollectRepeats(info.Prefixes, targetName, "prefix", doubled);
                CollectRepeats(info.Postfixes, targetName, "postfix", doubled);
                CollectRepeats(info.Transpilers, targetName, "transpiler", doubled);
                CollectRepeats(info.Finalizers, targetName, "finalizer", doubled);
            }

            RuntimeAssert.True(doubled.Count == 0,
                "VPB installed the same patch method more than once on these targets, so every hooked side "
                + "effect happens twice - usually because PatchAll ran twice:"
                + Environment.NewLine + Bullets(doubled));
        }

        private static void CollectRepeats(System.Collections.Generic.IList<Patch> patches,
                                           string targetName, string kind, List<string> into)
        {
            if (patches == null) return;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < patches.Count; i++)
            {
                Patch p = patches[i];
                if (p == null || p.PatchMethod == null || !IsVpbOwner(p.owner)) continue;

                string key = (p.PatchMethod.DeclaringType != null ? p.PatchMethod.DeclaringType.FullName + "." : "")
                             + p.PatchMethod.Name;

                int seen;
                counts[key] = counts.TryGetValue(key, out seen) ? seen + 1 : 1;
            }

            foreach (KeyValuePair<string, int> kv in counts)
                if (kv.Value > 1)
                    into.Add(targetName + "  <-  " + kind + " " + kv.Key + " x" + kv.Value);
        }

        private static int CountVpbOwners(System.Collections.Generic.IList<Patch> patches)
        {
            if (patches == null) return 0;
            int n = 0;
            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i] != null && IsVpbOwner(patches[i].owner)) n++;
            }
            return n;
        }

        private static bool IsVpbOwner(string owner)
        {
            return !string.IsNullOrEmpty(owner) && owner.IndexOf("VPB", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountVpbPatchedMethods()
        {
            int n = 0;
            foreach (MethodBase target in Harmony.GetAllPatchedMethods())
            {
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                if (CountVpbOwners(info.Prefixes) > 0 || CountVpbOwners(info.Postfixes) > 0
                    || CountVpbOwners(info.Transpilers) > 0 || CountVpbOwners(info.Finalizers) > 0)
                    n++;
            }
            return n;
        }

        private static bool IsPatchedBy(MethodBase target, MethodInfo patchMethod)
        {
            Patches info = Harmony.GetPatchInfo(target);
            if (info == null) return false;

            return Mentions(info.Prefixes, patchMethod)
                || Mentions(info.Postfixes, patchMethod)
                || Mentions(info.Transpilers, patchMethod)
                || Mentions(info.Finalizers, patchMethod);
        }

        private static bool Mentions(System.Collections.Generic.IList<Patch> patches, MethodInfo patchMethod)
        {
            if (patches == null) return false;
            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i] != null && patches[i].PatchMethod == patchMethod) return true;
            }
            return false;
        }

        private static Assembly FindVpbAssembly()
        {
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                AssemblyName name = all[i].GetName();
                if (string.Equals(name.Name, "VPB", StringComparison.OrdinalIgnoreCase)) return all[i];
            }
            return null;
        }

        private static List<HarmonyMethod> AttributesOn(MemberInfo member)
        {
            var list = new List<HarmonyMethod>();
            object[] attrs = member.GetCustomAttributes(typeof(HarmonyPatch), true);
            for (int i = 0; i < attrs.Length; i++)
            {
                var patch = attrs[i] as HarmonyPatch;
                if (patch != null && patch.info != null) list.Add(patch.info);
            }
            return list;
        }

        private static HarmonyMethod Merge(List<HarmonyMethod> parts)
        {
            var result = new HarmonyMethod();
            for (int i = 0; i < parts.Count; i++)
            {
                HarmonyMethod p = parts[i];
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
            if (kind == MethodType.Constructor) return AccessTools.Constructor(spec.declaringType, spec.argumentTypes);
            if (kind == MethodType.Getter) return AccessTools.PropertyGetter(spec.declaringType, spec.methodName);
            if (kind == MethodType.Setter) return AccessTools.PropertySetter(spec.declaringType, spec.methodName);
            if (string.IsNullOrEmpty(spec.methodName)) return null;

            return spec.argumentTypes != null
                ? AccessTools.Method(spec.declaringType, spec.methodName, spec.argumentTypes)
                : AccessTools.Method(spec.declaringType, spec.methodName);
        }

        private static string Describe(HarmonyMethod spec)
        {
            string owner = spec.declaringType == null ? "<no type>" : spec.declaringType.FullName;
            return owner + "." + (spec.methodName ?? "<no name>");
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                var list = new List<Type>();
                if (ex.Types != null)
                {
                    for (int i = 0; i < ex.Types.Length; i++)
                        if (ex.Types[i] != null) list.Add(ex.Types[i]);
                }
                return list;
            }
        }

        private static string Bullets(List<string> items)
        {
            var sb = new StringBuilder();
            int max = items.Count < 30 ? items.Count : 30;
            for (int i = 0; i < max; i++) sb.Append("  - ").AppendLine(items[i]);
            if (items.Count > max) sb.Append("  ... and ").Append(items.Count - max).AppendLine(" more");
            return sb.ToString();
        }
    }
}
