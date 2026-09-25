using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using ICSharpCode.SharpZipLib.Zip;
using SimpleJSON;

namespace VPB
{
    internal sealed class InsightScanItem
    {
        internal string Uid;
        internal string VarPath;
        internal string Creator;
        internal string PackageName;
        internal long Size;
        internal long MtimeTicks;
    }

    internal static class VpbPackageInsightScanner
    {
        internal const int ScanVersion = 1;

        private const int MaxJsonEntryBytes = 2 * 1024 * 1024;
        private const int MaxScriptEntryBytes = 1024 * 1024;
        private const int MaxTotalBytesPerPackage = 32 * 1024 * 1024;
        private const int MorphBloatThreshold = 150;
        private const int MaxDetailLines = 40;
        private const int MaxDetailLineChars = 240;
        private const int MaxUndeclaredDeps = 64;
        private const int PublishBatchSize = 128;
        private const int ThrottleSleepMs = 4;

        private static readonly object StateLock = new object();
        private static Thread _worker;
        private static volatile bool _cancelRequested;
        private static volatile bool _running;
        private static volatile int _processed;
        private static volatile int _total;
        private static volatile int _scannedThisRun;
        private static volatile string _currentLabel = "";

        internal static bool IsRunning { get { return _running; } }
        internal static int Processed { get { return _processed; } }
        internal static int Total { get { return _total; } }
        internal static int ScannedThisRun { get { return _scannedThisRun; } }
        internal static string CurrentLabel { get { return _currentLabel ?? ""; } }

        internal static float Progress
        {
            get
            {
                int t = _total;
                if (t <= 0) return 0f;
                float p = (float)_processed / t;
                return p < 0f ? 0f : (p > 1f ? 1f : p);
            }
        }

        internal static void RequestCancel()
        {
            _cancelRequested = true;
        }

        internal static void Shutdown()
        {
            _cancelRequested = true;
            Thread t;
            lock (StateLock) { t = _worker; }
            if (t == null) return;
            try { t.Join(2000); } catch { }
            lock (StateLock) { _worker = null; }
            _running = false;
        }

        internal static void LoadCachedIntoStore()
        {
            try
            {
                var map = new Dictionary<string, PackageInsightRecord>(StringComparer.OrdinalIgnoreCase);
                var reviews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (VpbLocalDatabase.TryLoadPackageInsights(map, reviews))
                {
                    VpbPackageInsightStore.SeedReviewed(reviews);
                    VpbPackageInsightStore.PublishAll(map);
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Insights] cached load failed: " + ex.Message); } catch { }
            }
        }

        internal static bool StartScan(HashSet<string> onlyUids, bool force)
        {
            lock (StateLock)
            {
                if (_running) return false;
                List<InsightScanItem> items = SnapshotPackagesOnMainThread(onlyUids);
                if (items == null || items.Count == 0)
                {
                    _currentLabel = "";
                    _total = 0;
                    _processed = 0;
                    return false;
                }

                _cancelRequested = false;
                _running = true;
                _processed = 0;
                _scannedThisRun = 0;
                _total = items.Count;
                _currentLabel = "";

                bool forceSnap = force;
                bool fullLibrary = onlyUids == null;
                var worker = new Thread(() => RunScan(items, forceSnap, fullLibrary));
                worker.IsBackground = true;
                try { worker.Priority = System.Threading.ThreadPriority.BelowNormal; } catch { }
                worker.Name = "VPB.InsightScan";
                _worker = worker;
                worker.Start();
                return true;
            }
        }

        private static List<InsightScanItem> SnapshotPackagesOnMainThread(HashSet<string> onlyUids)
        {
            Dictionary<string, VarPackage> byUid = null;
            try { byUid = FileManager.PackagesByUid; }
            catch { byUid = null; }
            if (byUid == null || byUid.Count == 0) return null;

            var list = new List<InsightScanItem>(onlyUids != null ? onlyUids.Count : byUid.Count);
            foreach (KeyValuePair<string, VarPackage> kv in byUid)
            {
                VarPackage pkg = kv.Value;
                if (pkg == null) continue;
                string uid = pkg.Uid ?? kv.Key ?? "";
                if (uid.Length == 0) continue;
                if (onlyUids != null && !onlyUids.Contains(uid)) continue;

                string path = null;
                try { path = pkg.Path; } catch { path = null; }
                if (string.IsNullOrEmpty(path)) continue;

                var item = new InsightScanItem
                {
                    Uid = uid,
                    VarPath = path,
                    Creator = SafeCreator(pkg),
                    PackageName = SafeName(pkg)
                };
                list.Add(item);
            }
            return list;
        }

