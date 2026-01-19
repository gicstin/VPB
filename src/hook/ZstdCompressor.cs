using System;
using System.IO;
using ZstdNet;

namespace VPB
{
    public class ZstdCompressor
    {
        private static string _cachedZstdPath = null;
        private static bool _zstdChecked = false;

        private static string GetNativeZstdPath()
        {
            if (_zstdChecked) return _cachedZstdPath;
            _zstdChecked = true;
            try
            {
                string pluginDir = null;
                try { pluginDir = BepInEx.Paths.PluginPath; } catch {}
                
                string assemblyDir = null;
                try { 
                    string loc = typeof(ZstdCompressor).Assembly.Location;
                    if (!string.IsNullOrEmpty(loc)) assemblyDir = Path.GetDirectoryName(loc);
                } catch {}

                string bepRoot = null;
                try { bepRoot = BepInEx.Paths.BepInExRootPath; } catch {}
                string scriptDir = !string.IsNullOrEmpty(bepRoot) ? Path.Combine(bepRoot, "scripts") : null;

                System.Collections.Generic.List<string> searchDirs = new System.Collections.Generic.List<string>();
                
                // 1. Same directory as VPB.dll (Handles scripts folder or custom location)
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    searchDirs.Add(assemblyDir);
                    searchDirs.Add(Path.Combine(assemblyDir, "zstd"));
                }

                // 2. BepInEx scripts directory specific
                if (!string.IsNullOrEmpty(scriptDir))
                {
                    searchDirs.Add(scriptDir);
                    searchDirs.Add(Path.Combine(scriptDir, "zstd"));
                    searchDirs.Add(Path.Combine(scriptDir, "VPB\\zstd"));
                }

                // 3. BepInEx plugins root (preferred location for shared zstd)
                if (!string.IsNullOrEmpty(pluginDir))
                {
                    searchDirs.Add(pluginDir);
                    searchDirs.Add(Path.Combine(pluginDir, "zstd"));
                    searchDirs.Add(Path.Combine(pluginDir, "VPB\\zstd"));
                }

                foreach (string dir in searchDirs)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    
                    string path = Path.Combine(dir, "zstd.exe");
                    if (File.Exists(path)) { _cachedZstdPath = path; return path; }
                }
            }
            catch { }
            // Final fallback: try PATH
            _cachedZstdPath = "zstd.exe";
            return _cachedZstdPath;
        }

        private static bool RunZstd(string arguments, int timeoutMs = 30000)
        {
            string exePath = GetNativeZstdPath();
            if (string.IsNullOrEmpty(exePath)) return false;

            // If we don't have the full path, and it's not "zstd.exe", we can't be sure it exists
            if (exePath.Contains("\\") && !File.Exists(exePath)) return false;

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        bool exited = process.WaitForExit(timeoutMs);
                        if (exited && process.ExitCode == 0)
                        {
                            return true;
                        }
                        if (!exited)
                        {
                            try { process.Kill(); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] RunZstd failed: " + ex.Message);
            }
            return false;
        }

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

                if (RunZstd(string.Format("-{0} \"{1}\" -o \"{2}\" -f", level, tempIn, tempOut)))
                {
                    if (File.Exists(tempOut))
                    {
                        return File.ReadAllBytes(tempOut);
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

                if (RunZstd(string.Format("-{0} \"{1}\" -o \"{2}\" -f", level, tempIn, path)))
                {
                    return;
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

        /// <summary>
        /// Compresses an existing file on disk and saves it to the specified path.
        /// This is more memory-efficient than reading the file into a byte array first.
        /// </summary>
        public static void SaveCacheFromFile(string outputPath, string inputPath, int level)
        {
            if (!File.Exists(inputPath)) return;

            try
            {
                // If input and output are same, we need a temp file for output
                bool samePath = string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase);
                string actualOut = samePath ? outputPath + ".tmp_zstd" : outputPath;

                if (RunZstd(string.Format("-{0} \"{1}\" -o \"{2}\" -f", level, inputPath, actualOut)))
                {
                    if (samePath)
                    {
                        if (File.Exists(outputPath)) File.Delete(outputPath);
                        File.Move(actualOut, outputPath);
                    }
                    return;
                }

                // Fallback: if external fails, we have to do it internally (memory intensive)
                // We use CompressInternal directly here to avoid another RunZstd attempt inside SaveCache
                byte[] data = File.ReadAllBytes(inputPath);
                byte[] compressed = CompressInternal(data, level);
                File.WriteAllBytes(outputPath, compressed);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] SaveCacheFromFile failed: " + ex.Message);
            }
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
