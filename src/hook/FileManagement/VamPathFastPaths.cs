using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace VPB
{
    internal static class VamPathFastPaths
    {
        const string ThroughLastSlashPattern = ".*/";

        static int s_RedirectedRegexCalls;

        internal static int RedirectedRegexCalls { get { return s_RedirectedRegexCalls; } }

        internal static string StripThroughLastSlash(string input)
        {
            if (input == null || input.IndexOf('\n') >= 0)
                return Regex.Replace(input, ThroughLastSlashPattern, string.Empty);
            int slash = input.LastIndexOf('/');
            return slash < 0 ? input : input.Substring(slash + 1);
        }

        public static string RegexReplace(string input, string pattern, string replacement)
        {
            if (replacement != null && replacement.Length == 0
                && string.Equals(pattern, ThroughLastSlashPattern, StringComparison.Ordinal))
                return StripThroughLastSlash(input);
            return Regex.Replace(input, pattern, replacement);
        }

        internal static IEnumerable<ConstructorInfo> VamEntryConstructors()
        {
            yield return AccessTools.Constructor(typeof(MVR.FileManagement.VarFileEntry),
                new[] { typeof(MVR.FileManagement.VarPackage), typeof(string), typeof(DateTime), typeof(long), typeof(bool) });
            yield return AccessTools.Constructor(typeof(MVR.FileManagement.VarDirectoryEntry),
                new[] { typeof(MVR.FileManagement.VarPackage), typeof(string), typeof(MVR.FileManagement.VarDirectoryEntry) });
            yield return AccessTools.Constructor(typeof(MVR.FileManagement.FileEntry), new[] { typeof(string) });
            yield return AccessTools.Constructor(typeof(MVR.FileManagement.DirectoryEntry), new[] { typeof(string) });
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null) return;

            var transpiler = new HarmonyMethod(typeof(VamPathFastPaths), nameof(RedirectRegexReplace));
            int patched = 0;
            int expected = 0;
            foreach (ConstructorInfo ctor in VamEntryConstructors())
            {
                expected++;
                try
                {
                    if (ctor == null) continue;
                    harmony.Patch(ctor, transpiler: transpiler);
                    patched++;
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning(VamStartupOptimizations.LogTag + " entry name fast path skipped for "
                        + (ctor != null ? ctor.DeclaringType.Name : "?") + ": " + ex.Message);
                }
            }
            if (patched < expected)
                LogUtil.LogWarning(VamStartupOptimizations.LogTag + " entry name fast path covers " + patched + " of " + expected
                    + " VaM file/directory entry constructors; the rest keep VaM's regex");

            try
            {
                MethodInfo normalizeCommon = AccessTools.Method(typeof(MVR.FileManagement.FileManager), "NormalizeCommon", new[] { typeof(string) });
                if (normalizeCommon == null) throw new MissingMethodException("MVR.FileManagement.FileManager", "NormalizeCommon");
                harmony.Patch(normalizeCommon,
                    prefix: new HarmonyMethod(typeof(VamPathFastPaths), nameof(SkipNormalizeCommonWithoutPackageReference)));
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning(VamStartupOptimizations.LogTag + " NormalizeCommon fast path skipped: " + ex.Message);
            }
        }

        internal static IEnumerable<CodeInstruction> RedirectRegexReplace(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo fast = AccessTools.Method(typeof(VamPathFastPaths), nameof(RegexReplace));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && IsStaticRegexReplace(instruction.operand as MethodInfo))
                {
                    instruction.operand = fast;
                    s_RedirectedRegexCalls++;
                }
                yield return instruction;
            }
        }

        static bool IsStaticRegexReplace(MethodInfo method)
        {
            if (method == null || !method.IsStatic || method.DeclaringType != typeof(Regex) || method.Name != "Replace") return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 3
                && parameters[0].ParameterType == typeof(string)
                && parameters[1].ParameterType == typeof(string)
                && parameters[2].ParameterType == typeof(string);
        }

        internal static bool SkipNormalizeCommonWithoutPackageReference(string __0, ref string __result)
        {
            if (__0 == null || __0.IndexOf(':') >= 0) return true;
            __result = __0;
            return false;
        }
    }
}
