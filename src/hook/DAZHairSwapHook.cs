using HarmonyLib;
using MeshVR;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Keeps outgoing hair visible until incoming hair finishes loading during preset restore.
    /// Outgoing hair collisions are disabled first; mesh hide runs only after incoming hair is ready.
    /// </summary>
    public static class DAZHairSwapHook
    {
        sealed class HairSwapSession
        {
            public bool swapActive;
            public bool finalizeScheduled;
            public bool executingVanilla;
            public readonly HashSet<DAZHairGroup> incoming = new HashSet<DAZHairGroup>();
            public readonly HashSet<DAZHairGroup> pendingHide = new HashSet<DAZHairGroup>();
            public readonly HashSet<DAZHairGroup> outgoingAtStart = new HashSet<DAZHairGroup>();
            public readonly Dictionary<string, bool> targetEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> loadWatchers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Coroutine finalizeCoroutine;
        }

        static readonly Dictionary<int, HairSwapSession> s_SessionsBySelectorId =
            new Dictionary<int, HairSwapSession>();

        static MethodInfo s_SetActiveHairItemGroup;
        static MethodInfo s_ResetHair;
        static MethodInfo s_IsCustomAppearanceParamLocked;
        static FieldInfo s_InsideRestoreField;
        static FieldInfo s_HairItemJsonsField;
        static MethodInfo s_SyncAnatomy;

        static bool Enabled =>
            Settings.Instance != null &&
            Settings.Instance.HairSwapKeepVisibleUntilLoaded != null &&
            Settings.Instance.HairSwapKeepVisibleUntilLoaded.Value;

        public static void PatchAll(Harmony harmony)
        {
            try
            {
                s_SetActiveHairItemGroup = FindSetActiveHairItemGroup();
                s_ResetHair = AccessTools.Method(typeof(DAZCharacterSelector), "ResetHair", new[] { typeof(bool) });
                s_IsCustomAppearanceParamLocked = AccessTools.Method(typeof(DAZCharacterSelector), "IsCustomAppearanceParamLocked");
                s_InsideRestoreField = AccessTools.Field(typeof(DAZCharacterSelector), "insideRestore");
                s_SyncAnatomy = AccessTools.Method(typeof(DAZCharacterSelector), "SyncAnatomy");
                s_HairItemJsonsField = AccessTools.Field(typeof(DAZCharacterSelector), "hairItemJSONs");

                var mRestoreFromJson = AccessTools.Method(
                    typeof(DAZCharacterSelector),
                    "RestoreFromJSON",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(JSONArray), typeof(bool) });

                if (mRestoreFromJson != null)
                {
                    harmony.Patch(
                        mRestoreFromJson,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreRestoreFromJSON)),
                        postfix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PostRestoreFromJSON)));
                }
                else
                {
                    LogUtil.LogWarning("DAZHairSwapHook: RestoreFromJSON not found.");
                }

                if (s_ResetHair != null)
                {
                    harmony.Patch(
                        s_ResetHair,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreResetHair)));
                }

                if (s_SetActiveHairItemGroup != null)
                {
                    harmony.Patch(
                        s_SetActiveHairItemGroup,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreSetActiveHairItem)));
                }
                else
                {
                    LogUtil.LogWarning("DAZHairSwapHook: SetActiveHairItem(DAZHairGroup,...) not found.");
                }

                var mRefreshDynamicHair = AccessTools.Method(typeof(DAZCharacterSelector), "RefreshDynamicHair");
                if (mRefreshDynamicHair != null)
                {
                    harmony.Patch(
                        mRefreshDynamicHair,
                        postfix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PostRefreshDynamicHair)));
                }

                var mUnloadInactive = AccessTools.Method(typeof(DAZCharacterSelector), "UnloadInactiveHairItems");
                if (mUnloadInactive != null)
                {
                    harmony.Patch(
                        mUnloadInactive,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreBlockHairUnloadDuringSwap)));
                }

                var mUnloadDisabled = AccessTools.Method(typeof(DAZCharacterSelector), "UnloadDisabledHairItems");
                if (mUnloadDisabled != null)
                {
                    harmony.Patch(
                        mUnloadDisabled,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreBlockHairUnloadDuringSwap)));
                }

                var mOnLoadComplete = AccessTools.Method(typeof(JSONStorableDynamic), "OnLoadComplete", new[] { typeof(bool) });
                if (mOnLoadComplete != null)
                {
                    harmony.Patch(
                        mOnLoadComplete,
                        postfix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PostOnLoadComplete)));
                }

                var mRestoreStorables = AccessTools.Method(typeof(PresetManager), "RestoreStorables");
                if (mRestoreStorables != null)
                {
                    harmony.Patch(
                        mRestoreStorables,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreRestoreStorables)));
                }

                var mJsRestore = AccessTools.Method(
                    typeof(JSONStorable),
                    "RestoreFromJSON",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(JSONArray), typeof(bool) });
                if (mJsRestore != null)
                {
                    harmony.Patch(
                        mJsRestore,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreJsonStorableRestoreFromJSON)));
                }

                var mJsLateRestore = AccessTools.Method(
                    typeof(JSONStorable),
                    "LateRestoreFromJSON",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(bool) });
                if (mJsLateRestore != null)
                {
                    harmony.Patch(
                        mJsLateRestore,
                        prefix: new HarmonyMethod(typeof(DAZHairSwapHook), nameof(PreJsonStorableLateRestoreFromJSON)));
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("DAZHairSwapHook PatchAll failed: " + ex);
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

        static HairSwapSession GetSession(DAZCharacterSelector selector, bool create)
        {
            if (selector == null) return null;
            int id = selector.GetInstanceID();
            if (s_SessionsBySelectorId.TryGetValue(id, out var session))
            {
                return session;
            }
            if (!create) return null;
            session = new HairSwapSession();
            s_SessionsBySelectorId[id] = session;
            return session;
        }

        static void RemoveSession(DAZCharacterSelector selector)
        {
            if (selector == null) return;
            int id = selector.GetInstanceID();
            if (!s_SessionsBySelectorId.TryGetValue(id, out var session)) return;

            if (session.finalizeCoroutine != null && SuperController.singleton != null)
            {
                try { SuperController.singleton.StopCoroutine(session.finalizeCoroutine); } catch { }
            }
            s_SessionsBySelectorId.Remove(id);
        }

        static bool IsInsideRestore(DAZCharacterSelector selector)
        {
            if (selector == null || s_InsideRestoreField == null) return false;
            try { return (bool)s_InsideRestoreField.GetValue(selector); }
            catch { return false; }
        }

        static bool IsHairLocked(DAZCharacterSelector selector)
        {
            if (selector == null || s_IsCustomAppearanceParamLocked == null) return false;
            try
            {
                object result = s_IsCustomAppearanceParamLocked.Invoke(selector, new object[] { "hair" });
                return result is bool b && b;
            }
            catch { return false; }
        }

        static void ParseTargetHairState(JSONClass jc, HairSwapSession session)
        {
            session.targetEnabled.Clear();
            if (jc == null || jc["hair"] == null) return;

            JSONArray asArray = jc["hair"].AsArray;
            if (asArray != null)
            {
                foreach (JSONClass item in asArray)
                {
                    if (item == null) continue;
                    string id = item["id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    string normalized = MVR.FileManagement.FileManager.NormalizeID(id);
                    session.targetEnabled[normalized] = item["enabled"].AsBool;

                    string internalId = item["internalId"];
                    if (!string.IsNullOrEmpty(internalId))
                    {
                        session.targetEnabled[internalId] = item["enabled"].AsBool;
                    }
                }
                return;
            }

            string single = jc["hair"];
            if (!string.IsNullOrEmpty(single))
            {
                session.targetEnabled[MVR.FileManagement.FileManager.NormalizeID(single)] = true;
            }
        }

        static void ParseTargetHairFromPreset(JSONClass presetJson, HairSwapSession session)
        {
            if (presetJson == null) return;
            if (presetJson["hair"] != null)
            {
                ParseTargetHairState(presetJson, session);
                return;
            }

            JSONArray storables = presetJson["storables"] != null ? presetJson["storables"].AsArray : null;
            if (storables == null) return;

            foreach (JSONNode node in storables)
            {
                JSONClass st = node as JSONClass;
                if (st == null || st["hair"] == null) continue;
                string id = st["id"];
                if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase))
                {
                    ParseTargetHairState(st, session);
                    return;
                }
            }
        }

        static bool IsHairPresetManager(PresetManager pm)
        {
            if (pm == null) return false;
            return pm.itemType == PresetManager.ItemType.HairFemale
                || pm.itemType == PresetManager.ItemType.HairMale
                || pm.itemType == PresetManager.ItemType.HairNeutral;
        }

        static DAZCharacterSelector FindCharacterSelector(Component component)
        {
            if (component == null) return null;
            try
            {
                Atom atom = component.GetComponentInParent<Atom>();
                if (atom != null) return atom.GetComponentInChildren<DAZCharacterSelector>();
            }
            catch { }
            return null;
        }

        static DAZHairGroup FindOwningHairGroup(JSONStorable storable)
        {
            if (storable == null) return null;
            try
            {
                Transform t = storable.transform;
                while (t != null)
                {
                    DAZHairGroup hair = t.GetComponent<DAZHairGroup>();
                    if (hair != null) return hair;
                    t = t.parent;
                }
            }
            catch { }
            return null;
        }

        static bool IsOutgoingHairDuringSwap(DAZHairGroup hair, HairSwapSession session)
        {
            if (hair == null || session == null || !session.swapActive) return false;
            if (ShouldKeepHairVisible(hair, session)) return false;
            if (session.outgoingAtStart.Contains(hair)) return true;
            if (session.pendingHide.Contains(hair)) return true;
            return (hair.active || hair.gameObject.activeSelf) && !session.incoming.Contains(hair);
        }

        static void SnapshotOutgoingHair(DAZCharacterSelector selector, HairSwapSession session)
        {
            session.outgoingAtStart.Clear();
            if (selector == null) return;

            try
            {
                DAZHairGroup[] items = selector.hairItems;
                if (items == null) return;
                for (int i = 0; i < items.Length; i++)
                {
                    DAZHairGroup item = items[i];
                    if (item == null) continue;
                    if (item.active || item.gameObject.activeSelf)
                    {
                        session.outgoingAtStart.Add(item);
                    }
                }
            }
            catch { }
        }

        static void BeginSwapSession(DAZCharacterSelector selector, JSONClass presetJson, bool fromGeometryDirect)
        {
            if (selector == null || presetJson == null) return;
            if (selector.mergeRestore) return;
            if (IsHairLocked(selector)) return;

            var session = GetSession(selector, create: true);
            if (session.finalizeCoroutine != null && SuperController.singleton != null)
            {
                try { SuperController.singleton.StopCoroutine(session.finalizeCoroutine); } catch { }
                session.finalizeCoroutine = null;
            }

            if (!session.swapActive)
            {
                SnapshotOutgoingHair(selector, session);
            }

            session.swapActive = true;
            session.finalizeScheduled = false;
            session.incoming.Clear();
            session.pendingHide.Clear();
            session.loadWatchers.Clear();

            if (fromGeometryDirect)
            {
                ParseTargetHairState(presetJson, session);
            }
            else
            {
                ParseTargetHairFromPreset(presetJson, session);
            }
        }

        public static void PreRestoreStorables(PresetManager __instance)
        {
            if (!Enabled || __instance == null) return;
            if (!IsHairPresetManager(__instance)) return;

            DAZCharacterSelector selector = FindCharacterSelector(__instance);
            if (selector == null) return;

            JSONClass jc = __instance.filteredJSON ?? __instance.lastLoadedJSON;
            if (jc == null) return;

            BeginSwapSession(selector, jc, fromGeometryDirect: false);
        }

        public static bool PreJsonStorableRestoreFromJSON(
            JSONStorable __instance,
            JSONClass jc,
            bool restorePhysical,
            bool restoreAppearance)
        {
            if (!Enabled || __instance == null || !restoreAppearance) return true;
            if (!__instance.isPresetRestore) return true;

            DAZHairGroup hair = FindOwningHairGroup(__instance);
            if (hair == null || hair.characterSelector == null) return true;

            var session = GetSession(hair.characterSelector, create: false);
            if (session == null || !session.swapActive) return true;
            if (!IsOutgoingHairDuringSwap(hair, session)) return true;

            return false;
        }

        public static bool PreJsonStorableLateRestoreFromJSON(
            JSONStorable __instance,
            JSONClass jc,
            bool restorePhysical,
            bool restoreAppearance)
        {
            return PreJsonStorableRestoreFromJSON(__instance, jc, restorePhysical, restoreAppearance);
        }

        static bool ShouldHairBeEnabled(DAZHairGroup item, HairSwapSession session)
        {
            if (item == null) return false;
            if (session.targetEnabled.Count == 0) return false;

            if (session.targetEnabled.TryGetValue(item.uid, out bool byUid)) return byUid;
            if (!string.IsNullOrEmpty(item.internalUid) && session.targetEnabled.TryGetValue(item.internalUid, out bool byInternal)) return byInternal;
            if (!string.IsNullOrEmpty(item.backupId) && session.targetEnabled.TryGetValue(item.backupId, out bool byBackup)) return byBackup;
            return false;
        }

        public static void PreRestoreFromJSON(
            DAZCharacterSelector __instance,
            JSONClass jc,
            bool restorePhysical,
            bool restoreAppearance)
        {
            if (!Enabled || __instance == null || jc == null) return;
            if (!restoreAppearance || jc["hair"] == null) return;
            BeginSwapSession(__instance, jc, fromGeometryDirect: true);
        }

        public static void PostRestoreFromJSON(DAZCharacterSelector __instance)
        {
            if (!Enabled || __instance == null) return;
            var session = GetSession(__instance, create: false);
            if (session == null || !session.swapActive) return;

            ReconcilePendingHide(__instance, session);
            ScheduleFinalize(__instance, session);
        }

        public static bool PreResetHair(DAZCharacterSelector __instance, bool clearAll)
        {
            if (!Enabled || !clearAll || __instance == null) return true;
            var session = GetSession(__instance, create: false);
            if (session != null && session.swapActive) return false;
            return true;
        }

        public static bool PreSetActiveHairItem(
            DAZCharacterSelector __instance,
            DAZHairGroup item,
            bool active,
            bool fromRestore)
        {
            if (!Enabled || __instance == null || item == null) return true;

            var session = GetSession(__instance, create: false);
            if (session == null || !session.swapActive || session.executingVanilla) return true;

            if (item.locked)
            {
                if (IsInsideRestore(__instance)) return true;
            }

            if (active)
            {
                session.incoming.Add(item);
                EnsureLoadWatcher(__instance, session, item);
                return true;
            }

            if (fromRestore || session.pendingHide.Contains(item))
            {
                SetHairCollisions(item, false);
                session.pendingHide.Add(item);
                return false;
            }

            return true;
        }

        public static bool PreBlockHairUnloadDuringSwap(DAZCharacterSelector __instance)
        {
            if (!Enabled || __instance == null) return true;
            var session = GetSession(__instance, create: false);
            if (session != null && session.swapActive) return false;
            return true;
        }

        public static void PostOnLoadComplete(JSONStorableDynamic __instance)
        {
            if (!Enabled || __instance == null) return;
            var hair = __instance as DAZHairGroup;
            if (hair == null || hair.characterSelector == null) return;

            var session = GetSession(hair.characterSelector, create: false);
            if (session == null || !session.swapActive) return;
            if (!session.incoming.Contains(hair)) return;

            ScheduleFinalize(hair.characterSelector, session);
        }

        static void EnsureLoadWatcher(DAZCharacterSelector selector, HairSwapSession session, DAZHairGroup item)
        {
            if (selector == null || item == null || session == null) return;
            if (item.ready) return;

            string key = item.uid ?? item.GetInstanceID().ToString();
            if (!session.loadWatchers.Add(key)) return;

            try
            {
                JSONStorableDynamic.OnLoaded callback = () =>
                {
                    try
                    {
                        if (item != null && item.characterSelector != null)
                        {
                            ScheduleFinalize(item.characterSelector, session);
                        }
                    }
                    catch { }
                };
                item.onLoadedHandlers = (JSONStorableDynamic.OnLoaded)Delegate.Combine(item.onLoadedHandlers, callback);
            }
            catch { }
        }

        public static void PostRefreshDynamicHair(DAZCharacterSelector __instance)
        {
            if (!Enabled || __instance == null) return;
            var session = GetSession(__instance, create: false);
            if (session == null || !session.swapActive) return;

            try
            {
                DAZHairGroup[] items = __instance.hairItems;
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        DAZHairGroup item = items[i];
                        if (item == null) continue;
                        if (!ShouldHairBeEnabled(item, session)) continue;
                        session.incoming.Add(item);
                        EnsureLoadWatcher(__instance, session, item);
                    }
                }
            }
            catch { }

            ReconcilePendingHide(__instance, session);
            session.finalizeScheduled = false;
            ScheduleFinalize(__instance, session);
        }

        static bool ShouldKeepHairVisible(DAZHairGroup item, HairSwapSession session)
        {
            if (item == null || session == null) return false;
            if (session.incoming.Contains(item)) return true;
            return ShouldHairBeEnabled(item, session);
        }

        static List<DAZHairGroup> CollectHairToHide(DAZCharacterSelector selector, HairSwapSession session)
        {
            var result = new List<DAZHairGroup>();
            if (selector == null || session == null) return result;

            ReconcilePendingHide(selector, session);

            try
            {
                DAZHairGroup[] items = selector.hairItems;
                if (items == null) return result;

                for (int i = 0; i < items.Length; i++)
                {
                    DAZHairGroup item = items[i];
                    if (item == null) continue;
                    if (ShouldKeepHairVisible(item, session)) continue;
                    if (!item.active && !item.gameObject.activeSelf) continue;
                    if (item.locked) continue;
                    result.Add(item);
                }
            }
            catch { }

            return result;
        }

        static void ReconcilePendingHide(DAZCharacterSelector selector, HairSwapSession session)
        {
            if (selector == null || session == null) return;

            try
            {
                DAZHairGroup[] items = selector.hairItems;
                if (items == null) return;

                for (int i = 0; i < items.Length; i++)
                {
                    DAZHairGroup item = items[i];
                    if (item == null) continue;
                    if (ShouldHairBeEnabled(item, session)) continue;
                    if (!item.active && !item.gameObject.activeSelf) continue;

                    SetHairCollisions(item, false);
                    session.pendingHide.Add(item);
                }
            }
            catch { }
        }

        static void ScheduleFinalize(DAZCharacterSelector selector, HairSwapSession session)
        {
            if (selector == null || session == null || !session.swapActive) return;
            if (session.finalizeScheduled) return;
            session.finalizeScheduled = true;

            if (SuperController.singleton == null)
            {
                try { FinalizeSwapImmediate(selector, session); }
                catch { EndSwap(selector); }
                return;
            }

            session.finalizeCoroutine = SuperController.singleton.StartCoroutine(FinalizeSwapCoroutine(selector, session));
        }

        static IEnumerator FinalizeSwapCoroutine(DAZCharacterSelector selector, HairSwapSession session)
        {
            yield return null;
            yield return null;

            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (AllIncomingReady(session)) break;
                yield return null;
            }

            try { FinalizeSwapImmediate(selector, session); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB] DAZHairSwapHook finalize failed: " + ex.Message); }
            finally { EndSwap(selector); }
        }

        static bool AllIncomingReady(HairSwapSession session)
        {
            if (session == null) return true;
            if (session.incoming.Count == 0) return true;

            foreach (var item in session.incoming)
            {
                if (item == null) continue;
                if (!item.active && !item.gameObject.activeSelf) continue;
                if (!item.ready) return false;
            }
            return true;
        }

        static void FinalizeSwapImmediate(DAZCharacterSelector selector, HairSwapSession session)
        {
            if (selector == null || session == null) return;

            session.executingVanilla = true;
            try
            {
                List<DAZHairGroup> toHide = CollectHairToHide(selector, session);
                for (int i = 0; i < toHide.Count; i++)
                {
                    DAZHairGroup item = toHide[i];
                    if (item == null) continue;
                    SetHairCollisions(item, false);
                    DisableHairItemDirect(selector, item);
                }
            }
            finally
            {
                session.executingVanilla = false;
            }
        }

        static void DisableHairItemDirect(DAZCharacterSelector selector, DAZHairGroup item)
        {
            if (selector == null || item == null) return;

            try
            {
                if (item.locked) item.SetLocked(false);
                item.active = false;
                item.gameObject.SetActive(false);
                SyncHairItemToggle(selector, item, false);
                InvokeSyncAnatomy(selector);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] DAZHairSwapHook disable hair failed: " + ex.Message);
            }
        }

        static void InvokeSyncAnatomy(DAZCharacterSelector selector)
        {
            if (selector == null || s_SyncAnatomy == null) return;
            try { s_SyncAnatomy.Invoke(selector, null); }
            catch { }
        }

        static void SyncHairItemToggle(DAZCharacterSelector selector, DAZHairGroup item, bool active)
        {
            if (selector == null || item == null || string.IsNullOrEmpty(item.uid)) return;
            if (s_HairItemJsonsField == null) return;

            try
            {
                var dict = s_HairItemJsonsField.GetValue(selector) as Dictionary<string, JSONStorableBool>;
                if (dict != null && dict.TryGetValue(item.uid, out JSONStorableBool toggle) && toggle != null)
                {
                    toggle.val = active;
                }
            }
            catch { }
        }

        static void EndSwap(DAZCharacterSelector selector)
        {
            RemoveSession(selector);
        }

        static void SetHairCollisions(DAZHairGroup group, bool enabled)
        {
            if (group == null) return;

            try
            {
                HairSimControl[] sims = group.GetComponentsInChildren<HairSimControl>(true);
                if (sims != null)
                {
                    for (int i = 0; i < sims.Length; i++)
                    {
                        HairSimControl sim = sims[i];
                        if (sim == null) continue;
                        JSONStorableBool collision = sim.GetBoolJSONParam("collisionEnabled");
                        if (collision != null) collision.val = enabled;
                    }
                }
            }
            catch { }

            try
            {
                if (group.hairColliders == null) return;
                for (int i = 0; i < group.hairColliders.Length; i++)
                {
                    CapsuleCollider capsule = group.hairColliders[i];
                    if (capsule != null) capsule.enabled = enabled;
                }
            }
            catch { }
        }

    }
}
