using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Profiling;

namespace VPB
{
    // Read-only periodic snapshot for diagnosing progressive FPS degradation.
    // Off by default; toggled via BepInEx config Logging.LogPerfTelemetry.
    // Emits one tagged "VPB_PERF_TELEMETRY ..." line every IntervalSeconds; safe to grep.
    static class VpbPerfTelemetry
    {
        const float IntervalSeconds = 30f;

        static float _nextEmitRealtime;
        static Snapshot _prev;

        struct Snapshot
        {
            public bool valid;
            public int textureCache;
            public int immediateTextureCache;
            public int thumbnailCache;
            public int iconSpriteCache;
            public int queuedImages;
            public int dispatchedImages;
            public int pendingThumbnailCallbacks;
            public int pendingThumbCacheJobsTotal;
            public int thumbCacheTotalEnqueuedTotal;
            public int thumbCacheSavedTotal;
            public int panelCount;
            public int scrollListenerCountTotal;
            public int fileButtonPoolTotal;
            public int navButtonPoolTotal;
            public int fileButtonImagesTotal;
            public int activeButtonsTotal;
            public int tabButtonsTotal;
            public int packagesByUidCount;
            public int dataToPathCount;
            public long monoHeap;
            public long allocHeap;
            public long managedHeap;
            public int gen0;
            public int gen1;
            public int gen2;
        }

        public static void EmitSnapshotIfDue()
        {
            try
            {
                if (!IsEnabled()) return;

                float now = Time.realtimeSinceStartup;
                if (now < _nextEmitRealtime) return;
                _nextEmitRealtime = now + IntervalSeconds;

                Snapshot s = Capture();
                Emit(s, _prev);
                _prev = s;
            }
            catch (Exception ex)
            {
                // Telemetry must never destabilize the host plugin.
                Debug.LogWarning("[VPB] PerfTelemetry exception: " + ex.Message);
            }
        }

        static bool IsEnabled()
        {
            try
            {
                var inst = Settings.Instance;
                if (inst == null || inst.LogPerfTelemetry == null) return false;
                return inst.LogPerfTelemetry.Value;
            }
            catch { return false; }
        }

