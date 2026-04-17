using System;
using System.Reflection;
using System.Reflection.Emit;

namespace VPB
{
    /// <summary>
    /// VaM's secure file operations infer plugin signature from the call stack assembly name
    /// (expects "MVRPlugin_*"). VPB runs as a BepInEx plugin, so direct Save() calls can be
    /// rejected with "Plugin did not have signature!" on some flows. This bridge invokes
    /// scene save through a tiny dynamic assembly named like a VaM plugin to satisfy that check.
    /// </summary>
    internal static class PluginSignatureSaveBridge
    {
        private const string FakeAssemblyName = "MVRPlugin_VPBBridge_1";
        private const string FakePluginHash = "MVRPlugin_VPBBridge";

        private static readonly object InitLock = new object();
        private static Action<SuperController, string> _signedSaveInvoker;
        private static bool _initAttempted;

        public static bool TrySaveScene(string path, out Exception error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) return false;
            if (SuperController.singleton == null) return false;

            try
            {
                EnsureInitialized();
                if (_signedSaveInvoker == null) return false;
                _signedSaveInvoker(SuperController.singleton, path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initAttempted) return;
            lock (InitLock)
            {
                if (_initAttempted) return;
                _initAttempted = true;

                TryRegisterPluginSignature();
                _signedSaveInvoker = BuildSignedSaveInvoker();
            }
        }

        private static void TryRegisterPluginSignature()
        {
            try
            {
                string pluginPath = null;
                try { pluginPath = typeof(VamHookPlugin).Assembly.Location; } catch { pluginPath = null; }
                if (string.IsNullOrEmpty(pluginPath)) pluginPath = "BepInEx/plugins/VPB.dll";

                Type fmType = typeof(MVR.FileManagement.FileManager);
                TryInvokeStatic(fmType, "RegisterPluginHashToPluginPath", new object[] { FakePluginHash, pluginPath });

                // Allow scene writes through VaM's plugin secure path checks.
                TryInvokeStatic(fmType, "RegisterPluginSecureWritePath", new object[] { "Saves", false });
                TryInvokeStatic(fmType, "RegisterPluginSecureWritePath", new object[] { "Saves/scene", false });
                TryInvokeStatic(fmType, "RegisterPluginSecureWritePath", new object[] { "Saves\\scene", false });
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Failed to register plugin signature bridge: " + ex.Message);
            }
        }

        private static void TryInvokeStatic(Type t, string methodName, object[] args)
        {
            try
            {
                MethodInfo mi = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (mi == null) return;
                mi.Invoke(null, args);
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static Action<SuperController, string> BuildSignedSaveInvoker()
        {
            MethodInfo saveMethod = typeof(SuperController).GetMethod(
                "Save",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string) },
                null);

            if (saveMethod == null)
            {
                saveMethod = typeof(SuperController).GetMethod(
                    "SaveScene",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(string) },
                    null);
            }
            if (saveMethod == null) return null;

            AssemblyName an = new AssemblyName(FakeAssemblyName);
            AssemblyBuilder ab = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder mb = ab.DefineDynamicModule("Main");
            TypeBuilder tb = mb.DefineType("VPBSceneSaveBridge", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

            MethodBuilder bridge = tb.DefineMethod(
                "Invoke",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new Type[] { typeof(SuperController), typeof(string) });

            ILGenerator il = bridge.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, saveMethod);
            il.Emit(OpCodes.Ret);

            Type bridgeType = tb.CreateType();
            MethodInfo invoke = bridgeType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Static);
            if (invoke == null) return null;

            return (Action<SuperController, string>)Delegate.CreateDelegate(typeof(Action<SuperController, string>), invoke);
        }
    }
}
