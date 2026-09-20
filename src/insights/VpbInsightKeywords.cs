using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VPB
{
    internal sealed class VpbKeywordMatcher
    {
        private const int BucketCount = 128;

        private readonly List<string>[] _buckets = new List<string>[BucketCount];
        private readonly List<string> _all = new List<string>();

        internal int Count { get { return _all.Count; } }

        internal void Add(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return;
            string k = keyword.Trim().ToLowerInvariant();
            if (k.Length == 0) return;
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i], k, StringComparison.Ordinal)) return;
            }
            _all.Add(k);
            char c = k[0];
            if (c >= BucketCount) return;
            if (_buckets[c] == null) _buckets[c] = new List<string>(4);
            _buckets[c].Add(k);
        }

        internal string FindFirst(string text, out int hitLineStart, out int hitLineEnd)
        {
            hitLineStart = 0;
            hitLineEnd = 0;
            if (string.IsNullOrEmpty(text) || _all.Count == 0) return null;

            int len = text.Length;
            for (int i = 0; i < len; i++)
            {
                char c = text[i];
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                if (c >= BucketCount) continue;
                List<string> bucket = _buckets[c];
                if (bucket == null) continue;
                for (int b = 0; b < bucket.Count; b++)
                {
                    string kw = bucket[b];
                    if (MatchesAt(text, i, kw))
                    {
                        ResolveLineBounds(text, i, out hitLineStart, out hitLineEnd);
                        return kw;
                    }
                }
            }
            return null;
        }

        private static bool MatchesAt(string text, int start, string keywordLower)
        {
            int kl = keywordLower.Length;
            if (start + kl > text.Length) return false;
            for (int j = 0; j < kl; j++)
            {
                char a = text[start + j];
                if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
                if (a != keywordLower[j]) return false;
            }
            return true;
        }

        private static void ResolveLineBounds(string text, int hitIndex, out int start, out int end)
        {
            start = hitIndex;
            while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r') start--;
            end = hitIndex;
            int len = text.Length;
            while (end < len && text[end] != '\n' && text[end] != '\r') end++;
        }
    }

    internal static class VpbInsightKeywords
    {
        private static readonly object Lock = new object();
        private static VpbKeywordMatcher _high;
        private static VpbKeywordMatcher _low;
        private static bool _loaded;
        private static string _sourceLabel = "";

        internal const int BuiltinRevision = 1;

        private static readonly string[] DefaultHigh =
        {
            "Process.Start",
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "cmd.exe",
            "powershell",
            "rundll32",
            "regsvr32",
            "WebClient",
            "DownloadFile",
            "DownloadString",
            "UploadFile",
            "UploadString",
            "Socket(",
            "TcpClient",
            "UdpClient",
            "HttpWebRequest",
            "File.WriteAllBytes",
            "File.WriteAllText",
            "File.Delete",
            "Directory.Delete",
            "Registry",
            "Environment.SpecialFolder",
            "GetEnvironmentVariable",
            "Assembly.Load",
            "AppDomain.CurrentDomain",
            "DllImport",
            "kernel32",
            "advapi32",
            "FromBase64String",
            "CryptoStream",
            "keylog",
        };

        private static readonly string[] DefaultLow =
        {
            "Application.OpenURL",
            "UnityWebRequest",
            "WWWForm",
            "new WWW(",
            "System.Reflection",
            "GetMethod(",
            "Invoke(",
            "BindingFlags",
            "SystemInfo.deviceUniqueIdentifier",
            "Environment.UserName",
            "Environment.MachineName",
            "SuperController.singleton.Quit",
            "Screen.SetResolution",
        };

        internal static string SourceLabel
        {
            get { EnsureLoaded(); return _sourceLabel; }
        }

        internal static VpbKeywordMatcher High
        {
            get { EnsureLoaded(); return _high; }
        }

        internal static VpbKeywordMatcher Low
        {
            get { EnsureLoaded(); return _low; }
        }

        internal static string UserFilePath
        {
            get
            {
                try { return Path.Combine(GlobalInfo.PluginInfoDirectory ?? "", "insight_keywords.txt"); }
                catch { return ""; }
            }
        }

        internal static void Invalidate()
        {
            lock (Lock)
            {
                _loaded = false;
                _high = null;
                _low = null;
            }
        }

        private static void EnsureLoaded()
        {
            lock (Lock)
            {
                if (_loaded && _high != null && _low != null) return;

                var high = new VpbKeywordMatcher();
                var low = new VpbKeywordMatcher();
                string label = VPBTranslation.T("insights.keywords.builtin", "built-in list");

                string path = UserFilePath;
                bool fromFile = false;
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            fromFile = LoadUserFile(path, high, low);
                            if (fromFile)
                                label = VPBTranslation.T("insights.keywords.user", "custom list");
                        }
                    }
                    catch (Exception ex)
                    {
                        try { LogUtil.LogWarning("[VPB.Insights] keyword file unreadable: " + ex.Message); } catch { }
                        fromFile = false;
                    }
                }

                if (!fromFile)
                {
                    for (int i = 0; i < DefaultHigh.Length; i++) high.Add(DefaultHigh[i]);
                    for (int i = 0; i < DefaultLow.Length; i++) low.Add(DefaultLow[i]);
                }

                _high = high;
                _low = low;
                _sourceLabel = label;
                _loaded = true;
            }
        }

        private static bool LoadUserFile(string path, VpbKeywordMatcher high, VpbKeywordMatcher low)
        {
            string[] lines = File.ReadAllLines(path);
            if (lines == null || lines.Length == 0) return false;

            int section = 0; // 0 = none, 1 = high, 2 = low
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (string.Equals(line, "[high]", StringComparison.OrdinalIgnoreCase)) { section = 1; continue; }
                if (string.Equals(line, "[low]", StringComparison.OrdinalIgnoreCase)) { section = 2; continue; }
                if (section == 1) high.Add(line);
                else if (section == 2) low.Add(line);
            }
            return high.Count > 0 || low.Count > 0;
        }

        internal static bool TryWriteDefaultUserFile(out string writtenPath)
        {
            writtenPath = UserFilePath;
            if (string.IsNullOrEmpty(writtenPath)) return false;
            try
            {
                GlobalInfo.EnsurePluginDataInitialized();
                var sb = new StringBuilder(2048);
                sb.AppendLine("// VPB package insights — script keyword list.");
                sb.AppendLine("// One literal substring per line, case-insensitive. Lines starting with // are ignored.");
                sb.AppendLine("// Matches are a prompt to review a plugin, never proof of anything.");
                sb.AppendLine();
                sb.AppendLine("[high]");
                for (int i = 0; i < DefaultHigh.Length; i++) sb.AppendLine(DefaultHigh[i]);
                sb.AppendLine();
                sb.AppendLine("[low]");
                for (int i = 0; i < DefaultLow.Length; i++) sb.AppendLine(DefaultLow[i]);
                File.WriteAllText(writtenPath, sb.ToString());
                Invalidate();
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Insights] could not write keyword file: " + ex.Message); } catch { }
                return false;
            }
        }
    }
}
