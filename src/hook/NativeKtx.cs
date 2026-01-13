using System;
using System.IO;
using System.Runtime.InteropServices;
using SimpleJSON;
using BepInEx;

namespace VPB
{
    public enum KtxTestFormat : int
    {
        Dxt1 = 1,
        Dxt5 = 2,
        Rgb24 = 3,
        Rgba32 = 4
    }

    public class DxtMipChain
    {
        public int width;
        public int height;
        public KtxTestFormat format;
        public int mipCount;
        public byte[] data;
        public int[] mipOffsets;
        public int[] mipSizes;
        public int dataSize;
        public bool isSRGB;
        public bool isNormalMap;
        public bool useZstd;
    }

    public sealed class NativeKtx
    {
        private const string WrapperDllName = "VPBKtx.x64.dll";
        private const string LibKtxDllName = "ktx.dll";
        private const string LibKtxFolderName = "KTX";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int vpb_ktx_write_from_dxt_Delegate(
            [MarshalAs(UnmanagedType.LPStr)] string ktxPath,
            int format,
            int width,
            int height,
            int mipCount,
            IntPtr mipOffsets,
            IntPtr mipSizes,
            IntPtr data,
            int dataSize,
            int isSRGB,
            int isNormalMap,
            int useZstd);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int vpb_ktx_read_to_dxt_Delegate(
            [MarshalAs(UnmanagedType.LPStr)] string ktxPath,
            out int outFormat,
            out int outWidth,
            out int outHeight,
            out int outMipCount,
            out IntPtr outMipOffsets,
            out IntPtr outMipSizes,
            out IntPtr outData,
            out int outDataSize,
            out int outIsSRGB);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void vpb_ktx_free_Delegate(IntPtr p);

        private IntPtr _hModule = IntPtr.Zero;
        private vpb_ktx_write_from_dxt_Delegate _write;
        private vpb_ktx_read_to_dxt_Delegate _read;
        private vpb_ktx_free_Delegate _free;
        private bool _initialized;

        public static bool CheckDlls()
        {
            try
            {
                string pluginPath = Paths.PluginPath;
                string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                bool libKtxFound = false;
                foreach (var candidate in new[]
                {
                    Path.Combine(Path.Combine(pluginPath, LibKtxFolderName), LibKtxDllName),
                    Path.Combine(pluginPath, LibKtxDllName),
                    Path.Combine(Path.Combine(assemblyDir, LibKtxFolderName), LibKtxDllName),
                    Path.Combine(assemblyDir, LibKtxDllName),
                })
                {
                    if (File.Exists(candidate)) { libKtxFound = true; break; }
                }

                if (!libKtxFound) return false;

                bool wrapperFound = false;
                foreach (var candidate in new[]
                {
                    Path.Combine(pluginPath, WrapperDllName),
                    Path.Combine(Path.Combine(pluginPath, LibKtxFolderName), WrapperDllName),
                    Path.Combine(assemblyDir, WrapperDllName),
                    Path.Combine(Path.Combine(assemblyDir, LibKtxFolderName), WrapperDllName),
                })
                {
                    if (File.Exists(candidate)) { wrapperFound = true; break; }
                }

                return wrapperFound;
            }
            catch { return false; }
        }

        public void Initialize()
        {
            if (_initialized) return;

            string pluginPath = Paths.PluginPath;
            string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            string libKtxFoundPath = null;
            foreach (var candidate in new[]
            {
                Path.Combine(Path.Combine(pluginPath, LibKtxFolderName), LibKtxDllName),
                Path.Combine(pluginPath, LibKtxDllName),
                Path.Combine(Path.Combine(assemblyDir, LibKtxFolderName), LibKtxDllName),
                Path.Combine(assemblyDir, LibKtxDllName),
            })
            {
                if (File.Exists(candidate))
                {
                    libKtxFoundPath = candidate;
                    break;
                }
            }

            if (libKtxFoundPath != null)
            {
                try
                {
                    var hLib = VPB.Native.Kernel32.LoadLibrary(libKtxFoundPath);
                    if (hLib != IntPtr.Zero)
                        LogUtil.Log("[VPB] KTX: Loaded dependency " + libKtxFoundPath);
                    else
                        LogUtil.LogWarning("[VPB] KTX: Failed to LoadLibrary dependency " + libKtxFoundPath);
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB] KTX: Exception loading dependency " + libKtxFoundPath + " ex=" + ex.Message);
                }
            }

            string dllPath = null;
            foreach (var candidate in new[]
            {
                Path.Combine(pluginPath, WrapperDllName),
                Path.Combine(Path.Combine(pluginPath, LibKtxFolderName), WrapperDllName),
                Path.Combine(assemblyDir, WrapperDllName),
                Path.Combine(Path.Combine(assemblyDir, LibKtxFolderName), WrapperDllName),
            })
            {
                if (File.Exists(candidate))
                {
                    dllPath = candidate;
                    break;
                }
            }

