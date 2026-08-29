using System;
using UnityEngine;
using UnityEngine.Profiling;
using VPB.src.util;

namespace VPB
{
    public sealed class VpbNetHistogram
    {
        const int BucketCount = 128;

        readonly int[] _counts = new int[BucketCount + 1];
        readonly double _width;
        int _total;
        double _sum;
        double _max;
        double _min;

        public VpbNetHistogram(double bucketWidth)
        {
            _width = bucketWidth > 0.0 ? bucketWidth : 1.0;
        }

        public int Count { get { return _total; } }
        public double Mean { get { return _total > 0 ? _sum / _total : 0.0; } }
        public double Max { get { return _max; } }
        public double Min { get { return _total > 0 ? _min : 0.0; } }
        public int Overflow { get { return _counts[BucketCount]; } }
        public double Ceiling { get { return BucketCount * _width; } }

        public void Reset()
        {
            for (int i = 0; i <= BucketCount; i++) _counts[i] = 0;
            _total = 0;
            _sum = 0.0;
            _max = 0.0;
            _min = 0.0;
        }

        public void Add(double v)
        {
            if (_total == 0) { _min = v; _max = v; }
            else
            {
                if (v > _max) _max = v;
                if (v < _min) _min = v;
            }
            _total++;
            _sum += v;

            int b = (int)(v / _width);
            if (b < 0) b = 0;
            else if (b > BucketCount) b = BucketCount;
            _counts[b]++;
        }

        public double Percentile(double p)
        {
            if (_total == 0) return 0.0;
            int target = (int)(_total * p);
            if (target >= _total) target = _total - 1;

            int seen = 0;
            for (int i = 0; i <= BucketCount; i++)
            {
                seen += _counts[i];
                if (seen <= target) continue;
                if (i >= BucketCount) return _max;
                double edge = (i + 1) * _width;
                return edge > _max ? _max : edge;
            }
            return _max;
        }
    }

    public sealed class VpbNetPerfHarness
    {
        public const double SamplerBudgetUs = 150.0;
        public const double ApplierBudgetUs = 500.0;

        public readonly VpbNetHistogram SamplerUs = new VpbNetHistogram(2.0);
        public readonly VpbNetHistogram ApplierUs = new VpbNetHistogram(5.0);
        public readonly VpbNetHistogram FrameMs = new VpbNetHistogram(0.5);

        public long RegionAllocBytes;
        public int RegionTicks;
        public long FrameAllocBytes;
        public int FrameAllocSamples;
        public int FramesOverBudget;
        public float FrameBudgetMs = 11.1f;

        public double CalibratedSamplerUs = -1.0;
        public double CalibratedApplierUs = -1.0;
        public long CalibratedSamplerBytes = -1;
        public long CalibratedApplierBytes = -1;

        public int AtomCount;
        public int PersonCount;
        public bool VrActive;
        public long MonoHeapAtStart;

        int _gcBase;
        int _gcCount;
        long _lastFrameHeap;
        bool _haveFrameHeap;

        public int GcCollections { get { return _gcCount; } }

        public double RegionBytesPerTick { get { return RegionTicks > 0 ? (double)RegionAllocBytes / RegionTicks : 0.0; } }
        public double FrameBytesPerFrame { get { return FrameAllocSamples > 0 ? (double)FrameAllocBytes / FrameAllocSamples : 0.0; } }

        public void Reset()
        {
            SamplerUs.Reset();
            ApplierUs.Reset();
            FrameMs.Reset();
            RegionAllocBytes = 0;
            RegionTicks = 0;
            FrameAllocBytes = 0;
            FrameAllocSamples = 0;
            FramesOverBudget = 0;
            _gcBase = SafeCollectionCount();
            _gcCount = 0;
            _haveFrameHeap = false;
        }

        public void CaptureContext()
        {
            try
            {
                VrActive = XrUtils.IsVrActive();
                FrameBudgetMs = VrActive ? 11.1f : 16.7f;
            }
            catch { }

            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    System.Collections.Generic.List<Atom> atoms = sc.GetAtoms();
                    if (atoms != null)
                    {
                        AtomCount = atoms.Count;
                        int persons = 0;
                        for (int i = 0; i < atoms.Count; i++)
                        {
                            if (SceneUtils.IsPersonLikeAtom(atoms[i])) persons++;
                        }
                        PersonCount = persons;
                    }
                }
            }
            catch { }

