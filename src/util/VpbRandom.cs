using System;
using System.Collections.Generic;
using System.Threading;

namespace VPB
{
    public static class VpbRandom
    {
        [ThreadStatic] private static uint s_S0;
        [ThreadStatic] private static uint s_S1;
        [ThreadStatic] private static uint s_S2;
        [ThreadStatic] private static uint s_S3;
        [ThreadStatic] private static bool s_Seeded;

        private static int s_StreamCounter;

        public static void ReseedThisThread()
        {
            s_Seeded = false;
        }

        private static void Seed()
        {
            uint mix;
            unchecked
            {
                int stream = Interlocked.Increment(ref s_StreamCounter);
                long ticks = DateTime.UtcNow.Ticks;
                long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
                int guid = Guid.NewGuid().GetHashCode();
                int thread = Thread.CurrentThread.ManagedThreadId;

                mix = (uint)guid;
                mix ^= (uint)ticks;
                mix ^= (uint)(ticks >> 32) * 2654435761u;
                mix ^= (uint)stamp * 2246822519u;
                mix ^= (uint)(stamp >> 32);
                mix ^= (uint)thread * 3266489917u;
                mix ^= (uint)stream * 668265263u;
            }

            uint x = mix;
            s_S0 = SplitMix32(ref x);
            s_S1 = SplitMix32(ref x);
            s_S2 = SplitMix32(ref x);
            s_S3 = SplitMix32(ref x);
            if ((s_S0 | s_S1 | s_S2 | s_S3) == 0u) s_S3 = 0x9E3779B9u;
            s_Seeded = true;
        }

        private static uint SplitMix32(ref uint x)
        {
            unchecked
            {
                x += 0x9E3779B9u;
                uint z = x;
                z = (z ^ (z >> 16)) * 0x21F0AAADu;
                z = (z ^ (z >> 15)) * 0x735A2D97u;
                return z ^ (z >> 15);
            }
        }

        public static uint NextUInt()
        {
            if (!s_Seeded) Seed();
            unchecked
            {
                uint s1 = s_S1;
                uint result = Rotl(s1 * 5u, 7) * 9u;
                uint t = s1 << 9;

                s_S2 ^= s_S0;
                s_S3 ^= s1;
                s_S1 = s1 ^ s_S2;
                s_S0 ^= s_S3;
                s_S2 ^= t;
                s_S3 = Rotl(s_S3, 11);

                return result;
            }
        }

        private static uint Rotl(uint x, int k)
        {
            unchecked { return (x << k) | (x >> (32 - k)); }
        }

        public static int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)Bounded((uint)maxExclusive);
        }

        public static int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            long span = (long)maxExclusive - minInclusive;
            if (span > uint.MaxValue) span = uint.MaxValue;
            return minInclusive + (int)Bounded((uint)span);
        }

        private static uint Bounded(uint range)
        {
            unchecked
            {
                ulong m = (ulong)NextUInt() * range;
                uint low = (uint)m;
                if (low < range)
                {
                    uint threshold = (0u - range) % range;
                    while (low < threshold)
                    {
                        m = (ulong)NextUInt() * range;
                        low = (uint)m;
                    }
                }
                return (uint)(m >> 32);
            }
        }

        public static float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public static double NextDouble()
        {
            return (NextUInt() >> 8) * (1.0 / 16777216.0);
        }

        public static bool NextBool()
        {
            return (NextUInt() & 1u) != 0u;
        }

        public static void Shuffle<T>(List<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)Bounded((uint)(i + 1));
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        public static void Shuffle<T>(T[] array)
        {
            if (array == null) return;
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = (int)Bounded((uint)(i + 1));
                T tmp = array[i];
                array[i] = array[j];
                array[j] = tmp;
            }
        }
    }
}
