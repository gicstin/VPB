using HarmonyLib;
using System;
using UnityEngine;

namespace VPB
{
    public static class DAZClothingHook
    {
        public static void PatchAll(Harmony harmony)
        {
            try
            {
                var mSetActiveClothingItem = FindSetActiveClothingItem();
                if (mSetActiveClothingItem != null)
                {
                    harmony.Patch(mSetActiveClothingItem, prefix: new HarmonyMethod(typeof(DAZClothingHook), nameof(PreSetActiveClothingItem)));
                }
                else
                {
                    LogUtil.LogWarning("DAZClothingHook: SetActiveClothingItem method not found. Clothing hooks disabled.");
                }

                var mSetActiveClothingItemByUid = FindSetActiveClothingItemByUid();
                if (mSetActiveClothingItemByUid != null)
                {
                    harmony.Patch(mSetActiveClothingItemByUid, prefix: new HarmonyMethod(typeof(DAZClothingHook), nameof(PreSetActiveClothingItemByUid)));
                }

                var mRemoveAllClothing = AccessTools.Method(typeof(DAZCharacterSelector), "RemoveAllClothing");
                if (mRemoveAllClothing != null)
                {
                    harmony.Patch(mRemoveAllClothing, prefix: new HarmonyMethod(typeof(DAZClothingHook), nameof(PreRemoveAllClothing)));
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("DAZClothingHook PatchAll failed: " + ex);
            }
        }

        static System.Reflection.MethodInfo FindSetActiveClothingItem()
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(DAZCharacterSelector)))
            {
                if (method.Name != "SetActiveClothingItem") continue;
                var parameters = method.GetParameters();
                if (parameters.Length > 0 && parameters[0].ParameterType == typeof(DAZClothingItem))
                {
                    return method;
                }
            }
            return null;
        }

        static System.Reflection.MethodInfo FindSetActiveClothingItemByUid()
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(DAZCharacterSelector)))
            {
                if (method.Name != "SetActiveClothingItem") continue;
                var parameters = method.GetParameters();
                if (parameters.Length > 0 && parameters[0].ParameterType == typeof(string))
                {
                    return method;
                }
            }
            return null;
        }

        public static void PreSetActiveClothingItem(DAZCharacterSelector __instance, DAZClothingItem item, bool active)
        {
        }

        public static void PreSetActiveClothingItemByUid(DAZCharacterSelector __instance, string itemId, bool active)
        {
        }

        public static void PreRemoveAllClothing(DAZCharacterSelector __instance)
        {
        }
    }
}
