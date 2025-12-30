using System;
using System.Runtime.InteropServices;

namespace var_browser.Native
{
    public static class MinHook
    {
        public enum Status
        {
            MH_UNKNOWN = -1,
            MH_OK = 0,
            MH_ERROR_ALREADY_INITIALIZED,
            MH_ERROR_NOT_INITIALIZED,
            MH_ERROR_ALREADY_CREATED,
            MH_ERROR_NOT_CREATED,
            MH_ERROR_ENABLED,
            MH_ERROR_DISABLED,
            MH_ERROR_NOT_EXECUTABLE,
            MH_ERROR_UNSUPPORTED_FUNCTION,
            MH_ERROR_MEMORY_ALLOC,
            MH_ERROR_MEMORY_PROTECT,
            MH_ERROR_MODULE_NOT_FOUND,
            MH_ERROR_FUNCTION_NOT_FOUND
        }

        // Delegates
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate Status MH_Initialize_Delegate();
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate Status MH_Uninitialize_Delegate();
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate Status MH_CreateHook_Delegate(IntPtr pTarget, IntPtr pDetour, out IntPtr ppOriginal);
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate Status MH_EnableHook_Delegate(IntPtr pTarget);
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate Status MH_DisableHook_Delegate(IntPtr pTarget);
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate Status MH_QueueEnableHook_Delegate(IntPtr pTarget);
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate Status MH_ApplyQueued_Delegate();

        // Function Pointers
        private static MH_Initialize_Delegate _MH_Initialize;
        private static MH_Uninitialize_Delegate _MH_Uninitialize;
        private static MH_CreateHook_Delegate _MH_CreateHook;
        private static MH_EnableHook_Delegate _MH_EnableHook;
        private static MH_DisableHook_Delegate _MH_DisableHook;
        private static MH_QueueEnableHook_Delegate _MH_QueueEnableHook;
        private static MH_ApplyQueued_Delegate _MH_ApplyQueued;

        public static void Load(IntPtr hModule)
        {
            if (hModule == IntPtr.Zero) throw new ArgumentException("hModule cannot be zero");

            _MH_Initialize = LoadFunc<MH_Initialize_Delegate>(hModule, "MH_Initialize");
            _MH_Uninitialize = LoadFunc<MH_Uninitialize_Delegate>(hModule, "MH_Uninitialize");
            _MH_CreateHook = LoadFunc<MH_CreateHook_Delegate>(hModule, "MH_CreateHook");
            _MH_EnableHook = LoadFunc<MH_EnableHook_Delegate>(hModule, "MH_EnableHook");
            _MH_DisableHook = LoadFunc<MH_DisableHook_Delegate>(hModule, "MH_DisableHook");
            _MH_QueueEnableHook = LoadFunc<MH_QueueEnableHook_Delegate>(hModule, "MH_QueueEnableHook");
            _MH_ApplyQueued = LoadFunc<MH_ApplyQueued_Delegate>(hModule, "MH_ApplyQueued");
        }

        private static T LoadFunc<T>(IntPtr hModule, string name) where T : class
        {
            IntPtr addr = Kernel32.GetProcAddress(hModule, name);
            if (addr == IntPtr.Zero) throw new Exception("Could not find function: " + name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(addr, typeof(T));
        }

        // Static wrappers
        public static Status MH_Initialize() => _MH_Initialize();
        public static Status MH_Uninitialize() => _MH_Uninitialize();
        public static Status MH_CreateHook(IntPtr pTarget, IntPtr pDetour, out IntPtr ppOriginal) => _MH_CreateHook(pTarget, pDetour, out ppOriginal);
        public static Status MH_EnableHook(IntPtr pTarget) => _MH_EnableHook(pTarget);
        public static Status MH_DisableHook(IntPtr pTarget) => _MH_DisableHook(pTarget);
        public static Status MH_QueueEnableHook(IntPtr pTarget) => _MH_QueueEnableHook(pTarget);
        public static Status MH_ApplyQueued() => _MH_ApplyQueued();
    }
}
