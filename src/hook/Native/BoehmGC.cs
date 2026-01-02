using System;
using System.Runtime.InteropServices;

namespace VPB.Native
{
    public static class BoehmGC
    {
        // internal void GC_gcollect(void)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GC_gcollect_Delegate();

        // public void mono_gc_collect(int generation)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void mono_gc_collect_Delegate(int generation);

        private static IntPtr _originalGCCollect;
        private static Delegate _originalDelegate; // Can be either of above
        private static bool _isMonoApi = false;
        private static long _lastCollectTime = 0;
        private const long MinIntervalTicks = 1000 * 10000; // 1 second in ticks (10,000 ticks per ms)

        public static void ApplyHooks()
        {
            IntPtr gcCollectAddr = IntPtr.Zero;
            string[] modulesToTry = { "mono.dll", "mono-2.0-bdwgc.dll", "UnityPlayer.dll" };
            IntPtr moduleHandle = IntPtr.Zero;

            // 1. Try GetProcAddress across common modules
            foreach (var modName in modulesToTry)
            {
                moduleHandle = Kernel32.GetModuleHandle(modName);
                if (moduleHandle != IntPtr.Zero)
                {
                    // Try exact internal name
                    gcCollectAddr = Kernel32.GetProcAddress(moduleHandle, "GC_gcollect");
                    if (gcCollectAddr != IntPtr.Zero)
                    {
                        LogUtil.Log("BoehmGC: Found export GC_gcollect in " + modName);
                        _isMonoApi = false;
                        break;
                    }
                    
                    // Try public mono api name
                    gcCollectAddr = Kernel32.GetProcAddress(moduleHandle, "mono_gc_collect");
                    if (gcCollectAddr != IntPtr.Zero)
                    {
                        LogUtil.Log("BoehmGC: Found export mono_gc_collect in " + modName);
                        _isMonoApi = true;
                        break;
                    }
                }
            }

            // 2. Fallback to SigScan if not exported (Assumes GC_gcollect internal signature)
            if (gcCollectAddr == IntPtr.Zero)
            {
                moduleHandle = Kernel32.GetModuleHandle("UnityPlayer.dll");
                if (moduleHandle != IntPtr.Zero)
                {
                    _isMonoApi = false; // Internal functions usually match GC_gcollect void(void)
                    
                    // Attempt 1: Unity 2018.x Mono GC_gcollect
                    gcCollectAddr = SigScanner.Scan(moduleHandle, "48 83 EC 28 E8 ?? ?? ?? ?? 90 48 83 C4 28 C3");

                    if (gcCollectAddr == IntPtr.Zero)
                    {
                        // Attempt 2: Alternative pattern
                        gcCollectAddr = SigScanner.Scan(moduleHandle, "40 53 48 83 EC 20 48 8B D9 E8 ?? ?? ?? ?? 48 83 C4 20 5B C3");
                    }
                    
                    if (gcCollectAddr == IntPtr.Zero)
                    {
                        // Attempt 3: Unity 2019+ / Newer 2018
                        gcCollectAddr = SigScanner.Scan(moduleHandle, "48 83 EC 28 80 3D ?? ?? ?? ?? 00 74 ?? E8");
                    }
                }
            }

            if (gcCollectAddr != IntPtr.Zero)
            {
                LogUtil.Log("BoehmGC: Found GC collect function at 0x" + gcCollectAddr.ToString("X"));

                Delegate detourDelegate;
                if (_isMonoApi)
                {
                    detourDelegate = new mono_gc_collect_Delegate(Detour_mono_gc_collect);
                }
                else
                {
                    detourDelegate = new GC_gcollect_Delegate(Detour_GC_gcollect);
                }

                MinHook.Status result = MinHook.MH_CreateHook(gcCollectAddr, 
                    Marshal.GetFunctionPointerForDelegate(detourDelegate), 
                    out _originalGCCollect);

                if (result == MinHook.Status.MH_OK)
                {
                    MinHook.MH_EnableHook(gcCollectAddr);
                    
                    if (_isMonoApi)
                        _originalDelegate = Marshal.GetDelegateForFunctionPointer(_originalGCCollect, typeof(mono_gc_collect_Delegate));
                    else
                        _originalDelegate = Marshal.GetDelegateForFunctionPointer(_originalGCCollect, typeof(GC_gcollect_Delegate));
                        
                    LogUtil.Log("BoehmGC: Hooked successfully.");
                }
                else
                {
                    LogUtil.LogError("BoehmGC: Failed to hook: " + result);
                }
            }
            else
            {
                LogUtil.Log("BoehmGC: GC collect function not found.");
            }
        }

        private static bool _hasLoggedInterception = false;

        private static void Detour_GC_gcollect()
        {
            if (!_hasLoggedInterception)
            {
                _hasLoggedInterception = true;
                LogUtil.Log("BoehmGC: GC_gcollect intercepted.");
            }
            
            // Rate Limiting Logic
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastCollectTime < MinIntervalTicks)
            {
                 // Skip collection if too frequent
                 return;
            }
            _lastCollectTime = now;

            if (_originalDelegate != null)
                ((GC_gcollect_Delegate)_originalDelegate)();
        }

        private static void Detour_mono_gc_collect(int generation)
        {
            if (!_hasLoggedInterception)
            {
                _hasLoggedInterception = true;
                LogUtil.Log("BoehmGC: mono_gc_collect intercepted (gen " + generation + ").");
            }
            
            // Rate Limiting Logic
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastCollectTime < MinIntervalTicks)
            {
                 return;
            }
            _lastCollectTime = now;

            if (_originalDelegate != null)
                ((mono_gc_collect_Delegate)_originalDelegate)(generation);
        }
    }
}
