using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using size_t = System.UIntPtr;
using BepInEx;

namespace ZstdNet
{
	internal static class ExternMethods
	{
		private static bool _initialized = false;

		static ExternMethods()
		{
            Initialize();
		}

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

			if(Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
				SetWinDllDirectory();
            }
        }

		private static void SetWinDllDirectory()
		{
            try
            {
                string dllName = "libzstd.dll";
                // Try to find where the DLL is
                string location = "";
                try { location = typeof(ExternMethods).Assembly.Location; } catch {}
                
                string assemblyDir = !string.IsNullOrEmpty(location) ? Path.GetDirectoryName(location) : null;
                
                string pluginDir = null;
                try { pluginDir = BepInEx.Paths.PluginPath; } catch {}
                
                string bepRoot = null;
                try { bepRoot = BepInEx.Paths.BepInExRootPath; } catch {}
                
                string scriptDir = !string.IsNullOrEmpty(bepRoot) ? Path.Combine(bepRoot, "scripts") : null;

                System.Collections.Generic.List<string> searchDirs = new System.Collections.Generic.List<string>();
                
                // 1. Target specific directory: BepInEx/plugins/VPB/zstd/dll/ (Standard Installation)
                if (!string.IsNullOrEmpty(pluginDir))
                {
                    searchDirs.Add(Path.Combine(pluginDir, "VPB\\zstd\\dll"));
                    searchDirs.Add(Path.Combine(pluginDir, "zstd\\dll"));
                }

                // 2. Same directory as VPB.dll (Handles scripts folder or custom location)
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    searchDirs.Add(assemblyDir);
                    searchDirs.Add(Path.Combine(assemblyDir, "zstd\\dll"));
                    searchDirs.Add(Path.Combine(assemblyDir, "x64"));
                }
                
                // 3. Scripts directory specific (if VPB.dll is in scripts, libzstd.dll might be in a subfolder there)
                if (!string.IsNullOrEmpty(scriptDir))
                {
                    searchDirs.Add(scriptDir);
                    searchDirs.Add(Path.Combine(scriptDir, "x64"));
                    searchDirs.Add(Path.Combine(scriptDir, "zstd\\dll"));
                    searchDirs.Add(Path.Combine(scriptDir, "VPB\\zstd\\dll"));
                }

                // 4. BepInEx plugins directory (fallback)
                if (!string.IsNullOrEmpty(pluginDir))
                {
                    searchDirs.Add(pluginDir);
                    searchDirs.Add(Path.Combine(pluginDir, "x64"));
                }

                // 4. One level up from plugins (BepInEx folder)
                if (!string.IsNullOrEmpty(pluginDir))
                {
                    string parent = Path.GetDirectoryName(pluginDir);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        searchDirs.Add(parent);
                        searchDirs.Add(Path.Combine(parent, "x64"));
                    }
                }

                string foundPath = null;
                string loadedVia = null;
                System.Text.StringBuilder failDetails = new System.Text.StringBuilder();

