using System;
using UnityEngine;

namespace VPB
{
    internal static class VpbSkinRegionMap
    {
        internal const int GenderFemale = 0;
        internal const int GenderMale = 1;

        private static byte[] s_female;
        private static byte[] s_male;
        private static bool s_femaleLoadTried;
        private static bool s_maleLoadTried;

        internal static byte[] Get(int gender)
        {
            return gender == GenderMale ? s_male : s_female;
        }

        internal static bool IsReady(int gender)
        {
            byte[] map = Get(gender);
            return map != null && map.Length > 0;
        }

        internal static void EnsureLoaded(int gender)
        {
            if (gender == GenderMale)
            {
                if (s_maleLoadTried) return;
                s_maleLoadTried = true;
                s_male = VpbLocalDatabase.TryLoadSkinRegionMap(GenderMale);
            }
            else
            {
                if (s_femaleLoadTried) return;
                s_femaleLoadTried = true;
                s_female = VpbLocalDatabase.TryLoadSkinRegionMap(GenderFemale);
            }
        }

        internal static bool EnsureHarvested(Atom atom)
        {
            if (atom == null) return false;
            if (IsReady(GenderFemale) && IsReady(GenderMale)) return true;

            DAZCharacterSelector selector = FindSelector(atom);
            if (selector == null) return false;

            int gender;
            try { gender = selector.gender == DAZCharacterSelector.Gender.Male ? GenderMale : GenderFemale; }
            catch { gender = GenderFemale; }

            EnsureLoaded(gender);
            if (IsReady(gender)) return true;

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
            if (skin == null) return false;

            DAZBone[] strongest = null;
            try { strongest = skin.strongestDAZBone; }
            catch { strongest = null; }
            if (strongest == null || strongest.Length == 0) return false;

            byte[] map = new byte[strongest.Length];
            int classified = 0;
            for (int i = 0; i < strongest.Length; i++)
            {
                DAZBone b = strongest[i];
                if (b == null) { map[i] = (byte)VpbBodyRegion.Unknown; continue; }

                string id = null;
                try { id = b.id; } catch { id = null; }
                if (string.IsNullOrEmpty(id))
                {
                    try { id = b.name; } catch { id = null; }
                }

                VpbBodyRegion r = VpbBodyRegions.FromBoneId(id);
                if (r == VpbBodyRegion.Unknown) { map[i] = (byte)VpbBodyRegion.Unknown; continue; }
                map[i] = (byte)r;
                classified++;
            }

            if (classified < map.Length / 2)
            {
                return false;
            }

            if (gender == GenderMale) s_male = map; else s_female = map;
            VpbLocalDatabase.TrySaveSkinRegionMap(gender, map);

            return true;
        }

        internal static DAZCharacterSelector FindSelector(Atom atom)
        {
            if (atom == null) return null;
            try
            {
                DAZCharacterSelector s = atom.GetStorableByID("geometry") as DAZCharacterSelector;
                if (s != null) return s;
            }
            catch { }
            try { return atom.GetComponentInChildren<DAZCharacterSelector>(true); }
            catch { return null; }
        }

        internal static void ResetSessionState()
        {
            s_female = null;
            s_male = null;
            s_femaleLoadTried = false;
            s_maleLoadTried = false;
        }
    }
}
