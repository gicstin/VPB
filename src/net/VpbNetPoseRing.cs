using System;
using System.Diagnostics;
using System.Threading;

namespace VPB
{
    public sealed class VpbNetPoseRing
    {
        readonly byte[][] _slots;
        readonly int[] _lengths;
        readonly long[] _stamps;
        readonly int _mask;
        readonly int _slotBytes;

        volatile int _head;
        volatile int _tail;
        int _droppedOldest;
        int _truncated;

        public VpbNetPoseRing(int capacityPowerOfTwo, int slotBytes)
        {
            int cap = 2;
            while (cap < capacityPowerOfTwo) cap <<= 1;
            _mask = cap - 1;
            _slotBytes = slotBytes > 0 ? slotBytes : 1;
            _slots = new byte[cap][];
            _lengths = new int[cap];
            _stamps = new long[cap];
            for (int i = 0; i < cap; i++) _slots[i] = new byte[_slotBytes];
        }

        public int Capacity { get { return _mask; } }
        public int DroppedOldest { get { return _droppedOldest; } }
        public int Truncated { get { return _truncated; } }
        public int SlotBytes { get { return _slotBytes; } }

        public int Count
        {
            get
            {
                int h = _head;
                int t = _tail;
                return (h - t) & _mask;
            }
        }

        public bool Enqueue(byte[] src, int len)
        {
            if (src == null || len <= 0) return false;

            int head = _head;
            int next = (head + 1) & _mask;
            bool dropped = false;

            if (next == _tail)
            {
                _tail = (_tail + 1) & _mask;
                Interlocked.Increment(ref _droppedOldest);
                dropped = true;
            }

            byte[] slot = _slots[head];
            if (len > slot.Length)
            {
                len = slot.Length;
                Interlocked.Increment(ref _truncated);
            }
            Buffer.BlockCopy(src, 0, slot, 0, len);
            _lengths[head] = len;
            _stamps[head] = Stopwatch.GetTimestamp();

            Thread.MemoryBarrier();
            _head = next;
            return !dropped;
        }

        public int TryDequeue(byte[] dst)
        {
            long ignored;
            return TryDequeue(dst, out ignored);
        }

        public int TryDequeue(byte[] dst, out long enqueuedTicks)
        {
            enqueuedTicks = 0;
            if (dst == null) return 0;

            int tail = _tail;
            if (tail == _head) return 0;

            int len = _lengths[tail];
            enqueuedTicks = _stamps[tail];
            byte[] slot = _slots[tail];
            if (len > dst.Length) len = dst.Length;
            Buffer.BlockCopy(slot, 0, dst, 0, len);

            Thread.MemoryBarrier();
            _tail = (tail + 1) & _mask;
            return len;
        }

        public void Clear()
        {
            _tail = _head;
        }

        public void ResetCounters()
        {
            _droppedOldest = 0;
            _truncated = 0;
        }
    }
}
