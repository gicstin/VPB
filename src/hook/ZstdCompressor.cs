using System;
using System.IO;
using ZstdNet;

namespace VPB
{
    public class ZstdCompressor
    {
        /// <summary>
        /// Compresses the given data using Zstd compression.
        /// </summary>
        /// <param name="data">The raw data to compress.</param>
        /// <param name="level">The compression level (typically 1-22).</param>
        /// <param name="length">Optional length of data to compress (for pooled buffers).</param>
        /// <returns>The compressed byte array.</returns>
        public static byte[] Compress(byte[] data, int level, int length = -1)
        {
            if (data == null || data.Length == 0)
                return new byte[0];

            if (length < 0) length = data.Length;
            if (length == 0) return new byte[0];

            return CompressExternal(data, level, length);
        }

        private static byte[] CompressInternal(byte[] data, int level, int length = -1)
        {
            if (length < 0) length = data.Length;

            // Explicitly initialize to ensure DLL is found/loaded before CompressionOptions static ctor runs
            try { ExternMethods.Initialize(); } catch { }

            using (var compressor = new Compressor(new CompressionOptions(level)))
            {
                return compressor.Wrap(data, 0, length);
            }
        }

        /// <summary>
        /// Compresses the given data using an external Zstd process to save memory in the main process.
        /// </summary>
        public static byte[] CompressExternal(byte[] data, int level, int length = -1)
        {
            if (data == null || data.Length == 0)
                return new byte[0];

            if (length < 0) length = data.Length;
            if (length == 0) return new byte[0];

            string tempIn = Path.GetTempFileName();
            string tempOut = Path.GetTempFileName();
            try
            {
                // Write only the specified length to avoid compressing trailing garbage in pooled buffers
                using (var fs = new FileStream(tempIn, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(data, 0, length);
                }

                string exePath = GetNativeZstdPath();

                if (File.Exists(exePath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = string.Format("-{0} \"{1}\" -o \"{2}\" -f", level, tempIn, tempOut),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };

                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit(30000); // 30s timeout
                            if (process.HasExited && process.ExitCode == 0)
                            {
                                if (File.Exists(tempOut))
                                {
                                    return File.ReadAllBytes(tempOut);
                                }
                            }
                        }
                    }
                }

                // Fallback to internal
                return CompressInternal(data, level, length);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] CompressExternal failed: " + ex.Message);
                return CompressInternal(data, level, length);
            }
            finally
            {
                try { if (File.Exists(tempIn)) File.Delete(tempIn); } catch { }
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            }
        }

        /// <summary>
        /// Decompresses the given Zstd-compressed data.
        /// </summary>
        /// <param name="compressed">The compressed data.</param>
        /// <returns>The original uncompressed byte array.</returns>
        public static byte[] Decompress(byte[] compressed)
        {
            if (compressed == null || compressed.Length == 0)
                return new byte[0];

            try { ExternMethods.Initialize(); } catch { }

            using (var decompressor = new Decompressor())
            {
                return decompressor.Unwrap(compressed);
            }
        }

        /// <summary>
        /// Compresses the provided data (e.g., DXT texture data) and saves it to the specified file path.
        /// </summary>
        /// <param name="path">The full path where the cache file will be saved.</param>
        /// <param name="data">The raw data to compress and save.</param>
        /// <param name="level">The compression level to use.</param>
        /// <param name="length">Optional length of data to compress (for pooled buffers).</param>
        public static void SaveCache(string path, byte[] data, int level, int length = -1)
        {
            if (data == null)
            {
                File.WriteAllBytes(path, new byte[0]);
                return;
            }

            if (length < 0) length = data.Length;
            if (length == 0)
            {
                File.WriteAllBytes(path, new byte[0]);
                return;
            }

            string tempIn = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempIn, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(data, 0, length);
                }

                string exePath = GetNativeZstdPath();

                if (File.Exists(exePath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = string.Format("-{0} \"{1}\" -o \"{2}\" -f", level, tempIn, path),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };

                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit(30000);
                            if (process.HasExited && process.ExitCode == 0)
                            {
                                return;
                            }
                        }
                    }
                }

                // Fallback to internal
                byte[] compressed = CompressInternal(data, level, length);
                File.WriteAllBytes(path, compressed);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] SaveCache external failed: " + ex.Message);
                byte[] compressed = CompressInternal(data, level, length);
                File.WriteAllBytes(path, compressed);
            }
            finally
            {
                try { if (File.Exists(tempIn)) File.Delete(tempIn); } catch { }
            }
        }

        private static string GetNativeZstdPath()
        {
            try
            {
                // %vam%\BepInEx\plugins\zstd\zstd.exe
                string pluginDir = BepInEx.Paths.PluginPath;
                if (!string.IsNullOrEmpty(pluginDir))
                {
                    string path = Path.Combine(pluginDir, "zstd\\zstd.exe");
                    if (File.Exists(path)) return path;
                }

                // Fallback: check if it's in the same dir as VPB.dll or one level up
                string assemblyDir = Path.GetDirectoryName(typeof(ZstdCompressor).Assembly.Location);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    string path = Path.Combine(assemblyDir, "zstd.exe");
                    if (File.Exists(path)) return path;

                    path = Path.Combine(assemblyDir, "zstd\\zstd.exe");
                    if (File.Exists(path)) return path;
                }
            }
            catch { }
            return "zstd.exe"; // Hope it's in PATH
        }

        /// <summary>
        /// Reads a compressed cache file from disk and decompresses it back to raw data (e.g., DXT texture).
        /// </summary>
        /// <param name="path">The full path to the cache file.</param>
        /// <returns>The decompressed data, or null if the file does not exist.</returns>
        public static byte[] LoadCache(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Read compressed bytes
            byte[] compressed = File.ReadAllBytes(path);

            // Decompress
            return Decompress(compressed);
        }

        /// <summary>
        /// Helper to ensure the compressor is initialized. With ZstdNet, this is handled by the library,
        /// but keeping the method signature for compatibility if needed by other callers (though existing callers seem to access static methods directly).
        /// </summary>
        private static void EnsureInitialized()
        {
            // No-op for ZstdNet implementation as it initializes on demand/static ctor.
        }
    }
}
