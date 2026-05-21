using UnityEngine;
using UnityEngine.Events;

namespace VPB
{
    /// <summary>Entry points for shared modal pickers (color, etc.). Host panel supplies gallery canvas / overlay integration.</summary>
    public static class VPBUiPickers
    {
        /// <summary>Opens <see cref="UIColorPicker"/> via <see cref="GalleryPanel.DisplayColorPicker"/>. Confirmed color keeps <paramref name="initial"/>.a (picker is RGB-only).</summary>
        public static void PickColorRgb(GalleryPanel hostPanel, string title, Color initial, UnityAction<Color> onConfirmed)
        {
            if (onConfirmed == null) return;
            float a = initial.a;
            if (hostPanel == null)
            {
                onConfirmed.Invoke(initial);
                return;
            }
            string t = string.IsNullOrEmpty(title) ? "Color" : title;
            hostPanel.DisplayColorPicker(t, initial, c =>
            {
                c.a = a;
                onConfirmed.Invoke(c);
            });
        }
    }
}
