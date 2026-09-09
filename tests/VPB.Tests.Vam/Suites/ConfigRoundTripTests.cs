using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ConfigRoundTripTests
    {
        private readonly ITestOutputHelper _out;
        public ConfigRoundTripTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static FieldInfo[] PublicFields()
        {
            return typeof(VPBConfig).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly)
                .ToArray();
        }

        private static HashSet<string> NormalizedFields()
        {
            string path = Path.Combine(Path.Combine(TestEnvironment.RepoRoot, "tests"), "known-normalized-config-fields.txt");
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return set;
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) set.Add(t);
            }
            return set;
        }

        private static bool TryMutate(FieldInfo f, object target, out object applied)
        {
            applied = null;
            Type t = f.FieldType;

            if (t == typeof(bool))
            {
                applied = !(bool)f.GetValue(target);
            }
            else if (t == typeof(int))
            {
                applied = (int)f.GetValue(target) + 1;
            }
            else if (t == typeof(long))
            {
                applied = (long)f.GetValue(target) + 1L;
            }
            else if (t == typeof(float))
            {
                float v = (float)f.GetValue(target);
                if (float.IsNaN(v) || float.IsInfinity(v)) return false;
                float half = v * 0.5f;
                if (Math.Abs(half - v) < 0.0005f) return false;
                applied = half;
            }
            else if (t.IsEnum)
            {
                Array values = Enum.GetValues(t);
                if (values.Length < 2) return false;
                object current = f.GetValue(target);
                object next = null;
                for (int i = 0; i < values.Length; i++)
                    if (!Equals(values.GetValue(i), current)) { next = values.GetValue(i); break; }
                if (next == null) return false;
                applied = next;
            }
            else
            {
                return false;
            }

            f.SetValue(target, applied);
            return true;
        }

        [Fact]
        public void ConfigFileIsWrittenAtAll()
        {
            using (var install = new TempInstall("cfg"))
            {
                HeadlessVam.ClearLog();
                VPBConfig cfg = VPBConfig.Instance;
                cfg.Save();

                string path = cfg.ConfigPathForDebug;
                _out.WriteLine("config path: " + path);

                Assert.True(File.Exists(path),
                    "VPBConfig.Save() did not produce " + path + "." + Environment.NewLine +
                    "Save() wraps its whole body in a try/catch, so a failure here is silent in the game too." + Environment.NewLine +
                    (HeadlessVam.SawEcall()
                        ? "The captured log shows a Unity ECall was reached - add a HeadlessVam.PatchPrefix for the" + Environment.NewLine +
                          "member named in the message below." + Environment.NewLine
                        : "") +
                    "Captured VPB log:" + Environment.NewLine + HeadlessVam.LogDump());
            }
        }

        [Fact]
        public void EveryScalarSettingSurvivesSaveAndReload()
        {
            using (var install = new TempInstall("roundtrip"))
            {
                HeadlessVam.ClearLog();

                FieldInfo[] fields = PublicFields();
                HashSet<string> normalized = NormalizedFields();
                string cfgPath = VPBConfig.Instance.ConfigPathForDebug;

                var lost = new List<string>();
                int exercised = 0;
                int skipped = 0;

                foreach (FieldInfo f in fields)
                {
                    if (normalized.Contains(f.Name)) { skipped++; continue; }

                    try { if (File.Exists(cfgPath)) File.Delete(cfgPath); } catch { }
                    VPBConfig.ReloadFromDisk();

                    VPBConfig fresh = VPBConfig.Instance;
                    object original = f.GetValue(fresh);

                    object applied;
                    if (!TryMutate(f, fresh, out applied)) { skipped++; continue; }
                    exercised++;

                    fresh.Save();
                    VPBConfig.ReloadFromDisk();
                    object reloaded = f.GetValue(VPBConfig.Instance);

                    if (ValuesMatch(f.FieldType, applied, reloaded)) continue;
                    if (!ValuesMatch(f.FieldType, original, reloaded)) continue;

                    lost.Add(f.Name + " : " + f.FieldType.Name +
                             "   default=" + Show(original) + "  set=" + Show(applied) + "  reloaded=" + Show(reloaded));
                }

                _out.WriteLine("public instance fields: " + fields.Length);
                _out.WriteLine("fields exercised one at a time: " + exercised);
                _out.WriteLine("skipped (non-scalar or allowlisted): " + skipped);
                _out.WriteLine("changes lost entirely: " + lost.Count);

                Assert.True(lost.Count == 0,
                    "These settings were changed, saved, and came back at their original value." + Environment.NewLine +
                    "In the game that is a setting which silently reverts on the next launch:" + Environment.NewLine +
                    HarmonyPatchTargetTests.Bullets(lost) + Environment.NewLine + Environment.NewLine +
                    "If the value is deliberately re-derived or clamped back on load, add the field name to" + Environment.NewLine +
                    "tests/known-normalized-config-fields.txt with a reason.");
            }
        }

        [Fact]
        public void ReloadWithoutAFileFallsBackToDefaults()
        {
            using (var install = new TempInstall("defaults"))
            {
                VPBConfig cfg = VPBConfig.Instance;
                Assert.NotNull(cfg);
                Assert.False(File.Exists(cfg.ConfigPathForDebug),
                    "Reading VPBConfig.Instance must not create VPB.cfg as a side effect.");
            }
        }

        [Fact]
        public void CorruptConfigDoesNotThrow()
        {
            using (var install = new TempInstall("corrupt"))
            {
                string path = Path.Combine(install.PluginDataDir, "VPB.cfg");
                File.WriteAllText(path, "{ this is not valid json ");
                VPBConfig.ReloadFromDisk();

                VPBConfig cfg = VPBConfig.Instance;
                Assert.NotNull(cfg);
                _out.WriteLine("recovered from corrupt config; log:");
                _out.WriteLine(HeadlessVam.LogDump());
            }
        }

        private static bool ValuesMatch(Type t, object a, object b)
        {
            if (t == typeof(float))
                return Math.Abs((float)a - (float)b) < 0.0005f;
            return Equals(a, b);
        }

        private static string Show(object v)
        {
            return v == null ? "<null>" : v.ToString();
        }
    }
}
