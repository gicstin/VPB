using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using VPB;

namespace VPB.src.util
{
    /// <summary>
    /// Enumerates and spawns non-Person atoms from a saved scene JSON. CUAs delegate to
    /// <see cref="CUAAtomImporter"/> so person-linked placement stays correct.
    /// </summary>
    public static class SceneAtomImporter
    {
        public struct SceneAtomEntry
        {
            public string Id;
            public string Type;
            public bool LinksToPerson;
            public bool UidCollision;
        }

        /// <summary>
        /// External atom UID referenced by selected import payload that does not resolve in the live scene
        /// and is not itself being imported in this batch.
        /// </summary>
        public struct BrokenUidRef
        {
            public string OriginalUid;
            public string SourceType;
            public string SuggestedLiveUid;
            /// <summary>Donor scene still has this atom — Remap modal can co-import it as Create new.</summary>
            public bool CanCreateFromSource;
        }

        /// <summary>
        /// Remap-choice sentinel: co-import the donor atom under its original UID (refs stay intact).
        /// Not a legal VaM atom id.
        /// </summary>
        public const string CreateNewUidSentinel = "__vpb_create_new__";

        private static bool s_importRunning;

        private static void LogAtom(string msg)
        {
            LogUtil.Log("[VPB][Atoms][import] " + msg);
        }

        private static void LogAtomWarn(string msg)
        {
            LogUtil.LogWarning("[VPB][Atoms][import] " + msg);
        }

        private static string DescribeAtomNode(JSONClass node)
        {
            if (node == null) return "(null node)";
            string id = node["id"] != null ? node["id"].Value : "?";
            string type = node["type"] != null ? node["type"].Value : "?";
            int storableCount = node["storables"] != null ? node["storables"].AsArray.Count : 0;
            JSONClass control = node.GetStorable("control");
            string linkTo = control != null && control.HasKey("linkTo") ? control["linkTo"].Value : null;
            string pos = control != null && control.HasKey("position")
                ? FormatVec3(control["position"])
                : (node.HasKey("position") ? FormatVec3(node["position"]) : null);
            string asset = ReadAssetHint(node, type);
            var parts = new List<string> { "id='" + id + "'", "type=" + type, "storables=" + storableCount };
            if (!string.IsNullOrEmpty(linkTo)) parts.Add("linkTo='" + linkTo + "'");
            if (!string.IsNullOrEmpty(pos)) parts.Add("pos=" + pos);
            if (!string.IsNullOrEmpty(asset)) parts.Add("asset='" + asset + "'");
            return string.Join(" ", parts.ToArray());
        }

        private static string FormatVec3(JSONNode vec)
        {
            if (vec == null) return null;
            JSONClass v = vec.AsObject;
            if (v == null) return null;
            return "(" + v["x"].AsFloat.ToString("F3") + "," + v["y"].AsFloat.ToString("F3") + "," + v["z"].AsFloat.ToString("F3") + ")";
        }

