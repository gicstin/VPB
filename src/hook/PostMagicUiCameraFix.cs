using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VPB
{
    internal static class PostMagicUiCameraFix
    {
        private const string ManagerTypeName = "MacGruber.PostMagic.Manager";
        private const string HookTypeName = "MacGruber.PostMagic.CameraHook";
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Harmony harmony;
        private static PropertyInfo rawTypeProperty;

        internal static void Apply(Harmony owner)
        {
            try
            {
                MethodInfo create = AccessTools.Method(typeof(MVRPluginManager), "CreateScriptController");
                Type scriptType = create.GetParameters()[1].ParameterType;
                MethodInfo beforeAddComponent = AccessTools.Method(scriptType, "CreateBehaviourInstance", new[] { typeof(GameObject) });
                rawTypeProperty = scriptType.GetProperty("RawType", InstanceFields);
                if (beforeAddComponent == null || rawTypeProperty == null || rawTypeProperty.PropertyType != typeof(Type))
                    throw new InvalidOperationException("Dynamic script creation boundary unavailable");
                harmony = owner;
                // This shared boundary also covers bootstrap scripts, before AddComponent invokes OnEnable.
                owner.Patch(beforeAddComponent, prefix: new HarmonyMethod(typeof(PostMagicUiCameraFix), nameof(BeforeCreateBehaviour)));
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB][VRUI] PostMagic fix setup failed: " + ex.Message); }
        }

        private static void BeforeCreateBehaviour(object __instance)
        {
            try
            {
                Type manager = rawTypeProperty.GetValue(__instance, null) as Type;
                if (manager == null || manager.FullName != ManagerTypeName) return;
                Type hook = manager.Assembly.GetType(HookTypeName, false);
                if (hook == null || !typeof(MonoBehaviour).IsAssignableFrom(hook)
                    || !HasField(hook, "mainCamera", typeof(Camera)) || !HasField(hook, "uiCamera", typeof(Camera))
                    || !HasField(hook, "mainCullingMask", typeof(int)) || !HasField(hook, "uiMask", typeof(int)))
                    throw new InvalidOperationException("PostMagic CameraHook shape unsupported");

                MethodInfo enable = hook.GetMethod("OnEnable", InstanceFields, null, Type.EmptyTypes, null);
                MethodInfo disable = hook.GetMethod("OnDisable", InstanceFields, null, Type.EmptyTypes, null);
                if (enable == null || disable == null || enable.ReturnType != typeof(void) || disable.ReturnType != typeof(void))
                    throw new InvalidOperationException("PostMagic CameraHook lifecycle unsupported");

                MethodInfo enabled = AccessTools.Method(typeof(PostMagicUiCameraFix), nameof(CameraHookEnabled));
                MethodInfo disabling = AccessTools.Method(typeof(PostMagicUiCameraFix), nameof(CameraHookDisabling));
                bool enablePatched = HasPatch(enable, enabled, false);
                bool disablePatched = HasPatch(disable, disabling, true);
                // Inspect Harmony ownership instead of retaining dynamic Types across VPB unpatch/reinitialization.
                if (!enablePatched) harmony.Patch(enable, postfix: new HarmonyMethod(enabled));
                if (!disablePatched) harmony.Patch(disable, prefix: new HarmonyMethod(disabling));
                if (!enablePatched || !disablePatched)
                    LogUtil.Log("[VPB][VRUI] PostMagic camera lifecycle patched: " + manager.Assembly.GetName().Name);
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB][VRUI] PostMagic camera patch failed: " + ex.Message); }
        }

        private static bool HasField(Type type, string name, Type fieldType)
        {
            FieldInfo field = type.GetField(name, InstanceFields);
            return field != null && field.FieldType == fieldType;
        }

        private static bool HasPatch(MethodInfo original, MethodInfo callback, bool prefix)
        {
            var info = Harmony.GetPatchInfo(original);
            if (info == null) return false;
            foreach (var patch in prefix ? info.Prefixes : info.Postfixes)
                if (patch.owner == harmony.Id && patch.PatchMethod == callback) return true;
            return false;
        }

        private static void CameraHookEnabled(MonoBehaviour __instance, Camera ___mainCamera, Camera ___uiCamera,
            int ___uiMask, ref int ___mainCullingMask)
        {
            try
            {
                int sharedBaseline;
                if (!TryGetOtherOwner(__instance, ___mainCamera, ___uiCamera, out sharedBaseline)) return;
                // Later hooks captured an already-separated UI mask; retain the first owner's original UI bits.
                ___mainCullingMask = (___mainCullingMask & ~___uiMask) | (sharedBaseline & ___uiMask);
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB][VRUI] PostMagic UI baseline capture failed: " + ex.Message); }
        }

        private static bool CameraHookDisabling(MonoBehaviour __instance, Camera ___mainCamera, Camera ___uiCamera)
        {
            try
            {
                int ignored;
                if (TryGetOtherOwner(__instance, ___mainCamera, ___uiCamera, out ignored))
                {
                    // OnDisable only restores the main mask and disables the shared UI camera.
                    LogUtil.Log("[VPB][VRUI] PostMagic UI camera retained for another active owner");
                    return false;
                }
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB][VRUI] PostMagic UI owner check failed: " + ex.Message); }
            return true;
        }

        private static bool TryGetOtherOwner(MonoBehaviour self, Camera main, Camera ui, out int baseline)
        {
            baseline = 0;
            if (self == null || main == null || ui == null) return false;
            MonoBehaviour[] components = main.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour peer = components[i];
                if (peer == null || peer == self || !peer.enabled || !peer.gameObject.activeInHierarchy) continue;
                Type type = peer.GetType();
                if (type.FullName != HookTypeName) continue;
                FieldInfo mainField = type.GetField("mainCamera", InstanceFields);
                FieldInfo uiField = type.GetField("uiCamera", InstanceFields);
                FieldInfo maskField = type.GetField("mainCullingMask", InstanceFields);
                if (mainField == null || uiField == null || maskField == null || maskField.FieldType != typeof(int)) continue;
                if (mainField.GetValue(peer) as Camera != main || uiField.GetValue(peer) as Camera != ui) continue;
                baseline = (int)maskField.GetValue(peer);
                return true;
            }
            return false;
        }
    }
}
