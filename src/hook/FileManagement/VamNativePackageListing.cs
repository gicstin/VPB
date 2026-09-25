using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using VPB.src.util;

namespace VPB
{
    internal static class VamNativePackageListing
    {
        const string PackagePattern = "*.var";
        const double FreshDirectorySeconds = 3.0;
        const double UncacheableTreeRetrySeconds = 2.0;

        internal sealed class TreeStamp
        {
            internal string Root;
            internal string[] Dirs;
            internal long[] Mtimes;
        }

        sealed class Snapshot
        {
            internal TreeStamp Tree;
            internal string[] Entries;
            internal bool[] Blocked;
            internal int BlockedVersion = int.MinValue;
        }

        struct ListingOutcome
        {
            internal bool Cached;
            internal int Listed;
            internal int Kept;
            internal int Skipped;
            internal long Ticks;
        }

        static readonly object s_Lock = new object();
        static readonly FieldInfo s_PackagesByPathField =
            AccessTools.Field(typeof(MVR.FileManagement.FileManager), "packagesByPath");

        static Snapshot s_Files;
        static Snapshot s_Directories;
        static TreeStamp s_LastTree;
        static string s_LastTreeRoot;
        static long s_LastTreeCapturedAt;
        static ListingOutcome s_LastDirectoriesOutcome;
        static bool s_HasDirectoriesOutcome;
        static int s_RedirectedCallSites;

        internal static int RedirectedCallSites { get { return s_RedirectedCallSites; } }
        internal static bool LastListingWasCached { get; private set; }

        internal static bool Enabled
        {
            get
            {
                try
                {
                    var settings = Settings.Instance;
                    if (settings == null || settings.PerfFastNativePackageRefresh == null) return true;
                    return settings.PerfFastNativePackageRefresh.Value;
                }
                catch { return true; }
            }
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                MethodInfo refresh = AccessTools.Method(typeof(MVR.FileManagement.FileManager), "Refresh", Type.EmptyTypes);
                if (refresh == null) throw new MissingMethodException("MVR.FileManagement.FileManager", "Refresh");
                harmony.Patch(refresh, transpiler: new HarmonyMethod(typeof(VamNativePackageListing), nameof(RedirectPackageFolderListing)));
                if (s_RedirectedCallSites < 2)
                    LogUtil.LogWarning(VamStartupOptimizations.LogTag + " native package listing: FileManager.Refresh redirected "
                        + s_RedirectedCallSites + " of 2 package folder scans; the rest run unoptimized");
                else
                    PrewarmAsync(MVR.FileManagement.FileManager.PackageFolder);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning(VamStartupOptimizations.LogTag + " native package listing patch failed, VaM package refresh runs unoptimized: " + ex.Message);
            }
        }

