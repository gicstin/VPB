using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public class UIRichValueHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Text target;
        public string prefix = "";
        public string value = "";
        public Color normalValueColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        public Color hoverValueColor = Color.yellow;

        private bool _hover;

        public void Set(string prefixText, string valueText)
        {
            prefix = prefixText ?? "";
            value = valueText ?? "";
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            Apply();
        }

        private void Apply()
        {
            if (target == null) return;
            target.supportRichText = true;
            string hex = ColorUtility.ToHtmlStringRGB(_hover ? hoverValueColor : normalValueColor);
            target.text = prefix + "<color=#" + hex + ">" + value + "</color>";
        }
    }
}

