using System;
using System.IO;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class ZstdRoundTripTest
    {
        public static void RunFullTest()
        {
            try
            {
                string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Cache/Textures"));
                
                if (!Directory.Exists(baseDir))
                {
                    LogUtil.LogWarning("[VPB] Zstd Test: Cache\\Textures not found at " + baseDir);
                    baseDir = Path.Combine(Environment.CurrentDirectory, "Cache\\Textures");
                    if (!Directory.Exists(baseDir))
                    {
                        LogUtil.LogError("[VPB] Zstd Test: Cache directory not found.");
                        return;
                    }
                }

                string testDir = Path.Combine(baseDir, "zstd_test");
                Directory.CreateDirectory(testDir);

                string[] files = Directory.GetFiles(baseDir, "*.vamcache", SearchOption.TopDirectoryOnly);
                LogUtil.Log(string.Format("[VPB] Zstd Test: Found {0} files for testing", files.Length));

                int successCount = 0;
                int failCount = 0;
                int level = Settings.Instance.ZstdCompressionLevel.Value;

                foreach (var file in files)
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        byte[] originalData = File.ReadAllBytes(file);
                        
                        if (originalData.Length == 0) continue;

                        LogUtil.Log(string.Format("[VPB] Zstd Test: Processing {0} ({1} bytes)", name, originalData.Length));

                        // Compress
                        byte[] compressedData = ZstdCompressor.Compress(originalData, level);
                        float ratio = (float)compressedData.Length / originalData.Length * 100f;
                        LogUtil.Log(string.Format("[VPB] Zstd Test: Compressed to {0} bytes ({1:F2}%)", compressedData.Length, ratio));

                        // Decompress
                        byte[] decompressedData = ZstdCompressor.Decompress(compressedData);

                        // Verify
                        if (originalData.Length != decompressedData.Length)
                        {
                            LogUtil.LogError(string.Format("[VPB] Zstd Test: Length mismatch for {0}! Original={1}, Decompressed={2}", 
                                name, originalData.Length, decompressedData.Length));
                            failCount++;
                        }
                        else if (!ByteEquals(originalData, decompressedData))
                        {
                            LogUtil.LogError("[VPB] Zstd Test: Data content mismatch for " + name);
                            failCount++;
                        }
                        else
                        {
                            LogUtil.Log("[VPB] Zstd Test: Success for " + name);
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        LogUtil.LogError("[VPB] Zstd Test: Error processing " + file + ": " + ex.Message);
                    }
                    
                    if (successCount + failCount >= 10) break; // Limit test to 10 files
                }

                LogUtil.Log(string.Format("[VPB] Zstd Test: Completed. Success: {0}, Fail: {1}", successCount, failCount));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Zstd Test: Global failure: " + ex.Message);
            }
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
    }
}
