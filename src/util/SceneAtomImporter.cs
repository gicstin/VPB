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
        /// External atom UID referenced by selected import payload that is not itself being imported.
        /// Shown for missing live UIDs, person-like retarget (Person→Player), and plugin-receiver remap.
        /// </summary>
        public struct BrokenUidRef
        {
            public string OriginalUid;
            public string SourceType;
            public string SuggestedLiveUid;
            /// <summary>Donor scene still has this atom — Remap modal can co-import it as Create new.</summary>
            public bool CanCreateFromSource;
            /// <summary>
            /// Distinct <c>plugin#N_ClassName</c> values from trigger <c>receiver</c> fields targeting
            /// <see cref="OriginalUid"/> inside the selected import JSON. Empty/null when none.
            /// </summary>
            public List<string> SourcePluginReceivers;
            /// <summary>
            /// Parallel to <see cref="SourcePluginReceivers"/>: PluginManager URL for that slot on the
            /// source-scene atom (may be empty). Used for URL-based live suggest.
            /// </summary>
            public List<string> SourcePluginReceiverUrls;
        }

        /// <summary>Live plugin slot on an atom (PluginManager key + param storable id + script URL).</summary>
        public struct LivePluginSlot
        {
            public string SlotKey;
            public string StoreId;
            public string ClassName;
            public string Url;
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
                selectedIds, relativeToTargetPerson, skipExistingInScene, null, null);
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
            return ImportSelectedAtoms(
                sourceScene, sourcePersonAtomId, targetPerson, sourceHostUid,
                selectedIds, relativeToTargetPerson, skipExistingInScene, uidRemap, null);
        }

        /// <param name="receiverRemapByUid">
        /// Optional: original atom uid → (source <c>plugin#N_Class</c> → live dest store id).
        /// Applied to trigger <c>receiver</c> fields before atom UID remap.
        /// </param>
        public static IEnumerator ImportSelectedAtoms(
            JSONClass sourceScene,
            string sourcePersonAtomId,
            Atom targetPerson,
            string sourceHostUid,
            HashSet<string> selectedIds,
            bool relativeToTargetPerson,
            bool skipExistingInScene,
            Dictionary<string, string> uidRemap,
            Dictionary<string, Dictionary<string, string>> receiverRemapByUid)
        {
            if (sourceScene == null || selectedIds == null || selectedIds.Count == 0)
            {
                LogAtom("abort — sourceScene=" + (sourceScene != null ? "ok" : "null")
                    + " selectedIds=" + (selectedIds != null ? selectedIds.Count.ToString() : "null"));
                yield break;
            }

            int recvCount = 0;
            if (receiverRemapByUid != null)
            {
                foreach (KeyValuePair<string, Dictionary<string, string>> kv in receiverRemapByUid)
                    if (kv.Value != null) recvCount += kv.Value.Count;
            }

            LogAtom("start selected=" + selectedIds.Count
                + " skipExisting=" + skipExistingInScene
                + " relativeToTarget=" + relativeToTargetPerson
                + " sourcePerson='" + (sourcePersonAtomId ?? "") + "'"
                + " targetPerson='" + (targetPerson != null ? targetPerson.uid : "(none)") + "'"
                + " sourceHostUid='" + (sourceHostUid ?? "") + "'"
                + " uidRemap=" + (uidRemap != null ? uidRemap.Count.ToString() : "0")
                + " receiverRemap=" + recvCount.ToString());

            while (s_importRunning)
                yield return null;
            s_importRunning = true;
            try
            {
                yield return ImportSelectedAtomsCore(
                    sourceScene, sourcePersonAtomId, targetPerson, sourceHostUid,
                    selectedIds, relativeToTargetPerson, skipExistingInScene,
                    uidRemap, receiverRemapByUid);
            }
            finally
            {
                s_importRunning = false;
            }
        }

        /// <summary>
        /// True when live scene has an atom with this exact uid (not <c>uid#N</c> variants).
        /// </summary>
        public static bool ExactAtomInScene(string atomUid)
        {
            if (string.IsNullOrEmpty(atomUid)) return false;
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            try { return sc.GetAtomByUid(atomUid) != null; }
            catch { return false; }
        }

        /// <summary>
        /// Finds external atom UIDs referenced by <paramref name="selectedIds"/> that need a Remap Atom UIDs
        /// row: missing in live scene, person-like retarget (e.g. donor <c>Person</c> → live <c>Player</c>),
        /// or trigger plugin receivers that may need slot remap. Cold path — scene import only.
        /// </summary>
        /// <param name="preferredPersonUid">
        /// Import-sidebar target person uid — preferred suggest for person-like refs when present live.
        /// </param>
        public static List<BrokenUidRef> CollectBrokenExternalUidRefs(
            JSONClass sourceScene, HashSet<string> selectedIds)
        {
            return CollectBrokenExternalUidRefs(sourceScene, selectedIds, null);
        }

        public static List<BrokenUidRef> CollectBrokenExternalUidRefs(
            JSONClass sourceScene, HashSet<string> selectedIds, string preferredPersonUid)
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
            var pluginReceiversByUid = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string id in selectedIds)
            {
                JSONClass node = FindAtom(atoms, id);
                if (node == null) continue;
                CollectReferencedSourceAtomUids(node, sourceIdToType, referenced);
                CollectTriggerPluginReceivers(node, pluginReceiversByUid);
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

                string srcType = string.Empty;
                sourceIdToType.TryGetValue(refUid, out srcType);

                List<string> srcReceivers = null;
                List<string> srcUrls = null;
                HashSet<string> recvSet;
                if (pluginReceiversByUid.TryGetValue(refUid, out recvSet) && recvSet != null && recvSet.Count > 0)
                {
                    srcReceivers = new List<string>(recvSet.Count);
                    srcUrls = new List<string>(recvSet.Count);
                    foreach (string r in recvSet)
                        srcReceivers.Add(r);
                    srcReceivers.Sort(StringComparer.OrdinalIgnoreCase);
                    JSONClass targetAtomNode = FindAtom(atoms, refUid);
                    for (int ri = 0; ri < srcReceivers.Count; ri++)
                        srcUrls.Add(LookupSourcePluginUrl(targetAtomNode, srcReceivers[ri]));
                }

                bool missingLive = !ExactAtomInScene(refUid);
                bool personLike = SceneUtils.IsPersonLikeAtomType(srcType);
                bool hasPluginRecv = srcReceivers != null && srcReceivers.Count > 0;
                // Always offer person-like retarget (Person→Player) and plugin-receiver rows even when
                // the donor uid still exists live — prior skip hid Embody retarget entirely.
                if (!missingLive && !personLike && !hasPluginRecv)
                    continue;

                string suggested = SuggestLiveUidForExternalRef(
                    refUid, srcType, preferredPersonUid, liveByType);

                // Non-person identity row with plugins that already match live → noise, skip.
                if (!missingLive && !personLike && hasPluginRecv)
                {
                    string dest = !string.IsNullOrEmpty(suggested) ? suggested : refUid;
                    BrokenUidRef probe = new BrokenUidRef
                    {
                        OriginalUid = refUid,
                        SourcePluginReceivers = srcReceivers,
                        SourcePluginReceiverUrls = srcUrls
                    };
                    if (CountUnresolvedPluginReceivers(probe, dest, null) == 0)
                        continue;
                }

                result.Add(new BrokenUidRef
                {
                    OriginalUid = refUid,
                    SourceType = srcType ?? string.Empty,
                    SuggestedLiveUid = suggested,
                    CanCreateFromSource = sourceIdToType.ContainsKey(refUid),
                    SourcePluginReceivers = srcReceivers,
                    SourcePluginReceiverUrls = srcUrls
                });
            }

            result.Sort((a, b) => string.Compare(a.OriginalUid, b.OriginalUid, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>
        /// Suggest live destination for an external ref: preferred person → exact uid → unique same-type.
        /// </summary>
        private static string SuggestLiveUidForExternalRef(
            string refUid,
            string srcType,
            string preferredPersonUid,
            Dictionary<string, List<string>> liveByType)
        {
            bool personLike = SceneUtils.IsPersonLikeAtomType(srcType);

            // Person-like: prefer import-sidebar target (Player) over donor uid when names diverged.
            if (personLike
                && !string.IsNullOrEmpty(preferredPersonUid)
                && ExactAtomInScene(preferredPersonUid))
                return preferredPersonUid;

            if (ExactAtomInScene(refUid))
                return refUid;

            if (!string.IsNullOrEmpty(srcType) && liveByType != null)
            {
                List<string> sameType;
                if (liveByType.TryGetValue(srcType, out sameType) && sameType != null && sameType.Count > 0)
                {
                    if (sameType.Count == 1)
                        return sameType[0];
                    if (!string.IsNullOrEmpty(preferredPersonUid))
                    {
                        for (int i = 0; i < sameType.Count; i++)
                        {
                            if (string.Equals(sameType[i], preferredPersonUid, StringComparison.Ordinal))
                                return preferredPersonUid;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Walk trigger-action objects (<c>receiverAtom</c> + <c>receiver</c>) and collect distinct
        /// <c>plugin#N_ClassName</c> receivers keyed by the target atom uid.
        /// </summary>
        private static void CollectTriggerPluginReceivers(
            JSONNode node, Dictionary<string, HashSet<string>> sink)
        {
            if (node == null || sink == null) return;

            JSONArray ja = node as JSONArray;
            if (ja != null)
            {
                for (int i = 0; i < ja.Count; i++)
                    CollectTriggerPluginReceivers(ja[i], sink);
                return;
            }

            JSONClass jc = node as JSONClass;
            if (jc == null) return;

            try
            {
                if (jc.HasKey("receiverAtom") && jc.HasKey("receiver"))
                {
                    string atom = jc["receiverAtom"] != null ? jc["receiverAtom"].Value : null;
                    string recv = jc["receiver"] != null ? jc["receiver"].Value : null;
                    atom = StripExternalRefPrefix(atom);
                    if (!string.IsNullOrEmpty(atom)
                        && !string.IsNullOrEmpty(recv)
                        && IsPluginStoreId(recv))
                    {
                        HashSet<string> set;
                        if (!sink.TryGetValue(atom, out set))
                        {
                            set = new HashSet<string>(StringComparer.Ordinal);
                            sink[atom] = set;
                        }
                        set.Add(recv);
                    }
                }
            }
            catch
            {
            }

            foreach (string key in jc.Keys)
                CollectTriggerPluginReceivers(jc[key], sink);
        }

        /// <summary>True for VaM plugin param storable ids like <c>plugin#0_Embody</c>.</summary>
        public static bool IsPluginStoreId(string storeId)
        {
            if (string.IsNullOrEmpty(storeId)) return false;
            if (!storeId.StartsWith("plugin#", StringComparison.Ordinal)) return false;
            return storeId.IndexOf('_') > "plugin#".Length;
        }

        /// <summary>Strip subscene <c>external_ref:</c> prefix from a receiverAtom value.</summary>
        public static string StripExternalRefPrefix(string receiverAtom)
        {
            if (string.IsNullOrEmpty(receiverAtom)) return receiverAtom;
            const string prefix = "external_ref:";
            if (receiverAtom.StartsWith(prefix, StringComparison.Ordinal))
                return receiverAtom.Substring(prefix.Length);
            return receiverAtom;
        }

        private static string LookupSourcePluginUrl(JSONClass atomNode, string storeId)
        {
            if (atomNode == null || string.IsNullOrEmpty(storeId)) return string.Empty;
            string slot = PluginStoreSlotKey(storeId);
            if (string.IsNullOrEmpty(slot)) return string.Empty;
            try
            {
                JSONClass pm = atomNode.GetStorable("PluginManager");
                if (pm == null) return string.Empty;
                JSONClass plugins = pm["plugins"] != null ? pm["plugins"].AsObject : null;
                if (plugins == null || !plugins.HasKey(slot)) return string.Empty;
                string url = plugins[slot] != null ? plugins[slot].Value : null;
                return url ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Class / type suffix of <c>plugin#N_ClassName</c>, or null.</summary>
        public static string PluginStoreClassName(string storeId)
        {
            if (!IsPluginStoreId(storeId)) return null;
            int us = storeId.IndexOf('_');
            if (us < 0 || us + 1 >= storeId.Length) return null;
            return storeId.Substring(us + 1);
        }

        /// <summary>
        /// Soft ClassName equality: exact ignore-case, or dotted suffix
        /// (<c>VamTimeline.AtomPlugin</c> ↔ <c>AtomPlugin</c>).
        /// </summary>
        public static bool PluginClassNamesMatch(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase)) return true;
            if (b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Slot key <c>plugin#N</c> from a store id or bare slot key.</summary>
        public static string PluginStoreSlotKey(string storeIdOrSlot)
        {
            if (string.IsNullOrEmpty(storeIdOrSlot)) return null;
            if (!storeIdOrSlot.StartsWith("plugin#", StringComparison.Ordinal)) return null;
            int us = storeIdOrSlot.IndexOf('_');
            if (us < 0) return storeIdOrSlot;
            return storeIdOrSlot.Substring(0, us);
        }

        /// <summary>
        /// Live plugin slots on an atom (cold path). Prefer PluginManager URLs; also surfaces
        /// <c>plugin#N_Class</c> store ids from <see cref="Atom.GetStorableIDs"/>.
        /// </summary>
        public static List<LivePluginSlot> ListLivePluginSlots(Atom atom)
        {
            var result = new List<LivePluginSlot>(8);
            if (atom == null) return result;

            var bySlot = new Dictionary<string, LivePluginSlot>(StringComparer.Ordinal);
            try
            {
                JSONStorable pm = atom.GetStorableByID("PluginManager");
                if (pm != null)
                {
                    JSONClass j = pm.GetJSON();
                    JSONClass plugins = (j != null && j["plugins"] != null) ? j["plugins"].AsObject : null;
                    if (plugins != null)
                    {
                        foreach (string k in plugins.Keys)
                        {
                            if (string.IsNullOrEmpty(k)) continue;
                            string url = plugins[k] != null ? plugins[k].Value : string.Empty;
                            string className = ClassNameFromPluginUrl(url);
                            LivePluginSlot slot = new LivePluginSlot
                            {
                                SlotKey = k,
                                StoreId = !string.IsNullOrEmpty(className) ? (k + "_" + className) : k,
                                ClassName = className ?? string.Empty,
                                Url = url ?? string.Empty
                            };
                            bySlot[k] = slot;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                foreach (string sid in atom.GetStorableIDs())
                {
                    if (!IsPluginStoreId(sid)) continue;
                    string slotKey = PluginStoreSlotKey(sid);
                    string className = PluginStoreClassName(sid);
                    LivePluginSlot existing;
                    if (!string.IsNullOrEmpty(slotKey) && bySlot.TryGetValue(slotKey, out existing))
                    {
                        existing.StoreId = sid;
                        if (string.IsNullOrEmpty(existing.ClassName) && !string.IsNullOrEmpty(className))
                            existing.ClassName = className;
                        bySlot[slotKey] = existing;
                    }
                    else if (!string.IsNullOrEmpty(slotKey))
                    {
                        bySlot[slotKey] = new LivePluginSlot
                        {
                            SlotKey = slotKey,
                            StoreId = sid,
                            ClassName = className ?? string.Empty,
                            Url = string.Empty
                        };
                    }
                }
            }
            catch
            {
            }

            foreach (KeyValuePair<string, LivePluginSlot> kv in bySlot)
                result.Add(kv.Value);
            result.Sort((a, b) => string.Compare(a.SlotKey, b.SlotKey, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static List<LivePluginSlot> ListLivePluginSlotsByUid(string atomUid)
        {
            if (string.IsNullOrEmpty(atomUid)) return new List<LivePluginSlot>(0);
            SuperController sc = SuperController.singleton;
            if (sc == null) return new List<LivePluginSlot>(0);
            Atom atom = null;
            try { atom = sc.GetAtomByUid(atomUid); } catch { atom = null; }
            return ListLivePluginSlots(atom);
        }

        /// <summary>
        /// Suggest a live store id for <paramref name="sourceReceiver"/> on <paramref name="destAtomUid"/>.
        /// Exact store id → unique ClassName → unique URL (when provided).
        /// </summary>
        public static string SuggestLiveReceiverStoreId(string destAtomUid, string sourceReceiver)
        {
            return SuggestLiveReceiverStoreId(destAtomUid, sourceReceiver, null);
        }

        public static string SuggestLiveReceiverStoreId(
            string destAtomUid, string sourceReceiver, string sourcePluginUrl)
        {
            if (string.IsNullOrEmpty(destAtomUid) || string.IsNullOrEmpty(sourceReceiver))
                return null;
            List<LivePluginSlot> slots = ListLivePluginSlotsByUid(destAtomUid);
            if (slots == null || slots.Count == 0) return null;

            // Exact store id already on dest — no slot remap needed.
            for (int i = 0; i < slots.Count; i++)
            {
                if (string.Equals(slots[i].StoreId, sourceReceiver, StringComparison.Ordinal))
                    return sourceReceiver;
            }

            string className = PluginStoreClassName(sourceReceiver);
            if (!string.IsNullOrEmpty(className))
            {
                string unique = null;
                int hits = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    LivePluginSlot s = slots[i];
                    if (string.IsNullOrEmpty(s.StoreId)) continue;
                    if (!PluginClassNamesMatch(s.ClassName, className)) continue;
                    hits++;
                    unique = s.StoreId;
                    if (hits > 1) { unique = null; break; }
                }
                if (!string.IsNullOrEmpty(unique)) return unique;
            }

            if (!string.IsNullOrEmpty(sourcePluginUrl))
            {
                string urlNorm = sourcePluginUrl.Trim();
                string unique = null;
                int hits = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    LivePluginSlot s = slots[i];
                    if (string.IsNullOrEmpty(s.StoreId) || string.IsNullOrEmpty(s.Url)) continue;
                    if (!string.Equals(s.Url.Trim(), urlNorm, StringComparison.OrdinalIgnoreCase))
                        continue;
                    hits++;
                    unique = s.StoreId;
                    if (hits > 1) { unique = null; break; }
                }
                if (!string.IsNullOrEmpty(unique)) return unique;
            }

            return null;
        }

        /// <summary>
        /// True when every source plugin receiver for this row either matches a live store id
        /// exactly, has an auto/explicit remap, or dest is Create new / empty (not applicable).
        /// </summary>
        public static int CountUnresolvedPluginReceivers(
            BrokenUidRef row,
            string destUid,
            string explicitPrimaryChoice)
        {
            if (row.SourcePluginReceivers == null || row.SourcePluginReceivers.Count == 0)
                return 0;
            if (string.IsNullOrEmpty(destUid)) return 0;
            if (string.Equals(destUid, CreateNewUidSentinel, StringComparison.Ordinal)) return 0;

            int unresolved = 0;
            for (int r = 0; r < row.SourcePluginReceivers.Count; r++)
            {
                string srcRecv = row.SourcePluginReceivers[r];
                if (string.IsNullOrEmpty(srcRecv)) continue;

                string url = null;
                if (row.SourcePluginReceiverUrls != null && r < row.SourcePluginReceiverUrls.Count)
                    url = row.SourcePluginReceiverUrls[r];

                string suggested = SuggestLiveReceiverStoreId(destUid, srcRecv, url);
                if (!string.IsNullOrEmpty(suggested))
                    continue;

                // Primary explicit UI choice covers index 0 only.
                if (r == 0
                    && !string.IsNullOrEmpty(explicitPrimaryChoice)
                    && LiveAtomHasStoreId(destUid, explicitPrimaryChoice))
                    continue;

                unresolved++;
            }
            return unresolved;
        }

        public static bool LiveAtomHasStoreId(string atomUid, string storeId)
        {
            if (string.IsNullOrEmpty(atomUid) || string.IsNullOrEmpty(storeId)) return false;
            List<LivePluginSlot> slots = ListLivePluginSlotsByUid(atomUid);
            for (int i = 0; i < slots.Count; i++)
            {
                if (string.Equals(slots[i].StoreId, storeId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Build per-original-uid receiver remaps: unique ClassName/URL auto-map plus explicit
        /// <paramref name="explicitChoices"/> (originalUid → chosen live store id for the primary/first source receiver).
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> BuildReceiverRemapByUid(
            List<BrokenUidRef> rows,
            Dictionary<string, string> uidChoices,
            Dictionary<string, string> explicitReceiverChoices)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (rows == null || uidChoices == null) return result;

            for (int i = 0; i < rows.Count; i++)
            {
                BrokenUidRef row = rows[i];
                if (string.IsNullOrEmpty(row.OriginalUid)) continue;
                if (row.SourcePluginReceivers == null || row.SourcePluginReceivers.Count == 0) continue;

                string dest;
                if (!uidChoices.TryGetValue(row.OriginalUid, out dest) || string.IsNullOrEmpty(dest))
                    continue;
                if (string.Equals(dest, CreateNewUidSentinel, StringComparison.Ordinal)) continue;

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int r = 0; r < row.SourcePluginReceivers.Count; r++)
                {
                    string srcRecv = row.SourcePluginReceivers[r];
                    if (string.IsNullOrEmpty(srcRecv)) continue;

                    string url = null;
                    if (row.SourcePluginReceiverUrls != null && r < row.SourcePluginReceiverUrls.Count)
                        url = row.SourcePluginReceiverUrls[r];

                    string suggested = SuggestLiveReceiverStoreId(dest, srcRecv, url);
                    if (!string.IsNullOrEmpty(suggested)
                        && !string.Equals(srcRecv, suggested, StringComparison.Ordinal))
                    {
                        map[srcRecv] = suggested;
                    }
                }

                // Explicit UI choice for primary receiver overrides / fills when auto failed.
                string chosenRecv;
                if (explicitReceiverChoices != null
                    && explicitReceiverChoices.TryGetValue(row.OriginalUid, out chosenRecv)
                    && !string.IsNullOrEmpty(chosenRecv)
                    && row.SourcePluginReceivers.Count > 0)
                {
                    string primary = row.SourcePluginReceivers[0];
                    if (!string.IsNullOrEmpty(primary)
                        && !string.Equals(primary, chosenRecv, StringComparison.Ordinal))
                        map[primary] = chosenRecv;
                }

                if (map.Count > 0)
                    result[row.OriginalUid] = map;
            }
            return result;
        }

        private static string ClassNameFromPluginUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string path = url;
            int colon = path.LastIndexOf(':');
            if (colon >= 0 && colon + 1 < path.Length)
                path = path.Substring(colon + 1);
            path = path.Replace('\\', '/');
            int slash = path.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < path.Length)
                path = path.Substring(slash + 1);
            // Embody.cslist / Embody.cs → Embody
            int dot = path.LastIndexOf('.');
            if (dot > 0) path = path.Substring(0, dot);
            return string.IsNullOrEmpty(path) ? null : path;
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

            val = StripExternalRefPrefix(val);
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

        /// <summary>
        /// Apply trigger receiver remaps while <c>receiverAtom</c> still names the original UID.
        /// Must run before <see cref="ApplyUidRemapToNode"/>.
        /// </summary>
        private static void ApplyReceiverRemapToNode(
            JSONClass node, Dictionary<string, Dictionary<string, string>> receiverRemapByUid)
        {
            if (node == null || receiverRemapByUid == null || receiverRemapByUid.Count == 0) return;
            foreach (KeyValuePair<string, Dictionary<string, string>> byAtom in receiverRemapByUid)
            {
                if (string.IsNullOrEmpty(byAtom.Key) || byAtom.Value == null || byAtom.Value.Count == 0)
                    continue;
                foreach (KeyValuePair<string, string> kv in byAtom.Value)
                {
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                    if (string.Equals(kv.Key, kv.Value, StringComparison.Ordinal)) continue;
                    JSONExtensions.RemapTriggerReceiverMutable(node, byAtom.Key, kv.Key, kv.Value);
                }
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
            Dictionary<string, string> uidRemap,
            Dictionary<string, Dictionary<string, string>> receiverRemapByUid)
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

                if (receiverRemapByUid != null && receiverRemapByUid.Count > 0)
                {
                    ApplyReceiverRemapToNode(node, receiverRemapByUid);
                    LogAtom("prepare '" + id + "' — applied trigger receiver remap(s)");
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