        private static string SafeCreator(VarPackage pkg)
        {
            try { return pkg.Creator ?? ""; } catch { return ""; }
        }

        private static string SafeName(VarPackage pkg)
        {
            try { return pkg.Name ?? ""; } catch { return ""; }
        }

        private static void RunScan(List<InsightScanItem> items, bool force, bool fullLibrary)
        {
            var batch = new List<PackageInsightRecord>(PublishBatchSize);
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int scanned = 0;

            try
            {
                Dictionary<string, PackageInsightRecord> cached = null;
                if (!force)
                {
                    cached = new Dictionary<string, PackageInsightRecord>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < items.Count; i++)
                    {
                        PackageInsightRecord existing = VpbPackageInsightStore.Get(items[i].Uid);
                        if (existing != null) cached[items[i].Uid] = existing;
                    }
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (_cancelRequested) break;
                    InsightScanItem item = items[i];
                    live.Add(item.Uid);
                    _currentLabel = item.Uid;

                    try
                    {
                        var fi = new FileInfo(item.VarPath);
                        if (!fi.Exists)
                        {
                            _processed = i + 1;
                            continue;
                        }
                        item.Size = fi.Length;
                        item.MtimeTicks = fi.LastWriteTimeUtc.Ticks;
                    }
                    catch
                    {
                        _processed = i + 1;
                        continue;
                    }

                    if (!force && cached != null)
                    {
                        PackageInsightRecord prev;
                        if (cached.TryGetValue(item.Uid, out prev)
                            && prev != null
                            && prev.ScanVersion == ScanVersion
                            && prev.VarSize == item.Size
                            && prev.VarMtimeTicks == item.MtimeTicks)
                        {
                            _processed = i + 1;
                            continue;
                        }
                    }

                    PackageInsightRecord rec = ScanOne(item);
                    if (rec != null)
                    {
                        batch.Add(rec);
                        scanned++;
                        _scannedThisRun = scanned;
                    }

                    _processed = i + 1;

                    if (batch.Count >= PublishBatchSize)
                    {
                        FlushBatch(batch);
                        batch.Clear();
                    }

                    if (ThrottleSleepMs > 0) Thread.Sleep(ThrottleSleepMs);
                }

                if (batch.Count > 0) FlushBatch(batch);

                if (fullLibrary && !_cancelRequested && items.Count > 0)
                {
                    try { VpbPackageInsightStore.RemoveMissingPackages(live); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Insights] scan aborted: " + ex.Message); } catch { }
            }
            finally
            {
                _currentLabel = "";
                _running = false;
                lock (StateLock) { _worker = null; }
                try
                {
                    LogUtil.Log("[VPB.Insights] scan done processed=" + _processed
                        + " rescanned=" + scanned + " cancelled=" + _cancelRequested);
                }
                catch { }
            }
        }

        private static void FlushBatch(List<PackageInsightRecord> batch)
        {
            try { VpbLocalDatabase.TrySavePackageInsights(batch); } catch { }
            try { VpbPackageInsightStore.PublishBatch(batch); } catch { }
        }

