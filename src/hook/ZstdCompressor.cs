using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ZstdNet;

namespace VPB
{
    public class ZstdCompressor
    {
        private const int MaxDecompressedBytes = 512 * 1024 * 1024;
        private static string _cachedZstdPath = null;
        private static bool _zstdChecked = false;
        private static readonly object _activeProcLock = new object();
        private static readonly List<Process> _activeProcs = new List<Process>(8);

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
                
                // 1. Standard installation: BepInEx/plugins/VPB/native/
                searchDirs.Add(VpbPaths.Native);

                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    searchDirs.Add(assemblyDir);
                    searchDirs.Add(Path.Combine(assemblyDir, "zstd"));
                }

                if (!string.IsNullOrEmpty(scriptDir))
                {
                    searchDirs.Add(scriptDir);
                    searchDirs.Add(Path.Combine(scriptDir, "zstd"));
                    searchDirs.Add(Path.Combine(scriptDir, "VPB\\zstd"));
                }

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
            _cachedZstdPath = "zstd.exe";
            return _cachedZstdPath;
        }

        public static void KillActiveProcesses()
        {
            Process[] snapshot;
            lock (_activeProcLock)
            {
                snapshot = _activeProcs.ToArray();
                _activeProcs.Clear();
            }
            for (int i = 0; i < snapshot.Length; i++)
            {
                Process p = snapshot[i];
                if (p == null) continue;
                try
                {
                    if (!p.HasExited) p.Kill();
                }
                catch { }
                try { p.Dispose(); } catch { }
            }
        }

        private static void RegisterActiveProcess(Process process)
        {
            if (process == null) return;
            try { VpbShutdown.Register("zstd-child-processes", KillActiveProcesses); } catch { }
            lock (_activeProcLock)
            {
                _activeProcs.Add(process);
            }
            if (VpbShutdown.IsQuitting) KillActiveProcesses();
        }

        private static void UnregisterActiveProcess(Process process)
        {
            if (process == null) return;
            lock (_activeProcLock)
            {
                _activeProcs.Remove(process);
            }
        }

        private static bool RunZstd(string arguments, int timeoutMs = 30000)
        {
            string exePath = GetNativeZstdPath();
            if (string.IsNullOrEmpty(exePath)) return false;

            // If we don't have the full path, and it's not "zstd.exe", we can't be sure it exists
            if (exePath.Contains("\\") && !File.Exists(exePath)) return false;

            Process process = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                process = Process.Start(startInfo);
                if (process == null) return false;

                RegisterActiveProcess(process);
                bool exited = process.WaitForExit(timeoutMs);
                if (exited && process.ExitCode == 0)
                    return true;
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] RunZstd failed: " + ex.Message);
            }
            finally
            {
                UnregisterActiveProcess(process);
                try { if (process != null) process.Dispose(); } catch { }
            }
            return false;
        }

        public static byte[] Compress(byte[] data, int level, int length = -1)
        {
            if (data == null || data.Length == 0)
                return new byte[0];

            if (length < 0) length = data.Length;
            if (length == 0) return new byte[0];

            try
            {
                return CompressInternal(data, level, length);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Zstd internal compress failed, trying external: " + ex.Message);
            }

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

        public static byte[] Decompress(byte[] compressed)
        {
            return Decompress(compressed, MaxDecompressedBytes);
        }

        public static byte[] Decompress(byte[] compressed, int maxDecompressedSize)
        {
            if (compressed == null || compressed.Length == 0)
                return new byte[0];
            if (maxDecompressedSize <= 0 || maxDecompressedSize > MaxDecompressedBytes)
                throw new ArgumentOutOfRangeException("maxDecompressedSize");

            try { ExternMethods.Initialize(); } catch { }

            using (var decompressor = new Decompressor())
            {
                return decompressor.Unwrap(compressed, maxDecompressedSize);
            }
        }

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

        public static void SaveCacheFromFile(string outputPath, string inputPath, int level)
        {
            if (!File.Exists(inputPath)) return;

            try
            {
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

                // Fallback: compress internally when external zstd fails.
                byte[] data = File.ReadAllBytes(inputPath);
                byte[] compressed = CompressInternal(data, level);
                File.WriteAllBytes(outputPath, compressed);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] SaveCacheFromFile failed: " + ex.Message);
            }
        }

        public static byte[] LoadCache(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] compressed = File.ReadAllBytes(path);

            return Decompress(compressed);
        }

        private static void EnsureInitialized()
        {
        }
    }
}
