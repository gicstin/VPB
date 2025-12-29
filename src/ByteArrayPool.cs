using System;
using System.Collections.Generic;

namespace var_browser
{
    public static class ByteArrayPool
    {
        private static readonly Dictionary<int, Stack<byte[]>> pool = new Dictionary<int, Stack<byte[]>>();
        private static readonly object lockObj = new object();

        // Debug stats
        public static int TotalRented = 0;
        public static int TotalReturned = 0;
        public static int PoolHits = 0;
        public static long TotalBytesAllocated = 0;
        public static long TotalBytesReused = 0;

        // Round up to next power of 2
        private static int NextPowerOfTwo(int v)
        {
            if (v <= 0) return 4096;
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v;
        }

        public static byte[] Rent(int minSize)
        {
            if (minSize < 0) minSize = 0;
            if (minSize == 0) return new byte[0];

            int size = NextPowerOfTwo(minSize);
            if (size < 4096) size = 4096;

            lock (lockObj)
            {
                Stack<byte[]> stack;
                if (pool.TryGetValue(size, out stack) && stack.Count > 0)
                {
                    PoolHits++;
                    TotalRented++;
                    TotalBytesReused += size;
                    return stack.Pop();
                }
                TotalRented++;
                TotalBytesAllocated += size;
            }

            return new byte[size];
        }

        public static void Return(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return;

            int size = buffer.Length;
            
            // Only pool Power of Two arrays, as Rent only returns POT
            if ((size & (size - 1)) != 0) return;

            lock (lockObj)
            {
                TotalReturned++;
                Stack<byte[]> stack;
                if (!pool.TryGetValue(size, out stack))
                {
                    stack = new Stack<byte[]>();
                    pool[size] = stack;
                }
                
                // Prevent pool from growing infinitely, though unlikely with POT sizes
                if (stack.Count < 50) 
                {
                    stack.Push(buffer);
                }
            }
        }
        
        public static void Clear()
        {
            lock (lockObj)
            {
                pool.Clear();
            }
        }

        public static string GetStatus()
        {
            return string.Format("ByteArrayPool: Rented {0}, Returned {1}, Hits {2}, Reused {3:F2} MB, Alloc {4:F2} MB",
                TotalRented, TotalReturned, PoolHits, TotalBytesReused / (1024.0 * 1024.0), TotalBytesAllocated / (1024.0 * 1024.0));
        }
    }
}
