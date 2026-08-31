using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Profiling;

namespace VPB
{
    // Read-only periodic snapshot for diagnosing progressive FPS degradation.
    // Off by default; toggled via BepInEx config Logging.LogPerfTelemetry.
    // Emits one tagged "VPB_PERF_TELEMETRY ..." line every Logging.LogPerfTelemetryIntervalSeconds (1-30); safe to grep.
    static class VpbPerfTelemetry
    {
        const int MinIntervalSeconds = 1;
        const int MaxIntervalSeconds = 30;

        static float _nextEmitRealtime;
        static Snapshot _prev;
        static bool _processMemoryFailureLogged;
        static bool _nativeImageLoaderFieldsInitialized;
        static FieldInfo _nativeTextureCacheField;
        static FieldInfo _nativeImmediateTextureCacheField;
        static FieldInfo _nativeThumbnailCacheField;
        static FieldInfo _nativeTextureTrackedCacheField;
        static FieldInfo _nativeTextureUseCountField;

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetProcessMemoryInfo(
            IntPtr process,
            ref ProcessMemoryCountersEx counters,
            uint size);

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessMemoryCountersEx
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

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
            public bool processMemoryValid;
            public long processWorkingSet;
            public long processPrivateBytes;
            public int nativeTextureCache;
            public int nativeImmediateTextureCache;
            public int nativeThumbnailCache;
            public int nativeTextureTrackedCache;
            public int nativeTextureUseCount;
            public int ilmTextureCache;
            public long ilmTextureCacheBytes;
            public int ilmDecompressedCache;
            public long ilmDecompressedCacheBytes;
            public int ilmPendingCreates;
            public long ilmPendingCreateBytes;
            public int ilmInflight;
            public int ilmActivePayloadWorkers;
            public int ilmPendingWritePaths;
            public int ilmRuntimeWriteQueue;
            public int ilmRuntimeWriteActive;
            public long byteArrayPoolRetainedBytes;
            public int onDemandZstdQueued;
            public int onDemandZstdActive;
            public long onDemandZstdPayloadBytes;
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
                _nextEmitRealtime = now + IntervalSeconds();

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

        static float IntervalSeconds()
        {
            try
            {
                var inst = Settings.Instance;
                if (inst == null || inst.LogPerfTelemetryIntervalSeconds == null) return MaxIntervalSeconds;
                int v = inst.LogPerfTelemetryIntervalSeconds.Value;
                if (v < MinIntervalSeconds) v = MinIntervalSeconds;
                if (v > MaxIntervalSeconds) v = MaxIntervalSeconds;
                return v;
            }
            catch { return MaxIntervalSeconds; }
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

            CaptureProcessMemory(ref s);
            CaptureNativeImageLoaderCounts(ref s);

            try
            {
                var imageManager = ImageLoadingMgr.singleton;
                if (imageManager != null)
                {
                    imageManager.GetMemoryTelemetry(
                        out s.ilmTextureCache,
                        out s.ilmTextureCacheBytes,
                        out s.ilmDecompressedCache,
                        out s.ilmDecompressedCacheBytes,
                        out s.ilmPendingCreates,
                        out s.ilmPendingCreateBytes,
                        out s.ilmInflight,
                        out s.ilmActivePayloadWorkers,
                        out s.ilmPendingWritePaths,
                        out s.ilmRuntimeWriteQueue,
                        out s.ilmRuntimeWriteActive);
                }
            }
            catch { }

            try { s.byteArrayPoolRetainedBytes = ByteArrayPool.RetainedBytes; } catch { }
            try
            {
                OnDemandZstdWriteQueue.GetTelemetryCounts(
                    out s.onDemandZstdQueued,
                    out s.onDemandZstdActive,
                    out s.onDemandZstdPayloadBytes);
            }
            catch { }

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

        static void CaptureProcessMemory(ref Snapshot snapshot)
        {
            try
            {
                ProcessMemoryCountersEx counters = new ProcessMemoryCountersEx();
                uint size = (uint)Marshal.SizeOf(typeof(ProcessMemoryCountersEx));
                counters.cb = size;
                if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, size))
                {
                    LogProcessMemoryFailure("Win32 error " + Marshal.GetLastWin32Error());
                    return;
                }

                snapshot.processWorkingSet = (long)counters.WorkingSetSize.ToUInt64();
                snapshot.processPrivateBytes = (long)counters.PrivateUsage.ToUInt64();
                snapshot.processMemoryValid = true;
            }
            catch (Exception ex)
            {
                LogProcessMemoryFailure(ex.GetType().Name + ": " + ex.Message);
            }
        }

        static void CaptureNativeImageLoaderCounts(ref Snapshot snapshot)
        {
            snapshot.nativeTextureCache = -1;
            snapshot.nativeImmediateTextureCache = -1;
            snapshot.nativeThumbnailCache = -1;
            snapshot.nativeTextureTrackedCache = -1;
            snapshot.nativeTextureUseCount = -1;

            try
            {
                ImageLoaderThreaded loader = ImageLoaderThreaded.singleton;
                if (loader == null) return;
                EnsureNativeImageLoaderFields();
                snapshot.nativeTextureCache = ReadCollectionCount(_nativeTextureCacheField, loader);
                snapshot.nativeImmediateTextureCache = ReadCollectionCount(_nativeImmediateTextureCacheField, loader);
                snapshot.nativeThumbnailCache = ReadCollectionCount(_nativeThumbnailCacheField, loader);
                snapshot.nativeTextureTrackedCache = ReadCollectionCount(_nativeTextureTrackedCacheField, loader);
                snapshot.nativeTextureUseCount = ReadCollectionCount(_nativeTextureUseCountField, loader);
            }
            catch { }
        }

