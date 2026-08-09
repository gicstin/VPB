using System;
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

    /// <summary>
    /// Chip X dismiss that survives parent chip-drag / ScrollRect steal.
    /// Owns IBeginDrag so EventSystem does not walk up to <see cref="TitleSearchChipDragSource"/>;
    /// fires on pointer-up when movement stays small (Button.onClick dies once dragging=true).
    /// </summary>
    public sealed class UIChipDismissClick : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action OnDismiss;

        private Vector2 _downPos;
        private bool _armed;
        private const float MaxMovePx = 12f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                _armed = false;
                return;
            }
            _armed = true;
            _downPos = eventData.position;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_armed) return;
            _armed = false;
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            if ((eventData.position - _downPos).sqrMagnitude > MaxMovePx * MaxMovePx) return;
            try { if (OnDismiss != null) OnDismiss(); } catch { }
        }

        public void OnBeginDrag(PointerEventData eventData) { }
        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) { }

        private void OnDisable()
        {
            _armed = false;
        }
    }
}
