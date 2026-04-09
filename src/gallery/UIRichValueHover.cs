using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Highlights the entire Text (prefix + value) on pointer hover.
    /// Resets on Set() and OnDisable so recycled list rows never keep a stuck hover color.
    /// </summary>
    public class UIRichValueHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Text target;
        public string prefix = "";
        public string value = "";
        public Color normalColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        public Color hoverColor = Color.yellow;

        private bool _hover;

        public void Set(string prefixText, string valueText)
        {
            prefix = prefixText ?? "";
            value = valueText ?? "";
            // Row rebound / recycle: force non-hover so color never sticks from a previous bind.
            _hover = false;
            ApplyVisual();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            ApplyVisual();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            ApplyVisual();
        }

        private void OnDisable()
        {
            _hover = false;
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (target == null) return;
            target.supportRichText = false;
            target.text = prefix + value;
            target.color = _hover ? hoverColor : normalColor;
        }
    }
}
