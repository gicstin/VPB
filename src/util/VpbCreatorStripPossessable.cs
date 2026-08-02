using System;
using UnityEngine;

namespace VPB.src.util
{
    /// <summary>
    /// Creator Strip possessable reset — clear all FreeControllerV3.possessable on Person,
    /// then optionally enable head + hands for VR possess (gender-filtered).
    /// Cold path only (post scene rebuild).
    /// </summary>
    public static class VpbCreatorStripPossessable
    {
        private static readonly string[] HeadHandsControlIds =
        {
            "headControl",
            "lHandControl",
            "rHandControl",
        };

        /// <summary>
        /// Clear possessable on every FreeControllerV3 of <paramref name="person"/>.
        /// </summary>
        public static int ClearAll(Atom person)
        {
            if (person == null) return 0;
            FreeControllerV3[] fcs = null;
            try { fcs = person.freeControllers; } catch { fcs = null; }
            if (fcs == null || fcs.Length == 0) return 0;

            int n = 0;
            for (int i = 0; i < fcs.Length; i++)
            {
                FreeControllerV3 fc = fcs[i];
                if (fc == null) continue;
                try
                {
                    if (!fc.possessable) continue;
                    fc.possessable = false;
                    n++;
                }
                catch { }
            }
            return n;
        }

        /// <summary>
        /// Enable head + left/right hand possessable (VR possess targets).
        /// </summary>
        public static int EnableHeadAndHands(Atom person)
        {
            if (person == null) return 0;
            int n = 0;
            for (int i = 0; i < HeadHandsControlIds.Length; i++)
            {
                FreeControllerV3 fc = null;
                try { fc = person.GetStorableByID(HeadHandsControlIds[i]) as FreeControllerV3; }
                catch { fc = null; }
                if (fc == null) continue;
                try
                {
                    fc.possessable = true;
                    n++;
                }
                catch { }
            }
            return n;
        }

        /// <summary>
        /// Apply strip possessable policy to one Person.
        /// Clear runs first when requested; head/hands only when gender matches add flags.
        /// Unknown gender: clear only (never auto-add).
        /// </summary>
        public static void ApplyToPerson(
            Atom person,
            bool clearAll,
            bool addForMale,
            bool addForFemale)
        {
            if (person == null) return;
            if (!SceneUtils.IsPersonLikeAtom(person)) return;

            if (clearAll)
                ClearAll(person);

            if (!addForMale && !addForFemale) return;

            bool male = false;
            bool female = false;
            try { male = AtomGenderUtils.IsMale(person); } catch { male = false; }
            try { female = AtomGenderUtils.IsFemale(person); } catch { female = false; }

            // Futa folds to male in AtomGenderUtils — treat as male for possess add.
            bool want =
                (addForMale && male)
                || (addForFemale && female);
            if (!want) return;

            EnableHeadAndHands(person);
        }

        /// <summary>
        /// Apply policy to every Person / InvisiblePerson currently in the scene.
        /// </summary>
        public static int ApplyToAllPersonsInScene(
            bool clearAll,
            bool addForMale,
            bool addForFemale)
        {
            if (!clearAll && !addForMale && !addForFemale) return 0;
            SuperController sc = SuperController.singleton;
            if (sc == null) return 0;

            System.Collections.Generic.List<Atom> atoms = null;
            try { atoms = sc.GetAtoms(); } catch { atoms = null; }
            if (atoms == null) return 0;

            int touched = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                if (!SceneUtils.IsPersonLikeAtom(a)) continue;
                ApplyToPerson(a, clearAll, addForMale, addForFemale);
                touched++;
            }
            return touched;
        }
    }
}
