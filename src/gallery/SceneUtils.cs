using UnityEngine;

namespace VPB
{
    public static class SceneUtils
    {
        /// <summary>
        /// VaM character atom types usable as gallery/clothing targets. <c>InvisiblePerson</c> is omitted
        /// when only <c>Person</c> is checked, which breaks the target picker for invisible characters.
        /// </summary>
        public static bool IsPersonLikeAtomType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return type == "Person" || type == "InvisiblePerson";
        }

        public static bool IsPersonLikeAtom(Atom atom)
        {
            if (atom == null) return false;
            try { return IsPersonLikeAtomType(atom.type); }
            catch { return false; }
        }

        public static Atom DetectAtom(Vector2 screenPos, Camera cam, out string statusMsg)
        {
            RaycastHit hit;
            return RaycastAtom(screenPos, cam, out statusMsg, out hit);
        }

        public static Atom RaycastAtom(Vector2 screenPos, Camera cam, out string statusMsg, out RaycastHit hit)
        {
            statusMsg = "";
            hit = new RaycastHit();
            if (cam == null) return null;

            Ray ray = cam.ScreenPointToRay(screenPos);

            // Mask out UI layer (5) and Ignore Raycast (2)
            int layerMask = Physics.DefaultRaycastLayers & ~(1 << 5);

            if (Physics.Raycast(ray, out hit, 1000f, layerMask))
            {
                Atom atom = hit.collider.GetComponentInParent<Atom>();
                if (atom != null && IsPersonLikeAtom(atom))
                {
                    statusMsg = $"Target: {atom.name}";
                    return atom;
                }
                // Return the atom for drag-drop logic even if it's not a Person, but skip message processing
                return atom;
            }
            return null;
        }
    }
}
