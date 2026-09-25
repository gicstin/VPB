using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public sealed class GalleryViewportCtrlScrollColumns : MonoBehaviour, IScrollHandler
    {
        private GalleryPanel _panel;
        private ScrollRect _scrollRect;
        private float _zoomNotchAccum;

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
                int notches = VpbScrollTuning.TakeNotches(ref _zoomNotchAccum, eventData.scrollDelta.y);
                if (notches == 0) return;
                _panel.ApplyCtrlScrollToGridColumns(notches > 0 ? -1 : 1);
                return;
            }

            ExecuteEvents.Execute(_scrollRect.gameObject, eventData, ExecuteEvents.scrollHandler);
        }
    }
}
