using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using SimpleJSON;
using UnityEngine;

namespace VPB.src.util
{
    public static class CUAAtomImporter
    {
        private sealed class CuaPlan
        {
            public JSONClass Node;          // source CUA atom node
            public string SourceId;
            public string LinkAtomId;       // atom uid the control links to (person or another CUA)
            public string LinkBone;         // rigidbody name within the link atom
            public bool LinksToPerson;
        }

        private static readonly Dictionary<string, HashSet<string>> s_importedByTarget =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

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
            float sourceScale = ReadPersonScale(sourcePerson);

            // Collect CUAs reaching the selected person (directly, or transitively through CUA->CUA links).
            var plans = new List<CuaPlan>();
            foreach (var kvp in cuaById)
            {
                CuaPlan p = BuildPlan(kvp.Key, kvp.Value, sourcePersonAtomId, cuaById);
                if (p != null) plans.Add(p);
            }
            if (plans.Count == 0) { LogUtil.Log($"[VPB][CUA] no CUAs linked to '{sourcePersonAtomId}'."); yield break; }

            LogUtil.Log($"[VPB][CUA] atom import: {plans.Count} CUA(s) linked to '{sourcePersonAtomId}' -> target '{targetPerson.uid}'.");

            // Settle the target skeleton so bone-based placement isn't sampled mid-morph (first import too low).
            yield return WaitForPersonSettled(targetPerson);

            // Re-import replaces rather than stacks: drop any prior imports of these same CUAs on this target.
            RemovePriorImports(plans, targetPerson);

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CuaPlan p in plans)
                idMap[p.SourceId] = MakeUniqueLiveId(p.SourceId, idMap);

            HashSet<string> importedSet;
            if (!s_importedByTarget.TryGetValue(targetPerson.uid, out importedSet))
            {
                importedSet = new HashSet<string>(StringComparer.Ordinal);
                s_importedByTarget[targetPerson.uid] = importedSet;
            }
            foreach (string liveId in idMap.Values) importedSet.Add(liveId);

            var srcBoneWorld = new Dictionary<string, SimpleTransform>(StringComparer.Ordinal);
            var placements = new List<Placement>();

            JSONArray outAtoms = new JSONArray();
            foreach (CuaPlan p in plans)
            {
                JSONClass node = JSON.Parse(JsonSerializationUtil.Serialize(p.Node, 1 << 16)).AsObject;
                if (node == null) continue;
                if (!string.IsNullOrEmpty(sourceHostUid))
                    JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(node, sourceHostUid);

                node["id"] = idMap[p.SourceId];

                // The person bone this CUA is (transitively) anchored to.
                string anchorBone = ResolveAnchorBone(p, sourcePersonAtomId, cuaById);

                bool haveOffset = false;
                SimpleTransform localOffset = new SimpleTransform();
                SimpleTransform srcBoneW = null;
                if (anchorBone != null)
                {
                    JSONClass control = node.GetStorable("control");
                    SimpleTransform boneWorld;
                    if (control != null && control.HasKey("position") && control.HasKey("rotation")
                        && TryGetSourceBoneWorldCached(sourcePerson, anchorBone, sourceScale, srcBoneWorld, out boneWorld))
                    {
                        SimpleTransform controlWorld = SimpleTransform.FromJson(control, "position", "rotation");
                        localOffset = boneWorld.InverseTransformPoint(controlWorld);
                        srcBoneW = boneWorld;
                        haveOffset = true;
                    }
                }

                if (!haveOffset)
                {
                    LogUtil.LogWarning($"[VPB][CUA] {p.SourceId}: could not resolve anchor bone '{anchorBone}'; skipping.");
                    continue;
                }

                // localOffset is in the source DAZBone (joint) frame, so bake against the live DAZBone.
                Transform liveBone = FindLiveBoneTransform(targetPerson, anchorBone);
                SimpleTransform destControlWorld = null;
                if (liveBone != null)
                    destControlWorld = new SimpleTransform(
                        liveBone.TransformPoint(localOffset.Position), liveBone.rotation * localOffset.Rotation);

                StripNativeLink(node);
                StripConflictingLinkPlugins(node);
                bool placed = destControlWorld != null && WriteControlWorld(node, destControlWorld);

                placements.Add(new Placement { LiveId = idMap[p.SourceId], AnchorBone = anchorBone, LocalOffset = localOffset });

                LogUtil.Log($"[VPB][CUA] {p.SourceId}: anchor '{anchorBone}' srcScale={sourceScale:F4} local={localOffset.Position} "
                    + $"srcReconBone={(srcBoneW != null ? srcBoneW.Position.ToString("F4") : "?")} "
                    + $"liveDestBone={(liveBone != null ? liveBone.position.ToString("F4") : "MISSING")} placed={placed}.");
                outAtoms.Add(node);
            }

