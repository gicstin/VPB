using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace VPB.Tests.Runtime
{
    public sealed class RuntimeTestCase
    {
        public string Suite;
        public string Name;
        public MethodInfo Method;
        public float TimeoutSeconds;
        public bool Skip;
        public string Reason;

        public string FullName { get { return Suite + "." + Name; } }
    }

    public sealed class RuntimeTestResult
    {
        public RuntimeTestCase Case;
        public bool Passed;
        public bool Skipped;
        public string Failure;
        public long ElapsedMs;
    }

    public static class RuntimeTestRunner
    {
        public const float DefaultTimeoutSeconds = 30f;

        private static readonly List<RuntimeTestResult> Results = new List<RuntimeTestResult>(64);

        public static IEnumerable<RuntimeTestResult> LastResults { get { return Results; } }

        public static List<RuntimeTestCase> Discover(Assembly assembly)
        {
            var cases = new List<RuntimeTestCase>(64);
            if (assembly == null) return cases;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types ?? new Type[0]; }

            var ordered = new List<KeyValuePair<int, Type>>(types.Length);
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null) continue;
                object[] suiteAttrs = type.GetCustomAttributes(typeof(VpbRuntimeSuiteAttribute), false);
                if (suiteAttrs.Length == 0) continue;
                ordered.Add(new KeyValuePair<int, Type>(((VpbRuntimeSuiteAttribute)suiteAttrs[0]).Order, type));
            }
            ordered.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key)
                                                 : string.CompareOrdinal(a.Value.Name, b.Value.Name));

            for (int i = 0; i < ordered.Count; i++)
            {
                Type type = ordered[i].Value;
                var suiteAttr = (VpbRuntimeSuiteAttribute)type.GetCustomAttributes(typeof(VpbRuntimeSuiteAttribute), false)[0];
                string suiteName = !string.IsNullOrEmpty(suiteAttr.Name) ? suiteAttr.Name : type.Name;

                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                Array.Sort(methods, (a, b) => string.CompareOrdinal(a.Name, b.Name));

                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo method = methods[m];
                    object[] attrs = method.GetCustomAttributes(typeof(VpbRuntimeTestAttribute), false);
                    if (attrs.Length == 0) continue;
                    if (method.GetParameters().Length != 0) continue;

                    var attr = (VpbRuntimeTestAttribute)attrs[0];
                    cases.Add(new RuntimeTestCase
                    {
                        Suite = suiteName,
                        Name = method.Name,
                        Method = method,
                        TimeoutSeconds = attr.TimeoutSeconds > 0f ? attr.TimeoutSeconds : DefaultTimeoutSeconds,
                        Skip = attr.Skip,
                        Reason = attr.Reason,
                    });
                }
            }

            return cases;
        }

        static bool IsSkip(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e is RuntimeSkipException) return true;
            return false;
        }

        static string SkipReason(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e is RuntimeSkipException) return e.Message;
            return "skipped";
        }

        public static IEnumerator RunAll(MonoBehaviour host, Assembly assembly, Action<string> log)
        {
            Results.Clear();
            List<RuntimeTestCase> cases = Discover(assembly);

            if (log != null) log("[VPB.RuntimeTests] discovered " + cases.Count + " test(s)");

            for (int i = 0; i < cases.Count; i++)
            {
                RuntimeTestCase testCase = cases[i];
                var result = new RuntimeTestResult { Case = testCase };

                if (testCase.Skip)
                {
                    result.Skipped = true;
                    result.Failure = testCase.Reason;
                    Results.Add(result);
                    continue;
                }

                Stopwatch sw = Stopwatch.StartNew();
                IEnumerator body = null;
                bool started = false;

                try
                {
                    object returned = testCase.Method.Invoke(null, null);
                    body = returned as IEnumerator;
                    started = true;
                }
                catch (Exception ex)
                {
                    if (IsSkip(ex))
                    {
                        result.Skipped = true;
                        result.Failure = SkipReason(ex);
                        result.ElapsedMs = 0;
                        Results.Add(result);
                        if (log != null) log("[VPB.RuntimeTests] SKIP " + testCase.FullName + " :: " + result.Failure);
                        continue;
                    }
                    result.Failure = RuntimeAssert.Unwrap(ex);
                }

                if (started && body != null)
                {
                    float deadline = Time.realtimeSinceStartup + testCase.TimeoutSeconds;
                    bool timedOut = false;

                    while (true)
                    {
                        bool moved;
                        try { moved = body.MoveNext(); }
                        catch (Exception ex)
                        {
                            if (IsSkip(ex))
                            {
                                result.Skipped = true;
                                result.Failure = SkipReason(ex);
                            }
                            else result.Failure = RuntimeAssert.Unwrap(ex);
                            break;
                        }

                        if (!moved) break;

                        if (Time.realtimeSinceStartup > deadline)
                        {
                            timedOut = true;
                            break;
                        }
                        yield return body.Current;
                    }

                    if (timedOut)
                        result.Failure = "timed out after " + testCase.TimeoutSeconds + "s";
                }

                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;
                result.Passed = started && result.Failure == null && !result.Skipped;
                Results.Add(result);

                if (log != null && result.Skipped)
                    log("[VPB.RuntimeTests] SKIP " + testCase.FullName + " :: " + result.Failure);
                else if (log != null && !result.Passed)
                    log("[VPB.RuntimeTests] FAIL " + testCase.FullName + " :: " + result.Failure);

                yield return null;
            }

            if (log != null) log(Summary());
        }

        public static string Summary()
        {
            int passed = 0, failed = 0, skipped = 0;
            long totalMs = 0;
            for (int i = 0; i < Results.Count; i++)
            {
                RuntimeTestResult r = Results[i];
                totalMs += r.ElapsedMs;
                if (r.Skipped) skipped++;
                else if (r.Passed) passed++;
                else failed++;
            }

            string verdict;
            if (failed > 0) verdict = "FAIL ";
            else if (passed == 0) verdict = "NOTHING VERIFIED ";
            else verdict = "PASS ";

            return "[VPB.RuntimeTests] " + verdict
                   + passed + "/" + (passed + failed) + " passed"
                   + (skipped > 0 ? ", " + skipped + " skipped" : "")
                   + " in " + totalMs + "ms";
        }

        public static string BuildJUnitXml()
        {
            int failures = 0, skipped = 0;
            long totalMs = 0;
            for (int i = 0; i < Results.Count; i++)
            {
                RuntimeTestResult r = Results[i];
                totalMs += r.ElapsedMs;
                if (r.Skipped) skipped++;
                else if (!r.Passed) failures++;
            }

            var sb = new StringBuilder(4096);
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<testsuite name=\"VPB.Tests.Runtime\" tests=\"").Append(Results.Count)
              .Append("\" failures=\"").Append(failures)
              .Append("\" skipped=\"").Append(skipped)
              .Append("\" time=\"").Append((totalMs / 1000.0).ToString("0.000"))
              .AppendLine("\">");

            for (int i = 0; i < Results.Count; i++)
            {
                RuntimeTestResult r = Results[i];
                sb.Append("  <testcase classname=\"").Append(Escape(r.Case.Suite))
                  .Append("\" name=\"").Append(Escape(r.Case.Name))
                  .Append("\" time=\"").Append((r.ElapsedMs / 1000.0).ToString("0.000")).Append("\"");

                if (r.Skipped)
                {
                    sb.AppendLine(">");
                    sb.Append("    <skipped message=\"").Append(Escape(r.Failure ?? "")).AppendLine("\" />");
                    sb.AppendLine("  </testcase>");
                }
                else if (!r.Passed)
                {
                    sb.AppendLine(">");
                    sb.Append("    <failure message=\"").Append(Escape(r.Failure ?? "")).AppendLine("\" />");
                    sb.AppendLine("  </testcase>");
                }
                else
                {
                    sb.AppendLine(" />");
                }
            }

            sb.AppendLine("</testsuite>");
            return sb.ToString();
        }

        public static string WriteReport(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory)) return null;
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "vpb-runtime-tests.xml");
                File.WriteAllText(path, BuildJUnitXml());
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
