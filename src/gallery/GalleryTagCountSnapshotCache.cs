using System;
using System.Collections.Generic;

namespace VPB
{
    /// <summary>Holds all side-tab / tag facet counters produced by <c>CacheTagCounts</c> / <c>CoCacheTagCountsInternal</c>.</summary>
    internal sealed class TagCountSnapshot
    {
        public Dictionary<string, int> TagCounts;

        public int AppearanceSourceCountAll;
        public int AppearanceSourceCountPresets;
        public int AppearanceSourceCountCustom;

        public int ClothingSubfilterCountAll;
        public int ClothingSubfilterCountReal;
        public int ClothingSubfilterCountPresets;
        public int ClothingSubfilterCountCustom;
        public int ClothingSubfilterCountItems;
        public int ClothingSubfilterCountMale;
        public int ClothingSubfilterCountFemale;
        public int ClothingSubfilterCountDecals;

        public int HairSubfilterCountAll;
        public int HairSubfilterCountPresets;
        public int HairSubfilterCountCustom;
        public int HairSubfilterCountItems;
        public int HairSubfilterCountMale;
        public int HairSubfilterCountFemale;

        public int AppearanceSubfilterCountAll;
        public int AppearanceSubfilterCountPresets;
        public int AppearanceSubfilterCountCustom;
        public int AppearanceSubfilterCountMale;
        public int AppearanceSubfilterCountFemale;
        public int AppearanceSubfilterCountFuta;

        public int ClothingSubfilterFacetCountReal;
        public int ClothingSubfilterFacetCountPresets;
        public int ClothingSubfilterFacetCountCustom;
        public int ClothingSubfilterFacetCountItems;
        public int ClothingSubfilterFacetCountMale;
        public int ClothingSubfilterFacetCountFemale;
        public int ClothingSubfilterFacetCountDecals;

        public int HairSubfilterFacetCountPresets;
        public int HairSubfilterFacetCountCustom;
        public int HairSubfilterFacetCountItems;
        public int HairSubfilterFacetCountMale;
        public int HairSubfilterFacetCountFemale;

        public int AppearanceSubfilterFacetCountPresets;
        public int AppearanceSubfilterFacetCountCustom;
        public int AppearanceSubfilterFacetCountMale;
        public int AppearanceSubfilterFacetCountFemale;
        public int AppearanceSubfilterFacetCountFuta;

        public int AppearanceSubfilterCurrentCountAll;
        public int AppearanceSubfilterCurrentCountMale;
        public int AppearanceSubfilterCurrentCountFemale;
        public int AppearanceSubfilterCurrentCountFuta;
    }

    /// <summary>
    /// Reuses tag/facet counts for a gallery category view when path/extension/creator/subfilters and package scan match,
    /// avoiding a second full VAR scan after the file grid refresh (often several seconds).
    /// </summary>
    internal static class GalleryTagCountSnapshotCache
    {
        private static readonly object s_Lock = new object();
        private static readonly Dictionary<string, TagCountSnapshot> s_ByKey = new Dictionary<string, TagCountSnapshot>(StringComparer.Ordinal);

        private const int MaxEntries = 32;

        public static void Clear()
        {
            lock (s_Lock) { s_ByKey.Clear(); }
        }

        public static bool TryGet(string key, out TagCountSnapshot snap)
        {
            snap = null;
            if (string.IsNullOrEmpty(key)) return false;
            lock (s_Lock)
            {
                TagCountSnapshot src;
                if (!s_ByKey.TryGetValue(key, out src) || src == null) return false;
                snap = Clone(src);
                return true;
            }
        }

        /// <summary>True if <see cref="TryGet"/> would succeed, without cloning the snapshot (cheaper than <see cref="TryGet"/> for probing).</summary>
        public static bool HasSnapshot(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (s_Lock)
            {
                TagCountSnapshot src;
                return s_ByKey.TryGetValue(key, out src) && src != null;
            }
        }

        public static void Put(string key, TagCountSnapshot snap)
        {
            if (string.IsNullOrEmpty(key) || snap == null) return;
            lock (s_Lock)
            {
                if (s_ByKey.Count >= MaxEntries)
                    s_ByKey.Clear();
                s_ByKey[key] = Clone(snap);
            }
        }

