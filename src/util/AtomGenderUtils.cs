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

        public static bool IsFuta(Atom atom)
        {
            if (!AtomActive(atom)) return false;

            DAZCharacter ch = GetSelectedCharacter(atom);
            if (ch == null) return false;

            string dn = "";
            string nm = "";
            try { dn = ch.displayName ?? ""; } catch { dn = ""; }
            try { nm = ch.name ?? ""; } catch { nm = ""; }

            return dn.StartsWith("futa", StringComparison.OrdinalIgnoreCase)
                || nm.StartsWith("futa", StringComparison.OrdinalIgnoreCase);
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
    }
}
