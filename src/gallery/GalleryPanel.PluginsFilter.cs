using System;
using System.Collections.Generic;
using System.IO;
using VpbDb = VPB.VpbLocalDatabase;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>Parent .cslist (or orphan .cs/.dll leaf) for Plugins float tree.</summary>
        private sealed class PluginsFloatRoot
        {
            public FileEntry Entry;
            public List<FileEntry> Children;
            /// <summary>False until expand (or search) parses this .cslist — avoids OpenStream stall on open.</summary>
            public bool ChildrenLoaded;
            public bool IsCslist;
            public int MaxRating;
            public string Creator;
            public string PackageName;
            public int Version;
            public string PackageUid;
        }

        /// <summary>Creator master group — packages nest underneath (Gestalt + browse by author).</summary>
        private sealed class PluginsFloatCreatorGroup
        {
            public string Creator;
            public string ExpandKey;
            public List<PluginsFloatRoot> Roots;
            public int MaxRating;
        }

        /// <summary>True after async warm of cslist membership; until then orphan .cs are skipped.</summary>
        private bool _cslistReferencedWarm;

        private HashSet<string> GetCslistReferencedPaths()
        {
            var local = _cslistReferencedPaths;
            if (local != null) return local;

            lock (_cslistReferencedLock)
            {
                if (_cslistReferencedPaths != null) return _cslistReferencedPaths;
                // Never sync SQL here — LIKE over plugins:cslist_referenced_% blocks main/worker
                // against deferred Persist during deep scan (float stuck on Loading…).
                _cslistReferencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return _cslistReferencedPaths;
            }
        }

        // Mtime alone suffices: a new VAR version is a different uid, so any mtime change on
        // this uid means the same file was modified or replaced.
        internal static string PerVarSig(VarPackage pkg)
        {
            if (pkg == null) return "0";
            try { return pkg.LastWriteTime.ToBinary().ToString(); } catch { return "0"; }
        }

        private bool IsCsReferencedByAnyCslist(FileEntry entry)
        {
            if (entry == null) return false;
            string p = entry.Path;
            if (string.IsNullOrEmpty(p)) return false;
            if (!p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;

            // VAR entries: writer stored Uid form (author.pkg.1:/custom/scripts/foo.cs).
            // Loose disk: writer stored relative path (custom/scripts/foo.cs). Use Path for those.
            string norm = (entry is VarFileEntry)
                ? (entry.UidLowerInvariant ?? string.Empty)
                : p.Replace('\\', '/').ToLowerInvariant();
            var set = GetCslistReferencedPaths();
            return set.Contains(norm);
        }

        public void InvalidateCslistReferencedCache()
        {
            lock (_cslistReferencedLock)
            {
                _cslistReferencedPaths = null;
                _cslistReferencedWarm = false;
            }
            _pluginsFloatChildCache.Clear();
            _pluginsFloatChildCacheSig = null;
            try
            {
                if (_pluginsFloatChildrenSql != null)
                    _pluginsFloatChildrenSql.Clear();
            }
            catch { }
        }

        // ---- Plugins float tree (warm path; not per-frame) ----

        private readonly Dictionary<string, List<FileEntry>> _pluginsFloatChildCache =
            new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
        private string _pluginsFloatChildCacheSig;

        private static string PluginsFloatEntryExt(FileEntry entry)
        {
            if (entry == null) return string.Empty;
            string name = null;
            try { name = entry.Name; } catch { name = null; }
            if (string.IsNullOrEmpty(name))
            {
                try { name = entry.Path; } catch { name = null; }
            }
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return Path.GetExtension(name).ToLowerInvariant();
        }

        private static string PluginsFloatNormKey(FileEntry entry)
        {
            if (entry == null) return string.Empty;
            if (entry is VarFileEntry)
            {
                string uid = entry.UidLowerInvariant;
                return uid ?? string.Empty;
            }
            string p = null;
            try { p = entry.Path; } catch { p = null; }
            if (string.IsNullOrEmpty(p)) return string.Empty;
            return p.Replace('\\', '/').ToLowerInvariant();
        }

        private static string PluginsFloatCslistDir(FileEntry cslist)
        {
            if (cslist == null) return string.Empty;
            string path = null;
            if (cslist is VarFileEntry vfe)
            {
                try { path = vfe.InternalPath; } catch { path = null; }
            }
            if (string.IsNullOrEmpty(path))
            {
                try { path = cslist.Path; } catch { path = null; }
            }
            if (string.IsNullOrEmpty(path)) return string.Empty;
            path = path.Replace('\\', '/');
            int sep = path.IndexOf(":/", StringComparison.Ordinal);
            if (sep >= 0 && !(sep == 1 && char.IsLetter(path[0])))
                path = path.Substring(sep + 2);
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : string.Empty;
        }

        private void IndexPluginsFloatLookup(FileEntry entry, Dictionary<string, FileEntry> map)
        {
            if (entry == null || map == null) return;
            string uid = null;
            try { uid = entry.Uid; } catch { uid = null; }
            if (!string.IsNullOrEmpty(uid))
                map[uid.ToLowerInvariant()] = entry;

            string path = null;
            try { path = entry.Path; } catch { path = null; }
            if (!string.IsNullOrEmpty(path))
                map[path.Replace('\\', '/').ToLowerInvariant()] = entry;

            VarFileEntry vfe = entry as VarFileEntry;
            if (vfe != null)
            {
                string ip = null;
                try { ip = vfe.InternalPath; } catch { ip = null; }
                if (!string.IsNullOrEmpty(ip))
                {
                    string ipn = ip.Replace('\\', '/').ToLowerInvariant();
                    map[ipn] = entry;
                    if (vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                        map[(vfe.Package.Uid + ":/" + ipn).ToLowerInvariant()] = entry;
                }
            }
        }

        private List<FileEntry> ResolveCslistChildren(FileEntry cslist, Dictionary<string, FileEntry> lookup)
        {
            var result = new List<FileEntry>(8);
            if (cslist == null || lookup == null) return result;

            string cacheKey = null;
            try { cacheKey = cslist.Uid; } catch { cacheKey = null; }
            if (string.IsNullOrEmpty(cacheKey))
            {
                try { cacheKey = cslist.Path; } catch { cacheKey = null; }
            }
            if (!string.IsNullOrEmpty(cacheKey) && _pluginsFloatChildCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            // Preferred: SQL parent→children (written at scan). No OpenStream.
            string parentKey = PluginsFloatParentKey(cslist);
            List<string> sqlKids;
            if (TryGetPluginsFloatChildrenFromSql(parentKey, out sqlKids) && sqlKids != null)
            {
                for (int i = 0; i < sqlKids.Count; i++)
                {
                    string r = sqlKids[i];
                    if (string.IsNullOrEmpty(r)) continue;
                    FileEntry child;
                    if (lookup.TryGetValue(r, out child) && child != null && !result.Contains(child))
                        result.Add(child);
                }
                if (!string.IsNullOrEmpty(cacheKey))
                    _pluginsFloatChildCache[cacheKey] = result;
                return result;
            }

            // Cold fallback: parse once (index miss / pre-tree library), then leave for next scan to SQL-fill.
            string dir = PluginsFloatCslistDir(cslist);
            List<string> refs = null;
            try
            {
                using (FileEntryStream fes = cslist.OpenStream())
                {
                    if (fes != null && fes.Stream != null)
                        refs = VPB.src.util.CslistParser.ParseReferencedCsPaths(fes.Stream, dir);
                }
            }
            catch { refs = null; }

            if (refs != null)
            {
                string pkgPrefix = null;
                VarFileEntry vfe = cslist as VarFileEntry;
                if (vfe != null && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                    pkgPrefix = vfe.Package.Uid + ":/";

                var childKeys = new List<string>(refs.Count);
                for (int i = 0; i < refs.Count; i++)
                {
                    string r = refs[i];
                    if (string.IsNullOrEmpty(r)) continue;
                    string full = pkgPrefix != null ? (pkgPrefix + r).ToLowerInvariant() : r.ToLowerInvariant();
                    childKeys.Add(full);
                    FileEntry child = null;
                    if (pkgPrefix != null)
                        lookup.TryGetValue(full, out child);
                    if (child == null)
                        lookup.TryGetValue(r, out child);
                    if (child != null && !result.Contains(child))
                        result.Add(child);
                }
                try
                {
                    if (_pluginsFloatChildrenSql != null)
                        _pluginsFloatChildrenSql[parentKey.Replace('\\', '/').ToLowerInvariant()] = childKeys;
                }
                catch { }
                // Persist off main thread — expand must not hitch on SQLite write.
                string parentCopy = parentKey;
                var childCopy = childKeys;
                System.Threading.ThreadPool.QueueUserWorkItem(__ =>
                {
                    try { VpbLocalDatabase.WriteCslistChildrenForParent(parentCopy, childCopy); } catch { }
                });
            }

            if (!string.IsNullOrEmpty(cacheKey))
                _pluginsFloatChildCache[cacheKey] = result;
            return result;
        }

        private static string PluginsFloatParentKey(FileEntry cslist)
        {
            if (cslist == null) return string.Empty;
            VarFileEntry vfe = cslist as VarFileEntry;
            if (vfe != null && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
            {
                string ip = null;
                try { ip = vfe.InternalPath; } catch { ip = null; }
                return VpbLocalDatabase.NormalizeCslistParentKey(vfe.Package.Uid, ip ?? "");
            }
            string path = null;
            try { path = cslist.Path; } catch { path = null; }
            if (string.IsNullOrEmpty(path))
            {
                try { path = cslist.Uid; } catch { path = null; }
            }
            return VpbLocalDatabase.NormalizeCslistParentKey(null, path ?? "");
        }

        private static int PluginsFloatGetRating(FileEntry entry)
        {
            if (entry == null) return 0;
            try { return RatingsManager.Instance != null ? RatingsManager.Instance.GetRating(entry) : 0; }
            catch { return 0; }
        }

        /// <summary>Canonical Author.Name.Version for VAR rows; null/empty for loose disk.</summary>
        private static string PluginsFloatPackageUid(FileEntry entry)
        {
            if (entry == null) return null;
            string raw = null;
            VarFileEntry vfe = entry as VarFileEntry;
            if (vfe != null)
            {
                try
                {
                    raw = vfe.GetRowPackageUid();
                }
                catch { raw = null; }
                if (string.IsNullOrEmpty(raw))
                {
                    try
                    {
                        if (vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                            raw = vfe.Package.Uid;
                    }
                    catch { }
                }
            }
            if (string.IsNullOrEmpty(raw)) return null;
            // Index sometimes stores AddonPackages/Author.Name.N.var — strip so latest compare works.
            string canon = CanonicalVarPackageUidFromPathOrHint(raw);
            return !string.IsNullOrEmpty(canon) ? canon : raw;
        }

        /// <summary>Creator.Name group + integer version via shared VAR uid parser.</summary>
        private static bool TryParsePluginsFloatPackageGroupVersion(string uid, out string group, out int version)
        {
            group = null;
            version = -1;
            string creator;
            string packageName;
            if (!TryParseVarUidParts(uid, out creator, out packageName, out version))
                return false;
            group = creator + "." + packageName;
            return true;
        }

        private static void PluginsFloatFillMeta(FileEntry entry, PluginsFloatRoot root)
        {
            if (root == null) return;
            root.PackageUid = PluginsFloatPackageUid(entry);
            root.Creator = null;
            root.PackageName = null;
            root.Version = -1;
            string creator;
            string pkgName;
            int ver;
            if (!string.IsNullOrEmpty(root.PackageUid)
                && TryParseVarUidParts(root.PackageUid, out creator, out pkgName, out ver))
            {
                root.Creator = creator;
                root.PackageName = pkgName;
                root.Version = ver;
                return;
            }
            root.Creator = "";
            try { root.PackageName = entry != null ? entry.Name : ""; }
            catch { root.PackageName = ""; }
        }

        private static HashSet<string> BuildPluginsFloatLatestPackageUidSet(IList<FileEntry> source)
        {
            var maxVer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var maxUid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < source.Count; i++)
            {
                string uid = PluginsFloatPackageUid(source[i]);
                string group;
                int ver;
                if (!TryParsePluginsFloatPackageGroupVersion(uid, out group, out ver)) continue;
                int prev;
                if (!maxVer.TryGetValue(group, out prev) || ver > prev)
                {
                    maxVer[group] = ver;
                    maxUid[group] = uid;
                }
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in maxUid)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                    set.Add(kv.Value);
            }
            return set;
        }

        private static bool PluginsFloatEntryPassesLatestFilter(FileEntry entry, HashSet<string> latestUids)
        {
            if (entry == null || latestUids == null) return true;
            string uid = PluginsFloatPackageUid(entry);
            // Loose Custom/Scripts (no package uid) always pass.
            if (string.IsNullOrEmpty(uid)) return true;
            string group;
            int ver;
            // Non-integer / odd uids: keep (cannot rank). Canonicalize handles AddonPackages/*.var form.
            if (!TryParsePluginsFloatPackageGroupVersion(uid, out group, out ver))
                return true;
            return latestUids.Contains(uid);
        }

        private static string PluginsFloatCreatorExpandKey(string creator)
        {
            string c = creator ?? "";
            if (c.Length == 0) return "c:_loose";
            return "c:" + c.ToLowerInvariant();
        }

        /// <summary>
        /// Build cslist parents + orphan leaves. Children stay lazy until expand
        /// (OpenStream per cslist on open stalls VaM when library is large).
        /// </summary>
        private List<PluginsFloatRoot> BuildPluginsFloatRoots(IList<FileEntry> source)
        {
            var roots = new List<PluginsFloatRoot>(64);
            if (source == null || source.Count == 0) return roots;

            string sig = source.Count.ToString();
            try
            {
                FileEntry a = source[0];
                FileEntry b = source[source.Count - 1];
                sig = source.Count + "|" + (a != null ? a.Uid : "") + "|" + (b != null ? b.Uid : "");
            }
            catch { }
            if (!string.Equals(sig, _pluginsFloatChildCacheSig, StringComparison.Ordinal))
            {
                _pluginsFloatChildCache.Clear();
                _pluginsFloatChildCacheSig = sig;
            }

            var cslists = new List<FileEntry>(256);
            var orphans = new List<FileEntry>(64);
            // Until refs warm: only .cslist + .dll. Showing all .cs as orphans ≈ 20k UI rows → freeze.
            bool refsWarm = _cslistReferencedWarm;
            HashSet<string> latestUids = null;
            if (_pluginsFloatLatestOnly)
            {
                if (_pluginsFloatLatestUidSet == null)
                    _pluginsFloatLatestUidSet = BuildPluginsFloatLatestPackageUidSet(source);
                latestUids = _pluginsFloatLatestUidSet;
            }

            for (int i = 0; i < source.Count; i++)
            {
                FileEntry e = source[i];
                if (e == null) continue;
                if (latestUids != null && !PluginsFloatEntryPassesLatestFilter(e, latestUids))
                    continue;
                string ext = PluginsFloatEntryExt(e);
                if (ext == ".cslist")
                    cslists.Add(e);
                else if (ext == ".cs")
                {
                    if (_pluginsFloatCslistOnly) continue;
                    if (!refsWarm) continue;
                    if (!IsCsReferencedByAnyCslist(e))
                        orphans.Add(e);
                }
                else if (ext == ".dll")
                {
                    if (_pluginsFloatCslistOnly) continue;
                    orphans.Add(e);
                }
            }

            for (int i = 0; i < cslists.Count; i++)
            {
                FileEntry parent = cslists[i];
                var root = new PluginsFloatRoot
                {
                    Entry = parent,
                    Children = null,
                    ChildrenLoaded = false,
                    IsCslist = true,
                    MaxRating = PluginsFloatGetRating(parent)
                };
                PluginsFloatFillMeta(parent, root);
                roots.Add(root);
            }

            for (int i = 0; i < orphans.Count; i++)
            {
                FileEntry e = orphans[i];
                var root = new PluginsFloatRoot
                {
                    Entry = e,
                    Children = null,
                    ChildrenLoaded = true,
                    IsCslist = false,
                    MaxRating = PluginsFloatGetRating(e)
                };
                PluginsFloatFillMeta(e, root);
                roots.Add(root);
            }

            return roots;
        }

        private List<PluginsFloatCreatorGroup> GroupPluginsFloatByCreator(List<PluginsFloatRoot> roots)
        {
            var groups = new List<PluginsFloatCreatorGroup>(64);
            if (roots == null || roots.Count == 0) return groups;

            var map = new Dictionary<string, PluginsFloatCreatorGroup>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roots.Count; i++)
            {
                PluginsFloatRoot root = roots[i];
                if (root == null || root.Entry == null) continue;
                string creator = root.Creator ?? "";
                string mapKey = creator.Length == 0 ? "" : creator;
                PluginsFloatCreatorGroup g;
                if (!map.TryGetValue(mapKey, out g) || g == null)
                {
                    g = new PluginsFloatCreatorGroup
                    {
                        Creator = creator,
                        ExpandKey = PluginsFloatCreatorExpandKey(creator),
                        Roots = new List<PluginsFloatRoot>(8),
                        MaxRating = 0
                    };
                    map[mapKey] = g;
                    groups.Add(g);
                }
                g.Roots.Add(root);
                if (root.MaxRating > g.MaxRating)
                    g.MaxRating = root.MaxRating;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                PluginsFloatCreatorGroup g = groups[i];
                if (g == null || g.Roots == null) continue;
                g.Roots.Sort(ComparePluginsFloatRoots);
            }
            return groups;
        }

        private Dictionary<string, FileEntry> BuildPluginsFloatLookup(IList<FileEntry> source)
        {
            var lookup = new Dictionary<string, FileEntry>(
                source != null ? source.Count * 2 : 8,
                StringComparer.OrdinalIgnoreCase);
            if (source == null) return lookup;
            for (int i = 0; i < source.Count; i++)
                IndexPluginsFloatLookup(source[i], lookup);
            return lookup;
        }

        /// <summary>Parse one .cslist on expand — warm path, not open path.</summary>
        private void EnsurePluginsFloatRootChildren(PluginsFloatRoot root, IList<FileEntry> catalog)
        {
            if (root == null || !root.IsCslist || root.ChildrenLoaded) return;
            var lookup = BuildPluginsFloatLookup(catalog);
            List<FileEntry> children = ResolveCslistChildren(root.Entry, lookup);
            if (children == null) children = new List<FileEntry>();

            // Latest-only: drop child scripts that resolve into older package versions.
            if (_pluginsFloatLatestOnly)
            {
                if (_pluginsFloatLatestUidSet == null)
                    _pluginsFloatLatestUidSet = BuildPluginsFloatLatestPackageUidSet(catalog);
                HashSet<string> latestUids = _pluginsFloatLatestUidSet;
                if (latestUids != null && latestUids.Count > 0)
                {
                    for (int i = children.Count - 1; i >= 0; i--)
                    {
                        if (!PluginsFloatEntryPassesLatestFilter(children[i], latestUids))
                            children.RemoveAt(i);
                    }
                }
            }

            root.Children = children;
            root.ChildrenLoaded = true;
            int maxR = PluginsFloatGetRating(root.Entry);
            for (int c = 0; c < root.Children.Count; c++)
            {
                int cr = PluginsFloatGetRating(root.Children[c]);
                if (cr > maxR) maxR = cr;
            }
            root.MaxRating = maxR;
        }
    }
}
