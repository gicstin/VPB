using UnityEngine;
using UnityEngine.EventSystems;

namespace VPB
{
    public class UIDragBlocker : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public void OnBeginDrag(PointerEventData eventData) { eventData.useDragThreshold = false; }
        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) { }
    }
}