                foreach (string dir in searchDirs)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    
                    string fullPath = Path.Combine(dir, dllName);
                    if (File.Exists(fullPath))
                    {
                        foundPath = fullPath;
                        
                        // Set DLL directory so subsequent DllImports can find it
                        SetDllDirectory(dir);
                        
                        // Explicitly load it to ensure it's in memory
                        IntPtr handle = LoadLibrary(fullPath);
                        if (handle != IntPtr.Zero)
                        {
                            loadedVia = "LoadLibrary";
                            if (VPB.Settings.Instance != null && VPB.Settings.Instance.LogStartupDetails != null && VPB.Settings.Instance.LogStartupDetails.Value)
                            {
                                SafeLog(string.Format("[ZstdNet] libzstd.dll loaded OK | via: {0} | path: {1} | Assembly: {2}, Plugins: {3}, Scripts: {4}",
                                    loadedVia,
                                    foundPath ?? "null",
                                    assemblyDir ?? "null",
                                    pluginDir ?? "null",
                                    scriptDir ?? "null"));
                            }
                            return;
                        }
                        else
                        {
                            long err = Marshal.GetLastWin32Error();
                            failDetails.Append("[ZstdNet] LoadLibrary failed for ").Append(fullPath).Append(". Error: ").Append(err).AppendLine();
                        }
                    }
                }

                failDetails.Append("[ZstdNet] libzstd.dll not found in search paths. Falling back to default search.").AppendLine();
                failDetails.Append("[ZstdNet] Paths - Assembly: ").Append(assemblyDir ?? "null").Append(", Plugins: ").Append(pluginDir ?? "null").Append(", Scripts: ").Append(scriptDir ?? "null").AppendLine();
                failDetails.Append("[ZstdNet] SearchDirs:").AppendLine();
                foreach (string dir in searchDirs)
                {
                    failDetails.Append("[ZstdNet]  ").Append(dir ?? "null").AppendLine();
                }

                SafeLog(failDetails.ToString().TrimEnd('\r', '\n'));
            }
            catch (Exception ex)
            {
                SafeLog(string.Format("[ZstdNet] Exception in SetWinDllDirectory: {0}", ex.Message));
            }
		}

        private static void SafeLog(string message)
        {
            try
            {
                bool logged = false;
                try
                {
                    VPB.LogUtil.Log(message);
                    logged = true;
                }
                catch
                {
                }

                if (!logged)
                {
                    UnityEngine.Debug.Log(message);
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern bool SetDllDirectory(string path);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

		private const string DllName = "libzstd";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint ZSTD_getErrorCode(size_t code);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_compressCCtx(IntPtr ctx, IntPtr dst, size_t dstCapacity, IntPtr src, size_t srcSize, int compressionLevel);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_decompressDCtx(IntPtr ctx, IntPtr dst, size_t dstCapacity, IntPtr src, size_t srcSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr ZSTD_createCCtx();
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_freeCCtx(IntPtr cctx);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr ZSTD_createDCtx();
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_freeDCtx(IntPtr cctx);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr ZSTD_createCDict(byte[] dict, size_t dictSize, int compressionLevel);
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_freeCDict(IntPtr cdict);
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_compress_usingCDict(IntPtr cctx, IntPtr dst, size_t dstCapacity, IntPtr src, size_t srcSize, IntPtr cdict);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr ZSTD_createDDict(byte[] dict, size_t dictSize);
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_freeDDict(IntPtr ddict);
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_decompress_usingDDict(IntPtr dctx, IntPtr dst, size_t dstCapacity, IntPtr src, size_t srcSize, IntPtr ddict);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern ulong ZSTD_getFrameContentSize(IntPtr src, size_t srcSize);
        
		public const ulong ZSTD_CONTENTSIZE_UNKNOWN = unchecked(0UL - 1);
		public const ulong ZSTD_CONTENTSIZE_ERROR = unchecked(0UL - 2);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern size_t ZSTD_CCtx_refCDict(IntPtr cctx, IntPtr cdict);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern int ZSTD_maxCLevel();
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern int ZSTD_minCLevel();
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_compressBound(size_t srcSize);
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern uint ZSTD_isError(size_t code);
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr ZSTD_getErrorName(size_t code);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_CCtx_setParameter(IntPtr cctx, ZSTD_cParameter param, int value);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern size_t ZSTD_DCtx_setParameter(IntPtr dctx, ZSTD_dParameter param, int value);
        
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern ZSTD_bounds ZSTD_cParam_getBounds(ZSTD_cParameter cParam);
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern ZSTD_bounds ZSTD_dParam_getBounds(ZSTD_dParameter dParam);

		[StructLayout(LayoutKind.Sequential)]
		internal struct ZSTD_bounds
		{
			public size_t error;
			public int lowerBound;
			public int upperBound;
		}
	}

	public enum ZSTD_cParameter
	{
		ZSTD_c_compressionLevel = 100,
		ZSTD_c_windowLog = 101,
		ZSTD_c_hashLog = 102,
		ZSTD_c_chainLog = 103,
		ZSTD_c_searchLog = 104,
		ZSTD_c_minMatch = 105,
		ZSTD_c_targetLength = 106,
		ZSTD_c_strategy = 107,

		ZSTD_c_enableLongDistanceMatching = 160,
		ZSTD_c_ldmHashLog = 161,
		ZSTD_c_ldmMinMatch = 162,
		ZSTD_c_ldmBucketSizeLog = 163,
		ZSTD_c_ldmHashRateLog = 164,

		ZSTD_c_contentSizeFlag = 200,
		ZSTD_c_checksumFlag = 201,
		ZSTD_c_dictIDFlag = 202,

		ZSTD_c_nbWorkers = 400,
		ZSTD_c_jobSize = 401,
		ZSTD_c_overlapLog = 402
	}

	public enum ZSTD_dParameter
	{
		ZSTD_d_windowLogMax = 100
	}

	public enum ZSTD_ErrorCode
	{
		ZSTD_error_no_error = 0,
		ZSTD_error_GENERIC = 1,
		ZSTD_error_prefix_unknown = 10,
		ZSTD_error_version_unsupported = 12,
		ZSTD_error_frameParameter_unsupported = 14,
		ZSTD_error_frameParameter_windowTooLarge = 16,
		ZSTD_error_corruption_detected = 20,
		ZSTD_error_checksum_wrong = 22,
		ZSTD_error_dictionary_corrupted = 30,
		ZSTD_error_dictionary_wrong = 32,
		ZSTD_error_dictionaryCreation_failed = 34,
		ZSTD_error_parameter_unsupported = 40,
		ZSTD_error_parameter_outOfBound = 42,
		ZSTD_error_tableLog_tooLarge = 44,
		ZSTD_error_maxSymbolValue_tooLarge = 46,
		ZSTD_error_maxSymbolValue_tooSmall = 48,
		ZSTD_error_stage_wrong = 60,
		ZSTD_error_init_missing = 62,
		ZSTD_error_memory_allocation = 64,
		ZSTD_error_workSpace_tooSmall = 66,
		ZSTD_error_dstSize_tooSmall = 70,
		ZSTD_error_srcSize_wrong = 72,
		ZSTD_error_dstBuffer_null = 74
	}
}
