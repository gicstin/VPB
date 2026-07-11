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
                + " sourceHostUid='" + (sourceHostUid ?? "") + "'");

            while (s_importRunning)
                yield return null;
            s_importRunning = true;
            try
            {
                yield return ImportSelectedAtomsCore(
                    sourceScene, sourcePersonAtomId, targetPerson, sourceHostUid,
                    selectedIds, relativeToTargetPerson, skipExistingInScene);
            }
            finally
            {
                s_importRunning = false;
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
            bool skipExistingInScene)
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