        private static PackageInsightRecord ScanOne(InsightScanItem item)
        {
            var rec = new PackageInsightRecord
            {
                Uid = item.Uid,
                VarSize = item.Size,
                VarMtimeTicks = item.MtimeTicks,
                ScanVersion = ScanVersion
            };

            var details = new List<string>(8);
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localRefs = new List<string>(4);
            var foreignCreators = new List<string>(4);
            var entryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ZipFile zip = null;
            try
            {
                zip = OpenZipForScan(item.VarPath);
                if (zip == null)
                {
                    rec.Issues |= PkgIssueFlags.ScanFailed;
                    rec.Details = new string[0];
                    rec.UndeclaredDeps = new string[0];
                    return rec;
                }

                var jsonEntries = new List<ZipEntry>(64);
                var scriptEntries = new List<ZipEntry>(8);
                ZipEntry metaEntry = null;
                int morphs = 0;

                foreach (ZipEntry ze in zip)
                {
                    if (_cancelRequested) return null;
                    if (ze == null || !ze.IsFile) continue;
                    string name = (ze.Name ?? "").Replace('\\', '/');
                    if (name.Length == 0) continue;
                    entryPaths.Add(name);

                    if (string.Equals(name, "meta.json", StringComparison.OrdinalIgnoreCase))
                    {
                        metaEntry = ze;
                        continue;
                    }

                    if (EndsWith(name, ".vmi")) { morphs++; continue; }

                    if (EndsWith(name, ".cs") || EndsWith(name, ".cslist"))
                    {
                        rec.Risk |= PkgRiskFlags.HasScripts;
                        rec.ScriptCount++;
                        if (EndsWith(name, ".cs")) scriptEntries.Add(ze);
                        continue;
                    }
                    if (EndsWith(name, ".dll"))
                    {
                        rec.Risk |= PkgRiskFlags.HasDll;
                        rec.DllCount++;
                        AddDetail(details, "DLL: " + name);
                        continue;
                    }
                    if (EndsWith(name, ".assetbundle"))
                    {
                        rec.Risk |= PkgRiskFlags.HasAssetBundle;
                        rec.AssetBundleCount++;
                        continue;
                    }

                    if (IsDependencyBearingJson(name))
                        jsonEntries.Add(ze);
                }

                if (!HasCanonicalVarFileName(item.VarPath))
                    rec.Issues |= PkgIssueFlags.NameFormatBad;

                rec.MorphCount = morphs;
                if (morphs >= MorphBloatThreshold)
                {
                    rec.Issues |= PkgIssueFlags.MorphBloat;
                    AddDetail(details, string.Format(
                        VPBTranslation.T("insights.detail.morphs_fmt", "{0} morph files bundled"), morphs));
                }

                if (metaEntry == null)
                {
                    rec.Issues |= PkgIssueFlags.MetaMissing;
                }
                else
                {
                    string metaText = ReadEntryText(zip, metaEntry, MaxJsonEntryBytes);
                    if (metaText == null)
                    {
                        rec.Issues |= PkgIssueFlags.MetaUnparsable;
                    }
                    else
                    {
                        ParseMetaJson(metaText, item, rec, details, declared);
                        CollectDependencyRefs(metaText, found);
                    }
                }

                int budget = MaxTotalBytesPerPackage;

                for (int i = 0; i < jsonEntries.Count; i++)
                {
                    if (_cancelRequested) return null;
                    if (budget <= 0) break;
                    ZipEntry ze = jsonEntries[i];
                    long sz = 0;
                    try { sz = ze.Size; } catch { sz = 0; }
                    if (sz > MaxJsonEntryBytes) continue;

                    string text = ReadEntryText(zip, ze, MaxJsonEntryBytes);
                    if (text == null) continue;
                    budget -= text.Length;

                    CollectDependencyRefs(text, found);
                    CollectLocalRefs(text, entryPaths, localRefs);

                    string entryName = (ze.Name ?? "").Replace('\\', '/');
                    if (EndsWith(entryName, ".vam"))
                        CollectForeignCreator(text, item.Creator, foreignCreators);
                }

                for (int i = 0; i < scriptEntries.Count; i++)
                {
                    if (_cancelRequested) return null;
                    if (budget <= 0) break;
                    ZipEntry ze = scriptEntries[i];
                    long sz = 0;
                    try { sz = ze.Size; } catch { sz = 0; }
                    if (sz > MaxScriptEntryBytes) continue;

                    string text = ReadEntryText(zip, ze, MaxScriptEntryBytes);
                    if (text == null) continue;
                    budget -= text.Length;
                    ScanScriptText(text, (ze.Name ?? "").Replace('\\', '/'), rec, details);
                }
            }
            catch (Exception ex)
            {
                rec.Issues |= PkgIssueFlags.ScanFailed;
                AddDetail(details, ex.Message ?? ex.GetType().Name);
            }
            finally
            {
                if (zip != null)
                {
                    try { zip.Close(); } catch { }
                }
            }

            var undeclared = new List<string>(8);
            foreach (string dep in found)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                if (declared.Contains(dep)) continue;
                if (IsSameFamily(dep, item)) continue;
                if (DeclaredCoversFamily(declared, dep)) continue;
                undeclared.Add(dep);
                if (undeclared.Count >= MaxUndeclaredDeps) break;
            }
            undeclared.Sort(StringComparer.OrdinalIgnoreCase);

