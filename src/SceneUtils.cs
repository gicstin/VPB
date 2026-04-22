using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class SceneLoadingUtils
    {
        static int sceneLoadSerial;
        static int lastScheduledSceneLoadSerial;

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
                if (string.IsNullOrEmpty(normalizedPath)) return false;
                SuperController sc = SuperController.singleton;
                if (sc == null) return false;

                EnsureLoadMethodsCached(sc);

                if (!merge)
                {
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

        private static string WriteTempSceneJson(JSONNode root, string filePrefix)
        {
            try
            {
                if (root == null) return null;

                string dir = GetTempScenesDir();
                try { Directory.CreateDirectory(dir); } catch { }

                string name = (string.IsNullOrEmpty(filePrefix) ? "vpb_scene" : filePrefix) + "_" + Guid.NewGuid().ToString() + ".json";
                string tempPath = Path.Combine(dir, name);
                File.WriteAllText(tempPath, root.ToString());

                ScheduleTempFileDelete(tempPath, 20);
                return tempPath.Replace('\\', '/');
            }
            catch
            {
                return null;
            }
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

        public static bool TryReplaceSceneKeepPersons(string scenePath, FileEntry entry)
        {
            try
            {
                if (string.IsNullOrEmpty(scenePath)) return false;
                if (SuperController.singleton == null) return false;
                if (Messager.singleton == null) return false;

                Messager.singleton.StartCoroutine(ReplaceSceneKeepPersonsCoroutine(scenePath, entry));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerator ReplaceSceneKeepPersonsCoroutine(string scenePath, FileEntry entry)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) yield break;

            List<string> personUids = new List<string>();
            foreach (Atom a in sc.GetAtoms())
            {
                if (SceneUtils.IsPersonLikeAtom(a)) personUids.Add(a.uid);
            }

            if (personUids.Count == 0)
            {
                sc.Load(UI.NormalizePath(scenePath));
                yield break;
            }

            string currentSceneTemp = Path.Combine(GetTempScenesDir(), "vpb_temp_current_" + Guid.NewGuid().ToString() + ".json").Replace('\\', '/');
            try { Directory.CreateDirectory(GetTempScenesDir()); } catch { }
            try
            {
                sc.Save(currentSceneTemp);
            }
            catch
            {
                currentSceneTemp = Path.Combine(sc.savesDir, "vpb_temp_current_" + Guid.NewGuid().ToString() + ".json");
                sc.Save(currentSceneTemp);
            }

            string personsOnlyTemp = CreateFilteredSceneJSON(currentSceneTemp, null, (atom) => atom != null && SceneUtils.IsPersonLikeAtomType(atom["type"].Value), false);
            try { if (File.Exists(currentSceneTemp)) File.Delete(currentSceneTemp); } catch { }

            if (string.IsNullOrEmpty(personsOnlyTemp))
            {
                yield break;
            }

            // Remove persons from new scene to prevent duplication/conflict.
            string newSceneFiltered = CreateFilteredSceneJSON(
                scenePath,
                entry,
                (atom) =>
                {
                    if (atom == null) return false;
                    string t = atom["type"].Value;
                    if (SceneUtils.IsPersonLikeAtomType(t)) return false;
                    return true;
                },
                false);

            string sceneToLoad = string.IsNullOrEmpty(newSceneFiltered) ? scenePath : newSceneFiltered;
            LoadScene(UI.NormalizePath(sceneToLoad), false);

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            LoadScene(UI.NormalizePath(personsOnlyTemp), true);

            ScheduleTempFileDelete(personsOnlyTemp, 30);
            ScheduleTempFileDelete(newSceneFiltered, 30);
        }

        private static void RewriteCustomPathsRecursive(JSONNode node, List<string> unresolved, ref int replaced)
        {
            if (node == null) return;

            if (node is JSONData jd)
            {
                string v = jd.Value;
                if (!string.IsNullOrEmpty(v))
                {
                    string candidate = v;
                    if (candidate.StartsWith("/")) candidate = candidate.Substring(1);
                    if (candidate.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase))
                    {
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
                    RewriteCustomPathsRecursive(ja[i], unresolved, ref replaced);
                }
                return;
            }

            if (node is JSONClass jc)
            {
                foreach (string k in jc.Keys)
                {
                    RewriteCustomPathsRecursive(jc[k], unresolved, ref replaced);
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

            int replaced = 0;
            var unresolved = new List<string>();
            try
            {
                RewriteCustomPathsRecursive(root, unresolved, ref replaced);
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
            if (entry == null) return false;

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
                                        if (depCount > 0)
                                        {
                                            string sample = string.Join(", ", deps.Take(5).ToArray());
                                            LogUtil.Log($"[VPB] EnsureInstalled: Parsed {depCount} package refs from {entry.Name}. Sample: {sample}");
                                        }

                                        int missing = 0;
                                        foreach (string key in deps)
                                        {
                                            VarPackage pkg = FileManager.GetPackageForDependency(key, false);
                                            if (pkg != null) continue;
                                            missing++;
                                        }
                                        if (missing > 0)
                                        {
                                            LogUtil.LogWarning($"[VPB] EnsureInstalled: Missing {missing}/{deps.Count} referenced packages for {entry.Name}");
                                        }
                                    }
                                    catch { }

                                    bool depsChanged = FileButton.EnsureInstalledBySet(deps, outMovedPackageUids);
                                    if (depsChanged) flag = true;
                                }
                            }
                        }
                    }
                }

                return flag;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
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
                        if (pkg != null && !string.IsNullOrEmpty(pkg.Uid))
                            result.Add(pkg.Uid);
                    }
                }
            }
            catch { }

            return result;
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