        static void EnsureNativeImageLoaderFields()
        {
            if (_nativeImageLoaderFieldsInitialized) return;
            _nativeImageLoaderFieldsInitialized = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = typeof(ImageLoaderThreaded);
            _nativeTextureCacheField = type.GetField("textureCache", flags);
            _nativeImmediateTextureCacheField = type.GetField("immediateTextureCache", flags);
            _nativeThumbnailCacheField = type.GetField("thumbnailCache", flags);
            _nativeTextureTrackedCacheField = type.GetField("textureTrackedCache", flags);
            _nativeTextureUseCountField = type.GetField("textureUseCount", flags);
        }

        static int ReadCollectionCount(FieldInfo field, object instance)
        {
            if (field == null || instance == null) return -1;
            var collection = field.GetValue(instance) as System.Collections.ICollection;
            return collection != null ? collection.Count : -1;
        }

        static void LogProcessMemoryFailure(string reason)
        {
            if (_processMemoryFailureLogged) return;
            _processMemoryFailureLogged = true;
            LogUtil.LogWarning("[VPB] Process memory telemetry unavailable: " + reason);
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
                " | gc[0/1/2]={42}/{43}/{44} (delta {45}/{46}/{47})" +
                " | procWorking={48} ({49}) procPrivate={50} ({51})" +
                " | ilmTex={52}/{53} ilmDecomp={54}/{55} ilmPending={56}/{57} ilmInflight={58} ilmWorkers={59}" +
                " ilmWritePaths={60} ilmWriteQ={61} ilmWriteActive={62}" +
                " | bytePool={63} odZstdQ={64} odZstdActive={65} odZstdMemPayload={66}" +
                " | vamTex={67} ({68}) vamImm={69} ({70}) vamThumb={71} ({72})" +
                " vamTracked={73} ({74}) vamUsed={75} ({76})",
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
                p.valid ? (s.gen2 - p.gen2) : 0,
                FormatProcessBytes(s.processWorkingSet, s.processMemoryValid),
                DeltaProcessBytes(s.processWorkingSet, p.processWorkingSet, s.processMemoryValid, p.valid && p.processMemoryValid),
                FormatProcessBytes(s.processPrivateBytes, s.processMemoryValid),
                DeltaProcessBytes(s.processPrivateBytes, p.processPrivateBytes, s.processMemoryValid, p.valid && p.processMemoryValid),
                s.ilmTextureCache, FormatBytes(s.ilmTextureCacheBytes),
                s.ilmDecompressedCache, FormatBytes(s.ilmDecompressedCacheBytes),
                s.ilmPendingCreates, FormatBytes(s.ilmPendingCreateBytes),
                s.ilmInflight, s.ilmActivePayloadWorkers,
                s.ilmPendingWritePaths, s.ilmRuntimeWriteQueue, s.ilmRuntimeWriteActive,
                FormatBytes(s.byteArrayPoolRetainedBytes),
                s.onDemandZstdQueued, s.onDemandZstdActive, FormatBytes(s.onDemandZstdPayloadBytes),
                FormatCount(s.nativeTextureCache), DeltaCount(s.nativeTextureCache, p.nativeTextureCache, p.valid),
                FormatCount(s.nativeImmediateTextureCache), DeltaCount(s.nativeImmediateTextureCache, p.nativeImmediateTextureCache, p.valid),
                FormatCount(s.nativeThumbnailCache), DeltaCount(s.nativeThumbnailCache, p.nativeThumbnailCache, p.valid),
                FormatCount(s.nativeTextureTrackedCache), DeltaCount(s.nativeTextureTrackedCache, p.nativeTextureTrackedCache, p.valid),
                FormatCount(s.nativeTextureUseCount), DeltaCount(s.nativeTextureUseCount, p.nativeTextureUseCount, p.valid));

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

        static string FormatCount(int value)
        {
            return value >= 0 ? value.ToString() : "n/a";
        }

        static string DeltaCount(int now, int previous, bool previousValid)
        {
            return now >= 0 && previous >= 0 && previousValid ? Delta(now, previous) : "n/a";
        }

        static string DeltaBytes(long now, long previous, bool previousValid)
        {
            if (!previousValid) return "0B";
            long delta = now - previous;
            if (delta == 0) return "0B";
            return delta > 0 ? "+" + FormatBytes(delta) : "-" + FormatBytes(-delta);
        }

        static string FormatProcessBytes(long bytes, bool valid)
        {
            return valid ? FormatBytes(bytes) : "n/a";
        }

        static string DeltaProcessBytes(long now, long previous, bool valid, bool previousValid)
        {
            return valid && previousValid ? DeltaBytes(now, previous, true) : "n/a";
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
