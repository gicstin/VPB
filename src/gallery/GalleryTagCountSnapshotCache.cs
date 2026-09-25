using System;
using System.Collections.Generic;
using System.Reflection;

namespace VPB
{
    internal class TagFacetCounts
    {
        public int AppearanceSourceCountAll;
        public int AppearanceSourceCountPresets;
        public int AppearanceSourceCountCustom;

        public int ClothingSubfilterCountAll;
        public int ClothingSubfilterCountReal;
        public int ClothingSubfilterCountPresets;
        public int ClothingSubfilterCountCustom;
        public int ClothingSubfilterCountCustomPreset;
        public int ClothingSubfilterCountItems;
        public int ClothingSubfilterCountMale;
        public int ClothingSubfilterCountFemale;
        public int ClothingSubfilterCountDecals;

        public int HairSubfilterCountAll;
        public int HairSubfilterCountPresets;
        public int HairSubfilterCountCustom;
        public int HairSubfilterCountCustomPreset;
        public int HairSubfilterCountItems;
        public int HairSubfilterCountMale;
        public int HairSubfilterCountFemale;

        public int AppearanceSubfilterCountAll;
        public int AppearanceSubfilterCountPresets;
        public int AppearanceSubfilterCountCustom;
        public int AppearanceSubfilterCountMale;
        public int AppearanceSubfilterCountFemale;
        public int AppearanceSubfilterCountFuta;
        public int AppearanceSubfilterCountUnknown;

        public int ClothingSubfilterFacetCountReal;
        public int ClothingSubfilterFacetCountPresets;
        public int ClothingSubfilterFacetCountCustom;
        public int ClothingSubfilterFacetCountCustomPreset;
        public int ClothingSubfilterFacetCountItems;
        public int ClothingSubfilterFacetCountMale;
        public int ClothingSubfilterFacetCountFemale;
        public int ClothingSubfilterFacetCountDecals;

        public int HairSubfilterFacetCountPresets;
        public int HairSubfilterFacetCountCustom;
        public int HairSubfilterFacetCountCustomPreset;
        public int HairSubfilterFacetCountItems;
        public int HairSubfilterFacetCountMale;
        public int HairSubfilterFacetCountFemale;

        public int AppearanceSubfilterFacetCountPresets;
        public int AppearanceSubfilterFacetCountCustom;
        public int AppearanceSubfilterFacetCountMale;
        public int AppearanceSubfilterFacetCountFemale;
        public int AppearanceSubfilterFacetCountFuta;
        public int AppearanceSubfilterFacetCountUnknown;

        public int AppearanceSubfilterCurrentCountAll;
        public int AppearanceSubfilterCurrentCountMale;
        public int AppearanceSubfilterCurrentCountFemale;
        public int AppearanceSubfilterCurrentCountFuta;
        public int AppearanceSubfilterCurrentCountUnknown;

        private static readonly FieldInfo[] s_Fields = typeof(TagFacetCounts).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public void CopyFrom(TagFacetCounts other)
        {
            if (other == null) return;
            foreach (FieldInfo f in s_Fields)
                f.SetValue(this, f.GetValue(other));
        }
    }

    internal sealed class TagCountSnapshot : TagFacetCounts
    {
        public Dictionary<string, int> TagCounts;
    }

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
                if (s_ByKey.Count >= MaxEntries && !s_ByKey.ContainsKey(key))
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
            var clone = new TagCountSnapshot { TagCounts = d };
            clone.CopyFrom(s);
            return clone;
        }
    }
}
