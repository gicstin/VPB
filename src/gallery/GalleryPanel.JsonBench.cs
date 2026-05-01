using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SimpleJSON;
using UnityEngine;
using VPB.Util;

namespace VPB
{
    /// <summary>Developer meta.json benchmark: SimpleJSON parse vs Boyer-Moore-Horspool substring scan.</summary>
    public partial class GalleryPanel : MonoBehaviour
    {
        private static readonly string[] JsonBenchProbeKeys = new[] { "creatorName", "name", "licenseType", "dependencies" };

        private sealed class JsonBenchResult
        {
            public string Uid;
            public long JsonSizeBytes;
            public bool ParsedSimple;
            public double SimpleCompareMs;
            public string SimpleError;
            public bool RanBmh;
            public bool BmhOk;
            public double BmhMs;
            public string BmhError;
        }

        /// <summary>BepInEx / Unity console lines are often truncated; split long messages.</summary>
        private const int JsonBenchMaxLogLineChars = 3500;

        private static void LogJsonBenchChunked(string fullLine)
        {
            if (string.IsNullOrEmpty(fullLine)) return;
            if (fullLine.Length <= JsonBenchMaxLogLineChars)
            {
                LogUtil.Log(fullLine);
                return;
            }
            int bodyChars = JsonBenchMaxLogLineChars - 48;
            if (bodyChars < 256) bodyChars = 256;
            int totalParts = (fullLine.Length + bodyChars - 1) / bodyChars;
            for (int i = 0; i < totalParts; i++)
            {
                int start = i * bodyChars;
                int len = Math.Min(bodyChars, fullLine.Length - start);
                LogUtil.Log("[VPB JSON Bench] cont " + (i + 1) + "/" + totalParts + " " + fullLine.Substring(start, len));
            }
        }

