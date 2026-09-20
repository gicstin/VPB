using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal static class OutlinerModelBuilder
    {
        internal static OutlinerModel Build(
            IList<OutlinerAtomFacts> facts,
            OutlinerGrouping grouping,
            string filter,
            CreatorStripKeepKind kindMask)
        {
            var model = new OutlinerModel();
            model.Grouping = grouping;
            OutlinerNode scene = NewNode(model, "scene", OutlinerNodeKind.Scene, "Scene", "", -1);
            model.RootIndex = 0;
            if (facts == null || facts.Count == 0)
                return model;

            var keep = new List<OutlinerAtomFacts>(facts.Count);
            OutlinerFilterQuery query = OutlinerFilter.Parse(filter);
            for (int i = 0; i < facts.Count; i++)
            {
                OutlinerAtomFacts f = facts[i];
                if (f == null || string.IsNullOrEmpty(f.Uid)) continue;
                model.SceneAtomCount++;
                if (kindMask != CreatorStripKeepKind.None && !PassesKind(f.Kind, kindMask))
                    continue;
                if (!OutlinerFilter.Matches(f, query))
                    continue;
                keep.Add(f);
            }

            if (grouping == OutlinerGrouping.Flat)
                BuildFlat(model, scene, keep);
            else if (grouping == OutlinerGrouping.Hierarchy)
                BuildHierarchy(model, scene, keep);
            else
                BuildTypeGroups(model, scene, keep);

            model.AtomCount = keep.Count;
            CompactSiblingAtomLabels(model);
            return model;
        }

        internal static CreatorStripKeepKind PresentKinds(IList<OutlinerAtomFacts> facts)
        {
            CreatorStripKeepKind mask = CreatorStripKeepKind.None;
            if (facts == null) return mask;
            for (int i = 0; i < facts.Count; i++)
            {
                OutlinerAtomFacts f = facts[i];
                if (f == null || string.IsNullOrEmpty(f.Uid)) continue;
                CreatorStripKeepKind k = f.Kind == CreatorStripKeepKind.None
                    ? CreatorStripKeepKind.Other
                    : f.Kind;
                mask |= k;
            }
            return mask;
        }

        internal static CreatorStripKeepKind ClipKindMask(
            CreatorStripKeepKind mask,
            CreatorStripKeepKind present)
        {
            if (mask == CreatorStripKeepKind.None) return CreatorStripKeepKind.None;
            return mask & present;
        }

        static void CompactSiblingAtomLabels(OutlinerModel model)
        {
            if (model == null) return;
            for (int n = 0; n < model.Nodes.Count; n++)
            {
                OutlinerNode parent = model.Get(n);
                if (parent == null || parent.ChildIndices.Count == 0) continue;
                int atomCount = 0;
                for (int i = 0; i < parent.ChildIndices.Count; i++)
                {
                    OutlinerNode child = model.Get(parent.ChildIndices[i]);
                    if (child != null && child.Kind == OutlinerNodeKind.Atom)
                        atomCount++;
                }
                if (atomCount == 0) continue;
                var full = new string[atomCount];
                var idxs = new int[atomCount];
                int w = 0;
                for (int i = 0; i < parent.ChildIndices.Count; i++)
                {
                    int ci = parent.ChildIndices[i];
                    OutlinerNode child = model.Get(ci);
                    if (child == null || child.Kind != OutlinerNodeKind.Atom) continue;
                    full[w] = child.Label;
                    idxs[w] = ci;
                    w++;
                }
                string[] compact = OutlinerFilter.DistinctPathLabels(full);
                for (int i = 0; i < idxs.Length; i++)
                {
                    OutlinerNode child = model.Get(idxs[i]);
                    if (child == null) continue;
                    if (!string.IsNullOrEmpty(compact[i]))
                        child.Label = compact[i];
                }
            }
        }

        static bool PassesKind(CreatorStripKeepKind kind, CreatorStripKeepKind mask)
        {
            CreatorStripKeepKind use = kind == CreatorStripKeepKind.None ? CreatorStripKeepKind.Other : kind;
            return (mask & use) != 0;
        }

        static void BuildTypeGroups(
            OutlinerModel model,
            OutlinerNode scene,
            List<OutlinerAtomFacts> keep)
        {
            CreatorStripKeepKind[] order = SceneUtils.CreatorStripKeepDisplayOrder;
            for (int oi = 0; oi < order.Length; oi++)
            {
                CreatorStripKeepKind kind = order[oi];
                int groupIndex = -1;
                OutlinerNode group = null;
                for (int i = 0; i < keep.Count; i++)
                {
                    OutlinerAtomFacts f = keep[i];
                    CreatorStripKeepKind k = f.Kind == CreatorStripKeepKind.None
                        ? CreatorStripKeepKind.Other
                        : f.Kind;
                    if (k != kind) continue;
                    if (group == null)
                    {
                        groupIndex = model.Nodes.Count;
                        group = NewNode(
                            model,
                            "group:" + ((int)kind).ToString(),
                            OutlinerNodeKind.TypeGroup,
                            SceneUtils.CreatorStripKeepKindLabel(kind),
                            "",
                            0);
                        group.AtomKind = kind;
                        scene.ChildIndices.Add(groupIndex);
                    }
                    AddAtomTree(model, group, groupIndex, f);
                }
            }
        }

        static void BuildFlat(
            OutlinerModel model,
            OutlinerNode scene,
            List<OutlinerAtomFacts> keep)
        {
            keep.Sort(CompareFactsName);
            for (int i = 0; i < keep.Count; i++)
                AddAtomTree(model, scene, 0, keep[i]);
        }

        static void BuildHierarchy(
            OutlinerModel model,
            OutlinerNode scene,
            List<OutlinerAtomFacts> keep)
        {
            var byUid = new Dictionary<string, OutlinerAtomFacts>(StringComparer.Ordinal);
            for (int i = 0; i < keep.Count; i++)
            {
                OutlinerAtomFacts f = keep[i];
                if (!byUid.ContainsKey(f.Uid))
                    byUid.Add(f.Uid, f);
            }

            var atomIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var pending = new List<OutlinerAtomFacts>(keep.Count);
            for (int i = 0; i < keep.Count; i++)
                pending.Add(keep[i]);

            int guard = 0;
            while (pending.Count > 0 && guard++ < 4000)
            {
                bool progressed = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    OutlinerAtomFacts f = pending[i];
                    int parentIndex = 0;
                    OutlinerNode parentNode = scene;
                    string want = HierarchyParentUid(f, byUid);
                    if (!string.IsNullOrEmpty(want))
                    {
                        int pidx;
                        if (!atomIndex.TryGetValue(want, out pidx))
                            continue;
                        parentIndex = pidx;
                        parentNode = model.Get(pidx);
                        if (parentNode == null) continue;
                    }
                    int created = AddAtomTree(model, parentNode, parentIndex, f);
                    atomIndex[f.Uid] = created;
                    pending.RemoveAt(i);
                    progressed = true;
                }
                if (!progressed)
                {
                    for (int i = 0; i < pending.Count; i++)
                    {
                        int created = AddAtomTree(model, scene, 0, pending[i]);
                        atomIndex[pending[i].Uid] = created;
                    }
                    pending.Clear();
                }
            }
        }

        static string HierarchyParentUid(
            OutlinerAtomFacts f,
            Dictionary<string, OutlinerAtomFacts> byUid)
        {
            if (!string.IsNullOrEmpty(f.ParentUid)
                && !string.Equals(f.ParentUid, f.Uid, StringComparison.Ordinal)
                && byUid.ContainsKey(f.ParentUid))
                return f.ParentUid;
            if (!string.IsNullOrEmpty(f.SubSceneUid)
                && !string.Equals(f.SubSceneUid, f.Uid, StringComparison.Ordinal)
                && byUid.ContainsKey(f.SubSceneUid))
                return f.SubSceneUid;
            return "";
        }

        static int AddAtomTree(
            OutlinerModel model,
            OutlinerNode parent,
            int parentIndex,
            OutlinerAtomFacts f)
        {
            int atomIndex = model.Nodes.Count;
            string label = string.IsNullOrEmpty(f.DisplayName) ? f.Uid : f.DisplayName;
            OutlinerNode atom = NewNode(
                model,
                "atom:" + f.Uid,
                OutlinerNodeKind.Atom,
                label,
                f.Type,
                parentIndex);
            atom.AtomUid = f.Uid;
            atom.Type = f.Type;
            atom.AtomKind = f.Kind;
            atom.PackageUid = f.PackageUid ?? "";
            atom.On = f.On;
            atom.Hidden = f.Hidden;
            atom.Collision = f.Collision;
            atom.Physics = f.Physics;
            atom.IsSystemProtected = f.IsSystemProtected;
            parent.ChildIndices.Add(atomIndex);

            return atomIndex;
        }

        static OutlinerNode NewNode(
            OutlinerModel model,
            string id,
            OutlinerNodeKind kind,
            string label,
            string sub,
            int parentIndex)
        {
            var n = new OutlinerNode();
            n.Id = id;
            n.Kind = kind;
            n.Label = label ?? "";
            n.SubLabel = sub ?? "";
            n.ParentIndex = parentIndex;
            model.Nodes.Add(n);
            if (!model.IndexById.ContainsKey(id))
                model.IndexById.Add(id, model.Nodes.Count - 1);
            return n;
        }

        static int CompareFactsName(OutlinerAtomFacts a, OutlinerAtomFacts b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            string la = string.IsNullOrEmpty(a.DisplayName) ? a.Uid : a.DisplayName;
            string lb = string.IsNullOrEmpty(b.DisplayName) ? b.Uid : b.DisplayName;
            int c = string.Compare(la, lb, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase);
        }
    }
}