            if (dllPath == null)
            {
                if (libKtxFoundPath != null)
                {
                    throw new FileNotFoundException(
                        "Missing native wrapper DLL: " + WrapperDllName + ". Found libktx at: " + libKtxFoundPath);
                }
                throw new FileNotFoundException("Missing native KTX DLLs");
            }

            _hModule = VPB.Native.Kernel32.LoadLibrary(dllPath);
            if (_hModule == IntPtr.Zero)
                throw new Exception("Failed to load " + dllPath);

            IntPtr pWrite = VPB.Native.Kernel32.GetProcAddress(_hModule, "vpb_ktx_write_from_dxt");
            IntPtr pRead = VPB.Native.Kernel32.GetProcAddress(_hModule, "vpb_ktx_read_to_dxt");
            IntPtr pFree = VPB.Native.Kernel32.GetProcAddress(_hModule, "vpb_ktx_free");

            if (pWrite == IntPtr.Zero || pRead == IntPtr.Zero || pFree == IntPtr.Zero)
                throw new Exception("Failed to find entry points in " + dllPath);

            _write = (vpb_ktx_write_from_dxt_Delegate)Marshal.GetDelegateForFunctionPointer(pWrite, typeof(vpb_ktx_write_from_dxt_Delegate));
            _read = (vpb_ktx_read_to_dxt_Delegate)Marshal.GetDelegateForFunctionPointer(pRead, typeof(vpb_ktx_read_to_dxt_Delegate));
            _free = (vpb_ktx_free_Delegate)Marshal.GetDelegateForFunctionPointer(pFree, typeof(vpb_ktx_free_Delegate));

            _initialized = true;
            LogUtil.Log("[VPB] KTX: Initialized successfully from " + dllPath);
        }

        public void WriteKtxFromDxt(string ktxPath, DxtMipChain input)
        {
            if (!_initialized) Initialize();

            // Ensure directory exists
            try
            {
                var dir = Path.GetDirectoryName(ktxPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] KTX: Failed to check/create directory for " + ktxPath + ": " + ex.Message);
            }

            // Sanitize path for cross-platform compatibility
            ktxPath = ktxPath.Replace('\\', '/');

            GCHandle hOffsets = GCHandle.Alloc(input.mipOffsets, GCHandleType.Pinned);
            GCHandle hSizes = GCHandle.Alloc(input.mipSizes, GCHandleType.Pinned);
            GCHandle hData = GCHandle.Alloc(input.data, GCHandleType.Pinned);

            try
            {
                int ret = _write(
                    ktxPath,
                    (int)input.format,
                    input.width,
                    input.height,
                    input.mipCount,
                    hOffsets.AddrOfPinnedObject(),
                    hSizes.AddrOfPinnedObject(),
                    hData.AddrOfPinnedObject(),
                    input.dataSize > 0 ? input.dataSize : input.data.Length,
                    input.isSRGB ? 1 : 0,
                    input.isNormalMap ? 1 : 0,
                    input.useZstd ? 1 : 0);

                if (ret != 0)
                    throw new Exception("vpb_ktx_write_from_dxt returned " + ret);
            }
            finally
            {
                hOffsets.Free();
                hSizes.Free();
                hData.Free();
            }
        }

        public DxtMipChain ReadDxtFromKtx(string ktxPath)
        {
            if (!_initialized) Initialize();

            ktxPath = ktxPath.Replace('\\', '/');

            int fmt, w, h, mips;
            IntPtr pOffsets, pSizes, pData;
            int dataSize, isSRGB;

            int ret = _read(ktxPath, out fmt, out w, out h, out mips, out pOffsets, out pSizes, out pData, out dataSize, out isSRGB);
            if (ret != 0)
                throw new Exception("vpb_ktx_read_to_dxt returned " + ret);

            try
            {
                var chain = new DxtMipChain();
                chain.width = w;
                chain.height = h;
                chain.format = (KtxTestFormat)fmt;
                chain.mipCount = mips;
                chain.isSRGB = isSRGB != 0;

                chain.mipOffsets = new int[mips];
                Marshal.Copy(pOffsets, chain.mipOffsets, 0, mips);

                chain.mipSizes = new int[mips];
                Marshal.Copy(pSizes, chain.mipSizes, 0, mips);

                chain.data = new byte[dataSize];
                Marshal.Copy(pData, chain.data, 0, dataSize);

                return chain;
            }
            finally
            {
                if (pOffsets != IntPtr.Zero) _free(pOffsets);
                if (pSizes != IntPtr.Zero) _free(pSizes);
                if (pData != IntPtr.Zero) _free(pData);
            }
        }
    }
}
