using System;
using UnityEngine;

namespace VPB.src.util
{
    public static class AtomGenderUtils
    {
        static bool AtomActive(Atom atom)
        {
            if (atom == null) return false;
            if (atom.type != "Person") return false;

            try
            {
                if (!atom.on) return false;
                if (atom.containingSubScene != null
                    && atom.containingSubScene.containingAtom != null
                    && !atom.containingSubScene.containingAtom.on)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        // selectedCharacter is VaM's authoritative gender; GetComponentInChildren<DAZCharacter>() returns the first active child, which AltFuta makes ambiguous.
        static DAZCharacter GetSelectedCharacter(Atom atom)
        {
            try
            {
                DAZCharacterSelector selector = atom.GetStorableByID("geometry") as DAZCharacterSelector;
                return selector != null ? selector.selectedCharacter : null;
            }
            catch
            {
                return null;
            }
        }

        static bool IsFutaCharacter(DAZCharacter ch)
        {
            if (ch == null) return false;

            string dn = "";
            string nm = "";
            try { dn = ch.displayName ?? ""; } catch { dn = ""; }
            try { nm = ch.name ?? ""; } catch { nm = ""; }

            return LooseVapGenderProbe.IsFutaCharacterName(dn)
                || LooseVapGenderProbe.IsFutaCharacterName(nm);
        }

        public static bool IsFuta(Atom atom)
        {
            if (!AtomActive(atom)) return false;
            return IsFutaCharacter(GetSelectedCharacter(atom));
        }

        public static bool IsMale(Atom atom)
        {
            if (!AtomActive(atom)) return false;

            DAZCharacter ch = GetSelectedCharacter(atom);
            if (ch == null) return false;

            return ch.isMale;
        }

        public static bool IsFemale(Atom atom)
        {
            if (!AtomActive(atom)) return false;
            return !IsMale(atom);
        }

        public static LooseVapGenderProbe.Gender ClassifyForBadge(Atom atom)
        {
            if (atom == null) return LooseVapGenderProbe.Gender.Unknown;
            try { if (atom.type != "Person") return LooseVapGenderProbe.Gender.Unknown; }
            catch { return LooseVapGenderProbe.Gender.Unknown; }

            DAZCharacterSelector selector = null;
            try { selector = atom.GetStorableByID("geometry") as DAZCharacterSelector; }
            catch { selector = null; }
            DAZCharacter ch = null;
            try { if (selector != null) ch = selector.selectedCharacter; }
            catch { ch = null; }
            if (ch == null) return LooseVapGenderProbe.Gender.Unknown;
            if (IsFutaCharacter(ch)) return LooseVapGenderProbe.Gender.Futa;

            bool useFemaleMorphsOnMale = false;
            try
            {
                JSONStorableBool flag = selector.GetBoolJSONParam("useFemaleMorphsOnMale");
                if (flag != null) useFemaleMorphsOnMale = flag.val;
            }
            catch { }

            try
            {
                if (ch.isMale)
                    return useFemaleMorphsOnMale
                        ? LooseVapGenderProbe.Gender.Futa
                        : LooseVapGenderProbe.Gender.Male;
                return LooseVapGenderProbe.Gender.Female;
            }
            catch { return LooseVapGenderProbe.Gender.Unknown; }
        }
    }
}
