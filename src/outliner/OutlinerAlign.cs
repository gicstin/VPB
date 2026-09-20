using System.Collections.Generic;
using UnityEngine;

namespace VPB.Outliner
{
    internal enum OutlinerPasteMode
    {
        PositionAndRotation,
        Position,
        Rotation,
        Scale
    }

    internal static class OutlinerAlign
    {
        internal const float SpreadEpsilon = 1e-4f;

        internal static float Axis(Vector3 v, int axis)
        {
            return axis == 0 ? v.x : (axis == 1 ? v.y : v.z);
        }

        internal static Vector3 WithAxis(Vector3 v, int axis, float value)
        {
            if (axis == 0) v.x = value;
            else if (axis == 1) v.y = value;
            else v.z = value;
            return v;
        }

        internal static int MatchAxis(Atom anchor, List<Atom> atoms, int axis)
        {
            Transform at = OutlinerEdits.ControlTransform(anchor);
            if (at == null || atoms == null) return 0;
            float value = Axis(at.position, axis);
            int done = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null || a == anchor) continue;
                Transform t = OutlinerEdits.ControlTransform(a);
                if (t == null) continue;
                if (OutlinerEdits.WriteMainControllerPosition(a, WithAxis(t.position, axis, value))) done++;
            }
            return done;
        }

        internal static int MatchRotation(Atom anchor, List<Atom> atoms)
        {
            Transform at = OutlinerEdits.ControlTransform(anchor);
            if (at == null || atoms == null) return 0;
            Vector3 euler = at.rotation.eulerAngles;
            int done = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null || a == anchor) continue;
                if (OutlinerEdits.WriteMainControllerRotation(a, euler)) done++;
            }
            return done;
        }

        internal static bool ComputeSpreadOrder(float[] keys, int count, int[] order)
        {
            if (keys == null || order == null) return false;
            if (count < 3 || keys.Length < count || order.Length < count) return false;
            for (int i = 0; i < count; i++)
                order[i] = i;
            for (int i = 1; i < count; i++)
            {
                int slot = order[i];
                float k = keys[slot];
                int j = i - 1;
                while (j >= 0 && keys[order[j]] > k)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = slot;
            }
            return Mathf.Abs(keys[order[count - 1]] - keys[order[0]]) >= SpreadEpsilon;
        }

        internal static float SpreadTarget(float[] keys, int count, int[] order, int rank)
        {
            float lo = keys[order[0]];
            float hi = keys[order[count - 1]];
            return lo + (hi - lo) * rank / (count - 1);
        }

        internal static int Spread(List<Atom> atoms, int axis)
        {
            if (atoms == null || atoms.Count < 3) return 0;
            int n = atoms.Count;
            Atom[] live = new Atom[n];
            Vector3[] pos = new Vector3[n];
            float[] keys = new float[n];
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                Transform t = OutlinerEdits.ControlTransform(a);
                if (t == null) continue;
                live[m] = a;
                pos[m] = t.position;
                keys[m] = Axis(pos[m], axis);
                m++;
            }
            int[] order = new int[m];
            if (!ComputeSpreadOrder(keys, m, order)) return 0;

            int done = 0;
            for (int rank = 1; rank < m - 1; rank++)
            {
                int slot = order[rank];
                Vector3 next = WithAxis(pos[slot], axis, SpreadTarget(keys, m, order, rank));
                if (OutlinerEdits.WriteMainControllerPosition(live[slot], next)) done++;
            }
            return done;
        }
    }
}
