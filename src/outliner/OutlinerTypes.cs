using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal enum OutlinerNodeKind
    {
        Scene = 0,
        TypeGroup = 1,
        Atom = 2
    }

    internal enum OutlinerGrouping
    {
        TypeGroups = 0,
        Hierarchy = 1,
        Flat = 2
    }

    internal enum OutlinerLayoutMode
    {
        Rail = 0,
        Float = 1
    }

    internal enum OutlinerParamKind
    {
        Float = 0,
        Bool = 1,
        String = 2,
        StringChooser = 3,
        Color = 4,
        Url = 5,
        Action = 6,
        Disabled = 7
    }

    internal static class OutlinerRowLayout
    {
        internal const int MaxVisibleDepth = 3;

        internal static int ClampVisibleDepth(int depth)
        {
            if (depth < 0) return 0;
            if (depth > MaxVisibleDepth) return MaxVisibleDepth;
            return depth;
        }

        internal static float IndentPx(int depth, float indentStep)
        {
            return ClampVisibleDepth(depth) * indentStep;
        }

        internal static float IconX(int depth, float indentStep, float chevronSlot, float hairGap)
        {
            return IndentPx(depth, indentStep) + chevronSlot + hairGap;
        }

        internal static float TextLeft(
            int depth, float indentStep, float chevronSlot, float iconSize, float hairGap, float tightGap, bool showIcon)
        {
            if (showIcon)
                return IconX(depth, indentStep, chevronSlot, hairGap) + iconSize + tightGap;
            return IndentPx(depth, indentStep) + chevronSlot + tightGap;
        }

        internal static float RowStridePx(float rowHeight, float gap)
        {
            if (rowHeight < 0f) rowHeight = 0f;
            if (gap < 0f) gap = 0f;
            return rowHeight + gap;
        }
    }

    internal static class OutlinerSplitLayout
    {
        internal static float ClampTreeShare(float split)
        {
            if (split <= 0.01f) return 0f;
            if (split >= 0.99f) return 1f;
            return split;
        }

        internal static float RailInspectorShare(float treeShare)
        {
            return 1f - treeShare;
        }
    }

    internal sealed class OutlinerAtomFacts
    {
        internal string Uid;
        internal string Type;
        internal string DisplayName;
        internal CreatorStripKeepKind Kind;
        internal bool On;
        internal bool Hidden;
        internal bool Collision;
        internal bool Physics;
        internal bool IsSystemProtected;
        internal string ParentUid;
        internal string SubSceneUid;
        internal string PackageUid;

        internal OutlinerAtomFacts()
        {
            Uid = "";
            Type = "";
            DisplayName = "";
            Kind = CreatorStripKeepKind.Other;
            On = true;
            Hidden = false;
            Collision = true;
            Physics = true;
            ParentUid = "";
            SubSceneUid = "";
            PackageUid = "";
        }
    }

    internal sealed class OutlinerNode
    {
        internal string Id;
        internal OutlinerNodeKind Kind;
        internal int ParentIndex;
        internal string Label;
        internal string SubLabel;
        internal string AtomUid;
        internal string Type;
        internal CreatorStripKeepKind AtomKind;
        internal string PackageUid;
        internal string StorableId;
        internal string ParamId;
        internal bool On;
        internal bool Hidden;
        internal bool Collision;
        internal bool Physics;
        internal bool IsSystemProtected;
        internal readonly List<int> ChildIndices = new List<int>(4);

        internal OutlinerNode()
        {
            Id = "";
            Kind = OutlinerNodeKind.Scene;
            ParentIndex = -1;
            Label = "";
            SubLabel = "";
            AtomUid = "";
            Type = "";
            AtomKind = CreatorStripKeepKind.None;
            PackageUid = "";
            StorableId = "";
            ParamId = "";
            On = true;
            Collision = true;
            Physics = true;
        }
    }

    internal sealed class OutlinerParamDescriptor
    {
        internal string StorableId;
        internal string ParamId;
        internal OutlinerParamKind Kind;
        internal string Label;
        internal string DisabledReason;
        internal float Min;
        internal float Max;
        internal string[] Choices;
        internal int Order;

        internal OutlinerParamDescriptor()
        {
            StorableId = "";
            ParamId = "";
            Kind = OutlinerParamKind.Float;
            Label = "";
            DisabledReason = "";
            Min = 0f;
            Max = 1f;
            Choices = null;
            Order = int.MaxValue;
        }

        internal string QualifiedId
        {
            get { return StorableId + "/" + ParamId; }
        }
    }

    internal sealed class OutlinerUndoRecord
    {
        internal string Key;
        internal string Label;
        internal string Before;
        internal string After;
        internal float Time;

        internal OutlinerUndoRecord()
        {
            Key = "";
            Label = "";
            Before = "";
            After = "";
        }
    }
}
