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
        private static readonly Dictionary<string, string> s_HitPkgUidByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_Lock = new object();
        private static double s_LastQueueTime;
        private static bool s_FlushActive;
        private static bool s_FlushRequested;
        private static double s_RetryAfter;
        private static string[] s_RetryPaths;
        private static List<VpbLocalDatabase.CacheUsagePkgRow> s_RetryPkgRows;

        private static double FlushClockSeconds()
        {
            return (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        }
        private static volatile bool s_FlushPending = false;

        public static bool IsScanning => s_Scanning;

        private static bool TryExtractPackageUidFromImagePath(string imgPathOrUidPath, out string uid)
        {
            uid = null;
            if (string.IsNullOrEmpty(imgPathOrUidPath)) return false;
            string p = imgPathOrUidPath.Replace('\\', '/');
            int idx = p.IndexOf(":/", StringComparison.Ordinal);
            if (idx <= 0) return false;
            string prefix = p.Substring(0, idx);
            if (string.IsNullOrEmpty(prefix)) return false;

            // Case 1: var/zip path: AddonPackages/Foo.Bar.1.var:/...
            if (prefix.EndsWith(".var", StringComparison.OrdinalIgnoreCase) || prefix.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(prefix);
                    if (!string.IsNullOrEmpty(name))
                    {
                        uid = name;
                        return true;
                    }
                }
                catch { }
                return false;
            }

            // Case 2: uid-style: Foo.Bar.1:/...
            if (prefix.IndexOf('/') < 0)
            {
                uid = prefix;
                return true;
            }

            return false;
        }

        public static void QueueHit(string path, string imgPathOrUidPath = null)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (s_HitBuffer)
            {
                s_HitBuffer.Add(path);
                if (!string.IsNullOrEmpty(imgPathOrUidPath))
                {
                    // Store a single best-effort package uid for exemption decisions.
                    // Mapping is append-only in DB (cache_path,pkg_uid), so losing some here is OK.
                    if (!s_HitPkgUidByPath.ContainsKey(path) && TryExtractPackageUidFromImagePath(imgPathOrUidPath, out string uid))
                    {
                        if (!string.IsNullOrEmpty(uid)) s_HitPkgUidByPath[path] = uid;
                    }
                }
                s_LastQueueTime = FlushClockSeconds();
                s_FlushPending = true;
            }
        }

        public static void CheckAutoFlush()
        {
            if (!s_FlushPending) return;
            lock (s_HitBuffer)
            {
                if (s_FlushActive || FlushClockSeconds() < s_RetryAfter) return;
                if (s_RetryPaths == null && !s_FlushRequested && s_HitBuffer.Count < 500
                    && FlushClockSeconds() - s_LastQueueTime <= 3.0) return;
            }
            FlushHitsBatch();
        }

        public static void FlushHitsBatch()
        {
            string[] toFlush;
            List<VpbLocalDatabase.CacheUsagePkgRow> pkgRows;
            lock (s_HitBuffer)
            {
                s_FlushRequested = true;
                if (s_FlushActive || FlushClockSeconds() < s_RetryAfter) return;
                if (s_RetryPaths != null)
                {
                    // Keep failed and newly arrived cohorts separate: each committed cohort counts once.
                    toFlush = s_RetryPaths;
                    pkgRows = s_RetryPkgRows;
                    s_RetryPaths = null;
                    s_RetryPkgRows = null;
                }
                else
                {
                    if (s_HitBuffer.Count == 0)
                    {
                        s_FlushRequested = false;
                        s_FlushPending = false;
                        return;
                    }
                    toFlush = new string[s_HitBuffer.Count];
                    s_HitBuffer.CopyTo(toFlush);
                    pkgRows = new List<VpbLocalDatabase.CacheUsagePkgRow>(s_HitPkgUidByPath.Count);
                    foreach (var kvp in s_HitPkgUidByPath)
                    {
                        if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                            pkgRows.Add(new VpbLocalDatabase.CacheUsagePkgRow { CachePath = kvp.Key, PackageUid = kvp.Value });
                    }
                    s_HitBuffer.Clear();
                    s_HitPkgUidByPath.Clear();
                }
                s_FlushActive = true;
                // Retrying an older cohort must preserve the request to drain newer buffered hits.
                s_FlushRequested = s_HitBuffer.Count > 0;
                s_FlushPending = s_HitBuffer.Count > 0;
            }

            bool queued = false;
            try
            {
                queued = ThreadPool.QueueUserWorkItem(_ =>
                {
                    bool success = false;
                    try { success = VpbLocalDatabase.TryRecordCacheUsageBatch(toFlush, pkgRows); }
                    catch (Exception ex) { LogUtil.LogError("[VPB] Cache hit flush failed: " + ex.Message); }
                    finally { CompleteHitsFlush(toFlush, pkgRows, success); }
                });
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] Cache hit worker submission failed: " + ex.Message); }
            if (!queued) CompleteHitsFlush(toFlush, pkgRows, false);
        }

        private static void CompleteHitsFlush(string[] paths, List<VpbLocalDatabase.CacheUsagePkgRow> rows, bool success)
        {
            lock (s_HitBuffer)
            {
                if (!success)
                {
                    s_RetryPaths = paths;
                    s_RetryPkgRows = rows;
                    s_RetryAfter = FlushClockSeconds() + 3.0;
                }
                else s_RetryAfter = 0;
                s_FlushActive = false;
                s_FlushPending = s_RetryPaths != null || s_HitBuffer.Count > 0;
                if (!s_FlushPending) s_FlushRequested = false;
            }
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
