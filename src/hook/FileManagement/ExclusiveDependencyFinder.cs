using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;

namespace VPB
{
    /// <summary>
    /// Finds installed packages that selected packages depend on and that no remaining
    /// package still needs. Capture on main; Find on ThreadPool (managed + zip IO only).
    /// </summary>
    internal static class ExclusiveDependencyFinder
    {
        const int MaxJsonEntryBytes = 2 * 1024 * 1024;
        const int MaxTotalScanBytesPerVar = 32 * 1024 * 1024;
        const int MaxJsonEntriesPerVar = 48;

        internal sealed class ScanInput
        {
            public HashSet<string> SeedUids;
            public HashSet<string> InstalledUids;
            public Dictionary<string, string> GroupToNewestUid;
            public Dictionary<string, List<string>> GroupToUids;
            public Dictionary<string, int> UidToVersion;
            public Dictionary<string, List<string>> Forward;
            public Dictionary<string, string> SeedVarPaths;
            public HashSet<string> LockedUids;
        }

        internal sealed class ScanResult
        {
            public List<string> ExclusiveUids = new List<string>();
            public int SeedCount;
            public int ForwardSources;
            public int SeedTokens;
            public int Candidates;
            public int VarsScanned;
            public int ResolveMisses;
        }

        /// <summary>Main thread. Copies UID/version/edges; no Unity objects retained.</summary>
        internal static ScanInput CaptureInstalledSnapshot(IList<string> seedUids)
        {
            var input = new ScanInput();
            input.SeedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            input.InstalledUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            input.GroupToNewestUid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            input.GroupToUids = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            input.UidToVersion = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            input.Forward = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            input.SeedVarPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            input.LockedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                HashSet<string> locked = LockedPackagesManager.Instance != null
                    ? LockedPackagesManager.Instance.GetLockedPackages()
                    : null;
                if (locked != null)
                {
                    foreach (string u in locked)
                    {
                        if (!string.IsNullOrEmpty(u)) input.LockedUids.Add(u);
                    }
                }
            }
            catch { }

            if (seedUids != null)
            {
                for (int i = 0; i < seedUids.Count; i++)
                    AddCanonicalSeed(input, seedUids[i]);
            }

            Dictionary<string, int> newestVer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                lock (FileManager.packagesLock)
                {
                    Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                    if (byUid != null)
                    {
                        foreach (KeyValuePair<string, VarPackage> kv in byUid)
                        {
                            VarPackage pkg = kv.Value;
                            if (pkg == null) continue;
                            string uid = pkg.Uid;
                            if (string.IsNullOrEmpty(uid)) uid = kv.Key;
                            if (string.IsNullOrEmpty(uid)) continue;

                            RememberInstalled(input, newestVer, uid, pkg.Version);

                            if (input.SeedUids.Contains(uid))
                            {
                                MergeTokenList(input.Forward, uid, pkg.RecursivePackageDependencies);
                                MergeTokenList(input.Forward, uid, pkg.PackageDependencies);
                                if (!string.IsNullOrEmpty(pkg.Path) && !input.SeedVarPaths.ContainsKey(uid))
                                    input.SeedVarPaths[uid] = pkg.Path;
                            }
                        }
                    }
                }
            }
            catch { }

