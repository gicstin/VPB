using HarmonyLib;
using MeshVR;
using SimpleJSON;
using System;
using System.Reflection;
using UnityEngine;

namespace VPB
{
    /// <summary>Re-apply perf hair tuning when hair changes in-scene (swap, load, restore).</summary>
    public static class VpbPerfHairHook
    {
        static MethodInfo s_SetActiveHairItemGroup;

        public static void PatchAll(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                s_SetActiveHairItemGroup = FindSetActiveHairItemGroup();
                if (s_SetActiveHairItemGroup != null)
                {
                    harmony.Patch(
                        s_SetActiveHairItemGroup,
                        postfix: new HarmonyMethod(typeof(VpbPerfHairHook), nameof(PostSetActiveHairItem)));
                }
                else
                {
                    LogUtil.LogWarning("VpbPerfHairHook: SetActiveHairItem(DAZHairGroup,...) not found.");
                }

                var mRefreshDynamicHair = AccessTools.Method(typeof(DAZCharacterSelector), "RefreshDynamicHair");
                if (mRefreshDynamicHair != null)
                {
                    harmony.Patch(
                        mRefreshDynamicHair,
                        postfix: new HarmonyMethod(typeof(VpbPerfHairHook), nameof(PostRefreshDynamicHair)));
                }

                var mOnLoadComplete = AccessTools.Method(typeof(JSONStorableDynamic), "OnLoadComplete", new[] { typeof(bool) });
                if (mOnLoadComplete != null)
                {
                    harmony.Patch(
                        mOnLoadComplete,
                        postfix: new HarmonyMethod(typeof(VpbPerfHairHook), nameof(PostOnLoadComplete)));
                }

                var mRestoreFromJson = AccessTools.Method(
                    typeof(DAZCharacterSelector),
                    "RestoreFromJSON",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(JSONArray), typeof(bool) });
                if (mRestoreFromJson != null)
                {
                    harmony.Patch(
                        mRestoreFromJson,
                        postfix: new HarmonyMethod(typeof(VpbPerfHairHook), nameof(PostRestoreFromJSON)));
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VpbPerfHairHook PatchAll failed: " + ex);
            }
        }

        static MethodInfo FindSetActiveHairItemGroup()
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(DAZCharacterSelector)))
            {
                if (method.Name != "SetActiveHairItem") continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 3 &&
                    parameters[0].ParameterType == typeof(DAZHairGroup) &&
                    parameters[1].ParameterType == typeof(bool) &&
                    parameters[2].ParameterType == typeof(bool))
                {
                    return method;
                }
            }
            return null;
        }

        public static void PostSetActiveHairItem(
            DAZCharacterSelector __instance,
            DAZHairGroup item,
            bool active,
            bool fromRestore)
        {
            if (!active) return;
            NotifySelector(__instance);
        }

        public static void PostRefreshDynamicHair(DAZCharacterSelector __instance)
        {
            NotifySelector(__instance);
        }

        public static void PostOnLoadComplete(JSONStorableDynamic __instance)
        {
            VamOnDemandLoader.NotifyDynamicItemLoaded();
            var hair = __instance as DAZHairGroup;
            if (hair == null) return;
            VpbPerfController.OnHairLoadCompleted(hair);
        }

        public static void PostRestoreFromJSON(
            DAZCharacterSelector __instance,
            JSONClass jc,
            bool restorePhysical,
            bool restoreAppearance)
        {
            if (__instance == null || !restoreAppearance) return;
            if (jc != null && jc["hair"] != null)
                NotifySelector(__instance);
        }

        static void NotifySelector(DAZCharacterSelector selector)
        {
            if (selector == null || !VpbPerfController.Enabled) return;
            try
            {
                Atom atom = selector.containingAtom;
                if (atom == null || atom.type != "Person") return;
                VpbPerfController.SchedulePersonHairApply(atom);
            }
            catch { }
        }
    }
}
