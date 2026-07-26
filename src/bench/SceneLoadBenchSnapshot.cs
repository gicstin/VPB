using System.Collections.Generic;

namespace VPB
{
    internal struct SceneLoadBenchPerfEntry
    {
        public double TotalMs;
        public long TotalBytes;
        public int Count;
    }

    internal struct SceneLoadBenchSnapshot
    {
        public string Context;
        public string SceneName;
        public string PackageUid;
        public double TotalMs;
        public double TotalSeconds;
        public int Serial;
        public bool Success;

        public float PreLoadInternalSec;
        public float LoadInternalWindowSec;
        public float PostLoadInternalToNotLoadingSec;
        public float FirstImageActivitySec;
        public float WorldUiSec;
        public float PreNotLoadingSec;
        public float TailSec;
        public float FirstNotBusyDelaySec;
        public float CriteriaReadyDelaySec;

        public int FrameSamples;
        public double FpsAvg;
        public float FrameMsAvg;
        public float FrameMsP95;
        public float FrameMsMax;
        public int St33;
        public int St50;
        public int St100;

        public long MemAllocEnd;
        public long MemAllocDelta;
        public long MemReservedEnd;
        public long MemReservedDelta;
        public long MemManagedEnd;
        public long MemManagedDelta;

        public int SettleFileExistsMisses;
        public int SettleOpenStreamMisses;
        public int SettleVarEntryMisses;
        public int SettleOnDemandRetries;
        public int SettleSampleCount;
        public int SettleBusySampleCount;
        public int SettleQueueMax;

        public Dictionary<string, SceneLoadBenchPerfEntry> Perf;
    }
}
