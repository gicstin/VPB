using System;
using System.Diagnostics;
using System.Threading;

namespace VPB
{
    public sealed class VpbNetFrameRing
    {
        readonly byte[][] _slots;
        readonly int[] _lengths;
        readonly long[] _stamps;
        readonly int _mask;

        volatile int _head;
        volatile int _tail;
        int _dropped;

        public VpbNetFrameRing(int capacityPowerOfTwo, int slotBytes)
        {
            int cap = 2;
            while (cap < capacityPowerOfTwo) cap <<= 1;
            _mask = cap - 1;
            _slots = new byte[cap][];
            _lengths = new int[cap];
            _stamps = new long[cap];
            for (int i = 0; i < cap; i++) _slots[i] = new byte[slotBytes];
        }

        public int Dropped { get { return _dropped; } }

        public int Count
        {
            get
            {
                int h = _head;
                int t = _tail;
                return (h - t) & _mask;
            }
        }

        public bool TryEnqueue(byte[] src, int len)
        {
            if (src == null || len <= 0) return false;

            int head = _head;
            int next = (head + 1) & _mask;
            if (next == _tail)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }

            byte[] slot = _slots[head];
            if (len > slot.Length) len = slot.Length;
            Buffer.BlockCopy(src, 0, slot, 0, len);
            _lengths[head] = len;
            _stamps[head] = Stopwatch.GetTimestamp();

            Thread.MemoryBarrier();
            _head = next;
            return true;
        }

        public int TryDequeue(byte[] dst)
        {
            long ignored;
            return TryDequeue(dst, out ignored);
        }

        public int TryDequeue(byte[] dst, out long enqueuedTicks)
        {
            enqueuedTicks = 0;

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
    }
}
