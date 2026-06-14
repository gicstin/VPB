using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal int VrPointerHoverCount => hoverCount;

        internal bool IsVrPointerOverGalleryPane()
        {
            if (!IsVisible) return false;
            if (canvas == null || !canvas.enabled) return false;

            if (hoverCount > 0) return true;

            try
            {
                if (IsPointerInsideGalleryWindowRect()) return true;
            }
            catch { }

            if (currentPointerData != null && currentPointerData.pointerCurrentRaycast.isValid)
            {
                GameObject hit = currentPointerData.pointerCurrentRaycast.gameObject;
                if (hit != null && canvas.transform != null && hit.transform.IsChildOf(canvas.transform))
                    return true;
            }

            return false;
        }

        internal ScrollRect ResolveScrollRectForVrThumbstick()
        {
            if (currentPointerData != null && currentPointerData.pointerCurrentRaycast.isValid)
            {
                GameObject hit = currentPointerData.pointerCurrentRaycast.gameObject;
                if (hit != null)
                {
                    GameObject handler = ExecuteEvents.GetEventHandler<IScrollHandler>(hit);
                    if (handler != null)
                    {
                        ScrollRect sr = handler.GetComponent<ScrollRect>();
                        if (sr != null && sr.vertical) return sr;
                    }
                }
            }

            return scrollRect;
        }
    }
}
