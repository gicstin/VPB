using System;
using UnityEngine;
using UnityEngine.EventSystems;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// Shared float-window move: parent-local pointer tracking (WorldSpace VR + Overlay desktop).
    /// Screen <see cref="PointerEventData.delta"/> breaks VR laser / parallax — never use for floats.
    /// </summary>
    public sealed class UIFloatPanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Target;
        public Action OnMoved;
        /// <summary>VR: keep panel near host (parallax safety). Desktop: off.</summary>
        public bool ClampPositionInVr = true;

        private Camera _dragCam;
        private Vector2 _grabOffsetLocal;
        private bool _active;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Target == null || eventData == null) return;
            if (!AcceptPointerButton(eventData)) return;

            RectTransform parent = Target.parent as RectTransform;
            if (parent == null) return;

            _dragCam = ResolveDragCamera(Target, eventData);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, _dragCam, out local))
                return;

            _grabOffsetLocal = local - Target.anchoredPosition;
            _active = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_active || Target == null || eventData == null) return;
            RectTransform parent = Target.parent as RectTransform;
            if (parent == null) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, _dragCam, out local))
                return;

            Vector2 pos = local - _grabOffsetLocal;
            if (ClampPositionInVr)
                pos = ClampFloatPosForVr(parent, Target, pos);
            Target.anchoredPosition = pos;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_active) return;
            _active = false;
            if (OnMoved != null) OnMoved();
        }

        private void OnDisable()
        {
            _active = false;
        }

        internal static bool AcceptPointerButton(PointerEventData eventData)
        {
            // VR laser often not Left; desktop keep LMB-only to avoid RMB thrash.
            try
            {
                if (XrUtils.IsVrActive()) return true;
            }
            catch { }
            return eventData.button == PointerEventData.InputButton.Left;
        }

        internal static Camera ResolveDragCamera(RectTransform target, PointerEventData eventData)
        {
            Canvas canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;

            Camera cam = eventData != null ? eventData.pressEventCamera : null;
            if (cam == null && canvas != null)
                cam = canvas.worldCamera;
            if (cam == null)
                cam = Camera.main;
            return cam;
        }

        /// <summary>
        /// Clamp panel geometric center (pivot-aware) — equal room left/right and top/bottom.
        /// Clamping top-left pivot alone favored right/bottom (window grows that way).
        /// Origin = (0,0): floats use center anchors on canvas; parent.rect.center can skew bounds.
        /// Only during drag — collapse does not re-run this.
        /// </summary>
        internal static Vector2 ClampFloatPosForVr(RectTransform parent, RectTransform target, Vector2 anchoredPos)
        {
            bool vr = false;
            try { vr = XrUtils.IsVrActive(); } catch { }
            if (!vr || parent == null || target == null) return anchoredPos;

            Vector2 origin = Vector2.zero;
            float halfW = Mathf.Abs(parent.rect.width) * 0.5f;
            float halfH = Mathf.Abs(parent.rect.height) * 0.5f;
            if (halfW < 1f) halfW = GalleryUiDesignTokens.FloatVrFallbackTravelRef;
            if (halfH < 1f) halfH = GalleryUiDesignTokens.FloatVrFallbackTravelRef;

            float maxX = ResolveVrTravel(halfW);
            float maxY = ResolveVrTravel(halfH);

            Vector2 size = target.sizeDelta;
            Vector2 piv = target.pivot;
            Vector2 toCenter = new Vector2((0.5f - piv.x) * size.x, (0.5f - piv.y) * size.y);
            Vector2 center = anchoredPos + toCenter;

            center.x = Mathf.Clamp(center.x, origin.x - maxX, origin.x + maxX);
            center.y = Mathf.Clamp(center.y, origin.y - maxY, origin.y + maxY);
            return center - toCenter;
        }

        /// <summary>Travel radius along one axis from host origin (local px).</summary>
        private static float ResolveVrTravel(float parentHalfExtent)
        {
            if (parentHalfExtent < 1f)
                return GalleryUiDesignTokens.FloatVrFallbackTravelRef;

            float travel = parentHalfExtent * GalleryUiDesignTokens.FloatVrTravelParentFraction;
            float ceil = GalleryUiDesignTokens.FloatVrMaxTravelRef;
            if (travel > ceil)
                travel = ceil;
            return travel;
        }
    }

    /// <summary>
    /// Shared float-window resize from bottom-right grip.
    /// Parent-local pointer deltas (not screen delta) so VR WorldSpace works.
    /// Incremental size — safe for top-left and center pivots.
    /// Does not re-clamp position (collapse/resize must not yank title).
    /// </summary>
    public sealed class UIFloatPanelResize : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Target;
        public Func<Vector2> GetMinSize;
        public Func<Vector2> GetMaxSize;
        public Vector2 MinSizeFallback = new Vector2(220f, 200f);
        public Vector2 MaxSizeFallback = new Vector2(960f, 900f);
        /// <summary>Fired each drag tick (live layout).</summary>
        public Action OnResizing;
        /// <summary>Fired on drag end (persist / rebuild).</summary>
        public Action OnResized;

        private Camera _dragCam;
        private Vector2 _lastLocal;
        private bool _active;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Target == null || eventData == null) return;
            if (!UIFloatPanelDrag.AcceptPointerButton(eventData)) return;

            RectTransform parent = Target.parent as RectTransform;
            if (parent == null) return;

            _dragCam = UIFloatPanelDrag.ResolveDragCamera(Target, eventData);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, _dragCam, out local))
                return;

            _lastLocal = local;
            _active = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_active || Target == null || eventData == null) return;
            RectTransform parent = Target.parent as RectTransform;
            if (parent == null) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, _dragCam, out local))
                return;

            Vector2 d = local - _lastLocal;
            _lastLocal = local;

            Vector2 size = Target.sizeDelta;
            size.x += d.x;
            size.y -= d.y;

            Vector2 min = GetMinSize != null ? GetMinSize() : MinSizeFallback;
            Vector2 max = GetMaxSize != null ? GetMaxSize() : MaxSizeFallback;
            size.x = Mathf.Clamp(size.x, min.x, max.x);
            size.y = Mathf.Clamp(size.y, min.y, max.y);
            Target.sizeDelta = size;

            if (OnResizing != null) OnResizing();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_active) return;
            _active = false;
            if (OnResized != null) OnResized();
        }

        private void OnDisable()
        {
            _active = false;
        }
    }
}
