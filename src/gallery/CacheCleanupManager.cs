using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace VPB
{
    internal static class CacheCleanupManager
    {
        private static bool s_Scanning = false;
        private static List<VpbLocalDatabase.CacheUsageRow> s_StaleItems = new List<VpbLocalDatabase.CacheUsageRow>();
        private static readonly HashSet<string> s_HitBuffer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_Lock = new object();
        private static float s_LastQueueTime = 0f;
        private static bool s_FlushPending = false;

        public static bool IsScanning => s_Scanning;

        public static void QueueHit(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (s_HitBuffer)
            {
                s_HitBuffer.Add(path);
                s_LastQueueTime = Time.realtimeSinceStartup;
                s_FlushPending = true;
            }
        }

        public static void CheckAutoFlush()
        {
            if (!s_FlushPending) return;
            
            // Auto flush if idle for 3 seconds or buffer is getting large (500 items)
            bool shouldFlush = false;
            lock (s_HitBuffer)
            {
                if (s_HitBuffer.Count > 0)
                {
                    if (s_HitBuffer.Count >= 500 || (Time.realtimeSinceStartup - s_LastQueueTime) > 3f)
                    {
                        shouldFlush = true;
                    }
                }
                else
                {
                    s_FlushPending = false;
                }
            }

            if (shouldFlush)
            {
                FlushHitsBatch();
            }
        }

        public static void FlushHitsBatch()
        {
            string[] toFlush;
            lock (s_HitBuffer)
            {
                if (s_HitBuffer.Count == 0)
                {
                    s_FlushPending = false;
                    return;
                }
                toFlush = new string[s_HitBuffer.Count];
                s_HitBuffer.CopyTo(toFlush);
                s_HitBuffer.Clear();
                s_FlushPending = false;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    VpbLocalDatabase.TryRecordCacheHitsBatch(toFlush);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Failed to flush cache hits batch: " + ex.Message);
                }
            });
        }

        public static void StartScan(int daysOlderThan, int maxHits)
        {
            if (s_Scanning) return;
            s_Scanning = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    long olderThanBinary = DateTime.UtcNow.AddDays(-daysOlderThan).ToBinary();
                    var results = new List<VpbLocalDatabase.CacheUsageRow>();
                    VpbLocalDatabase.TryGetStaleCacheItems(olderThanBinary, maxHits, results);

                    // Verify files still exist on disk
                    var verified = new List<VpbLocalDatabase.CacheUsageRow>();
                    foreach (var item in results)
                    {
                        if (File.Exists(item.CachePath))
                        {
                            verified.Add(item);
                        }
                        else
                        {
                            // Clean up DB if file is already gone
                            VpbLocalDatabase.TryDeleteCacheUsage(item.CachePath);
                        }
                    }

                    lock (s_Lock)
                    {
                        s_StaleItems = verified;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] CacheCleanupManager scan failed: " + ex.Message);
                }
                finally
                {
                    s_Scanning = false;
                }
            });
        }

        public static List<VpbLocalDatabase.CacheUsageRow> GetResults()
        {
            lock (s_Lock)
            {
                return new List<VpbLocalDatabase.CacheUsageRow>(s_StaleItems);
            }
        }

        public static void DeleteItems(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int deleted = 0;
                foreach (var path in paths)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            // Also delete meta file
                            string meta = path + "meta";
                            if (File.Exists(meta)) File.Delete(meta);
                            
                            VpbLocalDatabase.TryDeleteCacheUsage(path);
                            deleted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Failed to delete cache item " + path + ": " + ex.Message);
                    }
                }
                
                if (deleted > 0)
                {
                    LogUtil.Log("[VPB] Smart Cleanup: Deleted " + deleted + " stale cache items.");
                    // Refresh results
                    lock (s_Lock)
                    {
                        s_StaleItems.RemoveAll(item => paths.Contains(item.CachePath));
                    }
                }
            });
        }
    }
}
