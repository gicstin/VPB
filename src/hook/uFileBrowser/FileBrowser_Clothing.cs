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
        void InitTags()
        {
            for (int i = 0; i < TagFilter.ClothingRegionTags.Count; i++)
            {
                ClothingRegionTagsJsonStorable.Add(new JSONStorableBool(TagFilter.ClothingRegionTags[i], false));
            }
            for (int i = 0; i < TagFilter.ClothingTypeTags.Count; i++)
            {
                ClothingTypeTagsJsonStorable.Add(new JSONStorableBool(TagFilter.ClothingTypeTags[i], false));
            }
            for (int i = 0; i < TagFilter.ClothingOtherTags.Count; i++)
            {
                ClothingOtherTagsJsonStorable.Add(new JSONStorableBool(TagFilter.ClothingOtherTags[i], false));
            }

            for (int i = 0; i < TagFilter.HairRegionTags.Count; i++)
            {
                HairRegionTagsJsonStorable.Add(new JSONStorableBool(TagFilter.HairRegionTags[i], false));
            }
            for (int i = 0; i < TagFilter.HairTypeTags.Count; i++)
            {
                HairTypeTagsJsonStorable.Add(new JSONStorableBool(TagFilter.HairTypeTags[i], false));
            }
            for (int i = 0; i < TagFilter.HairOtherTags.Count; i++)
            {
                HairOtherTagsJsonStorable.Add(new JSONStorableBool(TagFilter.HairOtherTags[i], false));
            }
        }
        HashSet<string> GetHairFilter()
        {
            HashSet<string> ret = new HashSet<string>();
            foreach (var item in HairRegionTagsJsonStorable)
            {
                if (item.val)
                    ret.Add(item.name);
            }
            foreach (var item in HairTypeTagsJsonStorable)
            {
                if (item.val)
                    ret.Add(item.name);
            }
            foreach(var item in HairOtherTagsJsonStorable)
            {
                if (item.val)
                    ret.Add(item.name);
            }
            return ret;
        }
        // If nothing is selected, it means we need to filter clothing
        HashSet<string> GetClothingFilter()
        {
            HashSet<string> ret = new HashSet<string>();
            foreach (var item in ClothingRegionTagsJsonStorable)
            {
                if (item.val)
                    ret.Add(item.name);
            }
            foreach (var item in ClothingTypeTagsJsonStorable)
            {
                if (item.val)
                    ret.Add(item.name);
            }
            foreach (var item in ClothingOtherTagsJsonStorable)
            {
                if (item.val)
                    ret.Add(item.name);
            }
            return ret;
        }

    }
}
