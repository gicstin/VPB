using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MVR.FileManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SimpleJSON;

namespace VPB
{
    public partial class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private void PushUndoSnapshotForClothingHair(Atom target)
        {
            if (Panel == null || target == null) return;
            try
            {
                string atomUid = target.uid;

                Dictionary<string, bool> geometryToggleSnapshot = null;
                JSONClass clothingSnapshot = null;
                JSONClass hairSnapshot = null;

                JSONStorable geo = target.GetStorableByID("geometry");
                if (geo != null)
                {
                    List<string> names = geo.GetBoolParamNames();
                    if (names != null)
                    {
                        geometryToggleSnapshot = new Dictionary<string, bool>();
                        foreach (string key in names)
                        {
                            if (key.StartsWith("clothing:") || key.StartsWith("hair:"))
                            {
                                JSONStorableBool b = geo.GetBoolJSONParam(key);
                                if (b != null) geometryToggleSnapshot[key] = b.val;
                            }
                        }
                    }
                }

                JSONStorable clothing = target.GetStorableByID("Clothing");
                if (clothing != null)
                {
                    clothingSnapshot = clothing.GetJSON();
                }

                JSONStorable hair = target.GetStorableByID("Hair");
                if (hair != null)
                {
                    hairSnapshot = hair.GetJSON();
                }

                Panel.PushUndo(() =>
                {
                    Atom undoAtom = SuperController.singleton.GetAtomByUid(atomUid);
                    if (undoAtom == null) return;

                    if (geometryToggleSnapshot != null)
                    {
                        JSONStorable undoGeo = undoAtom.GetStorableByID("geometry");
                        if (undoGeo != null)
                        {
                            foreach (var kvp in geometryToggleSnapshot)
                            {
                                JSONStorableBool b = undoGeo.GetBoolJSONParam(kvp.Key);
                                if (b != null) b.val = kvp.Value;
                            }
                        }
                    }

                    if (clothingSnapshot != null)
                    {
                        JSONStorable undoClothing = undoAtom.GetStorableByID("Clothing");
                        if (undoClothing != null) undoClothing.RestoreFromJSON(clothingSnapshot);
                    }

                    if (hairSnapshot != null)
                    {
                        JSONStorable undoHair = undoAtom.GetStorableByID("Hair");
                        if (undoHair != null) undoHair.RestoreFromJSON(hairSnapshot);
                    }
                });
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PushUndoSnapshotForClothingHair exception: " + ex);
            }
        }

