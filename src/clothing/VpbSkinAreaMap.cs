using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    internal static class VpbSkinAreaMap
    {
        private const float CellSize = 0.025f;

        private static int[] s_femaleCells;
        private static int[] s_maleCells;
        private static bool s_femaleTried;
        private static bool s_maleTried;

        internal static int[] Get(int gender)
        {
            return gender == VpbSkinRegionMap.GenderMale ? s_maleCells : s_femaleCells;
        }

        internal static bool IsReady(int gender)
        {
            int[] m = Get(gender);
            return m != null && m.Length > 0;
        }

        internal static void EnsureLoaded(int gender)
        {
            if (gender == VpbSkinRegionMap.GenderMale)
            {
                if (s_maleTried) return;
                s_maleTried = true;
                s_maleCells = VpbLocalDatabase.TryLoadSkinAreaMap(VpbSkinRegionMap.GenderMale);
            }
            else
            {
                if (s_femaleTried) return;
                s_femaleTried = true;
                s_femaleCells = VpbLocalDatabase.TryLoadSkinAreaMap(VpbSkinRegionMap.GenderFemale);
            }
        }

        internal static void ResetSessionState()
        {
            s_femaleCells = null;
            s_maleCells = null;
            s_femaleTried = false;
            s_maleTried = false;
        }

        internal static bool EnsureHarvested(Atom atom)
        {
            if (atom == null) return false;
            if (IsReady(VpbSkinRegionMap.GenderFemale) && IsReady(VpbSkinRegionMap.GenderMale)) return true;

            DAZCharacterSelector selector = VpbSkinRegionMap.FindSelector(atom);
            if (selector == null) return false;

            int gender;
            try { gender = selector.gender == DAZCharacterSelector.Gender.Male ? VpbSkinRegionMap.GenderMale : VpbSkinRegionMap.GenderFemale; }
            catch { gender = VpbSkinRegionMap.GenderFemale; }

            EnsureLoaded(gender);
            if (IsReady(gender)) return true;

            Vector3[] verts = TryGetBaseVertices(atom, selector);
            if (verts == null || verts.Length == 0) return false;

            var dense = new Dictionary<int, int>(4096);
            int[] cells = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                int key = PackCell(v);
                int id;
                if (!dense.TryGetValue(key, out id))
                {
                    id = dense.Count;
                    dense.Add(key, id);
                }
                cells[i] = id;
            }

            if (dense.Count < 64)
            {
                return false;
            }

            if (gender == VpbSkinRegionMap.GenderMale) s_maleCells = cells; else s_femaleCells = cells;
            VpbLocalDatabase.TrySaveSkinAreaMap(gender, cells);

            return true;
        }

        private static Vector3[] TryGetBaseVertices(Atom atom, DAZCharacterSelector selector)
        {
            DAZSkinV2 skin = null;
            try
            {
                DAZCharacter ch = selector.selectedCharacter;
                if (ch != null) skin = ch.skinForClothes ?? ch.skin;
            }
            catch { skin = null; }
            if (skin == null)
            {
                try { skin = atom.GetComponentInChildren<DAZSkinV2>(true); }
                catch { skin = null; }
            }
            if (skin == null) return null;

            try
            {
                DAZMesh mesh = skin.dazMesh;
                if (mesh == null) return null;
                return mesh.UVVertices;
            }
            catch { return null; }
        }

        private static int PackCell(Vector3 v)
        {
            int ix = Mathf.FloorToInt(v.x / CellSize) + 256;
            int iy = Mathf.FloorToInt(v.y / CellSize) + 256;
            int iz = Mathf.FloorToInt(v.z / CellSize) + 256;
            if (ix < 0) ix = 0; if (ix > 1023) ix = 1023;
            if (iy < 0) iy = 0; if (iy > 1023) iy = 1023;
            if (iz < 0) iz = 0; if (iz > 1023) iz = 1023;
            return ix | (iy << 10) | (iz << 20);
        }
    }
}
