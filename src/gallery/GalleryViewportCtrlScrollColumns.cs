using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// On desktop, Ctrl (or Cmd) + wheel changes grid column count like +/- zoom buttons; plain wheel still scrolls.
    /// Placed on <see cref="ScrollRect.viewport"/> so this handler runs before <see cref="ScrollRect"/> in hierarchy lookup.
    /// </summary>
    public sealed class GalleryViewportCtrlScrollColumns : MonoBehaviour, IScrollHandler
    {
        private GalleryPanel _panel;
        private ScrollRect _scrollRect;

        public static void TryAttach(GalleryPanel panel, ScrollRect scrollRect)
        {
            if (panel == null || scrollRect == null || scrollRect.viewport == null) return;
            GameObject go = scrollRect.viewport.gameObject;
            var c = go.GetComponent<GalleryViewportCtrlScrollColumns>();
            if (c == null) c = go.AddComponent<GalleryViewportCtrlScrollColumns>();
            c._panel = panel;
            c._scrollRect = scrollRect;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_scrollRect == null) return;
            if (Mathf.Abs(eventData.scrollDelta.y) <= 0.01f) return;

            bool mod = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (mod && _panel != null)
            {
                // Same mapping as footer zoom buttons: wheel up → fewer columns (zoom in); wheel down → more columns (zoom out).
                int delta = eventData.scrollDelta.y > 0 ? -1 : 1;
                _panel.ApplyCtrlScrollToGridColumns(delta);
                return;
            }

            ExecuteEvents.Execute(_scrollRect.gameObject, eventData, ExecuteEvents.scrollHandler);
        }
    }
}
