using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private readonly List<FileEntry> _pluginsFloatCatalog = new List<FileEntry>(512);
        private Dictionary<string, List<string>> _pluginsFloatChildrenSql =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private Coroutine _pluginsFloatCatalogCo;
        private int _pluginsFloatCatalogGen;
        private DateTime _pluginsFloatCatalogPkgTime = DateTime.MinValue;
        private bool _pluginsFloatCatalogReady;
        private int _pluginsFloatRefsWarmGen;

        /// <summary>
        /// Grid-parity load: cat_mem JOIN pkg on ThreadPool.
        /// Never wait on cslist-ref LIKE (contends with deferred Persist → stuck "Loading…").
        /// </summary>
        private void RequestPluginsFloatCatalog(bool force)
        {
            DateTime pkgTime = DateTime.MinValue;
            try { pkgTime = FileManager.lastPackageRefreshTime; } catch { }

            if (!force
                && _pluginsFloatCatalogReady
                && _pluginsFloatCatalog.Count > 0
                && pkgTime != DateTime.MinValue
                && pkgTime == _pluginsFloatCatalogPkgTime)
            {
                try { LogUtil.Log("[VPB.PluginsFloat] catalog cache HIT count=" + _pluginsFloatCatalog.Count); } catch { }
                RefreshPluginsFloatTree(true);
                try { RefreshPluginsFloatDestButtons(); } catch { }
                return;
            }

            try
            {
                LogUtil.Log("[VPB.PluginsFloat] catalog load START force=" + (force ? "1" : "0")
                    + " ready=" + (_pluginsFloatCatalogReady ? "1" : "0")
                    + " prev=" + _pluginsFloatCatalog.Count);
            }
            catch { }

            _pluginsFloatCatalogGen++;
            int gen = _pluginsFloatCatalogGen;
            if (_pluginsFloatCatalogCo != null)
            {
                try { StopCoroutine(_pluginsFloatCatalogCo); } catch { }
                _pluginsFloatCatalogCo = null;
            }
            _pluginsFloatCatalogReady = false;
            _pluginsFloatCatalog.Clear();
            try { RefreshPluginsFloatTree(true); } catch { }
            _pluginsFloatCatalogCo = StartCoroutine(PluginsFloatCatalogLoadCo(gen, pkgTime));
        }

        private IEnumerator PluginsFloatCatalogLoadCo(int gen, DateTime pkgTime)
        {
            var built = new List<FileEntry>(512);
            var workerDone = new int[1];
            var workerOk = new bool[1];
            var workerEx = new string[1];

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Catalog only — do NOT read cslist_referenced here (SQLite writer lock = hang).
                    workerOk[0] = VpbLocalDatabase.TryBuildPluginsFloatCatalog(built);
                }
                catch (Exception ex)
                {
                    workerOk[0] = false;
                    try { workerEx[0] = ex.Message; } catch { workerEx[0] = "ex"; }
                }
                finally
                {
                    workerDone[0] = 1;
                }
            });

            while (workerDone[0] == 0)
                yield return null;

            if (gen != _pluginsFloatCatalogGen) yield break;

            if (!workerOk[0] || built == null)
            {
                try
                {
                    LogUtil.LogWarning("[VPB.PluginsFloat] catalog worker FAIL"
                        + " built=" + (built != null ? built.Count.ToString() : "null")
                        + (string.IsNullOrEmpty(workerEx[0]) ? "" : " ex=" + workerEx[0]));
                }
                catch { }
                _pluginsFloatCatalogReady = true;
                _pluginsFloatCatalogCo = null;
                try { RefreshPluginsFloatTree(true); } catch { }
                yield break;
            }

            ReplacePluginsFloatCatalog(built);
            _pluginsFloatCatalogPkgTime = pkgTime;
            _pluginsFloatLatestUidSet = null; // recompute if Latest-only after catalog swap
            _pluginsFloatCatalogReady = true;
            _pluginsFloatCatalogCo = null;
            try
            {
                LogUtil.Log("[VPB.PluginsFloat] catalog apply START count=" + _pluginsFloatCatalog.Count);
            }
            catch { }

            // Yielded row spawn — 1.7k+ GameObjects sync freezes main thread.
            RefreshPluginsFloatTree(true);
            try { RefreshPluginsFloatDestButtons(); } catch { }

            // Orphan .cs membership after UI is live (never blocks open).
            SchedulePluginsFloatCslistRefsWarm(gen);
        }

        private void SchedulePluginsFloatCslistRefsWarm(int catalogGen)
        {
            int warmGen = ++_pluginsFloatRefsWarmGen;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    VpbLocalDatabase.TryReadAllCslistReferencedFromCache(refs);
                    lock (_cslistReferencedLock)
                    {
                        _cslistReferencedPaths = refs;
                        _cslistReferencedWarm = true;
                    }
                    try
                    {
                        LogUtil.Log("[VPB.PluginsFloat] cslist refs warm count=" + refs.Count
                            + " warmGen=" + warmGen);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    try
                    {
                        LogUtil.LogWarning("[VPB.PluginsFloat] cslist refs warm FAIL: "
                            + (ex != null ? ex.Message : "?"));
                    }
                    catch { }
                    return;
                }

                _pluginsFloatRefsRefreshCatalogGen = catalogGen;
                Interlocked.Exchange(ref _pluginsFloatRefsRefreshPending, 1);
            });
        }

        private int _pluginsFloatRefsRefreshPending;
        private int _pluginsFloatRefsRefreshCatalogGen;

        /// <summary>Called from Update when refs warm finishes — refresh orphan leaves.</summary>
        private void TickPluginsFloatRefsRefresh()
        {
            if (Interlocked.Exchange(ref _pluginsFloatRefsRefreshPending, 0) == 0) return;
            if (_pluginsFloatRefsRefreshCatalogGen != _pluginsFloatCatalogGen) return;
            if (!IsPluginsFloatOpen() || !_pluginsFloatCatalogReady) return;
            try
            {
                LogUtil.Log("[VPB.PluginsFloat] tree refresh after refs warm");
            }
            catch { }
            RefreshPluginsFloatTree(false);
        }

        private void ReplacePluginsFloatCatalog(List<FileEntry> source)
        {
            _pluginsFloatCatalog.Clear();
            if (source == null || source.Count == 0) return;
            if (_pluginsFloatCatalog.Capacity < source.Count)
                _pluginsFloatCatalog.Capacity = source.Count;
            for (int i = 0; i < source.Count; i++)
            {
                FileEntry e = source[i];
                if (e != null) _pluginsFloatCatalog.Add(e);
            }
        }

        private bool TryGetPluginsFloatChildrenFromSql(string parentKey, out List<string> children)
        {
            children = null;
            if (string.IsNullOrEmpty(parentKey) || _pluginsFloatChildrenSql == null) return false;
            string key = parentKey.Replace('\\', '/').ToLowerInvariant();
            return _pluginsFloatChildrenSql.TryGetValue(key, out children) && children != null;
        }

        private void StopPluginsFloatTreePopulate()
        {
            // Legacy no-op — tree is virtualized (pool rebind), no populate coroutine.
        }

        private void StopPluginsFloatCatalogLoad()
        {
            _pluginsFloatCatalogGen++;
            _pluginsFloatRefsWarmGen++;
            Interlocked.Exchange(ref _pluginsFloatRefsRefreshPending, 0);
            if (_pluginsFloatCatalogCo != null)
            {
                try { StopCoroutine(_pluginsFloatCatalogCo); } catch { }
                _pluginsFloatCatalogCo = null;
            }
        }
    }
}
