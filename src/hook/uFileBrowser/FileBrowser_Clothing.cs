using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class FileBrowser : MonoBehaviour
    {
        List<JSONStorableBool> ClothingTypeTagsJsonStorable = new List<JSONStorableBool>();
        List<JSONStorableBool> ClothingRegionTagsJsonStorable = new List<JSONStorableBool>();
        List<JSONStorableBool> ClothingOtherTagsJsonStorable = new List<JSONStorableBool>();
        List<RectTransform> ClothingTagsUIList = new List<RectTransform>();

        List<JSONStorableBool> HairRegionTagsJsonStorable = new List<JSONStorableBool>();
        List<JSONStorableBool> HairTypeTagsJsonStorable = new List<JSONStorableBool>();
        List<JSONStorableBool> HairOtherTagsJsonStorable = new List<JSONStorableBool>();
        List<RectTransform> HairTagsUIList = new List<RectTransform>();

        bool needFilterClothing = false;
        void SetClothTagsActive(bool active)
        {
            needFilterClothing = active;
            foreach (var item in ClothingTagsUIList)
            {
                item.gameObject.SetActive(active);
            }
        }
        bool needFilterHair = false;
        void SetHairTagsActive(bool active)
        {
            needFilterHair = active;
            foreach (var item in HairTagsUIList)
            {
                item.gameObject.SetActive(active);
            }
        }
        static void InitTagStorables(IList<string> tagNames, List<JSONStorableBool> target)
        {
            for (int i = 0; i < tagNames.Count; i++)
                target.Add(new JSONStorableBool(tagNames[i], false));
        }

        void InitTags()
        {
            InitTagStorables(TagFilter.ClothingRegionTags, ClothingRegionTagsJsonStorable);
            InitTagStorables(TagFilter.ClothingTypeTags, ClothingTypeTagsJsonStorable);
            InitTagStorables(TagFilter.ClothingOtherTags, ClothingOtherTagsJsonStorable);
            InitTagStorables(TagFilter.HairRegionTags, HairRegionTagsJsonStorable);
            InitTagStorables(TagFilter.HairTypeTags, HairTypeTagsJsonStorable);
            InitTagStorables(TagFilter.HairOtherTags, HairOtherTagsJsonStorable);
        }

        static HashSet<string> CollectActiveTagFilter(params List<JSONStorableBool>[] groups)
        {
            HashSet<string> ret = new HashSet<string>();
            for (int g = 0; g < groups.Length; g++)
            {
                List<JSONStorableBool> group = groups[g];
                if (group == null) continue;
                for (int i = 0; i < group.Count; i++)
                {
                    JSONStorableBool item = group[i];
                    if (item != null && item.val)
                        ret.Add(item.name);
                }
            }
            return ret;
        }

        HashSet<string> GetHairFilter()
        {
            return CollectActiveTagFilter(HairRegionTagsJsonStorable, HairTypeTagsJsonStorable, HairOtherTagsJsonStorable);
        }

        // If nothing is selected, it means we need to filter clothing
        HashSet<string> GetClothingFilter()
        {
            return CollectActiveTagFilter(ClothingRegionTagsJsonStorable, ClothingTypeTagsJsonStorable, ClothingOtherTagsJsonStorable);
        }

    }
}
