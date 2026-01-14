using System;
using System.Runtime.InteropServices;
using size_t = System.UIntPtr;

namespace ZstdNet
{
    internal static class ReturnValueExtensions
    {
        public static void EnsureZstdSuccess(this size_t code)
        {
            if (ExternMethods.ZSTD_isError(code) != 0)
            {
                var ptr = ExternMethods.ZSTD_getErrorName(code);
                var msg = Marshal.PtrToStringAnsi(ptr);
                var errorCode = (ZSTD_ErrorCode)ExternMethods.ZSTD_getErrorCode(code);
                throw new ZstdException(errorCode, msg);
            }
        }

        public static IntPtr EnsureZstdSuccess(this IntPtr self)
        {
            if (self == IntPtr.Zero) throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_memory_allocation, "Allocation failed");
            return self;
        }
    }
}