        private static TagCountSnapshot Clone(TagCountSnapshot s)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (s.TagCounts != null)
            {
                foreach (var kv in s.TagCounts)
                    d[kv.Key] = kv.Value;
            }
            return new TagCountSnapshot
            {
                TagCounts = d,
                AppearanceSourceCountAll = s.AppearanceSourceCountAll,
                AppearanceSourceCountPresets = s.AppearanceSourceCountPresets,
                AppearanceSourceCountCustom = s.AppearanceSourceCountCustom,
                ClothingSubfilterCountAll = s.ClothingSubfilterCountAll,
                ClothingSubfilterCountReal = s.ClothingSubfilterCountReal,
                ClothingSubfilterCountPresets = s.ClothingSubfilterCountPresets,
                ClothingSubfilterCountCustom = s.ClothingSubfilterCountCustom,
                ClothingSubfilterCountItems = s.ClothingSubfilterCountItems,
                ClothingSubfilterCountMale = s.ClothingSubfilterCountMale,
                ClothingSubfilterCountFemale = s.ClothingSubfilterCountFemale,
                ClothingSubfilterCountDecals = s.ClothingSubfilterCountDecals,
                HairSubfilterCountAll = s.HairSubfilterCountAll,
                HairSubfilterCountPresets = s.HairSubfilterCountPresets,
                HairSubfilterCountCustom = s.HairSubfilterCountCustom,
                HairSubfilterCountItems = s.HairSubfilterCountItems,
                HairSubfilterCountMale = s.HairSubfilterCountMale,
                HairSubfilterCountFemale = s.HairSubfilterCountFemale,
                AppearanceSubfilterCountAll = s.AppearanceSubfilterCountAll,
                AppearanceSubfilterCountPresets = s.AppearanceSubfilterCountPresets,
                AppearanceSubfilterCountCustom = s.AppearanceSubfilterCountCustom,
                AppearanceSubfilterCountMale = s.AppearanceSubfilterCountMale,
                AppearanceSubfilterCountFemale = s.AppearanceSubfilterCountFemale,
                AppearanceSubfilterCountFuta = s.AppearanceSubfilterCountFuta,
                ClothingSubfilterFacetCountReal = s.ClothingSubfilterFacetCountReal,
                ClothingSubfilterFacetCountPresets = s.ClothingSubfilterFacetCountPresets,
                ClothingSubfilterFacetCountCustom = s.ClothingSubfilterFacetCountCustom,
                ClothingSubfilterFacetCountItems = s.ClothingSubfilterFacetCountItems,
                ClothingSubfilterFacetCountMale = s.ClothingSubfilterFacetCountMale,
                ClothingSubfilterFacetCountFemale = s.ClothingSubfilterFacetCountFemale,
                ClothingSubfilterFacetCountDecals = s.ClothingSubfilterFacetCountDecals,
                HairSubfilterFacetCountPresets = s.HairSubfilterFacetCountPresets,
                HairSubfilterFacetCountCustom = s.HairSubfilterFacetCountCustom,
                HairSubfilterFacetCountItems = s.HairSubfilterFacetCountItems,
                HairSubfilterFacetCountMale = s.HairSubfilterFacetCountMale,
                HairSubfilterFacetCountFemale = s.HairSubfilterFacetCountFemale,
                AppearanceSubfilterFacetCountPresets = s.AppearanceSubfilterFacetCountPresets,
                AppearanceSubfilterFacetCountCustom = s.AppearanceSubfilterFacetCountCustom,
                AppearanceSubfilterFacetCountMale = s.AppearanceSubfilterFacetCountMale,
                AppearanceSubfilterFacetCountFemale = s.AppearanceSubfilterFacetCountFemale,
                AppearanceSubfilterFacetCountFuta = s.AppearanceSubfilterFacetCountFuta,
                AppearanceSubfilterCurrentCountAll = s.AppearanceSubfilterCurrentCountAll,
                AppearanceSubfilterCurrentCountMale = s.AppearanceSubfilterCurrentCountMale,
                AppearanceSubfilterCurrentCountFemale = s.AppearanceSubfilterCurrentCountFemale,
                AppearanceSubfilterCurrentCountFuta = s.AppearanceSubfilterCurrentCountFuta,
            };
        }
    }
}
