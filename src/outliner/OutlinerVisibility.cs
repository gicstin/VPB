using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal enum OutlinerEyeState
    {
        None = 0,
        AllOn = 1,
        Mixed = 2,
        AllOff = 3
    }

    internal static class OutlinerVisibility
    {
        const char RecordSep = '\n';
        const char FieldSep = '\t';

        internal static void CollectAtoms(OutlinerModel model, OutlinerNode node, List<OutlinerNode> into)
        {
            if (into == null) return;
            into.Clear();
            if (model == null || node == null) return;
            Walk(model, node, into, 0);
        }

        static void Walk(OutlinerModel model, OutlinerNode node, List<OutlinerNode> into, int guard)
        {
            if (node == null || guard > 4000) return;
            if (node.Kind == OutlinerNodeKind.Atom
                && !node.IsSystemProtected
                && !string.IsNullOrEmpty(node.AtomUid))
                into.Add(node);
            for (int i = 0; i < node.ChildIndices.Count; i++)
                Walk(model, model.Get(node.ChildIndices[i]), into, guard + 1);
        }

        internal static OutlinerEyeState Aggregate(IList<OutlinerNode> atoms, out int onCount, out int total)
        {
            onCount = 0;
            total = 0;
            if (atoms == null) return OutlinerEyeState.None;
            for (int i = 0; i < atoms.Count; i++)
            {
                OutlinerNode n = atoms[i];
                if (n == null) continue;
                total++;
                if (n.On) onCount++;
            }
            if (total == 0) return OutlinerEyeState.None;
            if (onCount == 0) return OutlinerEyeState.AllOff;
            if (onCount == total) return OutlinerEyeState.AllOn;
            return OutlinerEyeState.Mixed;
        }

        internal static bool NextOnFor(OutlinerEyeState state)
        {
            return state == OutlinerEyeState.AllOff || state == OutlinerEyeState.None;
        }

        internal static string EncodeStates(IList<string> uids, IList<bool> on)
        {
            if (uids == null || on == null) return "";
            var sb = new System.Text.StringBuilder(uids.Count * 24);
            int n = Math.Min(uids.Count, on.Count);
            for (int i = 0; i < n; i++)
            {
                if (string.IsNullOrEmpty(uids[i])) continue;
                if (sb.Length > 0) sb.Append(RecordSep);
                sb.Append(uids[i]).Append(FieldSep).Append(on[i] ? '1' : '0');
            }
            return sb.ToString();
        }

        internal static int DecodeStates(string payload, List<string> uids, List<bool> on)
        {
            if (uids != null) uids.Clear();
            if (on != null) on.Clear();
            if (string.IsNullOrEmpty(payload) || uids == null || on == null) return 0;
            string[] recs = payload.Split(RecordSep);
            for (int i = 0; i < recs.Length; i++)
            {
                string rec = recs[i];
                int tab = rec.LastIndexOf(FieldSep);
                if (tab <= 0 || tab >= rec.Length - 1) continue;
                uids.Add(rec.Substring(0, tab));
                on.Add(rec[tab + 1] == '1');
            }
            return uids.Count;
        }
    }
}