        private static bool TryReadPackageMetaJson(string uid, out string jsonText, out string err)
        {
            jsonText = null;
            err = null;
            if (string.IsNullOrEmpty(uid))
            {
                err = "empty uid";
                return false;
            }
            try
            {
                var pkg = FileManager.GetPackage(uid, ensureInstalled: false);
                if (pkg == null)
                {
                    err = "package missing";
                    return false;
                }
                var meta = FileManager.GetVarFileEntry(pkg.Uid + ":/meta.json");
                if (meta == null)
                {
                    err = "meta.json missing";
                    return false;
                }
                using (var sr = new VarFileEntryStreamReader(meta))
                {
                    jsonText = sr.ReadToEnd();
                }
                if (string.IsNullOrEmpty(jsonText))
                {
                    err = "meta.json empty";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        private static long BenchSimpleJson(string jsonText, int loops, out bool parseOk, out string parseError)
        {
            parseOk = false;
            parseError = null;
            if (string.IsNullOrEmpty(jsonText) || loops <= 0) return 0;
            var sw = Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < loops; i++)
                {
                    JSONNode node = JSON.Parse(jsonText);
                    if (node == null) continue;
                    parseOk = true;
                    for (int k = 0; k < JsonBenchProbeKeys.Length; k++)
                    {
                        var p = node[JsonBenchProbeKeys[k]];
                        if (p == null) continue;
                        if (p is JSONArray) { var _ = p.Count; }
                        else if (p is JSONClass) { var _ = p.Count; }
                        else { var _ = p.Value; }
                    }
                }
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }
            sw.Stop();
            return sw.ElapsedTicks;
        }

        private static byte[][] s_BmhNeedles;

        private static byte[][] GetBmhNeedles()
        {
            if (s_BmhNeedles != null && s_BmhNeedles.Length == JsonBenchProbeKeys.Length)
                return s_BmhNeedles;
            var arr = new byte[JsonBenchProbeKeys.Length][];
            for (int i = 0; i < JsonBenchProbeKeys.Length; i++)
                arr[i] = Encoding.UTF8.GetBytes("\"" + JsonBenchProbeKeys[i] + "\"");
            s_BmhNeedles = arr;
            return s_BmhNeedles;
        }

        private static long BenchBmh(byte[] jsonBytes, int loops, out bool ok, out string err)
        {
            ok = false;
            err = null;
            if (jsonBytes == null || jsonBytes.Length == 0 || loops <= 0)
            {
                err = "empty json bytes";
                return 0;
            }
            byte[][] needles = GetBmhNeedles();
            var sw = Stopwatch.StartNew();
            try
            {
                for (int l = 0; l < loops; l++)
                {
                    bool anyMatched = false;
                    for (int i = 0; i < needles.Length; i++)
                    {
                        if (global::BoyerMooreHorspool.IndexOf(jsonBytes, needles[i]) >= 0)
                            anyMatched = true;
                    }
                    // BMH scan succeeded; missing keys are normal for some packages.
                    ok = true;
                    // Keep JIT from eliding the loop while staying allocation-free.
                    if (!anyMatched && loops < 0) err = "unreachable";
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
            sw.Stop();
            return sw.ElapsedTicks;
        }

        private static string TrimForLog(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            string one = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (one.Length <= maxLen) return one;
            return one.Substring(0, Math.Max(0, maxLen - 3)) + "...";
        }

        private static bool TryCollectSimpleSampleValues(string jsonText, Dictionary<string, string> outValues, out string err)
        {
            err = null;
            if (outValues == null) return false;
            outValues.Clear();
            if (string.IsNullOrEmpty(jsonText))
            {
                err = "empty json";
                return false;
            }
            try
            {
                JSONNode node = JSON.Parse(jsonText);
                if (node == null)
                {
                    err = "parse returned null";
                    return false;
                }
                for (int i = 0; i < JsonBenchProbeKeys.Length; i++)
                {
                    string k = JsonBenchProbeKeys[i];
                    var p = node[k];
                    if (p == null)
                    {
                        outValues[k] = "(missing)";
                        continue;
                    }
                    if (p is JSONArray || p is JSONClass)
                        outValues[k] = TrimForLog(p.ToString(), 120);
                    else
                        outValues[k] = TrimForLog(p.Value, 120);
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        /// <summary>Sample-side path: Boyer-Moore-Horspool finds key bytes; <see cref="Utf8JsonBmhRootKeyProbe"/> parses value at hits (root keys only).</summary>
        private static bool TryCollectBmhSampleValues(string jsonText, Dictionary<string, string> outValues, out string err)
        {
            err = null;
            return TryCollectBmhSampleValues(jsonText, outValues, null, out err);
        }

        private static bool TryCollectBmhSampleValues(string jsonText, Dictionary<string, string> outValues,
            Dictionary<string, string> outSources, out string err)
        {
            err = null;
            if (outValues == null) return false;
            outValues.Clear();
            if (outSources != null) outSources.Clear();
            if (string.IsNullOrEmpty(jsonText))
            {
                err = "empty json";
                return false;
            }
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(jsonText);
                byte[][] needles = GetBmhNeedles();
                for (int i = 0; i < JsonBenchProbeKeys.Length; i++)
                {
                    string key = JsonBenchProbeKeys[i];
                    if (!Utf8JsonBmhRootKeyProbe.TryGetRootPropertyDisplayForBench(bytes, needles[i], 120,
                            out string disp, out string source))
                    {
                        outValues[key] = "(missing)";
                        if (outSources != null) outSources[key] = "miss";
                    }
                    else
                    {
                        outValues[key] = disp;
                        if (outSources != null) outSources[key] = source;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        private void TboxBenchmarkJsonParsersSelected()
        {
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.IsDevMode)
                {
                    ShowTemporaryStatus("JSON benchmark is available only in Developer Mode.");
                    return;
                }

                var libraryUids = CollectUniquePackageUidsFromSelection(currentFilteredFiles);
                if (libraryUids == null || libraryUids.Count == 0)
                {
                    ShowTemporaryStatus("No package rows in current library.");
                    return;
                }

                int maxPackages = libraryUids.Count;
                const int loops = 1;
                int count = 0;
                long totalJsonBytes = 0;
                double totalSimpleMs = 0.0;
                double totalBmhMs = 0.0;
                int simpleSuccess = 0;
                int bmhSuccess = 0;
                var details = new List<JsonBenchResult>();

                foreach (string uid in libraryUids)
                {
                    if (count >= maxPackages) break;
                    count++;

                    var r = new JsonBenchResult { Uid = uid };
                    details.Add(r);

                    if (!TryReadPackageMetaJson(uid, out string json, out string metaErr))
                    {
                        r.SimpleError = metaErr;
                        r.BmhError = metaErr;
                        continue;
                    }

                    try { r.JsonSizeBytes = Encoding.UTF8.GetByteCount(json); } catch { r.JsonSizeBytes = 0; }
                    totalJsonBytes += r.JsonSizeBytes;

                    long simpleTicks = BenchSimpleJson(json, loops, out bool okSimple, out string simpleErr);
                    r.SimpleCompareMs = (double)simpleTicks * 1000.0 / (double)Stopwatch.Frequency;
                    r.ParsedSimple = okSimple;
                    r.SimpleError = simpleErr;
                    totalSimpleMs += r.SimpleCompareMs;
                    if (okSimple) simpleSuccess++;

                    r.RanBmh = true;
                    byte[] bytes = null;
                    try { bytes = Encoding.UTF8.GetBytes(json); } catch { bytes = null; }
                    long bmhTicks = BenchBmh(bytes, loops, out bool okBmh, out string bmhErr);
                    r.BmhMs = (double)bmhTicks * 1000.0 / (double)Stopwatch.Frequency;
                    r.BmhOk = okBmh;
                    r.BmhError = bmhErr;
                    totalBmhMs += r.BmhMs;
                    if (okBmh) bmhSuccess++;
                }

                LogUtil.Log("[VPB JSON Bench] SUMMARY begin");
                LogUtil.Log("[VPB JSON Bench] SUMMARY library_total=" + libraryUids.Count + " tested=" + count
                    + " simple_loops=1 bmh_loops=1");
                LogJsonBenchChunked("[VPB JSON Bench] SUMMARY keys=" + string.Join(",", JsonBenchProbeKeys));
                LogUtil.Log("[VPB JSON Bench] SUMMARY simple_ok=" + simpleSuccess + "/" + count
                    + " simple_ms=" + totalSimpleMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                LogUtil.Log("[VPB JSON Bench] SUMMARY bmh_ok=" + bmhSuccess + "/" + count
                    + " bmh_ms=" + totalBmhMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

                int simpleErrCount = 0;
                for (int i = 0; i < details.Count; i++)
                    if (!string.IsNullOrEmpty(details[i].SimpleError)) simpleErrCount++;
                if (simpleErrCount > 0)
                {
                    LogUtil.Log("[VPB JSON Bench] SUMMARY simple_errors=" + simpleErrCount + "/" + count);
                    int shown = 0;
                    for (int i = 0; i < details.Count && shown < 3; i++)
                    {
                        var d = details[i];
                        if (string.IsNullOrEmpty(d.SimpleError)) continue;
                        LogJsonBenchChunked("[VPB JSON Bench] SUMMARY simple_error uid=" + d.Uid + " err=" + d.SimpleError);
                        shown++;
                    }
                }

                double totalMiB = totalJsonBytes / (1024.0 * 1024.0);
                double simpleMBps = totalSimpleMs > 0.0 ? (totalMiB / (totalSimpleMs / 1000.0)) : 0.0;
                double bmhMBps = totalBmhMs > 0.0 ? (totalMiB / (totalBmhMs / 1000.0)) : 0.0;
                LogUtil.Log("[VPB JSON Bench] SUMMARY throughput_mb_s simple="
                    + simpleMBps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    + " bmh=" + bmhMBps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    + " total_mib=" + totalMiB.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

                const int sampleLimit = 50;
                int sampled = 0;
                LogUtil.Log("[VPB JSON Bench] SUMMARY samples_begin limit=" + sampleLimit);
                for (int i = 0; i < details.Count && sampled < sampleLimit; i++)
                {
                    var d = details[i];
                    if (d == null || !d.ParsedSimple) continue;
                    if (!TryReadPackageMetaJson(d.Uid, out string sampleJson, out _)) continue;

                    sampled++;
                    var simpleVals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var bmhVals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var bmhSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    bool simpleSampleOk = TryCollectSimpleSampleValues(sampleJson, simpleVals, out string simpleSampleErr);
                    bool bmhSampleOk = TryCollectBmhSampleValues(sampleJson, bmhVals, bmhSources, out string bmhSampleErr);
                    LogUtil.Log("[VPB JSON Bench] SUMMARY sample_begin #" + sampled + " uid=" + d.Uid);
                    for (int k = 0; k < JsonBenchProbeKeys.Length; k++)
                    {
                        string key = JsonBenchProbeKeys[k];
                        string sv = simpleSampleOk && simpleVals.ContainsKey(key) ? simpleVals[key] : "(simple n/a)";
                        string bv = bmhSampleOk && bmhVals.ContainsKey(key) ? bmhVals[key] : "(bmh n/a)";
                        string src = bmhSampleOk && bmhSources.ContainsKey(key) ? bmhSources[key] : "n/a";
                        LogJsonBenchChunked("[VPB JSON Bench] SUMMARY sample key=" + key + " simple=" + sv + " bmh=" + bv + " bmh_source=" + src);
                    }
                    if (!simpleSampleOk && !string.IsNullOrEmpty(simpleSampleErr))
                        LogJsonBenchChunked("[VPB JSON Bench] SUMMARY sample_simple_error=" + simpleSampleErr);
                    if (!bmhSampleOk && !string.IsNullOrEmpty(bmhSampleErr))
                        LogJsonBenchChunked("[VPB JSON Bench] SUMMARY sample_bmh_error=" + bmhSampleErr);
                    LogUtil.Log("[VPB JSON Bench] SUMMARY sample_end #" + sampled + " uid=" + d.Uid);
                }
                LogUtil.Log("[VPB JSON Bench] SUMMARY samples_end count=" + sampled);

                var topSimple = details
                    .Where(d => d != null && d.ParsedSimple)
                    .OrderByDescending(d => d.SimpleCompareMs)
                    .ThenByDescending(d => d.JsonSizeBytes)
                    .Take(10)
                    .ToList();
                LogUtil.Log("[VPB JSON Bench] SUMMARY top_simple_begin count=" + topSimple.Count);
                for (int i = 0; i < topSimple.Count; i++)
                {
                    var d = topSimple[i];
                    double kb = d.JsonSizeBytes / 1024.0;
                    LogJsonBenchChunked(
                        "[VPB JSON Bench] SUMMARY top_simple #" + (i + 1)
                        + " uid=" + d.Uid
                        + " simple_ms=" + d.SimpleCompareMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        + " bmh_ms=" + d.BmhMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        + " bmh_ok=" + d.BmhOk
                        + (string.IsNullOrEmpty(d.BmhError) ? "" : " bmh_note=" + TrimForLog(d.BmhError, 64))
                        + " json_bytes=" + d.JsonSizeBytes
                        + " json_kb=" + kb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    );
                }
                LogUtil.Log("[VPB JSON Bench] SUMMARY top_simple_end");
                LogUtil.Log("[VPB JSON Bench] SUMMARY end");

                ShowTemporaryStatus("JSON bench done: Simple "
                    + totalSimpleMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    + "ms, BMH " + totalBmhMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "ms.");
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB JSON Bench] failed: " + ex.Message);
                ShowTemporaryStatus("JSON bench failed. See log.");
            }
        }
    }
}
