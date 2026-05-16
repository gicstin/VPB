using SimpleJSON;
using UnityEngine;

namespace VPB.src.util
{
    /// <summary>
    /// JSON-patch helper for Appearance preset imports.
    /// Rewrites the preset's rescaleObject.scale to the target atom's current scale,
    /// so when the preset loads, scale "loads" to the existing value and visually nothing changes.
    /// </summary>
    internal static class AppearancePresetSuppress
    {
        /// <summary>
        /// Mutates presetJson in place. No-op if storables array or rescaleObject storable absent on either side.
        /// Safe to call regardless of category; callers should still gate on the SuppressAppearanceScaleChange flag.
        /// Returns true iff a rescaleObject was found in both the preset AND the target atom and the scale was rewritten.
        /// </summary>
        public static bool PatchScaleToTargetCurrent(JSONClass presetJson, Atom targetAtom)
        {
            if (presetJson == null || targetAtom == null) return false;
            var storables = presetJson["storables"] as JSONArray;
            if (storables == null) return false;

            var rescaleStorable = targetAtom.GetStorableByID("rescaleObject");
            if (rescaleStorable == null) return false;
            var scaleParam = rescaleStorable.GetFloatJSONParam("scale");
            if (scaleParam == null) return false;

            foreach (JSONNode n in storables)
            {
                var jc = n as JSONClass;
                if (jc != null && jc["id"] != null && jc["id"].Value == "rescaleObject")
                {
                    jc["scale"].AsFloat = scaleParam.val;
                    return true;
                }
            }
            return false;
        }
    }
}
