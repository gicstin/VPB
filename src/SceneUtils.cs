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

        // Gallery PrepareSceneEntry already applied temp WL + prewarm; LoadInternal must not redo.
        // Armed-flag (not path match): gallery may rewrite SELF→temp JSON so LoadInternal path ≠ note path.
        static bool s_GalleryScenePrepArmed;
        static float s_GalleryScenePrepRealtime;
        const float GalleryScenePrepSkipSeconds = 45f;
        const string PluginScriptPathMarker = ":/Custom/Scripts/";

        // Temp UID overrides owned by native (non-gallery) scene loads — removed after scene total.
        static readonly object s_NativeScenePrepLock = new object();
        static List<string> s_NativeSceneTempUids;
        static int s_NativeSceneCleanupSerial = -1;
        static bool s_NativeSceneCleanupRunning;

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

        private static readonly WaitForEndOfFrame s_WaitEndOfFrame = new WaitForEndOfFrame();

        // Pending rewrite/filter temps: one coordinator, watches VaM isLoading + VPB scene-load flag.
        // Avoids per-file coroutines + per-frame yield for up to 180s (old DeleteTempSceneAfterLoadSettles).
        static readonly object s_PendingTempSceneLock = new object();
        static readonly List<string> s_PendingTempSceneDeletes = new List<string>(8);
        static bool s_TempSceneDeleteCoordinatorRunning;
        const float TempSceneDeleteMinAliveSeconds = 5f;
        const float TempSceneDeleteFallbackSeconds = 180f;
        const int TempSceneDeleteSettleFrames = 30;
        const int TempSceneDeletePollFrames = 15; // ~0.25–0.5s at 30–60fps; reuse WaitForEndOfFrame
        const int TempScenePendingCap = 64;

        /// <summary>
        /// Called from <see cref="LogUtil.EndSceneLoadTotal"/> — restart coordinator if pending and idle.
        /// </summary>
        public static void NotifySceneLoadTotalEndedForTempScenes()
        {
            try
            {
                if (SuperController.singleton == null) return;
                lock (s_PendingTempSceneLock)
                {
                    if (s_PendingTempSceneDeletes.Count == 0) return;
                    if (s_TempSceneDeleteCoordinatorRunning) return;
                    s_TempSceneDeleteCoordinatorRunning = true;
                }
                SuperController.singleton.StartCoroutine(TempSceneDeleteCoordinator());
            }
            catch
            {
                lock (s_PendingTempSceneLock) { s_TempSceneDeleteCoordinatorRunning = false; }
            }
        }

        private static void ScheduleTempSceneFileDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                bool start = false;
                lock (s_PendingTempSceneLock)
                {
                    if (s_PendingTempSceneDeletes.Count >= TempScenePendingCap)
                        s_PendingTempSceneDeletes.RemoveAt(0);
                    for (int i = 0; i < s_PendingTempSceneDeletes.Count; i++)
                    {
                        if (string.Equals(s_PendingTempSceneDeletes[i], path, StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                    s_PendingTempSceneDeletes.Add(path);
                    if (!s_TempSceneDeleteCoordinatorRunning)
                    {
                        s_TempSceneDeleteCoordinatorRunning = true;
                        start = true;
                    }
                }

                if (!start) return;
                if (SuperController.singleton == null)
                {
                    lock (s_PendingTempSceneLock) { s_TempSceneDeleteCoordinatorRunning = false; }
                    return;
                }
                SuperController.singleton.StartCoroutine(TempSceneDeleteCoordinator());
            }
            catch
            {
                lock (s_PendingTempSceneLock) { s_TempSceneDeleteCoordinatorRunning = false; }
            }
        }

        private static IEnumerator TempSceneDeleteCoordinator()
        {
            float start = 0f;
            try { start = Time.realtimeSinceStartup; } catch { }

            try
            {
                for (; ; )
                {
                    bool loading = false;
                    try
                    {
                        if (SuperController.singleton != null)
                            loading = SuperController.singleton.isLoading;
                    }
                    catch { loading = false; }

                    // VPB total still active ⇒ plugins may LateRestore after VaM isLoading clears.
                    bool vpbLoadActive = false;
                    try { vpbLoadActive = LogUtil.IsSceneLoadActive(); } catch { }

                    float elapsed = 0f;
                    try { elapsed = Time.realtimeSinceStartup - start; } catch { elapsed = TempSceneDeleteFallbackSeconds; }

                    if (!loading && !vpbLoadActive && elapsed >= TempSceneDeleteMinAliveSeconds)
                        break;
                    if (elapsed >= TempSceneDeleteFallbackSeconds)
                        break;

                    for (int i = 0; i < TempSceneDeletePollFrames; i++)
                        yield return s_WaitEndOfFrame;
                }

                for (int i = 0; i < TempSceneDeleteSettleFrames; i++)
                    yield return s_WaitEndOfFrame;

                List<string> toDelete = null;
                lock (s_PendingTempSceneLock)
                {
                    if (s_PendingTempSceneDeletes.Count > 0)
                    {
                        toDelete = new List<string>(s_PendingTempSceneDeletes);
                        s_PendingTempSceneDeletes.Clear();
                    }
                }

                if (toDelete != null)
                {
                    for (int i = 0; i < toDelete.Count; i++)
                    {
                        string p = toDelete[i];
                        if (string.IsNullOrEmpty(p)) continue;
                        try { if (File.Exists(p)) File.Delete(p); } catch { }
                    }
                }
            }
            finally
            {
                bool restart = false;
                lock (s_PendingTempSceneLock)
                {
                    s_TempSceneDeleteCoordinatorRunning = false;
                    if (s_PendingTempSceneDeletes.Count > 0)
                    {
                        s_TempSceneDeleteCoordinatorRunning = true;
                        restart = true;
                    }
                }
                if (restart && SuperController.singleton != null)
                {
                    try { SuperController.singleton.StartCoroutine(TempSceneDeleteCoordinator()); }
                    catch { lock (s_PendingTempSceneLock) { s_TempSceneDeleteCoordinatorRunning = false; } }
                }
            }
        }

        /// <summary>Cold startup: wipe leftover rewrite temps from prior crashed loads.</summary>
        public static void CleanupOrphanTempSceneFiles()
        {
            string dir = GetTempScenesDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            int deleted = 0;
            deleted += DeleteMatchingFiles(dir, "vpb_rewrite_*.json");
            deleted += DeleteMatchingFiles(dir, "vpb_rewrite_*.json.hide");
            deleted += DeleteMatchingFiles(dir, "vpb_filtered_*.json");
            deleted += DeleteMatchingFiles(dir, "vpb_filtered_*.json.hide");
            deleted += DeleteMatchingFiles(dir, "vpb_scene_*.json");
            deleted += DeleteMatchingFiles(dir, "vpb_scene_*.json.hide");
            if (deleted > 0)
            {
                try { LogUtil.Log("[VPB] Cleared " + deleted + " orphan temp scene file(s) from " + dir); }
                catch { }
            }
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

                // Must outlive scene load — not DeleteFileAfterFrames(20).
                ScheduleTempSceneFileDelete(tempPath);
                ScheduleTempSceneFileDelete(tempPath + ".hide");
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

            // Package-backed scenes: load uid:/path directly. Writing a loose temp JSON clears
            // FileManager.CurrentPackageUid (SetLoadDirFromFilePath on Saves/scene/VPB_TempScenes/...),
            // which breaks PoseMe/BodyLanguage GetFiles("Custom/...") and audiobundle opens that
            // rely on package context / merged var FS. Bare Custom/ at access time is healed by
            // SuperControllerHook path rewrite + GetFiles package enumeration instead.
            // Any package-qualified UID path skips rewrite — not only VarFileEntry (gallery may
            // pass SystemFileEntry.isVar with the same uid:/ scene path).
            if (!string.IsNullOrEmpty(hostUid))
            {
                LogUtil.Log("[VPB] Scene rewrite skipped for package scene (keep load context): " + hostUid);
                return false;
            }

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
        /// host package UID (when entry is from a var) plus dependency references in JSON-like content.
        /// Always keeps raw VarNameParser UIDs even when VPB FM cannot resolve them yet — scan-whitelist
        /// temp allow + on-demand register need those strings (e.g. CheesyFX.BodyLanguage for PoseMe HUD).
        /// </summary>
        public static HashSet<string> CollectReferencedPackageUids(FileEntry entry)
        {
            return CollectReferencedPackageUids(entry, null);
        }

        /// <param name="pluginHostUids">Optional sink for packages referenced via :/Custom/Scripts/.</param>
        public static HashSet<string> CollectReferencedPackageUids(FileEntry entry, HashSet<string> pluginHostUids)
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
                    CollectPackageUidsFromContent(content, result, pluginHostUids);
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// One-pass scan of scene/preset text: raw package UIDs + optional plugin-host UIDs.
        /// Avoids a second full ReadToEnd on large scenes (Enjoying Joy ~6MB).
        /// </summary>
        static void CollectPackageUidsFromContent(string content, HashSet<string> result, HashSet<string> pluginHostUids)
        {
            if (string.IsNullOrEmpty(content) || result == null) return;

            try { VarNameParser.Parse(content, result); }
            catch { }

            if (pluginHostUids != null)
            {
                try { CollectPluginHostUidsFromContent(content, pluginHostUids); }
                catch { }
            }

            if (result.Count == 0) return;

            // Snapshot — may mutate result while expanding resolved groups.
            var snapshot = new List<string>(result.Count);
            foreach (string dep in result)
            {
                if (!string.IsNullOrEmpty(dep)) snapshot.Add(dep);
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                string dep = snapshot[i];
                VarPackage pkg = null;
                try { pkg = FileManager.GetPackageForDependency(dep, false); } catch { pkg = null; }
                if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) continue;
                result.Add(pkg.Uid);
                // Register every installed version of the referenced group, matching stock VaM
                // (which registers all versions on disk). Needed when .latest resolves to a build
                // that renamed/replaced an item still referenced by internalId in scene JSON.
                AddAllGroupVersionUids(pkg, result);
            }
        }

        /// <summary>
        /// Packages that own plugin scripts in this JSON (BodyLanguage/PoseMe, Embody, …).
        /// Linear scan — no regex alloc.
        /// </summary>
        static void CollectPluginHostUidsFromContent(string content, HashSet<string> pluginHostUids)
        {
            if (string.IsNullOrEmpty(content) || pluginHostUids == null) return;

            int i = 0;
            while (i < content.Length)
            {
                int hit = content.IndexOf(PluginScriptPathMarker, i, StringComparison.OrdinalIgnoreCase);
                if (hit < 0) break;

                int start = hit - 1;
                while (start >= 0)
                {
                    char c = content[start];
                    if (c == '"' || c == '\'' || c == ' ' || c == '\t' || c == '\n' || c == '\r'
                        || c == ',' || c == '[' || c == '{' || c == ':' || c == '\\')
                    {
                        start++;
                        break;
                    }
                    start--;
                }
                if (start < 0) start = 0;

                int len = hit - start;
                if (len > 0 && len < 200)
                {
                    string uid = content.Substring(start, len);
                    if (uid.IndexOf('.') > 0)
                        pluginHostUids.Add(uid);
                }

                i = hit + PluginScriptPathMarker.Length;
            }
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
            return PrewarmOnDemandPackagesForEntry(entry, pathHint, queueCoalescedRefresh, null);
        }

        public static int PrewarmOnDemandPackagesForEntry(
            FileEntry entry,
            string pathHint,
            bool queueCoalescedRefresh,
            HashSet<string> pluginHostUids)
        {
            return PrewarmOnDemandPackagesForEntry(entry, pathHint, queueCoalescedRefresh, pluginHostUids, null);
        }

        /// <param name="precollectedUids">
        /// When non-null, skip re-reading scene JSON (caller already ran CollectReferencedPackageUids).
        /// </param>
        public static int PrewarmOnDemandPackagesForEntry(
            FileEntry entry,
            string pathHint,
            bool queueCoalescedRefresh,
            HashSet<string> pluginHostUids,
            HashSet<string> precollectedUids)
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return 0;
            if (entry == null && string.IsNullOrEmpty(pathHint) && (precollectedUids == null || precollectedUids.Count == 0))
                return 0;

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
            // Own plugin-host set when caller did not collect (single scene ReadToEnd).
            HashSet<string> localPluginHosts = pluginHostUids;
            bool skipContentCollect = precollectedUids != null && precollectedUids.Count > 0;
            if (localPluginHosts == null && entry != null && !skipContentCollect)
                localPluginHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void addUid(string uid)
            {
                if (string.IsNullOrEmpty(uid)) return;
                uid = uid.Trim();
                if (string.IsNullOrEmpty(uid)) return;
                uidCandidates.Add(uid);
            }

            if (skipContentCollect)
            {
                foreach (string uid in precollectedUids)
                    addUid(uid);
            }
            else
            {
                try
                {
                    if (entry != null)
                    {
                        foreach (var uid in CollectReferencedPackageUids(entry, localPluginHosts))
                            addUid(uid);
                    }
                }
                catch { }
            }

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

            if (localPluginHosts != null)
            {
                foreach (string ph in localPluginHosts)
                    addUid(ph);
            }

            if (uidCandidates.Count == 0) return 0;

            int newlyRegistered = 0;
            foreach (string uid in uidCandidates)
            {
                try
                {
                    // Plugin hosts: persist UID override so LateRestore/script compile still finds the package
                    // after temp allow-list cleanup (PoseMe HUD lives under BodyLanguage).
                    bool isPluginHost = localPluginHosts != null && localPluginHosts.Contains(uid);
                    string result = VamOnDemandLoader.TryRegisterPackageOnDemand(uid, persistUidOverride: isPluginHost);
                    if (result != null) newlyRegistered++;
                }
                catch { }
            }

            // Nested plugin morph/script deps resolve by display name — reactive file hooks never fire.
            if (localPluginHosts != null && localPluginHosts.Count > 0)
            {
                foreach (string ph in localPluginHosts)
                {
                    try { VamOnDemandLoader.EnsureDeclaredDependenciesActivatedForParent(ph); }
                    catch { }
                }
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

        /// <summary>
        /// Gallery scene prep already ran temp whitelist + prewarm.
        /// Arms one-shot skip for next LoadInternal (path may differ after SELF→temp rewrite).
        /// </summary>
        public static void NoteGallerySceneLoadPrep(string saveName)
        {
            s_GalleryScenePrepArmed = true;
            try { s_GalleryScenePrepRealtime = Time.realtimeSinceStartup; }
            catch { s_GalleryScenePrepRealtime = 0f; }
        }

        /// <summary>
        /// Consumes gallery prep arm if still fresh. Path-agnostic — rewrite/temp loads must skip.
        /// </summary>
        static bool TryConsumeGallerySceneLoadPrep()
        {
            if (!s_GalleryScenePrepArmed) return false;
            try
            {
                if (Time.realtimeSinceStartup - s_GalleryScenePrepRealtime > GalleryScenePrepSkipSeconds)
                {
                    s_GalleryScenePrepArmed = false;
                    return false;
                }
            }
            catch
            {
                s_GalleryScenePrepArmed = false;
                return false;
            }

            s_GalleryScenePrepArmed = false;
            return true;
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

            // VaM Browser / Scene Loader / triggers hit LoadInternal without GalleryUIUtils prep.
            // Under scan whitelist those packages never enter VaM unless we temp-allow + prewarm here.
            try
            {
                EnsureNativeSceneLoadWhitelistAndPrewarm(saveName, loadMerge);
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB OnDemand] Native scene-load prep failed: " + ex.Message); } catch { }
            }
        }

        /// <summary>
        /// Scan-whitelist: temp-allow host + SQL transitive deps and prewarm on-demand registration
        /// for scene loads that did not go through the VPB gallery prepare path.
        /// </summary>
        static void EnsureNativeSceneLoadWhitelistAndPrewarm(string saveName, bool loadMerge)
        {
            if (string.IsNullOrEmpty(saveName)) return;
            if (!ScanWhitelistManager.Instance.IsEnabled) return;
            if (TryConsumeGallerySceneLoadPrep()) return;

            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pluginHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string hostUid = null;
            try { hostUid = PackageReferenceVersionResolver.TryExtractPackageUid(saveName); } catch { }
            if (!string.IsNullOrEmpty(hostUid))
                needed.Add(hostUid);

            // Host may be scan-excluded: register it first so GetFileEntry / scene parse can run.
            if (!string.IsNullOrEmpty(hostUid))
            {
                try
                {
                    var hostOnly = new string[] { hostUid };
                    ScanWhitelistManager.Instance.AddTemporaryUidOverrides(hostOnly);
                }
                catch { }
                try { VamOnDemandLoader.TryRegisterPackageOnDemand(hostUid); } catch { }
            }

            FileEntry entry = null;
            try { entry = FileManager.GetFileEntry(saveName); } catch { entry = null; }

            if (entry != null)
            {
                try
                {
                    foreach (string uid in CollectReferencedPackageUids(entry, pluginHosts))
                    {
                        if (!string.IsNullOrEmpty(uid)) needed.Add(uid);
                    }
                }
                catch { }
            }

            if (needed.Count > 0)
            {
                try
                {
                    var hosts = new List<string>(needed);
                    var sqlDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < hosts.Count; i++)
                    {
                        sqlDeps.Clear();
                        if (!VpbLocalDatabase.TryReadRecursiveDependencyUids(hosts[i], sqlDeps)) continue;
                        foreach (string dep in sqlDeps)
                        {
                            if (!string.IsNullOrEmpty(dep)) needed.Add(dep);
                        }
                    }
                }
                catch { }
            }

            // Plugin hosts (BodyLanguage → PoseMe HUD) must be allow-listed even if SQL index lags.
            foreach (string ph in pluginHosts)
            {
                if (!string.IsNullOrEmpty(ph)) needed.Add(ph);
            }

            List<string> added = null;
            if (needed.Count > 0)
            {
                try
                {
                    added = ScanWhitelistManager.Instance.AddTemporaryUidOverrides(needed);
                    if (added != null && added.Count > 0)
                    {
                        LogUtil.Log("[VPB ScanWhitelist] Temporary native scene-load allow-list: +"
                            + string.Join(", ", added.ToArray()));
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB ScanWhitelist] Native temp allow-list failed: " + ex.Message);
                }
            }

            try
            {
                // Always queue coalesced refresh when needed — cleanup drains before removing temp UIDs.
                // Pass needed + pluginHosts so Prewarm does not re-ReadToEnd the scene JSON.
                PrewarmOnDemandPackagesForEntry(entry, saveName, queueCoalescedRefresh: true, pluginHosts, needed);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] Native scene-load prewarm failed: " + ex.Message);
            }

            if (added != null && added.Count > 0)
                ScheduleNativeSceneLoadWhitelistCleanup(added);
        }

        static void ScheduleNativeSceneLoadWhitelistCleanup(List<string> temporaryUids)
        {
            if (temporaryUids == null || temporaryUids.Count == 0) return;
            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            int totalSerialAtStart = 0;
            try { totalSerialAtStart = LogUtil.GetSceneLoadTotalSerial(); } catch { }

            lock (s_NativeScenePrepLock)
            {
                if (s_NativeSceneTempUids == null)
                    s_NativeSceneTempUids = new List<string>(temporaryUids.Count);
                for (int i = 0; i < temporaryUids.Count; i++)
                {
                    string uid = temporaryUids[i];
                    if (string.IsNullOrEmpty(uid)) continue;
                    bool found = false;
                    for (int j = 0; j < s_NativeSceneTempUids.Count; j++)
                    {
                        if (string.Equals(s_NativeSceneTempUids[j], uid, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found) s_NativeSceneTempUids.Add(uid);
                }
                s_NativeSceneCleanupSerial = totalSerialAtStart;
            }

            if (s_NativeSceneCleanupRunning) return;
            try
            {
                sc.StartCoroutine(NativeSceneLoadWhitelistCleanupCoroutine());
            }
            catch { }
        }

        static IEnumerator NativeSceneLoadWhitelistCleanupCoroutine()
        {
            s_NativeSceneCleanupRunning = true;
            int startTotalSerial;
            lock (s_NativeScenePrepLock)
                startTotalSerial = s_NativeSceneCleanupSerial;

            float timeout = 60f;
            float elapsed = 0f;
            bool completedBySceneTotal = false;

            try
            {
                while (elapsed < timeout)
                {
                    int now = startTotalSerial;
                    try { now = LogUtil.GetSceneLoadTotalSerial(); } catch { }
                    if (now != startTotalSerial)
                    {
                        completedBySceneTotal = true;
                        break;
                    }
                    yield return new WaitForSeconds(0.1f);
                    elapsed += 0.1f;
                }

                if (completedBySceneTotal)
                    yield return null;

                FinalizeNativeSceneLoadWhitelistCleanup(
                    completedBySceneTotal
                        ? "scene total ended"
                        : "scene-load-total signal timeout (native cleanup fallback)",
                    !completedBySceneTotal);
            }
            finally
            {
                s_NativeSceneCleanupRunning = false;
            }
        }

        static void FinalizeNativeSceneLoadWhitelistCleanup(string reason, bool asWarning)
        {
            List<string> toRemove = null;
            lock (s_NativeScenePrepLock)
            {
                if (s_NativeSceneTempUids != null && s_NativeSceneTempUids.Count > 0)
                {
                    toRemove = s_NativeSceneTempUids;
                    s_NativeSceneTempUids = null;
                }
            }

            if (toRemove == null || toRemove.Count == 0) return;

            string msg = "[VPB ScanWhitelist] Native scene-load allow-list cleanup: " + reason
                + " removing " + toRemove.Count + " temp UID(s)";
            if (asWarning) LogUtil.LogWarning(msg);
            else LogUtil.Log(msg);

            // Drain pending catalog refresh while temp allow-list still active (#77).
            try
            {
                if (VamOnDemandLoader.HasPendingCoalescedVamRefresh())
                    VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("native_scene_load_cleanup_drain");
            }
            catch { }

            try
            {
                ScanWhitelistManager.Instance.RemoveTemporaryUidOverrides(toRemove);
                LogUtil.Log("[VPB ScanWhitelist] Temporary native scene-load allow-list removed: -"
                    + string.Join(", ", toRemove.ToArray()));
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB ScanWhitelist] Native temp allow-list removal failed: " + ex.Message);
            }
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
