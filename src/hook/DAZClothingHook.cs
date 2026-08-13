using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Clothing activate hooks + custom-texture rebind after dynamic item load (issue #80).
    /// VPB's ImageLoadingMgr often finishes MaterialOptions custom textures before skin-wrap /
    /// cloth materials finish connecting. OnTexture*Loaded then paints a live MaterialOptions whose
    /// material slots are not ready yet (or get replaced). URL JSON stays correct; GPU tex/scale wrong.
    /// Fix: postfix DAZSkinWrap.InitMaterials → sync SetAllParameters; deferred multi-pass resync
    /// for late reconnect; queue only pending URL slots.
    /// </summary>
    public static class DAZClothingHook
    {
        static readonly HashSet<int> s_PendingCustomTexResync =
            new HashSet<int>();

        static FieldInfo s_MaterialsWereInitField;
        static bool s_InsideSkinWrapGpuResync;

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

                var mOnLoadComplete = AccessTools.Method(typeof(JSONStorableDynamic), "OnLoadComplete", new[] { typeof(bool) });
                if (mOnLoadComplete != null)
                {
                    harmony.Patch(
                        mOnLoadComplete,
                        postfix: new HarmonyMethod(typeof(DAZClothingHook), nameof(PostOnLoadComplete)));
                }
                else
                {
                    LogUtil.LogWarning("DAZClothingHook: JSONStorableDynamic.OnLoadComplete not found; custom texture resync disabled.");
                }

                // VaM AppearancePresets / ClothingPresets UI (and any LoadPresetPost path) — items already
                // loaded skip OnLoadComplete; still need a settle-pass Reload of customTexture URLs.
                var mLoadPresetPost = AccessTools.Method(typeof(MeshVR.PresetManager), "LoadPresetPost");
                if (mLoadPresetPost != null)
                {
                    harmony.Patch(
                        mLoadPresetPost,
                        postfix: new HarmonyMethod(typeof(DAZClothingHook), nameof(PostLoadPresetPost)));
                }

                // Root-cause edge: first InitMaterials clones GPUmaterials after early OnTexture*Loaded
                // may have painted pre-init / null-wrap slots. Re-push loaded customs onto clones.
                var mInitMaterials = AccessTools.Method(typeof(DAZSkinWrap), "InitMaterials", Type.EmptyTypes);
                if (mInitMaterials != null)
                {
                    s_MaterialsWereInitField = AccessTools.Field(typeof(DAZSkinWrap), "_materialsWereInit");
                    harmony.Patch(
                        mInitMaterials,
                        prefix: new HarmonyMethod(typeof(DAZClothingHook), nameof(PreInitMaterials)),
                        postfix: new HarmonyMethod(typeof(DAZClothingHook), nameof(PostInitMaterials)));
                }
                else
                {
                    LogUtil.LogWarning("DAZClothingHook: DAZSkinWrap.InitMaterials not found; GPU-init texture rebind disabled.");
                }

                // Ordering guard: a garment's own baked customTexture_* urls race the preset's urls
                // because VPB bypasses VaM's FIFO image queue. Without this, the InitMaterials rebind
                // above faithfully re-pushes whatever won the race — including the factory texture.
                MaterialOptionsTextureGuard.PatchAll(harmony);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("DAZClothingHook PatchAll failed: " + ex);
            }
        }

        /// <summary>
        /// Drop per-session bookkeeping. Called at scene-load start and plugin teardown: statics do
        /// not reset between scene loads, Unity recycles instance ids, and a coroutine killed with
        /// its host never runs its finally — so a leaked id would silently suppress a later resync.
        /// </summary>
        public static void ResetTransientState()
        {
            try { s_PendingCustomTexResync.Clear(); } catch { }
            try { MaterialOptionsTextureGuard.Reset(); } catch { }
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

        public static void PreSetActiveClothingItem(DAZCharacterSelector __instance, DAZClothingItem item, bool active, bool fromRestore)
        {
            // Issue #12: UI Assist / SetActive on scan-excluded clothing packages.
            // Object path: item already in catalog — register package files only (no Force Refresh).
            if (!active || item == null || fromRestore) return;
            try
            {
                string uid = VamOnDemandLoader.ResolvePackageUidForDynamicItem(
                    item.packageUid, item.uid, item.backupId);
                VamOnDemandLoader.EnsurePackageReadyForDynamicItemActivation(
                    uid, "set_active_clothing_item", allowCatalogForceRefresh: false);
            }
            catch { }
        }

        public static void PreSetActiveClothingItemByUid(DAZCharacterSelector __instance, string itemId, bool active, bool fromRestore)
        {
            // Issue #12: string-id path — Force Refresh only when catalog miss (UI Assist).
            // fromRestore: AtomHook batches native refresh; never Force mid-restore.
            if (!active || string.IsNullOrEmpty(itemId)) return;
            try
            {
                string uid = VamOnDemandLoader.UidFromEntryPath(itemId);
                if (string.IsNullOrEmpty(uid)) return;

                bool catalogMiss = __instance == null || !__instance.IsClothingUIDAvailable(itemId);
                bool allowForce = catalogMiss && !fromRestore;
                VamOnDemandLoader.EnsurePackageReadyForDynamicItemActivation(
                    uid, "set_active_clothing_uid", allowForce);
            }
            catch { }
        }

        public static void PreRemoveAllClothing(DAZCharacterSelector __instance)
        {
        }

        /// <summary>
        /// __state = true when this call will perform the one-shot GPUmaterials clone.
        /// </summary>
        public static void PreInitMaterials(DAZSkinWrap __instance, ref bool __state)
        {
            __state = false;
            if (__instance == null || s_InsideSkinWrapGpuResync) return;
            try
            {
                if (s_MaterialsWereInitField == null) return;
                object v = s_MaterialsWereInitField.GetValue(__instance);
                bool wereInit = v is bool && (bool)v;
                if (wereInit) return;
                // Match native InitMaterials gate — only when this call will clone.
                if (__instance.GPUmaterials == null) return;
                __state = true;
            }
            catch { __state = false; }
        }

        /// <summary>
        /// After first InitMaterials clone: sync-push MaterialOptions customTexture* onto new GPUmaterials.
        /// </summary>
        public static void PostInitMaterials(DAZSkinWrap __instance, bool __state)
        {
            if (!__state || __instance == null) return;
            if (s_InsideSkinWrapGpuResync) return;

            try
            {
                s_InsideSkinWrapGpuResync = true;

                MaterialOptions[] mos = null;
                try { mos = __instance.GetComponents<MaterialOptions>(); } catch { }
                if (mos == null || mos.Length == 0) return;

                for (int i = 0; i < mos.Length; i++)
                {
                    MaterialOptions mo = mos[i];
                    if (mo == null) continue;
                    ClothingLoadingUtils.ReapplyMaterialOptionsAfterGpuInit(mo);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("DAZClothingHook: InitMaterials custom texture rebind failed: " + ex.Message);
            }
            finally
            {
                s_InsideSkinWrapGpuResync = false;
            }
        }

        /// <summary>
        /// After PostLoadJSONRestore has applied preset material URLs onto freshly spawned
        /// MaterialOptions, schedule a delayed custom-texture Reload so GPU binds after materials settle.
        /// </summary>
        public static void PostOnLoadComplete(JSONStorableDynamic __instance)
        {
            var clothing = __instance as DAZClothingItem;
            if (clothing == null) return;
            if (!clothing.active) return;

            ScheduleItemCustomTextureResync(clothing);
        }

        public static void PostLoadPresetPost(MeshVR.PresetManager __instance, bool __result)
        {
            if (!__result || __instance == null) return;

            string storableId = null;
            Atom atom = null;
            try
            {
                var storable = __instance.GetComponentInParent<JSONStorable>();
                if (storable != null)
                {
                    storableId = storable.storeId;
                    atom = storable.GetComponentInParent<Atom>();
                }
            }
            catch { }

            if (atom == null) return;
            if (!string.Equals(atom.type, "Person", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrEmpty(storableId)) return;

            // Appearance + clothing presets: only ClothingPresets needs atom-wide rebind here.
            // AppearancePresets with Keep clothing was reloading every custom tex on each look
            // change (slow). New/replaced garments get OnLoadComplete item resync instead.
            if (!string.Equals(storableId, "ClothingPresets", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ScheduleAtomCustomTextureResync(atom);
        }

        internal static void ScheduleItemCustomTextureResync(DAZClothingItem item)
        {
            if (item == null) return;
            int id = item.GetInstanceID();
            if (!s_PendingCustomTexResync.Add(id)) return;

            MonoBehaviour host = Messager.singleton != null
                ? (MonoBehaviour)Messager.singleton
                : (MonoBehaviour)SuperController.singleton;
            if (host == null)
            {
                s_PendingCustomTexResync.Remove(id);
                return;
            }

            try
            {
                host.StartCoroutine(ClothingLoadingUtils.DeferredClothingItemCustomTextureResyncCoroutine(
                    item,
                    () => s_PendingCustomTexResync.Remove(id)));
            }
            catch
            {
                s_PendingCustomTexResync.Remove(id);
            }
        }

        /// <summary>
        /// Appearance / clothing preset apply path: items already loaded skip OnLoadComplete.
        /// Resync all active clothing MaterialOptions on the person after a short settle.
        /// </summary>
        internal static void ScheduleAtomCustomTextureResync(Atom atom)
        {
            if (atom == null) return;
            if (!string.Equals(atom.type, "Person", StringComparison.OrdinalIgnoreCase)) return;

            int id = atom.GetInstanceID();
            // Reuse pending set with negative atom ids to avoid colliding with clothing item ids.
            int key = -id;
            if (!s_PendingCustomTexResync.Add(key)) return;

            MonoBehaviour host = Messager.singleton != null
                ? (MonoBehaviour)Messager.singleton
                : (MonoBehaviour)SuperController.singleton;
            if (host == null)
            {
                s_PendingCustomTexResync.Remove(key);
                return;
            }

            try
            {
                host.StartCoroutine(ClothingLoadingUtils.DeferredAtomClothingCustomTextureResyncCoroutine(
                    atom,
                    () => s_PendingCustomTexResync.Remove(key)));
            }
            catch
            {
                s_PendingCustomTexResync.Remove(key);
            }
        }

        /// <summary>
        /// After SCENE_LOAD_TOTAL: final clothing material reconnect often drops UV tile to 1
        /// while custom tex path stays (overstretched look). Rebind URL + tile/offset then.
        /// </summary>
        internal static void SchedulePostSceneLoadCustomTextureResync()
        {
            const int key = int.MinValue;
            if (!s_PendingCustomTexResync.Add(key)) return;

            MonoBehaviour host = Messager.singleton != null
                ? (MonoBehaviour)Messager.singleton
                : (MonoBehaviour)SuperController.singleton;
            if (host == null)
            {
                s_PendingCustomTexResync.Remove(key);
                return;
            }

            try
            {
                host.StartCoroutine(PostSceneLoadCustomTextureResyncWrapper(key));
            }
            catch
            {
                s_PendingCustomTexResync.Remove(key);
            }
        }

        static System.Collections.IEnumerator PostSceneLoadCustomTextureResyncWrapper(int key)
        {
            try
            {
                yield return ClothingLoadingUtils.DeferredPostSceneLoadClothingCustomTextureResyncCoroutine();
            }
            finally
            {
                s_PendingCustomTexResync.Remove(key);
            }
        }
    }
}
