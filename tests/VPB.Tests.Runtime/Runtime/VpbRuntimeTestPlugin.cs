using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace VPB.Tests.Runtime
{
    [BepInPlugin(PluginGuid, "VPB Runtime Tests", "1.0.0")]
    [BepInDependency("VPB", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class VpbRuntimeTestPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "VPB.Tests.Runtime";
        private const string MarkerFileName = "run-tests.marker";
        private const string CommandLineFlag = "--vpb-runtime-tests";

        private const float GraceSeconds = 8f;
        private const float ReadinessTimeoutSeconds = 240f;

        private static bool s_ran;

        private void Awake()
        {
            if (s_ran) return;

            string marker = FindMarkerFile();
            bool armedByFlag = HasCommandLineFlag();

            if (marker == null && !armedByFlag)
            {
                Logger.LogInfo("[VPB.RuntimeTests] idle (no " + MarkerFileName + " and no " + CommandLineFlag + ")");
                return;
            }

            s_ran = true;
            StartCoroutine(RunWhenSettled(marker));
        }

        private IEnumerator RunWhenSettled(string markerToConsume)
        {
            Logger.LogMessage("[VPB.RuntimeTests] armed; waiting for VaM to come up");

            float deadline = Time.realtimeSinceStartup + ReadinessTimeoutSeconds;

            while (SuperController.singleton == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (SuperController.singleton == null)
                Logger.LogWarning("[VPB.RuntimeTests] SuperController never appeared within " +
                                  ReadinessTimeoutSeconds + "s; running anyway so the failures are real");
            else
                Logger.LogMessage("[VPB.RuntimeTests] SuperController up after " +
                                  Time.realtimeSinceStartup.ToString("0.0") + "s");

            while (IsVamLoading() && Time.realtimeSinceStartup < deadline)
                yield return null;

            float grace = Time.realtimeSinceStartup + GraceSeconds;
            while (Time.realtimeSinceStartup < grace)
                yield return null;

            Logger.LogMessage("[VPB.RuntimeTests] starting at " +
                              Time.realtimeSinceStartup.ToString("0.0") + "s");

            string reportDir = ResolveReportDirectory();
            WriteCrashReport(reportDir, "the run started and never finished - see the Unity player log");

            IEnumerator run = RuntimeTestRunner.RunAll(this, Assembly.GetExecutingAssembly(), Log);
            while (true)
            {
                bool moved;
                try { moved = run.MoveNext(); }
                catch (Exception ex)
                {
                    Logger.LogError("[VPB.RuntimeTests] the runner itself threw: " + ex);
                    WriteCrashReport(reportDir, "the runner threw: " + ex.GetType().Name + ": " + ex.Message);
                    ClearMarker(markerToConsume);
                    yield break;
                }

                if (!moved) break;
                yield return run.Current;
            }

            string written = RuntimeTestRunner.WriteReport(reportDir);
            if (!string.IsNullOrEmpty(written))
                Logger.LogMessage("[VPB.RuntimeTests] report: " + written);
            else
                Logger.LogError("[VPB.RuntimeTests] could not write the report to " + reportDir);

            ClearMarker(markerToConsume);
        }

        private static bool IsVamLoading()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                return sc != null && sc.isLoading;
            }
            catch
            {
                return false;
            }
        }

        private void ClearMarker(string marker)
        {
            if (string.IsNullOrEmpty(marker)) return;
            try { File.Delete(marker); }
            catch (Exception ex) { Logger.LogWarning("[VPB.RuntimeTests] could not clear marker: " + ex.Message); }
        }

        private void WriteCrashReport(string directory, string reason)
        {
            try
            {
                if (string.IsNullOrEmpty(directory)) return;
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<testsuite name=\"VPB.Tests.Runtime\" tests=\"1\" failures=\"1\" skipped=\"0\" time=\"0.000\">\r\n" +
                    "  <testcase classname=\"VpbRuntimeTestPlugin\" name=\"TheInGameRunCompletes\" time=\"0.000\">\r\n" +
                    "    <failure message=\"" + reason.Replace("&", "&amp;").Replace("<", "&lt;")
                                                     .Replace(">", "&gt;").Replace("\"", "&quot;")
                                                     .Replace("\r", " ").Replace("\n", " ") + "\" />\r\n" +
                    "  </testcase>\r\n" +
                    "</testsuite>\r\n";

                File.WriteAllText(Path.Combine(directory, "vpb-runtime-tests.xml"), xml);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[VPB.RuntimeTests] could not write the crash report: " + ex.Message);
            }
        }

        private void Log(string line)
        {
            if (line != null && line.IndexOf("FAIL", StringComparison.Ordinal) >= 0) Logger.LogError(line);
            else Logger.LogMessage(line);
        }

        private static string FindMarkerFile()
        {
            try
            {
                string dir = Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
                if (string.IsNullOrEmpty(dir)) return null;
                string path = Path.Combine(dir, MarkerFileName);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasCommandLineFlag()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], CommandLineFlag, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string ResolveReportDirectory()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                return Path.Combine(Path.Combine(Path.Combine(baseDir, "Saves"), "PluginData"), "VPB");
            }
            catch
            {
                return null;
            }
        }
    }
}
