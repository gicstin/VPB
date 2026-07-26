using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class SceneLoadingUtils
    {
        static int sceneLoadSerial;
        static int lastScheduledSceneLoadSerial;

        public struct EnsureInstalledResult
        {
            public bool DepsChanged;
            public int ReferencedCount;
            public int MissingCount;

            public bool IsDegraded
            {
                get { return MissingCount > 0; }
            }
        }

        private static MethodInfo s_LoadMergeMethod;
        private static MethodInfo s_LoadInternalMethod;

        private static void EnsureLoadMethodsCached(SuperController sc)
        {
            if (sc == null) return;
            if (s_LoadMergeMethod != null && s_LoadInternalMethod != null) return;

            try
            {
                // Prefer public LoadMerge when present.
                if (s_LoadMergeMethod == null)
                {
                    s_LoadMergeMethod = sc.GetType().GetMethod("LoadMerge", BindingFlags.Instance | BindingFlags.Public);
                }
            }
            catch { }

            try
            {
                if (s_LoadInternalMethod == null)
                {
                    s_LoadInternalMethod = sc.GetType().GetMethod("LoadInternal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }
            catch { }
        }

        public static bool LoadScene(string normalizedPath, bool merge)
        {
            try
            {
                string referrerUid = PackageReferenceVersionResolver.TryExtractPackageUid(normalizedPath);
                if (!string.IsNullOrEmpty(referrerUid))
                    PackageReferenceVersionResolver.SetActiveLoadReferrer(referrerUid);
                else
                    PackageReferenceVersionResolver.ClearActiveLoadReferrer();

                if (string.IsNullOrEmpty(normalizedPath)) return false;
                SuperController sc = SuperController.singleton;
                if (sc == null) return false;

                EnsureLoadMethodsCached(sc);

                if (!merge)
                {
                    try { Gallery.CollapsePanelsOnSceneLaunch(); } catch { }
                    // Prefer direct public API.
                    sc.Load(normalizedPath);
                    return true;
                }

                // Merge load: prefer public LoadMerge when available, otherwise fallback to LoadInternal.
                if (s_LoadMergeMethod != null)
                {
                    s_LoadMergeMethod.Invoke(sc, new object[] { normalizedPath });
                    return true;
                }

                if (s_LoadInternalMethod != null)
                {
                    s_LoadInternalMethod.Invoke(sc, new object[] { normalizedPath, true, false });
                    return true;
                }

                // Last resort fallback (might not merge).
                sc.Load(normalizedPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetTempScenesDir()
        {
            return "Saves/scene/VPB_TempScenes";
        }

        private static void ScheduleTempFileDelete(string path, int frames = 10)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (SuperController.singleton == null) return;
                SuperController.singleton.StartCoroutine(DeleteFileAfterFrames(path, frames));
            }
            catch { }
        }

        private static IEnumerator DeleteFileAfterFrames(string path, int frames)
        {
            if (string.IsNullOrEmpty(path)) yield break;
            if (frames < 1) frames = 1;

            for (int i = 0; i < frames; i++) yield return new WaitForEndOfFrame();

            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Delete leftover gallery undo snapshot JSON under Saves/ (written for undo, deleted only when undo runs).
        /// Call once at process launch while undo stacks are empty.
        /// </summary>
        public static void CleanupOrphanUndoTempFiles()
        {
            string savesDir = null;
            try
            {
                if (SuperController.singleton != null)
                    savesDir = SuperController.singleton.savesDir;
            }
            catch { }
            if (string.IsNullOrEmpty(savesDir))
                savesDir = "Saves";

            if (!Directory.Exists(savesDir)) return;

            int deleted = 0;
            deleted += DeleteMatchingFiles(savesDir, "vpb_temp_undo_atom_*.json");
            deleted += DeleteMatchingFiles(savesDir, "vpb_temp_undo_redo_scene_*.json");
            if (deleted > 0)
            {
                try { LogUtil.Log("[VPB] Cleared " + deleted + " orphan undo temp file(s) from " + savesDir); }
                catch { }
            }
        }

        private static int DeleteMatchingFiles(string dir, string pattern)
        {
            string[] files = null;
            try { files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { return 0; }
            if (files == null || files.Length == 0) return 0;

            int deleted = 0;
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    File.Delete(files[i]);
                    deleted++;
                }
                catch { }
            }
            return deleted;
        }

        private static string WriteTempSceneJson(JSONNode root, string filePrefix)
        {
            try
            {
                if (root == null) return null;

                string dir = GetTempScenesDir();
                try { Directory.CreateDirectory(dir); } catch { }

                string name = (string.IsNullOrEmpty(filePrefix) ? "vpb_scene" : filePrefix) + "_" + Guid.NewGuid().ToString() + ".json";
                string tempPath = Path.Combine(dir, name);
                File.WriteAllText(tempPath, VPB.src.util.JsonSerializationUtil.Serialize(root, 100_000));
                LocalSceneGallerySupport.TryEnsureVpbGeneratedSceneHideMarker(tempPath);

                ScheduleTempFileDelete(tempPath, 20);
                ScheduleTempFileDelete(tempPath + ".hide", 20);
                return tempPath.Replace('\\', '/');
            }
            catch
            {
                return null;
            }
        }

        public static string WriteTempSceneForMergeLoad(JSONNode root, string prefix)
        {
            return WriteTempSceneJson(root, string.IsNullOrEmpty(prefix) ? "vpb_scene" : prefix);
        }

        public static string CreateFilteredSceneJSON(string path, FileEntry entry, Func<JSONNode, bool> atomFilter, bool ensureUniqueIds = false)
        {
            try
            {
                JSONNode root = UI.LoadJSONWithFallback(path, entry);
                if (root == null || root["atoms"] == null) return null;

                JSONArray atoms = root["atoms"].AsArray;
                JSONArray newAtoms = new JSONArray();

                Dictionary<string, string> idMapping = new Dictionary<string, string>();
                foreach (JSONNode atom in atoms)
                {
                    if (atomFilter(atom))
                    {
                        if (ensureUniqueIds)
                        {
                            string oldId = atom["id"].Value;
                            string newId = oldId;
                            if (SuperController.singleton != null && (SuperController.singleton.GetAtomByUid(newId) != null || idMapping.ContainsValue(newId)))
                            {
                                int count = 2;
                                while (SuperController.singleton.GetAtomByUid(newId + "#" + count) != null || idMapping.ContainsValue(newId + "#" + count))
                                {
                                    count++;
                                }
                                newId = newId + "#" + count;
                                atom["id"] = newId;
                                idMapping[oldId] = newId;
                            }
                        }

                        newAtoms.Add(atom);
                    }
                }

                if (newAtoms.Count == 0) return null;
                root["atoms"] = newAtoms;

                return WriteTempSceneJson(root, "vpb_filtered");
            }
            catch
            {
                return null;
            }
        }

        public static bool TryMergeLoadSceneNoPersons(string scenePath, FileEntry entry)
        {
            try
            {
                if (string.IsNullOrEmpty(scenePath)) return false;
                if (SuperController.singleton == null) return false;

                string tempPath = CreateFilteredSceneJSON(scenePath, entry, (atom) => atom != null && !SceneUtils.IsPersonLikeAtomType(atom["type"].Value), true);
                if (string.IsNullOrEmpty(tempPath)) return false;

                string loadPath = UI.NormalizePath(tempPath);

                return LoadScene(loadPath, true);
            }
            catch
            {
                return false;
            }
        }


        private static void RewriteCustomPathsRecursive(JSONNode node, List<string> unresolved, ref int replaced, string hostUid, string hostSceneDir, ICollection<string> sceneDeps)
        {
            if (node == null) return;

            if (node is JSONData jd)
            {
                string v = jd.Value;
                if (!string.IsNullOrEmpty(v))
                {
                    // Packaged scenes commonly reference their own assets via SELF:/ — once the scene
                    // is extracted to a loose temp file there is no host-package context, so SELF:/
                    // no longer resolves. Heal it to the concrete host package UID.
                    if (!string.IsNullOrEmpty(hostUid)
                        && v.Replace('\\', '/').StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase))
                    {
                        string rest = v.Replace('\\', '/').Substring("SELF:/".Length);
                        if (rest.StartsWith("/")) rest = rest.Substring(1);
                        jd.Value = hostUid + ":/" + rest;
                        replaced++;
                        return;
                    }

                    string candidate = v;
                    if (candidate.StartsWith("/")) candidate = candidate.Substring(1);
                    if (candidate.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase))
                    {
                        // For a packaged scene, its own assets live in the host package. Resolve against
                        // the known host package FIRST so the loose temp rewrite keeps a valid,
                        // fully-qualified reference even when the global internal-path index is
                        // incomplete (e.g. the package was scan-excluded and only registered on demand).
                        if (!string.IsNullOrEmpty(hostUid))
                        {
                            string hostCandidate = hostUid + ":/" + candidate;
                            try
                            {
                                if (VPB.FileManager.GetVarFileEntry(hostCandidate) != null)
                                {
                                    jd.Value = hostCandidate;
                                    replaced++;
                                    return;
                                }
                            }
                            catch { }
                        }

                        // A loose file on disk always wins over a VAR copy with the same internal path.
                        // The rewrite only heals references whose loose target is missing.
                        string loosePath = Path.Combine(Directory.GetCurrentDirectory(), candidate);
                        if (File.Exists(loosePath))
                        {
                            return;
                        }

                        // Prefer the scene's own declared dependencies over the global internal-path
                        // index. The index is first-writer-wins, so a bare reference whose internal
                        // path collides across multiple packages could otherwise resolve to an
                        // unrelated package. The scene's dependency packages are the intended source.
                        if (sceneDeps != null && sceneDeps.Count > 0)
                        {
                            foreach (string depUid in sceneDeps)
                            {
                                if (string.IsNullOrEmpty(depUid)) continue;
                                string depCandidate = depUid + ":/" + candidate;
                                try
                                {
                                    if (VPB.FileManager.GetVarFileEntry(depCandidate) != null)
                                    {
                                        jd.Value = depCandidate;
                                        replaced++;
                                        return;
                                    }
                                }
                                catch { }
                            }
                        }

                        if (VPB.FileManager.TryResolveCustomInternalPathToUidPath(candidate, out string uidPath) && !string.IsNullOrEmpty(uidPath))
                        {
                            jd.Value = uidPath;
                            replaced++;
                        }
                        else
                        {
                            if (unresolved != null && unresolved.Count < 8) unresolved.Add(v);
                        }
                    }
                }
                return;
            }

            if (node is JSONArray ja)
            {
                for (int i = 0; i < ja.Count; i++)
                {
                    RewriteCustomPathsRecursive(ja[i], unresolved, ref replaced, hostUid, hostSceneDir, sceneDeps);
                }
                return;
            }

            if (node is JSONClass jc)
            {
                foreach (string k in jc.Keys)
                {
                    // SceneLoader resolves bare sibling paths against temp CurrentLoadDir; restore original package directory.
                    if (!string.IsNullOrEmpty(hostSceneDir)
                        && string.Equals(k, "sceneFilePath", StringComparison.OrdinalIgnoreCase)
                        && jc[k] is JSONData sfp)
                    {
                        string sv = sfp.Value;
                        if (!string.IsNullOrEmpty(sv)
                            && sv.IndexOf(":/", StringComparison.Ordinal) < 0
                            && sv.IndexOf('/') < 0
                            && sv.IndexOf('\\') < 0)
                        {
                            string qualified = hostSceneDir + "/" + sv;
                            try
                            {
                                if (VPB.FileManager.GetVarFileEntry(qualified) != null)
                                {
                                    sfp.Value = qualified;
                                    replaced++;
                                    continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                try { LogUtil.LogWarning("[VPB.SceneImport] failed to resolve sibling scene path '" + qualified + "': " + ex.Message); } catch { }
                            }
                        }
                    }

                    RewriteCustomPathsRecursive(jc[k], unresolved, ref replaced, hostUid, hostSceneDir, sceneDeps);
                }
                return;
            }
        }

        public static bool TryPrepareLocalSceneForLoad(FileEntry entry, out string loadPath)
        {
            loadPath = null;
            if (entry == null) return false;

            string uidOrPath = !string.IsNullOrEmpty(entry.Uid) ? entry.Uid : entry.Path;
            if (string.IsNullOrEmpty(uidOrPath)) return false;

            string p;
            try
            {
                p = UI.NormalizePath(uidOrPath);
            }
            catch
            {
                p = uidOrPath;
            }
            p = (p ?? "").Replace('\\', '/');
            if (!p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            JSONNode root;
            try
            {
                using (var reader = entry.OpenStreamReader())
                {
                    string content = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(content)) return false;
                    root = JSON.Parse(content);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] TryPrepareLocalSceneForLoad: failed to read/parse scene {uidOrPath}: {ex.Message}");
                return false;
            }

            if (root == null) return false;

            // When the scene is loaded out of a .var, its own assets resolve against the host
            // package. Extract that UID (Author.Name[.ver]) so the rewrite can re-qualify the
            // scene's own SELF:/ and bare Custom/ references — the loose temp file has no package
            // context, so unqualified references would otherwise fail to load (e.g. assetbundles).
            string hostUid = null;
            try
            {
                string up = uidOrPath.Replace('\\', '/');
                int ci = up.IndexOf(":/");
                // ci > 1 excludes absolute Windows paths (E:/...); require a dot so it is a package UID.
                if (ci > 1 && up.Substring(0, ci).IndexOf('.') > 0)
                    hostUid = up.Substring(0, ci);
            }
            catch { hostUid = null; }

            // Package-qualified directory of the original scene, used to re-qualify bare sibling scene paths (SceneLoader triggers) that VaM would otherwise resolve into the temp dir.
            string hostSceneDir = null;
            if (!string.IsNullOrEmpty(hostUid))
            {
                string up2 = uidOrPath.Replace('\\', '/');
                int ls = up2.LastIndexOf('/');
                if (ls > 0) hostSceneDir = up2.Substring(0, ls);
            }

            // Collect the scene's de-facto dependency packages from its fully-qualified
            // (Author.Name.version) references. Bare Custom/ paths are resolved against these
            // before the global first-writer-wins index, so a reference whose internal path
            // collides across packages resolves to the package the scene actually depends on.
            var sceneDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                DependencyExtractor.ScanAllStringsForDependencies(root, sceneDeps);
                if (!string.IsNullOrEmpty(hostUid)) sceneDeps.Remove(hostUid);
            }
            catch { }

            int replaced = 0;
            var unresolved = new List<string>();
            try
            {
                RewriteCustomPathsRecursive(root, unresolved, ref replaced, hostUid, hostSceneDir, sceneDeps);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] TryPrepareLocalSceneForLoad: rewrite failed for {uidOrPath}: {ex.Message}");
            }

            if (replaced == 0)
            {
                return false;
            }

            string outPath = WriteTempSceneJson(root, "vpb_rewrite");
            if (string.IsNullOrEmpty(outPath)) return false;
            loadPath = outPath;

            if (unresolved.Count > 0)
            {
                LogUtil.LogWarning($"[VPB] Scene rewrite: replaced {replaced} Custom paths, unresolved sample: {string.Join(", ", unresolved.ToArray())}");
            }
            else
            {
                LogUtil.Log($"[VPB] Scene rewrite: replaced {replaced} Custom paths");
            }

            return true;
        }

        public static bool EnsureInstalled(FileEntry entry)
        {
            return EnsureInstalled(entry, null);
        }

        /// <param name="outMovedPackageUids">When non-null, receives UIDs for packages whose .var was moved during this call.</param>
        public static bool EnsureInstalled(FileEntry entry, List<string> outMovedPackageUids)
        {
            EnsureInstalledResult result = EnsureInstalledDetailed(entry, outMovedPackageUids);
            return result.DepsChanged;
        }

        public static EnsureInstalledResult EnsureInstalledDetailed(FileEntry entry, List<string> outMovedPackageUids)
        {
            EnsureInstalledResult result = default(EnsureInstalledResult);
            if (entry == null) return result;

            try
            {
                bool flag = false;
                if (entry is VarFileEntry varEntry && varEntry.Package != null)
                {
                    flag = outMovedPackageUids != null
                        ? varEntry.Package.InstallRecursive(outMovedPackageUids)
                        : varEntry.Package.InstallRecursive();
                }
                else if (entry is SystemFileEntry sysEntry && sysEntry.package != null)
                {
                    flag = outMovedPackageUids != null
                        ? sysEntry.package.InstallRecursive(outMovedPackageUids)
                        : sysEntry.package.InstallRecursive();
                }

                // Scan for internal dependencies if it's a JSON-like file
                if (!string.IsNullOrEmpty(entry.Path))
                {
                    string ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                    if (ext == ".json" || ext == ".vap" || ext == ".cslist")
                    {
                        using (var reader = entry.OpenStreamReader())
                        {
                            string content = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(content))
                            {
                                HashSet<string> deps = null;
                                try
                                {
                                    deps = VarNameParser.Parse(content);
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogWarning($"[VPB] EnsureInstalled: dependency parse failed for {entry.Path}: {ex.Message}");
                                }

                                if (deps != null)
                                {
                                    try
                                    {
                                        int depCount = deps.Count;
                                        result.ReferencedCount = depCount;
                                        if (depCount > 0)
                                        {
                                            string sample = string.Join(", ", deps.Take(5).ToArray());
                                            LogUtil.Log($"[VPB] EnsureInstalled: Parsed {depCount} package refs from {entry.Name}. Sample: {sample}");
                                        }

                                        int missing = 0;
                                        List<string> missingKeys = null;
                                        foreach (string key in deps)
                                        {
                                            VarPackage pkg = FileManager.GetPackageForDependency(key, false);
                                            if (pkg != null) continue;
                                            missing++;
                                            if (missingKeys == null) missingKeys = new List<string>(8);
                                            missingKeys.Add(key);
                                        }
                                        if (missing > 0)
                                        {
                                            // Listing only the missing keys (not all parsed deps) so the warning
                                            // line is actionable: each entry is one package the user needs.
                                            string list = missingKeys != null ? string.Join("; ", missingKeys.ToArray()) : "";
                                            LogUtil.LogWarning($"[VPB] EnsureInstalled: Missing {missing}/{deps.Count} referenced packages for {entry.Name}: {list}");
                                        }
                                        result.MissingCount = missing;
                                    }
                                    catch { }

                                    bool depsChanged = FileButton.EnsureInstalledBySet(deps, outMovedPackageUids);
                                    if (depsChanged) flag = true;
                                }
                            }
                        }
                    }
                }

                result.DepsChanged = flag;
                return result;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                return result;
            }
        }

        /// <summary>
        /// Time-sliced variant of <see cref="EnsureInstalledDetailed"/> for gallery scene-load coroutines.
        /// Dependency JSON parse runs on a thread-pool worker; installs stay on the main thread.
        /// </summary>
        public static IEnumerator EnsureInstalledDetailedCoroutine(FileEntry entry, List<string> outMovedPackageUids, Action<EnsureInstalledResult> onComplete)
        {
            EnsureInstalledResult result = default(EnsureInstalledResult);
            if (entry == null)
            {
                if (onComplete != null) onComplete(result);
                yield break;
            }

            bool flag = false;
            bool failed = false;
            try
            {
                if (entry is VarFileEntry varEntry && varEntry.Package != null)
                {
                    flag = outMovedPackageUids != null
                        ? varEntry.Package.InstallRecursive(outMovedPackageUids)
                        : varEntry.Package.InstallRecursive();
                }
                else if (entry is SystemFileEntry sysEntry && sysEntry.package != null)
                {
                    flag = outMovedPackageUids != null
                        ? sysEntry.package.InstallRecursive(outMovedPackageUids)
                        : sysEntry.package.InstallRecursive();
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                if (onComplete != null) onComplete(result);
                failed = true;
            }

            if (failed)
                yield break;

            yield return null;

            if (!string.IsNullOrEmpty(entry.Path))
            {
                string ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (ext == ".json" || ext == ".vap" || ext == ".cslist")
                {
                    string content = null;
                    try
                    {
                        using (var reader = entry.OpenStreamReader())
                        {
                            content = reader.ReadToEnd();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                        if (onComplete != null) onComplete(result);
                        failed = true;
                    }

                    if (failed)
                        yield break;

                    yield return null;

                    if (!string.IsNullOrEmpty(content))
                    {
                        HashSet<string> deps = null;
                        Exception parseEx = null;
                        int parseDone = 0;
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try { deps = VarNameParser.Parse(content); }
                            catch (Exception ex) { parseEx = ex; }
                            finally { Interlocked.Exchange(ref parseDone, 1); }
                        });
                        while (Interlocked.CompareExchange(ref parseDone, 0, 0) == 0)
                            yield return null;

                        if (parseEx != null)
                            LogUtil.LogWarning($"[VPB] EnsureInstalled: dependency parse failed for {entry.Path}: {parseEx.Message}");

                        if (deps != null)
                        {
                            PopulateEnsureInstalledMissingCounts(entry, deps, ref result);

                            bool depsChanged = false;
                            yield return FileButton.EnsureInstalledBySetCoroutine(deps, outMovedPackageUids, 4, changed => depsChanged = changed);
                            if (depsChanged) flag = true;
                        }
                    }
                }
            }

            result.DepsChanged = flag;
            if (onComplete != null) onComplete(result);
        }

        static void PopulateEnsureInstalledMissingCounts(FileEntry entry, HashSet<string> deps, ref EnsureInstalledResult result)
        {
            try
            {
                int depCount = deps.Count;
                result.ReferencedCount = depCount;
                if (depCount > 0)
                {
                    string sample = string.Join(", ", deps.Take(5).ToArray());
                    LogUtil.Log($"[VPB] EnsureInstalled: Parsed {depCount} package refs from {entry.Name}. Sample: {sample}");
                }

                int missing = 0;
                List<string> missingKeys = null;
                foreach (string key in deps)
                {
                    VarPackage pkg = FileManager.GetPackageForDependency(key, false);
                    if (pkg != null) continue;
                    missing++;
                    if (missingKeys == null) missingKeys = new List<string>(8);
                    missingKeys.Add(key);
                }
                if (missing > 0)
                {
                    string list = missingKeys != null ? string.Join("; ", missingKeys.ToArray()) : "";
                    LogUtil.LogWarning($"[VPB] EnsureInstalled: Missing {missing}/{deps.Count} referenced packages for {entry.Name}: {list}");
                }
                result.MissingCount = missing;
            }
            catch { }
        }

        /// <summary>
        /// Collects package UIDs needed by this entry's load path:
        /// host package UID (when entry is from a var) plus dependency references in JSON-like content
        /// that can be resolved to an installed package UID.
        /// </summary>
        public static HashSet<string> CollectReferencedPackageUids(FileEntry entry)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry == null) return result;

            try
            {
                if (entry is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                    result.Add(vfe.Package.Uid);
                else if (entry is SystemFileEntry sfe && sfe.package != null && !string.IsNullOrEmpty(sfe.package.Uid))
                    result.Add(sfe.package.Uid);
                else if (entry is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                    result.Add(ple.Package.Uid);
            }
            catch { }

            try
            {
                string path = entry.Path ?? "";
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".json" && ext != ".vap" && ext != ".cslist")
                    return result;

                using (var reader = entry.OpenStreamReader())
                {
                    string content = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(content)) return result;

                    HashSet<string> deps = null;
                    try { deps = VarNameParser.Parse(content); } catch { deps = null; }
                    if (deps == null || deps.Count == 0) return result;

                    foreach (string dep in deps)
                    {
                        if (string.IsNullOrEmpty(dep)) continue;
                        VarPackage pkg = null;
                        try { pkg = FileManager.GetPackageForDependency(dep, false); } catch { pkg = null; }
                        if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) continue;
                        result.Add(pkg.Uid);
                        // A scene stores an item's full path but references its package group by
                        // .latest/version. When a newer version renamed or replaced that file (e.g.
                        // group ships "Side Bob" in v1, "Side Bob 2" in v2), VaM's appearance restore
                        // falls back to the item's internalId (DAZCharacterSelector.LoadFromJSON ->
                        // GetHairItem/GetClothingItem), which only resolves if the version that owns
                        // the referenced item is registered. Resolving to .latest alone starves that
                        // fallback under the scan whitelist. Register every installed version of the
                        // referenced group, matching stock VaM (which registers all versions on disk).
                        AddAllGroupVersionUids(pkg, result);
                    }
                }
            }
            catch { }

            return result;
        }

        private static void AddAllGroupVersionUids(VarPackage pkg, HashSet<string> result)
        {
            try
            {
                var versions = pkg?.Group?.Packages;
                if (versions == null) return;
                for (int i = 0; i < versions.Count; i++)
                {
                    var v = versions[i];
                    if (v != null && !string.IsNullOrEmpty(v.Uid))
                        result.Add(v.Uid);
                }
            }
            catch { }
        }

        /// <summary>
        /// Pre-register host/dependency packages in VaM's FileManager before a preset load pass.
        /// This avoids one-shot missing item failures where VaM does not retry lookups after an initial miss.
        /// Returns the number of unique UID candidates attempted.
        /// </summary>
        public static int PrewarmOnDemandPackagesForEntry(FileEntry entry, string pathHint = null, bool queueCoalescedRefresh = true)
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return 0;
            if (entry == null && string.IsNullOrEmpty(pathHint)) return 0;

            try
            {
                string hostUid = null;
                if (entry is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                    hostUid = vfe.Package.Uid;
                if (string.IsNullOrEmpty(hostUid))
                    hostUid = PackageReferenceVersionResolver.TryExtractPackageUid(pathHint);
                if (string.IsNullOrEmpty(hostUid) && entry != null)
                    hostUid = PackageReferenceVersionResolver.TryExtractPackageUid(entry.Path);
                if (!string.IsNullOrEmpty(hostUid))
                    PackageReferenceVersionResolver.SetActiveLoadReferrer(hostUid);
                else
                    PackageReferenceVersionResolver.ClearActiveLoadReferrer();
            }
            catch { }

            var uidCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void addUid(string uid)
            {
                if (string.IsNullOrEmpty(uid)) return;
                uid = uid.Trim();
                if (string.IsNullOrEmpty(uid)) return;
                uidCandidates.Add(uid);
            }

            try
            {
                if (entry != null)
                {
                    foreach (var uid in CollectReferencedPackageUids(entry))
                        addUid(uid);
                }
            }
            catch { }

            string candidatePath = pathHint;
            if (string.IsNullOrEmpty(candidatePath) && entry != null)
                candidatePath = entry.Path;
            if (!string.IsNullOrEmpty(candidatePath))
            {
                string normalized = UI.NormalizePath(candidatePath);
                if (UI.IsLikelyVarPackageReference(normalized))
                {
                    int colon = normalized.IndexOf(':');
                    if (colon > 0)
                    {
                        addUid(normalized.Substring(0, colon));
                    }
                }
            }

            // SQLite transitive dependency lookup — resolves full dep tree for the host package(s)
            // without requiring deps to already be registered in VaM's FileManager.
            if (uidCandidates.Count > 0)
            {
                var sqlDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hostUids = new List<string>(uidCandidates);
                foreach (string hostUid in hostUids)
                {
                    try
                    {
                        sqlDeps.Clear();
                        if (VpbLocalDatabase.TryReadRecursiveDependencyUids(hostUid, sqlDeps))
                        {
                            foreach (string dep in sqlDeps)
                                addUid(dep);
                        }
                    }
                    catch { }
                }
            }

            if (entry != null && !string.IsNullOrEmpty(entry.Path))
            {
                string ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (ext == ".json" || ext == ".vap" || ext == ".cslist")
                {
                    try
                    {
                        using (var reader = entry.OpenStreamReader())
                        {
                            string content = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(content))
                            {
                                HashSet<string> deps = VarNameParser.Parse(content);
                                if (deps != null)
                                {
                                    foreach (string dep in deps)
                                    {
                                        addUid(dep);
                                        try
                                        {
                                            VarPackage pkg = FileManager.GetPackageForDependency(dep, false);
                                            if (pkg != null && !string.IsNullOrEmpty(pkg.Uid))
                                                addUid(pkg.Uid);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[VPB OnDemand] Prewarm dependency parse failed for {entry.Name}: {ex.Message}");
                    }
                }
            }

            if (uidCandidates.Count == 0) return 0;

            int newlyRegistered = 0;
            foreach (string uid in uidCandidates)
            {
                try
                {
                    string result = VamOnDemandLoader.TryRegisterPackageOnDemand(uid);
                    if (result != null) newlyRegistered++;
                }
                catch { }
            }

            try
            {
                string sample = string.Join(", ", uidCandidates.Take(5).ToArray());
                LogUtil.Log($"[VPB OnDemand] Prewarm attempted {uidCandidates.Count} package(s) ({newlyRegistered} new) for {(entry != null ? entry.Name : candidatePath)}. Sample: {sample}");
            }
            catch { }

            // In whitelist mode, VaM's clothing catalog (geometry 'clothing:*' bool params) is only
            // populated during a full FileManager.Refresh(). Without this, on-demand registered packages
            // have their files accessible but their clothing items are invisible to VaM's clothing system,
            // causing 'Param not found' / 'Clothing item missing' errors.
            // This mirrors what EnsureInstalled + Refresh() does in non-whitelist mode.
            if (queueCoalescedRefresh
                && VamOnDemandLoader.ShouldRequestCoalescedNativeRefreshForUids(uidCandidates, newlyRegistered))
            {
                try
                {
                    LogUtil.Log($"[VPB OnDemand] Queueing coalesced FileManager.Refresh for clothing catalog update ({newlyRegistered} new package(s))");
                    VamOnDemandLoader.RequestCoalescedVamRefresh("scene_prewarm_clothing_catalog");
                }
                catch { }
            }

            return uidCandidates.Count;
        }

        // Scoped dependency prep for one import-sidebar slice: register only the slice's refs (plus the
        // source host package for SELF:) and their transitive deps, not the source scene's full closure.
        public static int PrewarmAndEnsureForPresetSlice(string sliceJson, string hostUid)
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return 0;
            if (string.IsNullOrEmpty(sliceJson)) return 0;

            HashSet<string> directDeps;
            try { directDeps = VarNameParser.Parse(sliceJson) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
            catch { directDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

            if (!string.IsNullOrEmpty(hostUid)) directDeps.Add(hostUid.Trim());
            if (directDeps.Count == 0) return 0;

            // Install only the slice's own refs. InstallRecursive on the source scene package re-walks
            // the whole closure, which is the multi-minute cost this scoped path exists to avoid.
            try { FileButton.EnsureInstalledBySet(directDeps); }
            catch (Exception ex) { LogUtil.LogWarning($"[VPB import] Slice EnsureInstalledBySet failed: {ex.Message}"); }

            var uidCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dep in directDeps)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                uidCandidates.Add(dep.Trim());
                try
                {
                    VarPackage pkg = FileManager.GetPackageForDependency(dep, false);
                    if (pkg != null && !string.IsNullOrEmpty(pkg.Uid)) uidCandidates.Add(pkg.Uid);
                }
                catch { }
            }

            // A clothing/morph package the slice names declares its own deps (textures, resource packs)
            // in meta.json that the scene JSON never mentions; pull them or the apply hits the one-shot
            // missing-item failure the prewarm prevents.
            foreach (string host in new List<string>(uidCandidates))
            {
                try
                {
                    var sqlDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (VpbLocalDatabase.TryReadRecursiveDependencyUids(host, sqlDeps))
                        foreach (string dep in sqlDeps) uidCandidates.Add(dep);
                }
                catch { }
            }

            int newlyRegistered = 0;
            foreach (string uid in uidCandidates)
            {
                try { if (VamOnDemandLoader.TryRegisterPackageOnDemand(uid) != null) newlyRegistered++; }
                catch { }
            }

            try
            {
                string sample = string.Join(", ", uidCandidates.Take(5).ToArray());
                LogUtil.Log($"[VPB import] Slice prewarm attempted {uidCandidates.Count} package(s) ({newlyRegistered} new). Sample: {sample}");
            }
            catch { }

            // Same gate as the entry path: refresh VaM's clothing catalog only when the slice actually
            // registers clothing-bearing packages, so a morphs/plugins slice triggers no clothing sim work.
            if (VamOnDemandLoader.ShouldRequestCoalescedNativeRefreshForUids(uidCandidates, newlyRegistered))
            {
                try { VamOnDemandLoader.RequestCoalescedVamRefresh("vpb_import_slice_prewarm"); }
                catch { }
            }

            return uidCandidates.Count;
        }

        /// <summary>
        /// Copies the preset file's host .var from AllPackages to AddonPackages (if applicable) without scanning file contents for dependency VARs.
        /// Used for appearance "clothes only", where dependency install runs on garment-filtered JSON only (not the full .vap text).
        /// </summary>
        public static bool InstallHostPackageRecursive(FileEntry entry)
        {
            return InstallHostPackageRecursive(entry, null);
        }

        public static bool InstallHostPackageRecursive(FileEntry entry, List<string> outMovedPackageUids)
        {
            if (entry == null) return false;
            try
            {
                if (entry is VarFileEntry varEntry && varEntry.Package != null)
                    return outMovedPackageUids != null
                        ? varEntry.Package.InstallRecursive(outMovedPackageUids)
                        : varEntry.Package.InstallRecursive();
                if (entry is SystemFileEntry sysEntry && sysEntry.package != null)
                    return outMovedPackageUids != null
                        ? sysEntry.package.InstallRecursive(outMovedPackageUids)
                        : sysEntry.package.InstallRecursive();
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] InstallHostPackageRecursive error: {ex.Message}");
            }
            return false;
        }

        public static bool EnsureInstalled(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            
            FileEntry entry = FileManager.GetFileEntry(path);
            if (entry != null)
            {
                return EnsureInstalled(entry);
            }
            return false;
        }

        public static void NotifySceneLoadStarting(string saveName, bool loadMerge)
        {
            try
            {
                if (!loadMerge)
                {
                    unchecked { sceneLoadSerial++; }
                }
            }
            catch { }
        }

        public static void SchedulePostSceneLoadFixup()
        {
            try
            {
                int serial = sceneLoadSerial;
                if (serial == lastScheduledSceneLoadSerial) return;
                lastScheduledSceneLoadSerial = serial;

                if (SuperController.singleton != null)
                {
                    SuperController.singleton.StartCoroutine(PostSceneLoadFixupCoroutine(serial));
                }
            }
            catch { }
        }

        public static void SchedulePostPersonApplyFixup(Atom atom, List<KeyValuePair<JSONStorable, JSONClass>> lateRestoreTargets = null)
        {
            if (atom == null) return;
            if (SuperController.singleton == null) return;
            if (!SceneUtils.IsPersonLikeAtom(atom)) return;

            try
            {
                SuperController.singleton.StartCoroutine(PostPersonApplyFixupCoroutine(atom, lateRestoreTargets));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] SchedulePostPersonApplyFixup error: " + ex.Message);
            }
        }

        static IEnumerator PostPersonApplyFixupCoroutine(Atom atom, List<KeyValuePair<JSONStorable, JSONClass>> lateRestoreTargets)
        {
            yield return new WaitForEndOfFrame();

            if (atom == null) yield break;
            if (!SceneUtils.IsPersonLikeAtom(atom)) yield break;

            if (lateRestoreTargets != null)
            {
                for (int i = 0; i < lateRestoreTargets.Count; i++)
                {
                    try
                    {
                        var kvp = lateRestoreTargets[i];
                        if (kvp.Key != null && kvp.Value != null)
                        {
                            kvp.Key.LateRestoreFromJSON(kvp.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] LateRestoreFromJSON error: " + ex.Message);
                    }
                }
            }
        }

        static IEnumerator PostSceneLoadFixupCoroutine(int serial)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            if (serial != sceneLoadSerial) yield break;
        }

        /// <summary>
        /// After LoadInternal returns, atoms may still be spawning for a few frames — defer so the target list matches the new scene.
        /// </summary>
        public static void ScheduleGalleryTargetListRefresh()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return;
                sc.StartCoroutine(GalleryTargetListRefreshAfterSceneCoroutine());
            }
            catch { }
        }

        static IEnumerator GalleryTargetListRefreshAfterSceneCoroutine()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // LoadInternal has returned, but VPB may still set IsLoadingScene until WorldUI / idle completion.
            // GetAtoms() often does not yet list Person targets during that window — refreshing then leaves "None"
            // until some later UI pass (e.g. category change). Wait for the load flag to clear first.
            float loadWaitStart = Time.realtimeSinceStartup;
            while (VPBConfig.Instance != null && VPBConfig.Instance.IsLoadingScene
                   && (Time.realtimeSinceStartup - loadWaitStart) < 45f)
                yield return null;

            GalleryPanel.NotifyAllPanelsSceneTargetsChanged();

            // Person atoms can still register a few frames after loading ends; re-sync briefly.
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                GalleryPanel.NotifyAllPanelsSceneTargetsChanged();
            }
        }


    }
}
