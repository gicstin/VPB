using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace VPB.src.util
{
    // Imports a source scene's person-linked CustomUnityAsset atoms as native CUA atoms on a target person.
    // VaM's own assetUrl restore self-drives the assetbundle load, so the asset appears on the first import
    // (unlike the CUA-as-clothing path, whose third-party plugin chain only wired up on a second activation).
    public static class CUAAtomImporter
    {
        private sealed class CuaPlan
        {
            public JSONClass Node;          // source CUA atom node (clone)
            public string SourceId;
            public string LinkAtomId;       // atom uid the control links to (person or another CUA)
            public string LinkBone;         // rigidbody name within the link atom
            public bool LinksToPerson;
            public SimpleTransform SourceWorld;
        }

        public static IEnumerator ImportLinkedCUAsAsAtoms(
            JSONClass sourceScene, string sourcePersonAtomId, Atom targetPerson, string sourceHostUid)
        {
            if (sourceScene == null || targetPerson == null || string.IsNullOrEmpty(sourcePersonAtomId))
                yield break;
            JSONArray atoms = sourceScene["atoms"] != null ? sourceScene["atoms"].AsArray : null;
            if (atoms == null) yield break;

            // Index every CUA atom in the scene by id so chained CUA->CUA links can be resolved.
            var cuaById = new Dictionary<string, JSONClass>(StringComparer.Ordinal);
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                if (a["type"] != null && a["type"].Value == "CustomUnityAsset")
                {
                    string id = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value)) ? a["id"].Value : ("CustomUnityAsset_" + i);
                    if (!cuaById.ContainsKey(id)) cuaById[id] = a;
                }
            }
            if (cuaById.Count == 0) { LogUtil.Log("[VPB][CUA] no CUA atoms in source scene."); yield break; }

            JSONClass sourcePerson = FindAtom(atoms, sourcePersonAtomId);

            // Collect CUAs reaching the selected person (directly, or transitively through CUA->CUA links).
            var plans = new List<CuaPlan>();
            foreach (var kvp in cuaById)
            {
                CuaPlan p = BuildPlan(kvp.Key, kvp.Value, sourcePersonAtomId, cuaById);
                if (p != null) plans.Add(p);
            }
            if (plans.Count == 0) { LogUtil.Log($"[VPB][CUA] no CUAs linked to '{sourcePersonAtomId}'."); yield break; }

            LogUtil.Log($"[VPB][CUA] atom import: {plans.Count} CUA(s) linked to '{sourcePersonAtomId}' -> target '{targetPerson.uid}'.");

            // sourceId -> spawned live atom (chained children re-link to the new parent uid) and -> source world
            // transform (chained children need the parent's source pose to preserve their relative offset).
            var spawned = new Dictionary<string, Atom>(StringComparer.Ordinal);
            var srcWorldById = new Dictionary<string, SimpleTransform>(StringComparer.Ordinal);
            foreach (CuaPlan p in plans) srcWorldById[p.SourceId] = p.SourceWorld;

            var pending = new List<CuaPlan>(plans);
            int guard = 0;
            while (pending.Count > 0 && guard++ < plans.Count + 2)
            {
                bool progressed = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    CuaPlan p = pending[i];
                    if (!p.LinksToPerson && !spawned.ContainsKey(p.LinkAtomId)) continue;

                    IEnumerator spawn = SpawnOne(p, sourcePerson, targetPerson, sourceHostUid, spawned, srcWorldById);
                    while (spawn.MoveNext()) yield return spawn.Current;

                    pending.RemoveAt(i);
                    progressed = true;
                }
                if (!progressed) break;  // remaining links can't be resolved (cycle / missing parent)
            }
            if (pending.Count > 0)
                LogUtil.LogWarning($"[VPB][CUA] {pending.Count} CUA(s) skipped (unresolved link chain).");
        }

        private static CuaPlan BuildPlan(string id, JSONClass node, string personId, Dictionary<string, JSONClass> cuaById)
        {
            string linkTo = ReadLinkTo(node);
            if (string.IsNullOrEmpty(linkTo)) return null;
            int colon = linkTo.IndexOf(':');
            if (colon <= 0) return null;
            string linkAtom = linkTo.Substring(0, colon);
            string linkBone = linkTo.Substring(colon + 1);

            bool toPerson = linkAtom == personId;
            if (!toPerson)
            {
                // Reaches the person only if the chain of CUA links eventually lands on it.
                if (!ChainReachesPerson(linkAtom, personId, cuaById)) return null;
            }
            return new CuaPlan
            {
                Node = node,
                SourceId = id,
                LinkAtomId = linkAtom,
                LinkBone = linkBone,
                LinksToPerson = toPerson,
                SourceWorld = ControlTransform(node),
            };
        }

        private static bool ChainReachesPerson(string atomId, string personId, Dictionary<string, JSONClass> cuaById)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string cur = atomId;
            for (int i = 0; i < 16 && cur != null && seen.Add(cur); i++)
            {
                if (cur == personId) return true;
                JSONClass cua;
                if (!cuaById.TryGetValue(cur, out cua)) return false;
                string lt = ReadLinkTo(cua);
                if (string.IsNullOrEmpty(lt)) return false;
                int c = lt.IndexOf(':');
                cur = c > 0 ? lt.Substring(0, c) : null;
            }
            return false;
        }

        private static IEnumerator SpawnOne(
            CuaPlan p, JSONClass sourcePerson, Atom targetPerson, string sourceHostUid,
            Dictionary<string, Atom> spawned, Dictionary<string, SimpleTransform> srcWorldById)
        {
            JSONClass node = JSON.Parse(JsonSerializationUtil.Serialize(p.Node, 1 << 16)).AsObject;
            if (node == null) yield break;
            if (!string.IsNullOrEmpty(sourceHostUid))
                JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(node, sourceHostUid);

            // Re-link: person link -> target uid (same bone); chained link -> the parent's new uid.
            string newLinkAtomId = p.LinksToPerson ? targetPerson.uid : spawned[p.LinkAtomId].uid;
            SetLinkTo(node, newLinkAtomId + ":" + p.LinkBone);

            // Diagnostics that localize a cold-test failure: srcWorld=identity means the control stored localPosition
            // (wrong transform read); empty/SELF assetUrl means the load won't self-drive.
            JSONClass assetSt = node.GetStorable("asset");
            string assetUrl = assetSt != null && assetSt["assetUrl"] != null ? assetSt["assetUrl"].Value : "(none)";
            LogUtil.Log($"[VPB][CUA] {p.SourceId}: srcCtrlPos={p.SourceWorld.Position} assetUrl={assetUrl}");

            // lastAddedAtom is non-public and CreateUID may dedupe our requested uid, so diff the atom set to find it.
            var before = new HashSet<string>(StringComparer.Ordinal);
            foreach (Atom a in SuperController.singleton.GetAtoms())
                if (a != null && a.type == "CustomUnityAsset") before.Add(a.uid);

            yield return SuperController.singleton.AddAtomByType("CustomUnityAsset", "imported_" + p.SourceId, false, false, false);

            Atom atom = null;
            foreach (Atom a in SuperController.singleton.GetAtoms())
                if (a != null && a.type == "CustomUnityAsset" && !before.Contains(a.uid)) { atom = a; break; }
            if (atom == null) { LogUtil.LogWarning($"[VPB][CUA] spawn failed for {p.SourceId}."); yield break; }

            try
            {
                // Restore asset/scale/material, correct the control's world transform, then LateRestore so linkTo
                // (modifyState:false) holds the corrected pose while VaM's assetUrl restore self-drives the load.
                atom.Restore(node, true, true, true);

                Vector3 worldPos; Quaternion worldRot;
                if (ComputeTargetPlacement(p, sourcePerson, targetPerson, spawned, srcWorldById, out worldPos, out worldRot)
                    && atom.mainController != null)
                {
                    atom.mainController.transform.position = worldPos;
                    atom.mainController.transform.rotation = worldRot;
                    LogUtil.Log($"[VPB][CUA] {p.SourceId}: placed at {worldPos}.");
                }
                else
                {
                    LogUtil.LogWarning($"[VPB][CUA] {p.SourceId}: placement skipped (bone/parent unresolved); stays at source location.");
                }
                atom.LateRestore(node, true, true, true);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB][CUA] restore failed for {p.SourceId}: {ex.Message}");
            }

            spawned[p.SourceId] = atom;
            LogUtil.Log($"[VPB][CUA] spawned '{atom.uid}' linkTo {newLinkAtomId}:{p.LinkBone}.");
        }

        // Person link: place at liveBone * boneRelativeOffset. Chain link: preserve offset to the parent CUA.
        private static bool ComputeTargetPlacement(
            CuaPlan p, JSONClass sourcePerson, Atom targetPerson,
            Dictionary<string, Atom> spawned, Dictionary<string, SimpleTransform> srcWorldById,
            out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = Vector3.zero; worldRot = Quaternion.identity;

            if (p.LinksToPerson)
            {
                SimpleTransform offset;
                if (sourcePerson == null
                    || !CUAConverter.TryComputeCuaBoneOffset(sourcePerson, p.SourceWorld, p.LinkBone, out offset))
                    return false;
                Rigidbody rb = SuperController.singleton.RigidbodyNameToRigidbody(targetPerson.uid + ":" + p.LinkBone);
                if (rb == null) return false;
                worldPos = rb.transform.TransformPoint(offset.Position);
                worldRot = rb.transform.rotation * offset.Rotation;
                return true;
            }

            // Chained: child world = parentLive * (parentSource^-1 * childSource).
            Atom parent;
            SimpleTransform parentSource;
            if (!spawned.TryGetValue(p.LinkAtomId, out parent) || parent == null || parent.mainController == null) return false;
            if (!srcWorldById.TryGetValue(p.LinkAtomId, out parentSource) || parentSource == null) return false;
            SimpleTransform rel = parentSource.InverseTransformPoint(p.SourceWorld);
            Transform pt = parent.mainController.transform;
            worldPos = pt.TransformPoint(rel.Position);
            worldRot = pt.rotation * rel.Rotation;
            return true;
        }

        private static JSONClass FindAtom(JSONArray atoms, string id)
        {
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                string pid = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value)) ? a["id"].Value : null;
                if (pid == id) return a;
            }
            return null;
        }

        private static string ReadLinkTo(JSONClass atom)
        {
            JSONClass control = atom.GetStorable("control");
            return control != null && control["linkTo"] != null ? control["linkTo"].Value : null;
        }

        // A CUA atom's world transform is on its "control" storable, not the atom node root.
        private static SimpleTransform ControlTransform(JSONClass atom)
        {
            JSONClass control = atom.GetStorable("control");
            return control != null ? control.GetTransform() : new SimpleTransform();
        }

        private static void SetLinkTo(JSONClass atom, string value)
        {
            JSONArray storables = atom["storables"] != null ? atom["storables"].AsArray : null;
            if (storables == null) return;
            foreach (JSONNode n in storables)
            {
                JSONClass s = n as JSONClass;
                if (s != null && s["id"] != null && s["id"].Value == "control")
                {
                    s["linkTo"] = value;
                    return;
                }
            }
        }
    }
}
