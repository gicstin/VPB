using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace VPB
{
    /// <summary>
    /// VaM's secure file operations infer plugin signature from the call stack assembly name
    /// (expects "MVRPlugin_*"). VPB runs as a BepInEx plugin, so direct calls can be rejected
    /// with "Plugin did not have signature!" on some flows. This bridge builds tiny dynamic
    /// wrapper methods inside a fake "MVRPlugin_*" assembly so calls pass the signature check.
    /// </summary>
    internal static class PluginSignatureBridge
    {
        public const string DefaultFakeAssemblyName = "MVRPlugin_VPBBridge_1";
        public const string DefaultFakePluginHash = "MVRPlugin_VPBBridge";

        private static readonly object InitLock = new object();
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Action<object, object[]>> SignedInvokerCache = new Dictionary<string, Action<object, object[]>>();
        private static readonly HashSet<string> RegisteredPluginHashes = new HashSet<string>();
        private static readonly object[] EmptyArgs = new object[0];

        public static bool TryInvoke(MethodInfo method, object target, object[] args, out Exception error, string fakeAssemblyName = null, string fakePluginHash = null)
        {
            error = null;
            if (method == null)
            {
                error = new ArgumentNullException("method");
                return false;
            }

            if (!method.IsStatic && target == null)
            {
                error = new ArgumentNullException("target");
                return false;
            }

            object[] finalArgs = args ?? EmptyArgs;
            if (method.GetParameters().Length != finalArgs.Length)
            {
                error = new ArgumentException("Argument count does not match method signature");
                return false;
            }

            string finalAssemblyName = string.IsNullOrEmpty(fakeAssemblyName) ? DefaultFakeAssemblyName : fakeAssemblyName;
            string finalPluginHash = string.IsNullOrEmpty(fakePluginHash) ? DerivePluginHash(finalAssemblyName) : fakePluginHash;

            try
            {
                EnsurePluginHashRegistered(finalPluginHash);
                VpbSaveCacheSupport.RegisterPluginSaveWritePathsNoConfirm();
                Action<object, object[]> invoker = GetOrCreateSignedInvoker(method, finalAssemblyName);
                if (invoker == null)
                {
                    error = new InvalidOperationException("Unable to build signed method invoker");
                    return false;
                }

                invoker(target, finalArgs);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static string DerivePluginHash(string fakeAssemblyName)
        {
            if (string.IsNullOrEmpty(fakeAssemblyName)) return DefaultFakePluginHash;

            int index = fakeAssemblyName.LastIndexOf('_');
            if (index <= 0) return fakeAssemblyName;
            return fakeAssemblyName.Substring(0, index);
        }

        private static void EnsurePluginHashRegistered(string pluginHash)
        {
            if (string.IsNullOrEmpty(pluginHash)) return;

            lock (InitLock)
            {
                if (RegisteredPluginHashes.Contains(pluginHash)) return;

                TryRegisterPluginSignature(pluginHash);
                RegisteredPluginHashes.Add(pluginHash);
            }
        }

        private static void TryRegisterPluginSignature(string pluginHash)
        {
            try
            {
                string pluginPath = null;
                try { pluginPath = typeof(VamHookPlugin).Assembly.Location; } catch { pluginPath = null; }
                if (string.IsNullOrEmpty(pluginPath)) pluginPath = "BepInEx/plugins/VPB.dll";

                Type fmType = typeof(MVR.FileManagement.FileManager);
                TryInvokeStatic(fmType, "RegisterPluginHashToPluginPath", new object[] { pluginHash, pluginPath });
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Failed to register plugin signature bridge: " + ex.Message);
            }
        }

        internal static void RegisterPluginSceneWritePathsNoConfirm()
        {
            VpbSaveCacheSupport.RegisterPluginSceneWritePathsNoConfirm();
        }

        private static Action<object, object[]> GetOrCreateSignedInvoker(MethodInfo method, string fakeAssemblyName)
        {
            string cacheKey = BuildCacheKey(method, fakeAssemblyName);

            lock (CacheLock)
            {
                Action<object, object[]> cached;
                if (SignedInvokerCache.TryGetValue(cacheKey, out cached)) return cached;

                Action<object, object[]> created = BuildSignedInvoker(method, fakeAssemblyName);
                if (created != null) SignedInvokerCache[cacheKey] = created;
                return created;
            }
        }

        private static string BuildCacheKey(MethodInfo method, string fakeAssemblyName)
        {
            return fakeAssemblyName + "|" + method.Module.ModuleVersionId + "|" + method.MetadataToken;
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

        private static Action<object, object[]> BuildSignedInvoker(MethodInfo method, string fakeAssemblyName)
        {
            AssemblyName an = new AssemblyName(fakeAssemblyName);
            AssemblyBuilder ab = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder mb = ab.DefineDynamicModule("Main");
            TypeBuilder tb = mb.DefineType(
                "VPBPluginSignatureBridge_" + Math.Abs(method.MetadataToken) + "_" + Guid.NewGuid().ToString("N"),
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

            MethodBuilder bridge = tb.DefineMethod(
                "Invoke",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new Type[] { typeof(object), typeof(object[]) });

            ILGenerator il = bridge.GetILGenerator();

            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, method.DeclaringType);
            }

            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);

                Type parameterType = parameters[i].ParameterType;
                if (parameterType.IsValueType)
                {
                    il.Emit(OpCodes.Unbox_Any, parameterType);
                }
                else
                {
                    il.Emit(OpCodes.Castclass, parameterType);
                }
            }

            if (method.IsStatic)
            {
                il.Emit(OpCodes.Call, method);
            }
            else if (method.IsVirtual && !method.IsFinal && !method.DeclaringType.IsValueType)
            {
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.Emit(OpCodes.Call, method);
            }

            il.Emit(OpCodes.Ret);

            Type bridgeType = tb.CreateType();
            MethodInfo invoke = bridgeType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Static);
            if (invoke == null) return null;

            return (Action<object, object[]>)Delegate.CreateDelegate(typeof(Action<object, object[]>), invoke);
        }
    }

    internal static class PluginSignatureSceneSaveBridge
    {
        private static readonly object SaveInitLock = new object();
        private static MethodInfo _saveMethod;
        private static bool _saveMethodResolved;

        public static bool TrySaveScene(string path, out Exception error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) return false;
            if (SuperController.singleton == null) return false;

            MethodInfo saveMethod = ResolveSaveMethod();
            if (saveMethod == null) return false;

            VpbSaveCacheSupport.RegisterPluginSaveWritePathsNoConfirm();

            return PluginSignatureBridge.TryInvoke(
                saveMethod,
                SuperController.singleton,
                new object[] { path },
                out error,
                PluginSignatureBridge.DefaultFakeAssemblyName,
                PluginSignatureBridge.DefaultFakePluginHash);
        }

        private static MethodInfo ResolveSaveMethod()
        {
            if (_saveMethodResolved) return _saveMethod;
            lock (SaveInitLock)
            {
                if (_saveMethodResolved) return _saveMethod;
                _saveMethodResolved = true;

                _saveMethod = typeof(SuperController).GetMethod(
                    "Save",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (_saveMethod == null)
                {
                    _saveMethod = typeof(SuperController).GetMethod(
                        "SaveScene",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new Type[] { typeof(string) },
                        null);
                }

                return _saveMethod;
            }
        }
    }

    // Backward-compatible alias for existing call sites.
    internal static class PluginSignatureSaveBridge
    {
        public static bool TrySaveScene(string path, out Exception error)
        {
            return PluginSignatureSceneSaveBridge.TrySaveScene(path, out error);
        }
    }
}
