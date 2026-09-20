using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal sealed class OutlinerModel
    {
        internal readonly List<OutlinerNode> Nodes = new List<OutlinerNode>(64);
        internal readonly Dictionary<string, int> IndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        internal int RootIndex;
        internal int AtomCount;
        internal int SceneAtomCount;
        internal OutlinerGrouping Grouping;

        internal OutlinerModel()
        {
            RootIndex = -1;
        }

        internal OutlinerNode Get(int index)
        {
            if (index < 0 || index >= Nodes.Count) return null;
            return Nodes[index];
        }

        internal OutlinerNode Find(string id)
        {
            int idx;
            if (string.IsNullOrEmpty(id) || !IndexById.TryGetValue(id, out idx)) return null;
            return Get(idx);
        }

        internal void CollectVisible(
            HashSet<string> expanded,
            List<int> into,
            List<int> depths)
        {
            into.Clear();
            depths.Clear();
            if (RootIndex < 0) return;
            WalkVisible(RootIndex, 0, expanded, into, depths, 0);
        }

        internal static void ExpandDefaultCategories(
            OutlinerModel model,
            HashSet<string> expanded,
            HashSet<string> userCollapsed)
        {
            if (model == null || expanded == null) return;
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                OutlinerNode n = model.Nodes[i];
                if (n == null || n.Kind != OutlinerNodeKind.TypeGroup) continue;
                if (userCollapsed != null && userCollapsed.Contains(n.Id)) continue;
                expanded.Add(n.Id);
            }
        }

        void WalkVisible(
            int index,
            int depth,
            HashSet<string> expanded,
            List<int> into,
            List<int> depths,
            int guard)
        {
            if (guard > 8000) return;
            OutlinerNode node = Get(index);
            if (node == null) return;
            bool skipRow = node.Kind == OutlinerNodeKind.Scene;
            if (!skipRow)
            {
                into.Add(index);
                depths.Add(depth);
            }
            bool open = skipRow || (expanded != null && expanded.Contains(node.Id));
            if (!open) return;
            int childDepth = skipRow ? depth : depth + 1;
            for (int i = 0; i < node.ChildIndices.Count; i++)
                WalkVisible(node.ChildIndices[i], childDepth, expanded, into, depths, guard + 1);
        }
    }
}
