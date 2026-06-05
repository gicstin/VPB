using System.Collections.Generic;
using SimpleJSON;

namespace VPB
{
    internal sealed class IsolationGuardSnapshot
    {
        public string TargetStorableId;
        public Dictionary<string, string> StorableHashes;
        public Atom Atom;
    }

    internal static class VpbImportIsolationGuard
    {
        public static IsolationGuardSnapshot Snapshot(Atom atom, string targetStorableId)
        {
            if (atom == null) return null;

            IsolationGuardSnapshot snap = new IsolationGuardSnapshot
            {
                Atom = atom,
                TargetStorableId = targetStorableId,
                StorableHashes = new Dictionary<string, string>(64),
            };

            foreach (string storableId in atom.GetStorableIDs())
            {
                if (storableId == null) continue;
                JSONStorable st = atom.GetStorableByID(storableId);
                if (st == null) continue;

                JSONNode js = null;
                try { js = st.GetJSON(true, true, true); } catch { continue; }

                snap.StorableHashes[storableId] = js != null ? js.ToString() : "";
            }

            return snap;
        }

        public static void DiffAndWarn(IsolationGuardSnapshot snap, bool allowCuaMutations)
        {
            if (snap == null || snap.Atom == null) return;

            foreach (string storableId in snap.Atom.GetStorableIDs())
            {
                if (storableId == null) continue;
                if (storableId == snap.TargetStorableId) continue;
                if (allowCuaMutations && IsCuaRelated(storableId)) continue;

                JSONStorable st = snap.Atom.GetStorableByID(storableId);
                string before;
                snap.StorableHashes.TryGetValue(storableId, out before);

                string after = "";
                if (st != null)
                {
                    JSONNode js = null;
                    try { js = st.GetJSON(true, true, true); } catch { }
                    after = js != null ? js.ToString() : "";
                }
                if (before == null) before = "";

                if (before != after)
                {
                    LogUtil.LogWarning("[VPB import isolation] storable " + storableId
                        + " mutated outside scope (target was " + snap.TargetStorableId + ")."
                        + " before len=" + before.Length + " after len=" + after.Length);
                }
            }
        }

        private static bool IsCuaRelated(string storableId)
        {
            return !string.IsNullOrEmpty(storableId) && storableId.IndexOf("CustomUnity") >= 0;
        }
    }
}
