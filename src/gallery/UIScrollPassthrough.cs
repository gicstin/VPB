using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Allows a child UI element to be clickable while still forwarding scroll/drag events
    /// to a parent ScrollRect (so the list keeps scrolling when hovered).
    /// </summary>
    public class UIScrollPassthrough : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public ScrollRect target;

        public void OnScroll(PointerEventData eventData)
        {
            if (target == null) return;
            ExecuteEvents.Execute(target.gameObject, eventData, ExecuteEvents.scrollHandler);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null) return;
            ExecuteEvents.Execute(target.gameObject, eventData, ExecuteEvents.beginDragHandler);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null) return;
            ExecuteEvents.Execute(target.gameObject, eventData, ExecuteEvents.dragHandler);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (target == null) return;
            ExecuteEvents.Execute(target.gameObject, eventData, ExecuteEvents.endDragHandler);
        }
    }
}