            if (undeclared.Count > 0)
            {
                rec.Issues |= PkgIssueFlags.UndeclaredDeps;
                AddDetail(details, string.Format(
                    VPBTranslation.T("insights.detail.undeclared_fmt", "{0} dependencies referenced but not declared in meta.json"),
                    undeclared.Count));
            }

            if (localRefs.Count > 0)
            {
                rec.Issues |= PkgIssueFlags.LocalReferences;
                for (int i = 0; i < localRefs.Count && i < 6; i++)
                    AddDetail(details, VPBTranslation.T("insights.detail.local_ref", "Local reference: ") + localRefs[i]);
            }

            if (foreignCreators.Count > 0)
            {
                rec.Issues |= PkgIssueFlags.ForeignAssets;
                var sb = new StringBuilder(96);
                sb.Append(VPBTranslation.T("insights.detail.foreign", "Bundled assets credited to: "));
                for (int i = 0; i < foreignCreators.Count && i < 6; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(foreignCreators[i]);
                }
                AddDetail(details, sb.ToString());
            }

            rec.UndeclaredDeps = undeclared.ToArray();
            rec.Details = details.ToArray();
            return rec;
        }

        private static ZipFile OpenZipForScan(string varPath)
        {
            try
            {
                FileStream fs = File.Open(varPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Write | FileShare.Delete);
                var zf = new ZipFile(fs);
                zf.IsStreamOwner = true;
                return zf;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadEntryText(ZipFile zip, ZipEntry entry, int maxBytes)
        {
            if (zip == null || entry == null) return null;
            try
            {
                long size = entry.Size;
                if (size > maxBytes) return null;
                int cap = size > 0 ? (int)size : maxBytes;
                if (cap > maxBytes) cap = maxBytes;

                using (Stream s = zip.GetInputStream(entry))
                {
                    var ms = new MemoryStream(cap > 0 ? cap : 4096);
                    byte[] buf = new byte[16 * 1024];
                    int total = 0;
                    while (true)
                    {
                        int n = s.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        total += n;
                        if (total > maxBytes) return null;
                        ms.Write(buf, 0, n);
                    }
                    byte[] bytes = ms.ToArray();
                    if (bytes.Length == 0) return "";
                    return DecodeUtf8(bytes);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string DecodeUtf8(byte[] bytes)
        {
            int offset = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) offset = 3;
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        private static void ParseMetaJson(
            string metaText, InsightScanItem item, PackageInsightRecord rec,
            List<string> details, HashSet<string> declared)
        {
            JSONClass root = null;
            try { root = JSON.Parse(metaText).AsObject; }
            catch { root = null; }

            if (root == null)
            {
                rec.Issues |= PkgIssueFlags.MetaUnparsable;
                return;
            }

            try
            {
                JSONClass deps = root["dependencies"].AsObject;
                if (deps != null)
                {
                    foreach (string key in deps.Keys)
                    {
                        if (!string.IsNullOrEmpty(key)) declared.Add(key);
                    }
                }
            }
            catch { }

            string metaCreator = SafeNodeValue(root, "creatorName");
            string metaPackage = SafeNodeValue(root, "packageName");

            if (IsTrueish(SafeNodeValue(root, "hadReferenceIssues")))
                rec.Issues |= PkgIssueFlags.ReferenceIssues;
            if (IsTrueish(SafeNodeValue(root, "preloadMorphs")))
                rec.Issues |= PkgIssueFlags.PreloadMorphs;

            if (!string.IsNullOrEmpty(metaCreator) && !string.IsNullOrEmpty(item.Creator)
                && !NamesEquivalent(metaCreator, item.Creator))
            {
                rec.Issues |= PkgIssueFlags.NameMismatch;
                AddDetail(details, string.Format(
                    VPBTranslation.T("insights.detail.creator_mismatch_fmt", "meta.json creator \"{0}\" ≠ file name creator \"{1}\""),
                    metaCreator, item.Creator));
            }
            if (!string.IsNullOrEmpty(metaPackage) && !string.IsNullOrEmpty(item.PackageName)
                && !NamesEquivalent(metaPackage, item.PackageName))
            {
                rec.Issues |= PkgIssueFlags.NameMismatch;
                AddDetail(details, string.Format(
                    VPBTranslation.T("insights.detail.package_mismatch_fmt", "meta.json package \"{0}\" ≠ file name package \"{1}\""),
                    metaPackage, item.PackageName));
            }
        }

        private static string SafeNodeValue(JSONClass root, string key)
        {
            try
            {
                JSONNode n = root[key];
                return n != null ? (n.Value ?? "") : "";
            }
            catch { return ""; }
        }

        private static bool IsTrueish(string v)
        {
            return !string.IsNullOrEmpty(v) && string.Equals(v.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool NamesEquivalent(string a, string b)
        {
            return string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Canonical(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '_' || c == '-') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static bool HasCanonicalVarFileName(string varPath)
        {
            string file = varPath;
            int slash = file.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0) file = file.Substring(slash + 1);
            if (!EndsWith(file, ".var")) return false;
            string stem = file.Substring(0, file.Length - 4);
            string[] parts = stem.Split('.');
            if (parts.Length < 3) return false;
            string last = parts[parts.Length - 1];
            if (last.Length == 0) return false;
            for (int i = 0; i < last.Length; i++)
            {
                if (last[i] < '0' || last[i] > '9') return false;
            }
            return parts[0].Length > 0 && parts[1].Length > 0;
        }

        private static bool IsDependencyBearingJson(string name)
        {
            return EndsWith(name, ".json")
                || EndsWith(name, ".vap")
                || EndsWith(name, ".vaj")
                || EndsWith(name, ".vam")
                || EndsWith(name, ".vab")
                || EndsWith(name, ".clothingplugins");
        }

        private static bool EndsWith(string s, string suffix)
        {
            return s != null && s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectDependencyRefs(string text, HashSet<string> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            int idx = 0;
            while (true)
            {
                int hit = text.IndexOf(":/", idx, StringComparison.Ordinal);
                if (hit < 0) break;
                idx = hit + 2;

                int start = hit;
                while (start > 0)
                {
                    char c = text[start - 1];
                    if (c == '"' || c == '\'' || c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == ',' || c == '[' || c == '{' || c == ':')
                        break;
                    start--;
                }
                if (start >= hit) continue;

                string token = text.Substring(start, hit - start);
                string normalized = NormalizeDependencyToken(token);
                if (normalized != null) into.Add(normalized);
            }
        }

        internal static string NormalizeDependencyToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            string t = token.Trim();
            if (t.Length < 5) return null;
            if (string.Equals(t, "SELF", StringComparison.OrdinalIgnoreCase)) return null;
            if (t.IndexOf('/') >= 0 || t.IndexOf('\\') >= 0) return null;

            int lastDot = t.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == t.Length - 1) return null;
            string version = t.Substring(lastDot + 1);
            string head = t.Substring(0, lastDot);
            if (head.IndexOf('.') <= 0) return null;

            if (!IsVersionSegment(version)) return null;
            return t;
        }

        private static bool IsVersionSegment(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            if (string.Equals(v, "latest", StringComparison.OrdinalIgnoreCase)) return true;
            int start = 0;
            if (v.Length > 3 && v.StartsWith("min", StringComparison.OrdinalIgnoreCase)) start = 3;
            if (start >= v.Length) return false;
            for (int i = start; i < v.Length; i++)
            {
                if (v[i] < '0' || v[i] > '9') return false;
            }
            return true;
        }

        private static bool IsSameFamily(string dep, InsightScanItem item)
        {
            if (string.IsNullOrEmpty(dep) || item == null) return false;
            string family = FamilyOf(dep);
            if (family.Length == 0) return false;
            string own = (item.Creator ?? "") + "." + (item.PackageName ?? "");
            return string.Equals(Canonical(family), Canonical(own), StringComparison.OrdinalIgnoreCase);
        }

        private static bool DeclaredCoversFamily(HashSet<string> declared, string dep)
        {
            string family = FamilyOf(dep);
            if (family.Length == 0) return false;
            foreach (string d in declared)
            {
                if (string.Equals(FamilyOf(d), family, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string FamilyOf(string dep)
        {
            if (string.IsNullOrEmpty(dep)) return "";
            int lastDot = dep.LastIndexOf('.');
            return lastDot > 0 ? dep.Substring(0, lastDot) : dep;
        }

        private static void CollectLocalRefs(string text, HashSet<string> entryPaths, List<string> into)
        {
            if (string.IsNullOrEmpty(text) || into.Count >= 8) return;
            int idx = 0;
            while (into.Count < 8)
            {
                int hit = text.IndexOf("\"Custom/", idx, StringComparison.OrdinalIgnoreCase);
                if (hit < 0) break;
                int start = hit + 1;
                int end = text.IndexOf('"', start);
                if (end < 0) break;
                idx = end + 1;
                if (end - start > 400) continue;

                string path = text.Substring(start, end - start).Replace('\\', '/');
                if (path.Length == 0) continue;
                if (path.IndexOf(":/", StringComparison.Ordinal) >= 0) continue;
                if (entryPaths.Contains(path)) continue;
                if (ContainsOrdinalIgnoreCase(into, path)) continue;
                into.Add(path);
            }
        }

        private static void CollectForeignCreator(string vamText, string packageCreator, List<string> into)
        {
            if (string.IsNullOrEmpty(vamText) || into.Count >= 6) return;
            JSONClass root = null;
            try { root = JSON.Parse(vamText).AsObject; }
            catch { root = null; }
            if (root == null) return;

            string creator = SafeNodeValue(root, "creatorName");
            if (string.IsNullOrEmpty(creator)) return;
            if (NamesEquivalent(creator, packageCreator)) return;
            if (ContainsOrdinalIgnoreCase(into, creator)) return;
            into.Add(creator.Trim());
        }

        private static void ScanScriptText(string text, string entryName, PackageInsightRecord rec, List<string> details)
        {
            if (string.IsNullOrEmpty(text)) return;

            int ls, le;
            string hit = VpbInsightKeywords.High.FindFirst(text, out ls, out le);
            if (hit != null)
            {
                rec.Risk |= PkgRiskFlags.FlaggedHigh;
                AddDetail(details, entryName + "  →  " + hit + "   " + Quote(text, ls, le));
                return;
            }

            hit = VpbInsightKeywords.Low.FindFirst(text, out ls, out le);
            if (hit != null)
            {
                rec.Risk |= PkgRiskFlags.FlaggedLow;
                AddDetail(details, entryName + "  →  " + hit + "   " + Quote(text, ls, le));
            }
        }

        private static string Quote(string text, int start, int end)
        {
            if (start < 0 || end <= start || end > text.Length) return "";
            string line = text.Substring(start, end - start).Trim();
            if (line.Length > 120) line = line.Substring(0, 117) + "…";
            return line;
        }

        private static void AddDetail(List<string> details, string line)
        {
            if (details == null || string.IsNullOrEmpty(line)) return;
            if (details.Count >= MaxDetailLines) return;
            string s = line.Replace('\n', ' ').Replace('\r', ' ');
            if (s.Length > MaxDetailLineChars) s = s.Substring(0, MaxDetailLineChars - 1) + "…";
            details.Add(s);
        }

        private static bool ContainsOrdinalIgnoreCase(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
