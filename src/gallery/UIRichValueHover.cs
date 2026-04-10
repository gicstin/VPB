using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Highlights only the value part (not separators) on pointer hover.
    /// Resets on Set() and OnDisable so recycled list rows never keep a stuck hover color.
    /// </summary>
    public class UIRichValueHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Text target;
        public string prefix = "";
        public string value = "";
        public string separator = "";  // Part that stays uncolored (e.g., "  |  ")
        public Color normalColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        public Color hoverColor = Color.yellow;

        // Conditional coloring: different colors based on value
        public bool useConditionalColoring = false;
        public Color zeroValueColor = Color.green;  // Color when value is 0
        public Color nonZeroValueColor = Color.red; // Color when value > 0

        private bool _hover;

        public void Set(string prefixText, string valueText, string separatorText = "")
        {
            prefix = prefixText ?? "";
            value = valueText ?? "";
            separator = separatorText ?? "";
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
            target.supportRichText = true;

            // Determine the color for the value based on conditional coloring
            Color colorToUse = hoverColor;
            if (useConditionalColoring && !string.IsNullOrEmpty(value))
            {
                // Check if value is "0" (possibly with spaces)
                string trimmedValue = value.Trim();
                colorToUse = (trimmedValue == "0") ? zeroValueColor : nonZeroValueColor;
            }

            // Use rich text to color prefix + value, but not the separator
            if (_hover && (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(value)))
            {
                string colorHex = ColorUtility.ToHtmlStringRGB(colorToUse);
                target.text = $"<color=#{colorHex}>{prefix}{value}</color>" + separator;
                target.color = normalColor;
            }
            else
            {
                target.text = prefix + value + separator;
                target.color = normalColor;
            }
        }
    }
}
