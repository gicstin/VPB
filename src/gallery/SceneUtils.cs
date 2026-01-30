using UnityEngine;

namespace VPB
{
    public static class SceneUtils
    {
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
                if (atom != null && atom.type == "Person")
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