            try { MonoHeapAtStart = Profiler.GetMonoUsedSizeLong(); }
            catch { MonoHeapAtStart = 0; }
            if (MonoHeapAtStart <= 0)
            {
                try { MonoHeapAtStart = GC.GetTotalMemory(false); }
                catch { MonoHeapAtStart = 0; }
            }
        }

        public void AddRegion(double samplerUs, double applierUs, long allocBytes, bool applierRan)
        {
            SamplerUs.Add(samplerUs);
            if (applierRan) ApplierUs.Add(applierUs);
            RegionAllocBytes += allocBytes > 0 ? allocBytes : 0;
            RegionTicks++;
        }

        public void AddApplier(double applierUs)
        {
            ApplierUs.Add(applierUs);
        }

        public void AddFrame(float frameMs)
        {
            FrameMs.Add(frameMs);
            if (frameMs > FrameBudgetMs) FramesOverBudget++;

            long heap = 0;
            try { heap = GC.GetTotalMemory(false); }
            catch { return; }

            if (_haveFrameHeap)
            {
                long d = heap - _lastFrameHeap;
                if (d > 0) FrameAllocBytes += d;
                FrameAllocSamples++;
            }
            _lastFrameHeap = heap;
            _haveFrameHeap = true;

            int gc = SafeCollectionCount();
            if (gc > _gcBase)
            {
                _gcCount += gc - _gcBase;
                _gcBase = gc;
            }
        }

        public void RecordCalibration(double samplerUs, double applierUs, long samplerBytes, long applierBytes)
        {
            if (CalibratedSamplerUs < 0.0 || samplerUs < CalibratedSamplerUs) CalibratedSamplerUs = samplerUs;
            if (CalibratedApplierUs < 0.0 || applierUs < CalibratedApplierUs) CalibratedApplierUs = applierUs;
            if (CalibratedSamplerBytes < 0 || samplerBytes < CalibratedSamplerBytes) CalibratedSamplerBytes = samplerBytes;
            if (CalibratedApplierBytes < 0 || applierBytes < CalibratedApplierBytes) CalibratedApplierBytes = applierBytes;
        }

        public bool SamplerWithinBudget { get { return SamplerUs.Count > 0 && SamplerUs.Percentile(0.99) <= SamplerBudgetUs; } }
        public bool ApplierWithinBudget { get { return ApplierUs.Count > 0 && ApplierUs.Percentile(0.99) <= ApplierBudgetUs; } }
        public bool AllocationClean { get { return CalibratedSamplerBytes == 0 && CalibratedApplierBytes == 0; } }

        public void EmitContext(string label)
        {
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] {0} scene: atoms={1} persons={2} vr={3} frameBudget={4:0.0}ms monoHeap={5:0.0}MB{6}",
                label, AtomCount, PersonCount, VrActive ? "yes" : "no", FrameBudgetMs,
                MonoHeapAtStart / 1048576.0,
                AtomCount < 8 || PersonCount < 2 ? "  <-- LIGHT SCENE, numbers do not count against the budget" : ""));
        }

        public void EmitVerdict(string label)
        {
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] {0} sampler: p50={1:0.0} p95={2:0.0} p99={3:0.0} max={4:0.0}us over{5:0}us={6} n={7} (budget {8:0}us)",
                label, SamplerUs.Percentile(0.5), SamplerUs.Percentile(0.95), SamplerUs.Percentile(0.99),
                SamplerUs.Max, SamplerUs.Ceiling, SamplerUs.Overflow, SamplerUs.Count, SamplerBudgetUs));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] {0} applier: p50={1:0.0} p95={2:0.0} p99={3:0.0} max={4:0.0}us over{5:0}us={6} n={7} (budget {8:0}us)",
                label, ApplierUs.Percentile(0.5), ApplierUs.Percentile(0.95), ApplierUs.Percentile(0.99),
                ApplierUs.Max, ApplierUs.Ceiling, ApplierUs.Overflow, ApplierUs.Count, ApplierBudgetUs));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] {0} alloc: calibrated sampler={1}B applier={2}B per call | region={3:0}B/tick | whole frame={4:0}B/frame | gc={5}",
                label, CalibratedSamplerBytes, CalibratedApplierBytes,
                RegionBytesPerTick, FrameBytesPerFrame, _gcCount));

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] {0} frame: p50={1:0.00} p95={2:0.00} p99={3:0.00} max={4:0.00}ms over-budget={5}/{6} frames (context only, compare against an idle session)",
                label, FrameMs.Percentile(0.5), FrameMs.Percentile(0.95), FrameMs.Percentile(0.99),
                FrameMs.Max, FramesOverBudget, FrameMs.Count));
        }

        static int SafeCollectionCount()
        {
            try { return GC.CollectionCount(0); }
            catch { return 0; }
        }
    }
}
