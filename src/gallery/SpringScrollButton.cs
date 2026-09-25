using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public class SpringScrollButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public ScrollRect scrollRect;

        public Color normalColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        public Color heldColor = new Color(0.15f, 0.75f, 0.2f, 0.95f);

        public float deadzoneFraction = 0.12f;

        public float maxViewportHeightsPerSecond = 3.5f;

        public float speedSmoothing = 22f;

        public float responsePower = 2.4f;

        private bool _held;
        private float _targetSpeedPx;
        private float _currentSpeedPx;
        private RectTransform _rt;
        private Image _img;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _img = GetComponent<Image>();
            ApplyHeldVisual();
        }

        private void OnDisable()
        {
            _held = false;
            _targetSpeedPx = 0f;
            _currentSpeedPx = 0f;
            ApplyHeldVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _held = true;
            ApplyHeldVisual();
            UpdateTargetFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_held) return;
            UpdateTargetFromPointer(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _held = false;
            _targetSpeedPx = 0f;
            _currentSpeedPx = 0f;
            ApplyHeldVisual();
        }

        private void ApplyHeldVisual()
        {
            if (_img == null) _img = GetComponent<Image>();
            if (_img == null) return;
            _img.color = _held ? heldColor : normalColor;
        }

        private void Update()
        {
            if (!_held) return;
            if (scrollRect == null) return;

            float dt = Time.unscaledDeltaTime;
            _currentSpeedPx = _targetSpeedPx;

            float scrollablePx = 0f;
            try
            {
                if (scrollRect.content != null && scrollRect.viewport != null)
                    scrollablePx = Mathf.Max(0f, scrollRect.content.rect.height - scrollRect.viewport.rect.height);
            }
            catch { scrollablePx = 0f; }

            if (scrollablePx <= 0.5f) return;

            float deltaNorm = (_currentSpeedPx * dt) / scrollablePx;
            float pos = scrollRect.verticalNormalizedPosition;
            pos += deltaNorm;
            if (pos < 0f) pos = 0f;
            else if (pos > 1f) pos = 1f;
            scrollRect.verticalNormalizedPosition = pos;
        }

        private void UpdateTargetFromPointer(PointerEventData eventData)
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null)
            {
                _targetSpeedPx = 0f;
                return;
            }

            Vector2 local;
            Camera cam = null;
            try { cam = eventData != null ? (eventData.pressEventCamera ?? eventData.enterEventCamera) : null; } catch { cam = null; }
            if (cam == null)
            {
                Canvas c = null;
                try { c = GetComponentInParent<Canvas>(); } catch { c = null; }
                if (c != null && c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    cam = null;
                }
                else
                {
                    cam = Camera.main;
                }
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, eventData.position, cam, out local))
            {
                _targetSpeedPx = 0f;
                return;
            }

            float halfH = 0f;
            try { halfH = _rt != null ? (_rt.rect.height * 0.5f) : 0f; } catch { halfH = 0f; }
            if (halfH <= 0.5f)
            {
                _targetSpeedPx = 0f;
                return;
            }

            float yN = Mathf.Clamp(local.y / halfH, -1f, 1f);
            float dzN = Mathf.Clamp01(deadzoneFraction);
            if (Mathf.Abs(yN) <= dzN)
            {
                _targetSpeedPx = 0f;
                return;
            }

            yN = yN > 0f
                ? (yN - dzN) / Mathf.Max(0.0001f, (1f - dzN))
                : (yN + dzN) / Mathf.Max(0.0001f, (1f - dzN));

            float t = Mathf.Clamp(yN, -1f, 1f);

            float sign = Mathf.Sign(t);
            float mag = Mathf.Pow(Mathf.Abs(t), Mathf.Max(1f, responsePower));

            float viewportH = 0f;
            try { viewportH = scrollRect != null && scrollRect.viewport != null ? scrollRect.viewport.rect.height : 0f; } catch { viewportH = 0f; }
            if (viewportH <= 0.5f) viewportH = 800f;

            float maxPxPerSec = Mathf.Max(10f, maxViewportHeightsPerSecond) * viewportH;
            _targetSpeedPx = sign * mag * maxPxPerSec;
        }

        public static GameObject Create(GameObject parent, ScrollRect targetScrollRect, float widthPx = 22f, float heightPx = 22f)
        {
            var go = UI.CreateChildRT(parent, "SpringScrollButton", AnchorPresets.middleCenter, new Vector2(widthPx, heightPx));

            var rr = go.AddComponent<RoundedRect>();
            rr.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = true;
            cg.interactable = true;

            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(widthPx, heightPx, 20f);
            bc.center = Vector3.zero;
            // UI collider must not participate in physics collisions with scene atoms.
            bc.isTrigger = true;

            var s = go.AddComponent<SpringScrollButton>();
            s.scrollRect = targetScrollRect;
            s.normalColor = rr.color;

            // Subtle border/hover feedback if the project has it.
            try { go.AddComponent<UIHoverBorder>(); } catch { }

            return go;
        }

        public void SetSize(float widthPx, float heightPx)
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt != null)
                _rt.sizeDelta = new Vector2(widthPx, heightPx);

            var bc = GetComponent<BoxCollider>();
            if (bc != null)
            {
                bc.size = new Vector3(widthPx, heightPx, bc.size.z > 0.1f ? bc.size.z : 20f);
                bc.isTrigger = true;
            }
        }
    }
}
