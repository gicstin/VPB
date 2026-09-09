using System;
using System.Collections.Generic;
using MVR.FileManagement;
using UnityEngine;

namespace VPB
{
    internal static class VpbClothingFootprintCache
    {
        private static readonly Dictionary<string, VpbClothingFootprint> s_mem =
            new Dictionary<string, VpbClothingFootprint>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, int> s_misses =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private const int MaxFootprintMisses = 3;

        private const int MaxMemEntries = 512;

        internal static void ResetSessionState()
        {
            s_mem.Clear();
            s_misses.Clear();
        }

        internal static VpbClothingFootprint Get(string itemUid, FileEntry entry = null, Atom atom = null,
                                                 bool isHair = false)
        {
            if (string.IsNullOrEmpty(itemUid)) return null;

            string key = (isHair ? "h|" : "c|") + itemUid;

            VpbClothingFootprint fp;
            if (s_mem.TryGetValue(key, out fp)) return fp;

            int misses;
            if (s_misses.TryGetValue(key, out misses) && misses >= MaxFootprintMisses) return null;

            fp = VpbLocalDatabase.TryLoadClothingFootprint(key);
            if (fp != null)
            {
                if (fp.RegionMask == 0u && fp.IsBodySpace)
                {
                    uint mask = VpbClothingFootprint.ComputeRegionMask(fp);
                    if (mask != 0u)
                    {
                        fp.RegionMask = mask;
                        VpbLocalDatabase.TrySaveClothingFootprint(key, fp);
                    }
                }
                if (!fp.IsBodySpace && atom != null)
                {
                    VpbClothingFootprint projected = TryFromDisk(itemUid, entry, isHair, atom);
                    if (projected != null && projected.IsBodySpace)
                    {
                        Store(key, projected, persist: true);
                        return projected;
                    }
                }
                Store(key, fp, persist: false);
                return fp;
            }

            fp = TryFromDisk(itemUid, entry, isHair, atom);
            if (fp != null)
            {
                Store(key, fp, persist: true);
                return fp;
            }

            fp = TryFromLive(itemUid, atom, isHair);
            if (fp != null)
            {
                Store(key, fp, persist: false);
                return fp;
            }

            s_misses[key] = misses + 1;
            return null;
        }

        private static void Store(string key, VpbClothingFootprint fp, bool persist)
        {
            if (s_mem.Count >= MaxMemEntries) TrimOldest();
            s_mem[key] = fp;
            s_misses.Remove(key);
            if (persist) VpbLocalDatabase.TrySaveClothingFootprint(key, fp);
        }

        private static void TrimOldest()
        {
            int drop = MaxMemEntries / 4;
            if (drop < 1) drop = 1;

            var doomed = new List<string>(drop);
            foreach (var kv in s_mem)
            {
                doomed.Add(kv.Key);
                if (doomed.Count >= drop) break;
            }
            for (int i = 0; i < doomed.Count; i++) s_mem.Remove(doomed[i]);
        }

        private static VpbClothingFootprint TryFromLive(string itemUid, Atom atom, bool isHair)
        {
            if (atom == null) return null;
            if (isHair) return TryFromLiveHair(itemUid, atom);
            try
            {
                DAZCharacterSelector selector = VpbSkinRegionMap.FindSelector(atom);
                if (selector == null) return null;

                DAZClothingItem item = selector.GetClothingItem(itemUid);
                if (item == null) return null;
                if (!item.active) return null;

                VpbClothingFootprint fp = VpbClothingFootprint.FromLiveItem(item);
                if (fp != null) return fp;

                string loadPath = null;
                try { loadPath = item.dynamicRuntimeLoadPath; }
                catch { loadPath = null; }
                if (string.IsNullOrEmpty(loadPath)) return null;

                byte[] vab = VpbClothingFootprint.TryReadVabBytes(loadPath, null);
                if (vab == null) return null;

                int gender = VpbSkinRegionMap.GenderFemale;
                try { if (item.gender == DAZDynamicItem.Gender.Male) gender = VpbSkinRegionMap.GenderMale; }
                catch { }
                return VpbClothingFootprint.FromVabBytes(vab, gender);
            }
            catch { return null; }
        }

        private static VpbClothingFootprint TryFromLiveHair(string itemUid, Atom atom)
        {
            try
            {
                DAZCharacterSelector selector = VpbSkinRegionMap.FindSelector(atom);
                if (selector == null) return null;

                DAZHairGroup[] items = selector.hairItems;
                if (items == null) return null;

                for (int i = 0; i < items.Length; i++)
                {
                    DAZHairGroup h = items[i];
                    if (h == null) continue;

                    string uid = null;
                    try { uid = h.uid; } catch { uid = null; }
                    if (!string.Equals(uid, itemUid, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!h.active) return null;

                    VpbClothingFootprint fp = VpbHairFootprint.FromLiveItem(h);
                    if (fp != null) return fp;

                    string loadPath = null;
                    try { loadPath = h.dynamicRuntimeLoadPath; }
                    catch { loadPath = null; }
                    if (string.IsNullOrEmpty(loadPath)) return null;

                    byte[] vab = VpbClothingFootprint.TryReadVabBytes(loadPath, null);
                    if (vab == null) return null;

                    int gender = VpbSkinRegionMap.GenderFemale;
                    try { if (h.gender == DAZDynamicItem.Gender.Male) gender = VpbSkinRegionMap.GenderMale; }
                    catch { }
                    return VpbHairFootprint.FromVabBytes(vab, gender, atom);
                }
                return null;
            }
            catch { return null; }
        }

        private static VpbClothingFootprint TryFromDisk(string itemUid, FileEntry entry, bool isHair, Atom atom)
        {
            try
            {
                byte[] vab = VpbClothingFootprint.TryReadVabBytes(itemUid, entry);
                if (vab == null) return null;
                int gender = VpbClothingFootprint.GenderFromPath(itemUid);
                return isHair
                    ? VpbHairFootprint.FromVabBytes(vab, gender, atom)
                    : VpbClothingFootprint.FromVabBytes(vab, gender);
            }
            catch
            {
                return null;
            }
        }
    }
}
