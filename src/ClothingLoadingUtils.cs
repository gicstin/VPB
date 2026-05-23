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

        private static bool Has(string source, string value)
        {
            if (source == null || value == null) return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public enum ResourceGender
        {
            Unknown = 0,
            Female = 1,
            Male = 2,
        }

        public enum ResourceKind
        {
            Unknown = 0,
            Clothing = 1,
            Hair = 2,
        }

        public static bool IsDecalLikePath(string pathOrUid)
        {
            if (string.IsNullOrEmpty(pathOrUid)) return false;
            string p = pathOrUid;

            if (Has(p, "/textures/makeups/") || Has(p, "/textures/makeup/") || Has(p, "/makeups/") || Has(p, "makeup")) return true;
            if (Has(p, "/textures/decals/") || Has(p, "/textures/decal/") || Has(p, "/decals/") || Has(p, "/decal/")) return true;
            if (Has(p, "/textures/overlays/") || Has(p, "/textures/overlay/") || Has(p, "/overlays/") || Has(p, "/overlay/")) return true;

            if (Has(p, "freckle") || Has(p, "blush") || Has(p, "eyeshadow") || Has(p, "eye_shadow") || Has(p, "eyeliner") || Has(p, "eye_liner") ||
                Has(p, "lipstick") || Has(p, "foundation") || Has(p, "concealer") || Has(p, "highlight") || Has(p, "highlighter") || Has(p, "contour") || Has(p, "powder"))
                return true;

            if (Has(p, "facemask") || Has(p, "face_mask") || Has(p, "mask") || Has(p, "opacity") || Has(p, "alpha"))
            {
                if (Has(p, "face") || Has(p, "makeup") || Has(p, "makeups") || Has(p, "freckle") || Has(p, "blush")) return true;
            }

            if (Has(p, "iris") || Has(p, "cornea") || Has(p, "eyeball") || Has(p, "sclera")) return true;
            if (Has(p, "eye") && (Has(p, "enhance") || Has(p, "enhancement") || Has(p, "overlay") || Has(p, "decal") || Has(p, "makeup"))) return true;

            return false;
        }

        public static void ClassifyClothingHairPath(string pathOrUid, out ResourceKind kind, out ResourceGender gender)
        {
            kind = ResourceKind.Unknown;
            gender = ResourceGender.Unknown;
            if (string.IsNullOrEmpty(pathOrUid)) return;

            // Expected patterns include:
            // - Custom/Clothing/Female/...
            // - Custom/Clothing/Male/...
            // - package.var:/Custom/Clothing/Female/...
            // We keep this allocation-free: no Replace/ToLower/Substring.

            int start = 0;
            int idx = pathOrUid.IndexOf(":/", StringComparison.Ordinal);
            if (idx >= 0) start = idx + 2;
            else
            {
                idx = pathOrUid.IndexOf(":\\", StringComparison.Ordinal);
                if (idx >= 0) start = idx + 2;
            }
            if (start < pathOrUid.Length && (pathOrUid[start] == '/' || pathOrUid[start] == '\\')) start++;

            // Also handle cases where we get full paths that contain "/Custom/..." somewhere in the middle.
            int customIdx = pathOrUid.IndexOf("Custom/", start, StringComparison.OrdinalIgnoreCase);
            if (customIdx < 0) customIdx = pathOrUid.IndexOf("Custom\\", start, StringComparison.OrdinalIgnoreCase);
            if (customIdx >= 0) start = customIdx;

            if (pathOrUid.IndexOf("Custom/Clothing/Female", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Clothing\\Female", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Clothing;
                gender = ResourceGender.Female;
                return;
            }
            if (pathOrUid.IndexOf("Custom/Clothing/Male", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Clothing\\Male", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Clothing;
                gender = ResourceGender.Male;
                return;
            }
            if (pathOrUid.IndexOf("Custom/Hair/Female", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Hair\\Female", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Hair;
                gender = ResourceGender.Female;
                return;
            }
            if (pathOrUid.IndexOf("Custom/Hair/Male", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Hair\\Male", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Hair;
                gender = ResourceGender.Male;
                return;
            }

            // Atom-level preset folders (used by VaM for person clothing/hair presets)
            if (pathOrUid.IndexOf("Custom/Atom/Person/Clothing", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Atom\\Person\\Clothing", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Clothing;
                return;
            }
            if (pathOrUid.IndexOf("Custom/Atom/Person/Hair", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Atom\\Person\\Hair", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Hair;
                return;
            }

            if (pathOrUid.IndexOf("Custom/Clothing", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Clothing", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Clothing;
                return;
            }
            if (pathOrUid.IndexOf("Custom/Hair", start, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("Custom\\Hair", start, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = ResourceKind.Hair;
                return;
            }
        }

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

            // Conservative settle: give VaM more frames for load/skin/physics/collider init.
            // Clothing items need extra time to properly attach colliders and physics to the body.
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForEndOfFrame();
            }
            yield return new WaitForSeconds(0.1f);

            // Best-effort refresh hooks. These are intentionally minimal and guarded:
            // only call if an action exists on the storable.
            string[] actionNames = new string[]
            {
                "Refresh",
                "Rebuild",
                "Resync",
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
                creator = parts[idx + 2] ?? "";
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

        private static string NormalizeMatchToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            char[] buf = new char[value.Length];
            int n = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                    buf[n++] = char.ToLowerInvariant(c);
            }
            return n > 0 ? new string(buf, 0, n) : "";
        }

        private static JSONStorable FindItemPresetStorableFuzzy(Atom atom, string itemUid, string itemName, string creator, string inferredBaseId, bool isClothing, out string storableId)
        {
            storableId = null;
            if (atom == null) return null;

            List<string> needles = new List<string>();
            void add(string s)
            {
                string n = NormalizeMatchToken(s);
                if (!string.IsNullOrEmpty(n) && !needles.Contains(n))
                    needles.Add(n);
            }

            add(itemName);
            add(GetItemKeyForMatching(itemUid));
            add(ExtractKeyFromInferredBaseId(inferredBaseId));
            add(inferredBaseId);
            add(creator);

            if (needles.Count == 0) return null;

            JSONStorable best = null;
            string bestId = null;
            int bestScore = -1;
            int bestTokenHits = 0;

            List<string> storableIds = atom.GetStorableIDs();
            for (int i = 0; i < storableIds.Count; i++)
            {
                string sid = storableIds[i];
                if (string.IsNullOrEmpty(sid)) continue;
                if (string.Equals(sid, "ClothingPresets", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(sid, "HairPresets", StringComparison.OrdinalIgnoreCase)) continue;

                JSONStorable s = atom.GetStorableByID(sid);
                if (s == null) continue;
                MeshVR.PresetManager pm = s.GetComponentInChildren<MeshVR.PresetManager>();
                if (pm == null) continue;

                string sidNorm = NormalizeMatchToken(sid);
                if (string.IsNullOrEmpty(sidNorm)) continue;

                int tokenHits = 0;
                for (int k = 0; k < needles.Count; k++)
                {
                    if (sidNorm.Contains(needles[k])) tokenHits++;
                }

                // Hard requirement: must match at least one semantic token from item/path.
                if (tokenHits == 0) continue;

                // Basic type sanity to avoid applying clothing presets into unrelated hair storables (and vice versa).
                string sidLower = sid.ToLowerInvariant();
                if (isClothing && sidLower.Contains("hair") && !sidLower.Contains("cloth"))
                    continue;
                if (!isClothing && sidLower.Contains("cloth") && !sidLower.Contains("hair"))
                    continue;

                int score = tokenHits * 3;
                if (sid.EndsWith("Preset", StringComparison.OrdinalIgnoreCase)) score += 2;
                if (sid.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0) score += 1;

                if (score > bestScore || (score == bestScore && tokenHits > bestTokenHits))
                {
                    bestScore = score;
                    bestTokenHits = tokenHits;
                    best = s;
                    bestId = sid;
                }
            }

            if (best != null && bestScore > 0 && bestTokenHits > 0)
            {
                storableId = bestId;
                return best;
            }

            return null;
        }

        private static void FixupUnprefixedCustomPathsInVarPreset(JSONNode node, string presetPackageName)
        {
            if (node == null || string.IsNullOrEmpty(presetPackageName)) return;

            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kvp in obj)
                {
                    FixupUnprefixedCustomPathsInVarPreset(kvp.Value, presetPackageName);
                }
                return;
            }

            JSONArray arr = node as JSONArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    FixupUnprefixedCustomPathsInVarPreset(arr[i], presetPackageName);
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
            }
        }

        private static JSONClass LoadPresetJsonWithPathFixups(string normalizedPresetPath)
        {
            if (string.IsNullOrEmpty(normalizedPresetPath)) return null;

            JSONNode node = UI.LoadJSONWithFallback(normalizedPresetPath, null);
            JSONClass presetJSON = (node != null) ? node.AsObject : null;
            if (presetJSON == null) return null;

            if (UI.IsLikelyVarPackageReference(normalizedPresetPath))
            {
                string presetPackageName = normalizedPresetPath.Substring(0, normalizedPresetPath.IndexOf(':'));
                string folderFullPath = FileManagerSecure.GetDirectoryName(normalizedPresetPath);
                folderFullPath = FileManagerSecure.NormalizeLoadPath(folderFullPath);

                string presetJSONString = VPB.src.util.JsonSerializationUtil.Serialize(presetJSON, 16_384);
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

                FixupUnprefixedCustomPathsInVarPreset(presetJSON, presetPackageName);
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

            // Give the clothing item a few frames to initialize after being activated
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            DateTime startDelayTime = DateTime.Now;
            while ((DateTime.Now - startDelayTime).TotalSeconds < 20)
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

                if (pm == null)
                {
                    presetStorable = FindItemPresetStorableFuzzy(atom, itemUid, itemName, creator, inferredBaseId, isClothing, out storableId);
                    pm = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;
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
                        if (UI.IsLikelyVarPackageReference(normalizedPath))
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

                    VpbImport.LoadPreset(entry, atom, VpbResourceType.General,
                        ClothingApplyMode.Replace, presetJC: presetJSON, storableNameOverride: storableId,
                        skipDependencyPrewarm: true, updateLastRestoredData: false);

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

            if (isClothing && VPBConfig.Instance != null && VPBConfig.Instance.DragDropReplaceMode)
            {
                RemoveAllClothing(atom);
            }

            string normalizedPath = UI.NormalizePath(path);

            string itemName = "";
            string packageName = "";
            string p = path.Replace('\\', '/');

            if (UI.IsLikelyVarPackageReference(p))
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
                                JSONNode node = UI.LoadJSONWithFallback(normalizedVam, null);
                                JSONClass vamJSON = (node != null) ? node.AsObject : null;
                                if (vamJSON != null)
                                {
                                    if (UI.IsLikelyVarPackageReference(normalizedVam))
                                    {
                                        string pkg = normalizedVam.Substring(0, normalizedVam.IndexOf(':'));
                                        string jsonStr = VPB.src.util.JsonSerializationUtil.Serialize(vamJSON, 16_384);
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

                                    VpbImport.LoadPreset(entry, atom, isClothing ? VpbResourceType.Clothing : VpbResourceType.Hair,
                                        ClothingApplyMode.Replace, presetJC: vamJSON);

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

        internal sealed class ClothingHairUndoState
        {
            public Dictionary<string, bool> GeometryToggles;
            public List<JSONClass> StorableSnapshots;
        }

        private static readonly HashSet<string> s_ClothingHairAggregateStorables =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Clothing",
                "Hair",
                "ClothingPresets",
                "HairPresets",
            };

        private static readonly string[] s_ClothingHairItemStorableSuffixes =
        {
            "Preset",
            "Presets",
            "Material",
            "Sim",
            "Physics",
            "Colliders",
            "Collider",
        };

        private static string NormalizeUndoMatchToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            char[] buf = new char[value.Length];
            int n = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                    buf[n++] = char.ToLowerInvariant(c);
            }
            return n > 0 ? new string(buf, 0, n) : "";
        }

        private static void CollectClothingHairItemMatchTokens(string itemUid, HashSet<string> tokens)
        {
            if (tokens == null || string.IsNullOrEmpty(itemUid)) return;

            void add(string s)
            {
                string n = NormalizeUndoMatchToken(s);
                if (!string.IsNullOrEmpty(n)) tokens.Add(n);
            }

            add(itemUid);

            string path = itemUid.Replace('\\', '/');
            int slash = path.LastIndexOf('/');
            if (slash >= 0 && slash < path.Length - 1)
                path = path.Substring(slash + 1);
            int dot = path.LastIndexOf('.');
            if (dot > 0)
                path = path.Substring(0, dot);
            add(path);

            string creator = "";
            int colon = itemUid.IndexOf(':');
            if (colon > 0)
                creator = itemUid.Substring(0, colon);
            if (!string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(path))
                add(creator + ":" + path);
        }

        private static bool IsClothingHairItemStorableId(string storableId)
        {
            if (string.IsNullOrEmpty(storableId)) return false;
            return storableId.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase) >= 0
                || storableId.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase)
                || storableId.IndexOf("hairItem#", StringComparison.OrdinalIgnoreCase) >= 0
                || storableId.StartsWith("hairItem", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExcludedPersonPresetStorable(string storableId)
        {
            if (string.IsNullOrEmpty(storableId)) return false;
            return string.Equals(storableId, "AppearancePresets", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storableId, "PosePresets", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storableId, "MorphPresets", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storableId, "SkinPresets", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storableId, "PluginPresets", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storableId, "AnimationPresets", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storableId, "FemaleBreastPhysicsPresets", StringComparison.OrdinalIgnoreCase);
        }

        private static bool StorableMatchesClothingHairItemTokens(string storableId, HashSet<string> itemTokens)
        {
            if (string.IsNullOrEmpty(storableId) || itemTokens == null || itemTokens.Count == 0)
                return false;

            string sidNorm = NormalizeUndoMatchToken(storableId);
            if (string.IsNullOrEmpty(sidNorm)) return false;

            foreach (string token in itemTokens)
            {
                if (string.IsNullOrEmpty(token)) continue;
                if (sidNorm.Contains(token)) return true;
            }

            return false;
        }

        private static bool ShouldCaptureClothingHairStorable(string storableId, HashSet<string> itemTokens)
        {
            if (string.IsNullOrEmpty(storableId)) return false;
            if (s_ClothingHairAggregateStorables.Contains(storableId)) return true;
            if (IsClothingHairItemStorableId(storableId)) return true;
            if (StorableMatchesClothingHairItemTokens(storableId, itemTokens)) return true;

            if (IsExcludedPersonPresetStorable(storableId)) return false;

            for (int i = 0; i < s_ClothingHairItemStorableSuffixes.Length; i++)
            {
                if (storableId.EndsWith(s_ClothingHairItemStorableSuffixes[i], StringComparison.OrdinalIgnoreCase)
                    && storableId.IndexOf(':') >= 0)
                {
                    return StorableMatchesClothingHairItemTokens(storableId, itemTokens)
                        || storableId.IndexOf("cloth", StringComparison.OrdinalIgnoreCase) >= 0
                        || storableId.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }

            return false;
        }

        internal static ClothingHairUndoState CaptureClothingHairUndoState(Atom atom)
        {
            var state = new ClothingHairUndoState
            {
                GeometryToggles = new Dictionary<string, bool>(),
                StorableSnapshots = new List<JSONClass>(),
            };
            if (atom == null) return state;

            HashSet<string> itemTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            JSONStorable geometry = null;
            try { geometry = atom.GetStorableByID("geometry"); } catch { }

            if (geometry != null)
            {
                List<string> names = null;
                try { names = geometry.GetBoolParamNames(); } catch { }
                if (names != null)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        string key = names[i];
                        if (string.IsNullOrEmpty(key)) continue;
                        if (!key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)
                            && !key.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        JSONStorableBool b = null;
                        try { b = geometry.GetBoolJSONParam(key); } catch { }
                        if (b != null) state.GeometryToggles[key] = b.val;

                        string prefix = key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)
                            ? "clothing:"
                            : "hair:";
                        CollectClothingHairItemMatchTokens(key.Substring(prefix.Length), itemTokens);
                    }
                }
            }

            HashSet<string> capturedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> storableIds = null;
            try { storableIds = atom.GetStorableIDs(); } catch { }

            if (storableIds != null)
            {
                for (int i = 0; i < storableIds.Count; i++)
                {
                    string sid = storableIds[i];
                    if (string.IsNullOrEmpty(sid)) continue;
                    if (!ShouldCaptureClothingHairStorable(sid, itemTokens)) continue;
                    if (!capturedIds.Add(sid)) continue;

                    JSONStorable s = null;
                    try { s = atom.GetStorableByID(sid); } catch { }
                    if (s == null) continue;

                    JSONClass snap = null;
                    try { snap = s.GetJSON(); } catch { }
                    if (snap != null) state.StorableSnapshots.Add(snap);
                }
            }

            return state;
        }

        internal static void RestoreClothingHairUndoState(Atom atom, ClothingHairUndoState state)
        {
            if (atom == null || state == null) return;

            if (state.GeometryToggles != null)
            {
                JSONStorable geometry = null;
                try { geometry = atom.GetStorableByID("geometry"); } catch { }

                if (geometry != null)
                {
                    foreach (KeyValuePair<string, bool> kvp in state.GeometryToggles)
                    {
                        JSONStorableBool b = null;
                        try { b = geometry.GetBoolJSONParam(kvp.Key); } catch { }
                        if (b != null) b.val = kvp.Value;
                    }

                    List<string> currentNames = null;
                    try { currentNames = geometry.GetBoolParamNames(); } catch { }
                    if (currentNames != null)
                    {
                        for (int i = 0; i < currentNames.Count; i++)
                        {
                            string key = currentNames[i];
                            if (string.IsNullOrEmpty(key)) continue;
                            if (!key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)
                                && !key.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            if (state.GeometryToggles.ContainsKey(key)) continue;

                            JSONStorableBool b = null;
                            try { b = geometry.GetBoolJSONParam(key); } catch { }
                            if (b != null) b.val = false;
                        }
                    }
                }
            }

            RestoreClothingHairStorableSnapshots(atom, state.StorableSnapshots);

            try
            {
                if (SuperController.singleton != null)
                {
                    SuperController.singleton.StartCoroutine(
                        DeferredRestoreClothingHairUndoStateCoroutine(atom, state));
                }
            }
            catch { }
        }

        private static void RestoreClothingHairStorableSnapshots(Atom atom, List<JSONClass> storableSnapshots)
        {
            if (atom == null || storableSnapshots == null) return;

            for (int i = 0; i < storableSnapshots.Count; i++)
            {
                JSONClass snap = storableSnapshots[i];
                if (snap == null || snap["id"] == null) continue;

                string sid = snap["id"].Value;
                if (string.IsNullOrEmpty(sid)) continue;

                JSONStorable s = null;
                try { s = atom.GetStorableByID(sid); } catch { }
                if (s == null) continue;

                try { s.RestoreFromJSON(snap); } catch { }
            }
        }

        private static IEnumerator DeferredRestoreClothingHairUndoStateCoroutine(
            Atom atom,
            ClothingHairUndoState state)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            if (atom == null || state == null) yield break;
            RestoreClothingHairStorableSnapshots(atom, state.StorableSnapshots);
        }

        internal static bool ClothingHairUndoStateContainsStorable(ClothingHairUndoState state, string storableId)
        {
            if (state == null || state.StorableSnapshots == null || string.IsNullOrEmpty(storableId))
                return false;

            for (int i = 0; i < state.StorableSnapshots.Count; i++)
            {
                JSONClass snap = state.StorableSnapshots[i];
                if (snap == null || snap["id"] == null) continue;
                if (string.Equals(snap["id"].Value, storableId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
