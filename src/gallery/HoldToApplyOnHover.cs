using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    /// <summary>
    /// Hold-to-apply timer that triggers the panel's apply/launch logic after a delay.
    /// Requires the user to actively hold the pointer button/trigger down (similar to starting a drag).
    /// </summary>
    public class HoldToApplyOnHover : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public GalleryPanel panel;
        public FileEntry file;

        public float holdSeconds = 2.0f;

        private Coroutine _co;
        private GameObject _overlay;
        private Image _fill;
        private Text _text;

        private static bool IsVR()
        {
            try { return UnityEngine.XR.XRSettings.enabled; }
            catch { return false; }
        }

        private void TryStartHold()
        {
            if (panel == null || file == null) return;
            if (!panel.HoldToLaunchEnabled) return;
            if (_co != null) return;
            _co = StartCoroutine(HoldRoutine());
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Hold-to-launch is described as "hover-hold" in VR; starting on hover makes it work
            // with VR laser pointers that don't issue pointer-down events for hover.
            if (!IsVR()) return;
            TryStartHold();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Desktop: require active press/hold (prevents accidental trigger while scrolling).
            if (IsVR() || eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            TryStartHold();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Cancel();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Cancel();
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void Cancel()
        {
            if (_co != null)
            {
                StopCoroutine(_co);
                _co = null;
            }
            if (_overlay != null)
            {
                Destroy(_overlay);
                _overlay = null;
                _fill = null;
                _text = null;
            }
        }

        private IEnumerator HoldRoutine()
        {
            EnsureOverlay();

            float start = Time.unscaledTime;
            float end = start + Mathf.Max(0.05f, holdSeconds);
            while (Time.unscaledTime < end)
            {
                float t = Mathf.InverseLerp(start, end, Time.unscaledTime);
                if (_fill != null) _fill.fillAmount = t;
                if (_text != null)
                {
                    float left = Mathf.Max(0f, end - Time.unscaledTime);
                    _text.text = left.ToString("0.0");
                }
                yield return null;
            }

            if (_fill != null) _fill.fillAmount = 1f;
            if (_text != null) _text.text = "0.0";

            try
            {
                panel.ApplyFileFromHold(file);
            }
            catch { }

            Cancel();
        }

        private void EnsureOverlay()
        {
            if (_overlay != null) return;

            _overlay = new GameObject("HoldToApplyOverlay");
            _overlay.transform.SetParent(transform, false);

            var rt = _overlay.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(78f, 78f);
            rt.anchoredPosition = Vector2.zero;

            var bg = _overlay.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(_overlay.transform, false);
            var frt = fillGO.AddComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.sizeDelta = new Vector2(-10f, -10f);
            frt.anchoredPosition = Vector2.zero;

            _fill = fillGO.AddComponent<Image>();
            _fill.color = new Color(0.15f, 0.75f, 0.2f, 0.85f);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Radial360;
            _fill.fillOrigin = 2; // Top
            _fill.fillClockwise = true;
            _fill.fillAmount = 0f;
            _fill.raycastTarget = false;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(_overlay.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            trt.anchoredPosition = Vector2.zero;

            _text = textGO.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.fontSize = 22;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.color = Color.white;
            _text.raycastTarget = false;
            _text.text = holdSeconds.ToString("0.0");
        }
    }
}

