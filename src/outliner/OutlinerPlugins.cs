using System;

namespace VPB.Outliner
{
    internal static class OutlinerPlugins
    {
        internal const string ManagerStorableId = "PluginManager";

        internal static bool IsManagerStorable(string storableId)
        {
            return string.Equals(storableId, ManagerStorableId, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsCreatePluginAction(string storableId, string paramId)
        {
            if (!IsManagerStorable(storableId) || string.IsNullOrEmpty(paramId)) return false;
            if (string.Equals(paramId, "CreatePlugin", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(paramId, "AddPlugin", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string DropHover(string itemName, string atomUid)
        {
            string who = atomUid ?? "";
            string what = string.IsNullOrEmpty(itemName) ? "item" : itemName;
            if (string.IsNullOrEmpty(who))
                return "Select an atom in Scene Overview, then drop.";
            return "Add " + what + " to " + who;
        }

        internal static float PopupWidthRefForCharCount(int charCount)
        {
            if (charCount < 0) charCount = 0;
            float charW = GalleryUiDesignTokens.FontBodyRef * 0.55f;
            float pad = GalleryUiDesignTokens.PopupMenuPaddingRef * 2f
                + GalleryUiDesignTokens.PopupMenuRowTextPadXRef * 2f;
            float w = charCount * charW + pad;
            float min = GalleryUiDesignTokens.OverflowMenuPanelWidthRef;
            float max = GalleryUiDesignTokens.OverflowMenuPanelWidthRef
                + GalleryUiDesignTokens.SectionGapRef * 5f;
            if (w < min) return min;
            if (w > max) return max;
            return w;
        }
    }
}