            return input;
        }

        static void AddCanonicalSeed(ScanInput input, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            string uid = raw;
            try
            {
                VarPackage pkg = FileManager.GetPackage(raw, false);
                if (pkg == null)
                    pkg = FileManager.GetPackageForDependency(raw, false);
                if (pkg != null && !string.IsNullOrEmpty(pkg.Uid))
                {
                    uid = pkg.Uid;
                    if (!string.IsNullOrEmpty(pkg.Path) && !input.SeedVarPaths.ContainsKey(uid))
                        input.SeedVarPaths[uid] = pkg.Path;
                    MergeTokenList(input.Forward, uid, pkg.RecursivePackageDependencies);
                    MergeTokenList(input.Forward, uid, pkg.PackageDependencies);
                }
            }
            catch { }
            input.SeedUids.Add(uid);
            string canon = FileManager.CanonicalizeUidSegments(uid);
            if (!string.IsNullOrEmpty(canon))
                input.SeedUids.Add(canon);
        }

        static void RememberInstalled(ScanInput input, Dictionary<string, int> newestVer, string uid, int ver)
        {
            input.InstalledUids.Add(uid);
            string canon = FileManager.CanonicalizeUidSegments(uid);
            if (!string.IsNullOrEmpty(canon))
                input.InstalledUids.Add(canon);
            input.UidToVersion[uid] = ver;

            string group = GroupShort(uid);
            if (string.IsNullOrEmpty(group)) group = GroupShort(canon);
            if (string.IsNullOrEmpty(group)) return;

            RememberInstalledGroup(input, newestVer, group, uid, ver);

            string canonGroup = GroupShort(canon);
            if (!string.IsNullOrEmpty(canonGroup) && !string.Equals(canonGroup, group, StringComparison.OrdinalIgnoreCase))
                RememberInstalledGroup(input, newestVer, canonGroup, uid, ver);
        }

        static void RememberInstalledGroup(ScanInput input, Dictionary<string, int> newestVer, string group, string uid, int ver)
        {
            List<string> list;
            if (!input.GroupToUids.TryGetValue(group, out list) || list == null)
            {
                list = new List<string>(2);
                input.GroupToUids[group] = list;
            }
            list.Add(uid);

            int best;
            if (!newestVer.TryGetValue(group, out best) || ver > best)
            {
                newestVer[group] = ver;
                input.GroupToNewestUid[group] = uid;
            }
        }

        /// <summary>Worker-safe. Zip scan for seeds with no SQL/Recursive tokens.</summary>
        internal static ScanResult Find(ScanInput input, Func<bool> isAborted)
        {
            var result = new ScanResult();
            if (input == null || input.SeedUids == null || input.SeedUids.Count == 0)
                return result;
            if (isAborted != null && isAborted()) return result;

            result.SeedCount = input.SeedUids.Count;
            var forward = input.Forward ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var sqlFwd = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (VpbLocalDatabase.TryLoadPackageDepEdgesUngated(sqlFwd) && sqlFwd.Count > 0)
                {
                    foreach (KeyValuePair<string, List<string>> kv in sqlFwd)
                        MergeTokenList(forward, kv.Key, kv.Value);
                }
            }
            catch { }

            foreach (string seed in input.SeedUids)
            {
                if (isAborted != null && isAborted()) return result;
                int n = TokenCount(forward, seed);
                if (n > 0) continue;
                string path;
                if (input.SeedVarPaths == null || !input.SeedVarPaths.TryGetValue(seed, out path) || string.IsNullOrEmpty(path))
                    continue;
                var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ScanVarForDepTokens(path, scanned, isAborted);
                result.VarsScanned++;
                MergeTokenList(forward, seed, new List<string>(scanned));
            }

            if (forward.Count == 0)
                TryFillForwardFromInstalledRecursive(forward, isAborted);

            result.ForwardSources = forward.Count;
            int seedTokens = 0;
            foreach (string seed in input.SeedUids)
                seedTokens += TokenCount(forward, seed);
            result.SeedTokens = seedTokens;

            if (forward.Count == 0) return result;

            var seen = new HashSet<string>(input.SeedUids, StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            foreach (string seed in input.SeedUids)
            {
                if (!string.IsNullOrEmpty(seed))
                    queue.Enqueue(seed);
            }

            int steps = 0;
            int resolveMiss = 0;
            while (queue.Count > 0)
            {
                if ((steps++ & 255) == 0 && isAborted != null && isAborted())
                    return result;

                string src = queue.Dequeue();
                List<string> tokens = LookupForward(forward, src);
                if (tokens == null) continue;
                for (int i = 0; i < tokens.Count; i++)
                {
                    string resolved = ResolveDepToken(tokens[i], input);
                    if (string.IsNullOrEmpty(resolved))
                    {
                        resolveMiss++;
                        continue;
                    }
                    if (!input.InstalledUids.Contains(resolved)) continue;
                    if (!seen.Add(resolved)) continue;
                    queue.Enqueue(resolved);
                }
            }
            result.ResolveMisses = resolveMiss;

            var candidates = new List<string>();
            foreach (string uid in seen)
            {
                if (!input.SeedUids.Contains(uid))
                    candidates.Add(uid);
            }
            result.Candidates = candidates.Count;
            if (candidates.Count == 0) return result;

            var reverse = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> kv in forward)
            {
                if (isAborted != null && isAborted()) return result;
                string src = kv.Key;
                List<string> tokens = kv.Value;
                if (string.IsNullOrEmpty(src) || tokens == null) continue;
                if (!input.InstalledUids.Contains(src)) continue;
                for (int i = 0; i < tokens.Count; i++)
                {
                    string resolved = ResolveDepToken(tokens[i], input);
                    if (string.IsNullOrEmpty(resolved)) continue;
                    if (string.Equals(resolved, src, StringComparison.OrdinalIgnoreCase)) continue;
                    HashSet<string> srcs;
                    if (!reverse.TryGetValue(resolved, out srcs) || srcs == null)
                    {
                        srcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        reverse[resolved] = srcs;
                    }
                    srcs.Add(src);
                }
            }

            var closure = new HashSet<string>(input.SeedUids, StringComparer.OrdinalIgnoreCase);
            var exclusiveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 64)
            {
                if (isAborted != null && isAborted()) return result;
                changed = false;
                for (int i = 0; i < candidates.Count; i++)
                {
                    string c = candidates[i];
                    if (closure.Contains(c)) continue;
                    if (input.LockedUids != null && input.LockedUids.Contains(c)) continue;
                    if (HasExternalDependent(c, closure, input.InstalledUids, reverse))
                        continue;
                    exclusiveSet.Add(c);
                    closure.Add(c);
                    changed = true;
                }
            }

            foreach (string uid in exclusiveSet)
                result.ExclusiveUids.Add(uid);
            result.ExclusiveUids.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        static int TokenCount(Dictionary<string, List<string>> forward, string src)
        {
            List<string> list = LookupForward(forward, src);
            return list != null ? list.Count : 0;
        }

        static List<string> LookupForward(Dictionary<string, List<string>> forward, string src)
        {
            if (forward == null || string.IsNullOrEmpty(src)) return null;
            List<string> list;
            if (forward.TryGetValue(src, out list) && list != null && list.Count > 0)
                return list;
            string canon = FileManager.CanonicalizeUidSegments(src);
            if (!string.IsNullOrEmpty(canon)
                && !string.Equals(canon, src, StringComparison.Ordinal)
                && forward.TryGetValue(canon, out list)
                && list != null
                && list.Count > 0)
                return list;
            return null;
        }

        static void MergeTokenList(Dictionary<string, List<string>> forward, string src, IList<string> tokens)
        {
            if (forward == null || string.IsNullOrEmpty(src) || tokens == null || tokens.Count == 0)
                return;
            List<string> list;
            HashSet<string> have = null;
            if (!forward.TryGetValue(src, out list) || list == null)
            {
                list = new List<string>(tokens.Count);
                forward[src] = list;
            }
            else
            {
                have = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = NormalizeDepToken(tokens[i]);
                if (string.IsNullOrEmpty(t)) continue;
                if (have != null && !have.Add(t)) continue;
                if (have == null)
                {
                    have = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                    if (!have.Add(t)) continue;
                }
                list.Add(t);
            }
        }

        static bool HasExternalDependent(
            string depUid,
            HashSet<string> closure,
            HashSet<string> installed,
            Dictionary<string, HashSet<string>> reverse)
        {
            HashSet<string> srcs;
            if (!reverse.TryGetValue(depUid, out srcs) || srcs == null || srcs.Count == 0)
                return false;
            foreach (string src in srcs)
            {
                if (string.IsNullOrEmpty(src)) continue;
                if (!installed.Contains(src)) continue;
                if (closure.Contains(src)) continue;
                return true;
            }
            return false;
        }

        static void TryFillForwardFromInstalledRecursive(Dictionary<string, List<string>> forward, Func<bool> isAborted)
        {
            try
            {
                lock (FileManager.packagesLock)
                {
                    Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                    if (byUid == null) return;
                    foreach (KeyValuePair<string, VarPackage> kv in byUid)
                    {
                        if (isAborted != null && isAborted()) return;
                        VarPackage pkg = kv.Value;
                        if (pkg == null) continue;
                        string uid = pkg.Uid;
                        if (string.IsNullOrEmpty(uid)) uid = kv.Key;
                        MergeTokenList(forward, uid, pkg.RecursivePackageDependencies);
                        MergeTokenList(forward, uid, pkg.PackageDependencies);
                    }
                }
            }
            catch { }
        }

        static void ScanVarForDepTokens(string varPath, HashSet<string> into, Func<bool> isAborted)
        {
            if (into == null || string.IsNullOrEmpty(varPath)) return;
            string path = varPath.Replace('\\', '/');
            if (!File.Exists(path)) return;
            long total = 0;
            int files = 0;
            try
            {
                using (ZipFile zf = new ZipFile(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete)))
                {
                    zf.IsStreamOwner = true;
                    foreach (ZipEntry e in zf)
                    {
                        if (isAborted != null && isAborted()) return;
                        if (e == null || !e.IsFile) continue;
                        string name = (e.Name ?? "").Replace('\\', '/');
                        bool meta = name.EndsWith("/meta.json", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "meta.json", StringComparison.OrdinalIgnoreCase);
                        bool jsonish = EndsWithDepJson(name);
                        if (!meta && !jsonish) continue;
                        if (e.Size < 0 || e.Size > MaxJsonEntryBytes) continue;
                        if (total + e.Size > MaxTotalScanBytesPerVar) break;
                        if (files >= MaxJsonEntriesPerVar && !meta) continue;

                        string text = null;
                        try
                        {
                            using (Stream s = zf.GetInputStream(e))
                            using (var reader = new StreamReader(s, Encoding.UTF8))
                                text = reader.ReadToEnd();
                        }
                        catch { text = null; }
                        if (string.IsNullOrEmpty(text)) continue;
                        total += text.Length;
                        files++;
                        if (meta)
                        {
                            try
                            {
                                HashSet<string> fromMeta = DependencyExtractor.ExtractDependenciesFromJson(text, false);
                                if (fromMeta != null)
                                {
                                    foreach (string t in fromMeta)
                                    {
                                        string n = NormalizeDepToken(t);
                                        if (!string.IsNullOrEmpty(n)) into.Add(n);
                                    }
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            try { DependencyExtractor.ExtractDependenciesWithRegex(text, into); }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        static bool EndsWithDepJson(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".vaj", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".vam", StringComparison.OrdinalIgnoreCase);
        }

        static string ResolveDepToken(string token, ScanInput input)
        {
            string t = NormalizeDepToken(token);
            if (string.IsNullOrEmpty(t) || input == null) return null;
            if (input.InstalledUids != null && input.InstalledUids.Contains(t))
                return t;

            string canonToken = FileManager.CanonicalizeUidSegments(t);
            if (!string.Equals(canonToken, t, StringComparison.Ordinal)
                && input.InstalledUids != null
                && input.InstalledUids.Contains(canonToken))
                return canonToken;

            string group = GroupShort(t);
            if (string.IsNullOrEmpty(group)) return null;
            string ver = LastSegment(t);
            if (string.IsNullOrEmpty(ver)) return null;

            if (string.Equals(ver, "latest", StringComparison.OrdinalIgnoreCase))
                return NewestInGroup(group, input);

            if (ver.Length > 3 && ver.StartsWith("min", StringComparison.OrdinalIgnoreCase))
            {
                int minVer;
                if (!int.TryParse(ver.Substring(3), out minVer)) return null;
                return ClosestMin(group, minVer, input);
            }

            // Exact pin missing: same as ForceLatest / GetPackageForDependency fallback.
            return NewestInGroup(group, input);
        }

        static string NewestInGroup(string group, ScanInput input)
        {
            string newest;
            if (input.GroupToNewestUid == null) return null;
            if (input.GroupToNewestUid.TryGetValue(group, out newest))
                return newest;
            string canonGroup = FileManager.CanonicalizeUidSegments(group);
            if (!string.Equals(canonGroup, group, StringComparison.Ordinal)
                && input.GroupToNewestUid.TryGetValue(canonGroup, out newest))
                return newest;
            return null;
        }

        static string ClosestMin(string group, int minVer, ScanInput input)
        {
            List<string> uids;
            if (input.GroupToUids == null) return null;
            if (!input.GroupToUids.TryGetValue(group, out uids) || uids == null)
            {
                string canonGroup = FileManager.CanonicalizeUidSegments(group);
                if (string.Equals(canonGroup, group, StringComparison.Ordinal)
                    || !input.GroupToUids.TryGetValue(canonGroup, out uids)
                    || uids == null)
                    return null;
            }
            string bestUid = null;
            int bestVer = int.MaxValue;
            for (int i = 0; i < uids.Count; i++)
            {
                string uid = uids[i];
                int v;
                if (input.UidToVersion == null || !input.UidToVersion.TryGetValue(uid, out v))
                    v = ParseVersion(uid);
                if (v < minVer) continue;
                if (v < bestVer)
                {
                    bestVer = v;
                    bestUid = uid;
                }
            }
            return bestUid;
        }

        static string NormalizeDepToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            string d = token.Replace('\\', '/').Trim();
            int colonSlash = d.IndexOf(":/", StringComparison.Ordinal);
            if (colonSlash > 0) d = d.Substring(0, colonSlash);
            int lastSlash = d.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash + 1 < d.Length) d = d.Substring(lastSlash + 1);
            if (d.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                d = d.Substring(0, d.Length - 4);
            if (string.IsNullOrEmpty(d)) return null;
            if (string.Equals(d, "SELF", StringComparison.OrdinalIgnoreCase)) return null;
            return d;
        }

        static string GroupShort(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            int firstDot = uid.IndexOf('.');
            if (firstDot < 0) return null;
            int secondDot = uid.IndexOf('.', firstDot + 1);
            if (secondDot < 0) return null;
            return uid.Substring(0, secondDot);
        }

        static string LastSegment(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            int lastDot = uid.LastIndexOf('.');
            if (lastDot < 0 || lastDot + 1 >= uid.Length) return null;
            return uid.Substring(lastDot + 1);
        }

        static int ParseVersion(string uid)
        {
            string seg = LastSegment(uid);
            int v;
            if (!string.IsNullOrEmpty(seg) && int.TryParse(seg, out v)) return v;
            return 0;
        }
    }
}