        static Snapshot Capture()
        {
            Snapshot s = new Snapshot();
            s.valid = true;

            try
            {
                var loader = CustomImageLoaderThreaded.singleton;
                if (loader != null)
                {
                    loader.GetTelemetryCounts(
                        out s.textureCache,
                        out s.immediateTextureCache,
                        out s.thumbnailCache,
                        out s.queuedImages,
                        out s.dispatchedImages,
                        out s.pendingThumbnailCallbacks);
                }
            }
            catch { }

            try { s.iconSpriteCache = UI.IconSpriteCacheCount; } catch { }

            try
            {
                var gallery = Gallery.singleton;
                if (gallery != null)
                {
                    var panels = gallery.Panels;
                    if (panels != null)
                    {
                        s.panelCount = panels.Count;
                        for (int i = 0; i < panels.Count; i++)
                        {
                            var panel = panels[i];
                            if (panel == null) continue;
                            int jobs, enq, saved;
                            try
                            {
                                panel.GetPerfTelemetry(out jobs, out enq, out saved);
                                s.pendingThumbCacheJobsTotal += jobs;
                                s.thumbCacheTotalEnqueuedTotal += enq;
                                s.thumbCacheSavedTotal += saved;
                            }
                            catch { }
                            try
                            {
                                int listeners = panel.GetScrollListenerCountForTelemetry();
                                if (listeners >= 0) s.scrollListenerCountTotal += listeners;
                            }
                            catch { }
                            try
                            {
                                int fbPool, navPool, fbImg, activeBtns, tabBtns;
                                panel.GetButtonPoolTelemetry(out fbPool, out navPool, out fbImg, out activeBtns, out tabBtns);
                                s.fileButtonPoolTotal += fbPool;
                                s.navButtonPoolTotal += navPool;
                                s.fileButtonImagesTotal += fbImg;
                                s.activeButtonsTotal += activeBtns;
                                s.tabButtonsTotal += tabBtns;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            try
            {
                // Racy read on Count; acceptable for telemetry. FileManager owns the write lock; we only sample.
                var pkgs = FileManager.PackagesByUid;
                s.packagesByUidCount = pkgs != null ? pkgs.Count : 0;
            }
            catch { }

            try { s.dataToPathCount = GenericTextureHook.DataToPathCount; } catch { }

            try { s.monoHeap = Profiler.GetMonoUsedSizeLong(); } catch { }
            try { s.allocHeap = Profiler.GetTotalAllocatedMemoryLong(); } catch { }
            try { s.managedHeap = GC.GetTotalMemory(false); } catch { }
            try
            {
                s.gen0 = GC.CollectionCount(0);
                s.gen1 = GC.CollectionCount(1);
                s.gen2 = GC.CollectionCount(2);
            }
            catch { }

            return s;
        }

        static void Emit(Snapshot s, Snapshot p)
        {
            string msg = string.Format(
                "VPB_PERF_TELEMETRY t={0:0}s" +
                " | texCache={1} ({2}) immCache={3} ({4}) thumbCache={5} ({6}) iconSprites={7} ({8})" +
                " | queued={9} ({10}) dispatched={11} ({12}) pendingCb={13} ({14})" +
                " | thumbJobs={15} ({16}) thumbEnq={17} ({18}) thumbSaved={19} ({20})" +
                " | panels={21} ({22}) scrollListeners={23} ({24})" +
                " | poolFB={25} ({26}) poolNav={27} ({28}) btnImg={29} ({30}) activeBtns={31} ({32}) tabBtns={33} ({34}) pkgUid={35} ({36}) bufPath={37} ({38})" +
                " | mono={39} alloc={40} managed={41}" +
                " | gc[0/1/2]={42}/{43}/{44} (delta {45}/{46}/{47})",
                Time.realtimeSinceStartup,
                s.textureCache, Delta(s.textureCache, p.textureCache),
                s.immediateTextureCache, Delta(s.immediateTextureCache, p.immediateTextureCache),
                s.thumbnailCache, Delta(s.thumbnailCache, p.thumbnailCache),
                s.iconSpriteCache, Delta(s.iconSpriteCache, p.iconSpriteCache),
                s.queuedImages, Delta(s.queuedImages, p.queuedImages),
                s.dispatchedImages, Delta(s.dispatchedImages, p.dispatchedImages),
                s.pendingThumbnailCallbacks, Delta(s.pendingThumbnailCallbacks, p.pendingThumbnailCallbacks),
                s.pendingThumbCacheJobsTotal, Delta(s.pendingThumbCacheJobsTotal, p.pendingThumbCacheJobsTotal),
                s.thumbCacheTotalEnqueuedTotal, Delta(s.thumbCacheTotalEnqueuedTotal, p.thumbCacheTotalEnqueuedTotal),
                s.thumbCacheSavedTotal, Delta(s.thumbCacheSavedTotal, p.thumbCacheSavedTotal),
                s.panelCount, Delta(s.panelCount, p.panelCount),
                s.scrollListenerCountTotal, Delta(s.scrollListenerCountTotal, p.scrollListenerCountTotal),
                s.fileButtonPoolTotal, Delta(s.fileButtonPoolTotal, p.fileButtonPoolTotal),
                s.navButtonPoolTotal, Delta(s.navButtonPoolTotal, p.navButtonPoolTotal),
                s.fileButtonImagesTotal, Delta(s.fileButtonImagesTotal, p.fileButtonImagesTotal),
                s.activeButtonsTotal, Delta(s.activeButtonsTotal, p.activeButtonsTotal),
                s.tabButtonsTotal, Delta(s.tabButtonsTotal, p.tabButtonsTotal),
                s.packagesByUidCount, Delta(s.packagesByUidCount, p.packagesByUidCount),
                s.dataToPathCount, Delta(s.dataToPathCount, p.dataToPathCount),
                FormatBytes(s.monoHeap),
                FormatBytes(s.allocHeap),
                FormatBytes(s.managedHeap),
                s.gen0, s.gen1, s.gen2,
                p.valid ? (s.gen0 - p.gen0) : 0,
                p.valid ? (s.gen1 - p.gen1) : 0,
                p.valid ? (s.gen2 - p.gen2) : 0);

            LogUtil.LogWarning(msg);
        }

        static string Delta(int now, int prev)
        {
            int d = now - prev;
            return d >= 0 ? ("+" + d.ToString()) : d.ToString();
        }

        static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0B";
            double b = bytes;
            if (b >= 1024d * 1024d * 1024d) return (b / (1024d * 1024d * 1024d)).ToString("0.00") + "GB";
            if (b >= 1024d * 1024d) return (b / (1024d * 1024d)).ToString("0.00") + "MB";
            if (b >= 1024d) return (b / 1024d).ToString("0.00") + "KB";
            return bytes.ToString() + "B";
        }

        // UnityEventBase has a private InvokableCallList m_Calls, which holds m_RuntimeCalls
        // (the list AddListener appends to) plus a mirror of persistent calls. Counting runtime
        // calls is sufficient to detect AddListener leaks across panel rebuilds.
        static FieldInfo _callsField;
        static FieldInfo _runtimeCallsField;
        static bool _reflectionInitialized;

        internal static int CountListeners(UnityEventBase ev)
        {
            if (ev == null) return -1;
            try
            {
                EnsureReflection();
                if (_callsField == null || _runtimeCallsField == null) return -1;
                var calls = _callsField.GetValue(ev);
                if (calls == null) return -1;
                var rc = _runtimeCallsField.GetValue(calls) as System.Collections.ICollection;
                return rc != null ? rc.Count : -1;
            }
            catch { return -1; }
        }

        static void EnsureReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;
            try
            {
                _callsField = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_callsField != null)
                {
                    var t = _callsField.FieldType;
                    _runtimeCallsField = t.GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic);
                }
            }
            catch { }
        }
    }
}
