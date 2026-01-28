using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MVR.FileManagement;
using MVR.FileManagementSecure;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class ClothingLoadingUtils
    {
        private static MethodInfo s_ClothingClearMethod;

        private static bool TryInvokeAction(JSONStorable storable, string actionName)
        {
            if (storable == null || string.IsNullOrEmpty(actionName)) return false;
            try
            {
                JSONStorableAction act = storable.GetAction(actionName);
                if (act != null && act.actionCallback != null)
                {
                    act.actionCallback();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static IEnumerator PostApplyClothingHairFixupCoroutine(Atom atom, string inferredBaseId)
        {
            if (atom == null) yield break;

            // Conservative settle: give VaM a few frames for load/skin/physics init.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // Best-effort refresh hooks. These are intentionally minimal and guarded:
            // only call if an action exists on the storable.
            string[] actionNames = new string[]
            {
                "Refresh",
                "Rebuild",
                "Resync",
                "Reset",
                "ResetSimulation",
                "ResetSim",
                "RebuildColliders",
                "RefreshColliders",
            };

            try
            {
                // If we have a stable inferred base id, probe common companion storables.
                if (!string.IsNullOrEmpty(inferredBaseId))
                {
                    string[] candidateStorables = new string[]
                    {
                        inferredBaseId,
                        inferredBaseId + "Sim",
                        inferredBaseId + "Physics",
                        inferredBaseId + "Colliders",
                        inferredBaseId + "Collider",
                        inferredBaseId + "Material",
                    };

                    for (int i = 0; i < candidateStorables.Length; i++)
                    {
                        JSONStorable s = null;
                        try { s = atom.GetStorableByID(candidateStorables[i]); } catch { }
                        if (s == null) continue;

                        for (int a = 0; a < actionNames.Length; a++)
                        {
                            TryInvokeAction(s, actionNames[a]);
                        }
                    }
                }

                // Also try common VaM aggregate storables.
                JSONStorable clothing = null;
                JSONStorable hair = null;
                try { clothing = atom.GetStorableByID("Clothing"); } catch { }
                try { hair = atom.GetStorableByID("Hair"); } catch { }

                if (clothing != null)
                {
                    for (int a = 0; a < actionNames.Length; a++)
                    {
                        TryInvokeAction(clothing, actionNames[a]);
                    }
                }
                if (hair != null)
                {
                    for (int a = 0; a < actionNames.Length; a++)
                    {
                        TryInvokeAction(hair, actionNames[a]);
                    }
                }
            }
            catch { }
        }

        private static void SchedulePostApplyFixup(Atom atom, string inferredBaseId)
        {
            try
            {
                if (atom == null) return;
                if (SuperController.singleton == null) return;
                SuperController.singleton.StartCoroutine(PostApplyClothingHairFixupCoroutine(atom, inferredBaseId));
            }
            catch { }
        }

        private static void EnsureClothingClearCached(JSONStorable clothing)
        {
            if (s_ClothingClearMethod != null) return;
            if (clothing == null) return;
            try
            {
                s_ClothingClearMethod = clothing.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
            }
            catch { }
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

            if (!string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(itemName))
            {
                storableId = creator + ":" + itemName + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;

                storableId = creator + ":" + itemName;
                s = atom.GetStorableByID(storableId);
                if (s != null && s.GetComponentInChildren<MeshVR.PresetManager>() != null) return s;
            }

            if (!string.IsNullOrEmpty(itemName))
            {
                storableId = itemName + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;

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
            string normalizedCandidate = FileManagerSecure.NormalizePath(candidate);
            if (FileManagerSecure.FileExists(normalizedCandidate))
            {
                node.Value = candidate;
                modified = true;
            }
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
                string folderFullPath = FileManagerSecure.GetDirectoryName(normalizedPresetPath);
                folderFullPath = FileManagerSecure.NormalizeLoadPath(folderFullPath);

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

        private static string NormalizeInferredBaseId(string baseId)
        {
            if (string.IsNullOrEmpty(baseId)) return "";
            string s = baseId;
            while (s.EndsWith("_", StringComparison.Ordinal) || s.EndsWith("-", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - 1);
                if (s.Length == 0) break;
            }
            return s;
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

            string lcp = LongestCommonPrefix(allIds);
            if (string.IsNullOrEmpty(lcp)) return "";
            return NormalizeInferredBaseId(lcp);
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

        private static string GetItemKeyForMatching(string itemUid)
        {
            if (string.IsNullOrEmpty(itemUid)) return "";
            string s = itemUid;
            int idx = s.LastIndexOf('/');
            if (idx >= 0 && idx < s.Length - 1) s = s.Substring(idx + 1);
            idx = s.LastIndexOf('\\');
            if (idx >= 0 && idx < s.Length - 1) s = s.Substring(idx + 1);
            idx = s.LastIndexOf('.');
            if (idx > 0) s = s.Substring(0, idx);
            return s;
        }

        private static IEnumerator ActivateClothingHairItemPresetCoroutine(Atom atom, FileEntry entry, bool isClothing, string itemUid, string itemName)
        {
            if (atom == null || entry == null) yield break;

            string normalizedPath = UI.NormalizePath(entry.Path);
            string creator;
            TryGetCreatorFromPresetPath(entry.Path, isClothing, out creator);

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
                        FileManager.PushLoadDirFromFilePath(normalizedPath);
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
                            FileManager.PopLoadDir();
                        }
                        catch { }
                    }

                    // Conservative post-apply stabilization (best-effort, no-op if actions are missing).
                    SchedulePostApplyFixup(atom, inferredBaseId);
                    yield break;
                }

                yield return new WaitForEndOfFrame();
            }

            LogUtil.LogWarning($"[DragDropDebug] Timed out waiting for item preset storable for {lookupName} ({itemUid}). Preset not applied: {entry.Path}");
        }

        public static void ActivateClothingHairItemPreset(Atom atom, FileEntry entry, bool isClothing)
        {
            if (atom == null || entry == null) return;
            string path = entry.Path;
            string normalizedPath = UI.NormalizePath(path);

            string itemName = "";
            string packageName = "";
            string p = path.Replace('\\', '/');

            if (p.Contains(":"))
            {
                packageName = p.Substring(0, p.IndexOf(':'));
            }

            string[] parts = p.Split('/');
            if (parts.Length >= 2)
            {
                itemName = parts[parts.Length - 2];
            }

            if (string.IsNullOrEmpty(itemName)) return;

            LogUtil.Log($"[DragDropDebug] Target Item Name from path: {itemName} (Package: {packageName})");

            JSONStorable geometry = atom.GetStorableByID("geometry");
            if (geometry == null) return;

            string inferredKey = "";
            try
            {
                JSONClass presetJSON = LoadPresetJsonWithPathFixups(normalizedPath);
                string inferredBaseId = InferClothingHairBaseIdFromPresetJson(presetJSON);
                inferredKey = ExtractKeyFromInferredBaseId(inferredBaseId);
            }
            catch { }

            string itemUid = "";
            string prefix = isClothing ? "clothing:" : "hair:";

            if (!string.IsNullOrEmpty(inferredKey))
            {
                foreach (string paramName in geometry.GetBoolParamNames())
                {
                    if (!paramName.StartsWith(prefix)) continue;
                    string actualItemName = paramName.Substring(prefix.Length);
                    string actualKey = GetItemKeyForMatching(actualItemName);
                    if (actualKey.Equals(inferredKey, StringComparison.OrdinalIgnoreCase))
                    {
                        itemUid = actualItemName;
                        LogUtil.Log($"[DragDropDebug] Matched item via inferredKey: {inferredKey} -> {itemUid}");
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(itemUid))
            {
                LogUtil.Log($"[DragDropDebug] Identified Item UID (inferred): {itemUid}");
                JSONStorableBool inferredActive = geometry.GetBoolJSONParam(prefix + itemUid);
                if (inferredActive != null && !inferredActive.val) inferredActive.val = true;
                SuperController.singleton.StartCoroutine(ActivateClothingHairItemPresetCoroutine(atom, entry, isClothing, itemUid, itemName));
                return;
            }

            foreach (string paramName in geometry.GetBoolParamNames())
            {
                if (paramName.StartsWith(prefix))
                {
                    string actualItemName = paramName.Substring(prefix.Length);
                    string actualKey = GetItemKeyForMatching(actualItemName);

                    bool packageMatch = string.IsNullOrEmpty(packageName) ||
                                       actualItemName.StartsWith(packageName + ".") ||
                                       actualItemName.StartsWith(packageName + ":") ||
                                       actualItemName.StartsWith(packageName + ":/");

                    if (packageMatch)
                    {
                        if (actualKey.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                        {
                            itemUid = actualItemName;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(itemUid))
            {
                foreach (string paramName in geometry.GetBoolParamNames())
                {
                    if (paramName.StartsWith(prefix))
                    {
                        string actualItemName = paramName.Substring(prefix.Length);
                        string actualKey = GetItemKeyForMatching(actualItemName);
                        if (actualKey.Equals(itemName, StringComparison.OrdinalIgnoreCase) ||
                            actualKey.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            itemUid = actualItemName;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(itemUid))
            {
                string cleanItemName = itemName.Replace(" ", "").Replace("_", "").ToLower();

                foreach (string paramName in geometry.GetBoolParamNames())
                {
                    if (paramName.StartsWith(prefix))
                    {
                        string actualItemName = paramName.Substring(prefix.Length);
                        string actualKey = GetItemKeyForMatching(actualItemName);
                        string cleanActual = actualKey.Replace(" ", "").Replace("_", "").ToLower();

                        if (cleanActual.Contains(cleanItemName) || cleanItemName.Contains(cleanActual))
                        {
                            itemUid = actualItemName;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(itemUid))
            {
                string vamPath = FileManagerSecure.GetDirectoryName(path) + "/" + itemName + ".vam";
                if (FileManagerSecure.FileExists(vamPath))
                {
                    LogUtil.Log($"[DragDropDebug] Clothing item not found on atom. Attempting to load parent .vam via JSON: {vamPath}");

                    string presetsStorableId = isClothing ? "ClothingPresets" : "HairPresets";
                    JSONStorable presetsStorable = atom.GetStorableByID(presetsStorableId);
                    if (presetsStorable != null)
                    {
                        try
                        {
                            MeshVR.PresetManager pm = presetsStorable.GetComponentInChildren<MeshVR.PresetManager>();
                            if (pm != null)
                            {
                                string normalizedVam = UI.NormalizePath(vamPath);
                                JSONNode node = SuperController.singleton.LoadJSON(normalizedVam);
                                JSONClass vamJSON = (node != null) ? node.AsObject : null;
                                if (vamJSON != null)
                                {
                                    if (normalizedVam.Contains(":"))
                                    {
                                        string pkg = normalizedVam.Substring(0, normalizedVam.IndexOf(':'));
                                        string jsonStr = vamJSON.ToString();
                                        if (jsonStr.Contains("SELF:"))
                                        {
                                            JSONNode parsed = SimpleJSON.JSON.Parse(jsonStr.Replace("SELF:", pkg + ":"));
                                            vamJSON = (parsed != null) ? parsed.AsObject : null;
                                        }
                                    }

                                    JSONStorableString presetNameJSS = presetsStorable.GetStringJSONParam("presetName");
                                    if (presetNameJSS != null)
                                    {
                                        try
                                        {
                                            presetNameJSS.val = pm.GetPresetNameFromFilePath(normalizedVam);
                                        }
                                        catch
                                        {
                                            presetNameJSS.val = Path.GetFileNameWithoutExtension(normalizedVam);
                                        }
                                    }

                                    pm.LoadPresetFromJSON(vamJSON, false);
                                    itemUid = UI.NormalizePath(vamPath);
                                }
                                else
                                {
                                    LogUtil.LogError($"[DragDropDebug] Failed to load parent .vam JSON: {normalizedVam}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError($"[DragDropDebug] Failed to load parent .vam: {ex.Message}");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(itemUid))
            {
                LogUtil.LogWarning($"[DragDropDebug] Could not identify target item UID for preset: {itemName}");
                return;
            }

            LogUtil.Log($"[DragDropDebug] Identified Item UID: {itemUid}");

            JSONStorableBool activeJSB = geometry.GetBoolJSONParam(prefix + itemUid);
            if (activeJSB != null && !activeJSB.val)
            {
                activeJSB.val = true;
            }

            SuperController.singleton.StartCoroutine(ActivateClothingHairItemPresetCoroutine(atom, entry, isClothing, itemUid, itemName));
        }

        public static void RemoveAllClothing(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] RemoveAllClothing: target is null");
                return;
            }

            LogUtil.Log($"[VPB] RemoveAllClothing: target={target.uid} ({target.type})");

            bool cleared = false;
            try
            {
                JSONStorable clothing = target.GetStorableByID("Clothing");
                LogUtil.Log($"[VPB] RemoveAllClothing: Clothing storable {(clothing != null ? "found" : "NOT found")}");
                if (clothing != null)
                {
                    EnsureClothingClearCached(clothing);
                    LogUtil.Log($"[VPB] RemoveAllClothing: Clear() method {(s_ClothingClearMethod != null ? "found" : "NOT found")} on {clothing.GetType().FullName}");
                    if (s_ClothingClearMethod != null)
                    {
                        s_ClothingClearMethod.Invoke(clothing, null);
                        cleared = true;
                        LogUtil.Log("[VPB] RemoveAllClothing: Clear() invoked");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] RemoveAllClothing: Clear() exception: " + ex);
            }

            if (!cleared)
            {
                LogUtil.LogWarning("[VPB] RemoveAllClothing: falling back to geometry bool disable");
                try
                {
                    JSONStorable geometry = target.GetStorableByID("geometry");
                    if (geometry == null)
                    {
                        LogUtil.LogWarning("[VPB] RemoveAllClothing: geometry storable NOT found");
                        return;
                    }

                    int disabledCount = 0;
                    int totalClothingParams = 0;
                    foreach (var name in geometry.GetBoolParamNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                        totalClothingParams++;

                        JSONStorableBool active = null;
                        try { active = geometry.GetBoolJSONParam(name); } catch { }
                        if (active == null) continue;
                        if (!active.val) continue;

                        active.val = false;
                        disabledCount++;
                    }

                    LogUtil.Log($"[VPB] RemoveAllClothing: geometry fallback disabled {disabledCount} clothing items (params={totalClothingParams})");
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] RemoveAllClothing: geometry fallback exception: " + ex);
                }
            }
        }
    }
}