        internal static IEnumerable<CodeInstruction> RedirectPackageFolderListing(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo listFiles = AccessTools.Method(typeof(VamNativePackageListing), nameof(ListFiles));
            MethodInfo listDirectories = AccessTools.Method(typeof(VamNativePackageListing), nameof(ListDirectories));
            int redirected = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call)
                {
                    MethodInfo callee = instruction.operand as MethodInfo;
                    if (IsRecursiveDirectoryQuery(callee, "GetFiles"))
                    {
                        instruction.operand = listFiles;
                        redirected++;
                    }
                    else if (IsRecursiveDirectoryQuery(callee, "GetDirectories"))
                    {
                        instruction.operand = listDirectories;
                        redirected++;
                    }
                }
                yield return instruction;
            }
            s_RedirectedCallSites = redirected;
        }

        static bool IsRecursiveDirectoryQuery(MethodInfo method, string name)
        {
            if (method == null || method.DeclaringType != typeof(Directory) || method.Name != name) return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 3
                && parameters[0].ParameterType == typeof(string)
                && parameters[1].ParameterType == typeof(string)
                && parameters[2].ParameterType == typeof(SearchOption);
        }

        public static string[] ListDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            if (!IsPackageFolderScan(searchPattern, searchOption))
                return Directory.GetDirectories(path, searchPattern, searchOption);

            ListingOutcome outcome;
            string[] result = List(path, searchPattern, searchOption, true, out outcome);
            s_LastDirectoriesOutcome = outcome;
            s_HasDirectoriesOutcome = true;
            return result;
        }

        public static string[] ListFiles(string path, string searchPattern, SearchOption searchOption)
        {
            if (!IsPackageFolderScan(searchPattern, searchOption))
                return Directory.GetFiles(path, searchPattern, searchOption);

            ListingOutcome outcome;
            string[] result = List(path, searchPattern, searchOption, false, out outcome);
            LogRefreshListing(outcome);
            return result;
        }

        static bool IsPackageFolderScan(string searchPattern, SearchOption searchOption)
        {
            return searchOption == SearchOption.AllDirectories
                && string.Equals(searchPattern, PackagePattern, StringComparison.Ordinal)
                && Enabled;
        }

        static string[] List(string path, string searchPattern, SearchOption searchOption, bool directories, out ListingOutcome outcome)
        {
            long started = Stopwatch.GetTimestamp();
            outcome = new ListingOutcome();
            bool cached;
            Snapshot snapshot = ResolveSnapshot(path, searchPattern, searchOption, directories, out cached);
            LastListingWasCached = cached;

            int skipped;
            string[] result = KeepRegistrableEntries(snapshot, out skipped);
            VamScanFilter.RecordScanBlocked(skipped);

            outcome.Cached = cached;
            outcome.Listed = snapshot.Entries != null ? snapshot.Entries.Length : 0;
            outcome.Kept = result != null ? result.Length : 0;
            outcome.Skipped = skipped;
            outcome.Ticks = Stopwatch.GetTimestamp() - started;
            return result;
        }

        static Snapshot ResolveSnapshot(string path, string searchPattern, SearchOption searchOption, bool directories, out bool cached)
        {
            lock (s_Lock)
            {
                Snapshot current = directories ? s_Directories : s_Files;
                if (current != null
                    && string.Equals(current.Tree.Root, path, StringComparison.Ordinal)
                    && TreeUnchanged(current.Tree))
                {
                    cached = true;
                    return current;
                }

                cached = false;
                TreeStamp tree = TreeForEnumeration(path);
                string[] entries = directories
                    ? Directory.GetDirectories(path, searchPattern, searchOption)
                    : Directory.GetFiles(path, searchPattern, searchOption);

                var fresh = new Snapshot { Tree = tree, Entries = entries };
                Snapshot stored = tree != null ? fresh : null;
                if (directories) s_Directories = stored;
                else s_Files = stored;
                return fresh;
            }
        }

        static TreeStamp TreeForEnumeration(string root)
        {
            bool sameRoot = string.Equals(s_LastTreeRoot, root, StringComparison.Ordinal);
            if (sameRoot && s_LastTree != null && TreeUnchanged(s_LastTree))
                return s_LastTree;

            long now = Stopwatch.GetTimestamp();
            double ageSeconds = (now - s_LastTreeCapturedAt) / (double)Stopwatch.Frequency;
            if (sameRoot && s_LastTree == null && ageSeconds >= 0 && ageSeconds < UncacheableTreeRetrySeconds)
                return null;

            TreeStamp tree = CaptureTree(root);
            s_LastTree = tree;
            s_LastTreeRoot = root;
            s_LastTreeCapturedAt = now;
            return tree;
        }

        internal static void PrewarmAsync(string root)
        {
            if (string.IsNullOrEmpty(root) || !Enabled) return;
            try { ThreadPool.QueueUserWorkItem(_ => Prewarm(root)); }
            catch (Exception ex)
            {
                LogUtil.LogWarning(VamStartupOptimizations.LogTag + " native package listing prewarm not queued: " + ex.Message);
            }
        }

        internal static bool Prewarm(string root)
        {
            try
            {
                long started = Stopwatch.GetTimestamp();
                int entries;
                lock (s_Lock)
                {
                    if (s_Files != null || s_Directories != null || !Directory.Exists(root)) return false;
                    TreeStamp tree = TreeForEnumeration(root);
                    if (tree == null) return false;
                    string[] dirs = Directory.GetDirectories(root, PackagePattern, SearchOption.AllDirectories);
                    string[] files = Directory.GetFiles(root, PackagePattern, SearchOption.AllDirectories);
                    s_Directories = new Snapshot { Tree = tree, Entries = dirs };
                    s_Files = new Snapshot { Tree = tree, Entries = files };
                    entries = dirs.Length + files.Length;
                }
                double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                VPBLogger.Files.LogInfo(string.Format(CultureInfo.InvariantCulture,
                    "{0} native package listing prewarmed off the main thread entries={1} ms={2:0}",
                    VamStartupOptimizations.LogTag, entries, ms));
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning(VamStartupOptimizations.LogTag + " native package listing prewarm failed, VaM scans the folder itself: " + ex.Message);
                return false;
            }
        }

        internal static TreeStamp CaptureTree(string root)
        {
            if (string.IsNullOrEmpty(root)) return null;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
            catch { return null; }

            var dirs = new string[subdirs.Length + 1];
            dirs[0] = root;
            Array.Copy(subdirs, 0, dirs, 1, subdirs.Length);

            DateTime freshAfter = DateTime.UtcNow.AddSeconds(-FreshDirectorySeconds);
            var mtimes = new long[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                long mtime;
                bool reparsePoint;
                if (!FileManager.TryGetDirectoryLastWriteBinaryFollowingLinks(dirs[i], out mtime, out reparsePoint))
                    return null;
                if (DateTime.FromBinary(mtime) > freshAfter)
                    return null;
                mtimes[i] = mtime;
            }

            return new TreeStamp { Root = root, Dirs = dirs, Mtimes = mtimes };
        }

        internal static bool TreeUnchanged(TreeStamp stamp)
        {
            if (stamp == null || stamp.Dirs == null || stamp.Mtimes == null) return false;
            for (int i = 0; i < stamp.Dirs.Length; i++)
            {
                long mtime;
                bool reparsePoint;
                if (!FileManager.TryGetDirectoryLastWriteBinaryFollowingLinks(stamp.Dirs[i], out mtime, out reparsePoint))
                    return false;
                if (mtime != stamp.Mtimes[i]) return false;
            }
            return true;
        }

        static string[] KeepRegistrableEntries(Snapshot snapshot, out int skipped)
        {
            skipped = 0;
            string[] entries = snapshot.Entries;
            if (entries == null) return null;
            if (entries.Length == 0 || VamOnDemandLoader.s_AllowRegistration || !ScanWhitelistManager.Instance.IsEnabled)
                return (string[])entries.Clone();

            bool[] blocked = BlockedFlags(snapshot);
            var registered = s_PackagesByPathField != null
                ? s_PackagesByPathField.GetValue(null) as IDictionary<string, MVR.FileManagement.VarPackage>
                : null;
            return KeepRegistrableEntries(entries, blocked, registered, out skipped);
        }

        static bool[] BlockedFlags(Snapshot snapshot)
        {
            int version = ScanWhitelistManager.StateVersion;
            if (snapshot.Blocked != null && snapshot.BlockedVersion == version) return snapshot.Blocked;

            string[] entries = snapshot.Entries;
            var blocked = new bool[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                blocked[i] = VamScanFilter.ClassifyNativeRegistration(entries[i]) == VamScanFilter.NativeRegistrationGate.Blocked;
            snapshot.Blocked = blocked;
            snapshot.BlockedVersion = version;
            return blocked;
        }

        internal static string[] KeepRegistrableEntries(
            string[] entries,
            bool[] blocked,
            IDictionary<string, MVR.FileManagement.VarPackage> registered,
            out int skipped)
        {
            skipped = 0;
            var kept = new List<string>(Math.Min(entries.Length, 1024));
            bool keptOneBlockedSoVamStillRunsRefreshHandlers = false;
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i];
                if (!blocked[i] || (registered != null && entry != null && registered.ContainsKey(entry)))
                {
                    kept.Add(entry);
                }
                else if (!keptOneBlockedSoVamStillRunsRefreshHandlers)
                {
                    keptOneBlockedSoVamStillRunsRefreshHandlers = true;
                    kept.Add(entry);
                }
                else
                {
                    skipped++;
                }
            }
            return kept.ToArray();
        }

        static void LogRefreshListing(ListingOutcome files)
        {
            try
            {
                ListingOutcome dirs = s_LastDirectoriesOutcome;
                bool hasDirs = s_HasDirectoriesOutcome;
                s_HasDirectoriesOutcome = false;

                double ms = (files.Ticks + (hasDirs ? dirs.Ticks : 0L)) * 1000.0 / Stopwatch.Frequency;
                VPBLogger.Files.LogInfo(string.Format(CultureInfo.InvariantCulture,
                    "{0} native package listing files={1} dirs={2} listed={3} kept={4} skipped_blocked={5} ms={6:0.0}",
                    VamStartupOptimizations.LogTag,
                    files.Cached ? "cached" : "scanned",
                    hasDirs ? (dirs.Cached ? "cached" : "scanned") : "-",
                    files.Listed + (hasDirs ? dirs.Listed : 0),
                    files.Kept + (hasDirs ? dirs.Kept : 0),
                    files.Skipped + (hasDirs ? dirs.Skipped : 0),
                    ms));
            }
            catch { }
        }

        internal static void ResetForTests()
        {
            lock (s_Lock)
            {
                s_Files = null;
                s_Directories = null;
                s_LastTree = null;
                s_LastTreeRoot = null;
                s_LastTreeCapturedAt = 0;
                s_HasDirectoriesOutcome = false;
                LastListingWasCached = false;
            }
        }
    }
}
