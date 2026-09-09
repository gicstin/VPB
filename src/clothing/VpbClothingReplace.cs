using System;
using System.Collections.Generic;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    internal enum VpbReplaceStrictness
    {
        Strict = 0,
        Balanced = 1,
        Aggressive = 2,
    }

    internal struct VpbReplaceVerdict
    {
        internal bool Remove;
        internal float CoverExisting;
        internal float CoverDropped;
    }

    internal static class VpbClothingReplace
    {
        internal const float CoverageThreshold = 0.5f;

        private const float NoiseFloor = 0.08f;

        internal static bool GeometryEnabled
        {
            get
            {
                var cfg = VPBConfig.Instance;
                return cfg == null || cfg.ClothingReplaceUseGeometry;
            }
        }

        internal static VpbReplaceStrictness Strictness
        {
            get
            {
                var cfg = VPBConfig.Instance;
                if (cfg == null) return VpbReplaceStrictness.Balanced;
                int v = cfg.ClothingReplaceStrictness;
                if (v < 0) v = 0;
                if (v > 2) v = 2;
                return (VpbReplaceStrictness)v;
            }
        }

        internal static VpbReplaceVerdict Decide(VpbClothingFootprint dropped, VpbClothingFootprint existing,
                                                 VpbReplaceStrictness strictness)
        {
            var verdict = new VpbReplaceVerdict();
            if (dropped == null || existing == null || !dropped.IsUsable || !existing.IsUsable) return verdict;
            if (!dropped.ComparableWith(existing)) return verdict;

            if (dropped.RegionMask != 0u && existing.RegionMask != 0u &&
                (dropped.RegionMask & existing.RegionMask) == 0u)
                return verdict;

            bool byArea;
            float coverDropped, coverExisting;
            if (!dropped.TryGetMutualCoverage(existing, out coverDropped, out coverExisting, out byArea))
                return verdict;

            verdict.CoverExisting = coverExisting;
            verdict.CoverDropped = coverDropped;

            if (coverExisting < NoiseFloor && coverDropped < NoiseFloor) return verdict;

            switch (strictness)
            {
                case VpbReplaceStrictness.Strict:
                    verdict.Remove = coverExisting >= CoverageThreshold && coverDropped >= CoverageThreshold;
                    break;

                case VpbReplaceStrictness.Aggressive:
                    verdict.Remove = coverExisting >= CoverageThreshold || coverDropped >= CoverageThreshold;
                    break;

                default:
                    verdict.Remove = coverExisting >= CoverageThreshold;
                    break;
            }

            return verdict;
        }

        internal static List<string> CollectDisplacedParams(Atom atom, string droppedUid, FileEntry droppedEntry)
        {
            if (atom == null || string.IsNullOrEmpty(droppedUid)) return null;
            if (!GeometryEnabled) return null;

            if (!VpbSkinRegionMap.EnsureHarvested(atom))
            {
                return null;
            }
            VpbSkinAreaMap.EnsureHarvested(atom);

            if (!TargetGenderAcceptsPath(atom, droppedUid))
            {
                return new List<string>();
            }

            VpbClothingFootprint dropped = VpbClothingFootprintCache.Get(droppedUid, droppedEntry, atom);
            if (dropped == null || !dropped.IsUsable)
            {
                return null;
            }

            return CollectDisplacedParams(atom, dropped, droppedUid, droppedEntry, null);
        }

        private static List<string> CollectDisplacedParams(Atom atom, VpbClothingFootprint dropped,
                                                           string droppedUid, FileEntry droppedEntry,
                                                           List<string> incomingUids)
        {
            JSONStorable geometry = null;
            try { geometry = atom.GetStorableByID("geometry"); }
            catch { geometry = null; }
            if (geometry == null) return null;

            List<string> names = null;
            try { names = geometry.GetBoolParamNames(); }
            catch { names = null; }
            if (names == null) return null;

            VpbReplaceStrictness strictness = Strictness;
            var result = new List<string>(8);

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                if (!n.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;

                string wornUid = n.Substring("clothing:".Length);

                JSONStorableBool active = null;
                try { active = geometry.GetBoolJSONParam(n); }
                catch { active = null; }
                if (active == null || !active.val) continue;

                if (string.Equals(wornUid, droppedUid, StringComparison.OrdinalIgnoreCase)
                    || (incomingUids != null && ContainsUid(incomingUids, wornUid)))
                    continue;

                if (!SameWearLayer(droppedUid, wornUid, droppedEntry, atom)) continue;

                VpbClothingFootprint existing = VpbClothingFootprintCache.Get(wornUid, null, atom);
                if (existing == null || !existing.IsUsable) continue;

                if (existing.VertCount == dropped.VertCount
                    && existing.ComparableWith(dropped)
                    && existing.IntersectionCount(dropped) == dropped.VertCount)
                    continue;

                if (Decide(dropped, existing, strictness).Remove) result.Add(n);
            }

            return result;
        }

        internal static bool TryApplyOutfitPreset(Atom atom, JSONClass presetJson, string presetUid)
        {
            if (atom == null || presetJson == null) return false;
            if (!GeometryEnabled) return false;
            if (!VpbSkinRegionMap.EnsureHarvested(atom)) return false;
            VpbSkinAreaMap.EnsureHarvested(atom);

            List<string> itemUids = CollectPresetClothingUids(presetJson, presetUid);
            if (itemUids == null || itemUids.Count == 0) return false;

            var parts = new List<VpbClothingFootprint>(itemUids.Count);
            for (int i = 0; i < itemUids.Count; i++)
            {
                VpbClothingFootprint fp = VpbClothingFootprintCache.Get(itemUids[i], null, atom);
                if (fp != null && fp.IsUsable && fp.IsBodySpace) parts.Add(fp);
            }
            if (parts.Count == 0) return false;

            if (parts.Count * 2 < itemUids.Count)
            {
                return false;
            }

            VpbClothingFootprint outfit = VpbClothingFootprint.Union(parts);
            if (outfit == null || !outfit.IsUsable) return false;


            return ApplyFootprint(atom, outfit, presetUid, null, itemUids);
        }

        private static List<string> CollectPresetClothingUids(JSONClass presetJson, string presetUid)
        {
            string selfPkg = null;
            if (!string.IsNullOrEmpty(presetUid))
            {
                int colon = presetUid.IndexOf(":/", StringComparison.Ordinal);
                if (colon > 0) selfPkg = presetUid.Substring(0, colon);
            }

            var uids = new List<string>(8);
            try
            {
                JSONArray storables = presetJson["storables"] as JSONArray;
                if (storables == null)
                {
                    AppendClothingUids(presetJson, uids, selfPkg);
                    return uids;
                }

                for (int i = 0; i < storables.Count; i++)
                    AppendClothingUids(storables[i] as JSONClass, uids, selfPkg);
            }
            catch { }
            return uids;
        }

        private static void AppendClothingUids(JSONClass storable, List<string> into, string selfPkg)
        {
            if (storable == null) return;
            JSONArray clothing = storable["clothing"] as JSONArray;
            if (clothing == null) return;

            for (int i = 0; i < clothing.Count; i++)
            {
                JSONClass item = clothing[i] as JSONClass;
                if (item == null) continue;

                if (item["enabled"] != null && !item["enabled"].AsBool) continue;

                string id = item["id"] != null ? item["id"].Value : null;
                if (string.IsNullOrEmpty(id)) continue;

                if (id.StartsWith("SELF:", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(selfPkg)) continue;
                    id = selfPkg + ":" + id.Substring("SELF:".Length);
                }

                if (!ContainsUid(into, id)) into.Add(id);
            }
        }

        internal static bool HairMayDisplace(Atom atom, string droppedUid, FileEntry droppedEntry, string wornUid)
        {
            if (!GeometryEnabled) return true;
            if (atom == null || string.IsNullOrEmpty(droppedUid) || string.IsNullOrEmpty(wornUid)) return true;
            if (string.Equals(droppedUid, wornUid, StringComparison.OrdinalIgnoreCase)) return true;

            if (!TargetGenderAcceptsPath(atom, droppedUid))
            {
                return false;
            }

            VpbHairFootprint.EnsureScalpsLearned(atom);

            VpbClothingFootprint dropped = VpbClothingFootprintCache.Get(droppedUid, droppedEntry, atom, isHair: true);
            if (dropped == null || !dropped.IsUsable)
            {
                return true;
            }

            VpbClothingFootprint worn = VpbClothingFootprintCache.Get(wornUid, null, atom, isHair: true);
            if (worn == null || !worn.IsUsable)
            {
                return true;
            }

            if (!dropped.IsBodySpace || !worn.IsBodySpace || !dropped.ComparableWith(worn)) return true;

            int shared = dropped.IntersectionCount(worn);
            if (shared > 0) return true;

            return false;
        }

        private static bool ContainsUid(List<string> uids, string uid)
        {
            for (int i = 0; i < uids.Count; i++)
                if (string.Equals(uids[i], uid, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool ApplyFootprint(Atom atom, VpbClothingFootprint footprint, string label,
                                           FileEntry entry, List<string> incomingUids)
        {
            List<string> displaced = CollectDisplacedParams(atom, footprint, label, entry, incomingUids);
            if (displaced == null) return false;

            JSONStorable geometry = null;
            try { geometry = atom.GetStorableByID("geometry"); }
            catch { geometry = null; }
            if (geometry == null) return false;

            for (int i = 0; i < displaced.Count; i++)
            {
                try
                {
                    JSONStorableBool p = geometry.GetBoolJSONParam(displaced[i]);
                    if (p == null || !p.val) continue;
                    p.val = false;
                }
                catch { }
            }

            return true;
        }

        internal static bool TargetGenderAccepts(Atom atom, int itemGender)
        {
            if (atom == null) return true;
            try
            {
                DAZCharacterSelector selector = VpbSkinRegionMap.FindSelector(atom);
                if (selector == null) return true;
                int atomGender = selector.gender == DAZCharacterSelector.Gender.Male
                    ? VpbSkinRegionMap.GenderMale
                    : VpbSkinRegionMap.GenderFemale;
                return atomGender == itemGender;
            }
            catch { return true; }
        }

        internal static bool TargetGenderAcceptsPath(Atom atom, string uid)
        {
            if (string.IsNullOrEmpty(uid)) return true;
            bool male = uid.IndexOf("/Male/", StringComparison.OrdinalIgnoreCase) >= 0
                        || uid.IndexOf("\\Male\\", StringComparison.OrdinalIgnoreCase) >= 0;
            bool female = uid.IndexOf("/Female/", StringComparison.OrdinalIgnoreCase) >= 0
                          || uid.IndexOf("\\Female\\", StringComparison.OrdinalIgnoreCase) >= 0;
            if (male == female) return true;

            return TargetGenderAccepts(atom, male ? VpbSkinRegionMap.GenderMale : VpbSkinRegionMap.GenderFemale);
        }

        private static bool SameWearLayer(string droppedUid, string wornUid, FileEntry droppedEntry, Atom atom)
        {
            ClothingLoadingUtils.ClothingWearClass a =
                ClothingLoadingUtils.ClassifyClothingWearClass(droppedUid, droppedEntry, atom);
            ClothingLoadingUtils.ClothingWearClass b =
                ClothingLoadingUtils.ClassifyClothingWearClass(wornUid, null, atom);

            if (a == ClothingLoadingUtils.ClothingWearClass.Unknown ||
                b == ClothingLoadingUtils.ClothingWearClass.Unknown)
                return true;
            return a == b;
        }

        internal static bool TryApply(Atom atom, string droppedUid, FileEntry droppedEntry)
        {
            List<string> displaced = CollectDisplacedParams(atom, droppedUid, droppedEntry);
            if (displaced == null) return false;

            JSONStorable geometry = null;
            try { geometry = atom.GetStorableByID("geometry"); }
            catch { geometry = null; }
            if (geometry == null) return false;

            for (int i = 0; i < displaced.Count; i++)
            {
                try
                {
                    JSONStorableBool p = geometry.GetBoolJSONParam(displaced[i]);
                    if (p == null || !p.val) continue;
                    p.val = false;
                }
                catch { }
            }

            return displaced.Count > 0;
        }

    }
}
