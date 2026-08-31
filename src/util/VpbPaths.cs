using System;
using System.IO;
using VPB.Shared;

namespace VPB
{
    internal static class VpbPaths
    {
        private static bool s_LegacyRootSwept;

        internal const string AssetsDirName = "assets";
        internal const string NativeDirName = "native";
        internal const string NetDirName = "net";
        internal const string ClipsDirName = "clips";

        private static readonly string s_Root;
        private static readonly string s_LegacyRoot;

        static VpbPaths()
        {
            string root = null;
            try
            {
                var asm = typeof(VpbPaths).Assembly;
                string loc = asm != null ? asm.Location : null;
                if (string.IsNullOrEmpty(loc))
                {
                    string cb = asm != null ? asm.CodeBase : null;
                    if (!string.IsNullOrEmpty(cb)) loc = new Uri(cb).LocalPath;
                }
                if (!string.IsNullOrEmpty(loc)) root = Path.GetDirectoryName(loc);
            }
            catch { }

            string legacy = null;
            try { legacy = BepInEx.Paths.PluginPath; } catch { }

            if (string.IsNullOrEmpty(root)) root = legacy;
            if (string.IsNullOrEmpty(root)) root = ".";

            s_Root = root;
            s_LegacyRoot = string.Equals(legacy, root, StringComparison.OrdinalIgnoreCase) ? null : legacy;
        }

        internal static string Root { get { return s_Root; } }

        internal static string LegacyRoot { get { return s_LegacyRoot; } }

        internal static string Assets { get { return Path.Combine(s_Root, AssetsDirName); } }

        internal static string Native { get { return Path.Combine(s_Root, NativeDirName); } }

        internal static string Net { get { return Path.Combine(s_Root, NetDirName); } }

        internal static string Clips { get { return Path.Combine(s_Root, ClipsDirName); } }

        internal static void SweepLegacyRoot()
        {
            if (s_LegacyRootSwept) return;
            s_LegacyRootSwept = true;

            if (string.IsNullOrEmpty(s_LegacyRoot)) return;

            try
            {
                int removed = VpbLegacyLayout.SweepPluginsRoot(s_LegacyRoot, LogSweepInfo, LogSweepWarning);
                if (removed > 0)
                {
                    LogUtil.Log("[VPB] Retired " + removed + " item(s) from the old plugins-root layout; the shipped tree is "
                        + s_Root + ". Restart VaM if anything looks stale.");
                }
            }
            catch (Exception ex)
            {
                LogUtil.Log("[VPB] Legacy root sweep failed: " + ex.Message);
            }
        }

        private static void LogSweepInfo(string message)
        {
            LogUtil.Log("[VPB] " + message);
        }

        private static void LogSweepWarning(string message)
        {
            LogUtil.Log("[VPB] " + message);
        }

        internal static string Combine(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return s_Root;
            return Path.Combine(s_Root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string FindFile(params string[] relativeCandidates)
        {
            return Find(relativeCandidates, false);
        }

        internal static string FindDir(params string[] relativeCandidates)
        {
            return Find(relativeCandidates, true);
        }

        private static string Find(string[] relativeCandidates, bool directory)
        {
            if (relativeCandidates == null || relativeCandidates.Length == 0) return s_Root;

            for (int i = 0; i < relativeCandidates.Length; i++)
            {
                string hit = Probe(s_Root, relativeCandidates[i], directory);
                if (hit != null) return hit;
            }

            if (!string.IsNullOrEmpty(s_LegacyRoot))
            {
                for (int i = 0; i < relativeCandidates.Length; i++)
                {
                    string hit = Probe(s_LegacyRoot, relativeCandidates[i], directory);
                    if (hit != null) return hit;
                }
            }

            return Combine(relativeCandidates[0]);
        }

        private static string Probe(string root, string relative, bool directory)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(relative)) return null;
            try
            {
                string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (directory ? Directory.Exists(full) : File.Exists(full)) return full;
            }
            catch { }
            return null;
        }
    }
}