            if (outAtoms.Count == 0) { LogUtil.Log("[VPB][CUA] nothing to import after anchoring."); yield break; }

            JSONClass root = new JSONClass();
            root["atoms"] = outAtoms;
            string tempPath = SceneLoadingUtils.WriteTempSceneForMergeLoad(root, "vpb_cua");
            if (string.IsNullOrEmpty(tempPath))
            {
                LogUtil.LogWarning("[VPB][CUA] failed to write temp CUA scene; import aborted.");
                yield break;
            }

            bool ok = SceneLoadingUtils.LoadScene(UI.NormalizePath(tempPath), true);
            LogUtil.Log($"[VPB][CUA] merge-loaded {outAtoms.Count} CUA atom(s) from '{tempPath}' (ok={ok}).");

            // Attach each spawned CUA to its target bone with a native VaM ParentLink so it rides the body.
            yield return EstablishParentLinks(placements, targetPerson);
        }

        private sealed class Placement
        {
            public string LiveId;
            public string AnchorBone;
            public SimpleTransform LocalOffset;
        }

        private static IEnumerator WaitForPersonSettled(Atom person)
        {
            if (person == null) yield break;
            Transform probe = FindLiveBoneTransform(person, "head") ?? FindLiveBoneTransform(person, "hip");
            if (probe == null) { for (int i = 0; i < 10; i++) yield return null; yield break; }
            Vector3 last = probe.position;
            int stable = 0;
            for (int i = 0; i < 180 && stable < 6; i++)
            {
                yield return null;
                Vector3 cur = probe.position;
                if ((cur - last).sqrMagnitude < 1e-8f) stable++; else stable = 0;
                last = cur;
            }
        }

        private static IEnumerator EstablishParentLinks(List<Placement> placements, Atom target)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null || placements == null) yield break;

            int guard = 0;
            while (sc.isLoading && guard++ < 600) yield return null;
            for (int i = 0; i < 10; i++) yield return null;

            foreach (Placement pl in placements)
            {
                Atom cua = null;
                for (int i = 0; i < 300 && cua == null; i++)
                {
                    cua = sc.GetAtomByUid(pl.LiveId);
                    if (cua == null) yield return null;
                }
                if (cua == null) { LogUtil.LogWarning($"[VPB][CUA] spawned atom '{pl.LiveId}' not found; cannot parent-link."); continue; }

                FreeControllerV3 mc = cua.mainController;
                Transform boneTf = FindLiveBoneTransform(target, pl.AnchorBone);
                Rigidbody boneRB = FindLiveBoneRigidbody(target, pl.AnchorBone);
                if (mc == null || boneRB == null)
                {
                    LogUtil.LogWarning($"[VPB][CUA] '{pl.LiveId}': mc/boneRB missing (bone '{pl.AnchorBone}'); not linked.");
                    continue;
                }

                int held = 0;
                for (int i = 0; i < 180 && held < 5; i++)
                {
                    if (mc.linkToRB != boneRB
                        || mc.currentPositionState != FreeControllerV3.PositionState.ParentLink
                        || mc.currentRotationState != FreeControllerV3.RotationState.ParentLink)
                    {
                        mc.transform.position = boneTf != null ? boneTf.TransformPoint(pl.LocalOffset.Position) : mc.transform.position;
                        mc.transform.rotation = boneTf != null ? boneTf.rotation * pl.LocalOffset.Rotation : mc.transform.rotation;
                        mc.SelectLinkToRigidbody(boneRB, FreeControllerV3.SelectLinkState.PositionAndRotation);
                        mc.currentPositionState = FreeControllerV3.PositionState.ParentLink;
                        mc.currentRotationState = FreeControllerV3.RotationState.ParentLink;
                        held = 0;
                    }
                    else held++;
                    yield return null;
                }
                LogUtil.Log($"[VPB][CUA] '{pl.LiveId}': ParentLink -> '{target.uid}':{boneRB.name} held={held} at {mc.transform.position.ToString("F4")}.");
            }
        }

        private static void RemovePriorImports(List<CuaPlan> plans, Atom target)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null || target == null) return;
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CuaPlan p in plans) sourceIds.Add(p.SourceId);
            HashSet<string> priorIds;
            s_importedByTarget.TryGetValue(target.uid, out priorIds);

            var toRemove = new List<Atom>();
            foreach (Atom a in sc.GetAtoms())
            {
                if (a == null || a.type != "CustomUnityAsset") continue;
                string uid = a.uid;
                if (priorIds != null && priorIds.Contains(uid)) { toRemove.Add(a); continue; }
                int hash = uid.IndexOf('#');
                string baseUid = hash > 0 ? uid.Substring(0, hash) : uid;
                if (!sourceIds.Contains(uid) && !sourceIds.Contains(baseUid)) continue;
                if (WasImportedByUs(a, target)) toRemove.Add(a);
            }
            if (priorIds != null) foreach (Atom a in toRemove) priorIds.Remove(a.uid);
            foreach (Atom a in toRemove)
            {
                try { sc.RemoveAtom(a); }
                catch (Exception ex) { LogUtil.LogWarning("[VPB][CUA] remove prior import " + a.uid + " failed: " + ex.Message); }
            }
            if (toRemove.Count > 0) LogUtil.Log($"[VPB][CUA] replaced {toRemove.Count} prior import(s) on '{target.uid}'.");
        }

        private static bool WasImportedByUs(Atom cua, Atom target)
        {
            try
            {
                List<string> ids = cua.GetStorableIDs();
                if (ids != null)
                    for (int i = 0; i < ids.Count; i++)
                        if (ids[i] != null && ids[i].IndexOf("CUAParentLink", StringComparison.Ordinal) >= 0) return true;
            }
            catch { }
            Atom cur = cua;
            var seen = new HashSet<Atom>();
            for (int i = 0; i < 16 && cur != null && seen.Add(cur); i++)
            {
                FreeControllerV3 fc = cur.mainController;
                Rigidbody rb = fc != null ? fc.linkToRB : null;
                if (rb == null) return false;
                Atom linked = rb.GetComponentInParent<Atom>();
                if (linked == null) return false;
                if (linked == target) return true;
                if (linked.type != "CustomUnityAsset") return false;
                cur = linked;
            }
            return false;
        }

        private static bool TryGetSourceBoneWorldCached(
            JSONClass sourcePerson, string bone, float sourceScale, Dictionary<string, SimpleTransform> cache, out SimpleTransform boneWorld)
        {
            if (cache.TryGetValue(bone, out boneWorld)) return boneWorld != null;
            SimpleTransform result = null;
            if (sourcePerson != null && CUAConverter.TryGetSourceBoneWorld(sourcePerson, bone, sourceScale, out SimpleTransform w))
                result = w;
            cache[bone] = result;
            boneWorld = result;
            return result != null;
        }

        // Read the source person's uniform scale (rescaleObject.scale). Defaults to 1 when absent/invalid.
        private static float ReadPersonScale(JSONClass person)
        {
            if (person == null) return 1f;
            JSONClass rescale = person.GetStorable("rescaleObject");
            if (rescale != null && rescale.HasKey("scale"))
            {
                float s = rescale["scale"].AsFloat;
                if (s > 0.0001f) return s;
            }
            return 1f;
        }

        private static void StripNativeLink(JSONClass node)
        {
            JSONArray storables = node["storables"] != null ? node["storables"].AsArray : null;
            if (storables == null) return;
            foreach (JSONNode n in storables)
            {
                JSONClass s = n as JSONClass;
                if (s == null || s["id"] == null || s["id"].Value != "control") continue;
                s["positionState"] = "On";
                s["rotationState"] = "On";
                s.Remove("linkTo");
                return;
            }
        }

        private static readonly string[] ConflictingLinkPluginMarkers = new string[]
        {
            "ParentHoldLink", "ParentLink", "ParentHold", "LinkToAtom", "HoldLink"
        };

        private static bool IsConflictingLinkPlugin(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            // Never strip our own shim.
            if (s.IndexOf("CUAParentLink", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            for (int i = 0; i < ConflictingLinkPluginMarkers.Length; i++)
                if (s.IndexOf(ConflictingLinkPluginMarkers[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static void StripConflictingLinkPlugins(JSONClass node)
        {
            JSONArray storables = node["storables"] != null ? node["storables"].AsArray : null;
            if (storables == null) return;

            JSONClass pluginManager = null;
            foreach (JSONNode n in storables)
            {
                JSONClass s = n as JSONClass;
                if (s != null && s.HasKey("id") && s["id"].Value == "PluginManager") { pluginManager = s; break; }
            }
            if (pluginManager == null || !pluginManager.HasKey("plugins")) return;

            JSONClass plugins = pluginManager["plugins"].AsObject;
            // Collect the plugin#<N> slots whose url is a conflicting positioner.
            List<string> killSlots = new List<string>();
            foreach (KeyValuePair<string, JSONNode> kv in plugins.AsObject)
            {
                if (kv.Key != null && kv.Key.StartsWith("plugin#") && IsConflictingLinkPlugin(kv.Value.Value))
                    killSlots.Add(kv.Key);
            }
            if (killSlots.Count == 0) return;
            foreach (string slot in killSlots) plugins.Remove(slot);

            // Remove the matching param storables ("plugin#<N>_<Type>") for the killed slots.
            for (int i = storables.Count - 1; i >= 0; i--)
            {
                JSONClass s = storables[i] as JSONClass;
                if (s == null || !s.HasKey("id")) continue;
                string id = s["id"].Value;
                if (id == null || !id.StartsWith("plugin#")) continue;
                foreach (string slot in killSlots)
                {
                    if (id == slot || id.StartsWith(slot + "_")) { storables.Remove(i); break; }
                }
            }
        }

        // Write a world transform into the spawned CUA's control (and atom/container) so it loads at the correct
        // spot before the post-load ParentLink is established (no spawn-frame flash).
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

        private static Transform FindLiveBoneTransform(Atom person, string bone)
        {
            DAZBone[] bones = person.GetComponentsInChildren<DAZBone>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name == bone) return bones[i].transform;
            Rigidbody[] rbs = person.GetComponentsInChildren<Rigidbody>();
            for (int i = 0; i < rbs.Length; i++)
                if (rbs[i] != null && rbs[i].name == bone) return rbs[i].transform;
            FreeControllerV3[] fcs = person.freeControllers;
            if (fcs != null)
                for (int i = 0; i < fcs.Length; i++)
                    if (fcs[i] != null && fcs[i].name == bone) return fcs[i].transform;
            return null;
        }

        // The bone rigidbody a native ParentLink attaches to (the control's linkToRB is a Rigidbody, not a DAZBone).
        private static Rigidbody FindLiveBoneRigidbody(Atom person, string bone)
        {
            Rigidbody[] rbs = person.GetComponentsInChildren<Rigidbody>();
            for (int i = 0; i < rbs.Length; i++)
                if (rbs[i] != null && rbs[i].name == bone) return rbs[i];
            return null;
        }

        private static void WriteVec3(JSONClass parent, string key, Vector3 v)
        {
            JSONClass o = new JSONClass();
            o["x"] = F(v.x);
            o["y"] = F(v.y);
            o["z"] = F(v.z);
            parent[key] = o;
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

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

        // The person bone a CUA is (transitively) anchored to: its own link bone if it links to the person,
        // else follow the CUA->CUA chain until it terminates at the person and return that terminal bone.
        private static string ResolveAnchorBone(CuaPlan p, string personId, Dictionary<string, JSONClass> cuaById)
        {
            if (p.LinksToPerson) return p.LinkBone;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string cur = p.LinkAtomId;
            for (int i = 0; i < 16 && cur != null && seen.Add(cur); i++)
            {
                JSONClass cua;
                if (!cuaById.TryGetValue(cur, out cua)) return null;
                string lt = ReadLinkTo(cua);
                if (string.IsNullOrEmpty(lt)) return null;
                int c = lt.IndexOf(':');
                if (c <= 0) return null;
                string atomPart = lt.Substring(0, c);
                string bonePart = lt.Substring(c + 1);
                if (atomPart == personId) return bonePart;
                cur = atomPart;
            }
            return null;
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

        public static void DumpLiveCUAGeometry(string tag)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            int n = 0;
            foreach (Atom a in sc.GetAtoms())
            {
                if (a == null || a.type != "CustomUnityAsset") continue;
                n++;
                FreeControllerV3 mc = a.mainController;
                Rigidbody link = mc != null ? mc.linkToRB : null;

                Renderer[] rends = a.GetComponentsInChildren<Renderer>();
                bool haveMesh = rends != null && rends.Length > 0;
                Vector3 meshCenter = Vector3.zero;
                string meshStr = "(no renderer)";
                if (haveMesh)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    meshCenter = b.center;
                    meshStr = $"center{b.center.ToString("F4")} size{b.size.ToString("F3")} rends{rends.Length}";
                }

                string ctrlStr = mc != null
                    ? $"{mc.transform.position.ToString("F4")} eul{mc.transform.rotation.eulerAngles.ToString("F2")}"
                    : "(no ctrl)";
                string linkStr = link != null
                    ? $"{link.name} pos{link.transform.position.ToString("F4")} eul{link.transform.rotation.eulerAngles.ToString("F2")}"
                    : "(unlinked)";

                LogUtil.Log($"[VPB][CUA][dump:{tag}] '{a.uid}' ctrl={ctrlStr}");
                LogUtil.Log($"[VPB][CUA][dump:{tag}]   link={linkStr}");
                LogUtil.Log($"[VPB][CUA][dump:{tag}]   mesh={meshStr}");

                // Both native source loads and our imports carry a live linkToRB now, so the bone-local delta is
                // directly comparable between them.
                Transform anchor = link != null ? link.transform : null;
                string anchorName = link != null ? link.name : null;

                if (mc != null && haveMesh)
                    LogUtil.Log($"[VPB][CUA][dump:{tag}]   ctrl->mesh={(meshCenter - mc.transform.position).ToString("F4")}");
                if (anchor != null)
                {
                    LogUtil.Log($"[VPB][CUA][dump:{tag}]   anchor='{anchorName}' pos{anchor.position.ToString("F4")} eul{anchor.rotation.eulerAngles.ToString("F2")}");
                    Quaternion invRot = Quaternion.Inverse(anchor.rotation);
                    if (mc != null)
                    {
                        Vector3 ctrlLocal = invRot * (mc.transform.position - anchor.position);
                        LogUtil.Log($"[VPB][CUA][dump:{tag}]   ctrl-local-to-bone={ctrlLocal.ToString("F4")} dist={((mc.transform.position - anchor.position).magnitude).ToString("F4")}");
                    }
                    if (haveMesh)
                    {
                        Vector3 meshLocal = invRot * (meshCenter - anchor.position);
                        LogUtil.Log($"[VPB][CUA][dump:{tag}]   mesh-local-to-bone={meshLocal.ToString("F4")} dist={((meshCenter - anchor.position).magnitude).ToString("F4")}");
                    }
                }
            }
            if (n == 0) LogUtil.Log($"[VPB][CUA][dump:{tag}] no live CUA atoms.");
        }
    }
}