        private static string ReadAssetHint(JSONClass node, string type)
        {
            if (type != "CustomUnityAsset" && type != "SubScene") return null;
            JSONArray storables = node["storables"] != null ? node["storables"].AsArray : null;
            if (storables == null) return null;
            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i].AsObject;
                if (s == null) continue;
                if (s.HasKey("url"))
                {
                    string url = s["url"].Value;
                    if (!string.IsNullOrEmpty(url)) return url;
                }
                if (s.HasKey("assetUrl"))
                {
                    string url = s["assetUrl"].Value;
                    if (!string.IsNullOrEmpty(url)) return url;
                }
            }
            return null;
        }

        public static List<SceneAtomEntry> EnumerateSceneAtoms(JSONClass sourceScene, string sourcePersonAtomId)
        {
            var result = new List<SceneAtomEntry>();
            if (sourceScene == null) return result;
            JSONArray atoms = sourceScene["atoms"] != null ? sourceScene["atoms"].AsArray : null;
            if (atoms == null) return result;

            Dictionary<string, bool> cuaLinks = null;

            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                string type = a["type"] != null ? a["type"].Value : string.Empty;
                if (SceneUtils.IsPersonLikeAtomType(type)) continue;

                string id = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value))
                    ? a["id"].Value
                    : (type + "_" + i);

                bool linksToPerson = false;
                if (type == "CustomUnityAsset" && !string.IsNullOrEmpty(sourcePersonAtomId))
                {
                    if (cuaLinks == null)
                    {
                        cuaLinks = new Dictionary<string, bool>(StringComparer.Ordinal);
                        foreach (CUAAtomImporter.CuaEntry ce in CUAAtomImporter.EnumerateSceneCUAs(sourceScene, sourcePersonAtomId))
                            cuaLinks[ce.Id] = ce.LinksToPerson;
                    }
                    bool tagged;
                    if (cuaLinks.TryGetValue(id, out tagged)) linksToPerson = tagged;
                }

                bool collision = AtomAlreadyInScene(id);
                result.Add(new SceneAtomEntry
                {
                    Id = id,
                    Type = type,
                    LinksToPerson = linksToPerson,
                    UidCollision = collision
                });
            }
            return result;
        }

        /// <summary>
        /// True when the live scene already has an atom with this source uid, or a prior import variant (uid#2, …).
        /// </summary>
        public static bool AtomAlreadyInScene(string sourceAtomId)
        {
            if (string.IsNullOrEmpty(sourceAtomId)) return false;
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            if (sc.GetAtomByUid(sourceAtomId) != null) return true;
            foreach (Atom a in sc.GetAtoms())
            {
                if (a == null || string.IsNullOrEmpty(a.uid)) continue;
                if (string.Equals(a.uid, sourceAtomId, StringComparison.Ordinal)) return true;
                if (a.uid.StartsWith(sourceAtomId + "#", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static IEnumerator ImportSelectedAtoms(
            JSONClass sourceScene,
            string sourcePersonAtomId,
            Atom targetPerson,
            string sourceHostUid,
            HashSet<string> selectedIds,
            bool relativeToTargetPerson,
            bool skipExistingInScene)
        {
            return ImportSelectedAtoms(
                sourceScene, sourcePersonAtomId, targetPerson, sourceHostUid,
                selectedIds, relativeToTargetPerson, skipExistingInScene, null);
        }

        public static IEnumerator ImportSelectedAtoms(
            JSONClass sourceScene,
            string sourcePersonAtomId,
            Atom targetPerson,
            string sourceHostUid,
            HashSet<string> selectedIds,
            bool relativeToTargetPerson,
            bool skipExistingInScene,
            Dictionary<string, string> uidRemap)
        {
            if (sourceScene == null || selectedIds == null || selectedIds.Count == 0)
            {
                LogAtom("abort — sourceScene=" + (sourceScene != null ? "ok" : "null")
                    + " selectedIds=" + (selectedIds != null ? selectedIds.Count.ToString() : "null"));
                yield break;
            }

            LogAtom("start selected=" + selectedIds.Count
                + " skipExisting=" + skipExistingInScene
                + " relativeToTarget=" + relativeToTargetPerson
                + " sourcePerson='" + (sourcePersonAtomId ?? "") + "'"
                + " targetPerson='" + (targetPerson != null ? targetPerson.uid : "(none)") + "'"
                + " sourceHostUid='" + (sourceHostUid ?? "") + "'"
                + " uidRemap=" + (uidRemap != null ? uidRemap.Count.ToString() : "0"));

            while (s_importRunning)
                yield return null;
            s_importRunning = true;
            try
            {
                yield return ImportSelectedAtomsCore(
                    sourceScene, sourcePersonAtomId, targetPerson, sourceHostUid,
                    selectedIds, relativeToTargetPerson, skipExistingInScene, uidRemap);
            }
            finally
            {
                s_importRunning = false;
            }
        }

        /// <summary>
        /// Finds atom UIDs referenced by <paramref name="selectedIds"/> that are neither selected for import
        /// nor already present in the live scene. Used to drive the Remap Atom UIDs modal.
        /// Cold path — scene import only.
        /// </summary>
        public static List<BrokenUidRef> CollectBrokenExternalUidRefs(
            JSONClass sourceScene, HashSet<string> selectedIds)
        {
            var result = new List<BrokenUidRef>();
            if (sourceScene == null || selectedIds == null || selectedIds.Count == 0) return result;

            JSONArray atoms = sourceScene["atoms"] != null ? sourceScene["atoms"].AsArray : null;
            if (atoms == null) return result;

            var sourceIdToType = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                string id = ResolveAtomId(a, i);
                if (string.IsNullOrEmpty(id)) continue;
                string type = a["type"] != null ? a["type"].Value : string.Empty;
                sourceIdToType[id] = type ?? string.Empty;
            }

            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selectedIds)
            {
                JSONClass node = FindAtom(atoms, id);
                if (node == null) continue;
                CollectReferencedSourceAtomUids(node, sourceIdToType, referenced);
            }

            // Live uid → type for same-type suggestions.
            var liveByType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            SuperController sc = SuperController.singleton;
            if (sc != null)
            {
                foreach (Atom live in sc.GetAtoms())
                {
                    if (live == null || string.IsNullOrEmpty(live.uid) || string.IsNullOrEmpty(live.type)) continue;
                    List<string> list;
                    if (!liveByType.TryGetValue(live.type, out list))
                    {
                        list = new List<string>(4);
                        liveByType[live.type] = list;
                    }
                    list.Add(live.uid);
                }
            }

            foreach (string refUid in referenced)
            {
                if (string.IsNullOrEmpty(refUid)) continue;
                if (selectedIds.Contains(refUid)) continue;
                if (AtomAlreadyInScene(refUid)) continue;

                string srcType = string.Empty;
                sourceIdToType.TryGetValue(refUid, out srcType);

                string suggested = null;
                if (!string.IsNullOrEmpty(srcType))
                {
                    List<string> sameType;
                    if (liveByType.TryGetValue(srcType, out sameType) && sameType != null && sameType.Count == 1)
                        suggested = sameType[0];
                }

                result.Add(new BrokenUidRef
                {
                    OriginalUid = refUid,
                    SourceType = srcType ?? string.Empty,
                    SuggestedLiveUid = suggested,
                    CanCreateFromSource = sourceIdToType.ContainsKey(refUid)
                });
            }

            result.Sort((a, b) => string.Compare(a.OriginalUid, b.OriginalUid, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static void CollectReferencedSourceAtomUids(
            JSONNode node, Dictionary<string, string> sourceIdToType, HashSet<string> sink)
        {
            if (node == null || sourceIdToType == null || sink == null) return;

            JSONArray ja = node as JSONArray;
            if (ja != null)
            {
                for (int i = 0; i < ja.Count; i++)
                    CollectReferencedSourceAtomUids(ja[i], sourceIdToType, sink);
                return;
            }

            JSONClass jc = node as JSONClass;
            if (jc != null)
            {
                foreach (string key in jc.Keys)
                {
                    JSONNode child = jc[key];
                    if (child == null) continue;
                    JSONArray childArr = child as JSONArray;
                    JSONClass childObj = child as JSONClass;
                    if (childArr == null && childObj == null)
                    {
                        CollectReferencedUidFromLeaf(child, key, sourceIdToType, sink);
                        continue;
                    }
                    CollectReferencedSourceAtomUids(child, sourceIdToType, sink);
                }
                return;
            }

            CollectReferencedUidFromLeaf(node, null, sourceIdToType, sink);
        }

        private static void CollectReferencedUidFromLeaf(
            JSONNode leaf, string key, Dictionary<string, string> sourceIdToType, HashSet<string> sink)
        {
            if (leaf == null) return;
            string val;
            try { val = leaf.Value; }
            catch { return; }
            if (string.IsNullOrEmpty(val)) return;

            // Compound atom:storable — always consider the atom prefix.
            int colon = val.IndexOf(':');
            if (colon > 0)
            {
                string prefix = val.Substring(0, colon);
                if (sourceIdToType.ContainsKey(prefix))
                    sink.Add(prefix);
            }

            // Bare atom equals — skip type/id/url keys so "type":"Person" is not a Person ref.
            if (JSONExtensions.JsonKeyIsNonAtomUidRef(key)) return;
            if (sourceIdToType.ContainsKey(val))
                sink.Add(val);
        }

        private static void ApplyUidRemapToNode(JSONClass node, Dictionary<string, string> uidRemap)
        {
            if (node == null || uidRemap == null || uidRemap.Count == 0) return;
            // Two-phase via temps so A→B + B→C cannot collide mid-walk.
            List<string> fromList = new List<string>(uidRemap.Count);
            List<string> toList = new List<string>(uidRemap.Count);
            foreach (KeyValuePair<string, string> kv in uidRemap)
            {
                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                if (string.Equals(kv.Key, kv.Value, StringComparison.Ordinal)) continue;
                fromList.Add(kv.Key);
                toList.Add(kv.Value);
            }
            for (int i = 0; i < fromList.Count; i++)
            {
                string tempId = "__vpb_import_ren_" + i.ToString();
                JSONExtensions.RemapAtomUidReferencesKeyAwareMutable(node, fromList[i], tempId);
            }
            for (int i = 0; i < fromList.Count; i++)
            {
                string tempId = "__vpb_import_ren_" + i.ToString();
                JSONExtensions.RemapAtomUidReferencesKeyAwareMutable(node, tempId, toList[i]);
            }
        }

        private static HashSet<string> FilterSkipExisting(HashSet<string> selectedIds)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selectedIds)
            {
                if (AtomAlreadyInScene(id))
                    LogAtom("skip existing in scene: '" + id + "'");
                else
                    kept.Add(id);
            }
            if (kept.Count < selectedIds.Count)
                LogAtom("after skip-existing filter: " + kept.Count + "/" + selectedIds.Count + " remain");
            return kept;
        }

        private static IEnumerator ImportSelectedAtomsCore(
            JSONClass sourceScene,
            string sourcePersonAtomId,
            Atom targetPerson,
            string sourceHostUid,
            HashSet<string> selectedIds,
            bool relativeToTargetPerson,
            bool skipExistingInScene,
            Dictionary<string, string> uidRemap)
        {
            JSONArray atoms = sourceScene["atoms"] != null ? sourceScene["atoms"].AsArray : null;
            if (atoms == null)
            {
                LogAtomWarn("abort — source scene has no atoms[] array.");
                yield break;
            }

            HashSet<string> idsToImport = skipExistingInScene
                ? FilterSkipExisting(selectedIds)
                : selectedIds;
            if (idsToImport.Count == 0)
            {
                LogAtom("nothing to import — all selected atoms already exist in scene.");
                yield break;
            }

            var cuaIds = new HashSet<string>(StringComparer.Ordinal);
            var genericOrder = new List<string>();
            var missingFromScene = new List<string>();
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                string id = ResolveAtomId(a, i);
                if (!idsToImport.Contains(id)) continue;
                string type = a["type"] != null ? a["type"].Value : string.Empty;
                if (type == "CustomUnityAsset") cuaIds.Add(id);
                else genericOrder.Add(id);
            }
            foreach (string id in idsToImport)
            {
                if (!cuaIds.Contains(id) && !genericOrder.Contains(id))
                    missingFromScene.Add(id);
            }
            if (missingFromScene.Count > 0)
                LogAtomWarn("selected id(s) not found in scene JSON: " + string.Join(", ", missingFromScene.ToArray()));

            LogAtom("split selected=" + idsToImport.Count + " cua=" + cuaIds.Count + " generic=" + genericOrder.Count);

            if (cuaIds.Count > 0)
            {
                if (targetPerson != null && !string.IsNullOrEmpty(sourcePersonAtomId))
                {
                    LogAtom("delegating " + cuaIds.Count + " CUA(s) to CUAAtomImporter: "
                        + string.Join(", ", new List<string>(cuaIds).ToArray()));
                    yield return CUAAtomImporter.ImportSelectedCUAsAsAtoms(
                        sourceScene, sourcePersonAtomId, targetPerson, sourceHostUid,
                        cuaIds, relativeToTargetPerson, replaceExisting: false);
                }
                else
                {
                    LogAtomWarn("skipping " + cuaIds.Count + " CUA(s) — need sourcePerson + targetPerson (sourcePerson='"
                        + (sourcePersonAtomId ?? "") + "' target='" + (targetPerson != null ? targetPerson.uid : "(none)") + "').");
                }
            }

            if (genericOrder.Count == 0)
            {
                LogAtom("no generic atoms to spawn.");
                yield break;
            }

            JSONClass sourcePerson = FindAtom(atoms, sourcePersonAtomId);
            SimpleTransform srcPersonRoot = ReadPersonRootWorld(sourcePerson);
            SimpleTransform destPersonRoot = targetPerson != null && targetPerson.mainController != null
                ? new SimpleTransform(targetPerson.mainController.transform.position, targetPerson.mainController.transform.rotation)
                : null;

            LogAtom("relative placement: sourcePerson=" + (sourcePerson != null ? sourcePersonAtomId : "(missing)")
                + " srcRoot=" + (srcPersonRoot != null ? "ok" : "missing")
                + " destRoot=" + (destPersonRoot != null ? "ok" : "missing")
                + " enabled=" + relativeToTargetPerson);

            if (targetPerson != null)
                yield return CUAAtomImporter.WaitForPersonSettled(targetPerson);

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string id in genericOrder)
            {
                if (skipExistingInScene)
                {
                    if (AtomAlreadyInScene(id))
                    {
                        LogAtom("idMap skip existing: '" + id + "'");
                        continue;
                    }
                    idMap[id] = id;
                    LogAtom("idMap '" + id + "' -> '" + id + "' (keep uid)");
                }
                else
                {
                    string liveId = MakeUniqueLiveId(id, idMap);
                    idMap[id] = liveId;
                    if (liveId != id)
                        LogAtom("idMap '" + id + "' -> '" + liveId + "' (uid collision)");
                    else
                        LogAtom("idMap '" + id + "' -> '" + liveId + "'");
                }
            }

            JSONArray outAtoms = new JSONArray();
            int prepareFailed = 0;
            foreach (string id in genericOrder)
            {
                string liveId;
                if (!idMap.TryGetValue(id, out liveId))
                {
                    LogAtom("prepare skip '" + id + "' — not in idMap (likely already in scene).");
                    prepareFailed++;
                    continue;
                }

                JSONClass srcNode = FindAtom(atoms, id);
                if (srcNode == null)
                {
                    LogAtomWarn("prepare fail '" + id + "' — atom node missing from scene JSON.");
                    prepareFailed++;
                    continue;
                }

                string srcType = srcNode["type"] != null ? srcNode["type"].Value : "?";
                LogAtom("prepare '" + id + "' " + DescribeAtomNode(srcNode));

                JSONClass node;
                try
                {
                    node = JSON.Parse(JsonSerializationUtil.Serialize(srcNode, 1 << 16)).AsObject;
                }
                catch (Exception ex)
                {
                    LogAtomWarn("prepare fail '" + id + "' — serialize/parse: " + ex.Message);
                    prepareFailed++;
                    continue;
                }
                if (node == null)
                {
                    LogAtomWarn("prepare fail '" + id + "' — cloned node null after parse.");
                    prepareFailed++;
                    continue;
                }

                if (!string.IsNullOrEmpty(sourceHostUid))
                {
                    JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(node, sourceHostUid);
                    LogAtom("prepare '" + id + "' — rewrote self-prefix with hostUid '" + sourceHostUid + "'");
                }

                if (uidRemap != null && uidRemap.Count > 0)
                {
                    ApplyUidRemapToNode(node, uidRemap);
                    LogAtom("prepare '" + id + "' — applied " + uidRemap.Count + " uid remap(s)");
                }

                node["id"] = liveId;

                if (relativeToTargetPerson && srcPersonRoot != null && destPersonRoot != null)
                {
                    if (TryRepositionRelativeToPerson(node, srcPersonRoot, destPersonRoot))
                        LogAtom("prepare '" + id + "' — repositioned relative to target person -> liveId='" + liveId + "'");
                    else
                        LogAtomWarn("prepare '" + id + "' — relative reposition skipped (no control position/rotation).");
                }
                else if (relativeToTargetPerson)
                {
                    LogAtomWarn("prepare '" + id + "' — relative reposition skipped (missing src/dest person root).");
                }

                outAtoms.Add(node);
                LogAtom("prepare ok '" + id + "' -> live '" + liveId + "' type=" + srcType);
            }

            if (outAtoms.Count == 0)
            {
                LogAtomWarn("nothing to spawn — prepared=0 failed=" + prepareFailed + " generic=" + genericOrder.Count);
                yield break;
            }

            LogAtom("spawning " + outAtoms.Count + " generic atom(s) (prepareFailed=" + prepareFailed + ").");
            yield return SpawnAndRestoreAtoms(outAtoms);
        }

        private static IEnumerator SpawnAndRestoreAtoms(JSONArray outAtoms)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null || outAtoms == null)
            {
                LogAtomWarn("spawn abort — sc=" + (sc != null ? "ok" : "null") + " outAtoms=" + (outAtoms != null ? outAtoms.Count.ToString() : "null"));
                yield break;
            }

            var created = new List<KeyValuePair<Atom, JSONClass>>();
            int spawnSkipped = 0;
            int spawnFailed = 0;
            foreach (JSONNode n in outAtoms)
            {
                JSONClass node = n as JSONClass;
                if (node == null)
                {
                    LogAtomWarn("spawn skip — outAtoms entry not JSONClass.");
                    spawnSkipped++;
                    continue;
                }
                string type = node["type"] != null && !string.IsNullOrEmpty(node["type"].Value)
                    ? node["type"].Value
                    : "Empty";
                string uid = node["id"] != null ? node["id"].Value : null;
                if (string.IsNullOrEmpty(uid))
                {
                    LogAtomWarn("spawn skip — " + DescribeAtomNode(node) + " (missing id).");
                    spawnSkipped++;
                    continue;
                }

                Atom atom = sc.GetAtomByUid(uid);
                if (atom != null)
                {
                    LogAtom("spawn skip '" + uid + "' — already in scene (type=" + atom.type + ").");
                    spawnSkipped++;
                    continue;
                }

                LogAtom("spawn AddAtomByType type='" + type + "' uid='" + uid + "' " + DescribeAtomNode(node)
                    + " isLoading=" + sc.isLoading);
                yield return sc.AddAtomByType(type, uid);
                atom = sc.GetAtomByUid(uid);
                if (atom == null)
                {
                    LogAtomWarn("spawn FAIL '" + uid + "' type='" + type + "' — AddAtomByType finished but atom not found"
                        + " isLoading=" + sc.isLoading + " " + DescribeAtomNode(node));
                    spawnFailed++;
                    continue;
                }

                try { atom.SetOn(true); } catch (Exception ex) { LogAtomWarn("spawn '" + uid + "' SetOn: " + ex.Message); }
                LogAtom("spawn OK '" + uid + "' type='" + atom.type + "' on=" + atom.on);
                created.Add(new KeyValuePair<Atom, JSONClass>(atom, node));
            }

            if (created.Count == 0)
            {
                LogAtomWarn("no atoms spawned — requested=" + outAtoms.Count + " skipped=" + spawnSkipped + " failed=" + spawnFailed);
                yield break;
            }

            LogAtom("restore pipeline for " + created.Count + " atom(s) (skipped=" + spawnSkipped + " failed=" + spawnFailed + ").");
            foreach (var kv in created)
            {
                try
                {
                    kv.Key.PreRestore();
                    LogAtom("restore '" + kv.Key.uid + "' PreRestore ok");
                }
                catch (Exception ex) { LogAtomWarn("restore '" + kv.Key.uid + "' PreRestore: " + ex.Message); }
            }
            foreach (var kv in created)
            {
                try
                {
                    kv.Key.RestoreTransform(kv.Value);
                    LogAtom("restore '" + kv.Key.uid + "' RestoreTransform ok");
                }
                catch (Exception ex) { LogAtomWarn("restore '" + kv.Key.uid + "' RestoreTransform: " + ex.Message); }
            }
            foreach (var kv in created)
            {
                try
                {
                    kv.Key.Restore(kv.Value);
                    LogAtom("restore '" + kv.Key.uid + "' Restore ok");
                }
                catch (Exception ex) { LogAtomWarn("restore '" + kv.Key.uid + "' Restore: " + ex.Message); }
            }
            foreach (var kv in created)
            {
                try
                {
                    kv.Key.LateRestore(kv.Value);
                    LogAtom("restore '" + kv.Key.uid + "' LateRestore ok");
                }
                catch (Exception ex) { LogAtomWarn("restore '" + kv.Key.uid + "' LateRestore: " + ex.Message); }
            }
            foreach (var kv in created)
            {
                try
                {
                    kv.Key.PostRestore();
                    LogAtom("restore '" + kv.Key.uid + "' PostRestore ok");
                }
                catch (Exception ex) { LogAtomWarn("restore '" + kv.Key.uid + "' PostRestore: " + ex.Message); }
            }

            LogAtom("done — spawned=" + created.Count + " requested=" + outAtoms.Count
                + " skipped=" + spawnSkipped + " failed=" + spawnFailed);
        }

        private static bool TryRepositionRelativeToPerson(
            JSONClass node, SimpleTransform srcPersonRoot, SimpleTransform destPersonRoot)
        {
            JSONClass control = node.GetStorable("control");
            if (control == null || !control.HasKey("position") || !control.HasKey("rotation")) return false;

            SimpleTransform srcCtrlWorld = SimpleTransform.FromJson(control, "position", "rotation");
            SimpleTransform localToPerson = srcPersonRoot.InverseTransformPoint(srcCtrlWorld);
            SimpleTransform destWorld = destPersonRoot.TransformPoint(localToPerson);
            return WriteControlWorld(node, destWorld);
        }

        private static bool WriteControlWorld(JSONClass node, SimpleTransform world)
        {
            JSONClass control = node.GetStorable("control");
            if (control == null) return false;
            Vector3 pos = world.Position;
            Vector3 eul = world.Rotation.eulerAngles;
            WriteVec3(control, "position", pos);
            WriteVec3(control, "rotation", eul);
            WriteVec3(node, "position", pos);
            WriteVec3(node, "rotation", eul);
            if (node.HasKey("containerPosition")) WriteVec3(node, "containerPosition", pos);
            if (node.HasKey("containerRotation")) WriteVec3(node, "containerRotation", eul);
            return true;
        }

        private static void WriteVec3(JSONClass node, string key, Vector3 v)
        {
            JSONClass vec = new JSONClass();
            vec["x"].AsFloat = v.x;
            vec["y"].AsFloat = v.y;
            vec["z"].AsFloat = v.z;
            node[key] = vec;
        }

        private static SimpleTransform ReadPersonRootWorld(JSONClass person)
        {
            if (person == null) return null;
            JSONClass control = person.GetStorable("control");
            if (control == null || !control.HasKey("position") || !control.HasKey("rotation")) return null;
            return SimpleTransform.FromJson(control, "position", "rotation");
        }

        private static string MakeUniqueLiveId(string desired, Dictionary<string, string> alreadyAssigned)
        {
            SuperController sc = SuperController.singleton;
            bool Taken(string id) =>
                (sc != null && sc.GetAtomByUid(id) != null) || alreadyAssigned.ContainsValue(id);
            if (!Taken(desired)) return desired;
            for (int n = 2; n < 10000; n++)
            {
                string candidate = desired + "#" + n;
                if (!Taken(candidate)) return candidate;
            }
            return desired + "#" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static JSONClass FindAtom(JSONArray atoms, string id)
        {
            if (atoms == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                if (ResolveAtomId(a, i) == id) return a;
            }
            return null;
        }

        private static string ResolveAtomId(JSONClass a, int index)
        {
            string type = a["type"] != null ? a["type"].Value : "Atom";
            return (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value))
                ? a["id"].Value
                : (type + "_" + index);
        }
    }
}
