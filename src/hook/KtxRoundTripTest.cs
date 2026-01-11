using System;
using System.IO;
using System.Runtime.InteropServices;
using SimpleJSON;
using BepInEx;

namespace VPB
{
    public static class KtxRoundTripTest
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
        }

        public static void RunRoundTrip(string outputDir, DxtMipChain input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (input.data == null) throw new ArgumentException("input.data is null");
            if (input.mipOffsets == null || input.mipSizes == null) throw new ArgumentException("input mip arrays are null");
            if (input.mipOffsets.Length != input.mipCount || input.mipSizes.Length != input.mipCount)
                throw new ArgumentException("mip arrays do not match mipCount");

            Directory.CreateDirectory(outputDir);

            string ktxPath = Path.Combine(outputDir, "roundtrip.ktx2");
            string ktxMetaPath = ktxPath + ".meta";
            string outDxtPath = Path.Combine(outputDir, "roundtrip_out.dxt");
            string outMetaPath = outDxtPath + ".meta";

            var native = new NativeKtx();
            native.Initialize();

            LogUtil.Log("[VPB] KTX RoundTrip: writing " + ktxPath);
            native.WriteKtxFromDxt(ktxPath, input);
            WriteMeta(ktxMetaPath, input);

            LogUtil.Log("[VPB] KTX RoundTrip: reading " + ktxPath);
            var back = native.ReadDxtFromKtx(ktxPath);
            WriteMeta(outMetaPath, back);
            File.WriteAllBytes(outDxtPath, back.data);

            if (input.width != back.width || input.height != back.height || input.format != back.format || input.mipCount != back.mipCount)
                throw new Exception("RoundTrip meta mismatch");

            if (!ByteEquals(input.data, back.data))
                throw new Exception("RoundTrip data mismatch");

            for (int i = 0; i < input.mipCount; i++)
            {
                if (input.mipOffsets[i] != back.mipOffsets[i] || input.mipSizes[i] != back.mipSizes[i])
                    throw new Exception("RoundTrip mip table mismatch at level " + i);
            }

            LogUtil.Log("[VPB] KTX RoundTrip: success");
        }

        private static bool ByteEquals(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static void WriteMeta(string metaPath, DxtMipChain chain)
        {
            var root = new JSONClass();
            root["type"] = "dxt";
            root["width"].AsInt = chain.width;
            root["height"].AsInt = chain.height;
            root["format"] = chain.format.ToString();
            root["mipCount"].AsInt = chain.mipCount;

            var mips = new JSONArray();
            for (int i = 0; i < chain.mipCount; i++)
            {
                var m = new JSONClass();
                m["level"].AsInt = i;
                m["offset"].AsInt = chain.mipOffsets[i];
                m["size"].AsInt = chain.mipSizes[i];
                mips.Add(m);
            }
            root["mips"] = mips;

            File.WriteAllText(metaPath, root.ToString(string.Empty));
        }

        public static void RunFullTest()
        {
            try
            {
                // Application.dataPath is usually <VaM_Folder>/VaM_Data
                // Cache is usually in <VaM_Folder>/Cache
                string baseDir = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Cache/Textures"));
                
                if (!Directory.Exists(baseDir))
                {
                    LogUtil.LogWarning("[VPB] KTX Test: Cache\\Textures not found at " + baseDir);
                    // Fallback to current directory just in case
                    baseDir = Path.Combine(Environment.CurrentDirectory, "Cache\\Textures");
                    if (!Directory.Exists(baseDir))
                    {
                        LogUtil.LogError("[VPB] KTX Test: Cache directory not found anywhere.");
                        return;
                    }
                }

                string ktxDir = Path.Combine(baseDir, "ktx\\ktx_tex");
                string dxtDir = Path.Combine(baseDir, "ktx\\dxt_tex");

                Directory.CreateDirectory(ktxDir);
                Directory.CreateDirectory(dxtDir);

                var native = new NativeKtx();
                native.Initialize();

                // VaM Cache files use .vamcache extension
                string[] files = Directory.GetFiles(baseDir, "*.vamcache", SearchOption.TopDirectoryOnly);
                LogUtil.Log(string.Format("[VPB] KTX Test: Found {0} files in {1}", files.Length, baseDir));

                foreach (var file in files)
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        LogUtil.Log("[VPB] KTX Test: Processing " + name);

                        byte[] dxtData = File.ReadAllBytes(file);
                        // VPB Meta files use .vamcachemeta extension
                        string metaPath = file + "meta";
                        if (!File.Exists(metaPath))
                        {
                            LogUtil.LogWarning("[VPB] KTX Test: Missing meta for " + file);
                            continue;
                        }

                        var metaJson = JSON.Parse(File.ReadAllText(metaPath));
                        var input = new DxtMipChain();
                        input.width = metaJson["width"].AsInt;
                        input.height = metaJson["height"].AsInt;
                        input.mipCount = metaJson["mipCount"].AsInt;
                        input.data = dxtData;

                        string fmtStr = metaJson["format"].Value.ToUpper();
                        if (fmtStr == "DXT1") input.format = KtxTestFormat.Dxt1;
                        else if (fmtStr == "DXT5") input.format = KtxTestFormat.Dxt5;
                        else if (fmtStr == "RGB24") input.format = KtxTestFormat.Rgb24;
                        else if (fmtStr == "RGBA32") input.format = KtxTestFormat.Rgba32;
                        else
                        {
                            LogUtil.LogWarning(string.Format("[VPB] KTX Test: Skipping unknown format {0} for {1}", fmtStr, name));
                            continue;
                        }
                        
                        var mipsArray = metaJson["mips"].AsArray;
                        if (mipsArray != null && mipsArray.Count > 0)
                        {
                            input.mipCount = mipsArray.Count;
                            input.mipOffsets = new int[input.mipCount];
                            input.mipSizes = new int[input.mipCount];
                            for (int i = 0; i < input.mipCount; i++)
                            {
                                input.mipOffsets[i] = mipsArray[i]["offset"].AsInt;
                                input.mipSizes[i] = mipsArray[i]["size"].AsInt;
                            }
                        }
                        else
                        {
                            AutoDetectMips(input);
                        }

                        string ktxPath = Path.Combine(ktxDir, name + ".ktx2");
                        LogUtil.Log(string.Format("[VPB] KTX Test: {0} {1}x{2} mips={3} format={4}", name, input.width, input.height, input.mipCount, input.format));
                        native.WriteKtxFromDxt(ktxPath, input);

                        var back = native.ReadDxtFromKtx(ktxPath);
                        string outDxtPath = Path.Combine(dxtDir, name + "_back.dxt");
                        File.WriteAllBytes(outDxtPath, back.data);
                        
                        // Verification
                        if (input.width != back.width || input.height != back.height || input.format != back.format || input.mipCount != back.mipCount)
                            LogUtil.LogError("[VPB] KTX Test: Meta mismatch for " + name);
                        else if (input.data.Length != back.data.Length)
                            LogUtil.LogError("[VPB] KTX Test: Data length mismatch for " + name);
                        else
                            LogUtil.Log("[VPB] KTX Test: Success for " + name);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] KTX Test: Failed file " + file + ": " + ex.Message);
                    }
                }
                LogUtil.Log("[VPB] KTX Test: Completed");
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] KTX Test: Global failure: " + ex.Message);
            }
        }

        private static void AutoDetectMips(DxtMipChain input)
        {
            int blockSize = GetBlockSize(input.format);
            bool isCompressed = input.format == KtxTestFormat.Dxt1 || input.format == KtxTestFormat.Dxt5;

            int w = input.width;
            int h = input.height;
            int totalExpectedSize = 0;
            int count = 0;

            // First, calculate how many levels we would have if it was a full chain
            int tempW = w, tempH = h;
            while (true)
            {
                int levelSize;
                if (isCompressed)
                    levelSize = Math.Max(1, (tempW + 3) / 4) * Math.Max(1, (tempH + 3) / 4) * blockSize;
                else
                    levelSize = tempW * tempH * blockSize;

                totalExpectedSize += levelSize;
                count++;
                if (tempW == 1 && tempH == 1) break;
                if (tempW > 1) tempW >>= 1;
                if (tempH > 1) tempH >>= 1;
            }

            if (input.data.Length == totalExpectedSize)
            {
                input.mipCount = count;
                input.mipOffsets = new int[count];
                input.mipSizes = new int[count];
                int offset = 0;
                tempW = w; tempH = h;
                for (int i = 0; i < count; i++)
                {
                    int levelSize;
                    if (isCompressed)
                        levelSize = Math.Max(1, (tempW + 3) / 4) * Math.Max(1, (tempH + 3) / 4) * blockSize;
                    else
                        levelSize = tempW * tempH * blockSize;

                    input.mipOffsets[i] = offset;
                    input.mipSizes[i] = levelSize;
                    offset += levelSize;

                    if (tempW > 1) tempW >>= 1;
                    if (tempH > 1) tempH >>= 1;
                }
            }
            else
            {
                int firstMipSize;
                if (isCompressed)
                    firstMipSize = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockSize;
                else
                    firstMipSize = w * h * blockSize;

                input.mipCount = 1;
                input.mipOffsets = new int[] { 0 };
                input.mipSizes = new int[] { Math.Min(input.data.Length, firstMipSize) };
            }
        }

        private static int GetBlockSize(KtxTestFormat format)
        {
            switch (format)
            {
                case KtxTestFormat.Dxt1: return 8;
                case KtxTestFormat.Dxt5: return 16;
                case KtxTestFormat.Rgb24: return 3;
                case KtxTestFormat.Rgba32: return 4;
                default: return 8;
            }
        }

        internal sealed class NativeKtx
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
                int dataSize);

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
                out int outDataSize);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void vpb_ktx_free_Delegate(IntPtr p);

            private IntPtr _hModule = IntPtr.Zero;
            private vpb_ktx_write_from_dxt_Delegate _write;
            private vpb_ktx_read_to_dxt_Delegate _read;
            private vpb_ktx_free_Delegate _free;
            private bool _initialized;

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
                            LogUtil.Log("[VPB] KTX RoundTrip: Loaded dependency " + libKtxFoundPath);
                        else
                            LogUtil.LogWarning("[VPB] KTX RoundTrip: Failed to LoadLibrary dependency " + libKtxFoundPath);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning("[VPB] KTX RoundTrip: Exception loading dependency " + libKtxFoundPath + " ex=" + ex.Message);
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
                            "Missing native wrapper DLL: " + WrapperDllName + ". Found libktx at: " + libKtxFoundPath + ". " +
                            "ktx.dll (libktx) alone does not export vpb_ktx_* functions. You still need a small wrapper DLL that exports: " +
                            "vpb_ktx_write_from_dxt, vpb_ktx_read_to_dxt, vpb_ktx_free.");
                    }

                    throw new FileNotFoundException(
                        "Missing native wrapper DLL: " + WrapperDllName + " (searched BepInEx plugins + assembly folder + 'ktx' subfolder). " +
                        "Also did not find libktx dependency '" + LibKtxDllName + "'.");
                }

                _hModule = VPB.Native.Kernel32.LoadLibrary(dllPath);
                if (_hModule == IntPtr.Zero)
                    throw new Exception("LoadLibrary failed for " + dllPath);

                _write = LoadFunc<vpb_ktx_write_from_dxt_Delegate>(_hModule, "vpb_ktx_write_from_dxt");
                _read = LoadFunc<vpb_ktx_read_to_dxt_Delegate>(_hModule, "vpb_ktx_read_to_dxt");
                _free = LoadFunc<vpb_ktx_free_Delegate>(_hModule, "vpb_ktx_free");

                _initialized = true;
            }

            public void WriteKtxFromDxt(string ktxPath, DxtMipChain chain)
            {
                if (!_initialized) throw new Exception("NativeKtx not initialized");

                GCHandle hOffsets = default(GCHandle);
                GCHandle hSizes = default(GCHandle);
                GCHandle hData = default(GCHandle);

                try
                {
                    hOffsets = GCHandle.Alloc(chain.mipOffsets, GCHandleType.Pinned);
                    hSizes = GCHandle.Alloc(chain.mipSizes, GCHandleType.Pinned);
                    hData = GCHandle.Alloc(chain.data, GCHandleType.Pinned);

                    int rc = _write(
                        ktxPath,
                        (int)chain.format,
                        chain.width,
                        chain.height,
                        chain.mipCount,
                        hOffsets.AddrOfPinnedObject(),
                        hSizes.AddrOfPinnedObject(),
                        hData.AddrOfPinnedObject(),
                        chain.data.Length);

                    if (rc != 0)
                    {
                        string msg = "vpb_ktx_write_from_dxt failed rc=" + rc;
                        if (rc <= -2000 && rc > -3000) msg += " (KTX_ERROR=" + (-(rc + 2000)) + ")";
                        else if (rc <= -3000 && rc > -4000) msg += " (GetImageOffset error=" + (-(rc + 3000)) + ")";
                        else if (rc <= -4000 && rc > -5000) msg += " (WriteToNamedFile error=" + (-(rc + 4000)) + ")";
                        else if (rc == -1) msg += " (LoadLibrary failed)";
                        else if (rc == -101) msg += " (ktxTexture2_Create not found)";
                        else if (rc == -102) msg += " (ktxTexture2_CreateFromNamedFile not found)";
                        else if (rc == -103) msg += " (ktxTexture2_Destroy not found)";
                        else if (rc == -104) msg += " (ktxTexture_GetData not found)";
                        else if (rc == -105) msg += " (ktxTexture2_GetImageOffset not found)";
                        else if (rc == -106) msg += " (ktxTexture2_WriteToNamedFile not found)";
                        else if (rc == -107) msg += " (ktxTexture_calcImageSize not found)";
                        throw new Exception(msg);
                    }
                }
                finally
                {
                    if (hOffsets.IsAllocated) hOffsets.Free();
                    if (hSizes.IsAllocated) hSizes.Free();
                    if (hData.IsAllocated) hData.Free();
                }
            }

            public DxtMipChain ReadDxtFromKtx(string ktxPath)
            {
                if (!_initialized) throw new Exception("NativeKtx not initialized");

                int fmt;
                int w;
                int h;
                int mipCount;
                IntPtr pOffsets;
                IntPtr pSizes;
                IntPtr pData;
                int dataSize;

                int rc = _read(ktxPath, out fmt, out w, out h, out mipCount, out pOffsets, out pSizes, out pData, out dataSize);
                if (rc != 0)
                {
                    string msg = "vpb_ktx_read_to_dxt failed rc=" + rc;
                    if (rc <= -2000 && rc > -3000) msg += " (KTX_ERROR=" + (-(rc + 2000)) + ")";
                    else if (rc == -1) msg += " (LoadLibrary failed)";
                    // reuse same messages as write for common errors
                    throw new Exception(msg);
                }

                if (pOffsets == IntPtr.Zero || pSizes == IntPtr.Zero || pData == IntPtr.Zero)
                    throw new Exception("vpb_ktx_read_to_dxt returned null pointers");

                var chain = new DxtMipChain();
                chain.width = w;
                chain.height = h;
                chain.format = (KtxTestFormat)fmt;
                chain.mipCount = mipCount;

                try
                {
                    chain.mipOffsets = new int[mipCount];
                    chain.mipSizes = new int[mipCount];

                    Marshal.Copy(pOffsets, chain.mipOffsets, 0, mipCount);
                    Marshal.Copy(pSizes, chain.mipSizes, 0, mipCount);

                    chain.data = new byte[dataSize];
                    Marshal.Copy(pData, chain.data, 0, dataSize);

                    return chain;
                }
                finally
                {
                    try { _free(pOffsets); } catch { }
                    try { _free(pSizes); } catch { }
                    try { _free(pData); } catch { }
                }
            }

            private static T LoadFunc<T>(IntPtr hModule, string name) where T : class
            {
                IntPtr addr = VPB.Native.Kernel32.GetProcAddress(hModule, name);
                if (addr == IntPtr.Zero)
                    throw new Exception("Could not find function: " + name);
                return (T)(object)Marshal.GetDelegateForFunctionPointer(addr, typeof(T));
            }
        }
    }
}
