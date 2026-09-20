using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB.Outliner
{
    public sealed class OutlinerSpringAxis : MonoBehaviour,
        IPointerDownHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerUpHandler
    {
        public const float DeadzoneFraction = 0.12f;
        public const float ResponsePower = 2.4f;
        public const float MaxFrameSeconds = 0.05f;

        public const float PositionMetersPerSecondMin = 0.02f;
        public const float PositionMetersPerSecondMax = 2f;
        public const float RotationDegreesPerSecondMin = 2f;
        public const float RotationDegreesPerSecondMax = 180f;
        public const float ScalePerSecondMin = 0.01f;
        public const float ScalePerSecondMax = 0.8f;

        public enum Kind
        {
            Position = 0,
            Rotation = 1,
            Scale = 2
        }

        public System.Action OnDriveBegin;
        public System.Action<float, float> OnDriveTick;
        public System.Action OnDriveEnd;

        RectTransform _rt;
        RectTransform _handleRt;
        RectTransform _fillRt;
        Image _handleImg;
        Image _fillImg;
        Canvas _canvas;
        Color _handleIdle;
        Color _handleHeld;
        float _handleW;
        PointerEventData _pointer;
        bool _pressing;
        bool _driving;
        float _rate;
        float _handleX;
        bool _fillShown;

        public bool IsDriving { get { return _driving; } }

        public static float PullToRate(float localX, float halfWidth, float handleHalf, float power)
        {
            if (halfWidth <= 0.5f) return 0f;
            float dz = Mathf.Max(handleHalf, halfWidth * DeadzoneFraction);
            if (dz >= halfWidth) dz = halfWidth * DeadzoneFraction;
            float x = Mathf.Clamp(localX, -halfWidth, halfWidth);
            float ax = Mathf.Abs(x);
            if (ax <= dz) return 0f;
            float t = (ax - dz) / Mathf.Max(0.0001f, halfWidth - dz);
            if (t > 1f) t = 1f;
            float p = power < 1f ? 1f : power;
            float mag = Mathf.Pow(t, p);
            return x < 0f ? -mag : mag;
        }

        public static float SpeedFor(Kind kind, float rate)
        {
            float a = Mathf.Abs(rate);
            if (a <= 0f) return 0f;
            if (a > 1f) a = 1f;
            float lo, hi;
            if (kind == Kind.Rotation) { lo = RotationDegreesPerSecondMin; hi = RotationDegreesPerSecondMax; }
            else if (kind == Kind.Scale) { lo = ScalePerSecondMin; hi = ScalePerSecondMax; }
            else { lo = PositionMetersPerSecondMin; hi = PositionMetersPerSecondMax; }
            float speed = lo + (hi - lo) * a;
            return rate < 0f ? -speed : speed;
        }

        public static float NudgeToStep(float current, float step, float sign)
        {
            if (step <= 1e-8f || sign == 0f) return current;
            double s = step;
            double n = current / s;
            double nextIndex = sign > 0f
                ? System.Math.Floor(n + 1e-6) + 1.0
                : System.Math.Ceiling(n - 1e-6) - 1.0;
            return (float)(nextIndex * s);
        }

        public void Wire(
            RectTransform handleRt,
            RectTransform fillRt,
            Image handleImg,
            Image fillImg,
            float handleW,
            Color handleIdle,
            Color handleHeld)
        {
            _handleRt = handleRt;
            _fillRt = fillRt;
            _handleImg = handleImg;
            _fillImg = fillImg;
            _handleW = handleW;
            _handleIdle = handleIdle;
            _handleHeld = handleHeld;
            _rt = transform as RectTransform;
            try { _canvas = GetComponentInParent<Canvas>(); } catch { _canvas = null; }
            ResetVisual(false);
        }

        void Awake()
        {
            _rt = transform as RectTransform;
        }

        void OnDisable()
        {
            EndPress();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            _pointer = eventData;
            _pressing = true;
            ApplyHeldVisual(true);
            SamplePointer();
            if (_driving) return;
            _driving = true;
            if (OnDriveBegin != null) OnDriveBegin();
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData != null) eventData.useDragThreshold = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData != null) eventData.useDragThreshold = false;
            if (_pressing) _pointer = eventData;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_pressing) return;
            _pointer = eventData;
            SamplePointer();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            EndPress();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            EndPress();
        }

        void EndPress()
        {
            _pointer = null;
            _rate = 0f;
            bool wasDriving = _driving;
            _driving = false;
            _pressing = false;
            ResetVisual(false);
            if (wasDriving && OnDriveEnd != null) OnDriveEnd();
        }

        void Update()
        {
            if (!_pressing) return;
            SamplePointer();
            if (_rate == 0f) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            if (dt > MaxFrameSeconds) dt = MaxFrameSeconds;
            if (OnDriveTick != null) OnDriveTick(_rate, dt);
        }

        void SamplePointer()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null || _pointer == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rt, _pointer.position, ResolveCamera(), out local))
                return;
            float halfW = _rt.rect.width * 0.5f;
            float handleHalf = _handleW * 0.5f;
            _rate = PullToRate(local.x, halfW, handleHalf, ResponsePower);
            float travel = Mathf.Max(1f, halfW - handleHalf);
            SetHandleX(Mathf.Clamp(local.x, -travel, travel));
        }

        void SetHandleX(float x)
        {
            if (Mathf.Abs(x - _handleX) < 0.5f) return;
            _handleX = x;
            if (_handleRt != null)
                _handleRt.anchoredPosition = new Vector2(x, 0f);
            if (_fillRt != null)
            {
                Vector2 size = _fillRt.sizeDelta;
                size.x = x >= 0f ? x : -x;
                _fillRt.pivot = new Vector2(x >= 0f ? 0f : 1f, 0.5f);
                _fillRt.sizeDelta = size;
            }
            bool show = (x < 0f ? -x : x) > 0.5f;
            if (_fillImg != null && show != _fillShown)
            {
                _fillShown = show;
                _fillImg.enabled = show;
            }
        }

        void ResetVisual(bool held)
        {
            _handleX = float.NaN;
            SetHandleX(0f);
            ApplyHeldVisual(held);
            if (_fillImg != null && _fillShown)
            {
                _fillShown = false;
                _fillImg.enabled = false;
            }
        }

        void ApplyHeldVisual(bool held)
        {
            if (_handleImg == null) return;
            _handleImg.color = held ? _handleHeld : _handleIdle;
        }

        Camera ResolveCamera()
        {
            Camera cam = null;
            try { cam = _pointer.pressEventCamera ?? _pointer.enterEventCamera; }
            catch { cam = null; }
            if (cam != null) return cam;
            if (_canvas == null)
            {
                try { _canvas = GetComponentInParent<Canvas>(); } catch { _canvas = null; }
            }
            if (_canvas != null)
            {
                if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
                if (_canvas.worldCamera != null) return _canvas.worldCamera;
            }
            return Camera.main;
        }
    }
}
