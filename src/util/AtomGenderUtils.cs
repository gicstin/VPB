using System;
using UnityEngine;

namespace VPB.src.util
{
    public static class AtomGenderUtils
    {
        public static bool IsFuta(Atom atom)
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

            DAZCharacter ch = null;
            try { ch = atom.GetComponentInChildren<DAZCharacter>(); } catch { ch = null; }
            string name = "";
            try { name = ch != null ? (ch.name ?? "") : ""; } catch { name = ""; }
            if (string.IsNullOrEmpty(name)) return false;

            return name.StartsWith("futa", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMale(Atom atom)
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

            DAZCharacter ch = null;
            try { ch = atom.GetComponentInChildren<DAZCharacter>(); } catch { ch = null; }
            string name = "";
            try { name = ch != null ? (ch.name ?? "") : ""; } catch { name = ""; }
            if (string.IsNullOrEmpty(name)) return false;

            return name.StartsWith("male", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("futa", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFemale(Atom atom)
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

            return !IsMale(atom);
        }
    }
}

