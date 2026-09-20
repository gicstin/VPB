using UnityEngine;
using UnityEngine.EventSystems;

namespace VPB.Outliner
{
    public sealed class OutlinerRepeatButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public const float FirstDelaySeconds = 0.32f;
        public const float StartIntervalSeconds = 0.11f;
        public const float MinIntervalSeconds = 0.03f;
        public const float RampSeconds = 1.6f;
        public const float MaxFrameSeconds = 0.05f;

        public System.Action OnRepeat;

        bool _down;
        float _held;
        float _next;

        public static float IntervalFor(float held)
        {
            float t = RampSeconds <= 0f ? 1f : Mathf.Clamp01(held / RampSeconds);
            return Mathf.Lerp(StartIntervalSeconds, MinIntervalSeconds, t);
        }

        void OnDisable()
        {
            _down = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            _down = true;
            _held = 0f;
            _next = FirstDelaySeconds;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _down = false;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _down = false;
        }

        void Update()
        {
            if (!_down) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            if (dt > MaxFrameSeconds) dt = MaxFrameSeconds;
            _held += dt;
            _next -= dt;
            if (_next > 0f) return;
            _next = IntervalFor(_held);
            if (OnRepeat != null) OnRepeat();
        }
    }
}
