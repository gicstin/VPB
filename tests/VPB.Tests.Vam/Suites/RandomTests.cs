using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class RandomTests
    {
        private const int Samples = 200000;

        private readonly ITestOutputHelper _out;
        public RandomTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        [Theory]
        [InlineData(0, 2)]
        [InlineData(0, 10)]
        [InlineData(-5, 5)]
        [InlineData(100, 101)]
        public void NextStaysInsideTheHalfOpenRange(int min, int maxExclusive)
        {
            for (int i = 0; i < Samples; i++)
            {
                int v = VpbRandom.Next(min, maxExclusive);
                if (v < min || v >= maxExclusive)
                    Assert.True(false, "VpbRandom.Next(" + min + ", " + maxExclusive + ") returned " + v +
                                       " on iteration " + i + ". An out-of-range draw indexes past the end of a " +
                                       "result list and throws somewhere unrelated.");
            }
        }

        [Fact]
        public void NextWithAnEmptyRangeIsStable()
        {
            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal(7, VpbRandom.Next(7, 7));
                Assert.Equal(0, VpbRandom.Next(0));
            }
        }

        [Fact]
        public void SingleArgumentNextStaysBelowTheBound()
        {
            for (int i = 0; i < Samples; i++)
            {
                int v = VpbRandom.Next(4);
                Assert.InRange(v, 0, 3);
            }
        }

        [Fact]
        public void EveryValueInASmallRangeIsReachable()
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000 && seen.Count < 6; i++)
                seen.Add(VpbRandom.Next(0, 6));

            Assert.Equal(6, seen.Count);
        }

        [Fact]
        public void SmallRangeIsNotVisiblyBiased()
        {
            const int buckets = 8;
            var counts = new int[buckets];
            for (int i = 0; i < Samples; i++) counts[VpbRandom.Next(0, buckets)]++;

            double expected = (double)Samples / buckets;
            for (int b = 0; b < buckets; b++)
            {
                double drift = Math.Abs(counts[b] - expected) / expected;
                _out.WriteLine("bucket " + b + ": " + counts[b] + " (drift " + drift.ToString("0.000") + ")");
                Assert.True(drift < 0.05,
                    "Bucket " + b + " drifted " + drift.ToString("0.000") + " from uniform. A modulo-biased " +
                    "generator makes the random-pick feature quietly favour the first few entries.");
            }
        }

        [Fact]
        public void FloatAndDoubleStayInTheUnitInterval()
        {
            for (int i = 0; i < Samples; i++)
            {
                float f = VpbRandom.NextFloat();
                Assert.True(f >= 0f && f < 1f, "NextFloat returned " + f);

                double d = VpbRandom.NextDouble();
                Assert.True(d >= 0.0 && d < 1.0, "NextDouble returned " + d);
            }
        }

        [Fact]
        public void BoolIsNotStuckOnOneValue()
        {
            int trues = 0;
            for (int i = 0; i < Samples; i++) if (VpbRandom.NextBool()) trues++;

            double ratio = (double)trues / Samples;
            _out.WriteLine("true ratio: " + ratio.ToString("0.0000"));
            Assert.True(Math.Abs(ratio - 0.5) < 0.01, "NextBool true ratio was " + ratio);
        }

        [Fact]
        public void ShuffleKeepsEveryElementExactlyOnce()
        {
            for (int trial = 0; trial < 200; trial++)
            {
                var list = new List<int>();
                for (int i = 0; i < 50; i++) list.Add(i);

                VpbRandom.Shuffle(list);

                Assert.Equal(50, list.Count);
                var seen = new HashSet<int>(list);
                Assert.Equal(50, seen.Count);
                for (int i = 0; i < 50; i++) Assert.Contains(i, seen);
            }
        }

        [Fact]
        public void ShuffleActuallyReorders()
        {
            var list = new List<int>();
            for (int i = 0; i < 64; i++) list.Add(i);

            int identical = 0;
            for (int trial = 0; trial < 50; trial++)
            {
                var copy = new List<int>(list);
                VpbRandom.Shuffle(copy);
                if (copy[0] == 0 && copy[1] == 1 && copy[2] == 2) identical++;
            }

            Assert.True(identical < 5,
                "Shuffle left the first three elements in place " + identical + "/50 times. A Shuffle that " +
                "silently no-ops looks fine until someone notices the random order never changes.");
        }

        [Fact]
        public void ShuffleHandlesDegenerateInput()
        {
            VpbRandom.Shuffle((List<int>)null);
            VpbRandom.Shuffle((int[])null);
            VpbRandom.Shuffle(new List<int>());
            VpbRandom.Shuffle(new int[0]);

            var one = new List<int> { 42 };
            VpbRandom.Shuffle(one);
            Assert.Equal(new[] { 42 }, one.ToArray());
        }

        [Fact]
        public void ArrayShuffleIsAPermutationToo()
        {
            var array = new int[128];
            for (int i = 0; i < array.Length; i++) array[i] = i;

            VpbRandom.Shuffle(array);

            var seen = new HashSet<int>(array);
            Assert.Equal(array.Length, seen.Count);
        }

        [Fact]
        public void SeparateThreadsDoNotProduceTheSameSequence()
        {
            uint[] first = null;
            uint[] second = null;

            var a = new Thread(() => { VpbRandom.ReseedThisThread(); first = Draw(16); });
            var b = new Thread(() => { VpbRandom.ReseedThisThread(); second = Draw(16); });

            a.Start(); b.Start();
            a.Join(); b.Join();

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.False(SequencesMatch(first, second),
                "Two threads produced an identical draw sequence. VpbRandom state is [ThreadStatic]; identical " +
                "sequences mean the per-thread seed is not actually varying, so parallel picks correlate.");
        }

        private static uint[] Draw(int n)
        {
            var values = new uint[n];
            for (int i = 0; i < n; i++) values[i] = VpbRandom.NextUInt();
            return values;
        }

        private static bool SequencesMatch(uint[] a, uint[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        [Fact]
        public void ReseedProducesADifferentStream()
        {
            uint[] before = Draw(16);
            VpbRandom.ReseedThisThread();
            uint[] after = Draw(16);

            Assert.False(SequencesMatch(before, after));
        }
    }
}