        private bool PushUndoSnapshotForFullAtomState(Atom atom)
        {
            if (Panel == null || atom == null)
            {
                LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: Panel or atom null");
                return false;
            }
            if (SuperController.singleton == null)
            {
                LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: SuperController.singleton null");
                return false;
            }

            try
            {
                LogUtil.Log("[VPB] PushUndoSnapshotForFullAtomState called for " + atom.uid + " (" + atom.type + ")");
            }
            catch { }

            try
            {
                string atomUid = null;
                try { atomUid = atom.uid; } catch { }
                if (string.IsNullOrEmpty(atomUid))
                {
                    LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: atom uid empty");
                    return false;
                }

                JSONNode atomNode = null;
                try
                {
                    string[] candidates = new[] { "GetSaveJSON", "GetJSON", "GetAtomJSON", "GetSceneJSON" };
                    for (int i = 0; i < candidates.Length && atomNode == null; i++)
                    {
                        MethodInfo mi = null;
                        try { mi = atom.GetType().GetMethod(candidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch { }
                        if (mi == null) continue;
                        var ps = mi.GetParameters();
                        if (ps != null && ps.Length != 0) continue;
                        object result = null;
                        try { result = mi.Invoke(atom, null); }
                        catch { }
                        if (result == null) continue;
                        if (result is JSONNode node)
                        {
                            atomNode = node;
                        }
                        else
                        {
                            try
                            {
                                string s = result.ToString();
                                if (!string.IsNullOrEmpty(s)) atomNode = JSON.Parse(s);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                if (atomNode == null)
                {
                    LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: failed to serialize atom " + atomUid + "; attempting scene-save fallback");

                    try
                    {
                        SuperController sc = SuperController.singleton;
                        if (sc == null) return false;
                        JSONNode sceneRoot = null;
                        try
                        {
                            string[] sceneCandidates = new[]
                            {
                                "GetSaveJSON",
                                "GetSaveSceneJSON",
                                "GetSceneJSON",
                                "GetJSON",
                                "GetSaveJson",
                                "GetSceneJson",
                            };

                            for (int i = 0; i < sceneCandidates.Length && sceneRoot == null; i++)
                            {
                                MethodInfo mi = null;
                                try { mi = sc.GetType().GetMethod(sceneCandidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                                catch { }
                                if (mi == null) continue;
                                var ps = mi.GetParameters();
                                if (ps != null && ps.Length != 0) continue;

                                object result = null;
                                try { result = mi.Invoke(sc, null); }
                                catch { }
                                if (result == null) continue;

                                if (result is JSONNode node)
                                {
                                    sceneRoot = node;
                                }
                                else
                                {
                                    try
                                    {
                                        string s = result.ToString();
                                        if (!string.IsNullOrEmpty(s)) sceneRoot = JSON.Parse(s);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        if (sceneRoot == null || sceneRoot["atoms"] == null)
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: scene JSON reflection failed for " + atomUid);
                            return false;
                        }

                        JSONArray atoms = null;
                        try { atoms = sceneRoot["atoms"].AsArray; } catch { }
                        if (atoms == null)
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: scene atoms missing for " + atomUid);
                            return false;
                        }

                        JSONArray newAtoms = new JSONArray();
                        for (int i = 0; i < atoms.Count; i++)
                        {
                            JSONNode a = atoms[i];
                            if (a == null) continue;
                            try
                            {
                                if (a["id"] != null && string.Equals(a["id"].Value, atomUid, StringComparison.OrdinalIgnoreCase))
                                {
                                    newAtoms.Add(a);
                                }
                            }
                            catch { }
                        }

                        if (newAtoms.Count == 0)
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: atom not found in scene JSON for " + atomUid);
                            return false;
                        }

                        JSONClass miniScene = new JSONClass();
                        miniScene["atoms"] = newAtoms;

                        string undoTempPathFallback = Path.Combine(sc.savesDir, "vpb_temp_undo_atom_" + Guid.NewGuid().ToString() + ".json");
                        try { File.WriteAllText(undoTempPathFallback, miniScene.ToString()); }
                        catch
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: failed to write temp scene for " + atomUid);
                            return false;
                        }

                        try
                        {
                            string loadPath = UI.NormalizePath(undoTempPathFallback);
                            Panel.PushUndo(() => {
                                try
                                {
                                    if (SuperController.singleton == null) return;
                                    if (!File.Exists(undoTempPathFallback)) return;
                                    SceneLoadingUtils.LoadScene(loadPath, true);
                                }
                                catch { }
                                finally
                                {
                                    try { if (File.Exists(undoTempPathFallback)) File.Delete(undoTempPathFallback); } catch { }
                                }
                            });
                        }
                        catch { }

                        LogUtil.Log("[VPB] Undo snapshot pushed (full atom via scene-json): " + atomUid);
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        LogUtil.LogError("[VPB] PushUndoSnapshotForFullAtomState fallback exception: " + ex2);
                        return false;
                    }
                }

                try
                {
                    if (atomNode["id"] == null || string.IsNullOrEmpty(atomNode["id"].Value)) atomNode["id"] = atomUid;
                }
                catch { }

                string atomJson = null;
                try { atomJson = atomNode.ToString(); }
                catch { }
                if (string.IsNullOrEmpty(atomJson))
                {
                    LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: atom json empty for " + atomUid);
                    return false;
                }

                JSONClass mini = new JSONClass();
                JSONArray one = new JSONArray();
                try { one.Add(JSON.Parse(atomJson)); }
                catch { one.Add(atomNode); }
                mini["atoms"] = one;

                string undoTempPath = Path.Combine(SuperController.singleton.savesDir, "vpb_temp_undo_atom_" + Guid.NewGuid().ToString() + ".json");
                File.WriteAllText(undoTempPath, mini.ToString());

                Panel.PushUndo(() => {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        if (!File.Exists(undoTempPath)) return;
                        SceneLoadingUtils.LoadScene(undoTempPath, true);
                    }
                    catch { }
                    finally
                    {
                        try { if (File.Exists(undoTempPath)) File.Delete(undoTempPath); } catch { }
                    }
                });

                LogUtil.Log("[VPB] Undo snapshot pushed (full atom): " + atomUid);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PushUndoSnapshotForFullAtomState exception: " + ex);
                return false;
            }
        }

        private bool IsPluginLikeStorableId(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            if (string.Equals(sid, "PluginPresets", StringComparison.OrdinalIgnoreCase)) return false;
            if (sid.IndexOf("plugin#", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("clothingplugin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("hairplugin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("plugindestructor", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("stopper.", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("plugin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private IEnumerator PostUndoPersonRefreshCoroutine(string atomUid, JSONClass geometrySnapshot, JSONClass skinSnapshot, int framesToWait)
        {
            if (framesToWait < 1) framesToWait = 1;
            for (int i = 0; i < framesToWait; i++)
            {
                yield return new WaitForEndOfFrame();
            }

            Atom targetAtom = null;
            try { targetAtom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null; } catch { }
            if (targetAtom == null) yield break;

            try
            {
                if (geometrySnapshot != null)
                {
                    JSONStorable geo = null;
                    try { geo = targetAtom.GetStorableByID("geometry"); } catch { }
                    try { if (geo != null) geo.RestoreFromJSON(geometrySnapshot); } catch { }
                }
            }
            catch { }

            try
            {
                if (skinSnapshot != null)
                {
                    JSONStorable skin = null;
                    try { skin = targetAtom.GetStorableByID("Skin"); } catch { }
                    try { if (skin != null) skin.RestoreFromJSON(skinSnapshot); } catch { }
                }
            }
            catch { }

            try
            {
                DAZCharacterSelector dcs = null;
                try { dcs = targetAtom.GetComponentInChildren<DAZCharacterSelector>(); } catch { }
                if (dcs != null)
                {
                    string[] methodCandidates = new[] { "Refresh", "RefreshAll", "RefreshGeometry", "RefreshSkin", "ResetSkin", "ResetMaterials", "SyncSkin", "SyncMaterials" };
                    for (int i = 0; i < methodCandidates.Length; i++)
                    {
                        MethodInfo mi = null;
                        try { mi = dcs.GetType().GetMethod(methodCandidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } catch { }
                        if (mi == null) continue;
                        var ps = mi.GetParameters();
                        if (ps != null && ps.Length != 0) continue;
                        try { mi.Invoke(dcs, null); } catch { }
                    }
                }
            }
            catch { }
        }

        private JSONClass ExtractAtomFromScene(JSONClass sceneJSON, string atomType)
        {
            if (sceneJSON == null || sceneJSON["atoms"] == null) return null;
            
            JSONArray atoms = sceneJSON["atoms"].AsArray;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (atoms[i]["type"].Value == atomType)
                {
                    JSONClass personAtom = atoms[i].AsObject;
                    JSONClass extracted = new JSONClass();
                    extracted["storables"] = personAtom["storables"];
                    if (personAtom["setUnlistedParamsToDefault"] != null)
                        extracted["setUnlistedParamsToDefault"] = personAtom["setUnlistedParamsToDefault"];
                    return extracted;
                }
            }
            return null;
        }

        private bool CheckDualPose()
        {
            if (_isDualPose.HasValue) return _isDualPose.Value;
            
            _isDualPose = false;
            
            if (FileEntry != null && FileEntry.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // Try reading using SuperController.singleton.ReadFileIntoString first if path is normalized or manageable
                // Otherwise try stream
                
                string content = null;
                try
                {
                    // Prefer using FileManager or SuperController which handles reading better
                    string normalized = UI.NormalizePath(FileEntry.Path);
                    if (normalized.Contains(":")) // Var
                    {
                         // Use OpenStreamReader for vars as it handles the archive access
                         using (var reader = FileEntry.OpenStreamReader())
                         {
                             content = reader.ReadToEnd();
                         }
                    }
                    else
                    {
                        // For loose files, standard file IO might be safer or SuperController
                        // But FileEntry.OpenStreamReader should ideally work.
                        // However, let's try SuperController read if it's a file path
                         using (var reader = FileEntry.OpenStreamReader())
                         {
                             content = reader.ReadToEnd();
                         }
                    }

                    if (!string.IsNullOrEmpty(content))
                    {
                        _dualPoseNode = JSON.Parse(content);
                        if (_dualPoseNode != null)
                        {
                            // Check PeopleCount (string or int)
                            if (_dualPoseNode["PeopleCount"] != null)
                            {
                                int count = _dualPoseNode["PeopleCount"].AsInt;
                                if (count >= 2)
                                {
                                    _isDualPose = true;
                                    LogUtil.Log($"[DragDropDebug] Detected Dual Pose: PeopleCount={count} in {FileEntry.Name}");
                                }
                                else
                                {
                                    LogUtil.Log($"[DragDropDebug] Not Dual Pose: PeopleCount={count} in {FileEntry.Name}");
                                }
                            }
                            else
                            {
                                 // LogUtil.Log($"[DragDropDebug] Not Dual Pose: No PeopleCount in {FileEntry.Name}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                     LogUtil.LogError($"[DragDropDebug] CheckDualPose error reading {FileEntry.Name}: {ex.Message}");
                }
            }
            return _isDualPose.Value;
        }

        private static string GetItemKeyForMatching(string actualItemName)
        {
            if (string.IsNullOrEmpty(actualItemName)) return "";

            string s = actualItemName;
            int colonIndex = s.IndexOf(":/", StringComparison.Ordinal);
            if (colonIndex >= 0)
            {
                s = s.Substring(colonIndex + 2);
            }

            s = s.Replace('\\', '/');
            int slash = s.LastIndexOf('/');
            if (slash >= 0 && slash < s.Length - 1)
            {
                s = s.Substring(slash + 1);
            }

            if (s.EndsWith(".vam", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 4);
            }

            return s;
        }

        private static void TryGetCreatorFromPresetPath(string presetPath, bool isClothing, out string creator)
        {
            creator = "";
            if (string.IsNullOrEmpty(presetPath)) return;

            string p = presetPath.Replace('\\', '/');
            string[] parts = p.Split('/');
            if (parts == null || parts.Length < 6) return;

            int idx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], "Clothing", StringComparison.OrdinalIgnoreCase) && isClothing)
                {
                    idx = i;
                    break;
                }
                if (string.Equals(parts[i], "Hair", StringComparison.OrdinalIgnoreCase) && !isClothing)
                {
                    idx = i;
                    break;
                }
            }

            // Expected: Custom/Clothing/Female/<creator>/<item>/<preset>.vap
            // Expected: Custom/Hair/Female/<creator>/<item>/<preset>.vap
            if (idx >= 0 && idx + 2 < parts.Length)
            {
                int creatorIdx = idx + 2;
                if (creatorIdx >= 0 && creatorIdx < parts.Length)
                {
                    creator = parts[creatorIdx] ?? "";
                }
            }
        }

        private static JSONStorable FindItemPresetStorable(Atom atom, string itemUid, string itemName, string creator, out string storableId)
        {
            storableId = null;
            if (atom == null) return null;

            // Preferred ids (match VaM)
            if (!string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(itemName))
            {
                storableId = creator + ":" + itemName + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;

                // Check without Preset suffix (e.g. Sim storables)
                storableId = creator + ":" + itemName;
                s = atom.GetStorableByID(storableId);
                if (s != null && s.GetComponentInChildren<MeshVR.PresetManager>() != null) return s;
            }

            if (!string.IsNullOrEmpty(itemName))
            {
                storableId = itemName + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;

                // Check without Preset suffix
                storableId = itemName;
                s = atom.GetStorableByID(storableId);
                if (s != null && s.GetComponentInChildren<MeshVR.PresetManager>() != null) return s;
            }

            if (!string.IsNullOrEmpty(itemUid))
            {
                storableId = itemUid + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;
            }

            // Fallback: search all storables for name match
            foreach (string sid in atom.GetStorableIDs())
            {
                if (sid.EndsWith("Preset", StringComparison.OrdinalIgnoreCase) &&
                    (!string.IsNullOrEmpty(itemName) && sid.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    JSONStorable s = atom.GetStorableByID(sid);
                    if (s != null && s.GetComponentInChildren<MeshVR.PresetManager>() != null)
                    {
                        storableId = sid;
                        return s;
                    }
                }
            }

            storableId = null;
            return null;
        }

        private static JSONClass LoadPresetJsonWithPathFixups(string normalizedPresetPath)
        {
            if (string.IsNullOrEmpty(normalizedPresetPath)) return null;

            JSONNode node = SuperController.singleton.LoadJSON(normalizedPresetPath);
            JSONClass presetJSON = (node != null) ? node.AsObject : null;
            if (presetJSON == null) return null;

            if (normalizedPresetPath.Contains(":"))
            {
                string presetPackageName = normalizedPresetPath.Substring(0, normalizedPresetPath.IndexOf(':'));
                string folderFullPath = MVR.FileManagementSecure.FileManagerSecure.GetDirectoryName(normalizedPresetPath);
                folderFullPath = MVR.FileManagementSecure.FileManagerSecure.NormalizeLoadPath(folderFullPath);

                string presetJSONString = presetJSON.ToString();
                bool modified = false;

                if (presetJSONString.Contains("SELF:"))
                {
                    presetJSONString = presetJSONString.Replace("SELF:", presetPackageName + ":");
                    modified = true;
                }

                if (presetJSONString.Contains("\":\"./"))
                {
                    presetJSONString = presetJSONString.Replace("\":\"./", "\":\"" + folderFullPath + "/");
                    modified = true;
                }

                if (modified)
                {
                    JSONNode parsed = SimpleJSON.JSON.Parse(presetJSONString);
                    presetJSON = (parsed != null) ? parsed.AsObject : presetJSON;
                }

                bool fixedCustomPaths = false;
                FixupUnprefixedCustomPathsInVarPreset(presetJSON, presetPackageName, ref fixedCustomPaths);
            }

            return presetJSON;
        }

        private static void FixupUnprefixedCustomPathsInVarPreset(JSONNode node, string presetPackageName, ref bool modified)
        {
            if (node == null || string.IsNullOrEmpty(presetPackageName)) return;

            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kvp in obj)
                {
                    FixupUnprefixedCustomPathsInVarPreset(kvp.Value, presetPackageName, ref modified);
                }
                return;
            }

            JSONArray arr = node as JSONArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    FixupUnprefixedCustomPathsInVarPreset(arr[i], presetPackageName, ref modified);
                }
                return;
            }

            string v = node.Value;
            if (string.IsNullOrEmpty(v)) return;
            if (v.IndexOf(':') >= 0) return;

            string vNorm = v.Replace('\\', '/');
            if (!vNorm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase)) return;

            string candidate = presetPackageName + ":/" + vNorm;
            string normalizedCandidate = MVR.FileManagementSecure.FileManagerSecure.NormalizePath(candidate);
            if (MVR.FileManagementSecure.FileManagerSecure.FileExists(normalizedCandidate))
            {
                node.Value = candidate;
                modified = true;
            }
        }

        private static string LongestCommonPrefix(List<string> values)
        {
            if (values == null || values.Count == 0) return "";
            string prefix = values[0] ?? "";
            for (int i = 1; i < values.Count; i++)
            {
                string s = values[i] ?? "";
                int j = 0;
                int max = Mathf.Min(prefix.Length, s.Length);
                while (j < max && prefix[j] == s[j]) j++;
                prefix = prefix.Substring(0, j);
                if (prefix.Length == 0) break;
            }
            return prefix;
        }

        private static string InferClothingHairBaseIdFromPresetJson(JSONClass presetJSON)
        {
            if (presetJSON == null || presetJSON["storables"] == null) return "";
            JSONArray storables = presetJSON["storables"].AsArray;
            if (storables == null || storables.Count == 0) return "";

            var baseCandidates = new List<string>();
            var allIds = new List<string>();

            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode node = storables[i];
                if (node == null || node["id"] == null) continue;
                string id = node["id"].Value;
                if (string.IsNullOrEmpty(id)) continue;

                allIds.Add(id);

                if (id.EndsWith("Material", StringComparison.Ordinal))
                {
                    baseCandidates.Add(id.Substring(0, id.Length - 8));
                    continue;
                }

                if (id.EndsWith("Sim", StringComparison.Ordinal))
                {
                    baseCandidates.Add(id.Substring(0, id.Length - 3));
                    continue;
                }

                if (id.EndsWith("Physics", StringComparison.Ordinal))
                {
                    baseCandidates.Add(id.Substring(0, id.Length - 7));
                    continue;
                }
            }

            // Prefer the most common candidate base (best signal for clothing item presets)
            if (baseCandidates.Count > 0)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string c in baseCandidates)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    counts[c] = counts.TryGetValue(c, out int n) ? (n + 1) : 1;
                }
                if (counts.Count > 0)
                {
                    string best = null;
                    int bestCount = -1;
                    foreach (var kvp in counts)
                    {
                        if (kvp.Value > bestCount)
                        {
                            best = kvp.Key;
                            bestCount = kvp.Value;
                        }
                    }
                    if (!string.IsNullOrEmpty(best)) return NormalizeInferredBaseId(best);
                }
            }

            // Fallback: longest common prefix across all ids, then trim to a safe boundary
            string lcp = LongestCommonPrefix(allIds);
            if (string.IsNullOrEmpty(lcp)) return "";
            return NormalizeInferredBaseId(lcp);
        }

        private static string NormalizeInferredBaseId(string baseId)
        {
            if (string.IsNullOrEmpty(baseId)) return "";
            string s = baseId;

            // Many "Sim" / "Material" storables use an underscore separator before the suffix.
            while (s.EndsWith("_", StringComparison.Ordinal) || s.EndsWith("-", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - 1);
                if (s.Length == 0) break;
            }

            return s;
        }

        private static string ExtractKeyFromInferredBaseId(string inferredBaseId)
        {
            if (string.IsNullOrEmpty(inferredBaseId)) return "";
            string s = NormalizeInferredBaseId(inferredBaseId);
            int colon = s.IndexOf(':');
            if (colon >= 0 && colon < s.Length - 1)
            {
                s = s.Substring(colon + 1);
            }
            return s;
        }

        private static IEnumerator ActivateClothingHairItemPresetCoroutine(Atom atom, FileEntry entry, bool isClothing, string itemUid, string itemName)
        {
            if (atom == null || entry == null) yield break;

            string normalizedPath = UI.NormalizePath(entry.Path);
            string creator;
            TryGetCreatorFromPresetPath(entry.Path, isClothing, out creator);

            // Load preset JSON first so we can infer the real storable prefix for variant folders.
            JSONClass presetJSON = LoadPresetJsonWithPathFixups(normalizedPath);
            string inferredBaseId = InferClothingHairBaseIdFromPresetJson(presetJSON);

            string lookupName = !string.IsNullOrEmpty(inferredBaseId) ? inferredBaseId : itemName;
            LogUtil.Log($"[DragDropDebug] Waiting for item preset storable. isClothing={isClothing}, itemName={itemName}, inferredBaseId={inferredBaseId}, itemUid={itemUid}, creator={creator}, presetPath={normalizedPath}");

            DateTime startDelayTime = DateTime.Now;
            while ((DateTime.Now - startDelayTime).TotalSeconds < 10)
            {
                string storableId;
                JSONStorable presetStorable = FindItemPresetStorable(atom, itemUid, itemName, creator, out storableId);
                MeshVR.PresetManager pm = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;

                if (pm == null && !string.IsNullOrEmpty(inferredBaseId))
                {
                    presetStorable = FindItemPresetStorable(atom, itemUid, inferredBaseId, creator, out storableId);
                    pm = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;
                }

                if (pm == null && !string.IsNullOrEmpty(inferredBaseId))
                {
                    // Direct check by inferred base id
                    string directId = inferredBaseId + "Preset";
                    presetStorable = atom.GetStorableByID(directId);
                    pm = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;
                    if (pm != null)
                    {
                        storableId = directId;
                    }
                }

                if (pm != null)
                {
                    if (presetJSON == null)
                    {
                        LogUtil.LogWarning($"[DragDropDebug] Failed to load preset JSON from path: {normalizedPath}");
                        yield break;
                    }

                    LogUtil.Log($"[DragDropDebug] Found item preset storable: {storableId}. Applying preset now.");

                    JSONStorableString presetNameJSS = presetStorable.GetStringJSONParam("presetName");
                    if (presetNameJSS != null)
                    {
                        string fileNameNoExt = Path.GetFileNameWithoutExtension(normalizedPath);
                        if (normalizedPath.Contains(":"))
                        {
                            string presetPackageName = normalizedPath.Substring(0, normalizedPath.IndexOf(':'));
                            presetNameJSS.val = presetPackageName + ":" + fileNameNoExt + ".vap";
                        }
                        else
                        {
                            presetNameJSS.val = fileNameNoExt + ".vap";
                        }
                    }

                    LogUtil.Log($"[DragDropDebug] Loading preset into {storableId} via JSON (delayed)");

                    try
                    {
                        MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath);
                    }
                    catch { }

                    try
                    {
                        pm.LoadPresetFromJSON(presetJSON, false);
                    }
                    finally
                    {
                        try
                        {
                            MVR.FileManagement.FileManager.PopLoadDir();
                        }
                        catch { }
                    }
                    yield break;
                }

                yield return new WaitForEndOfFrame();
            }

            LogUtil.LogWarning($"[DragDropDebug] Timed out waiting for item preset storable for {lookupName} ({itemUid}). Preset not applied: {entry.Path}");
        }

        public static void ActivateClothingHairItemPreset(Atom atom, FileEntry entry, bool isClothing)
        {
            ClothingLoadingUtils.ActivateClothingHairItemPreset(atom, entry, isClothing);
        }

        private bool IsAtomMale(Atom atom)
        {
            if (atom == null) return false;
            JSONStorable geometry = atom.GetStorableByID("geometry");
            if (geometry != null)
            {
                JSONStorableStringChooser charChooser = geometry.GetStringChooserJSONParam("character");
                if (charChooser != null)
                {
                    string val = charChooser.val;
                    if (!string.IsNullOrEmpty(val) && val.StartsWith("Male", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false; 
        }

        private enum ItemType { Clothing, Hair, Pose, Skin, Morphs, Appearance, Animation, BreastPhysics, Plugins, General, ClothingItem, HairItem, ClothingPreset, HairPreset, SubScene, Scene, CUA, Other }

        private ItemType GetItemType(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Path)) return ItemType.Other;
            string p = entry.Path.Replace('\\', '/');
            
            // Check for person preset categories (these use .vap or .json)
            if (p.IndexOf("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Appearance;
            if (p.IndexOf("Custom/Atom/Person/AnimationPresets", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Animation;
            if (p.IndexOf("Custom/Atom/Person/BreastPhysics", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.BreastPhysics;
            if (p.IndexOf("Custom/Atom/Person/Clothing", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Clothing;
            if (p.IndexOf("Custom/Atom/Person/Hair", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Hair;
            if (p.IndexOf("Custom/Atom/Person/Morphs", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Morphs;
            if (p.IndexOf("Custom/Atom/Person/Plugins", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Plugins;
            if (p.IndexOf("Custom/Atom/Person/Pose", StringComparison.OrdinalIgnoreCase) >= 0 || p.EndsWith(".vac", StringComparison.OrdinalIgnoreCase)) return ItemType.Pose;
            if (p.IndexOf("Custom/Atom/Person/Skin", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Skin;
            if (p.IndexOf("Custom/Atom/Person/General", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.General;
            
            // Check for clothing/hair items (these use .vam and toggle on/off)
            if (p.IndexOf("Custom/Clothing/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)) return ItemType.ClothingPreset;
                return ItemType.ClothingItem;
            }
            if (p.IndexOf("Custom/Hair/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)) return ItemType.HairPreset;
                return ItemType.HairItem;
            }
            
            // Check for subscenes
            if (p.IndexOf("Custom/SubScene", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.SubScene;

            // Scenes
            if (p.IndexOf("Saves/scene", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("/scene/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return ItemType.Scene;
            }
            if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ItemType.Scene;
            }

            // Pose fallback for loose .json pose presets (non-.vap) when path/name indicates pose
            if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.IndexOf("pose", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ItemType.Pose;
            }

            // CUA
            if (p.IndexOf("Custom/Assets", StringComparison.OrdinalIgnoreCase) >= 0 || p.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
            {
                return ItemType.CUA;
            }
            
            return ItemType.Other;
        }

        private string GetStorableIdForItemType(ItemType itemType)
        {
            switch (itemType)
            {
                case ItemType.Appearance: return "AppearancePresets";
                case ItemType.Animation: return "AnimationPresets";
                case ItemType.BreastPhysics: return "FemaleBreastPhysicsPresets";
                case ItemType.Clothing: return "ClothingPresets";
                case ItemType.ClothingItem: return "ClothingPresets";
                case ItemType.General: return "Preset";
                case ItemType.Hair: return "HairPresets";
                case ItemType.HairItem: return "HairPresets";
                case ItemType.ClothingPreset: return null; // Targets specific clothing items
                case ItemType.HairPreset: return null; // Targets specific hair items
                case ItemType.Morphs: return "MorphPresets";
                case ItemType.Plugins: return "PluginPresets";
                case ItemType.Pose: return "PosePresets";
                case ItemType.Skin: return "SkinPresets";
                default: return null;
            }
        }


    }

}
