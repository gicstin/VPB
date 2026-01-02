using System;
using System.Runtime.InteropServices;
using System.Globalization;

namespace VPB.Native
{
    public static class SigScanner
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MODULEINFO
        {
            public IntPtr lpBaseOfDll;
            public uint SizeOfImage;
            public IntPtr EntryPoint;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, uint cb);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetCurrentProcess();

        public static IntPtr Scan(IntPtr moduleHandle, string signature)
        {
            if (moduleHandle == IntPtr.Zero) return IntPtr.Zero;

            // Parse signature
            string[] tokens = signature.Split(' ');
            byte?[] pattern = new byte?[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "?" || tokens[i] == "??")
                {
                    pattern[i] = null;
                }
                else
                {
                    pattern[i] = byte.Parse(tokens[i], NumberStyles.HexNumber);
                }
            }

            // Get module info
            MODULEINFO moduleInfo;
            if (!GetModuleInformation(GetCurrentProcess(), moduleHandle, out moduleInfo, (uint)Marshal.SizeOf(typeof(MODULEINFO))))
            {
                return IntPtr.Zero;
            }

            long start = moduleInfo.lpBaseOfDll.ToInt64();
            long size = moduleInfo.SizeOfImage;
            long end = start + size;

            // Naive scan (slow but works)
            // Ideally we'd buffer this, but reading memory directly in-process is fast enough for one-time init.
            unsafe
            {
                byte* pStart = (byte*)start;
                byte* pEnd = (byte*)end - pattern.Length;

                for (byte* p = pStart; p < pEnd; p++)
                {
                    bool match = true;
                    for (int i = 0; i < pattern.Length; i++)
                    {
                        if (pattern[i].HasValue && pattern[i].Value != p[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        return new IntPtr(p);
                    }
                }
            }

            return IntPtr.Zero;
        }
    }
}
