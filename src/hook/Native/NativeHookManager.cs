using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace VPB.Native
{
    public class NativeHookManager
    {
        private static bool _initialized = false;
        private static IntPtr _minHookHandle = IntPtr.Zero;

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Attempt to load MinHook.x64.dll
                // We try a few common locations
                string pluginPath = Paths.PluginPath;
                string dllPath = Path.Combine(pluginPath, "MinHook.x64.dll");
                
                if (!File.Exists(dllPath))
                {
                    // Try adjacent to our assembly?
                    string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    dllPath = Path.Combine(assemblyDir, "MinHook.x64.dll");
                }

                if (File.Exists(dllPath))
                {
                    _minHookHandle = Kernel32.LoadLibrary(dllPath);
                    if (_minHookHandle == IntPtr.Zero)
                    {
                        LogUtil.LogError("NativeHookManager: Failed to LoadLibrary " + dllPath);
                        return;
                    }
                    // Initialize MinHook function pointers manually
                    MinHook.Load(_minHookHandle);
                }
                else
                {
                    LogUtil.LogError("NativeHookManager: MinHook.x64.dll not found at " + dllPath);
                    // We might fail gracefully if it's not found
                    return;
                }

                MinHook.Status status = MinHook.MH_Initialize();
                if (status != MinHook.Status.MH_OK)
                {
                    LogUtil.LogError("NativeHookManager: MH_Initialize failed: " + status);
                    return;
                }

                _initialized = true;
                LogUtil.Log("NativeHookManager: MinHook Initialized.");

                // Apply specific hooks
                BoehmGC.ApplyHooks();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("NativeHookManager: Exception during Init: " + ex.ToString());
            }
        }

        public static void Shutdown()
        {
            if (!_initialized) return;

            MinHook.MH_Uninitialize();
            
            if (_minHookHandle != IntPtr.Zero)
            {
                Kernel32.FreeLibrary(_minHookHandle);
                _minHookHandle = IntPtr.Zero;
            }
            
            _initialized = false;
        }
    }
}
