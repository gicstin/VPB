using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using VPB.src.util;

namespace VPB.Tests
{
    public static class HeadlessVam
    {
        public const string EcallMarker = "ECall methods must be packaged into a system module";

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static readonly List<string> Captured = new List<string>();
        private static readonly List<string> Unresolved = new List<string>();

        public static IEnumerable<string> LogLines
        {
            get { lock (Gate) return Captured.ToArray(); }
        }

        public static IEnumerable<string> UnresolvedSeams
        {
            get { lock (Gate) return Unresolved.ToArray(); }
        }

        public static void Install()
        {
            lock (Gate)
            {
                if (_harmony != null) return;
                _harmony = new Harmony("vpb.tests.headless");

                PatchPrefix(typeof(VPBLogSource), "Log", new[] { typeof(LogLevel), typeof(object), typeof(bool) }, nameof(CaptureLogPrefix));

                VpbSqlite3.SetGameInstallRootForNativeDll(TestEnvironment.VaMPath);
            }
        }

        private static BepInEx.Configuration.ConfigFile _settingsFile;

        public static BepInEx.Configuration.ConfigEntry<T> OverrideSetting<T>(string fieldName, T value)
        {
            lock (Gate)
            {
                if (_settingsFile == null)
                {
                    string path = Path.Combine(
                        Path.GetTempPath(), "vpbtest_settings_" + Guid.NewGuid().ToString("N") + ".cfg");
                    _settingsFile = new BepInEx.Configuration.ConfigFile(path, true);
                }

                FieldInfo field = typeof(Settings).GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                    throw new InvalidOperationException(
                        "Settings." + fieldName + " does not exist. OverrideSetting assigns a bound ConfigEntry " +
                        "onto Settings.Instance so a test can pin plugin behaviour that reads it.");

                var entry = _settingsFile.Bind("Tests", fieldName + "_" + Guid.NewGuid().ToString("N"), value, "test override");
                field.SetValue(Settings.Instance, entry);
                return entry;
            }
        }

        private static bool _vamAssembliesLoaded;

        public static void EnsureVamAssembliesLoaded()
        {
            lock (Gate)
            {
                if (_vamAssembliesLoaded) return;
                _vamAssembliesLoaded = true;

                string dir;
                try { dir = Path.GetDirectoryName(new Uri(typeof(HeadlessVam).Assembly.CodeBase).LocalPath); }
                catch { return; }
                if (string.IsNullOrEmpty(dir)) return;

                foreach (string path in Directory.GetFiles(dir, "*.dll"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith("NuGet.", StringComparison.OrdinalIgnoreCase)) continue;

                    try { Assembly.LoadFrom(path); }
                    catch { }
                }
            }
        }

        public static void ClearLog()
        {
            lock (Gate) Captured.Clear();
        }

        public static string LogDump()
        {
            lock (Gate)
            {
                if (Captured.Count == 0) return "(no VPB log output captured)";
                return string.Join(Environment.NewLine, Captured.ToArray());
            }
        }

        public static bool SawEcall()
        {
            lock (Gate)
            {
                for (int i = 0; i < Captured.Count; i++)
                    if (Captured[i].IndexOf(EcallMarker, StringComparison.Ordinal) >= 0) return true;
                return false;
            }
        }

        public static void NeutralizeStaticConstructor(Type type)
        {
            ConstructorInfo cctor = null;
            foreach (ConstructorInfo c in type.GetConstructors(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (c.IsStatic) { cctor = c; break; }
            }

            if (cctor == null)
            {
                Unresolved.Add(type.FullName + "..cctor");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HeadlessVam), nameof(SkipVoidPrefix));
            _harmony.Patch(cctor, prefix: new HarmonyMethod(prefix));
        }

        public static void PatchPrefix(Type declaringType, string methodName, Type[] argumentTypes, string prefixName)
        {
            MethodBase target = argumentTypes != null
                ? AccessTools.Method(declaringType, methodName, argumentTypes)
                : AccessTools.Method(declaringType, methodName);

            if (target == null)
            {
                Unresolved.Add(Describe(declaringType, methodName, argumentTypes));
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HeadlessVam), prefixName);
            if (prefix == null) throw new InvalidOperationException("HeadlessVam prefix not found: " + prefixName);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        private static string Describe(Type t, string name, Type[] args)
        {
            string a = "(any)";
            if (args != null)
            {
                var names = new string[args.Length];
                for (int i = 0; i < args.Length; i++) names[i] = args[i].Name;
                a = "(" + string.Join(", ", names) + ")";
            }
            return (t == null ? "<null>" : t.FullName) + "." + name + a;
        }

        public static bool CaptureLogPrefix(LogLevel level, object data)
        {
            lock (Gate) Captured.Add(level + " | " + data);
            return false;
        }

        public static bool ReturnFalsePrefix(ref bool __result)
        {
            __result = false;
            return false;
        }

        public static bool SkipVoidPrefix()
        {
            return false;
        }

        public static string DescribeUnwrapped(Exception ex)
        {
            Exception e = ex;
            while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            while (e is TypeInitializationException && e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }

        public static bool IsEcall(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e.Message != null && e.Message.IndexOf(EcallMarker, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
